using System.Text.Json;
using ReelDiscovery.Models.Case;
using ReelDiscovery.Models.Corpus;
using ReelDiscovery.Models.Evidence;
using ReelDiscovery.Services.Evidence;

namespace ReelDiscovery.Tests.Evidence;

public class GenerationStateTests
{
    private static LedgerEntry Entry(string id, string date = "2024-03-19", string statement = "Agreed to a revised delivery date") =>
        new()
        {
            Id = id,
            Date = date,
            Kind = LedgerKind.Commitment,
            About = "CH-002",
            Statement = statement,
            Value = "April 12, 2024",
            KnownByIds = new() { "CH-002", "CH-004" },
            FactIds = new() { "F-003" },
            IssueIds = new() { "ISS-01" },
            SourceDocId = "RD-000003",
            SourceThreadId = "T-0003",
            WaveIndex = 2
        };

    [Fact]
    public void Ledger_RoundTripsThroughCaseDraftJsonOptions()
    {
        var state = new GenerationState { RunPlanId = Guid.NewGuid() };
        state.Ledger.Entries.Add(Entry("T-0003-L01"));
        state.Ledger.Entries.Add(Entry("T-0003-L02", "2024-03-20", "Shop drawings resubmitted"));

        var json = JsonSerializer.Serialize(state, CaseDraft.JsonOptions);
        var loaded = JsonSerializer.Deserialize<GenerationState>(json, CaseDraft.JsonOptions)!;
        loaded.Normalize();

        Assert.Equal(state.RunPlanId, loaded.RunPlanId);
        Assert.Equal(2, loaded.Ledger.Entries.Count);
        AssertSameEntry(state.Ledger.Entries[0], loaded.Ledger.Entries[0]);
        AssertSameEntry(state.Ledger.Entries[1], loaded.Ledger.Entries[1]);
    }

    internal static void AssertSameEntry(LedgerEntry expected, LedgerEntry actual)
    {
        Assert.Equal(expected.Id, actual.Id);
        Assert.Equal(expected.Date, actual.Date);
        Assert.Equal(expected.Kind, actual.Kind);
        Assert.Equal(expected.About, actual.About);
        Assert.Equal(expected.Statement, actual.Statement);
        Assert.Equal(expected.Value, actual.Value);
        Assert.Equal(expected.KnownByIds, actual.KnownByIds);
        Assert.Equal(expected.FactIds, actual.FactIds);
        Assert.Equal(expected.IssueIds, actual.IssueIds);
        Assert.Equal(expected.SourceDocId, actual.SourceDocId);
        Assert.Equal(expected.SourceThreadId, actual.SourceThreadId);
        Assert.Equal(expected.WaveIndex, actual.WaveIndex);
    }

    [Fact]
    public void LedgerEntry_ParsedDate()
    {
        Assert.Equal(new DateOnly(2024, 3, 19), Entry("A").ParsedDate);
        Assert.Null(Entry("A", "03/19/2024").ParsedDate);
    }

    [Fact]
    public void Ledger_Normalize_DropsMalformedAndDuplicates()
    {
        var ledger = new NarrativeLedger
        {
            Entries = new()
            {
                null!,
                Entry(""),
                Entry("T-0001-L01", statement: ""),
                Entry("T-0001-L02", date: "2024-3-19"),
                Entry("T-0001-L03", date: "not a date"),
                Entry("T-0001-L04"),
                Entry("T-0001-L04", statement: "Duplicate id"),
                new LedgerEntry
                {
                    Id = "T-0001-L05",
                    Date = "2024-04-01",
                    Kind = (LedgerKind)99,
                    Statement = "Meeting moved",
                    About = null!,
                    Value = null!,
                    KnownByIds = null!,
                    FactIds = null!,
                    IssueIds = null!,
                    SourceDocId = null!,
                    SourceThreadId = null!
                }
            }
        };

        ledger.Normalize();

        Assert.Equal(new[] { "T-0001-L04", "T-0001-L05" }, ledger.Entries.Select(e => e.Id));
        Assert.Equal("Agreed to a revised delivery date", ledger.Entries[0].Statement);
        var repaired = ledger.Entries[1];
        Assert.Equal(LedgerKind.Other, repaired.Kind);
        Assert.Equal(string.Empty, repaired.About);
        Assert.Equal(string.Empty, repaired.Value);
        Assert.Empty(repaired.KnownByIds);
        Assert.Empty(repaired.FactIds);
        Assert.Empty(repaired.IssueIds);
        Assert.Equal(string.Empty, repaired.SourceDocId);
        Assert.Equal(string.Empty, repaired.SourceThreadId);
    }

    [Fact]
    public void Ledger_Normalize_NullEntriesBecomesEmpty()
    {
        var ledger = new NarrativeLedger { Entries = null! };
        ledger.Normalize();
        Assert.NotNull(ledger.Entries);
        Assert.Empty(ledger.Entries);
    }

    [Fact]
    public void Ledger_Normalize_CapsEntries()
    {
        var ledger = new NarrativeLedger();
        for (var i = 0; i < NarrativeLedger.MaxEntries + 5; i++)
            ledger.Entries.Add(Entry($"T-{i:D5}-L01"));

        ledger.Normalize();

        Assert.Equal(NarrativeLedger.MaxEntries, ledger.Entries.Count);
    }

    [Fact]
    public void JournalRecord_Normalize_KeepsOnlySafePaths()
    {
        var record = new JournalRecord
        {
            Type = JournalRecordType.ThreadDone,
            Files = new()
            {
                "Mailboxes/jim/Inbox/RD-000001.eml",
                "../evil.eml",
                "C:\\x.eml",
                "Mailboxes/a/b/c/d.eml",
                "Mailboxes/jim/Inbox/x.txt",
                null!
            },
            InputTokens = -5,
            OutputTokens = -1,
            CostUsd = -0.25m,
            Attempts = -2,
            Messages = null!,
            LedgerDelta = null!,
            Violations = null!,
            ThreadId = null!,
            Reason = null!,
            Message = null!
        };

        record.Normalize();

        Assert.Equal(new[] { "Mailboxes/jim/Inbox/RD-000001.eml" }, record.Files);
        Assert.Equal(0, record.InputTokens);
        Assert.Equal(0, record.OutputTokens);
        Assert.Equal(0m, record.CostUsd);
        Assert.Equal(0, record.Attempts);
        Assert.Empty(record.Messages);
        Assert.Empty(record.LedgerDelta);
        Assert.Empty(record.Violations);
        Assert.Equal(string.Empty, record.ThreadId);
        Assert.Equal(string.Empty, record.Reason);
        Assert.Equal(string.Empty, record.Message);
    }

    [Fact]
    public void JournalRecord_RoundTripsAsJsonLine()
    {
        var record = new JournalRecord
        {
            Type = JournalRecordType.ThreadDone,
            Utc = new DateTime(2026, 10, 3, 12, 0, 0, DateTimeKind.Utc),
            PlanId = Guid.NewGuid(),
            ThreadId = "T-0003",
            Status = ThreadWriteStatus.Warnings,
            Attempts = 2,
            Violations = new() { "V3: names a person the sender has not met" },
            Files = new() { "Mailboxes/jim/Inbox/RD-000001.eml" },
            Messages = new() { new JournalMessage { DocId = "RD-000001", Subject = "Delivery", BodyPlain = "Body", SignOff = "Jim" } },
            LedgerDelta = new() { new LedgerDelta { Seq = 1, Kind = LedgerKind.Figure, Statement = "Change order total", Value = "$48,200" } },
            InputTokens = 1200,
            OutputTokens = 800,
            CostUsd = 0.0123m
        };

        var line = JsonSerializer.Serialize(record, CaseDraft.JsonOptions).ReplaceLineEndings(string.Empty);
        var loaded = JsonSerializer.Deserialize<JournalRecord>(line, CaseDraft.JsonOptions)!;
        loaded.Normalize();

        Assert.Equal(record.PlanId, loaded.PlanId);
        Assert.Equal(ThreadWriteStatus.Warnings, loaded.Status);
        Assert.Equal(record.Files, loaded.Files);
        Assert.Equal("$48,200", loaded.LedgerDelta[0].Value);
        Assert.Equal(LedgerKind.Figure, loaded.LedgerDelta[0].Kind);
        Assert.Equal(0.0123m, loaded.CostUsd);
    }

    [Fact]
    public void GenerationState_Normalize_ClampsAndRepairs()
    {
        var state = new GenerationState
        {
            Ledger = null!,
            LastMessage = null!,
            KeyError = null!,
            StoryError = null!,
            LastOutcome = (GenerationOutcome)42,
            ThreadsTotal = -1,
            ThreadsWritten = -2,
            ThreadsWithWarnings = -3,
            ThreadsFailed = -4,
            EmailsWritten = -5,
            EmailsFailed = -6,
            FilesWritten = -7,
            Mailboxes = -8,
            CostUsd = -1m
        };

        state.Normalize();

        Assert.NotNull(state.Ledger);
        Assert.Empty(state.Ledger.Entries);
        Assert.Equal(GenerationOutcome.None, state.LastOutcome);
        Assert.Equal(string.Empty, state.LastMessage);
        Assert.Equal(string.Empty, state.KeyError);
        Assert.Equal(string.Empty, state.StoryError);
        Assert.Equal(0, state.ThreadsTotal);
        Assert.Equal(0, state.ThreadsWritten);
        Assert.Equal(0, state.ThreadsWithWarnings);
        Assert.Equal(0, state.ThreadsFailed);
        Assert.Equal(0, state.EmailsWritten);
        Assert.Equal(0, state.EmailsFailed);
        Assert.Equal(0, state.FilesWritten);
        Assert.Equal(0, state.Mailboxes);
        Assert.Equal(0m, state.CostUsd);
    }

    [Fact]
    public void GenerationState_Phase5Json_LoadsPhase6CountsAsZero()
    {
        var draft = CaseDraft.FromJson("{\"FormatVersion\":3,\"Corpus\":{\"Generation\":{\"ThreadsTotal\":12,\"ThreadsWritten\":4,\"CostUsd\":0.5}}}");
        draft.Normalize();

        var generation = draft.Corpus!.Generation!;
        Assert.Equal(12, generation.ThreadsTotal);
        Assert.Equal(4, generation.ThreadsWritten);
        Assert.Equal(0, generation.NonRelevantTotal);
        Assert.Equal(0, generation.NonRelevantWritten);
        Assert.Equal(0, generation.NonRelevantWithWarnings);
        Assert.Equal(0, generation.NonRelevantFailed);
        Assert.Equal(0, generation.AttachmentsTotal);
        Assert.Equal(0, generation.AttachmentsWritten);
        Assert.Equal(0, generation.AttachmentsWithWarnings);
        Assert.Equal(0, generation.AttachmentsFailed);
        Assert.Equal(0, generation.AttachmentsSkipped);
        Assert.Equal(0m, generation.MediaCostUsd);
    }

    [Fact]
    public void GenerationState_Normalize_ClampsPhase6Counts()
    {
        var state = new GenerationState
        {
            NonRelevantTotal = -1,
            NonRelevantWritten = -2,
            NonRelevantWithWarnings = -3,
            NonRelevantFailed = -4,
            AttachmentsTotal = -5,
            AttachmentsWritten = -6,
            AttachmentsWithWarnings = -7,
            AttachmentsFailed = -8,
            AttachmentsSkipped = -9,
            MediaCostUsd = -1m
        };

        state.Normalize();

        Assert.Equal(0, state.NonRelevantTotal);
        Assert.Equal(0, state.NonRelevantWritten);
        Assert.Equal(0, state.NonRelevantWithWarnings);
        Assert.Equal(0, state.NonRelevantFailed);
        Assert.Equal(0, state.AttachmentsTotal);
        Assert.Equal(0, state.AttachmentsWritten);
        Assert.Equal(0, state.AttachmentsWithWarnings);
        Assert.Equal(0, state.AttachmentsFailed);
        Assert.Equal(0, state.AttachmentsSkipped);
        Assert.Equal(0m, state.MediaCostUsd);
    }

    [Fact]
    public void GenerationState_Phase6Counts_RoundTrip()
    {
        var (draft, _) = EvidenceTestData.ApprovedCase();
        draft.Corpus!.Generation = new GenerationState
        {
            NonRelevantTotal = 30,
            NonRelevantWritten = 20,
            AttachmentsTotal = 9,
            AttachmentsSkipped = 2,
            MediaCostUsd = 0.25m
        };

        var loaded = CaseDraft.FromJson(draft.ToJson());
        loaded.Normalize();

        var generation = loaded.Corpus!.Generation!;
        Assert.Equal(30, generation.NonRelevantTotal);
        Assert.Equal(20, generation.NonRelevantWritten);
        Assert.Equal(9, generation.AttachmentsTotal);
        Assert.Equal(2, generation.AttachmentsSkipped);
        Assert.Equal(0.25m, generation.MediaCostUsd);
    }

    [Fact]
    public void ComposedEmail_New_HasNoAttachmentsAndEmptyQuoteFragment()
    {
        var email = new ComposedEmail();

        Assert.NotNull(email.Attachments);
        Assert.Empty(email.Attachments);
        Assert.Equal(string.Empty, email.QuoteFragment);
    }

    [Fact]
    public void ComposedAttachment_Defaults_AreEmpty_AndBytesAreNotSerialized()
    {
        var part = new ComposedAttachment();
        Assert.Equal(string.Empty, part.DocId);
        Assert.Equal(string.Empty, part.FileName);
        Assert.Equal(string.Empty, part.Title);
        Assert.Equal(string.Empty, part.MimeType);
        Assert.Equal(string.Empty, part.ContentId);
        Assert.Empty(part.Bytes);
        Assert.False(part.IsInline);

        part.Bytes = new byte[] { 1, 2, 3 };
        var json = JsonSerializer.Serialize(part);
        Assert.DoesNotContain("Bytes", json);
    }

    [Fact]
    public void MessageTime_LocalDate_UsesSenderZone()
    {
        var message = new PlannedMessage
        {
            SentAt = new DateTimeOffset(2024, 3, 20, 2, 30, 0, TimeSpan.Zero),
            TimeZoneId = "America/Phoenix"
        };

        Assert.Equal(new DateOnly(2024, 3, 19), MessageTime.LocalDate(message));
    }

    [Fact]
    public void MessageTime_LocalDate_UnknownZoneFallsBackToOffset()
    {
        var message = new PlannedMessage
        {
            SentAt = new DateTimeOffset(2024, 3, 20, 2, 30, 0, TimeSpan.Zero),
            TimeZoneId = "Nowhere/Invalid"
        };

        Assert.Equal(new DateOnly(2024, 3, 20), MessageTime.LocalDate(message));
    }

    [Fact]
    public void MessageTime_ThreadBounds()
    {
        var thread = new PlannedThread
        {
            Messages = new()
            {
                new PlannedMessage { Seq = 2, SentAt = new DateTimeOffset(2024, 3, 22, 9, 0, 0, TimeSpan.Zero), TimeZoneId = "UTC" },
                new PlannedMessage { Seq = 1, SentAt = new DateTimeOffset(2024, 3, 20, 9, 0, 0, TimeSpan.Zero), TimeZoneId = "UTC" }
            }
        };

        Assert.Equal(new DateTime(2024, 3, 20, 9, 0, 0), MessageTime.FirstUtc(thread));
        Assert.Equal(new DateOnly(2024, 3, 20), MessageTime.FirstLocalDate(thread));
        Assert.Equal(new DateOnly(2024, 3, 22), MessageTime.LastLocalDate(thread));

        var empty = new PlannedThread();
        Assert.Equal(DateTime.MaxValue, MessageTime.FirstUtc(empty));
        Assert.Equal(DateOnly.MaxValue, MessageTime.FirstLocalDate(empty));
        Assert.Equal(DateOnly.MinValue, MessageTime.LastLocalDate(empty));
    }

    [Fact]
    public void CaseDraft_GenerationState_RoundTrips()
    {
        var (draft, _) = EvidenceTestData.ApprovedCase();
        var state = new GenerationState { RunPlanId = draft.Corpus!.PlanId, LastOutcome = GenerationOutcome.Cancelled, LastMessage = "Stopped" };
        state.Ledger.Entries.Add(Entry("T-0003-L01"));
        state.Ledger.Entries.Add(Entry("T-0003-L02", "2024-03-20", "Shop drawings resubmitted"));
        draft.Corpus.Generation = state;

        var json = draft.ToJson();
        Assert.Contains("\"FormatVersion\": 3", json);
        var loaded = CaseDraft.FromJson(json);
        loaded.Normalize();

        var generation = loaded.Corpus!.Generation!;
        Assert.Equal(state.RunPlanId, generation.RunPlanId);
        Assert.Equal(GenerationOutcome.Cancelled, generation.LastOutcome);
        Assert.Equal("Stopped", generation.LastMessage);
        Assert.Equal(2, generation.Ledger.Entries.Count);
        AssertSameEntry(state.Ledger.Entries[0], generation.Ledger.Entries[0]);
        AssertSameEntry(state.Ledger.Entries[1], generation.Ledger.Entries[1]);
    }

    [Fact]
    public void CaseDraft_NullGenerationParts_NormalizeToEmpty()
    {
        var draft = CaseDraft.FromJson("{\"FormatVersion\":3,\"Corpus\":{\"Generation\":{\"Ledger\":null,\"LastMessage\":null}}}");
        draft.Normalize();

        var generation = draft.Corpus!.Generation!;
        Assert.NotNull(generation.Ledger);
        Assert.Empty(generation.Ledger.Entries);
        Assert.Equal(string.Empty, generation.LastMessage);
    }

    [Fact]
    public void CorpusState_Normalize_RepairsGeneration()
    {
        var corpus = new CorpusState { Generation = new GenerationState { ThreadsFailed = -3, Ledger = null! } };
        corpus.Normalize();
        Assert.Equal(0, corpus.Generation!.ThreadsFailed);
        Assert.NotNull(corpus.Generation.Ledger);
    }

    [Fact]
    public void ApprovedCase_CoversEveryThreadShape()
    {
        var (draft, plan) = EvidenceTestData.ApprovedCase();
        var guide = draft.Guide!;

        Assert.True(draft.Corpus!.Approved);
        Assert.Same(plan, draft.Corpus.Plan);
        Assert.Equal(plan.PlanId, draft.Corpus.PlanId);

        var relevant = plan.Threads.Where(t => t.Relevance == ThreadRelevance.Relevant).ToList();

        var fourMessage = Assert.Single(relevant, t => t.Messages.Count == 4);
        Assert.Equal(new[] { MessageKind.New, MessageKind.Reply, MessageKind.ReplyAll, MessageKind.Forward },
            fourMessage.Messages.OrderBy(m => m.Seq).Select(m => m.Kind));
        Assert.Equal(new[] { 0, 1, 2, 3 }, fourMessage.Messages.OrderBy(m => m.Seq).Select(m => m.ReplyToSeq));
        Assert.Contains(fourMessage.Messages, m => m.IsHot);

        var vendor = plan.Externals.Single(e => e.Id == EvidenceTestData.VendorId);
        Assert.Equal(ExternalKind.Vendor, vendor.Kind);
        Assert.Contains(relevant, t => t.Messages.OrderBy(m => m.Seq).First().FromId == vendor.Id);

        Assert.Contains(relevant, t => t.IsPrivileged
            && t.Messages.SelectMany(m => new[] { m.FromId }.Concat(m.ToIds).Concat(m.CcIds))
                .Select(id => guide.FindCast(id))
                .Any(c => c != null && c.Role is CastRole.InHouseCounsel or CastRole.OutsideCounsel));

        Assert.Contains(plan.AllMessages(), m => m.CustodianIds.Count >= 3);

        foreach (var message in relevant.SelectMany(t => t.Messages))
        {
            Assert.False(string.IsNullOrEmpty(message.DocId), message.Id);
            Assert.True(CorpusPlan.IsSafeEmlPath(message.FilePath), message.FilePath);
        }

        // Seq order is time order within every thread
        foreach (var thread in plan.Threads)
        {
            var ordered = thread.Messages.OrderBy(m => m.Seq).Select(m => m.SentAt.UtcDateTime).ToList();
            Assert.Equal(ordered.OrderBy(x => x), ordered);
        }
    }

    [Fact]
    public void ThreadAssignment_For_FindsBySeq()
    {
        var assignment = new ThreadAssignment
        {
            Messages = new() { new MessageAssignment { Seq = 1 }, new MessageAssignment { Seq = 2, Subtlety = Subtlety.Explicit } }
        };

        Assert.Equal(Subtlety.Explicit, assignment.For(2)!.Subtlety);
        Assert.Null(assignment.For(3));
    }
}
