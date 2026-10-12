using ReelDiscovery.Helpers;
using ReelDiscovery.Models;
using ReelDiscovery.Models.Case;
using ReelDiscovery.Models.Corpus;
using ReelDiscovery.Models.Evidence;
using ReelDiscovery.Services.Case;
using ReelDiscovery.Tests.Case;
using ReelDiscovery.UserControls;

namespace ReelDiscovery.Tests.Render;

/// <summary>
/// Renders the whole Case Guide step at the active scaling in every state of the UI-SPEC render contract (1-9).
/// LoadForRender never calls the generator; the case is saved under a temp root only.
/// </summary>
[Trait("Category", "Render")]
[Collection(RenderCollection.Name)]
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
                        && AllControls(step).OfType<Button>().Any(b => b.Visible && b.Text == "Review Facts")
                        && AllControls(step).OfType<Button>().Any(b => b.Visible && b.Enabled && b.Text == "Mark All Reviewed");
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
    public void FactsTab_NeedsReview_MultiSelect()
    {
        RenderHarness.RunSta(() =>
        {
            foreach (var size in new[] { RenderHarness.Large, RenderHarness.Small })
            {
                // After a cast edit most facts need review (UAT): the banner offers Mark All Reviewed, the grid a selection bar
                var draft = CaseTestData.SampleGuidedDraft();
                foreach (var fact in draft.Guide!.Facts.Skip(1))
                {
                    fact.NeedsReview = true;
                    fact.ReviewReason = "Tom Becker was changed after it was written.";
                }
                using var fixture = new GuideFixture(draft);
                var banner = false;
                var bar = false;
                var bannerButtonsRight = -1;
                var bannerRight = -1;
                var result = Render(fixture, "case-guide-facts-multi-select", size, false, null, GuidePart.Facts, step =>
                {
                    step.ShowTab(GuidePart.Facts, "F-002");
                    var grid = AllControls(step).OfType<FactsTabPanel>().Single().Controls.Cast<Control>()
                        .SelectMany(AllControls).OfType<DataGridView>().First();
                    foreach (DataGridViewRow row in grid.Rows)
                    {
                        if (row.Tag is "F-003" or "F-004" or "F-005" or "F-006")
                            row.Selected = true;
                    }
                    banner = VisibleLabel(step, "5 facts reference characters you changed.")
                        && AllControls(step).OfType<Button>().Any(b => b.Visible && b.Text == "Mark All Reviewed");
                    bar = VisibleLabel(step, "5 facts selected");
                    var markAll = AllControls(step).OfType<Button>().First(b => b.Text == "Mark All Reviewed");
                    bannerButtonsRight = step.PointToClient(markAll.Parent!.PointToScreen(new Point(markAll.Right, 0))).X;
                    var card = AllControls(step).OfType<CardPanel>().First(c => c.Visible && c.BorderColor == Theme.Warning);
                    bannerRight = step.PointToClient(card.Parent!.PointToScreen(new Point(card.Right, 0))).X - card.Padding.Right;
                });

                AssertRendered(result);
                Assert.True(banner);
                Assert.True(bar);
                // The banner's buttons end on its 16 px right padding
                Assert.Equal(bannerRight, bannerButtonsRight);
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
                var wholeRows = 0;
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
                    var grid = AllControls(step).OfType<DataGridView>().First(g => g.Columns.Contains("Severity"));
                    wholeRows = grid.DisplayedRowCount(false) == grid.DisplayedRowCount(true) ? grid.DisplayedRowCount(false) : -1;
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
                    // Whole checks only: no row is cut off at the bottom of the card
                    Assert.True(wholeRows >= 1, $"whole rows shown: {wholeRows}");
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
    public void Checks_WithFixHintsAndFixButtons()
    {
        RenderHarness.RunSta(() =>
        {
            foreach (var size in new[] { RenderHarness.Large, RenderHarness.Small })
            {
                // Errors with an automatic fix (inactive actor, broken link) and one that needs the user (date after filing)
                var draft = CaseTestData.SampleGuidedDraft();
                draft.Guide!.FindCast("CH-002")!.ActiveTo = "2024-03-01";
                draft.Guide.FindFact("F-002")!.ActorIds.Add("CH-099");
                draft.Guide.FindFact("F-006")!.Date = "2024-10-02";
                var automatic = CaseGuideUiText.AutomaticFixCount(CaseGuideValidator.Validate(draft));
                using var fixture = new GuideFixture(draft);

                var hint = false;
                var fix = (Visible: false, Enabled: false, Primary: false, Text: "");
                var fixAll = (Visible: false, Text: "");
                var rowHeight = 0;
                var expectedRowHeight = 0;
                var wholeRows = 0;
                var buttonsRight = -1;
                var captionRight = -1;
                var captionLeft = -1;
                var cardLeft = -1;
                var listShown = false;
                var result = Render(fixture, "case-guide-checks-fixes", size, false, null, GuidePart.Facts, step =>
                {
                    step.ShowTab(GuidePart.Facts, "F-003");
                    var grid = AllControls(step).OfType<DataGridView>().First(g => g.Columns.Contains("Severity"));
                    listShown = grid.Visible;
                    var first = grid.Rows.Cast<DataGridViewRow>().First(r => ((GuideFinding)r.Tag!).QuickFix == GuideQuickFix.WidenActiveRange);
                    step.SelectCheck(first.Index, focus: false);

                    hint = VisibleLabel(step, CaseGuideUiText.ChecksHintText);
                    var fixAllButton = AllControls(step).OfType<ThemedButton>().First(b => b.Text.StartsWith("Fix All", StringComparison.Ordinal));
                    fixAll = (fixAllButton.Visible, fixAllButton.Text);
                    // Fix sits just left of Fix All in the caption line
                    var fixButton = (ThemedButton)fixAllButton.Parent!.Controls[0];
                    fix = (fixButton.Visible, fixButton.Enabled, fixButton.Variant == ButtonStyle.Primary, fixButton.Text);
                    rowHeight = first.Height;
                    expectedRowHeight = step.LogicalToDeviceUnits(48);

                    var checks = AllControls(step).OfType<Label>().First(l => l.Text.StartsWith("Checks (", StringComparison.Ordinal));
                    var card = checks.Parent!.Parent!.Parent!;
                    wholeRows = grid.DisplayedRowCount(false) == grid.DisplayedRowCount(true) ? grid.DisplayedRowCount(false) : -1;
                    // Fix All ends on the caption line's 15 px right inset; the caption starts on Content x = 16
                    buttonsRight = step.PointToClient(fixAllButton.Parent!.PointToScreen(new Point(fixAllButton.Right, 0))).X;
                    captionRight = step.PointToClient(card.PointToScreen(new Point(card.Width, 0))).X - 1 - step.LogicalToDeviceUnits(15);
                    captionLeft = step.PointToClient(checks.Parent!.PointToScreen(new Point(checks.Left, 0))).X;
                    cardLeft = step.PointToClient(card.PointToScreen(Point.Empty)).X;
                });

                AssertRendered(result);
                Assert.True(automatic >= 3);
                Assert.Equal(CaseGuideUiText.FixAllButtonText(automatic), fixAll.Text);
                Assert.True(fixAll.Visible);
                Assert.Equal(captionRight, buttonsRight);
                Assert.Equal(cardLeft + 1 + fixture.ScaledInset, captionLeft);
                if (size == RenderHarness.Large)
                {
                    Assert.True(listShown);
                    Assert.True(hint);
                    Assert.True(fix.Visible);
                    Assert.True(fix.Enabled);
                    Assert.True(fix.Primary);
                    Assert.Equal("Fix", fix.Text);
                    Assert.Equal(expectedRowHeight, rowHeight);
                    // Whole checks only: no row is cut off at the bottom of the card
                    Assert.Equal(2, wholeRows);
                }
                else if (listShown)
                {
                    // A short window keeps at least one whole check, and Fix follows it
                    Assert.True(wholeRows >= 1, $"whole rows shown: {wholeRows}");
                    Assert.True(fix.Visible);
                }
                else
                {
                    // The folded list has no row to pick: only Fix All stays
                    Assert.False(fix.Visible);
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

    [Fact]
    public void PlanDependencyBanner()
    {
        RenderHarness.RunSta(() =>
        {
            foreach (var size in new[] { RenderHarness.Large, RenderHarness.WizardMin })
            {
                // 09 S2 decision table row 1: the plan was built from an earlier guide and no other banner holds the row
                var draft = CaseTestData.SampleGuidedDraft();
                draft.Corpus = PlanCorpus("stale", approved: false);
                using var fixture = new GuideFixture(draft);
                var heading = false;
                var body = false;
                var reviewPlan = false;
                var primaryInBanner = true;
                var lineShown = true;
                var result = Render(fixture, "case-guide-plan-dependency-" + SizeName(size), size, false, null, GuidePart.Issues, step =>
                {
                    heading = VisibleLabel(step, "The corpus plan was built from an earlier version of this guide.");
                    body = AllControls(step).OfType<Label>()
                        .Any(l => l.Visible && l.Text.Contains("The 1,240 emails already written stay in place.", StringComparison.Ordinal));
                    var banner = AllControls(step).OfType<CardPanel>().First(c => c.Visible && c.BorderColor == Theme.Warning);
                    var buttons = AllControls(banner).OfType<ThemedButton>().Where(b => b.Visible).ToList();
                    reviewPlan = buttons.Any(b => b.Enabled && b.Text == "Review Plan" && b.Variant == ButtonStyle.Default);
                    primaryInBanner = buttons.Any(b => b.Variant == ButtonStyle.Primary);
                    lineShown = DependencyLine(step) != null;
                });

                AssertRendered(result);
                Assert.True(heading);
                Assert.True(body);
                Assert.True(reviewPlan);
                Assert.False(primaryInBanner);
                Assert.False(lineShown);
            }
        });
    }

    [Fact]
    public void StaleCoreBanner_WithDependencyLine()
    {
        RenderHarness.RunSta(() =>
        {
            foreach (var size in new[] { RenderHarness.Large, RenderHarness.WizardMin })
            {
                // 09 S2: StaleCore holds the banner row, so the changed plan shows as a Warning header line
                var draft = CaseTestData.SampleGuidedDraft();
                foreach (var part in GuideStaleness.Order)
                    GuideStaleness.Stamp(draft, part);
                var section = draft.CoreDocument!.Sections.First(s => s.Paragraphs.Count > 0);
                section.Paragraphs[0] += " Harmon later disputed the change order in writing.";
                draft.Corpus = PlanCorpus("stale", approved: false);
                using var fixture = new GuideFixture(draft);
                var staleBanner = false;
                var reviewPlanInBanner = true;
                LineCheck line = default;
                var result = Render(fixture, "case-guide-stale-core-dependency-" + SizeName(size), size, false, null, GuidePart.Issues, step =>
                {
                    staleBanner = VisibleLabel(step, "The guide is out of date with the core document.")
                        && AllControls(step).OfType<Button>().Any(b => b.Visible && b.Text == "Update Guide");
                    reviewPlanInBanner = AllControls(step).OfType<Button>().Any(b => b.Visible && b.Text == "Review Plan");
                    line = CheckLine(step);
                });

                AssertRendered(result);
                Assert.True(staleBanner);
                Assert.False(reviewPlanInBanner);
                Assert.Equal("The corpus plan was built from an earlier version of this guide. Review it on Review Plan before generating emails.", line.Text);
                Assert.Equal(Theme.Warning, line.Color);
                AssertLineFits(line);
            }
        });
    }

    [Fact]
    public void DependencyLine_Info()
    {
        RenderHarness.RunSta(() =>
        {
            foreach (var size in new[] { RenderHarness.Large, RenderHarness.WizardMin })
            {
                // 09 S2: an approved plan built from this guide -> muted Info line; Values left unwritten so Resume Guide
                // shows beside the line (the line must wrap before it, never under it)
                var draft = CaseTestData.SampleGuidedDraft();
                draft.Guide!.Values.Clear();
                draft.Guide.ValuesState.Status = GuidePartStatus.Pending;
                draft.Corpus = PlanCorpus(draft.Guide.ComputeHash(), approved: true);
                using var fixture = new GuideFixture(draft);
                var banner = true;
                LineCheck line = default;
                var result = Render(fixture, "case-guide-dependency-info-" + SizeName(size), size, false, null, GuidePart.Issues, step =>
                {
                    banner = AllControls(step).OfType<CardPanel>().Any(c => c.Visible && c.BorderColor == Theme.Warning);
                    line = CheckLine(step);
                });

                AssertRendered(result);
                Assert.False(banner);
                Assert.Equal("The approved corpus plan and 1,240 generated emails were built from this guide. Editing the guide withdraws plan approval until you review the plan again.", line.Text);
                Assert.Equal(Theme.TextMuted, line.Color);
                Assert.True(line.ResumeVisible);
                AssertLineFits(line);
            }
        });
    }

    // ----- Helpers -----

    private struct LineCheck
    {
        public string Text;
        public Color Color;
        public Rectangle Bounds;
        public Rectangle Resume;
        public bool ResumeVisible;
        public int LineHeight;
        public bool Ellipsis;
    }

    private static CorpusState PlanCorpus(string guideHash, bool approved) => new()
    {
        PlanId = Guid.NewGuid(),
        PlanGuideHash = guideHash,
        Approved = approved,
        Generation = new GenerationState { EmailsWritten = 1240 }
    };

    private static string SizeName(Size size) => size == RenderHarness.Large ? "large" : "wizardmin";

    // The header dependency line: the visible label in the header's third row (the header also hosts Resume Guide)
    private static Label? DependencyLine(StepCaseGuide step) =>
        AllControls(step).OfType<Label>().FirstOrDefault(l => l.Visible && l.Parent is TableLayoutPanel t
            && t.GetRow(l) == 2 && t.GetColumn(l) == 0 && AllControls(t).Any(c => c is Button b && b.Text == "Resume Guide"));

    private static LineCheck CheckLine(StepCaseGuide step)
    {
        var label = DependencyLine(step);
        Assert.NotNull(label);
        var resume = AllControls(step).OfType<Button>().First(b => b.Text == "Resume Guide");
        return new LineCheck
        {
            Text = label!.Text,
            Color = label.ForeColor,
            Bounds = step.RectangleToClient(label.Parent!.RectangleToScreen(label.Bounds)),
            Resume = step.RectangleToClient(resume.Parent!.RectangleToScreen(resume.Bounds)),
            ResumeVisible = resume.Visible,
            LineHeight = TextRenderer.MeasureText("Ag", label.Font).Height,
            Ellipsis = label.AutoEllipsis
        };
    }

    // Rule 44: wraps to at most 2 lines, never ellipsizes, never overlaps or runs under Resume Guide
    private static void AssertLineFits(LineCheck line)
    {
        Assert.False(line.Ellipsis);
        Assert.True(line.Bounds.Width > 0 && line.Bounds.Height > 0);
        Assert.True(line.Bounds.Height <= 2 * line.LineHeight, $"line height {line.Bounds.Height} > 2 x {line.LineHeight}");
        if (line.ResumeVisible)
            Assert.True(line.Bounds.Right <= line.Resume.Left, $"line {line.Bounds} runs into Resume Guide {line.Resume}");
    }

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
            fixture.ScaledInset = step.LogicalToDeviceUnits(15);
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

        public int ScaledInset { get; set; }

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
