using ReelDiscovery.Models.Case;
using ReelDiscovery.Services.Case;

namespace ReelDiscovery.Tests.Case;

/// <summary>
/// Fix hints and quick fixes for Checks findings: every Error says how to fix it, the automatic fixes are pure and
/// idempotent, and applying them all to a broken guide clears every error they cover.
/// </summary>
public class CaseGuideQuickFixTests
{
    private static CaseDraft Sample() => CaseTestData.SampleGuidedDraft();

    private static GuideFinding Finding(CaseDraft draft, string messageStart) =>
        CaseGuideValidator.Validate(draft).FirstOrDefault(f => f.Message.StartsWith(messageStart, StringComparison.Ordinal))
            ?? throw new Xunit.Sdk.XunitException($"No finding starting \"{messageStart}\". Found:\n"
                + string.Join("\n", CaseGuideValidator.Validate(draft).Select(f => f.Message)));

    private static bool Has(CaseDraft draft, string messageStart) =>
        CaseGuideValidator.Validate(draft).Any(f => f.Message.StartsWith(messageStart, StringComparison.Ordinal));

    private static CastMember Member(CaseDraft draft, string id, string first, string last, string orgId, string email = "")
    {
        var member = new CastMember
        {
            Id = id,
            FirstName = first,
            LastName = last,
            Title = "Manager",
            OrganizationId = orgId,
            Tier = CastTier.Key,
            Role = CastRole.Staff,
            Email = email.Length > 0 ? email : $"{first}.{last}@harmon.com".ToLowerInvariant(),
            ActiveFrom = "2023-10-01",
            ActiveTo = "2024-12-31"
        };
        draft.Guide!.Cast.Add(member);
        return member;
    }

    /// <summary>
    /// The sample guide broken so that every Error rule fires at least once.
    /// </summary>
    private static CaseDraft BrokenDraft()
    {
        var draft = Sample();
        var guide = draft.Guide!;
        var doc = draft.CoreDocument!;

        // Ids: a value with no id, two values sharing V-004
        guide.Values.Add(new CanonicalValue { Id = "", Kind = ValueKind.Other, Label = "Site address", Value = "100 Lakeview Drive" });
        guide.Values.Add(new CanonicalValue { Id = "V-004", Kind = ValueKind.Other, Label = "Retainage", Value = "5 percent" });

        // Facts: broken links, missing issue / actors / title, bad dates, after the filing date, inactive actor, real name
        guide.FindFact("F-001")!.IssueIds.Add("ISS-99");
        guide.FindFact("F-001")!.Description += " Acme Windows signed it too.";
        guide.FindFact("F-002")!.ActorIds.Add("CH-099");
        guide.FindFact("F-003")!.IssueIds.Clear();
        guide.FindFact("F-004")!.ActorIds.Clear();
        guide.FindFact("F-005")!.Title = "";
        guide.FindFact("F-006")!.Date = "June 20, 2024";
        var issueIds = guide.Issues.Select(i => i.Id).ToList();
        guide.Facts.Add(new KeyFact { Id = "F-007", Date = "sometime in spring", Title = "Undated call", IssueIds = issueIds.ToList(), ActorIds = { "CH-001" }, KnownByIds = { "CH-001" } });
        guide.Facts.Add(new KeyFact { Id = "F-008", Date = "2024-10-01", Title = "Late letter", IssueIds = issueIds.ToList(), ActorIds = { "CH-003" }, KnownByIds = { "CH-003" } });
        guide.FindCast("CH-002")!.ActiveTo = "2024-03-01"; // F-003 (2024-03-18) names Tom Becker under Who did it

        // Cast: no name, missing range, invalid and shared email, unknown organization
        guide.FindCast("CH-009")!.FirstName = "";
        guide.FindCast("CH-009")!.LastName = "";
        guide.FindCast("CH-008")!.ActiveFrom = "";
        guide.FindCast("CH-007")!.Email = "not-an-email";
        guide.FindCast("CH-006")!.Email = guide.FindCast("CH-005")!.Email;
        Member(draft, "CH-010", "Gale", "Boetticher", "ORG-99", "gale.boetticher@harmon.com");

        // Organizations: the law firm shares Harmon's domain
        guide.FindOrganization("ORG-03")!.Domain = "harmon.com";

        // Values: no exact value, a link to a missing fact, one label with two values
        guide.FindValue("V-003")!.Value = "";
        guide.FindValue("V-001")!.FactIds.Add("F-099");
        guide.Values.Add(new CanonicalValue { Id = "V-006", Kind = ValueKind.Amount, Label = "Change Order 7 amount", Value = "$190,000.00" });

        // Parts: the canonical values were not written
        guide.ValuesState.Status = GuidePartStatus.Pending;

        // Parties: an individual whose namesake is linked to a party that is gone, an individual with no character,
        // a party whose namesake organization is unlinked, and a party with no organization at all
        doc.Parties.Add(new CaseParty { Id = "party-3", Name = "Ryan Howard", Role = PartyRole.Defendant, EntityType = "individual" });
        Member(draft, "CH-011", "Ryan", "Howard", "ORG-02").PartyId = "party-gone";
        doc.Parties.Add(new CaseParty { Id = "party-4", Name = "Kelly Kapoor", Role = PartyRole.Defendant, EntityType = "individual" });
        doc.Parties.Add(new CaseParty { Id = "party-5", Name = "Lakeview Tower Owner LLC", Role = PartyRole.Defendant, EntityType = "Illinois limited liability company" });
        guide.Organizations.Add(new CaseOrganization { Id = "ORG-04", Name = "Lakeview Tower Owner LLC", ShortName = "Lakeview Owner", Kind = OrganizationKind.Party, Domain = "lakeviewowner.com" });
        doc.Parties.Add(new CaseParty { Id = "party-6", Name = "Northgate Bank", Role = PartyRole.ThirdParty, EntityType = "national bank" });
        return draft;
    }

    // Every Error rule, by the start of its message, with the quick fix it must offer
    public static IEnumerable<object[]> ErrorRules() => new[]
    {
        new object[] { "An entry has no ID.", GuideQuickFix.AssignNewId },
        new object[] { "Two entries use the ID V-004.", GuideQuickFix.AssignNewId },
        new object[] { "F-001 links to issue ISS-99", GuideQuickFix.RemoveBrokenLinks },
        new object[] { "F-002 refers to a character that does not exist (CH-099)", GuideQuickFix.RemoveBrokenLinks },
        new object[] { "F-003 is not linked to any issue.", GuideQuickFix.RegenerateEntry },
        new object[] { "F-004 has no one under Who did it.", GuideQuickFix.RegenerateEntry },
        new object[] { "F-005 has no title.", GuideQuickFix.RegenerateEntry },
        new object[] { "F-006 has no valid date.", GuideQuickFix.NormalizeDate },
        new object[] { "F-007 has no valid date.", GuideQuickFix.RegenerateEntry },
        new object[] { "F-008 is dated 2024-10-01, after the filing date", GuideQuickFix.None },
        new object[] { "F-003 is dated 2024-03-18, but Tom Becker (CH-002) is only active", GuideQuickFix.WidenActiveRange },
        new object[] { "CH-009 has no name.", GuideQuickFix.RegenerateEntry },
        new object[] { "Priya Nair (CH-008) has an active range that is missing", GuideQuickFix.WidenActiveRange },
        new object[] { "Evan Price (CH-007) has an invalid email address.", GuideQuickFix.ResetEmail },
        new object[] { "karen.walsh@brightline.com is used by more than one character.", GuideQuickFix.ResetEmail },
        new object[] { "Gale Boetticher (CH-010) belongs to an organization that does not exist", GuideQuickFix.RegenerateCast },
        new object[] { "Harmon Builders, Inc. and Ellis & Park LLP use the same email domain", GuideQuickFix.NewEmailDomain },
        new object[] { "V-003 has no exact value.", GuideQuickFix.RegenerateEntry },
        new object[] { "V-001 links to fact F-099", GuideQuickFix.RemoveBrokenLinks },
        new object[] { "\"Change Order 7 amount\" has two different values", GuideQuickFix.None },
        new object[] { "F-001 uses the real name \"Acme Windows\".", GuideQuickFix.ReplaceRealNames },
        new object[] { "The canonical values were not written.", GuideQuickFix.ResumeGuide },
        new object[] { "Party Ryan Howard is not in the cast.", GuideQuickFix.LinkParty },
        new object[] { "Party Kelly Kapoor is not in the cast.", GuideQuickFix.RegenerateCast },
        new object[] { "Party Lakeview Tower Owner LLC has no organization.", GuideQuickFix.LinkParty },
        new object[] { "Party Northgate Bank has no organization.", GuideQuickFix.RegenerateCast }
    };

    [Theory]
    [MemberData(nameof(ErrorRules))]
    public void BrokenDraft_EachErrorRule_HasHintAndExpectedQuickFix(string messageStart, GuideQuickFix expected)
    {
        var finding = CaseGuideValidator.Validate(BrokenDraft())
            .Where(f => f.Severity == IssueSeverity.Error)
            .FirstOrDefault(f => f.Message.StartsWith(messageStart, StringComparison.Ordinal));

        Assert.True(finding != null, $"The broken draft must raise \"{messageStart}\"");
        Assert.False(string.IsNullOrWhiteSpace(finding!.FixHint), finding.Message);
        Assert.Equal(expected, finding.QuickFix);
    }

    [Fact]
    public void BrokenDraft_EveryErrorHasAFixHint()
    {
        var errors = CaseGuideValidator.Validate(BrokenDraft()).Where(f => f.Severity == IssueSeverity.Error).ToList();

        Assert.True(errors.Count >= ErrorRules().Count());
        Assert.All(errors, e => Assert.False(string.IsNullOrWhiteSpace(e.FixHint), e.Message));
        // Hints are sentences that name what to change
        Assert.All(errors, e => Assert.EndsWith(".", e.FixHint));
    }

    [Fact]
    public void BrokenDraft_EveryWarningHasAFixHint()
    {
        var warnings = CaseGuideValidator.Validate(BrokenDraft()).Where(f => f.Severity == IssueSeverity.Warning).ToList();

        Assert.NotEmpty(warnings);
        Assert.All(warnings, w => Assert.False(string.IsNullOrWhiteSpace(w.FixHint), w.Message));
    }

    [Fact]
    public void Hints_NameTheEntityAndValue()
    {
        var draft = BrokenDraft();

        Assert.Equal("Widen Tom Becker's active dates to include 2024-03-18, or change F-003's date.",
            Finding(draft, "F-003 is dated 2024-03-18, but Tom Becker").FixHint);
        Assert.Equal("Remove the link to CH-099 from F-002, or pick a different character.",
            Finding(draft, "F-002 refers to a character").FixHint);
        Assert.Equal("Change F-006's date from June 20, 2024 to 2024-06-20.", Finding(draft, "F-006 has no valid date.").FixHint);
        Assert.Equal("Set Priya Nair's active dates to 2024-01-08 to 2024-12-31, or type them as YYYY-MM-DD.",
            Finding(draft, "Priya Nair (CH-008) has an active range").FixHint);
        Assert.Equal("Replace \"Acme Windows\" with \"Brightline Glazing\" in F-001.", Finding(draft, "F-001 uses the real name").FixHint);
        Assert.Equal("Click Resume Guide to write the canonical values.", Finding(draft, "The canonical values were not written.").FixHint);
        Assert.Equal("Link Ryan Howard to Ryan Howard (CH-011), the character with the same name.",
            Finding(draft, "Party Ryan Howard is not in the cast.").FixHint);
    }

    [Fact]
    public void CounselWarning_PointsToRegenerateCast()
    {
        var finding = Finding(Sample(), "Harmon Builders, Inc. has no in-house counsel.");

        Assert.Equal("Click Regenerate Cast to add in-house counsel for Harmon Builders, Inc., or add a character with the In-house counsel role.",
            finding.FixHint);
        Assert.Equal(GuideQuickFix.RegenerateCast, finding.QuickFix);
        Assert.Equal(GuideArea.Cast, finding.GoTo);
    }

    [Fact]
    public void ApplyAll_OnBrokenDraft_ClearsEveryAutomaticError()
    {
        var draft = BrokenDraft();
        var before = CaseGuideValidator.Validate(draft);
        var revision = draft.Revision;

        var applied = CaseGuideQuickFix.ApplyAll(draft, before);

        var after = CaseGuideValidator.Validate(draft);
        Assert.True(applied > 0);
        Assert.True(draft.Revision > revision);
        // No error that a fix can clear is left, and every automatic rule's error is gone
        Assert.DoesNotContain(after, f => f.Severity == IssueSeverity.Error && f.HasAutomaticFix);
        foreach (var rule in ErrorRules().Where(r => CaseGuideQuickFix.IsAutomatic((GuideQuickFix)r[1])))
            Assert.DoesNotContain(after, f => f.Severity == IssueSeverity.Error && f.Message.StartsWith((string)rule[0], StringComparison.Ordinal));
        // The errors that need the user or the model are still listed
        Assert.Contains(after, f => f.Message.StartsWith("F-003 is not linked to any issue.", StringComparison.Ordinal));
        Assert.Contains(after, f => f.Message.StartsWith("F-008 is dated 2024-10-01, after the filing date", StringComparison.Ordinal));
    }

    [Fact]
    public void ApplyAll_IsIdempotent()
    {
        var draft = BrokenDraft();
        CaseGuideQuickFix.ApplyAll(draft, CaseGuideValidator.Validate(draft));
        var json = System.Text.Json.JsonSerializer.Serialize(draft.Guide);

        var second = CaseGuideQuickFix.ApplyAll(draft, CaseGuideValidator.Validate(draft));

        Assert.Equal(0, second);
        Assert.Equal(json, System.Text.Json.JsonSerializer.Serialize(draft.Guide));
    }

    [Fact]
    public void Apply_ModelFixesAndNone_ChangeNothing()
    {
        var draft = BrokenDraft();
        var revision = draft.Revision;

        foreach (var finding in CaseGuideValidator.Validate(draft).Where(f => !f.HasAutomaticFix))
            Assert.False(CaseGuideQuickFix.Apply(draft, finding), finding.Message);

        Assert.Equal(revision, draft.Revision);
    }

    // ----- Each automatic fix -----

    [Fact]
    public void AssignNewId_GivesBlankAndDuplicateIdsNewOnes_FirstKeepsItsId()
    {
        var draft = Sample();
        var guide = draft.Guide!;
        var original = guide.FindValue("V-004")!;
        var twin = new CanonicalValue { Id = "V-004", Label = "Retainage", Value = "5 percent" };
        var blank = new CanonicalValue { Id = " ", Label = "Site", Value = "100 Lakeview Drive" };
        guide.Values.Add(twin);
        guide.Values.Add(blank);

        Assert.True(CaseGuideQuickFix.Apply(draft, Finding(draft, "Two entries use the ID V-004.")));
        Assert.True(CaseGuideQuickFix.Apply(draft, Finding(draft, "An entry has no ID.")));

        Assert.Equal("V-004", original.Id);
        Assert.Equal("V-005", twin.Id);
        Assert.Equal("V-006", blank.Id);
        Assert.False(Has(draft, "Two entries use the ID"));
        Assert.False(Has(draft, "An entry has no ID."));
    }

    [Fact]
    public void RemoveBrokenLinks_DropsOnlyMissingIds_FromFactsValuesAndFirms()
    {
        var draft = Sample();
        var guide = draft.Guide!;
        var fact = guide.FindFact("F-002")!;
        fact.ActorIds.Add("CH-099");
        fact.KnownByIds.Add("CH-098");
        fact.IssueIds.Add("ISS-99");
        guide.FindValue("V-001")!.FactIds.Add("F-099");
        guide.FindOrganization("ORG-03")!.RepresentsOrgIds.Add("ORG-99");

        Assert.True(CaseGuideQuickFix.Apply(draft, Finding(draft, "F-002 refers to a character")));
        Assert.True(CaseGuideQuickFix.Apply(draft, Finding(draft, "V-001 links to fact F-099")));
        Assert.True(CaseGuideQuickFix.Apply(draft, Finding(draft, "Ellis & Park LLP represents an organization that does not exist (ORG-99).")));

        Assert.Equal(new[] { "CH-004", "CH-002" }, fact.ActorIds);
        Assert.Equal(new[] { "CH-004", "CH-002", "CH-001" }, fact.KnownByIds);
        Assert.DoesNotContain("ISS-99", fact.IssueIds);
        Assert.Equal(new[] { "F-003" }, guide.FindValue("V-001")!.FactIds);
        Assert.Equal(new[] { "ORG-01" }, guide.FindOrganization("ORG-03")!.RepresentsOrgIds);
        Assert.DoesNotContain(CaseGuideValidator.Validate(draft), f => f.Severity == IssueSeverity.Error);
    }

    [Fact]
    public void WidenActiveRange_WidensTheNamedCharacter_EvenWhenHandEdited()
    {
        var draft = Sample();
        var tom = draft.Guide!.FindCast("CH-002")!;
        tom.ActiveFrom = "2024-02-01";
        tom.ActiveTo = "2024-03-01";
        tom.IsUserEdited = true;
        var lisa = draft.Guide.FindCast("CH-004")!;
        var lisaRange = (lisa.ActiveFrom, lisa.ActiveTo);

        var finding = Finding(draft, "F-003 is dated 2024-03-18, but Tom Becker");
        Assert.Equal("CH-002", finding.FixTargetId);
        Assert.Equal("2024-03-18", finding.FixValue);
        Assert.True(CaseGuideQuickFix.Apply(draft, finding));

        Assert.Equal("2024-02-01", tom.ActiveFrom);
        Assert.Equal("2024-03-18", tom.ActiveTo);
        Assert.Equal(lisaRange, (lisa.ActiveFrom, lisa.ActiveTo));
        Assert.False(Has(draft, "F-003 is dated 2024-03-18, but Tom Becker"));
        Assert.False(CaseGuideQuickFix.Apply(draft, finding));
    }

    [Fact]
    public void WidenActiveRange_EarlierDate_MovesTheStart()
    {
        var draft = Sample();
        var maria = draft.Guide!.FindCast("CH-001")!;
        maria.ActiveFrom = "2024-03-01";

        Assert.True(CaseGuideQuickFix.Apply(draft, Finding(draft, "F-001 is dated 2024-01-15, but Maria Delgado")));

        Assert.Equal("2024-01-15", maria.ActiveFrom);
        Assert.Equal("2024-12-31", maria.ActiveTo);
    }

    [Theory]
    [InlineData("", "2024-12-31", "2024-01-08", "2024-12-31")]           // missing start: the timeline start
    [InlineData("2023-10-01", "", "2023-10-01", "2024-09-16")]           // missing end: the filing date
    [InlineData("2024-12-31", "2023-10-01", "2023-10-01", "2024-12-31")] // reversed: swapped
    [InlineData("2023/10/1", "nope", "2023-10-01", "2024-09-16")]        // unambiguous format normalized
    public void WidenActiveRange_RepairsAMissingOrReversedRange(string from, string to, string expectedFrom, string expectedTo)
    {
        var draft = Sample();
        var priya = draft.Guide!.FindCast("CH-008")!;
        priya.ActiveFrom = from;
        priya.ActiveTo = to;

        Assert.True(CaseGuideQuickFix.Apply(draft, Finding(draft, "Priya Nair (CH-008) has an active range")));

        Assert.Equal(expectedFrom, priya.ActiveFrom);
        Assert.Equal(expectedTo, priya.ActiveTo);
        Assert.False(Has(draft, "Priya Nair (CH-008) has an active range"));
    }

    [Fact]
    public void WidenActiveRange_RepairCoversTheCharactersFacts()
    {
        var draft = Sample();
        var tom = draft.Guide!.FindCast("CH-002")!;
        tom.ActiveFrom = "";
        tom.ActiveTo = "";

        Assert.True(CaseGuideQuickFix.Apply(draft, Finding(draft, "Tom Becker (CH-002) has an active range")));

        // F-002 (2024-02-12) through F-004 (2024-04-22)
        Assert.Equal("2024-02-12", tom.ActiveFrom);
        Assert.Equal("2024-04-22", tom.ActiveTo);
    }

    [Fact]
    public void WidenActiveRange_KeepsDownstreamPartsFresh()
    {
        var draft = Sample();
        // Broken before the parts are stamped, so every part starts fresh
        draft.Guide!.FindCast("CH-002")!.ActiveTo = "2024-03-01";
        foreach (var part in GuideStaleness.Order)
            GuideStaleness.Stamp(draft, part);
        Assert.False(GuideStaleness.IsStale(draft, GuidePart.Facts));

        Assert.True(CaseGuideQuickFix.Apply(draft, Finding(draft, "F-003 is dated 2024-03-18, but Tom Becker")));

        Assert.False(GuideStaleness.IsStale(draft, GuidePart.Facts));
        Assert.False(GuideStaleness.IsStale(draft, GuidePart.Values));
    }

    [Theory]
    [InlineData("2024-3-4", "2024-03-04")]
    [InlineData("2024/03/04", "2024-03-04")]
    [InlineData("2024.03.04", "2024-03-04")]
    [InlineData("20240304", "2024-03-04")]
    [InlineData("2024-03-04T09:30:00", "2024-03-04")]
    [InlineData("2024-03-04T23:30:00Z", "2024-03-04")]
    [InlineData("March 4, 2024", "2024-03-04")]
    [InlineData("Mar 4, 2024", "2024-03-04")]
    [InlineData("4 March 2024", "2024-03-04")]
    [InlineData(" 3/15/2024 ", "2024-03-15")]
    [InlineData("15/3/2024", "2024-03-15")]
    [InlineData("4/4/2024", "2024-04-04")]
    public void TryNormalizeDate_ReadsUnambiguousDates(string text, string expected)
    {
        Assert.True(CaseGuideQuickFix.TryNormalizeDate(text, out var iso));
        Assert.Equal(expected, iso);
    }

    [Theory]
    [InlineData("3/4/2024")]   // March 4 or April 3
    [InlineData("13/13/2024")]
    [InlineData("2/30/2024")]
    [InlineData("spring 2024")]
    [InlineData("")]
    [InlineData(null)]
    public void TryNormalizeDate_RejectsAmbiguousOrInvalidDates(string? text)
    {
        Assert.False(CaseGuideQuickFix.TryNormalizeDate(text, out var iso));
        Assert.Equal(string.Empty, iso);
    }

    [Fact]
    public void NormalizeDate_RewritesTheFactDate()
    {
        var draft = Sample();
        draft.Guide!.FindFact("F-004")!.Date = "April 22, 2024";

        var finding = Finding(draft, "F-004 has no valid date.");
        Assert.Equal(GuideQuickFix.NormalizeDate, finding.QuickFix);
        Assert.True(CaseGuideQuickFix.Apply(draft, finding));

        Assert.Equal("2024-04-22", draft.Guide.FindFact("F-004")!.Date);
        Assert.False(Has(draft, "F-004 has no valid date."));
    }

    [Fact]
    public void ResetEmail_RebuildsInvalidAndSharedAddresses()
    {
        var draft = Sample();
        var guide = draft.Guide!;
        var evan = guide.FindCast("CH-007")!;
        evan.Email = "evan at ellis";
        evan.EmailIsUserEdited = true;
        var tom = guide.FindCast("CH-002")!;
        tom.Email = guide.FindCast("CH-001")!.Email;

        Assert.True(CaseGuideQuickFix.Apply(draft, Finding(draft, "Evan Price (CH-007) has an invalid email address.")));
        Assert.True(CaseGuideQuickFix.Apply(draft, CaseGuideValidator.Validate(draft)
            .First(f => f.EntityId == "CH-002" && f.Message.EndsWith("is used by more than one character.", StringComparison.Ordinal))));

        Assert.Equal("evan.price@ellispark.com", evan.Email);
        Assert.False(evan.EmailIsUserEdited);
        Assert.Equal("tom.becker@brightline.com", tom.Email);
        Assert.Contains("tom.becker@brightline.com", tom.Signature);
        Assert.DoesNotContain(CaseGuideValidator.Validate(draft), f => f.Severity == IssueSeverity.Error);
    }

    [Fact]
    public void ResetEmail_WithoutAnOrganizationDomain_IsNotOffered()
    {
        var draft = Sample();
        draft.Guide!.FindOrganization("ORG-03")!.Domain = "";
        draft.Guide.FindCast("CH-007")!.Email = "bad";

        var finding = Finding(draft, "Evan Price (CH-007) has an invalid email address.");

        Assert.Equal(GuideQuickFix.None, finding.QuickFix);
        Assert.Equal("Type a valid address for Evan Price, like name@company.com.", finding.FixHint);
    }

    [Fact]
    public void NewEmailDomain_MovesTheSecondOrganizationAndItsCodeBuiltAddresses()
    {
        var draft = Sample();
        var guide = draft.Guide!;
        guide.FindOrganization("ORG-03")!.Domain = "harmon.com";
        var typed = guide.FindCast("CH-007")!;
        typed.Email = "evan@harmon.com";
        typed.EmailIsUserEdited = true;

        var finding = Finding(draft, "Harmon Builders, Inc. and Ellis & Park LLP use the same email domain (harmon.com).");
        Assert.Equal("ORG-03", finding.EntityId);
        Assert.True(CaseGuideQuickFix.Apply(draft, finding));

        var firm = guide.FindOrganization("ORG-03")!;
        Assert.NotEqual("harmon.com", firm.Domain);
        Assert.Equal(finding.FixValue, firm.Domain);
        Assert.Equal("harmon.com", guide.FindOrganization("ORG-02")!.Domain);
        Assert.EndsWith("@" + firm.Domain, guide.FindCast("CH-006")!.Email);
        // An address the user typed is theirs
        Assert.Equal("evan@harmon.com", typed.Email);
        Assert.False(Has(draft, "Harmon Builders, Inc. and Ellis & Park LLP use the same email domain"));
        Assert.False(CaseGuideQuickFix.Apply(draft, finding));
    }

    [Fact]
    public void AddActorsToKnownBy_AddsWhoDidIt()
    {
        var draft = Sample();
        var fact = draft.Guide!.FindFact("F-004")!;
        fact.KnownByIds.Remove("CH-002");

        Assert.True(CaseGuideQuickFix.Apply(draft, Finding(draft, "F-004: Tom Becker did it but is not listed under Who knew.")));

        Assert.Contains("CH-002", fact.KnownByIds);
        Assert.False(Has(draft, "F-004: Tom Becker did it"));
    }

    [Fact]
    public void ReplaceRealNames_UsesTheSubstitutes_InFactsValuesCastAndOrganizations()
    {
        var draft = Sample();
        var guide = draft.Guide!;
        guide.FindFact("F-002")!.Title = "Acme Windows confirms the procedure";
        guide.FindValue("V-003")!.Description = "Project run by Acme Windows.";
        guide.FindCast("CH-009")!.Involvement = "Foreman at Acme Windows.";
        guide.FindOrganization("ORG-01")!.ShortName = "Acme Windows";

        foreach (var finding in CaseGuideValidator.Validate(draft).Where(f => f.QuickFix == GuideQuickFix.ReplaceRealNames).ToList())
            Assert.True(CaseGuideQuickFix.Apply(draft, finding), finding.Message);

        Assert.Equal("Brightline Glazing confirms the procedure", guide.FindFact("F-002")!.Title);
        Assert.Equal("Project run by Brightline Glazing.", guide.FindValue("V-003")!.Description);
        Assert.Equal("Foreman at Brightline Glazing.", guide.FindCast("CH-009")!.Involvement);
        Assert.Equal("Brightline Glazing", guide.FindOrganization("ORG-01")!.ShortName);
        Assert.DoesNotContain(CaseGuideValidator.Validate(draft), f => f.Message.Contains("uses the real name"));
    }

    [Fact]
    public void LinkParty_LinksTheSameNamedCharacter()
    {
        var draft = Sample();
        draft.CoreDocument!.Parties.Add(new CaseParty { Id = "party-3", Name = "Ryan Howard", Role = PartyRole.Defendant, EntityType = "individual" });
        var ryan = Member(draft, "CH-010", "Ryan", "Howard", "ORG-02");
        ryan.PartyId = "party-old";

        var finding = Finding(draft, "Party Ryan Howard is not in the cast.");
        Assert.Equal(GuideQuickFix.LinkParty, finding.QuickFix);
        Assert.Equal("party-3", finding.FixTargetId);
        Assert.True(CaseGuideQuickFix.Apply(draft, finding));

        Assert.Equal("party-3", ryan.PartyId);
        Assert.False(Has(draft, "Party Ryan Howard is not in the cast."));
    }

    [Fact]
    public void LinkParty_NeverTakesAnotherIndividualPartysCharacter()
    {
        var draft = Sample();
        draft.CoreDocument!.Parties.Add(new CaseParty { Id = "party-3", Name = "Ryan Howard", Role = PartyRole.Defendant, EntityType = "individual" });
        draft.CoreDocument.Parties.Add(new CaseParty { Id = "party-4", Name = "Ryan Howard", Role = PartyRole.Defendant, EntityType = "individual" });
        Member(draft, "CH-010", "Ryan", "Howard", "ORG-02").PartyId = "party-3";

        var finding = CaseGuideValidator.Validate(draft).Single(f => f.Message == "Party Ryan Howard is not in the cast.");

        Assert.Equal(GuideQuickFix.RegenerateCast, finding.QuickFix);
        Assert.Equal("Click Regenerate Cast to add Ryan Howard as a character, or add a character named Ryan Howard.", finding.FixHint);
    }

    [Fact]
    public void LinkParty_LinksTheSameNamedOrganization()
    {
        var draft = Sample();
        draft.CoreDocument!.Parties.Add(new CaseParty { Id = "party-3", Name = "Lakeview Tower Owner LLC", Role = PartyRole.Defendant, EntityType = "LLC" });
        var org = new CaseOrganization { Id = "ORG-04", Name = "Lakeview Tower Owner, LLC", Kind = OrganizationKind.ThirdParty, Domain = "lakeview.com" };
        draft.Guide!.Organizations.Add(org);

        var finding = Finding(draft, "Party Lakeview Tower Owner LLC has no organization.");
        Assert.Equal("Link Lakeview Tower Owner LLC to the organization Lakeview Tower Owner, LLC (ORG-04).", finding.FixHint);
        Assert.True(CaseGuideQuickFix.Apply(draft, finding));

        Assert.Equal("party-3", org.PartyId);
        Assert.False(Has(draft, "Party Lakeview Tower Owner LLC has no organization."));
    }

    [Fact]
    public void MarkReviewed_ClearsFactAndValueFlags()
    {
        var draft = Sample();
        var fact = draft.Guide!.FindFact("F-003")!;
        fact.NeedsReview = true;
        fact.ReviewReason = "Tom Becker was changed after it was written.";
        var value = draft.Guide.FindValue("V-001")!;
        value.NeedsReview = true;
        value.ReviewReason = "F-003 was changed after it was written.";

        Assert.True(CaseGuideQuickFix.Apply(draft, Finding(draft, "F-003 needs review")));
        Assert.True(CaseGuideQuickFix.Apply(draft, Finding(draft, "V-001 is linked to F-003, which you changed.")));

        Assert.False(fact.NeedsReview);
        Assert.False(value.NeedsReview);
        Assert.Equal(CaseTestData.SampleGuidedDraft().Guide!.FindFact("F-003")!.Description, fact.Description);
    }

    [Fact]
    public void CountAutomatic_GroupsByFixInEnumOrder()
    {
        var counts = CaseGuideQuickFix.CountAutomatic(CaseGuideValidator.Validate(BrokenDraft()));

        Assert.Equal(counts.OrderBy(c => (int)c.Fix).Select(c => c.Fix), counts.Select(c => c.Fix));
        Assert.All(counts, c => Assert.True(CaseGuideQuickFix.IsAutomatic(c.Fix)));
        Assert.Contains(counts, c => c.Fix == GuideQuickFix.ResetEmail && c.Count >= 3);
    }

    [Fact]
    public void Apply_NullsAndUnknownEntities_ReturnFalse()
    {
        var draft = Sample();

        Assert.False(CaseGuideQuickFix.Apply(null!, new GuideFinding { QuickFix = GuideQuickFix.MarkReviewed }));
        Assert.False(CaseGuideQuickFix.Apply(draft, null!));
        Assert.False(CaseGuideQuickFix.Apply(draft, new GuideFinding { Area = GuideArea.Facts, EntityId = "F-404", QuickFix = GuideQuickFix.RemoveBrokenLinks }));
        Assert.False(CaseGuideQuickFix.Apply(draft, new GuideFinding { QuickFix = GuideQuickFix.WidenActiveRange, FixTargetId = "CH-404", FixValue = "2024-01-01" }));
        Assert.Equal(0, CaseGuideQuickFix.ApplyAll(draft, null!));
    }
}
