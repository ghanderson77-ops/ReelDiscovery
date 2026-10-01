using System.Text.RegularExpressions;
using ReelDiscovery.Models.Case;

namespace ReelDiscovery.Services.Case;

/// <summary>
/// Converts between a section's paragraph list and editor text, and applies user edits (CASE-07).
/// Paragraphs are separated by blank lines; numbers typed by the user are stripped because numbering is computed.
/// </summary>
public static partial class CoreDocumentEditing
{
    [GeneratedRegex(@"\r?\n\s*\r?\n")]
    private static partial Regex BlankLine();

    public static List<string> SplitParagraphs(string? editorText)
    {
        if (string.IsNullOrWhiteSpace(editorText))
            return new List<string>();

        return BlankLine().Split(editorText)
            .Select(p => CoreDocumentAssembler.StripLeadingNumber(p.Trim()))
            .Where(p => p.Length > 0)
            .ToList();
    }

    public static string JoinParagraphs(IEnumerable<string> paragraphs) => string.Join("\r\n\r\n", paragraphs);

    /// <summary>
    /// Replaces a section's paragraphs with the edited text. Returns false (and leaves Revision alone)
    /// when the section is unknown or the text is unchanged.
    /// </summary>
    public static bool ApplyEdit(CaseDraft draft, string sectionId, string editorText)
    {
        var section = draft?.CoreDocument?.FindSection(sectionId);
        if (draft == null || section == null)
            return false;

        var paragraphs = SplitParagraphs(editorText);
        if (paragraphs.SequenceEqual(section.Paragraphs))
            return false;

        section.Paragraphs = paragraphs;
        section.IsUserEdited = true;

        // The user wrote a failed or unwritten section by hand: it counts as written (CR-02)
        if ((section.Status is SectionStatus.Failed or SectionStatus.Pending) && paragraphs.Count > 0)
        {
            section.Status = SectionStatus.Ready;
            section.FailureMessage = string.Empty;
        }

        draft.Revision++;
        return true;
    }
}
