using DocumentFormat.OpenXml.Wordprocessing;
using ReelDiscovery.Models.Discovery;
using static ReelDiscovery.Services.Case.CaseGuideDocxExporter;

namespace ReelDiscovery.Services.Discovery;

/// <summary>
/// Requests for production, interrogatories and requests for admission as .docx bytes (ANC-02..ANC-05), built only from
/// the stored WrittenDiscoveryContent so Save Again needs no model call. Reuses the case guide WordBuilder, caption and
/// signature writers; WordBuilder strips characters XML cannot carry (T-08-20).
/// </summary>
public static class DiscoveryDocxBuilder
{
    public static byte[] Build(WrittenDiscoveryContent content)
    {
        ArgumentNullException.ThrowIfNull(content);
        var word = new WordBuilder();

        if (content.Variant == DiscoveryVariant.Civil)
        {
            // The caption already ends with the set title
            WriteCaption(word, content.CaptionText ?? string.Empty);
        }
        else
        {
            WriteMemoCaption(word, content);
        }

        if (!string.IsNullOrWhiteSpace(content.PreliminaryStatement))
            word.Paragraph(content.PreliminaryStatement, BodyFormat());

        var definitions = (content.Definitions ?? new List<DiscoveryDefinition>()).Where(d => d != null).ToList();
        if (definitions.Count > 0)
        {
            Heading(word, "DEFINITIONS");
            for (var i = 0; i < definitions.Count; i++)
                word.Paragraph($"{i + 1}. \"{definitions[i].Term}\" {definitions[i].Text}", BodyFormat());
        }

        var instructions = (content.Instructions ?? new List<string>()).Where(s => !string.IsNullOrWhiteSpace(s)).ToList();
        if (instructions.Count > 0)
        {
            Heading(word, "INSTRUCTIONS");
            for (var i = 0; i < instructions.Count; i++)
                word.Paragraph($"{i + 1}. {instructions[i]}", BodyFormat());
        }

        Heading(word, RequestsHeading(content.Kind, content.Variant));
        var requests = (content.Requests ?? new List<DiscoveryRequest>()).Where(r => r != null).ToList();
        for (var i = 0; i < requests.Count; i++)
        {
            // Numbered by position so the printed numbers are always consecutive; the code prefix is never dropped
            var request = requests[i];
            var prefix = (request.Prefix ?? string.Empty).Trim();
            var text = prefix.Length > 0 ? $"{prefix} {request.Text}" : request.Text;
            word.Paragraph(text, new ParaFormat
            {
                BoldLead = $"{content.RequestLabel} {i + 1}: ",
                Align = JustificationValues.Both,
                KeepLines = true
            });
        }

        WriteSignatureBlock(word, content.SignatureText ?? string.Empty);

        Heading(word, "CERTIFICATE OF SERVICE");
        word.Paragraph(content.CertificateText, BodyFormat());

        return word.Save(content.Title, landscape: false);
    }

    // Memo variants (D-25): matter header lines centered in bold instead of a pleading caption
    private static void WriteMemoCaption(WordBuilder word, WrittenDiscoveryContent content)
    {
        var lines = (content.CaptionText ?? string.Empty).Replace("\r\n", "\n").Split('\n').Select(l => l.Trim()).ToList();
        while (lines.Count > 0 && lines[^1].Length == 0)
            lines.RemoveAt(lines.Count - 1);

        // The caption text ends with the title; it is written once, as a heading
        var title = (content.Title ?? string.Empty).Trim();
        if (lines.Count > 0 && string.Equals(lines[^1], title, StringComparison.Ordinal))
            lines.RemoveAt(lines.Count - 1);

        foreach (var line in lines)
        {
            if (line.Length == 0)
                word.Paragraph(string.Empty, Compact());
            else
                word.Paragraph(line, new ParaFormat { Bold = true, Align = JustificationValues.Center, SpaceAfter = 0 });
        }

        word.Paragraph(title, new ParaFormat { Style = "Heading1", Align = JustificationValues.Center, SpaceBefore = 240 });
    }

    private static void Heading(WordBuilder word, string text) =>
        word.Paragraph(text, new ParaFormat { Style = "Heading1", Align = JustificationValues.Center });

    private static string RequestsHeading(DiscoveryKind kind, DiscoveryVariant variant) => (variant, kind) switch
    {
        (DiscoveryVariant.MemoCriminal, _) => "DOCUMENTS TO BE PRODUCED",
        (DiscoveryVariant.MemoRegulatory, DiscoveryKind.Interrogatories) => "INTERROGATORIES",
        (DiscoveryVariant.MemoRegulatory, _) => "DOCUMENTS TO BE PRODUCED",
        (DiscoveryVariant.MemoCivil, DiscoveryKind.Interrogatories) => "QUESTIONS",
        (DiscoveryVariant.MemoCivil, DiscoveryKind.RequestsForAdmission) => "REQUESTS TO ADMIT",
        (DiscoveryVariant.MemoCivil, _) => "DOCUMENTS REQUESTED",
        (_, DiscoveryKind.Interrogatories) => "INTERROGATORIES",
        (_, DiscoveryKind.RequestsForAdmission) => "REQUESTS FOR ADMISSION",
        _ => "REQUESTS FOR PRODUCTION"
    };
}
