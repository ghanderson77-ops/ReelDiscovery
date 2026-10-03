using ReelDiscovery.Helpers;
using ReelDiscovery.Models.Corpus;
using ReelDiscovery.Models.Evidence;
using ReelDiscovery.Services.Case;
using ReelDiscovery.Services.Corpus;
using ReelDiscovery.Services.Evidence;
using ReelDiscovery.Tests.Render;
using ReelDiscovery.UserControls;

namespace ReelDiscovery.Tests.Evidence;

/// <summary>
/// Case Summary actions for the generated corpus (05-16, UI-SPEC S3): Export Story So Far, Open in Word with a missing
/// story file, and Open Mailboxes Folder path verification. Not a render test: the step is never shown.
/// </summary>
public class CaseSummaryGenerationTests
{
    private static NarrativeLedger Ledger(CorpusPlan plan)
    {
        var threads = plan.Threads.Where(t => t.Relevance == ThreadRelevance.Relevant).OrderBy(t => t.Id, StringComparer.Ordinal).ToList();
        return new NarrativeLedger
        {
            Entries = new List<LedgerEntry>
            {
                new()
                {
                    Id = threads[0].Id + "-L01",
                    Date = "2024-03-04",
                    Kind = LedgerKind.Commitment,
                    Statement = "The glazing subcontractor committed to the revised quote.",
                    Value = "$18,425",
                    KnownByIds = new List<string> { "CH-001", "CH-002" },
                    SourceThreadId = threads[0].Id,
                    WaveIndex = 1
                },
                new()
                {
                    Id = threads[1].Id + "-L01",
                    Date = "2024-04-09",
                    Kind = LedgerKind.Meeting,
                    Statement = "The site meeting moved to the east entrance.",
                    SourceThreadId = threads[1].Id,
                    WaveIndex = 2
                }
            }
        };
    }

    private static void SeedGeneration(GenerateCorpusRenderTests.StepFixture fixture, string storyError = "")
    {
        fixture.Draft.Corpus!.Generation = new GenerationState
        {
            RunPlanId = fixture.Plan.PlanId,
            Ledger = Ledger(fixture.Plan),
            ThreadsTotal = 4,
            ThreadsWritten = 2,
            FilesWritten = 9,
            Mailboxes = 4,
            EmailsWritten = 5,
            StoryError = storyError
        };
    }

    private static string StoryName(string folder) => StorySoFarDocxBuilder.FileNameFor(Path.GetFileName(folder));

    private static StepCaseSummary CreateStep(GenerateCorpusRenderTests.StepFixture fixture, List<string> opened)
    {
        var step = new StepCaseSummary();
        step.OpenFile = opened.Add;
        step.OpenFolder = opened.Add;
        step.LoadForRender(fixture.State);
        return step;
    }

    [Fact]
    public void ExportStorySoFar_WritesDocx_StampsClearsErrorAndSaves()
    {
        using var fixture = new GenerateCorpusRenderTests.StepFixture();
        SeedGeneration(fixture, storyError: "locked: The file is open in Word");
        var opened = new List<string>();

        GenerateCorpusStepTests.RunSta(() =>
        {
            using var step = CreateStep(fixture, opened);
            Assert.True(step.ExportStoryButton.Visible);
            Assert.False(step.OpenStoryButton.Visible);
            Assert.Equal(Theme.Danger, step.StoryLine3.ForeColor);

            GenerateCorpusStepTests.Pump(step.ExportStoryAsync());

            var path = Path.Combine(fixture.Folder, StoryName(fixture.Folder));
            Assert.True(File.Exists(path), path);
            var stamp = fixture.Draft.Corpus!.WrittenFiles.Last(f => f.Kind == CorpusFileKind.StorySoFar);
            Assert.Equal(StoryName(fixture.Folder), stamp.FileName);
            Assert.Equal(fixture.Plan.PlanId, stamp.PlanId);
            Assert.Equal(string.Empty, fixture.Draft.Corpus.Generation!.StoryError);

            // Saved: case.json on disk carries the stamp
            var json = File.ReadAllText(Path.Combine(fixture.Folder, CaseStore.CaseFileName));
            Assert.Contains(StoryName(fixture.Folder), json);

            Assert.StartsWith("Written ", step.StoryLine3.Text);
            Assert.Equal(Theme.Success, step.StoryLine3.ForeColor);
            Assert.True(step.OpenStoryButton.Visible);
            Assert.Equal(Theme.Success, step.StatusLabel.ForeColor);
            Assert.True(step.CanMoveNext);
            Assert.Empty(opened);
        });
    }

    [Fact]
    public void OpenInWord_OpensTheStoryFile_AndAMissingFileShowsStoryMissingText()
    {
        using var fixture = new GenerateCorpusRenderTests.StepFixture();
        SeedGeneration(fixture);
        var name = StoryName(fixture.Folder);
        var path = Path.Combine(fixture.Folder, name);
        File.WriteAllBytes(path, new byte[] { 1, 2, 3 });
        fixture.Draft.Corpus!.WrittenFiles.Add(new WrittenCorpusFile
        {
            Kind = CorpusFileKind.StorySoFar,
            FileName = name,
            PlanId = fixture.Plan.PlanId,
            WrittenUtc = DateTime.UtcNow
        });
        var opened = new List<string>();

        GenerateCorpusStepTests.RunSta(() =>
        {
            using var step = CreateStep(fixture, opened);
            Assert.True(step.OpenStoryButton.Visible);

            step.OpenStory();
            Assert.Equal(new[] { Path.GetFullPath(path) }, opened);

            File.Delete(path);
            opened.Clear();
            step.OpenStory();

            Assert.Empty(opened);
            Assert.Equal(GenerationUiText.StoryMissingText(name), step.StatusLabel.Text);
            Assert.Equal(Theme.Danger, step.StatusLabel.ForeColor);
            // The row was refreshed: the file is gone
            Assert.False(step.OpenStoryButton.Visible);
            Assert.Equal(Theme.TextMuted, step.StoryLine3.ForeColor);
        });
    }

    [Fact]
    public void OpenMailboxesFolder_OpensOnlyTheCaseMailboxesFolder()
    {
        using var fixture = new GenerateCorpusRenderTests.StepFixture();
        SeedGeneration(fixture);
        var opened = new List<string>();
        var expected = Path.GetFullPath(Path.Combine(fixture.Folder, PlanNumbering.MailboxesFolder));

        GenerateCorpusStepTests.RunSta(() =>
        {
            // No Mailboxes folder yet: hidden, and nothing opens
            using (var step = CreateStep(fixture, opened))
            {
                Assert.False(step.OpenMailboxesButton.Visible);
                step.OpenMailboxesFolder();
                Assert.Empty(opened);
            }

            Directory.CreateDirectory(expected);
            using (var step = CreateStep(fixture, opened))
            {
                Assert.True(step.OpenMailboxesButton.Visible);
                Assert.False(step.GenerateEmailsButton.Visible);
                step.OpenMailboxesFolder();
                Assert.Equal(new[] { expected }, opened);
            }
        });
    }

    [Fact]
    public void MailboxesPath_ResolvesDirectlyUnderTheCaseFolder()
    {
        var folder = EvidenceTestData.TempCaseFolder();
        try
        {
            var expected = Path.GetFullPath(Path.Combine(folder, PlanNumbering.MailboxesFolder));
            Assert.Equal(expected, StepCaseSummary.MailboxesPath(folder));
            Assert.Equal(expected, StepCaseSummary.MailboxesPath(folder + Path.DirectorySeparatorChar));
            Assert.Null(StepCaseSummary.MailboxesPath(string.Empty));
            Assert.Null(StepCaseSummary.MailboxesPath("   "));
        }
        finally
        {
            EvidenceTestData.DeleteFolder(folder);
        }
    }

    [Fact]
    public void NotGenerated_ShowsGenerateEmails()
    {
        using var fixture = new GenerateCorpusRenderTests.StepFixture();
        var opened = new List<string>();

        GenerateCorpusStepTests.RunSta(() =>
        {
            using var step = CreateStep(fixture, opened);
            Assert.Equal("Not generated yet", step.MailboxesLine3.Text);
            Assert.Equal(Theme.BorderStrong, step.MailboxesDotColor);
            Assert.True(step.GenerateEmailsButton.Visible);
            Assert.False(step.OpenMailboxesButton.Visible);
            Assert.False(step.ExportStoryButton.Visible);
            Assert.False(step.OpenStoryButton.Visible);
        });
    }
}
