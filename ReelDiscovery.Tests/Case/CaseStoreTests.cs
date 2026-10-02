using ReelDiscovery.Models;
using ReelDiscovery.Models.Case;
using ReelDiscovery.Services.Case;

namespace ReelDiscovery.Tests.Case;

public class CaseStoreTests : IDisposable
{
    private readonly string _temp;
    private readonly string _casesRoot;
    private readonly string _appData;

    public CaseStoreTests()
    {
        // Temp folders only: never the user's real Documents or %LOCALAPPDATA% recent list
        _temp = Path.Combine(Path.GetTempPath(), "rd-" + Guid.NewGuid().ToString("N"));
        _casesRoot = Path.Combine(_temp, "Cases");
        _appData = Path.Combine(_temp, "AppData");
    }

    public void Dispose()
    {
        if (Directory.Exists(_temp))
            Directory.Delete(_temp, recursive: true);
    }

    private CaseStore CreateStore() => new(_casesRoot, _appData);

    private static string CaseFile(CaseDraft draft) => Path.Combine(draft.FolderPath, CaseStore.CaseFileName);

    private static CaseDraft ReadBack(CaseDraft draft) => CaseDraft.FromJson(File.ReadAllText(CaseFile(draft)));

    [Fact]
    public void Constructor_DoesNoIo()
    {
        _ = CreateStore();
        Assert.False(Directory.Exists(_temp));
    }

    [Fact]
    public void WizardState_ExposesStore()
    {
        var state = new WizardState();
        Assert.NotNull(state.CaseStore);
        Assert.Equal(string.Empty, state.PendingNotice);
    }

    [Fact]
    public async Task Save_NoCoreDocument_NoOp()
    {
        var store = CreateStore();
        var draft = new CaseDraft { Scenario = "Only a scenario" };

        Assert.False(CaseStore.CanSave(draft));
        Assert.False(await store.SaveAsync(draft));
        Assert.False(Directory.Exists(_casesRoot));
        Assert.Equal(string.Empty, store.LastSaveError);
        Assert.Equal(string.Empty, draft.FolderPath);
    }

    [Fact]
    public async Task Save_CreatesFolderAndFile()
    {
        var store = CreateStore();
        var draft = CaseTestData.SampleGuidedDraft();

        Assert.True(await store.SaveAsync(draft));

        var expected = Path.Combine(Path.GetFullPath(_casesRoot), "Brightline v. Harmon");
        Assert.Equal(expected, draft.FolderPath);
        Assert.True(File.Exists(Path.Combine(expected, "case.json")));
        Assert.NotNull(store.LastSavedUtc);
        Assert.Equal(string.Empty, store.LastSaveError);

        var recent = Assert.Single(store.GetRecentCases());
        Assert.Equal(CaseStage.CaseGuide, recent.Stage);
        Assert.Equal(Path.Combine(expected, "case.json"), recent.Path);
        Assert.Equal("Brightline Glazing, LLC v. Harmon Builders, Inc.", recent.Title);
    }

    [Fact]
    public async Task Save_SecondCaseSameName_GetsSuffix()
    {
        var store = CreateStore();
        var first = CaseTestData.SampleGuidedDraft();
        var second = CaseTestData.SampleGuidedDraft();

        Assert.True(await store.SaveAsync(first));
        Assert.True(await store.SaveAsync(second));

        Assert.Equal("Brightline v. Harmon (2)", Path.GetFileName(second.FolderPath));
        Assert.Equal(2, store.GetRecentCases().Count);
    }

    [Fact]
    public async Task Save_NoTempFilesLeft()
    {
        var store = CreateStore();
        var draft = CaseTestData.SampleGuidedDraft();

        for (var i = 0; i < 3; i++)
        {
            draft.CoreDocument!.Title = $"Title {i}";
            Assert.True(await store.SaveAsync(draft));
        }

        Assert.Empty(Directory.GetFiles(draft.FolderPath, "*.tmp"));
        Assert.Empty(Directory.GetFiles(_appData, "*.tmp"));
        Assert.Equal("Title 2", ReadBack(draft).CoreDocument!.Title);
    }

    [Fact]
    public async Task Save_Coalesces()
    {
        var store = CreateStore();
        var draft = CaseTestData.SampleGuidedDraft();

        draft.CoreDocument!.Title = "First title";
        var firstTask = store.SaveAsync(draft);
        draft.CoreDocument.Title = "Second title";
        var secondTask = store.SaveAsync(draft);

        var results = await Task.WhenAll(firstTask, secondTask);

        Assert.All(results, Assert.True);
        Assert.Equal("Second title", ReadBack(draft).CoreDocument!.Title);
        Assert.Empty(Directory.GetFiles(draft.FolderPath, "*.tmp"));
    }

    [Fact]
    public async Task SaveNow_AfterPendingAsync()
    {
        var store = CreateStore();
        var draft = CaseTestData.SampleGuidedDraft();

        draft.CoreDocument!.Title = "Async title";
        var pending = store.SaveAsync(draft);
        draft.CoreDocument.Title = "Close title";

        Assert.True(store.SaveNow(draft));
        await pending;

        Assert.Equal("Close title", ReadBack(draft).CoreDocument!.Title);
        Assert.Empty(Directory.GetFiles(draft.FolderPath, "*.tmp"));
    }

    [Fact]
    public async Task Save_Failure_Reported()
    {
        var store = CreateStore();
        var draft = CaseTestData.SampleGuidedDraft();
        var folder = store.EnsureFolder(draft);
        Directory.CreateDirectory(Path.Combine(folder, CaseStore.CaseFileName));

        var raised = 0;
        store.SaveCompleted += (_, _) => raised++;

        var ok = await store.SaveAsync(draft);

        Assert.False(ok);
        Assert.NotEqual(string.Empty, store.LastSaveError);
        Assert.Equal(1, raised);
        Assert.Null(store.LastSavedUtc);
        Assert.Empty(Directory.GetFiles(folder, "*.tmp"));
    }

    [Fact]
    public void SaveNow_Failure_Reported()
    {
        var store = CreateStore();
        var draft = CaseTestData.SampleGuidedDraft();
        var folder = store.EnsureFolder(draft);
        Directory.CreateDirectory(Path.Combine(folder, CaseStore.CaseFileName));

        Assert.False(store.SaveNow(draft));
        Assert.NotEqual(string.Empty, store.LastSaveError);
    }

    [Fact]
    public async Task Save_MissingCaseFolder_FailsWithoutForking()
    {
        // WR-06: a folder that went away (drive unplugged, renamed) must not silently become "<name> (2)"
        var store = CreateStore();
        var draft = CaseTestData.SampleGuidedDraft();
        Assert.True(await store.SaveAsync(draft));
        var folder = draft.FolderPath;
        Directory.Delete(folder, recursive: true);

        Assert.False(await store.SaveAsync(draft));
        Assert.False(store.SaveNow(draft));

        Assert.Equal(folder, draft.FolderPath);
        Assert.Contains("is not available", store.LastSaveError);
        Assert.Empty(Directory.GetDirectories(_casesRoot));
        Assert.Throws<DirectoryNotFoundException>(() => store.EnsureFolder(draft));
    }

    [Fact]
    public async Task Save_UpdatesUpdatedUtc_AndHasNoSecrets()
    {
        var store = CreateStore();
        var draft = CaseTestData.SampleGuidedDraft();
        var before = DateTime.UtcNow.AddSeconds(-1);
        draft.UpdatedUtc = new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        Assert.True(await store.SaveAsync(draft));

        Assert.True(draft.UpdatedUtc >= before);
        var json = File.ReadAllText(CaseFile(draft));
        Assert.DoesNotContain("ApiKey", json);
        Assert.DoesNotContain("sk-", json);
        Assert.DoesNotContain("FolderPath", json);
    }

    [Fact]
    public async Task Load_RoundTrip()
    {
        var store = CreateStore();
        var draft = CaseTestData.SampleGuidedDraft();
        Assert.True(await store.SaveAsync(draft));

        var other = CreateStore();
        var result = other.Load(CaseFile(draft));

        Assert.True(result.Succeeded);
        Assert.Equal(CaseLoadError.None, result.Error);
        Assert.Equal(draft.FolderPath, result.Draft!.FolderPath);
        Assert.Equal(draft.Guide!.Cast.Count, result.Draft.Guide!.Cast.Count);
        Assert.Equal(draft.CaseId, result.Draft.CaseId);
        Assert.Single(other.GetRecentCases());
    }

    [Fact]
    public async Task Load_Missing()
    {
        var store = CreateStore();
        var draft = CaseTestData.SampleGuidedDraft();
        Assert.True(await store.SaveAsync(draft));
        var path = CaseFile(draft);
        File.Delete(path);

        var result = store.Load(path);

        Assert.False(result.Succeeded);
        Assert.Equal(CaseLoadError.Missing, result.Error);
        Assert.Equal($"This case folder no longer exists: {path}. It was removed from Recent cases.", result.Message);
        Assert.Empty(store.GetRecentCases());
    }

    [Fact]
    public void Load_TooLarge()
    {
        var store = CreateStore();
        Directory.CreateDirectory(_temp);
        var path = Path.Combine(_temp, "case.json");
        using (var fs = new FileStream(path, FileMode.CreateNew))
            fs.SetLength(CaseStore.MaxCaseFileBytes + 1);

        var result = store.Load(path);

        Assert.Equal(CaseLoadError.TooLarge, result.Error);
        Assert.Equal("Could not open case.json: the file is larger than 50 MB. Choose a different case.json with Open Case...", result.Message);
    }

    [Fact]
    public void Load_NewerVersion()
    {
        var store = CreateStore();
        Directory.CreateDirectory(_temp);
        var path = Path.Combine(_temp, "case.json");
        File.WriteAllText(path, "{\"FormatVersion\":2,\"Scenario\":\"x\"}");

        var result = store.Load(path);

        Assert.Equal(CaseLoadError.NewerVersion, result.Error);
        Assert.Equal("This case was saved by a newer version of ReelDiscovery. Update the app to open it.", result.Message);
        Assert.Null(result.Draft);
    }

    [Theory]
    [InlineData("{not json")]
    [InlineData("null")]
    [InlineData("[1,2]")]
    [InlineData("")]
    public void Load_Corrupt(string content)
    {
        var store = CreateStore();
        Directory.CreateDirectory(_temp);
        var path = Path.Combine(_temp, "case.json");
        File.WriteAllText(path, content);

        var result = store.Load(path);

        Assert.Equal(CaseLoadError.Corrupt, result.Error);
        Assert.StartsWith("Could not open case.json: ", result.Message);
        Assert.EndsWith(" Choose a different case.json with Open Case...", result.Message);
        Assert.DoesNotContain(".. Choose", result.Message);
    }

    [Fact]
    public void Load_NullsNormalized()
    {
        var store = CreateStore();
        Directory.CreateDirectory(_temp);
        var path = Path.Combine(_temp, "case.json");
        File.WriteAllText(path, "{\"FormatVersion\":1,\"CoreDocument\":{\"Parties\":null},\"Guide\":{\"Cast\":null}}");

        var result = store.Load(path);

        Assert.True(result.Succeeded, result.Message);
        Assert.Empty(result.Draft!.CoreDocument!.Parties);
        Assert.Empty(result.Draft.Guide!.Cast);
        Assert.Equal(Path.GetFullPath(_temp), result.Draft.FolderPath);
    }

    [Fact]
    public async Task Recent_CapAndPrune()
    {
        var store = CreateStore();
        var drafts = new List<CaseDraft>();
        for (var i = 0; i < 10; i++)
        {
            var draft = CaseTestData.SampleGuidedDraft();
            Assert.True(await store.SaveAsync(draft));
            drafts.Add(draft);
        }

        var recent = store.GetRecentCases();
        Assert.Equal(CaseStore.MaxRecentCases, recent.Count);
        Assert.Equal(CaseFile(drafts[9]), recent[0].Path);
        Assert.Equal(CaseFile(drafts[2]), recent[7].Path);

        Directory.Delete(drafts[5].FolderPath, recursive: true);
        var pruned = store.GetRecentCases();
        Assert.Equal(7, pruned.Count);
        Assert.DoesNotContain(pruned, r => r.Path == CaseFile(drafts[5]));

        File.WriteAllText(Path.Combine(_appData, "recent.json"), "{corrupt");
        Assert.Empty(store.GetRecentCases());
    }

    [Fact]
    public async Task Recent_TouchExisting_MovesToFront()
    {
        var store = CreateStore();
        var first = CaseTestData.SampleGuidedDraft();
        var second = CaseTestData.SampleGuidedDraft();
        Assert.True(await store.SaveAsync(first));
        Assert.True(await store.SaveAsync(second));

        store.TouchRecent(first);

        var recent = store.GetRecentCases();
        Assert.Equal(2, recent.Count);
        Assert.Equal(CaseFile(first), recent[0].Path);

        store.RemoveRecent(CaseFile(first));
        Assert.Equal(CaseFile(second), Assert.Single(store.GetRecentCases()).Path);
    }

    [Fact]
    public async Task Move_MovesFolder()
    {
        var store = CreateStore();
        var draft = CaseTestData.SampleGuidedDraft();
        Assert.True(await store.SaveAsync(draft));
        var oldFolder = draft.FolderPath;
        var target = Path.Combine(_temp, "Elsewhere");

        var newFolder = store.MoveFolder(draft, target);

        Assert.False(Directory.Exists(oldFolder));
        Assert.Equal(Path.Combine(Path.GetFullPath(target), "Brightline v. Harmon"), newFolder);
        Assert.Equal(newFolder, draft.FolderPath);
        Assert.True(File.Exists(Path.Combine(newFolder, "case.json")));
        Assert.Equal(Path.Combine(newFolder, "case.json"), Assert.Single(store.GetRecentCases()).Path);

        // Later saves follow the moved folder
        draft.CoreDocument!.Title = "After move";
        Assert.True(await store.SaveAsync(draft));
        Assert.Equal("After move", ReadBack(draft).CoreDocument!.Title);
        Assert.False(Directory.Exists(oldFolder));
    }

    [Fact]
    public async Task Move_CopyPath_LockedSourceFile_CaseFollowsCopy()
    {
        // WR-05: an export open in Word blocks deleting the old folder; the case must already point at the full copy
        var store = CreateStore();
        store.CopyOnMove = true;
        var draft = CaseTestData.SampleGuidedDraft();
        Assert.True(await store.SaveAsync(draft));
        var oldFolder = draft.FolderPath;
        var export = Path.Combine(oldFolder, "Brightline v. Harmon - Core Document.docx");
        File.WriteAllText(export, "docx");
        var target = Path.Combine(_temp, "Elsewhere");

        string newFolder;
        using (new FileStream(export, FileMode.Open, FileAccess.Read, FileShare.Read))
            newFolder = store.MoveFolder(draft, target);

        Assert.Equal(newFolder, draft.FolderPath);
        Assert.True(File.Exists(Path.Combine(newFolder, "case.json")));
        Assert.True(File.Exists(Path.Combine(newFolder, Path.GetFileName(export))));
        Assert.Equal(Path.GetFullPath(oldFolder), store.LastMoveLeftover);
        Assert.Equal(Path.Combine(newFolder, "case.json"), Assert.Single(store.GetRecentCases()).Path);

        // Later saves go to the copy, never back into the half-deleted source
        draft.CoreDocument!.Title = "After move";
        Assert.True(await store.SaveAsync(draft));
        Assert.Equal("After move", ReadBack(draft).CoreDocument!.Title);
        Assert.False(File.Exists(Path.Combine(oldFolder, "case.json")));
    }

    [Fact]
    public async Task Move_CopyPath_CleanSourceIsRemoved()
    {
        var store = CreateStore();
        store.CopyOnMove = true;
        var draft = CaseTestData.SampleGuidedDraft();
        Assert.True(await store.SaveAsync(draft));
        var oldFolder = draft.FolderPath;

        var newFolder = store.MoveFolder(draft, Path.Combine(_temp, "Elsewhere"));

        Assert.False(Directory.Exists(oldFolder));
        Assert.Equal(string.Empty, store.LastMoveLeftover);
        Assert.True(File.Exists(Path.Combine(newFolder, "case.json")));
    }

    [Fact]
    public void WriteAtomic_ReplacesContent()
    {
        Directory.CreateDirectory(_temp);
        var path = Path.Combine(_temp, "x.json");
        CaseStore.WriteAtomic(path, "one"u8.ToArray());
        CaseStore.WriteAtomic(path, "two"u8.ToArray());

        Assert.Equal("two", File.ReadAllText(path));
        Assert.Single(Directory.GetFiles(_temp));
    }
}
