using System.Text.Json;
using System.Text.RegularExpressions;
using ReelDiscovery.Models;
using ReelDiscovery.Models.Case;
using ReelDiscovery.Models.Corpus;
using ReelDiscovery.Services.Ai;
using ReelDiscovery.Services.Corpus;
using ReelDiscovery.Tests.Case;
using ReelDiscovery.Tests.Fakes;

namespace ReelDiscovery.Tests.Corpus;

public partial class SubjectSeederTests
{
    // "S-0001 | Daily business | ..."
    [GeneratedRegex(@"^(S-\d{4}) \| ([^|]+?) \|", RegexOptions.Multiline)]
    private static partial Regex SlotLinePattern();

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

    // ----- Task 2: SeedAsync -----

    private static List<(string SlotId, string Category)> Slots(string prompt) =>
        SlotLinePattern().Matches(prompt).Select(m => (m.Groups[1].Value, m.Groups[2].Value.Trim())).ToList();

    private static object Seed(string slotId, string category, string? subject = null, string? angle = null, string? senderName = null) => new
    {
        slotId,
        subject = subject ?? $"Subject {slotId}",
        angle = angle ?? (category == "Near-miss" ? $"Angle {slotId}." : string.Empty),
        senderName = senderName ?? (category is "Newsletter" or "Spam" or "Vendor" ? $"Sender {slotId}" : category == "Personal" ? $"Pat Sender{slotId[2..]}" : string.Empty)
    };

    /// <summary>
    /// Answers every slot listed in the prompt except <paramref name="omit"/>.
    /// </summary>
    private static LlmProviderResponse Echo(LlmProviderRequest request, params string[] omit)
    {
        var seeds = Slots(request.UserPrompt).Where(s => !omit.Contains(s.SlotId)).Select(s => Seed(s.SlotId, s.Category)).ToArray();
        return FakeLlmProvider.Ok(JsonSerializer.Serialize(new { seeds }));
    }

    private static LlmProviderResponse Respond(params object[] seeds) => FakeLlmProvider.Ok(JsonSerializer.Serialize(new { seeds }));

    private static (RecordingGateway Gateway, FakeLlmProvider Fake) Gateway()
    {
        var (gateway, fake) = CorpusTestData.CreateGateway();
        return (new RecordingGateway(gateway), fake);
    }

    private static HashSet<string> CaseDomains(CaseGuide guide) =>
        guide.Organizations.Select(o => o.Domain).Where(d => !string.IsNullOrWhiteSpace(d)).ToHashSet(StringComparer.OrdinalIgnoreCase);

    private static string LowerFirst(string text) => text.Length == 0 ? text : char.ToLowerInvariant(text[0]) + text[1..];

    [Fact]
    public async Task SeedAsync_HappyPath_SeedsEveryThread_AndSendsStoryRequests()
    {
        var draft = CorpusTestData.RichDraft();
        var guide = draft.Guide!;
        var skeleton = Skeleton(draft, Mix(daily: 300, ancillary: 100, newsletter: 120, decoy: 60));
        var n = SubjectSeeder.BuildBatches(skeleton.Threads).Count;
        var (gateway, fake) = Gateway();
        fake.Respond = r => Echo(r);

        var result = await new SubjectSeeder(gateway).SeedAsync(draft, skeleton.Threads, skeleton.Externals);

        Assert.False(result.IsFatal);
        Assert.Equal(n, result.TotalChunks);
        Assert.Equal(0, result.RefusedChunks);
        Assert.Equal(0, result.FailedChunks);
        Assert.Empty(result.Notes);

        var mustNot = SubjectSeeder.MustNotMention(guide);
        Assert.All(skeleton.Threads, t =>
        {
            Assert.Matches(@"^Subject S-\d{4}$", t.Subject);
            Assert.False(t.UsesFallbackContent);
            if (t.Category == NonRelevantCategory.Decoy)
            {
                Assert.Matches(@"^Angle S-\d{4}\.$", t.DecoyAngle);
                Assert.Equal(mustNot, t.MustNotMention);
            }
            else
            {
                Assert.Empty(t.DecoyAngle);
                Assert.Empty(t.MustNotMention);
            }
        });

        // Every external used as a sender was renamed from a seed; domains never collide with case organizations
        var senders = skeleton.Threads.Select(t => t.Messages[0].FromId).Where(id => id.StartsWith("EXT-")).ToHashSet();
        var caseDomains = CaseDomains(guide);
        foreach (var external in skeleton.Externals)
        {
            Assert.DoesNotContain(external.Domain, caseDomains);
            Assert.EndsWith("@" + external.Domain, external.Email);
            if (senders.Contains(external.Id))
                Assert.Contains("Sender", external.Name);
        }

        Assert.Equal(n, gateway.Requests.Count);
        var expected = Enumerable.Range(1, n).Select(i => $"Corpus Plan - Other email (batch {i} of {n})").OrderBy(s => s, StringComparer.Ordinal);
        Assert.Equal(expected, gateway.Requests.Select(r => r.OperationName).OrderBy(s => s, StringComparer.Ordinal));
        Assert.All(gateway.Requests, r =>
        {
            Assert.Equal(LlmTier.Story, r.Tier);
            Assert.Equal(8_000, r.MaxOutputTokens);
            Assert.Equal(CorpusPlanPrompts.SystemPrompt, r.SystemPrompt);
        });
        Assert.All(fake.Requests, CaseTestData.AssertNoJsonInstructions);
    }

    [Fact]
    public async Task SeedAsync_RenamesExternals_SafeDomains()
    {
        var draft = CorpusTestData.RichDraft();
        var externals = Externals();
        externals.Add(new ExternalParty { Id = "EXT-004", Name = "Keystone Benefits", Email = "offers@keystonebenefits.com", Domain = "keystonebenefits.com", Kind = ExternalKind.Vendor });
        var threads = new List<PlannedThread>
        {
            NewsletterThread("N-0001", "EXT-001"),
            NewsletterThread("N-0002", "EXT-003"),
            NewsletterThread("N-0003", "EXT-004"),
            NewsletterThread("N-0004", "EXT-001")
        };
        var (gateway, fake) = Gateway();
        fake.Respond = _ => Respond(
            Seed("S-0001", "Newsletter", "Spring market outlook", senderName: "Lakeside Facilities Report"),
            Seed("S-0002", "Personal", "Dinner Saturday?", senderName: "Dana Whitfield"),
            Seed("S-0003", "Vendor", "Your benefits renewal", senderName: "Ellis Park"),
            Seed("S-0004", "Newsletter", "Summer issue", senderName: "Another Name Entirely"));

        await new SubjectSeeder(gateway).SeedAsync(draft, threads, externals);

        Assert.Equal("Lakeside Facilities Report", externals[0].Name);
        Assert.Equal("news@lakesidefacilitiesreport.com", externals[0].Email);
        Assert.Equal("lakesidefacilitiesreport.com", externals[0].Domain);

        Assert.Equal("Dana Whitfield", externals[2].Name);
        Assert.Equal("dana.whitfield@gmail.com", externals[2].Email);
        Assert.Equal("gmail.com", externals[2].Domain);

        // "Ellis Park" folds to ellispark.com, the law firm's domain
        Assert.Equal("Ellis Park", externals[3].Name);
        Assert.Equal("ellisparkmail.com", externals[3].Domain);
        Assert.Equal("offers@ellisparkmail.com", externals[3].Email);

        // Untouched external keeps its defaults
        Assert.Equal("Daily Deal Alerts", externals[1].Name);
        Assert.Equal("Spring market outlook", threads[0].Subject);
    }

    [Fact]
    public async Task SeedAsync_ExternalName_CappedFoldedOrKept()
    {
        var draft = CorpusTestData.RichDraft();
        var externals = Externals();
        var threads = new List<PlannedThread> { NewsletterThread("N-0001", "EXT-001"), NewsletterThread("N-0002", "EXT-002") };
        var longName = string.Concat(Enumerable.Repeat("Facility Weekly ", 6));
        var (gateway, fake) = Gateway();
        fake.Respond = _ => Respond(
            Seed("S-0001", "Newsletter", "Issue 12", senderName: longName),
            Seed("S-0002", "Spam", "Big savings", senderName: "@@@ !!!"));

        await new SubjectSeeder(gateway).SeedAsync(draft, threads, externals);

        Assert.True(externals[0].Name.Length <= 60);
        Assert.Matches(@"^news@[a-z0-9]{1,30}\.com$", externals[0].Email);
        Assert.Equal("Daily Deal Alerts", externals[1].Name);
        Assert.Equal("offers@dailydealalerts.com", externals[1].Email);
    }

    [Fact]
    public async Task SeedAsync_DecoyForbidden_ReaskedOnceThenTemplated()
    {
        var draft = CorpusTestData.RichDraft();
        var guide = draft.Guide!;
        var threads = new List<PlannedThread> { DecoyThread("N-0001"), DecoyThread("N-0002") };
        var (gateway, fake) = Gateway();
        fake.Respond = r => Respond(Slots(r.UserPrompt).Select(s => s.SlotId == "S-0001"
            ? Seed(s.SlotId, s.Category, "Lakeview Tower CO-007 payment")
            : Seed(s.SlotId, s.Category)).ToArray());

        var result = await new SubjectSeeder(gateway).SeedAsync(draft, threads, new List<ExternalParty>());

        Assert.Equal(2, fake.Requests.Count);
        Assert.Equal(new[] { "S-0001" }, CorpusTestData.SlotIdsIn(fake.Requests[1].UserPrompt));
        Assert.Equal("Corpus Plan - Other email (batch 1 of 1) retry", gateway.Requests[1].OperationName);

        var templated = threads[0];
        Assert.True(templated.UsesFallbackContent);
        Assert.StartsWith("Lakeview Tower ", templated.Subject);
        var topic = templated.Subject["Lakeview Tower ".Length..];
        Assert.Contains(topic, SubjectSeeder.RoutineTopics.Select(LowerFirst));
        Assert.DoesNotContain("CO-007", templated.Subject);
        Assert.False(string.IsNullOrWhiteSpace(templated.DecoyAngle));
        Assert.Equal(SubjectSeeder.MustNotMention(guide), templated.MustNotMention);

        Assert.Equal("Subject S-0002", threads[1].Subject);
        Assert.False(threads[1].UsesFallbackContent);
        Assert.Contains("1 other email uses a placeholder subject because the model did not plan it.", result.Notes);
    }

    [Fact]
    public async Task SeedAsync_DecoyForbiddenAngle_ReaskFixes()
    {
        var draft = CorpusTestData.RichDraft();
        var threads = new List<PlannedThread> { DecoyThread("N-0001") };
        var (gateway, fake) = Gateway();
        var calls = 0;
        fake.Respond = r => Interlocked.Increment(ref calls) == 1
            ? Respond(Seed("S-0001", "Near-miss", "Lakeview Tower site badges", "Badges, plus the inv-2024-118 balance."))
            : Respond(Seed("S-0001", "Near-miss", "Lakeview Tower site badges", "New badge readers at the site entrance."));

        var result = await new SubjectSeeder(gateway).SeedAsync(draft, threads, new List<ExternalParty>());

        Assert.Equal(2, fake.Requests.Count);
        Assert.Equal("Lakeview Tower site badges", threads[0].Subject);
        Assert.Equal("New badge readers at the site entrance.", threads[0].DecoyAngle);
        Assert.False(threads[0].UsesFallbackContent);
        Assert.Empty(result.Notes);
    }

    [Fact]
    public async Task SeedAsync_UnknownIgnored_MissingReaskedThenTemplatedByCategory()
    {
        var draft = CorpusTestData.RichDraft();
        var externals = Externals();
        var threads = new List<PlannedThread>
        {
            DailyThread("N-0001"),
            NewsletterThread("N-0002", "EXT-001"),
            NewsletterThread("N-0003", "EXT-002"),
            NewsletterThread("N-0004", "EXT-003"),
            DailyThread("N-0005")
        };
        var (gateway, fake) = Gateway();
        fake.Respond = _ => Respond(Seed("S-0005", "Daily business"), Seed("S-9999", "Daily business", "Invented"));

        var result = await new SubjectSeeder(gateway).SeedAsync(draft, threads, externals);

        Assert.Equal(2, fake.Requests.Count);
        Assert.Equal(new[] { "S-0001", "S-0002", "S-0003", "S-0004" }, CorpusTestData.SlotIdsIn(fake.Requests[1].UserPrompt));
        Assert.Contains(threads[0].Subject, SubjectSeeder.RoutineTopics);
        Assert.Equal("Commercial Builder Weekly weekly update", threads[1].Subject);
        Assert.Equal("Limited-time offer from Daily Deal Alerts", threads[2].Subject);
        Assert.Equal("Weekend plans", threads[3].Subject);
        Assert.Equal("Subject S-0005", threads[4].Subject);
        Assert.All(threads.Take(4), t => Assert.True(t.UsesFallbackContent));
        Assert.False(threads[4].UsesFallbackContent);
        Assert.DoesNotContain(threads, t => t.Subject == "Invented");
        Assert.Contains("4 other emails use placeholder subjects because the model did not plan them.", result.Notes);
    }

    [Fact]
    public void TemplateSubject_ByCategory()
    {
        var guide = CorpusTestData.RichDraft().Guide!;
        var externals = Externals().ToDictionary(e => e.Id);
        var random = new Random(7);

        Assert.Contains(SubjectSeeder.TemplateSubject(DailyThread(), guide, externals, random), SubjectSeeder.RoutineTopics);
        var ancillary = DailyThread();
        ancillary.Category = NonRelevantCategory.Ancillary;
        Assert.Contains(SubjectSeeder.TemplateSubject(ancillary, guide, externals, random), SubjectSeeder.RoutineTopics);
        Assert.Equal("Commercial Builder Weekly weekly update", SubjectSeeder.TemplateSubject(NewsletterThread("N-1", "EXT-001"), guide, externals, random));
        Assert.Equal("Limited-time offer from Daily Deal Alerts", SubjectSeeder.TemplateSubject(NewsletterThread("N-1", "EXT-002"), guide, externals, random));
        Assert.Equal("Weekend plans", SubjectSeeder.TemplateSubject(NewsletterThread("N-1", "EXT-003"), guide, externals, random));

        var decoy = SubjectSeeder.TemplateSubject(DecoyThread(), guide, externals, random);
        Assert.StartsWith("Lakeview Tower ", decoy);
        Assert.Contains(decoy["Lakeview Tower ".Length..], SubjectSeeder.RoutineTopics.Select(LowerFirst));
        Assert.Contains("Parking passes for May", SubjectSeeder.RoutineTopics);
        Assert.Contains("Team lunch Thursday", SubjectSeeder.RoutineTopics);
    }

    [Fact]
    public async Task SeedAsync_StripsIdTokens_AndCapsSubject()
    {
        var draft = CorpusTestData.RichDraft();
        var threads = new List<PlannedThread> { DailyThread("N-0001"), DailyThread("N-0002") };
        var (gateway, fake) = Gateway();
        var longSubject = string.Concat(Enumerable.Repeat("Quarterly budget review ", 10));
        fake.Respond = _ => Respond(
            Seed("S-0001", "Daily business", "RE: Ask CH-002 about F-003 and ISS-01 parking"),
            Seed("S-0002", "Daily business", longSubject));

        await new SubjectSeeder(gateway).SeedAsync(draft, threads, new List<ExternalParty>());

        Assert.Equal("Ask about and parking", threads[0].Subject);
        Assert.True(threads[1].Subject.Length <= 120);
        Assert.StartsWith("Quarterly budget review", threads[1].Subject);
    }

    [Fact]
    public async Task SeedAsync_RefusedBatch_CountedAndTemplated()
    {
        var draft = CorpusTestData.RichDraft();
        var threads = new List<PlannedThread> { DailyThread("N-0001"), DecoyThread("N-0002") };
        var (gateway, fake) = Gateway();
        fake.Respond = r => r.UserPrompt.Contains("near-miss") ? FakeLlmProvider.Refused("No.") : Echo(r);

        var result = await new SubjectSeeder(gateway).SeedAsync(draft, threads, new List<ExternalParty>());

        Assert.Equal(2, result.TotalChunks);
        Assert.Equal(1, result.RefusedChunks);
        Assert.Equal("No.", result.RefusalMessage);
        Assert.Equal(2, fake.Requests.Count);
        Assert.Equal("Subject S-0001", threads[0].Subject);
        Assert.True(threads[1].UsesFallbackContent);
        Assert.StartsWith("Lakeview Tower ", threads[1].Subject);
    }

    [Fact]
    public async Task SeedAsync_FailedBatch_ReaskedThenTemplated()
    {
        var draft = CorpusTestData.RichDraft();
        var threads = new List<PlannedThread> { DailyThread("N-0001") };
        var (gateway, fake) = Gateway();
        fake.Respond = _ => FakeLlmProvider.Failed(LlmFailureKind.Network, "connection reset");

        var result = await new SubjectSeeder(gateway).SeedAsync(draft, threads, new List<ExternalParty>());

        Assert.Equal(1, result.FailedChunks);
        Assert.False(result.IsFatal);
        Assert.True(threads[0].UsesFallbackContent);
        Assert.Contains(threads[0].Subject, SubjectSeeder.RoutineTopics);
    }

    [Fact]
    public async Task SeedAsync_FatalError_StopsRemainingBatches()
    {
        var draft = CorpusTestData.RichDraft();
        var skeleton = Skeleton(draft, Mix(daily: 300, ancillary: 100, newsletter: 120, decoy: 60));
        Assert.True(SubjectSeeder.BuildBatches(skeleton.Threads).Count >= 3);
        var (gateway, fake) = Gateway();
        fake.Respond = _ => FakeLlmProvider.Failed(LlmFailureKind.Auth, "Invalid API key");

        var result = await new SubjectSeeder(gateway, maxParallel: 1).SeedAsync(draft, skeleton.Threads, skeleton.Externals);

        Assert.True(result.IsFatal);
        Assert.Contains("Invalid API key", result.FatalError);
        Assert.Single(fake.Requests);
    }

    [Fact]
    public async Task SeedAsync_UserCancel_Throws()
    {
        var draft = CorpusTestData.RichDraft();
        var skeleton = Skeleton(draft, Mix(daily: 300, decoy: 60));
        var (gateway, fake) = Gateway();
        using var cts = new CancellationTokenSource();
        fake.OnSend = _ => cts.Cancel();
        fake.Respond = r => Echo(r);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new SubjectSeeder(gateway).SeedAsync(draft, skeleton.Threads, skeleton.Externals, null, cts.Token));
    }

    [Fact]
    public async Task SeedAsync_ReportsPlanningOtherProgress()
    {
        var draft = CorpusTestData.RichDraft();
        var skeleton = Skeleton(draft, Mix(daily: 300, ancillary: 100, newsletter: 120, decoy: 60));
        var n = SubjectSeeder.BuildBatches(skeleton.Threads).Count;
        var (gateway, fake) = Gateway();
        fake.Respond = r => Echo(r);
        var progress = new LockedProgress();

        await new SubjectSeeder(gateway).SeedAsync(draft, skeleton.Threads, skeleton.Externals, progress);

        var reports = progress.Snapshot();
        Assert.All(reports, r => Assert.Equal(PlanStage.PlanningOther, r.Stage));
        Assert.All(reports, r => Assert.Equal(n, r.StageTotal));
        Assert.Equal(Enumerable.Range(0, n), reports.Select(r => r.StageDone).OrderBy(d => d));
    }

    [Fact]
    public async Task SeedAsync_NoThreads_NoRequests()
    {
        var (gateway, fake) = Gateway();

        var result = await new SubjectSeeder(gateway).SeedAsync(CorpusTestData.RichDraft(), new List<PlannedThread>(), new List<ExternalParty>());

        Assert.Equal(0, result.TotalChunks);
        Assert.Empty(fake.Requests);
    }

    // Synchronous and thread-safe: onStart fires from parallel batch tasks
    private sealed class LockedProgress : IProgress<PlanProgress>
    {
        private readonly List<PlanProgress> _reports = new();

        public void Report(PlanProgress value)
        {
            lock (_reports)
                _reports.Add(value);
        }

        public List<PlanProgress> Snapshot()
        {
            lock (_reports)
                return _reports.ToList();
        }
    }

    private sealed class RecordingGateway : ILlmGateway
    {
        private readonly ILlmGateway _inner;

        public RecordingGateway(ILlmGateway inner)
        {
            _inner = inner;
        }

        public List<LlmRequest> Requests { get; } = new();

        public LlmProviderKind Provider => _inner.Provider;

        public ReelDiscovery.Models.AIModelConfig ResolveModel(LlmTier tier) => _inner.ResolveModel(tier);

        public Task<LlmResult<T>> GenerateAsync<T>(LlmRequest request, CancellationToken ct = default) where T : class
        {
            lock (Requests)
                Requests.Add(new LlmRequest { Tier = request.Tier, OperationName = request.OperationName, SystemPrompt = request.SystemPrompt, UserPrompt = request.UserPrompt, MaxOutputTokens = request.MaxOutputTokens });
            return _inner.GenerateAsync<T>(request, ct);
        }
    }
}
