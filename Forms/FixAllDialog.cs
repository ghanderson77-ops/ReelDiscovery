using ReelDiscovery.Helpers;
using ReelDiscovery.UserControls;

namespace ReelDiscovery.Forms;

/// <summary>
/// Fix All (Checks card): lists how many checks each automatic fix clears and asks before changing the guide.
/// Cancel is the safe, focused default; Fix All is Primary.
/// </summary>
public class FixAllDialog : ThemedDialog
{
    private readonly FixAllContent _content;
    private Button _btnCancel = null!;
    private Button _btnFixAll = null!;

    public FixAllDialog(FixAllContent content)
        : base(content.Title)
    {
        _content = content;
        InitializeUI();
    }

    private void InitializeUI()
    {
        AddHeadline(_content.Headline);
        var body = AddBody(_content.Body);
        // The list belongs to this sentence (it ends with a colon)
        body.Margin = new Padding(0, 0, 0, LDU(Theme.SmallGap));
        AddItemsCard(_content.Lines.Select(l => (l, string.Empty)).ToList());
        AddBody(_content.Foot);

        _btnCancel = ButtonHelper.CreateButton("Cancel", 0, LDU(34), ButtonStyle.Default);
        _btnCancel.DialogResult = DialogResult.Cancel;
        _btnFixAll = ButtonHelper.CreateButton(_content.ConfirmText, 0, LDU(34), ButtonStyle.Primary);
        _btnFixAll.DialogResult = DialogResult.OK;
        AddButtons(_btnCancel, _btnFixAll);

        FinishLayout();
    }
}
