using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using MimeKit;
using ReelDiscovery.Models;
using ReelDiscovery.Models.Case;
using ReelDiscovery.Models.Corpus;
using ReelDiscovery.Models.Evidence;
using ReelDiscovery.Services.Ai;
using ReelDiscovery.Services.Corpus;
using ReelDiscovery.Services.Evidence;
using ReelDiscovery.Tests.Case;
using ReelDiscovery.Tests.Fakes;

namespace ReelDiscovery.Tests.Evidence;

/// <summary>
/// D-05 / D-07 / D-09 / D-10 at the runner level: the same plan run live and through <see cref="BatchingLlmProvider"/>
/// writes the same journal (apart from Utc and cost fields), byte-identical EMLs and the same answer key; relevant waves
/// and repairs live at live parallelism, only non-relevant mail batched (amended D-07), restart resumes pending batches,
/// stale wave batches closed, orphans billed once.
/// </summary>
public partial class BatchParityTests : IDisposable
{
    // Every run is guarded so a scheduling bug fails the test instead of hanging the suite
    private static readonly TimeSpan RunGuard = TimeSpan.FromSeconds(120);

    private static readonly JsonSerializerOptions DtoOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    // Fields that legitimately differ between live and batch (time and batch-priced cost)
    private static readonly HashSet<string> VolatileFields = new(StringComparer.OrdinalIgnoreCase)
    {
        "Utc", "CostUsd", "CostDetail"
    };

    private readonly CaseDraft _draft;
    private readonly CorpusPlan _plan;
    private readonly AIModelConfig _story;
    private readonly AIModelConfig _volume;
    private readonly List<string> _folders = new();
    private readonly List<BatchingLlmProvider> _providers = new();

    public BatchParityTests()
    {
        (_draft, _plan) = AttachmentTestData.MixedCase();
        _story = CaseTestData.StoryModel();
        _volume = CaseTestData.VolumeModel();
        _story.InputTokenPricePerMillion = 3m;
        _story.OutputTokenPricePerMillion = 15m;
        _volume.InputTokenPricePerMillion = 1m;
        _volume.OutputTokenPricePerMillion = 5m;
        _story.BatchDiscountPercent = 50m;
        _volume.BatchDiscountPercent = 50m;
    }

    public void Dispose()
    {
        foreach (var provider in _providers)
            provider.Detach();
        foreach (var folder in _folders)
            EvidenceTestData.DeleteFolder(folder);
    }

    [GeneratedRegex(@"Thread id: (T-\d{4})")]
    private static partial Regex ThreadIdLine();

    [GeneratedRegex(@"Attachment id: (\S+)")]
    private static partial Regex AttachmentIdLine();

    private static string RenderSchema => JsonSchemaRegistry.GetSchemaName(typeof(ThreadRenderer.ThreadRenderDto));
    private static string SideWorldSchema => JsonSchemaRegistry.GetSchemaName(typeof(SideWorldBuilder.SideWorldDto));

    private CaseGuide Guide => _draft.Guide!;

    private string NewFolder()
    {
        var folder = EvidenceTestData.TempCaseFolder();
        _folders.Add(folder);
        return folder;
    }

    private static BatchTimings Fast() => new()
    {
        QuietPeriod = TimeSpan.FromMilliseconds(100),
        PollInterval = TimeSpan.FromMilliseconds(10),
        SlowPollInterval = TimeSpan.FromMilliseconds(20),
        SlowAfter = TimeSpan.FromSeconds(30)
    };

    private static string ThreadOf(LlmProviderRequest request) =>
        ThreadIdLine().Match(request.UserPrompt ?? string.Empty).Groups[1].Value;

    private static string? AttachmentIdOf(LlmProviderRequest request)
    {
        var match = AttachmentIdLine().Match(request.UserPrompt ?? string.Empty);
        return match.Success ? match.Groups[1].Value : null;
    }

    // A fresh responder per run: AttachmentResponder counts attempts, so a shared one would change later runs
    private Func<LlmProviderRequest, LlmProviderResponse> Responder(Func<LlmProviderRequest, LlmProviderResponse?>? intercept = null)
    {
        var inner = AttachmentResponder.Create(_plan, Guide);
        var sideWorld = JsonSerializer.Serialize(SideWorldDto(), DtoOptions);
        return request => intercept?.Invoke(request)
                          ?? (request.SchemaName == SideWorldSchema ? FakeLlmProvider.Ok(sideWorld, 700, 500) : inner(request));
    }

    private SideWorldBuilder.SideWorldDto SideWorldDto()
    {
        var dto = new SideWorldBuilder.SideWorldDto();
        var organizations = SideWorldBuilder.CoveredOrganizations(Guide);
        for (var i = 0; i < organizations.Count; i++)
        {
            dto.Organizations.Add(new SideWorldBuilder.SideWorldOrgDto
            {
                OrganizationId = organizations[i].Id,
                Projects = new()
                {
                    new() { Name = $"Lakeview Clinic Account {i + 1}", Topics = new() { "renewal pricing", "kickoff scheduling" } },
                    new() { Name = $"Northgate Office Fitout {i + 1}", Topics = new() { "furniture delivery", "badge access" } }
                }
            });
        }

        return dto;
    }

    private AIModelConfig? Price(string modelId) =>
        modelId == _story.ModelId ? _story : modelId == _volume.ModelId ? _volume : null;

    private sealed class Mode
    {
        public FakeLlmProvider Live { get; init; } = null!;
        public FakeBatchTransport? Transport { get; init; }
        public BatchingLlmProvider? Batch { get; init; }
        public LlmGateway Gateway { get; init; } = null!;
    }

    private Mode LiveMode(Func<LlmProviderRequest, LlmProviderResponse> respond)
    {
        var live = new FakeLlmProvider(LlmProviderKind.Anthropic) { Respond = respond };
        return new Mode { Live = live, Gateway = new LlmGateway(live, _story, _volume) };
    }

    // liveOverride wraps the recording fake (e.g. a concurrency probe); Mode.Live stays the fake for request assertions
    private Mode BatchMode(string folder, Func<LlmProviderRequest, LlmProviderResponse> respond, FakeBatchTransport? transport = null,
        Func<ILlmProvider, ILlmProvider>? liveOverride = null)
    {
        var live = new FakeLlmProvider(LlmProviderKind.Anthropic) { Respond = respond };
        transport ??= new FakeBatchTransport { Kind = LlmProviderKind.Anthropic };
        transport.Respond = item => respond(item.Request);
        var batch = new BatchingLlmProvider(liveOverride?.Invoke(live) ?? live, transport, new BatchLedger(folder), _plan.PlanId, Price, Fast());
        _providers.Add(batch);
        return new Mode { Live = live, Transport = transport, Batch = batch, Gateway = new LlmGateway(batch, _story, _volume) };
    }

    private GenerationRunInput Input(string folder, Mode mode, IMediaGenerator? media = null) =>
        new() { Draft = _draft, Plan = _plan, CaseFolder = folder, Media = media, Batch = mode.Batch };

    private static Task<GenerationRunResult> Run(GenerationRunner runner, GenerationRunInput input, GenerationSnapshot? snapshot = null,
        CancellationToken ct = default) =>
        runner.RunAsync(input, snapshot ?? new GenerationSnapshot(), null, ct).WaitAsync(RunGuard);

    private static List<JournalRecord> JournalRecords(string folder) =>
        File.ReadAllLines(GenerationJournal.PathFor(folder))
            .Where(l => l.Trim().Length > 0)
            .Select(l => JsonSerializer.Deserialize<JournalRecord>(l, CaseDraft.JsonOptions)!)
            .ToList();

    /// <summary>
    /// Journal lines without Utc and cost fields, sorted (units of a wave finish in any order in both modes).
    /// <paramref name="skipRunLines"/> drops RunStarted/RunEnded (a restart adds a pair).
    /// </summary>
    private static List<string> NormalizedJournal(string folder, bool skipRunLines = false)
    {
        var lines = new List<string>();
        foreach (var line in File.ReadAllLines(GenerationJournal.PathFor(folder)).Where(l => l.Trim().Length > 0))
        {
            var node = JsonNode.Parse(line)!;
            if (skipRunLines)
            {
                var record = JsonSerializer.Deserialize<JournalRecord>(line, CaseDraft.JsonOptions)!;
                if (record.Type is JournalRecordType.RunStarted or JournalRecordType.RunEnded)
                    continue;
            }

            Strip(node);
            lines.Add(node.ToJsonString());
        }

        lines.Sort(StringComparer.Ordinal);
        return lines;
    }

    private static void Strip(JsonNode? node)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (var key in obj.Select(p => p.Key).Where(VolatileFields.Contains).ToList())
                    obj.Remove(key);
                foreach (var (_, child) in obj)
                    Strip(child);
                break;
            case JsonArray array:
                foreach (var child in array)
                    Strip(child);
                break;
        }
    }

    private static Dictionary<string, byte[]> AllEmls(string folder) =>
        Directory.GetFiles(Path.Combine(folder, PlanNumbering.MailboxesFolder), "*.eml", SearchOption.AllDirectories)
            .ToDictionary(f => Path.GetRelativePath(folder, f), File.ReadAllBytes, StringComparer.OrdinalIgnoreCase);

    private static byte[] AnswerKey(string folder) => File.ReadAllBytes(Path.Combine(folder, AnswerKeyCsvWriter.FileName));

    private static void AssertSameOutput(string expectedFolder, string actualFolder)
    {
        Assert.Equal(NormalizedJournal(expectedFolder), NormalizedJournal(actualFolder));
        var expected = AllEmls(expectedFolder);
        var actual = AllEmls(actualFolder);
        Assert.Equal(expected.Keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase),
            actual.Keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase));
        foreach (var (path, bytes) in expected)
            AssertSameEml(bytes, actual[path], path);
        Assert.Equal(AnswerKey(expectedFolder), AnswerKey(actualFolder));
    }

    [GeneratedRegex(@"\bR[0-9a-f]{16}\b")]
    private static partial Regex PackageRelationshipId();

    /// <summary>
    /// Byte-identical EMLs, except inside Office attachments: the OpenXml SDK writes zip timestamps, a random
    /// core-properties part name and random relationship ids into every package, so two runs of the same mode differ there
    /// too. Every header, every other part byte and the Office content XML (ids masked) must match.
    /// </summary>
    private static void AssertSameEml(byte[] expected, byte[] actual, string path)
    {
        if (expected.AsSpan().SequenceEqual(actual))
            return;

        var a = MimeMessage.Load(new MemoryStream(expected));
        var b = MimeMessage.Load(new MemoryStream(actual));
        Assert.Equal(a.Headers.Select(h => h.ToString()), b.Headers.Select(h => h.ToString()));
        var partsA = a.BodyParts.ToList();
        var partsB = b.BodyParts.ToList();
        Assert.Equal(partsA.Count, partsB.Count);
        for (var i = 0; i < partsA.Count; i++)
        {
            Assert.Equal(partsA[i].Headers.Select(h => h.ToString()), partsB[i].Headers.Select(h => h.ToString()));
            var bytesA = Decoded(partsA[i]);
            var bytesB = Decoded(partsB[i]);
            if (bytesA.AsSpan().SequenceEqual(bytesB))
                continue;

            var name = (partsA[i] as MimePart)?.FileName ?? string.Empty;
            Assert.True(name.EndsWith(".docx", StringComparison.OrdinalIgnoreCase)
                        || name.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase)
                        || name.EndsWith(".pptx", StringComparison.OrdinalIgnoreCase), $"EML part differs: {path} / {name}");
            Assert.Equal(PackageContent(bytesA), PackageContent(bytesB));
        }
    }

    private static byte[] Decoded(MimeEntity entity)
    {
        using var stream = new MemoryStream();
        if (entity is MimePart part)
            part.Content?.DecodeTo(stream);
        else
            entity.WriteTo(stream);
        return stream.ToArray();
    }

    // Content parts of an Office package (no package plumbing), relationship ids masked
    private static List<(string Name, string Xml)> PackageContent(byte[] package)
    {
        using var zip = new ZipArchive(new MemoryStream(package), ZipArchiveMode.Read);
        return zip.Entries
            .Where(e => e.FullName.StartsWith("word/", StringComparison.Ordinal)
                        || e.FullName.StartsWith("xl/", StringComparison.Ordinal)
                        || e.FullName.StartsWith("ppt/", StringComparison.Ordinal))
            .OrderBy(e => e.FullName, StringComparer.Ordinal)
            .Select(e =>
            {
                using var reader = new StreamReader(e.Open(), Encoding.UTF8);
                return (e.FullName, PackageRelationshipId().Replace(reader.ReadToEnd(), "R#"));
            })
            .ToList();
    }

    private Dictionary<string, int> WaveOf() => WavePlanner.WaveIndexByThread(WavePlanner.Plan(_plan));

    // custom id -> stage label of the ledger batch it was submitted in
    private static Dictionary<string, string> LabelByCustomId(string folder)
    {
        var state = new BatchLedger(folder).Replay();
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var record in state.SubmittedById.Values)
        {
            foreach (var item in record.Items)
                map[item.CustomId] = record.StageLabel;
        }

        return map;
    }

    private HashSet<string> RelevantThreadIds() =>
        _plan.Threads.Where(WavePlanner.IsGeneratable).Select(t => t.Id).ToHashSet(StringComparer.Ordinal);

    private List<string> RelevantAttachmentIds() =>
        _plan.Threads.Where(WavePlanner.IsGeneratable)
            .SelectMany(t => t.Messages.Where(m => m != null).SelectMany(m => m.Attachments))
            .Select(a => a.DocId)
            .Where(id => !string.IsNullOrEmpty(id))
            .ToList();

    /// <summary>
    /// Live provider wrapper: calls whose ambient <see cref="BatchStage.Current"/> matches are held 25 ms and counted, so a
    /// test sees how many of them ran at once and which thread and schema each was for.
    /// </summary>
    private sealed class ConcurrencyProbe : ILlmProvider
    {
        private readonly ILlmProvider _inner;
        private readonly Func<string, bool> _match;
        private readonly object _lock = new();
        private readonly List<(string ThreadId, string Schema)> _calls = new();
        private int _inFlight;
        private int _max;

        public ConcurrencyProbe(ILlmProvider inner, Func<string, bool> match)
        {
            _inner = inner;
            _match = match;
        }

        public LlmProviderKind Kind => _inner.Kind;
        public int? MaxTokensCeiling => _inner.MaxTokensCeiling;
        public int MaxConcurrency => Volatile.Read(ref _max);

        public List<(string ThreadId, string Schema)> Calls
        {
            get
            {
                lock (_lock)
                    return _calls.ToList();
            }
        }

        public async Task<LlmProviderResponse> SendAsync(LlmProviderRequest request, CancellationToken ct = default)
        {
            if (!_match(BatchStage.Current))
                return await _inner.SendAsync(request, ct);

            lock (_lock)
                _calls.Add((ThreadOf(request), request.SchemaName));
            var now = Interlocked.Increment(ref _inFlight);
            int seen;
            while (now > (seen = Volatile.Read(ref _max)) && Interlocked.CompareExchange(ref _max, now, seen) != seen)
            {
            }

            try
            {
                await Task.Delay(25, ct);
                return await _inner.SendAsync(request, ct);
            }
            finally
            {
                Interlocked.Decrement(ref _inFlight);
            }
        }

        public Task<LlmKeyTestResult> TestKeyAsync(string modelId, CancellationToken ct = default) =>
            _inner.TestKeyAsync(modelId, ct);
    }

    // ---------- D-05 parity ----------

    [Fact]
    public async Task LiveAndBatch_SameJournalEmlsAndAnswerKey()
    {
        var liveFolder = NewFolder();
        var live = LiveMode(Responder());
        var liveResult = await Run(new GenerationRunner(live.Gateway), Input(liveFolder, live, new FakeMediaGenerator()));
        Assert.Equal(GenerationOutcome.Finished, liveResult.Outcome);

        var batchFolder = NewFolder();
        var batch = BatchMode(batchFolder, Responder());
        var snapshot = new GenerationSnapshot();
        var batchResult = await Run(new GenerationRunner(batch.Gateway), Input(batchFolder, batch, new FakeMediaGenerator()), snapshot);
        Assert.Equal(GenerationOutcome.Finished, batchResult.Outcome);

        AssertSameOutput(liveFolder, batchFolder);

        // Non-relevant first attempts really went through the provider batch; relevant threads never did (amended D-07)
        Assert.NotEmpty(batch.Transport!.Submissions);
        Assert.Contains(batch.Transport.Submissions.SelectMany(s => s), i => i.Request.SchemaName == RenderSchema);
        var relevant = RelevantThreadIds();
        Assert.DoesNotContain(batch.Transport.Submissions.SelectMany(s => s), i => relevant.Contains(ThreadOf(i.Request)));
        lock (batch.Live.Requests)
        {
            foreach (var id in relevant)
                Assert.Contains(batch.Live.Requests, r => r.SchemaName == RenderSchema && ThreadOf(r) == id);
        }
        var events = snapshot.Peek().Events;
        Assert.Contains(events, e => e.Kind == GenerationEventKind.BatchSubmitted);
        Assert.Contains(events, e => e.Kind == GenerationEventKind.BatchFinished);

        // Batch-priced spend is cheaper than the same tokens live
        var liveCost = JournalRecords(liveFolder).Sum(r => r.CostUsd);
        var batchCost = JournalRecords(batchFolder).Sum(r => r.CostUsd);
        Assert.True(batchCost < liveCost, $"batch {batchCost} vs live {liveCost}");
    }

    // ---------- amended D-07: relevant waves live, only non-relevant mail batched ----------

    [Fact]
    public async Task Batch_RelevantWavesRunLive_OnlyNonRelevantSubmitted()
    {
        var folder = NewFolder();
        var mode = BatchMode(folder, Responder());
        var waves = WavePlanner.Plan(_plan);
        var relevant = RelevantThreadIds();
        var checks = new List<string>();
        var runner = new GenerationRunner(mode.Gateway)
        {
            BeforeWaveHook = i =>
            {
                if (i == 0)
                    return Task.CompletedTask;

                // Wave i+1 starts only after every thread of the earlier waves is committed; nothing was batched yet
                var done = JournalRecords(folder).Where(r => r.Type == JournalRecordType.ThreadDone).Select(r => r.ThreadId).ToHashSet();
                foreach (var id in waves.Take(i).SelectMany(w => w.ThreadIds))
                {
                    if (!done.Contains(id))
                        checks.Add($"wave {i + 1} started before {id} was committed");
                }

                if (mode.Transport!.Submissions.Count > 0)
                    checks.Add($"a provider batch was submitted before wave {i + 1} started");

                return Task.CompletedTask;
            }
        };

        var result = await Run(runner, Input(folder, mode));

        Assert.Equal(GenerationOutcome.Finished, result.Outcome);
        Assert.Empty(checks);

        var submitted = mode.Transport!.Submissions.SelectMany(s => s).ToList();
        Assert.NotEmpty(submitted);
        Assert.All(LabelByCustomId(folder).Values, label => Assert.Equal("non-relevant mail", label));
        Assert.DoesNotContain(submitted, i => relevant.Contains(ThreadOf(i.Request)));
        foreach (var docId in RelevantAttachmentIds())
            Assert.DoesNotContain(submitted, i => AttachmentIdOf(i.Request) == docId);
    }

    [Fact]
    public async Task Batch_RelevantWaves_UseLiveParallelism()
    {
        var relevant = RelevantThreadIds();
        Assert.True(WavePlanner.Plan(_plan).Any(w => w.ThreadIds.Count(relevant.Contains) >= 4),
            "the fixture needs a wave with at least 4 relevant threads, or this test passes trivially");

        var folder = NewFolder();
        var probe = (ConcurrencyProbe?)null;
        var mode = BatchMode(folder, Responder(), liveOverride: inner =>
            probe = new ConcurrencyProbe(inner, stage => stage.StartsWith("wave ", StringComparison.Ordinal)));
        var result = await Run(new GenerationRunner(mode.Gateway), Input(folder, mode));

        Assert.Equal(GenerationOutcome.Finished, result.Outcome);
        var rendered = probe!.Calls.Where(c => c.Schema == RenderSchema).Select(c => c.ThreadId).ToHashSet(StringComparer.Ordinal);
        Assert.Equal(relevant.OrderBy(id => id, StringComparer.Ordinal),
            rendered.Where(relevant.Contains).OrderBy(id => id, StringComparer.Ordinal));
        Assert.InRange(probe.MaxConcurrency, 2, GenerationRunner.MaxParallelThreads);
    }

    [Fact]
    public async Task Batch_WholeBatchFails_LiveFallbackConcurrency_AtMostMaxParallel()
    {
        var liveFolder = NewFolder();
        var live = LiveMode(Responder());
        Assert.Equal(GenerationOutcome.Finished, (await Run(new GenerationRunner(live.Gateway), Input(liveFolder, live))).Outcome);

        var folder = NewFolder();
        var transport = new FakeBatchTransport { Kind = LlmProviderKind.Anthropic, FailWholeBatch = true };
        var probe = (ConcurrencyProbe?)null;
        var mode = BatchMode(folder, Responder(), transport, inner =>
            probe = new ConcurrencyProbe(inner, stage => stage == "non-relevant mail"));
        var result = await Run(new GenerationRunner(mode.Gateway), Input(folder, mode));

        Assert.Equal(GenerationOutcome.Finished, result.Outcome);
        Assert.NotEmpty(transport.Submissions);
        var nonRelevant = _plan.Threads.Count(t => t.Relevance != ThreadRelevance.Relevant && t.Messages.Any(m => m != null));
        Assert.True(probe!.Calls.Count >= nonRelevant, $"{probe.Calls.Count} live non-relevant calls for {nonRelevant} threads");
        Assert.InRange(probe.MaxConcurrency, 1, GenerationRunner.MaxParallelThreads);
        Assert.Equal(NormalizedJournal(liveFolder), NormalizedJournal(folder));
    }

    // ---------- revision 1 W9: repairs never wait in a batch ----------

    [Fact]
    public async Task Batch_AttachmentRepair_RunsLive_NeverSubmitted()
    {
        var folder = NewFolder();
        var other = AttachmentTestData.Thread(_plan, AttachmentTestData.OtherOrgDocThreadId);
        var refused = other.Messages.Single(m => m.Seq == 1).Attachments.First(a => a.Kind == AttachmentType.Excel).DocId;

        // First run live with the Excel refused: the thread is written and its attachment needs a repair
        var first = LiveMode(Responder(r => AttachmentIdOf(r) == refused ? FakeLlmProvider.Refused("cannot write that document") : null));
        await Run(new GenerationRunner(first.Gateway), Input(folder, first));

        var mode = BatchMode(folder, Responder());
        var result = await Run(new GenerationRunner(mode.Gateway), Input(folder, mode));

        Assert.Equal(GenerationOutcome.Finished, result.Outcome);
        Assert.DoesNotContain(mode.Transport!.Submissions.SelectMany(s => s), i => AttachmentIdOf(i.Request) == refused);
        lock (mode.Live.Requests)
            Assert.Contains(mode.Live.Requests, r => AttachmentIdOf(r) == refused);
        Assert.Contains(JournalRecords(folder), r => r.Type == JournalRecordType.AttachmentsRepaired);
    }

    // ---------- D-10 errored items run live ----------

    [Fact]
    public async Task Batch_ErroredItem_RendersLive_JournalMatchesLive()
    {
        var liveFolder = NewFolder();
        var live = LiveMode(Responder());
        await Run(new GenerationRunner(live.Gateway), Input(liveFolder, live));

        // The first-attempt render of the first non-relevant thread errors inside the batch (relevant waves are live)
        var thread = _plan.Threads.First(t => t.Relevance != ThreadRelevance.Relevant && t.Messages?.Any(m => m != null) == true).Id;
        LlmProviderRequest render;
        lock (live.Live.Requests)
            render = live.Live.Requests.First(r => ThreadOf(r) == thread && AttachmentIdOf(r) == null);
        var errored = RequestIdentity.CustomIdFor(LlmProviderKind.Anthropic, render);

        var batchFolder = NewFolder();
        var transport = new FakeBatchTransport { Kind = LlmProviderKind.Anthropic };
        transport.ErroredIds.Add(errored);
        var mode = BatchMode(batchFolder, Responder(), transport);
        var result = await Run(new GenerationRunner(mode.Gateway), Input(batchFolder, mode));

        Assert.Equal(GenerationOutcome.Finished, result.Outcome);
        Assert.Contains(transport.Submissions.SelectMany(s => s), i => i.CustomId == errored);
        lock (mode.Live.Requests)
            Assert.Contains(mode.Live.Requests, r => ThreadOf(r) == thread && AttachmentIdOf(r) == null);
        Assert.Equal(NormalizedJournal(liveFolder), NormalizedJournal(batchFolder));
    }

    // ---------- RUN-04 / D-09 restart ----------

    [Fact]
    public async Task Restart_CancelWhileNonRelevantWaits_ContinueResumes_NoResubmit_SameJournal()
    {
        // Reference: an uninterrupted batch run
        var reference = NewFolder();
        var full = BatchMode(reference, Responder());
        Assert.Equal(GenerationOutcome.Finished, (await Run(new GenerationRunner(full.Gateway), Input(reference, full))).Outcome);

        var folder = NewFolder();
        var transport = new FakeBatchTransport { Kind = LlmProviderKind.Anthropic, PollsUntilEnded = 1_000_000 };
        var first = BatchMode(folder, Responder(), transport);
        using var cts = new CancellationTokenSource();
        first.Batch!.StatusChanged += e =>
        {
            if (e.Kind == BatchStatusKind.Submitted && e.StageLabel == "non-relevant mail")
                cts.Cancel();
        };

        var cancelled = await Run(new GenerationRunner(first.Gateway), Input(folder, first), ct: cts.Token);
        Assert.Equal(GenerationOutcome.Cancelled, cancelled.Outcome);
        var pendingBatch = Assert.Single(transport.Submissions);
        var pendingIds = pendingBatch.Select(i => i.CustomId).ToHashSet(StringComparer.Ordinal);
        var pendingThreads = pendingBatch.Select(i => ThreadOf(i.Request)).Where(t => t.Length > 0).ToHashSet(StringComparer.Ordinal);
        Assert.NotEmpty(pendingThreads);
        Assert.Equal(1, BatchLedger.PendingBatchCount(folder, _plan.PlanId));
        var done = JournalRecords(folder).Where(r => r.Type == JournalRecordType.ThreadDone).Select(r => r.ThreadId).ToHashSet();
        Assert.All(RelevantThreadIds(), id => Assert.Contains(id, done));
        Assert.DoesNotContain(done, pendingThreads.Contains);

        // The provider finishes the batch while the app is closed; a new session continues over the same folder
        transport.PollsUntilEnded = 0;
        var second = BatchMode(folder, Responder(), transport);
        var snapshot = new GenerationSnapshot();
        var result = await Run(new GenerationRunner(second.Gateway), Input(folder, second), snapshot);

        Assert.Equal(GenerationOutcome.Finished, result.Outcome);
        Assert.DoesNotContain(transport.Submissions.Skip(1).SelectMany(s => s), i => pendingIds.Contains(i.CustomId));
        Assert.Contains(snapshot.Peek().Events, e => e.Kind == GenerationEventKind.BatchCollectedAfterRestart);
        Assert.Equal(0, BatchLedger.PendingBatchCount(folder, _plan.PlanId));
        Assert.Equal(NormalizedJournal(reference, skipRunLines: true), NormalizedJournal(folder, skipRunLines: true));
    }

    // ---------- amended D-07: a wave batch from an earlier build never blocks a run ----------

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FinishedRun_ClosesStaleWaveBatch_PendingCountZero(bool cancelFails)
    {
        // The first-attempt render of a wave-1 thread, exactly as the runner sends it
        var liveFolder = NewFolder();
        var live = LiveMode(Responder());
        await Run(new GenerationRunner(live.Gateway), Input(liveFolder, live));
        var thread = WavePlanner.Plan(_plan)[0].ThreadIds[0];
        LlmProviderRequest render;
        lock (live.Live.Requests)
            render = live.Live.Requests.First(r => r.SchemaName == RenderSchema && ThreadOf(r) == thread);
        render.AllowBatch = true;
        var staleId = RequestIdentity.CustomIdFor(LlmProviderKind.Anthropic, render);

        // Reference spend of a clean batch run (no stale batch)
        var reference = NewFolder();
        var clean = BatchMode(reference, Responder());
        Assert.Equal(GenerationOutcome.Finished, (await Run(new GenerationRunner(clean.Gateway), Input(reference, clean))).Outcome);
        var referenceEnded = JournalRecords(reference).Last(r => r.Type == JournalRecordType.RunEnded);

        // Session 1 (an earlier build): that render went into a "wave 1" provider batch that never ends
        var folder = NewFolder();
        var transport = new FakeBatchTransport { Kind = LlmProviderKind.Anthropic };
        transport.NeverEndIds.Add(staleId);
        var old = BatchMode(folder, Responder(), transport);
        var submitted = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        old.Batch!.StatusChanged += e =>
        {
            if (e.Kind == BatchStatusKind.Submitted)
                submitted.TrySetResult(e.BatchId);
        };
        using (var cts = new CancellationTokenSource())
        {
            Task<LlmProviderResponse> waiting;
            using (BatchStage.Enter("wave 1"))
                waiting = old.Batch.SendAsync(render, cts.Token);
            var staleBatch = await submitted.Task.WaitAsync(RunGuard);
            cts.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiting.WaitAsync(RunGuard));
            old.Batch.Detach();
            Assert.Equal(1, BatchLedger.PendingBatchCount(folder, _plan.PlanId));

            // Session 2: the run writes the thread live without waiting on the stale batch, then closes it
            if (cancelFails)
                transport.CancelFailure = new InvalidOperationException("provider down");
            var mode = BatchMode(folder, Responder(), transport);
            var result = await Run(new GenerationRunner(mode.Gateway), Input(folder, mode));

            Assert.Equal(GenerationOutcome.Finished, result.Outcome);
            lock (mode.Live.Requests)
                Assert.Contains(mode.Live.Requests, r => r.SchemaName == RenderSchema && ThreadOf(r) == thread);
            if (!cancelFails)
                Assert.Contains(staleBatch, transport.CancelledIds);
            Assert.Contains(staleBatch, new BatchLedger(folder).Replay().CancelledIds);
            Assert.Equal(0, BatchLedger.PendingBatchCount(folder, _plan.PlanId));
            Assert.Equal(0, mode.Batch!.PendingBatchCount);

            var ended = JournalRecords(folder).Last(r => r.Type == JournalRecordType.RunEnded);
            Assert.Equal(referenceEnded.CostUsd, ended.CostUsd);
            Assert.Equal(referenceEnded.InputTokens, ended.InputTokens);
            Assert.Equal(referenceEnded.OutputTokens, ended.OutputTokens);
        }
    }

    // ---------- D-10 orphan billing ----------

    [Fact]
    public async Task FinishedRun_BillsOrphanSpendOnce_InRunEnded()
    {
        var folder = NewFolder();

        // A collected result from an earlier session that no unit will ever ask for
        var ledger = new BatchLedger(folder);
        await ledger.AppendAsync(new BatchLedgerRecord
        {
            Type = BatchLedgerRecordType.Submitted,
            Utc = DateTime.UtcNow,
            Provider = LlmProviderKind.Anthropic,
            BatchId = "msgbatch_orphan",
            PlanId = _plan.PlanId,
            SubmittedUtc = DateTime.UtcNow,
            StageLabel = "wave 1",
            Items = new() { new BatchLedgerItem { CustomId = "orphan-1", Operation = "Thread", ModelId = _volume.ModelId } }
        });
        ledger.SaveResults("msgbatch_orphan", new List<BatchItemResult>
        {
            new()
            {
                CustomId = "orphan-1",
                Outcome = BatchItemOutcome.Succeeded,
                Response = new LlmProviderResponse
                {
                    Outcome = LlmOutcome.Succeeded, Text = "{}", InputTokens = 1_000_000, OutputTokens = 200_000, IsBatch = true
                }
            }
        });
        await ledger.AppendAsync(new BatchLedgerRecord
        {
            Type = BatchLedgerRecordType.Collected,
            Utc = DateTime.UtcNow,
            Provider = LlmProviderKind.Anthropic,
            BatchId = "msgbatch_orphan",
            PlanId = _plan.PlanId,
            Total = 1,
            Done = 1
        });
        var orphanCost = _volume.CalculateCost(new TokenUsage { InputTokens = 1_000_000, OutputTokens = 200_000, IsBatch = true }).TotalUsd;
        Assert.True(orphanCost > 0);

        var mode = BatchMode(folder, Responder());
        var result = await Run(new GenerationRunner(mode.Gateway), Input(folder, mode));

        Assert.Equal(GenerationOutcome.Finished, result.Outcome);
        var ended = JournalRecords(folder).Last(r => r.Type == JournalRecordType.RunEnded);
        Assert.Equal(1_000_000, ended.InputTokens);
        Assert.Equal(200_000, ended.OutputTokens);
        Assert.Equal(orphanCost, ended.CostUsd);
        Assert.Equal(orphanCost, ended.CostDetail!.TotalUsd);

        // A second Continue on the finished folder adds nothing
        var again = BatchMode(folder, Responder());
        await Run(new GenerationRunner(again.Gateway), Input(folder, again));
        var last = JournalRecords(folder).Last(r => r.Type == JournalRecordType.RunEnded);
        Assert.Equal(0m, last.CostUsd);
        Assert.Equal(0, last.InputTokens);
        Assert.Equal(2, JournalRecords(folder).Count(r => r.Type == JournalRecordType.RunEnded));
    }

    // ---------- live mode unchanged ----------

    [Fact]
    public void LiveConstants_Unchanged_BatchConstants()
    {
        Assert.Equal(3, GenerationRunner.MaxParallelThreads);
        Assert.Equal(50, GenerationRunner.FillerChunkSize);
        Assert.Equal(4, GenerationRunner.MaxConcurrentRepairs);
        Assert.Equal(2_000, GenerationRunner.BatchFillerChunkSize);
        Assert.True(GenerationRunner.BatchMaxParallel >= 10_000);
    }

    [Fact]
    public async Task LiveRun_NoBatch_NeverSetsBatchFields()
    {
        var folder = NewFolder();
        var live = LiveMode(Responder());
        var snapshot = new GenerationSnapshot();
        var result = await Run(new GenerationRunner(live.Gateway), Input(folder, live), snapshot);

        Assert.Equal(GenerationOutcome.Finished, result.Outcome);
        var view = snapshot.Peek();
        Assert.DoesNotContain(view.Events, e => e.Kind >= GenerationEventKind.BatchSubmitted);
        Assert.All(view.Waves, w => Assert.False(w.BatchWaiting));
        Assert.False(File.Exists(BatchLedger.PathFor(folder)));
    }
}
