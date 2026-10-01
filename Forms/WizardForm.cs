using System.Reflection;
using ReelDiscovery.Helpers;
using ReelDiscovery.Models;
using ReelDiscovery.Models.Case;
using ReelDiscovery.UserControls;

namespace ReelDiscovery.Forms;

public partial class WizardForm : Form
{
    private readonly WizardState _state = new();
    private readonly List<UserControl> _steps = new();
    private int _currentStepIndex = 0;
    private WizardFlow _builtFlow;
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

    public WizardForm()
    {
        InitializeComponent();
        InitializeWizardUI();
        LoadSteps();
        InitializeCostTracker();
        LoadIcon();
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
            TextAlign = ContentAlignment.MiddleLeft
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
        _btnCancel.Click += BtnCancel_Click;

        _btnBack = ButtonHelper.CreateButton("< Back", buttonWidth, buttonHeight, ButtonStyle.Default);
        _btnBack.Margin = new Padding(0, 0, LogicalToDeviceUnits(Theme.SmallGap), 0);
        _btnBack.Click += BtnBack_Click;

        _btnNext = ButtonHelper.CreateButton("Next >", buttonWidth, buttonHeight, ButtonStyle.Primary);
        _btnNext.Margin = new Padding(0);
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
    /// Steps for each flow. Index 0 (API configuration) is shared and never rebuilt.
    /// </summary>
    internal static IReadOnlyList<Type> StepTypesFor(WizardFlow flow) =>
        flow == WizardFlow.CaseDriven
            ? new[] { typeof(StepApiConfiguration), typeof(StepCaseScenario), typeof(StepCoreDocument) }
            : new[]
            {
                typeof(StepApiConfiguration), typeof(StepTopicInput), typeof(StepStorylines), typeof(StepCharacters),
                typeof(StepGenerationConfig), typeof(StepGeneration), typeof(StepComplete)
            };

    private void LoadSteps()
    {
        _steps.Add(new StepApiConfiguration());
        WireStep(_steps[0]);
        BuildSteps(_state.Flow);
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

    private void BuildSteps(WizardFlow flow)
    {
        // Legacy flow stays selectable until Phase 7 (PROJECT.md).
        // Drop every step after the shared API step before creating the new list
        for (var i = _steps.Count - 1; i >= 1; i--)
        {
            _contentPanel.Controls.Remove(_steps[i]);
            _steps[i].Dispose();
            _steps.RemoveAt(i);
        }

        foreach (var type in StepTypesFor(flow).Skip(1))
        {
            var step = (UserControl)Activator.CreateInstance(type)!;
            _steps.Add(step);
            WireStep(step);
        }

        _builtFlow = flow;
    }

    private async void ShowStep(int index)
    {
        if (index < 0 || index >= _steps.Count) return;

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
        _btnBack.Enabled = _currentStepIndex > 0;

        if (_steps[_currentStepIndex] is IWizardStep wizard)
        {
            _btnBack.Enabled = wizard.CanMoveBack && _currentStepIndex > 0;
            _btnNext.Enabled = wizard.CanMoveNext;
        }

        // Last step shows "Finish" (handled by StepComplete)
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

        // Leaving the API step: rebuild the step list when the workflow choice changed
        if (_currentStepIndex == 0 && _state.Flow != _builtFlow)
            BuildSteps(_state.Flow);

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
        // Unsaved case work: OnFormClosing shows the specific discard prompt, so skip the generic one
        FlushCurrentCaseStep();
        if (HasUnsavedCase(_state.Flow, _state.Case))
        {
            this.Close();
            return;
        }

        if (MessageBox.Show("Are you sure you want to cancel?", "Confirm",
            MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
        {
            this.Close();
        }
    }

    public void NavigateToStep(int stepIndex)
    {
        if (stepIndex >= 0 && stepIndex < _steps.Count)
        {
            ShowStep(stepIndex);
        }
    }

    public void EnableNavigation(bool enabled)
    {
        _btnBack.Enabled = enabled && _currentStepIndex > 0;
        _btnNext.Enabled = enabled;
        _btnCancel.Enabled = enabled;
    }

    internal const string DiscardCaseMessage =
        "This version does not save the case yet. Closing will discard the scenario and document. Close anyway?";

    /// <summary>
    /// True when closing would discard case-flow work (a typed scenario or a generated document) (WR-08).
    /// </summary>
    internal static bool HasUnsavedCase(WizardFlow flow, CaseDraft draft) =>
        flow == WizardFlow.CaseDriven && draft != null
        && (draft.CoreDocument != null || !string.IsNullOrWhiteSpace(draft.Scenario));

    // Copies unsubmitted input (scenario text, a pending section edit) into the draft before the discard check
    private void FlushCurrentCaseStep()
    {
        if (_state.Flow != WizardFlow.CaseDriven || _currentStepIndex < 0 || _currentStepIndex >= _steps.Count)
            return;

        // Both case steps complete OnLeaveStepAsync synchronously
        if (_steps[_currentStepIndex] is (StepCaseScenario or StepCoreDocument) and IWizardStep step)
            _ = step.OnLeaveStepAsync();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        // Every close path (Finish, Cancel, window X) asks before discarding the case (WR-08)
        if (e.CloseReason == CloseReason.UserClosing && !_closeConfirmed)
        {
            FlushCurrentCaseStep();
            if (HasUnsavedCase(_state.Flow, _state.Case))
            {
                if (MessageBox.Show(DiscardCaseMessage, "Close Case", MessageBoxButtons.YesNo,
                        MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
                {
                    e.Cancel = true;
                    base.OnFormClosing(e);
                    return;
                }
                _closeConfirmed = true;
            }
        }

        _costUpdateTimer?.Stop();
        _costUpdateTimer?.Dispose();
        base.OnFormClosing(e);
    }
}
