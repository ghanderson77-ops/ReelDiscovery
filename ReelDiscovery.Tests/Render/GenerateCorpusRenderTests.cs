using ReelDiscovery.Helpers;
using ReelDiscovery.Models;
using ReelDiscovery.Models.Case;
using ReelDiscovery.Models.Corpus;
using ReelDiscovery.Models.Evidence;
using ReelDiscovery.Services.Case;
using ReelDiscovery.Services.Evidence;
using ReelDiscovery.Tests.Evidence;
using ReelDiscovery.UserControls;

namespace ReelDiscovery.Tests.Render;

/// <summary>
/// Renders the Generate Corpus step at 144 DPI (05-UI-SPEC S1 render states 1-6, 06-UI-SPEC S1 render checks 1-7) at
/// Large and WizardMin from seeded GenerationView snapshots. No run is started.
/// </summary>
[Trait("Category", "Render")]
[Collection(RenderCollection.Name)]
public class GenerateCorpusRenderTests
{
    // ----- States 1-6 -----

    [Fact]
    public void Ready()
    {
        RenderBoth("generate-corpus-ready", GenerateCorpusRenderState.Ready, null, (step, labels) =>
        {
            Assert.True(step.StateCardShown);
            Assert.Equal("Ready to write the corpus", step.StateHeading);
            Assert.DoesNotContain("Estimated cost", step.StateBody);
            Assert.True(step.EstimateLabel.Visible);
            Assert.StartsWith("Estimated cost ≈$", step.EstimateLabel.Text);
            Assert.False(step.MediaNoticeLabel.Visible);
            Assert.True(step.StateActionButton.Visible);
            Assert.Equal("Generate Emails", step.StateActionButton.Text);
            Assert.Equal(ButtonStyle.Primary, ((ThemedButton)step.StateActionButton).Variant);
            Assert.False(step.Tiles[0].Visible);
            Assert.True(step.NoticeLabel.Visible);
            Assert.StartsWith("This step writes the ", step.NoticeLabel.Text);
            Assert.False(step.CancelButton.Visible);
        }, primary: 1);
    }

    [Fact]
    public void Running()
    {
        var view = RunningView();
        RenderBoth("generate-corpus-running", GenerateCorpusRenderState.Running, view, (step, labels) =>
        {
            Assert.True(step.CancelButton.Visible);
            Assert.True(step.ProgressBar.Visible);
            Assert.False(step.StartOverButton.Visible);
            Assert.False(step.RetryFailedButton.Visible);
            Assert.False(step.BannerPanel.Visible);
            Assert.Equal(GenerationUiText.ProgressText(view), step.StatusLabel.Text);
            Assert.Equal(Theme.Accent, step.StatusLabel.ForeColor);
            Assert.Equal(40, step.ActivityGrid.Rows.Count);
            Assert.Equal(14, step.WavesGrid.Rows.Count);
            // Newest first
            Assert.Equal(GenerationUiText.ActivityText(view.Events[^1]).Text, step.ActivityGrid.Rows[0].Cells["Event"].Value);
            Assert.Equal(GenerationUiText.ActivityText(view.Events[0]).Text, step.ActivityGrid.Rows[39].Cells["Event"].Value);
            var percent = step.ProgressBar.Value * 100 / step.ProgressBar.Maximum;
            Assert.InRange(percent, 25, 35);
            Assert.False(step.CanMoveNext);
        }, primary: 0);
    }

    [Fact]
    public void Partial()
    {
        var view = FinishedView(done: 21, total: 60, warnings: 1, failed: 2, cancelled: true);
        RenderBoth("generate-corpus-partial", GenerateCorpusRenderState.Partial, view, (step, labels) =>
        {
            Assert.True(step.BannerPanel.Visible);
            Assert.Contains(GenerationUiText.BannerHeading(runEnded: false), labels);
            Assert.Contains(GenerationUiText.BannerBody(21, 60, 0, 0, 2), labels);
            Assert.True(step.ContinueButton.Visible);
            Assert.True(step.StartOverButton.Visible);
            Assert.False(step.RetryFailedButton.Visible);
            Assert.Equal(Theme.Warning, step.StatusLabel.ForeColor);
        }, primary: 1);
    }

    [Fact]
    public void FinishedClean()
    {
        var view = FinishedView(done: 60, total: 60, warnings: 0, failed: 0, cancelled: false);
        RenderBoth("generate-corpus-finished-clean", GenerateCorpusRenderState.FinishedClean, view, (step, labels) =>
        {
            Assert.False(step.BannerPanel.Visible);
            Assert.True(step.StartOverButton.Visible);
            Assert.False(step.RetryFailedButton.Visible);
            Assert.Equal("0", step.Tiles[4].ValueText);
            Assert.Equal("0", step.Tiles[5].ValueText);
            Assert.Equal(Theme.Text, step.Tiles[4].ValueColor);
            Assert.Equal(Theme.Text, step.Tiles[5].ValueColor);
            Assert.Equal(Theme.Success, step.StatusLabel.ForeColor);
        }, primary: 0);
    }

    [Fact]
    public void FinishedWithIssues()
    {
        var view = FinishedView(done: 58, total: 60, warnings: 3, failed: 2, cancelled: false);
        RenderBoth("generate-corpus-finished-issues", GenerateCorpusRenderState.FinishedIssues, view, (step, labels) =>
        {
            Assert.True(step.StartOverButton.Visible);
            Assert.True(step.RetryFailedButton.Visible);
            Assert.False(step.BannerPanel.Visible);
            Assert.Equal("3", step.Tiles[4].ValueText);
            Assert.Equal("2", step.Tiles[5].ValueText);
            Assert.Equal(Theme.Warning, step.Tiles[4].ValueColor);
            Assert.Equal(Theme.Danger, step.Tiles[5].ValueColor);
            Assert.Equal(Theme.Warning, step.StatusLabel.ForeColor);
        }, primary: 1);
    }

    [Fact]
    public void NotApprovedCard()
    {
        RenderBoth("generate-corpus-not-approved", GenerateCorpusRenderState.NotApproved, null, (step, labels) =>
        {
            Assert.Equal("Approve the plan first", step.StateHeading);
            Assert.Equal("Review Plan", step.StateActionButton.Text);
            Assert.Equal(ButtonStyle.Primary, ((ThemedButton)step.StateActionButton).Variant);
            Assert.False(step.NoticeLabel.Visible);
        }, primary: 1);
    }

    [Fact]
    public void PlanChangedCard()
    {
        RenderBoth("generate-corpus-plan-changed", GenerateCorpusRenderState.PlanChanged, null, (step, labels) =>
        {
            Assert.Equal("The plan changed after emails were generated", step.StateHeading);
            Assert.StartsWith("12 threads were written for an earlier plan", step.StateBody);
            Assert.Equal("Start Over...", step.StateActionButton.Text);
            Assert.Equal(ButtonStyle.Danger, ((ThemedButton)step.StateActionButton).Variant);
        }, earlierThreads: 12, primary: 0);
    }

    [Fact]
    public void UnreadableCard()
    {
        RenderBoth("generate-corpus-unreadable", GenerateCorpusRenderState.Unreadable, null, (step, labels) =>
        {
            Assert.Equal("The saved run could not be opened", step.StateHeading);
            Assert.StartsWith("The saved run could not be read: the file is locked by another program.", step.StateBody);
            Assert.Equal(ButtonStyle.Danger, ((ThemedButton)step.StateActionButton).Variant);
        }, reason: "The saved run could not be read: the file is locked by another program.", primary: 0);
    }

    // ----- Phase 6 render checks 1-7 (06-UI-SPEC S1) -----

    [Fact]
    public void ReadyWithMediaEstimate()
    {
        var ready = ReadyInput(mediaAvailable: true);
        var card = GenerationUiText.ReadyCard(ready);
        RenderBoth("generate-corpus-ready-media", GenerateCorpusRenderState.ReadyWithMedia, null, (step, labels) =>
        {
            Assert.True(step.StateCardShown);
            Assert.Equal(card.Body, step.StateBody);
            Assert.True(step.EstimateLabel.Visible);
            Assert.Equal(card.EstimateLine, step.EstimateLabel.Text);
            Assert.Contains("· images and voicemail ≈$", step.EstimateLabel.Text);
            Assert.Equal(Theme.TextMuted, step.EstimateLabel.ForeColor);
            Assert.False(step.MediaNoticeLabel.Visible);
            // The last line carries the 20 bottom margin
            Assert.Equal(step.LogicalToDeviceUnits(20), step.EstimateLabel.Margin.Bottom);
            Assert.Equal("Generate Emails", step.StateActionButton.Text);
        }, primary: 1, ready: ready);
    }

    [Fact]
    public void ReadyMediaUnavailableNotice()
    {
        var ready = ReadyInput(mediaAvailable: false);
        var card = GenerationUiText.ReadyCard(ready);
        RenderBoth("generate-corpus-ready-media-unavailable", GenerateCorpusRenderState.ReadyMediaUnavailable, null, (step, labels) =>
        {
            Assert.True(step.EstimateLabel.Visible);
            Assert.True(step.MediaNoticeLabel.Visible);
            Assert.Equal(card.MediaNotice, step.MediaNoticeLabel.Text);
            Assert.Equal(Theme.Warning, step.MediaNoticeLabel.ForeColor);
            // Estimate bottom 4 when the notice follows; the notice takes the 20
            Assert.Equal(step.LogicalToDeviceUnits(4), step.EstimateLabel.Margin.Bottom);
            Assert.Equal(step.LogicalToDeviceUnits(20), step.MediaNoticeLabel.Margin.Bottom);
            Assert.True(step.StateActionButton.Visible);
        }, primary: 1, ready: ready);
    }

    [Fact]
    public void RunningNonRelevantStage()
    {
        var view = RunningNonRelevantView();
        RenderBoth("generate-corpus-running-non-relevant", GenerateCorpusRenderState.RunningNonRelevant, view, (step, labels) =>
        {
            Assert.True(step.CancelButton.Visible);
            Assert.False(step.BannerPanel.Visible);
            Assert.False(step.RetryFailedButton.Visible);
            Assert.Equal(15, step.WavesGrid.Rows.Count);
            var stage = step.WavesGrid.Rows[14];
            Assert.Equal("Non-relevant mail", stage.Cells["Dates"].Value);
            Assert.Equal(string.Empty, stage.Cells["Wave"].Value);
            Assert.Equal("2,240 / 3,760", stage.Cells["Done"].Value);
            // The running row is scrolled into view
            Assert.True(stage.Displayed, "running Non-relevant mail row not in view");
            // Threads + attachments
            Assert.Equal("17", stage.Cells["Warnings"].Value);
            Assert.Equal("24", stage.Cells["Failed"].Value);
            // A clean wave leaves Warnings / Failed blank
            Assert.Equal(string.Empty, step.WavesGrid.Rows[0].Cells["Warnings"].Value);
            Assert.Equal(string.Empty, step.WavesGrid.Rows[0].Cells["Failed"].Value);
            Assert.Equal("Non-relevant mail: 2,240 of 3,760 threads written...", step.StatusLabel.Text);
            var events = step.ActivityGrid.Rows.Cast<DataGridViewRow>().Select(r => (string)r.Cells["Event"].Value!).ToList();
            Assert.Contains(events, e => e.StartsWith("Images and voicemail stopped: the OpenAI quota is used up."));
            Assert.Contains(events, e => e.Contains(" skipped: "));
            Assert.Contains(events, e => e.Contains("The email was saved without it."));
            Assert.Contains(events, e => e.Contains(": attempt 1 of 3 failed"));
            var percent = step.ProgressBar.Value * 100 / step.ProgressBar.Maximum;
            Assert.InRange(percent, 65, 75);
        }, primary: 0);
    }

    [Fact]
    public void PartialUpgradeBanner()
    {
        var view = PartialUpgradeView();
        RenderBoth("generate-corpus-partial-upgrade", GenerateCorpusRenderState.PartialUpgrade, view, (step, labels) =>
        {
            Assert.Equal(GenerateCorpusBanner.PartialRun, step.BannerKind);
            Assert.True(step.BannerPanel.Visible);
            Assert.Equal("Part of the corpus is not written yet.", step.BannerHeadingText);
            Assert.Equal(GenerationUiText.BannerBody(1240, 5000, 0, 2140, 0), step.BannerBodyText);
            Assert.True(step.BannerButton.Visible);
            Assert.Equal("Continue Generating", step.BannerButton.Text);
            Assert.False(step.RetryFailedButton.Visible);
            Assert.True(step.StartOverButton.Visible);
            var repair = step.WavesGrid.Rows[step.WavesGrid.Rows.Count - 1];
            Assert.Equal("Attachments for written threads", repair.Cells["Dates"].Value);
            Assert.Equal("0 / 312", repair.Cells["Done"].Value);
            Assert.Equal(GenerationUiText.StageRowText(view.Waves[^1]).Tooltip, repair.Cells["Dates"].ToolTipText);
        }, primary: 1, runEnded: true);
    }

    [Fact]
    public void FinishedSkippedMedia()
    {
        var view = FinishedPhase6View(failedThreads: 0, failedAttachments: 0, skipped: 44);
        var media = GenerationUiText.MediaBanner(44, 0, true, "the OpenAI key was rejected");
        RenderBoth("generate-corpus-finished-skipped-media", GenerateCorpusRenderState.FinishedSkippedMedia, view, (step, labels) =>
        {
            Assert.Equal(GenerateCorpusBanner.Media, step.BannerKind);
            Assert.Equal("Some images and voicemails were skipped.", step.BannerHeadingText);
            Assert.Equal(media.Body, step.BannerBodyText);
            Assert.True(step.BannerButton.Visible);
            Assert.Equal("Create Skipped Media", step.BannerButton.Text);
            Assert.False(step.RetryFailedButton.Visible);
            // Rule 19 variant 2 ("9,955" + "of 9,999") takes the sub-line at 9,999; the tooltip always names the skipped items
            if (step.Tiles[2].ValueText.Contains('/'))
                Assert.Equal("44 skipped", step.Tiles[2].SubLineText);
            Assert.Contains("44 items skipped because images and voicemail were unavailable.", step.Tiles[2].TooltipText);
            Assert.Equal(Theme.Warning, step.StatusLabel.ForeColor);
        }, primary: 1, media: media);
    }

    [Fact]
    public void FinishedSkippedMediaWithFailures()
    {
        var view = FinishedPhase6View(failedThreads: 3, failedAttachments: 6, skipped: 44);
        var media = GenerationUiText.MediaBanner(44, 9, true, string.Empty);
        RenderBoth("generate-corpus-finished-skipped-media-failures", GenerateCorpusRenderState.FinishedSkippedMediaWithFailures, view, (step, labels) =>
        {
            Assert.Equal(GenerateCorpusBanner.Media, step.BannerKind);
            Assert.True(step.BannerButton.Visible);
            Assert.Equal("Retry Failed and Skipped Items", step.BannerButton.Text);
            // One Primary rule: the media banner button replaces the header's Retry Failed Items
            Assert.False(step.RetryFailedButton.Visible);
            Assert.True(step.StartOverButton.Visible);
            Assert.Equal("9", step.Tiles[5].ValueText);
            Assert.DoesNotContain("Retry Failed Items", step.StatusLabel.Text);
        }, primary: 1, media: media);
    }

    [Fact]
    public void FinishedSkippedMediaUnavailable()
    {
        var view = FinishedPhase6View(failedThreads: 0, failedAttachments: 0, skipped: 44);
        RenderBoth("generate-corpus-finished-skipped-media-unavailable", GenerateCorpusRenderState.FinishedSkippedMediaUnavailable, view, (step, labels) =>
        {
            Assert.Equal(GenerateCorpusBanner.Media, step.BannerKind);
            Assert.True(step.BannerPanel.Visible);
            Assert.False(step.BannerButton.Visible);
            Assert.EndsWith("Test an OpenAI key on the API Configuration step, then come back here.", step.BannerBodyText);
            Assert.False(step.RetryFailedButton.Visible);
        }, primary: 0);
    }

    [Fact]
    public void FinishedCleanTiles()
    {
        var view = FinishedPhase6View(failedThreads: 0, failedAttachments: 0, skipped: 0);
        RenderBoth("generate-corpus-finished-clean-tiles", GenerateCorpusRenderState.FinishedClean, view, (step, labels) =>
        {
            Assert.False(step.BannerPanel.Visible);
            Assert.False(step.RetryFailedButton.Visible);
            Assert.Equal(new[] { "Relevant", "Non-relevant", "Attachments", "Emails", "Warnings", "Failed", "Cost" },
                step.Tiles.Select(t => t.CaptionText).ToArray());
            Assert.StartsWith("1,240", step.Tiles[0].ValueText);
            Assert.StartsWith("8,759", step.Tiles[1].ValueText);
            Assert.StartsWith("9,999", step.Tiles[2].ValueText);
            Assert.Equal("0", step.Tiles[4].ValueText);
            Assert.Equal("0", step.Tiles[5].ValueText);
            Assert.Equal(Theme.Success, step.StatusLabel.ForeColor);
            Assert.All(step.WavesGrid.Rows.Cast<DataGridViewRow>(), r => Assert.Equal(string.Empty, r.Cells["Failed"].Value));
        }, primary: 0);
    }

    [Fact]
    public void FinishedWithFailuresHeaderButton()
    {
        var view = FinishedPhase6View(failedThreads: 3, failedAttachments: 6, skipped: 0);
        RenderBoth("generate-corpus-finished-failures-header", GenerateCorpusRenderState.FinishedIssues, view, (step, labels) =>
        {
            Assert.False(step.BannerPanel.Visible);
            Assert.True(step.RetryFailedButton.Visible);
            Assert.Equal("Retry Failed Items", step.RetryFailedButton.Text);
            Assert.Equal(ButtonStyle.Primary, ((ThemedButton)step.RetryFailedButton).Variant);
            Assert.Equal("9", step.Tiles[5].ValueText);
            Assert.Equal(Theme.Danger, step.Tiles[5].ValueColor);
            Assert.Contains("Retry Failed Items", step.StatusLabel.Text);
        }, primary: 1);
    }

    // ----- Harness -----

    private sealed class Measured
    {
        public List<string> Labels = new();
        public bool DashboardShown;
        public int FirstTileLeft;
        public int LastTileRight;
        public List<int> TileHeights = new();
        public int WavesLeft;
        public int ActivityRight;
        public int HeaderRight = -1;
        public bool AnyGridHorizontalScroll;
        public int DatesWidth;
        public int EventWidth;
        public List<(int Needed, int Width)> ProgressValues = new();
        public int PrimaryCount;
        public List<(string Text, int Needed, int Width)> Buttons = new();
        public bool StateOverflow;
        public int DoneNeeded;
        public int DoneWidth;
    }

    private static void RenderBoth(string name, GenerateCorpusRenderState render, GenerationView? view,
        Action<StepGenerateCorpus, List<string>> verify, string reason = "", int earlierThreads = 0, int? primary = null,
        GenerationReadyInput? ready = null, MediaBannerContent? media = null, bool runEnded = false)
    {
        RenderHarness.RunSta(() =>
        {
            foreach (var size in new[] { RenderHarness.Large, RenderHarness.WizardMin })
            {
                using var fixture = new StepFixture(approved: render != GenerateCorpusRenderState.NotApproved);
                StepGenerateCorpus? step = null;
                var measured = new Measured();
                var result = RenderHarness.RenderControl(() =>
                {
                    step = new StepGenerateCorpus();
                    step.LoadForRender(fixture.State, render, view, reason, earlierThreads, ready, media, runEnded);
                    return step;
                }, name, size, control =>
                {
                    control.PerformLayout();
                    Application.DoEvents();
                    Measure(step!, measured);
                    // Visible flags and grid rows only hold while the form is shown, so state checks run here
                    verify(step!, measured.Labels);
                });

                Assert.True(File.Exists(result.PngPath), result.PngPath);
                Assert.True(result.DeviceDpi >= RenderHarness.MinimumDpi, $"DPI {result.DeviceDpi}");
                Assert.Empty(result.Clipping);
                Assert.False(measured.AnyGridHorizontalScroll);
                // Rule 24: the state card content fits inside the card
                Assert.False(measured.StateOverflow, $"state card content overflows at {size}");

                // Accent rule 1: never more than one Primary besides the shell's Next, and exactly the state's expected count
                Assert.InRange(measured.PrimaryCount, 0, 1);
                if (primary.HasValue)
                    Assert.True(primary.Value == measured.PrimaryCount, $"{name} at {size}: {measured.PrimaryCount} Primary buttons, expected {primary.Value}");

                // Rule 23: no visible button text is ellipsized
                foreach (var (text, needed, width) in measured.Buttons)
                    Assert.True(needed <= width, $"button '{text}' needs {needed}, has {width} at {size}");

                if (!measured.DashboardShown)
                    continue;

                // Rule 17: tiles and body cards share outer edges; the header's rightmost control ends there too
                Assert.Single(measured.TileHeights.Distinct());
                Assert.True(Math.Abs(measured.FirstTileLeft - measured.WavesLeft) <= 1, $"tile left {measured.FirstTileLeft}, waves left {measured.WavesLeft}");
                Assert.True(Math.Abs(measured.LastTileRight - measured.ActivityRight) <= 1, $"tile right {measured.LastTileRight}, activity right {measured.ActivityRight}");
                if (measured.HeaderRight >= 0)
                    Assert.True(Math.Abs(measured.HeaderRight - measured.ActivityRight) <= 1, $"header right {measured.HeaderRight}, cards right {measured.ActivityRight}");

                // Rule 18 at WizardMin: Dates >= 120 and Event >= 160 (logical)
                var dpiScale = result.DeviceDpi / 96.0;
                Assert.True(measured.DatesWidth >= (int)Math.Floor(120 * dpiScale), $"Dates {measured.DatesWidth} at {size}");
                Assert.True(measured.EventWidth >= (int)Math.Floor(160 * dpiScale), $"Event {measured.EventWidth} at {size}");

                // Rules 19 and 21: the Relevant, Non-relevant and Attachments values never ellipsize (each picks its own variant)
                foreach (var (needed, width) in measured.ProgressValues)
                    Assert.True(needed <= width, $"tile value needs {needed}, has {width} at {size}");

                // Stage Done cells (up to "9,999 / 9,999") are never cut
                Assert.True(measured.DoneNeeded <= measured.DoneWidth, $"Done needs {measured.DoneNeeded}, has {measured.DoneWidth} at {size}");
            }
        });
    }

    private static void Measure(StepGenerateCorpus step, Measured m)
    {
        m.Labels = AllControls(step).OfType<Label>().Where(l => l.Visible).Select(l => l.Text).ToList();
        m.AnyGridHorizontalScroll = new[] { step.WavesGrid, step.ActivityGrid }
            .Any(g => g.Visible && g.Controls.OfType<HScrollBar>().Any(b => b.Visible));
        var buttons = AllControls(step).OfType<ThemedButton>().Where(b => b.Visible).ToList();
        m.PrimaryCount = buttons.Count(b => b.Variant == ButtonStyle.Primary);
        m.Buttons = buttons.Select(b => (b.Text, TextRenderer.MeasureText(b.Text, b.Font).Width, b.Width)).ToList();
        if (step.StateCardPanel.Visible)
        {
            var content = step.StateCardPanel.Controls[0];
            m.StateOverflow = content.Bottom > step.StateCardPanel.ClientSize.Height || content.Right > step.StateCardPanel.ClientSize.Width;
        }
        m.DashboardShown = step.WavesCard.Visible;
        if (!m.DashboardShown)
            return;

        m.TileHeights = step.Tiles.Select(t => t.Height).ToList();
        m.FirstTileLeft = Left(step, step.Tiles[0]);
        m.LastTileRight = Left(step, step.Tiles[^1]) + step.Tiles[^1].Width;
        m.WavesLeft = Left(step, step.WavesCard);
        m.ActivityRight = Left(step, step.ActivityCard) + step.ActivityCard.Width;
        var header = new[] { step.RetryFailedButton, step.StartOverButton }.FirstOrDefault(b => b.Visible);
        if (header != null)
            m.HeaderRight = Left(step, header) + header.Width;
        m.DatesWidth = step.WavesGrid.Columns["Dates"]!.Width;
        m.EventWidth = step.ActivityGrid.Columns["Event"]!.Width;
        using var body = Theme.BodyFont();
        var done = step.WavesGrid.Columns["Done"]!;
        m.DoneWidth = done.Width - done.DefaultCellStyle.Padding.Horizontal;
        m.DoneNeeded = step.WavesGrid.Rows.Cast<DataGridViewRow>()
            .Select(r => TextRenderer.MeasureText((string?)r.Cells["Done"].Value ?? string.Empty, body).Width)
            .DefaultIfEmpty(0).Max();
        using var title = Theme.TitleFont();
        m.ProgressValues = step.Tiles.Take(3).Select(t => (TextRenderer.MeasureText(t.ValueText, title).Width, t.ValueWidth)).ToList();
    }

    private static int Left(Control root, Control control) => root.PointToClient(control.PointToScreen(Point.Empty)).X;

    private static IEnumerable<Control> AllControls(Control root)
    {
        foreach (Control child in root.Controls)
        {
            yield return child;
            foreach (var nested in AllControls(child))
                yield return nested;
        }
    }

    // ----- Seeded views (neutral subjects, no names from fictional works) -----

    private static readonly string[] Subjects =
    {
        "Schedule update",
        "Pay application 7 review",
        "Change order pricing",
        "Site walk notes",
        "Window submittal comments",
        "RE: Revised delivery dates",
        "Punch list items for level 3",
        "Retention release request"
    };

    private static readonly DateOnly FirstWave = new(2023, 11, 6);

    private static List<WaveProgress> Waves(int count, Func<int, (int Done, int Warnings, int Failed, bool Running)> state)
    {
        var waves = new List<WaveProgress>();
        for (var i = 1; i <= count; i++)
        {
            var start = FirstWave.AddDays((i - 1) * 21);
            var (done, warnings, failed, running) = state(i);
            waves.Add(new WaveProgress
            {
                Index = i,
                Start = start,
                // Wave 3 spans the year end
                End = start.AddDays(i == 3 ? 20 : 13),
                Threads = i % 3 == 0 ? 5 : 4,
                Emails = i % 3 == 0 ? 14 : 11,
                Done = done,
                Warnings = warnings,
                Failed = failed,
                Running = running
            });
        }

        return waves;
    }

    /// <summary>
    /// Wave 3 of 14 running, 40 activity rows (retries and warnings included), 18 of 60 threads finished (30%).
    /// </summary>
    private static GenerationView RunningView()
    {
        var waves = Waves(14, i => i switch
        {
            1 => (4, 1, 0, false),
            2 => (3, 0, 1, false),
            3 => (2, 0, 0, true),
            _ => (0, 0, 0, false)
        });
        var events = new List<GenerationEvent>();
        var time = new DateTime(2026, 10, 3, 9, 58, 12, DateTimeKind.Local);
        events.Add(new GenerationEvent { Kind = GenerationEventKind.RunStarted, LocalTime = time, Threads = 60, WaveCount = 14 });
        var thread = 1;
        while (events.Count < 40)
        {
            time = time.AddSeconds(17);
            var id = $"T-{thread:0000}";
            var subject = Subjects[thread % Subjects.Length];
            switch (events.Count % 6)
            {
                case 1:
                    events.Add(new GenerationEvent
                    {
                        Kind = GenerationEventKind.WaveStarted,
                        LocalTime = time,
                        WaveIndex = Math.Min(3, events.Count / 12 + 1),
                        WaveCount = 14,
                        WaveStart = waves[0].Start,
                        WaveEnd = waves[0].End,
                        Threads = 4
                    });
                    break;
                case 2:
                    events.Add(new GenerationEvent
                    {
                        Kind = GenerationEventKind.Retry,
                        LocalTime = time,
                        ThreadId = id,
                        Attempt = 1,
                        Violations = new List<string>
                        {
                            "msg 2: names a person who is not in the cast",
                            "msg 3: uses $48,200 for the change order total; the case value is $46,800"
                        }
                    });
                    break;
                case 3:
                    events.Add(new GenerationEvent
                    {
                        Kind = GenerationEventKind.KeptWithWarnings,
                        LocalTime = time,
                        ThreadId = id,
                        Subject = subject,
                        Violations = new List<string> { "msg 1: mentions the inspection report, dated after this email" }
                    });
                    thread++;
                    break;
                case 4:
                    events.Add(new GenerationEvent
                    {
                        Kind = GenerationEventKind.ThreadFailed,
                        LocalTime = time,
                        ThreadId = id,
                        Reason = "The provider timed out"
                    });
                    thread++;
                    break;
                default:
                    events.Add(new GenerationEvent
                    {
                        Kind = GenerationEventKind.ThreadWritten,
                        LocalTime = time,
                        ThreadId = id,
                        Subject = subject,
                        Emails = 3,
                        Details = 2
                    });
                    thread++;
                    break;
            }
        }

        return new GenerationView
        {
            Counts = new GenerationCounts
            {
                TotalThreads = 60,
                DoneThreads = 16,
                WarningThreads = 1,
                FailedThreads = 2,
                WaveCount = 14,
                EmailsPlanned = 172,
                EmailsWritten = 47,
                FilesWritten = 131,
                Mailboxes = 9,
                LedgerEntries = 38,
                CostUsd = 1.84m,
                RemainingCostUsd = 4.92m,
                Running = true
            },
            Waves = waves,
            Events = events,
            Stage = GenerationStage.Writing,
            StageWave = 3,
            StageWaveCount = 14,
            StageWaveStart = waves[2].Start,
            StageWaveEnd = waves[2].End,
            StageCount = 5
        };
    }

    private static GenerationView FinishedView(int done, int total, int warnings, int failed, bool cancelled)
    {
        var finished = done + failed;
        var waves = Waves(14, i =>
        {
            // Threads fill waves in order; warnings and failures land in waves 2 and 5
            var before = Enumerable.Range(1, i - 1).Sum(k => k % 3 == 0 ? 5 : 4);
            var size = i % 3 == 0 ? 5 : 4;
            var waveDone = Math.Clamp(finished - before, 0, size);
            var waveFailed = i == 5 ? Math.Min(failed, waveDone) : 0;
            var waveWarnings = i == 2 ? Math.Min(warnings, waveDone) : 0;
            return (waveDone - waveFailed, waveWarnings, waveFailed, false);
        });
        var events = new List<GenerationEvent>();
        var time = new DateTime(2026, 10, 3, 14, 2, 5, DateTimeKind.Local);
        for (var t = 1; t <= Math.Min(finished, 30); t++)
        {
            time = time.AddSeconds(23);
            var id = $"T-{t:0000}";
            events.Add(t <= failed && failed > 0
                ? new GenerationEvent { Kind = GenerationEventKind.ThreadFailed, LocalTime = time, ThreadId = id, Reason = "The model refused to write this thread" }
                : t <= failed + warnings
                    ? new GenerationEvent { Kind = GenerationEventKind.KeptWithWarnings, LocalTime = time, ThreadId = id, Subject = Subjects[t % Subjects.Length], Violations = new List<string> { "msg 2: has 3 messages; the plan has 4" } }
                    : new GenerationEvent { Kind = GenerationEventKind.ThreadWritten, LocalTime = time, ThreadId = id, Subject = Subjects[t % Subjects.Length], Emails = 3, Details = 1 });
        }
        events.Add(cancelled
            ? new GenerationEvent { Kind = GenerationEventKind.RunCancelled, LocalTime = time.AddSeconds(5), Done = done, Total = total }
            : new GenerationEvent { Kind = GenerationEventKind.RunFinished, LocalTime = time.AddSeconds(5) });

        return new GenerationView
        {
            Counts = new GenerationCounts
            {
                TotalThreads = total,
                DoneThreads = done,
                WarningThreads = warnings,
                FailedThreads = failed,
                WaveCount = 14,
                EmailsPlanned = 180,
                EmailsWritten = done * 3,
                FilesWritten = done * 8,
                Mailboxes = 12,
                LedgerEntries = done * 2,
                CostUsd = done * 0.11m,
                RemainingCostUsd = (total - done) * 0.11m,
                Finished = !cancelled
            },
            Waves = waves,
            Events = events
        };
    }

    // ----- Phase 6 seeded views (counts up to 9,999 for DPI rule 21) -----

    private static GenerationReadyInput ReadyInput(bool mediaAvailable) => new()
    {
        RelevantThreads = 1240,
        NonRelevantThreads = 3760,
        Emails = 9876,
        Waves = 14,
        Attachments = 2140,
        StoryCost = 182.40m,
        VolumeCost = 96.10m,
        MediaCost = 31.20m,
        StoryModel = "Claude Sonnet 4.5",
        VolumeModel = "Claude Haiku 4.5",
        MediaPlanned = 320,
        MediaAvailable = mediaAvailable
    };

    /// <summary>
    /// 14 relevant waves holding 1,240 threads (89 or 88 each), all finished; wave 4 kept 2 with warnings.
    /// </summary>
    private static List<WaveProgress> RelevantWavesDone()
    {
        var waves = new List<WaveProgress>();
        for (var i = 1; i <= 14; i++)
        {
            var start = FirstWave.AddDays((i - 1) * 21);
            var threads = i <= 8 ? 89 : 88;
            waves.Add(new WaveProgress
            {
                Index = i,
                Start = start,
                End = start.AddDays(i == 3 ? 20 : 13),
                Threads = threads,
                Emails = threads * 3,
                Done = threads,
                Warnings = i == 4 ? 2 : 0,
                Attachments = threads / 2,
                AttachmentsDone = threads / 2
            });
        }

        return waves;
    }

    /// <summary>
    /// Relevant waves done, the Non-relevant mail row running: 3,480 written + 20 failed of 5,000 threads (70%), with
    /// attachment retry / kept / failed / skipped and media-stopped activity rows.
    /// </summary>
    private static GenerationView RunningNonRelevantView()
    {
        var waves = RelevantWavesDone();
        waves.Add(new WaveProgress
        {
            Index = 15,
            RowKind = StageRowKind.NonRelevant,
            Threads = 3760,
            Emails = 7900,
            Done = 2240,
            Warnings = 12,
            Failed = 20,
            Attachments = 1520,
            AttachmentsDone = 870,
            AttachmentWarnings = 5,
            AttachmentsFailed = 4,
            AttachmentsSkipped = 44,
            Running = true
        });

        var events = new List<GenerationEvent>();
        var time = new DateTime(2026, 10, 4, 10, 12, 3, DateTimeKind.Local);
        GenerationEvent At(GenerationEvent e)
        {
            time = time.AddSeconds(9);
            e.LocalTime = time;
            return e;
        }

        events.Add(At(new GenerationEvent { Kind = GenerationEventKind.RunStarted, Threads = 5000, Attachments = 2140 }));
        events.Add(At(new GenerationEvent { Kind = GenerationEventKind.SideProjectsReady, Projects = 18, Organizations = 6 }));
        events.Add(At(new GenerationEvent { Kind = GenerationEventKind.WaveFinished, WaveIndex = 14, WaveCount = 14, Written = 88, LedgerTotal = 412 }));
        events.Add(At(new GenerationEvent { Kind = GenerationEventKind.NonRelevantStarted, Threads = 3760 }));
        for (var n = 1; events.Count < 34; n++)
        {
            var id = $"N-{n + 2200:0000}";
            events.Add(At((n % 9) switch
            {
                2 => new GenerationEvent
                {
                    Kind = GenerationEventKind.AttachmentRetry,
                    ThreadId = id,
                    FileName = "Level 3 punch list.xlsx",
                    Attempt = 1,
                    Violations = new List<string> { "mentions \"window submittal\", which this email must not mention", "has 4 sheets; the plan has 3" }
                },
                4 => new GenerationEvent
                {
                    Kind = GenerationEventKind.AttachmentKeptWithWarnings,
                    ThreadId = id,
                    FileName = "Quarterly facilities budget.xlsx",
                    Violations = new List<string> { "invite not tied to a known meeting" }
                },
                5 => new GenerationEvent { Kind = GenerationEventKind.AttachmentFailed, ThreadId = id, FileName = "Lobby renovation concept.pptx", Reason = "the request timed out" },
                7 => new GenerationEvent { Kind = GenerationEventKind.AttachmentSkipped, ThreadId = id, FileName = "Parking lot photo 0412.png", Reason = "the OpenAI quota is used up" },
                _ => new GenerationEvent { Kind = GenerationEventKind.ThreadWritten, ThreadId = id, Subject = Subjects[n % Subjects.Length], Emails = 2, Attachments = n % 2 }
            }));
            if (n == 6)
                events.Add(At(new GenerationEvent { Kind = GenerationEventKind.MediaStopped, Reason = "the OpenAI quota is used up", MediaRemaining = 44 }));
        }

        return new GenerationView
        {
            Counts = new GenerationCounts
            {
                TotalThreads = 5000,
                DoneThreads = 3480,
                WarningThreads = 14,
                FailedThreads = 20,
                WaveCount = 14,
                RelevantTotal = 1240,
                RelevantDone = 1240,
                NonRelevantTotal = 3760,
                NonRelevantDone = 2240,
                NonRelevantDailyBusiness = 1300,
                NonRelevantAncillary = 400,
                NonRelevantBulk = 420,
                NonRelevantDecoy = 120,
                AttachmentsTotal = 2140,
                AttachmentsDone = 1490,
                AttachmentsWarnings = 5,
                AttachmentsFailed = 4,
                AttachmentsSkipped = 44,
                AttachmentsWord = 700,
                AttachmentsExcel = 520,
                AttachmentsPowerPoint = 210,
                AttachmentsInvite = 390,
                AttachmentsImage = 240,
                AttachmentsVoicemail = 80,
                MediaPlanned = 320,
                EmailsPlanned = 9876,
                EmailsWritten = 6912,
                FilesWritten = 9840,
                Mailboxes = 48,
                LedgerEntries = 412,
                CostUsd = 214.37m,
                MediaCostUsd = 18.40m,
                RemainingCostUsd = 88.10m,
                Running = true
            },
            Waves = waves,
            Events = events,
            Stage = GenerationStage.NonRelevant
        };
    }

    /// <summary>
    /// A Phase 5 run that finished its relevant waves before non-relevant mail and attachments existed: the Non-relevant
    /// mail row is pending and 312 attachments wait on 140 written threads.
    /// </summary>
    private static GenerationView PartialUpgradeView()
    {
        var waves = RelevantWavesDone();
        foreach (var wave in waves)
            wave.AttachmentsDone = 0;
        waves.Add(new WaveProgress { Index = 15, RowKind = StageRowKind.NonRelevant, Threads = 3760, Emails = 7900, Attachments = 1520 });
        waves.Add(new WaveProgress { Index = 16, RowKind = StageRowKind.Attachments, Threads = 140, Attachments = 312 });

        var events = new List<GenerationEvent>();
        var time = new DateTime(2026, 10, 2, 16, 40, 0, DateTimeKind.Local);
        for (var t = 1; t <= 12; t++)
        {
            time = time.AddSeconds(31);
            events.Add(new GenerationEvent
            {
                Kind = GenerationEventKind.ThreadWritten,
                LocalTime = time,
                ThreadId = $"T-{t + 1228:0000}",
                Subject = Subjects[t % Subjects.Length],
                Emails = 3,
                Details = 2,
                Relevant = true
            });
        }
        events.Add(new GenerationEvent { Kind = GenerationEventKind.RunFinished, LocalTime = time.AddSeconds(4) });

        return new GenerationView
        {
            Counts = new GenerationCounts
            {
                TotalThreads = 5000,
                DoneThreads = 1240,
                WaveCount = 14,
                RelevantTotal = 1240,
                RelevantDone = 1240,
                NonRelevantTotal = 3760,
                AttachmentsTotal = 2140,
                AttachmentsWord = 700,
                AttachmentsExcel = 520,
                AttachmentsPowerPoint = 210,
                AttachmentsInvite = 390,
                AttachmentsImage = 240,
                AttachmentsVoicemail = 80,
                MediaPlanned = 320,
                EmailsPlanned = 9876,
                EmailsWritten = 3720,
                FilesWritten = 9310,
                Mailboxes = 48,
                LedgerEntries = 412,
                CostUsd = 61.20m,
                RemainingCostUsd = 241.50m,
                Finished = true
            },
            Waves = waves,
            Events = events
        };
    }

    /// <summary>
    /// A finished Phase 6 run: 1,240 relevant + 8,759 non-relevant threads and 9,999 attachments, with the given failed
    /// threads (non-relevant), failed attachments and skipped media.
    /// </summary>
    private static GenerationView FinishedPhase6View(int failedThreads, int failedAttachments, int skipped)
    {
        const int nonRelevant = 8759;
        const int attachments = 9999;
        var waves = RelevantWavesDone();
        foreach (var wave in waves)
            wave.Warnings = 0;
        waves.Add(new WaveProgress
        {
            Index = 15,
            RowKind = StageRowKind.NonRelevant,
            Threads = nonRelevant,
            Emails = 9200,
            Done = nonRelevant - failedThreads,
            Failed = failedThreads,
            Attachments = attachments - 620,
            AttachmentsDone = attachments - 620 - failedAttachments - skipped,
            AttachmentsFailed = failedAttachments,
            AttachmentsSkipped = skipped
        });

        var events = new List<GenerationEvent>();
        var time = new DateTime(2026, 10, 4, 18, 5, 0, DateTimeKind.Local);
        for (var t = 1; t <= 24; t++)
        {
            time = time.AddSeconds(13);
            var id = $"N-{t + 8730:0000}";
            events.Add(t <= failedThreads
                ? new GenerationEvent { Kind = GenerationEventKind.ThreadFailed, LocalTime = time, ThreadId = id, Reason = "the model returned no usable content" }
                : t - failedThreads <= failedAttachments
                    ? new GenerationEvent { Kind = GenerationEventKind.AttachmentFailed, LocalTime = time, ThreadId = id, FileName = "Vendor invoice summary.xlsx", Reason = "the request timed out" }
                    : t - failedThreads - failedAttachments <= Math.Min(skipped, 3)
                        ? new GenerationEvent { Kind = GenerationEventKind.AttachmentSkipped, LocalTime = time, ThreadId = id, FileName = "Site voicemail 0918.mp3", Reason = "the OpenAI key was rejected" }
                        : new GenerationEvent { Kind = GenerationEventKind.ThreadWritten, LocalTime = time, ThreadId = id, Subject = Subjects[t % Subjects.Length], Emails = 2, Attachments = 1 });
        }
        events.Add(new GenerationEvent { Kind = GenerationEventKind.NonRelevantFinished, LocalTime = time.AddSeconds(2), Written = nonRelevant - failedThreads, Failed = failedThreads });
        events.Add(new GenerationEvent { Kind = GenerationEventKind.RunFinished, LocalTime = time.AddSeconds(5) });

        return new GenerationView
        {
            Counts = new GenerationCounts
            {
                TotalThreads = 1240 + nonRelevant,
                DoneThreads = 1240 + nonRelevant - failedThreads,
                FailedThreads = failedThreads,
                WaveCount = 14,
                RelevantTotal = 1240,
                RelevantDone = 1240,
                NonRelevantTotal = nonRelevant,
                NonRelevantDone = nonRelevant - failedThreads,
                NonRelevantDailyBusiness = 5200,
                NonRelevantAncillary = 1800,
                NonRelevantBulk = 1400,
                NonRelevantDecoy = 359,
                AttachmentsTotal = attachments,
                AttachmentsDone = attachments - failedAttachments - skipped,
                AttachmentsFailed = failedAttachments,
                AttachmentsSkipped = skipped,
                AttachmentsWord = 3200,
                AttachmentsExcel = 2100,
                AttachmentsPowerPoint = 900,
                AttachmentsInvite = 1500,
                AttachmentsImage = 1600,
                AttachmentsVoicemail = 699,
                MediaPlanned = 2299,
                EmailsPlanned = 9999,
                EmailsWritten = 9999 - failedThreads * 2,
                FilesWritten = 9999,
                Mailboxes = 48,
                LedgerEntries = 412,
                CostUsd = 312.48m,
                MediaCostUsd = 41.20m,
                RemainingCostUsd = 0m,
                Finished = true
            },
            Waves = waves,
            Events = events
        };
    }

    // ----- Fixture -----

    /// <summary>
    /// A saved case with the evidence fixture plan (approved or not) under a temp Cases root; deleted on dispose.
    /// </summary>
    internal sealed class StepFixture : IDisposable
    {
        private readonly string _root;

        public StepFixture(bool approved = true)
        {
            _root = Path.Combine(Path.GetTempPath(), "rd-generate-corpus-" + Guid.NewGuid().ToString("N"));
            var store = new CaseStore(Path.Combine(_root, "Cases"), Path.Combine(_root, "AppData"));
            var (draft, plan) = EvidenceTestData.ApprovedCase();
            if (!approved)
                draft.Corpus!.Approved = false;
            draft.AdvanceStage(CaseStage.ReviewPlan);
            Folder = store.EnsureFolder(draft);
            Assert.True(store.SaveNow(draft), store.LastSaveError);
            Draft = draft;
            Plan = plan;
            State = new WizardState { Case = draft, CaseStore = store, OpenAIApiKey = "sk-render-test", OpenAIKeyTested = true };
        }

        public WizardState State { get; }
        public CaseDraft Draft { get; }
        public CorpusPlan Plan { get; }
        public string Folder { get; }

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

/// <summary>
/// Entering Generate Corpus (UI-SPEC S1 entry rules): the right card or dashboard from the journal, and never a run.
/// Not a render test: the step is never shown.
/// </summary>
public class GenerateCorpusEntryTests
{
    [Fact]
    public void NoJournal_ShowsReady_AndWritesNothing()
    {
        using var fixture = new GenerateCorpusRenderTests.StepFixture();

        Enter(fixture, step =>
        {
            Assert.True(step.StateCardShown);
            Assert.Equal("Ready to write the corpus", step.StateHeading);
            Assert.False(step.IsGenerating);
            // Never auto-starts: no journal, no mailboxes (U3)
            Assert.False(File.Exists(GenerationJournal.PathFor(fixture.Folder)));
            Assert.False(Directory.Exists(Path.Combine(fixture.Folder, CorpusPlan.MailboxesRoot)));
        });
    }

    [Fact]
    public void UnapprovedPlan_ShowsNotApproved()
    {
        using var fixture = new GenerateCorpusRenderTests.StepFixture(approved: false);

        Enter(fixture, step =>
        {
            Assert.Equal("Approve the plan first", step.StateHeading);
            Assert.Equal(string.Empty, step.NoticeText);
        });
    }

    [Fact]
    public void JournalWithPendingThreads_ShowsDashboardAndBanner()
    {
        using var fixture = new GenerateCorpusRenderTests.StepFixture();
        var threads = fixture.Plan.Threads.Where(t => t.Relevance == ThreadRelevance.Relevant).OrderBy(t => t.Id, StringComparer.Ordinal).ToList();
        Append(fixture,
            new JournalRecord { Type = JournalRecordType.RunStarted, PlanId = fixture.Plan.PlanId, Utc = DateTime.UtcNow },
            GenerationRunStateTests.Done(fixture.Plan, threads[0], ThreadWriteStatus.Written, 0.10m),
            GenerationRunStateTests.Failed(fixture.Plan, threads[1], 0.02m),
            new JournalRecord { Type = JournalRecordType.RunEnded, PlanId = fixture.Plan.PlanId, Utc = DateTime.UtcNow, Outcome = GenerationOutcome.Cancelled });

        Enter(fixture, step =>
        {
            Assert.False(step.StateCardShown);
            Assert.True(step.BannerShown);
            // Threads tile: 1 written (the variant depends on the unshown tile width)
            Assert.StartsWith("1", step.Tiles[0].ValueText);
            Assert.Equal("1", step.Tiles[5].ValueText);
            Assert.Equal(WavePlanner.Plan(fixture.Plan).Count, step.WavesGrid.Rows.Count);
            Assert.Equal(threads.Count, step.WavesGrid.Rows.Cast<DataGridViewRow>()
                .Sum(r => int.Parse(((string)r.Cells["Done"].Value!).Split('/')[1].Trim())));
            // Failed, written, cancelled: newest first
            Assert.Equal(3, step.ActivityGrid.Rows.Count);
            Assert.StartsWith("Cancelled.", (string)step.ActivityGrid.Rows[0].Cells["Event"].Value!);
            Assert.False(step.IsGenerating);
        });
    }

    [Fact]
    public void JournalForAnotherPlan_ShowsPlanChanged()
    {
        using var fixture = new GenerateCorpusRenderTests.StepFixture();
        var thread = fixture.Plan.Threads.First(t => t.Relevance == ThreadRelevance.Relevant);
        var foreign = GenerationRunStateTests.Done(fixture.Plan, thread, ThreadWriteStatus.Written, 0.10m);
        foreign.PlanId = Guid.NewGuid();
        Append(fixture, new JournalRecord { Type = JournalRecordType.RunStarted, PlanId = foreign.PlanId, Utc = DateTime.UtcNow }, foreign);

        Enter(fixture, step =>
        {
            Assert.Equal("The plan changed after emails were generated", step.StateHeading);
            Assert.StartsWith("1 thread was written for an earlier plan", step.StateBody);
        });
    }

    private static void Append(GenerateCorpusRenderTests.StepFixture fixture, params JournalRecord[] records)
    {
        var journal = new GenerationJournal(fixture.Folder);
        foreach (var record in records)
            journal.AppendAsync(record).GetAwaiter().GetResult();
    }

    /// <summary>
    /// Creates the step on an STA thread with a WinForms context, pumps until OnEnterStepAsync completes and checks it on
    /// that thread (handles created by tooltips belong to it).
    /// </summary>
    private static void Enter(GenerateCorpusRenderTests.StepFixture fixture, Action<StepGenerateCorpus> verify)
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try
            {
                SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());
                using var step = new StepGenerateCorpus();
                step.BindState(fixture.State);
                var task = step.OnEnterStepAsync();
                var deadline = DateTime.UtcNow.AddSeconds(30);
                while (!task.IsCompleted && DateTime.UtcNow < deadline)
                {
                    Application.DoEvents();
                    Thread.Sleep(5);
                }
                task.GetAwaiter().GetResult();
                verify(step);
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
