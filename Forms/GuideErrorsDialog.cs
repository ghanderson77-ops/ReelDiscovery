using ReelDiscovery.Helpers;
using ReelDiscovery.UserControls;

namespace ReelDiscovery.Forms;

public enum GuideErrorsChoice
{
    ShowChecks,
    FixAutomatically,
    ContinueAnyway
}

/// <summary>
/// M2 (Next with errors): lists up to five errors, each with how to fix it, and offers Show Checks (the safe, focused
/// default), Continue Anyway and, when some errors fix automatically, Fix Automatically ({k}) as the Primary action.
/// </summary>
public class GuideErrorsDialog : ThemedDialog
{
    private readonly ErrorsGateContent _content;
    private Button _btnShowChecks = null!;
    private Button _btnContinue = null!;
    private Button? _btnFix;

    public GuideErrorsDialog(ErrorsGateContent content)
        : base(content.Title)
    {
        _content = content;
        InitializeUI();
    }

    /// <summary>
    /// The user's choice; Show Checks unless another button was clicked (Esc and the close box keep Show Checks).
    /// </summary>
    public GuideErrorsChoice Choice { get; private set; } = GuideErrorsChoice.ShowChecks;

    private void InitializeUI()
    {
        AddHeadline(_content.Headline);
        var body = AddBody(_content.Body);
        // The list belongs to this sentence (it ends with a colon)
        body.Margin = new Padding(0, 0, 0, LDU(Theme.SmallGap));
        AddItemsCard(_content.Lines.Select(l => (l.Message, l.FixHint)).ToList(), _content.MoreText);

        _btnShowChecks = ButtonHelper.CreateButton(_content.ShowChecksText, 0, LDU(34), ButtonStyle.Default);
        _btnShowChecks.DialogResult = DialogResult.Cancel;
        _btnContinue = ButtonHelper.CreateButton(_content.ContinueAnywayText, 0, LDU(34), ButtonStyle.Default);
        _btnContinue.DialogResult = DialogResult.OK;
        if (_content.FixAutomaticallyText.Length > 0)
        {
            _btnFix = ButtonHelper.CreateButton(_content.FixAutomaticallyText, 0, LDU(34), ButtonStyle.Primary);
            _btnFix.DialogResult = DialogResult.OK;
            AddButtons(_btnShowChecks, _btnContinue, _btnFix);
        }
        else
        {
            AddButtons(_btnShowChecks, _btnContinue);
        }

        FinishLayout();

        // Handlers wired after every control exists
        _btnShowChecks.Click += (s, e) => Choice = GuideErrorsChoice.ShowChecks;
        _btnContinue.Click += (s, e) => Choice = GuideErrorsChoice.ContinueAnyway;
        if (_btnFix != null)
            _btnFix.Click += (s, e) => Choice = GuideErrorsChoice.FixAutomatically;
    }
}
