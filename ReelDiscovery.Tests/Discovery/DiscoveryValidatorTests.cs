using ReelDiscovery.Models.Case;
using ReelDiscovery.Models.Discovery;
using ReelDiscovery.Services.Discovery;

namespace ReelDiscovery.Tests.Discovery;

public class DiscoveryValidatorTests
{
    // ---- D1: structure ----

    [Fact]
    public void D1_ShortChunk_Flagged()
    {
        var ctx = DepoContext();
        ctx.Part = 2;
        ctx.TargetTurns = 100;

        var violations = DiscoveryValidator.CheckDepositionChunk(Turns(30), Array.Empty<string>(), ctx);

        var v = Assert.Single(violations, x => x.Rule == DiscoveryRule.D1ShortPart);
        Assert.Equal(2, v.Number);
        Assert.Equal(100, v.Expected);
        Assert.Equal(30, v.Actual);
    }

    [Fact]
    public void D1_HalfTheTarget_Clean()
    {
        var ctx = DepoContext();
        ctx.TargetTurns = 100;

        var violations = DiscoveryValidator.CheckDepositionChunk(Turns(60), Array.Empty<string>(), ctx);

        Assert.DoesNotContain(violations, x => x.Rule == DiscoveryRule.D1ShortPart);
    }

    [Fact]
    public void D1_ExpectedExhibitNotIntroduced_Flagged()
    {
        var ctx = DepoContext();
        ctx.ExpectedExhibits = new List<int> { 3, 4 };
        var turns = Turns(10);
        turns[2].ExhibitNumber = 3;

        var violations = DiscoveryValidator.CheckDepositionChunk(turns, Array.Empty<string>(), ctx);

        var v = Assert.Single(violations, x => x.Rule == DiscoveryRule.D1ExhibitNotShown);
        Assert.Equal(4, v.Number);
    }

    // ---- D2: unknown people ----

    [Fact]
    public void D2_CastAndExtraNames_Clean()
    {
        var ctx = DepoContext();
        ctx.ExtraNames.Add("Janet Pruitt");
        var turns = TextTurns("Michael Scott and Nora Quist were present.", "Janet Pruitt, the reporter, swore the witness.",
            "Monday Morning we met. Thank You for asking.");

        var violations = DiscoveryValidator.CheckDepositionChunk(turns, new[] { "Michael Scott", "Nora Quist", "Janet Pruitt" }, ctx);

        Assert.DoesNotContain(violations, x => x.Rule == DiscoveryRule.D2UnknownPerson);
    }

    [Fact]
    public void D2_UnknownPeopleNamed_Flagged()
    {
        var violations = DiscoveryValidator.CheckDepositionChunk(TextTurns("Yes."), new[] { "Bob Vance" }, DepoContext());

        var v = Assert.Single(violations, x => x.Rule == DiscoveryRule.D2UnknownPerson);
        Assert.Equal("Bob Vance", v.Value);
    }

    [Fact]
    public void D2_KnownFirstNameWithUnknownSurname_Flagged()
    {
        var violations = DiscoveryValidator.CheckDepositionChunk(TextTurns("Michael Jordan said we should wait."), Array.Empty<string>(), DepoContext());

        Assert.Contains(violations, x => x.Rule == DiscoveryRule.D2UnknownPerson && x.Value == "Michael Jordan");
    }

    [Fact]
    public void D2_HonorificWithUnknownSurname_Flagged()
    {
        var violations = DiscoveryValidator.CheckDepositionChunk(TextTurns("I spoke with Mr. Halbrook about it."), Array.Empty<string>(), DepoContext());

        Assert.Contains(violations, x => x.Rule == DiscoveryRule.D2UnknownPerson && x.Value == "Mr. Halbrook");
    }

    [Fact]
    public void D2_InRequests_Flagged()
    {
        var ctx = RequestContext(DiscoveryKind.RequestsForProduction, 1);

        var violations = DiscoveryValidator.CheckRequests(new[] { (1, "All emails from Michael Jordan to Harmon.") }, ctx);

        var v = Assert.Single(violations, x => x.Rule == DiscoveryRule.D2UnknownPerson);
        Assert.Equal(1, v.Number);
    }

    // ---- D3: canonical values ----

    [Fact]
    public void D3_AmountVariant_Flagged()
    {
        var violations = DiscoveryValidator.CheckDepositionChunk(TextTurns("The balance was $480,000 at the time."), Array.Empty<string>(), DepoContext());

        var v = Assert.Single(violations, x => x.Rule == DiscoveryRule.D3AmountVariant);
        Assert.Equal("$480,000", v.Value);
        Assert.Equal("$487,250.00", v.Canonical);
    }

    [Fact]
    public void D3_ExactAndFarAmounts_Clean()
    {
        var violations = DiscoveryValidator.CheckDepositionChunk(
            TextTurns("The balance was $487,250 and the project cost $2,000,000."), Array.Empty<string>(), DepoContext());

        Assert.DoesNotContain(violations, x => x.Rule == DiscoveryRule.D3AmountVariant);
    }

    [Fact]
    public void D3_UnregisteredIdentifier_Flagged()
    {
        var violations = DiscoveryValidator.CheckDepositionChunk(TextTurns("Change order CO-009 was never approved."), Array.Empty<string>(), DepoContext());

        var v = Assert.Single(violations, x => x.Rule == DiscoveryRule.D3UnknownIdentifier);
        Assert.Equal("CO-009", v.Value);
    }

    [Fact]
    public void D3_AllowedIdentifierAndRegistered_Clean()
    {
        var ctx = DepoContext();
        ctx.AllowedIdentifiers.Add("CO-0123");

        var violations = DiscoveryValidator.CheckDepositionChunk(TextTurns("See CO-0123 and CO-007."), Array.Empty<string>(), ctx);

        Assert.DoesNotContain(violations, x => x.Rule == DiscoveryRule.D3UnknownIdentifier);
    }

    // ---- D4: slots ----

    [Fact]
    public void D4_MissingAndExtraSlots_Flagged()
    {
        var ctx = RequestContext(DiscoveryKind.RequestsForProduction, 5);
        var requests = new[] { 1, 2, 2, 4, 7 }.Select(n => (n, "All documents concerning the Lakeview Tower project.")).ToList();

        var violations = DiscoveryValidator.CheckRequests(requests, ctx);

        Assert.Equal(new[] { 3, 5 }, violations.Where(v => v.Rule == DiscoveryRule.D4MissingRequest).Select(v => v.Number).OrderBy(n => n));
        Assert.Equal(new[] { 2, 7 }, violations.Where(v => v.Rule == DiscoveryRule.D4ExtraRequest).Select(v => v.Number).OrderBy(n => n));
    }

    // ---- D5: RFA fact detail ----

    [Fact]
    public void D5_MissingAmount_Flagged()
    {
        var ctx = RfaContext();

        var violations = DiscoveryValidator.CheckRequests(new[] { (3, "Admit that Michael Scott approved a back-charge.") }, ctx);

        var v = Assert.Single(violations, x => x.Rule == DiscoveryRule.D5MissingFactDetail);
        Assert.Equal(3, v.Number);
        Assert.Equal("$487,250.00", v.Value);
    }

    [Fact]
    public void D5_MissingActor_Flagged()
    {
        var ctx = RfaContext();

        var violations = DiscoveryValidator.CheckRequests(new[] { (3, "Admit that Harmon withheld $487,250.00.") }, ctx);

        var v = Assert.Single(violations, x => x.Rule == DiscoveryRule.D5MissingFactDetail);
        Assert.Equal("Michael Scott", v.Value);
    }

    [Theory]
    [InlineData("Admit that Mr. Scott approved a back-charge of $487,250.00.")]
    [InlineData("Admit that Scott approved a back-charge of $487,250.")]
    public void D5_ActorSurnameAndAmount_Clean(string text)
    {
        var violations = DiscoveryValidator.CheckRequests(new[] { (3, text) }, RfaContext());

        Assert.DoesNotContain(violations, x => x.Rule == DiscoveryRule.D5MissingFactDetail);
    }

    [Fact]
    public void D5_GenuinenessSlot_NotChecked()
    {
        var ctx = RfaContext();
        ctx.GenuinenessSlots.Add(3);

        var violations = DiscoveryValidator.CheckRequests(new[] { (3, "Admit that Exhibit A is a true and correct copy.") }, ctx);

        Assert.DoesNotContain(violations, x => x.Rule == DiscoveryRule.D5MissingFactDetail);
    }

    [Fact]
    public void D5_OnlyForAdmissions()
    {
        var ctx = RfaContext();
        ctx.Kind = DiscoveryKind.RequestsForProduction;

        var violations = DiscoveryValidator.CheckRequests(new[] { (3, "All documents concerning the back-charge.") }, ctx);

        Assert.DoesNotContain(violations, x => x.Rule == DiscoveryRule.D5MissingFactDetail);
    }

    // ---- D6: after the deposition date ----

    [Theory]
    [InlineData("We met on July 2, 2025 to discuss it.", "July 2, 2025")]
    [InlineData("The record dated 2025-07-02 shows it.", "2025-07-02")]
    public void D6_DateAfterDeposition_Flagged(string text, string expected)
    {
        var violations = DiscoveryValidator.CheckDepositionChunk(TextTurns(text), Array.Empty<string>(), DepoContext());

        var v = Assert.Single(violations, x => x.Rule == DiscoveryRule.D6AfterDepositionDate);
        Assert.Equal(expected, v.Value);
    }

    [Fact]
    public void D6_YearlessAndEarlierDates_Clean()
    {
        var violations = DiscoveryValidator.CheckDepositionChunk(
            TextTurns("We met on July 2 and again on May 1, 2025."), Array.Empty<string>(), DepoContext());

        Assert.DoesNotContain(violations, x => x.Rule == DiscoveryRule.D6AfterDepositionDate);
    }

    // ---- D7: interrogatory subparts ----

    [Fact]
    public void D7_LetteredParts_Flagged()
    {
        var ctx = RequestContext(DiscoveryKind.Interrogatories, 1);

        var violations = DiscoveryValidator.CheckRequests(new[] { (1, "Identify (a) each person and (b) each document.") }, ctx);

        Assert.Single(violations, x => x.Rule == DiscoveryRule.D7Subparts && x.Number == 1);
    }

    [Fact]
    public void D7_NumberedLines_Flagged()
    {
        var ctx = RequestContext(DiscoveryKind.Interrogatories, 1);

        var violations = DiscoveryValidator.CheckRequests(new[] { (1, "Identify:\n1. each person\n2. each document") }, ctx);

        Assert.Contains(violations, x => x.Rule == DiscoveryRule.D7Subparts);
    }

    [Theory]
    [InlineData("Identify each person with knowledge of the Change Order 7 dispute.")]
    [InlineData("Identify the person(s) and document(s) that support your defense.")]
    public void D7_SingleQuestion_Clean(string text)
    {
        var ctx = RequestContext(DiscoveryKind.Interrogatories, 1);

        var violations = DiscoveryValidator.CheckRequests(new[] { (1, text) }, ctx);

        Assert.DoesNotContain(violations, x => x.Rule == DiscoveryRule.D7Subparts);
    }

    // ---- D8: real entity names ----

    [Fact]
    public void D8_AppliedSubstitution_Flagged()
    {
        var violations = DiscoveryValidator.CheckDepositionChunk(TextTurns("Acme Corporation shipped the frames."), Array.Empty<string>(), DepoContext());

        var v = Assert.Single(violations, x => x.Rule == DiscoveryRule.D8RealEntityName);
        Assert.Equal("Acme Corporation", v.Value);
        Assert.Equal("Apex Fabrication Co.", v.Canonical);
    }

    [Fact]
    public void D8_KeptRealName_Clean()
    {
        var ctx = DepoContext();
        ctx.Draft.Substitutions[0].Apply = false;

        var violations = DiscoveryValidator.CheckDepositionChunk(TextTurns("Acme Corporation shipped the frames."), Array.Empty<string>(), ctx);

        Assert.DoesNotContain(violations, x => x.Rule == DiscoveryRule.D8RealEntityName);
    }

    // ---- Keep-best and feedback ----

    [Fact]
    public void IsBetter_FollowsKeepBestRule()
    {
        var structural = new List<DiscoveryViolation> { V(DiscoveryRule.D1ShortPart) };
        var three = new List<DiscoveryViolation> { V(DiscoveryRule.D2UnknownPerson), V(DiscoveryRule.D3AmountVariant), V(DiscoveryRule.D7Subparts) };
        var twoA = new List<DiscoveryViolation> { V(DiscoveryRule.D2UnknownPerson), V(DiscoveryRule.D3AmountVariant) };
        var twoB = new List<DiscoveryViolation> { V(DiscoveryRule.D6AfterDepositionDate), V(DiscoveryRule.D8RealEntityName) };

        Assert.True(DiscoveryValidator.IsBetter(three, null));
        Assert.True(DiscoveryValidator.IsBetter(three, structural));
        Assert.False(DiscoveryValidator.IsBetter(structural, twoA));
        Assert.True(DiscoveryValidator.IsBetter(twoB, twoA));
        Assert.False(DiscoveryValidator.IsBetter(three, twoA));
    }

    [Fact]
    public void FeedbackText_HeaderAndOneLinePerViolation()
    {
        var violations = new List<DiscoveryViolation>
        {
            new() { Rule = DiscoveryRule.D1ShortPart, Number = 2, Expected = 100, Actual = 30 },
            new() { Rule = DiscoveryRule.D2UnknownPerson, Value = "Bob Vance" },
            new() { Rule = DiscoveryRule.D3AmountVariant, Value = "$480,000", Canonical = "$487,250.00" },
            new() { Rule = DiscoveryRule.D8RealEntityName, Value = "Acme Corporation", Canonical = "Apex Fabrication Co." }
        };

        var text = DiscoveryValidator.FeedbackText(violations);
        var lines = text.Split('\n');

        Assert.StartsWith("Problems in the previous attempt (fix all of them and rewrite the whole part):", text);
        Assert.Equal(4, lines.Count(l => l.StartsWith("- ", StringComparison.Ordinal)));
        Assert.Contains("- Part 2 has 30 turns; write about 100.", lines);
        Assert.Contains("- Use \"Apex Fabrication Co.\" instead of \"Acme Corporation\".", lines);
    }

    [Fact]
    public void NullAndEmptyInput_NeverThrows()
    {
        var ctx = new DiscoveryCheckContext();

        Assert.Empty(DiscoveryValidator.CheckDepositionChunk(null!, null!, ctx));
        Assert.Empty(DiscoveryValidator.CheckRequests(null!, ctx));
        Assert.Empty(DiscoveryValidator.CheckRequests(new[] { (1, (string)null!) }, new DiscoveryCheckContext { ExpectedSlots = new HashSet<int> { 1 } }));
    }

    // ---- helpers ----

    private static DiscoveryCheckContext DepoContext() => new()
    {
        Draft = DiscoveryTestData.LitigationDraft(),
        Kind = DiscoveryKind.Deposition,
        Part = 1,
        TargetTurns = 0,
        DepositionDate = new DateOnly(2025, 6, 10)
    };

    private static DiscoveryCheckContext RequestContext(DiscoveryKind kind, int slots) => new()
    {
        Draft = DiscoveryTestData.LitigationDraft(),
        Kind = kind,
        ExpectedSlots = Enumerable.Range(1, slots).ToHashSet()
    };

    // Slot 3 keyed to F-010 (actor Michael Scott), with the $487,250.00 value linked to it
    private static DiscoveryCheckContext RfaContext()
    {
        var ctx = RequestContext(DiscoveryKind.RequestsForAdmission, 0);
        ctx.ExpectedSlots = new HashSet<int> { 3 };
        ctx.SlotFactIds[3] = "F-010";
        ctx.Draft.Guide!.Values.Single(v => v.Value == "$487,250.00").FactIds.Add("F-010");
        return ctx;
    }

    private static List<DepositionTurn> Turns(int count) =>
        Enumerable.Range(0, count)
            .Select(i => new DepositionTurn { Kind = i % 2 == 0 ? TurnKind.Q : TurnKind.A, Text = i % 2 == 0 ? "Did you see it?" : "Yes." })
            .ToList();

    private static List<DepositionTurn> TextTurns(params string[] texts) =>
        texts.Select(t => new DepositionTurn { Kind = TurnKind.A, Text = t }).ToList();

    private static DiscoveryViolation V(DiscoveryRule rule) => new() { Rule = rule };
}
