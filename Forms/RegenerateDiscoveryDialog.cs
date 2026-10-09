using ReelDiscovery.Helpers;
using ReelDiscovery.UserControls;

namespace ReelDiscovery.Forms;

/// <summary>
/// D-11 (08-UI-SPEC S2): confirms regenerating one discovery document, names the file it replaces, states the settings
/// the new version uses and collects optional guidance for the prompt.
/// </summary>
public class RegenerateDiscoveryDialog : ThemedDialog
{
    /// <summary>
    /// Code limit on the guidance appended to the prompt (T-08-27).
    /// </summary>
    public const int MaxGuidanceLength = 1_000;

    private readonly string _shortTitle;
    private readonly string _fileName;
    private readonly bool _isDeposition;
    private readonly string _settingsLine;
    private readonly bool _fileOpen;
    private readonly decimal _estimateUsd;
    private TextBox _txtGuidance = null!;
    private Label? _lblWarning;
    private Button _btnCancel = null!;
    private Button _btnRegenerate = null!;

    public RegenerateDiscoveryDialog(string shortTitle, string fileName, bool isDeposition, string settingsLine, bool fileOpen, decimal estimateUsd)
        : base(DiscoveryUiText.RegenerateTitle)
    {
        _shortTitle = shortTitle ?? "";
        _fileName = fileName ?? "";
        _isDeposition = isDeposition;
        _settingsLine = settingsLine ?? "";
        _fileOpen = fileOpen;
        _estimateUsd = Math.Max(0m, estimateUsd);
        InitializeUI();
    }

    /// <summary>
    /// Guidance for the regenerate prompt, trimmed and cut to MaxGuidanceLength characters (empty when none).
    /// </summary>
    public string Guidance => Truncate(_txtGuidance.Text.Trim(), MaxGuidanceLength);

    // Test seams
    internal TextBox GuidanceBox => _txtGuidance;
    internal bool WarningShown => _lblWarning != null;

    private static string Truncate(string text, int max) => text.Length <= max ? text : text[..max];

    private void InitializeUI()
    {
        // Rows per UI-SPEC S2; every label here may show a guide name, so none turns '&' into a mnemonic
        AddHeadline(DiscoveryUiText.RegenerateHeadline(_shortTitle));
        AddBody(DiscoveryUiText.RegenerateBody(_fileName, _isDeposition));

        if (_settingsLine.Length > 0)
        {
            var settings = CreateWrappingLabel(_settingsLine, Theme.SmallFont(), Theme.TextMuted);
            settings.Margin = new Padding(0, 0, 0, LDU(12));
            AddContent(settings);
        }

        var caption = CreateWrappingLabel(DiscoveryUiText.GuidanceCaption, Theme.SmallFont(), Theme.TextMuted);
        caption.Margin = new Padding(0, 0, 0, LDU(4));
        AddContent(caption);

        _txtGuidance = new TextBox
        {
            PlaceholderText = DiscoveryUiText.GuidancePlaceholder(_isDeposition),
            MaxLength = MaxGuidanceLength
        };
        var guidanceHost = TextFieldHost.Create(this, _txtGuidance, lines: 3);
        // Same as RegenerateTabDialog: the edit control scrolls with the caret, and a stock scrollbar would read as unthemed chrome
        _txtGuidance.ScrollBars = ScrollBars.None;
        guidanceHost.Dock = DockStyle.Fill;
        guidanceHost.Margin = Padding.Empty;
        AddContent(guidanceHost);

        if (_fileOpen)
        {
            _lblWarning = CreateWrappingLabel(DiscoveryUiText.FileOpenWarning(_fileName), Theme.SmallFont(), Theme.Warning);
            _lblWarning.Margin = new Padding(0, LDU(12), 0, 0);
            AddContent(_lblWarning);
        }

        var estimate = CreateWrappingLabel(DiscoveryUiText.DialogEstimate(_estimateUsd), Theme.SmallFont(), Theme.TextMuted);
        estimate.Margin = new Padding(0, LDU(12), 0, LDU(16));
        AddContent(estimate);

        _btnCancel = ButtonHelper.CreateButton("Cancel", 0, LDU(34), ButtonStyle.Default);
        _btnCancel.DialogResult = DialogResult.Cancel;
        _btnRegenerate = ButtonHelper.CreateButton(DiscoveryUiText.RegenerateTitle, 0, LDU(34), ButtonStyle.Primary);
        _btnRegenerate.DialogResult = DialogResult.OK;
        AddButtons(_btnCancel, _btnRegenerate);

        FinishLayout();

        // Focal point (UI-SPEC S2): the guidance field, not the Cancel button AddButtons focuses
        ActiveControl = _txtGuidance;
    }
}
