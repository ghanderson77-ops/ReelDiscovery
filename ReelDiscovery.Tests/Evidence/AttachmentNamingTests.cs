using ReelDiscovery.Models;
using ReelDiscovery.Models.Corpus;
using ReelDiscovery.Models.Evidence;
using ReelDiscovery.Services.Corpus;
using ReelDiscovery.Services.Evidence;
using Xunit;

namespace ReelDiscovery.Tests.Evidence;

public class AttachmentNamingTests
{
    private const string GenericPattern = @"^(Document|Spreadsheet|Presentation|Photo|Image|Invite|Voicemail) \d+\.";

    private static readonly IReadOnlyDictionary<string, AttachmentChainLink> NoChains = new Dictionary<string, AttachmentChainLink>();

    // 2024-03-05 14:07 in New York (EST, before the DST change)
    private static PlannedMessage Message(params PlannedAttachment[] attachments)
    {
        var message = new PlannedMessage
        {
            DocId = "DM-000001",
            Seq = 1,
            FromId = "CH-001",
            SentAt = new DateTimeOffset(2024, 3, 5, 14, 7, 0, TimeSpan.FromHours(-5)),
            TimeZoneId = "America/New_York"
        };
        for (var i = 0; i < attachments.Length; i++)
        {
            attachments[i].DocId = $"DM-000001.{i + 1:0000}";
            message.Attachments.Add(attachments[i]);
        }

        return message;
    }

    private static PlannedAttachment Spec(AttachmentType kind, string title = "", string fileName = "", string description = "") =>
        new() { Kind = kind, Title = title, FileName = fileName, Description = description };

    [Fact]
    public void NamesFor_ChainVersions_UseBaseTitleAndVersionSuffix()
    {
        var (draft, plan) = AttachmentTestData.MixedCase();
        var chains = AttachmentChains.Compute(plan, draft.Guide);
        var first = AttachmentTestData.Thread(plan, AttachmentTestData.RelevantDocThreadId).Messages[0];
        var second = AttachmentTestData.Thread(plan, AttachmentTestData.ChainThreadId).Messages[0];

        var v1 = AttachmentNaming.NamesFor(first, null, chains);
        var v2 = AttachmentNaming.NamesFor(second, null, chains);

        // FileNameFor sanitizing turns spaces into underscores (same as every approved plan file name)
        Assert.Equal("CO-007_Pricing_Backup.xlsx", v1[first.Attachments.Single(a => a.Kind == AttachmentType.Excel).DocId]);
        Assert.Equal("CO-007_Pricing_Backup_v2.xlsx", v2[second.Attachments[0].DocId]);
        Assert.Equal(RelevantThreadFiller.FileNameFor("CO-007 Pricing Backup_v2", AttachmentType.Excel), v2[second.Attachments[0].DocId]);
        Assert.Equal(new[] { first.Attachments[0].DocId, first.Attachments[1].DocId, first.Attachments[2].DocId }, v1.Keys);
    }

    [Fact]
    public void NamesFor_UntitledNonRelevant_UsesFillerTitle()
    {
        var message = Message(Spec(AttachmentType.Word, fileName: "Document 1.docx"));

        var names = AttachmentNaming.NamesFor(message, new[] { "Q3 Supplier Review" }, NoChains);

        Assert.Equal("Q3_Supplier_Review.docx", names["DM-000001.0001"]);
    }

    [Fact]
    public void NamesFor_NoTitleNoFiller_UsesDescriptionThenKindAndDate()
    {
        var message = Message(
            Spec(AttachmentType.Word, fileName: "Document 1.docx"),
            Spec(AttachmentType.Excel, fileName: "Spreadsheet 1.xlsx",
                description: "Monthly crane rental costs for the north tower site and the parking deck"),
            Spec(AttachmentType.Image, fileName: "Photo 1.png"));

        var names = AttachmentNaming.NamesFor(message, null, NoChains);

        Assert.Equal("Notes_2024-03-05.docx", names["DM-000001.0001"]);
        Assert.Equal("Monthly_crane_rental_costs_for_the_north_tower.xlsx", names["DM-000001.0002"]);
        Assert.Equal("Photo_2024-03-05.png", names["DM-000001.0003"]);
        Assert.All(names.Values, n => Assert.DoesNotMatch(GenericPattern, n));
    }

    [Fact]
    public void NamesFor_Voicemail_UsesLocalTimestamp()
    {
        var message = Message(Spec(AttachmentType.Voicemail, title: "Voicemail from Raymond Harmon"));

        var names = AttachmentNaming.NamesFor(message, null, NoChains);

        Assert.Equal("Voicemail_2024-03-05_2-07_PM.mp3", names["DM-000001.0001"]);
    }

    [Fact]
    public void NamesFor_SameName_IsDeduplicated()
    {
        var message = Message(
            Spec(AttachmentType.Word, title: "Site Notes"),
            Spec(AttachmentType.Word, title: "Site Notes"),
            Spec(AttachmentType.Word, title: "site notes"));

        var names = AttachmentNaming.NamesFor(message, null, NoChains);

        Assert.Equal(new[] { "Site_Notes.docx", "Site_Notes (2).docx", "site_notes (3).docx" }, names.Values);
    }

    [Theory]
    [InlineData("..\\..\\evil")]
    [InlineData("../../etc/passwd")]
    [InlineData("a..b")]
    [InlineData("CON:<>|?*")]
    [InlineData("   ")]
    [InlineData("....")]
    public void NamesFor_HostileTitles_AreSafe(string title)
    {
        var message = Message(Spec(AttachmentType.Word, title: title), Spec(AttachmentType.Image, title: title));

        var names = AttachmentNaming.NamesFor(message, new[] { title, title }, NoChains);

        Assert.Equal(2, names.Count);
        Assert.All(names.Values, n => Assert.True(CorpusPlan.IsSafeFileName(n), n));
        Assert.All(names.Values, n => Assert.DoesNotContain("..", n));
        Assert.All(names.Values, n => Assert.DoesNotMatch(GenericPattern, n));
    }

    [Fact]
    public void NamesFor_MixedCase_EveryNameIsSafeAndNotGeneric()
    {
        var (draft, plan) = AttachmentTestData.MixedCase();
        var chains = AttachmentChains.Compute(plan, draft.Guide);

        foreach (var message in plan.AllMessages().Where(m => m.Attachments.Count > 0))
        {
            var names = AttachmentNaming.NamesFor(message, null, chains);
            Assert.Equal(message.Attachments.Select(a => a.DocId), names.Keys);
            Assert.All(names.Values, n => Assert.True(CorpusPlan.IsSafeFileName(n), n));
            Assert.All(names.Values, n => Assert.DoesNotMatch(GenericPattern, n));
            Assert.Equal(names.Count, names.Values.Distinct(StringComparer.OrdinalIgnoreCase).Count());
            Assert.Equal(names, AttachmentNaming.NamesFor(message, null, chains));
        }
    }

    [Fact]
    public void TitleFor_PrefersSpecTitleThenFillerThenDescription()
    {
        var message = Message(Spec(AttachmentType.Word));

        Assert.Equal("Own", AttachmentNaming.TitleFor(Spec(AttachmentType.Word, title: "Own"), message, "Filler"));
        Assert.Equal("Filler", AttachmentNaming.TitleFor(Spec(AttachmentType.Word), message, "Filler"));
        Assert.Equal("One two three four five six seven eight",
            AttachmentNaming.TitleFor(Spec(AttachmentType.Word, description: "One two three four five six seven eight nine ten"), message, null));
        Assert.Equal("Slides 2024-03-05", AttachmentNaming.TitleFor(Spec(AttachmentType.PowerPoint), message, " "));
    }

    [Fact]
    public void ContentIdFor_IsStableHexAtDomain()
    {
        var id = AttachmentNaming.ContentIdFor("DM-000001.0002", "dundermifflin.com");

        Assert.Matches(@"^[0-9a-f]{16}@dundermifflin\.com$", id);
        Assert.Equal(id, AttachmentNaming.ContentIdFor("DM-000001.0002", "dundermifflin.com"));
        Assert.NotEqual(id, AttachmentNaming.ContentIdFor("DM-000001.0003", "dundermifflin.com"));
        Assert.EndsWith("@" + EmailComposer.FallbackDomain, AttachmentNaming.ContentIdFor("DM-000001.0002", "  "));
        Assert.EndsWith("@" + EmailComposer.FallbackDomain, AttachmentNaming.ContentIdFor("DM-000001.0002", null));
        Assert.Matches(@"^[0-9a-f]{16}@evil\.com$", AttachmentNaming.ContentIdFor("DM-000001.0002", " EVIL.com>\r\n"));
    }

    [Fact]
    public void VoiceAssignments_PersistedVoiceWins()
    {
        var cast = new CastVoiceSet { Voices = { new CastVoice { PersonId = "CH-001", Voice = "nova" } } };

        Assert.Equal("nova", VoiceAssignments.For("CH-001", cast));
    }

    [Fact]
    public void VoiceAssignments_MissingOrInvalid_UsesFallback()
    {
        var fallback = VoiceAssignments.Fallback("CH-001");

        Assert.Equal(fallback, VoiceAssignments.For("CH-001", null));
        Assert.Equal(fallback, VoiceAssignments.For("CH-001", new CastVoiceSet { Voices = { new CastVoice { PersonId = "CH-002", Voice = "nova" } } }));
        Assert.Equal(fallback, VoiceAssignments.For("CH-001", new CastVoiceSet { Voices = { new CastVoice { PersonId = "CH-001", Voice = "banana" } } }));
    }

    [Fact]
    public void VoiceAssignments_FallbackIsStableAndSpread()
    {
        var voices = Enumerable.Range(1, 50).Select(i => VoiceAssignments.Fallback($"CH-{i:000}")).ToList();

        Assert.All(voices, v => Assert.Contains(v, CastVoiceSet.AllowedVoices));
        Assert.True(voices.Distinct().Count() >= 4);
        Assert.Equal(VoiceAssignments.Fallback("CH-001"), VoiceAssignments.Fallback("CH-001"));
        Assert.Contains(VoiceAssignments.Fallback(null), CastVoiceSet.AllowedVoices);
    }
}
