using System.Text.Json;
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
using ReelDiscovery.Tests.Corpus;
using ReelDiscovery.Tests.Fakes;

namespace ReelDiscovery.Tests.Evidence;

/// <summary>
/// Attachment repair of already written threads (06-20 Task 1, D-26, review concerns 1, 3, 5 and 06-20): per-message
/// harvest, byte-stable rewrites from the journaled text, the no-loss rule and one AttachmentsRepaired commit line.
/// </summary>
public partial class AttachmentRepairTests : IDisposable
{
    private const string RetryHint = "Click Continue Generating.";
    private const int RepairRow = 9;

    private static readonly TimeSpan Guard = TimeSpan.FromSeconds(10);

    private readonly string _folder;
    private readonly CaseDraft _draft;
    private readonly CorpusPlan _plan;
    private readonly LlmGateway _gateway;
    private readonly FakeLlmProvider _fake;
    private readonly GenerationJournal _journal;
    private readonly GenerationSnapshot _snapshot;
    private readonly AIModelConfig _story;
    private readonly AIModelConfig _volume;
    private readonly List<FileStream> _locks = new();

    public AttachmentRepairTests()
    {
        _folder = EvidenceTestData.TempCaseFolder();
        (_draft, _plan) = AttachmentTestData.MixedCase();
        (_gateway, _fake) = CorpusTestData.CreateGateway();
        _story = _gateway.ResolveModel(LlmTier.Story);
        _volume = _gateway.ResolveModel(LlmTier.Volume);
        _story.InputTokenPricePerMillion = 3m;
        _story.OutputTokenPricePerMillion = 15m;
        _volume.InputTokenPricePerMillion = 1m;
        _volume.OutputTokenPricePerMillion = 5m;
        _journal = new GenerationJournal(_folder);
        _snapshot = new GenerationSnapshot();
        _snapshot.Reset(new GenerationCounts { TotalThreads = 1, WaveCount = 1 }, new[]
        {
            new WaveProgress { Index = 1, Threads = 1 },
            new WaveProgress { Index = RepairRow, RowKind = StageRowKind.Attachments, Threads = 1 }
        });
    }

    public void Dispose()
    {
        foreach (var stream in _locks)
            stream.Dispose();
        EvidenceTestData.DeleteFolder(_folder);
    }

    [GeneratedRegex(@"Attachment id: (\S+)")]
    private static partial Regex AttachmentIdLine();

    private static string RenderSchema => JsonSchemaRegistry.GetSchemaName(typeof(ThreadRenderer.ThreadRenderDto));

    private IReadOnlyDictionary<string, string> Folders => PlanNumbering.CustodianFolders(_draft.Guide!);

    private static string? AttachmentIdOf(LlmProviderRequest request)
    {
        var match = AttachmentIdLine().Match(request.UserPrompt ?? string.Empty);
        return match.Success ? match.Groups[1].Value : null;
    }

    private List<LlmProviderRequest> Requests()
    {
        lock (_fake.Requests)
            return _fake.Requests.ToList();
    }

    private Func<LlmProviderRequest, LlmProviderResponse> Responder(Func<LlmProviderRequest, LlmProviderResponse?>? intercept = null)
    {
        var inner = AttachmentResponder.Create(_plan, _draft.Guide!);
        return request => intercept?.Invoke(request) ?? inner(request);
    }

    private static Func<LlmProviderRequest, LlmProviderResponse?> Refuse(params string[] docIds) =>
        request => docIds.Contains(AttachmentIdOf(request)) ? FakeLlmProvider.Refused("cannot write that document") : null;

    private AttachmentUnitOfWork AttachmentUnit(IMediaGenerator? media) =>
        new(_gateway, _snapshot, media, new MediaRunGate(), _story, _volume);

    private ThreadRenderContext Ctx(PlannedThread thread)
    {
        var people = PeopleDirectory.Create(_draft.Guide!, _plan);
        var assignment = GenerationRunState.AllAssignments(_plan)[thread.Id];
        return ThreadRenderContext.Create(_draft, _plan, thread, assignment, people, new List<LedgerEntry>(), 1);
    }

    private ChainScheduler Chains(GenerationRunState? state = null)
    {
        var committed = new Dictionary<string, string>(StringComparer.Ordinal);
        if (state != null)
        {
            foreach (var (docId, record) in state.AttachmentRecords)
            {
                if (record.Type == JournalRecordType.AttachmentDone)
                    committed[docId] = record.FlattenedText;
            }
        }

        return new ChainScheduler(AttachmentChains.Compute(_plan, _draft.Guide), committed, ChainScheduler.ThreadOfAttachment(_plan),
            ChainScheduler.RankThreads(_plan));
    }

    // Writes one thread the Phase 6 way (thread + attachments in one ThreadDone line)
    private async Task<ThreadOutcome> Write(PlannedThread thread, Func<LlmProviderRequest, LlmProviderResponse?>? intercept = null,
        IMediaGenerator? media = null, bool withAttachments = true)
    {
        _fake.Respond = Responder(intercept);
        using var cts = new CancellationTokenSource();
        using var run = new PlanCallRun(cts.Token, 1, RetryHint);
        var chains = Chains();
        chains.BeginStep(new[] { thread.Id });
        var unit = new ThreadUnitOfWork(_gateway, _journal, new MailboxWriter(_folder, Folders), _snapshot, _story, AttachmentUnit(media));
        var ctx = Ctx(thread);
        return await unit.RunAsync(ctx, RenderProfile.For(thread, _gateway, null), run,
            withAttachments ? new ThreadAttachmentContext { Chains = chains, RowIndex = 1 } : null);
    }

    private async Task<AttachmentRepairOutcome> Repair(PlannedThread thread, IMediaGenerator? media = null,
        Func<LlmProviderRequest, LlmProviderResponse?>? intercept = null, bool late = false, CancellationTokenSource? cts = null,
        Func<JournalRecord, Task>? beforeAppend = null)
    {
        _fake.Respond = Responder(intercept);
        var state = GenerationRunState.From(_plan, GenerationJournal.Replay(_folder, _plan));
        var group = state.RepairGroups(_plan, media != null).SingleOrDefault(g => g.ThreadId == thread.Id)
                    ?? new AttachmentRepairGroup { ThreadId = thread.Id, Relevant = thread.Relevance == ThreadRelevance.Relevant };
        var chains = Chains(state);
        chains.BeginStep(new[] { thread.Id });
        var actx = new ThreadAttachmentContext { Chains = chains, RowIndex = RepairRow, LateRepair = late };
        var repair = new AttachmentRepair(_journal, new MailboxWriter(_folder, Folders), AttachmentUnit(media), _snapshot, _folder, Folders)
        {
            BeforeAppendHook = beforeAppend
        };
        var source = cts ?? new CancellationTokenSource();
        try
        {
            using var run = new PlanCallRun(source.Token, 1, RetryHint);
            return await repair.RepairThreadAsync(Ctx(thread), RenderProfile.For(thread, _gateway, null), state.Records[thread.Id],
                state.AttachmentRecords, group, actx, run).WaitAsync(Guard);
        }
        finally
        {
            if (cts == null)
                source.Dispose();
        }
    }

    private List<JournalRecord> JournalRecords()
    {
        var records = new List<JournalRecord>();
        foreach (var line in File.ReadAllLines(GenerationJournal.PathFor(_folder)).Where(l => l.Length > 0))
            records.Add(JsonSerializer.Deserialize<JournalRecord>(line, CaseDraft.JsonOptions)!);
        return records;
    }

    private async Task RewriteJournal(Action<JournalRecord> edit)
    {
        var records = JournalRecords();
        File.Delete(GenerationJournal.PathFor(_folder));
        foreach (var record in records)
        {
            edit(record);
            await _journal.AppendAsync(record);
        }
    }

    private List<string> Copies(PlannedMessage message) => MailboxWriter.CopyPathsFor(message, Folders);

    private string Full(string relative) => Path.Combine(_folder, relative);

    private Dictionary<string, byte[]> Bytes(PlannedThread thread) =>
        thread.Messages.SelectMany(Copies).ToDictionary(c => c, c => File.ReadAllBytes(Full(c)), StringComparer.Ordinal);

    private static List<MimeEntity> DocParts(MimeMessage message) =>
        message.BodyParts.Where(p => p.Headers.Contains(MailboxWriter.DocIdHeader)).ToList();

    private MimeMessage Load(string relative) => MimeMessage.Load(Full(relative));

    private static byte[] PartBytes(MimeMessage message, string docId)
    {
        var part = (MimePart)DocParts(message).Single(p => p.Headers[MailboxWriter.DocIdHeader] == docId);
        using var stream = new MemoryStream();
        part.Content!.DecodeTo(stream);
        return stream.ToArray();
    }

    private static IEnumerable<string> PartIds(MimeMessage message) =>
        DocParts(message).Select(p => p.Headers[MailboxWriter.DocIdHeader] ?? string.Empty);

    // Rewrites a copy without the given parts (a complete, readable file)
    private void StripParts(string relative, params string[] docIds)
    {
        var message = Load(relative);
        foreach (var nested in AllMultiparts(message.Body!).ToList())
        {
            foreach (var entity in nested.Where(e => docIds.Contains(e.Headers[MailboxWriter.DocIdHeader])).ToList())
                nested.Remove(entity);
        }

        using var stream = File.Create(Full(relative));
        message.WriteTo(stream);
    }

    private static IEnumerable<Multipart> AllMultiparts(MimeEntity entity)
    {
        if (entity is not Multipart multipart)
            yield break;
        yield return multipart;
        foreach (var child in multipart)
        {
            foreach (var nested in AllMultiparts(child))
                yield return nested;
        }
    }

    private static PlannedAttachment Spec(PlannedThread thread, int seq, AttachmentType kind) =>
        thread.Messages.Single(m => m.Seq == seq).Attachments.First(a => a.Kind == kind);

    private static PlannedAttachment Add(PlannedMessage message, AttachmentType kind, string title, bool inline = false)
    {
        var extension = kind switch
        {
            AttachmentType.Word => ".docx",
            AttachmentType.Excel => ".xlsx",
            AttachmentType.PowerPoint => ".pptx",
            AttachmentType.Image => ".png",
            _ => ".ics"
        };
        var spec = new PlannedAttachment
        {
            Kind = kind,
            Title = title,
            FileName = title + extension,
            Description = title + " for the thread.",
            IsInline = inline,
            DocId = message.DocId + ".00" + (11 + message.Attachments.Count)
        };
        message.Attachments.Add(spec);
        return spec;
    }

    private JournalRecord LastLine() => JournalRecords().Last();

    // ---------- reuse and one commit line ----------

    [Fact]
    public async Task Repair_FailedExcel_OneCall_ReusesWord_EveryCopy_OneLine()
    {
        var thread = AttachmentTestData.Thread(_plan, AttachmentTestData.RelevantDocThreadId);
        var root = thread.Messages.Single(m => m.Seq == 1);
        var word = Spec(thread, 1, AttachmentType.Word).DocId;
        var excel = Spec(thread, 1, AttachmentType.Excel).DocId;
        await Write(thread, Refuse(excel));
        var wordBytes = Copies(root).Select(c => PartBytes(Load(c), word)).ToList();
        var before = Requests().Count;
        _snapshot.Drain();

        var outcome = await Repair(thread);

        var calls = Requests().Skip(before).ToList();
        Assert.Equal(excel, AttachmentIdOf(Assert.Single(calls)));
        Assert.DoesNotContain(calls, r => r.SchemaName == RenderSchema);
        foreach (var (copy, i) in Copies(root).Select((c, i) => (c, i)))
        {
            var eml = Load(copy);
            Assert.Equal(AttachmentTestData.AttachmentsOf(_plan, thread.Id).Select(a => a.DocId).OrderBy(d => d), PartIds(eml).OrderBy(d => d));
            Assert.Equal(wordBytes[i], PartBytes(eml, word));
        }

        var line = LastLine();
        Assert.Equal(JournalRecordType.AttachmentsRepaired, line.Type);
        Assert.Equal(thread.Id, line.ThreadId);
        var nested = Assert.Single(line.Attachments);
        Assert.Equal(excel, nested.AttachmentDocId);
        Assert.Equal(JournalRecordType.AttachmentDone, nested.Type);
        Assert.Equal(Copies(root), nested.Files);
        Assert.Equal(Copies(root).OrderBy(c => c), line.Files.OrderBy(c => c));
        Assert.Equal(0m, line.CostUsd);
        Assert.Equal(_story.CalculateTotalCost(800, 300), nested.CostUsd);

        Assert.Equal(1, outcome.Added);
        Assert.False(outcome.Unjournaled);
        Assert.Equal(Copies(root).Count, outcome.FilesRewritten);
        Assert.NotEmpty(outcome.Deltas);

        var view = _snapshot.Drain();
        var added = Assert.Single(view.Events, e => e.Kind == GenerationEventKind.AttachmentsAdded);
        Assert.Equal(EmailComposer.SubjectFor(thread, root, Ctx(thread).Assignment.Marking), added.Subject);
        Assert.Equal(1, added.Attachments);
        Assert.Equal(Copies(root).Count, added.Files);
        Assert.Equal(1, view.Waves.Single(w => w.Index == RepairRow).AttachmentsDone);

        var replay = GenerationJournal.Replay(_folder, _plan);
        Assert.Equal(JournalRecordType.AttachmentDone, replay.LastByAttachment[excel].Type);
        Assert.DoesNotContain(GenerationRunState.From(_plan, replay).RepairGroups(_plan, mediaAvailable: false), g => g.ThreadId == thread.Id);
    }

    [Fact]
    public async Task Repair_MultiMessage_ReusesPartsOnEveryMessage()
    {
        var thread = AttachmentTestData.Thread(_plan, AttachmentTestData.MultiMessageThreadId);
        var root = thread.Messages.Single(m => m.Seq == 1);
        var reply = thread.Messages.Single(m => m.Seq == 2);
        var slides = Add(root, AttachmentType.PowerPoint, "Site Visit Slides").DocId;
        var word = Spec(thread, 1, AttachmentType.Word).DocId;
        var excel = Spec(thread, 2, AttachmentType.Excel).DocId;
        await Write(thread, Refuse(slides));
        var wordBytes = Copies(root).Select(c => PartBytes(Load(c), word)).ToList();
        var excelBytes = Copies(reply).Select(c => PartBytes(Load(c), excel)).ToList();
        var before = Requests().Count;

        var outcome = await Repair(thread);

        Assert.Equal(slides, AttachmentIdOf(Assert.Single(Requests().Skip(before))));
        Assert.Equal(1, outcome.Added);
        foreach (var (copy, i) in Copies(root).Select((c, i) => (c, i)))
        {
            Assert.Equal(wordBytes[i], PartBytes(Load(copy), word));
            Assert.Contains(slides, PartIds(Load(copy)));
        }

        foreach (var (copy, i) in Copies(reply).Select((c, i) => (c, i)))
            Assert.Equal(excelBytes[i], PartBytes(Load(copy), excel));
    }

    [Fact]
    public async Task Repair_IncompleteFirstCopy_UsesOtherCopy()
    {
        var thread = AttachmentTestData.Thread(_plan, AttachmentTestData.RelevantDocThreadId);
        var root = thread.Messages.Single(m => m.Seq == 1);
        var word = Spec(thread, 1, AttachmentType.Word).DocId;
        var excel = Spec(thread, 1, AttachmentType.Excel).DocId;
        await Write(thread, Refuse(excel));
        var copies = Copies(root);
        Assert.True(copies.Count > 1);
        var original = PartBytes(Load(copies[1]), word);
        StripParts(copies[0], word);
        Assert.DoesNotContain(word, PartIds(Load(copies[0])));
        var before = Requests().Count;
        _snapshot.Drain();

        await Repair(thread);

        Assert.Equal(excel, AttachmentIdOf(Assert.Single(Requests().Skip(before))));
        Assert.DoesNotContain(_snapshot.Drain().Events, e => e.Kind == GenerationEventKind.AttachmentRegenerated);
        foreach (var copy in copies)
            Assert.Equal(original, PartBytes(Load(copy), word));
    }

    [Fact]
    public async Task Repair_UnchangedEmailsByteIdentical_TextPartsNeverChange()
    {
        var thread = AttachmentTestData.Thread(_plan, AttachmentTestData.MultiMessageThreadId);
        var reply = thread.Messages.Single(m => m.Seq == 2);
        var excel = Spec(thread, 2, AttachmentType.Excel).DocId;
        await Write(thread, Refuse(excel));
        var before = Bytes(thread);
        var texts = before.Keys.ToDictionary(c => c, c => (Load(c).TextBody, Load(c).HtmlBody), StringComparer.Ordinal);

        await Repair(thread);

        var after = Bytes(thread);
        var changed = new HashSet<string>(Copies(reply), StringComparer.Ordinal);
        foreach (var (copy, bytes) in before)
        {
            if (changed.Contains(copy))
                Assert.NotEqual(bytes, after[copy]);
            else
                Assert.Equal(bytes, after[copy]);

            var eml = Load(copy);
            Assert.Equal(texts[copy], (eml.TextBody, eml.HtmlBody));
        }

        // The rewritten text equals a composition of the journaled text
        var record = GenerationJournal.Replay(_folder, _plan).LastByThread[thread.Id];
        var saved = record.Messages.Single(m => m.DocId == reply.DocId);
        Assert.Contains(saved.BodyPlain, Load(Copies(reply)[0]).TextBody);
        Assert.Contains(excel, PartIds(Load(Copies(reply)[0])));
    }

    [Fact]
    public async Task Repair_CommittedWordMissingFromEveryCopy_RegeneratedWithEvent()
    {
        var thread = AttachmentTestData.Thread(_plan, AttachmentTestData.RelevantDocThreadId);
        var root = thread.Messages.Single(m => m.Seq == 1);
        var word = Spec(thread, 1, AttachmentType.Word).DocId;
        var excel = Spec(thread, 1, AttachmentType.Excel).DocId;
        await Write(thread, Refuse(excel));
        var wordName = GenerationJournal.Replay(_folder, _plan).LastByAttachment[word].FileName;
        foreach (var copy in Copies(root))
            StripParts(copy, word);
        var before = Requests().Count;
        _snapshot.Drain();

        var outcome = await Repair(thread);

        Assert.Equal(new[] { word, excel }, Requests().Skip(before).Select(AttachmentIdOf));
        Assert.Equal(2, outcome.Added);
        var regenerated = Assert.Single(_snapshot.Drain().Events, e => e.Kind == GenerationEventKind.AttachmentRegenerated);
        Assert.Equal(wordName, regenerated.FileName);
        Assert.All(Copies(root), c => Assert.Contains(word, PartIds(Load(c))));
        Assert.Equal(2, LastLine().Attachments.Count);
    }

    [Fact]
    public async Task Repair_RegenerationFails_KeepsExistingCopies()
    {
        var thread = AttachmentTestData.Thread(_plan, AttachmentTestData.RelevantDocThreadId);
        var root = thread.Messages.Single(m => m.Seq == 1);
        var word = Spec(thread, 1, AttachmentType.Word).DocId;
        var excel = Spec(thread, 1, AttachmentType.Excel).DocId;
        var slides = Spec(thread, 1, AttachmentType.PowerPoint).DocId;
        await Write(thread, Refuse(excel));
        var wordName = GenerationJournal.Replay(_folder, _plan).LastByAttachment[word].FileName;
        var before = Bytes(thread);

        // Every copy unreadable: nothing can be harvested, and the Word and PowerPoint cannot be created again
        foreach (var copy in Copies(root))
            _locks.Add(new FileStream(Full(copy), FileMode.Open, FileAccess.ReadWrite, FileShare.None));

        var outcome = await Repair(thread, intercept: Refuse(word, slides));

        foreach (var stream in _locks)
            stream.Dispose();
        _locks.Clear();
        Assert.Equal(before, Bytes(thread));
        Assert.Equal(0, outcome.Added);
        Assert.Equal(0, outcome.FilesRewritten);

        var line = LastLine();
        Assert.Equal(JournalRecordType.AttachmentsRepaired, line.Type);
        Assert.Empty(line.Files);
        var expected = $"kept the existing email: {wordName} could not be reused or created again";
        var excelRecord = line.Attachments.Single(a => a.AttachmentDocId == excel);
        Assert.Equal(JournalRecordType.AttachmentFailed, excelRecord.Type);
        Assert.Equal(expected, excelRecord.Reason);
        Assert.All(line.Attachments, a => Assert.Equal(JournalRecordType.AttachmentFailed, a.Type));
        Assert.All(line.Attachments, a => Assert.Equal(expected, a.Reason));
        Assert.Equal(_story.CalculateTotalCost(800, 300), excelRecord.CostUsd);
    }

    [Fact]
    public async Task Repair_SecondCopyWriteFails_ConvergesNextTime()
    {
        var thread = AttachmentTestData.Thread(_plan, AttachmentTestData.RelevantDocThreadId);
        var root = thread.Messages.Single(m => m.Seq == 1);
        var excel = Spec(thread, 1, AttachmentType.Excel).DocId;
        await Write(thread, Refuse(excel));
        var copies = Copies(root);
        Assert.True(copies.Count > 1);
        File.Delete(Full(copies[1]));
        Directory.CreateDirectory(Full(copies[1]));
        var doneBefore = _snapshot.Drain().Counts.AttachmentsDone;

        var first = await Repair(thread);

        var line = LastLine();
        var failed = Assert.Single(line.Attachments);
        Assert.Equal(JournalRecordType.AttachmentFailed, failed.Type);
        Assert.StartsWith($"Could not rewrite {root.DocId}.eml: ", failed.Reason);
        Assert.Equal(_story.CalculateTotalCost(800, 300), failed.CostUsd);
        // Only fully rewritten emails are listed; the partly rewritten copy is already listed by its ThreadDone line
        Assert.Empty(line.Files);
        Assert.Equal(0, first.Added);
        var view = _snapshot.Drain();
        Assert.Equal(doneBefore, view.Counts.AttachmentsDone);
        Assert.Equal(0, view.Waves.Single(w => w.Index == RepairRow).AttachmentsDone);
        Assert.DoesNotContain(view.Events, e => e.Kind == GenerationEventKind.AttachmentsAdded);

        Directory.Delete(Full(copies[1]));
        var second = await Repair(thread);

        Assert.Equal(1, second.Added);
        Assert.Equal(JournalRecordType.AttachmentDone, Assert.Single(LastLine().Attachments).Type);
        Assert.Equal(copies.OrderBy(c => c), LastLine().Files.Where(copies.Contains).OrderBy(c => c));
        foreach (var copy in copies)
        {
            Assert.Contains(excel, PartIds(Load(copy)));
            Assert.Equal(PartIds(Load(copies[0])).OrderBy(d => d), PartIds(Load(copy)).OrderBy(d => d));
        }

        Assert.Equal(1, _snapshot.Drain().Waves.Single(w => w.Index == RepairRow).AttachmentsDone);
    }

    // ---------- media ----------

    [Fact]
    public async Task Repair_SkippedInlineImage_MediaNow_HtmlGainsCid_ReplyQuotesPlaceholder()
    {
        var thread = AttachmentTestData.Thread(_plan, AttachmentTestData.MultiMessageThreadId);
        var root = thread.Messages.Single(m => m.Seq == 1);
        var reply = thread.Messages.Single(m => m.Seq == 2);
        var image = Add(root, AttachmentType.Image, "Site photo", inline: true).DocId;
        await Write(thread);
        Assert.Equal(JournalRecordType.AttachmentSkipped, GenerationJournal.Replay(_folder, _plan).LastByAttachment[image].Type);
        Assert.DoesNotContain("[image: Site photo]", Load(Copies(reply)[0]).HtmlBody);
        var media = new FakeMediaGenerator();

        var outcome = await Repair(thread, media);

        Assert.Equal(1, outcome.Added);
        Assert.Single(media.Requests, r => r.Kind == "image");
        var domain = PeopleDirectory.Create(_draft.Guide!, _plan).Find(root.FromId)!.Domain;
        var cid = AttachmentNaming.ContentIdFor(image, domain);
        Assert.All(Copies(root), c =>
        {
            var eml = Load(c);
            Assert.Contains("cid:" + cid, eml.HtmlBody);
            Assert.Equal(cid, DocParts(eml).Single(p => p.Headers[MailboxWriter.DocIdHeader] == image).ContentId);
        });
        Assert.All(Copies(reply), c => Assert.Contains("[image: Site photo]", Load(c).HtmlBody));
    }

    [Fact]
    public async Task Repair_Phase5StyleThread_AllPlannedAttachmentsGenerated()
    {
        var thread = AttachmentTestData.Thread(_plan, AttachmentTestData.RelevantDocThreadId);
        var root = thread.Messages.Single(m => m.Seq == 1);
        await Write(thread, withAttachments: false);
        await RewriteJournal(r =>
        {
            foreach (var message in r.Messages)
            {
                message.From = string.Empty;
                message.AttachmentTitles.Clear();
            }
        });
        Assert.All(Copies(root), c => Assert.Empty(DocParts(Load(c))));
        var texts = Copies(root).Select(c => (Load(c).TextBody, Load(c).HtmlBody)).ToList();
        var before = Requests().Count;

        var outcome = await Repair(thread);

        var docIds = AttachmentTestData.AttachmentsOf(_plan, thread.Id).Select(a => a.DocId).ToList();
        Assert.Equal(docIds.OrderBy(d => d), Requests().Skip(before).Select(AttachmentIdOf).OrderBy(d => d));
        Assert.Equal(3, outcome.Added);
        var names = AttachmentNaming.NamesFor(root, null, AttachmentChains.Compute(_plan, _draft.Guide));
        foreach (var (copy, i) in Copies(root).Select((c, i) => (c, i)))
        {
            var eml = Load(copy);
            Assert.Equal(docIds.OrderBy(d => d), PartIds(eml).OrderBy(d => d));
            Assert.Equal(docIds.Select(d => names[d]).OrderBy(n => n), eml.Attachments.Select(a => a.ContentDisposition?.FileName ?? ((MimePart)a).FileName).OrderBy(n => n));
            Assert.Equal(texts[i], (eml.TextBody, eml.HtmlBody));
        }
    }

    // Phase 5 wrote voicemail threads as ordinary emails and journaled no From
    private async Task<PlannedThread> Phase5Voicemail()
    {
        var thread = AttachmentTestData.Thread(_plan, AttachmentTestData.RelevantVoicemailThreadId);
        await Write(thread, withAttachments: false);
        var record = GenerationJournal.Replay(_folder, _plan).LastByThread[thread.Id];
        var rendered = new RenderedThread
        {
            ThreadId = thread.Id,
            Messages = record.Messages.Select(m => new RenderedMessage
            {
                Seq = thread.Messages.Single(p => p.DocId == m.DocId).Seq,
                Body = m.BodyPlain,
                SignOff = m.SignOff
            }).ToList()
        };
        var ctx = Ctx(thread);
        var composed = EmailComposer.Compose(_plan.PlanId, thread, ctx.Assignment, rendered, ctx.People, null, voicemailNotice: false);
        var mailbox = new MailboxWriter(_folder, Folders);
        foreach (var email in composed.Emails)
            await mailbox.WriteAsync(email);
        await RewriteJournal(r =>
        {
            foreach (var message in r.Messages)
                message.From = string.Empty;
        });
        return thread;
    }

    [Fact]
    public async Task Repair_Phase5VoicemailThread_KeepsTextAddsAudio()
    {
        var thread = await Phase5Voicemail();
        var root = thread.Messages.Single();
        var voicemail = Spec(thread, 1, AttachmentType.Voicemail).DocId;
        var before = Copies(root).ToDictionary(c => c, Load, StringComparer.Ordinal);
        var media = new FakeMediaGenerator();

        var outcome = await Repair(thread, media);

        Assert.Equal(1, outcome.Added);
        Assert.Single(media.Requests, r => r.Kind == "speech");
        foreach (var (copy, original) in before)
        {
            var eml = Load(copy);
            Assert.Equal(original.From.ToString(), eml.From.ToString());
            Assert.Equal(original.Subject, eml.Subject);
            Assert.Equal(original.TextBody, eml.TextBody);
            Assert.Equal(original.HtmlBody, eml.HtmlBody);
            var audio = Assert.Single(DocParts(eml));
            Assert.Equal(voicemail, audio.Headers[MailboxWriter.DocIdHeader]);
            Assert.Equal("audio/mpeg", audio.ContentType.MimeType);
            Assert.DoesNotContain("Voice Mail", eml.From.ToString());
            Assert.DoesNotContain(VoicemailComposer.TranscriptHeader, eml.TextBody);
        }
    }

    [Fact]
    public async Task Repair_Phase6VoicemailThread_KeepsNoticeAsWritten()
    {
        var thread = AttachmentTestData.Thread(_plan, AttachmentTestData.RelevantVoicemailThreadId);
        var root = thread.Messages.Single();
        await Write(thread);
        var before = Copies(root).ToDictionary(c => c, Load, StringComparer.Ordinal);
        Assert.All(before.Values, e => Assert.Contains("Voice Mail", e.From.ToString()));

        var outcome = await Repair(thread, new FakeMediaGenerator());

        Assert.Equal(1, outcome.Added);
        foreach (var (copy, original) in before)
        {
            var eml = Load(copy);
            Assert.Equal(original.From.ToString(), eml.From.ToString());
            Assert.Equal(original.Subject, eml.Subject);
            Assert.Equal(original.TextBody, eml.TextBody);
            Assert.Equal(original.HtmlBody, eml.HtmlBody);
            Assert.Contains(VoicemailComposer.TranscriptHeader, eml.TextBody);
            Assert.Single(DocParts(eml), p => p.ContentType.MimeType == "audio/mpeg");
        }
    }

    [Fact]
    public async Task Repair_InlineImageReused_KeepsHarvestedContentId()
    {
        var thread = AttachmentTestData.Thread(_plan, AttachmentTestData.InviteThreadId);
        var root = thread.Messages.Single();
        var invite = Spec(thread, 1, AttachmentType.CalendarInvite).DocId;
        var image = Spec(thread, 1, AttachmentType.Image).DocId;
        await Write(thread, Refuse(invite), new FakeMediaGenerator());
        const string custom = "0123456789abcdef@harvested.example";
        foreach (var copy in Copies(root))
        {
            var eml = Load(copy);
            DocParts(eml).Single(p => p.Headers[MailboxWriter.DocIdHeader] == image).ContentId = custom;
            using var stream = File.Create(Full(copy));
            eml.WriteTo(stream);
        }

        var outcome = await Repair(thread, new FakeMediaGenerator());

        Assert.Equal(1, outcome.Added);
        Assert.All(Copies(root), c =>
        {
            var eml = Load(c);
            Assert.Equal(custom, DocParts(eml).Single(p => p.Headers[MailboxWriter.DocIdHeader] == image).ContentId);
            Assert.Contains("cid:" + custom, eml.HtmlBody);
            Assert.Contains(invite, PartIds(eml));
        });
    }

    [Fact]
    public async Task Repair_InlineImageRegenerated_GetsComputedContentId()
    {
        var thread = AttachmentTestData.Thread(_plan, AttachmentTestData.InviteThreadId);
        var root = thread.Messages.Single();
        var invite = Spec(thread, 1, AttachmentType.CalendarInvite).DocId;
        var image = Spec(thread, 1, AttachmentType.Image).DocId;
        await Write(thread, Refuse(invite), new FakeMediaGenerator());
        foreach (var copy in Copies(root))
            StripParts(copy, image);
        var media = new FakeMediaGenerator();

        var outcome = await Repair(thread, media);

        Assert.Equal(2, outcome.Added);
        Assert.Single(media.Requests);
        var domain = PeopleDirectory.Create(_draft.Guide!, _plan).Find(root.FromId)!.Domain;
        Assert.All(Copies(root), c => Assert.Equal(AttachmentNaming.ContentIdFor(image, domain),
            DocParts(Load(c)).Single(p => p.Headers[MailboxWriter.DocIdHeader] == image).ContentId));
    }

    // ---------- late repair and chains ----------

    [Fact]
    public async Task Repair_Late_RecordFlagged_NoDeltasReturned()
    {
        var thread = AttachmentTestData.Thread(_plan, AttachmentTestData.RelevantDocThreadId);
        var excel = Spec(thread, 1, AttachmentType.Excel).DocId;
        await Write(thread, Refuse(excel));
        var before = Requests().Count;

        var outcome = await Repair(thread, late: true);

        var nested = Assert.Single(LastLine().Attachments);
        Assert.True(nested.LateRepair);
        Assert.True(LastLine().LateRepair);
        Assert.NotEmpty(nested.LedgerDelta);
        Assert.Empty(outcome.Deltas);
        Assert.All(Requests().Skip(before), r => Assert.Contains(AttachmentPrompts.LateRepairText, r.UserPrompt));
        Assert.Contains(nested.Violations, v => v.Contains("adds a new detail after later emails were written"));
    }

    [Fact]
    public async Task Repair_SameThreadChain_V2GetsNewV1Text_NoSelfWait()
    {
        var thread = AttachmentTestData.Thread(_plan, AttachmentTestData.MultiMessageThreadId);
        var v1 = Spec(thread, 1, AttachmentType.Word).DocId;
        var v2 = Add(thread.Messages.Single(m => m.Seq == 3), AttachmentType.Word, "Site Visit Notes v2").DocId;
        Assert.Equal(v1, AttachmentChains.Compute(_plan, _draft.Guide)[v2].PreviousDocId);
        await Write(thread, Refuse(v1, v2));

        var outcome = await Repair(thread);

        Assert.Equal(2, outcome.Added);
        var v1Text = GenerationJournal.Replay(_folder, _plan).LastByAttachment[v1].FlattenedText;
        Assert.NotEmpty(v1Text);
        var prompt = Requests().Last(r => AttachmentIdOf(r) == v2).UserPrompt!;
        Assert.Contains(v1Text.Trim(), prompt);
        Assert.DoesNotContain(AttachmentPrompts.EarlierDraftNotAvailable, prompt);
    }

    // ---------- spend, cancel ----------

    private static LlmProviderResponse Fatal(int input, int output) => new()
    {
        Outcome = LlmOutcome.Failed,
        FailureKind = LlmFailureKind.Auth,
        Message = "invalid key",
        InputTokens = input,
        OutputTokens = output
    };

    [Fact]
    public async Task Repair_FatalAfterPaidCall_NotRunSpendReturned()
    {
        var thread = AttachmentTestData.Thread(_plan, AttachmentTestData.RelevantDocThreadId);
        var excel = Spec(thread, 1, AttachmentType.Excel).DocId;
        var slides = Spec(thread, 1, AttachmentType.PowerPoint).DocId;
        await Write(thread, Refuse(excel, slides));
        var before = Requests().Count;

        var outcome = await Repair(thread, intercept: r => AttachmentIdOf(r) == excel ? Fatal(800, 300) : null);

        Assert.DoesNotContain(Requests().Skip(before), r => AttachmentIdOf(r) == slides);
        Assert.Equal(_story.CalculateTotalCost(800, 300), outcome.NotRunCostUsd);
        Assert.Equal(800, outcome.NotRunInputTokens);
        Assert.Equal(300, outcome.NotRunOutputTokens);
        Assert.False(outcome.Unjournaled);
        Assert.DoesNotContain(JournalRecords().Where(r => r.Type == JournalRecordType.AttachmentsRepaired), r => true);
        Assert.Equal(JournalRecordType.AttachmentFailed, GenerationJournal.Replay(_folder, _plan).LastByAttachment[excel].Type);
    }

    [Fact]
    public async Task Repair_LineNotSaved_Unjournaled_SpendOnTotals_NotRunZero()
    {
        var thread = AttachmentTestData.Thread(_plan, AttachmentTestData.RelevantDocThreadId);
        var excel = Spec(thread, 1, AttachmentType.Excel).DocId;
        var slides = Spec(thread, 1, AttachmentType.PowerPoint).DocId;
        await Write(thread, Refuse(excel, slides));
        var costBefore = _snapshot.Drain().Counts.CostUsd;

        var outcome = await Repair(thread, intercept: r => AttachmentIdOf(r) == slides ? Fatal(800, 300) : null,
            beforeAppend: _ => Task.FromException(new IOException("journal locked")));

        var cost = _story.CalculateTotalCost(800, 300);
        Assert.True(outcome.Unjournaled);
        Assert.Equal(0, outcome.NotRunInputTokens);
        Assert.Equal(0, outcome.NotRunOutputTokens);
        Assert.Equal(0m, outcome.NotRunCostUsd);
        Assert.Equal(1600, outcome.InputTokens);
        Assert.Equal(600, outcome.OutputTokens);
        Assert.Equal(2 * cost, outcome.CostUsd + outcome.MediaCostUsd);
        Assert.Empty(outcome.Deltas);
        Assert.Equal(0, outcome.Added);
        Assert.DoesNotContain(JournalRecords(), r => r.Type == JournalRecordType.AttachmentsRepaired);
        var view = _snapshot.Drain();
        Assert.Equal(costBefore + 2 * cost, view.Counts.CostUsd);
        Assert.DoesNotContain(view.Events, e => e.Kind == GenerationEventKind.AttachmentsAdded);
    }

    [Fact]
    public async Task Repair_CancelDuringSecondAttachment_FirstCommitted_ThenThrows()
    {
        var thread = AttachmentTestData.Thread(_plan, AttachmentTestData.RelevantDocThreadId);
        var root = thread.Messages.Single(m => m.Seq == 1);
        var excel = Spec(thread, 1, AttachmentType.Excel).DocId;
        var slides = Spec(thread, 1, AttachmentType.PowerPoint).DocId;
        await Write(thread, Refuse(excel, slides));
        var start = Requests().Count;
        using var cts = new CancellationTokenSource();
        _fake.OnSend = index =>
        {
            if (index == start + 1)
                cts.Cancel();
        };

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Repair(thread, cts: cts));

        _fake.OnSend = null;
        var line = LastLine();
        Assert.Equal(JournalRecordType.AttachmentsRepaired, line.Type);
        var nested = Assert.Single(line.Attachments);
        Assert.Equal(excel, nested.AttachmentDocId);
        Assert.Equal(JournalRecordType.AttachmentDone, nested.Type);
        Assert.All(Copies(root), c =>
        {
            Assert.Contains(excel, PartIds(Load(c)));
            Assert.DoesNotContain(slides, PartIds(Load(c)));
        });
    }
}
