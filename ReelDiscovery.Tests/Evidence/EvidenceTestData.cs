using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using ReelDiscovery.Models.Case;
using ReelDiscovery.Models.Corpus;
using ReelDiscovery.Models.Evidence;
using ReelDiscovery.Services.Ai;
using ReelDiscovery.Services.Corpus;
using ReelDiscovery.Services.Evidence;
using ReelDiscovery.Tests.Corpus;
using ReelDiscovery.Tests.Fakes;

namespace ReelDiscovery.Tests.Evidence;

/// <summary>
/// Phase 5 approved-plan fixture: PlanFixtures.ValidPlan on CorpusTestData.RichDraft with richer relevant threads.
/// F-003 is a hot 4-message thread (New, Reply, ReplyAll, Forward); F-004 starts with an external vendor (EXT-002);
/// F-001 is privileged with in-house and outside counsel and three custodians on its root.
/// </summary>
internal static partial class EvidenceTestData
{
    public const string VendorId = "EXT-002";

    // Neutral filler: no person names, no amounts, no ids
    private static readonly string[] FillerWords =
    {
        "please", "review", "the", "updated", "schedule", "and", "let", "me", "know", "if", "anything", "needs",
        "to", "change", "before", "we", "move", "ahead", "with", "next", "steps", "on", "this", "item"
    };

    private static readonly JsonSerializerOptions ResponderJsonOptions = new() { Converters = { new JsonStringEnumConverter() } };

    [GeneratedRegex(@"Thread id: (T-\d{4})")]
    private static partial Regex ThreadIdLine();

    /// <summary>
    /// Fake render responder: answers a render request by its "Thread id: T-xxxx" line with a valid ThreadRenderDto (one
    /// message per planned seq, neutral bodies inside each message's length band, signOff "Thanks,", one Commitment detail
    /// "site walk {n}" on seq 1). <paramref name="overrideJson"/> gets (threadId, attempt) and may return replacement JSON
    /// (null = default). Attempts are counted per thread. Unknown threads fail with a transient error.
    /// </summary>
    public static Func<LlmProviderRequest, LlmProviderResponse> RenderResponder(CorpusPlan plan, CaseGuide guide,
        Func<string, int, string?>? overrideJson = null)
    {
        var assignments = RenderAssignments.Compute(plan);
        var threads = plan.Threads
            .Where(t => t != null && !string.IsNullOrWhiteSpace(t.Id))
            .GroupBy(t => t.Id, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        var attempts = new ConcurrentDictionary<string, int>(StringComparer.Ordinal);

        return request =>
        {
            var match = ThreadIdLine().Match(request.UserPrompt ?? string.Empty);
            if (!match.Success || !threads.TryGetValue(match.Groups[1].Value, out var thread))
                return FakeLlmProvider.Failed(LlmFailureKind.Transient, "unknown thread");

            var attempt = attempts.AddOrUpdate(thread.Id, 1, (_, n) => n + 1);
            var custom = overrideJson?.Invoke(thread.Id, attempt);
            if (custom != null)
                return FakeLlmProvider.Ok(custom, 1000, 400);

            assignments.TryGetValue(thread.Id, out var assignment);
            var number = int.Parse(thread.Id[2..]).ToString(System.Globalization.CultureInfo.InvariantCulture);
            var dto = new ThreadRenderer.ThreadRenderDto();
            foreach (var message in thread.Messages.Where(m => m != null).OrderBy(m => m.Seq))
            {
                var spec = RenderAssignments.Spec(assignment?.For(message.Seq)?.Length ?? LengthBand.Medium);
                dto.Messages.Add(new ThreadRenderer.RenderedMessageDto
                {
                    Seq = message.Seq,
                    Body = Filler((spec.MinWords + spec.MaxWords) / 2),
                    SignOff = "Thanks,",
                    PeopleNamed = new List<string>()
                });
            }

            dto.NewDetails.Add(new ThreadRenderer.LedgerDeltaDto
            {
                Seq = 1,
                Kind = LedgerKind.Commitment,
                About = $"site walk {number}",
                Statement = $"The team agreed to a site walk {number}.",
                Value = string.Empty,
                FactIds = new List<string>()
            });

            return FakeLlmProvider.Ok(JsonSerializer.Serialize(dto, ResponderJsonOptions), 1000, 400);
        };
    }

    // Shared with AttachmentResponder filler answers
    internal static string Filler(int words)
    {
        var text = string.Join(" ", Enumerable.Range(0, Math.Max(1, words)).Select(i => FillerWords[i % FillerWords.Length]));
        return char.ToUpperInvariant(text[0]) + text[1..] + ".";
    }

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
