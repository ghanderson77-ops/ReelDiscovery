using System.Text.RegularExpressions;
using ReelDiscovery.Helpers;
using ReelDiscovery.Models;
using ReelDiscovery.Models.Case;
using ReelDiscovery.Services.Ai;
using ReelDiscovery.Services.Case;

namespace ReelDiscovery.UserControls;

/// <summary>
/// Core document review step (UI-SPEC S3): inline generation with progress and cancel, a section list
/// with a per-section editor and full preview, the issues grid, and the Finish gate on Error issues.
/// </summary>
public class StepCoreDocument : UserControl, IWizardStep
{
    private static readonly Color Accent = Theme.Accent;
    private const string EmDash = "—";
    private const string DefaultEmptyBody = "Click Generate Document to draft the complaint, petition or investigation memo from your scenario.";
    private const string CancelledText = "Generation cancelled. Completed sections were kept.";

    private WizardState _state = null!;
    private Label _lblTitle = null!;
    private Label _lblSubtitle = null!;
    private Button _btnRegenerateAll = null!;
    private Button _btnResumeAll = null!;
    private Panel _bodyHost = null!;
    private CardPanel _issuesCard = null!;
    private RowStyle _issuesRow = null!;
    private TableLayoutPanel _bodyLayout = null!;
    private Label _lblSectionsCount = null!;
    private ListBox _lstSections = null!;
    private ThemedButton _btnShowSection = null!;
    private ThemedButton _btnShowFull = null!;
    private Panel _editorHost = null!;
    private Label _lblSectionHeading = null!;
    private Label _lblSectionHint = null!;
    private TextBox _txtEditor = null!;
    private Button _btnApply = null!;
    private TextBox _txtGuidance = null!;
    private Button _btnRegenerateSection = null!;
    private TextBox _txtPreview = null!;
    private Label _lblIssues = null!;
    private DataGridView _gridIssues = null!;
    private Label _lblNoIssues = null!;
    private Label _lblStatus = null!;
    private SlimProgressBar _progress = null!;
    private Button _btnCancel = null!;
    private CardPanel _emptyPanel = null!;
    private FlowLayoutPanel _emptyContent = null!;
    private Label _lblEmptyHeading = null!;
    private Label _lblEmptyBody = null!;
    private SlimProgressBar _emptyBusyBar = null!;
    private Button _btnGenerate = null!;

    private CancellationTokenSource? _cts;
    private bool _isGenerating;
    private bool _showFull;
    private bool _suppressSelection;
    private List<CoreDocumentIssue> _issues = new();
    private string _editorSectionId = string.Empty;
    private string _editorOriginalText = string.Empty;
    private string _declinedStaleHash = string.Empty;

    public string StepTitle => "Review Core Document";

    /// <summary>True while a document or section run is writing (the shell asks before closing, C12).</summary>
    public bool IsGenerating => _isGenerating;

    public bool CanMoveNext => !_isGenerating && _state?.Case.CoreDocument != null;
    public bool CanMoveBack => !_isGenerating;

    // Last case-flow step in Phase 2; Phase 3 changes this to "Next >"
    public string NextButtonText => "Finish";

    public event EventHandler? StateChanged;

    public StepCoreDocument()
    {
        InitializeUI();
    }

    private void InitializeUI()
    {
        // Content aligns with the wizard's page margin (the shell supplies it)
        this.Padding = new Padding(0);
        this.BackColor = Theme.Background;
        var gap = LogicalToDeviceUnits(Theme.Gap);
        var pad = LogicalToDeviceUnits(Theme.CardPadding);
        var buttonHeight = LogicalToDeviceUnits(34);

        var mainLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            Padding = new Padding(0),
            BackColor = Theme.Background
        };
        mainLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        mainLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));                            // 0: Header
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));                        // 1: Sections + editor
        _issuesRow = new RowStyle(SizeType.Absolute, LogicalToDeviceUnits(184));
        mainLayout.RowStyles.Add(_issuesRow);                                                 // 2: Issues card
        mainLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));                            // 3: Status strip

        // ----- Header: document type + court or memo author, actions on the right -----
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
            Text = "Core Document",
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
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.TopLeft,
            Margin = new Padding(0, LogicalToDeviceUnits(2), 0, 0),
            Font = Theme.BodyFont(),
            ForeColor = Theme.TextMuted
        };
        _lblSubtitle.Height = _lblSubtitle.PreferredHeight;

        var headerButtons = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Anchor = AnchorStyles.Right,
            Margin = new Padding(gap, 0, 0, 0)
        };
        // Writes every unwritten section (resume after cancel or a fatal stop); shown only when some remain
        _btnResumeAll = ButtonHelper.CreateButton("Resume All", 0, buttonHeight, ButtonStyle.Default);
        _btnResumeAll.Margin = new Padding(0, 0, LogicalToDeviceUnits(Theme.SmallGap), 0);
        _btnResumeAll.Visible = false;
        _btnRegenerateAll = ButtonHelper.CreateButton("Regenerate All", 0, buttonHeight, ButtonStyle.Default);
        _btnRegenerateAll.Margin = new Padding(0);
        headerButtons.Controls.Add(_btnResumeAll);
        headerButtons.Controls.Add(_btnRegenerateAll);

        headerLayout.Controls.Add(_lblTitle, 0, 0);
        headerLayout.Controls.Add(_lblSubtitle, 0, 1);
        headerLayout.Controls.Add(headerButtons, 1, 0);
        headerLayout.SetRowSpan(headerButtons, 2);
        mainLayout.Controls.Add(headerLayout, 0, 0);

        // ----- Body: Sections card | editor card (or the full-width empty / outline state) -----
        _bodyHost = new Panel { Dock = DockStyle.Fill, Margin = new Padding(0), BackColor = Theme.Background };

        _bodyLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Margin = new Padding(0),
            BackColor = Theme.Background
        };
        _bodyLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 30));
        _bodyLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 70));
        _bodyLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        // Sections card
        var sectionsCard = new CardPanel { Dock = DockStyle.Fill, Padding = new Padding(1), Margin = new Padding(0, 0, gap / 2, 0) };
        var sectionsLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, BackColor = Theme.Surface, Margin = new Padding(0) };
        sectionsLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));     // 0: Card header
        sectionsLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); // 1: Section list
        var sectionsHeader = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(pad, LogicalToDeviceUnits(14), pad, LogicalToDeviceUnits(10)),
            Margin = new Padding(0)
        };
        sectionsHeader.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        sectionsHeader.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        sectionsHeader.Controls.Add(new Label { Text = "Sections", AutoSize = true, Font = Theme.LabelFont(), ForeColor = Theme.Text, Margin = new Padding(0) }, 0, 0);
        _lblSectionsCount = new Label { AutoSize = true, Font = Theme.SmallFont(), ForeColor = Theme.TextMuted, Anchor = AnchorStyles.Right, Margin = new Padding(0) };
        sectionsHeader.Controls.Add(_lblSectionsCount, 1, 0);
        sectionsLayout.Controls.Add(sectionsHeader, 0, 0);

        // Section list (owner-drawn rows: status dot, heading, status on the right)
        _lstSections = new ListBox
        {
            Dock = DockStyle.Fill,
            DrawMode = DrawMode.OwnerDrawFixed,
            ItemHeight = LogicalToDeviceUnits(36),
            Font = Theme.BodyFont(),
            BackColor = Theme.Surface,
            ForeColor = Theme.Text,
            BorderStyle = BorderStyle.None,
            IntegralHeight = false,
            Margin = new Padding(0, 0, 0, LogicalToDeviceUnits(6))
        };
        sectionsLayout.Controls.Add(_lstSections, 0, 1);
        sectionsCard.Controls.Add(sectionsLayout);
        _bodyLayout.Controls.Add(sectionsCard, 0, 0);

        // Editor card
        var editorCard = new CardPanel { Dock = DockStyle.Fill, Padding = new Padding(pad), Margin = new Padding(gap / 2, 0, 0, 0) };
        var editorLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5, BackColor = Theme.Surface, Margin = new Padding(0) };
        editorLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));                          // 0: Heading + view toggle
        editorLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));                          // 1: Hint for unwritten sections
        editorLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));                      // 2: Editor or full preview
        editorLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, LogicalToDeviceUnits(1))); // 3: Divider
        editorLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));                          // 4: Toolbar

        var editorHeader = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Margin = new Padding(0, 0, 0, LogicalToDeviceUnits(Theme.SmallGap))
        };
        editorHeader.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        editorHeader.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _lblSectionHeading = new Label
        {
            Dock = DockStyle.Fill,
            AutoEllipsis = true,
            TextAlign = ContentAlignment.MiddleLeft,
            Margin = new Padding(0),
            Font = Theme.HeadingFont(),
            ForeColor = Theme.Text
        };
        _lblSectionHeading.Height = _lblSectionHeading.PreferredHeight;
        editorHeader.Controls.Add(_lblSectionHeading, 0, 0);

        // Section / Full document view toggle
        var toggle = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Anchor = AnchorStyles.Right,
            Margin = new Padding(gap, 0, 0, 0),
            BackColor = Theme.Surface
        };
        _btnShowSection = (ThemedButton)ButtonHelper.CreateButton("Section", 0, buttonHeight, ButtonStyle.Toggle);
        _btnShowSection.Margin = new Padding(0, 0, LogicalToDeviceUnits(4), 0);
        _btnShowSection.Selected = true;
        _btnShowFull = (ThemedButton)ButtonHelper.CreateButton("Full document", 0, buttonHeight, ButtonStyle.Toggle);
        _btnShowFull.Margin = new Padding(0);
        toggle.Controls.Add(_btnShowSection);
        toggle.Controls.Add(_btnShowFull);
        editorHeader.Controls.Add(toggle, 1, 0);
        editorLayout.Controls.Add(editorHeader, 0, 0);

        // Explains an empty editor (writing, not written yet, failed); hidden once the section has text
        _lblSectionHint = new Label
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 0, 0, LogicalToDeviceUnits(Theme.SmallGap)),
            Font = Theme.BodyFont(),
            ForeColor = Theme.TextMuted,
            Visible = false
        };
        editorLayout.Controls.Add(_lblSectionHint, 0, 1);

        _editorHost = new Panel { Dock = DockStyle.Fill, Margin = new Padding(0), BackColor = Theme.Surface };
        _txtEditor = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ScrollBars = ScrollBars.Vertical,
            WordWrap = true,
            AcceptsReturn = true,
            BorderStyle = BorderStyle.None,
            // Default 32,767 would silently truncate long sections
            MaxLength = 0,
            Font = Theme.EditorFont(),
            ForeColor = Theme.Text,
            BackColor = Theme.Surface,
            ReadOnly = true
        };
        _txtPreview = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            WordWrap = true,
            BorderStyle = BorderStyle.None,
            MaxLength = 0,
            Font = Theme.EditorFont(),
            ForeColor = Theme.Text,
            BackColor = Theme.Surface,
            Visible = false
        };
        _editorHost.Controls.Add(_txtEditor);
        _editorHost.Controls.Add(_txtPreview);
        editorLayout.Controls.Add(_editorHost, 0, 2);

        editorLayout.Controls.Add(new Panel { Dock = DockStyle.Fill, BackColor = Theme.Border, Margin = new Padding(0) }, 0, 3);

        // Toolbar: Apply Edits | guidance (stretches) | Regenerate Section
        var toolbar = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 1,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Margin = new Padding(0, LogicalToDeviceUnits(12), 0, 0),
            BackColor = Theme.Surface
        };
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _btnApply = ButtonHelper.CreateButton("Apply Edits", 0, buttonHeight, ButtonStyle.Default);
        _btnApply.Margin = new Padding(0);
        _btnApply.Enabled = false;
        _btnRegenerateSection = ButtonHelper.CreateButton("Regenerate Section", 0, buttonHeight, ButtonStyle.Primary);
        _btnRegenerateSection.Margin = new Padding(0);
        _btnRegenerateSection.Enabled = false;

        // Guidance box: borderless TextBox inside a rounded border matching the buttons' height
        _txtGuidance = new TextBox
        {
            Dock = DockStyle.Fill,
            BorderStyle = BorderStyle.None,
            Font = Theme.BodyFont(),
            ForeColor = Theme.Text,
            BackColor = Theme.Surface,
            PlaceholderText = "Optional guidance for Regenerate Section, e.g. add the March 3 site meeting"
        };
        var guidanceHost = new CardPanel
        {
            Dock = DockStyle.Fill,
            Radius = Theme.Radius,
            BorderColor = Theme.BorderStrong,
            Margin = new Padding(LogicalToDeviceUnits(Theme.SmallGap), 0, LogicalToDeviceUnits(Theme.SmallGap), 0)
        };
        var guidanceVertical = Math.Max(LogicalToDeviceUnits(4), (_btnApply.Height - _txtGuidance.PreferredHeight) / 2);
        guidanceHost.Padding = new Padding(LogicalToDeviceUnits(10), guidanceVertical, LogicalToDeviceUnits(10), 0);
        guidanceHost.Height = _btnApply.Height;
        guidanceHost.Controls.Add(_txtGuidance);

        toolbar.Controls.Add(_btnApply, 0, 0);
        toolbar.Controls.Add(guidanceHost, 1, 0);
        toolbar.Controls.Add(_btnRegenerateSection, 2, 0);
        editorLayout.Controls.Add(toolbar, 0, 4);
        editorCard.Controls.Add(editorLayout);
        _bodyLayout.Controls.Add(editorCard, 1, 0);
        _bodyHost.Controls.Add(_bodyLayout);

        // Full-width state card: no document yet, a stopped run, or the outline being written
        _emptyPanel = new CardPanel { Dock = DockStyle.Fill, Visible = false };
        _emptyContent = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            BackColor = Theme.Surface
        };
        _lblEmptyHeading = new Label
        {
            Text = "No core document yet",
            AutoSize = true,
            Font = Theme.HeadingFont(),
            ForeColor = Theme.Text,
            Margin = new Padding(0, 0, 0, LogicalToDeviceUnits(Theme.SmallGap))
        };
        _lblEmptyBody = new Label
        {
            Text = DefaultEmptyBody,
            AutoSize = true,
            MaximumSize = new Size(LogicalToDeviceUnits(520), 0),
            Font = Theme.BodyFont(),
            ForeColor = Theme.TextMuted,
            Margin = new Padding(0, 0, 0, LogicalToDeviceUnits(20))
        };
        _emptyBusyBar = new SlimProgressBar
        {
            Width = LogicalToDeviceUnits(360),
            Height = LogicalToDeviceUnits(6),
            Marquee = true,
            Visible = false,
            Margin = new Padding(0, LogicalToDeviceUnits(4), 0, LogicalToDeviceUnits(8))
        };
        _btnGenerate = ButtonHelper.CreateButton("Generate Document", 0, buttonHeight, ButtonStyle.Primary);
        _btnGenerate.Margin = new Padding(0);
        _emptyContent.Controls.Add(_lblEmptyHeading);
        _emptyContent.Controls.Add(_lblEmptyBody);
        _emptyContent.Controls.Add(_emptyBusyBar);
        _emptyContent.Controls.Add(_btnGenerate);
        _emptyPanel.Controls.Add(_emptyContent);
        _bodyHost.Controls.Add(_emptyPanel);

        mainLayout.Controls.Add(_bodyHost, 0, 1);

        // ----- Issues card -----
        var issuesCard = new CardPanel { Dock = DockStyle.Fill, Padding = new Padding(1), Margin = new Padding(0, gap, 0, 0) };
        _issuesCard = issuesCard;
        var issuesLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, BackColor = Theme.Surface, Margin = new Padding(0) };
        issuesLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));     // 0: Caption
        issuesLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); // 1: Grid or no-issues text
        _lblIssues = new Label
        {
            Text = "Issues",
            AutoSize = true,
            Margin = new Padding(pad - 1, LogicalToDeviceUnits(12), pad, LogicalToDeviceUnits(8)),
            Font = Theme.LabelFont(),
            ForeColor = Theme.Text
        };
        issuesLayout.Controls.Add(_lblIssues, 0, 0);

        var issuesHost = new Panel { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, LogicalToDeviceUnits(6)), BackColor = Theme.Surface };
        _gridIssues = new DataGridView
        {
            Dock = DockStyle.Fill,
            ReadOnly = true,
            AutoGenerateColumns = false,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            AllowUserToResizeRows = false,
            RowHeadersVisible = false,
            MultiSelect = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            BackgroundColor = Theme.Surface,
            BorderStyle = BorderStyle.None,
            CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal,
            GridColor = Theme.Border,
            EnableHeadersVisualStyles = false,
            ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None,
            ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize,
            Font = Theme.BodyFont()
        };
        _gridIssues.ColumnHeadersDefaultCellStyle.BackColor = Theme.SurfaceMuted;
        _gridIssues.ColumnHeadersDefaultCellStyle.ForeColor = Theme.TextMuted;
        _gridIssues.ColumnHeadersDefaultCellStyle.SelectionBackColor = Theme.SurfaceMuted;
        _gridIssues.ColumnHeadersDefaultCellStyle.Font = Theme.SmallFont();
        _gridIssues.ColumnHeadersDefaultCellStyle.Padding = new Padding(pad - 4, LogicalToDeviceUnits(6), 0, LogicalToDeviceUnits(6));
        _gridIssues.DefaultCellStyle.Padding = new Padding(pad - 4, 0, 0, 0);
        _gridIssues.DefaultCellStyle.SelectionBackColor = Theme.AccentSoft;
        _gridIssues.DefaultCellStyle.SelectionForeColor = Theme.Text;
        _gridIssues.RowTemplate.Height = LogicalToDeviceUnits(32);
        _gridIssues.Columns.Add(new DataGridViewTextBoxColumn { Name = "Severity", HeaderText = "Severity", Width = LogicalToDeviceUnits(96) });
        _gridIssues.Columns.Add(new DataGridViewTextBoxColumn { Name = "Section", HeaderText = "Section", Width = LogicalToDeviceUnits(240) });
        _gridIssues.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Issue",
            HeaderText = "Issue",
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill
        });
        _lblNoIssues = new Label
        {
            Text = "No issues found. No case-law citations or real names detected.",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.TopLeft,
            Padding = new Padding(pad - 1, 0, pad, 0),
            Font = Theme.BodyFont(),
            ForeColor = Theme.Success,
            Visible = false
        };
        issuesHost.Controls.Add(_gridIssues);
        issuesHost.Controls.Add(_lblNoIssues);
        issuesLayout.Controls.Add(issuesHost, 0, 1);
        issuesCard.Controls.Add(issuesLayout);
        mainLayout.Controls.Add(issuesCard, 0, 2);

        // ----- Status strip: status + progress bar + cancel (bar and cancel only while generating) -----
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
        _btnCancel = ButtonHelper.CreateButton("Cancel Generation", 0, buttonHeight, ButtonStyle.Danger);
        _btnCancel.Margin = new Padding(0);
        _btnCancel.Visible = false;
        _lblStatus = new Label
        {
            Dock = DockStyle.Fill,
            MinimumSize = new Size(0, _btnCancel.Height),
            AutoEllipsis = true,
            TextAlign = ContentAlignment.MiddleLeft,
            Margin = new Padding(0),
            Font = Theme.BodyFont()
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
        mainLayout.Controls.Add(progressStrip, 0, 3);

        this.Controls.Add(mainLayout);

        // Wire handlers only after every control exists (handlers read other controls)
        _lstSections.DrawItem += LstSections_DrawItem;
        _lstSections.SelectedIndexChanged += LstSections_SelectedIndexChanged;
        _btnShowSection.Click += (s, e) => SetPreviewMode(false);
        _btnShowFull.Click += (s, e) => SetPreviewMode(true);
        _btnRegenerateAll.Click += BtnRegenerateAll_Click;
        _btnResumeAll.Click += async (s, e) => await RunGenerationAsync(fullRegenerate: false);
        // An existing outline is resumed, never silently replaced (CR-02, WR-09); Regenerate All asks first
        _btnGenerate.Click += async (s, e) => await RunGenerationAsync(fullRegenerate: _state?.Case.CoreDocument == null);
        _btnCancel.Click += BtnCancel_Click;
        _txtEditor.TextChanged += (s, e) => _btnApply.Enabled = !_isGenerating && _txtEditor.Text != _editorOriginalText;
        _txtEditor.Leave += (s, e) => ApplyPendingEdit();
        _btnApply.Click += (s, e) => ApplyPendingEdit();
        _btnRegenerateSection.Click += async (s, e) => await RegenerateSelectedSectionAsync();
        _gridIssues.CellDoubleClick += GridIssues_CellDoubleClick;
        editorCard.SizeChanged += (s, e) =>
            _lblSectionHint.MaximumSize = new Size(Math.Max(0, editorCard.Width - 2 * pad), 0);
        _emptyPanel.Resize += (s, e) => CenterEmptyContent();
        _emptyContent.SizeChanged += (s, e) => CenterEmptyContent();
    }

    private void SetPreviewMode(bool showFull)
    {
        if (showFull && !_showFull)
            ApplyPendingEdit();

        _showFull = showFull;
        _btnShowSection.Selected = !showFull;
        _btnShowFull.Selected = showFull;
        _txtPreview.Visible = showFull;
        _txtEditor.Visible = !showFull;
        _lblSectionHint.Visible = !showFull && _lblSectionHint.Text.Length > 0;
        RefreshPreview();
    }

    private void CenterEmptyContent()
    {
        _emptyContent.Location = new Point(
            Math.Max(0, (_emptyPanel.Width - _emptyContent.Width) / 2),
            Math.Max(0, (_emptyPanel.Height - _emptyContent.Height) / 2));
    }

    // ----- Pure display helpers (unit tested) -----

    public static string SectionSuffix(CoreDocumentSection s, IReadOnlyCollection<CoreDocumentIssue> sectionIssues)
    {
        return s.Status switch
        {
            SectionStatus.Writing => $" {EmDash} writing...",
            SectionStatus.Pending => $" {EmDash} pending",
            SectionStatus.Failed => $" {EmDash} failed",
            _ when sectionIssues.Count > 0 => $" {EmDash} {sectionIssues.Count} issues",
            _ when s.IsUserEdited => $" {EmDash} edited",
            _ => string.Empty
        };
    }

    public static Color SectionSuffixColor(CoreDocumentSection s, IReadOnlyCollection<CoreDocumentIssue> sectionIssues)
    {
        return s.Status switch
        {
            SectionStatus.Writing => Accent,
            SectionStatus.Pending => Color.DimGray,
            SectionStatus.Failed => Color.Red,
            _ when sectionIssues.Any(i => i.Severity == IssueSeverity.Error) => Color.Red,
            _ when sectionIssues.Count > 0 => Color.DarkOrange,
            _ => SystemColors.ControlText
        };
    }

    public static string DocumentTitle(CoreDocument doc)
    {
        var detail = DocumentSubtitle(doc);
        return detail.Length == 0 ? DocumentTypeLabel(doc) : $"{DocumentTypeLabel(doc)} {EmDash} {detail}";
    }

    public static string DocumentTypeLabel(CoreDocument doc) => doc.DocumentType switch
    {
        CoreDocumentType.Petition => "Petition",
        CoreDocumentType.InvestigationMemo => "Investigation Memo",
        _ => "Complaint"
    };

    /// <summary>
    /// Court name (pleadings) or memo author organization, with runs of whitespace collapsed.
    /// </summary>
    public static string DocumentSubtitle(CoreDocument doc)
    {
        var detail = doc.DocumentType == CoreDocumentType.InvestigationMemo
            ? doc.Memo.Organization
            : doc.Jurisdiction.CourtName;
        var text = string.Join(' ', (detail ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

        // A model sometimes runs on past the memo author's name ("..., retained by X. The memorandum will contain ..."):
        // keep the first sentence. Abbreviations such as "Inc." or "U.S." are not sentence ends.
        if (doc.DocumentType == CoreDocumentType.InvestigationMemo)
        {
            var sentenceEnd = Regex.Match(text, @"(?<=[A-Za-z]{4})\. (?=[A-Z])", RegexOptions.None, TimeSpan.FromMilliseconds(200));
            if (sentenceEnd.Success)
                return text[..sentenceEnd.Index];
        }
        return text;
    }

    /// <summary>
    /// Issues shown while a run is writing: only finished sections are checked, so unwritten
    /// sections and document-wide counts do not flood the list mid-run.
    /// </summary>
    public static List<CoreDocumentIssue> VisibleIssues(CoreDocument? doc, IReadOnlyList<CoreDocumentIssue> issues, bool isGenerating)
    {
        if (!isGenerating || doc == null)
            return issues.ToList();

        return issues.Where(i => doc.FindSection(i.SectionId)?.Status == SectionStatus.Ready).ToList();
    }

    /// <summary>
    /// Editor hint shown when the selected section has no text yet.
    /// </summary>
    public static string EditorPlaceholder(CoreDocumentSection s, bool isGenerating) => s.Status switch
    {
        SectionStatus.Writing => "Writing this section...",
        SectionStatus.Pending when isGenerating => "Not written yet. It will appear here when the generator reaches it.",
        SectionStatus.Pending => "Not written yet. Click Resume All to write it, or type the text here yourself.",
        SectionStatus.Failed => $"This section failed: {s.FailureMessage}. Click Retry Section, or type the text here yourself.",
        _ => string.Empty
    };

    public static (string Text, Color Color) ReadyStatus(CaseDraft draft, IReadOnlyCollection<CoreDocumentIssue> issues)
    {
        if (issues.Count == 0)
        {
            var doc = draft.CoreDocument;
            var sections = doc?.Sections.Count ?? 0;
            // Memo paragraphs are not numbered, so count all of them
            if (doc?.DocumentType == CoreDocumentType.InvestigationMemo)
            {
                var memoParagraphs = doc.Sections.Sum(sec => sec.Paragraphs.Count);
                return ($"Document ready: {sections} sections, {memoParagraphs} paragraphs.", Color.Green);
            }

            var paragraphs = doc == null ? 0 : CoreDocumentAssembler.CountNumberedParagraphs(doc);
            return ($"Document ready: {sections} sections, {paragraphs} numbered paragraphs.", Color.Green);
        }

        var errors = CoreDocumentValidator.CountErrors(issues);
        var warnings = CoreDocumentValidator.CountWarnings(issues);
        return ($"Document ready with {errors} errors and {warnings} warnings. Review the Issues list.", Color.DarkOrange);
    }

    /// <summary>
    /// Empty state: no document, or nothing written yet and no text anywhere (never while a run is filling the list).
    /// Hand-written text in a Pending section keeps the editor visible (CR-02).
    /// </summary>
    public static bool ShowsEmptyState(CoreDocument? doc, bool isGenerating) =>
        !isGenerating && (doc == null || doc.Sections.All(s => s.Status == SectionStatus.Pending && s.Paragraphs.Count == 0));

    /// <summary>
    /// True when Resume All has work to do: a Pending or Failed section without hand-written text (WR-05).
    /// </summary>
    public static bool HasUnfinishedSections(CoreDocument? doc) =>
        doc != null && doc.Sections.Any(s => (s.Status is SectionStatus.Pending or SectionStatus.Failed)
            && !(s.IsUserEdited && s.Paragraphs.Count > 0));

    public static string IssuesCaption(IReadOnlyCollection<CoreDocumentIssue> issues) =>
        $"Issues ({CoreDocumentValidator.CountErrors(issues)} errors, {CoreDocumentValidator.CountWarnings(issues)} warnings)";

    private static string FatalText(string? message) =>
        $"Generation stopped: {message}. Completed sections were kept. Fix the problem on the API Configuration step, then click Resume All or Regenerate All.";

    private static string ErrorText(Exception ex) =>
        ex.InnerException != null ? $"Error: {ex.Message} ({ex.InnerException.Message})" : $"Error: {ex.Message}";

    // ----- Refresh -----

    private List<CoreDocumentIssue> IssuesFor(string sectionId) =>
        _issues.Where(i => i.SectionId == sectionId).ToList();

    private CoreDocumentSection? SelectedSection => _lstSections.SelectedItem as CoreDocumentSection;

    private void RefreshAll()
    {
        if (_state == null)
            return;

        var doc = _state.Case.CoreDocument;
        _issues = CoreDocumentValidator.Validate(_state.Case);

        _lblTitle.Text = doc == null ? "Core Document" : DocumentTypeLabel(doc);
        _lblSubtitle.Text = doc == null ? string.Empty : DocumentSubtitle(doc);

        // Rebind the section list only when the section objects changed; otherwise just repaint
        var sections = doc?.Sections ?? new List<CoreDocumentSection>();
        if (!_lstSections.Items.Cast<object>().SequenceEqual(sections))
        {
            var selectedId = SelectedSection?.Id ?? _editorSectionId;
            _suppressSelection = true;
            try
            {
                _lstSections.BeginUpdate();
                _lstSections.Items.Clear();
                _lstSections.Items.AddRange(sections.Cast<object>().ToArray());
                _lstSections.EndUpdate();
                var index = sections.FindIndex(s => s.Id == selectedId);
                if (index >= 0)
                    _lstSections.SelectedIndex = index;
            }
            finally
            {
                _suppressSelection = false;
            }
        }
        _lstSections.Invalidate();

        // Issues grid (mid-run: finished sections only)
        var visibleIssues = VisibleIssues(doc, _issues, _isGenerating);
        _lblIssues.Text = doc == null
            ? "Issues"
            : _isGenerating ? $"{IssuesCaption(visibleIssues)} {EmDash} checking sections as they finish" : IssuesCaption(visibleIssues);
        _gridIssues.Rows.Clear();
        foreach (var issue in visibleIssues)
        {
            var rowIndex = _gridIssues.Rows.Add(
                issue.Severity == IssueSeverity.Error ? "Error" : "Warning",
                SectionLabel(doc, issue.SectionId),
                issue.Message);
            var row = _gridIssues.Rows[rowIndex];
            row.Tag = issue.SectionId;
            row.DefaultCellStyle.ForeColor = issue.Severity == IssueSeverity.Error ? Color.Red : Color.DarkOrange;
        }
        _gridIssues.ClearSelection(); // Keep severity colors visible on the first row
        _gridIssues.Visible = visibleIssues.Count > 0;
        _lblNoIssues.Text = _isGenerating
            ? "No issues in the finished sections so far."
            : "No issues found. No case-law citations or real names detected.";
        _lblNoIssues.Visible = doc != null && visibleIssues.Count == 0;

        RefreshPreview();

        // Full-width state card: no document, a stopped run, or the outline still being written
        var writingOutline = _isGenerating && (doc == null || doc.Sections.Count == 0);
        _emptyPanel.Visible = writingOutline || ShowsEmptyState(doc, _isGenerating);
        _bodyLayout.Visible = !_emptyPanel.Visible;
        _emptyBusyBar.Visible = writingOutline;
        _btnGenerate.Visible = !writingOutline;
        if (writingOutline)
        {
            _lblEmptyHeading.Text = "Writing the outline...";
            _lblEmptyBody.Text = "Planning the parties, court, timeline and sections. The sections appear here as soon as the outline is ready, usually within a minute.";
        }
        else if (_lblEmptyHeading.Text != "No core document yet")
        {
            _lblEmptyHeading.Text = "No core document yet";
        }
        if (_emptyPanel.Visible)
            CenterEmptyContent();

        var total = doc?.Sections.Count ?? 0;
        var written = doc?.Sections.Count(sec => sec.Status == SectionStatus.Ready) ?? 0;
        _lblSectionsCount.Text = total == 0 ? string.Empty : $"{written} of {total} written";

        _btnRegenerateAll.Enabled = !_isGenerating && doc != null;
        _btnRegenerateAll.Visible = doc != null;

        // The issues card appears once there is a document to check
        var showIssues = !_emptyPanel.Visible;
        _issuesCard.Visible = showIssues;
        _issuesRow.Height = showIssues ? LogicalToDeviceUnits(184) : 0;

        // Hidden in the empty state: its Generate Document button already resumes
        _btnResumeAll.Visible = !_emptyPanel.Visible && HasUnfinishedSections(doc);
        _btnResumeAll.Enabled = !_isGenerating;
    }

    private static string SectionLabel(CoreDocument? doc, string sectionId)
    {
        if (string.IsNullOrEmpty(sectionId))
            return "Document";
        if (sectionId == "outline")
            return "Outline";
        return doc?.FindSection(sectionId)?.Heading ?? sectionId;
    }

    private void RefreshPreview()
    {
        if (!_showFull)
            return;

        var doc = _state?.Case.CoreDocument;
        _txtPreview.Text = doc == null ? string.Empty : CoreDocumentAssembler.RenderPlainText(doc);
    }

    private void LstSections_DrawItem(object? sender, DrawItemEventArgs e)
    {
        if (e.Index < 0 || e.Index >= _lstSections.Items.Count)
            return;

        var section = (CoreDocumentSection)_lstSections.Items[e.Index];
        var selected = (e.State & DrawItemState.Selected) == DrawItemState.Selected;
        var font = e.Font ?? _lstSections.Font;
        var g = e.Graphics;
        var bounds = e.Bounds;

        using (var background = new SolidBrush(selected ? Theme.AccentSoft : Theme.Surface))
        {
            g.FillRectangle(background, bounds);
        }
        if (selected)
        {
            using var bar = new SolidBrush(Theme.Accent);
            g.FillRectangle(bar, bounds.X, bounds.Y, LogicalToDeviceUnits(3), bounds.Height);
        }

        var sectionIssues = IssuesFor(section.Id);
        var pad = LogicalToDeviceUnits(Theme.CardPadding);

        // Status dot
        var dot = LogicalToDeviceUnits(8);
        var dotRect = new Rectangle(bounds.X + pad, bounds.Y + (bounds.Height - dot) / 2, dot, dot);
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        using (var dotBrush = new SolidBrush(StatusDotColor(section, sectionIssues)))
        {
            g.FillEllipse(dotBrush, dotRect);
        }
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.Default;

        const TextFormatFlags flags = TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine |
            TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis;

        // Status text, right-aligned
        var status = StatusLabel(SectionSuffix(section, sectionIssues));
        var statusWidth = 0;
        if (status.Length > 0)
        {
            using var small = Theme.SmallFont();
            // A little slack so the measured text is never ellipsized when drawn
            statusWidth = TextRenderer.MeasureText(g, status, small, Size.Empty, flags & ~TextFormatFlags.EndEllipsis).Width + LogicalToDeviceUnits(4);
            var statusBounds = new Rectangle(bounds.Right - pad - statusWidth, bounds.Y, statusWidth, bounds.Height);
            var statusColor = section.Status == SectionStatus.Pending ? Theme.TextMuted : SectionSuffixColor(section, sectionIssues);
            TextRenderer.DrawText(g, status, small, statusBounds, statusColor, flags | TextFormatFlags.Right);
        }

        // Heading fills the space between the dot and the status
        var textLeft = dotRect.Right + LogicalToDeviceUnits(10);
        var textRight = bounds.Right - pad - (statusWidth > 0 ? statusWidth + LogicalToDeviceUnits(12) : 0);
        var headingBounds = new Rectangle(textLeft, bounds.Y, Math.Max(0, textRight - textLeft), bounds.Height);
        TextRenderer.DrawText(g, section.Heading, font, headingBounds, selected ? Theme.Accent : Theme.Text, flags | TextFormatFlags.Left);
    }

    /// <summary>
    /// Status dot: green when written cleanly, blue while writing, grey when pending, red on failure or errors, amber on warnings.
    /// </summary>
    public static Color StatusDotColor(CoreDocumentSection s, IReadOnlyCollection<CoreDocumentIssue> sectionIssues) => s.Status switch
    {
        SectionStatus.Writing => Theme.Accent,
        SectionStatus.Pending => Theme.BorderStrong,
        SectionStatus.Failed => Theme.Danger,
        _ when sectionIssues.Any(i => i.Severity == IssueSeverity.Error) => Theme.Danger,
        _ when sectionIssues.Count > 0 => Theme.Warning,
        _ => Theme.Success
    };

    /// <summary>
    /// Row status text from the list suffix: " — 2 issues" becomes "2 issues", " — pending" becomes "Pending".
    /// </summary>
    public static string StatusLabel(string suffix)
    {
        var text = suffix.Trim().TrimStart('—').Trim();
        return text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text[1..];
    }

    private void LstSections_SelectedIndexChanged(object? sender, EventArgs e)
    {
        if (_suppressSelection)
            return;

        // Edits to the previous section are applied, never lost, when switching (the editor is read-only mid-run)
        if (!_isGenerating)
            ApplyPendingEdit();
        LoadSelectedSection();
    }

    private void LoadSelectedSection()
    {
        var s = SelectedSection;
        if (s == null)
        {
            _editorSectionId = string.Empty;
            _lblSectionHeading.Text = _isGenerating ? "Writing the document..." : "Select a section";
            SetSectionHint(_isGenerating
                ? "Sections appear in the list as the outline is written. Select one to read it while the rest are written."
                : string.Empty);
            _txtEditor.Text = string.Empty;
            _editorOriginalText = string.Empty;
            _btnApply.Enabled = false;
            UpdateEditorEnabled();
            return;
        }

        _editorSectionId = s.Id;
        _lblSectionHeading.Text = s.Heading;
        SetSectionHint(s.Paragraphs.Count == 0 ? EditorPlaceholder(s, _isGenerating) : string.Empty);
        _txtEditor.Text = CoreDocumentEditing.JoinParagraphs(s.Paragraphs);
        _editorOriginalText = _txtEditor.Text;
        _btnApply.Enabled = false;
        _btnRegenerateSection.Text = s.Status == SectionStatus.Failed || s.Status == SectionStatus.Pending
            ? "Retry Section"
            : "Regenerate Section";
        UpdateEditorEnabled();
    }

    /// <summary>
    /// Applies the editor text to the section it was loaded from (no-op when unchanged), then re-validates.
    /// </summary>
    private void ApplyPendingEdit()
    {
        if (_isGenerating || _state == null || string.IsNullOrEmpty(_editorSectionId) || _txtEditor.Text == _editorOriginalText)
            return;

        var section = _state.Case.CoreDocument?.FindSection(_editorSectionId);
        if (section == null)
            return;

        CoreDocumentEditing.ApplyEdit(_state.Case, _editorSectionId, _txtEditor.Text);

        // Show the normalized text (also when only whitespace changed and nothing was applied)
        _txtEditor.Text = CoreDocumentEditing.JoinParagraphs(section.Paragraphs);
        _editorOriginalText = _txtEditor.Text;
        _btnApply.Enabled = false;
        if (section.Status == SectionStatus.Ready)
            _btnRegenerateSection.Text = "Regenerate Section";
        RefreshAll();
    }

    private void SetSectionHint(string text)
    {
        _lblSectionHint.Text = text;
        _lblSectionHint.Visible = text.Length > 0 && !_showFull;
    }

    private void UpdateEditorEnabled()
    {
        var hasSection = !string.IsNullOrEmpty(_editorSectionId);
        // Sections can be read while a run writes; editing waits until it finishes
        _lstSections.Enabled = true;
        _txtEditor.Enabled = true;
        _txtEditor.ReadOnly = _isGenerating || !hasSection;
        _txtEditor.BackColor = Theme.Surface;
        _txtGuidance.Enabled = !_isGenerating && hasSection;
        _btnRegenerateSection.Enabled = !_isGenerating && hasSection;
        _btnApply.Enabled = !_isGenerating && hasSection && _txtEditor.Text != _editorOriginalText;
        _btnRegenerateAll.Enabled = !_isGenerating && _state?.Case.CoreDocument != null;
        _btnResumeAll.Enabled = !_isGenerating;
        _btnGenerate.Enabled = !_isGenerating;
    }

    // Selects the first section when nothing is selected, otherwise reloads the editor from the model
    private void ReloadEditorAfterRun()
    {
        if (_lstSections.Items.Count == 0)
        {
            LoadSelectedSection();
            return;
        }

        if (_lstSections.SelectedIndex < 0)
            _lstSections.SelectedIndex = 0; // Raises SelectedIndexChanged -> LoadSelectedSection
        else
            LoadSelectedSection();
    }

    // ----- Generation -----

    // No-op once the step is disposed: generation continuations can resume after the window closed (WR-07)
    private void SetStatus(string text, Color color)
    {
        if (IsDisposed)
            return;

        _lblStatus.Text = text;
        _lblStatus.ForeColor = color;
    }

    // Explains a cancelled or stopped run in the empty state instead of the first-run copy (WR-09)
    private void SetEmptyBody(string reason)
    {
        if (IsDisposed)
            return;

        _lblEmptyBody.Text = EmptyStateBody(_state.Case.CoreDocument != null, reason);
    }

    public static string EmptyStateBody(bool hasOutline, string? reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
            return DefaultEmptyBody;

        var next = hasOutline
            ? "The outline was kept. Click Generate Document to write the sections."
            : "Click Generate Document to try again.";
        return $"{reason} {next}";
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            // Closing the wizard or switching flows stops in-flight generation so no further calls are billed (WR-07)
            try
            {
                _cts?.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // The run already finished and released its token source; nothing to cancel
            }
        }

        base.Dispose(disposing);
    }

    private void SetBusy(bool busy)
    {
        _isGenerating = busy;
        _progress.Visible = busy;
        _progress.Marquee = busy;
        _btnCancel.Visible = busy;
        _btnCancel.Enabled = busy;
        UpdateEditorEnabled();
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private async Task RunGenerationAsync(bool fullRegenerate)
    {
        if (_isGenerating || _state == null)
            return;

        ApplyPendingEdit();
        _cts = new CancellationTokenSource();
        _lblEmptyBody.Text = DefaultEmptyBody;
        _progress.Value = 0;
        SetBusy(true);
        RefreshAll();

        try
        {
            var generator = new CoreDocumentGenerator(_state.CreateGateway());
            var progress = new Progress<string>(msg =>
            {
                if (IsDisposed)
                    return;

                SetStatus(msg, Accent);
                var doc = _state.Case.CoreDocument;
                var max = Math.Max(1, doc?.Sections.Count ?? 1);
                var ready = doc?.Sections.Count(s => s.Status == SectionStatus.Ready) ?? 0;
                _progress.Marquee = doc == null || doc.Sections.Count == 0;
                _progress.Maximum = max;
                _progress.Value = Math.Min(max, ready);
                RefreshAll();

                // Show the outline's first section as soon as it exists, and keep the selected one current
                if (_lstSections.SelectedIndex < 0 && _lstSections.Items.Count > 0)
                {
                    _lstSections.SelectedIndex = 0;
                }
                else if (SelectedSection is { } selected
                    && (CoreDocumentEditing.JoinParagraphs(selected.Paragraphs) != _txtEditor.Text
                        || EditorPlaceholder(selected, _isGenerating) != _lblSectionHint.Text))
                {
                    // Reload only when the section changed, so reading mid-run keeps its scroll position
                    LoadSelectedSection();
                }
            });

            var writeOutline = fullRegenerate || _state.Case.CoreDocument == null;
            SetStatus(writeOutline ? "Writing the outline..." : "Writing the remaining sections...", Accent);

            var result = writeOutline
                ? await generator.GenerateAsync(_state.Case, progress, _cts.Token)
                : await generator.GenerateSectionsAsync(_state.Case, progress, _cts.Token);

            // The window may have closed during the run (WR-07)
            if (IsDisposed)
                return;

            var failed = _state.Case.CoreDocument?.Sections.FirstOrDefault(s => s.Status == SectionStatus.Failed);
            if (result.IsFatal)
            {
                SetStatus(FatalText(result.FatalError), Color.Red);
                SetEmptyBody($"Generation stopped: {result.FatalError}. Fix the problem on the API Configuration step.");
            }
            else if (failed != null)
            {
                SetStatus($"{failed.Heading} failed: {failed.FailureMessage}. Select it and click Retry Section.", Color.Red);
            }
            else
            {
                var (text, color) = ReadyStatus(_state.Case, CoreDocumentValidator.Validate(_state.Case));
                SetStatus(text, color);
            }
        }
        catch (OperationCanceledException)
        {
            SetStatus(CancelledText, Color.DarkOrange);
            SetEmptyBody("Generation cancelled.");
        }
        catch (LlmCallException ex) when (ex.Result.Outcome == LlmOutcome.Refused)
        {
            var refusedText = $"The model declined to write this scenario: {ex.Result.Message}. Go Back and rephrase it. Keep criminal methods general and focus on who knew what, when, and where the money went.";
            // Always shown: a refused Regenerate All keeps the old document visible, so the status explains why
            SetStatus(refusedText, Color.Red);
            if (!IsDisposed && _state.Case.CoreDocument == null)
                _lblEmptyBody.Text = refusedText;
        }
        catch (LlmCallException ex) when (ex.Result.IsFatal)
        {
            SetStatus(FatalText(ex.Result.Message ?? ex.Message), Color.Red);
            SetEmptyBody($"Generation stopped: {ex.Result.Message ?? ex.Message}. Fix the problem on the API Configuration step.");
        }
        catch (Exception ex)
        {
            SetStatus(ErrorText(ex), Color.Red);
        }
        finally
        {
            _cts?.Dispose();
            _cts = null;
            if (!IsDisposed)
            {
                SetBusy(false);
                RefreshAll();
                ReloadEditorAfterRun();
                StateChanged?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    private async Task RegenerateSelectedSectionAsync()
    {
        if (_isGenerating || _state == null)
            return;

        var s = SelectedSection;
        if (s == null)
            return;

        ApplyPendingEdit();

        if (s.IsUserEdited)
        {
            var answer = MessageBox.Show(
                $"Regenerate \"{s.Heading}\"? Your edits to this section will be replaced.",
                "Regenerate Section",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2);
            if (answer != DialogResult.Yes)
                return;
        }

        // Retry Section and Regenerate Section both write only the selected section and send the guidance (WR-05);
        // Resume All writes every unwritten section
        _cts = new CancellationTokenSource();
        _progress.Value = 0;
        SetBusy(true);
        SetStatus($"Writing {s.Heading}...", Accent);

        try
        {
            var generator = new CoreDocumentGenerator(_state.CreateGateway());
            await generator.RegenerateSectionAsync(_state.Case, s.Id, _txtGuidance.Text.Trim(), _cts.Token);
            if (IsDisposed)
                return;

            _txtGuidance.Clear();
            var (text, color) = ReadyStatus(_state.Case, CoreDocumentValidator.Validate(_state.Case));
            SetStatus(text, color);
        }
        catch (OperationCanceledException)
        {
            SetStatus(CancelledText, Color.DarkOrange);
        }
        catch (LlmCallException ex) when (ex.Result.IsFatal)
        {
            SetStatus(FatalText(ex.Result.Message ?? ex.Message), Color.Red);
        }
        catch (LlmCallException ex)
        {
            // The generator keeps the old text on failure
            SetStatus($"{s.Heading} failed: {ex.Result.Message ?? ex.Message}. Select it and click Retry Section.", Color.Red);
        }
        catch (Exception ex)
        {
            SetStatus(ErrorText(ex), Color.Red);
        }
        finally
        {
            _cts?.Dispose();
            _cts = null;
            if (!IsDisposed)
            {
                SetBusy(false);
                RefreshAll();
                ReloadEditorAfterRun();
                StateChanged?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    private void GridIssues_CellDoubleClick(object? sender, DataGridViewCellEventArgs e)
    {
        if (_isGenerating || e.RowIndex < 0 || e.RowIndex >= _gridIssues.Rows.Count)
            return;

        if (_gridIssues.Rows[e.RowIndex].Tag is not string sectionId || string.IsNullOrEmpty(sectionId))
            return;

        var index = -1;
        for (var i = 0; i < _lstSections.Items.Count; i++)
        {
            if (_lstSections.Items[i] is CoreDocumentSection section && section.Id == sectionId)
            {
                index = i;
                break;
            }
        }
        if (index < 0)
            return;

        _lstSections.SelectedIndex = index; // Applies pending edits, then loads the section
        SetPreviewMode(false);
        _txtEditor.Focus();
    }

    private void BtnCancel_Click(object? sender, EventArgs e)
    {
        if (!_isGenerating || _cts == null)
            return;

        var answer = MessageBox.Show(
            "Stop generating? Completed sections will be kept.",
            "Cancel Generation",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Question,
            MessageBoxDefaultButton.Button2);
        if (answer == DialogResult.Yes)
        {
            _btnCancel.Enabled = false;
            _cts?.Cancel();
        }
    }

    private async void BtnRegenerateAll_Click(object? sender, EventArgs e)
    {
        if (_isGenerating)
            return;

        if (_state.Case.CoreDocument != null)
        {
            var answer = MessageBox.Show(
                "Regenerate the whole document? Every section, including your edits, will be replaced. This makes a new set of Story-model calls.",
                "Regenerate Document",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2);
            if (answer != DialogResult.Yes)
                return;
        }

        await RunGenerationAsync(fullRegenerate: true);
    }

    // ----- IWizardStep -----

    public void BindState(WizardState state)
    {
        _state = state;
    }

    public async Task OnEnterStepAsync()
    {
        if (_isGenerating)
            return;

        var doc = _state.Case.CoreDocument;
        if (doc == null)
        {
            // Auto-generate on first entry, inline (partial results stay visible)
            await RunGenerationAsync(fullRegenerate: true);
            return;
        }

        RefreshAll();
        ReloadEditorAfterRun();

        // Never regenerate silently: only a changed scenario/settings hash prompts, and only once per hash
        var currentHash = _state.Case.ComputeSourceHash();
        if (doc.SourceHash != currentHash && currentHash != _declinedStaleHash)
        {
            var answer = MessageBox.Show(
                "Your scenario or settings changed after this document was written. Regenerate it now? Your edits will be replaced.",
                "Scenario Changed",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question,
                MessageBoxDefaultButton.Button2);
            if (answer == DialogResult.Yes)
                await RunGenerationAsync(fullRegenerate: true);
            else
                _declinedStaleHash = currentHash; // The validator reports the stale document as a Warning
        }
    }

    public Task OnLeaveStepAsync()
    {
        ApplyPendingEdit();
        return Task.CompletedTask;
    }

    public Task<bool> ValidateStepAsync()
    {
        if (_isGenerating)
            return Task.FromResult(false);

        ApplyPendingEdit();
        RefreshAll();

        if (_state.Case.CoreDocument == null)
        {
            MessageBox.Show("Generate the core document before finishing.", "Validation", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return Task.FromResult(false);
        }

        // Invented case law must never reach later phases (CASE-06)
        var citationErrors = CoreDocumentValidator.CountCitationErrors(_issues);
        if (citationErrors > 0)
        {
            MessageBox.Show(
                $"The document cites {citationErrors} court decisions. Case-law citations are not allowed because they may be invented. Edit or regenerate the sections listed under Issues, then click Finish again.",
                "Case Citations Found",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return Task.FromResult(false);
        }

        var errors = CoreDocumentValidator.CountErrors(_issues);
        if (errors > 0)
        {
            MessageBox.Show(
                $"The document has {errors} errors (listed under Issues). Fix or regenerate those sections, then click Finish again.",
                "Validation",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return Task.FromResult(false);
        }

        var warnings = CoreDocumentValidator.CountWarnings(_issues);
        if (warnings > 0)
        {
            var answer = MessageBox.Show(
                $"{warnings} warnings remain (possible case names or citation phrases). Continue anyway?",
                "Review Warnings",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question,
                MessageBoxDefaultButton.Button2);
            return Task.FromResult(answer == DialogResult.Yes);
        }

        return Task.FromResult(true);
    }
}
