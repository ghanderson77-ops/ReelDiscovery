using System.Text.Json;
using System.Text.Json.Serialization;
using ReelDiscovery.Models;
using ReelDiscovery.Models.Case;
using ReelDiscovery.Models.Corpus;
using ReelDiscovery.Models.Evidence;
using ReelDiscovery.Services.Ai;
using ReelDiscovery.Services.Evidence;
using ReelDiscovery.Tests.Corpus;

namespace ReelDiscovery.Tests.Evidence;

public class EvidencePromptsTests
{
    private const string HotSuffix = "This is a key document: state the key point plainly.";

    private static ThreadRenderContext Context(CaseDraft draft, CorpusPlan plan, PlannedThread thread,
        IReadOnlyList<LedgerEntry>? ledger = null, ThreadAssignment? assignment = null, int waveIndex = 2)
    {
        var people = PeopleDirectory.Create(draft.Guide!, plan);
        assignment ??= RenderAssignments.Compute(plan)[thread.Id];
        return ThreadRenderContext.Create(draft, plan, thread, assignment, people, ledger ?? new List<LedgerEntry>(), waveIndex);
    }

    private static (CaseDraft Draft, CorpusPlan Plan, PlannedThread Thread) HotCase()
    {
        var (draft, plan) = EvidenceTestData.ApprovedCase();
        return (draft, plan, PlanFixtures.Thread(plan, "F-003"));
    }

    private static List<string> MessageLines(string prompt) =>
        prompt.Split('\n').Select(l => l.TrimEnd('\r')).Where(l => l.StartsWith("msg ", StringComparison.Ordinal)).ToList();

    [Fact]
    public void Create_GatesKnowledgeAndSelectsLedger()
    {
        var (draft, plan, thread) = HotCase();

        var ctx = Context(draft, plan, thread);

        Assert.Same(thread, ctx.Thread);
        Assert.Same(draft.Guide, ctx.Guide);
        Assert.Equal(2, ctx.WaveIndex);
        Assert.Contains("CH-004", ctx.Knowledge.Participants);
        Assert.Contains(ctx.Knowledge.Facts, f => f.Id == "F-003");
        Assert.Empty(ctx.LedgerView);
    }

    [Fact]
    public void BuildThreadPrompt_ListsThreadMessagesAndVoices()
    {
        var (draft, plan, thread) = HotCase();
        var ctx = Context(draft, plan, thread);

        var prompt = EvidencePrompts.BuildThreadPrompt(ctx);

        Assert.Contains($"Thread id: {thread.Id}", prompt);
        var lines = MessageLines(prompt);
        Assert.Equal(thread.Messages.OrderBy(m => m.Seq).Select(m => $"msg {m.Seq} |").ToList(),
            lines.Select(l => l[..(l.IndexOf('|') + 1)]).ToList());
        foreach (var line in lines)
        {
            Assert.Contains("subtlety:", line);
            Assert.Contains("length:", line);
            Assert.Contains("may mention:", line);
        }

        foreach (var id in ctx.Knowledge.Participants)
        {
            var member = draft.Guide!.FindCast(id);
            if (member != null && member.VoiceNote.Length > 0)
                Assert.Contains(member.VoiceNote, prompt);
        }

        Assert.Contains("People in this thread:", prompt);
        Assert.Contains("Lisa Chen", prompt);
        Assert.Contains("Harmon Builders, Inc. (harmon.com)", prompt);
    }

    [Fact]
    public void BuildThreadPrompt_NeverLeaksCriteriaComplaintFutureFactsOrUnknowableValues()
    {
        var (draft, plan, thread) = HotCase();
        var ctx = Context(draft, plan, thread);

        var prompt = EvidencePrompts.BuildThreadPrompt(ctx);

        foreach (var issue in draft.Guide!.Issues)
            Assert.DoesNotContain(issue.RelevanceCriteria, prompt);

        var paragraphs = (draft.CoreDocument?.Sections ?? new List<CoreDocumentSection>())
            .SelectMany(s => s.Paragraphs)
            .Where(p => p.Length > 40)
            .ToList();
        Assert.NotEmpty(paragraphs);
        foreach (var paragraph in paragraphs)
            Assert.DoesNotContain(paragraph, prompt);

        foreach (var fact in draft.Guide.Facts.Where(f => string.CompareOrdinal(f.Date, ctx.Knowledge.LastDate.ToString("yyyy-MM-dd")) > 0))
            Assert.DoesNotContain(fact.Title, prompt);

        // INV-2024-118 becomes knowable only with F-005 (2024-05-10)
        Assert.DoesNotContain("INV-2024-118", prompt);
        Assert.Contains("$184,250.00", prompt);
    }

    [Fact]
    public void BuildThreadPrompt_HotSuffixOnHotMessagesOnly()
    {
        var (draft, plan, thread) = HotCase();
        var ctx = Context(draft, plan, thread);

        var lines = MessageLines(EvidencePrompts.BuildThreadPrompt(ctx));
        var ordered = thread.Messages.OrderBy(m => m.Seq).ToList();

        Assert.Contains(ordered, m => m.IsHot);
        Assert.Contains(ordered, m => !m.IsHot);
        for (var i = 0; i < ordered.Count; i++)
        {
            if (ordered[i].IsHot)
                Assert.Contains(HotSuffix, lines[i]);
            else
                Assert.DoesNotContain(HotSuffix, lines[i]);
        }
    }

    [Fact]
    public void BuildThreadPrompt_HotSuffixDoesNotDependOnSubtlety_PrivilegedThread()
    {
        var (draft, plan) = EvidenceTestData.ApprovedCase();
        var thread = PlanFixtures.Thread(plan, "F-001");
        Assert.True(thread.IsPrivileged);
        var root = thread.Messages.OrderBy(m => m.Seq).First();
        root.IsHot = true;
        foreach (var message in thread.Messages.Where(m => m != root))
            message.IsHot = false;

        var assignment = new ThreadAssignment { ThreadId = thread.Id, Marking = PrivilegeMarking.Banner };
        foreach (var message in thread.Messages.OrderBy(m => m.Seq))
            assignment.Messages.Add(new MessageAssignment { MessageId = message.Id, Seq = message.Seq, Subtlety = Subtlety.Counsel, Length = LengthBand.Medium });

        var prompt = EvidencePrompts.BuildThreadPrompt(Context(draft, plan, thread, assignment: assignment));
        var lines = MessageLines(prompt);

        Assert.Contains("legal advice to or from counsel in plain words", lines[0]);
        Assert.Contains(HotSuffix, lines[0]);
        Assert.DoesNotContain(HotSuffix, lines[1]);
        Assert.Contains("This is a confidential exchange with counsel", prompt);
    }

    [Fact]
    public void BuildThreadPrompt_NonPrivilegedThread_HasNoCounselInstruction_QuotedSaysNoNewFacts()
    {
        var (draft, plan, thread) = HotCase();
        var prompt = EvidencePrompts.BuildThreadPrompt(Context(draft, plan, thread));

        Assert.DoesNotContain("This is a confidential exchange with counsel", prompt);
        var quoted = thread.Messages.Single(m => m.Seq == 2);
        Assert.Equal(RelevanceBasis.Quoted, quoted.Basis);
        Assert.Contains("add no new facts", MessageLines(prompt)[1]);
    }

    [Fact]
    public void SystemPrompt_KeepsNamesAndScopesLegalTerms()
    {
        var system = EvidencePrompts.SystemPrompt;

        Assert.Contains("including characters and companies from films, TV series, books or games", system);
        Assert.Contains("Except where a thread is marked as an exchange with counsel", system);
        Assert.DoesNotContain("must be fictional", system, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("fictional names", system, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("fictional name", system, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildThreadPrompt_ShowsSelectedLedgerEntriesOnly()
    {
        var (draft, plan, thread) = HotCase();
        var ledger = new List<LedgerEntry>
        {
            new()
            {
                Id = "T-0900-L01", Date = "2024-02-20", Kind = LedgerKind.Meeting, About = "glazing coordination meeting",
                Statement = "The team set a coordination meeting for the upper floors.", Value = "2024-02-27",
                KnownByIds = new() { "CH-004" }, WaveIndex = 1
            },
            new()
            {
                // Same wave: not visible
                Id = "T-0901-L01", Date = "2024-02-21", Kind = LedgerKind.Figure, About = "crane rental quote",
                Statement = "The crane rental was quoted for the week.", KnownByIds = new() { "CH-004" }, WaveIndex = 2
            },
            new()
            {
                // Nobody on the thread saw it
                Id = "T-0902-L01", Date = "2024-02-22", Kind = LedgerKind.Decision, About = "parking lot resurfacing",
                Statement = "Accounting approved the resurfacing.", KnownByIds = new() { "CH-008" }, WaveIndex = 1
            }
        };

        var prompt = EvidencePrompts.BuildThreadPrompt(Context(draft, plan, thread, ledger));

        Assert.Contains("Earlier details from previous emails", prompt);
        Assert.Contains("glazing coordination meeting", prompt);
        Assert.Contains("[2024-02-27]", prompt);
        Assert.DoesNotContain("crane rental quote", prompt);
        Assert.DoesNotContain("parking lot resurfacing", prompt);
    }

    [Fact]
    public void BuildThreadPrompt_FeedbackBlockAppendedAtEnd()
    {
        var (draft, plan, thread) = HotCase();
        var ctx = Context(draft, plan, thread);
        var feedback = new List<RenderViolation>
        {
            new() { Code = "V2", Seq = 2, Text = "names \"X\", who is not in the cast" },
            new() { Code = "V1", Seq = 0, Text = "is missing message 4" }
        };

        var first = EvidencePrompts.BuildThreadPrompt(ctx);
        var second = EvidencePrompts.BuildThreadPrompt(ctx, feedback);

        Assert.DoesNotContain("Problems in the previous attempt", first);
        Assert.StartsWith(first, second, StringComparison.Ordinal);
        var tail = second[first.Length..];
        Assert.Contains("Problems in the previous attempt (fix all of them and rewrite the whole thread):", tail);
        Assert.Contains("- msg 2: names \"X\", who is not in the cast", tail);
        Assert.Contains("- thread: is missing message 4", tail);
        Assert.Equal(first, EvidencePrompts.BuildThreadPrompt(ctx, new List<RenderViolation>()));
    }

    [Fact]
    public void BuildThreadPrompt_ListsAttachmentTitles()
    {
        var (draft, plan, thread) = HotCase();
        var root = thread.Messages.OrderBy(m => m.Seq).First();
        root.Attachments.Clear();
        root.Attachments.Add(new PlannedAttachment { Kind = AttachmentType.Excel, Title = "Change Order 7 pricing", FileName = "Change Order 7 pricing.xlsx" });

        var lines = MessageLines(EvidencePrompts.BuildThreadPrompt(Context(draft, plan, thread)));

        Assert.Contains("attachments: \"Change Order 7 pricing\"", lines[0]);
    }

    [Fact]
    public void BuildRequest_IsStoryTierWithStablePrefixAndAttemptLabel()
    {
        var (draft, plan, thread) = HotCase();
        var ctx = Context(draft, plan, thread);
        var feedback = new List<RenderViolation> { new() { Code = "V2", Seq = 2, Text = "names \"X\", who is not in the cast" } };

        var first = ThreadRenderer.BuildRequest(ctx, 1, null);
        var second = ThreadRenderer.BuildRequest(ctx, 2, feedback);

        Assert.Equal(LlmTier.Story, first.Tier);
        Assert.Equal(12_000, first.MaxOutputTokens);
        Assert.Equal(ThreadRenderer.MaxOutputTokens, second.MaxOutputTokens);
        Assert.Equal(EvidencePrompts.SystemPrompt, first.SystemPrompt);
        Assert.Equal(EvidencePrompts.SystemPrompt, second.SystemPrompt);
        Assert.Equal($"Generate Corpus - {thread.Id}", first.OperationName);
        Assert.Equal($"Generate Corpus - {thread.Id} attempt 2", second.OperationName);
        Assert.StartsWith(first.UserPrompt, second.UserPrompt, StringComparison.Ordinal);
        Assert.EndsWith("- msg 2: names \"X\", who is not in the cast" + Environment.NewLine, second.UserPrompt);
    }

    [Fact]
    public void RenderResponder_AnswersByThreadIdWithinLengthBands()
    {
        var (draft, plan, thread) = HotCase();
        var ctx = Context(draft, plan, thread);
        var responder = EvidenceTestData.RenderResponder(plan, draft.Guide!);
        var request = ThreadRenderer.BuildRequest(ctx, 1, null);

        var response = responder(new LlmProviderRequest { SystemPrompt = request.SystemPrompt, UserPrompt = request.UserPrompt });

        Assert.Equal(LlmOutcome.Succeeded, response.Outcome);
        var dto = JsonSerializer.Deserialize<ThreadRenderer.ThreadRenderDto>(response.Text,
            new JsonSerializerOptions { Converters = { new JsonStringEnumConverter() } })!;
        Assert.Equal(thread.Messages.OrderBy(m => m.Seq).Select(m => m.Seq), dto.Messages.Select(m => m.Seq));

        var assignment = RenderAssignments.Compute(plan)[thread.Id];
        foreach (var message in dto.Messages)
        {
            var spec = RenderAssignments.Spec(assignment.For(message.Seq)!.Length);
            var words = message.Body.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;
            Assert.InRange(words, spec.MinWords, spec.MaxWords);
            Assert.Equal("Thanks,", message.SignOff);
            Assert.Empty(message.PeopleNamed);
        }

        var detail = Assert.Single(dto.NewDetails);
        var n = int.Parse(thread.Id[2..]).ToString();
        Assert.Equal(1, detail.Seq);
        Assert.Equal(LedgerKind.Commitment, detail.Kind);
        Assert.Equal($"site walk {n}", detail.About);
        Assert.Equal($"The team agreed to a site walk {n}.", detail.Statement);
        Assert.Equal(string.Empty, detail.Value);
    }

    [Fact]
    public void RenderResponder_OverrideReceivesAttemptAndUnknownThreadFails()
    {
        var (draft, plan, thread) = HotCase();
        var ctx = Context(draft, plan, thread);
        var attempts = new List<int>();
        var responder = EvidenceTestData.RenderResponder(plan, draft.Guide!, (id, attempt) =>
        {
            attempts.Add(attempt);
            return attempt == 1 ? "{\"messages\":[],\"newDetails\":[]}" : null;
        });
        var prompt = ThreadRenderer.BuildRequest(ctx, 1, null).UserPrompt;

        var first = responder(new LlmProviderRequest { UserPrompt = prompt });
        var second = responder(new LlmProviderRequest { UserPrompt = prompt });
        var unknown = responder(new LlmProviderRequest { UserPrompt = "Thread id: T-9999" });

        Assert.Equal("{\"messages\":[],\"newDetails\":[]}", first.Text);
        Assert.Contains("site walk", second.Text);
        Assert.Equal(new[] { 1, 2 }, attempts);
        Assert.Equal(LlmOutcome.Failed, unknown.Outcome);
    }
}
