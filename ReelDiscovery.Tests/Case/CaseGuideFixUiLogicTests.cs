using ReelDiscovery.Models.Case;
using ReelDiscovery.Services.Case;
using ReelDiscovery.UserControls;

namespace ReelDiscovery.Tests.Case;

// Checks fixes copy and state (Fix / Fix All buttons, the M2 errors dialog, M1 "how to finish"); no control is instantiated.
public class CaseGuideFixUiLogicTests
{
    private static GuideFinding Error(string message, string hint, GuideQuickFix fix, GuideArea area = GuideArea.Facts, string id = "F-001") => new()
    {
        Severity = IssueSeverity.Error,
        Area = area,
        EntityId = id,
        Message = message,
        FixHint = hint,
        QuickFix = fix
    };

    // --- Fix button ---

    [Fact]
    public void FixButton_NoSelection_IsDisabledFix()
    {
        var state = CaseGuideUiText.FixButtonFor(null, running: false);

        Assert.False(state.Enabled);
        Assert.False(state.Primary);
        Assert.Equal("Fix", state.Text);
    }

    [Fact]
    public void FixButton_AutomaticFix_IsPrimaryFix()
    {
        var state = CaseGuideUiText.FixButtonFor(Error("x", "y", GuideQuickFix.WidenActiveRange), running: false);

        Assert.True(state.Enabled);
        Assert.True(state.Primary);
        Assert.Equal("Fix", state.Text);
    }

    [Fact]
    public void FixButton_WithTheBannersUpdateGuideShown_StaysDefault()
    {
        // At most two Primary buttons at once: Update Guide and the detail card's Regenerate are already Primary
        var state = CaseGuideUiText.FixButtonFor(Error("x", "y", GuideQuickFix.WidenActiveRange), running: false, otherPrimaryShown: true);

        Assert.True(state.Enabled);
        Assert.False(state.Primary);
        Assert.Equal("Fix", state.Text);
    }

    [Fact]
    public void FixButton_CheckWithoutFix_IsDisabled()
    {
        var state = CaseGuideUiText.FixButtonFor(Error("x", "y", GuideQuickFix.None), running: false);

        Assert.False(state.Enabled);
        Assert.Equal("Fix", state.Text);
    }

    [Theory]
    [InlineData(GuideQuickFix.RegenerateEntry, GuideArea.Facts, "Regenerate Fact")]
    [InlineData(GuideQuickFix.RegenerateEntry, GuideArea.Cast, "Regenerate Character")]
    [InlineData(GuideQuickFix.RegenerateEntry, GuideArea.Values, "Regenerate Value")]
    [InlineData(GuideQuickFix.RegenerateCast, GuideArea.Guide, "Regenerate Cast...")]
    [InlineData(GuideQuickFix.RegenerateValues, GuideArea.Values, "Regenerate Values...")]
    [InlineData(GuideQuickFix.RetryIssueDetails, GuideArea.Guide, "Retry Issue Details")]
    [InlineData(GuideQuickFix.UpdateGuide, GuideArea.Guide, "Update Guide")]
    [InlineData(GuideQuickFix.ResumeGuide, GuideArea.Guide, "Resume Guide")]
    public void FixButton_ModelFix_NamesTheActionAndIsNotPrimary(GuideQuickFix fix, GuideArea area, string text)
    {
        var state = CaseGuideUiText.FixButtonFor(Error("x", "y", fix, area), running: false);

        Assert.True(state.Enabled);
        Assert.False(state.Primary);
        Assert.Equal(text, state.Text);
    }

    [Fact]
    public void FixButton_DuringARun_IsDisabled()
    {
        var state = CaseGuideUiText.FixButtonFor(Error("x", "y", GuideQuickFix.RemoveBrokenLinks), running: true);

        Assert.False(state.Enabled);
        Assert.False(state.Primary);
    }

    [Fact]
    public void EveryAutomaticFix_HasAFixAllLine_AndEveryOtherFixAnAction()
    {
        foreach (var fix in Enum.GetValues<GuideQuickFix>().Where(f => f != GuideQuickFix.None))
        {
            if (CaseGuideQuickFix.IsAutomatic(fix))
            {
                Assert.Equal("Fix", CaseGuideUiText.QuickFixActionText(fix, GuideArea.Facts));
                Assert.NotEqual(string.Empty, CaseGuideUiText.FixAllLine(fix, 2));
            }
            else
            {
                Assert.NotEqual("Fix", CaseGuideUiText.QuickFixActionText(fix, GuideArea.Facts));
                Assert.NotEqual(string.Empty, CaseGuideUiText.QuickFixActionText(fix, GuideArea.Facts));
            }
        }
        Assert.Equal(string.Empty, CaseGuideUiText.QuickFixActionText(GuideQuickFix.None, GuideArea.Facts));
    }

    // --- Fix All ---

    [Theory]
    [InlineData(1, "Fix All (1)")]
    [InlineData(12, "Fix All (12)")]
    [InlineData(1200, "Fix All (1,200)")]
    public void FixAllButton_ShowsTheCount(int n, string text)
    {
        Assert.Equal(text, CaseGuideUiText.FixAllButtonText(n));
    }

    [Fact]
    public void FixAllText_ListsCountsPerKind()
    {
        var content = CaseGuideUiText.FixAllText(new List<(GuideQuickFix, int)>
        {
            (GuideQuickFix.RemoveBrokenLinks, 1),
            (GuideQuickFix.WidenActiveRange, 3),
            (GuideQuickFix.AddActorsToKnownBy, 1),
            (GuideQuickFix.MarkReviewed, 2)
        });

        Assert.Equal("Fix All", content.Title);
        Assert.Equal("Fix 7 checks automatically?", content.Headline);
        Assert.Equal(new[]
        {
            "Remove broken links from 1 entry",
            "Widen or repair 3 active ranges",
            "Add 1 person under Who knew",
            "Mark 2 entries reviewed (the text stays as it is)"
        }, content.Lines);
        Assert.Equal("Fix All", content.ConfirmText);
        Assert.Contains("no model calls", content.Body);
    }

    [Fact]
    public void AutomaticFixCount_CountsOnlyAutomaticFixes()
    {
        var findings = new[]
        {
            Error("a", "h", GuideQuickFix.WidenActiveRange),
            Error("b", "h", GuideQuickFix.RegenerateCast),
            Error("c", "h", GuideQuickFix.None),
            Error("d", "h", GuideQuickFix.MarkReviewed)
        };

        Assert.Equal(2, CaseGuideUiText.AutomaticFixCount(findings));
        Assert.Equal(0, CaseGuideUiText.AutomaticFixCount(null!));
    }

    [Theory]
    [InlineData(3, 0, 0, "Fixed 3 checks. No problems remain.")]
    [InlineData(1, 1, 0, "Fixed 1 check. 1 error remains under Checks.")]
    [InlineData(2, 2, 3, "Fixed 2 checks. 2 errors and 3 warnings remain under Checks.")]
    [InlineData(1, 0, 4, "Fixed 1 check. 4 warnings remain under Checks.")]
    [InlineData(0, 2, 0, "Nothing changed: the check was already cleared.")]
    public void FixedChecksText_SaysWhatIsLeft(int fixedCount, int errors, int warnings, string text)
    {
        Assert.Equal(text, CaseGuideUiText.FixedChecksText(fixedCount, errors, warnings));
    }

    // --- M2 errors dialog ---

    [Fact]
    public void ErrorsGate_ListsUpToFiveErrorsWithHints_AndCountsTheRest()
    {
        var errors = Enumerable.Range(1, 7)
            .Select(i => Error($"F-00{i} has no title.", $"Type a title for F-00{i} under Fact, or click Regenerate Fact.",
                i <= 2 ? GuideQuickFix.RemoveBrokenLinks : GuideQuickFix.RegenerateEntry, id: $"F-00{i}"))
            .ToList();

        var content = CaseGuideUiText.ErrorsGateText(errors);

        Assert.Equal("Fix the Guide First", content.Title);
        Assert.Equal("The case guide has 7 errors", content.Headline);
        Assert.Equal("Later steps rely on these links and dates. Here is how to fix them:", content.Body);
        Assert.Equal(5, content.Lines.Count);
        Assert.Equal("F-001 has no title.", content.Lines[0].Message);
        Assert.Equal("Type a title for F-001 under Fact, or click Regenerate Fact.", content.Lines[0].FixHint);
        Assert.Equal("Key Facts · F-001", content.Lines[0].Where);
        Assert.Equal("+ 2 more under Checks", content.MoreText);
        Assert.Equal("Show Checks", content.ShowChecksText);
        Assert.Equal("Fix Automatically (2)", content.FixAutomaticallyText);
        Assert.Equal("Continue Anyway", content.ContinueAnywayText);
    }

    [Fact]
    public void ErrorsGate_OneErrorWithoutAutomaticFix_HasNoFixButtonOrMoreLine()
    {
        var content = CaseGuideUiText.ErrorsGateText(new[] { Error("F-008 is dated 2024-10-01, after the filing date (2024-09-16).",
            "Change F-008's date to 2024-09-16 or earlier, or change the filing date in the core document.", GuideQuickFix.None) });

        Assert.Equal("The case guide has 1 error", content.Headline);
        Assert.EndsWith("Here is how to fix it:", content.Body);
        Assert.Single(content.Lines);
        Assert.Equal(string.Empty, content.MoreText);
        Assert.Equal(string.Empty, content.FixAutomaticallyText);
    }

    [Theory]
    [InlineData(1, "The guide still has 1 error. Later steps may produce inconsistent emails. Continue anyway?")]
    [InlineData(4, "The guide still has 4 errors. Later steps may produce inconsistent emails. Continue anyway?")]
    public void ContinueWithErrors_AsksOnce(int errors, string text)
    {
        var (title, body) = CaseGuideUiText.C15ContinueWithErrors(errors);

        Assert.Equal("Continue Anyway", title);
        Assert.Equal(text, body);
    }

    // --- M1 "how to finish" ---

    [Fact]
    public void ResumeHint_NamesOnePartOrSaysThem()
    {
        Assert.Equal("Click Resume Guide to write the key facts", CaseGuideUiText.ResumeHint(new[] { GuidePart.Facts }));
        Assert.Equal("Click Resume Guide to write them", CaseGuideUiText.ResumeHint(new[] { GuidePart.Facts, GuidePart.Values }));
        Assert.Equal("Click Resume Guide", CaseGuideUiText.ResumeHint(Array.Empty<GuidePart>()));
    }

    [Fact]
    public void NextGate_UnfinishedCast_SaysHowToFinish()
    {
        var gate = CaseGuideUiText.NextGateDecision(false, new[] { GuidePart.Cast }, 0, 0);

        Assert.Equal("The guide is not finished: the cast has not been written. Click Resume Guide to write the cast, then click Next again.", gate.Message);
    }

    [Fact]
    public void NextGate_M1AndValidatorAgreeOnHowToFinish()
    {
        // The unwritten-part check and the M1 gate give the same instruction
        var draft = CaseTestData.SampleGuidedDraft();
        draft.Guide!.ValuesState.Status = GuidePartStatus.Pending;

        var finding = CaseGuideValidator.Validate(draft).Single(f => f.Message == "The canonical values were not written.");
        var gate = CaseGuideUiText.NextGateDecision(false, new[] { GuidePart.Values }, 1, 0);

        Assert.Contains(finding.FixHint.TrimEnd('.'), gate.Message);
    }

    // --- Navigation (double-click / Enter on a check) ---

    [Fact]
    public void NavigationTarget_EntityFinding_SelectsItsEntity()
    {
        var draft = CaseTestData.SampleGuidedDraft();
        draft.Guide!.FindFact("F-002")!.ActorIds.Add("CH-099");
        var finding = CaseGuideValidator.Validate(draft).First(f => f.EntityId == "F-002" && f.Severity == IssueSeverity.Error);

        Assert.Equal("F-002", StepCaseGuide.NavigationTarget(draft, finding));
        Assert.Equal(GuideFindingField.Actors, finding.Field);
    }

    [Fact]
    public void NavigationTarget_OrganizationFinding_SelectsItsFirstCharacterOnTheCastTab()
    {
        var draft = CaseTestData.SampleGuidedDraft();
        var finding = CaseGuideValidator.Validate(draft).First(f => f.Message == "Harmon Builders, Inc. has no in-house counsel.");

        Assert.Equal(GuideArea.Cast, finding.GoTo);
        Assert.Equal(GuidePart.Cast, StepCaseGuide.PartFor(finding.GoTo!.Value));
        Assert.Equal("CH-003", StepCaseGuide.NavigationTarget(draft, finding));
    }

    [Fact]
    public void NavigationTarget_PartyFinding_SelectsTheSameNamedCharacter()
    {
        var draft = CaseTestData.SampleGuidedDraft();
        draft.CoreDocument!.Parties.Add(new CaseParty { Id = "party-3", Name = "Lisa Chen", Role = PartyRole.Defendant, EntityType = "individual" });
        draft.Guide!.FindCast("CH-004")!.PartyId = "party-old";
        var finding = CaseGuideValidator.Validate(draft).First(f => f.Message == "Party Lisa Chen is not in the cast.");

        Assert.Equal("CH-004", StepCaseGuide.NavigationTarget(draft, finding));
    }

    [Fact]
    public void NavigationTarget_UnwrittenPart_OpensThatTabWithoutSelecting()
    {
        var draft = CaseTestData.SampleGuidedDraft();
        draft.Guide!.ValuesState.Status = GuidePartStatus.Failed;
        var finding = CaseGuideValidator.Validate(draft).First(f => f.Message == "The canonical values were not written.");

        Assert.Equal(GuideArea.Values, finding.GoTo);
        Assert.Equal(string.Empty, StepCaseGuide.NavigationTarget(draft, finding));
    }

    // --- Spacing hygiene ---

    public static IEnumerable<object[]> FixTemplates()
    {
        yield return new object[] { CaseGuideUiText.FixedChecksText(2, 1, 1) };
        yield return new object[] { CaseGuideUiText.C15ContinueWithErrors(2).Text };
        yield return new object[] { CaseGuideUiText.FixAllText(new List<(GuideQuickFix, int)> { (GuideQuickFix.ResetEmail, 2) }).Foot };
        yield return new object[] { CaseGuideUiText.NextGateDecision(false, new[] { GuidePart.Issues }, 0, 0).Message };
        yield return new object[] { CaseGuideUiText.ChecksHintText };
        foreach (var finding in CaseGuideValidator.Validate(CaseTestData.SampleGuidedDraft()))
            yield return new object[] { finding.FixHint };
    }

    [Theory]
    [MemberData(nameof(FixTemplates))]
    public void FixTemplates_HaveNoDoubleSpacesOrSpaceBeforePeriod(string text)
    {
        Assert.DoesNotContain("  ", text);
        Assert.DoesNotContain(" .", text);
        Assert.DoesNotContain("..", text.Replace("...", ""));
        Assert.DoesNotContain("!", text);
        Assert.DoesNotContain("please", text, StringComparison.OrdinalIgnoreCase);
    }
}
