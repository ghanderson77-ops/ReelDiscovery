using System.ComponentModel;
using System.Drawing.Drawing2D;
using System.Globalization;
using ReelDiscovery.Helpers;
using ReelDiscovery.Models;
using ReelDiscovery.Models.Case;
using ReelDiscovery.Models.Corpus;
using ReelDiscovery.Services.Case;
using ReelDiscovery.Services.Corpus;

namespace ReelDiscovery.UserControls;

/// <summary>
/// Which plan state LoadForRender shows (render tests only; no run is started).
/// </summary>
internal enum ReviewPlanRenderState
{
    Planning,
    Ready,
    Stale,
    ApprovedSettingsChanged,
    NoPlan,
    Failed
}

/// <summary>
/// Review Plan step (04-UI-SPEC S2): header with save state, Re-plan / Approve Plan or the approval badge, the stale /
/// settings banner, seven stat tiles, the month histogram and the counts tables (or a full-width state card), the Checks
/// card and the status strip. This file holds the layout and the display; StepReviewPlan.Run.cs holds the planning run,
/// re-plan, keep plan, approval, autosave, the wizard lifecycle and the Next gate.
/// </summary>
public partial class StepReviewPlan : UserControl, IWizardStep
{
    private enum CountsTable
    {
        Issue,
        KeyFact,
        Custodian
    }

    // Least body height (logical) worth keeping the Checks list for (UI-SPEC WizardMin note)
    private const int MinBodyHeight = 160;
    // The Checks list shows two whole findings (about the UI-SPEC's 152 logical px card)
    private const int ChecksRows = 2;
    private const string ChecksCollapsedText = "Make the window taller to see the list.";
    // UI-SPEC says 96; "Not relevant" needs a little more beside the header's sort-glyph reserve to stay on one line
    private const int NotRelevantWidth = 108;
    private const string RelevantHeaderTooltip = "An email linked to two issues counts under both.";

    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    private WizardState _state = null!;

    private TableLayoutPanel _root = null!;
    private Label _lblTitle = null!;
    private Label _lblSubtitle = null!;
    private FlowLayoutPanel _headerButtons = null!;
    private Button _btnReplan = null!;
    private Button _btnApprove = null!;
    private DotText _badge = null!;

    private CardPanel _banner = null!;
    private RowStyle _bannerRow = null!;
    private Label _lblBannerHeading = null!;
    private Label _lblBannerBody = null!;
    private FlowLayoutPanel _bannerButtons = null!;
    private Button _btnBannerReplan = null!;
    private Button _btnKeepPlan = null!;

    private TableLayoutPanel _tilesRow = null!;
    private readonly List<StatTile> _tiles = new();

    private Panel _bodyHost = null!;
    private TableLayoutPanel _bodyLayout = null!;
    private CardPanel _histogramCard = null!;
    private MonthHistogram _histogram = null!;
    private CardPanel _countsCard = null!;
    private readonly Dictionary<CountsTable, ThemedButton> _tableToggles = new();
    private readonly Dictionary<CountsTable, DataGridView> _grids = new();
    private DataGridView _gridIssues = null!;
    private DataGridView _gridFacts = null!;
    private DataGridView _gridCustodians = null!;

    private CardPanel _stateCard = null!;
    private FlowLayoutPanel _stateContent = null!;
    private Label _lblStateHeading = null!;
    private Label _lblStateBody = null!;
    private Button _btnStateAction = null!;
    private FlowLayoutPanel _stageList = null!;
    private readonly Dictionary<PlanStage, StageRow> _stageRows = new();

    private CardPanel _checksCard = null!;
    private RowStyle _checksRow = null!;
    private TableLayoutPanel _checksCaptionLine = null!;
    private Label _lblChecks = null!;
    private Label _lblChecksLine = null!;
    private Label _lblChecksHint = null!;
    private Panel _checksGridHost = null!;
    private DataGridView _gridChecks = null!;

    private Label _lblStatus = null!;
    private SlimProgressBar _progress = null!;
    private Button _btnCancel = null!;

    // Logical corner radius inset of the full-bleed cards (deferred-items 04-12; 04-17 precedent)
    private int _cornerInset;

    private bool _isPlanning;
    private bool _isApproving;
    private bool _stateCardShown = true;
    private PlanBannerVariant _bannerVariant = PlanBannerVariant.None;
    private CountsTable _selectedTable = CountsTable.Issue;
    private List<PlanFinding> _findings = new();
    private bool _checksListShown;

    public string StepTitle => "Review Plan";
    public string NextButtonText => "Next >";
    public bool CanMoveNext => !_isPlanning && !_isApproving;
    public bool CanMoveBack => !_isPlanning && !_isApproving;

    /// <summary>
    /// True while a planning run is active (closing asks C-P2).
    /// </summary>
    public bool IsPlanning => _isPlanning;

    public event EventHandler? StateChanged;

    public StepReviewPlan()
    {
        InitializeUI();
    }

    private CaseDraft Draft => _state.Case;

    private static string MyDocuments => Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

    // ----- Layout -----

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
        _cornerInset = LogicalToDeviceUnits(8);

        _root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 6,
            Padding = new Padding(0),
            BackColor = Theme.Background
        };
        _root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        _root.RowStyles.Add(new RowStyle(SizeType.AutoSize));     // 0: Header
        // Sized in SizeBanner from the page width: the wrapping text makes the card's own preferred size unusable
        _bannerRow = new RowStyle(SizeType.Absolute, 0);
        _root.RowStyles.Add(_bannerRow);                          // 1: Banner
        _root.RowStyles.Add(new RowStyle(SizeType.AutoSize));     // 2: Tiles
        _root.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); // 3: Body (histogram + counts, or the state card)
        // Absolute (two whole findings) with findings, AutoSize with none or folded; set in UpdateChecksLayout
        _checksRow = new RowStyle(SizeType.AutoSize);
        _root.RowStyles.Add(_checksRow);                          // 4: Checks card
        _root.RowStyles.Add(new RowStyle(SizeType.AutoSize));     // 5: Status strip

        BuildHeader(gap, smallGap, buttonHeight);
        BuildBanner(gap, smallGap, pad, buttonHeight);
        BuildTiles(gap);
        BuildBody(gap, smallGap, inset, buttonHeight);
        BuildChecksCard(gap, smallGap, inset);
        BuildStatusStrip(gap, buttonHeight);

        this.Controls.Add(_root);

        // Wire handlers only after every control exists (handlers read other controls)
        foreach (var (table, toggle) in _tableToggles)
            toggle.Click += (s, e) => SelectTable(table);
        foreach (var grid in _grids.Values)
        {
            grid.SortCompare += Grid_SortCompare;
            grid.Sorted += (s, e) => HideSortGlyphs((DataGridView)s!);
        }
        _gridChecks.CellDoubleClick += (s, e) => NavigateToFinding(e.RowIndex);
        _gridChecks.KeyDown += GridChecks_KeyDown;
        _btnReplan.Click += async (s, e) => await ReplanAsync();
        _btnBannerReplan.Click += async (s, e) => await ReplanAsync();
        _btnKeepPlan.Click += (s, e) => KeepPlan();
        _btnApprove.Click += async (s, e) => await ApproveAsync();
        _btnStateAction.Click += async (s, e) => await PlanFromStateCardAsync();
        _btnCancel.Click += BtnCancel_Click;
        _root.SizeChanged += (s, e) =>
        {
            SizeBanner();
            UpdateChecksLayout();
        };
        _stateCard.Resize += (s, e) => CenterStateContent();
        _stateContent.SizeChanged += (s, e) => CenterStateContent();
    }

    private void BuildHeader(int gap, int smallGap, int buttonHeight)
    {
        var headerLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 2,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Margin = new Padding(0, 0, 0, gap)
        };
        headerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        headerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        headerLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // 0: Title
        headerLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // 1: Subtitle

        _lblTitle = new Label
        {
            Text = "Review Plan",
            AutoSize = false,
            AutoEllipsis = true,
            UseMnemonic = false,
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

        // Right side, 8 apart; the last visible item ends at the cards' right edge (no right margin)
        _headerButtons = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Anchor = AnchorStyles.Right,
            Margin = new Padding(gap, 0, 0, 0),
            Padding = new Padding(0)
        };
        _btnReplan = ButtonHelper.CreateButton("Re-plan...", 0, buttonHeight, ButtonStyle.Default);
        _btnReplan.Margin = new Padding(0);
        _btnApprove = ButtonHelper.CreateButton("Approve Plan", 0, buttonHeight, ButtonStyle.Primary);
        _btnApprove.Margin = new Padding(smallGap, 0, 0, 0);
        _badge = new DotText
        {
            DotColor = Theme.Success,
            ForeColor = Theme.Success,
            Font = Theme.LabelFont(),
            MinHeightLogical = 34,
            BackColor = Theme.Background,
            Margin = new Padding(smallGap, 0, 0, 0),
            Visible = false
        };
        _headerButtons.Controls.Add(_btnReplan);
        _headerButtons.Controls.Add(_btnApprove);
        _headerButtons.Controls.Add(_badge);

        headerLayout.Controls.Add(_lblTitle, 0, 0);
        headerLayout.Controls.Add(_lblSubtitle, 0, 1);
        headerLayout.Controls.Add(_headerButtons, 1, 0);
        headerLayout.SetRowSpan(_headerButtons, 2);
        _root.Controls.Add(headerLayout, 0, 0);
    }

    private void BuildBanner(int gap, int smallGap, int pad, int buttonHeight)
    {
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
        _btnBannerReplan = ButtonHelper.CreateButton("Re-plan...", 0, buttonHeight, ButtonStyle.Primary);
        _btnBannerReplan.Margin = new Padding(0, 0, smallGap, 0);
        _btnKeepPlan = ButtonHelper.CreateButton("Keep This Plan", 0, buttonHeight, ButtonStyle.Default);
        _btnKeepPlan.Margin = new Padding(0);
        _bannerButtons.Controls.Add(_btnBannerReplan);
        _bannerButtons.Controls.Add(_btnKeepPlan);
        bannerLayout.Controls.Add(_lblBannerHeading, 0, 0);
        bannerLayout.Controls.Add(_lblBannerBody, 0, 1);
        bannerLayout.Controls.Add(_bannerButtons, 1, 0);
        bannerLayout.SetRowSpan(_bannerButtons, 2);
        _banner.Controls.Add(bannerLayout);
        _root.Controls.Add(_banner, 0, 1);
    }

    private void BuildTiles(int gap)
    {
        // Seven equal columns; tiles 4 + 4 apart, the outer tiles flush with the cards below
        _tilesRow = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 7,
            RowCount = 1,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Margin = new Padding(0, 0, 0, gap),
            Padding = new Padding(0),
            BackColor = Theme.Background,
            Visible = false
        };
        _tilesRow.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // 0: Tiles (Dock Fill, so one height)
        var half = LogicalToDeviceUnits(Theme.SmallGap) / 2;
        for (var i = 0; i < 7; i++)
        {
            _tilesRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / 7));
            var tile = new StatTile
            {
                Dock = DockStyle.Fill,
                Margin = new Padding(i == 0 ? 0 : half, 0, i == 6 ? 0 : half, 0)
            };
            _tiles.Add(tile);
            _tilesRow.Controls.Add(tile, i, 0);
        }
        _root.Controls.Add(_tilesRow, 0, 2);
    }

    private void BuildBody(int gap, int smallGap, int inset, int buttonHeight)
    {
        _bodyHost = new Panel { Dock = DockStyle.Fill, Margin = new Padding(0), BackColor = Theme.Background };

        _bodyLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Margin = new Padding(0),
            Padding = new Padding(0),
            BackColor = Theme.Background,
            Visible = false
        };
        _bodyLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        _bodyLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        _bodyLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); // 0: Histogram card | counts card

        _histogramCard = BuildHistogramCard(inset);
        _histogramCard.Margin = new Padding(0, 0, smallGap, 0);
        _countsCard = BuildCountsCard(inset);
        _countsCard.Margin = new Padding(smallGap, 0, 0, 0);
        _bodyLayout.Controls.Add(_histogramCard, 0, 0);
        _bodyLayout.Controls.Add(_countsCard, 1, 0);
        _bodyHost.Controls.Add(_bodyLayout);

        // State card (planning / no plan / failed / unreadable); replaces the tiles, the body and the Checks card
        _stateCard = new CardPanel { Dock = DockStyle.Fill, Visible = true };
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
            BackColor = Theme.Surface,
            Margin = new Padding(0, 0, 0, smallGap)
        };
        _lblStateBody = new Label
        {
            AutoSize = true,
            UseMnemonic = false,
            MaximumSize = new Size(LogicalToDeviceUnits(520), 0),
            Font = Theme.BodyFont(),
            ForeColor = Theme.TextMuted,
            BackColor = Theme.Surface,
            Margin = new Padding(0, 0, 0, LogicalToDeviceUnits(20))
        };
        _btnStateAction = ButtonHelper.CreateButton("Plan Corpus", 0, buttonHeight, ButtonStyle.Primary);
        _btnStateAction.Margin = new Padding(0);

        // Stage checklist: one 28 px row per stage, 360 wide
        _stageList = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Margin = new Padding(0),
            Padding = new Padding(0),
            BackColor = Theme.Surface,
            Visible = false
        };
        foreach (var stage in new[] { PlanStage.Allocating, PlanStage.PlanningRelevant, PlanStage.PlanningOther, PlanStage.WritingProtocol, PlanStage.Validating })
        {
            var row = new StageRow
            {
                StageName = CorpusUiText.StageName(stage),
                Width = LogicalToDeviceUnits(360),
                Height = LogicalToDeviceUnits(28),
                Margin = new Padding(0),
                BackColor = Theme.Surface
            };
            _stageRows[stage] = row;
            _stageList.Controls.Add(row);
        }

        _stateContent.Controls.Add(_lblStateHeading);
        _stateContent.Controls.Add(_lblStateBody);
        _stateContent.Controls.Add(_btnStateAction);
        _stateContent.Controls.Add(_stageList);
        _stateCard.Controls.Add(_stateContent);
        _bodyHost.Controls.Add(_stateCard);
        _root.Controls.Add(_bodyHost, 0, 3);
    }

    /// <summary>
    /// Full-bleed card: 1 px sides, top and bottom inset by the corner radius so white content never covers the rounded
    /// corners (deferred-items 04-12). Header margins subtract that inset, so text still sits 15/14 from the border.
    /// </summary>
    private CardPanel NewFullBleedCard() => new()
    {
        Dock = DockStyle.Fill,
        Padding = new Padding(1, _cornerInset, 1, _cornerInset),
        BackColor = Theme.Surface
    };

    private Padding CardHeaderMargin(int inset) =>
        new(inset, Math.Max(0, LogicalToDeviceUnits(14) - _cornerInset + 1), inset, LogicalToDeviceUnits(10));

    private static TableLayoutPanel NewCardLayout() => new()
    {
        Dock = DockStyle.Fill,
        ColumnCount = 1,
        RowCount = 2,
        Margin = new Padding(0),
        Padding = new Padding(0),
        BackColor = Theme.Surface
    };

    private TableLayoutPanel NewCardHeader(int inset) => new()
    {
        Dock = DockStyle.Fill,
        ColumnCount = 2,
        RowCount = 1,
        AutoSize = true,
        AutoSizeMode = AutoSizeMode.GrowAndShrink,
        Margin = CardHeaderMargin(inset),
        Padding = new Padding(0),
        BackColor = Theme.Surface
    };

    private static Label CardCaption(string text) => new()
    {
        Text = text,
        AutoSize = true,
        UseMnemonic = false,
        Anchor = AnchorStyles.Left,
        Margin = new Padding(0),
        Font = Theme.LabelFont(),
        ForeColor = Theme.Text,
        BackColor = Theme.Surface
    };

    private CardPanel BuildHistogramCard(int inset)
    {
        var card = NewFullBleedCard();
        var layout = NewCardLayout();
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));     // 0: Header (caption | legend)
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); // 1: Histogram

        var header = NewCardHeader(inset);
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        header.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // 0: Caption | legend
        var legend = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Anchor = AnchorStyles.Right,
            Margin = new Padding(LogicalToDeviceUnits(Theme.SmallGap), 0, 0, 0),
            Padding = new Padding(0),
            BackColor = Theme.Surface
        };
        // Legend items are 28 tall (the counts card's toggle height), so both card headers are one height
        legend.Controls.Add(new DotText
        {
            Text = "Relevant",
            DotColor = Theme.Accent,
            ForeColor = Theme.TextMuted,
            Font = Theme.SmallFont(),
            MinHeightLogical = 28,
            BackColor = Theme.Surface,
            Margin = new Padding(0, 0, LogicalToDeviceUnits(12), 0)
        });
        legend.Controls.Add(new DotText
        {
            Text = "Not relevant",
            DotColor = Theme.TextDisabled,
            ForeColor = Theme.TextMuted,
            Font = Theme.SmallFont(),
            MinHeightLogical = 28,
            BackColor = Theme.Surface,
            Margin = new Padding(0)
        });
        header.Controls.Add(CardCaption("Emails by month"), 0, 0);
        header.Controls.Add(legend, 1, 0);
        layout.Controls.Add(header, 0, 0);

        _histogram = new MonthHistogram
        {
            Dock = DockStyle.Fill,
            // The card's bottom inset (corner radius) already exceeds the 6 px bottom margin
            Margin = new Padding(0, 0, 0, Math.Max(0, LogicalToDeviceUnits(6) + 1 - _cornerInset)),
            TabStop = false
        };
        layout.Controls.Add(_histogram, 0, 1);
        card.Controls.Add(layout);
        return card;
    }

    private CardPanel BuildCountsCard(int inset)
    {
        var card = NewFullBleedCard();
        var layout = NewCardLayout();
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));     // 0: Header (caption | table toggles)
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); // 1: Grid host

        var header = NewCardHeader(inset);
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        header.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // 0: Caption | toggles
        var toggles = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Anchor = AnchorStyles.Right,
            Margin = new Padding(LogicalToDeviceUnits(Theme.SmallGap), 0, 0, 0),
            Padding = new Padding(0),
            BackColor = Theme.Surface
        };
        var compact = LogicalToDeviceUnits(28);
        foreach (var (table, text) in new[] { (CountsTable.Issue, "Issue"), (CountsTable.KeyFact, "Key fact"), (CountsTable.Custodian, "Custodian") })
        {
            var toggle = (ThemedButton)ButtonHelper.CreateButton(text, 0, compact, ButtonStyle.Toggle);
            toggle.Selected = table == _selectedTable;
            _tableToggles[table] = toggle;
        }
        // One segmented control: equal widths and heights, 4 apart
        var toggleWidth = _tableToggles.Values.Max(t => t.Width);
        var toggleHeight = _tableToggles.Values.Max(t => t.Height);
        var last = CountsTable.Custodian;
        foreach (var (table, toggle) in _tableToggles)
        {
            toggle.Size = new Size(toggleWidth, toggleHeight);
            toggle.Margin = new Padding(0, 0, table == last ? 0 : LogicalToDeviceUnits(4), 0);
            toggles.Controls.Add(toggle);
        }
        header.Controls.Add(CardCaption("Counts"), 0, 0);
        header.Controls.Add(toggles, 1, 0);
        layout.Controls.Add(header, 0, 0);

        var host = new Panel
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 0, 0, Math.Max(0, LogicalToDeviceUnits(6) + 1 - _cornerInset)),
            BackColor = Theme.Surface
        };
        _gridIssues = NewCountsGrid();
        GridStyle.Apply(_gridIssues, this);
        AddTextColumn(_gridIssues, "Id", "ID", 64);
        AddFillColumn(_gridIssues, "Issue", "Issue", 120, 100);
        AddNumberColumn(_gridIssues, "Relevant", "Relevant", 80).HeaderCell.ToolTipText = RelevantHeaderTooltip;
        AddNumberColumn(_gridIssues, "NotRelevant", "Not relevant", NotRelevantWidth);
        AddNumberColumn(_gridIssues, "Total", "Total", 64);

        _gridFacts = NewCountsGrid();
        GridStyle.Apply(_gridFacts, this);
        AddTextColumn(_gridFacts, "Date", "Date", 96);
        AddFillColumn(_gridFacts, "Fact", "Fact", 120, 100);
        AddNumberColumn(_gridFacts, "Threads", "Threads", 72);
        AddNumberColumn(_gridFacts, "Emails", "Emails", 72);
        AddNumberColumn(_gridFacts, "Hot", "Hot", 56);

        _gridCustodians = NewCountsGrid();
        GridStyle.Apply(_gridCustodians, this);
        AddFillColumn(_gridCustodians, "Custodian", "Custodian", 140, 100);
        AddNumberColumn(_gridCustodians, "Relevant", "Relevant", 80);
        AddNumberColumn(_gridCustodians, "NotRelevant", "Not relevant", NotRelevantWidth);
        AddNumberColumn(_gridCustodians, "Total", "Total", 64);

        _grids[CountsTable.Issue] = _gridIssues;
        _grids[CountsTable.KeyFact] = _gridFacts;
        _grids[CountsTable.Custodian] = _gridCustodians;
        foreach (var (table, grid) in _grids)
        {
            grid.Visible = table == _selectedTable;
            host.Controls.Add(grid);
        }
        layout.Controls.Add(host, 0, 1);
        card.Controls.Add(layout);
        return card;
    }

    private DataGridView NewCountsGrid()
    {
        // GridStyle.Apply runs on each grid right after this (BuildCountsCard)
        var grid = new DataGridView { Dock = DockStyle.Fill, ReadOnly = true, Margin = new Padding(0) };
        grid.AllowUserToResizeColumns = false;
        grid.ScrollBars = ScrollBars.Vertical;
        // One-line headers: a wrapped "Not relevant" header drew off the right-aligned number line
        grid.ColumnHeadersDefaultCellStyle.WrapMode = DataGridViewTriState.False;
        return grid;
    }

    private DataGridViewTextBoxColumn AddTextColumn(DataGridView grid, string name, string header, int width)
    {
        var column = new DataGridViewTextBoxColumn
        {
            Name = name,
            HeaderText = header,
            Width = LogicalToDeviceUnits(width),
            AutoSizeMode = DataGridViewAutoSizeColumnMode.None,
            SortMode = DataGridViewColumnSortMode.Automatic
        };
        grid.Columns.Add(column);
        return column;
    }

    private DataGridViewTextBoxColumn AddFillColumn(DataGridView grid, string name, string header, int minWidth, float weight)
    {
        var column = new DataGridViewTextBoxColumn
        {
            Name = name,
            HeaderText = header,
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
            FillWeight = weight,
            MinimumWidth = LogicalToDeviceUnits(minWidth),
            SortMode = DataGridViewColumnSortMode.Automatic
        };
        grid.Columns.Add(column);
        return column;
    }

    /// <summary>
    /// Right-aligned int column: header and cell text end 15 inside the grid (16 from the card's outer edge, UI-SPEC rule 15).
    /// </summary>
    private DataGridViewTextBoxColumn AddNumberColumn(DataGridView grid, string name, string header, int width)
    {
        var column = AddTextColumn(grid, name, header, width);
        column.ValueType = typeof(int);
        column.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
        column.DefaultCellStyle.Format = "N0";
        column.DefaultCellStyle.FormatProvider = Invariant;
        column.DefaultCellStyle.Padding = new Padding(0, 0, LogicalToDeviceUnits(15), 0);
        column.HeaderCell.Style.Alignment = DataGridViewContentAlignment.MiddleRight;
        column.HeaderCell.Style.Padding = new Padding(0, LogicalToDeviceUnits(6), LogicalToDeviceUnits(15) - GridStyle.HeaderTextMargin, LogicalToDeviceUnits(6));
        return column;
    }

    private void BuildChecksCard(int gap, int smallGap, int inset)
    {
        _checksCard = NewFullBleedCard();
        _checksCard.AutoSize = true;
        _checksCard.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        _checksCard.Margin = new Padding(0, gap, 0, 0);
        _checksCard.Visible = false;
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

        // Caption | no-findings or folded line | hint; margins 15, 12, 16, 8 from the border (less the corner inset)
        _checksCaptionLine = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 1,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(0),
            Margin = new Padding(inset, Math.Max(0, LogicalToDeviceUnits(12) - _cornerInset + 1), LogicalToDeviceUnits(16) - 1, smallGap),
            BackColor = Theme.Surface
        };
        _checksCaptionLine.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _checksCaptionLine.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        _checksCaptionLine.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _checksCaptionLine.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // 0: Caption | line | hint
        _lblChecks = new Label
        {
            Text = "Checks",
            AutoSize = true,
            UseMnemonic = false,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(0),
            Font = Theme.LabelFont(),
            ForeColor = Theme.Text,
            BackColor = Theme.Surface
        };
        _lblChecksLine = new Label
        {
            AutoSize = false,
            AutoEllipsis = true,
            UseMnemonic = false,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            Margin = new Padding(smallGap, 0, 0, 0),
            Font = Theme.SmallFont(),
            ForeColor = Theme.Success,
            BackColor = Theme.Surface,
            Visible = false
        };
        _lblChecksLine.Height = _lblChecksLine.PreferredHeight;
        _lblChecksHint = new Label
        {
            Text = CorpusUiText.ChecksHint,
            AutoSize = true,
            UseMnemonic = false,
            Anchor = AnchorStyles.Right,
            Margin = new Padding(smallGap, 0, 0, 0),
            Font = Theme.SmallFont(),
            ForeColor = Theme.TextMuted,
            BackColor = Theme.Surface
        };
        _checksCaptionLine.Controls.Add(_lblChecks, 0, 0);
        _checksCaptionLine.Controls.Add(_lblChecksLine, 1, 0);
        _checksCaptionLine.Controls.Add(_lblChecksHint, 2, 0);
        checksLayout.Controls.Add(_checksCaptionLine, 0, 0);

        _checksGridHost = new Panel { Dock = DockStyle.Fill, Margin = new Padding(0), BackColor = Theme.Surface };
        _gridChecks = new DataGridView { Dock = DockStyle.Fill, ReadOnly = true };
        GridStyle.Apply(_gridChecks, this);
        _gridChecks.AllowUserToResizeColumns = false;
        _gridChecks.ScrollBars = ScrollBars.Vertical;
        _gridChecks.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Where",
            HeaderText = "Where",
            Width = LogicalToDeviceUnits(176),
            AutoSizeMode = DataGridViewAutoSizeColumnMode.None,
            SortMode = DataGridViewColumnSortMode.NotSortable
        });
        _gridChecks.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Check",
            HeaderText = "Check",
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
            FillWeight = 55,
            MinimumWidth = LogicalToDeviceUnits(160),
            SortMode = DataGridViewColumnSortMode.NotSortable
        });
        _gridChecks.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Fix",
            HeaderText = "How to fix",
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
            FillWeight = 45,
            MinimumWidth = LogicalToDeviceUnits(140),
            SortMode = DataGridViewColumnSortMode.NotSortable
        });
        foreach (DataGridViewColumn column in _gridChecks.Columns)
            column.DefaultCellStyle.ForeColor = column.Name == "Where" ? Theme.Warning : Theme.Text;
        _checksGridHost.Controls.Add(_gridChecks);
        checksLayout.Controls.Add(_checksGridHost, 0, 1);
        _checksCard.Controls.Add(checksLayout);
        _root.Controls.Add(_checksCard, 0, 4);
    }

    private void BuildStatusStrip(int gap, int buttonHeight)
    {
        var strip = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 1,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Margin = new Padding(0, LogicalToDeviceUnits(12), 0, 0)
        };
        strip.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        strip.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        strip.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        strip.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // 0: Status | bar | Cancel
        _btnCancel = ButtonHelper.CreateButton("Cancel Planning", 0, buttonHeight, ButtonStyle.Danger);
        _btnCancel.Margin = new Padding(0);
        _btnCancel.Visible = false;
        _lblStatus = new Label
        {
            Dock = DockStyle.Fill,
            MinimumSize = new Size(0, Math.Max(buttonHeight, _btnCancel.Height)),
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
        strip.Controls.Add(_lblStatus, 0, 0);
        strip.Controls.Add(_progress, 1, 0);
        strip.Controls.Add(_btnCancel, 2, 0);
        _root.Controls.Add(strip, 0, 5);
    }

    // ----- Display -----

    /// <summary>
    /// Shows the plan summary: tiles, histogram, the three count tables and the Checks list. Formatting only.
    /// </summary>
    private void ShowPlan(CorpusPlan plan, List<PlanFinding> findings)
    {
        var guide = Draft.Guide ?? new CaseGuide();
        var summary = PlanSummary.From(plan, guide);
        var cost = GenerationCostEstimator.Estimate(plan, _state.StoryModelConfig);

        var kinds = new[] { TileKind.Emails, TileKind.Threads, TileKind.Relevant, TileKind.Hot, TileKind.Privileged, TileKind.Attachments, TileKind.Cost };
        for (var i = 0; i < kinds.Length; i++)
            _tiles[i].SetContent(CorpusUiText.Tile(kinds[i], summary, cost));

        _histogram.Buckets = summary.Months;
        FillCountGrids(summary, guide);

        _findings = findings ?? new List<PlanFinding>();
        FillChecks();

        SetStateCardShown(false);
        RefreshHeader();
    }

    private void FillCountGrids(PlanSummary summary, CaseGuide guide)
    {
        _gridIssues.Rows.Clear();
        foreach (var row in summary.ByIssue)
        {
            var index = _gridIssues.Rows.Add(row.IssueId, row.Title, row.Relevant, row.NotRelevant, row.Total);
            _gridIssues.Rows[index].Tag = row.IssueId;
            _gridIssues.Rows[index].Cells["Issue"].ToolTipText = $"{row.IssueId} · {row.Title}";
        }

        _gridFacts.Rows.Clear();
        foreach (var row in summary.ByFact)
        {
            var index = _gridFacts.Rows.Add(row.Date, row.Title, row.Threads, row.Emails, row.Hot);
            _gridFacts.Rows[index].Tag = row.FactId;
            _gridFacts.Rows[index].Cells["Fact"].ToolTipText = $"{row.FactId} · {row.Title}";
        }

        _gridCustodians.Rows.Clear();
        foreach (var row in summary.ByCustodian)
        {
            var index = _gridCustodians.Rows.Add(row.Name, row.Relevant, row.NotRelevant, row.Total);
            _gridCustodians.Rows[index].Tag = row.CastId;
            var detail = string.Join(" · ", new[] { row.Email, row.Organization }.Where(s => !string.IsNullOrWhiteSpace(s)));
            _gridCustodians.Rows[index].Cells["Custodian"].ToolTipText = detail.Length > 0 ? $"{row.Name}{Environment.NewLine}{detail}" : row.Name;
        }

        // Default sorts (UI-SPEC): ID ascending, Date ascending, Total descending
        _gridIssues.Sort(_gridIssues.Columns["Id"]!, ListSortDirection.Ascending);
        _gridFacts.Sort(_gridFacts.Columns["Date"]!, ListSortDirection.Ascending);
        _gridCustodians.Sort(_gridCustodians.Columns["Total"]!, ListSortDirection.Descending);
        foreach (var grid in _grids.Values)
            grid.ClearSelection();
    }

    // Numeric columns sort by value; text by ordinal-ignore-case; ties keep the row order (stable)
    private static void Grid_SortCompare(object? sender, DataGridViewSortCompareEventArgs e)
    {
        int result;
        if (e.CellValue1 is int a && e.CellValue2 is int b)
            result = a.CompareTo(b);
        else
            result = string.Compare(e.CellValue1?.ToString(), e.CellValue2?.ToString(), StringComparison.OrdinalIgnoreCase);
        if (result == 0)
            result = e.RowIndex1.CompareTo(e.RowIndex2);
        e.SortResult = result;
        e.Handled = true;
    }

    // The sort glyph would push right-aligned header text off the 16 px Content x line (rule 15)
    private static void HideSortGlyphs(DataGridView grid)
    {
        foreach (DataGridViewColumn column in grid.Columns)
            column.HeaderCell.SortGlyphDirection = SortOrder.None;
        grid.ClearSelection();
    }

    private void FillChecks()
    {
        _gridChecks.Rows.Clear();
        foreach (var finding in _findings)
        {
            var index = _gridChecks.Rows.Add(finding.Where, finding.Message, finding.FixHint);
            var row = _gridChecks.Rows[index];
            row.Tag = finding;
            row.Cells["Check"].ToolTipText = finding.Message;
            row.Cells["Fix"].ToolTipText = finding.FixHint;
        }
        _gridChecks.ClearSelection();

        _lblChecks.Text = CorpusUiText.ChecksCaption(_findings.Count);
        UpdateChecksLayout();
    }

    /// <summary>
    /// Two whole findings with findings (about 152 logical px), one content-sized line without. When the body would be
    /// shorter than MinBodyHeight, the card folds to its caption line with the "make the window taller" hint.
    /// </summary>
    private void UpdateChecksLayout()
    {
        if (_state == null)
            return;

        var hasFindings = _findings.Count > 0;
        var fold = hasFindings && _checksCard.Visible && ChecksWouldCrowdBody(ChecksCardHeight());
        _checksListShown = hasFindings && !fold;
        if (_checksGridHost.Visible != _checksListShown)
            _checksGridHost.Visible = _checksListShown;

        // Balanced 12 / 12 around a one-line card; 8 under the caption when the list follows
        var bottom = _checksListShown
            ? LogicalToDeviceUnits(Theme.SmallGap)
            : Math.Max(0, LogicalToDeviceUnits(12) - _cornerInset + 1);
        if (_checksCaptionLine.Margin.Bottom != bottom)
            _checksCaptionLine.Margin = new Padding(_checksCaptionLine.Margin.Left, _checksCaptionLine.Margin.Top, _checksCaptionLine.Margin.Right, bottom);

        var line = fold ? ChecksCollapsedText : hasFindings ? string.Empty : CorpusUiText.NoChecksText;
        _lblChecksLine.Text = line;
        _lblChecksLine.ForeColor = hasFindings ? Theme.TextMuted : Theme.Success;
        _lblChecksLine.Visible = line.Length > 0;
        _lblChecksHint.Visible = hasFindings;

        if (_checksListShown)
        {
            var height = ChecksCardHeight();
            if (_checksRow.SizeType != SizeType.Absolute)
                _checksRow.SizeType = SizeType.Absolute;
            if ((int)_checksRow.Height != height)
                _checksRow.Height = height;
        }
        else if (_checksRow.SizeType != SizeType.AutoSize)
        {
            _checksRow.SizeType = SizeType.AutoSize;
        }
    }

    /// <summary>
    /// Root row height (device px) for two whole findings: top margin, card insets, caption line, grid header and rows.
    /// </summary>
    private int ChecksCardHeight()
    {
        var caption = _lblChecks.PreferredHeight + _checksCaptionLine.Margin.Top + LogicalToDeviceUnits(Theme.SmallGap);
        var header = _gridChecks.ColumnHeadersVisible ? _gridChecks.ColumnHeadersHeight : 0;
        return _checksCard.Margin.Vertical + _checksCard.Padding.Vertical + caption + header
            + ChecksRows * _gridChecks.RowTemplate.Height + _checksGridHost.Margin.Vertical;
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

    /// <summary>
    /// Shows a full-width state card (planning, no plan, failed, unreadable) in place of the tiles, body and Checks.
    /// </summary>
    private void ShowStateCard(PlanStateCard card, string reason = "")
    {
        var content = CorpusUiText.StateCard(card, reason);
        _lblStateHeading.Text = content.Heading;
        _lblStateBody.Text = content.Body;
        _btnStateAction.Text = content.ButtonText.Length > 0 ? content.ButtonText : "Plan Corpus";
        _btnStateAction.Visible = content.ButtonText.Length > 0;
        _stageList.Visible = card == PlanStateCard.Planning;
        if (card == PlanStateCard.Planning)
            UpdateStageChecklist(PlanStage.Allocating, 0, 0);

        ShowBanner(PlanBannerVariant.None);
        SetStateCardShown(true);
        RefreshHeader();
    }

    private void SetStateCardShown(bool shown)
    {
        _stateCardShown = shown;
        _root.SuspendLayout();
        _bodyLayout.Visible = !shown;
        _stateCard.Visible = shown;
        _tilesRow.Visible = !shown;
        _checksCard.Visible = !shown;
        _root.ResumeLayout(true);
        UpdateChecksLayout();
        if (shown)
            CenterStateContent();
    }

    private void CenterStateContent()
    {
        _stateContent.Location = new Point(
            Math.Max(0, (_stateCard.Width - _stateContent.Width) / 2),
            Math.Max(0, (_stateCard.Height - _stateContent.Height) / 2));
    }

    /// <summary>
    /// Banner variant (or None). Approve is disabled for the stale-guide banner; the header Re-plan hides while a banner shows.
    /// </summary>
    private void ShowBanner(PlanBannerVariant variant)
    {
        var plan = Draft?.Corpus?.Plan;
        var content = plan == null ? null
            : CorpusUiText.PlanBannerText(variant, plan.Settings.TotalEmails, plan.Settings.RelevantPercent,
                Draft!.Corpus!.Approved);
        _bannerVariant = content == null ? PlanBannerVariant.None : variant;
        if (content != null)
        {
            _lblBannerHeading.Text = content.Heading;
            _lblBannerBody.Text = content.Body;
            _btnBannerReplan.Text = content.PrimaryText;
            _btnKeepPlan.Text = content.SecondaryText;
        }

        var show = _bannerVariant != PlanBannerVariant.None;
        if (_banner.Visible != show)
            _banner.Visible = show;
        SizeBanner();
        RefreshHeader();
        UpdateChecksLayout();
    }

    private void SizeBanner()
    {
        if (_bannerVariant == PlanBannerVariant.None)
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

    /// <summary>
    /// Stage checklist: stages before <paramref name="stage"/> done, the current one with "{done} of {total}" when known.
    /// </summary>
    private void UpdateStageChecklist(PlanStage stage, int done, int total)
    {
        foreach (var (s, row) in _stageRows)
        {
            var state = s < stage ? StageRowState.Done : s == stage ? StageRowState.Current : StageRowState.Pending;
            row.Update(state, s == stage && total > 0 ? CorpusUiText.StageProgress(done, total) : string.Empty);
        }
    }

    /// <summary>
    /// Header per UI-SPEC: nothing while planning, approving or on a state card; with a banner, Approve (disabled when
    /// stale) or the badge; otherwise Re-plan with Approve or the badge.
    /// </summary>
    private void RefreshHeader()
    {
        if (_state == null)
            return;

        var store = _state.CaseStore;
        var saveFailed = !string.IsNullOrEmpty(store.LastSaveError);
        _lblSubtitle.Text = CaseGuideUiText.SubtitleText(Draft.FolderPath, store.LastSavedUtc?.ToLocalTime(), saveFailed, MyDocuments);
        _lblSubtitle.ForeColor = saveFailed ? Theme.Danger : Theme.TextMuted;

        var corpus = Draft.Corpus;
        var hasPlan = corpus?.Plan != null && !_stateCardShown;
        var busy = _isPlanning || _isApproving;
        var approved = corpus?.Approved == true;
        var banner = _bannerVariant != PlanBannerVariant.None;

        var showActions = hasPlan && !busy;
        _btnReplan.Visible = showActions && !banner;
        _btnApprove.Visible = showActions && !approved;
        _btnApprove.Enabled = _bannerVariant != PlanBannerVariant.StaleGuide;
        _btnApprove.Margin = new Padding(_btnReplan.Visible ? LogicalToDeviceUnits(Theme.SmallGap) : 0, 0, 0, 0);

        var showBadge = showActions && approved;
        if (showBadge)
            _badge.Text = CorpusUiText.ApprovalBadgeText((corpus!.ApprovedUtc ?? DateTime.UtcNow).ToLocalTime());
        _badge.Visible = showBadge;
        _badge.Margin = new Padding(_btnReplan.Visible ? LogicalToDeviceUnits(Theme.SmallGap) : 0, 0, 0, 0);

        _btnBannerReplan.Enabled = !busy;
        _btnKeepPlan.Enabled = !busy;
    }

    private void SelectTable(CountsTable table)
    {
        _selectedTable = table;
        foreach (var (t, toggle) in _tableToggles)
            toggle.Selected = t == table;
        foreach (var (t, grid) in _grids)
        {
            var visible = t == table;
            if (grid.Visible != visible)
                grid.Visible = visible;
        }
    }

    private void NavigateToFinding(int rowIndex)
    {
        if (rowIndex < 0 || rowIndex >= _gridChecks.Rows.Count || _gridChecks.Rows[rowIndex].Tag is not PlanFinding finding)
            return;

        CountsTable? table = finding.Area switch
        {
            PlanArea.Facts => CountsTable.KeyFact,
            PlanArea.Issues => CountsTable.Issue,
            PlanArea.Custodian => CountsTable.Custodian,
            _ => null
        };
        // Thread and plan-level findings have no table row (no per-thread drill-in, D-22)
        if (table == null || string.IsNullOrEmpty(finding.EntityId))
            return;

        SelectTable(table.Value);
        var grid = _grids[table.Value];
        foreach (DataGridViewRow row in grid.Rows)
        {
            if (!string.Equals(row.Tag as string, finding.EntityId, StringComparison.OrdinalIgnoreCase))
                continue;

            grid.ClearSelection();
            row.Selected = true;
            grid.CurrentCell = row.Cells[0];
            if (grid.DisplayedRowCount(false) > 0 && !row.Displayed)
                grid.FirstDisplayedScrollingRowIndex = row.Index;
            grid.Focus();
            return;
        }
    }

    private void GridChecks_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode != Keys.Enter || _gridChecks.CurrentRow == null)
            return;

        e.Handled = true;
        e.SuppressKeyPress = true;
        NavigateToFinding(_gridChecks.CurrentRow.Index);
    }

    /// <summary>
    /// "Plan ready: ..." (Success) for a clean plan, "Plan ready with {n} warnings..." (Warning) otherwise.
    /// </summary>
    private void ShowReadyStatus(CorpusPlan plan, List<PlanFinding> findings)
    {
        if (findings.Count > 0)
        {
            SetStatus(CorpusUiText.ReadyWithWarningsText(findings.Count), Theme.Warning);
            return;
        }

        var summary = PlanSummary.From(plan, Draft.Guide ?? new CaseGuide());
        SetStatus(CorpusUiText.ReadyText(summary.Emails, summary.Threads, summary.Relevant), Theme.Success);
    }

    private void SetStatus(string text, Color color)
    {
        if (IsDisposed)
            return;

        _lblStatus.Text = text;
        _lblStatus.ForeColor = color;
    }

    // ----- Render-test access -----

    /// <summary>
    /// Binds and shows one state synchronously for render tests; never starts a run or saves.
    /// </summary>
    internal void LoadForRender(WizardState state, ReviewPlanRenderState render, string selectedTable = "Issue",
        PlanStage stage = PlanStage.PlanningRelevant, int stageDone = 6, int stageTotal = 17,
        List<PlanFinding>? findings = null, string reason = "")
    {
        BindState(state);
        SelectTable(selectedTable switch
        {
            "Key fact" => CountsTable.KeyFact,
            "Custodian" => CountsTable.Custodian,
            _ => CountsTable.Issue
        });
        var plan = Draft.Corpus?.Plan;

        switch (render)
        {
            case ReviewPlanRenderState.Planning:
                _isPlanning = true;
                ShowStateCard(PlanStateCard.Planning);
                UpdateStageChecklist(stage, stageDone, stageTotal);
                _progress.Marquee = false;
                _progress.Maximum = Math.Max(1, stageTotal + 4);
                _progress.Value = stageDone + 1;
                _progress.Visible = true;
                _btnCancel.Visible = true;
                SetStatus(CorpusUiText.ProgressText(stage, stageDone, stageTotal), Theme.Accent);
                break;
            case ReviewPlanRenderState.Ready:
            case ReviewPlanRenderState.Stale:
            case ReviewPlanRenderState.ApprovedSettingsChanged:
                var shown = findings ?? CorpusPlanValidator.Validate(Draft, plan!, _state.IsMediaAvailable);
                ShowPlan(plan!, shown);
                var variant = render switch
                {
                    ReviewPlanRenderState.Stale => PlanBannerVariant.StaleGuide,
                    ReviewPlanRenderState.ApprovedSettingsChanged => PlanBannerVariant.SettingsChanged,
                    _ => PlanBannerVariant.None
                };
                ShowBanner(variant);
                if (render == ReviewPlanRenderState.Ready)
                    ShowReadyStatus(plan!, shown);
                break;
            case ReviewPlanRenderState.Failed:
                ShowStateCard(PlanStateCard.Failed, reason);
                break;
            default:
                ShowStateCard(PlanStateCard.NoPlan);
                break;
        }
    }

    internal IReadOnlyList<StatTile> Tiles => _tiles;
    internal CardPanel HistogramCard => _histogramCard;
    internal CardPanel CountsCard => _countsCard;
    internal MonthHistogram Histogram => _histogram;
    internal Button ApproveButton => _btnApprove;
    internal Control Badge => _badge;
    internal IReadOnlyList<DataGridView> CountGrids => new[] { _gridIssues, _gridFacts, _gridCustodians };
    internal DataGridView ChecksGrid => _gridChecks;
    internal CardPanel ChecksCard => _checksCard;
    internal bool ChecksListShown => _checksListShown;
    internal CardPanel Banner => _banner;
    internal CardPanel StateCardPanel => _stateCard;

    // ----- Small painted controls -----

    /// <summary>
    /// An 8 px dot, a 10 px gap and one line of text, vertically centred (legend items and the approval badge).
    /// Sizes itself to its text; every length goes through LogicalToDeviceUnits at paint time (rule 11).
    /// </summary>
    internal sealed class DotText : Control
    {
        private Color _dotColor = Theme.Accent;
        private int _minHeightLogical;

        public DotText()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.ResizeRedraw | ControlStyles.UserPaint | ControlStyles.SupportsTransparentBackColor, true);
            TabStop = false;
        }

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public Color DotColor
        {
            get => _dotColor;
            set
            {
                _dotColor = value;
                Invalidate();
            }
        }

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public int MinHeightLogical
        {
            get => _minHeightLogical;
            set
            {
                _minHeightLogical = value;
                FitSize();
            }
        }

        public override Size GetPreferredSize(Size proposedSize)
        {
            var text = TextRenderer.MeasureText(Text ?? string.Empty, Font, Size.Empty, TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
            var width = LogicalToDeviceUnits(8) + LogicalToDeviceUnits(10) + text.Width + 1;
            var height = Math.Max(LogicalToDeviceUnits(_minHeightLogical), text.Height);
            return new Size(width, height);
        }

        private void FitSize() => Size = GetPreferredSize(Size.Empty);

        protected override void OnTextChanged(EventArgs e)
        {
            base.OnTextChanged(e);
            FitSize();
            Invalidate();
        }

        protected override void OnFontChanged(EventArgs e)
        {
            base.OnFontChanged(e);
            FitSize();
        }

        protected override void OnDpiChangedAfterParent(EventArgs e)
        {
            base.OnDpiChangedAfterParent(e);
            FitSize();
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(BackColor);
            var dot = LogicalToDeviceUnits(8);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var brush = new SolidBrush(_dotColor))
                g.FillEllipse(brush, 0, (Height - dot) / 2f, dot, dot);
            g.SmoothingMode = SmoothingMode.None;

            var x = dot + LogicalToDeviceUnits(10);
            var bounds = new Rectangle(x, 0, Math.Max(0, Width - x), Height);
            TextRenderer.DrawText(g, Text, Font, bounds, ForeColor,
                TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.SingleLine);
        }
    }

    private enum StageRowState
    {
        Pending,
        Current,
        Done
    }

    /// <summary>
    /// One stage-checklist row: dot (BorderStrong pending, Accent current, Success done), 10 gap, the stage name and the
    /// current stage's right-aligned "6 of 17".
    /// </summary>
    private sealed class StageRow : Control
    {
        private readonly Font _nameFont = Theme.BodyFont();
        private readonly Font _progressFont = Theme.SmallFont();
        private StageRowState _rowState = StageRowState.Pending;
        private string _progressText = string.Empty;

        public StageRow()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
            TabStop = false;
        }

        [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
        public string StageName { get; set; } = string.Empty;

        internal string ProgressText => _progressText;

        public void Update(StageRowState state, string progress)
        {
            if (_rowState == state && _progressText == progress)
                return;
            _rowState = state;
            _progressText = progress;
            Invalidate();
        }

        protected override void OnDpiChangedAfterParent(EventArgs e)
        {
            base.OnDpiChangedAfterParent(e);
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(BackColor);
            var dot = LogicalToDeviceUnits(8);
            var dotColor = _rowState switch
            {
                StageRowState.Done => Theme.Success,
                StageRowState.Current => Theme.Accent,
                _ => Theme.BorderStrong
            };
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var brush = new SolidBrush(dotColor))
                g.FillEllipse(brush, 0, (Height - dot) / 2f, dot, dot);
            g.SmoothingMode = SmoothingMode.None;

            const TextFormatFlags flags = TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.VerticalCenter
                | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis;
            var progressWidth = 0;
            if (_progressText.Length > 0)
            {
                // Measured without EndEllipsis (it shortens the measured width) and with a pixel of slack
                progressWidth = TextRenderer.MeasureText(_progressText, _progressFont, Size.Empty,
                    TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine).Width + 2;
                TextRenderer.DrawText(g, _progressText, _progressFont, new Rectangle(Width - progressWidth, 0, progressWidth, Height),
                    Theme.TextMuted, flags | TextFormatFlags.Right);
                progressWidth += LogicalToDeviceUnits(Theme.SmallGap);
            }

            var x = dot + LogicalToDeviceUnits(10);
            var nameColor = _rowState == StageRowState.Pending ? Theme.TextMuted : Theme.Text;
            TextRenderer.DrawText(g, StageName, _nameFont, new Rectangle(x, 0, Math.Max(0, Width - x - progressWidth), Height),
                nameColor, flags | TextFormatFlags.Left);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _nameFont.Dispose();
                _progressFont.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
