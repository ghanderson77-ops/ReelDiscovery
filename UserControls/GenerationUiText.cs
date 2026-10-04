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
    Unreadable
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

    // Mailboxes: Open Mailboxes Folder / Generate Emails. Story so far: Open in Word / Export Story So Far
    public bool ShowPrimary;
    public bool ShowSecondary;
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
                    Tooltip = $"Spent on generation in this case: ${c.CostUsd.ToString("0.00", Invariant)}{media}. Estimate for the remaining items: ≈{Money(c.RemainingCostUsd)}, ±50%. The cost ticker in the header shows the whole session."
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

    public static string FileWriteFailedText(string fileName, string reason) =>
        $"Could not update {fileName}{Colon(reason)}. Your emails are saved; the file is written again when you leave this step.";

    public static string StartedOverText(int files) =>
        $"Cleared the earlier run: deleted {Plural(files, "EML file", "EML files")}, the narrative ledger and the side projects.";

    public static string MissingEmlText(string docId) => $"{docId}.eml is no longer in the case folder.";

    // --- Entering the step ---

    public static GenerationEntry EntryDecision(GenerationEntryInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (input.Running)
            return GenerationEntry.KeepRun;
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
    /// Not-approved, Plan-changed and Unreadable cards; the Ready branch delegates to <see cref="ReadyCard"/> with one
    /// model and no media.
    /// </summary>
    public static GenerationCardContent StateCard(
        GenerationStateCard card, int threads, int emails, int waves, decimal cost, string modelName, int earlierThreads, string reason)
    {
        return card switch
        {
            GenerationStateCard.NotApproved => new GenerationCardContent
            {
                Heading = "Approve the plan first",
                Body = "Emails are written from an approved corpus plan. Approve the plan on Review Plan, then come back to this step.",
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
        var total = input.StoryCost + input.VolumeCost + mediaCost;
        var mediaClause = mediaPlanned ? $" · images and voicemail ≈{Money(mediaCost)}" : "";
        var estimate = string.Equals(input.StoryModel, input.VolumeModel, StringComparison.Ordinal)
            ? $"Estimated cost ≈{Money(total)} on {input.StoryModel}, ±50%{mediaClause}"
            : $"Estimated cost ≈{Money(total)}, ±50%: {input.StoryModel} ≈{Money(input.StoryCost)} · {input.VolumeModel} ≈{Money(input.VolumeCost)}{mediaClause}";

        var notice = mediaPlanned && !input.MediaAvailable
            ? $"{Plural(input.MediaPlanned, "image or voicemail", "images and voicemails")} {(input.MediaPlanned == 1 ? "is" : "are")} planned, but no tested OpenAI key is set up this session, so they will be marked Skipped. Test an OpenAI key on the API Configuration step to include them."
            : "";

        return new GenerationCardContent
        {
            Heading = "Ready to write the corpus",
            Body = $"Generate Emails writes the {order}: {Plural(input.Emails, "email", "emails")} in total{WithAttachments(input.Attachments)}. Each finished thread is saved straight to the custodians' mailboxes, so you can stop at any time and continue later.",
            ButtonText = "Generate Emails",
            EstimateLine = estimate,
            MediaNotice = notice
        };
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
    /// Partial-run banner body; <paramref name="failedItems"/> counts threads + attachments.
    /// </summary>
    public static string BannerBody(int threadsDone, int threadsTotal, int attachmentsDone, int attachmentsTotal, int failedItems)
    {
        var failedClause = failedItems > 0 ? $", and tries the {Plural(failedItems, "failed item", "failed items")} again" : "";
        return $"{N(threadsDone)} of {Plural(threadsTotal, "thread", "threads")}{AttachmentProgress(attachmentsDone, attachmentsTotal)} are written and saved. Continue Generating writes the rest and keeps everything already saved{failedClause}.";
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

    public static (string Title, string Text) CG1CancelGeneration(int threads, int attachments)
    {
        var written = Plural(threads, "thread", "threads") + (attachments > 0 ? " and " + Plural(attachments, "attachment", "attachments") : "");
        var verb = threads + attachments == 1 ? "stays" : "stay";
        return ("Cancel Generation",
            $"Stop generating? The {written} already written {verb} saved. Items in progress are discarded and can be written later with Continue Generating.");
    }

    public static (string Title, string Text) CG2StartOver(int files) =>
        ("Start Over",
            $"Delete the {Plural(files, "generated EML file", "generated EML files")} (relevant and non-relevant, with their attachments), the narrative ledger and the side projects, and reset the answer key to Planned? This cannot be undone. The corpus plan and case guide are kept.");

    public static (string Title, string Text) CG3CloseDuringRun() =>
        ("Stop Generating?",
            "Emails are still being written. Close and stop? Finished threads and attachments are saved; items in progress are discarded and can be written later with Continue Generating.");

    // --- Case Summary rows (S2) ---

    public static SummaryRowContent MailboxesRow(MailboxesRowInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var row = new SummaryRowContent
        {
            Line2 = $"{Plural(input.Files, "EML file", "EML files")} in {Plural(input.Mailboxes, "custodian mailbox", "custodian mailboxes")} · Mailboxes folder",
            ShowPrimary = input.FolderExists,
            ShowSecondary = !input.Generated
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

    // --- Shared formatting ---

    private static string Plural(int n, string singular, string plural) => CaseGuideUiText.Plural(n, singular, plural);

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
