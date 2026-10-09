using ReelDiscovery.Helpers;
using ReelDiscovery.Models;
using ReelDiscovery.Models.Case;
using ReelDiscovery.Models.Discovery;
using ReelDiscovery.Services.Case;
using ReelDiscovery.Services.Discovery;
using ReelDiscovery.Tests.Evidence;
using ReelDiscovery.UserControls;

namespace ReelDiscovery.Tests.Discovery;

/// <summary>
/// Discovery Documents step logic (08-14): binding to CaseDraft.Discovery and the step model, field validation, the
/// lifecycle (paint first, corpus probe off the UI thread, stale results discarded), Next never blocking and the run-safety
/// hooks. Not a render test: the step is never shown. Everything runs on one STA thread with a WinForms context.
/// </summary>
public class StepDiscoveryDocumentsTests
{
    private sealed class Fixture : IDisposable
    {
        private readonly string _root;

        public Fixture(CaseDraft draft)
        {
            _root = Path.Combine(Path.GetTempPath(), "rd-discovery-step-" + Guid.NewGuid().ToString("N"));
            var store = new CaseStore(Path.Combine(_root, "Cases"), Path.Combine(_root, "AppData"));
            store.EnsureFolder(draft);
            Draft = draft;
            State = new WizardState { Case = draft, CaseStore = store, OpenAIApiKey = "sk-step-test", OpenAIKeyTested = true };
        }

        public WizardState State { get; }
        public CaseDraft Draft { get; }
        public int Saves;
        public bool KeyReady = true;
        public readonly List<string> Confirms = new();
        public bool ConfirmAnswer = true;

        public StepDiscoveryDocuments CreateStep()
        {
            var step = new StepDiscoveryDocuments();
            step.BindState(State);
            step.KeyReady = () => KeyReady;
            step.SaveCase = () =>
            {
                Saves++;
                return Task.FromResult(true);
            };
            step.Confirm = (title, text, icon) =>
            {
                Confirms.Add(title);
                return ConfirmAnswer;
            };
            return step;
        }

        public void Dispose()
        {
            try
            {
                Directory.Delete(_root, recursive: true);
            }
            catch (IOException)
            {
                // A file handle may linger briefly; the temp folder is harmless
            }
        }
    }

    private static void Run(CaseDraft draft, Action<Fixture, StepDiscoveryDocuments> body, Action<Fixture>? setup = null)
    {
        using var fixture = new Fixture(draft);
        setup?.Invoke(fixture);
        GenerateCorpusStepTests.RunSta(() =>
        {
            using var step = fixture.CreateStep();
            body(fixture, step);
        });
    }

    private static void Enter(StepDiscoveryDocuments step) => GenerateCorpusStepTests.Pump(step.OnEnterStepAsync());

    private static List<string> Statuses(StepDiscoveryDocuments step) =>
        step.DocumentsGrid.Rows.Cast<DataGridViewRow>().Select(r => (string)r.Cells["Status"].Value!).ToList();

    // Seven or more eligible witnesses so the 6-witness cap can be reached
    private static CaseDraft SevenWitnessDraft()
    {
        var draft = DiscoveryTestData.LitigationDraft();
        var guide = draft.Guide!;
        var template = guide.Cast.First(c => c.Id == "CH-002");
        foreach (var (id, first, last) in new[] { ("CH-090", "Dana", "Ortiz"), ("CH-091", "Glen", "Ashby") })
        {
            guide.Cast.Add(new CastMember
            {
                Id = id,
                FirstName = first,
                LastName = last,
                Title = "Estimator",
                OrganizationId = template.OrganizationId,
                Tier = CastTier.Key,
                Role = CastRole.Staff,
                Email = first.ToLowerInvariant() + "@example.com"
            });
        }
        return draft;
    }

    // ----- IWizardStep members -----

    [Fact]
    public void Members_TitleNextAndNavigation()
    {
        Run(DiscoveryTestData.LitigationDraft(), (f, step) =>
        {
            Assert.Equal("Discovery Documents", step.StepTitle);
            Assert.Equal("Next >", step.NextButtonText);
            Enter(step);
            Assert.True(step.CanMoveNext);
            Assert.True(step.CanMoveBack);
            Assert.False(step.IsGenerating);

            var changes = 0;
            step.StateChanged += (s, e) => changes++;
            using var cts = new CancellationTokenSource();
            step.EnterRunState(cts);
            Assert.True(step.IsGenerating);
            Assert.False(step.CanMoveNext);
            Assert.False(step.CanMoveBack);
            Assert.Equal(DiscoveryUiText.CancelGenerationButton, step.RunButton.Text);
            Assert.Equal(ButtonStyle.Danger, ((ThemedButton)step.RunButton).Variant);
            Assert.False(step.PagesText.Enabled);
            Assert.False(step.RfpCheck.Enabled);
            Assert.True(step.WitnessGrid.Columns["Check"]!.ReadOnly);

            step.ExitRunState();
            Assert.True(step.CanMoveNext);
            Assert.Equal(DiscoveryUiText.GenerateButton, step.RunButton.Text);
            Assert.Equal(ButtonStyle.Primary, ((ThemedButton)step.RunButton).Variant);
            Assert.Equal(2, changes);
        });
    }

    // ----- Entering the step -----

    [Fact]
    public void OnEnter_StandardCase_AppliesDefaultsAndBuildsRows()
    {
        var draft = DiscoveryTestData.LitigationDraft();
        draft.Discovery = null;
        Run(draft, (f, step) =>
        {
            Enter(step);

            var settings = f.Draft.Discovery;
            Assert.NotNull(settings);
            Assert.False(settings!.UserTouched);

            // Eligible witnesses in rank order, with the preset's two default checks
            var eligible = DepositionPlanner.Eligible(f.Draft);
            var grid = step.WitnessGrid;
            Assert.Equal(eligible.Select(w => w.FullName), grid.Rows.Cast<DataGridViewRow>().Select(r => (string)r.Cells["Witness"].Value!));
            Assert.Equal(eligible.Select(w => w.FactCount.ToString("N0")), grid.Rows.Cast<DataGridViewRow>().Select(r => (string)r.Cells["Facts"].Value!));
            var checkedIds = grid.Rows.Cast<DataGridViewRow>().Where(r => r.Cells["Check"].Value is true).Select(r => (string)r.Tag!).ToList();
            Assert.Equal(2, checkedIds.Count);
            Assert.Equal(DepositionPlanner.DefaultWitnessIds(f.Draft).OrderBy(x => x), checkedIds.OrderBy(x => x));
            Assert.Equal(DiscoveryUiText.SelectionHint(2), step.SelectionHintLabel.Text);

            Assert.Equal("25", step.PagesText.Text);
            Assert.True(step.RfpCheck.Checked);
            Assert.True(step.RogCheck.Checked);
            Assert.True(step.RfaCheck.Checked);
            Assert.Equal("25", step.RfpText.Text);
            Assert.Equal("18", step.RogText.Text);
            Assert.Equal("20", step.RfaText.Text);
            Assert.Equal(new[] { true, true, true }, step.KindRowsShown);

            // Default parties: the first plaintiff serves the first defendant
            Assert.Equal(settings.PropoundingPartyId, step.Model.Parties[step.ServedByCombo.SelectedIndex].Id);
            Assert.NotEqual(step.ServedByCombo.SelectedIndex, step.ServedOnCombo.SelectedIndex);
            Assert.Equal("Served by", step.ServedByLabel.Text);

            Assert.Equal(5, step.DocumentsGrid.Rows.Count);
            Assert.All(Statuses(step), s => Assert.Equal("Not generated", s));
            Assert.True(step.RunButton.Enabled);
            Assert.StartsWith("Estimated cost ≈$", step.EstimateLabel.Text);
            Assert.Equal(5, step.EstimateTooltipText.Split(Environment.NewLine).Length);
            Assert.False(step.StateCardShown);
            Assert.Equal(DiscoveryUiText.Notice(memo: false, corpusUsable: false), step.NoticeLabel.Text);
        });
    }

    [Fact]
    public void OnEnter_NoTestedKey_RunDisabledAndKeyHint()
    {
        Run(DiscoveryTestData.LitigationDraft(), (f, step) =>
        {
            Enter(step);
            Assert.False(step.RunButton.Enabled);
            Assert.Equal("Test an API key on the API Configuration step first.", step.EstimateLabel.Text);
            Assert.Equal(Theme.Warning, step.EstimateLabel.ForeColor);
        }, f => f.KeyReady = false);
    }

    [Fact]
    public void OnEnter_UserTouchedSettings_AreKept()
    {
        var draft = DiscoveryTestData.LitigationDraft();
        draft.Discovery = DiscoverySettings.ForPreset(ComplexityLevel.Standard);
        draft.Discovery.PagesPerExcerpt = 40;
        draft.Discovery.RogEnabled = false;
        draft.Discovery.UserTouched = true;
        Run(draft, (f, step) =>
        {
            Enter(step);
            Assert.Equal("40", step.PagesText.Text);
            Assert.False(step.RogCheck.Checked);
            Assert.False(step.RogText.Enabled);
            Assert.Empty(f.Draft.Discovery!.WitnessIds);
            // Nothing checked as a witness, ROG unchecked: RFP and RFA only
            Assert.Equal(2, step.DocumentsGrid.Rows.Count);
        });
    }

    [Fact]
    public void OnEnter_NoGuide_ShowsStateCardAndNextStaysEnabled()
    {
        Run(DiscoveryTestData.NoGuideDraft(), (f, step) =>
        {
            Enter(step);
            Assert.True(step.StateCardShown);
            Assert.True(step.CanMoveNext);
            Assert.False(step.RunButton.Enabled);
            Assert.Null(f.Draft.Discovery);
            Assert.True(PumpResult(step.ValidateStepAsync()));
        });
    }

    [Fact]
    public void OnEnter_MemoCriminal_HidesRogAndRfaAndUsesMemoLabels()
    {
        Run(DiscoveryTestData.MemoCriminalDraft(), (f, step) =>
        {
            Enter(step);
            Assert.Equal(new[] { true, false, false }, step.KindRowsShown);
            Assert.Equal("Sworn witness interviews", step.DepositionsCaptionLabel.Text);
            Assert.Equal(DiscoveryUiText.MemoNote(DiscoveryVariant.MemoCriminal), step.MemoNoteLabel.Text);
            Assert.NotEmpty(step.MemoNoteLabel.Text);
            Assert.Equal("Requested by", step.ServedByLabel.Text);
            Assert.Equal("Directed to", step.ServedOnLabel.Text);
            Assert.Equal("Subpoena rider (documents)", step.RfpCheck.Text);
            Assert.DoesNotContain(step.DocumentsGrid.Rows.Cast<DataGridViewRow>(),
                r => ((DiscoveryRowInfo)r.Tag!).Kind is DiscoveryKind.Interrogatories or DiscoveryKind.RequestsForAdmission);
        });
    }

    [Fact]
    public async Task OnEnter_PaintsBeforeCorpusDetection_ThenRefreshes()
    {
        var folder = Path.Combine(Path.GetTempPath(), "rd-discovery-corpus-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var draft = await DiscoveryCorpusFixture.WithWrittenCorpusAsync(folder);
            GenerateCorpusStepTests.RunSta(() =>
            {
                using var step = new StepDiscoveryDocuments();
                step.BindState(new WizardState { Case = draft, OpenAIApiKey = "sk-step-test", OpenAIKeyTested = true });
                step.KeyReady = () => true;
                step.SaveCase = () => Task.FromResult(true);

                var enter = step.OnEnterStepAsync();
                // The synchronous part painted the rows; the probe's result is not applied until the UI thread pumps
                Assert.Equal(5, step.DocumentsGrid.Rows.Count);
                Assert.False(step.Model.CorpusUsable);
                Assert.Equal(DiscoveryUiText.Notice(memo: false, corpusUsable: false), step.NoticeLabel.Text);

                GenerateCorpusStepTests.Pump(enter);
                Assert.True(step.Model.CorpusUsable);
                Assert.Equal(DiscoveryUiText.Notice(memo: false, corpusUsable: true), step.NoticeLabel.Text);
            });
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public async Task OnEnter_LeftBeforeCorpusDetectionCompletes_DiscardsStaleResult()
    {
        var folder = Path.Combine(Path.GetTempPath(), "rd-discovery-corpus-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var draft = await DiscoveryCorpusFixture.WithWrittenCorpusAsync(folder);
            GenerateCorpusStepTests.RunSta(() =>
            {
                using var step = new StepDiscoveryDocuments();
                step.BindState(new WizardState { Case = draft, OpenAIApiKey = "sk-step-test", OpenAIKeyTested = true });
                step.KeyReady = () => true;
                step.SaveCase = () => Task.FromResult(true);

                var enter = step.OnEnterStepAsync();
                GenerateCorpusStepTests.Pump(step.OnLeaveStepAsync());
                GenerateCorpusStepTests.Pump(enter);
                Assert.False(step.Model.CorpusUsable);
            });
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    // ----- Inputs -----

    [Fact]
    public void CheckingWitness_SetsUserTouched_AndAddsNotGeneratedRow()
    {
        Run(DiscoveryTestData.LitigationDraft(), (f, step) =>
        {
            Enter(step);
            var free = step.WitnessGrid.Rows.Cast<DataGridViewRow>().First(r => r.Cells["Check"].Value is not true).Index;

            step.SetWitnessChecked(free, true);

            var settings = f.Draft.Discovery!;
            Assert.True(settings.UserTouched);
            Assert.Equal(3, settings.WitnessIds.Count);
            Assert.Contains((string)step.WitnessGrid.Rows[free].Tag!, settings.WitnessIds);
            Assert.Equal(6, step.DocumentsGrid.Rows.Count);
            Assert.All(Statuses(step), s => Assert.Equal("Not generated", s));
            Assert.Equal(DiscoveryUiText.SelectionHint(3), step.SelectionHintLabel.Text);
            Assert.True(f.Saves >= 1);
        });
    }

    [Fact]
    public void SeventhWitness_IsRefused_WithWarningCapHint()
    {
        Run(SevenWitnessDraft(), (f, step) =>
        {
            Enter(step);
            Assert.True(step.WitnessGrid.Rows.Count >= 7);
            var rows = step.WitnessGrid.Rows.Cast<DataGridViewRow>().ToList();
            foreach (var row in rows.Where(r => r.Cells["Check"].Value is not true).Take(4))
                step.SetWitnessChecked(row.Index, true);
            Assert.Equal(6, f.Draft.Discovery!.WitnessIds.Count);

            var seventh = rows.First(r => r.Cells["Check"].Value is not true).Index;
            step.SetWitnessChecked(seventh, true);

            Assert.False(step.WitnessGrid.Rows[seventh].Cells["Check"].Value is true);
            Assert.Equal(6, f.Draft.Discovery.WitnessIds.Count);
            Assert.Equal(DiscoveryUiText.CapHint, step.SelectionHintLabel.Text);
            Assert.Equal(Theme.Warning, step.SelectionHintLabel.ForeColor);
        });
    }

    [Fact]
    public void Pages_OutOfRange_IsInvalidAndKeepsLastValidValue()
    {
        Run(DiscoveryTestData.LitigationDraft(), (f, step) =>
        {
            Enter(step);

            step.PagesText.Text = "5";
            step.CommitPendingInput();
            Assert.Equal(Theme.Danger, step.PagesHost.BorderColor);
            Assert.Equal("Enter a number from 10 to 60.", step.PagesHintLabel.Text);
            Assert.Equal(Theme.Danger, step.PagesHintLabel.ForeColor);
            Assert.False(step.RunButton.Enabled);
            Assert.Equal(25, f.Draft.Discovery!.PagesPerExcerpt);

            step.PagesText.Text = "30";
            step.CommitPendingInput();
            Assert.Equal(Theme.BorderStrong, step.PagesHost.BorderColor);
            Assert.Equal(DiscoveryUiText.PagesHintValid, step.PagesHintLabel.Text);
            Assert.Equal(30, f.Draft.Discovery.PagesPerExcerpt);
            Assert.True(f.Draft.Discovery.UserTouched);
            Assert.True(step.RunButton.Enabled);
        });
    }

    [Fact]
    public void Count_OutOfRange_ShowsDangerHint_AndUncheckingClearsIt()
    {
        Run(DiscoveryTestData.LitigationDraft(), (f, step) =>
        {
            Enter(step);

            step.RfpText.Text = "99";
            step.CommitPendingInput();
            Assert.Equal(Theme.Danger, step.RfpHost.BorderColor);
            Assert.Equal("Enter 5 to 80 requests for production.", step.CountsHintLabel.Text);
            Assert.Equal(Theme.Danger, step.CountsHintLabel.ForeColor);
            Assert.False(step.RunButton.Enabled);
            Assert.Equal(25, f.Draft.Discovery!.RfpCount);

            // An unchecked set's field is disabled and no longer blocks Generate Documents
            step.RfpCheck.Checked = false;
            Assert.False(f.Draft.Discovery.RfpEnabled);
            Assert.False(step.RfpText.Enabled);
            Assert.Equal(Theme.TextMuted, step.CountsHintLabel.ForeColor);
            Assert.True(step.RunButton.Enabled);
        });
    }

    [Fact]
    public void SameParty_ShowsDangerHint_AndDisablesRun()
    {
        Run(DiscoveryTestData.LitigationDraft(), (f, step) =>
        {
            Enter(step);
            step.ServedOnCombo.SelectedIndex = step.ServedByCombo.SelectedIndex;

            Assert.Equal(f.Draft.Discovery!.PropoundingPartyId, f.Draft.Discovery.RespondingPartyId);
            Assert.True(f.Draft.Discovery.UserTouched);
            Assert.Equal("Choose two different parties.", step.PartyHintLabel.Text);
            Assert.Equal(Theme.Danger, step.PartyHintLabel.ForeColor);
            Assert.False(step.RunButton.Enabled);
        });
    }

    [Fact]
    public void AllWrittenSetsUnchecked_DisablesPartyPickers()
    {
        Run(DiscoveryTestData.LitigationDraft(), (f, step) =>
        {
            Enter(step);
            step.RfpCheck.Checked = false;
            step.RogCheck.Checked = false;
            step.RfaCheck.Checked = false;

            Assert.False(step.ServedByCombo.Enabled);
            Assert.False(step.ServedOnCombo.Enabled);
            Assert.Equal(2, step.DocumentsGrid.Rows.Count);
        });
    }

    // ----- Leaving -----

    [Fact]
    public void ValidateStep_InvalidFields_StillAdvancesAndSaves()
    {
        Run(DiscoveryTestData.LitigationDraft(), (f, step) =>
        {
            Enter(step);
            step.PagesText.Text = "5";
            var saves = f.Saves;

            Assert.True(PumpResult(step.ValidateStepAsync()));
            Assert.True(f.Saves > saves);
            Assert.Equal(25, f.Draft.Discovery!.PagesPerExcerpt);
        });
    }

    [Fact]
    public void ValidateStep_SaveFails_ShowsDangerStatus_AndStillAdvances()
    {
        Run(DiscoveryTestData.LitigationDraft(), (f, step) =>
        {
            Enter(step);
            step.SaveCase = () => Task.FromResult(false);

            Assert.True(PumpResult(step.ValidateStepAsync()));
            Assert.Equal(Theme.Danger, step.StatusLabel.ForeColor);
            Assert.StartsWith("Not saved", step.StatusLabel.Text);
        });
    }

    [Fact]
    public void ConfirmLeave_IdleTrue_RunningAsksAndCancelsOnYes()
    {
        Run(DiscoveryTestData.LitigationDraft(), (f, step) =>
        {
            Enter(step);
            Assert.True(step.ConfirmLeave());
            Assert.Empty(f.Confirms);

            using var cts = new CancellationTokenSource();
            step.EnterRunState(cts);
            f.ConfirmAnswer = false;
            Assert.False(step.ConfirmLeave());
            Assert.False(cts.IsCancellationRequested);

            f.ConfirmAnswer = true;
            Assert.True(step.ConfirmLeave());
            Assert.True(cts.IsCancellationRequested);
            Assert.Equal(new[] { "Cancel Generation", "Cancel Generation" }, f.Confirms);
            Assert.Equal(DiscoveryUiText.StatusStopping, step.StatusLabel.Text);
        });
    }

    [Fact]
    public void CancelAndWaitForRun_CancelsAndWaitsUntilTheRunUnwinds()
    {
        Run(DiscoveryTestData.LitigationDraft(), (f, step) =>
        {
            Enter(step);
            using var cts = new CancellationTokenSource();
            var context = SynchronizationContext.Current!;
            // The fake run unwinds on the UI thread once cancelled, like the real run's finally block
            cts.Token.Register(() => context.Post(_ => step.ExitRunState(), null));
            step.EnterRunState(cts);

            step.CancelAndWaitForRun(TimeSpan.FromSeconds(10));

            Assert.True(cts.IsCancellationRequested);
            Assert.False(step.IsGenerating);
            Assert.True(step.CanMoveNext);
        });
    }

    private static bool PumpResult(Task<bool> task)
    {
        GenerateCorpusStepTests.Pump(task);
        return task.Result;
    }
}
