using ReelDiscovery.Helpers;
using ReelDiscovery.Models;
using ReelDiscovery.Models.Evidence;

namespace ReelDiscovery.UserControls;

/// <summary>
/// Which Generate Corpus state LoadForRender shows (render tests only; no run is started).
/// </summary>
internal enum GenerateCorpusRenderState
{
    Ready,
    Running,
    Partial,
    FinishedClean,
    FinishedIssues,
    NotApproved,
    PlanChanged,
    Unreadable,
    ReadyWithMedia,
    ReadyMediaUnavailable,
    RunningNonRelevant,
    PartialUpgrade,
    FinishedSkippedMedia,
    FinishedSkippedMediaWithFailures,
    FinishedSkippedMediaUnavailable,
    GuideChanged,
    NoPlan
}

// What the row-2 banner slot holds (06-UI-SPEC S1 Row 2: at most one banner, partial-run first)
internal enum GenerateCorpusBanner { None, PartialRun, Media }

/// <summary>
/// Generate Corpus step (05-UI-SPEC S1, 06-UI-SPEC S1): header with save state and Start Over / Retry Failed Items, the
/// scope notice, the partial-run or media banner, seven stat tiles, the Stages and Activity cards (or a full-width state card) and the status
/// strip. This file holds the layout and paints any GenerationView snapshot; StepGenerateCorpus.Run.cs holds the wizard
/// lifecycle and the entry decision. All copy comes from GenerationUiText.
/// </summary>
public partial class StepGenerateCorpus : UserControl
{
    // Activity view cap; the journal keeps everything (UI-SPEC S1)
    internal const int MaxActivityRows = 500;

    private const string StagesHint = "Relevant waves oldest first, then non-relevant mail";
    private const string ActivityHint = "Newest first";

    // Tiles 0-2 (Relevant, Non-relevant, Attachments) show "done / total" and pick a DPI rule-19 variant
    private const int FractionTiles = 3;

    private static readonly GenerationTileKind[] TileKinds =
    {
        GenerationTileKind.Relevant,
        GenerationTileKind.NonRelevant,
        GenerationTileKind.Attachments,
        GenerationTileKind.Emails,
        GenerationTileKind.Warnings,
        GenerationTileKind.Failed,
        GenerationTileKind.Cost
    };

    private WizardState _state = null!;

    private TableLayoutPanel _root = null!;
    private Label _lblTitle = null!;
    private Label _lblSubtitle = null!;
    private FlowLayoutPanel _headerButtons = null!;
    private Button _btnStartOver = null!;
    private Button _btnRetryFailed = null!;
    private Label _lblNotice = null!;

    private CardPanel _banner = null!;
    private RowStyle _bannerRow = null!;
    private Label _lblBannerHeading = null!;
    private Label _lblBannerBody = null!;
    private FlowLayoutPanel _bannerButtons = null!;
    private Button _btnContinue = null!;

    private TableLayoutPanel _tilesRow = null!;
    private readonly List<StatTile> _tiles = new();

    private Panel _bodyHost = null!;
    private TableLayoutPanel _bodyLayout = null!;
    private CardPanel _wavesCard = null!;
    private DataGridView _gridWaves = null!;
    private CardPanel _activityCard = null!;
    private DataGridView _gridActivity = null!;

    private CardPanel _stateCard = null!;
    private FlowLayoutPanel _stateContent = null!;
    private Label _lblStateHeading = null!;
    private Label _lblStateBody = null!;
    private Label _lblStateEstimate = null!;
    private Label _lblStateMediaNotice = null!;
    private Button _btnStateAction = null!;

    // Ready card only (07 S1, D-08): the saved batch choice and its hint
    private CheckBox _chkUseBatch = null!;
    private Label _lblBatchHint = null!;
    // Set while the step writes the checkbox itself, so CheckedChanged does not save
    private bool _syncingBatch;

    private Label _lblStatus = null!;
    private SlimProgressBar _progress = null!;
    private Button _btnCancel = null!;
    private readonly ToolTip _toolTip = new();

    // Applies the run snapshot every 250 ms during a run (RUN-07); started by the run code (05-15)
    private System.Windows.Forms.Timer _refreshTimer = null!;

    // Logical corner radius inset of the full-bleed cards (deferred-items 04-12; 04-17 precedent)
    private int _cornerInset;
    private int _buttonHeight;

    // Assigned by the run code (05-15)
    private bool _isGenerating = false;
    // Start Over is deleting files; navigation waits for it
    private bool _isResetting = false;
    // Which state card is shown, so its single action button knows what to do
    private GenerationStateCard _cardKind = GenerationStateCard.Ready;
    private bool _stateCardShown = true;
    private bool _bannerShown;
    private GenerateCorpusBanner _bannerKind = GenerateCorpusBanner.None;
    // Visible reads false while a parent is hidden, so the banner layout keeps its own flag
    private bool _bannerHasButton = true;
    private GenerationCounts? _lastCounts;
    // Running stage row to keep in view (-1 = none) and the last time the user scrolled the Stages grid (05-UI-SPEC S1)
    private int _runningStageIndex = -1;
    private DateTime _stagesUserScrollUtc = DateTime.MinValue;
    private bool _stagesAutoScrolling;
    private string _noticeText = string.Empty;

    public string StepTitle => "Generate Corpus";
    public string NextButtonText => "Next >";
    public bool CanMoveNext => !_isGenerating && !_isResetting;
    public bool CanMoveBack => !_isGenerating && !_isResetting;

    /// <summary>
    /// True while a generation run is active (closing asks C-G3).
    /// </summary>
    public bool IsGenerating => _isGenerating;

    /// <summary>
    /// True while another provider's pending batches are collected or cancelled before a run (10 D-13); closing asks C-G3.
    /// </summary>
    public bool IsResolvingBatches => false;

    public event EventHandler? StateChanged;

    public StepGenerateCorpus()
    {
        InitializeUI();
        WireRunHandlers();
    }

    private void RaiseStateChanged() => StateChanged?.Invoke(this, EventArgs.Empty);

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
        _buttonHeight = LogicalToDeviceUnits(34);
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
        _root.RowStyles.Add(new RowStyle(SizeType.AutoSize));     // 1: Notice line
        // Sized in SizeBanner from the page width: the wrapping text makes the card's own preferred size unusable
        _bannerRow = new RowStyle(SizeType.Absolute, 0);
        _root.RowStyles.Add(_bannerRow);                          // 2: Partial-run banner
        _root.RowStyles.Add(new RowStyle(SizeType.AutoSize));     // 3: Tiles
        _root.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); // 4: Body (Waves + Activity, or the state card)
        _root.RowStyles.Add(new RowStyle(SizeType.AutoSize));     // 5: Status strip

        BuildHeader(gap, smallGap);
        BuildNotice(gap);
        BuildBanner(gap, pad);
        BuildTiles(gap);
        BuildBody(smallGap, inset);
        BuildStatusStrip(gap);

        _refreshTimer = new System.Windows.Forms.Timer { Interval = 250 };

        this.Controls.Add(_root);

        // Wire handlers only after every control exists (handlers read other controls)
        _gridWaves.CellPainting += Grid_CellPainting;
        _gridWaves.Scroll += (s, e) =>
        {
            if (!_stagesAutoScrolling)
                _stagesUserScrollUtc = DateTime.UtcNow;
        };
        _gridWaves.SizeChanged += (s, e) => ScrollRunningStageIntoView();
        _gridWaves.VisibleChanged += (s, e) => ScrollRunningStageIntoView();
        _gridActivity.CellPainting += Grid_CellPainting;
        _root.SizeChanged += (s, e) =>
        {
            SizeNotice();
            SizeBanner();
        };
        for (var i = 0; i < FractionTiles; i++)
        {
            var index = i;
            _tiles[index].SizeChanged += (s, e) => ApplyFractionTile(index);
        }
        _stateCard.Resize += (s, e) => CenterStateContent();
        _stateContent.SizeChanged += (s, e) => CenterStateContent();
        _lblStatus.TextChanged += (s, e) => _toolTip.SetToolTip(_lblStatus, _lblStatus.Text);
    }

    private void BuildHeader(int gap, int smallGap)
    {
        var headerLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 2,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Margin = new Padding(0, 0, 0, smallGap)
        };
        headerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        headerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        headerLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // 0: Title
        headerLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // 1: Subtitle

        _lblTitle = new Label
        {
            Text = "Generate Corpus",
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
        _btnStartOver = ButtonHelper.CreateButton("Start Over...", 0, _buttonHeight, ButtonStyle.Default);
        _btnStartOver.Margin = new Padding(0);
        _btnStartOver.Visible = false;
        _btnRetryFailed = ButtonHelper.CreateButton(GenerationUiText.RetryFailedButtonText, 0, _buttonHeight, ButtonStyle.Primary);
        _btnRetryFailed.Margin = new Padding(smallGap, 0, 0, 0);
        _btnRetryFailed.Visible = false;
        _toolTip.SetToolTip(_btnRetryFailed, GenerationUiText.RetryFailedTooltip);
        _headerButtons.Controls.Add(_btnStartOver);
        _headerButtons.Controls.Add(_btnRetryFailed);

        headerLayout.Controls.Add(_lblTitle, 0, 0);
        headerLayout.Controls.Add(_lblSubtitle, 0, 1);
        headerLayout.Controls.Add(_headerButtons, 1, 0);
        headerLayout.SetRowSpan(_headerButtons, 2);
        _root.Controls.Add(headerLayout, 0, 0);
    }

    private void BuildNotice(int gap)
    {
        // Wraps to the page width (SizeNotice); hidden when there is no plan to describe
        _lblNotice = new Label
        {
            AutoSize = true,
            UseMnemonic = false,
            Margin = new Padding(0, 0, 0, gap),
            Font = Theme.SmallFont(),
            ForeColor = Theme.TextMuted,
            BackColor = Theme.Background,
            Visible = false
        };
        _root.Controls.Add(_lblNotice, 0, 1);
    }

    private void BuildBanner(int gap, int pad)
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
            Text = GenerationUiText.BannerHeading(runEnded: false),
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
        _btnContinue = ButtonHelper.CreateButton(GenerationUiText.BannerButton, 0, _buttonHeight, ButtonStyle.Primary);
        _btnContinue.Margin = new Padding(0);
        _bannerButtons.Controls.Add(_btnContinue);
        bannerLayout.Controls.Add(_lblBannerHeading, 0, 0);
        bannerLayout.Controls.Add(_lblBannerBody, 0, 1);
        bannerLayout.Controls.Add(_bannerButtons, 1, 0);
        bannerLayout.SetRowSpan(_bannerButtons, 2);
        _banner.Controls.Add(bannerLayout);
        _root.Controls.Add(_banner, 0, 2);
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
        for (var i = 0; i < TileKinds.Length; i++)
        {
            _tilesRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / TileKinds.Length));
            var tile = new StatTile
            {
                Dock = DockStyle.Fill,
                TabStop = false,
                Margin = new Padding(i == 0 ? 0 : half, 0, i == TileKinds.Length - 1 ? 0 : half, 0)
            };
            _tiles.Add(tile);
            _tilesRow.Controls.Add(tile, i, 0);
        }
        _root.Controls.Add(_tilesRow, 0, 3);
    }

    private void BuildBody(int smallGap, int inset)
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
        _bodyLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); // 0: Stages card | Activity card

        _wavesCard = BuildWavesCard(inset);
        _wavesCard.Margin = new Padding(0, 0, smallGap, 0);
        _activityCard = BuildActivityCard(inset);
        _activityCard.Margin = new Padding(smallGap, 0, 0, 0);
        _bodyLayout.Controls.Add(_wavesCard, 0, 0);
        _bodyLayout.Controls.Add(_activityCard, 1, 0);
        _bodyHost.Controls.Add(_bodyLayout);

        // State card (ready / not approved / plan changed / unreadable); replaces the banner, tiles and body
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
        // Ready card only: the estimate line and the Warning media notice (06-UI-SPEC S1 Row 4); hidden when empty
        _lblStateEstimate = new Label
        {
            AutoSize = true,
            UseMnemonic = false,
            MaximumSize = new Size(LogicalToDeviceUnits(520), 0),
            Font = Theme.SmallFont(),
            ForeColor = Theme.TextMuted,
            BackColor = Theme.Surface,
            Margin = new Padding(0, 0, 0, LogicalToDeviceUnits(20)),
            Visible = false
        };
        _lblStateMediaNotice = new Label
        {
            AutoSize = true,
            UseMnemonic = false,
            MaximumSize = new Size(LogicalToDeviceUnits(520), 0),
            Font = Theme.SmallFont(),
            ForeColor = Theme.Warning,
            BackColor = Theme.Surface,
            Margin = new Padding(0, 0, 0, LogicalToDeviceUnits(20)),
            Visible = false
        };
        // Batch opt-in (07-UI-SPEC S1 rows 4-5): saved to the case on change; the hint wraps at 520 like the other lines
        _chkUseBatch = new CheckBox
        {
            Text = GenerationUiText.BatchCheckboxText,
            AutoSize = true,
            FlatStyle = FlatStyle.System,
            Font = Theme.BodyFont(),
            ForeColor = Theme.Text,
            BackColor = Theme.Surface,
            UseMnemonic = false,
            Margin = new Padding(0, 0, 0, LogicalToDeviceUnits(4)),
            Visible = false
        };
        _chkUseBatch.CheckedChanged += (s, e) => OnUseBatchChanged();
        _lblBatchHint = new Label
        {
            AutoSize = true,
            UseMnemonic = false,
            MaximumSize = new Size(LogicalToDeviceUnits(520), 0),
            Font = Theme.SmallFont(),
            ForeColor = Theme.TextMuted,
            BackColor = Theme.Surface,
            Margin = new Padding(0, 0, 0, LogicalToDeviceUnits(20)),
            Visible = false
        };
        _btnStateAction = ButtonHelper.CreateButton("Generate Emails", 0, _buttonHeight, ButtonStyle.Primary);
        _btnStateAction.Margin = new Padding(0);

        _stateContent.Controls.Add(_lblStateHeading);
        _stateContent.Controls.Add(_lblStateBody);
        _stateContent.Controls.Add(_lblStateEstimate);
        _stateContent.Controls.Add(_chkUseBatch);
        _stateContent.Controls.Add(_lblBatchHint);
        _stateContent.Controls.Add(_lblStateMediaNotice);
        _stateContent.Controls.Add(_btnStateAction);
        _stateCard.Controls.Add(_stateContent);
        _bodyHost.Controls.Add(_stateCard);
        _root.Controls.Add(_bodyHost, 0, 4);
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

    private static TableLayoutPanel NewCardLayout() => new()
    {
        Dock = DockStyle.Fill,
        ColumnCount = 1,
        RowCount = 2,
        Margin = new Padding(0),
        Padding = new Padding(0),
        BackColor = Theme.Surface
    };

    /// <summary>
    /// Card header: caption left, muted hint right (ellipsizes before it collides), at least 28 tall.
    /// </summary>
    private TableLayoutPanel NewCardHeader(int inset, string caption, string hint)
    {
        var header = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Margin = new Padding(inset, Math.Max(0, LogicalToDeviceUnits(14) - _cornerInset + 1), inset, LogicalToDeviceUnits(10)),
            Padding = new Padding(0),
            BackColor = Theme.Surface
        };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        header.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // 0: Caption | hint
        var minHeight = LogicalToDeviceUnits(28);
        header.Controls.Add(new Label
        {
            Text = caption,
            AutoSize = true,
            UseMnemonic = false,
            MinimumSize = new Size(0, minHeight),
            TextAlign = ContentAlignment.MiddleLeft,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(0),
            Font = Theme.LabelFont(),
            ForeColor = Theme.Text,
            BackColor = Theme.Surface
        }, 0, 0);
        header.Controls.Add(new Label
        {
            Text = hint,
            AutoSize = false,
            AutoEllipsis = true,
            UseMnemonic = false,
            Dock = DockStyle.Fill,
            Height = minHeight,
            TextAlign = ContentAlignment.MiddleRight,
            Margin = new Padding(LogicalToDeviceUnits(Theme.SmallGap), 0, 0, 0),
            Font = Theme.SmallFont(),
            ForeColor = Theme.TextMuted,
            BackColor = Theme.Surface
        }, 1, 0);
        return header;
    }

    private CardPanel BuildWavesCard(int inset)
    {
        var card = NewFullBleedCard();
        var layout = NewCardLayout();
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));     // 0: Card header (caption | hint)
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); // 1: Stages grid
        layout.Controls.Add(NewCardHeader(inset, "Stages", StagesHint), 0, 0);

        _gridWaves = NewGrid();
        GridStyle.AddDotColumn(_gridWaves, this);
        AddRightColumn(_gridWaves, "Wave", "Wave", 56);
        AddFillColumn(_gridWaves, "Dates", "Dates", 120);
        // Stage rows count items (threads or attachments), so the column is Done (06-UI-SPEC S1 Row 4). UI-SPEC says 80;
        // a 5K run's "2,240 / 3,760" plus the 15 px right inset needs more at Segoe UI 9.5, so it fits "9,999 / 9,999"
        var doneColumn = AddRightColumn(_gridWaves, "Done", "Done", 80);
        using (var bodyFont = Theme.BodyFont())
        {
            var doneText = TextRenderer.MeasureText("9,999 / 9,999", bodyFont).Width + LogicalToDeviceUnits(15 + 8);
            doneColumn.Width = Math.Max(doneColumn.Width, doneText);
        }
        AddRightColumn(_gridWaves, "Warnings", "Warnings", 80);
        AddRightColumn(_gridWaves, "Failed", "Failed", 64);
        layout.Controls.Add(_gridWaves, 0, 1);
        card.Controls.Add(layout);
        return card;
    }

    private CardPanel BuildActivityCard(int inset)
    {
        var card = NewFullBleedCard();
        var layout = NewCardLayout();
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));     // 0: Card header (caption | hint)
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); // 1: Activity grid
        layout.Controls.Add(NewCardHeader(inset, "Activity", ActivityHint), 0, 0);

        _gridActivity = NewGrid();
        GridStyle.AddDotColumn(_gridActivity, this);
        // UI-SPEC says 88; "12:59:59 PM" plus the 15 px content inset needs a little more at Segoe UI 9.5
        var time = AddTextColumn(_gridActivity, "Time", "Time", 88);
        using var bodyFont = Theme.BodyFont();
        var timeText = TextRenderer.MeasureText("12:59:59 PM", bodyFont).Width + LogicalToDeviceUnits(15 + 8);
        time.Width = Math.Max(time.Width, timeText);
        AddTextColumn(_gridActivity, "Thread", "Thread", 72);
        AddFillColumn(_gridActivity, "Event", "Event", 160);
        layout.Controls.Add(_gridActivity, 0, 1);
        card.Controls.Add(layout);
        return card;
    }

    private DataGridView NewGrid()
    {
        var grid = new DataGridView
        {
            Dock = DockStyle.Fill,
            ReadOnly = true,
            // The card's bottom inset (corner radius) already covers most of the 6 px bottom margin
            Margin = new Padding(0, 0, 0, Math.Max(0, LogicalToDeviceUnits(6) + 1 - _cornerInset))
        };
        GridStyle.Apply(grid, this);
        grid.AllowUserToResizeColumns = false;
        grid.ScrollBars = ScrollBars.Vertical;
        grid.ShowCellToolTips = true;
        grid.ColumnHeadersDefaultCellStyle.WrapMode = DataGridViewTriState.False;
        grid.DefaultCellStyle.WrapMode = DataGridViewTriState.False;
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
            Resizable = DataGridViewTriState.False,
            // Chronological order is the meaning: no sorting
            SortMode = DataGridViewColumnSortMode.NotSortable
        };
        grid.Columns.Add(column);
        return column;
    }

    private void AddFillColumn(DataGridView grid, string name, string header, int minWidth)
    {
        grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = name,
            HeaderText = header,
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
            FillWeight = 100,
            MinimumWidth = LogicalToDeviceUnits(minWidth),
            SortMode = DataGridViewColumnSortMode.NotSortable
        });
    }

    /// <summary>
    /// Right-aligned column: header and cell text end 15 inside the grid (16 from the card's outer edge, Phase 4 rule 15).
    /// </summary>
    private DataGridViewTextBoxColumn AddRightColumn(DataGridView grid, string name, string header, int width)
    {
        var column = AddTextColumn(grid, name, header, width);
        column.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
        column.DefaultCellStyle.Padding = new Padding(0, 0, LogicalToDeviceUnits(15), 0);
        column.HeaderCell.Style.Alignment = DataGridViewContentAlignment.MiddleRight;
        column.HeaderCell.Style.Padding = new Padding(0, LogicalToDeviceUnits(6), LogicalToDeviceUnits(15) - GridStyle.HeaderTextMargin, LogicalToDeviceUnits(6));
        return column;
    }

    private void BuildStatusStrip(int gap)
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
        _btnCancel = ButtonHelper.CreateButton("Cancel Generation", 0, _buttonHeight, ButtonStyle.Danger);
        _btnCancel.Margin = new Padding(0);
        _btnCancel.Visible = false;
        _lblStatus = new Label
        {
            Dock = DockStyle.Fill,
            MinimumSize = new Size(0, Math.Max(_buttonHeight, _btnCancel.Height)),
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
    /// Paints a snapshot: tiles, waves (in place), new activity rows at the top, the bar and the status text. Workers never
    /// call this; the step's timer or the entry code does, on the UI thread.
    /// </summary>
    internal void ApplyView(GenerationView view)
    {
        ArgumentNullException.ThrowIfNull(view);
        if (IsDisposed)
            return;

        var counts = view.Counts ?? new GenerationCounts();
        // Cost tile batch clauses: the case's saved choice and the savings known from the journal (07 S1 tile 7)
        counts.BatchOn = CaseUseBatch;
        if (counts.Running)
        {
            // Live: the case's earlier savings plus what this run's batch discount took off so far
            var session = _state?.UsageTracker.TotalBatchSavings ?? _runSavingsBase;
            counts.BatchSavingsUsd = _caseBatchSavings + Math.Max(0m, session - _runSavingsBase);
            _pendingProviderBatches = view.PendingProviderBatches;
        }
        else
        {
            counts.BatchSavingsUsd = Math.Max(counts.BatchSavingsUsd, _caseBatchSavings);
        }
        _lastCounts = counts.Clone();
        for (var i = FractionTiles; i < TileKinds.Length; i++)
            _tiles[i].SetContent(GenerationUiText.Tile(TileKinds[i], counts));
        ApplyProgressTiles();

        ApplyWaves(view.Waves ?? new List<WaveProgress>());
        AddActivity(view.Events ?? new List<GenerationEvent>());

        if (counts.Running)
        {
            SetRunChrome(true);
            _progress.Marquee = false;
            var (maximum, value) = ProgressUnits(counts);
            _progress.Maximum = maximum;
            _progress.Value = value;
            // Batch wait and collecting texts with the countdown (D-09); otherwise the normal stage text
            var text = GenerationUiText.ProgressText(view, DateTime.UtcNow);
            if (text.Length > 0)
                SetStatus(text, Theme.Accent);
        }
    }

    /// <summary>
    /// Progress bar units (U22): relevant threads, non-relevant threads and attachments count equally; Written, Warnings,
    /// Failed and Skipped are finished. The counts move per item status (06-02), so a retried item is never counted twice;
    /// the value is clamped to the maximum.
    /// </summary>
    internal static (int Maximum, int Value) ProgressUnits(GenerationCounts counts)
    {
        ArgumentNullException.ThrowIfNull(counts);
        var maximum = Math.Max(1, counts.TotalThreads + counts.AttachmentsTotal);
        var finished = counts.DoneThreads + counts.FailedThreads
                       + counts.AttachmentsDone + counts.AttachmentsFailed + counts.AttachmentsSkipped;
        return (maximum, Math.Clamp(finished, 0, maximum));
    }

    /// <summary>
    /// Relevant, Non-relevant and Attachments tiles, each fitted on its own (DPI rule 21).
    /// </summary>
    private void ApplyProgressTiles()
    {
        for (var i = 0; i < FractionTiles; i++)
            ApplyFractionTile(i);
    }

    /// <summary>
    /// One of the Relevant, Non-relevant and Attachments tiles: it takes the widest variant whose value fits it at TitleFont
    /// (DPI rules 19 and 21, independently per tile).
    /// </summary>
    private void ApplyFractionTile(int index)
    {
        if (_lastCounts == null)
            return;

        var tile = _tiles[index];
        var kind = TileKinds[index];
        // Tile card padding is 16 + 16; the value label spans the rest
        var available = tile.Width - LogicalToDeviceUnits(32);
        var variant = 0;
        if (available > 0)
        {
            using var font = Theme.TitleFont();
            while (variant < 2)
            {
                var value = GenerationUiText.Tile(kind, _lastCounts, variant).Value;
                if (TextRenderer.MeasureText(value, font).Width <= available)
                    break;
                variant++;
            }
        }

        var content = GenerationUiText.Tile(kind, _lastCounts, variant);
        if (tile.ValueText != content.Value || tile.SubLineText != content.SubLine || tile.TooltipText.Length == 0)
            tile.SetContent(content);
    }

    /// <summary>
    /// Stages grid: relevant wave rows, then the Non-relevant mail and Attachments for written threads rows. Rows are
    /// created once per row count, then updated in place by position (no rebuild, no scroll jump).
    /// </summary>
    private void ApplyWaves(List<WaveProgress> waves)
    {
        if (_gridWaves.Rows.Count != waves.Count)
        {
            _gridWaves.Rows.Clear();
            if (waves.Count > 0)
                _gridWaves.Rows.Add(waves.Count);
        }

        for (var i = 0; i < waves.Count; i++)
        {
            var stage = waves[i];
            var row = _gridWaves.Rows[i];
            var color = StageColor(stage);
            if (row.Tag is not DotTag tag || tag.Color != color)
            {
                row.Tag = new DotTag { Color = color };
                _gridWaves.InvalidateCell(0, i);
            }

            // Stage rows have no wave number
            SetCell(row, "Wave", stage.RowKind == StageRowKind.Wave
                ? stage.Index.ToString(System.Globalization.CultureInfo.InvariantCulture)
                : string.Empty);
            var (dates, done, tooltip) = GenerationUiText.StageRowText(stage);
            if (stage.BatchWaiting)
            {
                // D-09: the Dates cell shows the compact wait (DPI rule 27); the tooltip keeps the dates, the full
                // sentence and the countdown
                var wait = GenerationUiText.BatchWaitText(stage, DateTime.Now);
                tooltip = GenerationUiText.BatchWaitTooltip(dates, wait,
                    GenerationUiText.SecondsUntil(stage.BatchNextCheckUtc, DateTime.UtcNow));
                dates = GenerationUiText.BatchWaitCellText(stage);
            }
            SetCell(row, "Dates", dates);
            row.Cells["Dates"].ToolTipText = tooltip;
            SetCell(row, "Done", done);
            var (warnings, failed) = StageProblems(stage);
            SetCell(row, "Warnings", warnings > 0 ? warnings.ToString("N0") : string.Empty);
            SetCell(row, "Failed", failed > 0 ? failed.ToString("N0") : string.Empty);
        }

        _gridWaves.ClearSelection();
        _runningStageIndex = waves.FindIndex(w => w.Running);
        ScrollRunningStageIntoView();
    }

    /// <summary>
    /// Keeps the running stage row visible unless the user scrolled the grid in the last 5 seconds (05-UI-SPEC S1).
    /// </summary>
    private void ScrollRunningStageIntoView()
    {
        var index = _runningStageIndex;
        if (index < 0 || index >= _gridWaves.Rows.Count || !_gridWaves.IsHandleCreated || !_gridWaves.Visible)
            return;
        if (DateTime.UtcNow - _stagesUserScrollUtc < TimeSpan.FromSeconds(5))
            return;

        var shown = _gridWaves.DisplayedRowCount(false);
        if (shown <= 0)
            return;
        var first = Math.Max(0, _gridWaves.FirstDisplayedScrollingRowIndex);
        if (index >= first && index < first + shown)
            return;

        _stagesAutoScrolling = true;
        try
        {
            _gridWaves.FirstDisplayedScrollingRowIndex = Math.Max(0, index - shown + 1);
        }
        finally
        {
            _stagesAutoScrolling = false;
        }
    }

    // Wave and Non-relevant rows count their threads and attachments; the repair row counts attachments only
    private static (int Warnings, int Failed) StageProblems(WaveProgress stage) =>
        stage.RowKind == StageRowKind.Attachments
            ? (stage.AttachmentWarnings, stage.AttachmentsFailed)
            : (stage.Warnings + stage.AttachmentWarnings, stage.Failed + stage.AttachmentsFailed);

    private static void SetCell(DataGridViewRow row, string column, string value)
    {
        var cell = row.Cells[column];
        if (!Equals(cell.Value as string, value))
            cell.Value = value;
    }

    /// <summary>
    /// Phase 5 wave dot rules for every stage row: running Accent, any failure Danger, pending BorderStrong, warnings or
    /// skipped Warning, otherwise Success.
    /// </summary>
    private static Color StageColor(WaveProgress stage)
    {
        // A stage waiting on a provider batch is still running (07 S1 Row 4)
        if (stage.Running || stage.BatchWaiting)
            return Theme.Accent;

        var (warnings, failed) = StageProblems(stage);
        if (failed > 0)
            return Theme.Danger;

        var (total, finished) = stage.RowKind == StageRowKind.Attachments
            ? (stage.Attachments, stage.AttachmentsDone + stage.AttachmentsFailed + stage.AttachmentsSkipped)
            : (stage.Threads, stage.Done + stage.Failed);
        if (total == 0 || finished < total)
            return Theme.BorderStrong;
        return warnings > 0 || stage.AttachmentsSkipped > 0 ? Theme.Warning : Theme.Success;
    }

    /// <summary>
    /// New events (oldest first in the list) go to the top in one batch, newest first; the view keeps the newest 500.
    /// </summary>
    private void AddActivity(List<GenerationEvent> events)
    {
        if (events.Count == 0)
            return;

        var recent = events.Count > MaxActivityRows ? events.Skip(events.Count - MaxActivityRows).ToList() : events;
        var rows = new List<DataGridViewRow>(recent.Count);
        for (var i = recent.Count - 1; i >= 0; i--)
        {
            var e = recent[i];
            var line = GenerationUiText.ActivityText(e);
            // From the template so inserted rows keep the 32 px grid row height
            var row = (DataGridViewRow)_gridActivity.RowTemplate.Clone();
            row.CreateCells(_gridActivity, string.Empty, e.LocalTime.ToString("h:mm:ss tt", System.Globalization.CultureInfo.GetCultureInfo("en-US")),
                e.ThreadId, line.Text);
            row.Cells[_gridActivity.Columns["Event"]!.Index].ToolTipText = line.Tooltip;
            row.Tag = new DotTag { Color = ToneColor(line.Tone), Event = e };
            rows.Add(row);
        }

        _gridActivity.SuspendLayout();
        try
        {
            _gridActivity.Rows.InsertRange(0, rows.ToArray());
            while (_gridActivity.Rows.Count > MaxActivityRows)
                _gridActivity.Rows.RemoveAt(_gridActivity.Rows.Count - 1);
            _gridActivity.ClearSelection();
        }
        finally
        {
            _gridActivity.ResumeLayout();
        }
    }

    private void ClearActivity()
    {
        _gridActivity.Rows.Clear();
        _gridWaves.Rows.Clear();
        _runningStageIndex = -1;
    }

    internal static Color ToneColor(ActivityTone tone) => tone switch
    {
        ActivityTone.Success => Theme.Success,
        ActivityTone.Warning => Theme.Warning,
        ActivityTone.Danger => Theme.Danger,
        _ => Theme.Accent
    };

    private void Grid_CellPainting(object? sender, DataGridViewCellPaintingEventArgs e)
    {
        if (sender is not DataGridView grid || e.RowIndex < 0 || e.Graphics == null)
            return;

        var dot = grid.Columns[GridStyle.DotColumnName];
        if (dot == null || e.ColumnIndex != dot.Index)
            return;

        e.Paint(e.CellBounds, DataGridViewPaintParts.All & ~DataGridViewPaintParts.ContentForeground);
        if (grid.Rows[e.RowIndex].Tag is DotTag tag)
            Theme.DrawStatusDot(e.Graphics, e.CellBounds, tag.Color, this);
        e.Handled = true;
    }

    /// <summary>
    /// Shows a full-width state card (ready, not approved, plan changed, unreadable) in place of the banner, tiles and body.
    /// </summary>
    internal void ShowStateCard(GenerationCardContent content, GenerationStateCard kind)
    {
        ArgumentNullException.ThrowIfNull(content);
        _cardKind = kind;
        _lblStateHeading.Text = content.Heading;
        _lblStateBody.Text = content.Body;

        // Ready card: body bottom 8 when a line follows; estimate, checkbox and hint bottom 4 when a line follows; whichever
        // line is last carries 20 (07-UI-SPEC S1, 06 S1 rule extended). The checkbox shows on the Ready card only (D-08)
        var estimate = content.EstimateLine ?? string.Empty;
        var notice = content.MediaNotice ?? string.Empty;
        var ready = kind == GenerationStateCard.Ready;
        var hint = ready ? content.BatchHint ?? string.Empty : string.Empty;
        var last = LogicalToDeviceUnits(20);
        var xs = LogicalToDeviceUnits(4);
        _lblStateBody.Margin = new Padding(0, 0, 0,
            estimate.Length > 0 || ready || notice.Length > 0 ? LogicalToDeviceUnits(Theme.SmallGap) : last);
        _lblStateEstimate.Text = estimate;
        _lblStateEstimate.Visible = estimate.Length > 0;
        _lblStateEstimate.Margin = new Padding(0, 0, 0, ready || notice.Length > 0 ? xs : last);
        _toolTip.SetToolTip(_lblStateEstimate, ready ? content.EstimateTooltip ?? string.Empty : string.Empty);
        _syncingBatch = true;
        try
        {
            _chkUseBatch.Checked = ready && CaseUseBatch;
        }
        finally
        {
            _syncingBatch = false;
        }
        _chkUseBatch.Visible = ready;
        _chkUseBatch.Margin = new Padding(0, 0, 0, hint.Length > 0 || notice.Length > 0 ? xs : last);
        _lblBatchHint.Text = hint;
        _lblBatchHint.ForeColor = content.BatchHintTone == SummaryTone.Warning ? Theme.Warning : Theme.TextMuted;
        _lblBatchHint.Visible = hint.Length > 0;
        _lblBatchHint.Margin = new Padding(0, 0, 0, notice.Length > 0 ? xs : last);
        _lblStateMediaNotice.Text = notice;
        _lblStateMediaNotice.Visible = notice.Length > 0;
        ButtonHelper.StyleButton(_btnStateAction, content.Danger ? ButtonStyle.Danger : ButtonStyle.Primary);
        _btnStateAction.Text = content.ButtonText;
        if (_btnStateAction is ThemedButton themed)
            themed.FitToText(0, _buttonHeight);
        _btnStateAction.Visible = content.ButtonText.Length > 0;

        SetRunChrome(false);
        _bannerShown = false;
        _bannerKind = GenerateCorpusBanner.None;
        _btnStartOver.Visible = false;
        _btnRetryFailed.Visible = false;
        SetStateCardShown(true);
        RefreshHeader();
        RaiseStateChanged();
    }

    /// <summary>
    /// Shows the dashboard (tiles, stages, activity) after a run or on reopen: the partial-run banner when items are left,
    /// Start Over in the header, and Retry Failed Items when a finished run has failed items (UI-SPEC Row 0 table).
    /// <paramref name="failed"/> counts failed threads + attachments; <paramref name="runEnded"/> picks the banner heading.
    /// </summary>
    internal void ShowDashboard(bool banner, int done, int total, int failed, bool finishedWithFailures, bool runEnded = false,
        int attachmentsDone = 0, int attachmentsTotal = 0)
    {
        SetRunChrome(false);
        _btnStartOver.Visible = true;
        _btnRetryFailed.Margin = new Padding(LogicalToDeviceUnits(Theme.SmallGap), 0, 0, 0);
        _btnRetryFailed.Visible = !banner && finishedWithFailures;
        if (banner)
            SetBanner(GenerateCorpusBanner.PartialRun, GenerationUiText.BannerHeading(runEnded),
                GenerationUiText.BannerBody(done, total, attachmentsDone, attachmentsTotal, failed, CaseUseBatch, _pendingProviderBatches,
                    otherProviderBatches: _otherProviderBatches, otherProvider: _otherProvider.ToString(), otherProviderBlocked: _otherProviderBlocked),
                GenerationUiText.BannerButton);
        else
            SetBanner(GenerateCorpusBanner.None, string.Empty, string.Empty, string.Empty);
        SetStateCardShown(false);
        RefreshHeader();
        RaiseStateChanged();
    }

    /// <summary>
    /// Partial-run banner (06-UI-SPEC S1 Row 2): heading by how the last run ended, Continue Generating; it takes the
    /// banner slot over any media banner and hides the header's Retry Failed Items.
    /// </summary>
    internal void ShowPartialBanner(bool runEnded, int threadsDone, int threadsTotal, int attachmentsDone, int attachmentsTotal,
        int failedItems)
    {
        _btnRetryFailed.Visible = false;
        SetBanner(GenerateCorpusBanner.PartialRun, GenerationUiText.BannerHeading(runEnded),
            GenerationUiText.BannerBody(threadsDone, threadsTotal, attachmentsDone, attachmentsTotal, failedItems, CaseUseBatch,
                _pendingProviderBatches, otherProviderBatches: _otherProviderBatches, otherProvider: _otherProvider.ToString(),
                otherProviderBlocked: _otherProviderBlocked),
            GenerationUiText.BannerButton);
        RaiseStateChanged();
    }

    /// <summary>
    /// Media banner after a finished run with skipped images or voicemails. Ignored while the partial-run banner shows
    /// (precedence rule); a banner button hides the header's Retry Failed Items (one Primary rule). An empty ButtonText
    /// shows no button. Every banner button starts the same run as Continue Generating.
    /// </summary>
    internal void ShowMediaBanner(MediaBannerContent content)
    {
        ArgumentNullException.ThrowIfNull(content);
        if (_bannerKind == GenerateCorpusBanner.PartialRun && _bannerShown)
            return;

        var button = content.ButtonText ?? string.Empty;
        if (button.Length > 0)
            _btnRetryFailed.Visible = false;
        SetBanner(GenerateCorpusBanner.Media, content.Heading ?? string.Empty, content.Body ?? string.Empty, button);
        RaiseStateChanged();
    }

    /// <summary>
    /// Fills the single banner card; the button sizes to its text (never ellipsized) and is hidden when empty.
    /// </summary>
    private void SetBanner(GenerateCorpusBanner kind, string heading, string body, string buttonText)
    {
        _bannerKind = kind;
        _bannerShown = kind != GenerateCorpusBanner.None;
        if (_bannerShown)
        {
            _lblBannerHeading.Text = heading;
            _lblBannerBody.Text = body;
            _btnContinue.Text = buttonText;
            if (_btnContinue is ThemedButton themed && buttonText.Length > 0)
                themed.FitToText(0, _buttonHeight);
            _bannerHasButton = buttonText.Length > 0;
            _btnContinue.Visible = _bannerHasButton;
            _bannerButtons.Visible = _bannerHasButton;
        }
        UpdateBannerVisibility();
    }

    /// <summary>
    /// During a run: bar and Cancel visible, header buttons and banner hidden; the dashboard replaces any state card.
    /// </summary>
    private void SetRunChrome(bool running)
    {
        if (_progress.Visible != running)
            _progress.Visible = running;
        if (_btnCancel.Visible != running)
            _btnCancel.Visible = running;
        if (!running)
            return;

        if (_btnStartOver.Visible)
            _btnStartOver.Visible = false;
        if (_btnRetryFailed.Visible)
            _btnRetryFailed.Visible = false;
        if (_bannerShown)
        {
            _bannerShown = false;
            _bannerKind = GenerateCorpusBanner.None;
            UpdateBannerVisibility();
        }
        if (_stateCardShown)
            SetStateCardShown(false);
    }

    private void SetStateCardShown(bool shown)
    {
        _stateCardShown = shown;
        _root.SuspendLayout();
        _bodyLayout.Visible = !shown;
        _stateCard.Visible = shown;
        _tilesRow.Visible = !shown;
        _root.ResumeLayout(true);
        UpdateBannerVisibility();
        if (shown)
            CenterStateContent();
    }

    private void UpdateBannerVisibility()
    {
        var show = _bannerShown && !_stateCardShown;
        if (_banner.Visible != show)
            _banner.Visible = show;
        SizeBanner();
    }

    private void CenterStateContent()
    {
        _stateContent.Location = new Point(
            Math.Max(0, (_stateCard.Width - _stateContent.Width) / 2),
            Math.Max(0, (_stateCard.Height - _stateContent.Height) / 2));
    }

    private void SizeBanner()
    {
        if (!_banner.Visible)
        {
            if (_bannerRow.Height != 0)
                _bannerRow.Height = 0;
            return;
        }

        // The text wraps beside the button; the card is as tall as the taller of the two plus its padding.
        // Measured from the page width (the banner spans the single root column), so it is right before the first layout.
        // A media banner without a button gives the text the full width
        var inner = Math.Max(1, _root.ClientSize.Width - _banner.Margin.Horizontal - _banner.Padding.Horizontal);
        var buttons = _bannerHasButton ? _bannerButtons.GetPreferredSize(Size.Empty) : Size.Empty;
        var buttonsMargin = _bannerHasButton ? _bannerButtons.Margin.Horizontal : 0;
        var textWidth = Math.Max(1, inner - buttons.Width - buttonsMargin);
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
    /// Scope notice (row 1); empty hides it.
    /// </summary>
    private void SetNotice(string text)
    {
        _noticeText = text ?? string.Empty;
        _lblNotice.Text = _noticeText;
        _lblNotice.Visible = _noticeText.Length > 0;
        SizeNotice();
    }

    private void SizeNotice()
    {
        var width = Math.Max(1, _root.ClientSize.Width - _lblNotice.Margin.Horizontal);
        if (_lblNotice.MaximumSize.Width != width)
            _lblNotice.MaximumSize = new Size(width, 0);
    }

    /// <summary>
    /// Subtitle: case folder and save time (or "Not saved" in Danger).
    /// </summary>
    private void RefreshHeader()
    {
        if (_state == null)
            return;

        var store = _state.CaseStore;
        var saveFailed = !string.IsNullOrEmpty(store.LastSaveError);
        _lblSubtitle.Text = CaseGuideUiText.SubtitleText(_state.Case.FolderPath, store.LastSavedUtc?.ToLocalTime(), saveFailed, MyDocuments);
        _lblSubtitle.ForeColor = saveFailed ? Theme.Danger : Theme.TextMuted;
    }

    internal void SetStatus(string text, Color color)
    {
        if (IsDisposed)
            return;

        _lblStatus.Text = text;
        _lblStatus.ForeColor = color;
    }

    // ----- Render-test access -----

    /// <summary>
    /// Binds and shows one state synchronously for render tests; never reads the journal, starts a run or saves.
    /// <paramref name="ready"/> overrides the Ready card input, <paramref name="media"/> the media banner content, and
    /// <paramref name="runEnded"/> picks the partial-run heading.
    /// </summary>
    internal void LoadForRender(WizardState state, GenerateCorpusRenderState render, GenerationView? view = null, string reason = "",
        int earlierThreads = 0, GenerationReadyInput? ready = null, MediaBannerContent? media = null, bool runEnded = false)
    {
        BindState(state);
        RefreshHeader();
        var plan = state.Case.Corpus?.Plan;
        // The Not-approved, No-plan and Guide-changed cards hide the plan's notice (09 UI-SPEC S1)
        var noNotice = render is GenerateCorpusRenderState.NotApproved or GenerateCorpusRenderState.NoPlan
            or GenerateCorpusRenderState.GuideChanged;
        SetNotice(plan == null || noNotice ? string.Empty : NoticeFor(plan));
        var counts = view?.Counts ?? new GenerationCounts();
        var failedItems = counts.FailedThreads + counts.AttachmentsFailed;
        // The banner's pending-batches clauses read the case's batch ledger by provider, as entering the step does (D-15)
        _pendingProviderBatches = plan == null ? 0 : Services.Ai.BatchLedger.PendingBatchCount(state.Case.FolderPath, plan.PlanId, state.TextProvider);
        SetOtherProviderBatches(plan == null ? 0 : Services.Ai.BatchLedger.PendingBatchCountOtherProviders(state.Case.FolderPath, plan.PlanId, state.TextProvider));

        switch (render)
        {
            case GenerateCorpusRenderState.Ready:
            case GenerateCorpusRenderState.ReadyWithMedia:
            case GenerateCorpusRenderState.ReadyMediaUnavailable:
                ShowStateCard(ready == null ? ReadyCard(plan!) : GenerationUiText.ReadyCard(ready), GenerationStateCard.Ready);
                break;
            case GenerateCorpusRenderState.Running:
            case GenerateCorpusRenderState.RunningNonRelevant:
                _isGenerating = true;
                ShowDashboard(false, 0, 0, 0, false);
                ApplyView(view!);
                RaiseStateChanged();
                break;
            case GenerateCorpusRenderState.Partial:
            case GenerateCorpusRenderState.PartialUpgrade:
                ApplyView(view!);
                ShowDashboard(true, counts.DoneThreads, counts.TotalThreads, failedItems, false,
                    runEnded || render == GenerateCorpusRenderState.PartialUpgrade, counts.AttachmentsDone, counts.AttachmentsTotal);
                // Entering a Phase 5 run shows no outcome line; the cancelled case shows the line a cancel leaves behind
                if (render == GenerateCorpusRenderState.Partial)
                    ShowOutcome(GenerationOutcome.Cancelled, counts, string.Empty, retryButtonShown: false);
                break;
            case GenerateCorpusRenderState.FinishedClean:
            case GenerateCorpusRenderState.FinishedIssues:
                ApplyView(view!);
                ShowDashboard(false, counts.DoneThreads, counts.TotalThreads, failedItems, failedItems > 0);
                ShowOutcome(GenerationOutcome.Finished, counts, string.Empty, retryButtonShown: failedItems > 0);
                break;
            case GenerateCorpusRenderState.FinishedSkippedMedia:
            case GenerateCorpusRenderState.FinishedSkippedMediaWithFailures:
            case GenerateCorpusRenderState.FinishedSkippedMediaUnavailable:
                ApplyView(view!);
                ShowDashboard(false, counts.DoneThreads, counts.TotalThreads, failedItems, failedItems > 0);
                var content = media ?? GenerationUiText.MediaBanner(counts.AttachmentsSkipped, failedItems,
                    render != GenerateCorpusRenderState.FinishedSkippedMediaUnavailable, string.Empty);
                ShowMediaBanner(content);
                // The outcome's Retry tail only names the header button when it shows
                ShowOutcome(GenerationOutcome.Finished, counts, string.Empty,
                    retryButtonShown: failedItems > 0 && string.IsNullOrEmpty(content.ButtonText));
                break;
            case GenerateCorpusRenderState.NotApproved:
                ShowStateCard(GenerationUiText.StateCard(GenerationStateCard.NotApproved, 0, 0, 0, 0m, string.Empty, 0, string.Empty), GenerationStateCard.NotApproved);
                break;
            case GenerateCorpusRenderState.NoPlan:
                ShowStateCard(GenerationUiText.StateCard(GenerationStateCard.NotApproved, 0, 0, 0, 0m, string.Empty, 0, string.Empty,
                    hasPlan: false), GenerationStateCard.NotApproved);
                break;
            case GenerateCorpusRenderState.GuideChanged:
                ShowStateCard(GenerationUiText.StateCard(GenerationStateCard.GuideChanged, 0, 0, 0, 0m, string.Empty, 0, string.Empty,
                    state.Case.Corpus?.Generation?.EmailsWritten ?? 0), GenerationStateCard.GuideChanged);
                break;
            case GenerateCorpusRenderState.PlanChanged:
                ShowStateCard(GenerationUiText.StateCard(GenerationStateCard.PlanChanged, 0, 0, 0, 0m, string.Empty, earlierThreads, string.Empty), GenerationStateCard.PlanChanged);
                break;
            default:
                ShowStateCard(GenerationUiText.StateCard(GenerationStateCard.Unreadable, 0, 0, 0, 0m, string.Empty, 0, reason), GenerationStateCard.Unreadable);
                break;
        }
    }

    /// <summary>
    /// Outcome status line in its tone (UI-SPEC outcome table); <paramref name="retryButtonShown"/> keeps the Retry tail
    /// only when the header button is showing.
    /// </summary>
    internal void ShowOutcome(GenerationOutcome outcome, GenerationCounts counts, string message, bool retryButtonShown = true)
    {
        var line = GenerationUiText.OutcomeText(outcome, counts, message, retryButtonShown);
        SetStatus(line.Text, ToneColor(line.Tone));
    }

    internal bool StateCardShown => _stateCardShown;
    internal bool BannerShown => _bannerShown && !_stateCardShown;
    internal string StateHeading => _lblStateHeading.Text;
    internal string StateBody => _lblStateBody.Text;
    internal string NoticeText => _noticeText;
    internal string BannerHeadingText => _lblBannerHeading.Text;
    internal string BannerBodyText => _lblBannerBody.Text;
    internal GenerateCorpusBanner BannerKind => _bannerShown && !_stateCardShown ? _bannerKind : GenerateCorpusBanner.None;

    internal DataGridView WavesGrid => _gridWaves;
    internal DataGridView ActivityGrid => _gridActivity;
    internal CardPanel WavesCard => _wavesCard;
    internal CardPanel ActivityCard => _activityCard;
    internal CardPanel StateCardPanel => _stateCard;
    internal CardPanel BannerPanel => _banner;
    internal IReadOnlyList<StatTile> Tiles => _tiles;
    internal Button CancelButton => _btnCancel;
    internal Button StartOverButton => _btnStartOver;
    internal Button RetryFailedButton => _btnRetryFailed;
    internal Button StateActionButton => _btnStateAction;
    internal Button ContinueButton => _btnContinue;
    internal Button BannerButton => _btnContinue;
    internal Label EstimateLabel => _lblStateEstimate;
    internal Label MediaNoticeLabel => _lblStateMediaNotice;
    internal CheckBox UseBatchCheckBox => _chkUseBatch;
    internal Label BatchHintLabel => _lblBatchHint;
    internal string EstimateTooltipText => _toolTip.GetToolTip(_lblStateEstimate) ?? string.Empty;
    internal Label StatusLabel => _lblStatus;
    internal Label NoticeLabel => _lblNotice;
    internal SlimProgressBar ProgressBar => _progress;

    /// <summary>
    /// Dot color and (for activity rows) the event behind a grid row.
    /// </summary>
    private sealed class DotTag
    {
        public Color Color;
        public GenerationEvent? Event;
    }
}
