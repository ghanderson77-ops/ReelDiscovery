using System.Globalization;
using System.Text.Json;
using ReelDiscovery.Models;
using ReelDiscovery.Models.Case;
using ReelDiscovery.Models.Corpus;
using ReelDiscovery.Services.Case;
using ReelDiscovery.Services.Corpus;

namespace ReelDiscovery.Tests.Corpus;

/// <summary>
/// Hand-built corpus plans for CorpusTestData.RichDraft(). ValidPlan passes every CorpusPlanValidator rule; it is never
/// passed through Normalize (TotalEmails 20 is below CorpusSettings.MinEmails on purpose).
/// </summary>
internal static class PlanFixtures
{
    public const string Prefix = "RD";

    private sealed class Spec
    {
        public ThreadRelevance Relevance;
        public NonRelevantCategory Category;
        public bool IsPrivileged;
        public bool IsHot;
        public string FactId = string.Empty;
        public List<string> IssueIds = new();
        public string From = string.Empty;
        public string To = string.Empty;
        public DateOnly Day;
        public string AttachmentName = string.Empty;
    }

    /// <summary>
    /// 20 emails: 8 relevant one-message fact threads (F-001..F-008) and 12 non-relevant (6 daily business, 3 ancillary,
    /// 2 newsletter, 1 decoy), every message at 10:00 sender-local, DocIDs RD-000001.. in SentAt order.
    /// </summary>
    public static CorpusPlan ValidPlan(CaseDraft draft)
    {
        var guide = draft.Guide!;
        var settings = CorpusSettings.CreateDefault(draft);
        settings.TotalEmails = 20;
        settings.RelevantPercent = 40;
        settings.DailyBusinessPercent = 50;
        settings.AncillaryPercent = 20;
        settings.NewsletterPercent = 20;
        settings.DecoyPercent = 10;
        var (start, end) = CorpusSettings.DefaultWindow(guide);
        settings.WindowStart = start;
        settings.WindowEnd = end;
        settings.DocIdPrefix = Prefix;

        var specs = new List<Spec>
        {
            Fact(guide, "F-001", "CH-001", "CH-005", new DateOnly(2024, 1, 16), privileged: true),
            Fact(guide, "F-002", "CH-004", "CH-002", new DateOnly(2024, 2, 13)),
            Fact(guide, "F-003", "CH-004", "CH-002", new DateOnly(2024, 3, 19), hot: true, attachment: "Change Order 7.docx"),
            Fact(guide, "F-004", "CH-002", "CH-004", new DateOnly(2024, 4, 23)),
            Fact(guide, "F-005", "CH-001", "CH-004", new DateOnly(2024, 5, 13)),
            Fact(guide, "F-006", "CH-003", "CH-001", new DateOnly(2024, 6, 21)),
            Fact(guide, "F-007", "CH-001", "CH-005", new DateOnly(2024, 6, 3)),
            Fact(guide, "F-008", "CH-008", "CH-008", new DateOnly(2024, 7, 5)),

            Other(NonRelevantCategory.DailyBusiness, "CH-002", "CH-009", new DateOnly(2024, 5, 14), "Site schedule.docx"),
            Other(NonRelevantCategory.DailyBusiness, "CH-001", "CH-002", new DateOnly(2024, 2, 20)),
            Other(NonRelevantCategory.DailyBusiness, "CH-003", "CH-004", new DateOnly(2024, 3, 5)),
            Other(NonRelevantCategory.DailyBusiness, "CH-004", "CH-003", new DateOnly(2024, 4, 9)),
            Other(NonRelevantCategory.DailyBusiness, "CH-002", "CH-001", new DateOnly(2024, 5, 21)),
            Other(NonRelevantCategory.DailyBusiness, "CH-003", "CH-004", new DateOnly(2024, 6, 11)),

            Other(NonRelevantCategory.Ancillary, "CH-008", "CH-004", new DateOnly(2024, 2, 27)),
            Other(NonRelevantCategory.Ancillary, "CH-008", "CH-004", new DateOnly(2024, 4, 16)),
            Other(NonRelevantCategory.Ancillary, "CH-008", "CH-004", new DateOnly(2024, 6, 18)),

            Other(NonRelevantCategory.NewsletterSpamPersonal, "EXT-001", "CH-001", new DateOnly(2024, 3, 12)),
            Other(NonRelevantCategory.NewsletterSpamPersonal, "EXT-001", "CH-003", new DateOnly(2024, 5, 7)),

            Other(NonRelevantCategory.Decoy, "CH-004", "CH-002", new DateOnly(2024, 4, 2), issueIds: new List<string> { "ISS-01" })
        };

        var orgById = guide.Organizations.ToDictionary(o => o.Id, StringComparer.Ordinal);
        TimeZoneInfo ZoneFor(string id)
        {
            var member = guide.FindCast(id);
            if (member == null)
                return BusinessCalendar.ResolveId(UsTimeZones.Default)!;
            orgById.TryGetValue(member.OrganizationId, out var org);
            return BusinessCalendar.Resolve(org);
        }

        var dated = specs
            .Select(s =>
            {
                var zone = ZoneFor(s.From);
                return (Spec: s, Zone: zone, SentAt: BusinessCalendar.At(zone, s.Day, new TimeOnly(10, 0)));
            })
            .OrderBy(x => x.SentAt.UtcDateTime)
            .ToList();

        var plan = new CorpusPlan
        {
            PlanId = Guid.NewGuid(),
            CreatedUtc = new DateTime(2026, 10, 2, 12, 0, 0, DateTimeKind.Utc),
            DocIdPrefix = Prefix,
            Settings = settings,
            Externals = new List<ExternalParty>
            {
                new()
                {
                    Id = "EXT-001",
                    Name = "Commercial Builder Weekly",
                    Email = "news@commercialbuilderweekly.com",
                    Domain = "commercialbuilderweekly.com",
                    Kind = ExternalKind.Newsletter
                }
            }
        };

        for (var i = 0; i < dated.Count; i++)
        {
            var (spec, zone, sentAt) = dated[i];
            var number = i + 1;
            var threadId = $"T-{number:0000}";
            var docId = $"{Prefix}-{number.ToString("000000", CultureInfo.InvariantCulture)}";

            var message = new PlannedMessage
            {
                Id = $"{threadId}-M01",
                DocId = docId,
                Seq = 1,
                ReplyToSeq = 0,
                Kind = MessageKind.New,
                FromId = spec.From,
                ToIds = new List<string> { spec.To },
                SentAt = sentAt,
                TimeZoneId = zone.Id,
                FactIds = spec.FactId.Length > 0 ? new List<string> { spec.FactId } : new List<string>(),
                Basis = spec.Relevance == ThreadRelevance.Relevant ? RelevanceBasis.Own : RelevanceBasis.None,
                IsHot = spec.IsHot,
                CustodianIds = new List<string> { spec.From }
            };

            if (spec.AttachmentName.Length > 0)
            {
                message.Attachments.Add(new PlannedAttachment
                {
                    DocId = $"{docId}.0001",
                    Kind = AttachmentType.Word,
                    FileName = spec.AttachmentName,
                    Title = Path.GetFileNameWithoutExtension(spec.AttachmentName)
                });
            }

            plan.Threads.Add(new PlannedThread
            {
                Id = threadId,
                Relevance = spec.Relevance,
                Category = spec.Category,
                IsPrivileged = spec.IsPrivileged,
                IssueIds = new List<string>(spec.IssueIds),
                FactIds = spec.FactId.Length > 0 ? new List<string> { spec.FactId } : new List<string>(),
                Subject = $"Subject {threadId}",
                OwnerCustodianId = spec.From,
                Messages = new List<PlannedMessage> { message }
            });
        }

        plan.GuideHash = guide.ComputeHash();
        plan.SettingsHash = settings.ComputeHash();
        return plan;
    }

    /// <summary>
    /// Deep copy via JSON round-trip (no Normalize) so tests can mutate the copy.
    /// </summary>
    public static CorpusPlan Clone(CorpusPlan plan)
    {
        var json = JsonSerializer.Serialize(plan, CaseDraft.JsonOptions);
        return JsonSerializer.Deserialize<CorpusPlan>(json, CaseDraft.JsonOptions)!;
    }

    public static PlannedThread Thread(CorpusPlan plan, string factOrCategory) =>
        plan.Threads.First(t => t.FactIds.Contains(factOrCategory) || t.Category.ToString() == factOrCategory);

    private static Spec Fact(CaseGuide guide, string factId, string from, string to, DateOnly day, bool privileged = false,
        bool hot = false, string attachment = "") => new()
    {
        Relevance = ThreadRelevance.Relevant,
        Category = NonRelevantCategory.None,
        IsPrivileged = privileged,
        IsHot = hot,
        FactId = factId,
        IssueIds = new List<string>(guide.FindFact(factId)!.IssueIds),
        From = from,
        To = to,
        Day = day,
        AttachmentName = attachment
    };

    private static Spec Other(NonRelevantCategory category, string from, string to, DateOnly day, string attachment = "",
        List<string>? issueIds = null) => new()
    {
        Relevance = ThreadRelevance.NotRelevant,
        Category = category,
        From = from,
        To = to,
        Day = day,
        AttachmentName = attachment,
        IssueIds = issueIds ?? new List<string>()
    };
}
