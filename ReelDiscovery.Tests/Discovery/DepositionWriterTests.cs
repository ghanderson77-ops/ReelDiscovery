using System.Text.Json;
using System.Text.Json.Serialization;
using ReelDiscovery.Models.Case;
using ReelDiscovery.Models.Discovery;
using ReelDiscovery.Services.Ai;
using ReelDiscovery.Services.Corpus;
using ReelDiscovery.Services.Discovery;
using ReelDiscovery.Tests.Case;
using ReelDiscovery.Tests.Fakes;

namespace ReelDiscovery.Tests.Discovery;

public class DepositionWriterTests
{
    private const string CaseNo = "2:24-cv-01234";
    private const string QuestionText = "Did you review that schedule with the project team on site?";
    private const string AnswerText = "Yes, I reviewed it with the team that week.";
    private const string LongAnswerText =
        "Yes, I reviewed it with the team that week, and we went through each line item together before anyone signed off on the revised schedule.";

    private static readonly JsonSerializerOptions JsonOptions = new() { Converters = { new JsonStringEnumConverter() } };

    // ---- helpers ----

    private sealed class Fixture
    {
        public CaseDraft Draft { get; init; } = new();
        public DepositionPlan Plan { get; init; } = new();
        public LlmGateway Gateway { get; init; } = null!;
        public FakeLlmProvider Fake { get; init; } = null!;
        public string SystemPrompt { get; init; } = string.Empty;

        // Attempt number (1-based) per chunk index, counted from the prompts the fake received
        public Dictionary<int, int> Attempts { get; } = new();

        public DepositionSpeaker Examining => Plan.Speakers.First(s => s.Role == SpeakerRole.Examining);
        public DepositionSpeaker? Defending => Plan.Speakers.FirstOrDefault(s => s.Role == SpeakerRole.Defending);
        public DepositionSpeaker Witness => Plan.Speakers.First(s => s.Role == SpeakerRole.Witness);

        // Answers each request by the chunk its prompt asks for (sequential writer, but routing by prompt keeps it robust)
        public void Script(Func<DepositionChunk, int, LlmProviderResponse> respond)
        {
            Fake.Respond = request =>
            {
                var chunk = ChunkFor(Plan, request.UserPrompt);
                Attempts[chunk.Index] = Attempts.TryGetValue(chunk.Index, out var n) ? n + 1 : 1;
                return respond(chunk, Attempts[chunk.Index]);
            };
        }
    }

    private static Fixture CreateFixture(int pages = 10, bool priced = false)
    {
        var draft = DiscoveryTestData.LitigationDraft();
        var plan = DepositionPlanner.Plan(draft, new[] { "CH-003" }, pages, null, null).Single();
        Assert.True(plan.Chunks.Count >= 2);

        // Two chunks keep the request arithmetic simple; re-index so prompts identify them uniquely
        plan.Chunks = plan.Chunks.Take(2).ToList();
        for (var i = 0; i < plan.Chunks.Count; i++)
            plan.Chunks[i].Index = i;

        var fake = new FakeLlmProvider(LlmProviderKind.Anthropic);
        var story = CaseTestData.StoryModel();
        if (priced)
        {
            // $1 per token in and out, so costs are easy to add up
            story.InputTokenPricePerMillion = 1_000_000m;
            story.OutputTokenPricePerMillion = 1_000_000m;
        }

        return new Fixture
        {
            Draft = draft,
            Plan = plan,
            Gateway = new LlmGateway(fake, story, CaseTestData.VolumeModel()),
            Fake = fake,
            SystemPrompt = DiscoveryPrompts.BuildSystemPrompt(draft)
        };
    }

    private static DepositionChunk ChunkFor(DepositionPlan plan, string prompt)
    {
        foreach (var chunk in plan.Chunks)
        {
            if (prompt.Contains("- Topic: " + chunk.TopicTitle + "\n", StringComparison.Ordinal)
                && prompt.Contains($"This is part {chunk.Part} of {chunk.PartCount} of this topic.", StringComparison.Ordinal))
                return chunk;
        }

        throw new InvalidOperationException("Prompt matches no chunk.");
    }

    private static DiscoveryPrompts.DepositionChunkDto CleanDto(Fixture f, DepositionChunk chunk, int? turns = null, string answer = AnswerText)
    {
        var count = turns ?? chunk.TargetTurns;
        var dto = new DiscoveryPrompts.DepositionChunkDto();
        foreach (var speaker in f.Plan.Speakers.Where(s => s.Role is not (SpeakerRole.Reporter or SpeakerRole.Videographer)))
        {
            dto.Speakers.Add(new DiscoveryPrompts.SpeakerDto
            {
                SpeakerId = speaker.SpeakerId,
                Honorific = speaker.Role == SpeakerRole.Witness ? DiscoveryPrompts.HonorificKind.DR : DiscoveryPrompts.HonorificKind.MS
            });
        }

        for (var i = 0; i < count; i++)
        {
            var question = i % 2 == 0;
            var exhibitSlot = i / 2;
            dto.Turns.Add(new DiscoveryPrompts.TurnDto
            {
                Kind = question ? TurnKind.Q : TurnKind.A,
                SpeakerId = question ? f.Examining.SpeakerId : f.Witness.SpeakerId,
                Text = question ? QuestionText : answer,
                ExhibitNumber = question && exhibitSlot < chunk.ExhibitNumbers.Count ? chunk.ExhibitNumbers[exhibitSlot] : 0
            });
        }

        return dto;
    }

    private static LlmProviderResponse Ok(DiscoveryPrompts.DepositionChunkDto dto) =>
        FakeLlmProvider.Ok(JsonSerializer.Serialize(dto, JsonOptions));

    private static Task<DepositionWriteOutcome> Write(Fixture f, PlanCallRun run) =>
        new DepositionWriter(f.Gateway).WriteAsync(f.Draft, f.Plan, CaseNo, f.SystemPrompt, string.Empty, run);

    private static PlanCallRun NewRun() => new(CancellationToken.None, 1, "Click Retry.");

    // ---- tests ----

    [Fact]
    public async Task TwoChunks_CleanFirstAttempt_AssemblesContentInOrder()
    {
        var f = CreateFixture();
        f.Script((chunk, _) => Ok(CleanDto(f, chunk)));
        using var run = NewRun();

        var outcome = await Write(f, run);

        Assert.Equal(2, f.Fake.Requests.Count);
        Assert.All(f.Fake.Requests, r => Assert.Equal("claude-story", r.ModelId));
        Assert.All(f.Fake.Requests, r => Assert.False(r.AllowBatch));
        Assert.Equal(f.Fake.Requests[0].SystemPrompt, f.Fake.Requests[1].SystemPrompt);
        Assert.Equal(f.SystemPrompt, f.Fake.Requests[0].SystemPrompt);

        var content = outcome.Content;
        Assert.NotNull(content);
        Assert.False(outcome.NotRun);
        Assert.Equal(string.Empty, outcome.Error);
        Assert.Empty(outcome.Warnings);

        var expectedTopics = f.Plan.Chunks.SelectMany(c => Enumerable.Repeat(c.TopicIndex, c.TargetTurns)).ToList();
        Assert.Equal(expectedTopics, content!.Turns.Select(t => t.TopicIndex));

        foreach (var speaker in content.Speakers)
        {
            if (speaker.Role is SpeakerRole.Reporter or SpeakerRole.Videographer)
                Assert.Equal(string.Empty, speaker.Honorific);
            else
                Assert.Equal(speaker.Role == SpeakerRole.Witness ? "DR" : "MS", speaker.Honorific);
        }

        // Plan speakers are deep-copied, never mutated
        Assert.All(f.Plan.Speakers, s => Assert.Equal(string.Empty, s.Honorific));

        Assert.Equal(f.Plan.Witness.Id, content.WitnessId);
        Assert.Equal(f.Plan.DocumentTitle, content.DocumentTitle);
        Assert.Equal(CaseNo, content.CaseNumber);
        Assert.Equal(f.Plan.Schedule.Date.ToString("yyyy-MM-dd"), content.Date);
        Assert.NotEmpty(content.CaptionLines);
        Assert.NotEmpty(content.EndTime);
        Assert.NotEqual("the close of the session", content.EndTime);
        Assert.Equal(TranscriptLayout.ComputeEndTime(content.StartTime, TranscriptLayout.CountTestimonyLines(content)), content.EndTime);
        Assert.Equal(f.Plan.TestimonyPages, content.TargetTestimonyPages);
        Assert.Equal(f.Plan.Exhibits.Count, content.Exhibits.Count);
    }

    [Fact]
    public async Task UnknownPerson_RetriedWithFeedback()
    {
        var f = CreateFixture();
        f.Script((chunk, attempt) =>
        {
            var dto = CleanDto(f, chunk);
            if (chunk.Index == 0 && attempt == 1)
                dto.PeopleNamed.Add("Zelda Quarkworth");
            return Ok(dto);
        });
        using var run = NewRun();

        var outcome = await Write(f, run);

        Assert.Equal(3, f.Fake.Requests.Count);
        Assert.DoesNotContain(DiscoveryValidator.FeedbackHeader, f.Fake.Requests[0].UserPrompt);
        Assert.Contains(DiscoveryValidator.FeedbackHeader, f.Fake.Requests[1].UserPrompt);
        Assert.Contains("Zelda Quarkworth", f.Fake.Requests[1].UserPrompt);
        Assert.NotNull(outcome.Content);
        Assert.Empty(outcome.Warnings);
    }

    [Fact]
    public async Task ThreeAttempts_KeepsBestNonStructural_AndItsViolationsBecomeWarnings()
    {
        var f = CreateFixture();
        f.Script((chunk, attempt) =>
        {
            if (chunk.Index != 0)
                return Ok(CleanDto(f, chunk));
            if (attempt == 2)
            {
                var dto = CleanDto(f, chunk);
                dto.PeopleNamed.Add("Zelda Quarkworth");
                return Ok(dto);
            }
            return Ok(CleanDto(f, chunk, turns: 2));
        });
        using var run = NewRun();

        var outcome = await Write(f, run);

        Assert.Equal(DepositionWriter.MaxAttempts + 1, f.Fake.Requests.Count);
        var content = Assert.IsType<DepositionContent>(outcome.Content);

        // Attempt 2 (full length) was kept, not one of the two-turn attempts
        Assert.Equal(f.Plan.Chunks.Sum(c => c.TargetTurns), content.Turns.Count);

        var warning = Assert.Single(outcome.Warnings);
        Assert.Equal(DiscoveryRule.D2UnknownPerson, warning.Rule);
        Assert.Equal("Zelda Quarkworth", warning.Value);
    }

    [Fact]
    public async Task Normalization_FixesSpeakersExhibitsAndDropsEmptyTurns()
    {
        var f = CreateFixture();
        f.Script((chunk, _) =>
        {
            var dto = CleanDto(f, chunk);
            if (chunk.Index == 0)
            {
                dto.Turns.InsertRange(0, new[]
                {
                    new DiscoveryPrompts.TurnDto { Kind = TurnKind.Q, SpeakerId = "BOGUS", Text = QuestionText, ExhibitNumber = 999 },
                    new DiscoveryPrompts.TurnDto { Kind = TurnKind.Objection, SpeakerId = "NOBODY", Text = "Objection, form." },
                    new DiscoveryPrompts.TurnDto { Kind = TurnKind.A, SpeakerId = "WHO", Text = AnswerText },
                    new DiscoveryPrompts.TurnDto { Kind = TurnKind.Colloquy, SpeakerId = f.Examining.SpeakerId, Text = "   " }
                });
            }
            return Ok(dto);
        });
        using var run = NewRun();

        var outcome = await Write(f, run);

        var turns = Assert.IsType<DepositionContent>(outcome.Content).Turns;
        Assert.Equal(TurnKind.Q, turns[0].Kind);
        Assert.Equal(f.Examining.SpeakerId, turns[0].SpeakerId);
        Assert.Equal(0, turns[0].ExhibitNumber);
        Assert.Equal(TurnKind.Objection, turns[1].Kind);
        Assert.Equal((f.Defending ?? f.Examining).SpeakerId, turns[1].SpeakerId);
        Assert.Equal(TurnKind.A, turns[2].Kind);
        Assert.Equal(f.Witness.SpeakerId, turns[2].SpeakerId);
        Assert.DoesNotContain(turns, t => string.IsNullOrWhiteSpace(t.Text));
        Assert.Equal(f.Plan.Chunks.Sum(c => c.TargetTurns) + 3, turns.Count);

        // Only exhibits assigned to a chunk are marked, each once
        var assigned = f.Plan.Chunks.SelectMany(c => c.ExhibitNumbers).ToHashSet();
        Assert.All(turns.Where(t => t.ExhibitNumber != 0), t => Assert.Contains(t.ExhibitNumber, assigned));
        Assert.Equal(turns.Count(t => t.ExhibitNumber != 0), turns.Where(t => t.ExhibitNumber != 0).Select(t => t.ExhibitNumber).Distinct().Count());
    }

    [Fact]
    public async Task ExhibitMentionedButNotMarked_GetsMarkerOnFirstMention()
    {
        var f = CreateFixture();
        var chunk = f.Plan.Chunks.FirstOrDefault(c => c.ExhibitNumbers.Count > 0);
        Assert.NotNull(chunk);
        var number = chunk!.ExhibitNumbers[0];
        f.Script((c, _) =>
        {
            var dto = CleanDto(f, c);
            if (c.Index == chunk.Index)
            {
                foreach (var turn in dto.Turns.Where(t => t.ExhibitNumber == number))
                    turn.ExhibitNumber = 0;
                dto.Turns[4].Text = $"Let me show you what has been marked as Exhibit {number}. Do you recognize it?";
            }
            return Ok(dto);
        });
        using var run = NewRun();

        var outcome = await Write(f, run);

        var turns = Assert.IsType<DepositionContent>(outcome.Content).Turns;
        var marked = Assert.Single(turns, t => t.ExhibitNumber == number);
        Assert.Contains($"Exhibit {number}", marked.Text);
    }

    [Fact]
    public async Task Overflow_TrimmedAtTurnBoundaryInLastTopic()
    {
        var f = CreateFixture();
        var last = f.Plan.Chunks[^1];
        f.Script((chunk, _) => chunk.Index == last.Index
            ? Ok(CleanDto(f, chunk, turns: chunk.TargetTurns * 3, answer: LongAnswerText))
            : Ok(CleanDto(f, chunk)));
        using var run = NewRun();

        var outcome = await Write(f, run);

        var content = Assert.IsType<DepositionContent>(outcome.Content);
        var maxLines = (int)(f.Plan.TestimonyPages * TranscriptLayout.LinesPerPage * 1.25);
        Assert.True(TranscriptLayout.CountTestimonyLines(content) <= maxLines);

        // Earlier topics are untouched; the last topic lost turns from its end
        var earlier = f.Plan.Chunks.Where(c => c.TopicIndex != last.TopicIndex).Sum(c => c.TargetTurns);
        Assert.Equal(earlier, content.Turns.Count(t => t.TopicIndex != last.TopicIndex));
        var lastTopicTurns = content.Turns.Where(t => t.TopicIndex == last.TopicIndex).ToList();
        var supplied = f.Plan.Chunks.Where(c => c.TopicIndex == last.TopicIndex && c.Index != last.Index).Sum(c => c.TargetTurns)
            + last.TargetTurns * 3;
        Assert.InRange(lastTopicTurns.Count, 1, supplied - 1);
        Assert.Equal(content.Turns.Count - lastTopicTurns.Count, content.Turns.FindIndex(t => t.TopicIndex == last.TopicIndex));
        Assert.Equal(TranscriptLayout.ComputeEndTime(content.StartTime, TranscriptLayout.CountTestimonyLines(content)), content.EndTime);
    }

    [Fact]
    public async Task FatalProviderError_ReturnsNotRunWithCost()
    {
        var f = CreateFixture(priced: true);
        f.Script((_, _) => new LlmProviderResponse
        {
            Outcome = LlmOutcome.Failed, FailureKind = LlmFailureKind.Auth, Message = "Bad key", InputTokens = 7, OutputTokens = 0
        });
        using var run = NewRun();

        var outcome = await Write(f, run);

        Assert.True(outcome.NotRun);
        Assert.True(run.IsFatal);
        Assert.Null(outcome.Content);
        Assert.Equal(7m, outcome.Cost.TotalUsd);
        Assert.Single(f.Fake.Requests);
    }

    [Fact]
    public async Task RefusedOnEveryAttempt_FailsWithAllCostCaptured()
    {
        var f = CreateFixture(priced: true);
        f.Script((_, _) => FakeLlmProvider.Refused("Not comfortable"));
        using var run = NewRun();

        var outcome = await Write(f, run);

        Assert.Equal(DepositionWriter.MaxAttempts, f.Fake.Requests.Count);
        Assert.Null(outcome.Content);
        Assert.False(outcome.NotRun);
        Assert.True(outcome.Refused);
        Assert.NotEmpty(outcome.Error);

        // Refused() bills 10 input + 2 output tokens at $1 each
        Assert.Equal(36m, outcome.Cost.TotalUsd);
    }

    [Fact]
    public async Task SecondChunkTimesOut_DocumentFailsWithTimeoutReason()
    {
        var f = CreateFixture();
        f.Script((chunk, _) => chunk.Index == 0 ? Ok(CleanDto(f, chunk)) : throw new OperationCanceledException());
        using var run = NewRun();

        var outcome = await Write(f, run);

        Assert.Equal(1 + DepositionWriter.MaxAttempts, f.Fake.Requests.Count);
        Assert.Null(outcome.Content);
        Assert.True(outcome.TimedOut);
        Assert.False(outcome.NotRun);
        Assert.StartsWith("The request timed out", outcome.Error);
    }

    [Fact]
    public async Task UserCancellation_Propagates()
    {
        var f = CreateFixture();
        f.Script((chunk, _) => Ok(CleanDto(f, chunk)));
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        using var run = new PlanCallRun(cts.Token, 1, "Click Retry.");

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Write(f, run));
        Assert.Empty(f.Fake.Requests);
    }

    [Fact]
    public async Task OnPart_ReportsEachChunk()
    {
        var f = CreateFixture();
        f.Script((chunk, _) => Ok(CleanDto(f, chunk)));
        using var run = NewRun();
        var parts = new List<(int, int)>();

        await new DepositionWriter(f.Gateway).WriteAsync(f.Draft, f.Plan, CaseNo, f.SystemPrompt, string.Empty, run, (p, n) => parts.Add((p, n)));

        Assert.Equal(new[] { (1, 2), (2, 2) }, parts);
    }
}
