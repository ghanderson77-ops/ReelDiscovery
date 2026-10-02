using ReelDiscovery.Models;
using ReelDiscovery.Models.Case;
using ReelDiscovery.Models.Corpus;
using ReelDiscovery.Services.Case;
using ReelDiscovery.UserControls;

namespace ReelDiscovery.Tests.Corpus;

/// <summary>
/// Pure helpers and commit behavior of the Corpus Settings step (04-UI-SPEC S1), without showing a window.
/// </summary>
public class CorpusSettingsStepLogicTests
{
    [Theory]
    [InlineData("1000", 1000)]
    [InlineData("1,000", 1000)]
    [InlineData(" 2,500 ", 2500)]
    [InlineData("0", 0)]
    [InlineData("99999999999999", int.MaxValue)]
    public void ParseCount_AcceptsDigitsAndCommas(string text, int expected)
    {
        Assert.Equal(expected, StepCorpusSettings.ParseCount(text));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(",")]
    [InlineData("-5")]
    [InlineData("1.5")]
    [InlineData("12a")]
    [InlineData("=1+1")]
    [InlineData(null)]
    public void ParseCount_RejectsAnythingElse(string? text)
    {
        Assert.Null(StepCorpusSettings.ParseCount(text));
    }

    [Theory]
    [InlineData(50, "50")]
    [InlineData(1000, "1,000")]
    [InlineData(5000, "5,000")]
    public void FormatCount_UsesInvariantThousands(int n, string expected)
    {
        Assert.Equal(expected, StepCorpusSettings.FormatCount(n));
    }

    [Theory]
    [InlineData(250, 250)]
    [InlineData(1000, 1000)]
    [InlineData(2500, 2500)]
    [InlineData(5000, 5000)]
    public void MatchingPreset_FindsPresets(int total, int expected)
    {
        Assert.Equal(expected, StepCorpusSettings.MatchingPreset(total));
    }

    [Theory]
    [InlineData(50)]
    [InlineData(999)]
    [InlineData(1001)]
    public void MatchingPreset_NullForOtherSizes(int total)
    {
        Assert.Null(StepCorpusSettings.MatchingPreset(total));
    }

    [Theory]
    [InlineData(40, 50, 5000, 50, true)]
    [InlineData(6000, 50, 5000, 5000, true)]
    [InlineData(1200, 50, 5000, 1200, false)]
    [InlineData(5, 5, 60, 5, false)]
    [InlineData(9, 1, 5, 5, true)]
    public void ClampInt_ReportsClamping(int value, int min, int max, int expected, bool clamped)
    {
        var result = StepCorpusSettings.ClampInt(value, min, max);
        Assert.Equal(expected, result.Value);
        Assert.Equal(clamped, result.Clamped);
    }

    [Theory]
    [InlineData("dm", "DM")]
    [InlineData("ab-12/cd", "ABCD")]
    [InlineData("abcdefgh", "ABCDEF")]
    [InlineData("..\\=", "")]
    [InlineData(null, "")]
    public void SanitizePrefix_KeepsSixUppercaseLetters(string? text, string expected)
    {
        Assert.Equal(expected, StepCorpusSettings.SanitizePrefix(text));
    }

    [Theory]
    [InlineData(true, true, true)]
    [InlineData(false, true, false)]
    [InlineData(true, false, false)]
    [InlineData(false, false, false)]
    public void ClearsAutoPlanSuppression_OnlyWhenNextSucceeds(bool mixOk, bool datesOk, bool expected)
    {
        Assert.Equal(expected, StepCorpusSettings.ClearsAutoPlanSuppression(mixOk, datesOk));
    }

    [Fact]
    public void StepContract()
    {
        RunSta(() =>
        {
            using var step = new StepCorpusSettings();
            Assert.Equal("Corpus Settings", step.StepTitle);
            Assert.Equal("Next >", step.NextButtonText);
            Assert.True(step.CanMoveNext);
            Assert.True(step.CanMoveBack);
        });
    }

    [Fact]
    public void Enter_CreatesDefaultsFromComplexityAndFacts()
    {
        RunSta(() =>
        {
            var draft = CorpusTestData.RichDraft();
            draft.Complexity = ComplexityLevel.Complex;
            draft.Corpus = null;
            var state = new WizardState { Case = draft, CaseStore = TempStore() };

            using var step = new StepCorpusSettings();
            step.LoadForRender(state);

            var settings = draft.Corpus!.Settings;
            Assert.Equal(2_500, settings.TotalEmails);
            Assert.Equal(CorpusSettings.DefaultRelevantPercent, settings.RelevantPercent);
            var (start, end) = CorpusSettings.DefaultWindow(draft.Guide);
            Assert.Equal(start, settings.WindowStart);
            Assert.Equal(end, settings.WindowEnd);
            Assert.True(step.SizeToggles[2].Selected);
        });
    }

    [Fact]
    public void Commits_ClampSizeOverridesAndWindow()
    {
        RunSta(() =>
        {
            var draft = CorpusTestData.RichDraft();
            var state = new WizardState { Case = draft, CaseStore = TempStore() };
            using var step = new StepCorpusSettings();
            step.LoadForRender(state);
            var settings = draft.Corpus!.Settings;
            var range = CorpusSettings.FactRange(draft.Guide)!.Value;

            step.HotField.Text = "9";
            step.StartField.Text = range.First.AddDays(10).ToString("yyyy-MM-dd");
            step.PercentField.Text = "75";
            step.FlushCommits();

            Assert.Equal(CorpusSettings.MaxHotPerHotFact, settings.HotPerHotFact);
            Assert.Equal("5", step.HotField.Text);
            Assert.Equal(range.First.ToString("yyyy-MM-dd"), settings.WindowStart);
            Assert.Equal(CorpusSettings.MaxRelevantPercent, settings.RelevantPercent);
            Assert.Equal(CorpusSettings.MaxRelevantPercent, step.Slider.Value);
        });
    }

    [Fact]
    public void InvalidDate_IsNotCommitted()
    {
        RunSta(() =>
        {
            var draft = CorpusTestData.RichDraft();
            var state = new WizardState { Case = draft, CaseStore = TempStore() };
            using var step = new StepCorpusSettings();
            step.LoadForRender(state);
            var before = draft.Corpus!.Settings.WindowStart;

            step.StartField.Text = "2024-13-45";
            step.FlushCommits();

            Assert.Equal(before, draft.Corpus.Settings.WindowStart);
        });
    }

    [Fact]
    public void MediaChoice_KeptWithoutKey()
    {
        RunSta(() =>
        {
            var draft = CorpusTestData.RichDraft();
            var state = new WizardState { Case = draft, CaseStore = TempStore() };
            using var step = new StepCorpusSettings();
            step.LoadForRender(state);

            Assert.False(state.IsMediaAvailable);
            Assert.False(step.ImagesBox.Enabled);
            Assert.False(step.ImagesBox.Checked);
            Assert.True(draft.Corpus!.Settings.IncludeImages);
            Assert.True(draft.Corpus.Settings.IncludeVoicemail);
        });
    }

    private static CaseStore TempStore()
    {
        var root = Path.Combine(Path.GetTempPath(), "rd-corpus-step-" + Guid.NewGuid().ToString("N"));
        return new CaseStore(Path.Combine(root, "Cases"), Path.Combine(root, "AppData"));
    }

    private static void RunSta(Action action)
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                error = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (error != null)
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error).Throw();
    }
}
