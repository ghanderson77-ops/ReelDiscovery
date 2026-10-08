using System.Text;
using ReelDiscovery.Models.Discovery;
using ReelDiscovery.Services.Discovery;

namespace ReelDiscovery.Tests.Discovery;

public class DiscoveryStoreTests : IDisposable
{
    private readonly string _folder;

    public DiscoveryStoreTests()
    {
        // Temp folder only: never a real case folder
        _folder = Path.Combine(Path.GetTempPath(), "rd-disc-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_folder);
    }

    public void Dispose()
    {
        DiscoveryStore.SizeCapOverride = 0;
        if (Directory.Exists(_folder))
            Directory.Delete(_folder, recursive: true);
    }

    private static DiscoveryDocumentSet SampleSet(Guid caseId)
    {
        var set = new DiscoveryDocumentSet { CaseId = caseId, CaseNumber = "2:24-cv-01234", UnattributedCostUsd = 0.12m };
        set.ExhibitAssignments.Add(new ExhibitAssignment { Number = 1, WitnessId = "CH-002", SourceKey = "DEP-CH-002", Description = "Email re Change Order 7", DocId = "BH-000042", Date = "2024-03-18", IssueId = "ISS-01" });
        set.Documents.Add(new DiscoveryDocument
        {
            Key = "DEP-CH-002",
            Kind = DiscoveryKind.Deposition,
            WitnessId = "CH-002",
            Title = "Deposition of Ann Lee (Excerpt)",
            FileName = "Case - Deposition of Ann Lee (Excerpt).docx",
            Status = DiscoveryStatus.GeneratedWithWarnings,
            FileSaved = true,
            Warnings = { new DiscoveryViolation { Rule = DiscoveryRule.D3AmountVariant, Number = 2, Value = "$41,000", Canonical = "$42,000" } },
            GuideHash = "g",
            CoreHash = "c",
            CaptionHash = "k",
            CorpusPlanId = Guid.NewGuid(),
            CostUsd = 1.25m,
            CostDetail = { InputUsd = 0.5m, OutputUsd = 0.75m },
            GeneratedUtc = new DateTime(2026, 10, 8, 12, 0, 0, DateTimeKind.Utc),
            TestimonyPages = 25,
            Deposition = new DepositionContent
            {
                WitnessId = "CH-002",
                WitnessName = "Ann Lee",
                Date = "2025-02-11",
                Speakers = { new DepositionSpeaker { SpeakerId = "S1", FullName = "Nora Quist", Role = SpeakerRole.Examining, Honorific = "MS" } },
                Turns =
                {
                    new DepositionTurn { Kind = TurnKind.Q, SpeakerId = "S1", Text = "Did you approve it?", ExhibitNumber = 1, TopicIndex = 1 },
                    new DepositionTurn { Kind = TurnKind.A, SpeakerId = "W", Text = "Yes." }
                },
                Exhibits = { new DepositionExhibit { Number = 1, Description = "Email re Change Order 7", DocId = "BH-000042", Date = "2024-03-18" } }
            }
        });
        set.Documents.Add(new DiscoveryDocument
        {
            Key = "RFP",
            Kind = DiscoveryKind.RequestsForProduction,
            Status = DiscoveryStatus.Generated,
            RequestCount = 1,
            Written = new WrittenDiscoveryContent
            {
                Kind = DiscoveryKind.RequestsForProduction,
                Variant = DiscoveryVariant.Civil,
                RequestLabel = "REQUEST FOR PRODUCTION NO.",
                Definitions = { new DiscoveryDefinition { Term = "DOCUMENT", Text = "Any writing." } },
                Requests = { new DiscoveryRequest { Number = 1, Text = "All documents about Change Order 7.", IssueIds = { "ISS-01" } } }
            }
        });
        return set;
    }

    [Fact]
    public async Task SaveThenLoad_RoundTrips()
    {
        var caseId = Guid.NewGuid();
        var set = SampleSet(caseId);

        await DiscoveryStore.SaveAsync(_folder, set);
        var result = DiscoveryStore.Load(_folder, caseId);

        Assert.True(result.Succeeded, result.Reason);
        var loaded = result.Set!;
        Assert.Equal("2:24-cv-01234", loaded.CaseNumber);
        Assert.Equal(0.12m, loaded.UnattributedCostUsd);
        Assert.Single(loaded.ExhibitAssignments);
        Assert.Equal(2, loaded.Documents.Count);

        var dep = loaded.Find("DEP-CH-002")!;
        Assert.Equal(DiscoveryStatus.GeneratedWithWarnings, dep.Status);
        Assert.Equal(set.Documents[0].CorpusPlanId, dep.CorpusPlanId);
        Assert.Equal(1.25m, dep.CostUsd);
        Assert.Equal(0.75m, dep.CostDetail.OutputUsd);
        Assert.Equal(DiscoveryRule.D3AmountVariant, Assert.Single(dep.Warnings).Rule);
        Assert.Equal(2, dep.Deposition!.Turns.Count);
        Assert.Equal(1, dep.Deposition.Turns[0].ExhibitNumber);
        Assert.Equal("BH-000042", Assert.Single(dep.Deposition.Exhibits).DocId);
        Assert.True(dep.HasContent);

        var rfp = loaded.Find("RFP")!;
        Assert.Equal("All documents about Change Order 7.", Assert.Single(rfp.Written!.Requests).Text);
        Assert.Equal(1.37m, loaded.TotalCostUsd);
    }

    [Fact]
    public void Load_MissingFile_ReportsMissing()
    {
        var result = DiscoveryStore.Load(_folder, Guid.NewGuid());

        Assert.False(result.Succeeded);
        Assert.True(result.Missing);
        Assert.Equal(string.Empty, result.Reason);
    }

    [Fact]
    public async Task Load_Oversized_Fails()
    {
        var caseId = Guid.NewGuid();
        await DiscoveryStore.SaveAsync(_folder, SampleSet(caseId));
        DiscoveryStore.SizeCapOverride = 10;

        var result = DiscoveryStore.Load(_folder, caseId);

        Assert.False(result.Succeeded);
        Assert.False(result.Missing);
        Assert.Contains("larger than 20 MB", result.Reason);
    }

    [Fact]
    public void Load_NewerVersion_Fails()
    {
        var caseId = Guid.NewGuid();
        File.WriteAllText(DiscoveryStore.SetPath(_folder), $"{{ \"FormatVersion\": 9, \"CaseId\": \"{caseId}\" }}", Encoding.UTF8);

        var result = DiscoveryStore.Load(_folder, caseId);

        Assert.False(result.Succeeded);
        Assert.Contains("saved by a newer version", result.Reason);
    }

    [Fact]
    public async Task Load_OtherCase_Fails()
    {
        await DiscoveryStore.SaveAsync(_folder, SampleSet(Guid.NewGuid()));

        var result = DiscoveryStore.Load(_folder, Guid.NewGuid());

        Assert.False(result.Succeeded);
        Assert.Contains("does not match this case", result.Reason);
    }

    [Theory]
    [InlineData("{ not json")]
    [InlineData("[1, 2, 3]")]
    [InlineData("null")]
    [InlineData("{ \"Documents\": [ null, { \"Key\": null, \"Warnings\": null } ], \"CaseId\": \"x\" }")]
    public void Load_Malformed_FailsWithoutThrowing(string json)
    {
        File.WriteAllText(DiscoveryStore.SetPath(_folder), json, Encoding.UTF8);

        var result = DiscoveryStore.Load(_folder, Guid.NewGuid());

        Assert.False(result.Succeeded);
        Assert.NotEmpty(result.Reason);
    }

    [Fact]
    public void Load_NullLists_AreRepaired()
    {
        var caseId = Guid.NewGuid();
        File.WriteAllText(DiscoveryStore.SetPath(_folder),
            $"{{ \"CaseId\": \"{caseId}\", \"CaseNumber\": null, \"ExhibitAssignments\": null, \"Documents\": [ null, {{ \"Key\": \"RFP\", \"Warnings\": null, \"CostDetail\": null, \"Written\": {{ \"Requests\": [ null ] }} }} ] }}",
            Encoding.UTF8);

        var result = DiscoveryStore.Load(_folder, caseId);

        Assert.True(result.Succeeded, result.Reason);
        Assert.Equal(string.Empty, result.Set!.CaseNumber);
        Assert.Empty(result.Set.ExhibitAssignments);
        var doc = Assert.Single(result.Set.Documents);
        Assert.Empty(doc.Warnings);
        Assert.NotNull(doc.CostDetail);
        Assert.Empty(doc.Written!.Requests);
    }

    [Fact]
    public void LoadOrNew_ReturnsEmptySetForCase()
    {
        var caseId = Guid.NewGuid();

        var set = DiscoveryStore.LoadOrNew(_folder, caseId);

        Assert.Equal(caseId, set.CaseId);
        Assert.Empty(set.Documents);
    }

    [Theory]
    [InlineData("Case - Deposition of Ann Lee (Excerpt).docx", true)]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("..\\x.docx", false)]
    [InlineData("a/b.docx", false)]
    [InlineData("C:\\x.docx", false)]
    [InlineData("x.txt", false)]
    [InlineData("x..docx", false)]
    [InlineData(".docx", false)]
    public void IsDiscoveryFileName_Guards(string? name, bool expected)
    {
        Assert.Equal(expected, DiscoveryStore.IsDiscoveryFileName(name));
    }

    [Fact]
    public void DocxPath_StaysInsideDiscoveryFolder()
    {
        Assert.Null(DiscoveryStore.DocxPath(_folder, "..\\..\\evil.docx"));
        Assert.Null(DiscoveryStore.DocxPath(_folder, null));
        Assert.Null(DiscoveryStore.DocxPath(_folder, "C:\\evil.docx"));

        var path = DiscoveryStore.DocxPath(_folder, "Case - Deposition of Ann Lee (Excerpt).docx");

        Assert.NotNull(path);
        var root = Path.GetFullPath(Path.Combine(_folder, "Discovery")) + Path.DirectorySeparatorChar;
        Assert.StartsWith(root, Path.GetFullPath(path!), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void FileNameFor_TruncatesCasePartFirst()
    {
        var caseName = new string('C', 80);
        var docPart = "Deposition of " + new string('W', 40) + " (Excerpt)";
        var folder = "C:\\" + new string('f', 97);

        var name = DiscoveryStore.FileNameFor(caseName, docPart);

        Assert.EndsWith(".docx", name);
        Assert.True(DiscoveryStore.IsDiscoveryFileName(name));
        Assert.Contains(docPart, name);
        Assert.Equal(100, folder.Length);
        Assert.True(Path.Combine(folder, "Discovery", name).Length < 240);
    }

    [Fact]
    public void FileNameFor_ShortNames_KeepsBothParts()
    {
        var name = DiscoveryStore.FileNameFor("Brightline v Harmon", "Harmon's First Set of Requests for Production");

        Assert.Equal("Brightline v Harmon - Harmon's First Set of Requests for Production.docx", name);
    }

    [Fact]
    public void HasDocuments_DetectsDocx()
    {
        Assert.False(DiscoveryStore.HasDocuments(_folder));

        Directory.CreateDirectory(DiscoveryStore.FolderPath(_folder));
        Assert.False(DiscoveryStore.HasDocuments(_folder));

        File.WriteAllBytes(Path.Combine(DiscoveryStore.FolderPath(_folder), "x.docx"), new byte[] { 1 });
        Assert.True(DiscoveryStore.HasDocuments(_folder));
    }
}
