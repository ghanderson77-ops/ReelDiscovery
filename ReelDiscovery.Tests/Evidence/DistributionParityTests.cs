using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using MimeKit;
using ReelDiscovery.Helpers;
using ReelDiscovery.Models;
using ReelDiscovery.Models.Case;
using ReelDiscovery.Models.Corpus;
using ReelDiscovery.Models.Evidence;
using ReelDiscovery.Services.Ai;
using ReelDiscovery.Services.Corpus;
using ReelDiscovery.Services.Evidence;
using ReelDiscovery.Tests.Corpus;
using ReelDiscovery.Tests.Fakes;
using Xunit.Abstractions;

namespace ReelDiscovery.Tests.Evidence;

/// <summary>
/// One finished fake run: the plan, its case folder, every provider request and the code-side assignments.
/// </summary>
public sealed class ParityRun
{
    public string Name { get; set; } = string.Empty;
    public CaseDraft Draft { get; set; } = null!;
    public CorpusPlan Plan { get; set; } = null!;
    public string Folder { get; set; } = string.Empty;
    public GenerationRunResult Result { get; set; } = null!;
    public List<LlmProviderRequest> Requests { get; set; } = new();
    public Dictionary<string, ThreadAssignment> RelevantAssignments { get; set; } = new();
    public Dictionary<string, ThreadAssignment> FillerAssignments { get; set; } = new();
}

/// <summary>
/// Builds and runs both parity fixtures once per test class: a synthetic mixed plan (MixedCase plus deterministic clones)
/// and a plan made by the real Phase 4 CorpusPlanner. Temp case folders are deleted on dispose.
/// </summary>
public sealed class ParityRuns : IAsyncLifetime
{
    public const string Synthetic = "Synthetic";
    public const string ProductionPlanner = "ProductionPlanner";

    // Fixed planner seed so the production-planner plan is the same on every run
    private const int PlannerSeed = 2106;

    private readonly Dictionary<string, ParityRun> _runs = new(StringComparer.Ordinal);

    public ParityRun this[string name] => _runs[name];

    public async Task InitializeAsync()
    {
        var (syntheticDraft, syntheticPlan) = DistributionParityTests.BuildSyntheticPlan(40, 60);
        _runs[Synthetic] = await DistributionParityTests.RunAsync(Synthetic, syntheticDraft, syntheticPlan);

        var draft = CorpusPlannerTests.Draft();
        // The real Phase 4 planner with the CorpusPlannerTests responder; its seeded generator is the planner's own
        // input (the synthetic plan above uses index arithmetic only)
        var (gateway, fake) = CorpusTestData.CreateGateway();
        var planner = new CorpusPlanner(gateway, new(PlannerSeed));
        fake.Respond = CorpusPlannerTests.Responder();
        var planned = await planner.PlanAsync(draft, mediaAvailable: true, string.Empty)
            .WaitAsync(DistributionParityTests.RunGuard);
        if (!planned.Succeeded || planned.Plan == null)
            throw new InvalidOperationException($"Production planner fixture failed: {planned.FailureMessage}");

        draft.Corpus ??= new CorpusState();
        draft.Corpus.Plan = planned.Plan;
        _runs[ProductionPlanner] = await DistributionParityTests.RunAsync(ProductionPlanner, draft, planned.Plan);
    }

    public Task DisposeAsync()
    {
        foreach (var run in _runs.Values)
            EvidenceTestData.DeleteFolder(run.Folder);
        return Task.CompletedTask;
    }
}

/// <summary>
/// Code-side parity only. Model-written length and style parity is measured on the live Office-case run in 06-25
/// (06-HUMAN-UAT.md); D-12 strict means no V8 retry pulls filler into band.
/// Every fact runs on a synthetic plan and on a plan built by the real CorpusPlanner (NOISE-05, ROADMAP criterion 4).
/// </summary>
public partial class DistributionParityTests : IClassFixture<ParityRuns>
{
    // Every run is guarded so a scheduling bug fails the suite instead of hanging it
    internal static readonly TimeSpan RunGuard = TimeSpan.FromSeconds(90);

    private static readonly JsonSerializerOptions DtoOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private static readonly NonRelevantCategory[] BusinessCategories =
    {
        NonRelevantCategory.DailyBusiness, NonRelevantCategory.Ancillary, NonRelevantCategory.Decoy
    };

    // Disclaimer image class written by HtmlEmailFormatter.WrapInTemplate
    private const string DisclaimerMarker = "disclaimer-banner";

    private readonly ParityRuns _runs;
    private readonly ITestOutputHelper _output;

    public DistributionParityTests(ParityRuns runs, ITestOutputHelper output)
    {
        _runs = runs;
        _output = output;
    }

    [GeneratedRegex(@"Thread id: (T-\d{4})")]
    private static partial Regex ThreadIdLine();

    [GeneratedRegex(@"^msg (\d+) \|[^\r\n]*", RegexOptions.Multiline)]
    private static partial Regex MessageLine();

    [GeneratedRegex(@"^[0-9a-f]{24}@[A-Za-z0-9.-]+$")]
    private static partial Regex MessageIdShape();

    [GeneratedRegex(@"^(Document|Spreadsheet|Presentation|Photo|Image|Invite|Voicemail)( \d+)?\.", RegexOptions.IgnoreCase)]
    private static partial Regex CodeDefaultName();

    [GeneratedRegex(@"^(?:(?:RE|FW|Fwd):\s*)+", RegexOptions.IgnoreCase)]
    private static partial Regex ReplyPrefix();

    private static string RenderSchema => JsonSchemaRegistry.GetSchemaName(typeof(ThreadRenderer.ThreadRenderDto));
    private static string FillerSchema => JsonSchemaRegistry.GetSchemaName(typeof(FillerRenderer.FillerThreadDto));
    private static string SideWorldSchema => JsonSchemaRegistry.GetSchemaName(typeof(SideWorldBuilder.SideWorldDto));

    private enum Shape { Root, Reply, Forward }

    // ---------- fixtures ----------

    /// <summary>
    /// MixedCase plus deterministic clones (index arithmetic only) up to <paramref name="relevant"/> relevant and
    /// <paramref name="nonRelevant"/> non-relevant business threads: 1-5 messages, mixed Reply/ReplyAll/Forward, 0-3 Cc,
    /// every fourth clone privileged, an attachment on every fifth message of each population. Renumbered with
    /// PlanNumbering so ids, DocIDs and paths use the production formats.
    /// </summary>
    internal static (CaseDraft Draft, CorpusPlan Plan) BuildSyntheticPlan(int relevant, int nonRelevant)
    {
        var (draft, plan) = AttachmentTestData.MixedCase();
        var guide = draft.Guide!;

        bool CastOnly(PlannedThread t) => t.Messages.Count > 0 && guide.FindCast(t.Messages[0].FromId) != null
            && t.Messages[0].ToIds.All(id => guide.FindCast(id) != null);

        var relevantPool = plan.Threads
            .Where(t => t.Relevance == ThreadRelevance.Relevant && !t.IsPrivileged && !HasVoicemail(t) && CastOnly(t))
            .OrderBy(t => t.Id, StringComparer.Ordinal)
            .ToList();
        var relevantPrivileged = plan.Threads.First(t => t.Relevance == ThreadRelevance.Relevant && t.IsPrivileged && CastOnly(t));
        var businessPool = plan.Threads
            .Where(t => t.Relevance != ThreadRelevance.Relevant && IsBusiness(t) && !t.IsPrivileged && !HasVoicemail(t) && CastOnly(t))
            .OrderBy(t => t.Id, StringComparer.Ordinal)
            .ToList();
        var businessPrivileged = plan.Threads.First(t => t.Relevance != ThreadRelevance.Relevant && IsBusiness(t) && t.IsPrivileged && CastOnly(t));

        var relevantClones = new List<PlannedThread>();
        var relevantNeeded = relevant - plan.Threads.Count(t => t.Relevance == ThreadRelevance.Relevant);
        for (var c = 0; c < relevantNeeded; c++)
        {
            var privileged = c % 4 == 0;
            var source = privileged ? relevantPrivileged : relevantPool[c % relevantPool.Count];
            relevantClones.Add(CloneThread(guide, source, $"C-R{c:000}", c, privileged));
        }

        var businessClones = new List<PlannedThread>();
        var businessNeeded = nonRelevant - plan.Threads.Count(t => t.Relevance != ThreadRelevance.Relevant && IsBusiness(t));
        for (var c = 0; c < businessNeeded; c++)
        {
            var privileged = c % 4 == 0;
            var source = privileged ? businessPrivileged : businessPool[c % businessPool.Count];
            businessClones.Add(CloneThread(guide, source, $"C-N{c:000}", c, privileged));
        }

        AddAttachments(relevantClones, relevant: true);
        AddAttachments(businessClones, relevant: false);

        plan.Threads.AddRange(relevantClones);
        plan.Threads.AddRange(businessClones);
        PlanNumbering.Assign(plan, guide);
        draft.Corpus!.Plan = plan;
        return (draft, plan);
    }

    private static PlannedThread CloneThread(CaseGuide guide, PlannedThread source, string id, int c, bool privileged)
    {
        var root = source.Messages.OrderBy(m => m.Seq).First();
        var relevant = source.Relevance == ThreadRelevance.Relevant;
        var thread = new PlannedThread
        {
            Id = id,
            Relevance = source.Relevance,
            Category = source.Category,
            IsPrivileged = privileged,
            FactIds = new List<string>(source.FactIds),
            IssueIds = new List<string>(source.IssueIds),
            Subject = $"Follow-up {c + 1}: {source.Subject}",
            Purpose = source.Purpose,
            DecoyAngle = source.DecoyAngle,
            MustNotMention = new List<string>(source.MustNotMention ?? new List<string>())
        };

        var count = 1 + c % 5;
        var factId = source.FactIds.FirstOrDefault() ?? string.Empty;
        var sentAt = root.SentAt.AddMinutes(13 * (c + 1));
        for (var seq = 1; seq <= count; seq++)
        {
            var parent = seq == 1 ? null : thread.Messages[seq - 2];
            var kind = seq switch
            {
                1 => MessageKind.New,
                2 => c % 3 == 1 ? MessageKind.Forward : MessageKind.Reply,
                3 => MessageKind.ReplyAll,
                4 => MessageKind.Forward,
                _ => MessageKind.Reply
            };

            string from;
            List<string> to;
            if (parent == null)
            {
                from = root.FromId;
                to = new List<string> { root.ToIds[0] };
            }
            else if (kind == MessageKind.Forward)
            {
                from = parent.ToIds[0];
                to = new List<string> { Pool(guide, sentAt, new[] { from, parent.FromId }, c + seq).First() };
            }
            else
            {
                from = parent.ToIds[0];
                to = new List<string> { parent.FromId };
            }

            var ccCount = (c + seq) % 4;
            var cc = Pool(guide, sentAt, new[] { from }.Concat(to).ToArray(), c * 3 + seq).Take(ccCount).ToList();
            var zone = BusinessCalendar.Resolve(guide.FindOrganization(guide.FindCast(from)!.OrganizationId));
            var own = relevant && (seq == 1 || seq % 2 == 1 || kind == MessageKind.Forward);

            thread.Messages.Add(new PlannedMessage
            {
                Id = $"{id}-M{seq:00}",
                Seq = seq,
                ReplyToSeq = seq - 1,
                Kind = kind,
                FromId = from,
                ToIds = to,
                CcIds = cc,
                SentAt = TimeZoneInfo.ConvertTime(sentAt, zone),
                TimeZoneId = zone.Id,
                FactIds = own && factId.Length > 0 ? new List<string> { factId } : new List<string>(),
                Basis = !relevant ? RelevanceBasis.None : own ? RelevanceBasis.Own : RelevanceBasis.Quoted
            });
            sentAt = sentAt.AddMinutes(90);
        }

        return thread;
    }

    // Cast members active on the day, ordinal by id, rotated by offset, without the excluded ids
    private static List<string> Pool(CaseGuide guide, DateTimeOffset at, string[] exclude, int offset)
    {
        var day = at.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var pool = guide.Cast
            .Where(m => string.CompareOrdinal(m.ActiveFrom, day) <= 0 && string.CompareOrdinal(day, m.ActiveTo) <= 0)
            .Select(m => m.Id)
            .Where(memberId => !exclude.Contains(memberId, StringComparer.Ordinal))
            .OrderBy(memberId => memberId, StringComparer.Ordinal)
            .ToList();
        return pool.Skip(offset % pool.Count).Concat(pool.Take(offset % pool.Count)).ToList();
    }

    // One attachment on every fifth message: Word, Excel, PowerPoint, then an inline image. Relevant files carry
    // planned titles; non-relevant files carry the code-default names the planner writes (filler titles replace them)
    private static void AddAttachments(List<PlannedThread> threads, bool relevant)
    {
        var kinds = new[] { AttachmentType.Word, AttachmentType.Excel, AttachmentType.PowerPoint, AttachmentType.Image };
        var k = 0;
        foreach (var message in threads.SelectMany(t => t.Messages))
        {
            if (k % 5 == 0)
            {
                var n = k / 5;
                var kind = kinds[n % kinds.Length];
                var (extension, stem, codeDefault) = kind switch
                {
                    AttachmentType.Word => (".docx", "Coordination Memo", "Document"),
                    AttachmentType.Excel => (".xlsx", "Cost Tracker", "Spreadsheet"),
                    AttachmentType.PowerPoint => (".pptx", "Status Deck", "Presentation"),
                    _ => (".png", "Site Photo", "Photo")
                };
                var title = relevant ? $"{stem} R{n + 1:00}" : string.Empty;
                message.Attachments.Add(new PlannedAttachment
                {
                    Kind = kind,
                    Title = title,
                    FileName = relevant ? title + extension : $"{codeDefault} {n + 1}{extension}",
                    Description = relevant ? $"{title} for the thread." : $"{stem} for the thread.",
                    IsInline = kind == AttachmentType.Image
                });
            }

            k++;
        }
    }

    internal static async Task<ParityRun> RunAsync(string name, CaseDraft draft, CorpusPlan plan,
        Func<string, int, string?>? overrideJson = null)
    {
        var folder = EvidenceTestData.TempCaseFolder();
        var (gateway, fake) = CorpusTestData.CreateGateway();
        fake.Respond = Responder(plan, draft.Guide!, overrideJson);

        var input = new GenerationRunInput { Draft = draft, Plan = plan, CaseFolder = folder, Media = new FakeMediaGenerator() };
        var result = await new GenerationRunner(gateway).RunAsync(input, new GenerationSnapshot()).WaitAsync(RunGuard);

        List<LlmProviderRequest> requests;
        lock (fake.Requests)
            requests = fake.Requests.ToList();

        return new ParityRun
        {
            Name = name,
            Draft = draft,
            Plan = plan,
            Folder = folder,
            Result = result,
            Requests = requests,
            RelevantAssignments = RenderAssignments.Compute(plan),
            FillerAssignments = FillerAssignments.Compute(plan)
        };
    }

    // AttachmentResponder (renders, filler, attachments, voices) plus a deterministic side world
    private static Func<LlmProviderRequest, LlmProviderResponse> Responder(CorpusPlan plan, CaseGuide guide,
        Func<string, int, string?>? overrideJson)
    {
        var inner = AttachmentResponder.Create(plan, guide, overrideJson);
        var dto = new SideWorldBuilder.SideWorldDto();
        var organizations = SideWorldBuilder.CoveredOrganizations(guide);
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

        var sideWorld = JsonSerializer.Serialize(dto, DtoOptions);
        return request => request.SchemaName == SideWorldSchema ? FakeLlmProvider.Ok(sideWorld, 700, 500) : inner(request);
    }

    // ---------- population helpers ----------

    private ParityRun Finished(string name)
    {
        var run = _runs[name];
        Assert.True(run.Result.Outcome == GenerationOutcome.Finished, $"{name}: outcome {run.Result.Outcome} {run.Result.Message}");
        return run;
    }

    private static bool IsBusiness(PlannedThread thread) => BusinessCategories.Contains(thread.Category);

    private static bool HasVoicemail(PlannedThread thread) =>
        thread.Messages.SelectMany(m => m.Attachments).Any(a => a.Kind == AttachmentType.Voicemail);

    // Same rule as EvidencePrompts / FillerPrompts: a single-message voicemail thread's body is the spoken transcript
    private static bool IsVoicemailMessage(PlannedThread thread, PlannedMessage message) =>
        thread.Messages.Count == 1 && message.Attachments.Any(a => a.Kind == AttachmentType.Voicemail);

    private static List<PlannedThread> RelevantThreads(ParityRun run) =>
        run.Plan.Threads.Where(t => run.RelevantAssignments.ContainsKey(t.Id)).ToList();

    private static List<PlannedThread> BusinessThreads(ParityRun run) =>
        run.Plan.Threads.Where(t => t.Relevance != ThreadRelevance.Relevant && IsBusiness(t) && run.FillerAssignments.ContainsKey(t.Id)).ToList();

    private static IEnumerable<(PlannedThread Thread, PlannedMessage Message)> MessagesOf(IEnumerable<PlannedThread> threads) =>
        threads.SelectMany(t => t.Messages.OrderBy(m => m.Seq).Select(m => (t, m)));

    private static ThreadAssignment AssignmentOf(ParityRun run, PlannedThread thread) =>
        run.RelevantAssignments.TryGetValue(thread.Id, out var relevant) ? relevant : run.FillerAssignments[thread.Id];

    private static string EmlPath(ParityRun run, PlannedMessage message) =>
        Path.Combine(run.Folder, MailboxWriter.CopyPathsFor(message, PlanNumbering.CustodianFolders(run.Draft.Guide!))[0]);

    private static MimeMessage Load(ParityRun run, PlannedMessage message) => MimeMessage.Load(EmlPath(run, message));

    private static Shape ShapeOf(PlannedMessage message) => message.Kind switch
    {
        MessageKind.Reply or MessageKind.ReplyAll => Shape.Reply,
        MessageKind.Forward => Shape.Forward,
        _ => Shape.Root
    };

    private static string ThreadOf(LlmProviderRequest request) =>
        ThreadIdLine().Match(request.UserPrompt ?? string.Empty).Groups[1].Value;

    private static int[] BandCounts(IEnumerable<MessageAssignment> messages)
    {
        var list = messages.ToList();
        return RenderAssignments.LengthBands.Select(b => list.Count(m => m.Length == b.Band)).ToArray();
    }

    // DocID -> row (header name -> value) from answer-key.csv
    private static List<Dictionary<string, string>> KeyRows(ParityRun run)
    {
        var text = File.ReadAllText(Path.Combine(run.Folder, AnswerKeyCsvWriter.FileName), Encoding.UTF8);
        var rows = GenerationRunnerTests.ParseCsv(text.TrimStart('﻿'));
        var header = rows[0];
        return rows.Skip(1)
            .Where(r => r.Length == header.Length)
            .Select(r => header.Select((h, i) => (h, r[i])).ToDictionary(x => x.h, x => x.Item2, StringComparer.Ordinal))
            .ToList();
    }

    // The model body inside the EML text: after the privilege banner, before the fake's "Thanks," sign-off
    private static string ModelBody(MimeMessage eml)
    {
        var text = (eml.TextBody ?? string.Empty).Replace("\r\n", "\n");
        if (text.StartsWith(HtmlEmailFormatter.PrivilegedBannerText, StringComparison.Ordinal))
            text = text[HtmlEmailFormatter.PrivilegedBannerText.Length..].TrimStart('\n');
        var end = text.IndexOf("\n\nThanks,", StringComparison.Ordinal);
        return (end >= 0 ? text[..end] : text).Trim();
    }

    private static int Words(string text) => text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;

    // ---------- facts ----------

    /// <summary>
    /// Proves: code-controlled input. Relevant (RenderAssignments) and non-relevant business (FillerAssignments) messages
    /// get the 55/35/10 Short/Medium/Long mix within one message per band, and the populations' band shares differ by at
    /// most 0.05 (D-07, D-11).
    /// </summary>
    [Theory]
    [InlineData(ParityRuns.Synthetic)]
    [InlineData(ParityRuns.ProductionPlanner)]
    public void Parity_LengthBands(string fixture)
    {
        var run = Finished(fixture);
        var shares = RenderAssignments.LengthBands.Select(b => b.Share).ToList();
        var relevant = BandCounts(RelevantThreads(run).SelectMany(t => run.RelevantAssignments[t.Id].Messages));
        var business = BandCounts(BusinessThreads(run).SelectMany(t => run.FillerAssignments[t.Id].Messages));
        _output.WriteLine($"{fixture}: relevant S/M/L {string.Join("/", relevant)}; business S/M/L {string.Join("/", business)}");

        Assert.True(relevant.Sum() > 0 && business.Sum() > 0, "both populations need messages");
        foreach (var counts in new[] { relevant, business })
        {
            var expected = Apportion.Largest(counts.Sum(), shares);
            for (var b = 0; b < counts.Length; b++)
                Assert.True(Math.Abs(counts[b] - expected[b]) <= 1,
                    $"{fixture} {RenderAssignments.LengthBands[b].Band}: {counts[b]} vs expected {expected[b]}");
        }

        for (var b = 0; b < relevant.Length; b++)
        {
            var difference = Math.Abs(relevant[b] / (double)relevant.Sum() - business[b] / (double)business.Sum());
            Assert.True(difference <= 0.05, $"{fixture} {RenderAssignments.LengthBands[b].Band} share differs by {difference:F3}");
        }
    }

    /// <summary>
    /// Proves: code-controlled input. Every ThreadRenderDto and FillerThreadDto request carries, on each non-voicemail
    /// message line, the "length: {min}-{max} words" token of that message's assigned band.
    /// </summary>
    [Theory]
    [InlineData(ParityRuns.Synthetic)]
    [InlineData(ParityRuns.ProductionPlanner)]
    public void Parity_LengthTokens(string fixture)
    {
        var run = Finished(fixture);
        var threads = run.Plan.Threads.ToDictionary(t => t.Id, StringComparer.Ordinal);
        var requests = run.Requests.Where(r => r.SchemaName == RenderSchema || r.SchemaName == FillerSchema).ToList();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var request in requests)
        {
            var thread = threads[ThreadOf(request)];
            var assignment = AssignmentOf(run, thread);
            var lines = MessageLine().Matches(request.UserPrompt ?? string.Empty)
                .ToDictionary(m => int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture), m => m.Value);
            seen.Add(thread.Id);

            foreach (var message in thread.Messages)
            {
                Assert.True(lines.TryGetValue(message.Seq, out var line), $"{thread.Id} msg {message.Seq} has no prompt line");
                if (IsVoicemailMessage(thread, message))
                {
                    Assert.Contains("voicemail:", line!);
                    continue;
                }

                var band = RenderAssignments.Spec(assignment.For(message.Seq)!.Length);
                Assert.Contains($"| length: {band.MinWords}-{band.MaxWords} words", line!);
            }
        }

        // Every relevant and business thread was asked for at least once
        Assert.All(RelevantThreads(run).Concat(BusinessThreads(run)), t => Assert.Contains(t.Id, seen));
    }

    /// <summary>
    /// Proves: plumbing only (fake provider). The fake sizes bodies to the band midpoint, so this proves plumbing, not
    /// model length parity; live parity is recorded in 06-HUMAN-UAT.md. Logs per-band median EML body word counts for both
    /// populations and checks every non-voicemail body is non-empty and is the fake's body.
    /// </summary>
    [Theory]
    [InlineData(ParityRuns.Synthetic)]
    [InlineData(ParityRuns.ProductionPlanner)]
    public void Parity_FakeOutputWordCounts_PlumbingOnly(string fixture)
    {
        var run = Finished(fixture);
        foreach (var (label, threads) in new[] { ("relevant", RelevantThreads(run)), ("business", BusinessThreads(run)) })
        {
            var byBand = new Dictionary<LengthBand, List<int>>();
            foreach (var (thread, message) in MessagesOf(threads).Where(x => !IsVoicemailMessage(x.Thread, x.Message)))
            {
                var spec = RenderAssignments.Spec(AssignmentOf(run, thread).For(message.Seq)!.Length);
                var body = ModelBody(Load(run, message));

                Assert.False(string.IsNullOrWhiteSpace(body), $"{message.DocId} has an empty body");
                Assert.Equal(EvidenceTestData.Filler((spec.MinWords + spec.MaxWords) / 2), body);

                if (!byBand.TryGetValue(spec.Band, out var counts))
                    byBand[spec.Band] = counts = new List<int>();
                counts.Add(Words(body));
            }

            foreach (var (band, counts) in byBand.OrderBy(x => x.Key))
            {
                var sorted = counts.OrderBy(n => n).ToList();
                _output.WriteLine($"{fixture} {label} {band}: n={sorted.Count}, median words {sorted[sorted.Count / 2]}");
            }
        }
    }

    /// <summary>
    /// Proves: code-controlled input. D-12 strict: a 400-word body for a Short-band daily business message is kept on
    /// attempt 1 (exactly one FillerThreadDto request, ThreadDone Written); filler length parity is a prompt mechanism,
    /// not a V8 check.
    /// </summary>
    [Fact]
    public async Task Parity_FillerOutOfBand_IsKeptWithoutRetry()
    {
        var (draft, plan) = AttachmentTestData.MixedCase();
        var assignments = FillerAssignments.Compute(plan);
        var (thread, seq) = plan.Threads
            .Where(t => t.Category == NonRelevantCategory.DailyBusiness && assignments.ContainsKey(t.Id))
            .OrderBy(t => t.Id, StringComparer.Ordinal)
            .SelectMany(t => t.Messages.Where(m => !IsVoicemailMessage(t, m) && assignments[t.Id].For(m.Seq)!.Length == LengthBand.Short)
                .Select(m => (Thread: t, Seq: m.Seq)))
            .First();
        var longBody = EvidenceTestData.Filler(400);
        var dto = AttachmentResponder.FillerDto(thread, assignments[thread.Id]);
        dto.Messages.Single(m => m.Seq == seq).Body = longBody;
        var json = JsonSerializer.Serialize(dto, DtoOptions);

        var run = await RunAsync("OutOfBand", draft, plan, (id, _) => id == thread.Id ? json : null);
        try
        {
            Assert.Equal(GenerationOutcome.Finished, run.Result.Outcome);
            Assert.Single(run.Requests, r => r.SchemaName == FillerSchema && ThreadOf(r) == thread.Id);

            var record = GenerationJournal.Replay(run.Folder, plan).LastByThread[thread.Id];
            Assert.Equal(JournalRecordType.ThreadDone, record.Type);
            Assert.Equal(ThreadWriteStatus.Written, record.Status);
            Assert.Equal(1, record.Attempts);

            var message = thread.Messages.Single(m => m.Seq == seq);
            Assert.Equal(400, Words(ModelBody(Load(run, message))));
        }
        finally
        {
            EvidenceTestData.DeleteFolder(run.Folder);
        }
    }

    /// <summary>
    /// Proves: code-controlled input. Privileged threads are labeled at 85% (and subject-tagged at 40% of labeled) within
    /// one thread in both populations; every Banner/BannerAndSubject email has the banner text in its plain text, every
    /// other email has none; BannerAndSubject subjects carry the tag; every Privileged=Y key row with a banner marking
    /// has the banner in its EML (Pitfall 2).
    /// </summary>
    [Theory]
    [InlineData(ParityRuns.Synthetic)]
    [InlineData(ParityRuns.ProductionPlanner)]
    public void Parity_PrivilegeMarkers(string fixture)
    {
        var run = Finished(fixture);
        foreach (var (label, assignments) in new[] { ("relevant", run.RelevantAssignments), ("non-relevant", run.FillerAssignments) })
        {
            var privileged = run.Plan.Threads.Where(t => t.IsPrivileged && assignments.ContainsKey(t.Id)).ToList();
            var labeled = privileged.Count(t => assignments[t.Id].Marking is PrivilegeMarking.Banner or PrivilegeMarking.BannerAndSubject);
            var tagged = privileged.Count(t => assignments[t.Id].Marking == PrivilegeMarking.BannerAndSubject);
            _output.WriteLine($"{fixture} {label}: privileged {privileged.Count}, labeled {labeled}, subject-tagged {tagged}");

            Assert.True(Math.Abs(labeled - RenderAssignments.LabeledShare * privileged.Count) <= 1, $"{label} labeled {labeled} of {privileged.Count}");
            Assert.True(Math.Abs(tagged - RenderAssignments.SubjectTagShare * labeled) <= 1, $"{label} tagged {tagged} of {labeled}");
            Assert.DoesNotContain(assignments.Keys, id => !run.Plan.Threads.Single(t => t.Id == id).IsPrivileged
                && assignments[id].Marking != PrivilegeMarking.None);
        }

        foreach (var (thread, message) in MessagesOf(RelevantThreads(run).Concat(BusinessThreads(run)))
                     .Where(x => !IsVoicemailMessage(x.Thread, x.Message)))
        {
            var marking = AssignmentOf(run, thread).Marking;
            var eml = Load(run, message);
            var banner = (eml.TextBody ?? string.Empty).Contains(HtmlEmailFormatter.PrivilegedBannerText, StringComparison.Ordinal);
            Assert.True(banner == (marking is PrivilegeMarking.Banner or PrivilegeMarking.BannerAndSubject),
                $"{message.DocId} marking {marking}, banner {banner}");
            if (marking == PrivilegeMarking.BannerAndSubject)
                Assert.StartsWith(EmailComposer.PrivilegedSubjectTag, ReplyPrefix().Replace(eml.Subject ?? string.Empty, string.Empty));
        }

        var threads = run.Plan.Threads.ToDictionary(t => t.Id, StringComparer.Ordinal);
        var checkedRows = 0;
        foreach (var row in KeyRows(run).Where(r => r["Doc Type"] == AnswerKeyBuilder.DocTypeEmail && r["Privileged"] == "Y"))
        {
            var thread = threads[row["Thread ID"]];
            var marking = AssignmentOf(run, thread).Marking;
            if (marking is not (PrivilegeMarking.Banner or PrivilegeMarking.BannerAndSubject))
                continue;

            var message = thread.Messages.Single(m => m.DocId == row["DocID"]);
            if (IsVoicemailMessage(thread, message))
                continue;

            var eml = MimeMessage.Load(Path.Combine(run.Folder, row["File Path"].Replace('/', Path.DirectorySeparatorChar)));
            Assert.Contains(HtmlEmailFormatter.PrivilegedBannerText, eml.TextBody ?? string.Empty);
            checkedRows++;
        }

        _output.WriteLine($"{fixture}: {checkedRows} privileged key rows with a banner checked against their EML");
    }

    /// <summary>
    /// Proves: code-controlled input. For each shape (root, reply, forward) the header-name sets of relevant EMLs equal
    /// those of non-relevant business EMLs (roots legitimately lack In-Reply-To/References); every Message-ID has the
    /// EmailComposer.MessageIdFor shape; X-ReelDiscovery-DocID equals the file name stem; every HTML part has the
    /// disclaimer banner.
    /// </summary>
    [Theory]
    [InlineData(ParityRuns.Synthetic)]
    [InlineData(ParityRuns.ProductionPlanner)]
    public void Parity_HeadersByShape(string fixture)
    {
        var run = Finished(fixture);
        Dictionary<Shape, HashSet<string>> Sets(IEnumerable<PlannedThread> threads)
        {
            var result = new Dictionary<Shape, HashSet<string>>();
            foreach (var (_, message) in MessagesOf(threads))
            {
                var path = EmlPath(run, message);
                var eml = MimeMessage.Load(path);
                Assert.Matches(MessageIdShape(), eml.MessageId ?? string.Empty);
                Assert.Equal(Path.GetFileNameWithoutExtension(path), eml.Headers[MailboxWriter.DocIdHeader]);
                Assert.Contains(DisclaimerMarker, eml.HtmlBody ?? string.Empty);

                // Cc is present only when the plan copies someone; the structure fact checks it against the plan
                var names = eml.Headers.Select(h => h.Field).Where(f => !f.Equals("Cc", StringComparison.OrdinalIgnoreCase))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(f => f, StringComparer.OrdinalIgnoreCase);
                var key = string.Join(",", names);
                if (!result.TryGetValue(ShapeOf(message), out var set))
                    result[ShapeOf(message)] = set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                set.Add(key);
            }

            return result;
        }

        var relevant = Sets(RelevantThreads(run));
        var business = Sets(BusinessThreads(run));
        Assert.Contains(Shape.Root, relevant.Keys);
        Assert.Contains(Shape.Root, business.Keys);
        Assert.Contains(Shape.Reply, relevant.Keys);
        Assert.Contains(Shape.Reply, business.Keys);

        foreach (var shape in relevant.Keys.Intersect(business.Keys))
        {
            _output.WriteLine($"{fixture} {shape}: relevant [{string.Join(" | ", relevant[shape])}]");
            Assert.True(relevant[shape].SetEquals(business[shape]),
                $"{fixture} {shape}: relevant [{string.Join(" | ", relevant[shape])}] vs business [{string.Join(" | ", business[shape])}]");
        }
    }

    /// <summary>
    /// Proves: code-controlled input. To/Cc counts and References counts equal the plan; the MIME root is
    /// multipart/alternative without attachments, multipart/alternative with a multipart/related child for inline images
    /// only, multipart/mixed when any file attachment exists (MimeKit BodyBuilder) - the same rules for both populations.
    /// </summary>
    [Theory]
    [InlineData(ParityRuns.Synthetic)]
    [InlineData(ParityRuns.ProductionPlanner)]
    public void Parity_StructureByShape(string fixture)
    {
        var run = Finished(fixture);
        foreach (var (label, threads) in new[] { ("relevant", RelevantThreads(run)), ("business", BusinessThreads(run)) })
        {
            var layouts = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var (thread, message) in MessagesOf(threads))
            {
                var eml = Load(run, message);
                Assert.Equal(message.ToIds.Count, eml.To.Count);
                Assert.Equal(message.CcIds.Count, eml.Cc.Count);
                Assert.Equal(KnowledgeGate.Ancestors(thread, message.Seq).Count, eml.References.Count);
                Assert.Equal(message.ReplyToSeq > 0, !string.IsNullOrEmpty(eml.InReplyTo));

                var files = message.Attachments.Any(a => !(a.Kind == AttachmentType.Image && a.IsInline));
                var inline = message.Attachments.Any(a => a.Kind == AttachmentType.Image && a.IsInline);
                var root = Assert.IsAssignableFrom<Multipart>(eml.Body);
                var related = root.OfType<Multipart>().Any(p => p.ContentType.MimeType == "multipart/related")
                    || (root.ContentType.MimeType == "multipart/mixed"
                        && root.OfType<Multipart>().SelectMany(p => p.OfType<Multipart>()).Any(p => p.ContentType.MimeType == "multipart/related"));
                var layout = files ? "files" : inline ? "inline" : "none";
                layouts[layout] = layouts.GetValueOrDefault(layout) + 1;

                Assert.Equal(files ? "multipart/mixed" : "multipart/alternative", root.ContentType.MimeType);
                Assert.True(related == inline, $"{message.DocId}: inline {inline}, related part {related}");
            }

            _output.WriteLine($"{fixture} {label}: {string.Join(", ", layouts.OrderBy(x => x.Key).Select(x => $"{x.Key} {x.Value}"))}");
        }
    }

    /// <summary>
    /// Proves: code-controlled input. Every attachment row's File Name equals the EML part's file name (matched by the
    /// DocID header); no File Name is a code default like "Document 1.docx"; every non-relevant attachment row is
    /// Generated or Generated - with warnings (Pitfall 1).
    /// </summary>
    [Theory]
    [InlineData(ParityRuns.Synthetic)]
    [InlineData(ParityRuns.ProductionPlanner)]
    public void Parity_AttachmentNames(string fixture)
    {
        var run = Finished(fixture);
        var rows = KeyRows(run).Where(r => r["Doc Type"] == AnswerKeyBuilder.DocTypeAttachment).ToList();
        Assert.NotEmpty(rows);
        Assert.Contains(rows, r => r["Relevance"] == AnswerKeyBuilder.NotRelevantLabel);

        foreach (var row in rows)
        {
            var eml = MimeMessage.Load(Path.Combine(run.Folder, row["File Path"].Replace('/', Path.DirectorySeparatorChar)));
            var part = eml.BodyParts.OfType<MimePart>().Single(p => p.Headers[MailboxWriter.DocIdHeader] == row["DocID"]);

            Assert.Equal(part.FileName, row["File Name"]);
            Assert.DoesNotMatch(CodeDefaultName(), row["File Name"]);
            if (row["Relevance"] == AnswerKeyBuilder.NotRelevantLabel)
                Assert.Contains(row["Status"], new[] { AnswerKeyBuilder.StatusGenerated, AnswerKeyBuilder.StatusGeneratedWithWarnings });
        }

        _output.WriteLine($"{fixture}: {rows.Count} attachment rows checked");
    }
}
