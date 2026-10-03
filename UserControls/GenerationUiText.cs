using System.Globalization;
using ReelDiscovery.Models.Evidence;

namespace ReelDiscovery.UserControls;

/// <summary>
/// The seven Generate Corpus tiles (05-UI-SPEC S1 tiles table). Distinct from the Phase 4 <see cref="TileKind"/>.
/// </summary>
public enum GenerationTileKind
{
    Threads,
    Emails,
    EmlFiles,
    Ledger,
    Warnings,
    Failed,
    Cost
}

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

    // Mailboxes: Open Mailboxes Folder / Generate Emails. Story so far: Open in Word / Export Story So Far
    public bool ShowPrimary;
    public bool ShowSecondary;
}

public class MailboxesRowInput
{
    public bool Generated;
    public bool EarlierPlan;
    public int Total;

    // Includes kept-with-warnings
    public int Written;
    public int Warnings;
    public int Failed;
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
/// All Phase 5 Generate Corpus and Case Summary copy with counts, dates or conditions (05-UI-SPEC), pure and
/// unit-tested so the generation step stays layout-only (03-06 precedent). Every string is plain text and may carry
/// model-written subjects or violation texts; labels showing it set UseMnemonic = false (T-04-33, T-05-15).
/// </summary>
public static class GenerationUiText
{
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;
    private static readonly CultureInfo EnUs = CultureInfo.GetCultureInfo("en-US");

    public const string StoppingText = "Stopping... finished threads are saved.";

    // --- Notice line ---

    /// <summary>
    /// Scope honesty while attachments and non-relevant email are not generated yet (U16).
    /// </summary>
    public static string ScopeNotice(int relevantThreads, int nonRelevantEmails, int attachments)
    {
        var lead = $"This step writes the {Plural(relevantThreads, "relevant thread", "relevant threads")} from the approved plan as email. ";
        var rest = $"the {N(nonRelevantEmails)} non-relevant emails are not generated yet; their answer-key rows stay Planned.";
        return attachments > 0
            ? lead + "Attachments and " + rest
            : lead + "T" + rest[1..];
    }

    // --- Tiles ---

    /// <summary>
    /// Tile copy; threadsVariant 1 drops the spaces around "/", 2 moves the total to the sub-line (DPI rule 19).
    /// </summary>
    public static TileContent Tile(GenerationTileKind kind, GenerationCounts c, int threadsVariant = 0)
    {
        ArgumentNullException.ThrowIfNull(c);
        switch (kind)
        {
            case GenerationTileKind.Threads:
                var waves = Plural(c.WaveCount, "wave", "waves");
                return new TileContent
                {
                    Caption = "Threads",
                    Value = threadsVariant switch
                    {
                        1 => $"{N(c.DoneThreads)}/{N(c.TotalThreads)}",
                        2 => N(c.DoneThreads),
                        _ => $"{N(c.DoneThreads)} / {N(c.TotalThreads)}"
                    },
                    SubLine = threadsVariant == 2 ? $"of {N(c.TotalThreads)}" : waves,
                    Tooltip = $"{N(c.DoneThreads)} of {N(c.TotalThreads)} relevant threads written, in {waves} by date"
                };
            case GenerationTileKind.Emails:
                return new TileContent
                {
                    Caption = "Emails",
                    Value = N(c.EmailsWritten),
                    SubLine = $"of {N(c.EmailsPlanned)} planned",
                    Tooltip = "Relevant emails written so far. Each email is saved in every participant's mailbox."
                };
            case GenerationTileKind.EmlFiles:
                return new TileContent
                {
                    Caption = "EML files",
                    Value = N(c.FilesWritten),
                    SubLine = Plural(c.Mailboxes, "mailbox", "mailboxes"),
                    Tooltip = $"{N(c.FilesWritten)} .eml files in {Plural(c.Mailboxes, "custodian mailbox", "custodian mailboxes")} under the Mailboxes folder"
                };
            case GenerationTileKind.Ledger:
                return new TileContent
                {
                    Caption = "Ledger",
                    Value = N(c.LedgerEntries),
                    SubLine = "story details",
                    Tooltip = "Commitments, figures, meetings, decisions, people and documents the written emails introduced. Later emails build on them."
                };
            case GenerationTileKind.Warnings:
                return new TileContent
                {
                    Caption = "Warnings",
                    Value = N(c.WarningThreads),
                    SubLine = "threads kept",
                    Tooltip = "Threads that still broke a consistency check after 3 attempts. The best attempt was kept and marked \"Generated - with warnings\" in the answer key.",
                    Tone = c.WarningThreads > 0 ? TileTone.Warning : TileTone.Normal
                };
            case GenerationTileKind.Failed:
                return new TileContent
                {
                    Caption = "Failed",
                    Value = N(c.FailedThreads),
                    SubLine = "threads not written",
                    Tooltip = "Threads that could not be written. Retry them with Continue Generating or Retry Failed Threads.",
                    Tone = c.FailedThreads > 0 ? TileTone.Danger : TileTone.Normal
                };
            default:
                return new TileContent
                {
                    Caption = "Cost",
                    Value = Money(c.CostUsd),
                    SubLine = c.Finished ? "this case" : $"≈{Money(c.RemainingCostUsd)} to go",
                    Tooltip = $"Spent on generation in this case: ${c.CostUsd.ToString("0.00", Invariant)}. Estimate for the remaining threads: ≈{Money(c.RemainingCostUsd)}, ±50%. The cost ticker in the header shows the whole session."
                };
        }
    }

    // --- Waves ---

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

    public static string WaveTooltip(int threads, int emails) =>
        $"{Plural(threads, "thread", "threads")}, {Plural(emails, "email", "emails")}";

    // --- Activity ---

    public static ActivityLine ActivityText(GenerationEvent e)
    {
        ArgumentNullException.ThrowIfNull(e);
        var violations = e.Violations ?? new List<string>();
        var first = violations.Count > 0 ? TrimMessage(violations[0]) : "";

        var (text, tone) = e.Kind switch
        {
            GenerationEventKind.RunStarted =>
                ($"Started: {Plural(e.Threads, "thread", "threads")} to write in {Plural(e.WaveCount, "wave", "waves")}", ActivityTone.Accent),
            GenerationEventKind.RunContinued =>
                ($"Continued: {Plural(e.Threads, "thread", "threads")} left to write", ActivityTone.Accent),
            GenerationEventKind.WaveStarted =>
                ($"Wave {e.WaveIndex} of {e.WaveCount} started ({WaveDates(e.WaveStart, e.WaveEnd)}, {Plural(e.Threads, "thread", "threads")})", ActivityTone.Accent),
            GenerationEventKind.ThreadWritten =>
                ($"Written: \"{e.Subject}\", {Plural(e.Emails, "email", "emails")}, {Plural(e.Details, "new ledger detail", "new ledger details")}", ActivityTone.Success),
            GenerationEventKind.Retry =>
                ($"Attempt {e.Attempt} of 3 failed {Plural(violations.Count, "check", "checks")}{Colon(first)}. Retrying.", ActivityTone.Warning),
            GenerationEventKind.KeptWithWarnings =>
                ($"Kept with {Plural(violations.Count, "warning", "warnings")} after 3 attempts{Colon(first)}", ActivityTone.Warning),
            GenerationEventKind.ThreadFailed =>
                ($"Failed{Colon(e.Reason)}. The run continues.", ActivityTone.Danger),
            GenerationEventKind.WaveFinished =>
                ($"Wave {e.WaveIndex} finished: {N(e.Written)} written, {N(e.Warnings)} with warnings, {N(e.Failed)} failed. Ledger now {N(e.LedgerTotal)} details.",
                    e.Failed > 0 ? ActivityTone.Danger : e.Warnings > 0 ? ActivityTone.Warning : ActivityTone.Success),
            GenerationEventKind.RunCancelled =>
                ($"Cancelled. {N(e.Done)} of {N(e.Total)} threads are saved.", ActivityTone.Warning),
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

    // --- Status strip ---

    public static string ProgressText(GenerationView v)
    {
        ArgumentNullException.ThrowIfNull(v);
        return v.Stage switch
        {
            GenerationStage.Writing =>
                $"Wave {v.StageWave} of {v.StageWaveCount} ({WaveDates(v.StageWaveStart, v.StageWaveEnd)}): writing {Plural(v.StageCount, "thread", "threads")}...",
            GenerationStage.Merging =>
                $"Wave {v.StageWave} of {v.StageWaveCount}: merging {Plural(v.StageCount, "new ledger detail", "new ledger details")}...",
            GenerationStage.UpdatingKey => "Updating the answer key...",
            GenerationStage.WritingStory => "Writing the story so far...",
            GenerationStage.Stopping => StoppingText,
            _ => ""
        };
    }

    public static OutcomeLine OutcomeText(GenerationOutcome outcome, GenerationCounts c, string message)
    {
        ArgumentNullException.ThrowIfNull(c);
        switch (outcome)
        {
            case GenerationOutcome.Finished:
                if (c.WarningThreads == 0 && c.FailedThreads == 0)
                {
                    return new OutcomeLine
                    {
                        Text = $"Done: {Plural(c.DoneThreads, "relevant thread", "relevant threads")} written as {Plural(c.EmailsWritten, "email", "emails")} ({Plural(c.FilesWritten, "EML file", "EML files")} in {Plural(c.Mailboxes, "mailbox", "mailboxes")}). The answer key and story so far are updated.",
                        Tone = ActivityTone.Success
                    };
                }

                // Zero clauses are dropped; the Retry instruction only appears when something failed
                var clauses = new List<string>();
                if (c.WarningThreads > 0)
                    clauses.Add(Plural(c.WarningThreads, "warning", "warnings"));
                if (c.FailedThreads > 0)
                    clauses.Add(Plural(c.FailedThreads, "failed thread", "failed threads"));
                var tail = c.FailedThreads > 0
                    ? "The answer key marks them; click Retry Failed Threads to try the failed ones again."
                    : "The answer key marks them.";
                return new OutcomeLine { Text = $"Done with {string.Join(" and ", clauses)}. {tail}", Tone = ActivityTone.Warning };

            case GenerationOutcome.Cancelled:
                return new OutcomeLine
                {
                    Text = $"Cancelled. {N(c.DoneThreads)} of {N(c.TotalThreads)} threads are saved. Click Continue Generating to write the rest.",
                    Tone = ActivityTone.Warning
                };

            case GenerationOutcome.Fatal:
                return new OutcomeLine
                {
                    Text = $"Generation stopped{Colon(message)}. Finished threads are saved. Fix the problem on the API Configuration step, then click Continue Generating.",
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
        $"Cleared the earlier run: deleted {Plural(files, "EML file", "EML files")} and the narrative ledger.";

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
        return input.Written + input.Failed < input.Total
            ? GenerationEntry.PartialDashboard
            : GenerationEntry.FinishedDashboard;
    }

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
            _ => new GenerationCardContent
            {
                Heading = "Ready to write the relevant emails",
                Body = $"Generate Emails writes the {Plural(threads, "relevant thread", "relevant threads")} ({Plural(emails, "email", "emails")}) from the approved plan, oldest first, in {Plural(waves, "wave", "waves")}. Each finished thread is saved straight to the custodians' mailboxes, so you can stop at any time and continue later. Estimated cost: ≈{Money(cost)} on {modelName}, ±50%.",
                ButtonText = "Generate Emails"
            }
        };
    }

    // --- Partial-run banner ---

    public const string BannerHeading = "The last run did not finish.";
    public const string BannerButton = "Continue Generating";

    public static string BannerBody(int done, int total, int failed)
    {
        var failedClause = failed > 0 ? $", and tries the {Plural(failed, "failed thread", "failed threads")} again" : "";
        return $"{N(done)} of {Plural(total, "relevant thread", "relevant threads")} are written and saved. Continue Generating writes the rest and keeps everything already saved{failedClause}.";
    }

    // --- Confirmations (MessageBox, default button = the safe choice) ---

    public static (string Title, string Text) CG1CancelGeneration(int done) =>
        ("Cancel Generation",
            $"Stop generating? The {Plural(done, "thread", "threads")} already written {(done == 1 ? "stays" : "stay")} saved. Threads in progress are discarded and can be written later with Continue Generating.");

    public static (string Title, string Text) CG2StartOver(int files) =>
        ("Start Over",
            $"Delete the {Plural(files, "generated EML file", "generated EML files")} and the narrative ledger, and reset the answer key to Planned? This cannot be undone. The corpus plan and case guide are kept.");

    public static (string Title, string Text) CG3CloseDuringRun() =>
        ("Stop Generating?",
            "Emails are still being written. Close and stop? Finished threads are saved; threads in progress are discarded and can be written later with Continue Generating.");

    // --- Case Summary rows (S3) ---

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
        }
        else if (input.EarlierPlan)
        {
            row.Line3 = "Written for an earlier plan. Start over on Generate Corpus.";
            row.Line3Tone = SummaryTone.Warning;
        }
        else if (input.Written >= input.Total && input.Warnings == 0 && input.Failed == 0)
        {
            row.Line3 = $"All {Plural(input.Total, "relevant thread", "relevant threads")} written";
            row.Line3Tone = SummaryTone.Success;
        }
        else
        {
            // Zero clauses are dropped
            var text = $"{N(input.Written)} of {N(input.Total)} relevant threads written";
            if (input.Warnings > 0)
                text += $" · {N(input.Warnings)} with warnings";
            if (input.Failed > 0)
                text += $" · {N(input.Failed)} failed";
            row.Line3 = text;
            row.Line3Tone = SummaryTone.Warning;
        }

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

    public static string StoryMissingText(string fileName) =>
        $"{fileName} is no longer in the case folder. Click Export Story So Far to write it again.";

    public const string StoryExportingText = "Exporting the story so far...";

    public static string StoryExportedText(string fileName) => $"Exported {fileName} to the case folder.";

    public static string StoryLockedText(string fileName) => $"Close {fileName} in Word, then click Export Story So Far.";

    public static string StoryExportFailedText(string fileName, string reason) =>
        $"Could not export {fileName}{Colon(reason)}. Check that the case folder is writable, then click Export Story So Far.";

    // --- Shared formatting ---

    private static string Plural(int n, string singular, string plural) => CaseGuideUiText.Plural(n, singular, plural);

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
