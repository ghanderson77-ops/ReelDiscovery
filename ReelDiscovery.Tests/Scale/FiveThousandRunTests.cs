using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using ReelDiscovery.Models;
using ReelDiscovery.Models.Case;
using ReelDiscovery.Models.Corpus;
using ReelDiscovery.Models.Evidence;
using ReelDiscovery.Services.Ai;
using ReelDiscovery.Services.Corpus;
using ReelDiscovery.Services.Evidence;
using ReelDiscovery.Services.Qa;
using ReelDiscovery.Tests.Case;
using ReelDiscovery.Tests.Corpus;
using ReelDiscovery.Tests.Evidence;
using ReelDiscovery.Tests.Fakes;
using Xunit.Abstractions;

namespace ReelDiscovery.Tests.Scale;

/// <summary>
/// One measured ~5,000-email run: outcome, case folder, wall time, memory and the timing of every thread-render request.
/// </summary>
public sealed class ScaleRun
{
    public GenerationRunResult Result { get; set; } = null!;
    public string Folder { get; set; } = string.Empty;
    public TimeSpan Duration { get; set; }
    public long PeakSampledWorkingSet { get; set; }
    public long ProcessPeakWorkingSet { get; set; }
    public long ManagedAfterCollect { get; set; }
    public long JournalBytes { get; set; }
    public List<RenderCall> RenderCalls { get; set; } = new();
    public FakeBatchTransport? Transport { get; set; }
}

/// <summary>A first-attempt thread render as the runner sent it: stage label, thread id and send time.</summary>
public sealed record RenderCall(string Stage, string ThreadId, TimeSpan At);

/// <summary>
/// Plans one ~5,000-email corpus with the real CorpusPlanner (fixed seed, fake planner responder) and runs it live once,
/// lazily, so the live and batch tests share the plan and the batch test can compare its answer key with the live one.
/// Temp case folders are deleted on dispose (T-07-72).
/// </summary>
public sealed class FiveThousandRuns : IAsyncLifetime
{
    public const int TotalEmails = 5_000;

    // Fixed planner seed so the plan is the same on every run
    private const int PlannerSeed = 2121;

    // Every run is guarded so a scheduling bug fails the test instead of hanging the suite
    public static readonly TimeSpan RunGuard = TimeSpan.FromMinutes(30);

    private readonly List<string> _folders = new();
    private readonly SemaphoreSlim _liveGate = new(1, 1);
    private ScaleRun? _live;

    public CaseDraft Draft { get; private set; } = null!;
    public CorpusPlan Plan { get; private set; } = null!;
    public TimeSpan PlanDuration { get; private set; }

    public async Task InitializeAsync()
    {
        var watch = Stopwatch.StartNew();
        var draft = CorpusPlannerTests.Draft(TotalEmails);
        var (gateway, fake) = CorpusTestData.CreateGateway();
        fake.Respond = CorpusPlannerTests.Responder();
        var planned = await new CorpusPlanner(gateway, new Random(PlannerSeed))
            .PlanAsync(draft, mediaAvailable: true, string.Empty)
            .WaitAsync(RunGuard);
        if (!planned.Succeeded || planned.Plan == null)
            throw new InvalidOperationException($"5K planner fixture failed: {planned.FailureMessage}");

        draft.Corpus ??= new CorpusState();
        draft.Corpus.Plan = planned.Plan;
        Draft = draft;
        Plan = planned.Plan;
        PlanDuration = watch.Elapsed;
    }

    public Task DisposeAsync()
    {
        lock (_folders)
        {
            foreach (var folder in _folders)
                EvidenceTestData.DeleteFolder(folder);
        }

        return Task.CompletedTask;
    }

    public string NewFolder()
    {
        var folder = EvidenceTestData.TempCaseFolder();
        lock (_folders)
            _folders.Add(folder);
        return folder;
    }

    /// <summary>The live run, made once on first use.</summary>
    public async Task<ScaleRun> LiveAsync()
    {
        await _liveGate.WaitAsync();
        try
        {
            return _live ??= await FiveThousandRunTests.RunAsync(this, batch: false);
        }
        finally
        {
            _liveGate.Release();
        }
    }
}

/// <summary>
/// Roadmap criterion 3 / OUT-03 / RUN-03 at scale: an in-process ~5,000-email run in live and batch mode finishes with
/// exact EML and answer-key counts, inside the memory budget, and end-of-run QA completes on it.
/// </summary>
[Trait("Category", "Scale")]
public partial class FiveThousandRunTests : IClassFixture<FiveThousandRuns>
{
    // [ASSUMED budget A5]
    private const long PeakWorkingSetBudget = 1_536L * 1024 * 1024;
    private const long ManagedBudget = 1_024L * 1024 * 1024;

    // Journal stop line (90% of the 200 MB replay cap)
    private const long JournalBudget = 180L * 1024 * 1024;

    // D-07: the quiet period must stay safely above the wave unit start-up spread (raise it if the spread passes 250 ms)
    private static readonly TimeSpan QuietPeriod = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan SpreadLimit = TimeSpan.FromMilliseconds(250);

    private readonly FiveThousandRuns _runs;
    private readonly ITestOutputHelper _output;

    public FiveThousandRunTests(FiveThousandRuns runs, ITestOutputHelper output)
    {
        _runs = runs;
        _output = output;
    }

    [GeneratedRegex(@"Thread id: (T-\d{4})")]
    private static partial Regex ThreadIdLine();

    private static string RenderSchema => JsonSchemaRegistry.GetSchemaName(typeof(ThreadRenderer.ThreadRenderDto));

    // ---------- run harness ----------

    /// <summary>
    /// Runs the shared plan in a fresh folder with the fake render/attachment responders and FakeMediaGenerator, sampling
    /// the working set every 500 ms. The provider is lean (no request log) so the measurement is the app, not the fake.
    /// </summary>
    internal static async Task<ScaleRun> RunAsync(FiveThousandRuns runs, bool batch)
    {
        var folder = runs.NewFolder();
        var respond = DistributionParityTests.Responder(runs.Plan, runs.Draft.Guide!, null);
        var story = CaseTestData.StoryModel();
        var volume = CaseTestData.VolumeModel();
        var clock = Stopwatch.StartNew();
        var calls = new ConcurrentQueue<RenderCall>();

        ILlmProvider provider = new RespondingProvider(respond);
        FakeBatchTransport? transport = null;
        BatchingLlmProvider? batching = null;
        if (batch)
        {
            transport = new FakeBatchTransport { Kind = LlmProviderKind.Anthropic, Respond = item => respond(item.Request) };
            var timings = new BatchTimings
            {
                QuietPeriod = QuietPeriod,
                PollInterval = TimeSpan.FromMilliseconds(20),
                SlowPollInterval = TimeSpan.FromMilliseconds(20),
                SlowAfter = TimeSpan.FromHours(1)
            };
            batching = new BatchingLlmProvider(provider, transport, new BatchLedger(folder), runs.Plan.PlanId,
                id => id == story.ModelId ? story : id == volume.ModelId ? volume : null, timings);
            provider = batching;
        }

        var gateway = new LlmGateway(new TimingProvider(provider, clock, calls), story, volume);
        var input = new GenerationRunInput
        {
            Draft = runs.Draft,
            Plan = runs.Plan,
            CaseFolder = folder,
            Media = new FakeMediaGenerator(),
            Batch = batching
        };

        // Working set sampled every 500 ms while the run is in flight
        long peak = 0;
        using var stopSampling = new CancellationTokenSource();
        var sampler = Task.Run(async () =>
        {
            using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(500));
            do
            {
                using var process = Process.GetCurrentProcess();
                Interlocked.Exchange(ref peak, Math.Max(Interlocked.Read(ref peak), process.WorkingSet64));
            }
            while (await WaitTick(timer, stopSampling.Token));
        });

        GenerationRunResult result;
        try
        {
            result = await new GenerationRunner(gateway).RunAsync(input, new GenerationSnapshot()).WaitAsync(FiveThousandRuns.RunGuard);
        }
        finally
        {
            stopSampling.Cancel();
            await sampler;
            batching?.Detach();
        }

        var duration = clock.Elapsed;
        long processPeak;
        using (var process = Process.GetCurrentProcess())
            processPeak = process.PeakWorkingSet64;
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        return new ScaleRun
        {
            Result = result,
            Folder = folder,
            Duration = duration,
            PeakSampledWorkingSet = Interlocked.Read(ref peak),
            ProcessPeakWorkingSet = processPeak,
            ManagedAfterCollect = GC.GetTotalMemory(false),
            JournalBytes = new FileInfo(GenerationJournal.PathFor(folder)).Length,
            RenderCalls = calls.ToList(),
            Transport = transport
        };
    }

    private static async Task<bool> WaitTick(PeriodicTimer timer, CancellationToken ct)
    {
        try
        {
            return await timer.WaitForNextTickAsync(ct);
        }
        catch (OperationCanceledException)
        {
            // The run ended: one last sample was taken, stop sampling
            return false;
        }
    }

    /// <summary>Answers every request through the responder; keeps no request log.</summary>
    private sealed class RespondingProvider : ILlmProvider
    {
        private readonly Func<LlmProviderRequest, LlmProviderResponse> _respond;

        public RespondingProvider(Func<LlmProviderRequest, LlmProviderResponse> respond)
        {
            _respond = respond;
        }

        public LlmProviderKind Kind => LlmProviderKind.Anthropic;

        public int? MaxTokensCeiling => null;

        public Task<LlmProviderResponse> SendAsync(LlmProviderRequest request, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult(_respond(request));
        }

        public Task<LlmKeyTestResult> TestKeyAsync(string modelId, CancellationToken ct = default) =>
            Task.FromResult(new LlmKeyTestResult { Succeeded = true, Message = "OK" });
    }

    /// <summary>Records when each thread render leaves the runner (stage label from the ambient BatchStage), then delegates.</summary>
    private sealed class TimingProvider : ILlmProvider
    {
        private readonly ILlmProvider _inner;
        private readonly Stopwatch _clock;
        private readonly ConcurrentQueue<RenderCall> _calls;

        public TimingProvider(ILlmProvider inner, Stopwatch clock, ConcurrentQueue<RenderCall> calls)
        {
            _inner = inner;
            _clock = clock;
            _calls = calls;
        }

        public LlmProviderKind Kind => _inner.Kind;

        public int? MaxTokensCeiling => _inner.MaxTokensCeiling;

        public Task<LlmProviderResponse> SendAsync(LlmProviderRequest request, CancellationToken ct = default)
        {
            if (request.SchemaName == RenderSchema)
            {
                var match = ThreadIdLine().Match(request.UserPrompt ?? string.Empty);
                _calls.Enqueue(new RenderCall(BatchStage.Current, match.Success ? match.Groups[1].Value : string.Empty, _clock.Elapsed));
            }

            return _inner.SendAsync(request, ct);
        }

        public Task<LlmKeyTestResult> TestKeyAsync(string modelId, CancellationToken ct = default) =>
            _inner.TestKeyAsync(modelId, ct);
    }

    // ---------- shared assertions ----------

    private int ExpectedEmlCount()
    {
        var folders = PlanNumbering.CustodianFolders(_runs.Draft.Guide!);
        return _runs.Plan.Threads.SelectMany(t => t.Messages).Sum(m => MailboxWriter.CopyPathsFor(m, folders).Count);
    }

    private static int EmlCount(string folder) =>
        Directory.GetFiles(Path.Combine(folder, PlanNumbering.MailboxesFolder), "*.eml", SearchOption.AllDirectories).Length;

    private static byte[] AnswerKey(string folder) => File.ReadAllBytes(Path.Combine(folder, AnswerKeyCsvWriter.FileName));

    private void Report(string mode, ScaleRun run)
    {
        const double Mb = 1024 * 1024;
        var messages = _runs.Plan.Threads.Sum(t => t.Messages.Count);
        _output.WriteLine($"{mode}: {_runs.Plan.Threads.Count} threads, {messages} emails, plan {_runs.PlanDuration.TotalSeconds:F1} s, " +
                          $"run {run.Duration.TotalSeconds:F1} s");
        _output.WriteLine($"{mode}: peak working set sampled {run.PeakSampledWorkingSet / Mb:F0} MB, process peak " +
                          $"{run.ProcessPeakWorkingSet / Mb:F0} MB, managed after GC {run.ManagedAfterCollect / Mb:F0} MB, " +
                          $"journal {run.JournalBytes / Mb:F1} MB");
    }

    private void AssertFinishedWithinBudget(ScaleRun run)
    {
        Assert.True(run.Result.Outcome == GenerationOutcome.Finished, $"outcome {run.Result.Outcome}: {run.Result.Message}");
        Assert.True(run.ProcessPeakWorkingSet < PeakWorkingSetBudget, $"process peak working set {run.ProcessPeakWorkingSet:N0} bytes");
        Assert.True(run.PeakSampledWorkingSet < PeakWorkingSetBudget, $"sampled peak working set {run.PeakSampledWorkingSet:N0} bytes");
        Assert.True(run.ManagedAfterCollect < ManagedBudget, $"managed memory after GC {run.ManagedAfterCollect:N0} bytes");
        Assert.True(run.JournalBytes < JournalBudget, $"journal {run.JournalBytes:N0} bytes");

        // Every planned custodian copy of every planned message is on disk
        Assert.Equal(ExpectedEmlCount(), EmlCount(run.Folder));

        // One key row per planned email and planned attachment; every one generated (media is available)
        var rows = GenerationRunnerTests.ParseCsv(Encoding.UTF8.GetString(AnswerKey(run.Folder)).TrimStart('﻿'))
            .Where(r => r.Length > 1)
            .ToList();
        var header = rows[0];
        var docType = Array.IndexOf(header, "Doc Type");
        var status = Array.IndexOf(header, "Status");
        var data = rows.Skip(1).ToList();
        var messages = _runs.Plan.Threads.SelectMany(t => t.Messages).ToList();
        var attachments = messages.Sum(m => m.Attachments.Count);
        Assert.Equal(messages.Count + attachments, data.Count);
        Assert.Equal(messages.Count, data.Count(r => r[docType] == AnswerKeyBuilder.DocTypeEmail));
        Assert.Equal(attachments, data.Count(r => r[docType] == AnswerKeyBuilder.DocTypeAttachment));
        var statuses = data.GroupBy(r => r[status]).ToDictionary(g => g.Key, g => g.Count());
        _output.WriteLine("Key statuses: " + string.Join(", ", statuses.Select(s => $"{s.Key} {s.Value}")));
        Assert.All(statuses.Keys, s => Assert.True(
            s == AnswerKeyBuilder.StatusGenerated || s == AnswerKeyBuilder.StatusGeneratedWithWarnings, $"unexpected status {s}"));
    }

    // ---------- tests ----------

    [Fact]
    public async Task Live_5K_FinishesWithExactCounts_InsideMemoryBudget_AndQaCompletes()
    {
        var run = await _runs.LiveAsync();
        Report("live", run);
        AssertFinishedWithinBudget(run);

        // End-of-run QA (scans + leakage) on the 5K folder
        var qa = Stopwatch.StartNew();
        var (scans, leakage) = await QaRunner.RunEndOfRunAsync(_runs.Draft, _runs.Plan, run.Folder, null)
            .WaitAsync(FiveThousandRuns.RunGuard);
        _output.WriteLine($"QA: {qa.Elapsed.TotalSeconds:F1} s");
        Assert.NotNull(scans);
        Assert.NotNull(leakage);
    }

    [Fact]
    public async Task Batch_5K_SameCountsAndAnswerKeyAsLive_OneRenderBatchPerWave()
    {
        var live = await _runs.LiveAsync();
        var run = await RunAsync(_runs, batch: true);
        Report("batch", run);
        AssertFinishedWithinBudget(run);

        Assert.Equal(EmlCount(live.Folder), EmlCount(run.Folder));
        Assert.Equal(AnswerKey(live.Folder), AnswerKey(run.Folder));

        // D-07: per wave label, exactly one submission made only of thread renders (attachments share the label, apart)
        var labelById = new BatchLedger(run.Folder).Replay().SubmittedById.Values
            .SelectMany(r => r.Items.Select(i => (i.CustomId, r.StageLabel)))
            .ToDictionary(p => p.CustomId, p => p.StageLabel, StringComparer.Ordinal);
        var renderSubmissions = run.Transport!.Submissions
            .Where(s => s.Count > 0 && s.All(i => i.Request.SchemaName == RenderSchema))
            .GroupBy(s => labelById[s[0].CustomId])
            .ToDictionary(g => g.Key, g => g.Count());
        var waves = WavePlanner.Plan(_runs.Plan);
        foreach (var wave in waves)
        {
            var label = $"wave {wave.Index}";
            Assert.True(renderSubmissions.TryGetValue(label, out var count) && count == 1,
                $"{label}: {(renderSubmissions.TryGetValue(label, out var c) ? c : 0)} thread-render submissions");
        }

        // Wave unit start-up spread: first to last thread render of one wave leaving the runner
        var spreads = run.RenderCalls
            .Where(c => c.Stage.StartsWith("wave ", StringComparison.Ordinal))
            .GroupBy(c => c.Stage)
            .Select(g => (Stage: g.Key, Units: g.Count(), Spread: g.Max(c => c.At) - g.Min(c => c.At)))
            .OrderByDescending(s => s.Spread)
            .ToList();
        var widest = spreads[0];
        _output.WriteLine($"{waves.Count} waves, {run.Transport.Submissions.Count} submissions; widest start-up spread " +
                          $"{widest.Spread.TotalMilliseconds:F0} ms ({widest.Stage}, {widest.Units} units); quiet period " +
                          $"{QuietPeriod.TotalMilliseconds:F0} ms");
        Assert.True(widest.Spread < SpreadLimit, $"{widest.Stage} start-up spread {widest.Spread.TotalMilliseconds:F0} ms");
    }
}
