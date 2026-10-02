using ReelDiscovery.Helpers;
using ReelDiscovery.Models.Case;
using ReelDiscovery.Models.Corpus;
using ReelDiscovery.Services.Corpus;

namespace ReelDiscovery.Tests.Corpus;

public class CorpusApprovalTests : IDisposable
{
    private const string FolderName = "Brightline v. Harmon";
    private readonly string _root;
    private readonly string _folder;

    public CorpusApprovalTests()
    {
        // Temp folder only: never a real case folder
        _root = Path.Combine(Path.GetTempPath(), "rd-approve-" + Guid.NewGuid().ToString("N"));
        _folder = Path.Combine(_root, FolderName);
        Directory.CreateDirectory(_folder);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private static (CaseDraft Draft, CorpusPlan Plan) Setup()
    {
        var draft = CorpusTestData.RichDraft();
        var plan = PlanFixtures.ValidPlan(draft);
        plan.PlanId = Guid.NewGuid();
        plan.CreatedUtc = new DateTime(2024, 9, 1, 12, 0, 0, DateTimeKind.Utc);
        draft.Corpus ??= new CorpusState();
        draft.Corpus.Settings = plan.Settings;
        draft.AdvanceStage(CaseStage.CorpusSettings);
        return (draft, plan);
    }

    private static void Attach(CaseDraft draft, CorpusPlan plan)
    {
        draft.Corpus!.Plan = plan;
        draft.Corpus.PlanId = plan.PlanId;
        draft.Corpus.PlanGuideHash = plan.GuideHash;
        draft.Corpus.PlanSettingsHash = plan.SettingsHash;
        draft.Corpus.PlanCreatedUtc = plan.CreatedUtc;
    }

    private static FileWriter FailingWriter(int failOn, Exception error)
    {
        var count = 0;
        return (path, bytes, ct) =>
        {
            count++;
            if (count == failOn)
                throw error;
            return Task.CompletedTask;
        };
    }

    // ---- Task 1: approval writes ----

    [Fact]
    public void BuildFiles_ReturnsThreeFilesInOrder()
    {
        var (draft, plan) = Setup();

        var files = CorpusApproval.BuildFiles(draft, plan, FolderName);

        Assert.Equal(3, files.Count);
        Assert.Equal(CorpusFileKind.AnswerKeyCsv, files[0].Kind);
        Assert.Equal("answer-key.csv", files[0].FileName);
        Assert.Equal(CorpusFileKind.AnswerKeyXlsx, files[1].Kind);
        Assert.Equal("answer-key.xlsx", files[1].FileName);
        Assert.Equal(CorpusFileKind.ReviewProtocol, files[2].Kind);
        Assert.Equal("Brightline v. Harmon - Review Protocol.docx", files[2].FileName);
        Assert.All(files, f => Assert.NotEmpty(f.Bytes));
    }

    [Fact]
    public async Task Approve_DefaultWriter_WritesFilesAndRecordsApproval()
    {
        var (draft, plan) = Setup();
        Attach(draft, plan);
        draft.Corpus!.DismissedStaleGuideHash = "old";

        var result = await CorpusApproval.ApproveAsync(_folder, draft, plan);

        Assert.True(result.Succeeded);
        Assert.True(File.Exists(Path.Combine(_folder, "answer-key.csv")));
        Assert.True(File.Exists(Path.Combine(_folder, "answer-key.xlsx")));
        Assert.True(File.Exists(Path.Combine(_folder, "Brightline v. Harmon - Review Protocol.docx")));
        Assert.True(draft.Corpus.Approved);
        Assert.NotNull(draft.Corpus.ApprovedUtc);
        Assert.Equal(3, draft.Corpus.WrittenFiles.Count);
        Assert.All(draft.Corpus.WrittenFiles, f => Assert.Equal(plan.PlanId, f.PlanId));
        Assert.Equal(string.Empty, draft.Corpus.DismissedStaleGuideHash);
        Assert.True(draft.Stage >= CaseStage.ReviewPlan);
    }

    [Fact]
    public async Task Approve_SecondFileFails_RecordsNothing()
    {
        var (draft, plan) = Setup();
        Attach(draft, plan);
        var before = draft.Corpus!.WrittenFiles.Count;

        var result = await CorpusApproval.ApproveAsync(_folder, draft, plan, FailingWriter(2, new IOException("Disk full")));

        Assert.False(result.Succeeded);
        Assert.Equal("answer-key.xlsx", result.FailedFileName);
        Assert.Equal("Disk full", result.Error);
        Assert.False(result.FileOpenElsewhere);
        Assert.False(draft.Corpus.Approved);
        Assert.Null(draft.Corpus.ApprovedUtc);
        Assert.Equal(before, draft.Corpus.WrittenFiles.Count);
    }

    [Fact]
    public async Task Approve_SharingViolation_ReportsFileOpenElsewhere()
    {
        var (draft, plan) = Setup();
        Attach(draft, plan);
        var error = new IOException("The file is in use.", unchecked((int)0x80070020));

        var result = await CorpusApproval.ApproveAsync(_folder, draft, plan, FailingWriter(1, error));

        Assert.False(result.Succeeded);
        Assert.Equal("answer-key.csv", result.FailedFileName);
        Assert.True(result.FileOpenElsewhere);
    }

    [Fact]
    public async Task Approve_AfterReplan_ReplacesWrittenFiles()
    {
        var (draft, plan) = Setup();
        Attach(draft, plan);
        Assert.True((await CorpusApproval.ApproveAsync(_folder, draft, plan)).Succeeded);

        var next = PlanFixtures.Clone(plan);
        next.PlanId = Guid.NewGuid();
        Attach(draft, next);
        draft.Corpus!.Approved = false;
        Assert.True((await CorpusApproval.ApproveAsync(_folder, draft, next)).Succeeded);

        Assert.Equal(3, draft.Corpus.WrittenFiles.Count);
        Assert.All(draft.Corpus.WrittenFiles, f => Assert.Equal(next.PlanId, f.PlanId));
    }

    [Fact]
    public void FileLockDetector_SharingViolationHResult_True()
    {
        var error = new IOException("in use", unchecked((int)0x80070020));
        Assert.True(FileLockDetector.IsFileOpenElsewhere(error, Path.Combine(_folder, "x.csv")));
        var locked = new IOException("locked", unchecked((int)0x80070021));
        Assert.True(FileLockDetector.IsFileOpenElsewhere(locked, Path.Combine(_folder, "x.csv")));
    }

    [Fact]
    public void FileLockDetector_UnauthorizedOnMissingFile_False()
    {
        var path = Path.Combine(_folder, "missing.csv");
        Assert.False(FileLockDetector.IsFileOpenElsewhere(new UnauthorizedAccessException(), path));
    }

    [Fact]
    public void FileLockDetector_FileHeldWithShareNone_True()
    {
        var path = Path.Combine(_folder, "held.csv");
        File.WriteAllText(path, "a");
        using (new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            Assert.True(FileLockDetector.IsFileOpenElsewhere(new UnauthorizedAccessException(), path));
        }

        Assert.False(FileLockDetector.IsFileOpenElsewhere(new UnauthorizedAccessException(), path));
    }
}
