using System.Text;
using ReelDiscovery.Models.Case;

namespace ReelDiscovery.Services.Case;

/// <summary>
/// Readable, Windows-safe per-case folder names (D-09), confined to the Cases root.
/// </summary>
public static class CaseFolderNaming
{
    public const int MaxLength = 80;
    public const string Untitled = "Untitled Case";

    private static readonly HashSet<string> ReservedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
    };

    private static readonly HashSet<char> InvalidChars = new(Path.GetInvalidFileNameChars());

    /// <summary>
    /// Human-readable case title: "Plaintiff v. Defendant" for pleadings, "{Organization} - {Re}" for memos.
    /// </summary>
    public static string CaseTitle(CaseDraft draft)
    {
        var doc = draft.CoreDocument;
        if (doc == null)
            return Untitled;

        if (doc.DocumentType == CoreDocumentType.InvestigationMemo)
        {
            var org = doc.Memo?.Organization?.Trim();
            if (string.IsNullOrEmpty(org))
                org = doc.Parties?.FirstOrDefault(p => !string.IsNullOrWhiteSpace(p?.Name))?.Name.Trim();
            var re = doc.Memo?.Re?.Trim();

            if (!string.IsNullOrEmpty(org) && !string.IsNullOrEmpty(re))
                return $"{org} - {re}";
            if (!string.IsNullOrEmpty(re))
                return re;
            if (!string.IsNullOrEmpty(org))
                return org;
        }
        else
        {
            var (first, second) = PleadingParties(doc);
            if (first != null && second != null)
                return $"{first.Name.Trim()} v. {second.Name.Trim()}";
        }

        return string.IsNullOrWhiteSpace(doc.Title) ? Untitled : doc.Title.Trim();
    }

    /// <summary>
    /// Default folder name: short party names for pleadings ("Brightline v. Harmon"), the case title for memos; sanitized.
    /// </summary>
    public static string DefaultFolderName(CaseDraft draft)
    {
        var doc = draft.CoreDocument;
        if (doc != null && doc.DocumentType != CoreDocumentType.InvestigationMemo)
        {
            var (first, second) = PleadingParties(doc);
            if (first != null && second != null)
                return Sanitize($"{ShortOrName(first)} v. {ShortOrName(second)}");
        }

        return Sanitize(CaseTitle(draft));
    }

    public static string Sanitize(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return Untitled;

        var builder = new StringBuilder(name.Length);
        var lastWasSpace = false;
        foreach (var c in name)
        {
            if (InvalidChars.Contains(c))
                continue;

            if (char.IsWhiteSpace(c))
            {
                if (!lastWasSpace)
                    builder.Append(' ');
                lastWasSpace = true;
                continue;
            }

            builder.Append(c);
            lastWasSpace = false;
        }

        // Leading dots would hide the folder or form "..": strip them; Windows silently strips trailing dots/spaces
        var result = builder.ToString().TrimStart('.', ' ').TrimEnd('.', ' ');

        var dot = result.IndexOf('.');
        var baseName = (dot >= 0 ? result[..dot] : result).TrimEnd(' ');
        if (ReservedNames.Contains(baseName))
            result += " Case";

        if (result.Length > MaxLength)
            result = result[..MaxLength].TrimEnd('.', ' ');

        return result.Length == 0 ? Untitled : result;
    }

    /// <summary>
    /// root\name, or root\name (2), root\name (3)... when taken. Throws InvalidOperationException if the path escapes root.
    /// </summary>
    public static string UniqueFolderPath(string root, string name)
    {
        var fullRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        var basePath = Path.GetFullPath(Path.Combine(fullRoot, name));
        EnsureInsideRoot(fullRoot, basePath);

        var candidate = basePath;
        for (var suffix = 2; Directory.Exists(candidate) || File.Exists(candidate); suffix++)
            candidate = $"{basePath} ({suffix})";

        EnsureInsideRoot(fullRoot, candidate);
        return candidate;
    }

    private static void EnsureInsideRoot(string fullRoot, string candidate)
    {
        // LLM-derived titles must never escape the Cases root (ASVS V12)
        var parent = Path.GetDirectoryName(candidate);
        if (parent == null
            || !string.Equals(Path.TrimEndingDirectorySeparator(parent), fullRoot, StringComparison.OrdinalIgnoreCase)
            || Path.GetFileName(candidate).Length == 0)
        {
            throw new InvalidOperationException($"The case folder name must stay inside {fullRoot}.");
        }
    }

    private static (CaseParty? First, CaseParty? Second) PleadingParties(CoreDocument doc)
    {
        var parties = doc.Parties?.Where(p => p != null && !string.IsNullOrWhiteSpace(p.Name)).ToList() ?? new List<CaseParty>();
        var first = parties.FirstOrDefault(p => p.Role == PartyRole.Plaintiff)
            ?? parties.FirstOrDefault(p => p.Role == PartyRole.Complainant);
        var second = parties.FirstOrDefault(p => p.Role == PartyRole.Defendant)
            ?? parties.FirstOrDefault(p => p.Role == PartyRole.Subject);
        return (first, second);
    }

    private static string ShortOrName(CaseParty party) =>
        string.IsNullOrWhiteSpace(party.ShortName) ? party.Name.Trim() : party.ShortName.Trim();
}
