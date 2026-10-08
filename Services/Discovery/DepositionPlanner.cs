using System.Globalization;
using ReelDiscovery.Models.Case;
using ReelDiscovery.Models.Discovery;
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
