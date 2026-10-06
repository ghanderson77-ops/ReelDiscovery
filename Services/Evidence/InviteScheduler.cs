using System.Globalization;
using System.Text.RegularExpressions;
using ReelDiscovery.Models.Case;
using ReelDiscovery.Models.Corpus;
using ReelDiscovery.Models.Evidence;
using ReelDiscovery.Services.Corpus;

namespace ReelDiscovery.Services.Evidence;

public enum InviteSource { OwnDelta, Ledger, Fact, Fallback }

/// <summary>
/// The schedule of one calendar invite; the caller persists it on the attachment record (06-01 InviteMeetingKey /
/// InviteStart / InviteEnd) so every later invite for the same meeting key reuses it.
/// </summary>
public class InviteSlot
{
    public DateTimeOffset Start { get; set; }
    public DateTimeOffset End { get; set; }
    public InviteSource Source { get; set; }

    // Source id for diagnostics only: "{threadId}:d{seq}", a ledger entry id or a fact id
    public string MatchedId { get; set; } = string.Empty;

    // Canonical meeting identity ("fact:F-012" or "mtg:{thread}:{doc}:{detail}"); empty for fallback slots
    public string MeetingKey { get; set; } = string.Empty;

    public bool Warning { get; set; }
    public string WarningText { get; set; } = string.Empty;
}

/// <summary>
/// Everything a relevant invite is scheduled from. OwnDeltas is every ledger delta of the thread known at resolve time,
/// unfiltered: Resolve gates visibility itself so the in-thread merge sees exactly what LedgerMerger.Build will see.
/// </summary>
public class InviteRequest
{
    public PlannedThread Thread { get; set; } = new();
    public PlannedMessage Parent { get; set; } = new();
    public PlannedAttachment Invite { get; set; } = new();
    public IReadOnlyList<LedgerDelta> OwnDeltas { get; set; } = Array.Empty<LedgerDelta>();
    public IReadOnlyList<LedgerEntry> LedgerView { get; set; } = Array.Empty<LedgerEntry>();
    public CaseGuide Guide { get; set; } = new();
    public IReadOnlyDictionary<string, InviteSlot>? KnownSlots { get; set; }

    // 1-based wave of the thread (LedgerMerger.EntriesFor stamps it on the normalized entries)
    public int WaveIndex { get; set; }

    /// <summary>D-24: zone every new meeting slot is derived in, so invites for one meeting agree across senders; empty = the sender's zone.</summary>
    public string CanonicalTimeZoneId { get; set; } = string.Empty;
}

/// <summary>
/// One meeting an invite may refer to, before schedule selection.
/// </summary>
internal class InviteCandidate
{
    public InviteSource Source { get; set; }
    public DateOnly Date { get; set; }
    public TimeOnly? StatedTime { get; set; }
    public string MeetingKey { get; set; } = string.Empty;
    public string SlotSeed { get; set; } = string.Empty;
    public string MatchedId { get; set; } = string.Empty;

    // The normalized entry the key was computed from (own delta / ledger); null for fact candidates
    public LedgerEntry? Entry { get; set; }
}

/// <summary>
/// Deterministic invite schedules (D-20): the LLM supplies only title, agenda and location; code picks the meeting the
/// invite belongs to and its time. A meeting's date comes from the detail's Value (then Statement), never from
/// LedgerEntry.Date, which is the source email's date (review concern 6). An own delta, the ledger entry Build makes from
/// it and the guide fact it records share one canonical meeting key, so every invite for that meeting gets one schedule
/// per meeting key (a persisted schedule wins). Residual (T-06-16c, accepted): two same-wave threads that record one
/// meeting where only one cites the date-matching fact get different keys and may get different derived times; the date
/// always agrees. Derived times use the invite parent's time zone, so invites sent from different zones only agree once
/// the first schedule is persisted.
/// </summary>
public static partial class InviteScheduler
{
    public const string NotTiedWarning = "invite not tied to a known meeting";

    // 09:00 + 30-minute steps: 14 starts from 09:00 to 15:30, so a 60-minute slot ends by 16:30
    private const int SlotSteps = 14;
    private const int SlotStepMinutes = 30;
    private const int MinTokenLength = 4;

    private static readonly HashSet<string> StopWords = new(StringComparer.Ordinal)
    {
        "about", "after", "agenda", "also", "been", "before", "call", "from", "have", "invite", "invitation", "meet",
        "meeting", "next", "over", "please", "said", "should", "that", "their", "them", "there", "they", "this", "thread",
        "time", "update", "week", "were", "what", "when", "which", "will", "with", "would"
    };

    private static readonly string[] TimeFormats = { "h:mm tt", "hh:mm tt", "H:mm", "HH:mm" };

    public static InviteSlot Resolve(InviteRequest request)
    {
        var parent = request.Parent;
        var tz = ZoneOf(parent);
        var parentLocal = MessageTime.Local(parent);
        var notBefore = parentLocal.AddMinutes(15);

        // D-24: new slots come from one case zone; notBefore stays in the sender zone (instants compare across zones)
        var slotZone = BusinessCalendar.ResolveId(request.CanonicalTimeZoneId) ?? tz;

        var ordered = Candidates(request)
            .Select((c, i) => (Candidate: c, Index: i))
            .OrderBy(x => x.Candidate.Date)
            .ThenBy(x => (int)x.Candidate.Source)
            .ThenBy(x => x.Candidate.MeetingKey, StringComparer.Ordinal)
            .ThenBy(x => x.Index)
            .Select(x => x.Candidate);

        foreach (var candidate in ordered)
        {
            InviteSlot slot;
            if (request.KnownSlots != null && request.KnownSlots.TryGetValue(candidate.MeetingKey, out var known) && known != null)
            {
                // One schedule per meeting key: the persisted one is returned unchanged
                slot = new InviteSlot { Start = known.Start, End = known.End };
            }
            else
            {
                var (start, end) = Derive(slotZone, candidate.Date, candidate.StatedTime, candidate.SlotSeed);
                slot = new InviteSlot { Start = start, End = end };
            }

            // An invite never schedules a meeting that starts before (or right at) the email that sends it
            if (slot.Start <= notBefore)
                continue;

            slot.Source = candidate.Source;
            slot.MatchedId = candidate.MatchedId;
            slot.MeetingKey = candidate.MeetingKey;
            return slot;
        }

        var fallback = FallbackSlot(parent, request.Invite);
        fallback.Warning = true;
        fallback.WarningText = NotTiedWarning;
        return fallback;
    }

    /// <summary>
    /// Non-relevant invites use plan data only: a business-hours slot on the next business day after the email.
    /// </summary>
    public static InviteSlot ForNonRelevant(PlannedMessage parent, PlannedAttachment invite) => FallbackSlot(parent, invite);

    /// <summary>
    /// The meeting day: Value parsed with LedgerMerger.TryParseDate, else the first yyyy-MM-dd in Value, else in Statement.
    /// Never LedgerEntry.Date: that is the source email's date (review concern 6). Null when nothing parses; never throws.
    /// </summary>
    internal static DateOnly? MeetingDate(string? value, string? statement)
    {
        var trimmed = (value ?? string.Empty).Trim();
        if (trimmed.Length > 0 && LedgerMerger.TryParseDate(trimmed, out var exact))
            return exact;

        foreach (var text in new[] { value, statement })
        {
            if (string.IsNullOrEmpty(text))
                continue;
            foreach (Match match in IsoDatePattern().Matches(text))
            {
                if (DateOnly.TryParseExact(match.Value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
                    return date;
            }
        }

        return null;
    }

    /// <summary>
    /// A stated start time ("10:30 AM", "2:00 PM", "14:00") from Value, else Statement; null when none parses.
    /// </summary>
    internal static TimeOnly? MeetingTime(string? value, string? statement)
    {
        foreach (var text in new[] { value, statement })
        {
            if (string.IsNullOrEmpty(text))
                continue;
            foreach (Match match in TimePattern().Matches(text))
            {
                var meridiem = match.Groups[3].Value.Replace(".", string.Empty).ToUpperInvariant();
                var normalized = match.Groups[1].Value + ":" + match.Groups[2].Value + (meridiem.Length > 0 ? " " + meridiem : string.Empty);
                if (TimeOnly.TryParseExact(normalized, TimeFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var time))
                    return time;
            }
        }

        return null;
    }

    // Checker 06-05: an own delta, the ledger entry built from it and the fact it records name one meeting; never key by entry.Id (that id only exists after the merge)
    internal static string MeetingKeyFor(LedgerEntry entry, DateOnly meetingDate, CaseGuide guide)
    {
        foreach (var id in (entry.FactIds ?? new List<string>()).Where(id => !string.IsNullOrEmpty(id)).OrderBy(id => id, StringComparer.Ordinal))
        {
            var fact = guide?.FindFact(id);
            if (fact != null && ParseIso(fact.Date) == meetingDate)
                return "fact:" + id;
        }

        return $"mtg:{entry.SourceThreadId}:{entry.SourceDocId}:{LedgerMerger.DetailKey(entry)}";
    }

    // Same-wave threads cannot see each other's entry; a content seed gives them the same time even when Build keeps the other thread's key
    internal static string SlotSeedFor(string meetingKey, LedgerEntry? entry, DateOnly meetingDate)
    {
        if (entry == null || meetingKey.StartsWith("fact:", StringComparison.Ordinal))
            return meetingKey;
        return "mtg~" + LedgerMerger.DetailKey(entry) + "|" + meetingDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Every compatible meeting on or after the parent's local date, in source order (own deltas, ledger, facts), one per
    /// meeting key (first kept).
    /// </summary>
    internal static List<InviteCandidate> Candidates(InviteRequest request)
    {
        var all = new List<InviteCandidate>();
        all.AddRange(OwnDeltaCandidates(request));
        all.AddRange(LedgerCandidates(request));
        all.AddRange(FactCandidates(request));

        var seen = new HashSet<string>(StringComparer.Ordinal);
        return all.Where(c => seen.Add(c.MeetingKey)).ToList();
    }

    /// <summary>
    /// Own Meeting deltas on the parent message or its ancestors, keyed by the entry the ledger will hold (before dedupe).
    /// </summary>
    internal static List<InviteCandidate> OwnDeltaCandidates(InviteRequest request)
    {
        var result = new List<InviteCandidate>();
        var thread = request.Thread;
        var parent = request.Parent;
        var guide = request.Guide;
        if (thread == null || parent == null || request.OwnDeltas == null || request.OwnDeltas.Count == 0)
            return result;

        var parentDate = MessageTime.LocalDate(parent);
        var visible = new HashSet<int>(KnowledgeGate.Ancestors(thread, parent.Seq)) { parent.Seq };
        var threadDocIds = new HashSet<string>((thread.Messages ?? new List<PlannedMessage>())
            .Where(m => m != null && !string.IsNullOrEmpty(m.DocId)).Select(m => m.DocId), StringComparer.Ordinal);
        var inviteText = InviteText(request);
        var planFacts = PlanFacts(request);

        // Checker 06-05: key the entry the ledger will hold, never the raw delta
        var threadEntries = LedgerMerger.EntriesFor(thread, request.OwnDeltas, guide, request.WaveIndex);
        foreach (var delta in request.OwnDeltas)
        {
            if (delta == null || delta.Kind != LedgerKind.Meeting || !visible.Contains(delta.Seq))
                continue;

            var normalized = LedgerMerger.EntriesFor(thread, new[] { delta }, guide, request.WaveIndex).FirstOrDefault();
            if (normalized == null)
                continue;

            // In-thread duplicate: EntriesFor keeps the first copy (its SourceDocId, unioned FactIds)
            var canonical = threadEntries.FirstOrDefault(e => LedgerMerger.SameDetail(e, normalized)
                && (threadDocIds.Contains(e.SourceDocId) || e.SourceDocId == normalized.SourceDocId)) ?? normalized;

            // Checker 06-05 warning: Build absorbs this delta into the earlier entry, so the invite must use that entry's key
            var earlier = (request.LedgerView ?? Array.Empty<LedgerEntry>())
                .Where(e => e != null && e.Kind == LedgerKind.Meeting && LedgerMerger.SameDetail(e, canonical))
                .OrderBy(e => e.WaveIndex)
                .ThenBy(e => e.Id, StringComparer.Ordinal)
                .FirstOrDefault();
            var effective = earlier ?? canonical;

            var date = MeetingDate(effective.Value, effective.Statement);
            if (date == null || date.Value < parentDate)
                continue;
            if (!TopicCompatible(effective.FactIds, effective.About + " " + effective.Statement, planFacts, inviteText))
                continue;

            var key = MeetingKeyFor(effective, date.Value, guide);
            result.Add(new InviteCandidate
            {
                Source = InviteSource.OwnDelta,
                Date = date.Value,
                StatedTime = MeetingTime(effective.Value, effective.Statement),
                MeetingKey = key,
                SlotSeed = SlotSeedFor(key, effective, date.Value),
                MatchedId = $"{thread.Id}:d{delta.Seq.ToString(CultureInfo.InvariantCulture)}",
                Entry = effective
            });
        }

        return result;
    }

    private static IEnumerable<InviteCandidate> LedgerCandidates(InviteRequest request)
    {
        var parentDate = MessageTime.LocalDate(request.Parent);
        var participants = Participants(request.Parent);
        var inviteText = InviteText(request);
        var planFacts = PlanFacts(request);

        foreach (var entry in request.LedgerView ?? Array.Empty<LedgerEntry>())
        {
            if (entry == null || entry.Kind != LedgerKind.Meeting)
                continue;
            if (!(entry.KnownByIds ?? new List<string>()).Any(participants.Contains))
                continue;
            if (!TopicCompatible(entry.FactIds, entry.About + " " + entry.Statement, planFacts, inviteText))
                continue;

            var date = MeetingDate(entry.Value, entry.Statement);
            if (date == null || date.Value < parentDate)
                continue;

            var key = MeetingKeyFor(entry, date.Value, request.Guide);
            yield return new InviteCandidate
            {
                Source = InviteSource.Ledger,
                Date = date.Value,
                StatedTime = MeetingTime(entry.Value, entry.Statement),
                MeetingKey = key,
                SlotSeed = SlotSeedFor(key, entry, date.Value),
                MatchedId = entry.Id,
                Entry = entry
            };
        }
    }

    private static IEnumerable<InviteCandidate> FactCandidates(InviteRequest request)
    {
        var parentDate = MessageTime.LocalDate(request.Parent);
        var participants = Participants(request.Parent);
        var inviteText = InviteText(request);
        var planFacts = PlanFacts(request);

        foreach (var id in planFacts.OrderBy(id => id, StringComparer.Ordinal))
        {
            var fact = request.Guide?.FindFact(id);
            if (fact == null)
                continue;

            var people = (fact.ActorIds ?? new List<string>()).Concat(fact.KnownByIds ?? new List<string>()).ToList();
            if (people.Count > 0 && !people.Any(participants.Contains))
                continue;
            if (!TopicCompatible(new[] { fact.Id }, fact.Title + " " + fact.Description, planFacts, inviteText))
                continue;

            var date = ParseIso(fact.Date);
            if (date == null || date.Value < parentDate)
                continue;

            var key = "fact:" + fact.Id;
            yield return new InviteCandidate
            {
                Source = InviteSource.Fact,
                Date = date.Value,
                MeetingKey = key,
                SlotSeed = SlotSeedFor(key, null, date.Value),
                MatchedId = fact.Id
            };
        }
    }

    // Start = stated time, else a 30-minute grid step from 09:00; duration 30 or 60 minutes, all from one rank
    private static (DateTimeOffset Start, DateTimeOffset End) Derive(TimeZoneInfo tz, DateOnly date, TimeOnly? stated, string seed)
    {
        var rank = RenderAssignments.StableRank(seed + ":invite-slot");
        var time = stated ?? BusinessCalendar.DayStart.AddMinutes(SlotStepMinutes * (int)(rank % SlotSteps));
        var minutes = (rank / SlotSteps) % 2 == 0 ? 30 : 60;
        var start = BusinessCalendar.At(tz, date, time);
        return (start, start.AddMinutes(minutes));
    }

    private static InviteSlot FallbackSlot(PlannedMessage parent, PlannedAttachment invite)
    {
        var tz = ZoneOf(parent);
        var day = BusinessCalendar.NextBusinessDay(MessageTime.LocalDate(parent).AddDays(1));
        var (start, end) = Derive(tz, day, null, invite?.DocId ?? string.Empty);
        return new InviteSlot { Start = start, End = end, Source = InviteSource.Fallback };
    }

    private static TimeZoneInfo ZoneOf(PlannedMessage parent) => BusinessCalendar.ResolveId(parent?.TimeZoneId) ?? TimeZoneInfo.Utc;

    private static HashSet<string> Participants(PlannedMessage parent) =>
        new[] { parent.FromId }
            .Concat(parent.ToIds ?? new List<string>())
            .Concat(parent.CcIds ?? new List<string>())
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id.Trim())
            .ToHashSet(StringComparer.Ordinal);

    private static HashSet<string> PlanFacts(InviteRequest request) =>
        (request.Thread?.FactIds ?? new List<string>())
            .Concat(request.Parent?.FactIds ?? new List<string>())
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id.Trim())
            .ToHashSet(StringComparer.Ordinal);

    private static string InviteText(InviteRequest request) =>
        $"{request.Invite?.Title} {request.Invite?.Description} {request.Thread?.Subject}";

    // Shares a plan fact, or a significant word appears whole-word in both texts
    private static bool TopicCompatible(IEnumerable<string>? candidateFacts, string candidateText, HashSet<string> planFacts, string inviteText)
    {
        if ((candidateFacts ?? Array.Empty<string>()).Any(planFacts.Contains))
            return true;
        var inviteTokens = Tokens(inviteText);
        return Tokens(candidateText).Any(inviteTokens.Contains);
    }

    private static HashSet<string> Tokens(string? text) =>
        WordPattern().Matches((text ?? string.Empty).ToLowerInvariant())
            .Select(m => m.Value)
            .Where(w => w.Length >= MinTokenLength && !StopWords.Contains(w))
            .ToHashSet(StringComparer.Ordinal);

    private static DateOnly? ParseIso(string? text) =>
        DateOnly.TryParseExact((text ?? string.Empty).Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? date
            : null;

    [GeneratedRegex(@"\b\d{4}-\d{2}-\d{2}\b")]
    private static partial Regex IsoDatePattern();

    [GeneratedRegex(@"\b(\d{1,2}):(\d{2})(?:\s*([AaPp]\.?[Mm]\.?))?")]
    private static partial Regex TimePattern();

    [GeneratedRegex(@"[\p{L}\p{Nd}]+")]
    private static partial Regex WordPattern();
}
