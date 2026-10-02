using System.Globalization;
using System.Text.RegularExpressions;
using ReelDiscovery.Models.Case;

namespace ReelDiscovery.Services.Case;

/// <summary>
/// Deterministic (no model) fixes for Checks findings: new ids, broken links, active ranges, date formats, email
/// addresses and domains, Who knew, real names, party links and review flags. Each fix re-reads the draft, so applying a
/// finding twice, or one that another fix already cleared, changes nothing. Edits run inside GuideStaleness.AbsorbHandEdit
/// (parts that were fresh stay fresh) and bump the draft revision.
/// </summary>
public static partial class CaseGuideQuickFix
{
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    // Formats with the year first or the month spelled out: their day and month can never be swapped
    private static readonly string[] UnambiguousFormats =
    {
        "yyyy-M-d", "yyyy/M/d", "yyyy.M.d", "yyyyMMdd",
        "yyyy-M-d'T'HH:mm", "yyyy-M-d'T'HH:mm:ss", "yyyy-M-d'T'HH:mm:ssK", "yyyy-M-d'T'HH:mm:ss.FFFFFFFK",
        "yyyy-M-d HH:mm", "yyyy-M-d HH:mm:ss",
        "MMMM d, yyyy", "MMMM d yyyy", "MMM d, yyyy", "MMM d yyyy", "MMM. d, yyyy",
        "d MMMM yyyy", "d MMM yyyy", "d MMMM, yyyy", "dddd, MMMM d, yyyy"
    };

    // 3/15/2024 or 15/3/2024 (also with dashes or dots): read only when one part cannot be a month
    [GeneratedRegex(@"^(\d{1,2})[/.\-](\d{1,2})[/.\-](\d{4})$", RegexOptions.None, 200)]
    private static partial Regex NumericDayMonthYear();

    /// <summary>
    /// True for fixes applied by code with no model call: the Checks card's Fix / Fix All and the Next gate's
    /// Fix Automatically apply only these.
    /// </summary>
    public static bool IsAutomatic(GuideQuickFix fix) => fix is GuideQuickFix.AssignNewId
        or GuideQuickFix.RemoveBrokenLinks
        or GuideQuickFix.WidenActiveRange
        or GuideQuickFix.NormalizeDate
        or GuideQuickFix.ResetEmail
        or GuideQuickFix.NewEmailDomain
        or GuideQuickFix.AddActorsToKnownBy
        or GuideQuickFix.ReplaceRealNames
        or GuideQuickFix.LinkParty
        or GuideQuickFix.MarkReviewed;

    /// <summary>
    /// Applies one finding's automatic fix. False when the finding has none, its entity is gone, or nothing needed changing.
    /// </summary>
    public static bool Apply(CaseDraft draft, GuideFinding finding)
    {
        if (draft?.Guide == null || finding == null || !IsAutomatic(finding.QuickFix))
            return false;

        try
        {
            return finding.QuickFix switch
            {
                GuideQuickFix.AssignNewId => AssignNewIds(draft, finding.Area, finding.EntityId),
                GuideQuickFix.RemoveBrokenLinks => RemoveBrokenLinks(draft, finding),
                GuideQuickFix.WidenActiveRange => WidenActiveRange(draft, finding.FixTargetId, finding.FixValue),
                GuideQuickFix.NormalizeDate => NormalizeFactDate(draft, finding.EntityId),
                GuideQuickFix.ResetEmail => ResetEmail(draft, finding.FixTargetId.Length > 0 ? finding.FixTargetId : finding.EntityId),
                GuideQuickFix.NewEmailDomain => NewEmailDomain(draft, finding.EntityId),
                GuideQuickFix.AddActorsToKnownBy => AddActorsToKnownBy(draft, finding.EntityId),
                GuideQuickFix.ReplaceRealNames => ReplaceRealNames(draft, finding.Area, finding.EntityId),
                GuideQuickFix.LinkParty => LinkParty(draft, finding.FixTargetId),
                GuideQuickFix.MarkReviewed => finding.Area == GuideArea.Values
                    ? CaseGuideEditing.MarkValueReviewed(draft, finding.EntityId)
                    : CaseGuideEditing.MarkReviewed(draft, finding.EntityId),
                _ => false
            };
        }
        catch (RegexMatchTimeoutException)
        {
            // Pathological text in a name substitution: leave the entry as it is; the finding stays listed under Checks
            return false;
        }
    }

    /// <summary>
    /// Applies every automatic fix in the list, in order. Returns how many changed the draft.
    /// </summary>
    public static int ApplyAll(CaseDraft draft, IEnumerable<GuideFinding> findings)
    {
        if (draft?.Guide == null || findings == null)
            return 0;

        // Snapshot: a fix never changes the list it came from
        var count = 0;
        foreach (var finding in findings.Where(f => f != null && IsAutomatic(f.QuickFix)).ToList())
        {
            if (Apply(draft, finding))
                count++;
        }
        return count;
    }

    /// <summary>
    /// Number of findings an automatic fix can clear, per fix kind, in enum order (the Fix All confirmation lists these).
    /// </summary>
    public static List<(GuideQuickFix Fix, int Count)> CountAutomatic(IEnumerable<GuideFinding> findings) =>
        (findings ?? Enumerable.Empty<GuideFinding>())
            .Where(f => f != null && IsAutomatic(f.QuickFix))
            .GroupBy(f => f.QuickFix)
            .OrderBy(g => (int)g.Key)
            .Select(g => (g.Key, g.Count()))
            .ToList();

    /// <summary>
    /// Reads a date written in an unambiguous non-ISO form ("2024-3-4", "2024/03/04", "March 4, 2024",
    /// "2024-03-04T09:30:00Z", "15/3/2024") as yyyy-MM-dd. A day and month that could be swapped ("3/4/2024") are not read.
    /// </summary>
    public static bool TryNormalizeDate(string? text, out string iso)
    {
        iso = string.Empty;
        var value = (text ?? string.Empty).Trim();
        if (value.Length == 0)
            return false;

        if (DateTime.TryParseExact(value, UnambiguousFormats, Invariant, DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.RoundtripKind, out var date)
            || TryDayMonth(value, out date))
        {
            iso = Format(date);
            return true;
        }
        return false;
    }

    private static bool TryDayMonth(string value, out DateTime date)
    {
        date = default;
        var match = NumericDayMonthYear().Match(value);
        if (!match.Success)
            return false;

        var a = int.Parse(match.Groups[1].Value, Invariant);
        var b = int.Parse(match.Groups[2].Value, Invariant);
        var year = int.Parse(match.Groups[3].Value, Invariant);

        // Month/day when the second part cannot be a month, day/month when the first cannot; equal parts read the same either way
        int month, day;
        if (a == b || (a <= 12 && b > 12))
            (month, day) = (a, b);
        else if (a > 12 && b <= 12)
            (month, day) = (b, a);
        else
            return false;

        if (month < 1 || month > 12 || day < 1 || day > DateTime.DaysInMonth(year, month))
            return false;

        date = new DateTime(year, month, day);
        return true;
    }

    // ----- Suggestions shown in the fix hints (the fix computes the same values) -----

    /// <summary>
    /// The address the code would build for the character (first.last@ their organization's domain, unique in the cast);
    /// empty when the organization or its domain is missing.
    /// </summary>
    public static string SuggestedEmail(CaseDraft draft, CastMember member)
    {
        var guide = draft?.Guide;
        var org = guide?.FindOrganization(member?.OrganizationId);
        if (guide == null || member == null || string.IsNullOrWhiteSpace(org?.Domain))
            return string.Empty;

        return EmailIdentity.AddressFor(member.FirstName ?? string.Empty, member.LastName ?? string.Empty, org.Domain, TakenAddresses(guide, member));
    }

    /// <summary>
    /// Of the characters sharing one address, the one it belongs to: whose own code-built address it is (first.last at
    /// their organization's domain), else the first in the cast.
    /// </summary>
    public static CastMember SharedAddressOwner(CaseDraft draft, IReadOnlyList<CastMember> holders)
    {
        var guide = draft?.Guide;
        foreach (var holder in holders)
        {
            var org = guide?.FindOrganization(holder.OrganizationId);
            if (string.IsNullOrWhiteSpace(org?.Domain))
                continue;

            var own = EmailIdentity.AddressFor(holder.FirstName ?? string.Empty, holder.LastName ?? string.Empty, org.Domain,
                new HashSet<string>(StringComparer.OrdinalIgnoreCase));
            if (string.Equals(own, (holder.Email ?? string.Empty).Trim(), StringComparison.OrdinalIgnoreCase))
                return holder;
        }
        return holders[0];
    }

    /// <summary>
    /// A domain no other organization uses, from the organization's name.
    /// </summary>
    public static string SuggestedDomain(CaseDraft draft, CaseOrganization org)
    {
        var taken = new HashSet<string>(
            (draft?.Guide?.Organizations ?? new List<CaseOrganization>())
                .Where(o => o != null && !string.IsNullOrWhiteSpace(o.Domain))
                .Select(o => o.Domain.Trim().TrimEnd('.').ToLowerInvariant()),
            StringComparer.OrdinalIgnoreCase);
        return EmailIdentity.DomainFor(org, taken);
    }

    /// <summary>
    /// The active range that repairs a missing or reversed one: the ends that can be read (normalized, swapped when reversed),
    /// widened to every fact the character did or knew about; a missing end falls back to the fact dates, then to the core
    /// document's timeline (start) and filing or timeline end. False when an end cannot be found.
    /// </summary>
    public static bool TryRepairRange(CaseDraft draft, CastMember member, out string from, out string to)
    {
        from = to = string.Empty;
        if (draft?.Guide == null || member == null)
            return false;

        DateTime? start = TryNormalizeDate(member.ActiveFrom, out var f) && CaseDraft.TryParseDate(f, out var fd) ? fd : null;
        DateTime? end = TryNormalizeDate(member.ActiveTo, out var t) && CaseDraft.TryParseDate(t, out var td) ? td : null;
        if (start.HasValue && end.HasValue && end < start)
            (start, end) = (end, start);

        var dates = FactDates(draft, member.Id);
        if (dates.Count > 0)
        {
            start = start.HasValue && start <= dates.Min() ? start : dates.Min();
            end = end.HasValue && end >= dates.Max() ? end : dates.Max();
        }

        var doc = draft.CoreDocument;
        if (!start.HasValue && CaseDraft.TryParseDate(doc?.TimelineStart, out var timelineStart))
            start = timelineStart;
        if (!end.HasValue)
        {
            if (CaseDraft.TryParseDate(doc?.FilingDate, out var filing))
                end = filing;
            else if (CaseDraft.TryParseDate(doc?.TimelineEnd, out var timelineEnd))
                end = timelineEnd;
        }

        if (!start.HasValue || !end.HasValue || end < start)
            return false;

        from = Format(start.Value);
        to = Format(end.Value);
        return true;
    }

    /// <summary>
    /// A character whose name matches the individual party exactly and is not another individual party.
    /// </summary>
    public static CastMember? ExactNameMember(CaseDraft draft, CaseParty party)
    {
        var key = CaseGuideGenerator.PersonKey(party?.Name);
        if (draft?.Guide == null || key.Length == 0)
            return null;

        var partyIds = PartyIds(draft);
        var matches = draft.Guide.Cast
            .Where(c => c != null && CaseGuideGenerator.PersonKey(c.FullName) == key
                && (string.IsNullOrEmpty(c.PartyId) || c.PartyId == party!.Id || !partyIds.Contains(c.PartyId)))
            .ToList();
        return matches.FirstOrDefault(c => c.Tier == CastTier.Key) ?? matches.FirstOrDefault();
    }

    /// <summary>
    /// An organization (not a law firm) whose name matches the party exactly and is not linked to another party.
    /// </summary>
    public static CaseOrganization? ExactNameOrganization(CaseDraft draft, CaseParty party)
    {
        var key = CaseGuideGenerator.PersonKey(party?.Name);
        if (draft?.Guide == null || key.Length == 0)
            return null;

        var partyIds = PartyIds(draft);
        return draft.Guide.Organizations.FirstOrDefault(o => o != null && o.Kind != OrganizationKind.LawFirm
            && CaseGuideGenerator.PersonKey(o.Name) == key
            && (string.IsNullOrEmpty(o.PartyId) || o.PartyId == party!.Id || !partyIds.Contains(o.PartyId)));
    }

    // ----- Fixes -----

    private static bool AssignNewIds(CaseDraft draft, GuideArea area, string entityId)
    {
        var guide = draft.Guide!;
        var (prefix, part, entries) = area switch
        {
            GuideArea.Issues => (IdAllocator.Issue, GuidePart.Issues, guide.Issues.Where(x => x != null).Select(x => (Get: (Func<string>)(() => x.Id), Set: (Action<string>)(v => x.Id = v))).ToList()),
            GuideArea.Cast => (IdAllocator.Character, GuidePart.Cast, guide.Cast.Where(x => x != null).Select(x => (Get: (Func<string>)(() => x.Id), Set: (Action<string>)(v => x.Id = v))).ToList()),
            GuideArea.Facts => (IdAllocator.Fact, GuidePart.Facts, guide.Facts.Where(x => x != null).Select(x => (Get: (Func<string>)(() => x.Id), Set: (Action<string>)(v => x.Id = v))).ToList()),
            GuideArea.Values => (IdAllocator.Value, GuidePart.Values, guide.Values.Where(x => x != null).Select(x => (Get: (Func<string>)(() => x.Id), Set: (Action<string>)(v => x.Id = v))).ToList()),
            _ => (IdAllocator.Organization, GuidePart.Cast, guide.Organizations.Where(x => x != null).Select(x => (Get: (Func<string>)(() => x.Id), Set: (Action<string>)(v => x.Id = v))).ToList())
        };

        // A blank id gets a new one; of entries sharing an id, the first keeps it (links point to it) and the rest get new ones
        var targets = string.IsNullOrEmpty(entityId)
            ? entries.Where(e => string.IsNullOrWhiteSpace(e.Get())).ToList()
            : entries.Where(e => e.Get() == entityId).Skip(1).ToList();
        if (targets.Count == 0)
            return false;

        GuideStaleness.AbsorbHandEdit(draft, part, () =>
        {
            foreach (var entry in targets)
                entry.Set(IdAllocator.Next(guide, prefix));
        });
        draft.Revision++;
        return true;
    }

    private static bool RemoveBrokenLinks(CaseDraft draft, GuideFinding finding)
    {
        var guide = draft.Guide!;
        switch (finding.Area)
        {
            case GuideArea.Facts:
            {
                var fact = guide.FindFact(finding.EntityId);
                if (fact == null)
                    return false;

                var issues = Clean(fact.IssueIds).Where(id => guide.FindIssue(id) != null).ToList();
                var actors = Clean(fact.ActorIds).Where(id => guide.FindCast(id) != null).ToList();
                var knowers = Clean(fact.KnownByIds).Where(id => guide.FindCast(id) != null).ToList();
                if (issues.Count == Count(fact.IssueIds) && actors.Count == Count(fact.ActorIds) && knowers.Count == Count(fact.KnownByIds))
                    return false;

                GuideStaleness.AbsorbHandEdit(draft, GuidePart.Facts, () =>
                {
                    fact.IssueIds = issues;
                    fact.ActorIds = actors;
                    fact.KnownByIds = knowers;
                });
                break;
            }

            case GuideArea.Values:
            {
                var value = guide.FindValue(finding.EntityId);
                if (value == null)
                    return false;

                var facts = Clean(value.FactIds).Where(id => guide.FindFact(id) != null).ToList();
                if (facts.Count == Count(value.FactIds))
                    return false;

                GuideStaleness.AbsorbHandEdit(draft, GuidePart.Values, () => value.FactIds = facts);
                break;
            }

            default:
            {
                var firm = guide.FindOrganization(finding.EntityId);
                if (firm == null)
                    return false;

                var represents = Clean(firm.RepresentsOrgIds).Where(id => guide.FindOrganization(id) != null).ToList();
                if (represents.Count == Count(firm.RepresentsOrgIds))
                    return false;

                GuideStaleness.AbsorbHandEdit(draft, GuidePart.Cast, () => firm.RepresentsOrgIds = represents);
                break;
            }
        }

        draft.Revision++;
        return true;
    }

    /// <summary>
    /// Widens the character's range to include the date (hand-edited or not: the user asked for it), or repairs a missing or
    /// reversed range when no date is given.
    /// </summary>
    private static bool WidenActiveRange(CaseDraft draft, string castId, string dateText)
    {
        var member = draft.Guide!.FindCast(castId);
        if (member == null)
            return false;

        string from, to;
        var valid = CaseDraft.TryParseDate(member.ActiveFrom, out var currentFrom)
            && CaseDraft.TryParseDate(member.ActiveTo, out var currentTo) && currentTo >= currentFrom;
        if (valid)
        {
            CaseDraft.TryParseDate(member.ActiveTo, out currentTo);
            if (!CaseDraft.TryParseDate(dateText, out var date))
                return false;
            from = Format(date < currentFrom ? date : currentFrom);
            to = Format(date > currentTo ? date : currentTo);
        }
        else
        {
            if (!TryRepairRange(draft, member, out from, out to))
                return false;
            if (CaseDraft.TryParseDate(dateText, out var date))
            {
                CaseDraft.TryParseDate(from, out var repairedFrom);
                CaseDraft.TryParseDate(to, out var repairedTo);
                from = Format(date < repairedFrom ? date : repairedFrom);
                to = Format(date > repairedTo ? date : repairedTo);
            }
        }

        if (from == member.ActiveFrom && to == member.ActiveTo)
            return false;

        GuideStaleness.AbsorbHandEdit(draft, GuidePart.Cast, () =>
        {
            member.ActiveFrom = from;
            member.ActiveTo = to;
        });
        draft.Revision++;
        return true;
    }

    private static bool NormalizeFactDate(CaseDraft draft, string factId)
    {
        var fact = draft.Guide!.FindFact(factId);
        if (fact == null || CaseDraft.TryParseDate(fact.Date, out _) || !TryNormalizeDate(fact.Date, out var iso))
            return false;

        GuideStaleness.AbsorbHandEdit(draft, GuidePart.Facts, () => fact.Date = iso);
        draft.Revision++;
        return true;
    }

    private static bool ResetEmail(CaseDraft draft, string castId)
    {
        var guide = draft.Guide!;
        var member = guide.FindCast(castId);
        var suggested = member == null ? string.Empty : SuggestedEmail(draft, member);
        if (member == null || suggested.Length == 0 || string.Equals(suggested, member.Email, StringComparison.OrdinalIgnoreCase))
            return false;

        GuideStaleness.AbsorbHandEdit(draft, GuidePart.Cast, () =>
        {
            // The address is code-owned again, so it follows later name and organization edits
            member.EmailIsUserEdited = false;
            CaseGuideEditing.RefreshIdentity(draft, member);
        });
        draft.Revision++;
        return true;
    }

    private static bool NewEmailDomain(CaseDraft draft, string orgId)
    {
        var guide = draft.Guide!;
        var org = guide.FindOrganization(orgId);
        var domain = (org?.Domain ?? string.Empty).Trim().TrimEnd('.');
        if (org == null || domain.Length == 0)
            return false;

        // Only while another organization still shares it
        var shared = guide.Organizations.Any(o => o != null && o != org
            && string.Equals((o.Domain ?? string.Empty).Trim().TrimEnd('.'), domain, StringComparison.OrdinalIgnoreCase));
        if (!shared)
            return false;

        var newDomain = SuggestedDomain(draft, org);
        GuideStaleness.AbsorbHandEdit(draft, GuidePart.Cast, () =>
        {
            org.Domain = newDomain;
            foreach (var member in guide.Cast.Where(c => c != null && c.OrganizationId == org.Id))
                CaseGuideEditing.RefreshIdentity(draft, member);
        });
        draft.Revision++;
        return true;
    }

    private static bool AddActorsToKnownBy(CaseDraft draft, string factId)
    {
        var fact = draft.Guide!.FindFact(factId);
        if (fact == null)
            return false;

        var knowers = Clean(fact.KnownByIds).Concat(Clean(fact.ActorIds)).Distinct(StringComparer.Ordinal).ToList();
        if (knowers.Count == Count(fact.KnownByIds))
            return false;

        GuideStaleness.AbsorbHandEdit(draft, GuidePart.Facts, () => fact.KnownByIds = knowers);
        draft.Revision++;
        return true;
    }

    private static bool ReplaceRealNames(CaseDraft draft, GuideArea area, string entityId)
    {
        var guide = draft.Guide!;
        var subs = draft.Substitutions ?? new List<EntitySubstitution>();
        string Swap(string? text) => NameSubstitution.Apply(text ?? string.Empty, subs);

        Action? apply = null;
        var part = GuidePart.Cast;
        switch (area)
        {
            case GuideArea.Cast:
            {
                var member = guide.FindCast(entityId);
                if (member == null)
                    break;

                var fullName = Swap(member.FullName);
                var (title, involvement, voice) = (Swap(member.Title), Swap(member.Involvement), Swap(member.VoiceNote));
                if (fullName == member.FullName && title == (member.Title ?? string.Empty)
                    && involvement == (member.Involvement ?? string.Empty) && voice == (member.VoiceNote ?? string.Empty))
                    break;

                apply = () =>
                {
                    if (fullName != member.FullName)
                    {
                        // Same split as editing the full name: the last word is the last name
                        var split = fullName.LastIndexOf(' ');
                        member.FirstName = split > 0 ? fullName[..split].Trim() : fullName;
                        member.LastName = split > 0 ? fullName[(split + 1)..].Trim() : string.Empty;
                    }
                    member.Title = title;
                    member.Involvement = involvement;
                    member.VoiceNote = voice;
                    CaseGuideEditing.RefreshIdentity(draft, member);
                };
                break;
            }

            case GuideArea.Facts:
            {
                var fact = guide.FindFact(entityId);
                if (fact == null)
                    break;

                var (title, description) = (Swap(fact.Title), Swap(fact.Description));
                if (title == (fact.Title ?? string.Empty) && description == (fact.Description ?? string.Empty))
                    break;

                part = GuidePart.Facts;
                apply = () =>
                {
                    fact.Title = title;
                    fact.Description = description;
                };
                break;
            }

            case GuideArea.Values:
            {
                var value = guide.FindValue(entityId);
                if (value == null)
                    break;

                var (label, text, description) = (Swap(value.Label), Swap(value.Value), Swap(value.Description));
                if (label == (value.Label ?? string.Empty) && text == (value.Value ?? string.Empty) && description == (value.Description ?? string.Empty))
                    break;

                part = GuidePart.Values;
                apply = () =>
                {
                    value.Label = label;
                    value.Value = text;
                    value.Description = description;
                };
                break;
            }

            case GuideArea.Guide:
            {
                var org = guide.FindOrganization(entityId);
                if (org == null)
                    break;

                var (name, shortName) = (Swap(org.Name), Swap(org.ShortName));
                if (name == (org.Name ?? string.Empty) && shortName == (org.ShortName ?? string.Empty))
                    break;

                apply = () =>
                {
                    org.Name = name;
                    org.ShortName = shortName;
                };
                break;
            }
        }

        if (apply == null)
            return false;

        GuideStaleness.AbsorbHandEdit(draft, part, apply);
        draft.Revision++;
        return true;
    }

    private static bool LinkParty(CaseDraft draft, string partyId)
    {
        var party = (draft.CoreDocument?.Parties ?? new List<CaseParty>()).FirstOrDefault(p => p != null && p.Id == partyId);
        if (party == null || string.IsNullOrEmpty(partyId))
            return false;

        Action? apply = null;
        if (CaseGuideGenerator.IsIndividual(party))
        {
            var member = CaseGuideGenerator.FindPartyMember(draft.Guide!.Cast, party) == null ? ExactNameMember(draft, party) : null;
            if (member != null)
                apply = () => member.PartyId = partyId;
        }
        else if (!draft.Guide!.Organizations.Any(o => o != null && o.PartyId == partyId))
        {
            var org = ExactNameOrganization(draft, party);
            if (org != null)
                apply = () => org.PartyId = partyId;
        }

        if (apply == null)
            return false;

        GuideStaleness.AbsorbHandEdit(draft, GuidePart.Cast, apply);
        draft.Revision++;
        return true;
    }

    // ----- Helpers -----

    private static List<DateTime> FactDates(CaseDraft draft, string castId) =>
        (draft.Guide?.Facts ?? new List<KeyFact>())
            .Where(f => f != null && (Clean(f.ActorIds).Contains(castId) || Clean(f.KnownByIds).Contains(castId)))
            .Select(f => CaseDraft.TryParseDate(f.Date, out var d) ? d : (DateTime?)null)
            .Where(d => d.HasValue)
            .Select(d => d!.Value)
            .ToList();

    private static HashSet<string> TakenAddresses(CaseGuide guide, CastMember except) =>
        new(guide.Cast.Where(c => c != null && c != except && !string.IsNullOrWhiteSpace(c.Email)).Select(c => c.Email.Trim().ToLowerInvariant()),
            StringComparer.OrdinalIgnoreCase);

    private static HashSet<string> PartyIds(CaseDraft draft) =>
        new((draft.CoreDocument?.Parties ?? new List<CaseParty>()).Where(p => p != null && !string.IsNullOrEmpty(p.Id)).Select(p => p.Id),
            StringComparer.Ordinal);

    private static List<string> Clean(List<string>? ids) =>
        (ids ?? new List<string>()).Where(id => !string.IsNullOrEmpty(id)).Distinct(StringComparer.Ordinal).ToList();

    private static int Count(List<string>? ids) => ids?.Count ?? 0;

    private static string Format(DateTime date) => date.ToString("yyyy-MM-dd", Invariant);
}
