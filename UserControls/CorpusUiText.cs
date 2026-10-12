using System.Globalization;
using ReelDiscovery.Models;
using ReelDiscovery.Models.Case;
using ReelDiscovery.Models.Corpus;
using ReelDiscovery.Services.Corpus;

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

public enum PlanBannerVariant
{
    None,
    StaleGuide,
    SettingsChanged
}

public class PlanBannerContent
{
    public PlanBannerVariant Variant;
    public string Heading = "";
    public string Body = "";
    public string PrimaryText = "";
    public string SecondaryText = "";
}

public enum TileKind
{
    Emails,
    Threads,
    Relevant,
    Hot,
    Privileged,
    Attachments,
    Cost
}

/// <summary>
/// Color of a tile value: Warning/Danger only for the generation Warnings/Failed tiles above 0 (05-UI-SPEC U9).
/// </summary>
public enum TileTone
{
    Normal,
    Warning,
    Danger
}

public class TileContent
{
    public string Caption = "";
    public string Value = "";
    public string SubLine = "";
    public string Tooltip = "";
    public TileTone Tone = TileTone.Normal;
}

/// <summary>
/// What entering Review Plan does (UI-SPEC S2 entry rules; reopen and unreadable-plan cards added).
/// </summary>
public enum ReviewPlanEntry
{
    KeepRun,
    AutoPlan,
    AutoReplan,
    SettingsBanner,
    StaleBanner,
    ShowPlan,
    NoPlanCard,
    UnreadableCard
}

public class ReviewPlanEntryInput
{
    public bool Running;
    public bool HasPlan;

    // Non-empty when the saved plan could not be loaded
    public string UnreadableReason = "";
    public bool Approved;

    // plan.SettingsHash != settings.ComputeHash(); dismissed = that hash was kept with Keep This Plan
    public bool SettingsChanged;
    public bool SettingsDismissed;

    // plan.GuideHash != guide.ComputeHash(); dismissed = that hash was kept with Keep This Plan
    public bool GuideStale;
    public bool GuideDismissed;

    // One-shot: Next on Corpus Settings asked for planning (D-12); every other entry shows the No corpus plan yet card (09 W2)
    public bool PlanRequested;
}

public enum PlanStateCard
{
    Planning,
    NoPlan,
    Failed,
    Unreadable
}

public enum PlanNextGateKind
{
    Disabled,
    AskSkip,
    AskApprove,
    Proceed
}

public class PlanNextGateResult
{
    public PlanNextGateKind Kind;
    public string Title = "";
    public string Message = "";
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
            return "Click Next to plan every thread and message from these settings and the case guide. Nothing is written yet. To write discovery documents without emails, click Skip to Discovery Documents.";
        if (!approved)
            return "Changing these settings replaces the current plan when you click Next. Skip to Discovery Documents leaves the plan as it is.";

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

    /// <summary>
    /// D-RP body. generatedThreads &gt; 0 (threads already written for the current plan) adds the start-over warning (05-UI-SPEC S2).
    /// </summary>
    public static string ReplanBody(bool approved, int generatedThreads = 0)
    {
        const string body = "Builds a new plan from the case guide and the current corpus settings, and replaces this one. Every DocID changes. This makes new Story-model calls.";
        var text = approved
            ? body + " The answer key and review protocol in the case folder are rewritten when you approve the new plan."
            : body;
        if (generatedThreads > 0)
            text += $" {CaseGuideUiText.Plural(generatedThreads, "thread", "threads")} already generated for this plan will no longer match it. After re-planning, Generate Corpus asks you to start over.";
        return text;
    }

    // --- Case Summary corpus rows ---

    public const string NotPlannedLine = "Not planned yet";

    // Answer key line 2 before there is a plan to count rows from
    public const string AnswerKeyFilesLine = "answer-key.csv and answer-key.xlsx";

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

    // --- Review Plan: entry rules (D-12, D-25, D-26) ---

    /// <summary>
    /// First match wins: active run, unreadable plan, no plan (auto-plan only when Corpus Settings Next asked for it,
    /// otherwise the card), unapproved plan with changed settings (auto re-plan only when asked), stale guide (banner;
    /// wins over changed settings), changed settings (banner), otherwise show the plan. Nothing re-plans an approved
    /// plan silently (T-04-34), and only an explicit request starts a planning run (T-04-62, 09 W2).
    /// </summary>
    public static ReviewPlanEntry ReviewPlanEntryDecision(ReviewPlanEntryInput input)
    {
        if (input.Running)
            return ReviewPlanEntry.KeepRun;
        if (!string.IsNullOrWhiteSpace(input.UnreadableReason))
            return ReviewPlanEntry.UnreadableCard;
        if (!input.HasPlan)
            return input.PlanRequested ? ReviewPlanEntry.AutoPlan : ReviewPlanEntry.NoPlanCard;
        // Without a planning request the saved plan shows with the settings banner instead of re-planning (CR-02, 09 W2)
        if (!input.Approved && input.SettingsChanged && input.PlanRequested)
            return ReviewPlanEntry.AutoReplan;
        if (input.GuideStale && !input.GuideDismissed)
            return ReviewPlanEntry.StaleBanner;
        if (input.SettingsChanged && !input.SettingsDismissed)
            return ReviewPlanEntry.SettingsBanner;
        return ReviewPlanEntry.ShowPlan;
    }

    /// <summary>
    /// True when entering the step must set Approved = false and save: an approved plan behind a stale-guide banner (D-25).
    /// </summary>
    public static bool WithdrawsApproval(ReviewPlanEntryInput input) =>
        input.Approved && ReviewPlanEntryDecision(input) == ReviewPlanEntry.StaleBanner;

    // --- Review Plan: banner ---

    /// <summary>
    /// emails and percent are the plan's (earlier) settings; approved picks the SettingsChanged wording; null for None.
    /// </summary>
    public static PlanBannerContent? PlanBannerText(PlanBannerVariant variant, int emails, int percent, bool approved = true)
    {
        return variant switch
        {
            PlanBannerVariant.StaleGuide => new PlanBannerContent
            {
                Variant = variant,
                Heading = "The plan is out of date with the case guide.",
                Body = "The case guide changed after this plan was built. Re-plan builds a new plan from the current guide. Approval is withdrawn until you re-plan, or keep this plan and approve it again.",
                PrimaryText = "Re-plan...",
                SecondaryText = "Keep This Plan"
            },
            PlanBannerVariant.SettingsChanged when !approved => new PlanBannerContent
            {
                Variant = variant,
                Heading = "The corpus settings changed after this plan was built.",
                Body = $"This plan still uses the earlier settings ({CaseGuideUiText.Plural(emails, "email", "emails")}, {N(percent)}% relevant). Re-plan builds a new plan with the current settings. Keep This Plan keeps this plan as it is.",
                PrimaryText = "Re-plan...",
                SecondaryText = "Keep This Plan"
            },
            PlanBannerVariant.SettingsChanged => new PlanBannerContent
            {
                Variant = variant,
                Heading = "The corpus settings changed after this plan was approved.",
                Body = $"This plan still uses the earlier settings ({CaseGuideUiText.Plural(emails, "email", "emails")}, {N(percent)}% relevant). Re-plan builds a new plan with the current settings. Keep This Plan leaves the approved plan and its answer key as they are.",
                PrimaryText = "Re-plan...",
                SecondaryText = "Keep This Plan"
            },
            _ => null
        };
    }

    // --- Review Plan: tiles ---

    public static TileContent Tile(TileKind kind, PlanSummary summary, CostEstimate cost)
    {
        switch (kind)
        {
            case TileKind.Emails:
                return new TileContent
                {
                    Caption = "Emails",
                    Value = N(summary.Emails),
                    SubLine = CaseGuideUiText.Plural(summary.Custodians, "custodian", "custodians"),
                    Tooltip = $"{N(summary.Emails)} planned emails across {N(summary.Custodians)} custodian mailboxes"
                };
            case TileKind.Threads:
                var average = summary.Threads == 0 ? 0.0 : (double)summary.Emails / summary.Threads;
                return new TileContent
                {
                    Caption = "Threads",
                    Value = N(summary.Threads),
                    SubLine = $"avg {average.ToString("0.0", Invariant)} emails"
                };
            case TileKind.Relevant:
                return new TileContent
                {
                    Caption = "Relevant",
                    Value = PercentText(summary.RelevantPercent),
                    SubLine = CaseGuideUiText.Plural(summary.Relevant, "email", "emails"),
                    Tooltip = $"{N(summary.Relevant)} relevant and {N(summary.NonRelevant)} non-relevant emails"
                };
            case TileKind.Hot:
                return new TileContent
                {
                    Caption = "Hot",
                    Value = N(summary.HotEmails),
                    SubLine = CaseGuideUiText.Plural(summary.HotFacts, "hot fact", "hot facts"),
                    Tooltip = "Hot documents follow the facts marked Hot in the case guide."
                };
            case TileKind.Privileged:
                return new TileContent
                {
                    Caption = "Privileged",
                    Value = N(summary.PrivilegedThreads),
                    SubLine = CaseGuideUiText.Plural(summary.PrivilegedEmails, "email", "emails"),
                    Tooltip = "Privileged threads include in-house or outside counsel."
                };
            case TileKind.Attachments:
                return new TileContent
                {
                    Caption = "Attachments",
                    Value = N(summary.Attachments),
                    SubLine = $"on {PercentText(summary.AttachmentPercent)} of emails",
                    Tooltip = AttachmentTooltip(summary.AttachmentsByType)
                };
            default:
                var model = string.IsNullOrWhiteSpace(cost.ModelName) ? "the Story model" : cost.ModelName.Trim();
                return new TileContent
                {
                    Caption = "Est. cost",
                    Value = "≈" + Money(cost.Total),
                    SubLine = "to generate, ±50%",
                    Tooltip = $"Rough estimate for writing this corpus on {model}: relevant email {Money(cost.RelevantCost)}, other email {Money(cost.OtherCost)}, attachments {Money(cost.AttachmentCost)}. Images and voicemail are billed separately by OpenAI. Planning cost so far is in the cost ticker."
                };
        }
    }

    // --- Review Plan: stage checklist and progress ---

    public static string StageName(PlanStage stage) => stage switch
    {
        PlanStage.Allocating => "Allocating threads to the key facts",
        PlanStage.PlanningRelevant => "Planning relevant threads",
        PlanStage.PlanningOther => "Planning other email",
        PlanStage.WritingProtocol => "Writing the review protocol",
        _ => "Checking the plan"
    };

    public static string StageProgress(int done, int total) => $"{N(done)} of {N(total)}";

    /// <summary>
    /// Status strip message; relevant chunks and other-email batches show "(chunk/batch i of n)" when n > 0.
    /// </summary>
    public static string ProgressText(PlanStage stage, int index, int total)
    {
        var name = StageName(stage);
        if (total > 0 && stage == PlanStage.PlanningRelevant)
            return $"{name} (chunk {N(index)} of {N(total)})...";
        if (total > 0 && stage == PlanStage.PlanningOther)
            return $"{name} (batch {N(index)} of {N(total)})...";
        return name + "...";
    }

    // --- Review Plan: state cards ---

    public static StateCardContent StateCard(PlanStateCard card, string reason = "")
    {
        return card switch
        {
            PlanStateCard.Planning => new StateCardContent
            {
                Heading = "Planning the corpus...",
                Body = "Code fixes every email's date, people and labels first. Then the model writes a subject and purpose for each thread. This takes 1-2 minutes for 1,000 emails and up to 10 minutes for 5,000."
            },
            PlanStateCard.Failed => new StateCardContent
            {
                Heading = "The plan was not built",
                Body = Sentence(reason) + "Your corpus settings were kept. Click Retry Planning to try again, or Skip to Discovery Documents to continue without emails.",
                ButtonText = "Retry Planning",
                SecondaryText = "Skip to Discovery Documents"
            },
            PlanStateCard.Unreadable => new StateCardContent
            {
                Heading = "The saved plan could not be opened",
                Body = Sentence(reason) + "Click Plan Corpus to build a new plan from the case guide, or Skip to Discovery Documents to continue without emails.",
                ButtonText = "Plan Corpus",
                SecondaryText = "Skip to Discovery Documents"
            },
            _ => new StateCardContent
            {
                Heading = "No corpus plan yet",
                Body = "Click Plan Corpus to plan every thread and message from the case guide and your corpus settings. To write discovery documents without emails, click Skip to Discovery Documents.",
                ButtonText = "Plan Corpus",
                SecondaryText = "Skip to Discovery Documents"
            }
        };
    }

    // --- Review Plan: Checks card ---

    public const string NoChecksText = "No problems found. Every key fact has planned evidence, and every date and participant checks out.";
    public const string ChecksHint = "Warnings never block Approve Plan.";

    public static string ChecksCaption(int warnings) =>
        warnings <= 0 ? "Checks" : $"Checks ({CaseGuideUiText.Plural(warnings, "warning", "warnings")})";

    // --- Review Plan: outcome status lines ---

    public const string KeptStaleText = "Kept this plan. Click Approve Plan to write the answer key again.";
    public const string ApprovingText = "Writing the answer key and review protocol...";

    public static string ReadyText(int emails, int threads, int relevant)
    {
        var emailText = CaseGuideUiText.Plural(emails, "email", "emails");
        var threadText = CaseGuideUiText.Plural(threads, "thread", "threads");
        return $"Plan ready: {emailText} in {threadText}, {N(relevant)} relevant. Review it, then click Approve Plan.";
    }

    public static string ReadyWithWarningsText(int warnings) =>
        $"Plan ready with {CaseGuideUiText.Plural(warnings, "warning", "warnings")}. Review the Checks list, then click Approve Plan.";

    // Cancel confirmed after the planner had finished and the new plan was being saved (WR-09)
    public const string CancelTooLateText = "Planning had already finished, so the new plan was kept. Use Re-plan to build another.";

    public static string CancelledText(bool previousPlan) =>
        previousPlan
            ? "Planning cancelled. The previous plan is still in place."
            : "Planning cancelled. Your corpus settings were kept.";

    public static string FatalText(string message) =>
        $"Planning stopped{Colon(message)}. Fix the problem on the API Configuration step, then click Retry Planning.";

    public static string RefusedText(int refused, int total, string message)
    {
        var why = TrimMessage(message);
        var detail = why.Length == 0 ? "" : $" ({why})";
        return $"Planning stopped: the model declined {N(refused)} of {N(total)} thread batches{detail}. Click Retry Planning, or Re-plan with guidance that softens the wording.";
    }

    public static string ApprovedText(string folderName) =>
        $"Plan approved. Wrote answer-key.csv, answer-key.xlsx and {ProtocolFileName(folderName)} to the case folder.";

    public static string ApproveFileOpenText(string fileName) => $"Close {fileName}, then click Approve Plan again.";

    public static string ApproveFailedText(string reason) =>
        $"Could not write the answer key{Colon(reason)}. Check that the case folder is writable, then click Approve Plan again.";

    // --- Review Plan: Next gate, confirmations, badge ---

    public static PlanNextGateResult PlanNextGate(bool running, bool hasPlan, bool approved)
    {
        if (running)
            return new PlanNextGateResult { Kind = PlanNextGateKind.Disabled };
        if (!hasPlan)
        {
            return new PlanNextGateResult
            {
                Kind = PlanNextGateKind.AskSkip,
                Title = "Continue Without a Plan",
                Message = "Continue without a corpus plan? Generate Corpus needs an approved plan before it writes emails; click Next there to write discovery documents. You can plan the corpus later from Review Plan."
            };
        }

        if (!approved)
        {
            return new PlanNextGateResult
            {
                Kind = PlanNextGateKind.AskApprove,
                Title = "Approve the Plan",
                Message = "Approve this plan and continue? Approve Plan writes the answer key (CSV and Excel) and the review protocol to the case folder."
            };
        }

        return new PlanNextGateResult { Kind = PlanNextGateKind.Proceed };
    }

    public static (string Title, string Text) CP1CancelPlanning(bool previousPlan)
    {
        const string text = "Stop planning? The partial plan is discarded. Your corpus settings are kept.";
        return ("Cancel Planning", previousPlan ? text + " The previous plan stays in place." : text);
    }

    public static (string Title, string Text) CP2ClosePlanning() =>
        ("Stop Planning?", "Planning is still running. Close and stop it? The partial plan is discarded; your settings are saved.");

    public static string ApprovalBadgeText(DateTime approvedLocal) =>
        "Approved " + approvedLocal.ToString("MMM d, h:mm tt", Invariant);

    // --- Shared formatting ---

    /// <summary>
    /// "{n} documents, {n} calendar invites, {n} images, {n} voicemails", listing only types with a count;
    /// Word, Excel and PowerPoint together are documents.
    /// </summary>
    private static string AttachmentTooltip(IReadOnlyDictionary<AttachmentType, int> byType)
    {
        int Count(AttachmentType type) => byType.TryGetValue(type, out var n) ? Math.Max(0, n) : 0;

        var docs = Count(AttachmentType.Word) + Count(AttachmentType.Excel) + Count(AttachmentType.PowerPoint);
        var parts = new List<string>();
        if (docs > 0)
            parts.Add(CaseGuideUiText.Plural(docs, "document", "documents"));
        if (Count(AttachmentType.CalendarInvite) > 0)
            parts.Add(CaseGuideUiText.Plural(Count(AttachmentType.CalendarInvite), "calendar invite", "calendar invites"));
        if (Count(AttachmentType.Image) > 0)
            parts.Add(CaseGuideUiText.Plural(Count(AttachmentType.Image), "image", "images"));
        if (Count(AttachmentType.Voicemail) > 0)
            parts.Add(CaseGuideUiText.Plural(Count(AttachmentType.Voicemail), "voicemail", "voicemails"));

        return parts.Count == 0 ? "No attachments planned" : string.Join(", ", parts);
    }

    /// <summary>
    /// "$130" from $10 up, "$4.75" below (D-21).
    /// </summary>
    private static string Money(decimal amount) =>
        amount >= 10m
            ? "$" + Math.Round(amount, 0, MidpointRounding.AwayFromZero).ToString("N0", Invariant)
            : "$" + amount.ToString("0.00", Invariant);

    /// <summary>
    /// ": {message}" or "" when there is no message.
    /// </summary>
    private static string Colon(string? message)
    {
        var trimmed = TrimMessage(message);
        return trimmed.Length == 0 ? "" : ": " + trimmed;
    }

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

    private static string Date(DateTime value) => value.ToString("MMM d, yyyy", Invariant);

    private static string Month(DateTime value) => value.ToString("MMM yyyy", Invariant);

    private static string DateTimeText(DateTime value) => value.ToString("MMM d, yyyy h:mm tt", Invariant);
}
