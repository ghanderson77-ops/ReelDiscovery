using System.Globalization;
using ReelDiscovery.Models.Evidence;

namespace ReelDiscovery.UserControls;

/// <summary>
/// The seven Generate Corpus tiles (06-UI-SPEC S1 Row 3). Distinct from the Phase 4 <see cref="TileKind"/>.
/// </summary>
public enum GenerationTileKind { Relevant, NonRelevant, Attachments, Emails, Warnings, Failed, Cost }

/// <summary>
/// Dot and text color of an activity row or status line, mapped to Theme tokens by the step.
/// </summary>
public enum ActivityTone
{
    Accent,
    Success,
    Warning,
    Danger
}

public class ActivityLine
{
    public string Text = "";

    // Full text plus every violation on its own line
    public string Tooltip = "";
    public ActivityTone Tone;
}

public class OutcomeLine
{
    public string Text = "";
    public ActivityTone Tone;
}

/// <summary>
/// What entering Generate Corpus shows (05-UI-SPEC S1 interaction rules 1-7).
/// </summary>
public enum GenerationEntry
{
    KeepRun,
    GuideChangedCard,
    NotApprovedCard,
    PlanChangedCard,
    UnreadableCard,
    ReadyCard,
    PartialDashboard,
    FinishedDashboard
}

public class GenerationEntryInput
{
    public bool Running;
    public bool HasPlan;
    public bool Approved;
    public bool HasSavedRun;

    // The guide changed since the plan was built and was not kept (CorpusApproval.IsGuideStale, before withdrawal)
    public bool GuideStale;

    // The saved run's plan identity differs from the current plan
    public bool PlanChanged;

    // Non-empty when the saved run could not be read
    public string UnreadableReason = "";
    public int Total;

    // Includes kept-with-warnings
    public int Written;
    public int Failed;

    public int AttachmentsTotal;

    // Attachments with a final record: Written + Warnings + Failed + Skipped (Skipped media alone never makes a run unfinished)
    public int AttachmentsFinal;

    // True only when the newest RunStarted has a RunEnded with outcome Finished after it (JournalReplay.LatestAttemptEnded
    // plus the outcome): an older RunEnded never hides an interrupted Continue, and a cancelled or stopped run did not
    // finish (06-UI-SPEC Row 2, review MEDIUM 06-13)
    public bool LatestAttemptEnded;
}

public enum GenerationStateCard
{
    Ready,
    NotApproved,
    PlanChanged,
    Unreadable,
    GuideChanged
}

public class GenerationCardContent
{
    public string Heading = "";
    public string Body = "";
    public string ButtonText = "";

    // Danger button (Start Over...) instead of Primary
    public bool Danger;

    // Ready card only: the Small estimate line and the Warning media notice (empty when not shown)
    public string EstimateLine = "";
    public string MediaNotice = "";

    // Ready card only (07 S1): the estimate line's per-model tooltip and the batch hint under the checkbox (empty = hidden)
    public string EstimateTooltip = "";
    public string BatchHint = "";
    public SummaryTone BatchHintTone;
}

/// <summary>
/// What the Ready card states (06-UI-SPEC S1 Row 4): plan counts and the Story / Volume / media estimate (D-29).
/// </summary>
public class GenerationReadyInput
{
    public int RelevantThreads;
    public int NonRelevantThreads;
    public int Emails;
    public int Waves;
    public int Attachments;
    public decimal StoryCost;
    public decimal VolumeCost;
    public decimal MediaCost;
    public string StoryModel = "";
    public string VolumeModel = "";

    // Image + voicemail attachments in the plan
    public int MediaPlanned;

    // A tested OpenAI key is set up this session (PROV-06)
    public bool MediaAvailable;

    // D-17: both totals from the one GenerationCostEstimator.Estimate call; the batch costs assume no cache reads
    public decimal LiveTotal;
    public decimal BatchStoryCost;
    public decimal BatchVolumeCost;
    public decimal BatchTotal;

    // D-08: the case's saved batch choice (drives the hint variant)
    public bool BatchChecked;
}

/// <summary>
/// Media banner copy (06-UI-SPEC S1 Row 2, Decision 2); an empty ButtonText means no button.
/// </summary>
public class MediaBannerContent
{
    public string Heading = "";
    public string Body = "";
    public string ButtonText = "";
}

/// <summary>
/// Line-3 and status-dot tone of a Case Summary row, mapped to Theme tokens (TextMuted/BorderStrong, Success, Warning, Danger).
/// </summary>
public enum SummaryTone
{
    Muted,
    Success,
    Warning,
    Danger
}

public class SummaryRowContent
{
    public string Line2 = "";
    public string Line3 = "";
    public SummaryTone Line3Tone;
    public SummaryTone DotTone;

    // Full line-3 breakdown (line 3 itself ellipsizes); empty when there is none
    public string Line3Tooltip = "";

    // Mailboxes: Open Mailboxes Folder / Generate Emails or Generate Corpus (SecondaryText). Story so far: Open in Word / Export Story So Far
    public bool ShowPrimary;
    public bool ShowSecondary;

    // Caption of the secondary button when the row sets it; empty keeps the button's own caption
    public string SecondaryText = "";
}

public class MailboxesRowInput
{
    public bool Generated;
    public bool EarlierPlan;

    // Thread "Written" counts include kept-with-warnings
    public int RelevantTotal;
    public int RelevantWritten;
    public int NonRelevantTotal;
    public int NonRelevantWritten;

    // Attachments written include kept-with-warnings; Skipped is separate
    public int AttachmentsTotal;
    public int AttachmentsWritten;
    public int AttachmentsSkipped;

    // Item counts: threads + attachments
    public int ItemWarnings;
    public int ItemFailed;
    public int Files;
    public int Mailboxes;
    public bool FolderExists;
}

public class StoryRowInput
{
    public int Entries;
    public int Threads;
    public string FileName = "";
    public bool FileExists;
    public DateTime? WrittenLocal;

    // Non-empty when the last write failed
    public string Error = "";
}

/// <summary>
/// All Generate Corpus and Case Summary copy with counts, dates or conditions (05- and 06-UI-SPEC), pure and
/// unit-tested so the generation step stays layout-only (03-06 precedent). Every string is plain text and may carry
/// model-written subjects or violation texts; labels showing it set UseMnemonic = false (T-04-33, T-05-15).
/// </summary>
public static class GenerationUiText
{
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;
    private static readonly CultureInfo EnUs = CultureInfo.GetCultureInfo("en-US");

    public const string StoppingText = "Stopping... finished threads and attachments are saved.";

    // --- Notice line ---

    /// <summary>
    /// What the step writes, in run order (06-UI-SPEC S1 Row 1). A zero non-relevant count drops the "then" part and a
    /// zero attachment count drops the "with" clause.
    /// </summary>
    public static string ScopeNotice(int relevantThreads, int nonRelevantThreads, int attachments)
    {
        var relevant = Plural(relevantThreads, "relevant thread", "relevant threads");
        var order = nonRelevantThreads > 0
            ? $"{relevant} first, oldest first, then {Plural(nonRelevantThreads, "non-relevant thread", "non-relevant threads")}"
            : $"{relevant}, oldest first";
        return $"This step writes the approved plan as email: {order}{WithAttachments(attachments)}. Each answer-key row moves from Planned to Generated as its file is saved.";
    }

    // --- Tiles ---

    /// <summary>
    /// Tile copy (06-UI-SPEC S1 Row 3). For the Relevant, Non-relevant and Attachments tiles, variant 1 drops the spaces
    /// around "/" and variant 2 moves the total to the sub-line (DPI rule 19, applied to each tile independently); the
    /// other tiles ignore it. Warnings and Failed count items (threads + attachments); Skipped is never Failed.
    /// </summary>
    public static TileContent Tile(GenerationTileKind kind, GenerationCounts c, int variant = 0)
    {
        ArgumentNullException.ThrowIfNull(c);
        switch (kind)
        {
            case GenerationTileKind.Relevant:
                var waves = Plural(c.WaveCount, "wave", "waves");
                return Fraction("Relevant", c.RelevantDone, c.RelevantTotal, waves, variant,
                    $"{N(c.RelevantDone)} of {Plural(c.RelevantTotal, "relevant thread", "relevant threads")} written, in {waves} by date. The narrative ledger holds {Plural(c.LedgerEntries, "story detail", "story details")}.");

            case GenerationTileKind.NonRelevant:
                // Zero categories are dropped; "near-miss decoys" is the plan's own term (Copywriting Contract)
                var categories = Join(", ",
                    c.NonRelevantDailyBusiness > 0 ? $"{N(c.NonRelevantDailyBusiness)} daily business" : "",
                    c.NonRelevantAncillary > 0 ? $"{N(c.NonRelevantAncillary)} ancillary" : "",
                    c.NonRelevantBulk > 0 ? $"{N(c.NonRelevantBulk)} newsletters, spam and personal" : "",
                    c.NonRelevantDecoy > 0 ? Plural(c.NonRelevantDecoy, "near-miss decoy", "near-miss decoys") : "");
                return Fraction("Non-relevant", c.NonRelevantDone, c.NonRelevantTotal, "threads", variant,
                    $"{N(c.NonRelevantDone)} of {Plural(c.NonRelevantTotal, "non-relevant thread", "non-relevant threads")} written{Colon(categories)}.");

            case GenerationTileKind.Attachments:
                var kinds = Join(", ",
                    c.AttachmentsWord > 0 ? $"{N(c.AttachmentsWord)} Word" : "",
                    c.AttachmentsExcel > 0 ? $"{N(c.AttachmentsExcel)} Excel" : "",
                    c.AttachmentsPowerPoint > 0 ? $"{N(c.AttachmentsPowerPoint)} PowerPoint" : "",
                    c.AttachmentsInvite > 0 ? Plural(c.AttachmentsInvite, "invite", "invites") : "",
                    c.AttachmentsImage > 0 ? Plural(c.AttachmentsImage, "image", "images") : "",
                    c.AttachmentsVoicemail > 0 ? Plural(c.AttachmentsVoicemail, "voicemail", "voicemails") : "");
                var skipped = c.AttachmentsSkipped > 0
                    ? $" {Plural(c.AttachmentsSkipped, "item", "items")} skipped because images and voicemail were unavailable."
                    : "";
                var subLine = c.AttachmentsTotal == 0 ? "none planned"
                    : c.AttachmentsSkipped > 0 ? $"{N(c.AttachmentsSkipped)} skipped"
                    : "files and media";
                return Fraction("Attachments", c.AttachmentsDone, c.AttachmentsTotal, subLine, variant,
                    $"{N(c.AttachmentsDone)} of {Plural(c.AttachmentsTotal, "attachment", "attachments")} written{Colon(kinds)}.{skipped}");

            case GenerationTileKind.Emails:
                return new TileContent
                {
                    Caption = "Emails",
                    Value = N(c.EmailsWritten),
                    SubLine = Plural(c.FilesWritten, "EML file", "EML files"),
                    Tooltip = $"{N(c.EmailsWritten)} of {N(c.EmailsPlanned)} planned emails written, saved as {Plural(c.FilesWritten, ".eml file", ".eml files")} in {Plural(c.Mailboxes, "custodian mailbox", "custodian mailboxes")} under the Mailboxes folder."
                };

            case GenerationTileKind.Warnings:
                var warnings = c.WarningThreads + c.AttachmentsWarnings;
                return new TileContent
                {
                    Caption = "Warnings",
                    Value = N(warnings),
                    SubLine = "items kept",
                    Tooltip = $"{ItemSubject(c.WarningThreads, c.AttachmentsWarnings, "that ")}still broke a consistency check after 3 attempts. The best attempt was kept and marked \"Generated - with warnings\" in the answer key.",
                    Tone = warnings > 0 ? TileTone.Warning : TileTone.Normal
                };

            case GenerationTileKind.Failed:
                var failed = c.FailedThreads + c.AttachmentsFailed;
                return new TileContent
                {
                    Caption = "Failed",
                    Value = N(failed),
                    SubLine = "items not written",
                    Tooltip = $"{ItemSubject(c.FailedThreads, c.AttachmentsFailed, "that ")}could not be written. An email whose attachment failed is saved without it. Retry them with Continue Generating or {RetryFailedButtonText}.",
                    Tone = failed > 0 ? TileTone.Danger : TileTone.Normal
                };

            default:
                // CostUsd already includes the media share (06-02)
                var media = c.MediaPlanned > 0
                    ? $" (text ${(c.CostUsd - c.MediaCostUsd).ToString("0.00", Invariant)}, images and voicemail ${c.MediaCostUsd.ToString("0.00", Invariant)})"
                    : "";
                return new TileContent
                {
                    Caption = "Cost",
                    Value = Money(c.CostUsd),
                    SubLine = c.Finished ? "this case" : $"≈{Money(c.RemainingCostUsd)} to go",
                    Tooltip = $"Spent on generation in this case: ${c.CostUsd.ToString("0.00", Invariant)}{media}. Estimate for the remaining items: ≈{Money(c.RemainingCostUsd)}, ±50%{(c.BatchOn ? " at batch prices" : "")}. The cost ticker in the header shows the whole session."
                        + (c.BatchSavingsUsd > 0m ? $" Batch pricing saved ${c.BatchSavingsUsd.ToString("0.00", Invariant)}." : "")
                };
        }
    }

    // A "done / total" tile with the rule-19 fallback variants
    private static TileContent Fraction(string caption, int done, int total, string subLine, int variant, string tooltip) => new()
    {
        Caption = caption,
        Value = variant switch
        {
            1 => $"{N(done)}/{N(total)}",
            2 => N(done),
            _ => $"{N(done)} / {N(total)}"
        },
        SubLine = variant == 2 ? $"of {N(total)}" : subLine,
        Tooltip = tooltip
    };

    // "2 threads and 3 attachments " with a zero clause dropped together with its "and"; both zero gives the generic
    // "Threads and attachments {relative}" so the tooltip still explains the tile
    private static string ItemSubject(int threads, int attachments, string relative)
    {
        if (threads == 0 && attachments == 0)
            return "Threads and attachments " + relative;
        return Join(" and ",
            threads > 0 ? Plural(threads, "thread", "threads") : "",
            attachments > 0 ? Plural(attachments, "attachment", "attachments") : "") + " ";
    }

    // --- Waves and stage rows ---

    /// <summary>
    /// "Mar 4 – Mar 17, 2024", "Dec 28, 2023 – Jan 10, 2024" across years, "Mar 4, 2024" for one day.
    /// </summary>
    public static string WaveDates(DateOnly start, DateOnly end)
    {
        if (start == end)
            return start.ToString("MMM d, yyyy", EnUs);
        if (start.Year == end.Year)
            return $"{start.ToString("MMM d", EnUs)} – {end.ToString("MMM d, yyyy", EnUs)}";
        return $"{start.ToString("MMM d, yyyy", EnUs)} – {end.ToString("MMM d, yyyy", EnUs)}";
    }

    /// <summary>
    /// "{t} threads, {m} emails, {a} attachments"; the attachment clause is dropped at 0.
    /// </summary>
    public static string WaveTooltip(int threads, int emails, int attachments = 0) =>
        $"{Plural(threads, "thread", "threads")}, {Plural(emails, "email", "emails")}"
        + (attachments > 0 ? ", " + Plural(attachments, "attachment", "attachments") : "");

    /// <summary>
    /// Dates, Done and tooltip cells of a Stages-grid row (06-UI-SPEC S1 Row 4). Counting rule (review 06-23): a wave or
    /// the Non-relevant row counts the attachments of its threads that were still pending at run start; the Attachments
    /// for written threads row counts only repairs of threads written earlier, so its Done cell is repairs done / repairs.
    /// The tiles count every attachment once, so a repair never appears on both a wave row and the repair row.
    /// </summary>
    public static (string Dates, string Done, string Tooltip) StageRowText(WaveProgress row)
    {
        ArgumentNullException.ThrowIfNull(row);
        switch (row.RowKind)
        {
            case StageRowKind.NonRelevant:
                return ("Non-relevant mail", $"{N(row.Done)} / {N(row.Threads)}",
                    WaveTooltip(row.Threads, row.Emails, row.Attachments) + ", across all dates");

            case StageRowKind.Attachments:
                return ("Attachments for written threads", $"{N(row.AttachmentsDone)} / {N(row.Attachments)}",
                    $"{Plural(row.Attachments, "attachment", "attachments")} on {Plural(row.Threads, "thread", "threads")} that were written earlier. Their emails are rewritten in every mailbox.");

            default:
                var dates = WaveDates(row.Start, row.End);
                // Full dates first: the Dates column may trim them at the minimum window size
                return (dates, $"{N(row.Done)} / {N(row.Threads)}",
                    dates + Environment.NewLine + WaveTooltip(row.Threads, row.Emails, row.Attachments));
        }
    }

    /// <summary>
    /// Dates-cell text of a stage row waiting on provider batches (07-UI-SPEC S1 Row 4, D-09); "" when not waiting.
    /// The submission time is local, with the date when it is not today.
    /// </summary>
    public static string BatchWaitText(WaveProgress row, DateTime nowLocal)
    {
        ArgumentNullException.ThrowIfNull(row);
        if (!row.BatchWaiting)
            return "";

        var submitted = row.BatchFirstSubmittedUtc.HasValue ? SubmittedText(row.BatchFirstSubmittedUtc.Value, nowLocal) : "";
        var progress = $"{N(row.BatchDone)} of {Plural(row.BatchTotal, "request", "requests")} done";
        if (row.BatchCount > 1)
        {
            var when = submitted.Length > 0 ? $", first submitted {submitted}" : "";
            return $"Waiting on {Plural(row.BatchCount, "provider batch", "provider batches")}{when}, {progress}";
        }

        return $"Waiting on provider batch{(submitted.Length > 0 ? ", submitted " + submitted : "")}, {progress}";
    }

    /// <summary>
    /// Compact Dates-cell form of <see cref="BatchWaitText"/> (07-UI-SPEC S1 Row 4, amended in execution 07-11 for DPI
    /// rule 27): "Batch: 6 of 14 done", "2 batches: 6 of 14 done" when several wait; "" when not waiting. The submission
    /// time and the full sentence stay in the row tooltip (and the status strip), because the time does not fit the
    /// Dates column at Large / 144 DPI next to a 10,000-request count.
    /// </summary>
    public static string BatchWaitCellText(WaveProgress row)
    {
        ArgumentNullException.ThrowIfNull(row);
        if (!row.BatchWaiting)
            return "";

        var subject = row.BatchCount > 1 ? Plural(row.BatchCount, "batch", "batches") : "Batch";
        return $"{subject}: {N(row.BatchDone)} of {N(row.BatchTotal)} done";
    }

    /// <summary>
    /// Row tooltip while waiting: "{dates or stage name}. {full wait text}. Next check in {s} s."
    /// </summary>
    public static string BatchWaitTooltip(string datesOrStage, string waitText, int secondsToNextCheck) =>
        $"{TrimMessage(datesOrStage)}. {TrimMessage(waitText)}. Next check in {Math.Max(0, secondsToNextCheck).ToString(Invariant)} s.";

    /// <summary>
    /// Whole seconds until the decorator checks the provider again, never negative (the 1 s UI timer counts it down).
    /// </summary>
    public static int SecondsUntil(DateTime? nextCheckUtc, DateTime nowUtc) =>
        nextCheckUtc.HasValue ? Math.Max(0, (int)Math.Ceiling((nextCheckUtc.Value - nowUtc).TotalSeconds)) : 0;

    // "2:14 PM" today, "Oct 5, 2:14 PM" on another day (local time)
    private static string SubmittedText(DateTime submittedUtc, DateTime nowLocal)
    {
        var local = submittedUtc.Kind == DateTimeKind.Local
            ? submittedUtc
            : DateTime.SpecifyKind(submittedUtc, DateTimeKind.Utc).ToLocalTime();
        return local.Date == nowLocal.Date ? local.ToString("h:mm tt", EnUs) : local.ToString("MMM d, h:mm tt", EnUs);
    }

    // "42 min" or "2 h 15 min" (at least 1 min)
    private static string DurationText(TimeSpan duration)
    {
        var minutes = Math.Max(1, (int)Math.Floor(duration.TotalMinutes));
        return minutes < 60 ? $"{minutes} min" : $"{minutes / 60} h {minutes % 60} min";
    }

    // "Anthropic" / "OpenAI" (LlmProviderKind names); empty falls back to "the provider"
    private static string ProviderName(string? provider) =>
        string.IsNullOrWhiteSpace(provider) ? "the provider" : provider.Trim();

    // --- Activity ---

    public static ActivityLine ActivityText(GenerationEvent e)
    {
        ArgumentNullException.ThrowIfNull(e);
        var violations = e.Violations ?? new List<string>();
        var first = violations.Count > 0 ? TrimMessage(violations[0]) : "";
        var file = (e.FileName ?? "").Trim();
        var attachmentsClause = e.Attachments > 0 ? ", " + Plural(e.Attachments, "attachment", "attachments") : "";
        var andAttachments = e.Attachments > 0 ? " and " + Plural(e.Attachments, "attachment", "attachments") : "";

        var (text, tone) = e.Kind switch
        {
            GenerationEventKind.RunStarted =>
                ($"Started: {Plural(e.Threads, "thread", "threads")}{andAttachments} to write", ActivityTone.Accent),
            GenerationEventKind.RunContinued =>
                ($"Continued: {Plural(e.Threads, "thread", "threads")}{andAttachments} left to write", ActivityTone.Accent),
            GenerationEventKind.WaveStarted =>
                ($"Wave {e.WaveIndex} of {e.WaveCount} started ({WaveDates(e.WaveStart, e.WaveEnd)}, {Plural(e.Threads, "thread", "threads")})", ActivityTone.Accent),
            // Non-relevant threads have no ledger clause: only relevant mail feeds the narrative ledger
            GenerationEventKind.ThreadWritten => (e.Relevant
                    ? $"Written: \"{e.Subject}\", {Plural(e.Emails, "email", "emails")}, {Plural(e.Details, "new ledger detail", "new ledger details")}{attachmentsClause}"
                    : $"Written: \"{e.Subject}\", {Plural(e.Emails, "email", "emails")}{attachmentsClause}",
                ActivityTone.Success),
            GenerationEventKind.Retry =>
                ($"Attempt {e.Attempt} of 3 failed {Plural(violations.Count, "check", "checks")}{Colon(first)}. Retrying.", ActivityTone.Warning),
            GenerationEventKind.KeptWithWarnings =>
                ($"Kept with {Plural(violations.Count, "warning", "warnings")} after 3 attempts{Colon(first)}", ActivityTone.Warning),
            GenerationEventKind.ThreadFailed =>
                ($"Failed{Colon(e.Reason)}. The run continues.", ActivityTone.Danger),
            GenerationEventKind.NotSaved =>
                ($"Not saved{Colon(e.Reason)}. It stays pending for the next Continue.", ActivityTone.Danger),
            GenerationEventKind.WaveFinished =>
                ($"Wave {e.WaveIndex} finished: {N(e.Written)} written, {N(e.Warnings)} with warnings, {N(e.Failed)} failed. Ledger now {N(e.LedgerTotal)} details.",
                    StageTone(e)),
            GenerationEventKind.SideProjectsReady =>
                ($"Side projects ready: {Plural(e.Projects, "project", "projects")} for {Plural(e.Organizations, "organization", "organizations")}", ActivityTone.Success),
            GenerationEventKind.NonRelevantStarted =>
                ($"Non-relevant mail started: {Plural(e.Threads, "thread", "threads")} to write", ActivityTone.Accent),
            GenerationEventKind.NonRelevantFinished =>
                ($"Non-relevant mail finished: {N(e.Written)} written, {N(e.Warnings)} with warnings, {N(e.Failed)} failed.", StageTone(e)),
            GenerationEventKind.AttachmentRetry =>
                ($"{file}: attempt {e.Attempt} of 3 failed {Plural(violations.Count, "check", "checks")}{Colon(first)}. Retrying.", ActivityTone.Warning),
            GenerationEventKind.AttachmentKeptWithWarnings =>
                ($"{file} kept with {Plural(violations.Count, "warning", "warnings")} after 3 attempts{Colon(first)}", ActivityTone.Warning),
            GenerationEventKind.AttachmentFailed =>
                ($"{file} failed{Colon(e.Reason)}. The email was saved without it.", ActivityTone.Danger),
            GenerationEventKind.AttachmentSkipped =>
                ($"{file} skipped{Colon(e.Reason)}.", ActivityTone.Warning),
            // A media stop is Warning, not Danger: text continues and Skipped is resumable (UI-SPEC Decision 2)
            GenerationEventKind.MediaStopped =>
                ((TrimMessage(e.Reason).Length == 0 ? "Images and voicemail stopped" : $"Images and voicemail stopped: {TrimMessage(e.Reason)}")
                    + $". Text generation continues; the remaining {Plural(e.MediaRemaining, "image or voicemail", "images and voicemails")} {(e.MediaRemaining == 1 ? "is" : "are")} marked Skipped.",
                    ActivityTone.Warning),
            GenerationEventKind.AttachmentRepairStarted =>
                ($"Adding {Plural(e.Attachments, "missing attachment", "missing attachments")} to {Plural(e.Threads, "written thread", "written threads")}", ActivityTone.Accent),
            GenerationEventKind.AttachmentsAdded =>
                ($"Added {Plural(e.Attachments, "attachment", "attachments")} to \"{e.Subject}\" and rewrote {Plural(e.Files, "EML file", "EML files")}", ActivityTone.Success),
            GenerationEventKind.AttachmentRegenerated =>
                ($"{file} could not be reused and was created again", ActivityTone.Warning),
            GenerationEventKind.RunCancelled =>
                ($"Cancelled. {N(e.Done)} of {Plural(e.Total, "thread", "threads")}{AttachmentProgress(e.AttachmentsDone, e.AttachmentsTotal)} are saved.", ActivityTone.Warning),
            GenerationEventKind.RunStopped =>
                (string.IsNullOrWhiteSpace(e.Reason) ? "Stopped" : $"Stopped: {e.Reason.Trim()}", ActivityTone.Danger),
            // D-09 batch events (07-UI-SPEC S1 Row 4); provider and reason are fixed plain words from the decorator (T-07-52)
            GenerationEventKind.BatchSubmitted =>
                ($"Batch submitted: {Plural(e.BatchRequests, "request", "requests")} to {ProviderName(e.BatchProvider)}"
                    + (TrimMessage(e.BatchStage).Length > 0 ? " for " + TrimMessage(e.BatchStage) : ""), ActivityTone.Accent),
            GenerationEventKind.BatchFinished =>
                ($"Batch finished: {Plural(e.BatchRequests, "result", "results")} collected after {DurationText(e.BatchDuration)}", ActivityTone.Success),
            GenerationEventKind.BatchFinishedWithFailures =>
                ($"Batch finished: {N(e.BatchSucceeded)} succeeded, {N(e.BatchFailed)} failed or expired. The {Plural(e.BatchFailed, "request", "requests")} run live at full price.",
                    ActivityTone.Warning),
            GenerationEventKind.BatchFailedAtProvider =>
                ($"Batch failed at {ProviderName(e.BatchProvider)}{Colon(e.BatchReason)}. Its {Plural(e.BatchRequests, "request", "requests")} run live at full price.",
                    ActivityTone.Warning),
            GenerationEventKind.BatchCollectedAfterRestart =>
                ($"Collected {Plural(e.BatchRequests, "result", "results")} from a batch submitted"
                    + (e.BatchSubmittedUtc.HasValue ? " at " + SubmittedText(e.BatchSubmittedUtc.Value, e.LocalTime) : "")
                    + " before the app closed", ActivityTone.Success),
            // 10 D-13: other-provider batches resolved at run start
            GenerationEventKind.OtherProviderBatchesCollected =>
                ($"Collected {Plural(e.Total, $"{ProviderName(e.BatchProvider)} batch", $"{ProviderName(e.BatchProvider)} batches")} from an earlier session. {(e.Total == 1 ? "Its" : "Their")} results are kept as batch results not used.", ActivityTone.Success),
            GenerationEventKind.OtherProviderBatchesCancelled =>
                ($"Cancelled {Plural(e.Total, $"{ProviderName(e.BatchProvider)} batch", $"{ProviderName(e.BatchProvider)} batches")} from an earlier session. {(e.Total == 1 ? "Its" : "Their")} requests run once in this run.", ActivityTone.Warning),
            _ =>
                ("Finished. Answer key and story so far updated.", ActivityTone.Success)
        };

        var tooltip = text;
        foreach (var violation in violations)
            tooltip += Environment.NewLine + violation;

        return new ActivityLine { Text = text, Tooltip = tooltip, Tone = tone };
    }

    // Wave and non-relevant stage finish rows: any failure Danger, else any warning Warning, else Success
    private static ActivityTone StageTone(GenerationEvent e) =>
        e.Failed > 0 ? ActivityTone.Danger : e.Warnings > 0 ? ActivityTone.Warning : ActivityTone.Success;

    // " and {d} of {t} attachments", or "" when there are no attachments
    private static string AttachmentProgress(int done, int total) =>
        total > 0 ? $" and {N(done)} of {Plural(total, "attachment", "attachments")}" : "";

    // --- Status strip ---

    public static string ProgressText(GenerationView v)
    {
        ArgumentNullException.ThrowIfNull(v);
        var counts = v.Counts ?? new GenerationCounts();
        switch (v.Stage)
        {
            case GenerationStage.Writing:
                var attachments = v.StageAttachments > 0 ? " and " + Plural(v.StageAttachments, "attachment", "attachments") : "";
                return $"Wave {v.StageWave} of {v.StageWaveCount} ({WaveDates(v.StageWaveStart, v.StageWaveEnd)}): writing {Plural(v.StageCount, "thread", "threads")}{attachments}...";
            case GenerationStage.Merging:
                return $"Wave {v.StageWave} of {v.StageWaveCount}: merging {Plural(v.StageCount, "new ledger detail", "new ledger details")}...";
            case GenerationStage.SideProjects:
                return $"Preparing side projects for {Plural(v.StageCount, "organization", "organizations")}...";
            case GenerationStage.NonRelevant:
                return $"Non-relevant mail: {N(counts.NonRelevantDone)} of {Plural(counts.NonRelevantTotal, "thread", "threads")} written...";
            case GenerationStage.Attachments:
                // Repairs only (the Attachments for written threads row); Written, Warnings, Failed and Skipped all count as finished
                var repair = (v.Waves ?? new List<WaveProgress>()).FirstOrDefault(w => w != null && w.RowKind == StageRowKind.Attachments);
                var finished = repair == null ? 0 : repair.AttachmentsDone + repair.AttachmentsFailed + repair.AttachmentsSkipped;
                var total = repair?.Attachments ?? v.StageCount;
                return $"Adding missing attachments: {N(finished)} of {Plural(total, "attachment", "attachments")}...";
            case GenerationStage.UpdatingKey:
                return "Updating the answer key...";
            case GenerationStage.WritingStory:
                return "Writing the story so far...";
            case GenerationStage.Stopping:
                return StoppingText;
            default:
                return "";
        }
    }

    public const string CollectingAfterRestartText = "Collecting batches submitted before the app closed...";

    /// <summary>
    /// Status strip with the D-09 batch texts (07-UI-SPEC S1 Row 5): Stopping first, then collecting after a restart,
    /// then the lowest waiting stage row with its countdown, else the normal stage text.
    /// </summary>
    public static string ProgressText(GenerationView v, DateTime nowUtc)
    {
        ArgumentNullException.ThrowIfNull(v);
        if (v.Stage == GenerationStage.Stopping)
            return StoppingText;
        if (v.CollectingAfterRestart)
            return CollectingAfterRestartText;

        var waiting = (v.Waves ?? new List<WaveProgress>())
            .Where(w => w != null && w.BatchWaiting)
            .OrderBy(w => w.Index)
            .FirstOrDefault();
        if (waiting == null)
            return ProgressText(v);

        var waveCount = v.StageWaveCount > 0 ? v.StageWaveCount : (v.Counts?.WaveCount ?? 0);
        return ProgressBatchText(waiting, waveCount, SecondsUntil(waiting.BatchNextCheckUtc, nowUtc));
    }

    /// <summary>
    /// "Wave {i} of {n} ({dates}): waiting on provider batch, ..." or "Non-relevant mail: waiting on provider batch, ...".
    /// </summary>
    public static string ProgressBatchText(WaveProgress row, int waveCount, int secondsToNextCheck)
    {
        ArgumentNullException.ThrowIfNull(row);
        var stage = row.RowKind switch
        {
            StageRowKind.NonRelevant => "Non-relevant mail",
            StageRowKind.Attachments => "Attachments for written threads",
            _ => $"Wave {row.Index} of {waveCount} ({WaveDates(row.Start, row.End)})"
        };
        return $"{stage}: waiting on provider batch, {N(row.BatchDone)} of {Plural(row.BatchTotal, "request", "requests")} done. Next check in {Math.Max(0, secondsToNextCheck).ToString(Invariant)} s.";
    }

    /// <summary>
    /// Outcome status line (06-UI-SPEC S1 outcome table). <paramref name="retryButtonShown"/> is false when the media
    /// banner shows its own button, which hides the header's Retry Failed Items (one Primary rule).
    /// </summary>
    public static OutcomeLine OutcomeText(GenerationOutcome outcome, GenerationCounts c, string message, bool retryButtonShown = true)
    {
        ArgumentNullException.ThrowIfNull(c);
        switch (outcome)
        {
            case GenerationOutcome.Finished:
                // Items: threads + attachments; Skipped media is its own clause, never a failure
                var warnings = c.WarningThreads + c.AttachmentsWarnings;
                var failed = c.FailedThreads + c.AttachmentsFailed;
                var skipped = c.AttachmentsSkipped;
                if (warnings == 0 && failed == 0 && skipped == 0)
                {
                    return new OutcomeLine
                    {
                        Text = $"Done: {Plural(c.DoneThreads, "thread", "threads")} written as {Plural(c.EmailsWritten, "email", "emails")} ({Plural(c.FilesWritten, "EML file", "EML files")} in {Plural(c.Mailboxes, "mailbox", "mailboxes")}){WithAttachments(c.AttachmentsDone)}. The answer key and story so far are updated.",
                        Tone = ActivityTone.Success
                    };
                }

                // Zero clauses are dropped; the Retry instruction only appears when something failed and the button shows
                var clauses = new List<string>();
                if (warnings > 0)
                    clauses.Add(Plural(warnings, "warning", "warnings"));
                if (failed > 0)
                    clauses.Add(Plural(failed, "failed item", "failed items"));
                if (skipped > 0)
                    clauses.Add(Plural(skipped, "skipped image or voicemail", "skipped images and voicemails"));
                var tail = failed > 0 && retryButtonShown
                    ? $"; click {RetryFailedButtonText} to try the failed ones again"
                    : "";
                return new OutcomeLine { Text = $"Done with {JoinClauses(clauses)}. The answer key marks them{tail}.", Tone = ActivityTone.Warning };

            case GenerationOutcome.Cancelled:
                return new OutcomeLine
                {
                    Text = $"Cancelled. {N(c.DoneThreads)} of {Plural(c.TotalThreads, "thread", "threads")}{AttachmentProgress(c.AttachmentsDone, c.AttachmentsTotal)} are saved. Click Continue Generating to write the rest.",
                    Tone = ActivityTone.Warning
                };

            case GenerationOutcome.Fatal:
                return new OutcomeLine
                {
                    Text = $"Generation stopped{Colon(message)}. Finished threads and attachments are saved. Fix the problem on the API Configuration step, then click Continue Generating.",
                    Tone = ActivityTone.Danger
                };

            case GenerationOutcome.InternalError:
                // A code defect, not a key or quota problem: never send the user to API Configuration
                return new OutcomeLine
                {
                    Text = $"Generation stopped because of an internal error: {TrimMessage(message)}. Completed threads are saved. Click Continue Generating to try the remaining threads.",
                    Tone = ActivityTone.Danger
                };

            default:
                return new OutcomeLine { Text = "", Tone = ActivityTone.Accent };
        }
    }

    public static string AnswerKeyLockedText(string fileName) =>
        $"The answer key was not updated because {fileName} is open. Close it; the key is updated again when you leave this step.";

    /// <summary>
    /// 10 D-10: a key file that could not be parsed was copied aside before the new one was written (a notice, not KeyError).
    /// </summary>
    public static string AnswerKeyBackupText(string fileName, string backupFileName) =>
        $"{fileName} could not be read, so it was kept as \"{backupFileName}\" and a new answer key was written. Copy any relabels from the backup into the Relevance column.";

    public static string FileWriteFailedText(string fileName, string reason) =>
        $"Could not update {fileName}{Colon(reason)}. Your emails are saved; the file is written again when you leave this step.";

    // Start Over could not reach a provider to cancel its batches (best effort, T-07-53)
    public const string BatchCancelFailedText =
        "Pending provider batches could not be cancelled; the provider ends them when they expire.";

    public static string StartedOverText(int files) =>
        $"Cleared the earlier run: deleted {Plural(files, "EML file", "EML files")}, the narrative ledger and the side projects.";

    // A run start refused by the gate re-check (09 UI-SPEC S1, U4)
    public const string RunRefusedGuideChangedText =
        "Generation did not start: the case guide changed after this plan was built. Open Review Plan to approve the plan.";

    public const string RunRefusedNotApprovedText =
        "Generation did not start: the corpus plan is not approved. Open Review Plan to approve it.";

    // 10 D-14: another provider's batches are pending and its key is missing
    public static string RunRefusedOtherProviderKeyText(string provider) =>
        "Generation did not start: " + OtherProviderKeyMissingText(provider);

    // 10 D-22: another provider's batches could not be collected or stopped; never run anyway
    public static string RunRefusedOtherProviderFailedText(string provider) =>
        $"Generation did not start: {ProviderName(provider)} batches from an earlier session could not be collected or stopped. Nothing was started, so nothing is paid twice. Try again in a moment.";

    public static string MissingEmlText(string docId) => $"{docId}.eml is no longer in the case folder.";

    // --- Entering the step ---

    /// <summary>
    /// First match wins: a running run is kept; a plan built from a changed (not kept) guide shows the GuideChanged card
    /// before the approval check, so a stale plan never reaches the Ready card (09 UI-SPEC S1); then not approved, plan
    /// changed, unreadable, ready, partial and finished.
    /// </summary>
    public static GenerationEntry EntryDecision(GenerationEntryInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (input.Running)
            return GenerationEntry.KeepRun;
        if (input.HasPlan && input.GuideStale)
            return GenerationEntry.GuideChangedCard;
        if (!input.HasPlan || !input.Approved)
            return GenerationEntry.NotApprovedCard;
        if (input.HasSavedRun && input.PlanChanged)
            return GenerationEntry.PlanChangedCard;
        if (!string.IsNullOrWhiteSpace(input.UnreadableReason))
            return GenerationEntry.UnreadableCard;

        // Never auto-starts: generation spends real money (UI-SPEC rule 5, U3)
        if (!input.HasSavedRun)
            return GenerationEntry.ReadyCard;

        // Unfinished = any thread or attachment without a final record; Skipped counts as final (media banner instead)
        return input.Written + input.Failed < input.Total || input.AttachmentsFinal < input.AttachmentsTotal
            ? GenerationEntry.PartialDashboard
            : GenerationEntry.FinishedDashboard;
    }

    /// <summary>
    /// Not-approved, Guide-changed, Plan-changed and Unreadable cards; the Ready branch delegates to <see cref="ReadyCard"/>
    /// with one model and no media. <paramref name="emailsWritten"/> feeds the Guide-changed body; <paramref name="hasPlan"/>
    /// splits the Not-approved card into "No corpus plan yet" and "Approve the plan first" (09 UI-SPEC S1, U10).
    /// </summary>
    public static GenerationCardContent StateCard(
        GenerationStateCard card, int threads, int emails, int waves, decimal cost, string modelName, int earlierThreads, string reason,
        int emailsWritten = 0, bool hasPlan = true)
    {
        return card switch
        {
            GenerationStateCard.NotApproved when !hasPlan => new GenerationCardContent
            {
                Heading = "No corpus plan yet",
                Body = "Emails are written from an approved corpus plan. Plan and approve it on Review Plan, or click Next to write discovery documents without emails.",
                ButtonText = "Review Plan"
            },
            GenerationStateCard.NotApproved => new GenerationCardContent
            {
                Heading = "Approve the plan first",
                Body = "Emails are written from an approved corpus plan. Approve the plan on Review Plan, then come back to this step. Or click Next to write discovery documents without emails.",
                ButtonText = "Review Plan"
            },
            GenerationStateCard.GuideChanged => new GenerationCardContent
            {
                Heading = "The case guide changed after this plan was built",
                Body = "Emails are written from the corpus plan and the guide it was built from, so generation is paused. Open Review Plan to keep this plan and approve it, or re-plan from the current guide."
                    + EmailsKeptSentence(emailsWritten),
                ButtonText = "Review Plan"
            },
            GenerationStateCard.PlanChanged => new GenerationCardContent
            {
                Heading = "The plan changed after emails were generated",
                Body = $"{Plural(earlierThreads, "thread", "threads")} {(earlierThreads == 1 ? "was" : "were")} written for an earlier plan, and their DocIDs no longer match the answer key. Start Over deletes those emails and the narrative ledger, then you can write the current plan.",
                ButtonText = "Start Over...",
                Danger = true
            },
            GenerationStateCard.Unreadable => new GenerationCardContent
            {
                Heading = "The saved run could not be opened",
                Body = $"{TrimMessage(reason)}. Start Over clears the saved run, deletes the emails it wrote and starts again from the approved plan.",
                ButtonText = "Start Over...",
                Danger = true
            },
            _ => ReadyCard(new GenerationReadyInput
            {
                RelevantThreads = threads,
                Emails = emails,
                Waves = waves,
                StoryCost = cost,
                LiveTotal = cost,
                BatchTotal = cost,
                StoryModel = modelName,
                VolumeModel = modelName
            })
        };
    }

    /// <summary>
    /// Ready card (06-UI-SPEC S1 Row 4): body in run order, the estimate line split Story / Volume / media (one model
    /// name when both are the same), and the Warning media notice when media is planned without a tested OpenAI key.
    /// </summary>
    public static GenerationCardContent ReadyCard(GenerationReadyInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var relevant = Plural(input.RelevantThreads, "relevant thread", "relevant threads");
        var waves = Plural(input.Waves, "wave", "waves");
        var order = input.NonRelevantThreads > 0
            ? $"{relevant} first, oldest first in {waves}, then the {Plural(input.NonRelevantThreads, "non-relevant thread", "non-relevant threads")}"
            : $"{relevant}, oldest first in {waves}";

        var mediaPlanned = input.MediaPlanned > 0;
        var mediaCost = mediaPlanned ? input.MediaCost : 0m;
        var hint = BatchHint(input.Emails, input.BatchChecked, BatchRecommendThreshold);

        var notice = mediaPlanned && !input.MediaAvailable
            ? $"{Plural(input.MediaPlanned, "image or voicemail", "images and voicemails")} {(input.MediaPlanned == 1 ? "is" : "are")} planned, but no tested OpenAI key is set up this session, so they will be marked Skipped. Test an OpenAI key on the API Configuration step to include them."
            : "";

        return new GenerationCardContent
        {
            Heading = "Ready to write the corpus",
            Body = $"Generate Emails writes the {order}: {Plural(input.Emails, "email", "emails")} in total{WithAttachments(input.Attachments)}. Each finished thread is saved straight to the custodians' mailboxes, so you can stop at any time and continue later.",
            ButtonText = "Generate Emails",
            EstimateLine = EstimateLine(input.LiveTotal, input.BatchTotal),
            EstimateTooltip = EstimateTooltip(input, mediaPlanned, mediaCost),
            BatchHint = hint.Text,
            BatchHintTone = hint.Tone,
            MediaNotice = notice
        };
    }

    // --- Batch pricing (07-UI-SPEC S1 Ready card, D-08, D-17) ---

    public const int BatchRecommendThreshold = 2_000; // [DEFAULT] UI-SPEC U28, research A8

    public const string BatchCheckboxText = "Use batch pricing for non-relevant mail (slower, lower cost)";

    /// <summary>
    /// Ready-card estimate line: the live and batch totals of the one estimate (D-17).
    /// </summary>
    public static string EstimateLine(decimal live, decimal batch) =>
        $"Estimated cost ≈{Money(live)} at live prices or ≈{Money(batch)} with batch pricing, ±50%, including prompt caching.";

    /// <summary>
    /// Estimate-line tooltip: per-model live and batch costs (one model name when Story and Volume are the same) plus the
    /// media clause when media is planned.
    /// </summary>
    public static string EstimateTooltip(GenerationReadyInput input, bool mediaPlanned, decimal mediaCost)
    {
        ArgumentNullException.ThrowIfNull(input);
        var mediaClause = mediaPlanned ? $" · images and voicemail ≈{Money(mediaCost)}" : "";
        var sameModel = string.Equals(input.StoryModel, input.VolumeModel, StringComparison.Ordinal);
        var live = sameModel
            ? $"{input.StoryModel} ≈{Money(input.StoryCost + input.VolumeCost)}"
            : $"{input.StoryModel} ≈{Money(input.StoryCost)} · {input.VolumeModel} ≈{Money(input.VolumeCost)}";
        var batch = sameModel
            ? $"{input.StoryModel} ≈{Money(input.BatchStoryCost + input.BatchVolumeCost)}"
            : $"{input.StoryModel} ≈{Money(input.BatchStoryCost)} · {input.VolumeModel} ≈{Money(input.BatchVolumeCost)}";
        return $"Live: {live}{mediaClause}. Batch: {batch}{mediaClause}. Relevant threads and their attachments, images and voicemail are always priced live. Cache savings are included only where the shared prompt is long enough for the provider to cache it. The batch total assumes no cache savings on batched mail.";
    }

    /// <summary>
    /// Hint under the batch checkbox: hidden below the threshold while unchecked, a Muted recommendation at or above it,
    /// and the Warning duration caution whenever batch is checked. Relevant threads run live first (amended D-07); only
    /// non-relevant mail waits on the provider.
    /// </summary>
    public static (string Text, SummaryTone Tone) BatchHint(int plannedEmails, bool batchChecked, int threshold)
    {
        if (batchChecked)
        {
            return ("Relevant threads run live first. Non-relevant mail then waits on the provider in large batches, which can take several hours. Keep this screen open; if the app closes, Continue Generating collects the finished batches without paying again.",
                SummaryTone.Warning);
        }

        if (plannedEmails >= threshold)
        {
            return ("Batch pricing is recommended for a corpus this size. Relevant threads still run live; non-relevant mail then waits on the provider, which can take several hours.",
                SummaryTone.Muted);
        }

        return ("", SummaryTone.Muted);
    }

    // --- Partial-run and media banners ---

    /// <summary>
    /// "The last run did not finish." when the latest attempt did not end (cancelled, crashed, stopped); "Part of the
    /// corpus is not written yet." when it ended but plan items have no record (for example a Phase 5 run).
    /// </summary>
    public static string BannerHeading(bool runEnded) =>
        runEnded ? "Part of the corpus is not written yet." : "The last run did not finish.";

    // Entry heading follows the latest run attempt, never an older RunEnded (review MEDIUM 06-13)
    public static string EntryBannerHeading(GenerationEntryInput input) => BannerHeading(input.LatestAttemptEnded);

    public const string BannerButton = "Continue Generating";

    // Header button (06-UI-SPEC Row 0): shown when a finished run has failed threads or attachments and no media banner button
    public const string RetryFailedButtonText = "Retry Failed Items";

    public const string RetryFailedTooltip =
        "Writes the failed threads and attachments again. Emails that are already saved are rewritten only when one of their attachments is added.";

    /// <summary>
    /// D-14: another provider's batches of this plan are pending and its key is missing; generating could pay twice, so
    /// the run refuses until the key is added or the provider is switched back.
    /// </summary>
    public static string OtherProviderKeyMissingText(string provider) =>
        $"{ProviderName(provider)} batches from an earlier session are still pending, and no {ProviderName(provider)} key is set up. Add the {ProviderName(provider)} key on the API Configuration step, or switch the text provider back to {ProviderName(provider)}, before generating.";

    /// <summary>
    /// Partial-run banner body; <paramref name="failedItems"/> counts threads + attachments. The batch clause counts only
    /// this plan's batches of the current text provider; the D-15 clause names another provider's batches, which are
    /// resolved at run start, or shows the D-14 notice when that provider's key is missing.
    /// </summary>
    public static string BannerBody(int threadsDone, int threadsTotal, int attachmentsDone, int attachmentsTotal, int failedItems,
        bool batchOn = false, int pendingBatches = 0, int otherProviderBatches = 0, string otherProvider = "", bool otherProviderBlocked = false)
    {
        var failedClause = failedItems > 0 ? $", and tries the {Plural(failedItems, "failed item", "failed items")} again" : "";
        // D-09: batches pending from before the app closed win over the saved-choice clause
        var batchClause = pendingBatches > 0
            ? $" {Plural(pendingBatches, "provider batch", "provider batches")} submitted earlier will be collected first, without paying again."
            : batchOn ? " Batch pricing stays on for this case." : "";
        // D-15: another provider's batches are resolved at run start (D-13), or block it when that key is missing (D-14)
        var otherClause = otherProviderBatches <= 0
            ? ""
            : otherProviderBlocked
                ? " " + OtherProviderKeyMissingText(otherProvider)
                : $" {Plural(otherProviderBatches, $"{ProviderName(otherProvider)} batch", $"{ProviderName(otherProvider)} batches")} from an earlier session {(otherProviderBatches == 1 ? "is" : "are")} collected or cancelled before generating.";
        return $"{N(threadsDone)} of {Plural(threadsTotal, "thread", "threads")}{AttachmentProgress(attachmentsDone, attachmentsTotal)} are written and saved. Continue Generating writes the rest and keeps everything already saved{failedClause}.{batchClause}{otherClause}";
    }

    /// <summary>
    /// Media banner after a finished run with Skipped images or voicemails. With media available now the button is
    /// Create Skipped Media, or Retry Failed and Skipped Items when failed items exist too; without it there is no button.
    /// </summary>
    public static MediaBannerContent MediaBanner(int skipped, int failedItems, bool mediaAvailable, string reason)
    {
        var subject = $"{Plural(skipped, "image or voicemail", "images and voicemails")} {(skipped == 1 ? "was" : "were")} not created";
        const string saved = "Their emails are saved without them, and the answer key marks them Skipped.";
        if (!mediaAvailable)
        {
            return new MediaBannerContent
            {
                Heading = "Some images and voicemails were skipped.",
                Body = $"{subject} because no tested OpenAI key is set up this session. {saved} Test an OpenAI key on the API Configuration step, then come back here."
            };
        }

        var button = failedItems > 0 ? "Retry Failed and Skipped Items" : "Create Skipped Media";
        return new MediaBannerContent
        {
            Heading = "Some images and voicemails were skipped.",
            Body = $"{subject}{Colon(reason)}. {saved} {button} creates them and rewrites the affected emails in every mailbox.",
            ButtonText = button
        };
    }

    // --- Confirmations (MessageBox, default button = the safe choice) ---

    public static (string Title, string Text) CG1CancelGeneration(int threads, int attachments, bool batchesPending = false)
    {
        var written = Plural(threads, "thread", "threads") + (attachments > 0 ? " and " + Plural(attachments, "attachment", "attachments") : "");
        var verb = threads + attachments == 1 ? "stays" : "stay";
        return ("Cancel Generation",
            $"Stop generating? The {written} already written {verb} saved. Items in progress are discarded and can be written later with Continue Generating."
            + (batchesPending ? " Provider batches already submitted keep running; Continue Generating collects their results without paying again." : ""));
    }

    /// <summary>
    /// C-G2 Start Over. The discovery clause (09 UI-SPEC S5) follows the batches clause when a saved discovery
    /// document cites at least one email.
    /// </summary>
    public static (string Title, string Text) CG2StartOver(int files, bool batchesPending = false, bool citedByDiscovery = false) =>
        ("Start Over",
            $"Delete the {Plural(files, "generated EML file", "generated EML files")} (relevant and non-relevant, with their attachments), the narrative ledger and the side projects, and reset the answer key to Planned? This cannot be undone. The corpus plan and case guide are kept."
            + (batchesPending ? " Pending provider batches are cancelled." : "")
            + (citedByDiscovery ? " Discovery documents that cite these emails will show Out of date." : ""));

    public static (string Title, string Text) CG3CloseDuringRun(bool batchesPending = false) =>
        ("Stop Generating?",
            "Emails are still being written. Close and stop? Finished threads and attachments are saved; items in progress are discarded and can be written later with Continue Generating."
            + (batchesPending ? " Provider batches already submitted keep running and are collected the next time you click Continue Generating." : ""));

    // --- Case Summary rows (S2) ---

    public static SummaryRowContent MailboxesRow(MailboxesRowInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var row = new SummaryRowContent
        {
            Line2 = $"{Plural(input.Files, "EML file", "EML files")} in {Plural(input.Mailboxes, "custodian mailbox", "custodian mailboxes")} · Mailboxes folder",
            ShowPrimary = input.FolderExists,
            // 09 W6: the way back to Generate Corpus is always offered; its entry gate decides what the user sees
            ShowSecondary = true,
            SecondaryText = input.Generated ? "Generate Corpus" : "Generate Emails"
        };

        if (!input.Generated)
        {
            row.Line3 = "Not generated yet";
            row.Line3Tone = SummaryTone.Muted;
            row.DotTone = row.Line3Tone;
            return row;
        }

        var threadsTotal = input.RelevantTotal + input.NonRelevantTotal;
        var threadsWritten = input.RelevantWritten + input.NonRelevantWritten;
        var complete = threadsWritten >= threadsTotal && input.AttachmentsWritten >= input.AttachmentsTotal
            && input.ItemWarnings == 0 && input.ItemFailed == 0 && input.AttachmentsSkipped == 0;

        if (input.EarlierPlan)
        {
            row.Line3 = "Written for an earlier plan. Start over on Generate Corpus.";
            row.Line3Tone = SummaryTone.Warning;
        }
        else if (complete)
        {
            row.Line3 = $"All {Plural(threadsTotal, "thread", "threads")}{(input.AttachmentsTotal > 0 ? " and " + Plural(input.AttachmentsTotal, "attachment", "attachments") : "")} written";
            row.Line3Tone = SummaryTone.Success;
        }
        else
        {
            // Zero clauses are dropped; warnings, failed and skipped are item counts (threads + attachments)
            var text = $"{N(threadsWritten)} of {N(threadsTotal)} threads written";
            if (input.ItemWarnings > 0)
                text += $" · {Plural(input.ItemWarnings, "item", "items")} with warnings";
            if (input.ItemFailed > 0)
                text += $" · {Plural(input.ItemFailed, "failed item", "failed items")}";
            if (input.AttachmentsSkipped > 0)
                text += $" · {Plural(input.AttachmentsSkipped, "skipped item", "skipped items")}";
            row.Line3 = text;
            row.Line3Tone = SummaryTone.Warning;
        }

        var skippedClause = input.AttachmentsSkipped > 0 ? $" ({N(input.AttachmentsSkipped)} skipped)" : "";
        row.Line3Tooltip =
            $"Relevant: {N(input.RelevantWritten)} of {Plural(input.RelevantTotal, "thread", "threads")}. Non-relevant: {N(input.NonRelevantWritten)} of {Plural(input.NonRelevantTotal, "thread", "threads")}. Attachments: {N(input.AttachmentsWritten)} of {N(input.AttachmentsTotal)}{skippedClause}.";
        row.DotTone = row.Line3Tone;
        return row;
    }

    public static SummaryRowContent StoryRow(StoryRowInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var row = new SummaryRowContent
        {
            Line2 = $"Narrative ledger · {Plural(input.Entries, "detail", "details")} from {Plural(input.Threads, "thread", "threads")} · {input.FileName}",
            ShowPrimary = input.FileExists,
            ShowSecondary = input.Entries > 0
        };

        if (!string.IsNullOrWhiteSpace(input.Error))
        {
            row.Line3 = $"Could not write: {TrimMessage(input.Error)}";
            row.Line3Tone = SummaryTone.Danger;
        }
        else if (input.WrittenLocal.HasValue)
        {
            row.Line3 = $"Written {DateTimeText(input.WrittenLocal.Value)}";
            row.Line3Tone = SummaryTone.Success;
        }
        else
        {
            row.Line3 = "Not written yet. It is written when a generation run ends.";
            row.Line3Tone = SummaryTone.Muted;
        }

        row.DotTone = row.Line3Tone;
        return row;
    }

    public static string AnswerKeyRunClause(DateTime writtenLocal, int generated, int planned) =>
        $"Written {DateTimeText(writtenLocal)} · {N(generated)} generated, {N(planned)} planned";

    // Answer key row tooltip addition: Skipped rows count as neither generated nor planned (06-UI-SPEC S2)
    public static string AnswerKeyRunTooltip(int skipped) => $"{N(skipped)} skipped";

    public static string StoryMissingText(string fileName) =>
        $"{fileName} is no longer in the case folder. Click Export Story So Far to write it again.";

    public const string StoryExportingText = "Exporting the story so far...";

    public static string StoryExportedText(string fileName) => $"Exported {fileName} to the case folder.";

    public static string StoryLockedText(string fileName) => $"Close {fileName} in Word, then click Export Story So Far.";

    public static string StoryExportFailedText(string fileName, string reason) =>
        $"Could not export {fileName}{Colon(reason)}. Check that the case folder is writable, then click Export Story So Far.";

    // --- Cost breakdown (07-UI-SPEC S6 header ticker tooltip, S2 R1 Case Summary cost row) ---

    /// <summary>
    /// Header cost ticker tooltip (D-16): the session total, then one line per non-zero component (4 decimals).
    /// The batch discount is shown as a negative amount; the listed items add up to the total.
    /// </summary>
    public static string CostBreakdown(decimal total, decimal input, decimal cacheWrite, decimal cacheRead,
        decimal output, decimal batchSavings, decimal media)
    {
        if (total == 0m && input == 0m && cacheWrite == 0m && cacheRead == 0m && output == 0m && batchSavings == 0m && media == 0m)
            return "Nothing spent this session yet.";

        var lines = new List<string> { $"Spent this session: {Dollars(total, "0.0000")}" };
        lines.AddRange(CostItems(input, cacheWrite, cacheRead, output, batchSavings, media, 0m)
            .Select(item => $"{item.Line}: {item.Sign}{Dollars(item.Amount, "0.0000")}"));
        return string.Join("\n", lines);
    }

    /// <summary>
    /// Case Summary "Generation cost" row (S2 R1): line 3 lists the total and every non-zero clause (2 decimals);
    /// spend recorded without a breakdown appears as "not itemized" so the clauses always add up to the total.
    /// Discovery documents spend (08-UI-SPEC S5) is one lump clause after images and voicemail; callers pass a
    /// <paramref name="total"/> that already includes it.
    /// </summary>
    public static SummaryRowContent CostRow(bool anySpend, decimal total, decimal input, decimal cacheWrite,
        decimal cacheRead, decimal output, decimal batchSavings, decimal media, decimal unsplit, decimal discovery = 0m)
    {
        var row = new SummaryRowContent
        {
            Line2 = "What this case has cost so far, across all sessions.",
            Line3Tone = SummaryTone.Muted
        };

        if (!anySpend)
        {
            row.Line3 = "Nothing spent yet";
            row.DotTone = SummaryTone.Muted;
            return row;
        }

        var items = CostItems(input, cacheWrite, cacheRead, output, batchSavings, media, unsplit, discovery);
        row.Line3 = $"{Dollars(total, "0.00")} spent"
            + string.Concat(items.Select(item => $" · {item.Clause} {item.Sign}{Dollars(item.Amount, "0.00")}"));
        row.Line3Tooltip = string.Join("\n", new[] { $"Spent so far: {Dollars(total, "0.00")}" }
            .Concat(items.Select(item => $"{item.Line}: {item.Sign}{Dollars(item.Amount, "0.00")}")));
        row.DotTone = SummaryTone.Success;
        return row;
    }

    // Non-zero cost components in display order: (row clause, tooltip line, sign, amount)
    private static List<(string Clause, string Line, string Sign, decimal Amount)> CostItems(decimal input,
        decimal cacheWrite, decimal cacheRead, decimal output, decimal batchSavings, decimal media, decimal unsplit,
        decimal discovery = 0m)
    {
        var all = new List<(string Clause, string Line, string Sign, decimal Amount)>
        {
            ("input", "Input", "", input),
            ("cache write", "Cached input, cache write", "", cacheWrite),
            ("cache read", "Cached input, cache read", "", cacheRead),
            ("output", "Output", "", output),
            // U+2212 minus sign: the discount is subtracted from the total
            ("batch discount", "Batch discount", "−", batchSavings),
            ("images and voicemail", "Images and voicemail", "", media),
            ("discovery documents", "Discovery documents", "", discovery),
            ("not itemized", "Not itemized", "", unsplit)
        };
        return all.Where(item => item.Amount != 0m).ToList();
    }

    private static string Dollars(decimal amount, string format) => "$" + amount.ToString(format, Invariant);

    // --- Shared formatting ---

    private static string Plural(int n, string singular, string plural) => CaseGuideUiText.Plural(n, singular, plural);

    // Leading-space sentence appended to the Guide-changed body; empty when no emails were written
    private static string EmailsKeptSentence(int n)
    {
        if (n <= 0)
            return "";
        if (n == 1)
            return " The 1 email already written stays in place.";
        return $" The {n.ToString("N0", CultureInfo.InvariantCulture)} emails already written stay in place.";
    }

    // ", with {a} attachments", or "" at 0
    private static string WithAttachments(int attachments) =>
        attachments > 0 ? ", with " + Plural(attachments, "attachment", "attachments") : "";

    // Joins the non-empty parts with the separator (zero clauses are passed as "")
    private static string Join(string separator, params string[] parts) =>
        string.Join(separator, parts.Where(p => p.Length > 0));

    // "a", "a and b", "a, b and c"
    private static string JoinClauses(IReadOnlyList<string> clauses) =>
        clauses.Count <= 1
            ? string.Join("", clauses)
            : string.Join(", ", clauses.Take(clauses.Count - 1)) + " and " + clauses[^1];

    /// <summary>
    /// "$12.35" under $100, "$250" from $100 up (05-UI-SPEC tile 7).
    /// </summary>
    private static string Money(decimal amount) =>
        amount >= 100m
            ? "$" + Math.Round(amount, 0, MidpointRounding.AwayFromZero).ToString("N0", Invariant)
            : "$" + Math.Round(amount, 2, MidpointRounding.AwayFromZero).ToString("0.00", Invariant);

    /// <summary>
    /// Trims whitespace and trailing periods so templates can append their own punctuation.
    /// </summary>
    private static string TrimMessage(string? message) => (message ?? "").Trim().TrimEnd('.').TrimEnd();

    /// <summary>
    /// ": {message}" or "" when there is no message.
    /// </summary>
    private static string Colon(string? message)
    {
        var trimmed = TrimMessage(message);
        return trimmed.Length == 0 ? "" : ": " + trimmed;
    }

    private static string N(int value) => value.ToString("N0", Invariant);

    // Same "MMM d, yyyy h:mm tt" format as CorpusUiText.DateTimeText (private there)
    private static string DateTimeText(DateTime value) => value.ToString("MMM d, yyyy h:mm tt", Invariant);
}
