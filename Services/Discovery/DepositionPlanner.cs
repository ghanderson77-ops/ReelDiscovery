using System.Globalization;
using ReelDiscovery.Helpers;
using ReelDiscovery.Models.Case;
using ReelDiscovery.Models.Discovery;
using ReelDiscovery.Models.Evidence;
using ReelDiscovery.Services.Case;
using ReelDiscovery.Services.Corpus;
using ReelDiscovery.Services.Evidence;

namespace ReelDiscovery.Services.Discovery;

/// <summary>
/// A key, non-counsel character who can be deposed, with the side they are on and how central they are to the facts.
/// </summary>
public class WitnessCandidate
{
    public string Id { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string OrganizationName { get; set; } = string.Empty;
    public WitnessSide Side { get; set; }

    // Core document party id; empty for a non-party witness
    public string PartyId { get; set; } = string.Empty;

    // Facts the witness acts in or knows
    public int FactCount { get; set; }

    // Facts the witness acts in
    public int ActorCount { get; set; }
}

/// <summary>
/// When, where and before whom a deposition is taken (D-14).
/// </summary>
public class DepositionSchedule
{
    public string WitnessId { get; set; } = string.Empty;
    public DateOnly Date { get; set; }
    public string StartTime { get; set; } = string.Empty;
    public string Location { get; set; } = string.Empty;
    public bool Remote { get; set; }
    public string ReporterName { get; set; } = string.Empty;
    public string ReporterCsr { get; set; } = string.Empty;
    public string VideographerName { get; set; } = string.Empty;
}

/// <summary>
/// Attorneys at a deposition (D-13). Appearances lists every attorney on the record, examining and defending first.
/// </summary>
public class DepositionRoles
{
    public DepositionSpeaker Examining { get; set; } = new();

    // Null for a non-party witness (no one defends)
    public DepositionSpeaker? Defending { get; set; }

    public List<DepositionSpeaker> Appearances { get; set; } = new();

    // Fallbacks taken because the guide lacks counsel (shown to the user)
    public List<string> Notes { get; set; } = new();
}

/// <summary>
/// One model call's worth of testimony: a part of one topic (D-15, D-23).
/// </summary>
public class DepositionChunk
{
    public int Index { get; set; }
    public int TopicIndex { get; set; }

    // Empty for the "Background and role" topic
    public string IssueId { get; set; } = string.Empty;

    public string TopicTitle { get; set; } = string.Empty;

    // 1-based part of the topic and the topic's part count
    public int Part { get; set; }
    public int PartCount { get; set; }

    public int Pages { get; set; }
    public int TargetTurns { get; set; }
    public List<string> FactIds { get; set; } = new();

    // Exhibits first shown in this chunk (only the topic's first part)
    public List<int> ExhibitNumbers { get; set; } = new();
}

/// <summary>
/// Everything canonical about one deposition, decided before any model call (D-04).
/// </summary>
public class DepositionPlan
{
    public WitnessCandidate Witness { get; set; } = new();
    public DepositionRoles Roles { get; set; } = new();
    public DepositionSchedule Schedule { get; set; } = new();
    public List<DepositionChunk> Chunks { get; set; } = new();

    // Ordered by exhibit number
    public List<DepositionExhibit> Exhibits { get; set; } = new();

    // The same exhibits as sidecar assignments (with SourceKey), for the generator to persist
    public List<ExhibitAssignment> Assignments { get; set; } = new();

    // What the witness knows as of the deposition date (Pitfall 7)
    public List<KeyFact> KnownFacts { get; set; } = new();
    public List<CanonicalValue> KnownValues { get; set; } = new();
    public List<LedgerEntry> KnownLedger { get; set; } = new();

    public List<string> TopicTitles { get; set; } = new();
    public int TestimonyPages { get; set; }
    public string DocumentTitle { get; set; } = string.Empty;
    public List<DepositionSpeaker> Speakers { get; set; } = new();
    public List<string> Notes { get; set; } = new();
}

/// <summary>
/// Pure, deterministic deposition planning (D-04 "code owns structure", D-12..D-15, D-21, D-22): witnesses, attorneys,
/// schedule, reporter, topic chunks and exhibits are decided here before any model call. Nothing here throws.
/// </summary>
public static class DepositionPlanner
{
    public const int MaxWitnesses = DiscoverySettings.MaxWitnesses;

    // Topic chunks are at most 8 testimony pages so one model call stays well inside the output budget (D-23)
    public const int MaxChunkPages = 8;

    // [ASSUMED] A1: about 14 Q/A turns fill one 25-line transcript page; calibrate on the first live run
    public const int TurnsPerPage = 14;

    // Invented names (never real court reporters, T-08-10)
    private static readonly string[] ReporterNames =
    {
        "Dana Whitcombe", "Marisol Ketter", "Graham Ollerton", "Priya Venkataram", "Colette Arbuckle", "Desmond Farrow",
        "Lena Hartsfield", "Rowan Pemberly", "Teresa Quillan", "Victor Almendra", "Joanna Strickmere", "Nolan Briarwood"
    };

    private static readonly string[] VideographerNames =
    {
        "Corbin Ashgrove", "Felicity Dunmore", "Wesley Tranholm", "Imogen Calderwood", "Jasper Fenwright", "Odessa Merriweather",
        "Ruben Halloway", "Sylvie Brackenridge"
    };

    /// <summary>
    /// Key-tier Party/Staff characters (counsel excluded), ranked by facts acted in or known, then facts acted in, then a
    /// stable per-case rank. Non-party witnesses are eligible (A5). Empty without a guide.
    /// </summary>
    public static List<WitnessCandidate> Eligible(CaseDraft draft)
    {
        var guide = draft?.Guide;
        if (guide == null)
            return new List<WitnessCandidate>();

        var caseId = CaseKey(draft!);
        var facts = (guide.Facts ?? new List<KeyFact>()).Where(f => f != null).ToList();

        return (guide.Cast ?? new List<CastMember>())
            .Where(c => c != null
                && !string.IsNullOrEmpty(c.Id)
                && c.Tier == CastTier.Key
                && c.Role is CastRole.Party or CastRole.Staff
                && !CorpusPlanPrompts.IsCounsel(c))
            .Select(c => Candidate(draft!, c, facts))
            .OrderByDescending(w => w.FactCount)
            .ThenByDescending(w => w.ActorCount)
            .ThenBy(w => RenderAssignments.StableRank(caseId + "|" + w.Id))
            .ThenBy(w => w.Id, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>
    /// Default deponents by complexity (Simple 1, Standard 2, Complex 3): defense side first, then alternating; when one
    /// side runs out the other continues; non-party witnesses only after both sides are exhausted (D-12).
    /// </summary>
    public static List<string> DefaultWitnessIds(CaseDraft draft)
    {
        var eligible = Eligible(draft);
        var wanted = draft?.Complexity switch
        {
            ComplexityLevel.Simple => 1,
            ComplexityLevel.Complex => 3,
            _ => 2
        };
        var count = Math.Min(Math.Min(wanted, MaxWitnesses), eligible.Count);

        var defense = new Queue<WitnessCandidate>(eligible.Where(w => w.Side == WitnessSide.Defense));
        var plaintiff = new Queue<WitnessCandidate>(eligible.Where(w => w.Side == WitnessSide.Plaintiff));
        var nonParty = new Queue<WitnessCandidate>(eligible.Where(w => w.Side == WitnessSide.NonParty));

        var result = new List<string>();
        for (var i = 0; result.Count < count; i++)
        {
            var preferred = i % 2 == 0 ? defense : plaintiff;
            var other = i % 2 == 0 ? plaintiff : defense;
            var queue = preferred.Count > 0 ? preferred : other.Count > 0 ? other : nonParty;
            if (queue.Count == 0)
                break;
            result.Add(queue.Dequeue().Id);
        }
        return result;
    }

    /// <summary>
    /// Transcript title (D-25). The name is only upper-cased, never altered (D-02).
    /// </summary>
    public static string DocumentTitle(DiscoveryVariant variant, string fullName)
    {
        var name = (fullName ?? string.Empty).ToUpperInvariant();
        return variant switch
        {
            DiscoveryVariant.MemoCriminal or DiscoveryVariant.MemoCivil => $"TRANSCRIPT OF SWORN WITNESS INTERVIEW OF {name}",
            DiscoveryVariant.MemoRegulatory => $"EXAMINATION UNDER OATH OF {name}",
            _ => $"DEPOSITION OF {name}"
        };
    }

    /// <summary>
    /// Examining attorney = partner of the opposing party's firm; defending attorney = partner or associate of the
    /// witness's firm (none for a non-party witness); every other party's partner appears too (D-13). Missing counsel
    /// falls back to "Counsel for {party}" with a note; honorifics stay empty (the first model chunk fills them).
    /// </summary>
    public static DepositionRoles AssignRoles(CaseDraft draft, WitnessCandidate witness)
    {
        var roles = new DepositionRoles();
        if (draft == null || witness == null)
        {
            roles.Examining = new DepositionSpeaker { SpeakerId = "COUNSEL-EXAMINING", FullName = "Examining counsel", Role = SpeakerRole.Examining };
            roles.Appearances.Add(roles.Examining);
            return roles;
        }

        var doc = draft.CoreDocument;
        var opposing = OpposingParty(draft, witness);

        // Examining attorney
        var examiner = opposing == null ? null : CounselDirectory.Partner(draft, opposing.Id);
        if (examiner != null)
        {
            roles.Examining = Speaker(draft, examiner, opposing!, SpeakerRole.Examining);
        }
        else if (opposing != null && CounselDirectory.IsPlaintiffSide(opposing.Role)
            && !string.IsNullOrWhiteSpace(doc?.Counsel?.AttorneyName))
        {
            // The plaintiff's signing attorney from the core document
            roles.Examining = new DepositionSpeaker
            {
                SpeakerId = "COUNSEL-CORE",
                FullName = doc!.Counsel.AttorneyName.Trim(),
                Role = SpeakerRole.Examining,
                FirmName = doc.Counsel.FirmName ?? string.Empty,
                Email = doc.Counsel.Email ?? string.Empty,
                PartyLabel = PartyLabel(opposing)
            };
        }
        else if (opposing != null)
        {
            roles.Examining = Fallback(opposing, SpeakerRole.Examining, roles.Notes);
        }
        else
        {
            roles.Examining = new DepositionSpeaker { SpeakerId = "COUNSEL-EXAMINING", FullName = "Examining counsel", Role = SpeakerRole.Examining };
            roles.Notes.Add("The core document names no opposing party, so the transcript says \"Examining counsel\".");
        }
        roles.Appearances.Add(roles.Examining);

        // Defending attorney (none for a non-party witness)
        var ownParty = FindParty(doc, witness.PartyId);
        if (witness.Side != WitnessSide.NonParty)
        {
            if (ownParty == null)
            {
                roles.Defending = new DepositionSpeaker { SpeakerId = "COUNSEL-WITNESS", FullName = "Counsel for the witness", Role = SpeakerRole.Defending };
                roles.Notes.Add("The witness's party is not in the core document, so the transcript says \"Counsel for the witness\".");
            }
            else
            {
                var partner = CounselDirectory.Partner(draft, ownParty.Id);
                var associate = CounselDirectory.Associate(draft, ownParty.Id);

                // Associate for variety when the stable rank picks it
                var useAssociate = associate != null
                    && (partner == null || RenderAssignments.StableRank(CaseKey(draft) + "|" + witness.Id + "|defending") % 2 == 1);
                var defender = useAssociate ? associate : partner;
                roles.Defending = defender != null
                    ? Speaker(draft, defender, ownParty, SpeakerRole.Defending)
                    : Fallback(ownParty, SpeakerRole.Defending, roles.Notes);
            }
            AddAppearance(roles, roles.Defending);
        }

        // Every other party's counsel appears on the record
        foreach (var party in (doc?.Parties ?? new List<CaseParty>()).Where(p => p != null))
        {
            if (party.Id == opposing?.Id || party.Id == ownParty?.Id)
                continue;
            var partner = CounselDirectory.Partner(draft, party.Id);
            AddAppearance(roles, partner != null
                ? Speaker(draft, partner, party, SpeakerRole.Appearance)
                : Fallback(party, SpeakerRole.Appearance, roles.Notes));
        }

        return roles;
    }

    /// <summary>
    /// Deposition dates, start times, locations, reporters and videographers in the given witness order (D-14): distinct
    /// business days 3-9 months after the filing date and after the latest dated key fact. Pure and deterministic.
    /// </summary>
    public static List<DepositionSchedule> Schedule(CaseDraft draft, IReadOnlyList<WitnessCandidate> witnesses)
    {
        var result = new List<DepositionSchedule>();
        if (draft == null || witnesses == null || witnesses.Count == 0)
            return result;

        var list = witnesses.Where(w => w != null).ToList();
        var caseId = CaseKey(draft);
        var doc = draft.CoreDocument;
        var latestFact = LatestFactDate(draft);

        // Anchor: filing date, else the latest fact, else the timeline end, else the creation date
        DateOnly anchor;
        if (CaseDraft.TryParseDate(doc?.FilingDate, out var filed))
            anchor = DateOnly.FromDateTime(filed);
        else if (latestFact != null)
            anchor = latestFact.Value;
        else if (CaseDraft.TryParseDate(doc?.TimelineEnd, out var end))
            anchor = DateOnly.FromDateTime(end);
        else
            anchor = DateOnly.FromDateTime(draft.CreatedUtc);

        var floor = latestFact?.AddDays(1) ?? DateOnly.MinValue;
        var start = Max(SafeAddMonths(anchor, 3), floor);
        var stop = Max(SafeAddMonths(anchor, 9), SafeAddDays(start, 30));
        var span = Math.Max(1, stop.DayNumber - start.DayNumber);
        var n = list.Count;

        var castNames = (draft.Guide?.Cast ?? new List<CastMember>())
            .Where(c => c != null)
            .Select(c => c.FullName)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var used = new HashSet<DateOnly>();

        for (var i = 0; i < n; i++)
        {
            var witness = list[i];
            var rank = RenderAssignments.StableRank(caseId + "|" + witness.Id);

            // Evenly spaced slots plus a stable jitter of up to half a slot
            var offset = (int)Math.Round(i * span / (double)n) + (int)(rank % (ulong)Math.Max(1, span / (2 * n)));
            var day = SafeAddDays(start, offset);
            day = BusinessCalendar.NextBusinessDay(day);
            while (used.Contains(day))
                day = BusinessCalendar.NextBusinessDay(SafeAddDays(day, 1));
            used.Add(day);

            var roles = AssignRoles(draft, witness);
            var firm = OpposingFirm(draft, witness);
            var firmName = firm?.Name ?? roles.Examining.FirmName ?? string.Empty;

            // About one in three depositions is remote; a fallback examiner with no firm is always remote
            var remote = rank % 3 == 0 || string.IsNullOrWhiteSpace(firmName);
            string location;
            if (remote)
                location = "Taken remotely via videoconference";
            else
            {
                var city = doc?.Jurisdiction?.VenueCity;
                if (string.IsNullOrWhiteSpace(city))
                    city = firm?.Location;
                location = string.IsNullOrWhiteSpace(city) ? $"the offices of {firmName}" : $"{firmName}, {city}";
            }

            var reporterRank = RenderAssignments.StableRank(caseId + "|" + witness.Id + "|reporter");
            var videoRank = RenderAssignments.StableRank(caseId + "|" + witness.Id + "|videographer");

            result.Add(new DepositionSchedule
            {
                WitnessId = witness.Id ?? string.Empty,
                Date = day,
                StartTime = rank % 2 == 0 ? "9:30 a.m." : "10:00 a.m.",
                Location = location,
                Remote = remote,
                ReporterName = PickName(ReporterNames, reporterRank, castNames),
                ReporterCsr = string.Create(CultureInfo.InvariantCulture, $"CSR No. {10000 + reporterRank % 90000}"),
                VideographerName = PickName(VideographerNames, videoRank, castNames)
            });
        }

        return result;
    }

    /// <summary>
    /// Complete plans for the selected witnesses (unknown ids ignored, at most six, pages clamped 10-60), ordered by
    /// deposition date. Exhibit numbers run across the depositions in that order; a witness's existing assignments keep
    /// their numbers and new numbers start after the highest valid existing number (D-21, T-08-09).
    /// </summary>
    public static List<DepositionPlan> Plan(CaseDraft draft, IReadOnlyList<string> witnessIds, int testimonyPages,
        DiscoveryCorpus? corpus, IReadOnlyList<ExhibitAssignment>? existing)
    {
        var plans = new List<DepositionPlan>();
        var guide = draft?.Guide;
        if (guide == null || witnessIds == null)
            return plans;

        var eligible = Eligible(draft!).ToDictionary(w => w.Id, StringComparer.Ordinal);
        var witnesses = witnessIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id.Trim())
            .Distinct(StringComparer.Ordinal)
            .Where(eligible.ContainsKey)
            .Select(id => eligible[id])
            .Take(MaxWitnesses)
            .ToList();
        if (witnesses.Count == 0)
            return plans;

        var pages = Math.Clamp(testimonyPages, DiscoverySettings.MinPages, DiscoverySettings.MaxPages);
        var schedules = Schedule(draft!, witnesses);
        var variant = DiscoveryVariants.Of(draft!.CoreDocument);

        for (var i = 0; i < witnesses.Count; i++)
        {
            var roles = AssignRoles(draft, witnesses[i]);
            plans.Add(new DepositionPlan
            {
                Witness = witnesses[i],
                Roles = roles,
                Schedule = schedules[i],
                TestimonyPages = pages,
                DocumentTitle = DocumentTitle(variant, witnesses[i].FullName),
                Notes = new List<string>(roles.Notes)
            });
        }

        // Exhibit numbering and the Documents grid both follow deposition date order (UI-SPEC S1 Card 3)
        plans = plans.OrderBy(p => p.Schedule.Date).ThenBy(p => p.Witness.Id, StringComparer.Ordinal).ToList();

        var context = new PlanContext(guide, corpus?.Usable == true ? corpus : null);

        // Untrusted sidecar numbers: non-positive and duplicate numbers are ignored (first wins)
        var valid = new List<ExhibitAssignment>();
        var seen = new HashSet<int>();
        foreach (var assignment in existing ?? Array.Empty<ExhibitAssignment>())
        {
            if (assignment != null && assignment.Number > 0 && seen.Add(assignment.Number))
                valid.Add(assignment);
        }
        var next = valid.Count == 0 ? 1 : valid.Max(a => a.Number) + 1;

        foreach (var plan in plans)
        {
            FillKnowledge(plan, context);
            FillChunks(plan, context);
            next = FillExhibits(plan, context, valid, next);
            FillSpeakers(plan, guide);
        }

        return plans;
    }

    private static void FillKnowledge(DepositionPlan plan, PlanContext context)
    {
        var id = plan.Witness.Id;
        var asOf = plan.Schedule.Date;

        plan.KnownFacts = context.Facts.Where(f => KnowledgeGate.KnowsFact(f, id, asOf)).ToList();
        plan.KnownValues = context.Values
            .Where(v => KnowledgeGate.KnowsValue(KnowledgeGate.Knowability(v, context.Guide), id, asOf))
            .ToList();
        plan.KnownLedger = (context.Corpus?.Ledger ?? new List<LedgerEntry>())
            .Where(e => e != null
                && (e.KnownByIds ?? new List<string>()).Contains(id)
                && CaseDraft.TryParseDate(e.Date, out var date)
                && DateOnly.FromDateTime(date) <= asOf)
            .ToList();
    }

    private static void FillChunks(DepositionPlan plan, PlanContext context)
    {
        // Group known facts by their first guide issue, in guide issue order
        var byIssue = new Dictionary<string, List<KeyFact>>(StringComparer.Ordinal);
        var unassigned = new List<KeyFact>();
        foreach (var fact in plan.KnownFacts)
        {
            var issueId = context.FirstIssue(fact);
            if (issueId == null)
            {
                unassigned.Add(fact);
                continue;
            }
            if (!byIssue.TryGetValue(issueId, out var list))
                byIssue[issueId] = list = new List<KeyFact>();
            list.Add(fact);
        }

        var topics = byIssue
            .OrderBy(kv => context.IssueOrder[kv.Key])
            .Select(kv => (IssueId: kv.Key, Title: context.IssueTitle(kv.Key), Facts: kv.Value))
            .ToList();
        if (topics.Count == 0)
            topics.Add((string.Empty, "Background and role", unassigned));
        else
            topics[0].Facts.AddRange(unassigned);

        // At least one page per topic: keep the most fact-heavy topics (ties in guide order), shown in guide order
        if (topics.Count > plan.TestimonyPages)
        {
            topics = topics
                .Select((t, index) => (Topic: t, Index: index))
                .OrderByDescending(x => x.Topic.Facts.Count)
                .ThenBy(x => x.Index)
                .Take(plan.TestimonyPages)
                .OrderBy(x => x.Index)
                .Select(x => x.Topic)
                .ToList();
        }

        var budgets = Apportion.Largest(plan.TestimonyPages, topics.Select(t => (double)t.Facts.Count).ToList(), min: 1);
        plan.TopicTitles = topics.Select(t => t.Title).ToList();
        plan.Chunks = new List<DepositionChunk>();

        for (var t = 0; t < topics.Count; t++)
        {
            // Budgets above 8 pages split into near-equal parts (20 -> 7, 7, 6)
            var budget = budgets[t];
            var parts = Math.Max(1, (budget + MaxChunkPages - 1) / MaxChunkPages);
            var size = budget / parts;
            var extra = budget % parts;
            for (var part = 0; part < parts; part++)
            {
                var chunkPages = size + (part < extra ? 1 : 0);
                plan.Chunks.Add(new DepositionChunk
                {
                    Index = plan.Chunks.Count,
                    TopicIndex = t,
                    IssueId = topics[t].IssueId,
                    TopicTitle = topics[t].Title,
                    Part = part + 1,
                    PartCount = parts,
                    Pages = chunkPages,
                    TargetTurns = chunkPages * TurnsPerPage,
                    FactIds = topics[t].Facts.Select(f => f.Id).ToList()
                });
            }
        }
    }

    private static int FillExhibits(DepositionPlan plan, PlanContext context, List<ExhibitAssignment> existing, int next)
    {
        var witnessId = plan.Witness.Id;
        var selections = new List<ExhibitSource>();

        var reused = existing.Where(a => string.Equals(a.WitnessId, witnessId, StringComparison.Ordinal)).OrderBy(a => a.Number).ToList();
        if (reused.Count > 0)
        {
            // Regenerating a deposition keeps its exhibits verbatim (same numbers, same descriptions)
            foreach (var a in reused)
            {
                var source = context.SourceFor(a);
                source.Number = a.Number;
                selections.Add(source);
            }
        }
        else
        {
            // About one exhibit per 5 testimony pages, 3 to 8 per deposition, capped by what is available
            var wanted = Math.Clamp((int)Math.Round(plan.TestimonyPages / 5.0, MidpointRounding.AwayFromZero), 3, 8);
            var ranked = context.Corpus != null
                ? context.CorpusSources(plan.Witness.Id, plan.Schedule.Date)
                : context.ValueSources(plan.KnownValues);
            selections = ranked.Take(wanted).ToList();

            // Number in the order the transcript introduces them: topic, then date
            foreach (var source in selections
                .OrderBy(s => TopicIndexOf(plan, s.IssueId))
                .ThenBy(s => s.Date, StringComparer.Ordinal)
                .ThenBy(s => s.SourceKey, StringComparer.Ordinal))
            {
                source.Number = next++;
            }
        }

        // D-22: the top-ranked (hot first) exhibit impeaches an adverse witness
        var impeachment = plan.Witness.Side is WitnessSide.Plaintiff or WitnessSide.Defense
            ? selections.OrderBy(s => s.Rank).FirstOrDefault()
            : null;

        foreach (var source in selections.OrderBy(s => s.Number))
        {
            var exhibit = new DepositionExhibit
            {
                Number = source.Number,
                Description = source.Description,
                DocId = source.DocId,
                Date = source.Date,
                IssueId = source.IssueId,
                TopicIndex = TopicIndexOf(plan, source.IssueId),
                IsImpeachment = ReferenceEquals(source, impeachment)
            };
            plan.Exhibits.Add(exhibit);
            plan.Assignments.Add(new ExhibitAssignment
            {
                Number = source.Number,
                WitnessId = witnessId,
                SourceKey = source.SourceKey,
                Description = source.Description,
                DocId = source.DocId,
                Date = source.Date,
                IssueId = source.IssueId
            });

            var firstPart = plan.Chunks.FirstOrDefault(c => c.TopicIndex == exhibit.TopicIndex);
            firstPart?.ExhibitNumbers.Add(exhibit.Number);
        }

        return next;
    }

    private static void FillSpeakers(DepositionPlan plan, CaseGuide guide)
    {
        plan.Speakers = new List<DepositionSpeaker>(plan.Roles.Appearances);
        plan.Speakers.Add(new DepositionSpeaker
        {
            SpeakerId = plan.Witness.Id,
            FullName = plan.Witness.FullName,
            Role = SpeakerRole.Witness,
            FirmName = plan.Witness.OrganizationName,
            Email = guide.FindCast(plan.Witness.Id)?.Email ?? string.Empty
        });
        plan.Speakers.Add(new DepositionSpeaker { SpeakerId = "REPORTER", FullName = plan.Schedule.ReporterName, Role = SpeakerRole.Reporter });
        plan.Speakers.Add(new DepositionSpeaker { SpeakerId = "VIDEOGRAPHER", FullName = plan.Schedule.VideographerName, Role = SpeakerRole.Videographer });
    }

    // Topic of the exhibit's issue, else the first topic
    private static int TopicIndexOf(DepositionPlan plan, string issueId)
    {
        if (string.IsNullOrEmpty(issueId))
            return 0;
        var chunk = plan.Chunks.FirstOrDefault(c => string.Equals(c.IssueId, issueId, StringComparison.Ordinal));
        return chunk?.TopicIndex ?? 0;
    }

    /// <summary>
    /// A document chosen as an exhibit. Rank orders the selection (hot first, then linked fact significance, then date).
    /// </summary>
    private class ExhibitSource
    {
        public string SourceKey { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string DocId { get; set; } = string.Empty;
        public string Date { get; set; } = string.Empty;
        public string IssueId { get; set; } = string.Empty;
        public int Rank { get; set; }
        public int Number { get; set; }
    }

    /// <summary>
    /// Guide lookups shared by every plan in one Plan call.
    /// </summary>
    private class PlanContext
    {
        private static readonly ValueKind[] DocumentKinds =
        {
            ValueKind.DocumentTitle, ValueKind.ContractNumber, ValueKind.ChangeOrderNumber, ValueKind.InvoiceNumber, ValueKind.PurchaseOrder
        };

        private readonly Dictionary<string, KeyFact> _factsById = new(StringComparer.Ordinal);
        private readonly Dictionary<string, string> _issueTitles = new(StringComparer.Ordinal);

        public PlanContext(CaseGuide guide, DiscoveryCorpus? corpus)
        {
            Guide = guide;
            Corpus = corpus;
            Facts = (guide.Facts ?? new List<KeyFact>()).Where(f => f != null).ToList();
            Values = (guide.Values ?? new List<CanonicalValue>()).Where(v => v != null).ToList();

            foreach (var fact in Facts.Where(f => !string.IsNullOrEmpty(f.Id)))
                _factsById.TryAdd(fact.Id, fact);

            foreach (var issue in (guide.Issues ?? new List<CaseIssue>()).Where(i => i != null && !string.IsNullOrEmpty(i.Id)))
            {
                if (IssueOrder.TryAdd(issue.Id, IssueOrder.Count))
                    _issueTitles[issue.Id] = string.IsNullOrWhiteSpace(issue.Title) ? issue.Id : issue.Title;
            }
        }

        public CaseGuide Guide { get; }

        // Null when there is no usable corpus
        public DiscoveryCorpus? Corpus { get; }

        public List<KeyFact> Facts { get; }
        public List<CanonicalValue> Values { get; }
        public Dictionary<string, int> IssueOrder { get; } = new(StringComparer.Ordinal);

        public string IssueTitle(string issueId) => _issueTitles.TryGetValue(issueId, out var title) ? title : issueId;

        // First of the fact's issues that exists in the guide
        public string? FirstIssue(KeyFact fact) =>
            (fact.IssueIds ?? new List<string>()).FirstOrDefault(id => id != null && IssueOrder.ContainsKey(id));

        /// <summary>
        /// Written emails the witness sent or received by the deposition date that are hot or link a Hot/Key fact.
        /// </summary>
        public List<ExhibitSource> CorpusSources(string witnessId, DateOnly asOf)
        {
            var cutoff = asOf.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            var seen = new HashSet<string>(StringComparer.Ordinal);

            return (Corpus?.Candidates ?? new List<ExhibitCandidate>())
                .Where(c => c != null
                    && !string.IsNullOrWhiteSpace(c.DocId)
                    && (c.ParticipantIds ?? new List<string>()).Contains(witnessId)
                    && CaseDraft.TryParseDate(c.Date, out _)
                    && string.CompareOrdinal(c.Date, cutoff) <= 0)
                .Select(c => (Candidate: c, Significance: BestSignificance(c.FactIds)))
                .Where(x => x.Candidate.IsHot || x.Significance <= 1)
                .OrderBy(x => x.Candidate.IsHot ? 0 : 1)
                .ThenBy(x => x.Significance)
                .ThenBy(x => x.Candidate.Date, StringComparer.Ordinal)
                .ThenBy(x => x.Candidate.DocId, StringComparer.Ordinal)
                .Where(x => seen.Add(x.Candidate.DocId))
                .Select((x, rank) => new ExhibitSource
                {
                    SourceKey = "DOC:" + x.Candidate.DocId,
                    Description = $"Email dated {CaseGuideDocxExporter.LongDate(x.Candidate.Date)}, \"{x.Candidate.Subject ?? string.Empty}\" ({x.Candidate.DocId})",
                    DocId = x.Candidate.DocId,
                    Date = x.Candidate.Date,
                    IssueId = FirstLinkedIssue(x.Candidate.FactIds),
                    Rank = rank
                })
                .ToList();
        }

        /// <summary>
        /// Without a corpus: canonical document identifiers (titles, contract, change order, invoice and PO numbers)
        /// the witness knows.
        /// </summary>
        public List<ExhibitSource> ValueSources(IEnumerable<CanonicalValue> known) =>
            known
                .Where(v => DocumentKinds.Contains(v.Kind) && !string.IsNullOrWhiteSpace(v.Value) && !string.IsNullOrEmpty(v.Id))
                .Select(v => (Value: v, Significance: BestSignificance(v.FactIds), Date: EarliestDate(v.FactIds)))
                .OrderBy(x => x.Significance)
                .ThenBy(x => x.Date, StringComparer.Ordinal)
                .ThenBy(x => x.Value.Id, StringComparer.Ordinal)
                .Select((x, rank) => new ExhibitSource
                {
                    SourceKey = "VAL:" + x.Value.Id,
                    Description = $"{x.Value.Label}: {x.Value.Value}",
                    Date = x.Date,
                    IssueId = FirstLinkedIssue(x.Value.FactIds),
                    Rank = rank
                })
                .ToList();

        /// <summary>
        /// Rebuilds a reused sidecar assignment; its rank (for the impeachment tag) comes from the current sources.
        /// </summary>
        public ExhibitSource SourceFor(ExhibitAssignment assignment)
        {
            var rank = 1000 + assignment.Number;
            var candidate = string.IsNullOrEmpty(assignment.DocId)
                ? null
                : Corpus?.Candidates?.FirstOrDefault(c => c != null && string.Equals(c.DocId, assignment.DocId, StringComparison.Ordinal));
            if (candidate != null)
                rank = (candidate.IsHot ? 0 : 100) + BestSignificance(candidate.FactIds) * 10;
            else if ((assignment.SourceKey ?? string.Empty).StartsWith("VAL:", StringComparison.Ordinal))
            {
                var value = Values.FirstOrDefault(v => string.Equals("VAL:" + v.Id, assignment.SourceKey, StringComparison.Ordinal));
                if (value != null)
                    rank = 100 + BestSignificance(value.FactIds) * 10;
            }

            return new ExhibitSource
            {
                SourceKey = assignment.SourceKey ?? string.Empty,
                Description = assignment.Description ?? string.Empty,
                DocId = assignment.DocId ?? string.Empty,
                Date = assignment.Date ?? string.Empty,
                IssueId = assignment.IssueId ?? string.Empty,
                Rank = rank
            };
        }

        // Hot 0, Key 1, Supporting 2, no linked fact 3
        private int BestSignificance(IEnumerable<string>? factIds)
        {
            var best = 3;
            foreach (var id in factIds ?? Array.Empty<string>())
            {
                if (id != null && _factsById.TryGetValue(id, out var fact))
                {
                    var score = fact.Significance switch
                    {
                        FactSignificance.Hot => 0,
                        FactSignificance.Key => 1,
                        _ => 2
                    };
                    best = Math.Min(best, score);
                }
            }
            return best;
        }

        private string EarliestDate(IEnumerable<string>? factIds) =>
            (factIds ?? Array.Empty<string>())
                .Where(id => id != null && _factsById.ContainsKey(id))
                .Select(id => _factsById[id].Date)
                .Where(d => CaseDraft.TryParseDate(d, out _))
                .OrderBy(d => d, StringComparer.Ordinal)
                .FirstOrDefault() ?? string.Empty;

        // Issue of the first linked fact that has a guide issue
        private string FirstLinkedIssue(IEnumerable<string>? factIds)
        {
            foreach (var id in factIds ?? Array.Empty<string>())
            {
                if (id != null && _factsById.TryGetValue(id, out var fact))
                {
                    var issue = FirstIssue(fact);
                    if (issue != null)
                        return issue;
                }
            }
            return string.Empty;
        }
    }

    private static WitnessCandidate Candidate(CaseDraft draft, CastMember member, List<KeyFact> facts)
    {
        var party = CounselDirectory.PartyOf(draft, member);
        var side = party == null
            ? WitnessSide.NonParty
            : CounselDirectory.IsPlaintiffSide(party.Role) ? WitnessSide.Plaintiff : WitnessSide.Defense;

        return new WitnessCandidate
        {
            Id = member.Id,
            FullName = member.FullName,
            Title = member.Title ?? string.Empty,
            OrganizationName = draft.Guide?.FindOrganization(member.OrganizationId)?.Name ?? string.Empty,
            Side = side,
            PartyId = party?.Id ?? string.Empty,
            FactCount = facts.Count(f => (f.ActorIds ?? new List<string>()).Contains(member.Id) || KnowledgeGate.KnowersOf(f).Contains(member.Id)),
            ActorCount = facts.Count(f => (f.ActorIds ?? new List<string>()).Contains(member.Id))
        };
    }

    // Defense or non-party witness: the first plaintiff-side party examines; plaintiff witness: the first defense party
    private static CaseParty? OpposingParty(CaseDraft draft, WitnessCandidate witness) =>
        witness.Side == WitnessSide.Plaintiff
            ? CounselDirectory.FirstDefenseSide(draft.CoreDocument)
            : CounselDirectory.FirstPlaintiffSide(draft.CoreDocument);

    private static CaseOrganization? OpposingFirm(CaseDraft draft, WitnessCandidate witness) =>
        CounselDirectory.FirmFor(draft, OpposingParty(draft, witness)?.Id);

    private static DepositionSpeaker Speaker(CaseDraft draft, CastMember member, CaseParty party, SpeakerRole role) => new()
    {
        SpeakerId = member.Id,
        FullName = member.FullName,
        Role = role,
        FirmName = draft.Guide?.FindOrganization(member.OrganizationId)?.Name ?? string.Empty,
        Email = member.Email ?? string.Empty,
        PartyLabel = PartyLabel(party)
    };

    private static DepositionSpeaker Fallback(CaseParty party, SpeakerRole role, List<string> notes)
    {
        var name = $"Counsel for {party.Name}";
        var note = $"{name} is not in the cast, so the transcript says \"{name}\".";
        if (!notes.Contains(note))
            notes.Add(note);

        return new DepositionSpeaker
        {
            SpeakerId = $"COUNSEL-{party.Id}",
            FullName = name,
            Role = role,
            PartyLabel = PartyLabel(party)
        };
    }

    private static void AddAppearance(DepositionRoles roles, DepositionSpeaker? speaker)
    {
        if (speaker != null && !roles.Appearances.Any(s => s.SpeakerId == speaker.SpeakerId))
            roles.Appearances.Add(speaker);
    }

    private static string PartyLabel(CaseParty party)
    {
        var word = party.Role switch
        {
            PartyRole.Plaintiff => "Plaintiff",
            PartyRole.Defendant => "Defendant",
            PartyRole.Complainant => "Complainant",
            PartyRole.Subject => "Subject",
            _ => "Third Party"
        };
        return $"On behalf of {word} {party.Name}";
    }

    private static CaseParty? FindParty(CoreDocument? doc, string? partyId)
    {
        if (doc?.Parties == null || string.IsNullOrEmpty(partyId))
            return null;
        return doc.Parties.FirstOrDefault(p => p != null && string.Equals(p.Id, partyId, StringComparison.Ordinal));
    }

    private static DateOnly? LatestFactDate(CaseDraft draft)
    {
        DateOnly? latest = null;
        foreach (var fact in draft.Guide?.Facts ?? new List<KeyFact>())
        {
            if (fact != null && CaseDraft.TryParseDate(fact.Date, out var date))
            {
                var day = DateOnly.FromDateTime(date);
                if (latest == null || day > latest.Value)
                    latest = day;
            }
        }
        return latest;
    }

    private static string PickName(string[] names, ulong rank, HashSet<string> castNames)
    {
        var index = (int)(rank % (ulong)names.Length);

        // Skip names that collide with a cast member; the lists are longer than any realistic collision set
        for (var step = 0; step < names.Length; step++)
        {
            var name = names[(index + step) % names.Length];
            if (!castNames.Contains(name))
                return name;
        }
        return names[index];
    }

    private static string CaseKey(CaseDraft draft) => draft.CaseId.ToString("N");

    private static DateOnly Max(DateOnly a, DateOnly b) => a > b ? a : b;

    // Guards against tampered dates at the edge of the DateOnly range
    private static DateOnly SafeAddMonths(DateOnly day, int months) =>
        day.Year >= 9998 ? day : day.AddMonths(months);

    private static DateOnly SafeAddDays(DateOnly day, int days) =>
        day.DayNumber > DateOnly.MaxValue.DayNumber - days - 400 ? day : day.AddDays(days);
}
