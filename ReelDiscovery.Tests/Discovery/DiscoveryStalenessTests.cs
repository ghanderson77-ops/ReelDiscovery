using ReelDiscovery.Models.Case;
using ReelDiscovery.Models.Corpus;
using ReelDiscovery.Models.Discovery;
using ReelDiscovery.Services.Discovery;
using ReelDiscovery.Tests.Case;

namespace ReelDiscovery.Tests.Discovery;

public class DiscoveryStalenessTests
{
    private static (CaseDraft Draft, DiscoveryDocument Doc) Stamped(bool withCorpus = true)
    {
        var draft = CaseTestData.SampleCounselDraft();
        draft.Corpus = new CorpusState { PlanId = Guid.NewGuid() };
        var corpus = withCorpus ? new DiscoveryCorpus { Usable = true, PlanId = draft.Corpus.PlanId } : DiscoveryCorpus.None("No corpus");
        var doc = new DiscoveryDocument { Key = "RFP", Kind = DiscoveryKind.RequestsForProduction, Written = new WrittenDiscoveryContent() };
        DiscoveryStaleness.Stamp(doc, draft, corpus);
        return (draft, doc);
    }

    [Fact]
    public void Unchanged_IsCurrent()
    {
        var (draft, doc) = Stamped();

        Assert.NotEmpty(doc.GuideHash);
        Assert.NotEmpty(doc.CoreHash);
        Assert.NotEmpty(doc.CaptionHash);
        Assert.Equal(draft.Corpus!.PlanId, doc.CorpusPlanId);
        Assert.Equal(StaleSource.None, DiscoveryStaleness.Check(doc, draft));
    }

    [Fact]
    public void FactEdit_IsGuide()
    {
        var (draft, doc) = Stamped();
        draft.Guide!.Facts[0].Title += " (revised)";

        Assert.Equal(StaleSource.Guide, DiscoveryStaleness.Check(doc, draft));
    }

    [Fact]
    public void SectionEdit_IsCore()
    {
        var (draft, doc) = Stamped();
        var section = draft.CoreDocument!.Sections.First(s => s.Paragraphs.Count > 0);
        section.Paragraphs[0] += " Additional allegation.";

        Assert.Equal(StaleSource.Core, DiscoveryStaleness.Check(doc, draft));
    }

    [Fact]
    public void CounselEdit_IsCaption()
    {
        var (draft, doc) = Stamped();
        draft.CoreDocument!.Counsel.AttorneyName = "Someone Else";

        Assert.Equal(StaleSource.Caption, DiscoveryStaleness.Check(doc, draft));
    }

    [Fact]
    public void NewPlan_IsCorpusPlan()
    {
        var (draft, doc) = Stamped();
        draft.Corpus!.PlanId = Guid.NewGuid();

        Assert.Equal(StaleSource.CorpusPlan, DiscoveryStaleness.Check(doc, draft));
    }

    [Fact]
    public void NoCorpusAtStamp_IgnoresPlanChanges()
    {
        var (draft, doc) = Stamped(withCorpus: false);
        Assert.Null(doc.CorpusPlanId);

        draft.Corpus!.PlanId = Guid.NewGuid();

        Assert.Equal(StaleSource.None, DiscoveryStaleness.Check(doc, draft));
    }

    [Fact]
    public void DocumentWithoutContent_IsNeverStale()
    {
        var (draft, doc) = Stamped();
        doc.Written = null;
        draft.CoreDocument!.Counsel.AttorneyName = "Someone Else";

        Assert.Equal(StaleSource.None, DiscoveryStaleness.Check(doc, draft));
    }

    [Fact]
    public void FirstDifferingSource_Wins()
    {
        var (draft, doc) = Stamped();
        draft.CoreDocument!.Counsel.AttorneyName = "Someone Else";
        draft.Guide!.Facts[0].Title += " (revised)";

        Assert.Equal(StaleSource.Guide, DiscoveryStaleness.Check(doc, draft));
    }
}
