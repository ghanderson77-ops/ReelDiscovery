using System.Globalization;
using ReelDiscovery.Models.Case;
using ReelDiscovery.Models.Corpus;

namespace ReelDiscovery.UserControls;

/// <summary>
/// One relevant-share line under the slider: Warning color when IsWarning, otherwise muted (D-07).
/// </summary>
public class WarningLine
{
    public string Text = "";
    public bool IsWarning;
}

/// <summary>
/// Status of an answer-key or review-protocol file on the Case Summary Contents card.
/// </summary>
public enum CorpusFileStatus
{
    NotWritten,
    Written,
    EarlierPlan,
    Failed
}

/// <summary>
/// All Phase 4 copy with counts, dates or conditions (04-UI-SPEC), pure and unit-tested so the corpus steps stay
/// layout-only (03-06 precedent). Every string is plain text; labels showing it set UseMnemonic = false (T-04-33).
/// </summary>
public static class CorpusUiText
{
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    // --- Corpus Settings: size and relevance ---

    public const string DateInvalidText = "Use a date like 2024-03-03 (YYYY-MM-DD).";
    public const string EndBeforeStartText = "The end date must be after the start date.";
    public const string PrefixInvalidText = "Use 2 to 6 letters, e.g. DM.";
    public const string MediaNotice = "Images and voicemail need a tested OpenAI key. Add one on the API Configuration step to turn them on.";
    public const string AttachmentTypesHint = "Office documents are Word, Excel and PowerPoint files. The plan only gives attachments of the types you check.";
    public const string DecoyHint = "Near-miss decoys mention the same projects and people but are not about the alleged conduct.";
    public const string OverridesHint = "Leave a field empty to let the planner choose.";

    // Automatic planner defaults quoted in the override hints (D-09)
    private const int AutoPrivilegedPercent = 8;
    private const int AutoAttachmentPercent = 20;
    private const int CounselNamesShown = 2;

    /// <summary>
    /// "≈{r:N0} relevant / {n:N0} non-relevant emails"; a fixed format, so the noun stays plural.
    /// </summary>
    public static string LiveCounts(int relevant, int nonRelevant) =>
        $"≈{N(relevant)} relevant / {N(nonRelevant)} non-relevant emails";

    /// <summary>
    /// Warning when relevant emails are fewer than dated key facts, a muted hint below twice that, else null (D-07).
    /// Never blocks Next.
    /// </summary>
    public static WarningLine? RelevantWarning(int total, int percent, int datedFacts)
    {
        if (datedFacts <= 0)
            return null;

        var relevant = CorpusSettings.RelevantCount(total, percent);
        if (relevant < datedFacts)
        {
            var emails = CaseGuideUiText.Plural(relevant, "relevant email", "relevant emails");
            var facts = CaseGuideUiText.Plural(datedFacts, "key fact", "key facts");
            return new WarningLine
            {
                IsWarning = true,
                Text = $"Only {emails} for {facts}. Every key fact needs at least one relevant email: raise this to at least {MinPercentFor(total, datedFacts)}%."
            };
        }

        if (relevant < 2 * datedFacts)
        {
            return new WarningLine
            {
                IsWarning = false,
                Text = $"Most key facts will get a single short email. Raise this to {MinPercentFor(total, 2 * datedFacts)}% or more for fuller threads."
            };
        }

        return null;
    }

    /// <summary>
    /// Smallest whole percent in the slider range whose RelevantCount reaches needed; capped at the maximum (60).
    /// </summary>
    public static int MinPercentFor(int total, int needed)
    {
        for (var p = CorpusSettings.MinRelevantPercent; p < CorpusSettings.MaxRelevantPercent; p++)
        {
            if (CorpusSettings.RelevantCount(total, p) >= needed)
                return p;
        }

        return CorpusSettings.MaxRelevantPercent;
    }

    public static string SettingsNotice(bool hasPlan, bool approved, DateTime? approvedLocal)
    {
        if (!hasPlan)
            return "Click Next to plan every thread and message from these settings and the case guide. Nothing is written yet.";
        if (!approved)
            return "Changing these settings replaces the current plan when you click Next.";

        var on = approvedLocal.HasValue ? $" on {Date(approvedLocal.Value)}" : "";
        return $"The plan was approved{on}. If you change these settings, Review Plan offers to re-plan; the approved plan stays until you do.";
    }

    public static string SizeHint(ComplexityLevel level) =>
        $"Default for a {LevelName(level)} case: {N(CorpusSettings.DefaultTotalFor(level))} emails. Type any number from {N(CorpusSettings.MinEmails)} to {N(CorpusSettings.MaxEmails)}.";

    public static string SizeClampNotice(int clamped) =>
        $"Set to {N(clamped)}. The corpus size must be {N(CorpusSettings.MinEmails)} to {N(CorpusSettings.MaxEmails)} emails.";

    // --- Corpus Settings: dates and attachments ---

    public static string WindowHint(DateTime firstFact, DateTime lastFact) =>
        $"Key facts run from {Date(firstFact)} to {Date(lastFact)}. The default window adds about a month before and two months after for everyday email.";

    public static string StartClampedText(DateTime start) =>
        $"Start moved to {Date(start)}, the first key fact. The window must include every key fact.";

    public static string EndClampedText(DateTime end) =>
        $"End moved to {Date(end)}, the last key fact. The window must include every key fact.";

    // --- Corpus Settings: Advanced ---

    public static (string Text, bool Ok) MixTotalLine(int total) =>
        total == 100
            ? ("Total 100%", true)
            : ($"Total {N(total)}%. The four shares must add up to 100%.", false);

    public static string MixHint(int nonRelevant) => $"How the {N(nonRelevant)} non-relevant emails are split.";

    /// <summary>
    /// Collapsed Advanced header; "custom" when any of the three overrides is set. prefix is the effective DocID prefix.
    /// </summary>
    public static string AdvancedSummary(CorpusSettings settings, string prefix)
    {
        var custom = settings.HotPerHotFact.HasValue || settings.PrivilegedPercent.HasValue || settings.AttachmentPercent.HasValue;
        return $"Other email mix {settings.DailyBusinessPercent} / {settings.AncillaryPercent} / {settings.NewsletterPercent} / {settings.DecoyPercent}% · hot, privileged and attachment rate {(custom ? "custom" : "automatic")} · DocID prefix {prefix}";
    }

    /// <summary>
    /// "about {f}-{2f} hot emails": the automatic planner gives each hot fact one or two hot emails.
    /// </summary>
    public static string HotHint(int hotFacts)
    {
        if (hotFacts <= 0)
            return "Auto: no facts are marked Hot.";
        return $"Auto: 1 or 2 each, about {N(hotFacts)}-{N(hotFacts * 2)} hot emails from {CaseGuideUiText.Plural(hotFacts, "hot fact", "hot facts")}.";
    }

    /// <summary>
    /// Names at most two counsel, then "and {n} more"; no counsel means the field is disabled.
    /// </summary>
    public static string PrivilegedHint(IReadOnlyList<string> counselNames)
    {
        var names = counselNames.Where(n => !string.IsNullOrWhiteSpace(n)).Select(n => n.Trim()).ToList();
        if (names.Count == 0)
            return "No counsel in the cast, so no privileged threads are planned.";

        string who;
        if (names.Count <= CounselNamesShown)
            who = names.Count == 1 ? names[0] : $"{names[0]} and {names[1]}";
        else
            who = $"{string.Join(", ", names.Take(CounselNamesShown))} and {N(names.Count - CounselNamesShown)} more";

        return $"Auto: about {AutoPrivilegedPercent}%, threads with {who}.";
    }

    public static string AttachmentHint() => $"Auto: {AutoAttachmentPercent}% of emails carry an attachment.";

    public static string PrefixHint(string prefix) => $"DocIDs will read {prefix}-000001, attachments {prefix}-000001.0001.";

    public static string OverrideClampNotice(int value, int min, int max) => $"Set to {N(value)}. Use {N(min)} to {N(max)}.";

    // --- Next-gate messages and confirmations (Corpus Settings) ---

    public static (string Title, string Text) MS1MixTotal(int total) =>
        ("Check the Settings", $"The non-relevant mix adds up to {N(total)}%. Change the shares under Advanced so they add up to 100%, then click Next again.");

    /// <summary>
    /// which = "start" or "end".
    /// </summary>
    public static (string Title, string Text) MS2InvalidDate(string which) =>
        ("Check the Settings", $"The {which} date is not a valid date. Use a date like 2024-03-03, then click Next again.");

    public static (string Title, string Text) CS1ResetDefaults(ComplexityLevel level) =>
        ("Reset to Defaults", $"Reset every corpus setting to the defaults for a {LevelName(level)} case?");

    // --- Re-plan dialog (D-RP) ---

    public const string ReplanTitle = "Re-plan Corpus";
    public const string ReplanHeadline = "Re-plan the corpus";
    public const string ReplanGuidanceCaption = "Guidance (optional)";
    public const string ReplanPlaceholder = "e.g. more traffic around the March change-order dispute";
    public const string ReplanHint = "Guidance can shift volume toward or away from facts and periods. Dates, people and labels still come from the case guide.";

    public static string ReplanBody(bool approved)
    {
        const string body = "Builds a new plan from the case guide and the current corpus settings, and replaces this one. Every DocID changes. This makes new Story-model calls.";
        return approved
            ? body + " The answer key and review protocol in the case folder are rewritten when you approve the new plan."
            : body;
    }

    // --- Case Summary corpus rows ---

    public const string NotPlannedLine = "Not planned yet";

    public static string CorpusPlanLine(int emails, int threads, decimal relevantPercent, DateTime first, DateTime last)
    {
        var emailText = CaseGuideUiText.Plural(emails, "email", "emails");
        var threadText = CaseGuideUiText.Plural(threads, "thread", "threads");
        return $"{emailText} in {threadText} · {PercentText(relevantPercent)} relevant · {Month(first)} to {Month(last)}";
    }

    /// <summary>
    /// Stale wins over the approval state (Case Summary marks files out of date, D-25).
    /// </summary>
    public static string PlanStatusLine(bool approved, DateTime? approvedLocal, bool stale)
    {
        if (stale)
            return "Out of date with the case guide";
        if (!approved)
            return "Not approved yet";
        return approvedLocal.HasValue ? $"Approved {DateTimeText(approvedLocal.Value)}" : "Approved";
    }

    public static string AnswerKeyLine(int rows, int emails, int attachments) =>
        $"answer-key.csv and answer-key.xlsx · {CaseGuideUiText.Plural(rows, "row", "rows")} ({N(emails)} emails, {N(attachments)} attachments)";

    public static string ProtocolLine(int issues, string fileName) =>
        $"Coding manual for reviewers · {CaseGuideUiText.Plural(issues, "issue code", "issue codes")} · {fileName}";

    public static string ProtocolFileName(string folderName) => $"{folderName} - Review Protocol.docx";

    public static string FileStatusLine(CorpusFileStatus status, DateTime? writtenLocal, string reason)
    {
        return status switch
        {
            CorpusFileStatus.Written => writtenLocal.HasValue ? $"Written {DateTimeText(writtenLocal.Value)}" : "Written",
            CorpusFileStatus.EarlierPlan => "Written for an earlier plan. Approve the current plan to rewrite it.",
            CorpusFileStatus.Failed => TrimMessage(reason).Length == 0 ? "Could not write" : $"Could not write: {TrimMessage(reason)}",
            _ => "Not written yet. Approve the plan on Review Plan."
        };
    }

    public static string MissingFileText(string fileName) =>
        $"{fileName} is no longer in the case folder. Approve the plan again on Review Plan to rewrite it.";

    // --- Shared formatting ---

    /// <summary>
    /// Whole percentages without decimals ("20%"), otherwise one decimal ("19.6%"), rounded away from zero.
    /// </summary>
    public static string PercentText(decimal percent)
    {
        var rounded = Math.Round(percent, 1, MidpointRounding.AwayFromZero);
        return rounded == decimal.Truncate(rounded)
            ? rounded.ToString("0", Invariant) + "%"
            : rounded.ToString("0.0", Invariant) + "%";
    }

    private static string LevelName(ComplexityLevel level) => level switch
    {
        ComplexityLevel.Simple => "Simple",
        ComplexityLevel.Complex => "Complex",
        _ => "Standard"
    };

    /// <summary>
    /// Trims whitespace and trailing periods so templates can append their own punctuation.
    /// </summary>
    private static string TrimMessage(string? message) => (message ?? "").Trim().TrimEnd('.').TrimEnd();

    /// <summary>
    /// "{message}. " or "" when there is no message.
    /// </summary>
    private static string Sentence(string? message)
    {
        var trimmed = TrimMessage(message);
        return trimmed.Length == 0 ? "" : trimmed + ". ";
    }

    private static string N(int value) => value.ToString("N0", Invariant);

    private static string N(decimal value) => value.ToString("N0", Invariant);

    private static string Date(DateTime value) => value.ToString("MMM d, yyyy", Invariant);

    private static string Month(DateTime value) => value.ToString("MMM yyyy", Invariant);

    private static string DateTimeText(DateTime value) => value.ToString("MMM d, yyyy h:mm tt", Invariant);
}
