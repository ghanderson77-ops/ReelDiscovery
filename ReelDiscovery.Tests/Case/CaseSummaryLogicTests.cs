using ReelDiscovery.Models.Case;
using ReelDiscovery.Models.Corpus;
using ReelDiscovery.Services.Case;
using ReelDiscovery.Tests.Corpus;
using ReelDiscovery.UserControls;

namespace ReelDiscovery.Tests.Case;

// Covers only the pure StepCaseSummary line helpers; no control is instantiated.
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
}
