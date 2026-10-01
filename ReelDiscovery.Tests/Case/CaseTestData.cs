using ReelDiscovery.Models;
using ReelDiscovery.Models.Case;
using ReelDiscovery.Services.Ai;
using ReelDiscovery.Tests.Fakes;

namespace ReelDiscovery.Tests.Case;

/// <summary>
/// Shared gateway/fake helpers and sample complaint/memo drafts for all Phase 2 tests.
/// Section ids and headings are the contract with CoreDocumentTemplates; keep them byte-identical.
/// </summary>
internal static class CaseTestData
{
    public static AIModelConfig StoryModel() => new()
    {
        ModelId = "claude-story",
        DisplayName = "Story",
        Provider = LlmProviderKind.Anthropic,
        MaxOutputTokens = 64_000
    };

    public static AIModelConfig VolumeModel() => new()
    {
        ModelId = "claude-volume",
        DisplayName = "Volume",
        Provider = LlmProviderKind.Anthropic,
        MaxOutputTokens = 64_000
    };

    public static (LlmGateway Gateway, FakeLlmProvider Fake) CreateGateway()
    {
        // Anthropic fake proves the case generators are provider-agnostic
        var fake = new FakeLlmProvider(LlmProviderKind.Anthropic);
        return (new LlmGateway(fake, StoryModel(), VolumeModel()), fake);
    }

    public static void AssertNoJsonInstructions(LlmProviderRequest request)
    {
        var prompts = request.SystemPrompt + request.UserPrompt;
        Assert.DoesNotContain("Respond with JSON", prompts);
        Assert.DoesNotContain("valid JSON", prompts);
    }

    public sealed class SyncProgress<T> : IProgress<T>
    {
        public List<T> Reports { get; } = new();

        // Synchronous: avoids posting to a SynchronizationContext in tests
        public void Report(T value) => Reports.Add(value);
    }

    public static CaseDraft SampleComplaintDraft()
    {
        const string scenario = "Window subcontractor Brightline Glazing, LLC sues general contractor Harmon Builders, Inc. over unpaid change orders on the Lakeview Tower project in Chicago.";

        var draft = new CaseDraft
        {
            Scenario = scenario,
            EffectiveScenario = scenario,
            Complexity = ComplexityLevel.Simple,
            Substitutions = new List<EntitySubstitution>
            {
                new()
                {
                    Original = "Acme Windows",
                    Aliases = new List<string> { "Acme" },
                    Kind = EntityKind.Company,
                    Reason = "Real company",
                    Substitute = "Brightline Glazing",
                    Apply = true
                },
                new()
                {
                    Original = "Real Person",
                    Aliases = new List<string>(),
                    Kind = EntityKind.Person,
                    Reason = "Real person",
                    Substitute = "Dana Ortiz",
                    Apply = false
                }
            },
            Revision = 1,
            CoreDocument = new CoreDocument
            {
                DocumentType = CoreDocumentType.Complaint,
                MatterKind = MatterKind.Civil,
                Jurisdiction = new Jurisdiction
                {
                    CourtSystem = CourtSystem.Federal,
                    State = "Illinois",
                    CourtName = "United States District Court for the Northern District of Illinois",
                    District = "Northern District of Illinois",
                    VenueCity = "Chicago"
                },
                DemandsJuryTrial = true,
                Title = "Complaint for Breach of Contract and Unjust Enrichment",
                FilingDate = "2024-09-16",
                TimelineStart = "2024-01-08",
                TimelineEnd = "2024-06-28",
                Parties = new List<CaseParty>
                {
                    new()
                    {
                        Id = "party-1",
                        Name = "Brightline Glazing, LLC",
                        ShortName = "Brightline",
                        Role = PartyRole.Plaintiff,
                        EntityType = "Illinois limited liability company",
                        Domicile = "Chicago, Illinois",
                        Description = "Window and curtain wall subcontractor on the Lakeview Tower project."
                    },
                    new()
                    {
                        Id = "party-2",
                        Name = "Harmon Builders, Inc.",
                        ShortName = "Harmon",
                        Role = PartyRole.Defendant,
                        EntityType = "Delaware corporation",
                        Domicile = "Chicago, Illinois",
                        Description = "General contractor for the Lakeview Tower project."
                    }
                },
                Counts = new List<CaseCount>
                {
                    new()
                    {
                        Id = "count-1",
                        Number = 1,
                        Title = "Breach of Contract",
                        AgainstPartyIds = new List<string> { "party-2" },
                        LegalBasis = "Illinois common law",
                        Summary = "Harmon failed to pay approved change orders."
                    }
                },
                FactPlan = new List<FactSubsectionPlan>
                {
                    new()
                    {
                        Id = "facts-1",
                        Heading = "The Subcontract",
                        StartDate = "2024-01-08",
                        EndDate = "2024-02-29",
                        TargetParagraphs = 3,
                        Focus = "Formation of the glazing subcontract and its change order terms."
                    },
                    new()
                    {
                        Id = "facts-2",
                        Heading = "Unpaid Change Orders",
                        StartDate = "2024-03-01",
                        EndDate = "2024-06-28",
                        TargetParagraphs = 3,
                        Focus = "Approved change orders that Harmon refused to pay."
                    }
                },
                Counsel = new CounselInfo
                {
                    AttorneyName = "Morgan Ellis",
                    FirmName = "Ellis & Park LLP",
                    BarNumber = "6301234",
                    Address = "200 W. Adams St., Suite 900, Chicago, IL 60606",
                    Phone = "(555) 555-0142",
                    Email = "mellis@ellispark.example"
                },
                Sections = new List<CoreDocumentSection>
                {
                    Section("nature-of-action", SectionKind.NatureOfAction, "NATURE OF THE ACTION",
                        "This action arises from the refusal of Harmon to pay Brightline for approved change order work on the Lakeview Tower project.",
                        "Brightline performed the changed work in good faith and seeks the unpaid contract balance and related damages."),
                    Section("parties", SectionKind.Parties, "PARTIES",
                        "Plaintiff Brightline Glazing, LLC is an Illinois limited liability company with its principal place of business in Chicago, Illinois.",
                        "Defendant Harmon Builders, Inc. is a Delaware corporation with its principal place of business in Chicago, Illinois."),
                    Section("jurisdiction", SectionKind.JurisdictionVenue, "JURISDICTION AND VENUE",
                        "This Court has subject matter jurisdiction under 28 U.S.C. § 1332 because the parties are of diverse citizenship and the amount in controversy exceeds the jurisdictional threshold.",
                        "Venue is proper in the Northern District of Illinois because the project is located in Chicago and the events giving rise to the claims occurred there."),
                    Section("facts-1", SectionKind.FactualAllegations, "The Subcontract",
                        "Harmon engaged Brightline to furnish and install the window systems for the Lakeview Tower project.",
                        "The subcontract required Harmon to pay for changed work once a written change order was approved.",
                        "Brightline began mobilizing crews and ordering materials shortly after the subcontract was signed."),
                    Section("facts-2", SectionKind.FactualAllegations, "Unpaid Change Orders",
                        "Harmon directed Brightline to perform additional work on the upper floors and approved the related change orders in writing.",
                        "Brightline completed the changed work and submitted invoices for the approved amounts.",
                        "Harmon refused to pay the invoices and offered no legitimate reason for withholding payment."),
                    Section("count-1", SectionKind.Count, "COUNT I — Breach of Contract",
                        "The subcontract is a valid and enforceable agreement between Brightline and Harmon.",
                        "Harmon breached the subcontract by failing to pay for approved change order work, causing Brightline damages.",
                        countId: "count-1"),
                    Section("prayer", SectionKind.PrayerForRelief, "PRAYER FOR RELIEF",
                        "Brightline asks the Court to enter judgment in its favor and against Harmon.",
                        "Brightline asks the Court to award compensatory damages, interest and costs.",
                        "Brightline asks the Court to grant any further relief it deems just and proper.")
                }
            }
        };

        draft.CoreDocument.SourceHash = draft.ComputeSourceHash();
        return draft;
    }

    /// <summary>
    /// SampleComplaintDraft plus a complete, internally consistent case guide (Phase 3 fixture).
    /// Every issue has a fact and every key party/staff character acts in at least one fact; validators expect zero errors.
    /// </summary>
    public static CaseDraft SampleGuidedDraft()
    {
        var draft = SampleComplaintDraft();
        var doc = draft.CoreDocument!;
        draft.Stage = CaseStage.CaseGuide;

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
                Description = $"Whether Harmon is liable to Brightline for {count.Title.ToLowerInvariant()} arising from the unpaid Lakeview Tower change orders.",
                Elements = new List<string>
                {
                    "A valid subcontract obligated Harmon to pay for approved changed work.",
                    "Harmon failed to pay for approved change order work, causing Brightline damages."
                },
                RelevanceCriteria = "Documents discussing change order approval, invoicing or payment for the Lakeview Tower glazing work."
            });
        }

        var firmTokens = doc.Counsel.FirmName
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(t => new string(t.Where(char.IsLetter).ToArray()).ToLowerInvariant())
            .Where(t => t.Length > 0 && t is not ("llp" or "llc" or "pc"))
            .Take(2);
        var firmDomain = string.Concat(firmTokens) + ".com";

        guide.Organizations.Add(Organization("ORG-01", "Brightline Glazing, LLC", "Brightline", OrganizationKind.Party, "party-1", "brightline.com"));
        guide.Organizations.Add(Organization("ORG-02", "Harmon Builders, Inc.", "Harmon", OrganizationKind.Party, "party-2", "harmon.com"));
        var firm = Organization("ORG-03", doc.Counsel.FirmName, doc.Counsel.FirmName.Split(' ')[0], OrganizationKind.LawFirm, "", firmDomain);
        firm.RepresentsOrgIds.Add("ORG-01");
        guide.Organizations.Add(firm);

        var attorney = doc.Counsel.AttorneyName;
        var split = attorney.LastIndexOf(' ');
        var attorneyFirst = split > 0 ? attorney[..split] : attorney;
        var attorneyLast = split > 0 ? attorney[(split + 1)..] : string.Empty;

        guide.Cast.Add(Member(guide, "CH-001", "Maria", "Delgado", "President", "", "ORG-01", CastTier.Key, CastRole.Party,
            "Founder of Brightline who signed the subcontract and pressed Harmon for payment.", "Direct and formal; signs off with her first initial."));
        guide.Cast.Add(Member(guide, "CH-002", "Tom", "Becker", "Project Manager", "", "ORG-01", CastTier.Key, CastRole.Staff,
            "Ran the Lakeview Tower glazing work and negotiated Change Order 7 in the field.", "Short, practical messages written from the jobsite."));
        guide.Cast.Add(Member(guide, "CH-003", "Raymond", "Harmon", "Chief Executive Officer", "", "ORG-02", CastTier.Key, CastRole.Party,
            "Harmon's chief executive who decided to withhold payment on the change orders.", "Terse and dismissive; rarely writes more than two lines."));
        guide.Cast.Add(Member(guide, "CH-004", "Lisa", "Chen", "Project Executive", "", "ORG-02", CastTier.Key, CastRole.Staff,
            "Harmon's project executive who approved Change Order 7 in writing.", "Polished and procedural; cites contract sections."));
        guide.Cast.Add(Member(guide, "CH-005", "Karen", "Walsh", "General Counsel", "", "ORG-01", CastTier.Key, CastRole.InHouseCounsel,
            "Brightline's general counsel who reviewed the subcontract and the payment dispute.", "Careful and measured; marks messages privileged."));
        guide.Cast.Add(Member(guide, "CH-006", attorneyFirst, attorneyLast, "Partner", "", "ORG-03", CastTier.Key, CastRole.OutsideCounsel,
            "Outside litigation counsel who sent the demand letter and filed the complaint.", "Formal legal prose with numbered points."));
        guide.Cast.Add(Member(guide, "CH-007", "Evan", "Price", "Associate", "", "ORG-03", CastTier.Key, CastRole.OutsideCounsel,
            "Associate who assembled the payment history and invoice records for the claim.", "Thorough and deferential; summarizes before asking."));
        guide.Cast.Add(Member(guide, "CH-008", "Priya", "Nair", "Accounts Payable Specialist", "Accounting", "ORG-02", CastTier.Background, CastRole.Staff, "", ""));
        guide.Cast.Add(Member(guide, "CH-009", "Owen", "Reilly", "Glazing Foreman", "Field Operations", "ORG-01", CastTier.Background, CastRole.Staff, "", ""));

        var issueIds = guide.Issues.Select(i => i.Id).ToList();
        guide.Facts.Add(Fact(1, "2024-01-15", "Subcontract signed for Lakeview Tower",
            "Brightline and Harmon signed the glazing subcontract for Lakeview Tower, which required Harmon to pay for changed work once a written change order was approved.",
            issueIds, new[] { "CH-001", "CH-003" }, "CH-005", FactSignificance.Key, "facts-1"));
        guide.Facts.Add(Fact(2, "2024-02-12", "Harmon confirms the change order procedure",
            "Harmon's project executive confirmed to Brightline that approved change orders on Lakeview Tower would be paid within thirty days of invoice.",
            issueIds, new[] { "CH-004", "CH-002" }, "CH-001", FactSignificance.Supporting, "facts-1"));
        guide.Facts.Add(Fact(3, "2024-03-18", "Harmon approves Change Order 7",
            "Harmon approved Change Order 7 in writing for $184,250.00 covering additional window work on the upper floors of Lakeview Tower.",
            issueIds, new[] { "CH-004", "CH-002" }, "CH-003", FactSignificance.Hot, "facts-2"));
        guide.Facts.Add(Fact(4, "2024-04-22", "Brightline completes the changed work",
            "Brightline completed the upper-floor window work described in Change Order 7 and Harmon accepted it without objection.",
            issueIds, new[] { "CH-002" }, "CH-004", FactSignificance.Supporting, "facts-2"));
        guide.Facts.Add(Fact(5, "2024-05-10", "Brightline invoices Change Order 7",
            "Brightline submitted invoice INV-2024-118 to Harmon for the $184,250.00 approved under Change Order 7.",
            issueIds, new[] { "CH-001" }, "CH-004", FactSignificance.Key, "facts-2"));
        guide.Facts.Add(Fact(6, "2024-06-20", "Harmon refuses to pay",
            "Harmon's chief executive refused to pay INV-2024-118 and gave no legitimate reason for withholding the Change Order 7 amount.",
            issueIds, new[] { "CH-003" }, "CH-001", FactSignificance.Key, "facts-2"));

        guide.Values.Add(Value(1, ValueKind.Amount, "Change Order 7 amount", "$184,250.00", "F-003"));
        guide.Values.Add(Value(2, ValueKind.ChangeOrderNumber, "Change order number", "CO-007", "F-003"));
        guide.Values.Add(Value(3, ValueKind.ProjectName, "Project name", "Lakeview Tower", "F-001"));
        guide.Values.Add(Value(4, ValueKind.InvoiceNumber, "Unpaid invoice", "INV-2024-118", "F-005"));

        guide.IdCounters = new Dictionary<string, int>
        {
            ["ISS"] = guide.Issues.Count,
            ["ORG"] = 3,
            ["CH"] = 9,
            ["F"] = 6,
            ["V"] = 4
        };

        foreach (var state in new[] { guide.IssuesState, guide.IssueDetailsState, guide.CastState, guide.FactsState, guide.ValuesState })
            state.Status = GuidePartStatus.Ready;

        draft.Guide = guide;
        return draft;
    }

    /// <summary>
    /// SampleGuidedDraft plus Harmon's counsel so CounselGaps is empty: CH-010 General Counsel at ORG-02, and law firm
    /// ORG-04 (Thistle &amp; Bracken LLP, representing ORG-02) with partner CH-011 and associate CH-012. All three are
    /// hand-edited so a cast regeneration keeps them without the model returning them.
    /// </summary>
    public static CaseDraft SampleCounselDraft()
    {
        var draft = SampleGuidedDraft();
        var guide = draft.Guide!;

        var firm = Organization("ORG-04", "Thistle & Bracken LLP", "Thistle & Bracken", OrganizationKind.LawFirm, "", "thistlebracken.com");
        firm.RepresentsOrgIds.Add("ORG-02");
        guide.Organizations.Add(firm);

        var counsel = new[]
        {
            Member(guide, "CH-010", "Paul", "Novak", "General Counsel", "", "ORG-02", CastTier.Key, CastRole.InHouseCounsel,
                "Harmon's general counsel who answered the demand letter.", "Brief and guarded; copies outside counsel."),
            Member(guide, "CH-011", "Nora", "Quist", "Partner", "", "ORG-04", CastTier.Key, CastRole.OutsideCounsel,
                "Outside counsel defending Harmon.", "Formal and concise."),
            Member(guide, "CH-012", "Owen", "Hale", "Associate", "", "ORG-04", CastTier.Key, CastRole.OutsideCounsel,
                "Associate reviewing Harmon's project records.", "Detailed and careful.")
        };
        foreach (var member in counsel)
        {
            member.IsUserEdited = true;
            guide.Cast.Add(member);
        }

        // Key characters first, then background staff (the generator's cast order)
        guide.Cast = guide.Cast.OrderBy(c => c.Tier == CastTier.Key ? 0 : 1).ThenBy(c => c.Id, StringComparer.Ordinal).ToList();
        guide.IdCounters["ORG"] = 4;
        guide.IdCounters["CH"] = 12;
        return draft;
    }

    private static CaseOrganization Organization(string id, string name, string shortName, OrganizationKind kind, string partyId, string domain) => new()
    {
        Id = id,
        Name = name,
        ShortName = shortName,
        Kind = kind,
        PartyId = partyId,
        Domain = domain,
        Location = "Chicago, Illinois",
        TimeZoneId = "America/Chicago"
    };

    private static CastMember Member(CaseGuide guide, string id, string first, string last, string title, string department,
        string orgId, CastTier tier, CastRole role, string involvement, string voiceNote)
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
            Phone = $"(555) 555-01{id[^2..]}",
            ActiveFrom = "2023-10-01",
            ActiveTo = "2024-12-31",
            Involvement = involvement,
            VoiceNote = voiceNote,
            SignatureStyle = SignatureStyle.Full
        };

        if (tier == CastTier.Key)
            member.Signature = $"{member.FullName}\n{title}\n{org.Name}\n{member.Phone}\n{member.Email}";

        return member;
    }

    private static KeyFact Fact(int number, string date, string title, string description, List<string> issueIds,
        string[] actorIds, string alsoKnownBy, FactSignificance significance, string sourceSectionId) => new()
    {
        Id = $"F-{number:000}",
        Date = date,
        Title = title,
        Description = description,
        IssueIds = new List<string>(issueIds),
        ActorIds = actorIds.ToList(),
        KnownByIds = actorIds.Append(alsoKnownBy).ToList(),
        Significance = significance,
        SourceSectionId = sourceSectionId
    };

    private static CanonicalValue Value(int number, ValueKind kind, string label, string value, string factId) => new()
    {
        Id = $"V-{number:000}",
        Kind = kind,
        Label = label,
        Value = value,
        Description = $"{label} as stated in the case guide; use exactly this text.",
        FactIds = new List<string> { factId }
    };

    public static CaseDraft SampleMemoDraft()
    {
        const string scenario = "A federal task force investigates whether Victor Lane used Desert Bloom Laundromats LLC in Albuquerque to launder cash from a drug distribution ring during 2023.";

        var draft = new CaseDraft
        {
            Scenario = scenario,
            EffectiveScenario = scenario,
            Complexity = ComplexityLevel.Simple,
            CoreDocument = new CoreDocument
            {
                DocumentType = CoreDocumentType.InvestigationMemo,
                MatterKind = MatterKind.Criminal,
                MemoPerspective = MemoPerspective.Government,
                Jurisdiction = new Jurisdiction
                {
                    CourtSystem = CourtSystem.Federal,
                    State = "New Mexico",
                    CourtName = "United States District Court for the District of New Mexico",
                    District = "District of New Mexico",
                    VenueCity = "Albuquerque"
                },
                Title = "Investigation Memorandum: Desert Bloom Laundromats",
                TimelineStart = "2023-02-01",
                TimelineEnd = "2023-08-31",
                Memo = new MemoHeader
                {
                    To = "Assistant U.S. Attorney Rachel Kim",
                    From = "Special Agent Tom Reyes, Task Force 7",
                    Cc = "",
                    Date = "2023-10-02",
                    Re = "Suspected distribution conspiracy - Desert Bloom Laundromats",
                    Organization = "Southwest Financial Crimes Task Force"
                },
                Parties = new List<CaseParty>
                {
                    new()
                    {
                        Id = "party-1",
                        Name = "Victor Lane",
                        ShortName = "Lane",
                        Role = PartyRole.Subject,
                        Description = "Owner and operator of Desert Bloom Laundromats LLC."
                    },
                    new()
                    {
                        Id = "party-2",
                        Name = "Desert Bloom Laundromats LLC",
                        ShortName = "Desert Bloom",
                        Role = PartyRole.Subject,
                        EntityType = "New Mexico limited liability company",
                        Domicile = "Albuquerque, New Mexico",
                        Description = "Chain of cash-intensive laundromats in Albuquerque."
                    }
                },
                Counts = new List<CaseCount>
                {
                    new()
                    {
                        Id = "count-1",
                        Number = 1,
                        Title = "Money laundering through cash-intensive business",
                        AgainstPartyIds = new List<string> { "party-1", "party-2" },
                        LegalBasis = "Federal money laundering statutes",
                        Summary = "Drug proceeds were commingled with laundromat receipts."
                    }
                },
                FactPlan = new List<FactSubsectionPlan>
                {
                    new()
                    {
                        Id = "facts-1",
                        Heading = "Early 2023 cash deposits",
                        StartDate = "2023-02-01",
                        EndDate = "2023-04-30",
                        TargetParagraphs = 3,
                        Focus = "Unusual growth in cash deposits."
                    },
                    new()
                    {
                        Id = "facts-2",
                        Heading = "Summer 2023 structuring",
                        StartDate = "2023-05-01",
                        EndDate = "2023-08-31",
                        TargetParagraphs = 3,
                        Focus = "Deposits structured below reporting thresholds."
                    }
                },
                Sections = new List<CoreDocumentSection>
                {
                    Section("exec-summary", SectionKind.ExecutiveSummary, "EXECUTIVE SUMMARY",
                        "The task force has developed evidence that Lane used Desert Bloom to move cash generated by a drug distribution ring.",
                        "The evidence supports further investigation and consideration of charges."),
                    Section("background", SectionKind.Background, "BACKGROUND",
                        "Desert Bloom operates several laundromats in Albuquerque that take in large volumes of cash.",
                        "Lane acquired the business shortly before the deposit pattern changed."),
                    Section("scope", SectionKind.ScopeMethodology, "SCOPE AND METHODOLOGY",
                        "Agents reviewed bank records, business filings and surveillance reports for the review period.",
                        "Agents also interviewed former employees and a cooperating witness."),
                    Section("key-individuals", SectionKind.KeyIndividuals, "KEY INDIVIDUALS AND ENTITIES",
                        "Lane owns and manages Desert Bloom and controls its bank accounts.",
                        "Desert Bloom is the business through which the cash was deposited."),
                    Section("facts-1", SectionKind.FactualAllegations, "Early 2023 cash deposits",
                        "Cash deposits at Desert Bloom rose sharply without any increase in customer traffic.",
                        "Lane personally delivered many of the deposits to the bank.",
                        "Utility usage at the laundromats stayed flat while reported revenue climbed."),
                    Section("facts-2", SectionKind.FactualAllegations, "Summer 2023 structuring",
                        "Deposits began to arrive in amounts kept just under the bank reporting threshold.",
                        "Several deposits were split across branches on the same day.",
                        "A former employee described being told to keep each deposit small."),
                    Section("finding-1", SectionKind.Finding, "FINDING 1: Money laundering through cash-intensive business",
                        "The deposit pattern is consistent with commingling drug proceeds with legitimate receipts.",
                        "Lane directed the deposits and had knowledge of the source of the cash.",
                        countId: "count-1"),
                    Section("legal-analysis", SectionKind.LegalAnalysis, "LEGAL ANALYSIS AND EXPOSURE",
                        "The conduct described above supports potential money laundering and structuring charges.",
                        "Desert Bloom faces forfeiture exposure for the commingled funds."),
                    Section("recommendations", SectionKind.Recommendations, "CONCLUSIONS AND RECOMMENDATIONS",
                        "The task force recommends grand jury subpoenas for the remaining bank accounts.",
                        "The task force also recommends a financial interview of Lane.")
                }
            }
        };

        draft.CoreDocument.SourceHash = draft.ComputeSourceHash();
        return draft;
    }

    private static CoreDocumentSection Section(string id, SectionKind kind, string heading, string p1, string p2, string? p3 = null, string countId = "")
    {
        var paragraphs = new List<string> { p1, p2 };
        if (p3 != null)
            paragraphs.Add(p3);

        return new CoreDocumentSection
        {
            Id = id,
            Kind = kind,
            Heading = heading,
            CountId = countId,
            Paragraphs = paragraphs,
            Status = SectionStatus.Ready
        };
    }
}
