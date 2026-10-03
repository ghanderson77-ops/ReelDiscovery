using ReelDiscovery.Models.Case;
using ReelDiscovery.Models.Corpus;
using ReelDiscovery.Services.Corpus;
using ReelDiscovery.Tests.Corpus;

namespace ReelDiscovery.Tests.Evidence;

/// <summary>
/// Phase 5 approved-plan fixture: PlanFixtures.ValidPlan on CorpusTestData.RichDraft with richer relevant threads.
/// F-003 is a hot 4-message thread (New, Reply, ReplyAll, Forward); F-004 starts with an external vendor (EXT-002);
/// F-001 is privileged with in-house and outside counsel and three custodians on its root.
/// </summary>
internal static class EvidenceTestData
{
    public const string VendorId = "EXT-002";

    public static (CaseDraft Draft, CorpusPlan Plan) ApprovedCase()
    {
        var draft = CorpusTestData.RichDraft();
        var guide = draft.Guide!;
        var plan = PlanFixtures.ValidPlan(draft);

        // F-003: CH-004 -> CH-002 hot root, then a reply, a reply-all and a forward
        var hot = PlanFixtures.Thread(plan, "F-003");
        var hotRoot = hot.Messages[0];
        var reply = Add(guide, hot, 2, 1, MessageKind.Reply, "CH-002", new[] { "CH-004" }, Array.Empty<string>(),
            hotRoot.SentAt.AddHours(2), RelevanceBasis.Quoted);
        var replyAll = Add(guide, hot, 3, 2, MessageKind.ReplyAll, "CH-004", new[] { "CH-002" }, new[] { "CH-003" },
            reply.SentAt.AddDays(1), RelevanceBasis.Own, "F-003");
        Add(guide, hot, 4, 3, MessageKind.Forward, "CH-002", new[] { "CH-001" }, Array.Empty<string>(),
            replyAll.SentAt.AddDays(1), RelevanceBasis.Own, "F-003");

        // F-004: an outside vendor writes first, the project manager answers
        plan.Externals.Add(new ExternalParty
        {
            Id = VendorId,
            Name = "Dana Whitfield",
            Email = "dana@summitglazingsupply.com",
            Domain = "summitglazingsupply.com",
            Kind = ExternalKind.Vendor
        });
        var vendor = PlanFixtures.Thread(plan, "F-004");
        var vendorRoot = vendor.Messages[0];
        vendorRoot.FromId = VendorId;
        vendorRoot.ToIds = new List<string> { "CH-002" };
        vendorRoot.CcIds = new List<string>();
        Add(guide, vendor, 2, 1, MessageKind.Reply, "CH-002", new[] { VendorId }, Array.Empty<string>(),
            vendorRoot.SentAt.AddDays(1), RelevanceBasis.Own, "F-004");

        // F-001: privileged president -> in-house counsel, outside counsel copied, counsel answers
        var privileged = PlanFixtures.Thread(plan, "F-001");
        var privilegedRoot = privileged.Messages[0];
        privilegedRoot.CcIds = new List<string> { "CH-007" };
        Add(guide, privileged, 2, 1, MessageKind.Reply, "CH-005", new[] { "CH-001" }, new[] { "CH-007" },
            privilegedRoot.SentAt.AddHours(3), RelevanceBasis.Own, "F-001");

        PlanNumbering.Assign(plan, guide);

        draft.Corpus = new CorpusState
        {
            Settings = plan.Settings,
            PlanId = plan.PlanId,
            PlanGuideHash = plan.GuideHash,
            PlanSettingsHash = plan.SettingsHash,
            PlanCreatedUtc = plan.CreatedUtc,
            Plan = plan
        };
        CorpusApproval.RecordApproval(draft, plan, new List<WrittenCorpusFile>(),
            new DateTime(2026, 10, 3, 12, 0, 0, DateTimeKind.Utc));

        return (draft, plan);
    }

    public static string TempCaseFolder()
    {
        var path = Path.Combine(Path.GetTempPath(), "rd-evidence-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    public static void DeleteFolder(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch
        {
            // Best-effort temp cleanup; a locked file must not fail the test that already asserted its result
        }
    }

    private static PlannedMessage Add(CaseGuide guide, PlannedThread thread, int seq, int replyToSeq, MessageKind kind,
        string from, string[] to, string[] cc, DateTimeOffset sentAt, RelevanceBasis basis, string factId = "")
    {
        var zone = ZoneFor(guide, from, thread);
        var message = new PlannedMessage
        {
            Seq = seq,
            ReplyToSeq = replyToSeq,
            Kind = kind,
            FromId = from,
            ToIds = to.ToList(),
            CcIds = cc.ToList(),
            SentAt = TimeZoneInfo.ConvertTime(sentAt, zone),
            TimeZoneId = zone.Id,
            FactIds = factId.Length > 0 ? new List<string> { factId } : new List<string>(),
            Basis = basis
        };
        thread.Messages.Add(message);
        return message;
    }

    // Cast sender: their organization's zone; external sender: the thread root's zone
    private static TimeZoneInfo ZoneFor(CaseGuide guide, string from, PlannedThread thread)
    {
        var member = guide.FindCast(from);
        if (member != null)
            return BusinessCalendar.Resolve(guide.FindOrganization(member.OrganizationId));
        return BusinessCalendar.ResolveId(thread.Messages[0].TimeZoneId) ?? TimeZoneInfo.Utc;
    }
}
