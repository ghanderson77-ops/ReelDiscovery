using ReelDiscovery.Helpers;
using ReelDiscovery.UserControls;

namespace ReelDiscovery.Forms;

/// <summary>
/// D-RP: confirms re-planning the corpus (every DocID changes) and collects optional guidance that can
/// shift volume between facts and periods. Dates, people and labels still come from the case guide.
/// </summary>
public class ReplanDialog : ThemedDialog
{
    private readonly bool _approved;
    private readonly int _generatedThreads;
    private readonly int _relabels;
    private TextBox _txtGuidance = null!;
    private Button _btnCancel = null!;
    private Button _btnReplan = null!;

    /// <param name="generatedThreads">Threads already generated for the current plan; &gt; 0 adds the start-over warning.</param>
    /// <param name="relabels">Manual relabels of the current plan; &gt; 0 adds the D-08 dropped warning.</param>
    public ReplanDialog(bool approved, int generatedThreads = 0, int relabels = 0)
        : base(CorpusUiText.ReplanTitle)
    {
        _approved = approved;
        _generatedThreads = generatedThreads;
        _relabels = relabels;
        InitializeUI();
    }

    /// <summary>
    /// Guidance text for the planner prompt, trimmed (empty when none).
    /// </summary>
    public string Guidance => _txtGuidance.Text.Trim();

    private void InitializeUI()
    {
        AddHeadline(CorpusUiText.ReplanHeadline);
        AddBody(CorpusUiText.ReplanBody(_approved, _generatedThreads, _relabels));

        var caption = CreateWrappingLabel(CorpusUiText.ReplanGuidanceCaption, Theme.SmallFont(), Theme.TextMuted);
        caption.Margin = new Padding(0, 0, 0, LDU(4));
        AddContent(caption);

        _txtGuidance = new TextBox { PlaceholderText = CorpusUiText.ReplanPlaceholder };
        var guidanceHost = TextFieldHost.Create(this, _txtGuidance, lines: 3);
        // Three lines of optional guidance: the edit control still scrolls with the caret, and an
        // always-visible stock scrollbar in an empty field reads as unthemed chrome
        _txtGuidance.ScrollBars = ScrollBars.None;
        guidanceHost.Dock = DockStyle.Fill;
        guidanceHost.Margin = Padding.Empty;
        AddContent(guidanceHost);

        var hint = CreateWrappingLabel(CorpusUiText.ReplanHint, Theme.SmallFont(), Theme.TextMuted);
        hint.Margin = new Padding(0, LDU(8), 0, 0);
        AddContent(hint);

        _btnCancel = ButtonHelper.CreateButton("Cancel", 0, LDU(34), ButtonStyle.Default);
        _btnCancel.DialogResult = DialogResult.Cancel;
        _btnReplan = ButtonHelper.CreateButton("Re-plan Corpus", 0, LDU(34), ButtonStyle.Primary);
        _btnReplan.DialogResult = DialogResult.OK;
        AddButtons(_btnCancel, _btnReplan);

        // U16: the user opened the dialog to type; nothing changes until the Primary is pressed
        this.ActiveControl = _txtGuidance;

        FinishLayout();
    }
}
