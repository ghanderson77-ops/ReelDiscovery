using System.Globalization;
using ReelDiscovery.Models;
using ReelDiscovery.Models.Case;
using ReelDiscovery.Models.Corpus;
using ReelDiscovery.Models.Evidence;
using ReelDiscovery.Services.Corpus;
using ReelDiscovery.Services.Evidence;
using Xunit;

namespace ReelDiscovery.Tests.Evidence;

public class InviteSchedulerTests
{
    private const string Zone = "America/New_York";
    private const string MeetingDay = "2024-03-12";
    private const string KfId = "F-090";
    private const string HiddenFactId = "F-091";

    private static readonly TimeZoneInfo Tz = BusinessCalendar.ResolveId(Zone)!;
    private static readonly DateOnly Meeting = new(2024, 3, 12);

    // ---------- fixtures ----------

    private static CaseGuide Guide()
    {
        var guide = EvidenceTestData.ApprovedCase().Draft.Guide!;
        guide.Facts.Add(new KeyFact
        {
            Id = KfId,
            Date = MeetingDay,
            Title = "Upper floor site walk",
            Description = "The parties walk the upper floors together.",
            ActorIds = new List<string> { "CH-004" },
            KnownByIds = new List<string> { "CH-004", "CH-002", "CH-001" }
        });
        guide.Facts.Add(new KeyFact
        {
            Id = HiddenFactId,
            Date = MeetingDay,
            Title = "Foreman walk",
            Description = "Only the foreman knew.",
            ActorIds = new List<string> { "CH-009" },
            KnownByIds = new List<string> { "CH-009" }
        });
        return guide;
    }

    // Linear thread in New York: seq i replies to i-1, two hours apart from 10:00 local on day; DocIds DM-{docBase}{seq:00}
    private static PlannedThread NewThread(string id, DateOnly day, int count, string docBase, params string[] factIds)
    {
        var thread = new PlannedThread
        {
            Id = id,
            Relevance = ThreadRelevance.Relevant,
            Subject = "Upper floor site walk",
            FactIds = factIds.ToList()
        };
        for (var seq = 1; seq <= count; seq++)
        {
            var odd = seq % 2 == 1;
            thread.Messages.Add(new PlannedMessage
            {
                Id = $"{id}-M{seq:00}",
                DocId = $"DM-{docBase}{seq:00}",
                Seq = seq,
                ReplyToSeq = seq - 1,
                Kind = seq == 1 ? MessageKind.New : MessageKind.Reply,
                FromId = odd ? "CH-004" : "CH-002",
                ToIds = new List<string> { odd ? "CH-002" : "CH-004" },
                CcIds = new List<string> { "CH-001" },
                SentAt = BusinessCalendar.At(Tz, day, new TimeOnly(10, 0)).AddHours(2 * (seq - 1)),
                TimeZoneId = Zone
            });
        }

        return thread;
    }

    private static PlannedMessage Msg(PlannedThread thread, int seq) => thread.Messages.Single(m => m.Seq == seq);

    private static PlannedAttachment Invite(PlannedMessage parent) => new()
    {
        DocId = parent.DocId + ".0001",
        Kind = AttachmentType.CalendarInvite,
        Title = "Site Walk Meeting",
        Description = "Walk the upper floors together."
    };

    private static LedgerDelta MeetingDelta(int seq, string about = "Site walk", string value = MeetingDay,
        string statement = "The teams will walk the upper floors.", params string[] facts) => new()
    {
        Seq = seq,
        Kind = LedgerKind.Meeting,
        About = about,
        Statement = statement,
        Value = value,
        FactIds = facts.ToList()
    };

    private static InviteRequest Request(CaseGuide guide, PlannedThread thread, int seq, IReadOnlyList<LedgerDelta>? own = null,
        IReadOnlyList<LedgerEntry>? view = null, int wave = 1, IReadOnlyDictionary<string, InviteSlot>? known = null)
    {
        var parent = Msg(thread, seq);
        return new InviteRequest
        {
            Thread = thread,
            Parent = parent,
            Invite = Invite(parent),
            OwnDeltas = own ?? Array.Empty<LedgerDelta>(),
            LedgerView = view ?? Array.Empty<LedgerEntry>(),
            Guide = guide,
            KnownSlots = known,
            WaveIndex = wave
        };
    }

    private static List<LedgerEntry> Ledger(CaseGuide guide, params (PlannedThread Thread, int Wave, List<LedgerDelta> Deltas)[] sources)
    {
        var plan = new CorpusPlan();
        plan.Threads.AddRange(sources.Select(s => s.Thread));
        return LedgerMerger.Build(plan, guide,
            sources.ToDictionary(s => s.Thread.Id, s => s.Wave),
            sources.ToDictionary(s => s.Thread.Id, s => s.Deltas)).Entries;
    }

    private static DateOnly LocalDate(DateTimeOffset instant) => BusinessCalendar.LocalDate(instant, Tz);

    private static TimeOnly LocalTime(DateTimeOffset instant) => TimeOnly.FromDateTime(TimeZoneInfo.ConvertTime(instant, Tz).DateTime);

    // Thread A's invite from its own deltas; thread B (wave 2, same people) sees the Build ledger of A
    private static (InviteSlot A, InviteSlot B, List<LedgerEntry> View) CrossThread(CaseGuide guide, PlannedThread a, int inviteSeq,
        List<LedgerDelta> deltas)
    {
        var slotA = InviteScheduler.Resolve(Request(guide, a, inviteSeq, deltas));
        var view = Ledger(guide, (a, 1, deltas));
        var b = NewThread("T-0802", new DateOnly(2024, 3, 7), 1, "8020");
        var slotB = InviteScheduler.Resolve(Request(guide, b, 1, view: view, wave: 2));

        Assert.Equal(InviteSource.OwnDelta, slotA.Source);
        Assert.Equal(InviteSource.Ledger, slotB.Source);
        Assert.Equal(slotA.MeetingKey, slotB.MeetingKey);
        Assert.Equal(slotA.Start, slotB.Start);
        Assert.Equal(slotA.End, slotB.End);
        return (slotA, slotB, view);
    }

    private static string BuildKey(CaseGuide guide, LedgerEntry entry) =>
        InviteScheduler.MeetingKeyFor(entry, InviteScheduler.MeetingDate(entry.Value, entry.Statement)!.Value, guide);

    // ---------- ledger candidates ----------

    [Fact]
    public void Ledger_SiteWalk_UsesValueNotSourceDate()
    {
        var (draft, plan) = AttachmentTestData.MixedCase();
        var guide = draft.Guide!;
        var entry = AttachmentTestData.SiteWalkMeeting(plan);
        var thread = AttachmentTestData.Thread(plan, AttachmentTestData.InviteThreadId);
        var parent = thread.Messages[0];

        var slot = InviteScheduler.Resolve(new InviteRequest
        {
            Thread = thread,
            Parent = parent,
            Invite = parent.Attachments.Single(a => a.Kind == AttachmentType.CalendarInvite),
            LedgerView = new[] { entry },
            Guide = guide,
            WaveIndex = 2
        });

        var tz = BusinessCalendar.ResolveId(parent.TimeZoneId)!;
        var value = DateOnly.ParseExact(entry.Value, "yyyy-MM-dd", CultureInfo.InvariantCulture);
        Assert.Equal(InviteSource.Ledger, slot.Source);
        Assert.Equal(value, BusinessCalendar.LocalDate(slot.Start, tz));
        Assert.NotEqual(entry.ParsedDate, BusinessCalendar.LocalDate(slot.Start, tz));
        Assert.Equal(entry.Id, slot.MatchedId);
        Assert.Equal(InviteScheduler.MeetingKeyFor(entry, value, guide), slot.MeetingKey);
        Assert.NotEqual(entry.Id, slot.MeetingKey);
        Assert.False(slot.Warning);
    }

    [Fact]
    public void Ledger_KnownByDisjoint_NotMatched()
    {
        var (draft, plan) = AttachmentTestData.MixedCase();
        var entry = AttachmentTestData.SiteWalkMeeting(plan);
        entry.KnownByIds = new List<string> { "CH-009" };
        var thread = AttachmentTestData.Thread(plan, AttachmentTestData.InviteThreadId);
        var parent = thread.Messages[0];

        var slot = InviteScheduler.Resolve(new InviteRequest
        {
            Thread = thread,
            Parent = parent,
            Invite = parent.Attachments.Single(a => a.Kind == AttachmentType.CalendarInvite),
            LedgerView = new[] { entry },
            Guide = draft.Guide!
        });

        Assert.NotEqual(InviteSource.Ledger, slot.Source);
        Assert.NotEqual(entry.Id, slot.MatchedId);
    }

    [Fact]
    public void Ledger_TopicIncompatible_NotMatched()
    {
        var (draft, plan) = AttachmentTestData.MixedCase();
        var entry = AttachmentTestData.SiteWalkMeeting(plan);
        entry.About = "budget review";
        entry.Statement = "Quarterly budget numbers get reviewed.";
        entry.FactIds = new List<string>();
        var thread = AttachmentTestData.Thread(plan, AttachmentTestData.InviteThreadId);
        var parent = thread.Messages[0];

        var slot = InviteScheduler.Resolve(new InviteRequest
        {
            Thread = thread,
            Parent = parent,
            Invite = parent.Attachments.Single(a => a.Kind == AttachmentType.CalendarInvite),
            LedgerView = new[] { entry },
            Guide = draft.Guide!
        });

        Assert.NotEqual(InviteSource.Ledger, slot.Source);
        Assert.NotEqual(entry.Id, slot.MatchedId);
    }

    [Fact]
    public void MixedCase_TwoInvitesForOneMeeting_SameSchedule()
    {
        var (draft, plan) = AttachmentTestData.MixedCase();
        var guide = draft.Guide!;
        var entry = AttachmentTestData.SiteWalkMeeting(plan);

        InviteSlot For(string threadId)
        {
            var thread = AttachmentTestData.Thread(plan, threadId);
            var parent = thread.Messages[0];
            return InviteScheduler.Resolve(new InviteRequest
            {
                Thread = thread,
                Parent = parent,
                Invite = parent.Attachments.Single(a => a.Kind == AttachmentType.CalendarInvite),
                LedgerView = new[] { entry },
                Guide = guide,
                WaveIndex = 2
            });
        }

        var first = For(AttachmentTestData.InviteThreadId);
        var second = For(AttachmentTestData.SecondInviteThreadId);

        Assert.Equal(InviteSource.Ledger, first.Source);
        Assert.Equal(first.MeetingKey, second.MeetingKey);
        Assert.Equal(first.Start, second.Start);
        Assert.Equal(first.End, second.End);
    }

    [Fact]
    public void KnownSlots_StoredScheduleWins()
    {
        var (draft, plan) = AttachmentTestData.MixedCase();
        var guide = draft.Guide!;
        var entry = AttachmentTestData.SiteWalkMeeting(plan);
        var thread = AttachmentTestData.Thread(plan, AttachmentTestData.SecondInviteThreadId);
        var parent = thread.Messages[0];
        var request = new InviteRequest
        {
            Thread = thread,
            Parent = parent,
            Invite = parent.Attachments.Single(),
            LedgerView = new[] { entry },
            Guide = guide
        };
        var key = InviteScheduler.Resolve(request).MeetingKey;
        var stored = new InviteSlot
        {
            Start = new DateTimeOffset(2024, 5, 13, 15, 45, 0, TimeSpan.FromHours(-5)),
            End = new DateTimeOffset(2024, 5, 13, 16, 30, 0, TimeSpan.FromHours(-5))
        };
        request.KnownSlots = new Dictionary<string, InviteSlot> { [key] = stored };

        var slot = InviteScheduler.Resolve(request);

        Assert.Equal(stored.Start, slot.Start);
        Assert.Equal(stored.End, slot.End);
        Assert.Equal(key, slot.MeetingKey);
    }

    // ---------- own deltas ----------

    [Fact]
    public void OwnDelta_ParentSeq_KeyedByNormalizedEntry()
    {
        var guide = Guide();
        var a = NewThread("T-0801", new DateOnly(2024, 3, 5), 1, "8010");
        var delta = MeetingDelta(1);

        var slot = InviteScheduler.Resolve(Request(guide, a, 1, new[] { delta }));

        var entry = LedgerMerger.EntriesFor(a, new[] { delta }, guide, 1).Single();
        Assert.Equal(InviteSource.OwnDelta, slot.Source);
        Assert.Equal($"mtg:{a.Id}:{Msg(a, 1).DocId}:{LedgerMerger.DetailKey(entry)}", slot.MeetingKey);
        Assert.Equal($"{a.Id}:d1", slot.MatchedId);
        Assert.Equal(Meeting, LocalDate(slot.Start));
    }

    [Fact]
    public void OwnDelta_AttachmentDelta_KeyedByTheEntrySourceDoc()
    {
        var guide = Guide();
        var a = NewThread("T-0801", new DateOnly(2024, 3, 5), 1, "8010");
        var delta = MeetingDelta(1, about: "Mockup walk-through");
        delta.FromAttachment = true;
        delta.AttachmentDocId = Msg(a, 1).DocId + ".0002";

        var slot = InviteScheduler.Resolve(Request(guide, a, 1, new List<LedgerDelta> { MeetingDelta(1), delta }));
        var candidates = InviteScheduler.OwnDeltaCandidates(Request(guide, a, 1, new List<LedgerDelta> { MeetingDelta(1), delta }));

        // The key follows the entry's SourceDocId (the attachment DocId once 06-08 lands), whatever EntriesFor assigns
        var built = Ledger(guide, (a, 1, new List<LedgerDelta> { MeetingDelta(1), delta }));
        var attachmentEntry = built.Single(e => e.About == "Mockup walk-through");
        Assert.Contains(candidates, c => c.MeetingKey == BuildKey(guide, attachmentEntry));
        Assert.Equal(InviteSource.OwnDelta, slot.Source);
    }

    [Fact]
    public void OwnDelta_SiblingBranch_IsNeverACandidate()
    {
        var guide = Guide();
        var a = NewThread("T-0801", new DateOnly(2024, 3, 5), 3, "8010");
        Msg(a, 3).ReplyToSeq = 1;

        var request = Request(guide, a, 3, new[] { MeetingDelta(2) });
        var slot = InviteScheduler.Resolve(request);

        Assert.Empty(InviteScheduler.OwnDeltaCandidates(request));
        Assert.Equal(InviteSource.Fallback, slot.Source);
    }

    // ---------- adversarial normalization (checker 06-05 final) ----------

    [Fact]
    public void Key_OwnDeltaIdTokenInAbout_MatchesLedger()
    {
        var guide = Guide();
        var a = NewThread("T-0801", new DateOnly(2024, 3, 5), 1, "8010");
        var deltas = new List<LedgerDelta> { MeetingDelta(1, about: "Site walk F-012 with CH-003") };

        var (slotA, _, view) = CrossThread(guide, a, 1, deltas);

        Assert.Equal("Site walk with", view.Single().About);
        Assert.Equal(BuildKey(guide, view.Single()), slotA.MeetingKey);
    }

    [Fact]
    public void Key_OwnDeltaNonCanonicalFactId_MatchesLedger()
    {
        var guide = Guide();
        const string spelling = " f-090 ";
        Assert.Equal(KfId, RelevantThreadFiller.CanonicalId(spelling));

        var a = NewThread("T-0801", new DateOnly(2024, 3, 5), 1, "8010", KfId);
        var (slotA, slotB, _) = CrossThread(guide, a, 1, new List<LedgerDelta> { MeetingDelta(1, facts: spelling) });
        Assert.Equal("fact:" + KfId, slotA.MeetingKey);
        Assert.Equal("fact:" + KfId, slotB.MeetingKey);

        // A fact the knowledge gate filters out never produces a "fact:" key
        var hidden = NewThread("T-0803", new DateOnly(2024, 3, 5), 1, "8030");
        var (slotH, _, view) = CrossThread(guide, hidden, 1, new List<LedgerDelta> { MeetingDelta(1, facts: HiddenFactId) });
        Assert.StartsWith("mtg:", slotH.MeetingKey);
        Assert.Empty(view.Single().FactIds);
    }

    [Fact]
    public void Key_DuplicateDeltaTwoSeqs_MatchesLedger()
    {
        var guide = Guide();

        // seq 1 cites the fact, seq 3 (the parent) repeats the detail: FactIds unioned like EntriesFor
        var a = NewThread("T-0801", new DateOnly(2024, 3, 5), 3, "8010", KfId);
        var deltas = new List<LedgerDelta> { MeetingDelta(1, facts: KfId), MeetingDelta(3) };
        var (slotA, _, view) = CrossThread(guide, a, 3, deltas);
        Assert.Equal("fact:" + KfId, slotA.MeetingKey);
        Assert.Equal(BuildKey(guide, view.Single()), slotA.MeetingKey);

        // Without the fact: the first SourceDocId (seq 1) is kept
        var plain = NewThread("T-0803", new DateOnly(2024, 3, 5), 3, "8030");
        var plainDeltas = new List<LedgerDelta> { MeetingDelta(1), MeetingDelta(3) };
        var (slotP, _, plainView) = CrossThread(guide, plain, 3, plainDeltas);
        var plainEntry = LedgerMerger.EntriesFor(plain, plainDeltas, guide, 1).Single();
        Assert.Equal($"mtg:{plain.Id}:{Msg(plain, 1).DocId}:{LedgerMerger.DetailKey(plainEntry)}", slotP.MeetingKey);
        Assert.Equal(BuildKey(guide, plainView.Single()), slotP.MeetingKey);

        // The first copy on a sibling branch: the parent's own copy is the candidate, keyed by the Build entry
        var sibling = NewThread("T-0804", new DateOnly(2024, 3, 5), 3, "8040");
        Msg(sibling, 3).ReplyToSeq = 1;
        var siblingDeltas = new List<LedgerDelta> { MeetingDelta(2), MeetingDelta(3) };
        var (slotS, _, siblingView) = CrossThread(guide, sibling, 3, siblingDeltas);
        Assert.Equal(Msg(sibling, 2).DocId, siblingView.Single().SourceDocId);
        Assert.Equal(BuildKey(guide, siblingView.Single()), slotS.MeetingKey);
        Assert.Equal($"{sibling.Id}:d3", slotS.MatchedId);
    }

    [Fact]
    public void Key_LaterThreadDuplicateOfEarlierWaveEntry_UsesEarlierKey()
    {
        var guide = Guide();
        var a = NewThread("T-0801", new DateOnly(2024, 3, 5), 1, "8010");
        var b = NewThread("T-0802", new DateOnly(2024, 3, 6), 1, "8020");
        var c = NewThread("T-0803", new DateOnly(2024, 3, 7), 1, "8030");
        var deltasA = new List<LedgerDelta> { MeetingDelta(1) };
        var deltasB = new List<LedgerDelta> { MeetingDelta(1, statement: "We walk the upper floors next week.") };

        var slotA = InviteScheduler.Resolve(Request(guide, a, 1, deltasA));
        var viewB = Ledger(guide, (a, 1, deltasA));
        var e = viewB.Single();
        var slotB = InviteScheduler.Resolve(Request(guide, b, 1, deltasB, viewB, wave: 2));

        Assert.Equal(InviteSource.OwnDelta, slotB.Source);
        Assert.Equal(BuildKey(guide, e), slotB.MeetingKey);
        Assert.Equal(slotA.MeetingKey, slotB.MeetingKey);
        Assert.Equal(slotA.Start, slotB.Start);
        Assert.Equal(slotA.End, slotB.End);

        var viewC = Ledger(guide, (a, 1, deltasA), (b, 2, deltasB));
        Assert.Single(viewC);
        var slotC = InviteScheduler.Resolve(Request(guide, c, 1, view: viewC, wave: 3));
        Assert.Equal(InviteSource.Ledger, slotC.Source);
        Assert.Equal(slotA.MeetingKey, slotC.MeetingKey);
        Assert.Equal(slotA.Start, slotC.Start);
        Assert.Equal(slotA.End, slotC.End);
    }

    [Fact]
    public void Key_EqualsBuildEntryKey_ForEveryOwnDelta()
    {
        var checkedCount = 0;
        foreach (var (guide, threads) in ParityScenarios())
        {
            var all = threads.Select(t => (t.Thread, t.Wave, t.Deltas)).ToArray();
            var full = Ledger(guide, all);
            foreach (var (thread, wave, deltas) in threads)
            {
                var earlier = Ledger(guide, all.Where(x => x.Wave < wave).ToArray());
                foreach (var message in thread.Messages)
                {
                    var visible = KnowledgeGate.Ancestors(thread, message.Seq).Append(message.Seq).ToHashSet();
                    var candidates = InviteScheduler.OwnDeltaCandidates(new InviteRequest
                    {
                        Thread = thread,
                        Parent = message,
                        Invite = Invite(message),
                        OwnDeltas = deltas,
                        LedgerView = earlier,
                        Guide = guide,
                        WaveIndex = wave
                    });

                    foreach (var delta in deltas.Where(d => d.Kind == LedgerKind.Meeting && visible.Contains(d.Seq)))
                    {
                        var normalized = LedgerMerger.EntriesFor(thread, new[] { delta }, guide, wave).Single();
                        var holder = full.First(e => e.Kind == LedgerKind.Meeting && LedgerMerger.SameDetail(e, normalized));
                        var expected = BuildKey(guide, holder);
                        Assert.Contains(candidates, c => c.MatchedId == $"{thread.Id}:d{delta.Seq}"
                            && LedgerMerger.SameDetail(c.Entry!, normalized) && c.MeetingKey == expected);
                        checkedCount++;
                    }
                }
            }
        }

        Assert.True(checkedCount >= 12, checkedCount.ToString(CultureInfo.InvariantCulture));
    }

    // Every own-delta fixture except the same-wave duplicate (documented: neither thread can know which entry Build keeps)
    private static IEnumerable<(CaseGuide Guide, List<(PlannedThread Thread, int Wave, List<LedgerDelta> Deltas)> Threads)> ParityScenarios()
    {
        var (draft, plan) = AttachmentTestData.MixedCase();
        var first = AttachmentTestData.Thread(plan, AttachmentTestData.InviteThreadId);
        var second = AttachmentTestData.Thread(plan, AttachmentTestData.SecondInviteThreadId);
        var siteWalk = AttachmentTestData.SiteWalkMeeting(plan).Value;
        yield return (draft.Guide!, new()
        {
            (first, 1, new List<LedgerDelta> { MeetingDelta(1, about: "site walk", value: siteWalk, facts: "F-004") }),
            (second, 2, new List<LedgerDelta> { MeetingDelta(1, about: "Site Walk", value: siteWalk) })
        });

        var guide = Guide();
        yield return (guide, new()
        {
            (NewThread("T-0801", new DateOnly(2024, 3, 5), 1, "8010"), 1,
                new List<LedgerDelta> { MeetingDelta(1, about: "Site walk F-012 with CH-003") }),
            (NewThread("T-0802", new DateOnly(2024, 3, 5), 1, "8020", KfId), 1,
                new List<LedgerDelta> { MeetingDelta(1, about: "Kickoff walk", facts: " f-090 ") }),
            (NewThread("T-0803", new DateOnly(2024, 3, 5), 1, "8030"), 1,
                new List<LedgerDelta> { MeetingDelta(1, about: "Foreman walk", facts: HiddenFactId) }),
            (NewThread("T-0804", new DateOnly(2024, 3, 5), 3, "8040", KfId), 1,
                new List<LedgerDelta> { MeetingDelta(1, about: "Floor walk", facts: KfId), MeetingDelta(3, about: "Floor walk") }),
            (NewThread("T-0805", new DateOnly(2024, 3, 5), 3, "8050"), 1,
                new List<LedgerDelta> { MeetingDelta(1, about: "Roof walk"), MeetingDelta(3, about: "Roof walk") })
        });

        var sibling = NewThread("T-0806", new DateOnly(2024, 3, 5), 3, "8060");
        Msg(sibling, 3).ReplyToSeq = 1;
        var later = NewThread("T-0807", new DateOnly(2024, 3, 6), 1, "8070");
        yield return (guide, new()
        {
            (sibling, 1, new List<LedgerDelta> { MeetingDelta(2, about: "Lobby walk"), MeetingDelta(3, about: "Lobby walk") }),
            (NewThread("T-0808", new DateOnly(2024, 3, 5), 1, "8080"), 1, new List<LedgerDelta> { MeetingDelta(1) }),
            (later, 2, new List<LedgerDelta> { MeetingDelta(1, statement: "We walk the upper floors next week.") })
        });
    }

    [Fact]
    public void Key_SameWaveDuplicate_SameDerivedSlot()
    {
        var guide = Guide();
        var b = NewThread("T-0802", new DateOnly(2024, 3, 6), 1, "8020");
        var d = NewThread("T-0804", new DateOnly(2024, 3, 7), 1, "8040");

        var slotB = InviteScheduler.Resolve(Request(guide, b, 1, new[] { MeetingDelta(1) }, wave: 2));
        var slotD = InviteScheduler.Resolve(Request(guide, d, 1, new[] { MeetingDelta(1, statement: "Walking the upper floors.") }, wave: 2));

        // Keys may differ (neither thread can know which entry Build keeps); the content seed gives one time
        Assert.NotEqual(slotB.MeetingKey, slotD.MeetingKey);
        Assert.Equal(slotB.Start, slotD.Start);
        Assert.Equal(slotB.End, slotD.End);
    }

    [Fact]
    public void Key_OwnDeltaThenLedger_SameKeyAndSlot()
    {
        var guide = Guide();
        var a = NewThread("T-0801", new DateOnly(2024, 3, 5), 1, "8010");
        var deltas = new List<LedgerDelta> { MeetingDelta(1) };

        var (slotA, _, view) = CrossThread(guide, a, 1, deltas);

        var stored = new InviteSlot
        {
            Start = BusinessCalendar.At(Tz, Meeting, new TimeOnly(13, 0)),
            End = BusinessCalendar.At(Tz, Meeting, new TimeOnly(14, 0))
        };
        var b = NewThread("T-0802", new DateOnly(2024, 3, 7), 1, "8020");
        var slotB = InviteScheduler.Resolve(Request(guide, b, 1, view: view, wave: 2,
            known: new Dictionary<string, InviteSlot> { [slotA.MeetingKey] = stored }));
        Assert.Equal(stored.Start, slotB.Start);
        Assert.Equal(stored.End, slotB.End);
        Assert.Equal(slotA.MeetingKey, slotB.MeetingKey);
    }

    [Fact]
    public void Key_FactThenLedger_SameKeyAndSlot()
    {
        var guide = Guide();
        var x = NewThread("T-0809", new DateOnly(2024, 3, 6), 1, "8090", KfId);
        var slotX = InviteScheduler.Resolve(Request(guide, x, 1));
        Assert.Equal(InviteSource.Fact, slotX.Source);
        Assert.Equal("fact:" + KfId, slotX.MeetingKey);
        Assert.Equal(Meeting, LocalDate(slotX.Start));

        var a = NewThread("T-0801", new DateOnly(2024, 3, 5), 1, "8010", KfId);
        var view = Ledger(guide, (a, 1, new List<LedgerDelta> { MeetingDelta(1, facts: KfId) }));
        var b = NewThread("T-0802", new DateOnly(2024, 3, 7), 1, "8020");
        var slotB = InviteScheduler.Resolve(Request(guide, b, 1, view: view, wave: 2));
        Assert.Equal(InviteSource.Ledger, slotB.Source);
        Assert.Equal("fact:" + KfId, slotB.MeetingKey);
        Assert.Equal(slotX.Start, slotB.Start);
        Assert.Equal(slotX.End, slotB.End);

        // Citing the fact with another date is a different event
        var other = Ledger(guide, (a, 1, new List<LedgerDelta> { MeetingDelta(1, value: "2024-03-14", facts: KfId) }));
        var slotO = InviteScheduler.Resolve(Request(guide, b, 1, view: other, wave: 2));
        Assert.Equal(InviteSource.Ledger, slotO.Source);
        Assert.StartsWith("mtg:", slotO.MeetingKey);
    }

    [Fact]
    public void Key_OwnDeltaWithFact_SameAsFact()
    {
        var guide = Guide();
        var a = NewThread("T-0801", new DateOnly(2024, 3, 5), 1, "8010", KfId);
        var slotA = InviteScheduler.Resolve(Request(guide, a, 1, new[] { MeetingDelta(1, facts: KfId) }));
        Assert.Equal(InviteSource.OwnDelta, slotA.Source);
        Assert.Equal("fact:" + KfId, slotA.MeetingKey);

        var x = NewThread("T-0809", new DateOnly(2024, 3, 6), 1, "8090", KfId);
        var slotX = InviteScheduler.Resolve(Request(guide, x, 1));
        Assert.Equal(InviteSource.Fact, slotX.Source);
        Assert.Equal(slotA.Start, slotX.Start);
        Assert.Equal(slotA.End, slotX.End);
    }

    [Fact]
    public void Candidates_SameKey_DeduplicatedKeepingFirstSource()
    {
        var guide = Guide();
        var a = NewThread("T-0801", new DateOnly(2024, 3, 5), 1, "8010", KfId);

        var candidates = InviteScheduler.Candidates(Request(guide, a, 1, new[] { MeetingDelta(1, facts: KfId) }));

        var fact = Assert.Single(candidates, c => c.MeetingKey == "fact:" + KfId);
        Assert.Equal(InviteSource.OwnDelta, fact.Source);
    }

    // ---------- times and dates ----------

    [Fact]
    public void StatedTime_InValueOrStatement_IsTheStart()
    {
        var guide = Guide();
        var a = NewThread("T-0801", new DateOnly(2024, 3, 5), 1, "8010");

        var fromValue = InviteScheduler.Resolve(Request(guide, a, 1, new[] { MeetingDelta(1, value: "2024-03-14 10:30 AM") }));
        var fromStatement = InviteScheduler.Resolve(Request(guide, a, 1,
            new[] { MeetingDelta(1, value: "2024-03-14", statement: "The site walk at 2:00 PM on the upper floors.") }));

        Assert.Equal(BusinessCalendar.At(Tz, new DateOnly(2024, 3, 14), new TimeOnly(10, 30)), fromValue.Start);
        Assert.Equal(BusinessCalendar.At(Tz, new DateOnly(2024, 3, 14), new TimeOnly(14, 0)), fromStatement.Start);
        Assert.Equal(new TimeOnly(14, 0), InviteScheduler.MeetingTime("2024-03-14", "at 14:00 sharp"));
        Assert.Equal(new TimeOnly(9, 15), InviteScheduler.MeetingTime("9:15 a.m.", null));
        Assert.Null(InviteScheduler.MeetingTime("25:99", "no time here"));
    }

    [Fact]
    public void DerivedTime_GridFromContentSeed()
    {
        var guide = Guide();
        var a = NewThread("T-0801", new DateOnly(2024, 3, 5), 1, "8010");
        var delta = MeetingDelta(1);

        var slot = InviteScheduler.Resolve(Request(guide, a, 1, new[] { delta }));

        var entry = LedgerMerger.EntriesFor(a, new[] { delta }, guide, 1).Single();
        var seed = $"mtg~{LedgerMerger.DetailKey(entry)}|{MeetingDay}";
        Assert.Equal(seed, InviteScheduler.SlotSeedFor(slot.MeetingKey, entry, Meeting));
        Assert.Equal("fact:F-001", InviteScheduler.SlotSeedFor("fact:F-001", entry, Meeting));
        var rank = RenderAssignments.StableRank(seed + ":invite-slot");
        var start = BusinessCalendar.At(Tz, Meeting, new TimeOnly(9, 0).AddMinutes(30 * (int)(rank % 14)));
        Assert.Equal(start, slot.Start);
        Assert.Equal(start.AddMinutes((rank / 14) % 2 == 0 ? 30 : 60), slot.End);
    }

    [Theory]
    [InlineData("2024-03-14", null, "2024-03-14")]
    [InlineData("March 14, 2024", null, "2024-03-14")]
    [InlineData("2024-03-14 10:30 AM", null, "2024-03-14")]
    [InlineData("next Thursday", "Site walk on 2024-03-14 at 9:00", "2024-03-14")]
    [InlineData("2024-13-45", "on 2024-03-14", "2024-03-14")]
    [InlineData("2024-02-30", null, null)]
    [InlineData("not a date", "", null)]
    [InlineData(null, null, null)]
    public void MeetingDate_ParsesValueThenStatement(string? value, string? statement, string? expected)
    {
        var date = InviteScheduler.MeetingDate(value, statement);

        Assert.Equal(expected, date?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
    }

    [Fact]
    public void SameDay_StartBeforeParent_IsRejected()
    {
        var guide = Guide();
        var a = NewThread("T-0801", Meeting, 1, "8010");
        Msg(a, 1).SentAt = BusinessCalendar.At(Tz, Meeting, new TimeOnly(16, 45));

        var rejected = InviteScheduler.Resolve(Request(guide, a, 1, new[] { MeetingDelta(1) }));
        Assert.Equal(InviteSource.Fallback, rejected.Source);
        Assert.True(rejected.Start > Msg(a, 1).SentAt);

        var next = InviteScheduler.Resolve(Request(guide, a, 1,
            new[] { MeetingDelta(1), MeetingDelta(1, about: "Site walk follow-up", value: "2024-03-13") }));
        Assert.Equal(InviteSource.OwnDelta, next.Source);
        Assert.Equal(new DateOnly(2024, 3, 13), LocalDate(next.Start));

        var evening = InviteScheduler.Resolve(Request(guide, a, 1, new[] { MeetingDelta(1, value: "2024-03-12 5:30 PM") }));
        Assert.Equal(InviteSource.OwnDelta, evening.Source);
        Assert.Equal(new TimeOnly(17, 30), LocalTime(evening.Start));
    }

    [Fact]
    public void EarlierMeetingWins_TieBySourceOrder()
    {
        var guide = Guide();
        var a = NewThread("T-0801", new DateOnly(2024, 3, 5), 1, "8010");

        var slot = InviteScheduler.Resolve(Request(guide, a, 1, new[]
        {
            MeetingDelta(1, value: "2024-03-14"),
            MeetingDelta(1, about: "Site walk prep", value: "2024-03-13")
        }));
        Assert.Equal(new DateOnly(2024, 3, 13), LocalDate(slot.Start));

        var other = NewThread("T-0802", new DateOnly(2024, 3, 4), 1, "8020");
        var view = Ledger(guide, (other, 1, new List<LedgerDelta> { MeetingDelta(1, about: "Site walk rehearsal") }));
        var tie = InviteScheduler.Resolve(Request(guide, a, 1, new[] { MeetingDelta(1) }, view, wave: 2));
        Assert.Equal(InviteSource.OwnDelta, tie.Source);
        Assert.Equal(Meeting, LocalDate(tie.Start));
    }

    [Fact]
    public void NoCandidate_FallbackNextBusinessDayWithWarning()
    {
        var guide = Guide();
        var friday = NewThread("T-0801", new DateOnly(2024, 3, 8), 1, "8010");
        var request = Request(guide, friday, 1);

        var slot = InviteScheduler.Resolve(request);

        Assert.Equal(InviteSource.Fallback, slot.Source);
        Assert.True(slot.Warning);
        Assert.Equal(InviteScheduler.NotTiedWarning, slot.WarningText);
        Assert.Equal("invite not tied to a known meeting", slot.WarningText);
        Assert.Equal(string.Empty, slot.MeetingKey);
        var rank = RenderAssignments.StableRank(request.Invite.DocId + ":invite-slot");
        var start = BusinessCalendar.At(Tz, new DateOnly(2024, 3, 11), new TimeOnly(9, 0).AddMinutes(30 * (int)(rank % 14)));
        Assert.Equal(start, slot.Start);
        Assert.True(BusinessCalendar.IsBusinessDay(LocalDate(slot.Start)));
    }

    [Fact]
    public void SaturdayMeeting_KeepsItsDate()
    {
        var guide = Guide();
        var a = NewThread("T-0801", new DateOnly(2024, 3, 5), 1, "8010");

        var slot = InviteScheduler.Resolve(Request(guide, a, 1, new[] { MeetingDelta(1, value: "2024-03-16") }));

        Assert.Equal(InviteSource.OwnDelta, slot.Source);
        Assert.Equal(new DateOnly(2024, 3, 16), LocalDate(slot.Start));
        Assert.Equal(DayOfWeek.Saturday, LocalDate(slot.Start).DayOfWeek);
    }

    [Fact]
    public void MalformedValues_NeverThrow()
    {
        var guide = Guide();
        var a = NewThread("T-0801", new DateOnly(2024, 3, 5), 1, "8010");
        var deltas = new List<LedgerDelta>
        {
            MeetingDelta(1, value: "not a date"),
            MeetingDelta(1, about: "Second", value: "2024-13-45"),
            MeetingDelta(1, about: "Third", value: "", statement: ""),
            new() { Seq = 1, Kind = LedgerKind.Meeting, About = null!, Statement = "Walk soon.", Value = null! },
            MeetingDelta(99)
        };
        var view = new List<LedgerEntry>
        {
            new() { Id = "T-0700-L01", Kind = LedgerKind.Meeting, About = "site walk", Value = "garbage", KnownByIds = new() { "CH-004" } },
            null!
        };

        var slot = InviteScheduler.Resolve(Request(guide, a, 1, deltas, view));

        Assert.Equal(InviteSource.Fallback, slot.Source);
    }

    [Fact]
    public void ForNonRelevant_BusinessSlotWithoutWarning()
    {
        var friday = NewThread("T-0801", new DateOnly(2024, 3, 8), 1, "8010");
        var parent = Msg(friday, 1);

        var slot = InviteScheduler.ForNonRelevant(parent, Invite(parent));

        Assert.Equal(InviteSource.Fallback, slot.Source);
        Assert.False(slot.Warning);
        Assert.Equal(string.Empty, slot.WarningText);
        Assert.Equal(new DateOnly(2024, 3, 11), LocalDate(slot.Start));
        Assert.True(LocalTime(slot.Start) >= new TimeOnly(9, 0));
        Assert.True(LocalTime(slot.End) <= new TimeOnly(17, 0));
    }

    [Fact]
    public void Offsets_FollowTheParentZone()
    {
        var guide = Guide();
        var a = NewThread("T-0801", new DateOnly(2024, 3, 5), 1, "8010");

        var afterDst = InviteScheduler.Resolve(Request(guide, a, 1, new[] { MeetingDelta(1) }));
        var beforeDst = InviteScheduler.Resolve(Request(guide, a, 1, new[] { MeetingDelta(1, value: "2024-03-08") }));

        Assert.Equal(TimeSpan.FromHours(-4), afterDst.Start.Offset);
        Assert.Equal(TimeSpan.FromHours(-5), beforeDst.Start.Offset);
    }
}
