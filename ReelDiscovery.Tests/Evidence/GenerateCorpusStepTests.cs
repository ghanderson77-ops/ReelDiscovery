using System.Text;
using ReelDiscovery.Helpers;
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
        public Harness(bool approved = true)
        {
            Fixture = new GenerateCorpusRenderTests.StepFixture(approved);
            (Gateway, Fake) = CorpusTestData.CreateGateway();
            Fake.Respond = EvidenceTestData.RenderResponder(Fixture.Plan, Fixture.Draft.Guide!);
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
            Assert.Equal("Ready to write the relevant emails", step.StateHeading);
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
            Assert.Equal(state.Written + state.Warnings, generation.ThreadsWritten + generation.ThreadsWithWarnings);
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

            var counts = ToCounts(h, generation);
            Assert.Equal(GenerationUiText.OutcomeText(GenerationOutcome.Finished, counts, string.Empty).Text, step.StatusLabel.Text);
            Assert.Equal(Theme.Success, step.StatusLabel.ForeColor);
            Assert.False(step.CancelButton.Visible);
            Assert.True(step.StartOverButton.Visible);
            Assert.False(step.BannerShown);
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
            Assert.Equal(GenerationUiText.BannerHeading, step.BannerHeadingText);
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
            Assert.Equal(h.Relevant.Count, GenerationJournal.Replay(h.Folder, h.Plan).LastByThread.Count);
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

    // ----- Helpers -----


    private static GenerationCounts ToCounts(Harness h, GenerationState generation) => new()
    {
        TotalThreads = generation.ThreadsTotal,
        DoneThreads = generation.ThreadsWritten + generation.ThreadsWithWarnings,
        WarningThreads = generation.ThreadsWithWarnings,
        FailedThreads = generation.ThreadsFailed,
        EmailsWritten = generation.EmailsWritten,
        FilesWritten = generation.FilesWritten,
        Mailboxes = generation.Mailboxes
    };

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
