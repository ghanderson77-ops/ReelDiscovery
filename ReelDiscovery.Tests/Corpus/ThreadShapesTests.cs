using ReelDiscovery.Models;
using ReelDiscovery.Models.Corpus;
using ReelDiscovery.Services.Corpus;

namespace ReelDiscovery.Tests.Corpus;

public class ThreadShapesTests
{
    private const int Draws = 10_000;

    private static double Pct(int count) => count * 100.0 / Draws;

    private static void AssertNear(double expected, double actual, double tolerance = 3.0) =>
        Assert.InRange(actual, expected - tolerance, expected + tolerance);

    [Fact]
    public void SampleDepth_Business_MatchesTable()
    {
        var random = new Random(42);
        var depths = Enumerable.Range(0, Draws).Select(_ => ThreadShapes.SampleDepth(ThreadShapeClass.Business, random)).ToList();

        AssertNear(30, Pct(depths.Count(d => d == 1)));
        AssertNear(28, Pct(depths.Count(d => d == 2)));
        AssertNear(15, Pct(depths.Count(d => d == 3)));
        AssertNear(10, Pct(depths.Count(d => d == 4)));
        AssertNear(9, Pct(depths.Count(d => d is 5 or 6)));
        AssertNear(5, Pct(depths.Count(d => d is >= 7 and <= 9)));
        AssertNear(3, Pct(depths.Count(d => d is >= 10 and <= 12)));
        Assert.InRange(depths.Average(), 2.6, 3.2);
        Assert.All(depths, d => Assert.InRange(d, 1, ThreadShapes.MaxThreadDepth));
    }

    [Fact]
    public void SampleDepth_Ancillary_MatchesTable()
    {
        var random = new Random(42);
        var depths = Enumerable.Range(0, Draws).Select(_ => ThreadShapes.SampleDepth(ThreadShapeClass.Ancillary, random)).ToList();

        AssertNear(45, Pct(depths.Count(d => d == 1)));
        AssertNear(30, Pct(depths.Count(d => d == 2)));
        AssertNear(12, Pct(depths.Count(d => d == 3)));
        AssertNear(7, Pct(depths.Count(d => d == 4)));
        AssertNear(4, Pct(depths.Count(d => d is 5 or 6)));
        AssertNear(2, Pct(depths.Count(d => d is >= 7 and <= 9)));
        Assert.Equal(0, depths.Count(d => d >= 10));
    }

    [Fact]
    public void SampleDepth_Bulk_AlwaysOne()
    {
        var random = new Random(42);
        Assert.All(Enumerable.Range(0, 1_000), _ => Assert.Equal(1, ThreadShapes.SampleDepth(ThreadShapeClass.Bulk, random)));
    }

    [Fact]
    public void SampleKind_MatchesMix()
    {
        var random = new Random(42);
        var kinds = Enumerable.Range(0, Draws).Select(_ => ThreadShapes.SampleKind(3, random)).ToList();

        AssertNear(55, Pct(kinds.Count(k => k == MessageKind.Reply)));
        AssertNear(30, Pct(kinds.Count(k => k == MessageKind.ReplyAll)));
        AssertNear(15, Pct(kinds.Count(k => k == MessageKind.Forward)));
    }

    [Fact]
    public void SampleKind_SingleRecipient_NeverReplyAll()
    {
        var random = new Random(42);
        var kinds = Enumerable.Range(0, Draws).Select(_ => ThreadShapes.SampleKind(1, random)).ToList();

        Assert.DoesNotContain(MessageKind.ReplyAll, kinds);
        AssertNear(85, Pct(kinds.Count(k => k == MessageKind.Reply)));
    }

    [Fact]
    public void SampleRootRecipientCounts_MatchTable()
    {
        var random = new Random(42);
        var to = Enumerable.Range(0, Draws).Select(_ => ThreadShapes.SampleRootToCount(random)).ToList();
        var cc = Enumerable.Range(0, Draws).Select(_ => ThreadShapes.SampleRootCcCount(random)).ToList();

        AssertNear(65, Pct(to.Count(n => n == 1)));
        AssertNear(25, Pct(to.Count(n => n == 2)));
        AssertNear(10, Pct(to.Count(n => n is 3 or 4)));
        Assert.All(to, n => Assert.InRange(n, 1, 4));

        AssertNear(50, Pct(cc.Count(n => n == 0)));
        AssertNear(30, Pct(cc.Count(n => n == 1)));
        AssertNear(20, Pct(cc.Count(n => n is 2 or 3)));
        Assert.All(cc, n => Assert.InRange(n, 0, 3));
    }

    [Fact]
    public void SampleReplyToSeq_MostlyPrevious()
    {
        var random = new Random(42);
        var parents = Enumerable.Range(0, Draws).Select(_ => ThreadShapes.SampleReplyToSeq(5, random)).ToList();

        AssertNear(80, Pct(parents.Count(p => p == 4)));
        Assert.All(parents, p => Assert.InRange(p, 1, 4));
        Assert.Equal(0, ThreadShapes.SampleReplyToSeq(1, random));
        Assert.Equal(1, ThreadShapes.SampleReplyToSeq(2, random));
    }

    [Fact]
    public void SampleLatencyMinutes_Internal_MatchesMixture()
    {
        var random = new Random(42);
        var latencies = Enumerable.Range(0, Draws).Select(_ => ThreadShapes.SampleLatencyMinutes(false, random)).ToList();

        AssertNear(33, Pct(latencies.Count(m => m is >= 2 and < 25)));
        AssertNear(33, Pct(latencies.Count(m => m is >= 25 and < 240)));
        AssertNear(26, Pct(latencies.Count(m => m is >= 240 and < 480)));
        AssertNear(8, Pct(latencies.Count(m => m is >= 480 and <= 960)));
        Assert.All(latencies, m => Assert.InRange(m, 2, 960));
    }

    [Fact]
    public void SampleLatencyMinutes_External_IsOneAndAHalfTimes()
    {
        var a = new Random(42);
        var b = new Random(42);
        for (var i = 0; i < 1_000; i++)
        {
            var inside = ThreadShapes.SampleLatencyMinutes(false, a);
            var outside = ThreadShapes.SampleLatencyMinutes(true, b);
            Assert.Equal((int)Math.Round(inside * 1.5, MidpointRounding.AwayFromZero), outside);
        }
    }

    [Fact]
    public void SampleRootTime_InsideWindow()
    {
        var random = new Random(42);
        for (var i = 0; i < Draws; i++)
        {
            var t = ThreadShapes.SampleRootTime(random);
            Assert.True(t >= new TimeOnly(9, 0) && t < new TimeOnly(16, 45), t.ToString());
        }
    }

    [Fact]
    public void SampleAttachmentType_OnlyEnabledTypes()
    {
        var random = new Random(42);
        var enabled = new[] { AttachmentType.Word, AttachmentType.Excel };
        var seen = Enumerable.Range(0, 2_000).Select(_ => ThreadShapes.SampleAttachmentType(enabled, true, random)).ToList();

        Assert.All(seen, t => Assert.Contains(t!.Value, enabled));
        Assert.Contains(AttachmentType.Word, seen.Select(t => t!.Value));
        Assert.Contains(AttachmentType.Excel, seen.Select(t => t!.Value));
    }

    [Fact]
    public void SampleAttachmentType_CalendarOnlyOnRoot()
    {
        var random = new Random(42);
        var all = Enum.GetValues<AttachmentType>();
        var nonRoot = Enumerable.Range(0, 2_000).Select(_ => ThreadShapes.SampleAttachmentType(all, false, random)).ToList();
        var root = Enumerable.Range(0, 2_000).Select(_ => ThreadShapes.SampleAttachmentType(all, true, random)).ToList();

        Assert.DoesNotContain(AttachmentType.CalendarInvite, nonRoot.Select(t => t!.Value));
        Assert.Contains(AttachmentType.CalendarInvite, root.Select(t => t!.Value));
        Assert.Null(ThreadShapes.SampleAttachmentType(new[] { AttachmentType.CalendarInvite }, false, random));
    }

    [Fact]
    public void SampleAttachmentType_EmptyEnabled_ReturnsNull()
    {
        Assert.Null(ThreadShapes.SampleAttachmentType(Array.Empty<AttachmentType>(), true, new Random(42)));
    }

    [Fact]
    public void SampleAttachmentCount_MatchesTable()
    {
        var random = new Random(42);
        var counts = Enumerable.Range(0, Draws).Select(_ => ThreadShapes.SampleAttachmentCount(random)).ToList();

        AssertNear(80, Pct(counts.Count(n => n == 1)));
        AssertNear(15, Pct(counts.Count(n => n == 2)));
        AssertNear(5, Pct(counts.Count(n => n == 3)));
    }

    [Fact]
    public void RecipientsFor_Reply_ToParentSender()
    {
        var parent = new PlannedMessage { FromId = "A", ToIds = new() { "B" }, CcIds = new() { "C" } };
        var random = new Random(42);
        var withCc = 0;
        for (var i = 0; i < 2_000; i++)
        {
            var (to, cc) = ThreadShapes.RecipientsFor(MessageKind.Reply, parent, "B", null, random);
            Assert.Equal(new[] { "A" }, to);
            Assert.True(cc.Count == 0 || cc.SequenceEqual(new[] { "C" }));
            if (cc.Count > 0)
                withCc++;
        }

        Assert.InRange(withCc * 100.0 / 2_000, 21, 29);
    }

    [Fact]
    public void RecipientsFor_ReplyAll_CopiesEveryoneButSelf()
    {
        var parent = new PlannedMessage { FromId = "A", ToIds = new() { "B", "D" }, CcIds = new() { "C" } };
        var (to, cc) = ThreadShapes.RecipientsFor(MessageKind.ReplyAll, parent, "B", null, new Random(42));

        Assert.Equal(new[] { "A", "D" }, to);
        Assert.Equal(new[] { "C" }, cc);
    }

    [Fact]
    public void RecipientsFor_ReplyAll_WithoutOthers_DegradesToReply()
    {
        var parent = new PlannedMessage { FromId = "A", ToIds = new() { "B" } };
        var (to, cc) = ThreadShapes.RecipientsFor(MessageKind.ReplyAll, parent, "B", null, new Random(42));

        Assert.Equal(new[] { "A" }, to);
        Assert.Empty(cc);
        Assert.Equal(MessageKind.Reply, ThreadShapes.ResolveKind(MessageKind.ReplyAll, parent, "B", null));
    }

    [Fact]
    public void RecipientsFor_Forward_ToTarget()
    {
        var parent = new PlannedMessage { FromId = "A", ToIds = new() { "B" }, CcIds = new() { "C" } };
        var (to, cc) = ThreadShapes.RecipientsFor(MessageKind.Forward, parent, "B", "E", new Random(42));

        Assert.Equal(new[] { "E" }, to);
        Assert.Empty(cc);
    }

    [Fact]
    public void RecipientsFor_Forward_NoTarget_FallsBackToReply()
    {
        var parent = new PlannedMessage { FromId = "A", ToIds = new() { "B" } };
        var (to, _) = ThreadShapes.RecipientsFor(MessageKind.Forward, parent, "B", null, new Random(42));

        Assert.Equal(new[] { "A" }, to);
        Assert.Equal(MessageKind.Reply, ThreadShapes.ResolveKind(MessageKind.Forward, parent, "B", null));
    }

    [Fact]
    public void SkeletonContext_ActiveRanges()
    {
        var ctx = SkeletonContext.Create(CorpusTestData.RichDraft(), new CorpusSettings(), false, new Random(42));

        Assert.False(ctx.IsActive("CH-009", new DateOnly(2024, 6, 20)));
        Assert.True(ctx.IsActive("CH-009", new DateOnly(2024, 6, 1)));
        Assert.True(ctx.IsActive("CH-001", new DateOnly(2024, 6, 20)));
        Assert.True(ctx.IsActive("EXT-001", new DateOnly(2024, 6, 20)));
    }

    [Fact]
    public void SkeletonContext_EmptyActiveRange_IsActive()
    {
        var draft = CorpusTestData.RichDraft();
        var member = draft.Guide!.Cast.Single(c => c.Id == "CH-002");
        member.ActiveFrom = string.Empty;
        member.ActiveTo = string.Empty;
        var ctx = SkeletonContext.Create(draft, new CorpusSettings(), false, new Random(42));

        Assert.True(ctx.IsActive("CH-002", new DateOnly(1990, 1, 1)));
        Assert.True(ctx.IsActive("CH-002", new DateOnly(2090, 1, 1)));
    }

    [Fact]
    public void SkeletonContext_Zones()
    {
        var ctx = SkeletonContext.Create(CorpusTestData.RichDraft(), new CorpusSettings(), false, new Random(42));

        Assert.Equal("America/Phoenix", ctx.ZoneFor("CH-003").Id);
        Assert.Equal("America/Chicago", ctx.ZoneFor("CH-001").Id);
        Assert.Equal("America/New_York", ctx.ZoneFor("EXT-004").Id);
        Assert.Equal("America/New_York", ctx.ZoneFor("nobody").Id);
        Assert.True(ctx.IsExternalTo("CH-001", "CH-003"));
        Assert.False(ctx.IsExternalTo("CH-001", "CH-002"));
    }

    [Fact]
    public void SkeletonContext_FactsWindowAndMedia()
    {
        var draft = CorpusTestData.RichDraft();
        var settings = CorpusSettings.CreateDefault(draft);
        settings.AttachmentPercent = null;
        var ctx = SkeletonContext.Create(draft, settings, false, new Random(42));

        Assert.Equal(8, ctx.DatedFacts.Count);
        Assert.Equal(ctx.DatedFacts.OrderBy(f => f.Date, StringComparer.Ordinal).Select(f => f.Id), ctx.DatedFacts.Select(f => f.Id));
        Assert.Equal(new DateOnly(2023, 12, 15), ctx.WindowStart);
        Assert.Equal(new DateOnly(2024, 9, 4), ctx.WindowEnd);
        Assert.Equal(ThreadShapes.DefaultAttachmentPercent, ctx.AttachmentPercent);
        Assert.DoesNotContain(AttachmentType.Image, ctx.EnabledTypes);
        Assert.Contains(AttachmentType.Image, SkeletonContext.Create(draft, settings, true, new Random(42)).EnabledTypes);
    }

    [Fact]
    public void SkeletonContext_NullGuide_DoesNotThrow()
    {
        var draft = CorpusTestData.RichDraft();
        draft.Guide = null;
        var ctx = SkeletonContext.Create(draft, new CorpusSettings(), false, new Random(42));

        Assert.Empty(ctx.DatedFacts);
        Assert.Empty(ctx.CastById);
        Assert.True(ctx.WindowStart <= ctx.WindowEnd);
    }
}
