using System.ComponentModel;
using ReelDiscovery.Helpers;

namespace ReelDiscovery.UserControls;

public class LinkItem
{
    public string Id = "";
    public string Text = "";
}

/// <summary>
/// Filter box plus a checklist for issue / cast / fact references (UI-SPEC LinkPicker). Checked items list
/// first, locked items cannot be unchecked, and the filter never unchecks items it hides.
/// The list is at most 6 rows tall; its height comes from ItemHeight after the handle exists.
/// </summary>
public class LinkPicker : UserControl
{
    private const int MaxVisibleRows = 6;

    private readonly List<LinkItem> _items = new();
    private readonly HashSet<string> _checked = new(StringComparer.Ordinal);
    private readonly HashSet<string> _locked = new(StringComparer.Ordinal);
    private readonly List<string> _unknownChecked = new();
    private readonly List<string> _shownIds = new();
    private string _lockedSuffix = " (did it)";
    private bool _suppress;
    private bool _readOnly;

    private TextBox _txtFilter = null!;
    private CardPanel _filterHost = null!;
    private Label _lblCount = null!;
    private CardPanel _listHost = null!;
    private CheckedListBox _list = null!;

    public event EventHandler? CheckedChanged;

    public LinkPicker(string filterPlaceholder)
    {
        InitializeUI(filterPlaceholder);
    }

    private void InitializeUI(string filterPlaceholder)
    {
        this.Dock = DockStyle.Top;
        this.Padding = new Padding(0);
        this.Margin = new Padding(0);
        this.BackColor = Theme.Surface;

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 2,
            Margin = new Padding(0),
            Padding = new Padding(0),
            BackColor = Theme.Surface
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));     // 0: Filter | count
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100)); // 1: Checklist

        _txtFilter = new TextBox { PlaceholderText = filterPlaceholder };
        _filterHost = TextFieldHost.Create(this, _txtFilter);
        _filterHost.Dock = DockStyle.Fill;
        _filterHost.Margin = new Padding(0);
        _lblCount = new Label
        {
            AutoSize = true,
            Anchor = AnchorStyles.Right,
            Margin = new Padding(LogicalToDeviceUnits(Theme.SmallGap), 0, 0, 0),
            UseMnemonic = false,
            Font = Theme.SmallFont(),
            ForeColor = Theme.TextMuted,
            BackColor = Theme.Surface,
            Text = CaseGuideUiText.SelectedCountText(0)
        };

        _listHost = new CardPanel
        {
            Radius = Theme.Radius,
            BorderColor = Theme.BorderStrong,
            Padding = new Padding(1),
            Dock = DockStyle.Fill,
            Margin = new Padding(0, LogicalToDeviceUnits(4), 0, 0)
        };
        _list = new CheckedListBox
        {
            BorderStyle = BorderStyle.None,
            CheckOnClick = true,
            IntegralHeight = false,
            Font = Theme.BodyFont(),
            ForeColor = Theme.Text,
            BackColor = Theme.Surface,
            Dock = DockStyle.Fill
        };
        _listHost.Controls.Add(_list);

        layout.Controls.Add(_filterHost, 0, 0);
        layout.Controls.Add(_lblCount, 1, 0);
        layout.Controls.Add(_listHost, 0, 1);
        layout.SetColumnSpan(_listHost, 2);
        this.Controls.Add(layout);
        UpdateHeight();

        // Wire handlers only after every control exists
        _txtFilter.TextChanged += (s, e) => Rebuild();
        _list.ItemCheck += List_ItemCheck;
    }

    /// <summary>
    /// Ids currently checked, in item order (including checked ids hidden by the filter). Checked ids that
    /// are not in the item list are kept so a commit never drops a reference the user did not touch.
    /// </summary>
    public IReadOnlyList<string> CheckedIds =>
        _items.Where(i => _checked.Contains(i.Id)).Select(i => i.Id).Concat(_unknownChecked).ToList();

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool ReadOnly
    {
        get => _readOnly;
        set
        {
            _readOnly = value;
            _list.ForeColor = value ? Theme.TextMuted : Theme.Text;
        }
    }

    public void SetItems(IReadOnlyList<LinkItem> items, IEnumerable<string> checkedIds, IEnumerable<string>? lockedIds = null, string lockedSuffix = " (did it)")
    {
        _items.Clear();
        _items.AddRange(items.Where(i => i != null && !string.IsNullOrEmpty(i.Id)));
        _lockedSuffix = lockedSuffix;
        _locked.Clear();
        _checked.Clear();
        _unknownChecked.Clear();

        var known = new HashSet<string>(_items.Select(i => i.Id), StringComparer.Ordinal);
        foreach (var id in lockedIds ?? Enumerable.Empty<string>())
        {
            if (!string.IsNullOrEmpty(id) && known.Contains(id))
                _locked.Add(id);
        }
        foreach (var id in checkedIds ?? Enumerable.Empty<string>())
        {
            if (string.IsNullOrEmpty(id))
                continue;
            if (known.Contains(id))
                _checked.Add(id);
            else if (!_unknownChecked.Contains(id))
                _unknownChecked.Add(id);
        }
        // Locked items are always checked
        _checked.UnionWith(_locked);

        Rebuild();
    }

    private string DisplayText(LinkItem item)
    {
        if (!_locked.Contains(item.Id))
            return $"{item.Id}  {item.Text}";

        // The suffix follows the name (the part before " — "), so a narrow list never cuts it off
        var dash = item.Text.IndexOf(" — ", StringComparison.Ordinal);
        return dash > 0
            ? $"{item.Id}  {item.Text[..dash]}{_lockedSuffix}{item.Text[dash..]}"
            : $"{item.Id}  {item.Text}{_lockedSuffix}";
    }

    private void Rebuild()
    {
        var filter = _txtFilter.Text.Trim();
        var visible = _items
            .Select((item, index) => (item, index))
            .Where(x => filter.Length == 0
                || x.item.Id.Contains(filter, StringComparison.OrdinalIgnoreCase)
                || x.item.Text.Contains(filter, StringComparison.OrdinalIgnoreCase))
            // Locked first (e.g. the people who did it), then the other checked items, then the original order
            .OrderBy(x => _locked.Contains(x.item.Id) ? 0 : _checked.Contains(x.item.Id) ? 1 : 2)
            .ThenBy(x => x.index)
            .Select(x => x.item)
            .ToList();

        _suppress = true;
        try
        {
            _list.BeginUpdate();
            _list.Items.Clear();
            _shownIds.Clear();
            foreach (var item in visible)
            {
                _list.Items.Add(DisplayText(item), _checked.Contains(item.Id));
                _shownIds.Add(item.Id);
            }
            _list.EndUpdate();
        }
        finally
        {
            _suppress = false;
        }

        UpdateCount();
        UpdateHeight();
    }

    private void List_ItemCheck(object? sender, ItemCheckEventArgs e)
    {
        if (_suppress || e.Index < 0 || e.Index >= _shownIds.Count)
            return;

        if (_readOnly)
        {
            e.NewValue = e.CurrentValue;
            return;
        }

        var id = _shownIds[e.Index];
        if (_locked.Contains(id) && e.NewValue != CheckState.Checked)
        {
            e.NewValue = CheckState.Checked;
            return;
        }

        // ItemCheck fires before the check state changes: read e.NewValue and apply after the event (Pitfall 7)
        var isChecked = e.NewValue == CheckState.Checked;
        void Apply()
        {
            if (isChecked)
                _checked.Add(id);
            else
                _checked.Remove(id);
            UpdateCount();
            CheckedChanged?.Invoke(this, EventArgs.Empty);
        }

        if (IsHandleCreated)
            BeginInvoke(new Action(Apply));
        else
            Apply();
    }

    private void UpdateCount()
    {
        _lblCount.Text = CaseGuideUiText.SelectedCountText(_checked.Count + _unknownChecked.Count);
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        UpdateHeight();
    }

    private void UpdateHeight()
    {
        // At most 6 rows tall, measured from the list's real ItemHeight (never hard-coded); at least one row
        var rows = Math.Max(1, Math.Min(_list.Items.Count, MaxVisibleRows));
        var listHeight = rows * _list.ItemHeight + 2;
        _listHost.Height = listHeight;
        var height = _filterHost.Height + _listHost.Margin.Vertical + listHeight;
        if (Height != height)
            Height = height;
    }
}
