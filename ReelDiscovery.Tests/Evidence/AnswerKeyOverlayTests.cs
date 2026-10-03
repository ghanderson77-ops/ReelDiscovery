using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using ReelDiscovery.Models.Corpus;
using ReelDiscovery.Services.Case;
using ReelDiscovery.Services.Corpus;

namespace ReelDiscovery.Tests.Evidence;

public class AnswerKeyOverlayTests
{
    private const string RenderedSubject = "Privileged & Confidential - Subject T-0003";

    // First planned email that carries an attachment (its .0001 row must follow the overlaid subject)
    private static PlannedMessage EmailWithAttachment(CorpusPlan plan) =>
        plan.Threads.SelectMany(t => t.Messages).OrderBy(m => m.DocId, StringComparer.Ordinal).First(m => m.Attachments.Count > 0);

    private static List<string> Fingerprint(IEnumerable<AnswerKeyRow> rows) =>
        rows.Select(r => string.Join("|", r.DocId, r.DocType, r.FilePath, r.Subject, r.Status, r.Relevance, r.FactIds, r.IssueCodes, r.Rationale)).ToList();

    [Fact]
    public void Build_NoOverlay_AllPlanned()
    {
        var (draft, plan) = EvidenceTestData.ApprovedCase();

        var rows = AnswerKeyBuilder.Build(plan, draft.Guide!);

        Assert.NotEmpty(rows);
        Assert.All(rows, r => Assert.Equal(AnswerKeyBuilder.StatusPlanned, r.Status));
        Assert.Equal(Fingerprint(rows), Fingerprint(AnswerKeyBuilder.Build(plan, draft.Guide!, null)));
        Assert.Equal(Fingerprint(rows), Fingerprint(AnswerKeyBuilder.Build(plan, draft.Guide!, new AnswerKeyOverlay())));
    }

    [Fact]
    public void Build_Overlay_SetsEmailStatusAndSubject_AttachmentCopiesSubject()
    {
        var (draft, plan) = EvidenceTestData.ApprovedCase();
        var target = EmailWithAttachment(plan);
        var baseline = AnswerKeyBuilder.Build(plan, draft.Guide!);
        var overlay = new AnswerKeyOverlay();
        overlay.ByDocId[target.DocId] = new AnswerKeyOverlayEntry { Status = AnswerKeyBuilder.StatusGenerated, Subject = RenderedSubject };

        var rows = AnswerKeyBuilder.Build(plan, draft.Guide!, overlay);

        var email = rows.Single(r => r.DocId == target.DocId);
        Assert.Equal(AnswerKeyBuilder.StatusGenerated, email.Status);
        Assert.Equal(RenderedSubject, email.Subject);

        var attachment = rows.Single(r => r.DocId == target.DocId + ".0001");
        Assert.Equal(AnswerKeyBuilder.DocTypeAttachment, attachment.DocType);
        Assert.Equal(AnswerKeyBuilder.StatusPlanned, attachment.Status);
        Assert.Equal(RenderedSubject, attachment.Subject);

        // Rows outside the overlay (and not attachments of the target) are unchanged
        var untouched = rows.Where(r => r.DocId != target.DocId && r.ParentDocId != target.DocId);
        var expected = baseline.Where(r => r.DocId != target.DocId && r.ParentDocId != target.DocId);
        Assert.Equal(Fingerprint(expected), Fingerprint(untouched));
    }

    [Fact]
    public void Build_OverlayEmptySubject_KeepsComputedSubject()
    {
        var (draft, plan) = EvidenceTestData.ApprovedCase();
        var target = EmailWithAttachment(plan);
        var computed = AnswerKeyBuilder.Build(plan, draft.Guide!).Single(r => r.DocId == target.DocId).Subject;
        var overlay = new AnswerKeyOverlay();
        overlay.ByDocId[target.DocId] = new AnswerKeyOverlayEntry { Status = AnswerKeyBuilder.StatusGeneratedWithWarnings, Subject = "  " };

        var rows = AnswerKeyBuilder.Build(plan, draft.Guide!, overlay);

        var email = rows.Single(r => r.DocId == target.DocId);
        Assert.Equal("Generated - with warnings", email.Status);
        Assert.Equal(computed, email.Subject);
        Assert.Equal(computed, rows.Single(r => r.DocId == target.DocId + ".0001").Subject);
    }

    [Fact]
    public void Build_OverlayBlankStatus_FallsBackToPlanned()
    {
        var (draft, plan) = EvidenceTestData.ApprovedCase();
        var target = EmailWithAttachment(plan);
        var overlay = new AnswerKeyOverlay();
        overlay.ByDocId[target.DocId] = new AnswerKeyOverlayEntry { Status = "", Subject = RenderedSubject };

        var email = AnswerKeyBuilder.Build(plan, draft.Guide!, overlay).Single(r => r.DocId == target.DocId);

        Assert.Equal(AnswerKeyBuilder.StatusPlanned, email.Status);
        Assert.Equal(RenderedSubject, email.Subject);
    }

    [Fact]
    public void StatusConstants_MatchLegendWording()
    {
        Assert.Equal("Planned", AnswerKeyBuilder.StatusPlanned);
        Assert.Equal("Generated", AnswerKeyBuilder.StatusGenerated);
        Assert.Equal("Generated - with warnings", AnswerKeyBuilder.StatusGeneratedWithWarnings);
        Assert.Equal("Failed", AnswerKeyBuilder.StatusFailed);
        Assert.Equal("Skipped", AnswerKeyBuilder.StatusSkipped);
    }

    [Theory]
    [InlineData("$7.5 million", 7_500_000)]
    [InlineData("$250,000", 250_000)]
    public void TryParseAmount_ParsesDollarAmounts(string text, decimal expected)
    {
        Assert.True(CanonicalValueScanner.TryParseAmount(text, out var amount));
        Assert.Equal(expected, amount);
    }

    [Fact]
    public void TryParseAmount_RejectsNonAmounts()
    {
        Assert.False(CanonicalValueScanner.TryParseAmount("abc", out _));
        Assert.False(CanonicalValueScanner.TryParseAmount(null, out _));
    }

    [Fact]
    public void Legend_ExplainsAllStatuses()
    {
        var (draft, plan) = EvidenceTestData.ApprovedCase();
        var bytes = AnswerKeyXlsxWriter.Build(AnswerKeyBuilder.Build(plan, draft.Guide!), draft.Guide!);

        using var doc = SpreadsheetDocument.Open(new MemoryStream(bytes), false);
        var sheet = doc.WorkbookPart!.Workbook!.Sheets!.Elements<Sheet>().Single(s => s.Name == "Legend");
        var worksheet = ((WorksheetPart)doc.WorkbookPart.GetPartById(sheet.Id!)).Worksheet!;
        var texts = worksheet.Descendants<Cell>()
            .Select(c => c.DataType?.Value == CellValues.InlineString ? c.InlineString!.Text!.Text : c.CellValue?.Text ?? string.Empty)
            .ToList();

        Assert.Contains(texts, t => t.Contains("Planned, Generated, Generated - with warnings, Failed or Skipped."));
        Assert.DoesNotContain(texts, t => t.Contains("Planned until the document is generated"));
    }
}
