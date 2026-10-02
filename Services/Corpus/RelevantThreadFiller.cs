using System.ComponentModel;
using System.Globalization;
using System.Text;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using ReelDiscovery.Helpers;
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
    /// Writes the text plan of every relevant thread in place. Structure (dates, people, order, links to facts and issues,
    /// labels) is never changed. Chunks run in parallel (bounded); results are applied on the calling thread after all
    /// calls finish. Threads still missing after one re-ask per chunk get code templates and UsesFallbackContent (D-19).
    /// A fatal provider error returns with FatalError set; a user cancel throws OperationCanceledException (D-18).
    /// </summary>
    public async Task<FillResult> FillAsync(CaseDraft draft, IReadOnlyList<PlannedThread> relevant, string guidance,
        IProgress<PlanProgress>? progress = null, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        guidance ??= string.Empty;

        var chunks = BuildChunks(relevant);
        var n = chunks.Count;
        var result = new FillResult { TotalChunks = n };
        if (n == 0)
            return result;

        var knownTokens = KnownTokens(draft, relevant);

        // Earlier threads per chunk (all threads of the previous chunks), fixed for the first pass and the re-ask
        var earlierLists = new List<List<PlannedThread>>(n);
        var running = new List<PlannedThread>();
        foreach (var chunk in chunks)
        {
            earlierLists.Add(running.ToList());
            running.AddRange(chunk);
        }

        // Build every prompt before any call so the draft is only read here
        var prompts = chunks.Select((chunk, i) => BuildChunkPrompt(draft, chunk, earlierLists[i], guidance)).ToList();

        var refused = new bool[n];
        var errors = new string[n];
        Array.Fill(errors, string.Empty);

        using var run = new PlanCallRun(ct, _maxParallel, "Click Retry Planning.");

        // First pass: one call per chunk; tasks only return DTOs and never touch the threads
        var firstTasks = new List<Task<PlanCallOutcome<ThreadPlanBatchDto>>>(n);
        for (var i = 0; i < n; i++)
        {
            var index = i;
            var request = ChunkRequest(prompts[index], index, n, retry: false);
            firstTasks.Add(PlanCallRunner.RunAsync<ThreadPlanBatchDto>(_llm, request, run,
                onStart: () => progress?.Report(new PlanProgress
                {
                    Stage = PlanStage.PlanningRelevant,
                    Message = $"Planning relevant threads ({index + 1} of {n})",
                    StageDone = index,
                    StageTotal = n
                })));
        }

        // A user cancel propagates from WhenAll before anything is applied to the threads
        var first = await Task.WhenAll(firstTasks);

        if (run.IsFatal)
        {
            result.FatalError = run.FatalError;
            return result;
        }

        for (var i = 0; i < n; i++)
            Record(first[i], chunks[i], earlierLists[i], i, knownTokens, refused, errors, result);

        // Re-ask once (D-19): per chunk, only the threads still missing text; refused chunks are not re-sent
        ct.ThrowIfCancellationRequested();
        var retryChunks = new List<(int Index, List<PlannedThread> Missing)>();
        for (var i = 0; i < n; i++)
        {
            if (refused[i])
                continue;
            var missing = chunks[i].Where(IsMissing).ToList();
            if (missing.Count > 0)
                retryChunks.Add((i, missing));
        }

        if (retryChunks.Count > 0)
        {
            var retryTasks = retryChunks
                .Select(r => PlanCallRunner.RunAsync<ThreadPlanBatchDto>(_llm,
                    ChunkRequest(BuildChunkPrompt(draft, r.Missing, earlierLists[r.Index], guidance), r.Index, n, retry: true), run))
                .ToList();
            var second = await Task.WhenAll(retryTasks);

            if (run.IsFatal)
            {
                result.FatalError = run.FatalError;
                return result;
            }

            for (var k = 0; k < retryChunks.Count; k++)
            {
                var (index, missing) = retryChunks[k];
                Record(second[k], missing, earlierLists[index], index, knownTokens, refused, errors, result, chunkThreads: chunks[index]);
            }
        }

        // Outcome counts and notes
        for (var i = 0; i < n; i++)
        {
            if (refused[i])
                result.RefusedChunks++;
            else if (errors[i].Length > 0)
            {
                result.FailedChunks++;
                result.Notes.Add($"Relevant threads part {i + 1} of {n} was not planned: {errors[i]}");
            }
        }

        // Templates for whatever the model still did not plan
        var templated = 0;
        foreach (var thread in chunks.SelectMany(c => c))
        {
            if (ApplyTemplates(draft, thread))
                templated++;
        }

        if (templated == 1)
            result.Notes.Add("1 relevant thread uses a placeholder subject because the model did not plan it.");
        else if (templated > 1)
            result.Notes.Add($"{templated} relevant threads use placeholder subjects because the model did not plan them.");

        return result;
    }

    private static LlmRequest ChunkRequest(string prompt, int index, int total, bool retry) => new()
    {
        Tier = LlmTier.Story,
        OperationName = $"Corpus Plan - Relevant threads (chunk {index + 1} of {total})" + (retry ? " retry" : string.Empty),
        SystemPrompt = CorpusPlanPrompts.SystemPrompt,
        UserPrompt = prompt,
        MaxOutputTokens = ChunkMaxOutputTokens
    };

    // Applies one outcome to the requested threads and records refusal/error state for the chunk
    private static void Record(PlanCallOutcome<ThreadPlanBatchDto> outcome, IReadOnlyList<PlannedThread> requested,
        IReadOnlyList<PlannedThread> earlier, int index, HashSet<string> knownTokens, bool[] refused, string[] errors,
        FillResult result, IReadOnlyList<PlannedThread>? chunkThreads = null)
    {
        if (outcome.Value != null)
        {
            errors[index] = string.Empty;
            var linkable = (chunkThreads ?? requested).Concat(earlier).Select(t => t.Id).ToHashSet(StringComparer.Ordinal);
            ApplyBatch(outcome.Value, requested, linkable, knownTokens);
            return;
        }

        if (outcome.Refused)
        {
            refused[index] = true;
            if (result.RefusalMessage.Length == 0)
                result.RefusalMessage = outcome.RefusalMessage;
            return;
        }

        errors[index] = outcome.Error.Length > 0 ? outcome.Error : "The request did not return a plan.";
    }

    /// <summary>
    /// Maps a response strictly by canonical thread id and seq onto the requested threads; unknown threads, messages and
    /// links are dropped and unknown CH-/F-/ISS-/T-/V-/ORG- tokens are stripped from the text (T-04-37).
    /// </summary>
    private static void ApplyBatch(ThreadPlanBatchDto batch, IReadOnlyList<PlannedThread> requested, HashSet<string> linkable, HashSet<string> knownTokens)
    {
        var byId = requested.ToDictionary(t => t.Id, StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var dto in batch.Threads ?? new List<ThreadPlanDto>())
        {
            if (dto == null)
                continue;
            var id = CanonicalId(dto.ThreadId);
            if (!byId.TryGetValue(id, out var thread) || !seen.Add(id))
                continue;

            var subject = StripReplyPrefix(Clean(dto.Subject, knownTokens));
            if (subject.Length > 0)
                thread.Subject = subject;
            var purpose = Clean(dto.Purpose, knownTokens);
            if (purpose.Length > 0)
                thread.Purpose = purpose;
            var rationale = Clean(dto.Rationale, knownTokens);
            if (rationale.Length > 0)
                thread.Rationale = rationale;

            thread.LinkedThreadIds = (dto.LinkedThreadIds ?? new List<string>())
                .Select(CanonicalId)
                .Where(l => l != thread.Id && linkable.Contains(l))
                .Distinct(StringComparer.Ordinal)
                .ToList();

            var bySeq = thread.Messages.GroupBy(m => m.Seq).ToDictionary(g => g.Key, g => g.First());
            var seenSeq = new HashSet<int>();
            foreach (var messageDto in dto.Messages ?? new List<MessagePlanDto>())
            {
                if (messageDto == null || !bySeq.TryGetValue(messageDto.Seq, out var message) || !seenSeq.Add(messageDto.Seq))
                    continue;

                var intent = Clean(messageDto.Intent, knownTokens);
                if (intent.Length > 0)
                    message.Intent = intent;

                var points = (messageDto.MustConvey ?? new List<string>())
                    .Select(p => Clean(p, knownTokens))
                    .Where(p => p.Length > 0)
                    .Distinct(StringComparer.Ordinal)
                    .Take(3)
                    .ToList();
                if (points.Count > 0)
                    message.MustConvey = points;

                var titles = messageDto.AttachmentTitles ?? new List<string>();
                for (var k = 0; k < message.Attachments.Count && k < titles.Count; k++)
                {
                    var title = Clean(titles[k], knownTokens);
                    if (title.Length == 0)
                        continue;
                    var attachment = message.Attachments[k];
                    attachment.Title = title;
                    var fileName = FileNameFor(title, attachment.Kind);
                    if (fileName.Length > 0)
                        attachment.FileName = fileName;
                }
            }
        }
    }

    // A thread needs the re-ask when any text the answer key or renderer relies on is missing
    private static bool IsMissing(PlannedThread thread) =>
        string.IsNullOrWhiteSpace(thread.Subject)
        || string.IsNullOrWhiteSpace(thread.Purpose)
        || string.IsNullOrWhiteSpace(thread.Rationale)
        || thread.Messages.Any(m => string.IsNullOrWhiteSpace(m.Intent) || m.MustConvey.Count == 0);

    /// <summary>
    /// Fills blank text from the thread's first fact and issue; returns true (and sets UsesFallbackContent) when any
    /// template was used. Blank attachment titles fall back to the code file name without being flagged.
    /// </summary>
    private static bool ApplyTemplates(CaseDraft draft, PlannedThread thread)
    {
        var guide = draft.Guide ?? new CaseGuide();
        var fact = thread.FactIds.Select(id => guide.Facts.FirstOrDefault(f => f.Id == id)).FirstOrDefault(f => f != null);
        var issue = thread.IssueIds.Select(id => guide.Issues.FirstOrDefault(i => i.Id == id)).FirstOrDefault(i => i != null);
        var factTitle = string.IsNullOrWhiteSpace(fact?.Title) ? "the matter" : fact!.Title.Trim();
        var used = false;

        if (string.IsNullOrWhiteSpace(thread.Subject))
        {
            thread.Subject = TemplateSubject(factTitle);
            used = true;
        }

        if (string.IsNullOrWhiteSpace(thread.Purpose))
        {
            thread.Purpose = $"Discuss {factTitle}.";
            used = true;
        }

        if (string.IsNullOrWhiteSpace(thread.Rationale))
        {
            var factPart = fact != null ? $"{factTitle} ({fact.Id})" : factTitle;
            thread.Rationale = issue != null
                ? $"Evidence of {factPart} under {issue.Title.Trim()} ({issue.Id})."
                : $"Evidence of {factPart}.";
            used = true;
        }

        foreach (var message in thread.Messages)
        {
            if (string.IsNullOrWhiteSpace(message.Intent))
            {
                message.Intent = TemplateIntent(message.Kind, factTitle);
                used = true;
            }

            if (message.MustConvey.Count == 0)
            {
                message.MustConvey = new List<string> { factTitle };
                used = true;
            }

            foreach (var attachment in message.Attachments)
            {
                if (string.IsNullOrWhiteSpace(attachment.Title))
                    attachment.Title = Path.GetFileNameWithoutExtension(attachment.FileName);
            }
        }

        if (used)
            thread.UsesFallbackContent = true;
        return used;
    }

    /// <summary>
    /// Placeholder subject: the fact title cut to at most 10 words.
    /// </summary>
    internal static string TemplateSubject(string factTitle) =>
        string.Join(" ", (factTitle ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Take(10));

    private static string TemplateIntent(MessageKind kind, string factTitle) => kind switch
    {
        MessageKind.New => $"Raises {factTitle} with the recipients.",
        MessageKind.Reply => $"Replies about {factTitle}.",
        MessageKind.ReplyAll => $"Replies to everyone about {factTitle}.",
        MessageKind.Forward => $"Forwards the discussion of {factTitle}.",
        _ => $"Writes about {factTitle}."
    };

    // Every id the guide or this pass defines; any other id-shaped token in model text is stripped
    private static HashSet<string> KnownTokens(CaseDraft draft, IReadOnlyList<PlannedThread> relevant)
    {
        var guide = draft.Guide ?? new CaseGuide();
        var known = new HashSet<string>(StringComparer.Ordinal);
        known.UnionWith(guide.Cast.Select(c => c.Id));
        known.UnionWith(guide.Facts.Select(f => f.Id));
        known.UnionWith(guide.Issues.Select(i => i.Id));
        known.UnionWith(guide.Values.Select(v => v.Id));
        known.UnionWith(guide.Organizations.Select(o => o.Id));
        known.UnionWith(relevant.Select(t => t.Id));
        known.Remove(string.Empty);
        return known;
    }

    internal static string CanonicalId(string? id) => (id ?? string.Empty).Trim().ToUpperInvariant();

    private static string Clean(string? text, HashSet<string> knownTokens)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;
        var stripped = IdTokenPattern().Replace(text, m => knownTokens.Contains(m.Value) ? m.Value : string.Empty);
        return WhitespacePattern().Replace(stripped, " ").Trim();
    }

    private static string StripReplyPrefix(string subject)
    {
        var cleaned = ReplyPrefixPattern().Replace(subject, string.Empty).Trim();
        return cleaned.Length > 0 ? cleaned : subject;
    }

    /// <summary>
    /// "{sanitized title}{ext}": FileNameHelper sanitizing (no separators or traversal), leading/trailing dots trimmed,
    /// capped at 80 characters, extension chosen by code from the attachment kind (T-04-38). Empty when nothing usable remains.
    /// </summary>
    internal static string FileNameFor(string title, AttachmentType kind)
    {
        var stem = FileNameHelper.SanitizeForFileName(title).Trim('.', '_', ' ');
        if (stem.Length > MaxFileNameStem)
            stem = stem[..MaxFileNameStem].TrimEnd('.', '_', ' ');
        return stem.Length == 0 ? string.Empty : stem + Extension(kind);
    }

    private const int MaxFileNameStem = 80;

    private static string Extension(AttachmentType kind) => kind switch
    {
        AttachmentType.Word => ".docx",
        AttachmentType.Excel => ".xlsx",
        AttachmentType.PowerPoint => ".pptx",
        AttachmentType.Image => ".png",
        AttachmentType.CalendarInvite => ".ics",
        AttachmentType.Voicemail => ".mp3",
        _ => ".bin"
    };

    [GeneratedRegex(@"\b(?:CH|F|ISS|T|V|ORG)-\d{2,4}\b")]
    private static partial Regex IdTokenPattern();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespacePattern();

    [GeneratedRegex(@"^(?:(?:RE|FW|FWD)\s*:\s*)+", RegexOptions.IgnoreCase)]
    private static partial Regex ReplyPrefixPattern();

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
