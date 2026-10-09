using System.Globalization;
using ReelDiscovery.Models.Case;
using ReelDiscovery.Models.Discovery;
using ReelDiscovery.Services.Ai;
using ReelDiscovery.Services.Corpus;
using ReelDiscovery.Services.Evidence;

namespace ReelDiscovery.Services.Discovery;

/// <summary>
/// Result of writing one deposition: the content when every chunk produced usable testimony, otherwise the reason it
/// failed (D-05). Cost covers every attempt of every chunk, including failed and refused ones (PROV-07).
/// </summary>
public class DepositionWriteOutcome
{
    public DepositionContent? Content { get; set; }
    public List<DiscoveryViolation> Warnings { get; set; } = new();
    public string Error { get; set; } = string.Empty;
    public bool Refused { get; set; }
    public bool TimedOut { get; set; }

    // A fatal provider error (bad key, spend limit) stopped the run before this document finished; leave it pending
    public bool NotRun { get; set; }

    public CostBreakdown Cost { get; set; } = new();
}

/// <summary>
/// One deposition unit of work (D-23, D-26, D-28): chunks are written in order on the Story tier with the shared
/// byte-identical system prompt, each checked by <see cref="DiscoveryValidator"/>, retried with feedback up to
/// <see cref="MaxAttempts"/> times and the best attempt kept. Code owns structure: speaker ids, exhibit markers,
/// honorifics, overflow trimming and the end time (D-04, D-21). Never throws except on user cancellation.
/// </summary>
public sealed class DepositionWriter
{
    // D-26: one attempt plus two retries with feedback
    public const int MaxAttempts = 3;

    // Overflow allowance over the target testimony pages before trimming
    public const double MaxPageOverflow = 1.25;

    // Trimming never empties a topic
    private const int MinTurnsPerTopic = 2;

    private const string TimeoutPrefix = "The request timed out";

    private readonly ILlmGateway _llm;

    public DepositionWriter(ILlmGateway llm)
    {
        _llm = llm ?? throw new ArgumentNullException(nameof(llm));
    }

    /// <summary>
    /// Writes every chunk of <paramref name="plan"/> sequentially and assembles the transcript content.
    /// <paramref name="onPart"/> receives (1-based part, part count) before each chunk.
    /// </summary>
    internal async Task<DepositionWriteOutcome> WriteAsync(CaseDraft draft, DepositionPlan plan, string caseNumber, string systemPrompt,
        string guidance, PlanCallRun run, Action<int, int>? onPart = null)
    {
        var cost = new CostBreakdown();
        var content = BuildSkeleton(draft, plan, caseNumber);
        var warnings = new List<DiscoveryViolation>();
        var speakerIds = new HashSet<string>(plan.Speakers.Where(s => s != null).Select(s => s.SpeakerId), StringComparer.Ordinal);
        var examiningId = plan.Speakers.FirstOrDefault(s => s?.Role == SpeakerRole.Examining)?.SpeakerId ?? plan.Roles.Examining.SpeakerId;
        var defendingId = plan.Speakers.FirstOrDefault(s => s?.Role == SpeakerRole.Defending)?.SpeakerId;
        var witnessId = plan.Witness.Id;
        var reporterId = plan.Speakers.FirstOrDefault(s => s?.Role == SpeakerRole.Reporter)?.SpeakerId;
        var covered = new List<string>();
        var chunks = plan.Chunks.Where(c => c != null).ToList();

        for (var i = 0; i < chunks.Count; i++)
        {
            var chunk = chunks[i];
            onPart?.Invoke(i + 1, chunks.Count);

            var ctx = CheckContext(draft, plan, chunk, i + 1);
            var previous = content.Turns.Skip(Math.Max(0, content.Turns.Count - DiscoveryPrompts.PreviousTurnsInPrompt)).ToList();
            var coveredTopics = covered.Where(t => !string.Equals(t, chunk.TopicTitle, StringComparison.Ordinal)).ToList();

            List<DepositionTurn>? best = null;
            DiscoveryPrompts.DepositionChunkDto? bestDto = null;
            List<DiscoveryViolation>? bestViolations = null;
            List<DiscoveryViolation>? feedback = null;
            var error = string.Empty;
            var refused = false;
            var timedOut = false;

            for (var attempt = 1; attempt <= MaxAttempts; attempt++)
            {
                var request = new LlmRequest
                {
                    Tier = LlmTier.Story,
                    OperationName = DiscoveryPrompts.OperationDeposition,
                    SystemPrompt = systemPrompt,
                    UserPrompt = DiscoveryPrompts.BuildDepositionPrompt(draft, plan, chunk, previous, coveredTopics, guidance,
                        feedback ?? new List<DiscoveryViolation>()),
                    MaxOutputTokens = DiscoveryPrompts.MaxOutputTokens,
                    AllowBatch = false
                };

                var outcome = await PlanCallRunner.RunAsync<DiscoveryPrompts.DepositionChunkDto>(_llm, request, run);
                cost.Add(outcome.Cost);

                // Left pending: the next run writes it (D-05)
                if (run.IsFatal)
                    return new DepositionWriteOutcome { NotRun = true, Error = run.FatalError, Cost = cost };

                if (outcome.Value == null)
                {
                    // An earlier attempt is still the best available text
                    if (best != null)
                        break;

                    // A refusal or timeout may succeed on the next attempt; the gateway already retried transient errors
                    refused = outcome.Refused;
                    timedOut = outcome.Error.StartsWith(TimeoutPrefix, StringComparison.Ordinal);
                    error = outcome.Error.Length > 0 ? outcome.Error : "The model returned no testimony.";
                    continue;
                }

                var turns = NormalizeTurns(outcome.Value, chunk, speakerIds, examiningId, defendingId, witnessId, reporterId);
                var violations = DiscoveryValidator.CheckDepositionChunk(turns, outcome.Value.PeopleNamed ?? new List<string>(), ctx);

                if (DiscoveryValidator.IsBetter(violations, bestViolations))
                {
                    best = turns;
                    bestDto = outcome.Value;
                    bestViolations = violations;
                }

                if (violations.Count == 0)
                    break;

                feedback = violations;
            }

            // A transcript missing a topic is not kept
            if (best == null)
                return new DepositionWriteOutcome { Error = error, Refused = refused, TimedOut = timedOut, Cost = cost };

            content.Turns.AddRange(best);
            warnings.AddRange(bestViolations ?? new List<DiscoveryViolation>());
            if (i == 0)
                ApplyHonorifics(content, bestDto!);
            if (!covered.Contains(chunk.TopicTitle, StringComparer.Ordinal))
                covered.Add(chunk.TopicTitle);
        }

        TrimOverflow(content);
        content.EndTime = TranscriptLayout.ComputeEndTime(content.StartTime, TranscriptLayout.CountTestimonyLines(content));

        return new DepositionWriteOutcome { Content = content, Warnings = Dedupe(warnings), Cost = cost };
    }

    private static DepositionContent BuildSkeleton(CaseDraft draft, DepositionPlan plan, string caseNumber)
    {
        var doc = draft?.CoreDocument ?? new CoreDocument();
        return new DepositionContent
        {
            WitnessId = plan.Witness.Id,
            WitnessName = plan.Witness.FullName,
            DocumentTitle = plan.DocumentTitle,
            CaptionLines = DiscoveryBoilerplate.TranscriptCaptionLines(doc, caseNumber ?? string.Empty),
            CaseNumber = caseNumber ?? string.Empty,
            Date = plan.Schedule.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            StartTime = plan.Schedule.StartTime,
            Location = plan.Schedule.Location,
            Remote = plan.Schedule.Remote,
            ReporterName = plan.Schedule.ReporterName,
            ReporterCsr = plan.Schedule.ReporterCsr,
            VideographerName = plan.Schedule.VideographerName,
            IsMemo = DiscoveryVariants.IsMemo(DiscoveryVariants.Of(doc)),
            TargetTestimonyPages = plan.TestimonyPages,
            TopicTitles = new List<string>(plan.TopicTitles),

            // Deep copies: honorifics are set on the content, never on the plan
            Speakers = plan.Speakers.Where(s => s != null).Select(s => new DepositionSpeaker
            {
                SpeakerId = s.SpeakerId,
                FullName = s.FullName,
                Role = s.Role,
                Honorific = s.Honorific,
                FirmName = s.FirmName,
                Email = s.Email,
                PartyLabel = s.PartyLabel
            }).ToList(),
            Exhibits = plan.Exhibits.Where(e => e != null).Select(e => new DepositionExhibit
            {
                Number = e.Number,
                Description = e.Description,
                DocId = e.DocId,
                Date = e.Date,
                IssueId = e.IssueId,
                TopicIndex = e.TopicIndex,
                IsImpeachment = e.IsImpeachment
            }).ToList()
        };
    }

    private static DiscoveryCheckContext CheckContext(CaseDraft draft, DepositionPlan plan, DepositionChunk chunk, int part)
    {
        var ctx = new DiscoveryCheckContext
        {
            Draft = draft,
            Kind = DiscoveryKind.Deposition,
            Part = part,
            TargetTurns = chunk.TargetTurns,
            ExpectedExhibits = new List<int>(chunk.ExhibitNumbers),
            DepositionDate = plan.Schedule.Date
        };

        // Reporter, videographer and fallback counsel are code-issued people outside the guide cast
        foreach (var name in new[] { plan.Schedule.ReporterName, plan.Schedule.VideographerName }
                     .Concat(plan.Speakers.Where(s => s != null).Select(s => s.FullName)))
        {
            if (!string.IsNullOrWhiteSpace(name))
                ctx.ExtraNames.Add(name.Trim());
        }

        foreach (var exhibit in plan.Exhibits.Where(e => e != null && !string.IsNullOrWhiteSpace(e.DocId)))
            ctx.AllowedIdentifiers.Add(exhibit.DocId.Trim());

        return ctx;
    }

    /// <summary>
    /// T-08-34: speaker ids and exhibit numbers are accepted only from code-issued sets. Q/A belong to the examining
    /// attorney and the witness; an unrecognized objector becomes the defending attorney (examining when none); other
    /// unrecognized ids go to the examining attorney (parentheticals to the reporter). Exhibit markers outside this
    /// chunk's assignment are zeroed, repeats are dropped, and an assigned exhibit only mentioned in text is marked on
    /// its first mention. Empty turns are dropped.
    /// </summary>
    private static List<DepositionTurn> NormalizeTurns(DiscoveryPrompts.DepositionChunkDto dto, DepositionChunk chunk,
        HashSet<string> speakerIds, string examiningId, string? defendingId, string witnessId, string? reporterId)
    {
        var assigned = new HashSet<int>(chunk.ExhibitNumbers);
        var marked = new HashSet<int>();
        var turns = new List<DepositionTurn>();

        foreach (var t in (dto.Turns ?? new List<DiscoveryPrompts.TurnDto>()).Where(t => t != null))
        {
            var text = (t.Text ?? string.Empty).Trim();
            if (text.Length == 0)
                continue;

            var id = (t.SpeakerId ?? string.Empty).Trim();
            var known = speakerIds.Contains(id);
            var speaker = t.Kind switch
            {
                TurnKind.Q => examiningId,
                TurnKind.A => witnessId,
                _ when known => id,
                TurnKind.Objection => defendingId ?? examiningId,
                TurnKind.Parenthetical => reporterId ?? examiningId,
                _ => examiningId
            };

            var exhibit = assigned.Contains(t.ExhibitNumber) && marked.Add(t.ExhibitNumber) ? t.ExhibitNumber : 0;
            turns.Add(new DepositionTurn { Kind = t.Kind, SpeakerId = speaker, Text = text, ExhibitNumber = exhibit, TopicIndex = chunk.TopicIndex });
        }

        foreach (var number in chunk.ExhibitNumbers.Where(n => !marked.Contains(n)).Distinct())
        {
            var label = "Exhibit " + number.ToString(CultureInfo.InvariantCulture);
            var first = turns.FirstOrDefault(t => t.ExhibitNumber == 0 && RenderValidator.ContainsWord(t.Text, label));
            if (first != null)
            {
                first.ExhibitNumber = number;
                marked.Add(number);
            }
        }

        return turns;
    }

    // Honorifics come from the first chunk only, and only for attorneys and the witness (D-23)
    private static void ApplyHonorifics(DepositionContent content, DiscoveryPrompts.DepositionChunkDto dto)
    {
        foreach (var s in (dto.Speakers ?? new List<DiscoveryPrompts.SpeakerDto>()).Where(s => s != null))
        {
            var speaker = content.Speakers.FirstOrDefault(c => string.Equals(c.SpeakerId, (s.SpeakerId ?? string.Empty).Trim(), StringComparison.Ordinal));
            if (speaker != null && speaker.Role is SpeakerRole.Examining or SpeakerRole.Defending or SpeakerRole.Appearance or SpeakerRole.Witness)
                speaker.Honorific = s.Honorific.ToString();
        }
    }

    /// <summary>
    /// T-08-36: drops turns from the end of the last topic until the testimony fits in 125% of the target pages. When the
    /// last topic is down to <see cref="MinTurnsPerTopic"/> turns trimming moves to the topic before it. Exhibit marker
    /// turns and the answers to marked questions are never trimmed (D-21); a question left unanswered by a trim goes too.
    /// If only protected turns remain, the overflow is accepted.
    /// </summary>
    private static void TrimOverflow(DepositionContent content)
    {
        if (content.TargetTestimonyPages <= 0 || content.Turns.Count == 0)
            return;

        var maxLines = (int)(content.TargetTestimonyPages * TranscriptLayout.LinesPerPage * MaxPageOverflow);
        var lines = TranscriptLayout.CountTestimonyLines(content);
        if (lines <= maxLines)
            return;

        var topics = content.Turns.Select(t => t.TopicIndex).Distinct().ToList();
        for (var ti = topics.Count - 1; ti >= 0 && lines > maxLines; ti--)
        {
            var topic = topics[ti];
            var cursor = content.Turns.FindLastIndex(t => t.TopicIndex == topic);
            while (lines > maxLines && cursor >= 0 && content.Turns.Count(t => t.TopicIndex == topic) > MinTurnsPerTopic)
            {
                // Walk back past exhibit marker turns (and the answers to marked questions): they are never trimmed
                var j = cursor;
                while (j >= 0 && content.Turns[j].TopicIndex == topic && IsProtected(content.Turns, j))
                    j--;
                if (j < 0 || content.Turns[j].TopicIndex != topic)
                    break;

                // Estimate from the turn's own lines, then confirm with a full recount once the estimate says it fits
                var removed = content.Turns[j];
                lines -= TranscriptLayout.FormatTurn(removed, content).Count;
                content.Turns.RemoveAt(j);
                cursor = j - 1;

                // Everything after j in this topic is protected, so a question before a removed answer is now unanswered
                if (removed.Kind == TurnKind.A && cursor >= 0 && content.Turns[cursor].TopicIndex == topic
                    && content.Turns[cursor].Kind == TurnKind.Q && !IsProtected(content.Turns, cursor)
                    && content.Turns.Count(t => t.TopicIndex == topic) > MinTurnsPerTopic)
                {
                    lines -= TranscriptLayout.FormatTurn(content.Turns[cursor], content).Count;
                    content.Turns.RemoveAt(cursor);
                    cursor--;
                }

                if (lines <= maxLines)
                    lines = TranscriptLayout.CountTestimonyLines(content);
            }

            var end = content.Turns.FindLastIndex(t => t.TopicIndex == topic);
            if (end >= 0 && content.Turns[end].Kind == TurnKind.Q && !IsProtected(content.Turns, end)
                && content.Turns.Count(t => t.TopicIndex == topic) > MinTurnsPerTopic)
            {
                content.Turns.RemoveAt(end);
                lines = TranscriptLayout.CountTestimonyLines(content);
            }
        }

        // Still over budget only when exhibit turns remain: the overflow is accepted rather than losing an exhibit
    }

    // An exhibit marker turn, or the answer that directly follows a marked question
    private static bool IsProtected(List<DepositionTurn> turns, int index) =>
        turns[index].ExhibitNumber != 0
        || (turns[index].Kind == TurnKind.A && index > 0 && turns[index - 1].Kind == TurnKind.Q && turns[index - 1].ExhibitNumber != 0
            && turns[index - 1].TopicIndex == turns[index].TopicIndex);

    private static List<DiscoveryViolation> Dedupe(List<DiscoveryViolation> violations) =>
        violations
            .Where(v => v != null)
            .GroupBy(v => (v.Rule, v.Number, v.Value, v.Canonical))
            .Select(g => g.First())
            .ToList();
}
