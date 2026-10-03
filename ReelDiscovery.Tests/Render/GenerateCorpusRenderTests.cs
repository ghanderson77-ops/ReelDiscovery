using ReelDiscovery.Helpers;
using ReelDiscovery.Models;
using ReelDiscovery.Models.Case;
using ReelDiscovery.Models.Corpus;
using ReelDiscovery.Models.Evidence;
using ReelDiscovery.Services.Case;
using ReelDiscovery.Services.Evidence;
using ReelDiscovery.Tests.Evidence;
using ReelDiscovery.UserControls;

namespace ReelDiscovery.Tests.Render;

/// <summary>
/// Renders the Generate Corpus step at 144 DPI (05-UI-SPEC S1, render states 1-6) at Large and WizardMin from seeded
/// GenerationView snapshots. No run is started.
/// </summary>
[Trait("Category", "Render")]
[Collection(RenderCollection.Name)]
public class GenerateCorpusRenderTests
{
    // ----- States 1-6 -----

    [Fact]
    public void Ready()
    {
        RenderBoth("generate-corpus-ready", GenerateCorpusRenderState.Ready, null, (step, labels) =>
        {
            Assert.True(step.StateCardShown);
            Assert.Equal("Ready to write the relevant emails", step.StateHeading);
            Assert.Contains("Estimated cost: ≈$", step.StateBody);
            Assert.True(step.StateActionButton.Visible);
            Assert.Equal("Generate Emails", step.StateActionButton.Text);
            Assert.Equal(ButtonStyle.Primary, ((ThemedButton)step.StateActionButton).Variant);
            Assert.False(step.Tiles[0].Visible);
            Assert.True(step.NoticeLabel.Visible);
            Assert.StartsWith("This step writes the ", step.NoticeLabel.Text);
            Assert.False(step.CancelButton.Visible);
        });
    }

    [Fact]
    public void Running()
    {
        var view = RunningView();
        RenderBoth("generate-corpus-running", GenerateCorpusRenderState.Running, view, (step, labels) =>
        {
            Assert.True(step.CancelButton.Visible);
            Assert.True(step.ProgressBar.Visible);
            Assert.False(step.StartOverButton.Visible);
            Assert.False(step.RetryFailedButton.Visible);
            Assert.False(step.BannerPanel.Visible);
            Assert.Equal(GenerationUiText.ProgressText(view), step.StatusLabel.Text);
            Assert.Equal(Theme.Accent, step.StatusLabel.ForeColor);
            Assert.Equal(40, step.ActivityGrid.Rows.Count);
            Assert.Equal(14, step.WavesGrid.Rows.Count);
            // Newest first
            Assert.Equal(GenerationUiText.ActivityText(view.Events[^1]).Text, step.ActivityGrid.Rows[0].Cells["Event"].Value);
            Assert.Equal(GenerationUiText.ActivityText(view.Events[0]).Text, step.ActivityGrid.Rows[39].Cells["Event"].Value);
            var percent = step.ProgressBar.Value * 100 / step.ProgressBar.Maximum;
            Assert.InRange(percent, 25, 35);
            Assert.False(step.CanMoveNext);
        });
    }

    [Fact]
    public void Partial()
    {
        var view = FinishedView(done: 21, total: 60, warnings: 1, failed: 2, cancelled: true);
        RenderBoth("generate-corpus-partial", GenerateCorpusRenderState.Partial, view, (step, labels) =>
        {
            Assert.True(step.BannerPanel.Visible);
            Assert.Contains(GenerationUiText.BannerHeading, labels);
            Assert.Contains(GenerationUiText.BannerBody(21, 60, 2), labels);
            Assert.True(step.ContinueButton.Visible);
            Assert.True(step.StartOverButton.Visible);
            Assert.False(step.RetryFailedButton.Visible);
            Assert.Equal(Theme.Warning, step.StatusLabel.ForeColor);
        });
    }

    [Fact]
    public void FinishedClean()
    {
        var view = FinishedView(done: 60, total: 60, warnings: 0, failed: 0, cancelled: false);
        RenderBoth("generate-corpus-finished-clean", GenerateCorpusRenderState.FinishedClean, view, (step, labels) =>
        {
            Assert.False(step.BannerPanel.Visible);
            Assert.True(step.StartOverButton.Visible);
            Assert.False(step.RetryFailedButton.Visible);
            Assert.Equal("0", step.Tiles[4].ValueText);
            Assert.Equal("0", step.Tiles[5].ValueText);
            Assert.Equal(Theme.Text, step.Tiles[4].ValueColor);
            Assert.Equal(Theme.Text, step.Tiles[5].ValueColor);
            Assert.Equal(Theme.Success, step.StatusLabel.ForeColor);
        });
    }

    [Fact]
    public void FinishedWithIssues()
    {
        var view = FinishedView(done: 58, total: 60, warnings: 3, failed: 2, cancelled: false);
        RenderBoth("generate-corpus-finished-issues", GenerateCorpusRenderState.FinishedIssues, view, (step, labels) =>
        {
            Assert.True(step.StartOverButton.Visible);
            Assert.True(step.RetryFailedButton.Visible);
            Assert.False(step.BannerPanel.Visible);
            Assert.Equal("3", step.Tiles[4].ValueText);
            Assert.Equal("2", step.Tiles[5].ValueText);
            Assert.Equal(Theme.Warning, step.Tiles[4].ValueColor);
            Assert.Equal(Theme.Danger, step.Tiles[5].ValueColor);
            Assert.Equal(Theme.Warning, step.StatusLabel.ForeColor);
        });
    }

    [Fact]
    public void NotApprovedCard()
    {
        RenderBoth("generate-corpus-not-approved", GenerateCorpusRenderState.NotApproved, null, (step, labels) =>
        {
            Assert.Equal("Approve the plan first", step.StateHeading);
            Assert.Equal("Review Plan", step.StateActionButton.Text);
            Assert.Equal(ButtonStyle.Primary, ((ThemedButton)step.StateActionButton).Variant);
            Assert.False(step.NoticeLabel.Visible);
        });
    }

    [Fact]
    public void PlanChangedCard()
    {
        RenderBoth("generate-corpus-plan-changed", GenerateCorpusRenderState.PlanChanged, null, (step, labels) =>
        {
            Assert.Equal("The plan changed after emails were generated", step.StateHeading);
            Assert.StartsWith("12 threads were written for an earlier plan", step.StateBody);
            Assert.Equal("Start Over...", step.StateActionButton.Text);
            Assert.Equal(ButtonStyle.Danger, ((ThemedButton)step.StateActionButton).Variant);
        }, earlierThreads: 12);
    }

    [Fact]
    public void UnreadableCard()
    {
        RenderBoth("generate-corpus-unreadable", GenerateCorpusRenderState.Unreadable, null, (step, labels) =>
        {
            Assert.Equal("The saved run could not be opened", step.StateHeading);
            Assert.StartsWith("The saved run could not be read: the file is locked by another program.", step.StateBody);
            Assert.Equal(ButtonStyle.Danger, ((ThemedButton)step.StateActionButton).Variant);
        }, reason: "The saved run could not be read: the file is locked by another program.");
    }

    // ----- Harness -----

    private sealed class Measured
    {
        public List<string> Labels = new();
        public bool DashboardShown;
        public int FirstTileLeft;
        public int LastTileRight;
        public List<int> TileHeights = new();
        public int WavesLeft;
        public int ActivityRight;
        public int HeaderRight = -1;
        public bool AnyGridHorizontalScroll;
        public int DatesWidth;
        public int EventWidth;
        public int ThreadsValueNeeded;
        public int ThreadsValueWidth;
    }

    private static void RenderBoth(string name, GenerateCorpusRenderState render, GenerationView? view,
        Action<StepGenerateCorpus, List<string>> verify, string reason = "", int earlierThreads = 0)
    {
        RenderHarness.RunSta(() =>
        {
            foreach (var size in new[] { RenderHarness.Large, RenderHarness.WizardMin })
            {
                using var fixture = new StepFixture(approved: render != GenerateCorpusRenderState.NotApproved);
                StepGenerateCorpus? step = null;
                var measured = new Measured();
                var result = RenderHarness.RenderControl(() =>
                {
                    step = new StepGenerateCorpus();
                    step.LoadForRender(fixture.State, render, view, reason, earlierThreads);
                    return step;
                }, name, size, control =>
                {
                    control.PerformLayout();
                    Application.DoEvents();
                    Measure(step!, measured);
                    // Visible flags and grid rows only hold while the form is shown, so state checks run here
                    verify(step!, measured.Labels);
                });

                Assert.True(File.Exists(result.PngPath), result.PngPath);
                Assert.True(result.DeviceDpi >= RenderHarness.MinimumDpi, $"DPI {result.DeviceDpi}");
                Assert.Empty(result.Clipping);
                Assert.False(measured.AnyGridHorizontalScroll);

                if (!measured.DashboardShown)
                    continue;

                // Rule 17: tiles and body cards share outer edges; the header's rightmost control ends there too
                Assert.Single(measured.TileHeights.Distinct());
                Assert.True(Math.Abs(measured.FirstTileLeft - measured.WavesLeft) <= 1, $"tile left {measured.FirstTileLeft}, waves left {measured.WavesLeft}");
                Assert.True(Math.Abs(measured.LastTileRight - measured.ActivityRight) <= 1, $"tile right {measured.LastTileRight}, activity right {measured.ActivityRight}");
                if (measured.HeaderRight >= 0)
                    Assert.True(Math.Abs(measured.HeaderRight - measured.ActivityRight) <= 1, $"header right {measured.HeaderRight}, cards right {measured.ActivityRight}");

                // Rule 18 at WizardMin: Dates >= 120 and Event >= 160 (logical)
                var dpiScale = result.DeviceDpi / 96.0;
                Assert.True(measured.DatesWidth >= (int)Math.Floor(120 * dpiScale), $"Dates {measured.DatesWidth} at {size}");
                Assert.True(measured.EventWidth >= (int)Math.Floor(160 * dpiScale), $"Event {measured.EventWidth} at {size}");

                // Rule 19: the Threads value never ellipsizes at Large
                if (size == RenderHarness.Large)
                    Assert.True(measured.ThreadsValueNeeded <= measured.ThreadsValueWidth, $"Threads value needs {measured.ThreadsValueNeeded}, has {measured.ThreadsValueWidth}");
            }
        });
    }

    private static void Measure(StepGenerateCorpus step, Measured m)
    {
        m.Labels = AllControls(step).OfType<Label>().Where(l => l.Visible).Select(l => l.Text).ToList();
        m.AnyGridHorizontalScroll = new[] { step.WavesGrid, step.ActivityGrid }
            .Any(g => g.Visible && g.Controls.OfType<HScrollBar>().Any(b => b.Visible));
        m.DashboardShown = step.WavesCard.Visible;
        if (!m.DashboardShown)
            return;

        m.TileHeights = step.Tiles.Select(t => t.Height).ToList();
        m.FirstTileLeft = Left(step, step.Tiles[0]);
        m.LastTileRight = Left(step, step.Tiles[^1]) + step.Tiles[^1].Width;
        m.WavesLeft = Left(step, step.WavesCard);
        m.ActivityRight = Left(step, step.ActivityCard) + step.ActivityCard.Width;
        var header = new[] { step.RetryFailedButton, step.StartOverButton }.FirstOrDefault(b => b.Visible);
        if (header != null)
            m.HeaderRight = Left(step, header) + header.Width;
        m.DatesWidth = step.WavesGrid.Columns["Dates"]!.Width;
        m.EventWidth = step.ActivityGrid.Columns["Event"]!.Width;
        m.ThreadsValueNeeded = TextRenderer.MeasureText(step.Tiles[0].ValueText, Theme.TitleFont()).Width;
        m.ThreadsValueWidth = step.Tiles[0].ValueWidth;
    }

    private static int Left(Control root, Control control) => root.PointToClient(control.PointToScreen(Point.Empty)).X;

    private static IEnumerable<Control> AllControls(Control root)
    {
        foreach (Control child in root.Controls)
        {
            yield return child;
            foreach (var nested in AllControls(child))
                yield return nested;
        }
    }

    // ----- Seeded views (neutral subjects, no names from fictional works) -----

    private static readonly string[] Subjects =
    {
        "Schedule update",
        "Pay application 7 review",
        "Change order pricing",
        "Site walk notes",
        "Window submittal comments",
        "RE: Revised delivery dates",
        "Punch list items for level 3",
        "Retention release request"
    };

    private static readonly DateOnly FirstWave = new(2023, 11, 6);

    private static List<WaveProgress> Waves(int count, Func<int, (int Done, int Warnings, int Failed, bool Running)> state)
    {
        var waves = new List<WaveProgress>();
        for (var i = 1; i <= count; i++)
        {
            var start = FirstWave.AddDays((i - 1) * 21);
            var (done, warnings, failed, running) = state(i);
            waves.Add(new WaveProgress
            {
                Index = i,
                Start = start,
                // Wave 3 spans the year end
                End = start.AddDays(i == 3 ? 20 : 13),
                Threads = i % 3 == 0 ? 5 : 4,
                Emails = i % 3 == 0 ? 14 : 11,
                Done = done,
                Warnings = warnings,
                Failed = failed,
                Running = running
            });
        }

        return waves;
    }

    /// <summary>
    /// Wave 3 of 14 running, 40 activity rows (retries and warnings included), 18 of 60 threads finished (30%).
    /// </summary>
    private static GenerationView RunningView()
    {
        var waves = Waves(14, i => i switch
        {
            1 => (4, 1, 0, false),
            2 => (3, 0, 1, false),
            3 => (2, 0, 0, true),
            _ => (0, 0, 0, false)
        });
        var events = new List<GenerationEvent>();
        var time = new DateTime(2026, 10, 3, 9, 58, 12, DateTimeKind.Local);
        events.Add(new GenerationEvent { Kind = GenerationEventKind.RunStarted, LocalTime = time, Threads = 60, WaveCount = 14 });
        var thread = 1;
        while (events.Count < 40)
        {
            time = time.AddSeconds(17);
            var id = $"T-{thread:0000}";
            var subject = Subjects[thread % Subjects.Length];
            switch (events.Count % 6)
            {
                case 1:
                    events.Add(new GenerationEvent
                    {
                        Kind = GenerationEventKind.WaveStarted,
                        LocalTime = time,
                        WaveIndex = Math.Min(3, events.Count / 12 + 1),
                        WaveCount = 14,
                        WaveStart = waves[0].Start,
                        WaveEnd = waves[0].End,
                        Threads = 4
                    });
                    break;
                case 2:
                    events.Add(new GenerationEvent
                    {
                        Kind = GenerationEventKind.Retry,
                        LocalTime = time,
                        ThreadId = id,
                        Attempt = 1,
                        Violations = new List<string>
                        {
                            "msg 2: names a person who is not in the cast",
                            "msg 3: uses $48,200 for the change order total; the case value is $46,800"
                        }
                    });
                    break;
                case 3:
                    events.Add(new GenerationEvent
                    {
                        Kind = GenerationEventKind.KeptWithWarnings,
                        LocalTime = time,
                        ThreadId = id,
                        Subject = subject,
                        Violations = new List<string> { "msg 1: mentions the inspection report, dated after this email" }
                    });
                    thread++;
                    break;
                case 4:
                    events.Add(new GenerationEvent
                    {
                        Kind = GenerationEventKind.ThreadFailed,
                        LocalTime = time,
                        ThreadId = id,
                        Reason = "The provider timed out"
                    });
                    thread++;
                    break;
                default:
                    events.Add(new GenerationEvent
                    {
                        Kind = GenerationEventKind.ThreadWritten,
                        LocalTime = time,
                        ThreadId = id,
                        Subject = subject,
                        Emails = 3,
                        Details = 2
                    });
                    thread++;
                    break;
            }
        }

        return new GenerationView
        {
            Counts = new GenerationCounts
            {
                TotalThreads = 60,
                DoneThreads = 16,
                WarningThreads = 1,
                FailedThreads = 2,
                WaveCount = 14,
                EmailsPlanned = 172,
                EmailsWritten = 47,
                FilesWritten = 131,
                Mailboxes = 9,
                LedgerEntries = 38,
                CostUsd = 1.84m,
                RemainingCostUsd = 4.92m,
                Running = true
            },
            Waves = waves,
            Events = events,
            Stage = GenerationStage.Writing,
            StageWave = 3,
            StageWaveCount = 14,
            StageWaveStart = waves[2].Start,
            StageWaveEnd = waves[2].End,
            StageCount = 5
        };
    }

    private static GenerationView FinishedView(int done, int total, int warnings, int failed, bool cancelled)
    {
        var finished = done + failed;
        var waves = Waves(14, i =>
        {
            // Threads fill waves in order; warnings and failures land in waves 2 and 5
            var before = Enumerable.Range(1, i - 1).Sum(k => k % 3 == 0 ? 5 : 4);
            var size = i % 3 == 0 ? 5 : 4;
            var waveDone = Math.Clamp(finished - before, 0, size);
            var waveFailed = i == 5 ? Math.Min(failed, waveDone) : 0;
            var waveWarnings = i == 2 ? Math.Min(warnings, waveDone) : 0;
            return (waveDone - waveFailed, waveWarnings, waveFailed, false);
        });
        var events = new List<GenerationEvent>();
        var time = new DateTime(2026, 10, 3, 14, 2, 5, DateTimeKind.Local);
        for (var t = 1; t <= Math.Min(finished, 30); t++)
        {
            time = time.AddSeconds(23);
            var id = $"T-{t:0000}";
            events.Add(t <= failed && failed > 0
                ? new GenerationEvent { Kind = GenerationEventKind.ThreadFailed, LocalTime = time, ThreadId = id, Reason = "The model refused to write this thread" }
                : t <= failed + warnings
                    ? new GenerationEvent { Kind = GenerationEventKind.KeptWithWarnings, LocalTime = time, ThreadId = id, Subject = Subjects[t % Subjects.Length], Violations = new List<string> { "msg 2: has 3 messages; the plan has 4" } }
                    : new GenerationEvent { Kind = GenerationEventKind.ThreadWritten, LocalTime = time, ThreadId = id, Subject = Subjects[t % Subjects.Length], Emails = 3, Details = 1 });
        }
        events.Add(cancelled
            ? new GenerationEvent { Kind = GenerationEventKind.RunCancelled, LocalTime = time.AddSeconds(5), Done = done, Total = total }
            : new GenerationEvent { Kind = GenerationEventKind.RunFinished, LocalTime = time.AddSeconds(5) });

        return new GenerationView
        {
            Counts = new GenerationCounts
            {
                TotalThreads = total,
                DoneThreads = done,
                WarningThreads = warnings,
                FailedThreads = failed,
                WaveCount = 14,
                EmailsPlanned = 180,
                EmailsWritten = done * 3,
                FilesWritten = done * 8,
                Mailboxes = 12,
                LedgerEntries = done * 2,
                CostUsd = done * 0.11m,
                RemainingCostUsd = (total - done) * 0.11m,
                Finished = !cancelled
            },
            Waves = waves,
            Events = events
        };
    }

    // ----- Fixture -----

    /// <summary>
    /// A saved case with the evidence fixture plan (approved or not) under a temp Cases root; deleted on dispose.
    /// </summary>
    internal sealed class StepFixture : IDisposable
    {
        private readonly string _root;

        public StepFixture(bool approved = true)
        {
            _root = Path.Combine(Path.GetTempPath(), "rd-generate-corpus-" + Guid.NewGuid().ToString("N"));
            var store = new CaseStore(Path.Combine(_root, "Cases"), Path.Combine(_root, "AppData"));
            var (draft, plan) = EvidenceTestData.ApprovedCase();
            if (!approved)
                draft.Corpus!.Approved = false;
            draft.AdvanceStage(CaseStage.ReviewPlan);
            Folder = store.EnsureFolder(draft);
            Assert.True(store.SaveNow(draft), store.LastSaveError);
            Draft = draft;
            Plan = plan;
            State = new WizardState { Case = draft, CaseStore = store, OpenAIApiKey = "sk-render-test", OpenAIKeyTested = true };
        }

        public WizardState State { get; }
        public CaseDraft Draft { get; }
        public CorpusPlan Plan { get; }
        public string Folder { get; }

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

/// <summary>
/// Entering Generate Corpus (UI-SPEC S1 entry rules): the right card or dashboard from the journal, and never a run.
/// Not a render test: the step is never shown.
/// </summary>
public class GenerateCorpusEntryTests
{
    [Fact]
    public void NoJournal_ShowsReady_AndWritesNothing()
    {
        using var fixture = new GenerateCorpusRenderTests.StepFixture();

        Enter(fixture, step =>
        {
            Assert.True(step.StateCardShown);
            Assert.Equal("Ready to write the relevant emails", step.StateHeading);
            Assert.False(step.IsGenerating);
            // Never auto-starts: no journal, no mailboxes (U3)
            Assert.False(File.Exists(GenerationJournal.PathFor(fixture.Folder)));
            Assert.False(Directory.Exists(Path.Combine(fixture.Folder, CorpusPlan.MailboxesRoot)));
        });
    }

    [Fact]
    public void UnapprovedPlan_ShowsNotApproved()
    {
        using var fixture = new GenerateCorpusRenderTests.StepFixture(approved: false);

        Enter(fixture, step =>
        {
            Assert.Equal("Approve the plan first", step.StateHeading);
            Assert.Equal(string.Empty, step.NoticeText);
        });
    }

    [Fact]
    public void JournalWithPendingThreads_ShowsDashboardAndBanner()
    {
        using var fixture = new GenerateCorpusRenderTests.StepFixture();
        var threads = fixture.Plan.Threads.Where(t => t.Relevance == ThreadRelevance.Relevant).OrderBy(t => t.Id, StringComparer.Ordinal).ToList();
        Append(fixture,
            new JournalRecord { Type = JournalRecordType.RunStarted, PlanId = fixture.Plan.PlanId, Utc = DateTime.UtcNow },
            GenerationRunStateTests.Done(fixture.Plan, threads[0], ThreadWriteStatus.Written, 0.10m),
            GenerationRunStateTests.Failed(fixture.Plan, threads[1], 0.02m),
            new JournalRecord { Type = JournalRecordType.RunEnded, PlanId = fixture.Plan.PlanId, Utc = DateTime.UtcNow, Outcome = GenerationOutcome.Cancelled });

        Enter(fixture, step =>
        {
            Assert.False(step.StateCardShown);
            Assert.True(step.BannerShown);
            // Threads tile: 1 written (the variant depends on the unshown tile width)
            Assert.StartsWith("1", step.Tiles[0].ValueText);
            Assert.Equal("1", step.Tiles[5].ValueText);
            Assert.Equal(WavePlanner.Plan(fixture.Plan).Count, step.WavesGrid.Rows.Count);
            Assert.Equal(threads.Count, step.WavesGrid.Rows.Cast<DataGridViewRow>()
                .Sum(r => int.Parse(((string)r.Cells["Threads"].Value!).Split('/')[1].Trim())));
            // Failed, written, cancelled: newest first
            Assert.Equal(3, step.ActivityGrid.Rows.Count);
            Assert.StartsWith("Cancelled.", (string)step.ActivityGrid.Rows[0].Cells["Event"].Value!);
            Assert.False(step.IsGenerating);
        });
    }

    [Fact]
    public void JournalForAnotherPlan_ShowsPlanChanged()
    {
        using var fixture = new GenerateCorpusRenderTests.StepFixture();
        var thread = fixture.Plan.Threads.First(t => t.Relevance == ThreadRelevance.Relevant);
        var foreign = GenerationRunStateTests.Done(fixture.Plan, thread, ThreadWriteStatus.Written, 0.10m);
        foreign.PlanId = Guid.NewGuid();
        Append(fixture, new JournalRecord { Type = JournalRecordType.RunStarted, PlanId = foreign.PlanId, Utc = DateTime.UtcNow }, foreign);

        Enter(fixture, step =>
        {
            Assert.Equal("The plan changed after emails were generated", step.StateHeading);
            Assert.StartsWith("1 thread was written for an earlier plan", step.StateBody);
        });
    }

    private static void Append(GenerateCorpusRenderTests.StepFixture fixture, params JournalRecord[] records)
    {
        var journal = new GenerationJournal(fixture.Folder);
        foreach (var record in records)
            journal.AppendAsync(record).GetAwaiter().GetResult();
    }

    /// <summary>
    /// Creates the step on an STA thread with a WinForms context, pumps until OnEnterStepAsync completes and checks it on
    /// that thread (handles created by tooltips belong to it).
    /// </summary>
    private static void Enter(GenerateCorpusRenderTests.StepFixture fixture, Action<StepGenerateCorpus> verify)
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try
            {
                SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());
                using var step = new StepGenerateCorpus();
                step.BindState(fixture.State);
                var task = step.OnEnterStepAsync();
                var deadline = DateTime.UtcNow.AddSeconds(30);
                while (!task.IsCompleted && DateTime.UtcNow < deadline)
                {
                    Application.DoEvents();
                    Thread.Sleep(5);
                }
                task.GetAwaiter().GetResult();
                verify(step);
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
