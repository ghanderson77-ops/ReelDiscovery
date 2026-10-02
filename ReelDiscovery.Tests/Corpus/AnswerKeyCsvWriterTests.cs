using System.Text;
using ReelDiscovery.Models.Corpus;
using ReelDiscovery.Services.Corpus;

namespace ReelDiscovery.Tests.Corpus;

public class AnswerKeyCsvWriterTests
{
    private static AnswerKeyRow Row(string docId, string custodian = "Lisa Chen", string subject = "Change Order 7") => new()
    {
        DocId = docId,
        DocType = "Email",
        FilePath = $"Mailboxes/lisa.chen/Sent Items/{docId}.eml",
        FileName = $"{docId}.eml",
        Custodian = custodian,
        AllCustodians = custodian,
        DateSent = "2024-03-18 09:15 -05:00",
        From = custodian,
        To = "Dan Harmon",
        Subject = subject,
        ThreadId = "T-0001",
        FamilyId = docId,
        Relevance = "Relevant",
        IssueCodes = "ISS-01",
        FactIds = "F-001",
        Hot = "N",
        Privileged = "N",
        NearMiss = "N",
        Rationale = "Approves the change order, the approval later denied.",
        Status = "Planned"
    };

    private static string Body(byte[] bytes) => new UTF8Encoding(false).GetString(bytes, 3, bytes.Length - 3);

    [Fact]
    public void Build_StartsWithBom_AndHeaderLine()
    {
        var bytes = AnswerKeyCsvWriter.Build(new[] { Row("BR-000001") });

        Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, bytes.Take(3).ToArray());
        var body = Body(bytes);
        var header = body[..body.IndexOf("\r\n", StringComparison.Ordinal)];
        Assert.Equal(string.Join(",", AnswerKeyColumns.Headers), header);
        Assert.Equal(23, AnswerKeyColumns.Headers.Length);
    }

    [Fact]
    public void Build_RowsCrlfTerminated()
    {
        var body = Body(AnswerKeyCsvWriter.Build(new[] { Row("BR-000001"), Row("BR-000002") }));

        Assert.EndsWith("\r\n", body);
        var lines = body.Split("\r\n");
        Assert.Equal(4, lines.Length); // header, 2 rows, trailing empty
        Assert.Equal(string.Empty, lines[^1]);
        Assert.StartsWith("BR-000001,Email,", lines[1]);
        Assert.StartsWith("BR-000002,Email,", lines[2]);
    }

    [Fact]
    public void Build_EmptyRows_HeaderOnly()
    {
        var body = Body(AnswerKeyCsvWriter.Build(Array.Empty<AnswerKeyRow>()));

        Assert.Equal(string.Join(",", AnswerKeyColumns.Headers) + "\r\n", body);
    }

    [Theory]
    [InlineData("plain", "plain")]
    [InlineData("a,b", "\"a,b\"")]
    [InlineData("say \"hi\"", "\"say \"\"hi\"\"\"")]
    [InlineData("line1\nline2", "\"line1\nline2\"")]
    [InlineData("line1\r\nline2", "\"line1\r\nline2\"")]
    [InlineData("a=b", "a=b")]
    [InlineData("", "")]
    public void Field_Rfc4180Quoting(string input, string expected)
    {
        Assert.Equal(expected, AnswerKeyCsvWriter.Field(input));
    }

    [Fact]
    public void Field_Null_IsEmpty()
    {
        Assert.Equal(string.Empty, AnswerKeyCsvWriter.Field(null));
    }

    [Theory]
    [InlineData("=HYPERLINK(\"x\")", "\"'=HYPERLINK(\"\"x\"\")\"")]
    [InlineData("+1", "\"'+1\"")]
    [InlineData("-1", "\"'-1\"")]
    [InlineData("@SUM", "\"'@SUM\"")]
    [InlineData("\tx", "\"'\tx\"")]
    [InlineData("\rx", "\"'\rx\"")]
    [InlineData("\nx", "\"'\nx\"")]
    [InlineData("＝1", "\"'＝1\"")]
    [InlineData("＋1", "\"'＋1\"")]
    [InlineData("－1", "\"'－1\"")]
    [InlineData("＠SUM", "\"'＠SUM\"")]
    [InlineData("=cmd|' /C calc'!A0", "\"'=cmd|' /C calc'!A0\"")]
    public void Field_FormulaLead_PrefixedAndQuoted(string input, string expected)
    {
        Assert.Equal(expected, AnswerKeyCsvWriter.Field(input));
    }

    [Fact]
    public void Build_GuardsSubjectInRow()
    {
        var body = Body(AnswerKeyCsvWriter.Build(new[] { Row("BR-000001", subject: "=HYPERLINK(\"http://x\")") }));

        Assert.Contains(",\"'=HYPERLINK(\"\"http://x\"\")\",", body);
    }

    [Fact]
    public void Build_AccentedNames_RoundTripUtf8()
    {
        var bytes = AnswerKeyCsvWriter.Build(new[] { Row("BR-000001", custodian: "José Núñez") });

        Assert.Contains("José Núñez", Body(bytes));
        Assert.Contains("José Núñez", new UTF8Encoding(true).GetString(bytes));
    }

    [Fact]
    public void FileName_IsAnswerKeyCsv()
    {
        Assert.Equal("answer-key.csv", AnswerKeyCsvWriter.FileName);
    }
}
