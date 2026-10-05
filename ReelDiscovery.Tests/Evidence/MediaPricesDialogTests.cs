using ReelDiscovery.Models;
using ReelDiscovery.Tests.Render;
using ReelDiscovery.UserControls;

namespace ReelDiscovery.Tests.Evidence;

/// <summary>
/// Cancel, Reset Defaults, OK and clone semantics of the media prices in ModelConfigurationDialog (06-UI-SPEC S4, D-29).
/// The dialog is constructed on an STA thread and never shown modally.
/// </summary>
public class MediaPricesDialogTests
{
    [Fact]
    public void ApplyDialogResult_Cancel_ReturnsCurrentUnchanged()
    {
        var current = new MediaPrices { ImagePerUnit = 0.02m, TtsPerMillionChars = 30m };
        var edited = new MediaPrices { ImagePerUnit = 0.5m, TtsPerMillionChars = 99m };

        var stored = StepApiConfiguration.ApplyDialogResult(DialogResult.Cancel, current, edited);

        Assert.Same(current, stored);
        Assert.Equal(0.02m, current.ImagePerUnit);
        Assert.Equal(30m, current.TtsPerMillionChars);
    }

    [Fact]
    public void ApplyDialogResult_Ok_ReturnsNormalizedCopy_WithoutMutatingInputs()
    {
        var current = new MediaPrices { ImagePerUnit = 0.02m, TtsPerMillionChars = 30m };
        var edited = new MediaPrices { ImagePerUnit = 12m, TtsPerMillionChars = -3m };

        var stored = StepApiConfiguration.ApplyDialogResult(DialogResult.OK, current, edited);

        Assert.NotSame(current, stored);
        Assert.NotSame(edited, stored);
        Assert.Equal(10m, stored.ImagePerUnit);
        Assert.Equal(0m, stored.TtsPerMillionChars);
        Assert.Equal(12m, edited.ImagePerUnit);
        Assert.Equal(-3m, edited.TtsPerMillionChars);
        Assert.Equal(0.02m, current.ImagePerUnit);
        Assert.Equal(30m, current.TtsPerMillionChars);
    }

    [Fact]
    public void Dialog_EditsACopy_PassedPricesUnchangedBeforeOk()
    {
        RenderHarness.RunSta(() =>
        {
            var passed = new MediaPrices { ImagePerUnit = 0.01m, TtsPerMillionChars = 20m };
            using var dialog = new ModelConfigurationDialog(AIModelConfig.GetDefaultModels(), passed);

            Assert.Equal(0.01m, dialog.ImagePriceInput.Value);
            Assert.Equal(20m, dialog.TtsPriceInput.Value);

            dialog.ImagePriceInput.Value = 0.25m;
            dialog.TtsPriceInput.Value = 42.5m;

            Assert.Equal(0.01m, passed.ImagePerUnit);
            Assert.Equal(20m, passed.TtsPerMillionChars);
            Assert.NotSame(passed, dialog.MediaPrices);
            // Nothing is committed to the dialog's MediaPrices until OK
            Assert.Equal(0.01m, dialog.MediaPrices.ImagePerUnit);
            Assert.Equal(20m, dialog.MediaPrices.TtsPerMillionChars);
        });
    }

    [Fact]
    public void ResetDefaults_RestoresDefaultPrices_AndOkStoresThem()
    {
        RenderHarness.RunSta(() =>
        {
            using var dialog = new ModelConfigurationDialog(AIModelConfig.GetDefaultModels(),
                new MediaPrices { ImagePerUnit = 0.5m, TtsPerMillionChars = 99m });

            dialog.ResetDefaultsForTest();

            Assert.Equal(0.006m, dialog.ImagePriceInput.Value);
            Assert.Equal(15.00m, dialog.TtsPriceInput.Value);

            dialog.CommitMediaPricesForTest();

            Assert.Equal(0.006m, dialog.MediaPrices.ImagePerUnit);
            Assert.Equal(15.00m, dialog.MediaPrices.TtsPerMillionChars);
        });
    }
}
