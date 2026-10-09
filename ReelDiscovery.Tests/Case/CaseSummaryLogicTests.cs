using System.Globalization;
using ReelDiscovery.Helpers;
using ReelDiscovery.Models;
using ReelDiscovery.Models.Case;
using ReelDiscovery.Models.Corpus;
using ReelDiscovery.Models.Discovery;
using ReelDiscovery.Services.Case;
using ReelDiscovery.Services.Discovery;
using ReelDiscovery.Tests.Corpus;
using ReelDiscovery.Tests.Discovery;
using ReelDiscovery.Tests.Evidence;
using ReelDiscovery.UserControls;

namespace ReelDiscovery.Tests.Case;

// Covers the pure StepCaseSummary line helpers and (08-18) the Discovery documents row on a step that is never shown.
public class CaseSummaryLogicTests
{
    [Theory]
    [InlineData(CoreDocumentType.Complaint, "Complaint")]
    [InlineData(CoreDocumentType.Petition, "Petition")]
    [InlineData(CoreDocumentType.InvestigationMemo, "Investigation Memo")]
    public void DocumentTypeLabel_NamesEveryType(CoreDocumentType type, string expected)
    {
        Assert.Equal(expected, StepCaseSummary.DocumentTypeLabel(type));
    }

    [Fact]
    public void CoreDocumentLine_CountsSectionsAndNumberedParagraphs()
    {
        var doc = CaseTestData.SampleComplaintDraft().CoreDocument!;
        var sections = doc.Sections.Count;
        var paragraphs = CoreDocumentAssembler.CountNumberedParagraphs(doc);

        var expected = $"Complaint · {CaseGuideUiText.Plural(sections, "section", "sections")} · {CaseGuideUiText.Plural(paragraphs, "paragraph", "paragraphs")}";
        Assert.Equal(expected, StepCaseSummary.CoreDocumentLine(doc));
        Assert.True(paragraphs > 0);
    }

    [Fact]
    public void CastLine_CountsKeyBackgroundAndOrganizations()
    {
        var guide = CaseTestData.SampleGuidedDraft().Guide!;
        Assert.Equal("7 key and 2 background characters · 3 organizations", StepCaseSummary.CastLine(guide));
    }

    [Fact]
    public void FactsLine_UsesFirstAndLastFactDates()
    {
        var guide = CaseTestData.SampleGuidedDraft().Guide!;
        Assert.Equal("6 facts · Jan 15, 2024 to Jun 20, 2024", StepCaseSummary.FactsLine(guide));
    }

    [Fact]
    public void FactsLine_NoFacts_ShowsCountOnly()
    {
        Assert.Equal("0 facts", StepCaseSummary.FactsLine(new CaseGuide()));
    }

    [Fact]
    public void ValuesLine_SaysWhereValuesLive()
    {
        var guide = CaseTestData.SampleGuidedDraft().Guide!;
        Assert.Equal("4 values · saved in case.json", StepCaseSummary.ValuesLine(guide));
    }

    [Theory]
    [InlineData(0, 0, "No problems found")]
    [InlineData(1, 2, "1 error, 2 warnings")]
    [InlineData(0, 1, "0 errors, 1 warning")]
    public void ChecksLine_SummarizesFindings(int errors, int warnings, string expected)
    {
        Assert.Equal(expected, StepCaseSummary.ChecksLine(errors, warnings));
    }

    [Fact]
    public void ExportedAt_MissingFile_IsNull()
    {
        var folder = Path.Combine(Path.GetTempPath(), "rd-" + Guid.NewGuid().ToString("N"));
        Assert.Null(StepCaseSummary.ExportedAt(folder, "missing.docx"));
        Assert.Null(StepCaseSummary.ExportedAt(string.Empty, "missing.docx"));
    }

    [Fact]
    public void ExportedAt_ExistingFile_IsItsLastWriteTime()
    {
        var folder = Path.Combine(Path.GetTempPath(), "rd-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var path = Path.Combine(folder, "export.docx");
            File.WriteAllBytes(path, new byte[] { 1, 2, 3 });
            var stamp = new DateTime(2024, 3, 18, 14, 5, 0, DateTimeKind.Local);
            File.SetLastWriteTime(path, stamp);

            Assert.Equal(stamp, StepCaseSummary.ExportedAt(folder, "export.docx"));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    // ----- Corpus rows (04-15) -----

    private static readonly Guid CurrentPlan = Guid.NewGuid();

    private static CorpusState CorpusWith(params WrittenCorpusFile[] files) => new()
    {
        PlanId = CurrentPlan,
        WrittenFiles = files.ToList()
    };

    private static WrittenCorpusFile Written(CorpusFileKind kind, Guid planId) => new()
    {
        Kind = kind,
        FileName = "ignored-by-the-summary.csv",
        PlanId = planId,
        WrittenUtc = new DateTime(2024, 3, 18, 14, 5, 0, DateTimeKind.Utc)
    };

    [Fact]
    public void FileStatusFor_NoCorpus_IsNotWritten()
    {
        Assert.Equal(CorpusFileStatus.NotWritten, StepCaseSummary.FileStatusFor(null, CorpusFileKind.AnswerKeyCsv, fileExists: true));
    }

    [Fact]
    public void FileStatusFor_NoEntryForKind_IsNotWritten()
    {
        var corpus = CorpusWith(Written(CorpusFileKind.AnswerKeyXlsx, CurrentPlan));
        Assert.Equal(CorpusFileStatus.NotWritten, StepCaseSummary.FileStatusFor(corpus, CorpusFileKind.AnswerKeyCsv, fileExists: true));
    }

    [Fact]
    public void FileStatusFor_EntryForOldPlan_IsEarlierPlan()
    {
        var corpus = CorpusWith(Written(CorpusFileKind.ReviewProtocol, Guid.NewGuid()));
        Assert.Equal(CorpusFileStatus.EarlierPlan, StepCaseSummary.FileStatusFor(corpus, CorpusFileKind.ReviewProtocol, fileExists: true));
    }

    [Fact]
    public void FileStatusFor_EntryForCurrentPlanWithFile_IsWritten()
    {
        var corpus = CorpusWith(Written(CorpusFileKind.AnswerKeyCsv, CurrentPlan));
        Assert.Equal(CorpusFileStatus.Written, StepCaseSummary.FileStatusFor(corpus, CorpusFileKind.AnswerKeyCsv, fileExists: true));
    }

    [Fact]
    public void FileStatusFor_EntryButFileMissing_IsNotWritten()
    {
        var corpus = CorpusWith(Written(CorpusFileKind.AnswerKeyCsv, CurrentPlan));
        Assert.Equal(CorpusFileStatus.NotWritten, StepCaseSummary.FileStatusFor(corpus, CorpusFileKind.AnswerKeyCsv, fileExists: false));
    }

    [Fact]
    public void CombinedStatus_WeakestStateWins()
    {
        Assert.Equal(CorpusFileStatus.Written, StepCaseSummary.CombinedStatus(new[] { CorpusFileStatus.Written, CorpusFileStatus.Written }));
        Assert.Equal(CorpusFileStatus.EarlierPlan, StepCaseSummary.CombinedStatus(new[] { CorpusFileStatus.Written, CorpusFileStatus.EarlierPlan }));
        Assert.Equal(CorpusFileStatus.NotWritten, StepCaseSummary.CombinedStatus(new[] { CorpusFileStatus.EarlierPlan, CorpusFileStatus.NotWritten }));
    }

    [Theory]
    [InlineData(CorpusFileStatus.Written, "Success")]
    [InlineData(CorpusFileStatus.NotWritten, "BorderStrong")]
    [InlineData(CorpusFileStatus.EarlierPlan, "Warning")]
    [InlineData(CorpusFileStatus.Failed, "Danger")]
    public void CorpusDotState_FollowsUiSpec(CorpusFileStatus status, string expected)
    {
        Assert.Equal(expected, StepCaseSummary.CorpusDotState(status));
    }

    [Theory]
    [InlineData(false, false, false, "BorderStrong")]
    [InlineData(true, false, false, "BorderStrong")]
    [InlineData(true, true, false, "Success")]
    [InlineData(true, true, true, "Warning")]
    public void PlanDotState_StaleWinsOverApproval(bool hasPlan, bool approved, bool stale, string expected)
    {
        Assert.Equal(expected, StepCaseSummary.PlanDotState(hasPlan, approved, stale));
    }

    [Fact]
    public void WrittenLocal_IsNewestEntryInLocalTime()
    {
        var corpus = CorpusWith(Written(CorpusFileKind.AnswerKeyCsv, CurrentPlan));
        var expected = new DateTime(2024, 3, 18, 14, 5, 0, DateTimeKind.Utc).ToLocalTime();
        Assert.Equal(expected, StepCaseSummary.WrittenLocal(corpus, CorpusFileKind.AnswerKeyCsv, CorpusFileKind.AnswerKeyXlsx));
        Assert.Null(StepCaseSummary.WrittenLocal(corpus, CorpusFileKind.ReviewProtocol));
    }

    [Fact]
    public void PlanRowStale_DismissedHashCountsAsCurrent()
    {
        var draft = CorpusTestData.RichDraft();
        draft.Corpus = new CorpusState
        {
            PlanId = CurrentPlan,
            PlanGuideHash = draft.Guide!.ComputeHash(),
            Approved = true
        };
        Assert.False(StepCaseSummary.PlanRowStale(draft));

        // Guide edited after planning: the plan is out of date
        draft.Guide.Facts[0].Title += " (revised)";
        Assert.True(StepCaseSummary.PlanRowStale(draft));

        // Keep This Plan for the edited guide: the row counts the plan as current again
        draft.Corpus.DismissedStaleGuideHash = draft.Guide.ComputeHash();
        Assert.False(StepCaseSummary.PlanRowStale(draft));
    }

    // ----- Discovery documents row (08-UI-SPEC S4) -----

    internal static readonly DateTime DiscoveryGeneratedUtc = new(2025, 6, 2, 12, 0, 0, DateTimeKind.Utc);

    internal static string DiscoveryDateText =>
        DiscoveryGeneratedUtc.ToLocalTime().ToString("MMM d, yyyy", CultureInfo.GetCultureInfo("en-US"));

    /// <summary>
    /// A discovery document as a finished run leaves it: content, stamped hashes, file saved and the .docx on disk.
    /// </summary>
    internal static DiscoveryDocument SeedDiscoveryDocument(CaseDraft draft, string folder, DiscoveryKind kind, string key,
        string witnessId = "", bool writeFile = true, decimal cost = 0.50m)
    {
        var doc = new DiscoveryDocument
        {
            Key = key,
            Kind = kind,
            WitnessId = witnessId,
            FileName = key + ".docx",
            Status = DiscoveryStatus.Generated,
            FileSaved = true,
            RequestCount = kind == DiscoveryKind.Deposition ? 0 : 12,
            TestimonyPages = kind == DiscoveryKind.Deposition ? 25 : 0,
            CostUsd = cost,
            GeneratedUtc = DiscoveryGeneratedUtc
        };
        if (kind == DiscoveryKind.Deposition)
            doc.Deposition = new DepositionContent { WitnessId = witnessId, WitnessName = "Witness " + key, Date = "2025-06-01" };
        else
            doc.Written = new WrittenDiscoveryContent();
        DiscoveryStaleness.Stamp(doc, draft, null);
        if (writeFile)
        {
            Directory.CreateDirectory(DiscoveryStore.FolderPath(folder));
            File.WriteAllBytes(Path.Combine(DiscoveryStore.FolderPath(folder), doc.FileName), new byte[] { 1 });
        }
        return doc;
    }

    /// <summary>
    /// One clean set, one with warnings and one failed: "2 documents · ... · 1 with warnings · 1 failed".
    /// </summary>
    internal static void SeedDiscoveryWarningsAndFailed(CaseDraft draft, string folder)
    {
        var set = new DiscoveryDocumentSet { CaseId = draft.CaseId };
        set.Upsert(SeedDiscoveryDocument(draft, folder, DiscoveryKind.RequestsForProduction, "RFP"));
        var rog = SeedDiscoveryDocument(draft, folder, DiscoveryKind.Interrogatories, "ROG");
        rog.Status = DiscoveryStatus.GeneratedWithWarnings;
        set.Upsert(rog);
        var rfa = SeedDiscoveryDocument(draft, folder, DiscoveryKind.RequestsForAdmission, "RFA", writeFile: false);
        rfa.Written = null;
        rfa.Status = DiscoveryStatus.Failed;
        rfa.FailureReason = "Timed out";
        set.Upsert(rfa);
        DiscoveryStore.SaveAsync(folder, set).GetAwaiter().GetResult();
    }

    /// <summary>
    /// Every problem clause: warnings, out of date, not saved, file missing and failed, plus one clean deposition.
    /// </summary>
    internal static void SeedDiscoveryEveryProblem(CaseDraft draft, string folder)
    {
        var witnesses = DiscoveryTestData.WitnessIds(draft);
        var set = new DiscoveryDocumentSet { CaseId = draft.CaseId };
        set.Upsert(SeedDiscoveryDocument(draft, folder, DiscoveryKind.Deposition, "DEP-" + witnesses[0], witnesses[0]));
        var notSaved = SeedDiscoveryDocument(draft, folder, DiscoveryKind.Deposition, "DEP-" + witnesses[1], witnesses[1],
            writeFile: false);
        notSaved.FileSaved = false;
        set.Upsert(notSaved);
        set.Upsert(SeedDiscoveryDocument(draft, folder, DiscoveryKind.Deposition, "DEP-" + witnesses[2], witnesses[2],
            writeFile: false));
        var rfp = SeedDiscoveryDocument(draft, folder, DiscoveryKind.RequestsForProduction, "RFP");
        rfp.Status = DiscoveryStatus.GeneratedWithWarnings;
        set.Upsert(rfp);
        var rog = SeedDiscoveryDocument(draft, folder, DiscoveryKind.Interrogatories, "ROG");
        rog.GuideHash = "guide-edited-since";
        set.Upsert(rog);
        var rfa = SeedDiscoveryDocument(draft, folder, DiscoveryKind.RequestsForAdmission, "RFA", writeFile: false);
        rfa.Written = null;
        rfa.Status = DiscoveryStatus.Failed;
        rfa.FailureReason = "Timed out";
        set.Upsert(rfa);
        DiscoveryStore.SaveAsync(folder, set).GetAwaiter().GetResult();
    }

    private sealed class DiscoveryStepFixture : IDisposable
    {
        private readonly string _root;

        public DiscoveryStepFixture()
        {
            _root = Path.Combine(Path.GetTempPath(), "rd-summary-discovery-" + Guid.NewGuid().ToString("N"));
            var store = new CaseStore(Path.Combine(_root, "Cases"), Path.Combine(_root, "AppData"));
            var draft = DiscoveryTestData.LitigationDraft();
            DiscoveryStepModel.ApplyDefaults(draft);
            draft.AdvanceStage(CaseStage.Summary);
            Folder = store.EnsureFolder(draft);
            Assert.True(store.SaveNow(draft), store.LastSaveError);
            Draft = draft;
            State = new WizardState { Case = draft, CaseStore = store };
        }

        public WizardState State { get; }
        public CaseDraft Draft { get; }
        public string Folder { get; }

        public void Dispose()
        {
            try
            {
                Directory.Delete(_root, recursive: true);
            }
            catch (IOException)
            {
                // A file handle may linger briefly; the temp folder is harmless
            }
        }
    }

    private static StepCaseSummary CreateStep(DiscoveryStepFixture fixture, List<string>? opened = null)
    {
        var step = new StepCaseSummary();
        step.OpenFile = path => opened?.Add(path);
        step.OpenFolder = path => opened?.Add(path);
        step.LoadForRender(fixture.State);
        return step;
    }

    [Fact]
    public void DiscoveryRow_NoSidecar_NotGenerated()
    {
        using var fixture = new DiscoveryStepFixture();

        GenerateCorpusStepTests.RunSta(() =>
        {
            using var step = CreateStep(fixture);

            Assert.Equal(DiscoveryUiText.SummaryTitle, step.DiscoveryLine1.Text);
            Assert.Equal(DiscoveryUiText.SummaryLine2, step.DiscoveryLine2.Text);
            Assert.Equal("Not generated", step.DiscoveryLine3.Text);
            Assert.Equal(Theme.BorderStrong, step.DiscoveryDotColor);
            Assert.Equal(Theme.TextMuted, step.DiscoveryLine3.ForeColor);
            Assert.False(step.OpenDiscoveryButton.Visible);
            Assert.True(step.DiscoveryDocumentsButton.Visible);
            Assert.Equal("Open Discovery Folder", step.OpenDiscoveryButton.Text);
            Assert.Equal("Discovery Documents", step.DiscoveryDocumentsButton.Text);
            Assert.True(step.DiscoveryLine3.AutoEllipsis);
        });
    }

    [Fact]
    public void DiscoveryRow_WarningsAndFailed_DangerWithTooltip()
    {
        using var fixture = new DiscoveryStepFixture();
        SeedDiscoveryWarningsAndFailed(fixture.Draft, fixture.Folder);

        GenerateCorpusStepTests.RunSta(() =>
        {
            using var step = CreateStep(fixture);

            Assert.Equal($"2 documents · last generated {DiscoveryDateText} · 1 with warnings · 1 failed", step.DiscoveryLine3.Text);
            Assert.Equal(Theme.Danger, step.DiscoveryLine3.ForeColor);
            Assert.Equal(Theme.Danger, step.DiscoveryDotColor);
            var tooltip = step.TooltipOf(step.DiscoveryLine3);
            Assert.StartsWith(step.DiscoveryLine3.Text, tooltip);
            Assert.Contains("Requests for Production (12): ", tooltip);
            Assert.Contains("Interrogatories (12): ", tooltip);
            Assert.Contains("Requests for Admission (12): ", tooltip);
            Assert.True(step.OpenDiscoveryButton.Visible);
            Assert.True(step.DiscoveryDocumentsButton.Visible);
        });
    }

    [Fact]
    public void DiscoveryRow_CostRowIncludesDiscoverySpend_WithoutCorpusPlan()
    {
        using var fixture = new DiscoveryStepFixture();
        SeedDiscoveryWarningsAndFailed(fixture.Draft, fixture.Folder);

        GenerateCorpusStepTests.RunSta(() =>
        {
            using var step = CreateStep(fixture);
            Assert.Equal("$1.50 spent · discovery documents $1.50", step.GenerationCostLine3.Text);
        });
    }

    [Fact]
    public void DiscoveryDocumentsButton_NavigatesThroughTheSeam()
    {
        using var fixture = new DiscoveryStepFixture();

        GenerateCorpusStepTests.RunSta(() =>
        {
            using var step = CreateStep(fixture);
            var calls = 0;
            step.NavigateToDiscoveryOverride = () => calls++;

            step.NavigateToDiscovery();

            Assert.Equal(1, calls);
        });
    }

    [Fact]
    public void OpenDiscoveryFolder_OpensTheCaseDiscoveryFolder()
    {
        using var fixture = new DiscoveryStepFixture();
        SeedDiscoveryWarningsAndFailed(fixture.Draft, fixture.Folder);

        GenerateCorpusStepTests.RunSta(() =>
        {
            var opened = new List<string>();
            using var step = CreateStep(fixture, opened);

            step.OpenDiscoveryFolder();

            Assert.Equal(new[] { Path.GetFullPath(DiscoveryStore.FolderPath(fixture.Folder)) }, opened);
        });
    }

    [Fact]
    public void OpenDiscoveryFolder_MissingFolder_OpensNothing()
    {
        using var fixture = new DiscoveryStepFixture();

        GenerateCorpusStepTests.RunSta(() =>
        {
            var opened = new List<string>();
            using var step = CreateStep(fixture, opened);

            step.OpenDiscoveryFolder();

            Assert.Empty(opened);
        });
    }

    [Fact]
    public void Source_DiscoveryGroupBetweenQualityChecksAndCaseGuide()
    {
        var source = File.ReadAllText(Path.Combine(FindRepoRoot(), "UserControls", "StepCaseSummary.cs"));
        var blind = source.IndexOf("(_rowBlindReview, \"\"),", StringComparison.Ordinal);
        var heading = source.IndexOf("(null, \"Discovery\"),", StringComparison.Ordinal);
        var row = source.IndexOf("(_rowDiscovery, \"\"),", StringComparison.Ordinal);
        var guide = source.IndexOf("(null, \"Case guide\"),", StringComparison.Ordinal);
        Assert.True(blind > 0 && blind < heading && heading < row && row < guide, "Discovery group order");
        Assert.Contains("NavigateToStep(typeof(StepDiscoveryDocuments))", source);
    }

    [Fact]
    public void Source_ExportAllNeverTouchesDiscovery()
    {
        var source = File.ReadAllText(Path.Combine(FindRepoRoot(), "UserControls", "StepCaseSummary.cs")).Replace("\r\n", "\n");
        var start = source.IndexOf("private async Task ExportAllAsync()", StringComparison.Ordinal);
        Assert.True(start > 0);
        var end = source.IndexOf("\n    }\n", start, StringComparison.Ordinal);
        Assert.True(end > start);
        Assert.DoesNotContain("Discovery", source[start..end]);

        // D-30: nothing about discovery documents enters the answer key or the review protocol
        foreach (var file in new[] { "AnswerKeyBuilder.cs", "ReviewProtocolBuilder.cs" })
        {
            var text = File.ReadAllText(Path.Combine(FindRepoRoot(), "Services", "Corpus", file));
            Assert.DoesNotContain("DiscoveryStore", text);
            Assert.DoesNotContain("DiscoveryDocument", text);
        }
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "ReelDiscovery.csproj")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Could not locate ReelDiscovery.csproj");
    }
}
