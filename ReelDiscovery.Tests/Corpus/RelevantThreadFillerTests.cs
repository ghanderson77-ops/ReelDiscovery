using System.Text.Json;
using System.Text.RegularExpressions;
using ReelDiscovery.Helpers;
using ReelDiscovery.Models;
using ReelDiscovery.Models.Case;
using ReelDiscovery.Models.Corpus;
using ReelDiscovery.Services.Ai;
using ReelDiscovery.Services.Corpus;
using ReelDiscovery.Tests.Case;
using ReelDiscovery.Tests.Fakes;

namespace ReelDiscovery.Tests.Corpus;

public partial class RelevantThreadFillerTests
{
    private const string Guidance = "more traffic around the March change-order dispute";

    // "T-0003 msg 2 | ... | attachments: Word, Excel"
    [GeneratedRegex(@"^\s*(T-\d{4}) msg (\d+) \|.*\| attachments: (.+?)\s*$", RegexOptions.Multiline)]
    private static partial Regex MessageLinePattern();

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
    }

    // ----- Task 2: FillAsync -----

    private sealed record EchoMessage(int Seq, int Attachments);

    /// <summary>
    /// Thread ids with their messages (seq + attachment count) parsed from a chunk prompt, in order.
    /// </summary>
    private static List<(string ThreadId, List<EchoMessage> Messages)> Parse(string prompt)
    {
        var result = new List<(string ThreadId, List<EchoMessage> Messages)>();
        foreach (Match match in MessageLinePattern().Matches(prompt))
        {
            var id = match.Groups[1].Value;
            var seq = int.Parse(match.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture);
            var kinds = match.Groups[3].Value.Trim();
            var count = kinds == "none" ? 0 : kinds.Split(',').Length;
            var index = result.FindIndex(r => r.ThreadId == id);
            if (index < 0)
                result.Add((id, new List<EchoMessage> { new(seq, count) }));
            else
                result[index].Messages.Add(new EchoMessage(seq, count));
        }

        return result;
    }

    private static object EchoThread(string id, IEnumerable<EchoMessage> messages) => new
    {
        threadId = id,
        subject = $"Subject for {id}",
        purpose = $"Purpose of {id}.",
        rationale = $"Rationale for {id}.",
        linkedThreadIds = Array.Empty<string>(),
        messages = messages.Select(m => new
        {
            seq = m.Seq,
            intent = $"Intent {id} {m.Seq}",
            mustConvey = new[] { "Point A", "Point B" },
            attachmentTitles = Enumerable.Range(1, m.Attachments).Select(k => $"Title {id} m{m.Seq} a{k}").ToArray()
        }).ToArray()
    };

    /// <summary>
    /// Answers every thread and message listed in the prompt, except the ids in <paramref name="omit"/>.
    /// </summary>
    private static LlmProviderResponse Echo(LlmProviderRequest request, params string[] omit)
    {
        var threads = Parse(request.UserPrompt)
            .Where(t => !omit.Contains(t.ThreadId))
            .Select(t => EchoThread(t.ThreadId, t.Messages))
            .ToArray();
        return FakeLlmProvider.Ok(JsonSerializer.Serialize(new { threads }));
    }

    private static (RecordingGateway Gateway, FakeLlmProvider Fake) Gateway()
    {
        var (gateway, fake) = CorpusTestData.CreateGateway();
        return (new RecordingGateway(gateway), fake);
    }

    private static PlannedThread SecondThread()
    {
        var thread = new PlannedThread
        {
            Id = "T-0002",
            Relevance = ThreadRelevance.Relevant,
            FactIds = new List<string> { "F-002" },
            IssueIds = new List<string> { "ISS-01" }
        };
        thread.Messages.Add(new PlannedMessage
        {
            Id = "T-0002-M01",
            Seq = 1,
            Kind = MessageKind.New,
            FromId = "CH-004",
            ToIds = new List<string> { "CH-002" },
            SentAt = Phoenix(2024, 2, 12, 10, 15),
            TimeZoneId = "America/Phoenix",
            FactIds = new List<string> { "F-002" },
            Basis = RelevanceBasis.Own
        });
        return thread;
    }

    private static PlannedThread ThirdThread()
    {
        var thread = SecondThread();
        thread.Id = "T-0004";
        thread.FactIds = new List<string> { "F-004" };
        thread.Messages[0].Id = "T-0004-M01";
        thread.Messages[0].FactIds = new List<string> { "F-004" };
        thread.Messages[0].SentAt = Phoenix(2024, 4, 22, 11, 0);
        return thread;
    }

    private static string StructureJson(IEnumerable<PlannedThread> threads) => JsonSerializer.Serialize(threads.Select(t => new
    {
        t.Id,
        t.FactIds,
        t.IssueIds,
        t.IsPrivileged,
        t.Relevance,
        Messages = t.Messages.Select(m => new { m.Seq, m.Kind, m.FromId, m.ToIds, m.CcIds, m.SentAt, m.FactIds, m.IsHot, m.Basis, Kinds = m.Attachments.Select(a => a.Kind) })
    }));

    private static string Extension(AttachmentType kind) => kind switch
    {
        AttachmentType.Word => ".docx",
        AttachmentType.Excel => ".xlsx",
        AttachmentType.PowerPoint => ".pptx",
        AttachmentType.Image => ".png",
        AttachmentType.CalendarInvite => ".ics",
        AttachmentType.Voicemail => ".mp3",
        _ => ".bin"
    };

    [Fact]
    public async Task FillAsync_HappyPath_FillsEveryThread_AndSendsStoryRequests()
    {
        var draft = CorpusTestData.RichDraft();
        var threads = Skeleton(draft, 180);
        var chunkCount = RelevantThreadFiller.BuildChunks(threads).Count;
        var (gateway, fake) = Gateway();
        fake.Respond = r => Echo(r);

        var result = await new RelevantThreadFiller(gateway).FillAsync(draft, threads, string.Empty);

        Assert.False(result.IsFatal);
        Assert.Equal(chunkCount, result.TotalChunks);
        Assert.Equal(0, result.RefusedChunks);
        Assert.Equal(0, result.FailedChunks);
        Assert.Empty(result.Notes);
        foreach (var thread in threads)
        {
            Assert.Equal($"Subject for {thread.Id}", thread.Subject);
            Assert.Equal($"Purpose of {thread.Id}.", thread.Purpose);
            Assert.Equal($"Rationale for {thread.Id}.", thread.Rationale);
            Assert.False(thread.UsesFallbackContent);
            foreach (var message in thread.Messages)
            {
                Assert.Equal($"Intent {thread.Id} {message.Seq}", message.Intent);
                Assert.Equal(new[] { "Point A", "Point B" }, message.MustConvey);
                for (var k = 0; k < message.Attachments.Count; k++)
                {
                    var attachment = message.Attachments[k];
                    var title = $"Title {thread.Id} m{message.Seq} a{k + 1}";
                    Assert.Equal(title, attachment.Title);
                    Assert.Equal(FileNameHelper.SanitizeForFileName(title) + Extension(attachment.Kind), attachment.FileName);
                }
            }
        }

        Assert.Equal(chunkCount, gateway.Requests.Count);
        var names = gateway.Requests.Select(r => r.OperationName).OrderBy(n => n, StringComparer.Ordinal).ToList();
        var expected = Enumerable.Range(1, chunkCount).Select(i => $"Corpus Plan - Relevant threads (chunk {i} of {chunkCount})").OrderBy(n => n, StringComparer.Ordinal).ToList();
        Assert.Equal(expected, names);
        foreach (var request in gateway.Requests)
        {
            Assert.Equal(LlmTier.Story, request.Tier);
            Assert.Equal(16_000, request.MaxOutputTokens);
            Assert.Equal(CorpusPlanPrompts.SystemPrompt, request.SystemPrompt);
        }

        foreach (var request in fake.Requests)
            CaseTestData.AssertNoJsonInstructions(request);
    }

    [Fact]
    public async Task FillAsync_ChangesNoStructuralField()
    {
        var draft = CorpusTestData.RichDraft();
        var threads = Skeleton(draft, 180);
        var before = StructureJson(threads);
        var (gateway, fake) = Gateway();
        fake.Respond = r => Echo(r);

        await new RelevantThreadFiller(gateway).FillAsync(draft, threads, string.Empty);

        Assert.Equal(before, StructureJson(threads));
    }

    [Fact]
    public async Task FillAsync_MapsIdsStrictly_AndStripsUnknownTokens()
    {
        var draft = CorpusTestData.RichDraft();
        var threads = new List<PlannedThread> { SecondThread(), SampleThread() };
        var (gateway, fake) = Gateway();
        var response = new
        {
            threads = new object[]
            {
                new
                {
                    threadId = " t-0003 ",
                    subject = "Change Order 7 approved CH-999",
                    purpose = "Lisa Chen (CH-004) confirms F-077 and F-003.",
                    rationale = "Shows the approval under ISS-01.",
                    linkedThreadIds = new[] { "t-0002", "T-0003", "T-9999" },
                    messages = new object[]
                    {
                        new { seq = 1, intent = "Lisa approves the change.", mustConvey = new[] { "CO-007 is approved", "", "Amount $184,250.00", "Extra", "Fourth" }, attachmentTitles = Array.Empty<string>() },
                        new { seq = 2, intent = "Tom confirms.", mustConvey = new[] { "Thanks" }, attachmentTitles = new[] { "../CO 7: Approval?" } },
                        new { seq = 99, intent = "Invented.", mustConvey = new[] { "x" }, attachmentTitles = Array.Empty<string>() }
                    }
                },
                EchoThread("T-0002", new[] { new EchoMessage(1, 0) }),
                EchoThread("T-9999", new[] { new EchoMessage(1, 0) })
            }
        };
        fake.Respond = _ => FakeLlmProvider.Ok(JsonSerializer.Serialize(response));

        var result = await new RelevantThreadFiller(gateway).FillAsync(draft, threads, string.Empty);

        var t3 = threads[1];
        Assert.Equal("Change Order 7 approved", t3.Subject);
        Assert.Equal("Lisa Chen (CH-004) confirms and F-003.", t3.Purpose);
        Assert.Equal("Shows the approval under ISS-01.", t3.Rationale);
        Assert.Equal(new[] { "T-0002" }, t3.LinkedThreadIds);
        Assert.Equal(new[] { "CO-007 is approved", "Amount $184,250.00", "Extra" }, t3.Messages[0].MustConvey);
        Assert.Equal("Tom confirms.", t3.Messages[1].Intent);
        Assert.Equal("CO_7_Approval.docx", t3.Messages[1].Attachments[0].FileName);
        Assert.Equal(2, t3.Messages.Count);
        Assert.False(t3.UsesFallbackContent);
        Assert.Equal("Subject for T-0002", threads[0].Subject);
        Assert.Single(fake.Requests);
        Assert.Empty(result.Notes);
    }

    [Fact]
    public async Task FillAsync_AttachmentFileName_IsSanitizedAndCapped()
    {
        var draft = CorpusTestData.RichDraft();
        var threads = new List<PlannedThread> { SampleThread() };
        var (gateway, fake) = Gateway();
        var longTitle = string.Concat(Enumerable.Repeat("Change Order Seven Backup ", 8)) + @"C:\Windows\..\x";
        var response = new
        {
            threads = new object[]
            {
                new
                {
                    threadId = "T-0003", subject = "S", purpose = "P.", rationale = "R.", linkedThreadIds = Array.Empty<string>(),
                    messages = new object[]
                    {
                        new { seq = 1, intent = "I.", mustConvey = new[] { "A" }, attachmentTitles = Array.Empty<string>() },
                        new { seq = 2, intent = "I.", mustConvey = new[] { "A" }, attachmentTitles = new[] { longTitle } }
                    }
                }
            }
        };
        fake.Respond = _ => FakeLlmProvider.Ok(JsonSerializer.Serialize(response));

        await new RelevantThreadFiller(gateway).FillAsync(draft, threads, string.Empty);

        var fileName = threads[0].Messages[1].Attachments[0].FileName;
        Assert.EndsWith(".docx", fileName);
        Assert.True(fileName.Length <= 80 + ".docx".Length);
        Assert.DoesNotContain(@"\", fileName);
        Assert.DoesNotContain("/", fileName);
        Assert.DoesNotContain(":", fileName);
        Assert.Equal(longTitle.Trim(), threads[0].Messages[1].Attachments[0].Title);
    }

    [Fact]
    public async Task FillAsync_MissingThread_IsReaskedOnce()
    {
        var draft = CorpusTestData.RichDraft();
        var threads = new List<PlannedThread> { SecondThread(), SampleThread() };
        var (gateway, fake) = Gateway();
        var calls = 0;
        fake.Respond = r => Interlocked.Increment(ref calls) == 1 ? Echo(r, "T-0002") : Echo(r);

        var result = await new RelevantThreadFiller(gateway).FillAsync(draft, threads, string.Empty);

        Assert.Equal(2, fake.Requests.Count);
        var retry = CorpusTestData.ThreadSeqsIn(fake.Requests[1].UserPrompt);
        Assert.Equal(new[] { "T-0002" }, retry.Select(r => r.ThreadId));
        Assert.Equal("Corpus Plan - Relevant threads (chunk 1 of 1) retry", gateway.Requests[1].OperationName);
        Assert.Equal("Subject for T-0002", threads[0].Subject);
        Assert.Equal("Intent T-0002 1", threads[0].Messages[0].Intent);
        Assert.False(threads[0].UsesFallbackContent);
        Assert.Equal("Subject for T-0003", threads[1].Subject);
        Assert.Empty(result.Notes);
    }

    [Fact]
    public async Task FillAsync_MessageWithoutIntent_IsReasked()
    {
        var draft = CorpusTestData.RichDraft();
        var threads = new List<PlannedThread> { SampleThread() };
        var (gateway, fake) = Gateway();
        var calls = 0;
        fake.Respond = r =>
        {
            if (Interlocked.Increment(ref calls) > 1)
                return Echo(r);
            var partial = new { threads = new[] { EchoThread("T-0003", new[] { new EchoMessage(1, 0) }) } };
            return FakeLlmProvider.Ok(JsonSerializer.Serialize(partial));
        };

        await new RelevantThreadFiller(gateway).FillAsync(draft, threads, string.Empty);

        Assert.Equal(2, fake.Requests.Count);
        Assert.Equal("Intent T-0003 2", threads[0].Messages[1].Intent);
        Assert.Equal("Title T-0003 m2 a1", threads[0].Messages[1].Attachments[0].Title);
        Assert.False(threads[0].UsesFallbackContent);
    }

    [Fact]
    public async Task FillAsync_StillMissing_UsesTemplates_AndNotes()
    {
        var draft = CorpusTestData.RichDraft();
        var threads = new List<PlannedThread> { SecondThread(), SampleThread(), ThirdThread() };
        var (gateway, fake) = Gateway();
        fake.Respond = r => Echo(r, "T-0002", "T-0004");

        var result = await new RelevantThreadFiller(gateway).FillAsync(draft, threads, string.Empty);

        Assert.Equal(2, fake.Requests.Count);
        var guide = draft.Guide!;
        var fact = guide.Facts.Single(f => f.Id == "F-002");
        var issue = guide.Issues.Single(i => i.Id == "ISS-01");
        var t2 = threads[0];
        Assert.True(t2.UsesFallbackContent);
        Assert.Equal(fact.Title, t2.Subject);
        Assert.Equal($"Discuss {fact.Title}.", t2.Purpose);
        Assert.Equal($"Evidence of {fact.Title} (F-002) under {issue.Title} (ISS-01).", t2.Rationale);
        Assert.False(string.IsNullOrWhiteSpace(t2.Messages[0].Intent));
        Assert.Equal(new[] { fact.Title }, t2.Messages[0].MustConvey);
        Assert.False(threads[1].UsesFallbackContent);
        Assert.True(threads[2].UsesFallbackContent);
        Assert.Contains("2 relevant threads use placeholder subjects because the model did not plan them.", result.Notes);
    }

    [Fact]
    public void TemplateSubject_IsAtMostTenWords()
    {
        Assert.Equal("one two three four five six seven eight nine ten", RelevantThreadFiller.TemplateSubject("one two three four five six seven eight nine ten eleven twelve"));
        Assert.Equal("Short title", RelevantThreadFiller.TemplateSubject("  Short title "));
    }

    [Fact]
    public async Task FillAsync_RefusedChunk_IsCountedAndTemplated()
    {
        var draft = CorpusTestData.RichDraft();
        var threads = new List<PlannedThread> { SecondThread(), SampleThread() };
        var (gateway, fake) = Gateway();
        fake.Respond = _ => FakeLlmProvider.Refused("I can't help with that.");

        var result = await new RelevantThreadFiller(gateway).FillAsync(draft, threads, string.Empty);

        Assert.Equal(1, result.TotalChunks);
        Assert.Equal(1, result.RefusedChunks);
        Assert.Equal("I can't help with that.", result.RefusalMessage);
        Assert.False(result.IsFatal);
        Assert.Single(fake.Requests);
        Assert.All(threads, t => Assert.True(t.UsesFallbackContent));
        Assert.All(threads, t => Assert.False(string.IsNullOrWhiteSpace(t.Subject)));
    }

    [Fact]
    public async Task FillAsync_FailedChunk_IsReaskedThenTemplated()
    {
        var draft = CorpusTestData.RichDraft();
        var threads = new List<PlannedThread> { SampleThread() };
        var (gateway, fake) = Gateway();
        fake.Respond = _ => FakeLlmProvider.Failed(LlmFailureKind.Network, "connection reset");

        var result = await new RelevantThreadFiller(gateway).FillAsync(draft, threads, string.Empty);

        Assert.Equal(1, result.FailedChunks);
        Assert.False(result.IsFatal);
        Assert.True(threads[0].UsesFallbackContent);
    }

    [Fact]
    public async Task FillAsync_FatalError_StopsOtherChunks_AndReturns()
    {
        var draft = CorpusTestData.RichDraft();
        var threads = Skeleton(draft, 180);
        Assert.True(RelevantThreadFiller.BuildChunks(threads).Count >= 3);
        var (gateway, fake) = Gateway();
        fake.Respond = _ => FakeLlmProvider.Failed(LlmFailureKind.Auth, "Invalid API key");

        var result = await new RelevantThreadFiller(gateway, maxParallel: 1).FillAsync(draft, threads, string.Empty);

        Assert.True(result.IsFatal);
        Assert.Contains("Invalid API key", result.FatalError);
        Assert.Single(fake.Requests);
    }

    [Fact]
    public async Task FillAsync_UserCancel_Throws()
    {
        var draft = CorpusTestData.RichDraft();
        var threads = Skeleton(draft, 180);
        var (gateway, fake) = Gateway();
        using var cts = new CancellationTokenSource();
        fake.OnSend = _ => cts.Cancel();
        fake.Respond = r => Echo(r);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new RelevantThreadFiller(gateway).FillAsync(draft, threads, string.Empty, null, cts.Token));
    }

    [Fact]
    public async Task FillAsync_GuidanceVerbatimInEveryChunk()
    {
        var draft = CorpusTestData.RichDraft();
        var threads = Skeleton(draft, 180);
        var (gateway, fake) = Gateway();
        fake.Respond = r => Echo(r);

        await new RelevantThreadFiller(gateway).FillAsync(draft, threads, Guidance);

        Assert.True(fake.Requests.Count >= 3);
        Assert.All(fake.Requests, r => Assert.Contains($"Guidance from the user: {Guidance}", r.UserPrompt));
    }

    [Fact]
    public async Task FillAsync_ReportsPlanningRelevantProgress()
    {
        var draft = CorpusTestData.RichDraft();
        var threads = Skeleton(draft, 180);
        var n = RelevantThreadFiller.BuildChunks(threads).Count;
        var (gateway, fake) = Gateway();
        fake.Respond = r => Echo(r);
        var progress = new LockedProgress();

        await new RelevantThreadFiller(gateway).FillAsync(draft, threads, string.Empty, progress);

        var reports = progress.Snapshot();
        Assert.All(reports, r => Assert.Equal(PlanStage.PlanningRelevant, r.Stage));
        Assert.All(reports, r => Assert.Equal(n, r.StageTotal));
        Assert.Equal(Enumerable.Range(0, n), reports.Select(r => r.StageDone).OrderBy(d => d));
    }

    // Synchronous and thread-safe: onStart fires from parallel chunk tasks
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
