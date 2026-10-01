using ReelDiscovery.Models.Case;
using ReelDiscovery.Services.Case;

namespace ReelDiscovery.Tests.Case;

public class CaseFolderNamingTests : IDisposable
{
    private readonly string _root;

    public CaseFolderNamingTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "rd-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public void Title_Pleading()
    {
        var draft = CaseTestData.SampleComplaintDraft();

        Assert.Equal("Brightline Glazing, LLC v. Harmon Builders, Inc.", CaseFolderNaming.CaseTitle(draft));
        Assert.Equal("Brightline v. Harmon", CaseFolderNaming.DefaultFolderName(draft));
    }

    [Fact]
    public void Title_Memo()
    {
        var draft = CaseTestData.SampleMemoDraft();
        var memo = draft.CoreDocument!.Memo;

        Assert.Equal($"{memo.Organization} - {memo.Re}", CaseFolderNaming.CaseTitle(draft));

        memo.Organization = "";
        Assert.Equal($"{draft.CoreDocument.Parties[0].Name} - {memo.Re}", CaseFolderNaming.CaseTitle(draft));
    }

    [Fact]
    public void Title_Fallbacks()
    {
        var draft = CaseTestData.SampleComplaintDraft();
        draft.CoreDocument!.Parties.Clear();
        Assert.Equal(draft.CoreDocument.Title, CaseFolderNaming.CaseTitle(draft));

        Assert.Equal(CaseFolderNaming.Untitled, CaseFolderNaming.CaseTitle(new CaseDraft()));
        Assert.Equal(CaseFolderNaming.Untitled, CaseFolderNaming.DefaultFolderName(new CaseDraft()));
    }

    [Fact]
    public void Sanitize_InvalidChars()
    {
        Assert.Equal("ABC", CaseFolderNaming.Sanitize("A/B:C*?"));
    }

    [Fact]
    public void Sanitize_TrailingDotsAndSpaces()
    {
        Assert.Equal("Harmon v", CaseFolderNaming.Sanitize("Harmon v."));
        Assert.Equal("x", CaseFolderNaming.Sanitize("  x . . "));
    }

    [Fact]
    public void Sanitize_ReservedNames()
    {
        Assert.Equal("CON Case", CaseFolderNaming.Sanitize("CON"));
        Assert.Equal("lpt1 Case", CaseFolderNaming.Sanitize("lpt1"));
        Assert.Equal("COM3.txt Case", CaseFolderNaming.Sanitize("COM3.txt"));
        Assert.Equal("Conway v. Smith", CaseFolderNaming.Sanitize("Conway v. Smith"));
    }

    [Fact]
    public void Sanitize_Length()
    {
        var name = string.Concat(Enumerable.Repeat("Abcdefghi.", 12));
        var result = CaseFolderNaming.Sanitize(name);

        Assert.True(result.Length <= CaseFolderNaming.MaxLength);
        Assert.False(result.EndsWith('.'));
        Assert.False(result.EndsWith(' '));
    }

    [Fact]
    public void Sanitize_TraversalStripped()
    {
        var result = CaseFolderNaming.Sanitize("..\\..\\Windows");
        Assert.DoesNotContain("\\", result);
        Assert.False(result.StartsWith(".."));

        Assert.Equal(CaseFolderNaming.Untitled, CaseFolderNaming.Sanitize(""));
        Assert.Equal(CaseFolderNaming.Untitled, CaseFolderNaming.Sanitize(null));
        Assert.Equal(CaseFolderNaming.Untitled, CaseFolderNaming.Sanitize("<>|?*"));
        Assert.Equal(CaseFolderNaming.Untitled, CaseFolderNaming.Sanitize(" . .. "));
    }

    [Fact]
    public void Unique()
    {
        Directory.CreateDirectory(Path.Combine(_root, "Name"));
        Directory.CreateDirectory(Path.Combine(_root, "Name (2)"));

        var result = CaseFolderNaming.UniqueFolderPath(_root, "Name");

        Assert.Equal(Path.Combine(Path.GetFullPath(_root), "Name (3)"), result);
        Assert.StartsWith(Path.GetFullPath(_root) + Path.DirectorySeparatorChar, result);
    }

    [Fact]
    public void Unique_EscapingRoot_Throws()
    {
        Assert.Throws<InvalidOperationException>(() => CaseFolderNaming.UniqueFolderPath(_root, "..\\outside"));
    }
}
