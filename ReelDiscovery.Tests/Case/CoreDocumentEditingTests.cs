using ReelDiscovery.Models.Case;
using ReelDiscovery.Services.Case;

namespace ReelDiscovery.Tests.Case;

public class CoreDocumentEditingTests
{
    [Fact]
    public void SplitParagraphs_SplitsOnBlankLines()
    {
        var result = CoreDocumentEditing.SplitParagraphs("A one.\r\n\r\nB two.\n\n\nC three.");

        Assert.Equal(new[] { "A one.", "B two.", "C three." }, result);
    }

    [Fact]
    public void SplitParagraphs_StripsLeadingNumbers()
    {
        var result = CoreDocumentEditing.SplitParagraphs("1. First.\r\n\r\n2. Second.");

        Assert.Equal(new[] { "First.", "Second." }, result);
    }

    [Fact]
    public void SplitParagraphs_WhitespaceOnly_ReturnsEmpty()
    {
        Assert.Empty(CoreDocumentEditing.SplitParagraphs("   \r\n\r\n  "));
        Assert.Empty(CoreDocumentEditing.SplitParagraphs(null));
    }

    [Fact]
    public void SplitParagraphs_SingleNewline_DoesNotSplit()
    {
        var result = CoreDocumentEditing.SplitParagraphs("Line one\r\nstill one");

        Assert.Equal(new[] { "Line one\r\nstill one" }, result);
    }

    [Fact]
    public void JoinParagraphs_UsesBlankLine()
    {
        Assert.Equal("A\r\n\r\nB", CoreDocumentEditing.JoinParagraphs(new[] { "A", "B" }));
    }

    [Fact]
    public void ApplyEdit_ChangedText_UpdatesSectionAndRevision()
    {
        var draft = CaseTestData.SampleComplaintDraft();
        var revision = draft.Revision;

        var changed = CoreDocumentEditing.ApplyEdit(draft, "facts-1", "New first.\r\n\r\nNew second.");

        Assert.True(changed);
        var section = draft.CoreDocument!.FindSection("facts-1")!;
        Assert.Equal(new[] { "New first.", "New second." }, section.Paragraphs);
        Assert.True(section.IsUserEdited);
        Assert.Equal(revision + 1, draft.Revision);
    }

    [Fact]
    public void ApplyEdit_IdenticalText_NoChange()
    {
        var draft = CaseTestData.SampleComplaintDraft();
        var revision = draft.Revision;
        var section = draft.CoreDocument!.FindSection("facts-1")!;
        var text = CoreDocumentEditing.JoinParagraphs(section.Paragraphs);

        var changed = CoreDocumentEditing.ApplyEdit(draft, "facts-1", text);

        Assert.False(changed);
        Assert.Equal(revision, draft.Revision);
        Assert.False(section.IsUserEdited);
    }

    [Fact]
    public void ApplyEdit_UnknownSectionOrNoDocument_ReturnsFalse()
    {
        var draft = CaseTestData.SampleComplaintDraft();

        Assert.False(CoreDocumentEditing.ApplyEdit(draft, "no-such-section", "Text."));
        Assert.False(CoreDocumentEditing.ApplyEdit(new CaseDraft(), "facts-1", "Text."));
    }

    [Fact]
    public void ApplyEdit_FailedSection_BecomesReady()
    {
        var draft = CaseTestData.SampleComplaintDraft();
        var section = draft.CoreDocument!.FindSection("facts-2")!;
        section.Status = SectionStatus.Failed;
        section.FailureMessage = "timeout";

        Assert.True(CoreDocumentEditing.ApplyEdit(draft, "facts-2", "Hand-written paragraph."));

        Assert.Equal(SectionStatus.Ready, section.Status);
        Assert.Equal(string.Empty, section.FailureMessage);
    }
}
