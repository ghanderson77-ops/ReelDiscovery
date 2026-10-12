using ReelDiscovery.Helpers;
using ReelDiscovery.Models;
using ReelDiscovery.Models.Case;
using ReelDiscovery.Models.Corpus;
using ReelDiscovery.Services.Case;
using ReelDiscovery.Tests.Corpus;
using ReelDiscovery.UserControls;

namespace ReelDiscovery.Tests.Render;

/// <summary>
/// Renders the Corpus Settings step at 144 DPI (04-UI-SPEC S1, render states 1-4) at Large and WizardMin.
/// </summary>
[Trait("Category", "Render")]
[Collection(RenderCollection.Name)]
public class CorpusSettingsRenderTests
{
    [Fact]
    public void Defaults_AdvancedCollapsed()
    {
        RenderBoth("corpus-settings-defaults", draft => { }, mediaAvailable: true, expandAdvanced: false, arrange: null,
            (step, labels) =>
            {
                Assert.True(step.ImagesBox.Enabled);
                Assert.True(step.ImagesBox.Checked);
                Assert.True(step.SizeToggles[1].Selected);
            });
    }

    [Fact]
    public void AdvancedExpanded_Mix95_Clamped()
    {
        RenderBoth("corpus-settings-advanced",
            draft => draft.Corpus!.Settings.DailyBusinessPercent = 45,
            mediaAvailable: true, expandAdvanced: true,
            arrange: step =>
            {
                step.HotField.Text = "9";
                step.FlushCommits();
                // Show the bottom of the Advanced card (the total line and Use Default Mix)
                step.ScrollHost.AutoScrollPosition = new Point(0, step.ScrollHost.DisplayRectangle.Height);
            },
            (step, labels) =>
            {
                Assert.Contains(CorpusUiText.MixTotalLine(95).Text, labels);
                Assert.Contains("Set to 5. Use 1 to 5.", labels);
            });
    }

    [Fact]
    public void LowRelevantWarning_DateClamp()
    {
        RenderBoth("corpus-settings-warnings",
            draft =>
            {
                draft.Corpus!.Settings.TotalEmails = 250;
                draft.Corpus.Settings.RelevantPercent = 5;
            },
            mediaAvailable: true, expandAdvanced: false,
            arrange: step =>
            {
                var first = CorpusSettings.FactRange(step.DraftForTest.Guide)!.Value.First;
                step.StartField.Text = first.AddDays(20).ToString("yyyy-MM-dd");
                step.FlushCommits();
            },
            (step, labels) =>
            {
                var first = CorpusSettings.FactRange(step.DraftForTest.Guide)!.Value.First;
                Assert.Contains(CorpusUiText.StartClampedText(first), labels);
                var warning = CorpusUiText.RelevantWarning(250, 5, CorpusTestData.DatedFactCount(step.DraftForTest));
                Assert.NotNull(warning);
                Assert.Contains(warning!.Text, labels);
            });
    }

    [Fact]
    public void NoOpenAIKey()
    {
        RenderBoth("corpus-settings-no-media", draft => { }, mediaAvailable: false, expandAdvanced: false, arrange: null,
            (step, labels) =>
            {
                Assert.False(step.ImagesBox.Enabled);
                Assert.False(step.VoicemailBox.Enabled);
                Assert.False(step.ImagesBox.Checked);
                Assert.Contains(CorpusUiText.MediaNotice, labels);
            });
    }

    // 09 UI-SPEC S4 / rule 46: with no plan the header shows Skip to Discovery Documents then Reset to Defaults on one row
    [Fact]
    public void NoPlan_SkipToDiscovery()
    {
        RenderBoth("corpus-settings-skip", draft => { }, mediaAvailable: true, expandAdvanced: false, arrange: null,
            (step, labels) =>
            {
                Assert.Contains(CorpusUiText.SettingsNotice(false, false, null), labels);
            },
            shown: step =>
            {
                var skip = (ThemedButton)step.SkipToDiscoveryButton;
                var reset = (ThemedButton)step.ResetDefaultsButton;
                Assert.True(skip.Visible);
                Assert.True(reset.Visible);
                Assert.Equal("Skip to Discovery Documents", skip.Text);
                Assert.Equal(ButtonStyle.Default, skip.Variant);
                Assert.Equal(ButtonStyle.Default, reset.Variant);
                var skipAt = step.PointToClient(skip.PointToScreen(Point.Empty));
                var resetAt = step.PointToClient(reset.PointToScreen(Point.Empty));
                Assert.Equal(skipAt.Y, resetAt.Y);
                Assert.True(resetAt.X >= skipAt.X + skip.Width, $"reset left {resetAt.X}, skip right {skipAt.X + skip.Width}");
                Assert.True(resetAt.X + reset.Width <= step.ClientSize.Width, "Reset to Defaults runs past the step");
                foreach (var button in new[] { skip, reset })
                {
                    var textWidth = TextRenderer.MeasureText(button.Text, button.Font).Width;
                    Assert.True(button.Width >= textWidth, $"'{button.Text}' {button.Width} px narrower than its text {textWidth} px");
                }
                // The title gives way before the buttons: it ends left of the button row
                var titleRight = step.PointToClient(step.TitleLabel.PointToScreen(Point.Empty)).X + step.TitleLabel.Width;
                Assert.True(titleRight <= skipAt.X, $"title right {titleRight}, skip left {skipAt.X}");
                Assert.True(step.NoticeLabel.Height >= step.NoticeLabel.PreferredHeight - 1,
                    $"notice {step.NoticeLabel.Height} px, needs {step.NoticeLabel.PreferredHeight} px");
            });
    }

    [Fact]
    public void ApprovedPlan_SkipHidden()
    {
        RenderBoth("corpus-settings-approved", draft =>
            {
                draft.Corpus!.PlanId = Guid.NewGuid();
                draft.Corpus.Approved = true;
                draft.Corpus.ApprovedUtc = new DateTime(2026, 9, 30, 15, 5, 0, DateTimeKind.Utc);
            },
            mediaAvailable: true, expandAdvanced: false, arrange: null,
            (step, labels) => { },
            shown: step =>
            {
                Assert.False(step.SkipToDiscoveryButton.Visible);
                Assert.True(step.ResetDefaultsButton.Visible);
            });
    }

    private static void RenderBoth(string name, Action<CaseDraft> setup, bool mediaAvailable, bool expandAdvanced,
        Action<StepCorpusSettings>? arrange, Action<StepCorpusSettings, List<string>> verify,
        Action<StepCorpusSettings>? shown = null)
    {
        RenderHarness.RunSta(() =>
        {
            foreach (var size in new[] { RenderHarness.Large, RenderHarness.WizardMin })
            {
                var draft = CorpusTestData.RichDraft();
                draft.Complexity = ComplexityLevel.Standard;
                draft.Corpus = new CorpusState { Settings = CorpusSettings.CreateDefault(draft) };
                setup(draft);
                using var fixture = new SettingsFixture(draft, mediaAvailable);

                StepCorpusSettings? step = null;
                var horizontalScroll = true;
                var leftHeight = 0;
                var rightHeight = -1;
                var sliderCenter = 0;
                var suffixCenter = int.MaxValue;
                var visibleLabels = new List<string>();
                var result = RenderHarness.RenderControl(() =>
                {
                    step = new StepCorpusSettings();
                    step.LoadForRender(fixture.State, expandAdvanced);
                    return step;
                }, name, size, control =>
                {
                    arrange?.Invoke(step!);
                    control.PerformLayout();
                    Application.DoEvents();
                    horizontalScroll = step!.ScrollHost.HorizontalScroll.Visible;
                    leftHeight = step.SizeCard.Height;
                    rightHeight = step.DatesCard.Height;
                    sliderCenter = CenterY(step, step.Slider);
                    suffixCenter = CenterY(step, step.PercentSuffix);
                    // Visible only while the form is shown, so collect the texts here
                    visibleLabels = AllControls(step).OfType<Label>().Where(l => l.Visible).Select(l => l.Text).ToList();
                    // Visible flags only hold while the form is shown
                    shown?.Invoke(step);
                });

                Assert.True(File.Exists(result.PngPath), result.PngPath);
                Assert.True(result.DeviceDpi >= RenderHarness.MinimumDpi, $"DPI {result.DeviceDpi}");
                Assert.Empty(result.Clipping);
                Assert.False(horizontalScroll);
                Assert.Equal(leftHeight, rightHeight);
                Assert.True(Math.Abs(sliderCenter - suffixCenter) <= 1, $"slider center {sliderCenter}, % center {suffixCenter}");
                verify(step!, visibleLabels);
            }
        });
    }

    private static int CenterY(Control root, Control control)
    {
        var top = root.PointToClient(control.PointToScreen(Point.Empty)).Y;
        return top + control.Height / 2;
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

    /// <summary>
    /// A saved case under a temp Cases root; deleted on dispose. Tests write only here.
    /// </summary>
    private sealed class SettingsFixture : IDisposable
    {
        private readonly string _root;

        public SettingsFixture(CaseDraft draft, bool mediaAvailable)
        {
            _root = Path.Combine(Path.GetTempPath(), "rd-corpus-settings-" + Guid.NewGuid().ToString("N"));
            var store = new CaseStore(Path.Combine(_root, "Cases"), Path.Combine(_root, "AppData"));
            draft.AdvanceStage(CaseStage.CorpusSettings);
            store.EnsureFolder(draft);
            Assert.True(store.SaveNow(draft), store.LastSaveError);
            State = new WizardState { Case = draft, CaseStore = store };
            if (mediaAvailable)
            {
                State.OpenAIApiKey = "sk-render-test";
                State.OpenAIKeyTested = true;
            }
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
