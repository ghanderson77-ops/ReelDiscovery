using ReelDiscovery.Helpers;
using ReelDiscovery.Models.Case;
using ReelDiscovery.Models.Corpus;
using ReelDiscovery.Services.Case;
using ReelDiscovery.Services.Corpus;
using ReelDiscovery.Tests.Evidence;

namespace ReelDiscovery.Tests.Corpus;

public class CorpusApprovalTests : IDisposable
{
    private const string FolderName = "Brightline v. Harmon";
    private readonly string _root;
    private readonly string _folder;

    public CorpusApprovalTests()
    {
        // Temp folder only: never a real case folder
        _root = Path.Combine(Path.GetTempPath(), "rd-approve-" + Guid.NewGuid().ToString("N"));
        _folder = Path.Combine(_root, FolderName);
        Directory.CreateDirectory(_folder);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private static (CaseDraft Draft, CorpusPlan Plan) Setup()
    {
        var draft = CorpusTestData.RichDraft();
        var plan = PlanFixtures.ValidPlan(draft);
        plan.PlanId = Guid.NewGuid();
        plan.CreatedUtc = new DateTime(2024, 9, 1, 12, 0, 0, DateTimeKind.Utc);
        draft.Corpus ??= new CorpusState();
        draft.Corpus.Settings = PlanFixtures.Clone(plan).Settings;
        draft.AdvanceStage(CaseStage.CorpusSettings);
        return (draft, plan);
    }

    private static void Attach(CaseDraft draft, CorpusPlan plan)
    {
        draft.Corpus!.Plan = plan;
        draft.Corpus.PlanId = plan.PlanId;
        draft.Corpus.PlanGuideHash = plan.GuideHash;
        draft.Corpus.PlanSettingsHash = plan.SettingsHash;
        draft.Corpus.PlanCreatedUtc = plan.CreatedUtc;
    }

    private static FileWriter FailingWriter(int failOn, Exception error)
    {
        var count = 0;
        return (path, bytes, ct) =>
        {
            count++;
            if (count == failOn)
                throw error;
            return Task.CompletedTask;
        };
    }

    // ---- Task 1: approval writes ----

    [Fact]
    public void BuildFiles_ReturnsThreeFilesInOrder()
    {
        var (draft, plan) = Setup();

        var files = CorpusApproval.BuildFiles(draft, plan, FolderName);

        Assert.Equal(3, files.Count);
        Assert.Equal(CorpusFileKind.AnswerKeyCsv, files[0].Kind);
        Assert.Equal("answer-key.csv", files[0].FileName);
        Assert.Equal(CorpusFileKind.AnswerKeyXlsx, files[1].Kind);
        Assert.Equal("answer-key.xlsx", files[1].FileName);
        Assert.Equal(CorpusFileKind.ReviewProtocol, files[2].Kind);
        Assert.Equal("Brightline v. Harmon - Review Protocol.docx", files[2].FileName);
        Assert.All(files, f => Assert.NotEmpty(f.Bytes));
    }

    [Fact]
    public async Task Approve_DefaultWriter_WritesFilesAndRecordsApproval()
    {
        var (draft, plan) = Setup();
        Attach(draft, plan);
        draft.Corpus!.DismissedStaleGuideHash = "old";

        var result = await CorpusApproval.ApproveAsync(_folder, draft, plan);

        Assert.True(result.Succeeded);
        Assert.True(File.Exists(Path.Combine(_folder, "answer-key.csv")));
        Assert.True(File.Exists(Path.Combine(_folder, "answer-key.xlsx")));
        Assert.True(File.Exists(Path.Combine(_folder, "Brightline v. Harmon - Review Protocol.docx")));
        Assert.True(draft.Corpus.Approved);
        Assert.NotNull(draft.Corpus.ApprovedUtc);
        Assert.Equal(3, draft.Corpus.WrittenFiles.Count);
        Assert.All(draft.Corpus.WrittenFiles, f => Assert.Equal(plan.PlanId, f.PlanId));
        Assert.Equal(string.Empty, draft.Corpus.DismissedStaleGuideHash);
        Assert.True(draft.Stage >= CaseStage.ReviewPlan);
    }

    [Fact]
    public async Task Approve_SecondFileFails_ApprovesNothing_StampsOnlyTheWrittenFile()
    {
        var (draft, plan) = Setup();
        Attach(draft, plan);

        var result = await CorpusApproval.ApproveAsync(_folder, draft, plan, FailingWriter(2, new IOException("Disk full")));

        Assert.False(result.Succeeded);
        Assert.Equal("answer-key.xlsx", result.FailedFileName);
        Assert.Equal("Disk full", result.Error);
        Assert.False(result.FileOpenElsewhere);
        Assert.False(draft.Corpus!.Approved);
        Assert.Null(draft.Corpus.ApprovedUtc);
        // WR-02: answer-key.csv was overwritten, so its stamp follows the plan it now holds
        var entry = Assert.Single(draft.Corpus.WrittenFiles);
        Assert.Equal(CorpusFileKind.AnswerKeyCsv, entry.Kind);
        Assert.Equal(plan.PlanId, entry.PlanId);
    }

    [Fact]
    public async Task Approve_PartialWriteAfterReplan_RestampsOnlyReplacedFiles()
    {
        // WR-02: Word holds the protocol open while re-approving a new plan
        var (draft, plan) = Setup();
        Attach(draft, plan);
        Assert.True((await CorpusApproval.ApproveAsync(_folder, draft, plan)).Succeeded);

        var next = PlanFixtures.Clone(plan);
        next.PlanId = Guid.NewGuid();
        Attach(draft, next);
        draft.Corpus!.Approved = false;
        var error = new IOException("The file is in use.", unchecked((int)0x80070020));
        var result = await CorpusApproval.ApproveAsync(_folder, draft, next, FailingWriter(3, error));

        Assert.False(result.Succeeded);
        Assert.False(draft.Corpus.Approved);
        Assert.Equal(3, draft.Corpus.WrittenFiles.Count);
        var byKind = draft.Corpus.WrittenFiles.ToDictionary(f => f.Kind);
        Assert.Equal(next.PlanId, byKind[CorpusFileKind.AnswerKeyCsv].PlanId);
        Assert.Equal(next.PlanId, byKind[CorpusFileKind.AnswerKeyXlsx].PlanId);
        Assert.Equal(plan.PlanId, byKind[CorpusFileKind.ReviewProtocol].PlanId);
    }

    [Fact]
    public async Task Approve_SharingViolation_ReportsFileOpenElsewhere()
    {
        var (draft, plan) = Setup();
        Attach(draft, plan);
        var error = new IOException("The file is in use.", unchecked((int)0x80070020));

        var result = await CorpusApproval.ApproveAsync(_folder, draft, plan, FailingWriter(1, error));

        Assert.False(result.Succeeded);
        Assert.Equal("answer-key.csv", result.FailedFileName);
        Assert.True(result.FileOpenElsewhere);
    }

    [Fact]
    public async Task Approve_AfterReplan_ReplacesWrittenFiles()
    {
        var (draft, plan) = Setup();
        Attach(draft, plan);
        Assert.True((await CorpusApproval.ApproveAsync(_folder, draft, plan)).Succeeded);

        var next = PlanFixtures.Clone(plan);
        next.PlanId = Guid.NewGuid();
        Attach(draft, next);
        draft.Corpus!.Approved = false;
        Assert.True((await CorpusApproval.ApproveAsync(_folder, draft, next)).Succeeded);

        Assert.Equal(3, draft.Corpus.WrittenFiles.Count);
        Assert.All(draft.Corpus.WrittenFiles, f => Assert.Equal(next.PlanId, f.PlanId));
    }

    // ---- Phase 10: approval through the answer-key funnel (D-02, D-08, D-10, D-12) ----

    private string CsvPath => Path.Combine(_folder, AnswerKeyCsvWriter.FileName);

    private string XlsxPath => Path.Combine(_folder, AnswerKeyXlsxWriter.FileName);

    private static string FirstRelevantEmail(CaseDraft draft, CorpusPlan plan) => AnswerKeyBuilder.Build(plan, draft.Guide!)
        .First(r => r.DocType == AnswerKeyBuilder.DocTypeEmail && r.Relevance == AnswerKeyBuilder.RelevantLabel)
        .DocId;

    private string XlsxRelevance(string docId) =>
        AnswerKeyReader.ParseXlsx(File.ReadAllBytes(XlsxPath)).Rows.Single(r => r.DocId == docId).Relevance;

    [Fact]
    public async Task ReApprove_SamePlanId_KeepsRelabel()
    {
        var (draft, plan) = Setup();
        Attach(draft, plan);
        Assert.True((await CorpusApproval.ApproveAsync(_folder, draft, plan)).Succeeded);
        var docId = FirstRelevantEmail(draft, plan);
        AnswerKeyRelabelLifecycleTests.RelabelCsvFile(CsvPath, docId, AnswerKeyBuilder.NotRelevantLabel);

        var result = await CorpusApproval.ApproveAsync(_folder, draft, plan);

        Assert.True(result.Succeeded);
        Assert.Equal(AnswerKeyBuilder.NotRelevantLabel, AnswerKeyRelabelLifecycleTests.CsvCellOf(CsvPath, docId, "Relevance"));
        Assert.Equal(AnswerKeyBuilder.NotRelevantLabel, XlsxRelevance(docId));
    }

    [Fact]
    public async Task ReApprove_WithOverlay_KeepsStatuses()
    {
        var (draft, plan) = Setup();
        Attach(draft, plan);
        var docId = FirstRelevantEmail(draft, plan);
        var overlay = new AnswerKeyOverlay();
        overlay.ByDocId[docId] = new AnswerKeyOverlayEntry { Status = AnswerKeyBuilder.StatusGenerated };

        var result = await CorpusApproval.ApproveAsync(_folder, draft, plan, overlay: overlay);

        Assert.True(result.Succeeded);
        Assert.Equal(AnswerKeyBuilder.StatusGenerated, AnswerKeyRelabelLifecycleTests.CsvCellOf(CsvPath, docId, "Status"));
    }

    [Fact]
    public async Task Approve_NewPlanId_PlainKey_PrunesOldRelabels()
    {
        var (draft, plan) = Setup();
        Attach(draft, plan);
        Assert.True((await CorpusApproval.ApproveAsync(_folder, draft, plan)).Succeeded);
        var docId = FirstRelevantEmail(draft, plan);
        AnswerKeyRelabelLifecycleTests.RelabelCsvFile(CsvPath, docId, AnswerKeyBuilder.NotRelevantLabel);
        Assert.True((await CorpusApproval.ApproveAsync(_folder, draft, plan)).Succeeded);
        Assert.Contains(new AnswerKeyRelabelStore(_folder).Load().Relabels, r => r.PlanId == plan.PlanId);

        var next = PlanFixtures.Clone(plan);
        next.PlanId = Guid.NewGuid();
        Attach(draft, next);
        draft.Corpus!.Approved = false;
        var result = await CorpusApproval.ApproveAsync(_folder, draft, next);

        Assert.True(result.Succeeded);
        Assert.Equal(AnswerKeyBuilder.RelevantLabel, AnswerKeyRelabelLifecycleTests.CsvCellOf(CsvPath, docId, "Relevance"));
        Assert.Equal(AnswerKeyBuilder.RelevantLabel, XlsxRelevance(docId));
        Assert.DoesNotContain(new AnswerKeyRelabelStore(_folder).Load().Relabels, r => r.PlanId == plan.PlanId);
    }

    [Fact]
    public async Task Approve_CsvWriteFails_StopsBeforeXlsx()
    {
        var (draft, plan) = Setup();
        Attach(draft, plan);
        var attempted = new List<string>();
        var failing = FailingWriter(1, new IOException("Disk full"));
        FileWriter recording = (path, bytes, ct) =>
        {
            attempted.Add(Path.GetFileName(path));
            return failing(path, bytes, ct);
        };

        var result = await CorpusApproval.ApproveAsync(_folder, draft, plan, recording);

        Assert.False(result.Succeeded);
        Assert.Equal("answer-key.csv", result.FailedFileName);
        Assert.Equal(new[] { "answer-key.csv" }, attempted);
        Assert.Empty(result.Files);
        Assert.False(draft.Corpus!.Approved);
        Assert.Null(draft.Corpus.ApprovedUtc);
    }

    [Fact]
    public async Task Approve_DamagedXlsx_BackupReported()
    {
        var (draft, plan) = Setup();
        Attach(draft, plan);
        Assert.True((await CorpusApproval.ApproveAsync(_folder, draft, plan)).Succeeded);
        File.WriteAllBytes(XlsxPath, new byte[] { 1, 2, 3, 4, 5 });

        var result = await CorpusApproval.ApproveAsync(_folder, draft, plan);

        Assert.True(result.Succeeded);
        Assert.Equal(string.Empty, result.FailedFileName);
        var backup = Assert.Single(result.Backups);
        Assert.Equal(AnswerKeyXlsxWriter.FileName, backup.FileName);
        Assert.True(File.Exists(Path.Combine(_folder, backup.BackupFileName)));
        Assert.Equal(3, result.Files.Count);
    }

    [Fact]
    public void FileLockDetector_SharingViolationHResult_True()
    {
        var error = new IOException("in use", unchecked((int)0x80070020));
        Assert.True(FileLockDetector.IsFileOpenElsewhere(error, Path.Combine(_folder, "x.csv")));
        var locked = new IOException("locked", unchecked((int)0x80070021));
        Assert.True(FileLockDetector.IsFileOpenElsewhere(locked, Path.Combine(_folder, "x.csv")));
    }

    [Fact]
    public void FileLockDetector_UnauthorizedOnMissingFile_False()
    {
        var path = Path.Combine(_folder, "missing.csv");
        Assert.False(FileLockDetector.IsFileOpenElsewhere(new UnauthorizedAccessException(), path));
    }

    [Fact]
    public void FileLockDetector_FileHeldWithShareNone_True()
    {
        var path = Path.Combine(_folder, "held.csv");
        File.WriteAllText(path, "a");
        using (new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            Assert.True(FileLockDetector.IsFileOpenElsewhere(new UnauthorizedAccessException(), path));
        }

        Assert.False(FileLockDetector.IsFileOpenElsewhere(new UnauthorizedAccessException(), path));
    }

    // ---- Task 2: apply new plan, staleness and keep-plan rules ----

    [Fact]
    public async Task ApplyNewPlan_SavesBeforeAssigning_AndResetsApproval()
    {
        var (draft, old) = Setup();
        Attach(draft, old);
        Assert.True((await CorpusApproval.ApproveAsync(_folder, draft, old)).Succeeded);
        draft.Corpus!.DismissedSettingsHash = "s";
        draft.Corpus.DismissedStaleGuideHash = "g";
        draft.Stage = CaseStage.CorpusSettings;

        var next = PlanFixtures.Clone(old);
        next.PlanId = Guid.NewGuid();
        next.CreatedUtc = new DateTime(2024, 10, 1, 8, 0, 0, DateTimeKind.Utc);
        next.GuideHash = "new-guide";
        next.SettingsHash = "new-settings";
        var savedWhileOld = false;
        PlanSaver saver = (folder, plan, ct) =>
        {
            savedWhileOld = draft.Corpus.PlanId == old.PlanId && ReferenceEquals(draft.Corpus.Plan, old);
            return Task.CompletedTask;
        };

        var result = await CorpusApproval.ApplyNewPlanAsync(_folder, draft, next, saver);

        Assert.True(result.Saved);
        Assert.True(savedWhileOld);
        Assert.Same(next, draft.Corpus.Plan);
        Assert.Equal(next.PlanId, draft.Corpus.PlanId);
        Assert.Equal("new-guide", draft.Corpus.PlanGuideHash);
        Assert.Equal("new-settings", draft.Corpus.PlanSettingsHash);
        Assert.Equal(next.CreatedUtc, draft.Corpus.PlanCreatedUtc);
        Assert.False(draft.Corpus.Approved);
        Assert.Null(draft.Corpus.ApprovedUtc);
        Assert.Equal(string.Empty, draft.Corpus.DismissedStaleGuideHash);
        Assert.Equal(string.Empty, draft.Corpus.DismissedSettingsHash);
        Assert.Equal(3, draft.Corpus.WrittenFiles.Count);
        Assert.All(draft.Corpus.WrittenFiles, f => Assert.Equal(old.PlanId, f.PlanId));
        Assert.Equal(CaseStage.ReviewPlan, draft.Stage);
    }

    [Fact]
    public async Task ApplyNewPlan_SaverFails_DraftUnchanged()
    {
        var (draft, old) = Setup();
        Attach(draft, old);
        Assert.True((await CorpusApproval.ApproveAsync(_folder, draft, old)).Succeeded);
        var before = draft.ToJson();

        var next = PlanFixtures.Clone(old);
        next.PlanId = Guid.NewGuid();
        PlanSaver saver = (folder, plan, ct) => throw new IOException("Disk full");

        var result = await CorpusApproval.ApplyNewPlanAsync(_folder, draft, next, saver);

        Assert.False(result.Saved);
        Assert.Equal("Disk full", result.Error);
        Assert.Equal(before, draft.ToJson());
        Assert.Same(old, draft.Corpus!.Plan);
        Assert.Equal(old.PlanId, draft.Corpus.PlanId);
        Assert.True(draft.Corpus.Approved);
    }

    [Fact]
    public async Task ApplyNewPlan_DefaultSaver_WritesPlanFile()
    {
        var (draft, plan) = Setup();

        var result = await CorpusApproval.ApplyNewPlanAsync(_folder, draft, plan);

        Assert.True(result.Saved);
        Assert.True(CorpusPlanStore.Load(_folder, plan.PlanId).Succeeded);
        Assert.Equal(plan.PlanId, draft.Corpus!.PlanId);
    }

    [Fact]
    public async Task IsGuideStale_ContentOnlyAndDismissible()
    {
        var (draft, plan) = Setup();
        await CorpusApproval.ApplyNewPlanAsync(_folder, draft, plan, (f, p, ct) => Task.CompletedTask);
        Assert.False(CorpusApproval.IsGuideStale(draft));

        var fact = draft.Guide!.Facts[0];
        fact.NeedsReview = !fact.NeedsReview;
        Assert.False(CorpusApproval.IsGuideStale(draft));

        fact.Title += " (revised)";
        Assert.True(CorpusApproval.IsGuideStale(draft));

        CorpusApproval.KeepStalePlan(draft);
        Assert.False(CorpusApproval.IsGuideStale(draft));

        fact.Title += " again";
        Assert.True(CorpusApproval.IsGuideStale(draft));
    }

    [Fact]
    public void IsGuideStale_NoPlan_False()
    {
        var (draft, _) = Setup();
        Assert.False(CorpusApproval.IsGuideStale(draft));
        Assert.False(CorpusApproval.IsSettingsChanged(draft));
    }

    [Fact]
    public async Task WithdrawApprovalIfStale_ApprovedAndStale_Withdraws()
    {
        var (draft, plan) = Setup();
        await CorpusApproval.ApplyNewPlanAsync(_folder, draft, plan, (f, p, ct) => Task.CompletedTask);
        draft.Corpus!.Approved = true;

        Assert.False(CorpusApproval.WithdrawApprovalIfStale(draft));
        Assert.True(draft.Corpus.Approved);

        draft.Guide!.Facts[0].Title += " (revised)";
        Assert.True(CorpusApproval.WithdrawApprovalIfStale(draft));
        Assert.False(draft.Corpus.Approved);
    }

    [Fact]
    public async Task WithdrawIfGuideChanged_ApprovedAndEdited_WithdrawsAndBumpsRevisionOnce()
    {
        var (draft, plan) = Setup();
        await CorpusApproval.ApplyNewPlanAsync(_folder, draft, plan, (f, p, ct) => Task.CompletedTask);
        draft.Corpus!.Approved = true;
        var revision = draft.Revision;

        // Unchanged guide: nothing to withdraw
        Assert.False(CorpusApproval.WithdrawIfGuideChanged(draft));
        Assert.True(draft.Corpus.Approved);
        Assert.Equal(revision, draft.Revision);

        draft.Guide!.Facts[0].Title += " (revised)";
        Assert.True(CorpusApproval.WithdrawIfGuideChanged(draft));
        Assert.False(draft.Corpus.Approved);
        Assert.Equal(revision + 1, draft.Revision);

        // Second call: already withdrawn, revision unchanged
        Assert.False(CorpusApproval.WithdrawIfGuideChanged(draft));
        Assert.Equal(revision + 1, draft.Revision);
    }

    [Fact]
    public async Task WithdrawIfGuideChanged_KeptHash_DoesNothing()
    {
        var (draft, plan) = Setup();
        await CorpusApproval.ApplyNewPlanAsync(_folder, draft, plan, (f, p, ct) => Task.CompletedTask);
        draft.Corpus!.Approved = true;
        draft.Guide!.Facts[0].Title += " (revised)";
        CorpusApproval.KeepStalePlan(draft);
        var revision = draft.Revision;

        Assert.False(CorpusApproval.WithdrawIfGuideChanged(draft));
        Assert.True(draft.Corpus.Approved);
        Assert.Equal(revision, draft.Revision);
    }

    [Fact]
    public async Task WithdrawIfGuideChanged_Unapproved_DoesNothing()
    {
        var (draft, plan) = Setup();
        await CorpusApproval.ApplyNewPlanAsync(_folder, draft, plan, (f, p, ct) => Task.CompletedTask);
        draft.Corpus!.Approved = false;
        draft.Guide!.Facts[0].Title += " (revised)";
        var revision = draft.Revision;

        Assert.False(CorpusApproval.WithdrawIfGuideChanged(draft));
        Assert.False(draft.Corpus.Approved);
        Assert.Equal(revision, draft.Revision);
    }

    [Fact]
    public async Task WidenActiveRanges_AfterPlanning_DoesNotStaleGuide()
    {
        // A1 / Pitfall 6: visiting Case Guide right after planning must not change the guide hash
        var (draft, plan) = Setup();
        await CorpusApproval.ApplyNewPlanAsync(_folder, draft, plan, (f, p, ct) => Task.CompletedTask);

        Assert.False(CaseGuideEditing.WidenActiveRanges(draft));
        Assert.False(CorpusApproval.IsGuideStale(draft));
    }

    [Fact]
    public async Task WithdrawApprovalIfStale_Dismissed_Unchanged()
    {
        var (draft, plan) = Setup();
        await CorpusApproval.ApplyNewPlanAsync(_folder, draft, plan, (f, p, ct) => Task.CompletedTask);
        draft.Corpus!.Approved = true;
        draft.Guide!.Facts[0].Title += " (revised)";
        CorpusApproval.KeepStalePlan(draft);

        Assert.False(CorpusApproval.WithdrawApprovalIfStale(draft));
        Assert.True(draft.Corpus.Approved);
    }

    [Fact]
    public async Task Approve_AfterKeepingStalePlan_StaysCurrent()
    {
        // Keep This Plan, then Approve Plan: the approved plan must not read as stale again on the next entry (D-25)
        var (draft, plan) = Setup();
        await CorpusApproval.ApplyNewPlanAsync(_folder, draft, plan, (f, p, ct) => Task.CompletedTask);
        draft.Guide!.Facts[0].Title += " (revised)";
        CorpusApproval.KeepStalePlan(draft);

        var result = await CorpusApproval.ApproveAsync(_folder, draft, plan, (path, bytes, ct) => Task.CompletedTask);

        Assert.True(result.Succeeded);
        Assert.True(draft.Corpus!.Approved);
        Assert.False(CorpusApproval.IsGuideStale(draft));
        Assert.False(CorpusApproval.WithdrawApprovalIfStale(draft));
        Assert.True(draft.Corpus.Approved);

        // A later guide change makes it stale again
        draft.Guide.Facts[0].Title += " again";
        Assert.True(CorpusApproval.IsGuideStale(draft));
    }

    [Fact]
    public async Task IsSettingsChanged_DetectsAndKeeps()
    {
        var (draft, plan) = Setup();
        await CorpusApproval.ApplyNewPlanAsync(_folder, draft, plan, (f, p, ct) => Task.CompletedTask);
        Assert.False(CorpusApproval.IsSettingsChanged(draft));

        draft.Corpus!.Settings.TotalEmails += 100;
        Assert.True(CorpusApproval.IsSettingsChanged(draft));

        CorpusApproval.KeepSettingsChange(draft);
        Assert.False(CorpusApproval.IsSettingsChanged(draft));

        draft.Corpus.Settings.TotalEmails += 100;
        Assert.True(CorpusApproval.IsSettingsChanged(draft));
    }
}
