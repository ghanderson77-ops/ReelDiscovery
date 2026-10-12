using ReelDiscovery.Models.Qa;
using ReelDiscovery.Services.Qa;
using ReelDiscovery.UserControls;

namespace ReelDiscovery.Tests.Qa;

public class QaUiTextTests
{
    private static readonly string[] BannedWords = { "evidence", "noise", "filler" };

    private static void AssertNoBannedWords(string text)
    {
        foreach (var word in BannedWords)
            Assert.DoesNotContain(word, text, StringComparison.OrdinalIgnoreCase);
    }

    private static ScanFinding Finding(string doc, string thread, ScanCheck check) =>
        new() { DocId = doc, ThreadId = thread, Check = check, Subject = "s", Text = "t", DateSent = new DateTime(2024, 3, 1) };

    private static LeakageReport Leakage(double ba, int relevant = 100, int nonRelevant = 400, params LeakageSignal[] signals) =>
        new()
        {
            BalancedAccuracy = ba,
            RelevantEmails = relevant,
            NonRelevantEmails = nonRelevant,
            Verdict = LeakageAudit.VerdictFor(ba, relevant, nonRelevant),
            Signals = signals.ToList()
        };

    private static LeakageSignal Signal(string feature, LeakageSignalKind kind, double share = 0.83, double baseRate = 0.2) =>
        new() { Feature = feature, Kind = kind, RelevantShare = share, BaseRate = baseRate, Support = 12, Lift = share - baseRate };

    // --- Consistency scans row (R2) ---

    [Fact]
    public void ScansRow_NotRun()
    {
        var row = QaUiText.ScansRow(null, outOfDate: false);
        Assert.Equal("Free checks for mentions of facts dated after the email and values that contradict the case guide. They run at the end of every run.", row.Line2);
        Assert.Equal("Not run yet. They run when the emails are generated.", row.Line3);
        Assert.Equal(SummaryTone.Muted, row.Line3Tone);
        Assert.Equal(SummaryTone.Muted, row.DotTone);
        Assert.False(row.ShowPrimary);
    }

    [Theory]
    [InlineData(0, "No problems in 0 scanned emails")]
    [InlineData(1, "No problems in 1 scanned email")]
    [InlineData(2, "No problems in 2 scanned emails")]
    [InlineData(1000, "No problems in 1,000 scanned emails")]
    public void ScansRow_Clean(int scanned, string expected)
    {
        var row = QaUiText.ScansRow(new ScanReport { ScannedEmails = scanned }, outOfDate: false);
        Assert.Equal(expected, row.Line3);
        Assert.Equal(SummaryTone.Success, row.Line3Tone);
        Assert.Equal(SummaryTone.Success, row.DotTone);
        Assert.False(row.ShowPrimary);
    }

    [Fact]
    public void ScansRow_Findings_CountsEmailsThreadsAndChecks()
    {
        var report = new ScanReport
        {
            ScannedEmails = 500,
            Findings =
            {
                Finding("RD-1", "T1", ScanCheck.Timeline),
                Finding("RD-1", "T1", ScanCheck.Value),
                Finding("RD-2", "T1", ScanCheck.Timeline),
                Finding("RD-3", "T2", ScanCheck.StoryDetail)
            }
        };

        var row = QaUiText.ScansRow(report, outOfDate: false);

        Assert.Equal("3 flagged emails in 2 threads: 2 timeline, 1 value, 1 story-detail", row.Line3);
        Assert.Equal(SummaryTone.Warning, row.Line3Tone);
        Assert.Equal(SummaryTone.Warning, row.DotTone);
        Assert.True(row.ShowPrimary);
        Assert.Equal(row.Line3, row.Line3Tooltip);
    }

    [Fact]
    public void ScansRow_Findings_ZeroClausesDropped_SingularForms()
    {
        var report = new ScanReport { ScannedEmails = 10, Findings = { Finding("RD-1", "T1", ScanCheck.Value) } };
        Assert.Equal("1 flagged email in 1 thread: 1 value", QaUiText.ScansRow(report, false).Line3);

        var attachment = new ScanReport { ScannedEmails = 10, Findings = { Finding("RD-1", "T1", ScanCheck.Attachment) } };
        Assert.Equal("1 flagged email in 1 thread: 1 attachment", QaUiText.ScansRow(attachment, false).Line3);
    }

    [Fact]
    public void ScansRow_ThousandsSeparators()
    {
        var report = new ScanReport { ScannedEmails = 5000 };
        for (var i = 0; i < 1000; i++)
            report.Findings.Add(Finding($"RD-{i}", $"T{i}", ScanCheck.Timeline));

        Assert.Equal("1,000 flagged emails in 1,000 threads: 1,000 timeline", QaUiText.ScansRow(report, false).Line3);
    }

    [Fact]
    public void ScansRow_OutOfDate()
    {
        var report = new ScanReport { ScannedEmails = 10, Findings = { Finding("RD-1", "T1", ScanCheck.Value) } };
        var row = QaUiText.ScansRow(report, outOfDate: true);
        Assert.Equal("Last scan is older than the corpus. It runs again at the end of the next run.", row.Line3);
        Assert.Equal(SummaryTone.Muted, row.Line3Tone);
        Assert.Equal(SummaryTone.Muted, row.DotTone);
    }

    // --- Leakage audit row (R3) ---

    [Fact]
    public void LeakageRow_NotRun()
    {
        var row = QaUiText.LeakageRow(null);
        Assert.Equal("Checks whether sender, dates, recipients, attachments, thread depth or privilege marks alone can tell relevant mail from non-relevant mail.", row.Line2);
        Assert.Equal("Not run yet.", row.Line3);
        Assert.Equal(SummaryTone.Muted, row.DotTone);
        Assert.False(row.ShowPrimary);
    }

    [Fact]
    public void LeakageRow_PassAtExactly60()
    {
        var row = QaUiText.LeakageRow(Leakage(0.60, signals: Signal("privilege banner", LeakageSignalKind.OnlyOnRelevant)));
        Assert.Equal("Pass: metadata alone predicts relevance 60% of the time (50% is a coin flip).", row.Line3);
        Assert.Equal(SummaryTone.Success, row.Line3Tone);
        Assert.Equal(SummaryTone.Success, row.DotTone);
        Assert.True(row.ShowPrimary);
    }

    [Fact]
    public void LeakageRow_WarningJustAbove60()
    {
        var row = QaUiText.LeakageRow(Leakage(0.6001, signals: Signal("privilege banner", LeakageSignalKind.OnlyOnRelevant)));
        Assert.Equal("Warning: metadata alone predicts relevance 60% of the time. Top signal: privilege banner: only on relevant mail.", row.Line3);
        Assert.Equal(SummaryTone.Warning, row.Line3Tone);
        Assert.Equal(SummaryTone.Warning, row.DotTone);
    }

    [Fact]
    public void LeakageRow_WarningAtExactly70()
    {
        var row = QaUiText.LeakageRow(Leakage(0.70, signals: Signal("sent on Sunday", LeakageSignalKind.Lift, 0.83, 0.2)));
        Assert.Equal("Warning: metadata alone predicts relevance 70% of the time. Top signal: sent on Sunday: 83% relevant vs 20% overall.", row.Line3);
        Assert.Equal(SummaryTone.Warning, row.Line3Tone);
    }

    [Fact]
    public void LeakageRow_FailJustAbove70()
    {
        var row = QaUiText.LeakageRow(Leakage(0.7001, signals: Signal("privileged subject tag", LeakageSignalKind.NeverOnRelevant)));
        Assert.Equal("Fail: metadata alone predicts relevance 70% of the time. Top signal: privileged subject tag: never on relevant mail.", row.Line3);
        Assert.Equal(SummaryTone.Danger, row.Line3Tone);
        Assert.Equal(SummaryTone.Danger, row.DotTone);
    }

    [Fact]
    public void LeakageRow_NoSignals_DropsTopSignalSentence()
    {
        var row = QaUiText.LeakageRow(Leakage(0.75));
        Assert.Equal("Fail: metadata alone predicts relevance 75% of the time.", row.Line3);
    }

    [Theory]
    [InlineData(39, 400, "this corpus has 39 and 400.")]
    [InlineData(1000, 1, "this corpus has 1,000 and 1.")]
    public void LeakageRow_NotEnoughData(int relevant, int nonRelevant, string tail)
    {
        var row = QaUiText.LeakageRow(Leakage(0.9, relevant, nonRelevant));
        Assert.Equal($"Not enough data: the audit needs {LeakageAudit.MinEmailsPerClass} relevant and {LeakageAudit.MinEmailsPerClass} non-relevant emails; {tail}", row.Line3);
        Assert.Equal(SummaryTone.Muted, row.DotTone);
        Assert.True(row.ShowPrimary);
    }

    [Fact]
    public void LeakageTooltip_NamesThresholds()
    {
        Assert.Equal(
            "Balanced accuracy of a simple model trained on metadata only, tested on threads it did not see (5-fold). Pass is 60% or lower, Warning is above 60% up to 70%, Fail is above 70%. Some signal is normal: real custodians and the dispute dates also predict relevance.",
            QaUiText.LeakageTooltip);
        Assert.Equal(QaUiText.LeakageTooltip, QaUiText.LeakageRow(Leakage(0.5)).Line3Tooltip);
    }

    [Fact]
    public void SignalText_AllKinds()
    {
        Assert.Equal("privilege banner: only on relevant mail", QaUiText.SignalText(Signal("privilege banner", LeakageSignalKind.OnlyOnRelevant)));
        Assert.Equal("sender a@b.com: never on relevant mail", QaUiText.SignalText(Signal("sender a@b.com", LeakageSignalKind.NeverOnRelevant)));
        Assert.Equal("sent on Sunday: 83% relevant vs 20% overall", QaUiText.SignalText(Signal("sent on Sunday", LeakageSignalKind.Lift, 0.83, 0.2)));
    }

    [Fact]
    public void SignalCells_FormatShareSupportAndLift()
    {
        var cells = QaUiText.SignalCells(Signal("sent on Sunday", LeakageSignalKind.Lift, 0.83, 0.2));
        Assert.Equal("sent on Sunday: 83% relevant vs 20% overall", cells.Signal);
        Assert.Equal("83%", cells.Share);
        Assert.Equal("12", cells.Emails);
        Assert.Equal("+63 pts", cells.Lift);

        var negative = QaUiText.SignalCells(Signal("sender a@b.com", LeakageSignalKind.NeverOnRelevant, 0.0, 0.2));
        Assert.Equal("−20 pts", negative.Lift);
    }

    // --- Blind review row (R4) ---

    [Fact]
    public void BlindReviewRow_NotRun()
    {
        var row = QaUiText.BlindReviewRow(new BlindReviewRowInput
        {
            State = BlindReviewState.NotRun, SampleSize = 1000, Model = "gpt-5-mini", EstimateCost = 1.234m
        });
        Assert.Equal("A second model labels a sample of emails without seeing the answer key. Disagreements are listed for you to check.", row.Line2);
        Assert.Equal("Not run yet. ≈$1.23 to review 1,000 emails with gpt-5-mini.", row.Line3);
        Assert.Equal(SummaryTone.Muted, row.DotTone);
        Assert.False(row.ShowPrimary);
    }

    [Theory]
    [InlineData(0, 1, "Reviewing: 0 of 1 email...")]
    [InlineData(1, 2, "Reviewing: 1 of 2 emails...")]
    [InlineData(999, 1000, "Reviewing: 999 of 1,000 emails...")]
    public void BlindReviewRow_Running(int done, int total, string expected)
    {
        var row = QaUiText.BlindReviewRow(new BlindReviewRowInput { State = BlindReviewState.Running, Reviewed = done, SampleSize = total, Model = "m" });
        Assert.Equal(expected, row.Line3);
        Assert.Equal(SummaryTone.Muted, row.Line3Tone);
        Assert.True(row.InProgress);
        Assert.False(row.ShowPrimary);
    }

    [Fact]
    public void BlindReviewRow_Stopped()
    {
        var row = QaUiText.BlindReviewRow(new BlindReviewRowInput { State = BlindReviewState.Stopped, Reviewed = 7, SampleSize = 20, Model = "m" });
        Assert.Equal("7 of 20 emails reviewed; the review stopped. Run Blind Review again to finish the rest.", row.Line3);
        Assert.Equal(SummaryTone.Warning, row.DotTone);
    }

    [Fact]
    public void BlindReviewRow_DoneAgree_WithAndWithoutFallback()
    {
        var input = new BlindReviewRowInput { State = BlindReviewState.Done, Reviewed = 1, SampleSize = 1, Model = "claude-haiku" };
        var row = QaUiText.BlindReviewRow(input);
        Assert.Equal("1 email reviewed by claude-haiku: all agree with the answer key", row.Line3);
        Assert.Equal(SummaryTone.Success, row.DotTone);
        Assert.False(row.ShowPrimary);

        input.Fallback = true;
        row = QaUiText.BlindReviewRow(input);
        Assert.Equal("1 email reviewed by claude-haiku: all agree with the answer key · same provider as the corpus", row.Line3);
        Assert.Equal(SummaryTone.Warning, row.DotTone);
        Assert.Equal(QaUiText.FallbackTooltip, row.Line3Tooltip);
    }

    [Theory]
    [InlineData(1000, 1, "1,000 emails reviewed by m: 1 disagreement (100% agree)")]
    [InlineData(2, 1, "2 emails reviewed by m: 1 disagreement (50% agree)")]
    [InlineData(2, 2, "2 emails reviewed by m: 2 disagreements (0% agree)")]
    [InlineData(4000, 1000, "4,000 emails reviewed by m: 1,000 disagreements (75% agree)")]
    public void BlindReviewRow_DoneDisagreements(int total, int disagreements, string expected)
    {
        var row = QaUiText.BlindReviewRow(new BlindReviewRowInput
        {
            State = BlindReviewState.Done, Reviewed = total, SampleSize = total, Disagreements = disagreements, Model = "m"
        });
        Assert.Equal(expected, row.Line3);
        Assert.Equal(SummaryTone.Warning, row.DotTone);
        Assert.True(row.ShowPrimary);
    }

    [Fact]
    public void BlindReviewRow_DoneDisagreements_WithFallback()
    {
        var row = QaUiText.BlindReviewRow(new BlindReviewRowInput
        {
            State = BlindReviewState.Done, Reviewed = 10, SampleSize = 10, Disagreements = 3, Model = "m", Fallback = true
        });
        Assert.Equal("10 emails reviewed by m: 3 disagreements (70% agree) · same provider as the corpus", row.Line3);
    }

    // --- Findings dialog content (S3) ---

    [Fact]
    public void FindingsContent_Scans()
    {
        var c = QaUiText.FindingsContent(QaFindingsMode.Scans, flaggedEmails: 3, threads: 2, reviewerModel: "", leakage: null);
        Assert.Equal("Consistency Scan Findings", c.Title);
        Assert.Equal("Consistency scan findings", c.Headline);
        Assert.Equal("3 flagged emails in 2 threads, one row per finding. Nothing in the corpus has changed.", c.Summary);
        Assert.StartsWith("Each row names the check and what it found.", QaUiText.FindingsSummaryTooltip(QaFindingsMode.Scans));
    }

    [Fact]
    public void FindingsContent_BlindReview()
    {
        var c = QaUiText.FindingsContent(QaFindingsMode.BlindReview, flaggedEmails: 1, threads: 1, reviewerModel: "m", leakage: null);
        Assert.Equal("Blind Review Disagreements", c.Title);
        Assert.Equal("Blind review disagreements", c.Headline);
        Assert.Equal("m disagreed with the answer key on 1 email. Relabel or regenerate each one.", c.Summary);
        Assert.Equal("Check each one: edit the Relevance column of the answer key spreadsheet to relabel it, or click Regenerate Thread... to write its thread again. Relabels are kept when the answer key is rewritten; edits to other columns are replaced.", QaUiText.FindingsSummaryTooltip(QaFindingsMode.BlindReview));
    }

    [Fact]
    public void FindingsContent_Leakage()
    {
        var c = QaUiText.FindingsContent(QaFindingsMode.Leakage, 0, 0, "", Leakage(0.65, signals: Signal("privilege banner", LeakageSignalKind.OnlyOnRelevant)));
        Assert.Equal("Leakage Audit Report", c.Title);
        Assert.Equal("Warning: metadata alone predicts relevance 65% of the time.", c.Headline);
        Assert.Equal(QaUiText.LeakageTooltip, c.Summary);
    }

    [Theory]
    [InlineData(QaFindingsMode.Scans)]
    [InlineData(QaFindingsMode.BlindReview)]
    public void FindingsContent_Empty_SaysNoFindings(QaFindingsMode mode)
    {
        Assert.Equal("No findings.", QaUiText.FindingsContent(mode, 0, 0, "m", null).Summary);
    }

    [Fact]
    public void CellTexts()
    {
        Assert.Equal("Timeline", QaUiText.CheckText(ScanCheck.Timeline));
        Assert.Equal("Value", QaUiText.CheckText(ScanCheck.Value));
        Assert.Equal("Story detail", QaUiText.CheckText(ScanCheck.StoryDetail));
        Assert.Equal("Attachment", QaUiText.CheckText(ScanCheck.Attachment));
        Assert.Equal("Relevant", QaUiText.LabelText(true));
        Assert.Equal("Not relevant", QaUiText.LabelText(false));
        Assert.Equal("Mar 1, 2024", QaUiText.DateText(new DateTime(2024, 3, 1, 15, 30, 0)));
        Assert.Equal(" · queued", QaUiText.QueuedSuffix);
    }

    // --- Footer status (S3) ---

    [Theory]
    [InlineData(1, "1 thread queued for regeneration. Go to Generate Corpus and click Continue Generating to write it.")]
    [InlineData(2, "2 threads queued for regeneration. Go to Generate Corpus and click Continue Generating to write them.")]
    [InlineData(1000, "1,000 threads queued for regeneration. Go to Generate Corpus and click Continue Generating to write them.")]
    public void FooterQueued(int q, string expected) => Assert.Equal(expected, QaUiText.FooterQueued(q));

    [Theory]
    [InlineData(1, "1 EML file could not be deleted because it is open. Close any program that has it open and delete it by hand.")]
    [InlineData(2, "2 EML files could not be deleted because it is open. Close any program that has them open and delete them by hand.")]
    public void FooterNotDeleted(int n, string expected) => Assert.Equal(expected, QaUiText.FooterNotDeleted(n, "it is open."));

    [Fact]
    public void FooterKeyNotUpdated() =>
        Assert.Equal("The answer key could not be updated: the file is locked. It is updated again when you click Continue Generating.",
            QaUiText.FooterKeyNotUpdated("the file is locked."));

    [Fact]
    public void RegenerateFailed() =>
        Assert.Equal("Could not queue the thread for regeneration: This thread has not been written yet.",
            QaUiText.RegenerateFailed("This thread has not been written yet."));

    // --- Confirmations ---

    [Fact]
    public void CQ1_AllClauseVariants()
    {
        var plain = QaUiText.CQ1RegenerateThread("Q3 forecast", 1, 0, relevant: false);
        Assert.Equal("Regenerate Thread", plain.Title);
        Assert.Equal("Regenerate \"Q3 forecast\"? Its 1 EML file is deleted now and the answer key marks the thread Planned. Continue Generating on the Generate Corpus step writes it again.", plain.Text);

        var full = QaUiText.CQ1RegenerateThread("Pour schedule", 4, 2, relevant: true);
        Assert.Equal("Regenerate \"Pour schedule\"? Its 4 EML files and 2 attachments are deleted now and the answer key marks the thread Planned. Continue Generating on the Generate Corpus step writes it again, keeping the story details it already established so later threads stay consistent.", full.Text);

        Assert.Contains("and 1 attachment are", QaUiText.CQ1RegenerateThread("s", 1000, 1, false).Text);
        Assert.Contains("Its 1,000 EML files", QaUiText.CQ1RegenerateThread("s", 1000, 1, false).Text);
    }

    [Fact]
    public void CQ1_DiscoveryClause_OnlyWhenCited()
    {
        var plain = QaUiText.CQ1RegenerateThread("s", 2, 1, true);
        var cited = QaUiText.CQ1RegenerateThread("s", 2, 1, true, citedByDiscovery: true);
        Assert.Equal(plain.Text, QaUiText.CQ1RegenerateThread("s", 2, 1, true, false).Text);
        Assert.Equal(plain.Text + " Discovery documents that cite its emails will show Out of date.", cited.Text);
        Assert.EndsWith(" Discovery documents that cite its emails will show Out of date.", cited.Text);
        Assert.Equal("Regenerate Thread", cited.Title);
    }

    [Fact]
    public void CQ1_RelabelsClause_OnlyWhenRelabelsExist()
    {
        var plain = QaUiText.CQ1RegenerateThread("Q3 schedule", 2, 0, true);
        Assert.Equal(plain.Text, QaUiText.CQ1RegenerateThread("Q3 schedule", 2, 0, true, false, false).Text);
        Assert.DoesNotContain("Relabels", plain.Text);

        var kept = QaUiText.CQ1RegenerateThread("Q3 schedule", 2, 0, true, relabelsKept: true);
        Assert.Equal(plain.Text + " Relabels you made for its emails in the answer key are kept.", kept.Text);

        var both = QaUiText.CQ1RegenerateThread("Q3 schedule", 2, 0, true, citedByDiscovery: true, relabelsKept: true);
        Assert.EndsWith(" will show Out of date. Relabels you made for its emails in the answer key are kept.", both.Text);
        Assert.Equal("Regenerate Thread", both.Title);
    }

    [Theory]
    [InlineData(0, "0 emails", "are")]
    [InlineData(1, "1 email", "is")]
    [InlineData(1000, "1,000 emails", "are")]
    public void CQ2_CancelReview(int d, string count, string verb)
    {
        var c = QaUiText.CQ2CancelReview(d);
        Assert.Equal("Cancel Review", c.Title);
        Assert.Equal($"Stop the blind review? The {count} already reviewed {verb} kept, and Run Blind Review finishes the rest later.", c.Text);
    }

    [Fact]
    public void CQ3_LeaveDuringReview()
    {
        var c = QaUiText.CQ3LeaveDuringReview();
        Assert.Equal("Stop Blind Review?", c.Title);
        Assert.Equal("The blind review is still running. Leave and stop it? Reviewed emails are kept.", c.Text);
    }

    // --- Blind review dialog (S4) and status strip ---

    [Fact]
    public void BlindReviewDialogCopy()
    {
        Assert.Equal("Blind Review", QaUiText.BlindReviewTitle);
        Assert.Equal("Run a blind review", QaUiText.BlindReviewHeadline);
        Assert.Equal("gpt-5-mini from OpenAI reads each sampled email the way a reviewer sees it, without the answer key, and says whether it is relevant and why. Disagreements are listed for you to check. Nothing in the corpus changes.",
            QaUiText.BlindReviewBody("gpt-5-mini", "OpenAI"));
        Assert.Equal("Only one provider key is tested, so the reviewer comes from the same provider that wrote the corpus. Test a key for the other provider on the API Configuration step for a more independent review.",
            QaUiText.FallbackSentence);
        Assert.Equal("Emails to review:", QaUiText.SampleLabel);
        Assert.Equal("of 123,456", QaUiText.SampleOfTotal(123456));
        Assert.Equal("Default: all 1,000 relevant emails and the same number of non-relevant ones, near-miss decoys first.", QaUiText.DefaultRule(1000));
    }

    [Fact]
    public void Estimate_WithAndWithoutResumeClause()
    {
        Assert.Equal("Estimated cost ≈$1.23, ±50%.", QaUiText.Estimate(1.234m, remaining: 50, reviewed: 0));
        Assert.Equal("Estimated cost ≈$250, ±50% for the 1 email not yet reviewed; 1,000 already reviewed are kept.", QaUiText.Estimate(250m, 1, 1000));
        Assert.Equal("Estimated cost ≈$0.40, ±50% for the 2 emails not yet reviewed; 1 already reviewed is kept.", QaUiText.Estimate(0.4m, 2, 1));
    }

    [Fact]
    public void StatusStripLines()
    {
        Assert.Equal("Blind review: 1,000 of 2,000 emails reviewed...", QaUiText.BlindReviewProgress(1000, 2000));
        Assert.Equal("Blind review: 0 of 1 email reviewed...", QaUiText.BlindReviewProgress(0, 1));

        var clean = QaUiText.BlindReviewFinished(0);
        Assert.Equal("Blind review finished: 0 disagreements.", clean.Text);
        Assert.Equal(SummaryTone.Success, clean.Tone);
        var some = QaUiText.BlindReviewFinished(1);
        Assert.Equal("Blind review finished: 1 disagreement.", some.Text);
        Assert.Equal(SummaryTone.Warning, some.Tone);

        Assert.Equal("Blind review stopped: the network is down. Reviewed emails are kept; run it again to finish.",
            QaUiText.BlindReviewStopped("the network is down."));
        Assert.Equal("Running the leakage audit...", QaUiText.LeakageRunningText);
        Assert.Equal("Test an API key on the API Configuration step first.", QaUiText.NoReviewerKeyText);
    }

    [Fact]
    public void AllCopy_NoBannedWords()
    {
        var texts = new List<string>
        {
            QaUiText.ScansRow(null, false).Line2, QaUiText.ScansRow(null, false).Line3,
            QaUiText.ScansRow(new ScanReport(), true).Line3,
            QaUiText.LeakageRow(null).Line2, QaUiText.LeakageTooltip,
            QaUiText.BlindReviewRow(new BlindReviewRowInput()).Line2, QaUiText.FallbackTooltip,
            QaUiText.FindingsContent(QaFindingsMode.Scans, 2, 1, "", null).Summary,
            QaUiText.FindingsContent(QaFindingsMode.BlindReview, 2, 1, "m", null).Summary,
            QaUiText.FooterQueued(2), QaUiText.FooterNotDeleted(2, "r"), QaUiText.FooterKeyNotUpdated("r"),
            QaUiText.CQ1RegenerateThread("s", 2, 2, true).Text, QaUiText.CQ2CancelReview(2).Text, QaUiText.CQ3LeaveDuringReview().Text,
            QaUiText.BlindReviewBody("m", "p"), QaUiText.FallbackSentence, QaUiText.DefaultRule(2), QaUiText.Estimate(1m, 2, 2),
            QaUiText.BlindReviewStopped("x")
        };
        foreach (var state in Enum.GetValues<BlindReviewState>())
            texts.Add(QaUiText.BlindReviewRow(new BlindReviewRowInput { State = state, SampleSize = 2, Reviewed = 2, Disagreements = 1, Model = "m" }).Line3);

        foreach (var text in texts)
        {
            Assert.False(string.IsNullOrWhiteSpace(text));
            AssertNoBannedWords(text);
        }
    }
}
