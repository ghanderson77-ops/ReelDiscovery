using ReelDiscovery.Helpers;
using ReelDiscovery.Services.Corpus;
using ReelDiscovery.UserControls;

namespace ReelDiscovery.Tests.Corpus;

/// <summary>
/// Pure behavior of the Phase 4 controls (PercentSlider, MonthHistogram helpers, StatTile), constructed
/// without being shown.
/// </summary>
public class CorpusControlsTests
{
    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 1)]
    [InlineData(2, 2)]
    [InlineData(3, 5)]
    [InlineData(7, 10)]
    [InlineData(21, 25)]
    [InlineData(46, 50)]
    [InlineData(120, 200)]
    [InlineData(166, 200)]
    [InlineData(200, 200)]
    [InlineData(201, 250)]
    [InlineData(210, 250)]
    [InlineData(250, 250)]
    [InlineData(251, 500)]
    [InlineData(501, 1000)]
    [InlineData(2100, 2500)]
    public void NiceAxisMax_RoundsUpToOneTwoTwoAndAHalfFive(int max, int expected)
    {
        Assert.Equal(expected, MonthHistogram.NiceAxisMax(max));
    }

    [Theory]
    [InlineData(40, 30, 1)]
    [InlineData(20, 30, 2)]
    [InlineData(13, 30, 3)]
    [InlineData(12, 30, 6)]
    [InlineData(2, 30, 12)]
    [InlineData(0, 30, 12)]
    public void HistogramLabelStep_PicksSmallestStepThatFits(int bucketWidth, int labelWidth, int expected)
    {
        Assert.Equal(expected, MonthHistogram.HistogramLabelStep(bucketWidth, labelWidth));
    }

    [Fact]
    public void HistogramTooltip_SpellsOutTheMonth()
    {
        var bucket = new MonthBucket { Month = new DateOnly(2024, 3, 1), Relevant = 46, NonRelevant = 120 };
        Assert.Equal("March 2024: 46 relevant, 120 not relevant (166 emails)", MonthHistogram.TooltipFor(bucket));
    }

    [Fact]
    public void Histogram_AcceptsEmptyAndNullBuckets()
    {
        using var histogram = new MonthHistogram();
        histogram.Buckets = null!;
        Assert.Empty(histogram.Buckets);
    }

    [Fact]
    public void Slider_DefaultsAndClamps()
    {
        using var slider = new PercentSlider();
        Assert.Equal(5, slider.Minimum);
        Assert.Equal(60, slider.Maximum);
        Assert.True(slider.TabStop);

        slider.Value = 200;
        Assert.Equal(60, slider.Value);
        slider.Value = -3;
        Assert.Equal(5, slider.Value);
    }

    [Fact]
    public void Slider_ValueFromX_MapsThumbTravelEnds()
    {
        using var slider = new PercentSlider { Width = 400 };
        var inset = slider.LogicalToDeviceUnits(8);

        Assert.Equal(5, slider.ValueFromX(inset));
        Assert.Equal(60, slider.ValueFromX(slider.Width - inset));
        Assert.Equal(5, slider.ValueFromX(0));
        Assert.Equal(60, slider.ValueFromX(slider.Width));
        var mid = slider.ValueFromX(slider.Width / 2);
        Assert.InRange(mid, 32, 33);
    }

    [Fact]
    public void Slider_HandleKey_StepsAndJumps()
    {
        using var slider = new PercentSlider { Value = 20 };

        Assert.True(slider.HandleKey(Keys.Right));
        Assert.Equal(21, slider.Value);
        Assert.True(slider.HandleKey(Keys.Left));
        Assert.Equal(20, slider.Value);
        Assert.True(slider.HandleKey(Keys.PageUp));
        Assert.Equal(25, slider.Value);
        Assert.True(slider.HandleKey(Keys.PageDown));
        Assert.Equal(20, slider.Value);
        Assert.True(slider.HandleKey(Keys.Home));
        Assert.Equal(5, slider.Value);
        Assert.True(slider.HandleKey(Keys.End));
        Assert.Equal(60, slider.Value);
        Assert.False(slider.HandleKey(Keys.A));
    }

    [Fact]
    public void Slider_ValueChanged_OnlyWhenValueChanges()
    {
        using var slider = new PercentSlider { Value = 20 };
        var changed = 0;
        slider.ValueChanged += (_, _) => changed++;

        slider.Value = 20;
        Assert.Equal(0, changed);
        slider.Value = 21;
        Assert.Equal(1, changed);
        slider.HandleKey(Keys.End);
        slider.HandleKey(Keys.End);
        slider.HandleKey(Keys.Right);
        Assert.Equal(2, changed);
    }

    [Fact]
    public void Slider_ValueCommitted_OnCommitOnly()
    {
        using var slider = new PercentSlider { Value = 20 };
        var committed = 0;
        slider.ValueCommitted += (_, _) => committed++;

        slider.HandleKey(Keys.Right);
        slider.Value = 30;
        Assert.Equal(0, committed);
        slider.Commit();
        Assert.Equal(1, committed);
    }

    [Fact]
    public void StatTile_SetContent_FillsLabelsAndTooltip()
    {
        using var tile = new StatTile();
        tile.SetContent(new TileContent
        {
            Caption = "Relevant",
            Value = "19.6%",
            SubLine = "312 emails",
            Tooltip = "312 relevant and 1,280 non-relevant emails"
        });

        Assert.Equal("Relevant", tile.CaptionText);
        Assert.Equal("19.6%", tile.ValueText);
        Assert.Equal("312 emails", tile.SubLineText);
        Assert.StartsWith("Relevant: 19.6% (312 emails)", tile.TooltipText);
        Assert.Contains("312 relevant and 1,280 non-relevant emails", tile.TooltipText);
    }

    [Fact]
    public void StatTile_SetContent_WithoutExtraTooltip()
    {
        using var tile = new StatTile();
        tile.SetContent(new TileContent { Caption = "Hot", Value = "3", SubLine = "2 hot facts" });

        Assert.Equal("Hot: 3 (2 hot facts)", tile.TooltipText);
        Assert.True(tile.Height > 0);
    }
}
