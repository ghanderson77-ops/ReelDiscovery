using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using MimeKit;
using ReelDiscovery.Helpers;
using ReelDiscovery.Models;
using ReelDiscovery.Models.Corpus;
using ReelDiscovery.Models.Evidence;
using ReelDiscovery.Services;
using ReelDiscovery.Services.Evidence;

namespace ReelDiscovery.Tests.Documents;

public class DocumentRegressionTests
{
    private readonly OfficeDocumentService _office = new();

    [Fact]
    public void Word_OpensAndValidates()
    {
        var bytes = _office.CreateWordDocument("Project Spec", "First paragraph.\n\nSecond paragraph with details.");

        using var doc = WordprocessingDocument.Open(new MemoryStream(bytes), false);
        var errors = new OpenXmlValidator().Validate(doc).ToList();

        // Pinned baseline: the builder emits w:color after w:sz inside w:rPr (schema-order violation that
        // predates the OpenXml 3.5.1 upgrade; Word still opens the file). One error per themed run here.
        const int baselineErrors = 3;
        Assert.True(
            errors.Count == baselineErrors
                && errors.All(e => e.Id == "Sch_UnexpectedElementContentExpectingComplex" && e.Description.Contains(":color")),
            $"Expected {baselineErrors} pinned w:color ordering errors, got {errors.Count}: "
                + string.Join(" || ", errors.Select(e => e.Description + " @ " + e.Path?.XPath)));
    }

    [Fact]
    public void Excel_OpensAndValidates()
    {
        var headers = new List<string> { "Item", "Qty", "Cost" };
        var rows = new List<List<string>>
        {
            new() { "Window unit", "12", "4500" },
            new() { "Sealant", "30", "210.50" }
        };
        var bytes = _office.CreateExcelDocument("Budget", headers, rows);

        using var doc = SpreadsheetDocument.Open(new MemoryStream(bytes), false);
        var errors = new OpenXmlValidator().Validate(doc).ToList();

        Assert.Empty(errors);
    }

    [Fact]
    public void PowerPoint_OpensAndValidates()
    {
        var slides = new List<(string slideTitle, string content)>
        {
            ("Overview", "Scope of work\nSchedule"),
            ("Risks", "Water intrusion\nDelays")
        };
        var bytes = _office.CreatePowerPointDocument("Status Update", slides);

        using var doc = PresentationDocument.Open(new MemoryStream(bytes), false);
        var errors = new OpenXmlValidator().Validate(doc).ToList();

        Assert.Empty(errors);
    }

    [Fact]
    public async Task Eml_ThreadRoundTrip()
    {
        // Case-flow path: composed root + reply written by MailboxWriter, the reply carrying a Word attachment
        var folders = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["CH-001"] = "alice.smith",
            ["CH-002"] = "bob.jones"
        };
        var alice = new EmailAddressPart { Name = "Alice Smith", Email = "alice@example.com" };
        var bob = new EmailAddressPart { Name = "Bob Jones", Email = "bob@example.com" };
        var rootId = ThreadingHelper.GenerateMessageId("example.com").Trim('<', '>');

        var first = new ComposedEmail
        {
            Planned = new PlannedMessage
            {
                Id = "T-0001-M01",
                DocId = "RD-000001",
                Seq = 1,
                FromId = "CH-001",
                ToIds = new List<string> { "CH-002" },
                CustodianIds = new List<string> { "CH-001", "CH-002" },
                SentAt = new DateTimeOffset(2024, 3, 1, 9, 0, 0, TimeSpan.FromHours(-5)),
                FilePath = "Mailboxes/alice.smith/Sent Items/RD-000001.eml"
            },
            ThreadId = "T-0001",
            MessageId = rootId,
            Subject = "Window spec",
            From = alice,
            To = new List<EmailAddressPart> { bob },
            Date = new DateTimeOffset(2024, 3, 1, 9, 0, 0, TimeSpan.FromHours(-5)),
            PlainText = "Please review the window spec.",
            Html = "<html><body><p>Please review the window spec.</p></body></html>"
        };
        var reply = new ComposedEmail
        {
            Planned = new PlannedMessage
            {
                Id = "T-0001-M02",
                DocId = "RD-000002",
                Seq = 2,
                FromId = "CH-002",
                ToIds = new List<string> { "CH-001" },
                CustodianIds = new List<string> { "CH-001", "CH-002" },
                SentAt = new DateTimeOffset(2024, 3, 1, 11, 30, 0, TimeSpan.FromHours(-5)),
                FilePath = "Mailboxes/bob.jones/Sent Items/RD-000002.eml"
            },
            ThreadId = "T-0001",
            MessageId = ThreadingHelper.GenerateMessageId("example.com").Trim('<', '>'),
            InReplyTo = rootId,
            References = new List<string> { rootId },
            Subject = ThreadingHelper.AddReplyPrefix("Window spec"),
            From = bob,
            To = new List<EmailAddressPart> { alice },
            Date = new DateTimeOffset(2024, 3, 1, 11, 30, 0, TimeSpan.FromHours(-5)),
            PlainText = "Attached is the marked-up spec.",
            Html = "<html><body><p>Attached is the marked-up spec.</p></body></html>",
            Attachments = new List<ComposedAttachment>
            {
                new()
                {
                    DocId = "RD-000002.0001",
                    Kind = AttachmentType.Word,
                    FileName = "Spec.docx",
                    Title = "Spec",
                    MimeType = "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
                    Bytes = _office.CreateWordDocument("Spec", "Body")
                }
            }
        };

        var folder = Path.Combine(Path.GetTempPath(), "ReelDiscoveryTests", Guid.NewGuid().ToString("N"));
        try
        {
            var writer = new MailboxWriter(folder, folders);
            await writer.WriteAsync(first);
            var written = await writer.WriteAsync(reply);

            var files = Directory.GetFiles(folder, "*.eml", SearchOption.AllDirectories);
            Assert.Equal(4, files.Length);

            var loaded = MimeMessage.Load(Path.Combine(folder, written[0].Replace('/', Path.DirectorySeparatorChar)));
            Assert.Equal(rootId, loaded.InReplyTo?.Trim('<', '>'));
            Assert.Equal("RE: Window spec", loaded.Subject);

            var wordPart = loaded.Attachments.OfType<MimePart>().SingleOrDefault(p => p.FileName == "Spec.docx");
            Assert.NotNull(wordPart);
            Assert.Equal(
                "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
                wordPart!.ContentType.MimeType);
        }
        finally
        {
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, true);
            }
        }
    }
}
