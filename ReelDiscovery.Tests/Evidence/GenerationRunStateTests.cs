using System.Text.Json;
using ReelDiscovery.Models;
using ReelDiscovery.Models.Case;
using ReelDiscovery.Models.Corpus;
using ReelDiscovery.Models.Evidence;
using ReelDiscovery.Services.Corpus;
using ReelDiscovery.Services.Evidence;

namespace ReelDiscovery.Tests.Evidence;

public class GenerationRunStateTests
{
    private static readonly DateTime Now = new(2026, 10, 3, 18, 30, 0, DateTimeKind.Utc);

    // Relevant thread ids of the fixture plan, ordinal order
    private static List<PlannedThread> Relevant(CorpusPlan plan) =>
        plan.Threads.Where(t => t.Relevance == ThreadRelevance.Relevant).OrderBy(t => t.Id, StringComparer.Ordinal).ToList();

    internal static JournalRecord Done(CorpusPlan plan, PlannedThread thread, ThreadWriteStatus status, decimal cost,
        string folder = "alex.morgan")
    {
        var record = new JournalRecord
        {
            Type = JournalRecordType.ThreadDone,
            Utc = Now,
            PlanId = plan.PlanId,
            ThreadId = thread.Id,
            Status = status,
            Attempts = 1,
            CostUsd = cost,
            InputTokens = 1000,
            OutputTokens = 400
        };
        if (status == ThreadWriteStatus.Warnings)
            record.Violations = new List<string> { "Message 2 is shorter than its length band", "Message 1 names an unknown person" };

        foreach (var message in thread.Messages.OrderBy(m => m.Seq))
        {
            record.Messages.Add(new JournalMessage { DocId = message.DocId, MessageId = message.Id, Subject = "x", BodyPlain = "Body." });
            record.Files.Add($"Mailboxes/{folder}/Sent Items/{message.DocId}.eml");
            record.Files.Add($"Mailboxes/jamie.cole/Inbox/{message.DocId}.eml");
        }

        record.LedgerDelta.Add(new LedgerDelta { Seq = 1, Kind = LedgerKind.Commitment, About = "site walk", Statement = $"Site walk agreed in {thread.Id}." });
        return record;
    }

    internal static JournalRecord Failed(CorpusPlan plan, PlannedThread thread, decimal cost) => new()
    {
        Type = JournalRecordType.ThreadFailed,
        Utc = Now,
        PlanId = plan.PlanId,
        ThreadId = thread.Id,
        FailureKind = ThreadFailureKind.Provider,
        Reason = "The provider returned an error",
        CostUsd = cost
    };

    internal static JournalReplay Replay(params JournalRecord[] records)
    {
        var replay = new JournalReplay { Exists = true, HasRunStarted = true };
        foreach (var record in records)
            replay.LastByThread[record.ThreadId] = record;
        return replay;
    }

    // Written, Warnings and Failed on the first three relevant threads
    internal static (CaseDraft Draft, CorpusPlan Plan, GenerationRunState State, List<PlannedThread> Threads) Mixed()
    {
        var (draft, plan) = EvidenceTestData.ApprovedCase();
        var threads = Relevant(plan);
        var replay = Replay(
            Done(plan, threads[0], ThreadWriteStatus.Written, 0.10m),
            Done(plan, threads[1], ThreadWriteStatus.Warnings, 0.20m, "pat.lee"),
            Failed(plan, threads[2], 0.05m));
        return (draft, plan, GenerationRunState.From(plan, replay), threads);
    }

    [Fact]
    public void RelevantThreadWithoutMessages_IsNeitherScheduledNorPending()
    {
        var (_, plan) = EvidenceTestData.ApprovedCase();
        var written = Relevant(plan);
        plan.Threads.Add(new PlannedThread { Id = "T-0999", Relevance = ThreadRelevance.Relevant, Messages = new List<PlannedMessage>() });

        var state = GenerationRunState.From(plan,
            Replay(written.Select(t => Done(plan, t, ThreadWriteStatus.Written, 0.01m)).ToArray()));

        Assert.DoesNotContain(WavePlanner.Plan(plan), w => w.ThreadIds.Contains("T-0999"));
        Assert.False(RenderAssignments.Compute(plan).ContainsKey("T-0999"));
        Assert.Equal(written.Count, state.RelevantTotal);
        Assert.Equal(state.RelevantTotal, state.RelevantWritten);
        Assert.False(state.StatusByThread.ContainsKey("T-0999"));
    }

    [Fact]
    public void From_ProjectsStatusesAndCounts()
    {
        var (_, plan, state, threads) = Mixed();
        var relevant = Relevant(plan);

        Assert.True(relevant.Count > 3);
        Assert.Equal(relevant.Count, state.RelevantTotal);
        Assert.Equal(relevant.Count + state.NonRelevantTotal, state.TotalThreads);
        Assert.Equal(ThreadRunStatus.Written, state.StatusByThread[threads[0].Id]);
        Assert.Equal(ThreadRunStatus.Warnings, state.StatusByThread[threads[1].Id]);
        Assert.Equal(ThreadRunStatus.Failed, state.StatusByThread[threads[2].Id]);
        foreach (var other in relevant.Skip(3))
            Assert.Equal(ThreadRunStatus.Pending, state.StatusByThread[other.Id]);

        Assert.Equal(1, state.Written);
        Assert.Equal(1, state.Warnings);
        Assert.Equal(1, state.Failed);
        Assert.Equal(relevant.Count - 3 + state.NonRelevantTotal, state.Pending);
        Assert.Equal(threads[0].Messages.Count + threads[1].Messages.Count, state.EmailsWritten);
        Assert.Equal(threads[2].Messages.Count, state.EmailsFailed);
        Assert.Equal(2 * (threads[0].Messages.Count + threads[1].Messages.Count), state.FilesWritten);
        // alex.morgan, pat.lee and jamie.cole
        Assert.Equal(3, state.Mailboxes);
        Assert.Equal(0.35m, state.CostUsd);
        Assert.True(state.HasRun);
        Assert.False(state.PlanChanged);
        Assert.Equal(string.Empty, state.UnreadableReason);

        Assert.Equal(2, state.DeltasByThread.Count);
        Assert.Single(state.DeltasByThread[threads[0].Id]);
        Assert.False(state.DeltasByThread.ContainsKey(threads[2].Id));
    }

    [Fact]
    public void From_LastRecordWins_FailedAfterDone()
    {
        var (_, plan) = EvidenceTestData.ApprovedCase();
        var thread = Relevant(plan)[0];
        var replay = new JournalReplay { Exists = true };
        replay.LastByThread[thread.Id] = Done(plan, thread, ThreadWriteStatus.Written, 0.1m);
        replay.LastByThread[thread.Id] = Failed(plan, thread, 0.02m);

        var state = GenerationRunState.From(plan, replay);

        Assert.Equal(ThreadRunStatus.Failed, state.StatusByThread[thread.Id]);
        Assert.False(state.IsDone(thread.Id));
        Assert.Equal(0, state.Written);
        Assert.Equal(1, state.Failed);
        Assert.Equal(0, state.FilesWritten);
        Assert.True(state.HasRun);
    }

    [Fact]
    public void From_NoJournal_AllPending()
    {
        var (_, plan) = EvidenceTestData.ApprovedCase();

        var state = GenerationRunState.From(plan, new JournalReplay());

        Assert.False(state.HasRun);
        Assert.Equal(state.TotalThreads, state.Pending);
        Assert.All(state.StatusByThread.Values, s => Assert.Equal(ThreadRunStatus.Pending, s));
        Assert.Equal(0m, state.CostUsd);
    }

    [Fact]
    public void From_CopiesPlanChangedAndReason()
    {
        var (_, plan) = EvidenceTestData.ApprovedCase();
        var replay = new JournalReplay { Exists = true, PlanChanged = true, Reason = "The saved run could not be read: locked." };

        var state = GenerationRunState.From(plan, replay);

        Assert.True(state.PlanChanged);
        Assert.Equal("The saved run could not be read: locked.", state.UnreadableReason);
    }

    [Fact]
    public void IsDone_OnlyWrittenAndWarnings()
    {
        var (_, plan, state, threads) = Mixed();

        Assert.True(state.IsDone(threads[0].Id));
        Assert.True(state.IsDone(threads[1].Id));
        Assert.False(state.IsDone(threads[2].Id));
        Assert.False(state.IsDone(Relevant(plan)[3].Id));
        Assert.False(state.IsDone("T-9999"));
    }

    [Fact]
    public void BuildOverlay_MapsStatusesAndComposerSubjects()
    {
        var (draft, plan, state, threads) = Mixed();
        var assignments = RenderAssignments.Compute(plan);

        var overlay = state.BuildOverlay(plan, assignments);

        foreach (var thread in Relevant(plan))
        {
            var expected = state.StatusByThread[thread.Id] switch
            {
                ThreadRunStatus.Written => AnswerKeyBuilder.StatusGenerated,
                ThreadRunStatus.Warnings => AnswerKeyBuilder.StatusGeneratedWithWarnings,
                ThreadRunStatus.Failed => AnswerKeyBuilder.StatusFailed,
                _ => AnswerKeyBuilder.StatusPlanned
            };
            var marking = assignments.TryGetValue(thread.Id, out var a) ? a.Marking : PrivilegeMarking.None;
            foreach (var message in thread.Messages)
            {
                var entry = overlay.ByDocId[message.DocId];
                Assert.Equal(expected, entry.Status);
                Assert.Equal(EmailComposer.SubjectFor(thread, message, marking), entry.Subject);
            }
        }

        Assert.Equal("Generated", overlay.ByDocId[threads[0].Messages[0].DocId].Status);
        Assert.Equal("Generated - with warnings", overlay.ByDocId[threads[1].Messages[0].DocId].Status);
        Assert.Equal("Failed", overlay.ByDocId[threads[2].Messages[0].DocId].Status);

        // Pending non-relevant emails are overlaid as Planned (06-14: the overlay covers the whole corpus)
        foreach (var message in plan.Threads.Where(t => t.Relevance != ThreadRelevance.Relevant).SelectMany(t => t.Messages))
            Assert.Equal(AnswerKeyBuilder.StatusPlanned, overlay.ByDocId[message.DocId].Status);

        var baseline = AnswerKeyBuilder.Build(plan, draft.Guide!);
        var rows = AnswerKeyBuilder.Build(plan, draft.Guide!, overlay);
        Assert.All(rows.Where(r => r.DocType == AnswerKeyBuilder.DocTypeAttachment),
            r => Assert.Equal(AnswerKeyBuilder.StatusPlanned, r.Status));
        var nonRelevant = rows.Where(r => r.Relevance == AnswerKeyBuilder.NotRelevantLabel).ToList();
        var nonRelevantBaseline = baseline.Where(r => r.Relevance == AnswerKeyBuilder.NotRelevantLabel).ToList();
        Assert.Equal(nonRelevantBaseline.Select(r => r.Subject + "|" + r.Status), nonRelevant.Select(r => r.Subject + "|" + r.Status));
    }

    [Fact]
    public void BuildOverlay_BannerAndSubjectMarking_PrefixesSubject()
    {
        var (_, plan, state, threads) = Mixed();
        var assignments = RenderAssignments.Compute(plan);
        var thread = threads[0];
        assignments[thread.Id] = new ThreadAssignment { ThreadId = thread.Id, Marking = PrivilegeMarking.BannerAndSubject };

        var overlay = state.BuildOverlay(plan, assignments);

        Assert.StartsWith(EmailComposer.PrivilegedSubjectTag, overlay.ByDocId[thread.Messages[0].DocId].Subject);
    }

    [Fact]
    public void ToCounts_FillsGenerationCounts()
    {
        var (_, plan, state, threads) = Mixed();

        var counts = state.ToCounts(plan, waveCount: 4, ledgerEntries: 7, remainingCost: 1.25m, running: true, finished: false);

        Assert.Equal(state.TotalThreads, counts.TotalThreads);
        Assert.Equal(2, counts.DoneThreads);
        Assert.Equal(1, counts.WarningThreads);
        Assert.Equal(1, counts.FailedThreads);
        Assert.Equal(4, counts.WaveCount);
        Assert.Equal(GenerationThreads(plan).Sum(t => t.Messages.Count), counts.EmailsPlanned);
        Assert.Equal(state.EmailsWritten, counts.EmailsWritten);
        Assert.Equal(state.FilesWritten, counts.FilesWritten);
        Assert.Equal(3, counts.Mailboxes);
        Assert.Equal(7, counts.LedgerEntries);
        Assert.Equal(0.35m, counts.CostUsd);
        Assert.Equal(1.25m, counts.RemainingCostUsd);
        Assert.True(counts.Running);
        Assert.False(counts.Finished);
    }

    [Fact]
    public void EstimateRemaining_CountsMessagesOfThreadsNotDone()
    {
        var (_, plan, state, threads) = Mixed();
        var model = new AIModelConfig { DisplayName = "Test Model", InputTokenPricePerMillion = 4m, OutputTokenPricePerMillion = 20m };
        var remainingMessages = Relevant(plan).Where(t => !state.IsDone(t.Id)).Sum(t => t.Messages.Count);

        var estimate = GenerationRunState.EstimateRemaining(plan, state, model);

        var perMessage = model.CalculateTotalCost(GenerationCostEstimator.RelevantInputPerMessage,
            GenerationCostEstimator.RelevantOutputPerMessage) * GenerationCostEstimator.QaOverhead;
        Assert.Equal(remainingMessages * perMessage, estimate);
        Assert.Equal(0m, GenerationRunState.EstimateRemaining(plan, state, null));
    }

    private static NarrativeLedger Ledger(params string[] ids)
    {
        var ledger = new NarrativeLedger();
        foreach (var id in ids)
            ledger.Entries.Add(new LedgerEntry { Id = id, Date = "2024-03-04", Kind = LedgerKind.Figure, Statement = "Quote issued", Value = "$18,425" });
        return ledger;
    }

    private static List<WrittenCorpusFile> KeyFiles(CorpusPlan plan) => new()
    {
        new WrittenCorpusFile { Kind = CorpusFileKind.AnswerKeyCsv, FileName = AnswerKeyCsvWriter.FileName, PlanId = plan.PlanId, WrittenUtc = Now },
        new WrittenCorpusFile { Kind = CorpusFileKind.StorySoFar, FileName = "Case - Story So Far.docx", PlanId = plan.PlanId, WrittenUtc = Now }
    };

    [Fact]
    public void Apply_NotFinal_StoresLedgerCountsAndFiles()
    {
        var (draft, plan, state, _) = Mixed();
        var protocol = new WrittenCorpusFile { Kind = CorpusFileKind.ReviewProtocol, FileName = "Case - Review Protocol.docx", PlanId = plan.PlanId };
        var oldCsv = new WrittenCorpusFile { Kind = CorpusFileKind.AnswerKeyCsv, FileName = AnswerKeyCsvWriter.FileName, PlanId = Guid.NewGuid() };
        draft.Corpus!.WrittenFiles = new List<WrittenCorpusFile> { protocol, oldCsv };
        draft.Corpus.Generation = null;
        var stageBefore = draft.Stage;
        var ledger = Ledger("T-0001-L01", "T-0001-L02");

        GenerationStateApplier.Apply(draft, plan, state, ledger, KeyFiles(plan), GenerationOutcome.Finished, "done", Now, final: false);

        var generation = draft.Corpus.Generation;
        Assert.NotNull(generation);
        Assert.Equal(plan.PlanId, generation!.RunPlanId);
        Assert.Same(ledger, generation.Ledger);
        Assert.Equal(state.RelevantTotal, generation.ThreadsTotal);
        Assert.Equal(1, generation.ThreadsWritten);
        Assert.Equal(1, generation.ThreadsWithWarnings);
        Assert.Equal(1, generation.ThreadsFailed);
        Assert.Equal(state.EmailsWritten, generation.EmailsWritten);
        Assert.Equal(state.EmailsFailed, generation.EmailsFailed);
        Assert.Equal(state.FilesWritten, generation.FilesWritten);
        Assert.Equal(3, generation.Mailboxes);
        Assert.Equal(0.35m, generation.CostUsd);
        Assert.Equal(Now, generation.ProjectedUtc);
        Assert.Equal(GenerationOutcome.None, generation.LastOutcome);
        Assert.Null(generation.LastRunEndedUtc);
        Assert.Equal(stageBefore, draft.Stage);

        var files = draft.Corpus.WrittenFiles;
        Assert.Equal(3, files.Count);
        Assert.Contains(protocol, files);
        Assert.Equal(plan.PlanId, files.Single(f => f.Kind == CorpusFileKind.AnswerKeyCsv).PlanId);
        Assert.Single(files, f => f.Kind == CorpusFileKind.StorySoFar);
    }

    [Fact]
    public void Apply_Final_RecordsOutcomeAndAdvancesStage()
    {
        var (draft, plan, state, _) = Mixed();
        Assert.True(draft.Stage < CaseStage.Generation);

        GenerationStateApplier.Apply(draft, plan, state, Ledger("T-0001-L01"), new List<WrittenCorpusFile>(),
            GenerationOutcome.Cancelled, "Cancelled by the user", Now, final: true);

        var generation = draft.Corpus!.Generation!;
        Assert.Equal(GenerationOutcome.Cancelled, generation.LastOutcome);
        Assert.Equal("Cancelled by the user", generation.LastMessage);
        Assert.Equal(Now, generation.LastRunEndedUtc);
        Assert.Equal(CaseStage.Generation, draft.Stage);
    }

    [Fact]
    public void Apply_Final_NeverMovesStageBackwards()
    {
        var (draft, plan, state, _) = Mixed();
        draft.AdvanceStage(CaseStage.Summary);

        GenerationStateApplier.Apply(draft, plan, state, new NarrativeLedger(), new List<WrittenCorpusFile>(),
            GenerationOutcome.Finished, string.Empty, Now, final: true);

        Assert.Equal(CaseStage.Summary, draft.Stage);
    }

    [Fact]
    public void Apply_KeepsExistingGenerationErrorsAndOutcomeWhenNotFinal()
    {
        var (draft, plan, state, _) = Mixed();
        draft.Corpus!.Generation = new GenerationState { LastOutcome = GenerationOutcome.Fatal, LastMessage = "stopped", KeyError = "locked" };

        GenerationStateApplier.Apply(draft, plan, state, new NarrativeLedger(), new List<WrittenCorpusFile>(),
            GenerationOutcome.Finished, "ignored", Now, final: false);

        Assert.Equal(GenerationOutcome.Fatal, draft.Corpus.Generation!.LastOutcome);
        Assert.Equal("stopped", draft.Corpus.Generation.LastMessage);
        Assert.Equal("locked", draft.Corpus.Generation.KeyError);
    }

    [Fact]
    public void Clear_RemovesGenerationAndStoryFile()
    {
        var (draft, plan, state, _) = Mixed();
        var protocol = new WrittenCorpusFile { Kind = CorpusFileKind.ReviewProtocol, FileName = "Case - Review Protocol.docx", PlanId = plan.PlanId };
        draft.Corpus!.WrittenFiles = new List<WrittenCorpusFile> { protocol };
        GenerationStateApplier.Apply(draft, plan, state, Ledger("T-0001-L01"), KeyFiles(plan), GenerationOutcome.Finished, "", Now, final: true);

        GenerationStateApplier.Clear(draft);

        Assert.Null(draft.Corpus.Generation);
        Assert.DoesNotContain(draft.Corpus.WrittenFiles, f => f.Kind == CorpusFileKind.StorySoFar);
        Assert.Contains(protocol, draft.Corpus.WrittenFiles);
        Assert.Contains(draft.Corpus.WrittenFiles, f => f.Kind == CorpusFileKind.AnswerKeyCsv);
    }

    [Fact]
    public void Clear_NoCorpus_IsNoOp()
    {
        var draft = new CaseDraft();

        GenerationStateApplier.Clear(draft);

        Assert.Null(draft.Corpus);
    }
    // ---- GenerationOutputs (Task 3) ----

    private const int SharingViolation = unchecked((int)0x80070020);

    private static AnswerKeyOverlay MixedOverlay(CorpusPlan plan, GenerationRunState state) =>
        state.BuildOverlay(plan, RenderAssignments.Compute(plan));

    [Fact]
    public async Task WriteAnswerKeyAsync_WritesCsvAndXlsxWithOverlay()
    {
        var (draft, plan, state, threads) = Mixed();
        var folder = EvidenceTestData.TempCaseFolder();
        try
        {
            var result = await GenerationOutputs.WriteAnswerKeyAsync(folder, draft.Guide!, plan, MixedOverlay(plan, state));

            Assert.True(result.Succeeded);
            Assert.Equal(new[] { CorpusFileKind.AnswerKeyCsv, CorpusFileKind.AnswerKeyXlsx }, result.Files.Select(f => f.Kind));
            Assert.All(result.Files, f => Assert.Equal(plan.PlanId, f.PlanId));
            Assert.Equal(AnswerKeyCsvWriter.FileName, result.Files[0].FileName);
            Assert.Equal(AnswerKeyXlsxWriter.FileName, result.Files[1].FileName);
            Assert.True(File.Exists(Path.Combine(folder, AnswerKeyXlsxWriter.FileName)));

            var csv = await File.ReadAllTextAsync(Path.Combine(folder, AnswerKeyCsvWriter.FileName));
            var written = csv.Split('\n').Select(l => l.TrimEnd('\r'))
                .First(l => l.StartsWith(threads[0].Messages[0].DocId + ",", StringComparison.Ordinal));
            Assert.Contains("Generated", written.Split(','));
            Assert.Contains("Generated - with warnings", csv);
            Assert.Contains("Failed", csv);
        }
        finally
        {
            EvidenceTestData.DeleteFolder(folder);
        }
    }

    [Fact]
    public async Task WriteAnswerKeyAsync_LockedCsv_StillWritesXlsx()
    {
        var (draft, plan, state, _) = Mixed();
        var written = new List<string>();
        Task Writer(string path, byte[] bytes, CancellationToken ct)
        {
            if (path.EndsWith(AnswerKeyCsvWriter.FileName, StringComparison.Ordinal))
                throw new IOException("The file is in use", SharingViolation);
            written.Add(Path.GetFileName(path));
            return Task.CompletedTask;
        }

        var result = await GenerationOutputs.WriteAnswerKeyAsync(@"C:\Cases\Brightline", draft.Guide!, plan, MixedOverlay(plan, state), Writer);

        Assert.False(result.Succeeded);
        Assert.Equal(AnswerKeyCsvWriter.FileName, result.FailedFileName);
        Assert.Equal("The file is in use", result.Error);
        Assert.True(result.FileOpenElsewhere);
        Assert.Equal(CorpusFileKind.AnswerKeyXlsx, Assert.Single(result.Files).Kind);
        Assert.Equal(new[] { AnswerKeyXlsxWriter.FileName }, written);
    }

    [Fact]
    public async Task WriteAnswerKeyAsync_PlainIoError_NotOpenElsewhere_FirstFailureKept()
    {
        var (draft, plan, state, _) = Mixed();
        Task Writer(string path, byte[] bytes, CancellationToken ct) => throw new IOException("Disk full: " + Path.GetFileName(path));

        var result = await GenerationOutputs.WriteAnswerKeyAsync(@"C:\Cases\Brightline", draft.Guide!, plan, MixedOverlay(plan, state), Writer);

        Assert.Equal(AnswerKeyCsvWriter.FileName, result.FailedFileName);
        Assert.Equal("Disk full: answer-key.csv", result.Error);
        Assert.False(result.FileOpenElsewhere);
        Assert.Empty(result.Files);
    }

    [Fact]
    public async Task WriteAnswerKeyAsync_Cancellation_Propagates()
    {
        var (draft, plan, state, _) = Mixed();
        Task Writer(string path, byte[] bytes, CancellationToken ct) => throw new OperationCanceledException();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            GenerationOutputs.WriteAnswerKeyAsync(@"C:\Cases\Brightline", draft.Guide!, plan, MixedOverlay(plan, state), Writer));
    }

    [Fact]
    public async Task WriteStorySoFarAsync_WritesFolderNamedDocx()
    {
        var (draft, plan, state, _) = Mixed();
        var root = EvidenceTestData.TempCaseFolder();
        var folder = Path.Combine(root, "Brightline v Harmon");
        Directory.CreateDirectory(folder);
        try
        {
            var people = PeopleDirectory.Create(draft.Guide!, plan);
            var result = await GenerationOutputs.WriteStorySoFarAsync(folder + Path.DirectorySeparatorChar, draft, plan,
                Ledger("T-0001-L01"), state, people);

            Assert.True(result.Succeeded);
            var file = Assert.Single(result.Files);
            Assert.Equal(CorpusFileKind.StorySoFar, file.Kind);
            Assert.Equal("Brightline v Harmon - Story So Far.docx", file.FileName);
            Assert.Equal(plan.PlanId, file.PlanId);
            Assert.True(File.Exists(Path.Combine(folder, "Brightline v Harmon - Story So Far.docx")));
        }
        finally
        {
            EvidenceTestData.DeleteFolder(root);
        }
    }

    [Fact]
    public async Task WriteStorySoFarAsync_IoError_IsReported()
    {
        var (draft, plan, state, _) = Mixed();
        var people = PeopleDirectory.Create(draft.Guide!, plan);
        Task Writer(string path, byte[] bytes, CancellationToken ct) => throw new UnauthorizedAccessException("Access denied");

        var result = await GenerationOutputs.WriteStorySoFarAsync(@"C:\Cases\Brightline v Harmon", draft, plan,
            Ledger("T-0001-L01"), state, people, Writer);

        Assert.False(result.Succeeded);
        Assert.Equal("Brightline v Harmon - Story So Far.docx", result.FailedFileName);
        Assert.Equal("Access denied", result.Error);
        Assert.Empty(result.Files);
    }

    [Fact]
    public async Task WriteStorySoFarAsync_Cancellation_Propagates()
    {
        var (draft, plan, state, _) = Mixed();
        var people = PeopleDirectory.Create(draft.Guide!, plan);
        Task Writer(string path, byte[] bytes, CancellationToken ct) => throw new OperationCanceledException();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => GenerationOutputs.WriteStorySoFarAsync(
            @"C:\Cases\Brightline v Harmon", draft, plan, Ledger("T-0001-L01"), state, people, Writer));
    }

    // ---- 06-14: whole corpus, attachments, repairs, shared estimate ----

    private const string NoKeyReason = "no tested OpenAI key this session";

    internal static IEnumerable<PlannedThread> GenerationThreads(CorpusPlan plan) =>
        plan.Threads.Where(t => !string.IsNullOrEmpty(t.Id) && t.Messages.Count > 0)
            .GroupBy(t => t.Id, StringComparer.Ordinal)
            .Select(g => g.First());

    internal static PlannedMessage ParentOf(CorpusPlan plan, string attachmentDocId) =>
        plan.Threads.SelectMany(t => t.Messages).First(m => m.Attachments.Any(a => a.DocId == attachmentDocId));

    internal static JournalRecord Attachment(CorpusPlan plan, PlannedAttachment attachment, JournalRecordType type,
        ThreadWriteStatus status = ThreadWriteStatus.Written, string reason = "", string fileName = "", decimal media = 0m,
        bool lateRepair = false, string delta = "")
    {
        var record = new JournalRecord
        {
            Type = type,
            Utc = Now,
            PlanId = plan.PlanId,
            AttachmentDocId = attachment.DocId,
            ParentDocId = ParentOf(plan, attachment.DocId).DocId,
            AttachmentKind = attachment.Kind,
            Status = status,
            Reason = reason,
            FileName = fileName,
            MediaCostUsd = media,
            LateRepair = lateRepair
        };
        if (delta.Length > 0)
            record.LedgerDelta.Add(new LedgerDelta { Seq = 1, Kind = LedgerKind.Figure, About = delta, Statement = $"{delta} stated." });
        return record;
    }

    internal static JournalRecord Repaired(CorpusPlan plan, PlannedThread thread, params JournalRecord[] attachments) => new()
    {
        Type = JournalRecordType.AttachmentsRepaired,
        Utc = Now,
        PlanId = plan.PlanId,
        ThreadId = thread.Id,
        Attachments = attachments.ToList()
    };

    internal static JournalRecord DoneWith(CorpusPlan plan, PlannedThread thread, params JournalRecord[] attachments)
    {
        var record = Done(plan, thread, ThreadWriteStatus.Written, 0.01m);
        record.Attachments = attachments.ToList();
        return record;
    }

    // Real journal file and replay, so ownership validation (06-01) applies
    internal static JournalReplay Journal(CorpusPlan plan, params JournalRecord[] records)
    {
        var folder = EvidenceTestData.TempCaseFolder();
        try
        {
            // Same line format as GenerationJournal.AppendAsync, written synchronously
            var options = new JsonSerializerOptions(CaseDraft.JsonOptions) { WriteIndented = false };
            var lines = new[] { new JournalRecord { Type = JournalRecordType.RunStarted, Utc = Now, PlanId = plan.PlanId } }
                .Concat(records)
                .Select(r => JsonSerializer.Serialize(r, options));
            File.WriteAllLines(GenerationJournal.PathFor(folder), lines);
            return GenerationJournal.Replay(folder, plan);
        }
        finally
        {
            EvidenceTestData.DeleteFolder(folder);
        }
    }

    private static PlannedThread T(CorpusPlan plan, string id) => AttachmentTestData.Thread(plan, id);

    private static List<PlannedAttachment> A(CorpusPlan plan, string id) => AttachmentTestData.AttachmentsOf(plan, id).ToList();

    [Fact]
    public void Mixed_EmptyJournal_CountsWholeCorpus()
    {
        var (_, plan) = AttachmentTestData.MixedCase();

        var state = GenerationRunState.From(plan, new JournalReplay());

        var threads = GenerationThreads(plan).ToList();
        var relevant = threads.Count(t => t.Relevance == ThreadRelevance.Relevant);
        var nonRelevant = threads.Count(t => t.Relevance == ThreadRelevance.NotRelevant);
        Assert.True(nonRelevant > 0);
        Assert.Equal(relevant + nonRelevant, state.TotalThreads);
        Assert.Equal(relevant, state.RelevantTotal);
        Assert.Equal(nonRelevant, state.NonRelevantTotal);
        Assert.Equal(state.TotalThreads, state.Pending);
        Assert.Equal(plan.AttachmentCount, state.AttachmentsTotal);
        Assert.Equal(plan.AttachmentCount, state.StatusByAttachment.Count);
        Assert.All(state.StatusByAttachment.Values, s => Assert.Equal(AttachmentRunStatus.Pending, s));
        Assert.Empty(state.AttachmentRecords);
    }

    [Fact]
    public void NonRelevantThreadDone_CountsFilesButNoLedgerDeltas()
    {
        var (_, plan) = AttachmentTestData.MixedCase();
        var daily = T(plan, AttachmentTestData.DailyBusinessThreadId);

        var state = GenerationRunState.From(plan, Journal(plan, Done(plan, daily, ThreadWriteStatus.Written, 0.01m, "dana.cho")));

        Assert.Equal(1, state.NonRelevantWritten);
        Assert.Equal(0, state.RelevantWritten);
        Assert.Equal(1, state.Written);
        Assert.Equal(daily.Messages.Count, state.EmailsWritten);
        Assert.Equal(2 * daily.Messages.Count, state.FilesWritten);
        Assert.Equal(2, state.Mailboxes);
        Assert.False(state.DeltasByThread.ContainsKey(daily.Id));
    }

    [Fact]
    public void RelevantThreadDone_AttachmentDeltasFollowInPlanOrder_LateRepairExcluded()
    {
        var (_, plan) = AttachmentTestData.MixedCase();
        var thread = T(plan, AttachmentTestData.RelevantDocThreadId);
        var parts = A(plan, thread.Id);
        var done = DoneWith(plan, thread,
            Attachment(plan, parts[2], JournalRecordType.AttachmentDone, delta: "slides"),
            Attachment(plan, parts[0], JournalRecordType.AttachmentDone, delta: "memo"));

        var state = GenerationRunState.From(plan, Journal(plan, done,
            Repaired(plan, thread, Attachment(plan, parts[1], JournalRecordType.AttachmentDone, lateRepair: true, delta: "backup"))));

        var deltas = state.DeltasByThread[thread.Id];
        Assert.Equal(new[] { "site walk", "memo", "slides" }, deltas.Select(d => d.About));
        Assert.False(deltas[0].FromAttachment);
        Assert.True(deltas[1].FromAttachment);
        Assert.Equal(parts[0].DocId, deltas[1].AttachmentDocId);
        Assert.Equal(parts[2].DocId, deltas[2].AttachmentDocId);
        Assert.Equal(AttachmentRunStatus.Written, state.StatusByAttachment[parts[1].DocId]);
        Assert.Equal(3, state.AttachmentsWritten);
    }

    [Fact]
    public void NestedSkipped_IsSkippedNotFailed()
    {
        var (_, plan) = AttachmentTestData.MixedCase();
        var thread = T(plan, AttachmentTestData.InviteThreadId);
        var image = A(plan, thread.Id).Single(a => a.Kind == AttachmentType.Image);

        var state = GenerationRunState.From(plan, Journal(plan,
            DoneWith(plan, thread, Attachment(plan, image, JournalRecordType.AttachmentSkipped, reason: NoKeyReason))));

        Assert.Equal(AttachmentRunStatus.Skipped, state.StatusByAttachment[image.DocId]);
        Assert.Equal(1, state.AttachmentsSkipped);
        Assert.Equal(0, state.AttachmentsFailed);
        Assert.Equal(NoKeyReason, state.AttachmentRecords[image.DocId].Reason);
    }

    [Fact]
    public void AttachmentRecord_OfNotDoneThread_IsIgnored()
    {
        var (_, plan) = AttachmentTestData.MixedCase();
        var thread = T(plan, AttachmentTestData.RelevantDocThreadId);
        var part = A(plan, thread.Id)[0];
        var standalone = Attachment(plan, part, JournalRecordType.AttachmentDone);
        standalone.ThreadId = thread.Id;
        var replay = Journal(plan, Failed(plan, thread, 0.01m), standalone);
        Assert.True(replay.LastByAttachment.ContainsKey(part.DocId));

        var state = GenerationRunState.From(plan, replay);

        Assert.Equal(AttachmentRunStatus.Pending, state.StatusByAttachment[part.DocId]);
        Assert.Empty(state.AttachmentRecords);
    }

    [Fact]
    public void AttachmentSeeds_RowsFollowRepairGroupsAndMediaAvailability()
    {
        var (_, plan) = AttachmentTestData.MixedCase();
        var invite = T(plan, AttachmentTestData.InviteThreadId);
        var parts = A(plan, invite.Id);
        var ics = parts.Single(a => a.Kind == AttachmentType.CalendarInvite);
        var image = parts.Single(a => a.Kind == AttachmentType.Image);
        var state = GenerationRunState.From(plan, Journal(plan, DoneWith(plan, invite,
            Attachment(plan, ics, JournalRecordType.AttachmentDone),
            Attachment(plan, image, JournalRecordType.AttachmentSkipped, reason: NoKeyReason))));
        var waves = WavePlanner.Plan(plan);
        var waveOf = WavePlanner.WaveIndexByThread(waves);
        int filler = waves.Count + 1, repair = waves.Count + 2;

        var withMedia = state.AttachmentSeeds(plan, waveOf, filler, repair, mediaAvailable: true);
        var withoutMedia = state.AttachmentSeeds(plan, waveOf, filler, repair, mediaAvailable: false);

        Assert.Equal(plan.AttachmentCount, withMedia.Count);
        Assert.Equal(repair, withMedia[image.DocId].RowIndex);
        Assert.Equal(AttachmentRunStatus.Skipped, withMedia[image.DocId].Status);
        Assert.Equal(waveOf[invite.Id], withoutMedia[image.DocId].RowIndex);
        Assert.Equal(AttachmentRunStatus.Skipped, withoutMedia[image.DocId].Status);
        Assert.Equal(waveOf[invite.Id], withMedia[ics.DocId].RowIndex);
        Assert.Equal(AttachmentRunStatus.Written, withMedia[ics.DocId].Status);

        // Pending threads: relevant on their wave, filler on the Non-relevant row
        var pendingRelevant = A(plan, AttachmentTestData.RelevantDocThreadId)[0];
        Assert.Equal(waveOf[AttachmentTestData.RelevantDocThreadId], withMedia[pendingRelevant.DocId].RowIndex);
        Assert.Equal(AttachmentRunStatus.Pending, withMedia[pendingRelevant.DocId].Status);
        var pendingFiller = A(plan, AttachmentTestData.AncillaryThreadId)[0];
        Assert.Equal(filler, withMedia[pendingFiller.DocId].RowIndex);

        // Same set the runner repairs
        var repairIds = state.RepairGroups(plan, mediaAvailable: true).SelectMany(g => g.AttachmentDocIds).ToHashSet();
        Assert.All(withMedia, p => Assert.Equal(repairIds.Contains(p.Key), p.Value.RowIndex == repair));
    }

    [Fact]
    public void RepairGroups_DoneThreadsOnly_GroupedAndOrderedByWave()
    {
        var (_, plan) = AttachmentTestData.MixedCase();
        var docThread = T(plan, AttachmentTestData.RelevantDocThreadId);
        var docParts = A(plan, docThread.Id);
        var invite = T(plan, AttachmentTestData.InviteThreadId);
        var inviteParts = A(plan, invite.Id);
        var ics = inviteParts.Single(a => a.Kind == AttachmentType.CalendarInvite);
        var image = inviteParts.Single(a => a.Kind == AttachmentType.Image);
        var daily = T(plan, AttachmentTestData.DailyBusinessThreadId);
        var multi = T(plan, AttachmentTestData.MultiMessageThreadId);

        var state = GenerationRunState.From(plan, Journal(plan,
            DoneWith(plan, docThread,
                Attachment(plan, docParts[0], JournalRecordType.AttachmentDone),
                Attachment(plan, docParts[1], JournalRecordType.AttachmentFailed, reason: "provider error")),
            DoneWith(plan, invite,
                Attachment(plan, ics, JournalRecordType.AttachmentDone),
                Attachment(plan, image, JournalRecordType.AttachmentSkipped, reason: NoKeyReason)),
            DoneWith(plan, daily),
            Failed(plan, multi, 0.01m)));

        var noMedia = state.RepairGroups(plan, mediaAvailable: false);
        var media = state.RepairGroups(plan, mediaAvailable: true);

        var waveOf = WavePlanner.WaveIndexByThread(WavePlanner.Plan(plan));
        Assert.Equal(new[] { docThread.Id, daily.Id }, noMedia.Select(g => g.ThreadId));
        var docGroup = noMedia[0];
        Assert.True(docGroup.Relevant);
        Assert.Equal(waveOf[docThread.Id], docGroup.WaveIndex);
        Assert.Equal(new[] { docParts[1].DocId, docParts[2].DocId }, docGroup.AttachmentDocIds);
        Assert.Equal(new[] { ParentOf(plan, docParts[1].DocId).DocId }, docGroup.ParentDocIds);
        Assert.False(noMedia[1].Relevant);
        Assert.Equal(A(plan, daily.Id).Select(a => a.DocId), noMedia[1].AttachmentDocIds);

        // Skipped media joins only when media is available; relevant groups come in wave order, filler last
        Assert.Equal(new[] { docThread.Id, invite.Id, daily.Id }, media.Select(g => g.ThreadId));
        Assert.Equal(new[] { image.DocId }, media[1].AttachmentDocIds);
        Assert.True(media[0].WaveIndex <= media[1].WaveIndex);

        // Failed and pending threads are never repaired
        Assert.DoesNotContain(media, g => g.ThreadId == multi.Id);
        Assert.Equal(media.SelectMany(g => g.AttachmentDocIds), state.AttachmentsToRepair(plan, mediaAvailable: true));
    }

    [Fact]
    public void RepairGroups_Phase5Journal_RepairsEveryAttachmentOfDoneThreads()
    {
        var (_, plan) = AttachmentTestData.MixedCase();
        var relevant = GenerationThreads(plan).Where(t => t.Relevance == ThreadRelevance.Relevant).ToList();

        var state = GenerationRunState.From(plan, Journal(plan, relevant.Select(t => Done(plan, t, ThreadWriteStatus.Written, 0.01m)).ToArray()));

        var expected = relevant.SelectMany(t => t.Messages.SelectMany(m => m.Attachments)).Select(a => a.DocId).OrderBy(d => d, StringComparer.Ordinal);
        var groups = state.RepairGroups(plan, mediaAvailable: false);
        Assert.NotEmpty(groups);
        Assert.All(groups, g => Assert.True(g.Relevant));
        Assert.Equal(expected, groups.SelectMany(g => g.AttachmentDocIds).OrderBy(d => d, StringComparer.Ordinal));
        var waves = groups.Select(g => g.WaveIndex).ToList();
        Assert.Equal(waves.OrderBy(w => w), waves);
    }

    [Fact]
    public void InviteSlotsByMeeting_FirstCommittedPerKeyWins()
    {
        var (_, plan) = AttachmentTestData.MixedCase();
        var first = T(plan, AttachmentTestData.InviteThreadId);
        var second = T(plan, AttachmentTestData.SecondInviteThreadId);
        var firstIcs = A(plan, first.Id).Single(a => a.Kind == AttachmentType.CalendarInvite);
        var secondIcs = A(plan, second.Id).Single();
        var start = new DateTimeOffset(2024, 5, 13, 9, 0, 0, TimeSpan.FromHours(-4));
        var a = Attachment(plan, firstIcs, JournalRecordType.AttachmentDone);
        a.InviteMeetingKey = "fact:F-004";
        a.InviteStart = start;
        a.InviteEnd = start.AddHours(1);
        var b = Attachment(plan, secondIcs, JournalRecordType.AttachmentDone);
        b.InviteMeetingKey = "fact:F-004";
        b.InviteStart = start.AddDays(1);
        b.InviteEnd = start.AddDays(1).AddHours(2);

        var state = GenerationRunState.From(plan, Journal(plan, DoneWith(plan, first, a), DoneWith(plan, second, b)));

        var slot = Assert.Single(state.InviteSlotsByMeeting).Value;
        Assert.Equal(start, slot.Start);
        Assert.Equal(start.AddHours(1), slot.End);
        Assert.Equal("fact:F-004", slot.MeetingKey);
    }

    [Fact]
    public void Spend_CountsEveryMediaAttempt()
    {
        var (_, plan) = AttachmentTestData.MixedCase();
        var thread = T(plan, AttachmentTestData.InviteThreadId);
        var image = A(plan, thread.Id).Single(a => a.Kind == AttachmentType.Image);

        var replay = Journal(plan,
            DoneWith(plan, thread, Attachment(plan, image, JournalRecordType.AttachmentFailed, media: 0.04m)),
            Repaired(plan, thread, Attachment(plan, image, JournalRecordType.AttachmentFailed, media: 0.04m)),
            Repaired(plan, thread, Attachment(plan, image, JournalRecordType.AttachmentDone, media: 0.04m)));
        var state = GenerationRunState.From(plan, replay);

        Assert.Equal(0.12m, state.MediaCostUsd);
        Assert.Equal(replay.SpentCostUsd, state.CostUsd);
        Assert.Equal(0.13m, state.CostUsd);
        Assert.Equal(AttachmentRunStatus.Written, state.StatusByAttachment[image.DocId]);
    }

    [Fact]
    public void FlagsAndFillerTitles_FromReplay()
    {
        var (_, plan) = AttachmentTestData.MixedCase();
        var daily = T(plan, AttachmentTestData.DailyBusinessThreadId);
        var done = Done(plan, daily, ThreadWriteStatus.Written, 0.01m);
        done.Messages[0].AttachmentTitles = new List<string> { "Insurance Certificate 2024" };

        var state = GenerationRunState.From(plan, Journal(plan, done,
            new JournalRecord { Type = JournalRecordType.SideWorld, PlanId = plan.PlanId, Utc = Now, SideWorld = new SideWorld() }));

        Assert.Equal(new[] { "Insurance Certificate 2024" }, state.FillerTitlesByDocId[daily.Messages[0].DocId]);
        Assert.True(state.HasSideWorld);
        Assert.False(state.HasCastVoices);
        Assert.False(state.LatestAttemptEnded);
    }

    [Fact]
    public void ToCounts_FillsCorpusAndAttachmentFields()
    {
        var (_, plan) = AttachmentTestData.MixedCase();
        var invite = T(plan, AttachmentTestData.InviteThreadId);
        var daily = T(plan, AttachmentTestData.DailyBusinessThreadId);
        var image = A(plan, invite.Id).Single(a => a.Kind == AttachmentType.Image);
        var ics = A(plan, invite.Id).Single(a => a.Kind == AttachmentType.CalendarInvite);
        var state = GenerationRunState.From(plan, Journal(plan,
            DoneWith(plan, invite,
                Attachment(plan, ics, JournalRecordType.AttachmentDone, status: ThreadWriteStatus.Warnings),
                Attachment(plan, image, JournalRecordType.AttachmentSkipped, reason: NoKeyReason)),
            DoneWith(plan, daily, Attachment(plan, A(plan, daily.Id)[0], JournalRecordType.AttachmentFailed))));

        var counts = state.ToCounts(plan, 3, 0, 0m, running: false, finished: false);

        var threads = GenerationThreads(plan).ToList();
        var attachments = threads.SelectMany(t => t.Messages.SelectMany(m => m.Attachments)).ToList();
        var nonRelevant = threads.Where(t => t.Relevance == ThreadRelevance.NotRelevant).ToList();
        Assert.Equal(threads.Count, counts.TotalThreads);
        Assert.Equal(2, counts.DoneThreads);
        Assert.Equal(state.RelevantTotal, counts.RelevantTotal);
        Assert.Equal(1, counts.RelevantDone);
        Assert.Equal(nonRelevant.Count, counts.NonRelevantTotal);
        Assert.Equal(1, counts.NonRelevantDone);
        Assert.Equal(nonRelevant.Count(t => t.Category == NonRelevantCategory.DailyBusiness), counts.NonRelevantDailyBusiness);
        Assert.Equal(nonRelevant.Count(t => t.Category == NonRelevantCategory.Ancillary), counts.NonRelevantAncillary);
        Assert.Equal(nonRelevant.Count(t => t.Category == NonRelevantCategory.NewsletterSpamPersonal), counts.NonRelevantBulk);
        Assert.Equal(nonRelevant.Count(t => t.Category == NonRelevantCategory.Decoy), counts.NonRelevantDecoy);
        Assert.Equal(threads.Sum(t => t.Messages.Count), counts.EmailsPlanned);
        Assert.Equal(plan.AttachmentCount, counts.AttachmentsTotal);
        Assert.Equal(1, counts.AttachmentsDone);
        Assert.Equal(1, counts.AttachmentsWarnings);
        Assert.Equal(1, counts.AttachmentsFailed);
        Assert.Equal(1, counts.AttachmentsSkipped);
        Assert.Equal(attachments.Count(a => a.Kind == AttachmentType.Word), counts.AttachmentsWord);
        Assert.Equal(attachments.Count(a => a.Kind == AttachmentType.Excel), counts.AttachmentsExcel);
        Assert.Equal(attachments.Count(a => a.Kind == AttachmentType.PowerPoint), counts.AttachmentsPowerPoint);
        Assert.Equal(attachments.Count(a => a.Kind == AttachmentType.CalendarInvite), counts.AttachmentsInvite);
        Assert.Equal(attachments.Count(a => a.Kind == AttachmentType.Image), counts.AttachmentsImage);
        Assert.Equal(attachments.Count(a => a.Kind == AttachmentType.Voicemail), counts.AttachmentsVoicemail);
        Assert.Equal(counts.AttachmentsImage + counts.AttachmentsVoicemail, counts.MediaPlanned);
        Assert.Equal(state.MediaCostUsd, counts.MediaCostUsd);
    }

    private static AIModelConfig Story() => new() { DisplayName = "Story", InputTokenPricePerMillion = 4m, OutputTokenPricePerMillion = 20m };

    private static AIModelConfig Volume() => new() { DisplayName = "Volume", InputTokenPricePerMillion = 0.5m, OutputTokenPricePerMillion = 2m };

    [Fact]
    public void EstimateRemaining_EmptyJournal_EqualsReadyCardEstimate()
    {
        var (draft, plan) = AttachmentTestData.MixedCase();
        var state = GenerationRunState.From(plan, new JournalReplay());
        var prices = MediaPrices.Defaults();

        Assert.Equal(GenerationCostEstimator.Estimate(plan, Story(), Volume(), prices).Total,
            GenerationRunState.EstimateRemaining(plan, state, Story(), Volume(), prices, mediaAvailable: true));
        Assert.Equal(GenerationCostEstimator.Estimate(plan, Story(), Volume(), prices, true, draft.Guide).Total,
            GenerationRunState.EstimateRemaining(plan, state, Story(), Volume(), prices, true, draft.Guide));
        Assert.Equal(GenerationCostEstimator.Estimate(plan, Story(), Volume(), prices, mediaAvailable: false).Total,
            GenerationRunState.EstimateRemaining(plan, state, Story(), Volume(), prices, mediaAvailable: false));
    }

    [Fact]
    public void EstimateRemaining_PricesPendingAndRepairWork()
    {
        var (_, plan) = AttachmentTestData.MixedCase();
        var docThread = T(plan, AttachmentTestData.RelevantDocThreadId);
        var docParts = A(plan, docThread.Id);
        var voicemail = T(plan, AttachmentTestData.RelevantVoicemailThreadId);
        var state = GenerationRunState.From(plan, Journal(plan,
            DoneWith(plan, docThread,
                Attachment(plan, docParts[0], JournalRecordType.AttachmentDone),
                Attachment(plan, docParts[1], JournalRecordType.AttachmentDone)),
            new JournalRecord { Type = JournalRecordType.SideWorld, PlanId = plan.PlanId, Utc = Now, SideWorld = new SideWorld() }));
        var prices = MediaPrices.Defaults();

        var work = new PricedWork { IncludeSideWorld = false, IncludeVoiceCast = true };
        foreach (var thread in GenerationThreads(plan).Where(t => t.Id != docThread.Id))
        {
            foreach (var message in thread.Messages)
            {
                work.Messages.Add((thread, message));
                foreach (var attachment in message.Attachments)
                    work.Attachments.Add((thread, message, attachment));
            }
        }

        work.Attachments.Add((docThread, ParentOf(plan, docParts[2].DocId), docParts[2]));
        Assert.Contains(work.Attachments, a => a.Thread == voicemail);

        var expected = GenerationCostEstimator.PriceWork(work, Story(), Volume(), prices, mediaAvailable: true).Total;
        Assert.Equal(expected, GenerationRunState.EstimateRemaining(plan, state, Story(), Volume(), prices, mediaAvailable: true));
        Assert.True(expected > 0m);
    }

    [Fact]
    public void EstimateRemaining_BatchFlag_ReturnsBatchTotal()
    {
        var (draft, plan) = AttachmentTestData.MixedCase();
        var state = GenerationRunState.From(plan, new JournalReplay());
        var prices = MediaPrices.Defaults();
        var opus = AIModelConfig.GetDefaultModels().First(m => m.ModelId == "claude-opus-5-5");
        var haiku = AIModelConfig.GetDefaultModels().First(m => m.ModelId == "claude-haiku-4-5-20251001");

        var ready = GenerationCostEstimator.Estimate(plan, opus, haiku, prices, true, draft.Guide);

        Assert.Equal(ready.BatchTotal,
            GenerationRunState.EstimateRemaining(plan, state, opus, haiku, prices, true, draft.Guide, batch: true));
        Assert.Equal(ready.Total,
            GenerationRunState.EstimateRemaining(plan, state, opus, haiku, prices, true, draft.Guide, batch: false));
        Assert.True(ready.BatchTotal < ready.Total);
    }

    [Fact]
    public void Apply_CopiesPhase6CachedCounts_RelevantThreadCountsStayRelevant()
    {
        var (draft, plan) = AttachmentTestData.MixedCase();
        var invite = T(plan, AttachmentTestData.InviteThreadId);
        var daily = T(plan, AttachmentTestData.DailyBusinessThreadId);
        var image = A(plan, invite.Id).Single(a => a.Kind == AttachmentType.Image);
        var state = GenerationRunState.From(plan, Journal(plan,
            DoneWith(plan, invite, Attachment(plan, image, JournalRecordType.AttachmentSkipped, reason: NoKeyReason, media: 0.02m)),
            Done(plan, daily, ThreadWriteStatus.Warnings, 0.01m)));

        GenerationStateApplier.Apply(draft, plan, state, new NarrativeLedger(), new List<WrittenCorpusFile>(),
            GenerationOutcome.Finished, string.Empty, Now, final: false);

        var g = draft.Corpus!.Generation!;
        Assert.Equal(state.RelevantTotal, g.ThreadsTotal);
        Assert.Equal(1, g.ThreadsWritten);
        Assert.Equal(0, g.ThreadsWithWarnings);
        Assert.Equal(state.NonRelevantTotal, g.NonRelevantTotal);
        Assert.Equal(0, g.NonRelevantWritten);
        Assert.Equal(1, g.NonRelevantWithWarnings);
        Assert.Equal(plan.AttachmentCount, g.AttachmentsTotal);
        Assert.Equal(1, g.AttachmentsSkipped);
        Assert.Equal(0.02m, g.MediaCostUsd);
    }
}
