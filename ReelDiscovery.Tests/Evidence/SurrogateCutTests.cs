using DocumentFormat.OpenXml.Packaging;
using ReelDiscovery.Models;
using ReelDiscovery.Models.Evidence;
using ReelDiscovery.Services;
using ReelDiscovery.Services.Evidence;

namespace ReelDiscovery.Tests.Evidence;

// IN-01: a cut must never end on a high surrogate, and the Office builders must drop unpaired surrogates
public class SurrogateCutTests
{
    private const string Emoji = "\U0001F600"; // one astral character = a surrogate pair

    private static bool HasLoneSurrogate(string text)
    {
        for (var i = 0; i < text.Length; i++)
        {
            if (char.IsHighSurrogate(text[i]))
            {
                if (i + 1 >= text.Length || !char.IsLowSurrogate(text[i + 1]))
                    return true;
                i++;
            }
            else if (char.IsLowSurrogate(text[i]))
            {
                return true;
            }
        }
        return false;
    }

    // Puts the pair across the cut: the high surrogate is the last char kept by a naive text[..max]
    private static string AcrossCut(int max) => new string('a', max - 1) + Emoji + "tail";

    [Fact]
    public void AttachmentRenderer_Clean_DoesNotSplitPairAtCut()
    {
        var cut = AttachmentRenderer.Clean(AcrossCut(AttachmentRenderer.MaxChars));
        Assert.False(HasLoneSurrogate(cut));
        Assert.True(cut.Length <= AttachmentRenderer.MaxChars);
    }

    [Fact]
    public void FillerRenderer_CleanTitle_DoesNotSplitPairAtCut()
    {
        var cut = FillerRenderer.CleanTitle(AcrossCut(FillerRenderer.MaxTitleChars));
        Assert.False(HasLoneSurrogate(cut));
        Assert.True(cut.Length <= FillerRenderer.MaxTitleChars);
    }

    [Fact]
    public void SideWorld_Clip_DoesNotSplitPairAtCut()
    {
        var cut = SideWorld.Clip(AcrossCut(SideWorld.MaxNameChars));
        Assert.False(HasLoneSurrogate(cut));
        Assert.True(cut.Length <= SideWorld.MaxNameChars);
    }

    [Fact]
    public void Cut_KeepsWholePairBeforeTheCut()
    {
        var text = new string('a', FillerRenderer.MaxTitleChars - 2) + Emoji + "tail";
        Assert.EndsWith(Emoji, FillerRenderer.CleanTitle(text));
    }

    [Fact]
    public void OfficeClean_DropsUnpairedSurrogates_KeepsPairs()
    {
        var cleaned = OfficeDocumentService.Clean("a\uD83Db\uDE00c" + Emoji + "\uD83D");
        Assert.Equal("abc" + Emoji, cleaned);
    }

    [Fact]
    public void CreateWordDocument_LoneSurrogateInModelText_BuildsValidPackage()
    {
        var office = new OfficeDocumentService();
        var meta = new DocumentMetadata
        {
            Author = "Test Author",
            Created = new DateTimeOffset(2024, 3, 4, 9, 30, 0, TimeSpan.Zero),
            Modified = new DateTimeOffset(2024, 3, 4, 9, 30, 0, TimeSpan.Zero)
        };
        var sections = new List<WordSection>
        {
            new() { Heading = "Heading \uD83D", Paragraphs = new List<string> { "Body ends on a cut \uD83D" } }
        };

        var bytes = office.CreateWordDocument("Title \uD83D", sections, null, OrganizationTheme.Default, meta);

        using var stream = new MemoryStream(bytes);
        using var doc = WordprocessingDocument.Open(stream, false);
        var text = doc.MainDocumentPart!.Document!.InnerText;
        Assert.Contains("Body ends on a cut", text);
        Assert.False(HasLoneSurrogate(text));
    }
}
