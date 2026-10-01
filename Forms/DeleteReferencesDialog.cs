using ReelDiscovery.Helpers;
using ReelDiscovery.UserControls;

namespace ReelDiscovery.Forms;

public enum DeleteChoice
{
    Keep,
    DeleteOnly,
    RemoveAndDelete
}

/// <summary>
/// D2: deleting a character that facts still reference. Lists the linked facts and offers
/// Keep (safe, focused), Delete Only (links stay and show under Checks) or Remove from Facts and Delete.
/// </summary>
public class DeleteReferencesDialog : ThemedDialog
{
    private const int MaxVisibleRows = 6;
    private const int ListPaddingHorizontal = 10;
    private const int ListPaddingVertical = 6;

    private readonly string _name;
    private readonly string _firstName;
    private readonly string _id;
    private readonly IReadOnlyList<string> _factLines;
    private CardPanel _listHost = null!;
    private ListBox _lstFacts = null!;
    private Button _btnKeep = null!;
    private Button _btnDeleteOnly = null!;
    private Button _btnRemoveAndDelete = null!;

    public DeleteReferencesDialog(string name, string firstName, string id, IReadOnlyList<string> factLines)
        : base("Delete Character")
    {
        _name = name;
        _firstName = string.IsNullOrWhiteSpace(firstName) ? name : firstName;
        _id = id;
        _factLines = factLines;
        InitializeUI();
    }

    /// <summary>
    /// The user's choice; Keep unless one of the delete buttons was clicked.
    /// </summary>
    public DeleteChoice Choice { get; private set; } = DeleteChoice.Keep;

    private void InitializeUI()
    {
        AddHeadline($"Delete {_name}?");
        var body = AddBody(CaseGuideUiText.DeleteLinkedBodyText(_name, _id, _factLines.Count));
        // The list belongs to this sentence (it ends with a colon), so it sits closer than a paragraph gap
        body.Margin = new Padding(0, 0, 0, LDU(Theme.SmallGap));

        _lstFacts = new ListBox
        {
            BorderStyle = BorderStyle.None,
            Font = Theme.BodyFont(),
            ForeColor = Theme.Text,
            BackColor = Theme.Surface,
            IntegralHeight = false,
            SelectionMode = SelectionMode.None,
            Dock = DockStyle.Fill,
            TabStop = false
        };
        _lstFacts.Items.AddRange(_factLines.Cast<object>().ToArray());

        // Same border host as the text fields; text inset matches the field text (10)
        _listHost = new CardPanel
        {
            Radius = Theme.Radius,
            BorderColor = Theme.BorderStrong,
            BackColor = Theme.Surface,
            Dock = DockStyle.Fill,
            Padding = new Padding(LDU(ListPaddingHorizontal), LDU(ListPaddingVertical), LDU(ListPaddingHorizontal), LDU(ListPaddingVertical)),
            Margin = new Padding(0, 0, 0, LDU(Theme.Gap))
        };
        _listHost.Controls.Add(_lstFacts);
        AddContent(_listHost);
        SizeList(_lstFacts.Font.Height);

        AddBody(CaseGuideUiText.DeleteLinkedFootText(_firstName));

        // '&' in a name must not become a mnemonic in the owner-drawn button text
        var safeFirstName = _firstName.Replace("&", "&&");
        _btnKeep = ButtonHelper.CreateButton($"Keep {safeFirstName}", 0, LDU(34), ButtonStyle.Default);
        _btnKeep.DialogResult = DialogResult.Cancel;
        _btnDeleteOnly = ButtonHelper.CreateButton("Delete Only", 0, LDU(34), ButtonStyle.Default);
        _btnDeleteOnly.DialogResult = DialogResult.OK;
        _btnRemoveAndDelete = ButtonHelper.CreateButton("Remove from Facts and Delete", 0, LDU(34), ButtonStyle.Danger);
        _btnRemoveAndDelete.DialogResult = DialogResult.OK;
        AddButtons(_btnKeep, _btnDeleteOnly, _btnRemoveAndDelete);

        FinishLayout();

        // Handlers wired after every control exists
        _btnKeep.Click += (s, e) => Choice = DeleteChoice.Keep;
        _btnDeleteOnly.Click += (s, e) => Choice = DeleteChoice.DeleteOnly;
        _btnRemoveAndDelete.Click += (s, e) => Choice = DeleteChoice.RemoveAndDelete;
    }

    protected override void OnLoad(EventArgs e)
    {
        // ItemHeight is only reliable once the list handle exists; resize to at most 6 rows, then refit the dialog
        SizeList(_lstFacts.ItemHeight);
        FinishLayout();
        base.OnLoad(e);
    }

    private void SizeList(int itemHeight)
    {
        var rows = Math.Max(1, Math.Min(_factLines.Count, MaxVisibleRows));
        _listHost.Height = rows * itemHeight + _listHost.Padding.Vertical + 2;
    }
}
