using System.ComponentModel;
using System.Globalization;
using ReelDiscovery.Helpers;
using ReelDiscovery.Models.Qa;
using ReelDiscovery.Services.Evidence;
using ReelDiscovery.UserControls;

namespace ReelDiscovery.Forms;

/// <summary>
/// One row of QaFindingsDialog: a scan finding or a blind-review disagreement, plus what C-Q1 states about its thread.
/// </summary>
public class QaFindingRow
{
    public string ThreadId { get; set; } = string.Empty;
    public string DocId { get; set; } = string.Empty;
    public DateTime DateSent { get; set; }
    public string Subject { get; set; } = string.Empty;

    /// <summary>Check (scans) or Answer key label (blind review).</summary>
    public string Col1 { get; set; } = string.Empty;

    /// <summary>Reviewer label (blind review only).</summary>
    public string Col2 { get; set; } = string.Empty;

    /// <summary>What was found (scans) or the reviewer's reason (blind review).</summary>
    public string Text { get; set; } = string.Empty;

    public bool Relevant { get; set; }

    /// <summary>Thread subject quoted in C-Q1.</summary>
    public string RegenerateSubject { get; set; } = string.Empty;

    public int EmlCount { get; set; }
    public int AttachmentCount { get; set; }

    /// <summary>A saved discovery document cites one of this thread's emails (09 S5 C-Q1 clause).</summary>
    public bool CitedByDiscovery { get; set; }
}

/// <summary>
/// Consistency scan findings, blind-review disagreements or the leakage report (07-UI-SPEC S3). Lists only; the corpus
/// changes only when the user confirms Regenerate Thread... (C-Q1), which goes through the regenerate delegate
/// (ThreadInvalidation.InvalidateAsync in the app).
/// </summary>
public class QaFindingsDialog : Form
{
    internal static readonly Size LogicalSize = new(900, 560);
    internal static readonly Size LogicalMinimumSize = new(640, 400);

    private const int DialogPadding = 24;
    private const string Col1Name = "Col1";
    private const string Col2Name = "Col2";

    private readonly QaFindingsMode _mode;
    private readonly IReadOnlyList<QaFindingRow> _rows;
    private readonly LeakageReport? _leakage;
    private readonly string _reviewerModel;
    private readonly ISet<string> _queued;
    private readonly Func<string, Task<ThreadInvalidationResult>>? _regenerate;
    private readonly Action<string>? _openEmail;
    private bool _busy;
    private ToolTip? _toolTip;

    private Label _lblHeadline = null!;
    private Label _lblSummary = null!;
    private DataGridView _grid = null!;
    private Label _lblFooter = null!;
    private Button _btnOpen = null!;
    private Button _btnRegenerate = null!;
    private Button _btnClose = null!;

    public QaFindingsDialog(QaFindingsMode mode, IReadOnlyList<QaFindingRow> rows, LeakageReport? leakage,
        string reviewerModel, ISet<string> queuedThreadIds, Func<string, Task<ThreadInvalidationResult>>? regenerate,
        Action<string>? openEmail)
    {
        _mode = mode;
        _rows = rows ?? new List<QaFindingRow>();
        _leakage = leakage;
        _reviewerModel = reviewerModel ?? string.Empty;
        _queued = new HashSet<string>(queuedThreadIds ?? new HashSet<string>(), StringComparer.Ordinal);
        _regenerate = regenerate;
        _openEmail = openEmail;

        // Scale the empty form once (ThemedDialog rule) before any child exists, so the LogicalToDeviceUnits sizes
        // given to children below are never scaled a second time
        this.SuspendLayout();
        this.AutoScaleDimensions = new SizeF(7F, 15F);
        this.AutoScaleMode = AutoScaleMode.Font;
        this.Font = Theme.BodyFont();
        this.ResumeLayout(false);
        this.PerformAutoScale();

        InitializeUI();
    }

    /// <summary>
    /// True once any thread was queued for regeneration here; the caller refreshes Case Summary on close.
    /// </summary>
    public bool QueuedAny { get; private set; }

    /// <summary>
    /// C-Q1 seam: (title, text) → Yes. Defaults to the Warning MessageBox with No as the default button.
    /// </summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal Func<string, string, bool>? Confirm { get; set; }

    internal DataGridView FindingsGrid => _grid;
    internal Button RegenerateButton => _btnRegenerate;
    internal Button OpenButton => _btnOpen;
    internal Button CloseButton => _btnClose;
    internal Label FooterLabel => _lblFooter;
    internal Label HeadlineLabel => _lblHeadline;
    internal Label SummaryLabel => _lblSummary;

    private bool ListMode => _mode != QaFindingsMode.Leakage;

    private void InitializeUI()
    {
        var content = QaUiText.FindingsContent(_mode,
            _mode == QaFindingsMode.Scans ? _rows.Select(r => r.DocId).Distinct(StringComparer.Ordinal).Count() : _rows.Count,
            _rows.Select(r => r.ThreadId).Distinct(StringComparer.Ordinal).Count(), _reviewerModel, _leakage);

        this.Text = content.Title;
        this.FormBorderStyle = FormBorderStyle.Sizable;
        this.StartPosition = FormStartPosition.CenterParent;
        this.MinimizeBox = false;
        this.MaximizeBox = false;
        this.ShowInTaskbar = false;
        this.BackColor = Theme.Background;
        this.Padding = new Padding(LogicalToDeviceUnits(DialogPadding));
        this.Size = new Size(LogicalToDeviceUnits(LogicalSize.Width), LogicalToDeviceUnits(LogicalSize.Height));
        this.MinimumSize = new Size(LogicalToDeviceUnits(LogicalMinimumSize.Width), LogicalToDeviceUnits(LogicalMinimumSize.Height));

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 5,
            BackColor = Theme.Background,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));      // 0: Headline
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));      // 1: Summary line
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));  // 2: Grid
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));      // 3: Footer status
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));      // 4: Buttons

        _lblHeadline = CreateLabel(content.Headline, Theme.HeadingFont(), Theme.Text);
        _lblHeadline.Margin = new Padding(0, 0, 0, LogicalToDeviceUnits(8));
        root.Controls.Add(_lblHeadline, 0, 0);

        _lblSummary = CreateLabel(content.Summary, Theme.SmallFont(), Theme.TextMuted);
        _lblSummary.Margin = new Padding(0, 0, 0, LogicalToDeviceUnits(16));
        root.Controls.Add(_lblSummary, 0, 1);
        var summaryTip = QaUiText.FindingsSummaryTooltip(_mode);
        if (summaryTip.Length > 0 && _rows.Count > 0)
        {
            _toolTip = new ToolTip();
            _toolTip.SetToolTip(_lblSummary, summaryTip);
        }

        _grid = new DataGridView
        {
            Dock = DockStyle.Fill,
            ReadOnly = true,
            ScrollBars = ScrollBars.Both,
            ShowCellToolTips = true,
            Margin = Padding.Empty
        };
        GridStyle.Apply(_grid, this);
        _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None;
        _grid.DefaultCellStyle.WrapMode = DataGridViewTriState.False;
        if (ListMode)
            AddListColumns();
        else
            AddLeakageColumns();
        FillRows();
        root.Controls.Add(_grid, 0, 2);

        _lblFooter = CreateLabel(string.Empty, Theme.SmallFont(), Theme.TextMuted);
        _lblFooter.Margin = new Padding(0, LogicalToDeviceUnits(8), 0, 0);
        _lblFooter.Visible = false;
        root.Controls.Add(_lblFooter, 0, 3);

        _btnOpen = ButtonHelper.CreateButton(QaUiText.OpenEmailText, 0, LogicalToDeviceUnits(34), ButtonStyle.Default);
        _btnRegenerate = ButtonHelper.CreateButton(QaUiText.RegenerateThreadText, 0, LogicalToDeviceUnits(34), ButtonStyle.Primary);
        _btnClose = ButtonHelper.CreateButton(QaUiText.CloseText, 0, LogicalToDeviceUnits(34), ButtonStyle.Default);
        _btnClose.DialogResult = DialogResult.Cancel;

        var buttons = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.RightToLeft,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            WrapContents = false,
            Anchor = AnchorStyles.Top | AnchorStyles.Right,
            BackColor = Theme.Background,
            Margin = new Padding(0, LogicalToDeviceUnits(16), 0, 0),
            Padding = Padding.Empty
        };
        // RightToLeft flow: the row reads Open Email, Regenerate Thread..., Close left to right; every button but the
        // leftmost visible one keeps the 8 px gap on its left
        _btnClose.Margin = ListMode ? new Padding(LogicalToDeviceUnits(Theme.SmallGap), 0, 0, 0) : Padding.Empty;
        _btnRegenerate.Margin = new Padding(LogicalToDeviceUnits(Theme.SmallGap), 0, 0, 0);
        _btnOpen.Margin = Padding.Empty;
        buttons.Controls.Add(_btnClose);
        buttons.Controls.Add(_btnRegenerate);
        buttons.Controls.Add(_btnOpen);
        _btnOpen.Visible = ListMode;
        _btnRegenerate.Visible = ListMode;
        root.Controls.Add(buttons, 0, 4);

        this.Controls.Add(root);
        this.CancelButton = _btnClose;

        if (_grid.Rows.Count == 0 && ListMode)
        {
            _grid.Visible = false;
            this.ActiveControl = _btnClose;
        }
        else
        {
            this.ActiveControl = _grid;
        }

        // Handlers wired after every control exists
        _grid.SelectionChanged += (s, e) => UpdateButtons();
        _grid.CellPainting += Grid_CellPainting;
        _grid.CellDoubleClick += (s, e) =>
        {
            if (e.RowIndex >= 0)
                OpenSelected();
        };
        _grid.KeyDown += Grid_KeyDown;
        _btnOpen.Click += (s, e) => OpenSelected();
        _btnRegenerate.Click += async (s, e) => await RegenerateSelectedAsync();
        UpdateButtons();
    }

    private Label CreateLabel(string text, Font font, Color color) => new()
    {
        Text = text,
        AutoSize = true,
        Dock = DockStyle.Fill,
        Font = font,
        ForeColor = color,
        BackColor = Theme.Background,
        // Subjects, reasons and addresses are model-written (T-07-58)
        UseMnemonic = false,
        Padding = Padding.Empty
    };

    private DataGridViewTextBoxColumn AddColumn(string name, string header, int logicalWidth, bool fill = false,
        float fillWeight = 100)
    {
        var column = new DataGridViewTextBoxColumn
        {
            Name = name,
            HeaderText = header,
            SortMode = DataGridViewColumnSortMode.Automatic,
            Resizable = DataGridViewTriState.True
        };
        if (fill)
        {
            column.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
            column.MinimumWidth = LogicalToDeviceUnits(logicalWidth);
            column.FillWeight = fillWeight;
        }
        else
        {
            column.AutoSizeMode = DataGridViewAutoSizeColumnMode.None;
            column.Width = LogicalToDeviceUnits(logicalWidth);
        }
        _grid.Columns.Add(column);
        return column;
    }

    private void AddListColumns()
    {
        GridStyle.AddDotColumn(_grid, this);
        // Widths amended in execution 07-17 (UI-SPEC S3): Date fits "Mar 12, 2024" and the status columns fit
        // "Not relevant · queued" / "Story detail · queued" next to the sort glyph
        AddColumn("DocId", "Doc ID", 112);
        var date = AddColumn("Date", "Date", 108);
        date.ValueType = typeof(DateTime);
        date.DefaultCellStyle.Format = "MMM d, yyyy";
        date.DefaultCellStyle.FormatProvider = CultureInfo.InvariantCulture;
        AddColumn("Subject", "Subject", 140, fill: true, fillWeight: 100);
        if (_mode == QaFindingsMode.Scans)
        {
            AddColumn(Col1Name, "Check", 144);
            AddColumn("Text", "What was found", 160, fill: true, fillWeight: 150);
        }
        else
        {
            AddColumn(Col1Name, "Answer key", 104);
            AddColumn(Col2Name, "Reviewer", 144);
            AddColumn("Text", "Reviewer's reason", 160, fill: true, fillWeight: 150);
        }
    }

    private void AddLeakageColumns()
    {
        AddColumn("Signal", "Signal", 200, fill: true);
        var share = AddColumn("Share", "Relevant share", 120);
        share.ValueType = typeof(double);
        share.DefaultCellStyle.Format = "0%";
        share.DefaultCellStyle.FormatProvider = CultureInfo.InvariantCulture;
        var emails = AddColumn("Emails", "Emails", 72);
        emails.ValueType = typeof(int);
        emails.DefaultCellStyle.Format = "N0";
        emails.DefaultCellStyle.FormatProvider = CultureInfo.InvariantCulture;
        var lift = AddColumn("Lift", "Lift", 80);
        lift.ValueType = typeof(double);
        _grid.CellFormatting += (s, e) =>
        {
            // Lift sorts by its number and shows signed percentage points
            if (e.RowIndex >= 0 && e.ColumnIndex == lift.Index && _grid.Rows[e.RowIndex].Tag is LeakageSignal signal)
            {
                e.Value = QaUiText.SignalCells(signal).Lift;
                e.FormattingApplied = true;
            }
        };
    }

    private void FillRows()
    {
        if (!ListMode)
        {
            foreach (var signal in _leakage?.Signals ?? new List<LeakageSignal>())
            {
                var index = _grid.Rows.Add(QaUiText.SignalText(signal), signal.RelevantShare, signal.Support, signal.Lift);
                var row = _grid.Rows[index];
                row.Tag = signal;
                row.Cells[0].ToolTipText = QaUiText.SignalText(signal);
            }
            return;
        }

        // Date ascending by default; ordinal Doc ID keeps same-day rows stable
        foreach (var finding in _rows.OrderBy(r => r.DateSent).ThenBy(r => r.DocId, StringComparer.Ordinal))
        {
            // From the row template so the GridStyle 32 px row height applies (a bare DataGridViewRow is ~22 px)
            var row = (DataGridViewRow)_grid.RowTemplate.Clone();
            row.CreateCells(_grid);
            row.Tag = finding;
            row.Cells[_grid.Columns["DocId"]!.Index].Value = finding.DocId;
            row.Cells[_grid.Columns["Date"]!.Index].Value = finding.DateSent;
            SetText(row, "Subject", finding.Subject);
            SetText(row, "Text", finding.Text);
            _grid.Rows.Add(row);
            SetStatusCell(_grid.Rows[_grid.Rows.Count - 1]);
        }
    }

    private void SetText(DataGridViewRow row, string column, string text)
    {
        var cell = row.Cells[_grid.Columns[column]!.Index];
        cell.Value = text;
        // Long cells ellipsize; the tooltip carries the full text
        cell.ToolTipText = text;
    }

    // Check (scans) or Reviewer (blind review) cell, with " · queued" once the thread is queued
    private void SetStatusCell(DataGridViewRow row)
    {
        if (row.Tag is not QaFindingRow finding)
            return;
        var queued = _queued.Contains(finding.ThreadId) ? QaUiText.QueuedSuffix : string.Empty;
        row.Cells[Col1Name].Value = _mode == QaFindingsMode.Scans ? finding.Col1 + queued : finding.Col1;
        if (_mode == QaFindingsMode.BlindReview)
            row.Cells[Col2Name].Value = finding.Col2 + queued;
    }

    private QaFindingRow? SelectedFinding =>
        _grid.SelectedRows.Count > 0 ? _grid.SelectedRows[0].Tag as QaFindingRow : null;

    internal Color DotColorAt(int rowIndex) =>
        _grid.Rows[rowIndex].Tag is QaFindingRow finding && _queued.Contains(finding.ThreadId) ? Theme.BorderStrong : Theme.Warning;

    internal void UpdateButtons()
    {
        var selected = SelectedFinding;
        _btnOpen.Enabled = !_busy && selected != null && _openEmail != null;
        _btnRegenerate.Enabled = !_busy && selected != null && _regenerate != null && !_queued.Contains(selected.ThreadId);
    }

    internal void OpenSelected()
    {
        var selected = SelectedFinding;
        if (selected != null && _openEmail != null)
            _openEmail(selected.DocId);
    }

    /// <summary>
    /// Regenerate Thread...: C-Q1, then the regenerate delegate; marks the row queued and states the result in the footer.
    /// </summary>
    internal async Task RegenerateSelectedAsync()
    {
        var selected = SelectedFinding;
        if (selected == null || _regenerate == null || _busy || _queued.Contains(selected.ThreadId))
            return;

        var (title, text) = QaUiText.CQ1RegenerateThread(selected.RegenerateSubject, selected.EmlCount,
            selected.AttachmentCount, selected.Relevant, selected.CitedByDiscovery);
        var confirm = Confirm ?? ((t, x) =>
            MessageBox.Show(this, x, t, MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) == DialogResult.Yes);
        if (!confirm(title, text))
            return;

        _busy = true;
        UpdateButtons();
        try
        {
            var result = await _regenerate(selected.ThreadId);
            if (!result.Appended)
            {
                ShowFooter(QaUiText.RegenerateFailed(result.Errors.FirstOrDefault() ?? ThreadInvalidation.NotWrittenMessage), Theme.Danger);
                return;
            }

            _queued.Add(selected.ThreadId);
            QueuedAny = true;
            foreach (DataGridViewRow row in _grid.Rows)
            {
                if (row.Tag is QaFindingRow finding && finding.ThreadId == selected.ThreadId)
                {
                    SetStatusCell(row);
                    _grid.InvalidateRow(row.Index);
                }
            }

            var lines = new List<string> { QaUiText.FooterQueued(_queued.Count) };
            if (result.Errors.Count > 0)
                lines.Add(QaUiText.FooterNotDeleted(result.Errors.Count, ReasonOf(result.Errors[0])));
            // Revision 2: the answer key rewrite failed after the invalidation; own line after the not-deleted line
            if (!string.IsNullOrWhiteSpace(result.KeyError))
                lines.Add(QaUiText.FooterKeyNotUpdated(result.KeyError));
            ShowFooter(string.Join("\n", lines), lines.Count > 1 ? Theme.Warning : Theme.Success);
        }
        catch (OperationCanceledException)
        {
            // WR-B02a: this runs from an async-void click handler, so a cancel (or an IO timeout surfacing as one) is a
            // footer message, never a rethrow that would crash the app
            ShowFooter(QaUiText.RegenerateFailed("the operation was cancelled"), Theme.Danger);
        }
        catch (Exception ex)
        {
            ShowFooter(QaUiText.RegenerateFailed(ex.InnerException?.Message ?? ex.Message), Theme.Danger);
        }
        finally
        {
            _busy = false;
            UpdateButtons();
        }
    }

    // Error lines are "{relative}: {reason}"; the footer states the reason
    private static string ReasonOf(string error)
    {
        var at = error.IndexOf(": ", StringComparison.Ordinal);
        return at >= 0 ? error[(at + 2)..] : error;
    }

    private void ShowFooter(string text, Color color)
    {
        _lblFooter.Text = text;
        _lblFooter.ForeColor = color;
        _lblFooter.Visible = true;
    }

    private void Grid_KeyDown(object? sender, KeyEventArgs e)
    {
        // Enter opens the email instead of moving to the next row
        if (e.KeyCode == Keys.Enter && ListMode)
        {
            OpenSelected();
            e.Handled = true;
        }
    }

    private void Grid_CellPainting(object? sender, DataGridViewCellPaintingEventArgs e)
    {
        if (e.RowIndex < 0 || e.Graphics == null || !ListMode)
            return;

        var dot = _grid.Columns[GridStyle.DotColumnName];
        if (dot == null || e.ColumnIndex != dot.Index)
            return;

        e.Paint(e.CellBounds, DataGridViewPaintParts.All & ~DataGridViewPaintParts.ContentForeground);
        Theme.DrawStatusDot(e.Graphics, e.CellBounds, DotColorAt(e.RowIndex), this);
        e.Handled = true;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            _toolTip?.Dispose();
        base.Dispose(disposing);
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);

        // Keep the scaled window on screen on small displays at high scaling
        var area = Screen.FromControl(this).WorkingArea;
        MinimumSize = new Size(Math.Min(area.Width, MinimumSize.Width), Math.Min(area.Height, MinimumSize.Height));
        Size = new Size(Math.Min(area.Width, Width), Math.Min(area.Height, Height));
    }

    /// <summary>
    /// WR-B03: Close, Esc and the title-bar X wait while a regeneration is in flight, so <see cref="QueuedAny"/> is final
    /// when ShowDialog returns (the caller then reprojects) and the continuation never writes to a disposed dialog.
    /// Windows shutdown and Application.Exit still close.
    /// </summary>
    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (_busy && e.CloseReason is CloseReason.UserClosing or CloseReason.None)
        {
            e.Cancel = true;
            return;
        }

        base.OnFormClosing(e);
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);

        // Regenerate Thread... needs a row the user picked, not the grid's automatic first row (the leakage table has
        // no row actions, so it starts unselected too)
        if (_grid.Rows.Count > 0)
        {
            _grid.ClearSelection();
            _grid.CurrentCell = null;
        }
        UpdateButtons();
    }

    protected override void OnLayout(LayoutEventArgs levent)
    {
        // Wrapping labels follow the client width inside the padding
        if (_lblSummary != null)
        {
            var width = Math.Max(0, ClientSize.Width - Padding.Horizontal);
            _lblHeadline.MaximumSize = new(width, 0);
            _lblSummary.MaximumSize = new(width, 0);
            _lblFooter.MaximumSize = new(width, 0);
        }
        base.OnLayout(levent);
    }
}
