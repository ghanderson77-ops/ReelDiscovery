using ReelDiscovery.Models;
using ReelDiscovery.Models.Case;
using ReelDiscovery.Models.Corpus;
using ReelDiscovery.Services.Case;
using ReelDiscovery.Services.Corpus;
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
