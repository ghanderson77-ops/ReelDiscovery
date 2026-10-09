using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using ReelDiscovery.Models.Case;
using ReelDiscovery.Models.Discovery;
using ReelDiscovery.Services.Ai;
using ReelDiscovery.Services.Discovery;
using ReelDiscovery.Tests.Case;
using ReelDiscovery.Tests.Fakes;
using ReelDiscovery.UserControls;

namespace ReelDiscovery.Tests.Discovery;

public partial class DiscoveryGeneratorTests : IDisposable
{
    private const string Witness = "CH-003";
    private const string RfpKey = "RFP";
    private const string QuestionText = "Did you review that schedule with the project team on site?";
    private const string AnswerText = "Yes, I reviewed it with the team that week.";
    private const string RfpText = "All Documents and Communications Concerning the revised project schedule.";

    private static readonly JsonSerializerOptions JsonOptions = new() { Converters = { new JsonStringEnumConverter() } };

    [GeneratedRegex(@"^- Slot (\d+) \|", RegexOptions.Multiline)]
    private static partial Regex SlotLineRegex();

    private readonly string _folder;
    private readonly CaseDraft _draft;
    private readonly FakeLlmProvider _fake;
    private readonly LlmGateway _gateway;
    private readonly DiscoverySettings _settings;
    private readonly string _depositionSchema = JsonSchemaRegistry.GetSchemaName(typeof(DiscoveryPrompts.DepositionChunkDto));

    // The deposition plan of the current run, so the fake can route chunk prompts
    private DepositionPlan? _plan;

    public DiscoveryGeneratorTests()
    {
        // Temp folder only: never a real case folder
        _folder = Path.Combine(Path.GetTempPath(), "rd-discgen-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_folder);

        _draft = DiscoveryTestData.LitigationDraft();
        _draft.FolderPath = _folder;
        _settings = new DiscoverySettings
        {
            WitnessIds = new List<string> { Witness },
            PagesPerExcerpt = 10,
            RfpEnabled = true,
            RfpCount = 5,
            RogEnabled = false,
            RfaEnabled = false
        };

        _fake = new FakeLlmProvider(LlmProviderKind.Anthropic);
        var story = CaseTestData.StoryModel();

        // $1 per token in and out, so costs are easy to add up
        story.InputTokenPricePerMillion = 1_000_000m;
        story.OutputTokenPricePerMillion = 1_000_000m;
        _gateway = new LlmGateway(_fake, story, CaseTestData.VolumeModel());
    }

    public void Dispose()
    {
        if (Directory.Exists(_folder))
            Directory.Delete(_folder, recursive: true);
    }

    // ---- helpers ----

    private sealed class SyncProgress : IProgress<DiscoveryProgress>
    {
        private readonly Action<DiscoveryProgress> _action;

        public SyncProgress(Action<DiscoveryProgress> action)
        {
            _action = action;
        }

        public void Report(DiscoveryProgress value) => _action(value);
    }

    private DiscoveryGenerator NewGenerator() => new(_gateway) { MaxParallel = 1 };

    private List<DiscoveryWorkItem> Items(params string[] keys)
    {
        var set = DiscoveryStore.Load(_folder, _draft.CaseId).Set;
        var items = DiscoveryWorklist.Build(_draft, _settings, null, set);
        if (keys.Length > 0)
            items = items.Where(i => keys.Contains(i.Key)).ToList();
        _plan = items.FirstOrDefault(i => i.Deposition != null)?.Deposition;
        return items;
    }

    private DiscoveryDocumentSet LoadSet()
    {
        var load = DiscoveryStore.Load(_folder, _draft.CaseId);
        Assert.True(load.Succeeded, load.Reason);
        return load.Set!;
    }

    private string DocxPath(DiscoveryWorkItem item) => DiscoveryStore.DocxPath(_folder, item.FileName)!;

    private static string DepKey => DiscoveryWorklist.DepositionKey(Witness);

    private bool IsDeposition(LlmProviderRequest request) => request.SchemaName == _depositionSchema;

    /// <summary>
    /// Routes each request by schema: deposition chunks to <paramref name="deposition"/>, written sets to
    /// <paramref name="written"/> (slot numbers parsed from the prompt).
    /// </summary>
    private void Script(Func<DepositionChunk, LlmProviderResponse>? deposition = null,
        Func<List<int>, LlmProviderResponse>? written = null)
    {
        deposition ??= chunk => Ok(CleanDeposition(chunk));
        written ??= slots => Ok(Rfp(slots));
        _fake.Respond = request =>
        {
            if (IsDeposition(request))
                return deposition(ChunkFor(request.UserPrompt));
            var slots = SlotLineRegex().Matches(request.UserPrompt)
                .Select(m => int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture)).ToList();
            return written(slots);
        };
    }

    private DepositionChunk ChunkFor(string prompt)
    {
        foreach (var chunk in _plan!.Chunks)
        {
            if (prompt.Contains("- Topic: " + chunk.TopicTitle + "\n", StringComparison.Ordinal)
                && prompt.Contains($"This is part {chunk.Part} of {chunk.PartCount} of this topic.", StringComparison.Ordinal))
                return chunk;
        }

        throw new InvalidOperationException("Prompt matches no chunk.");
    }

    private DiscoveryPrompts.DepositionChunkDto CleanDeposition(DepositionChunk chunk, string answer = AnswerText)
    {
        var examining = _plan!.Speakers.First(s => s.Role == SpeakerRole.Examining);
        var witness = _plan.Speakers.First(s => s.Role == SpeakerRole.Witness);
        var dto = new DiscoveryPrompts.DepositionChunkDto();
        for (var i = 0; i < chunk.TargetTurns; i++)
        {
            var question = i % 2 == 0;
            var exhibitSlot = i / 2;
            dto.Turns.Add(new DiscoveryPrompts.TurnDto
            {
                Kind = question ? TurnKind.Q : TurnKind.A,
                SpeakerId = question ? examining.SpeakerId : witness.SpeakerId,
                Text = question ? QuestionText : answer,
                ExhibitNumber = question && exhibitSlot < chunk.ExhibitNumbers.Count ? chunk.ExhibitNumbers[exhibitSlot] : 0
            });
        }

        return dto;
    }

    private static DiscoveryPrompts.RequestSetDto Rfp(IEnumerable<int> slots)
    {
        var dto = new DiscoveryPrompts.RequestSetDto();
        foreach (var slot in slots)
            dto.Requests.Add(new DiscoveryPrompts.RequestDto { Slot = slot, Text = RfpText });
        return dto;
    }

    private static LlmProviderResponse Ok(DiscoveryPrompts.DepositionChunkDto dto) =>
        FakeLlmProvider.Ok(JsonSerializer.Serialize(dto, JsonOptions));

    private static LlmProviderResponse Ok(DiscoveryPrompts.RequestSetDto dto) => FakeLlmProvider.Ok(JsonSerializer.Serialize(dto));

    // ---- tests ----

    [Fact]
    public async Task Run_DepositionAndRfp_SavesBothFilesAndStampedRecords()
    {
        Script();
        var items = Items();
        Assert.Equal(new[] { DepKey, RfpKey }, items.Select(i => i.Key));

        var result = await NewGenerator().RunAsync(_draft, null, items);

        Assert.Equal(string.Empty, result.FatalMessage);
        Assert.False(result.Cancelled);
        Assert.Equal(2, result.Total);
        Assert.Equal(2, result.Saved);
        Assert.Equal(0, result.Failed);
        Assert.True(result.CostUsd > 0m);
        foreach (var item in items)
        {
            Assert.True(File.Exists(DocxPath(item)));
            Assert.StartsWith(Path.Combine(_folder, DiscoveryStore.FolderName), DocxPath(item));
        }

        var set = LoadSet();
        Assert.Equal(CaseNumber.For(_draft), set.CaseNumber);
        Assert.Equal(2, set.Documents.Count);
        foreach (var doc in set.Documents)
        {
            Assert.Equal(DiscoveryStatus.Generated, doc.Status);
            Assert.True(doc.FileSaved);
            Assert.True(doc.HasContent);
            Assert.Equal(string.Empty, doc.FailureReason);
            Assert.NotEmpty(doc.GuideHash);
            Assert.NotEmpty(doc.CoreHash);
            Assert.NotEmpty(doc.CaptionHash);
            Assert.True(doc.CostUsd > 0m);
        }
        Assert.Equal(StaleSource.None, DiscoveryStaleness.Check(set.Find(DepKey)!, _draft));
        Assert.True(set.Find(DepKey)!.TestimonyPages > 0);
        Assert.Equal(5, set.Find(RfpKey)!.RequestCount);
        Assert.Equal(set.Documents.Sum(d => d.CostUsd), result.CostUsd);

        // Exhibits for the deponent are persisted
        var assigned = set.ExhibitAssignments.Where(a => a.WitnessId == Witness).Select(a => a.Number).ToList();
        Assert.NotEmpty(assigned);
        Assert.Equal(_plan!.Assignments.Select(a => a.Number), assigned);

        // The case number is assigned once: a persisted number is never recomputed
        set.CaseNumber = "9:99-cv-99999";
        await DiscoveryStore.SaveAsync(_folder, set);
        var second = await NewGenerator().RunAsync(_draft, null, Items(RfpKey));
        Assert.Equal(1, second.Saved);
        Assert.Equal("9:99-cv-99999", LoadSet().CaseNumber);
        Assert.Equal("9:99-cv-99999", LoadSet().Find(RfpKey)!.Written!.CaseNumber);
    }

    [Fact]
    public async Task RfpMissingRequestOnEveryAttempt_SavedWithWarnings_DepositionClean()
    {
        // Slot 1 is never returned: D4 on all three attempts
        Script(written: slots => Ok(Rfp(slots.Where(s => s != 1))));

        var result = await NewGenerator().RunAsync(_draft, null, Items());

        Assert.Equal(2, result.Saved);
        Assert.Equal(1, result.Warnings);
        var set = LoadSet();
        var rfp = set.Find(RfpKey)!;
        Assert.Equal(DiscoveryStatus.GeneratedWithWarnings, rfp.Status);
        Assert.Contains(rfp.Warnings, w => w.Rule == DiscoveryRule.D4MissingRequest && w.Number == 1);
        Assert.Equal(WrittenSetWriter.MaxAttempts, _fake.Requests.Count(r => !IsDeposition(r)));
        Assert.Equal(DiscoveryStatus.Generated, set.Find(DepKey)!.Status);
    }

    [Fact]
    public async Task RfpRefused_FailedWithAllAttemptsCosted_DepositionStillSaved()
    {
        Script(written: _ => FakeLlmProvider.Refused("Not comfortable"));

        var result = await NewGenerator().RunAsync(_draft, null, Items());

        Assert.Equal(string.Empty, result.FatalMessage);
        Assert.Equal(1, result.Saved);
        Assert.Equal(1, result.Failed);
        var set = LoadSet();
        var rfp = set.Find(RfpKey)!;
        Assert.Equal(DiscoveryStatus.Failed, rfp.Status);
        Assert.Equal(DiscoveryGenerator.ReasonRefused, rfp.FailureReason);
        Assert.False(rfp.HasContent);

        // Refusals carry 10 input + 2 output tokens at $1 each, for every attempt
        Assert.Equal(WrittenSetWriter.MaxAttempts, _fake.Requests.Count(r => !IsDeposition(r)));
        Assert.Equal(WrittenSetWriter.MaxAttempts * 12m, rfp.CostUsd);
        Assert.False(File.Exists(DocxPath(Items(RfpKey).Single())));

        Assert.Equal(DiscoveryStatus.Generated, set.Find(DepKey)!.Status);
        Assert.True(File.Exists(DocxPath(Items(DepKey).Single())));
    }

    [Fact]
    public async Task FatalAuth_StopsRun_KeepsFinishedDocuments_NoRecordForUnstarted()
    {
        Script(written: _ => FakeLlmProvider.Failed(LlmFailureKind.Auth, "The API key was rejected."));

        var result = await NewGenerator().RunAsync(_draft, null, Items());

        Assert.NotEqual(string.Empty, result.FatalMessage);
        Assert.Equal(1, result.Saved);
        Assert.Equal(0, result.Failed);
        Assert.Single(_fake.Requests, r => !IsDeposition(r));

        var set = LoadSet();
        Assert.NotNull(set.Find(DepKey));
        Assert.Null(set.Find(RfpKey));
        Assert.Equal(0m, set.UnattributedCostUsd);
    }

    [Fact]
    public async Task FatalOnFirstCall_NoRecordsAndNoFurtherCalls()
    {
        _fake.Respond = _ => FakeLlmProvider.Failed(LlmFailureKind.Auth, "The API key was rejected.");

        var result = await NewGenerator().RunAsync(_draft, null, Items());

        Assert.NotEqual(string.Empty, result.FatalMessage);
        Assert.Single(_fake.Requests);
        Assert.Equal(0, result.Saved);
        var load = DiscoveryStore.Load(_folder, _draft.CaseId);
        Assert.True(load.Missing || load.Set!.Documents.Count == 0);
    }

    [Fact]
    public async Task Cancel_AfterFirstDocument_KeepsFinishedDocument()
    {
        Script();
        var items = Items();
        using var cts = new CancellationTokenSource();
        var progress = new SyncProgress(p =>
        {
            // The deposition's calls are done when the RFP starts: cancel there
            if (p.Key == RfpKey && p.Part == 0)
                cts.Cancel();
        });

        var result = await NewGenerator().RunAsync(_draft, null, items, null, progress, cts.Token);

        Assert.True(result.Cancelled);
        Assert.Equal(string.Empty, result.FatalMessage);
        Assert.True(File.Exists(DocxPath(items[0])));
        Assert.False(File.Exists(DocxPath(items[1])));
        var set = LoadSet();
        Assert.NotNull(set.Find(DepKey));
        Assert.Null(set.Find(RfpKey));
        Assert.DoesNotContain(_fake.Requests, r => !IsDeposition(r));
    }

    [Fact]
    public async Task Cancel_MidDeposition_RecordsSpendOfFinishedChunks()
    {
        // CR-02: the first chunk is paid for, then the user cancels before the second
        using var cts = new CancellationTokenSource();
        Script(deposition: chunk =>
        {
            cts.Cancel();
            return Ok(CleanDeposition(chunk));
        });
        var items = Items(DepKey);
        Assert.True(_plan!.Chunks.Count >= 2);

        var result = await NewGenerator().RunAsync(_draft, null, items, null, null, cts.Token);

        Assert.True(result.Cancelled);
        Assert.Single(_fake.Requests);
        Assert.True(result.CostUsd > 0m);
        var set = LoadSet();
        Assert.Null(set.Find(DepKey));
        Assert.Equal(result.CostUsd, set.UnattributedCostUsd);
        Assert.Equal(result.CostUsd, set.TotalCostUsd);
        Assert.False(File.Exists(DocxPath(items[0])));
    }

    [Fact]
    public async Task FileOpenInWord_ContentKeptNotSaved_SaveAgainWritesWithoutModelCall()
    {
        Script();
        var items = Items(RfpKey);
        var path = DocxPath(items[0]);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, new byte[] { 1, 2, 3 });

        DiscoveryRunResult result;
        using (new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            result = await NewGenerator().RunAsync(_draft, null, items);

            // Still open: Save Again reports it without writing
            var stillOpen = await DiscoveryGenerator.SaveAgainAsync(_draft, RfpKey);
            Assert.True(stillOpen.StillOpen);
            Assert.False(stillOpen.Saved);
        }

        Assert.Equal(0, result.Saved);
        Assert.Equal(1, result.NotSaved);
        var record = LoadSet().Find(RfpKey)!;
        Assert.True(record.HasContent);
        Assert.False(record.FileSaved);
        Assert.Equal(string.Empty, record.FailureReason);
        Assert.Equal(3, File.ReadAllBytes(path).Length);

        var requests = _fake.Requests.Count;
        var saved = await DiscoveryGenerator.SaveAgainAsync(_draft, RfpKey);

        Assert.True(saved.Saved);
        Assert.Equal(string.Empty, saved.Error);
        Assert.Equal(items[0].FileName, saved.FileName);
        Assert.Equal(requests, _fake.Requests.Count);
        Assert.True(LoadSet().Find(RfpKey)!.FileSaved);

        // A real .docx (zip package) replaced the placeholder bytes
        var written = File.ReadAllBytes(path);
        Assert.True(written.Length > 3);
        Assert.Equal((byte)'P', written[0]);
        Assert.Equal((byte)'K', written[1]);
    }

    [Fact]
    public async Task WriteFailure_IsNotSaved_NotRewrittenByGenerate_SaveAgainClearsIt()
    {
        // CR-01: a non-lock write failure (a directory squats on the .docx path) keeps the paid content
        Script();
        var items = Items(RfpKey);
        var path = DocxPath(items[0]);
        Directory.CreateDirectory(path);

        var result = await NewGenerator().RunAsync(_draft, null, items);

        Assert.Equal(1, result.NotSaved);
        Assert.Equal(0, result.Failed);
        var record = LoadSet().Find(RfpKey)!;
        Assert.True(record.HasContent);
        Assert.False(record.FileSaved);
        Assert.True(DiscoveryGenerator.IsWriteFailure(record.FailureReason));

        // The row is Not saved (Save Again), never Failed, so Generate Documents does not pay for it again
        var model = DiscoveryStepModel.Build(_draft, LoadSet(), DiscoveryCorpus.None(), CaseTestData.StoryModel(), _folder);
        var row = model.Rows.Single(r => r.Key == RfpKey);
        Assert.Equal(DiscoveryRowState.NotSaved, row.State);
        Assert.False(row.PreviousKept);
        Assert.StartsWith("Not saved: the Word file could not be written", DiscoveryUiText.RowStatusFull(row));
        Assert.DoesNotContain(model.ToWrite, i => i.Key == RfpKey);

        var requests = _fake.Requests.Count;
        Directory.Delete(path);
        var saved = await DiscoveryGenerator.SaveAgainAsync(_draft, RfpKey);

        Assert.True(saved.Saved);
        Assert.Equal(requests, _fake.Requests.Count);
        var after = LoadSet().Find(RfpKey)!;
        Assert.True(after.FileSaved);
        Assert.Equal(string.Empty, after.FailureReason);
    }

    [Fact]
    public async Task RegenerateFails_KeepsPreviousContentAndFile_AddsCost_SendsGuidance()
    {
        Script();
        await NewGenerator().RunAsync(_draft, null, Items());
        var before = LoadSet().Find(RfpKey)!;
        var item = Items(RfpKey).Single();
        var bytesBefore = File.ReadAllBytes(DocxPath(item));
        var firstCount = _fake.Requests.Count;

        Script(written: _ => FakeLlmProvider.Refused("Not comfortable"));
        var guidance = new Dictionary<string, string> { [RfpKey] = "focus on payment" };
        var result = await NewGenerator().RunAsync(_draft, null, new[] { item }, guidance);

        Assert.Equal(1, result.Failed);
        var after = LoadSet().Find(RfpKey)!;
        Assert.Equal(DiscoveryGenerator.ReasonRefused, after.FailureReason);
        Assert.True(after.HasContent);
        Assert.Equal(before.Status, after.Status);
        Assert.Equal(before.Written!.Requests.Select(r => r.Text), after.Written!.Requests.Select(r => r.Text));
        Assert.Equal(before.CostUsd + result.CostUsd, after.CostUsd);
        Assert.True(result.CostUsd > 0m);
        Assert.Equal(bytesBefore, File.ReadAllBytes(DocxPath(item)));
        Assert.Contains("focus on payment", _fake.Requests[firstCount].UserPrompt);
    }

    [Fact]
    public async Task RegenerateSucceeds_ReplacesContent_ClearsFailure_KeepsExhibitNumbers()
    {
        Script();
        await NewGenerator().RunAsync(_draft, null, Items());
        var exhibitsBefore = LoadSet().ExhibitAssignments.Where(a => a.WitnessId == Witness)
            .Select(a => (a.Number, a.Description)).ToList();
        Assert.NotEmpty(exhibitsBefore);

        // A failed regenerate first leaves a FailureReason
        Script(deposition: _ => FakeLlmProvider.Refused("Not comfortable"));
        await NewGenerator().RunAsync(_draft, null, Items(DepKey));
        var failed = LoadSet().Find(DepKey)!;
        Assert.Equal(DiscoveryGenerator.ReasonRefused, failed.FailureReason);
        var costBefore = failed.CostUsd;

        const string NewAnswer = "No, I never saw that schedule before the meeting.";
        Script(deposition: chunk => Ok(CleanDeposition(chunk, NewAnswer)));
        var result = await NewGenerator().RunAsync(_draft, null, Items(DepKey));

        Assert.Equal(1, result.Saved);
        var set = LoadSet();
        var doc = set.Find(DepKey)!;
        Assert.Equal(string.Empty, doc.FailureReason);
        Assert.Equal(DiscoveryStatus.Generated, doc.Status);
        Assert.Contains(doc.Deposition!.Turns, t => t.Text == NewAnswer);
        Assert.DoesNotContain(doc.Deposition.Turns, t => t.Text == AnswerText);
        Assert.Equal(costBefore + result.CostUsd, doc.CostUsd);
        Assert.Equal(exhibitsBefore, set.ExhibitAssignments.Where(a => a.WitnessId == Witness).Select(a => (a.Number, a.Description)).ToList());
        Assert.Equal(exhibitsBefore.Select(e => e.Number), doc.Deposition.Exhibits.Select(e => e.Number));
    }

    [Fact]
    public async Task SidecarFromNewerVersion_FatalAndNothingWritten()
    {
        Script();
        var items = Items();
        var json = $"{{\"FormatVersion\":99,\"CaseId\":\"{_draft.CaseId}\"}}";
        File.WriteAllText(DiscoveryStore.SetPath(_folder), json);

        var result = await NewGenerator().RunAsync(_draft, null, items);

        Assert.Contains("newer version", result.FatalMessage);
        Assert.Empty(_fake.Requests);
        Assert.False(Directory.Exists(DiscoveryStore.FolderPath(_folder)));
        Assert.Equal(json, File.ReadAllText(DiscoveryStore.SetPath(_folder)));
    }

    [Fact]
    public void MaxParallelCalls_IsThreeAndSchemasAreTheDiscoveryDtos()
    {
        Assert.Equal(3, DiscoveryGenerator.MaxParallelCalls);
        Assert.Equal(3, new DiscoveryGenerator(_gateway).MaxParallel);
        Assert.Equal(DiscoveryPrompts.SchemaTypes, DiscoveryGenerator.SchemaTypes);
    }
}
