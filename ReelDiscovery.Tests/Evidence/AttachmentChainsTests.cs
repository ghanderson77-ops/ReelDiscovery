using ReelDiscovery.Models;
using ReelDiscovery.Models.Corpus;
using ReelDiscovery.Services.Evidence;
using ReelDiscovery.Tests.Corpus;
using Xunit;

namespace ReelDiscovery.Tests.Evidence;

public class AttachmentChainsTests
{
    private static PlannedAttachment Excel(CorpusPlan plan, string threadId) =>
        AttachmentTestData.AttachmentsOf(plan, threadId).Single(a => a.Kind == AttachmentType.Excel);

    [Fact]
    public void Compute_SameOrganizationSameStem_FormsTwoVersionChain()
    {
        var (draft, plan) = AttachmentTestData.MixedCase();

        var chains = AttachmentChains.Compute(plan, draft.Guide);

        var first = Excel(plan, AttachmentTestData.RelevantDocThreadId);
        var second = Excel(plan, AttachmentTestData.ChainThreadId);
        Assert.True(chains.TryGetValue(first.DocId, out var v1));
        Assert.True(chains.TryGetValue(second.DocId, out var v2));
        Assert.Equal(1, v1!.Version);
        Assert.Equal(2, v2!.Version);
        Assert.Equal(2, v1.Count);
        Assert.Equal(2, v2.Count);
        Assert.Equal(string.Empty, v1.PreviousDocId);
        Assert.Equal(first.DocId, v2.PreviousDocId);
        Assert.Equal(v1.ChainId, v2.ChainId);
        Assert.Equal(AttachmentTestData.PricingBackupTitle, v1.BaseTitle);
        Assert.Equal(AttachmentTestData.PricingBackupTitle, v2.BaseTitle);
    }

    [Fact]
    public void Compute_SameTitleOtherOrganization_IsNotLinked()
    {
        var (draft, plan) = AttachmentTestData.MixedCase();

        var chains = AttachmentChains.Compute(plan, draft.Guide);

        var other = Excel(plan, AttachmentTestData.OtherOrgDocThreadId);
        var harmon = chains[Excel(plan, AttachmentTestData.RelevantDocThreadId).DocId];
        if (chains.TryGetValue(other.DocId, out var link))
            Assert.NotEqual(harmon.ChainId, link.ChainId);
        Assert.DoesNotContain(chains.Values, l => l.PreviousDocId == other.DocId);
        Assert.Equal(2, chains.Values.Count(l => l.ChainId == harmon.ChainId));
    }

    [Theory]
    [InlineData("Pricing Backup FINAL")]
    [InlineData("pricing backup (draft) 2024-03-01")]
    [InlineData("Pricing_Backup_v3")]
    [InlineData("Pricing Backup - Revised 3/1/2024")]
    [InlineData("Pricing Backup rev 2 (copy)")]
    public void NormalizeStem_StripsVersionTokensDatesAndPunctuation(string title)
    {
        Assert.Equal("pricing backup", AttachmentChains.NormalizeStem(title));
    }

    [Fact]
    public void NormalizeStem_BlankIsEmpty()
    {
        Assert.Equal(string.Empty, AttachmentChains.NormalizeStem(null));
        Assert.Equal(string.Empty, AttachmentChains.NormalizeStem("  v2 FINAL  "));
    }

    [Fact]
    public void Compute_WordAndExcelWithSameStem_AreSeparateChains()
    {
        var (draft, plan) = AttachmentTestData.MixedCase();
        var chainRoot = AttachmentTestData.Thread(plan, AttachmentTestData.ChainThreadId).Messages[0];
        chainRoot.Attachments.Add(new PlannedAttachment
        {
            DocId = chainRoot.DocId + ".0002",
            Kind = AttachmentType.Word,
            Title = AttachmentTestData.PricingBackupTitle,
            FileName = AttachmentTestData.PricingBackupTitle + ".docx"
        });

        var chains = AttachmentChains.Compute(plan, draft.Guide);

        Assert.False(chains.ContainsKey(chainRoot.DocId + ".0002"));
        Assert.Equal(2, chains[Excel(plan, AttachmentTestData.ChainThreadId).DocId].Count);
    }

    [Fact]
    public void OrganizationKey_CastExternalUnknownAndNoGuide()
    {
        var (draft, plan) = AttachmentTestData.MixedCase();
        var people = PeopleDirectory.Create(draft.Guide!, plan);

        Assert.Equal(draft.Guide!.FindCast("CH-004")!.OrganizationId, AttachmentChains.OrganizationKey(people, "CH-004"));
        Assert.Equal("ext:summitglazingsupply.com", AttachmentChains.OrganizationKey(people, EvidenceTestData.VendorId));
        Assert.Equal("unknown", AttachmentChains.OrganizationKey(people, "EXT-999"));
        Assert.Equal("from:CH-004", AttachmentChains.OrganizationKey(null, "CH-004"));
    }

    [Fact]
    public void Compute_NoGuide_ScopesByFromId()
    {
        var (_, plan) = AttachmentTestData.MixedCase();

        var chains = AttachmentChains.Compute(plan, null);

        // CH-004 and CH-008 share an organization but not a sender id
        Assert.False(chains.ContainsKey(Excel(plan, AttachmentTestData.RelevantDocThreadId).DocId));
        Assert.False(chains.ContainsKey(Excel(plan, AttachmentTestData.ChainThreadId).DocId));
    }

    [Fact]
    public void Compute_SingleAndNonRelevantAttachments_HaveNoEntry()
    {
        var (draft, plan) = AttachmentTestData.MixedCase();

        var chains = AttachmentChains.Compute(plan, draft.Guide);

        var nonRelevant = plan.Threads.Where(t => t.Relevance != ThreadRelevance.Relevant)
            .SelectMany(t => t.Messages).SelectMany(m => m.Attachments).Select(a => a.DocId);
        Assert.All(nonRelevant, id => Assert.False(chains.ContainsKey(id), id));
        var memo = AttachmentTestData.AttachmentsOf(plan, AttachmentTestData.RelevantDocThreadId).Single(a => a.Kind == AttachmentType.Word);
        Assert.False(chains.ContainsKey(memo.DocId));
        Assert.Equal(2, chains.Count);
    }

    [Fact]
    public void Compute_IsPureAndIndependentOfThreadOrder()
    {
        var (draft, plan) = AttachmentTestData.MixedCase();
        var reversed = PlanFixtures.Clone(plan);
        reversed.Threads.Reverse();

        var a = AttachmentChains.Compute(plan, draft.Guide);
        var b = AttachmentChains.Compute(plan, draft.Guide);
        var c = AttachmentChains.Compute(reversed, draft.Guide);

        foreach (var other in new[] { b, c })
        {
            Assert.Equal(a.Keys.OrderBy(k => k, StringComparer.Ordinal), other.Keys.OrderBy(k => k, StringComparer.Ordinal));
            foreach (var (id, link) in a)
            {
                var o = other[id];
                Assert.Equal((link.ChainId, link.Version, link.Count, link.PreviousDocId, link.OrganizationKey, link.BaseTitle),
                    (o.ChainId, o.Version, o.Count, o.PreviousDocId, o.OrganizationKey, o.BaseTitle));
            }
        }
    }
}
