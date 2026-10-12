using System.Text;
using ReelDiscovery.Helpers;
using ReelDiscovery.Models;
using ReelDiscovery.Models.Case;
using ReelDiscovery.Models.Corpus;
using ReelDiscovery.Models.Evidence;
using ReelDiscovery.Services.Ai;
using ReelDiscovery.Services.Case;
using ReelDiscovery.Services.Corpus;
using ReelDiscovery.Services.Evidence;
using ReelDiscovery.Tests.Corpus;
using ReelDiscovery.Tests.Fakes;
using ReelDiscovery.Tests.Render;
using ReelDiscovery.UserControls;

namespace ReelDiscovery.Tests.Evidence;

/// <summary>
/// Generate Corpus step logic (05-15): the run wired to the step with a fake provider, live snapshot draining, UI-thread
/// checkpoints, cancel, dispose, Start Over, reopen materialization, leave-step retries and opening an EML. Not a render
/// test: the step is never shown. Everything runs on one STA thread with a WinForms context and a message pump.
/// </summary>
public class GenerateCorpusStepTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    private sealed class Harness : IDisposable
    {
        public Harness(bool approved = true, bool mixed = false)
        {
            Fixture = new GenerateCorpusRenderTests.StepFixture(approved, mixed);
            (Gateway, Fake) = CorpusTestData.CreateGateway();
            // Thread renders, attachments and filler (06-19: the run writes all three)
            Fake.Respond = AttachmentResponder.Create(Fixture.Plan, Fixture.Draft.Guide!);
        }

        public GenerateCorpusRenderTests.StepFixture Fixture { get; }
        public LlmGateway Gateway { get; }
        public FakeLlmProvider Fake { get; }
        public CaseDraft Draft => Fixture.Draft;
        public CorpusPlan Plan => Fixture.Plan;
        public string Folder => Fixture.Folder;

        public List<PlannedThread> Relevant =>
            Plan.Threads.Where(t => t.Relevance == ThreadRelevance.Relevant && t.Messages.Count > 0).ToList();

        public StepGenerateCorpus CreateStep()
        {
            var step = new StepGenerateCorpus();
            step.BindState(Fixture.State);
            step.GatewayFactory = () => Gateway;
            step.Confirm = (title, text, icon) => true;
            // The fixture has a tested OpenAI key; a test run never reaches OpenAI (tests may replace this hook)
            step.RunInputCreated = input =>
            {
                if (input.Media != null)
                    input.Media = new FakeMediaGenerator();
            };
            return step;
        }

        public void Dispose() => Fixture.Dispose();
    }

    // ----- Task 1: run lifecycle -----

    [Fact]
    public void GenerateEmails_RunsToCompletion_PersistsOnUiThreadAndSaves()
    {
        using var h = new Harness();
        // Slow enough that the 250 ms timer paints mid-run
        h.Fake.OnSend = _ => Thread.Sleep(120);

        RunSta(() =>
        {
            using var step = h.CreateStep();
            var changes = 0;
            step.StateChanged += (s, e) => changes++;
            Pump(step.OnEnterStepAsync());
            Assert.Equal("Ready to write the corpus", step.StateHeading);
            changes = 0;

            var uiThread = Environment.CurrentManagedThreadId;
            var applyThreads = new List<int>();
            step.CheckpointApplied = () => applyThreads.Add(Environment.CurrentManagedThreadId);

            var run = step.ClickStateActionAsync();
            Assert.True(step.IsGenerating);
            Assert.False(step.CanMoveNext);
            Assert.False(step.CanMoveBack);
            Assert.True(step.CancelButton.Visible);
            Assert.False(step.StartOverButton.Visible);
            Assert.False(step.RetryFailedButton.Visible);
            Assert.False(step.BannerShown);
            Assert.False(step.StateCardShown);
            Assert.True(changes >= 1);

            var paintedMidRun = new HashSet<string>();
            Pump(run, () =>
            {
                if (step.IsGenerating)
                    paintedMidRun.Add(step.Tiles[0].ValueText);
            });

            Assert.False(step.IsGenerating);
            Assert.True(step.CanMoveNext);
            Assert.True(changes >= 2);
            // The timer painted at least one non-zero thread count while running (RUN-07)
            Assert.Contains(paintedMidRun, v => v.Length > 0 && !v.StartsWith("0"));

            // Every checkpoint was applied on the UI thread (T-05-48)
            Assert.NotEmpty(applyThreads);
            Assert.All(applyThreads, id => Assert.Equal(uiThread, id));

            var generation = h.Draft.Corpus!.Generation;
            Assert.NotNull(generation);
            Assert.Equal(CaseStage.Generation, h.Draft.Stage);
            Assert.NotEmpty(generation!.Ledger.Entries);
            var state = GenerationRunState.From(h.Plan, GenerationJournal.Replay(h.Folder, h.Plan));
            // ThreadsWritten.. stay relevant counts; since 06-19 the same run also writes the non-relevant mail
            Assert.Equal(state.RelevantWritten + state.RelevantWarnings, generation.ThreadsWritten + generation.ThreadsWithWarnings);
            Assert.Equal(state.NonRelevantTotal, generation.NonRelevantWritten + generation.NonRelevantWithWarnings);
            Assert.Equal(h.Relevant.Count, generation.ThreadsWritten + generation.ThreadsWithWarnings);
            Assert.Equal(GenerationOutcome.Finished, generation.LastOutcome);
            Assert.Equal(string.Empty, generation.KeyError);

            var kinds = h.Draft.Corpus.WrittenFiles.Where(f => f.PlanId == h.Plan.PlanId).Select(f => f.Kind).ToList();
            Assert.Contains(CorpusFileKind.AnswerKeyCsv, kinds);
            Assert.Contains(CorpusFileKind.AnswerKeyXlsx, kinds);
            Assert.Contains(CorpusFileKind.StorySoFar, kinds);

            WaitForSave(h);
            var json = File.ReadAllText(Path.Combine(h.Folder, CaseStore.CaseFileName));
            Assert.Contains("Generation", json);

            // The whole corpus as the step shows it after the run (06-19: relevant and non-relevant threads)
            var counts = state.ToCounts(h.Plan, WavePlanner.Plan(h.Plan).Count, generation.Ledger.Entries.Count, 0m, running: false, finished: true);
            Assert.Equal(GenerationUiText.OutcomeText(GenerationOutcome.Finished, counts, string.Empty).Text, step.StatusLabel.Text);
            Assert.Equal(Theme.Success, step.StatusLabel.ForeColor);
            Assert.False(step.CancelButton.Visible);
            Assert.True(step.StartOverButton.Visible);
            Assert.False(step.BannerShown);
        });
    }

    [Fact]
    public void RunEnd_WritesQaReportsAfterNavigationIsBack()
    {
        using var h = new Harness();

        RunSta(() =>
        {
            using var step = h.CreateStep();
            Pump(step.OnEnterStepAsync());
            Assert.Null(step.QaRun);

            Pump(step.ClickStateActionAsync());

            // Navigation is back before the QA pass is awaited (D-15: QA never blocks the user)
            Assert.False(step.IsGenerating);
            Assert.True(step.CanMoveNext);
            Assert.NotNull(step.QaRun);

            var changes = 0;
            step.StateChanged += (s, e) => changes++;
            Pump(step.QaRun!);

            var store = new ReelDiscovery.Services.Qa.QaReportStore(h.Folder);
            var scans = store.LoadScans();
            Assert.NotNull(scans);
            Assert.Equal(h.Plan.PlanId, scans!.PlanId);
            Assert.True(scans.ScannedEmails > 0);
            Assert.NotNull(store.LoadLeakage());
            Assert.True(changes >= 1);
        });
    }

    [Fact]
    public void Cancel_KeepsFinishedThreads_BannerOffersContinue_ContinueFinishes()
    {
        using var h = new Harness();
        var cancelNow = 0;
        h.Fake.OnSend = n =>
        {
            Thread.Sleep(60);
            if (n == 3)
                Interlocked.Exchange(ref cancelNow, 1);
        };

        RunSta(() =>
        {
            using var step = h.CreateStep();
            Pump(step.OnEnterStepAsync());

            var run = step.ClickStateActionAsync();
            var stoppingSeen = false;
            Pump(run, () =>
            {
                if (Interlocked.Exchange(ref cancelNow, 0) == 1)
                {
                    step.RequestCancel();
                    Assert.Equal(GenerationUiText.StoppingText, step.StatusLabel.Text);
                    Assert.False(step.CancelButton.Enabled);
                    stoppingSeen = true;
                }
            });

            Assert.True(stoppingSeen);
            Assert.False(step.IsGenerating);
            Assert.Equal(Theme.Warning, step.StatusLabel.ForeColor);
            Assert.StartsWith("Cancelled.", step.StatusLabel.Text);
            Assert.True(step.BannerShown);
            Assert.Equal(GenerationUiText.BannerHeading(runEnded: false), step.BannerHeadingText);
            Assert.Equal(GenerationOutcome.Cancelled, h.Draft.Corpus!.Generation!.LastOutcome);

            var replay = GenerationJournal.Replay(h.Folder, h.Plan);
            Assert.Equal(GenerationOutcome.Cancelled, replay.LastRunEnded?.Outcome);
            var saved = replay.LastByThread.Count;
            Assert.InRange(saved, 1, h.Relevant.Count - 1);

            // Continue Generating writes the rest
            h.Fake.OnSend = null;
            Pump(step.ClickContinueAsync());
            Assert.False(step.BannerShown);
            Assert.Equal(GenerationOutcome.Finished, h.Draft.Corpus.Generation!.LastOutcome);
            // Relevant and non-relevant threads (06-19: one run writes both)
            Assert.Equal(GenerationRunState.From(h.Plan, GenerationJournal.Replay(h.Folder, h.Plan)).TotalThreads,
                GenerationJournal.Replay(h.Folder, h.Plan).LastByThread.Count);
            Assert.Equal(Theme.Success, step.StatusLabel.ForeColor);
        });
    }

    [Fact]
    public void CancelDeclined_KeepsRunning()
    {
        using var h = new Harness();
        var asked = 0;
        h.Fake.OnSend = n =>
        {
            Thread.Sleep(30);
            if (n == 2)
                Interlocked.Exchange(ref asked, 1);
        };

        RunSta(() =>
        {
            using var step = h.CreateStep();
            Pump(step.OnEnterStepAsync());
            var icons = new List<MessageBoxIcon>();
            step.Confirm = (title, text, icon) =>
            {
                icons.Add(icon);
                return false;
            };

            var run = step.ClickStateActionAsync();
            Pump(run, () =>
            {
                if (Interlocked.Exchange(ref asked, 0) == 1)
                    step.RequestCancel();
            });

            Assert.Equal(new[] { MessageBoxIcon.Question }, icons);
            Assert.Equal(GenerationOutcome.Finished, h.Draft.Corpus!.Generation!.LastOutcome);
        });
    }

    [Fact]
    public void LockedAnswerKeyXlsx_ShowsLockedText_AndKeepsKeyError()
    {
        using var h = new Harness();
        var xlsx = Path.Combine(h.Folder, AnswerKeyXlsxWriter.FileName);

        RunSta(() =>
        {
            using var step = h.CreateStep();
            Pump(step.OnEnterStepAsync());
            using (new FileStream(xlsx, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
                Pump(step.ClickStateActionAsync());

            Assert.Equal(GenerationUiText.AnswerKeyLockedText(AnswerKeyXlsxWriter.FileName), step.StatusLabel.Text);
            Assert.Equal(Theme.Warning, step.StatusLabel.ForeColor);
            Assert.NotEqual(string.Empty, h.Draft.Corpus!.Generation!.KeyError);
            Assert.Equal(GenerationOutcome.Finished, h.Draft.Corpus.Generation.LastOutcome);
        });
    }

    [Fact]
    public void DisposeDuringRun_CancelsAndUnwindsWithoutError()
    {
        using var h = new Harness();
        var disposeNow = 0;
        h.Fake.OnSend = n =>
        {
            Thread.Sleep(40);
            if (n == 2)
                Interlocked.Exchange(ref disposeNow, 1);
        };

        RunSta(() =>
        {
            var step = h.CreateStep();
            Pump(step.OnEnterStepAsync());
            var run = step.ClickStateActionAsync();
            Pump(run, () =>
            {
                if (Interlocked.Exchange(ref disposeNow, 0) == 1)
                    step.Dispose();
            });

            Assert.True(step.IsDisposed);
            Assert.True(run.IsCompletedSuccessfully);
            // Finished threads stayed journaled; nothing beyond them was billed for long
            Assert.True(GenerationJournal.Replay(h.Folder, h.Plan).LastByThread.Count < h.Relevant.Count);
        });
    }

    [Fact]
    public void PersistFromWorker_WithoutWindowHandle_NeverAppliesOffTheUiThread()
    {
        using var h = new Harness();

        RunSta(() =>
        {
            using var step = h.CreateStep();
            Pump(step.OnEnterStepAsync());
            var applyThreads = new List<int>();
            step.CheckpointApplied = () =>
            {
                lock (applyThreads)
                    applyThreads.Add(Environment.CurrentManagedThreadId);
            };
            var uiThread = Environment.CurrentManagedThreadId;

            // Never shown, so no handle exists and InvokeRequired is false on every thread (as while the step is disposing)
            Assert.False(step.IsHandleCreated);
            Pump(Task.Run(() => step.PersistOnUiThreadAsync(new GenerationCheckpoint { State = new GenerationRunState() })));

            lock (applyThreads)
                Assert.All(applyThreads, id => Assert.Equal(uiThread, id));

            // After dispose released the pending checkpoints, a late checkpoint completes at once
            step.Dispose();
            var late = Task.Run(() => step.PersistOnUiThreadAsync(new GenerationCheckpoint()));
            Pump(late);
        });
    }

    [Fact]
    public void CancelAndWaitForRun_StopsTheRunWithinTimeout()
    {
        using var h = new Harness();
        var started = 0;
        h.Fake.OnSend = n =>
        {
            Thread.Sleep(40);
            Interlocked.Exchange(ref started, 1);
        };

        RunSta(() =>
        {
            using var step = h.CreateStep();
            Pump(step.OnEnterStepAsync());
            var run = step.ClickStateActionAsync();
            Pump(Task.Run(() => SpinWait.SpinUntil(() => Volatile.Read(ref started) == 1, Timeout)));

            Assert.True(step.CancelAndWaitForRun(Timeout));
            Assert.True(run.IsCompleted);
            Assert.False(step.IsGenerating);
            Assert.Equal(GenerationOutcome.Cancelled, GenerationJournal.Replay(h.Folder, h.Plan).LastRunEnded?.Outcome);
        });
    }

    // ----- Task 2: Start Over, reopen, leave-step retries, opening an EML -----

    [Fact]
    public void StartOver_Yes_DeletesRunFiles_ResetsKeyToPlanned_ShowsReady()
    {
        using var h = new Harness();

        RunSta(() =>
        {
            using var step = h.CreateStep();
            Pump(step.OnEnterStepAsync());
            Pump(step.ClickStateActionAsync());
            Assert.True(step.StartOverButton.Visible);

            var mailboxes = Path.Combine(h.Folder, CorpusPlan.MailboxesRoot);
            var emls = Directory.GetFiles(mailboxes, "*.eml", SearchOption.AllDirectories).Length;
            Assert.True(emls > 0);
            var story = Path.Combine(h.Folder, StorySoFarDocxBuilder.FileNameFor(Path.GetFileName(h.Folder)));
            Assert.True(File.Exists(story));

            var icons = new List<MessageBoxIcon>();
            step.Confirm = (title, text, icon) =>
            {
                icons.Add(icon);
                Assert.Equal(GenerationUiText.CG2StartOver(emls), (title, text));
                return true;
            };
            Pump(step.ClickStartOverAsync());

            Assert.Equal(new[] { MessageBoxIcon.Warning }, icons);
            Assert.False(File.Exists(GenerationJournal.PathFor(h.Folder)));
            Assert.Empty(Directory.GetFiles(mailboxes, "*.eml", SearchOption.AllDirectories));
            Assert.False(File.Exists(story));
            Assert.Null(h.Draft.Corpus!.Generation);
            Assert.DoesNotContain(h.Draft.Corpus.WrittenFiles, f => f.Kind == CorpusFileKind.StorySoFar);

            var key = KeyStatuses(h.Folder);
            foreach (var message in h.Relevant.SelectMany(t => t.Messages))
                Assert.Equal("Planned", key[message.DocId]);

            Assert.Equal(GenerationUiText.StartedOverText(emls), step.StatusLabel.Text);
            Assert.Equal(Theme.Success, step.StatusLabel.ForeColor);
            Assert.True(step.StateCardShown);
            Assert.Equal("Ready to write the corpus", step.StateHeading);
            Assert.True(step.CanMoveNext);
        });
    }

    [Fact]
    public void StartOver_No_ChangesNothing()
    {
        using var h = new Harness();

        RunSta(() =>
        {
            using var step = h.CreateStep();
            Pump(step.OnEnterStepAsync());
            Pump(step.ClickStateActionAsync());
            var status = step.StatusLabel.Text;
            var mailboxes = Path.Combine(h.Folder, CorpusPlan.MailboxesRoot);
            var emls = Directory.GetFiles(mailboxes, "*.eml", SearchOption.AllDirectories).Length;

            step.Confirm = (title, text, icon) => false;
            Pump(step.ClickStartOverAsync());

            Assert.True(File.Exists(GenerationJournal.PathFor(h.Folder)));
            Assert.Equal(emls, Directory.GetFiles(mailboxes, "*.eml", SearchOption.AllDirectories).Length);
            Assert.NotNull(h.Draft.Corpus!.Generation);
            Assert.Equal(status, step.StatusLabel.Text);
            Assert.False(step.StateCardShown);
        });
    }

    // Seeds the journal synchronously: the test drives WinForms controls on the test thread
    #pragma warning disable xUnit1031
    [Fact]
    public void Reopen_JournalNewerThanCaseJson_MaterializesLedgerCountsAndKey_WithoutProviderCalls()
    {
        using var h = new Harness();
        var threads = h.Relevant.OrderBy(t => t.Id, StringComparer.Ordinal).Take(2).ToList();
        var journal = new GenerationJournal(h.Folder);
        journal.AppendAsync(new JournalRecord { Type = JournalRecordType.RunStarted, PlanId = h.Plan.PlanId, Utc = DateTime.UtcNow })
            .GetAwaiter().GetResult();
        foreach (var thread in threads)
            journal.AppendAsync(GenerationRunStateTests.Done(h.Plan, thread, ThreadWriteStatus.Written, 0.10m)).GetAwaiter().GetResult();
        Assert.Null(h.Draft.Corpus!.Generation);

        RunSta(() =>
        {
            using var step = h.CreateStep();
            step.GatewayFactory = () => throw new InvalidOperationException("entering the step must not build a provider");
            Pump(step.OnEnterStepAsync());

            var generation = h.Draft.Corpus.Generation;
            Assert.NotNull(generation);
            Assert.Equal(2, generation!.ThreadsWritten);
            Assert.Equal(h.Plan.PlanId, generation.RunPlanId);
            Assert.NotEmpty(generation.Ledger.Entries);
            Assert.NotNull(generation.ProjectedUtc);
            Assert.False(step.IsGenerating);
            Assert.True(step.BannerShown);

            var key = KeyStatuses(h.Folder);
            foreach (var thread in h.Relevant)
                foreach (var message in thread.Messages)
                    Assert.Equal(threads.Contains(thread) ? "Generated" : "Planned", key[message.DocId]);

            var saved = System.Text.Json.JsonSerializer.Deserialize<CaseDraft>(File.ReadAllText(Path.Combine(h.Folder, CaseStore.CaseFileName)), CaseDraft.JsonOptions);
            Assert.Equal(2, saved?.Corpus?.Generation?.ThreadsWritten);
        });

        Assert.Empty(h.Fake.Requests);
    }
    #pragma warning restore xUnit1031

    [Fact]
    public void OpenEml_OnlyOpensExistingFilesUnderTheCaseFolder()
    {
        using var h = new Harness();

        RunSta(() =>
        {
            using var step = h.CreateStep();
            Pump(step.OnEnterStepAsync());
            var opened = new List<string>();
            step.OpenFile = opened.Add;
            step.SetStatus("before", Theme.TextMuted);

            // Outside the case folder or not an EML: nothing happens
            step.OpenEml(new GenerationEvent { Kind = GenerationEventKind.ThreadWritten, FilePath = "../../outside/DOC-000001.eml" });
            step.OpenEml(new GenerationEvent { Kind = GenerationEventKind.ThreadWritten, FilePath = "C:/Windows/notepad.exe" });
            Assert.Empty(opened);
            Assert.Equal("before", step.StatusLabel.Text);

            // Other rows do nothing
            var relative = MailboxWriter.CopyPathsFor(h.Relevant[0].Messages[0], PlanNumbering.CustodianFolders(h.Draft.Guide!))[0];
            step.OpenEml(new GenerationEvent { Kind = GenerationEventKind.ThreadFailed, FilePath = relative });
            Assert.Empty(opened);
            Assert.Equal("before", step.StatusLabel.Text);

            // Missing file
            step.OpenEml(new GenerationEvent { Kind = GenerationEventKind.ThreadWritten, FilePath = relative });
            Assert.Equal(GenerationUiText.MissingEmlText(Path.GetFileNameWithoutExtension(relative)), step.StatusLabel.Text);
            Assert.Equal(Theme.Danger, step.StatusLabel.ForeColor);
            Assert.Empty(opened);

            // Existing file under the case folder
            var full = Path.Combine(h.Folder, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            File.WriteAllText(full, "x");
            step.OpenEml(new GenerationEvent { Kind = GenerationEventKind.KeptWithWarnings, FilePath = relative });
            Assert.Equal(new[] { Path.GetFullPath(full) }, opened);
        });
    }

    [Fact]
    public void LeaveStep_RetriesLockedKey_ClearsKeyErrorOnceReleased()
    {
        using var h = new Harness();
        var xlsx = Path.Combine(h.Folder, AnswerKeyXlsxWriter.FileName);

        RunSta(() =>
        {
            using var step = h.CreateStep();
            Pump(step.OnEnterStepAsync());
            using (var lockStream = new FileStream(xlsx, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
            {
                Pump(step.ClickStateActionAsync());
                Assert.NotEqual(string.Empty, h.Draft.Corpus!.Generation!.KeyError);

                // Still locked: the retry fails quietly and keeps the error
                Pump(step.OnLeaveStepAsync());
                Assert.NotEqual(string.Empty, h.Draft.Corpus.Generation!.KeyError);
                Assert.Equal(0, lockStream.Length);
            }

            Pump(step.OnLeaveStepAsync());
            Assert.Equal(string.Empty, h.Draft.Corpus.Generation!.KeyError);
            Assert.True(new FileInfo(xlsx).Length > 0);
            Assert.Contains(h.Draft.Corpus.WrittenFiles, f => f.Kind == CorpusFileKind.AnswerKeyXlsx && f.PlanId == h.Plan.PlanId);
            var key = KeyStatuses(h.Folder);
            foreach (var message in h.Relevant.SelectMany(t => t.Messages))
                Assert.Equal("Generated", key[message.DocId]);
        });
    }

    // ----- 06-23: run input, entry banners, Ready card, progress units, confirmations -----

    [Fact]
    public void RunInput_WithTestedKey_CarriesMediaAndAClonedPrices()
    {
        using var h = new Harness();
        Assert.True(h.Fixture.State.IsMediaAvailable);
        var start = h.Fixture.State.MediaPrices.ImagePerUnit;

        RunSta(() =>
        {
            using var step = h.CreateStep();
            var inputs = new List<GenerationRunInput>();
            var mediaTypes = new List<Type?>();
            step.RunInputCreated = input =>
            {
                inputs.Add(input);
                mediaTypes.Add(input.Media?.GetType());
                // Never a real OpenAI call from a test
                input.Media = new FakeMediaGenerator();
            };
            Pump(step.OnEnterStepAsync());
            Pump(step.ClickStateActionAsync());

            var captured = Assert.Single(inputs);
            Assert.Equal(typeof(OpenAIMediaGenerator), mediaTypes[0]);
            Assert.NotSame(h.Fixture.State.MediaPrices, captured.MediaPrices);

            // Editing prices after the start never changes that run's prices (review LOW 06-24)
            h.Fixture.State.MediaPrices.ImagePerUnit = 0.5m;
            Assert.Equal(start, captured.MediaPrices.ImagePerUnit);
        });
    }

    [Fact]
    public void RunInput_WithoutTestedKey_HasNoMedia()
    {
        using var h = new Harness();
        h.Fixture.State.OpenAIKeyTested = false;

        RunSta(() =>
        {
            using var step = h.CreateStep();
            GenerationRunInput? captured = null;
            step.RunInputCreated = input => captured = input;
            Pump(step.OnEnterStepAsync());
            Pump(step.ClickStateActionAsync());

            Assert.NotNull(captured);
            Assert.Null(captured!.Media);
        });
    }

    [Fact]
    public void BannerAndHeaderButtons_StartOneRunPerClick_WithoutConfirmation()
    {
        using var h = new Harness();

        RunSta(() =>
        {
            using var step = h.CreateStep();
            var runs = 0;
            var confirms = 0;
            step.RunInputCreated = input =>
            {
                runs++;
                input.Media = null;
            };
            Pump(step.OnEnterStepAsync());
            Pump(step.ClickStateActionAsync());
            Assert.Equal(1, runs);

            step.Confirm = (title, text, icon) =>
            {
                confirms++;
                return true;
            };
            Pump(step.ClickBannerButtonAsync());
            Assert.Equal(2, runs);
            Pump(step.ClickRetryFailedAsync());
            Assert.Equal(3, runs);
            Pump(step.ClickContinueAsync());
            Assert.Equal(4, runs);
            Assert.Equal(0, confirms);
        });
    }

    [Fact]
    public void Entry_ThreadsDoneAttachmentsMissing_LastRunFinished_PartOfCorpusHeading()
    {
        using var h = new Harness(mixed: true);
        var ids = GenerationIds(h.Plan);
        WriteJournal(h, ids.Select(id => GenerationRunStateTests.Done(h.Plan, ThreadOf(h, id), ThreadWriteStatus.Written, 0.01m))
            .Append(RunEnded(h, GenerationOutcome.Finished)));

        RunSta(() =>
        {
            using var step = h.CreateStep();
            Pump(step.OnEnterStepAsync());

            var state = GenerationRunState.From(h.Plan, GenerationJournal.Replay(h.Folder, h.Plan));
            Assert.True(state.AttachmentsTotal > 0);
            Assert.Equal(GenerateCorpusBanner.PartialRun, step.BannerKind);
            Assert.Equal("Part of the corpus is not written yet.", step.BannerHeadingText);
            Assert.Equal(GenerationUiText.BannerBody(ids.Count, ids.Count, 0, state.AttachmentsTotal, 0), step.BannerBodyText);
            Assert.Equal(GenerationUiText.BannerButton, step.BannerButton.Text);

            // Reopen rebuilds the Stages rows from the journal: waves, Non-relevant mail, Attachments for written threads
            var waves = WavePlanner.Plan(h.Plan).Count;
            Assert.Equal(waves + 2, step.WavesGrid.Rows.Count);
            Assert.Equal("Non-relevant mail", step.WavesGrid.Rows[waves].Cells["Dates"].Value);
            Assert.Equal("Attachments for written threads", step.WavesGrid.Rows[waves + 1].Cells["Dates"].Value);
        });
    }

    [Fact]
    public void Entry_NoRunEnded_LastRunDidNotFinish()
    {
        using var h = new Harness(mixed: true);
        var ids = GenerationIds(h.Plan);
        WriteJournal(h, ids.Take(2).Select(id => GenerationRunStateTests.Done(h.Plan, ThreadOf(h, id), ThreadWriteStatus.Written, 0.01m)));

        RunSta(() =>
        {
            using var step = h.CreateStep();
            Pump(step.OnEnterStepAsync());
            Assert.Equal(GenerateCorpusBanner.PartialRun, step.BannerKind);
            Assert.Equal("The last run did not finish.", step.BannerHeadingText);
        });
    }

    [Fact]
    public void Entry_InterruptedContinueAfterFinishedRun_LastRunDidNotFinish()
    {
        using var h = new Harness(mixed: true);
        var ids = GenerationIds(h.Plan);
        var records = ids.Select(id => GenerationRunStateTests.Done(h.Plan, ThreadOf(h, id), ThreadWriteStatus.Written, 0.01m)).ToList();
        records.Add(RunEnded(h, GenerationOutcome.Finished));
        // A later Continue started and never ended (crash): an older RunEnded never counts (review MEDIUM 06-23)
        records.Add(new JournalRecord { Type = JournalRecordType.RunStarted, PlanId = h.Plan.PlanId, Utc = DateTime.UtcNow });
        WriteJournal(h, records);

        RunSta(() =>
        {
            using var step = h.CreateStep();
            Pump(step.OnEnterStepAsync());
            Assert.Equal(GenerateCorpusBanner.PartialRun, step.BannerKind);
            Assert.Equal("The last run did not finish.", step.BannerHeadingText);
        });
    }

    [Fact]
    public void Entry_OnlySkippedMediaLeft_MediaAvailable_CreateSkippedMedia()
    {
        using var h = new Harness(mixed: true);
        WriteCompleteJournal(h, failOne: false);

        RunSta(() =>
        {
            using var step = h.CreateStep();
            Pump(step.OnEnterStepAsync());

            var state = GenerationRunState.From(h.Plan, GenerationJournal.Replay(h.Folder, h.Plan));
            Assert.True(state.AttachmentsSkipped >= 3);
            var expected = GenerationUiText.MediaBanner(state.AttachmentsSkipped, 0, true, MostReason);
            Assert.Equal(GenerateCorpusBanner.Media, step.BannerKind);
            Assert.Equal("Create Skipped Media", step.BannerButton.Text);
            Assert.Equal(expected.Heading, step.BannerHeadingText);
            // The reason clause is the most frequent AttachmentSkipped reason in the journal
            Assert.Equal(expected.Body, step.BannerBodyText);
            Assert.Contains(MostReason, step.BannerBodyText);
            Assert.DoesNotContain(OtherReason, step.BannerBodyText);
            Assert.False(step.RetryFailedButton.Visible);
        });
    }

    [Fact]
    public void Entry_SkippedMediaAndFailedItem_RetryFailedAndSkippedItems()
    {
        using var h = new Harness(mixed: true);
        WriteCompleteJournal(h, failOne: true);

        RunSta(() =>
        {
            using var step = h.CreateStep();
            Pump(step.OnEnterStepAsync());
            Assert.Equal(GenerateCorpusBanner.Media, step.BannerKind);
            Assert.Equal("Retry Failed and Skipped Items", step.BannerButton.Text);
            Assert.False(step.RetryFailedButton.Visible);
        });
    }

    [Fact]
    public void Entry_SkippedMedia_MediaUnavailable_BannerWithoutButton()
    {
        using var h = new Harness(mixed: true);
        h.Fixture.State.OpenAIKeyTested = false;
        WriteCompleteJournal(h, failOne: false);

        RunSta(() =>
        {
            using var step = h.CreateStep();
            Pump(step.OnEnterStepAsync());

            var state = GenerationRunState.From(h.Plan, GenerationJournal.Replay(h.Folder, h.Plan));
            var expected = GenerationUiText.MediaBanner(state.AttachmentsSkipped, 0, false, MostReason);
            Assert.Equal(string.Empty, expected.ButtonText);
            Assert.Equal(GenerateCorpusBanner.Media, step.BannerKind);
            Assert.Equal(expected.Body, step.BannerBodyText);
            Assert.False(step.BannerButton.Visible);
        });
    }

    [Fact]
    public void ReadyCard_MixedCase_TieredEstimateAndMediaNotice()
    {
        using var h = new Harness(mixed: true);
        h.Fixture.State.OpenAIKeyTested = false;
        var state = h.Fixture.State;
        Assert.NotNull(state.StoryModelConfig);
        Assert.NotNull(state.VolumeModelConfig);

        RunSta(() =>
        {
            using var step = h.CreateStep();
            Pump(step.OnEnterStepAsync());

            Assert.Equal("Ready to write the corpus", step.StateHeading);
            // 07 S1 (D-17): two totals on the line, the per-model breakdown in its tooltip
            Assert.StartsWith("Estimated cost ≈$", step.EstimateLabel.Text);
            Assert.Contains("with batch pricing", step.EstimateLabel.Text);
            Assert.Contains(state.StoryModelConfig!.DisplayName, step.EstimateTooltipText);
            Assert.Contains(state.VolumeModelConfig!.DisplayName, step.EstimateTooltipText);
            Assert.Contains("images and voicemail", step.EstimateTooltipText);
            Assert.NotEqual(string.Empty, step.MediaNoticeLabel.Text);
            // The body names the non-relevant threads and the attachments (run order)
            Assert.Contains("non-relevant thread", step.StateBody);
            Assert.Contains("attachment", step.StateBody);

            // With a tested key there is no notice
            state.OpenAIKeyTested = true;
            Pump(step.OnEnterStepAsync());
            Assert.Equal(string.Empty, step.MediaNoticeLabel.Text);
        });
    }

    // ----- 07-11: batch choice, run wiring, Start Over cancel -----

    [Fact]
    public void BatchCheckbox_ReadyCardOnly_SavedToCase_HintFollows()
    {
        using var h = new Harness();

        RunSta(() =>
        {
            using var step = h.CreateStep();
            Pump(step.OnEnterStepAsync());

            Assert.True(step.UseBatchCheckBox.Visible);
            Assert.False(step.UseBatchCheckBox.Checked);
            Assert.Equal(GenerationUiText.BatchCheckboxText, step.UseBatchCheckBox.Text);
            Assert.False(step.UseBatchCheckBox.UseMnemonic);

            step.UseBatchCheckBox.Checked = true;
            Assert.True(h.Draft.Corpus!.Generation!.UseBatch);
            Assert.True(step.UseBatchCheckBox.Checked);
            Assert.True(step.BatchHintLabel.Visible);
            Assert.Equal(GenerationUiText.BatchHint(0, true, GenerationUiText.BatchRecommendThreshold).Text, step.BatchHintLabel.Text);
            Assert.Equal(Theme.Warning, step.BatchHintLabel.ForeColor);

            // Re-entering reads the saved choice
            Pump(step.OnEnterStepAsync());
            Assert.True(step.UseBatchCheckBox.Checked);

            step.UseBatchCheckBox.Checked = false;
            Assert.False(h.Draft.Corpus.Generation.UseBatch);
        });
    }

    [Fact]
    public void BatchCheckbox_HiddenOnNotApprovedCard()
    {
        using var h = new Harness(approved: false);

        RunSta(() =>
        {
            using var step = h.CreateStep();
            Pump(step.OnEnterStepAsync());
            Assert.Equal("Approve the plan first", step.StateHeading);
            Assert.False(step.UseBatchCheckBox.Visible);
            Assert.False(step.BatchHintLabel.Visible);
        });
    }

    [Fact]
    public void Run_UsesSavedBatchChoice()
    {
        using var h = new Harness();
        h.Draft.Corpus!.Generation = new GenerationState { UseBatch = true };

        RunSta(() =>
        {
            using var step = h.CreateStep();
            var choices = new List<bool>();
            step.RunGatewayFactory = useBatch =>
            {
                choices.Add(useBatch);
                return (h.Gateway, null);
            };
            Pump(step.OnEnterStepAsync());
            Pump(step.ClickStateActionAsync());

            Assert.Equal(new[] { true }, choices);
            Assert.True(h.Draft.Corpus.Generation!.UseBatch);
        });
    }

    [Fact]
    public void StartOver_CancelsPendingProviderBatches_KeepsBatchChoice()
    {
        using var h = new Harness();

        RunSta(() =>
        {
            using var step = h.CreateStep();
            Pump(step.OnEnterStepAsync());
            Pump(step.ClickStateActionAsync());
            h.Draft.Corpus!.Generation!.UseBatch = true;

            // One batch still at the provider from before the app closed
            new BatchLedger(h.Folder).AppendAsync(new BatchLedgerRecord
            {
                Type = BatchLedgerRecordType.Submitted,
                Utc = DateTime.UtcNow,
                Provider = LlmProviderKind.OpenAI,
                BatchId = "batch_pending",
                PlanId = h.Plan.PlanId,
                SubmittedUtc = DateTime.UtcNow,
                StageLabel = "wave 1",
                Total = 2
            }).GetAwaiter().GetResult();

            var transport = new FakeBatchTransport();
            step.BatchTransportFactory = _ => transport;
            string? confirmText = null;
            step.Confirm = (title, text, icon) =>
            {
                confirmText = text;
                return true;
            };
            Pump(step.ClickStartOverAsync());

            Assert.EndsWith(" Pending provider batches are cancelled.", confirmText);
            Assert.Equal(new[] { "batch_pending" }, transport.CancelledIds);
            Assert.True(h.Draft.Corpus.Generation!.UseBatch);
            Assert.Equal(0, step.PendingProviderBatches);
            Assert.Equal(Theme.Success, step.StatusLabel.ForeColor);
        });
    }

    [Fact]
    public void WRB05_StartOver_StopsTheQaPassFirst_SoItsReportIsNotWrittenAfterTheReset()
    {
        using var h = new Harness();

        RunSta(() =>
        {
            using var step = h.CreateStep();
            var stopped = false;
            // A QA pass that is still running when Start Over is clicked and saves its report as it stops
            step.QaPassOverride = async (plan, folder, ct) =>
            {
                try
                {
                    await Task.Delay(System.Threading.Timeout.Infinite, ct);
                }
                catch (OperationCanceledException)
                {
                    stopped = true;
                }

                await Task.Delay(50);
                new ReelDiscovery.Services.Qa.QaReportStore(folder).SaveScans(new ReelDiscovery.Models.Qa.ScanReport { PlanId = plan.PlanId });
            };
            Pump(step.OnEnterStepAsync());
            Pump(step.ClickStateActionAsync());
            Assert.NotNull(step.QaRun);
            Assert.False(step.QaRun!.IsCompleted);

            Pump(step.ClickStartOverAsync());

            Assert.True(stopped);
            Assert.True(step.QaRun.IsCompleted);
            Assert.False(File.Exists(new ReelDiscovery.Services.Qa.QaReportStore(h.Folder).ScansPath));
            Assert.False(File.Exists(GenerationJournal.PathFor(h.Folder)));
        });
    }

    [Fact]
    public void StartOver_CancelFailure_StillClears_WithWarning()
    {
        using var h = new Harness();

        RunSta(() =>
        {
            using var step = h.CreateStep();
            Pump(step.OnEnterStepAsync());
            Pump(step.ClickStateActionAsync());

            new BatchLedger(h.Folder).AppendAsync(new BatchLedgerRecord
            {
                Type = BatchLedgerRecordType.Submitted,
                Utc = DateTime.UtcNow,
                Provider = LlmProviderKind.OpenAI,
                BatchId = "batch_pending",
                PlanId = h.Plan.PlanId,
                SubmittedUtc = DateTime.UtcNow,
                Total = 1
            }).GetAwaiter().GetResult();

            step.BatchTransportFactory = _ => throw new InvalidOperationException("provider down");
            Pump(step.ClickStartOverAsync());

            Assert.False(File.Exists(GenerationJournal.PathFor(h.Folder)));

            // WR-A05: the batch that could not be cancelled stays tracked
            Assert.True(File.Exists(BatchLedger.PathFor(h.Folder)));
            Assert.EndsWith(GenerationUiText.BatchCancelFailedText, step.StatusLabel.Text);
            Assert.Equal(Theme.Warning, step.StatusLabel.ForeColor);
            Assert.Equal("Ready to write the corpus", step.StateHeading);
        });
    }

    [Fact]
    public void ProgressUnits_ThreadsAndAttachmentsAreEqualUnits()
    {
        var counts = new GenerationCounts
        {
            TotalThreads = 10,
            DoneThreads = 3,
            FailedThreads = 1,
            AttachmentsTotal = 5,
            AttachmentsDone = 1,
            AttachmentsFailed = 1,
            AttachmentsSkipped = 1
        };
        Assert.Equal((15, 7), StepGenerateCorpus.ProgressUnits(counts));

        // Never above the maximum
        counts.DoneThreads = 40;
        var (maximum, value) = StepGenerateCorpus.ProgressUnits(counts);
        Assert.Equal(15, maximum);
        Assert.Equal(15, value);

        // Empty plan: a non-zero maximum
        Assert.Equal((1, 0), StepGenerateCorpus.ProgressUnits(new GenerationCounts()));
    }

    [Fact]
    public void ProgressBar_RetriedFailedAttachment_IsNotCountedTwice()
    {
        RunSta(() =>
        {
            using var step = new StepGenerateCorpus();
            var snapshot = new GenerationSnapshot();
            snapshot.Reset(new GenerationCounts
            {
                TotalThreads = 4,
                DoneThreads = 2,
                AttachmentsTotal = 3,
                AttachmentsDone = 1,
                AttachmentsFailed = 1,
                Running = true
            }, new List<WaveProgress> { new() { Index = 1, Threads = 4, Attachments = 3 } },
                new Dictionary<string, AttachmentSeed>
                {
                    ["ATT-1"] = new() { RowIndex = 1, Status = AttachmentRunStatus.Written },
                    ["ATT-2"] = new() { RowIndex = 1, Status = AttachmentRunStatus.Failed },
                    ["ATT-3"] = new() { RowIndex = 1, Status = AttachmentRunStatus.Pending }
                });
            step.ApplyView(snapshot.Drain());
            Assert.Equal(7, step.ProgressBar.Maximum);
            Assert.Equal(4, step.ProgressBar.Value);

            // The replayed Failed attachment written in this run moves from Failed to Done
            snapshot.RecordAttachment("ATT-2", 1, AttachmentRunStatus.Written, 0.01m);
            step.ApplyView(snapshot.Drain());
            Assert.Equal(7, step.ProgressBar.Maximum);
            Assert.Equal(4, step.ProgressBar.Value);

            // A pending one skipped counts as finished
            snapshot.RecordAttachment("ATT-3", 1, AttachmentRunStatus.Skipped, 0m);
            step.ApplyView(snapshot.Drain());
            Assert.Equal(5, step.ProgressBar.Value);
            Assert.True(step.ProgressBar.Value <= step.ProgressBar.Maximum);
        });
    }

    [Fact]
    public void CancelConfirmation_CarriesTheAttachmentClause()
    {
        var counts = new GenerationCounts { DoneThreads = 4, AttachmentsDone = 3 };
        Assert.Equal(GenerationUiText.CG1CancelGeneration(4, 3), StepGenerateCorpus.CancelConfirmation(counts));
        Assert.Contains("3 attachments", StepGenerateCorpus.CancelConfirmation(counts).Text);
        Assert.Equal(GenerationUiText.CG1CancelGeneration(0, 0), StepGenerateCorpus.CancelConfirmation(null));
    }

    // ----- 09 W1: guide changed -----

    private const string GuideChangedHeading = "The case guide changed after this plan was built";

    private static CaseDraft? SavedDraft(Harness h) =>
        System.Text.Json.JsonSerializer.Deserialize<CaseDraft>(File.ReadAllText(Path.Combine(h.Folder, CaseStore.CaseFileName)), CaseDraft.JsonOptions);

    [Fact]
    public void Entry_GuideChanged_WithdrawsApprovalSavesAndShowsGuideChangedCard_WithoutProviderCalls()
    {
        using var h = new Harness();
        h.Draft.Guide!.Facts[0].Title += " (revised)";

        RunSta(() =>
        {
            using var step = h.CreateStep();
            step.GatewayFactory = () => throw new InvalidOperationException("entering the step must not build a provider");
            Pump(step.OnEnterStepAsync());

            Assert.True(step.StateCardShown);
            Assert.Equal(GuideChangedHeading, step.StateHeading);
            Assert.Equal("Review Plan", step.StateActionButton.Text);
            Assert.False(h.Draft.Corpus!.Approved);
            Assert.False(SavedDraft(h)?.Corpus?.Approved);
        });

        Assert.Empty(h.Fake.Requests);
        // No journal was read or started
        Assert.False(File.Exists(GenerationJournal.PathFor(h.Folder)));
    }

    [Fact]
    public void Entry_GuideChanged_KeptPlan_ShowsReadyCard()
    {
        using var h = new Harness();
        h.Draft.Guide!.Facts[0].Title += " (revised)";
        CorpusApproval.KeepStalePlan(h.Draft);

        RunSta(() =>
        {
            using var step = h.CreateStep();
            Pump(step.OnEnterStepAsync());

            Assert.Equal("Ready to write the corpus", step.StateHeading);
            Assert.True(h.Draft.Corpus!.Approved);
        });

        Assert.Empty(h.Fake.Requests);
    }

    [Fact]
    public void Entry_UnapprovedPlanAndStaleGuide_ShowsGuideChangedCard()
    {
        using var h = new Harness(approved: false);
        h.Draft.Guide!.Facts[0].Title += " (revised)";

        RunSta(() =>
        {
            using var step = h.CreateStep();
            Pump(step.OnEnterStepAsync());

            Assert.Equal(GuideChangedHeading, step.StateHeading);
            Assert.Equal("Review Plan", step.StateActionButton.Text);
        });

        Assert.Empty(h.Fake.Requests);
    }

    [Fact]
    public void GuideChangedCard_Click_NavigatesOnly_JournalSurvives()
    {
        using var h = new Harness();

        RunSta(() =>
        {
            using var step = h.CreateStep();
            Pump(step.OnEnterStepAsync());
            Pump(step.ClickStateActionAsync());
            Assert.False(step.IsGenerating);
            var requests = h.Fake.Requests.Count;

            h.Draft.Guide!.Facts[0].Title += " (revised)";
            Pump(step.OnEnterStepAsync());
            Assert.Equal(GuideChangedHeading, step.StateHeading);
            var files = GenerationJournal.ListAllFiles(h.Folder).Count;
            Assert.True(files > 0);
            Assert.True(File.Exists(GenerationJournal.PathFor(h.Folder)));

            var confirms = 0;
            step.Confirm = (title, text, icon) =>
            {
                confirms++;
                return true;
            };
            Pump(step.ClickStateActionAsync());

            Assert.Equal(0, confirms);
            Assert.Equal(files, GenerationJournal.ListAllFiles(h.Folder).Count);
            Assert.True(File.Exists(GenerationJournal.PathFor(h.Folder)));
            Assert.NotNull(h.Draft.Corpus!.Generation);
            Assert.False(h.Draft.Corpus.Approved);
            Assert.False(step.IsGenerating);
            Assert.Equal(requests, h.Fake.Requests.Count);
        });
    }

    [Fact]
    public void Run_GuideChangedAfterEntry_RefusedWithWarning_NoProviderCalls()
    {
        using var h = new Harness();

        RunSta(() =>
        {
            using var step = h.CreateStep();
            Pump(step.OnEnterStepAsync());
            Assert.Equal("Ready to write the corpus", step.StateHeading);

            h.Draft.Guide!.Facts[0].Title += " (revised)";
            Pump(step.ClickStateActionAsync());

            Assert.False(step.IsGenerating);
            Assert.True(step.StateCardShown);
            Assert.Equal(GuideChangedHeading, step.StateHeading);
            Assert.Equal(GenerationUiText.RunRefusedGuideChangedText, step.StatusLabel.Text);
            Assert.Equal(Theme.Warning, step.StatusLabel.ForeColor);
            Assert.False(h.Draft.Corpus!.Approved);
            Assert.False(SavedDraft(h)?.Corpus?.Approved);
        });

        Assert.Empty(h.Fake.Requests);
        Assert.False(File.Exists(GenerationJournal.PathFor(h.Folder)));
    }

    [Fact]
    public void Run_NotApprovedAfterEntry_RefusedWithWarning_NoProviderCalls()
    {
        using var h = new Harness();

        RunSta(() =>
        {
            using var step = h.CreateStep();
            Pump(step.OnEnterStepAsync());
            Assert.Equal("Ready to write the corpus", step.StateHeading);

            h.Draft.Corpus!.Approved = false;
            Pump(step.ClickContinueAsync());

            Assert.False(step.IsGenerating);
            Assert.True(step.StateCardShown);
            Assert.Equal("Approve the plan first", step.StateHeading);
            Assert.Equal(GenerationUiText.RunRefusedNotApprovedText, step.StatusLabel.Text);
            Assert.Equal(Theme.Warning, step.StatusLabel.ForeColor);
        });

        Assert.Empty(h.Fake.Requests);
        Assert.False(File.Exists(GenerationJournal.PathFor(h.Folder)));
    }

    [Fact]
    public void MostFrequentSkippedReason_PicksTheCommonestReason()
    {
        using var h = new Harness(mixed: true);
        WriteCompleteJournal(h, failOne: false);
        var state = GenerationRunState.From(h.Plan, GenerationJournal.Replay(h.Folder, h.Plan));
        Assert.Equal(MostReason, StepGenerateCorpus.MostFrequentSkippedReason(state));
        Assert.Equal(string.Empty, StepGenerateCorpus.MostFrequentSkippedReason(new GenerationRunState()));
    }

    private const string MostReason = "the OpenAI quota is used up";
    private const string OtherReason = "the image was refused";

    private static List<string> GenerationIds(CorpusPlan plan) =>
        GenerationRunState.From(plan, new JournalReplay()).StatusByThread.Keys.OrderBy(id => id, StringComparer.Ordinal).ToList();

    private static PlannedThread ThreadOf(Harness h, string id) => h.Plan.Threads.First(t => t.Id == id);

    private static JournalRecord RunEnded(Harness h, GenerationOutcome outcome) =>
        new() { Type = JournalRecordType.RunEnded, PlanId = h.Plan.PlanId, Utc = DateTime.UtcNow, Outcome = outcome };

    // Every thread written with every attachment final: Office parts written, media Skipped (the first media item with
    // OtherReason, the rest with MostReason), and with failOne the first Office attachment Failed
    private static void WriteCompleteJournal(Harness h, bool failOne)
    {
        var records = new List<JournalRecord>();
        var media = 0;
        var failed = false;
        foreach (var id in GenerationIds(h.Plan))
        {
            var thread = ThreadOf(h, id);
            var attachments = thread.Messages.SelectMany(m => m.Attachments).Where(a => !string.IsNullOrEmpty(a.DocId))
                .GroupBy(a => a.DocId).Select(g => g.First())
                .Select(a =>
                {
                    if (a.Kind is AttachmentType.Image or AttachmentType.Voicemail)
                        return GenerationRunStateTests.Attachment(h.Plan, a, JournalRecordType.AttachmentSkipped,
                            reason: media++ == 0 ? OtherReason : MostReason);
                    if (failOne && !failed)
                    {
                        failed = true;
                        return GenerationRunStateTests.Attachment(h.Plan, a, JournalRecordType.AttachmentFailed,
                            ThreadWriteStatus.Written, "The provider returned an error");
                    }

                    return GenerationRunStateTests.Attachment(h.Plan, a, JournalRecordType.AttachmentDone);
                })
                .ToArray();
            records.Add(GenerationRunStateTests.DoneWith(h.Plan, thread, attachments));
        }

        records.Add(RunEnded(h, GenerationOutcome.Finished));
        WriteJournal(h, records);
    }

    // Same line format as GenerationJournal.AppendAsync, after a RunStarted record
    private static void WriteJournal(Harness h, IEnumerable<JournalRecord> records)
    {
        var options = new System.Text.Json.JsonSerializerOptions(CaseDraft.JsonOptions) { WriteIndented = false };
        var lines = new[] { new JournalRecord { Type = JournalRecordType.RunStarted, PlanId = h.Plan.PlanId, Utc = DateTime.UtcNow } }
            .Concat(records)
            .Select(r => System.Text.Json.JsonSerializer.Serialize(r, options));
        File.WriteAllLines(GenerationJournal.PathFor(h.Folder), lines);
    }

    // ----- Helpers -----


    private static void WaitForSave(Harness h)
    {
        // The coalescing save runs on the pool; SaveNow flushes the latest snapshot
        Assert.True(h.Fixture.State.CaseStore.SaveNow(h.Draft), h.Fixture.State.CaseStore.LastSaveError);
    }

    internal static Dictionary<string, string> KeyStatuses(string folder)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        var lines = File.ReadAllLines(Path.Combine(folder, AnswerKeyCsvWriter.FileName), Encoding.UTF8);
        var header = SplitCsv(lines[0].TrimStart('﻿'));
        var docCol = Array.IndexOf(header, "DocID");
        var statusCol = Array.IndexOf(header, "Status");
        foreach (var line in lines.Skip(1).Where(l => l.Length > 0))
        {
            var fields = SplitCsv(line);
            if (fields.Length > Math.Max(docCol, statusCol))
                map[fields[docCol]] = fields[statusCol];
        }

        return map;
    }

    // Single-line CSV split with quotes (the key's fields never contain line breaks in this fixture)
    private static string[] SplitCsv(string line)
    {
        var fields = new List<string>();
        var field = new StringBuilder();
        var quoted = false;
        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (quoted)
            {
                if (c == '"' && i + 1 < line.Length && line[i + 1] == '"')
                {
                    field.Append('"');
                    i++;
                }
                else if (c == '"')
                    quoted = false;
                else
                    field.Append(c);
            }
            else if (c == '"')
                quoted = true;
            else if (c == ',')
            {
                fields.Add(field.ToString());
                field.Clear();
            }
            else
                field.Append(c);
        }

        fields.Add(field.ToString());
        return fields.ToArray();
    }

    /// <summary>
    /// Pumps messages on the STA thread until the task completes (30 s), running an optional check on every turn.
    /// </summary>
    [ThreadStatic]
    private static Exception? _uiError;

    internal static void Pump(Task task, Action? onTurn = null)
    {
        var deadline = DateTime.UtcNow + Timeout;
        while (!task.IsCompleted && DateTime.UtcNow < deadline)
        {
            Application.DoEvents();
            if (_uiError != null)
                throw new InvalidOperationException("A WinForms callback threw", _uiError);
            onTurn?.Invoke();
            if (!task.IsCompleted)
                Thread.Sleep(5);
        }

        // One more turn so posted continuations (StateChanged, status) have run
        Application.DoEvents();
        Assert.True(task.IsCompleted, "the task did not finish within 30 s");
        task.GetAwaiter().GetResult();
    }

    internal static void RunSta(Action body)
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try
            {
                // Application.DoEvents would otherwise swap the WinForms context out, and awaits would resume on the pool
                WindowsFormsSynchronizationContext.AutoInstall = false;
                SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());
                // An exception in a WinForms callback would otherwise open the modal ThreadException dialog and hang the test
                ThreadExceptionEventHandler onError = (s, e) => _uiError ??= e.Exception;
                Application.ThreadException += onError;
                try
                {
                    _uiError = null;
                    body();
                    if (_uiError != null)
                        throw new InvalidOperationException("A WinForms callback threw", _uiError);
                }
                finally
                {
                    Application.ThreadException -= onError;
                }
            }
            catch (Exception ex)
            {
                error = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (error != null)
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error).Throw();
    }
}
