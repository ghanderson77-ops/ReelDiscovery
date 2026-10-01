using System.Globalization;
using ReelDiscovery.Helpers;
using ReelDiscovery.Models;
using ReelDiscovery.Models.Case;
using ReelDiscovery.Services.Case;

namespace ReelDiscovery.UserControls;

/// <summary>
/// Case scenario step (UI-SPEC S1): free-text scenario, complexity preset, optional
/// document/court preferences and the real-name pre-flight check on Next.
/// </summary>
public class StepCaseScenario : UserControl, IWizardStep
{
    private const string AutoState = "Auto";

    private WizardState _state = null!;
    private TextBox _txtScenario = null!;
    private Label _lblCounter = null!;
    private RadioButton _rbSimple = null!;
    private RadioButton _rbStandard = null!;
    private RadioButton _rbComplex = null!;
    private Label _lblPresetSummary = null!;
    private ComboBox _cmbDocument = null!;
    private ComboBox _cmbCourt = null!;
    private ComboBox _cmbState = null!;
    private Label _lblStatus = null!;
    private LoadingOverlay _loadingOverlay = null!;
    private bool _isChecking;

    public string StepTitle => "Describe the Case";
    public bool CanMoveNext => !string.IsNullOrWhiteSpace(_txtScenario?.Text) && !_isChecking;
    public bool CanMoveBack => !_isChecking;
    public string NextButtonText => "Next >";

    public event EventHandler? StateChanged;

    public StepCaseScenario()
    {
        InitializeUI();
    }

    private void InitializeUI()
    {
        this.Padding = new Padding(20);

        var mainLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 9,
            Padding = new Padding(8)
        };

        mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));  // 0: Scenario label
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));  // 1: Scenario input
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));  // 2: Hint + counter
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));  // 3: Complexity label
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));  // 4: Complexity radios
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));  // 5: Preset summary
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));  // 6: Document and court label
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));  // 7: Document/court/state combos
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));  // 8: Status

        mainLayout.Controls.Add(CreateCaptionLabel("Describe the dispute or investigation"), 0, 0);

        // Scenario input
        _txtScenario = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ScrollBars = ScrollBars.Vertical,
            WordWrap = true,
            AcceptsReturn = true,
            // Default 32,767 would silently truncate pastes; cap enforced in validation
            MaxLength = 0,
            Font = new Font("Segoe UI", 10F)
        };
        _txtScenario.TextChanged += (s, e) =>
        {
            UpdateCounter();
            StateChanged?.Invoke(this, EventArgs.Empty);
        };
        mainLayout.Controls.Add(_txtScenario, 0, 1);

        // Hint + counter
        var hintLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Margin = new Padding(0)
        };
        hintLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        hintLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        var lblHint = new Label
        {
            Text = "Name the parties, what went wrong, and roughly when. Real names will be checked and replaced.",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            Font = new Font("Segoe UI", 9F),
            ForeColor = Color.DimGray
        };
        _lblCounter = new Label
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleRight,
            Font = new Font("Segoe UI", 9F),
            ForeColor = Color.DimGray
        };
        hintLayout.Controls.Add(lblHint, 0, 0);
        hintLayout.Controls.Add(_lblCounter, 1, 0);
        mainLayout.Controls.Add(hintLayout, 0, 2);

        mainLayout.Controls.Add(CreateCaptionLabel("Complexity"), 0, 3);

        // Preset summary (created before the radios: their CheckedChanged updates it)
        _lblPresetSummary = new Label
        {
            AutoSize = false,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.TopLeft,
            Font = new Font("Segoe UI", 9F),
            Text = ComplexityPreset.Get(ComplexityLevel.Standard).Summary
        };
        mainLayout.Controls.Add(_lblPresetSummary, 0, 5);

        // Complexity radios
        var complexityPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false
        };
        _rbSimple = CreateRadio("Simple");
        _rbStandard = CreateRadio("Standard");
        _rbComplex = CreateRadio("Complex");
        // Check the default only after all radios exist (CheckedChanged reads SelectedLevel)
        _rbStandard.Checked = true;
        complexityPanel.Controls.Add(_rbSimple);
        complexityPanel.Controls.Add(_rbStandard);
        complexityPanel.Controls.Add(_rbComplex);
        mainLayout.Controls.Add(complexityPanel, 0, 4);

        mainLayout.Controls.Add(CreateCaptionLabel("Document and court (optional)"), 0, 6);

        // Document / court / state combos
        var preferencePanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false
        };
        _cmbDocument = CreateComboBox(180);
        _cmbDocument.Items.AddRange(new object[] { "Auto-detect", "Complaint", "Petition", "Investigation Memo" });
        _cmbDocument.SelectedIndex = 0;
        _cmbDocument.Margin = new Padding(0, 4, 16, 0);

        _cmbCourt = CreateComboBox(120);
        _cmbCourt.Items.AddRange(new object[] { "Auto", "Federal", "State" });
        _cmbCourt.SelectedIndex = 0;
        _cmbCourt.Margin = new Padding(0, 4, 16, 0);
        _cmbCourt.SelectedIndexChanged += (s, e) => UpdateStateComboEnabled();

        _cmbState = CreateComboBox(160);
        _cmbState.Items.Add(AutoState);
        _cmbState.Items.AddRange(Jurisdiction.UsStates.Cast<object>().ToArray());
        _cmbState.SelectedIndex = 0;
        _cmbState.Margin = new Padding(0, 4, 16, 0);
        _cmbState.Enabled = false;

        preferencePanel.Controls.Add(CreateComboCaption("Document:"));
        preferencePanel.Controls.Add(_cmbDocument);
        preferencePanel.Controls.Add(CreateComboCaption("Court:"));
        preferencePanel.Controls.Add(_cmbCourt);
        preferencePanel.Controls.Add(CreateComboCaption("State:"));
        preferencePanel.Controls.Add(_cmbState);
        mainLayout.Controls.Add(preferencePanel, 0, 7);

        // Status
        _lblStatus = new Label
        {
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            Font = new Font("Segoe UI", 9F)
        };
        mainLayout.Controls.Add(_lblStatus, 0, 8);

        _loadingOverlay = new LoadingOverlay(LoadingOverlay.LoadingType.EntityCheck);

        this.Controls.Add(mainLayout);
        UpdateCounter();
    }

    private Label CreateCaptionLabel(string text) => new()
    {
        Text = text,
        Dock = DockStyle.Fill,
        TextAlign = ContentAlignment.BottomLeft,
        Font = new Font(this.Font, FontStyle.Bold)
    };

    private static Label CreateComboCaption(string text) => new()
    {
        Text = text,
        AutoSize = true,
        Font = new Font("Segoe UI", 9F),
        Margin = new Padding(0, 8, 4, 0)
    };

    private RadioButton CreateRadio(string text)
    {
        var radio = new RadioButton
        {
            Text = text,
            AutoSize = true,
            Font = new Font("Segoe UI", 9F),
            Margin = new Padding(0, 4, 16, 0)
        };
        radio.CheckedChanged += (s, e) =>
        {
            if (radio.Checked)
            {
                _lblPresetSummary.Text = ComplexityPreset.Get(SelectedLevel()).Summary;
            }
        };
        return radio;
    }

    private static ComboBox CreateComboBox(int width) => new()
    {
        Width = width,
        DropDownStyle = ComboBoxStyle.DropDownList,
        Font = new Font("Segoe UI", 10F)
    };

    /// <summary>
    /// Counter text against the 8,000-character scenario cap, e.g. "1,234 / 8,000 characters".
    /// </summary>
    public static string CounterText(int length) =>
        $"{length.ToString("N0", CultureInfo.InvariantCulture)} / 8,000 characters";

    // 7,200 = 90% of the cap
    public static Color CounterColor(int length) =>
        length > CaseDraft.MaxScenarioLength ? Color.Red : length > 7_200 ? Color.DarkOrange : Color.DimGray;

    private void UpdateCounter()
    {
        var length = _txtScenario.Text.Length;
        _lblCounter.Text = CounterText(length);
        _lblCounter.ForeColor = CounterColor(length);
    }

    private void UpdateStateComboEnabled()
    {
        _cmbState.Enabled = !_isChecking && _cmbCourt.SelectedIndex == (int)CourtPreference.State;
    }

    private ComplexityLevel SelectedLevel() =>
        _rbSimple.Checked ? ComplexityLevel.Simple
        : _rbComplex.Checked ? ComplexityLevel.Complex
        : ComplexityLevel.Standard;

    private void SetInputsEnabled(bool enabled)
    {
        _txtScenario.ReadOnly = !enabled;
        _rbSimple.Enabled = enabled;
        _rbStandard.Enabled = enabled;
        _rbComplex.Enabled = enabled;
        _cmbDocument.Enabled = enabled;
        _cmbCourt.Enabled = enabled;
        UpdateStateComboEnabled();
    }

    public void BindState(WizardState state)
    {
        _state = state;
    }

    public Task OnEnterStepAsync()
    {
        var draft = _state.Case;

        _txtScenario.Text = draft.Scenario;

        switch (draft.Complexity)
        {
            case ComplexityLevel.Simple: _rbSimple.Checked = true; break;
            case ComplexityLevel.Complex: _rbComplex.Checked = true; break;
            default: _rbStandard.Checked = true; break;
        }

        _cmbDocument.SelectedIndex = (int)draft.DocumentPreference;
        _cmbCourt.SelectedIndex = (int)draft.CourtPreference;
        var stateIndex = string.IsNullOrEmpty(draft.StatePreference) ? -1 : _cmbState.Items.IndexOf(draft.StatePreference);
        _cmbState.SelectedIndex = stateIndex >= 0 ? stateIndex : 0;
        UpdateStateComboEnabled();

        _lblPresetSummary.Text = ComplexityPreset.Get(SelectedLevel()).Summary;
        UpdateCounter();
        _txtScenario.Focus();

        return Task.CompletedTask;
    }

    public Task OnLeaveStepAsync()
    {
        // Keep unsubmitted input when the user goes Back (CR-01). No validation or entity check here:
        // EffectiveScenario and EntityCheckHash are untouched, so a changed text is re-checked on the next Next.
        if (_state != null)
            StoreInputs(_state.Case, _txtScenario.Text, SelectedLevel(), _cmbDocument.SelectedIndex, _cmbCourt.SelectedIndex, _cmbState.SelectedItem as string);

        return Task.CompletedTask;
    }

    /// <summary>
    /// Copies the raw scenario inputs into the draft. Never touches EffectiveScenario, Substitutions or EntityCheckHash.
    /// </summary>
    public static void StoreInputs(CaseDraft draft, string? scenario, ComplexityLevel level, int documentIndex, int courtIndex, string? selectedState)
    {
        draft.Scenario = scenario?.Trim() ?? string.Empty;
        draft.Complexity = level;
        draft.DocumentPreference = (DocumentTypePreference)Math.Max(0, documentIndex);
        draft.CourtPreference = (CourtPreference)Math.Max(0, courtIndex);
        var state = selectedState ?? AutoState;
        draft.StatePreference = draft.CourtPreference == CourtPreference.State && state != AutoState
            ? state
            : string.Empty;
    }

    public async Task<bool> ValidateStepAsync()
    {
        var text = _txtScenario.Text.Trim();
        var error = CaseDraft.ValidateScenario(text);
        if (error != null)
        {
            MessageBox.Show(error, "Validation", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }

        var draft = _state.Case;
        StoreInputs(draft, text, SelectedLevel(), _cmbDocument.SelectedIndex, _cmbCourt.SelectedIndex, _cmbState.SelectedItem as string);

        // Unchanged scenario reuses the previous decisions with no LLM call (T-02-28)
        var hash = CaseDraft.ComputeHash(text);
        if (hash != draft.EntityCheckHash)
        {
            _isChecking = true;
            SetInputsEnabled(false);
            _lblStatus.Text = "Checking the scenario for real names...";
            _lblStatus.ForeColor = Color.FromArgb(0, 120, 215);
            _loadingOverlay.Show(_txtScenario);
            StateChanged?.Invoke(this, EventArgs.Empty);

            try
            {
                var entities = await new RealEntityDetector(_state.CreateGateway()).DetectAsync(text);

                // Hide the overlay before the modal dialog opens
                _loadingOverlay.Hide();

                if (entities.Count > 0)
                {
                    using var dlg = new EntityReviewDialog(entities);
                    if (dlg.ShowDialog(this) != DialogResult.OK)
                    {
                        _lblStatus.Text = "";
                        return false;
                    }
                    draft.Substitutions = dlg.Entities;
                    _lblStatus.Text = "";
                }
                else
                {
                    draft.Substitutions = new();
                    _lblStatus.Text = "No real names found.";
                    _lblStatus.ForeColor = Color.Green;
                }

                draft.EntityCheckHash = hash;
            }
            catch (Exception ex)
            {
                var message = ex.InnerException != null
                    ? $"{ex.Message} ({ex.InnerException.Message})"
                    : ex.Message;
                _lblStatus.Text = $"Could not check the scenario for real names: {message}. Click Next to try again.";
                _lblStatus.ForeColor = Color.Red;
                return false;
            }
            finally
            {
                _isChecking = false;
                SetInputsEnabled(true);
                _loadingOverlay.Hide();
                StateChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        draft.EffectiveScenario = NameSubstitution.Apply(text, draft.Substitutions);
        return true;
    }
}
