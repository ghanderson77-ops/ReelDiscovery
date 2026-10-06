using ReelDiscovery.Models.Case;
using ReelDiscovery.Models.Corpus;
using ReelDiscovery.Models.Evidence;
using ReelDiscovery.Models.Qa;
using ReelDiscovery.Services.Evidence;
using ReelDiscovery.Services.Qa;
using ReelDiscovery.Tests.Corpus;
using ReelDiscovery.Tests.Evidence;

namespace ReelDiscovery.Tests.Qa;

/// <summary>
/// D-15 automatic end-of-run QA: scans and leakage audit written to qa-scans.json / qa-leakage.json, report-only.
/// </summary>
public class QaRunnerTests : IDisposable
{
    private readonly string _folder;
    private readonly CaseDraft _draft;
    private readonly CorpusPlan _plan;

    public QaRunnerTests()
    {
        _folder = EvidenceTestData.TempCaseFolder();
        (_draft, _plan) = EvidenceTestData.ApprovedCase();
        _plan.Threads.RemoveAll(t => t.Relevance != ThreadRelevance.Relevant);
        foreach (var message in _plan.Threads.SelectMany(t => t.Messages))
            message.Attachments.Clear();
    }

    public void Dispose() => EvidenceTestData.DeleteFolder(_folder);

    private QaReportStore Store => new(_folder);

    private async Task WriteCorpusAsync()
    {
        var (gateway, fake) = CorpusTestData.CreateGateway();
        fake.Respond = EvidenceTestData.RenderResponder(_plan, _draft.Guide!);
        var result = await new GenerationRunner(gateway)
            .RunAsync(new GenerationRunInput { Draft = _draft, Plan = _plan, CaseFolder = _folder }, new GenerationSnapshot())
            .WaitAsync(TimeSpan.FromSeconds(60));
        Assert.Equal(GenerationOutcome.Finished, result.Outcome);
    }

    [Fact]
    public async Task RunEndOfRun_WrittenCorpus_WritesBothReportsThatLoadBack()
    {
        await WriteCorpusAsync();

        var (scans, leakage) = await QaRunner.RunEndOfRunAsync(_draft, _plan, _folder, null);

        Assert.NotNull(scans);
        Assert.NotNull(leakage);
        Assert.True(File.Exists(Store.ScansPath));
        Assert.True(File.Exists(Store.LeakagePath));

        var loadedScans = Store.LoadScans();
        Assert.NotNull(loadedScans);
        Assert.Equal(_plan.PlanId, loadedScans!.PlanId);
        Assert.Equal(scans!.ScannedEmails, loadedScans.ScannedEmails);
        Assert.True(loadedScans.ScannedEmails > 0);
        Assert.Equal(File.GetLastWriteTimeUtc(GenerationJournal.PathFor(_folder)), loadedScans.JournalUtc);

        var loadedLeakage = Store.LoadLeakage();
        Assert.NotNull(loadedLeakage);
        Assert.Equal(leakage!.Verdict, loadedLeakage!.Verdict);
    }

    [Fact]
    public async Task RunEndOfRun_CorruptJournal_DoesNotThrowAndLeavesReportsUntouched()
    {
        var scansBefore = "{\"marker\":\"scans\"}";
        var leakageBefore = "{\"marker\":\"leakage\"}";
        File.WriteAllText(Store.ScansPath, scansBefore);
        File.WriteAllText(Store.LeakagePath, leakageBefore);
        File.WriteAllText(GenerationJournal.PathFor(_folder), "this is not json\n{also not json\n");

        var (scans, leakage) = await QaRunner.RunEndOfRunAsync(_draft, _plan, _folder, null);

        Assert.Null(scans);
        Assert.Null(leakage);
        Assert.Equal(scansBefore, File.ReadAllText(Store.ScansPath));
        Assert.Equal(leakageBefore, File.ReadAllText(Store.LeakagePath));
    }

    [Fact]
    public async Task RunEndOfRun_Cancelled_ThrowsOperationCanceledOnly()
    {
        await WriteCorpusAsync();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => QaRunner.RunEndOfRunAsync(_draft, _plan, _folder, null, cts.Token));
        Assert.False(File.Exists(Store.ScansPath));
    }

    [Fact]
    public async Task RunEndOfRun_NoWrittenEmails_WritesNothing()
    {
        var (scans, leakage) = await QaRunner.RunEndOfRunAsync(_draft, _plan, _folder, null);

        Assert.Null(scans);
        Assert.Null(leakage);
        Assert.False(File.Exists(Store.ScansPath));
        Assert.False(File.Exists(Store.LeakagePath));
    }

    [Fact]
    public async Task RunEndOfRun_OnlyFailedThreads_WritesNothing()
    {
        var journal = new GenerationJournal(_folder);
        var thread = _plan.Threads.First(t => t.Messages.Count > 0);
        await journal.AppendAsync(new JournalRecord
        {
            Type = JournalRecordType.ThreadFailed,
            PlanId = _plan.PlanId,
            ThreadId = thread.Id,
            FailureKind = ThreadFailureKind.Refused,
            Reason = "refused"
        });

        var (scans, leakage) = await QaRunner.RunEndOfRunAsync(_draft, _plan, _folder, null);

        Assert.Null(scans);
        Assert.Null(leakage);
        Assert.False(File.Exists(Store.ScansPath));
    }
}
