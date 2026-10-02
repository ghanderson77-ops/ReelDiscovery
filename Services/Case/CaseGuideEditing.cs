using System.Text.RegularExpressions;
using ReelDiscovery.Models.Case;

namespace ReelDiscovery.Services.Case;

public enum CastField
{
    FirstName,
    LastName,
    FullName,
    Title,
    Department,
    OrganizationId,
    Role,
    Tier,
    Email,
    ActiveFrom,
    ActiveTo,
    Involvement,
    VoiceNote,
    SignatureStyle
}

public enum FactField
{
    Date,
    Title,
    Description,
    Significance
}

public enum FactLinkKind
{
    Issues,
    Actors,
    KnownBy
}

public enum ValueField
{
    Kind,
    Label,
    Value,
    Description
}

/// <summary>
/// Outcome of a guide edit: Error holds the UI-SPEC message when the input was rejected (nothing changed).
/// </summary>
public class GuideEditResult
{
    public bool Changed { get; set; }
    public string Error { get; set; } = string.Empty;

    /// <summary>
    /// Facts marked NeedsReview by a cast edit.
    /// </summary>
    public List<string> AffectedFactIds { get; set; } = new();

    public bool Succeeded => Error.Length == 0;

    internal static GuideEditResult Fail(string error) => new() { Error = error };
}

/// <summary>
/// Hand edits, adds and deletes for the case guide (GUIDE-08, D-18..D-21). Every change runs inside
/// GuideStaleness.AbsorbHandEdit, marks the entry user-edited and bumps the draft revision; unknown ids and
/// unchanged values are no-ops.
/// </summary>
public static partial class CaseGuideEditing
{
    public const string DateFormatError = "Use a date like 2024-03-03 (YYYY-MM-DD).";

    private const string FirstNameEmptyError = "First name cannot be empty.";
    private const string NameEmptyError = "Name cannot be empty.";
    private const string FactEmptyError = "Fact cannot be empty.";
    private const string LabelEmptyError = "Label cannot be empty.";
    private const string EmailError = "Use an address like name@company.com.";
    private const string OrganizationError = "Choose an organization from the list.";
    private const string RangeError = "The active range ends before it starts.";
    private const string ChoiceError = "Choose a value from the list.";

    // ---- Cast ----

    public static CastMember AddCharacter(CaseDraft draft)
    {
        var guide = draft.Guide ??= new CaseGuide();
        var doc = draft.CoreDocument;
        var org = guide.Organizations.FirstOrDefault(o => o.Kind == OrganizationKind.Party) ?? guide.Organizations.FirstOrDefault();

        var member = new CastMember
        {
            Id = IdAllocator.Next(guide, IdAllocator.Character),
            FirstName = "New",
            LastName = "character",
            OrganizationId = org?.Id ?? string.Empty,
            Tier = CastTier.Key,
            Role = CastRole.Staff,
            Phone = SignatureBuilder.FictionalPhone(),
            ActiveFrom = doc?.TimelineStart ?? string.Empty,
            ActiveTo = !string.IsNullOrEmpty(doc?.FilingDate) ? doc.FilingDate : doc?.TimelineEnd ?? string.Empty,
            SignatureStyle = SignatureStyle.Full,
            IsUserEdited = true
        };

        GuideStaleness.AbsorbHandEdit(draft, GuidePart.Cast, () =>
        {
            guide.Cast.Add(member);
            RefreshIdentity(draft, member);
        });

        draft.Revision++;
        return member;
    }

    public static GuideEditResult SetCastField(CaseDraft draft, string castId, CastField field, string value)
    {
        var guide = draft?.Guide;
        var member = guide?.FindCast(castId);
        if (draft == null || guide == null || member == null)
            return new GuideEditResult();

        var text = (value ?? string.Empty).Trim();
        Action? apply = null;
        var identity = false;   // name or organization: email and signature are recomputed
        var flagsFacts = false; // facts citing the character need review

        switch (field)
        {
            case CastField.FirstName:
                if (text.Length == 0)
                    return GuideEditResult.Fail(FirstNameEmptyError);
                if (text != member.FirstName)
                    apply = () => member.FirstName = text;
                identity = flagsFacts = true;
                break;

            case CastField.LastName:
                if (text != member.LastName)
                    apply = () => member.LastName = text;
                identity = flagsFacts = true;
                break;

            case CastField.FullName:
                if (text.Length == 0)
                    return GuideEditResult.Fail(NameEmptyError);
                var split = text.LastIndexOf(' ');
                var first = split > 0 ? text[..split].Trim() : text;
                var last = split > 0 ? text[(split + 1)..].Trim() : string.Empty;
                if (first != member.FirstName || last != member.LastName)
                    apply = () => { member.FirstName = first; member.LastName = last; };
                identity = flagsFacts = true;
                break;

            case CastField.Title:
                if (text != member.Title)
                    apply = () => member.Title = text;
                break;

            case CastField.Department:
                if (text != member.Department)
                    apply = () => member.Department = text;
                break;

            case CastField.Involvement:
                if (text != member.Involvement)
                    apply = () => member.Involvement = text;
                break;

            case CastField.VoiceNote:
                if (text != member.VoiceNote)
                    apply = () => member.VoiceNote = text;
                break;

            case CastField.OrganizationId:
                if (guide.FindOrganization(text) == null)
                    return GuideEditResult.Fail(OrganizationError);
                if (text != member.OrganizationId)
                    apply = () => member.OrganizationId = text;
                identity = flagsFacts = true;
                break;

            case CastField.Role:
                if (!TryParseChoice<CastRole>(text, out var role))
                    return GuideEditResult.Fail(ChoiceError);
                if (role != member.Role)
                    apply = () => member.Role = role;
                break;

            case CastField.Tier:
                if (!TryParseChoice<CastTier>(text, out var tier))
                    return GuideEditResult.Fail(ChoiceError);
                if (tier != member.Tier)
                    apply = () => member.Tier = tier;
                break;

            case CastField.SignatureStyle:
                if (!TryParseChoice<SignatureStyle>(text, out var style))
                    return GuideEditResult.Fail(ChoiceError);
                if (style != member.SignatureStyle)
                    apply = () => member.SignatureStyle = style;
                break;

            case CastField.Email:
                if (!EmailIdentity.IsWellFormed(text))
                    return GuideEditResult.Fail(EmailError);
                if (text != member.Email)
                    apply = () => { member.Email = text; member.EmailIsUserEdited = true; };
                identity = true;
                break;

            case CastField.ActiveFrom:
            case CastField.ActiveTo:
                if (text.Length > 0 && !CaseDraft.TryParseDate(text, out _))
                    return GuideEditResult.Fail(DateFormatError);
                var from = field == CastField.ActiveFrom ? text : member.ActiveFrom;
                var to = field == CastField.ActiveTo ? text : member.ActiveTo;
                if (CaseDraft.TryParseDate(from, out var fromDate) && CaseDraft.TryParseDate(to, out var toDate) && toDate < fromDate)
                    return GuideEditResult.Fail(RangeError);
                if (from != member.ActiveFrom || to != member.ActiveTo)
                    apply = () => { member.ActiveFrom = from; member.ActiveTo = to; };
                flagsFacts = true;
                break;
        }

        if (apply == null)
            return new GuideEditResult();

        var result = new GuideEditResult { Changed = true };
        var oldName = member.FullName;

        GuideStaleness.AbsorbHandEdit(draft, GuidePart.Cast, () =>
        {
            apply();
            member.IsUserEdited = true;

            if (identity)
            {
                // Field edits set the address explicitly; only name/org edits regenerate it
                if (field != CastField.Email)
                    RefreshIdentity(draft, member);
                else
                    RefreshSignature(guide, member);
                SyncCounsel(draft, oldName, member);
            }
            else
            {
                RefreshSignature(guide, member);
            }

            if (flagsFacts)
                result.AffectedFactIds = FlagReferencingFacts(draft, member, oldName);
        });

        draft.Revision++;
        return result;
    }

    public static List<KeyFact> FactsReferencing(CaseDraft draft, string castId)
    {
        if (draft?.Guide == null || string.IsNullOrEmpty(castId))
            return new List<KeyFact>();

        return draft.Guide.Facts
            .Where(f => f.ActorIds.Contains(castId) || f.KnownByIds.Contains(castId))
            .ToList();
    }

    public static bool DeleteCharacter(CaseDraft draft, string castId, bool removeFromFacts)
    {
        var guide = draft?.Guide;
        var member = guide?.FindCast(castId);
        if (draft == null || guide == null || member == null)
            return false;

        GuideStaleness.AbsorbHandEdit(draft, GuidePart.Cast, () =>
        {
            guide.Cast.Remove(member);

            // Kept links become orphans that the validator lists under Checks (D-19)
            if (!removeFromFacts)
                return;

            foreach (var fact in FactsReferencing(draft, castId))
            {
                fact.ActorIds.RemoveAll(id => id == castId);
                fact.KnownByIds.RemoveAll(id => id == castId);
                fact.NeedsReview = true;
                fact.ReviewReason = $"{member.FullName} was removed from the cast.";
            }
        });

        draft.Revision++;
        return true;
    }

    /// <summary>
    /// Recomputes the code-owned email (unless the user typed it) and signature for a character.
    /// </summary>
    internal static void RefreshIdentity(CaseDraft draft, CastMember member)
    {
        var guide = draft.Guide;
        if (guide == null)
            return;

        var org = guide.FindOrganization(member.OrganizationId);
        if (!member.EmailIsUserEdited && !string.IsNullOrWhiteSpace(org?.Domain))
        {
            var taken = new HashSet<string>(
                guide.Cast.Where(c => c != member && !string.IsNullOrEmpty(c.Email)).Select(c => c.Email.ToLowerInvariant()),
                StringComparer.OrdinalIgnoreCase);
            member.Email = EmailIdentity.AddressFor(member.FirstName, member.LastName, org.Domain, taken);
        }

        RefreshSignature(guide, member);
    }

    private static void RefreshSignature(CaseGuide guide, CastMember member) =>
        member.Signature = member.Tier == CastTier.Key
            ? SignatureBuilder.Build(member, guide.FindOrganization(member.OrganizationId))
            : string.Empty;

    // The pleading's signing attorney is a cast member; keep the signature block in step with renames
    private static void SyncCounsel(CaseDraft draft, string oldName, CastMember member)
    {
        var counsel = draft.CoreDocument?.Counsel;
        if (counsel == null || oldName.Length == 0
            || !string.Equals(counsel.AttorneyName?.Trim(), oldName, StringComparison.OrdinalIgnoreCase))
            return;

        counsel.AttorneyName = member.FullName;
        counsel.Email = member.Email;
    }

    internal static List<string> FlagReferencingFacts(CaseDraft draft, CastMember member, string oldName)
    {
        var newName = member.FullName;
        var renamed = oldName.Length > 0 && oldName != newName;
        Regex? pattern = renamed
            ? new Regex(@"(?<![\p{L}\p{N}])" + Regex.Escape(oldName) + @"(?![\p{L}\p{N}])", RegexOptions.None, TimeSpan.FromSeconds(1))
            : null;

        var affected = new List<string>();
        foreach (var fact in FactsReferencing(draft, member.Id))
        {
            if (pattern != null)
            {
                try
                {
                    fact.Title = pattern.Replace(fact.Title, newName.Replace("$", "$$"));
                    fact.Description = pattern.Replace(fact.Description, newName.Replace("$", "$$"));
                }
                catch (RegexMatchTimeoutException)
                {
                    // Pathological text: leave it as written; the review flag below still asks the user to check it (T-03-23)
                }
            }

            fact.NeedsReview = true;
            fact.ReviewReason = ChangedReason(newName);
            affected.Add(fact.Id);
        }

        return affected;
    }

    private static string ChangedReason(string name) => $"{name} was changed after it was written.";

    /// <summary>
    /// Widens each generated (not hand-edited) character's active range so it covers the date of every fact they did or knew
    /// about: ActiveFrom moves earlier and ActiveTo later, never the other way (yyyy-MM-dd). Facts with unparseable dates
    /// are skipped. Hand-edited characters are never changed, so the validator still flags them. Applied like a cast hand
    /// edit, so parts that were fresh stay fresh. True when any range changed.
    /// </summary>
    public static bool WidenActiveRanges(CaseDraft draft)
    {
        var guide = draft?.Guide;
        if (draft == null || guide == null)
            return false;

        var facts = (guide.Facts ?? new List<KeyFact>())
            .Where(f => f != null && CaseDraft.TryParseDate(f.Date, out _))
            .Select(f =>
            {
                CaseDraft.TryParseDate(f.Date, out var date);
                return (Date: date, People: (f.ActorIds ?? new List<string>()).Concat(f.KnownByIds ?? new List<string>()).ToHashSet(StringComparer.Ordinal));
            })
            .ToList();

        var changes = new List<(CastMember Member, string From, string To)>();
        foreach (var member in (guide.Cast ?? new List<CastMember>()).Where(c => c != null && !c.IsUserEdited && !string.IsNullOrEmpty(c.Id)))
        {
            var dates = facts.Where(f => f.People.Contains(member.Id)).Select(f => f.Date).ToList();
            if (dates.Count == 0)
                continue;

            var first = dates.Min();
            var last = dates.Max();
            var from = CaseDraft.TryParseDate(member.ActiveFrom, out var currentFrom) && currentFrom <= first ? currentFrom : first;
            var to = CaseDraft.TryParseDate(member.ActiveTo, out var currentTo) && currentTo >= last ? currentTo : last;

            var fromText = FormatDate(from);
            var toText = FormatDate(to);
            if (fromText != member.ActiveFrom || toText != member.ActiveTo)
                changes.Add((member, fromText, toText));
        }

        if (changes.Count == 0)
            return false;

        GuideStaleness.AbsorbHandEdit(draft, GuidePart.Cast, () =>
        {
            foreach (var (member, from, to) in changes)
            {
                member.ActiveFrom = from;
                member.ActiveTo = to;
            }
        });

        return true;
    }

    private static string FormatDate(DateTime date) => date.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

    // ---- Facts ----

    public static KeyFact AddFact(CaseDraft draft)
    {
        var guide = draft.Guide ??= new CaseGuide();
        var fact = new KeyFact
        {
            Id = IdAllocator.Next(guide, IdAllocator.Fact),
            Date = draft.CoreDocument?.TimelineStart ?? string.Empty,
            Title = "New fact",
            Significance = FactSignificance.Supporting,
            IsUserEdited = true
        };

        GuideStaleness.AbsorbHandEdit(draft, GuidePart.Facts, () => guide.Facts.Add(fact));

        draft.Revision++;
        return fact;
    }

    public static GuideEditResult SetFactField(CaseDraft draft, string factId, FactField field, string value)
    {
        var guide = draft?.Guide;
        var fact = guide?.FindFact(factId);
        if (draft == null || guide == null || fact == null)
            return new GuideEditResult();

        var text = (value ?? string.Empty).Trim();
        Action? apply = null;

        switch (field)
        {
            case FactField.Date:
                if (!CaseDraft.TryParseDate(text, out _))
                    return GuideEditResult.Fail(DateFormatError);
                if (text != fact.Date)
                    apply = () => fact.Date = text;
                break;

            case FactField.Title:
                if (text.Length == 0)
                    return GuideEditResult.Fail(FactEmptyError);
                if (text != fact.Title)
                    apply = () => fact.Title = text;
                break;

            case FactField.Description:
                if (text != fact.Description)
                    apply = () => fact.Description = text;
                break;

            case FactField.Significance:
                if (!TryParseChoice<FactSignificance>(text, out var significance))
                    return GuideEditResult.Fail(ChoiceError);
                if (significance != fact.Significance)
                    apply = () => fact.Significance = significance;
                break;
        }

        if (apply == null)
            return new GuideEditResult();

        GuideStaleness.AbsorbHandEdit(draft, GuidePart.Facts, () =>
        {
            apply();
            fact.IsUserEdited = true;
            FlagValues(guide, fact.Id, ChangedReason(fact.Id));
        });

        draft.Revision++;
        return new GuideEditResult { Changed = true };
    }

    /// <summary>
    /// Replaces a fact's issue, actor or knower links. Unknown ids are dropped; actors are always knowers.
    /// </summary>
    public static GuideEditResult SetFactLinks(CaseDraft draft, string factId, FactLinkKind kind, IReadOnlyList<string> ids)
    {
        var guide = draft?.Guide;
        var fact = guide?.FindFact(factId);
        if (draft == null || guide == null || fact == null)
            return new GuideEditResult();

        // Only ids that exist are stored (T-03-24)
        var clean = (ids ?? Array.Empty<string>())
            .Where(id => kind == FactLinkKind.Issues ? guide.FindIssue(id) != null : guide.FindCast(id) != null)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        var issueIds = fact.IssueIds;
        var actorIds = fact.ActorIds;
        var knownByIds = fact.KnownByIds;

        switch (kind)
        {
            case FactLinkKind.Issues:
                issueIds = clean;
                break;
            case FactLinkKind.Actors:
                actorIds = clean;
                knownByIds = Union(fact.KnownByIds, actorIds);
                break;
            case FactLinkKind.KnownBy:
                knownByIds = Union(clean, fact.ActorIds);
                break;
        }

        if (issueIds.SequenceEqual(fact.IssueIds) && actorIds.SequenceEqual(fact.ActorIds) && knownByIds.SequenceEqual(fact.KnownByIds))
            return new GuideEditResult();

        GuideStaleness.AbsorbHandEdit(draft, GuidePart.Facts, () =>
        {
            fact.IssueIds = issueIds.ToList();
            fact.ActorIds = actorIds.ToList();
            fact.KnownByIds = knownByIds.ToList();
            fact.IsUserEdited = true;
            FlagValues(guide, fact.Id, ChangedReason(fact.Id));
        });

        draft.Revision++;
        return new GuideEditResult { Changed = true };
    }

    public static bool MarkReviewed(CaseDraft draft, string factId)
    {
        var fact = draft?.Guide?.FindFact(factId);
        if (draft == null || fact == null || !fact.NeedsReview)
            return false;

        fact.NeedsReview = false;
        fact.ReviewReason = string.Empty;
        draft.Revision++;
        return true;
    }

    public static int ValuesReferencing(CaseDraft draft, string factId) =>
        draft?.Guide?.Values.Count(v => v.FactIds.Contains(factId)) ?? 0;

    public static bool DeleteFact(CaseDraft draft, string factId)
    {
        var guide = draft?.Guide;
        var fact = guide?.FindFact(factId);
        if (draft == null || guide == null || fact == null)
            return false;

        GuideStaleness.AbsorbHandEdit(draft, GuidePart.Facts, () =>
        {
            guide.Facts.Remove(fact);
            FlagValues(guide, factId, $"{factId} was deleted.");
            foreach (var value in guide.Values)
                value.FactIds.RemoveAll(id => id == factId);
        });

        draft.Revision++;
        return true;
    }

    // Canonical values citing an edited fact may no longer match its text (W3)
    internal static void FlagValues(CaseGuide guide, string factId, string reason)
    {
        foreach (var value in guide.Values.Where(v => v != null && v.FactIds != null && v.FactIds.Contains(factId)))
        {
            value.NeedsReview = true;
            value.ReviewReason = reason;
        }
    }

    // ---- Canonical values ----

    public static CanonicalValue AddValue(CaseDraft draft)
    {
        var guide = draft.Guide ??= new CaseGuide();
        var value = new CanonicalValue
        {
            Id = IdAllocator.Next(guide, IdAllocator.Value),
            Kind = ValueKind.Other,
            Label = "New value",
            IsUserEdited = true
        };

        GuideStaleness.AbsorbHandEdit(draft, GuidePart.Values, () => guide.Values.Add(value));

        draft.Revision++;
        return value;
    }

    public static GuideEditResult SetValueField(CaseDraft draft, string valueId, ValueField field, string value)
    {
        var guide = draft?.Guide;
        var entry = guide?.FindValue(valueId);
        if (draft == null || guide == null || entry == null)
            return new GuideEditResult();

        var text = (value ?? string.Empty).Trim();
        Action? apply = null;

        switch (field)
        {
            case ValueField.Kind:
                if (!TryParseChoice<ValueKind>(text, out var kind))
                    return GuideEditResult.Fail(ChoiceError);
                if (kind != entry.Kind)
                    apply = () => entry.Kind = kind;
                break;

            case ValueField.Label:
                if (text.Length == 0)
                    return GuideEditResult.Fail(LabelEmptyError);
                if (text != entry.Label)
                    apply = () => entry.Label = text;
                break;

            case ValueField.Value:
                if (text != entry.Value)
                    apply = () => entry.Value = text;
                break;

            case ValueField.Description:
                if (text != entry.Description)
                    apply = () => entry.Description = text;
                break;
        }

        if (apply == null)
            return new GuideEditResult();

        GuideStaleness.AbsorbHandEdit(draft, GuidePart.Values, () =>
        {
            apply();
            entry.IsUserEdited = true;
        });

        draft.Revision++;
        return new GuideEditResult { Changed = true };
    }

    public static GuideEditResult SetValueFacts(CaseDraft draft, string valueId, IReadOnlyList<string> factIds)
    {
        var guide = draft?.Guide;
        var entry = guide?.FindValue(valueId);
        if (draft == null || guide == null || entry == null)
            return new GuideEditResult();

        // Only facts that exist are stored (T-03-24)
        var clean = (factIds ?? Array.Empty<string>())
            .Where(id => guide.FindFact(id) != null)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        if (clean.SequenceEqual(entry.FactIds))
            return new GuideEditResult();

        GuideStaleness.AbsorbHandEdit(draft, GuidePart.Values, () =>
        {
            entry.FactIds = clean;
            entry.IsUserEdited = true;
        });

        draft.Revision++;
        return new GuideEditResult { Changed = true };
    }

    public static bool MarkValueReviewed(CaseDraft draft, string valueId)
    {
        var entry = draft?.Guide?.FindValue(valueId);
        if (draft == null || entry == null || !entry.NeedsReview)
            return false;

        entry.NeedsReview = false;
        entry.ReviewReason = string.Empty;
        draft.Revision++;
        return true;
    }

    public static bool DeleteValue(CaseDraft draft, string valueId)
    {
        var guide = draft?.Guide;
        var entry = guide?.FindValue(valueId);
        if (draft == null || guide == null || entry == null)
            return false;

        GuideStaleness.AbsorbHandEdit(draft, GuidePart.Values, () => guide.Values.Remove(entry));

        draft.Revision++;
        return true;
    }

    // ---- Shared ----

    // Enum names only (case-insensitive); numeric strings are rejected
    private static bool TryParseChoice<T>(string text, out T result) where T : struct, Enum
    {
        var name = Enum.GetNames<T>().FirstOrDefault(n => n.Equals(text, StringComparison.OrdinalIgnoreCase));
        result = name != null ? Enum.Parse<T>(name) : default;
        return name != null;
    }

    private static List<string> Union(IEnumerable<string> first, IEnumerable<string> second) =>
        first.Concat(second).Distinct(StringComparer.Ordinal).ToList();
}
