using System.Globalization;
using ReelDiscovery.Helpers;
using ReelDiscovery.Models.Case;
using ReelDiscovery.Models.Corpus;
using ReelDiscovery.Models.Evidence;
using ReelDiscovery.Services.Case;
using ReelDiscovery.Services.Corpus;
using ReelDiscovery.Services.Evidence;
using ReelDiscovery.Tests.Corpus;

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

    // ---- Compose ----

    private const string Banner = "PRIVILEGED & CONFIDENTIAL - ATTORNEY-CLIENT COMMUNICATION";

    private static (CaseGuide Guide, CorpusPlan Plan, PeopleDirectory People) Fixture()
    {
        var (draft, plan) = EvidenceTestData.ApprovedCase();
        return (draft.Guide!, plan, PeopleDirectory.Create(draft.Guide!, plan));
    }

    private static ThreadAssignment Assignment(PlannedThread thread, PrivilegeMarking marking, LengthBand band = LengthBand.Medium) => new()
    {
        ThreadId = thread.Id,
        Marking = marking,
        Messages = thread.Messages.Select(m => new MessageAssignment { MessageId = m.Id, Seq = m.Seq, Length = band }).ToList()
    };

    // Bodies by seq; listed in reverse so matching must be by Seq, not position
    private static RenderedThread Rendered(PlannedThread thread, Func<int, string>? body = null, string signOff = "Thanks,") => new()
    {
        ThreadId = thread.Id,
        Messages = thread.Messages
            .OrderByDescending(m => m.Seq)
            .Select(m => new RenderedMessage
            {
                Seq = m.Seq,
                Body = body?.Invoke(m.Seq) ?? $"Body of message {m.Seq} about the window schedule.",
                SignOff = signOff
            })
            .ToList()
    };

    private static ComposedThread ComposeFact(string factId, PrivilegeMarking marking, Func<int, string>? body = null,
        string signOff = "Thanks,")
    {
        var (_, plan, people) = Fixture();
        var thread = PlanFixtures.Thread(plan, factId);
        return EmailComposer.Compose(plan.PlanId, thread, Assignment(thread, marking), Rendered(thread, body, signOff), people);
    }

    [Fact]
    public void Compose_threads_by_ReplyToSeq_ancestry()
    {
        var m = ComposeFact("F-003", PrivilegeMarking.None).Emails;
        Assert.Equal(4, m.Count);

        Assert.Equal(string.Empty, m[0].InReplyTo);
        Assert.Empty(m[0].References);
        Assert.Equal(m[0].MessageId, m[1].InReplyTo);
        Assert.Equal(new List<string> { m[0].MessageId }, m[1].References);
        Assert.Equal(m[1].MessageId, m[2].InReplyTo);
        Assert.Equal(new List<string> { m[0].MessageId, m[1].MessageId }, m[2].References);
        Assert.Equal(m[2].MessageId, m[3].InReplyTo);
        Assert.Equal(new List<string> { m[0].MessageId, m[1].MessageId, m[2].MessageId }, m[3].References);
        Assert.Equal(4, m.Select(e => e.MessageId).Distinct().Count());
    }

    [Fact]
    public void Compose_is_deterministic()
    {
        // Same plan composed twice (each fixture call gets a fresh PlanId)
        var (_, plan, people) = Fixture();
        var thread = PlanFixtures.Thread(plan, "F-003");
        var first = EmailComposer.Compose(plan.PlanId, thread, Assignment(thread, PrivilegeMarking.None), Rendered(thread), people);
        var second = EmailComposer.Compose(plan.PlanId, thread, Assignment(thread, PrivilegeMarking.None), Rendered(thread), people);
        Assert.Equal(first.Emails.Select(e => e.MessageId), second.Emails.Select(e => e.MessageId));
        Assert.Equal(first.Emails.Select(e => e.PlainText), second.Emails.Select(e => e.PlainText));
        Assert.Equal(first.Emails.Select(e => e.Html), second.Emails.Select(e => e.Html));
    }

    [Fact]
    public void Compose_quotes_the_parent_for_replies_and_forwards_it()
    {
        var m = ComposeFact("F-003", PrivilegeMarking.None).Emails;

        var parent = m[0];
        var expectedQuote = EmailComposer.QuoteHeader(MessageTime.Local(parent.Planned), parent.From.Name, parent.From.Email)
            + "\n" + ThreadingHelper.QuoteText(parent.PlainText);
        Assert.EndsWith("\n\n" + expectedQuote, m[1].PlainText);

        Assert.Contains("---------- Forwarded message ----------", m[3].PlainText);
        Assert.Contains(string.Join("\n", EmailComposer.ForwardHeaderLines(m[2])), m[3].PlainText);
        Assert.EndsWith("\n\n" + m[2].PlainText, m[3].PlainText);
    }

    [Fact]
    public void Compose_takes_people_dates_and_subjects_from_the_plan()
    {
        var (_, plan, people) = Fixture();
        var thread = PlanFixtures.Thread(plan, "F-003");
        var composed = EmailComposer.Compose(plan.PlanId, thread, Assignment(thread, PrivilegeMarking.None), Rendered(thread), people);

        Assert.Equal(thread.Id, composed.ThreadId);
        foreach (var email in composed.Emails)
        {
            var planned = email.Planned;
            Assert.Equal(thread.Id, email.ThreadId);
            Assert.Equal(people.NameOf(planned.FromId), email.From.Name);
            Assert.Equal(people.EmailOf(planned.FromId), email.From.Email);
            Assert.Equal(planned.ToIds.Select(people.EmailOf), email.To.Select(t => t.Email));
            Assert.Equal(planned.ToIds.Select(people.NameOf), email.To.Select(t => t.Name));
            Assert.Equal(planned.CcIds.Select(people.EmailOf), email.Cc.Select(t => t.Email));
            Assert.Equal(planned.SentAt, email.Date);
            Assert.Equal(planned.SentAt.Offset, email.Date.Offset);
            Assert.Equal(EmailComposer.SubjectFor(thread, planned, PrivilegeMarking.None), email.Subject);
            Assert.Equal(EmailComposer.MessageIdFor(plan.PlanId, planned.DocId, people.Find(planned.FromId)!.Domain), email.MessageId);
        }
    }

    [Fact]
    public void Compose_lays_out_body_sign_off_and_signature()
    {
        var (_, plan, people) = Fixture();
        var thread = PlanFixtures.Thread(plan, "F-003");
        var composed = EmailComposer.Compose(plan.PlanId, thread, Assignment(thread, PrivilegeMarking.None), Rendered(thread), people);

        var root = composed.Emails[0];
        var signature = EmailComposer.SignatureFor(people.Find(root.Planned.FromId), LengthBand.Medium, MessageKind.New);
        Assert.Equal("Body of message 1 about the window schedule.\n\nThanks,\n" + signature, root.PlainText);
        Assert.Equal("Body of message 1 about the window schedule.", root.BodyPlain);
        Assert.Equal("Thanks,", root.SignOff);
        Assert.Equal(signature, root.Signature);

        foreach (var email in composed.Emails)
            Assert.Equal(EmailComposer.SignatureFor(people.Find(email.Planned.FromId), LengthBand.Medium, email.Planned.Kind), email.Signature);

        var noSignOff = ComposeFact("F-003", PrivilegeMarking.None, signOff: "");
        Assert.Equal("Body of message 1 about the window schedule.\n\n" + signature, noSignOff.Emails[0].PlainText);
    }

    [Fact]
    public void Compose_labels_banner_threads()
    {
        foreach (var email in ComposeFact("F-001", PrivilegeMarking.Banner).Emails)
        {
            Assert.StartsWith(Banner + "\n\n", email.PlainText);
            Assert.Contains("<strong>PRIVILEGED &amp; CONFIDENTIAL - ATTORNEY-CLIENT COMMUNICATION</strong>", email.Html);
            Assert.DoesNotContain(EmailComposer.PrivilegedSubjectTag, email.Subject);
        }
    }

    [Fact]
    public void Compose_tags_subjects_for_banner_and_subject_threads()
    {
        foreach (var email in ComposeFact("F-001", PrivilegeMarking.BannerAndSubject).Emails)
        {
            Assert.StartsWith(Banner + "\n\n", email.PlainText);
            Assert.Contains("<strong>PRIVILEGED &amp; CONFIDENTIAL", email.Html);
            Assert.Contains(EmailComposer.PrivilegedSubjectTag, email.Subject);
        }
    }

    [Fact]
    public void Compose_leaves_unlabeled_privileged_threads_unmarked()
    {
        foreach (var email in ComposeFact("F-001", PrivilegeMarking.Unlabeled).Emails)
        {
            Assert.DoesNotContain("PRIVILEGED", email.PlainText, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("PRIVILEGED", email.Html, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Privileged", email.Subject, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void Compose_appends_the_counsel_signature_verbatim()
    {
        var (guide, plan, people) = Fixture();
        var thread = PlanFixtures.Thread(plan, "F-001");
        var composed = EmailComposer.Compose(plan.PlanId, thread, Assignment(thread, PrivilegeMarking.Banner), Rendered(thread), people);

        var counsel = composed.Emails.Single(e => e.Planned.FromId == "CH-005");
        var signature = guide.FindCast("CH-005")!.Signature;
        Assert.False(string.IsNullOrWhiteSpace(signature));
        Assert.Equal(signature, counsel.Signature);
        Assert.Contains("Thanks,\n" + signature, counsel.PlainText);
    }

    [Fact]
    public void Compose_themes_html_by_sender_domain_and_nests_replies()
    {
        var (_, plan, people) = Fixture();
        var thread = PlanFixtures.Thread(plan, "F-003");
        var composed = EmailComposer.Compose(plan.PlanId, thread, Assignment(thread, PrivilegeMarking.None), Rendered(thread), people);

        foreach (var email in composed.Emails)
        {
            var theme = OrganizationThemes.ForDomain(people.Find(email.Planned.FromId)!.Domain);
            Assert.Contains(theme.PrimaryColor, email.Html, StringComparison.OrdinalIgnoreCase);
        }

        var reply = composed.Emails[1];
        var quoteStart = reply.Html.IndexOf("<blockquote", StringComparison.Ordinal);
        Assert.True(quoteStart >= 0);
        Assert.True(reply.Html.IndexOf("Body of message 1", StringComparison.Ordinal) > quoteStart);
    }

    [Fact]
    public void Compose_renders_at_most_one_table_per_thread()
    {
        const string table = "Status below.\n\n| Item | Amount |\n|---|---|\n| Frames | 1200 |";
        var m = ComposeFact("F-003", PrivilegeMarking.None, seq => seq is 1 or 3 ? table : $"Body {seq}.").Emails;

        Assert.Contains("<table", m[0].Html);

        // Message 3 nests message 1's table; its own table stays literal text
        Assert.Equal(1, CountOf(m[2].Html, "<table"));
        Assert.Contains("| Frames | 1200 |", m[2].Html);
    }

    [Fact]
    public void Compose_throws_when_a_planned_message_was_not_rendered()
    {
        var (_, plan, people) = Fixture();
        var thread = PlanFixtures.Thread(plan, "F-003");
        var rendered = Rendered(thread);
        rendered.Messages.RemoveAll(r => r.Seq == 3);

        var ex = Assert.Throws<ArgumentException>(() =>
            EmailComposer.Compose(plan.PlanId, thread, Assignment(thread, PrivilegeMarking.None), rendered, people));
        Assert.Contains("3", ex.Message);
    }

    [Fact]
    public void Compose_matches_rendered_messages_by_seq()
    {
        var m = ComposeFact("F-003", PrivilegeMarking.None).Emails;
        for (var i = 0; i < m.Count; i++)
        {
            Assert.Equal(i + 1, m[i].Planned.Seq);
            Assert.Equal($"Body of message {i + 1} about the window schedule.", m[i].BodyPlain);
        }
    }

    [Fact]
    public void Compose_handles_an_external_sender()
    {
        var m = ComposeFact("F-004", PrivilegeMarking.None).Emails;
        Assert.Equal("Dana Whitfield", m[0].From.Name);
        Assert.Equal("Dana Whitfield", m[0].Signature);
        Assert.EndsWith("@summitglazingsupply.com", m[0].MessageId);
    }

    private static int CountOf(string text, string value)
    {
        var count = 0;
        for (var i = text.IndexOf(value, StringComparison.Ordinal); i >= 0; i = text.IndexOf(value, i + value.Length, StringComparison.Ordinal))
            count++;
        return count;
    }
}
