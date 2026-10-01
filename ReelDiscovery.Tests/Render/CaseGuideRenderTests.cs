using ReelDiscovery.Helpers;
using ReelDiscovery.Models;
using ReelDiscovery.Models.Case;
using ReelDiscovery.Services.Case;
using ReelDiscovery.Tests.Case;
using ReelDiscovery.UserControls;

namespace ReelDiscovery.Tests.Render;

/// <summary>
/// Renders the whole Case Guide step at the active scaling in every state of the UI-SPEC render contract (1-9).
/// LoadForRender never calls the generator; the case is saved under a temp root only.
/// </summary>
[Trait("Category", "Render")]
public class CaseGuideRenderTests
{
    private const string FailureMessage = "The response was cut off. Click Retry Key Facts.";

    [Fact]
    public void Generating_CastWriting()
    {
        RenderHarness.RunSta(() =>
        {
            var draft = IssuesOnlyDraft();
            draft.Guide!.CastState.Status = GuidePartStatus.Generating;
            using var fixture = new GuideFixture(draft);
            var heading = false;
            var cancel = false;
            var bar = false;
            var caption = string.Empty;
            var result = Render(fixture, "case-guide-generating-cast", RenderHarness.Large, true, GuidePart.Cast, GuidePart.Cast, step =>
            {
                heading = VisibleLabel(step, "Writing the cast...");
                cancel = AllControls(step).OfType<Button>().Any(b => b.Visible && b.Text == "Cancel Generation");
                bar = AllControls(step).OfType<SlimProgressBar>().Count(b => b.Visible) == 2;
                caption = AllControls(step).OfType<Label>().First(l => l.Text.StartsWith("Checks", StringComparison.Ordinal)).Text;
            });

            AssertRendered(result);
            Assert.True(heading);
            Assert.True(cancel);
            Assert.True(bar);
            Assert.Equal(CaseGuideUiText.ChecksRunningText, caption);
        });
    }

    [Fact]
    public void CastTab_Key()
    {
        RenderHarness.RunSta(() =>
        {
            foreach (var size in new[] { RenderHarness.Large, RenderHarness.Small })
            {
                using var fixture = new GuideFixture(CaseTestData.SampleGuidedDraft());
                var heading = false;
                var result = Render(fixture, "case-guide-cast-key", size, false, null, GuidePart.Cast, step =>
                {
                    step.ShowTab(GuidePart.Cast, "CH-001");
                    heading = VisibleLabel(step, "Maria Delgado");
                });

                AssertRendered(result);
                Assert.True(heading);
            }
        });
    }

    [Fact]
    public void CastTab_Background()
    {
        RenderHarness.RunSta(() =>
        {
            using var fixture = new GuideFixture(CaseTestData.SampleGuidedDraft());
            var heading = false;
            var result = Render(fixture, "case-guide-cast-background", RenderHarness.Large, false, null, GuidePart.Cast, step =>
            {
                step.ShowTab(GuidePart.Cast, "CH-008");
                heading = VisibleLabel(step, "Priya Nair");
            });

            AssertRendered(result);
            Assert.True(heading);
        });
    }

    [Fact]
    public void FactsTab_NeedsReview()
    {
        RenderHarness.RunSta(() =>
        {
            foreach (var size in new[] { RenderHarness.Large, RenderHarness.Small })
            {
                var draft = CaseTestData.SampleGuidedDraft();
                var fact = draft.Guide!.FindFact("F-004")!;
                fact.NeedsReview = true;
                fact.ReviewReason = "Tom Becker was changed after it was written.";
                using var fixture = new GuideFixture(draft);
                var banner = false;
                var notice = false;
                var result = Render(fixture, "case-guide-facts-needs-review", size, false, null, GuidePart.Facts, step =>
                {
                    step.ShowTab(GuidePart.Facts, "F-004");
                    banner = VisibleLabel(step, "1 fact references characters you changed.")
                        && AllControls(step).OfType<Button>().Any(b => b.Visible && b.Text == "Review Facts");
                    notice = VisibleLabel(step, "Review this fact: Tom Becker was changed after it was written.");
                });

                // Key Facts at the minimum window is the only state allowed a horizontal grid scrollbar
                AssertRendered(result);
                Assert.True(banner);
                Assert.True(notice);
            }
        });
    }

    [Fact]
    public void ValuesTab()
    {
        RenderHarness.RunSta(() =>
        {
            using var fixture = new GuideFixture(CaseTestData.SampleGuidedDraft());
            var heading = false;
            var actions = false;
            var result = Render(fixture, "case-guide-values", RenderHarness.Large, false, null, GuidePart.Values, step =>
            {
                step.ShowTab(GuidePart.Values, "V-001");
                heading = VisibleLabel(step, "Change Order 7 amount");
                actions = AllControls(step).OfType<Button>().Any(b => b.Visible && b.Text == "Add Value")
                    && AllControls(step).OfType<Button>().Any(b => b.Visible && b.Text == "Regenerate Values...");
            });

            AssertRendered(result);
            Assert.True(heading);
            Assert.True(actions);
        });
    }

    [Fact]
    public void IssuesTab()
    {
        RenderHarness.RunSta(() =>
        {
            using var fixture = new GuideFixture(CaseTestData.SampleGuidedDraft());
            var captions = new List<string>();
            var result = Render(fixture, "case-guide-issues", RenderHarness.Large, false, null, GuidePart.Issues, step =>
            {
                captions.AddRange(AllControls(step).OfType<ThemedButton>()
                    .Where(b => b.Variant == ButtonStyle.Toggle && b.Height == step.LogicalToDeviceUnits(34))
                    .Select(b => b.Text));
            });

            AssertRendered(result);
            var guide = fixture.State.Case.Guide!;
            Assert.Equal(new[]
            {
                $"Issues ({guide.Issues.Count})",
                "Cast (9)",
                "Key Facts (6)",
                "Canonical Values (4)"
            }, captions);
        });
    }

    [Fact]
    public void StaleBanner_WithChecks()
    {
        RenderHarness.RunSta(() =>
        {
            foreach (var size in new[] { RenderHarness.Large, RenderHarness.Small })
            {
                var draft = CaseTestData.SampleGuidedDraft();
                foreach (var part in GuideStaleness.Order)
                    GuideStaleness.Stamp(draft, part);
                var section = draft.CoreDocument!.Sections.First(s => s.Paragraphs.Count > 0);
                section.Paragraphs[0] += " Harmon later disputed the change order in writing.";
                // An orphan link: an Error under Checks
                draft.Guide!.FindFact("F-002")!.ActorIds.Add("CH-099");
                using var fixture = new GuideFixture(draft);

                var banner = false;
                var caption = string.Empty;
                var checksHeight = 0;
                var listShown = false;
                var collapsedHint = false;
                var result = Render(fixture, "case-guide-stale-with-checks", size, false, null, GuidePart.Facts, step =>
                {
                    step.ShowTab(GuidePart.Facts, "F-002");
                    banner = VisibleLabel(step, "The guide is out of date with the core document.")
                        && AllControls(step).OfType<Button>().Any(b => b.Visible && b.Text == "Update Guide")
                        && AllControls(step).OfType<Button>().Any(b => b.Visible && b.Text == "Not Now");
                    var checks = AllControls(step).OfType<Label>().First(l => l.Text.StartsWith("Checks (", StringComparison.Ordinal));
                    caption = checks.Text;
                    checksHeight = checks.Parent!.Parent!.Parent!.Height;
                    listShown = AllControls(step).OfType<DataGridView>().Any(g => g.Visible && g.Columns.Contains("Severity"));
                    collapsedHint = VisibleLabel(step, "Make the window taller to see the list.");
                });

                AssertRendered(result);
                Assert.True(banner);
                Assert.DoesNotContain("(0 errors", caption);
                Assert.DoesNotContain(" 0 warnings)", caption);
                if (size == RenderHarness.Large)
                {
                    Assert.True(listShown);
                    Assert.False(collapsedHint);
                    Assert.Equal(fixture.ScaledChecksHeight, checksHeight);
                }
                else
                {
                    // The minimum window cannot fit the banner, the list and a usable detail card: the list folds to its caption line
                    Assert.False(listShown);
                    Assert.True(collapsedHint);
                }
            }
        });
    }

    [Fact]
    public void FailedPart()
    {
        RenderHarness.RunSta(() =>
        {
            var draft = CaseTestData.SampleGuidedDraft();
            draft.Guide!.Facts.Clear();
            draft.Guide.Values.Clear();
            draft.Guide.FactsState.Status = GuidePartStatus.Failed;
            draft.Guide.FactsState.FailureMessage = FailureMessage;
            draft.Guide.ValuesState.Status = GuidePartStatus.Pending;
            using var fixture = new GuideFixture(draft);
            var heading = false;
            var body = false;
            var retry = false;
            var resume = false;
            var result = Render(fixture, "case-guide-failed-part", RenderHarness.Large, false, null, GuidePart.Facts, step =>
            {
                heading = VisibleLabel(step, "The key facts were not written");
                body = VisibleLabel(step, "The response was cut off. Click Retry Key Facts to try again. Finished parts were kept.");
                retry = AllControls(step).OfType<Button>().Any(b => b.Visible && b.Text == "Retry Key Facts");
                resume = AllControls(step).OfType<Button>().Any(b => b.Visible && b.Text == "Resume Guide");
            });

            AssertRendered(result);
            Assert.True(heading);
            Assert.True(body);
            Assert.True(retry);
            Assert.True(resume);
        });
    }

    [Fact]
    public void NoGuide()
    {
        RenderHarness.RunSta(() =>
        {
            var draft = CaseTestData.SampleComplaintDraft();
            draft.Guide = null;
            using var fixture = new GuideFixture(draft);
            var heading = false;
            var generate = false;
            var checksShown = true;
            var result = Render(fixture, "case-guide-no-guide", RenderHarness.Large, false, null, GuidePart.Issues, step =>
            {
                heading = VisibleLabel(step, "No case guide yet");
                generate = AllControls(step).OfType<Button>().Any(b => b.Visible && b.Text == "Generate Guide");
                checksShown = AllControls(step).OfType<Label>().Any(l => l.Visible && l.Text.StartsWith("Checks", StringComparison.Ordinal));
            });

            AssertRendered(result);
            Assert.True(heading);
            Assert.True(generate);
            Assert.False(checksShown);
        });
    }

    // ----- Helpers -----

    private static RenderResult Render(GuideFixture fixture, string name, Size size, bool simulateRun, GuidePart? writingPart,
        GuidePart selectedTab, Action<StepCaseGuide>? arrange = null)
    {
        // The minimum window may scroll the Key Facts grid sideways (UI-SPEC render contract); nothing else may
        var options = new ClippingOptions { AllowGridHorizontalScroll = size == RenderHarness.Small && selectedTab == GuidePart.Facts };
        return RenderHarness.RenderControl(() =>
        {
            var step = new StepCaseGuide();
            step.LoadForRender(fixture.State, simulateRun, writingPart, selectedTab);
            return step;
        }, name, size, control =>
        {
            var step = (StepCaseGuide)control;
            // The 152 row includes the card's 16 top margin
            fixture.ScaledChecksHeight = step.LogicalToDeviceUnits(152) - step.LogicalToDeviceUnits(Theme.Gap);
            arrange?.Invoke(step);
        }, options);
    }

    private static bool VisibleLabel(Control root, string text) =>
        AllControls(root).OfType<Label>().Any(l => l.Visible && l.Text == text);

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

    // Issues written, everything after them still pending and empty
    private static CaseDraft IssuesOnlyDraft()
    {
        var draft = CaseTestData.SampleGuidedDraft();
        var guide = draft.Guide!;
        guide.Organizations.Clear();
        guide.Cast.Clear();
        guide.Facts.Clear();
        guide.Values.Clear();
        guide.CastState.Status = GuidePartStatus.Pending;
        guide.FactsState.Status = GuidePartStatus.Pending;
        guide.ValuesState.Status = GuidePartStatus.Pending;
        return draft;
    }

    /// <summary>
    /// A saved case under a temp Cases root; deleted on dispose. Tests write only here.
    /// </summary>
    private sealed class GuideFixture : IDisposable
    {
        private readonly string _root;

        public GuideFixture(CaseDraft draft)
        {
            _root = Path.Combine(Path.GetTempPath(), "rd-guide-" + Guid.NewGuid().ToString("N"));
            var store = new CaseStore(Path.Combine(_root, "Cases"), Path.Combine(_root, "AppData"));
            draft.AdvanceStage(CaseStage.CaseGuide);
            store.EnsureFolder(draft);
            Assert.True(store.SaveNow(draft), store.LastSaveError);
            State = new WizardState { Case = draft, CaseStore = store };
        }

        public WizardState State { get; }

        public int ScaledChecksHeight { get; set; }

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
