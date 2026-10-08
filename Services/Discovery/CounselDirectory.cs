using ReelDiscovery.Models.Case;

namespace ReelDiscovery.Services.Discovery;

/// <summary>
/// Party and counsel resolution shared by the discovery planners and the written-discovery boilerplate (D-13, ANC-05):
/// which party a character belongs to, which outside firm represents a party, and that firm's partner and associate.
/// Every lookup is null-safe and degrades to null on a hand-edited or incomplete guide; nothing here throws.
/// </summary>
public static class CounselDirectory
{
    /// <summary>
    /// Plaintiff and Complainant sit on the plaintiff side of the caption. Mirrors the private grouping used by
    /// CoreDocumentAssembler.BuildCaption; kept here so discovery code does not depend on that file.
    /// </summary>
    public static bool IsPlaintiffSide(PartyRole role) => role is PartyRole.Plaintiff or PartyRole.Complainant;

    /// <summary>
    /// The core document party a character belongs to: the character's own PartyId (individual party) first, then the
    /// PartyId of the character's organization. Null for non-party characters (law firms, agencies, third parties).
    /// </summary>
    public static CaseParty? PartyOf(CaseDraft draft, CastMember? member)
    {
        if (draft?.CoreDocument == null || member == null)
            return null;

        var partyId = member.PartyId;
        if (string.IsNullOrEmpty(partyId))
            partyId = draft.Guide?.FindOrganization(member.OrganizationId)?.PartyId;

        return FindParty(draft.CoreDocument, partyId);
    }

    /// <summary>
    /// Ids of the guide organizations that stand for a party (Party organizations and an individual party's personal
    /// organization).
    /// </summary>
    public static IReadOnlyList<string> PartyOrganizationIds(CaseDraft draft, string partyId)
    {
        var guide = draft?.Guide;
        if (guide == null || string.IsNullOrEmpty(partyId))
            return Array.Empty<string>();

        return guide.Organizations
            .Where(o => o != null
                && o.Kind is OrganizationKind.Party or OrganizationKind.Individual
                && string.Equals(o.PartyId, partyId, StringComparison.Ordinal))
            .Select(o => o.Id)
            .ToList();
    }

    /// <summary>
    /// First law firm (guide order) whose RepresentsOrgIds include one of the party's own organizations; null when the
    /// party has no outside firm. Only the party's organizations count, so another party's counsel is never returned.
    /// </summary>
    public static CaseOrganization? FirmFor(CaseDraft draft, string? partyId)
    {
        var guide = draft?.Guide;
        if (guide == null || string.IsNullOrEmpty(partyId))
            return null;

        var orgIds = PartyOrganizationIds(draft!, partyId);
        if (orgIds.Count == 0)
            return null;

        return guide.Organizations.FirstOrDefault(o => o != null
            && o.Kind == OrganizationKind.LawFirm
            && (o.RepresentsOrgIds ?? new List<string>()).Any(orgIds.Contains));
    }

    /// <summary>
    /// Outside counsel partner at the firm representing the party (same title rule as CaseGuideGenerator.CounselGaps).
    /// </summary>
    public static CastMember? Partner(CaseDraft draft, string? partyId) => OutsideCounsel(draft, partyId, "Partner");

    /// <summary>
    /// Outside counsel associate at the firm representing the party; null when the firm has none.
    /// </summary>
    public static CastMember? Associate(CaseDraft draft, string? partyId) => OutsideCounsel(draft, partyId, "Associate");

    /// <summary>
    /// D-16 default propounding side: the first Plaintiff or Complainant.
    /// </summary>
    public static CaseParty? FirstPlaintiffSide(CoreDocument? doc) =>
        doc?.Parties?.FirstOrDefault(p => p != null && IsPlaintiffSide(p.Role));

    /// <summary>
    /// D-16 default responding side: the first Defendant or Subject, falling back to the first ThirdParty.
    /// </summary>
    public static CaseParty? FirstDefenseSide(CoreDocument? doc)
    {
        var parties = doc?.Parties;
        if (parties == null)
            return null;

        return parties.FirstOrDefault(p => p != null && p.Role is PartyRole.Defendant or PartyRole.Subject)
            ?? parties.FirstOrDefault(p => p != null && p.Role == PartyRole.ThirdParty);
    }

    private static CastMember? OutsideCounsel(CaseDraft draft, string? partyId, string titleWord)
    {
        var firm = FirmFor(draft, partyId);
        if (firm == null)
            return null;

        return draft.Guide!.Cast.FirstOrDefault(c => c != null
            && c.Role == CastRole.OutsideCounsel
            && c.OrganizationId == firm.Id
            && (c.Title ?? string.Empty).Contains(titleWord, StringComparison.OrdinalIgnoreCase));
    }

    private static CaseParty? FindParty(CoreDocument doc, string? partyId)
    {
        if (string.IsNullOrEmpty(partyId) || doc.Parties == null)
            return null;

        return doc.Parties.FirstOrDefault(p => p != null && string.Equals(p.Id, partyId, StringComparison.Ordinal));
    }
}
