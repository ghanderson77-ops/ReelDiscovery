using System.Diagnostics;
using ReelDiscovery.Forms;
using ReelDiscovery.Helpers;
using ReelDiscovery.Models;
using ReelDiscovery.Models.Corpus;
using ReelDiscovery.Models.Evidence;
using ReelDiscovery.Models.Qa;
using ReelDiscovery.Services.Ai;
using ReelDiscovery.Services.Corpus;
using ReelDiscovery.Services.Evidence;
using ReelDiscovery.Services.Qa;
using ReelDiscovery.Tests.Evidence;
using ReelDiscovery.Tests.Render;
using ReelDiscovery.UserControls;

namespace ReelDiscovery.Tests.Qa;

/// <summary>
/// Case Summary's Generation cost row and Quality checks group (07-UI-SPEC S2 R1-R4): report loading, Run Audit, the blind
/// review lifecycle (start, progress, cancel, resume), View Findings with Regenerate Thread..., and the leave guard (C-Q3).
/// Not a render test: the step is never shown.
/// </summary>
public class CaseSummaryQaTests
{
    private const string NotRunScans = "Not run yet. They run when the emails are generated.";

    // ----- fixtures -----

    /// <summary>
    /// Journals a RunStarted line and one ThreadDone line per thread (every message written), each with an itemized cost.
    /// </summary>
    internal static void WriteJournal(string folder, CorpusPlan plan, string provider = "OpenAI", bool withCost = true)
    {
        var journal = new GenerationJournal(folder);
        journal.AppendAsync(new JournalRecord
        {
            Type = JournalRecordType.RunStarted,
            PlanId = plan.PlanId,
            Provider = provider,
            Utc = DateTime.UtcNow
        }).GetAwaiter().GetResult();

        foreach (var thread in plan.Threads)
        {
            journal.AppendAsync(new JournalRecord
            {
                Type = JournalRecordType.ThreadDone,
                PlanId = plan.PlanId,
                ThreadId = thread.Id,
                Status = ThreadWriteStatus.Written,
                Attempts = 1,
                Messages = thread.Messages.Select(m => new JournalMessage
                {
                    DocId = m.DocId,
                    Subject = thread.Subject,
                    BodyPlain = "Body of " + m.DocId + ". The schedule and the change order are discussed."
                }).ToList(),
                CostUsd = withCost ? 0.10m : 0m,
                CostDetail = withCost
                    ? new CostBreakdown { InputUsd = 0.04m, CacheWriteUsd = 0.01m, CacheReadUsd = 0.01m, OutputUsd = 0.05m, BatchSavingsUsd = 0.01m }
                    : null,
                Utc = DateTime.UtcNow
            }).GetAwaiter().GetResult();
        }
    }

    /// <summary>
    /// A written corpus: the journal, the Mailboxes folder and case.json's cached counts as a run end leaves them.
    /// </summary>
    internal static void SeedGenerated(GenerateCorpusRenderTests.StepFixture fixture, string provider = "OpenAI")
    {
        WriteJournal(fixture.Folder, fixture.Plan, provider);
        Directory.CreateDirectory(Path.Combine(fixture.Folder, PlanNumbering.MailboxesFolder));
        var replay = GenerationJournal.Replay(fixture.Folder, fixture.Plan);
        var state = GenerationRunState.From(fixture.Plan, replay);
        GenerationStateApplier.Apply(fixture.Draft, fixture.Plan, state, new NarrativeLedger(), new List<WrittenCorpusFile>(),
            GenerationOutcome.Finished, string.Empty, DateTime.UtcNow, final: true);
    }

    private static StepCaseSummary CreateStep(GenerateCorpusRenderTests.StepFixture fixture, List<string>? opened = null)
    {
        var step = new StepCaseSummary();
        step.OpenFile = path => opened?.Add(path);
        step.OpenFolder = path => opened?.Add(path);
        step.LoadForRender(fixture.State);
        return step;
    }

    internal static void PumpUntil(Func<bool> condition, string what)
    {
        var clock = Stopwatch.StartNew();
        while (!condition() && clock.Elapsed < TimeSpan.FromSeconds(30))
        {
            Application.DoEvents();
            Thread.Sleep(5);
        }
        Application.DoEvents();
        Assert.True(condition(), "timed out waiting for: " + what);
    }

    internal static AIModelConfig Haiku() =>
        AIModelConfig.GetDefaultModels().Single(m => m.ModelId == "claude-haiku-4-5-20251001");

    /// <summary>
    /// Answers each review once a permit is released (or at once when Open); relevant = the DocId's last digit is even.
    /// Calls are asynchronous so the step's UI thread keeps pumping.
    /// </summary>
    internal sealed class GatedReviewer : ILlmProvider
    {
        private readonly SemaphoreSlim _permits = new(0);
        public volatile bool Open;
        public int Calls;

        public LlmProviderKind Kind => LlmProviderKind.Anthropic;
        public int? MaxTokensCeiling => null;

        public void Release(int count) => _permits.Release(count);

        public async Task<LlmProviderResponse> SendAsync(LlmProviderRequest request, CancellationToken ct = default)
        {
            Interlocked.Increment(ref Calls);
            if (!Open)
                await _permits.WaitAsync(ct);
            await Task.Yield();
            var subject = request.UserPrompt.Split('\n').First(l => l.StartsWith("Subject: ", StringComparison.Ordinal));
            var relevant = subject.Length % 2 == 0 ? "true" : "false";
            return FakeLlmResponses.Ok($"{{\"relevant\":{relevant},\"issueCodes\":[],\"reason\":\"Routine scheduling only.\"}}");
        }

        public Task<LlmKeyTestResult> TestKeyAsync(string modelId, CancellationToken ct = default) =>
            Task.FromResult(new LlmKeyTestResult { Succeeded = true, Message = "OK" });
    }

    private static class FakeLlmResponses
    {
        public static LlmProviderResponse Ok(string text) =>
            new() { Outcome = LlmOutcome.Succeeded, Text = text, InputTokens = 400, OutputTokens = 40 };
    }

    // ----- not run -----

    [Fact]
    public void NoEmails_QaRowsNotRun_RunButtonsHidden_NothingSpent()
    {
        using var fixture = new GenerateCorpusRenderTests.StepFixture();

        GenerateCorpusStepTests.RunSta(() =>
        {
            using var step = CreateStep(fixture);

            Assert.Equal("Nothing spent yet", step.GenerationCostLine3.Text);
            Assert.Equal(NotRunScans, step.ScansLine3.Text);
            Assert.Equal("Not run yet.", step.LeakageLine3.Text);
            Assert.Equal(QaUiText.BlindReviewNotRunNoEmails, step.BlindReviewLine3.Text);
            Assert.False(step.ViewScansButton.Visible);
            Assert.False(step.ViewLeakageButton.Visible);
            Assert.False(step.RunAuditButton.Visible);
            Assert.False(step.ViewBlindReviewButton.Visible);
            Assert.False(step.RunBlindReviewButton.Visible);
            Assert.False(step.CancelReviewButton.Visible);
            Assert.Equal(Theme.BorderStrong, step.ScansDotColor);
        });
    }

    // ----- generation cost and scans -----

    [Fact]
    public void Generated_CostRowShowsBreakdown_AndRunButtonsAppear()
    {
        using var fixture = new GenerateCorpusRenderTests.StepFixture();
        SeedGenerated(fixture);
        var threads = fixture.Plan.Threads.Count;

        GenerateCorpusStepTests.RunSta(() =>
        {
            using var step = CreateStep(fixture);

            var expected = GenerationUiText.CostRow(true, 0.10m * threads, 0.04m * threads, 0.01m * threads, 0.01m * threads,
                0.05m * threads, 0.01m * threads, 0m, 0m);
            Assert.Equal(expected.Line3, step.GenerationCostLine3.Text);
            Assert.Equal(expected.Line3Tooltip, step.GenerationCostTooltip);
            Assert.True(step.RunAuditButton.Visible);
            Assert.True(step.RunBlindReviewButton.Visible);
            Assert.False(step.CancelReviewButton.Visible);
            // Same-provider fallback estimate on the not-run line: only the OpenAI key is tested
            Assert.StartsWith("Not run yet. ≈$", step.BlindReviewLine3.Text);
        });
    }

    [Fact]
    public void ScanFindings_ShowWarningAndViewFindings_OlderScanIsOutOfDate()
    {
        using var fixture = new GenerateCorpusRenderTests.StepFixture();
        SeedGenerated(fixture);
        var journalUtc = File.GetLastWriteTimeUtc(GenerationJournal.PathFor(fixture.Folder));
        var thread = fixture.Plan.Threads[0];
        var report = new ScanReport
        {
            PlanId = fixture.Plan.PlanId,
            CreatedUtc = DateTime.UtcNow,
            JournalUtc = journalUtc,
            ScannedEmails = 20,
            Findings = new List<ScanFinding>
            {
                new() { ThreadId = thread.Id, DocId = thread.Messages[0].DocId, Subject = thread.Subject, Check = ScanCheck.Timeline, Text = "Mentions the March 9 meeting before it happened", Relevant = true },
                new() { ThreadId = thread.Id, DocId = thread.Messages[0].DocId, Subject = thread.Subject, Check = ScanCheck.Value, Text = "States $18,000 where the guide says $18,425", Relevant = true }
            }
        };
        var store = new QaReportStore(fixture.Folder);
        store.SaveScans(report);

        GenerateCorpusStepTests.RunSta(() =>
        {
            using (var step = CreateStep(fixture))
            {
                Assert.Equal(QaUiText.ScansRow(report, outOfDate: false).Line3, step.ScansLine3.Text);
                Assert.Equal(Theme.Warning, step.ScansLine3.ForeColor);
                Assert.Equal(Theme.Warning, step.ScansDotColor);
                Assert.True(step.ViewScansButton.Visible);
            }

            // The corpus changed after the scan
            report.JournalUtc = journalUtc.AddMinutes(-5);
            store.SaveScans(report);
            using (var step = CreateStep(fixture))
            {
                Assert.Equal("Last scan is older than the corpus. It runs again at the end of the next run.", step.ScansLine3.Text);
                Assert.False(step.ViewScansButton.Visible);
            }
        });
    }

    // ----- leakage audit -----

    [Fact]
    public void RunAudit_RunsOffTheUiThread_SavesTheReport_AndUpdatesLine3()
    {
        using var fixture = new GenerateCorpusRenderTests.StepFixture();
        SeedGenerated(fixture);
        var store = new QaReportStore(fixture.Folder);
        Assert.Null(store.LoadLeakage());

        GenerateCorpusStepTests.RunSta(() =>
        {
            using var step = CreateStep(fixture);
            Assert.Equal("Not run yet.", step.LeakageLine3.Text);

            var run = step.RunAuditAsync();
            Assert.Equal(QaUiText.LeakageRunningText, step.StatusLabel.Text);
            Assert.Equal(Theme.Accent, step.StatusLabel.ForeColor);
            Assert.False(step.CanMoveNext);
            GenerateCorpusStepTests.Pump(run);

            var saved = store.LoadLeakage();
            Assert.NotNull(saved);
            Assert.Equal(fixture.Plan.PlanId, saved!.PlanId);
            Assert.Equal(QaUiText.LeakageLine(saved).Text, step.LeakageLine3.Text);
            Assert.True(step.ViewLeakageButton.Visible);
            Assert.NotEqual(QaUiText.LeakageRunningText, step.StatusLabel.Text);
            Assert.True(step.CanMoveNext);
        });
    }

    // ----- blind review -----

    [Fact]
    public void RunBlindReview_NoTestedKey_ShowsWarningAndOpensNoDialog()
    {
        using var fixture = new GenerateCorpusRenderTests.StepFixture();
        SeedGenerated(fixture);
        fixture.State.OpenAIKeyTested = false;
        fixture.State.AnthropicKeyTested = false;

        GenerateCorpusStepTests.RunSta(() =>
        {
            using var step = CreateStep(fixture);
            var dialogs = 0;
            step.ShowDialogOverride = _ =>
            {
                dialogs++;
                return DialogResult.Cancel;
            };

            GenerateCorpusStepTests.Pump(step.RunBlindReviewAsync());

            Assert.Equal(0, dialogs);
            Assert.Equal(QaUiText.NoReviewerKeyText, step.StatusLabel.Text);
            Assert.Equal(Theme.Warning, step.StatusLabel.ForeColor);
            Assert.False(step.IsReviewRunning);
        });
    }

    [Fact]
    public void BlindReview_ProgressCancelKeepsVerdicts_AndRerunFinishesTheRest()
    {
        using var fixture = new GenerateCorpusRenderTests.StepFixture();
        SeedGenerated(fixture);
        var reviewer = new GatedReviewer();
        var model = Haiku();

        GenerateCorpusStepTests.RunSta(() =>
        {
            using var step = CreateStep(fixture);
            step.ReviewerGatewayFactory = () => (new LlmGateway(reviewer, model, model), LlmProviderKind.Anthropic, false, model);
            var estimate = string.Empty;
            step.ShowDialogOverride = dialog =>
            {
                dialog.SampleInput.Value = 6;
                estimate = dialog.EstimateLabel.Text;
                return DialogResult.OK;
            };
            var confirms = new List<string>();
            step.Confirm = (title, text) =>
            {
                confirms.Add(title);
                return true;
            };

            var run = step.RunBlindReviewAsync();
            PumpUntil(() => step.IsReviewRunning, "review running");
            Assert.StartsWith("Estimated cost ≈$", estimate);
            Assert.Equal("Reviewing: 0 of 6 emails...", step.BlindReviewLine3.Text);
            Assert.Equal(Theme.Accent, step.BlindReviewLine3.ForeColor);
            Assert.Equal(Theme.Accent, step.BlindReviewDotColor);
            Assert.True(step.CancelReviewButton.Visible);
            Assert.False(step.RunBlindReviewButton.Visible);
            Assert.False(step.ViewBlindReviewButton.Visible);
            // Next/Back stay enabled during a review (S2 R4)
            Assert.True(step.CanMoveNext);

            reviewer.Release(2);
            PumpUntil(() => step.BlindReviewLine3.Text == "Reviewing: 2 of 6 emails...", "2 of 6");
            Assert.Equal(QaUiText.BlindReviewProgress(2, 6), step.StatusLabel.Text);
            Assert.Equal(Theme.Accent, step.StatusLabel.ForeColor);

            step.CancelReview();
            Assert.Equal(new[] { QaUiText.CQ2CancelReview(2).Title }, confirms);
            GenerateCorpusStepTests.Pump(run);

            Assert.False(step.IsReviewRunning);
            var (_, kept) = new QaReportStore(fixture.Folder).LoadBlindReview();
            Assert.Equal(2, kept.Count);
            Assert.Equal("2 of 6 emails reviewed; the review stopped. Run Blind Review again to finish the rest.", step.BlindReviewLine3.Text);
            Assert.Equal(Theme.Warning, step.BlindReviewLine3.ForeColor);
            Assert.True(step.RunBlindReviewButton.Visible);
            Assert.False(step.CancelReviewButton.Visible);

            // Rerun: only the 4 not yet reviewed are paid for
            reviewer.Open = true;
            var callsBefore = reviewer.Calls;
            step.ShowDialogOverride = dialog =>
            {
                Assert.Equal(6, (int)dialog.SampleInput.Value);
                Assert.Contains("4 emails not yet reviewed; 2 already reviewed are kept", dialog.EstimateLabel.Text);
                return DialogResult.OK;
            };
            GenerateCorpusStepTests.Pump(step.RunBlindReviewAsync());

            Assert.Equal(4, reviewer.Calls - callsBefore);
            var (header, verdicts) = new QaReportStore(fixture.Folder).LoadBlindReview();
            Assert.Equal(6, verdicts.Count);
            Assert.Equal("claude-haiku-4-5-20251001", header!.ReviewerModel);
            var disagreements = BlindReviewer.Disagreements(verdicts).Count;
            var expected = QaUiText.BlindReviewRow(new BlindReviewRowInput
            {
                State = BlindReviewState.Done,
                Model = model.DisplayName,
                SampleSize = 6,
                Reviewed = 6,
                Disagreements = disagreements
            });
            Assert.Equal(expected.Line3, step.BlindReviewLine3.Text);
            Assert.Equal(QaUiText.BlindReviewFinished(disagreements).Text, step.StatusLabel.Text);
            Assert.Equal(disagreements > 0, step.ViewBlindReviewButton.Visible);
        });
    }

    [Fact]
    public void Disagreements_ViewFindingsListsThem_RegenerateQueuesTheThread_MailboxesTurnPartial()
    {
        using var fixture = new GenerateCorpusRenderTests.StepFixture();
        SeedGenerated(fixture);
        var store = new QaReportStore(fixture.Folder);
        var generated = BlindReviewer.GeneratedEmailDocIds(GenerationJournal.Replay(fixture.Folder, fixture.Plan));
        var size = 4;
        var sample = BlindReviewPrompts.Sample(fixture.Plan, fixture.Draft.Guide!, generated, size);
        // The sample starts with a relevant email
        var docId = sample[0];
        var thread = fixture.Plan.Threads.Single(t => t.Messages.Any(m => m.DocId == docId));
        Assert.Equal(ThreadRelevance.Relevant, thread.Relevance);
        store.AppendBlindReviewHeaderAsync(new BlindReviewHeader
        {
            PlanId = fixture.Plan.PlanId,
            ReviewerProvider = "OpenAI",
            ReviewerModel = "gpt-5-mini",
            SameProviderFallback = true,
            SampleSize = size,
            StartedUtc = DateTime.UtcNow
        }).GetAwaiter().GetResult();
        foreach (var id in sample)
        {
            store.AppendBlindReviewVerdictAsync(new BlindReviewVerdict
            {
                DocId = id,
                ThreadId = fixture.Plan.Threads.First(t => t.Messages.Any(m => m.DocId == id)).Id,
                KeyRelevant = true,
                ReviewerRelevant = id != docId,
                Reason = id == docId ? "Only arranges a site visit; no defect or payment is discussed." : "Discusses the leak.",
                Utc = DateTime.UtcNow
            }).GetAwaiter().GetResult();
        }

        GenerateCorpusStepTests.RunSta(() =>
        {
            var opened = new List<string>();
            using var step = CreateStep(fixture, opened);
            Assert.Contains(" · same provider as the corpus", step.BlindReviewLine3.Text);
            Assert.Contains("1 disagreement", step.BlindReviewLine3.Text);
            Assert.True(step.ViewBlindReviewButton.Visible);
            // Planned attachments were never journaled, so the row may already be partial; the relevant count drops below
            var mailboxesBefore = step.MailboxesLine3Tooltip;

            QaFindingRow[] rows = Array.Empty<QaFindingRow>();
            string? confirmTitle = null;
            step.ShowFindingsOverride = dialog =>
            {
                rows = dialog.FindingsGrid.Rows.Cast<DataGridViewRow>().Select(r => (QaFindingRow)r.Tag!).ToArray();
                dialog.Opacity = 0;
                dialog.StartPosition = FormStartPosition.Manual;
                dialog.Show();
                dialog.FindingsGrid.ClearSelection();
                dialog.FindingsGrid.CurrentCell = dialog.FindingsGrid.Rows[0].Cells[dialog.FindingsGrid.Columns.Count - 1];
                dialog.FindingsGrid.Rows[0].Selected = true;
                dialog.Confirm = (title, text) =>
                {
                    confirmTitle = title;
                    return true;
                };
                GenerateCorpusStepTests.Pump(dialog.RegenerateSelectedAsync());

                // Open Email resolves the sender copy under the case folder; a missing file opens nothing
                dialog.OpenButton.PerformClick();
                dialog.Close();
                return DialogResult.Cancel;
            };

            GenerateCorpusStepTests.Pump(step.ViewFindingsAsync(QaFindingsMode.BlindReview));

            var row = Assert.Single(rows);
            Assert.Equal(docId, row.DocId);
            Assert.Equal(thread.Id, row.ThreadId);
            Assert.Equal(QaUiText.LabelText(true), row.Col1);
            Assert.Equal(QaUiText.LabelText(false), row.Col2);
            Assert.Equal("Only arranges a site visit; no defect or payment is discussed.", row.Text);
            Assert.Equal("Regenerate Thread", confirmTitle);
            Assert.Empty(opened);

            var replay = GenerationJournal.Replay(fixture.Folder, fixture.Plan);
            Assert.Contains(thread.Id, replay.InvalidatedThreadIds);
            Assert.Equal(Theme.Warning, step.MailboxesDotColor);
            Assert.Contains(" of ", step.MailboxesLine3.Text);
            Assert.NotEqual(mailboxesBefore, step.MailboxesLine3Tooltip);
            var relevant = fixture.Plan.Threads.Count(t => t.Relevance == ThreadRelevance.Relevant);
            Assert.Equal(relevant - 1, fixture.Draft.Corpus!.Generation!.ThreadsWritten);
        });
    }

    [Fact]
    public void OpenEmail_OpensTheSenderCopyUnderTheCaseFolder()
    {
        using var fixture = new GenerateCorpusRenderTests.StepFixture();
        SeedGenerated(fixture);
        var message = fixture.Plan.Threads[0].Messages[0];
        var relative = MailboxWriter.CopyPathsFor(message, PlanNumbering.CustodianFolders(fixture.Draft.Guide!))[0];
        var full = MailboxWriter.ResolveUnderCase(fixture.Folder, relative)!;
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, "From: a@b.com\r\n\r\nBody");

        GenerateCorpusStepTests.RunSta(() =>
        {
            var opened = new List<string>();
            using var step = CreateStep(fixture, opened);

            step.OpenEmail(message.DocId);
            Assert.Equal(new[] { full }, opened);

            opened.Clear();
            step.OpenEmail("RD-999999");
            step.OpenEmail("..\\..\\Windows");
            Assert.Empty(opened);
        });
    }

    // ----- leaving during a review -----

    [Fact]
    public void LeavingDuringAReview_AsksCQ3_NoStays_YesStops()
    {
        using var fixture = new GenerateCorpusRenderTests.StepFixture();
        SeedGenerated(fixture);
        var reviewer = new GatedReviewer();
        var model = Haiku();

        GenerateCorpusStepTests.RunSta(() =>
        {
            using var step = CreateStep(fixture);
            step.ReviewerGatewayFactory = () => (new LlmGateway(reviewer, model, model), LlmProviderKind.Anthropic, false, model);
            step.ShowDialogOverride = dialog =>
            {
                dialog.SampleInput.Value = 4;
                return DialogResult.OK;
            };

            // Not running: leaving never asks
            var asked = new List<string>();
            step.Confirm = (title, text) =>
            {
                asked.Add(title);
                return false;
            };
            Assert.True(step.ConfirmLeave());
            Assert.Empty(asked);

            var run = step.RunBlindReviewAsync();
            PumpUntil(() => step.IsReviewRunning, "review running");

            Assert.False(step.ConfirmLeave());
            Assert.Equal(new[] { QaUiText.CQ3LeaveDuringReview().Title }, asked);
            Assert.True(step.IsReviewRunning);

            step.Confirm = (title, text) =>
            {
                asked.Add(title);
                return true;
            };
            Assert.True(step.ConfirmLeave());
            GenerateCorpusStepTests.Pump(run);
            Assert.False(step.IsReviewRunning);
            Assert.Equal(2, asked.Count);
        });
    }
}
