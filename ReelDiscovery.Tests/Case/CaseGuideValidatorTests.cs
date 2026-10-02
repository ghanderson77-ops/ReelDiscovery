using ReelDiscovery.Models.Case;
using ReelDiscovery.Services.Case;

namespace ReelDiscovery.Tests.Case;

public class CaseGuideValidatorTests
{
    private static CaseDraft Sample() => CaseTestData.SampleGuidedDraft();

    private static CaseGuide G(CaseDraft draft) => draft.Guide!;

    private static List<GuideFinding> Errors(List<GuideFinding> findings) =>
        findings.Where(f => f.Severity == IssueSeverity.Error).ToList();

    private static void AssertFinding(List<GuideFinding> findings, IssueSeverity severity, GuideArea area, string entityId, string message)
    {
        var match = findings.Any(f => f.Severity == severity && f.Area == area && f.EntityId == entityId && f.Message == message);
        Assert.True(match, $"Missing {severity} [{area}] '{entityId}' \"{message}\". Found:\n"
            + string.Join("\n", findings.Select(f => $"{f.Severity} [{f.Area}] '{f.EntityId}' \"{f.Message}\"")));
    }

    private static void AssertNoMessage(List<GuideFinding> findings, string message) =>
        Assert.DoesNotContain(findings, f => f.Message == message);

    private static CastMember AddMember(CaseDraft draft, string id, string first, string last, string orgId, CastTier tier, CastRole role, string title = "Analyst")
    {
        var org = G(draft).FindOrganization(orgId)!;
        var member = new CastMember
        {
            Id = id,
            FirstName = first,
            LastName = last,
            Title = title,
            OrganizationId = orgId,
            Tier = tier,
            Role = role,
            Email = $"{first}.{last}@{org.Domain}".ToLowerInvariant(),
            ActiveFrom = "2023-10-01",
            ActiveTo = "2024-12-31"
        };
        G(draft).Cast.Add(member);
        return member;
    }

    // ---- Fixture, null safety, helpers ----

    [Fact]
    public void Fixture_HasNoErrors()
    {
        var findings = CaseGuideValidator.Validate(Sample());

        Assert.Empty(Errors(findings));
        Assert.Equal(0, CaseGuideValidator.CountErrors(findings));
        Assert.True(CaseGuideValidator.CountWarnings(findings) > 0);
        Assert.DoesNotContain(findings, f => f.Message.StartsWith("A check could not run", StringComparison.Ordinal));
    }

    [Fact]
    public void NullSafe_MissingDraftDocumentOrGuide_ReturnsEmpty()
    {
        Assert.Empty(CaseGuideValidator.Validate(null));

        var noDoc = Sample();
        noDoc.CoreDocument = null;
        Assert.Empty(CaseGuideValidator.Validate(noDoc));

        var noGuide = Sample();
        noGuide.Guide = null;
        Assert.Empty(CaseGuideValidator.Validate(noGuide));
    }

    [Fact]
    public void NullSafe_NullListsAndEntriesAfterFromJson_DoesNotThrow()
    {
        const string json = """
        {
          "CoreDocument": { "Parties": null, "Counts": [null], "Sections": [null, { "Id": "s", "Paragraphs": null }], "Memo": null, "FilingDate": null },
          "Substitutions": [null],
          "Guide": {
            "Issues": null,
            "Organizations": [null, { "Id": null, "Name": null, "Kind": "Party", "RepresentsOrgIds": null, "Domain": null, "PartyId": null }],
            "Cast": [null, { "Id": "CH-001", "FirstName": null, "Email": null, "OrganizationId": null, "ActiveFrom": null }],
            "Facts": [null, { "Id": "F-001", "Date": null, "Title": null, "IssueIds": null, "ActorIds": [null, "CH-001"], "KnownByIds": null, "MappingNotes": [null] }],
            "Values": [null, { "Id": null, "Label": null, "Value": null, "FactIds": null, "MappingNotes": null }],
            "IssuesState": null,
            "CastState": null,
            "FactsState": { "Status": "Ready", "BasisHash": null, "BasisComponents": null }
          }
        }
        """;

        var draft = CaseDraft.FromJson(json);

        var findings = CaseGuideValidator.Validate(draft);

        Assert.NotNull(findings);
        Assert.DoesNotContain(findings, f => f.Message.StartsWith("A check could not run", StringComparison.Ordinal));
    }

    [Fact]
    public void NullSafe_HugeText_DoesNotThrow()
    {
        var draft = Sample();
        G(draft).Facts[0].Description = string.Concat(Enumerable.Repeat("Acme $1,000 PO-12 ", 120_000));

        var findings = CaseGuideValidator.Validate(draft);

        Assert.NotNull(findings);
    }

    [Fact]
    public void Where_FormatsAreaAndEntity()
    {
        Assert.Equal("Key Facts · F-012", new GuideFinding { Area = GuideArea.Facts, EntityId = "F-012" }.Where);
        Assert.Equal("Cast · CH-007", new GuideFinding { Area = GuideArea.Cast, EntityId = "CH-007" }.Where);
        Assert.Equal("Canonical Values · V-004", new GuideFinding { Area = GuideArea.Values, EntityId = "V-004" }.Where);
        Assert.Equal("Issues · ISS-01", new GuideFinding { Area = GuideArea.Issues, EntityId = "ISS-01" }.Where);
        Assert.Equal("Guide", new GuideFinding { Area = GuideArea.Guide }.Where);
    }

    [Fact]
    public void WorstFor_ErrorBeatsWarning_NoneIsNull()
    {
        var findings = new List<GuideFinding>
        {
            new() { Severity = IssueSeverity.Warning, Area = GuideArea.Facts, EntityId = "F-001", Message = "a" },
            new() { Severity = IssueSeverity.Error, Area = GuideArea.Facts, EntityId = "F-001", Message = "b" },
            new() { Severity = IssueSeverity.Warning, Area = GuideArea.Facts, EntityId = "F-002", Message = "c" }
        };

        Assert.Equal(IssueSeverity.Error, CaseGuideValidator.WorstFor(findings, "F-001"));
        Assert.Equal(IssueSeverity.Warning, CaseGuideValidator.WorstFor(findings, "F-002"));
        Assert.Null(CaseGuideValidator.WorstFor(findings, "F-003"));
        Assert.Equal(2, CaseGuideValidator.FindingsFor(findings, "F-001").Count);
    }

    // ---- Rule 1: ids ----

    [Fact]
    public void DuplicateId_IsError()
    {
        var draft = Sample();
        G(draft).Facts[1].Id = "F-001";

        AssertFinding(CaseGuideValidator.Validate(draft), IssueSeverity.Error, GuideArea.Facts, "F-001", "Two entries use the ID F-001.");
    }

    [Fact]
    public void EmptyId_IsError()
    {
        var draft = Sample();
        G(draft).Cast.Single(c => c.Id == "CH-009").Id = "";

        AssertFinding(CaseGuideValidator.Validate(draft), IssueSeverity.Error, GuideArea.Cast, "", "An entry has no ID.");
    }

    // ---- Rules 2-9: facts ----

    [Fact]
    public void FactUnknownIssue_IsError()
    {
        var draft = Sample();
        G(draft).Facts[0].IssueIds.Add("ISS-09");

        AssertFinding(CaseGuideValidator.Validate(draft), IssueSeverity.Error, GuideArea.Facts, "F-001", "F-001 links to issue ISS-09, which does not exist.");
    }

    [Fact]
    public void FactUnknownActorOrKnower_IsError()
    {
        var draft = Sample();
        G(draft).Facts[0].ActorIds.Add("CH-099");
        G(draft).Facts[1].KnownByIds.Add("CH-098");

        var findings = CaseGuideValidator.Validate(draft);

        AssertFinding(findings, IssueSeverity.Error, GuideArea.Facts, "F-001", "F-001 refers to a character that does not exist (CH-099).");
        AssertFinding(findings, IssueSeverity.Error, GuideArea.Facts, "F-002", "F-002 refers to a character that does not exist (CH-098).");
    }

    [Fact]
    public void FactWithoutIssue_IsError()
    {
        var draft = Sample();
        G(draft).Facts[0].IssueIds.Clear();

        AssertFinding(CaseGuideValidator.Validate(draft), IssueSeverity.Error, GuideArea.Facts, "F-001", "F-001 is not linked to any issue.");
    }

    [Fact]
    public void FactWithoutActors_IsError()
    {
        var draft = Sample();
        G(draft).Facts[3].ActorIds.Clear();

        AssertFinding(CaseGuideValidator.Validate(draft), IssueSeverity.Error, GuideArea.Facts, "F-004", "F-004 has no one under Who did it.");
    }

    [Fact]
    public void FactWithoutTitle_IsError()
    {
        var draft = Sample();
        G(draft).Facts[0].Title = "  ";

        AssertFinding(CaseGuideValidator.Validate(draft), IssueSeverity.Error, GuideArea.Facts, "F-001", "F-001 has no title.");
    }

    [Theory]
    [InlineData("2024-13-01")]
    [InlineData("")]
    [InlineData("March 3, 2024")]
    public void FactBadDate_IsError(string date)
    {
        var draft = Sample();
        G(draft).Facts[0].Date = date;

        AssertFinding(CaseGuideValidator.Validate(draft), IssueSeverity.Error, GuideArea.Facts, "F-001", "F-001 has no valid date. Use YYYY-MM-DD.");
    }

    [Fact]
    public void FactAfterFilingDate_IsError_AndOutsideTimelineWarning()
    {
        var draft = Sample();
        G(draft).Facts[5].Date = "2024-10-01";

        var findings = CaseGuideValidator.Validate(draft);

        AssertFinding(findings, IssueSeverity.Error, GuideArea.Facts, "F-006", "F-006 is dated 2024-10-01, after the filing date (2024-09-16).");
        AssertFinding(findings, IssueSeverity.Warning, GuideArea.Facts, "F-006", "F-006 is dated 2024-10-01, outside the timeline (2024-01-08 to 2024-06-28).");
    }

    [Fact]
    public void FactAfterMemoDate_IsError()
    {
        var draft = Sample();
        draft.CoreDocument!.DocumentType = CoreDocumentType.InvestigationMemo;
        draft.CoreDocument.Memo.Date = "2024-06-01";

        AssertFinding(CaseGuideValidator.Validate(draft), IssueSeverity.Error, GuideArea.Facts, "F-006", "F-006 is dated 2024-06-20, after the memo date (2024-06-01).");
    }

    [Fact]
    public void FactOutsideTimeline_IsWarning()
    {
        var draft = Sample();
        G(draft).Facts[0].Date = "2024-01-02";

        var findings = CaseGuideValidator.Validate(draft);

        AssertFinding(findings, IssueSeverity.Warning, GuideArea.Facts, "F-001", "F-001 is dated 2024-01-02, outside the timeline (2024-01-08 to 2024-06-28).");
        Assert.Empty(Errors(findings));
    }

    [Fact]
    public void ActorInactive_IsError()
    {
        var draft = Sample();
        G(draft).FindCast("CH-003")!.ActiveTo = "2024-06-01";

        AssertFinding(CaseGuideValidator.Validate(draft), IssueSeverity.Error, GuideArea.Facts, "F-006",
            "F-006 is dated 2024-06-20, but Raymond Harmon (CH-003) is only active 2023-10-01 to 2024-06-01.");
    }

    [Fact]
    public void KnowerInactive_IsWarning()
    {
        var draft = Sample();
        G(draft).FindCast("CH-005")!.ActiveTo = "2024-01-10";

        var findings = CaseGuideValidator.Validate(draft);

        AssertFinding(findings, IssueSeverity.Warning, GuideArea.Facts, "F-001",
            "F-001 is dated 2024-01-15, but Karen Walsh (CH-005), who knew about it, is only active 2023-10-01 to 2024-01-10.");
        Assert.Empty(Errors(findings));
    }

    [Fact]
    public void ActorNotKnower_IsWarning()
    {
        var draft = Sample();
        G(draft).Facts[3].KnownByIds.Remove("CH-002");

        AssertFinding(CaseGuideValidator.Validate(draft), IssueSeverity.Warning, GuideArea.Facts, "F-004", "F-004: Tom Becker did it but is not listed under Who knew.");
    }

    // ---- Rules 10-13: cast and organizations ----

    [Theory]
    [InlineData("", "2024-12-31")]
    [InlineData("2023-10-01", "not a date")]
    [InlineData("2024-12-31", "2023-10-01")]
    public void ActiveRangeInvalid_IsError(string from, string to)
    {
        var draft = Sample();
        var member = G(draft).FindCast("CH-009")!;
        member.ActiveFrom = from;
        member.ActiveTo = to;

        AssertFinding(CaseGuideValidator.Validate(draft), IssueSeverity.Error, GuideArea.Cast, "CH-009",
            "Owen Reilly (CH-009) has an active range that is missing or ends before it starts.");
    }

    [Fact]
    public void CastWithoutName_IsError()
    {
        var draft = Sample();
        var member = G(draft).FindCast("CH-009")!;
        member.FirstName = "";
        member.LastName = " ";

        AssertFinding(CaseGuideValidator.Validate(draft), IssueSeverity.Error, GuideArea.Cast, "CH-009", "CH-009 has no name.");
    }

    [Fact]
    public void InvalidEmail_IsError()
    {
        var draft = Sample();
        G(draft).FindCast("CH-009")!.Email = "not-an-email";

        AssertFinding(CaseGuideValidator.Validate(draft), IssueSeverity.Error, GuideArea.Cast, "CH-009", "Owen Reilly (CH-009) has an invalid email address.");
    }

    [Fact]
    public void DuplicateEmail_IsErrorOnEach()
    {
        var draft = Sample();
        G(draft).FindCast("CH-009")!.Email = "tom.becker@brightline.com";

        var findings = CaseGuideValidator.Validate(draft);

        AssertFinding(findings, IssueSeverity.Error, GuideArea.Cast, "CH-002", "tom.becker@brightline.com is used by more than one character.");
        AssertFinding(findings, IssueSeverity.Error, GuideArea.Cast, "CH-009", "tom.becker@brightline.com is used by more than one character.");
    }

    [Fact]
    public void EmailOffOrganizationDomain_IsWarning_UnlessUserEdited()
    {
        var draft = Sample();
        var member = G(draft).FindCast("CH-009")!;
        member.Email = "owen.reilly@gmail.com";
        const string message = "Owen Reilly's email does not use Brightline Glazing, LLC's domain (brightline.com).";

        AssertFinding(CaseGuideValidator.Validate(draft), IssueSeverity.Warning, GuideArea.Cast, "CH-009", message);

        member.EmailIsUserEdited = true;
        AssertNoMessage(CaseGuideValidator.Validate(draft), message);
    }

    [Fact]
    public void ReservedEmailDomain_IsWarning()
    {
        var draft = Sample();
        G(draft).FindCast("CH-009")!.Email = "owen.reilly@brightline.example";

        AssertFinding(CaseGuideValidator.Validate(draft), IssueSeverity.Warning, GuideArea.Cast, "CH-009", "Owen Reilly's email uses a reserved test domain.");
    }

    [Fact]
    public void UnknownOrganization_IsError()
    {
        var draft = Sample();
        G(draft).FindCast("CH-009")!.OrganizationId = "ORG-99";

        AssertFinding(CaseGuideValidator.Validate(draft), IssueSeverity.Error, GuideArea.Cast, "CH-009",
            "Owen Reilly (CH-009) belongs to an organization that does not exist (ORG-99).");
    }

    [Fact]
    public void SharedOrganizationDomain_IsError()
    {
        var draft = Sample();
        G(draft).FindOrganization("ORG-02")!.Domain = "Brightline.com";

        AssertFinding(CaseGuideValidator.Validate(draft), IssueSeverity.Error, GuideArea.Guide, "ORG-02",
            "Brightline Glazing, LLC and Harmon Builders, Inc. use the same email domain (brightline.com).");
    }

    // ---- Rules 14-16: issues and key characters ----

    [Fact]
    public void IssueFromRemovedCount_IsWarning()
    {
        var draft = Sample();
        G(draft).Issues[0].CountId = "count-9";

        AssertFinding(CaseGuideValidator.Validate(draft), IssueSeverity.Warning, GuideArea.Issues, "ISS-01",
            "ISS-01 comes from a count that is no longer in the core document.");
    }

    [Fact]
    public void IssueWithoutFacts_IsWarning()
    {
        var draft = Sample();
        G(draft).Issues.Add(new CaseIssue { Id = "ISS-02", CountId = "count-1", Number = 2, Title = "Unjust Enrichment" });

        AssertFinding(CaseGuideValidator.Validate(draft), IssueSeverity.Warning, GuideArea.Issues, "ISS-02", "No fact is linked to ISS-02 (Unjust Enrichment).");
    }

    [Fact]
    public void KeyCharacterWithoutFacts_IsWarning_CounselAndBackgroundExempt()
    {
        var draft = Sample();
        AddMember(draft, "CH-010", "Dana", "Fox", "ORG-02", CastTier.Key, CastRole.Staff);

        var findings = CaseGuideValidator.Validate(draft);

        AssertFinding(findings, IssueSeverity.Warning, GuideArea.Cast, "CH-010", "Dana Fox (CH-010) is a key character but appears in no fact.");
        AssertNoMessage(findings, "Morgan Ellis (CH-006) is a key character but appears in no fact.");
        AssertNoMessage(findings, "Owen Reilly (CH-009) is a key character but appears in no fact.");
    }

    // ---- Rule 17: counsel coverage ----

    [Fact]
    public void CounselCoverage_FixtureDefendantHasNoCounsel()
    {
        var findings = CaseGuideValidator.Validate(Sample());

        AssertFinding(findings, IssueSeverity.Warning, GuideArea.Guide, "ORG-02", "Harmon Builders, Inc. has no in-house counsel.");
        AssertFinding(findings, IssueSeverity.Warning, GuideArea.Guide, "ORG-02", "Harmon Builders, Inc. has no outside law firm with a partner and an associate.");
        AssertNoMessage(findings, "Brightline Glazing, LLC has no in-house counsel.");
        AssertNoMessage(findings, "Brightline Glazing, LLC has no outside law firm with a partner and an associate.");
    }

    [Fact]
    public void CounselCoverage_FirmWithoutAssociate_IsWarning()
    {
        var draft = Sample();
        G(draft).FindCast("CH-007")!.Title = "Paralegal";

        AssertFinding(CaseGuideValidator.Validate(draft), IssueSeverity.Warning, GuideArea.Guide, "ORG-01",
            "Brightline Glazing, LLC has no outside law firm with a partner and an associate.");
    }

    [Fact]
    public void IndividualPartyCounsel_OutsideOnly()
    {
        var draft = Sample();
        G(draft).Organizations.Add(new CaseOrganization { Id = "ORG-04", Name = "Dana Ortiz", Kind = OrganizationKind.Individual, Domain = "ortizfamily.com" });

        var findings = CaseGuideValidator.Validate(draft);

        AssertFinding(findings, IssueSeverity.Warning, GuideArea.Guide, "ORG-04", "Dana Ortiz has no outside law firm with a partner and an associate.");
        AssertNoMessage(findings, "Dana Ortiz has no in-house counsel.");
        Assert.DoesNotContain(findings, f => f.Message.StartsWith("Dana Ortiz has 0 background staff", StringComparison.Ordinal));
    }

    // ---- Rule 18: preset ----

    [Fact]
    public void PresetRanges_AreWarnings()
    {
        var findings = CaseGuideValidator.Validate(Sample());

        AssertFinding(findings, IssueSeverity.Warning, GuideArea.Guide, "", "The cast has 4 key characters (not counting counsel); the Simple preset expects 6-10.");
        AssertFinding(findings, IssueSeverity.Warning, GuideArea.Guide, "ORG-01", "Brightline Glazing, LLC has 1 background staff; the Simple preset expects about 3.");
        AssertFinding(findings, IssueSeverity.Warning, GuideArea.Guide, "", "The guide has 6 key facts; the Simple preset expects 12-20.");
    }

    // ---- Rule 19: canonical values ----

    [Fact]
    public void ValueLabelConflict_IsError()
    {
        var draft = Sample();
        G(draft).Values.Add(new CanonicalValue { Id = "V-005", Kind = ValueKind.Amount, Label = "change order 7 amount", Value = "$184,205.00" });

        AssertFinding(CaseGuideValidator.Validate(draft), IssueSeverity.Error, GuideArea.Values, "V-005",
            "\"Change Order 7 amount\" has two different values: $184,250.00 and $184,205.00.");
    }

    [Fact]
    public void ValueWithoutValue_IsError()
    {
        var draft = Sample();
        G(draft).Values[2].Value = "";

        AssertFinding(CaseGuideValidator.Validate(draft), IssueSeverity.Error, GuideArea.Values, "V-003", "V-003 has no exact value.");
    }

    [Fact]
    public void ValueUnknownFact_IsError()
    {
        var draft = Sample();
        G(draft).Values[2].FactIds.Add("F-099");

        AssertFinding(CaseGuideValidator.Validate(draft), IssueSeverity.Error, GuideArea.Values, "V-003", "V-003 links to fact F-099, which does not exist.");
    }

    [Fact]
    public void SameValueTwoLabels_IsWarning()
    {
        var draft = Sample();
        G(draft).Values.Add(new CanonicalValue { Id = "V-005", Kind = ValueKind.ProjectName, Label = "Project", Value = "Lakeview Tower" });

        AssertFinding(CaseGuideValidator.Validate(draft), IssueSeverity.Warning, GuideArea.Values, "V-005",
            "Lakeview Tower is registered under two labels: Project name and Project.");
    }

    [Fact]
    public void UnregisteredScannedValues_AreWarnings_ReportedOnce()
    {
        var draft = Sample();
        draft.CoreDocument!.FindSection("facts-2")!.Paragraphs.Add("Harmon later offered $50,000 to settle PO-4471.");
        G(draft).Facts[1].Description += " Purchase order PO-4471 and invoice INV-2024-200 were attached.";

        var findings = CaseGuideValidator.Validate(draft);

        AssertFinding(findings, IssueSeverity.Warning, GuideArea.Values, "", "$50,000 appears in the core document but is not in the canonical values.");
        AssertFinding(findings, IssueSeverity.Warning, GuideArea.Values, "", "PO-4471 appears in the core document but is not in the canonical values.");
        AssertFinding(findings, IssueSeverity.Warning, GuideArea.Values, "", "INV-2024-200 appears in the key facts but is not in the canonical values.");
        AssertNoMessage(findings, "PO-4471 appears in the key facts but is not in the canonical values.");
        Assert.DoesNotContain(findings, f => f.Message.StartsWith("$184,250.00 appears", StringComparison.Ordinal));
        Assert.DoesNotContain(findings, f => f.Message.StartsWith("INV-2024-118 appears", StringComparison.Ordinal));
    }

    [Fact]
    public void RegisteredValueNotInText_IsWarning()
    {
        var findings = CaseGuideValidator.Validate(Sample());

        AssertFinding(findings, IssueSeverity.Warning, GuideArea.Values, "V-002", "V-002 (CO-007) does not appear in the core document or any fact.");
        Assert.DoesNotContain(findings, f => f.Message.StartsWith("V-001 ($184,250.00) does not appear", StringComparison.Ordinal));
        Assert.DoesNotContain(findings, f => f.Message.StartsWith("V-003 (", StringComparison.Ordinal));
    }

    // ---- Rule 20: real-name leaks ----

    [Fact]
    public void Leak_MultiWordIsError_SingleTokenIsWarning()
    {
        var draft = Sample();
        G(draft).Facts[2].Description += " Acme Windows supplied the glass.";
        G(draft).FindCast("CH-002")!.Involvement = "Formerly worked at Acme.";

        var findings = CaseGuideValidator.Validate(draft);

        AssertFinding(findings, IssueSeverity.Error, GuideArea.Facts, "F-003", "F-003 uses the real name \"Acme Windows\".");
        AssertFinding(findings, IssueSeverity.Warning, GuideArea.Cast, "CH-002", "CH-002 uses the real name \"Acme\".");
    }

    [Fact]
    public void Leak_InOrganizationAndValue()
    {
        var draft = Sample();
        G(draft).FindOrganization("ORG-02")!.Name = "Acme Windows Holdings";
        G(draft).Values[2].Description = "Formerly the Acme Windows plant.";

        var findings = CaseGuideValidator.Validate(draft);

        AssertFinding(findings, IssueSeverity.Error, GuideArea.Guide, "ORG-02", "ORG-02 uses the real name \"Acme Windows\".");
        AssertFinding(findings, IssueSeverity.Error, GuideArea.Values, "V-003", "V-003 uses the real name \"Acme Windows\".");
    }

    // ---- Rule 21: unfinished parts ----

    [Theory]
    [InlineData(GuidePart.Issues, GuidePartStatus.Pending, "The issues were not written.")]
    [InlineData(GuidePart.Cast, GuidePartStatus.Generating, "The cast was not written.")]
    [InlineData(GuidePart.Facts, GuidePartStatus.Failed, "The key facts were not written.")]
    [InlineData(GuidePart.Values, GuidePartStatus.Pending, "The canonical values were not written.")]
    public void UnfinishedPart_IsError(GuidePart part, GuidePartStatus status, string message)
    {
        var draft = Sample();
        G(draft).State(part).Status = status;

        AssertFinding(CaseGuideValidator.Validate(draft), IssueSeverity.Error, GuideArea.Guide, "", message);
    }

    [Fact]
    public void IssueDetailsFailed_IsWarning()
    {
        var draft = Sample();
        G(draft).IssueDetailsState.Status = GuidePartStatus.Failed;

        var findings = CaseGuideValidator.Validate(draft);

        AssertFinding(findings, IssueSeverity.Warning, GuideArea.Guide, "", "The issue details were not written. Click Retry Issue Details.");
        Assert.Empty(Errors(findings));
    }

    [Theory]
    [InlineData(GuidePartStatus.Pending)]
    [InlineData(GuidePartStatus.Generating)]
    public void IssueDetailsInterrupted_IsWarning(GuidePartStatus status)
    {
        // CR-02: a cancel or fatal error leaves the details Pending (a closed run leaves them Generating) with issues missing details
        var draft = Sample();
        G(draft).IssueDetailsState.Status = status;
        G(draft).Issues[0].Description = string.Empty;
        G(draft).Issues[0].RelevanceCriteria = string.Empty;

        var findings = CaseGuideValidator.Validate(draft);

        AssertFinding(findings, IssueSeverity.Warning, GuideArea.Guide, "", "The issue details were not written. Click Retry Issue Details.");
    }

    // ---- Rule 22: staleness and review ----

    private static void StampAll(CaseDraft draft)
    {
        foreach (var part in GuideStaleness.Order)
            GuideStaleness.Stamp(draft, part);
    }

    [Fact]
    public void Stale_CoreDocumentEdit()
    {
        var draft = Sample();
        StampAll(draft);
        draft.CoreDocument!.FindSection("facts-1")!.Paragraphs[0] += " The parties met on site.";

        var findings = CaseGuideValidator.Validate(draft);

        AssertFinding(findings, IssueSeverity.Warning, GuideArea.Guide, "", "The issues are out of date with the core document.");
        AssertFinding(findings, IssueSeverity.Warning, GuideArea.Guide, "", "The cast is out of date with the core document.");
        AssertFinding(findings, IssueSeverity.Warning, GuideArea.Guide, "", "The key facts are out of date with the core document.");
        AssertFinding(findings, IssueSeverity.Warning, GuideArea.Guide, "", "The canonical values are out of date with the core document.");
    }

    [Fact]
    public void Stale_CastNameEdit()
    {
        var draft = Sample();
        StampAll(draft);
        G(draft).FindCast("CH-002")!.FirstName = "Thomas";

        var findings = CaseGuideValidator.Validate(draft);

        AssertFinding(findings, IssueSeverity.Warning, GuideArea.Guide, "", "The key facts are out of date with the cast.");
        Assert.DoesNotContain(findings, f => f.Message.StartsWith("The issues are out of date", StringComparison.Ordinal));
        Assert.DoesNotContain(findings, f => f.Message.StartsWith("The canonical values are out of date", StringComparison.Ordinal));
    }

    [Fact]
    public void NeedsReviewFact_IsWarning()
    {
        var draft = Sample();
        var fact = G(draft).Facts[1];
        fact.NeedsReview = true;
        fact.ReviewReason = "Tom Becker was changed after it was written.";

        AssertFinding(CaseGuideValidator.Validate(draft), IssueSeverity.Warning, GuideArea.Facts, "F-002", "F-002 needs review: Tom Becker was changed after it was written.");
    }

    [Fact]
    public void NeedsReviewValue_ChangedFact()
    {
        var draft = Sample();
        Assert.True(CaseGuideEditing.SetFactField(draft, "F-003", FactField.Title, "Harmon approves CO-007").Changed);

        AssertFinding(CaseGuideValidator.Validate(draft), IssueSeverity.Warning, GuideArea.Values, "V-001", "V-001 is linked to F-003, which you changed.");
    }

    [Fact]
    public void NeedsReviewValue_DeletedFact()
    {
        var draft = Sample();
        Assert.True(CaseGuideEditing.DeleteFact(draft, "F-003"));

        var findings = CaseGuideValidator.Validate(draft);

        AssertFinding(findings, IssueSeverity.Warning, GuideArea.Values, "V-001", "V-001 needs review: F-003 was deleted.");
        AssertNoMessage(findings, "V-001 is linked to F-003, which you changed.");
    }

    // ---- Rule 23: mapping notes ----

    [Fact]
    public void MappingNotes_AreWarnings()
    {
        var draft = Sample();
        G(draft).Facts[0].MappingNotes.Add("Could not map \"Bob\" to a character.");
        G(draft).Values[0].MappingNotes.Add("Dropped unknown fact F-200.");

        var findings = CaseGuideValidator.Validate(draft);

        AssertFinding(findings, IssueSeverity.Warning, GuideArea.Facts, "F-001", "F-001: Could not map \"Bob\" to a character.");
        AssertFinding(findings, IssueSeverity.Warning, GuideArea.Values, "V-001", "V-001: Dropped unknown fact F-200.");
    }

    // ---- Rule 24: parties vs organizations ----

    [Fact]
    public void PartyWithoutOrganization_IsError()
    {
        var draft = Sample();
        draft.CoreDocument!.Parties.Add(new CaseParty { Id = "party-3", Name = "Lakeview Tower Owner LLC", Role = PartyRole.Defendant });

        AssertFinding(CaseGuideValidator.Validate(draft), IssueSeverity.Error, GuideArea.Guide, "",
            "Party Lakeview Tower Owner LLC has no organization. Click Update Guide or Regenerate Cast.");
    }

    [Fact]
    public void PartyRenamed_IsWarning()
    {
        var draft = Sample();
        draft.CoreDocument!.Parties[1].Name = "Harmon Construction, Inc.";

        var findings = CaseGuideValidator.Validate(draft);

        AssertFinding(findings, IssueSeverity.Warning, GuideArea.Guide, "ORG-02", "Harmon Builders, Inc. name differs from party Harmon Construction, Inc.");
        AssertNoMessage(findings, "Brightline Glazing, LLC name differs from party Brightline Glazing, LLC.");
    }

    [Fact]
    public void PartyNameDiffersOnlyByCaseOrSpace_NoWarning()
    {
        var draft = Sample();
        draft.CoreDocument!.Parties[1].Name = "  harmon builders, inc. ";

        Assert.DoesNotContain(CaseGuideValidator.Validate(draft), f => f.Message.Contains("name differs from party", StringComparison.Ordinal));
    }
}
