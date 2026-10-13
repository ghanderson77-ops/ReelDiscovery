using System.Text.RegularExpressions;
using ReelDiscovery.Forms;
using ReelDiscovery.Helpers;
using ReelDiscovery.Models;
using ReelDiscovery.Models.Case;
using ReelDiscovery.Services.Case;
using ReelDiscovery.Services.Corpus;

namespace ReelDiscovery.UserControls;

/// <summary>
/// Case Guide step (UI-SPEC S1): header with save state, stale / needs-review banner, segmented tabs hosting the four
/// tab panels (or a state card), the Checks card and the status strip. This file holds the layout and the display
/// refresh; StepCaseGuide.Run.cs holds the generation runs, host actions, autosave, lifecycle and the Next gate.
/// </summary>
public partial class StepCaseGuide : UserControl, IWizardStep, IGuideHost
{
    // Least body height (logical) worth keeping the Checks list for
    private const int MinBodyHeight = 300;
    private const string ChecksCollapsedText = "Make the window taller to see the list.";

    // The Checks list shows up to two whole two-line checks, one when the window is short, and folds to its caption line
    // when even one would crowd the body
    private const int ChecksMaxRows = 2;
    private const int ChecksRowHeight = 48;
    // Space between a check and its fix hint inside one row
    private const int CheckLineGap = 2;

    private WizardState _state = null!;
    private readonly Dictionary<GuidePart, GuideTabPanel> _panels = new();
    private readonly Dictionary<GuidePart, ThemedButton> _tabButtons = new();

    private TableLayoutPanel _root = null!;
    private Label _lblTitle = null!;
    private Label _lblSubtitle = null!;
    private Label _lblDependency = null!;
    private FlowLayoutPanel _headerButtons = null!;
    private Button _btnResume = null!;
    private CardPanel _banner = null!;
    private RowStyle _bannerRow = null!;
    private Label _lblBannerHeading = null!;
    private Label _lblBannerBody = null!;
    private FlowLayoutPanel _bannerButtons = null!;
    private Button _btnUpdateGuide = null!;
    private Button _btnNotNow = null!;
    private Button _btnReviewFacts = null!;
    private Button _btnMarkAllReviewed = null!;
    private Button _btnBannerReviewPlan = null!;
    private FlowLayoutPanel _tabStrip = null!;
    private FlowLayoutPanel _tabActions = null!;
    private Panel _bodyHost = null!;
    private CardPanel _stateCard = null!;
    private FlowLayoutPanel _stateContent = null!;
    private Label _lblStateHeading = null!;
    private Label _lblStateBody = null!;
    private SlimProgressBar _stateBar = null!;
    private Button _btnStateAction = null!;
    private CardPanel _checksCard = null!;
    private RowStyle _checksRow = null!;
    private Label _lblChecks = null!;
    private Label _lblNoChecks = null!;
    private Panel _checksGridHost = null!;
    private TableLayoutPanel _checksCaptionLine = null!;
    private FlowLayoutPanel _checksButtons = null!;
    private ThemedButton _btnFix = null!;
    private ThemedButton _btnFixAll = null!;
    private DataGridView _gridChecks = null!;
    private readonly Font _checkHintFont = Theme.SmallFont();
    private Label _lblStatus = null!;
    private SlimProgressBar _progress = null!;
    private Button _btnCancel = null!;

    private bool _isGenerating;
    private bool _userPickedTab;
    private GuidePart? _writingPart;
    private string _writingEntityId = string.Empty;
    private GuidePart _selectedTab = GuidePart.Issues;
    private GuideTabPanel? _actionsOwner;
    private List<GuideFinding> _findings = new();
    private bool _showBanner;
    // The stale banner shows its Primary Update Guide
    private bool _bannerPrimaryShown;
    private bool _checksHasFindings;
    private bool _checksShowsNoProblems;
    // The user picked a check (click, keys or Show Checks): its selection stays drawn and drives the Fix button
    private bool _checksUserSelected;
    // The findings list is shown (it folds to the caption line on a short window)
    private bool _checksListShown;
    private List<GuideFinding> _visibleFindings = new();

    public string StepTitle => "Case Guide";
    public string NextButtonText => "Next >";
    public bool CanMoveNext => !_isGenerating;
    public bool CanMoveBack => !_isGenerating;

    /// <summary>
    /// True while a guide, tab or entry run is active.
    /// </summary>
    public bool IsGenerating => _isGenerating;

    public event EventHandler? StateChanged;

    public StepCaseGuide()
    {
        InitializeUI();
    }

    private void InitializeUI()
    {
        // Content aligns with the wizard's page margin (the shell supplies it)
        this.Padding = new Padding(0);
        this.BackColor = Theme.Background;
        var gap = LogicalToDeviceUnits(Theme.Gap);
        var smallGap = LogicalToDeviceUnits(Theme.SmallGap);
        var pad = LogicalToDeviceUnits(Theme.CardPadding);
        var inset = LogicalToDeviceUnits(15);
        var buttonHeight = LogicalToDeviceUnits(34);

        _root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 6,
            Padding = new Padding(0),
            BackColor = Theme.Background
        };
        _root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        _root.RowStyles.Add(new RowStyle(SizeType.AutoSize));                                 // 0: Header
        // Sized in SizeBanner from the page width: the wrapping text makes the card's own preferred size unusable
        _bannerRow = new RowStyle(SizeType.Absolute, 0);
        _root.RowStyles.Add(_bannerRow);                                                      // 1: Banner
        _root.RowStyles.Add(new RowStyle(SizeType.AutoSize));                                 // 2: Tabs row
        _root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));                             // 3: Body
        // Sized in UpdateChecksLayout to whole checks (ChecksCardHeightFor)
        _checksRow = new RowStyle(SizeType.AutoSize);
        _root.RowStyles.Add(_checksRow);                                                      // 4: Checks card
        _root.RowStyles.Add(new RowStyle(SizeType.AutoSize));                                 // 5: Status strip

        // ----- Row 0, header: title + save state, Resume Guide on the right -----
        var headerLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 3,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Margin = new Padding(0, 0, 0, gap)
        };
        headerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        headerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        headerLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // 0: Title
        headerLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // 1: Subtitle
        headerLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // 2: Dependency line (09 S2; collapses when hidden)

        _lblTitle = new Label
        {
            Text = "Case Guide",
            AutoSize = false,
            AutoEllipsis = true,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.BottomLeft,
            Margin = new Padding(0),
            Font = Theme.TitleFont(),
            ForeColor = Theme.Text
        };
        _lblTitle.Height = _lblTitle.PreferredHeight;
        _lblSubtitle = new Label
        {
            AutoSize = false,
            AutoEllipsis = true,
            UseMnemonic = false,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.TopLeft,
            Margin = new Padding(0, LogicalToDeviceUnits(2), 0, 0),
            Font = Theme.BodyFont(),
            ForeColor = Theme.TextMuted
        };
        _lblSubtitle.Height = _lblSubtitle.PreferredHeight;
        // Who depends on this guide (09 S2); wraps to the header column width set in SizeDependencyLine, never ellipsizes
        _lblDependency = new Label
        {
            AutoSize = true,
            AutoEllipsis = false,
            UseMnemonic = false,
            Margin = new Padding(0, LogicalToDeviceUnits(4), 0, 0),
            Font = Theme.SmallFont(),
            ForeColor = Theme.TextMuted,
            Visible = false
        };

        var headerButtons = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Anchor = AnchorStyles.Right,
            Margin = new Padding(gap, 0, 0, 0)
        };
        // Writes every unwritten part (after a cancel, a failure or reopening a partial guide)
        _btnResume = ButtonHelper.CreateButton("Resume Guide", 0, buttonHeight, ButtonStyle.Default);
        _btnResume.Margin = new Padding(0);
        _btnResume.Visible = false;
        headerButtons.Controls.Add(_btnResume);
        _headerButtons = headerButtons;

        headerLayout.Controls.Add(_lblTitle, 0, 0);
        headerLayout.Controls.Add(_lblSubtitle, 0, 1);
        headerLayout.Controls.Add(_lblDependency, 0, 2);
        headerLayout.Controls.Add(headerButtons, 1, 0);
        headerLayout.SetRowSpan(headerButtons, 3);
        _root.Controls.Add(headerLayout, 0, 0);

        // ----- Row 1, banner: stale guide or facts to review (Warning border on a white card) -----
        _banner = new CardPanel
        {
            BorderColor = Theme.Warning,
            BackColor = Theme.Surface,
            Padding = new Padding(pad),
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 0, 0, gap),
            Visible = false
        };
        var bannerLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 2,
            Margin = new Padding(0),
            Padding = new Padding(0),
            BackColor = Theme.Surface
        };
        bannerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        bannerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        bannerLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));     // 0: Heading
        bannerLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); // 1: Body
        _lblBannerHeading = new Label
        {
            AutoSize = true,
            UseMnemonic = false,
            Margin = new Padding(0, 0, 0, LogicalToDeviceUnits(4)),
            Font = Theme.LabelFont(),
            ForeColor = Theme.Warning,
            BackColor = Theme.Surface
        };
        _lblBannerBody = new Label
        {
            AutoSize = true,
            UseMnemonic = false,
            Margin = new Padding(0),
            Font = Theme.BodyFont(),
            ForeColor = Theme.TextMuted,
            BackColor = Theme.Surface
        };
        _bannerButtons = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Anchor = AnchorStyles.Right,
            Margin = new Padding(gap, 0, 0, 0),
            BackColor = Theme.Surface
        };
        _btnUpdateGuide = ButtonHelper.CreateButton("Update Guide", 0, buttonHeight, ButtonStyle.Primary);
        _btnUpdateGuide.Margin = new Padding(0, 0, smallGap, 0);
        _btnNotNow = ButtonHelper.CreateButton("Not Now", 0, buttonHeight, ButtonStyle.Default);
        _btnNotNow.Margin = new Padding(0);
        _btnReviewFacts = ButtonHelper.CreateButton("Review Facts", 0, buttonHeight, ButtonStyle.Default);
        _btnReviewFacts.Margin = new Padding(0, 0, smallGap, 0);
        _btnMarkAllReviewed = ButtonHelper.CreateButton(CaseGuideUiText.MarkAllReviewedButtonText, 0, buttonHeight, ButtonStyle.Default);
        _btnMarkAllReviewed.Margin = new Padding(0);
        // PlanDependency banner (09 S2): Default, never takes focus by itself
        _btnBannerReviewPlan = ButtonHelper.CreateButton("Review Plan", 0, buttonHeight, ButtonStyle.Default);
        _btnBannerReviewPlan.Margin = new Padding(0);
        _btnBannerReviewPlan.Visible = false;
        _bannerButtons.Controls.Add(_btnUpdateGuide);
        _bannerButtons.Controls.Add(_btnNotNow);
        _bannerButtons.Controls.Add(_btnReviewFacts);
        _bannerButtons.Controls.Add(_btnMarkAllReviewed);
        _bannerButtons.Controls.Add(_btnBannerReviewPlan);
        bannerLayout.Controls.Add(_lblBannerHeading, 0, 0);
        bannerLayout.Controls.Add(_lblBannerBody, 0, 1);
        bannerLayout.Controls.Add(_bannerButtons, 1, 0);
        bannerLayout.SetRowSpan(_bannerButtons, 2);
        _banner.Controls.Add(bannerLayout);
        _root.Controls.Add(_banner, 0, 1);

        // ----- Row 2, tabs row: segmented tab toggles | the selected tab's actions -----
        var tabsRow = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Margin = new Padding(0, 0, 0, gap)
        };
        tabsRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        tabsRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        tabsRow.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // 0: Toggles | actions
        _tabStrip = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(0)
        };
        foreach (var part in GuideStaleness.Order)
        {
            var toggle = (ThemedButton)ButtonHelper.CreateButton(CaseGuideUiText.PartNoun(part, true), 0, buttonHeight, ButtonStyle.Toggle);
            toggle.Margin = new Padding(0, 0, part == GuidePart.Values ? 0 : LogicalToDeviceUnits(4), 0);
            toggle.Selected = part == _selectedTab;
            _tabButtons[part] = toggle;
            _tabStrip.Controls.Add(toggle);
        }
        _tabActions = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Anchor = AnchorStyles.Right,
            Margin = new Padding(gap, 0, 0, 0)
        };
        tabsRow.Controls.Add(_tabStrip, 0, 0);
        tabsRow.Controls.Add(_tabActions, 1, 0);
        _root.Controls.Add(tabsRow, 0, 2);

        // ----- Row 3, body: the selected tab's panel, or the full-width state card -----
        _bodyHost = new Panel { Dock = DockStyle.Fill, Margin = new Padding(0), BackColor = Theme.Background };
        _panels[GuidePart.Issues] = new IssuesTabPanel();
        _panels[GuidePart.Cast] = new CastTabPanel();
        _panels[GuidePart.Facts] = new FactsTabPanel();
        _panels[GuidePart.Values] = new ValuesTabPanel();
        foreach (var panel in _panels.Values)
        {
            panel.Dock = DockStyle.Fill;
            panel.Visible = false;
            panel.Attach(this);
            _bodyHost.Controls.Add(panel);
        }

        _stateCard = new CardPanel { Dock = DockStyle.Fill, Visible = false };
        _stateContent = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            BackColor = Theme.Surface
        };
        _lblStateHeading = new Label
        {
            AutoSize = true,
            UseMnemonic = false,
            Font = Theme.HeadingFont(),
            ForeColor = Theme.Text,
            Margin = new Padding(0, 0, 0, smallGap)
        };
        _lblStateBody = new Label
        {
            AutoSize = true,
            UseMnemonic = false,
            MaximumSize = new Size(LogicalToDeviceUnits(520), 0),
            Font = Theme.BodyFont(),
            ForeColor = Theme.TextMuted,
            Margin = new Padding(0, 0, 0, LogicalToDeviceUnits(20))
        };
        _stateBar = new SlimProgressBar
        {
            Width = LogicalToDeviceUnits(360),
            Height = LogicalToDeviceUnits(6),
            Marquee = true,
            Visible = false,
            Margin = new Padding(0, LogicalToDeviceUnits(4), 0, smallGap)
        };
        _btnStateAction = ButtonHelper.CreateButton("Generate Guide", 0, buttonHeight, ButtonStyle.Primary);
        _btnStateAction.Margin = new Padding(0);
        _stateContent.Controls.Add(_lblStateHeading);
        _stateContent.Controls.Add(_lblStateBody);
        _stateContent.Controls.Add(_stateBar);
        _stateContent.Controls.Add(_btnStateAction);
        _stateCard.Controls.Add(_stateContent);
        _bodyHost.Controls.Add(_stateCard);
        _root.Controls.Add(_bodyHost, 0, 3);

        // ----- Row 4, Checks card: validator findings (never called "Issues": that is a tab) -----
        _checksCard = new CardPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(1),
            Margin = new Padding(0, gap, 0, 0)
        };
        var checksLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            BackColor = Theme.Surface,
            Margin = new Padding(0)
        };
        checksLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        checksLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));     // 0: Caption line
        checksLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); // 1: Findings grid
        var captionLine = _checksCaptionLine = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 1,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(0),
            Margin = new Padding(inset, LogicalToDeviceUnits(12), inset, smallGap),
            BackColor = Theme.Surface
        };
        captionLine.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        captionLine.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        captionLine.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        captionLine.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // 0: Caption | hint or no-findings line | Fix, Fix All
        _lblChecks = new Label
        {
            Text = "Checks",
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(0),
            Font = Theme.LabelFont(),
            ForeColor = Theme.Text
        };
        _lblNoChecks = new Label
        {
            Text = CaseGuideUiText.NoChecksText,
            AutoSize = false,
            AutoEllipsis = true,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            Margin = new Padding(smallGap, 0, 0, 0),
            Font = Theme.SmallFont(),
            ForeColor = Theme.Success,
            Visible = false
        };
        _lblNoChecks.Height = _lblNoChecks.PreferredHeight;
        // Fix runs the selected check's fix; Fix All every automatic one (both only while the list has findings)
        _checksButtons = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Anchor = AnchorStyles.Right,
            Margin = new Padding(gap, 0, 0, 0),
            Padding = new Padding(0),
            BackColor = Theme.Surface,
            Visible = false
        };
        _btnFix = (ThemedButton)ButtonHelper.CreateButton(CaseGuideUiText.FixButtonText, 0, buttonHeight, ButtonStyle.Default);
        _btnFix.Margin = new Padding(0);
        _btnFix.Enabled = false;
        _btnFixAll = (ThemedButton)ButtonHelper.CreateButton(CaseGuideUiText.FixAllButtonText(0), 0, buttonHeight, ButtonStyle.Default);
        _btnFixAll.Margin = new Padding(smallGap, 0, 0, 0);
        _checksButtons.Controls.Add(_btnFix);
        _checksButtons.Controls.Add(_btnFixAll);
        captionLine.Controls.Add(_lblChecks, 0, 0);
        captionLine.Controls.Add(_lblNoChecks, 1, 0);
        captionLine.Controls.Add(_checksButtons, 2, 0);
        checksLayout.Controls.Add(captionLine, 0, 0);

        _checksGridHost = new Panel { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, LogicalToDeviceUnits(6)), BackColor = Theme.Surface };
        _gridChecks = new DataGridView { Dock = DockStyle.Fill, ReadOnly = true };
        GridStyle.Apply(_gridChecks, this);
        // Two lines per check: what is wrong, then how to fix it (painted in CellPainting)
        _gridChecks.RowTemplate.Height = LogicalToDeviceUnits(ChecksRowHeight);
        SetChecksSelectionVisible(false);
        _gridChecks.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Severity",
            HeaderText = "Severity",
            Width = LogicalToDeviceUnits(96),
            SortMode = DataGridViewColumnSortMode.NotSortable
        });
        _gridChecks.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Where",
            HeaderText = "Where",
            Width = LogicalToDeviceUnits(176),
            SortMode = DataGridViewColumnSortMode.NotSortable
        });
        _gridChecks.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Check",
            HeaderText = "Check",
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
            SortMode = DataGridViewColumnSortMode.NotSortable
        });
        _checksGridHost.Controls.Add(_gridChecks);
        checksLayout.Controls.Add(_checksGridHost, 0, 1);
        _checksCard.Controls.Add(checksLayout);
        _root.Controls.Add(_checksCard, 0, 4);

        // ----- Row 5, status strip: status + progress bar + cancel (bar and cancel only during a run) -----
        var progressStrip = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 1,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Margin = new Padding(0, LogicalToDeviceUnits(12), 0, 0)
        };
        progressStrip.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        progressStrip.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        progressStrip.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        progressStrip.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // 0: Status | bar | Cancel
        _btnCancel = ButtonHelper.CreateButton("Cancel Generation", 0, buttonHeight, ButtonStyle.Danger);
        _btnCancel.Margin = new Padding(0);
        _btnCancel.Visible = false;
        _lblStatus = new Label
        {
            Dock = DockStyle.Fill,
            MinimumSize = new Size(0, _btnCancel.Height),
            AutoEllipsis = true,
            UseMnemonic = false,
            TextAlign = ContentAlignment.MiddleLeft,
            Margin = new Padding(0),
            Font = Theme.BodyFont(),
            ForeColor = Theme.TextMuted
        };
        _progress = new SlimProgressBar
        {
            Width = LogicalToDeviceUnits(220),
            Height = LogicalToDeviceUnits(6),
            Anchor = AnchorStyles.Left | AnchorStyles.Right,
            Margin = new Padding(gap, 0, gap, 0),
            Visible = false
        };
        progressStrip.Controls.Add(_lblStatus, 0, 0);
        progressStrip.Controls.Add(_progress, 1, 0);
        progressStrip.Controls.Add(_btnCancel, 2, 0);
        _root.Controls.Add(progressStrip, 0, 5);

        this.Controls.Add(_root);

        // Wire handlers only after every control exists (handlers read other controls)
        foreach (var (part, toggle) in _tabButtons)
            toggle.Click += (s, e) => TabButton_Click(part);
        _btnResume.Click += async (s, e) => await GenerateOrResumeAsync();
        _btnStateAction.Click += async (s, e) => await GenerateOrResumeAsync();
        _btnUpdateGuide.Click += async (s, e) => await UpdateGuideAsync();
        _btnNotNow.Click += (s, e) => DismissStaleBanner();
        _btnReviewFacts.Click += (s, e) => ReviewFacts();
        _btnMarkAllReviewed.Click += (s, e) => MarkAllReviewed();
        // Audit N1: the banner is a shortcut past Next, so it asks the same Next gate first
        _btnBannerReviewPlan.Click += async (s, e) =>
        {
            if (await ValidateStepAsync() && !IsDisposed)
                (FindForm() as WizardForm)?.NavigateToStage(CaseStage.ReviewPlan);
        };
        _btnCancel.Click += BtnCancel_Click;
        _gridChecks.CellDoubleClick += (s, e) => NavigateToFinding(e.RowIndex);
        _gridChecks.KeyDown += GridChecks_KeyDown;
        _gridChecks.CellPainting += GridChecks_CellPainting;
        // The grid makes its first row current when it is first shown; the selection is drawn only while the
        // grid has focus or the user picked a check, so an untouched Checks list never looks like something was picked
        _gridChecks.Enter += (s, e) => SetChecksSelectionVisible(true);
        _gridChecks.Leave += (s, e) => SetChecksSelectionVisible(_checksUserSelected);
        _gridChecks.SelectionChanged += (s, e) =>
        {
            if (_gridChecks.Focused && _gridChecks.SelectedRows.Count > 0)
                _checksUserSelected = true;
            UpdateFixButtons();
        };
        _btnFix.Click += async (s, e) => await FixSelectedCheckAsync();
        _btnFixAll.Click += (s, e) => FixAllChecks();
        _root.SizeChanged += (s, e) =>
        {
            SizeBanner();
            UpdateChecksLayout();
        };
        _stateCard.Resize += (s, e) => CenterStateContent();
        _stateContent.SizeChanged += (s, e) => CenterStateContent();
    }

    // ----- Pure state helpers (unit tested) -----

    /// <summary>
    /// What the body shows for a tab: null shows the tab's panel (the part has entries or is finished);
    /// otherwise the state card (writing, waiting, failed, not written, or no guide at all).
    /// </summary>
    public static GuideTabState? TabStateFor(CaseDraft draft, GuidePart part, bool running, GuidePart? writingPart)
    {
        var guide = draft?.Guide;
        if (guide == null)
        {
            if (!running)
                return GuideTabState.NoGuide;

            // A run with no guide yet starts with the issues
            var first = writingPart ?? GuidePart.Issues;
            return part == first ? GuideTabState.Writing : GuideTabState.Waiting;
        }

        var state = guide.State(part);
        if (state.Status == GuidePartStatus.Ready || HasEntries(guide, part))
            return null;

        if (running)
        {
            var writing = writingPart
                ?? GuideStaleness.Order.Cast<GuidePart?>().FirstOrDefault(p => guide.State(p!.Value).Status == GuidePartStatus.Generating)
                ?? GuideStaleness.Order.Cast<GuidePart?>().FirstOrDefault(p => guide.State(p!.Value).Status != GuidePartStatus.Ready);
            if (part == writing)
                return GuideTabState.Writing;
            if (writing != null && part > writing.Value)
                return GuideTabState.Waiting;
        }

        // Generating left over from a closed run counts as not written (nothing is running now)
        return state.Status == GuidePartStatus.Failed ? GuideTabState.Failed : GuideTabState.NotWritten;
    }

    /// <summary>
    /// Parts not finished yet (Pending, Failed or left Generating), in guide order; every part when there is no guide.
    /// </summary>
    public static IReadOnlyList<GuidePart> UnfinishedParts(CaseDraft draft)
    {
        var guide = draft?.Guide;
        if (guide == null)
            return GuideStaleness.Order.ToList();

        return GuideStaleness.Order.Where(p => guide.State(p).Status != GuidePartStatus.Ready).ToList();
    }

    /// <summary>
    /// Findings shown in Checks: everything when idle; mid-run only those about finished parts, so unwritten
    /// parts do not flood the list. Issue and cast findings mostly check links from the facts ("no fact is
    /// linked to ISS-01"), so mid-run they wait until the key facts are written.
    /// </summary>
    public static List<GuideFinding> VisibleFindings(CaseDraft draft, IReadOnlyList<GuideFinding> findings, bool running)
    {
        if (!running)
            return findings.ToList();

        var guide = draft?.Guide;
        if (guide == null)
            return new List<GuideFinding>();

        var factsReady = guide.FactsState.Status == GuidePartStatus.Ready;
        return findings.Where(f => PartFor(f.Area) is { } part
            && guide.State(part).Status == GuidePartStatus.Ready
            && (factsReady || part is not (GuidePart.Issues or GuidePart.Cast))).ToList();
    }

    /// <summary>
    /// A part's failure message without the generator's trailing "Click Retry {Part}." hint: the state card and
    /// the status line add their own instruction, so the hint would otherwise appear twice.
    /// </summary>
    public static string ReasonWithoutRetryHint(string? reason) =>
        TrailingRetryHint().Replace(reason ?? string.Empty, string.Empty).Trim();

    [GeneratedRegex(@"\s*Click Retry [A-Za-z ]+\.?\s*$", RegexOptions.None, 200)]
    private static partial Regex TrailingRetryHint();

    public static GuidePart? PartFor(GuideArea area) => area switch
    {
        GuideArea.Issues => GuidePart.Issues,
        GuideArea.Cast => GuidePart.Cast,
        GuideArea.Facts => GuidePart.Facts,
        GuideArea.Values => GuidePart.Values,
        _ => null
    };

    // Cast shows its table as soon as the key characters exist (background staff are written after them)
    private static bool HasEntries(CaseGuide guide, GuidePart part) => part switch
    {
        GuidePart.Issues => guide.Issues.Count > 0,
        GuidePart.Cast => guide.Cast.Any(c => c.Tier == CastTier.Key),
        GuidePart.Facts => guide.Facts.Count > 0,
        GuidePart.Values => guide.Values.Count > 0,
        _ => false
    };

    private static int EntryCount(CaseGuide? guide, GuidePart part) => guide == null ? 0 : part switch
    {
        GuidePart.Issues => guide.Issues.Count,
        // Key + background
        GuidePart.Cast => guide.Cast.Count,
        GuidePart.Facts => guide.Facts.Count,
        GuidePart.Values => guide.Values.Count,
        _ => 0
    };

    private static string MyDocuments => Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

    // ----- IGuideHost (display members) -----

    public CaseDraft Draft => _state.Case;

    public IReadOnlyList<GuideFinding> Findings => _findings;

    public bool IsBusy => _isGenerating;

    public string WritingEntityId => _writingEntityId;

    // No-op once the step is disposed: generation continuations can resume after the window closed (WR-07)
    public void SetStatus(string text, Color color)
    {
        if (IsDisposed)
            return;

        _lblStatus.Text = text;
        _lblStatus.ForeColor = color;
    }

    public void ShowTab(GuidePart part, string? entityId = null)
    {
        if (_state == null || IsDisposed)
            return;

        SelectTab(part);
        if (!string.IsNullOrEmpty(entityId) && _panels[part].Visible)
            _panels[part].SelectEntity(entityId);
    }

    /// <summary>
    /// A panel changed the draft: revalidate and refresh captions, banner, header, Checks and dots, then autosave.
    /// The panel keeps its own rows and selection (it updates rows in place or rebuilds itself).
    /// </summary>
    public void NotifyEdited(GuidePart part)
    {
        if (_state == null || IsDisposed)
            return;

        RefreshShell();
        // Dots read the new findings
        _panels[part].Invalidate(true);
        StateChanged?.Invoke(this, EventArgs.Empty);
        QueueSave();
    }

    // ----- Refresh -----

    /// <summary>
    /// Revalidates and refreshes every part of the screen, including the selected panel's rows.
    /// </summary>
    private void RefreshAll()
    {
        if (_state == null || IsDisposed)
            return;

        RefreshShell();
        var panel = _panels[_selectedTab];
        if (panel.Visible)
            panel.RefreshFromDraft();
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    // Everything except the selected panel's rows
    private void RefreshShell()
    {
        _findings = CaseGuideValidator.Validate(Draft);
        RefreshTabCaptions();
        RefreshHeader();
        RefreshBanner();
        RefreshChecks();
        ShowSelectedTab();
    }

    private void RefreshTabCaptions()
    {
        var guide = Draft.Guide;
        foreach (var (part, toggle) in _tabButtons)
        {
            var status = guide?.State(part).Status
                ?? (_isGenerating && part == (_writingPart ?? GuidePart.Issues) ? GuidePartStatus.Generating : GuidePartStatus.Pending);
            var caption = CaseGuideUiText.TabCaption(part, EntryCount(guide, part), status);
            if (toggle.Text != caption)
                toggle.Text = caption;
            toggle.Selected = part == _selectedTab;
        }
    }

    private void RefreshHeader()
    {
        var store = _state.CaseStore;
        var saveFailed = !string.IsNullOrEmpty(store.LastSaveError);
        _lblSubtitle.Text = CaseGuideUiText.SubtitleText(Draft.FolderPath, store.LastSavedUtc?.ToLocalTime(), saveFailed, MyDocuments);
        _lblSubtitle.ForeColor = saveFailed ? Theme.Danger : Theme.TextMuted;

        // The no-guide state card already offers Generate Guide
        _btnResume.Visible = !_isGenerating && Draft.Guide != null && UnfinishedParts(Draft).Count > 0;
        _btnResume.Enabled = !_isGenerating;
    }

    private void RefreshBanner()
    {
        var draft = Draft;
        BannerContent content;
        if (draft.Guide == null)
        {
            content = new BannerContent { Variant = BannerVariant.None };
        }
        else
        {
            // Not Now hides the stale variants until the basis changes again (D-20); needs-review can still show
            var stale = GuideStaleness.StaleParts(draft);
            if (stale.Count > 0 && GuideStaleness.StaleKey(draft) == draft.Guide.DismissedStaleKey)
                stale = new List<GuidePart>();
            content = CaseGuideUiText.StaleBannerText(stale, GuideStaleness.NeedsReviewCount(draft));
        }

        // 09 S2: who depends on this guide (lowest priority; never displaces stale/needs-review banners).
        // Only cached counts are read here; the journal is never replayed on the UI thread (T-09-18)
        var dependency = draft.Guide == null
            ? new DependencyNoticeContent()
            : CaseGuideUiText.DependencyNotice(draft.Corpus?.HasPlan == true, draft.Corpus?.Approved == true,
                CorpusApproval.IsGuideStale(draft), draft.Corpus?.Generation?.EmailsWritten ?? 0);
        var planBanner = content.Variant == BannerVariant.None && dependency.Kind == DependencyNoticeKind.Changed;
        if (planBanner)
            content = new BannerContent { Variant = BannerVariant.PlanDependency, Heading = dependency.Heading, Body = dependency.Body };

        // The Changed line shows only while a higher variant holds the banner row
        var line = planBanner ? string.Empty : dependency.Line;
        if (_lblDependency.Text != line)
            _lblDependency.Text = line;
        _lblDependency.ForeColor = dependency.Kind == DependencyNoticeKind.Changed ? Theme.Warning : Theme.TextMuted;
        var showLine = line.Length > 0;
        if (_lblDependency.Visible != showLine)
            _lblDependency.Visible = showLine;

        var show = content.Variant != BannerVariant.None;
        if (show)
        {
            _lblBannerHeading.Text = content.Heading;
            _lblBannerBody.Text = content.Body;
            var stale = content.Variant is BannerVariant.StaleCore or BannerVariant.StaleDownstream;
            _btnUpdateGuide.Visible = stale;
            _btnNotNow.Visible = stale;
            var needsReview = content.Variant == BannerVariant.NeedsReview;
            _btnReviewFacts.Visible = needsReview;
            _btnMarkAllReviewed.Visible = needsReview;
            _btnBannerReviewPlan.Visible = content.Variant == BannerVariant.PlanDependency;
            _btnBannerReviewPlan.Enabled = !_isGenerating;
            _btnUpdateGuide.Enabled = !_isGenerating;
            _btnNotNow.Enabled = !_isGenerating;
            _btnReviewFacts.Enabled = !_isGenerating;
            _btnMarkAllReviewed.Enabled = !_isGenerating;
        }

        _showBanner = show;
        _bannerPrimaryShown = show && content.Variant is BannerVariant.StaleCore or BannerVariant.StaleDownstream;
        if (_banner.Visible != show)
            _banner.Visible = show;
        SizeBanner();
    }

    private void SizeBanner()
    {
        SizeDependencyLine();
        if (!_showBanner)
        {
            if (_bannerRow.Height != 0)
                _bannerRow.Height = 0;
            return;
        }

        // The text wraps beside the buttons; the card is as tall as the taller of the two plus its padding.
        // Measured from the page width (the banner spans the single root column), so it is right before the first layout
        var inner = Math.Max(1, _root.ClientSize.Width - _banner.Margin.Horizontal - _banner.Padding.Horizontal);
        var buttons = _bannerButtons.GetPreferredSize(Size.Empty);
        var textWidth = Math.Max(1, inner - buttons.Width - _bannerButtons.Margin.Horizontal);
        if (_lblBannerHeading.MaximumSize.Width != textWidth)
            _lblBannerHeading.MaximumSize = new Size(textWidth, 0);
        if (_lblBannerBody.MaximumSize.Width != textWidth)
            _lblBannerBody.MaximumSize = new Size(textWidth, 0);

        var textHeight = _lblBannerHeading.GetPreferredSize(new Size(textWidth, 0)).Height + _lblBannerHeading.Margin.Vertical
            + _lblBannerBody.GetPreferredSize(new Size(textWidth, 0)).Height;
        var height = Math.Max(textHeight, buttons.Height) + _banner.Padding.Vertical + _banner.Margin.Vertical;
        if (_bannerRow.Height != height)
            _bannerRow.Height = height;
    }

    // The dependency line wraps within header column 0 (page width less the Resume Guide flow), so it never runs under the button
    private void SizeDependencyLine()
    {
        var header = _lblDependency.Parent;
        // The header spans the single root column; measured from the page width so it is right before the first layout
        var headerWidth = _root.ClientSize.Width - (header?.Margin.Horizontal ?? 0);
        var buttons = _headerButtons.GetPreferredSize(Size.Empty);
        var width = Math.Max(1, headerWidth - buttons.Width - _headerButtons.Margin.Horizontal - _lblDependency.Margin.Horizontal);
        if (_lblDependency.MaximumSize.Width != width)
            _lblDependency.MaximumSize = new Size(width, 0);
    }

    private void RefreshChecks()
    {
        var draft = Draft;
        var visible = VisibleFindings(draft, _findings, _isGenerating);
        var showCard = draft.Guide != null || _isGenerating;
        _checksCard.Visible = showCard;

        _lblChecks.Text = CaseGuideUiText.ChecksCaption(
            CaseGuideValidator.CountErrors(visible), CaseGuideValidator.CountWarnings(visible), _isGenerating);

        // A picked check keeps its place after a fix: the next check moves up into the selection
        var keepIndex = _checksUserSelected && _gridChecks.SelectedRows.Count > 0 ? _gridChecks.SelectedRows[0].Index : -1;
        _visibleFindings = visible;
        _gridChecks.Rows.Clear();
        foreach (var finding in visible)
        {
            var index = _gridChecks.Rows.Add(
                finding.Severity == IssueSeverity.Error ? "Error" : "Warning",
                finding.Where,
                finding.Message);
            var row = _gridChecks.Rows[index];
            row.Tag = finding;
            // The painted lines end in an ellipsis when the window is narrow; the tooltip has both in full
            row.Cells[2].ToolTipText = finding.FixHint.Length > 0 ? $"{finding.Message}\n{finding.FixHint}" : finding.Message;
        }
        _gridChecks.ClearSelection(); // Keep severity colors visible on the first row
        if (keepIndex >= 0 && _gridChecks.Rows.Count > 0)
            _gridChecks.Rows[Math.Min(keepIndex, _gridChecks.Rows.Count - 1)].Selected = true;
        else
            _checksUserSelected = false;
        SetChecksSelectionVisible(_gridChecks.Focused || _checksUserSelected);

        _checksHasFindings = visible.Count > 0;
        _checksShowsNoProblems = !_checksHasFindings && !_isGenerating && draft.Guide != null;
        UpdateChecksLayout();
        UpdateFixButtons();
    }

    /// <summary>
    /// Up to two whole checks with findings, one content-sized line without. When the window is too short for even one check and a usable
    /// body (MinBodyHeight), the card keeps only its caption line and says how to see the list, so the tab's
    /// detail card never collapses into overlapping fields.
    /// </summary>
    private void UpdateChecksLayout()
    {
        if (_state == null)
            return;

        // The caption line holds the 34 px Fix buttons whenever there are findings: 10 above and 8 below, so the header is
        // as tall as the tab cards' headers (52); a one-line card without findings is balanced 12 / 12
        var buttons = _checksHasFindings && !_isGenerating;
        var captionTop = LogicalToDeviceUnits(buttons ? 10 : 12);
        var captionBottom = LogicalToDeviceUnits(buttons ? Theme.SmallGap : 12);
        if (_checksCaptionLine.Margin.Top != captionTop || _checksCaptionLine.Margin.Bottom != captionBottom)
            _checksCaptionLine.Margin = new Padding(_checksCaptionLine.Margin.Left, captionTop, _checksCaptionLine.Margin.Right, captionBottom);
        if (_checksButtons.Visible != buttons)
            _checksButtons.Visible = buttons;

        // Two whole checks when the body keeps MinBodyHeight, else one, else none (the list folds to its caption line)
        var rows = _checksCard.Visible && _checksHasFindings ? Math.Min(ChecksMaxRows, _visibleFindings.Count) : 0;
        while (rows > 0 && ChecksWouldCrowdBody(ChecksCardHeightFor(rows)))
            rows--;
        var collapse = _checksCard.Visible && _checksHasFindings && rows == 0;
        _checksListShown = _checksHasFindings && !collapse;
        _checksGridHost.Visible = _checksListShown;

        // One line beside the caption: "No problems found..." (Success), the double-click hint or the collapsed-list hint (TextMuted)
        var lineText = collapse ? ChecksCollapsedText
            : _checksShowsNoProblems ? CaseGuideUiText.NoChecksText
            : _checksHasFindings && !_isGenerating ? CaseGuideUiText.ChecksHintText
            : string.Empty;
        _lblNoChecks.Text = lineText;
        _lblNoChecks.ForeColor = _checksShowsNoProblems && !collapse ? Theme.Success : Theme.TextMuted;
        _lblNoChecks.Visible = lineText.Length > 0;

        if (rows > 0)
        {
            var height = ChecksCardHeightFor(rows);
            if (_checksRow.SizeType != SizeType.Absolute)
                _checksRow.SizeType = SizeType.Absolute;
            if ((int)_checksRow.Height != height)
                _checksRow.Height = height;
        }
        else if (_checksRow.SizeType != SizeType.AutoSize)
        {
            _checksRow.SizeType = SizeType.AutoSize;
        }
        UpdateFixButtons();
    }

    /// <summary>
    /// Root row height (device px) that shows exactly <paramref name="rows"/> whole checks: the card's top margin and 1 px
    /// padding, the caption line with its margins, the grid header, the rows and the 6 px bottom inset.
    /// </summary>
    private int ChecksCardHeightFor(int rows)
    {
        var caption = Math.Max(_lblChecks.PreferredHeight, _checksButtons.Visible ? LogicalToDeviceUnits(34) : 0)
            + _checksCaptionLine.Margin.Vertical;
        var header = _gridChecks.ColumnHeadersVisible ? _gridChecks.ColumnHeadersHeight : 0;
        return _checksCard.Margin.Vertical + _checksCard.Padding.Vertical + caption + header
            + rows * _gridChecks.RowTemplate.Height + _checksGridHost.Margin.Vertical;
    }

    /// <summary>
    /// Fix follows the selected check (Primary for an automatic fix, the matching action for a model fix, disabled when the
    /// check has none); Fix All ({n}) shows while any listed check fixes automatically.
    /// </summary>
    private void UpdateFixButtons()
    {
        if (_state == null)
            return;

        var state = CaseGuideUiText.FixButtonFor(_checksListShown ? SelectedCheck() : null, _isGenerating, _bannerPrimaryShown);
        if (_btnFix.Text != state.Text)
            _btnFix.Text = state.Text;
        var variant = state.Primary ? ButtonStyle.Primary : ButtonStyle.Default;
        if (_btnFix.Variant != variant)
            _btnFix.Variant = variant;
        _btnFix.Enabled = state.Enabled;
        // The list is folded on a short window, so there is no row to pick
        _btnFix.Visible = _checksListShown;

        var automatic = CaseGuideUiText.AutomaticFixCount(_visibleFindings);
        var fixAllText = CaseGuideUiText.FixAllButtonText(automatic);
        if (_btnFixAll.Text != fixAllText)
            _btnFixAll.Text = fixAllText;
        _btnFixAll.Visible = automatic > 0;
        _btnFixAll.Enabled = !_isGenerating;
        _btnFixAll.Margin = new Padding(_btnFix.Visible ? LogicalToDeviceUnits(Theme.SmallGap) : 0, 0, 0, 0);
    }

    // The check the user picked; null until they pick one (the grid's own first-row selection does not count)
    private GuideFinding? SelectedCheck() =>
        (_checksUserSelected || _gridChecks.Focused) && _gridChecks.SelectedRows.Count > 0
            ? _gridChecks.SelectedRows[0].Tag as GuideFinding
            : null;

    /// <summary>
    /// Picks a check as if the user clicked it: selected, drawn, scrolled into view, and the Fix button follows it.
    /// </summary>
    internal void SelectCheck(int rowIndex, bool focus)
    {
        if (rowIndex < 0 || rowIndex >= _gridChecks.Rows.Count)
            return;

        _checksUserSelected = true;
        if (focus && _gridChecks.CanFocus)
            _gridChecks.Focus();
        _gridChecks.ClearSelection();
        _gridChecks.Rows[rowIndex].Selected = true;
        if (_gridChecks.Columns.Count > 0)
            _gridChecks.CurrentCell = _gridChecks.Rows[rowIndex].Cells[0];
        SetChecksSelectionVisible(true);
        if (_gridChecks.DisplayedRowCount(false) > 0 && !_gridChecks.Rows[rowIndex].Displayed)
            _gridChecks.FirstDisplayedScrollingRowIndex = rowIndex;
        UpdateFixButtons();
    }

    // True when a Checks card of this height would leave the body shorter than MinBodyHeight
    private bool ChecksWouldCrowdBody(int checksHeight)
    {
        var rows = _root.GetRowHeights();
        if (rows.Length < 6 || _root.ClientSize.Height <= 0)
            return false;

        // The banner row is set by SizeBanner (current even before the next layout); the others do not depend on Checks
        var bodyAndChecks = _root.ClientSize.Height - rows[0] - (int)_bannerRow.Height - rows[2] - rows[5];
        return bodyAndChecks - checksHeight < LogicalToDeviceUnits(MinBodyHeight);
    }

    private void ShowSelectedTab()
    {
        var part = _selectedTab;
        var state = TabStateFor(Draft, part, _isGenerating, _writingPart);

        _bodyHost.SuspendLayout();
        foreach (var panel in _panels.Values)
        {
            var visible = state == null && panel.Part == part;
            if (panel.Visible != visible)
                panel.Visible = visible;
        }

        if (state != null)
        {
            var reason = ReasonWithoutRetryHint(Draft.Guide?.State(part).FailureMessage);
            var content = CaseGuideUiText.StateCardText(part, state.Value, reason);
            _lblStateHeading.Text = content.Heading;
            _lblStateBody.Text = content.Body;
            _stateBar.Visible = content.ShowBar;
            _btnStateAction.Text = content.ButtonText;
            _btnStateAction.Visible = content.ButtonText.Length > 0;
            _btnStateAction.Enabled = !_isGenerating;
        }
        _stateCard.Visible = state != null;
        _bodyHost.ResumeLayout(true);
        if (_stateCard.Visible)
            CenterStateContent();

        // The tabs row carries the selected panel's actions; a state card offers its own button instead
        var owner = state == null ? _panels[part] : null;
        if (owner != _actionsOwner)
        {
            _tabActions.SuspendLayout();
            _tabActions.Controls.Clear();
            if (owner != null)
            {
                var first = true;
                foreach (var action in owner.TabActions)
                {
                    action.Margin = new Padding(first ? 0 : LogicalToDeviceUnits(Theme.SmallGap), 0, 0, 0);
                    first = false;
                    _tabActions.Controls.Add(action);
                }
            }
            _tabActions.ResumeLayout(true);
            _actionsOwner = owner;
        }
    }

    private void CenterStateContent()
    {
        _stateContent.Location = new Point(
            Math.Max(0, (_stateCard.Width - _stateContent.Width) / 2),
            Math.Max(0, (_stateCard.Height - _stateContent.Height) / 2));
    }

    private void SelectTab(GuidePart part)
    {
        var previous = _panels[_selectedTab];
        if (part != _selectedTab && previous.Visible && !_isGenerating)
        {
            previous.CommitGridEdits();
            previous.CommitPendingEdits();
        }

        _selectedTab = part;
        foreach (var (p, toggle) in _tabButtons)
            toggle.Selected = p == part;

        ShowSelectedTab();
        var panel = _panels[part];
        if (panel.Visible)
            panel.RefreshFromDraft();
    }

    private void TabButton_Click(GuidePart part)
    {
        if (_state == null)
            return;

        // During a run the selection follows the part being written until the user picks a tab (U12)
        if (_isGenerating && !_userPickedTab)
            _userPickedTab = true;
        SelectTab(part);
    }

    private void SetBusy(bool busy)
    {
        _isGenerating = busy;
        _progress.Visible = busy;
        _progress.Marquee = busy;
        _btnCancel.Visible = busy;
        _btnCancel.Enabled = busy;
        foreach (var panel in _panels.Values)
            panel.SetBusy(busy);
        _btnResume.Enabled = !busy;
        _btnStateAction.Enabled = !busy;
        _btnUpdateGuide.Enabled = !busy;
        _btnNotNow.Enabled = !busy;
        _btnReviewFacts.Enabled = !busy;
        _btnMarkAllReviewed.Enabled = !busy;
        _btnBannerReviewPlan.Enabled = !busy;
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    // ----- Checks navigation -----

    private void NavigateToFinding(int rowIndex)
    {
        if (rowIndex < 0 || rowIndex >= _gridChecks.Rows.Count)
            return;
        if (_gridChecks.Rows[rowIndex].Tag is GuideFinding finding)
            NavigateToFinding(finding);
    }

    /// <summary>
    /// Opens the finding's tab (a guide-level finding opens the tab it names in GoTo), selects its entry (for an
    /// organization, its first character) and focuses the field the check is about.
    /// </summary>
    private void NavigateToFinding(GuideFinding finding)
    {
        if (_state == null || IsDisposed || PartFor(finding.GoTo ?? finding.Area) is not { } part)
            return;

        var entityId = NavigationTarget(Draft, finding);
        // SelectEntity clears a filter that hides the entity, selects its row and focuses the first detail field
        ShowTab(part, entityId.Length == 0 ? null : entityId);
        var panel = _panels[part];
        if (entityId.Length > 0 && panel.Visible)
            panel.FocusField(finding.Field);
    }

    /// <summary>
    /// Entry to select for a finding: its own entity, or for an organization finding shown on the Cast tab, the
    /// organization's first character (the party's linked character for a party finding). Empty selects nothing.
    /// </summary>
    public static string NavigationTarget(CaseDraft draft, GuideFinding finding)
    {
        if (finding == null)
            return string.Empty;
        if (finding.Area != GuideArea.Guide || finding.GoTo != GuideArea.Cast)
            return finding.EntityId ?? string.Empty;

        var guide = draft?.Guide;
        if (guide == null)
            return string.Empty;

        var orgId = finding.EntityId ?? string.Empty;
        if (orgId.Length == 0 && finding.FixTargetId.Length > 0)
        {
            // A party finding: the organization (or character) with the party's name
            var party = draft!.CoreDocument?.Parties?.FirstOrDefault(p => p != null && p.Id == finding.FixTargetId);
            if (party != null && CaseGuideQuickFix.ExactNameMember(draft, party) is { } member)
                return member.Id;
            if (party != null && CaseGuideQuickFix.ExactNameOrganization(draft, party) is { } org)
                orgId = org.Id;
        }

        return orgId.Length == 0
            ? string.Empty
            : guide.Cast.FirstOrDefault(c => c != null && c.OrganizationId == orgId)?.Id ?? string.Empty;
    }

    private void GridChecks_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode != Keys.Enter || _gridChecks.CurrentRow == null)
            return;

        // Enter navigates like a double-click instead of moving to the next row
        e.Handled = true;
        NavigateToFinding(_gridChecks.CurrentRow.Index);
    }

    private void SetChecksSelectionVisible(bool visible)
    {
        _gridChecks.DefaultCellStyle.SelectionBackColor = visible ? Theme.AccentSoft : Theme.Surface;
        _gridChecks.Invalidate();
    }

    /// <summary>
    /// Paints every Checks cell: Severity (Danger / Warning) and Where on the first line, and the Check cell as two lines,
    /// the problem in Text and how to fix it in Small TextMuted. The first lines share one baseline across the row and the
    /// pair is centered vertically; text starts at the grid's 15 px cell inset (Content x).
    /// </summary>
    private void GridChecks_CellPainting(object? sender, DataGridViewCellPaintingEventArgs e)
    {
        if (e.RowIndex < 0 || e.ColumnIndex < 0 || e.Graphics == null || e.CellStyle == null)
            return;
        if (_gridChecks.Rows[e.RowIndex].Tag is not GuideFinding finding)
            return;

        e.Paint(e.ClipBounds, DataGridViewPaintParts.Background | DataGridViewPaintParts.Border | DataGridViewPaintParts.SelectionBackground);

        // No NoPadding: stock text cells keep GDI's left padding, so painted text lands on the header text x
        const TextFormatFlags flags = TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis
            | TextFormatFlags.Left | TextFormatFlags.Top;
        var font = e.CellStyle.Font ?? _gridChecks.Font;
        var lineHeight = TextRenderer.MeasureText(e.Graphics, "Ag", font, Size.Empty, flags).Height;
        var hintHeight = TextRenderer.MeasureText(e.Graphics, "Ag", _checkHintFont, Size.Empty, flags).Height;
        var gap = LogicalToDeviceUnits(CheckLineGap);
        // Rows without a hint center their single line; every cell of the row uses the same first-line top
        var pairHeight = finding.FixHint.Length > 0 ? lineHeight + gap + hintHeight : lineHeight;
        var bounds = e.CellBounds;
        var top = bounds.Top + Math.Max(0, (bounds.Height - pairHeight) / 2);
        var left = bounds.Left + e.CellStyle.Padding.Left;
        var width = Math.Max(0, bounds.Right - left - LogicalToDeviceUnits(8));

        var (text, color) = e.ColumnIndex switch
        {
            0 => (finding.Severity == IssueSeverity.Error ? "Error" : "Warning", finding.Severity == IssueSeverity.Error ? Theme.Danger : Theme.Warning),
            1 => (finding.Where, Theme.Text),
            _ => (finding.Message, Theme.Text)
        };
        TextRenderer.DrawText(e.Graphics, text, font, new Rectangle(left, top, width, lineHeight), color, flags);
        if (e.ColumnIndex == 2 && finding.FixHint.Length > 0)
            TextRenderer.DrawText(e.Graphics, finding.FixHint, _checkHintFont,
                new Rectangle(left, top + lineHeight + gap, width, hintHeight), Theme.TextMuted, flags);

        e.Handled = true;
    }

    private void ReviewFacts()
    {
        if (_state == null || _isGenerating)
            return;

        ShowTab(GuidePart.Facts);
        if (_panels[GuidePart.Facts].Visible)
            _panels[GuidePart.Facts].ShowNeedsReview();
    }

    /// <summary>
    /// Mark All Reviewed (needs-review banner, C13): clears every fact's needs-review flag; their text is unchanged.
    /// </summary>
    private void MarkAllReviewed()
    {
        if (_state == null || _isGenerating || Draft.Guide == null)
            return;

        CommitAllEdits();
        var count = GuideStaleness.NeedsReviewCount(Draft);
        if (count == 0)
            return;

        var (title, text) = CaseGuideUiText.C13MarkAllReviewed(count);
        var answer = MessageBox.Show(FindForm(), text, title, MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2);
        if (answer != DialogResult.Yes || IsDisposed)
            return;

        var marked = CaseGuideEditing.MarkAllFactsReviewed(Draft);
        if (marked == 0)
            return;

        RefreshAll();
        QueueSave();
        SetStatus(CaseGuideUiText.MarkedReviewedText(marked), Theme.Success);
    }

    // ----- IWizardStep -----

    public void BindState(WizardState state)
    {
        _state = state;
    }

    /// <summary>
    /// Binds and shows a state without running or saving anything (render tests); simulateRun shows the run UI.
    /// </summary>
    internal void LoadForRender(WizardState state, bool simulateRun, GuidePart? writingPart, GuidePart selectedTab)
    {
        BindState(state);
        _isGenerating = simulateRun;
        _writingPart = writingPart;
        SetBusy(simulateRun);
        _selectedTab = selectedTab;
        foreach (var (p, toggle) in _tabButtons)
            toggle.Selected = p == selectedTab;
        RefreshAll();
    }
}
