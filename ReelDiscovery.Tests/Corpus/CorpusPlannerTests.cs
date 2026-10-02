using System.Text.Json;
using System.Text.RegularExpressions;
using ReelDiscovery.Models.Case;
using ReelDiscovery.Models.Corpus;
using ReelDiscovery.Services.Ai;
using ReelDiscovery.Services.Corpus;
using ReelDiscovery.Tests.Fakes;
using Xunit.Abstractions;

namespace ReelDiscovery.Tests.Corpus;

public partial class CorpusPlannerTests
{
    private const string Guidance = "more traffic around the change order approval";

    private readonly ITestOutputHelper _output;

    public CorpusPlannerTests(ITestOutputHelper output)
    {
        _output = output;
    }

    // "T-0003 msg 2 | ... | attachments: Word, Excel"
    [GeneratedRegex(@"^\s*(T-\d{4}) msg (\d+) \|.*\| attachments: (.+?)\s*$", RegexOptions.Multiline)]
    private static partial Regex MessageLinePattern();

    // "S-0001 | Daily business | ..."
    [GeneratedRegex(@"^(S-\d{4}) \| ([^|]+?) \|", RegexOptions.Multiline)]
    private static partial Regex SlotLinePattern();

    [GeneratedRegex("\"seed\"\\s*:", RegexOptions.IgnoreCase)]
    private static partial Regex SeedProperty();

    private static readonly string ThreadSchema = JsonSchemaRegistry.GetSchemaName(typeof(RelevantThreadFiller.ThreadPlanBatchDto));
    private static readonly string SeedSchema = JsonSchemaRegistry.GetSchemaName(typeof(SubjectSeeder.SubjectSeedBatchDto));
    private static readonly string ProtocolSchema = JsonSchemaRegistry.GetSchemaName(typeof(ReviewProtocolBuilder.ReviewProtocolProseDto));
    private static readonly string GuidanceSchema = JsonSchemaRegistry.GetSchemaName(typeof(CorpusPlanner.GuidanceWeightsDto));

    private const string ProseJson = """
        {
          "purpose": "This protocol guides review of documents collected for the Lakeview Tower payment dispute.",
          "background": "Brightline Glazing installed windows on Lakeview Tower and was not paid for Change Order 7.",
          "hotGuidance": "Flag documents that directly show Harmon approving or refusing Change Order 7.",
          "privilegeGuidance": "Communications with counsel are privileged unless a third party is copied.",
          "issues": [
            { "issueId": "ISS-01", "relevantExamples": ["An email approving Change Order 7."], "notRelevantExamples": ["A lunch invitation."] },
            { "issueId": "ISS-02", "relevantExamples": ["A late-payment notice."], "notRelevantExamples": ["A holiday schedule."] }
          ]
        }
        """;

    // ----- Fixtures -----

    private static CaseDraft Draft(int total = 250, int percent = 20)
    {
        var draft = CorpusTestData.RichDraft();
        draft.Corpus = new CorpusState { Settings = new CorpusSettings { TotalEmails = total, RelevantPercent = percent } };
        return draft;
    }

    private static LlmProviderResponse EchoThreads(LlmProviderRequest request)
    {
        var threads = new List<(string Id, List<(int Seq, int Attachments)> Messages)>();
        foreach (Match match in MessageLinePattern().Matches(request.UserPrompt))
        {
            var id = match.Groups[1].Value;
            var seq = int.Parse(match.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture);
            var kinds = match.Groups[3].Value.Trim();
            var count = kinds == "none" ? 0 : kinds.Split(',').Length;
            var index = threads.FindIndex(t => t.Id == id);
            if (index < 0)
                threads.Add((id, new List<(int, int)> { (seq, count) }));
            else
                threads[index].Messages.Add((seq, count));
        }

        var dto = threads.Select(t => new
        {
            threadId = t.Id,
            subject = $"Subject for {t.Id}",
            purpose = $"Purpose of {t.Id}.",
            rationale = $"Rationale for {t.Id}.",
            linkedThreadIds = Array.Empty<string>(),
            messages = t.Messages.Select(m => new
            {
                seq = m.Seq,
                intent = $"Intent {t.Id} {m.Seq}",
                mustConvey = new[] { "Point A" },
                attachmentTitles = Enumerable.Range(1, m.Attachments).Select(k => $"Title {t.Id} m{m.Seq} a{k}").ToArray()
            }).ToArray()
        }).ToArray();
        return FakeLlmProvider.Ok(JsonSerializer.Serialize(new { threads = dto }));
    }

    private static LlmProviderResponse EchoSlots(LlmProviderRequest request)
    {
        var seeds = SlotLinePattern().Matches(request.UserPrompt).Select(m =>
        {
            var slotId = m.Groups[1].Value;
            var category = m.Groups[2].Value.Trim();
            return new
            {
                slotId,
                subject = $"Routine topic {slotId}",
                angle = category == "Near-miss" ? $"Angle {slotId}." : string.Empty,
                senderName = category is "Newsletter" or "Spam" or "Vendor" ? $"Sender {slotId}"
                    : category == "Personal" ? $"Pat Sender{slotId[2..]}" : string.Empty
            };
        }).ToArray();
        return FakeLlmProvider.Ok(JsonSerializer.Serialize(new { seeds }));
    }

    private static string WeightsJson(params (string FactId, string Weight)[] weights) =>
        JsonSerializer.Serialize(new { factWeights = weights.Select(w => new { factId = w.FactId, weight = w.Weight }).ToArray() });

    /// <summary>
    /// Schema-aware responder: echoes thread ids and slots, writes protocol prose and the given guidance weights.
    /// </summary>
    private static Func<LlmProviderRequest, LlmProviderResponse> Responder(string? weightsJson = null,
        Func<LlmProviderRequest, LlmProviderResponse?>? overrideFn = null) => request =>
    {
        var custom = overrideFn?.Invoke(request);
        if (custom != null)
            return custom;
        if (request.SchemaName == ThreadSchema)
            return EchoThreads(request);
        if (request.SchemaName == SeedSchema)
            return EchoSlots(request);
        if (request.SchemaName == ProtocolSchema)
            return FakeLlmProvider.Ok(ProseJson);
        if (request.SchemaName == GuidanceSchema)
            return FakeLlmProvider.Ok(weightsJson ?? WeightsJson());
        throw new InvalidOperationException($"Unexpected schema {request.SchemaName}");
    };

    private static (CorpusPlanner Planner, FakeLlmProvider Fake) Planner(int seed = 42)
    {
        var (gateway, fake) = CorpusTestData.CreateGateway();
        return (new CorpusPlanner(gateway, new Random(seed)), fake);
    }

    private static int Count(FakeLlmProvider fake, string schema)
    {
        lock (fake.Requests)
            return fake.Requests.Count(r => r.SchemaName == schema);
    }

    private sealed class SyncProgress : IProgress<PlanProgress>
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

    // ----- Tests -----

    [Fact]
    public async Task PlanAsync_EndToEnd_BuildsCompleteValidatedPlan()
    {
        var draft = Draft();
        var before = draft.ToJson();
        var settings = draft.Corpus!.Settings;
        var (planner, fake) = Planner();
        fake.Respond = Responder();

        var result = await planner.PlanAsync(draft, false, string.Empty);

        Assert.True(result.Succeeded, result.FailureMessage);
        var plan = result.Plan!;
        Assert.Equal(250, plan.EmailCount);
        var relevant = plan.Threads.Where(t => t.Relevance == ThreadRelevance.Relevant).ToList();
        Assert.Equal(CorpusSettings.RelevantCount(250, 20), relevant.Sum(t => t.Messages.Count));
        Assert.Equal(50, relevant.Sum(t => t.Messages.Count));

        foreach (var thread in plan.Threads)
        {
            Assert.False(string.IsNullOrWhiteSpace(thread.Subject), $"{thread.Id} has no subject");
            var seqs = thread.Messages.Select(m => m.Seq).ToHashSet();
            foreach (var message in thread.Messages)
            {
                Assert.False(string.IsNullOrWhiteSpace(message.FromId));
                Assert.NotEmpty(message.ToIds);
                Assert.NotEqual(default, message.SentAt);
                Assert.False(string.IsNullOrWhiteSpace(message.TimeZoneId));
                if (message.Seq == thread.Messages.Min(m => m.Seq))
                    Assert.Equal(MessageKind.New, message.Kind);
                else
                    Assert.True(seqs.Contains(message.ReplyToSeq) && message.ReplyToSeq < message.Seq, $"{message.Id} reply-to {message.ReplyToSeq}");
            }
        }

        // DocIDs contiguous
        var numbers = plan.AllMessages().Select(m => m.DocId).ToList();
        Assert.All(numbers, n => Assert.False(string.IsNullOrWhiteSpace(n)));
        Assert.Equal(numbers.Count, numbers.Distinct().Count());

        Assert.Equal(draft.Guide!.ComputeHash(), plan.GuideHash);
        Assert.Equal(settings.ComputeHash(), plan.SettingsHash);
        Assert.Equal(DocIdPrefix.Effective(settings, draft), plan.DocIdPrefix);
        Assert.False(plan.Protocol.IsFallback);
        Assert.Equal(string.Empty, plan.Guidance);

        Assert.DoesNotContain(result.Findings, f => f.Message.Contains("relevant emails (") && f.Message.Contains("the settings ask for"));
        Assert.DoesNotContain(result.Findings, f => f.Message.StartsWith("No planned email is evidence of"));
        Assert.DoesNotContain(result.Findings, f => f.Message.Contains("emails (more than"));
        Assert.DoesNotContain(result.Findings, f => f.Message.Contains("DocID"));

        // Empty guidance never asks for weights
        Assert.Equal(0, Count(fake, GuidanceSchema));
        Assert.Equal(1, Count(fake, ProtocolSchema));
        Assert.Equal(before, draft.ToJson());

        // D-15 diagnostics: multi-fact threads (merged across issues) and threads carrying 2+ issues
        var multiFact = relevant.Count(t => t.FactIds.Distinct().Count() >= 2);
        var multiIssue = relevant.Count(t => t.IssueIds.Distinct().Count() >= 2);
        _output.WriteLine($"Relevant threads {relevant.Count}; multi-fact (merged) {multiFact}; two+ issues {multiIssue}");
        foreach (var finding in result.Findings)
            _output.WriteLine($"Finding: {finding.Where}: {finding.Message}");
    }

    [Fact]
    public async Task PlanAsync_DocIdsAreContiguousInOrder()
    {
        var draft = Draft();
        var (planner, fake) = Planner();
        fake.Respond = Responder();

        var result = await planner.PlanAsync(draft, false, string.Empty);

        var plan = result.Plan!;
        var docIds = plan.AllMessages().Select(m => m.DocId).OrderBy(d => d, StringComparer.Ordinal).ToList();
        var digits = docIds.Select(d => int.Parse(new string(d.Where(char.IsDigit).ToArray()), System.Globalization.CultureInfo.InvariantCulture)).ToList();
        Assert.All(docIds, d => Assert.StartsWith(plan.DocIdPrefix, d));
        Assert.Equal(Enumerable.Range(digits[0], digits.Count), digits);
    }

    [Fact]
    public async Task PlanAsync_ReportsStagesInOrder_DoneMonotonic_EndsAtTotal()
    {
        var draft = Draft();
        var (planner, fake) = Planner();
        fake.Respond = Responder();
        var progress = new SyncProgress();

        var result = await planner.PlanAsync(draft, false, string.Empty, progress);

        Assert.True(result.Succeeded);
        var reports = progress.Snapshot();
        Assert.Equal(PlanStage.Allocating, reports[0].Stage);
        Assert.Equal(0, reports[0].Done);

        var relevant = result.Plan!.Threads.Where(t => t.Relevance == ThreadRelevance.Relevant).ToList();
        var nonRelevant = result.Plan.Threads.Where(t => t.Relevance != ThreadRelevance.Relevant).ToList();
        var chunks = RelevantThreadFiller.BuildChunks(relevant).Count;
        var batches = SubjectSeeder.BuildBatches(nonRelevant).Count;
        var total = chunks + batches + 2;

        Assert.All(reports.Where(r => r.Stage == PlanStage.PlanningRelevant), r => Assert.Equal(chunks, r.StageTotal));
        Assert.All(reports.Where(r => r.Stage == PlanStage.PlanningOther), r => Assert.Equal(batches, r.StageTotal));
        Assert.Contains(reports, r => r.Stage == PlanStage.PlanningRelevant);
        Assert.Contains(reports, r => r.Stage == PlanStage.PlanningOther);
        Assert.Contains(reports, r => r.Stage == PlanStage.WritingProtocol);
        Assert.Contains(reports, r => r.Stage == PlanStage.Validating);

        // Stage order never goes back
        var stages = reports.Select(r => (int)r.Stage).ToList();
        Assert.Equal(stages.OrderBy(s => s), stages);

        Assert.All(reports, r => Assert.Equal(total, r.Total));
        for (var i = 1; i < reports.Count; i++)
            Assert.True(reports[i].Done >= reports[i - 1].Done, $"Done decreased at report {i}");
        Assert.Equal(total, reports[^1].Done);
    }

    [Fact]
    public async Task PlanAsync_Guidance_BoostsFact_PassesTextToEveryChunk_AndStoresIt()
    {
        // Baseline without guidance
        var (baseline, baseFake) = Planner(7);
        baseFake.Respond = Responder();
        var plain = await baseline.PlanAsync(Draft(), false, string.Empty);

        var draft = Draft();
        var (planner, fake) = Planner(7);
        fake.Respond = Responder(WeightsJson(("F-003", "boost"), ("F-999", "boost"), (" f-001 ", "normal")));
        var guided = await planner.PlanAsync(draft, false, Guidance);

        Assert.True(plain.Succeeded && guided.Succeeded);
        int ThreadsFor(CorpusPlanResult r, string factId) =>
            r.Plan!.Threads.Count(t => t.Relevance == ThreadRelevance.Relevant && t.FactIds.Contains(factId));
        Assert.True(ThreadsFor(guided, "F-003") > ThreadsFor(plain, "F-003"),
            $"F-003 threads: guided {ThreadsFor(guided, "F-003")}, plain {ThreadsFor(plain, "F-003")}");

        Assert.Equal(1, Count(fake, GuidanceSchema));
        var guidanceRequest = fake.Requests.First(r => r.SchemaName == GuidanceSchema);
        Assert.Contains(Guidance, guidanceRequest.UserPrompt);
        Assert.Contains("F-003", guidanceRequest.UserPrompt);

        var chunkPrompts = fake.Requests.Where(r => r.SchemaName == ThreadSchema).ToList();
        Assert.NotEmpty(chunkPrompts);
        Assert.All(chunkPrompts, r => Assert.Contains(Guidance, r.UserPrompt));
        Assert.Equal(Guidance, guided.Plan!.Guidance);
    }

    [Fact]
    public async Task PlanAsync_GuidanceCallFails_NotesItAndStillPlans()
    {
        var draft = Draft();
        var (planner, fake) = Planner();
        fake.Respond = Responder(overrideFn: r => r.SchemaName == GuidanceSchema ? FakeLlmProvider.Refused("no") : null);

        var result = await planner.PlanAsync(draft, false, Guidance);

        Assert.True(result.Succeeded);
        Assert.Contains("The guidance could not be applied to email volume; it was still passed to the thread planner.", result.Plan!.Notes);
        Assert.All(fake.Requests.Where(r => r.SchemaName == ThreadSchema), r => Assert.Contains(Guidance, r.UserPrompt));
    }

    [Fact]
    public async Task PlanAsync_FatalOnFirstChunk_StopsWithClassifiedResult()
    {
        var draft = Draft();
        var before = draft.ToJson();
        var (planner, fake) = Planner();
        fake.Respond = Responder(overrideFn: r => r.SchemaName == ThreadSchema ? FakeLlmProvider.Failed(LlmFailureKind.Auth, "Invalid API key") : null);

        var result = await planner.PlanAsync(draft, false, string.Empty);

        Assert.False(result.Succeeded);
        Assert.True(result.IsFatal);
        Assert.Contains("Invalid API key", result.FatalError);
        Assert.Null(result.Plan);
        Assert.Equal(0, Count(fake, ProtocolSchema));
        Assert.Equal(0, Count(fake, SeedSchema));
        Assert.Equal(before, draft.ToJson());
    }

    [Fact]
    public async Task PlanAsync_FatalOnProtocol_Fails()
    {
        var draft = Draft();
        var (planner, fake) = Planner();
        fake.Respond = Responder(overrideFn: r => r.SchemaName == ProtocolSchema ? FakeLlmProvider.Failed(LlmFailureKind.SpendLimit, "Spend limit reached") : null);

        var result = await planner.PlanAsync(draft, false, string.Empty);

        Assert.False(result.Succeeded);
        Assert.Contains("Spend limit reached", result.FatalError);
        Assert.Null(result.Plan);
    }

    [Fact]
    public async Task PlanAsync_MajorityRefused_FailsWithCounts()
    {
        var draft = Draft();
        var before = draft.ToJson();
        var (planner, fake) = Planner();
        fake.Respond = Responder(overrideFn: r => r.SchemaName is var s && (s == ThreadSchema || s == SeedSchema)
            ? FakeLlmProvider.Refused("I can't help with that.") : null);

        var result = await planner.PlanAsync(draft, false, string.Empty);

        Assert.False(result.Succeeded);
        Assert.False(result.IsFatal);
        Assert.True(result.TotalChunks > 0);
        Assert.Equal(result.TotalChunks, result.RefusedChunks);
        Assert.Equal("I can't help with that.", result.RefusalMessage);
        Assert.Contains(result.RefusedChunks.ToString(System.Globalization.CultureInfo.InvariantCulture), result.FailureMessage);
        Assert.Null(result.Plan);
        Assert.Equal(0, Count(fake, ProtocolSchema));
        Assert.Equal(before, draft.ToJson());
    }

    [Fact]
    public async Task PlanAsync_MinorityRefused_SucceedsWithTemplates_AndRule17Finding()
    {
        var draft = Draft();
        var (planner, fake) = Planner();
        fake.Respond = Responder(overrideFn: r => r.SchemaName == ThreadSchema ? FakeLlmProvider.Refused("no") : null);

        var result = await planner.PlanAsync(draft, false, string.Empty);

        // One relevant chunk refused out of the chunk + batch total
        Assert.True(result.TotalChunks >= 3, $"total {result.TotalChunks}");
        Assert.True(result.Succeeded, result.FailureMessage);
        Assert.Equal(1, result.RefusedChunks);
        var templated = result.Plan!.Threads.Where(t => t.Relevance == ThreadRelevance.Relevant).ToList();
        Assert.All(templated, t => Assert.True(t.UsesFallbackContent));
        Assert.Contains(result.Findings, f => f.Message.Contains("uses a placeholder subject because the model did not plan it"));
    }

    [Fact]
    public async Task PlanAsync_CancelMidRun_Throws_AndLeavesDraftUnchanged()
    {
        var draft = Draft();
        var before = draft.ToJson();
        var (planner, fake) = Planner();
        using var cts = new CancellationTokenSource();
        fake.Respond = Responder();
        fake.OnSend = i =>
        {
            if (i == 1)
                cts.Cancel();
        };

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => planner.PlanAsync(draft, false, string.Empty, null, cts.Token));
        Assert.Equal(before, draft.ToJson());
    }

    [Fact]
    public async Task PlanAsync_NoDatedFacts_Fails()
    {
        var draft = Draft();
        foreach (var fact in draft.Guide!.Facts)
            fact.Date = string.Empty;
        var (planner, fake) = Planner();
        fake.Respond = Responder();

        var result = await planner.PlanAsync(draft, false, Guidance);

        Assert.False(result.Succeeded);
        Assert.Equal("The case guide has no dated key facts. Add key facts on the Case Guide step, then click Plan Corpus.", result.FailureMessage);
        Assert.Empty(fake.Requests);
    }

    [Fact]
    public async Task PlanAsync_RelevantShortfall_KeepsOneThreadPerFact_AndNotes()
    {
        // 50 emails at 5% = 3 relevant emails for 8 dated facts
        var draft = Draft(50, 5);
        var (planner, fake) = Planner();
        fake.Respond = Responder();

        var result = await planner.PlanAsync(draft, false, string.Empty);

        Assert.True(result.Succeeded, result.FailureMessage);
        var relevant = result.Plan!.Threads.Where(t => t.Relevance == ThreadRelevance.Relevant).ToList();
        foreach (var fact in draft.Guide!.Facts.Where(f => CaseDraft.TryParseDate(f.Date, out _)))
            Assert.Contains(relevant, t => t.FactIds.Contains(fact.Id));
        Assert.Contains("The plan has more relevant emails than the % relevant setting because every key fact needs at least one email.", result.Plan.Notes);
    }

    [Fact]
    public async Task PlanAsync_PlanJsonHasNoSeed_AndSettingsAreACopy()
    {
        var draft = Draft();
        var (planner, fake) = Planner();
        fake.Respond = Responder();

        var result = await planner.PlanAsync(draft, false, string.Empty);

        var json = JsonSerializer.Serialize(result.Plan, CaseDraft.JsonOptions);
        Assert.DoesNotMatch(SeedProperty(), json);
        Assert.NotSame(draft.Corpus!.Settings, result.Plan!.Settings);
    }

    [Fact]
    public async Task PlanAsync_NoCorpusState_UsesDefaultSettings()
    {
        var draft = CorpusTestData.RichDraft();
        draft.Corpus = null;
        var before = draft.ToJson();
        var (planner, fake) = Planner();
        fake.Respond = Responder();

        var result = await planner.PlanAsync(draft, false, string.Empty);

        Assert.True(result.Succeeded, result.FailureMessage);
        Assert.Equal(CorpusSettings.CreateDefault(draft).TotalEmails, result.Plan!.EmailCount);
        Assert.Null(draft.Corpus);
        Assert.Equal(before, draft.ToJson());
    }

    [Fact]
    public void SchemaTypes_ContainEveryPlanningDto()
    {
        Assert.Equal(4, CorpusPlanner.SchemaTypes.Count);
        Assert.Contains(typeof(CorpusPlanner.GuidanceWeightsDto), CorpusPlanner.SchemaTypes);
        Assert.Contains(typeof(RelevantThreadFiller.ThreadPlanBatchDto), CorpusPlanner.SchemaTypes);
        Assert.Contains(typeof(SubjectSeeder.SubjectSeedBatchDto), CorpusPlanner.SchemaTypes);
        Assert.Contains(typeof(ReviewProtocolBuilder.ReviewProtocolProseDto), CorpusPlanner.SchemaTypes);
    }
}
