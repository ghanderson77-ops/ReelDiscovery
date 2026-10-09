using System.Diagnostics;
using System.Globalization;
using ReelDiscovery.Helpers;
using ReelDiscovery.Models;
using ReelDiscovery.Models.Case;
using ReelDiscovery.Models.Discovery;
using ReelDiscovery.Services.Case;
using ReelDiscovery.Services.Discovery;

namespace ReelDiscovery.UserControls;

/// <summary>
/// Which Discovery Documents state LoadForRender shows (render tests only; nothing is started or saved).
/// </summary>
internal enum DiscoveryRenderState
{
    NoGuide,
    Ready,
    Running,
    Results,
    MemoCriminal,
    Invalid,
    NoWitnesses
}

/// <summary>
/// Discovery Documents step (08-UI-SPEC S1): optional; this file holds layout, binding and lifecycle;
/// StepDiscoveryDocuments.Run.cs holds generation.
/// </summary>
public partial class StepDiscoveryDocuments : UserControl, IWizardStep
{
    private const string CheckColumn = "Check";
    private const string WitnessColumn = "Witness";
    private const string SideColumn = "Side";
    private const string FactsColumn = "Facts";
    private const string DocumentColumn = "Document";
    private const string StatusColumn = "Status";

    // The cap hint shows for 3 s after a 7th witness is refused (UI-SPEC S1 Card 1)
    private const int CapHintMilliseconds = 3000;

    private static readonly DiscoveryKind[] WrittenKinds =
    {
        DiscoveryKind.RequestsForProduction, DiscoveryKind.Interrogatories, DiscoveryKind.RequestsForAdmission
    };

    private WizardState _state = null!;
    private DiscoveryStepModel _model = new();
    private DiscoveryDocumentSet _set = new();
    private DiscoveryCorpus _corpus = DiscoveryCorpus.None();
    private bool _isGenerating;
    private bool _loading;
    // Set by EnterRunState while a run is active
    private CancellationTokenSource? _cts;
    private readonly ToolTip _toolTip = new();
    private readonly System.Windows.Forms.Timer _debounce = new() { Interval = 250 };
    private readonly System.Windows.Forms.Timer _capTimer = new() { Interval = CapHintMilliseconds };

    // Incremented on every enter and leave: a corpus detection that finishes after either is discarded
    private int _enterVersion;

    // Field validity from the last commit; only valid values reach the settings (T-08-40)
    private bool _pagesValid = true;
    private readonly bool[] _countValid = { true, true, true };
    private bool _capShown;
    // Set by LoadForRender: paints only, never saves or starts timers
    private bool _renderOnly;
    // Set while the Documents grid rows are rebuilt, so SelectionChanged does not repaint half a grid
    private bool _paintingRows;
    private bool _focusPending;

    private int _buttonHeight;
    private int _cornerInset;
    private int _inset;

    // Wrapping labels and the layout whose width they follow
    private readonly List<(Label Label, Control Container)> _wrapLabels = new();

    private TableLayoutPanel _root = null!;
    private Label _lblTitle = null!;
    private Label _lblSubtitle = null!;
    private Button _btnOpenFolder = null!;
    private Label _lblNotice = null!;

    private Panel _body = null!;
    private TableLayoutPanel _cards = null!;
    private CardPanel _stateCard = null!;
    private FlowLayoutPanel _stateContent = null!;
    private Label _lblStateHeading = null!;
    private Label _lblStateBody = null!;

    // Card 1: Depositions
    private Label _lblDepositionsCaption = null!;
    private Label _lblSelectionHint = null!;
    private DataGridView _gridWitnesses = null!;
    private Label _lblNoWitnesses = null!;
    private Label _lblPages = null!;
    private TextBox _txtPages = null!;
    private CardPanel _hostPages = null!;
    private Label _lblPagesHint = null!;

    // Card 2: Written discovery
    private TableLayoutPanel _rowRfp = null!;
    private TableLayoutPanel _rowRog = null!;
    private TableLayoutPanel _rowRfa = null!;
    private CheckBox _chkRfp = null!;
    private CheckBox _chkRog = null!;
    private CheckBox _chkRfa = null!;
    private TextBox _txtRfp = null!;
    private TextBox _txtRog = null!;
    private TextBox _txtRfa = null!;
    private CardPanel _hostRfp = null!;
    private CardPanel _hostRog = null!;
    private CardPanel _hostRfa = null!;
    private Label _lblCountsHint = null!;
    private Label _lblMemoNote = null!;
    private Label _lblServedBy = null!;
    private ComboBox _cmbServedBy = null!;
    private CardPanel _hostServedBy = null!;
    private Label _lblServedOn = null!;
    private ComboBox _cmbServedOn = null!;
    private CardPanel _hostServedOn = null!;
    private Label _lblPartyHint = null!;

    // Card 3: Documents
    private Label _lblSpent = null!;
    private DataGridView _gridDocuments = null!;
    private Label _lblNoDocuments = null!;
    private Label _lblDetail = null!;
    private Button _btnOpenWord = null!;
    private Button _btnRegenerate = null!;
    private Button _btnSaveAgain = null!;
    private Label _lblEstimate = null!;
    private Label _lblStatus = null!;
    private SlimProgressBar _progress = null!;
    private Button _btnRun = null!;

    /// <summary>
    /// Yes/No confirmation (title, text, icon); default button = the safe choice. Test seam.
    /// </summary>
    internal Func<string, string, MessageBoxIcon, bool> Confirm = null!;

    /// <summary>
    /// Saves the case through the existing case-save path; false when the save failed. Test seam.
    /// </summary>
    internal Func<Task<bool>> SaveCase = null!;

    /// <summary>
    /// True when the current provider has a tested key. Test seam.
    /// </summary>
    internal Func<bool> KeyReady = null!;

    /// <summary>
    /// File probe for the Documents rows (validated paths only). Test seam.
    /// </summary>
    internal Func<string, bool> FileExists = File.Exists;

    public string StepTitle => DiscoveryUiText.StepTitle;
    public string NextButtonText => "Next >";
    public bool CanMoveNext => !_isGenerating;
    public bool CanMoveBack => !_isGenerating;

    /// <summary>
    /// True while discovery documents are being written (closing asks C-D2).
    /// </summary>
    public bool IsGenerating => _isGenerating;

    public event EventHandler? StateChanged;

    public StepDiscoveryDocuments()
    {
        // Default button = the safe choice (No) for every confirmation on this step
        Confirm = (title, text, icon) => MessageBox.Show(FindForm(), text, title, MessageBoxButtons.YesNo, icon,
            MessageBoxDefaultButton.Button2) == DialogResult.Yes;
        SaveCase = DefaultSaveCaseAsync;
        KeyReady = () => _state?.CanProceedFromKeys() == true;

        InitializeUI();
        WireHandlers();
        WireRunHandlers();
    }

    // Implemented in StepDiscoveryDocuments.Run.cs (Plan 08-17): Generate/Cancel, Regenerate..., Save Again, Open in Word
    partial void WireRunHandlers();

    private CaseDraft Draft => _state.Case;

    // The case's selections; defaults are written by ApplyDefaults on enter
    private DiscoverySettings Settings => Draft.Discovery ??= DiscoveryStepModel.Defaults(Draft);

    private void RaiseStateChanged() => StateChanged?.Invoke(this, EventArgs.Empty);

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _debounce.Dispose();
            _capTimer.Dispose();
            _toolTip.Dispose();
        }
        base.Dispose(disposing);
    }

    // ----- Layout -----

    private void InitializeUI()
    {
        // Content aligns with the wizard's page margin (the shell supplies it)
        this.Padding = new Padding(0);
        this.BackColor = Theme.Background;
        var gap = LogicalToDeviceUnits(Theme.Gap);
        var smallGap = LogicalToDeviceUnits(Theme.SmallGap);
        _buttonHeight = LogicalToDeviceUnits(34);
        _cornerInset = LogicalToDeviceUnits(8);
        // Full-bleed cards: content sits 15 inside the 1 px border (Content x = 16)
        _inset = LogicalToDeviceUnits(15);

        _root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(0),
            BackColor = Theme.Background
        };
        _root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        _root.RowStyles.Add(new RowStyle(SizeType.AutoSize));     // 0: Header
        _root.RowStyles.Add(new RowStyle(SizeType.AutoSize));     // 1: Notice line
        _root.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); // 2: Body (three cards, or the no-guide state card)

        BuildHeader(gap, smallGap);
        BuildNotice(gap);
        BuildBody(gap, smallGap);

        this.Controls.Add(_root);
    }

    private void BuildHeader(int gap, int smallGap)
    {
        var header = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 2,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Margin = new Padding(0, 0, 0, smallGap)
        };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        header.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // 0: Title
        header.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // 1: Subtitle

        _lblTitle = new Label
        {
            Text = DiscoveryUiText.HeaderTitle,
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
            Text = DiscoveryUiText.HeaderSubtitle,
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

        _btnOpenFolder = ButtonHelper.CreateButton(DiscoveryUiText.OpenFolderButton, 0, _buttonHeight, ButtonStyle.Default);
        _btnOpenFolder.Anchor = AnchorStyles.Right;
        _btnOpenFolder.Margin = new Padding(gap, 0, 0, 0);
        _btnOpenFolder.Visible = false;

        header.Controls.Add(_lblTitle, 0, 0);
        header.Controls.Add(_lblSubtitle, 0, 1);
        header.Controls.Add(_btnOpenFolder, 1, 0);
        header.SetRowSpan(_btnOpenFolder, 2);
        _root.Controls.Add(header, 0, 0);
    }

    private void BuildNotice(int gap)
    {
        // Wraps to the page width (SizeNotice)
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

    private void BuildBody(int gap, int smallGap)
    {
        _body = new Panel { Dock = DockStyle.Fill, Margin = new Padding(0), BackColor = Theme.Background };

        _cards = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 1,
            Margin = new Padding(0),
            Padding = new Padding(0),
            BackColor = Theme.Background
        };
        _cards.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33F));
        _cards.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33F));
        _cards.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.34F));
        _cards.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); // 0: Depositions | Written discovery | Documents

        var depositions = BuildDepositionsCard(smallGap);
        depositions.Margin = new Padding(0, 0, gap, 0);
        var written = BuildWrittenCard(gap, smallGap);
        written.Margin = new Padding(0, 0, gap, 0);
        var documents = BuildDocumentsCard(smallGap);
        documents.Margin = new Padding(0);
        _cards.Controls.Add(depositions, 0, 0);
        _cards.Controls.Add(written, 1, 0);
        _cards.Controls.Add(documents, 2, 0);
        _body.Controls.Add(_cards);

        // No-guide state card (05 state-card anatomy, body max width 520); replaces the three cards
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
            Text = DiscoveryUiText.NoGuideHeading,
            AutoSize = true,
            UseMnemonic = false,
            Font = Theme.HeadingFont(),
            ForeColor = Theme.Text,
            BackColor = Theme.Surface,
            Margin = new Padding(0, 0, 0, smallGap)
        };
        _lblStateBody = new Label
        {
            Text = DiscoveryUiText.NoGuideBody,
            AutoSize = true,
            UseMnemonic = false,
            MaximumSize = new Size(LogicalToDeviceUnits(520), 0),
            Font = Theme.BodyFont(),
            ForeColor = Theme.TextMuted,
            BackColor = Theme.Surface,
            Margin = new Padding(0)
        };
        _stateContent.Controls.Add(_lblStateHeading);
        _stateContent.Controls.Add(_lblStateBody);
        _stateCard.Controls.Add(_stateContent);
        _body.Controls.Add(_stateCard);
        _root.Controls.Add(_body, 0, 2);
    }

    private CardPanel BuildDepositionsCard(int smallGap)
    {
        var (card, layout) = NewCard();
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));     // 0: Card header (caption | selection hint)
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); // 1: Witness grid (or the no-witness text)
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));     // 2: Pages row (label | field)
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));     // 3: Pages hint

        layout.Controls.Add(NewCardHeader(DiscoveryUiText.DepositionsCaption(DiscoveryVariant.Civil), out _lblDepositionsCaption,
            out _lblSelectionHint), 0, 0);

        _gridWitnesses = NewGrid();
        _gridWitnesses.ReadOnly = false;
        var check = new DataGridViewCheckBoxColumn
        {
            Name = CheckColumn,
            HeaderText = string.Empty,
            Width = LogicalToDeviceUnits(28),
            AutoSizeMode = DataGridViewAutoSizeColumnMode.None,
            Resizable = DataGridViewTriState.False,
            SortMode = DataGridViewColumnSortMode.NotSortable
        };
        check.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
        check.DefaultCellStyle.Padding = Padding.Empty;
        _gridWitnesses.Columns.Add(check);
        AddFillColumn(_gridWitnesses, WitnessColumn, "Witness", 96);
        var side = AddTextColumn(_gridWitnesses, SideColumn, "Side", 80);
        // DPI rule 35: Side fits Non-party, Complainant and Subject without ellipsis
        using (var body = Theme.BodyFont())
        {
            var widest = new[] { "Non-party", "Complainant", "Subject", "Plaintiff", "Defense" }
                .Max(t => TextRenderer.MeasureText(t, body).Width);
            side.Width = Math.Max(side.Width, widest + LogicalToDeviceUnits(15 + 6));
        }
        var facts = AddRightColumn(_gridWitnesses, FactsColumn, "Facts", 52);
        facts.ToolTipText = DiscoveryUiText.FactsHeaderTooltip;
        using (var small = Theme.SmallFont())
        {
            // Header text plus its paddings (left 13, right 13) must fit without ellipsis
            var header = TextRenderer.MeasureText("Facts", small).Width + 2 * (LogicalToDeviceUnits(15) - GridStyle.HeaderTextMargin)
                + 2 * GridStyle.HeaderTextMargin;
            facts.Width = Math.Max(facts.Width, header);
        }

        _lblNoWitnesses = new Label
        {
            Text = DiscoveryUiText.NoWitnessesText,
            AutoSize = false,
            Dock = DockStyle.Fill,
            UseMnemonic = false,
            TextAlign = ContentAlignment.TopLeft,
            Padding = new Padding(_inset, LogicalToDeviceUnits(4), _inset, 0),
            Font = Theme.BodyFont(),
            ForeColor = Theme.TextMuted,
            BackColor = Theme.Surface,
            Visible = false
        };
        layout.Controls.Add(NewGridHost(_gridWitnesses, _lblNoWitnesses), 0, 1);

        var pagesRow = NewRowLayout();
        pagesRow.Margin = new Padding(_inset, smallGap, _inset, 0);
        _lblPages = new Label
        {
            Text = DiscoveryUiText.PagesLabel,
            AutoSize = false,
            UseMnemonic = false,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            Margin = new Padding(0),
            Font = Theme.BodyFont(),
            ForeColor = Theme.Text,
            BackColor = Theme.Surface
        };
        _txtPages = new TextBox { TextAlign = HorizontalAlignment.Right, MaxLength = 2 };
        _hostPages = NumberHost(_txtPages, smallGap);
        _lblPages.Height = _hostPages.Height;
        pagesRow.Controls.Add(_lblPages, 0, 0);
        pagesRow.Controls.Add(_hostPages, 1, 0);
        layout.Controls.Add(pagesRow, 0, 2);
        _toolTip.SetToolTip(_txtPages, DiscoveryUiText.PagesTooltip);

        _lblPagesHint = WrapLabel(layout, Theme.SmallFont(), Theme.TextMuted,
            new Padding(_inset, LogicalToDeviceUnits(4), _inset, _cornerInset));
        _lblPagesHint.Text = DiscoveryUiText.PagesHintValid;
        layout.Controls.Add(_lblPagesHint, 0, 3);
        return card;
    }

    private CardPanel BuildWrittenCard(int gap, int smallGap)
    {
        var (card, layout) = NewCard();
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));     // 0: Card header (caption)
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));     // 1: RFP row (checkbox | count)
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));     // 2: ROG row (checkbox | count)
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));     // 3: RFA row (checkbox | count)
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));     // 4: Counts hint
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));     // 5: Memo note (hidden when empty)
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));     // 6: "Served by" label
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));     // 7: Served-by combo
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));     // 8: "Served on" label
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));     // 9: Served-on combo
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));     // 10: Party hint (hidden when empty)
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); // 11: Filler

        layout.Controls.Add(NewCardHeader("Written discovery", out _, out _), 0, 0);

        (_rowRfp, _chkRfp, _txtRfp, _hostRfp) = NewRequestRow(smallGap, 0);
        (_rowRog, _chkRog, _txtRog, _hostRog) = NewRequestRow(smallGap, smallGap);
        (_rowRfa, _chkRfa, _txtRfa, _hostRfa) = NewRequestRow(smallGap, smallGap);
        _chkRfp.Text = DiscoveryUiText.KindLabel(DiscoveryKind.RequestsForProduction, DiscoveryVariant.Civil);
        _chkRog.Text = DiscoveryUiText.KindLabel(DiscoveryKind.Interrogatories, DiscoveryVariant.Civil);
        _chkRfa.Text = DiscoveryUiText.KindLabel(DiscoveryKind.RequestsForAdmission, DiscoveryVariant.Civil);
        layout.Controls.Add(_rowRfp, 0, 1);
        layout.Controls.Add(_rowRog, 0, 2);
        layout.Controls.Add(_rowRfa, 0, 3);

        var xs = LogicalToDeviceUnits(4);
        _lblCountsHint = WrapLabel(layout, Theme.SmallFont(), Theme.TextMuted, new Padding(_inset, xs, _inset, 0));
        layout.Controls.Add(_lblCountsHint, 0, 4);
        _lblMemoNote = WrapLabel(layout, Theme.SmallFont(), Theme.TextMuted, new Padding(_inset, xs, _inset, 0));
        _lblMemoNote.Visible = false;
        layout.Controls.Add(_lblMemoNote, 0, 5);

        var (by, on) = DiscoveryUiText.PartyLabels(memo: false);
        _lblServedBy = FieldLabel(by, new Padding(_inset, gap, _inset, xs));
        layout.Controls.Add(_lblServedBy, 0, 6);
        _cmbServedBy = NewCombo();
        _hostServedBy = HostCombo(_cmbServedBy);
        _hostServedBy.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        _hostServedBy.Margin = new Padding(_inset, 0, _inset, 0);
        layout.Controls.Add(_hostServedBy, 0, 7);

        _lblServedOn = FieldLabel(on, new Padding(_inset, smallGap, _inset, xs));
        layout.Controls.Add(_lblServedOn, 0, 8);
        _cmbServedOn = NewCombo();
        _hostServedOn = HostCombo(_cmbServedOn);
        _hostServedOn.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        _hostServedOn.Margin = new Padding(_inset, 0, _inset, 0);
        layout.Controls.Add(_hostServedOn, 0, 9);

        _lblPartyHint = WrapLabel(layout, Theme.SmallFont(), Theme.TextMuted, new Padding(_inset, xs, _inset, 0));
        _lblPartyHint.Visible = false;
        layout.Controls.Add(_lblPartyHint, 0, 10);
        layout.Controls.Add(new Panel { Dock = DockStyle.Fill, Margin = new Padding(0), BackColor = Theme.Surface }, 0, 11);
        return card;
    }

    private CardPanel BuildDocumentsCard(int smallGap)
    {
        var (card, layout) = NewCard();
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));     // 0: Card header (caption | spent hint)
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); // 1: Documents grid (or the empty text)
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));     // 2: Selected-row detail
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));     // 3: Row buttons
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));     // 4: Divider
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));     // 5: Estimate line
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));     // 6: Status line (hidden when empty)
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));     // 7: Progress bar (visible only while running)
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));     // 8: Run button

        layout.Controls.Add(NewCardHeader(DiscoveryUiText.DocumentsCaption, out _, out _lblSpent), 0, 0);

        _gridDocuments = NewGrid();
        GridStyle.AddDotColumn(_gridDocuments, this);
        AddFillColumn(_gridDocuments, DocumentColumn, "Document", 112);
        AddTextColumn(_gridDocuments, StatusColumn, "Status", 112);
        _lblNoDocuments = new Label
        {
            Text = DiscoveryUiText.EmptyDocumentsText,
            AutoSize = false,
            Dock = DockStyle.Fill,
            UseMnemonic = false,
            TextAlign = ContentAlignment.TopLeft,
            Padding = new Padding(_inset, LogicalToDeviceUnits(4), _inset, 0),
            Font = Theme.BodyFont(),
            ForeColor = Theme.TextMuted,
            BackColor = Theme.Surface,
            Visible = false
        };
        layout.Controls.Add(NewGridHost(_gridDocuments, _lblNoDocuments), 0, 1);

        _lblDetail = new Label
        {
            AutoSize = false,
            AutoEllipsis = true,
            UseMnemonic = false,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            Margin = new Padding(_inset, smallGap, _inset, 0),
            Font = Theme.SmallFont(),
            ForeColor = Theme.TextMuted,
            BackColor = Theme.Surface
        };
        _lblDetail.Height = _lblDetail.PreferredHeight;
        layout.Controls.Add(_lblDetail, 0, 2);

        var buttons = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Anchor = AnchorStyles.Right,
            Margin = new Padding(_inset, smallGap, _inset, 0),
            Padding = new Padding(0),
            BackColor = Theme.Surface
        };
        _btnOpenWord = ButtonHelper.CreateButton(DiscoveryUiText.OpenInWordButton, 0, _buttonHeight, ButtonStyle.Default);
        _btnOpenWord.Margin = new Padding(0);
        // Regenerate... and Save Again share one slot: only one of them is visible
        _btnRegenerate = ButtonHelper.CreateButton(DiscoveryUiText.RegenerateButton, 0, _buttonHeight, ButtonStyle.Default);
        _btnRegenerate.Margin = new Padding(smallGap, 0, 0, 0);
        _btnSaveAgain = ButtonHelper.CreateButton(DiscoveryUiText.SaveAgainButton, 0, _buttonHeight, ButtonStyle.Default);
        _btnSaveAgain.Margin = new Padding(smallGap, 0, 0, 0);
        _btnSaveAgain.Visible = false;
        buttons.Controls.Add(_btnOpenWord);
        buttons.Controls.Add(_btnRegenerate);
        buttons.Controls.Add(_btnSaveAgain);
        layout.Controls.Add(buttons, 0, 3);

        var divider = new Panel
        {
            Dock = DockStyle.Fill,
            Height = Math.Max(1, LogicalToDeviceUnits(1)),
            BackColor = Theme.Border,
            Margin = new Padding(_inset, LogicalToDeviceUnits(12), _inset, LogicalToDeviceUnits(12))
        };
        layout.Controls.Add(divider, 0, 4);

        _lblEstimate = WrapLabel(layout, Theme.SmallFont(), Theme.TextMuted, new Padding(_inset, 0, _inset, 0));
        layout.Controls.Add(_lblEstimate, 0, 5);

        _lblStatus = new Label
        {
            AutoSize = false,
            AutoEllipsis = true,
            UseMnemonic = false,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            Margin = new Padding(_inset, LogicalToDeviceUnits(4), _inset, 0),
            Font = Theme.SmallFont(),
            ForeColor = Theme.TextMuted,
            BackColor = Theme.Surface,
            Visible = false
        };
        _lblStatus.Height = _lblStatus.PreferredHeight;
        layout.Controls.Add(_lblStatus, 0, 6);

        _progress = new SlimProgressBar
        {
            Height = LogicalToDeviceUnits(6),
            Anchor = AnchorStyles.Left | AnchorStyles.Right,
            Margin = new Padding(_inset, LogicalToDeviceUnits(4), _inset, 0),
            Visible = false
        };
        layout.Controls.Add(_progress, 0, 7);

        _btnRun = ButtonHelper.CreateButton(DiscoveryUiText.GenerateButton, 0, _buttonHeight, ButtonStyle.Primary);
        _btnRun.Anchor = AnchorStyles.Right;
        _btnRun.Margin = new Padding(_inset, smallGap, _inset, _cornerInset);
        layout.Controls.Add(_btnRun, 0, 8);
        return card;
    }

    /// <summary>
    /// Full-bleed card: 1 px sides, top and bottom inset by the corner radius so white content never covers the rounded
    /// corners; rows add the 15 px content inset themselves, so text sits 16 from the border and grids span the card.
    /// </summary>
    private (CardPanel Card, TableLayoutPanel Layout) NewCard()
    {
        var card = new CardPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(1, _cornerInset, 1, _cornerInset),
            BackColor = Theme.Surface
        };
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            Margin = new Padding(0),
            Padding = new Padding(0),
            BackColor = Theme.Surface
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        card.Controls.Add(layout);
        return (card, layout);
    }

    /// <summary>
    /// Card header: caption (LabelFont) left, muted hint right (ellipsizes before it collides), bottom margin 8.
    /// </summary>
    private TableLayoutPanel NewCardHeader(string caption, out Label captionLabel, out Label hintLabel)
    {
        var header = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Margin = new Padding(_inset, Math.Max(0, LogicalToDeviceUnits(14) - _cornerInset + 1), _inset, LogicalToDeviceUnits(Theme.SmallGap)),
            Padding = new Padding(0),
            BackColor = Theme.Surface
        };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        header.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // 0: Caption | hint
        var minHeight = LogicalToDeviceUnits(28);
        captionLabel = new Label
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
        };
        hintLabel = new Label
        {
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
        };
        header.Controls.Add(captionLabel, 0, 0);
        header.Controls.Add(hintLabel, 1, 0);
        return header;
    }

    // The grid and its empty-state text share the Percent row; one of them is visible
    private Panel NewGridHost(DataGridView grid, Label empty)
    {
        var host = new Panel
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(0),
            Padding = new Padding(0),
            BackColor = Theme.Surface
        };
        host.Controls.Add(grid);
        host.Controls.Add(empty);
        return host;
    }

    private DataGridView NewGrid()
    {
        var grid = new DataGridView
        {
            Dock = DockStyle.Fill,
            ReadOnly = true,
            Margin = new Padding(0)
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
            ReadOnly = true,
            Width = LogicalToDeviceUnits(width),
            AutoSizeMode = DataGridViewAutoSizeColumnMode.None,
            Resizable = DataGridViewTriState.False,
            // Rank and worklist order are the meaning: no sorting
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
            ReadOnly = true,
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
            FillWeight = 100,
            MinimumWidth = LogicalToDeviceUnits(minWidth),
            SortMode = DataGridViewColumnSortMode.NotSortable
        });
    }

    /// <summary>
    /// Right-aligned column: header and cell text end 15 inside the grid (16 from the card's outer edge).
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

    // Two columns: Percent 100 (label or checkbox) | AutoSize (64-wide field)
    private static TableLayoutPanel NewRowLayout()
    {
        var row = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(0),
            BackColor = Theme.Surface
        };
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        row.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // 0: Label | field
        return row;
    }

    // Count and pages fields: 64 wide, two digits, right-aligned (UI-SPEC U51)
    private CardPanel NumberHost(TextBox textBox, int smallGap)
    {
        var host = TextFieldHost.Create(this, textBox);
        host.Dock = DockStyle.None;
        host.Anchor = AnchorStyles.Left;
        host.Width = LogicalToDeviceUnits(64);
        host.Margin = new Padding(smallGap, 0, 0, 0);
        return host;
    }

    private (TableLayoutPanel Row, CheckBox Check, TextBox Text, CardPanel Host) NewRequestRow(int smallGap, int top)
    {
        var row = NewRowLayout();
        row.Margin = new Padding(_inset, top, _inset, 0);
        var check = new CheckBox
        {
            AutoSize = true,
            FlatStyle = FlatStyle.System,
            Font = Theme.BodyFont(),
            ForeColor = Theme.Text,
            BackColor = Theme.Surface,
            UseMnemonic = false,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(0)
        };
        var text = new TextBox { TextAlign = HorizontalAlignment.Right, MaxLength = 2 };
        var host = NumberHost(text, smallGap);
        row.Controls.Add(check, 0, 0);
        row.Controls.Add(host, 1, 0);
        return (row, check, text, host);
    }

    private Label FieldLabel(string text, Padding margin) => new()
    {
        Text = text,
        AutoSize = true,
        UseMnemonic = false,
        Anchor = AnchorStyles.Left,
        Margin = margin,
        Font = Theme.BodyFont(),
        ForeColor = Theme.Text,
        BackColor = Theme.Surface
    };

    private static ComboBox NewCombo() => new()
    {
        DropDownStyle = ComboBoxStyle.DropDownList,
        FlatStyle = FlatStyle.Flat,
        Font = Theme.BodyFont(),
        ForeColor = Theme.Text,
        BackColor = Theme.Surface,
        Margin = new Padding(0)
    };

    /// <summary>
    /// Puts a drop-down list in the same rounded 34-high border as the text fields, with its text on the text fields'
    /// 10 px inner edge (copy of GuideTabPanel.HostCombo).
    /// </summary>
    private CardPanel HostCombo(ComboBox combo)
    {
        var height = LogicalToDeviceUnits(34);
        // The flat drop-down list draws its text 3 px in from its own edge
        var left = LogicalToDeviceUnits(10) - LogicalToDeviceUnits(3);
        var vertical = Math.Max(1, (height - combo.PreferredHeight) / 2);
        var host = new CardPanel
        {
            Radius = Theme.Radius,
            BorderColor = Theme.BorderStrong,
            BackColor = Theme.Surface,
            Height = height,
            Margin = Padding.Empty,
            Padding = new Padding(left, vertical, LogicalToDeviceUnits(4), 0)
        };
        combo.Dock = DockStyle.Fill;

        // A disabled drop-down list paints a grey system frame inside the rounded border; while disabled the host shows
        // the choice as plain text instead, like a read-only text field
        var readOnlyText = new Label
        {
            Dock = DockStyle.Top,
            Height = Math.Max(1, height - 2 * vertical),
            AutoSize = false,
            AutoEllipsis = true,
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = Padding.Empty,
            UseMnemonic = false,
            Font = Theme.BodyFont(),
            ForeColor = Theme.TextMuted,
            BackColor = Theme.Surface,
            Visible = false
        };
        host.Controls.Add(combo);
        host.Controls.Add(readOnlyText);

        // Wire handlers only after both controls exist
        void Sync()
        {
            readOnlyText.Text = combo.Text;
            readOnlyText.Visible = !combo.Enabled;
            combo.Visible = combo.Enabled;
        }
        combo.EnabledChanged += (s, e) => Sync();
        combo.SelectedIndexChanged += (s, e) => readOnlyText.Text = combo.Text;
        // DPI rule 38: the list is at least as wide as the host and widens to the longest item
        host.SizeChanged += (s, e) => FitDropDown(combo);
        return host;
    }

    private static void FitDropDown(ComboBox combo)
    {
        var widest = 0;
        foreach (var item in combo.Items)
            widest = Math.Max(widest, TextRenderer.MeasureText(item?.ToString() ?? string.Empty, combo.Font).Width);
        var width = Math.Max(combo.Parent?.Width ?? combo.Width, widest + SystemInformation.VerticalScrollBarWidth + combo.LogicalToDeviceUnits(12));
        if (width > 0 && combo.DropDownWidth != width)
            combo.DropDownWidth = width;
    }

    /// <summary>
    /// A label that wraps to its container's content width (minus its own margins) and grows to fit its text.
    /// </summary>
    private Label WrapLabel(Control container, Font font, Color color, Padding margin)
    {
        var label = new Label
        {
            AutoSize = true,
            UseMnemonic = false,
            Margin = margin,
            Padding = new Padding(0),
            Font = font,
            ForeColor = color,
            BackColor = Theme.Surface
        };
        _wrapLabels.Add((label, container));
        return label;
    }

    private void SizeWrapLabels(Control container)
    {
        foreach (var (label, owner) in _wrapLabels)
        {
            if (owner != container)
                continue;
            var width = Math.Max(1, container.ClientSize.Width - container.Padding.Horizontal - label.Margin.Horizontal);
            if (label.MaximumSize.Width != width)
                label.MaximumSize = new Size(width, 0);
        }
    }

    private void SizeNotice()
    {
        var width = Math.Max(1, _root.ClientSize.Width - _lblNotice.Margin.Horizontal);
        if (_lblNotice.MaximumSize.Width != width)
            _lblNotice.MaximumSize = new Size(width, 0);
    }

    private void CenterStateContent()
    {
        _stateContent.Location = new Point(
            Math.Max(0, (_stateCard.Width - _stateContent.Width) / 2),
            Math.Max(0, (_stateCard.Height - _stateContent.Height) / 2));
    }

    // Witness inputs, count fields and party pickers, in Card 1 / Card 2 order
    private CheckBox[] KindChecks => new[] { _chkRfp, _chkRog, _chkRfa };
    private TextBox[] KindTexts => new[] { _txtRfp, _txtRog, _txtRfa };
    private CardPanel[] KindHosts => new[] { _hostRfp, _hostRog, _hostRfa };
    private TableLayoutPanel[] KindRows => new[] { _rowRfp, _rowRog, _rowRfa };

    private void WireHandlers()
    {
        // Wire handlers only after every control exists (handlers read other controls; research Pitfall 8)
        _root.SizeChanged += (s, e) => SizeNotice();
        foreach (var container in _wrapLabels.Select(w => w.Container).Distinct())
        {
            var owner = container;
            owner.SizeChanged += (s, e) => SizeWrapLabels(owner);
        }
        _stateCard.Resize += (s, e) => CenterStateContent();
        _stateContent.SizeChanged += (s, e) => CenterStateContent();
        _lblStatus.TextChanged += (s, e) => _toolTip.SetToolTip(_lblStatus, _lblStatus.Text);

        _gridWitnesses.CurrentCellDirtyStateChanged += (s, e) =>
        {
            // Commit the check on click so CellValueChanged fires at once
            if (_gridWitnesses.IsCurrentCellDirty)
                _gridWitnesses.CommitEdit(DataGridViewDataErrorContexts.Commit);
        };
        _gridWitnesses.CellValueChanged += WitnessGrid_CellValueChanged;

        foreach (var check in KindChecks)
            check.CheckedChanged += (s, e) => OnKindCheckedChanged();
        foreach (var text in KindTexts.Append(_txtPages))
        {
            text.TextChanged += (s, e) =>
            {
                if (_loading)
                    return;
                // 250 ms debounce for text fields (UI-SPEC S1 interaction rules)
                _debounce.Stop();
                _debounce.Start();
            };
        }
        _debounce.Tick += (s, e) => CommitPendingInput();
        _capTimer.Tick += (s, e) =>
        {
            _capTimer.Stop();
            _capShown = false;
            PaintSelectionHint();
        };
        _cmbServedBy.SelectedIndexChanged += (s, e) => OnPartyChanged();
        _cmbServedOn.SelectedIndexChanged += (s, e) => OnPartyChanged();

        _gridDocuments.SelectionChanged += (s, e) =>
        {
            if (!_paintingRows)
                PaintSelection();
        };
        _gridDocuments.CellPainting += DocumentsGrid_CellPainting;

        VisibleChanged += (s, e) =>
        {
            if (Visible && _focusPending)
                FocusInitial();
        };
    }

    // ----- IWizardStep lifecycle -----

    public void BindState(WizardState state)
    {
        _state = state;
    }

    public async Task OnEnterStepAsync()
    {
        if (_state == null || _isGenerating)
            return;

        var version = ++_enterVersion;
        _renderOnly = false;
        if (Draft.Guide != null)
            DiscoveryStepModel.ApplyDefaults(Draft);
        _set = LoadSet();
        _corpus = DiscoveryCorpus.None();

        // Paint at once without corpus enrichment; the journal/plan read never runs on the UI thread (T-08-42)
        _model = BuildModel();
        PopulateInputs();
        PaintAll();
        FocusInitial();

        if (!_model.HasGuide)
            return;

        var draft = Draft;
        var caseId = draft.CaseId;
        var corpus = await Task.Run(() => CorpusDetector.Detect(draft));

        // Leaving the step, switching case or starting a run during the probe discards its result
        if (IsDisposed || _enterVersion != version || Draft.CaseId != caseId || _isGenerating)
            return;

        _corpus = corpus;
        RefreshModel();
        FocusInitial();
    }

    public Task OnLeaveStepAsync()
    {
        _enterVersion++;
        // A field typed within the debounce window still reaches the settings
        if (_debounce.Enabled && _state != null)
            CommitPendingInput();
        return Task.CompletedTask;
    }

    /// <summary>
    /// Next never blocks (D-07): valid field values are committed, the case is saved, and the step always returns true.
    /// </summary>
    public async Task<bool> ValidateStepAsync()
    {
        if (_state == null)
            return true;

        CommitPendingInput();
        var saved = await SaveCase();
        if (!saved && !IsDisposed)
            SetStatus(CaseGuideUiText.SaveFailedText(_state.CaseStore.LastSaveError), Theme.Danger);
        return true;
    }

    /// <summary>
    /// Leaving the step during a run asks C-D1: No keeps the user here, Yes stops the run (finished documents are saved).
    /// </summary>
    public bool ConfirmLeave()
    {
        if (!_isGenerating)
            return true;

        var (title, text) = DiscoveryUiText.CD1CancelGeneration();
        if (!Confirm(title, text, MessageBoxIcon.Question))
            return false;

        StopRun();
        return true;
    }

    /// <summary>
    /// Closing the app after C-D2: cancels the run and pumps messages until it unwinds (finished documents are on disk).
    /// </summary>
    public void CancelAndWaitForRun(TimeSpan timeout)
    {
        if (!_isGenerating)
            return;

        StopRun();
        var clock = Stopwatch.StartNew();
        while (_isGenerating && clock.Elapsed < timeout)
        {
            Application.DoEvents();
            if (_isGenerating)
                Thread.Sleep(15);
        }
    }

    /// <summary>
    /// Run chrome on: the token source the run cancels through, inputs locked, Cancel Generation in the run slot, Back and
    /// Next disabled. The run code (Plan 08-17) calls this as a run starts.
    /// </summary>
    internal void EnterRunState(CancellationTokenSource cts)
    {
        _cts = cts;
        _isGenerating = true;
        PaintRunChrome();
        PaintHeaderButton();
        RaiseStateChanged();
    }

    /// <summary>
    /// Run chrome off once the run has unwound; the run code disposes its token source.
    /// </summary>
    internal void ExitRunState()
    {
        _cts = null;
        _isGenerating = false;
        if (IsDisposed)
            return;
        PaintRunChrome();
        PaintHeaderButton();
        RaiseStateChanged();
    }

    private void StopRun()
    {
        try
        {
            _cts?.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // The run released its token source while finishing; it is unwinding already
        }
        SetStatus(DiscoveryUiText.StatusStopping, Theme.Accent);
    }

    private Task<bool> DefaultSaveCaseAsync() =>
        _state != null && CaseStore.CanSave(Draft) ? _state.CaseStore.SaveAsync(Draft) : Task.FromResult(true);

    // Saves in the background after an input change (the store coalesces queued snapshots, latest wins)
    private void QueueSave()
    {
        if (_renderOnly || _state == null)
            return;
        _ = SaveQuietlyAsync();
    }

    // A failure is surfaced in the status line, never swallowed: saving is core work
    private async Task SaveQuietlyAsync()
    {
        var ok = await SaveCase();
        if (!ok && !IsDisposed)
            SetStatus(CaseGuideUiText.SaveFailedText(_state.CaseStore.LastSaveError), Theme.Danger);
    }

    private DiscoveryDocumentSet LoadSet() =>
        string.IsNullOrWhiteSpace(Draft.FolderPath)
            ? new DiscoveryDocumentSet { CaseId = Draft.CaseId }
            : DiscoveryStore.LoadOrNew(Draft.FolderPath, Draft.CaseId);

    private DiscoveryStepModel BuildModel() =>
        DiscoveryStepModel.Build(Draft, _set, _corpus, _state.StoryModelConfig, Draft.FolderPath, FileExists);

    /// <summary>
    /// Rebuilds the step model from the draft, the sidecar and the corpus snapshot, then repaints everything but the input
    /// values (so an invalid field keeps what the user typed).
    /// </summary>
    private void RefreshModel()
    {
        if (_loading || _state == null)
            return;

        _model = BuildModel();
        PaintAll();
    }

    // ----- Inputs -----

    /// <summary>
    /// Fills the witness grid, fields, checkboxes and pickers from the settings and the model; handlers do not write
    /// while this runs (research Pitfall 8).
    /// </summary>
    private void PopulateInputs()
    {
        _loading = true;
        try
        {
            _debounce.Stop();
            _pagesValid = true;
            Array.Fill(_countValid, true);
            _capShown = false;
            _gridWitnesses.Rows.Clear();
            if (!_model.HasGuide)
                return;

            var settings = Draft.Discovery ?? DiscoveryStepModel.Defaults(Draft);
            var selected = new HashSet<string>(settings.WitnessIds ?? new List<string>(), StringComparer.Ordinal);
            foreach (var witness in _model.Witnesses)
            {
                var index = _gridWitnesses.Rows.Add(selected.Contains(witness.Id), witness.FullName,
                    DiscoveryUiText.SideText(witness.Side, _model.IsMemo), witness.FactCount.ToString("N0", CultureInfo.InvariantCulture));
                var row = _gridWitnesses.Rows[index];
                row.Tag = witness.Id;
                row.Cells[WitnessColumn].ToolTipText =
                    DiscoveryUiText.WitnessTooltip(witness.Title, witness.OrganizationName, witness.FactCount);
            }
            _gridWitnesses.ClearSelection();
            _txtPages.Text = settings.PagesPerExcerpt.ToString(CultureInfo.InvariantCulture);

            for (var i = 0; i < WrittenKinds.Length; i++)
            {
                var kind = WrittenKinds[i];
                var (enabled, count) = KindSetting(settings, kind);
                KindRows[i].Visible = IsKindVisible(kind);
                KindChecks[i].Text = DiscoveryUiText.KindLabel(kind, _model.Variant);
                KindChecks[i].Checked = enabled;
                KindTexts[i].Text = count.ToString(CultureInfo.InvariantCulture);
            }

            var (by, on) = DiscoveryUiText.PartyLabels(_model.IsMemo);
            _lblServedBy.Text = by;
            _lblServedOn.Text = on;
            FillPartyCombo(_cmbServedBy, _model.PartyChoice.Propounding?.Id);
            FillPartyCombo(_cmbServedOn, _model.PartyChoice.Responding?.Id);
        }
        finally
        {
            _loading = false;
        }
    }

    private void FillPartyCombo(ComboBox combo, string? selectedId)
    {
        combo.Items.Clear();
        foreach (var party in _model.Parties)
            combo.Items.Add(new PartyItem(party));
        var index = _model.Parties.FindIndex(p => string.Equals(p.Id, selectedId, StringComparison.Ordinal));
        combo.SelectedIndex = index >= 0 ? index : combo.Items.Count > 0 ? 0 : -1;
        FitDropDown(combo);
    }

    private bool IsKindVisible(DiscoveryKind kind) =>
        _model.Kinds.FirstOrDefault(k => k.Kind == kind)?.Visible == true && DiscoveryUiText.KindVisible(kind, _model.Variant);

    private static (bool Enabled, int Count) KindSetting(DiscoverySettings settings, DiscoveryKind kind) => kind switch
    {
        DiscoveryKind.RequestsForProduction => (settings.RfpEnabled, settings.RfpCount),
        DiscoveryKind.Interrogatories => (settings.RogEnabled, settings.RogCount),
        _ => (settings.RfaEnabled, settings.RfaCount)
    };

    private void WitnessGrid_CellValueChanged(object? sender, DataGridViewCellEventArgs e)
    {
        if (_loading || _state == null || e.RowIndex < 0 || e.ColumnIndex != _gridWitnesses.Columns[CheckColumn]!.Index)
            return;

        var cell = _gridWitnesses.Rows[e.RowIndex].Cells[CheckColumn];
        if (cell.Value is true && CheckedWitnessIds().Count > DiscoverySettings.MaxWitnesses)
        {
            // Refuse the 7th check (D-12); the cap is also enforced by DiscoverySettings.Normalize (T-08-40)
            _loading = true;
            try
            {
                cell.Value = false;
                _gridWitnesses.RefreshEdit();
            }
            finally
            {
                _loading = false;
            }
            ShowCapHint();
            return;
        }

        Settings.WitnessIds = CheckedWitnessIds();
        Settings.UserTouched = true;
        RefreshModel();
        QueueSave();
    }

    private List<string> CheckedWitnessIds() =>
        _gridWitnesses.Rows.Cast<DataGridViewRow>()
            .Where(r => r.Cells[CheckColumn].Value is true && r.Tag is string)
            .Select(r => (string)r.Tag!)
            .ToList();

    private void ShowCapHint()
    {
        _capShown = true;
        PaintSelectionHint();
        if (_renderOnly)
            return;
        _capTimer.Stop();
        _capTimer.Start();
    }

    private void OnKindCheckedChanged()
    {
        if (_loading || _state == null || !_model.HasGuide)
            return;

        var settings = Settings;
        settings.RfpEnabled = _chkRfp.Checked;
        settings.RogEnabled = _chkRog.Checked;
        settings.RfaEnabled = _chkRfa.Checked;
        settings.UserTouched = true;
        RefreshModel();
        QueueSave();
    }

    private void OnPartyChanged()
    {
        if (_loading || _state == null || !_model.HasGuide)
            return;

        var settings = Settings;
        settings.PropoundingPartyId = (_cmbServedBy.SelectedItem as PartyItem)?.Party.Id ?? string.Empty;
        settings.RespondingPartyId = (_cmbServedOn.SelectedItem as PartyItem)?.Party.Id ?? string.Empty;
        settings.UserTouched = true;
        RefreshModel();
        QueueSave();
    }

    /// <summary>
    /// Parses the pages and count fields now (the debounce tick, Next and leaving the step call this). Only values passing
    /// TryParsePages / TryParseCount are written; an invalid field keeps its text and the settings keep the last valid value.
    /// </summary>
    internal void CommitPendingInput()
    {
        _debounce.Stop();
        if (_state == null || !_model.HasGuide)
            return;

        var settings = Settings;
        var changed = false;
        _pagesValid = DiscoveryStepModel.TryParsePages(_txtPages.Text, out var pages);
        if (_pagesValid && settings.PagesPerExcerpt != pages)
        {
            settings.PagesPerExcerpt = pages;
            changed = true;
        }

        for (var i = 0; i < WrittenKinds.Length; i++)
        {
            var kind = WrittenKinds[i];
            _countValid[i] = DiscoveryStepModel.TryParseCount(kind, KindTexts[i].Text, out var count);
            if (!_countValid[i] || KindSetting(settings, kind).Count == count)
                continue;
            switch (kind)
            {
                case DiscoveryKind.RequestsForProduction:
                    settings.RfpCount = count;
                    break;
                case DiscoveryKind.Interrogatories:
                    settings.RogCount = count;
                    break;
                default:
                    settings.RfaCount = count;
                    break;
            }
            changed = true;
        }

        if (changed)
            settings.UserTouched = true;
        RefreshModel();
        if (changed)
            QueueSave();
    }

    /// <summary>
    /// Every visible, enabled field holds a valid value (Generate Documents gate).
    /// </summary>
    private bool FieldsValid()
    {
        if (_model.Witnesses.Count > 0 && !_pagesValid)
            return false;
        for (var i = 0; i < WrittenKinds.Length; i++)
        {
            if (IsKindVisible(WrittenKinds[i]) && KindChecks[i].Checked && !_countValid[i])
                return false;
        }
        return true;
    }

    // ----- Painting -----

    private void PaintAll()
    {
        var hasGuide = _model.HasGuide;
        _cards.Visible = hasGuide;
        _stateCard.Visible = !hasGuide;
        if (hasGuide)
        {
            _lblNotice.Text = DiscoveryUiText.Notice(_model.IsMemo, _model.CorpusUsable);
            _lblNotice.Visible = true;
        }
        else
        {
            _lblNotice.Visible = false;
            CenterStateContent();
        }
        SizeNotice();
        PaintHeaderButton();
        if (!hasGuide)
        {
            PaintRunChrome();
            return;
        }

        PaintDepositions();
        PaintWritten();
        PaintDocuments();
        PaintEstimate();
        PaintRunChrome();
    }

    private void PaintHeaderButton()
    {
        var folder = _state?.Case?.FolderPath;
        _btnOpenFolder.Visible = !string.IsNullOrWhiteSpace(folder) && DiscoveryStore.HasDocuments(folder);
        _btnOpenFolder.Enabled = !_isGenerating;
    }

    private void PaintDepositions()
    {
        _lblDepositionsCaption.Text = DiscoveryUiText.DepositionsCaption(_model.Variant);
        var any = _model.Witnesses.Count > 0;
        _gridWitnesses.Visible = any;
        _lblNoWitnesses.Visible = !any;
        // The grid stays scrollable during a run; only the checks are locked
        _gridWitnesses.Columns[CheckColumn]!.ReadOnly = _isGenerating;
        PaintSelectionHint();

        var pagesEnabled = any && !_isGenerating;
        _txtPages.Enabled = pagesEnabled;
        _lblPages.Enabled = any;
        TextFieldHost.SetInvalid(_hostPages, any && !_pagesValid);
        _lblPagesHint.Text = any && !_pagesValid ? DiscoveryUiText.PagesHintInvalid : DiscoveryUiText.PagesHintValid;
        _lblPagesHint.ForeColor = any && !_pagesValid ? Theme.Danger : Theme.TextMuted;
    }

    private void PaintSelectionHint()
    {
        if (_capShown)
        {
            _lblSelectionHint.Text = DiscoveryUiText.CapHint;
            _lblSelectionHint.ForeColor = Theme.Warning;
            return;
        }
        _lblSelectionHint.Text = _model.Witnesses.Count > 0 ? DiscoveryUiText.SelectionHint(CheckedWitnessIds().Count) : string.Empty;
        _lblSelectionHint.ForeColor = Theme.TextMuted;
    }

    private void PaintWritten()
    {
        var visibleKinds = new List<DiscoveryKind>();
        var invalidHint = string.Empty;
        var extra = string.Empty;
        for (var i = 0; i < WrittenKinds.Length; i++)
        {
            var kind = WrittenKinds[i];
            var visible = IsKindVisible(kind);
            KindRows[i].Visible = visible;
            KindChecks[i].Enabled = !_isGenerating;
            KindTexts[i].Enabled = KindChecks[i].Checked && !_isGenerating;
            var invalid = visible && KindChecks[i].Checked && !_countValid[i];
            TextFieldHost.SetInvalid(KindHosts[i], invalid);
            if (!visible)
                continue;
            visibleKinds.Add(kind);
            if (invalid && invalidHint.Length == 0)
            {
                var (min, max) = CountRange(kind);
                invalidHint = DiscoveryUiText.InvalidCountHint(min, max, DiscoveryUiText.KindLabelLower(kind, _model.Variant));
            }
            if (KindChecks[i].Checked)
                extra += _model.CountHint(kind);
        }

        if (invalidHint.Length > 0)
        {
            _lblCountsHint.Text = invalidHint;
            _lblCountsHint.ForeColor = Theme.Danger;
        }
        else
        {
            _lblCountsHint.Text = DiscoveryUiText.CountsHint(_model.Variant, visibleKinds) + extra;
            _lblCountsHint.ForeColor = Theme.TextMuted;
        }

        var note = DiscoveryUiText.MemoNote(_model.Variant);
        _lblMemoNote.Text = note;
        _lblMemoNote.Visible = note.Length > 0;

        // The pickers stay visible but disabled when no written set is checked, so the layout does not jump
        var pickers = _model.AnyWrittenChecked && !_isGenerating;
        _cmbServedBy.Enabled = pickers;
        _cmbServedOn.Enabled = pickers;
        _lblServedBy.Enabled = _model.AnyWrittenChecked;
        _lblServedOn.Enabled = _model.AnyWrittenChecked;

        if (_model.AnyWrittenChecked && _model.PartyChoice.Same)
        {
            _lblPartyHint.Text = DiscoveryUiText.SamePartyHint;
            _lblPartyHint.ForeColor = Theme.Danger;
            _lblPartyHint.Visible = true;
        }
        else if (_model.PartyChoice.Fallback)
        {
            _lblPartyHint.Text = DiscoveryUiText.MissingPartyHint;
            _lblPartyHint.ForeColor = Theme.TextMuted;
            _lblPartyHint.Visible = true;
        }
        else
        {
            _lblPartyHint.Text = string.Empty;
            _lblPartyHint.Visible = false;
        }
    }

    private static (int Min, int Max) CountRange(DiscoveryKind kind) => kind switch
    {
        DiscoveryKind.RequestsForProduction => (DiscoverySettings.MinRfp, DiscoverySettings.MaxRfp),
        DiscoveryKind.Interrogatories => (DiscoverySettings.MinRogs, DiscoverySettings.MaxInterrogatories),
        _ => (DiscoverySettings.MinRfa, DiscoverySettings.MaxRfa)
    };

    /// <summary>
    /// Documents grid rows (in place when the keys are unchanged, so scroll and selection hold), the spent hint, the
    /// detail line and the row buttons.
    /// </summary>
    internal void PaintDocuments()
    {
        var rows = _model.Rows;
        var selectedKey = SelectedRow()?.Key;
        var sameKeys = _gridDocuments.Rows.Count == rows.Count
            && _gridDocuments.Rows.Cast<DataGridViewRow>().Select(r => (r.Tag as DiscoveryRowInfo)?.Key)
                .SequenceEqual(rows.Select(r => r.Key));

        _paintingRows = true;
        try
        {
            if (!sameKeys)
            {
                _gridDocuments.Rows.Clear();
                foreach (var _ in rows)
                    _gridDocuments.Rows.Add();
            }
            for (var i = 0; i < rows.Count; i++)
            {
                var info = rows[i];
                var gridRow = _gridDocuments.Rows[i];
                gridRow.Tag = info;
                var tooltip = DiscoveryUiText.RowTooltip(info);
                gridRow.Cells[DocumentColumn].Value = info.ShortTitle;
                gridRow.Cells[StatusColumn].Value = DiscoveryUiText.RowStatusCell(info.State);
                foreach (DataGridViewCell cell in gridRow.Cells)
                    cell.ToolTipText = tooltip;
            }
            if (!sameKeys)
            {
                var index = rows.FindIndex(r => string.Equals(r.Key, selectedKey, StringComparison.Ordinal));
                _gridDocuments.ClearSelection();
                if (index >= 0)
                    _gridDocuments.Rows[index].Selected = true;
            }
            _gridDocuments.Invalidate();
        }
        finally
        {
            _paintingRows = false;
        }

        _gridDocuments.Visible = rows.Count > 0;
        _lblNoDocuments.Visible = rows.Count == 0;
        _lblSpent.Text = DiscoveryUiText.SpentHint(_model.SpentUsd);
        PaintSelection();
    }

    /// <summary>
    /// Selects the row with this key (render and run code).
    /// </summary>
    internal void SelectDocument(string key)
    {
        foreach (DataGridViewRow row in _gridDocuments.Rows)
        {
            if (row.Tag is DiscoveryRowInfo info && string.Equals(info.Key, key, StringComparison.Ordinal))
            {
                _gridDocuments.ClearSelection();
                row.Selected = true;
                if (_gridDocuments.IsHandleCreated && row.Cells[DocumentColumn].Visible)
                    _gridDocuments.CurrentCell = row.Cells[DocumentColumn];
            }
        }
        PaintSelection();
    }

    private DiscoveryRowInfo? SelectedRow() =>
        _gridDocuments.SelectedRows.Count > 0 ? _gridDocuments.SelectedRows[0].Tag as DiscoveryRowInfo : null;

    private void PaintSelection()
    {
        var row = SelectedRow();
        var detail = row == null ? string.Empty : DiscoveryUiText.RowStatusFull(row);
        _lblDetail.Text = detail;
        _lblDetail.ForeColor = row == null ? Theme.TextMuted : DetailColor(row.State);
        _toolTip.SetToolTip(_lblDetail, detail);

        var saveAgain = row != null && row.State is DiscoveryRowState.NotSaved or DiscoveryRowState.FileMissing;
        _btnSaveAgain.Visible = saveAgain;
        _btnRegenerate.Visible = !saveAgain;
        _btnSaveAgain.Enabled = !_isGenerating;
        _btnOpenWord.Enabled = !_isGenerating && row != null && row.FileExists;
        _btnRegenerate.Enabled = !_isGenerating && row != null
            && row.State is not (DiscoveryRowState.NotGenerated or DiscoveryRowState.Writing) && KeyReady();
    }

    private void PaintEstimate()
    {
        var condition = _model.Condition(KeyReady());
        _lblEstimate.Text = DiscoveryUiText.EstimateLine(condition, _model.EstimateUsd, _model.ToWrite.Count,
            _state?.StoryModelConfig?.DisplayName);
        _lblEstimate.ForeColor = condition == DiscoveryUiText.EstimateCondition.NoKey ? Theme.Warning : Theme.TextMuted;

        var titles = _model.Rows.ToDictionary(r => r.Key, r => r.ShortTitle, StringComparer.Ordinal);
        var lines = _model.ToWrite.Select(item => DiscoveryUiText.EstimateTooltipLine(
            titles.TryGetValue(item.Key, out var title) ? title : item.Key,
            _model.EstimateByKey.TryGetValue(item.Key, out var cost) ? cost : 0m));
        _toolTip.SetToolTip(_lblEstimate, condition == DiscoveryUiText.EstimateCondition.SomeToWrite
            ? string.Join(Environment.NewLine, lines)
            : string.Empty);
    }

    /// <summary>
    /// Run button, progress bar and input locks for the current _isGenerating (the run code calls this when a run starts
    /// and ends).
    /// </summary>
    internal void PaintRunChrome()
    {
        if (_isGenerating)
        {
            _btnRun.Text = DiscoveryUiText.CancelGenerationButton;
            ButtonHelper.StyleButton(_btnRun, ButtonStyle.Danger);
            _btnRun.Enabled = true;
        }
        else
        {
            _btnRun.Text = DiscoveryUiText.GenerateButton;
            ButtonHelper.StyleButton(_btnRun, ButtonStyle.Primary);
            _btnRun.Enabled = _model.HasGuide && _model.CanGenerate(KeyReady(), FieldsValid());
        }
        _progress.Visible = _isGenerating;
        _btnOpenFolder.Enabled = !_isGenerating;
        if (_model.HasGuide)
        {
            _gridWitnesses.Columns[CheckColumn]!.ReadOnly = _isGenerating;
            _txtPages.Enabled = _model.Witnesses.Count > 0 && !_isGenerating;
            for (var i = 0; i < WrittenKinds.Length; i++)
            {
                KindChecks[i].Enabled = !_isGenerating;
                KindTexts[i].Enabled = KindChecks[i].Checked && !_isGenerating;
            }
            _cmbServedBy.Enabled = _model.AnyWrittenChecked && !_isGenerating;
            _cmbServedOn.Enabled = _model.AnyWrittenChecked && !_isGenerating;
            PaintSelection();
        }
    }

    internal void SetStatus(string text, Color color)
    {
        if (IsDisposed)
            return;

        _lblStatus.Text = text ?? string.Empty;
        _lblStatus.ForeColor = color;
        _lblStatus.Visible = _lblStatus.Text.Length > 0;
    }

    private void FocusInitial()
    {
        Control target = _btnRun.Enabled && _model.HasGuide ? _btnRun : _gridWitnesses;
        if (!target.CanSelect)
        {
            _focusPending = true;
            return;
        }
        _focusPending = false;
        ActiveControl = target;
    }

    private static Color DotColor(DiscoveryRowState state) => state switch
    {
        DiscoveryRowState.Writing => Theme.Accent,
        DiscoveryRowState.Generated => Theme.Success,
        DiscoveryRowState.Failed => Theme.Danger,
        DiscoveryRowState.Warnings or DiscoveryRowState.OutOfDate or DiscoveryRowState.NotSaved or DiscoveryRowState.FileMissing
            => Theme.Warning,
        _ => Theme.BorderStrong
    };

    // Green stays reserved for the dot and outcome lines: a clean row's detail is muted
    private static Color DetailColor(DiscoveryRowState state) => state switch
    {
        DiscoveryRowState.Generated or DiscoveryRowState.NotGenerated => Theme.TextMuted,
        _ => DotColor(state)
    };

    private static Color ToneColor(SummaryTone tone) => tone switch
    {
        SummaryTone.Success => Theme.Success,
        SummaryTone.Warning => Theme.Warning,
        SummaryTone.Danger => Theme.Danger,
        _ => Theme.TextMuted
    };

    private void DocumentsGrid_CellPainting(object? sender, DataGridViewCellPaintingEventArgs e)
    {
        if (e.RowIndex < 0 || e.Graphics == null)
            return;

        var dot = _gridDocuments.Columns[GridStyle.DotColumnName];
        if (dot == null || e.ColumnIndex != dot.Index)
            return;

        e.Paint(e.CellBounds, DataGridViewPaintParts.All & ~DataGridViewPaintParts.ContentForeground);
        if (_gridDocuments.Rows[e.RowIndex].Tag is DiscoveryRowInfo info)
            Theme.DrawStatusDot(e.Graphics, e.CellBounds, DotColor(info.State), this);
        e.Handled = true;
    }

    // ----- Render-test access -----

    /// <summary>
    /// Binds and paints one state synchronously for render tests; never probes the corpus, saves or starts a run.
    /// <paramref name="model"/> replaces the built model (Running and Results rows); <paramref name="status"/> is the
    /// status line (Accent while running, otherwise <paramref name="tone"/>).
    /// </summary>
    internal void LoadForRender(WizardState state, DiscoveryRenderState render, DiscoveryStepModel? model = null,
        string status = "", SummaryTone tone = SummaryTone.Muted)
    {
        BindState(state);
        _renderOnly = true;
        if (Draft.Guide != null)
            DiscoveryStepModel.ApplyDefaults(Draft);
        _set = LoadSet();
        _corpus = DiscoveryCorpus.None();
        _model = model ?? BuildModel();
        _isGenerating = render == DiscoveryRenderState.Running;
        PopulateInputs();
        PaintAll();

        if (render == DiscoveryRenderState.Running)
        {
            var finished = _model.Rows.Count(r => r.State is not (DiscoveryRowState.NotGenerated or DiscoveryRowState.Writing));
            _progress.Maximum = Math.Max(1, _model.Rows.Count);
            _progress.Value = Math.Min(_progress.Maximum, finished + 1);
            RaiseStateChanged();
        }

        if (render == DiscoveryRenderState.Invalid && _model.HasGuide)
        {
            // Render check 6: pages 5, RFP 99, the same party in both pickers and the refused 7th witness
            _loading = true;
            try
            {
                _txtPages.Text = "5";
                _txtRfp.Text = "99";
                if (_cmbServedBy.Items.Count > 0)
                    _cmbServedOn.SelectedIndex = _cmbServedBy.SelectedIndex;
            }
            finally
            {
                _loading = false;
            }
            Settings.RespondingPartyId = Settings.PropoundingPartyId;
            CommitPendingInput();
            ShowCapHint();
        }

        if (status.Length > 0)
            SetStatus(status, render == DiscoveryRenderState.Running ? Theme.Accent : ToneColor(tone));
        FocusInitial();
    }

    internal DiscoveryStepModel Model => _model;
    internal DataGridView WitnessGrid => _gridWitnesses;
    internal DataGridView DocumentsGrid => _gridDocuments;
    internal Button RunButton => _btnRun;
    internal Button RegenerateButton => _btnRegenerate;
    internal Button SaveAgainButton => _btnSaveAgain;
    internal Button OpenWordButton => _btnOpenWord;
    internal Button OpenFolderButton => _btnOpenFolder;
    internal CardPanel PagesHost => _hostPages;
    internal CardPanel RfpHost => _hostRfp;
    internal TextBox PagesText => _txtPages;
    internal TextBox RfpText => _txtRfp;
    internal TextBox RogText => _txtRog;
    internal TextBox RfaText => _txtRfa;
    internal CheckBox RfpCheck => _chkRfp;
    internal CheckBox RogCheck => _chkRog;
    internal CheckBox RfaCheck => _chkRfa;
    // Which request rows the variant shows (D-25); Visible reads false until the step is on screen
    internal IReadOnlyList<bool> KindRowsShown => WrittenKinds.Select(IsKindVisible).ToList();
    internal TableLayoutPanel RogRow => _rowRog;
    internal TableLayoutPanel RfaRow => _rowRfa;
    internal ComboBox ServedByCombo => _cmbServedBy;
    internal ComboBox ServedOnCombo => _cmbServedOn;
    internal Label ServedByLabel => _lblServedBy;
    internal Label ServedOnLabel => _lblServedOn;
    internal Label SelectionHintLabel => _lblSelectionHint;
    internal Label DepositionsCaptionLabel => _lblDepositionsCaption;
    internal Label PagesHintLabel => _lblPagesHint;
    internal Label CountsHintLabel => _lblCountsHint;
    internal Label PartyHintLabel => _lblPartyHint;
    internal Label MemoNoteLabel => _lblMemoNote;
    internal Label NoWitnessesLabel => _lblNoWitnesses;
    internal Label NoDocumentsLabel => _lblNoDocuments;
    internal Label DetailLabel => _lblDetail;
    internal Label NoticeLabel => _lblNotice;
    internal Label SpentLabel => _lblSpent;
    internal CardPanel StateCardPanel => _stateCard;
    internal TableLayoutPanel CardsLayout => _cards;
    internal bool StateCardShown => !_model.HasGuide;
    internal Label StatusLabel => _lblStatus;
    internal SlimProgressBar ProgressBar => _progress;
    internal Label EstimateLabel => _lblEstimate;
    internal string EstimateTooltipText => _toolTip.GetToolTip(_lblEstimate) ?? string.Empty;

    /// <summary>
    /// Checks or clears a witness row through the same path as a click (tests).
    /// </summary>
    internal void SetWitnessChecked(int rowIndex, bool value) => _gridWitnesses.Rows[rowIndex].Cells[CheckColumn].Value = value;

    /// <summary>
    /// A party in a picker: "{name} ({role})" (UI-SPEC S1 Card 2).
    /// </summary>
    private sealed class PartyItem
    {
        public PartyItem(CaseParty party)
        {
            Party = party;
            Text = DiscoveryUiText.PartyItem(party.Name, RoleText(party.Role));
        }

        public CaseParty Party { get; }
        public string Text { get; }

        public override string ToString() => Text;

        private static string RoleText(PartyRole role) => role switch
        {
            PartyRole.ThirdParty => "Third party",
            _ => role.ToString()
        };
    }
}
