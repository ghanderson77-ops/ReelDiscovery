using ReelDiscovery.Models.Case;
using ReelDiscovery.UserControls;

namespace ReelDiscovery.Tests.Case;

// Covers only CaseGuideUiText; no control is instantiated.
public class CaseGuideUiLogicTests
{
    private const string MyDocs = @"C:\Users\x\OneDrive\Documents";
    private const string CaseFolder = @"C:\Users\x\OneDrive\Documents\ReelDiscovery\Cases\A";

    // --- Plural / part nouns ---

    [Theory]
    [InlineData(0, "0 facts")]
    [InlineData(1, "1 fact")]
    [InlineData(2, "2 facts")]
    [InlineData(1000, "1,000 facts")]
    public void Plural_UsesCountAndInvariantGrouping(int n, string expected)
    {
        Assert.Equal(expected, CaseGuideUiText.Plural(n, "fact", "facts"));
    }

    [Theory]
    [InlineData(GuidePart.Issues, true, "Issues")]
    [InlineData(GuidePart.Cast, false, "cast")]
    [InlineData(GuidePart.Facts, true, "Key Facts")]
    [InlineData(GuidePart.Values, false, "canonical values")]
    public void PartNoun_ReturnsDisplayNames(GuidePart part, bool capitalized, string expected)
    {
        Assert.Equal(expected, CaseGuideUiText.PartNoun(part, capitalized));
    }

    [Fact]
    public void IsPluralPart_OnlyCastIsSingular()
    {
        Assert.False(CaseGuideUiText.IsPluralPart(GuidePart.Cast));
        Assert.True(CaseGuideUiText.IsPluralPart(GuidePart.Issues));
        Assert.True(CaseGuideUiText.IsPluralPart(GuidePart.Facts));
        Assert.True(CaseGuideUiText.IsPluralPart(GuidePart.Values));
    }

    [Fact]
    public void JoinParts_UsesArticlesAndAnd()
    {
        Assert.Equal("the issues and the cast", CaseGuideUiText.JoinParts(new[] { GuidePart.Issues, GuidePart.Cast }));
        Assert.Equal("the key facts and the canonical values", CaseGuideUiText.JoinParts(new[] { GuidePart.Facts, GuidePart.Values }));
        Assert.Equal("the issues, the cast and the key facts",
            CaseGuideUiText.JoinParts(new[] { GuidePart.Issues, GuidePart.Cast, GuidePart.Facts }));
    }

    // --- Tabs ---

    [Theory]
    [InlineData(GuidePart.Cast, 42, GuidePartStatus.Ready, "Cast (42)")]
    [InlineData(GuidePart.Facts, 0, GuidePartStatus.Generating, "Key Facts (writing)")]
    [InlineData(GuidePart.Values, 0, GuidePartStatus.Failed, "Canonical Values (failed)")]
    [InlineData(GuidePart.Issues, 0, GuidePartStatus.Pending, "Issues")]
    public void TabCaption_FollowsStatus(GuidePart part, int count, GuidePartStatus status, string expected)
    {
        Assert.Equal(expected, CaseGuideUiText.TabCaption(part, count, status));
    }

    // --- State cards ---

    [Fact]
    public void StateCard_FailedCast_UsesSingularVerb()
    {
        var card = CaseGuideUiText.StateCardText(GuidePart.Cast, GuideTabState.Failed, "Timed out.");
        Assert.Equal("The cast was not written", card.Heading);
        Assert.Equal("Timed out. Click Retry Cast to try again. Finished parts were kept.", card.Body);
        Assert.Equal("Retry Cast", card.ButtonText);
        Assert.False(card.ShowBar);
    }

    [Fact]
    public void StateCard_FailedFacts_UsesPluralVerb()
    {
        var card = CaseGuideUiText.StateCardText(GuidePart.Facts, GuideTabState.Failed, "Timed out");
        Assert.Equal("The key facts were not written", card.Heading);
        Assert.Equal("Retry Key Facts", card.ButtonText);
        Assert.StartsWith("Timed out. Click Retry Key Facts", card.Body);
    }

    [Fact]
    public void StateCard_WritingCast_ShowsBarAndNoButton()
    {
        var card = CaseGuideUiText.StateCardText(GuidePart.Cast, GuideTabState.Writing);
        Assert.Equal("Writing the cast...", card.Heading);
        Assert.Equal("Organizations and key characters come first, then their details and the background staff. The table appears as soon as the key characters are ready.", card.Body);
        Assert.True(card.ShowBar);
        Assert.Equal("", card.ButtonText);
    }

    [Theory]
    [InlineData(GuidePart.Facts, "Waiting for the cast", "The key facts are written after the cast. This tab fills in automatically.")]
    [InlineData(GuidePart.Values, "Waiting for the key facts", "The canonical values are written after the key facts. This tab fills in automatically.")]
    [InlineData(GuidePart.Cast, "Waiting for the issues", "The cast is written after the issues. This tab fills in automatically.")]
    public void StateCard_Waiting_NamesPreviousPart(GuidePart part, string heading, string body)
    {
        var card = CaseGuideUiText.StateCardText(part, GuideTabState.Waiting);
        Assert.Equal(heading, card.Heading);
        Assert.Equal(body, card.Body);
        Assert.Equal("", card.ButtonText);
    }

    [Fact]
    public void StateCard_NotWrittenValues_OffersResume()
    {
        var card = CaseGuideUiText.StateCardText(GuidePart.Values, GuideTabState.NotWritten);
        Assert.Equal("Canonical Values not written yet", card.Heading);
        Assert.Equal("Click Resume Guide to write the rest of the guide. Finished parts were kept.", card.Body);
        Assert.Equal("Resume Guide", card.ButtonText);
    }

    [Fact]
    public void StateCard_NoGuide_OffersGenerate()
    {
        var card = CaseGuideUiText.StateCardText(GuidePart.Issues, GuideTabState.NoGuide);
        Assert.Equal("No case guide yet", card.Heading);
        Assert.Equal("Click Generate Guide to build the issues, cast, key facts and canonical values from the core document.", card.Body);
        Assert.Equal("Generate Guide", card.ButtonText);
    }

    // --- Checks ---

    [Theory]
    [InlineData(1, 0, false, "Checks (1 error, 0 warnings)")]
    [InlineData(2, 3, false, "Checks (2 errors, 3 warnings)")]
    [InlineData(2, 3, true, "Checks — run as each part finishes")]
    public void ChecksCaption_CountsOrRunning(int errors, int warnings, bool running, string expected)
    {
        Assert.Equal(expected, CaseGuideUiText.ChecksCaption(errors, warnings, running));
    }

    // --- Banner ---

    [Fact]
    public void Banner_IssuesAndCastStale_IsStaleCore()
    {
        var banner = CaseGuideUiText.StaleBannerText(new[] { GuidePart.Issues, GuidePart.Cast }, 4);
        Assert.Equal(BannerVariant.StaleCore, banner.Variant);
        Assert.Equal("The guide is out of date with the core document.", banner.Heading);
        Assert.Contains("after the issues and the cast were written", banner.Body);
        Assert.Equal("Update Guide", banner.PrimaryText);
        Assert.Equal("Not Now", banner.SecondaryText);
    }

    [Fact]
    public void Banner_CastStale_UsesSingularVerb()
    {
        var banner = CaseGuideUiText.StaleBannerText(new[] { GuidePart.Cast }, 0);
        Assert.Equal(BannerVariant.StaleCore, banner.Variant);
        Assert.Equal("The core document changed after the cast was written. Update Guide rewrites those parts in order and keeps the entries you edited.", banner.Body);
    }

    [Theory]
    [InlineData(new[] { GuidePart.Facts }, "Key Facts are out of date with the cast.")]
    [InlineData(new[] { GuidePart.Values }, "Canonical Values are out of date with the key facts.")]
    [InlineData(new[] { GuidePart.Facts, GuidePart.Values }, "Key Facts and Canonical Values are out of date with the cast.")]
    public void Banner_Downstream_NamesUpstream(GuidePart[] parts, string heading)
    {
        var banner = CaseGuideUiText.StaleBannerText(parts, 0);
        Assert.Equal(BannerVariant.StaleDownstream, banner.Variant);
        Assert.Equal(heading, banner.Heading);
        Assert.Equal("Update Guide", banner.PrimaryText);
    }

    [Fact]
    public void Banner_DownstreamBody_AgreesWithUpstream()
    {
        var banner = CaseGuideUiText.StaleBannerText(new[] { GuidePart.Values }, 0);
        Assert.Equal("The key facts were regenerated after the canonical values were written. Update Guide rewrites them and keeps the entries you edited.", banner.Body);
        var cast = CaseGuideUiText.StaleBannerText(new[] { GuidePart.Facts }, 0);
        Assert.StartsWith("The cast was regenerated after the key facts were written.", cast.Body);
    }

    [Theory]
    [InlineData(3, "3 facts reference characters you changed.")]
    [InlineData(1, "1 fact references characters you changed.")]
    public void Banner_NeedsReview_AgreesWithCount(int count, string heading)
    {
        var banner = CaseGuideUiText.StaleBannerText(Array.Empty<GuidePart>(), count);
        Assert.Equal(BannerVariant.NeedsReview, banner.Variant);
        Assert.Equal(heading, banner.Heading);
        Assert.Equal("Review Facts", banner.PrimaryText);
        Assert.Equal("", banner.SecondaryText);
    }

    [Fact]
    public void Banner_NothingToShow_IsNone()
    {
        Assert.Equal(BannerVariant.None, CaseGuideUiText.StaleBannerText(Array.Empty<GuidePart>(), 0).Variant);
    }

    // --- Next gate ---

    [Fact]
    public void NextGate_Running_IsDisabled()
    {
        Assert.Equal(NextGateKind.Disabled, CaseGuideUiText.NextGateDecision(true, new[] { GuidePart.Cast }, 2, 2).Kind);
    }

    [Fact]
    public void NextGate_UnfinishedFactsAndValues_ListsBoth()
    {
        var gate = CaseGuideUiText.NextGateDecision(false, new[] { GuidePart.Facts, GuidePart.Values }, 0, 0);
        Assert.Equal(NextGateKind.Unfinished, gate.Kind);
        Assert.Equal("Guide Not Finished", gate.Title);
        Assert.Equal("The guide is not finished: the key facts and the canonical values have not been written. Click Resume Guide to write them, then click Next again.", gate.Message);
    }

    [Fact]
    public void NextGate_UnfinishedCast_UsesSingularVerb()
    {
        var gate = CaseGuideUiText.NextGateDecision(false, new[] { GuidePart.Cast }, 0, 0);
        Assert.Contains("the cast has not been written", gate.Message);
    }

    [Fact]
    public void NextGate_Errors_Block()
    {
        var gate = CaseGuideUiText.NextGateDecision(false, Array.Empty<GuidePart>(), 2, 5);
        Assert.Equal(NextGateKind.Errors, gate.Kind);
        Assert.Equal("Fix the Guide First", gate.Title);
        Assert.Equal("The case guide has 2 errors (listed under Checks). Later steps rely on these links and dates. Fix them, then click Next again.", gate.Message);
    }

    [Theory]
    [InlineData(1, "1 warning remains (listed under Checks). Continue anyway?")]
    [InlineData(3, "3 warnings remain (listed under Checks). Continue anyway?")]
    public void NextGate_Warnings_Ask(int warnings, string message)
    {
        var gate = CaseGuideUiText.NextGateDecision(false, Array.Empty<GuidePart>(), 0, warnings);
        Assert.Equal(NextGateKind.Warnings, gate.Kind);
        Assert.Equal("Review Warnings", gate.Title);
        Assert.Equal(message, gate.Message);
    }

    [Fact]
    public void NextGate_Clean_Proceeds()
    {
        Assert.Equal(NextGateKind.Proceed, CaseGuideUiText.NextGateDecision(false, Array.Empty<GuidePart>(), 0, 0).Kind);
    }

    // --- Paths, subtitle, recent list ---

    [Fact]
    public void DisplayPath_ReplacesMyDocumentsPrefix()
    {
        Assert.Equal(@"Documents\ReelDiscovery\Cases\A", CaseGuideUiText.DisplayPath(CaseFolder, MyDocs));
    }

    [Theory]
    [InlineData(@"D:\Cases\A")]
    [InlineData(@"C:\Users\x\OneDrive\DocumentsArchive\A")]
    public void DisplayPath_OtherPathsUnchanged(string path)
    {
        Assert.Equal(path, CaseGuideUiText.DisplayPath(path, MyDocs));
    }

    [Fact]
    public void Subtitle_Saved_ShowsTime()
    {
        Assert.Equal(@"Documents\ReelDiscovery\Cases\A · Saved 3:42 PM",
            CaseGuideUiText.SubtitleText(CaseFolder, new DateTime(2024, 3, 3, 15, 42, 0), false, MyDocs));
    }

    [Fact]
    public void Subtitle_SaveFailed_SaysNotSaved()
    {
        Assert.Equal(@"Documents\ReelDiscovery\Cases\A · Not saved",
            CaseGuideUiText.SubtitleText(CaseFolder, new DateTime(2024, 3, 3, 15, 42, 0), true, MyDocs));
    }

    [Fact]
    public void Subtitle_NoFolder_NotSavedYet()
    {
        Assert.Equal("Not saved yet", CaseGuideUiText.SubtitleText("", null, false, MyDocs));
    }

    [Theory]
    [InlineData(CaseStage.CaseGuide, false, "Case guide · opened Mar 3, 2024")]
    [InlineData(CaseStage.CaseGuide, true, "Case guide · open now")]
    [InlineData(CaseStage.CorpusSettings, false, "Corpus settings · opened Mar 3, 2024")]
    [InlineData(CaseStage.ReviewPlan, true, "Plan review · open now")]
    [InlineData(CaseStage.Generation, true, "Generating emails · open now")]
    [InlineData(CaseStage.Summary, false, "Complete · opened Mar 3, 2024")]
    [InlineData(CaseStage.CoreDocument, false, "Core document · opened Mar 3, 2024")]
    [InlineData(CaseStage.Described, false, "Core document · opened Mar 3, 2024")]
    public void RecentItemSecondLine_StageAndDate(CaseStage stage, bool isOpen, string expected)
    {
        Assert.Equal(expected, CaseGuideUiText.RecentItemSecondLine(stage, new DateTime(2024, 3, 3, 9, 0, 0), isOpen));
    }

    [Fact]
    public void SaveAndExportLines()
    {
        Assert.Equal("Saved 3:42 PM", CaseGuideUiText.SaveLine(new DateTime(2024, 3, 3, 15, 42, 0), ""));
        Assert.Equal("Not saved: Disk full", CaseGuideUiText.SaveLine(null, "Disk full."));
        Assert.Equal("Not exported yet", CaseGuideUiText.ExportLine(null, "A - Core Document.docx"));
        Assert.Equal("Exported 3:42 PM · A - Core Document.docx",
            CaseGuideUiText.ExportLine(new DateTime(2024, 3, 3, 15, 42, 0), "A - Core Document.docx"));
    }

    // --- Status lines ---

    [Fact]
    public void GuideReadyText_ListsCounts()
    {
        Assert.Equal("Guide ready: 4 issues, 12 key and 30 background characters, 46 facts, 22 values.",
            CaseGuideUiText.GuideReadyText(4, 12, 30, 46, 22));
    }

    [Fact]
    public void GuideReadyWithFindingsText_Pluralizes()
    {
        Assert.Equal("Guide ready with 1 error and 2 warnings. Review the Checks list.",
            CaseGuideUiText.GuideReadyWithFindingsText(1, 2));
    }

    [Theory]
    [InlineData(1, "1 fact references Maria Delgado and was marked for review.")]
    [InlineData(3, "3 facts reference Maria Delgado and were marked for review.")]
    public void FactsMarkedForReviewText_AgreesWithCount(int n, string expected)
    {
        Assert.Equal(expected, CaseGuideUiText.FactsMarkedForReviewText(n, "Maria Delgado"));
    }

    [Theory]
    [InlineData(1, "Deleted Maria Delgado. 1 fact still points to CH-007; it is listed under Checks.")]
    [InlineData(2, "Deleted Maria Delgado. 2 facts still point to CH-007; they are listed under Checks.")]
    public void DeletedWithLinksText_AgreesWithCount(int n, string expected)
    {
        Assert.Equal(expected, CaseGuideUiText.DeletedWithLinksText("Maria Delgado", n, "CH-007"));
    }

    [Fact]
    public void FatalAndPartFailedText_TrimMessagePeriod()
    {
        Assert.Equal("Generation stopped: Invalid API key. Finished parts were kept. Fix the problem on the API Configuration step, then click Resume Guide.",
            CaseGuideUiText.FatalText("Invalid API key."));
        Assert.Equal("The cast failed: Timed out. Click Retry Cast.", CaseGuideUiText.PartFailedText(GuidePart.Cast, "Timed out."));
        Assert.Equal("The model declined to write the key facts: Unsafe. Go Back and soften the core document's wording, or click Retry Key Facts with guidance.",
            CaseGuideUiText.RefusedText(GuidePart.Facts, "Unsafe"));
    }

    [Fact]
    public void ExportedCountText_Pluralizes()
    {
        Assert.Equal("Exported 1 document to the case folder.", CaseGuideUiText.ExportedCountText(1));
        Assert.Equal("Exported 3 documents to the case folder.", CaseGuideUiText.ExportedCountText(3));
    }

    [Theory]
    [InlineData(GuidePart.Cast, "Select a character to see and edit the details.")]
    [InlineData(GuidePart.Issues, "Select an issue to see and edit the details.")]
    [InlineData(GuidePart.Facts, "Select a fact to see and edit the details.")]
    [InlineData(GuidePart.Values, "Select a value to see and edit the details.")]
    public void EmptyDetailText_UsesArticle(GuidePart part, string expected)
    {
        Assert.Equal(expected, CaseGuideUiText.EmptyDetailText(part));
    }

    [Fact]
    public void ProgressTexts_MatchSpec()
    {
        Assert.Equal("Extracting the issues from the counts...", CaseGuideUiText.ProgressIssuesExtract(false));
        Assert.Equal("Extracting the issues from the findings...", CaseGuideUiText.ProgressIssuesExtract(true));
        Assert.Equal("Writing the cast: details for characters 1-10 of 24...", CaseGuideUiText.ProgressCastDetails(1, 10, 24));
        Assert.Equal("Writing the cast: background staff for Harmon Builders (2 of 5)...", CaseGuideUiText.ProgressBackground("Harmon Builders", 2, 5));
        Assert.Equal("Writing the key facts: The bid (1 of 4)...", CaseGuideUiText.ProgressFacts("The bid", 1, 4));
    }

    // --- Confirmations ---

    [Fact]
    public void C5DeleteFact_Unlinked()
    {
        var (title, text) = CaseGuideUiText.C5DeleteFact("F-012", "Site meeting", 0);
        Assert.Equal("Delete Fact", title);
        Assert.Equal("Delete F-012 \"Site meeting\"? This cannot be undone.", text);
    }

    [Fact]
    public void C5DeleteFact_LinkedToValues()
    {
        var (_, text) = CaseGuideUiText.C5DeleteFact("F-012", "Site meeting", 2);
        Assert.Equal("Delete F-012 \"Site meeting\"? It is used by 2 canonical values; those links will be removed. This cannot be undone.", text);
    }

    [Fact]
    public void C3RegenerateEdited_CapitalizesTitle()
    {
        var (title, text) = CaseGuideUiText.C3RegenerateEdited("character", "Maria Delgado");
        Assert.Equal("Regenerate Character", title);
        Assert.Equal("Regenerate Maria Delgado? Your edits to this character will be replaced.", text);
    }

    [Fact]
    public void C1UpdateGuide_ListsParts()
    {
        var (title, text) = CaseGuideUiText.C1UpdateGuide(new[] { GuidePart.Issues, GuidePart.Cast });
        Assert.Equal("Update Guide", title);
        Assert.Equal("Update the guide? The issues and the cast that are out of date will be rewritten. Entries you edited are kept. This makes new Story-model calls.", text);
    }

    [Fact]
    public void FixedConfirmations_HaveSpecTitles()
    {
        Assert.Equal("Cancel Generation", CaseGuideUiText.C2CancelGeneration().Title);
        Assert.Equal("Delete Character", CaseGuideUiText.C4DeleteCharacter("Maria Delgado", "CH-007").Title);
        Assert.Equal("Delete Maria Delgado (CH-007)? This cannot be undone.", CaseGuideUiText.C4DeleteCharacter("Maria Delgado", "CH-007").Text);
        Assert.Equal("Delete Value", CaseGuideUiText.C6DeleteValue("Contract price", "$1,250,000").Title);
        Assert.Equal("Move Case Folder", CaseGuideUiText.C7MoveFolder(@"D:\Cases\A").Title);
        Assert.Equal("Open Case", CaseGuideUiText.C8OpenOverUnsaved("A v. B").Title);
        Assert.Equal("Regenerate Document", CaseGuideUiText.C9RegenerateDocumentWithGuide().Title);
        Assert.Equal("The case could not be saved: Disk full. Close anyway? Changes since 3:42 PM will be lost.",
            CaseGuideUiText.C10CloseAfterFailedSave("Disk full.", new DateTime(2024, 3, 3, 15, 42, 0)).Text);
        Assert.Equal("Close Case", CaseGuideUiText.C11CloseUnsavedScenario().Title);
        Assert.Equal("Stop Generating?", CaseGuideUiText.C12CloseDuringRun().Title);
    }

    // --- Dialog copy ---

    [Fact]
    public void RegenerateDialogCopy_PerTab()
    {
        Assert.Equal("Regenerate Cast", CaseGuideUiText.RegenerateTabTitle(GuidePart.Cast));
        Assert.Equal("Regenerate the values", CaseGuideUiText.RegenerateTabHeadline(GuidePart.Values));
        Assert.Equal("Also replace my 3 edited characters", CaseGuideUiText.ReplaceEditedText(GuidePart.Cast, 3));
        Assert.Equal("Also replace my 1 edited fact", CaseGuideUiText.ReplaceEditedText(GuidePart.Facts, 1));
        Assert.EndsWith(" Facts are not changed.", CaseGuideUiText.RegenerateTabBody(GuidePart.Values));
        Assert.EndsWith("Facts that point to removed characters will be marked for review.", CaseGuideUiText.RegenerateTabBody(GuidePart.Cast));
    }

    [Fact]
    public void DeleteReferencesCopy()
    {
        Assert.Equal("Maria Delgado (CH-007) is linked to 1 fact:", CaseGuideUiText.DeleteLinkedBodyText("Maria Delgado", "CH-007", 1));
        Assert.Equal("Remove from Facts and Delete takes Maria out of these facts. Delete Only keeps the links; each one is listed under Checks until you fix it.",
            CaseGuideUiText.DeleteLinkedFootText("Maria"));
    }

    // --- Spacing hygiene across templates ---

    public static IEnumerable<object[]> TemplateSamples()
    {
        yield return new object[] { CaseGuideUiText.StateCardText(GuidePart.Cast, GuideTabState.Failed, "").Body };
        yield return new object[] { CaseGuideUiText.StateCardText(GuidePart.Facts, GuideTabState.Failed, "  Timed out.  ").Body };
        yield return new object[] { CaseGuideUiText.StaleBannerText(new[] { GuidePart.Facts, GuidePart.Values }, 0).Body };
        yield return new object[] { CaseGuideUiText.NextGateDecision(false, new[] { GuidePart.Issues }, 0, 0).Message };
        yield return new object[] { CaseGuideUiText.FatalText("") };
        yield return new object[] { CaseGuideUiText.SaveFailedText("Access denied.") };
        yield return new object[] { CaseGuideUiText.EntryRegenFailedText("Maria Delgado", "Timed out.") };
        yield return new object[] { CaseGuideUiText.C5DeleteFact("F-001", "Kickoff", 1).Text };
        yield return new object[] { CaseGuideUiText.C10CloseAfterFailedSave("", null).Text };
        yield return new object[] { CaseGuideUiText.RegenerateTabBody(GuidePart.Facts) };
        yield return new object[] { CaseGuideUiText.FactsCountText(46, 0) };
        yield return new object[] { CaseGuideUiText.ExportFailedText("cast", "Access denied.") };
    }

    [Theory]
    [MemberData(nameof(TemplateSamples))]
    public void Templates_HaveNoDoubleSpacesOrSpaceBeforePeriod(string text)
    {
        Assert.DoesNotContain("  ", text);
        Assert.DoesNotContain(" .", text);
        Assert.DoesNotContain("..", text.Replace("...", ""));
    }

    [Fact]
    public void CountLines_UsePluralAndAgreement()
    {
        Assert.Equal("46 facts · 1 needs review", CaseGuideUiText.FactsCountText(46, 1));
        Assert.Equal("46 facts · 3 need review", CaseGuideUiText.FactsCountText(46, 3));
        Assert.Equal("12 key · 30 background", CaseGuideUiText.CastCountText(12, 30));
        Assert.Equal("Appears in 1 fact.", CaseGuideUiText.LinkedFactsText(1));
        Assert.Equal("Not in any fact yet.", CaseGuideUiText.LinkedFactsText(0));
    }

    // --- Plan dependency notice (09 UI-SPEC S2) ---

    private const string ChangedHeading = "The corpus plan was built from an earlier version of this guide.";
    private const string ChangedLine =
        "The corpus plan was built from an earlier version of this guide. Review it on Review Plan before generating emails.";

    [Fact]
    public void DependencyNotice_Changed_NoEmails()
    {
        var notice = CaseGuideUiText.DependencyNotice(hasPlan: true, approved: false, guideStale: true, emailsWritten: 0);

        Assert.Equal(DependencyNoticeKind.Changed, notice.Kind);
        Assert.Equal(ChangedHeading, notice.Heading);
        Assert.Equal(
            "Generate Corpus will not write emails from this plan until you review it. Open Review Plan to keep the plan and approve it, or re-plan from the current guide.",
            notice.Body);
        Assert.Equal("Review Plan", notice.ButtonText);
        Assert.Equal(ChangedLine, notice.Line);
    }

    [Fact]
    public void DependencyNotice_Changed_WithEmails()
    {
        var many = CaseGuideUiText.DependencyNotice(true, false, true, 1240);
        Assert.Equal(DependencyNoticeKind.Changed, many.Kind);
        Assert.Equal(
            "Generate Corpus will not write emails from this plan until you review it. The 1,240 emails already written stay in place. Open Review Plan to keep the plan and approve it, or re-plan from the current guide.",
            many.Body);

        var one = CaseGuideUiText.DependencyNotice(true, true, true, 1);
        Assert.Equal(DependencyNoticeKind.Changed, one.Kind);
        Assert.Equal(
            "Generate Corpus will not write emails from this plan until you review it. The 1 email already written stays in place. Open Review Plan to keep the plan and approve it, or re-plan from the current guide.",
            one.Body);
    }

    [Fact]
    public void DependencyNotice_ApprovedCurrent_Info()
    {
        var none = CaseGuideUiText.DependencyNotice(true, true, false, 0);
        Assert.Equal(DependencyNoticeKind.Info, none.Kind);
        Assert.Equal(
            "The approved corpus plan was built from this guide. Editing the guide withdraws plan approval until you review the plan again.",
            none.Line);
        Assert.Equal("", none.Heading);
        Assert.Equal("", none.Body);
        Assert.Equal("", none.ButtonText);

        Assert.Equal(
            "The approved corpus plan and 3 generated emails were built from this guide. Editing the guide withdraws plan approval until you review the plan again.",
            CaseGuideUiText.DependencyNotice(true, true, false, 3).Line);
        Assert.Equal(
            "The approved corpus plan and 1 generated email were built from this guide. Editing the guide withdraws plan approval until you review the plan again.",
            CaseGuideUiText.DependencyNotice(true, true, false, 1).Line);
    }

    [Fact]
    public void DependencyNotice_UnapprovedWithEmails_Info()
    {
        var five = CaseGuideUiText.DependencyNotice(true, false, false, 5);
        Assert.Equal(DependencyNoticeKind.Info, five.Kind);
        Assert.Equal("5 generated emails were written from this guide. Emails already written do not change when you edit it.", five.Line);
        Assert.Equal("", five.Heading);

        Assert.Equal(
            "1 generated email was written from this guide. Emails already written do not change when you edit it.",
            CaseGuideUiText.DependencyNotice(true, false, false, 1).Line);
    }

    [Theory]
    [InlineData(false, false, false, 0)]
    [InlineData(true, false, false, 0)]
    [InlineData(false, false, true, 0)]
    [InlineData(true, false, false, -2)]
    public void DependencyNotice_None(bool hasPlan, bool approved, bool guideStale, int emailsWritten)
    {
        var notice = CaseGuideUiText.DependencyNotice(hasPlan, approved, guideStale, emailsWritten);

        Assert.Equal(DependencyNoticeKind.None, notice.Kind);
        Assert.Equal("", notice.Line);
        Assert.Equal("", notice.Heading);
        Assert.Equal("", notice.Body);
        Assert.Equal("", notice.ButtonText);
    }

    [Fact]
    public void PlanDependency_IsLastBannerVariant_AndNeverFromStaleBanner()
    {
        Assert.Equal(BannerVariant.PlanDependency, Enum.GetValues<BannerVariant>().Last());
        Assert.NotEqual(BannerVariant.PlanDependency, CaseGuideUiText.StaleBannerText(Array.Empty<GuidePart>(), 0).Variant);
        Assert.NotEqual(BannerVariant.PlanDependency, CaseGuideUiText.StaleBannerText(new[] { GuidePart.Cast }, 2).Variant);
        Assert.NotEqual(BannerVariant.PlanDependency, CaseGuideUiText.StaleBannerText(new[] { GuidePart.Facts }, 0).Variant);
    }

    [Fact]
    public void StateCardContent_SecondaryText_DefaultsEmpty()
    {
        Assert.Equal("", new StateCardContent().SecondaryText);
    }
}
