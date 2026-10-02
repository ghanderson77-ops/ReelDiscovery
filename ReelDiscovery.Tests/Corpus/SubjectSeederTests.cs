using System.Text.Json;
using ReelDiscovery.Models;
using ReelDiscovery.Models.Case;
using ReelDiscovery.Models.Corpus;
using ReelDiscovery.Services.Ai;
using ReelDiscovery.Services.Corpus;
using ReelDiscovery.Tests.Case;
using ReelDiscovery.Tests.Fakes;

namespace ReelDiscovery.Tests.Corpus;

public class SubjectSeederTests
{
    // ----- Fixtures -----

    private static Dictionary<NonRelevantCategory, int> Mix(int daily = 0, int ancillary = 0, int newsletter = 0, int decoy = 0) => new()
    {
        [NonRelevantCategory.DailyBusiness] = daily,
        [NonRelevantCategory.Ancillary] = ancillary,
        [NonRelevantCategory.NewsletterSpamPersonal] = newsletter,
        [NonRelevantCategory.Decoy] = decoy
    };

    private static NonRelevantSkeleton Skeleton(CaseDraft draft, Dictionary<NonRelevantCategory, int> mix, int seed = 42)
    {
        var ctx = SkeletonContext.Create(draft, new CorpusSettings(), false, new Random(seed));
        var budget = BudgetAllocator.AllocateRelevant(ctx.DatedFacts, 120, null, ctx.Random);
        var relevant = ThreadSkeletonBuilder.BuildRelevant(ctx, budget);
        return NonRelevantSkeletonBuilder.Build(ctx, mix, relevant);
    }

    private static DateTimeOffset Chicago(int year, int month, int day, int hour) =>
        new(year, month, day, hour, 0, 0, TimeSpan.FromHours(-5));

    /// <summary>
    /// Daily business: CH-002 -> CH-009, 3 messages in March 2024, one Word attachment.
    /// </summary>
    private static PlannedThread DailyThread(string id = "N-0001")
    {
        var thread = new PlannedThread { Id = id, Relevance = ThreadRelevance.NotRelevant, Category = NonRelevantCategory.DailyBusiness, OwnerCustodianId = "CH-002" };
        thread.Messages.Add(new PlannedMessage { Seq = 1, Kind = MessageKind.New, FromId = "CH-002", ToIds = new List<string> { "CH-009" }, SentAt = Chicago(2024, 3, 12, 9), TimeZoneId = "America/Chicago" });
        thread.Messages.Add(new PlannedMessage { Seq = 2, ReplyToSeq = 1, Kind = MessageKind.Reply, FromId = "CH-009", ToIds = new List<string> { "CH-002" }, SentAt = Chicago(2024, 3, 12, 11), TimeZoneId = "America/Chicago" });
        thread.Messages.Add(new PlannedMessage
        {
            Seq = 3, ReplyToSeq = 2, Kind = MessageKind.Reply, FromId = "CH-002", ToIds = new List<string> { "CH-009" }, SentAt = Chicago(2024, 3, 13, 10), TimeZoneId = "America/Chicago",
            Attachments = new List<PlannedAttachment> { new() { Kind = AttachmentType.Word, FileName = "Document 1.docx" } }
        });
        return thread;
    }

    private static PlannedThread NewsletterThread(string id = "N-0002", string externalId = "EXT-001")
    {
        var thread = new PlannedThread { Id = id, Relevance = ThreadRelevance.NotRelevant, Category = NonRelevantCategory.NewsletterSpamPersonal, OwnerCustodianId = "CH-001" };
        thread.Messages.Add(new PlannedMessage { Seq = 1, Kind = MessageKind.New, FromId = externalId, ToIds = new List<string> { "CH-001" }, SentAt = Chicago(2024, 4, 3, 8), TimeZoneId = "America/New_York" });
        return thread;
    }

    private static PlannedThread DecoyThread(string id = "N-0003")
    {
        var thread = new PlannedThread
        {
            Id = id, Relevance = ThreadRelevance.NotRelevant, Category = NonRelevantCategory.Decoy, OwnerCustodianId = "CH-002",
            IssueIds = new List<string> { "ISS-01" }
        };
        thread.Messages.Add(new PlannedMessage { Seq = 1, Kind = MessageKind.New, FromId = "CH-002", ToIds = new List<string> { "CH-004" }, SentAt = Chicago(2024, 3, 20, 9), TimeZoneId = "America/Chicago" });
        thread.Messages.Add(new PlannedMessage { Seq = 2, ReplyToSeq = 1, Kind = MessageKind.Reply, FromId = "CH-004", ToIds = new List<string> { "CH-002" }, SentAt = Chicago(2024, 3, 20, 13), TimeZoneId = "America/Phoenix" });
        return thread;
    }

    private static List<ExternalParty> Externals() => new()
    {
        new() { Id = "EXT-001", Name = "Commercial Builder Weekly", Email = "news@commercialbuilderweekly.com", Domain = "commercialbuilderweekly.com", Kind = ExternalKind.Newsletter },
        new() { Id = "EXT-002", Name = "Daily Deal Alerts", Email = "offers@dailydealalerts.com", Domain = "dailydealalerts.com", Kind = ExternalKind.Spam },
        new() { Id = "EXT-003", Name = "Jamie Ellison", Email = "jamie.ellison@gmail.com", Domain = "gmail.com", Kind = ExternalKind.Personal }
    };

    /// <summary>
    /// Every value string the everyday prompt must never contain: fact ids/titles/descriptions, issue ids/titles, canonical values.
    /// </summary>
    private static IEnumerable<string> CaseStrings(CaseGuide guide) =>
        guide.Facts.SelectMany(f => new[] { f.Title, f.Description })
            .Concat(guide.Issues.Select(i => i.Title))
            .Concat(guide.Values.Select(v => v.Value))
            .Where(s => !string.IsNullOrWhiteSpace(s));

    // ----- Task 1: slots, prompts, must-not-mention -----

    [Fact]
    public void SubjectSeedBatchDto_IsStrict()
    {
        Assert.Equal(new[] { typeof(SubjectSeeder.SubjectSeedBatchDto) }, SubjectSeeder.SchemaTypes);
        foreach (var type in SubjectSeeder.SchemaTypes)
        {
            var schema = JsonSchemaRegistry.GetSchemaJson(type);
            var errors = StrictSchemaValidator.Validate(schema);
            Assert.True(errors.Count == 0, $"{type.Name}: {string.Join("; ", errors)}");
            Assert.True(StrictSchemaValidator.CountUnionTypes(schema) <= 16);
        }
    }

    [Fact]
    public void BuildBatches_SlotIdsInThreadOrder_DecoysSeparate_AtMost100()
    {
        var draft = CorpusTestData.RichDraft();
        var skeleton = Skeleton(draft, Mix(daily: 400, ancillary: 120, newsletter: 120, decoy: 80));
        var threads = skeleton.Threads;

        var batches = SubjectSeeder.BuildBatches(threads);

        var flat = batches.SelectMany(b => b).ToList();
        Assert.Equal(threads.Count, flat.Count);
        Assert.Equal(threads.Select(t => t.Id).OrderBy(i => i), flat.Select(s => s.Thread.Id).OrderBy(i => i));
        var bySlot = flat.OrderBy(s => s.SlotId, StringComparer.Ordinal).ToList();
        for (var i = 0; i < threads.Count; i++)
        {
            Assert.Equal($"S-{i + 1:0000}", bySlot[i].SlotId);
            Assert.Same(threads[i], bySlot[i].Thread);
        }

        Assert.All(batches, b => Assert.InRange(b.Count, 1, SubjectSeeder.SlotsPerBatch));
        Assert.All(batches, b =>
        {
            var decoys = b.Count(s => s.Thread.Category == NonRelevantCategory.Decoy);
            Assert.True(decoys == 0 || decoys == b.Count, "decoys share a batch with everyday slots");
        });
        Assert.True(batches.Count >= 3);
    }

    [Fact]
    public void BuildSeedPrompt_SlotLineFormat()
    {
        var draft = CorpusTestData.RichDraft();
        var slots = new List<(string, PlannedThread)> { ("S-0001", DailyThread()), ("S-0042", NewsletterThread()) };

        var prompt = SubjectSeeder.BuildSeedPrompt(draft, slots, Externals());

        Assert.Contains("S-0001 | Daily business | from CH-002 Tom Becker, Project Manager, Brightline Glazing, LLC | to CH-009 Owen Reilly | Mar 2024 | 3 messages | attachments: Word", prompt);
        Assert.Contains("S-0042 | Newsletter | external: newsletter | to CH-001 Maria Delgado | Apr 2024 | 1 message | attachments: none", prompt);
        Assert.Contains("Write a realistic subject line for each slot: the everyday work of these people, unrelated to any dispute. For external slots, also give the sending publication, company or person. Keep angle empty.", prompt);
        Assert.Contains("Keep every name the document uses exactly as written", prompt);
        Assert.Equal(new[] { "S-0001", "S-0042" }, CorpusTestData.SlotIdsIn(prompt));
    }

    [Fact]
    public void BuildSeedPrompt_ContainsNoCaseFactsIssuesOrValues()
    {
        var draft = CorpusTestData.RichDraft();
        var guide = draft.Guide!;
        var skeleton = Skeleton(draft, Mix(daily: 300, ancillary: 100, newsletter: 100, decoy: 60));
        var everyday = SubjectSeeder.BuildBatches(skeleton.Threads)
            .Where(b => b[0].Thread.Category != NonRelevantCategory.Decoy)
            .ToList();
        Assert.NotEmpty(everyday);

        foreach (var batch in everyday)
        {
            var prompt = SubjectSeeder.BuildSeedPrompt(draft, batch, skeleton.Externals);
            foreach (var text in CaseStrings(guide))
                Assert.DoesNotContain(text.Trim(), prompt, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotMatch(@"\bF-\d{3}\b", prompt);
            Assert.DoesNotMatch(@"\bISS-\d{2}\b", prompt);
            Assert.DoesNotContain("$184,250.00", prompt);
            Assert.DoesNotContain("CO-007", prompt);
            Assert.DoesNotContain("INV-2024-118", prompt);
        }
    }

    [Fact]
    public void BuildSeedPrompt_ListsOnlySlotParticipants()
    {
        var draft = CorpusTestData.RichDraft();
        var prompt = SubjectSeeder.BuildSeedPrompt(draft, new List<(string, PlannedThread)> { ("S-0001", DailyThread()) }, Externals());

        Assert.Contains("CH-002 Tom Becker", prompt);
        Assert.Contains("CH-009 Owen Reilly", prompt);
        Assert.DoesNotContain("Priya Nair", prompt);
        Assert.DoesNotContain("Raymond Harmon", prompt);
        Assert.DoesNotContain("Commercial Builder Weekly", prompt);
    }

    [Fact]
    public void MustNotMention_FactTitlesAndFactLinkedValues_NoProjectOrOrgNames()
    {
        var draft = CorpusTestData.RichDraft();
        var guide = draft.Guide!;

        var list = SubjectSeeder.MustNotMention(guide);

        foreach (var fact in guide.Facts)
            Assert.Contains(fact.Title.Trim(), list);
        Assert.Contains("$184,250.00", list);
        Assert.Contains("CO-007", list);
        Assert.Contains("INV-2024-118", list);
        Assert.DoesNotContain("Lakeview Tower", list);
        foreach (var org in guide.Organizations)
            Assert.DoesNotContain(org.Name, list);
        Assert.Equal(list.Count, list.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void MustNotMention_SkipsValuesWithoutFacts_AndOtherKinds()
    {
        var guide = CorpusTestData.RichDraft().Guide!;
        guide.Values.Add(new CanonicalValue { Id = "V-010", Kind = ValueKind.Amount, Label = "Unlinked", Value = "$9,999.00" });
        guide.Values.Add(new CanonicalValue { Id = "V-011", Kind = ValueKind.Address, Label = "Site", Value = "100 Lake Shore Dr", FactIds = new List<string> { "F-001" } });
        guide.Values.Add(new CanonicalValue { Id = "V-012", Kind = ValueKind.PurchaseOrder, Label = "PO", Value = "PO-5521", FactIds = new List<string> { "F-004" } });

        var list = SubjectSeeder.MustNotMention(guide);

        Assert.DoesNotContain("$9,999.00", list);
        Assert.DoesNotContain("100 Lake Shore Dr", list);
        Assert.Contains("PO-5521", list);
    }

    [Fact]
    public void BuildDecoyPrompt_ProjectsOrganizationsAndForbiddenList()
    {
        var draft = CorpusTestData.RichDraft();
        var guide = draft.Guide!;
        var mustNot = SubjectSeeder.MustNotMention(guide);

        var prompt = SubjectSeeder.BuildDecoyPrompt(draft, new List<(string, PlannedThread)> { ("S-0003", DecoyThread()) }, mustNot);

        Assert.Contains("Lakeview Tower", prompt);
        foreach (var org in guide.Organizations)
            Assert.Contains(org.Name, prompt);
        Assert.Contains("Do not mention any of these", prompt);
        var tail = prompt[prompt.IndexOf("Do not mention any of these", StringComparison.Ordinal)..];
        foreach (var term in mustNot)
            Assert.Contains(term, tail);
        Assert.Contains("it mentions the same projects, companies and people as the matter, but it is about a routine, unrelated topic", prompt);
        Assert.Contains("S-0003 |", prompt);
        Assert.Contains("from CH-002 Tom Becker", prompt);
        Assert.DoesNotContain("ISS-01", prompt);
        Assert.Equal(new[] { "S-0003" }, CorpusTestData.SlotIdsIn(prompt));
    }

    [Fact]
    public void Prompts_NoJsonOrFictionalInstructions()
    {
        var draft = CorpusTestData.RichDraft();
        var seed = SubjectSeeder.BuildSeedPrompt(draft, new List<(string, PlannedThread)> { ("S-0001", DailyThread()) }, Externals());
        var decoy = SubjectSeeder.BuildDecoyPrompt(draft, new List<(string, PlannedThread)> { ("S-0002", DecoyThread()) }, SubjectSeeder.MustNotMention(draft.Guide!));

        foreach (var prompt in new[] { seed, decoy })
        {
            CaseTestData.AssertNoJsonInstructions(new LlmProviderRequest { SystemPrompt = CorpusPlanPrompts.SystemPrompt, UserPrompt = prompt });
            Assert.DoesNotContain("JSON", prompt);
            Assert.DoesNotContain("invented names fictional", prompt, StringComparison.OrdinalIgnoreCase);
        }
    }
}
