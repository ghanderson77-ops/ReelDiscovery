using ReelDiscovery.Models.Corpus;
using ReelDiscovery.Services.Corpus;

namespace ReelDiscovery.Tests.Corpus;

public class AnswerKeyRelabelStoreTests : IDisposable
{
    private readonly string _folder;

    public AnswerKeyRelabelStoreTests()
    {
        // Temp folder only: never a real case folder
        _folder = Path.Combine(Path.GetTempPath(), "rd-relabels-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_folder);
    }

    public void Dispose()
    {
        if (Directory.Exists(_folder))
            Directory.Delete(_folder, recursive: true);
    }

    private static readonly Guid PlanId = Guid.Parse("11111111-2222-3333-4444-555555555555");

    private static AnswerKeyRelabel Relabel(string docId, string relevance, string rationale = "") => new()
    {
        PlanId = PlanId,
        DocId = docId,
        Relevance = relevance,
        Rationale = rationale,
        CapturedUtc = new DateTime(2026, 10, 12, 9, 30, 0, DateTimeKind.Utc),
        Source = AnswerKeyCsvWriter.FileName
    };

    private static void AssertEmpty(AnswerKeyRelabelSet set)
    {
        Assert.Equal(1, set.Version);
        Assert.Empty(set.Relabels);
        Assert.Empty(set.Files);
    }

    [Fact]
    public void FileName_IsConstant_AndInCaseFolder()
    {
        Assert.Equal("answer-key-relabels.json", AnswerKeyRelabelStore.FileName);
        Assert.Equal(Path.Combine(_folder, "answer-key-relabels.json"), new AnswerKeyRelabelStore(_folder).FilePath);
    }

    [Fact]
    public void Load_MissingFile_IsEmpty()
    {
        AssertEmpty(new AnswerKeyRelabelStore(_folder).Load());
    }

    [Fact]
    public void SaveThenLoad_RoundTripsRelabelsAndSnapshots()
    {
        var store = new AnswerKeyRelabelStore(_folder);
        var set = new AnswerKeyRelabelSet
        {
            Relabels =
            {
                Relabel("BR-000001", "Not Relevant", "User says routine,\r\nnot evidence."),
                Relabel("BR-000002", "Relevant")
            },
            Files =
            {
                [AnswerKeyXlsxWriter.FileName] = new AnswerKeyFileSnapshot
                {
                    PlanId = PlanId,
                    Sha256 = new string('a', 64),
                    WrittenUtc = new DateTime(2026, 10, 12, 10, 0, 0, DateTimeKind.Utc),
                    Applied = { ["BR-000001"] = Relabel("BR-000001", "Not Relevant", "User says routine,\r\nnot evidence.") }
                }
            }
        };

        store.Save(set);
        var loaded = store.Load();

        Assert.Equal(1, loaded.Version);
        Assert.Equal(2, loaded.Relabels.Count);
        var first = loaded.Relabels[0];
        Assert.Equal(PlanId, first.PlanId);
        Assert.Equal("BR-000001", first.DocId);
        Assert.Equal("Not Relevant", first.Relevance);
        Assert.Equal("User says routine,\r\nnot evidence.", first.Rationale);
        Assert.Equal(new DateTime(2026, 10, 12, 9, 30, 0, DateTimeKind.Utc), first.CapturedUtc);
        Assert.Equal(AnswerKeyCsvWriter.FileName, first.Source);
        Assert.Equal("Relevant", loaded.Relabels[1].Relevance);

        var snapshot = Assert.Single(loaded.Files);
        Assert.Equal(AnswerKeyXlsxWriter.FileName, snapshot.Key);
        Assert.Equal(PlanId, snapshot.Value.PlanId);
        Assert.Equal(new string('a', 64), snapshot.Value.Sha256);
        Assert.Equal(new DateTime(2026, 10, 12, 10, 0, 0, DateTimeKind.Utc), snapshot.Value.WrittenUtc);
        var applied = Assert.Single(snapshot.Value.Applied);
        Assert.Equal("BR-000001", applied.Key);
        Assert.Equal("Not Relevant", applied.Value.Relevance);
    }

    [Fact]
    public void Load_DamagedJson_IsEmpty()
    {
        File.WriteAllText(Path.Combine(_folder, AnswerKeyRelabelStore.FileName), "{ not json");

        AssertEmpty(new AnswerKeyRelabelStore(_folder).Load());
    }

    [Fact]
    public void Load_NullCollectionsAndBlankEntries_AreNormalized()
    {
        File.WriteAllText(Path.Combine(_folder, AnswerKeyRelabelStore.FileName),
            "{\"version\":1,\"relabels\":[null,{\"docId\":\"\"},{\"docId\":\"BR-000003\",\"relevance\":\"Relevant\"}]," +
            "\"files\":{\"answer-key.csv\":{\"sha256\":\"ab\",\"applied\":null}}}");

        var set = new AnswerKeyRelabelStore(_folder).Load();

        Assert.Equal("BR-000003", Assert.Single(set.Relabels).DocId);
        Assert.NotNull(Assert.Single(set.Files).Value.Applied);
    }

    [Fact]
    public void Load_NullCollections_AreEmpty()
    {
        File.WriteAllText(Path.Combine(_folder, AnswerKeyRelabelStore.FileName),
            "{\"version\":1,\"relabels\":null,\"files\":null}");

        AssertEmpty(new AnswerKeyRelabelStore(_folder).Load());
    }

    [Fact]
    public void Load_FileLargerThanCap_IsEmpty()
    {
        var path = Path.Combine(_folder, AnswerKeyRelabelStore.FileName);
        using (var stream = new FileStream(path, FileMode.CreateNew))
            stream.SetLength(AnswerKeyRelabelStore.MaxFileBytes + 1);

        AssertEmpty(new AnswerKeyRelabelStore(_folder).Load());
    }

    [Fact]
    public void Save_LeavesNoTempFile()
    {
        var store = new AnswerKeyRelabelStore(_folder);

        store.Save(new AnswerKeyRelabelSet { Relabels = { Relabel("BR-000001", "Relevant") } });
        store.Save(new AnswerKeyRelabelSet { Relabels = { Relabel("BR-000002", "Relevant") } });

        Assert.Empty(Directory.GetFiles(_folder, "*.tmp"));
        Assert.Equal("BR-000002", Assert.Single(store.Load().Relabels).DocId);
    }

    [Fact]
    public void Save_MissingFolder_ThrowsIOException()
    {
        var store = new AnswerKeyRelabelStore(Path.Combine(_folder, "does-not-exist"));

        Assert.ThrowsAny<IOException>(() => store.Save(new AnswerKeyRelabelSet()));
    }
}
