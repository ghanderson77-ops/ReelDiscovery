using System.Globalization;
using ReelDiscovery.Models.Case;

namespace ReelDiscovery.Services.Case;

/// <summary>
/// Email signatures built by code from the cast fields; the model never writes them (D-16).
/// </summary>
public static class SignatureBuilder
{
    public static string Build(CastMember member, CaseOrganization? org)
    {
        var fullName = member.FullName;
        var firstName = string.IsNullOrWhiteSpace(member.FirstName) ? fullName : member.FirstName.Trim();
        var title = (member.Title ?? string.Empty).Trim();
        var orgName = (org?.Name ?? string.Empty).Trim();

        return member.SignatureStyle switch
        {
            SignatureStyle.Short => JoinLines(fullName, string.Join(", ", new[] { title, orgName }.Where(s => s.Length > 0))),
            SignatureStyle.NameOnly => firstName,
            SignatureStyle.MobileFooter => firstName + "\n\nSent from my mobile",
            _ => JoinLines(fullName, title, orgName, (member.Phone ?? string.Empty).Trim(), (member.Email ?? string.Empty).Trim())
        };
    }

    /// <summary>
    /// Random number in the (555) 555-0100..0199 block.
    /// </summary>
    public static string FictionalPhone() =>
        // Fictional 555-01xx range reserved for fiction; never a real number (Pitfall 12)
        $"(555) 555-01{Random.Shared.Next(0, 100).ToString("D2", CultureInfo.InvariantCulture)}";

    // Blank lines are skipped so missing fields never leave gaps
    private static string JoinLines(params string[] lines) =>
        string.Join("\n", lines.Where(l => !string.IsNullOrWhiteSpace(l)));
}
