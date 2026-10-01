using ReelDiscovery.Services.Case;

namespace ReelDiscovery.Tests.Case;

public class CitationScannerTests
{
    private static readonly string[] OwnParties = { "Brightline Glazing, LLC", "Harmon Builders, Inc." };

    [Theory]
    [InlineData("531 U.S. 98 (2000)")]
    [InlineData("123 F.3d 456")]
    [InlineData("45 Cal. Rptr. 3d 12")]
    [InlineData("88 F. Supp. 2d 101")]
    [InlineData("210 So. 3d 44")]
    [InlineData("17 N.E.3d 900")]
    [InlineData("512 S.W.3d 1")]
    public void Reporter_IsError(string text)
    {
        var finding = Assert.Single(CitationScanner.Scan(text, OwnParties));
        Assert.Equal(CitationSeverity.Error, finding.Severity);
        Assert.Equal("Reporter", finding.Kind);
    }

    [Theory]
    [InlineData("2019 WL 1234567")]
    [InlineData("2020 U.S. Dist. LEXIS 12345")]
    public void Database_IsError(string text)
    {
        var finding = Assert.Single(CitationScanner.Scan(text, OwnParties));
        Assert.Equal(CitationSeverity.Error, finding.Severity);
        Assert.Equal("Database", finding.Kind);
    }

    [Fact]
    public void CaseName_IsWarning()
    {
        var finding = Assert.Single(CitationScanner.Scan("as held in Smith v. Jones, the duty", OwnParties));
        Assert.Equal(CitationSeverity.Warning, finding.Severity);
        Assert.Equal("CaseName", finding.Kind);
    }

    [Theory]
    [InlineData("Id. at 12")]
    [InlineData("supra")]
    [InlineData("See, e.g., the regulations")]
    public void Signal_IsWarning(string text)
    {
        var finding = Assert.Single(CitationScanner.Scan(text, OwnParties));
        Assert.Equal(CitationSeverity.Warning, finding.Severity);
        Assert.Equal("Signal", finding.Kind);
    }

    [Theory]
    [InlineData("28 U.S.C. § 1332")]
    [InlineData("28 U.S.C. § 1391(b)")]
    [InlineData("Fed. R. Civ. P. 38")]
    [InlineData("Fla. Stat. § 553.84")]
    [InlineData("18 U.S.C. § 1956")]
    [InlineData("15 U.S.C. § 78j(b)")]
    [InlineData("17 C.F.R. § 240.10b-5")]
    [InlineData("Brightline Glazing, LLC v. Harmon Builders, Inc.")]
    [InlineData("The parties met on March 3, 2024 at 10 a.m.")]
    [InlineData("")]
    [InlineData("   ")]
    public void NegativeCorpus_NoFindings(string text)
    {
        Assert.Empty(CitationScanner.Scan(text, OwnParties));
    }

    [Theory]
    [InlineData("Brightline v. Harmon")]
    [InlineData("Brightline Glazing v. Harmon Builders")]
    public void OwnCaption_ShortOrFullNames_NoFindings(string text)
    {
        var parties = new[] { "Brightline Glazing, LLC", "Brightline", "Harmon Builders, Inc.", "Harmon" };

        Assert.Empty(CitationScanner.Scan(text, parties));
    }

    [Theory]
    [InlineData("as in Grace v. Pace, the duty")]
    [InlineData("as in Goldberg v. Pace, the duty")]
    public void ShortPartyNameInsideOtherWord_IsNotOwnCaption(string text)
    {
        // Short names "Ace" and "Gold" must not hide a real case name by substring match (WR-10)
        var parties = new[] { "Ace Glass, LLC", "Ace", "Gold Coast Builders, Inc.", "Gold" };

        var finding = Assert.Single(CitationScanner.Scan(text, parties));
        Assert.Equal("CaseName", finding.Kind);
        Assert.Equal(CitationSeverity.Warning, finding.Severity);
    }

    [Fact]
    public void Findings_OrderedByIndex()
    {
        var findings = CitationScanner.Scan("See Smith v. Jones, 123 F.3d 456; also 2019 WL 1234567.", OwnParties);
        Assert.Equal(new[] { "CaseName", "Reporter", "Database" }, findings.Select(f => f.Kind).ToArray());
        Assert.True(findings.Zip(findings.Skip(1)).All(p => p.First.Index <= p.Second.Index));
    }

    [Fact]
    public void HasErrors_TrueOnlyWithError()
    {
        Assert.True(CitationScanner.HasErrors(CitationScanner.Scan("531 U.S. 98", OwnParties)));
        Assert.False(CitationScanner.HasErrors(CitationScanner.Scan("Smith v. Jones and supra", OwnParties)));
        Assert.False(CitationScanner.HasErrors(CitationScanner.Scan("28 U.S.C. § 1332", OwnParties)));
    }

    [Theory]
    [InlineData("ReporterCitation")]
    [InlineData("DatabaseCitation")]
    [InlineData("CaseName")]
    [InlineData("CitationSignal")]
    public void ScanningPatterns_HaveMatchTimeout(string patternName)
    {
        var method = typeof(CitationScanner).GetMethod(patternName,
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        Assert.NotNull(method);

        var regex = (System.Text.RegularExpressions.Regex)method!.Invoke(null, null)!;

        Assert.Equal(TimeSpan.FromMilliseconds(CitationScanner.MatchTimeoutMs), regex.MatchTimeout);
    }
}
