using ReelDiscovery.Helpers;
using ReelDiscovery.Models.Case;
using ReelDiscovery.UserControls;

namespace ReelDiscovery.Forms;

/// <summary>
/// Regenerate Selected (Key Facts selection bar): confirms rewriting the selected facts and collects optional guidance plus,
/// when any of them were edited by hand, whether those are replaced too. Same chrome as D1 (RegenerateTabDialog).
/// </summary>
public class RegenerateSelectedDialog : ThemedDialog
{
    private readonly int _selectedCount;
    private readonly int _editedCount;
    private TextBox _txtGuidance = null!;
    private CheckBox? _chkReplaceEdited;
    private Button _btnCancel = null!;
    private Button _btnRegenerate = null!;

    public RegenerateSelectedDialog(int selectedCount, int editedCount)
        : base(CaseGuideUiText.RegenerateSelectedTitle)
    {
        _selectedCount = Math.Max(0, selectedCount);
        _editedCount = Math.Clamp(editedCount, 0, _selectedCount);
        InitializeUI();
    }

    /// <summary>
    /// Guidance text for every fact's prompt, trimmed (empty when none).
    /// </summary>
    public string Guidance => _txtGuidance.Text.Trim();

    /// <summary>
    /// True when the user also wants the selected facts they edited rewritten.
    /// </summary>
    public bool ReplaceEdited => _chkReplaceEdited?.Checked ?? false;

    private void InitializeUI()
    {
        AddHeadline(CaseGuideUiText.RegenerateSelectedHeadline(_selectedCount));
        AddBody(CaseGuideUiText.RegenerateSelectedBody(_editedCount));

        var caption = CreateWrappingLabel("Guidance (optional)", Theme.SmallFont(), Theme.TextMuted);
        caption.Margin = new Padding(0, 0, 0, LDU(4));
        AddContent(caption);

        _txtGuidance = new TextBox { PlaceholderText = CaseGuideUiText.RegenerateSelectedPlaceholder };
        var guidanceHost = TextFieldHost.Create(this, _txtGuidance, lines: 3);
        // Same as D1: no always-visible stock scrollbar in an empty three-line field
        _txtGuidance.ScrollBars = ScrollBars.None;
        guidanceHost.Dock = DockStyle.Fill;
        guidanceHost.Margin = Padding.Empty;
        AddContent(guidanceHost);

        if (_editedCount > 0)
        {
            _chkReplaceEdited = new CheckBox
            {
                Text = CaseGuideUiText.ReplaceEditedText(GuidePart.Facts, _editedCount),
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
        _btnRegenerate = ButtonHelper.CreateButton("Regenerate Selected", 0, LDU(34), ButtonStyle.Primary);
        _btnRegenerate.DialogResult = DialogResult.OK;
        AddButtons(_btnCancel, _btnRegenerate);

        FinishLayout();
    }
}
