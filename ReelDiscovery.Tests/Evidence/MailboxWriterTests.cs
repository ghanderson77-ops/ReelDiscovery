using MimeKit;
using ReelDiscovery.Models;
using ReelDiscovery.Models.Corpus;
using ReelDiscovery.Models.Evidence;
using ReelDiscovery.Services.Corpus;
using ReelDiscovery.Services.Evidence;
using ReelDiscovery.Tests.Evidence.Golden;
using Xunit;

namespace ReelDiscovery.Tests.Evidence;

public class MailboxWriterTests : IDisposable
{
    private readonly string _folder;

    private static readonly IReadOnlyDictionary<string, string> Folders = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["CH-001"] = "pat.lee",
        ["CH-002"] = "kim.park",
        ["CH-005"] = "sam.ortiz",
        ["CH-007"] = "dana.cho"
    };

    public MailboxWriterTests()
    {
        _folder = Path.Combine(Path.GetTempPath(), "rd-mail-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_folder);
    }

    public void Dispose()
    {
        if (Directory.Exists(_folder))
            Directory.Delete(_folder, recursive: true);
    }

    private static PlannedMessage InternalMessage() => new()
    {
        Id = "T-0003-M02",
        DocId = "PL-000042",
        Seq = 2,
        FromId = "CH-001",
        ToIds = new List<string> { "CH-005" },
        CcIds = new List<string> { "CH-007" },
        CustodianIds = new List<string> { "CH-001", "CH-005", "CH-007" },
        SentAt = new DateTimeOffset(2024, 3, 18, 9, 41, 0, TimeSpan.FromHours(-7)),
        TimeZoneId = "America/Phoenix",
        FilePath = "Mailboxes/pat.lee/Sent Items/PL-000042.eml"
    };

    private static ComposedEmail Composed() => new()
    {
        Planned = InternalMessage(),
        ThreadId = "T-0003",
        MessageId = "abc123.PL-000042@northwind.example",
        InReplyTo = "root01.PL-000040@northwind.example",
        References = new List<string> { "root01.PL-000040@northwind.example" },
        Subject = "RE: Window delivery schedule",
        From = new EmailAddressPart { Name = "Pat Lee", Email = "pat.lee@northwind.example" },
        To = new List<EmailAddressPart> { new() { Name = "Sam Ortiz", Email = "sam.ortiz@northwind.example" } },
        Cc = new List<EmailAddressPart> { new() { Name = "Dana Cho", Email = "dana.cho@contoso.example" } },
        Date = new DateTimeOffset(2024, 3, 18, 9, 41, 0, TimeSpan.FromHours(-7)),
        PlainText = "Please review the updated schedule.\n\nThanks,\nPat",
        Html = "<html><body><p>Please review the updated schedule.</p><p>Thanks,<br>Pat</p></body></html>"
    };

    [Fact]
    public void CopyPathsFor_InternalMessage_SenderThenRecipients()
    {
        var paths = MailboxWriter.CopyPathsFor(InternalMessage(), Folders);

        Assert.Equal(new[]
        {
            "Mailboxes/pat.lee/Sent Items/PL-000042.eml",
            "Mailboxes/sam.ortiz/Inbox/PL-000042.eml",
            "Mailboxes/dana.cho/Inbox/PL-000042.eml"
        }, paths);
        Assert.Contains(InternalMessage().FilePath, paths);
    }

    [Fact]
    public void CopyPathsFor_ExternalSender_OnlyInboxCopy()
    {
        var message = new PlannedMessage
        {
            DocId = "PL-000050",
            FromId = "EXT-002",
            ToIds = new List<string> { "CH-002" },
            CustodianIds = new List<string> { "CH-002" },
            FilePath = "Mailboxes/kim.park/Inbox/PL-000050.eml"
        };

        var paths = MailboxWriter.CopyPathsFor(message, Folders);

        Assert.Equal(new[] { "Mailboxes/kim.park/Inbox/PL-000050.eml" }, paths);
    }

    [Fact]
    public void CopyPathsFor_NoCastParticipant_KeepsPlannedPath()
    {
        var message = new PlannedMessage
        {
            DocId = "PL-000051",
            FromId = "EXT-001",
            ToIds = new List<string> { "EXT-003" },
            FilePath = "Mailboxes/Unassigned/Inbox/PL-000051.eml"
        };

        Assert.Equal(new[] { "Mailboxes/Unassigned/Inbox/PL-000051.eml" }, MailboxWriter.CopyPathsFor(message, Folders));
    }

    [Fact]
    public void CopyPathsFor_UnsafePaths_NeverReturned()
    {
        var message = InternalMessage();
        message.FilePath = "../evil.eml";
        var folders = new Dictionary<string, string>(Folders, StringComparer.Ordinal) { ["CH-005"] = "..\\..\\escape" };

        var paths = MailboxWriter.CopyPathsFor(message, folders);

        Assert.DoesNotContain("../evil.eml", paths);
        Assert.All(paths, p => Assert.True(CorpusPlan.IsSafeEmlPath(p)));
        Assert.Equal(new[]
        {
            "Mailboxes/pat.lee/Sent Items/PL-000042.eml",
            "Mailboxes/dana.cho/Inbox/PL-000042.eml"
        }, paths);
    }

    [Fact]
    public void ResolveUnderCase_SafePath_UnderCase()
    {
        var full = MailboxWriter.ResolveUnderCase(_folder, "Mailboxes/a/Inbox/x.eml");

        Assert.NotNull(full);
        Assert.StartsWith(Path.GetFullPath(_folder).TrimEnd('\\') + "\\", full, StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith(Path.Combine("Mailboxes", "a", "Inbox", "x.eml"), full);
    }

    [Theory]
    [InlineData("Mailboxes/../../x.eml")]
    [InlineData("Mailboxes/..\\..\\x/Inbox/x.eml")]
    [InlineData("C:/Windows/x/y.eml")]
    [InlineData("../Mailboxes/a/Inbox/x.eml")]
    [InlineData("")]
    public void ResolveUnderCase_Outside_Null(string path)
    {
        Assert.Null(MailboxWriter.ResolveUnderCase(_folder, path));
    }

    [Fact]
    public void ResolveUnderCase_TrailingSeparatorOnCase_StillResolves()
    {
        Assert.NotNull(MailboxWriter.ResolveUnderCase(_folder + "\\", "Mailboxes/a/Inbox/x.eml"));
    }

    [Fact]
    public void BuildMime_RoundTrip_HeadersAndAlternativeBody()
    {
        var email = Composed();
        using var ms = new MemoryStream();
        MailboxWriter.BuildMime(email).WriteTo(ms);
        ms.Position = 0;

        var loaded = MimeMessage.Load(ms);

        var alternative = Assert.IsType<MultipartAlternative>(loaded.Body);
        Assert.Equal(2, alternative.Count);
        Assert.Equal("text/plain", alternative[0].ContentType.MimeType);
        Assert.Equal("text/html", alternative[1].ContentType.MimeType);
        Assert.Equal(email.PlainText, loaded.TextBody!.Replace("\r\n", "\n").TrimEnd());
        Assert.Contains("updated schedule", loaded.HtmlBody);

        Assert.Equal("PL-000042", loaded.Headers[MailboxWriter.DocIdHeader]);
        Assert.Equal("abc123.PL-000042@northwind.example", loaded.MessageId);
        Assert.Equal("root01.PL-000040@northwind.example", loaded.InReplyTo);
        Assert.Equal(new[] { "root01.PL-000040@northwind.example" }, loaded.References.ToArray());
        Assert.Equal(TimeSpan.FromHours(-7), loaded.Date.Offset);
        Assert.Equal(email.Date, loaded.Date);
        Assert.Equal("RE: Window delivery schedule", loaded.Subject);

        var from = Assert.Single(loaded.From.Mailboxes);
        Assert.Equal("Pat Lee", from.Name);
        Assert.Equal("pat.lee@northwind.example", from.Address);
        var to = Assert.Single(loaded.To.Mailboxes);
        Assert.Equal("Sam Ortiz", to.Name);
        Assert.Equal("sam.ortiz@northwind.example", to.Address);
        var cc = Assert.Single(loaded.Cc.Mailboxes);
        Assert.Equal("Dana Cho", cc.Name);
        Assert.Equal("dana.cho@contoso.example", cc.Address);
    }

    [Fact]
    public void MailboxWriter_NoAttachments_MatchesGolden()
    {
        // Frozen before the Phase 6 change: comparing two calls to the new code would not prove compatibility (review 06-07/06-10)
        using var ms = new MemoryStream();
        MailboxWriter.BuildMime(Composed()).WriteTo(ms);

        Assert.Equal(Convert.FromBase64String(MailboxWriterGolden.NoAttachmentsEml), ms.ToArray());
    }

    [Fact]
    public void BuildMime_Root_NoThreadingHeaders()
    {
        var email = Composed();
        email.InReplyTo = string.Empty;
        email.References = new List<string>();

        var message = MailboxWriter.BuildMime(email);

        Assert.Null(message.InReplyTo);
        Assert.Empty(message.References);
    }

    [Fact]
    public void BuildMime_AngleBracketIds_Trimmed()
    {
        var email = Composed();
        email.MessageId = "<abc123.PL-000042@northwind.example>";

        Assert.Equal("abc123.PL-000042@northwind.example", MailboxWriter.BuildMime(email).MessageId);
    }

    [Fact]
    public async Task WriteAsync_WritesEveryCopy_Idempotent()
    {
        var writer = new MailboxWriter(_folder, Folders);
        var email = Composed();

        var written = await writer.WriteAsync(email);

        Assert.Equal(MailboxWriter.CopyPathsFor(email.Planned, Folders), written);
        var first = new List<byte[]>();
        foreach (var relative in written)
        {
            var full = Path.Combine(_folder, relative.Replace('/', Path.DirectorySeparatorChar));
            Assert.True(File.Exists(full), full);
            first.Add(await File.ReadAllBytesAsync(full));
        }

        var again = await writer.WriteAsync(Composed());

        Assert.Equal(written, again);
        for (var i = 0; i < written.Count; i++)
        {
            var full = Path.Combine(_folder, written[i].Replace('/', Path.DirectorySeparatorChar));
            Assert.Equal(first[i], await File.ReadAllBytesAsync(full));
        }

        // Atomic writer leaves no temp files behind
        Assert.All(Directory.GetFiles(_folder, "*", SearchOption.AllDirectories),
            f => Assert.EndsWith(".eml", f, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task WriteAsync_FolderMapFromPlanNumbering_MatchesPlannedPath()
    {
        var guide = new ReelDiscovery.Models.Case.CaseGuide();
        guide.Cast.Add(new ReelDiscovery.Models.Case.CastMember { Id = "CH-001", Email = "pat.lee@northwind.example" });
        guide.Cast.Add(new ReelDiscovery.Models.Case.CastMember { Id = "CH-005", Email = "sam.ortiz@northwind.example" });
        guide.Cast.Add(new ReelDiscovery.Models.Case.CastMember { Id = "CH-007", Email = "dana.cho@contoso.example" });
        var folders = PlanNumbering.CustodianFolders(guide);

        var written = await new MailboxWriter(_folder, folders).WriteAsync(Composed());

        Assert.Equal(3, written.Count);
        Assert.Equal("Mailboxes/pat.lee/Sent Items/PL-000042.eml", written[0]);
    }

    private static ComposedAttachment Attachment(string docId, AttachmentType kind, string fileName, bool inline = false, string contentId = "")
        => new()
        {
            DocId = docId,
            Kind = kind,
            FileName = fileName,
            Title = fileName,
            Bytes = System.Text.Encoding.UTF8.GetBytes($"bytes of {docId} {fileName}"),
            IsInline = inline,
            ContentId = contentId
        };

    private static byte[] Serialize(ComposedEmail email)
    {
        using var ms = new MemoryStream();
        MailboxWriter.BuildMime(email).WriteTo(ms);
        return ms.ToArray();
    }

    private static MimeMessage Reload(ComposedEmail email)
    {
        using var ms = new MemoryStream(Serialize(email));
        return MimeMessage.Load(ms);
    }

    private static IEnumerable<Multipart> Multiparts(MimeEntity entity)
    {
        if (entity is not Multipart multipart)
            yield break;
        yield return multipart;
        foreach (var child in multipart)
            foreach (var nested in Multiparts(child))
                yield return nested;
    }

    [Fact]
    public void BuildMime_WordAndExcel_MixedWithFilesInPlanOrder()
    {
        var email = Composed();
        email.Attachments.Add(Attachment("PL-000043", AttachmentType.Word, "Schedule.docx"));
        email.Attachments.Add(Attachment("PL-000044", AttachmentType.Excel, "Costs.xlsx"));

        var loaded = Reload(email);

        var mixed = Assert.IsType<Multipart>(loaded.Body);
        Assert.Equal("multipart/mixed", mixed.ContentType.MimeType);
        Assert.Equal(3, mixed.Count);
        var alternative = Assert.IsType<MultipartAlternative>(mixed[0]);
        Assert.Equal("text/plain", alternative[0].ContentType.MimeType);
        Assert.Equal("text/html", alternative[1].ContentType.MimeType);

        var word = Assert.IsType<MimePart>(mixed[1]);
        Assert.Equal("application/vnd.openxmlformats-officedocument.wordprocessingml.document", word.ContentType.MimeType);
        Assert.Equal("Schedule.docx", word.FileName);
        Assert.True(word.IsAttachment);
        Assert.Equal("PL-000043", word.Headers[MailboxWriter.DocIdHeader]);

        var excel = Assert.IsType<MimePart>(mixed[2]);
        Assert.Equal("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", excel.ContentType.MimeType);
        Assert.Equal("Costs.xlsx", excel.FileName);
        Assert.Equal("PL-000044", excel.Headers[MailboxWriter.DocIdHeader]);

        using var decoded = new MemoryStream();
        excel.Content!.DecodeTo(decoded);
        Assert.Equal(email.Attachments[1].Bytes, decoded.ToArray());
    }

    [Fact]
    public void BuildMime_PowerPoint_OpenXmlType()
    {
        var email = Composed();
        email.Attachments.Add(Attachment("PL-000043", AttachmentType.PowerPoint, "Deck.pptx"));

        var part = Assert.Single(Reload(email).Attachments.OfType<MimePart>());
        Assert.Equal("application/vnd.openxmlformats-officedocument.presentationml.presentation", part.ContentType.MimeType);
    }

    [Fact]
    public void BuildMime_InlineImageOnly_AlternativeWithRelatedHtml()
    {
        var email = Composed();
        email.Html = "<html><body><p>See photo</p><img src=\"cid:img1.PL-000043@reeldiscovery\"></body></html>";
        email.Attachments.Add(Attachment("PL-000043", AttachmentType.Image, "photo.png", inline: true, contentId: "img1.PL-000043@reeldiscovery"));

        var loaded = Reload(email);

        var alternative = Assert.IsType<MultipartAlternative>(loaded.Body);
        Assert.DoesNotContain(Multiparts(loaded.Body), m => m.ContentType.MimeType == "multipart/mixed");
        Assert.Equal("text/plain", alternative[0].ContentType.MimeType);
        var related = Assert.IsType<MultipartRelated>(alternative[1]);
        Assert.Equal("text/html", related[0].ContentType.MimeType);
        var image = Assert.IsType<MimePart>(related[1]);
        Assert.Equal("image/png", image.ContentType.MimeType);
        Assert.Equal("img1.PL-000043@reeldiscovery", image.ContentId);
        Assert.Equal("PL-000043", image.Headers[MailboxWriter.DocIdHeader]);
    }

    [Fact]
    public void BuildMime_NonInlineImage_AttachedAsPng()
    {
        var email = Composed();
        email.Attachments.Add(Attachment("PL-000043", AttachmentType.Image, "site.png"));

        var part = Assert.Single(Reload(email).Attachments.OfType<MimePart>());
        Assert.Equal("image/png", part.ContentType.MimeType);
        Assert.Equal("PL-000043", part.Headers[MailboxWriter.DocIdHeader]);
    }

    [Fact]
    public void BuildMime_InviteAndVoicemail_CalendarRequestAndAudio()
    {
        var email = Composed();
        email.Attachments.Add(Attachment("PL-000043", AttachmentType.CalendarInvite, "Site walk.ics"));
        email.Attachments.Add(Attachment("PL-000044", AttachmentType.Voicemail, "Voicemail.mp3"));

        var parts = Reload(email).Attachments.OfType<MimePart>().ToList();

        Assert.Equal(2, parts.Count);
        Assert.Equal("text/calendar", parts[0].ContentType.MimeType);
        Assert.Equal("REQUEST", parts[0].ContentType.Parameters["method"]);
        Assert.Equal("utf-8", parts[0].ContentType.Charset);
        Assert.EndsWith(".ics", parts[0].FileName);
        Assert.Equal("PL-000043", parts[0].Headers[MailboxWriter.DocIdHeader]);
        Assert.Equal("audio/mpeg", parts[1].ContentType.MimeType);
        Assert.Equal("PL-000044", parts[1].Headers[MailboxWriter.DocIdHeader]);
    }

    [Fact]
    public void BuildMime_WithAttachments_DeterministicDistinctBoundaries()
    {
        static ComposedEmail Build()
        {
            var email = Composed();
            email.Html = "<html><body><img src=\"cid:img1@rd\"></body></html>";
            email.Attachments.Add(Attachment("PL-000043", AttachmentType.Image, "photo.png", inline: true, contentId: "img1@rd"));
            email.Attachments.Add(Attachment("PL-000044", AttachmentType.Word, "Schedule.docx"));
            return email;
        }

        Assert.Equal(Serialize(Build()), Serialize(Build()));

        var multiparts = Multiparts(MailboxWriter.BuildMime(Build()).Body!).ToList();
        Assert.Equal(3, multiparts.Count);
        var boundaries = multiparts.Select(m => m.Boundary).ToList();
        Assert.All(boundaries, b => Assert.StartsWith("=-rd-", b));
        Assert.Equal(boundaries.Count, boundaries.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void BuildMime_FileNameWithPath_ReducedToName()
    {
        var email = Composed();
        email.Attachments.Add(Attachment("PL-000043", AttachmentType.Word, "folder\\sub\\Schedule.docx"));

        var part = Assert.Single(Reload(email).Attachments.OfType<MimePart>());
        Assert.Equal("Schedule.docx", part.FileName);
    }

    [Theory]
    [InlineData("")]
    [InlineData("..")]
    [InlineData("bad<name>.docx")]
    public void BuildMime_UnsafeFileName_PartSkipped(string fileName)
    {
        var email = Composed();
        email.Attachments.Add(Attachment("PL-000043", AttachmentType.Word, fileName));
        email.Attachments.Add(Attachment("PL-000044", AttachmentType.Excel, "Costs.xlsx"));

        var part = Assert.Single(Reload(email).Attachments.OfType<MimePart>());
        Assert.Equal("PL-000044", part.Headers[MailboxWriter.DocIdHeader]);
    }

    [Fact]
    public async Task WriteAsync_WithAttachments_SameBytesInEveryCopy()
    {
        var email = Composed();
        email.Attachments.Add(Attachment("PL-000043", AttachmentType.Word, "Schedule.docx"));

        var written = await new MailboxWriter(_folder, Folders).WriteAsync(email);

        Assert.Equal(3, written.Count);
        var expected = Serialize(email);
        foreach (var relative in written)
            Assert.Equal(expected, await File.ReadAllBytesAsync(Path.Combine(_folder, relative.Replace('/', Path.DirectorySeparatorChar))));
    }

    [Fact]
    public async Task WriteAsync_NonRelevantExternalSender_OnlyInboxCopies()
    {
        var email = Composed();
        email.Planned = new PlannedMessage
        {
            DocId = "PL-000060",
            FromId = "EXT-002",
            ToIds = new List<string> { "CH-002", "CH-005" },
            CustodianIds = new List<string> { "CH-002", "CH-005" },
            Basis = RelevanceBasis.None,
            FilePath = "Mailboxes/kim.park/Inbox/PL-000060.eml"
        };
        email.Attachments.Add(Attachment("PL-000061", AttachmentType.Excel, "Menu.xlsx"));

        var written = await new MailboxWriter(_folder, Folders).WriteAsync(email);

        Assert.Equal(new[] { "Mailboxes/kim.park/Inbox/PL-000060.eml", "Mailboxes/sam.ortiz/Inbox/PL-000060.eml" }, written);
    }
}
