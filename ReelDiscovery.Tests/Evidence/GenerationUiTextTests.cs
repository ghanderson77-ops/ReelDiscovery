using ReelDiscovery.Models.Evidence;
using ReelDiscovery.UserControls;

namespace ReelDiscovery.Tests.Evidence;

public class GenerationUiTextTests
{
    private static GenerationCounts Counts(
        int total = 1000, int done = 3, int warnings = 0, int failed = 0, int waves = 14,
        int emailsPlanned = 30, int emailsWritten = 10, int files = 37, int mailboxes = 6, int ledger = 45,
        decimal cost = 0m, decimal remaining = 0m, bool running = false, bool finished = false) =>
        new()
        {
            TotalThreads = total,
            DoneThreads = done,
            WarningThreads = warnings,
            FailedThreads = failed,
            WaveCount = waves,
            EmailsPlanned = emailsPlanned,
            EmailsWritten = emailsWritten,
            FilesWritten = files,
            Mailboxes = mailboxes,
            LedgerEntries = ledger,
            CostUsd = cost,
            RemainingCostUsd = remaining,
            Running = running,
            Finished = finished,
            RelevantTotal = total,
            RelevantDone = done
        };

    // Every copy function's output, for the banned-word scan
    private static readonly string[] BannedWords = { "evidence", "noise", "filler" };

    private static void AssertNoBannedWords(string text)
    {
        foreach (var word in BannedWords)
            Assert.DoesNotContain(word, text, StringComparison.OrdinalIgnoreCase);
    }

    // --- Scope notice ---

    [Fact]
    public void ScopeNotice_RelevantNonRelevantAndAttachments()
    {
        Assert.Equal(
            "This step writes the approved plan as email: 12 relevant threads first, oldest first, then 30 non-relevant threads, with 9 attachments. Each answer-key row moves from Planned to Generated as its file is saved.",
            GenerationUiText.ScopeNotice(12, 30, 9));
    }

    [Fact]
    public void ScopeNotice_NoNonRelevant_DropsMiddle()
    {
        Assert.Equal(
            "This step writes the approved plan as email: 12 relevant threads, oldest first. Each answer-key row moves from Planned to Generated as its file is saved.",
            GenerationUiText.ScopeNotice(12, 0, 0));
        Assert.Equal(
            "This step writes the approved plan as email: 12 relevant threads, oldest first, with 1 attachment. Each answer-key row moves from Planned to Generated as its file is saved.",
            GenerationUiText.ScopeNotice(12, 0, 1));
    }

    [Fact]
    public void ScopeNotice_NoAttachments_DropsAttachmentClause()
    {
        Assert.Equal(
            "This step writes the approved plan as email: 1 relevant thread first, oldest first, then 1 non-relevant thread. Each answer-key row moves from Planned to Generated as its file is saved.",
            GenerationUiText.ScopeNotice(1, 1, 0));
    }

    [Theory]
    [InlineData(1, "1 relevant thread ", "1 non-relevant thread,", "1 attachment.")]
    [InlineData(2, "2 relevant threads", "2 non-relevant threads", "2 attachments")]
    [InlineData(1000, "1,000 relevant threads", "1,000 non-relevant threads", "1,000 attachments")]
    public void ScopeNotice_Plurals(int n, string threads, string nonRelevant, string attachments)
    {
        var text = GenerationUiText.ScopeNotice(n, n, n);
        Assert.Contains(threads, text);
        Assert.Contains(nonRelevant, text);
        Assert.Contains(attachments, text);
    }

    [Fact]
    public void ScopeNotice_ZeroRelevant()
    {
        Assert.StartsWith("This step writes the approved plan as email: 0 relevant threads, oldest first.", GenerationUiText.ScopeNotice(0, 0, 0));
    }

    // --- Tiles ---

    [Fact]
    public void TileKinds_InUiSpecOrder()
    {
        Assert.Equal(
            new[]
            {
                GenerationTileKind.Relevant, GenerationTileKind.NonRelevant, GenerationTileKind.Attachments, GenerationTileKind.Emails,
                GenerationTileKind.Warnings, GenerationTileKind.Failed, GenerationTileKind.Cost
            },
            Enum.GetValues<GenerationTileKind>());
    }

    [Fact]
    public void Tile_Relevant_DefaultVariant()
    {
        var c = Counts(total: 12, done: 3, waves: 2, ledger: 14);
        var tile = GenerationUiText.Tile(GenerationTileKind.Relevant, c);

        Assert.Equal("Relevant", tile.Caption);
        Assert.Equal("3 / 12", tile.Value);
        Assert.Equal("2 waves", tile.SubLine);
        Assert.Equal("3 of 12 relevant threads written, in 2 waves by date. The narrative ledger holds 14 story details.", tile.Tooltip);
        Assert.Equal(TileTone.Normal, tile.Tone);
    }

    [Fact]
    public void Tile_Relevant_UsesRelevantSplit()
    {
        var c = Counts(total: 50, done: 20);
        c.RelevantTotal = 12;
        c.RelevantDone = 3;
        Assert.Equal("3 / 12", GenerationUiText.Tile(GenerationTileKind.Relevant, c).Value);
    }

    [Theory]
    [InlineData(GenerationTileKind.Relevant)]
    [InlineData(GenerationTileKind.NonRelevant)]
    [InlineData(GenerationTileKind.Attachments)]
    public void Tile_FractionTiles_NarrowVariants(GenerationTileKind kind)
    {
        var c = new GenerationCounts
        {
            RelevantTotal = 1000, RelevantDone = 3, NonRelevantTotal = 1000, NonRelevantDone = 3,
            AttachmentsTotal = 1000, AttachmentsDone = 3, WaveCount = 14
        };

        Assert.Equal("3 / 1,000", GenerationUiText.Tile(kind, c).Value);
        var compact = GenerationUiText.Tile(kind, c, 1);
        Assert.Equal("3/1,000", compact.Value);
        Assert.Equal(GenerationUiText.Tile(kind, c).SubLine, compact.SubLine);

        var split = GenerationUiText.Tile(kind, c, 2);
        Assert.Equal("3", split.Value);
        Assert.Equal("of 1,000", split.SubLine);
    }

    [Fact]
    public void Tile_Variant_IgnoredByOtherTiles()
    {
        foreach (var kind in new[] { GenerationTileKind.Emails, GenerationTileKind.Warnings, GenerationTileKind.Failed, GenerationTileKind.Cost })
            Assert.Equal(GenerationUiText.Tile(kind, Counts()).Value, GenerationUiText.Tile(kind, Counts(), 2).Value);
    }

    [Theory]
    [InlineData(0, "0 waves", "0 story details")]
    [InlineData(1, "1 wave", "1 story detail.")]
    [InlineData(2, "2 waves", "2 story details")]
    [InlineData(1000, "1,000 waves", "1,000 story details")]
    public void Tile_Relevant_Plurals(int n, string subLine, string ledger)
    {
        var tile = GenerationUiText.Tile(GenerationTileKind.Relevant, Counts(waves: n, ledger: n));
        Assert.Equal(subLine, tile.SubLine);
        Assert.Contains(ledger, tile.Tooltip);
    }

    [Fact]
    public void Tile_NonRelevant_AllCategories()
    {
        var c = new GenerationCounts
        {
            NonRelevantTotal = 40, NonRelevantDone = 30, NonRelevantDailyBusiness = 20, NonRelevantAncillary = 8,
            NonRelevantBulk = 10, NonRelevantDecoy = 2
        };
        var tile = GenerationUiText.Tile(GenerationTileKind.NonRelevant, c);

        Assert.Equal("Non-relevant", tile.Caption);
        Assert.Equal("30 / 40", tile.Value);
        Assert.Equal("threads", tile.SubLine);
        Assert.Equal(
            "30 of 40 non-relevant threads written: 20 daily business, 8 ancillary, 10 newsletters, spam and personal, 2 near-miss decoys.",
            tile.Tooltip);
        Assert.Equal(TileTone.Normal, tile.Tone);
    }

    [Fact]
    public void Tile_NonRelevant_ZeroCategoriesDropped()
    {
        var c = new GenerationCounts { NonRelevantTotal = 21, NonRelevantDone = 0, NonRelevantDailyBusiness = 20, NonRelevantDecoy = 1 };
        Assert.Equal("0 of 21 non-relevant threads written: 20 daily business, 1 near-miss decoy.",
            GenerationUiText.Tile(GenerationTileKind.NonRelevant, c).Tooltip);
    }

    [Fact]
    public void Tile_NonRelevant_NonePlanned()
    {
        var tile = GenerationUiText.Tile(GenerationTileKind.NonRelevant, new GenerationCounts());
        Assert.Equal("0 / 0", tile.Value);
        Assert.Equal("0 of 0 non-relevant threads written.", tile.Tooltip);
    }

    [Fact]
    public void Tile_Attachments_AllKinds_WithSkipped()
    {
        var c = new GenerationCounts
        {
            AttachmentsTotal = 18, AttachmentsDone = 13, AttachmentsSkipped = 2, AttachmentsWord = 5, AttachmentsExcel = 3,
            AttachmentsPowerPoint = 2, AttachmentsInvite = 4, AttachmentsImage = 3, AttachmentsVoicemail = 1
        };
        var tile = GenerationUiText.Tile(GenerationTileKind.Attachments, c);

        Assert.Equal("Attachments", tile.Caption);
        Assert.Equal("13 / 18", tile.Value);
        Assert.Equal("2 skipped", tile.SubLine);
        Assert.Equal(
            "13 of 18 attachments written: 5 Word, 3 Excel, 2 PowerPoint, 4 invites, 3 images, 1 voicemail. 2 items skipped because images and voicemail were unavailable.",
            tile.Tooltip);
    }

    [Fact]
    public void Tile_Attachments_NoSkipped_ZeroKindsDropped()
    {
        var c = new GenerationCounts { AttachmentsTotal = 6, AttachmentsDone = 6, AttachmentsWord = 5, AttachmentsInvite = 1 };
        var tile = GenerationUiText.Tile(GenerationTileKind.Attachments, c);

        Assert.Equal("files and media", tile.SubLine);
        Assert.Equal("6 of 6 attachments written: 5 Word, 1 invite.", tile.Tooltip);
    }

    [Fact]
    public void Tile_Attachments_NonePlanned()
    {
        var tile = GenerationUiText.Tile(GenerationTileKind.Attachments, new GenerationCounts());
        Assert.Equal("0 / 0", tile.Value);
        Assert.Equal("none planned", tile.SubLine);
    }

    [Theory]
    [InlineData(1, "1 skipped", "1 item skipped")]
    [InlineData(2, "2 skipped", "2 items skipped")]
    [InlineData(1000, "1,000 skipped", "1,000 items skipped")]
    public void Tile_Attachments_SkippedPlurals(int n, string subLine, string tooltipPart)
    {
        var tile = GenerationUiText.Tile(GenerationTileKind.Attachments, new GenerationCounts { AttachmentsTotal = 5000, AttachmentsSkipped = n });
        Assert.Equal(subLine, tile.SubLine);
        Assert.Contains(tooltipPart, tile.Tooltip);
    }

    [Fact]
    public void Tile_Emails()
    {
        var tile = GenerationUiText.Tile(GenerationTileKind.Emails, Counts(emailsWritten: 1200, emailsPlanned: 3400, files: 4000, mailboxes: 6));

        Assert.Equal("Emails", tile.Caption);
        Assert.Equal("1,200", tile.Value);
        Assert.Equal("4,000 EML files", tile.SubLine);
        Assert.Equal(
            "1,200 of 3,400 planned emails written, saved as 4,000 .eml files in 6 custodian mailboxes under the Mailboxes folder.",
            tile.Tooltip);
    }

    [Theory]
    [InlineData(0, "0 EML files", "0 .eml files in 0 custodian mailboxes")]
    [InlineData(1, "1 EML file", "1 .eml file in 1 custodian mailbox ")]
    [InlineData(2, "2 EML files", "2 .eml files in 2 custodian mailboxes")]
    [InlineData(1000, "1,000 EML files", "1,000 .eml files in 1,000 custodian mailboxes")]
    public void Tile_Emails_Plurals(int n, string subLine, string tooltipPart)
    {
        var tile = GenerationUiText.Tile(GenerationTileKind.Emails, Counts(files: n, mailboxes: n));
        Assert.Equal(subLine, tile.SubLine);
        Assert.Contains(tooltipPart, tile.Tooltip);
    }

    [Fact]
    public void Tile_Warnings_CountsItems()
    {
        var c = Counts(warnings: 2);
        c.AttachmentsWarnings = 3;
        var tile = GenerationUiText.Tile(GenerationTileKind.Warnings, c);

        Assert.Equal("Warnings", tile.Caption);
        Assert.Equal("5", tile.Value);
        Assert.Equal("items kept", tile.SubLine);
        Assert.Equal(TileTone.Warning, tile.Tone);
        Assert.Equal(
            "2 threads and 3 attachments still broke a consistency check after 3 attempts. The best attempt was kept and marked \"Generated - with warnings\" in the answer key.",
            tile.Tooltip);
    }

    [Fact]
    public void Tile_Warnings_ZeroClauseDroppedWithItsAnd()
    {
        var threadsOnly = GenerationUiText.Tile(GenerationTileKind.Warnings, Counts(warnings: 1));
        Assert.StartsWith("1 thread still broke a consistency check", threadsOnly.Tooltip);

        var c = Counts();
        c.AttachmentsWarnings = 1;
        Assert.StartsWith("1 attachment still broke a consistency check", GenerationUiText.Tile(GenerationTileKind.Warnings, c).Tooltip);

        var none = GenerationUiText.Tile(GenerationTileKind.Warnings, Counts());
        Assert.Equal("0", none.Value);
        Assert.Equal(TileTone.Normal, none.Tone);
        Assert.StartsWith("Threads and attachments that still broke a consistency check after 3 attempts.", none.Tooltip);
    }

    [Fact]
    public void Tile_Failed_CountsItems()
    {
        var c = Counts(failed: 1);
        c.AttachmentsFailed = 2;
        var tile = GenerationUiText.Tile(GenerationTileKind.Failed, c);

        Assert.Equal("Failed", tile.Caption);
        Assert.Equal("3", tile.Value);
        Assert.Equal("items not written", tile.SubLine);
        Assert.Equal(TileTone.Danger, tile.Tone);
        Assert.Equal(
            "1 thread and 2 attachments could not be written. An email whose attachment failed is saved without it. Retry them with Continue Generating or Retry Failed Items.",
            tile.Tooltip);
    }

    [Fact]
    public void Tile_Failed_ZeroClauseDroppedWithItsAnd()
    {
        var c = Counts();
        c.AttachmentsFailed = 1000;
        Assert.StartsWith("1,000 attachments could not be written.", GenerationUiText.Tile(GenerationTileKind.Failed, c).Tooltip);
        Assert.StartsWith("2 threads could not be written.", GenerationUiText.Tile(GenerationTileKind.Failed, Counts(failed: 2)).Tooltip);

        var none = GenerationUiText.Tile(GenerationTileKind.Failed, Counts());
        Assert.Equal(TileTone.Normal, none.Tone);
        Assert.StartsWith("Threads and attachments that could not be written.", none.Tooltip);
    }

    [Fact]
    public void Tile_SkippedIsNotFailed()
    {
        var c = Counts();
        c.AttachmentsSkipped = 4;
        Assert.Equal("0", GenerationUiText.Tile(GenerationTileKind.Failed, c).Value);
        Assert.Equal("0", GenerationUiText.Tile(GenerationTileKind.Warnings, c).Value);
    }

    [Fact]
    public void Tile_Cost_Running_NoMedia()
    {
        var tile = GenerationUiText.Tile(GenerationTileKind.Cost, Counts(cost: 12.345m, remaining: 40m, running: true));

        Assert.Equal("Cost", tile.Caption);
        Assert.Equal("$12.35", tile.Value);
        Assert.Equal("≈$40.00 to go", tile.SubLine);
        Assert.Equal(
            "Spent on generation in this case: $12.35. Estimate for the remaining items: ≈$40.00, ±50%. The cost ticker in the header shows the whole session.",
            tile.Tooltip);
        Assert.Equal(TileTone.Normal, tile.Tone);
    }

    [Fact]
    public void Tile_Cost_WithMedia_SplitsTextAndMedia()
    {
        var c = Counts(cost: 5m, remaining: 1m, running: true);
        c.MediaPlanned = 4;
        c.MediaCostUsd = 0.4m;

        Assert.Equal(
            "Spent on generation in this case: $5.00 (text $4.60, images and voicemail $0.40). Estimate for the remaining items: ≈$1.00, ±50%. The cost ticker in the header shows the whole session.",
            GenerationUiText.Tile(GenerationTileKind.Cost, c).Tooltip);
    }

    [Fact]
    public void Tile_Cost_LargeAndFinished()
    {
        var large = GenerationUiText.Tile(GenerationTileKind.Cost, Counts(cost: 250m, remaining: 120m, running: true));
        Assert.Equal("$250", large.Value);
        Assert.Equal("≈$120 to go", large.SubLine);

        var finished = GenerationUiText.Tile(GenerationTileKind.Cost, Counts(cost: 8m, finished: true));
        Assert.Equal("$8.00", finished.Value);
        Assert.Equal("this case", finished.SubLine);
    }

    [Fact]
    public void Tile_OtherKinds_AreNormalTone()
    {
        var c = Counts(warnings: 5, failed: 5);
        c.AttachmentsFailed = 3;
        c.AttachmentsSkipped = 3;
        foreach (var kind in new[] { GenerationTileKind.Relevant, GenerationTileKind.NonRelevant, GenerationTileKind.Attachments, GenerationTileKind.Emails, GenerationTileKind.Cost })
            Assert.Equal(TileTone.Normal, GenerationUiText.Tile(kind, c).Tone);
    }

    [Fact]
    public void TileContent_DefaultsToNormalTone()
    {
        Assert.Equal(TileTone.Normal, new TileContent().Tone);
    }

    // --- Waves and stage rows ---

    [Fact]
    public void WaveDates_SameYear()
    {
        Assert.Equal("Mar 4 – Mar 17, 2024", GenerationUiText.WaveDates(new DateOnly(2024, 3, 4), new DateOnly(2024, 3, 17)));
    }

    [Fact]
    public void WaveDates_AcrossYears()
    {
        Assert.Equal("Dec 28, 2023 – Jan 10, 2024", GenerationUiText.WaveDates(new DateOnly(2023, 12, 28), new DateOnly(2024, 1, 10)));
    }

    [Fact]
    public void WaveDates_SameDay()
    {
        Assert.Equal("Mar 4, 2024", GenerationUiText.WaveDates(new DateOnly(2024, 3, 4), new DateOnly(2024, 3, 4)));
    }

    [Theory]
    [InlineData(0, "0 threads, 0 emails")]
    [InlineData(1, "1 thread, 1 email, 1 attachment")]
    [InlineData(2, "2 threads, 2 emails, 2 attachments")]
    [InlineData(1000, "1,000 threads, 1,000 emails, 1,000 attachments")]
    public void WaveTooltip_Plurals(int n, string expected)
    {
        Assert.Equal(expected, GenerationUiText.WaveTooltip(n, n, n));
    }

    [Fact]
    public void StageRow_Wave()
    {
        var row = new WaveProgress
        {
            Index = 2, Start = new DateOnly(2024, 3, 4), End = new DateOnly(2024, 3, 17), Threads = 8, Emails = 20, Done = 3, Attachments = 4
        };
        var (dates, done, tooltip) = GenerationUiText.StageRowText(row);

        Assert.Equal("Mar 4 – Mar 17, 2024", dates);
        Assert.Equal("3 / 8", done);
        Assert.Equal("Mar 4 – Mar 17, 2024" + Environment.NewLine + "8 threads, 20 emails, 4 attachments", tooltip);

        row.Attachments = 0;
        Assert.EndsWith("8 threads, 20 emails", GenerationUiText.StageRowText(row).Tooltip);
    }

    [Fact]
    public void StageRow_NonRelevant()
    {
        var row = new WaveProgress { RowKind = StageRowKind.NonRelevant, Index = 99, Threads = 40, Emails = 95, Done = 1000, Attachments = 6 };
        var (dates, done, tooltip) = GenerationUiText.StageRowText(row);

        Assert.Equal("Non-relevant mail", dates);
        Assert.Equal("1,000 / 40", done);
        Assert.Equal("40 threads, 95 emails, 6 attachments, across all dates", tooltip);

        row.Attachments = 0;
        Assert.Equal("40 threads, 95 emails, across all dates", GenerationUiText.StageRowText(row).Tooltip);
    }

    [Fact]
    public void StageRow_Attachments_CountsRepairsOnly()
    {
        var row = new WaveProgress { RowKind = StageRowKind.Attachments, Threads = 3, Done = 3, Attachments = 12, AttachmentsDone = 5 };
        var (dates, done, tooltip) = GenerationUiText.StageRowText(row);

        Assert.Equal("Attachments for written threads", dates);
        Assert.Equal("5 / 12", done);
        Assert.Equal("12 attachments on 3 threads that were written earlier. Their emails are rewritten in every mailbox.", tooltip);
    }

    [Theory]
    [InlineData(0, "0 attachments on 0 threads")]
    [InlineData(1, "1 attachment on 1 thread ")]
    [InlineData(2, "2 attachments on 2 threads")]
    [InlineData(1000, "1,000 attachments on 1,000 threads")]
    public void StageRow_Attachments_Plurals(int n, string expected)
    {
        var row = new WaveProgress { RowKind = StageRowKind.Attachments, Threads = n, Attachments = n };
        Assert.StartsWith(expected, GenerationUiText.StageRowText(row).Tooltip);
    }

    // --- Activity ---

    private static GenerationEvent Event(GenerationEventKind kind) => new() { Kind = kind, LocalTime = new DateTime(2026, 10, 3, 14, 5, 9) };

    [Fact]
    public void Activity_RunStarted()
    {
        var e = Event(GenerationEventKind.RunStarted);
        e.Threads = 120;
        e.WaveCount = 14;
        e.Attachments = 30;

        var line = GenerationUiText.ActivityText(e);
        Assert.Equal("Started: 120 threads and 30 attachments to write", line.Text);
        Assert.Equal(ActivityTone.Accent, line.Tone);
        Assert.Equal(line.Text, line.Tooltip);

        e.Attachments = 0;
        Assert.Equal("Started: 120 threads to write", GenerationUiText.ActivityText(e).Text);
    }

    [Fact]
    public void Activity_RunContinued()
    {
        var e = Event(GenerationEventKind.RunContinued);
        e.Threads = 1;

        var line = GenerationUiText.ActivityText(e);
        Assert.Equal("Continued: 1 thread left to write", line.Text);
        Assert.Equal(ActivityTone.Accent, line.Tone);

        e.Attachments = 2;
        Assert.Equal("Continued: 1 thread and 2 attachments left to write", GenerationUiText.ActivityText(e).Text);
    }

    [Fact]
    public void Activity_WaveStarted()
    {
        var e = Event(GenerationEventKind.WaveStarted);
        e.WaveIndex = 3;
        e.WaveCount = 14;
        e.WaveStart = new DateOnly(2024, 3, 4);
        e.WaveEnd = new DateOnly(2024, 3, 17);
        e.Threads = 8;

        var line = GenerationUiText.ActivityText(e);
        Assert.Equal("Wave 3 of 14 started (Mar 4 – Mar 17, 2024, 8 threads)", line.Text);
        Assert.Equal(ActivityTone.Accent, line.Tone);
    }

    [Fact]
    public void Activity_ThreadWritten_Relevant()
    {
        var e = Event(GenerationEventKind.ThreadWritten);
        e.ThreadId = "T-0042";
        e.Subject = "Budget";
        e.Emails = 1;
        e.Details = 0;
        e.Relevant = true;

        var line = GenerationUiText.ActivityText(e);
        Assert.Equal("Written: \"Budget\", 1 email, 0 new ledger details", line.Text);
        Assert.Equal(ActivityTone.Success, line.Tone);

        e.Attachments = 2;
        Assert.Equal("Written: \"Budget\", 1 email, 0 new ledger details, 2 attachments", GenerationUiText.ActivityText(e).Text);
    }

    [Fact]
    public void Activity_ThreadWritten_NonRelevant_NoLedgerClause()
    {
        var e = Event(GenerationEventKind.ThreadWritten);
        e.Subject = "Lunch order";
        e.Emails = 3;
        e.Details = 5;
        e.Relevant = false;

        var line = GenerationUiText.ActivityText(e);
        Assert.Equal("Written: \"Lunch order\", 3 emails", line.Text);
        Assert.Equal(ActivityTone.Success, line.Tone);

        e.Attachments = 1;
        Assert.Equal("Written: \"Lunch order\", 3 emails, 1 attachment", GenerationUiText.ActivityText(e).Text);
    }

    [Fact]
    public void Activity_Retry_FirstViolationInText_AllInTooltip()
    {
        var e = Event(GenerationEventKind.Retry);
        e.Attempt = 2;
        e.Violations = new() { "names \"X\", who is not in the cast", "b" };

        var line = GenerationUiText.ActivityText(e);
        Assert.Equal("Attempt 2 of 3 failed 2 checks: names \"X\", who is not in the cast. Retrying.", line.Text);
        Assert.Equal(ActivityTone.Warning, line.Tone);

        var lines = line.Tooltip.Split(Environment.NewLine);
        Assert.Equal(new[] { line.Text, "names \"X\", who is not in the cast", "b" }, lines);
    }

    [Fact]
    public void Activity_KeptWithWarnings()
    {
        var e = Event(GenerationEventKind.KeptWithWarnings);
        e.Violations = new() { "has 3 messages; the plan has 4" };

        var line = GenerationUiText.ActivityText(e);
        Assert.Equal("Kept with 1 warning after 3 attempts: has 3 messages; the plan has 4", line.Text);
        Assert.Equal(ActivityTone.Warning, line.Tone);
        Assert.Equal(line.Text + Environment.NewLine + "has 3 messages; the plan has 4", line.Tooltip);
    }

    [Fact]
    public void Activity_ThreadFailed()
    {
        var e = Event(GenerationEventKind.ThreadFailed);
        e.Reason = "The model refused the request.";

        var line = GenerationUiText.ActivityText(e);
        Assert.Equal("Failed: The model refused the request. The run continues.", line.Text);
        Assert.Equal(ActivityTone.Danger, line.Tone);
    }

    [Theory]
    [InlineData(0, 0, ActivityTone.Success)]
    [InlineData(2, 0, ActivityTone.Warning)]
    [InlineData(2, 1, ActivityTone.Danger)]
    [InlineData(0, 1, ActivityTone.Danger)]
    public void Activity_WaveFinished_Tone(int warnings, int failed, ActivityTone tone)
    {
        var e = Event(GenerationEventKind.WaveFinished);
        e.WaveIndex = 4;
        e.Written = 6;
        e.Warnings = warnings;
        e.Failed = failed;
        e.LedgerTotal = 1234;

        var line = GenerationUiText.ActivityText(e);
        Assert.Equal($"Wave 4 finished: 6 written, {warnings} with warnings, {failed} failed. Ledger now 1,234 details.", line.Text);
        Assert.Equal(tone, line.Tone);
    }

    [Fact]
    public void Activity_SideProjectsReady()
    {
        var e = Event(GenerationEventKind.SideProjectsReady);
        e.Projects = 6;
        e.Organizations = 1;

        var line = GenerationUiText.ActivityText(e);
        Assert.Equal("Side projects ready: 6 projects for 1 organization", line.Text);
        Assert.Equal(ActivityTone.Success, line.Tone);
    }

    [Fact]
    public void Activity_NonRelevantStarted()
    {
        var e = Event(GenerationEventKind.NonRelevantStarted);
        e.Threads = 40;

        var line = GenerationUiText.ActivityText(e);
        Assert.Equal("Non-relevant mail started: 40 threads to write", line.Text);
        Assert.Equal(ActivityTone.Accent, line.Tone);
    }

    [Theory]
    [InlineData(0, 0, ActivityTone.Success)]
    [InlineData(2, 0, ActivityTone.Warning)]
    [InlineData(0, 1, ActivityTone.Danger)]
    public void Activity_NonRelevantFinished_Tone(int warnings, int failed, ActivityTone tone)
    {
        var e = Event(GenerationEventKind.NonRelevantFinished);
        e.Written = 37;
        e.Warnings = warnings;
        e.Failed = failed;

        var line = GenerationUiText.ActivityText(e);
        Assert.Equal($"Non-relevant mail finished: 37 written, {warnings} with warnings, {failed} failed.", line.Text);
        Assert.Equal(tone, line.Tone);
    }

    [Fact]
    public void Activity_AttachmentRetry()
    {
        var e = Event(GenerationEventKind.AttachmentRetry);
        e.FileName = "Budget Q3.xlsx";
        e.Attempt = 1;
        e.Violations = new() { "mentions \"Project X\", which this email must not mention", "b" };

        var line = GenerationUiText.ActivityText(e);
        Assert.Equal("Budget Q3.xlsx: attempt 1 of 3 failed 2 checks: mentions \"Project X\", which this email must not mention. Retrying.", line.Text);
        Assert.Equal(ActivityTone.Warning, line.Tone);
        Assert.Equal(3, line.Tooltip.Split(Environment.NewLine).Length);
    }

    [Fact]
    public void Activity_AttachmentKeptWithWarnings()
    {
        var e = Event(GenerationEventKind.AttachmentKeptWithWarnings);
        e.FileName = "Kickoff.ics";
        e.Violations = new() { "invite not tied to a known meeting" };

        var line = GenerationUiText.ActivityText(e);
        Assert.Equal("Kickoff.ics kept with 1 warning after 3 attempts: invite not tied to a known meeting", line.Text);
        Assert.Equal(ActivityTone.Warning, line.Tone);
    }

    [Fact]
    public void Activity_AttachmentFailed()
    {
        var e = Event(GenerationEventKind.AttachmentFailed);
        e.FileName = "Site photo.png";
        e.Reason = "the request timed out";

        var line = GenerationUiText.ActivityText(e);
        Assert.Equal("Site photo.png failed: the request timed out. The email was saved without it.", line.Text);
        Assert.Equal(ActivityTone.Danger, line.Tone);
    }

    [Fact]
    public void Activity_AttachmentSkipped()
    {
        var e = Event(GenerationEventKind.AttachmentSkipped);
        e.FileName = "Voicemail 2024-03-04 2-15 PM.mp3";
        e.Reason = "no tested OpenAI key this session";

        var line = GenerationUiText.ActivityText(e);
        Assert.Equal("Voicemail 2024-03-04 2-15 PM.mp3 skipped: no tested OpenAI key this session.", line.Text);
        Assert.Equal(ActivityTone.Warning, line.Tone);
    }

    [Fact]
    public void Activity_MediaStopped()
    {
        var e = Event(GenerationEventKind.MediaStopped);
        e.Reason = "the OpenAI key was rejected";
        e.MediaRemaining = 3;

        var line = GenerationUiText.ActivityText(e);
        Assert.Equal(
            "Images and voicemail stopped: the OpenAI key was rejected. Text generation continues; the remaining 3 images and voicemails are marked Skipped.",
            line.Text);
        Assert.Equal(ActivityTone.Warning, line.Tone);
    }

    [Theory]
    [InlineData(0, "the remaining 0 images and voicemails are marked")]
    [InlineData(1, "the remaining 1 image or voicemail is marked")]
    [InlineData(2, "the remaining 2 images and voicemails are marked")]
    [InlineData(1000, "the remaining 1,000 images and voicemails are marked")]
    public void Activity_MediaStopped_Plurals(int n, string expected)
    {
        var e = Event(GenerationEventKind.MediaStopped);
        e.MediaRemaining = n;
        Assert.Contains(expected, GenerationUiText.ActivityText(e).Text);
    }

    [Fact]
    public void Activity_AttachmentRepairStarted()
    {
        var e = Event(GenerationEventKind.AttachmentRepairStarted);
        e.Attachments = 12;
        e.Threads = 1;

        var line = GenerationUiText.ActivityText(e);
        Assert.Equal("Adding 12 missing attachments to 1 written thread", line.Text);
        Assert.Equal(ActivityTone.Accent, line.Tone);
    }

    [Fact]
    public void Activity_AttachmentsAdded()
    {
        var e = Event(GenerationEventKind.AttachmentsAdded);
        e.Subject = "Budget";
        e.Attachments = 1;
        e.Files = 4;

        var line = GenerationUiText.ActivityText(e);
        Assert.Equal("Added 1 attachment to \"Budget\" and rewrote 4 EML files", line.Text);
        Assert.Equal(ActivityTone.Success, line.Tone);
    }

    [Fact]
    public void Activity_AttachmentRegenerated()
    {
        var e = Event(GenerationEventKind.AttachmentRegenerated);
        e.FileName = "Scope letter.docx";

        var line = GenerationUiText.ActivityText(e);
        Assert.Equal("Scope letter.docx could not be reused and was created again", line.Text);
        Assert.Equal(ActivityTone.Warning, line.Tone);
    }

    [Fact]
    public void Activity_RunCancelled()
    {
        var e = Event(GenerationEventKind.RunCancelled);
        e.Done = 4;
        e.Total = 1000;

        var line = GenerationUiText.ActivityText(e);
        Assert.Equal("Cancelled. 4 of 1,000 threads are saved.", line.Text);
        Assert.Equal(ActivityTone.Warning, line.Tone);

        e.AttachmentsDone = 3;
        e.AttachmentsTotal = 12;
        Assert.Equal("Cancelled. 4 of 1,000 threads and 3 of 12 attachments are saved.", GenerationUiText.ActivityText(e).Text);
    }

    [Fact]
    public void Activity_NotSaved_ShowsReasonAsDanger()
    {
        var e = Event(GenerationEventKind.NotSaved);
        e.Reason = "Could not save the run journal: disk full";

        var line = GenerationUiText.ActivityText(e);
        Assert.Equal("Not saved: Could not save the run journal: disk full. It stays pending for the next Continue.", line.Text);
        Assert.Equal(ActivityTone.Danger, line.Tone);
    }

    [Fact]
    public void Activity_RunStopped()
    {
        var e = Event(GenerationEventKind.RunStopped);
        e.Reason = "Invalid API key";

        var line = GenerationUiText.ActivityText(e);
        Assert.Equal("Stopped: Invalid API key", line.Text);
        Assert.Equal(ActivityTone.Danger, line.Tone);
    }

    [Fact]
    public void Activity_RunFinished()
    {
        var line = GenerationUiText.ActivityText(Event(GenerationEventKind.RunFinished));
        Assert.Equal("Finished. Answer key and story so far updated.", line.Text);
        Assert.Equal(ActivityTone.Success, line.Tone);
    }

    [Fact]
    public void Activity_EveryKindHasText_NoBannedWords()
    {
        foreach (var kind in Enum.GetValues<GenerationEventKind>())
        {
            var text = GenerationUiText.ActivityText(Event(kind)).Text;
            Assert.False(string.IsNullOrWhiteSpace(text), kind.ToString());
            AssertNoBannedWords(text);
        }
    }

    [Theory]
    [InlineData(0, "0 emails, 0 new ledger details")]
    [InlineData(1, "1 email, 1 new ledger detail, 1 attachment")]
    [InlineData(2, "2 emails, 2 new ledger details, 2 attachments")]
    [InlineData(1000, "1,000 emails, 1,000 new ledger details, 1,000 attachments")]
    public void Activity_ThreadWritten_Plurals(int n, string expected)
    {
        var e = Event(GenerationEventKind.ThreadWritten);
        e.Subject = "S";
        e.Emails = n;
        e.Details = n;
        e.Attachments = n;
        e.Relevant = true;
        Assert.EndsWith(expected, GenerationUiText.ActivityText(e).Text);
    }

    [Theory]
    [InlineData(0, "failed 0 checks", "Kept with 0 warnings")]
    [InlineData(1, "failed 1 check:", "Kept with 1 warning ")]
    [InlineData(2, "failed 2 checks", "Kept with 2 warnings")]
    [InlineData(1000, "failed 1,000 checks", "Kept with 1,000 warnings")]
    public void Activity_ViolationPlurals(int n, string retryPart, string keptPart)
    {
        var violations = Enumerable.Range(1, n).Select(i => $"v{i}").ToList();
        var retry = Event(GenerationEventKind.Retry);
        retry.Attempt = 1;
        retry.Violations = violations;
        var kept = Event(GenerationEventKind.KeptWithWarnings);
        kept.Violations = violations;

        Assert.Contains(retryPart, GenerationUiText.ActivityText(retry).Text);
        Assert.Contains(keptPart, GenerationUiText.ActivityText(kept).Text);
    }

    [Theory]
    [InlineData(0, "0 threads and 0 attachments", "0 projects for 0 organizations", "Adding 0 missing attachments to 0 written threads")]
    [InlineData(1, "1 thread and 1 attachment", "1 project for 1 organization", "Adding 1 missing attachment to 1 written thread")]
    [InlineData(2, "2 threads and 2 attachments", "2 projects for 2 organizations", "Adding 2 missing attachments to 2 written threads")]
    [InlineData(1000, "1,000 threads and 1,000 attachments", "1,000 projects for 1,000 organizations", "Adding 1,000 missing attachments to 1,000 written threads")]
    public void Activity_NewTemplatePlurals(int n, string started, string projects, string repair)
    {
        var run = Event(GenerationEventKind.RunStarted);
        run.Threads = n;
        // A zero attachment count drops the clause, so 0 uses the continued form to check the plural
        run.Attachments = n == 0 ? 0 : n;
        var side = Event(GenerationEventKind.SideProjectsReady);
        side.Projects = n;
        side.Organizations = n;
        var fix = Event(GenerationEventKind.AttachmentRepairStarted);
        fix.Attachments = n;
        fix.Threads = n;

        if (n > 0)
            Assert.Equal("Started: " + started + " to write", GenerationUiText.ActivityText(run).Text);
        Assert.EndsWith(projects, GenerationUiText.ActivityText(side).Text);
        Assert.Equal(repair, GenerationUiText.ActivityText(fix).Text);
    }

    [Theory]
    [InlineData(0, "Added 0 attachments to \"S\" and rewrote 0 EML files")]
    [InlineData(1, "Added 1 attachment to \"S\" and rewrote 1 EML file")]
    [InlineData(2, "Added 2 attachments to \"S\" and rewrote 2 EML files")]
    [InlineData(1000, "Added 1,000 attachments to \"S\" and rewrote 1,000 EML files")]
    public void Activity_AttachmentsAdded_Plurals(int n, string expected)
    {
        var e = Event(GenerationEventKind.AttachmentsAdded);
        e.Subject = "S";
        e.Attachments = n;
        e.Files = n;
        Assert.Equal(expected, GenerationUiText.ActivityText(e).Text);
    }

    // --- Progress ---

    private static GenerationView View(GenerationStage stage, int count) => new()
    {
        Stage = stage,
        StageWave = 2,
        StageWaveCount = 5,
        StageWaveStart = new DateOnly(2024, 3, 4),
        StageWaveEnd = new DateOnly(2024, 3, 17),
        StageCount = count
    };

    [Fact]
    public void ProgressText_EveryStage()
    {
        Assert.Equal("Wave 2 of 5 (Mar 4 – Mar 17, 2024): writing 3 threads...", GenerationUiText.ProgressText(View(GenerationStage.Writing, 3)));
        Assert.Equal("Wave 2 of 5: merging 4 new ledger details...", GenerationUiText.ProgressText(View(GenerationStage.Merging, 4)));
        Assert.Equal("Updating the answer key...", GenerationUiText.ProgressText(View(GenerationStage.UpdatingKey, 0)));
        Assert.Equal("Writing the story so far...", GenerationUiText.ProgressText(View(GenerationStage.WritingStory, 0)));
        Assert.Equal("Stopping... finished threads and attachments are saved.", GenerationUiText.ProgressText(View(GenerationStage.Stopping, 0)));
        Assert.Equal(GenerationUiText.StoppingText, GenerationUiText.ProgressText(View(GenerationStage.Stopping, 0)));
        Assert.Equal("", GenerationUiText.ProgressText(View(GenerationStage.Idle, 0)));
    }

    [Fact]
    public void ProgressText_Writing_AttachmentClause()
    {
        var v = View(GenerationStage.Writing, 3);
        v.StageAttachments = 5;
        Assert.Equal("Wave 2 of 5 (Mar 4 – Mar 17, 2024): writing 3 threads and 5 attachments...", GenerationUiText.ProgressText(v));
    }

    [Fact]
    public void ProgressText_SideProjects()
    {
        Assert.Equal("Preparing side projects for 3 organizations...", GenerationUiText.ProgressText(View(GenerationStage.SideProjects, 3)));
    }

    [Fact]
    public void ProgressText_NonRelevant_FromCounts()
    {
        var v = View(GenerationStage.NonRelevant, 0);
        v.Counts = new GenerationCounts { NonRelevantTotal = 1200, NonRelevantDone = 30 };
        Assert.Equal("Non-relevant mail: 30 of 1,200 threads written...", GenerationUiText.ProgressText(v));
    }

    [Fact]
    public void ProgressText_Attachments_FromRepairRow()
    {
        var v = View(GenerationStage.Attachments, 0);
        v.Waves = new List<WaveProgress>
        {
            new() { Index = 1, Threads = 5, Attachments = 9 },
            new() { RowKind = StageRowKind.Attachments, Index = 3, Attachments = 12, AttachmentsDone = 3, AttachmentsFailed = 1, AttachmentsSkipped = 1 }
        };
        Assert.Equal("Adding missing attachments: 5 of 12 attachments...", GenerationUiText.ProgressText(v));
    }

    [Theory]
    [InlineData(0, "writing 0 threads...", "merging 0 new ledger details...", "for 0 organizations...")]
    [InlineData(1, "writing 1 thread and 1 attachment...", "merging 1 new ledger detail...", "for 1 organization...")]
    [InlineData(2, "writing 2 threads and 2 attachments...", "merging 2 new ledger details...", "for 2 organizations...")]
    [InlineData(1000, "writing 1,000 threads and 1,000 attachments...", "merging 1,000 new ledger details...", "for 1,000 organizations...")]
    public void ProgressText_Plurals(int n, string writing, string merging, string side)
    {
        var w = View(GenerationStage.Writing, n);
        w.StageAttachments = n;
        Assert.EndsWith(writing, GenerationUiText.ProgressText(w));
        Assert.EndsWith(merging, GenerationUiText.ProgressText(View(GenerationStage.Merging, n)));
        Assert.EndsWith(side, GenerationUiText.ProgressText(View(GenerationStage.SideProjects, n)));
    }

    [Theory]
    [InlineData(1, "0 of 1 thread written...", "0 of 1 attachment...")]
    [InlineData(2, "0 of 2 threads written...", "0 of 2 attachments...")]
    [InlineData(1000, "0 of 1,000 threads written...", "0 of 1,000 attachments...")]
    public void ProgressText_StagePlurals(int n, string nonRelevant, string attachments)
    {
        var nr = View(GenerationStage.NonRelevant, 0);
        nr.Counts = new GenerationCounts { NonRelevantTotal = n };
        var at = View(GenerationStage.Attachments, 0);
        at.Waves = new List<WaveProgress> { new() { RowKind = StageRowKind.Attachments, Attachments = n } };

        Assert.EndsWith(nonRelevant, GenerationUiText.ProgressText(nr));
        Assert.EndsWith(attachments, GenerationUiText.ProgressText(at));
    }

    // --- Outcomes ---

    [Fact]
    public void Outcome_FinishedClean()
    {
        var line = GenerationUiText.OutcomeText(GenerationOutcome.Finished, Counts(total: 10, done: 10, emailsWritten: 30, files: 37, mailboxes: 6), "");

        Assert.Equal("Done: 10 threads written as 30 emails (37 EML files in 6 mailboxes). The answer key and story so far are updated.", line.Text);
        Assert.Equal(ActivityTone.Success, line.Tone);
    }

    [Fact]
    public void Outcome_FinishedClean_WithAttachments()
    {
        var c = Counts(total: 10, done: 10, emailsWritten: 30, files: 37, mailboxes: 6);
        c.AttachmentsTotal = 18;
        c.AttachmentsDone = 18;
        Assert.Equal(
            "Done: 10 threads written as 30 emails (37 EML files in 6 mailboxes), with 18 attachments. The answer key and story so far are updated.",
            GenerationUiText.OutcomeText(GenerationOutcome.Finished, c, "").Text);
    }

    [Theory]
    [InlineData(0, "Done: 0 threads written as 0 emails (0 EML files in 0 mailboxes).")]
    [InlineData(1, "Done: 1 thread written as 1 email (1 EML file in 1 mailbox), with 1 attachment.")]
    [InlineData(2, "Done: 2 threads written as 2 emails (2 EML files in 2 mailboxes), with 2 attachments.")]
    [InlineData(1000, "Done: 1,000 threads written as 1,000 emails (1,000 EML files in 1,000 mailboxes), with 1,000 attachments.")]
    public void Outcome_FinishedClean_Plurals(int n, string expectedStart)
    {
        var c = Counts(total: n, done: n, emailsWritten: n, files: n, mailboxes: n);
        c.AttachmentsTotal = n;
        c.AttachmentsDone = n;
        Assert.StartsWith(expectedStart, GenerationUiText.OutcomeText(GenerationOutcome.Finished, c, "").Text);
    }

    [Fact]
    public void Outcome_FinishedWithWarningsOnly_DropsTail()
    {
        var line = GenerationUiText.OutcomeText(GenerationOutcome.Finished, Counts(total: 10, done: 10, warnings: 2), "");

        Assert.Equal("Done with 2 warnings. The answer key marks them.", line.Text);
        Assert.Equal(ActivityTone.Warning, line.Tone);
    }

    [Fact]
    public void Outcome_FinishedWithAllProblems_JoinsWithCommaAndAnd()
    {
        var c = Counts(total: 10, done: 9, warnings: 1, failed: 1);
        c.AttachmentsWarnings = 1;
        c.AttachmentsFailed = 2;
        c.AttachmentsSkipped = 4;

        var line = GenerationUiText.OutcomeText(GenerationOutcome.Finished, c, "");
        Assert.Equal(
            "Done with 2 warnings, 3 failed items and 4 skipped images and voicemails. The answer key marks them; click Retry Failed Items to try the failed ones again.",
            line.Text);
        Assert.Equal(ActivityTone.Warning, line.Tone);
    }

    [Fact]
    public void Outcome_FinishedWithFailures_TailOnlyWhenHeaderButtonShows()
    {
        var c = Counts(total: 10, done: 8, failed: 2);
        Assert.Equal(
            "Done with 2 failed items. The answer key marks them; click Retry Failed Items to try the failed ones again.",
            GenerationUiText.OutcomeText(GenerationOutcome.Finished, c, "").Text);
        Assert.Equal(
            "Done with 2 failed items. The answer key marks them.",
            GenerationUiText.OutcomeText(GenerationOutcome.Finished, c, "", retryButtonShown: false).Text);
    }

    [Fact]
    public void Outcome_FinishedWithSkippedOnly()
    {
        var c = Counts(total: 10, done: 10);
        c.AttachmentsSkipped = 1;
        Assert.Equal("Done with 1 skipped image or voicemail. The answer key marks them.",
            GenerationUiText.OutcomeText(GenerationOutcome.Finished, c, "").Text);
    }

    [Theory]
    [InlineData(1, "Done with 1 warning, 1 failed item and 1 skipped image or voicemail.")]
    [InlineData(2, "Done with 2 warnings, 2 failed items and 2 skipped images and voicemails.")]
    [InlineData(1000, "Done with 1,000 warnings, 1,000 failed items and 1,000 skipped images and voicemails.")]
    public void Outcome_ProblemPlurals(int n, string expectedStart)
    {
        var c = Counts(total: 5000, done: 1000, warnings: n, failed: n);
        c.AttachmentsSkipped = n;
        Assert.StartsWith(expectedStart, GenerationUiText.OutcomeText(GenerationOutcome.Finished, c, "").Text);
    }

    [Fact]
    public void Outcome_Cancelled()
    {
        var line = GenerationUiText.OutcomeText(GenerationOutcome.Cancelled, Counts(total: 10, done: 4), "");

        Assert.Equal("Cancelled. 4 of 10 threads are saved. Click Continue Generating to write the rest.", line.Text);
        Assert.Equal(ActivityTone.Warning, line.Tone);
    }

    [Fact]
    public void Outcome_Cancelled_WithAttachments()
    {
        var c = Counts(total: 10, done: 4);
        c.AttachmentsTotal = 12;
        c.AttachmentsDone = 3;
        Assert.Equal(
            "Cancelled. 4 of 10 threads and 3 of 12 attachments are saved. Click Continue Generating to write the rest.",
            GenerationUiText.OutcomeText(GenerationOutcome.Cancelled, c, "").Text);
    }

    [Fact]
    public void Outcome_Fatal_PointsToApiConfiguration()
    {
        var line = GenerationUiText.OutcomeText(GenerationOutcome.Fatal, Counts(), "Invalid API key");

        Assert.Equal("Generation stopped: Invalid API key. Finished threads and attachments are saved. Fix the problem on the API Configuration step, then click Continue Generating.", line.Text);
        Assert.Equal(ActivityTone.Danger, line.Tone);
    }

    [Fact]
    public void Outcome_InternalError_NeverMentionsApiConfiguration()
    {
        var line = GenerationUiText.OutcomeText(GenerationOutcome.InternalError, Counts(), "Object reference not set");

        Assert.Equal("Generation stopped because of an internal error: Object reference not set. Completed threads are saved. Click Continue Generating to try the remaining threads.", line.Text);
        Assert.Equal(ActivityTone.Danger, line.Tone);
        Assert.DoesNotContain("API Configuration", line.Text);
    }

    [Fact]
    public void Outcome_TrailingPeriodInMessageIsNotDoubled()
    {
        var line = GenerationUiText.OutcomeText(GenerationOutcome.Fatal, Counts(), "Spend limit reached.");
        Assert.StartsWith("Generation stopped: Spend limit reached. Finished", line.Text);
    }

    [Fact]
    public void Outcome_NoBannedWords()
    {
        var c = Counts(warnings: 1, failed: 1);
        c.AttachmentsSkipped = 1;
        c.AttachmentsTotal = 2;
        foreach (var outcome in Enum.GetValues<GenerationOutcome>())
            AssertNoBannedWords(GenerationUiText.OutcomeText(outcome, c, "m").Text);
    }

    // --- File and maintenance status lines ---

    [Fact]
    public void FileStatusLines()
    {
        Assert.Equal(
            "The answer key was not updated because answer-key.xlsx is open. Close it; the key is updated again when you leave this step.",
            GenerationUiText.AnswerKeyLockedText("answer-key.xlsx"));
        Assert.Equal(
            "Could not update answer-key.csv: Access denied. Your emails are saved; the file is written again when you leave this step.",
            GenerationUiText.FileWriteFailedText("answer-key.csv", "Access denied."));
        Assert.Equal("Cleared the earlier run: deleted 1,200 EML files, the narrative ledger and the side projects.", GenerationUiText.StartedOverText(1200));
        Assert.Equal("RD-000123.eml is no longer in the case folder.", GenerationUiText.MissingEmlText("RD-000123"));
    }

    [Theory]
    [InlineData(0, "deleted 0 EML files")]
    [InlineData(1, "deleted 1 EML file,")]
    [InlineData(2, "deleted 2 EML files")]
    [InlineData(1000, "deleted 1,000 EML files")]
    public void StartedOver_Plurals(int n, string expected)
    {
        Assert.Contains(expected, GenerationUiText.StartedOverText(n));
    }

    // --- Entry decision (first match wins) ---

    private static GenerationEntryInput Entry(
        bool running = false, bool hasPlan = true, bool approved = true, bool hasSavedRun = true,
        bool planChanged = false, string unreadable = "", int total = 10, int written = 10, int failed = 0,
        int attachmentsTotal = 0, int attachmentsFinal = 0, bool latestAttemptEnded = false) =>
        new()
        {
            Running = running,
            HasPlan = hasPlan,
            Approved = approved,
            HasSavedRun = hasSavedRun,
            PlanChanged = planChanged,
            UnreadableReason = unreadable,
            Total = total,
            Written = written,
            Failed = failed,
            AttachmentsTotal = attachmentsTotal,
            AttachmentsFinal = attachmentsFinal,
            LatestAttemptEnded = latestAttemptEnded
        };

    [Fact]
    public void EntryDecision_RunningKeepsRun_EvenWhenNotApproved()
    {
        Assert.Equal(GenerationEntry.KeepRun, GenerationUiText.EntryDecision(Entry(running: true, approved: false, planChanged: true)));
    }

    [Fact]
    public void EntryDecision_NoPlanOrNotApproved()
    {
        Assert.Equal(GenerationEntry.NotApprovedCard, GenerationUiText.EntryDecision(Entry(hasPlan: false)));
        Assert.Equal(GenerationEntry.NotApprovedCard, GenerationUiText.EntryDecision(Entry(approved: false, planChanged: true, unreadable: "x")));
    }

    [Fact]
    public void EntryDecision_PlanChangedBeforeUnreadable()
    {
        Assert.Equal(GenerationEntry.PlanChangedCard, GenerationUiText.EntryDecision(Entry(planChanged: true, unreadable: "bad")));
    }

    [Fact]
    public void EntryDecision_PlanChangedNeedsSavedRun()
    {
        Assert.Equal(GenerationEntry.ReadyCard, GenerationUiText.EntryDecision(Entry(hasSavedRun: false, planChanged: true, written: 0)));
    }

    [Fact]
    public void EntryDecision_Unreadable()
    {
        Assert.Equal(GenerationEntry.UnreadableCard, GenerationUiText.EntryDecision(Entry(unreadable: "The journal is larger than 200 MB")));
    }

    [Fact]
    public void EntryDecision_NoSavedRun_ReadyCard_NeverAutoStarts()
    {
        Assert.Equal(GenerationEntry.ReadyCard, GenerationUiText.EntryDecision(Entry(hasSavedRun: false, written: 0)));
    }

    [Fact]
    public void EntryDecision_UnfinishedRun_PartialDashboard()
    {
        Assert.Equal(GenerationEntry.PartialDashboard, GenerationUiText.EntryDecision(Entry(written: 4, failed: 2)));
    }

    [Fact]
    public void EntryDecision_EveryThreadWrittenOrFailed_FinishedDashboard()
    {
        Assert.Equal(GenerationEntry.FinishedDashboard, GenerationUiText.EntryDecision(Entry(written: 8, failed: 2)));
        Assert.Equal(GenerationEntry.FinishedDashboard, GenerationUiText.EntryDecision(Entry(written: 10)));
    }

    [Fact]
    public void EntryDecision_AttachmentWithoutFinalRecord_IsUnfinished()
    {
        Assert.Equal(GenerationEntry.PartialDashboard,
            GenerationUiText.EntryDecision(Entry(written: 10, attachmentsTotal: 12, attachmentsFinal: 9)));
    }

    [Fact]
    public void EntryDecision_SkippedMediaAlone_IsFinished()
    {
        // AttachmentsFinal counts Written + Warnings + Failed + Skipped: 10 written and 2 skipped
        Assert.Equal(GenerationEntry.FinishedDashboard,
            GenerationUiText.EntryDecision(Entry(written: 10, attachmentsTotal: 12, attachmentsFinal: 12)));
    }

    // --- State cards ---

    private static GenerationReadyInput Ready(
        int relevant = 12, int nonRelevant = 30, int emails = 95, int waves = 4, int attachments = 9,
        decimal story = 3.2m, decimal volume = 0.7m, decimal media = 0.2m, string storyModel = "Claude Sonnet",
        string volumeModel = "Claude Haiku", int mediaPlanned = 4, bool mediaAvailable = true, decimal? live = null,
        decimal batchStory = 1.6m, decimal batchVolume = 0.35m, decimal? batchTotal = null, bool batchChecked = false) =>
        new()
        {
            RelevantThreads = relevant,
            NonRelevantThreads = nonRelevant,
            Emails = emails,
            Waves = waves,
            Attachments = attachments,
            StoryCost = story,
            VolumeCost = volume,
            MediaCost = media,
            StoryModel = storyModel,
            VolumeModel = volumeModel,
            MediaPlanned = mediaPlanned,
            MediaAvailable = mediaAvailable,
            LiveTotal = live ?? story + volume + (mediaPlanned > 0 ? media : 0m),
            BatchStoryCost = batchStory,
            BatchVolumeCost = batchVolume,
            BatchTotal = batchTotal ?? batchStory + batchVolume + (mediaPlanned > 0 ? media : 0m),
            BatchChecked = batchChecked
        };

    [Fact]
    public void ReadyCard_FullPlan()
    {
        var card = GenerationUiText.ReadyCard(Ready());

        Assert.Equal("Ready to write the corpus", card.Heading);
        Assert.Equal(
            "Generate Emails writes the 12 relevant threads first, oldest first in 4 waves, then the 30 non-relevant threads: 95 emails in total, with 9 attachments. Each finished thread is saved straight to the custodians' mailboxes, so you can stop at any time and continue later.",
            card.Body);
        Assert.Equal("Estimated cost ≈$4.10 at live prices or ≈$2.15 with batch pricing, ±50%, including prompt caching.", card.EstimateLine);
        Assert.Equal(
            "Live: Claude Sonnet ≈$3.20 · Claude Haiku ≈$0.70 · images and voicemail ≈$0.20. Batch: Claude Sonnet ≈$1.60 · Claude Haiku ≈$0.35 · images and voicemail ≈$0.20. Images and voicemail are always priced live. Cache savings are included only where the shared prompt is long enough for the provider to cache it. The batch total assumes no cache savings.",
            card.EstimateTooltip);
        Assert.Equal("", card.BatchHint);
        Assert.Equal("", card.MediaNotice);
        Assert.Equal("Generate Emails", card.ButtonText);
        Assert.False(card.Danger);
    }

    [Fact]
    public void ReadyCard_SameModel_AndNoMedia()
    {
        var card = GenerationUiText.ReadyCard(Ready(volumeModel: "Claude Sonnet", mediaPlanned: 0, media: 0m));
        Assert.Equal("Estimated cost ≈$3.90 at live prices or ≈$1.95 with batch pricing, ±50%, including prompt caching.", card.EstimateLine);
        Assert.Equal(
            "Live: Claude Sonnet ≈$3.90. Batch: Claude Sonnet ≈$1.95. Images and voicemail are always priced live. Cache savings are included only where the shared prompt is long enough for the provider to cache it. The batch total assumes no cache savings.",
            card.EstimateTooltip);

        var withMedia = GenerationUiText.ReadyCard(Ready(volumeModel: "Claude Sonnet"));
        Assert.Equal("Estimated cost ≈$4.10 at live prices or ≈$2.15 with batch pricing, ±50%, including prompt caching.", withMedia.EstimateLine);
        Assert.StartsWith("Live: Claude Sonnet ≈$3.90 · images and voicemail ≈$0.20. Batch: Claude Sonnet ≈$1.95 · images and voicemail ≈$0.20. ", withMedia.EstimateTooltip);
    }

    [Fact]
    public void ReadyCard_NoAttachmentsOrNonRelevant_DropsClauses()
    {
        var card = GenerationUiText.ReadyCard(Ready(nonRelevant: 0, attachments: 0, emails: 30, mediaPlanned: 0));
        Assert.Equal(
            "Generate Emails writes the 12 relevant threads, oldest first in 4 waves: 30 emails in total. Each finished thread is saved straight to the custodians' mailboxes, so you can stop at any time and continue later.",
            card.Body);
    }

    [Fact]
    public void ReadyCard_MediaPlannedWithoutKey_ShowsNotice()
    {
        var card = GenerationUiText.ReadyCard(Ready(mediaPlanned: 4, mediaAvailable: false));
        Assert.Equal(
            "4 images and voicemails are planned, but no tested OpenAI key is set up this session, so they will be marked Skipped. Test an OpenAI key on the API Configuration step to include them.",
            card.MediaNotice);

        Assert.Equal("", GenerationUiText.ReadyCard(Ready(mediaPlanned: 0, mediaAvailable: false)).MediaNotice);
    }

    [Theory]
    [InlineData(1, "1 relevant thread first, oldest first in 1 wave, then the 1 non-relevant thread: 1 email in total, with 1 attachment.", "1 image or voicemail is planned")]
    [InlineData(2, "2 relevant threads first, oldest first in 2 waves, then the 2 non-relevant threads: 2 emails in total, with 2 attachments.", "2 images and voicemails are planned")]
    [InlineData(1000, "1,000 relevant threads first, oldest first in 1,000 waves, then the 1,000 non-relevant threads: 1,000 emails in total, with 1,000 attachments.", "1,000 images and voicemails are planned")]
    public void ReadyCard_Plurals(int n, string bodyPart, string noticeStart)
    {
        var card = GenerationUiText.ReadyCard(Ready(relevant: n, nonRelevant: n, emails: n, waves: n, attachments: n, mediaPlanned: n, mediaAvailable: false));
        Assert.Contains(bodyPart, card.Body);
        Assert.StartsWith(noticeStart, card.MediaNotice);
    }

    [Fact]
    public void ReadyCard_LargeCost()
    {
        var card = GenerationUiText.ReadyCard(Ready(story: 200m, volume: 50m, media: 0m, mediaPlanned: 0, batchStory: 100m, batchVolume: 25m));
        Assert.Equal("Estimated cost ≈$250 at live prices or ≈$125 with batch pricing, ±50%, including prompt caching.", card.EstimateLine);
        Assert.StartsWith("Live: Claude Sonnet ≈$200 · Claude Haiku ≈$50.00. Batch: Claude Sonnet ≈$100 · Claude Haiku ≈$25.00. ", card.EstimateTooltip);
    }

    [Fact]
    public void StateCard_Ready_DelegatesToReadyCard()
    {
        var card = GenerationUiText.StateCard(GenerationStateCard.Ready, 12, 30, 4, 3.2m, "Claude Sonnet", 0, "");

        Assert.Equal("Ready to write the corpus", card.Heading);
        Assert.Equal(
            "Generate Emails writes the 12 relevant threads, oldest first in 4 waves: 30 emails in total. Each finished thread is saved straight to the custodians' mailboxes, so you can stop at any time and continue later.",
            card.Body);
        Assert.StartsWith("Estimated cost ≈$3.20 at live prices", card.EstimateLine);
        Assert.Equal("Generate Emails", card.ButtonText);
        Assert.False(card.Danger);
    }

    [Fact]
    public void StateCard_NotApproved()
    {
        var card = GenerationUiText.StateCard(GenerationStateCard.NotApproved, 0, 0, 0, 0m, "", 0, "");

        Assert.Equal("Approve the plan first", card.Heading);
        Assert.Equal("Emails are written from an approved corpus plan. Approve the plan on Review Plan, then come back to this step.", card.Body);
        Assert.Equal("Review Plan", card.ButtonText);
        Assert.False(card.Danger);
    }

    [Fact]
    public void StateCard_PlanChanged()
    {
        var card = GenerationUiText.StateCard(GenerationStateCard.PlanChanged, 0, 0, 0, 0m, "", 5, "");

        Assert.Equal("The plan changed after emails were generated", card.Heading);
        Assert.Equal(
            "5 threads were written for an earlier plan, and their DocIDs no longer match the answer key. Start Over deletes those emails and the narrative ledger, then you can write the current plan.",
            card.Body);
        Assert.Equal("Start Over...", card.ButtonText);
        Assert.True(card.Danger);
    }

    [Theory]
    [InlineData(0, "0 threads were written")]
    [InlineData(1, "1 thread was written")]
    [InlineData(2, "2 threads were written")]
    [InlineData(1000, "1,000 threads were written")]
    public void StateCard_PlanChanged_Plurals(int n, string expectedStart)
    {
        Assert.StartsWith(expectedStart, GenerationUiText.StateCard(GenerationStateCard.PlanChanged, 0, 0, 0, 0m, "", n, "").Body);
    }

    [Fact]
    public void StateCard_Unreadable()
    {
        var card = GenerationUiText.StateCard(GenerationStateCard.Unreadable, 0, 0, 0, 0m, "", 0, "The journal is larger than 200 MB");

        Assert.Equal("The saved run could not be opened", card.Heading);
        Assert.Equal(
            "The journal is larger than 200 MB. Start Over clears the saved run, deletes the emails it wrote and starts again from the approved plan.",
            card.Body);
        Assert.Equal("Start Over...", card.ButtonText);
        Assert.True(card.Danger);
    }

    // --- Partial-run banner ---

    [Fact]
    public void BannerHeading_FollowsRunEnded()
    {
        Assert.Equal("The last run did not finish.", GenerationUiText.BannerHeading(runEnded: false));
        Assert.Equal("Part of the corpus is not written yet.", GenerationUiText.BannerHeading(runEnded: true));
        Assert.Equal("Continue Generating", GenerationUiText.BannerButton);
    }

    [Fact]
    public void EntryBannerHeading_FollowsLatestAttempt()
    {
        // An older RunEnded never hides an interrupted Continue (review MEDIUM 06-13)
        Assert.Equal("The last run did not finish.",
            GenerationUiText.EntryBannerHeading(new GenerationEntryInput { HasSavedRun = true, LatestAttemptEnded = false }));
        Assert.Equal("Part of the corpus is not written yet.",
            GenerationUiText.EntryBannerHeading(new GenerationEntryInput { HasSavedRun = true, LatestAttemptEnded = true }));
    }

    [Fact]
    public void BannerBody_ThreadsAndAttachments()
    {
        Assert.Equal(
            "10 of 40 threads and 3 of 12 attachments are written and saved. Continue Generating writes the rest and keeps everything already saved, and tries the 2 failed items again.",
            GenerationUiText.BannerBody(10, 40, 3, 12, 2));
    }

    [Fact]
    public void BannerBody_NoAttachmentsOrFailures_DropsClauses()
    {
        Assert.Equal(
            "4 of 10 threads are written and saved. Continue Generating writes the rest and keeps everything already saved.",
            GenerationUiText.BannerBody(4, 10, 0, 0, 0));
    }

    [Theory]
    [InlineData(1, "of 1 thread and 0 of 1 attachment are", "tries the 1 failed item again.")]
    [InlineData(2, "of 2 threads and 0 of 2 attachments are", "tries the 2 failed items again.")]
    [InlineData(1000, "of 1,000 threads and 0 of 1,000 attachments are", "tries the 1,000 failed items again.")]
    public void BannerBody_Plurals(int n, string totalPart, string failedPart)
    {
        var body = GenerationUiText.BannerBody(0, n, 0, n, n);
        Assert.Contains(totalPart, body);
        Assert.EndsWith(failedPart, body);
    }

    [Fact]
    public void BannerBody_ZeroTotal()
    {
        Assert.StartsWith("0 of 0 threads are", GenerationUiText.BannerBody(0, 0, 0, 0, 0));
    }

    // --- Media banner (UI-SPEC Decision 2) ---

    [Fact]
    public void MediaBanner_Available_NoFailures()
    {
        var banner = GenerationUiText.MediaBanner(4, 0, true, "the OpenAI key was rejected");

        Assert.Equal("Some images and voicemails were skipped.", banner.Heading);
        Assert.Equal(
            "4 images and voicemails were not created: the OpenAI key was rejected. Their emails are saved without them, and the answer key marks them Skipped. Create Skipped Media creates them and rewrites the affected emails in every mailbox.",
            banner.Body);
        Assert.Equal("Create Skipped Media", banner.ButtonText);
    }

    [Fact]
    public void MediaBanner_Available_WithFailures_AndNoReason()
    {
        var banner = GenerationUiText.MediaBanner(1, 2, true, "");

        Assert.Equal(
            "1 image or voicemail was not created. Their emails are saved without them, and the answer key marks them Skipped. Retry Failed and Skipped Items creates them and rewrites the affected emails in every mailbox.",
            banner.Body);
        Assert.Equal("Retry Failed and Skipped Items", banner.ButtonText);
    }

    [Fact]
    public void MediaBanner_Unavailable_NoButton()
    {
        var banner = GenerationUiText.MediaBanner(2, 1, false, "the OpenAI key was rejected");

        Assert.Equal("Some images and voicemails were skipped.", banner.Heading);
        Assert.Equal(
            "2 images and voicemails were not created because no tested OpenAI key is set up this session. Their emails are saved without them, and the answer key marks them Skipped. Test an OpenAI key on the API Configuration step, then come back here.",
            banner.Body);
        Assert.Equal("", banner.ButtonText);
    }

    [Theory]
    [InlineData(0, "0 images and voicemails were")]
    [InlineData(1, "1 image or voicemail was")]
    [InlineData(2, "2 images and voicemails were")]
    [InlineData(1000, "1,000 images and voicemails were")]
    public void MediaBanner_Plurals(int n, string expectedStart)
    {
        Assert.StartsWith(expectedStart, GenerationUiText.MediaBanner(n, 0, true, "").Body);
        Assert.StartsWith(expectedStart, GenerationUiText.MediaBanner(n, 0, false, "").Body);
    }

    [Fact]
    public void RetryFailedButton_TextAndTooltip()
    {
        Assert.Equal("Retry Failed Items", GenerationUiText.RetryFailedButtonText);
        Assert.Equal(
            "Writes the failed threads and attachments again. Emails that are already saved are rewritten only when one of their attachments is added.",
            GenerationUiText.RetryFailedTooltip);
    }

    // --- Confirmations ---

    [Fact]
    public void CG1_CancelGeneration()
    {
        var (title, text) = GenerationUiText.CG1CancelGeneration(3, 2);

        Assert.Equal("Cancel Generation", title);
        Assert.Equal("Stop generating? The 3 threads and 2 attachments already written stay saved. Items in progress are discarded and can be written later with Continue Generating.", text);
    }

    [Theory]
    [InlineData(0, "The 0 threads already written stay saved.")]
    [InlineData(1, "The 1 thread already written stays saved.")]
    [InlineData(2, "The 2 threads already written stay saved.")]
    [InlineData(1000, "The 1,000 threads already written stay saved.")]
    public void CG1_NoAttachments_DropsClause(int n, string expected)
    {
        Assert.Contains(expected, GenerationUiText.CG1CancelGeneration(n, 0).Text);
    }

    [Theory]
    [InlineData(1, "The 1 thread and 1 attachment already written stay saved.")]
    [InlineData(2, "The 2 threads and 2 attachments already written stay saved.")]
    [InlineData(1000, "The 1,000 threads and 1,000 attachments already written stay saved.")]
    public void CG1_AttachmentPlurals(int n, string expected)
    {
        Assert.Contains(expected, GenerationUiText.CG1CancelGeneration(n, n).Text);
    }

    [Fact]
    public void CG2_StartOver()
    {
        var (title, text) = GenerationUiText.CG2StartOver(120);

        Assert.Equal("Start Over", title);
        Assert.Equal(
            "Delete the 120 generated EML files (relevant and non-relevant, with their attachments), the narrative ledger and the side projects, and reset the answer key to Planned? This cannot be undone. The corpus plan and case guide are kept.",
            text);
    }

    [Theory]
    [InlineData(0, "Delete the 0 generated EML files")]
    [InlineData(1, "Delete the 1 generated EML file ")]
    [InlineData(2, "Delete the 2 generated EML files")]
    [InlineData(1000, "Delete the 1,000 generated EML files")]
    public void CG2_Plurals(int n, string expectedStart)
    {
        Assert.StartsWith(expectedStart, GenerationUiText.CG2StartOver(n).Text);
    }

    [Fact]
    public void CG3_CloseDuringRun()
    {
        var (title, text) = GenerationUiText.CG3CloseDuringRun();

        Assert.Equal("Stop Generating?", title);
        Assert.Equal("Emails are still being written. Close and stop? Finished threads and attachments are saved; items in progress are discarded and can be written later with Continue Generating.", text);
    }

    // --- Case Summary: Mailboxes row ---

    private static MailboxesRowInput Mailboxes(
        int rt = 12, int rw = 12, int nt = 40, int nw = 40, int at = 18, int aw = 18, int skipped = 0,
        int warnings = 0, int failed = 0, int files = 37, int mailboxes = 6, bool generated = true, bool earlier = false) =>
        new()
        {
            Generated = generated,
            EarlierPlan = earlier,
            RelevantTotal = rt,
            RelevantWritten = rw,
            NonRelevantTotal = nt,
            NonRelevantWritten = nw,
            AttachmentsTotal = at,
            AttachmentsWritten = aw,
            AttachmentsSkipped = skipped,
            ItemWarnings = warnings,
            ItemFailed = failed,
            Files = files,
            Mailboxes = mailboxes,
            FolderExists = true
        };

    [Fact]
    public void MailboxesRow_NotGenerated()
    {
        var row = GenerationUiText.MailboxesRow(new MailboxesRowInput { Generated = false, RelevantTotal = 10, FolderExists = false });

        Assert.Equal("Not generated yet", row.Line3);
        Assert.Equal("", row.Line3Tooltip);
        Assert.Equal(SummaryTone.Muted, row.Line3Tone);
        Assert.Equal(SummaryTone.Muted, row.DotTone);
        Assert.False(row.ShowPrimary);
        Assert.True(row.ShowSecondary);

        Assert.True(GenerationUiText.MailboxesRow(new MailboxesRowInput { Generated = false, FolderExists = true }).ShowPrimary);
    }

    [Fact]
    public void MailboxesRow_CompleteClean()
    {
        var row = GenerationUiText.MailboxesRow(Mailboxes());

        Assert.Equal("37 EML files in 6 custodian mailboxes · Mailboxes folder", row.Line2);
        Assert.Equal("All 52 threads and 18 attachments written", row.Line3);
        Assert.Equal("Relevant: 12 of 12 threads. Non-relevant: 40 of 40 threads. Attachments: 18 of 18.", row.Line3Tooltip);
        Assert.Equal(SummaryTone.Success, row.Line3Tone);
        Assert.Equal(SummaryTone.Success, row.DotTone);
        Assert.True(row.ShowPrimary);
        Assert.False(row.ShowSecondary);
    }

    [Fact]
    public void MailboxesRow_CompleteClean_NoAttachments_DropsClause()
    {
        Assert.Equal("All 52 threads written", GenerationUiText.MailboxesRow(Mailboxes(at: 0, aw: 0)).Line3);
    }

    [Fact]
    public void MailboxesRow_Partial_AllClauses()
    {
        var row = GenerationUiText.MailboxesRow(Mailboxes(nw: 30, aw: 15, skipped: 3, warnings: 2, failed: 1));

        Assert.Equal("42 of 52 threads written · 2 items with warnings · 1 failed item · 3 skipped items", row.Line3);
        Assert.Equal("Relevant: 12 of 12 threads. Non-relevant: 30 of 40 threads. Attachments: 15 of 18 (3 skipped).", row.Line3Tooltip);
        Assert.Equal(SummaryTone.Warning, row.Line3Tone);
        Assert.Equal(SummaryTone.Warning, row.DotTone);
    }

    [Fact]
    public void MailboxesRow_Partial_ZeroClausesDropped()
    {
        Assert.Equal("42 of 52 threads written", GenerationUiText.MailboxesRow(Mailboxes(nw: 30)).Line3);
        Assert.Equal("52 of 52 threads written · 1 skipped item", GenerationUiText.MailboxesRow(Mailboxes(aw: 17, skipped: 1)).Line3);
        Assert.Equal("52 of 52 threads written · 1 item with warnings", GenerationUiText.MailboxesRow(Mailboxes(warnings: 1)).Line3);
        // Attachments not all written yet, no problem items
        Assert.Equal("52 of 52 threads written", GenerationUiText.MailboxesRow(Mailboxes(aw: 10)).Line3);
    }

    [Fact]
    public void MailboxesRow_EarlierPlan()
    {
        var row = GenerationUiText.MailboxesRow(Mailboxes(earlier: true));

        Assert.Equal("Written for an earlier plan. Start over on Generate Corpus.", row.Line3);
        Assert.NotEqual("", row.Line3Tooltip);
        Assert.Equal(SummaryTone.Warning, row.Line3Tone);
        Assert.Equal(SummaryTone.Warning, row.DotTone);
    }

    [Theory]
    [InlineData(0, "0 EML files in 0 custodian mailboxes · Mailboxes folder", "All 0 threads written")]
    [InlineData(1, "1 EML file in 1 custodian mailbox · Mailboxes folder", "All 1 thread and 1 attachment written")]
    [InlineData(2, "2 EML files in 2 custodian mailboxes · Mailboxes folder", "All 2 threads and 2 attachments written")]
    [InlineData(1000, "1,000 EML files in 1,000 custodian mailboxes · Mailboxes folder", "All 1,000 threads and 1,000 attachments written")]
    public void MailboxesRow_Plurals(int n, string line2, string line3)
    {
        var row = GenerationUiText.MailboxesRow(Mailboxes(rt: n, rw: n, nt: 0, nw: 0, at: n, aw: n, files: n, mailboxes: n));
        Assert.Equal(line2, row.Line2);
        Assert.Equal(line3, row.Line3);
    }

    [Theory]
    [InlineData(1, "1 of 2 threads written · 1 item with warnings · 1 failed item · 1 skipped item", "Relevant: 1 of 1 thread. Non-relevant: 0 of 1 thread. Attachments: 0 of 1 (1 skipped).")]
    [InlineData(2, "2 of 4 threads written · 2 items with warnings · 2 failed items · 2 skipped items", "Relevant: 2 of 2 threads. Non-relevant: 0 of 2 threads. Attachments: 0 of 2 (2 skipped).")]
    [InlineData(1000, "1,000 of 2,000 threads written · 1,000 items with warnings · 1,000 failed items · 1,000 skipped items", "Relevant: 1,000 of 1,000 threads. Non-relevant: 0 of 1,000 threads. Attachments: 0 of 1,000 (1,000 skipped).")]
    public void MailboxesRow_PartialPlurals(int n, string line3, string tooltip)
    {
        var row = GenerationUiText.MailboxesRow(Mailboxes(rt: n, rw: n, nt: n, nw: 0, at: n, aw: 0, skipped: n, warnings: n, failed: n));
        Assert.Equal(line3, row.Line3);
        Assert.Equal(tooltip, row.Line3Tooltip);
    }

    [Fact]
    public void Phase6Copy_NoBannedWords()
    {
        var texts = new List<string>
        {
            GenerationUiText.ScopeNotice(1, 1, 1),
            GenerationUiText.ReadyCard(Ready(mediaAvailable: false)).Body,
            GenerationUiText.ReadyCard(Ready(mediaAvailable: false)).EstimateLine,
            GenerationUiText.ReadyCard(Ready(mediaAvailable: false)).MediaNotice,
            GenerationUiText.BannerBody(1, 2, 1, 2, 1),
            GenerationUiText.MediaBanner(1, 1, true, "r").Body,
            GenerationUiText.MediaBanner(1, 1, false, "r").Body,
            GenerationUiText.CG1CancelGeneration(1, 1).Text,
            GenerationUiText.CG2StartOver(1).Text,
            GenerationUiText.CG3CloseDuringRun().Text,
            GenerationUiText.MailboxesRow(Mailboxes(nw: 1, skipped: 1, failed: 1)).Line3,
            GenerationUiText.MailboxesRow(Mailboxes()).Line3Tooltip,
            GenerationUiText.RetryFailedTooltip,
            GenerationUiText.StartedOverText(1)
        };
        var c = new GenerationCounts { AttachmentsTotal = 1, AttachmentsSkipped = 1, NonRelevantTotal = 1, NonRelevantDecoy = 1, MediaPlanned = 1 };
        foreach (var kind in Enum.GetValues<GenerationTileKind>())
        {
            var tile = GenerationUiText.Tile(kind, c);
            texts.Add(tile.Caption + " " + tile.Value + " " + tile.SubLine + " " + tile.Tooltip);
        }
        foreach (var kind in Enum.GetValues<StageRowKind>())
        {
            var (dates, done, tooltip) = GenerationUiText.StageRowText(new WaveProgress { RowKind = kind, Threads = 1, Attachments = 1 });
            texts.Add(dates + done + tooltip);
        }

        foreach (var text in texts)
            AssertNoBannedWords(text);
    }

    // --- Case Summary: Story so far row ---

    [Fact]
    public void StoryRow_Written()
    {
        var row = GenerationUiText.StoryRow(new StoryRowInput
        {
            Entries = 45, Threads = 10, FileName = "Case X - Story So Far.docx", FileExists = true,
            WrittenLocal = new DateTime(2026, 10, 3, 14, 5, 0)
        });

        Assert.Equal("Narrative ledger · 45 details from 10 threads · Case X - Story So Far.docx", row.Line2);
        Assert.Equal("Written Oct 3, 2026 2:05 PM", row.Line3);
        Assert.Equal(SummaryTone.Success, row.Line3Tone);
        Assert.Equal(SummaryTone.Success, row.DotTone);
        Assert.True(row.ShowPrimary);
        Assert.True(row.ShowSecondary);
    }

    [Fact]
    public void StoryRow_NotWritten()
    {
        var row = GenerationUiText.StoryRow(new StoryRowInput { FileName = "Case X - Story So Far.docx" });

        Assert.Equal("Not written yet. It is written when a generation run ends.", row.Line3);
        Assert.Equal(SummaryTone.Muted, row.Line3Tone);
        Assert.Equal(SummaryTone.Muted, row.DotTone);
        Assert.False(row.ShowPrimary);
        Assert.False(row.ShowSecondary);
    }

    [Fact]
    public void StoryRow_Failed()
    {
        var row = GenerationUiText.StoryRow(new StoryRowInput
        {
            Entries = 3, Threads = 1, FileName = "Case X - Story So Far.docx", WrittenLocal = new DateTime(2026, 10, 3, 14, 5, 0),
            Error = "The file is open in another program."
        });

        Assert.Equal("Could not write: The file is open in another program", row.Line3);
        Assert.Equal(SummaryTone.Danger, row.Line3Tone);
        Assert.Equal(SummaryTone.Danger, row.DotTone);
        Assert.False(row.ShowPrimary);
        Assert.True(row.ShowSecondary);
    }

    [Theory]
    [InlineData(0, "0 details from 0 threads")]
    [InlineData(1, "1 detail from 1 thread ")]
    [InlineData(2, "2 details from 2 threads")]
    [InlineData(1000, "1,000 details from 1,000 threads")]
    public void StoryRow_Plurals(int n, string expected)
    {
        Assert.Contains(expected, GenerationUiText.StoryRow(new StoryRowInput { Entries = n, Threads = n, FileName = "f.docx" }).Line2);
    }

    [Fact]
    public void AnswerKeyRunClause_AndStoryMissing()
    {
        Assert.Equal(
            "Written Oct 3, 2026 2:05 PM · 30 generated, 270 planned",
            GenerationUiText.AnswerKeyRunClause(new DateTime(2026, 10, 3, 14, 5, 0), 30, 270));
        Assert.Equal(
            "Written Oct 3, 2026 2:05 PM · 1,000 generated, 4,000 planned",
            GenerationUiText.AnswerKeyRunClause(new DateTime(2026, 10, 3, 14, 5, 0), 1000, 4000));
        Assert.Equal(
            "Case X - Story So Far.docx is no longer in the case folder. Click Export Story So Far to write it again.",
            GenerationUiText.StoryMissingText("Case X - Story So Far.docx"));
    }

    [Theory]
    [InlineData(0, "0 skipped")]
    [InlineData(1, "1 skipped")]
    [InlineData(1000, "1,000 skipped")]
    public void AnswerKeyRunTooltip_SkippedCount(int n, string expected)
    {
        Assert.Equal(expected, GenerationUiText.AnswerKeyRunTooltip(n));
    }

    // --- Cost breakdown (07-UI-SPEC S6, S2 R1) ---

    [Fact]
    public void CostBreakdown_DropsZeroLines_UsesMinusSign()
    {
        var text = GenerationUiText.CostBreakdown(1.2345m, 0.5m, 0m, 0.0123m, 0.9m, 0.1778m, 0m);
        Assert.Equal(
            "Spent this session: $1.2345\nInput: $0.5000\nCached input, cache read: $0.0123\nOutput: $0.9000\nBatch discount: −$0.1778",
            text);
        AssertNoBannedWords(text);
    }

    [Fact]
    public void CostBreakdown_AllLines()
    {
        Assert.Equal(
            "Spent this session: $2.0000\nInput: $0.5000\nCached input, cache write: $0.2500\nCached input, cache read: $0.0100\nOutput: $1.0000\nBatch discount: −$0.0100\nImages and voicemail: $0.2500",
            GenerationUiText.CostBreakdown(2m, 0.5m, 0.25m, 0.01m, 1m, 0.01m, 0.25m));
    }

    [Fact]
    public void CostBreakdown_NothingSpent()
    {
        Assert.Equal("Nothing spent this session yet.", GenerationUiText.CostBreakdown(0m, 0m, 0m, 0m, 0m, 0m, 0m));
    }

    [Fact]
    public void CostRow_NothingSpent_Muted()
    {
        var row = GenerationUiText.CostRow(false, 0m, 0m, 0m, 0m, 0m, 0m, 0m, 0m);
        Assert.Equal("What this case has cost so far, across all sessions.", row.Line2);
        Assert.Equal("Nothing spent yet", row.Line3);
        Assert.Equal(SummaryTone.Muted, row.Line3Tone);
        Assert.Equal(SummaryTone.Muted, row.DotTone);
        Assert.Equal("", row.Line3Tooltip);
        Assert.False(row.ShowPrimary);
        Assert.False(row.ShowSecondary);
    }

    [Fact]
    public void CostRow_FullBreakdown_SuccessDot()
    {
        var row = GenerationUiText.CostRow(true, 12.34m, 3m, 0.5m, 0.25m, 9m, 1.41m, 1m, 0m);
        Assert.Equal(
            "$12.34 spent · input $3.00 · cache write $0.50 · cache read $0.25 · output $9.00 · batch discount −$1.41 · images and voicemail $1.00",
            row.Line3);
        Assert.Equal(SummaryTone.Muted, row.Line3Tone);
        Assert.Equal(SummaryTone.Success, row.DotTone);
        Assert.Equal(
            "Spent so far: $12.34\nInput: $3.00\nCached input, cache write: $0.50\nCached input, cache read: $0.25\nOutput: $9.00\nBatch discount: −$1.41\nImages and voicemail: $1.00",
            row.Line3Tooltip);
        AssertNoBannedWords(row.Line3);
        AssertNoBannedWords(row.Line3Tooltip);
    }

    [Fact]
    public void CostRow_ZeroClausesDropped()
    {
        var row = GenerationUiText.CostRow(true, 4m, 1m, 0m, 0m, 3m, 0m, 0m, 0m);
        Assert.Equal("$4.00 spent · input $1.00 · output $3.00", row.Line3);
        Assert.Equal("Spent so far: $4.00\nInput: $1.00\nOutput: $3.00", row.Line3Tooltip);
    }

    [Fact]
    public void CostRow_NotItemized_AddsUpToTotal()
    {
        decimal input = 3m, cw = 0.5m, cr = 0.25m, output = 9m, batch = 1.41m, media = 1m, unsplit = 2m;
        var total = input + cw + cr + output - batch + media + unsplit;
        var row = GenerationUiText.CostRow(true, total, input, cw, cr, output, batch, media, unsplit);
        Assert.Equal(14.34m, total);
        Assert.EndsWith(" · images and voicemail $1.00 · not itemized $2.00", row.Line3);
        Assert.StartsWith("$14.34 spent", row.Line3);
        Assert.EndsWith("\nNot itemized: $2.00", row.Line3Tooltip);
    }

    [Fact]
    public void CostRow_OnlyNotItemized()
    {
        var row = GenerationUiText.CostRow(true, 2m, 0m, 0m, 0m, 0m, 0m, 0m, 2m);
        Assert.Equal("$2.00 spent · not itemized $2.00", row.Line3);
        Assert.Equal(SummaryTone.Success, row.DotTone);
    }

    // --- 07 S1: batch pricing and the live/batch estimate ---

    [Fact]
    public void EstimateLine_TwoTotals()
    {
        Assert.Equal("Estimated cost ≈$41.20 at live prices or ≈$21.70 with batch pricing, ±50%, including prompt caching.",
            GenerationUiText.EstimateLine(41.2m, 21.7m));
        Assert.Equal("Estimated cost ≈$412 at live prices or ≈$217 with batch pricing, ±50%, including prompt caching.",
            GenerationUiText.EstimateLine(412m, 217m));
    }

    [Fact]
    public void EstimateTooltip_NoMedia_DropsMediaClause()
    {
        var tooltip = GenerationUiText.EstimateTooltip(Ready(mediaPlanned: 0), false, 0m);
        Assert.StartsWith("Live: Claude Sonnet ≈$3.20 · Claude Haiku ≈$0.70. Batch: Claude Sonnet ≈$1.60 · Claude Haiku ≈$0.35. Images and voicemail", tooltip);
        AssertNoBannedWords(tooltip);
    }

    [Fact]
    public void BatchCheckboxText_IsTheContractCopy()
    {
        Assert.Equal("Use batch pricing (slower, about half the cost)", GenerationUiText.BatchCheckboxText);
        Assert.Equal(2_000, GenerationUiText.BatchRecommendThreshold);
    }

    [Fact]
    public void BatchHint_Boundary_1999_2000()
    {
        Assert.Equal("", GenerationUiText.BatchHint(1999, false, 2000).Text);
        var recommend = GenerationUiText.BatchHint(2000, false, 2000);
        Assert.Equal("Batch pricing is recommended for a corpus this size. It waits on the provider between waves, so the run can take several hours.", recommend.Text);
        Assert.Equal(SummaryTone.Muted, recommend.Tone);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1999)]
    [InlineData(5000)]
    public void BatchHint_Checked_IsWarning(int planned)
    {
        var hint = GenerationUiText.BatchHint(planned, true, 2000);
        Assert.Equal("Batch runs wait on the provider between waves and can take several hours. Keep this screen open; if the app closes, Continue Generating collects the finished batches without paying again.", hint.Text);
        Assert.Equal(SummaryTone.Warning, hint.Tone);
    }

    [Fact]
    public void ReadyCard_BatchHint_FollowsEmailsAndChoice()
    {
        Assert.Equal("", GenerationUiText.ReadyCard(Ready(emails: 1999)).BatchHint);
        var recommend = GenerationUiText.ReadyCard(Ready(emails: 2500));
        Assert.StartsWith("Batch pricing is recommended", recommend.BatchHint);
        Assert.Equal(SummaryTone.Muted, recommend.BatchHintTone);
        var checkedCard = GenerationUiText.ReadyCard(Ready(emails: 10, batchChecked: true));
        Assert.StartsWith("Batch runs wait on the provider", checkedCard.BatchHint);
        Assert.Equal(SummaryTone.Warning, checkedCard.BatchHintTone);
    }

    private static readonly DateTime Today = new(2026, 10, 6, 16, 0, 0, DateTimeKind.Local);

    private static WaveProgress Waiting(int count = 1, int done = 3, int total = 5, DateTime? submittedLocal = null, StageRowKind kind = StageRowKind.Wave) => new()
    {
        Index = 3,
        Start = new DateOnly(2024, 3, 4),
        End = new DateOnly(2024, 3, 17),
        RowKind = kind,
        BatchWaiting = true,
        BatchCount = count,
        BatchDone = done,
        BatchTotal = total,
        BatchFirstSubmittedUtc = (submittedLocal ?? new DateTime(2026, 10, 6, 14, 14, 0, DateTimeKind.Local)).ToUniversalTime(),
        BatchNextCheckUtc = Today.ToUniversalTime().AddSeconds(25)
    };

    [Fact]
    public void BatchWaitText_OneBatch_Today()
    {
        Assert.Equal("Waiting on provider batch, submitted 2:14 PM, 3 of 5 requests done",
            GenerationUiText.BatchWaitText(Waiting(), Today));
    }

    [Fact]
    public void BatchWaitText_TwoBatches()
    {
        Assert.Equal("Waiting on 2 provider batches, first submitted 2:14 PM, 3 of 5 requests done",
            GenerationUiText.BatchWaitText(Waiting(count: 2), Today));
    }

    [Fact]
    public void BatchWaitText_Yesterday_ShowsDate()
    {
        Assert.Equal("Waiting on provider batch, submitted Oct 5, 2:14 PM, 3 of 5 requests done",
            GenerationUiText.BatchWaitText(Waiting(submittedLocal: new DateTime(2026, 10, 5, 14, 14, 0, DateTimeKind.Local)), Today));
    }

    [Theory]
    [InlineData(0, "0 of 0 requests done")]
    [InlineData(1, "0 of 1 request done")]
    [InlineData(2, "0 of 2 requests done")]
    [InlineData(1000, "0 of 1,000 requests done")]
    public void BatchWaitText_Plurals(int n, string expectedEnd)
    {
        Assert.EndsWith(expectedEnd, GenerationUiText.BatchWaitText(Waiting(done: 0, total: n), Today));
    }

    [Fact]
    public void BatchWaitText_NotWaiting_IsEmpty()
    {
        var row = Waiting();
        row.BatchWaiting = false;
        Assert.Equal("", GenerationUiText.BatchWaitText(row, Today));
    }

    [Fact]
    public void BatchWaitTooltip_HasCountdown()
    {
        Assert.Equal("Mar 4 – Mar 17, 2024. Waiting on provider batch, submitted 2:14 PM, 3 of 5 requests done. Next check in 25 s.",
            GenerationUiText.BatchWaitTooltip("Mar 4 – Mar 17, 2024", GenerationUiText.BatchWaitText(Waiting(), Today), 25));
    }

    [Fact]
    public void ProgressText_BatchWait_WaveAndNonRelevant()
    {
        var view = new GenerationView { Stage = GenerationStage.Writing, StageWave = 3, StageWaveCount = 14, Waves = { Waiting() } };
        Assert.Equal("Wave 3 of 14 (Mar 4 – Mar 17, 2024): waiting on provider batch, 3 of 5 requests done. Next check in 25 s.",
            GenerationUiText.ProgressText(view, Today.ToUniversalTime()));

        var filler = new GenerationView { Stage = GenerationStage.NonRelevant, Waves = { Waiting(done: 1000, total: 2000, kind: StageRowKind.NonRelevant) } };
        Assert.Equal("Non-relevant mail: waiting on provider batch, 1,000 of 2,000 requests done. Next check in 25 s.",
            GenerationUiText.ProgressText(filler, Today.ToUniversalTime()));
    }

    [Fact]
    public void ProgressText_BatchWait_CountdownNeverNegative()
    {
        var view = new GenerationView { Stage = GenerationStage.Writing, StageWave = 3, StageWaveCount = 14, Waves = { Waiting(done: 0, total: 1) } };
        Assert.EndsWith("0 of 1 request done. Next check in 0 s.", GenerationUiText.ProgressText(view, Today.ToUniversalTime().AddMinutes(5)));
    }

    [Fact]
    public void ProgressText_CollectingAfterRestart()
    {
        var view = new GenerationView { Stage = GenerationStage.Writing, CollectingAfterRestart = true };
        Assert.Equal("Collecting batches submitted before the app closed...", GenerationUiText.ProgressText(view, Today.ToUniversalTime()));
    }

    [Fact]
    public void ProgressText_NoBatch_UnchangedAndStoppingWins()
    {
        var view = new GenerationView { Stage = GenerationStage.UpdatingKey };
        Assert.Equal("Updating the answer key...", GenerationUiText.ProgressText(view, Today.ToUniversalTime()));

        var stopping = new GenerationView { Stage = GenerationStage.Stopping, Waves = { Waiting() } };
        Assert.Equal(GenerationUiText.StoppingText, GenerationUiText.ProgressText(stopping, Today.ToUniversalTime()));
    }

    private static GenerationEvent BatchEvent(GenerationEventKind kind, int requests = 40, string stage = "wave 3")
    {
        var e = Event(kind);
        e.BatchRequests = requests;
        e.BatchProvider = "Anthropic";
        e.BatchStage = stage;
        return e;
    }

    [Fact]
    public void Activity_BatchSubmitted()
    {
        var line = GenerationUiText.ActivityText(BatchEvent(GenerationEventKind.BatchSubmitted));
        Assert.Equal("Batch submitted: 40 requests to Anthropic for wave 3", line.Text);
        Assert.Equal(ActivityTone.Accent, line.Tone);

        Assert.Equal("Batch submitted: 1 request to Anthropic for non-relevant mail",
            GenerationUiText.ActivityText(BatchEvent(GenerationEventKind.BatchSubmitted, 1, "non-relevant mail")).Text);
    }

    [Fact]
    public void Activity_BatchFinished_Durations()
    {
        var e = BatchEvent(GenerationEventKind.BatchFinished);
        e.BatchSucceeded = 40;
        e.BatchDuration = TimeSpan.FromMinutes(42.5);
        var line = GenerationUiText.ActivityText(e);
        Assert.Equal("Batch finished: 40 results collected after 42 min", line.Text);
        Assert.Equal(ActivityTone.Success, line.Tone);

        e.BatchDuration = TimeSpan.FromMinutes(135);
        Assert.Equal("Batch finished: 40 results collected after 2 h 15 min", GenerationUiText.ActivityText(e).Text);
    }

    [Fact]
    public void Activity_BatchFinishedWithFailures()
    {
        var e = BatchEvent(GenerationEventKind.BatchFinishedWithFailures);
        e.BatchSucceeded = 1000;
        e.BatchFailed = 1;
        var line = GenerationUiText.ActivityText(e);
        Assert.Equal("Batch finished: 1,000 succeeded, 1 failed or expired. The 1 request run live at full price.", line.Text);
        Assert.Equal(ActivityTone.Warning, line.Tone);
    }

    [Fact]
    public void Activity_BatchFailedAtProvider()
    {
        var e = BatchEvent(GenerationEventKind.BatchFailedAtProvider, 2);
        e.BatchProvider = "OpenAI";
        e.BatchReason = "the batch expired before it finished";
        var line = GenerationUiText.ActivityText(e);
        Assert.Equal("Batch failed at OpenAI: the batch expired before it finished. Its 2 requests run live at full price.", line.Text);
        Assert.Equal(ActivityTone.Warning, line.Tone);
    }

    [Fact]
    public void Activity_BatchCollectedAfterRestart()
    {
        var e = BatchEvent(GenerationEventKind.BatchCollectedAfterRestart, 1);
        e.BatchSucceeded = 1;
        e.LocalTime = new DateTime(2026, 10, 6, 16, 0, 0, DateTimeKind.Local);
        e.BatchSubmittedUtc = new DateTime(2026, 10, 6, 14, 14, 0, DateTimeKind.Local).ToUniversalTime();
        var line = GenerationUiText.ActivityText(e);
        Assert.Equal("Collected 1 result from a batch submitted at 2:14 PM before the app closed", line.Text);
        Assert.Equal(ActivityTone.Success, line.Tone);
    }

    [Fact]
    public void BannerBody_BatchClauses_PendingWins()
    {
        const string body = "4 of 10 threads are written and saved. Continue Generating writes the rest and keeps everything already saved.";
        Assert.Equal(body, GenerationUiText.BannerBody(4, 10, 0, 0, 0, batchOn: false, pendingBatches: 0));
        Assert.Equal(body + " Batch pricing stays on for this case.", GenerationUiText.BannerBody(4, 10, 0, 0, 0, batchOn: true, pendingBatches: 0));
        Assert.Equal(body + " 2 provider batches submitted earlier will be collected first, without paying again.",
            GenerationUiText.BannerBody(4, 10, 0, 0, 0, batchOn: true, pendingBatches: 2));
        Assert.Equal(body + " 1 provider batch submitted earlier will be collected first, without paying again.",
            GenerationUiText.BannerBody(4, 10, 0, 0, 0, batchOn: false, pendingBatches: 1));
        Assert.EndsWith(" 1,000 provider batches submitted earlier will be collected first, without paying again.",
            GenerationUiText.BannerBody(4, 10, 0, 0, 0, batchOn: false, pendingBatches: 1000));
    }

    [Fact]
    public void Confirmations_BatchClauses_OnlyWhenPending()
    {
        Assert.Equal(GenerationUiText.CG1CancelGeneration(3, 2).Text, GenerationUiText.CG1CancelGeneration(3, 2, false).Text);
        Assert.Equal(GenerationUiText.CG1CancelGeneration(3, 2).Text + " Provider batches already submitted keep running; Continue Generating collects their results without paying again.",
            GenerationUiText.CG1CancelGeneration(3, 2, true).Text);

        Assert.Equal(GenerationUiText.CG2StartOver(5).Text, GenerationUiText.CG2StartOver(5, false).Text);
        Assert.Equal(GenerationUiText.CG2StartOver(5).Text + " Pending provider batches are cancelled.", GenerationUiText.CG2StartOver(5, true).Text);

        Assert.Equal(GenerationUiText.CG3CloseDuringRun().Text, GenerationUiText.CG3CloseDuringRun(false).Text);
        Assert.Equal(GenerationUiText.CG3CloseDuringRun().Text + " Provider batches already submitted keep running and are collected the next time you click Continue Generating.",
            GenerationUiText.CG3CloseDuringRun(true).Text);
    }

    [Fact]
    public void Tile_Cost_BatchClauses()
    {
        var c = Counts(cost: 12.345m, remaining: 40m, running: true);
        c.BatchOn = true;
        Assert.Equal(
            "Spent on generation in this case: $12.35. Estimate for the remaining items: ≈$40.00, ±50% at batch prices. The cost ticker in the header shows the whole session.",
            GenerationUiText.Tile(GenerationTileKind.Cost, c).Tooltip);

        c.BatchSavingsUsd = 3.5m;
        Assert.EndsWith("The cost ticker in the header shows the whole session. Batch pricing saved $3.50.",
            GenerationUiText.Tile(GenerationTileKind.Cost, c).Tooltip);
    }

    [Fact]
    public void BatchCopy_NoBannedWords()
    {
        var texts = new List<string>
        {
            GenerationUiText.EstimateLine(1m, 0.5m),
            GenerationUiText.EstimateTooltip(Ready(), true, 0.2m),
            GenerationUiText.BatchHint(2000, false, 2000).Text,
            GenerationUiText.BatchHint(0, true, 2000).Text,
            GenerationUiText.BatchWaitText(Waiting(count: 2), Today),
            GenerationUiText.BannerBody(1, 2, 0, 0, 0, true, 3),
            GenerationUiText.CG1CancelGeneration(1, 1, true).Text,
            GenerationUiText.CG2StartOver(1, true).Text,
            GenerationUiText.CG3CloseDuringRun(true).Text
        };
        foreach (var kind in new[] { GenerationEventKind.BatchSubmitted, GenerationEventKind.BatchFinished, GenerationEventKind.BatchFinishedWithFailures, GenerationEventKind.BatchFailedAtProvider, GenerationEventKind.BatchCollectedAfterRestart })
            texts.Add(GenerationUiText.ActivityText(BatchEvent(kind)).Text);

        foreach (var text in texts)
            AssertNoBannedWords(text);
    }
}
