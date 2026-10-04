using ReelDiscovery.Models;
using ReelDiscovery.Models.Case;
using ReelDiscovery.Models.Corpus;
using ReelDiscovery.Models.Evidence;
using ReelDiscovery.Services.Evidence;

namespace ReelDiscovery.Tests.Evidence;

public class VoicemailComposerTests
{
    private static readonly Guid PlanId = Guid.Parse("5a1e2d3c-4b5a-6978-8796-a5b4c3d2e1f0");

    // 105 words -> 42 seconds at ~150 words per minute
    private static readonly string Transcript = string.Join(" ", Enumerable.Range(1, 105).Select(i => i % 7 == 0 ? "paper." : "paper"));

    private const string AudioDocId = "DM-000100.0001";

    private sealed class Fixture
    {
        public CaseGuide Guide = new();
        public CorpusPlan Plan = new() { PlanId = PlanId };
        public PlannedThread Thread = new();
    }

    private static Fixture Build(string phone = "570-555-0100", string recipientId = "CH-002", int messages = 1)
    {
        var f = new Fixture();
        f.Guide.Organizations.Add(new CaseOrganization { Id = "ORG-01", Name = "Dunder Mifflin", Domain = "dundermifflin.com", TimeZoneId = "America/New_York" });
        f.Guide.Cast.Add(new CastMember
        {
            Id = "CH-001",
            FirstName = "Michael",
            LastName = "Scott",
            OrganizationId = "ORG-01",
            Email = "michael.scott@dundermifflin.com",
            Phone = phone,
            Signature = "Michael Scott\nRegional Manager"
        });
        f.Guide.Cast.Add(new CastMember
        {
            Id = "CH-002",
            FirstName = "Pam",
            LastName = "Beesly",
            OrganizationId = "ORG-01",
            Email = "pam.beesly@dundermifflin.com",
            Signature = "Pam Beesly"
        });
        f.Plan.Externals.Add(new ExternalParty { Id = "EXT-001", Name = "Jan Levinson", Email = "jan@mailbox.example", Domain = "mailbox.example", Kind = ExternalKind.Personal });
        f.Plan.Externals.Add(new ExternalParty { Id = "EXT-002", Name = "Nobody", Email = string.Empty, Domain = string.Empty, Kind = ExternalKind.Personal });

        f.Thread = new PlannedThread
        {
            Id = "T-0100",
            Relevance = ThreadRelevance.NotRelevant,
            Category = NonRelevantCategory.DailyBusiness,
            Subject = "Call me back"
        };

        var sentAt = new DateTimeOffset(2024, 3, 5, 14, 7, 0, TimeSpan.FromHours(-5));
        for (var seq = 1; seq <= messages; seq++)
        {
            f.Thread.Messages.Add(new PlannedMessage
            {
                Id = $"T-0100-M{seq:00}",
                DocId = $"DM-{99 + seq:000000}",
                Seq = seq,
                ReplyToSeq = seq - 1,
                Kind = seq == 1 ? MessageKind.New : MessageKind.Reply,
                FromId = seq % 2 == 1 ? "CH-001" : recipientId,
                ToIds = new List<string> { seq % 2 == 1 ? recipientId : "CH-001" },
                CcIds = new List<string>(),
                SentAt = sentAt.AddHours(seq - 1),
                TimeZoneId = "America/New_York"
            });
        }

        f.Thread.Messages[0].Attachments.Add(new PlannedAttachment
        {
            DocId = AudioDocId,
            Kind = AttachmentType.Voicemail,
            Title = "Voicemail from Michael Scott",
            FileName = "Voicemail 2024-03-05 2-07 PM.mp3"
        });
        f.Plan.Threads.Add(f.Thread);
        return f;
    }

    private static ThreadAssignment Assignment(PlannedThread thread) => new()
    {
        ThreadId = thread.Id,
        Marking = PrivilegeMarking.None,
        Messages = thread.Messages.Select(m => new MessageAssignment { MessageId = m.Id, Seq = m.Seq, Length = LengthBand.Medium }).ToList()
    };

    private static RenderedThread Rendered(PlannedThread thread) => new()
    {
        ThreadId = thread.Id,
        Messages = thread.Messages.Select(m => new RenderedMessage { Seq = m.Seq, Body = m.Seq == 1 ? Transcript : "Got it.", SignOff = "Thanks," }).ToList()
    };

    private static Dictionary<string, ComposedAttachment> Audio() => new()
    {
        [AudioDocId] = new ComposedAttachment
        {
            DocId = AudioDocId,
            Kind = AttachmentType.Voicemail,
            FileName = "Voicemail 2024-03-05 2-07 PM.mp3",
            Title = "Voicemail from Michael Scott",
            MimeType = "audio/mpeg",
            Bytes = new byte[] { 1, 2, 3 }
        }
    };

    private static ComposedThread Compose(Fixture f, bool voicemailNotice = true) =>
        EmailComposer.Compose(PlanId, f.Thread, Assignment(f.Thread), Rendered(f.Thread), PeopleDirectory.Create(f.Guide, f.Plan),
            Audio(), voicemailNotice);

    [Fact]
    public void Compose_single_message_voicemail_becomes_a_phone_system_notice()
    {
        var email = Compose(Build()).Emails.Single();

        Assert.Equal("Dunder Mifflin Voice Mail", email.From.Name);
        Assert.Equal("voicemail@dundermifflin.com", email.From.Email);
        Assert.Equal("Voicemail from Michael Scott (0:42)", email.Subject);
        Assert.Equal(
            "You received a voicemail from Michael Scott (570-555-0100) on Tue, Mar 5, 2024 at 2:07 PM.\n\nDuration: 0:42\n\n"
            + VoicemailComposer.TranscriptHeader + "\n" + Transcript,
            email.PlainText);
        Assert.Equal(string.Empty, email.Signature);
        Assert.Equal(string.Empty, email.SignOff);
        Assert.Equal(Transcript, email.BodyPlain);
        Assert.Contains("Transcript (auto-generated):", email.Html);
        Assert.DoesNotContain("Regional Manager", email.Html);
        Assert.DoesNotContain("class=\"signature\"", email.Html);
        Assert.Equal(AudioDocId, Assert.Single(email.Attachments).DocId);
    }

    [Fact]
    public void Compose_caller_without_phone_has_no_empty_parentheses()
    {
        var email = Compose(Build(phone: "  ")).Emails.Single();
        Assert.StartsWith("You received a voicemail from Michael Scott on Tue, Mar 5, 2024 at 2:07 PM.", email.PlainText);
        Assert.DoesNotContain("()", email.PlainText);
    }

    [Theory]
    [InlineData(0, 5)]
    [InlineData(1, 5)]
    [InlineData(25, 10)]
    [InlineData(105, 42)]
    [InlineData(163, 65)]
    public void DurationSeconds_is_words_over_2_5_with_a_5_second_minimum(int words, int expected)
    {
        var transcript = string.Join(" ", Enumerable.Repeat("word", words));
        Assert.Equal(expected, VoicemailComposer.DurationSeconds(transcript));
    }

    [Fact]
    public void DurationSeconds_blank_is_the_minimum()
    {
        Assert.Equal(5, VoicemailComposer.DurationSeconds(""));
        Assert.Equal(5, VoicemailComposer.DurationSeconds(null!));
    }

    [Theory]
    [InlineData(5, "0:05")]
    [InlineData(42, "0:42")]
    [InlineData(65, "1:05")]
    [InlineData(600, "10:00")]
    public void Duration_formats_m_ss(int seconds, string expected)
    {
        Assert.Equal(expected, VoicemailComposer.Duration(seconds));
    }

    [Fact]
    public void IsVoicemail_requires_a_single_message_thread_with_a_voicemail_attachment()
    {
        var single = Build();
        Assert.True(VoicemailComposer.IsVoicemail(single.Thread, single.Thread.Messages[0]));

        var multi = Build(messages: 2);
        Assert.False(VoicemailComposer.IsVoicemail(multi.Thread, multi.Thread.Messages[0]));

        single.Thread.Messages[0].Attachments[0].Kind = AttachmentType.Word;
        Assert.False(VoicemailComposer.IsVoicemail(single.Thread, single.Thread.Messages[0]));
    }

    [Fact]
    public void Compose_multi_message_thread_with_voicemail_is_not_overridden()
    {
        var emails = Compose(Build(messages: 2)).Emails;
        Assert.Equal("Michael Scott", emails[0].From.Name);
        Assert.Equal("Call me back", emails[0].Subject);
        Assert.DoesNotContain(VoicemailComposer.TranscriptHeader, emails[0].PlainText);
    }

    [Fact]
    public void Compose_recipient_without_organization_uses_plain_voice_mail_and_own_domain()
    {
        var email = Compose(Build(recipientId: "EXT-001")).Emails.Single();
        Assert.Equal("Voice Mail", email.From.Name);
        Assert.Equal("voicemail@mailbox.example", email.From.Email);

        var blank = Compose(Build(recipientId: "EXT-002")).Emails.Single();
        Assert.Equal("Voice Mail", blank.From.Name);
        Assert.Equal("voicemail@" + EmailComposer.FallbackDomain, blank.From.Email);
    }

    [Fact]
    public void Compose_without_voicemail_notice_keeps_the_phase_5_text_plus_the_audio_part()
    {
        var legacy = Compose(Build(), voicemailNotice: false).Emails.Single();

        var plain = Build();
        plain.Thread.Messages[0].Attachments.Clear();
        var expected = Compose(plain).Emails.Single();

        Assert.Equal("Michael Scott", legacy.From.Name);
        Assert.Equal("michael.scott@dundermifflin.com", legacy.From.Email);
        Assert.Equal(expected.Subject, legacy.Subject);
        Assert.Equal(expected.PlainText, legacy.PlainText);
        Assert.Equal(expected.Html, legacy.Html);
        Assert.Equal(expected.Signature, legacy.Signature);
        Assert.Equal(AudioDocId, Assert.Single(legacy.Attachments).DocId);
        Assert.Empty(expected.Attachments);
    }

    [Fact]
    public void Compose_voicemail_headers_have_no_control_characters()
    {
        var f = Build();
        f.Guide.Cast[0].FirstName = "Mich\r\nael\t";
        f.Guide.Organizations[0].Name = "Dunder\r\nBcc: x@example.com Mifflin";

        var email = Compose(f).Emails.Single();

        Assert.DoesNotMatch(@"\p{Cc}", email.Subject);
        Assert.DoesNotMatch(@"\p{Cc}", email.From.Name);
        Assert.DoesNotMatch(@"\p{Cc}", email.From.Email);
        Assert.StartsWith("Voicemail from ", email.Subject);
    }

    [Fact]
    public void Compose_voicemail_keeps_planned_message_id_date_and_recipients()
    {
        var f = Build();
        var people = PeopleDirectory.Create(f.Guide, f.Plan);
        var planned = f.Thread.Messages[0];
        var email = Compose(f).Emails.Single();

        Assert.Equal(EmailComposer.MessageIdFor(PlanId, planned.DocId, "dundermifflin.com"), email.MessageId);
        Assert.Equal(planned.SentAt, email.Date);
        Assert.Equal(new[] { people.EmailOf("CH-002") }, email.To.Select(t => t.Email));
        Assert.Empty(email.Cc);
        Assert.Equal(string.Empty, email.InReplyTo);
    }

    [Fact]
    public void Compose_voicemail_is_deterministic()
    {
        var first = Compose(Build()).Emails.Single();
        var second = Compose(Build()).Emails.Single();
        Assert.Equal(first.PlainText, second.PlainText);
        Assert.Equal(first.Html, second.Html);
    }
}
