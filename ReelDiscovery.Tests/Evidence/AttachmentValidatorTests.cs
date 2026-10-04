using ReelDiscovery.Models;
using ReelDiscovery.Models.Case;
using ReelDiscovery.Models.Corpus;
using ReelDiscovery.Models.Evidence;
using ReelDiscovery.Services.Evidence;
using Xunit;

namespace ReelDiscovery.Tests.Evidence;

/// <summary>
/// RenderValidator.CheckAttachment (D-17: V2-V7 on relevant attachment text, gated to the parent's ancestry) and
/// CheckFiller (D-12 strict: V1/V5 only), plus the voicemail exemption of V8 (D-28).
/// </summary>
public class AttachmentValidatorTests
{
    // Sibling thread T-0905: msg 1 Maria (CH-001) -> Tom, Karen; msg 2 Tom (CH-002) -> Maria (attachment); msg 3 Karen
    // (CH-005) -> Maria, a sibling branch of msg 2
    private const string Neutral = "Got it, I will check the schedule and come back to you later today with the details.";

    private static ThreadRenderContext Context(string threadId, Action<CaseGuide>? editGuide = null, LengthBand band = LengthBand.Short,
        Action<CorpusPlan>? editPlan = null)
    {
        var (draft, plan) = AttachmentTestData.MixedCase();
        draft.Guide!.Values = new List<CanonicalValue>();
        editGuide?.Invoke(draft.Guide!);
        editPlan?.Invoke(plan);
        var thread = AttachmentTestData.Thread(plan, threadId);
        var assignment = new ThreadAssignment
        {
            ThreadId = thread.Id,
            Messages = thread.Messages.Select(m => new MessageAssignment { Seq = m.Seq, Length = band }).ToList()
        };
        var people = PeopleDirectory.Create(draft.Guide!, plan);
        return ThreadRenderContext.Create(draft, plan, thread, assignment, people, new List<LedgerEntry>(), 2);
    }

    private static RenderedThread Accepted(string threadId, params string[] bodies) => new()
    {
        ThreadId = threadId,
        Messages = bodies.Select((b, i) => new RenderedMessage { Seq = i + 1, Body = b, SignOff = "Thanks," }).ToList()
    };

    private static RenderedThread SiblingAccepted(string? msg1 = null, string? msg2 = null, string? msg3 = null) =>
        Accepted(AttachmentTestData.SiblingBranchThreadId, msg1 ?? Neutral, msg2 ?? Neutral, msg3 ?? Neutral);

    private static AttachmentCheckInput Input(string text, string variantText = "", RenderedThread? accepted = null) => new()
    {
        ParentSeq = 2,
        Text = text,
        VariantText = variantText,
        AcceptedThread = accepted ?? SiblingAccepted()
    };

    private static ThreadRenderContext Sibling(Action<CaseGuide>? editGuide = null) =>
        Context(AttachmentTestData.SiblingBranchThreadId, editGuide);

    private static CanonicalValue Value(string id, string label, string value, params string[] factIds) => new()
    {
        Id = id,
        Kind = ValueKind.Amount,
        Label = label,
        Value = value,
        FactIds = factIds.ToList()
    };

    private static Action<CaseGuide> WithFactValue(string factId, string date, string knower, string label, string value) => guide =>
    {
        guide.Facts.Add(new KeyFact
        {
            Id = factId,
            Date = date,
            Title = label,
            ActorIds = new List<string> { knower },
            KnownByIds = new List<string> { knower },
            IssueIds = new List<string> { "ISS-01" }
        });
        guide.Values = new List<CanonicalValue> { Value("V-901", label, value, factId) };
    };

    private static string Words(int count) =>
        string.Join(" ", Enumerable.Range(0, count).Select(i => i % 2 == 0 ? "schedule" : "update"));

    private static LedgerDelta Detail(int seq, string about, string value, LedgerKind kind = LedgerKind.Figure) => new()
    {
        Seq = seq,
        Kind = kind,
        About = about,
        Statement = $"The {about} is {value}.",
        Value = value
    };

    // ---------- V2 / V3 / V6 near-amount / V7 ----------

    [Fact]
    public void CheckAttachment_PersonNotInGuide_IsV2OnParentSeq()
    {
        var ctx = Sibling();
        var input = Input(Neutral);
        input.PeopleNamed.Add("Gerald Fontaine");

        var v = Assert.Single(RenderValidator.CheckAttachment(input, ctx));

        Assert.Equal("V2", v.Code);
        Assert.Equal(2, v.Seq);
        Assert.Equal("names \"Gerald Fontaine\", who is not in the cast", v.Text);
    }

    [Fact]
    public void CheckAttachment_CanonicalVariantInVariantText_IsV3_OnlyInText_IsNot()
    {
        var ctx = Sibling(g => g.Values = new List<CanonicalValue> { Value("V-900", "Contract value", "$1,250,000") });

        var flagged = RenderValidator.CheckAttachment(Input("Summary attached.", "Contract value $1,240,000"), ctx);
        var v = Assert.Single(flagged);
        Assert.Equal("V3", v.Code);
        Assert.Equal("uses $1,240,000 for Contract value; the case value is $1,250,000", v.Text);
        Assert.Equal(2, v.Seq);

        Assert.Empty(RenderValidator.CheckAttachment(Input("Line item $1,240,000 for the frames.", "Contract value $1,250,000"), ctx));
    }

    [Fact]
    public void CheckAttachment_ExcelLineItemsInTextOnly_NoNearAmountV6()
    {
        var ctx = Sibling();
        ctx.LedgerView = new List<LedgerEntry> { ContractValue() };
        var text = "Line items\nCurtain wall $1,180,000\nPermits $70,000\nTotal $1,250,000";

        Assert.Empty(RenderValidator.CheckAttachment(Input(text, "Contract value $1,250,000"), ctx));

        var v = Assert.Single(RenderValidator.CheckAttachment(Input(text, "Contract value $1,180,000"), ctx));
        Assert.Equal("V6", v.Code);
        Assert.Equal("gives contract value as $1,180,000; earlier emails say $1,250,000", v.Text);
    }

    private static LedgerEntry ContractValue() => new()
    {
        Id = "T-0100-L01",
        Kind = LedgerKind.Figure,
        About = "contract value",
        Statement = "The contract value is $1,250,000.",
        Value = "$1,250,000",
        KnownByIds = new List<string> { "CH-001", "CH-002" }
    };

    [Fact]
    public void CheckAttachment_LegalTermInNonCounselAttachment_IsV7()
    {
        var ctx = Sibling();

        var v = Assert.Single(RenderValidator.CheckAttachment(Input("The plaintiff will want to see this schedule."), ctx));

        Assert.Equal("V7", v.Code);
        Assert.Equal(2, v.Seq);
    }

    [Fact]
    public void CheckAttachment_NeverReturnsStructureOrLengthCodes()
    {
        var ctx = Sibling();

        var longText = RenderValidator.CheckAttachment(Input(Words(1000), Words(1000)), ctx);
        var empty = RenderValidator.CheckAttachment(Input(string.Empty), ctx);

        Assert.DoesNotContain(longText.Concat(empty), v => v.Code is "V1" or "V5" or "V8");
        Assert.Empty(longText);
        Assert.Empty(empty);
    }

    [Fact]
    public void CheckAttachment_NullInputs_ReturnEmpty()
    {
        Assert.Empty(RenderValidator.CheckAttachment(null!, Sibling()));
        Assert.Empty(RenderValidator.CheckAttachment(Input(Neutral), null!));
    }

    // ---------- V4 knowledge by ancestry and date ----------

    [Fact]
    public void CheckAttachment_ValueInAncestorBody_IsSeen()
    {
        var ctx = Sibling(WithFactValue("F-090", "2024-01-10", "CH-005", "Retention balance", "$4,615.50"));
        var accepted = SiblingAccepted(msg1: "Karen says the retention balance is $4,615.50 as of now.");

        Assert.Empty(RenderValidator.CheckAttachment(Input("Retention balance: $4,615.50", accepted: accepted), ctx));
    }

    [Fact]
    public void CheckAttachment_ValueKnownOnlyToOthers_IsV4()
    {
        var ctx = Sibling(WithFactValue("F-090", "2024-01-10", "CH-005", "Retention balance", "$4,615.50"));

        var v = Assert.Single(RenderValidator.CheckAttachment(Input("Retention balance: $4,615.50"), ctx));

        Assert.Equal("V4", v.Code);
        Assert.Equal(2, v.Seq);
        Assert.Equal("uses $4,615.50, which Tom does not know yet", v.Text);
    }

    [Fact]
    public void CheckAttachment_ValueKnowableAfterParentDate_IsV4_UnlessInParentBody()
    {
        var ctx = Sibling(WithFactValue("F-091", "2024-09-01", "CH-002", "Late fee", "$7,300"));

        var v = Assert.Single(RenderValidator.CheckAttachment(Input("Late fee: $7,300"), ctx));
        Assert.Equal("V4", v.Code);
        Assert.Equal(2, v.Seq);
        Assert.Equal("mentions Late fee, dated after this email", v.Text);

        // The attachment travels with its email: what the parent body says is seen text
        var accepted = SiblingAccepted(msg2: "The late fee comes to $7,300 per the attached.");
        Assert.Empty(RenderValidator.CheckAttachment(Input("Late fee: $7,300", accepted: accepted), ctx));
    }

    [Fact]
    public void CheckAttachment_ValueDated_LedgerDateContradiction_IsV6()
    {
        var ctx = Sibling();
        ctx.LedgerView = new List<LedgerEntry> { SiteWalk() };

        var v = Assert.Single(RenderValidator.CheckAttachment(Input("The site walk on 2024-03-21 is confirmed."), ctx));

        Assert.Equal("V6", v.Code);
        Assert.Equal("gives site walk as 2024-03-21; earlier emails say 2024-03-14", v.Text);
    }

    private static LedgerEntry SiteWalk() => new()
    {
        Id = "T-0100-L02",
        Kind = LedgerKind.Meeting,
        About = "site walk",
        Statement = "The site walk is set.",
        Value = "2024-03-14",
        KnownByIds = new List<string> { "CH-001", "CH-002" }
    };

    // ---------- Adversarial ----------

    [Fact]
    public void Adversarial_NewDetailsConflictingWithLedger_IsV6()
    {
        var ctx = Sibling();
        ctx.LedgerView = new List<LedgerEntry> { ContractValue() };
        var input = Input(Neutral);
        input.NewDetails.Add(Detail(9, "contract value", "$1,300,000"));

        var v = Assert.Single(RenderValidator.CheckAttachment(input, ctx));

        Assert.Equal("V6", v.Code);
        Assert.Equal(2, v.Seq);
        Assert.Equal("gives contract value as $1,300,000; earlier emails say $1,250,000", v.Text);

        // The caller's deltas are not modified
        Assert.Equal(9, input.NewDetails[0].Seq);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void Adversarial_NewDetailsConflictingWithParentOrAncestorDelta_IsV6(int deltaSeq)
    {
        var ctx = Sibling();
        var accepted = SiblingAccepted();
        accepted.NewDetails.Add(Detail(deltaSeq, "invoice balance", "$52,000"));
        var input = Input(Neutral, accepted: accepted);
        input.NewDetails.Add(Detail(2, "invoice balance", "$58,000"));

        var v = Assert.Single(RenderValidator.CheckAttachment(input, ctx));

        Assert.Equal("V6", v.Code);
        Assert.Equal("gives invoice balance as $58,000; earlier emails say $52,000", v.Text);
    }

    [Fact]
    public void Adversarial_NewDetailsConflictingWithSiblingBranchDelta_IsNotFlagged()
    {
        var ctx = Sibling();
        var accepted = SiblingAccepted();
        accepted.NewDetails.Add(Detail(3, "invoice balance", "$52,000"));
        var input = Input(Neutral, accepted: accepted);
        input.NewDetails.Add(Detail(2, "invoice balance", "$58,000"));

        Assert.Empty(RenderValidator.CheckAttachment(input, ctx));
    }

    [Fact]
    public void Adversarial_ValueOnlyInSiblingBody_IsNotSeen()
    {
        var ctx = Sibling(WithFactValue("F-090", "2024-01-10", "CH-005", "Retention balance", "$4,615.50"));
        var accepted = SiblingAccepted(msg3: "Retention balance is $4,615.50 per my notes.");

        var v = Assert.Single(RenderValidator.CheckAttachment(Input("Retention balance: $4,615.50", accepted: accepted), ctx));

        Assert.Equal("V4", v.Code);
    }

    [Fact]
    public void Adversarial_LateRepair_ContradictsLaterEstablishedDate_IsV6()
    {
        var ctx = Sibling();
        var input = Input("The site walk on 2024-03-21 is confirmed.");
        input.LateRepair = true;
        input.LaterEstablished.Add(SiteWalk());

        var v = Assert.Single(RenderValidator.CheckAttachment(input, ctx));

        Assert.Equal("V6", v.Code);
        Assert.Equal("gives site walk as 2024-03-21; earlier emails say 2024-03-14", v.Text);
    }

    [Fact]
    public void Adversarial_LaterEstablished_IgnoredWithoutLateRepair()
    {
        var ctx = Sibling();
        var input = Input("The site walk on 2024-03-21 is confirmed.");
        input.LaterEstablished.Add(SiteWalk());

        Assert.Empty(RenderValidator.CheckAttachment(input, ctx));
    }

    [Fact]
    public void Adversarial_LateRepairWithAnyNewDetail_IsV6()
    {
        var ctx = Sibling();
        var input = Input(Neutral);
        input.NewDetails.Add(Detail(2, "crane booking", "2024-06-03", LedgerKind.Commitment));

        Assert.Empty(RenderValidator.CheckAttachment(input, ctx));

        input.LateRepair = true;
        var v = Assert.Single(RenderValidator.CheckAttachment(input, ctx));
        Assert.Equal("V6", v.Code);
        Assert.Equal(2, v.Seq);
        Assert.Equal("adds a new detail after later emails were written", v.Text);
    }

    // ---------- CheckFiller (D-12 strict) ----------

    private static ThreadRenderContext Daily(LengthBand band = LengthBand.Short) =>
        Context(AttachmentTestData.DailyBusinessThreadId, g => g.Values = new List<CanonicalValue> { Value("V-900", "Change Order 7", "$184,250") }, band);

    [Fact]
    public void CheckFiller_MissingMessage_IsV1()
    {
        var ctx = Daily();
        var rendered = Accepted(AttachmentTestData.DailyBusinessThreadId, Neutral, Neutral, Neutral);
        rendered.Messages.RemoveAt(1);

        var v = Assert.Single(RenderValidator.CheckFiller(rendered, ctx));

        Assert.Equal("V1", v.Code);
        Assert.Equal("has 2 messages; the plan has 3", v.Text);
    }

    [Fact]
    public void CheckFiller_EmptyBodies_AreV1OrV5()
    {
        var ctx = Daily();
        var empty = Accepted(AttachmentTestData.DailyBusinessThreadId, Neutral, "  ", Neutral);
        Assert.Equal("V1", Assert.Single(RenderValidator.CheckFiller(empty, ctx)).Code);

        var quoted = Accepted(AttachmentTestData.DailyBusinessThreadId, Neutral, string.Empty, Neutral);
        quoted.RepairNotes.Add("msg 2: removed quoted text");
        var v = Assert.Single(RenderValidator.CheckFiller(quoted, ctx));
        Assert.Equal("V5", v.Code);
        Assert.Equal(2, v.Seq);
    }

    [Fact]
    public void CheckFiller_UnknownPeopleAndWrongAmounts_NoViolation()
    {
        var ctx = Daily();
        var rendered = Accepted(AttachmentTestData.DailyBusinessThreadId,
            "I spoke with Maria Ortiz and Mr. Kowalski this morning.", "The total came to $184,520 for the change order.", Neutral);
        rendered.Messages[0].PeopleNamed.Add("Gerald Fontaine");
        rendered.NewDetails.Add(Detail(1, "contract value", "$1,300,000"));
        ctx.LedgerView = new List<LedgerEntry> { ContractValue() };

        Assert.Empty(RenderValidator.CheckFiller(rendered, ctx));
        Assert.NotEmpty(RenderValidator.Check(rendered, ctx));
    }

    [Fact]
    public void CheckFiller_LongBodyInShortBand_NoViolation()
    {
        var ctx = Daily(LengthBand.Short);
        var rendered = Accepted(AttachmentTestData.DailyBusinessThreadId, Neutral, Words(400), Neutral);

        Assert.Empty(RenderValidator.CheckFiller(rendered, ctx));
        Assert.Contains(RenderValidator.Check(rendered, ctx), v => v.Code == "V8" && v.Seq == 2);
    }

    [Fact]
    public void CheckFiller_NullInputs_ReturnEmpty()
    {
        Assert.Empty(RenderValidator.CheckFiller(null!, Daily()));
        Assert.Empty(RenderValidator.CheckFiller(Accepted(AttachmentTestData.DailyBusinessThreadId, Neutral), null!));
    }

    // ---------- V8 voicemail exemption (D-28) ----------

    [Fact]
    public void Check_VoicemailMessage_SkipsV8()
    {
        var ctx = Context(AttachmentTestData.RelevantVoicemailThreadId);
        var rendered = Accepted(AttachmentTestData.RelevantVoicemailThreadId, Words(400));

        Assert.DoesNotContain(RenderValidator.Check(rendered, ctx), v => v.Code == "V8");

        var plain = Context(AttachmentTestData.RelevantVoicemailThreadId,
            editPlan: p => AttachmentTestData.Thread(p, AttachmentTestData.RelevantVoicemailThreadId).Messages[0].Attachments.Clear());
        Assert.Contains(RenderValidator.Check(rendered, plain), v => v.Code == "V8");
    }
}
