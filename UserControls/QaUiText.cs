using System.Globalization;
using ReelDiscovery.Models.Qa;
using ReelDiscovery.Services.Qa;

namespace ReelDiscovery.UserControls;

/// <summary>
/// What QaFindingsDialog lists (07-UI-SPEC S3): scan findings, blind-review disagreements or the leakage signal table.
/// </summary>
public enum QaFindingsMode
{
    Scans,
    BlindReview,
    Leakage
}

/// <summary>
/// Blind review row state (07-UI-SPEC S2 R4).
/// </summary>
public enum BlindReviewState
{
    NotRun,
    Running,
    Stopped,
    Done
}

public class BlindReviewRowInput
{
    public BlindReviewState State;
    public string Model = "";

    // Emails in the sample (n) and emails reviewed so far (d)
    public int SampleSize;
    public int Reviewed;
    public int Disagreements;

    // D-12: the reviewer came from the provider that wrote the corpus
    public bool Fallback;

    // Not run only: the pre-run estimate for the default sample
    public decimal EstimateCost;
}

/// <summary>
/// A Case Summary row whose line 3 and dot may be Accent (blind review running); SummaryTone has no Accent.
/// </summary>
public class QaRowContent : SummaryRowContent
{
    // Line 3 and dot use Theme.Accent instead of the Line3Tone/DotTone mapping
    public bool InProgress;
}

/// <summary>
/// All QA copy (07-UI-SPEC S2 R2-R4, S3, S4, C-Q1..C-Q3), pure and unit-tested like GenerationUiText. Strings may carry
/// model-written subjects, reasons and addresses; labels showing them set UseMnemonic = false (T-07-58).
/// </summary>
public static class QaUiText
{
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    // --- Case Summary rows (S2) ---

    public const string ScansLine2 =
        "Free checks for mentions of facts dated after the email and values that contradict the case guide. They run at the end of every run.";

    public const string LeakageLine2 =
        "Checks whether sender, dates, recipients, attachments, thread depth or privilege marks alone can tell relevant mail from non-relevant mail.";

    public const string BlindReviewLine2 =
        "A second model labels a sample of emails without seeing the answer key. Disagreements are listed for you to check.";

    public const string FallbackClause = " · same provider as the corpus";

    public const string FallbackTooltip =
        "Only one provider key was tested, so the reviewer came from the provider that wrote the corpus and is less likely to notice its writing habits.";

    public const string LeakageRunningText = "Running the leakage audit...";

    public const string NoReviewerKeyText = "Test an API key on the API Configuration step first.";

    /// <summary>
    /// Consistency scans row (R2). ShowPrimary = View Findings (findings exist and the scan is current).
    /// </summary>
    public static SummaryRowContent ScansRow(ScanReport? report, bool outOfDate)
    {
        var row = new SummaryRowContent { Line2 = ScansLine2, Line3Tone = SummaryTone.Muted };

        if (report == null)
        {
            row.Line3 = "Not run yet. They run when the emails are generated.";
        }
        else if (outOfDate)
        {
            row.Line3 = "Last scan is older than the corpus. It runs again at the end of the next run.";
        }
        else if (report.Findings.Count == 0)
        {
            row.Line3 = $"No problems in {Plural(report.ScannedEmails, "scanned email", "scanned emails")}";
            row.Line3Tone = SummaryTone.Success;
        }
        else
        {
            var findings = report.Findings;
            int Count(ScanCheck check) => findings.Count(f => f.Check == check);

            // Zero clauses are dropped; Attachment findings get their own clause (amended R2)
            var clauses = new[]
                {
                    (Count(ScanCheck.Timeline), "timeline"),
                    (Count(ScanCheck.Value), "value"),
                    (Count(ScanCheck.StoryDetail), "story-detail"),
                    (Count(ScanCheck.Attachment), "attachment")
                }
                .Where(c => c.Item1 > 0)
                .Select(c => $"{N(c.Item1)} {c.Item2}");

            row.Line3 = $"{Plural(FlaggedEmails(findings), "flagged email", "flagged emails")} in {Plural(Threads(findings), "thread", "threads")}: {string.Join(", ", clauses)}";
            row.Line3Tone = SummaryTone.Warning;
            row.Line3Tooltip = row.Line3;
            row.ShowPrimary = true;
        }

        row.DotTone = row.Line3Tone;
        return row;
    }

    /// <summary>
    /// Distinct flagged emails (an email can carry several findings).
    /// </summary>
    public static int FlaggedEmails(IEnumerable<ScanFinding> findings) =>
        findings.Select(f => f.DocId).Distinct(StringComparer.Ordinal).Count();

    public static int Threads(IEnumerable<ScanFinding> findings) =>
        findings.Select(f => f.ThreadId).Distinct(StringComparer.Ordinal).Count();

    /// <summary>
    /// The leakage tooltip and report summary; the thresholds come from the constants the audit uses.
    /// </summary>
    public static string LeakageTooltip
    {
        get
        {
            var pass = Percent(LeakageAudit.PassMaxBalancedAccuracy);
            var warning = Percent(LeakageAudit.WarningMaxBalancedAccuracy);
            return $"Balanced accuracy of a simple model trained on metadata only, tested on threads it did not see ({LeakageAudit.Folds}-fold). Pass is {pass}% or lower, Warning is above {pass}% up to {warning}%, Fail is above {warning}%. Some signal is normal: real custodians and the dispute dates also predict relevance.";
        }
    }

    /// <summary>
    /// Leakage audit row (R3). ShowPrimary = View Report (once run).
    /// </summary>
    public static SummaryRowContent LeakageRow(LeakageReport? report)
    {
        var (line, tone) = LeakageLine(report);
        return new SummaryRowContent
        {
            Line2 = LeakageLine2,
            Line3 = line,
            Line3Tone = tone,
            DotTone = tone,
            Line3Tooltip = report == null ? "" : LeakageTooltip,
            ShowPrimary = report != null
        };
    }

    /// <summary>
    /// Verdict line (R3 table). The verdict is recomputed from the stored accuracy and counts with the audit's own rule.
    /// </summary>
    public static (string Text, SummaryTone Tone) LeakageLine(LeakageReport? report)
    {
        if (report == null || report.Verdict == LeakageVerdict.NotRun)
            return ("Not run yet.", SummaryTone.Muted);

        var verdict = LeakageAudit.VerdictFor(report.BalancedAccuracy, report.RelevantEmails, report.NonRelevantEmails);
        var ba = Percent(report.BalancedAccuracy);
        var top = report.Signals.Count > 0 ? $" Top signal: {SignalText(report.Signals[0])}." : "";
        return verdict switch
        {
            LeakageVerdict.Pass => ($"Pass: metadata alone predicts relevance {ba}% of the time (50% is a coin flip).", SummaryTone.Success),
            LeakageVerdict.Warning => ($"Warning: metadata alone predicts relevance {ba}% of the time.{top}", SummaryTone.Warning),
            LeakageVerdict.Fail => ($"Fail: metadata alone predicts relevance {ba}% of the time.{top}", SummaryTone.Danger),
            _ => ($"Not enough data: the audit needs {N(LeakageAudit.MinEmailsPerClass)} relevant and {N(LeakageAudit.MinEmailsPerClass)} non-relevant emails; this corpus has {N(report.RelevantEmails)} and {N(report.NonRelevantEmails)}.", SummaryTone.Muted)
        };
    }

    public static string SignalText(LeakageSignal signal)
    {
        ArgumentNullException.ThrowIfNull(signal);
        return signal.Kind switch
        {
            LeakageSignalKind.OnlyOnRelevant => $"{signal.Feature}: only on relevant mail",
            LeakageSignalKind.NeverOnRelevant => $"{signal.Feature}: never on relevant mail",
            _ => $"{signal.Feature}: {Percent(signal.RelevantShare)}% relevant vs {Percent(signal.BaseRate)}% overall"
        };
    }

    /// <summary>
    /// Leakage report grid cells (S3): Signal, Relevant share, Emails, Lift (signed percentage points over the base rate).
    /// </summary>
    public static (string Signal, string Share, string Emails, string Lift) SignalCells(LeakageSignal signal)
    {
        var points = (int)Math.Round(signal.Lift * 100, MidpointRounding.AwayFromZero);
        // U+2212 minus sign, as in the cost breakdown
        var lift = (points >= 0 ? "+" : "−") + Math.Abs(points).ToString(Invariant) + " pts";
        return (SignalText(signal), Percent(signal.RelevantShare) + "%", N(signal.Support), lift);
    }

    /// <summary>
    /// Blind review row (R4). ShowPrimary = View Findings (done with disagreements); InProgress = Accent line 3 and dot.
    /// </summary>
    public static QaRowContent BlindReviewRow(BlindReviewRowInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var row = new QaRowContent { Line2 = BlindReviewLine2, Line3Tone = SummaryTone.Muted };
        var emails = Plural(input.SampleSize, "email", "emails");
        var fallback = input.Fallback ? FallbackClause : "";

        switch (input.State)
        {
            case BlindReviewState.Running:
                row.Line3 = $"Reviewing: {N(input.Reviewed)} of {emails}...";
                row.InProgress = true;
                break;
            case BlindReviewState.Stopped:
                row.Line3 = $"{N(input.Reviewed)} of {emails} reviewed; the review stopped. Run Blind Review again to finish the rest.";
                row.Line3Tone = SummaryTone.Warning;
                break;
            case BlindReviewState.Done when input.Disagreements == 0:
                row.Line3 = $"{emails} reviewed by {input.Model}: all agree with the answer key{fallback}";
                row.Line3Tone = input.Fallback ? SummaryTone.Warning : SummaryTone.Success;
                break;
            case BlindReviewState.Done:
                var agree = input.SampleSize > 0
                    ? Math.Max(0, input.SampleSize - input.Disagreements) * 100.0 / input.SampleSize
                    : 100.0;
                row.Line3 = $"{emails} reviewed by {input.Model}: {Plural(input.Disagreements, "disagreement", "disagreements")} ({agree.ToString("0", Invariant)}% agree){fallback}";
                row.Line3Tone = SummaryTone.Warning;
                row.ShowPrimary = true;
                break;
            default:
                row.Line3 = $"Not run yet. ≈{Money(input.EstimateCost)} to review {emails} with {input.Model}.";
                break;
        }

        row.Line3Tooltip = input.Fallback && input.State == BlindReviewState.Done ? FallbackTooltip : "";
        row.DotTone = row.Line3Tone;
        return row;
    }

    // --- Findings dialog (S3) ---

    public const string NoFindings = "No findings.";
    public const string QueuedSuffix = " · queued";
    public const string OpenEmailText = "Open Email";
    public const string RegenerateThreadText = "Regenerate Thread...";
    public const string CloseText = "Close";

    public static (string Title, string Headline, string Summary) FindingsContent(QaFindingsMode mode, int flaggedEmails,
        int threads, string reviewerModel, LeakageReport? leakage)
    {
        switch (mode)
        {
            case QaFindingsMode.Leakage:
                return ("Leakage Audit Report", LeakageLine(leakage).Text, LeakageTooltip);
            case QaFindingsMode.BlindReview:
                return ("Blind Review Disagreements", "Blind review disagreements", flaggedEmails == 0
                    ? NoFindings
                    : $"{reviewerModel} disagreed with the answer key on {Plural(flaggedEmails, "email", "emails")}. Check each one: edit the answer key spreadsheet to relabel it, or regenerate its thread.");
            default:
                return ("Consistency Scan Findings", "Consistency scan findings", flaggedEmails == 0
                    ? NoFindings
                    : $"{Plural(flaggedEmails, "flagged email", "flagged emails")} in {Plural(threads, "thread", "threads")}. Each row names the check and what it found. Nothing in the corpus has changed.");
        }
    }

    public static string CheckText(ScanCheck check) => check switch
    {
        ScanCheck.Timeline => "Timeline",
        ScanCheck.Value => "Value",
        ScanCheck.StoryDetail => "Story detail",
        _ => "Attachment"
    };

    public static string LabelText(bool relevant) => relevant ? "Relevant" : "Not relevant";

    public static string DateText(DateTime value) => value.ToString("MMM d, yyyy", Invariant);

    public static string FooterQueued(int threads) =>
        $"{Plural(threads, "thread", "threads")} queued for regeneration. Go to Generate Corpus and click Continue Generating to write {(threads == 1 ? "it" : "them")}.";

    public static string FooterNotDeleted(int files, string reason)
    {
        var them = files == 1 ? "it" : "them";
        return $"{Plural(files, "EML file", "EML files")} could not be deleted because {TrimMessage(reason)}. Close any program that has {them} open and delete {them} by hand.";
    }

    /// <summary>
    /// The answer key could not be rewritten after the invalidation (ThreadInvalidationResult.KeyError; revision 2).
    /// </summary>
    public static string FooterKeyNotUpdated(string reason) =>
        $"The answer key could not be updated: {TrimMessage(reason)}. It is updated again when you click Continue Generating.";

    // --- Confirmations (MessageBox, default button = the safe choice) ---

    public static (string Title, string Text) CQ1RegenerateThread(string subject, int emlFiles, int attachments, bool relevant)
    {
        var files = Plural(emlFiles, "EML file", "EML files");
        var att = attachments > 0 ? " and " + Plural(attachments, "attachment", "attachments") : "";
        var verb = emlFiles == 1 && attachments == 0 ? "is" : "are";
        var keep = relevant ? ", keeping the story details it already established so later threads stay consistent" : "";
        return ("Regenerate Thread",
            $"Regenerate \"{subject}\"? Its {files}{att} {verb} deleted now and the answer key marks the thread Planned. Continue Generating on the Generate Corpus step writes it again{keep}.");
    }

    public static (string Title, string Text) CQ2CancelReview(int reviewed) =>
        ("Cancel Review",
            $"Stop the blind review? The {Plural(reviewed, "email", "emails")} already reviewed {(reviewed == 1 ? "is" : "are")} kept, and Run Blind Review finishes the rest later.");

    public static (string Title, string Text) CQ3LeaveDuringReview() =>
        ("Stop Blind Review?", "The blind review is still running. Leave and stop it? Reviewed emails are kept.");

    // --- Blind review dialog (S4) ---

    public const string BlindReviewTitle = "Blind Review";
    public const string BlindReviewHeadline = "Run a blind review";
    public const string SampleLabel = "Emails to review:";
    public const string StartReviewText = "Start Review";

    public const string FallbackSentence =
        "Only one provider key is tested, so the reviewer comes from the same provider that wrote the corpus. Test a key for the other provider on the API Configuration step for a more independent review.";

    public static string BlindReviewBody(string model, string provider) =>
        $"{model} from {provider} reads each sampled email the way a reviewer sees it, without the answer key, and says whether it is relevant and why. Disagreements are listed for you to check. Nothing in the corpus changes.";

    public static string SampleOfTotal(int total) => $"of {N(total)}";

    public static string DefaultRule(int relevant) =>
        $"Default: all {N(relevant)} relevant emails and the same number of non-relevant ones, near-miss decoys first.";

    /// <summary>
    /// Estimate line; with emails already reviewed (a stopped review) the resume clause states what is still paid for.
    /// </summary>
    public static string Estimate(decimal cost, int remaining, int reviewed)
    {
        var resume = reviewed > 0
            ? $" for the {Plural(remaining, "email", "emails")} not yet reviewed; {N(reviewed)} already reviewed {(reviewed == 1 ? "is" : "are")} kept"
            : "";
        return $"Estimated cost ≈{Money(cost)}, ±50%{resume}.";
    }

    // --- Status strip (R4) ---

    public static string BlindReviewProgress(int reviewed, int total) =>
        $"Blind review: {N(reviewed)} of {Plural(total, "email", "emails")} reviewed...";

    public static (string Text, SummaryTone Tone) BlindReviewFinished(int disagreements) =>
        ($"Blind review finished: {Plural(disagreements, "disagreement", "disagreements")}.",
            disagreements == 0 ? SummaryTone.Success : SummaryTone.Warning);

    public static string BlindReviewStopped(string message) =>
        $"Blind review stopped: {TrimMessage(message)}. Reviewed emails are kept; run it again to finish.";

    // --- Shared formatting ---

    private static string Plural(int n, string singular, string plural) => CaseGuideUiText.Plural(n, singular, plural);

    private static string N(int value) => value.ToString("N0", Invariant);

    // 0..1 share as a whole percentage ("60")
    private static string Percent(double share) => (share * 100).ToString("0", Invariant);

    // Same as GenerationUiText.Money: "$12.35" under $100, "$250" from $100 up
    private static string Money(decimal amount) =>
        amount >= 100m
            ? "$" + Math.Round(amount, 0, MidpointRounding.AwayFromZero).ToString("N0", Invariant)
            : "$" + Math.Round(amount, 2, MidpointRounding.AwayFromZero).ToString("0.00", Invariant);

    private static string TrimMessage(string? message) => (message ?? "").Trim().TrimEnd('.').TrimEnd();
}
