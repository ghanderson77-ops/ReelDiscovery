using ReelDiscovery.Models;
using ReelDiscovery.Services.Corpus;
using ReelDiscovery.UserControls;

namespace ReelDiscovery.Tests.Corpus;

public class ReviewPlanLogicTests
{
    private static ReviewPlanEntryInput Input(bool running = false, bool hasPlan = true, string unreadable = "",
        bool approved = false, bool settingsChanged = false, bool settingsDismissed = false,
        bool guideStale = false, bool guideDismissed = false, bool requested = false) => new()
    {
        Running = running,
        HasPlan = hasPlan,
        UnreadableReason = unreadable,
        Approved = approved,
        SettingsChanged = settingsChanged,
        SettingsDismissed = settingsDismissed,
        GuideStale = guideStale,
        GuideDismissed = guideDismissed,
        PlanRequested = requested
    };

    private static PlanSummary Summary() => new()
    {
        Emails = 1000,
        Threads = 312,
        Custodians = 42,
        Relevant = 196,
        NonRelevant = 804,
        RelevantPercent = 19.6m,
        HotEmails = 9,
        HotFacts = 5,
        PrivilegedThreads = 4,
        PrivilegedEmails = 11,
        Attachments = 210,
        EmailsWithAttachments = 200,
        AttachmentPercent = 20m,
        AttachmentsByType = new Dictionary<AttachmentType, int>
        {
            [AttachmentType.Word] = 100,
            [AttachmentType.Excel] = 50,
            [AttachmentType.PowerPoint] = 10,
            [AttachmentType.CalendarInvite] = 0,
            [AttachmentType.Voicemail] = 1,
            [AttachmentType.Image] = 49
        }
    };

    private static CostEstimate Cost(decimal total = 130.4m) => new()
    {
        RelevantCost = 60m,
        OtherCost = 50m,
        AttachmentCost = 4.75m,
        Total = total,
        ModelName = "Claude Story"
    };

    // --- Review Plan entry rules (D-12, D-25, D-26) ---

    [Fact]
    public void Entry_RunningKeepsRun()
    {
        Assert.Equal(ReviewPlanEntry.KeepRun, CorpusUiText.ReviewPlanEntryDecision(Input(running: true, hasPlan: false)));
        Assert.Equal(ReviewPlanEntry.KeepRun, CorpusUiText.ReviewPlanEntryDecision(Input(running: true, guideStale: true, approved: true)));
    }

    [Fact]
    public void Entry_NoPlanRequested_AutoPlans()
    {
        // Next on Corpus Settings asked for planning (D-12, 09 W2)
        Assert.Equal(ReviewPlanEntry.AutoPlan, CorpusUiText.ReviewPlanEntryDecision(Input(hasPlan: false, requested: true)));
    }

    [Fact]
    public void Entry_NoPlanNotRequested_ShowsCard()
    {
        // Back from Generate Corpus, Case Summary's Review Plan button and a reopened case start no paid run (09 W2)
        Assert.Equal(ReviewPlanEntry.NoPlanCard, CorpusUiText.ReviewPlanEntryDecision(Input(hasPlan: false)));
    }

    [Fact]
    public void Entry_UnreadablePlanShowsCard()
    {
        Assert.Equal(ReviewPlanEntry.UnreadableCard, CorpusUiText.ReviewPlanEntryDecision(Input(hasPlan: false, unreadable: "The file is damaged")));
        Assert.Equal(ReviewPlanEntry.UnreadableCard, CorpusUiText.ReviewPlanEntryDecision(Input(hasPlan: false, unreadable: "bad", requested: true)));
    }

    [Fact]
    public void Entry_UnapprovedSettingsChangedAutoReplans()
    {
        Assert.Equal(ReviewPlanEntry.AutoReplan, CorpusUiText.ReviewPlanEntryDecision(Input(settingsChanged: true, requested: true)));
        // Without a planning request the settings banner shows instead (09 W2)
        Assert.Equal(ReviewPlanEntry.SettingsBanner, CorpusUiText.ReviewPlanEntryDecision(Input(settingsChanged: true)));
    }

    [Fact]
    public void Entry_ReopenedUnapprovedSettingsChanged_ShowsBannerNotReplan()
    {
        // CR-02: a case opened from disk never starts a (billed) re-plan by itself (T-04-62, D-25)
        var input = Input(settingsChanged: true);
        Assert.Equal(ReviewPlanEntry.SettingsBanner, CorpusUiText.ReviewPlanEntryDecision(input));
        Assert.False(CorpusUiText.WithdrawsApproval(input));
    }

    [Fact]
    public void Entry_ReopenedUnapprovedSettingsChangedDismissed_ShowsPlan()
    {
        Assert.Equal(ReviewPlanEntry.ShowPlan,
            CorpusUiText.ReviewPlanEntryDecision(Input(settingsChanged: true, settingsDismissed: true)));
    }

    [Fact]
    public void Entry_ReopenedUnapprovedStaleAndSettingsChanged_StaleWins()
    {
        Assert.Equal(ReviewPlanEntry.StaleBanner,
            CorpusUiText.ReviewPlanEntryDecision(Input(settingsChanged: true, guideStale: true)));
    }

    [Fact]
    public void Entry_ApprovedSettingsChangedShowsBanner()
    {
        Assert.Equal(ReviewPlanEntry.SettingsBanner, CorpusUiText.ReviewPlanEntryDecision(Input(approved: true, settingsChanged: true, requested: true)));
    }

    [Fact]
    public void Entry_ApprovedSettingsChangedDismissedShowsPlan()
    {
        Assert.Equal(ReviewPlanEntry.ShowPlan, CorpusUiText.ReviewPlanEntryDecision(Input(approved: true, settingsChanged: true, settingsDismissed: true)));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Entry_GuideStaleShowsStaleBanner(bool approved)
    {
        var input = Input(approved: approved, guideStale: true);
        Assert.Equal(ReviewPlanEntry.StaleBanner, CorpusUiText.ReviewPlanEntryDecision(input));
        Assert.Equal(approved, CorpusUiText.WithdrawsApproval(input));
    }

    [Fact]
    public void Entry_GuideStaleDismissedShowsPlan()
    {
        var input = Input(approved: true, guideStale: true, guideDismissed: true);
        Assert.Equal(ReviewPlanEntry.ShowPlan, CorpusUiText.ReviewPlanEntryDecision(input));
        Assert.False(CorpusUiText.WithdrawsApproval(input));
    }

    [Fact]
    public void Entry_GuideStaleWinsOverSettingsChangedWhenApproved()
    {
        var input = Input(approved: true, settingsChanged: true, guideStale: true);
        Assert.Equal(ReviewPlanEntry.StaleBanner, CorpusUiText.ReviewPlanEntryDecision(input));
        Assert.True(CorpusUiText.WithdrawsApproval(input));
    }

    [Fact]
    public void Entry_StaleDismissedFallsBackToSettingsBanner()
    {
        var input = Input(approved: true, settingsChanged: true, guideStale: true, guideDismissed: true);
        Assert.Equal(ReviewPlanEntry.SettingsBanner, CorpusUiText.ReviewPlanEntryDecision(input));
    }

    [Fact]
    public void Entry_CleanPlanShows()
    {
        Assert.Equal(ReviewPlanEntry.ShowPlan, CorpusUiText.ReviewPlanEntryDecision(Input()));
        Assert.Equal(ReviewPlanEntry.ShowPlan, CorpusUiText.ReviewPlanEntryDecision(Input(approved: true)));
        Assert.False(CorpusUiText.WithdrawsApproval(Input(approved: true)));
    }

    // --- Banner ---

    [Fact]
    public void Banner_StaleGuide()
    {
        var banner = CorpusUiText.PlanBannerText(PlanBannerVariant.StaleGuide, 1000, 20);

        Assert.NotNull(banner);
        Assert.Equal(PlanBannerVariant.StaleGuide, banner!.Variant);
        Assert.Equal("The plan is out of date with the case guide.", banner.Heading);
        Assert.Equal("The case guide changed after this plan was built. Re-plan builds a new plan from the current guide. Approval is withdrawn until you re-plan, or keep this plan and approve it again.", banner.Body);
        Assert.Equal("Re-plan...", banner.PrimaryText);
        Assert.Equal("Keep This Plan", banner.SecondaryText);
    }

    [Theory]
    [InlineData(1000, 20, "1,000 emails, 20% relevant")]
    [InlineData(1, 5, "1 email, 5% relevant")]
    public void Banner_SettingsChanged(int emails, int percent, string settings)
    {
        var banner = CorpusUiText.PlanBannerText(PlanBannerVariant.SettingsChanged, emails, percent);

        Assert.NotNull(banner);
        Assert.Equal("The corpus settings changed after this plan was approved.", banner!.Heading);
        Assert.Equal($"This plan still uses the earlier settings ({settings}). Re-plan builds a new plan with the current settings. Keep This Plan leaves the approved plan and its answer key as they are.", banner.Body);
        Assert.Equal("Re-plan...", banner.PrimaryText);
        Assert.Equal("Keep This Plan", banner.SecondaryText);
    }

    [Fact]
    public void Banner_SettingsChanged_UnapprovedWording()
    {
        var banner = CorpusUiText.PlanBannerText(PlanBannerVariant.SettingsChanged, 1000, 20, approved: false);

        Assert.NotNull(banner);
        Assert.Equal(PlanBannerVariant.SettingsChanged, banner!.Variant);
        Assert.Equal("The corpus settings changed after this plan was built.", banner.Heading);
        Assert.DoesNotContain("approved", banner.Body);
        Assert.Contains("1,000 emails, 20% relevant", banner.Body);
    }

    [Fact]
    public void Banner_NoneIsNull()
    {
        Assert.Null(CorpusUiText.PlanBannerText(PlanBannerVariant.None, 1000, 20));
    }

    // --- Tiles ---

    [Fact]
    public void Tile_Emails()
    {
        var tile = CorpusUiText.Tile(TileKind.Emails, Summary(), Cost());
        Assert.Equal("Emails", tile.Caption);
        Assert.Equal("1,000", tile.Value);
        Assert.Equal("42 custodians", tile.SubLine);
        Assert.Equal("1,000 planned emails across 42 custodian mailboxes", tile.Tooltip);
    }

    [Fact]
    public void Tile_Threads()
    {
        var tile = CorpusUiText.Tile(TileKind.Threads, Summary(), Cost());
        Assert.Equal("Threads", tile.Caption);
        Assert.Equal("312", tile.Value);
        Assert.Equal("avg 3.2 emails", tile.SubLine);
        Assert.Equal("", tile.Tooltip);
    }

    [Fact]
    public void Tile_Relevant_OneDecimalOnlyWhenNotWhole()
    {
        var tile = CorpusUiText.Tile(TileKind.Relevant, Summary(), Cost());
        Assert.Equal("Relevant", tile.Caption);
        Assert.Equal("19.6%", tile.Value);
        Assert.Equal("196 emails", tile.SubLine);
        Assert.Equal("196 relevant and 804 non-relevant emails", tile.Tooltip);

        var whole = Summary();
        whole.RelevantPercent = 20m;
        whole.Relevant = 1;
        Assert.Equal("20%", CorpusUiText.Tile(TileKind.Relevant, whole, Cost()).Value);
        Assert.Equal("1 email", CorpusUiText.Tile(TileKind.Relevant, whole, Cost()).SubLine);
    }

    [Fact]
    public void Tile_HotAndPrivileged()
    {
        var hot = CorpusUiText.Tile(TileKind.Hot, Summary(), Cost());
        Assert.Equal("Hot", hot.Caption);
        Assert.Equal("9", hot.Value);
        Assert.Equal("5 hot facts", hot.SubLine);
        Assert.Equal("Hot documents follow the facts marked Hot in the case guide.", hot.Tooltip);

        var privileged = CorpusUiText.Tile(TileKind.Privileged, Summary(), Cost());
        Assert.Equal("Privileged", privileged.Caption);
        Assert.Equal("4", privileged.Value);
        Assert.Equal("11 emails", privileged.SubLine);
        Assert.Equal("Privileged threads include in-house or outside counsel.", privileged.Tooltip);
    }

    [Fact]
    public void Tile_Attachments_ListsOnlyTypesWithCounts()
    {
        var tile = CorpusUiText.Tile(TileKind.Attachments, Summary(), Cost());
        Assert.Equal("Attachments", tile.Caption);
        Assert.Equal("210", tile.Value);
        Assert.Equal("on 20% of emails", tile.SubLine);
        Assert.Equal("160 documents, 49 images, 1 voicemail", tile.Tooltip);
    }

    [Fact]
    public void Tile_Cost_RoundingAndTooltip()
    {
        var tile = CorpusUiText.Tile(TileKind.Cost, Summary(), Cost(130.4m));
        Assert.Equal("Est. cost", tile.Caption);
        Assert.Equal("≈$130", tile.Value);
        Assert.Equal("to generate, ±50%", tile.SubLine);
        Assert.Equal("Rough estimate for writing this corpus on Claude Story: relevant email $60, other email $50, attachments $4.75. Images and voicemail are billed separately by OpenAI. Planning cost so far is in the cost ticker.", tile.Tooltip);

        Assert.Equal("≈$4.75", CorpusUiText.Tile(TileKind.Cost, Summary(), Cost(4.75m)).Value);
        Assert.Equal("≈$1,250", CorpusUiText.Tile(TileKind.Cost, Summary(), Cost(1250m)).Value);
    }

    // --- Stages and progress ---

    [Theory]
    [InlineData(PlanStage.Allocating, "Allocating threads to the key facts")]
    [InlineData(PlanStage.PlanningRelevant, "Planning relevant threads")]
    [InlineData(PlanStage.PlanningOther, "Planning other email")]
    [InlineData(PlanStage.WritingProtocol, "Writing the review protocol")]
    [InlineData(PlanStage.Validating, "Checking the plan")]
    public void StageName_PerStage(PlanStage stage, string expected)
    {
        Assert.Equal(expected, CorpusUiText.StageName(stage));
    }

    [Theory]
    [InlineData(PlanStage.Allocating, 0, 0, "Allocating threads to the key facts...")]
    [InlineData(PlanStage.PlanningRelevant, 6, 17, "Planning relevant threads (chunk 6 of 17)...")]
    [InlineData(PlanStage.PlanningOther, 2, 9, "Planning other email (batch 2 of 9)...")]
    [InlineData(PlanStage.WritingProtocol, 1, 1, "Writing the review protocol...")]
    [InlineData(PlanStage.Validating, 0, 0, "Checking the plan...")]
    [InlineData(PlanStage.PlanningRelevant, 0, 0, "Planning relevant threads...")]
    public void ProgressText_PerStage(PlanStage stage, int index, int total, string expected)
    {
        Assert.Equal(expected, CorpusUiText.ProgressText(stage, index, total));
    }

    [Fact]
    public void StageProgress_Text()
    {
        Assert.Equal("6 of 17", CorpusUiText.StageProgress(6, 17));
        Assert.Equal("1,000 of 1,200", CorpusUiText.StageProgress(1000, 1200));
    }

    // --- State cards ---

    [Fact]
    public void StateCard_Planning()
    {
        var card = CorpusUiText.StateCard(PlanStateCard.Planning);
        Assert.Equal("Planning the corpus...", card.Heading);
        Assert.Equal("Code fixes every email's date, people and labels first. Then the model writes a subject and purpose for each thread. This takes 1-2 minutes for 1,000 emails and up to 10 minutes for 5,000.", card.Body);
        Assert.Equal("", card.ButtonText);
        Assert.Equal("", card.SecondaryText);
    }

    [Fact]
    public void StateCard_NoPlan()
    {
        var card = CorpusUiText.StateCard(PlanStateCard.NoPlan);
        Assert.Equal("No corpus plan yet", card.Heading);
        Assert.Equal("Click Plan Corpus to plan every thread and message from the case guide and your corpus settings. To write discovery documents without emails, click Skip to Discovery Documents.", card.Body);
        Assert.Equal("Plan Corpus", card.ButtonText);
        Assert.Equal("Skip to Discovery Documents", card.SecondaryText);
    }

    [Theory]
    [InlineData("The model timed out")]
    [InlineData("The model timed out.")]
    public void StateCard_Failed_DoesNotDoublePeriod(string reason)
    {
        var card = CorpusUiText.StateCard(PlanStateCard.Failed, reason);
        Assert.Equal("The plan was not built", card.Heading);
        Assert.Equal("The model timed out. Your corpus settings were kept. Click Retry Planning to try again, or Skip to Discovery Documents to continue without emails.", card.Body);
        Assert.Equal("Retry Planning", card.ButtonText);
        Assert.Equal("Skip to Discovery Documents", card.SecondaryText);
    }

    [Fact]
    public void StateCard_Unreadable()
    {
        var card = CorpusUiText.StateCard(PlanStateCard.Unreadable, "The file is damaged.");
        Assert.Equal("The saved plan could not be opened", card.Heading);
        Assert.Equal("The file is damaged. Click Plan Corpus to build a new plan from the case guide, or Skip to Discovery Documents to continue without emails.", card.Body);
        Assert.Equal("Skip to Discovery Documents", card.SecondaryText);
        Assert.Equal("Plan Corpus", card.ButtonText);
    }

    // --- Checks ---

    [Theory]
    [InlineData(0, "Checks")]
    [InlineData(1, "Checks (1 warning)")]
    [InlineData(2, "Checks (2 warnings)")]
    [InlineData(5, "Checks (5 warnings)")]
    [InlineData(1000, "Checks (1,000 warnings)")]
    public void ChecksCaption_Text(int warnings, string expected)
    {
        Assert.Equal(expected, CorpusUiText.ChecksCaption(warnings));
    }

    [Fact]
    public void ChecksConstants()
    {
        Assert.Equal("No problems found. Every key fact has planned evidence, and every date and participant checks out.", CorpusUiText.NoChecksText);
        Assert.Equal("Warnings never block Approve Plan.", CorpusUiText.ChecksHint);
    }

    // --- Outcomes ---

    [Fact]
    public void Outcome_Ready()
    {
        Assert.Equal("Plan ready: 1,000 emails in 312 threads, 200 relevant. Review it, then click Approve Plan.", CorpusUiText.ReadyText(1000, 312, 200));
        Assert.Equal("Plan ready: 1 email in 1 thread, 1 relevant. Review it, then click Approve Plan.", CorpusUiText.ReadyText(1, 1, 1));
    }

    [Theory]
    [InlineData(1, "Plan ready with 1 warning. Review the Checks list, then click Approve Plan.")]
    [InlineData(5, "Plan ready with 5 warnings. Review the Checks list, then click Approve Plan.")]
    public void Outcome_ReadyWithWarnings(int warnings, string expected)
    {
        Assert.Equal(expected, CorpusUiText.ReadyWithWarningsText(warnings));
    }

    [Fact]
    public void Outcome_CancelledFatalRefused()
    {
        Assert.Equal("Planning cancelled. Your corpus settings were kept.", CorpusUiText.CancelledText(false));
        Assert.Equal("Planning cancelled. The previous plan is still in place.", CorpusUiText.CancelledText(true));
        Assert.Equal("Planning stopped: bad key. Fix the problem on the API Configuration step, then click Retry Planning.", CorpusUiText.FatalText("bad key"));
        Assert.Equal("Planning stopped: bad key. Fix the problem on the API Configuration step, then click Retry Planning.", CorpusUiText.FatalText("bad key."));
        Assert.Equal("Planning stopped: the model declined 3 of 17 thread batches (policy). Click Retry Planning, or Re-plan with guidance that softens the wording.", CorpusUiText.RefusedText(3, 17, "policy"));
    }

    [Fact]
    public void Outcome_Approval()
    {
        Assert.Equal("Plan approved. Wrote answer-key.csv, answer-key.xlsx and Brightline v. Harmon - Review Protocol.docx to the case folder.", CorpusUiText.ApprovedText("Brightline v. Harmon"));
        Assert.Equal("Close answer-key.xlsx, then click Approve Plan again.", CorpusUiText.ApproveFileOpenText("answer-key.xlsx"));
        Assert.Equal("Could not write the answer key: disk full. Check that the case folder is writable, then click Approve Plan again.", CorpusUiText.ApproveFailedText("disk full"));
        Assert.Equal("Kept this plan. Click Approve Plan to write the answer key again.", CorpusUiText.KeptStaleText);
        Assert.Equal("Writing the answer key and review protocol...", CorpusUiText.ApprovingText);
    }

    // --- Next gate and confirmations ---

    [Fact]
    public void NextGate_Running_Disabled()
    {
        Assert.Equal(PlanNextGateKind.Disabled, CorpusUiText.PlanNextGate(true, true, true).Kind);
        Assert.Equal(PlanNextGateKind.Disabled, CorpusUiText.PlanNextGate(true, false, false).Kind);
    }

    [Fact]
    public void NextGate_NoPlan_AsksToSkip()
    {
        var gate = CorpusUiText.PlanNextGate(false, false, false);
        Assert.Equal(PlanNextGateKind.AskSkip, gate.Kind);
        Assert.Equal("Continue Without a Plan", gate.Title);
        Assert.Equal("Continue without a corpus plan? Generate Corpus needs an approved plan before it writes emails; click Next there to write discovery documents. You can plan the corpus later from Review Plan.", gate.Message);
    }

    [Fact]
    public void NextGate_NotApproved_AsksApprove()
    {
        var gate = CorpusUiText.PlanNextGate(false, true, false);
        Assert.Equal(PlanNextGateKind.AskApprove, gate.Kind);
        Assert.Equal("Approve the Plan", gate.Title);
        Assert.Equal("Approve this plan and continue? Approve Plan writes the answer key (CSV and Excel) and the review protocol to the case folder.", gate.Message);
    }

    [Fact]
    public void NextGate_NotApproved_StaleGuide_AsksToSkipWithoutApproving()
    {
        // 09 CR-01: Next must never approve (and silently keep) a plan built from an earlier guide
        var gate = CorpusUiText.PlanNextGate(false, true, false, guideStale: true);
        Assert.Equal(PlanNextGateKind.AskSkip, gate.Kind);
        Assert.Equal("Continue Without Approving", gate.Title);
        Assert.Contains("can't be approved from here", gate.Message);
    }

    [Fact]
    public void NextGate_Approved_Proceeds()
    {
        Assert.Equal(PlanNextGateKind.Proceed, CorpusUiText.PlanNextGate(false, true, true).Kind);
    }

    [Fact]
    public void Confirmations_CP1_CP2()
    {
        Assert.Equal(("Cancel Planning", "Stop planning? The partial plan is discarded. Your corpus settings are kept."), CorpusUiText.CP1CancelPlanning(false));
        Assert.Equal(("Cancel Planning", "Stop planning? The partial plan is discarded. Your corpus settings are kept. The previous plan stays in place."), CorpusUiText.CP1CancelPlanning(true));
        Assert.Equal(("Stop Planning?", "Planning is still running. Close and stop it? The partial plan is discarded; your settings are saved."), CorpusUiText.CP2ClosePlanning());
    }

    [Fact]
    public void ApprovalBadgeText_Format()
    {
        Assert.Equal("Approved Mar 5, 2:07 PM", CorpusUiText.ApprovalBadgeText(new DateTime(2024, 3, 5, 14, 7, 0)));
    }
}
