using System.ComponentModel;
using ReelDiscovery.Helpers;
using ReelDiscovery.Models.Case;
using ReelDiscovery.Services.Case;

namespace ReelDiscovery.UserControls;

/// <summary>
/// Shared skeleton for every Case Guide tab (UI-SPEC S1 Row 3): a 60% grid card and a 40% detail card
/// (heading, meta, notice slot, fields, divider, guidance, Delete/Regenerate). Subclasses supply columns,
/// rows and detail fields; the base keeps sort and selection across rebuilds and paints the status dots.
/// </summary>
public abstract class GuideTabPanel : UserControl
{
    private readonly bool _hasToolbar;
    private readonly string _noun;
    private readonly string _guidancePlaceholder;
    private readonly List<Label> _wrapLabels = new();
    private readonly Dictionary<Control, Label> _errorLabels = new();
    private readonly List<Control> _invalidFields = new();
    private IGuideHost? _host;
    private TableLayoutPanel _body = null!;
    private TableLayoutPanel _detailLayout = null!;
    private Panel _fieldsPanel = null!;
    private Label _emptyGridLabel = null!;
    private Label _emptyDetailLabel = null!;
    private Font _bodyFont = null!;
    private Font _smallFont = null!;
    private bool _rebuilding;
    private bool _refreshQueued;
    private bool _busy;
    // Grid-cell commits waiting for the message loop (WR-02)
    private readonly List<Action> _queuedGridEdits = new();
    private bool _detailNarrow;
    private string? _shownId;

    protected DataGridView Grid = null!;
    protected Label Caption = null!;
    protected FlowLayoutPanel HeaderRight = null!;
    protected Label CountLabel = null!;
    protected Label DetailHeading = null!;
    protected Label DetailMeta = null!;
    protected Panel NoticeSlot = null!;
    protected TableLayoutPanel Fields = null!;
    protected TextBox GuidanceBox = null!;
    protected ThemedButton DeleteButton = null!;
    protected ThemedButton RegenerateButton = null!;

    protected GuideTabPanel(GuidePart part, string caption, string noun, bool hasToolbar, string guidancePlaceholder)
    {
        Part = part;
        _noun = noun;
        _hasToolbar = hasToolbar;
        _guidancePlaceholder = guidancePlaceholder;
        InitializeUI(caption);
    }

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public GuidePart Part { get; }

    protected IGuideHost Host => _host ?? throw new InvalidOperationException("Attach a guide host before using the tab.");

    protected bool IsAttached => _host != null;

    protected bool Busy => _busy;

    /// <summary>
    /// Buttons the shell shows on the tabs row while this tab is selected.
    /// </summary>
    public abstract IReadOnlyList<Control> TabActions { get; }

    /// <summary>
    /// Id of the selected row, or null when nothing is selected.
    /// </summary>
    protected string? SelectedId =>
        Grid.SelectedRows.Count > 0 ? Grid.SelectedRows[0].Tag as string : null;

    /// <summary>
    /// False keeps the grid read-only even when no run is active (Issues, D-08).
    /// </summary>
    protected virtual bool GridEditable => true;

    /// <summary>
    /// Text shown in place of the grid when BuildRows returns nothing; tabs with filters override it.
    /// </summary>
    protected virtual string EmptyRowsText => CaseGuideUiText.EmptyGridText(Part);

    /// <summary>
    /// Adds the grid columns. Called once from the constructor, before the subclass constructor body runs,
    /// so it may only use Grid and LogicalToDeviceUnits.
    /// </summary>
    protected abstract void BuildColumns();

    /// <summary>
    /// Rows in display order. Cells follow Grid.Columns; the status-dot cell may be omitted.
    /// </summary>
    protected abstract IEnumerable<(string Id, object?[] Cells)> BuildRows();

    /// <summary>
    /// Fills the detail card for the entity (null shows the empty detail state, already handled by the base).
    /// </summary>
    protected abstract void ShowDetail(string? id);

    public void Attach(IGuideHost host)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
    }

    private void InitializeUI(string caption)
    {
        this.Padding = new Padding(0);
        this.BackColor = Theme.Background;
        _bodyFont = Theme.BodyFont();
        _smallFont = Theme.SmallFont();
        var half = LogicalToDeviceUnits(Theme.SmallGap);
        var inset = LogicalToDeviceUnits(15);
        var buttonHeight = LogicalToDeviceUnits(34);

        _body = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Margin = new Padding(0),
            Padding = new Padding(0),
            BackColor = Theme.Background
        };
        _body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 60));
        _body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40));
        _body.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        // ----- Grid card (Padding = 1, so every inner left inset is 15: Content x = 16) -----
        var gridCard = new CardPanel { Dock = DockStyle.Fill, Padding = new Padding(1), Margin = new Padding(0, 0, half, 0) };
        var gridLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Margin = new Padding(0),
            BackColor = Theme.Surface
        };
        gridLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        gridLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));                            // 0: Card header
        gridLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, LogicalToDeviceUnits(1)));  // 1: Divider
        gridLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));                        // 2: Grid or empty text

        var top = LogicalToDeviceUnits(14);
        var bottom = LogicalToDeviceUnits(10);
        var header = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(inset, top, inset, bottom),
            Margin = new Padding(0),
            // Content height 28 (the compact toggles) on every tab, so the grids top-align when switching tabs
            MinimumSize = new Size(0, LogicalToDeviceUnits(28) + top + bottom),
            BackColor = Theme.Surface
        };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        header.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); // 0: Caption | toggles + count
        Caption = new Label
        {
            Text = caption,
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(0),
            UseMnemonic = false,
            Font = Theme.LabelFont(),
            ForeColor = Theme.Text
        };
        HeaderRight = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Anchor = AnchorStyles.Right,
            Margin = new Padding(LogicalToDeviceUnits(Theme.Gap), 0, 0, 0),
            Padding = new Padding(0),
            BackColor = Theme.Surface
        };
        CountLabel = new Label
        {
            AutoSize = true,
            // Neither Top nor Bottom: centered against 28-high toggles in the flow row
            Anchor = AnchorStyles.Left,
            Margin = new Padding(0),
            UseMnemonic = false,
            Font = _smallFont,
            ForeColor = Theme.TextMuted
        };
        HeaderRight.Controls.Add(CountLabel);
        header.Controls.Add(Caption, 0, 0);
        header.Controls.Add(HeaderRight, 1, 0);
        gridLayout.Controls.Add(header, 0, 0);
        gridLayout.Controls.Add(new Panel { Dock = DockStyle.Fill, BackColor = Theme.Border, Margin = new Padding(0) }, 0, 1);

        var gridHost = new Panel { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, LogicalToDeviceUnits(6)), BackColor = Theme.Surface };
        Grid = new DataGridView { Dock = DockStyle.Fill, EditMode = DataGridViewEditMode.EditOnKeystrokeOrF2 };
        GridStyle.Apply(Grid, this);
        _emptyGridLabel = new Label
        {
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
            Padding = new Padding(inset, 0, inset, 0),
            UseMnemonic = false,
            Font = _bodyFont,
            ForeColor = Theme.TextMuted,
            BackColor = Theme.Surface,
            Visible = false
        };
        gridHost.Controls.Add(Grid);
        gridHost.Controls.Add(_emptyGridLabel);
        gridLayout.Controls.Add(gridHost, 0, 2);
        gridCard.Controls.Add(gridLayout);
        _body.Controls.Add(gridCard, 0, 0);

        // ----- Detail card (Padding = 16: Content x = 16) -----
        var detailCard = new CardPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(LogicalToDeviceUnits(Theme.CardPadding)),
            Margin = new Padding(half, 0, 0, 0)
        };
        _detailLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = _hasToolbar ? 6 : 3,
            Margin = new Padding(0),
            BackColor = Theme.Surface,
            Visible = false
        };
        _detailLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        _detailLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));     // 0: Heading + meta
        _detailLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));     // 1: Inline notice (hidden unless needed)
        _detailLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); // 2: Fields (scrolls)
        if (_hasToolbar)
        {
            _detailLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, LogicalToDeviceUnits(1))); // 3: Divider
            _detailLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));                          // 4: Guidance
            _detailLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));                          // 5: Delete | Regenerate
        }

        var headingBlock = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Margin = new Padding(0, 0, 0, LogicalToDeviceUnits(Theme.SmallGap)),
            BackColor = Theme.Surface
        };
        headingBlock.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        headingBlock.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // 0: Heading
        headingBlock.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // 1: Meta line
        DetailHeading = new Label
        {
            AutoSize = false,
            AutoEllipsis = true,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            Margin = new Padding(0),
            UseMnemonic = false,
            Font = Theme.HeadingFont(),
            ForeColor = Theme.Text
        };
        DetailHeading.Height = DetailHeading.PreferredHeight;
        DetailMeta = new Label
        {
            AutoSize = false,
            AutoEllipsis = true,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            Margin = new Padding(0, LogicalToDeviceUnits(2), 0, 0),
            UseMnemonic = false,
            Font = _smallFont,
            ForeColor = Theme.TextMuted
        };
        DetailMeta.Height = DetailMeta.PreferredHeight;
        headingBlock.Controls.Add(DetailHeading, 0, 0);
        headingBlock.Controls.Add(DetailMeta, 0, 1);
        _detailLayout.Controls.Add(headingBlock, 0, 0);

        // A 1-column TLP (a Panel) so subclasses can stack an AutoSize notice in it
        var noticeSlot = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Margin = new Padding(0, 0, 0, LogicalToDeviceUnits(12)),
            BackColor = Theme.Surface,
            Visible = false
        };
        noticeSlot.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        NoticeSlot = noticeSlot;
        _detailLayout.Controls.Add(NoticeSlot, 0, 1);

        // The divider's 12 px top gap is this panel's bottom margin (a margin inside a 1 px row would vanish)
        _fieldsPanel = new Panel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            Margin = new Padding(0, 0, 0, _hasToolbar ? LogicalToDeviceUnits(12) : 0),
            BackColor = Theme.Surface
        };
        Fields = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            ColumnCount = 1,
            RowCount = 0,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            GrowStyle = TableLayoutPanelGrowStyle.AddRows,
            Margin = new Padding(0),
            Padding = new Padding(0),
            BackColor = Theme.Surface
        };
        Fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        _fieldsPanel.Controls.Add(Fields);
        _detailLayout.Controls.Add(_fieldsPanel, 0, 2);

        GuidanceBox = new TextBox { PlaceholderText = _guidancePlaceholder };
        DeleteButton = (ThemedButton)ButtonHelper.CreateButton($"Delete {_noun}", 0, buttonHeight, ButtonStyle.Default);
        DeleteButton.Margin = new Padding(0);
        RegenerateButton = (ThemedButton)ButtonHelper.CreateButton($"Regenerate {_noun}", 0, buttonHeight, ButtonStyle.Primary);
        // 8 apart even when the narrow detail card leaves no space between the two buttons
        RegenerateButton.Margin = new Padding(half, 0, 0, 0);
        if (_hasToolbar)
        {
            _detailLayout.Controls.Add(new Panel { Dock = DockStyle.Fill, BackColor = Theme.Border, Margin = new Padding(0) }, 0, 3);

            var guidanceHost = TextFieldHost.Create(this, GuidanceBox);
            guidanceHost.Dock = DockStyle.Fill;
            guidanceHost.Margin = new Padding(0, LogicalToDeviceUnits(12), 0, 0);
            _detailLayout.Controls.Add(guidanceHost, 0, 4);

            var toolbar = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 3,
                RowCount = 1,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Margin = new Padding(0, LogicalToDeviceUnits(Theme.SmallGap), 0, 0),
                BackColor = Theme.Surface
            };
            toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            toolbar.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // 0: Delete | spacer | Regenerate
            toolbar.Controls.Add(DeleteButton, 0, 0);
            toolbar.Controls.Add(RegenerateButton, 2, 0);
            _detailLayout.Controls.Add(toolbar, 0, 5);
        }

        _emptyDetailLabel = new Label
        {
            Text = CaseGuideUiText.EmptyDetailText(Part),
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
            UseMnemonic = false,
            Font = _bodyFont,
            ForeColor = Theme.TextMuted,
            BackColor = Theme.Surface
        };
        detailCard.Controls.Add(_detailLayout);
        detailCard.Controls.Add(_emptyDetailLabel);
        _body.Controls.Add(detailCard, 1, 0);

        this.Controls.Add(_body);

        // Columns before handlers: the dot painter looks columns up by name
        BuildColumns();

        // Wire handlers only after every control exists (handlers read other controls)
        _body.SizeChanged += (s, e) => ApplyDetailMinimum();
        header.SizeChanged += (s, e) => AlignEmptyDetail(header.Height);
        _fieldsPanel.ClientSizeChanged += (s, e) => SyncFieldsWidth();
        Fields.SizeChanged += (s, e) => SyncWrapLabels();
        Grid.CellPainting += Grid_CellPainting;
        Grid.SelectionChanged += Grid_SelectionChanged;
        Grid.KeyDown += Grid_KeyDown;
        // Never show the stock DataError dialog (T-03-42); edits are validated in CaseGuideEditing instead
        Grid.DataError += (s, e) => e.ThrowException = false;
    }

    // ----- Layout -----

    private void ApplyDetailMinimum()
    {
        // The detail card never gets narrower than 344 (UI-SPEC says 320, but "Delete Character" and "Regenerate Character"
        // need 309 plus the 32 card padding to sit 8 apart): below that the grid card shrinks instead
        var minimum = LogicalToDeviceUnits(344) + LogicalToDeviceUnits(Theme.SmallGap);
        var narrow = _body.ClientSize.Width * 0.4 < minimum;
        if (narrow == _detailNarrow)
            return;

        _detailNarrow = narrow;
        _body.SuspendLayout();
        _body.ColumnStyles[0] = narrow ? new ColumnStyle(SizeType.Percent, 100) : new ColumnStyle(SizeType.Percent, 60);
        _body.ColumnStyles[1] = narrow ? new ColumnStyle(SizeType.Absolute, minimum) : new ColumnStyle(SizeType.Percent, 40);
        _body.ResumeLayout(true);
    }

    private void AlignEmptyDetail(int headerHeight)
    {
        // Center the "Select ..." text on the same line as the grid's empty text, which is centered below
        // the grid card header (1 px card padding + header + 1 px divider, 6 px grid bottom inset)
        var top = Math.Max(0, 1 + headerHeight - LogicalToDeviceUnits(6));
        if (_emptyDetailLabel.Padding.Top != top)
            _emptyDetailLabel.Padding = new Padding(0, top, 0, 0);
    }

    private void SyncFieldsWidth()
    {
        // Pitfall 7: lock the fields table to the client width so the panel never scrolls horizontally
        var width = Math.Max(1, _fieldsPanel.ClientSize.Width);
        if (Fields.MaximumSize.Width == width && Fields.MinimumSize.Width == width)
            return;

        Fields.MinimumSize = new Size(width, 0);
        Fields.MaximumSize = new Size(width, 0);
        Fields.Width = width;
        SyncWrapLabels();
    }

    private void SyncWrapLabels()
    {
        foreach (var label in _wrapLabels)
            SetWrapWidth(label);
    }

    private void SetWrapWidth(Label label)
    {
        var width = Math.Max(1, Fields.Width - label.Margin.Horizontal);
        if (label.MaximumSize.Width != width)
            label.MaximumSize = new Size(width, 0);
    }

    // ----- Rows, sort and selection -----

    /// <summary>
    /// Rebuilds the rows keeping the sort column, direction, selected entity and scroll position,
    /// then refreshes the header and the detail card.
    /// </summary>
    public void RefreshFromDraft()
    {
        _refreshQueued = false;
        if (_host == null || IsDisposed)
            return;

        RefreshHeader();

        var sortColumn = Grid.SortedColumn;
        var sortOrder = Grid.SortOrder;
        var selectedId = SelectedId ?? _shownId;
        var firstDisplayed = Grid.FirstDisplayedScrollingRowIndex;
        var dot = Grid.Columns[GridStyle.DotColumnName];

        _rebuilding = true;
        try
        {
            if (Grid.IsCurrentCellInEditMode)
                Grid.EndEdit();

            Grid.Rows.Clear();
            foreach (var (id, cells) in BuildRows())
            {
                var values = cells;
                if (dot != null && dot.Index == 0 && cells.Length == Grid.Columns.Count - 1)
                    values = new object?[] { string.Empty }.Concat(cells).ToArray();
                var index = Grid.Rows.Add(values!);
                Grid.Rows[index].Tag = id;
            }

            if (sortColumn != null && sortOrder != SortOrder.None && sortColumn.SortMode != DataGridViewColumnSortMode.NotSortable)
                Grid.Sort(sortColumn, sortOrder == SortOrder.Descending ? ListSortDirection.Descending : ListSortDirection.Ascending);

            var hasRows = Grid.Rows.Count > 0;
            Grid.Visible = hasRows;
            _emptyGridLabel.Text = EmptyRowsText;
            _emptyGridLabel.Visible = !hasRows;

            if (hasRows)
            {
                if (firstDisplayed > 0 && Grid.IsHandleCreated)
                    Grid.FirstDisplayedScrollingRowIndex = Math.Min(firstDisplayed, Grid.Rows.Count - 1);
                var row = FindRow(selectedId) ?? Grid.Rows[0];
                SelectRow(row);
            }
        }
        finally
        {
            _rebuilding = false;
        }

        ApplyEditableState();
        ShowDetailCore(SelectedId);
    }

    /// <summary>
    /// Queues RefreshFromDraft on the message loop. Use it from grid cell events:
    /// rebuilding inside CellEndEdit throws (reentrant SetCurrentCellAddressCore).
    /// </summary>
    protected void RequestRefresh()
    {
        if (_refreshQueued)
            return;

        if (IsHandleCreated)
        {
            _refreshQueued = true;
            // Rebuilding inside CellEndEdit throws (reentrant SetCurrentCellAddressCore)
            BeginInvoke(new Action(RefreshFromDraft));
        }
        else
        {
            RefreshFromDraft();
        }
    }

    /// <summary>
    /// Updates the caption, count line and tab actions before the rows are rebuilt.
    /// </summary>
    protected virtual void RefreshHeader()
    {
    }

    /// <summary>
    /// Selects the entity's row, scrolls it into view and focuses the first detail field.
    /// Tabs with filters override this to clear a filter that hides the entity first.
    /// </summary>
    public virtual bool SelectEntity(string id)
    {
        var row = FindRow(id);
        if (row == null)
            return false;

        SelectRow(row);
        if (_shownId != id)
            ShowDetailCore(id);
        FocusFirstField();
        return true;
    }

    public virtual void ShowNeedsReview()
    {
    }

    /// <summary>
    /// Commits detail fields that changed but have not been applied yet (selection change, step leave).
    /// </summary>
    public virtual void CommitPendingEdits()
    {
    }

    /// <summary>
    /// Queues a grid-cell commit from CellEndEdit (applying it there would rebuild the grid reentrantly, T-03-46)
    /// and posts a flush. <see cref="CommitGridEdits"/> applies the queue at once when a run starts, on Next, on leave and on close.
    /// </summary>
    protected void QueueGridEdit(Action commit)
    {
        _queuedGridEdits.Add(commit);
        if (IsHandleCreated && !IsDisposed)
            BeginInvoke(new Action(FlushQueuedGridEdits));
        else
            FlushQueuedGridEdits();
    }

    /// <summary>
    /// Ends a cell still in edit mode and applies every queued grid edit now, so typed text is never dropped by a run that
    /// starts before the posted commit, a Next validation, or a window close (WR-02). Call outside grid events only.
    /// </summary>
    public void CommitGridEdits()
    {
        if (IsDisposed)
            return;

        // Fires CellEndEdit, which queues the commit
        if (Grid.IsCurrentCellInEditMode)
            Grid.EndEdit();
        FlushQueuedGridEdits();
    }

    private void FlushQueuedGridEdits()
    {
        if (IsDisposed || _queuedGridEdits.Count == 0)
            return;

        var edits = _queuedGridEdits.ToList();
        _queuedGridEdits.Clear();
        foreach (var edit in edits)
            edit();
    }

    public virtual void SetBusy(bool busy)
    {
        _busy = busy;
        ApplyEditableState();
        SetFieldsReadOnly(Fields, busy);
        GuidanceBox.ReadOnly = busy;
        foreach (var action in TabActions)
            action.Enabled = !busy;
        UpdateToolbar();
    }

    /// <summary>
    /// Re-applies grid read-only state; subclasses mark their editable columns after calling the base.
    /// </summary>
    protected virtual void ApplyEditableState()
    {
        Grid.ReadOnly = _busy || !GridEditable;
    }

    protected virtual void UpdateToolbar()
    {
        var enabled = !_busy && _shownId != null;
        DeleteButton.Enabled = enabled;
        RegenerateButton.Enabled = enabled;
    }

    private DataGridViewRow? FindRow(string? id)
    {
        if (string.IsNullOrEmpty(id))
            return null;

        foreach (DataGridViewRow row in Grid.Rows)
        {
            if (row.Tag is string tag && tag == id)
                return row;
        }
        return null;
    }

    private void SelectRow(DataGridViewRow row)
    {
        var column = Grid.Columns.GetFirstColumn(DataGridViewElementStates.Visible);
        if (column == null)
            return;

        // Setting CurrentCell selects the full row and scrolls it into view
        Grid.CurrentCell = row.Cells[column.Index];
        row.Selected = true;
    }

    private void Grid_SelectionChanged(object? sender, EventArgs e)
    {
        if (_rebuilding)
            return;

        var id = SelectedId;
        if (id == _shownId)
            return;

        // Edits to the previous entity are applied, never lost, when the selection moves
        CommitPendingEdits();
        ShowDetailCore(id);
    }

    private void Grid_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode != Keys.Delete || Grid.IsCurrentCellInEditMode || !_hasToolbar)
            return;

        e.Handled = true;
        if (DeleteButton.Enabled)
            DeleteButton.PerformClick();
    }

    private void ShowDetailCore(string? id)
    {
        _shownId = id;
        ClearFieldErrors();
        _detailLayout.Visible = id != null;
        _emptyDetailLabel.Visible = id == null;
        ShowDetail(id);
        if (_busy)
            SetFieldsReadOnly(Fields, true);
        UpdateToolbar();
    }

    protected void FocusFirstField()
    {
        var target = FirstFocusable(Fields);
        target?.Focus();
    }

    private static Control? FirstFocusable(Control parent)
    {
        foreach (Control child in parent.Controls.Cast<Control>().OrderBy(c => c.TabIndex))
        {
            if (child is TextBox or ComboBox or CheckedListBox && child.CanFocus)
                return child;
            var nested = FirstFocusable(child);
            if (nested != null)
                return nested;
        }
        return null;
    }

    private static void SetFieldsReadOnly(Control parent, bool readOnly)
    {
        foreach (Control child in parent.Controls)
        {
            switch (child)
            {
                case TextBox box:
                    box.ReadOnly = readOnly;
                    break;
                case ComboBox:
                case LinkPicker:
                    child.Enabled = !readOnly;
                    break;
                default:
                    SetFieldsReadOnly(child, readOnly);
                    break;
            }
        }
    }

    // ----- Status dots -----

    /// <summary>
    /// Dot color: Accent while the entry is written, otherwise the worst finding (Danger / Warning) or Success.
    /// </summary>
    protected virtual Color DotColor(string id)
    {
        if (_host == null)
            return Theme.BorderStrong;
        if (id == Host.WritingEntityId)
            return Theme.Accent;

        return CaseGuideValidator.WorstFor(Host.Findings, id) switch
        {
            IssueSeverity.Error => Theme.Danger,
            IssueSeverity.Warning => Theme.Warning,
            _ => Theme.Success
        };
    }

    private void Grid_CellPainting(object? sender, DataGridViewCellPaintingEventArgs e)
    {
        if (e.RowIndex < 0 || e.Graphics == null)
            return;

        var dot = Grid.Columns[GridStyle.DotColumnName];
        if (dot == null || e.ColumnIndex != dot.Index)
            return;

        e.Paint(e.CellBounds, DataGridViewPaintParts.All & ~DataGridViewPaintParts.ContentForeground);
        if (Grid.Rows[e.RowIndex].Tag is string id)
            Theme.DrawStatusDot(e.Graphics, e.CellBounds, DotColor(id), this);
        e.Handled = true;
    }

    // ----- Detail field helpers -----

    /// <summary>
    /// Removes every field from the detail card (call at the start of ShowDetail).
    /// </summary>
    protected void ClearFields()
    {
        Fields.SuspendLayout();
        var old = Fields.Controls.Cast<Control>().ToList();
        Fields.Controls.Clear();
        Fields.RowStyles.Clear();
        Fields.RowCount = 0;
        foreach (var control in old)
            control.Dispose();
        _wrapLabels.Clear();
        _errorLabels.Clear();
        _invalidFields.Clear();
        Fields.ResumeLayout(true);
        _fieldsPanel.AutoScrollPosition = Point.Empty;
    }

    private void AddRow(Control control)
    {
        Fields.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        Fields.Controls.Add(control, 0, Fields.RowCount);
        Fields.RowCount++;
    }

    private Label CreateCaption(string caption)
    {
        // 12 between consecutive fields; the first caption sits directly under the heading block
        var topMargin = Fields.Controls.Count == 0 ? 0 : LogicalToDeviceUnits(12);
        return new Label
        {
            Text = caption,
            AutoSize = true,
            Margin = new Padding(0, topMargin, 0, LogicalToDeviceUnits(4)),
            UseMnemonic = false,
            Font = _smallFont,
            ForeColor = Theme.TextMuted,
            BackColor = Theme.Surface
        };
    }

    private Label CreateWrapLabel(string text, Font font, Color color, Padding margin)
    {
        var label = new Label
        {
            Text = text,
            AutoSize = true,
            Margin = margin,
            UseMnemonic = false,
            Font = font,
            ForeColor = color,
            BackColor = Theme.Surface
        };
        _wrapLabels.Add(label);
        SetWrapWidth(label);
        return label;
    }

    private Label CreateErrorLabel()
    {
        var label = CreateWrapLabel(string.Empty, _smallFont, Theme.Danger, new Padding(0, LogicalToDeviceUnits(4), 0, 0));
        label.Visible = false;
        return label;
    }

    /// <summary>
    /// Read-only field: caption above a wrapping Body label. Returns the value label.
    /// </summary>
    protected Label AddCaptionedLabel(string caption, string text)
    {
        AddRow(CreateCaption(caption));
        var value = CreateWrapLabel(text, _bodyFont, Theme.Text, new Padding(0));
        AddRow(value);
        return value;
    }

    /// <summary>
    /// Read-only list field: caption above one wrapping label per line, 4 apart, so a wrapped line
    /// never reads as a new entry. Shows "—" when there are no lines.
    /// </summary>
    protected void AddCaptionedLines(string caption, IEnumerable<string> lines)
    {
        AddRow(CreateCaption(caption));
        var items = lines.Where(l => !string.IsNullOrWhiteSpace(l)).ToList();
        if (items.Count == 0)
            items.Add("—");
        for (var i = 0; i < items.Count; i++)
            AddRow(CreateWrapLabel(items[i], _bodyFont, Theme.Text, new Padding(0, i == 0 ? 0 : LogicalToDeviceUnits(4), 0, 0)));
    }

    /// <summary>
    /// Caption above any control (LinkPicker, custom rows). The control fills the width.
    /// </summary>
    protected void AddField(string caption, Control control)
    {
        AddRow(CreateCaption(caption));
        control.Dock = DockStyle.Fill;
        control.Margin = new Padding(0);
        AddRow(control);
        var error = CreateErrorLabel();
        _errorLabels[control] = error;
        AddRow(error);
    }

    /// <summary>
    /// Text field in a rounded host (single-line 34 high, or N lines), with an optional muted hint under it.
    /// </summary>
    protected CardPanel AddTextField(string caption, TextBox box, int lines = 1, string? hint = null)
    {
        AddRow(CreateCaption(caption));
        var host = TextFieldHost.Create(this, box, lines);
        host.Dock = DockStyle.Fill;
        host.Margin = new Padding(0);
        AddRow(host);
        var error = CreateErrorLabel();
        _errorLabels[host] = error;
        _errorLabels[box] = error;
        AddRow(error);
        if (!string.IsNullOrEmpty(hint))
            AddRow(CreateWrapLabel(hint, _smallFont, Theme.TextMuted, new Padding(0, LogicalToDeviceUnits(4), 0, 0)));
        return host;
    }

    /// <summary>
    /// Two text fields side by side (50/50, 8 apart), e.g. First name + Last name.
    /// </summary>
    protected (CardPanel Left, CardPanel Right) AddPairedTextFields(string captionLeft, TextBox left, string captionRight, TextBox right)
    {
        var leftHost = TextFieldHost.Create(this, left);
        var rightHost = TextFieldHost.Create(this, right);
        AddPair(captionLeft, leftHost, left, captionRight, rightHost, right);
        return (leftHost, rightHost);
    }

    /// <summary>
    /// Two drop-down list fields side by side (50/50, 8 apart), e.g. Role + Tier.
    /// </summary>
    protected (ComboBox Left, ComboBox Right) AddPairedCombos(string captionLeft, IEnumerable<string> itemsLeft,
        string captionRight, IEnumerable<string> itemsRight)
    {
        var left = CreateCombo(itemsLeft);
        var right = CreateCombo(itemsRight);
        AddPair(captionLeft, HostCombo(left), left, captionRight, HostCombo(right), right);
        return (left, right);
    }

    /// <summary>
    /// A text field and a drop-down list side by side (50/50, 8 apart), e.g. Date + Significance.
    /// </summary>
    protected (CardPanel Left, ComboBox Right) AddPairedTextAndCombo(string captionLeft, TextBox left,
        string captionRight, IEnumerable<string> itemsRight)
    {
        var leftHost = TextFieldHost.Create(this, left);
        var right = CreateCombo(itemsRight);
        AddPair(captionLeft, leftHost, left, captionRight, HostCombo(right), right);
        return (leftHost, right);
    }

    private void AddPair(string captionLeft, Control leftHost, Control left, string captionRight, Control rightHost, Control right)
    {
        var halfGap = LogicalToDeviceUnits(Theme.SmallGap) / 2;
        var topMargin = Fields.Controls.Count == 0 ? 0 : LogicalToDeviceUnits(12);
        var pair = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 3,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Margin = new Padding(0, topMargin, 0, 0),
            BackColor = Theme.Surface
        };
        pair.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        pair.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        pair.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // 0: Captions
        pair.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // 1: Fields
        pair.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // 2: Validation messages

        var column = 0;
        foreach (var (caption, host, box) in new[] { (captionLeft, leftHost, left), (captionRight, rightHost, right) })
        {
            var margin = column == 0 ? new Padding(0, 0, halfGap, 0) : new Padding(halfGap, 0, 0, 0);
            var label = new Label
            {
                Text = caption,
                AutoSize = true,
                Margin = new Padding(margin.Left, 0, margin.Right, LogicalToDeviceUnits(4)),
                UseMnemonic = false,
                Font = _smallFont,
                ForeColor = Theme.TextMuted,
                BackColor = Theme.Surface
            };
            host.Dock = DockStyle.Fill;
            host.Margin = margin;
            var error = new Label
            {
                AutoSize = true,
                Margin = new Padding(margin.Left, LogicalToDeviceUnits(4), margin.Right, 0),
                UseMnemonic = false,
                Font = _smallFont,
                ForeColor = Theme.Danger,
                BackColor = Theme.Surface,
                Visible = false
            };
            _errorLabels[host] = error;
            _errorLabels[box] = error;
            pair.Controls.Add(label, column, 0);
            pair.Controls.Add(host, column, 1);
            pair.Controls.Add(error, column, 2);
            column++;
        }

        pair.SizeChanged += (s, e) =>
        {
            foreach (var error in new[] { _errorLabels[leftHost], _errorLabels[rightHost] })
                error.MaximumSize = new Size(Math.Max(1, pair.Width / 2 - halfGap), 0);
        };
        AddRow(pair);
    }

    /// <summary>
    /// Drop-down list choice field.
    /// </summary>
    protected ComboBox AddCombo(string caption, IEnumerable<string> items)
    {
        AddRow(CreateCaption(caption));
        var combo = CreateCombo(items);
        var host = HostCombo(combo);
        host.Dock = DockStyle.Fill;
        AddRow(host);
        var error = CreateErrorLabel();
        _errorLabels[host] = error;
        _errorLabels[combo] = error;
        AddRow(error);
        return combo;
    }

    private ComboBox CreateCombo(IEnumerable<string> items)
    {
        var combo = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            FlatStyle = FlatStyle.Flat,
            Font = _bodyFont,
            ForeColor = Theme.Text,
            BackColor = Theme.Surface,
            Dock = DockStyle.Fill,
            Margin = new Padding(0)
        };
        combo.Items.AddRange(items.Cast<object>().ToArray());
        return combo;
    }

    /// <summary>
    /// Puts a drop-down list in the same rounded 34-high border as the text fields, with its text on the
    /// text fields' 10 px inner edge, so every field in the detail card shares one left edge and height.
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

        // A disabled drop-down list paints a grey system frame inside the rounded border; while disabled
        // (a run is active) the host shows the choice as plain text instead, like a read-only text field
        var readOnlyText = new Label
        {
            // Top-docked and as far from the bottom border as the top padding: centered on the 34 px height,
            // and the rounded border stays visible
            Dock = DockStyle.Top,
            Height = Math.Max(1, height - 2 * vertical),
            AutoSize = false,
            AutoEllipsis = true,
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = Padding.Empty,
            UseMnemonic = false,
            Font = _bodyFont,
            ForeColor = Theme.Text,
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
        return host;
    }

    /// <summary>
    /// Muted footer or hint line (SmallFont, TextMuted, top 12) that wraps to the panel width.
    /// </summary>
    protected Label AddNote(string text)
    {
        var note = CreateWrapLabel(text, _smallFont, Theme.TextMuted, new Padding(0, LogicalToDeviceUnits(12), 0, 0));
        AddRow(note);
        return note;
    }

    /// <summary>
    /// Danger border (text fields) and a SmallFont Danger message directly under the field.
    /// </summary>
    protected void ShowFieldError(Control field, string message)
    {
        if (field is CardPanel host)
            TextFieldHost.SetInvalid(host, true);
        else if (field.Parent is CardPanel parentHost && _errorLabels.ContainsKey(parentHost))
            TextFieldHost.SetInvalid(parentHost, true);
        _invalidFields.Add(field);

        if (_errorLabels.TryGetValue(field, out var error))
        {
            error.Text = message;
            error.Visible = true;
        }
    }

    protected void ClearFieldErrors()
    {
        foreach (var field in _invalidFields)
        {
            if (field is CardPanel host)
                TextFieldHost.SetInvalid(host, false);
            else if (field.Parent is CardPanel parentHost)
                TextFieldHost.SetInvalid(parentHost, false);
        }
        _invalidFields.Clear();
        foreach (var error in _errorLabels.Values)
        {
            error.Visible = false;
            error.Text = string.Empty;
        }
    }

    /// <summary>
    /// Adds a control to the card header just before the count line (compact filter toggles).
    /// </summary>
    protected void AddHeaderControl(Control control)
    {
        HeaderRight.Controls.Add(control);
        HeaderRight.Controls.SetChildIndex(control, HeaderRight.Controls.Count - 2);
    }
}
