using ReelDiscovery.Models.Corpus;
using ReelDiscovery.Models.Evidence;
using ReelDiscovery.Services.Case;

namespace ReelDiscovery.Services.Evidence;

/// <summary>
/// Must-not-mention check for decoy threads (D-23: code scan, no LLM judge). A decoy stays a near miss only while it
/// avoids the case terms the planner listed; a hit becomes a D1 violation fed back on retry like the Phase 5 checks.
/// </summary>
public static class DecoyScanner
{
    public const string Code = "D1";

    /// <summary>
    /// D1 violations for every rendered message body of a decoy thread; empty for any other thread. Never throws.
    /// </summary>
    public static List<RenderViolation> Scan(RenderedThread rendered, PlannedThread thread)
    {
        var result = new List<RenderViolation>();
        if (rendered?.Messages == null || !IsDecoy(thread))
            return result;

        foreach (var message in rendered.Messages)
        {
            if (message == null)
                continue;

            result.AddRange(ScanText(message.Body, message.Seq, thread));
        }

        return result;
    }

    /// <summary>
    /// D1 violations for one piece of text (a body or attachment text) at <paramref name="seq"/>; one per term.
    /// Amount terms match any scanned amount of the same value in any format; other terms match whole words.
    /// </summary>
    public static List<RenderViolation> ScanText(string? text, int seq, PlannedThread thread)
    {
        var result = new List<RenderViolation>();
        if (string.IsNullOrEmpty(text) || !IsDecoy(thread) || thread.MustNotMention == null)
            return result;

        List<ScannedValue>? amounts = null;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in thread.MustNotMention)
        {
            var term = raw?.Trim() ?? string.Empty;
            if (term.Length == 0 || !seen.Add(term))
                continue;

            try
            {
                bool hit;
                if (CanonicalValueScanner.TryParseAmount(term, out _))
                {
                    amounts ??= CanonicalValueScanner.Scan(text).Where(v => v.Kind == ScannedKind.Amount).ToList();
                    hit = amounts.Any(a => CanonicalValueScanner.AmountsEqual(a.Text, term));
                }
                else
                {
                    hit = RenderValidator.ContainsWord(text, term);
                }

                if (hit)
                    result.Add(new RenderViolation { Code = Code, Seq = seq, Text = $"mentions \"{term}\", which this email must not mention" });
            }
            catch (Exception)
            {
                // A broken term must not hide the others or throw into the run (Pitfall 5 pattern)
            }
        }

        return result;
    }

    private static bool IsDecoy(PlannedThread? thread) => thread != null && thread.Category == NonRelevantCategory.Decoy;
}
