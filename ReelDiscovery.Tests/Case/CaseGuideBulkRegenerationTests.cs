using System.Text.Json;
using System.Text.RegularExpressions;
using ReelDiscovery.Models.Case;
using ReelDiscovery.Services.Ai;
using ReelDiscovery.Services.Case;
using ReelDiscovery.Tests.Fakes;

namespace ReelDiscovery.Tests.Case;

/// <summary>
/// Regenerate Selected (RegenerateEntriesAsync): each selected fact is rewritten in place with the single-entry prompt, at most
/// MaxParallelCalls at a time, merged after every call finished; edited facts are skipped unless replaced, failures keep their
/// text, and a cancel keeps the facts that came back.
/// </summary>
public class CaseGuideBulkRegenerationTests
{
    private static readonly string[] AllFacts = { "F-001", "F-002", "F-003", "F-004", "F-005", "F-006" };

    private static string EntryJson(string id, string date = "2024-03-19", string actor = "CH-004") =>
        $$"""{ "date": "{{date}}", "title": "Rewritten {{id}}", "description": "Lisa Chen handled {{id}} on the Lakeview Tower project.", "issueIds": ["ISS-01"], "actorIds": ["{{actor}}"], "knownByIds": ["CH-003"], "significance": "Key" }""";

    private static (LlmGateway Gateway, KeyedProvider Provider) Gateway(int delayMs = 0)
    {
        var provider = new KeyedProvider(new FakeLlmProvider(LlmProviderKind.Anthropic), delayMs);
        foreach (var id in AllFacts)
            provider.Responses[id] = FakeLlmProvider.Ok(EntryJson(id));
        return (new LlmGateway(provider, CaseTestData.StoryModel(), CaseTestData.VolumeModel()), provider);
    }

    private static string Snapshot(KeyFact f) => JsonSerializer.Serialize(f, CaseDraft.JsonOptions);

    private static void FlagForReview(CaseGuide guide, params string[] ids)
    {
        foreach (var id in ids)
        {
            var fact = guide.FindFact(id)!;
            fact.NeedsReview = true;
            fact.ReviewReason = "Lisa Chen was changed after it was written.";
        }
    }

    [Fact]
    public async Task RewritesEachFactInPlace_ClearsNeedsReview_FlagsValues()
    {
        var draft = CaseTestData.SampleGuidedDraft();
        var guide = draft.Guide!;
        FlagForReview(guide, "F-001", "F-003", "F-005");
        var f3 = guide.FindFact("F-003")!;
        var revision = draft.Revision;
        var (gateway, provider) = Gateway();

        var result = await new CaseGuideGenerator(gateway).RegenerateEntriesAsync(draft, GuidePart.Facts,
            new[] { "F-001", "F-003", "F-005" }, "use the new names", replaceEdited: false);

        Assert.Equal(new[] { "F-001", "F-003", "F-005" }, result.Regenerated);
        Assert.Empty(result.Failed);
        Assert.Empty(result.SkippedEdited);
        Assert.False(result.IsFatal);
        Assert.Equal(3, result.Attempted);

        // One single-entry request per fact, each carrying the guidance
        Assert.Equal(3, provider.Requests.Count);
        Assert.Equal(new[] { "F-001", "F-003", "F-005" }, provider.Requests.Select(KeyedProvider.FactIdOf).OrderBy(i => i));
        Assert.All(provider.Requests, r => Assert.Contains("Guidance from the user: use the new names", r.UserPrompt));

        Assert.Same(f3, guide.FindFact("F-003"));
        foreach (var id in new[] { "F-001", "F-003", "F-005" })
        {
            var fact = guide.FindFact(id)!;
            Assert.Equal($"Rewritten {id}", fact.Title);
            Assert.False(fact.NeedsReview);
            Assert.Equal(string.Empty, fact.ReviewReason);
            Assert.False(fact.IsUserEdited);
        }

        // Untouched facts keep their text; values citing a rewritten fact are asked to be reviewed
        Assert.Equal("Harmon confirms the change order procedure", guide.FindFact("F-002")!.Title);
        Assert.True(guide.FindValue("V-001")!.NeedsReview);
        Assert.True(guide.FindValue("V-003")!.NeedsReview);
        Assert.True(draft.Revision > revision);
    }

    [Fact]
    public async Task RunsAtMostMaxParallelCalls()
    {
        var draft = CaseTestData.SampleGuidedDraft();
        var (gateway, provider) = Gateway(delayMs: 50);

        var result = await new CaseGuideGenerator(gateway).RegenerateEntriesAsync(draft, GuidePart.Facts, AllFacts, "", false);

        Assert.Equal(AllFacts.Length, result.Regenerated.Count);
        Assert.Equal(AllFacts.Length, provider.Requests.Count);
        Assert.InRange(provider.MaxConcurrent, 2, CaseGuideGenerator.MaxParallelCalls);
    }

    [Fact]
    public async Task SkipsEditedFacts_UnlessReplaceEdited()
    {
        var draft = CaseTestData.SampleGuidedDraft();
        var guide = draft.Guide!;
        var edited = guide.FindFact("F-002")!;
        edited.IsUserEdited = true;
        edited.Title = "My own wording";
        var (gateway, provider) = Gateway();

        var skipped = await new CaseGuideGenerator(gateway).RegenerateEntriesAsync(draft, GuidePart.Facts, new[] { "F-001", "F-002" }, "", false);

        Assert.Equal(new[] { "F-002" }, skipped.SkippedEdited);
        Assert.Equal(new[] { "F-001" }, skipped.Regenerated);
        Assert.DoesNotContain(provider.Requests, r => KeyedProvider.FactIdOf(r) == "F-002");
        Assert.Equal("My own wording", edited.Title);
        Assert.True(edited.IsUserEdited);

        var replaced = await new CaseGuideGenerator(gateway).RegenerateEntriesAsync(draft, GuidePart.Facts, new[] { "F-002" }, "", true);

        Assert.Equal(new[] { "F-002" }, replaced.Regenerated);
        Assert.Empty(replaced.SkippedEdited);
        Assert.Equal("Rewritten F-002", edited.Title);
        Assert.False(edited.IsUserEdited);
    }

    [Fact]
    public async Task PartialFailure_KeepsOldTextAndReportsIt()
    {
        var draft = CaseTestData.SampleGuidedDraft();
        var guide = draft.Guide!;
        FlagForReview(guide, "F-002", "F-004");
        var before = Snapshot(guide.FindFact("F-004")!);
        var (gateway, provider) = Gateway();
        provider.Responses["F-004"] = FakeLlmProvider.Refused("I won't rewrite that.");

        var result = await new CaseGuideGenerator(gateway).RegenerateEntriesAsync(draft, GuidePart.Facts, new[] { "F-002", "F-004" }, "", false);

        Assert.Equal(new[] { "F-002" }, result.Regenerated);
        var failure = Assert.Single(result.Failed);
        Assert.Equal("F-004", failure.Id);
        Assert.Equal("The model declined: I won't rewrite that.", failure.Message);
        Assert.True(result.WasRefused);
        Assert.Contains("F-004 was not regenerated: The model declined: I won't rewrite that.", result.Notes);
        Assert.Equal(2, result.Attempted);

        // The failed fact is exactly as it was, still needing review; the other one was rewritten
        Assert.Equal(before, Snapshot(guide.FindFact("F-004")!));
        Assert.True(guide.FindFact("F-004")!.NeedsReview);
        Assert.False(guide.FindFact("F-002")!.NeedsReview);
    }

    [Fact]
    public async Task FatalError_StopsTheRunAndKeepsEveryFact()
    {
        var draft = CaseTestData.SampleGuidedDraft();
        var before = JsonSerializer.Serialize(draft.Guide, CaseDraft.JsonOptions);
        var revision = draft.Revision;
        var (gateway, provider) = Gateway();
        foreach (var id in AllFacts)
            provider.Responses[id] = FakeLlmProvider.Failed(LlmFailureKind.Auth, "Invalid API key");

        var result = await new CaseGuideGenerator(gateway).RegenerateEntriesAsync(draft, GuidePart.Facts, AllFacts, "", false);

        Assert.True(result.IsFatal);
        Assert.Contains("Invalid API key", result.FatalError);
        Assert.Empty(result.Regenerated);
        Assert.Equal(AllFacts.Length, result.Failed.Count);
        // No more calls than the ones already in flight when the key was rejected
        Assert.InRange(provider.Requests.Count, 1, CaseGuideGenerator.MaxParallelCalls);
        Assert.Equal(before, JsonSerializer.Serialize(draft.Guide, CaseDraft.JsonOptions));
        Assert.Equal(revision, draft.Revision);
    }

    [Fact]
    public async Task UserCancel_KeepsTheFactsThatCameBack()
    {
        var draft = CaseTestData.SampleGuidedDraft();
        var guide = draft.Guide!;
        FlagForReview(guide, AllFacts);
        var before = AllFacts.ToDictionary(id => id, id => Snapshot(guide.FindFact(id)!));
        var revision = draft.Revision;
        var (gateway, provider) = Gateway();
        using var cts = new CancellationTokenSource();
        // Cancel when the fourth call starts: at least one call has come back by then
        provider.OnSend = index =>
        {
            if (index == 3)
                cts.Cancel();
        };

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new CaseGuideGenerator(gateway).RegenerateEntriesAsync(draft, GuidePart.Facts, AllFacts, "", false, null, cts.Token));

        var rewritten = AllFacts.Where(id => guide.FindFact(id)!.Title == $"Rewritten {id}").ToList();
        Assert.NotEmpty(rewritten);
        Assert.True(rewritten.Count < AllFacts.Length);
        foreach (var id in AllFacts)
        {
            var fact = guide.FindFact(id)!;
            if (rewritten.Contains(id))
                Assert.False(fact.NeedsReview);
            else
                Assert.Equal(before[id], Snapshot(fact));
        }
        Assert.True(draft.Revision > revision);
    }

    [Fact]
    public async Task WidensActiveRangesForRewrittenDates()
    {
        var draft = CaseTestData.SampleGuidedDraft();
        var guide = draft.Guide!;
        var chen = guide.FindCast("CH-004")!;
        Assert.False(chen.IsUserEdited);
        var (gateway, provider) = Gateway();
        provider.Responses["F-001"] = FakeLlmProvider.Ok(EntryJson("F-001", date: "2023-06-01", actor: "CH-004"));

        await new CaseGuideGenerator(gateway).RegenerateEntriesAsync(draft, GuidePart.Facts, new[] { "F-001" }, "", false);

        Assert.Equal("2023-06-01", guide.FindFact("F-001")!.Date);
        Assert.Equal("2023-06-01", chen.ActiveFrom);
    }

    [Fact]
    public async Task UnknownIdsAreNotedAndNothingIsCalledForThem()
    {
        var draft = CaseTestData.SampleGuidedDraft();
        var (gateway, provider) = Gateway();

        var result = await new CaseGuideGenerator(gateway).RegenerateEntriesAsync(draft, GuidePart.Facts, new[] { "F-099", "F-001", "F-001" }, "", false);

        Assert.Equal(new[] { "F-001" }, result.Regenerated);
        Assert.Contains("F-099 no longer exists and was skipped.", result.Notes);
        Assert.Single(provider.Requests);
    }

    [Theory]
    [InlineData(GuidePart.Cast)]
    [InlineData(GuidePart.Values)]
    [InlineData(GuidePart.Issues)]
    public async Task OnlyFactsCanBeRegeneratedAsASelection(GuidePart part)
    {
        var draft = CaseTestData.SampleGuidedDraft();
        var (gateway, _) = Gateway();

        await Assert.ThrowsAsync<ArgumentException>(() =>
            new CaseGuideGenerator(gateway).RegenerateEntriesAsync(draft, part, new[] { "CH-001" }, "", false));
    }

    /// <summary>
    /// Answers each single-fact request with the response scripted for that fact id (calls finish in any order), through a
    /// FakeLlmProvider that records every request. Tracks how many calls are in flight at once.
    /// </summary>
    internal sealed class KeyedProvider : ILlmProvider
    {
        private readonly FakeLlmProvider _inner;
        private readonly int _delayMs;
        private int _current;
        private int _max;
        private int _sent;

        public KeyedProvider(FakeLlmProvider inner, int delayMs)
        {
            _inner = inner;
            _delayMs = delayMs;
        }

        public Dictionary<string, LlmProviderResponse> Responses { get; } = new();

        /// <summary>
        /// Invoked with the zero-based call index when a call starts, before its response.
        /// </summary>
        public Action<int>? OnSend { get; set; }

        public List<LlmProviderRequest> Requests
        {
            get
            {
                lock (_inner)
                    return _inner.Requests.ToList();
            }
        }

        public int MaxConcurrent => Volatile.Read(ref _max);

        public LlmProviderKind Kind => _inner.Kind;

        public int? MaxTokensCeiling => _inner.MaxTokensCeiling;

        public static string FactIdOf(LlmProviderRequest request) =>
            Regex.Match(request.UserPrompt, @"^Fact:\r?\n(F-\d{3}) \|", RegexOptions.Multiline).Groups[1].Value;

        public async Task<LlmProviderResponse> SendAsync(LlmProviderRequest request, CancellationToken ct = default)
        {
            OnSend?.Invoke(Interlocked.Increment(ref _sent) - 1);
            var now = Interlocked.Increment(ref _current);
            lock (_inner)
                _max = Math.Max(_max, now);
            try
            {
                if (_delayMs > 0)
                    await Task.Delay(_delayMs, ct);

                // FakeLlmProvider is not thread-safe: script this call's response and send under one lock
                lock (_inner)
                {
                    _inner.Responses.Enqueue(Responses[FactIdOf(request)]);
                    return _inner.SendAsync(request, ct).GetAwaiter().GetResult();
                }
            }
            finally
            {
                Interlocked.Decrement(ref _current);
            }
        }

        public Task<LlmKeyTestResult> TestKeyAsync(string modelId, CancellationToken ct = default) => _inner.TestKeyAsync(modelId, ct);
    }
}
