using System.Globalization;
using ReelDiscovery.Models.Case;
using ReelDiscovery.Models.Discovery;
using ReelDiscovery.Services.Ai;
using ReelDiscovery.Services.Discovery;
using ReelDiscovery.Tests.Case;

namespace ReelDiscovery.Tests.Discovery;

public class DiscoveryPromptInvariantTests
{
    // ---- system prompt ----

    [Fact]
    public void SystemPrompt_IsByteStableAndCarriesStructuredCaseData()
    {
        var draft = DiscoveryTestData.LitigationDraft();
        var guide = draft.Guide!;

        var first = DiscoveryPrompts.BuildSystemPrompt(draft);
        var second = DiscoveryPrompts.BuildSystemPrompt(DiscoveryTestData.LitigationDraft());

        Assert.Equal(first, second, StringComparer.Ordinal);
        foreach (var issue in guide.Issues)
        {
            Assert.Contains(issue.Id, first);
            Assert.Contains(issue.Title, first);
        }
        foreach (var party in draft.CoreDocument!.Parties)
            Assert.Contains(party.Name, first);
        foreach (var member in guide.Cast)
            Assert.Contains(member.FullName, first);
        foreach (var value in guide.Values)
            Assert.Contains(value.Value, first);
        Assert.Contains("including characters and companies from films, TV series, books or games", first);
        Assert.Contains("Take a firm position", first);
        Assert.Contains("Michael Scott", first);
    }

    [Fact]
    public void SystemPrompt_HasNoFacts()
    {
        var draft = DiscoveryTestData.LitigationDraft();

        var prompt = DiscoveryPrompts.BuildSystemPrompt(draft);

        foreach (var fact in draft.Guide!.Facts)
            Assert.DoesNotContain(fact.Description, prompt);
    }

    // ---- name, json and raw-text invariants ----

    [Fact]
    public void NoPrompt_SaysFictionalOrJson()
    {
        foreach (var prompt in AllPrompts())
        {
            Assert.DoesNotContain("fictional", prompt, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("json", prompt, StringComparison.OrdinalIgnoreCase);
            CaseTestData.AssertNoJsonInstructions(new LlmProviderRequest { SystemPrompt = prompt, UserPrompt = string.Empty });
        }
    }

    [Fact]
    public void NoPrompt_CarriesCoreDocumentParagraphsOrScenario()
    {
        var drafts = new[] { DiscoveryTestData.LitigationDraft(), DiscoveryTestData.MemoCriminalDraft() };
        var paragraphs = drafts
            .SelectMany(d => d.CoreDocument!.Sections)
            .SelectMany(s => s.Paragraphs)
            .Select(p => p.Trim())
            .Where(p => p.Length > 60)
            .ToList();
        var scenarios = drafts.SelectMany(d => new[] { d.Scenario, d.EffectiveScenario }).Where(s => !string.IsNullOrWhiteSpace(s)).ToList();
        Assert.NotEmpty(paragraphs);

        foreach (var prompt in AllPrompts())
        {
            foreach (var paragraph in paragraphs)
                Assert.DoesNotContain(paragraph, prompt);
            foreach (var scenario in scenarios)
                Assert.DoesNotContain(scenario.Trim(), prompt);
        }
    }

    [Fact]
    public void Substitution_SubstituteListedOriginalNever()
    {
        var draft = DiscoveryTestData.LitigationDraft();

        var system = DiscoveryPrompts.BuildSystemPrompt(draft);

        Assert.Contains("Use the name Apex Fabrication Co.", system);
        foreach (var prompt in AllPrompts())
            Assert.DoesNotContain("Acme", prompt);
    }

    // ---- deposition prompts ----

    [Fact]
    public void DepositionPrompt_WitnessNameVerbatim()
    {
        var draft = DiscoveryTestData.LitigationDraft();
        var plan = DepositionPlanner.Plan(draft, new[] { "CH-013" }, 25, null, null).Single();

        var prompt = Deposition(draft, plan, plan.Chunks[0]);

        Assert.Contains("Michael Scott", prompt);
    }

    [Fact]
    public void DepositionPrompt_OnlyKnownFacts()
    {
        var draft = DiscoveryTestData.LitigationDraft();
        var plan = DepositionPlanner.Plan(draft, new[] { "CH-013" }, 25, null, null).Single();
        var known = plan.KnownFacts.Select(f => f.Id).ToHashSet();
        Assert.NotEmpty(known);

        foreach (var chunk in plan.Chunks)
        {
            var prompt = Deposition(draft, plan, chunk);
            foreach (var id in known)
                Assert.Contains(id, prompt);
            foreach (var fact in draft.Guide!.Facts.Where(f => !known.Contains(f.Id)))
            {
                Assert.DoesNotContain(fact.Id, prompt);
                Assert.DoesNotContain(fact.Description, prompt);
            }
        }
    }

    [Fact]
    public void DepositionPrompt_DatePartTurnsExhibitsAndSpeakers()
    {
        var draft = DiscoveryTestData.LitigationDraft();
        var plan = DepositionPlanner.Plan(draft, new[] { "CH-003" }, 25, null, null).Single();
        var withExhibits = plan.Chunks.First(c => c.ExhibitNumbers.Count > 0);
        var chunk = Copy(withExhibits);
        chunk.Part = 2;
        chunk.PartCount = 3;
        chunk.TargetTurns = 112;

        var prompt = Deposition(draft, plan, chunk);

        Assert.Contains(plan.Schedule.Date.ToString("MMMM d, yyyy", CultureInfo.InvariantCulture), prompt);
        Assert.Contains("part 2 of 3", prompt);
        Assert.Contains("about 112 turns", prompt);
        foreach (var number in chunk.ExhibitNumbers)
        {
            var exhibit = plan.Exhibits.Single(e => e.Number == number);
            Assert.Contains($"Exhibit {number}", prompt);
            Assert.Contains(exhibit.Description, prompt);
        }
        foreach (var speaker in plan.Speakers)
            Assert.Contains(speaker.SpeakerId, prompt);
    }

    [Fact]
    public void DepositionPrompt_FirstPartAsksForHonorifics_LaterPartsCarryPreviousTurns()
    {
        var draft = DiscoveryTestData.LitigationDraft();
        var plan = DepositionPlanner.Plan(draft, new[] { "CH-003" }, 25, null, null).Single();
        var first = Copy(plan.Chunks[0]);
        first.Part = 1;
        var later = Copy(plan.Chunks[0]);
        later.Part = 2;
        later.PartCount = 2;

        var turns = Enumerable.Range(1, 10)
            .Select(i => new DepositionTurn { Kind = i % 2 == 1 ? TurnKind.Q : TurnKind.A, SpeakerId = "CH-003", Text = $"Turn text number {i:00}." })
            .ToList();
        var covered = new List<string> { "Background and role", "Covered topic marker" };

        var firstPrompt = Deposition(draft, plan, first);
        var laterPrompt = DiscoveryPrompts.BuildDepositionPrompt(draft, plan, later, turns, covered, string.Empty, Array.Empty<DiscoveryViolation>());

        Assert.Contains("give each speaker id an honorific (MR, MS or DR)", firstPrompt);
        Assert.DoesNotContain("give each speaker id an honorific", laterPrompt);
        Assert.Contains("Previous turns", laterPrompt);
        for (var i = 5; i <= 10; i++)
            Assert.Contains($"Turn text number {i:00}.", laterPrompt);
        for (var i = 1; i <= 4; i++)
            Assert.DoesNotContain($"Turn text number {i:00}.", laterPrompt);
        Assert.Contains("Covered topic marker", laterPrompt);
    }

    [Fact]
    public void DepositionPrompt_AdverseWitnessWithExhibits_HasImpeachmentRule()
    {
        var draft = DiscoveryTestData.LitigationDraft();
        foreach (var witnessId in new[] { "CH-003", "CH-001" })
        {
            var plan = DepositionPlanner.Plan(draft, new[] { witnessId }, 25, null, null).Single();
            Assert.NotEqual(WitnessSide.NonParty, plan.Witness.Side);
            var impeachment = plan.Exhibits.Single(e => e.IsImpeachment);

            // Put every exhibit in one chunk, impeachment exhibit last by number, so it must be moved first
            var chunk = Copy(plan.Chunks[0]);
            chunk.ExhibitNumbers = plan.Exhibits.Where(e => !e.IsImpeachment).Select(e => e.Number).Append(impeachment.Number).ToList();

            var prompt = Deposition(draft, plan, chunk);

            Assert.Contains(DiscoveryPrompts.ImpeachmentRule, prompt);
            var ruleAt = prompt.IndexOf(DiscoveryPrompts.ImpeachmentRule, StringComparison.Ordinal);
            var tail = prompt[ruleAt..];
            foreach (var exhibit in plan.Exhibits)
                Assert.Contains($"Exhibit {exhibit.Number} | {LongDate(exhibit.Date)} | {exhibit.Description}", tail);
            var impeachmentAt = tail.IndexOf($"Exhibit {impeachment.Number} |", StringComparison.Ordinal);
            foreach (var other in plan.Exhibits.Where(e => !e.IsImpeachment))
                Assert.True(impeachmentAt < tail.IndexOf($"Exhibit {other.Number} |", StringComparison.Ordinal));
        }
    }

    [Fact]
    public void DepositionPrompt_NoExhibitsOrNonParty_NoImpeachmentRule()
    {
        var draft = DiscoveryTestData.LitigationDraft();
        var plan = DepositionPlanner.Plan(draft, new[] { "CH-003" }, 25, null, null).Single();
        var noExhibits = Copy(plan.Chunks[0]);
        noExhibits.ExhibitNumbers.Clear();

        Assert.DoesNotContain(DiscoveryPrompts.ImpeachmentRule, Deposition(draft, plan, noExhibits));
        Assert.DoesNotContain("Impeachment", Deposition(draft, plan, noExhibits));

        var memo = DiscoveryTestData.MemoCriminalDraft();
        var corpus = new DiscoveryCorpus
        {
            Usable = true,
            Candidates = new List<ExhibitCandidate>
            {
                new() { DocId = "RD-0100", ThreadId = "T-RD-0100", Subject = "SAR filed", Date = "2023-08-21", ParticipantIds = new List<string> { "CH-004" }, IsHot = true, FactIds = new List<string> { "F-005" } }
            }
        };
        var nonParty = DepositionPlanner.Plan(memo, new[] { "CH-004" }, 25, corpus, null).Single();
        Assert.Equal(WitnessSide.NonParty, nonParty.Witness.Side);
        var chunk = Copy(nonParty.Chunks[0]);
        chunk.ExhibitNumbers = nonParty.Exhibits.Select(e => e.Number).ToList();
        Assert.NotEmpty(chunk.ExhibitNumbers);

        var prompt = Deposition(memo, nonParty, chunk);

        Assert.DoesNotContain(DiscoveryPrompts.ImpeachmentRule, prompt);
        Assert.Contains($"Exhibit {chunk.ExhibitNumbers[0]}", prompt);
    }

    // ---- written prompts ----

    [Fact]
    public void WrittenPrompt_Rfa_ListsPrefixesAndFactDetails()
    {
        var draft = DiscoveryTestData.LitigationDraft();
        var guide = draft.Guide!;
        var plan = WrittenDiscoveryPlanner.Plan(draft, DiscoveryKind.RequestsForAdmission, new DiscoverySettings { RfaCount = 20 },
            WrittenDiscoveryPlannerTests.TwoHotCandidates())!;

        var prompt = DiscoveryPrompts.BuildWrittenPrompt(draft, plan, plan.Slots, string.Empty, Array.Empty<DiscoveryViolation>());

        Assert.Contains(plan.Title, prompt);
        Assert.Contains("Return every slot exactly once", prompt);
        foreach (var slot in plan.Slots)
        {
            Assert.Contains($"Slot {slot.Number} |", prompt);
            Assert.Contains(slot.Prefix, prompt);
            if (slot.IsGenuineness)
            {
                Assert.Contains(slot.DocSubject, prompt);
                continue;
            }
            var fact = guide.FindFact(slot.FactId)!;
            Assert.Contains(LongDate(fact.Date), prompt);
            foreach (var actor in fact.ActorIds.Select(id => guide.FindCast(id)).Where(c => c != null))
                Assert.Contains(actor!.FullName, prompt);
            foreach (var value in guide.Values.Where(v => v.FactIds.Contains(fact.Id) && v.Kind == ValueKind.Amount))
                Assert.Contains(value.Value, prompt);
        }
    }

    [Fact]
    public void WrittenPrompt_Rog_OneQuestionNoSubparts()
    {
        var draft = DiscoveryTestData.LitigationDraft();
        var plan = WrittenDiscoveryPlanner.Plan(draft, DiscoveryKind.Interrogatories, new DiscoverySettings { RogCount = 18 }, null)!;

        var prompt = DiscoveryPrompts.BuildWrittenPrompt(draft, plan, plan.Slots, string.Empty, Array.Empty<DiscoveryViolation>());

        Assert.Contains("one question", prompt);
        Assert.Contains("no lettered or numbered parts", prompt);
        Assert.Contains("persons with knowledge", prompt);
        foreach (var slot in plan.Slots)
            Assert.Contains($"Slot {slot.Number} |", prompt);
    }

    [Fact]
    public void WrittenPrompt_Rfp_NamesCustodiansAndDateRanges()
    {
        var draft = DiscoveryTestData.LitigationDraft();
        var guide = draft.Guide!;
        var plan = WrittenDiscoveryPlanner.Plan(draft, DiscoveryKind.RequestsForProduction, new DiscoverySettings { RfpCount = 12 }, null)!;

        var prompt = DiscoveryPrompts.BuildWrittenPrompt(draft, plan, plan.Slots, string.Empty, Array.Empty<DiscoveryViolation>());

        Assert.Contains("All Documents and Communications Concerning", prompt);
        foreach (var slot in plan.Slots)
        {
            foreach (var id in slot.CustodianIds)
                Assert.Contains(guide.FindCast(id)!.FullName, prompt);
            if (slot.FromDate.Length > 0)
                Assert.Contains($"{LongDate(slot.FromDate)} to {LongDate(slot.ToDate)}", prompt);
        }
    }

    // ---- guidance and feedback ----

    [Fact]
    public void Guidance_TruncatedInUserGuidanceSection_FeedbackAfterHeader()
    {
        var draft = DiscoveryTestData.LitigationDraft();
        var plan = DepositionPlanner.Plan(draft, new[] { "CH-003" }, 25, null, null).Single();
        var written = WrittenDiscoveryPlanner.Plan(draft, DiscoveryKind.Interrogatories, new DiscoverySettings { RogCount = 18 }, null)!;
        var guidance = new string('g', 999) + "X" + new string('h', 500);
        var feedback = new List<DiscoveryViolation> { new() { Rule = DiscoveryRule.D4MissingRequest, Number = 7 } };

        var prompts = new[]
        {
            DiscoveryPrompts.BuildDepositionPrompt(draft, plan, plan.Chunks[0], Array.Empty<DepositionTurn>(), Array.Empty<string>(), guidance, feedback),
            DiscoveryPrompts.BuildWrittenPrompt(draft, written, written.Slots, guidance, feedback)
        };

        foreach (var prompt in prompts)
        {
            var section = prompt.IndexOf("User guidance", StringComparison.Ordinal);
            Assert.True(section >= 0);
            Assert.Contains(new string('g', 999) + "X", prompt[section..]);
            Assert.DoesNotContain("h", prompt[(section + "User guidance".Length)..].Split('\n', 3)[1]);
            var header = prompt.IndexOf(DiscoveryValidator.FeedbackHeader, StringComparison.Ordinal);
            Assert.True(header > section);
            Assert.Contains("Request 7 is missing", prompt[header..]);
        }
    }

    [Fact]
    public void EmptyGuidanceAndFeedback_NoSections()
    {
        var draft = DiscoveryTestData.LitigationDraft();
        var plan = DepositionPlanner.Plan(draft, new[] { "CH-003" }, 25, null, null).Single();

        var prompt = Deposition(draft, plan, plan.Chunks[0]);

        Assert.DoesNotContain("User guidance", prompt);
        Assert.DoesNotContain(DiscoveryValidator.FeedbackHeader, prompt);
    }

    [Fact]
    public void SchemaTypes_AreTheTwoChunkDtos()
    {
        Assert.Equal(new[] { typeof(DiscoveryPrompts.DepositionChunkDto), typeof(DiscoveryPrompts.RequestSetDto) }, DiscoveryPrompts.SchemaTypes);
    }

    // ---- helpers ----

    private static string Deposition(CaseDraft draft, DepositionPlan plan, DepositionChunk chunk) =>
        DiscoveryPrompts.BuildDepositionPrompt(draft, plan, chunk, Array.Empty<DepositionTurn>(), Array.Empty<string>(), string.Empty,
            Array.Empty<DiscoveryViolation>());

    private static DepositionChunk Copy(DepositionChunk c) => new()
    {
        Index = c.Index,
        TopicIndex = c.TopicIndex,
        IssueId = c.IssueId,
        TopicTitle = c.TopicTitle,
        Part = c.Part,
        PartCount = c.PartCount,
        Pages = c.Pages,
        TargetTurns = c.TargetTurns,
        FactIds = new List<string>(c.FactIds),
        ExhibitNumbers = new List<int>(c.ExhibitNumbers)
    };

    private static string LongDate(string yyyyMmDd) =>
        DateTime.TryParseExact(yyyyMmDd, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
            ? d.ToString("MMMM d, yyyy", CultureInfo.InvariantCulture)
            : yyyyMmDd;

    /// <summary>
    /// System prompts and every deposition chunk and written-set prompt for the civil case and the three memo variants.
    /// </summary>
    private static IEnumerable<string> AllPrompts()
    {
        var drafts = new[]
        {
            DiscoveryTestData.LitigationDraft(),
            DiscoveryTestData.MemoCriminalDraft(),
            DiscoveryTestData.MemoRegulatoryDraft(),
            DiscoveryTestData.MemoCivilDraft()
        };
        foreach (var draft in drafts)
        {
            yield return DiscoveryPrompts.BuildSystemPrompt(draft);
            foreach (var plan in DepositionPlanner.Plan(draft, DepositionPlanner.Eligible(draft).Select(w => w.Id).ToList(), 25, null, null))
            {
                foreach (var chunk in plan.Chunks)
                    yield return Deposition(draft, plan, chunk);
            }
            foreach (var kind in new[] { DiscoveryKind.RequestsForProduction, DiscoveryKind.Interrogatories, DiscoveryKind.RequestsForAdmission })
            {
                var written = WrittenDiscoveryPlanner.Plan(draft, kind, new DiscoverySettings(), WrittenDiscoveryPlannerTests.TwoHotCandidates());
                if (written != null)
                    yield return DiscoveryPrompts.BuildWrittenPrompt(draft, written, written.Slots, string.Empty, Array.Empty<DiscoveryViolation>());
            }
        }
    }
}
