using System.Text;
using ReelDiscovery.Models;
using ReelDiscovery.Models.Case;
using ReelDiscovery.Models.Corpus;
using ReelDiscovery.Services.Corpus;

namespace ReelDiscovery.Tests.Corpus;

public class CorpusPlanStoreTests : IDisposable
{
    private readonly string _folder;

    public CorpusPlanStoreTests()
    {
        // Temp folder only: never a real case folder
        _folder = Path.Combine(Path.GetTempPath(), "rd-plan-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_folder);
    }

    public void Dispose()
    {
        CorpusPlanStore.SizeCapOverride = 0;
        if (Directory.Exists(_folder))
            Directory.Delete(_folder, recursive: true);
    }

    private static CorpusPlan SamplePlan()
    {
        var plan = new CorpusPlan
        {
            PlanId = Guid.NewGuid(),
            CreatedUtc = new DateTime(2024, 9, 1, 12, 0, 0, DateTimeKind.Utc),
            GuideHash = "g",
            SettingsHash = "s",
            DocIdPrefix = "BH",
            Settings = new CorpusSettings { TotalEmails = 250, WindowStart = "2024-01-01", WindowEnd = "2024-08-31" }
        };
        plan.Threads.Add(new PlannedThread
        {
            Id = "T-0001",
            Relevance = ThreadRelevance.Relevant,
            IssueIds = new List<string> { "ISS-01" },
            FactIds = new List<string> { "F-003" },
            Subject = "Change Order 7",
            Messages = new List<PlannedMessage>
            {
                new()
                {
                    Id = "T-0001-M01",
                    DocId = "BH-000001",
                    Seq = 1,
                    FromId = "CH-004",
                    ToIds = new List<string> { "CH-002" },
                    SentAt = new DateTimeOffset(2024, 3, 18, 10, 0, 0, TimeSpan.FromHours(-5)),
                    TimeZoneId = "America/Phoenix",
                    FactIds = new List<string> { "F-003" },
                    Basis = RelevanceBasis.Own,
                    IsHot = true,
                    CustodianIds = new List<string> { "CH-004", "CH-002" },
                    Attachments = new List<PlannedAttachment>
                    {
                        new() { DocId = "BH-000001.0001", Kind = AttachmentType.Word, FileName = "CO7.docx" }
                    }
                }
            }
        });
        return plan;
    }

    private string PlanFile => Path.Combine(_folder, CorpusState.PlanFileName);

    [Fact]
    public async Task SaveThenLoad_MatchingId_RoundTrips()
    {
        var plan = SamplePlan();
        await CorpusPlanStore.SaveAsync(_folder, plan);

        Assert.Equal(PlanFile, CorpusPlanStore.PlanPath(_folder));
        Assert.True(File.Exists(PlanFile));
        var text = File.ReadAllText(PlanFile);
        Assert.DoesNotContain("\n", text);

        var result = CorpusPlanStore.Load(_folder, plan.PlanId);
        Assert.True(result.Succeeded);
        Assert.Equal(string.Empty, result.Reason);
        Assert.Equal(Encoding.UTF8.GetString(CorpusPlanStore.Serialize(plan)),
            Encoding.UTF8.GetString(CorpusPlanStore.Serialize(result.Plan!)));
    }

    [Fact]
    public async Task Load_DifferentPlanId_NoPlan()
    {
        var plan = SamplePlan();
        await CorpusPlanStore.SaveAsync(_folder, plan);

        var result = CorpusPlanStore.Load(_folder, Guid.NewGuid());
        Assert.False(result.Succeeded);
        Assert.Null(result.Plan);
        Assert.Equal("The saved plan does not match this case.", result.Reason);
    }

    [Fact]
    public void Load_MissingFile_NoPlan()
    {
        var result = CorpusPlanStore.Load(_folder, Guid.NewGuid());
        Assert.False(result.Succeeded);
        Assert.Equal("The plan file corpus-plan.json is missing from the case folder.", result.Reason);
    }

    [Fact]
    public async Task Load_Oversized_NoPlan()
    {
        var plan = SamplePlan();
        await CorpusPlanStore.SaveAsync(_folder, plan);
        CorpusPlanStore.SizeCapOverride = 1024;
        File.WriteAllText(PlanFile, File.ReadAllText(PlanFile) + new string(' ', 2048));

        var result = CorpusPlanStore.Load(_folder, plan.PlanId);
        Assert.False(result.Succeeded);
        Assert.Contains("larger than 100 MB", result.Reason);
    }

    [Fact]
    public void Load_Garbage_NoPlan()
    {
        File.WriteAllText(PlanFile, "{ this is not json");

        var result = CorpusPlanStore.Load(_folder, Guid.NewGuid());
        Assert.False(result.Succeeded);
        Assert.StartsWith("The plan file could not be read:", result.Reason);
        Assert.EndsWith(".", result.Reason);
    }

    [Fact]
    public void Load_JsonNull_NoPlan()
    {
        File.WriteAllText(PlanFile, "null");

        var result = CorpusPlanStore.Load(_folder, Guid.NewGuid());
        Assert.False(result.Succeeded);
        Assert.StartsWith("The plan file could not be read:", result.Reason);
    }

    [Fact]
    public void Load_NewerVersion_NoPlan()
    {
        var id = Guid.NewGuid();
        File.WriteAllText(PlanFile, $"{{\"FormatVersion\":99,\"PlanId\":\"{id}\"}}");

        var result = CorpusPlanStore.Load(_folder, id);
        Assert.False(result.Succeeded);
        Assert.Equal("The plan was saved by a newer version of ReelDiscovery.", result.Reason);
    }

    [Fact]
    public void Load_NullHeavy_Normalized()
    {
        var id = Guid.NewGuid();
        File.WriteAllText(PlanFile,
            $"{{\"PlanId\":\"{id}\",\"GuideHash\":null,\"Settings\":null,\"Notes\":[null],\"Protocol\":null," +
            "\"Externals\":[null,{\"Id\":null}],\"Threads\":[null,{\"Id\":null,\"IssueIds\":null,\"FactIds\":[null]," +
            "\"Messages\":[null,{\"Id\":null,\"ToIds\":null,\"Attachments\":[null,{\"FileName\":null}]}]}]}");

        var result = CorpusPlanStore.Load(_folder, id);
        Assert.True(result.Succeeded, result.Reason);
        var plan = result.Plan!;
        Assert.Equal(string.Empty, plan.GuideHash);
        Assert.NotNull(plan.Settings);
        Assert.NotNull(plan.Protocol);
        Assert.Single(plan.Threads);
        Assert.Single(plan.Externals);
        Assert.Equal(string.Empty, plan.Externals[0].Id);
        var thread = plan.Threads[0];
        Assert.Equal(string.Empty, thread.Id);
        Assert.Empty(thread.IssueIds);
        Assert.Empty(thread.FactIds);
        var message = Assert.Single(thread.Messages);
        Assert.Empty(message.ToIds);
        var attachment = Assert.Single(message.Attachments);
        Assert.Equal(string.Empty, attachment.FileName);
    }

    [Fact]
    public async Task EnsureLoaded_AssignsPlan()
    {
        var plan = SamplePlan();
        await CorpusPlanStore.SaveAsync(_folder, plan);
        var draft = new CaseDraft { FolderPath = _folder, Corpus = new CorpusState { PlanId = plan.PlanId } };

        var result = CorpusPlanStore.EnsureLoaded(draft);
        Assert.True(result.Succeeded);
        Assert.Same(result.Plan, draft.Corpus.Plan);
        Assert.Equal(plan.PlanId, draft.Corpus.Plan!.PlanId);
    }

    [Fact]
    public void EnsureLoaded_NoCorpusOrNoPlan_NoOp()
    {
        var noCorpus = new CaseDraft { FolderPath = _folder };
        var result = CorpusPlanStore.EnsureLoaded(noCorpus);
        Assert.Equal(string.Empty, result.Reason);
        Assert.Null(result.Plan);
        Assert.Null(noCorpus.Corpus);

        var noPlan = new CaseDraft { FolderPath = _folder, Corpus = new CorpusState() };
        result = CorpusPlanStore.EnsureLoaded(noPlan);
        Assert.Equal(string.Empty, result.Reason);
        Assert.Null(noPlan.Corpus.Plan);
    }

    [Fact]
    public void EnsureLoaded_AlreadyLoaded_KeepsPlan()
    {
        var plan = SamplePlan();
        var draft = new CaseDraft { FolderPath = _folder, Corpus = new CorpusState { PlanId = plan.PlanId, Plan = plan } };

        var result = CorpusPlanStore.EnsureLoaded(draft);
        Assert.True(result.Succeeded);
        Assert.Same(plan, draft.Corpus.Plan);
    }

    [Fact]
    public void EnsureLoaded_Failure_LeavesPlanNull()
    {
        var draft = new CaseDraft { FolderPath = _folder, Corpus = new CorpusState { PlanId = Guid.NewGuid() } };

        var result = CorpusPlanStore.EnsureLoaded(draft);
        Assert.False(result.Succeeded);
        Assert.Null(draft.Corpus.Plan);
        Assert.Equal("The plan file corpus-plan.json is missing from the case folder.", result.Reason);
    }
}
