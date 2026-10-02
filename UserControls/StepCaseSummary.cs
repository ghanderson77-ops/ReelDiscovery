using System.ComponentModel;
using System.Diagnostics;
using ReelDiscovery.Helpers;
using ReelDiscovery.Models;
using ReelDiscovery.Models.Case;
using ReelDiscovery.Services.Case;

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
        _rowCaseFile.Line2.Text = CaseGuideUiText.CaseFileDescription;

        var rows = new[] { _rowCaseFile, _exportRows[CaseExportKind.CoreDocument], _exportRows[CaseExportKind.Cast], _exportRows[CaseExportKind.KeyFacts], _rowValues, _rowChecks };
        for (var i = 0; i < rows.Length; i++)
        {
            if (i > 0)
            {
                rowsLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 1)); // Divider
                rowsLayout.Controls.Add(Divider(), 0, rowsLayout.RowStyles.Count - 1);
            }
            rowsLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // Content row
            rowsLayout.Controls.Add(rows[i].Layout, 0, rowsLayout.RowStyles.Count - 1);
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
        foreach (var row in rows)
            row.Dot.Paint += (s, e) => PaintDot(row, e);
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

    private ContentRow CreateRow(string title, bool hasLine3, bool hasButtons, int buttonHeight)
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
            row.Open = ButtonHelper.CreateButton("Open in Word", 0, buttonHeight, ButtonStyle.Default);
            row.Open.Margin = new Padding(0, 0, LogicalToDeviceUnits(Theme.SmallGap), 0);
            row.Open.Visible = false;
            row.Export = ButtonHelper.CreateButton("Export to Word", 0, buttonHeight, ButtonStyle.Default);
            row.Export.Margin = new Padding(0);
            buttons.Controls.Add(row.Open);
            buttons.Controls.Add(row.Export);
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

        UpdateButtons();
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
