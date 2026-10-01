using System.ComponentModel;
using System.Text.Json.Serialization;
using ReelDiscovery.Models.Case;
using ReelDiscovery.Services.Ai;

namespace ReelDiscovery.Services.Case;

/// <summary>
/// Builds the case guide through the Story tier (D-03): issues extracted in code and enriched by one call (GUIDE-01),
/// then the cast (GUIDE-02..04). Code owns every id, email address, phone and signature.
/// </summary>
public class CaseGuideGenerator
{
    public const int IssueDetailsMaxOutputTokens = 8_000;
    public const int RosterMaxOutputTokens = 12_000;
    public const int CastDetailsMaxOutputTokens = 12_000;
    public const int BackgroundMaxOutputTokens = 8_000;
    public const int MaxParallelCalls = 3;
    public const int MaxDetailsPerCall = 10;

    private readonly ILlmGateway _llm;

    /// <summary>
    /// Top-level response types sent as strict schemas (verified by tests).
    /// </summary>
    internal static IReadOnlyList<Type> SchemaTypes { get; } = new[]
    {
        typeof(IssueDetailsDto), typeof(CastRosterDto), typeof(CastDetailsDto), typeof(BackgroundStaffDto)
    };

    public CaseGuideGenerator(ILlmGateway llm)
    {
        _llm = llm;
    }

    // ---------- Issues ----------

    /// <summary>
    /// Extracts the issues from the counts/findings (always succeeds), then writes the missing issue details.
    /// A failed enrichment marks only IssueDetailsState; a fatal failure is reported on the result. Cancellation propagates.
    /// </summary>
    public async Task<GuidePartResult> GenerateIssuesAsync(CaseDraft draft, IProgress<GuideProgress>? progress = null, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(draft);
        var doc = draft.CoreDocument ?? throw new InvalidOperationException("Write the core document before building the case guide.");
        var guide = draft.Guide ??= new CaseGuide();
        var result = new GuidePartResult { Part = GuidePart.Issues };

        progress?.Report(new GuideProgress
        {
            Part = GuidePart.Issues,
            Message = doc.DocumentType == CoreDocumentType.InvestigationMemo
                ? "Extracting the issues from the findings..."
                : "Extracting the issues from the counts..."
        });

        guide.Issues = IssueExtractor.Extract(doc, guide);
        guide.IssuesState.Status = GuidePartStatus.Ready;
        guide.IssuesState.FailureMessage = string.Empty;
        GuideStaleness.Stamp(draft, GuidePart.Issues);
        draft.Revision++;
        result.Succeeded = true;

        await EnrichIssuesAsync(draft, result, progress, ct);
        return result;
    }

    /// <summary>
    /// Writes details only for issues that have none (Retry Issue Details).
    /// </summary>
    public async Task<GuidePartResult> RetryIssueDetailsAsync(CaseDraft draft, IProgress<GuideProgress>? progress = null, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(draft);
        if (draft.CoreDocument == null || draft.Guide == null)
            throw new InvalidOperationException("Extract the issues before writing their details.");

        var result = new GuidePartResult { Part = GuidePart.Issues };
        await EnrichIssuesAsync(draft, result, progress, ct);
        result.Succeeded = draft.Guide.IssueDetailsState.Status == GuidePartStatus.Ready;
        return result;
    }

    private async Task EnrichIssuesAsync(CaseDraft draft, GuidePartResult result, IProgress<GuideProgress>? progress, CancellationToken ct)
    {
        var guide = draft.Guide!;
        var state = guide.IssueDetailsState;
        var pending = guide.Issues.Where(i => !i.HasDetails).ToList();
        if (pending.Count == 0)
        {
            SetReady(state);
            return;
        }

        state.Status = GuidePartStatus.Generating;
        progress?.Report(new GuideProgress { Part = GuidePart.Issues, Message = "Writing the issue details...", Total = pending.Count });

        try
        {
            var dto = (await _llm.GenerateAsync<IssueDetailsDto>(new LlmRequest
            {
                Tier = LlmTier.Story,
                OperationName = "Case Guide - Issue details",
                SystemPrompt = CaseGuidePrompts.SystemPrompt,
                UserPrompt = CaseGuidePrompts.BuildIssueDetailsPrompt(draft, pending),
                MaxOutputTokens = IssueDetailsMaxOutputTokens
            }, ct)).GetValueOrThrow();

            var written = 0;
            foreach (var detail in dto.Issues ?? new List<IssueDetailDto>())
            {
                if (detail == null)
                    continue;

                // Only issues listed in the request are accepted (T-03-30)
                var id = IdAllocator.Normalize(detail.IssueId, IdAllocator.Issue);
                var issue = id == null ? null : pending.FirstOrDefault(i => i.Id == id);
                if (issue == null)
                {
                    result.Notes.Add($"Ignored details for unknown issue {Clean(detail.IssueId)}.");
                    continue;
                }

                issue.Description = Clean(detail.Description);
                issue.Elements = (detail.Elements ?? new List<string>()).Select(Clean).Where(e => e.Length > 0).ToList();
                issue.RelevanceCriteria = Clean(detail.RelevanceCriteria);
                written++;
            }

            if (written > 0)
                draft.Revision++;

            var missing = guide.Issues.Count(i => !i.HasDetails);
            if (missing == 0)
                SetReady(state);
            else
                SetFailed(state, $"Details for {missing} issues were not written. Click Retry Issue Details.");
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // User cancel: nothing written
            state.Status = GuidePartStatus.Pending;
            throw;
        }
        catch (OperationCanceledException)
        {
            // Not cancelled by the user: the request timed out
            SetFailed(state, "The request timed out. Click Retry Issue Details.");
        }
        catch (LlmCallException ex) when (ex.Result.IsFatal)
        {
            // Bad key or exhausted spend limit: stop; the issues themselves are kept
            state.Status = GuidePartStatus.Pending;
            result.FatalError = ex.Result.Message ?? ex.Message;
        }
        catch (LlmCallException ex)
        {
            if (ex.Result.Outcome == LlmOutcome.Refused)
                result.WasRefused = true;
            SetFailed(state, DescribeOutcome(ex.Result, "Issue Details"));
        }
        catch (Exception ex)
        {
            SetFailed(state, ex.Message);
        }

        if (state.Status == GuidePartStatus.Failed)
            result.FailureMessage = state.FailureMessage;
    }

    // ---------- Cast ----------

    /// <summary>
    /// Organizations in code, then the roster (+ one counsel top-up), batched details and background staff per organization.
    /// Resumes an unfinished run: finished characters and organizations are not sent again. Cancellation propagates.
    /// </summary>
    public async Task<GuidePartResult> GenerateCastAsync(CaseDraft draft, IProgress<GuideProgress>? progress = null, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(draft);
        var doc = draft.CoreDocument ?? throw new InvalidOperationException("Write the core document before building the case guide.");
        var guide = draft.Guide ??= new CaseGuide();
        var state = guide.CastState;
        var result = new GuidePartResult { Part = GuidePart.Cast };
        var changed = SyncOrganizations(draft).Count > 0;

        // Resume: the key roster already exists from an unfinished run
        var resume = guide.Cast.Any(c => c.Tier == CastTier.Key) && state.Status != GuidePartStatus.Ready;
        state.Status = GuidePartStatus.Generating;

        using var runCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        using var semaphore = new SemaphoreSlim(MaxParallelCalls, MaxParallelCalls);
        var run = new CastRun(runCts, semaphore, ct);

        try
        {
            // 1. Roster, then at most one counsel top-up (W2)
            if (!resume)
            {
                progress?.Report(new GuideProgress { Part = GuidePart.Cast, Message = "Writing the cast: organizations and key characters..." });

                var roster = await RunUnitAsync<CastRosterDto>(new LlmRequest
                {
                    Tier = LlmTier.Story,
                    OperationName = "Case Guide - Cast roster",
                    SystemPrompt = CaseGuidePrompts.SystemPrompt,
                    UserPrompt = CaseGuidePrompts.BuildRosterPrompt(draft),
                    MaxOutputTokens = RosterMaxOutputTokens
                }, run);

                if (roster.Value == null)
                    return FinishCast(draft, result, run, roster.Error, roster.Refused, changed);

                MapRoster(draft, roster.Value, result);
                SeedAuthor(draft);
                changed = true;

                var gaps = CounselGaps(draft);
                if (gaps.Count > 0)
                {
                    var topUp = await RunUnitAsync<CastRosterDto>(new LlmRequest
                    {
                        Tier = LlmTier.Story,
                        OperationName = "Case Guide - Counsel top-up",
                        SystemPrompt = CaseGuidePrompts.SystemPrompt,
                        UserPrompt = CaseGuidePrompts.BuildCounselTopUpPrompt(draft, gaps),
                        MaxOutputTokens = RosterMaxOutputTokens
                    }, run);

                    // Never loops: remaining gaps surface as validator warnings
                    if (topUp.Value != null)
                        MapRoster(draft, topUp.Value, result);
                    else if (topUp.Error.Length > 0)
                        result.Notes.Add($"Counsel top-up failed: {topUp.Error}");
                }

                SyncCounsel(draft);
                if (run.IsFatal)
                    return FinishCast(draft, result, run, string.Empty, false, changed);
            }

            // 2. Details for pending key characters, in batches
            changed |= await WriteDetailsAsync(draft, result, run, progress);

            // 3. Background staff per organization
            if (!run.IsFatal)
                changed |= await WriteBackgroundAsync(draft, result, run, progress);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // User cancel: completed units are kept for Retry
            state.Status = GuidePartStatus.Pending;
            OrderCast(guide);
            if (changed)
                draft.Revision++;
            throw;
        }

        return FinishCast(draft, result, run, string.Empty, false, changed);
    }

    /// <summary>
    /// Mirrors the core document's parties as organizations (matched by PartyId). Existing ones are refreshed but keep
    /// their id and domain so email addresses stay stable. Returns the ids of the organizations added.
    /// </summary>
    internal static List<string> SyncOrganizations(CaseDraft draft)
    {
        var doc = draft.CoreDocument ?? throw new InvalidOperationException("Write the core document before building the case guide.");
        var guide = draft.Guide ??= new CaseGuide();
        var taken = TakenDomains(guide);
        var added = new List<string>();

        foreach (var party in doc.Parties.Where(p => p != null && !string.IsNullOrEmpty(p.Id)))
        {
            var org = guide.Organizations.FirstOrDefault(o => o.PartyId == party.Id);
            if (org != null)
            {
                org.Name = Clean(party.Name);
                org.ShortName = Clean(party.ShortName);
                org.Location = Clean(party.Domicile);
                org.TimeZoneId = UsTimeZones.ForLocation(party.Domicile);
                continue;
            }

            org = new CaseOrganization
            {
                Name = Clean(party.Name),
                ShortName = Clean(party.ShortName),
                Kind = KindFor(party),
                PartyId = party.Id,
                Location = Clean(party.Domicile),
                TimeZoneId = UsTimeZones.ForLocation(party.Domicile)
            };
            org.Domain = EmailIdentity.DomainFor(org, taken);
            org.Id = IdAllocator.Next(guide, IdAllocator.Organization);
            guide.Organizations.Add(org);
            added.Add(org.Id);
        }

        return added;
    }

    /// <summary>
    /// Missing counsel (GUIDE-03, D-15): in-house counsel for each Party organization, and an outside firm with a
    /// partner and an associate for each Party and Individual organization. Same rule as the validator.
    /// </summary>
    internal static List<string> CounselGaps(CaseDraft draft)
    {
        var gaps = new List<string>();
        var guide = draft.Guide;
        if (guide == null)
            return gaps;

        foreach (var org in guide.Organizations.Where(o => o.Kind is OrganizationKind.Party or OrganizationKind.Individual))
        {
            var label = org.Kind == OrganizationKind.Individual ? $"{org.Id} {org.Name} (individual)" : $"{org.Id} {org.Name}";

            if (org.Kind == OrganizationKind.Party
                && !guide.Cast.Any(c => c.OrganizationId == org.Id && c.Role == CastRole.InHouseCounsel))
            {
                gaps.Add($"{label} needs in-house counsel");
            }

            var represented = guide.Organizations.Any(firm => firm.Kind == OrganizationKind.LawFirm
                && firm.RepresentsOrgIds.Contains(org.Id)
                && HasOutsideCounsel(guide, firm.Id, "Partner")
                && HasOutsideCounsel(guide, firm.Id, "Associate"));
            if (!represented)
                gaps.Add($"{label} needs an outside law firm with a partner and an associate");
        }

        return gaps;
    }

    private static bool HasOutsideCounsel(CaseGuide guide, string firmId, string titleWord) =>
        guide.Cast.Any(c => c.OrganizationId == firmId
            && c.Role == CastRole.OutsideCounsel
            && (c.Title ?? string.Empty).Contains(titleWord, StringComparison.OrdinalIgnoreCase));

    private static OrganizationKind KindFor(CaseParty party)
    {
        var entity = party.EntityType ?? string.Empty;
        if (entity.Contains("individual", StringComparison.OrdinalIgnoreCase) || entity.Contains("person", StringComparison.OrdinalIgnoreCase))
            return OrganizationKind.Individual;

        var name = party.Name ?? string.Empty;
        string[] agencyWords = ["Department", "Agency", "Commission", "Bureau", "Office of", "United States"];
        return agencyWords.Any(w => name.Contains(w, StringComparison.OrdinalIgnoreCase))
            ? OrganizationKind.Agency
            : OrganizationKind.Party;
    }

    private static HashSet<string> TakenDomains(CaseGuide guide) =>
        new(guide.Organizations.Where(o => !string.IsNullOrEmpty(o.Domain)).Select(o => o.Domain.ToLowerInvariant()), StringComparer.OrdinalIgnoreCase);

    // Model output -> organizations and key characters; code assigns every id, email and phone (T-03-30)
    private static void MapRoster(CaseDraft draft, CastRosterDto dto, GuidePartResult result)
    {
        var guide = draft.Guide!;
        var taken = TakenDomains(guide);
        var pendingRefs = new List<(CaseOrganization Org, List<string> Refs)>();

        foreach (var item in dto.Organizations ?? new List<NewOrganizationDto>())
        {
            var name = Clean(item?.Name);
            if (item == null || name.Length == 0)
                continue;

            var shortName = Clean(item.ShortName);
            var org = FindOrganizationByName(guide, name) ?? (shortName.Length > 0 ? FindOrganizationByName(guide, shortName) : null);
            if (org == null)
            {
                org = new CaseOrganization
                {
                    Name = name,
                    ShortName = shortName,
                    Kind = item.Kind,
                    Location = Clean(item.Location),
                    TimeZoneId = UsTimeZones.ForLocation(item.Location)
                };
                org.Domain = EmailIdentity.DomainFor(org, taken);
                org.Id = IdAllocator.Next(guide, IdAllocator.Organization);
                guide.Organizations.Add(org);
            }

            pendingRefs.Add((org, item.RepresentsRefs ?? new List<string>()));
        }

        // Represented organizations resolve after every new organization exists
        foreach (var (org, refs) in pendingRefs)
        {
            foreach (var reference in refs)
            {
                var target = ResolveOrganization(guide, reference);
                if (target == null)
                {
                    result.Notes.Add($"Ignored unknown organization {Clean(reference)} represented by {org.Name}.");
                    continue;
                }

                if (target.Id != org.Id && !org.RepresentsOrgIds.Contains(target.Id))
                    org.RepresentsOrgIds.Add(target.Id);
            }
        }

        foreach (var person in dto.People ?? new List<RosterPersonDto>())
        {
            if (person == null)
                continue;

            var first = Clean(person.FirstName);
            var last = Clean(person.LastName);
            var fullName = string.Join(" ", new[] { first, last }.Where(s => s.Length > 0));
            if (fullName.Length == 0)
                continue;

            var org = ResolveOrganization(guide, person.OrganizationRef);
            if (org == null)
            {
                result.Notes.Add($"Dropped {fullName}: unknown organization {Clean(person.OrganizationRef)}.");
                continue;
            }

            // Existing characters are never duplicated
            if (guide.FindCast(IdAllocator.Normalize(person.ExistingId, IdAllocator.Character)) != null
                || guide.Cast.Any(c => string.Equals(c.FullName, fullName, StringComparison.OrdinalIgnoreCase)))
                continue;

            AddKeyMember(draft, first, last, Clean(person.Title), org, person.Role, Clean(person.Involvement));
        }
    }

    private static CastMember AddKeyMember(CaseDraft draft, string first, string last, string title, CaseOrganization org, CastRole role, string involvement)
    {
        var guide = draft.Guide!;
        var member = new CastMember
        {
            Id = IdAllocator.Next(guide, IdAllocator.Character),
            FirstName = first,
            LastName = last,
            Title = title,
            OrganizationId = org.Id,
            Tier = CastTier.Key,
            Role = role,
            Involvement = involvement,
            DetailsPending = true,
            Phone = SignatureBuilder.FictionalPhone()
        };
        guide.Cast.Add(member);
        CaseGuideEditing.RefreshIdentity(draft, member);
        return member;
    }

    private static CaseOrganization? FindOrganizationByName(CaseGuide guide, string name) =>
        guide.Organizations.FirstOrDefault(o =>
            string.Equals(o.Name, name, StringComparison.OrdinalIgnoreCase)
            || (o.ShortName.Length > 0 && string.Equals(o.ShortName, name, StringComparison.OrdinalIgnoreCase)));

    // ORG id (normalized, must exist) or an exact organization name
    private static CaseOrganization? ResolveOrganization(CaseGuide guide, string? reference)
    {
        var text = Clean(reference);
        if (text.Length == 0)
            return null;

        return guide.FindOrganization(IdAllocator.Normalize(text, IdAllocator.Organization)) ?? FindOrganizationByName(guide, text);
    }

    // The pleading's signing attorney or the memo author must be in the cast
    private static void SeedAuthor(CaseDraft draft)
    {
        var doc = draft.CoreDocument!;
        var guide = draft.Guide!;

        if (CoreDocumentTemplates.IsPleading(doc.DocumentType))
        {
            var attorney = Clean(doc.Counsel.AttorneyName);
            var firmName = Clean(doc.Counsel.FirmName);
            if (attorney.Length == 0 || firmName.Length == 0 || FindByName(guide, attorney) != null)
                return;

            var firm = FindOrganizationByName(guide, firmName);
            if (firm == null)
            {
                var clients = guide.Organizations
                    .Where(o => doc.Parties.Any(p => p.Id == o.PartyId && p.Role is PartyRole.Plaintiff or PartyRole.Complainant))
                    .Select(o => o.Id)
                    .ToList();
                firm = AddOrganization(guide, firmName, OrganizationKind.LawFirm, clients.FirstOrDefault() is { } id ? guide.FindOrganization(id)?.Location ?? string.Empty : string.Empty);
                firm.RepresentsOrgIds = clients;
            }

            var (first, last) = SplitName(attorney);
            AddKeyMember(draft, first, last, "Partner", firm, CastRole.OutsideCounsel,
                $"Signed the {doc.DocumentType.ToString().ToLowerInvariant()} as outside counsel.");
            return;
        }

        // Memo author: "Special Agent Tom Reyes, Task Force 7" -> name tokens before the comma
        var from = Clean(doc.Memo.From);
        if (from.Length == 0 || guide.Cast.Any(c => c.FullName.Length > 0 && from.Contains(c.FullName, StringComparison.OrdinalIgnoreCase)))
            return;

        var head = from.Split(',')[0].Trim();
        var tokens = head.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length == 0)
            return;

        var nameTokens = tokens.Length >= 2 ? tokens[^2..] : tokens;
        var title = string.Join(" ", tokens.Take(tokens.Length - nameTokens.Length));
        var rest = from.Contains(',') ? from[(from.IndexOf(',') + 1)..].Trim() : string.Empty;
        title = string.Join(", ", new[] { title, rest }.Where(s => s.Length > 0));

        var orgName = Clean(doc.Memo.Organization);
        var org = orgName.Length > 0 ? FindOrganizationByName(guide, orgName) : null;
        if (org == null && orgName.Length > 0)
        {
            var kind = doc.MemoPerspective == MemoPerspective.Government ? OrganizationKind.Agency
                : orgName.Contains("LLP", StringComparison.OrdinalIgnoreCase) || orgName.Contains("Law", StringComparison.OrdinalIgnoreCase) ? OrganizationKind.LawFirm
                : OrganizationKind.ThirdParty;
            org = AddOrganization(guide, orgName, kind, doc.Jurisdiction.VenueCity.Length > 0 && doc.Jurisdiction.State.Length > 0
                ? $"{doc.Jurisdiction.VenueCity}, {doc.Jurisdiction.State}"
                : doc.Jurisdiction.State);
        }

        if (org == null)
            return;

        AddKeyMember(draft, nameTokens.Length >= 2 ? nameTokens[0] : string.Empty, nameTokens[^1],
            title.Length > 0 ? title : "Investigator", org, CastRole.Staff, "Wrote the investigation memo.");
    }

    private static CaseOrganization AddOrganization(CaseGuide guide, string name, OrganizationKind kind, string location)
    {
        var org = new CaseOrganization
        {
            Name = name,
            Kind = kind,
            Location = location,
            TimeZoneId = UsTimeZones.ForLocation(location)
        };
        org.Domain = EmailIdentity.DomainFor(org, TakenDomains(guide));
        org.Id = IdAllocator.Next(guide, IdAllocator.Organization);
        guide.Organizations.Add(org);
        return org;
    }

    private static CastMember? FindByName(CaseGuide guide, string fullName) =>
        guide.Cast.FirstOrDefault(c => string.Equals(c.FullName, fullName, StringComparison.OrdinalIgnoreCase));

    private static (string First, string Last) SplitName(string fullName)
    {
        var tokens = fullName.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return tokens.Length <= 1
            ? (string.Empty, fullName)
            : (string.Join(" ", tokens[..^1]), tokens[^1]);
    }

    // The pleading signature block uses the signing attorney's cast email (D-14)
    private static void SyncCounsel(CaseDraft draft)
    {
        var counsel = draft.CoreDocument?.Counsel;
        if (counsel == null || string.IsNullOrWhiteSpace(counsel.AttorneyName))
            return;

        var member = FindByName(draft.Guide!, counsel.AttorneyName.Trim());
        if (member == null)
            return;

        counsel.AttorneyName = member.FullName;
        counsel.Email = member.Email;
    }

    private async Task<bool> WriteDetailsAsync(CaseDraft draft, GuidePartResult result, CastRun run, IProgress<GuideProgress>? progress)
    {
        var guide = draft.Guide!;
        var doc = draft.CoreDocument!;
        var pending = guide.Cast.Where(c => c.Tier == CastTier.Key && c.DetailsPending).ToList();
        if (pending.Count == 0)
            return false;

        var batches = pending.Chunk(MaxDetailsPerCall).ToList();
        var offsets = new List<int>();
        for (int i = 0, offset = 0; i < batches.Count; offset += batches[i].Length, i++)
            offsets.Add(offset);

        // Tasks return DTOs only; the guide is changed on this thread after Task.WhenAll
        var tasks = batches.Select((batch, i) => RunUnitAsync<CastDetailsDto>(new LlmRequest
        {
            Tier = LlmTier.Story,
            OperationName = "Case Guide - Cast details",
            SystemPrompt = CaseGuidePrompts.SystemPrompt,
            UserPrompt = CaseGuidePrompts.BuildCastDetailsPrompt(draft, batch),
            MaxOutputTokens = CastDetailsMaxOutputTokens
        }, run, () => progress?.Report(new GuideProgress
        {
            Part = GuidePart.Cast,
            Message = $"Writing the cast: details for characters {offsets[i] + 1}-{offsets[i] + batch.Length} of {pending.Count}...",
            Done = offsets[i],
            Total = pending.Count
        }))).ToList();

        try
        {
            await Task.WhenAll(tasks);
        }
        finally
        {
            // Merge every finished batch, also when the user cancelled
            var start = doc.TimelineStart;
            var end = CaseGuidePrompts.StoryEnd(doc);
            for (var i = 0; i < tasks.Count; i++)
            {
                if (!tasks[i].IsCompletedSuccessfully)
                    continue;

                var unit = tasks[i].Result;
                if (unit.Refused)
                    result.WasRefused = true;
                if (unit.Value != null)
                    MapDetails(guide, batches[i], unit.Value, start, end, result);
            }
        }

        return tasks.Any(t => t.IsCompletedSuccessfully && t.Result.Value != null);
    }

    private static void MapDetails(CaseGuide guide, CastMember[] batch, CastDetailsDto dto, string start, string end, GuidePartResult result)
    {
        foreach (var detail in dto.People ?? new List<CastDetailDto>())
        {
            if (detail == null)
                continue;

            // Only characters listed in this batch are accepted (T-03-30)
            var id = IdAllocator.Normalize(detail.CastId, IdAllocator.Character);
            var member = id == null ? null : batch.FirstOrDefault(m => m.Id == id);
            if (member == null)
            {
                result.Notes.Add($"Ignored details for unknown character {Clean(detail.CastId)}.");
                continue;
            }

            var summary = Clean(detail.Summary);
            if (summary.Length > 0)
                member.Involvement = summary;

            SetActiveRange(member, detail.ActiveFrom, detail.ActiveTo, start, end, result);
            member.VoiceNote = Clean(detail.VoiceNote);
            member.SignatureStyle = detail.SignatureStyle;
            member.Signature = SignatureBuilder.Build(member, guide.FindOrganization(member.OrganizationId));
            member.DetailsPending = false;
        }
    }

    // Invalid or reversed dates fall back to the case timeline, with a note
    private static void SetActiveRange(CastMember member, string? from, string? to, string start, string end, GuidePartResult result)
    {
        if (CaseDraft.TryParseDate(from, out var fromDate) && CaseDraft.TryParseDate(to, out var toDate) && fromDate <= toDate)
        {
            member.ActiveFrom = Clean(from);
            member.ActiveTo = Clean(to);
            return;
        }

        member.ActiveFrom = start;
        member.ActiveTo = end;
        result.Notes.Add($"{member.Id} {member.FullName}: invalid active dates; used the case timeline {start} to {end}.");
    }

    private async Task<bool> WriteBackgroundAsync(CaseDraft draft, GuidePartResult result, CastRun run, IProgress<GuideProgress>? progress)
    {
        var guide = draft.Guide!;
        var doc = draft.CoreDocument!;
        var orgs = BackgroundPending(draft);
        if (orgs.Count == 0)
            return false;

        var counts = orgs.Select(o => draft.Preset.BackgroundPerOrg - BackgroundCount(guide, o)).ToList();
        var tasks = orgs.Select((org, i) => RunUnitAsync<BackgroundStaffDto>(new LlmRequest
        {
            Tier = LlmTier.Story,
            OperationName = "Case Guide - Background staff",
            SystemPrompt = CaseGuidePrompts.SystemPrompt,
            UserPrompt = CaseGuidePrompts.BuildBackgroundPrompt(draft, org, counts[i]),
            MaxOutputTokens = BackgroundMaxOutputTokens
        }, run, () => progress?.Report(new GuideProgress
        {
            Part = GuidePart.Cast,
            Message = $"Writing the cast: background staff for {org.Name} ({i + 1} of {orgs.Count})...",
            Done = i,
            Total = orgs.Count
        }))).ToList();

        var added = false;
        try
        {
            await Task.WhenAll(tasks);
        }
        finally
        {
            // Ids are allocated in organization order on this thread
            var start = doc.TimelineStart;
            var end = CaseGuidePrompts.StoryEnd(doc);
            for (var i = 0; i < tasks.Count; i++)
            {
                if (!tasks[i].IsCompletedSuccessfully)
                    continue;

                var unit = tasks[i].Result;
                if (unit.Refused)
                    result.WasRefused = true;
                if (unit.Value == null)
                    continue;

                added |= MapBackground(draft, orgs[i], counts[i], unit.Value, start, end, result);
                if (!guide.CastState.CompletedUnits.Contains(orgs[i].Id))
                    guide.CastState.CompletedUnits.Add(orgs[i].Id);
            }
        }

        return added;
    }

    private static bool MapBackground(CaseDraft draft, CaseOrganization org, int count, BackgroundStaffDto dto, string start, string end, GuidePartResult result)
    {
        var guide = draft.Guide!;
        var added = 0;

        foreach (var person in dto.People ?? new List<BackgroundPersonDto>())
        {
            if (added >= count)
                break;
            if (person == null)
                continue;

            var first = Clean(person.FirstName);
            var last = Clean(person.LastName);
            var fullName = string.Join(" ", new[] { first, last }.Where(s => s.Length > 0));
            if (fullName.Length == 0
                || guide.Cast.Any(c => c.OrganizationId == org.Id && string.Equals(c.FullName, fullName, StringComparison.OrdinalIgnoreCase)))
                continue;

            var member = new CastMember
            {
                Id = IdAllocator.Next(guide, IdAllocator.Character),
                FirstName = first,
                LastName = last,
                Title = Clean(person.Title),
                Department = Clean(person.Department),
                OrganizationId = org.Id,
                Tier = CastTier.Background,
                Role = CastRole.Staff,
                Involvement = Clean(person.Role),
                Phone = SignatureBuilder.FictionalPhone()
            };
            SetActiveRange(member, person.ActiveFrom, person.ActiveTo, start, end, result);
            guide.Cast.Add(member);
            // Background staff carry no signature (RefreshIdentity leaves it empty)
            CaseGuideEditing.RefreshIdentity(draft, member);
            added++;
        }

        return added > 0;
    }

    private static int BackgroundCount(CaseGuide guide, CaseOrganization org) =>
        guide.Cast.Count(c => c.Tier == CastTier.Background && c.OrganizationId == org.Id);

    // Non-individual organizations not finished in this run and still short of the preset's background staff (D-13)
    private static List<CaseOrganization> BackgroundPending(CaseDraft draft)
    {
        var guide = draft.Guide!;
        return guide.Organizations
            .Where(o => o.Kind != OrganizationKind.Individual
                && !guide.CastState.CompletedUnits.Contains(o.Id)
                && BackgroundCount(guide, o) < draft.Preset.BackgroundPerOrg)
            .ToList();
    }

    private GuidePartResult FinishCast(CaseDraft draft, GuidePartResult result, CastRun run, string rosterError, bool rosterRefused, bool changed)
    {
        var guide = draft.Guide!;
        var state = guide.CastState;
        OrderCast(guide);
        if (changed)
            draft.Revision++;

        if (run.IsFatal)
        {
            state.Status = GuidePartStatus.Pending;
            result.FatalError = run.FatalError;
            return result;
        }

        if (rosterError.Length > 0)
        {
            SetFailed(state, rosterError);
            result.WasRefused |= rosterRefused;
            result.FailureMessage = rosterError;
            return result;
        }

        var details = guide.Cast.Count(c => c.Tier == CastTier.Key && c.DetailsPending);
        var staff = BackgroundPending(draft).Count;
        if (details == 0 && staff == 0)
        {
            SetReady(state);
            state.CompletedUnits.Clear();
            GuideStaleness.Stamp(draft, GuidePart.Cast);
            result.Succeeded = true;
            return result;
        }

        var message = details > 0
            ? $"Details for {details} characters{(staff > 0 ? $" and staff for {staff} organizations" : string.Empty)} were not written. Click Retry Cast."
            : $"Background staff for {staff} organizations were not written. Click Retry Cast.";
        SetFailed(state, message);
        result.FailureMessage = message;
        return result;
    }

    // Key characters by id, then background staff by organization order
    private static void OrderCast(CaseGuide guide)
    {
        var orgOrder = guide.Organizations.Select((o, i) => (o.Id, i)).GroupBy(x => x.Id).ToDictionary(g => g.Key, g => g.First().i);
        guide.Cast = guide.Cast
            .OrderBy(c => c.Tier == CastTier.Key ? 0 : 1)
            .ThenBy(c => c.Tier == CastTier.Key ? 0 : orgOrder.GetValueOrDefault(c.OrganizationId, int.MaxValue))
            .ThenBy(c => IdNumber(c.Id))
            .ToList();
    }

    private static int IdNumber(string id)
    {
        var dash = id.LastIndexOf('-');
        return dash >= 0 && int.TryParse(id[(dash + 1)..], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var n) ? n : int.MaxValue;
    }

    /// <summary>
    /// One bounded call: waits for a slot, skips when a fatal error already stopped the run, and classifies the outcome.
    /// Throws only when the user cancels.
    /// </summary>
    private async Task<UnitOutcome<T>> RunUnitAsync<T>(LlmRequest request, CastRun run, Action? onStart = null) where T : class
    {
        await run.Semaphore.WaitAsync(run.UserToken);
        try
        {
            // A fatal error in another call stopped the run: leave this unit pending
            if (run.Cts.IsCancellationRequested)
            {
                run.UserToken.ThrowIfCancellationRequested();
                return new UnitOutcome<T>();
            }

            onStart?.Invoke();
            var value = (await _llm.GenerateAsync<T>(request, run.Cts.Token)).GetValueOrThrow();
            return new UnitOutcome<T> { Value = value };
        }
        catch (OperationCanceledException) when (run.UserToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException) when (run.Cts.IsCancellationRequested)
        {
            // Fatal stop from another call
            return new UnitOutcome<T>();
        }
        catch (OperationCanceledException)
        {
            // Run not cancelled: the request timed out
            return new UnitOutcome<T> { Error = "The request timed out. Click Retry Cast." };
        }
        catch (LlmCallException ex) when (ex.Result.IsFatal)
        {
            // Bad key or exhausted spend limit: stop all further calls, keep completed units
            run.SetFatal(ex.Result.Message ?? ex.Message);
            return new UnitOutcome<T>();
        }
        catch (LlmCallException ex)
        {
            return new UnitOutcome<T> { Error = DescribeOutcome(ex.Result, "Cast"), Refused = ex.Result.Outcome == LlmOutcome.Refused };
        }
        catch (Exception ex)
        {
            return new UnitOutcome<T> { Error = ex.Message };
        }
        finally
        {
            run.Semaphore.Release();
        }
    }

    private sealed class UnitOutcome<T> where T : class
    {
        public T? Value { get; set; }
        public string Error { get; set; } = string.Empty;
        public bool Refused { get; set; }
    }

    private sealed class CastRun
    {
        private readonly object _lock = new();
        private string _fatalError = string.Empty;

        public CastRun(CancellationTokenSource cts, SemaphoreSlim semaphore, CancellationToken userToken)
        {
            Cts = cts;
            Semaphore = semaphore;
            UserToken = userToken;
        }

        public CancellationTokenSource Cts { get; }
        public SemaphoreSlim Semaphore { get; }
        public CancellationToken UserToken { get; }

        public string FatalError
        {
            get
            {
                lock (_lock)
                    return _fatalError;
            }
        }

        public bool IsFatal => FatalError.Length > 0;

        public void SetFatal(string message)
        {
            lock (_lock)
            {
                if (_fatalError.Length == 0)
                    _fatalError = message.Length > 0 ? message : "The run stopped.";
            }

            Cts.Cancel();
        }
    }

    // ---------- Shared helpers ----------

    private static void SetReady(GuidePartState state)
    {
        state.Status = GuidePartStatus.Ready;
        state.FailureMessage = string.Empty;
        state.GeneratedUtc = DateTime.UtcNow;
    }

    private static void SetFailed(GuidePartState state, string message)
    {
        state.Status = GuidePartStatus.Failed;
        state.FailureMessage = message;
    }

    private static string DescribeOutcome(LlmResult result, string part) => result.Outcome switch
    {
        LlmOutcome.Refused => $"The model declined: {result.Message}",
        LlmOutcome.Truncated => $"The response was cut off. Click Retry {part}.",
        LlmOutcome.Invalid => $"The response could not be read. Click Retry {part}.",
        _ => $"{result.FailureKind}: {result.Message}"
    };

    private static string Clean(string? value) => (value ?? string.Empty).Trim();

    // API Response DTOs (strict schemas: no nullable properties, no recursion, dates as YYYY-MM-DD strings)
    internal class IssueDetailsDto
    {
        [JsonPropertyName("issues")]
        [Description("One entry per issue ID listed in the request")]
        public List<IssueDetailDto> Issues { get; set; } = new();
    }

    internal class IssueDetailDto
    {
        [JsonPropertyName("issueId")]
        [Description("Issue ID from the request, e.g. ISS-01")]
        public string IssueId { get; set; } = string.Empty;

        [JsonPropertyName("description")]
        [Description("2-3 sentences stating the issue as fact, taking a firm position")]
        public string Description { get; set; } = string.Empty;

        [JsonPropertyName("elements")]
        [Description("3-6 short items that must be proven")]
        public List<string> Elements { get; set; } = new();

        [JsonPropertyName("relevanceCriteria")]
        [Description("2-3 sentences describing what responsive evidence in an email corpus looks like")]
        public string RelevanceCriteria { get; set; } = string.Empty;
    }

    internal class CastRosterDto
    {
        [JsonPropertyName("organizations")]
        [Description("Organizations to add that are not listed in the request, e.g. outside law firms; empty when none")]
        public List<NewOrganizationDto> Organizations { get; set; } = new();

        [JsonPropertyName("people")]
        [Description("Key people in the cast")]
        public List<RosterPersonDto> People { get; set; } = new();
    }

    internal class NewOrganizationDto
    {
        [JsonPropertyName("name")]
        [Description("Full name, e.g. Thistle & Bracken LLP")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("shortName")]
        [Description("Short name used in text, e.g. Thistle & Bracken")]
        public string ShortName { get; set; } = string.Empty;

        [JsonPropertyName("kind")]
        [Description("LawFirm, Agency, ThirdParty or Party")]
        public OrganizationKind Kind { get; set; }

        [JsonPropertyName("representsRefs")]
        [Description("For a law firm: IDs of the organizations it represents, e.g. ORG-01; empty otherwise")]
        public List<string> RepresentsRefs { get; set; } = new();

        [JsonPropertyName("location")]
        [Description("City and state, e.g. Chicago, Illinois")]
        public string Location { get; set; } = string.Empty;
    }

    internal class RosterPersonDto
    {
        [JsonPropertyName("existingId")]
        [Description("ID of an existing character this entry revises, e.g. CH-003; empty string for a new person")]
        public string ExistingId { get; set; } = string.Empty;

        [JsonPropertyName("firstName")]
        [Description("First name")]
        public string FirstName { get; set; } = string.Empty;

        [JsonPropertyName("lastName")]
        [Description("Last name")]
        public string LastName { get; set; } = string.Empty;

        [JsonPropertyName("title")]
        [Description("Job title, e.g. Project Manager, General Counsel, Partner, Associate")]
        public string Title { get; set; } = string.Empty;

        [JsonPropertyName("organizationRef")]
        [Description("Organization ID from the request, e.g. ORG-02, or the exact name of an organization added in this response")]
        public string OrganizationRef { get; set; } = string.Empty;

        [JsonPropertyName("role")]
        [Description("Party, InHouseCounsel, OutsideCounsel or Staff")]
        public CastRole Role { get; set; }

        [JsonPropertyName("involvement")]
        [Description("One line on this person's involvement in the matter")]
        public string Involvement { get; set; } = string.Empty;
    }

    internal class CastDetailsDto
    {
        [JsonPropertyName("people")]
        [Description("One entry per character ID listed in the request")]
        public List<CastDetailDto> People { get; set; } = new();
    }

    internal class CastDetailDto
    {
        [JsonPropertyName("castId")]
        [Description("Character ID from the request, e.g. CH-004")]
        public string CastId { get; set; } = string.Empty;

        [JsonPropertyName("summary")]
        [Description("2-4 sentences on the person's key involvement, stated as fact")]
        public string Summary { get; set; } = string.Empty;

        [JsonPropertyName("activeFrom")]
        [Description("First date active in the story, YYYY-MM-DD")]
        public string ActiveFrom { get; set; } = string.Empty;

        [JsonPropertyName("activeTo")]
        [Description("Last date active in the story, YYYY-MM-DD")]
        public string ActiveTo { get; set; } = string.Empty;

        [JsonPropertyName("voiceNote")]
        [Description("1-2 lines on how this person writes email")]
        public string VoiceNote { get; set; } = string.Empty;

        [JsonPropertyName("signatureStyle")]
        [Description("Full, Short, NameOnly or MobileFooter")]
        public SignatureStyle SignatureStyle { get; set; }
    }

    internal class BackgroundStaffDto
    {
        [JsonPropertyName("people")]
        [Description("Background staff for the organization")]
        public List<BackgroundPersonDto> People { get; set; } = new();
    }

    internal class BackgroundPersonDto
    {
        [JsonPropertyName("firstName")]
        [Description("First name")]
        public string FirstName { get; set; } = string.Empty;

        [JsonPropertyName("lastName")]
        [Description("Last name")]
        public string LastName { get; set; } = string.Empty;

        [JsonPropertyName("title")]
        [Description("Job title")]
        public string Title { get; set; } = string.Empty;

        [JsonPropertyName("department")]
        [Description("Department, e.g. Accounting")]
        public string Department { get; set; } = string.Empty;

        [JsonPropertyName("role")]
        [Description("One line on what this person does at the organization")]
        public string Role { get; set; } = string.Empty;

        [JsonPropertyName("activeFrom")]
        [Description("YYYY-MM-DD")]
        public string ActiveFrom { get; set; } = string.Empty;

        [JsonPropertyName("activeTo")]
        [Description("YYYY-MM-DD")]
        public string ActiveTo { get; set; } = string.Empty;
    }
}

public class GuideProgress
{
    public GuidePart Part { get; set; }
    public string Message { get; set; } = string.Empty;
    public int Done { get; set; }
    public int Total { get; set; }
}

/// <summary>
/// Outcome of one guide part run. Failures are also recorded on the part's GuidePartState.
/// </summary>
public class GuidePartResult
{
    public GuidePart Part { get; set; }
    public bool Succeeded { get; set; }
    public string FailureMessage { get; set; } = string.Empty;

    /// <summary>
    /// Non-empty when a fatal failure (bad key or spend limit) stopped further calls.
    /// </summary>
    public string FatalError { get; set; } = string.Empty;

    public bool IsFatal => FatalError.Length > 0;
    public bool WasRefused { get; set; }

    /// <summary>
    /// Mapping notes (ignored ids, dropped people, defaulted dates).
    /// </summary>
    public List<string> Notes { get; set; } = new();
}
