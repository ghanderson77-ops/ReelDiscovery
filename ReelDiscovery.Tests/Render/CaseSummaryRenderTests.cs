using ReelDiscovery.Helpers;
using ReelDiscovery.Models;
using ReelDiscovery.Models.Case;
using ReelDiscovery.Models.Corpus;
using ReelDiscovery.Models.Evidence;
using ReelDiscovery.Models.Qa;
using ReelDiscovery.Services.Ai;
using ReelDiscovery.Services.Case;
using ReelDiscovery.Services.Corpus;
using ReelDiscovery.Services.Evidence;
using ReelDiscovery.Services.Qa;
using ReelDiscovery.Tests.Case;
using ReelDiscovery.Tests.Corpus;
using ReelDiscovery.Tests.Qa;
using ReelDiscovery.UserControls;

namespace ReelDiscovery.Tests.Render;

/// <summary>
/// Renders the Case Summary step at 144 DPI with a case folder under a temp root (UI-SPEC S2; Corpus group 04-UI-SPEC S3).
/// </summary>
[Trait("Category", "Render")]
[Collection(RenderCollection.Name)]
public class CaseSummaryRenderTests
{
    [Fact]
    public void Summary_BeforeExport()
    {
        RenderHarness.RunSta(() =>
        {
            foreach (var size in new[] { RenderHarness.Large, RenderHarness.WizardMin })
            {
                using var fixture = new SummaryFixture(CaseTestData.SampleGuidedDraft());
                var result = Render(fixture, "case-summary-before-export", size);

                AssertRendered(result);
            }
        });
    }

    [Fact]
    public void Summary_AfterExport()
    {
        RenderHarness.RunSta(() =>
        {
            using var fixture = new SummaryFixture(CaseTestData.SampleGuidedDraft());
            var folder = fixture.State.Case.FolderPath;
            foreach (var kind in new[] { CaseExportKind.CoreDocument, CaseExportKind.Cast, CaseExportKind.KeyFacts })
            {
                var path = Path.Combine(folder, CaseGuideDocxExporter.FileNameFor(kind, Path.GetFileName(folder)));
                File.WriteAllBytes(path, CaseGuideDocxExporter.Build(fixture.State.Case, kind));
            }

            var openButtons = 0;
            var exportLines = 0;
            var result = Render(fixture, "case-summary-after-export", RenderHarness.Large, control =>
            {
                openButtons = AllControls(control).OfType<Button>().Count(b => b.Text == "Open in Word" && b.Visible);
                exportLines = AllControls(control).OfType<Label>().Count(l => l.Text.StartsWith("Exported ", StringComparison.Ordinal) && l.Text.EndsWith(".docx", StringComparison.Ordinal));
            });

            AssertRendered(result);
            Assert.Equal(3, openButtons);
            Assert.Equal(3, exportLines);
        });
    }

    [Fact]
    public void Summary_WithErrors()
    {
        RenderHarness.RunSta(() =>
        {
            var draft = CaseTestData.SampleGuidedDraft();
            // Orphan link: a fact points to an issue that does not exist
            draft.Guide!.Facts[0].IssueIds.Add("ISS-99");
            using var fixture = new SummaryFixture(draft);

            Color? checksColor = null;
            var result = Render(fixture, "case-summary-with-errors", RenderHarness.Large, control =>
            {
                var line = AllControls(control).OfType<Label>()
                    .FirstOrDefault(l => l.Text.Contains("error", StringComparison.Ordinal) && l.Text.Contains("warning", StringComparison.Ordinal));
                checksColor = line?.ForeColor;
            });

            AssertRendered(result);
            Assert.Equal(Helpers.Theme.Danger, checksColor);
        });
    }

    [Fact]
    public void Summary_NoCorpusPlan()
    {
        RenderHarness.RunSta(() =>
        {
            foreach (var size in new[] { RenderHarness.Large, RenderHarness.WizardMin })
            {
                using var fixture = new SummaryFixture(CaseTestData.SampleGuidedDraft());

                var notPlanned = false;
                var planCorpusVisible = false;
                var reviewPlanVisible = true;
                var horizontalScroll = true;
                var result = Render(fixture, "case-summary-no-corpus-plan", size, control =>
                {
                    notPlanned = AllControls(control).OfType<Label>().Any(l => l.Text == CorpusUiText.NotPlannedLine && l.Visible);
                    planCorpusVisible = VisibleButton(control, "Plan Corpus");
                    reviewPlanVisible = VisibleButton(control, "Review Plan");
                    horizontalScroll = RowsHost(control).HorizontalScroll.Visible;
                });

                AssertRendered(result);
                Assert.True(notPlanned);
                Assert.True(planCorpusVisible);
                Assert.False(reviewPlanVisible);
                Assert.False(horizontalScroll);
            }
        });
    }

    [Fact]
    public void Summary_CorpusApproved()
    {
        RenderHarness.RunSta(() =>
        {
            foreach (var size in new[] { RenderHarness.Large, RenderHarness.WizardMin })
            {
                using var fixture = new SummaryFixture(CorpusTestData.RichDraft());
                var draft = fixture.State.Case;
                var folder = draft.FolderPath;
                var plan = PlanFixtures.ValidPlan(draft);
                CorpusPlanStore.SaveAsync(folder, plan).GetAwaiter().GetResult();

                // Written files for the current plan, as Approve Plan leaves them
                var guide = draft.Guide!;
                var rows = AnswerKeyBuilder.Build(plan, guide);
                var protocolName = ReviewProtocolBuilder.FileNameFor(Path.GetFileName(folder));
                File.WriteAllBytes(Path.Combine(folder, AnswerKeyCsvWriter.FileName), AnswerKeyCsvWriter.Build(rows));
                File.WriteAllBytes(Path.Combine(folder, AnswerKeyXlsxWriter.FileName), AnswerKeyXlsxWriter.Build(rows, guide));
                File.WriteAllBytes(Path.Combine(folder, protocolName), ReviewProtocolBuilder.Build(draft, ReviewProtocolBuilder.FallbackProse(draft)));

                var writtenUtc = new DateTime(2024, 9, 3, 15, 30, 0, DateTimeKind.Utc);
                draft.Corpus = new CorpusState
                {
                    Settings = plan.Settings,
                    PlanId = plan.PlanId,
                    PlanGuideHash = plan.GuideHash,
                    PlanSettingsHash = plan.SettingsHash,
                    Approved = true,
                    ApprovedUtc = writtenUtc,
                    WrittenFiles = new List<WrittenCorpusFile>
                    {
                        new() { Kind = CorpusFileKind.AnswerKeyCsv, FileName = AnswerKeyCsvWriter.FileName, PlanId = plan.PlanId, WrittenUtc = writtenUtc },
                        new() { Kind = CorpusFileKind.AnswerKeyXlsx, FileName = AnswerKeyXlsxWriter.FileName, PlanId = plan.PlanId, WrittenUtc = writtenUtc },
                        new() { Kind = CorpusFileKind.ReviewProtocol, FileName = protocolName, PlanId = plan.PlanId, WrittenUtc = writtenUtc }
                    }
                };

                var buttons = new Dictionary<string, bool>();
                var writtenLines = 0;
                var approvedLines = 0;
                var horizontalScroll = true;
                var result = Render(fixture, "case-summary-corpus-approved", size, control =>
                {
                    // The Corpus group comes first, so the top of the contents shows it
                    foreach (var caption in new[] { "Open CSV", "Open in Excel", "Open in Word", "Review Plan", "Plan Corpus" })
                        buttons[caption] = VisibleButton(control, caption);
                    var labels = AllControls(control).OfType<Label>().Where(l => l.Visible).ToList();
                    writtenLines = labels.Count(l => l.Text.StartsWith("Written ", StringComparison.Ordinal));
                    approvedLines = labels.Count(l => l.Text.StartsWith("Approved ", StringComparison.Ordinal));
                    horizontalScroll = RowsHost(control).HorizontalScroll.Visible;
                });

                AssertRendered(result);
                Assert.True(buttons["Open CSV"]);
                Assert.True(buttons["Open in Excel"]);
                Assert.True(buttons["Open in Word"]);
                Assert.True(buttons["Review Plan"]);
                Assert.False(buttons["Plan Corpus"]);
                Assert.Equal(2, writtenLines);
                Assert.Equal(1, approvedLines);
                Assert.False(horizontalScroll);
            }
        });
    }

    // ----- Render state 7 (05-UI-SPEC): Mailboxes + Story so far rows, scrolled to the Corpus group -----

    public enum RunSeed
    {
        NotGenerated,
        Partial,
        Complete,
        EarlierPlan
    }

    [Theory]
    [InlineData(RunSeed.NotGenerated)]
    [InlineData(RunSeed.Partial)]
    [InlineData(RunSeed.Complete)]
    [InlineData(RunSeed.EarlierPlan)]
    public void Summary_GeneratedCorpusRows(RunSeed seed)
    {
        RenderHarness.RunSta(() =>
        {
            foreach (var size in new[] { RenderHarness.Large, RenderHarness.WizardMin })
            {
                using var fixture = new SummaryFixture(CorpusTestData.RichDraft());
                var plan = SeedApprovedCorpus(fixture);
                SeedRun(fixture, plan, seed);

                var mailboxesLine = string.Empty;
                Color mailboxesColor = default;
                Color mailboxesDot = default;
                var storyLine = string.Empty;
                Color storyColor = default;
                Color storyDot = default;
                var answerKeyLine = string.Empty;
                var openMailboxes = false;
                var generateEmails = false;
                var generateEmailsText = string.Empty;
                var openStory = false;
                var exportStory = false;
                var horizontalScroll = true;
                var name = $"case-summary-run-{seed.ToString().ToLowerInvariant()}";
                var result = Render(fixture, name, size, control =>
                {
                    var step = (StepCaseSummary)control;
                    // Scroll the Corpus group's last row into view
                    var host = RowsHost(control);
                    host.ScrollControlIntoView(step.StoryLine3.Parent!);
                    mailboxesLine = step.MailboxesLine3.Text;
                    mailboxesColor = step.MailboxesLine3.ForeColor;
                    mailboxesDot = step.MailboxesDotColor;
                    storyLine = step.StoryLine3.Text;
                    storyColor = step.StoryLine3.ForeColor;
                    storyDot = step.StoryDotColor;
                    answerKeyLine = step.AnswerKeyLine3.Text;
                    openMailboxes = step.OpenMailboxesButton.Visible;
                    generateEmails = step.GenerateEmailsButton.Visible;
                    generateEmailsText = step.GenerateEmailsButton.Text;
                    openStory = step.OpenStoryButton.Visible;
                    exportStory = step.ExportStoryButton.Visible;
                    horizontalScroll = host.HorizontalScroll.Visible;
                });

                AssertRendered(result);
                Assert.False(horizontalScroll);
                switch (seed)
                {
                    case RunSeed.NotGenerated:
                        Assert.Equal("Not generated yet", mailboxesLine);
                        Assert.Equal(Helpers.Theme.TextMuted, mailboxesColor);
                        Assert.Equal(Helpers.Theme.BorderStrong, mailboxesDot);
                        Assert.True(generateEmails);
                        Assert.Equal("Generate Emails", generateEmailsText);
                        Assert.False(openMailboxes);
                        Assert.Equal("Not written yet. It is written when a generation run ends.", storyLine);
                        Assert.Equal(Helpers.Theme.TextMuted, storyColor);
                        Assert.Equal(Helpers.Theme.BorderStrong, storyDot);
                        Assert.False(openStory);
                        Assert.False(exportStory);
                        Assert.DoesNotContain("generated", answerKeyLine);
                        break;
                    case RunSeed.Partial:
                        Assert.Equal("7 of 10 threads written · 1 item with warnings · 2 failed items", mailboxesLine);
                        Assert.Equal(Helpers.Theme.Warning, mailboxesColor);
                        Assert.Equal(Helpers.Theme.Warning, mailboxesDot);
                        Assert.True(openMailboxes);
                        // 09 W6: Case Summary always offers the way back to Generate Corpus
                        Assert.True(generateEmails);
                        Assert.Equal("Generate Corpus", generateEmailsText);
                        Assert.StartsWith("Could not write: ", storyLine);
                        Assert.Equal(Helpers.Theme.Danger, storyColor);
                        Assert.Equal(Helpers.Theme.Danger, storyDot);
                        Assert.False(openStory);
                        Assert.True(exportStory);
                        Assert.EndsWith($" · 18 generated, {Math.Max(0, plan.EmailCount + plan.AttachmentCount - 18 - 4):N0} planned", answerKeyLine);
                        break;
                    case RunSeed.Complete:
                        Assert.Equal("All 10 threads written", mailboxesLine);
                        Assert.Equal(Helpers.Theme.Success, mailboxesColor);
                        Assert.Equal(Helpers.Theme.Success, mailboxesDot);
                        Assert.True(openMailboxes);
                        // 09 W6: Case Summary always offers the way back to Generate Corpus
                        Assert.True(generateEmails);
                        Assert.Equal("Generate Corpus", generateEmailsText);
                        Assert.StartsWith("Written ", storyLine);
                        Assert.Equal(Helpers.Theme.Success, storyColor);
                        Assert.Equal(Helpers.Theme.Success, storyDot);
                        Assert.True(openStory);
                        Assert.True(exportStory);
                        Assert.StartsWith("Written ", answerKeyLine);
                        Assert.Contains(" generated, ", answerKeyLine);
                        break;
                    case RunSeed.EarlierPlan:
                        Assert.Equal("Written for an earlier plan. Start over on Generate Corpus.", mailboxesLine);
                        Assert.Equal(Helpers.Theme.Warning, mailboxesColor);
                        Assert.Equal(Helpers.Theme.Warning, mailboxesDot);
                        Assert.True(openMailboxes);
                        Assert.True(generateEmails);
                        Assert.Equal("Generate Corpus", generateEmailsText);
                        // The ledger belongs to another plan: no export from here, and no run clause on the key
                        Assert.False(exportStory);
                        Assert.DoesNotContain("generated", answerKeyLine);
                        break;
                }
            }
        });
    }

    // ----- Render check 8 (06-UI-SPEC S2): Mailboxes row over the full corpus, line 3 ellipsizes, tooltip has the breakdown -----

    [Fact]
    public void MailboxesRow_CompleteClean()
    {
        RenderHarness.RunSta(() =>
        {
            foreach (var size in new[] { RenderHarness.Large, RenderHarness.WizardMin })
            {
                using var fixture = new SummaryFixture(CorpusTestData.RichDraft());
                var plan = SeedApprovedCorpus(fixture);
                SeedFullCorpusRun(fixture, plan, partial: false);

                var (line, tooltip, color, keyLine, keyTooltip) = (string.Empty, string.Empty, default(Color), string.Empty, string.Empty);
                MailboxesButtons buttons = null!;
                var result = Render(fixture, "case-summary-mailboxes-complete", size, control =>
                {
                    var step = (StepCaseSummary)control;
                    RowsHost(control).ScrollControlIntoView(step.StoryLine3.Parent!);
                    buttons = CaptureMailboxesButtons(step);
                    line = step.MailboxesLine3.Text;
                    tooltip = step.MailboxesLine3Tooltip;
                    color = step.MailboxesLine3.ForeColor;
                    keyLine = step.AnswerKeyLine3.Text;
                    keyTooltip = step.AnswerKeyLine3Tooltip;
                });

                AssertRendered(result);
                AssertMailboxesButtonsFit(buttons);
                Assert.Equal("All 40 threads and 12 attachments written", line);
                Assert.Equal(Helpers.Theme.Success, color);
                Assert.Equal("Relevant: 10 of 10 threads. Non-relevant: 30 of 30 threads. Attachments: 12 of 12.", tooltip);
                // Generated counts email rows of every thread plus attachment rows
                Assert.EndsWith($" · 92 generated, {Math.Max(0, plan.EmailCount + plan.AttachmentCount - 92):N0} planned", keyLine);
                Assert.Equal(string.Empty, keyTooltip);
            }
        });
    }

    [Fact]
    public void MailboxesRow_PartialAllClauses()
    {
        RenderHarness.RunSta(() =>
        {
            foreach (var size in new[] { RenderHarness.Large, RenderHarness.WizardMin })
            {
                using var fixture = new SummaryFixture(CorpusTestData.RichDraft());
                var plan = SeedApprovedCorpus(fixture);
                SeedFullCorpusRun(fixture, plan, partial: true);

                var (line, tooltip, color, keyLine, keyTooltip) = (string.Empty, string.Empty, default(Color), string.Empty, string.Empty);
                MailboxesButtons buttons = null!;
                var result = Render(fixture, "case-summary-mailboxes-partial", size, control =>
                {
                    var step = (StepCaseSummary)control;
                    RowsHost(control).ScrollControlIntoView(step.StoryLine3.Parent!);
                    buttons = CaptureMailboxesButtons(step);
                    line = step.MailboxesLine3.Text;
                    tooltip = step.MailboxesLine3Tooltip;
                    color = step.MailboxesLine3.ForeColor;
                    keyLine = step.AnswerKeyLine3.Text;
                    keyTooltip = step.AnswerKeyLine3Tooltip;
                });

                // Line 3 may ellipsize (AutoEllipsis); the detector accepts that, the full text stays in Text and the tooltip
                AssertRendered(result);
                AssertMailboxesButtonsFit(buttons);
                Assert.Equal("34 of 40 threads written · 4 items with warnings · 4 failed items · 2 skipped items", line);
                Assert.Equal(Helpers.Theme.Warning, color);
                Assert.Equal("Relevant: 7 of 10 threads. Non-relevant: 27 of 30 threads. Attachments: 8 of 12 (2 skipped).", tooltip);
                // 60 emails + 8 attachments generated; 6 + 1 failed and 2 skipped count as neither generated nor planned
                Assert.EndsWith($" · 68 generated, {Math.Max(0, plan.EmailCount + plan.AttachmentCount - 68 - 7 - 2):N0} planned", keyLine);
                Assert.Equal("2 skipped", keyTooltip);
            }
        });
    }

    private sealed class MailboxesButtons
    {
        public bool GenerateVisible;
        public string GenerateText = "";
        public int GenerateWidth;
        public int GenerateTextWidth;
        public bool OpenVisible;
        public int OpenWidth;
        public int OpenTextWidth;
        public Rectangle GenerateScreen;
        public Rectangle OpenScreen;
        public Rectangle Line3Screen;
    }

    // 09 W6 (UI-SPEC S6, rule 47): Generate Corpus and Open Mailboxes Folder never ellipsize and never overlap the text column
    private static MailboxesButtons CaptureMailboxesButtons(StepCaseSummary step)
    {
        var generate = step.GenerateEmailsButton;
        var open = step.OpenMailboxesButton;
        return new MailboxesButtons
        {
            GenerateVisible = generate.Visible,
            GenerateText = generate.Text,
            GenerateWidth = generate.Width,
            GenerateTextWidth = TextRenderer.MeasureText(generate.Text, generate.Font).Width,
            OpenVisible = open.Visible,
            OpenWidth = open.Width,
            OpenTextWidth = TextRenderer.MeasureText(open.Text, open.Font).Width,
            GenerateScreen = generate.RectangleToScreen(generate.ClientRectangle),
            OpenScreen = open.RectangleToScreen(open.ClientRectangle),
            Line3Screen = step.MailboxesLine3.RectangleToScreen(step.MailboxesLine3.ClientRectangle)
        };
    }

    private static void AssertMailboxesButtonsFit(MailboxesButtons buttons)
    {
        Assert.True(buttons.GenerateVisible);
        Assert.Equal("Generate Corpus", buttons.GenerateText);
        Assert.True(buttons.GenerateWidth > buttons.GenerateTextWidth, $"Generate Corpus ellipsized: {buttons.GenerateWidth} <= {buttons.GenerateTextWidth}");
        Assert.True(buttons.OpenVisible);
        Assert.True(buttons.OpenWidth > buttons.OpenTextWidth, $"Open Mailboxes Folder ellipsized: {buttons.OpenWidth} <= {buttons.OpenTextWidth}");
        Assert.False(buttons.GenerateScreen.IntersectsWith(buttons.OpenScreen), "Mailboxes buttons overlap");
        Assert.True(buttons.OpenScreen.Left >= buttons.Line3Screen.Right, $"Button column overlaps line 3: {buttons.OpenScreen.Left} < {buttons.Line3Screen.Right}");
    }

    // ----- Render checks 5-6 (07-UI-SPEC S2, DPI rule 28): Generation cost row and the Quality checks group -----

    [Fact]
    public void QualityChecks_NotRun()
    {
        RenderHarness.RunSta(() =>
        {
            foreach (var size in new[] { RenderHarness.Large, RenderHarness.WizardMin })
            {
                using var fixture = new SummaryFixture(CorpusTestData.RichDraft());
                SeedApprovedCorpus(fixture);

                QaCapture capture = null!;
                var result = Render(fixture, "case-summary-qa-not-run", size, control => capture = CaptureQa((StepCaseSummary)control));

                AssertRendered(result);
                AssertQaRowsFit(capture);
                Assert.Equal("Nothing spent yet", capture.Lines[0]);
                Assert.Equal("Not run yet. They run when the emails are generated.", capture.Lines[1]);
                Assert.Equal("Not run yet.", capture.Lines[2]);
                Assert.Equal(QaUiText.BlindReviewNotRunNoEmails, capture.Lines[3]);
                // Run Audit and Run Blind Review... stay hidden until an email is generated
                Assert.Empty(capture.VisibleButtons);
                Assert.Equal(Helpers.Theme.BorderStrong, capture.Dots[1]);
            }
        });
    }

    [Fact]
    public void QualityChecks_Done_EveryCostClause()
    {
        RenderHarness.RunSta(() =>
        {
            foreach (var size in new[] { RenderHarness.Large, RenderHarness.WizardMin })
            {
                using var fixture = new SummaryFixture(CorpusTestData.RichDraft());
                var plan = SeedApprovedCorpus(fixture);
                SeedQaDone(fixture, plan);

                QaCapture capture = null!;
                var result = Render(fixture, "case-summary-qa-done", size, control => capture = CaptureQa((StepCaseSummary)control));

                AssertRendered(result);
                AssertQaRowsFit(capture);
                foreach (var clause in new[] { " spent · input $", " · cache write $", " · cache read $", " · output $", " · batch discount −$", " · images and voicemail $1.24", " · not itemized $0.37" })
                    Assert.Contains(clause, capture.Lines[0]);
                Assert.Contains("Not itemized: $0.37", capture.Tooltips[0]);
                Assert.Equal("2 flagged emails in 2 threads: 1 timeline, 1 value, 1 story-detail, 1 attachment", capture.Lines[1]);
                Assert.Equal(Helpers.Theme.Warning, capture.Dots[1]);
                Assert.StartsWith("Warning: metadata alone predicts relevance 66% of the time. Top signal: sender ", capture.Lines[2]);
                Assert.Contains("Pass is 60% or lower", capture.Tooltips[2]);
                Assert.Contains("2 disagreements", capture.Lines[3]);
                Assert.EndsWith(QaUiText.FallbackClause, capture.Lines[3]);
                Assert.Contains(QaUiText.FallbackTooltip, capture.Tooltips[3]);
                Assert.Equal(new[] { "View Findings", "View Report", "Run Audit", "View Findings", "Run Blind Review..." }, capture.VisibleButtons);
            }
        });
    }

    [Fact]
    public void QualityChecks_BlindReviewRunning()
    {
        RenderHarness.RunSta(() =>
        {
            // The review's awaits resume through the WinForms context; the harness pumps with Application.DoEvents, which
            // would otherwise swap that context out and strand the continuations (same setup as GenerateCorpusStepTests)
            WindowsFormsSynchronizationContext.AutoInstall = false;
            SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());
            foreach (var size in new[] { RenderHarness.Large, RenderHarness.WizardMin })
            {
                using var fixture = new SummaryFixture(CorpusTestData.RichDraft());
                var plan = SeedApprovedCorpus(fixture);
                SeedRun(fixture, plan, RunSeed.Complete);
                CaseSummaryQaTests.WriteJournal(fixture.State.Case.FolderPath, plan, "OpenAI");
                var reviewer = new CaseSummaryQaTests.GatedReviewer();
                var model = CaseSummaryQaTests.Haiku();
                ButtonStyle? cancelStyle = null;

                QaCapture capture = null!;
                var result = Render(fixture, "case-summary-qa-review-running", size, control =>
                {
                    var step = (StepCaseSummary)control;
                    step.ReviewerGatewayFactory = () => (new LlmGateway(reviewer, model, model), LlmProviderKind.Anthropic, false, model);
                    step.ShowDialogOverride = dialog =>
                    {
                        dialog.SampleInput.Value = 12;
                        return DialogResult.OK;
                    };
                    _ = step.RunBlindReviewAsync();
                    CaseSummaryQaTests.PumpUntil(() => step.IsReviewRunning, "review running");
                    reviewer.Release(3);
                    CaseSummaryQaTests.PumpUntil(() => step.BlindReviewLine3.Text == "Reviewing: 3 of 12 emails...", "3 of 12");
                    cancelStyle = (step.CancelReviewButton as ThemedButton)?.Variant;
                    capture = CaptureQa(step);
                });

                AssertRendered(result);
                AssertQaRowsFit(capture);
                Assert.Equal("Reviewing: 3 of 12 emails...", capture.Lines[3]);
                Assert.Equal(Helpers.Theme.Accent, capture.Colors[3]);
                Assert.Equal(Helpers.Theme.Accent, capture.Dots[3]);
                Assert.Equal(QaUiText.BlindReviewProgress(3, 12), capture.Status);
                Assert.Equal(ButtonStyle.Danger, cancelStyle);
                // Cancel Review replaces Run Blind Review...; View Findings is hidden while running
                Assert.Equal("Cancel Review", capture.VisibleButtons.Last());
                Assert.DoesNotContain("Run Blind Review...", capture.VisibleButtons);
            }
        });
    }

    // ----- Render check 9 (08-UI-SPEC S4-S5, DPI rule 40): Discovery group and the plan-independent cost row -----

    [Fact]
    public void DiscoveryRow_NotGenerated()
    {
        RenderHarness.RunSta(() =>
        {
            foreach (var size in new[] { RenderHarness.Large, RenderHarness.WizardMin })
            {
                using var fixture = new SummaryFixture(DiscoveryDraft());

                DiscoveryCapture capture = null!;
                var result = Render(fixture, "case-summary-discovery-not-generated", size,
                    control => capture = CaptureDiscovery((StepCaseSummary)control));

                AssertRendered(result);
                AssertDiscoveryRowFits(capture);
                Assert.Equal("Not generated", capture.Line3);
                Assert.Equal(Helpers.Theme.BorderStrong, capture.Dot);
                Assert.Equal(new[] { "Discovery Documents" }, capture.VisibleButtons);
                Assert.Equal("Nothing spent yet", capture.CostLine3);
            }
        });
    }

    [Fact]
    public void DiscoveryRow_ProblemState()
    {
        RenderHarness.RunSta(() =>
        {
            foreach (var size in new[] { RenderHarness.Large, RenderHarness.WizardMin })
            {
                var draft = DiscoveryDraft();
                using var fixture = new SummaryFixture(draft);
                CaseSummaryLogicTests.SeedDiscoveryEveryProblem(draft, draft.FolderPath);

                DiscoveryCapture capture = null!;
                var result = Render(fixture, "case-summary-discovery-problems", size,
                    control => capture = CaptureDiscovery((StepCaseSummary)control));

                AssertRendered(result);
                AssertDiscoveryRowFits(capture);
                Assert.Equal($"5 documents · last generated {CaseSummaryLogicTests.DiscoveryDateText} · 1 with warnings · 1 out of date"
                             + " · 1 not saved · 1 file missing · 1 failed", capture.Line3);
                Assert.Equal(Helpers.Theme.Danger, capture.Color);
                Assert.Equal(Helpers.Theme.Danger, capture.Dot);
                Assert.Equal(new[] { "Open Discovery Folder", "Discovery Documents" }, capture.VisibleButtons);
                // Six documents listed under the full line
                Assert.Equal(1 + 1 + 6, capture.Tooltip.Split(Environment.NewLine).Length);
            }
        });
    }

    [Fact]
    public void CostRow_NoCorpusPlan_WithDiscoverySpend()
    {
        RenderHarness.RunSta(() =>
        {
            foreach (var size in new[] { RenderHarness.Large, RenderHarness.WizardMin })
            {
                var draft = DiscoveryDraft();
                using var fixture = new SummaryFixture(draft);
                CaseSummaryLogicTests.SeedDiscoveryWarningsAndFailed(draft, draft.FolderPath);

                DiscoveryCapture capture = null!;
                var notPlanned = false;
                var result = Render(fixture, "case-summary-cost-no-corpus-discovery", size, control =>
                {
                    notPlanned = AllControls(control).OfType<Label>().Any(l => l.Text == CorpusUiText.NotPlannedLine && l.Visible);
                    capture = CaptureDiscovery((StepCaseSummary)control);
                });

                AssertRendered(result);
                AssertDiscoveryRowFits(capture);
                Assert.True(notPlanned);
                Assert.Equal("$1.50 spent · discovery documents $1.50", capture.CostLine3);
                Assert.Equal("Spent so far: $1.50\nDiscovery documents: $1.50", capture.CostTooltip);
                Assert.Equal(Helpers.Theme.Success, capture.CostDot);
            }
        });
    }

    private static CaseDraft DiscoveryDraft()
    {
        var draft = Discovery.DiscoveryTestData.LitigationDraft();
        DiscoveryStepModel.ApplyDefaults(draft);
        return draft;
    }

    /// <summary>
    /// The Discovery documents row and the Generation cost row: text, colors, tooltips, visible buttons and the layout facts
    /// DPI rule 40 checks.
    /// </summary>
    private sealed class DiscoveryCapture
    {
        public string Line3 = string.Empty;
        public Color Color;
        public Color Dot;
        public string Tooltip = string.Empty;
        public List<string> VisibleButtons = new();
        public string CostLine3 = string.Empty;
        public string CostTooltip = string.Empty;
        public Color CostDot;

        // Discovery line 3: single-line text height, label height, label right edge, button column left edge
        public int LineHeight;
        public int Height;
        public int Right;
        public int ButtonsLeft;

        // (caption, preferred width, actual width)
        public List<(string Caption, int Preferred, int Width)> Buttons = new();
    }

    private static DiscoveryCapture CaptureDiscovery(StepCaseSummary step)
    {
        RowsHost(step).ScrollControlIntoView(step.DiscoveryLine3.Parent!);
        Application.DoEvents();

        var label = step.DiscoveryLine3;
        var row = (TableLayoutPanel)label.Parent!;
        var buttons = row.Controls.OfType<FlowLayoutPanel>().Single();
        var capture = new DiscoveryCapture
        {
            Line3 = label.Text,
            Color = label.ForeColor,
            Dot = step.DiscoveryDotColor,
            Tooltip = step.TooltipOf(label),
            CostLine3 = step.GenerationCostLine3.Text,
            CostTooltip = step.GenerationCostTooltip,
            CostDot = step.GenerationCostDotColor,
            LineHeight = TextRenderer.MeasureText("Ag", label.Font).Height,
            Height = label.Height,
            Right = label.Right,
            ButtonsLeft = buttons.Left
        };
        foreach (var button in new[] { step.OpenDiscoveryButton, step.DiscoveryDocumentsButton })
        {
            if (!button.Visible)
                continue;
            capture.VisibleButtons.Add(button.Text);
            capture.Buttons.Add((button.Text, button.GetPreferredSize(Size.Empty).Width, button.Width));
        }
        return capture;
    }

    // DPI rule 40: line 3 is one line (ellipsis + tooltip), the buttons never ellipsize and never overlap line 3
    private static void AssertDiscoveryRowFits(DiscoveryCapture capture)
    {
        Assert.True(capture.Height < 2 * capture.LineHeight,
            $"line 3 '{capture.Line3}' is {capture.Height}px, more than one line ({capture.LineHeight}px)");
        Assert.True(capture.Right <= capture.ButtonsLeft,
            $"line 3 '{capture.Line3}' ends at {capture.Right}, past the button column at {capture.ButtonsLeft}");
        foreach (var (caption, preferred, width) in capture.Buttons)
            Assert.True(width >= preferred, $"'{caption}' is {width}px, needs {preferred}px");
    }

    /// <summary>
    /// What the four new rows show (Generation cost, Consistency scans, Leakage audit, Blind review): line 3 text, color,
    /// dot and tooltip; the visible QA buttons; and the layout facts DPI rule 28 checks.
    /// </summary>
    private sealed class QaCapture
    {
        public List<string> Lines = new();
        public List<Color> Colors = new();
        public List<Color> Dots = new();
        public List<string> Tooltips = new();
        public List<string> VisibleButtons = new();
        public string Status = string.Empty;

        // (line 3 text, single-line text height, label height, label right edge, button column left edge or -1)
        public List<(string Text, int LineHeight, int Height, int Right, int ButtonsLeft)> Line3Layout = new();

        // (caption, preferred width, actual width)
        public List<(string Caption, int Preferred, int Width)> Buttons = new();
    }

    private static QaCapture CaptureQa(StepCaseSummary step)
    {
        // The Quality checks group is below the Corpus group; bring the last QA row into view
        RowsHost(step).ScrollControlIntoView(step.BlindReviewLine3.Parent!);
        Application.DoEvents();

        var capture = new QaCapture { Status = step.StatusLabel.Text };
        var labels = new[] { step.GenerationCostLine3, step.ScansLine3, step.LeakageLine3, step.BlindReviewLine3 };
        var dots = new[] { step.GenerationCostDotColor, step.ScansDotColor, step.LeakageDotColor, step.BlindReviewDotColor };
        for (var i = 0; i < labels.Length; i++)
        {
            var label = labels[i];
            capture.Lines.Add(label.Text);
            capture.Colors.Add(label.ForeColor);
            capture.Dots.Add(dots[i]);
            capture.Tooltips.Add(step.TooltipOf(label));
            var row = (TableLayoutPanel)label.Parent!;
            var buttons = row.Controls.OfType<FlowLayoutPanel>().FirstOrDefault();
            var anyButton = buttons != null && buttons.Controls.OfType<Button>().Any(b => b.Visible);
            capture.Line3Layout.Add((label.Text, TextRenderer.MeasureText("Ag", label.Font).Height, label.Height, label.Right,
                anyButton ? buttons!.Left : -1));
        }

        foreach (var button in new[] { step.ViewScansButton, step.ViewLeakageButton, step.RunAuditButton, step.ViewBlindReviewButton, step.RunBlindReviewButton, step.CancelReviewButton })
        {
            if (!button.Visible)
                continue;
            capture.VisibleButtons.Add(button.Text);
            capture.Buttons.Add((button.Text, button.GetPreferredSize(Size.Empty).Width, button.Width));
        }
        return capture;
    }

    // DPI rule 28: line 3 is one line with the full text in its tooltip, buttons never ellipsize, and the button column
    // never overlaps line 3
    private static void AssertQaRowsFit(QaCapture capture)
    {
        for (var i = 0; i < capture.Line3Layout.Count; i++)
        {
            var (text, lineHeight, height, right, buttonsLeft) = capture.Line3Layout[i];
            Assert.True(height < 2 * lineHeight, $"line 3 '{text}' is {height}px, more than one line ({lineHeight}px)");
            if (buttonsLeft >= 0)
                Assert.True(right <= buttonsLeft, $"line 3 '{text}' ends at {right}, past the button column at {buttonsLeft}");
            // Generation cost carries its breakdown; the QA rows start with the full line
            if (i == 0)
                Assert.NotEmpty(capture.Tooltips[i]);
            else
                Assert.StartsWith(text, capture.Tooltips[i]);
        }

        foreach (var (caption, preferred, width) in capture.Buttons)
            Assert.True(width >= preferred, $"'{caption}' is {width}px, needs {preferred}px");
    }

    /// <summary>
    /// A finished run with every QA report: itemized spend, a pre-Phase-7 (not itemized) line and media at run end, scans
    /// with findings, a leakage Warning, and a same-provider blind review with two disagreements.
    /// </summary>
    private static void SeedQaDone(SummaryFixture fixture, CorpusPlan plan)
    {
        var draft = fixture.State.Case;
        var folder = draft.FolderPath;
        var guide = draft.Guide!;
        SeedRun(fixture, plan, RunSeed.Complete);
        CaseSummaryQaTests.WriteJournal(folder, plan, "Anthropic");
        new GenerationJournal(folder).AppendAsync(new JournalRecord
        {
            Type = JournalRecordType.RunEnded,
            PlanId = plan.PlanId,
            Outcome = GenerationOutcome.Finished,
            CostUsd = 0.37m,
            MediaCostUsd = 1.24m,
            Utc = DateTime.UtcNow
        }).GetAwaiter().GetResult();

        var store = new QaReportStore(folder);
        var threads = plan.Threads.Where(t => t.Messages.Count > 0).Take(2).ToList();
        ScanFinding Finding(PlannedThread thread, ScanCheck check, string text) => new()
        {
            ThreadId = thread.Id,
            DocId = thread.Messages[0].DocId,
            Subject = thread.Subject,
            DateSent = thread.Messages[0].SentAt.DateTime,
            Check = check,
            Text = text,
            Relevant = thread.Relevance == ThreadRelevance.Relevant
        };
        store.SaveScans(new ScanReport
        {
            PlanId = plan.PlanId,
            CreatedUtc = DateTime.UtcNow,
            JournalUtc = File.GetLastWriteTimeUtc(GenerationJournal.PathFor(folder)),
            ScannedEmails = plan.Threads.Sum(t => t.Messages.Count),
            Findings = new List<ScanFinding>
            {
                Finding(threads[0], ScanCheck.Timeline, "Mentions the March 9 site meeting before it happened"),
                Finding(threads[0], ScanCheck.Value, "States $18,000 where the case guide says $18,425"),
                Finding(threads[1], ScanCheck.StoryDetail, "Moves the meeting to the west entrance"),
                Finding(threads[1], ScanCheck.Attachment, "The attached change order is dated after the email")
            }
        });
        store.SaveLeakage(new LeakageReport
        {
            PlanId = plan.PlanId,
            CreatedUtc = DateTime.UtcNow,
            Verdict = LeakageVerdict.Warning,
            BalancedAccuracy = 0.66,
            Auc = 0.71,
            Folds = LeakageAudit.Folds,
            RelevantEmails = 120,
            NonRelevantEmails = 380,
            Signals = new List<LeakageSignal>
            {
                new() { Feature = "sender raymond.harmon@harmonbuilders.com", Kind = LeakageSignalKind.Lift, RelevantShare = 0.71, BaseRate = 0.24, Support = 64, Lift = 0.47 }
            }
        });

        var generated = BlindReviewer.GeneratedEmailDocIds(GenerationJournal.Replay(folder, plan));
        var sample = BlindReviewPrompts.Sample(plan, guide, generated, Math.Min(10, generated.Count));
        store.AppendBlindReviewHeaderAsync(new BlindReviewHeader
        {
            PlanId = plan.PlanId,
            ReviewerProvider = "Anthropic",
            ReviewerModel = "claude-haiku-4-5-20251001",
            SameProviderFallback = true,
            SampleSize = sample.Count,
            StartedUtc = DateTime.UtcNow
        }).GetAwaiter().GetResult();
        for (var i = 0; i < sample.Count; i++)
        {
            var thread = plan.Threads.First(t => t.Messages.Any(m => m.DocId == sample[i]));
            var relevant = thread.Relevance == ThreadRelevance.Relevant;
            store.AppendBlindReviewVerdictAsync(new BlindReviewVerdict
            {
                DocId = sample[i],
                ThreadId = thread.Id,
                KeyRelevant = relevant,
                // Two disagreements: the first two emails of the sample
                ReviewerRelevant = i < 2 ? !relevant : relevant,
                Reason = "Only arranges a site visit; no defect or payment is discussed.",
                Utc = DateTime.UtcNow
            }).GetAwaiter().GetResult();
        }
    }

    // Phase 6 run over relevant + non-relevant threads and attachments (cached counts as GenerationStateApplier writes them)
    private static void SeedFullCorpusRun(SummaryFixture fixture, CorpusPlan plan, bool partial)
    {
        var draft = fixture.State.Case;
        Directory.CreateDirectory(Path.Combine(draft.FolderPath, PlanNumbering.MailboxesFolder));
        draft.Corpus!.Generation = new GenerationState
        {
            RunPlanId = plan.PlanId,
            ThreadsTotal = 10,
            ThreadsWritten = partial ? 6 : 10,
            ThreadsWithWarnings = partial ? 1 : 0,
            ThreadsFailed = partial ? 2 : 0,
            NonRelevantTotal = 30,
            NonRelevantWritten = partial ? 25 : 30,
            NonRelevantWithWarnings = partial ? 2 : 0,
            NonRelevantFailed = partial ? 1 : 0,
            AttachmentsTotal = 12,
            AttachmentsWritten = partial ? 7 : 12,
            AttachmentsWithWarnings = partial ? 1 : 0,
            AttachmentsFailed = partial ? 1 : 0,
            AttachmentsSkipped = partial ? 2 : 0,
            EmailsWritten = partial ? 60 : 80,
            EmailsFailed = partial ? 6 : 0,
            FilesWritten = partial ? 210 : 280,
            Mailboxes = 9
        };
    }

    private static void SeedRun(SummaryFixture fixture, CorpusPlan plan, RunSeed seed)
    {
        if (seed == RunSeed.NotGenerated)
            return;

        var draft = fixture.State.Case;
        var folder = draft.FolderPath;
        Directory.CreateDirectory(Path.Combine(folder, PlanNumbering.MailboxesFolder));
        var ledger = new NarrativeLedger
        {
            Entries = Enumerable.Range(1, 6).Select(i => new LedgerEntry
            {
                Id = $"T-000{i}-L01",
                Date = $"2024-03-0{i}",
                Kind = LedgerKind.Figure,
                Statement = $"Detail {i}.",
                SourceThreadId = $"T-000{(i + 1) / 2}",
                WaveIndex = 1
            }).ToList()
        };
        var generation = new GenerationState
        {
            RunPlanId = seed == RunSeed.EarlierPlan ? Guid.NewGuid() : plan.PlanId,
            Ledger = ledger,
            ThreadsTotal = 10,
            ThreadsWritten = seed == RunSeed.Partial ? 6 : 10,
            ThreadsWithWarnings = seed == RunSeed.Partial ? 1 : 0,
            ThreadsFailed = seed == RunSeed.Partial ? 2 : 0,
            EmailsWritten = seed == RunSeed.Partial ? 18 : 26,
            EmailsFailed = seed == RunSeed.Partial ? 4 : 0,
            FilesWritten = seed == RunSeed.Partial ? 61 : 88,
            Mailboxes = 7
        };
        if (seed == RunSeed.Partial)
            generation.StoryError = $"{StorySoFarDocxBuilder.FileNameFor(Path.GetFileName(folder))}: The process cannot access the file because it is being used by another process.";
        draft.Corpus!.Generation = generation;

        if (seed is RunSeed.Complete or RunSeed.EarlierPlan)
        {
            var storyName = StorySoFarDocxBuilder.FileNameFor(Path.GetFileName(folder));
            File.WriteAllBytes(Path.Combine(folder, storyName), new byte[] { 1 });
            draft.Corpus.WrittenFiles.Add(new WrittenCorpusFile
            {
                Kind = CorpusFileKind.StorySoFar,
                FileName = storyName,
                PlanId = generation.RunPlanId,
                WrittenUtc = new DateTime(2024, 9, 4, 16, 45, 0, DateTimeKind.Utc)
            });
        }
    }

    /// <summary>
    /// Saves a valid plan and the files Approve Plan writes, and records the approval. Returns the plan.
    /// </summary>
    private static CorpusPlan SeedApprovedCorpus(SummaryFixture fixture)
    {
        var draft = fixture.State.Case;
        var folder = draft.FolderPath;
        var plan = PlanFixtures.ValidPlan(draft);
        CorpusPlanStore.SaveAsync(folder, plan).GetAwaiter().GetResult();

        var guide = draft.Guide!;
        var rows = AnswerKeyBuilder.Build(plan, guide);
        var protocolName = ReviewProtocolBuilder.FileNameFor(Path.GetFileName(folder));
        File.WriteAllBytes(Path.Combine(folder, AnswerKeyCsvWriter.FileName), AnswerKeyCsvWriter.Build(rows));
        File.WriteAllBytes(Path.Combine(folder, AnswerKeyXlsxWriter.FileName), AnswerKeyXlsxWriter.Build(rows, guide));
        File.WriteAllBytes(Path.Combine(folder, protocolName), ReviewProtocolBuilder.Build(draft, ReviewProtocolBuilder.FallbackProse(draft)));

        var writtenUtc = new DateTime(2024, 9, 3, 15, 30, 0, DateTimeKind.Utc);
        draft.Corpus = new CorpusState
        {
            Settings = plan.Settings,
            PlanId = plan.PlanId,
            PlanGuideHash = plan.GuideHash,
            PlanSettingsHash = plan.SettingsHash,
            Approved = true,
            ApprovedUtc = writtenUtc,
            WrittenFiles = new List<WrittenCorpusFile>
            {
                new() { Kind = CorpusFileKind.AnswerKeyCsv, FileName = AnswerKeyCsvWriter.FileName, PlanId = plan.PlanId, WrittenUtc = writtenUtc },
                new() { Kind = CorpusFileKind.AnswerKeyXlsx, FileName = AnswerKeyXlsxWriter.FileName, PlanId = plan.PlanId, WrittenUtc = writtenUtc },
                new() { Kind = CorpusFileKind.ReviewProtocol, FileName = protocolName, PlanId = plan.PlanId, WrittenUtc = writtenUtc }
            }
        };
        return plan;
    }

    // Visible within the step: Control.Visible is false for children of a hidden form, so compare against the root
    private static bool VisibleButton(Control root, string caption) =>
        AllControls(root).OfType<Button>().Any(b => b.Text == caption && b.Visible);

    private static Panel RowsHost(Control root) =>
        AllControls(root).OfType<Panel>().First(p => p.AutoScroll && p is not TableLayoutPanel);

    private static RenderResult Render(SummaryFixture fixture, string name, Size size, Action<Control>? inspect = null)
    {
        return RenderHarness.RenderControl(() =>
        {
            var step = new StepCaseSummary();
            step.LoadForRender(fixture.State);
            return step;
        }, name, size, control => inspect?.Invoke(control));
    }

    private static IEnumerable<Control> AllControls(Control root)
    {
        foreach (Control child in root.Controls)
        {
            yield return child;
            foreach (var nested in AllControls(child))
                yield return nested;
        }
    }

    private static void AssertRendered(RenderResult result)
    {
        Assert.True(File.Exists(result.PngPath), result.PngPath);
        Assert.True(result.DeviceDpi >= RenderHarness.MinimumDpi, $"DPI {result.DeviceDpi}");
        Assert.Empty(result.Clipping);
    }

    /// <summary>
    /// A saved case under a temp Cases root; deleted on dispose. Tests write only here.
    /// </summary>
    private sealed class SummaryFixture : IDisposable
    {
        private readonly string _root;

        public SummaryFixture(CaseDraft draft)
        {
            _root = Path.Combine(Path.GetTempPath(), "rd-summary-" + Guid.NewGuid().ToString("N"));
            var store = new CaseStore(Path.Combine(_root, "Cases"), Path.Combine(_root, "AppData"));
            draft.AdvanceStage(CaseStage.Summary);
            store.EnsureFolder(draft);
            Assert.True(store.SaveNow(draft), store.LastSaveError);
            State = new WizardState { Case = draft, CaseStore = store };
        }

        public WizardState State { get; }

        public void Dispose()
        {
            try
            {
                Directory.Delete(_root, recursive: true);
            }
            catch (IOException)
            {
                // A file handle may linger briefly after the render; the temp folder is harmless
            }
        }
    }
}
