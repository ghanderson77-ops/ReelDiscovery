using ReelDiscovery.Models.Case;
using ReelDiscovery.Models.Corpus;
using ReelDiscovery.UserControls;

namespace ReelDiscovery.Tests.Corpus;

public class CorpusUiTextTests
{
    // --- Live counts and the D-07 relevant warning ---

    [Theory]
    [InlineData(200, 800, "≈200 relevant / 800 non-relevant emails")]
    [InlineData(1, 249, "≈1 relevant / 249 non-relevant emails")]
    [InlineData(0, 50, "≈0 relevant / 50 non-relevant emails")]
    [InlineData(1000, 4000, "≈1,000 relevant / 4,000 non-relevant emails")]
    public void LiveCounts_FixedFormat(int relevant, int nonRelevant, string expected)
    {
        Assert.Equal(expected, CorpusUiText.LiveCounts(relevant, nonRelevant));
    }

    [Fact]
    public void RelevantWarning_FewerThanFacts_IsWarningWithThreshold()
    {
        var line = CorpusUiText.RelevantWarning(250, 5, 20);

        Assert.NotNull(line);
        Assert.True(line!.IsWarning);
        Assert.Equal("Only 13 relevant emails for 20 key facts. Every key fact needs at least one relevant email: raise this to at least 8%.", line.Text);
    }

    [Fact]
    public void RelevantWarning_FewerThanTwiceFacts_IsMutedHint()
    {
        var line = CorpusUiText.RelevantWarning(1000, 5, 30);

        Assert.NotNull(line);
        Assert.False(line!.IsWarning);
        Assert.Equal("Most key facts will get a single short email. Raise this to 6% or more for fuller threads.", line.Text);
    }

    [Theory]
    [InlineData(1000, 20, 30)]
    [InlineData(1000, 5, 0)]
    [InlineData(1000, 5, 25)]
    public void RelevantWarning_Enough_IsNull(int total, int percent, int facts)
    {
        Assert.Null(CorpusUiText.RelevantWarning(total, percent, facts));
    }

    [Fact]
    public void RelevantWarning_SingularFact()
    {
        // 50 x 5% = 2.5 -> 3 relevant; 1 fact is fine at 1 x; 2 x 1 = 2 < 3, so hidden
        Assert.Null(CorpusUiText.RelevantWarning(50, 5, 1));

        var line = CorpusUiText.RelevantWarning(50, 5, 4);
        Assert.NotNull(line);
        Assert.Equal("Only 3 relevant emails for 4 key facts. Every key fact needs at least one relevant email: raise this to at least 7%.", line!.Text);
    }

    [Fact]
    public void RelevantWarning_SingularRelevantEmail()
    {
        // 20 x 5% = 1 relevant
        var line = CorpusUiText.RelevantWarning(20, 5, 2);
        Assert.NotNull(line);
        Assert.StartsWith("Only 1 relevant email for 2 key facts.", line!.Text);
    }

    [Fact]
    public void RelevantWarning_ThresholdCappedAt60()
    {
        var line = CorpusUiText.RelevantWarning(50, 5, 1000);
        Assert.NotNull(line);
        Assert.Equal("Only 3 relevant emails for 1,000 key facts. Every key fact needs at least one relevant email: raise this to at least 60%.", line!.Text);
    }

    [Theory]
    [InlineData(250, 20, 8)]
    [InlineData(1000, 60, 6)]
    [InlineData(1000, 1, 5)]
    [InlineData(50, 1000, 60)]
    public void MinPercentFor_SmallestWholePercent(int total, int needed, int expected)
    {
        Assert.Equal(expected, CorpusUiText.MinPercentFor(total, needed));
    }

    // --- Notice, size, window, mix ---

    [Fact]
    public void SettingsNotice_ThreeStates()
    {
        Assert.Equal("Click Next to plan every thread and message from these settings and the case guide. Nothing is written yet. To write discovery documents without emails, click Skip to Discovery Documents.",
            CorpusUiText.SettingsNotice(false, false, null));
        Assert.Equal("Changing these settings replaces the current plan when you click Next. Skip to Discovery Documents leaves the plan as it is.",
            CorpusUiText.SettingsNotice(true, false, null));
        Assert.Equal("The plan was approved on Mar 5, 2024. If you change these settings, Review Plan offers to re-plan; the approved plan stays until you do.",
            CorpusUiText.SettingsNotice(true, true, new DateTime(2024, 3, 5, 14, 7, 0)));
    }

    [Theory]
    [InlineData(ComplexityLevel.Simple, "Default for a Simple case: 250 emails. Type any number from 50 to 5,000.")]
    [InlineData(ComplexityLevel.Standard, "Default for a Standard case: 1,000 emails. Type any number from 50 to 5,000.")]
    [InlineData(ComplexityLevel.Complex, "Default for a Complex case: 2,500 emails. Type any number from 50 to 5,000.")]
    public void SizeHint_PerComplexity(ComplexityLevel level, string expected)
    {
        Assert.Equal(expected, CorpusUiText.SizeHint(level));
    }

    [Theory]
    [InlineData(5000, "Set to 5,000. The corpus size must be 50 to 5,000 emails.")]
    [InlineData(50, "Set to 50. The corpus size must be 50 to 5,000 emails.")]
    public void SizeClampNotice_Text(int value, string expected)
    {
        Assert.Equal(expected, CorpusUiText.SizeClampNotice(value));
    }

    [Fact]
    public void WindowHint_AndClampTexts()
    {
        Assert.Equal("Key facts run from Jan 15, 2024 to Jun 20, 2024. The default window adds about a month before and two months after for everyday email.",
            CorpusUiText.WindowHint(new DateTime(2024, 1, 15), new DateTime(2024, 6, 20)));
        Assert.Equal("Start moved to Jan 15, 2024, the first key fact. The window must include every key fact.",
            CorpusUiText.StartClampedText(new DateTime(2024, 1, 15)));
        Assert.Equal("End moved to Jun 20, 2024, the last key fact. The window must include every key fact.",
            CorpusUiText.EndClampedText(new DateTime(2024, 6, 20)));
        Assert.Equal("Use a date like 2024-03-03 (YYYY-MM-DD).", CorpusUiText.DateInvalidText);
        Assert.Equal("The end date must be after the start date.", CorpusUiText.EndBeforeStartText);
    }

    [Fact]
    public void MixTotalLine_OkAndError()
    {
        Assert.Equal(("Total 100%", true), CorpusUiText.MixTotalLine(100));
        Assert.Equal(("Total 95%. The four shares must add up to 100%.", false), CorpusUiText.MixTotalLine(95));
        Assert.Equal(("Total 0%. The four shares must add up to 100%.", false), CorpusUiText.MixTotalLine(0));
    }

    [Theory]
    [InlineData(800, "How the 800 non-relevant emails are split.")]
    [InlineData(4000, "How the 4,000 non-relevant emails are split.")]
    public void MixHint_Text(int n, string expected)
    {
        Assert.Equal(expected, CorpusUiText.MixHint(n));
    }

    [Fact]
    public void AdvancedSummary_DefaultIsAutomatic()
    {
        Assert.Equal("Other email mix 50 / 20 / 20 / 10% · hot, privileged and attachment rate automatic · DocID prefix DM",
            CorpusUiText.AdvancedSummary(new CorpusSettings(), "DM"));
    }

    [Theory]
    [InlineData(1, null, null)]
    [InlineData(null, 0, null)]
    [InlineData(null, null, 30)]
    public void AdvancedSummary_AnyOverrideIsCustom(int? hot, int? privileged, int? attachments)
    {
        var settings = new CorpusSettings
        {
            HotPerHotFact = hot,
            PrivilegedPercent = privileged,
            AttachmentPercent = attachments,
            DailyBusinessPercent = 40,
            AncillaryPercent = 30
        };

        Assert.Equal("Other email mix 40 / 30 / 20 / 10% · hot, privileged and attachment rate custom · DocID prefix BH",
            CorpusUiText.AdvancedSummary(settings, "BH"));
    }

    // --- Override hints ---

    [Theory]
    [InlineData(0, "Auto: no facts are marked Hot.")]
    [InlineData(1, "Auto: 1 or 2 each, about 1-2 hot emails from 1 hot fact.")]
    [InlineData(2, "Auto: 1 or 2 each, about 2-4 hot emails from 2 hot facts.")]
    [InlineData(1000, "Auto: 1 or 2 each, about 1,000-2,000 hot emails from 1,000 hot facts.")]
    public void HotHint_Text(int hotFacts, string expected)
    {
        Assert.Equal(expected, CorpusUiText.HotHint(hotFacts));
    }

    [Fact]
    public void PrivilegedHint_Variants()
    {
        Assert.Equal("Auto: about 8%, threads with Karen Walsh, Morgan Ellis and 1 more.",
            CorpusUiText.PrivilegedHint(new[] { "Karen Walsh", "Morgan Ellis", "Evan Price" }));
        Assert.Equal("Auto: about 8%, threads with Karen Walsh and Morgan Ellis.",
            CorpusUiText.PrivilegedHint(new[] { "Karen Walsh", "Morgan Ellis" }));
        Assert.Equal("Auto: about 8%, threads with Karen Walsh.",
            CorpusUiText.PrivilegedHint(new[] { "Karen Walsh" }));
        Assert.Equal("Auto: about 8%, threads with A, B and 3 more.",
            CorpusUiText.PrivilegedHint(new[] { "A", "B", "C", "D", "E" }));
        Assert.Equal("No counsel in the cast, so no privileged threads are planned.",
            CorpusUiText.PrivilegedHint(Array.Empty<string>()));
    }

    [Fact]
    public void OtherOverrideHints()
    {
        Assert.Equal("Auto: 20% of emails carry an attachment.", CorpusUiText.AttachmentHint());
        Assert.Equal("DocIDs will read DM-000001, attachments DM-000001.0001.", CorpusUiText.PrefixHint("DM"));
        Assert.Equal("Set to 5. Use 1 to 5.", CorpusUiText.OverrideClampNotice(5, 1, 5));
        Assert.Equal("Set to 0. Use 0 to 30.", CorpusUiText.OverrideClampNotice(0, 0, 30));
        Assert.Equal("Use 2 to 6 letters, e.g. DM.", CorpusUiText.PrefixInvalidText);
        Assert.Equal("Images and voicemail need a tested OpenAI key. Add one on the API Configuration step to turn them on.", CorpusUiText.MediaNotice);
        Assert.Equal("Office documents are Word, Excel and PowerPoint files. The plan only gives attachments of the types you check.", CorpusUiText.AttachmentTypesHint);
        Assert.Equal("Near-miss decoys mention the same projects and people but are not about the alleged conduct.", CorpusUiText.DecoyHint);
        Assert.Equal("Leave a field empty to let the planner choose.", CorpusUiText.OverridesHint);
    }

    // --- Dialogs and message boxes ---

    [Fact]
    public void MS1_MS2_CS1_Texts()
    {
        Assert.Equal(("Check the Settings", "The non-relevant mix adds up to 95%. Change the shares under Advanced so they add up to 100%, then click Next again."),
            CorpusUiText.MS1MixTotal(95));
        Assert.Equal(("Check the Settings", "The start date is not a valid date. Use a date like 2024-03-03, then click Next again."),
            CorpusUiText.MS2InvalidDate("start"));
        Assert.Equal(("Check the Settings", "The end date is not a valid date. Use a date like 2024-03-03, then click Next again."),
            CorpusUiText.MS2InvalidDate("end"));
        Assert.Equal(("Reset to Defaults", "Reset every corpus setting to the defaults for a Standard case?"),
            CorpusUiText.CS1ResetDefaults(ComplexityLevel.Standard));
        Assert.Equal(("Reset to Defaults", "Reset every corpus setting to the defaults for a Complex case?"),
            CorpusUiText.CS1ResetDefaults(ComplexityLevel.Complex));
    }

    [Fact]
    public void ReplanDialog_Texts()
    {
        const string body = "Builds a new plan from the case guide and the current corpus settings, and replaces this one. Every DocID changes. This makes new Story-model calls.";
        Assert.Equal(body, CorpusUiText.ReplanBody(false));
        Assert.Equal(body + " The answer key and review protocol in the case folder are rewritten when you approve the new plan.", CorpusUiText.ReplanBody(true));
        Assert.Equal("Re-plan Corpus", CorpusUiText.ReplanTitle);
        Assert.Equal("Re-plan the corpus", CorpusUiText.ReplanHeadline);
        Assert.Equal("Guidance (optional)", CorpusUiText.ReplanGuidanceCaption);
        Assert.Equal("e.g. more traffic around the March change-order dispute", CorpusUiText.ReplanPlaceholder);
        Assert.Equal("Guidance can shift volume toward or away from facts and periods. Dates, people and labels still come from the case guide.", CorpusUiText.ReplanHint);
    }

    [Theory]
    [InlineData(true, 1, " 1 thread already generated for this plan will no longer match it. After re-planning, Generate Corpus asks you to start over.")]
    [InlineData(false, 12, " 12 threads already generated for this plan will no longer match it. After re-planning, Generate Corpus asks you to start over.")]
    [InlineData(true, 1000, " 1,000 threads already generated for this plan will no longer match it. After re-planning, Generate Corpus asks you to start over.")]
    public void ReplanBody_GeneratedThreads_AddsTheStartOverSentence(bool approved, int generated, string suffix)
    {
        Assert.Equal(CorpusUiText.ReplanBody(approved) + suffix, CorpusUiText.ReplanBody(approved, generated));
        Assert.EndsWith(suffix, CorpusUiText.ReplanBody(approved, generated));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ReplanBody_NoGeneratedThreads_IsUnchanged(bool approved)
    {
        Assert.Equal(CorpusUiText.ReplanBody(approved), CorpusUiText.ReplanBody(approved, 0));
        Assert.DoesNotContain("already generated", CorpusUiText.ReplanBody(approved, 0));
    }

    [Theory]
    [InlineData(1, "1 manual relabel in the answer key will be dropped.")]
    [InlineData(3, "3 manual relabels in the answer key will be dropped.")]
    [InlineData(1200, "1,200 manual relabels in the answer key will be dropped.")]
    public void RelabelsDroppedText_Plural(int relabels, string expected)
    {
        Assert.Equal(expected, CorpusUiText.RelabelsDroppedText(relabels));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ReplanBody_Relabels_AppendsDroppedClauseLast(bool approved)
    {
        Assert.Equal(CorpusUiText.ReplanBody(approved), CorpusUiText.ReplanBody(approved, 0, 0));
        Assert.Equal(CorpusUiText.ReplanBody(approved, 12), CorpusUiText.ReplanBody(approved, 12, relabels: 0));
        Assert.Equal(CorpusUiText.ReplanBody(approved, 12) + " 3 manual relabels in the answer key will be dropped.",
            CorpusUiText.ReplanBody(approved, 12, relabels: 3));
        Assert.Equal(CorpusUiText.ReplanBody(approved) + " 1 manual relabel in the answer key will be dropped.",
            CorpusUiText.ReplanBody(approved, relabels: 1));
    }

    [Fact]
    public void SettingsNotice_Relabels_OnlyOnTheReplaceNotice()
    {
        Assert.Equal(CorpusUiText.SettingsNotice(true, false, null), CorpusUiText.SettingsNotice(true, false, null, 0));
        Assert.Equal(
            "Changing these settings replaces the current plan when you click Next. Skip to Discovery Documents leaves the plan as it is. 2 manual relabels in the answer key will be dropped.",
            CorpusUiText.SettingsNotice(true, false, null, relabels: 2));

        var date = new DateTime(2024, 3, 5, 14, 7, 0);
        Assert.Equal(CorpusUiText.SettingsNotice(true, true, date), CorpusUiText.SettingsNotice(true, true, date, relabels: 2));
        Assert.Equal(CorpusUiText.SettingsNotice(false, false, null), CorpusUiText.SettingsNotice(false, false, null, relabels: 2));
    }

    [Fact]
    public void ReplanGeneratedThreads_CountsWrittenThreadsOfTheCurrentPlanOnly()
    {
        var planId = Guid.NewGuid();
        var corpus = new CorpusState
        {
            PlanId = planId,
            Generation = new Models.Evidence.GenerationState { RunPlanId = planId, ThreadsWritten = 7, ThreadsWithWarnings = 2, ThreadsFailed = 3 }
        };
        Assert.Equal(9, StepReviewPlan.ReplanGeneratedThreads(corpus));

        corpus.Generation.RunPlanId = Guid.NewGuid();
        Assert.Equal(0, StepReviewPlan.ReplanGeneratedThreads(corpus));

        corpus.Generation = null;
        Assert.Equal(0, StepReviewPlan.ReplanGeneratedThreads(corpus));
        Assert.Equal(0, StepReviewPlan.ReplanGeneratedThreads(null));
    }

    // --- Case Summary rows ---

    [Theory]
    [InlineData(1000, 312, "20", "1,000 emails in 312 threads · 20% relevant · Jan 2024 to Aug 2024")]
    [InlineData(1, 1, "100", "1 email in 1 thread · 100% relevant · Jan 2024 to Aug 2024")]
    [InlineData(2, 2, "19.6", "2 emails in 2 threads · 19.6% relevant · Jan 2024 to Aug 2024")]
    [InlineData(0, 0, "0", "0 emails in 0 threads · 0% relevant · Jan 2024 to Aug 2024")]
    public void CorpusPlanLine_Text(int emails, int threads, string percent, string expected)
    {
        Assert.Equal(expected, CorpusUiText.CorpusPlanLine(emails, threads, decimal.Parse(percent, System.Globalization.CultureInfo.InvariantCulture),
            new DateTime(2024, 1, 10), new DateTime(2024, 8, 3)));
    }

    [Theory]
    [InlineData("20", "20%")]
    [InlineData("20.0", "20%")]
    [InlineData("19.6", "19.6%")]
    [InlineData("8.25", "8.3%")]
    public void PercentText_WholeOrOneDecimal(string value, string expected)
    {
        Assert.Equal(expected, CorpusUiText.PercentText(decimal.Parse(value, System.Globalization.CultureInfo.InvariantCulture)));
    }

    [Fact]
    public void PlanStatusLine_Variants()
    {
        var approved = new DateTime(2024, 3, 5, 14, 7, 0);
        Assert.Equal("Not planned yet", CorpusUiText.NotPlannedLine);
        Assert.Equal("Approved Mar 5, 2024 2:07 PM", CorpusUiText.PlanStatusLine(true, approved, false));
        Assert.Equal("Not approved yet", CorpusUiText.PlanStatusLine(false, null, false));
        Assert.Equal("Out of date with the case guide", CorpusUiText.PlanStatusLine(false, null, true));
        Assert.Equal("Out of date with the case guide", CorpusUiText.PlanStatusLine(true, approved, true));
    }

    [Theory]
    [InlineData(1240, 1000, 240, "answer-key.csv and answer-key.xlsx · 1,240 rows (1,000 emails, 240 attachments)")]
    [InlineData(1, 1, 0, "answer-key.csv and answer-key.xlsx · 1 row (1 emails, 0 attachments)")]
    [InlineData(2, 2, 0, "answer-key.csv and answer-key.xlsx · 2 rows (2 emails, 0 attachments)")]
    [InlineData(0, 0, 0, "answer-key.csv and answer-key.xlsx · 0 rows (0 emails, 0 attachments)")]
    public void AnswerKeyLine_Text(int rows, int emails, int attachments, string expected)
    {
        Assert.Equal(expected, CorpusUiText.AnswerKeyLine(rows, emails, attachments));
    }

    [Theory]
    [InlineData(2, "Coding manual for reviewers · 2 issue codes · Brightline v. Harmon - Review Protocol.docx")]
    [InlineData(1, "Coding manual for reviewers · 1 issue code · Brightline v. Harmon - Review Protocol.docx")]
    [InlineData(0, "Coding manual for reviewers · 0 issue codes · Brightline v. Harmon - Review Protocol.docx")]
    [InlineData(1000, "Coding manual for reviewers · 1,000 issue codes · Brightline v. Harmon - Review Protocol.docx")]
    public void ProtocolLine_Text(int issues, string expected)
    {
        Assert.Equal(expected, CorpusUiText.ProtocolLine(issues, "Brightline v. Harmon - Review Protocol.docx"));
    }

    [Fact]
    public void ProtocolFileName_UsesFolderName()
    {
        Assert.Equal("Brightline v. Harmon - Review Protocol.docx", CorpusUiText.ProtocolFileName("Brightline v. Harmon"));
    }

    [Fact]
    public void FileStatusLine_Variants()
    {
        var written = new DateTime(2024, 3, 5, 9, 30, 0);
        Assert.Equal("Written Mar 5, 2024 9:30 AM", CorpusUiText.FileStatusLine(CorpusFileStatus.Written, written, ""));
        Assert.Equal("Written for an earlier plan. Approve the current plan to rewrite it.", CorpusUiText.FileStatusLine(CorpusFileStatus.EarlierPlan, written, ""));
        Assert.Equal("Not written yet. Approve the plan on Review Plan.", CorpusUiText.FileStatusLine(CorpusFileStatus.NotWritten, null, ""));
        Assert.Equal("Could not write: disk full", CorpusUiText.FileStatusLine(CorpusFileStatus.Failed, null, "disk full."));
    }

    [Fact]
    public void MissingFileText_NamesTheFile()
    {
        Assert.Equal("answer-key.xlsx is no longer in the case folder. Approve the plan again on Review Plan to rewrite it.",
            CorpusUiText.MissingFileText("answer-key.xlsx"));
    }
}
