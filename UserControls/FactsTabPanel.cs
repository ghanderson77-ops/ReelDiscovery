using System.Globalization;
using ReelDiscovery.Helpers;
using ReelDiscovery.Models.Case;
using ReelDiscovery.Services.Case;

namespace ReelDiscovery.UserControls;

public enum FactFilter
{
    All,
    NeedsReview
}

/// <summary>
/// What the Key Facts selection bar shows for the selected facts (pure, unit-tested).
/// </summary>
public class FactSelectionState
{
    public bool Visible { get; set; }
    public string Text { get; set; } = string.Empty;
    public bool CanRegenerate { get; set; }
    public bool CanMarkReviewed { get; set; }
    public bool CanDelete { get; set; }
}

/// <summary>
/// Key Facts tab (D-07, D-18..D-21): the timeline as a sortable grid and a detail card with three link pickers.
/// Date and Fact edit in the grid; every other field edits in the detail card. Every change goes through
/// CaseGuideEditing, which validates it, marks the fact user-edited and keeps the people who did it in Who knew.
/// </summary>
public class FactsTabPanel : GuideTabPanel
{
    private const string DatePlaceholder = "YYYY-MM-DD";
    private const string DidItSuffix = " (did it)";

    private static readonly FactSignificance[] Significances = { FactSignificance.Hot, FactSignificance.Key, FactSignificance.Supporting };

    private readonly ThemedButton _btnAll;
    private readonly ThemedButton _btnNeedsReview;
    private readonly CardPanel _notice;
    private readonly Label _lblNotice;
    private readonly ThemedButton _btnMarkReviewed;
    private readonly ThemedButton _btnAddFact;
    private readonly ThemedButton _btnRegenerateFacts;
    private readonly Panel _selectionBar;
    private readonly Label _lblSelection;
    private readonly FlowLayoutPanel _selectionButtons;
    private readonly ThemedButton _btnRegenerateSelected;
    private readonly ThemedButton _btnMarkSelectedReviewed;
    private readonly ThemedButton _btnDeleteSelected;
    private readonly Font _noteFont;
    private readonly Control[] _tabActions;
    private readonly Dictionary<TextBox, FactField> _textFields = new();
    private readonly Dictionary<Control, string> _fieldErrors = new();
    private readonly Dictionary<LinkPicker, string> _pickerState = new();
    private FactFilter _filter = FactFilter.All;

    // Detail fields of the shown fact; recreated only when the fact changes
    private string? _builtId;
    private TextBox? _txtDate;
    private ComboBox? _cmbSignificance;
    private TextBox? _txtTitle;
    private TextBox? _txtDescription;
    private LinkPicker? _pickIssues;
    private LinkPicker? _pickActors;
    private LinkPicker? _pickKnowers;
    private Label? _lblNotes;
    private string _notesShown = string.Empty;

    public FactsTabPanel()
        : base(GuidePart.Facts, "Key facts", "Fact", hasToolbar: true,
            guidancePlaceholder: "Optional guidance for Regenerate Fact, e.g. move it to the week after the site meeting")
    {
        var toggleHeight = LogicalToDeviceUnits(28);
        _btnAll = (ThemedButton)ButtonHelper.CreateButton("All", 0, toggleHeight, ButtonStyle.Toggle);
        _btnAll.Margin = new Padding(0, 0, LogicalToDeviceUnits(4), 0);
        _btnAll.Selected = true;
        _btnNeedsReview = (ThemedButton)ButtonHelper.CreateButton("Needs review", 0, toggleHeight, ButtonStyle.Toggle);
        // 12 between the last toggle and the count line
        _btnNeedsReview.Margin = new Padding(0, 0, LogicalToDeviceUnits(12), 0);
        AddHeaderControl(_btnAll);
        AddHeaderControl(_btnNeedsReview);

        // Inline needs-review notice (UI-SPEC detail row 1): wrapping text | Mark Reviewed
        _noteFont = Theme.SmallFont();
        var noticePadding = LogicalToDeviceUnits(12);
        _notice = new CardPanel
        {
            BorderColor = Theme.Warning,
            Padding = new Padding(noticePadding),
            // Anchored, not Fill: the AutoSize slot then takes the height SizeNotice sets instead of overriding it
            Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Right,
            Margin = new Padding(0)
        };
        var noticeLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Margin = new Padding(0),
            Padding = new Padding(0),
            BackColor = Theme.Surface
        };
        noticeLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        noticeLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        noticeLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); // 0: Notice text | Mark Reviewed
        _lblNotice = new Label
        {
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(0),
            UseMnemonic = false,
            Font = Theme.BodyFont(),
            ForeColor = Theme.Text,
            BackColor = Theme.Surface
        };
        _btnMarkReviewed = (ThemedButton)ButtonHelper.CreateButton("Mark Reviewed", 0, LogicalToDeviceUnits(34), ButtonStyle.Default);
        _btnMarkReviewed.Anchor = AnchorStyles.Right;
        _btnMarkReviewed.Margin = new Padding(noticePadding, 0, 0, 0);
        noticeLayout.Controls.Add(_lblNotice, 0, 0);
        noticeLayout.Controls.Add(_btnMarkReviewed, 1, 0);
        _notice.Controls.Add(noticeLayout);
        NoticeSlot.Controls.Add(_notice);

        var buttonHeight = LogicalToDeviceUnits(34);
        _btnAddFact = (ThemedButton)ButtonHelper.CreateButton("Add Fact", 0, buttonHeight, ButtonStyle.Default);
        _btnAddFact.Margin = new Padding(0);
        _btnRegenerateFacts = (ThemedButton)ButtonHelper.CreateButton("Regenerate Facts...", 0, buttonHeight, ButtonStyle.Default);
        _btnRegenerateFacts.Margin = new Padding(0);
        _tabActions = new Control[] { _btnAddFact, _btnRegenerateFacts };

        // Selection bar (2+ facts selected): "{n} facts selected" | Regenerate Selected... · Mark Reviewed · Delete Selected,
        // between the card header and the grid, on Content x (15 inside the Padding = 1 card), then a 1 px divider
        Grid.MultiSelect = true;
        var inset = LogicalToDeviceUnits(15);
        var smallGap = LogicalToDeviceUnits(Theme.SmallGap);
        // Placed by hand in LayoutSelectionBar (like the notice): nested AutoSize tables kept a stale height after the
        // one-line / two-line switch, and the spare height showed as a grey band above the grid
        _selectionBar = new Panel
        {
            // Anchored, not Fill: the AutoSize slot then takes the height LayoutSelectionBar sets
            Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Right,
            Margin = new Padding(0),
            Padding = new Padding(0),
            BackColor = Theme.Surface
        };
        _lblSelection = new Label
        {
            AutoSize = true,
            Margin = new Padding(0),
            UseMnemonic = false,
            Font = Theme.LabelFont(),
            ForeColor = Theme.Text,
            BackColor = Theme.Surface
        };
        _selectionButtons = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Margin = new Padding(0),
            Padding = new Padding(0),
            BackColor = Theme.Surface
        };
        _btnRegenerateSelected = (ThemedButton)ButtonHelper.CreateButton(CaseGuideUiText.RegenerateSelectedButtonText, 0, buttonHeight, ButtonStyle.Primary);
        _btnRegenerateSelected.Margin = new Padding(0, 0, smallGap, 0);
        _btnMarkSelectedReviewed = (ThemedButton)ButtonHelper.CreateButton("Mark Reviewed", 0, buttonHeight, ButtonStyle.Default);
        _btnMarkSelectedReviewed.Margin = new Padding(0, 0, smallGap, 0);
        _btnDeleteSelected = (ThemedButton)ButtonHelper.CreateButton("Delete Selected", 0, buttonHeight, ButtonStyle.Default);
        _btnDeleteSelected.Margin = new Padding(0);
        _selectionButtons.Controls.Add(_btnRegenerateSelected);
        _selectionButtons.Controls.Add(_btnMarkSelectedReviewed);
        _selectionButtons.Controls.Add(_btnDeleteSelected);
        _selectionBar.Controls.Add(_lblSelection);
        _selectionBar.Controls.Add(_selectionButtons);
        // 1 px divider under the bar, full card width like the header's divider
        _selectionBar.Controls.Add(new Panel { Dock = DockStyle.Bottom, Height = LogicalToDeviceUnits(1), BackColor = Theme.Border, Margin = new Padding(0) });
        GridBarSlot.Controls.Add(_selectionBar);

        // Wire handlers only after every control exists
        _btnAll.Click += (s, e) => SetFilter(FactFilter.All);
        _btnNeedsReview.Click += (s, e) => SetFilter(FactFilter.NeedsReview);
        _notice.SizeChanged += (s, e) => QueueSizeNotice();
        _btnMarkReviewed.Click += BtnMarkReviewed_Click;
        Grid.CellEndEdit += Grid_CellEndEdit;
        _btnAddFact.Click += BtnAddFact_Click;
        _btnRegenerateFacts.Click += BtnRegenerateFacts_Click;
        DeleteButton.Click += DeleteButton_Click;
        RegenerateButton.Click += RegenerateButton_Click;
        _btnRegenerateSelected.Click += BtnRegenerateSelected_Click;
        _btnMarkSelectedReviewed.Click += BtnMarkSelectedReviewed_Click;
        _btnDeleteSelected.Click += BtnDeleteSelected_Click;
        // The bar's width decides whether it fits on one line
        _selectionBar.SizeChanged += (s, e) => QueueLayoutSelectionBar();
    }

    public override IReadOnlyList<Control> TabActions => _tabActions;

    // ----- Static text and filter helpers (unit-tested) -----

    /// <summary>
    /// Grid card count line: "46 facts", "46 facts · 3 need review", "1 fact · 1 needs review".
    /// </summary>
    public static string CountText(int facts, int needReview)
    {
        var text = CaseGuideUiText.Plural(facts, "fact", "facts");
        return needReview > 0
            ? $"{text} · {needReview.ToString("N0", CultureInfo.InvariantCulture)} {(needReview == 1 ? "needs" : "need")} review"
            : text;
    }

    /// <summary>
    /// Detail meta line, e.g. "F-012 · March 3, 2024 · Hot" (an unparseable date is shown as typed).
    /// </summary>
    public static string MetaLine(KeyFact f)
    {
        var date = CaseGuideDocxExporter.LongDate(f.Date);
        var parts = new List<string> { f.Id };
        if (!string.IsNullOrWhiteSpace(date))
            parts.Add(date);
        parts.Add(SignificanceText(f.Significance));
        if (f.IsUserEdited)
            parts.Add("Edited by you");
        return string.Join(" · ", parts);
    }

    public static string NoticeText(KeyFact f) => $"Review this fact: {f.ReviewReason}";

    public static string IssuesCell(KeyFact f) => string.Join(", ", f.IssueIds);

    public static string NamesCell(CaseGuide g, IEnumerable<string> ids) =>
        string.Join(", ", ids.Select(id => NameOf(g, id)));

    public static bool PassesFilter(KeyFact f, FactFilter filter) =>
        filter != FactFilter.NeedsReview || f.NeedsReview;

    /// <summary>
    /// The selection bar shows for 2+ selected facts: "{n} facts selected", Regenerate Selected and Delete Selected
    /// (idle only), and Mark Reviewed only when at least one selected fact needs review.
    /// </summary>
    public static FactSelectionState SelectionState(IReadOnlyCollection<KeyFact> selected, bool busy)
    {
        var count = selected?.Count ?? 0;
        if (count < 2)
            return new FactSelectionState();

        return new FactSelectionState
        {
            Visible = true,
            Text = CaseGuideUiText.SelectedFactsText(count),
            CanRegenerate = !busy,
            CanMarkReviewed = !busy && selected!.Any(f => f.NeedsReview),
            CanDelete = !busy
        };
    }

    private static string SignificanceText(FactSignificance s) => s.ToString();

    private static string NameOf(CaseGuide g, string id)
    {
        var name = g.FindCast(id)?.FullName;
        return string.IsNullOrWhiteSpace(name) ? id : name;
    }

    // ----- Grid -----

    // Widths in logical px (UI-SPEC): no ID column on Key Facts, the date is the key
    protected override void BuildColumns()
    {
        // One header line on every tab, so the grids top-align when switching tabs
        Grid.ColumnHeadersDefaultCellStyle.WrapMode = DataGridViewTriState.False;
        GridStyle.AddDotColumn(Grid, this);
        Grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Date", HeaderText = "Date", Width = LogicalToDeviceUnits(96) });
        Grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Fact",
            HeaderText = "Fact",
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
            FillWeight = 45,
            MinimumWidth = LogicalToDeviceUnits(160)
        });
        Grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Issues", HeaderText = "Issues", Width = LogicalToDeviceUnits(80), ReadOnly = true });
        Grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Actors",
            HeaderText = "Who did it",
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
            FillWeight = 25,
            MinimumWidth = LogicalToDeviceUnits(112),
            ReadOnly = true
        });
        // Int cell values sort numerically; right-aligned with the card's 15 px right inset
        var knew = new DataGridViewTextBoxColumn { Name = "Knew", HeaderText = "Who knew", Width = LogicalToDeviceUnits(100), ReadOnly = true };
        knew.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
        knew.DefaultCellStyle.Padding = new Padding(0, 0, LogicalToDeviceUnits(15), 0);
        knew.HeaderCell.Style.Alignment = DataGridViewContentAlignment.MiddleRight;
        knew.HeaderCell.Style.Padding = new Padding(0, LogicalToDeviceUnits(6), LogicalToDeviceUnits(15) - GridStyle.HeaderTextMargin, LogicalToDeviceUnits(6));
        Grid.Columns.Add(knew);
    }

    private CaseGuide? Guide => IsAttached ? Host.Draft.Guide : null;

    private KeyFact? Fact(string? id) => Guide?.FindFact(id ?? string.Empty);

    protected override string EmptyRowsText =>
        _filter == FactFilter.NeedsReview && Guide?.Facts.Count > 0
            ? CaseGuideUiText.NoFilterMatchText(GuidePart.Facts)
            : base.EmptyRowsText;

    protected override void RefreshHeader()
    {
        var facts = Guide?.Facts ?? new List<KeyFact>();
        CountLabel.Text = CountText(facts.Count, facts.Count(f => f.NeedsReview));
    }

    protected override IEnumerable<(string Id, object?[] Cells)> BuildRows()
    {
        var guide = Guide;
        if (guide == null)
            yield break;

        // Chronological (yyyy-MM-dd text sorts by date); facts without a date last
        var ordered = guide.Facts
            .Where(f => PassesFilter(f, _filter))
            .OrderBy(f => string.IsNullOrEmpty(f.Date) ? 1 : 0)
            .ThenBy(f => f.Date, StringComparer.Ordinal)
            .ThenBy(f => f.Id, StringComparer.Ordinal);
        foreach (var fact in ordered)
            yield return (fact.Id, Cells(guide, fact));
    }

    private static object?[] Cells(CaseGuide guide, KeyFact f) =>
        new object?[] { f.Date, f.Title, IssuesCell(f), NamesCell(guide, f.ActorIds), f.KnownByIds.Count };

    protected override void ApplyEditableState()
    {
        base.ApplyEditableState();
        UpdateTooltips();
        if (Grid.ReadOnly)
            return;

        // Only Date and Fact edit in the grid; everything else edits in the detail card
        foreach (DataGridViewColumn column in Grid.Columns)
            column.ReadOnly = column.Name != "Date" && column.Name != "Fact";
    }

    private void UpdateTooltips()
    {
        var guide = Guide;
        if (guide == null)
            return;

        foreach (DataGridViewRow row in Grid.Rows)
        {
            var fact = guide.FindFact(row.Tag as string ?? string.Empty);
            if (fact != null)
                SetTooltips(guide, row, fact);
        }
    }

    private void SetTooltips(CaseGuide guide, DataGridViewRow row, KeyFact fact)
    {
        var issues = Grid.Columns["Issues"];
        var knew = Grid.Columns["Knew"];
        if (issues != null)
        {
            row.Cells[issues.Index].ToolTipText = string.Join(Environment.NewLine,
                fact.IssueIds.Select(id => guide.FindIssue(id) is { } issue ? $"{issue.Id}  {issue.Title}" : id));
        }
        if (knew != null)
            row.Cells[knew.Index].ToolTipText = string.Join(Environment.NewLine, fact.KnownByIds.Select(id => NameOf(guide, id)));
    }

    protected override Color DotColor(string id)
    {
        var color = base.DotColor(id);
        // Writing (Accent) and errors (Danger) win; otherwise a fact to review is Warning
        if (color == Theme.Accent || color == Theme.Danger)
            return color;
        return Fact(id)?.NeedsReview == true ? Theme.Warning : color;
    }

    private void SetFilter(FactFilter filter)
    {
        if (_filter == filter)
            return;

        CommitPendingEdits();
        ApplyFilter(filter);
        RefreshFromDraft();
    }

    private void ApplyFilter(FactFilter filter)
    {
        _filter = filter;
        _btnAll.Selected = filter == FactFilter.All;
        _btnNeedsReview.Selected = filter == FactFilter.NeedsReview;
    }

    public override bool SelectEntity(string id)
    {
        // Clear a filter that hides the fact first
        var fact = Fact(id);
        if (fact != null && !PassesFilter(fact, _filter))
        {
            ApplyFilter(FactFilter.All);
            RefreshFromDraft();
        }
        return base.SelectEntity(id);
    }

    /// <summary>
    /// Review Facts (needs-review banner): filters to the facts to review and selects the first one.
    /// </summary>
    public override void ShowNeedsReview()
    {
        CommitPendingEdits();
        ApplyFilter(FactFilter.NeedsReview);
        RefreshFromDraft();
        if (Grid.Rows.Count > 0 && Grid.Rows[0].Tag is string id)
            base.SelectEntity(id);
    }

    // ----- Selection bar -----

    private List<KeyFact> SelectedFacts() =>
        SelectedIds.Select(id => Fact(id)).OfType<KeyFact>().ToList();

    protected override void OnSelectionChanged()
    {
        if (Rebuilding)
            return;
        UpdateSelectionBar();
    }

    public override void SetBusy(bool busy)
    {
        base.SetBusy(busy);
        UpdateSelectionBar();
    }

    private void UpdateSelectionBar()
    {
        if (IsDisposed)
            return;

        var state = SelectionState(IsAttached ? SelectedFacts() : new List<KeyFact>(), Busy || (IsAttached && Host.IsBusy));
        // A longer count ("9" to "10 facts selected") can push the buttons to a second line
        var relayout = _lblSelection.Text != state.Text || GridBarSlot.Visible != state.Visible;
        if (_lblSelection.Text != state.Text)
            _lblSelection.Text = state.Text;
        _btnRegenerateSelected.Enabled = state.CanRegenerate;
        _btnMarkSelectedReviewed.Enabled = state.CanMarkReviewed;
        _btnDeleteSelected.Enabled = state.CanDelete;
        if (GridBarSlot.Visible != state.Visible)
            GridBarSlot.Visible = state.Visible;
        if (relayout)
            LayoutSelectionBar();
    }

    private bool _layoutSelectionBarQueued;

    // Width changes arrive inside a layout pass: decide one line or two afterwards
    private void QueueLayoutSelectionBar()
    {
        if (!IsHandleCreated || IsDisposed)
        {
            LayoutSelectionBar();
            return;
        }
        if (_layoutSelectionBarQueued)
            return;

        _layoutSelectionBarQueued = true;
        BeginInvoke(new Action(() =>
        {
            _layoutSelectionBarQueued = false;
            if (!IsDisposed)
                LayoutSelectionBar();
        }));
    }

    /// <summary>
    /// One line (count left, buttons right, 8 above and below the 34 px buttons) when it fits the grid card; otherwise the
    /// buttons go on a second line under the count, both at Content x (they fit the grid card at the minimum window size).
    /// </summary>
    private void LayoutSelectionBar()
    {
        if (!GridBarSlot.Visible)
            return;

        var inset = LogicalToDeviceUnits(15);
        var pad = LogicalToDeviceUnits(Theme.SmallGap);
        var divider = LogicalToDeviceUnits(1);
        // Before the first layout the bar has no width yet: measure against the grid host, which the bar spans
        var width = _selectionBar.Width > 1 ? _selectionBar.Width : (Grid.Parent ?? Grid).Width;
        var label = _lblSelection.PreferredSize;
        var buttons = _selectionButtons.GetPreferredSize(Size.Empty);
        var wrap = inset + label.Width + LogicalToDeviceUnits(Theme.Gap) + buttons.Width + inset > width;

        int height;
        if (wrap)
        {
            _lblSelection.Location = new Point(inset, pad);
            _selectionButtons.Location = new Point(inset, pad + label.Height + pad);
            height = pad + label.Height + pad + buttons.Height + pad + divider;
        }
        else
        {
            _lblSelection.Location = new Point(inset, pad + (buttons.Height - label.Height) / 2);
            _selectionButtons.Location = new Point(Math.Max(inset, width - inset - buttons.Width), pad);
            height = pad + buttons.Height + pad + divider;
        }

        if (_selectionBar.Height != height)
        {
            _selectionBar.Height = height;
            // The slot is AutoSize: the grid card must lay out again to give it the new height
            (GridBarSlot.Parent ?? GridBarSlot).PerformLayout();
        }
    }

    protected override void OnDeleteKey()
    {
        // Two or more selected rows: Delete removes the selection (asks first), like Delete Selected
        if (SelectedIds.Count >= 2)
        {
            if (_btnDeleteSelected.Enabled)
                _btnDeleteSelected.PerformClick();
            return;
        }

        base.OnDeleteKey();
    }

    /// <summary>
    /// Applies typed cells and detail fields before a bulk action reads the selection.
    /// </summary>
    private List<string> CommitAndGetSelection()
    {
        CommitGridEdits();
        CommitPendingEdits();
        return SelectedIds.Where(id => Fact(id) != null).ToList();
    }

    private async void BtnRegenerateSelected_Click(object? sender, EventArgs e)
    {
        if (!IsAttached || Host.IsBusy)
            return;

        var ids = CommitAndGetSelection();
        if (ids.Count < 2)
            return;

        GuideEntriesResult? result = null;
        try
        {
            result = await Host.RegenerateEntriesAsync(GuidePart.Facts, ids);
        }
        catch (Exception ex)
        {
            // The host reports its own failures; this only catches a fault before it could
            if (!IsDisposed)
                Host.SetStatus(ex.InnerException != null ? $"Error: {ex.Message} ({ex.InnerException.Message})" : $"Error: {ex.Message}", Theme.Danger);
        }

        if (IsDisposed)
            return;

        // Rewritten facts may change any field or their notes, so build the card again
        _builtId = null;
        RefreshFromDraft();
        // The facts that failed are selected again, ready for another try
        if (result is { Failed.Count: > 0 })
            SelectRows(result.Failed.Select(f => f.Id));
    }

    private void BtnMarkSelectedReviewed_Click(object? sender, EventArgs e)
    {
        if (!IsAttached || Host.IsBusy)
            return;

        var ids = CommitAndGetSelection();
        if (ids.Count == 0)
            return;

        // Under the Needs review filter the reviewed facts leave the list: move on to the next one
        var nextId = _filter == FactFilter.NeedsReview ? NeighborId(ids) : null;
        var marked = CaseGuideEditing.MarkFactsReviewed(Host.Draft, ids);
        if (marked == 0)
            return;

        Host.NotifyEdited(GuidePart.Facts);
        RefreshFromDraft();
        if (nextId != null)
            SelectEntity(nextId);
        Host.SetStatus(CaseGuideUiText.MarkedReviewedText(marked), Theme.Success);
    }

    private void BtnDeleteSelected_Click(object? sender, EventArgs e)
    {
        if (!IsAttached || Host.IsBusy)
            return;

        var ids = CommitAndGetSelection();
        if (ids.Count < 2)
            return;

        // Worded like C5 and defaults to No (T-03-49)
        var (title, text) = CaseGuideUiText.C14DeleteFacts(ids.Count, CaseGuideEditing.ValuesReferencingAny(Host.Draft, ids));
        var answer = MessageBox.Show(FindForm(), text, title, MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2);
        if (answer != DialogResult.Yes || IsDisposed)
            return;

        var nextId = NeighborId(ids);
        // Same side effects as Delete Fact for each one: values citing it are flagged and lose the link
        var deleted = ids.Count(id => CaseGuideEditing.DeleteFact(Host.Draft, id));
        if (deleted == 0)
            return;

        Host.NotifyEdited(GuidePart.Facts);
        RefreshFromDraft();
        if (nextId != null && SelectEntity(nextId))
            Grid.Focus();
        Host.SetStatus(CaseGuideUiText.DeletedFactsText(deleted), Theme.Success);
    }

    private void Grid_CellEndEdit(object? sender, DataGridViewCellEventArgs e)
    {
        if (e.RowIndex < 0 || e.RowIndex >= Grid.Rows.Count || !IsAttached)
            return;

        var row = Grid.Rows[e.RowIndex];
        var column = Grid.Columns[e.ColumnIndex];
        if (row.Tag is not string id || (column.Name != "Date" && column.Name != "Fact"))
            return;

        var text = Convert.ToString(row.Cells[e.ColumnIndex].Value, CultureInfo.InvariantCulture) ?? string.Empty;
        var field = column.Name == "Date" ? FactField.Date : FactField.Title;
        // Applying inside CellEndEdit would rebuild the grid reentrantly (T-03-46)
        QueueGridEdit(() => CommitGridEdit(id, field, text));
    }

    private void CommitGridEdit(string id, FactField field, string text)
    {
        if (IsDisposed || !IsAttached || Host.IsBusy)
            return;

        var fact = Fact(id);
        if (fact == null)
            return;

        var result = CaseGuideEditing.SetFactField(Host.Draft, id, field, text);
        if (!result.Succeeded)
        {
            // The edit is cancelled: the cell shows the stored value again
            UpdateRow(id);
            Host.SetStatus(result.Error, Theme.Danger);
            return;
        }

        UpdateRow(id);
        if (!result.Changed)
            return;

        if (_builtId == id)
            UpdateDetailValues(fact);
        Host.NotifyEdited(GuidePart.Facts);
        // A new date can move the fact in the timeline
        if (field == FactField.Date)
            RequestRefresh();
    }

    // ----- Detail -----

    protected override void ShowDetail(string? id)
    {
        var fact = Fact(id);
        if (fact == null)
        {
            ClearDetail();
            DetailHeading.Text = string.Empty;
            DetailMeta.Text = string.Empty;
            return;
        }

        if (_builtId == fact.Id && Fields.Controls.Count > 0 && _notesShown == NotesText(fact))
        {
            // Same fact: update in place so the card does not flicker or lose its scroll position
            UpdateDetailValues(fact);
            return;
        }

        BuildDetail(fact);
    }

    private void ClearDetail()
    {
        ClearFields();
        _textFields.Clear();
        _fieldErrors.Clear();
        _pickerState.Clear();
        _builtId = null;
        _txtDate = _txtTitle = _txtDescription = null;
        _cmbSignificance = null;
        _pickIssues = _pickActors = _pickKnowers = null;
        _lblNotes = null;
        _notesShown = string.Empty;
        NoticeSlot.Visible = false;
    }

    private void BuildDetail(KeyFact fact)
    {
        Fields.SuspendLayout();
        ClearDetail();

        _txtDate = new TextBox { PlaceholderText = DatePlaceholder };
        (_, _cmbSignificance) = AddPairedTextAndCombo("Date", _txtDate, "Significance", Significances.Select(SignificanceText));
        _txtTitle = new TextBox();
        AddTextField("Fact", _txtTitle);
        _txtDescription = new TextBox();
        AddTextField("What happened", _txtDescription, 5);
        _pickIssues = new LinkPicker("Filter issues");
        AddField("Issues", _pickIssues);
        _pickActors = new LinkPicker("Filter characters");
        AddField("Who did it", _pickActors);
        _pickKnowers = new LinkPicker("Filter characters");
        AddField("Who knew", _pickKnowers);
        _notesShown = NotesText(fact);
        if (_notesShown.Length > 0)
        {
            _lblNotes = AddCaptionedLabel("Notes from generation", _notesShown);
            _lblNotes.Font = _noteFont;
            _lblNotes.ForeColor = Theme.TextMuted;
        }

        _textFields[_txtDate] = FactField.Date;
        _textFields[_txtTitle] = FactField.Title;
        _textFields[_txtDescription] = FactField.Description;

        _builtId = fact.Id;
        UpdateDetailValues(fact);
        Fields.ResumeLayout(true);

        // Wire handlers only after every field exists and holds its value
        foreach (var box in _textFields.Keys)
            box.Leave += TextField_Leave;
        _cmbSignificance.SelectionChangeCommitted += Combo_SelectionChangeCommitted;
        _pickIssues.CheckedChanged += Picker_CheckedChanged;
        _pickActors.CheckedChanged += Picker_CheckedChanged;
        _pickKnowers.CheckedChanged += Picker_CheckedChanged;
    }

    private static string NotesText(KeyFact fact) =>
        string.Join(Environment.NewLine, fact.MappingNotes.Where(n => !string.IsNullOrWhiteSpace(n)).Select(n => n.Trim()));

    private void UpdateDetailValues(KeyFact fact)
    {
        var guide = Guide;
        if (guide == null || _builtId != fact.Id)
            return;

        DetailHeading.Text = fact.Title;
        DetailMeta.Text = MetaLine(fact);
        UpdateNotice(fact);

        foreach (var (box, field) in _textFields)
        {
            var value = ValueOf(fact, field);
            // Never overwrite text the user is typing or an invalid value that is still on screen
            if (box.Focused && box.Text.Trim() != value)
                continue;
            if (_fieldErrors.ContainsKey(box))
                continue;
            if (box.Text != value)
                box.Text = value;
        }

        if (_cmbSignificance != null)
            _cmbSignificance.SelectedIndex = Array.IndexOf(Significances, fact.Significance);

        var castItems = CastItems(guide);
        SyncPicker(_pickIssues, IssueItems(guide), fact.IssueIds, null);
        SyncPicker(_pickActors, castItems, fact.ActorIds, null);
        // The people who did it always knew: listed first, checked and locked (T-03-48)
        SyncPicker(_pickKnowers, castItems, fact.KnownByIds, fact.ActorIds);
    }

    private void UpdateNotice(KeyFact fact)
    {
        if (fact.NeedsReview)
        {
            _lblNotice.Text = NoticeText(fact);
            NoticeSlot.Visible = true;
            SizeNotice();
        }
        else
        {
            NoticeSlot.Visible = false;
        }
    }

    private bool _sizeNoticeQueued;

    /// <summary>
    /// Width changes arrive inside a layout pass, where a new height would not stick: size the notice afterwards.
    /// </summary>
    private void QueueSizeNotice()
    {
        if (!IsHandleCreated || IsDisposed)
        {
            SizeNotice();
            return;
        }
        if (_sizeNoticeQueued)
            return;

        _sizeNoticeQueued = true;
        BeginInvoke(new Action(() =>
        {
            _sizeNoticeQueued = false;
            if (!IsDisposed)
                SizeNotice();
        }));
    }

    private void SizeNotice()
    {
        // The text wraps beside the button; the card is as tall as the taller of the two plus its padding
        var inner = Math.Max(1, _notice.ClientSize.Width - _notice.Padding.Horizontal);
        var textWidth = Math.Max(1, inner - _btnMarkReviewed.Width - _btnMarkReviewed.Margin.Horizontal);
        if (_lblNotice.MaximumSize.Width != textWidth)
            _lblNotice.MaximumSize = new Size(textWidth, 0);
        var textHeight = _lblNotice.GetPreferredSize(new Size(textWidth, 0)).Height;
        var height = Math.Max(textHeight, _btnMarkReviewed.Height) + _notice.Padding.Vertical;
        if (_notice.Height != height)
        {
            _notice.Height = height;
            // The slot is AutoSize: its parent row must lay out again to give it the new height
            (NoticeSlot.Parent ?? NoticeSlot).PerformLayout();
        }
    }

    private static List<LinkItem> IssueItems(CaseGuide guide) =>
        guide.Issues.Select(i => new LinkItem { Id = i.Id, Text = i.Title }).ToList();

    private static List<LinkItem> CastItems(CaseGuide guide) =>
        guide.Cast.Select(m => new LinkItem { Id = m.Id, Text = CastItemText(guide, m) }).ToList();

    /// <summary>
    /// "Michael Scott — Regional Manager, Dunder Mifflin" (the picker adds the id in front).
    /// </summary>
    private static string CastItemText(CaseGuide guide, CastMember m)
    {
        var org = guide.FindOrganization(m.OrganizationId)?.Name ?? string.Empty;
        var role = string.Join(", ", new[] { m.Title, org }.Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s.Trim()));
        return role.Length > 0 ? $"{m.FullName} — {role}" : m.FullName;
    }

    /// <summary>
    /// Re-sets a picker's items only when its items, checks or locks changed, so the list keeps its scroll
    /// position and order while the user works in it.
    /// </summary>
    private void SyncPicker(LinkPicker? picker, List<LinkItem> items, List<string> checkedIds, List<string>? lockedIds)
    {
        if (picker == null)
            return;

        var state = PickerState(items, checkedIds, lockedIds);
        if (_pickerState.TryGetValue(picker, out var shown) && shown == state)
            return;

        _pickerState[picker] = state;
        picker.SetItems(items, checkedIds, lockedIds ?? Enumerable.Empty<string>(), DidItSuffix);
    }

    private static string PickerState(List<LinkItem> items, List<string> checkedIds, List<string>? lockedIds) =>
        string.Join("\u001f", items.Select(i => i.Id + "\u001e" + i.Text)) + "\u001d" +
        string.Join("\u001f", checkedIds) + "\u001d" + string.Join("\u001f", lockedIds ?? new List<string>());

    private static string ValueOf(KeyFact f, FactField field) => field switch
    {
        FactField.Date => f.Date,
        FactField.Title => f.Title,
        FactField.Description => f.Description,
        _ => string.Empty
    };

    // ----- Commits -----

    private void TextField_Leave(object? sender, EventArgs e)
    {
        if (sender is TextBox box)
            CommitText(box);
    }

    private void Combo_SelectionChangeCommitted(object? sender, EventArgs e)
    {
        if (sender is not ComboBox combo)
            return;

        var index = combo.SelectedIndex;
        Defer(() => CommitSignificance(combo, index));
    }

    /// <summary>
    /// Applies every detail field whose text differs from the stored value (selection change, step leave).
    /// </summary>
    public override void CommitPendingEdits()
    {
        if (!IsAttached || Busy || _builtId == null)
            return;

        foreach (var box in _textFields.Keys.ToList())
        {
            if (!box.IsDisposed)
                CommitText(box);
        }
    }

    private void CommitText(TextBox box)
    {
        if (!IsAttached || Busy || _builtId == null || !_textFields.TryGetValue(box, out var field))
            return;

        var id = _builtId;
        var fact = Fact(id);
        if (fact == null)
            return;

        if (box.Text.Trim() == ValueOf(fact, field))
        {
            // Typed back to the stored value: drop that field's error
            if (_fieldErrors.Remove(box))
                ReshowFieldErrors();
            return;
        }

        var result = CaseGuideEditing.SetFactField(Host.Draft, id, field, box.Text);
        if (!result.Succeeded)
        {
            // Invalid input is shown with its message and never stored
            _fieldErrors[box] = result.Error;
            ReshowFieldErrors();
            return;
        }

        if (!result.Changed)
            return;

        _fieldErrors.Clear();
        ClearFieldErrors();
        AfterChange(fact);
        // A new date can move the fact in the timeline: rebuild after the focus change completes
        if (field == FactField.Date)
            RequestRefresh();
    }

    private void CommitSignificance(ComboBox combo, int index)
    {
        if (IsDisposed || combo.IsDisposed || !IsAttached || Busy || _builtId == null || index < 0 || index >= Significances.Length)
            return;

        var fact = Fact(_builtId);
        if (fact == null)
            return;

        var result = CaseGuideEditing.SetFactField(Host.Draft, fact.Id, FactField.Significance, Significances[index].ToString());
        if (!result.Succeeded)
        {
            _fieldErrors[combo] = result.Error;
            ReshowFieldErrors();
            return;
        }

        if (result.Changed)
            AfterChange(fact);
    }

    private void Picker_CheckedChanged(object? sender, EventArgs e)
    {
        if (sender is not LinkPicker picker || picker.IsDisposed || !IsAttached || Busy || _builtId == null)
            return;

        var fact = Fact(_builtId);
        if (fact == null)
            return;

        var kind = picker == _pickIssues ? FactLinkKind.Issues : picker == _pickActors ? FactLinkKind.Actors : FactLinkKind.KnownBy;
        // Unknown ids are dropped and actors are kept in Who knew by CaseGuideEditing (T-03-47, T-03-48)
        var result = CaseGuideEditing.SetFactLinks(Host.Draft, fact.Id, kind, picker.CheckedIds);
        if (!result.Changed)
            return;

        // The picker the user clicked already shows the new checks: record them so it is not re-set (keeps its scroll)
        var guide = Guide!;
        if (kind == FactLinkKind.Issues)
            _pickerState[picker] = PickerState(IssueItems(guide), fact.IssueIds, null);
        else if (kind == FactLinkKind.Actors)
            _pickerState[picker] = PickerState(CastItems(guide), fact.ActorIds, null);
        else
            _pickerState[picker] = PickerState(CastItems(guide), fact.KnownByIds, fact.ActorIds);

        // After an Actors change, Who knew is re-set so the new actors appear first and locked
        AfterChange(fact);
    }

    private void ReshowFieldErrors()
    {
        ClearFieldErrors();
        foreach (var (field, message) in _fieldErrors)
            ShowFieldError(field, message);
    }

    /// <summary>
    /// Saves and revalidates through the host, then refreshes the row and the card in place.
    /// </summary>
    private void AfterChange(KeyFact fact)
    {
        Host.NotifyEdited(GuidePart.Facts);
        UpdateRow(fact.Id);
        UpdateDetailValues(fact);
    }

    /// <summary>
    /// Refreshes one row's cells in place; a change to the sorted column re-sorts with a full (deferred) rebuild.
    /// </summary>
    private void UpdateRow(string id)
    {
        var guide = Guide;
        var fact = Fact(id);
        if (guide == null || fact == null)
            return;

        var row = Grid.Rows.Cast<DataGridViewRow>().FirstOrDefault(r => r.Tag as string == id);
        if (row == null)
            return;

        var cells = Cells(guide, fact);
        var offset = Grid.Columns[GridStyle.DotColumnName] != null ? 1 : 0;
        var sorted = Grid.SortedColumn;
        var resort = false;
        for (var i = 0; i < cells.Length; i++)
        {
            var cell = row.Cells[i + offset];
            if (Equals(cell.Value, cells[i]))
                continue;
            if (sorted != null && sorted.Index == i + offset)
                resort = true;
            cell.Value = cells[i];
        }

        SetTooltips(guide, row, fact);
        Grid.InvalidateRow(row.Index);
        RefreshHeader();

        if (resort)
            RequestRefresh();
    }

    // ----- Actions -----

    private void BtnAddFact_Click(object? sender, EventArgs e)
    {
        if (!IsAttached || Host.IsBusy)
            return;

        CommitPendingEdits();
        var fact = CaseGuideEditing.AddFact(Host.Draft);
        Host.NotifyEdited(GuidePart.Facts);
        // A new fact does not need review: the Needs review filter would hide it
        ApplyFilter(FactFilter.All);
        RefreshFromDraft();
        if (SelectEntity(fact.Id) && _txtTitle != null)
        {
            _txtTitle.Focus();
            _txtTitle.SelectAll();
        }
    }

    private void DeleteButton_Click(object? sender, EventArgs e)
    {
        var id = SelectedId;
        var fact = Fact(id);
        if (!IsAttached || Host.IsBusy || id == null || fact == null)
            return;

        // C5 defaults to No (T-03-49)
        var (title, text) = CaseGuideUiText.C5DeleteFact(id, fact.Title, CaseGuideEditing.ValuesReferencing(Host.Draft, id));
        var answer = MessageBox.Show(FindForm(), text, title, MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2);
        if (answer != DialogResult.Yes || IsDisposed)
            return;

        var nextId = NeighborId(new[] { id });
        if (!CaseGuideEditing.DeleteFact(Host.Draft, id))
            return;

        Host.NotifyEdited(GuidePart.Facts);
        RefreshFromDraft();
        if (nextId != null && SelectEntity(nextId))
            Grid.Focus();
    }

    private async void RegenerateButton_Click(object? sender, EventArgs e)
    {
        var id = SelectedId;
        var fact = Fact(id);
        if (!IsAttached || Host.IsBusy || id == null || fact == null)
            return;

        CommitPendingEdits();
        var wasEdited = fact.IsUserEdited;
        if (wasEdited)
        {
            // C3 defaults to No (T-03-49)
            var (title, text) = CaseGuideUiText.C3RegenerateEdited("Fact", fact.Title);
            var answer = MessageBox.Show(FindForm(), text, title, MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2);
            if (answer != DialogResult.Yes || IsDisposed)
                return;
        }

        var before = Snapshot(fact);
        try
        {
            await Host.RegenerateEntryAsync(GuidePart.Facts, id, GuidanceBox.Text.Trim());
        }
        catch (Exception ex)
        {
            // The host reports its own failures; this only catches a fault before it could
            if (!IsDisposed)
                Host.SetStatus(ex.InnerException != null ? $"Error: {ex.Message} ({ex.InnerException.Message})" : $"Error: {ex.Message}", Theme.Danger);
        }

        if (IsDisposed)
            return;

        // Success clears the edited flag and rewrites the entry (host contract); failure keeps the old text and the guidance
        var after = Fact(id);
        if (after != null && !after.IsUserEdited && (wasEdited || Snapshot(after) != before))
            GuidanceBox.Clear();

        // The rewritten entry may change any field or its notes, so build the card again
        _builtId = null;
        RefreshFromDraft();
    }

    private static string Snapshot(KeyFact f) =>
        string.Join("\u001f", f.Date, f.Title, f.Description, f.Significance, string.Join(",", f.IssueIds),
            string.Join(",", f.ActorIds), string.Join(",", f.KnownByIds), string.Join("\u001e", f.MappingNotes));

    private async void BtnRegenerateFacts_Click(object? sender, EventArgs e)
    {
        if (!IsAttached || Host.IsBusy)
            return;

        CommitPendingEdits();
        try
        {
            await Host.RegenerateTabAsync(GuidePart.Facts);
        }
        catch (Exception ex)
        {
            // The host reports its own failures; this only catches a fault before it could
            if (!IsDisposed)
                Host.SetStatus(ex.InnerException != null ? $"Error: {ex.Message} ({ex.InnerException.Message})" : $"Error: {ex.Message}", Theme.Danger);
        }

        if (IsDisposed)
            return;

        _builtId = null;
        RefreshFromDraft();
    }

    private void BtnMarkReviewed_Click(object? sender, EventArgs e)
    {
        var id = _builtId;
        if (!IsAttached || Host.IsBusy || id == null)
            return;

        CommitPendingEdits();
        // Under the Needs review filter the reviewed fact leaves the list: move on to the next one
        var nextId = _filter == FactFilter.NeedsReview ? NeighborId(new[] { id }) : null;
        if (!CaseGuideEditing.MarkReviewed(Host.Draft, id))
            return;

        Host.NotifyEdited(GuidePart.Facts);
        RefreshFromDraft();
        if (nextId != null)
            SelectEntity(nextId);
    }

    /// <summary>
    /// The first row after the last of the given ones that is not among them (or the nearest one before it), so the selection
    /// stays in place after those rows leave.
    /// </summary>
    private string? NeighborId(IReadOnlyCollection<string> ids)
    {
        var rows = Grid.Rows.Cast<DataGridViewRow>().Select(r => r.Tag as string).ToList();
        var leaving = ids.ToHashSet(StringComparer.Ordinal);
        var last = rows.FindLastIndex(id => id != null && leaving.Contains(id));
        if (last < 0)
            return null;

        for (var i = last + 1; i < rows.Count; i++)
        {
            if (rows[i] is { } after && !leaving.Contains(after))
                return after;
        }
        for (var i = last - 1; i >= 0; i--)
        {
            if (rows[i] is { } before && !leaving.Contains(before))
                return before;
        }
        return null;
    }

    private void Defer(Action action)
    {
        if (IsHandleCreated && !IsDisposed)
            BeginInvoke(action);
        else
            action();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            _noteFont.Dispose();
        base.Dispose(disposing);
    }
}
