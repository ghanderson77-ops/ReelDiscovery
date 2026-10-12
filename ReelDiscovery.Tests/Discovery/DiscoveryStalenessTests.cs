using ReelDiscovery.Models.Case;
using ReelDiscovery.Models.Corpus;
using ReelDiscovery.Models.Discovery;
using ReelDiscovery.Models.Evidence;
using ReelDiscovery.Services.Discovery;
using ReelDiscovery.Services.Evidence;
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

    // 09 W3: per-exhibit stamps

    private static readonly DateTime T1 = new(2026, 10, 3, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime T2 = new(2026, 10, 3, 13, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime T9 = new(2026, 10, 3, 14, 0, 0, DateTimeKind.Utc);

    private static Dictionary<string, DateTime> Written() => new(StringComparer.Ordinal)
    {
        ["RD-000001"] = T1,
        ["RD-000002"] = T2,
        ["RD-000009"] = T9
    };

    private static DiscoveryDocument Deposition() => new()
    {
        Key = "DEP-CH-003",
        Kind = DiscoveryKind.Deposition,
        Deposition = new DepositionContent
        {
            Exhibits =
            {
                new DepositionExhibit { Number = 1, DocId = "RD-000001" },
                new DepositionExhibit { Number = 2, DocId = "" }
            }
        }
    };

    private static DiscoveryDocument Rfa() => new()
    {
        Key = "RFA",
        Kind = DiscoveryKind.RequestsForAdmission,
        Written = new WrittenDiscoveryContent
        {
            Requests =
            {
                new DiscoveryRequest { Number = 1, DocId = "RD-000002" },
                new DiscoveryRequest { Number = 2, DocId = " " }
            }
        }
    };

    private static (CaseDraft Draft, DiscoveryDocument Dep, DiscoveryDocument Rfa) StampedWithExhibits(Dictionary<string, DateTime>? written)
    {
        var draft = CaseTestData.SampleCounselDraft();
        draft.Corpus = new CorpusState { PlanId = Guid.NewGuid() };
        var corpus = new DiscoveryCorpus { Usable = true, PlanId = draft.Corpus.PlanId, WrittenStamps = written };
        var dep = Deposition();
        var rfa = Rfa();
        DiscoveryStaleness.Stamp(dep, draft, corpus);
        DiscoveryStaleness.Stamp(rfa, draft, corpus);
        return (draft, dep, rfa);
    }

    [Fact]
    public void Stamp_RecordsOnlyCitedWrittenExhibits()
    {
        var (_, dep, rfa) = StampedWithExhibits(Written());

        var depStamp = Assert.Single(dep.ExhibitStamps);
        Assert.Equal("RD-000001", depStamp.Key);
        Assert.Equal(T1, depStamp.Value);
        var rfaStamp = Assert.Single(rfa.ExhibitStamps);
        Assert.Equal("RD-000002", rfaStamp.Key);
        Assert.Equal(T2, rfaStamp.Value);
    }

    [Fact]
    public void Stamp_UnknownStamps_LeavesExhibitStampsEmpty()
    {
        var (_, dep, _) = StampedWithExhibits(null);
        Assert.Empty(dep.ExhibitStamps);

        var draft = CaseTestData.SampleCounselDraft();
        var doc = Deposition();
        doc.ExhibitStamps["RD-000001"] = T1;
        DiscoveryStaleness.Stamp(doc, draft, null);
        Assert.Empty(doc.ExhibitStamps);
    }

    [Fact]
    public void Check_SameStamps_IsCurrent()
    {
        var (draft, dep, rfa) = StampedWithExhibits(Written());

        Assert.Equal(StaleSource.None, DiscoveryStaleness.Check(dep, draft));
        Assert.Equal(StaleSource.None, DiscoveryStaleness.Check(dep, draft, Written()));
        Assert.Equal(StaleSource.None, DiscoveryStaleness.Check(rfa, draft, Written()));
    }

    [Fact]
    public void Check_MissingExhibit_IsExhibits()
    {
        var (draft, dep, _) = StampedWithExhibits(Written());
        var current = Written();
        current.Remove("RD-000001");

        Assert.Equal(StaleSource.Exhibits, DiscoveryStaleness.Check(dep, draft, current));
    }

    [Fact]
    public void Check_ChangedStamp_IsExhibits()
    {
        var (draft, dep, rfa) = StampedWithExhibits(Written());
        var current = Written();
        current["RD-000001"] = T1.AddMinutes(5);

        Assert.Equal(StaleSource.Exhibits, DiscoveryStaleness.Check(dep, draft, current));
        Assert.Equal(StaleSource.None, DiscoveryStaleness.Check(rfa, draft, current));
    }

    [Fact]
    public void Check_KnownEmptyStamps_StalesOnlyCitingDocuments()
    {
        var (draft, dep, _) = StampedWithExhibits(Written());
        var empty = new Dictionary<string, DateTime>(StringComparer.Ordinal);
        var (rfpDraft, rfp) = Stamped();

        Assert.Equal(StaleSource.Exhibits, DiscoveryStaleness.Check(dep, draft, empty));
        Assert.Equal(StaleSource.None, DiscoveryStaleness.Check(rfp, rfpDraft, empty));
    }

    [Fact]
    public void Check_UnknownStamps_NeverStale()
    {
        var (draft, dep, _) = StampedWithExhibits(Written());

        Assert.Equal(StaleSource.None, DiscoveryStaleness.Check(dep, draft, null));
    }

    [Fact]
    public void Check_EarlierSourcesWinOverExhibits()
    {
        var empty = new Dictionary<string, DateTime>(StringComparer.Ordinal);

        var (draft, dep, _) = StampedWithExhibits(Written());
        draft.Guide!.Facts[0].Title += " (revised)";
        Assert.Equal(StaleSource.Guide, DiscoveryStaleness.Check(dep, draft, empty));

        var (draft2, dep2, _) = StampedWithExhibits(Written());
        draft2.Corpus!.PlanId = Guid.NewGuid();
        Assert.Equal(StaleSource.CorpusPlan, DiscoveryStaleness.Check(dep2, draft2, empty));
    }

    [Fact]
    public void Check_LegacyDocument_ExistenceOnly()
    {
        var (draft, dep, _) = StampedWithExhibits(Written());
        dep.ExhibitStamps.Clear();

        var moved = Written();
        moved["RD-000001"] = T1.AddHours(3);
        Assert.Equal(StaleSource.None, DiscoveryStaleness.Check(dep, draft, moved));

        moved.Remove("RD-000001");
        Assert.Equal(StaleSource.Exhibits, DiscoveryStaleness.Check(dep, draft, moved));
    }

    [Fact]
    public async Task EmailStamps_FromJournal_UsesThreadDoneUtc()
    {
        var folder = Path.Combine(Path.GetTempPath(), "rd-stale-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var draft = await DiscoveryCorpusFixture.WithWrittenCorpusAsync(folder);
            var replay = GenerationJournal.Replay(folder, DiscoveryCorpusFixture.PlanOf(draft));

            var stamps = DiscoveryStaleness.EmailStamps(replay);

            Assert.Equal(new[] { "RD-000001", "RD-000002", "RD-000003", "RD-000005" }, stamps.Keys.OrderBy(k => k, StringComparer.Ordinal));
            Assert.All(stamps.Values, v => Assert.Equal(T1, v));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void EmailStamps_IgnoresAttachmentRecords()
    {
        var replay = new JournalReplay { Exists = true };
        replay.LastByThread["T-0001"] = new JournalRecord
        {
            Type = JournalRecordType.ThreadDone,
            Utc = T1,
            ThreadId = "T-0001",
            Messages = { new JournalMessage { DocId = "RD-000001" } }
        };
        replay.LastByAttachment["RD-000001.0001"] = new JournalRecord
        {
            Type = JournalRecordType.AttachmentDone,
            Utc = T1.AddHours(2),
            ThreadId = "T-0001",
            ParentDocId = "RD-000001"
        };

        var stamp = Assert.Single(DiscoveryStaleness.EmailStamps(replay));
        Assert.Equal(T1, stamp.Value);
        Assert.Empty(DiscoveryStaleness.EmailStamps(null));
    }

    [Fact]
    public void AnyCitesEmails_DetectsCitingDocuments()
    {
        Assert.False(DiscoveryStaleness.AnyCitesEmails(null));
        Assert.False(DiscoveryStaleness.AnyCitesEmails(new DiscoveryDocumentSet()));

        var set = new DiscoveryDocumentSet();
        set.Documents.Add(new DiscoveryDocument { Key = "RFP", Written = new WrittenDiscoveryContent() });
        Assert.False(DiscoveryStaleness.AnyCitesEmails(set));

        set.Documents.Add(Rfa());
        Assert.True(DiscoveryStaleness.AnyCitesEmails(set));
        Assert.Equal(new[] { "RD-000002" }, DiscoveryStaleness.CitedDocIds(set.Documents[1]));
    }
}
