using System.Globalization;
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

    // --- Cache and batch price columns (07-UI-SPEC S5, D-18) ---

    [Fact]
    public void Grid_ColumnOrder_IncludesCacheAndBatchColumns()
    {
        RenderHarness.RunSta(() =>
        {
            using var dialog = new ModelConfigurationDialog(AIModelConfig.GetDefaultModels());
            var names = dialog.ModelsGrid.Columns.Cast<DataGridViewColumn>()
                .OrderBy(c => c.DisplayIndex).Select(c => c.Name).ToArray();
            Assert.Equal(new[]
            {
                "Provider", "ModelId", "DisplayName", "InputPrice", "OutputPrice", "CacheWritePrice",
                "CacheReadPrice", "BatchDiscount", "DefaultForTier", "Effort", "MaxOutputTokens"
            }, names);
            Assert.Equal("Cache Write $/M", dialog.ModelsGrid.Columns["CacheWritePrice"]!.HeaderText);
            Assert.Equal("Cache Read $/M", dialog.ModelsGrid.Columns["CacheReadPrice"]!.HeaderText);
            Assert.Equal("Batch Discount %", dialog.ModelsGrid.Columns["BatchDiscount"]!.HeaderText);
            Assert.Equal("0.00#", dialog.ModelsGrid.Columns["CacheWritePrice"]!.DefaultCellStyle.Format);
            Assert.Equal("0", dialog.ModelsGrid.Columns["BatchDiscount"]!.DefaultCellStyle.Format);
            Assert.True(dialog.ModelsGrid.Columns["Provider"]!.Frozen);
            Assert.True(dialog.ModelsGrid.Columns["ModelId"]!.Frozen);
            Assert.Equal(DataGridViewTriState.True, dialog.ModelsGrid.ColumnHeadersDefaultCellStyle.WrapMode);
            Assert.Equal(new Size(1180, 480), ModelConfigurationDialog.LogicalSize);
        });
    }

    [Fact]
    public void Clone_CopiesCacheAndBatchProperties_OriginalUntouched()
    {
        RenderHarness.RunSta(() =>
        {
            var original = AIModelConfig.GetDefaultModels();
            var opus = original.Single(m => m.ModelId == "claude-opus-5-5");
            opus.MinCacheablePrefixTokens = 777;
            using var dialog = new ModelConfigurationDialog(original);

            var copy = dialog.Models.Single(m => m.ModelId == "claude-opus-5-5");
            Assert.NotSame(opus, copy);
            Assert.Equal(5.00m, copy.CacheWriteTokenPricePerMillion);
            Assert.Equal(0.20m, copy.CacheReadTokenPricePerMillion);
            Assert.Equal(50m, copy.BatchDiscountPercent);
            Assert.Equal(777, copy.MinCacheablePrefixTokens);

            copy.CacheReadTokenPricePerMillion = 0.05m;
            Assert.Equal(0.20m, opus.CacheReadTokenPricePerMillion);
        });
    }

    [Fact]
    public void EditCacheRead_UpdatesWorkingCopy_NotTheOriginal()
    {
        RenderHarness.RunSta(() =>
        {
            var original = AIModelConfig.GetDefaultModels();
            using var dialog = ShowHidden(new ModelConfigurationDialog(original));
            var row = OpusRow(dialog);

            var committed = TypeIntoCell(dialog, row, "CacheReadPrice", 0.05m.ToString(CultureInfo.CurrentCulture));

            Assert.True(committed);
            Assert.Equal(0.05m, dialog.Models.Single(m => m.ModelId == "claude-opus-5-5").CacheReadTokenPricePerMillion);
            Assert.Equal(0.20m, original.Single(m => m.ModelId == "claude-opus-5-5").CacheReadTokenPricePerMillion);
            Assert.True(dialog.Visible);
        });
    }

    [Theory]
    [InlineData("CacheWritePrice", "abc")]
    [InlineData("CacheWritePrice", "1001")]
    [InlineData("CacheWritePrice", "-1")]
    [InlineData("CacheReadPrice", "abc")]
    [InlineData("BatchDiscount", "101")]
    public void BadEntry_RevertsCell_NoMessageBox(string column, string text)
    {
        RenderHarness.RunSta(() =>
        {
            using var dialog = ShowHidden(new ModelConfigurationDialog(AIModelConfig.GetDefaultModels()));
            var row = OpusRow(dialog);
            var model = dialog.Models.Single(m => m.ModelId == "claude-opus-5-5");

            // A MessageBox would block this STA thread; reaching the asserts proves there is none
            TypeIntoCell(dialog, row, column, text);

            Assert.Equal(5.00m, model.CacheWriteTokenPricePerMillion);
            Assert.Equal(0.20m, model.CacheReadTokenPricePerMillion);
            Assert.Equal(50m, model.BatchDiscountPercent);
            Assert.False(dialog.ModelsGrid.IsCurrentCellInEditMode);
            Assert.True(dialog.Visible);
        });
    }

    [Theory]
    [InlineData("0", 1000, true)]
    [InlineData("1000", 1000, true)]
    [InlineData("1000.01", 1000, false)]
    [InlineData("-0.01", 1000, false)]
    [InlineData("abc", 1000, false)]
    [InlineData("", 100, false)]
    [InlineData("100", 100, true)]
    [InlineData("101", 100, false)]
    public void IsValidPrice_Bounds(string text, int max, bool expected)
    {
        var current = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        try
        {
            Assert.Equal(expected, ModelConfigurationDialog.IsValidPrice(text, max));
        }
        finally
        {
            CultureInfo.CurrentCulture = current;
        }
    }

    [Fact]
    public void ResetDefaults_RestoresCacheAndBatchPrices()
    {
        RenderHarness.RunSta(() =>
        {
            var models = AIModelConfig.GetDefaultModels();
            var opus = models.Single(m => m.ModelId == "claude-opus-5-5");
            opus.CacheWriteTokenPricePerMillion = 9m;
            opus.CacheReadTokenPricePerMillion = 9m;
            opus.BatchDiscountPercent = 10m;
            opus.InputTokenPricePerMillion = 99m;
            using var dialog = new ModelConfigurationDialog(models);

            dialog.ResetDefaultsForTest();

            var reset = dialog.Models.Single(m => m.ModelId == "claude-opus-5-5");
            Assert.Equal(5.00m, reset.CacheWriteTokenPricePerMillion);
            Assert.Equal(0.20m, reset.CacheReadTokenPricePerMillion);
            Assert.Equal(50m, reset.BatchDiscountPercent);
            Assert.Equal(4.00m, reset.InputTokenPricePerMillion);
            Assert.Equal(0.006m, dialog.ImagePriceInput.Value);
        });
    }

    private static ModelConfigurationDialog ShowHidden(ModelConfigurationDialog dialog)
    {
        dialog.Opacity = 0;
        dialog.ShowInTaskbar = false;
        dialog.Show();
        Application.DoEvents();
        return dialog;
    }

    private static DataGridViewRow OpusRow(ModelConfigurationDialog dialog) =>
        dialog.ModelsGrid.Rows.Cast<DataGridViewRow>()
            .Single(r => ((AIModelConfig)r.DataBoundItem!).ModelId == "claude-opus-5-5");

    // Types into a cell and leaves it the way a user would (EndEdit runs CellValidating and DataError)
    private static bool TypeIntoCell(ModelConfigurationDialog dialog, DataGridViewRow row, string column, string text)
    {
        var grid = dialog.ModelsGrid;
        grid.Focus();
        grid.CurrentCell = row.Cells[grid.Columns[column]!.Index];
        Assert.True(grid.BeginEdit(selectAll: true));
        grid.EditingControl!.Text = text;
        grid.NotifyCurrentCellDirty(true);
        var committed = grid.EndEdit();
        Application.DoEvents();
        return committed;
    }
}
