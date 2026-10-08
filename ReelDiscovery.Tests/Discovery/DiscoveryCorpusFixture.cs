using System.Globalization;
using ReelDiscovery.Models.Case;
using ReelDiscovery.Models.Corpus;
using ReelDiscovery.Models.Evidence;
using ReelDiscovery.Services.Corpus;
using ReelDiscovery.Services.Evidence;

namespace ReelDiscovery.Tests.Discovery;

/// <summary>
/// A LitigationDraft with a saved corpus plan and a journal recording written emails, for tests that need a usable
/// generated corpus (exhibits, genuineness RFAs, deposition ledger grounding).
/// </summary>
internal static class DiscoveryCorpusFixture
{
    // The draft's key non-counsel witnesses (see DiscoveryTestData.LitigationDraft)
    private static readonly string[] Witnesses = { "CH-003", "CH-004", "CH-001", "CH-002", "CH-013" };

    public const string NonRelevantThreadId = "T-NR-01";

    /// <summary>
    /// Saves a plan of <paramref name="relevant"/> one-message relevant threads (DocIds RD-000001...) plus one non-relevant
    /// thread, and journals ThreadDone for the first <paramref name="written"/> relevant threads and the non-relevant one.
    /// Relevant message <paramref name="hotIndex"/> is hot.
    /// </summary>
    public static async Task<CaseDraft> WithWrittenCorpusAsync(string folder, int relevant = 4, int written = 3, int hotIndex = 0)
    {
        var draft = DiscoveryTestData.LitigationDraft();
        draft.FolderPath = folder;
        var guide = draft.Guide!;
        var factIds = guide.Facts
            .Where(f => f.Significance is FactSignificance.Hot or FactSignificance.Key)
            .Select(f => f.Id)
            .ToList();

        var plan = new CorpusPlan { PlanId = Guid.NewGuid(), DocIdPrefix = "RD" };
        var start = new DateTimeOffset(2024, 3, 4, 10, 0, 0, TimeSpan.FromHours(-6));
        for (var i = 0; i <= relevant; i++)
        {
            var isRelevant = i < relevant;
            var threadId = isRelevant ? $"T-{i + 1:0000}" : NonRelevantThreadId;
            var message = new PlannedMessage
            {
                Id = $"{threadId}-M01",
                DocId = $"RD-{(i + 1).ToString("000000", CultureInfo.InvariantCulture)}",
                Seq = 1,
                Kind = MessageKind.New,
                FromId = Witnesses[i % Witnesses.Length],
                ToIds = new List<string> { Witnesses[(i + 1) % Witnesses.Length] },
                CcIds = new List<string> { Witnesses[(i + 2) % Witnesses.Length] },
                SentAt = start.AddDays(i),
                FactIds = isRelevant && factIds.Count > 0 ? new List<string> { factIds[i % factIds.Count] } : new List<string>(),
                IsHot = isRelevant && i == hotIndex,
                CustodianIds = new List<string> { Witnesses[i % Witnesses.Length] }
            };
            plan.Threads.Add(new PlannedThread
            {
                Id = threadId,
                Relevance = isRelevant ? ThreadRelevance.Relevant : ThreadRelevance.NotRelevant,
                Category = isRelevant ? NonRelevantCategory.None : NonRelevantCategory.DailyBusiness,
                IssueIds = isRelevant ? new List<string> { "ISS-01" } : new List<string>(),
                FactIds = new List<string>(message.FactIds),
                Subject = $"Subject line {threadId}",
                OwnerCustodianId = message.FromId,
                Messages = new List<PlannedMessage> { message }
            });
        }

        await CorpusPlanStore.SaveAsync(folder, plan);
        draft.Corpus = new CorpusState { PlanId = plan.PlanId, Plan = plan };

        var journal = new GenerationJournal(folder);
        var journaled = plan.Threads.Take(written).Append(plan.Threads[^1]);
        foreach (var thread in journaled)
            await journal.AppendAsync(Done(plan.PlanId, thread));

        return draft;
    }

    public static CorpusPlan PlanOf(CaseDraft draft) => draft.Corpus!.Plan!;

    public static JournalRecord Done(Guid planId, PlannedThread thread) => new()
    {
        Type = JournalRecordType.ThreadDone,
        Utc = new DateTime(2026, 10, 3, 12, 0, 0, DateTimeKind.Utc),
        PlanId = planId,
        ThreadId = thread.Id,
        Status = ThreadWriteStatus.Written,
        Messages = thread.Messages.Select(m => new JournalMessage
        {
            DocId = m.DocId,
            Subject = thread.Subject,
            BodyPlain = "Please see the attached schedule."
        }).ToList()
    };
}
