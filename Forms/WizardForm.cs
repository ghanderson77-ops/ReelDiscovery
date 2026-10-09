using System.Reflection;
using ReelDiscovery.Helpers;
using ReelDiscovery.Models;
using ReelDiscovery.Models.Case;
using ReelDiscovery.Services.Case;
using ReelDiscovery.UserControls;

namespace ReelDiscovery.Forms;

public partial class WizardForm : Form
{
    private readonly WizardState _state;
    private readonly List<UserControl> _steps = new();
    private int _currentStepIndex = 0;
    private bool _closeConfirmed;

    private Panel _contentPanel = null!;
    private Panel _navigationPanel = null!;
    private Panel _stepIndicatorPanel = null!;
    private Panel _footerPanel = null!;
    private Button _btnBack = null!;
    private Button _btnNext = null!;
    private Button _btnCancel = null!;
    private Label _lblStepTitle = null!;
    private Label _lblCostTracker = null!;
    private System.Windows.Forms.Timer _costUpdateTimer = null!;
    // D-16: cost breakdown tooltip on the header ticker, refreshed by the 1 s timer only when the text changes
    private readonly ToolTip _costToolTip = new();
    private string _lastCostTip = string.Empty;

    public WizardForm()
        : this(new WizardState(), forRender: false)
    {
    }

    private WizardForm(WizardState state, bool forRender)
    {
        _state = state;
        InitializeComponent();
        InitializeWizardUI();
        LoadSteps(showFirstStep: !forRender);
        InitializeCostTracker();
        if (!forRender)
            LoadIcon();
    }

    /// <summary>
    /// Test-only: a wizard over the given state with its steps built but none entered,
    /// so no saved API keys are loaded. Closing it never prompts.
    /// </summary>
    internal static WizardForm CreateForRender(WizardState state)
    {
        var form = new WizardForm(state, forRender: true);
        form._currentStepIndex = -1;
        // Render tests close the form programmatically; a prompt would block the test thread
        form._closeConfirmed = true;
        return form;
    }

    /// <summary>
    /// Test-only: shows a step without validating the current one.
    /// </summary>
    internal void ShowStepForRender(int index) => ShowStep(index);

    // The Case Guide needs room for its grid, detail card and Checks list (03-16). The header band, nav bar and
    // footer take about 48 x 184 logical, so a 1150 x 900 window client gives the step about 1100 x 716
    internal static readonly Size PreferredClientSize = new(1150, 900);
    internal static readonly Size MinimumClientSize = new(1000, 700);

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        ApplyWindowSize();
    }

    /// <summary>
    /// Sizes the window from logical units at the current DPI, clamped to the screen's working area.
    /// </summary>
    private void ApplyWindowSize()
    {
        var area = Screen.FromControl(this).WorkingArea;
        var chrome = Size - ClientSize;
        Size Clamp(Size logicalClient) => new(
            Math.Min(area.Width, LogicalToDeviceUnits(logicalClient.Width) + chrome.Width),
            Math.Min(area.Height, LogicalToDeviceUnits(logicalClient.Height) + chrome.Height));

        MinimumSize = Clamp(MinimumClientSize);
        Size = Clamp(PreferredClientSize);
        if (StartPosition == FormStartPosition.CenterScreen)
        {
            Location = new Point(area.Left + (area.Width - Width) / 2, area.Top + (area.Height - Height) / 2);
        }
    }

    private void LoadIcon()
    {
        try
        {
            var assembly = Assembly.GetExecutingAssembly();
            var resourceName = "ReelDiscovery.Resources.QD_Logo_Color_236x256.ico";
            using var stream = assembly.GetManifestResourceStream(resourceName);
            if (stream != null)
            {
                this.Icon = new Icon(stream);
            }
        }
        catch
        {
            // Icon loading failed, use default
        }
    }

    private void InitializeCostTracker()
    {
        _costUpdateTimer = new System.Windows.Forms.Timer
        {
            Interval = 1000 // Update every second
        };
        _costUpdateTimer.Tick += (s, e) => UpdateCostDisplay();
        _costUpdateTimer.Start();
    }

    private void UpdateCostDisplay()
    {
        var tracker = _state.UsageTracker;
        var totalTokens = tracker.TotalInputTokens + tracker.TotalOutputTokens;
        if (totalTokens > 0)
        {
            _lblCostTracker.Text = $"Tokens: {totalTokens:N0} | Cost: ${tracker.TotalCost:F4}";
        }
        else
        {
            _lblCostTracker.Text = "API Cost: $0.0000";
        }

        var tip = GenerationUiText.CostBreakdown(tracker.TotalCost, tracker.TotalInputCost, tracker.TotalCacheWriteCost,
            tracker.TotalCacheReadCost, tracker.TotalOutputCost, tracker.TotalBatchSavings, tracker.TotalMediaCost);
        if (tip != _lastCostTip)
        {
            _costToolTip.SetToolTip(_lblCostTracker, tip);
            _lastCostTip = tip;
        }
    }

    // Test accessors: the ticker tooltip after a forced refresh (no timer wait)
    internal string CostTooltipForTest
    {
        get
        {
            UpdateCostDisplay();
            return _costToolTip.GetToolTip(_lblCostTracker) ?? "";
        }
    }

    private void InitializeComponent()
    {
        this.SuspendLayout();
        this.AutoScaleDimensions = new SizeF(7F, 15F);
        this.AutoScaleMode = AutoScaleMode.Font;
        this.ClientSize = new Size(900, 600);
        this.MinimumSize = new Size(800, 500);
        this.Name = "WizardForm";
        this.Text = "Reel Discovery - E-Discovery Dataset Generator";
        this.StartPosition = FormStartPosition.CenterScreen;
        this.ResumeLayout(false);
    }

    private void InitializeWizardUI()
    {
        // Step indicator panel at top
        var margin = LogicalToDeviceUnits(Theme.PageMargin);
        _stepIndicatorPanel = new Panel
        {
            Dock = DockStyle.Top,
            Height = LogicalToDeviceUnits(64),
            BackColor = Color.FromArgb(45, 45, 48),
            Padding = new Padding(margin, LogicalToDeviceUnits(12), margin, LogicalToDeviceUnits(12))
        };

        _lblStepTitle = new Label
        {
            Dock = DockStyle.Fill,
            ForeColor = Color.White,
            Font = new Font("Segoe UI", 14F, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleLeft,
            Name = "lblStepTitle"
        };
        _stepIndicatorPanel.Controls.Add(_lblStepTitle);

        // Cost tracker panel on the right side - more prominent
        var costPanel = new Panel
        {
            Dock = DockStyle.Right,
            Width = LogicalToDeviceUnits(320),
            BackColor = Color.FromArgb(60, 60, 65),
            Padding = new Padding(10, 5, 10, 5)
        };

        _lblCostTracker = new Label
        {
            Dock = DockStyle.Fill,
            ForeColor = Color.FromArgb(100, 200, 100),
            Font = new Font("Segoe UI", 9.5F, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleCenter,
            Text = "API Cost: $0.0000"
        };
        costPanel.Controls.Add(_lblCostTracker);
        _stepIndicatorPanel.Controls.Add(costPanel);

        // Promotional footer at very bottom
        _footerPanel = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = 30,
            BackColor = Color.FromArgb(45, 45, 48)
        };

        var lblFooter = new LinkLabel
        {
            Text = "Provided for free distribution by QuikData  |  Visit www.quikdata.com for more information",
            Dock = DockStyle.Fill,
            ForeColor = Color.FromArgb(180, 180, 180),
            LinkColor = Color.FromArgb(100, 180, 255),
            ActiveLinkColor = Color.FromArgb(150, 200, 255),
            Font = new Font("Segoe UI", 8.5F),
            TextAlign = ContentAlignment.MiddleCenter
        };
        lblFooter.Links.Add(53, 16, "https://www.quikdata.com");
        lblFooter.LinkClicked += (s, e) =>
        {
            if (e.Link?.LinkData is string url)
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = url,
                    UseShellExecute = true
                });
            }
        };
        _footerPanel.Controls.Add(lblFooter);

        // Navigation bar: Cancel on the left, Back / Next on the right, on the same page margin as the content
        var navigation = new TableLayoutPanel
        {
            Dock = DockStyle.Bottom,
            ColumnCount = 4,
            RowCount = 1,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            BackColor = Theme.Background,
            Padding = new Padding(margin, LogicalToDeviceUnits(Theme.Gap), margin, LogicalToDeviceUnits(Theme.Gap))
        };
        navigation.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));     // 0: Cancel
        navigation.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); // 1: Spacer
        navigation.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));     // 2: Back
        navigation.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));     // 3: Next
        _navigationPanel = navigation;

        var buttonWidth = LogicalToDeviceUnits(96);
        var buttonHeight = LogicalToDeviceUnits(34);
        _btnCancel = ButtonHelper.CreateButton("Cancel", buttonWidth, buttonHeight, ButtonStyle.Default);
        _btnCancel.Margin = new Padding(0);
        _btnCancel.Name = "btnCancel";
        _btnCancel.Click += BtnCancel_Click;

        _btnBack = ButtonHelper.CreateButton("< Back", buttonWidth, buttonHeight, ButtonStyle.Default);
        _btnBack.Margin = new Padding(0, 0, LogicalToDeviceUnits(Theme.SmallGap), 0);
        _btnBack.Name = "btnBack";
        _btnBack.Click += BtnBack_Click;

        _btnNext = ButtonHelper.CreateButton("Next >", buttonWidth, buttonHeight, ButtonStyle.Primary);
        _btnNext.Margin = new Padding(0);
        _btnNext.Name = "btnNext";
        _btnNext.Click += BtnNext_Click;

        navigation.Controls.Add(_btnCancel, 0, 0);
        navigation.Controls.Add(_btnBack, 2, 0);
        navigation.Controls.Add(_btnNext, 3, 0);

        // Content panel in center: steps sit on the same page margin as the navigation bar
        _contentPanel = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Theme.Background,
            Padding = new Padding(margin, margin, margin, 0)
        };

        // Add panels in correct docking order:
        // For Bottom dock: LAST added ends up at the very bottom
        // For Top dock: LAST added ends up at the very top
        // Fill should be added LAST to take remaining space
        this.Controls.Add(_contentPanel);      // Fill - takes remaining space
        this.Controls.Add(_stepIndicatorPanel); // Top - header
        this.Controls.Add(_navigationPanel);   // Bottom - buttons (added before footer, so above it)
        this.Controls.Add(_footerPanel);       // Bottom - at very bottom (added last)
    }

    /// <summary>
    /// The one case-driven step list (D-21): API Configuration, then Case Scenario.
    /// </summary>
    internal static IReadOnlyList<Type> StepTypes { get; } = new[]
    {
        typeof(StepApiConfiguration), typeof(StepCaseScenario), typeof(StepCoreDocument), typeof(StepCaseGuide),
        typeof(StepCorpusSettings), typeof(StepReviewPlan), typeof(StepGenerateCorpus), typeof(StepDiscoveryDocuments),
        typeof(StepCaseSummary)
    };

    /// <summary>
    /// The step that shows a case at the given stage. Unknown values (a hand-edited case.json) open the scenario (T-03-60).
    /// </summary>
    // D-09: typeof(StepDiscoveryDocuments) has no CaseStage; it is reached by Next or from Case Summary
    internal static Type StepTypeForStage(CaseStage stage) => stage switch
    {
        CaseStage.CoreDocument => typeof(StepCoreDocument),
        CaseStage.CaseGuide => typeof(StepCaseGuide),
        CaseStage.CorpusSettings => typeof(StepCorpusSettings),
        CaseStage.ReviewPlan => typeof(StepReviewPlan),
        CaseStage.Generation => typeof(StepGenerateCorpus),
        CaseStage.Summary => typeof(StepCaseSummary),
        _ => typeof(StepCaseScenario)
    };

    /// <summary>
    /// Opens the step for a case stage (D-11). Does nothing for the API step.
    /// </summary>
    public void NavigateToStage(CaseStage stage)
    {
        // Map by step type, never by index: Phase 4 inserts steps before the summary
        var type = StepTypeForStage(stage);
        var index = _steps.FindIndex(s => s.GetType() == type);
        if (index > 0)
            ShowStep(index);
    }

    private void LoadSteps(bool showFirstStep)
    {
        foreach (var type in StepTypes)
        {
            var step = (UserControl)Activator.CreateInstance(type)!;
            _steps.Add(step);
            WireStep(step);
        }

        if (showFirstStep)
            ShowStep(0);
    }

    private void WireStep(UserControl step)
    {
        if (step is IWizardStep wizardStep)
        {
            wizardStep.BindState(_state);
            wizardStep.StateChanged += (s, e) => UpdateNavigationButtons();
        }
        step.Dock = DockStyle.Fill;
        step.Visible = false;
        _contentPanel.Controls.Add(step);
    }

    private async void ShowStep(int index)
    {
        if (index < 0 || index >= _steps.Count) return;

        // Leaving a step during a run asks first: Case Summary blind review (C-Q3, 07-UI-SPEC S2 R4), Discovery
        // Documents run (C-D1). No stays on the step
        if (index != _currentStepIndex && _currentStepIndex >= 0 && _currentStepIndex < _steps.Count
            && _steps[_currentStepIndex] is IConfirmLeave leaver && !leaver.ConfirmLeave())
            return;

        // Hide current step
        if (_currentStepIndex >= 0 && _currentStepIndex < _steps.Count)
        {
            var currentStep = _steps[_currentStepIndex];
            if (currentStep is IWizardStep current)
            {
                await current.OnLeaveStepAsync();
            }
            currentStep.Visible = false;
        }

        _currentStepIndex = index;
        var newStep = _steps[index];
        newStep.Visible = true;

        if (newStep is IWizardStep wizard)
        {
            _lblStepTitle.Text = $"Step {index + 1} of {_steps.Count}: {wizard.StepTitle}";
            _btnNext.Text = wizard.NextButtonText;
            _btnBack.Enabled = wizard.CanMoveBack && index > 0;

            await wizard.OnEnterStepAsync();
        }

        UpdateNavigationButtons();
    }

    private void UpdateNavigationButtons()
    {
        // No step shown yet (render-only forms)
        if (_currentStepIndex < 0 || _currentStepIndex >= _steps.Count)
            return;

        _btnBack.Enabled = _currentStepIndex > 0;

        if (_steps[_currentStepIndex] is IWizardStep wizard)
        {
            _btnBack.Enabled = wizard.CanMoveBack && _currentStepIndex > 0;
            _btnNext.Enabled = wizard.CanMoveNext;
        }

        // Last step shows "Finish"
        if (_currentStepIndex == _steps.Count - 1)
        {
            _btnNext.Text = "Finish";
        }
    }

    private async void BtnNext_Click(object? sender, EventArgs e)
    {
        var currentStep = _steps[_currentStepIndex];
        if (currentStep is IWizardStep wizard)
        {
            _btnNext.Enabled = false;
            try
            {
                if (!await wizard.ValidateStepAsync()) return;
            }
            finally
            {
                // The window may have closed while validation awaited an LLM call (WR-07)
                if (!IsDisposed)
                    _btnNext.Enabled = true;
            }

            if (IsDisposed)
                return;
        }

        if (_currentStepIndex >= _steps.Count - 1)
        {
            // Finish: the last step has validated (e.g. citation errors block closing).
            // OnFormClosing asks before discarding the case, the same as for X and Cancel.
            this.Close();
            return;
        }

        ShowStep(_currentStepIndex + 1);
    }

    private void BtnBack_Click(object? sender, EventArgs e)
    {
        if (_currentStepIndex > 0)
        {
            ShowStep(_currentStepIndex - 1);
        }
    }

    private void BtnCancel_Click(object? sender, EventArgs e)
    {
        // OnFormClosing saves and asks only when something would be lost (D-10)
        this.Close();
    }

    public void NavigateToStep(int stepIndex)
    {
        if (stepIndex >= 0 && stepIndex < _steps.Count)
        {
            ShowStep(stepIndex);
        }
    }

    /// <summary>
    /// Opens a step by its type (D-08: Case Summary reaches Discovery Documents). Does nothing for the API step.
    /// </summary>
    public void NavigateToStep(Type stepType)
    {
        // Map by step type, never by index: steps may be inserted later
        var index = _steps.FindIndex(s => s.GetType() == stepType);
        if (index > 0)
            ShowStep(index);
    }

    public void EnableNavigation(bool enabled)
    {
        _btnBack.Enabled = enabled && _currentStepIndex > 0;
        _btnNext.Enabled = enabled;
        _btnCancel.Enabled = enabled;
    }

    internal enum CloseDecision
    {
        CloseNow,
        AskUnsavedScenario,
        AskFailedSave,
        AskDuringRun
    }

    /// <summary>
    /// What closing the window should do after the current step was flushed and the case saved (D-10, C10-C12).
    /// A running generation asks first; a typed scenario with no core document yet asks (it has no folder);
    /// a failed save asks; everything else closes without a prompt.
    /// </summary>
    internal static CloseDecision DecideClose(CaseDraft draft, bool runActive, bool saveSucceeded)
    {
        if (draft == null)
            return CloseDecision.CloseNow;
        if (runActive)
            return CloseDecision.AskDuringRun;

        if (draft.CoreDocument == null && string.IsNullOrEmpty(draft.FolderPath))
            return string.IsNullOrWhiteSpace(draft.Scenario) ? CloseDecision.CloseNow : CloseDecision.AskUnsavedScenario;

        return saveSucceeded ? CloseDecision.CloseNow : CloseDecision.AskFailedSave;
    }

    /// <summary>
    /// Copies unsubmitted input (scenario text, a pending edit) into the draft, then saves case.json synchronously.
    /// Returns false only when a save was attempted and failed.
    /// </summary>
    private bool FlushCurrentCaseStep()
    {
        // Every case step commits its pending edits synchronously before its first await
        if (_currentStepIndex >= 0 && _currentStepIndex < _steps.Count
            && _steps[_currentStepIndex] is (StepCaseScenario or StepCoreDocument or StepCaseGuide or StepCorpusSettings or StepReviewPlan or StepGenerateCorpus
                or StepDiscoveryDocuments or StepCaseSummary)
                and IWizardStep step)
        {
            _ = step.OnLeaveStepAsync();
        }

        if (!CaseStore.CanSave(_state.Case))
            return true;

        return _state.CaseStore.SaveNow(_state.Case);
    }

    private bool IsRunActive()
    {
        if (_currentStepIndex < 0 || _currentStepIndex >= _steps.Count)
            return false;

        return _steps[_currentStepIndex] is StepCoreDocument { IsGenerating: true } or StepCaseGuide { IsGenerating: true }
            or StepReviewPlan { IsPlanning: true } or StepGenerateCorpus { IsGenerating: true }
            or StepDiscoveryDocuments { IsGenerating: true } or StepCaseSummary { IsReviewRunning: true };
    }

    // Disposing the running step cancels its token, so no further calls are billed (WR-07, T-03-61).
    // A guide run is cancelled and awaited first, so units that already came back are merged and saved (WR-01).
    private void StopActiveRun()
    {
        if (_currentStepIndex < 0 || _currentStepIndex >= _steps.Count)
            return;

        var step = _steps[_currentStepIndex];
        if (step is StepCaseGuide guide)
            WaitWhileStopping(() => guide.CancelAndWaitForRun(TimeSpan.FromSeconds(10)));
        else if (step is StepReviewPlan review)
            // A planning run is cancelled and awaited, so no further calls are billed (T-04-61)
            WaitWhileStopping(() => review.CancelAndWaitForRun(TimeSpan.FromSeconds(10)));
        else if (step is StepGenerateCorpus generate)
            // In-flight threads are discarded; finished ones are already journaled and saved (RUN-06)
            WaitWhileStopping(() => generate.CancelAndWaitForRun(TimeSpan.FromSeconds(10)));
        else if (step is StepDiscoveryDocuments discovery)
            // No further discovery calls are billed; finished documents are already saved (D-05)
            WaitWhileStopping(() => discovery.CancelAndWaitForRun(TimeSpan.FromSeconds(10)));
        else if (step is StepCaseSummary summary)
            // Verdicts already paid for are on disk; no further review calls are billed (D-13)
            WaitWhileStopping(() => summary.CancelAndWaitForReview(TimeSpan.FromSeconds(10)));

        step.Dispose();
    }

    private void WaitWhileStopping(Action stop)
    {
        // Disabled while messages are pumped, so no click or second close re-enters the wizard
        var wasEnabled = Enabled;
        UseWaitCursor = true;
        Enabled = false;
        try
        {
            stop();
        }
        finally
        {
            Enabled = wasEnabled;
            UseWaitCursor = false;
        }
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        // Closing never discards a saved case: flush and save, and ask only when work would be lost (D-10, T-03-59)
        if (!_closeConfirmed)
        {
            var runActive = IsRunActive();
            var saveOk = FlushCurrentCaseStep();
            var decision = DecideClose(_state.Case, runActive, saveOk);

            if (e.CloseReason == CloseReason.UserClosing)
            {
                if (decision == CloseDecision.AskDuringRun)
                {
                    // C-P2 ("Stop Planning?") while Review Plan is planning, C-G3 ("Stop Generating?") while Generate
                    // Corpus is writing emails; C12 for the core document and guide runs
                    var (runTitle, runText) = _steps[_currentStepIndex] switch
                    {
                        StepReviewPlan => CorpusUiText.CP2ClosePlanning(),
                        StepGenerateCorpus s => GenerationUiText.CG3CloseDuringRun(s.PendingProviderBatches > 0),
                        // C-Q3: closing the app during a blind review
                        StepCaseSummary => QaUiText.CQ3LeaveDuringReview(),
                        // C-D2: closing the app during a discovery documents run
                        StepDiscoveryDocuments => DiscoveryUiText.CD2CloseDuringRun(),
                        _ => CaseGuideUiText.C12CloseDuringRun()
                    };
                    if (MessageBox.Show(this, runText, runTitle, MessageBoxButtons.YesNo, MessageBoxIcon.Question,
                            MessageBoxDefaultButton.Button2) != DialogResult.Yes)
                    {
                        e.Cancel = true;
                        base.OnFormClosing(e);
                        return;
                    }

                    StopActiveRun();
                    saveOk = !CaseStore.CanSave(_state.Case) || _state.CaseStore.SaveNow(_state.Case);
                    decision = DecideClose(_state.Case, runActive: false, saveOk);
                }

                if (decision == CloseDecision.AskUnsavedScenario)
                {
                    // C11 ("Close Case"): a typed scenario has no folder until its core document exists (D-09)
                    var (title, text) = CaseGuideUiText.C11CloseUnsavedScenario();
                    if (MessageBox.Show(this, text, title, MessageBoxButtons.YesNo, MessageBoxIcon.Warning,
                            MessageBoxDefaultButton.Button2) != DialogResult.Yes)
                    {
                        e.Cancel = true;
                        base.OnFormClosing(e);
                        return;
                    }
                }
                else if (decision == CloseDecision.AskFailedSave)
                {
                    var lastSaved = _state.CaseStore.LastSavedUtc?.ToLocalTime();
                    var (title, text) = CaseGuideUiText.C10CloseAfterFailedSave(_state.CaseStore.LastSaveError, lastSaved);
                    if (MessageBox.Show(this, text, title, MessageBoxButtons.YesNo, MessageBoxIcon.Warning,
                            MessageBoxDefaultButton.Button2) != DialogResult.Yes)
                    {
                        e.Cancel = true;
                        base.OnFormClosing(e);
                        return;
                    }
                }
            }

            _closeConfirmed = true;
        }

        _costUpdateTimer?.Stop();
        _costUpdateTimer?.Dispose();
        _costToolTip.Dispose();
        base.OnFormClosing(e);
    }
}
