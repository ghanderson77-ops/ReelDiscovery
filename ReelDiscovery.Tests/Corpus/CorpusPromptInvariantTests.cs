using System.Text;
using System.Text.RegularExpressions;
using ReelDiscovery.Models.Case;
using ReelDiscovery.Services.Ai;
using ReelDiscovery.Services.Case;
using ReelDiscovery.Services.Corpus;
using ReelDiscovery.Tests.Case;

namespace ReelDiscovery.Tests.Corpus;

public class CorpusPromptInvariantTests
{
    [Fact]
    public void SystemPrompt_KeepsNamesAndTakesFirmPosition()
    {
        Assert.Contains("including characters and companies from films, TV series, books or games", CorpusPlanPrompts.SystemPrompt);
        Assert.Contains("Do not hedge", CorpusPlanPrompts.SystemPrompt);
    }

    [Fact]
    public void SystemPrompt_NeverSaysFictionalOrJson()
    {
        Assert.DoesNotContain("fictional", CorpusPlanPrompts.SystemPrompt, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotMatch(new Regex("json", RegexOptions.IgnoreCase), CorpusPlanPrompts.SystemPrompt);
    }

    [Fact]
    public void SystemPrompt_AsksForPlainBusinessLanguage()
    {
        Assert.Contains("plain business language", CorpusPlanPrompts.SystemPrompt);
        Assert.Contains("no legal terms", CorpusPlanPrompts.SystemPrompt);
    }

    [Fact]
    public void BuildProtocolPrompt_CarriesIssuesCounselAndNameRules()
    {
        var draft = CorpusTestData.RichDraft();
        var guide = draft.Guide!;

        var prompt = CorpusPlanPrompts.BuildProtocolPrompt(draft);

        foreach (var issue in guide.Issues)
        {
            Assert.Contains(issue.Id, prompt);
            Assert.Contains(issue.Title, prompt);
            Assert.Contains(issue.RelevanceCriteria, prompt);
        }

        foreach (var counsel in guide.Cast.Where(c => c.Role is CastRole.InHouseCounsel or CastRole.OutsideCounsel))
            Assert.Contains(counsel.FullName, prompt);
        foreach (var firm in guide.Organizations.Where(o => o.Kind == OrganizationKind.LawFirm))
            Assert.Contains(firm.Name, prompt);

        var nameRules = new StringBuilder();
        CaseGuidePrompts.AppendNameRules(nameRules, draft);
        Assert.Contains(nameRules.ToString().Trim(), prompt);
        Assert.Contains(CaseFolderNaming.CaseTitle(draft), prompt);
    }

    [Fact]
    public void BuildProtocolPrompt_HasNoPlanDataOrAnswerKeyFields()
    {
        var prompt = CorpusPlanPrompts.BuildProtocolPrompt(CorpusTestData.RichDraft());

        Assert.DoesNotContain("T-0", prompt);
        Assert.DoesNotMatch(new Regex(@"[A-Z]{2,6}-\d{6}"), prompt);
        Assert.DoesNotContain("near-miss", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("decoy", prompt, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildProtocolPrompt_HasNoJsonInstructions()
    {
        var request = new LlmProviderRequest
        {
            SystemPrompt = CorpusPlanPrompts.SystemPrompt,
            UserPrompt = CorpusPlanPrompts.BuildProtocolPrompt(CorpusTestData.RichDraft())
        };

        CaseTestData.AssertNoJsonInstructions(request);
        Assert.DoesNotMatch(new Regex("json", RegexOptions.IgnoreCase), request.UserPrompt);
    }

    [Fact]
    public void AppendCast_ListsIdNameTitleOrganizationAndRole()
    {
        var guide = CorpusTestData.RichDraft().Guide!;
        var sb = new StringBuilder();

        CorpusPlanPrompts.AppendCast(sb, guide);

        var text = sb.ToString();
        Assert.Contains("CH-001 Maria Delgado, President, Brightline Glazing, LLC (Party)", text);
        Assert.Contains("CH-005 Karen Walsh", text);
        Assert.Contains("CH-008 Priya Nair", text);
    }

    [Fact]
    public void AppendCast_OnlyIds_FiltersMembers()
    {
        var guide = CorpusTestData.RichDraft().Guide!;
        var sb = new StringBuilder();

        CorpusPlanPrompts.AppendCast(sb, guide, new[] { "CH-001" });

        var text = sb.ToString();
        Assert.Contains("CH-001 Maria Delgado", text);
        Assert.DoesNotContain("CH-002", text);
    }

    [Fact]
    public void AppendValues_ListsLabelEqualsValue()
    {
        var guide = CorpusTestData.RichDraft().Guide!;
        var sb = new StringBuilder();

        CorpusPlanPrompts.AppendValues(sb, guide);

        Assert.Contains("Change Order 7 amount = $184,250.00", sb.ToString());
    }

    [Fact]
    public void AppendCaseSynopsis_ListsIssuesWithCriteria()
    {
        var draft = CorpusTestData.RichDraft();
        var sb = new StringBuilder();

        CorpusPlanPrompts.AppendCaseSynopsis(sb, draft);

        var issue = draft.Guide!.Issues[0];
        Assert.Contains($"{issue.Id} {issue.Title}: {issue.RelevanceCriteria}", sb.ToString());
        Assert.Contains(CaseFolderNaming.CaseTitle(draft), sb.ToString());
    }
}
