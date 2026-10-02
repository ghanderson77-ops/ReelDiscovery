using ReelDiscovery.Forms;
using ReelDiscovery.Helpers;
using ReelDiscovery.Services.Corpus;
using ReelDiscovery.UserControls;

namespace ReelDiscovery.Tests.Render;

/// <summary>
/// Renders the Phase 4 building blocks (D-RP ReplanDialog, StatTile row, PercentSlider, MonthHistogram) at the
/// display's scaled DPI (144 reference) and checks nothing clips.
/// </summary>
[Trait("Category", "Render")]
[Collection(RenderCollection.Name)]
public class CorpusControlsRenderTests
{
    private static readonly TileContent[] Tiles =
    {
        new() { Caption = "Emails", Value = "1,000", SubLine = "14 custodians", Tooltip = "1,000 planned emails across 14 custodian mailboxes" },
        new() { Caption = "Threads", Value = "312", SubLine = "avg 3.2 emails" },
        new() { Caption = "Relevant", Value = "19.6%", SubLine = "196 emails", Tooltip = "196 relevant and 804 non-relevant emails" },
        new() { Caption = "Hot", Value = "3", SubLine = "2 hot facts", Tooltip = "Hot documents follow the facts marked Hot in the case guide." },
        new() { Caption = "Privileged", Value = "26", SubLine = "61 emails", Tooltip = "Privileged threads include in-house or outside counsel." },
        new() { Caption = "Attachments", Value = "240", SubLine = "on 20% of emails", Tooltip = "180 documents, 40 calendar invites, 20 images" },
        new() { Caption = "Est. cost", Value = "≈$130", SubLine = "to generate, ±50%", Tooltip = "Rough estimate for writing this corpus." }
    };

    [Theory]
    [InlineData(false, "replan-dialog-unapproved")]
    [InlineData(true, "replan-dialog-approved")]
    public void ReplanDialog_RendersWithoutClipping(bool approved, string name)
    {
        RenderHarness.RunSta(() =>
        {
            var height = 0;
            var workingHeight = 0;
            var activeIsGuidance = false;
            var result = RenderHarness.RenderForm(() => new ReplanDialog(approved), name, form =>
            {
                height = form.ClientSize.Height;
                workingHeight = Screen.FromControl(form).WorkingArea.Height;
                activeIsGuidance = form.ActiveControl is TextBox box && box.Multiline;
                Assert.Contains(Descendants(form).OfType<Button>(), b => b.Text == "Re-plan Corpus");
                Assert.Contains(Descendants(form).OfType<Label>(), l => l.Text == CorpusUiText.ReplanBody(approved));
            });

            Assert.True(result.DeviceDpi >= RenderHarness.MinimumDpi, $"DPI {result.DeviceDpi}");
            Assert.True(File.Exists(result.PngPath));
            Assert.Empty(result.Clipping);
            Assert.True(height > 0 && height < workingHeight,
                $"Dialog client height {height} must fit the working area {workingHeight}");
            Assert.True(activeIsGuidance, "The guidance box must have initial focus");
        });
    }

    [Fact]
    public void Controls_RenderWithoutClipping_Large()
    {
        RenderControls(RenderHarness.Large);
    }

    [Fact]
    public void Controls_RenderWithoutClipping_WizardMin()
    {
        RenderControls(RenderHarness.WizardMin);
    }

    [Fact]
    public void Histogram_NineBuckets_RendersWithoutClipping()
    {
        RenderHarness.RunSta(() =>
        {
            var buckets = Buckets(new DateOnly(2024, 1, 1), 9, seed: 7);
            var result = RenderHarness.RenderControl(() => new UserControl { BackColor = Theme.Background },
                "corpus-controls-histogram-9", RenderHarness.Large, owner =>
                {
                    var card = HistogramCard(owner, buckets);
                    card.Dock = DockStyle.Top;
                    card.Height = owner.LogicalToDeviceUnits(320);
                    owner.Controls.Add(card);
                });

            Assert.True(result.DeviceDpi >= RenderHarness.MinimumDpi, $"DPI {result.DeviceDpi}");
            Assert.True(File.Exists(result.PngPath));
            Assert.Empty(result.Clipping);
        });
    }

    private static void RenderControls(Size size)
    {
        RenderHarness.RunSta(() =>
        {
            var ellipsized = new List<string>();
            var result = RenderHarness.RenderControl(() => new UserControl { BackColor = Theme.Background },
                "corpus-controls", size, owner =>
                {
                    Build(owner);
                    owner.PerformLayout();
                    Descendants(owner).OfType<PercentSlider>().Single().Focus();
                    if (size == RenderHarness.Large)
                    {
                        foreach (var tile in Descendants(owner).OfType<StatTile>())
                        {
                            var value = Descendants(tile).OfType<Label>().Single(l => l.Text == tile.ValueText);
                            if (value.PreferredWidth > value.ClientSize.Width)
                                ellipsized.Add(tile.ValueText);
                        }
                    }
                });

            Assert.True(result.DeviceDpi >= RenderHarness.MinimumDpi, $"DPI {result.DeviceDpi}");
            Assert.True(File.Exists(result.PngPath));
            Assert.Empty(result.Clipping);
            Assert.Empty(ellipsized);
        });
    }

    private static void Build(Control owner)
    {
        int L(int value) => owner.LogicalToDeviceUnits(value);

        var root = new TableLayoutPanel
        {
            Name = "root",
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            BackColor = Theme.Background,
            Margin = Padding.Empty,
            Padding = new Padding(0, 0, 0, L(Theme.PageMargin))
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        // Tiles: 7 equal columns, 4 px margins between (outer edges flush with the card below)
        var tileRow = new TableLayoutPanel
        {
            Name = "tiles",
            Dock = DockStyle.Fill,
            ColumnCount = Tiles.Length,
            RowCount = 1,
            BackColor = Theme.Background,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };
        var tileHeight = 0;
        for (var i = 0; i < Tiles.Length; i++)
        {
            tileRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F / Tiles.Length));
            var tile = new StatTile
            {
                Name = $"tile{i}",
                Dock = DockStyle.Fill,
                Margin = new Padding(i == 0 ? 0 : L(4), 0, i == Tiles.Length - 1 ? 0 : L(4), 0)
            };
            tile.SetContent(Tiles[i]);
            tileHeight = Math.Max(tileHeight, tile.Height);
            tileRow.Controls.Add(tile, i, 0);
        }
        tileRow.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        // Slider on a white card, like the Corpus Settings slider row
        var sliderCard = new CardPanel
        {
            Name = "sliderCard",
            Dock = DockStyle.Fill,
            Padding = new Padding(L(Theme.CardPadding), L(12), L(Theme.CardPadding), L(12)),
            Margin = Padding.Empty
        };
        var slider = new PercentSlider { Name = "slider", Dock = DockStyle.Fill, Value = 20 };
        sliderCard.Controls.Add(slider);

        var histogramCard = HistogramCard(owner, Buckets(new DateOnly(2021, 1, 1), 51, seed: 42));
        histogramCard.Dock = DockStyle.Fill;
        histogramCard.Margin = Padding.Empty;

        root.RowStyles.Add(new RowStyle(SizeType.Absolute, tileHeight + L(Theme.Gap))); // 0: Tiles
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, L(28) + L(24) + L(Theme.Gap))); // 1: Slider card
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); // 2: Histogram card
        tileRow.Margin = new Padding(0, 0, 0, L(Theme.Gap));
        sliderCard.Margin = new Padding(0, 0, 0, L(Theme.Gap));
        root.Controls.Add(tileRow, 0, 0);
        root.Controls.Add(sliderCard, 0, 1);
        root.Controls.Add(histogramCard, 0, 2);
        owner.Controls.Add(root);
    }

    private static CardPanel HistogramCard(Control owner, IReadOnlyList<MonthBucket> buckets)
    {
        int L(int value) => owner.LogicalToDeviceUnits(value);

        var card = new CardPanel { Name = "histogramCard", Padding = new Padding(1) };
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            BackColor = Theme.Surface,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // 0: Card header
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); // 1: Histogram

        var caption = new Label
        {
            Text = "Emails by month",
            AutoSize = true,
            Font = Theme.LabelFont(),
            ForeColor = Theme.Text,
            BackColor = Theme.Surface,
            UseMnemonic = false,
            Margin = new Padding(L(15), L(14), L(15), L(10))
        };
        var histogram = new MonthHistogram
        {
            Name = "histogram",
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 0, 0, L(6)),
            Buckets = buckets
        };
        layout.Controls.Add(caption, 0, 0);
        layout.Controls.Add(histogram, 0, 1);
        card.Controls.Add(layout);
        return card;
    }

    private static IReadOnlyList<MonthBucket> Buckets(DateOnly start, int count, int seed)
    {
        var random = new Random(seed);
        var buckets = new List<MonthBucket>();
        for (var i = 0; i < count; i++)
        {
            var total = random.Next(0, 140);
            var relevant = (int)(total * random.NextDouble() * 0.45);
            buckets.Add(new MonthBucket { Month = start.AddMonths(i), Relevant = relevant, NonRelevant = total - relevant });
        }
        // The tallest month: 166 emails (nice axis max 200)
        buckets[count * 2 / 3] = new MonthBucket { Month = start.AddMonths(count * 2 / 3), Relevant = 46, NonRelevant = 120 };
        return buckets;
    }

    private static IEnumerable<Control> Descendants(Control root)
    {
        foreach (Control child in root.Controls)
        {
            yield return child;
            foreach (var nested in Descendants(child))
                yield return nested;
        }
    }
}
