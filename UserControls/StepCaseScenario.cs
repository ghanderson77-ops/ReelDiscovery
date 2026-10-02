using System.Globalization;
using System.Text.RegularExpressions;
using ReelDiscovery.Forms;
using ReelDiscovery.Helpers;
using ReelDiscovery.Models;
using ReelDiscovery.Models.Case;
using ReelDiscovery.Services.Case;

namespace ReelDiscovery.UserControls;

/// <summary>
/// Case scenario step (UI-SPEC S1/S3): free-text scenario, complexity preset, optional
/// document/court preferences, the real-name pre-flight check on Next, and the Recent cases card
/// that reopens saved cases (D-11).
/// </summary>
public class StepCaseScenario : UserControl, IWizardStep
{
    private const string AutoState = "Auto";

    public static readonly string RecentEmptyText = "No saved cases yet. A case is saved automatically once its core document is written.";

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
    private LinkLabel _lnkReviewNames = null!;
    private LinkLabel _lnkCancelCheck = null!;
    private LoadingOverlay _loadingOverlay = null!;
    private ListBox _lstRecent = null!;
    private Label _lblRecentEmpty = null!;
    private Button _btnNewCase = null!;
    private Button _btnOpenCase = null!;
    private ToolTip _recentToolTip = null!;
    private List<RecentCase> _recentCases = new();
    private int _hoverIndex = -1;
    private int _toolTipIndex = -1;
    private CancellationTokenSource? _checkCts;
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
        // The wizard shell supplies the page margin, so content lines up with the navigation bar
        this.Padding = new Padding(0);

        var mainLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 8,
            Padding = new Padding(0)
        };
        mainLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));                        // 0: Scenario
        mainLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, LogicalToDeviceUnits(296))); // 1: Recent cases card

        // Fixed rows size to their content so they grow with DPI scaling and font size
        mainLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));      // 0: Scenario label
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));  // 1: Scenario input + hint | Recent cases card
        mainLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));      // 2: Complexity label
        mainLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));      // 3: Complexity radios
        mainLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));      // 4: Preset summary
        mainLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));      // 5: Document and court label
        mainLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));      // 6: Document/court/state combos
        mainLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));      // 7: Status

        // Scenario card and hint share one cell, so the Recent cases card beside them spans no AutoSize row
        // (a card spanning the hint row left that row at one line when the hint wrapped)
        var scenarioColumn = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Margin = new Padding(0),
            Padding = new Padding(0)
        };
        scenarioColumn.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        scenarioColumn.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); // 0: Scenario input
        scenarioColumn.RowStyles.Add(new RowStyle(SizeType.AutoSize));     // 1: Hint + counter
        mainLayout.Controls.Add(scenarioColumn, 0, 1);

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
            BorderStyle = BorderStyle.None,
            Font = Theme.EditorFont(),
            ForeColor = Theme.Text,
            BackColor = Theme.Surface
        };
        // Borderless editor inside a rounded card, matching the core document editor
        var scenarioCard = new CardPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(LogicalToDeviceUnits(Theme.CardPadding), LogicalToDeviceUnits(12), LogicalToDeviceUnits(6), LogicalToDeviceUnits(12)),
            Margin = new Padding(0)
        };
        scenarioCard.Controls.Add(_txtScenario);
        scenarioColumn.Controls.Add(scenarioCard, 0, 0);

        // Hint + counter
        var hintLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Margin = new Padding(0, 4, 0, 0)
        };
        hintLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        hintLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        var lblHint = new Label
        {
            Text = "Name the parties, what went wrong, and roughly when. Real names will be checked and replaced.",
            AutoSize = true,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            Font = Theme.SmallFont(),
            ForeColor = Theme.TextMuted
        };
        _lblCounter = new Label
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleRight,
            Font = Theme.SmallFont(),
            ForeColor = Theme.TextMuted
        };
        hintLayout.Controls.Add(lblHint, 0, 0);
        hintLayout.Controls.Add(_lblCounter, 1, 0);
        scenarioColumn.Controls.Add(hintLayout, 0, 1);

        // Recent cases card beside the scenario card (UI-SPEC S3): top-aligned with it, ending with the hint row
        var recentCard = CreateRecentCasesCard();
        mainLayout.Controls.Add(recentCard, 1, 1);

        var complexityCaption = CreateCaptionLabel("Complexity");
        mainLayout.Controls.Add(complexityCaption, 0, 2);
        mainLayout.SetColumnSpan(complexityCaption, 2);

        // Preset summary (created before the radios: their CheckedChanged updates it)
        _lblPresetSummary = new Label
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            Margin = new Padding(3, 4, 3, 8),
            TextAlign = ContentAlignment.TopLeft,
            Font = Theme.SmallFont(),
            ForeColor = Theme.TextMuted,
            Text = ComplexityPreset.Get(ComplexityLevel.Standard).Summary
        };
        mainLayout.Controls.Add(_lblPresetSummary, 0, 4);
        mainLayout.SetColumnSpan(_lblPresetSummary, 2);

        // Complexity radios
        var complexityPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink
        };
        _rbSimple = CreateRadio("Simple");
        _rbStandard = CreateRadio("Standard");
        _rbComplex = CreateRadio("Complex");
        // Check the default only after all radios exist (CheckedChanged reads SelectedLevel)
        _rbStandard.Checked = true;
        complexityPanel.Controls.Add(_rbSimple);
        complexityPanel.Controls.Add(_rbStandard);
        complexityPanel.Controls.Add(_rbComplex);
        mainLayout.Controls.Add(complexityPanel, 0, 3);
        mainLayout.SetColumnSpan(complexityPanel, 2);

        var documentCaption = CreateCaptionLabel("Document and court (optional)");
        mainLayout.Controls.Add(documentCaption, 0, 5);
        mainLayout.SetColumnSpan(documentCaption, 2);

        // Document / court / state combos
        var preferencePanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink
        };
        _cmbDocument = CreateComboBox(180);
        _cmbDocument.Items.AddRange(new object[] { "Auto-detect", "Complaint", "Petition", "Investigation Memo" });
        _cmbDocument.SelectedIndex = 0;
        _cmbDocument.Margin = new Padding(0, 4, 16, 0);

        _cmbCourt = CreateComboBox(120);
        _cmbCourt.Items.AddRange(new object[] { "Auto", "Federal", "State" });
        _cmbCourt.SelectedIndex = 0;
        _cmbCourt.Margin = new Padding(0, 4, 16, 0);

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
        mainLayout.Controls.Add(preferencePanel, 0, 6);
        mainLayout.SetColumnSpan(preferencePanel, 2);

        // Status + "Review real names" and "Cancel check" links
        var statusLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 1,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Margin = new Padding(0, 4, 0, 0)
        };
        statusLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        statusLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        statusLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _lblStatus = new Label
        {
            AutoSize = true,
            MinimumSize = new Size(0, 20),
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            Font = Theme.SmallFont()
        };
        // Reopens the substitution decisions without editing the scenario (WR-06)
        _lnkReviewNames = CreateLink("Review real names");
        // Shown only while the real-name check runs (WR-07)
        _lnkCancelCheck = CreateLink("Cancel check");
        statusLayout.Controls.Add(_lblStatus, 0, 0);
        statusLayout.Controls.Add(_lnkReviewNames, 1, 0);
        statusLayout.Controls.Add(_lnkCancelCheck, 2, 0);
        mainLayout.Controls.Add(statusLayout, 0, 7);
        mainLayout.SetColumnSpan(statusLayout, 2);

        _loadingOverlay = new LoadingOverlay(LoadingOverlay.LoadingType.EntityCheck);
        _recentToolTip = new ToolTip();

        this.Controls.Add(mainLayout);

        // Wire handlers only after every control exists (handlers read other controls)
        _txtScenario.TextChanged += (s, e) =>
        {
            UpdateCounter();
            StateChanged?.Invoke(this, EventArgs.Empty);
        };
        _cmbCourt.SelectedIndexChanged += (s, e) => UpdateStateComboEnabled();
        _lnkReviewNames.LinkClicked += (s, e) => ReviewRealNames();
        _lnkCancelCheck.LinkClicked += (s, e) => CancelCheck();
        _lstRecent.DrawItem += LstRecent_DrawItem;
        _lstRecent.MouseMove += LstRecent_MouseMove;
        _lstRecent.MouseLeave += (s, e) => SetHoverIndex(-1);
        _lstRecent.MouseClick += LstRecent_MouseClick;
        _lstRecent.KeyDown += LstRecent_KeyDown;
        _btnOpenCase.Click += (s, e) => BrowseForCase();
        _btnNewCase.Click += (s, e) => StartNewCase();

        UpdateCounter();
    }

    private CardPanel CreateRecentCasesCard()
    {
        var inset = LogicalToDeviceUnits(15);
        var recentCard = new CardPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(1),
            Margin = new Padding(LogicalToDeviceUnits(Theme.Gap), 0, 0, 0)
        };
        var recentLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, BackColor = Theme.Surface, Margin = new Padding(0) };
        recentLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        recentLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));                          // 0: Card header
        recentLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));                      // 1: Recent list or empty label
        recentLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, LogicalToDeviceUnits(1))); // 2: Divider
        recentLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));                          // 3: Footer buttons

        var header = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 1,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(inset, LogicalToDeviceUnits(14), inset, LogicalToDeviceUnits(10)),
            Margin = new Padding(0)
        };
        header.Controls.Add(new Label { Text = "Recent cases", AutoSize = true, Font = Theme.LabelFont(), ForeColor = Theme.Text, Margin = new Padding(0) }, 0, 0);
        recentLayout.Controls.Add(header, 0, 0);

        // Row 1 hosts either the list or the empty-state label
        var listHost = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Surface, Margin = new Padding(0, 0, 0, LogicalToDeviceUnits(6)) };
        _lstRecent = new ListBox
        {
            Dock = DockStyle.Fill,
            DrawMode = DrawMode.OwnerDrawFixed,
            ItemHeight = LogicalToDeviceUnits(52),
            Font = Theme.BodyFont(),
            BackColor = Theme.Surface,
            ForeColor = Theme.Text,
            BorderStyle = BorderStyle.None,
            IntegralHeight = false,
            Cursor = Cursors.Hand,
            Margin = new Padding(0)
        };
        _lblRecentEmpty = new Label
        {
            Text = RecentEmptyText,
            Dock = DockStyle.Fill,
            AutoSize = false,
            Font = Theme.BodyFont(),
            ForeColor = Theme.TextMuted,
            BackColor = Theme.Surface,
            TextAlign = ContentAlignment.TopLeft,
            Padding = new Padding(inset, 0, inset, inset),
            Visible = false
        };
        listHost.Controls.Add(_lstRecent);
        listHost.Controls.Add(_lblRecentEmpty);
        recentLayout.Controls.Add(listHost, 0, 1);

        recentLayout.Controls.Add(new Panel { Dock = DockStyle.Fill, BackColor = Theme.Border, Margin = new Padding(0) }, 0, 2);

        var buttonHeight = LogicalToDeviceUnits(34);
        var footer = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 1,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(inset, LogicalToDeviceUnits(12), inset, LogicalToDeviceUnits(12)),
            Margin = new Padding(0),
            BackColor = Theme.Surface
        };
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); // 0: Spacer
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));     // 1: New Case
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));     // 2: Open Case...
        _btnNewCase = ButtonHelper.CreateButton("New Case", 0, buttonHeight, ButtonStyle.Default);
        _btnNewCase.Margin = new Padding(0, 0, LogicalToDeviceUnits(Theme.SmallGap), 0);
        _btnNewCase.Anchor = AnchorStyles.Right;
        _btnNewCase.Visible = false;
        _btnOpenCase = ButtonHelper.CreateButton("Open Case...", 0, buttonHeight, ButtonStyle.Default);
        _btnOpenCase.Margin = new Padding(0);
        _btnOpenCase.Anchor = AnchorStyles.Right;
        footer.Controls.Add(_btnNewCase, 1, 0);
        footer.Controls.Add(_btnOpenCase, 2, 0);
        recentLayout.Controls.Add(footer, 0, 3);

        recentCard.Controls.Add(recentLayout);
        return recentCard;
    }

    private Label CreateCaptionLabel(string text) => new()
    {
        Text = text,
        AutoSize = true,
        Margin = new Padding(3, 8, 3, 4),
        TextAlign = ContentAlignment.BottomLeft,
        Font = Theme.LabelFont(),
        ForeColor = Theme.Text
    };

    private static Label CreateComboCaption(string text) => new()
    {
        Text = text,
        AutoSize = true,
        Font = Theme.BodyFont(),
        ForeColor = Theme.Text,
        Margin = new Padding(0, 8, 4, 0)
    };

    private static LinkLabel CreateLink(string text) => new()
    {
        Text = text,
        AutoSize = true,
        Anchor = AnchorStyles.Right,
        Font = Theme.SmallFont(),
        LinkColor = Theme.Accent,
        ActiveLinkColor = Theme.AccentPressed,
        Visible = false
    };

    private RadioButton CreateRadio(string text)
    {
        var radio = new RadioButton
        {
            Text = text,
            AutoSize = true,
            Font = Theme.BodyFont(),
            ForeColor = Theme.Text,
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
        Font = Theme.BodyFont()
    };

    /// <summary>
    /// Counter text against the 8,000-character scenario cap, e.g. "1,234 / 8,000 characters".
    /// </summary>
    public static string CounterText(int length) =>
        $"{length.ToString("N0", CultureInfo.InvariantCulture)} / 8,000 characters";

    // 7,200 = 90% of the cap
    public static Color CounterColor(int length) =>
        length > CaseDraft.MaxScenarioLength ? Theme.Danger : length > 7_200 ? Theme.Warning : Theme.TextMuted;

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
        _lnkReviewNames.Enabled = enabled;
        _lstRecent.Enabled = enabled;
        _btnOpenCase.Enabled = enabled;
        _btnNewCase.Enabled = enabled;
        UpdateStateComboEnabled();
    }

    private void SetStatus(string text, Color color)
    {
        _lblStatus.Text = text;
        _lblStatus.ForeColor = color;
    }

    private void CancelCheck()
    {
        try
        {
            _checkCts?.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // The check already finished and released its token source; nothing to cancel
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            // Closing the wizard or switching flows stops the in-flight check (WR-07)
            CancelCheck();
            _loadingOverlay?.Dispose();
            _recentToolTip?.Dispose();
        }

        base.Dispose(disposing);
    }

    private void UpdateReviewNamesLink()
    {
        _lnkReviewNames.Visible = _state != null && _state.Case.Substitutions.Count > 0;
    }

    private void ReviewRealNames()
    {
        var draft = _state?.Case;
        if (draft == null || _isChecking || draft.Substitutions.Count == 0)
            return;

        using var dlg = new EntityReviewDialog(draft.Substitutions);
        if (dlg.ShowDialog(this) != DialogResult.OK)
            return;

        // EffectiveScenario is rebuilt from these decisions on Next; the unchanged hash skips a new LLM check
        draft.Substitutions = dlg.Entities;
        SetStatus("Real-name decisions updated. Click Next to continue.", Theme.Success);
    }

    public void BindState(WizardState state)
    {
        _state = state;
    }

    public Task OnEnterStepAsync()
    {
        LoadInputs(_state.Case);
        RefreshRecentList();

        if (!string.IsNullOrWhiteSpace(_state.PendingNotice))
        {
            SetStatus(_state.PendingNotice, Theme.Success);
            _state.PendingNotice = string.Empty;
        }

        _txtScenario.Focus();
        return Task.CompletedTask;
    }

    /// <summary>
    /// Test-only: binds the state and fills the inputs and the recent list without entering the step.
    /// </summary>
    internal void LoadForRender(WizardState state)
    {
        BindState(state);
        LoadInputs(state.Case);
        RefreshRecentList();
    }

    /// <summary>
    /// Fills the scenario, complexity and document/court inputs from a draft (on enter, after opening a case and on New Case).
    /// </summary>
    internal void LoadInputs(CaseDraft draft)
    {
        _txtScenario.Text = draft.Scenario;

        switch (draft.Complexity)
        {
            case ComplexityLevel.Simple: _rbSimple.Checked = true; break;
            case ComplexityLevel.Complex: _rbComplex.Checked = true; break;
            default: _rbStandard.Checked = true; break;
        }

        // A hand-edited case.json may carry an out-of-range enum value
        _cmbDocument.SelectedIndex = Math.Clamp((int)draft.DocumentPreference, 0, _cmbDocument.Items.Count - 1);
        _cmbCourt.SelectedIndex = Math.Clamp((int)draft.CourtPreference, 0, _cmbCourt.Items.Count - 1);
        var stateIndex = string.IsNullOrEmpty(draft.StatePreference) ? -1 : _cmbState.Items.IndexOf(draft.StatePreference);
        _cmbState.SelectedIndex = stateIndex >= 0 ? stateIndex : 0;
        UpdateStateComboEnabled();

        _lblPresetSummary.Text = ComplexityPreset.Get(SelectedLevel()).Summary;
        UpdateCounter();
        UpdateReviewNamesLink();
        _btnNewCase.Visible = !string.IsNullOrEmpty(draft.FolderPath);
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

    // ----- Recent cases, Open Case..., New Case (D-11, UI-SPEC S3) -----

    /// <summary>
    /// True when opening another case would discard a typed scenario that was never saved (C8):
    /// some scenario text, no case folder and no core document yet.
    /// </summary>
    public static bool NeedsOpenConfirmation(CaseDraft? current) =>
        current != null
        && !string.IsNullOrWhiteSpace(current.Scenario)
        && string.IsNullOrEmpty(current.FolderPath)
        && current.CoreDocument == null;

    private string? OpenCasePath()
    {
        var folder = _state?.Case.FolderPath;
        return string.IsNullOrEmpty(folder) ? null : Path.Combine(folder, CaseStore.CaseFileName);
    }

    private static bool SamePath(string? a, string? b)
    {
        if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b))
            return false;
        try
        {
            return string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            // A malformed recent entry never matches the open case
            return false;
        }
    }

    private bool IsOpenCase(RecentCase recent) => SamePath(recent.Path, OpenCasePath());

    private void RefreshRecentList()
    {
        if (_state == null)
            return;

        // The store already prunes missing paths and caps the list at 8
        _recentCases = _state.CaseStore.GetRecentCases().Take(CaseStore.MaxRecentCases).ToList();
        _hoverIndex = -1;
        _toolTipIndex = -1;
        _recentToolTip.SetToolTip(_lstRecent, string.Empty);

        _lstRecent.BeginUpdate();
        _lstRecent.Items.Clear();
        foreach (var recent in _recentCases)
            _lstRecent.Items.Add(recent.Title);
        _lstRecent.EndUpdate();
        _lstRecent.SelectedIndex = -1;

        var hasItems = _recentCases.Count > 0;
        _lstRecent.Visible = hasItems;
        _lblRecentEmpty.Visible = !hasItems;
        _btnNewCase.Visible = !string.IsNullOrEmpty(_state.Case.FolderPath);
    }

    private void LstRecent_DrawItem(object? sender, DrawItemEventArgs e)
    {
        if (e.Index < 0 || e.Index >= _recentCases.Count)
            return;

        var recent = _recentCases[e.Index];
        var isOpen = IsOpenCase(recent);
        var selected = (e.State & DrawItemState.Selected) == DrawItemState.Selected;
        var g = e.Graphics;
        var bounds = e.Bounds;

        // Keyboard selection reads like hover; the open case keeps the Accent selection look
        var background = isOpen ? Theme.AccentSoft
            : e.Index == _hoverIndex || selected ? Theme.SurfaceMuted
            : Theme.Surface;
        using (var brush = new SolidBrush(background))
        {
            g.FillRectangle(brush, bounds);
        }
        if (isOpen)
        {
            using var bar = new SolidBrush(Theme.Accent);
            g.FillRectangle(bar, bounds.X, bounds.Y, LogicalToDeviceUnits(3), bounds.Height);
        }

        // The open case shows its live title and stage; the others show what recent.json recorded
        var title = isOpen ? CaseFolderNaming.CaseTitle(_state.Case) : recent.Title;
        var stage = isOpen ? _state.Case.Stage : recent.Stage;
        var line2 = CaseGuideUiText.RecentItemSecondLine(stage, recent.LastOpenedUtc.ToLocalTime(), isOpen);

        const TextFormatFlags flags = TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix |
            TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis | TextFormatFlags.Left | TextFormatFlags.Top;

        var titleFont = e.Font ?? _lstRecent.Font;
        using var small = Theme.SmallFont();
        var titleHeight = TextRenderer.MeasureText(g, "Ag", titleFont, Size.Empty, flags).Height;
        var line2Height = TextRenderer.MeasureText(g, "Ag", small, Size.Empty, flags).Height;
        var lineGap = LogicalToDeviceUnits(3);

        // Content x: text inset 15 inside the Padding = 1 card; the two lines are centered as a pair
        var pad = LogicalToDeviceUnits(15);
        var width = Math.Max(0, bounds.Width - pad * 2);
        var top = bounds.Y + (bounds.Height - (titleHeight + lineGap + line2Height)) / 2;
        TextRenderer.DrawText(g, title, titleFont, new Rectangle(bounds.X + pad, top, width, titleHeight),
            isOpen ? Theme.Accent : Theme.Text, flags);
        TextRenderer.DrawText(g, line2, small, new Rectangle(bounds.X + pad, top + titleHeight + lineGap, width, line2Height),
            Theme.TextMuted, flags);
    }

    private int RecentIndexAt(Point location)
    {
        var index = _lstRecent.IndexFromPoint(location);
        if (index < 0 || index >= _recentCases.Count)
            return -1;
        return _lstRecent.GetItemRectangle(index).Contains(location) ? index : -1;
    }

    private void SetHoverIndex(int index)
    {
        if (index == _hoverIndex)
            return;

        var previous = _hoverIndex;
        _hoverIndex = index;
        if (previous >= 0 && previous < _lstRecent.Items.Count)
            _lstRecent.Invalidate(_lstRecent.GetItemRectangle(previous));
        if (index >= 0 && index < _lstRecent.Items.Count)
            _lstRecent.Invalidate(_lstRecent.GetItemRectangle(index));
    }

    private void LstRecent_MouseMove(object? sender, MouseEventArgs e)
    {
        var index = RecentIndexAt(e.Location);
        SetHoverIndex(index);

        // The tooltip shows the full folder path of the hovered case
        if (index != _toolTipIndex)
        {
            _toolTipIndex = index;
            var folder = index >= 0 ? Path.GetDirectoryName(_recentCases[index].Path) ?? string.Empty : string.Empty;
            _recentToolTip.SetToolTip(_lstRecent, folder);
        }
    }

    private void LstRecent_MouseClick(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left)
            return;

        var index = RecentIndexAt(e.Location);
        if (index >= 0)
            OpenRecent(index);
    }

    private void LstRecent_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode != Keys.Enter || _lstRecent.SelectedIndex < 0)
            return;

        e.Handled = true;
        e.SuppressKeyPress = true;
        OpenRecent(_lstRecent.SelectedIndex);
    }

    private void OpenRecent(int index)
    {
        if (index < 0 || index >= _recentCases.Count)
            return;

        var recent = _recentCases[index];
        OpenCase(recent.Path, recent.Title);
    }

    private void BrowseForCase()
    {
        if (_state == null || _isChecking)
            return;

        using var dlg = new OpenFileDialog
        {
            Title = "Open Case",
            Filter = "ReelDiscovery case (case.json)|case.json|JSON files (*.json)|*.json",
            CheckFileExists = true,
            Multiselect = false
        };
        if (Directory.Exists(_state.CaseStore.CasesRoot))
            dlg.InitialDirectory = _state.CaseStore.CasesRoot;

        if (dlg.ShowDialog(this) != DialogResult.OK)
            return;

        // The folder name stands in for the case title in C8 until the file is loaded
        var folderName = Path.GetFileName(Path.GetDirectoryName(dlg.FileName));
        OpenCase(dlg.FileName, string.IsNullOrWhiteSpace(folderName) ? CaseStore.CaseFileName : folderName);
    }

    /// <summary>
    /// Brings this step's inputs into a saved case and writes it before another case replaces it.
    /// Returns false (with the reason in the status line) when the save failed.
    /// </summary>
    private bool FlushCurrentCase()
    {
        var current = _state.Case;
        if (string.IsNullOrEmpty(current.FolderPath) || !CaseStore.CanSave(current))
            return true;

        StoreInputs(current, _txtScenario.Text, SelectedLevel(), _cmbDocument.SelectedIndex, _cmbCourt.SelectedIndex, _cmbState.SelectedItem as string);
        if (_state.CaseStore.SaveNow(current))
            return true;

        SetStatus(CaseGuideUiText.SaveFailedText(_state.CaseStore.LastSaveError), Theme.Danger);
        return false;
    }

    /// <summary>
    /// Opens a case.json (D-11): asks C8 over an unsaved typed scenario, loads the file, refreshes this step's
    /// inputs, then jumps to the furthest stage reached. Load errors show here in Danger and the list is refreshed.
    /// </summary>
    internal void OpenCase(string path, string title)
    {
        if (_state == null || _isChecking)
            return;

        // The case that is already open just continues at its stage; reloading it could drop edits made on this step
        if (!SamePath(path, OpenCasePath()))
        {
            if (NeedsOpenConfirmation(_state.Case))
            {
                var (caption, text) = CaseGuideUiText.C8OpenOverUnsaved(title);
                if (MessageBox.Show(this, text, caption, MessageBoxButtons.YesNo, MessageBoxIcon.Warning,
                        MessageBoxDefaultButton.Button2) != DialogResult.Yes)
                    return;
            }
            else if (!FlushCurrentCase())
            {
                return;
            }

            // Recent paths and dialog picks only reach CaseStore.Load (size cap, version check, never throws)
            var result = _state.CaseStore.Load(path);
            if (!result.Succeeded)
            {
                SetStatus(result.Message, Theme.Danger);
                RefreshRecentList();
                return;
            }

            _state.Case = result.Draft!;
        }

        // OnLeaveStepAsync stores the textbox inputs into the draft; refresh them first or the loaded scenario is overwritten (Pitfall 5)
        LoadInputs(_state.Case);
        RefreshRecentList();
        _state.PendingNotice = CaseGuideUiText.OpenedCaseText(CaseFolderNaming.CaseTitle(_state.Case));
        StateChanged?.Invoke(this, EventArgs.Empty);

        if (FindForm() is WizardForm wizard)
        {
            // A Described case maps back to this step, which shows the notice when it is entered again
            wizard.NavigateToStage(_state.Case.Stage);
        }
        else
        {
            SetStatus(_state.PendingNotice, Theme.Success);
            _state.PendingNotice = string.Empty;
        }
    }

    /// <summary>
    /// Starts a fresh draft with no prompt (the current case is on disk); the previous case stays under Recent cases.
    /// </summary>
    internal void StartNewCase()
    {
        if (_state == null || _isChecking)
            return;

        if (!FlushCurrentCase())
            return;

        var previousTitle = CaseFolderNaming.CaseTitle(_state.Case);
        _state.Case = new CaseDraft();
        LoadInputs(_state.Case);
        RefreshRecentList();
        SetStatus(CaseGuideUiText.NewCaseText(previousTitle), Theme.Success);
        _txtScenario.Focus();
        StateChanged?.Invoke(this, EventArgs.Empty);
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
            SetStatus("Checking the scenario for real names...", Theme.Accent);
            _loadingOverlay.Show(_txtScenario);
            var checkCts = new CancellationTokenSource();
            _checkCts = checkCts;
            _lnkCancelCheck.Visible = true;
            StateChanged?.Invoke(this, EventArgs.Empty);

            try
            {
                var entities = await new RealEntityDetector(_state.CreateGateway()).DetectAsync(text, checkCts.Token);

                // The window may have closed while the check ran (WR-07)
                if (IsDisposed)
                    return false;

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
                    SetStatus("No real names found.", Theme.Success);
                }

                draft.EntityCheckHash = hash;
            }
            catch (OperationCanceledException) when (checkCts.IsCancellationRequested)
            {
                if (!IsDisposed)
                {
                    SetStatus("Real-name check cancelled. Click Next to check again.", Theme.Warning);
                }
                return false;
            }
            catch (Exception ex)
            {
                if (IsDisposed)
                    return false;

                var message = ex.InnerException != null
                    ? $"{ex.Message} ({ex.InnerException.Message})"
                    : ex.Message;
                SetStatus($"Could not check the scenario for real names: {message}. Click Next to try again.", Theme.Danger);
                return false;
            }
            finally
            {
                checkCts.Dispose();
                _checkCts = null;
                _isChecking = false;
                if (!IsDisposed)
                {
                    _lnkCancelCheck.Visible = false;
                    SetInputsEnabled(true);
                    UpdateReviewNamesLink();
                    _loadingOverlay.Hide();
                    StateChanged?.Invoke(this, EventArgs.Empty);
                }
            }
        }

        try
        {
            draft.EffectiveScenario = NameSubstitution.Apply(text, draft.Substitutions);
        }
        catch (RegexMatchTimeoutException)
        {
            // Never let a regex timeout escape into the async void Next handler (WR-04)
            SetStatus("Could not apply the substitute names to this scenario. Shorten it and click Next again.", Theme.Danger);
            return false;
        }

        return true;
    }
}
