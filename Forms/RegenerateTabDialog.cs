using ReelDiscovery.Helpers;
using ReelDiscovery.Models.Case;
using ReelDiscovery.UserControls;

namespace ReelDiscovery.Forms;

/// <summary>
/// D1: confirms a whole-tab regenerate (Cast, Facts or Values) and collects optional guidance plus
/// whether entries the user edited should be replaced too.
/// </summary>
public class RegenerateTabDialog : ThemedDialog
{
    private readonly GuidePart _part;
    private readonly int _editedCount;
    private TextBox _txtGuidance = null!;
    private CheckBox? _chkReplaceEdited;
    private Button _btnCancel = null!;
    private Button _btnRegenerate = null!;

    public RegenerateTabDialog(GuidePart part, int editedCount)
        : base(CaseGuideUiText.RegenerateTabTitle(ValidatePart(part)))
    {
        _part = part;
        _editedCount = Math.Max(0, editedCount);
        InitializeUI();
    }

    /// <summary>
    /// Guidance text for the regenerate prompt, trimmed (empty when none).
    /// </summary>
    public string Guidance => _txtGuidance.Text.Trim();

    /// <summary>
    /// True when the user also wants their edited entries rewritten.
    /// </summary>
    public bool ReplaceEdited => _chkReplaceEdited?.Checked ?? false;

    private static GuidePart ValidatePart(GuidePart part)
    {
        if (part == GuidePart.Issues)
            throw new ArgumentException("Issues come from the core document and cannot be regenerated as a tab.", nameof(part));
        return part;
    }

    private void InitializeUI()
    {
        AddHeadline(CaseGuideUiText.RegenerateTabHeadline(_part));
        AddBody(CaseGuideUiText.RegenerateTabBody(_part));

        var caption = CreateWrappingLabel("Guidance (optional)", Theme.SmallFont(), Theme.TextMuted);
        caption.Margin = new Padding(0, 0, 0, LDU(4));
        AddContent(caption);

        _txtGuidance = new TextBox { PlaceholderText = CaseGuideUiText.RegenerateGuidancePlaceholder(_part) };
        var guidanceHost = TextFieldHost.Create(this, _txtGuidance, lines: 3);
        // Three lines of optional guidance: the edit control still scrolls with the caret, and an
        // always-visible stock scrollbar in an empty field reads as unthemed chrome
        _txtGuidance.ScrollBars = ScrollBars.None;
        guidanceHost.Dock = DockStyle.Fill;
        guidanceHost.Margin = Padding.Empty;
        AddContent(guidanceHost);

        if (_editedCount > 0)
        {
            _chkReplaceEdited = new CheckBox
            {
                Text = CaseGuideUiText.ReplaceEditedText(_part, _editedCount),
                AutoSize = true,
                FlatStyle = FlatStyle.System,
                Font = Theme.BodyFont(),
                ForeColor = Theme.Text,
                BackColor = Theme.Background,
                UseMnemonic = false,
                Margin = new Padding(0, LDU(12), 0, 0)
            };
            AddContent(_chkReplaceEdited);
        }

        _btnCancel = ButtonHelper.CreateButton("Cancel", 0, LDU(34), ButtonStyle.Default);
        _btnCancel.DialogResult = DialogResult.Cancel;
        _btnRegenerate = ButtonHelper.CreateButton(CaseGuideUiText.RegenerateTabTitle(_part), 0, LDU(34), ButtonStyle.Primary);
        _btnRegenerate.DialogResult = DialogResult.OK;
        AddButtons(_btnCancel, _btnRegenerate);

        FinishLayout();
    }
}
