using System.ComponentModel;
using System.Diagnostics;
using ReelDiscovery.Forms;
using ReelDiscovery.Helpers;
using ReelDiscovery.Models;
using ReelDiscovery.Models.Case;
using ReelDiscovery.Models.Corpus;
using ReelDiscovery.Models.Evidence;
using ReelDiscovery.Services.Case;
using ReelDiscovery.Services.Corpus;
using ReelDiscovery.Services.Evidence;

namespace ReelDiscovery.UserControls;

/// <summary>
/// Case Summary step (UI-SPEC S2, D-12): where the case is saved, what it contains, Word exports,
/// Open Case Folder / Move Case Folder, and Finish (saves and closes, no discard prompt).
/// </summary>
public class StepCaseSummary : UserControl, IWizardStep
{
    private const string Subtitle = "Everything is saved automatically in the case folder.";
    private const string ClosingHint = "Click Finish to close. To continue later, open this case from Recent cases on the Describe the Case step.";

    private static readonly CaseExportKind[] ExportKinds = { CaseExportKind.CoreDocument, CaseExportKind.Cast, CaseExportKind.KeyFacts };

    private WizardState _state = null!;
    private Label _lblTitle = null!;
    private Label _lblSubtitle = null!;
    private Button _btnExportAll = null!;
    private Label _lblPath = null!;
    private Label _lblLastSaved = null!;
    private Button _btnOpenFolder = null!;
    private Button _btnMoveFolder = null!;
    private Label _lblStatus = null!;
    private ToolTip _toolTip = null!;

    private ContentRow _rowCaseFile = null!;
    private ContentRow _rowValues = null!;
    private ContentRow _rowChecks = null!;
    private ContentRow _rowPlan = null!;
    private ContentRow _rowAnswerKey = null!;
    private ContentRow _rowProtocol = null!;
    private Button _btnReviewPlan = null!;
    private Button _btnPlanCorpus = null!;
    private Button _btnOpenCsv = null!;
    private Button _btnOpenXlsx = null!;
    private Button _btnOpenProtocol = null!;
    private ContentRow _rowMailboxes = null!;
    private ContentRow _rowStory = null!;
    private Button _btnOpenMailboxes = null!;
    private Button _btnGenerateEmails = null!;
    private Button _btnOpenStory = null!;
    private Button _btnExportStory = null!;
    private readonly Dictionary<CaseExportKind, ContentRow> _exportRows = new();
    private readonly HashSet<CaseExportKind> _failedExports = new();

    private bool _busy;

    public string StepTitle => "Case Summary";
    public bool CanMoveNext => !_busy;
    public bool CanMoveBack => !_busy;
    public string NextButtonText => "Finish";

    public event EventHandler? StateChanged;

    public StepCaseSummary()
    {
        InitializeUI();
    }

    private CaseDraft Draft => _state.Case;

    // ----- Pure line helpers (tested without controls) -----

    public static string DocumentTypeLabel(CoreDocumentType type) => type switch
    {
        CoreDocumentType.Petition => "Petition",
        CoreDocumentType.InvestigationMemo => "Investigation Memo",
        _ => "Complaint"
    };

    public static string CoreDocumentLine(CoreDocument doc) =>
        CaseGuideUiText.SummaryCoreDocumentLine(DocumentTypeLabel(doc.DocumentType), doc.Sections.Count,
            CoreDocumentAssembler.CountNumberedParagraphs(doc));

    public static string CastLine(CaseGuide g) =>
        CaseGuideUiText.SummaryCastLine(
            g.Cast.Count(c => c.Tier == CastTier.Key),
            g.Cast.Count(c => c.Tier == CastTier.Background),
            g.Organizations.Count);

    public static string FactsLine(CaseGuide g)
    {
        var dates = g.Facts
            .Select(f => CaseDraft.TryParseDate(f.Date, out var d) ? d : (DateTime?)null)
            .Where(d => d.HasValue)
            .Select(d => d!.Value)
            .ToList();
        return CaseGuideUiText.SummaryFactsLine(g.Facts.Count,
            dates.Count > 0 ? dates.Min() : null,
            dates.Count > 0 ? dates.Max() : null);
    }

    public static string ValuesLine(CaseGuide g) => CaseGuideUiText.SummaryValuesLine(g.Values.Count);

    public static string ChecksLine(int errors, int warnings) => CaseGuideUiText.SummaryChecksLine(errors, warnings);

    /// <summary>
    /// Local last-write time of an exported file in the case folder, or null when it does not exist.
    /// </summary>
    public static DateTime? ExportedAt(string folder, string fileName)
    {
        if (string.IsNullOrWhiteSpace(folder) || string.IsNullOrWhiteSpace(fileName))
            return null;
        var path = Path.Combine(folder, fileName);
        return File.Exists(path) ? File.GetLastWriteTime(path) : null;
    }

    /// <summary>
    /// State of one corpus file: no entry or no file on disk = not written; an entry stamped with another plan = written
    /// for an earlier plan. The caller checks the known file in the case folder (for the protocol, a validated recorded name
    /// when the folder was renamed, WR-08).
    /// </summary>
    public static CorpusFileStatus FileStatusFor(CorpusState? corpus, CorpusFileKind kind, bool fileExists)
    {
        var entry = WrittenEntry(corpus, kind);
        if (entry == null || !fileExists)
            return CorpusFileStatus.NotWritten;
        return entry.PlanId != corpus!.PlanId ? CorpusFileStatus.EarlierPlan : CorpusFileStatus.Written;
    }

    /// <summary>
    /// Combined state of several files shown on one row (answer-key.csv and .xlsx): the weakest state wins.
    /// </summary>
    public static CorpusFileStatus CombinedStatus(IEnumerable<CorpusFileStatus> statuses)
    {
        var list = statuses.ToList();
        if (list.Count == 0 || list.Contains(CorpusFileStatus.NotWritten))
            return CorpusFileStatus.NotWritten;
        if (list.Contains(CorpusFileStatus.Failed))
            return CorpusFileStatus.Failed;
        return list.Contains(CorpusFileStatus.EarlierPlan) ? CorpusFileStatus.EarlierPlan : CorpusFileStatus.Written;
    }

    /// <summary>
    /// Dot state name (Theme color) for a corpus file row: Success written, Warning earlier plan, Danger failed,
    /// BorderStrong not written (UI-SPEC S3).
    /// </summary>
    public static string CorpusDotState(CorpusFileStatus status) => status switch
    {
        CorpusFileStatus.Written => nameof(Theme.Success),
        CorpusFileStatus.EarlierPlan => nameof(Theme.Warning),
        CorpusFileStatus.Failed => nameof(Theme.Danger),
        _ => nameof(Theme.BorderStrong)
    };

    /// <summary>
    /// Dot state name for the Corpus plan row: out of date wins over approval (D-25).
    /// </summary>
    public static string PlanDotState(bool hasPlan, bool approved, bool stale)
    {
        if (!hasPlan)
            return nameof(Theme.BorderStrong);
        if (stale)
            return nameof(Theme.Warning);
        return approved ? nameof(Theme.Success) : nameof(Theme.BorderStrong);
    }

    /// <summary>
    /// The plan row's stale flag. Uses CorpusApproval.IsGuideStale, so a plan the user kept counts as current
    /// (same rule as validator rule 19); hashes are never compared here.
    /// </summary>
    public static bool PlanRowStale(CaseDraft draft) => CorpusApproval.IsGuideStale(draft);

    /// <summary>
    /// Local time of the newest written entry for the kinds, or null.
    /// </summary>
    public static DateTime? WrittenLocal(CorpusState? corpus, params CorpusFileKind[] kinds)
    {
        var times = kinds.Select(k => WrittenEntry(corpus, k)).Where(e => e != null).Select(e => e!.WrittenUtc).ToList();
        if (times.Count == 0)
            return null;
        var newest = times.Max();
        return newest == default ? null : DateTime.SpecifyKind(newest, DateTimeKind.Utc).ToLocalTime();
    }

    private static WrittenCorpusFile? WrittenEntry(CorpusState? corpus, CorpusFileKind kind) =>
        corpus?.WrittenFiles?.LastOrDefault(f => f != null && f.Kind == kind);

    private static Color DotColor(string state) => state switch
    {
        nameof(Theme.Success) => Theme.Success,
        nameof(Theme.Warning) => Theme.Warning,
        nameof(Theme.Danger) => Theme.Danger,
        _ => Theme.BorderStrong
    };

    private static Color FileLineColor(CorpusFileStatus status) => status switch
    {
        CorpusFileStatus.Written => Theme.Success,
        CorpusFileStatus.EarlierPlan => Theme.Warning,
        CorpusFileStatus.Failed => Theme.Danger,
        _ => Theme.TextMuted
    };

    // ----- Layout -----

    private void InitializeUI()
    {
        // Content aligns with the wizard's page margin (the shell supplies it)
        this.Padding = new Padding(0);
        this.BackColor = Theme.Background;
        var gap = LogicalToDeviceUnits(Theme.Gap);
        var smallGap = LogicalToDeviceUnits(Theme.SmallGap);
        var pad = LogicalToDeviceUnits(Theme.CardPadding);
        var buttonHeight = LogicalToDeviceUnits(34);
        _toolTip = new ToolTip();

        var mainLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 5,
            Padding = new Padding(0),
            BackColor = Theme.Background
        };
        mainLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        mainLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));     // 0: Header
        mainLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));     // 1: Case folder card
        mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); // 2: Contents card
        mainLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));     // 3: Status strip
        mainLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));     // 4: Closing hint

        // ----- Header: case title + subtitle, Export All on the right -----
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
            Text = "Case Summary",
            AutoSize = false,
            AutoEllipsis = true,
            UseMnemonic = false, // Every data label here may contain "&" (case titles, paths, "Key Facts & Events")
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.BottomLeft,
            Margin = new Padding(0),
            Font = Theme.TitleFont(),
            ForeColor = Theme.Text
        };
        _lblTitle.Height = _lblTitle.PreferredHeight;
        _lblSubtitle = new Label
        {
            Text = Subtitle,
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

        _btnExportAll = ButtonHelper.CreateButton("Export All to Word", 0, buttonHeight, ButtonStyle.Primary);
        _btnExportAll.Anchor = AnchorStyles.Right;
        _btnExportAll.Margin = new Padding(gap, 0, 0, 0);

        headerLayout.Controls.Add(_lblTitle, 0, 0);
        headerLayout.Controls.Add(_lblSubtitle, 0, 1);
        headerLayout.Controls.Add(_btnExportAll, 1, 0);
        headerLayout.SetRowSpan(_btnExportAll, 2);
        mainLayout.Controls.Add(headerLayout, 0, 0);

        // ----- Case folder card: caption, path, last saved | Open Case Folder, Move Case Folder... -----
        var folderCard = new CardPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(pad),
            Margin = new Padding(0, 0, 0, gap)
        };
        var folderLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 3,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            BackColor = Theme.Surface,
            Margin = new Padding(0)
        };
        folderLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        folderLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        folderLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // 0: Caption
        folderLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // 1: Path
        folderLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // 2: Last saved

        var folderCaption = new Label
        {
            Text = "Case folder",
            AutoSize = true,
            Margin = new Padding(0, 0, 0, LogicalToDeviceUnits(4)),
            Font = Theme.LabelFont(),
            ForeColor = Theme.Text
        };
        // Long paths end in an ellipsis; the tooltip carries the full path
        _lblPath = new Label
        {
            AutoSize = false,
            AutoEllipsis = true,
            UseMnemonic = false,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            Margin = new Padding(0),
            Font = Theme.BodyFont(),
            ForeColor = Theme.Text
        };
        _lblPath.Height = _lblPath.PreferredHeight;
        _lblLastSaved = new Label
        {
            AutoSize = false,
            AutoEllipsis = true,
            UseMnemonic = false,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            Margin = new Padding(0, LogicalToDeviceUnits(2), 0, 0),
            Font = Theme.SmallFont(),
            ForeColor = Theme.TextMuted
        };
        _lblLastSaved.Height = _lblLastSaved.PreferredHeight;

        var folderButtons = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Anchor = AnchorStyles.Right,
            BackColor = Theme.Surface,
            Margin = new Padding(gap, 0, 0, 0)
        };
        _btnOpenFolder = ButtonHelper.CreateButton("Open Case Folder", 0, buttonHeight, ButtonStyle.Default);
        _btnOpenFolder.Margin = new Padding(0, 0, smallGap, 0);
        _btnMoveFolder = ButtonHelper.CreateButton("Move Case Folder...", 0, buttonHeight, ButtonStyle.Default);
        _btnMoveFolder.Margin = new Padding(0);
        folderButtons.Controls.Add(_btnOpenFolder);
        folderButtons.Controls.Add(_btnMoveFolder);

        folderLayout.Controls.Add(folderCaption, 0, 0);
        folderLayout.Controls.Add(_lblPath, 0, 1);
        folderLayout.Controls.Add(_lblLastSaved, 0, 2);
        folderLayout.Controls.Add(folderButtons, 1, 0);
        folderLayout.SetRowSpan(folderButtons, 3);
        folderCard.Controls.Add(folderLayout);
        mainLayout.Controls.Add(folderCard, 0, 1);

        // ----- Contents card: header, then one row per item separated by 1 px dividers -----
        var contentsCard = new CardPanel { Dock = DockStyle.Fill, Padding = new Padding(1), Margin = new Padding(0) };
        var contentsLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, BackColor = Theme.Surface, Margin = new Padding(0) };
        contentsLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        contentsLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));     // 0: Card header
        contentsLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 1));  // 1: Divider
        contentsLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); // 2: Rows (scroll when the window is short)

        var contentsHeader = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 1,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(pad - 1, LogicalToDeviceUnits(14), pad - 1, LogicalToDeviceUnits(10)),
            Margin = new Padding(0)
        };
        contentsHeader.Controls.Add(new Label { Text = "Contents", AutoSize = true, Font = Theme.LabelFont(), ForeColor = Theme.Text, Margin = new Padding(0) }, 0, 0);
        contentsLayout.Controls.Add(contentsHeader, 0, 0);
        contentsLayout.Controls.Add(Divider(), 0, 1);

        var rowsHost = new Panel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = Theme.Surface, Margin = new Padding(0) };
        var rowsLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            ColumnCount = 1,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            BackColor = Theme.Surface,
            Margin = new Padding(0)
        };
        rowsLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        _rowCaseFile = CreateRow("Case file", hasLine3: true, hasButtons: false, buttonHeight);
        _exportRows[CaseExportKind.CoreDocument] = CreateRow("Core document", hasLine3: true, hasButtons: true, buttonHeight);
        _exportRows[CaseExportKind.Cast] = CreateRow("Cast of Key Characters", hasLine3: true, hasButtons: true, buttonHeight);
        _exportRows[CaseExportKind.KeyFacts] = CreateRow("Key Facts & Events", hasLine3: true, hasButtons: true, buttonHeight);
        _rowValues = CreateRow("Canonical values", hasLine3: false, hasButtons: false, buttonHeight);
        _rowChecks = CreateRow("Guide checks", hasLine3: false, hasButtons: false, buttonHeight);
        _rowPlan = CreateRow("Corpus plan", hasLine3: true, hasButtons: true, buttonHeight, new[] { "Review Plan", "Plan Corpus" });
        _rowAnswerKey = CreateRow("Answer key", hasLine3: true, hasButtons: true, buttonHeight, new[] { "Open CSV", "Open in Excel" });
        _rowProtocol = CreateRow("Review protocol", hasLine3: true, hasButtons: true, buttonHeight, new[] { "Open in Word" });
        _rowMailboxes = CreateRow("Mailboxes", hasLine3: true, hasButtons: true, buttonHeight, new[] { "Open Mailboxes Folder", "Generate Emails" });
        _rowStory = CreateRow("Story so far", hasLine3: true, hasButtons: true, buttonHeight, new[] { "Open in Word", "Export Story So Far" });
        _btnOpenMailboxes = _rowMailboxes.Buttons[0];
        _btnGenerateEmails = _rowMailboxes.Buttons[1];
        _btnOpenStory = _rowStory.Buttons[0];
        _btnExportStory = _rowStory.Buttons[1];
        _btnReviewPlan = _rowPlan.Buttons[0];
        _btnPlanCorpus = _rowPlan.Buttons[1];
        _btnOpenCsv = _rowAnswerKey.Buttons[0];
        _btnOpenXlsx = _rowAnswerKey.Buttons[1];
        _btnOpenProtocol = _rowProtocol.Buttons[0];
        _rowCaseFile.Line2.Text = CaseGuideUiText.CaseFileDescription;

        // Case file (ungrouped) · Corpus group · Case guide group (UI-SPEC S3); null = group heading
        var items = new (ContentRow? Row, string Heading)[]
        {
            (_rowCaseFile, ""),
            (null, "Corpus"),
            (_rowPlan, ""),
            (_rowAnswerKey, ""),
            (_rowProtocol, ""),
            (_rowMailboxes, ""),
            (_rowStory, ""),
            (null, "Case guide"),
            (_exportRows[CaseExportKind.CoreDocument], ""),
            (_exportRows[CaseExportKind.Cast], ""),
            (_exportRows[CaseExportKind.KeyFacts], ""),
            (_rowValues, ""),
            (_rowChecks, "")
        };
        for (var i = 0; i < items.Length; i++)
        {
            // A divider separates rows and sits above each group heading; a heading joins its first row without one
            var afterHeading = i > 0 && items[i - 1].Row == null;
            if (i > 0 && !afterHeading)
            {
                rowsLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 1)); // Divider
                rowsLayout.Controls.Add(Divider(), 0, rowsLayout.RowStyles.Count - 1);
            }
            rowsLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // Content row or group heading
            Control item = items[i].Row != null ? items[i].Row!.Layout : GroupHeading(items[i].Heading);
            rowsLayout.Controls.Add(item, 0, rowsLayout.RowStyles.Count - 1);
        }
        rowsLayout.RowCount = rowsLayout.RowStyles.Count;
        rowsHost.Controls.Add(rowsLayout);
        contentsLayout.Controls.Add(rowsHost, 0, 2);
        contentsCard.Controls.Add(contentsLayout);
        mainLayout.Controls.Add(contentsCard, 0, 2);

        // ----- Status strip: status label only (no bar or cancel) -----
        _lblStatus = new Label
        {
            AutoSize = false,
            AutoEllipsis = true,
            UseMnemonic = false,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            Margin = new Padding(0, LogicalToDeviceUnits(12), 0, 0),
            Font = Theme.BodyFont(),
            ForeColor = Theme.TextMuted
        };
        _lblStatus.Height = _lblStatus.PreferredHeight;
        mainLayout.Controls.Add(_lblStatus, 0, 3);

        // ----- Closing hint -----
        var hint = new Label
        {
            Text = ClosingHint,
            AutoSize = false,
            AutoEllipsis = true,
            UseMnemonic = false,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            Margin = new Padding(0, LogicalToDeviceUnits(8), 0, 0),
            Font = Theme.SmallFont(),
            ForeColor = Theme.TextMuted
        };
        hint.Height = hint.PreferredHeight;
        mainLayout.Controls.Add(hint, 0, 4);

        this.Controls.Add(mainLayout);

        // Wire handlers only after every control exists (handlers read other controls)
        // Rows always span the host's client width, so the contents never scroll sideways (also after the vertical bar appears)
        rowsHost.ClientSizeChanged += (s, e) => rowsLayout.Width = rowsHost.ClientSize.Width;
        foreach (var row in items.Where(i => i.Row != null).Select(i => i.Row!))
            row.Dot.Paint += (s, e) => PaintDot(row, e);
        _btnReviewPlan.Click += (s, e) => (FindForm() as WizardForm)?.NavigateToStage(CaseStage.ReviewPlan);
        _btnPlanCorpus.Click += (s, e) => (FindForm() as WizardForm)?.NavigateToStage(CaseStage.CorpusSettings);
        _btnOpenCsv.Click += (s, e) => OpenCorpusFile(CorpusFileKind.AnswerKeyCsv);
        _btnOpenXlsx.Click += (s, e) => OpenCorpusFile(CorpusFileKind.AnswerKeyXlsx);
        _btnOpenProtocol.Click += (s, e) => OpenCorpusFile(CorpusFileKind.ReviewProtocol);
        _btnOpenMailboxes.Click += (s, e) => OpenMailboxesFolder();
        _btnGenerateEmails.Click += (s, e) => (FindForm() as WizardForm)?.NavigateToStage(CaseStage.Generation);
        _btnOpenStory.Click += (s, e) => OpenStory();
        _btnExportStory.Click += async (s, e) => await ExportStoryAsync();
        foreach (var kind in ExportKinds)
        {
            var row = _exportRows[kind];
            row.Export!.Click += async (s, e) => await ExportOneAsync(kind);
            row.Open!.Click += (s, e) => OpenExport(kind);
        }
        _btnExportAll.Click += async (s, e) => await ExportAllAsync();
        _btnOpenFolder.Click += (s, e) => OpenCaseFolder();
        _btnMoveFolder.Click += async (s, e) => await MoveCaseFolderAsync();
    }

    private Panel Divider() => new() { Dock = DockStyle.Fill, Height = 1, BackColor = Theme.Border, Margin = new Padding(0) };

    private Label GroupHeading(string text)
    {
        var inset = LogicalToDeviceUnits(Theme.CardPadding) - 1;
        var label = new Label
        {
            Text = text,
            AutoSize = true,
            UseMnemonic = false,
            Anchor = AnchorStyles.Left | AnchorStyles.Top,
            // Content x: 15 inside the card's 1 px border, like the rows
            Padding = new Padding(inset, LogicalToDeviceUnits(12), inset, LogicalToDeviceUnits(4)),
            Margin = new Padding(0),
            Font = Theme.SmallFont(),
            ForeColor = Theme.TextMuted,
            BackColor = Theme.Surface
        };
        return label;
    }

    // Default captions keep the guide-document rows unchanged: Open in Word (hidden until exported), Export to Word
    private static readonly string[] DefaultRowButtons = { "Open in Word", "Export to Word" };

    private ContentRow CreateRow(string title, bool hasLine3, bool hasButtons, int buttonHeight, IReadOnlyList<string>? buttonCaptions = null)
    {
        var inset = LogicalToDeviceUnits(Theme.CardPadding) - 1;
        var lineGap = LogicalToDeviceUnits(2);
        var row = new ContentRow();

        row.Layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 3,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            BackColor = Theme.Surface,
            // Content x: 15 inside the card's 1 px border
            Padding = new Padding(inset, LogicalToDeviceUnits(12), inset, LogicalToDeviceUnits(12)),
            Margin = new Padding(0)
        };
        row.Layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));     // Dot
        row.Layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); // Text
        row.Layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));     // Buttons
        row.Layout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // 0: Line 1
        row.Layout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // 1: Line 2
        row.Layout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // 2: Line 3

        row.Line1 = RowLabel(Theme.LabelFont(), Theme.Text, 0);
        row.Line1.Text = title;
        row.Line2 = RowLabel(Theme.SmallFont(), Theme.TextMuted, lineGap);
        // 8 px dot + 10 px gap; centered on line 1
        row.Dot = new DoubleBufferedPanel
        {
            Width = LogicalToDeviceUnits(8 + 10),
            Height = row.Line1.Height,
            Anchor = AnchorStyles.Top | AnchorStyles.Left,
            BackColor = Theme.Surface,
            Margin = new Padding(0)
        };
        row.Layout.Controls.Add(row.Dot, 0, 0);
        row.Layout.Controls.Add(row.Line1, 1, 0);
        row.Layout.Controls.Add(row.Line2, 1, 1);

        if (hasLine3)
        {
            row.Line3 = RowLabel(Theme.SmallFont(), Theme.TextMuted, lineGap);
            row.Layout.Controls.Add(row.Line3, 1, 2);
        }

        if (hasButtons)
        {
            var buttons = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Anchor = AnchorStyles.Right,
                BackColor = Theme.Surface,
                Margin = new Padding(LogicalToDeviceUnits(Theme.Gap), 0, 0, 0)
            };
            foreach (var caption in buttonCaptions ?? DefaultRowButtons)
            {
                var button = ButtonHelper.CreateButton(caption, 0, buttonHeight, ButtonStyle.Default);
                // 8 px on the left of every button after the first, none on the right: the last visible button
                // always ends at the row's right inset, whichever buttons are hidden
                button.Margin = new Padding(row.Buttons.Count > 0 ? LogicalToDeviceUnits(Theme.SmallGap) : 0, 0, 0, 0);
                row.Buttons.Add(button);
                buttons.Controls.Add(button);
            }
            if (buttonCaptions == null)
            {
                row.Open = row.Buttons[0];
                row.Open.Visible = false;
                row.Export = row.Buttons[1];
            }
            row.Layout.Controls.Add(buttons, 2, 0);
            row.Layout.SetRowSpan(buttons, 3);
        }

        return row;
    }

    private static Label RowLabel(Font font, Color color, int top)
    {
        var label = new Label
        {
            AutoSize = false,
            AutoEllipsis = true,
            UseMnemonic = false,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            Margin = new Padding(0, top, 0, 0),
            Font = font,
            ForeColor = color
        };
        label.Height = label.PreferredHeight;
        return label;
    }

    private void PaintDot(ContentRow row, PaintEventArgs e)
    {
        // DrawStatusDot insets 15 px from the cell; the row padding already supplies Content x
        var inset = row.Dot.LogicalToDeviceUnits(15);
        var cell = new Rectangle(-inset, 0, row.Dot.Width + inset, row.Dot.Height);
        Theme.DrawStatusDot(e.Graphics, cell, row.DotColor, row.Dot);
    }

    // ----- Refresh -----

    private string CaseFolder =>
        !string.IsNullOrEmpty(Draft.FolderPath) && Directory.Exists(Draft.FolderPath) ? Draft.FolderPath : string.Empty;

    private static string MyDocuments => Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

    private DateTime? SavedLocal()
    {
        if (_state.CaseStore.LastSavedUtc.HasValue)
            return _state.CaseStore.LastSavedUtc.Value.ToLocalTime();
        // Opened from disk and not saved yet this session: case.json's own time
        var folder = CaseFolder;
        return folder.Length > 0 ? ExportedAt(folder, CaseStore.CaseFileName) : null;
    }

    private string ExportFileName(CaseExportKind kind, string folder) =>
        CaseGuideDocxExporter.FileNameFor(kind, Path.GetFileName(folder.TrimEnd('\\', '/')));

    /// <summary>
    /// Recomputes the header, case folder card and every contents row from the draft and the case folder.
    /// </summary>
    private void RefreshAll()
    {
        if (_state == null)
            return;

        var draft = Draft;
        var folder = CaseFolder;
        var guide = draft.Guide ?? new CaseGuide();
        var saved = SavedLocal();
        var saveError = _state.CaseStore.LastSaveError;

        _lblTitle.Text = CaseFolderNaming.CaseTitle(draft);

        // Case folder card
        _lblPath.Text = folder.Length > 0 ? CaseGuideUiText.DisplayPath(folder, MyDocuments) : "Not saved yet";
        _toolTip.SetToolTip(_lblPath, folder);
        _lblLastSaved.Text = saved.HasValue ? CaseGuideUiText.LastSavedLine(saved.Value) : "Not saved yet";

        // Case file
        _rowCaseFile.Line3!.Text = CaseGuideUiText.SaveLine(saved, saveError);
        var saveFailed = !string.IsNullOrWhiteSpace(saveError);
        _rowCaseFile.Line3.ForeColor = saveFailed ? Theme.Danger : saved.HasValue ? Theme.Success : Theme.TextMuted;
        SetDot(_rowCaseFile, saveFailed ? Theme.Danger : saved.HasValue ? Theme.Success : Theme.BorderStrong);

        // Exportable documents
        foreach (var kind in ExportKinds)
        {
            var row = _exportRows[kind];
            row.Line2.Text = kind switch
            {
                CaseExportKind.CoreDocument => draft.CoreDocument != null ? CoreDocumentLine(draft.CoreDocument) : "Not written yet",
                CaseExportKind.Cast => CastLine(guide),
                _ => FactsLine(guide)
            };

            var fileName = folder.Length > 0 ? ExportFileName(kind, folder) : string.Empty;
            var exported = ExportedAt(folder, fileName);
            row.Line3!.Text = CaseGuideUiText.ExportLine(exported, fileName);
            row.Line3.ForeColor = exported.HasValue ? Theme.Success : Theme.TextMuted;
            row.Open!.Visible = exported.HasValue;
            row.Export!.Text = exported.HasValue ? "Export Again" : "Export to Word";
            SetDot(row, _failedExports.Contains(kind) ? Theme.Danger : exported.HasValue ? Theme.Success : Theme.BorderStrong);
        }

        // Canonical values
        _rowValues.Line2.Text = ValuesLine(guide);
        SetDot(_rowValues, guide.Values.Count > 0 ? Theme.Success : Theme.BorderStrong);

        // Guide checks
        var findings = CaseGuideValidator.Validate(draft);
        var errors = CaseGuideValidator.CountErrors(findings);
        var warnings = CaseGuideValidator.CountWarnings(findings);
        var checksColor = errors > 0 ? Theme.Danger : warnings > 0 ? Theme.Warning : Theme.Success;
        _rowChecks.Line2.Text = ChecksLine(errors, warnings);
        _rowChecks.Line2.ForeColor = checksColor;
        SetDot(_rowChecks, checksColor);

        RefreshCorpusRows(folder, guide);
        UpdateButtons();
    }

    private static string FolderName(string folder) => Path.GetFileName(Path.TrimEndingDirectorySeparator(folder));

    // Known file names only. The protocol may use the recorded name from case.json, but only after it passes
    // ReviewProtocolBuilder.IsProtocolFileName (a bare "... - Review Protocol.docx" name) and exists (T-04-45, WR-08)
    private string CorpusFileName(CorpusFileKind kind, string folder) => kind switch
    {
        CorpusFileKind.AnswerKeyCsv => AnswerKeyCsvWriter.FileName,
        CorpusFileKind.AnswerKeyXlsx => AnswerKeyXlsxWriter.FileName,
        _ => ReviewProtocolBuilder.ResolveFileName(folder,
            WrittenEntry(Draft.Corpus, CorpusFileKind.ReviewProtocol)?.FileName)
    };

    private bool CorpusFileExists(CorpusFileKind kind, string folder) =>
        folder.Length > 0 && File.Exists(Path.Combine(folder, CorpusFileName(kind, folder)));

    /// <summary>
    /// Corpus plan, Answer key and Review protocol rows (UI-SPEC S3). Never throws: EnsureLoaded catches load failures.
    /// </summary>
    private void RefreshCorpusRows(string folder, CaseGuide guide)
    {
        var draft = Draft;
        var corpus = draft.Corpus;
        var load = CorpusPlanStore.EnsureLoaded(draft);
        var plan = load.Plan;

        // Corpus plan
        if (plan != null)
        {
            var summary = PlanSummary.From(plan, guide);
            var (first, last) = PlanMonths(summary, plan);
            var stale = PlanRowStale(draft);
            var approved = corpus!.Approved;
            var approvedLocal = corpus.ApprovedUtc.HasValue
                ? DateTime.SpecifyKind(corpus.ApprovedUtc.Value, DateTimeKind.Utc).ToLocalTime()
                : (DateTime?)null;
            _rowPlan.Line2.Text = CorpusUiText.CorpusPlanLine(summary.Emails, summary.Threads, summary.RelevantPercent, first, last);
            _rowPlan.Line2.ForeColor = Theme.TextMuted;
            _rowPlan.Line3!.Text = CorpusUiText.PlanStatusLine(approved, approvedLocal, stale);
            _rowPlan.Line3.ForeColor = stale ? Theme.Warning : approved ? Theme.Success : Theme.TextMuted;
            _rowPlan.Line3.Visible = true;
            SetDot(_rowPlan, DotColor(PlanDotState(hasPlan: true, approved, stale)));
            _btnReviewPlan.Visible = true;
            _btnPlanCorpus.Visible = false;
        }
        else if (!string.IsNullOrWhiteSpace(load.Reason))
        {
            // case.json names a plan that could not be read: Review Plan shows the unreadable card
            _rowPlan.Line2.Text = load.Reason;
            _rowPlan.Line2.ForeColor = Theme.Danger;
            _rowPlan.Line3!.Visible = false;
            SetDot(_rowPlan, Theme.Danger);
            _btnReviewPlan.Visible = true;
            _btnPlanCorpus.Visible = false;
        }
        else
        {
            _rowPlan.Line2.Text = CorpusUiText.NotPlannedLine;
            _rowPlan.Line2.ForeColor = Theme.TextMuted;
            _rowPlan.Line3!.Visible = false;
            SetDot(_rowPlan, DotColor(PlanDotState(hasPlan: false, approved: false, stale: false)));
            _btnReviewPlan.Visible = false;
            _btnPlanCorpus.Visible = true;
        }

        // Answer key: both files must exist for Written
        var csvExists = CorpusFileExists(CorpusFileKind.AnswerKeyCsv, folder);
        var xlsxExists = CorpusFileExists(CorpusFileKind.AnswerKeyXlsx, folder);
        var keyStatus = CombinedStatus(new[]
        {
            FileStatusFor(corpus, CorpusFileKind.AnswerKeyCsv, csvExists),
            FileStatusFor(corpus, CorpusFileKind.AnswerKeyXlsx, xlsxExists)
        });
        _rowAnswerKey.Line2.Text = plan != null
            ? CorpusUiText.AnswerKeyLine(plan.EmailCount + plan.AttachmentCount, plan.EmailCount, plan.AttachmentCount)
            : CorpusUiText.AnswerKeyFilesLine;
        _rowAnswerKey.Line3!.Text = CorpusUiText.FileStatusLine(keyStatus,
            WrittenLocal(corpus, CorpusFileKind.AnswerKeyCsv, CorpusFileKind.AnswerKeyXlsx), string.Empty);
        _rowAnswerKey.Line3.ForeColor = FileLineColor(keyStatus);
        SetDot(_rowAnswerKey, DotColor(CorpusDotState(keyStatus)));

        // A run for the current plan adds "· {g} generated, {p} planned" (key rows still Planned)
        var generation = corpus?.Generation;
        var runCurrent = plan != null && generation != null && generation.RunPlanId == plan.PlanId && corpus!.PlanId == plan.PlanId;
        var keyWritten = WrittenLocal(corpus, CorpusFileKind.AnswerKeyCsv, CorpusFileKind.AnswerKeyXlsx);
        if (runCurrent && HasGenerated(generation!) && keyStatus == CorpusFileStatus.Written && keyWritten.HasValue)
        {
            var planned = Math.Max(0, plan!.EmailCount + plan.AttachmentCount - generation!.EmailsWritten - generation.EmailsFailed);
            _rowAnswerKey.Line3.Text = GenerationUiText.AnswerKeyRunClause(keyWritten.Value, generation.EmailsWritten, planned);
        }
        _btnOpenCsv.Visible = csvExists;
        _btnOpenXlsx.Visible = xlsxExists;

        // Review protocol
        var protocolExists = CorpusFileExists(CorpusFileKind.ReviewProtocol, folder);
        var protocolStatus = FileStatusFor(corpus, CorpusFileKind.ReviewProtocol, protocolExists);
        var protocolName = folder.Length > 0
            ? CorpusFileName(CorpusFileKind.ReviewProtocol, folder)
            : ReviewProtocolBuilder.FileNameFor(CaseFolderNaming.DefaultFolderName(draft));
        _rowProtocol.Line2.Text = CorpusUiText.ProtocolLine(guide.Issues.Count, protocolName);
        _rowProtocol.Line3!.Text = CorpusUiText.FileStatusLine(protocolStatus, WrittenLocal(corpus, CorpusFileKind.ReviewProtocol), string.Empty);
        _rowProtocol.Line3.ForeColor = FileLineColor(protocolStatus);
        SetDot(_rowProtocol, DotColor(CorpusDotState(protocolStatus)));
        _btnOpenProtocol.Visible = protocolExists;

        RefreshGenerationRows(folder, corpus, generation, runCurrent);
    }

    private static bool HasGenerated(GenerationState generation) =>
        generation.ThreadsWritten + generation.ThreadsWithWarnings + generation.ThreadsFailed > 0 || generation.FilesWritten > 0;

    /// <summary>
    /// Mailboxes and Story so far rows (05-UI-SPEC S3) from the cached run counts and the ledger in case.json.
    /// </summary>
    private void RefreshGenerationRows(string folder, CorpusState? corpus, GenerationState? generation, bool runCurrent)
    {
        var mailboxesPath = MailboxesPath(folder);
        var mailboxes = GenerationUiText.MailboxesRow(new MailboxesRowInput
        {
            Generated = generation != null && HasGenerated(generation),
            EarlierPlan = generation != null && generation.RunPlanId != corpus!.PlanId,
            Total = generation?.ThreadsTotal ?? 0,
            Written = generation == null ? 0 : generation.ThreadsWritten + generation.ThreadsWithWarnings,
            Warnings = generation?.ThreadsWithWarnings ?? 0,
            Failed = generation?.ThreadsFailed ?? 0,
            Files = generation?.FilesWritten ?? 0,
            Mailboxes = generation?.Mailboxes ?? 0,
            FolderExists = mailboxesPath != null && Directory.Exists(mailboxesPath)
        });
        ApplySummaryRow(_rowMailboxes, mailboxes);
        _btnOpenMailboxes.Visible = mailboxes.ShowPrimary;
        _btnGenerateEmails.Visible = mailboxes.ShowSecondary;

        var entries = generation?.Ledger?.Entries ?? new List<LedgerEntry>();
        var storyName = StoryFileName(folder);
        var storyExists = folder.Length > 0 && File.Exists(Path.Combine(folder, storyName));
        var story = GenerationUiText.StoryRow(new StoryRowInput
        {
            Entries = entries.Count,
            Threads = entries.Select(e => e.SourceThreadId).Where(id => !string.IsNullOrWhiteSpace(id)).Distinct(StringComparer.Ordinal).Count(),
            FileName = storyName,
            FileExists = storyExists,
            WrittenLocal = storyExists ? WrittenLocal(corpus, CorpusFileKind.StorySoFar) : null,
            Error = generation?.StoryError ?? string.Empty
        });
        ApplySummaryRow(_rowStory, story);
        _btnOpenStory.Visible = story.ShowPrimary;
        // A ledger from an earlier plan is not exported against the current plan; Start Over on Generate Corpus clears it
        _btnExportStory.Visible = story.ShowSecondary && runCurrent;
    }

    // Name = verified case folder name + constant suffix, never a name read from case.json (T-05-52)
    private string StoryFileName(string folder) =>
        StorySoFarDocxBuilder.FileNameFor(folder.Length > 0 ? FolderName(folder) : CaseFolderNaming.DefaultFolderName(Draft));

    /// <summary>
    /// Full path of the case's Mailboxes folder, or null when the case folder is empty or the path does not resolve
    /// directly under it (T-05-52).
    /// </summary>
    internal static string? MailboxesPath(string caseFolder)
    {
        if (string.IsNullOrWhiteSpace(caseFolder))
            return null;

        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(caseFolder));
        var path = Path.GetFullPath(Path.Combine(root, PlanNumbering.MailboxesFolder));
        var expected = root + Path.DirectorySeparatorChar + PlanNumbering.MailboxesFolder;
        return string.Equals(path, expected, StringComparison.OrdinalIgnoreCase) ? path : null;
    }

    private static void ApplySummaryRow(ContentRow row, SummaryRowContent content)
    {
        row.Line2.Text = content.Line2;
        row.Line3!.Text = content.Line3;
        row.Line3.ForeColor = ToneColor(content.Line3Tone, Theme.TextMuted);
        SetDot(row, ToneColor(content.DotTone, Theme.BorderStrong));
    }

    private static Color ToneColor(SummaryTone tone, Color muted) => tone switch
    {
        SummaryTone.Success => Theme.Success,
        SummaryTone.Warning => Theme.Warning,
        SummaryTone.Danger => Theme.Danger,
        _ => muted
    };

    // First and last month of the histogram; an empty plan falls back to the plan's window
    private static (DateTime First, DateTime Last) PlanMonths(PlanSummary summary, CorpusPlan plan)
    {
        if (summary.Months.Count > 0)
        {
            var first = summary.Months.Min(m => m.Month);
            var last = summary.Months.Max(m => m.Month);
            return (first.ToDateTime(TimeOnly.MinValue), last.ToDateTime(TimeOnly.MinValue));
        }

        var start = CaseDraft.TryParseDate(plan.Settings?.WindowStart, out var s) ? s : DateTime.Today;
        var end = CaseDraft.TryParseDate(plan.Settings?.WindowEnd, out var e) ? e : start;
        return (start, end);
    }

    private void UpdateButtons()
    {
        var draft = Draft;
        var hasFolder = CaseFolder.Length > 0;
        _btnExportAll.Enabled = !_busy && draft.CoreDocument != null;
        _btnOpenFolder.Enabled = !_busy && hasFolder;
        _btnMoveFolder.Enabled = !_busy && hasFolder;
        foreach (var kind in ExportKinds)
        {
            var row = _exportRows[kind];
            // The cast and key-facts exports need the case guide
            row.Export!.Enabled = !_busy && draft.CoreDocument != null && (kind == CaseExportKind.CoreDocument || draft.Guide != null);
            row.Open!.Enabled = !_busy;
        }
        foreach (var button in _rowPlan.Buttons.Concat(_rowAnswerKey.Buttons).Concat(_rowProtocol.Buttons)
                     .Concat(_rowMailboxes.Buttons).Concat(_rowStory.Buttons))
            button.Enabled = !_busy;
    }

    /// <summary>
    /// Disables every action (and Back/Finish) while an export or move runs.
    /// </summary>
    private void SetBusy(bool busy)
    {
        _busy = busy;
        UpdateButtons();
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private static void SetDot(ContentRow row, Color color)
    {
        row.DotColor = color;
        row.Dot.Invalidate();
    }

    private void SetStatus(string text, Color color)
    {
        _lblStatus.Text = text;
        _lblStatus.ForeColor = color;
    }

    // ----- IWizardStep -----

    public void BindState(WizardState state)
    {
        _state = state;
    }

    /// <summary>
    /// Binds and refreshes without saving (render tests); reads only the case folder's file times.
    /// </summary>
    internal void LoadForRender(WizardState state)
    {
        BindState(state);
        RefreshAll();
    }

    public async Task OnEnterStepAsync()
    {
        _failedExports.Clear();
        if (!string.IsNullOrWhiteSpace(_state.PendingNotice))
        {
            SetStatus(_state.PendingNotice, Theme.Success);
            _state.PendingNotice = string.Empty;
        }
        else
        {
            SetStatus(string.Empty, Theme.TextMuted);
        }

        Draft.AdvanceStage(CaseStage.Summary);
        RefreshAll();

        if (CaseStore.CanSave(Draft))
        {
            var saved = await _state.CaseStore.SaveAsync(Draft);
            if (IsDisposed)
                return;
            if (!saved)
                SetStatus(CaseGuideUiText.SaveFailedText(_state.CaseStore.LastSaveError), Theme.Danger);
        }

        RefreshAll();
    }

    public Task OnLeaveStepAsync() => Task.CompletedTask;

    /// <summary>
    /// Finish: flush the save and close with no prompt (D-10); WizardForm does the closing.
    /// </summary>
    public async Task<bool> ValidateStepAsync()
    {
        if (_busy)
            return false;

        if (CaseStore.CanSave(Draft))
        {
            await _state.CaseStore.SaveAsync(Draft);
            if (!IsDisposed)
                RefreshAll();
        }
        return true;
    }

    // ----- Actions -----

    private async Task ExportOneAsync(CaseExportKind kind)
    {
        if (_busy)
            return;

        SetBusy(true);
        try
        {
            if (await ExportAsync(kind) && !IsDisposed)
                SetStatus(CaseGuideUiText.ExportedCountText(1), Theme.Success);
        }
        finally
        {
            if (!IsDisposed)
            {
                SetBusy(false);
                RefreshAll();
            }
        }
    }

    private async Task ExportAllAsync()
    {
        if (_busy)
            return;

        SetBusy(true);
        try
        {
            var exported = 0;
            foreach (var kind in ExportKinds)
            {
                // The first failure is reported and the rest are skipped; earlier successes stay on disk
                if (!await ExportAsync(kind) || IsDisposed)
                    return;
                exported++;
            }
            SetStatus(CaseGuideUiText.ExportedCountText(exported), Theme.Success);
        }
        finally
        {
            if (!IsDisposed)
            {
                SetBusy(false);
                RefreshAll();
            }
        }
    }

    /// <summary>
    /// Builds one .docx from the in-memory draft and writes it atomically into the case folder.
    /// Reports failures on the status strip and the row's dot; returns true on success.
    /// </summary>
    private async Task<bool> ExportAsync(CaseExportKind kind)
    {
        var noun = CaseGuideDocxExporter.DocumentNoun(kind);
        var fileName = string.Empty;
        try
        {
            var folder = _state.CaseStore.EnsureFolder(Draft);
            fileName = ExportFileName(kind, folder);
            SetStatus(CaseGuideUiText.ExportingText(noun), Theme.TextMuted);

            // Built on the UI thread so the export is a consistent snapshot of the draft
            var bytes = CaseGuideDocxExporter.Build(Draft, kind);
            var path = Path.Combine(folder, fileName);
            await CaseStore.WriteAtomicAsync(path, bytes);
            if (IsDisposed)
                return false;

            _failedExports.Remove(kind);
            RefreshAll();
            return true;
        }
        catch (Exception ex) when (IsFileOpenElsewhere(ex, fileName))
        {
            return ExportFailed(kind, CaseGuideUiText.ExportFileOpenText(fileName));
        }
        catch (Exception ex)
        {
            // UI boundary: any export failure (I/O, access, missing guide, OpenXml) is shown, never thrown
            return ExportFailed(kind, CaseGuideUiText.ExportFailedText(noun, ex.Message));
        }
    }

    private bool ExportFailed(CaseExportKind kind, string message)
    {
        if (IsDisposed)
            return false;
        _failedExports.Add(kind);
        RefreshAll();
        SetStatus(message, Theme.Danger);
        return false;
    }

    // Word holds an open .docx with a sharing lock: the replace fails with a sharing/lock violation or access denied
    private bool IsFileOpenElsewhere(Exception ex, string fileName)
    {
        const int SharingViolation = unchecked((int)0x80070020);
        const int LockViolation = unchecked((int)0x80070021);
        if (ex is IOException io && (io.HResult == SharingViolation || io.HResult == LockViolation))
            return true;
        if (ex is not UnauthorizedAccessException || string.IsNullOrEmpty(fileName) || CaseFolder.Length == 0)
            return false;

        var path = Path.Combine(CaseFolder, fileName);
        if (!File.Exists(path))
            return false;
        try
        {
            using var probe = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            return false;
        }
        catch (IOException)
        {
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            // Read-only or no permission: a real access problem, not an open file
            return false;
        }
    }

    private void OpenExport(CaseExportKind kind)
    {
        var folder = CaseFolder;
        if (folder.Length == 0)
            return;

        var fileName = ExportFileName(kind, folder);
        var path = Path.Combine(folder, fileName);
        if (!File.Exists(path))
        {
            RefreshAll();
            return;
        }

        try
        {
            // Opens the .docx this step just wrote; the path is built from the verified case folder
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or IOException)
        {
            SetStatus($"Could not open {fileName}: {ex.Message.Trim().TrimEnd('.')}.", Theme.Danger);
        }
    }

    /// <summary>
    /// Opens an answer-key or review-protocol file through the shell. The path is the verified case folder plus the
    /// known file name, never a name read from case.json (T-04-45).
    /// </summary>
    private void OpenCorpusFile(CorpusFileKind kind)
    {
        var folder = CaseFolder;
        if (folder.Length == 0)
        {
            RefreshAll();
            return;
        }

        var fileName = CorpusFileName(kind, folder);
        var path = Path.Combine(folder, fileName);
        if (!File.Exists(path))
        {
            RefreshAll();
            SetStatus(CorpusUiText.MissingFileText(fileName), Theme.Danger);
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or IOException)
        {
            SetStatus($"Could not open {fileName}: {ex.Message.Trim().TrimEnd('.')}.", Theme.Danger);
        }
    }

    private void OpenCaseFolder()
    {
        var folder = CaseFolder;
        if (folder.Length == 0 || !Directory.Exists(folder))
        {
            RefreshAll();
            return;
        }

        try
        {
            // Never shell-execute a path read from case.json (ASVS V12)
            Process.Start("explorer.exe", "\"" + Path.GetFullPath(folder) + "\"");
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            SetStatus($"Could not open the case folder: {ex.Message.Trim().TrimEnd('.')}.", Theme.Danger);
        }
    }

    // ----- Generated corpus (05-UI-SPEC S3) -----

    /// <summary>
    /// Opens a file through the shell. Test seam.
    /// </summary>
    internal Action<string> OpenFile = path =>
    {
        using var process = Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
    };

    /// <summary>
    /// Opens a folder in Explorer. Test seam.
    /// </summary>
    internal Action<string> OpenFolder = path =>
    {
        using var process = Process.Start("explorer.exe", "\"" + path + "\"");
    };

    /// <summary>
    /// Opens "{case folder}\Mailboxes" in Explorer, only when it resolves directly under the verified case folder (T-05-52).
    /// </summary>
    internal void OpenMailboxesFolder()
    {
        var path = MailboxesPath(CaseFolder);
        if (path == null || !Directory.Exists(path))
        {
            RefreshAll();
            return;
        }

        try
        {
            OpenFolder(path);
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            SetStatus($"Could not open the Mailboxes folder: {ex.Message.Trim().TrimEnd('.')}.", Theme.Danger);
        }
    }

    /// <summary>
    /// Opens the story-so-far .docx: verified case folder + the derived file name, never a name from case.json.
    /// A missing file refreshes the row and says how to write it again.
    /// </summary>
    internal void OpenStory()
    {
        var folder = CaseFolder;
        if (folder.Length == 0)
        {
            RefreshAll();
            return;
        }

        var fileName = StoryFileName(folder);
        var path = Path.GetFullPath(Path.Combine(folder, fileName));
        if (!File.Exists(path))
        {
            RefreshAll();
            SetStatus(GenerationUiText.StoryMissingText(fileName), Theme.Danger);
            return;
        }

        try
        {
            OpenFile(path);
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or IOException)
        {
            SetStatus($"Could not open {fileName}: {ex.Message.Trim().TrimEnd('.')}.", Theme.Danger);
        }
    }

    /// <summary>
    /// Rewrites the story-so-far .docx from the ledger saved in case.json (CORP-11), stamps it, and saves. The journal is
    /// replayed only for the warnings section and the thread counts; an unreadable journal still exports the ledger.
    /// </summary>
    internal async Task ExportStoryAsync()
    {
        if (_busy)
            return;

        var draft = Draft;
        var folder = CaseFolder;
        var plan = CorpusPlanStore.EnsureLoaded(draft).Plan;
        var generation = draft.Corpus?.Generation;
        if (folder.Length == 0 || plan == null || generation == null || generation.RunPlanId != plan.PlanId
            || draft.Corpus!.PlanId != plan.PlanId || generation.Ledger.Entries.Count == 0)
        {
            RefreshAll();
            return;
        }

        var guide = draft.Guide ?? new CaseGuide();
        var ledger = generation.Ledger;
        var fileName = StoryFileName(folder);
        SetBusy(true);
        SetStatus(GenerationUiText.StoryExportingText, Theme.TextMuted);
        try
        {
            GenerationFileResult result;
            try
            {
                // Every action (and Back/Finish) is disabled meanwhile, so the draft is a stable snapshot off the UI thread
                result = await Task.Run(async () =>
                {
                    var replay = GenerationJournal.Replay(folder, plan);
                    var state = GenerationRunState.From(plan, replay);
                    return await GenerationOutputs.WriteStorySoFarAsync(folder, draft, plan, ledger, state, PeopleDirectory.Create(guide, plan));
                });
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                result = new GenerationFileResult { FailedFileName = fileName, Error = ex.Message };
            }
            if (IsDisposed)
                return;

            generation.StoryError = result.Succeeded ? string.Empty : $"{result.FailedFileName}: {result.Error}";
            CorpusApproval.RecordPartialWrites(draft, result.Files);
            if (result.Succeeded)
                SetStatus(GenerationUiText.StoryExportedText(fileName), Theme.Success);
            else if (result.FileOpenElsewhere)
                SetStatus(GenerationUiText.StoryLockedText(fileName), Theme.Warning);
            else
                SetStatus(GenerationUiText.StoryExportFailedText(fileName, result.Error), Theme.Danger);

            if (CaseStore.CanSave(draft))
            {
                var saved = await _state.CaseStore.SaveAsync(draft);
                if (IsDisposed)
                    return;
                if (!saved)
                    SetStatus(CaseGuideUiText.SaveFailedText(_state.CaseStore.LastSaveError), Theme.Danger);
            }
        }
        finally
        {
            if (!IsDisposed)
            {
                SetBusy(false);
                RefreshAll();
            }
        }
    }

    // Test accessors (the step is never shown in step-logic tests)
    internal Label StatusLabel => _lblStatus;
    internal Label MailboxesLine3 => _rowMailboxes.Line3!;
    internal Label StoryLine3 => _rowStory.Line3!;
    internal Label AnswerKeyLine3 => _rowAnswerKey.Line3!;
    internal Color MailboxesDotColor => _rowMailboxes.DotColor;
    internal Color StoryDotColor => _rowStory.DotColor;
    internal Button OpenMailboxesButton => _btnOpenMailboxes;
    internal Button GenerateEmailsButton => _btnGenerateEmails;
    internal Button OpenStoryButton => _btnOpenStory;
    internal Button ExportStoryButton => _btnExportStory;

    private async Task MoveCaseFolderAsync()
    {
        if (_busy)
            return;

        var folder = CaseFolder;
        if (folder.Length == 0)
        {
            RefreshAll();
            return;
        }

        string chosen;
        using (var dialog = new FolderBrowserDialog
        {
            Description = "Choose where to move the case folder",
            UseDescriptionForTitle = true,
            ShowNewFolderButton = true
        })
        {
            if (dialog.ShowDialog(this) != DialogResult.OK || string.IsNullOrWhiteSpace(dialog.SelectedPath))
                return;
            chosen = dialog.SelectedPath;
        }

        var target = Path.Combine(chosen, Path.GetFileName(folder.TrimEnd('\\', '/')));
        var (title, text) = CaseGuideUiText.C7MoveFolder(CaseGuideUiText.DisplayPath(target, MyDocuments));
        if (MessageBox.Show(this, text, title, MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
            return;

        SetBusy(true);
        try
        {
            var store = _state.CaseStore;
            var draft = Draft;
            // A cross-volume move copies files; keep the UI responsive (every action is disabled meanwhile)
            var newFolder = await Task.Run(() => store.MoveFolder(draft, chosen));
            if (IsDisposed)
                return;

            await store.SaveAsync(draft);
            if (IsDisposed)
                return;

            var leftover = store.LastMoveLeftover;
            if (leftover.Length > 0)
                SetStatus(CaseGuideUiText.MovedWithLeftoverText(CaseGuideUiText.DisplayPath(newFolder, MyDocuments), CaseGuideUiText.DisplayPath(leftover, MyDocuments)), Theme.Warning);
            else
                SetStatus(CaseGuideUiText.MovedText(CaseGuideUiText.DisplayPath(newFolder, MyDocuments)), Theme.Success);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            if (!IsDisposed)
                SetStatus(CaseGuideUiText.MoveFailedText(ex.Message), Theme.Danger);
        }
        finally
        {
            if (!IsDisposed)
            {
                SetBusy(false);
                RefreshAll();
            }
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            _toolTip?.Dispose();
        base.Dispose(disposing);
    }

    private sealed class ContentRow
    {
        public TableLayoutPanel Layout = null!;
        public Panel Dot = null!;
        public Label Line1 = null!;
        public Label Line2 = null!;
        public Label? Line3;
        public Button? Open;
        public Button? Export;
        public List<Button> Buttons = new();
        public Color DotColor = Theme.BorderStrong;
    }

    private sealed class DoubleBufferedPanel : Panel
    {
        public DoubleBufferedPanel()
        {
            DoubleBuffered = true;
        }
    }
}
