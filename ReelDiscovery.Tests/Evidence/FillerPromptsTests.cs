using ReelDiscovery.Models;
using ReelDiscovery.Models.Case;
using ReelDiscovery.Models.Corpus;
using ReelDiscovery.Models.Evidence;
using ReelDiscovery.Services.Ai;
using ReelDiscovery.Services.Evidence;

namespace ReelDiscovery.Tests.Evidence;

public class FillerPromptsTests
{
    private const string FormattingSentence = "Formatting is optional and modest:";

    private static ThreadRenderContext Context(string threadId, out CaseDraft draft, out CorpusPlan plan)
    {
        (draft, plan) = AttachmentTestData.MixedCase();
        var thread = AttachmentTestData.Thread(plan, threadId);
        var people = PeopleDirectory.Create(draft.Guide!, plan);
        var assignment = FillerAssignments.Compute(plan)[threadId];
        return ThreadRenderContext.Create(draft, plan, thread, assignment, people, new List<LedgerEntry>(), 1);
    }

    private static ThreadRenderContext Context(string threadId) => Context(threadId, out _, out _);

    private static string Prompt(string threadId, IReadOnlyList<RenderViolation>? feedback = null)
    {
        var ctx = Context(threadId);
        return FillerPrompts.BuildThreadPrompt(ctx, SideWorldBuilder.Fallback(ctx.Guide), feedback);
    }

    private static List<string> Lines(string prompt) =>
        prompt.Split('\n').Select(l => l.TrimEnd('\r')).ToList();

    private static List<string> MessageLines(string prompt) =>
        Lines(prompt).Where(l => l.StartsWith("msg ", StringComparison.Ordinal)).ToList();

    [Fact]
    public void SystemPrompt_KeepsNamesAndSharesFormattingAllowance()
    {
        Assert.Contains("including characters and companies from films, TV series, books or games", FillerPrompts.SystemPrompt);
        Assert.DoesNotContain("fictional", FillerPrompts.SystemPrompt, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Take a firm position", FillerPrompts.SystemPrompt);
        Assert.Contains("These threads are ordinary correspondence that has nothing to do with any dispute.", FillerPrompts.SystemPrompt);
        Assert.Contains("For each planned attachment, give a short, realistic document title in attachmentTitles.", FillerPrompts.SystemPrompt);

        var evidence = EvidencePrompts.SystemPrompt;
        var allowance = evidence[evidence.IndexOf(FormattingSentence, StringComparison.Ordinal)..];
        Assert.EndsWith(allowance, FillerPrompts.SystemPrompt);
    }

    [Fact]
    public void DailyBusiness_ListsOngoingWorkOfParticipantOrganizations_NoCaseContext()
    {
        var ctx = Context(AttachmentTestData.DailyBusinessThreadId);
        var world = SideWorldBuilder.Fallback(ctx.Guide);

        var prompt = FillerPrompts.BuildThreadPrompt(ctx, world);

        Assert.Contains("Ongoing work:", prompt);
        var organizations = ctx.Thread.Messages
            .SelectMany(m => new[] { m.FromId }.Concat(m.ToIds).Concat(m.CcIds))
            .Select(id => ctx.People.Find(id))
            .Where(p => p != null && !p.IsExternal)
            .Select(p => ctx.Guide.FindOrganization(p!.OrganizationId))
            .Where(o => o != null)
            .DistinctBy(o => o!.Id)
            .ToList();
        Assert.NotEmpty(organizations);
        foreach (var organization in organizations)
        {
            foreach (var project in SideWorldBuilder.ProjectsFor(world, organization!, ctx.Guide))
                Assert.Contains($"{organization!.Name.Trim()}: {project.Name}", prompt);
        }

        Assert.DoesNotContain("What they know as of", prompt);
        Assert.DoesNotContain("Exact values they may use", prompt);
        Assert.DoesNotContain("Earlier details from previous emails", prompt);
        Assert.DoesNotContain("newDetails", prompt);
    }

    [Fact]
    public void OngoingWork_OrganizationMissingFromWorld_UsesValidatedTemplates()
    {
        var ctx = Context(AttachmentTestData.AncillaryThreadId);

        var prompt = FillerPrompts.BuildThreadPrompt(ctx, new SideWorld());

        var lines = Lines(prompt);
        var start = lines.IndexOf("Ongoing work:");
        Assert.True(start >= 0);
        Assert.Contains(":", lines[start + 1]);
        Assert.DoesNotContain("(routine day-to-day work)", prompt);
    }

    [Fact]
    public void MessageLines_ShareEvidenceShape_WithFillerLengthBandsAndTitleRequests()
    {
        var ctx = Context(AttachmentTestData.DailyBusinessThreadId);

        var lines = MessageLines(FillerPrompts.BuildThreadPrompt(ctx, null));

        var planned = ctx.Thread.Messages.OrderBy(m => m.Seq).ToList();
        Assert.Equal(planned.Count, lines.Count);
        for (var i = 0; i < planned.Count; i++)
        {
            var message = planned[i];
            var band = RenderAssignments.Spec(ctx.Assignment.For(message.Seq)!.Length);
            Assert.StartsWith($"msg {message.Seq} | {message.Kind}", lines[i]);
            Assert.Contains($" | from {ctx.People.NameOf(message.FromId)} to ", lines[i]);
            Assert.Contains($" | {MessageTime.Local(message):yyyy-MM-dd HH:mm} | ", lines[i]);
            Assert.Contains($" | length: {band.MinWords}-{band.MaxWords} words", lines[i]);
            Assert.DoesNotContain("subtlety:", lines[i]);
            Assert.DoesNotContain("may mention:", lines[i]);
        }

        Assert.EndsWith(" | attachments: Word document (title needed)", lines[0]);
        Assert.DoesNotContain("attachments:", lines[1]);
    }

    [Fact]
    public void Voicemail_UsesVoicemailInstructionInsteadOfLength()
    {
        var ctx = Context(AttachmentTestData.NonRelevantVoicemailThreadId);

        var line = Assert.Single(MessageLines(FillerPrompts.BuildThreadPrompt(ctx, null)));

        var root = ctx.Thread.Messages.Single();
        Assert.Contains(EvidencePrompts.VoicemailInstruction(ctx.People.NameOf(root.FromId)), line);
        Assert.DoesNotContain("length:", line);
        Assert.Contains("attachments: voicemail recording \"Voicemail from Owen Reilly\"", line);
    }

    [Fact]
    public void Decoy_CarriesAngleSharedNamesAndMustNotMention()
    {
        var ctx = Context(AttachmentTestData.DecoyThreadId);

        var prompt = FillerPrompts.BuildThreadPrompt(ctx, SideWorldBuilder.Fallback(ctx.Guide));
        var lines = Lines(prompt);

        Assert.Contains($"Angle: {ctx.Thread.DecoyAngle}", lines);
        var avoid = Assert.Single(lines, l => l.StartsWith("Do not mention: ", StringComparison.Ordinal));
        Assert.Contains(AttachmentTestData.DecoyFactTitle, avoid);
        Assert.Contains(AttachmentTestData.DecoyAmount, avoid);
        var shared = Assert.Single(lines, l => l.StartsWith("Shared names you may use:", StringComparison.Ordinal));
        var sender = ctx.People.Find(ctx.Thread.Messages[0].FromId)!;
        Assert.Contains(ctx.Guide.FindOrganization(sender.OrganizationId)!.Name.Trim(), shared);
        Assert.DoesNotContain(AttachmentTestData.DecoyFactTitle, shared);
        Assert.DoesNotContain("Ongoing work:", prompt);
        Assert.DoesNotContain("What they know as of", prompt);
    }

    [Fact]
    public void BulkMail_StyleLineFollowsSenderKind()
    {
        var personal = Prompt(AttachmentTestData.PersonalThreadId);
        var spam = Prompt(AttachmentTestData.SpamThreadId);
        var newsletter = Prompt(AttachmentTestData.NewsletterThreadId);

        Assert.Contains("everyday personal plans only: meals, kids, sports, errands, appointments; no health, money, account, HR or identity details", personal);
        Assert.Contains("marketing or mild phishing tone; no working links, no real brands", spam);
        Assert.Contains("2-4 short sections, each starting with a **Heading** line", newsletter);
        Assert.DoesNotContain(FillerPrompts.PersonalSafetyLine, spam);
        Assert.DoesNotContain(FillerPrompts.SpamLine, newsletter);
        Assert.DoesNotContain("Ongoing work:", personal);
    }

    [Fact]
    public void PrivilegedDailyBusiness_GetsCounselClause()
    {
        var privileged = Prompt(AttachmentTestData.DailyBusinessThreadId);
        var ordinary = Prompt(AttachmentTestData.AncillaryThreadId);

        Assert.Contains(EvidencePrompts.CounselText, privileged);
        Assert.DoesNotContain(EvidencePrompts.CounselText, ordinary);
    }

    [Fact]
    public void Feedback_IsLast_PrefixUnchanged()
    {
        var feedback = new List<RenderViolation>
        {
            new() { Code = "V1", Seq = 2, Text = "message 2 is missing" },
            new() { Code = "V5", Seq = 0, Text = "thread is empty" }
        };

        var first = Prompt(AttachmentTestData.DailyBusinessThreadId);
        var retry = Prompt(AttachmentTestData.DailyBusinessThreadId, feedback);

        Assert.StartsWith(first, retry);
        var tail = Lines(retry).Where(l => l.Length > 0).TakeLast(3).ToList();
        Assert.Equal(EvidencePrompts.FeedbackHeader, tail[0]);
        Assert.Equal("- msg 2: message 2 is missing", tail[1]);
        Assert.Equal("- thread: thread is empty", tail[2]);
        Assert.Equal(first, Prompt(AttachmentTestData.DailyBusinessThreadId, new List<RenderViolation>()));
    }

    [Fact]
    public void BuildRequest_UsesCallerTierAndFillerSystemPrompt()
    {
        var ctx = Context(AttachmentTestData.DecoyThreadId);

        var story = FillerRenderer.BuildRequest(ctx, null, LlmTier.Story, 1, null);
        var volume = FillerRenderer.BuildRequest(ctx, null, LlmTier.Volume, 2, null);

        Assert.Equal(LlmTier.Story, story.Tier);
        Assert.Equal(LlmTier.Volume, volume.Tier);
        Assert.Equal($"Generate Corpus - {AttachmentTestData.DecoyThreadId}", story.OperationName);
        Assert.Equal($"Generate Corpus - {AttachmentTestData.DecoyThreadId} attempt 2", volume.OperationName);
        Assert.Equal(FillerPrompts.SystemPrompt, story.SystemPrompt);
        Assert.Equal(FillerPrompts.BuildThreadPrompt(ctx, null), story.UserPrompt);
        Assert.Equal(ThreadRenderer.MaxOutputTokens, story.MaxOutputTokens);
        // D-07/D-10: only the first attempt is batch-eligible
        Assert.True(story.AllowBatch);
        Assert.False(volume.AllowBatch);
    }

    [Fact]
    public void ToRendered_MapsThroughRepair_TitlesCleanedCappedAndCut()
    {
        var ctx = Context(AttachmentTestData.DailyBusinessThreadId);
        var dto = new FillerRenderer.FillerThreadDto
        {
            Messages = new()
            {
                new() { Seq = 1, Body = "Please send the renewed certificate.", SignOff = "Thanks,", AttachmentTitles = new() { "Insurance\u0007 Certificate\n2024", "Extra title" } },
                new() { Seq = 2, Body = "Will do.", SignOff = "Best,", AttachmentTitles = new() { "Unplanned" } },
                new() { Seq = 3, Body = "Got it.", SignOff = string.Empty, AttachmentTitles = new() },
                new() { Seq = 9, Body = "Unknown message.", SignOff = string.Empty, AttachmentTitles = new() }
            }
        };

        var rendered = FillerRenderer.ToRendered(dto, ctx);

        Assert.Equal(ctx.Thread.Id, rendered.ThreadId);
        Assert.Equal(new[] { 1, 2, 3 }, rendered.Messages.Select(m => m.Seq));
        Assert.Empty(rendered.NewDetails);
        Assert.Contains(rendered.RepairNotes, n => n.Contains("unknown msg 9"));
        Assert.Equal(new[] { "Insurance Certificate 2024" }, rendered.Messages[0].AttachmentTitles);
        Assert.Empty(rendered.Messages[1].AttachmentTitles);
        Assert.Equal("Please send the renewed certificate.", rendered.Messages[0].Body);
        Assert.Equal("Thanks,", rendered.Messages[0].SignOff);
    }

    [Fact]
    public void ToRendered_LongAndMissingTitles_CappedOrCodeTitled()
    {
        var ctx = Context(AttachmentTestData.DailyBusinessThreadId);
        var root = ctx.Thread.Messages.Single(m => m.Seq == 1);

        var longTitle = FillerRenderer.ToRendered(new FillerRenderer.FillerThreadDto
        {
            Messages = new() { new() { Seq = 1, Body = "See attached.", AttachmentTitles = new() { new string('t', 300) } } }
        }, ctx);
        var blank = FillerRenderer.ToRendered(new FillerRenderer.FillerThreadDto
        {
            Messages = new() { new() { Seq = 1, Body = "See attached.", AttachmentTitles = new() { " \u0001 " } } }
        }, ctx);

        Assert.Equal(FillerRenderer.MaxTitleChars, longTitle.Messages[0].AttachmentTitles.Single().Length);
        Assert.Equal(AttachmentNaming.TitleFor(root.Attachments[0], root, null), blank.Messages[0].AttachmentTitles.Single());
        Assert.Empty(FillerRenderer.ToRendered(null, ctx).Messages);
    }
}
