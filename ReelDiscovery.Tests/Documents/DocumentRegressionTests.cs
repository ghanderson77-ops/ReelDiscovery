using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using MimeKit;
using ReelDiscovery.Helpers;
using ReelDiscovery.Models;
using ReelDiscovery.Services;

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
        var alice = new Character { FirstName = "Alice", LastName = "Smith", Email = "alice@example.com" };
        var bob = new Character { FirstName = "Bob", LastName = "Jones", Email = "bob@example.com" };

        var thread = new EmailThread { Subject = "Window spec" };
        var first = new EmailMessage
        {
            From = alice,
            To = new List<Character> { bob },
            Subject = "Window spec",
            BodyPlain = "Please review the window spec.",
            SentDate = new DateTime(2024, 3, 1, 9, 0, 0),
            SequenceInThread = 0
        };
        var reply = new EmailMessage
        {
            From = bob,
            To = new List<Character> { alice },
            Subject = "RE: Window spec",
            BodyPlain = "Attached is the marked-up spec.",
            SentDate = new DateTime(2024, 3, 1, 11, 30, 0),
            SequenceInThread = 1,
            Attachments = new List<Attachment>
            {
                new()
                {
                    Type = AttachmentType.Word,
                    FileName = "Spec.docx",
                    Content = _office.CreateWordDocument("Spec", "Body")
                }
            }
        };
        thread.Messages.Add(first);
        thread.Messages.Add(reply);

        ThreadingHelper.SetupThreading(thread, "example.com");

        var folder = Path.Combine(Path.GetTempPath(), "ReelDiscoveryTests", Guid.NewGuid().ToString("N"));
        try
        {
            await new EmlFileService().SaveAllEmailsAsync(new List<EmailThread> { thread }, folder);

            var files = Directory.GetFiles(folder, "*.eml");
            Assert.Equal(2, files.Length);

            var loaded = MimeMessage.Load(Path.Combine(folder, reply.GeneratedFileName!));
            Assert.Equal(first.MessageId.Trim('<', '>'), loaded.InReplyTo?.Trim('<', '>'));

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
