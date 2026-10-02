using ReelDiscovery.Models.Case;
using ReelDiscovery.Services.Ai;
using ReelDiscovery.Services.Case;
using ReelDiscovery.Tests.Fakes;
using static ReelDiscovery.Tests.Case.CaseGuideRegenerationTests;

namespace ReelDiscovery.Tests.Case;

/// <summary>
/// UAT gap fix (2026-10-02): generated characters' active ranges cover every fact they did or knew about; hand-edited
/// characters are never changed.
/// </summary>
public class ActiveRangeWideningTests
{
    private static (LlmGateway Gateway, FakeLlmProvider Fake) Gateway(params string[] responses)
    {
        var (gateway, fake) = CaseTestData.CreateGateway();
        foreach (var response in responses)
            fake.Responses.Enqueue(FakeLlmProvider.Ok(response));
        return (gateway, fake);
    }

    [Fact]
    public void WidenActiveRanges_WidensGeneratedLeavesEditedIgnoresBadDates()
    {
        var draft = CaseTestData.SampleGuidedDraft();
        var guide = draft.Guide!;

        // CH-001 did F-001 (2024-01-15) and F-005 (2024-05-10), knew F-002 (2024-02-12) and F-006 (2024-06-20)
        var maria = guide.FindCast("CH-001")!;
        maria.ActiveFrom = "2024-03-01";
        maria.ActiveTo = "2024-03-31";
        // CH-002 did F-002..F-004 (2024-02-12..2024-04-22); a fact with a bad date is ignored
        var tom = guide.FindCast("CH-002")!;
        tom.ActiveFrom = "2024-03-01";
        tom.ActiveTo = "2024-03-31";
        guide.Facts.Add(new KeyFact { Id = "F-007", Date = "2024-13-45", Title = "Undated", IssueIds = new() { "ISS-01" }, ActorIds = new() { "CH-002" }, KnownByIds = new() { "CH-002" } });
        // CH-003 was hand-edited: never changed
        var raymond = guide.FindCast("CH-003")!;
        raymond.ActiveFrom = "2024-03-01";
        raymond.ActiveTo = "2024-03-31";
        raymond.IsUserEdited = true;
        // CH-004 already covers its facts: unchanged
        var lisa = guide.FindCast("CH-004")!;
        lisa.ActiveFrom = "2023-10-01";
        lisa.ActiveTo = "2024-12-31";
        foreach (var part in GuideStaleness.Order)
            GuideStaleness.Stamp(draft, part);

        Assert.True(CaseGuideEditing.WidenActiveRanges(draft));

        Assert.Equal(("2024-01-15", "2024-06-20"), (maria.ActiveFrom, maria.ActiveTo));
        Assert.Equal(("2024-02-12", "2024-04-22"), (tom.ActiveFrom, tom.ActiveTo));
        Assert.Equal(("2024-03-01", "2024-03-31"), (raymond.ActiveFrom, raymond.ActiveTo));
        Assert.Equal(("2023-10-01", "2024-12-31"), (lisa.ActiveFrom, lisa.ActiveTo));
        Assert.False(maria.IsUserEdited);

        // Applied like a cast hand edit: fresh parts stay fresh; a second pass changes nothing
        Assert.Empty(GuideStaleness.StaleParts(draft));
        Assert.False(CaseGuideEditing.WidenActiveRanges(draft));

        // The validator still flags the hand-edited character only
        var findings = CaseGuideValidator.Validate(draft);
        Assert.DoesNotContain(findings, f => f.Message.Contains("(CH-001) is only active") || f.Message.Contains("(CH-002) is only active")
            || f.Message.Contains("(CH-001), who knew about it, is only active"));
        Assert.Contains(findings, f => f.Severity == IssueSeverity.Error && f.Message.Contains("Raymond Harmon (CH-003) is only active"));
    }

    [Fact]
    public void WidenActiveRanges_FillsMissingRange()
    {
        var draft = CaseTestData.SampleGuidedDraft();
        var tom = draft.Guide!.FindCast("CH-002")!;
        tom.ActiveFrom = string.Empty;
        tom.ActiveTo = "not a date";

        Assert.True(CaseGuideEditing.WidenActiveRanges(draft));

        Assert.Equal(("2024-02-12", "2024-04-22"), (tom.ActiveFrom, tom.ActiveTo));
    }

    [Fact]
    public async Task GenerateFacts_LeavesNoActiveRangeErrors()
    {
        // Characters written with ranges that miss the facts (the UAT: "F-001 is dated 2007-01-08, but David Wallace is only
        // active 2007-03-01 to 2008-07-31")
        var draft = CaseTestData.SampleGuidedDraft();
        var guide = draft.Guide!;
        guide.Facts.Clear();
        guide.Values.Clear();
        guide.FactsState.Status = GuidePartStatus.Pending;
        guide.ValuesState.Status = GuidePartStatus.Pending;
        foreach (var member in guide.Cast)
        {
            member.ActiveFrom = "2024-06-01";
            member.ActiveTo = "2024-06-30";
        }

        var facts = FactsJson(
            FactJson("2024-01-15", "Subcontract signed for Lakeview Tower", actors: "\"CH-001\", \"CH-003\"", knownBy: "\"CH-005\""),
            FactJson("2024-03-18", "Harmon approves Change Order 7", actors: "\"CH-004\"", knownBy: "\"CH-002\", \"CH-008\""),
            FactJson("2024-09-01", "Harmon refuses to pay", actors: "\"CH-003\"", knownBy: "\"CH-001\""));
        var (gateway, _) = Gateway(facts, facts);

        var result = await new CaseGuideGenerator(gateway).GenerateFactsAsync(draft);

        Assert.True(result.Succeeded, result.FailureMessage);
        Assert.NotEmpty(guide.Facts);
        Assert.DoesNotContain(CaseGuideValidator.Validate(draft), f => f.Message.Contains("is only active"));
        Assert.Equal(("2024-01-15", "2024-09-01"), (guide.FindCast("CH-001")!.ActiveFrom, guide.FindCast("CH-001")!.ActiveTo));
        Assert.Equal(("2024-03-18", "2024-06-30"), (guide.FindCast("CH-008")!.ActiveFrom, guide.FindCast("CH-008")!.ActiveTo));
        // Widened before the Facts stamp: the facts are fresh
        Assert.False(GuideStaleness.IsStale(draft, GuidePart.Facts));
    }

    [Fact]
    public async Task RegenerateFactEntry_WidensActors()
    {
        var draft = CaseTestData.SampleGuidedDraft();
        var raymond = draft.Guide!.FindCast("CH-003")!;
        raymond.ActiveTo = "2024-06-30";
        var entry = """{ "date": "2024-08-05", "title": "Harmon refuses to pay", "description": "Harmon refused to pay INV-2024-118.", "issueIds": ["ISS-01"], "actorIds": ["CH-003"], "knownByIds": ["CH-001"], "significance": "Key" }""";
        var (gateway, _) = Gateway(entry);

        await new CaseGuideGenerator(gateway).RegenerateEntryAsync(draft, GuidePart.Facts, "F-006", "");

        Assert.Equal("2024-08-05", raymond.ActiveTo);
        Assert.DoesNotContain(CaseGuideValidator.Validate(draft), f => f.Message.Contains("is only active"));
    }
}
