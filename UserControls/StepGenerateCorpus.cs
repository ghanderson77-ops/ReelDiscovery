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
    Unreadable
}

/// <summary>
/// Generate Corpus step (05-UI-SPEC S1): header with save state and Start Over / Retry Failed Threads, the scope notice,
/// the partial-run banner, seven stat tiles, the Waves and Activity cards (or a full-width state card) and the status
/// strip. This file holds the layout and paints any GenerationView snapshot; StepGenerateCorpus.Run.cs holds the wizard
/// lifecycle and the entry decision. All copy comes from GenerationUiText.
/// </summary>
public partial class StepGenerateCorpus : UserControl
{
    // Activity view cap; the journal keeps everything (UI-SPEC S1)
    internal const int MaxActivityRows = 500;

    private const string WavesHint = "Oldest first. Each wave builds on the ones before.";
    private const string ActivityHint = "Newest first";

    private static readonly GenerationTileKind[] TileKinds =
    {
        GenerationTileKind.Threads,
        GenerationTileKind.Emails,
        GenerationTileKind.EmlFiles,
        GenerationTileKind.Ledger,
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
    private Button _btnStateAction = null!;

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
    private GenerationCounts? _lastCounts;
    private string _noticeText = string.Empty;

    public string StepTitle => "Generate Corpus";
    public string NextButtonText => "Next >";
    public bool CanMoveNext => !_isGenerating && !_isResetting;
    public bool CanMoveBack => !_isGenerating && !_isResetting;

    /// <summary>
    /// True while a generation run is active (closing asks C-G3).
    /// </summary>
    public bool IsGenerating => _isGenerating;

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
        _gridActivity.CellPainting += Grid_CellPainting;
        _root.SizeChanged += (s, e) =>
        {
            SizeNotice();
            SizeBanner();
        };
        _tiles[0].SizeChanged += (s, e) => ApplyThreadsTile();
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
        _btnRetryFailed = ButtonHelper.CreateButton("Retry Failed Threads", 0, _buttonHeight, ButtonStyle.Primary);
        _btnRetryFailed.Margin = new Padding(smallGap, 0, 0, 0);
        _btnRetryFailed.Visible = false;
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
            Text = GenerationUiText.BannerHeading,
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
        _bodyLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); // 0: Waves card | Activity card

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
        _btnStateAction = ButtonHelper.CreateButton("Generate Emails", 0, _buttonHeight, ButtonStyle.Primary);
        _btnStateAction.Margin = new Padding(0);

        _stateContent.Controls.Add(_lblStateHeading);
        _stateContent.Controls.Add(_lblStateBody);
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
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); // 1: Waves grid
        layout.Controls.Add(NewCardHeader(inset, "Waves", WavesHint), 0, 0);

        _gridWaves = NewGrid();
        GridStyle.AddDotColumn(_gridWaves, this);
        AddRightColumn(_gridWaves, "Wave", "Wave", 56);
        AddFillColumn(_gridWaves, "Dates", "Dates", 120);
        AddRightColumn(_gridWaves, "Threads", "Threads", 80);
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
    private void AddRightColumn(DataGridView grid, string name, string header, int width)
    {
        var column = AddTextColumn(grid, name, header, width);
        column.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
        column.DefaultCellStyle.Padding = new Padding(0, 0, LogicalToDeviceUnits(15), 0);
        column.HeaderCell.Style.Alignment = DataGridViewContentAlignment.MiddleRight;
        column.HeaderCell.Style.Padding = new Padding(0, LogicalToDeviceUnits(6), LogicalToDeviceUnits(15) - GridStyle.HeaderTextMargin, LogicalToDeviceUnits(6));
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
        _lastCounts = counts.Clone();
        for (var i = 1; i < TileKinds.Length; i++)
            _tiles[i].SetContent(GenerationUiText.Tile(TileKinds[i], counts));
        ApplyThreadsTile();

        ApplyWaves(view.Waves ?? new List<WaveProgress>());
        AddActivity(view.Events ?? new List<GenerationEvent>());

        if (counts.Running)
        {
            SetRunChrome(true);
            _progress.Marquee = false;
            _progress.Maximum = Math.Max(1, counts.TotalThreads);
            _progress.Value = Math.Min(_progress.Maximum, counts.DoneThreads + counts.FailedThreads);
            var text = GenerationUiText.ProgressText(view);
            if (text.Length > 0)
                SetStatus(text, Theme.Accent);
        }
    }

    /// <summary>
    /// Threads tile: the widest variant whose value fits the tile at TitleFont (DPI rule 19).
    /// </summary>
    private void ApplyThreadsTile()
    {
        if (_lastCounts == null)
            return;

        var tile = _tiles[0];
        // Tile card padding is 16 + 16; the value label spans the rest
        var available = tile.Width - LogicalToDeviceUnits(32);
        var variant = 0;
        if (available > 0)
        {
            using var font = Theme.TitleFont();
            while (variant < 2)
            {
                var value = GenerationUiText.Tile(GenerationTileKind.Threads, _lastCounts, variant).Value;
                if (TextRenderer.MeasureText(value, font).Width <= available)
                    break;
                variant++;
            }
        }

        var content = GenerationUiText.Tile(GenerationTileKind.Threads, _lastCounts, variant);
        if (tile.ValueText != content.Value || tile.SubLineText != content.SubLine || tile.TooltipText.Length == 0)
            tile.SetContent(content);
    }

    /// <summary>
    /// Waves grid: rows created once per wave count, then updated in place (no rebuild, no scroll jump).
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
            var wave = waves[i];
            var row = _gridWaves.Rows[i];
            var color = WaveColor(wave);
            if (row.Tag is not DotTag tag || tag.Color != color)
            {
                row.Tag = new DotTag { Color = color };
                _gridWaves.InvalidateCell(0, i);
            }

            SetCell(row, "Wave", wave.Index.ToString(System.Globalization.CultureInfo.InvariantCulture));
            SetCell(row, "Dates", GenerationUiText.WaveDates(wave.Start, wave.End));
            // Full dates first: the Dates column may trim them at the minimum window size
            row.Cells["Dates"].ToolTipText = GenerationUiText.WaveDates(wave.Start, wave.End) + Environment.NewLine
                + GenerationUiText.WaveTooltip(wave.Threads, wave.Emails);
            SetCell(row, "Threads", $"{wave.Done:N0} / {wave.Threads:N0}");
            SetCell(row, "Warnings", wave.Warnings > 0 ? wave.Warnings.ToString("N0") : string.Empty);
            SetCell(row, "Failed", wave.Failed > 0 ? wave.Failed.ToString("N0") : string.Empty);
        }

        _gridWaves.ClearSelection();
    }

    private static void SetCell(DataGridViewRow row, string column, string value)
    {
        var cell = row.Cells[column];
        if (!Equals(cell.Value as string, value))
            cell.Value = value;
    }

    private static Color WaveColor(WaveProgress wave)
    {
        if (wave.Running)
            return Theme.Accent;
        if (wave.Failed > 0)
            return Theme.Danger;
        if (wave.Done + wave.Failed < wave.Threads || wave.Threads == 0)
            return Theme.BorderStrong;
        return wave.Warnings > 0 ? Theme.Warning : Theme.Success;
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
        ButtonHelper.StyleButton(_btnStateAction, content.Danger ? ButtonStyle.Danger : ButtonStyle.Primary);
        _btnStateAction.Text = content.ButtonText;
        if (_btnStateAction is ThemedButton themed)
            themed.FitToText(0, _buttonHeight);
        _btnStateAction.Visible = content.ButtonText.Length > 0;

        SetRunChrome(false);
        _bannerShown = false;
        _btnStartOver.Visible = false;
        _btnRetryFailed.Visible = false;
        SetStateCardShown(true);
        RefreshHeader();
        RaiseStateChanged();
    }

    /// <summary>
    /// Shows the dashboard (tiles, waves, activity) after a run or on reopen: the partial-run banner when threads are
    /// left, Start Over in the header, and Retry Failed Threads when a finished run has failures (UI-SPEC Row 0 table).
    /// </summary>
    internal void ShowDashboard(bool banner, int done, int total, int failed, bool finishedWithFailures)
    {
        SetRunChrome(false);
        _bannerShown = banner;
        if (banner)
            _lblBannerBody.Text = GenerationUiText.BannerBody(done, total, failed);
        _btnStartOver.Visible = true;
        _btnRetryFailed.Visible = !banner && finishedWithFailures;
        _btnRetryFailed.Margin = new Padding(LogicalToDeviceUnits(Theme.SmallGap), 0, 0, 0);
        SetStateCardShown(false);
        RefreshHeader();
        RaiseStateChanged();
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
    /// </summary>
    internal void LoadForRender(WizardState state, GenerateCorpusRenderState render, GenerationView? view = null, string reason = "",
        int earlierThreads = 0)
    {
        BindState(state);
        RefreshHeader();
        var plan = state.Case.Corpus?.Plan;
        SetNotice(plan == null || render == GenerateCorpusRenderState.NotApproved ? string.Empty : NoticeFor(plan));
        var counts = view?.Counts ?? new GenerationCounts();

        switch (render)
        {
            case GenerateCorpusRenderState.Ready:
                ShowStateCard(ReadyCard(plan!), GenerationStateCard.Ready);
                break;
            case GenerateCorpusRenderState.Running:
                _isGenerating = true;
                ShowDashboard(false, 0, 0, 0, false);
                ApplyView(view!);
                RaiseStateChanged();
                break;
            case GenerateCorpusRenderState.Partial:
                ApplyView(view!);
                ShowDashboard(true, counts.DoneThreads, counts.TotalThreads, counts.FailedThreads, false);
                ShowOutcome(GenerationOutcome.Cancelled, counts, string.Empty);
                break;
            case GenerateCorpusRenderState.FinishedClean:
            case GenerateCorpusRenderState.FinishedIssues:
                ApplyView(view!);
                ShowDashboard(false, counts.DoneThreads, counts.TotalThreads, counts.FailedThreads, counts.FailedThreads > 0);
                ShowOutcome(GenerationOutcome.Finished, counts, string.Empty);
                break;
            case GenerateCorpusRenderState.NotApproved:
                ShowStateCard(GenerationUiText.StateCard(GenerationStateCard.NotApproved, 0, 0, 0, 0m, string.Empty, 0, string.Empty), GenerationStateCard.NotApproved);
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
    /// Outcome status line in its tone (UI-SPEC outcome table).
    /// </summary>
    internal void ShowOutcome(GenerationOutcome outcome, GenerationCounts counts, string message)
    {
        var line = GenerationUiText.OutcomeText(outcome, counts, message);
        SetStatus(line.Text, ToneColor(line.Tone));
    }

    internal bool StateCardShown => _stateCardShown;
    internal bool BannerShown => _bannerShown && !_stateCardShown;
    internal string StateHeading => _lblStateHeading.Text;
    internal string StateBody => _lblStateBody.Text;
    internal string NoticeText => _noticeText;
    internal string BannerHeadingText => _lblBannerHeading.Text;

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
