using ReelDiscovery.Models.Corpus;
using ReelDiscovery.Services.Discovery;
using ReelDiscovery.Services.Evidence;
using ReelDiscovery.Tests.Evidence;

namespace ReelDiscovery.Tests.Discovery;

public class CorpusDetectorTests : IDisposable
{
    private readonly string _folder = EvidenceTestData.TempCaseFolder();

    public void Dispose() => EvidenceTestData.DeleteFolder(_folder);

    [Fact]
    public void NoCorpus_NotUsable()
    {
        var draft = DiscoveryTestData.LitigationDraft();
        draft.FolderPath = _folder;
        draft.Corpus = null;

        var corpus = CorpusDetector.Detect(draft);

        Assert.False(corpus.Usable);
        Assert.Empty(corpus.Candidates);
    }

    [Fact]
    public async Task WrittenRelevantEmails_AreCandidates()
    {
        var draft = await DiscoveryCorpusFixture.WithWrittenCorpusAsync(_folder, relevant: 4, written: 3, hotIndex: 1);
        var plan = DiscoveryCorpusFixture.PlanOf(draft);
        draft.Corpus!.Plan = null; // force the file load path

        var corpus = CorpusDetector.Detect(draft);

        Assert.True(corpus.Usable, corpus.Reason);
        Assert.Equal(plan.PlanId, corpus.PlanId);
        Assert.Equal(new[] { "RD-000001", "RD-000002", "RD-000003" }, corpus.Candidates.Select(c => c.DocId).ToArray());
        Assert.Equal(new[] { "RD-000002" }, corpus.Candidates.Where(c => c.IsHot).Select(c => c.DocId).ToArray());

        var first = corpus.Candidates[0];
        var message = plan.Threads[0].Messages[0];
        Assert.Equal("T-0001", first.ThreadId);
        Assert.Equal("Subject line T-0001", first.Subject);
        Assert.Equal("2024-03-04", first.Date);
        Assert.Contains(message.FromId, first.ParticipantIds);
        Assert.Contains(message.ToIds[0], first.ParticipantIds);
        Assert.Contains(message.CcIds[0], first.ParticipantIds);
        Assert.Equal(message.FactIds, first.FactIds);
        Assert.DoesNotContain(corpus.Candidates, c => c.ThreadId == DiscoveryCorpusFixture.NonRelevantThreadId);
        Assert.NotNull(corpus.Ledger);
    }

    [Fact]
    public async Task PlanChanged_NotUsable()
    {
        var draft = await DiscoveryCorpusFixture.WithWrittenCorpusAsync(_folder);
        var plan = DiscoveryCorpusFixture.PlanOf(draft);
        await new GenerationJournal(_folder).AppendAsync(DiscoveryCorpusFixture.Done(Guid.NewGuid(), plan.Threads[3]));

        var corpus = CorpusDetector.Detect(draft);

        Assert.False(corpus.Usable);
        Assert.NotEmpty(corpus.Reason);
    }

    [Fact]
    public async Task JournalMissing_NotUsable()
    {
        var draft = await DiscoveryCorpusFixture.WithWrittenCorpusAsync(_folder);
        File.Delete(GenerationJournal.PathFor(_folder));

        var corpus = CorpusDetector.Detect(draft);

        Assert.False(corpus.Usable);
        Assert.NotEmpty(corpus.Reason);
    }

    [Fact]
    public async Task UnwrittenDocIds_Excluded()
    {
        var draft = await DiscoveryCorpusFixture.WithWrittenCorpusAsync(_folder, relevant: 5, written: 2);

        var corpus = CorpusDetector.Detect(draft);

        Assert.True(corpus.Usable, corpus.Reason);
        var ids = corpus.Candidates.Select(c => c.DocId).ToList();
        Assert.Equal(new[] { "RD-000001", "RD-000002" }, ids);
        Assert.DoesNotContain("RD-000003", ids);
        Assert.DoesNotContain("RD-000004", ids);
        Assert.DoesNotContain("RD-000005", ids);
    }

    [Fact]
    public async Task NoRelevantWritten_NotUsable()
    {
        var draft = await DiscoveryCorpusFixture.WithWrittenCorpusAsync(_folder, relevant: 3, written: 0);

        var corpus = CorpusDetector.Detect(draft);

        Assert.False(corpus.Usable);
        Assert.NotEmpty(corpus.Reason);
    }

    [Fact]
    public async Task CorruptPlan_NotUsableNoThrow()
    {
        var draft = await DiscoveryCorpusFixture.WithWrittenCorpusAsync(_folder);
        File.WriteAllText(Path.Combine(_folder, CorpusState.PlanFileName), "{ not json");
        draft.Corpus!.Plan = null;

        var corpus = CorpusDetector.Detect(draft);

        Assert.False(corpus.Usable);
        Assert.NotEmpty(corpus.Reason);
        Assert.Null(corpus.WrittenStamps);
    }

    // 09 W3: written-email stamps

    private static readonly DateTime DoneUtc = new(2026, 10, 3, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task WrittenCorpus_PublishesThreadDoneStamps()
    {
        var draft = await DiscoveryCorpusFixture.WithWrittenCorpusAsync(_folder, relevant: 4, written: 3);

        var corpus = CorpusDetector.Detect(draft);

        Assert.True(corpus.Usable, corpus.Reason);
        Assert.NotNull(corpus.WrittenStamps);
        // Three relevant threads plus the non-relevant thread (RD-000005) are journaled ThreadDone
        Assert.Equal(new[] { "RD-000001", "RD-000002", "RD-000003", "RD-000005" },
            corpus.WrittenStamps!.Keys.OrderBy(k => k, StringComparer.Ordinal));
        Assert.All(corpus.WrittenStamps.Values, v => Assert.Equal(DoneUtc, v));
    }

    [Fact]
    public async Task NoRelevantWritten_PublishesKnownStamps()
    {
        var draft = await DiscoveryCorpusFixture.WithWrittenCorpusAsync(_folder, relevant: 3, written: 0);
        var replay = GenerationJournal.Replay(_folder, DiscoveryCorpusFixture.PlanOf(draft));

        var corpus = CorpusDetector.Detect(draft);

        Assert.False(corpus.Usable);
        Assert.NotNull(corpus.WrittenStamps);
        Assert.Equal(DiscoveryStaleness.EmailStamps(replay), corpus.WrittenStamps);
    }

    [Fact]
    public async Task JournalMissing_StampsKnownEmpty()
    {
        var draft = await DiscoveryCorpusFixture.WithWrittenCorpusAsync(_folder);
        File.Delete(GenerationJournal.PathFor(_folder));

        var corpus = CorpusDetector.Detect(draft);

        Assert.False(corpus.Usable);
        Assert.NotNull(corpus.WrittenStamps);
        Assert.Empty(corpus.WrittenStamps!);
    }

    [Fact]
    public async Task StartOver_StampsKnownEmpty()
    {
        var draft = await DiscoveryCorpusFixture.WithWrittenCorpusAsync(_folder);
        Assert.NotEmpty(CorpusDetector.Detect(draft).WrittenStamps!);

        var reset = await GenerationReset.StartOverAsync(_folder, DiscoveryCorpusFixture.PlanOf(draft), draft.Guide);
        Assert.Empty(reset.Errors);

        var corpus = CorpusDetector.Detect(draft);

        Assert.False(corpus.Usable);
        Assert.NotNull(corpus.WrittenStamps);
        Assert.Empty(corpus.WrittenStamps!);
    }

    [Fact]
    public async Task RegenerateThread_RemovesItsDocId()
    {
        var draft = await DiscoveryCorpusFixture.WithWrittenCorpusAsync(_folder);

        var result = await ThreadInvalidation.InvalidateAsync(_folder, DiscoveryCorpusFixture.PlanOf(draft), draft.Guide!, "T-0001");
        Assert.Empty(result.Errors);

        var corpus = CorpusDetector.Detect(draft);

        Assert.True(corpus.Usable, corpus.Reason);
        Assert.NotNull(corpus.WrittenStamps);
        Assert.DoesNotContain("RD-000001", corpus.WrittenStamps!.Keys);
        Assert.Contains("RD-000002", corpus.WrittenStamps.Keys);
    }

    [Fact]
    public async Task PlanChanged_StampsUnknown()
    {
        var draft = await DiscoveryCorpusFixture.WithWrittenCorpusAsync(_folder);
        var plan = DiscoveryCorpusFixture.PlanOf(draft);
        await new GenerationJournal(_folder).AppendAsync(DiscoveryCorpusFixture.Done(Guid.NewGuid(), plan.Threads[3]));

        Assert.Null(CorpusDetector.Detect(draft).WrittenStamps);
    }

    [Fact]
    public void NoPlan_StampsUnknown()
    {
        var draft = DiscoveryTestData.LitigationDraft();
        draft.FolderPath = _folder;
        draft.Corpus = null;

        Assert.Null(CorpusDetector.Detect(draft).WrittenStamps);
    }
}
