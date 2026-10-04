using ReelDiscovery.Models;
using ReelDiscovery.Models.Case;
using ReelDiscovery.Models.Corpus;
using ReelDiscovery.Models.Evidence;
using ReelDiscovery.Services.Case;
using ReelDiscovery.Services.Corpus;
using ReelDiscovery.Services.Evidence;
using ReelDiscovery.Tests.Case;
using ReelDiscovery.Tests.Corpus;
using ReelDiscovery.UserControls;

namespace ReelDiscovery.Tests.Render;

/// <summary>
/// Renders the Case Summary step at 144 DPI with a case folder under a temp root (UI-SPEC S2; Corpus group 04-UI-SPEC S3).
/// </summary>
[Trait("Category", "Render")]
[Collection(RenderCollection.Name)]
public class CaseSummaryRenderTests
{
    [Fact]
    public void Summary_BeforeExport()
    {
        RenderHarness.RunSta(() =>
        {
            foreach (var size in new[] { RenderHarness.Large, RenderHarness.WizardMin })
            {
                using var fixture = new SummaryFixture(CaseTestData.SampleGuidedDraft());
                var result = Render(fixture, "case-summary-before-export", size);

                AssertRendered(result);
            }
        });
    }

    [Fact]
    public void Summary_AfterExport()
    {
        RenderHarness.RunSta(() =>
        {
            using var fixture = new SummaryFixture(CaseTestData.SampleGuidedDraft());
            var folder = fixture.State.Case.FolderPath;
            foreach (var kind in new[] { CaseExportKind.CoreDocument, CaseExportKind.Cast, CaseExportKind.KeyFacts })
            {
                var path = Path.Combine(folder, CaseGuideDocxExporter.FileNameFor(kind, Path.GetFileName(folder)));
                File.WriteAllBytes(path, CaseGuideDocxExporter.Build(fixture.State.Case, kind));
            }

            var openButtons = 0;
            var exportLines = 0;
            var result = Render(fixture, "case-summary-after-export", RenderHarness.Large, control =>
            {
                openButtons = AllControls(control).OfType<Button>().Count(b => b.Text == "Open in Word" && b.Visible);
                exportLines = AllControls(control).OfType<Label>().Count(l => l.Text.StartsWith("Exported ", StringComparison.Ordinal) && l.Text.EndsWith(".docx", StringComparison.Ordinal));
            });

            AssertRendered(result);
            Assert.Equal(3, openButtons);
            Assert.Equal(3, exportLines);
        });
    }

    [Fact]
    public void Summary_WithErrors()
    {
        RenderHarness.RunSta(() =>
        {
            var draft = CaseTestData.SampleGuidedDraft();
            // Orphan link: a fact points to an issue that does not exist
            draft.Guide!.Facts[0].IssueIds.Add("ISS-99");
            using var fixture = new SummaryFixture(draft);

            Color? checksColor = null;
            var result = Render(fixture, "case-summary-with-errors", RenderHarness.Large, control =>
            {
                var line = AllControls(control).OfType<Label>()
                    .FirstOrDefault(l => l.Text.Contains("error", StringComparison.Ordinal) && l.Text.Contains("warning", StringComparison.Ordinal));
                checksColor = line?.ForeColor;
            });

            AssertRendered(result);
            Assert.Equal(Helpers.Theme.Danger, checksColor);
        });
    }

    [Fact]
    public void Summary_NoCorpusPlan()
    {
        RenderHarness.RunSta(() =>
        {
            foreach (var size in new[] { RenderHarness.Large, RenderHarness.WizardMin })
            {
                using var fixture = new SummaryFixture(CaseTestData.SampleGuidedDraft());

                var notPlanned = false;
                var planCorpusVisible = false;
                var reviewPlanVisible = true;
                var horizontalScroll = true;
                var result = Render(fixture, "case-summary-no-corpus-plan", size, control =>
                {
                    notPlanned = AllControls(control).OfType<Label>().Any(l => l.Text == CorpusUiText.NotPlannedLine && l.Visible);
                    planCorpusVisible = VisibleButton(control, "Plan Corpus");
                    reviewPlanVisible = VisibleButton(control, "Review Plan");
                    horizontalScroll = RowsHost(control).HorizontalScroll.Visible;
                });

                AssertRendered(result);
                Assert.True(notPlanned);
                Assert.True(planCorpusVisible);
                Assert.False(reviewPlanVisible);
                Assert.False(horizontalScroll);
            }
        });
    }

    [Fact]
    public void Summary_CorpusApproved()
    {
        RenderHarness.RunSta(() =>
        {
            foreach (var size in new[] { RenderHarness.Large, RenderHarness.WizardMin })
            {
                using var fixture = new SummaryFixture(CorpusTestData.RichDraft());
                var draft = fixture.State.Case;
                var folder = draft.FolderPath;
                var plan = PlanFixtures.ValidPlan(draft);
                CorpusPlanStore.SaveAsync(folder, plan).GetAwaiter().GetResult();

                // Written files for the current plan, as Approve Plan leaves them
                var guide = draft.Guide!;
                var rows = AnswerKeyBuilder.Build(plan, guide);
                var protocolName = ReviewProtocolBuilder.FileNameFor(Path.GetFileName(folder));
                File.WriteAllBytes(Path.Combine(folder, AnswerKeyCsvWriter.FileName), AnswerKeyCsvWriter.Build(rows));
                File.WriteAllBytes(Path.Combine(folder, AnswerKeyXlsxWriter.FileName), AnswerKeyXlsxWriter.Build(rows, guide));
                File.WriteAllBytes(Path.Combine(folder, protocolName), ReviewProtocolBuilder.Build(draft, ReviewProtocolBuilder.FallbackProse(draft)));

                var writtenUtc = new DateTime(2024, 9, 3, 15, 30, 0, DateTimeKind.Utc);
                draft.Corpus = new CorpusState
                {
                    Settings = plan.Settings,
                    PlanId = plan.PlanId,
                    PlanGuideHash = plan.GuideHash,
                    PlanSettingsHash = plan.SettingsHash,
                    Approved = true,
                    ApprovedUtc = writtenUtc,
                    WrittenFiles = new List<WrittenCorpusFile>
                    {
                        new() { Kind = CorpusFileKind.AnswerKeyCsv, FileName = AnswerKeyCsvWriter.FileName, PlanId = plan.PlanId, WrittenUtc = writtenUtc },
                        new() { Kind = CorpusFileKind.AnswerKeyXlsx, FileName = AnswerKeyXlsxWriter.FileName, PlanId = plan.PlanId, WrittenUtc = writtenUtc },
                        new() { Kind = CorpusFileKind.ReviewProtocol, FileName = protocolName, PlanId = plan.PlanId, WrittenUtc = writtenUtc }
                    }
                };

                var buttons = new Dictionary<string, bool>();
                var writtenLines = 0;
                var approvedLines = 0;
                var horizontalScroll = true;
                var result = Render(fixture, "case-summary-corpus-approved", size, control =>
                {
                    // The Corpus group comes first, so the top of the contents shows it
                    foreach (var caption in new[] { "Open CSV", "Open in Excel", "Open in Word", "Review Plan", "Plan Corpus" })
                        buttons[caption] = VisibleButton(control, caption);
                    var labels = AllControls(control).OfType<Label>().Where(l => l.Visible).ToList();
                    writtenLines = labels.Count(l => l.Text.StartsWith("Written ", StringComparison.Ordinal));
                    approvedLines = labels.Count(l => l.Text.StartsWith("Approved ", StringComparison.Ordinal));
                    horizontalScroll = RowsHost(control).HorizontalScroll.Visible;
                });

                AssertRendered(result);
                Assert.True(buttons["Open CSV"]);
                Assert.True(buttons["Open in Excel"]);
                Assert.True(buttons["Open in Word"]);
                Assert.True(buttons["Review Plan"]);
                Assert.False(buttons["Plan Corpus"]);
                Assert.Equal(2, writtenLines);
                Assert.Equal(1, approvedLines);
                Assert.False(horizontalScroll);
            }
        });
    }

    // ----- Render state 7 (05-UI-SPEC): Mailboxes + Story so far rows, scrolled to the Corpus group -----

    public enum RunSeed
    {
        NotGenerated,
        Partial,
        Complete,
        EarlierPlan
    }

    [Theory]
    [InlineData(RunSeed.NotGenerated)]
    [InlineData(RunSeed.Partial)]
    [InlineData(RunSeed.Complete)]
    [InlineData(RunSeed.EarlierPlan)]
    public void Summary_GeneratedCorpusRows(RunSeed seed)
    {
        RenderHarness.RunSta(() =>
        {
            foreach (var size in new[] { RenderHarness.Large, RenderHarness.WizardMin })
            {
                using var fixture = new SummaryFixture(CorpusTestData.RichDraft());
                var plan = SeedApprovedCorpus(fixture);
                SeedRun(fixture, plan, seed);

                var mailboxesLine = string.Empty;
                Color mailboxesColor = default;
                Color mailboxesDot = default;
                var storyLine = string.Empty;
                Color storyColor = default;
                Color storyDot = default;
                var answerKeyLine = string.Empty;
                var openMailboxes = false;
                var generateEmails = false;
                var openStory = false;
                var exportStory = false;
                var horizontalScroll = true;
                var name = $"case-summary-run-{seed.ToString().ToLowerInvariant()}";
                var result = Render(fixture, name, size, control =>
                {
                    var step = (StepCaseSummary)control;
                    // Scroll the Corpus group's last row into view
                    var host = RowsHost(control);
                    host.ScrollControlIntoView(step.StoryLine3.Parent!);
                    mailboxesLine = step.MailboxesLine3.Text;
                    mailboxesColor = step.MailboxesLine3.ForeColor;
                    mailboxesDot = step.MailboxesDotColor;
                    storyLine = step.StoryLine3.Text;
                    storyColor = step.StoryLine3.ForeColor;
                    storyDot = step.StoryDotColor;
                    answerKeyLine = step.AnswerKeyLine3.Text;
                    openMailboxes = step.OpenMailboxesButton.Visible;
                    generateEmails = step.GenerateEmailsButton.Visible;
                    openStory = step.OpenStoryButton.Visible;
                    exportStory = step.ExportStoryButton.Visible;
                    horizontalScroll = host.HorizontalScroll.Visible;
                });

                AssertRendered(result);
                Assert.False(horizontalScroll);
                switch (seed)
                {
                    case RunSeed.NotGenerated:
                        Assert.Equal("Not generated yet", mailboxesLine);
                        Assert.Equal(Helpers.Theme.TextMuted, mailboxesColor);
                        Assert.Equal(Helpers.Theme.BorderStrong, mailboxesDot);
                        Assert.True(generateEmails);
                        Assert.False(openMailboxes);
                        Assert.Equal("Not written yet. It is written when a generation run ends.", storyLine);
                        Assert.Equal(Helpers.Theme.TextMuted, storyColor);
                        Assert.Equal(Helpers.Theme.BorderStrong, storyDot);
                        Assert.False(openStory);
                        Assert.False(exportStory);
                        Assert.DoesNotContain("generated", answerKeyLine);
                        break;
                    case RunSeed.Partial:
                        Assert.Equal("7 of 10 threads written · 1 item with warnings · 2 failed items", mailboxesLine);
                        Assert.Equal(Helpers.Theme.Warning, mailboxesColor);
                        Assert.Equal(Helpers.Theme.Warning, mailboxesDot);
                        Assert.True(openMailboxes);
                        Assert.False(generateEmails);
                        Assert.StartsWith("Could not write: ", storyLine);
                        Assert.Equal(Helpers.Theme.Danger, storyColor);
                        Assert.Equal(Helpers.Theme.Danger, storyDot);
                        Assert.False(openStory);
                        Assert.True(exportStory);
                        Assert.EndsWith($" · 18 generated, {Math.Max(0, plan.EmailCount + plan.AttachmentCount - 18 - 4):N0} planned", answerKeyLine);
                        break;
                    case RunSeed.Complete:
                        Assert.Equal("All 10 threads written", mailboxesLine);
                        Assert.Equal(Helpers.Theme.Success, mailboxesColor);
                        Assert.Equal(Helpers.Theme.Success, mailboxesDot);
                        Assert.True(openMailboxes);
                        Assert.False(generateEmails);
                        Assert.StartsWith("Written ", storyLine);
                        Assert.Equal(Helpers.Theme.Success, storyColor);
                        Assert.Equal(Helpers.Theme.Success, storyDot);
                        Assert.True(openStory);
                        Assert.True(exportStory);
                        Assert.StartsWith("Written ", answerKeyLine);
                        Assert.Contains(" generated, ", answerKeyLine);
                        break;
                    case RunSeed.EarlierPlan:
                        Assert.Equal("Written for an earlier plan. Start over on Generate Corpus.", mailboxesLine);
                        Assert.Equal(Helpers.Theme.Warning, mailboxesColor);
                        Assert.Equal(Helpers.Theme.Warning, mailboxesDot);
                        Assert.True(openMailboxes);
                        // The ledger belongs to another plan: no export from here, and no run clause on the key
                        Assert.False(exportStory);
                        Assert.DoesNotContain("generated", answerKeyLine);
                        break;
                }
            }
        });
    }

    private static void SeedRun(SummaryFixture fixture, CorpusPlan plan, RunSeed seed)
    {
        if (seed == RunSeed.NotGenerated)
            return;

        var draft = fixture.State.Case;
        var folder = draft.FolderPath;
        Directory.CreateDirectory(Path.Combine(folder, PlanNumbering.MailboxesFolder));
        var ledger = new NarrativeLedger
        {
            Entries = Enumerable.Range(1, 6).Select(i => new LedgerEntry
            {
                Id = $"T-000{i}-L01",
                Date = $"2024-03-0{i}",
                Kind = LedgerKind.Figure,
                Statement = $"Detail {i}.",
                SourceThreadId = $"T-000{(i + 1) / 2}",
                WaveIndex = 1
            }).ToList()
        };
        var generation = new GenerationState
        {
            RunPlanId = seed == RunSeed.EarlierPlan ? Guid.NewGuid() : plan.PlanId,
            Ledger = ledger,
            ThreadsTotal = 10,
            ThreadsWritten = seed == RunSeed.Partial ? 6 : 10,
            ThreadsWithWarnings = seed == RunSeed.Partial ? 1 : 0,
            ThreadsFailed = seed == RunSeed.Partial ? 2 : 0,
            EmailsWritten = seed == RunSeed.Partial ? 18 : 26,
            EmailsFailed = seed == RunSeed.Partial ? 4 : 0,
            FilesWritten = seed == RunSeed.Partial ? 61 : 88,
            Mailboxes = 7
        };
        if (seed == RunSeed.Partial)
            generation.StoryError = $"{StorySoFarDocxBuilder.FileNameFor(Path.GetFileName(folder))}: The process cannot access the file because it is being used by another process.";
        draft.Corpus!.Generation = generation;

        if (seed is RunSeed.Complete or RunSeed.EarlierPlan)
        {
            var storyName = StorySoFarDocxBuilder.FileNameFor(Path.GetFileName(folder));
            File.WriteAllBytes(Path.Combine(folder, storyName), new byte[] { 1 });
            draft.Corpus.WrittenFiles.Add(new WrittenCorpusFile
            {
                Kind = CorpusFileKind.StorySoFar,
                FileName = storyName,
                PlanId = generation.RunPlanId,
                WrittenUtc = new DateTime(2024, 9, 4, 16, 45, 0, DateTimeKind.Utc)
            });
        }
    }

    /// <summary>
    /// Saves a valid plan and the files Approve Plan writes, and records the approval. Returns the plan.
    /// </summary>
    private static CorpusPlan SeedApprovedCorpus(SummaryFixture fixture)
    {
        var draft = fixture.State.Case;
        var folder = draft.FolderPath;
        var plan = PlanFixtures.ValidPlan(draft);
        CorpusPlanStore.SaveAsync(folder, plan).GetAwaiter().GetResult();

        var guide = draft.Guide!;
        var rows = AnswerKeyBuilder.Build(plan, guide);
        var protocolName = ReviewProtocolBuilder.FileNameFor(Path.GetFileName(folder));
        File.WriteAllBytes(Path.Combine(folder, AnswerKeyCsvWriter.FileName), AnswerKeyCsvWriter.Build(rows));
        File.WriteAllBytes(Path.Combine(folder, AnswerKeyXlsxWriter.FileName), AnswerKeyXlsxWriter.Build(rows, guide));
        File.WriteAllBytes(Path.Combine(folder, protocolName), ReviewProtocolBuilder.Build(draft, ReviewProtocolBuilder.FallbackProse(draft)));

        var writtenUtc = new DateTime(2024, 9, 3, 15, 30, 0, DateTimeKind.Utc);
        draft.Corpus = new CorpusState
        {
            Settings = plan.Settings,
            PlanId = plan.PlanId,
            PlanGuideHash = plan.GuideHash,
            PlanSettingsHash = plan.SettingsHash,
            Approved = true,
            ApprovedUtc = writtenUtc,
            WrittenFiles = new List<WrittenCorpusFile>
            {
                new() { Kind = CorpusFileKind.AnswerKeyCsv, FileName = AnswerKeyCsvWriter.FileName, PlanId = plan.PlanId, WrittenUtc = writtenUtc },
                new() { Kind = CorpusFileKind.AnswerKeyXlsx, FileName = AnswerKeyXlsxWriter.FileName, PlanId = plan.PlanId, WrittenUtc = writtenUtc },
                new() { Kind = CorpusFileKind.ReviewProtocol, FileName = protocolName, PlanId = plan.PlanId, WrittenUtc = writtenUtc }
            }
        };
        return plan;
    }

    // Visible within the step: Control.Visible is false for children of a hidden form, so compare against the root
    private static bool VisibleButton(Control root, string caption) =>
        AllControls(root).OfType<Button>().Any(b => b.Text == caption && b.Visible);

    private static Panel RowsHost(Control root) =>
        AllControls(root).OfType<Panel>().First(p => p.AutoScroll && p is not TableLayoutPanel);

    private static RenderResult Render(SummaryFixture fixture, string name, Size size, Action<Control>? inspect = null)
    {
        return RenderHarness.RenderControl(() =>
        {
            var step = new StepCaseSummary();
            step.LoadForRender(fixture.State);
            return step;
        }, name, size, control => inspect?.Invoke(control));
    }

    private static IEnumerable<Control> AllControls(Control root)
    {
        foreach (Control child in root.Controls)
        {
            yield return child;
            foreach (var nested in AllControls(child))
                yield return nested;
        }
    }

    private static void AssertRendered(RenderResult result)
    {
        Assert.True(File.Exists(result.PngPath), result.PngPath);
        Assert.True(result.DeviceDpi >= RenderHarness.MinimumDpi, $"DPI {result.DeviceDpi}");
        Assert.Empty(result.Clipping);
    }

    /// <summary>
    /// A saved case under a temp Cases root; deleted on dispose. Tests write only here.
    /// </summary>
    private sealed class SummaryFixture : IDisposable
    {
        private readonly string _root;

        public SummaryFixture(CaseDraft draft)
        {
            _root = Path.Combine(Path.GetTempPath(), "rd-summary-" + Guid.NewGuid().ToString("N"));
            var store = new CaseStore(Path.Combine(_root, "Cases"), Path.Combine(_root, "AppData"));
            draft.AdvanceStage(CaseStage.Summary);
            store.EnsureFolder(draft);
            Assert.True(store.SaveNow(draft), store.LastSaveError);
            State = new WizardState { Case = draft, CaseStore = store };
        }

        public WizardState State { get; }

        public void Dispose()
        {
            try
            {
                Directory.Delete(_root, recursive: true);
            }
            catch (IOException)
            {
                // A file handle may linger briefly after the render; the temp folder is harmless
            }
        }
    }
}
