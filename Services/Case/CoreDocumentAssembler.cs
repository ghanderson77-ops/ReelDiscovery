using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using ReelDiscovery.Models.Case;

namespace ReelDiscovery.Services.Case;

/// <summary>
/// A pleading paragraph with its computed number. Numbers are never stored on the model.
/// </summary>
public class NumberedParagraph
{
    public int Number { get; set; }
    public string Text { get; set; } = string.Empty;
    public string SectionId { get; set; } = string.Empty;
    public bool IsIncorporation { get; set; }
}

/// <summary>
/// Code-owned legal skeleton: numbering, incorporation paragraphs, caption, prayer, signature and memo header (CASE-05).
/// The LLM only writes section paragraphs; everything structural is rendered here on every call.
/// </summary>
public static partial class CoreDocumentAssembler
{
    private const string MemoRule = "------------------------------------------------------------";

    private static readonly (int Value, string Numeral)[] RomanNumerals =
    {
        (1000, "M"), (900, "CM"), (500, "D"), (400, "CD"),
        (100, "C"), (90, "XC"), (50, "L"), (40, "XL"),
        (10, "X"), (9, "IX"), (5, "V"), (4, "IV"), (1, "I")
    };

    // "12. " or "3) " at the start; the required whitespace leaves "1.5 million" untouched
    [GeneratedRegex(@"^\s*\d{1,3}[.)]\s+")]
    private static partial Regex LeadingNumber();

    public static string ToRoman(int number)
    {
        if (number < 1 || number > 3999)
            throw new ArgumentOutOfRangeException(nameof(number), number, "Roman numerals support 1 to 3999");

        var sb = new StringBuilder();
        foreach (var (value, numeral) in RomanNumerals)
        {
            while (number >= value)
            {
                sb.Append(numeral);
                number -= value;
            }
        }

        return sb.ToString();
    }

    public static string StripLeadingNumber(string paragraph)
    {
        if (string.IsNullOrEmpty(paragraph))
            return string.Empty;

        return LeadingNumber().Replace(paragraph, string.Empty, 1).Trim();
    }

    public static string PleadingPartyLabel(CoreDocument doc)
    {
        var plaintiffs = doc.Parties.Count(p => IsPlaintiffSide(p.Role));
        return plaintiffs > 1 ? "Plaintiffs" : "Plaintiff";
    }

    public static string IncorporationText(CoreDocument doc, int lastFactParagraph) =>
        $"{PleadingPartyLabel(doc)} repeats and realleges paragraphs 1 through {lastFactParagraph} as if fully set forth herein.";

    public static List<NumberedParagraph> Number(CoreDocument doc)
    {
        var numbered = new List<NumberedParagraph>();
        if (!CoreDocumentTemplates.IsPleading(doc.DocumentType))
            return numbered;

        var next = 1;
        var lastFact = 0;

        foreach (var section in doc.Sections)
        {
            if (section.Kind == SectionKind.PrayerForRelief)
                continue;

            if (section.Kind == SectionKind.Count)
            {
                // Each count incorporates the factual allegations; falls back to the last paragraph so far
                var incorporateThrough = lastFact > 0 ? lastFact : next - 1;
                numbered.Add(new NumberedParagraph
                {
                    Number = next++,
                    Text = IncorporationText(doc, incorporateThrough),
                    SectionId = section.Id,
                    IsIncorporation = true
                });
            }

            foreach (var paragraph in section.Paragraphs)
            {
                var text = StripLeadingNumber(paragraph);
                if (text.Length == 0)
                    continue;

                numbered.Add(new NumberedParagraph { Number = next, Text = text, SectionId = section.Id });

                if (section.Kind == SectionKind.FactualAllegations)
                    lastFact = next;

                next++;
            }
        }

        return numbered;
    }

    public static int CountNumberedParagraphs(CoreDocument doc) => Number(doc).Count;

    public static string BuildCaption(CoreDocument doc)
    {
        // No presiding-officer line or name of any kind (Pitfall 11: avoids naming real people on the bench)
        var sb = new StringBuilder();
        var jurisdiction = doc.Jurisdiction;

        sb.AppendLine(jurisdiction.CourtName.ToUpperInvariant());
        if (jurisdiction.CourtSystem == CourtSystem.State && !string.IsNullOrWhiteSpace(jurisdiction.District))
            sb.AppendLine(jurisdiction.District.ToUpperInvariant());
        sb.AppendLine();

        var plaintiffs = doc.Parties.Where(p => IsPlaintiffSide(p.Role)).ToList();
        var defendants = doc.Parties.Where(p => p.Role is PartyRole.Defendant or PartyRole.ThirdParty or PartyRole.Subject).ToList();

        foreach (var plaintiff in plaintiffs)
            sb.AppendLine($"{plaintiff.Name},");
        sb.AppendLine($"    {PleadingPartyLabel(doc)},");
        sb.AppendLine();

        var caseNumberLabel = doc.DocumentType == CoreDocumentType.Petition ? "Cause No." : "Case No.";
        sb.AppendLine($"v.                                        {caseNumberLabel} ____________");
        sb.AppendLine();

        foreach (var defendant in defendants)
            sb.AppendLine($"{defendant.Name},");
        sb.AppendLine(defendants.Count > 1 ? "    Defendants." : "    Defendant.");
        sb.AppendLine();

        var title = doc.Title.ToUpperInvariant();
        if (doc.DocumentType == CoreDocumentType.Complaint && doc.DemandsJuryTrial && !title.Contains("JURY", StringComparison.Ordinal))
            title += " AND DEMAND FOR JURY TRIAL";
        sb.AppendLine(title);

        return sb.ToString();
    }

    public static string BuildMemoHeader(CoreDocument doc)
    {
        var sb = new StringBuilder();
        var memo = doc.Memo;

        sb.AppendLine(doc.MemoPerspective == MemoPerspective.InternalCorporate
            ? "PRIVILEGED & CONFIDENTIAL — ATTORNEY-CLIENT COMMUNICATION — ATTORNEY WORK PRODUCT"
            : "LAW ENFORCEMENT SENSITIVE");
        sb.AppendLine();
        sb.AppendLine("MEMORANDUM");
        sb.AppendLine();
        sb.AppendLine($"TO: {memo.To}");
        sb.AppendLine($"FROM: {memo.From}");
        if (!string.IsNullOrWhiteSpace(memo.Cc))
            sb.AppendLine($"CC: {memo.Cc}");
        sb.AppendLine($"DATE: {FormatDate(memo.Date)}");
        sb.AppendLine($"RE: {memo.Re}");
        sb.AppendLine(MemoRule);

        return sb.ToString();
    }

    public static string BuildSignatureBlock(CoreDocument doc)
    {
        var sb = new StringBuilder();
        var counsel = doc.Counsel;

        sb.AppendLine($"Dated: {FormatDate(doc.FilingDate)}");
        sb.AppendLine();
        sb.AppendLine("Respectfully submitted,");
        sb.AppendLine();
        sb.AppendLine(counsel.FirmName);
        sb.AppendLine();
        sb.AppendLine($"By: /s/ {counsel.AttorneyName}");
        sb.AppendLine(counsel.AttorneyName);
        if (!string.IsNullOrWhiteSpace(counsel.BarNumber))
            sb.AppendLine($"Bar No. {counsel.BarNumber}");
        sb.AppendLine(counsel.Address);
        sb.AppendLine($"Tel: {counsel.Phone}");
        sb.AppendLine(counsel.Email);
        sb.AppendLine();
        sb.AppendLine($"Attorneys for {PleadingPartyLabel(doc)}");

        return sb.ToString();
    }

    public static string RenderPlainText(CoreDocument doc)
    {
        return CoreDocumentTemplates.IsPleading(doc.DocumentType)
            ? RenderPleading(doc)
            : RenderMemo(doc);
    }

    private static string RenderMemo(CoreDocument doc)
    {
        var sb = new StringBuilder();
        sb.Append(BuildMemoHeader(doc));

        foreach (var section in doc.Sections)
        {
            sb.AppendLine();
            sb.AppendLine(section.Heading);

            foreach (var paragraph in section.Paragraphs.Select(StripLeadingNumber).Where(p => p.Length > 0))
            {
                sb.AppendLine();
                sb.AppendLine(paragraph);
            }
        }

        return sb.ToString();
    }

    private static string RenderPleading(CoreDocument doc)
    {
        var sb = new StringBuilder();
        var isPetition = doc.DocumentType == CoreDocumentType.Petition;
        var label = PleadingPartyLabel(doc);
        var numberedBySection = Number(doc).ToLookup(p => p.SectionId);
        var factsHeaderWritten = false;
        var causesHeaderWritten = false;

        sb.Append(BuildCaption(doc));

        foreach (var section in doc.Sections)
        {
            if (section.Kind == SectionKind.FactualAllegations && !factsHeaderWritten)
            {
                sb.AppendLine();
                sb.AppendLine(isPetition ? "FACTS" : "FACTUAL ALLEGATIONS");
                factsHeaderWritten = true;
            }

            if (section.Kind == SectionKind.Count && isPetition && !causesHeaderWritten)
            {
                sb.AppendLine();
                sb.AppendLine("CAUSES OF ACTION");
                causesHeaderWritten = true;
            }

            sb.AppendLine();
            sb.AppendLine(section.Heading);

            if (section.Kind == SectionKind.PrayerForRelief)
            {
                AppendPrayer(sb, section, label);
                continue;
            }

            foreach (var paragraph in numberedBySection[section.Id])
            {
                sb.AppendLine();
                sb.AppendLine($"{paragraph.Number}. {paragraph.Text}");
            }
        }

        if (doc.DemandsJuryTrial)
        {
            if (doc.DocumentType == CoreDocumentType.Complaint)
            {
                sb.AppendLine();
                sb.AppendLine("DEMAND FOR JURY TRIAL");
                sb.AppendLine();
                sb.AppendLine($"{label} demands a trial by jury on all issues so triable.");
            }
            else
            {
                sb.AppendLine();
                sb.AppendLine("JURY DEMAND");
                sb.AppendLine();
                sb.AppendLine($"{label} demands a jury trial.");
            }
        }

        sb.AppendLine();
        sb.Append(BuildSignatureBlock(doc));

        return sb.ToString();
    }

    private static void AppendPrayer(StringBuilder sb, CoreDocumentSection section, string label)
    {
        sb.AppendLine();
        sb.AppendLine($"WHEREFORE, {label} respectfully requests that the Court enter judgment in its favor and award the following relief:");

        var items = section.Paragraphs.Select(StripLeadingNumber).Where(p => p.Length > 0).ToList();
        for (var i = 0; i < items.Count; i++)
        {
            // Letters A-Z; beyond 26 items fall back to numbers (presets never get close)
            var marker = i < 26 ? $"{(char)('A' + i)}." : $"{i + 1}.";
            sb.AppendLine();
            sb.AppendLine($"{marker} {items[i]}");
        }
    }

    private static bool IsPlaintiffSide(PartyRole role) => role is PartyRole.Plaintiff or PartyRole.Complainant;

    private static string FormatDate(string value) =>
        CaseDraft.TryParseDate(value, out var date)
            ? date.ToString("MMMM d, yyyy", CultureInfo.InvariantCulture)
            : value;
}
