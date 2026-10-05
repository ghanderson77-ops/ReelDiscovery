using ReelDiscovery.Models;
using ReelDiscovery.Models.Corpus;
using ReelDiscovery.Models.Evidence;
using ReelDiscovery.Services.Evidence;
using Xunit;

namespace ReelDiscovery.Tests.Evidence;

public class AttachmentHarvesterTests : IDisposable
{
    private readonly string _folder;

    private static readonly IReadOnlyDictionary<string, string> Folders = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["CH-001"] = "pat.lee",
        ["CH-005"] = "sam.ortiz",
        ["CH-007"] = "dana.cho"
    };

    public AttachmentHarvesterTests()
    {
        _folder = Path.Combine(Path.GetTempPath(), "rd-harvest-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_folder);
    }

    public void Dispose()
    {
        AttachmentHarvester.EmlCapOverride = 0;
        AttachmentHarvester.PartCapOverride = 0;
        AttachmentHarvester.TotalCapOverride = 0;
        if (Directory.Exists(_folder))
            Directory.Delete(_folder, recursive: true);
    }

    private static PlannedMessage Planned(string docId) => new()
    {
        Id = "T-0003-M02",
        DocId = docId,
        Seq = 2,
        FromId = "CH-001",
        ToIds = new List<string> { "CH-005" },
        CcIds = new List<string> { "CH-007" },
        CustodianIds = new List<string> { "CH-001", "CH-005", "CH-007" },
        SentAt = new DateTimeOffset(2024, 3, 18, 9, 41, 0, TimeSpan.FromHours(-7)),
        FilePath = $"Mailboxes/pat.lee/Sent Items/{docId}.eml"
    };

    private static ComposedEmail Email(string docId, params ComposedAttachment[] attachments) => new()
    {
        Planned = Planned(docId),
        ThreadId = "T-0003",
        MessageId = $"abc123.{docId}@northwind.example",
        Subject = "Window delivery schedule",
        From = new EmailAddressPart { Name = "Pat Lee", Email = "pat.lee@northwind.example" },
        To = new List<EmailAddressPart> { new() { Name = "Sam Ortiz", Email = "sam.ortiz@northwind.example" } },
        Cc = new List<EmailAddressPart> { new() { Name = "Dana Cho", Email = "dana.cho@northwind.example" } },
        Date = new DateTimeOffset(2024, 3, 18, 9, 41, 0, TimeSpan.FromHours(-7)),
        PlainText = "See attached.\n\nThanks,\nPat",
        Html = $"<html><body><p>See attached.</p><img src=\"cid:img.{docId}@rd\"></body></html>",
        Attachments = attachments.ToList()
    };

    private static ComposedAttachment Part(string docId, AttachmentType kind, string fileName, int size = 0, bool inline = false)
    {
        var bytes = size > 0
            ? Enumerable.Range(0, size).Select(i => (byte)(i % 251)).ToArray()
            : System.Text.Encoding.UTF8.GetBytes($"content of {docId} {fileName} éü");
        return new ComposedAttachment
        {
            DocId = docId,
            Kind = kind,
            FileName = fileName,
            Title = fileName,
            Bytes = bytes,
            IsInline = inline,
            ContentId = inline ? $"img.{docId}@rd" : string.Empty
        };
    }

    private static ComposedAttachment Word(string docId = "PL-000043") => Part(docId, AttachmentType.Word, "Schedule.docx");
    private static ComposedAttachment Excel(string docId = "PL-000044") => Part(docId, AttachmentType.Excel, "Costs.xlsx");

    private string Full(string relative) => Path.Combine(_folder, relative.Replace('/', Path.DirectorySeparatorChar));

    private async Task<List<string>> Write(ComposedEmail email) => await new MailboxWriter(_folder, Folders).WriteAsync(email);

    [Fact]
    public async Task Harvest_ThreeParts_BytesAndContentId()
    {
        var word = Word();
        var image = Part("PL-000045", AttachmentType.Image, "photo.png", inline: true);
        var invite = Part("PL-000046", AttachmentType.CalendarInvite, "Site walk.ics");
        var email = Email("PL-000042", word, image, invite);
        var copies = await Write(email);

        var harvested = AttachmentHarvester.HarvestMessage(_folder, copies, new[] { "PL-000043", "PL-000045", "PL-000046" });

        Assert.Equal(3, harvested.Count);
        Assert.Equal(word.Bytes, harvested["PL-000043"].Bytes);
        Assert.Equal(image.Bytes, harvested["PL-000045"].Bytes);
        Assert.Equal(invite.Bytes, harvested["PL-000046"].Bytes);
        Assert.Equal("img.PL-000045@rd", harvested["PL-000045"].ContentId);
        Assert.Equal(string.Empty, harvested["PL-000043"].ContentId);
        Assert.Equal(string.Empty, harvested["PL-000046"].ContentId);
    }

    [Fact]
    public async Task Harvest_RebuildFromHarvested_ByteIdenticalFiles()
    {
        var originals = new[]
        {
            Word(),
            Part("PL-000045", AttachmentType.Image, "photo.png", inline: true),
            Part("PL-000046", AttachmentType.CalendarInvite, "Site walk.ics")
        };
        var copies = await Write(Email("PL-000042", originals));
        var before = copies.Select(c => File.ReadAllBytes(Full(c))).ToList();

        var harvested = AttachmentHarvester.HarvestMessage(_folder, copies, originals.Select(a => a.DocId).ToList());
        var rebuilt = originals.Select(a => new ComposedAttachment
        {
            DocId = a.DocId,
            Kind = a.Kind,
            FileName = a.FileName,
            Title = a.Title,
            IsInline = a.IsInline,
            Bytes = harvested[a.DocId].Bytes,
            ContentId = harvested[a.DocId].ContentId
        }).ToArray();
        await Write(Email("PL-000042", rebuilt));

        for (var i = 0; i < copies.Count; i++)
            Assert.Equal(before[i], File.ReadAllBytes(Full(copies[i])));
    }

    [Fact]
    public async Task Harvest_MultiMessage_EachMessageGetsItsOwnPart()
    {
        var word = Word("PL-000043");
        var excel = Excel("PL-000051");
        var copies1 = await Write(Email("PL-000042", word));
        var copies2 = await Write(Email("PL-000050", excel));

        var first = AttachmentHarvester.HarvestMessage(_folder, copies1, new[] { "PL-000043" });
        var second = AttachmentHarvester.HarvestMessage(_folder, copies2, new[] { "PL-000051" });

        Assert.Equal(word.Bytes, Assert.Single(first).Value.Bytes);
        Assert.Equal(excel.Bytes, Assert.Single(second).Value.Bytes);
        Assert.Equal("PL-000043", first.Keys.Single());
        Assert.Equal("PL-000051", second.Keys.Single());
    }

    [Fact]
    public async Task Harvest_AlternateCopy_IncompleteFirstCopyDoesNotHideComplete()
    {
        var word = Word();
        var excel = Excel();
        var copies = await Write(Email("PL-000042", word, excel));

        // Copy A (the sender copy, harvested first) is readable but was written without the Excel part
        using (var ms = new MemoryStream())
        {
            MailboxWriter.BuildMime(Email("PL-000042", Word())).WriteTo(ms);
            File.WriteAllBytes(Full(copies[0]), ms.ToArray());
        }

        var harvested = AttachmentHarvester.HarvestMessage(_folder, copies, new[] { "PL-000043", "PL-000044" });

        Assert.Equal(2, harvested.Count);
        Assert.Equal(word.Bytes, harvested["PL-000043"].Bytes);
        Assert.Equal(excel.Bytes, harvested["PL-000044"].Bytes);
    }

    [Fact]
    public async Task Harvest_AlternateCopy_MissingFirstCopyUsesNext()
    {
        var excel = Excel();
        var copies = await Write(Email("PL-000042", excel));
        File.Delete(Full(copies[0]));

        var harvested = AttachmentHarvester.HarvestMessage(_folder, copies, new[] { "PL-000044" });

        Assert.Equal(excel.Bytes, harvested["PL-000044"].Bytes);
    }

    [Fact]
    public async Task Harvest_ExpectedNotFound_Absent()
    {
        var copies = await Write(Email("PL-000042", Word()));

        var harvested = AttachmentHarvester.HarvestMessage(_folder, copies, new[] { "PL-000043", "PL-000099" });

        Assert.Equal(new[] { "PL-000043" }, harvested.Keys.ToArray());
    }

    [Fact]
    public async Task Harvest_UnexpectedDocId_Ignored()
    {
        var copies = await Write(Email("PL-000042", Word(), Excel()));

        var harvested = AttachmentHarvester.HarvestMessage(_folder, copies, new[] { "PL-000044" });

        Assert.Equal(new[] { "PL-000044" }, harvested.Keys.ToArray());
    }

    [Fact]
    public async Task Harvest_UnsafePaths_Ignored()
    {
        var copies = await Write(Email("PL-000042", Word()));
        var outside = Path.Combine(Path.GetDirectoryName(_folder)!, Path.GetFileName(_folder) + "-outside.eml");
        File.Copy(Full(copies[0]), outside, overwrite: true);
        try
        {
            var harvested = AttachmentHarvester.HarvestMessage(_folder,
                new[] { "..\\" + Path.GetFileName(outside), "../" + Path.GetFileName(outside), outside, "Mailboxes/../../x/y.eml" },
                new[] { "PL-000043" });

            Assert.Empty(harvested);
        }
        finally
        {
            File.Delete(outside);
        }
    }

    // A cap set by one test must not reach tests running in parallel: a leaked cap made AttachmentRepairTests
    // re-create a committed Word part, so cancel and fatal landed on the wrong attachment
    [Fact]
    public async Task CapOverrides_SetInAnotherFlow_NotVisibleHere()
    {
        await Task.Run(() =>
        {
            AttachmentHarvester.EmlCapOverride = 100;
            AttachmentHarvester.PartCapOverride = 100;
            AttachmentHarvester.TotalCapOverride = 100;
            GenerationJournal.SizeCapOverride = 100;
            Services.Corpus.CorpusPlanStore.SizeCapOverride = 100;
        });

        Assert.Equal(0, AttachmentHarvester.EmlCapOverride);
        Assert.Equal(0, AttachmentHarvester.PartCapOverride);
        Assert.Equal(0, AttachmentHarvester.TotalCapOverride);
        Assert.Equal(0, GenerationJournal.SizeCapOverride);
        Assert.Equal(0, Services.Corpus.CorpusPlanStore.SizeCapOverride);
    }

    [Fact]
    public async Task Harvest_OversizedEml_SkippedBeforeLoad()
    {
        var copies = await Write(Email("PL-000042", Word()));
        AttachmentHarvester.EmlCapOverride = 100;

        Assert.Empty(AttachmentHarvester.HarvestMessage(_folder, copies, new[] { "PL-000043" }));
    }

    [Fact]
    public async Task Harvest_OversizedPart_Skipped()
    {
        var small = Part("PL-000043", AttachmentType.Word, "Small.docx", size: 50);
        var big = Part("PL-000044", AttachmentType.Excel, "Big.xlsx", size: 500);
        var copies = await Write(Email("PL-000042", small, big));
        AttachmentHarvester.PartCapOverride = 100;

        var harvested = AttachmentHarvester.HarvestMessage(_folder, copies, new[] { "PL-000043", "PL-000044" });

        Assert.Equal(new[] { "PL-000043" }, harvested.Keys.ToArray());
        Assert.Equal(small.Bytes, harvested["PL-000043"].Bytes);
    }

    [Fact]
    public async Task Harvest_TotalCap_StopsWithWhatItHas()
    {
        var parts = new[]
        {
            Part("PL-000043", AttachmentType.Word, "One.docx", size: 200),
            Part("PL-000044", AttachmentType.Excel, "Two.xlsx", size: 200),
            Part("PL-000045", AttachmentType.PowerPoint, "Three.pptx", size: 200)
        };
        var copies = await Write(Email("PL-000042", parts));
        AttachmentHarvester.TotalCapOverride = 400;

        var harvested = AttachmentHarvester.HarvestMessage(_folder, copies, parts.Select(p => p.DocId).ToList());

        Assert.Equal(new[] { "PL-000043", "PL-000044" }, harvested.Keys.OrderBy(k => k, StringComparer.Ordinal).ToArray());
    }

    [Fact]
    public async Task Harvest_TotalCap_PartPastRemainingBudget_Stops()
    {
        var parts = new[]
        {
            Part("PL-000043", AttachmentType.Word, "One.docx", size: 200),
            Part("PL-000044", AttachmentType.Excel, "Two.xlsx", size: 200)
        };
        var copies = await Write(Email("PL-000042", parts));
        AttachmentHarvester.TotalCapOverride = 300;

        var harvested = AttachmentHarvester.HarvestMessage(_folder, copies, parts.Select(p => p.DocId).ToList());

        Assert.Equal(new[] { "PL-000043" }, harvested.Keys.ToArray());
    }

    [Fact]
    public async Task Harvest_CorruptEml_ContributesNothing_NoException()
    {
        var excel = Part("PL-000044", AttachmentType.Excel, "Costs.xlsx", size: 3000);
        var copies = await Write(Email("PL-000042", excel));
        File.WriteAllBytes(Full(copies[0]), new byte[] { 0, 1, 2, 0xFF, 0xFE, 13, 10, 45, 45 });

        // Copy B is cut in the middle of the Excel part's base64 body: it must not supply partial bytes
        var full = File.ReadAllBytes(Full(copies[1]));
        var text = System.Text.Encoding.ASCII.GetString(full);
        var cut = text.IndexOf("Costs.xlsx", StringComparison.Ordinal) + 1500;
        File.WriteAllBytes(Full(copies[1]), full[..cut]);

        var harvested = AttachmentHarvester.HarvestMessage(_folder, copies, new[] { "PL-000044" });

        // The third copy is intact and supplies the part
        Assert.Equal(excel.Bytes, harvested["PL-000044"].Bytes);
        Assert.Empty(AttachmentHarvester.HarvestMessage(_folder, copies.Take(2), new[] { "PL-000044" }));
    }

    [Fact]
    public void Harvest_EmptyInputs_Empty()
    {
        Assert.Empty(AttachmentHarvester.HarvestMessage(_folder, Array.Empty<string>(), new[] { "PL-000043" }));
        Assert.Empty(AttachmentHarvester.HarvestMessage(_folder, new[] { "Mailboxes/a/Inbox/x.eml" }, Array.Empty<string>()));
    }
}
