using ReelDiscovery.Models.Case;
using ReelDiscovery.Tests.Case;

namespace ReelDiscovery.Tests.Discovery;

/// <summary>
/// Shared Phase 8 drafts built from CaseTestData and plain object initializers (no LLM). Every method returns a fresh
/// object graph, so tests may mutate the result.
/// </summary>
internal static class DiscoveryTestData
{
    /// <summary>
    /// Federal complaint (Brightline v. Harmon) with three issues, ten dated key facts before the filing date, a firm
    /// with a partner and an associate on each side and five key non-counsel witnesses with distinct fact counts
    /// (KnownByIds): CH-003 Raymond Harmon 7, CH-004 Lisa Chen 6, CH-001 Maria Delgado 5, CH-002 Tom Becker 4,
    /// CH-013 Michael Scott 3. Michael Scott (defense) keeps his name exactly (D-02).
    /// </summary>
    public static CaseDraft LitigationDraft()
    {
        var draft = CaseTestData.SampleCounselDraft();
        var doc = draft.CoreDocument!;
        var guide = draft.Guide!;

        draft.Complexity = ComplexityLevel.Standard;
        draft.Substitutions = new List<EntitySubstitution>
        {
            new()
            {
                Original = "Acme Corporation",
                Aliases = new List<string> { "Acme" },
                Kind = EntityKind.Company,
                Reason = "Real company",
                Substitute = "Apex Fabrication Co.",
                Apply = true
            }
        };

        doc.DocumentType = CoreDocumentType.Complaint;
        doc.Jurisdiction = new Jurisdiction
        {
            CourtSystem = CourtSystem.Federal,
            State = "Illinois",
            CourtName = "United States District Court for the Northern District of Illinois",
            District = "Northern District of Illinois",
            VenueCity = "Chicago"
        };
        doc.FilingDate = "2025-02-14";
        doc.TimelineStart = "2024-01-08";
        doc.TimelineEnd = "2024-11-30";

        // Two more counts and their issues (three issues in total)
        doc.Counts.Add(new CaseCount
        {
            Id = "count-2",
            Number = 2,
            Title = "Unjust Enrichment",
            AgainstPartyIds = new List<string> { "party-2" },
            LegalBasis = "Illinois common law",
            Summary = "Harmon kept the benefit of the changed work without paying for it."
        });
        doc.Counts.Add(new CaseCount
        {
            Id = "count-3",
            Number = 3,
            Title = "Violation of the Prompt Payment Act",
            AgainstPartyIds = new List<string> { "party-2" },
            LegalBasis = "Illinois Contractor Prompt Payment Act",
            Summary = "Harmon withheld payment beyond the statutory period without a good faith dispute."
        });
        foreach (var count in doc.Counts.Where(c => c.Number > 1))
        {
            guide.Issues.Add(new CaseIssue
            {
                Id = $"ISS-{count.Number:00}",
                CountId = count.Id,
                Number = count.Number,
                Title = count.Title,
                LegalBasis = count.LegalBasis,
                Summary = count.Summary,
                AgainstPartyIds = new List<string>(count.AgainstPartyIds),
                Description = $"Whether Harmon is liable to Brightline for {count.Title.ToLowerInvariant()} arising from the unpaid Lakeview Tower change orders.",
                Elements = new List<string> { "Brightline conferred a benefit on Harmon.", "Harmon retained the benefit without paying for it." },
                RelevanceCriteria = "Documents discussing payment timing, retention or the value of the Lakeview Tower glazing work."
            });
        }

        // Defense witness at Harmon
        guide.Cast.Add(Member(guide, "CH-013", "Michael", "Scott", "Regional Operations Manager", "Operations", "ORG-02",
            CastTier.Key, CastRole.Staff, "Harmon's regional operations manager who told the project team to hold subcontractor payments."));
        guide.Cast = guide.Cast.OrderBy(c => c.Tier == CastTier.Key ? 0 : 1).ThenBy(c => c.Id, StringComparer.Ordinal).ToList();

        // Spread existing facts across the three issues
        guide.FindFact("F-002")!.IssueIds = new List<string> { "ISS-01", "ISS-03" };
        guide.FindFact("F-004")!.IssueIds = new List<string> { "ISS-02" };
        guide.FindFact("F-006")!.IssueIds = new List<string> { "ISS-01", "ISS-02", "ISS-03" };

        guide.Facts.Add(Fact(7, "2024-07-15", "Harmon directs a hold on subcontractor payments",
            "Michael Scott told Harmon's project team in writing to hold all Brightline payments, including INV-2024-118, until year end.",
            new[] { "ISS-01", "ISS-03" }, new[] { "CH-013", "CH-003" }, new[] { "CH-004" }, FactSignificance.Hot));
        guide.Facts.Add(Fact(8, "2024-08-05", "Brightline sends a demand letter",
            "Brightline's outside counsel demanded payment of $487,250.00, the total unpaid contract balance under subcontract SC-LT-2024-031.",
            new[] { "ISS-01", "ISS-02" }, new[] { "CH-001", "CH-006" }, new[] { "CH-003" }, FactSignificance.Key));
        guide.Facts.Add(Fact(9, "2024-09-10", "Harmon disputes the change order quantities",
            "Harmon's project executive and Michael Scott raised a quantity dispute on Change Order 7 for the first time, months after approving it.",
            new[] { "ISS-03" }, new[] { "CH-004", "CH-013" }, new[] { "CH-003", "CH-002" }, FactSignificance.Supporting));
        guide.Facts.Add(Fact(10, "2024-10-22", "Harmon issues a back-charge",
            "Michael Scott approved a back-charge against Brightline that offset most of the Change Order 7 amount.",
            new[] { "ISS-02" }, new[] { "CH-013" }, new[] { "CH-003" }, FactSignificance.Key));

        guide.Values.Add(Value(5, ValueKind.Amount, "Total unpaid contract balance", "$487,250.00", "F-008"));
        guide.Values.Add(Value(6, ValueKind.ContractNumber, "Subcontract number", "SC-LT-2024-031", "F-001", "F-008"));

        guide.IdCounters["ISS"] = 3;
        guide.IdCounters["CH"] = 13;
        guide.IdCounters["F"] = 10;
        guide.IdCounters["V"] = 6;

        draft.CoreDocument!.SourceHash = draft.ComputeSourceHash();
        return draft;
    }

    /// <summary>
    /// LitigationDraft filed as a Texas state court petition.
    /// </summary>
    public static CaseDraft StateCourtDraft()
    {
        var draft = LitigationDraft();
        var doc = draft.CoreDocument!;
        doc.DocumentType = CoreDocumentType.Petition;
        doc.UsesPetitionTerminology = true;
        doc.Jurisdiction = new Jurisdiction
        {
            CourtSystem = CourtSystem.State,
            State = "Texas",
            CourtName = "District Court of Harris County, Texas",
            District = "Harris County",
            VenueCity = "Houston"
        };
        return draft;
    }

    /// <summary>
    /// SampleMemoDraft (criminal, Government perspective) with the United States as Complainant, two Subjects, a guide
    /// with two issues, five dated facts, four key non-counsel witnesses, an Amount value and a defense firm.
    /// </summary>
    public static CaseDraft MemoCriminalDraft()
    {
        var draft = CaseTestData.SampleMemoDraft();
        var doc = draft.CoreDocument!;

        doc.Parties.Add(new CaseParty
        {
            Id = "party-3",
            Name = "United States of America",
            ShortName = "United States",
            Role = PartyRole.Complainant,
            Description = "The government, acting through the Southwest Financial Crimes Task Force."
        });
        doc.Counts.Add(new CaseCount
        {
            Id = "count-2",
            Number = 2,
            Title = "Structuring of cash deposits",
            AgainstPartyIds = new List<string> { "party-1", "party-2" },
            LegalBasis = "Federal anti-structuring statute",
            Summary = "Deposits were split to stay below the bank reporting threshold."
        });

        var guide = new CaseGuide();
        foreach (var count in doc.Counts)
        {
            guide.Issues.Add(new CaseIssue
            {
                Id = $"ISS-{count.Number:00}",
                CountId = count.Id,
                Number = count.Number,
                Title = count.Title,
                LegalBasis = count.LegalBasis,
                Summary = count.Summary,
                AgainstPartyIds = new List<string>(count.AgainstPartyIds),
                Description = $"Whether Lane and Desert Bloom engaged in {count.Title.ToLowerInvariant()}.",
                Elements = new List<string> { "The funds were proceeds of unlawful activity.", "Lane knew the source of the funds." },
                RelevanceCriteria = "Documents discussing Desert Bloom cash deposits, bank reporting or the source of the cash."
            });
        }

        guide.Organizations.Add(Organization("ORG-01", "Desert Bloom Laundromats LLC", "Desert Bloom", OrganizationKind.Party, "party-2", "desertbloomlaundry.com"));
        guide.Organizations.Add(Organization("ORG-02", "Southwest Financial Crimes Task Force", "Task Force", OrganizationKind.Agency, "", "swfctf.gov"));
        guide.Organizations.Add(Organization("ORG-03", "First Sandia Bank", "First Sandia", OrganizationKind.ThirdParty, "", "firstsandia.com"));
        var firm = Organization("ORG-04", "Calloway & Marsh LLP", "Calloway & Marsh", OrganizationKind.LawFirm, "", "callowaymarsh.com");
        firm.RepresentsOrgIds.Add("ORG-01");
        guide.Organizations.Add(firm);

        var lane = Member(guide, "CH-001", "Victor", "Lane", "Owner", "", "ORG-01", CastTier.Key, CastRole.Party,
            "Owner of Desert Bloom who delivered and directed the cash deposits.");
        lane.PartyId = "party-1";
        guide.Cast.Add(lane);
        guide.Cast.Add(Member(guide, "CH-002", "Hector", "Ruiz", "Store Manager", "Operations", "ORG-01", CastTier.Key, CastRole.Staff,
            "Desert Bloom store manager who was told to keep each deposit small."));
        guide.Cast.Add(Member(guide, "CH-003", "Tom", "Reyes", "Special Agent", "Task Force 7", "ORG-02", CastTier.Key, CastRole.Staff,
            "Task force agent who traced the deposit pattern."));
        guide.Cast.Add(Member(guide, "CH-004", "Dana", "Whitfield", "Branch Manager", "Retail Banking", "ORG-03", CastTier.Key, CastRole.Staff,
            "Bank branch manager who filed suspicious activity reports on the Desert Bloom accounts."));
        guide.Cast.Add(Member(guide, "CH-005", "Grant", "Calloway", "Partner", "", "ORG-04", CastTier.Key, CastRole.OutsideCounsel,
            "Defense counsel for Desert Bloom and Lane."));
        guide.Cast.Add(Member(guide, "CH-006", "Ivy", "Marsh", "Associate", "", "ORG-04", CastTier.Key, CastRole.OutsideCounsel,
            "Associate reviewing Desert Bloom's bank records."));

        guide.Facts.Add(Fact(1, "2023-02-14", "Cash deposits spike at Desert Bloom",
            "Desert Bloom's monthly cash deposits at First Sandia Bank tripled with no change in customer traffic.",
            new[] { "ISS-01" }, new[] { "CH-001" }, new[] { "CH-004", "CH-003" }, FactSignificance.Key));
        guide.Facts.Add(Fact(2, "2023-03-20", "Lane delivers deposits personally",
            "Victor Lane began delivering the Desert Bloom cash deposits to the bank himself.",
            new[] { "ISS-01" }, new[] { "CH-001", "CH-004" }, new[] { "CH-003" }, FactSignificance.Supporting));
        guide.Facts.Add(Fact(3, "2023-05-08", "Deposits split below the reporting threshold",
            "Desert Bloom deposits began arriving in amounts just under the reporting threshold, split across branches on the same day.",
            new[] { "ISS-02" }, new[] { "CH-001", "CH-002" }, new[] { "CH-004" }, FactSignificance.Hot));
        guide.Facts.Add(Fact(4, "2023-06-12", "Store manager told to keep deposits small",
            "Lane told Hector Ruiz to keep each deposit under the threshold and to use more than one branch.",
            new[] { "ISS-02" }, new[] { "CH-001", "CH-002" }, Array.Empty<string>(), FactSignificance.Hot));
        guide.Facts.Add(Fact(5, "2023-08-21", "Bank files suspicious activity report",
            "First Sandia Bank reported $1,240,000.00 in Desert Bloom cash deposits as suspicious activity.",
            new[] { "ISS-01", "ISS-02" }, new[] { "CH-004" }, new[] { "CH-003" }, FactSignificance.Key));

        guide.Values.Add(Value(1, ValueKind.Amount, "Suspicious cash deposits", "$1,240,000.00", "F-005"));
        guide.Values.Add(Value(2, ValueKind.AccountNumber, "Desert Bloom operating account", "FSB-4471-0098", "F-001"));

        guide.IdCounters = new Dictionary<string, int>
        {
            ["ISS"] = guide.Issues.Count,
            ["ORG"] = 4,
            ["CH"] = 6,
            ["F"] = 5,
            ["V"] = 2
        };
        foreach (var state in new[] { guide.IssuesState, guide.IssueDetailsState, guide.CastState, guide.FactsState, guide.ValuesState })
            state.Status = GuidePartStatus.Ready;

        draft.Stage = CaseStage.CaseGuide;
        draft.Guide = guide;
        return draft;
    }

    public static CaseDraft MemoRegulatoryDraft()
    {
        var draft = MemoCriminalDraft();
        draft.CoreDocument!.MatterKind = MatterKind.Regulatory;
        return draft;
    }

    public static CaseDraft MemoCivilDraft()
    {
        var draft = MemoCriminalDraft();
        draft.CoreDocument!.MatterKind = MatterKind.Civil;
        draft.CoreDocument.MemoPerspective = MemoPerspective.InternalCorporate;
        return draft;
    }

    public static CaseDraft NoGuideDraft()
    {
        var draft = LitigationDraft();
        draft.Guide = null;
        return draft;
    }

    /// <summary>
    /// Hand-edited guide: the defense firm (ORG-04) and its outside counsel (CH-011, CH-012) are gone, so the defense
    /// side has no partner or associate.
    /// </summary>
    public static CaseDraft MissingDefenseFirmDraft()
    {
        var draft = LitigationDraft();
        var guide = draft.Guide!;
        guide.Organizations.RemoveAll(o => o.Id == "ORG-04");
        guide.Cast.RemoveAll(c => c.OrganizationId == "ORG-04");
        foreach (var fact in guide.Facts)
        {
            fact.ActorIds.RemoveAll(id => id is "CH-011" or "CH-012");
            fact.KnownByIds.RemoveAll(id => id is "CH-011" or "CH-012");
        }
        return draft;
    }

    /// <summary>
    /// Key non-counsel characters (Role Party or Staff), in cast order.
    /// </summary>
    public static List<string> WitnessIds(CaseDraft draft) =>
        draft.Guide?.Cast
            .Where(c => c.Tier == CastTier.Key && c.Role is CastRole.Party or CastRole.Staff)
            .Select(c => c.Id)
            .ToList()
        ?? new List<string>();

    private static CaseOrganization Organization(string id, string name, string shortName, OrganizationKind kind, string partyId, string domain) => new()
    {
        Id = id,
        Name = name,
        ShortName = shortName,
        Kind = kind,
        PartyId = partyId,
        Domain = domain,
        Location = "Albuquerque, New Mexico",
        TimeZoneId = "America/Denver"
    };

    private static CastMember Member(CaseGuide guide, string id, string first, string last, string title, string department,
        string orgId, CastTier tier, CastRole role, string involvement)
    {
        var org = guide.FindOrganization(orgId)!;
        var member = new CastMember
        {
            Id = id,
            FirstName = first,
            LastName = last,
            Title = title,
            Department = department,
            OrganizationId = orgId,
            Tier = tier,
            Role = role,
            Email = $"{first}.{last}@{org.Domain}".ToLowerInvariant(),
            Phone = $"(555) 555-02{id[^2..]}",
            ActiveFrom = "2022-10-01",
            ActiveTo = "2024-12-31",
            Involvement = involvement,
            VoiceNote = "Plain and direct.",
            SignatureStyle = SignatureStyle.Full
        };
        member.Signature = $"{member.FullName}\n{title}\n{org.Name}\n{member.Phone}\n{member.Email}";
        return member;
    }

    private static KeyFact Fact(int number, string date, string title, string description, string[] issueIds,
        string[] actorIds, string[] alsoKnownBy, FactSignificance significance) => new()
    {
        Id = $"F-{number:000}",
        Date = date,
        Title = title,
        Description = description,
        IssueIds = issueIds.ToList(),
        ActorIds = actorIds.ToList(),
        KnownByIds = actorIds.Concat(alsoKnownBy).ToList(),
        Significance = significance,
        SourceSectionId = "facts-2"
    };

    private static CanonicalValue Value(int number, ValueKind kind, string label, string value, params string[] factIds) => new()
    {
        Id = $"V-{number:000}",
        Kind = kind,
        Label = label,
        Value = value,
        Description = $"{label} as stated in the case guide; use exactly this text.",
        FactIds = factIds.ToList()
    };
}
