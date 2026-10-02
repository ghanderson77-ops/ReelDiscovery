using System.ComponentModel;
using System.Globalization;
using System.Text;
using System.Text.Json.Serialization;
using ReelDiscovery.Models;
using ReelDiscovery.Models.Case;
using ReelDiscovery.Models.Corpus;
using ReelDiscovery.Services.Ai;
using ReelDiscovery.Services.Case;

namespace ReelDiscovery.Services.Corpus;

/// <summary>
/// Story-tier pass that writes each relevant thread's text plan (subject, purpose, rationale, intents, must-convey points,
/// attachment titles, links) onto the code-built skeleton. Threads are chunked by time window across all issues (D-13, PLAN-03);
/// the model never sees or returns dates, people, counts or labels as output fields (D-31).
/// </summary>
public partial class RelevantThreadFiller
{
    public const int MaxThreadsPerChunk = 20;
    public const int MaxMessagesPerChunk = 60;
    public const int ChunkMaxOutputTokens = 16_000;
    public const int EarlierFactsContext = 3;
    public const int MaxParallelCalls = 3;

    // Earlier threads listed for linkedThreadIds; the most recent ones bound the prompt at 5K-email scale
    public const int MaxEarlierThreadsListed = 40;

    private const string TaskText = "For each thread, write a realistic subject line (no RE: or FW:, at most 10 words), one sentence on what the thread is about and why it happens now, and one sentence on why it is relevant to the listed issue and fact. For each message, write what the sender does and one to three specific points it must contain, using the exact names and values above, and a document title for each planned attachment. The dates, people, order and number of messages are fixed. Messages marked 'carries the facts: no' only react to or pass on what was already said; they add no new facts. You may list earlier thread IDs a thread refers back to.";

    private readonly ILlmGateway _llm;
    private readonly int _maxParallel;

    internal static IReadOnlyList<Type> SchemaTypes { get; } = new[] { typeof(ThreadPlanBatchDto) };

    public RelevantThreadFiller(ILlmGateway llm, int maxParallel = MaxParallelCalls)
    {
        _llm = llm;
        _maxParallel = Math.Max(1, maxParallel);
    }

    /// <summary>
    /// Cuts the relevant threads into chunks in first-message order: each chunk holds at most
    /// <see cref="MaxThreadsPerChunk"/> threads and <see cref="MaxMessagesPerChunk"/> messages, every thread exactly once.
    /// </summary>
    public static List<List<PlannedThread>> BuildChunks(IReadOnlyList<PlannedThread> relevant)
    {
        var ordered = relevant
            .OrderBy(FirstSentUtc)
            .ThenBy(t => t.Id, StringComparer.Ordinal)
            .ToList();

        var chunks = new List<List<PlannedThread>>();
        var current = new List<PlannedThread>();
        var messages = 0;
        foreach (var thread in ordered)
        {
            var count = thread.Messages.Count;
            if (current.Count > 0 && (current.Count >= MaxThreadsPerChunk || messages + count > MaxMessagesPerChunk))
            {
                chunks.Add(current);
                current = new List<PlannedThread>();
                messages = 0;
            }

            current.Add(thread);
            messages += count;
        }

        if (current.Count > 0)
            chunks.Add(current);
        return chunks;
    }

    /// <summary>
    /// The chunk request: name rules, case synopsis, participants, canonical values, the facts of the window (plus up to
    /// <see cref="EarlierFactsContext"/> earlier facts), earlier thread ids, one block per thread and the task. No format
    /// instructions: the strict schema defines the response shape.
    /// </summary>
    internal static string BuildChunkPrompt(CaseDraft draft, IReadOnlyList<PlannedThread> chunk, IReadOnlyList<PlannedThread> earlier, string guidance)
    {
        var guide = draft.Guide ?? new CaseGuide();
        var castById = guide.Cast
            .Where(c => !string.IsNullOrWhiteSpace(c.Id))
            .GroupBy(c => c.Id, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        var factById = guide.Facts
            .Where(f => !string.IsNullOrWhiteSpace(f.Id))
            .GroupBy(f => f.Id, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        var sb = new StringBuilder();
        CaseGuidePrompts.AppendNameRules(sb, draft);
        sb.AppendLine();
        CorpusPlanPrompts.AppendCaseSynopsis(sb, draft);

        // People: every participant of the chunk, in cast order
        var participants = chunk
            .SelectMany(t => t.Messages)
            .SelectMany(m => new[] { m.FromId }.Concat(m.ToIds).Concat(m.CcIds))
            .ToHashSet(StringComparer.Ordinal);
        sb.AppendLine();
        sb.AppendLine("People in these threads:");
        CorpusPlanPrompts.AppendCast(sb, guide, participants);

        if (guide.Values.Any(v => !string.IsNullOrWhiteSpace(v.Label) || !string.IsNullOrWhiteSpace(v.Value)))
        {
            sb.AppendLine();
            sb.AppendLine("Names and values to use exactly:");
            CorpusPlanPrompts.AppendValues(sb, guide);
        }

        // Facts in the window (plus any fact a chunk thread carries), then the facts just before it
        var days = chunk.SelectMany(t => t.Messages).Select(LocalDay).ToList();
        var windowStart = days.Count > 0 ? days.Min() : DateOnly.MinValue;
        var windowEnd = days.Count > 0 ? days.Max() : DateOnly.MaxValue;
        var chunkFactIds = chunk.SelectMany(t => t.FactIds).ToHashSet(StringComparer.Ordinal);
        var dated = guide.Facts
            .Select(f => (Fact: f, Date: SkeletonContext.FactDate(f)))
            .Where(x => x.Date != null)
            .OrderBy(x => x.Date)
            .ThenBy(x => x.Fact.Id, StringComparer.Ordinal)
            .ToList();
        var inWindow = dated
            .Where(x => (x.Date >= windowStart && x.Date <= windowEnd) || chunkFactIds.Contains(x.Fact.Id))
            .ToList();
        var inWindowIds = inWindow.Select(x => x.Fact.Id).ToHashSet(StringComparer.Ordinal);
        var firstInWindow = inWindow.Count > 0 ? inWindow.Min(x => x.Date!.Value) : windowStart;
        var before = dated
            .Where(x => x.Date < firstInWindow && !inWindowIds.Contains(x.Fact.Id))
            .TakeLast(EarlierFactsContext)
            .ToList();

        if (before.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("Already happened (earlier facts, for context):");
            foreach (var (fact, _) in before)
                sb.AppendLine(FactLine(fact));
        }

        if (inWindow.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("Facts in this period:");
            foreach (var (fact, _) in inWindow)
                sb.AppendLine(FactLine(fact));
        }

        var earlierListed = earlier.TakeLast(MaxEarlierThreadsListed).ToList();
        if (earlierListed.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("Earlier threads you may refer back to:");
            foreach (var thread in earlierListed)
                sb.AppendLine($"{thread.Id} | facts: {string.Join("; ", thread.FactIds.Select(id => FactTitle(factById, id)))}");
        }

        sb.AppendLine();
        sb.AppendLine("Threads to plan:");
        foreach (var thread in chunk)
        {
            sb.AppendLine();
            var facts = string.Join("; ", thread.FactIds.Select(id => $"{id} {FactTitle(factById, id)}".TrimEnd()));
            sb.AppendLine($"Thread {thread.Id} | facts: {facts} | issues: {string.Join(", ", thread.IssueIds)} | privileged: {(thread.IsPrivileged ? "yes" : "no")}");
            foreach (var message in thread.Messages.OrderBy(m => m.Seq))
                sb.AppendLine(MessageLine(thread.Id, message, castById));
        }

        if (!string.IsNullOrWhiteSpace(guidance))
        {
            sb.AppendLine();
            sb.AppendLine($"Guidance from the user: {guidance.Trim()}");
        }

        sb.AppendLine();
        sb.AppendLine(TaskText);
        return sb.ToString();
    }

    // "T-0003 msg 2 | ReplyAll | from CH-002 Tom Becker to CH-004 Lisa Chen; CH-003 Raymond Harmon | cc - | 2024-03-19 09:42 America/Phoenix | carries the facts: yes | attachments: Word"
    private static string MessageLine(string threadId, PlannedMessage message, IReadOnlyDictionary<string, CastMember> castById)
    {
        var to = message.ToIds.Count > 0 ? string.Join("; ", message.ToIds.Select(id => Person(castById, id))) : "-";
        var cc = message.CcIds.Count > 0 ? string.Join("; ", message.CcIds.Select(id => Person(castById, id))) : "-";
        var zone = string.IsNullOrWhiteSpace(message.TimeZoneId) ? "UTC" : message.TimeZoneId.Trim();
        var when = LocalTime(message).ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
        var carries = message.Basis == RelevanceBasis.Own ? "yes" : "no";
        var attachments = message.Attachments.Count > 0 ? string.Join(", ", message.Attachments.Select(a => a.Kind.ToString())) : "none";
        return $"{threadId} msg {message.Seq} | {message.Kind} | from {Person(castById, message.FromId)} to {to} | cc {cc} | {when} {zone} | carries the facts: {carries} | attachments: {attachments}";
    }

    private static string FactLine(KeyFact fact) =>
        $"{fact.Id} | {fact.Date.Trim()} | {fact.Title.Trim()} | {fact.Description.Trim()}";

    private static string FactTitle(IReadOnlyDictionary<string, KeyFact> factById, string id) =>
        factById.TryGetValue(id, out var fact) ? fact.Title.Trim() : string.Empty;

    private static string Person(IReadOnlyDictionary<string, CastMember> castById, string id) =>
        castById.TryGetValue(id, out var member) && !string.IsNullOrWhiteSpace(member.FullName) ? $"{id} {member.FullName.Trim()}" : id;

    // Sender-local clock time: the stored offset is sender-local, converted again when the zone id resolves
    private static DateTimeOffset LocalTime(PlannedMessage message)
    {
        var tz = BusinessCalendar.ResolveId(message.TimeZoneId);
        return tz != null ? TimeZoneInfo.ConvertTime(message.SentAt, tz) : message.SentAt;
    }

    private static DateOnly LocalDay(PlannedMessage message) => DateOnly.FromDateTime(LocalTime(message).DateTime);

    private static DateTime FirstSentUtc(PlannedThread thread) =>
        thread.Messages.Count > 0 ? thread.Messages.Min(m => m.SentAt.UtcDateTime) : DateTime.MaxValue;

    // ----- LLM chunk DTOs (strict-schema safe: no nullable members, no dates, people, counts or labels) -----

    internal class ThreadPlanBatchDto
    {
        [JsonPropertyName("threads")]
        [Description("One entry per thread listed in the request, using its threadId")]
        public List<ThreadPlanDto> Threads { get; set; } = new();
    }

    internal class ThreadPlanDto
    {
        [JsonPropertyName("threadId")]
        [Description("Thread ID from the request, e.g. T-0042")]
        public string ThreadId { get; set; } = string.Empty;

        [JsonPropertyName("subject")]
        [Description("Realistic email subject line, no RE:/FW: prefix, at most 10 words")]
        public string Subject { get; set; } = string.Empty;

        [JsonPropertyName("purpose")]
        [Description("One sentence: what this thread is about and why it happens now")]
        public string Purpose { get; set; } = string.Empty;

        [JsonPropertyName("rationale")]
        [Description("One sentence: why this thread is relevant to the listed issue and fact")]
        public string Rationale { get; set; } = string.Empty;

        [JsonPropertyName("linkedThreadIds")]
        [Description("Thread IDs from the request that this thread refers back to; empty if none")]
        public List<string> LinkedThreadIds { get; set; } = new();

        [JsonPropertyName("messages")]
        [Description("One entry per message in the request, by seq")]
        public List<MessagePlanDto> Messages { get; set; } = new();
    }

    internal class MessagePlanDto
    {
        [JsonPropertyName("seq")]
        [Description("Message number from the request")]
        public int Seq { get; set; }

        [JsonPropertyName("intent")]
        [Description("One sentence: what the sender does in this message")]
        public string Intent { get; set; } = string.Empty;

        [JsonPropertyName("mustConvey")]
        [Description("One to three specific points the message must contain, using the exact names and values given")]
        public List<string> MustConvey { get; set; } = new();

        [JsonPropertyName("attachmentTitles")]
        [Description("Document title for each planned attachment, in order; empty if none")]
        public List<string> AttachmentTitles { get; set; } = new();
    }
}
