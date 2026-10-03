using ReelDiscovery.Models.Case;
using ReelDiscovery.Models.Corpus;
using ReelDiscovery.Services.Evidence;
using ReelDiscovery.Tests.Corpus;

namespace ReelDiscovery.Tests.Evidence;

public class KnowledgeGateTests
{
    private static (CaseGuide Guide, PlannedThread Thread) RichSingle(string factId)
    {
        var draft = CorpusTestData.RichDraft();
        var plan = PlanFixtures.ValidPlan(draft);
        return (draft.Guide!, PlanFixtures.Thread(plan, factId));
    }

    private static (CaseGuide Guide, PlannedThread Thread) HotThread()
    {
        var (draft, plan) = EvidenceTestData.ApprovedCase();
        return (draft.Guide!, PlanFixtures.Thread(plan, "F-003"));
    }

    [Fact]
    public void Build_ExcludesFutureFactsAndFactsNoParticipantKnows()
    {
        // CH-004 -> CH-002 on 2024-03-19
        var (guide, thread) = RichSingle("F-003");

        var knowledge = KnowledgeGate.Build(guide, thread);
        var ids = knowledge.Facts.Select(f => f.Id).ToList();

        Assert.Equal(new[] { "F-002", "F-003" }, ids);
        foreach (var future in new[] { "F-004", "F-005", "F-006", "F-007", "F-008" })
            Assert.DoesNotContain(future, ids);

        // F-001 is dated earlier, but its knowers (CH-001, CH-003, CH-005) are not on the thread
        Assert.DoesNotContain("F-001", ids);
        Assert.Equal(new DateOnly(2024, 3, 19), knowledge.LastDate);
        Assert.Equal(new DateOnly(2024, 3, 19), knowledge.FirstDate);
        Assert.Equal(new[] { "CH-002", "CH-004" }, knowledge.Participants.OrderBy(p => p, StringComparer.Ordinal));
    }

    [Fact]
    public void KnowersOf_FallsBackToActorsThenPublic()
    {
        var known = new KeyFact { Id = "F-1", KnownByIds = new() { "CH-001" }, ActorIds = new() { "CH-002" } };
        var actors = new KeyFact { Id = "F-2", ActorIds = new() { "CH-002" } };
        var open = new KeyFact { Id = "F-3" };

        Assert.Equal(new[] { "CH-001" }, KnowledgeGate.KnowersOf(known));
        Assert.Equal(new[] { "CH-002" }, KnowledgeGate.KnowersOf(actors));
        Assert.Empty(KnowledgeGate.KnowersOf(open));

        var day = new DateOnly(2024, 1, 1);
        Assert.True(KnowledgeGate.KnowsFact(actors, "CH-002", day));
        Assert.False(KnowledgeGate.KnowsFact(actors, "CH-001", day));
        Assert.True(KnowledgeGate.KnowsFact(open, "CH-999", day));
    }

    [Fact]
    public void Build_IncludesPublicAndUndatedFacts()
    {
        var (guide, thread) = RichSingle("F-003");
        guide.Facts.Add(new KeyFact { Id = "F-020", Date = "2024-01-01", Title = "Public" });
        guide.Facts.Add(new KeyFact { Id = "F-021", Date = "", Title = "Undated", KnownByIds = new() { "CH-004" } });
        guide.Facts.Add(new KeyFact { Id = "F-022", Date = "2024-12-01", Title = "Future public" });
        guide.Facts.Add(new KeyFact { Id = "F-023", Date = "2024-01-01", Title = "Actors", ActorIds = new() { "CH-002" } });

        var ids = KnowledgeGate.Build(guide, thread).Facts.Select(f => f.Id).ToList();

        Assert.Contains("F-020", ids);
        Assert.Contains("F-021", ids);
        Assert.Contains("F-023", ids);
        Assert.DoesNotContain("F-022", ids);
    }

    [Fact]
    public void Build_AlwaysIncludesThreadPlanFacts()
    {
        // F-008 is known by CH-008 only and dated 2024-07-04; a thread carrying it between other people still sees it
        var (guide, thread) = RichSingle("F-003");
        thread.FactIds.Add("F-008");

        var ids = KnowledgeGate.Build(guide, thread).Facts.Select(f => f.Id).ToList();

        Assert.Contains("F-008", ids);
    }

    [Fact]
    public void MayMention_HoldsOnlySenderKnowledgePlusMessageFacts()
    {
        var (guide, thread) = HotThread();

        var knowledge = KnowledgeGate.Build(guide, thread);

        // Seq 2: CH-002 replies (quoted basis, no own facts); CH-002 knows F-002 and F-003 by then
        Assert.Equal(new[] { "F-002", "F-003" }, knowledge.MayMentionBySeq[2]);

        // Seq 4: CH-002 forwards to CH-001; F-001 (CH-001's) is not CH-002's knowledge
        Assert.DoesNotContain("F-001", knowledge.MayMentionBySeq[4]);
        Assert.Contains("F-003", knowledge.MayMentionBySeq[4]);

        // The forward brings CH-001 in, so F-001 is thread-level knowledge
        Assert.Contains("F-001", knowledge.Facts.Select(f => f.Id));
    }

    [Fact]
    public void MayMention_IncludesMessagePlanFactsTheSenderDoesNotKnow()
    {
        var (guide, thread) = HotThread();
        thread.Messages.Single(m => m.Seq == 2).FactIds.Add("F-008");

        var knowledge = KnowledgeGate.Build(guide, thread);

        Assert.Contains("F-008", knowledge.MayMentionBySeq[2]);
        Assert.DoesNotContain("F-008", knowledge.MayMentionBySeq[3]);
    }

    [Fact]
    public void Knowability_UsesEarliestLinkedFactAndUnionOfKnowers()
    {
        var (guide, _) = RichSingle("F-003");
        var value = new CanonicalValue { Id = "V-010", Value = "late notice", FactIds = new() { "F-007" } };

        var knowable = KnowledgeGate.Knowability(value, guide);

        Assert.Equal(new DateOnly(2024, 6, 1), knowable.KnowableFrom);
        Assert.Equal(new[] { "CH-001", "CH-005" }, knowable.KnownBy.OrderBy(k => k, StringComparer.Ordinal));
        Assert.False(knowable.IsPublic);
        Assert.False(KnowledgeGate.KnowsValue(knowable, "CH-001", new DateOnly(2024, 5, 31)));
        Assert.True(KnowledgeGate.KnowsValue(knowable, "CH-001", new DateOnly(2024, 6, 1)));
        Assert.False(KnowledgeGate.KnowsValue(knowable, "CH-004", new DateOnly(2024, 12, 31)));

        var multi = KnowledgeGate.Knowability(new CanonicalValue { Id = "V-011", FactIds = new() { "F-008", "F-007" } }, guide);
        Assert.Equal(new DateOnly(2024, 6, 1), multi.KnowableFrom);
        Assert.Equal(new[] { "CH-001", "CH-005", "CH-008" }, multi.KnownBy.OrderBy(k => k, StringComparer.Ordinal));
    }

    [Fact]
    public void Knowability_NoLinkedFactsIsPublic()
    {
        var (guide, _) = RichSingle("F-003");
        var knowable = KnowledgeGate.Knowability(new CanonicalValue { Id = "V-012", Value = "Lakeview Tower" }, guide);

        Assert.True(knowable.IsPublic);
        Assert.Null(knowable.KnowableFrom);
        Assert.True(KnowledgeGate.KnowsValue(knowable, "CH-999", DateOnly.MinValue));
        Assert.True(KnowledgeGate.KnowsValue(knowable, "EXT-001", new DateOnly(2030, 1, 1)));
    }

    [Fact]
    public void Knowability_LinkedPublicFactMakesValuePublic()
    {
        var (guide, _) = RichSingle("F-003");
        guide.Facts.Add(new KeyFact { Id = "F-030", Date = "2024-02-01" });
        var knowable = KnowledgeGate.Knowability(new CanonicalValue { Id = "V-013", FactIds = new() { "F-030", "F-007" } }, guide);

        Assert.True(knowable.IsPublic);
        Assert.Equal(new DateOnly(2024, 2, 1), knowable.KnowableFrom);
    }

    [Fact]
    public void Build_ValuesOnlyThoseAParticipantKnowsByLastDate()
    {
        var (guide, thread) = RichSingle("F-003");
        guide.Values.Add(new CanonicalValue { Id = "V-010", Value = "late notice", FactIds = new() { "F-007" } });

        var knowledge = KnowledgeGate.Build(guide, thread);
        var ids = knowledge.Values.Select(v => v.Value.Id).ToList();

        // V-001/V-002 link F-003 (CH-004, CH-002 know on 2024-03-18); V-003 links F-001 (no participant); V-004 links F-005 (future)
        Assert.Contains("V-001", ids);
        Assert.Contains("V-002", ids);
        Assert.DoesNotContain("V-003", ids);
        Assert.DoesNotContain("V-004", ids);
        Assert.DoesNotContain("V-010", ids);
        Assert.Contains("V-001", knowledge.KnownValueIdsBySeq[1]);
    }

    [Fact]
    public void Ancestors_FollowsReplyChainRootFirst()
    {
        var (_, thread) = HotThread();

        Assert.Equal(new[] { 1, 2, 3 }, KnowledgeGate.Ancestors(thread, 4));
        Assert.Empty(KnowledgeGate.Ancestors(thread, 1));
        Assert.Equal(new[] { 1, 2, 3 }, KnowledgeGate.Build(new CaseGuide(), thread).AncestorsBySeq[4]);
    }

    [Fact]
    public void Ancestors_StopsAtMissingSeqAndCycles()
    {
        var thread = new PlannedThread { Id = "T-0001" };
        thread.Messages.Add(new PlannedMessage { Seq = 1, ReplyToSeq = 9 });
        thread.Messages.Add(new PlannedMessage { Seq = 2, ReplyToSeq = 1 });
        thread.Messages.Add(new PlannedMessage { Seq = 3, ReplyToSeq = 4 });
        thread.Messages.Add(new PlannedMessage { Seq = 4, ReplyToSeq = 3 });

        Assert.Equal(new[] { 1 }, KnowledgeGate.Ancestors(thread, 2));
        var cycle = KnowledgeGate.Ancestors(thread, 3);
        Assert.Equal(new[] { 4 }, cycle);
        Assert.Empty(KnowledgeGate.Ancestors(thread, 42));
    }

    [Fact]
    public void Build_IsNullSafe()
    {
        var guide = new CaseGuide { Facts = null!, Values = null!, Cast = null! };
        var thread = new PlannedThread { Id = "T-0001", FactIds = null! };

        var knowledge = KnowledgeGate.Build(guide, thread);

        Assert.Empty(knowledge.Facts);
        Assert.Empty(knowledge.Values);
        Assert.Empty(knowledge.Participants);
    }
}
