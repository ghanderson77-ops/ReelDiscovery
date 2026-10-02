using ReelDiscovery.Models;
using ReelDiscovery.Models.Case;
using ReelDiscovery.Models.Corpus;
using ReelDiscovery.Services.Ai;
using ReelDiscovery.Services.Corpus;
using ReelDiscovery.Tests.Case;
using ReelDiscovery.Tests.Fakes;

namespace ReelDiscovery.Tests.Corpus;

public class RelevantThreadFillerTests
{
    // ----- Fixtures -----

    private static List<PlannedThread> Skeleton(CaseDraft draft, int relevant = 180, int seed = 42)
    {
        var ctx = SkeletonContext.Create(draft, new CorpusSettings(), false, new Random(seed));
        var budget = BudgetAllocator.AllocateRelevant(ctx.DatedFacts, relevant, null, ctx.Random);
        return ThreadSkeletonBuilder.BuildRelevant(ctx, budget);
    }

    private static DateTimeOffset Phoenix(int year, int month, int day, int hour, int minute) =>
        new(year, month, day, hour, minute, 0, TimeSpan.FromHours(-7));

    /// <summary>
    /// Hand-built T-0003 for F-003: msg 1 New CH-004 -> CH-002, msg 2 ReplyAll CH-002 -> CH-004; CH-003 with a Word attachment.
    /// </summary>
    private static PlannedThread SampleThread()
    {
        var thread = new PlannedThread
        {
            Id = "T-0003",
            Relevance = ThreadRelevance.Relevant,
            FactIds = new List<string> { "F-003" },
            IssueIds = new List<string> { "ISS-01" }
        };
        thread.Messages.Add(new PlannedMessage
        {
            Id = "T-0003-M01",
            Seq = 1,
            Kind = MessageKind.New,
            FromId = "CH-004",
            ToIds = new List<string> { "CH-002" },
            CcIds = new List<string> { "CH-001" },
            SentAt = Phoenix(2024, 3, 18, 15, 5),
            TimeZoneId = "America/Phoenix",
            FactIds = new List<string> { "F-003" },
            Basis = RelevanceBasis.Own
        });
        thread.Messages.Add(new PlannedMessage
        {
            Id = "T-0003-M02",
            Seq = 2,
            ReplyToSeq = 1,
            Kind = MessageKind.ReplyAll,
            FromId = "CH-002",
            ToIds = new List<string> { "CH-004", "CH-003" },
            SentAt = Phoenix(2024, 3, 19, 9, 42),
            TimeZoneId = "America/Phoenix",
            Basis = RelevanceBasis.Own,
            Attachments = new List<PlannedAttachment> { new() { Kind = AttachmentType.Word, FileName = "Document 1.docx" } }
        });
        return thread;
    }

    // ----- Task 1: DTOs, chunking, prompt -----

    [Fact]
    public void ThreadPlanBatchDto_IsStrict()
    {
        Assert.Equal(new[] { typeof(RelevantThreadFiller.ThreadPlanBatchDto) }, RelevantThreadFiller.SchemaTypes);
        foreach (var type in RelevantThreadFiller.SchemaTypes)
        {
            var schema = JsonSchemaRegistry.GetSchemaJson(type);
            var errors = StrictSchemaValidator.Validate(schema);
            Assert.True(errors.Count == 0, $"{type.Name}: {string.Join("; ", errors)}");
            Assert.True(StrictSchemaValidator.CountUnionTypes(schema) <= 16);
        }
    }

    [Fact]
    public void Dtos_HaveNoDatePersonCountOrLabelFields()
    {
        var names = new[] { typeof(RelevantThreadFiller.ThreadPlanDto), typeof(RelevantThreadFiller.MessagePlanDto) }
            .SelectMany(t => t.GetProperties())
            .Select(p => p.Name.ToLowerInvariant())
            .ToList();
        foreach (var forbidden in new[] { "date", "from", "to", "cc", "sender", "recipient", "count", "relevance", "hot", "privileged", "factid", "issueid" })
            Assert.DoesNotContain(names, n => n.Contains(forbidden) && n != "mustconvey");
    }

    [Fact]
    public void BuildChunks_RespectsLimits_InTimeOrder_EachThreadOnce()
    {
        var threads = Skeleton(CorpusTestData.RichDraft(), 180);
        Assert.InRange(threads.Count, 40, 90);

        var chunks = RelevantThreadFiller.BuildChunks(threads);

        Assert.True(chunks.Count >= 3);
        foreach (var chunk in chunks)
        {
            Assert.InRange(chunk.Count, 1, RelevantThreadFiller.MaxThreadsPerChunk);
            Assert.True(chunk.Sum(t => t.Messages.Count) <= RelevantThreadFiller.MaxMessagesPerChunk);
        }

        var flat = chunks.SelectMany(c => c).ToList();
        Assert.Equal(threads.Count, flat.Count);
        Assert.Equal(threads.Select(t => t.Id).OrderBy(i => i), flat.Select(t => t.Id).OrderBy(i => i));
        var starts = flat.Select(t => t.Messages[0].SentAt.UtcDateTime).ToList();
        Assert.Equal(starts.OrderBy(s => s), starts);
    }

    [Fact]
    public void BuildChunks_WindowSpansIssues()
    {
        var draft = CorpusTestData.RichDraft();
        var threads = Skeleton(draft, 180);
        var chunks = RelevantThreadFiller.BuildChunks(threads);

        // A chunk in March-May 2024 plans ISS-01-only threads together with threads touching ISS-02
        var mixed = chunks.FirstOrDefault(c =>
            c.Any(t => !t.IssueIds.Contains("ISS-02")) &&
            c.Any(t => t.IssueIds.Contains("ISS-02")) &&
            c.Any(t => t.Messages[0].SentAt.Month is >= 3 and <= 5 && t.Messages[0].SentAt.Year == 2024));
        Assert.NotNull(mixed);

        var prompt = RelevantThreadFiller.BuildChunkPrompt(draft, mixed!, new List<PlannedThread>(), string.Empty);
        Assert.Contains("issues: ISS-01 |", prompt);
        Assert.Contains("ISS-02", prompt);
    }

    [Fact]
    public void BuildChunkPrompt_ExactLineFormats()
    {
        var draft = CorpusTestData.RichDraft();
        var thread = SampleThread();

        var prompt = RelevantThreadFiller.BuildChunkPrompt(draft, new[] { thread }, new List<PlannedThread>(), string.Empty);

        Assert.Contains("Thread T-0003 | facts: F-003 Harmon approves Change Order 7 | issues: ISS-01 | privileged: no", prompt);
        Assert.Contains("T-0003 msg 2 | ReplyAll | from CH-002 Tom Becker to CH-004 Lisa Chen; CH-003 Raymond Harmon | cc - | 2024-03-19 09:42 America/Phoenix | carries the facts: yes | attachments: Word", prompt);
        Assert.Contains("T-0003 msg 1 | New | from CH-004 Lisa Chen to CH-002 Tom Becker | cc CH-001 Maria Delgado | 2024-03-18 15:05 America/Phoenix | carries the facts: yes | attachments: none", prompt);
        Assert.DoesNotContain("Guidance from the user", prompt);
    }

    [Fact]
    public void BuildChunkPrompt_CarriesGuideContext()
    {
        var draft = CorpusTestData.RichDraft();
        var thread = SampleThread();

        var prompt = RelevantThreadFiller.BuildChunkPrompt(draft, new[] { thread }, new List<PlannedThread>(), string.Empty);

        // Name rules + synopsis + cast for participants + values
        Assert.Contains("Keep every name the document uses exactly as written", prompt);
        Assert.Contains("ISS-01 ", prompt);
        Assert.Contains("CH-002 Tom Becker, Project Manager", prompt);
        Assert.Contains("CH-003 Raymond Harmon, Chief Executive Officer", prompt);
        Assert.DoesNotContain("CH-008 Priya Nair", prompt);
        Assert.Contains("Change Order 7 amount = $184,250.00", prompt);

        // Facts in the window with id/date/title/description, and up to 3 earlier facts
        Assert.Contains("F-003 | 2024-03-18 | Harmon approves Change Order 7 | Harmon approved Change Order 7 in writing", prompt);
        Assert.Contains("Already happened", prompt);
        Assert.Contains("F-001 | 2024-01-15 | Subcontract signed for Lakeview Tower", prompt);
        Assert.Contains("F-002 | 2024-02-12 | Harmon confirms the change order procedure", prompt);
        Assert.DoesNotContain("F-006 |", prompt);

        Assert.Contains("The dates, people, order and number of messages are fixed.", prompt);
        Assert.Contains("carries the facts: no' only react to or pass on what was already said", prompt);
    }

    [Fact]
    public void BuildChunkPrompt_EarlierThreadsAndGuidance()
    {
        var draft = CorpusTestData.RichDraft();
        var earlier = new PlannedThread { Id = "T-0001", FactIds = new List<string> { "F-001" }, IssueIds = new List<string> { "ISS-01" } };
        earlier.Messages.Add(new PlannedMessage { Seq = 1, FromId = "CH-001", SentAt = Phoenix(2024, 1, 16, 10, 0), TimeZoneId = "America/Phoenix" });

        var prompt = RelevantThreadFiller.BuildChunkPrompt(draft, new[] { SampleThread() }, new[] { earlier }, "more traffic around the March change-order dispute");

        Assert.Contains("T-0001 | facts: Subcontract signed for Lakeview Tower", prompt);
        Assert.Contains("Guidance from the user: more traffic around the March change-order dispute", prompt);
        Assert.DoesNotContain("T-0001 msg", prompt);
    }

    [Fact]
    public void BuildChunkPrompt_ThreadSeqsRecoverable()
    {
        var draft = CorpusTestData.RichDraft();
        var chunks = RelevantThreadFiller.BuildChunks(Skeleton(draft, 180));
        var earlier = new List<PlannedThread>();

        foreach (var chunk in chunks)
        {
            var prompt = RelevantThreadFiller.BuildChunkPrompt(draft, chunk, earlier, string.Empty);
            var parsed = CorpusTestData.ThreadSeqsIn(prompt);
            Assert.Equal(chunk.Select(t => t.Id), parsed.Select(p => p.ThreadId));
            for (var i = 0; i < chunk.Count; i++)
                Assert.Equal(chunk[i].Messages.Select(m => m.Seq), parsed[i].Seqs);
            earlier.AddRange(chunk);
        }
    }

    [Fact]
    public void BuildChunkPrompt_NoFictionalOrJsonInstructions()
    {
        var draft = CorpusTestData.RichDraft();
        var prompt = RelevantThreadFiller.BuildChunkPrompt(draft, new[] { SampleThread() }, new List<PlannedThread>(), "guidance");

        Assert.DoesNotContain("fictional", CorpusPlanPrompts.SystemPrompt, StringComparison.OrdinalIgnoreCase);
        CaseTestData.AssertNoJsonInstructions(new LlmProviderRequest { SystemPrompt = CorpusPlanPrompts.SystemPrompt, UserPrompt = prompt });
        Assert.DoesNotContain("JSON", prompt);
        // Applied substitutions are restated (use them exactly); the prompt never asks the model to invent names
        Assert.DoesNotContain("invented names fictional", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("invent", prompt.Replace("never invent", string.Empty), StringComparison.OrdinalIgnoreCase);
    }
}
