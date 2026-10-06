using ReelDiscovery.Models;
using ReelDiscovery.Models.Case;
using ReelDiscovery.Models.Corpus;
using ReelDiscovery.Models.Evidence;
using ReelDiscovery.Services.Ai;
using ReelDiscovery.Services.Evidence;

namespace ReelDiscovery.Tests.Evidence;

/// <summary>
/// AttachmentPrompts (D-14, D-15, D-16, D-20, D-27, Pitfall 4/12, review concerns 4 and 5) and the voicemail transcript
/// line of EvidencePrompts (D-28).
/// </summary>
public class AttachmentPromptsTests
{
    private const string NoText = "no text, letters, numbers, signage, logos or labels anywhere";

    private static AttachmentRenderContext Context(string threadId, AttachmentType kind, int parentSeq = 1, Action<CaseGuide>? editGuide = null)
    {
        var (draft, plan) = AttachmentTestData.MixedCase();
        editGuide?.Invoke(draft.Guide!);
        var thread = AttachmentTestData.Thread(plan, threadId);
        var assignment = new ThreadAssignment
        {
            ThreadId = thread.Id,
            Messages = thread.Messages.Select(m => new MessageAssignment { Seq = m.Seq, Length = LengthBand.Medium }).ToList()
        };
        var people = PeopleDirectory.Create(draft.Guide!, plan);
        var threadCtx = ThreadRenderContext.Create(draft, plan, thread, assignment, people, new List<LedgerEntry>(), 2);
        var parent = thread.Messages.Single(m => m.Seq == parentSeq);
        var spec = parent.Attachments.FirstOrDefault(a => a.Kind == kind)
            ?? new PlannedAttachment { DocId = parent.DocId + ".0009", Kind = kind, Title = "Test document", FileName = "Test document.docx" };
        var relevant = thread.Relevance == ThreadRelevance.Relevant;
        var ctx = new AttachmentRenderContext
        {
            Thread = threadCtx,
            Parent = parent,
            Spec = spec,
            Title = spec.Title,
            FileName = spec.FileName,
            ParentBody = "Attached is the document we discussed.",
            Relevant = relevant,
            IsDecoy = thread.Category == NonRelevantCategory.Decoy,
            Tier = relevant ? LlmTier.Story : LlmTier.Volume
        };
        ctx.Rich = AttachmentPrompts.IsRich(ctx);
        return ctx;
    }

    private static Action<CaseGuide> WithEarlyAndLateValues => guide =>
    {
        guide.Facts.Add(Fact("F-951", "2024-04-01", "Early pricing note", "CH-002"));
        guide.Facts.Add(Fact("F-950", "2024-04-24", "Late pricing note", "CH-002"));
        guide.Values = new List<CanonicalValue>
        {
            new() { Id = "V-951", Kind = ValueKind.Amount, Label = "Early figure", Value = "$12,345.00", FactIds = new() { "F-951" } },
            new() { Id = "V-950", Kind = ValueKind.Amount, Label = "Late figure", Value = "$77,777.00", FactIds = new() { "F-950" } }
        };
    };

    private static KeyFact Fact(string id, string date, string title, string knower) => new()
    {
        Id = id,
        Date = date,
        Title = title,
        Description = $"{title} details.",
        ActorIds = new List<string> { knower },
        KnownByIds = new List<string> { knower },
        IssueIds = new List<string> { "ISS-01" }
    };

    private static LedgerEntry Entry(string date, string about, string value, params string[] knownBy) => new()
    {
        Id = $"T-0001-L{about.Length:00}",
        Date = date,
        Kind = LedgerKind.Figure,
        About = about,
        Statement = $"The {about} is {value}.",
        Value = value,
        KnownByIds = knownBy.ToList(),
        WaveIndex = 1
    };

    // ---------- system prompts (D-01, D-03) ----------

    [Fact]
    public void SystemPrompts_KeepNamesFromFictionalWorks_WithoutTheWordFictional()
    {
        foreach (var prompt in new[] { AttachmentPrompts.SystemPrompt, AttachmentPrompts.FillerSystemPrompt })
        {
            Assert.Contains("including characters and companies from films, TV series, books or games", prompt);
            Assert.DoesNotContain("fictional", prompt, StringComparison.OrdinalIgnoreCase);
        }

        Assert.Contains("newDetails", AttachmentPrompts.SystemPrompt);
        Assert.DoesNotContain("newDetails", AttachmentPrompts.FillerSystemPrompt);
    }

    // ---------- gating (Pitfall 4, review concerns 4 and 5) ----------

    [Fact]
    public void Relevant_FactsValuesAndLedger_GatedToParentSeqAndDate()
    {
        var ctx = Context(AttachmentTestData.MultiMessageThreadId, AttachmentType.Word, 1, WithEarlyAndLateValues);
        Assert.Contains("V-950", ctx.Thread.Knowledge.Values.Select(v => v.Value.Id));
        ctx.Thread.LedgerView = new List<LedgerEntry>
        {
            Entry("2024-04-20", "early detail", "$1.11", "CH-002"),
            Entry("2024-04-25", "late detail", "$2.22", "CH-002")
        };

        var prompt = AttachmentPrompts.Build(ctx);

        Assert.Contains("What they know as of 2024-04-23:", prompt);
        Assert.Contains("F-951", prompt);
        Assert.DoesNotContain("F-950", prompt);
        Assert.Contains("Early figure: $12,345.00", prompt);
        Assert.DoesNotContain("$77,777.00", prompt);
        Assert.Contains("early detail", prompt);
        Assert.DoesNotContain("late detail", prompt);
        Assert.DoesNotContain("$2.22", prompt);
    }

    [Fact]
    public void Relevant_OwnThread_FollowsAncestryNotSequence()
    {
        RenderedThread Accepted() => new()
        {
            ThreadId = AttachmentTestData.SiblingBranchThreadId,
            Messages = new()
            {
                new() { Seq = 1, Body = "Root body about the invoice." },
                new() { Seq = 2, Body = "Branch A body with the summary." },
                new() { Seq = 3, Body = "Sibling secret body from counsel." }
            }
        };
        List<LedgerDelta> Deltas() => new()
        {
            new() { Seq = 1, Kind = LedgerKind.Commitment, About = "root detail", Statement = "Root commitment." },
            new() { Seq = 2, Kind = LedgerKind.Figure, About = "branch a detail", Statement = "Branch A figure." },
            new() { Seq = 3, Kind = LedgerKind.Decision, About = "sibling detail", Statement = "Sibling decision." }
        };

        var onTwo = Context(AttachmentTestData.SiblingBranchThreadId, AttachmentType.Word, 2);
        onTwo.AcceptedThread = Accepted();
        onTwo.VisibleThreadDeltas = Deltas();
        var promptTwo = AttachmentPrompts.Build(onTwo);

        Assert.Contains("Root body about the invoice.", promptTwo);
        Assert.Contains("root detail", promptTwo);
        Assert.Contains("branch a detail", promptTwo);
        Assert.DoesNotContain("Sibling secret body", promptTwo);
        Assert.DoesNotContain("sibling detail", promptTwo);

        // Msg 2 has a lower seq than msg 3 but is a sibling branch: never shown for an attachment on msg 3
        var onThree = Context(AttachmentTestData.SiblingBranchThreadId, AttachmentType.Word, 3);
        onThree.AcceptedThread = Accepted();
        onThree.VisibleThreadDeltas = Deltas();
        var promptThree = AttachmentPrompts.Build(onThree);

        Assert.Contains("Root body about the invoice.", promptThree);
        Assert.DoesNotContain("Branch A body", promptThree);
        Assert.DoesNotContain("branch a detail", promptThree);
        Assert.Contains("sibling detail", promptThree);
    }

    [Fact]
    public void LateRepair_AddsTheLine_AndNeverShowsLaterEstablished()
    {
        var ctx = Context(AttachmentTestData.RelevantDocThreadId, AttachmentType.Word);
        ctx.LateRepair = true;
        ctx.LaterEstablished = new List<LedgerEntry> { Entry("2024-05-01", "future total", "$99,999.00", "CH-004") };

        var prompt = AttachmentPrompts.Build(ctx);

        Assert.Contains("Later emails already exist: do not introduce new figures, dates, totals or commitments; use only the details listed above.", prompt);
        Assert.DoesNotContain("$99,999.00", prompt);
        Assert.DoesNotContain("future total", prompt);

        ctx.LateRepair = false;
        Assert.DoesNotContain("Later emails already exist", AttachmentPrompts.Build(ctx));
    }

    // ---------- parent email and spec ----------

    [Fact]
    public void Prompt_CarriesParentEmailAndSpec()
    {
        var ctx = Context(AttachmentTestData.RelevantDocThreadId, AttachmentType.Word);
        ctx.FileName = "Change Order Pricing Memo.docx";
        ctx.ParentBody = "Lisa here: the pricing memo is attached for review.";

        var prompt = AttachmentPrompts.Build(ctx);

        Assert.Contains($"Attachment id: {ctx.Spec.DocId}", prompt);
        Assert.Contains($"Thread id: {AttachmentTestData.RelevantDocThreadId}", prompt);
        Assert.Contains("Subject: \"Change Order 7 pricing package\"", prompt);
        Assert.Contains("From Lisa Chen to Tom Becker; cc Raymond Harmon", prompt);
        Assert.Contains($"Sent: {MessageTime.Local(ctx.Parent):yyyy-MM-dd HH:mm}", prompt);
        Assert.Contains("Lisa here: the pricing memo is attached for review.", prompt);
        Assert.Contains("Title: Change Order Pricing Memo", prompt);
        Assert.Contains("Description: Change Order Pricing Memo for the thread.", prompt);
        Assert.Contains("File name: Change Order Pricing Memo.docx", prompt);
        Assert.Contains("CH-004 Lisa Chen", prompt);
    }

    // ---------- richness (D-15) ----------

    [Fact]
    public void Richness_UsesTheSharedRule_AndSizesByKind()
    {
        var richWord = Context(AttachmentTestData.RelevantDocThreadId, AttachmentType.Word);
        var plainWord = Context(AttachmentTestData.MultiMessageThreadId, AttachmentType.Word);
        Assert.True(richWord.Rich);
        Assert.False(plainWord.Rich);
        Assert.Contains("1-3 pages", AttachmentPrompts.Build(richWord));
        Assert.Contains("3-6 headed sections", AttachmentPrompts.Build(richWord));
        Assert.Contains("about half a page", AttachmentPrompts.Build(plainWord));
        Assert.Contains("1-2 sections", AttachmentPrompts.Build(plainWord));

        Assert.Contains("10-40 rows", AttachmentPrompts.Build(Context(AttachmentTestData.RelevantDocThreadId, AttachmentType.Excel)));
        Assert.Contains("4-10 rows", AttachmentPrompts.Build(Context(AttachmentTestData.MultiMessageThreadId, AttachmentType.Excel, 2)));

        var deck = Context(AttachmentTestData.RelevantDocThreadId, AttachmentType.PowerPoint);
        Assert.Contains("4-8 slides", AttachmentPrompts.Build(deck));
        deck.Rich = false;
        Assert.Contains("2-3 slides", AttachmentPrompts.Build(deck));

        Assert.False(AttachmentPrompts.IsRich(Context(AttachmentTestData.DailyBusinessThreadId, AttachmentType.Word)));
    }

    // ---------- chains (D-16) ----------

    [Fact]
    public void Chain_Version2_ShowsPriorTextCut_OrSaysNotAvailable()
    {
        var ctx = Context(AttachmentTestData.ChainThreadId, AttachmentType.Excel);
        ctx.Chain = new AttachmentChainLink { ChainId = "ORG-02|Excel|co-007 pricing backup", Version = 2, Count = 2, PreviousDocId = "X" };
        ctx.PriorVersionText = string.Join(" ", Enumerable.Range(0, 2_000).Select(i => $"w{i}"));

        var prompt = AttachmentPrompts.Build(ctx);

        Assert.Contains("version 2 of 2", prompt);
        Assert.Contains("w1499", prompt);
        Assert.DoesNotContain("w1500", prompt);
        Assert.DoesNotContain("earlier draft not available", prompt);

        ctx.PriorVersionText = string.Empty;
        var missing = AttachmentPrompts.Build(ctx);
        Assert.Contains("version 2 of 2", missing);
        Assert.Contains("earlier draft not available", missing);
    }

    // ---------- invites (D-20) ----------

    [Fact]
    public void Invite_StatesCodeSlot_AndAsksOnlyForTitleAgendaLocation()
    {
        var ctx = Context(AttachmentTestData.InviteThreadId, AttachmentType.CalendarInvite);
        var offset = TimeSpan.FromHours(-5);
        ctx.Slot = new InviteSlot
        {
            Start = new DateTimeOffset(2024, 3, 12, 10, 30, 0, offset),
            End = new DateTimeOffset(2024, 3, 12, 11, 0, 0, offset)
        };

        var prompt = AttachmentPrompts.Build(ctx);

        Assert.Contains("Tue, Mar 12, 2024 10:30 AM - 11:00 AM", prompt);
        Assert.Contains("Write only the meeting title, a short agenda and a location", prompt);
        Assert.Contains("Attendees (fixed): Tom Becker, Maria Delgado", prompt);
        Assert.DoesNotContain("Length:", prompt);
    }

    // ---------- filler and decoy (D-13, NOISE-01) ----------

    [Fact]
    public void Filler_HasNoCaseContext_AndListsSenderSideProjects()
    {
        var ctx = Context(AttachmentTestData.DailyBusinessThreadId, AttachmentType.Word);
        ctx.SideWorld = new SideWorld
        {
            Organizations = new()
            {
                new() { OrganizationId = "ORG-01", Projects = new() { new() { Name = "Lakeview Tower glazing", Topics = new() { "shop drawings", "mockup" } } } },
                new() { OrganizationId = "ORG-02", Projects = new() { new() { Name = "Other org project" } } }
            }
        };

        var prompt = AttachmentPrompts.Build(ctx);

        Assert.False(ctx.Relevant);
        Assert.Contains("Ongoing work:", prompt);
        Assert.Contains("Lakeview Tower glazing: shop drawings, mockup", prompt);
        Assert.DoesNotContain("Other org project", prompt);
        Assert.DoesNotContain("What they know as of", prompt);
        Assert.DoesNotContain("Exact values", prompt);
        Assert.DoesNotContain("Earlier details", prompt);
        Assert.DoesNotContain("newDetails", prompt);
        Assert.Equal(AttachmentPrompts.FillerSystemPrompt, AttachmentRenderer.BuildRequest(ctx, 1, null).SystemPrompt);
    }

    [Fact]
    public void Decoy_ListsAngleAndEveryMustNotMentionTerm()
    {
        var ctx = Context(AttachmentTestData.DecoyThreadId, AttachmentType.Word);

        var prompt = AttachmentPrompts.Build(ctx);

        Assert.True(ctx.IsDecoy);
        Assert.Contains("Angle: Mentions change orders in general", prompt);
        Assert.Contains($"Do not mention: {AttachmentTestData.DecoyFactTitle}; {AttachmentTestData.DecoyAmount}", prompt);
        Assert.DoesNotContain("What they know as of", prompt);
        Assert.DoesNotContain("Ongoing work:", prompt);
    }

    [Fact]
    public void Feedback_IsLast()
    {
        var ctx = Context(AttachmentTestData.RelevantDocThreadId, AttachmentType.Word);
        var feedback = new List<RenderViolation>
        {
            new() { Code = "V3", Seq = 1, Text = "uses $1,240,000 for Contract value; the case value is $1,250,000" },
            new() { Code = "V2", Seq = 1, Text = "names \"Gerald Fontaine\", who is not in the cast" }
        };

        var prompt = AttachmentPrompts.Build(ctx, feedback).TrimEnd();
        var first = AttachmentPrompts.Build(ctx);

        Assert.StartsWith(first.TrimEnd(), prompt);
        Assert.Contains("Problems in the previous attempt", prompt);
        Assert.Contains("- uses $1,240,000 for Contract value; the case value is $1,250,000", prompt);
        Assert.EndsWith("- names \"Gerald Fontaine\", who is not in the cast", prompt);
    }

    [Fact]
    public void BuildRequest_UsesTierSystemPromptAndAttemptName()
    {
        var ctx = Context(AttachmentTestData.RelevantDocThreadId, AttachmentType.Word);

        var first = AttachmentRenderer.BuildRequest(ctx, 1, null);
        var second = AttachmentRenderer.BuildRequest(ctx, 2, new List<RenderViolation> { new() { Text = "fix" } });

        Assert.Equal(LlmTier.Story, first.Tier);
        Assert.Equal(AttachmentPrompts.SystemPrompt, first.SystemPrompt);
        Assert.Equal($"Generate Corpus - {ctx.Spec.DocId}", first.OperationName);
        Assert.Equal($"Generate Corpus - {ctx.Spec.DocId} attempt 2", second.OperationName);
        Assert.Equal(AttachmentRenderer.MaxOutputTokens, first.MaxOutputTokens);
        Assert.EndsWith("- fix", second.UserPrompt.TrimEnd());
        // D-07/D-10: only the first attempt is batch-eligible
        Assert.True(first.AllowBatch);
        Assert.False(second.AllowBatch);
    }

    // ---------- images (D-27, Pitfall 12) ----------

    private static List<string> NameTokens(AttachmentRenderContext ctx) =>
        ctx.Thread.People.KnownNameTokens
            .Concat(ctx.Thread.Guide.Organizations.SelectMany(o => new[] { o.Name, o.ShortName }))
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .ToList();

    [Fact]
    public void ImagePrompt_HasSceneAndSetting_NoNames_EndsWithNoTextRule()
    {
        var ctx = Context(AttachmentTestData.InviteThreadId, AttachmentType.Image);
        ctx.Spec.Description = "Curtain wall mockup photo for Lisa Chen at Harmon Builders, Inc.";

        var prompt = AttachmentPrompts.ImagePrompt(ctx);

        Assert.Contains("Curtain wall mockup photo", prompt);
        Assert.Contains("Phoenix", prompt);
        Assert.Contains("documentary photo", prompt);
        Assert.EndsWith(NoText, prompt);
        Assert.All(NameTokens(ctx), token => Assert.False(RenderValidator.ContainsWord(prompt, token), $"{token} in: {prompt}"));
    }

    [Fact]
    public void ImagePrompt_Spam_IsAnAbstractMarketingGraphic()
    {
        var ctx = Context(AttachmentTestData.SpamThreadId, AttachmentType.Image);

        var prompt = AttachmentPrompts.ImagePrompt(ctx);

        Assert.StartsWith("an abstract marketing graphic", prompt);
        Assert.Contains("lift rental", prompt);
        Assert.EndsWith(NoText, prompt);
        Assert.All(NameTokens(ctx), token => Assert.False(RenderValidator.ContainsWord(prompt, token), $"{token} in: {prompt}"));
    }

    // ---------- voicemail transcript (D-28) ----------

    [Fact]
    public void ThreadPrompt_SingleMessageVoicemail_AsksForASpokenTranscript()
    {
        var voicemail = Context(AttachmentTestData.RelevantVoicemailThreadId, AttachmentType.Voicemail).Thread;
        var multi = Context(AttachmentTestData.MultiMessageThreadId, AttachmentType.Word).Thread;

        var prompt = EvidencePrompts.BuildThreadPrompt(voicemail);
        var line = prompt.Split('\n').Single(l => l.StartsWith("msg 1 |", StringComparison.Ordinal));

        Assert.Contains("voicemail: write what Raymond Harmon says on the phone, 20-90 words, spoken style, no greeting line or sign-off", line);
        Assert.DoesNotContain("length:", line);
        Assert.Equal("voicemail: write what Raymond Harmon says on the phone, 20-90 words, spoken style, no greeting line or sign-off",
            EvidencePrompts.VoicemailInstruction("Raymond Harmon"));
        Assert.All(EvidencePrompts.BuildThreadPrompt(multi).Split('\n').Where(l => l.StartsWith("msg ", StringComparison.Ordinal)),
            l => Assert.Contains("| length: ", l));
    }
}
