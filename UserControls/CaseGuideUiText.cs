using System.Globalization;
using ReelDiscovery.Models.Case;
using ReelDiscovery.Services.Case;

namespace ReelDiscovery.UserControls;

public enum GuideTabState
{
    Writing,
    Waiting,
    Failed,
    NotWritten,
    NoGuide
}

public enum BannerVariant
{
    None,
    StaleCore,
    StaleDownstream,
    NeedsReview
}

public enum NextGateKind
{
    Disabled,
    Unfinished,
    Errors,
    Warnings,
    Proceed
}

public class StateCardContent
{
    public string Heading = "";
    public string Body = "";
    public string ButtonText = "";
    public bool ShowBar;
}

public class BannerContent
{
    public BannerVariant Variant;
    public string Heading = "";
    public string Body = "";
    public string PrimaryText = "";
    public string SecondaryText = "";
}

public class NextGateResult
{
    public NextGateKind Kind;
    public string Title = "";
    public string Message = "";
}

/// <summary>
/// The Checks card's Fix button for the selected check: Primary "Fix" for an automatic fix, the matching action
/// ("Regenerate Cast...", "Update Guide") for a model fix, disabled "Fix" otherwise.
/// </summary>
public class FixButtonState
{
    public bool Enabled;
    public bool Primary;
    public string Text = "";
}

/// <summary>
/// One error in the M2 gate dialog: what is wrong and how to fix it.
/// </summary>
public class GateErrorLine
{
    public string Where = "";
    public string Message = "";
    public string FixHint = "";
}

/// <summary>
/// M2 (errors block Next) dialog: up to five errors with their fixes, the "+ n more" line and the buttons.
/// FixAutomaticallyText is empty when no error has an automatic fix.
/// </summary>
public class ErrorsGateContent
{
    public string Title = "";
    public string Headline = "";
    public string Body = "";
    public List<GateErrorLine> Lines = new();
    public string MoreText = "";
    public string ShowChecksText = "";
    public string FixAutomaticallyText = "";
    public string ContinueAnywayText = "";
}

/// <summary>
/// Fix All confirmation: one line per kind of automatic fix with its count.
/// </summary>
public class FixAllContent
{
    public string Title = "";
    public string Headline = "";
    public string Body = "";
    public List<string> Lines = new();
    public string Foot = "";
    public string ConfirmText = "";
}

/// <summary>
/// Every Case Guide / Case Summary / Describe the Case string that carries a count, a part name or a
/// state (03-UI-SPEC Copywriting Contract). Pure functions: no controls, no colors.
/// </summary>
public static class CaseGuideUiText
{
    public const string NoChecksText = "No problems found. Every fact links to existing issues and characters, and every date is in range.";
    public const string ChecksRunningText = "Checks — run as each part finishes";
    public const string CancelledText = "Generation cancelled. Finished parts were kept.";
    public const string NoRecentCasesText = "No saved cases yet. A case is saved automatically once its core document is written.";
    public const string CaseFileDescription = "case.json · issues, cast, key facts, canonical values and the core document";

    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    // --- Counts and part names ---

    /// <summary>
    /// "{n:N0} {word}" with the singular only for exactly 1 (invariant culture).
    /// </summary>
    public static string Plural(int n, string singular, string plural)
    {
        return n.ToString("N0", Invariant) + " " + (n == 1 ? singular : plural);
    }

    public static string PartNoun(GuidePart part, bool capitalized)
    {
        return part switch
        {
            GuidePart.Issues => capitalized ? "Issues" : "issues",
            GuidePart.Cast => capitalized ? "Cast" : "cast",
            GuidePart.Facts => capitalized ? "Key Facts" : "key facts",
            GuidePart.Values => capitalized ? "Canonical Values" : "canonical values",
            _ => capitalized ? "Part" : "part"
        };
    }

    /// <summary>
    /// "The cast was ..." but "the key facts were ...".
    /// </summary>
    public static bool IsPluralPart(GuidePart part) => part != GuidePart.Cast;

    /// <summary>
    /// "the issues and the cast", "the issues, the cast and the key facts" (guide order).
    /// </summary>
    public static string JoinParts(IReadOnlyList<GuidePart> parts)
    {
        var names = Ordered(parts).Select(p => "the " + PartNoun(p, false)).ToList();
        return JoinWithAnd(names);
    }

    // --- Tabs, state cards, checks ---

    public static string TabCaption(GuidePart part, int count, GuidePartStatus status)
    {
        var name = PartNoun(part, true);
        return status switch
        {
            GuidePartStatus.Ready => $"{name} ({count.ToString("N0", Invariant)})",
            GuidePartStatus.Generating => $"{name} (writing)",
            GuidePartStatus.Failed => $"{name} (failed)",
            _ => name
        };
    }

    public static StateCardContent StateCardText(GuidePart part, GuideTabState state, string reason = "")
    {
        var noun = PartNoun(part, false);
        var name = PartNoun(part, true);
        switch (state)
        {
            case GuideTabState.Writing:
                return new StateCardContent
                {
                    Heading = $"Writing the {noun}...",
                    Body = WritingBody(part),
                    ShowBar = true
                };
            case GuideTabState.Waiting:
                var previous = PreviousNoun(part);
                return new StateCardContent
                {
                    Heading = $"Waiting for the {previous}",
                    Body = $"The {noun} {(IsPluralPart(part) ? "are" : "is")} written after the {previous}. This tab fills in automatically."
                };
            case GuideTabState.Failed:
                return new StateCardContent
                {
                    Heading = $"The {noun} {(IsPluralPart(part) ? "were" : "was")} not written",
                    Body = Sentence(reason) + $"Click Retry {name} to try again. Finished parts were kept.",
                    ButtonText = $"Retry {name}"
                };
            case GuideTabState.NotWritten:
                return new StateCardContent
                {
                    Heading = $"{name} not written yet",
                    Body = "Click Resume Guide to write the rest of the guide. Finished parts were kept.",
                    ButtonText = "Resume Guide"
                };
            default:
                return new StateCardContent
                {
                    Heading = "No case guide yet",
                    Body = "Click Generate Guide to build the issues, cast, key facts and canonical values from the core document.",
                    ButtonText = "Generate Guide"
                };
        }
    }

    public static string ChecksCaption(int errors, int warnings, bool running)
    {
        if (running)
            return ChecksRunningText;
        return $"Checks ({Plural(errors, "error", "errors")}, {Plural(warnings, "warning", "warnings")})";
    }

    // --- Stale / needs-review banner ---

    /// <summary>
    /// Stale against the core document (Issues or Cast) wins over downstream staleness, which wins over needs-review.
    /// </summary>
    public static BannerContent StaleBannerText(IReadOnlyList<GuidePart> staleParts, int needsReviewCount)
    {
        var stale = Ordered(staleParts);
        if (stale.Contains(GuidePart.Issues) || stale.Contains(GuidePart.Cast))
        {
            return new BannerContent
            {
                Variant = BannerVariant.StaleCore,
                Heading = "The guide is out of date with the core document.",
                Body = $"The core document changed after {JoinParts(stale)} {WasWere(stale)} written. Update Guide rewrites those parts in order and keeps the entries you edited.",
                PrimaryText = "Update Guide",
                SecondaryText = "Not Now"
            };
        }

        if (stale.Count > 0)
        {
            var upstreamPart = stale[0] == GuidePart.Values ? GuidePart.Facts : GuidePart.Cast;
            var upstream = PartNoun(upstreamPart, false);
            var names = JoinWithAnd(stale.Select(p => PartNoun(p, true)).ToList());
            return new BannerContent
            {
                Variant = BannerVariant.StaleDownstream,
                Heading = $"{names} {(stale.Count > 1 || IsPluralPart(stale[0]) ? "are" : "is")} out of date with the {upstream}.",
                Body = $"The {upstream} {(IsPluralPart(upstreamPart) ? "were" : "was")} regenerated after {JoinParts(stale)} {WasWere(stale)} written. Update Guide rewrites them and keeps the entries you edited.",
                PrimaryText = "Update Guide",
                SecondaryText = "Not Now"
            };
        }

        if (needsReviewCount > 0)
        {
            return new BannerContent
            {
                Variant = BannerVariant.NeedsReview,
                Heading = $"{Plural(needsReviewCount, "fact", "facts")} {(needsReviewCount == 1 ? "references" : "reference")} characters you changed.",
                Body = "Check that each one still reads correctly, then mark it reviewed.",
                PrimaryText = "Review Facts"
            };
        }

        return new BannerContent { Variant = BannerVariant.None };
    }

    // --- Next gate (M1-M3) ---

    public static NextGateResult NextGateDecision(bool running, IReadOnlyList<GuidePart> unfinished, int errors, int warnings)
    {
        if (running)
            return new NextGateResult { Kind = NextGateKind.Disabled };

        if (unfinished.Count > 0)
        {
            var parts = Ordered(unfinished);
            var plural = parts.Count > 1 || IsPluralPart(parts[0]);
            return new NextGateResult
            {
                Kind = NextGateKind.Unfinished,
                Title = "Guide Not Finished",
                Message = $"The guide is not finished: {JoinParts(parts)} {(plural ? "have" : "has")} not been written. {ResumeHint(parts)}, then click Next again."
            };
        }

        if (errors > 0)
        {
            return new NextGateResult
            {
                Kind = NextGateKind.Errors,
                Title = "Fix the Guide First",
                Message = $"The case guide has {Plural(errors, "error", "errors")} (listed under Checks). Later steps rely on these links and dates. Fix {(errors == 1 ? "it" : "them")}, then click Next again."
            };
        }

        if (warnings > 0)
        {
            return new NextGateResult
            {
                Kind = NextGateKind.Warnings,
                Title = "Review Warnings",
                Message = $"{Plural(warnings, "warning", "warnings")} {(warnings == 1 ? "remains" : "remain")} (listed under Checks). Continue anyway?"
            };
        }

        return new NextGateResult { Kind = NextGateKind.Proceed };
    }

    /// <summary>
    /// "Click Resume Guide to write the key facts" (one part) or "... to write them" (several); the M1 gate and the
    /// unwritten-part checks share it, so both say how to finish the guide the same way.
    /// </summary>
    public static string ResumeHint(IReadOnlyList<GuidePart> parts)
    {
        var ordered = Ordered(parts);
        if (ordered.Count == 0)
            return "Click Resume Guide";
        return ordered.Count == 1
            ? $"Click Resume Guide to write the {PartNoun(ordered[0], false)}"
            : "Click Resume Guide to write them";
    }

    // --- Checks fixes (Fix, Fix All, the M2 gate dialog) ---

    public const string ChecksHintText = "Double-click a check to go to it.";
    public const string FixButtonText = "Fix";
    public const string FixAllTitle = "Fix All";
    public const string ShowChecksText = "Show Checks";
    public const string ContinueAnywayText = "Continue Anyway";
    public const string NothingFixedText = "Nothing changed: the check was already cleared.";

    // The M2 dialog lists at most this many errors, then "+ n more under Checks"
    public const int GateErrorLimit = 5;

    public static string FixAllButtonText(int n) => $"Fix All ({n.ToString("N0", Invariant)})";

    public static string FixAutomaticallyText(int n) => $"Fix Automatically ({n.ToString("N0", Invariant)})";

    /// <summary>
    /// Text of the button that runs a finding's fix: "Fix" for an automatic one, otherwise the existing action it starts
    /// (an ellipsis when that action opens a dialog). Empty for None.
    /// </summary>
    public static string QuickFixActionText(GuideQuickFix fix, GuideArea area)
    {
        if (CaseGuideQuickFix.IsAutomatic(fix))
            return FixButtonText;

        return fix switch
        {
            GuideQuickFix.RegenerateEntry => area switch
            {
                GuideArea.Cast => "Regenerate Character",
                GuideArea.Values => "Regenerate Value",
                _ => "Regenerate Fact"
            },
            GuideQuickFix.RegenerateCast => "Regenerate Cast...",
            GuideQuickFix.RegenerateValues => "Regenerate Values...",
            GuideQuickFix.RetryIssueDetails => "Retry Issue Details",
            GuideQuickFix.UpdateGuide => "Update Guide",
            GuideQuickFix.ResumeGuide => "Resume Guide",
            _ => string.Empty
        };
    }

    /// <summary>
    /// Fix button for the selected check (null when none is selected). Disabled during a run and for checks without a fix.
    /// </summary>
    public static FixButtonState FixButtonFor(GuideFinding? selected, bool running, bool otherPrimaryShown = false)
    {
        var text = selected == null ? string.Empty : QuickFixActionText(selected.QuickFix, selected.Area);
        if (text.Length == 0)
            return new FixButtonState { Text = FixButtonText };

        return new FixButtonState
        {
            Text = text,
            Enabled = !running,
            // At most two Primary buttons show at once (UI-SPEC): with the banner's Update Guide up, Fix stays Default
            Primary = !running && selected!.HasAutomaticFix && !otherPrimaryShown
        };
    }

    /// <summary>
    /// How many listed checks Fix All clears (automatic fixes only).
    /// </summary>
    public static int AutomaticFixCount(IEnumerable<GuideFinding> findings) =>
        (findings ?? Enumerable.Empty<GuideFinding>()).Count(f => f != null && f.HasAutomaticFix);

    /// <summary>
    /// One Fix All confirmation line: "Widen or repair 3 active ranges".
    /// </summary>
    public static string FixAllLine(GuideQuickFix fix, int n) => fix switch
    {
        GuideQuickFix.AssignNewId => $"Give {Plural(n, "entry", "entries")} a new ID",
        GuideQuickFix.RemoveBrokenLinks => $"Remove broken links from {Plural(n, "entry", "entries")}",
        GuideQuickFix.WidenActiveRange => $"Widen or repair {Plural(n, "active range", "active ranges")}",
        GuideQuickFix.NormalizeDate => $"Rewrite {Plural(n, "date", "dates")} as YYYY-MM-DD",
        GuideQuickFix.ResetEmail => $"Rebuild {Plural(n, "email address", "email addresses")} from the name and organization",
        GuideQuickFix.NewEmailDomain => $"Give {Plural(n, "organization", "organizations")} a domain of {(n == 1 ? "its" : "their")} own",
        GuideQuickFix.AddActorsToKnownBy => $"Add {Plural(n, "person", "people")} under Who knew",
        GuideQuickFix.ReplaceRealNames => $"Replace {Plural(n, "real name", "real names")} with the fictional ones",
        GuideQuickFix.LinkParty => $"Link {Plural(n, "party", "parties")} to the matching name in the guide",
        GuideQuickFix.MarkReviewed => $"Mark {Plural(n, "entry", "entries")} reviewed (the text stays as it is)",
        _ => string.Empty
    };

    /// <summary>
    /// Fix All confirmation (default Cancel). <paramref name="counts"/> comes from CaseGuideQuickFix.CountAutomatic.
    /// </summary>
    public static FixAllContent FixAllText(IReadOnlyList<(GuideQuickFix Fix, int Count)> counts)
    {
        var total = counts.Sum(c => c.Count);
        return new FixAllContent
        {
            Title = FixAllTitle,
            Headline = $"Fix {Plural(total, "check", "checks")} automatically?",
            Body = "These changes are made directly in the guide, with no model calls:",
            Lines = counts.Where(c => c.Count > 0).Select(c => FixAllLine(c.Fix, c.Count)).Where(l => l.Length > 0).ToList(),
            Foot = "Checks that need your judgment or a model call stay listed. Every change can be edited afterward.",
            ConfirmText = FixAllTitle
        };
    }

    /// <summary>
    /// M2 (errors block Next): up to five errors with how to fix each, "+ n more under Checks", and the buttons
    /// Show Checks (the safe default), Continue Anyway and, when some errors fix automatically, Fix Automatically ({k}).
    /// </summary>
    public static ErrorsGateContent ErrorsGateText(IReadOnlyList<GuideFinding> errors)
    {
        var list = (errors ?? Array.Empty<GuideFinding>()).Where(f => f != null).ToList();
        var automatic = AutomaticFixCount(list);
        var more = list.Count - GateErrorLimit;
        return new ErrorsGateContent
        {
            Title = "Fix the Guide First",
            Headline = $"The case guide has {Plural(list.Count, "error", "errors")}",
            Body = $"Later steps rely on these links and dates. Here is how to fix {(list.Count == 1 ? "it" : "them")}:",
            Lines = list.Take(GateErrorLimit).Select(f => new GateErrorLine { Where = f.Where, Message = f.Message, FixHint = f.FixHint }).ToList(),
            MoreText = more > 0 ? $"+ {more.ToString("N0", Invariant)} more under Checks" : string.Empty,
            ShowChecksText = ShowChecksText,
            FixAutomaticallyText = automatic > 0 ? FixAutomaticallyText(automatic) : string.Empty,
            ContinueAnywayText = ContinueAnywayText
        };
    }

    /// <summary>
    /// Continue Anyway on the M2 dialog asks once more (default No).
    /// </summary>
    public static (string Title, string Text) C15ContinueWithErrors(int errors) =>
        (ContinueAnywayText,
            $"The guide still has {Plural(errors, "error", "errors")}. Later steps may produce inconsistent emails. Continue anyway?");

    /// <summary>
    /// Status line after Fix / Fix All / Fix Automatically.
    /// </summary>
    public static string FixedChecksText(int fixedCount, int errorsLeft, int warningsLeft)
    {
        if (fixedCount <= 0)
            return NothingFixedText;

        var text = $"Fixed {Plural(fixedCount, "check", "checks")}.";
        if (errorsLeft == 0 && warningsLeft == 0)
            return text + " No problems remain.";

        var left = errorsLeft > 0 && warningsLeft > 0
            ? $"{Plural(errorsLeft, "error", "errors")} and {Plural(warningsLeft, "warning", "warnings")}"
            : errorsLeft > 0 ? Plural(errorsLeft, "error", "errors") : Plural(warningsLeft, "warning", "warnings");
        return $"{text} {left} {(errorsLeft + warningsLeft == 1 ? "remains" : "remain")} under Checks.";
    }

    // --- Paths, subtitle, recent cases, Case Summary lines ---

    /// <summary>
    /// Replaces the My Documents prefix (whole folder names only) with "Documents"; other paths are unchanged.
    /// </summary>
    public static string DisplayPath(string fullPath, string myDocumentsPath)
    {
        if (string.IsNullOrEmpty(fullPath) || string.IsNullOrEmpty(myDocumentsPath))
            return fullPath ?? "";

        var root = myDocumentsPath.TrimEnd('\\', '/');
        if (root.Length == 0 || !fullPath.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            return fullPath;
        if (fullPath.Length == root.Length)
            return "Documents";
        var next = fullPath[root.Length];
        if (next != '\\' && next != '/')
            return fullPath;
        return "Documents" + fullPath[root.Length..];
    }

    public static string SubtitleText(string folderPath, DateTime? savedLocal, bool saveFailed, string myDocumentsPath)
    {
        if (string.IsNullOrWhiteSpace(folderPath))
            return "Not saved yet";
        var path = DisplayPath(folderPath, myDocumentsPath);
        if (saveFailed)
            return $"{path} · Not saved";
        return savedLocal.HasValue ? $"{path} · Saved {Time(savedLocal.Value)}" : path;
    }

    public static string StageLabel(CaseStage stage)
    {
        return stage switch
        {
            CaseStage.CaseGuide => "Case guide",
            CaseStage.CorpusSettings => "Corpus settings",
            CaseStage.ReviewPlan => "Plan review",
            CaseStage.Summary => "Complete",
            _ => "Core document"
        };
    }

    public static string RecentItemSecondLine(CaseStage stage, DateTime openedLocal, bool isOpen)
    {
        return isOpen
            ? $"{StageLabel(stage)} · open now"
            : $"{StageLabel(stage)} · opened {Date(openedLocal)}";
    }

    public static string SaveLine(DateTime? savedLocal, string error)
    {
        if (!string.IsNullOrWhiteSpace(error))
            return $"Not saved: {TrimMessage(error)}";
        return savedLocal.HasValue ? $"Saved {Time(savedLocal.Value)}" : "Not saved yet";
    }

    public static string LastSavedLine(DateTime savedLocal) =>
        $"Last saved {Date(savedLocal)} {Time(savedLocal)}";

    public static string ExportLine(DateTime? exportedLocal, string fileName)
    {
        return exportedLocal.HasValue
            ? $"Exported {Time(exportedLocal.Value)} · {fileName}"
            : "Not exported yet";
    }

    public static string SummaryCoreDocumentLine(string documentKind, int sections, int paragraphs) =>
        $"{documentKind} · {Plural(sections, "section", "sections")} · {Plural(paragraphs, "paragraph", "paragraphs")}";

    public static string SummaryCastLine(int keyCount, int backgroundCount, int organizations) =>
        $"{KeyAndBackground(keyCount, backgroundCount)} · {Plural(organizations, "organization", "organizations")}";

    public static string SummaryFactsLine(int facts, DateTime? first, DateTime? last)
    {
        var count = Plural(facts, "fact", "facts");
        if (facts == 0 || !first.HasValue || !last.HasValue)
            return count;
        return $"{count} · {Date(first.Value)} to {Date(last.Value)}";
    }

    public static string SummaryValuesLine(int values) =>
        $"{Plural(values, "value", "values")} · saved in case.json";

    public static string SummaryChecksLine(int errors, int warnings) =>
        errors == 0 && warnings == 0
            ? "No problems found"
            : $"{Plural(errors, "error", "errors")}, {Plural(warnings, "warning", "warnings")}";

    // --- Grid card headers and detail lines ---

    public static string IssuesSourceText(bool memo) =>
        memo ? "From the memo's findings" : "From the core document's counts";

    public static string CastCountText(int keyCount, int backgroundCount) =>
        $"{keyCount.ToString("N0", Invariant)} key · {backgroundCount.ToString("N0", Invariant)} background";

    public static string FactsCountText(int facts, int needsReview)
    {
        var text = Plural(facts, "fact", "facts");
        if (needsReview > 0)
            text += $" · {needsReview.ToString("N0", Invariant)} {(needsReview == 1 ? "needs" : "need")} review";
        return text;
    }

    public static string ValuesCountText(int values) => Plural(values, "value", "values");

    public static string SelectedCountText(int n) => $"{n.ToString("N0", Invariant)} selected";

    public static string LinkedFactsText(int n) =>
        n == 0 ? "Not in any fact yet." : $"Appears in {Plural(n, "fact", "facts")}.";

    public static string MoreLinkedText(int n) => $"and {n.ToString("N0", Invariant)} more";

    public static string EmptyDetailText(GuidePart part)
    {
        var item = part switch
        {
            GuidePart.Issues => "an issue",
            GuidePart.Cast => "a character",
            GuidePart.Facts => "a fact",
            _ => "a value"
        };
        return $"Select {item} to see and edit the details.";
    }

    public static string EmptyGridText(GuidePart part)
    {
        return part switch
        {
            GuidePart.Issues => "No issues. Issues come from the core document's counts or findings.",
            GuidePart.Cast => "No characters. Click Add Character or Regenerate Cast...",
            GuidePart.Facts => "No key facts. Click Add Fact or Regenerate Facts...",
            _ => "No canonical values. Click Add Value or Regenerate Values..."
        };
    }

    /// <summary>
    /// Shown when a grid filter hides every row ("Needs review" on Key Facts, "Key"/"Background" on Cast).
    /// </summary>
    public static string NoFilterMatchText(GuidePart part, bool keyFilter = false)
    {
        if (part == GuidePart.Facts)
            return "No facts need review.";
        return keyFilter ? "No key characters." : "No background characters.";
    }

    public static string NeedsReviewNoticeText(string name) =>
        $"Review this fact: {name} was changed after it was written.";

    // --- Progress messages ---

    public static string ProgressIssuesExtract(bool memo) =>
        memo ? "Extracting the issues from the findings..." : "Extracting the issues from the counts...";

    public static string ProgressIssueDetails() => "Writing the issue details...";

    public static string ProgressCastRoster() => "Writing the cast: organizations and key characters...";

    public static string ProgressCastDetails(int first, int last, int total) =>
        $"Writing the cast: details for characters {first.ToString(Invariant)}-{last.ToString(Invariant)} of {total.ToString(Invariant)}...";

    public static string ProgressBackground(string organization, int index, int total) =>
        $"Writing the cast: background staff for {organization} ({index.ToString(Invariant)} of {total.ToString(Invariant)})...";

    public static string ProgressFacts(string periodHeading, int index, int total) =>
        $"Writing the key facts: {periodHeading} ({index.ToString(Invariant)} of {total.ToString(Invariant)})...";

    public static string ProgressValues() => "Writing the canonical values...";

    public static string ProgressChecking() => "Checking the guide...";

    // --- Outcome and status lines ---

    public static string GuideReadyText(int issues, int keyCount, int backgroundCount, int facts, int values) =>
        $"Guide ready: {Plural(issues, "issue", "issues")}, {KeyAndBackground(keyCount, backgroundCount)}, {Plural(facts, "fact", "facts")}, {Plural(values, "value", "values")}.";

    public static string GuideReadyWithFindingsText(int errors, int warnings) =>
        $"Guide ready with {Plural(errors, "error", "errors")} and {Plural(warnings, "warning", "warnings")}. Review the Checks list.";

    public static string FatalText(string message) =>
        $"Generation stopped{Colon(message)}. Finished parts were kept. Fix the problem on the API Configuration step, then click Resume Guide.";

    public static string RefusedText(GuidePart part, string message) =>
        $"The model declined to write the {PartNoun(part, false)}{Colon(message)}. Go Back and soften the core document's wording, or click Retry {PartNoun(part, true)} with guidance.";

    public static string PartFailedText(GuidePart part, string message) =>
        $"The {PartNoun(part, false)} failed{Colon(message)}. Click Retry {PartNoun(part, true)}.";

    public static string EntryRegenFailedText(string name, string message) =>
        $"{name} was not regenerated{Colon(message)}. The previous text was kept.";

    public static string SaveFailedText(string reason) =>
        $"Not saved{Colon(reason)}. Your work is still open and will be saved again on the next change.";

    public static string DeletedWithLinksText(string name, int factCount, string id)
    {
        var one = factCount == 1;
        return $"Deleted {name}. {Plural(factCount, "fact", "facts")} still {(one ? "points" : "point")} to {id}; {(one ? "it is" : "they are")} listed under Checks.";
    }

    public static string FactsMarkedForReviewText(int factCount, string name)
    {
        var one = factCount == 1;
        return $"{Plural(factCount, "fact", "facts")} {(one ? "references" : "reference")} {name} and {(one ? "was" : "were")} marked for review.";
    }

    public static string WritingEntityText(string name) => $"Writing {name}...";

    public static string OpenedCaseText(string title) => $"Opened {title}.";

    public static string ExportedCountText(int documents) =>
        $"Exported {Plural(documents, "document", "documents")} to the case folder.";

    public static string ExportingText(string document) => $"Exporting the {document}...";

    public static string ExportFileOpenText(string fileName) => $"Close {fileName} in Word, then click Export Again.";

    public static string ExportFailedText(string document, string reason) =>
        $"Could not export the {document}{Colon(reason)}. Check that the case folder is writable, then click Export Again.";

    public static string MoveFailedText(string reason) =>
        $"Could not move the case folder{Colon(reason)}. Close any files or Explorer windows open in it, then try again.";

    public static string MovedText(string displayPath) => $"Moved the case folder to {displayPath}.";

    // WR-05: the case moved, but files open elsewhere kept part of the old folder
    public static string MovedWithLeftoverText(string displayPath, string leftoverPath) =>
        $"Moved the case folder to {displayPath}. Some files in {leftoverPath} were open and could not be removed; close them and delete that folder.";

    public static string NewCaseText(string previousTitle) =>
        $"Started a new case. {previousTitle} is saved and listed under Recent cases.";

    // --- Confirmations (C1-C12): (Title, Text) for MessageBox ---

    public static (string Title, string Text) C1UpdateGuide(IReadOnlyList<GuidePart> parts)
    {
        var ordered = Ordered(parts);
        var verb = ordered.Count > 1 || (ordered.Count == 1 && IsPluralPart(ordered[0])) ? "are" : "is";
        var list = ordered.Count == 0 ? "the parts" : JoinParts(ordered);
        return ("Update Guide",
            $"Update the guide? {Capitalize(list)} that {verb} out of date will be rewritten. Entries you edited are kept. This makes new Story-model calls.");
    }

    public static (string Title, string Text) C2CancelGeneration() =>
        ("Cancel Generation", "Stop generating? Finished parts will be kept.");

    public static (string Title, string Text) C3RegenerateEdited(string noun, string name) =>
        ($"Regenerate {Capitalize(noun)}", $"Regenerate {name}? Your edits to this {noun.ToLowerInvariant()} will be replaced.");

    public static (string Title, string Text) C4DeleteCharacter(string name, string id) =>
        ("Delete Character", $"Delete {name} ({id})? This cannot be undone.");

    public static (string Title, string Text) C5DeleteFact(string id, string title, int valuesUsing)
    {
        var text = $"Delete {id} \"{title}\"?";
        if (valuesUsing > 0)
        {
            text += $" It is used by {Plural(valuesUsing, "canonical value", "canonical values")}; " +
                    (valuesUsing == 1 ? "that link will be removed." : "those links will be removed.");
        }
        return ("Delete Fact", text + " This cannot be undone.");
    }

    public static (string Title, string Text) C6DeleteValue(string label, string value) =>
        ("Delete Value", $"Delete {label} ({value})? This cannot be undone.");

    public static (string Title, string Text) C7MoveFolder(string displayTarget) =>
        ("Move Case Folder", $"Move the case folder to {displayTarget}? Close any of its files that are open in Word first.");

    public static (string Title, string Text) C8OpenOverUnsaved(string caseTitle) =>
        ("Open Case", $"Open {caseTitle}? The scenario you typed has not been saved and will be discarded.");

    public static (string Title, string Text) C9RegenerateDocumentWithGuide() =>
        ("Regenerate Document",
            "Regenerate the whole document? Every section, including your edits, will be replaced, and the case guide will be marked out of date. This makes a new set of Story-model calls.");

    public static (string Title, string Text) C10CloseAfterFailedSave(string reason, DateTime? lastSavedLocal)
    {
        var lost = lastSavedLocal.HasValue
            ? $"Changes since {Time(lastSavedLocal.Value)} will be lost."
            : "Unsaved changes will be lost.";
        return ("Case Not Saved", $"The case could not be saved{Colon(reason)}. Close anyway? {lost}");
    }

    public static (string Title, string Text) C11CloseUnsavedScenario() =>
        ("Close Case", "This scenario has not been saved yet. A case is saved once its core document is written. Close anyway?");

    public static (string Title, string Text) C12CloseDuringRun() =>
        ("Stop Generating?", "Generation is still running. Close and stop it? Finished parts are saved.");

    // --- D1 RegenerateTabDialog copy ---

    /// <summary>
    /// "Cast" / "Facts" / "Values" (the short tab word used by the regenerate actions).
    /// </summary>
    public static string TabWord(GuidePart part)
    {
        return part switch
        {
            GuidePart.Cast => "Cast",
            GuidePart.Facts => "Facts",
            GuidePart.Values => "Values",
            _ => throw new ArgumentException("Issues cannot be regenerated as a tab.", nameof(part))
        };
    }

    public static string EntriesWord(GuidePart part) => part == GuidePart.Cast ? "characters" : TabWord(part).ToLowerInvariant();

    public static string RegenerateTabTitle(GuidePart part) => $"Regenerate {TabWord(part)}";

    public static string RegenerateTabHeadline(GuidePart part) => $"Regenerate the {TabWord(part).ToLowerInvariant()}";

    public static string RegenerateTabBody(GuidePart part)
    {
        var body = $"Rewrites the {EntriesWord(part)} you have not edited. Matching entries keep their IDs, so links from facts stay in place. This makes new Story-model calls.";
        if (part == GuidePart.Cast)
        {
            body += " Organizations are refreshed from the core document's parties, and background staff you have not edited are rewritten too. Facts that point to removed characters will be marked for review.";
        }
        else if (part == GuidePart.Values)
        {
            body += " Facts are not changed.";
        }
        return body;
    }

    public static string RegenerateGuidancePlaceholder(GuidePart part)
    {
        return part switch
        {
            GuidePart.Cast => "e.g. add a second associate at the outside firm",
            GuidePart.Facts => "e.g. show the cover-up starting in early March",
            _ => "e.g. use change-order numbers in the CO-0xx format"
        };
    }

    public static string ReplaceEditedText(GuidePart part, int editedCount)
    {
        var singular = part == GuidePart.Cast ? "character" : EntriesWord(part).TrimEnd('s');
        return $"Also replace my {Plural(editedCount, "edited " + singular, "edited " + EntriesWord(part))}";
    }

    // --- D2 DeleteReferencesDialog copy ---

    public static string DeleteLinkedBodyText(string name, string id, int factCount) =>
        $"{name} ({id}) is linked to {Plural(factCount, "fact", "facts")}:";

    public static string DeleteLinkedFootText(string firstName) =>
        $"Remove from Facts and Delete takes {firstName} out of these facts. Delete Only keeps the links; each one is listed under Checks until you fix it.";

    // --- Bulk fact actions (Key Facts selection bar, needs-review banner, Regenerate Selected dialog) ---

    public const string RegenerateSelectedButtonText = "Regenerate Selected...";
    public const string MarkAllReviewedButtonText = "Mark All Reviewed";
    public const string RegenerateSelectedTitle = "Regenerate Selected Facts";

    /// <summary>
    /// Selection bar line, e.g. "12 facts selected".
    /// </summary>
    public static string SelectedFactsText(int n) => $"{Plural(n, "fact", "facts")} selected";

    public static string RegenerateSelectedHeadline(int n) => $"Regenerate {Plural(n, "selected fact", "selected facts")}";

    public static string RegenerateSelectedBody(int editedCount)
    {
        var body = "Rewrites each fact in place with the current cast. Each fact keeps its ID, so canonical values that cite it stay linked and are marked for review. A rewritten fact no longer needs review.";
        if (editedCount > 0)
            body += " Facts you edited are kept unless you check the box below.";
        return body + " This makes one Story-model call per fact.";
    }

    public const string RegenerateSelectedPlaceholder = "e.g. keep the dates and use the new names from the cast";

    /// <summary>
    /// Status line after Regenerate Selected: "Regenerated 12 of 12 facts.", "Regenerated 10 of 12 facts; 2 failed: {reason}.
    /// Click Regenerate Selected to try again." (the failed facts are selected again), plus "Kept 2 facts you edited." when any were skipped.
    /// </summary>
    public static string RegeneratedFactsText(int regenerated, int attempted, int failed, string firstError, int skippedEdited)
    {
        var kept = skippedEdited > 0 ? $"Kept {Plural(skippedEdited, "fact", "facts")} you edited" : string.Empty;
        if (attempted == 0)
            return kept.Length > 0 ? kept + "; nothing was regenerated." : "Nothing was regenerated.";

        var text = $"Regenerated {regenerated.ToString("N0", Invariant)} of {Plural(attempted, "fact", "facts")}";
        if (failed > 0)
        {
            // One failed fact stays selected alone, so the detail card's button is the one to click
            var button = failed == 1 ? "Regenerate Fact" : "Regenerate Selected";
            text += $"; {failed.ToString("N0", Invariant)} failed{Colon(firstError)}. Click {button} to try again.";
        }
        else
        {
            text += ".";
        }
        return kept.Length > 0 ? $"{text} {kept}." : text;
    }

    public static string RegenerateFactsStoppedText(string message, int regenerated, int attempted) =>
        $"Generation stopped{Colon(message)}. Regenerated {regenerated.ToString("N0", Invariant)} of {Plural(attempted, "fact", "facts")}; the rest kept their text. Fix the problem on the API Configuration step, then try again.";

    public static string MarkedReviewedText(int n) => $"Marked {Plural(n, "fact", "facts")} as reviewed.";

    public static string DeletedFactsText(int n) => $"Deleted {Plural(n, "fact", "facts")}.";

    /// <summary>
    /// Mark All Reviewed (needs-review banner). Default No.
    /// </summary>
    public static (string Title, string Text) C13MarkAllReviewed(int n) =>
        ("Mark All Reviewed", n == 1
            ? "Mark 1 fact as reviewed? Its text will stay as it is."
            : $"Mark all {Plural(n, "fact", "facts")} as reviewed? Their text will stay as it is.");

    /// <summary>
    /// Delete Selected (Key Facts selection bar), worded like C5. Default No.
    /// </summary>
    public static (string Title, string Text) C14DeleteFacts(int n, int valuesUsing)
    {
        var text = $"Delete {Plural(n, "fact", "facts")}?";
        if (valuesUsing > 0)
        {
            text += $" They are used by {Plural(valuesUsing, "canonical value", "canonical values")}; " +
                    (valuesUsing == 1 ? "that link will be removed." : "those links will be removed.");
        }
        return ("Delete Facts", text + " This cannot be undone.");
    }

    // --- Private helpers ---

    private static List<GuidePart> Ordered(IReadOnlyList<GuidePart> parts) =>
        parts.Distinct().OrderBy(p => (int)p).ToList();

    private static string WasWere(IReadOnlyList<GuidePart> parts) =>
        parts.Count > 1 || (parts.Count == 1 && IsPluralPart(parts[0])) ? "were" : "was";

    private static string JoinWithAnd(IReadOnlyList<string> items)
    {
        if (items.Count == 0)
            return "";
        if (items.Count == 1)
            return items[0];
        return string.Join(", ", items.Take(items.Count - 1)) + " and " + items[^1];
    }

    private static string PreviousNoun(GuidePart part)
    {
        return part switch
        {
            GuidePart.Cast => PartNoun(GuidePart.Issues, false),
            GuidePart.Facts => PartNoun(GuidePart.Cast, false),
            GuidePart.Values => PartNoun(GuidePart.Facts, false),
            _ => "core document"
        };
    }

    private static string WritingBody(GuidePart part)
    {
        return part switch
        {
            GuidePart.Issues => "Reading the counts in the core document and describing what each one needs proven.",
            GuidePart.Cast => "Organizations and key characters come first, then their details and the background staff. The table appears as soon as the key characters are ready.",
            GuidePart.Facts => "One period of the timeline at a time. The table appears when every period is done.",
            _ => "Collecting the amounts, invoice and change-order numbers, document titles and project names the facts rely on."
        };
    }

    private static string KeyAndBackground(int keyCount, int backgroundCount) =>
        $"{keyCount.ToString("N0", Invariant)} key and {backgroundCount.ToString("N0", Invariant)} background {(keyCount + backgroundCount == 1 ? "character" : "characters")}";

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

    /// <summary>
    /// ": {message}" or "" when there is no message.
    /// </summary>
    private static string Colon(string? message)
    {
        var trimmed = TrimMessage(message);
        return trimmed.Length == 0 ? "" : ": " + trimmed;
    }

    private static string Capitalize(string text) =>
        text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text[1..];

    private static string Time(DateTime value) => value.ToString("h:mm tt", Invariant);

    private static string Date(DateTime value) => value.ToString("MMM d, yyyy", Invariant);
}
