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
            Finished = finished
        };

    // --- Scope notice ---

    [Fact]
    public void ScopeNotice_WithAttachments()
    {
        Assert.Equal(
            "This step writes the 12 relevant threads from the approved plan as email. Attachments and the 300 non-relevant emails are not generated yet; their answer-key rows stay Planned.",
            GenerationUiText.ScopeNotice(12, 300, 40));
    }

    [Fact]
    public void ScopeNotice_NoAttachments_DropsAttachmentClause()
    {
        Assert.Equal(
            "This step writes the 1 relevant thread from the approved plan as email. The 0 non-relevant emails are not generated yet; their answer-key rows stay Planned.",
            GenerationUiText.ScopeNotice(1, 0, 0));
    }

    [Theory]
    [InlineData(0, "0 relevant threads", "0 non-relevant")]
    [InlineData(1, "1 relevant thread ", "1 non-relevant")]
    [InlineData(2, "2 relevant threads", "2 non-relevant")]
    [InlineData(1000, "1,000 relevant threads", "1,000 non-relevant")]
    public void ScopeNotice_Plurals(int n, string threads, string nonRelevant)
    {
        var text = GenerationUiText.ScopeNotice(n, n, 1);
        Assert.Contains(threads, text);
        Assert.Contains(nonRelevant, text);
    }

    // --- Tiles ---

    [Fact]
    public void Tile_Threads_DefaultVariant()
    {
        var tile = GenerationUiText.Tile(GenerationTileKind.Threads, Counts());

        Assert.Equal("Threads", tile.Caption);
        Assert.Equal("3 / 1,000", tile.Value);
        Assert.Equal("14 waves", tile.SubLine);
        Assert.Equal("3 of 1,000 relevant threads written, in 14 waves by date", tile.Tooltip);
        Assert.Equal(TileTone.Normal, tile.Tone);
    }

    [Fact]
    public void Tile_Threads_NarrowVariants()
    {
        var compact = GenerationUiText.Tile(GenerationTileKind.Threads, Counts(), 1);
        Assert.Equal("3/1,000", compact.Value);
        Assert.Equal("14 waves", compact.SubLine);

        var split = GenerationUiText.Tile(GenerationTileKind.Threads, Counts(), 2);
        Assert.Equal("3", split.Value);
        Assert.Equal("of 1,000", split.SubLine);
    }

    [Theory]
    [InlineData(0, "0 waves")]
    [InlineData(1, "1 wave")]
    [InlineData(2, "2 waves")]
    [InlineData(1000, "1,000 waves")]
    public void Tile_Threads_WavePlural(int waves, string expected)
    {
        Assert.Equal(expected, GenerationUiText.Tile(GenerationTileKind.Threads, Counts(waves: waves)).SubLine);
    }

    [Fact]
    public void Tile_Emails()
    {
        var tile = GenerationUiText.Tile(GenerationTileKind.Emails, Counts(emailsWritten: 1200, emailsPlanned: 3400));

        Assert.Equal("Emails", tile.Caption);
        Assert.Equal("1,200", tile.Value);
        Assert.Equal("of 3,400 planned", tile.SubLine);
        Assert.Equal("Relevant emails written so far. Each email is saved in every participant's mailbox.", tile.Tooltip);
    }

    [Theory]
    [InlineData(0, "0 mailboxes", "in 0 custodian mailboxes")]
    [InlineData(1, "1 mailbox", "in 1 custodian mailbox ")]
    [InlineData(2, "2 mailboxes", "in 2 custodian mailboxes")]
    [InlineData(1000, "1,000 mailboxes", "in 1,000 custodian mailboxes")]
    public void Tile_EmlFiles_MailboxPlural(int mailboxes, string subLine, string tooltipPart)
    {
        var tile = GenerationUiText.Tile(GenerationTileKind.EmlFiles, Counts(files: 37, mailboxes: mailboxes));

        Assert.Equal("EML files", tile.Caption);
        Assert.Equal("37", tile.Value);
        Assert.Equal(subLine, tile.SubLine);
        Assert.Contains(tooltipPart, tile.Tooltip);
    }

    [Fact]
    public void Tile_EmlFiles_Tooltip()
    {
        var tile = GenerationUiText.Tile(GenerationTileKind.EmlFiles, Counts(files: 37, mailboxes: 6));
        Assert.Equal("37 .eml files in 6 custodian mailboxes under the Mailboxes folder", tile.Tooltip);
    }

    [Fact]
    public void Tile_Ledger()
    {
        var tile = GenerationUiText.Tile(GenerationTileKind.Ledger, Counts(ledger: 1045));

        Assert.Equal("Ledger", tile.Caption);
        Assert.Equal("1,045", tile.Value);
        Assert.Equal("story details", tile.SubLine);
        Assert.Equal("Commitments, figures, meetings, decisions, people and documents the written emails introduced. Later emails build on them.", tile.Tooltip);
    }

    [Theory]
    [InlineData(0, TileTone.Normal)]
    [InlineData(2, TileTone.Warning)]
    public void Tile_Warnings_ToneWhenAboveZero(int warnings, TileTone tone)
    {
        var tile = GenerationUiText.Tile(GenerationTileKind.Warnings, Counts(warnings: warnings));

        Assert.Equal("Warnings", tile.Caption);
        Assert.Equal(warnings.ToString(), tile.Value);
        Assert.Equal("threads kept", tile.SubLine);
        Assert.Equal(tone, tile.Tone);
        Assert.Equal("Threads that still broke a consistency check after 3 attempts. The best attempt was kept and marked \"Generated - with warnings\" in the answer key.", tile.Tooltip);
    }

    [Theory]
    [InlineData(0, TileTone.Normal)]
    [InlineData(1, TileTone.Danger)]
    public void Tile_Failed_ToneWhenAboveZero(int failed, TileTone tone)
    {
        var tile = GenerationUiText.Tile(GenerationTileKind.Failed, Counts(failed: failed));

        Assert.Equal("Failed", tile.Caption);
        Assert.Equal(failed.ToString(), tile.Value);
        Assert.Equal("threads not written", tile.SubLine);
        Assert.Equal(tone, tile.Tone);
        Assert.Equal("Threads that could not be written. Retry them with Continue Generating or Retry Failed Threads.", tile.Tooltip);
    }

    [Fact]
    public void Tile_Cost_Running()
    {
        var tile = GenerationUiText.Tile(GenerationTileKind.Cost, Counts(cost: 12.345m, remaining: 40m, running: true));

        Assert.Equal("Cost", tile.Caption);
        Assert.Equal("$12.35", tile.Value);
        Assert.Equal("≈$40.00 to go", tile.SubLine);
        Assert.Equal("Spent on generation in this case: $12.35. Estimate for the remaining threads: ≈$40.00, ±50%. The cost ticker in the header shows the whole session.", tile.Tooltip);
        Assert.Equal(TileTone.Normal, tile.Tone);
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
        foreach (var kind in new[] { GenerationTileKind.Threads, GenerationTileKind.Emails, GenerationTileKind.EmlFiles, GenerationTileKind.Ledger, GenerationTileKind.Cost })
            Assert.Equal(TileTone.Normal, GenerationUiText.Tile(kind, Counts(warnings: 5, failed: 5)).Tone);
    }

    [Fact]
    public void TileContent_DefaultsToNormalTone()
    {
        Assert.Equal(TileTone.Normal, new TileContent().Tone);
    }

    // --- Waves ---

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
    [InlineData(1, "1 thread, 1 email")]
    [InlineData(2, "2 threads, 2 emails")]
    [InlineData(1000, "1,000 threads, 1,000 emails")]
    public void WaveTooltip_Plurals(int n, string expected)
    {
        Assert.Equal(expected, GenerationUiText.WaveTooltip(n, n));
    }

    // --- Activity ---

    private static GenerationEvent Event(GenerationEventKind kind) => new() { Kind = kind, LocalTime = new DateTime(2026, 10, 3, 14, 5, 9) };

    [Fact]
    public void Activity_RunStarted()
    {
        var e = Event(GenerationEventKind.RunStarted);
        e.Threads = 120;
        e.WaveCount = 14;

        var line = GenerationUiText.ActivityText(e);
        Assert.Equal("Started: 120 threads to write in 14 waves", line.Text);
        Assert.Equal(ActivityTone.Accent, line.Tone);
        Assert.Equal(line.Text, line.Tooltip);
    }

    [Fact]
    public void Activity_RunContinued()
    {
        var e = Event(GenerationEventKind.RunContinued);
        e.Threads = 1;

        var line = GenerationUiText.ActivityText(e);
        Assert.Equal("Continued: 1 thread left to write", line.Text);
        Assert.Equal(ActivityTone.Accent, line.Tone);
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
    public void Activity_ThreadWritten()
    {
        var e = Event(GenerationEventKind.ThreadWritten);
        e.ThreadId = "T-0042";
        e.Subject = "Budget";
        e.Emails = 1;
        e.Details = 0;

        var line = GenerationUiText.ActivityText(e);
        Assert.Equal("Written: \"Budget\", 1 email, 0 new ledger details", line.Text);
        Assert.Equal(ActivityTone.Success, line.Tone);
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
    public void Activity_RunCancelled()
    {
        var e = Event(GenerationEventKind.RunCancelled);
        e.Done = 4;
        e.Total = 1000;

        var line = GenerationUiText.ActivityText(e);
        Assert.Equal("Cancelled. 4 of 1,000 threads are saved.", line.Text);
        Assert.Equal(ActivityTone.Warning, line.Tone);
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
    public void Activity_EveryKindHasText()
    {
        foreach (var kind in Enum.GetValues<GenerationEventKind>())
            Assert.False(string.IsNullOrWhiteSpace(GenerationUiText.ActivityText(Event(kind)).Text), kind.ToString());
    }

    [Theory]
    [InlineData(0, "0 emails, 0 new ledger details")]
    [InlineData(1, "1 email, 1 new ledger detail")]
    [InlineData(2, "2 emails, 2 new ledger details")]
    [InlineData(1000, "1,000 emails, 1,000 new ledger details")]
    public void Activity_ThreadWritten_Plurals(int n, string expected)
    {
        var e = Event(GenerationEventKind.ThreadWritten);
        e.Subject = "S";
        e.Emails = n;
        e.Details = n;
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
    [InlineData(0, "0 threads to write in 0 waves")]
    [InlineData(1, "1 thread to write in 1 wave")]
    [InlineData(2, "2 threads to write in 2 waves")]
    [InlineData(1000, "1,000 threads to write in 1,000 waves")]
    public void Activity_RunStarted_Plurals(int n, string expected)
    {
        var e = Event(GenerationEventKind.RunStarted);
        e.Threads = n;
        e.WaveCount = n;
        Assert.Equal("Started: " + expected, GenerationUiText.ActivityText(e).Text);
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
        Assert.Equal("Stopping... finished threads are saved.", GenerationUiText.ProgressText(View(GenerationStage.Stopping, 0)));
        Assert.Equal(GenerationUiText.StoppingText, GenerationUiText.ProgressText(View(GenerationStage.Stopping, 0)));
        Assert.Equal("", GenerationUiText.ProgressText(View(GenerationStage.Idle, 0)));
    }

    [Theory]
    [InlineData(0, "writing 0 threads...", "merging 0 new ledger details...")]
    [InlineData(1, "writing 1 thread...", "merging 1 new ledger detail...")]
    [InlineData(2, "writing 2 threads...", "merging 2 new ledger details...")]
    [InlineData(1000, "writing 1,000 threads...", "merging 1,000 new ledger details...")]
    public void ProgressText_Plurals(int n, string writing, string merging)
    {
        Assert.EndsWith(writing, GenerationUiText.ProgressText(View(GenerationStage.Writing, n)));
        Assert.EndsWith(merging, GenerationUiText.ProgressText(View(GenerationStage.Merging, n)));
    }

    // --- Outcomes ---

    [Fact]
    public void Outcome_FinishedClean()
    {
        var line = GenerationUiText.OutcomeText(GenerationOutcome.Finished, Counts(total: 10, done: 10, emailsWritten: 30, files: 37, mailboxes: 6), "");

        Assert.Equal("Done: 10 relevant threads written as 30 emails (37 EML files in 6 mailboxes). The answer key and story so far are updated.", line.Text);
        Assert.Equal(ActivityTone.Success, line.Tone);
    }

    [Theory]
    [InlineData(0, "Done: 0 relevant threads written as 0 emails (0 EML files in 0 mailboxes).")]
    [InlineData(1, "Done: 1 relevant thread written as 1 email (1 EML file in 1 mailbox).")]
    [InlineData(2, "Done: 2 relevant threads written as 2 emails (2 EML files in 2 mailboxes).")]
    [InlineData(1000, "Done: 1,000 relevant threads written as 1,000 emails (1,000 EML files in 1,000 mailboxes).")]
    public void Outcome_FinishedClean_Plurals(int n, string expectedStart)
    {
        var line = GenerationUiText.OutcomeText(GenerationOutcome.Finished, Counts(total: n, done: n, emailsWritten: n, files: n, mailboxes: n), "");
        Assert.StartsWith(expectedStart, line.Text);
    }

    [Fact]
    public void Outcome_FinishedWithWarningsOnly_DropsFailedClauseAndInstruction()
    {
        var line = GenerationUiText.OutcomeText(GenerationOutcome.Finished, Counts(total: 10, done: 10, warnings: 2), "");

        Assert.Equal("Done with 2 warnings. The answer key marks them.", line.Text);
        Assert.Equal(ActivityTone.Warning, line.Tone);
    }

    [Fact]
    public void Outcome_FinishedWithWarningsAndFailures()
    {
        var line = GenerationUiText.OutcomeText(GenerationOutcome.Finished, Counts(total: 10, done: 9, warnings: 2, failed: 1), "");

        Assert.Equal("Done with 2 warnings and 1 failed thread. The answer key marks them; click Retry Failed Threads to try the failed ones again.", line.Text);
        Assert.Equal(ActivityTone.Warning, line.Tone);
    }

    [Fact]
    public void Outcome_FinishedWithFailuresOnly_DropsWarningClause()
    {
        var line = GenerationUiText.OutcomeText(GenerationOutcome.Finished, Counts(total: 10, done: 8, failed: 2), "");

        Assert.Equal("Done with 2 failed threads. The answer key marks them; click Retry Failed Threads to try the failed ones again.", line.Text);
        Assert.Equal(ActivityTone.Warning, line.Tone);
    }

    [Theory]
    [InlineData(1, "Done with 1 warning and 1 failed thread.")]
    [InlineData(2, "Done with 2 warnings and 2 failed threads.")]
    [InlineData(1000, "Done with 1,000 warnings and 1,000 failed threads.")]
    public void Outcome_WarningPlurals(int n, string expectedStart)
    {
        var line = GenerationUiText.OutcomeText(GenerationOutcome.Finished, Counts(total: 5000, done: 1000, warnings: n, failed: n), "");
        Assert.StartsWith(expectedStart, line.Text);
    }

    [Fact]
    public void Outcome_Cancelled()
    {
        var line = GenerationUiText.OutcomeText(GenerationOutcome.Cancelled, Counts(total: 10, done: 4), "");

        Assert.Equal("Cancelled. 4 of 10 threads are saved. Click Continue Generating to write the rest.", line.Text);
        Assert.Equal(ActivityTone.Warning, line.Tone);
    }

    [Fact]
    public void Outcome_Fatal_PointsToApiConfiguration()
    {
        var line = GenerationUiText.OutcomeText(GenerationOutcome.Fatal, Counts(), "Invalid API key");

        Assert.Equal("Generation stopped: Invalid API key. Finished threads are saved. Fix the problem on the API Configuration step, then click Continue Generating.", line.Text);
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
        Assert.Equal("Cleared the earlier run: deleted 1,200 EML files and the narrative ledger.", GenerationUiText.StartedOverText(1200));
        Assert.Equal("RD-000123.eml is no longer in the case folder.", GenerationUiText.MissingEmlText("RD-000123"));
    }

    [Theory]
    [InlineData(0, "deleted 0 EML files")]
    [InlineData(1, "deleted 1 EML file ")]
    [InlineData(2, "deleted 2 EML files")]
    [InlineData(1000, "deleted 1,000 EML files")]
    public void StartedOver_Plurals(int n, string expected)
    {
        Assert.Contains(expected, GenerationUiText.StartedOverText(n));
    }

    // --- Entry decision (first match wins) ---

    private static GenerationEntryInput Entry(
        bool running = false, bool hasPlan = true, bool approved = true, bool hasSavedRun = true,
        bool planChanged = false, string unreadable = "", int total = 10, int written = 10, int failed = 0) =>
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
            Failed = failed
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

    // --- State cards ---

    [Fact]
    public void StateCard_Ready()
    {
        var card = GenerationUiText.StateCard(GenerationStateCard.Ready, 12, 30, 4, 3.2m, "Claude Sonnet", 0, "");

        Assert.Equal("Ready to write the relevant emails", card.Heading);
        Assert.Equal(
            "Generate Emails writes the 12 relevant threads (30 emails) from the approved plan, oldest first, in 4 waves. Each finished thread is saved straight to the custodians' mailboxes, so you can stop at any time and continue later. Estimated cost: ≈$3.20 on Claude Sonnet, ±50%.",
            card.Body);
        Assert.Equal("Generate Emails", card.ButtonText);
        Assert.False(card.Danger);
    }

    [Theory]
    [InlineData(0, "0 relevant threads (0 emails)", "in 0 waves.")]
    [InlineData(1, "1 relevant thread (1 email)", "in 1 wave.")]
    [InlineData(2, "2 relevant threads (2 emails)", "in 2 waves.")]
    [InlineData(1000, "1,000 relevant threads (1,000 emails)", "in 1,000 waves.")]
    public void StateCard_Ready_Plurals(int n, string threads, string waves)
    {
        var body = GenerationUiText.StateCard(GenerationStateCard.Ready, n, n, n, 250m, "M", 0, "").Body;
        Assert.Contains(threads, body);
        Assert.Contains(waves, body);
        Assert.Contains("≈$250 on M", body);
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
    public void Banner_NoFailures()
    {
        Assert.Equal("The last run did not finish.", GenerationUiText.BannerHeading);
        Assert.Equal("Continue Generating", GenerationUiText.BannerButton);
        Assert.Equal(
            "4 of 10 relevant threads are written and saved. Continue Generating writes the rest and keeps everything already saved.",
            GenerationUiText.BannerBody(4, 10, 0));
    }

    [Fact]
    public void Banner_WithFailures()
    {
        Assert.EndsWith(
            "keeps everything already saved, and tries the 2 failed threads again.",
            GenerationUiText.BannerBody(4, 10, 2));
    }

    [Theory]
    [InlineData(1, "of 1 relevant thread are", "tries the 1 failed thread again.")]
    [InlineData(2, "of 2 relevant threads are", "tries the 2 failed threads again.")]
    [InlineData(1000, "of 1,000 relevant threads are", "tries the 1,000 failed threads again.")]
    public void Banner_Plurals(int n, string totalPart, string failedPart)
    {
        var body = GenerationUiText.BannerBody(0, n, n);
        Assert.Contains(totalPart, body);
        Assert.EndsWith(failedPart, body);
    }

    [Fact]
    public void Banner_ZeroTotal()
    {
        Assert.StartsWith("0 of 0 relevant threads are", GenerationUiText.BannerBody(0, 0, 0));
    }

    // --- Confirmations ---

    [Fact]
    public void CG1_CancelGeneration()
    {
        var (title, text) = GenerationUiText.CG1CancelGeneration(3);

        Assert.Equal("Cancel Generation", title);
        Assert.Equal("Stop generating? The 3 threads already written stay saved. Threads in progress are discarded and can be written later with Continue Generating.", text);
    }

    [Theory]
    [InlineData(0, "The 0 threads already written stay saved.")]
    [InlineData(1, "The 1 thread already written stays saved.")]
    [InlineData(2, "The 2 threads already written stay saved.")]
    [InlineData(1000, "The 1,000 threads already written stay saved.")]
    public void CG1_Plurals(int n, string expected)
    {
        Assert.Contains(expected, GenerationUiText.CG1CancelGeneration(n).Text);
    }

    [Fact]
    public void CG2_StartOver()
    {
        var (title, text) = GenerationUiText.CG2StartOver(42);

        Assert.Equal("Start Over", title);
        Assert.Equal(
            "Delete the 42 generated EML files and the narrative ledger, and reset the answer key to Planned? This cannot be undone. The corpus plan and case guide are kept.",
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
        Assert.Equal("Emails are still being written. Close and stop? Finished threads are saved; threads in progress are discarded and can be written later with Continue Generating.", text);
    }

    // --- Case Summary: Mailboxes row ---

    [Fact]
    public void MailboxesRow_NotGenerated()
    {
        var row = GenerationUiText.MailboxesRow(new MailboxesRowInput { Generated = false, Total = 10, FolderExists = false });

        Assert.Equal("Not generated yet", row.Line3);
        Assert.Equal(SummaryTone.Muted, row.Line3Tone);
        Assert.Equal(SummaryTone.Muted, row.DotTone);
        Assert.False(row.ShowPrimary);
        Assert.True(row.ShowSecondary);

        Assert.True(GenerationUiText.MailboxesRow(new MailboxesRowInput { Generated = false, FolderExists = true }).ShowPrimary);
    }

    [Fact]
    public void MailboxesRow_Complete()
    {
        var row = GenerationUiText.MailboxesRow(new MailboxesRowInput
        {
            Generated = true, Total = 10, Written = 10, Files = 37, Mailboxes = 6, FolderExists = true
        });

        Assert.Equal("37 EML files in 6 custodian mailboxes · Mailboxes folder", row.Line2);
        Assert.Equal("All 10 relevant threads written", row.Line3);
        Assert.Equal(SummaryTone.Success, row.Line3Tone);
        Assert.Equal(SummaryTone.Success, row.DotTone);
        Assert.True(row.ShowPrimary);
        Assert.False(row.ShowSecondary);
    }

    [Fact]
    public void MailboxesRow_Partial()
    {
        var row = GenerationUiText.MailboxesRow(new MailboxesRowInput
        {
            Generated = true, Total = 10, Written = 7, Warnings = 1, Failed = 2, Files = 20, Mailboxes = 4, FolderExists = true
        });

        Assert.Equal("7 of 10 relevant threads written · 1 with warnings · 2 failed", row.Line3);
        Assert.Equal(SummaryTone.Warning, row.Line3Tone);
        Assert.Equal(SummaryTone.Warning, row.DotTone);
    }

    [Fact]
    public void MailboxesRow_Partial_ZeroClausesDropped()
    {
        Assert.Equal(
            "7 of 10 relevant threads written",
            GenerationUiText.MailboxesRow(new MailboxesRowInput { Generated = true, Total = 10, Written = 7 }).Line3);
        Assert.Equal(
            "8 of 10 relevant threads written · 2 failed",
            GenerationUiText.MailboxesRow(new MailboxesRowInput { Generated = true, Total = 10, Written = 8, Failed = 2 }).Line3);
        Assert.Equal(
            "10 of 10 relevant threads written · 3 with warnings",
            GenerationUiText.MailboxesRow(new MailboxesRowInput { Generated = true, Total = 10, Written = 10, Warnings = 3 }).Line3);
    }

    [Fact]
    public void MailboxesRow_EarlierPlan()
    {
        var row = GenerationUiText.MailboxesRow(new MailboxesRowInput
        {
            Generated = true, EarlierPlan = true, Total = 10, Written = 10, Files = 37, Mailboxes = 6, FolderExists = true
        });

        Assert.Equal("Written for an earlier plan. Start over on Generate Corpus.", row.Line3);
        Assert.Equal(SummaryTone.Warning, row.Line3Tone);
        Assert.Equal(SummaryTone.Warning, row.DotTone);
    }

    [Theory]
    [InlineData(0, "0 EML files in 0 custodian mailboxes · Mailboxes folder", "All 0 relevant threads written")]
    [InlineData(1, "1 EML file in 1 custodian mailbox · Mailboxes folder", "All 1 relevant thread written")]
    [InlineData(2, "2 EML files in 2 custodian mailboxes · Mailboxes folder", "All 2 relevant threads written")]
    [InlineData(1000, "1,000 EML files in 1,000 custodian mailboxes · Mailboxes folder", "All 1,000 relevant threads written")]
    public void MailboxesRow_Plurals(int n, string line2, string line3)
    {
        var row = GenerationUiText.MailboxesRow(new MailboxesRowInput { Generated = true, Total = n, Written = n, Files = n, Mailboxes = n });
        Assert.Equal(line2, row.Line2);
        Assert.Equal(line3, row.Line3);
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
}
