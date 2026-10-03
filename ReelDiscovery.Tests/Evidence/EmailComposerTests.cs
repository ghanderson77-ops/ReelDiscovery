using System.Globalization;
using ReelDiscovery.Models.Case;
using ReelDiscovery.Models.Corpus;
using ReelDiscovery.Models.Evidence;
using ReelDiscovery.Services.Case;
using ReelDiscovery.Services.Corpus;
using ReelDiscovery.Services.Evidence;

namespace ReelDiscovery.Tests.Evidence;

public class EmailComposerTests
{
    private static readonly Guid PlanId = Guid.Parse("0f1e2d3c-4b5a-6978-8796-a5b4c3d2e1f0");

    private static PlannedThread SubjectThread(string subject) => new() { Id = "T-0001", Subject = subject };

    private static PlannedMessage Kind(MessageKind kind) => new() { Seq = 1, Kind = kind };

    // ---- SubjectFor ----

    [Theory]
    [InlineData(MessageKind.New, "Pay app 7")]
    [InlineData(MessageKind.Reply, "RE: Pay app 7")]
    [InlineData(MessageKind.ReplyAll, "RE: Pay app 7")]
    [InlineData(MessageKind.Forward, "FW: Pay app 7")]
    public void SubjectFor_prefixes_once_from_the_root(MessageKind kind, string expected)
    {
        Assert.Equal(expected, EmailComposer.SubjectFor(SubjectThread("Pay app 7"), Kind(kind), PrivilegeMarking.None));
    }

    [Fact]
    public void SubjectFor_never_doubles_a_reply_prefix()
    {
        Assert.Equal("RE: Pay app 7",
            EmailComposer.SubjectFor(SubjectThread("RE: Pay app 7"), Kind(MessageKind.Reply), PrivilegeMarking.None));
    }

    [Fact]
    public void SubjectFor_tags_privileged_subjects_only_for_BannerAndSubject()
    {
        var thread = SubjectThread("Pay app 7");
        Assert.Equal("Privileged & Confidential - Pay app 7",
            EmailComposer.SubjectFor(thread, Kind(MessageKind.New), PrivilegeMarking.BannerAndSubject));
        Assert.Equal("RE: Privileged & Confidential - Pay app 7",
            EmailComposer.SubjectFor(thread, Kind(MessageKind.Reply), PrivilegeMarking.BannerAndSubject));
        Assert.Equal("Pay app 7", EmailComposer.SubjectFor(thread, Kind(MessageKind.New), PrivilegeMarking.Banner));
        Assert.Equal("Pay app 7", EmailComposer.SubjectFor(thread, Kind(MessageKind.New), PrivilegeMarking.Unlabeled));
    }

    [Fact]
    public void SubjectFor_does_not_repeat_an_existing_tag()
    {
        var thread = SubjectThread("privileged & confidential - Pay app 7");
        Assert.Equal("privileged & confidential - Pay app 7",
            EmailComposer.SubjectFor(thread, Kind(MessageKind.New), PrivilegeMarking.BannerAndSubject));
    }

    [Fact]
    public void SubjectFor_strips_control_characters()
    {
        var thread = SubjectThread("Pay app 7\r\nBcc: someone@example.com\t\u0007");
        var subject = EmailComposer.SubjectFor(thread, Kind(MessageKind.New), PrivilegeMarking.None);
        Assert.DoesNotContain('\r', subject);
        Assert.DoesNotContain('\n', subject);
        Assert.DoesNotContain('\t', subject);
        Assert.DoesNotContain('\u0007', subject);
        Assert.StartsWith("Pay app 7", subject);
    }

    [Fact]
    public void SubjectFor_matches_the_answer_key_for_every_relevant_message()
    {
        var (draft, plan) = EvidenceTestData.ApprovedCase();
        var rows = AnswerKeyBuilder.Build(plan, draft.Guide!).ToDictionary(r => r.DocId, StringComparer.Ordinal);

        var checkedCount = 0;
        foreach (var thread in plan.Threads.Where(t => t.Relevance == ThreadRelevance.Relevant))
        {
            foreach (var message in thread.Messages)
            {
                Assert.Equal(rows[message.DocId].Subject, EmailComposer.SubjectFor(thread, message, PrivilegeMarking.None));
                checkedCount++;
            }
        }

        Assert.True(checkedCount > 0);
    }

    // ---- MessageIdFor ----

    [Fact]
    public void MessageIdFor_is_deterministic_hex_at_the_lower_case_domain()
    {
        var id = EmailComposer.MessageIdFor(PlanId, "RD-000001", "Brightline.com");
        Assert.Matches("^[0-9a-f]{24}@brightline\\.com$", id);
        Assert.Equal(id, EmailComposer.MessageIdFor(PlanId, "RD-000001", "Brightline.com"));
        Assert.NotEqual(id, EmailComposer.MessageIdFor(PlanId, "RD-000002", "Brightline.com"));
    }

    [Fact]
    public void MessageIdFor_uses_the_fallback_domain_when_blank()
    {
        Assert.EndsWith("@reeldiscovery.local", EmailComposer.MessageIdFor(PlanId, "RD-000001", ""));
        Assert.EndsWith("@reeldiscovery.local", EmailComposer.MessageIdFor(PlanId, "RD-000001", "!!!"));
    }

    [Fact]
    public void MessageIdFor_keeps_only_safe_domain_characters()
    {
        var id = EmailComposer.MessageIdFor(PlanId, "RD-000001", "bad domain!");
        var domain = id[(id.IndexOf('@') + 1)..];
        Assert.Matches("^[a-z0-9.-]+$", domain);
        Assert.Equal("baddomain", domain);
    }

    // ---- SignatureFor ----

    private static PersonInfo CastSender(SignatureStyle style, string signature)
    {
        var member = new CastMember
        {
            Id = "CH-090",
            FirstName = "Marisol",
            LastName = "Okafor",
            Title = "Project Manager",
            Email = "marisol.okafor@brightline.com",
            Phone = "(555) 555-0142",
            SignatureStyle = style,
            Signature = signature
        };
        return new PersonInfo
        {
            Id = member.Id,
            FullName = member.FullName,
            FirstName = member.FirstName,
            LastName = member.LastName,
            Email = member.Email,
            Domain = "brightline.com",
            Cast = member
        };
    }

    [Fact]
    public void SignatureFor_full_style_is_the_cast_signature_verbatim()
    {
        const string signature = "Marisol Okafor\nProject Manager\nBrightline\n(555) 555-0142";
        Assert.Equal(signature,
            EmailComposer.SignatureFor(CastSender(SignatureStyle.Full, signature), LengthBand.Medium, MessageKind.Reply));
    }

    [Fact]
    public void SignatureFor_short_name_only_reply_is_the_first_name()
    {
        Assert.Equal("Marisol",
            EmailComposer.SignatureFor(CastSender(SignatureStyle.NameOnly, "Marisol O.\nBrightline"), LengthBand.Short, MessageKind.Reply));
    }

    [Fact]
    public void SignatureFor_short_mobile_footer_reply_is_the_signature_verbatim()
    {
        const string signature = "Marisol\n\nSent from my mobile";
        Assert.Equal(signature,
            EmailComposer.SignatureFor(CastSender(SignatureStyle.MobileFooter, signature), LengthBand.Short, MessageKind.Reply));
    }

    [Fact]
    public void SignatureFor_blank_signature_falls_back_to_the_signature_builder()
    {
        var sender = CastSender(SignatureStyle.Full, "  ");
        Assert.Equal(SignatureBuilder.Build(sender.Cast!, null),
            EmailComposer.SignatureFor(sender, LengthBand.Medium, MessageKind.New));
    }

    [Fact]
    public void SignatureFor_external_sender_is_its_name()
    {
        var external = new PersonInfo { Id = "EXT-009", FullName = "Priya Raman", FirstName = "Priya", IsExternal = true };
        Assert.Equal("Priya Raman", EmailComposer.SignatureFor(external, LengthBand.Medium, MessageKind.New));
        Assert.Equal(string.Empty, EmailComposer.SignatureFor(null, LengthBand.Medium, MessageKind.New));
    }

    // ---- QuoteHeader / ForwardHeaderLines ----

    [Fact]
    public void QuoteHeader_is_en_US_regardless_of_current_culture()
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            var local = new DateTimeOffset(2024, 3, 19, 9, 42, 0, TimeSpan.FromHours(-5));
            Assert.Equal("On Tue, Mar 19, 2024 at 9:42 AM, Tom Becker <tom.becker@brightline.com> wrote:",
                EmailComposer.QuoteHeader(local, "Tom Becker", "tom.becker@brightline.com"));
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public void ForwardHeaderLines_lists_from_date_subject_to_and_cc()
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            var parent = new ComposedEmail
            {
                Planned = new PlannedMessage { SentAt = new DateTimeOffset(2024, 3, 19, 14, 5, 0, TimeSpan.FromHours(-5)) },
                Subject = "RE: Pay app 7",
                From = new EmailAddressPart { Name = "Tom Becker", Email = "tom.becker@brightline.com" },
                To = new List<EmailAddressPart>
                {
                    new() { Name = "Ana Ruiz", Email = "ana.ruiz@brightline.com" },
                    new() { Name = "Lee Park", Email = "lee.park@brightline.com" }
                },
                Cc = new List<EmailAddressPart> { new() { Name = "Sam Ito", Email = "sam.ito@brightline.com" } }
            };

            var lines = EmailComposer.ForwardHeaderLines(parent);
            Assert.Equal(new List<string>
            {
                "From: Tom Becker <tom.becker@brightline.com>",
                "Date: Tue, Mar 19, 2024 at 2:05 PM",
                "Subject: RE: Pay app 7",
                "To: Ana Ruiz <ana.ruiz@brightline.com>; Lee Park <lee.park@brightline.com>",
                "Cc: Sam Ito <sam.ito@brightline.com>"
            }, lines);

            parent.Cc.Clear();
            Assert.DoesNotContain(EmailComposer.ForwardHeaderLines(parent), l => l.StartsWith("Cc:", StringComparison.Ordinal));
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }
}
