using System.Globalization;
using ReelDiscovery.Helpers;
using ReelDiscovery.Models.Case;
using ReelDiscovery.Services.Case;

namespace ReelDiscovery.UserControls;

/// <summary>
/// Canonical Values tab (GUIDE-06, GUIDE-08): the registry of amounts, numbers and names every later document
/// and email must use verbatim, as a sortable grid and a detail card. Label and Value edit in the grid; Kind,
/// Exact value, Description and Used in facts edit in the detail card. Every change goes through CaseGuideEditing.
/// </summary>
public class ValuesTabPanel : GuideTabPanel
{
    private const string ExactValueHint = "Every document and email must use this exact text.";

    // UI-SPEC order of the Kind drop-down
    private static readonly (ValueKind Kind, string Text)[] Kinds =
    {
        (ValueKind.Amount, "Amount"),
        (ValueKind.InvoiceNumber, "Invoice number"),
        (ValueKind.ChangeOrderNumber, "Change order number"),
        (ValueKind.ContractNumber, "Contract number"),
        (ValueKind.PurchaseOrder, "Purchase order"),
        (ValueKind.DocumentTitle, "Document title"),
        (ValueKind.ProjectName, "Project name"),
        (ValueKind.AccountNumber, "Account number"),
        (ValueKind.Address, "Address"),
        (ValueKind.Other, "Other")
    };

    private readonly CardPanel _notice;
    private readonly Label _lblNotice;
    private readonly ThemedButton _btnMarkReviewed;
    private readonly ThemedButton _btnAddValue;
    private readonly ThemedButton _btnRegenerateValues;
    private readonly Font _noteFont;
    private readonly Control[] _tabActions;
    private readonly Dictionary<TextBox, ValueField> _textFields = new();
    private readonly Dictionary<Control, string> _fieldErrors = new();
    private string _pickerState = string.Empty;

    // Detail fields of the shown value; recreated only when the value changes
    private string? _builtId;
    private ComboBox? _cmbKind;
    private TextBox? _txtLabel;
    private TextBox? _txtValue;
    private TextBox? _txtDescription;
    private LinkPicker? _pickFacts;
    private Label? _lblNotes;
    private string _notesShown = string.Empty;

    public ValuesTabPanel()
        : base(GuidePart.Values, "Canonical values", "Value", hasToolbar: true,
            guidancePlaceholder: "Optional guidance for Regenerate Value, e.g. use a round amount")
    {
        // Inline needs-review notice (W3): wrapping text | Mark Reviewed
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
        _btnAddValue = (ThemedButton)ButtonHelper.CreateButton("Add Value", 0, buttonHeight, ButtonStyle.Default);
        _btnAddValue.Margin = new Padding(0);
        _btnRegenerateValues = (ThemedButton)ButtonHelper.CreateButton("Regenerate Values...", 0, buttonHeight, ButtonStyle.Default);
        _btnRegenerateValues.Margin = new Padding(0);
        _tabActions = new Control[] { _btnAddValue, _btnRegenerateValues };

        // Wire handlers only after every control exists
        _notice.SizeChanged += (s, e) => QueueSizeNotice();
        _btnMarkReviewed.Click += BtnMarkReviewed_Click;
        Grid.CellEndEdit += Grid_CellEndEdit;
        _btnAddValue.Click += BtnAddValue_Click;
        _btnRegenerateValues.Click += BtnRegenerateValues_Click;
        DeleteButton.Click += DeleteButton_Click;
        RegenerateButton.Click += RegenerateButton_Click;
    }

    public override IReadOnlyList<Control> TabActions => _tabActions;

    // ----- Static text helpers (unit-tested) -----

    /// <summary>
    /// Display text of a kind: "Invoice number", "Change order number", ...
    /// </summary>
    public static string KindText(ValueKind kind)
    {
        foreach (var (k, text) in Kinds)
        {
            if (k == kind)
                return text;
        }
        return "Other";
    }

    /// <summary>
    /// The kind for a display text; anything that is not one of the ten texts is Other.
    /// </summary>
    public static ValueKind KindFromText(string text)
    {
        foreach (var (kind, kindText) in Kinds)
        {
            if (string.Equals(kindText, text?.Trim(), StringComparison.OrdinalIgnoreCase))
                return kind;
        }
        return ValueKind.Other;
    }

    /// <summary>
    /// Detail meta line, e.g. "V-004 · Invoice number" (+ " · Edited by you").
    /// </summary>
    public static string MetaLine(CanonicalValue v)
    {
        var parts = new List<string> { v.Id, KindText(v.Kind) };
        if (v.IsUserEdited)
            parts.Add("Edited by you");
        return string.Join(" · ", parts);
    }

    public static string NoticeText(CanonicalValue v) => $"Review this value: {v.ReviewReason}";

    public static string CountText(int values) => CaseGuideUiText.Plural(values, "value", "values");

    // ----- Grid -----

    // Widths in logical px (UI-SPEC)
    protected override void BuildColumns()
    {
        // One header line on every tab, so the grids top-align when switching tabs
        Grid.ColumnHeadersDefaultCellStyle.WrapMode = DataGridViewTriState.False;
        GridStyle.AddDotColumn(Grid, this);
        Grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Id", HeaderText = "ID", Width = LogicalToDeviceUnits(64), ReadOnly = true });
        Grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Kind", HeaderText = "Kind", Width = LogicalToDeviceUnits(112), ReadOnly = true });
        Grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Label",
            HeaderText = "Label",
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
            FillWeight = 35,
            MinimumWidth = LogicalToDeviceUnits(140)
        });
        Grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Value",
            HeaderText = "Value",
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
            FillWeight = 30,
            MinimumWidth = LogicalToDeviceUnits(120)
        });
        // Int cell values sort numerically; right-aligned with the card's 15 px right inset
        var facts = new DataGridViewTextBoxColumn { Name = "Facts", HeaderText = "Facts", Width = LogicalToDeviceUnits(56), ReadOnly = true };
        facts.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
        facts.DefaultCellStyle.Padding = new Padding(0, 0, LogicalToDeviceUnits(15), 0);
        facts.HeaderCell.Style.Alignment = DataGridViewContentAlignment.MiddleRight;
        facts.HeaderCell.Style.Padding = new Padding(0, LogicalToDeviceUnits(6), LogicalToDeviceUnits(15) - GridStyle.HeaderTextMargin, LogicalToDeviceUnits(6));
        Grid.Columns.Add(facts);
    }

    private CaseGuide? Guide => IsAttached ? Host.Draft.Guide : null;

    private CanonicalValue? Value(string? id) => Guide?.FindValue(id ?? string.Empty);

    protected override void RefreshHeader()
    {
        CountLabel.Text = CountText(Guide?.Values.Count ?? 0);
    }

    protected override IEnumerable<(string Id, object?[] Cells)> BuildRows()
    {
        var guide = Guide;
        if (guide == null)
            yield break;

        foreach (var value in guide.Values.OrderBy(v => v.Id, StringComparer.Ordinal))
            yield return (value.Id, Cells(value));
    }

    private static object?[] Cells(CanonicalValue v) =>
        new object?[] { v.Id, KindText(v.Kind), v.Label, v.Value, v.FactIds.Count };

    protected override void ApplyEditableState()
    {
        base.ApplyEditableState();
        UpdateTooltips();
        if (Grid.ReadOnly)
            return;

        // Only Label and Value edit in the grid; everything else edits in the detail card
        foreach (DataGridViewColumn column in Grid.Columns)
            column.ReadOnly = column.Name != "Label" && column.Name != "Value";
    }

    private void UpdateTooltips()
    {
        var guide = Guide;
        if (guide == null)
            return;

        foreach (DataGridViewRow row in Grid.Rows)
        {
            var value = guide.FindValue(row.Tag as string ?? string.Empty);
            if (value != null)
                SetTooltip(guide, row, value);
        }
    }

    private void SetTooltip(CaseGuide guide, DataGridViewRow row, CanonicalValue value)
    {
        var facts = Grid.Columns["Facts"];
        if (facts == null)
            return;

        row.Cells[facts.Index].ToolTipText = string.Join(Environment.NewLine,
            value.FactIds.Select(id => guide.FindFact(id) is { } fact ? $"{fact.Id}  {fact.Title}" : id));
    }

    protected override Color DotColor(string id)
    {
        var color = base.DotColor(id);
        // Writing (Accent) and errors (Danger) win; otherwise a value to review is Warning
        if (color == Theme.Accent || color == Theme.Danger)
            return color;
        return Value(id)?.NeedsReview == true ? Theme.Warning : color;
    }

    private void Grid_CellEndEdit(object? sender, DataGridViewCellEventArgs e)
    {
        if (e.RowIndex < 0 || e.RowIndex >= Grid.Rows.Count || !IsAttached)
            return;

        var row = Grid.Rows[e.RowIndex];
        var column = Grid.Columns[e.ColumnIndex];
        if (row.Tag is not string id || (column.Name != "Label" && column.Name != "Value"))
            return;

        var text = Convert.ToString(row.Cells[e.ColumnIndex].Value, CultureInfo.InvariantCulture) ?? string.Empty;
        var field = column.Name == "Label" ? ValueField.Label : ValueField.Value;
        // Applying inside CellEndEdit would rebuild the grid reentrantly
        Defer(() => CommitGridEdit(id, field, text));
    }

    private void CommitGridEdit(string id, ValueField field, string text)
    {
        if (IsDisposed || !IsAttached || Host.IsBusy)
            return;

        var value = Value(id);
        if (value == null)
            return;

        var result = CaseGuideEditing.SetValueField(Host.Draft, id, field, text);
        if (!result.Succeeded)
        {
            // The edit is cancelled: the cell shows the stored value again ("Label cannot be empty.")
            UpdateRow(id);
            Host.SetStatus(result.Error, Theme.Danger);
            return;
        }

        UpdateRow(id);
        if (!result.Changed)
            return;

        if (_builtId == id)
            UpdateDetailValues(value);
        Host.NotifyEdited(GuidePart.Values);
    }

    // ----- Detail -----

    protected override void ShowDetail(string? id)
    {
        var value = Value(id);
        if (value == null)
        {
            ClearDetail();
            DetailHeading.Text = string.Empty;
            DetailMeta.Text = string.Empty;
            return;
        }

        if (_builtId == value.Id && Fields.Controls.Count > 0 && _notesShown == NotesText(value))
        {
            // Same value: update in place so the card does not flicker or lose its scroll position
            UpdateDetailValues(value);
            return;
        }

        BuildDetail(value);
    }

    private void ClearDetail()
    {
        ClearFields();
        _textFields.Clear();
        _fieldErrors.Clear();
        _pickerState = string.Empty;
        _builtId = null;
        _txtLabel = _txtValue = _txtDescription = null;
        _cmbKind = null;
        _pickFacts = null;
        _lblNotes = null;
        _notesShown = string.Empty;
        NoticeSlot.Visible = false;
    }

    private void BuildDetail(CanonicalValue value)
    {
        Fields.SuspendLayout();
        ClearDetail();

        _cmbKind = AddCombo("Kind", Kinds.Select(k => k.Text));
        _txtLabel = new TextBox();
        AddTextField("Label", _txtLabel);
        _txtValue = new TextBox();
        AddTextField("Exact value", _txtValue, 1, ExactValueHint);
        _txtDescription = new TextBox();
        AddTextField("Description", _txtDescription, 3);
        _pickFacts = new LinkPicker("Filter facts");
        AddField("Used in facts", _pickFacts);
        _notesShown = NotesText(value);
        if (_notesShown.Length > 0)
        {
            _lblNotes = AddCaptionedLabel("Notes from generation", _notesShown);
            _lblNotes.Font = _noteFont;
            _lblNotes.ForeColor = Theme.TextMuted;
        }

        _textFields[_txtLabel] = ValueField.Label;
        _textFields[_txtValue] = ValueField.Value;
        _textFields[_txtDescription] = ValueField.Description;

        _builtId = value.Id;
        UpdateDetailValues(value);
        Fields.ResumeLayout(true);

        // Wire handlers only after every field exists and holds its value
        foreach (var box in _textFields.Keys)
            box.Leave += TextField_Leave;
        _cmbKind.SelectionChangeCommitted += Combo_SelectionChangeCommitted;
        _pickFacts.CheckedChanged += Picker_CheckedChanged;
    }

    private static string NotesText(CanonicalValue value) =>
        string.Join(Environment.NewLine, value.MappingNotes.Where(n => !string.IsNullOrWhiteSpace(n)).Select(n => n.Trim()));

    private void UpdateDetailValues(CanonicalValue value)
    {
        var guide = Guide;
        if (guide == null || _builtId != value.Id)
            return;

        DetailHeading.Text = value.Label;
        DetailMeta.Text = MetaLine(value);
        UpdateNotice(value);

        foreach (var (box, field) in _textFields)
        {
            var text = ValueOf(value, field);
            // Never overwrite text the user is typing or an invalid value that is still on screen
            if (box.Focused && box.Text.Trim() != text)
                continue;
            if (_fieldErrors.ContainsKey(box))
                continue;
            if (box.Text != text)
                box.Text = text;
        }

        if (_cmbKind != null)
            _cmbKind.SelectedIndex = Array.FindIndex(Kinds, k => k.Kind == value.Kind);

        SyncPicker(FactItems(guide), value.FactIds);
    }

    private void UpdateNotice(CanonicalValue value)
    {
        if (value.NeedsReview)
        {
            _lblNotice.Text = NoticeText(value);
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

    /// <summary>
    /// Facts in timeline order as "2024-03-03  {title}" (the picker adds the id in front).
    /// </summary>
    private static List<LinkItem> FactItems(CaseGuide guide) =>
        guide.Facts
            .OrderBy(f => string.IsNullOrEmpty(f.Date) ? 1 : 0)
            .ThenBy(f => f.Date, StringComparer.Ordinal)
            .ThenBy(f => f.Id, StringComparer.Ordinal)
            .Select(f => new LinkItem { Id = f.Id, Text = string.IsNullOrWhiteSpace(f.Date) ? f.Title : $"{f.Date}  {f.Title}" })
            .ToList();

    /// <summary>
    /// Re-sets the picker's items only when its items or checks changed, so the list keeps its scroll position.
    /// </summary>
    private void SyncPicker(List<LinkItem> items, List<string> checkedIds)
    {
        if (_pickFacts == null)
            return;

        var state = PickerState(items, checkedIds);
        if (_pickerState == state)
            return;

        _pickerState = state;
        _pickFacts.SetItems(items, checkedIds);
    }

    private static string PickerState(List<LinkItem> items, List<string> checkedIds) =>
        string.Join("\u001f", items.Select(i => i.Id + "\u001e" + i.Text)) + "\u001d" + string.Join("\u001f", checkedIds);

    private static string ValueOf(CanonicalValue v, ValueField field) => field switch
    {
        ValueField.Label => v.Label,
        ValueField.Value => v.Value,
        ValueField.Description => v.Description,
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
        Defer(() => CommitKind(combo, index));
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

        var value = Value(_builtId);
        if (value == null)
            return;

        if (box.Text.Trim() == ValueOf(value, field))
        {
            // Typed back to the stored value: drop that field's error
            if (_fieldErrors.Remove(box))
                ReshowFieldErrors();
            return;
        }

        var result = CaseGuideEditing.SetValueField(Host.Draft, value.Id, field, box.Text);
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
        AfterChange(value);
    }

    private void CommitKind(ComboBox combo, int index)
    {
        if (IsDisposed || combo.IsDisposed || !IsAttached || Busy || _builtId == null || index < 0 || index >= Kinds.Length)
            return;

        var value = Value(_builtId);
        if (value == null)
            return;

        // CaseGuideEditing takes the enum name
        var result = CaseGuideEditing.SetValueField(Host.Draft, value.Id, ValueField.Kind, Kinds[index].Kind.ToString());
        if (!result.Succeeded)
        {
            _fieldErrors[combo] = result.Error;
            ReshowFieldErrors();
            return;
        }

        if (result.Changed)
            AfterChange(value);
    }

    private void Picker_CheckedChanged(object? sender, EventArgs e)
    {
        if (sender is not LinkPicker picker || picker.IsDisposed || !IsAttached || Busy || _builtId == null)
            return;

        var value = Value(_builtId);
        if (value == null)
            return;

        // Unknown fact ids are dropped by CaseGuideEditing
        var result = CaseGuideEditing.SetValueFacts(Host.Draft, value.Id, picker.CheckedIds);
        if (!result.Changed)
            return;

        // The picker already shows the new checks: record them so it is not re-set (keeps its scroll)
        _pickerState = PickerState(FactItems(Guide!), value.FactIds);
        AfterChange(value);
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
    private void AfterChange(CanonicalValue value)
    {
        Host.NotifyEdited(GuidePart.Values);
        UpdateRow(value.Id);
        UpdateDetailValues(value);
    }

    /// <summary>
    /// Refreshes one row's cells in place; a change to the sorted column re-sorts with a full (deferred) rebuild.
    /// </summary>
    private void UpdateRow(string id)
    {
        var guide = Guide;
        var value = Value(id);
        if (guide == null || value == null)
            return;

        var row = Grid.Rows.Cast<DataGridViewRow>().FirstOrDefault(r => r.Tag as string == id);
        if (row == null)
            return;

        var cells = Cells(value);
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

        SetTooltip(guide, row, value);
        // Another row's dot can change too (a duplicate label is an error on both)
        Grid.Invalidate();
        RefreshHeader();

        if (resort)
            RequestRefresh();
    }

    // ----- Actions -----

    private void BtnAddValue_Click(object? sender, EventArgs e)
    {
        if (!IsAttached || Host.IsBusy)
            return;

        CommitPendingEdits();
        var value = CaseGuideEditing.AddValue(Host.Draft);
        Host.NotifyEdited(GuidePart.Values);
        RefreshFromDraft();
        if (SelectEntity(value.Id) && _txtLabel != null)
        {
            _txtLabel.Focus();
            _txtLabel.SelectAll();
        }
    }

    private void DeleteButton_Click(object? sender, EventArgs e)
    {
        var id = SelectedId;
        var value = Value(id);
        if (!IsAttached || Host.IsBusy || id == null || value == null)
            return;

        // C6 defaults to No (T-03-51)
        var (title, text) = CaseGuideUiText.C6DeleteValue(value.Label, value.Value);
        var answer = MessageBox.Show(FindForm(), text, title, MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2);
        if (answer != DialogResult.Yes || IsDisposed)
            return;

        var nextId = NeighborId(id);
        if (!CaseGuideEditing.DeleteValue(Host.Draft, id))
            return;

        Host.NotifyEdited(GuidePart.Values);
        RefreshFromDraft();
        if (nextId != null && SelectEntity(nextId))
            Grid.Focus();
    }

    private async void RegenerateButton_Click(object? sender, EventArgs e)
    {
        var id = SelectedId;
        var value = Value(id);
        if (!IsAttached || Host.IsBusy || id == null || value == null)
            return;

        CommitPendingEdits();
        var wasEdited = value.IsUserEdited;
        if (wasEdited)
        {
            // C3 defaults to No (T-03-51)
            var (title, text) = CaseGuideUiText.C3RegenerateEdited("Value", value.Label);
            var answer = MessageBox.Show(FindForm(), text, title, MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2);
            if (answer != DialogResult.Yes || IsDisposed)
                return;
        }

        var before = Snapshot(value);
        try
        {
            await Host.RegenerateEntryAsync(GuidePart.Values, id, GuidanceBox.Text.Trim());
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
        var after = Value(id);
        if (after != null && !after.IsUserEdited && (wasEdited || Snapshot(after) != before))
            GuidanceBox.Clear();

        // The rewritten entry may change any field or its notes, so build the card again
        _builtId = null;
        RefreshFromDraft();
    }

    private static string Snapshot(CanonicalValue v) =>
        string.Join("\u001f", v.Kind, v.Label, v.Value, v.Description, string.Join(",", v.FactIds), string.Join("\u001e", v.MappingNotes));

    private async void BtnRegenerateValues_Click(object? sender, EventArgs e)
    {
        if (!IsAttached || Host.IsBusy)
            return;

        CommitPendingEdits();
        try
        {
            await Host.RegenerateTabAsync(GuidePart.Values);
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
        if (!CaseGuideEditing.MarkValueReviewed(Host.Draft, id))
            return;

        Host.NotifyEdited(GuidePart.Values);
        RefreshFromDraft();
    }

    /// <summary>
    /// The row after the given one (or before it when it is last), so the selection stays in place after it leaves.
    /// </summary>
    private string? NeighborId(string id)
    {
        var rows = Grid.Rows.Cast<DataGridViewRow>().ToList();
        var index = rows.FindIndex(r => r.Tag as string == id);
        if (index < 0)
            return null;
        if (index + 1 < rows.Count)
            return rows[index + 1].Tag as string;
        return index > 0 ? rows[index - 1].Tag as string : null;
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
