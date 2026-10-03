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
    public void From_ProjectsStatusesAndCounts()
    {
        var (_, plan, state, threads) = Mixed();
        var relevant = Relevant(plan);

        Assert.True(relevant.Count > 3);
        Assert.Equal(relevant.Count, state.TotalThreads);
        Assert.Equal(ThreadRunStatus.Written, state.StatusByThread[threads[0].Id]);
        Assert.Equal(ThreadRunStatus.Warnings, state.StatusByThread[threads[1].Id]);
        Assert.Equal(ThreadRunStatus.Failed, state.StatusByThread[threads[2].Id]);
        foreach (var other in relevant.Skip(3))
            Assert.Equal(ThreadRunStatus.Pending, state.StatusByThread[other.Id]);

        Assert.Equal(1, state.Written);
        Assert.Equal(1, state.Warnings);
        Assert.Equal(1, state.Failed);
        Assert.Equal(relevant.Count - 3, state.Pending);
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

        // Non-relevant emails are not overlaid
        foreach (var message in plan.Threads.Where(t => t.Relevance != ThreadRelevance.Relevant).SelectMany(t => t.Messages))
            Assert.False(overlay.ByDocId.ContainsKey(message.DocId));

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
        Assert.Equal(Relevant(plan).Sum(t => t.Messages.Count), counts.EmailsPlanned);
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
        Assert.Equal(state.TotalThreads, generation.ThreadsTotal);
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
}
