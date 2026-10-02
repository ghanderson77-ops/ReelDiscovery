using System.Globalization;
using ReelDiscovery.Forms;
using ReelDiscovery.Helpers;
using ReelDiscovery.Models.Case;
using ReelDiscovery.Services.Case;

namespace ReelDiscovery.UserControls;

public enum CastFilter
{
    All,
    Key,
    Background
}

/// <summary>
/// Cast tab (D-17..D-21): key and background characters. Name and Title edit in the grid; every other
/// field edits in the detail card. Every change goes through CaseGuideEditing, which validates it,
/// marks the character user-edited and flags the facts that cite a renamed or moved character.
/// </summary>
public class CastTabPanel : GuideTabPanel
{
    private const string EmailHint = "Built from the name and organization. Edit to override.";
    private const string SignatureHint = "Built from the fields above.";
    private const string DatePlaceholder = "YYYY-MM-DD";

    private static readonly CastRole[] Roles = { CastRole.Party, CastRole.InHouseCounsel, CastRole.OutsideCounsel, CastRole.Staff };
    private static readonly CastTier[] Tiers = { CastTier.Key, CastTier.Background };
    private static readonly SignatureStyle[] SignatureStyles =
        { SignatureStyle.Full, SignatureStyle.Short, SignatureStyle.NameOnly, SignatureStyle.MobileFooter };

    private readonly ThemedButton _btnAll;
    private readonly ThemedButton _btnKey;
    private readonly ThemedButton _btnBackground;
    private readonly ThemedButton _btnAddCharacter;
    private readonly ThemedButton _btnRegenerateCast;
    private readonly Control[] _tabActions;
    private readonly Dictionary<TextBox, CastField> _textFields = new();
    private readonly Dictionary<Control, string> _fieldErrors = new();
    private readonly List<string> _orgIds = new();
    private CastFilter _filter = CastFilter.All;

    // Detail fields of the shown character; recreated only when the character or its tier changes
    private string? _builtId;
    private CastTier _builtTier;
    private TextBox? _txtFirst;
    private TextBox? _txtLast;
    private TextBox? _txtTitle;
    private ComboBox? _cmbOrganization;
    private ComboBox? _cmbRole;
    private ComboBox? _cmbTier;
    private TextBox? _txtEmail;
    private TextBox? _txtFrom;
    private TextBox? _txtTo;
    private TextBox? _txtInvolvement;
    private TextBox? _txtVoice;
    private ComboBox? _cmbSignatureStyle;
    private Label? _lblSignature;
    private Label? _lblLinkedFacts;

    public CastTabPanel()
        : base(GuidePart.Cast, "Characters", "Character", hasToolbar: true,
            guidancePlaceholder: "Optional guidance for Regenerate Character, e.g. make her more cautious in writing")
    {
        var toggleHeight = LogicalToDeviceUnits(28);
        var toggleGap = LogicalToDeviceUnits(4);
        _btnAll = (ThemedButton)ButtonHelper.CreateButton("All", 0, toggleHeight, ButtonStyle.Toggle);
        _btnAll.Margin = new Padding(0, 0, toggleGap, 0);
        _btnAll.Selected = true;
        _btnKey = (ThemedButton)ButtonHelper.CreateButton("Key", 0, toggleHeight, ButtonStyle.Toggle);
        _btnKey.Margin = new Padding(0, 0, toggleGap, 0);
        _btnBackground = (ThemedButton)ButtonHelper.CreateButton("Background", 0, toggleHeight, ButtonStyle.Toggle);
        // 12 between the last toggle and the count line
        _btnBackground.Margin = new Padding(0, 0, LogicalToDeviceUnits(12), 0);
        AddHeaderControl(_btnAll);
        AddHeaderControl(_btnKey);
        AddHeaderControl(_btnBackground);
        var buttonHeight = LogicalToDeviceUnits(34);
        _btnAddCharacter = (ThemedButton)ButtonHelper.CreateButton("Add Character", 0, buttonHeight, ButtonStyle.Default);
        _btnAddCharacter.Margin = new Padding(0);
        _btnRegenerateCast = (ThemedButton)ButtonHelper.CreateButton("Regenerate Cast...", 0, buttonHeight, ButtonStyle.Default);
        _btnRegenerateCast.Margin = new Padding(0);
        _tabActions = new Control[] { _btnAddCharacter, _btnRegenerateCast };

        // Wire handlers only after every control exists
        _btnAll.Click += (s, e) => SetFilter(CastFilter.All);
        _btnKey.Click += (s, e) => SetFilter(CastFilter.Key);
        _btnBackground.Click += (s, e) => SetFilter(CastFilter.Background);
        Grid.CellEndEdit += Grid_CellEndEdit;
        _btnAddCharacter.Click += BtnAddCharacter_Click;
        _btnRegenerateCast.Click += BtnRegenerateCast_Click;
        DeleteButton.Click += DeleteButton_Click;
        RegenerateButton.Click += RegenerateButton_Click;
    }

    public override IReadOnlyList<Control> TabActions => _tabActions;

    // ----- Static text and filter helpers (unit-tested) -----

    public static string RoleText(CastRole role) => role switch
    {
        CastRole.Party => "Party",
        CastRole.InHouseCounsel => "In-house counsel",
        CastRole.OutsideCounsel => "Outside counsel",
        _ => "Staff"
    };

    public static string TierText(CastTier tier) => tier == CastTier.Key ? "Key" : "Background";

    public static string SignatureStyleText(SignatureStyle style) => style switch
    {
        SignatureStyle.Full => "Full",
        SignatureStyle.Short => "Short",
        SignatureStyle.NameOnly => "Name only",
        _ => "Mobile footer"
    };

    /// <summary>
    /// Detail meta line, e.g. "CH-007 · Key · Outside counsel · Edited by you".
    /// </summary>
    public static string MetaLine(CastMember m)
    {
        var text = $"{m.Id} · {TierText(m.Tier)} · {RoleText(m.Role)}";
        return m.IsUserEdited ? text + " · Edited by you" : text;
    }

    public static string LinkedFactsText(int n) => CaseGuideUiText.LinkedFactsText(n);

    /// <summary>
    /// Splits a full name at the last space ("Maria Elena Ortiz" -> "Maria Elena" + "Ortiz"), the same rule as CastField.FullName.
    /// </summary>
    public static (string First, string Last) SplitName(string full)
    {
        var text = (full ?? string.Empty).Trim();
        var split = text.LastIndexOf(' ');
        return split > 0 ? (text[..split].Trim(), text[(split + 1)..].Trim()) : (text, string.Empty);
    }

    public static bool PassesFilter(CastMember m, CastFilter filter) => filter switch
    {
        CastFilter.Key => m.Tier == CastTier.Key,
        CastFilter.Background => m.Tier == CastTier.Background,
        _ => true
    };

    // ----- Grid -----

    // Widths in logical px. ID is wider than the UI-SPEC 64 ("CH-006" plus the 15 px cell inset was cut off) and Tier
    // is 96, not 88 ("Backgrou..."). The Fill minimums give back the difference so the columns fit beside the 344 detail
    // card at 900x600 without a horizontal scrollbar, also when a real-sized cast shows the vertical scrollbar:
    // 28 + 72 + 96 + 80 + 96 + 96 = 468, plus 17 for the scrollbar, inside the 490 grid. Organization keeps 96 so its
    // header is never cut.
    protected override void BuildColumns()
    {
        GridStyle.AddDotColumn(Grid, this);
        Grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Id", HeaderText = "ID", Width = LogicalToDeviceUnits(72), ReadOnly = true });
        Grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Name",
            HeaderText = "Name",
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
            FillWeight = 30,
            MinimumWidth = LogicalToDeviceUnits(96)
        });
        Grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Title",
            HeaderText = "Title",
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
            FillWeight = 25,
            MinimumWidth = LogicalToDeviceUnits(80)
        });
        Grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Organization",
            HeaderText = "Organization",
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
            FillWeight = 25,
            MinimumWidth = LogicalToDeviceUnits(96),
            ReadOnly = true
        });
        Grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Tier", HeaderText = "Tier", Width = LogicalToDeviceUnits(96), ReadOnly = true });
    }


    private CaseGuide? Guide => IsAttached ? Host.Draft.Guide : null;

    private CastMember? Member(string? id) => Guide?.FindCast(id ?? string.Empty);

    protected override string EmptyRowsText =>
        _filter != CastFilter.All && Guide?.Cast.Count > 0
            ? CaseGuideUiText.NoFilterMatchText(GuidePart.Cast, keyFilter: _filter == CastFilter.Key)
            : base.EmptyRowsText;

    protected override void RefreshHeader()
    {
        var cast = Guide?.Cast ?? new List<CastMember>();
        CountLabel.Text = CaseGuideUiText.CastCountText(cast.Count(c => c.Tier == CastTier.Key), cast.Count(c => c.Tier == CastTier.Background));
    }

    protected override IEnumerable<(string Id, object?[] Cells)> BuildRows()
    {
        var guide = Guide;
        if (guide == null)
            yield break;

        foreach (var member in guide.Cast.Where(m => PassesFilter(m, _filter)))
            yield return (member.Id, Cells(guide, member));
    }

    private static object?[] Cells(CaseGuide guide, CastMember m)
    {
        var org = guide.FindOrganization(m.OrganizationId);
        return new object?[] { m.Id, m.FullName, m.Title, org?.Name ?? string.Empty, TierText(m.Tier) };
    }

    protected override void ApplyEditableState()
    {
        base.ApplyEditableState();
        UpdateNameTooltips();
        if (Grid.ReadOnly)
            return;

        // Only Name and Title edit in the grid; everything else edits in the detail card
        foreach (DataGridViewColumn column in Grid.Columns)
            column.ReadOnly = column.Name != "Name" && column.Name != "Title";
    }

    private void UpdateNameTooltips()
    {
        var guide = Guide;
        var name = Grid.Columns["Name"];
        if (guide == null || name == null)
            return;

        foreach (DataGridViewRow row in Grid.Rows)
            row.Cells[name.Index].ToolTipText = guide.FindCast(row.Tag as string ?? string.Empty)?.Email ?? string.Empty;
    }

    protected override Color DotColor(string id)
    {
        // The entry being written stays Accent; otherwise a character without details is BorderStrong
        if (IsAttached && id != Host.WritingEntityId && Member(id)?.DetailsPending == true)
            return Theme.BorderStrong;
        return base.DotColor(id);
    }

    private void SetFilter(CastFilter filter)
    {
        if (_filter == filter)
            return;

        CommitPendingEdits();
        ApplyFilter(filter);
        RefreshFromDraft();
    }

    private void ApplyFilter(CastFilter filter)
    {
        _filter = filter;
        _btnAll.Selected = filter == CastFilter.All;
        _btnKey.Selected = filter == CastFilter.Key;
        _btnBackground.Selected = filter == CastFilter.Background;
    }

    public override void FocusField(GuideFindingField field) => FocusDetailControl(field switch
    {
        GuideFindingField.Name => _txtFirst,
        GuideFindingField.Email => _txtEmail,
        GuideFindingField.Organization => _cmbOrganization,
        GuideFindingField.ActiveRange => _txtFrom,
        _ => null
    });

    public override bool SelectEntity(string id)
    {
        // Clear a filter that hides the character first
        var member = Member(id);
        if (member != null && !PassesFilter(member, _filter))
        {
            ApplyFilter(CastFilter.All);
            RefreshFromDraft();
        }
        return base.SelectEntity(id);
    }

    private void Grid_CellEndEdit(object? sender, DataGridViewCellEventArgs e)
    {
        if (e.RowIndex < 0 || e.RowIndex >= Grid.Rows.Count || !IsAttached)
            return;

        var row = Grid.Rows[e.RowIndex];
        var column = Grid.Columns[e.ColumnIndex];
        if (row.Tag is not string id || (column.Name != "Name" && column.Name != "Title"))
            return;

        var text = Convert.ToString(row.Cells[e.ColumnIndex].Value, CultureInfo.InvariantCulture) ?? string.Empty;
        var field = column.Name == "Name" ? CastField.FullName : CastField.Title;
        // Applying inside CellEndEdit would rebuild the grid reentrantly (T-03-46)
        QueueGridEdit(() => CommitGridEdit(id, field, column.HeaderText, text));
    }

    private void CommitGridEdit(string id, CastField field, string columnHeader, string text)
    {
        if (IsDisposed || !IsAttached || Host.IsBusy)
            return;

        var member = Member(id);
        if (member == null)
            return;

        var result = CaseGuideEditing.SetCastField(Host.Draft, id, field, text);
        if (!result.Succeeded)
        {
            // The edit is cancelled: the cell shows the stored value again
            UpdateRow(id);
            Host.SetStatus(result.Error.Length > 0 ? result.Error : $"{columnHeader} cannot be empty.", Theme.Danger);
            return;
        }

        UpdateRow(id);
        if (!result.Changed)
            return;

        if (_builtId == id)
            UpdateDetailValues(member);
        AfterChange(member, result);
    }

    // ----- Detail -----

    protected override void ShowDetail(string? id)
    {
        var member = Member(id);
        if (member == null)
        {
            ClearDetail();
            DetailHeading.Text = string.Empty;
            DetailMeta.Text = string.Empty;
            return;
        }

        if (_builtId == member.Id && _builtTier == member.Tier && Fields.Controls.Count > 0)
        {
            // Same character and tier: update in place so the card does not flicker or lose its scroll position
            UpdateDetailValues(member);
            return;
        }

        BuildDetail(member);
    }

    private void ClearDetail()
    {
        ClearFields();
        _textFields.Clear();
        _fieldErrors.Clear();
        _builtId = null;
        _txtFirst = _txtLast = _txtTitle = _txtEmail = _txtFrom = _txtTo = _txtInvolvement = _txtVoice = null;
        _cmbOrganization = _cmbRole = _cmbTier = _cmbSignatureStyle = null;
        _lblSignature = _lblLinkedFacts = null;
    }

    private void BuildDetail(CastMember member)
    {
        var guide = Guide!;
        Fields.SuspendLayout();
        ClearDetail();
        var key = member.Tier == CastTier.Key;

        _txtFirst = new TextBox();
        _txtLast = new TextBox();
        AddPairedTextFields("First name", _txtFirst, "Last name", _txtLast);
        _txtTitle = new TextBox();
        AddTextField("Title", _txtTitle);
        _cmbOrganization = AddCombo("Organization", Array.Empty<string>());
        (_cmbRole, _cmbTier) = AddPairedCombos("Role", Roles.Select(RoleText), "Tier", Tiers.Select(TierText));
        _txtEmail = new TextBox();
        AddTextField("Email", _txtEmail, hint: EmailHint);
        _txtFrom = new TextBox { PlaceholderText = DatePlaceholder };
        _txtTo = new TextBox { PlaceholderText = DatePlaceholder };
        AddPairedTextFields("Active from", _txtFrom, "Active to", _txtTo);
        _txtInvolvement = new TextBox();
        AddTextField(key ? "Key involvement" : "Role at the organization", _txtInvolvement, key ? 4 : 2);
        if (key)
        {
            _txtVoice = new TextBox();
            AddTextField("Writing voice", _txtVoice, 2);
            _cmbSignatureStyle = AddCombo("Signature style", SignatureStyles.Select(SignatureStyleText));
            _lblSignature = AddCaptionedLabel("Signature", string.Empty);
            _lblSignature.ForeColor = Theme.TextMuted;
            var hint = AddNote(SignatureHint);
            hint.Margin = new Padding(0, LogicalToDeviceUnits(4), 0, 0);
        }
        _lblLinkedFacts = AddCaptionedLabel("Linked facts", string.Empty);

        _textFields[_txtFirst] = CastField.FirstName;
        _textFields[_txtLast] = CastField.LastName;
        _textFields[_txtTitle] = CastField.Title;
        _textFields[_txtEmail] = CastField.Email;
        _textFields[_txtFrom] = CastField.ActiveFrom;
        _textFields[_txtTo] = CastField.ActiveTo;
        _textFields[_txtInvolvement] = CastField.Involvement;
        if (_txtVoice != null)
            _textFields[_txtVoice] = CastField.VoiceNote;

        _builtId = member.Id;
        _builtTier = member.Tier;
        UpdateDetailValues(member, guide);
        Fields.ResumeLayout(true);

        // Wire handlers only after every field exists and holds its value
        foreach (var box in _textFields.Keys)
            box.Leave += TextField_Leave;
        _cmbOrganization.SelectionChangeCommitted += Combo_SelectionChangeCommitted;
        _cmbRole.SelectionChangeCommitted += Combo_SelectionChangeCommitted;
        _cmbTier.SelectionChangeCommitted += Combo_SelectionChangeCommitted;
        if (_cmbSignatureStyle != null)
            _cmbSignatureStyle.SelectionChangeCommitted += Combo_SelectionChangeCommitted;
    }

    private void UpdateDetailValues(CastMember member, CaseGuide? guide = null)
    {
        guide ??= Guide;
        if (guide == null || _builtId != member.Id)
            return;

        DetailHeading.Text = member.FullName;
        DetailMeta.Text = MetaLine(member);

        foreach (var (box, field) in _textFields)
        {
            var value = ValueOf(member, field);
            // Never overwrite text the user is typing or an invalid value that is still on screen
            if (box.Focused && box.Text.Trim() != value)
                continue;
            if (_fieldErrors.ContainsKey(box))
                continue;
            if (box.Text != value)
                box.Text = value;
        }

        if (_cmbOrganization != null)
        {
            var ids = guide.Organizations.Select(o => o.Id).ToList();
            if (!ids.SequenceEqual(_orgIds) || _cmbOrganization.Items.Count != ids.Count)
            {
                _orgIds.Clear();
                _orgIds.AddRange(ids);
                _cmbOrganization.BeginUpdate();
                _cmbOrganization.Items.Clear();
                _cmbOrganization.Items.AddRange(guide.Organizations.Select(o => (object)OrganizationText(o)).ToArray());
                _cmbOrganization.EndUpdate();
            }
            _cmbOrganization.SelectedIndex = _orgIds.IndexOf(member.OrganizationId);
        }
        if (_cmbRole != null)
            _cmbRole.SelectedIndex = Array.IndexOf(Roles, member.Role);
        if (_cmbTier != null)
            _cmbTier.SelectedIndex = Array.IndexOf(Tiers, member.Tier);
        if (_cmbSignatureStyle != null)
            _cmbSignatureStyle.SelectedIndex = Array.IndexOf(SignatureStyles, member.SignatureStyle);
        if (_lblSignature != null)
            _lblSignature.Text = string.IsNullOrWhiteSpace(member.Signature) ? "—" : member.Signature.Replace("\r\n", "\n").Replace("\n", Environment.NewLine);
        if (_lblLinkedFacts != null)
            _lblLinkedFacts.Text = LinkedFactsText(CaseGuideEditing.FactsReferencing(Host.Draft, member.Id).Count);
    }

    private static string OrganizationText(CaseOrganization org) =>
        string.IsNullOrWhiteSpace(org.Domain) ? org.Name : $"{org.Name} · {org.Domain}";

    private static string ValueOf(CastMember m, CastField field) => field switch
    {
        CastField.FirstName => m.FirstName,
        CastField.LastName => m.LastName,
        CastField.Title => m.Title,
        CastField.Email => m.Email,
        CastField.ActiveFrom => m.ActiveFrom,
        CastField.ActiveTo => m.ActiveTo,
        CastField.Involvement => m.Involvement,
        CastField.VoiceNote => m.VoiceNote,
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

        // Deferred: a tier change rebuilds the fields, which must not dispose the combo inside its own event
        var index = combo.SelectedIndex;
        Defer(() => CommitCombo(combo, index));
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
        var member = Member(id);
        if (member == null)
            return;

        if (box.Text.Trim() == ValueOf(member, field))
        {
            // Typed back to the stored value: drop that field's error
            if (_fieldErrors.Remove(box))
                ReshowFieldErrors();
            return;
        }

        var result = CaseGuideEditing.SetCastField(Host.Draft, id, field, box.Text);
        if (!result.Succeeded)
        {
            // Invalid input is shown with its message and never stored (T-03-44)
            _fieldErrors[box] = result.Error;
            ReshowFieldErrors();
            return;
        }

        if (!result.Changed)
            return;

        _fieldErrors.Clear();
        ClearFieldErrors();
        UpdateRow(id);
        UpdateDetailValues(member);
        AfterChange(member, result);
    }

    private void CommitCombo(ComboBox combo, int index)
    {
        if (IsDisposed || combo.IsDisposed || !IsAttached || Busy || _builtId == null || index < 0)
            return;

        var id = _builtId;
        var member = Member(id);
        if (member == null)
            return;

        CastField field;
        string value;
        if (combo == _cmbOrganization && index < _orgIds.Count)
            (field, value) = (CastField.OrganizationId, _orgIds[index]);
        else if (combo == _cmbRole && index < Roles.Length)
            (field, value) = (CastField.Role, Roles[index].ToString());
        else if (combo == _cmbTier && index < Tiers.Length)
            (field, value) = (CastField.Tier, Tiers[index].ToString());
        else if (combo == _cmbSignatureStyle && index < SignatureStyles.Length)
            (field, value) = (CastField.SignatureStyle, SignatureStyles[index].ToString());
        else
            return;

        var result = CaseGuideEditing.SetCastField(Host.Draft, id, field, value);
        if (!result.Succeeded)
        {
            _fieldErrors[combo] = result.Error;
            ReshowFieldErrors();
            return;
        }

        if (!result.Changed)
            return;

        _fieldErrors.Clear();
        ClearFieldErrors();
        if (field == CastField.Tier)
        {
            // Background and key characters show different fields; keep the character visible under the filter
            if (!PassesFilter(member, _filter))
                ApplyFilter(CastFilter.All);
            Host.NotifyEdited(GuidePart.Cast);
            RefreshFromDraft();
            return;
        }

        UpdateRow(id);
        UpdateDetailValues(member);
        AfterChange(member, result);
    }

    private void ReshowFieldErrors()
    {
        ClearFieldErrors();
        foreach (var (field, message) in _fieldErrors)
            ShowFieldError(field, message);
    }

    /// <summary>
    /// Saves and revalidates through the host, then reports the facts a name, organization or date change flagged.
    /// </summary>
    private void AfterChange(CastMember member, GuideEditResult result)
    {
        Host.NotifyEdited(GuidePart.Cast);
        if (result.AffectedFactIds.Count > 0)
            Host.SetStatus(CaseGuideUiText.FactsMarkedForReviewText(result.AffectedFactIds.Count, member.FullName), Theme.Warning);
    }

    /// <summary>
    /// Refreshes one row's cells in place; a change to the sorted column re-sorts with a full (deferred) rebuild.
    /// </summary>
    private void UpdateRow(string id)
    {
        var guide = Guide;
        var member = Member(id);
        if (guide == null || member == null)
            return;

        var row = Grid.Rows.Cast<DataGridViewRow>().FirstOrDefault(r => r.Tag as string == id);
        if (row == null)
            return;

        var cells = Cells(guide, member);
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

        var name = Grid.Columns["Name"];
        if (name != null)
            row.Cells[name.Index].ToolTipText = member.Email;
        Grid.InvalidateRow(row.Index);

        if (resort)
            RequestRefresh();
    }

    // ----- Actions -----

    private void BtnAddCharacter_Click(object? sender, EventArgs e)
    {
        if (!IsAttached || Host.IsBusy)
            return;

        CommitPendingEdits();
        var member = CaseGuideEditing.AddCharacter(Host.Draft);
        Host.NotifyEdited(GuidePart.Cast);
        // New characters are key; a Background filter would hide it
        if (!PassesFilter(member, _filter))
            ApplyFilter(CastFilter.All);
        RefreshFromDraft();
        // SelectEntity focuses the first detail field (First name)
        SelectEntity(member.Id);
    }

    private void DeleteButton_Click(object? sender, EventArgs e)
    {
        var id = SelectedId;
        var member = Member(id);
        if (!IsAttached || Host.IsBusy || id == null || member == null)
            return;

        var name = member.FullName;
        var refs = CaseGuideEditing.FactsReferencing(Host.Draft, id);
        var nextId = NeighborId(id);
        var status = string.Empty;

        if (refs.Count > 0)
        {
            using var dialog = new DeleteReferencesDialog(name, member.FirstName, id,
                refs.Select(f => $"{f.Id}  {f.Date}  {f.Title}").ToList());
            dialog.ShowDialog(FindForm());
            if (IsDisposed)
                return;

            switch (dialog.Choice)
            {
                case DeleteChoice.DeleteOnly:
                    CaseGuideEditing.DeleteCharacter(Host.Draft, id, removeFromFacts: false);
                    status = CaseGuideUiText.DeletedWithLinksText(name, refs.Count, id);
                    break;
                case DeleteChoice.RemoveAndDelete:
                    CaseGuideEditing.DeleteCharacter(Host.Draft, id, removeFromFacts: true);
                    status = CaseGuideUiText.FactsMarkedForReviewText(refs.Count, name);
                    break;
                default:
                    // Keep is the safe default (T-03-45)
                    return;
            }
        }
        else
        {
            var (title, text) = CaseGuideUiText.C4DeleteCharacter(name, id);
            var answer = MessageBox.Show(FindForm(), text, title, MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2);
            if (answer != DialogResult.Yes || IsDisposed)
                return;

            CaseGuideEditing.DeleteCharacter(Host.Draft, id, removeFromFacts: false);
        }

        Host.NotifyEdited(GuidePart.Cast);
        if (status.Length > 0)
            Host.SetStatus(status, Theme.Warning);
        RefreshFromDraft();
        if (nextId != null && SelectEntity(nextId))
            Grid.Focus();
    }

    /// <summary>
    /// The row after the given one (or before it when it is last), so the selection stays in place after a delete.
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

    private async void RegenerateButton_Click(object? sender, EventArgs e)
    {
        var id = SelectedId;
        var member = Member(id);
        if (!IsAttached || Host.IsBusy || id == null || member == null)
            return;

        CommitPendingEdits();
        var wasEdited = member.IsUserEdited;
        if (wasEdited)
        {
            var (title, text) = CaseGuideUiText.C3RegenerateEdited("Character", member.FullName);
            var answer = MessageBox.Show(FindForm(), text, title, MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2);
            if (answer != DialogResult.Yes || IsDisposed)
                return;
        }

        var before = Snapshot(member);
        try
        {
            await Host.RegenerateEntryAsync(GuidePart.Cast, id, GuidanceBox.Text.Trim());
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
        var after = Member(id);
        if (after != null && !after.IsUserEdited && (wasEdited || Snapshot(after) != before))
            GuidanceBox.Clear();

        // The rewritten entry may change its tier or any field, so build the card again
        _builtId = null;
        RefreshFromDraft();
    }

    private static string Snapshot(CastMember m) =>
        string.Join("\u001f", m.FullName, m.Title, m.OrganizationId, m.Role, m.Tier, m.Email, m.ActiveFrom, m.ActiveTo,
            m.Involvement, m.VoiceNote, m.SignatureStyle, m.Signature);

    private async void BtnRegenerateCast_Click(object? sender, EventArgs e)
    {
        if (!IsAttached || Host.IsBusy)
            return;

        CommitPendingEdits();
        try
        {
            await Host.RegenerateTabAsync(GuidePart.Cast);
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

    private void Defer(Action action)
    {
        if (IsHandleCreated && !IsDisposed)
            BeginInvoke(action);
        else
            action();
    }
}
