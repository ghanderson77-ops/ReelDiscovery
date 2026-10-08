using System.Text;
using ReelDiscovery.Models.Qa;
using ReelDiscovery.Services.Qa;
using ReelDiscovery.Tests.Evidence;

namespace ReelDiscovery.Tests.Qa;

public class QaReportStoreTests : IDisposable
{
    private readonly string _folder = EvidenceTestData.TempCaseFolder();

    public void Dispose() => EvidenceTestData.DeleteFolder(_folder);

    private static BlindReviewVerdict Verdict(string docId, bool reviewer, string reason = "") => new()
    {
        DocId = docId,
        ThreadId = "T-0001",
        KeyRelevant = true,
        ReviewerRelevant = reviewer,
        IssueCodes = new List<string> { "ISS-01" },
        Reason = reason,
        Utc = new DateTime(2026, 10, 6, 12, 0, 0, DateTimeKind.Utc),
        CostUsd = 0.0123m
    };

    private static BlindReviewHeader Header(string model) => new()
    {
        PlanId = Guid.Parse("11111111-2222-3333-4444-555555555555"),
        ReviewerProvider = "Anthropic",
        ReviewerModel = model,
        SameProviderFallback = false,
        SampleSize = 50,
        StartedUtc = new DateTime(2026, 10, 6, 11, 0, 0, DateTimeKind.Utc)
    };

    [Fact]
    public void FileNames_AreConstants()
    {
        Assert.Equal("qa-scans.json", QaReportStore.ScansFileName);
        Assert.Equal("qa-leakage.json", QaReportStore.LeakageFileName);
        Assert.Equal("qa-blind-review.jsonl", QaReportStore.BlindReviewFileName);
    }

    [Fact]
    public void Scans_RoundTrip()
    {
        var store = new QaReportStore(_folder);
        var report = new ScanReport
        {
            PlanId = Guid.NewGuid(),
            CreatedUtc = new DateTime(2026, 10, 6, 10, 0, 0, DateTimeKind.Utc),
            JournalUtc = new DateTime(2026, 10, 6, 9, 0, 0, DateTimeKind.Utc),
            ScannedEmails = 250,
            Findings =
            {
                new ScanFinding
                {
                    ThreadId = "T-0002", DocId = "BG-000004", Subject = "CO 7", Check = ScanCheck.Value,
                    DateSent = new DateTime(2024, 3, 18, 14, 0, 0, DateTimeKind.Utc), Text = "Amount differs", Relevant = true
                }
            }
        };

        store.SaveScans(report);
        Assert.True(File.Exists(Path.Combine(_folder, QaReportStore.ScansFileName)));
        var loaded = store.LoadScans();

        Assert.NotNull(loaded);
        Assert.Equal(report.PlanId, loaded!.PlanId);
        Assert.Equal(report.JournalUtc, loaded.JournalUtc);
        Assert.Equal(250, loaded.ScannedEmails);
        var finding = Assert.Single(loaded.Findings);
        Assert.Equal(ScanCheck.Value, finding.Check);
        Assert.Equal("BG-000004", finding.DocId);
        Assert.True(finding.Relevant);
    }

    [Fact]
    public void Leakage_RoundTrip()
    {
        var store = new QaReportStore(_folder);
        var report = new LeakageReport
        {
            PlanId = Guid.NewGuid(),
            CreatedUtc = DateTime.UtcNow,
            Verdict = LeakageVerdict.Warning,
            BalancedAccuracy = 0.65,
            Auc = 0.7,
            Folds = 5,
            RelevantEmails = 60,
            NonRelevantEmails = 190,
            Signals =
            {
                new LeakageSignal
                {
                    Feature = "privilege banner", Kind = LeakageSignalKind.OnlyOnRelevant, RelevantShare = 1, BaseRate = 0.24,
                    Support = 9, Lift = 0.76
                }
            }
        };

        store.SaveLeakage(report);
        var loaded = store.LoadLeakage();

        Assert.NotNull(loaded);
        Assert.Equal(LeakageVerdict.Warning, loaded!.Verdict);
        Assert.Equal(0.65, loaded.BalancedAccuracy);
        Assert.Equal("privilege banner", Assert.Single(loaded.Signals).Feature);
        // No temp files left behind
        Assert.Single(Directory.GetFiles(_folder));
    }

    [Fact]
    public void Save_Overwrites()
    {
        var store = new QaReportStore(_folder);
        store.SaveLeakage(new LeakageReport { Verdict = LeakageVerdict.Fail });
        store.SaveLeakage(new LeakageReport { Verdict = LeakageVerdict.Pass });
        Assert.Equal(LeakageVerdict.Pass, store.LoadLeakage()!.Verdict);
    }

    [Fact]
    public void Load_Missing_ReturnsNull()
    {
        var store = new QaReportStore(_folder);
        Assert.Null(store.LoadScans());
        Assert.Null(store.LoadLeakage());
        var (header, verdicts) = store.LoadBlindReview();
        Assert.Null(header);
        Assert.Empty(verdicts);
    }

    [Fact]
    public void Load_Corrupt_ReturnsNull()
    {
        File.WriteAllText(Path.Combine(_folder, QaReportStore.ScansFileName), "{ not json");
        File.WriteAllText(Path.Combine(_folder, QaReportStore.LeakageFileName), "[1,2,");
        var store = new QaReportStore(_folder);
        Assert.Null(store.LoadScans());
        Assert.Null(store.LoadLeakage());
    }

    [Fact]
    public void Load_MissingFolder_ReturnsNull()
    {
        var store = new QaReportStore(Path.Combine(_folder, "nope"));
        Assert.Null(store.LoadLeakage());
        Assert.Null(store.LoadBlindReview().Header);
    }

    [Fact]
    public async Task BlindReview_LaterVerdictForSameDoc_Wins()
    {
        var store = new QaReportStore(_folder);
        await store.AppendBlindReviewHeaderAsync(Header("m1"));
        await store.AppendBlindReviewVerdictAsync(Verdict("D1", true));
        await store.AppendBlindReviewVerdictAsync(Verdict("D2", false));
        await store.AppendBlindReviewVerdictAsync(Verdict("D3", true));
        await store.AppendBlindReviewVerdictAsync(Verdict("D2", true, "second look"));

        var (header, verdicts) = store.LoadBlindReview();

        Assert.NotNull(header);
        Assert.Equal("m1", header!.ReviewerModel);
        Assert.Equal(50, header.SampleSize);
        Assert.Equal(new[] { "D1", "D2", "D3" }, verdicts.Select(v => v.DocId).ToArray());
        var d2 = verdicts.Single(v => v.DocId == "D2");
        Assert.True(d2.ReviewerRelevant);
        Assert.Equal("second look", d2.Reason);
        Assert.Equal(0.0123m, d2.CostUsd);
        Assert.Equal(new[] { "ISS-01" }, d2.IssueCodes);
    }

    [Fact]
    public async Task BlindReview_NewHeader_ResetsVerdicts()
    {
        var store = new QaReportStore(_folder);
        await store.AppendBlindReviewHeaderAsync(Header("m1"));
        await store.AppendBlindReviewVerdictAsync(Verdict("D1", true));
        await store.AppendBlindReviewHeaderAsync(Header("m2"));
        await store.AppendBlindReviewVerdictAsync(Verdict("D9", false));

        var (header, verdicts) = store.LoadBlindReview();

        Assert.Equal("m2", header!.ReviewerModel);
        Assert.Equal("D9", Assert.Single(verdicts).DocId);
    }

    [Fact]
    public async Task BlindReview_TornLastLine_SkippedAndNextAppendStartsFreshLine()
    {
        var store = new QaReportStore(_folder);
        await store.AppendBlindReviewHeaderAsync(Header("m1"));
        await store.AppendBlindReviewVerdictAsync(Verdict("D1", true));
        var path = Path.Combine(_folder, QaReportStore.BlindReviewFileName);
        File.AppendAllText(path, "{\"Kind\":\"Verdict\",\"Verdict\":{\"DocId\":\"D", Encoding.UTF8);

        var (_, before) = store.LoadBlindReview();
        Assert.Equal("D1", Assert.Single(before).DocId);

        await store.AppendBlindReviewVerdictAsync(Verdict("D2", false));
        var (header, after) = store.LoadBlindReview();
        Assert.NotNull(header);
        Assert.Equal(new[] { "D1", "D2" }, after.Select(v => v.DocId).ToArray());
    }

    [Fact]
    public async Task BlindReview_VerdictsBeforeAnyHeader_Ignored()
    {
        var store = new QaReportStore(_folder);
        await store.AppendBlindReviewVerdictAsync(Verdict("D0", true));
        var (header, verdicts) = store.LoadBlindReview();
        Assert.Null(header);
        Assert.Empty(verdicts);
    }

    [Fact]
    public async Task WRB01_Restate_WritesHeaderAndKeptVerdictsTogether_CancelledBeforeLeavesFileUnchanged()
    {
        var store = new QaReportStore(_folder);
        await store.AppendBlindReviewHeaderAsync(Header("m"));
        await store.AppendBlindReviewVerdictAsync(Verdict("RD-000001", true));
        await store.AppendBlindReviewVerdictAsync(Verdict("RD-000002", false));
        var before = File.ReadAllText(store.BlindReviewPath);

        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var resized = Header("m");
        resized.SampleSize = 10;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            store.RestateBlindReviewAsync(resized, new[] { Verdict("RD-000001", true) }, cts.Token));
        Assert.Equal(before, File.ReadAllText(store.BlindReviewPath));

        await store.RestateBlindReviewAsync(resized, new[] { Verdict("RD-000001", true) });
        var (header, verdicts) = store.LoadBlindReview();
        Assert.Equal(10, header!.SampleSize);
        Assert.Equal(new[] { "RD-000001" }, verdicts.Select(v => v.DocId));
    }
}
