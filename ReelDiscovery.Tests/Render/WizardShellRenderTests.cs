using ReelDiscovery.Forms;
using ReelDiscovery.Models;
using ReelDiscovery.Models.Case;
using ReelDiscovery.Services.Ai;
using ReelDiscovery.Services.Case;
using ReelDiscovery.Tests.Case;
using ReelDiscovery.UserControls;

namespace ReelDiscovery.Tests.Render;

/// <summary>
/// Renders the whole wizard window on the 9-step case flow at the active scaling (W5): header text,
/// navigation button text, default window size and clipping for steps 3 to 9.
/// </summary>
[Trait("Category", "Render")]
[Collection(RenderCollection.Name)]
public class WizardShellRenderTests
{
    // D-21: the first screen after launch is API Configuration; dummy keys keep the step from reading the user's saved keys
    [Fact]
    public void Shell_ApiConfigurationStep()
    {
        RenderStep(0, "wizard-shell-api-configuration", "Step 1 of 9: API Configuration", "Next >", UseDummyKeys);
    }

    // D-21: Next from API Configuration leads to Case Scenario (recent cases)
    [Fact]
    public void Shell_CaseScenarioStep()
    {
        RenderStep(1, "wizard-shell-case-scenario", "Step 2 of 9: Describe the Case", "Next >", UseDummyKeys);
    }

    // S7 / DPI contract 32: with the Workflow rows gone the disclaimer is the last row and no blank gap follows it
    [Fact]
    public void ApiConfigurationStep_NoGapBelowDisclaimer()
    {
        RenderHarness.RunSta(() =>
        {
            foreach (var size in new[] { RenderHarness.Large, RenderHarness.WizardMin })
            {
                var gap = 0;
                var tolerance = 0;
                var result = RenderHarness.RenderControl(() =>
                {
                    var step = new StepApiConfiguration();
                    var state = new WizardState();
                    UseDummyKeys(state);
                    step.BindState(state);
                    return step;
                }, "step-api-configuration", size, control =>
                {
                    ((StepApiConfiguration)control).OnEnterStepAsync().GetAwaiter().GetResult();
                    control.PerformLayout();
                    var layout = control.Controls.OfType<TableLayoutPanel>().Single();
                    var disclaimer = Controls(layout).OfType<Label>().Single(l => l.Text.StartsWith("NOTICE:", StringComparison.Ordinal));
                    gap = layout.Height - layout.Padding.Bottom - disclaimer.Bottom - disclaimer.Margin.Bottom;
                    tolerance = control.LogicalToDeviceUnits(4);
                });

                Assert.True(File.Exists(result.PngPath), result.PngPath);
                Assert.True(result.DeviceDpi >= RenderHarness.MinimumDpi, $"DPI {result.DeviceDpi}");
                Assert.Empty(result.Clipping);
                Assert.True(gap <= tolerance, $"{gap}px blank below the disclaimer at {size}");
            }
        });
    }

    private static void UseDummyKeys(WizardState state)
    {
        state.OpenAIApiKey = "sk-render-test";
        state.AnthropicApiKey = "sk-ant-render-test";
    }

    [Fact]
    public void Shell_CaseGuideStep()
    {
        RenderStep(3, "wizard-shell-case-guide", "Step 4 of 9: Case Guide", "Next >");
    }

    [Fact]
    public void Shell_CorpusSettingsStep()
    {
        // D-12: Corpus Settings is step 5 of the 9-step case flow
        RenderStep(4, "wizard-shell-corpus-settings", "Step 5 of 9: Corpus Settings", "Next >");
    }

    [Fact]
    public void Shell_ReviewPlanStep()
    {
        // Entering Review Plan without a planning request never starts a run (09 W2)
        RenderStep(5, "wizard-shell-review-plan", "Step 6 of 9: Review Plan", "Next >");
    }

    [Fact]
    public void Shell_GenerateCorpusStep()
    {
        // UI-SPEC S4: Generate Corpus sits between Review Plan and Case Summary; entering it never starts a run
        RenderStep(6, "wizard-shell-generate-corpus", "Step 7 of 9: Generate Corpus", "Next >");
    }

    [Fact]
    public void Shell_CaseSummaryStep()
    {
        RenderStep(8, "wizard-shell-case-summary", "Step 9 of 9: Case Summary", "Finish");
    }

    [Fact]
    public void Shell_DiscoveryDocumentsStep()
    {
        // D-07: Discovery Documents is the optional step between Generate Corpus and Case Summary; entering it never starts a run
        RenderStep(7, "wizard-shell-discovery-documents", "Step 8 of 9: Discovery Documents", "Next >");
    }

    [Fact]
    public void Shell_CoreDocumentStep()
    {
        RenderStep(2, "wizard-shell-core-document", "Step 3 of 9: Review Core Document", "Next >");
    }

    // D-16 (07-UI-SPEC S6): the header ticker tooltip is the session cost breakdown of the tracker totals
    [Fact]
    public void Shell_CostTickerTooltip_MatchesBreakdown()
    {
        RenderHarness.RunSta(() =>
        {
            using var fixture = new ShellFixture();
            using var form = WizardForm.CreateForRender(fixture.State);

            Assert.Equal("Nothing spent this session yet.", form.CostTooltipForTest);

            var tracker = fixture.State.UsageTracker;
            var model = AIModelConfig.GetDefaultModels().Single(m => m.ModelId == "claude-opus-5-5");
            var usage = new TokenUsage
            {
                InputTokens = 100_000, CacheWriteTokens = 50_000, CacheReadTokens = 200_000, OutputTokens = 40_000, IsBatch = true
            };
            tracker.RecordUsage("Test", model, usage, model.CalculateCost(usage));
            tracker.RecordMediaUsage("Images", 0.25m);

            var expected = GenerationUiText.CostBreakdown(tracker.TotalCost, tracker.TotalInputCost,
                tracker.TotalCacheWriteCost, tracker.TotalCacheReadCost, tracker.TotalOutputCost,
                tracker.TotalBatchSavings, tracker.TotalMediaCost);
            Assert.Equal(expected, form.CostTooltipForTest);
            Assert.StartsWith("Spent this session: $", expected);
            Assert.Contains("\nCached input, cache write: $", expected);
            Assert.Contains("\nBatch discount: −$", expected);
            Assert.Contains("\nImages and voicemail: $0.2500", expected);
        });
    }

    private static void RenderStep(int index, string name, string expectedTitle, string expectedNext,
        Action<WizardState>? configure = null)
    {
        RenderHarness.RunSta(() =>
        {
            using var fixture = new ShellFixture();
            configure?.Invoke(fixture.State);
            var title = string.Empty;
            var next = string.Empty;
            var nextHeight = 0;
            var expectedHeight = 0;
            var client = Size.Empty;
            var expectedClient = Size.Empty;
            var area = Rectangle.Empty;
            var checksFolded = false;

            var result = RenderHarness.RenderForm(() => WizardForm.CreateForRender(fixture.State), name, arrange: form =>
            {
                ((WizardForm)form).ShowStepForRender(index);

                // ShowStep is async (entering a step saves the case): pump until the header shows the new step
                var titleLabel = Find<Label>(form, "lblStepTitle");
                var deadline = DateTime.UtcNow.AddSeconds(10);
                while (titleLabel.Text != expectedTitle && DateTime.UtcNow < deadline)
                {
                    Application.DoEvents();
                    Thread.Sleep(20);
                }

                title = titleLabel.Text;
                var nextButton = Find<Button>(form, "btnNext");
                next = nextButton.Text;
                nextHeight = nextButton.Height;
                expectedHeight = form.LogicalToDeviceUnits(34);
                client = form.ClientSize;
                expectedClient = new Size(form.LogicalToDeviceUnits(WizardForm.PreferredClientSize.Width),
                    form.LogicalToDeviceUnits(WizardForm.PreferredClientSize.Height));
                checksFolded = Controls(form).OfType<Label>().Any(l => l.Visible && l.Text == "Make the window taller to see the list.");
                area = Screen.FromControl(form).WorkingArea;
            });

            Assert.True(File.Exists(result.PngPath), result.PngPath);
            Assert.True(result.DeviceDpi >= RenderHarness.MinimumDpi, $"DPI {result.DeviceDpi}");
            Assert.Equal(expectedTitle, title);
            Assert.Equal(expectedNext, next);
            Assert.Equal(expectedHeight, nextHeight);

            // The window opens at the preferred logical client size unless the screen's working area is smaller;
            // at that size the Case Guide shows its Checks list instead of folding it
            if (area.Width > expectedClient.Width + 40 && area.Height > expectedClient.Height + 80)
            {
                Assert.Equal(expectedClient, client);
                Assert.False(checksFolded, "The Checks list folded at the default window size");
            }

            Assert.Empty(result.Clipping);
        });
    }

    private static IEnumerable<Control> Controls(Control root)
    {
        foreach (Control child in root.Controls)
        {
            yield return child;
            foreach (var nested in Controls(child))
                yield return nested;
        }
    }

    private static T Find<T>(Control root, string name) where T : Control =>
        root.Controls.Find(name, searchAllChildren: true).OfType<T>().Single();

    /// <summary>
    /// A saved guided case under a temp root; deleted on dispose. Tests write only here.
    /// </summary>
    private sealed class ShellFixture : IDisposable
    {
        private readonly string _root;

        public ShellFixture()
        {
            _root = Path.Combine(Path.GetTempPath(), "rd-shell-" + Guid.NewGuid().ToString("N"));
            var store = new CaseStore(Path.Combine(_root, "Cases"), Path.Combine(_root, "AppData"));
            var draft = CaseTestData.SampleGuidedDraft();
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
