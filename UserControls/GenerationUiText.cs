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
}
