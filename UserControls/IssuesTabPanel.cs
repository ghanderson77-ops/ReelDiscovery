using ReelDiscovery.Helpers;
using ReelDiscovery.Models.Case;
using ReelDiscovery.Services.Case;

namespace ReelDiscovery.UserControls;

/// <summary>
/// Issues tab (read-only, D-08): issues mirror the core document's counts or findings, so they are
/// changed by editing the core document, not here.
/// </summary>
public class IssuesTabPanel : GuideTabPanel
{
    private const int MaxLinkedFacts = 8;
    private const string FooterText = "Issues come from the core document. To change one, go Back and edit the core document.";

    private readonly ThemedButton _btnRetryDetails;
    private readonly Control[] _tabActions;

    public IssuesTabPanel()
        : base(GuidePart.Issues, "Issues", "Issue", hasToolbar: false, guidancePlaceholder: string.Empty)
    {
        _btnRetryDetails = (ThemedButton)ButtonHelper.CreateButton("Retry Issue Details", 0, LogicalToDeviceUnits(34), ButtonStyle.Default);
        _btnRetryDetails.Margin = new Padding(0);
        _btnRetryDetails.Visible = false;
        _tabActions = new Control[] { _btnRetryDetails };

        // Wire handlers only after every control exists
        _btnRetryDetails.Click += BtnRetryDetails_Click;
    }

    public override IReadOnlyList<Control> TabActions => _tabActions;

    protected override bool GridEditable => false;

    protected override void BuildColumns()
    {
        Grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Id", HeaderText = "ID", Width = LogicalToDeviceUnits(64) });
        Grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Source", HeaderText = "Source", Width = LogicalToDeviceUnits(88) });
        Grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Issue",
            HeaderText = "Issue",
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
            MinimumWidth = LogicalToDeviceUnits(160)
        });
        // Int cell values sort numerically; right-aligned with the card's 15 px right inset
        var facts = new DataGridViewTextBoxColumn { Name = "Facts", HeaderText = "Facts", Width = LogicalToDeviceUnits(56) };
        facts.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
        facts.DefaultCellStyle.Padding = new Padding(0, 0, LogicalToDeviceUnits(15), 0);
        facts.HeaderCell.Style.Alignment = DataGridViewContentAlignment.MiddleRight;
        facts.HeaderCell.Style.Padding = new Padding(0, LogicalToDeviceUnits(6), LogicalToDeviceUnits(15) - GridStyle.HeaderTextMargin, LogicalToDeviceUnits(6));
        Grid.Columns.Add(facts);
    }

    private CaseGuide? Guide => IsAttached ? Host.Draft.Guide : null;

    private CoreDocument? Document => IsAttached ? Host.Draft.CoreDocument : null;

    protected override void RefreshHeader()
    {
        CountLabel.Text = CaseGuideUiText.IssuesSourceText(Document?.DocumentType == CoreDocumentType.InvestigationMemo);
        // Any issue without details can be retried: Failed, or Pending/left Generating after a cancel, fatal error or close (CR-02).
        // Hidden while this run is writing them.
        var guide = Guide;
        _btnRetryDetails.Visible = guide != null
            && (guide.IssueDetailsState.Status == GuidePartStatus.Failed || CaseGuideGenerator.IssueDetailsMissing(guide))
            && !(Busy && guide.IssueDetailsState.Status == GuidePartStatus.Generating);
        _btnRetryDetails.Enabled = !Busy;
    }

    protected override IEnumerable<(string Id, object?[] Cells)> BuildRows()
    {
        var guide = Guide;
        if (guide == null)
            yield break;

        foreach (var issue in guide.Issues)
            yield return (issue.Id, new object?[] { issue.Id, SourceLabel(issue), issue.Title, LinkedFacts(guide, issue.Id).Count });
    }

    protected override void ShowDetail(string? id)
    {
        Fields.SuspendLayout();
        ClearFields();
        var guide = Guide;
        var issue = guide?.FindIssue(id);
        if (guide == null || issue == null)
        {
            DetailHeading.Text = string.Empty;
            DetailMeta.Text = string.Empty;
            Fields.ResumeLayout(true);
            return;
        }

        var facts = LinkedFacts(guide, issue.Id);
        DetailHeading.Text = issue.Title;
        DetailMeta.Text = $"{issue.Id} · {CaseGuideUiText.Plural(facts.Count, "fact", "facts")}";

        var source = SourceLabel(issue);
        AddCaptionedLabel("Source", string.IsNullOrWhiteSpace(issue.Title) ? source : $"{source} — {issue.Title}");
        AddCaptionedLabel("Against", ValueOrDash(AgainstText(issue)));
        AddCaptionedLabel("Legal basis", ValueOrDash(issue.LegalBasis));
        // The count's summary; the enrichment description stands in when the count had none
        AddCaptionedLabel("Summary", ValueOrDash(string.IsNullOrWhiteSpace(issue.Summary) ? issue.Description : issue.Summary));
        AddCaptionedLines("What must be proven", issue.Elements.Where(e => !string.IsNullOrWhiteSpace(e)).Select(e => "• " + e.Trim()));
        AddCaptionedLabel("What relevant evidence looks like", ValueOrDash(issue.RelevanceCriteria));
        AddCaptionedLines("Linked facts", LinkedFactLines(facts));
        AddNote(FooterText);
        Fields.ResumeLayout(true);
    }

    private string SourceLabel(CaseIssue issue)
    {
        var doc = Document;
        return doc != null ? IssueExtractor.SourceLabel(doc, issue) : $"Count {issue.Number}";
    }

    private string AgainstText(CaseIssue issue)
    {
        var parties = Document?.Parties ?? new List<CaseParty>();
        var names = issue.AgainstPartyIds
            .Select(pid => parties.FirstOrDefault(p => p.Id == pid)?.Name)
            .Where(n => !string.IsNullOrWhiteSpace(n));
        return string.Join(", ", names);
    }

    private static List<KeyFact> LinkedFacts(CaseGuide guide, string issueId) =>
        guide.Facts.Where(f => f.IssueIds.Contains(issueId)).ToList();

    private static List<string> LinkedFactLines(List<KeyFact> facts)
    {
        if (facts.Count == 0)
            return new List<string> { CaseGuideUiText.LinkedFactsText(0) };

        // Chronological; facts without a date sort last
        var ordered = facts
            .OrderBy(f => string.IsNullOrEmpty(f.Date) ? 1 : 0)
            .ThenBy(f => f.Date, StringComparer.Ordinal)
            .ThenBy(f => f.Id, StringComparer.Ordinal)
            .ToList();
        var lines = ordered.Take(MaxLinkedFacts)
            .Select(f => string.IsNullOrEmpty(f.Date) ? $"{f.Id} · {f.Title}" : $"{f.Id} · {f.Date} · {f.Title}")
            .ToList();
        if (ordered.Count > MaxLinkedFacts)
            lines.Add(CaseGuideUiText.MoreLinkedText(ordered.Count - MaxLinkedFacts));
        return lines;
    }

    private static string ValueOrDash(string? text) => string.IsNullOrWhiteSpace(text) ? "—" : text.Trim();

    private async void BtnRetryDetails_Click(object? sender, EventArgs e)
    {
        if (!IsAttached || Host.IsBusy)
            return;

        try
        {
            await Host.RetryIssueDetailsAsync();
        }
        catch (Exception ex)
        {
            // The host reports its own failures; this only catches a fault before it could
            if (!IsDisposed)
                Host.SetStatus(ex.InnerException != null ? $"Error: {ex.Message} ({ex.InnerException.Message})" : $"Error: {ex.Message}", Theme.Danger);
        }
    }
}
