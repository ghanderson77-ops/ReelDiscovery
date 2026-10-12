using ReelDiscovery.Helpers;
using ReelDiscovery.Models.Case;
using ReelDiscovery.Models.Corpus;
using ReelDiscovery.Services.Case;

namespace ReelDiscovery.Services.Corpus;

public delegate Task FileWriter(string path, byte[] bytes, CancellationToken ct);

public delegate Task PlanSaver(string caseFolder, CorpusPlan plan, CancellationToken ct);

public class ApprovalResult
{
    public bool Succeeded { get; set; }
    public List<WrittenCorpusFile> Files { get; set; } = new();
    public string FailedFileName { get; set; } = string.Empty;
    public string Error { get; set; } = string.Empty;
    public bool FileOpenElsewhere { get; set; }

    /// <summary>Damaged key files copied aside before approval rewrote them (D-10); a notice, not a failure.</summary>
    public List<AnswerKeyBackup> Backups { get; set; } = new();
}

public class PlanApplyResult
{
    public bool Saved { get; set; }
    public string Error { get; set; } = string.Empty;
}

/// <summary>
/// Review Plan state transitions (D-04, D-18, D-25, D-26) without UI: answer-key/protocol writes and approval.
/// </summary>
public static class CorpusApproval
{
    /// <summary>
    /// The three approval files in write order: answer-key.csv, answer-key.xlsx, '{folder} - Review Protocol.docx'.
    /// Pure: the approval write goes through AnswerKeyFiles, which also harvests relabels (D-02).
    /// </summary>
    public static List<(CorpusFileKind Kind, string FileName, byte[] Bytes)> BuildFiles(CaseDraft draft, CorpusPlan plan,
        string folderName, AnswerKeyOverlay? overlay = null, IReadOnlyDictionary<string, AnswerKeyRelabel>? relabels = null)
    {
        var guide = draft.Guide ?? new CaseGuide();
        var (csv, xlsx) = AnswerKeyFiles.BuildBytes(plan, guide, overlay, relabels);
        return new List<(CorpusFileKind Kind, string FileName, byte[] Bytes)>
        {
            (CorpusFileKind.AnswerKeyCsv, AnswerKeyCsvWriter.FileName, csv),
            (CorpusFileKind.AnswerKeyXlsx, AnswerKeyXlsxWriter.FileName, xlsx),
            (CorpusFileKind.ReviewProtocol, ReviewProtocolBuilder.FileNameFor(folderName),
                ReviewProtocolBuilder.Build(draft, plan.Protocol ?? new ReviewProtocolProse()))
        };
    }

    /// <summary>
    /// Writes the three files into the case folder, stopping at the first failure. Never touches the draft.
    /// </summary>
    public static async Task<ApprovalResult> WriteFilesAsync(string caseFolder, CaseDraft draft, CorpusPlan plan,
        FileWriter? writer = null, AnswerKeyOverlay? overlay = null, CancellationToken ct = default)
    {
        writer ??= CaseStore.WriteAtomicAsync;
        var folderName = Path.GetFileName(Path.TrimEndingDirectorySeparator(caseFolder));
        var guide = draft.Guide ?? new CaseGuide();
        var result = new ApprovalResult();

        // D-02/D-12: the key files go through the funnel (relabels of this PlanId kept; a new PlanId writes the plain key and
        // prunes the old plan's relabels); approval still stops at the first failure (D-26)
        var key = await AnswerKeyFiles.WriteAsync(caseFolder, plan, guide, overlay, writer, stopAtFirstFailure: true, ct);
        result.Files.AddRange(key.Files);
        result.Backups = key.Backups;
        if (!key.Succeeded)
        {
            result.Succeeded = false;
            result.FailedFileName = key.FailedFileName;
            result.Error = key.Error;
            result.FileOpenElsewhere = key.FileOpenElsewhere;
            return result;
        }

        ct.ThrowIfCancellationRequested();
        // Paths = case folder + constant names only; WrittenFiles from case.json never build paths (T-04-68)
        var protocolName = ReviewProtocolBuilder.FileNameFor(folderName);
        var path = Path.Combine(caseFolder, protocolName);
        try
        {
            await writer(path, ReviewProtocolBuilder.Build(draft, plan.Protocol ?? new ReviewProtocolProse()), ct);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            result.Succeeded = false;
            result.FailedFileName = protocolName;
            result.Error = ex.Message;
            result.FileOpenElsewhere = FileLockDetector.IsFileOpenElsewhere(ex, path);
            return result;
        }

        result.Files.Add(new WrittenCorpusFile
        {
            Kind = CorpusFileKind.ReviewProtocol,
            FileName = protocolName,
            PlanId = plan.PlanId,
            WrittenUtc = DateTime.UtcNow
        });
        result.Succeeded = true;
        return result;
    }

    /// <summary>
    /// Records approval after all three files were written; the written file list replaces the previous one.
    /// </summary>
    public static void RecordApproval(CaseDraft draft, CorpusPlan plan, IReadOnlyList<WrittenCorpusFile> files, DateTime utcNow)
    {
        draft.Corpus ??= new CorpusState();
        draft.Corpus.Approved = true;
        draft.Corpus.ApprovedUtc = utcNow;
        draft.Corpus.WrittenFiles = files.ToList();
        // Approving a plan the user kept against a changed guide accepts that guide: keep its hash dismissed, so the next
        // entry does not withdraw the new approval again (D-25). A plan built from the current guide needs no dismissal.
        var current = (draft.Guide ?? new CaseGuide()).ComputeHash();
        draft.Corpus.DismissedStaleGuideHash = (plan.GuideHash ?? string.Empty) == current ? string.Empty : current;
        // No-op once the plan was first saved (D-04 decision); kept so approval always lands at ReviewPlan
        draft.AdvanceStage(CaseStage.ReviewPlan);
    }

    /// <summary>
    /// Re-stamps the files a failed approval did overwrite with the plan they now hold, so the case summary never shows
    /// a file of the current plan as written for the earlier one (WR-02). Approval itself is not recorded (D-26).
    /// </summary>
    public static void RecordPartialWrites(CaseDraft draft, IReadOnlyList<WrittenCorpusFile> files)
    {
        if (files.Count == 0)
            return;

        draft.Corpus ??= new CorpusState();
        var corpus = draft.Corpus;
        corpus.WrittenFiles = corpus.WrittenFiles.Where(w => files.All(f => f.Kind != w.Kind)).Concat(files).ToList();
    }

    /// <summary>
    /// Writes the answer key and review protocol, then records approval only if every write succeeded (D-26, T-04-70).
    /// A partial write re-stamps only the files it replaced (WR-02). The caller saves case.json afterwards, also after a
    /// failure that wrote some files (result.Files is not empty).
    /// Re-approving the same PlanId keeps manual relabels and, with the run-state overlay the caller passes, the run
    /// statuses (D-12).
    /// </summary>
    public static async Task<ApprovalResult> ApproveAsync(string caseFolder, CaseDraft draft, CorpusPlan plan,
        FileWriter? writer = null, AnswerKeyOverlay? overlay = null, CancellationToken ct = default)
    {
        var result = await WriteFilesAsync(caseFolder, draft, plan, writer, overlay, ct);
        if (result.Succeeded)
            RecordApproval(draft, plan, result.Files, DateTime.UtcNow);
        else
            RecordPartialWrites(draft, result.Files);
        return result;
    }

    /// <summary>
    /// Saves a newly built plan to corpus-plan.json first, then points the draft at it and resets approval (D-18, T-04-69).
    /// A failed save leaves the draft (and any previous plan with its approval) untouched. A cancelled or failed planning
    /// run must not call this method, so the previous plan stays in place. The caller saves case.json afterwards.
    /// </summary>
    public static async Task<PlanApplyResult> ApplyNewPlanAsync(string caseFolder, CaseDraft draft, CorpusPlan plan,
        PlanSaver? saver = null, CancellationToken ct = default)
    {
        saver ??= CorpusPlanStore.SaveAsync;
        try
        {
            await saver(caseFolder, plan, ct);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new PlanApplyResult { Saved = false, Error = ex.Message };
        }

        // People the planner added under Who knew, so the guide matches the plan's GuideHash and the emails
        foreach (var (factId, ids) in plan.AddedKnowers ?? new())
        {
            var fact = draft.Guide?.Facts?.FirstOrDefault(f => f != null && f.Id == factId);
            if (fact != null)
                ThreadSkeletonBuilder.AddKnowers(fact, ids ?? new List<string>());
        }

        draft.Corpus ??= new CorpusState();
        var corpus = draft.Corpus;
        corpus.Plan = plan;
        corpus.PlanId = plan.PlanId;
        corpus.PlanGuideHash = plan.GuideHash;
        corpus.PlanSettingsHash = plan.SettingsHash;
        corpus.PlanCreatedUtc = plan.CreatedUtc;
        corpus.Approved = false;
        corpus.ApprovedUtc = null;
        corpus.DismissedStaleGuideHash = string.Empty;
        corpus.DismissedSettingsHash = string.Empty;
        // WrittenFiles kept: files from the previous plan now read as written for an earlier plan
        // D-04 decision: the stage advances as soon as a plan is first saved
        draft.AdvanceStage(CaseStage.ReviewPlan);
        return new PlanApplyResult { Saved = true };
    }

    /// <summary>
    /// True when the plan was built from a different guide (content-only hash) and that guide was not kept (D-25).
    /// </summary>
    public static bool IsGuideStale(CaseDraft draft)
    {
        var corpus = draft.Corpus;
        if (corpus == null || !corpus.HasPlan)
            return false;
        var current = (draft.Guide ?? new CaseGuide()).ComputeHash();
        return PlanGuideHash(corpus) != current && corpus.DismissedStaleGuideHash != current;
    }

    /// <summary>
    /// True when the settings changed since the plan was built and the change was not kept (D-26 settings banner).
    /// </summary>
    public static bool IsSettingsChanged(CaseDraft draft)
    {
        var corpus = draft.Corpus;
        if (corpus == null || !corpus.HasPlan)
            return false;
        var current = (corpus.Settings ?? new CorpusSettings()).ComputeHash();
        return PlanSettingsHash(corpus) != current && corpus.DismissedSettingsHash != current;
    }

    /// <summary>
    /// Withdraws approval while the guide is stale and not kept; returns true when approval was withdrawn.
    /// </summary>
    public static bool WithdrawApprovalIfStale(CaseDraft draft)
    {
        if (draft.Corpus == null || !draft.Corpus.Approved || !IsGuideStale(draft))
            return false;
        draft.Corpus.Approved = false;
        return true;
    }

    /// <summary>
    /// The one place a guide edit withdraws plan approval (W1): when the approved plan's guide is stale and not kept,
    /// approval is withdrawn and the draft revision is bumped so the next save persists it. Reverting the edit does not
    /// restore approval (D-25). Returns true when it withdrew.
    /// </summary>
    public static bool WithdrawIfGuideChanged(CaseDraft draft)
    {
        if (!WithdrawApprovalIfStale(draft))
            return false;
        draft.Revision++;
        return true;
    }

    /// <summary>
    /// Keep This Plan: dismisses the stale banner for the current guide hash only. Approval withdrawn for staleness stays
    /// withdrawn: approving again rewrites the protocol from the current guide (banner text says so, WR-11).
    /// </summary>
    public static void KeepStalePlan(CaseDraft draft)
    {
        draft.Corpus ??= new CorpusState();
        draft.Corpus.DismissedStaleGuideHash = (draft.Guide ?? new CaseGuide()).ComputeHash();
    }

    /// <summary>
    /// Dismisses the settings-changed banner for the current settings hash only.
    /// </summary>
    public static void KeepSettingsChange(CaseDraft draft)
    {
        draft.Corpus ??= new CorpusState();
        draft.Corpus.DismissedSettingsHash = (draft.Corpus.Settings ?? new CorpusSettings()).ComputeHash();
    }

    // The loaded plan's own stamp wins; the case.json copy covers a plan not yet loaded
    private static string PlanGuideHash(CorpusState corpus) => corpus.Plan?.GuideHash ?? corpus.PlanGuideHash;

    private static string PlanSettingsHash(CorpusState corpus) => corpus.Plan?.SettingsHash ?? corpus.PlanSettingsHash;
}
