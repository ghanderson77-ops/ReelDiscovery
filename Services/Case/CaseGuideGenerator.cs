using System.ComponentModel;
using System.Globalization;
using System.Text.Json.Serialization;
using ReelDiscovery.Models.Case;
using ReelDiscovery.Services.Ai;

namespace ReelDiscovery.Services.Case;

/// <summary>
/// Builds the case guide through the Story tier (D-03): issues extracted in code and enriched by one call (GUIDE-01),
/// then the cast (GUIDE-02..04), the dated key facts (GUIDE-05) and the canonical values (GUIDE-06).
/// Code owns every id, link check, email address, phone and signature.
/// </summary>
public class CaseGuideGenerator
{
    public const int IssueDetailsMaxOutputTokens = 8_000;
    public const int RosterMaxOutputTokens = 12_000;
    public const int CastDetailsMaxOutputTokens = 12_000;
    public const int BackgroundMaxOutputTokens = 8_000;
    public const int MaxParallelCalls = 3;
    public const int MaxDetailsPerCall = 10;
    public const int FactsMaxOutputTokens = 16_000;
    public const int ValuesMaxOutputTokens = 12_000;
    public const int MaxFactsPerCall = 30;
    public const int EntryMaxOutputTokens = 4_000;

    private readonly ILlmGateway _llm;

    /// <summary>
    /// Top-level response types sent as strict schemas (verified by tests).
    /// </summary>
    internal static IReadOnlyList<Type> SchemaTypes { get; } = new[]
    {
        typeof(IssueDetailsDto), typeof(CastRosterDto), typeof(CastDetailsDto), typeof(BackgroundStaffDto), typeof(FactBatchDto), typeof(ValuesDto),
        typeof(CastEntryDto),
        typeof(FactEntryDto),
        typeof(ValueEntryDto)
    };

    public CaseGuideGenerator(ILlmGateway llm)
    {
        _llm = llm;
    }

    // ---------- Whole guide ----------

    /// <summary>
    /// Runs issues -> cast -> facts -> values (D-06): finished parts are skipped, the run stops at the first failed part or on a
    /// fatal error, and <paramref name="partCompleted"/> is awaited after each part so the caller can autosave.
    /// A user cancel propagates after the callback ran for any partial results.
    /// </summary>
    public async Task<CaseGuideGenerationResult> GenerateAsync(CaseDraft draft, IProgress<GuideProgress>? progress = null,
        Func<GuidePart, Task>? partCompleted = null, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(draft);
        var guide = draft.Guide ??= new CaseGuide();
        var result = new CaseGuideGenerationResult();

        try
        {
            foreach (var part in GuideStaleness.Order)
            {
                // Finished parts are skipped; Issues counts as finished even when its details failed
                if (guide.State(part).Status == GuidePartStatus.Ready)
                    continue;

                // Every earlier part must be finished first
                if (GuideStaleness.Order.TakeWhile(p => p != part).Any(p => guide.State(p).Status != GuidePartStatus.Ready))
                    break;

                var partResult = await RunTrackedAsync(draft, part, () => RunPartAsync(draft, part, progress, ct), partCompleted);
                if (!RecordPart(draft, result, part, partResult))
                    break;
            }
        }
        finally
        {
            if (result.PartsWritten.Count > 0)
                draft.Revision++;
        }

        return result;
    }

    /// <summary>
    /// Update Guide (GUIDE-09, D-20): re-runs only the stale parts, in order issues -> cast -> facts -> values, keeping hand edits.
    /// Issues are re-extracted (ids and details kept); Cast, Facts and Values are regenerated without guidance. Stops at the first
    /// failed part or a fatal error; <paramref name="partCompleted"/> is awaited after each part so the caller can autosave.
    /// </summary>
    public async Task<CaseGuideGenerationResult> UpdateStaleAsync(CaseDraft draft, IProgress<GuideProgress>? progress = null,
        Func<GuidePart, Task>? partCompleted = null, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(draft);
        var result = new CaseGuideGenerationResult();

        try
        {
            // Only parts the user asked to update, and only when stale: nothing regenerates silently (D-20)
            foreach (var part in GuideStaleness.Order)
            {
                // Re-checked at each step: an earlier regeneration changes the later parts' basis
                if (!GuideStaleness.IsStale(draft, part))
                    continue;

                var partResult = await RunTrackedAsync(draft, part, () => part == GuidePart.Issues
                    ? GenerateIssuesAsync(draft, progress, ct)
                    : RegenerateTabAsync(draft, part, string.Empty, replaceEdited: false, progress, ct), partCompleted);
                if (!RecordPart(draft, result, part, partResult))
                    break;
            }
        }
        finally
        {
            if (result.PartsWritten.Count > 0)
                draft.Revision++;
        }

        return result;
    }

    // Runs one part, then awaits the autosave hook: after a finished or failed part, and after a cancelled part that kept partial results
    private static async Task<GuidePartResult> RunTrackedAsync(CaseDraft draft, GuidePart part, Func<Task<GuidePartResult>> run,
        Func<GuidePart, Task>? partCompleted)
    {
        var revision = draft.Revision;
        var finished = false;
        try
        {
            var partResult = await run();
            finished = true;
            return partResult;
        }
        finally
        {
            if (partCompleted != null && (finished || draft.Revision != revision))
                await partCompleted(part);
        }
    }

    // Adds one part's outcome to the run result; false when the run must stop (failure or fatal error)
    private static bool RecordPart(CaseDraft draft, CaseGuideGenerationResult result, GuidePart part, GuidePartResult partResult)
    {
        var guide = draft.Guide!;
        result.Notes.AddRange(partResult.Notes);
        if (partResult.Succeeded)
        {
            result.PartsWritten.Add(part);
            if (part == GuidePart.Issues && guide.IssueDetailsState.Status == GuidePartStatus.Failed)
                result.Notes.Add(guide.IssueDetailsState.FailureMessage);
        }

        if (partResult.IsFatal)
        {
            result.FatalError = partResult.FatalError;
            return false;
        }

        if (partResult.Succeeded)
            return true;

        result.PartsFailed.Add(part);
        result.FailureMessage = partResult.FailureMessage;
        if (partResult.WasRefused)
        {
            result.RefusedPart = part;
            result.RefusalMessage = partResult.FailureMessage;
        }

        return false;
    }

    private Task<GuidePartResult> RunPartAsync(CaseDraft draft, GuidePart part, IProgress<GuideProgress>? progress, CancellationToken ct) => part switch
    {
        GuidePart.Issues => GenerateIssuesAsync(draft, progress, ct),
        GuidePart.Cast => GenerateCastAsync(draft, progress, ct),
        GuidePart.Facts => GenerateFactsAsync(draft, progress, ct),
        GuidePart.Values => GenerateValuesAsync(draft, progress, ct),
        _ => throw new ArgumentOutOfRangeException(nameof(part), part, "Unknown guide part")
    };

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
            SetFailed(state, DescribeOutcome(ex.Result, RetryHint("Issue Details")));
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
        var run = new UnitRun(runCts, semaphore, ct, RetryHint("Cast"));

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

                await TopUpCounselAsync(draft, result, run);
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

    // At most one counsel top-up per run (W2)
    private async Task TopUpCounselAsync(CaseDraft draft, GuidePartResult result, UnitRun run)
    {
        var gaps = CounselGaps(draft);
        if (gaps.Count == 0)
            return;

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

    // Model output -> organizations and key characters; code assigns every id, email and phone (T-03-30)
    private static void MapRoster(CaseDraft draft, CastRosterDto dto, GuidePartResult result)
    {
        var guide = draft.Guide!;
        MapOrganizations(guide, dto, result);

        foreach (var person in dto.People ?? new List<RosterPersonDto>())
        {
            if (person == null)
                continue;

            // Existing characters are never duplicated
            if (guide.FindCast(IdAllocator.Normalize(person.ExistingId, IdAllocator.Character)) != null)
                continue;

            AddRosterPerson(draft, person, result);
        }
    }

    // A new key character from a roster entry; skipped when the name is already in the cast, dropped with a note when the organization is unknown
    private static void AddRosterPerson(CaseDraft draft, RosterPersonDto person, GuidePartResult result)
    {
        var guide = draft.Guide!;
        var first = Clean(person.FirstName);
        var last = Clean(person.LastName);
        var fullName = RosterName(person);
        if (fullName.Length == 0 || FindByName(guide, fullName) != null)
            return;

        var org = ResolveOrganization(guide, person.OrganizationRef);
        if (org == null)
        {
            result.Notes.Add($"Dropped {fullName}: unknown organization {Clean(person.OrganizationRef)}.");
            return;
        }

        AddKeyMember(draft, first, last, Clean(person.Title), org, person.Role, Clean(person.Involvement));
    }

    private static string RosterName(RosterPersonDto person) =>
        string.Join(" ", new[] { Clean(person.FirstName), Clean(person.LastName) }.Where(s => s.Length > 0));

    // New organizations from a roster response (matched by name first), then their represented organizations
    private static void MapOrganizations(CaseGuide guide, CastRosterDto dto, GuidePartResult result)
    {
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

    private async Task<bool> WriteDetailsAsync(CaseDraft draft, GuidePartResult result, UnitRun run, IProgress<GuideProgress>? progress)
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

    private async Task<bool> WriteBackgroundAsync(CaseDraft draft, GuidePartResult result, UnitRun run, IProgress<GuideProgress>? progress)
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

    private GuidePartResult FinishCast(CaseDraft draft, GuidePartResult result, UnitRun run, string rosterError, bool rosterRefused, bool changed)
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
    private async Task<UnitOutcome<T>> RunUnitAsync<T>(LlmRequest request, UnitRun run, Action? onStart = null) where T : class
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
            return new UnitOutcome<T> { Error = $"The request timed out. {run.RetryHint}" };
        }
        catch (LlmCallException ex) when (ex.Result.IsFatal)
        {
            // Bad key or exhausted spend limit: stop all further calls, keep completed units
            run.SetFatal(ex.Result.Message ?? ex.Message);
            return new UnitOutcome<T>();
        }
        catch (LlmCallException ex)
        {
            return new UnitOutcome<T> { Error = DescribeOutcome(ex.Result, run.RetryHint), Refused = ex.Result.Outcome == LlmOutcome.Refused };
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

    private sealed class UnitRun
    {
        private readonly object _lock = new();
        private string _fatalError = string.Empty;

        public UnitRun(CancellationTokenSource cts, SemaphoreSlim semaphore, CancellationToken userToken, string retryHint)
        {
            Cts = cts;
            Semaphore = semaphore;
            UserToken = userToken;
            RetryHint = retryHint;
        }

        /// <summary>
        /// Last sentence of failure messages, e.g. "Click Retry Cast.".
        /// </summary>
        public string RetryHint { get; }

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

    // ---------- Key facts ----------

    /// <summary>
    /// One call per core-document fact section (split by date when a section needs more than MaxFactsPerCall facts), at most
    /// MaxParallelCalls in flight. Code checks every link and assigns F-ids in date order. Finished sections are skipped on Retry.
    /// Cancellation propagates after the finished sections are merged.
    /// </summary>
    public async Task<GuidePartResult> GenerateFactsAsync(CaseDraft draft, IProgress<GuideProgress>? progress = null, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(draft);
        if (draft.CoreDocument == null)
            throw new InvalidOperationException("Write the core document before building the case guide.");
        var guide = draft.Guide ??= new CaseGuide();
        var state = guide.FactsState;
        var result = new GuidePartResult { Part = GuidePart.Facts };

        if (guide.CastState.Status != GuidePartStatus.Ready || guide.Cast.Count == 0)
        {
            result.FailureMessage = "Write the cast before the key facts.";
            return result;
        }

        var all = PlanFactUnits(draft);
        if (all.Count == 0)
        {
            SetFailed(state, "The core document has no fact sections.");
            result.FailureMessage = state.FailureMessage;
            return result;
        }

        var units = all.Where(u => !state.CompletedUnits.Contains(u.Key)).ToList();
        state.Status = GuidePartStatus.Generating;

        using var runCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        using var semaphore = new SemaphoreSlim(MaxParallelCalls, MaxParallelCalls);
        var run = new UnitRun(runCts, semaphore, ct, RetryHint("Key Facts"));

        // Tasks return DTOs only; prompts are built here, before any merge
        var tasks = units.Select((unit, i) => RunUnitAsync<FactBatchDto>(new LlmRequest
        {
            Tier = LlmTier.Story,
            OperationName = $"Case Guide - Facts ({unit.Plan.Heading})",
            SystemPrompt = CaseGuidePrompts.SystemPrompt,
            UserPrompt = CaseGuidePrompts.BuildFactsPrompt(draft, unit.Plan, unit.From, unit.To, unit.Target, Array.Empty<KeyFact>()),
            MaxOutputTokens = FactsMaxOutputTokens
        }, run, () => progress?.Report(new GuideProgress
        {
            Part = GuidePart.Facts,
            Message = $"Writing the key facts: {unit.Plan.Heading} ({i + 1} of {units.Count})...",
            Done = i,
            Total = units.Count
        }))).ToList();

        var changed = false;
        try
        {
            try
            {
                await Task.WhenAll(tasks);
            }
            finally
            {
                // Merge once on the calling thread: generator tests have no SynchronizationContext, so lambdas run on pool threads (RESEARCH Pattern 5)
                changed = MergeFacts(guide, units, tasks);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // User cancel: finished sections are kept for Retry
            state.Status = GuidePartStatus.Pending;
            if (changed)
                draft.Revision++;
            throw;
        }

        if (changed)
            draft.Revision++;

        var outcomes = tasks.Where(t => t.IsCompletedSuccessfully).Select(t => t.Result).ToList();
        result.WasRefused = outcomes.Any(o => o.Refused);

        if (run.IsFatal)
        {
            // Bad key or exhausted spend limit: finished sections are kept
            state.Status = GuidePartStatus.Pending;
            result.FatalError = run.FatalError;
            return result;
        }

        if (all.All(u => state.CompletedUnits.Contains(u.Key)))
        {
            SetReady(state);
            state.CompletedUnits.Clear();
            GuideStaleness.Stamp(draft, GuidePart.Facts);
            result.Succeeded = true;
            return result;
        }

        var errors = outcomes.Where(o => o.Value == null && o.Error.Length > 0).Select(o => o.Error).ToList();
        var first = errors.FirstOrDefault() ?? "Some periods were not written. Click Retry Key Facts.";
        var message = errors.Count > 1 ? $"{errors.Count} periods were not written: {first}" : first;
        SetFailed(state, message);
        result.FailureMessage = message;
        return result;
    }

    /// <summary>
    /// Fact calls for the preset's target count: the midpoint of FactsMin..FactsMax spread over the fact sections by
    /// TargetParagraphs (largest remainder, at least 2 each); a section above MaxFactsPerCall is split into equal date ranges.
    /// </summary>
    internal static List<(FactSubsectionPlan Plan, string From, string To, int Target)> PlanFactCalls(CaseDraft draft) =>
        PlanFactUnits(draft).Select(u => (u.Plan, u.From, u.To, u.Target)).ToList();

    private static List<FactUnit> PlanFactUnits(CaseDraft draft)
    {
        var doc = draft.CoreDocument ?? throw new InvalidOperationException("Write the core document before building the case guide.");
        var preset = draft.Preset;
        var plans = (doc.FactPlan ?? new List<FactSubsectionPlan>()).Where(p => p != null && !string.IsNullOrEmpty(p.Id)).ToList();
        var units = new List<FactUnit>();
        if (plans.Count == 0)
            return units;

        var targets = Distribute((preset.FactsMin + preset.FactsMax) / 2, plans.Select(p => Math.Max(0, p.TargetParagraphs)).ToList());
        for (var i = 0; i < plans.Count; i++)
        {
            var plan = plans[i];
            var target = Math.Max(2, targets[i]);
            if (target <= MaxFactsPerCall)
            {
                units.Add(new FactUnit(plan.Id, plan, plan.StartDate, plan.EndDate, target));
                continue;
            }

            if (!CaseDraft.TryParseDate(plan.StartDate, out var start) || !CaseDraft.TryParseDate(plan.EndDate, out var end) || end < start)
            {
                // No usable date range to split: one call at the per-call cap
                units.Add(new FactUnit(plan.Id, plan, plan.StartDate, plan.EndDate, MaxFactsPerCall));
                continue;
            }

            var parts = (target + MaxFactsPerCall - 1) / MaxFactsPerCall;
            var shares = Distribute(target, Enumerable.Repeat(1, parts).ToList());
            var days = (end - start).Days;
            for (var k = 0; k < parts; k++)
            {
                var from = k == 0 ? start : start.AddDays(days * k / parts + 1);
                var to = k == parts - 1 ? end : start.AddDays(days * (k + 1) / parts);
                if (from > to)
                    from = to;
                units.Add(new FactUnit($"{plan.Id}#{k + 1}", plan, FormatDate(from), FormatDate(to), shares[k]));
            }
        }

        return units;
    }

    // Largest remainder: integer shares of total proportional to the weights (equal shares when every weight is 0)
    private static List<int> Distribute(int total, List<int> weights)
    {
        if (weights.Sum() <= 0)
            weights = weights.Select(_ => 1).ToList();

        var sum = weights.Sum();
        var exact = weights.Select(w => (double)total * w / sum).ToList();
        var shares = exact.Select(e => (int)Math.Floor(e)).ToList();
        var left = total - shares.Sum();
        foreach (var i in Enumerable.Range(0, weights.Count).OrderByDescending(i => exact[i] - shares[i]).ThenBy(i => i).Take(left))
            shares[i]++;

        return shares;
    }

    private static string FormatDate(DateTime date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    // Maps every finished unit, allocates F-ids in date order and keeps guide.Facts sorted; true when anything changed
    private static bool MergeFacts(CaseGuide guide, List<FactUnit> units, List<Task<UnitOutcome<FactBatchDto>>> tasks)
    {
        var added = new List<(KeyFact Fact, DateTime Date, int Unit, int Seq)>();
        var completed = false;

        for (var i = 0; i < tasks.Count; i++)
        {
            if (!tasks[i].IsCompletedSuccessfully || tasks[i].Result.Value is not { } batch)
                continue;

            var seq = 0;
            foreach (var dto in batch.Facts ?? new List<FactDto>())
            {
                var fact = MapFact(guide, dto, units[i].Plan.Id);
                if (fact != null)
                    added.Add((fact, SortDate(fact.Date), i, seq++));
            }

            if (!guide.FactsState.CompletedUnits.Contains(units[i].Key))
                guide.FactsState.CompletedUnits.Add(units[i].Key);
            completed = true;
        }

        foreach (var item in added.OrderBy(a => a.Date).ThenBy(a => a.Unit).ThenBy(a => a.Seq))
        {
            item.Fact.Id = IdAllocator.Next(guide, IdAllocator.Fact);
            guide.Facts.Add(item.Fact);
        }

        if (added.Count > 0)
            guide.Facts = guide.Facts.OrderBy(f => SortDate(f.Date)).ThenBy(f => IdNumber(f.Id)).ToList();

        return completed;
    }

    // Undated or malformed dates sort last
    private static DateTime SortDate(string? date) => CaseDraft.TryParseDate(date, out var d) ? d : DateTime.MaxValue;

    // Model output -> fact; unknown issue and character references are dropped with mapping notes (T-03-34)
    private static KeyFact? MapFact(CaseGuide guide, FactDto? dto, string sectionId)
    {
        if (dto == null)
            return null;

        var title = Clean(dto.Title);
        var description = Clean(dto.Description);
        if (title.Length == 0 && description.Length == 0)
            return null;

        var fact = new KeyFact
        {
            Date = Clean(dto.Date),
            Title = title,
            Description = description,
            Significance = dto.Significance,
            SourceSectionId = sectionId
        };

        foreach (var raw in dto.IssueIds ?? new List<string>())
        {
            var id = IdAllocator.Normalize(raw, IdAllocator.Issue);
            if (id != null && guide.FindIssue(id) != null)
            {
                if (!fact.IssueIds.Contains(id))
                    fact.IssueIds.Add(id);
            }
            else if (Clean(raw).Length > 0)
            {
                fact.MappingNotes.Add($"Dropped unknown issue {Clean(raw)}.");
            }
        }

        fact.ActorIds = ResolveCast(guide, dto.ActorIds, fact.MappingNotes);
        // Actors always know (Phase 5 knowledge gating)
        fact.KnownByIds = fact.ActorIds.Union(ResolveCast(guide, dto.KnownByIds, fact.MappingNotes)).ToList();

        if (!CaseDraft.TryParseDate(fact.Date, out _))
            fact.MappingNotes.Add($"The date \"{fact.Date}\" is not YYYY-MM-DD.");

        fact.MappingNotes = fact.MappingNotes.Distinct(StringComparer.Ordinal).ToList();
        return fact;
    }

    // CH id (normalized, must exist) or an exact full name
    private static List<string> ResolveCast(CaseGuide guide, List<string>? refs, List<string> notes)
    {
        var ids = new List<string>();
        foreach (var raw in refs ?? new List<string>())
        {
            var text = Clean(raw);
            if (text.Length == 0)
                continue;

            var member = guide.FindCast(IdAllocator.Normalize(text, IdAllocator.Character)) ?? FindByName(guide, text);
            if (member == null)
            {
                notes.Add($"Dropped unknown character {text}.");
                continue;
            }

            if (!ids.Contains(member.Id))
                ids.Add(member.Id);
        }

        return ids;
    }

    // ---------- Canonical values ----------

    /// <summary>
    /// One call for the canonical values registry, seeded with every amount and identifier the scanner finds in the
    /// core document and the facts (GUIDE-06). Code assigns V-ids, checks fact links and merges duplicates. Cancellation propagates.
    /// </summary>
    public async Task<GuidePartResult> GenerateValuesAsync(CaseDraft draft, IProgress<GuideProgress>? progress = null, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(draft);
        if (draft.CoreDocument == null)
            throw new InvalidOperationException("Write the core document before building the case guide.");
        var guide = draft.Guide ??= new CaseGuide();
        var state = guide.ValuesState;
        var result = new GuidePartResult { Part = GuidePart.Values };

        if (guide.Facts.Count == 0)
        {
            result.FailureMessage = "Write the key facts before the canonical values.";
            return result;
        }

        var scanned = ScanValues(draft);
        state.Status = GuidePartStatus.Generating;
        progress?.Report(new GuideProgress { Part = GuidePart.Values, Message = "Writing the canonical values..." });

        try
        {
            var dto = (await _llm.GenerateAsync<ValuesDto>(new LlmRequest
            {
                Tier = LlmTier.Story,
                OperationName = "Case Guide - Canonical values",
                SystemPrompt = CaseGuidePrompts.SystemPrompt,
                UserPrompt = CaseGuidePrompts.BuildValuesPrompt(draft, scanned, Array.Empty<CanonicalValue>()),
                MaxOutputTokens = ValuesMaxOutputTokens
            }, ct)).GetValueOrThrow();

            if (MapValues(guide, dto))
                draft.Revision++;

            SetReady(state);
            GuideStaleness.Stamp(draft, GuidePart.Values);
            result.Succeeded = true;
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
            SetFailed(state, "The request timed out. Click Retry Canonical Values.");
        }
        catch (LlmCallException ex) when (ex.Result.IsFatal)
        {
            // Bad key or exhausted spend limit: stop the run
            state.Status = GuidePartStatus.Pending;
            result.FatalError = ex.Result.Message ?? ex.Message;
        }
        catch (LlmCallException ex)
        {
            result.WasRefused = ex.Result.Outcome == LlmOutcome.Refused;
            SetFailed(state, DescribeOutcome(ex.Result, RetryHint("Canonical Values")));
        }
        catch (Exception ex)
        {
            SetFailed(state, ex.Message);
        }

        if (state.Status == GuidePartStatus.Failed)
            result.FailureMessage = state.FailureMessage;
        return result;
    }

    // Amounts and identifiers in the core document, then in the facts; distinct by text in order of appearance
    private static List<ScannedValue> ScanValues(CaseDraft draft)
    {
        var texts = draft.CoreDocument!.Sections
            .Where(s => s != null)
            .SelectMany(s => s.Paragraphs ?? new List<string>())
            .Concat(draft.Guide!.Facts.Where(f => f != null).Select(f => f.Description));

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var hits = new List<ScannedValue>();
        foreach (var text in texts)
        {
            foreach (var hit in CanonicalValueScanner.Scan(text))
            {
                if (seen.Add(hit.Text))
                    hits.Add(hit);
            }
        }

        return hits;
    }

    // Model output -> values; fact links are normalized and existence-checked, duplicates (same label and value) merged (T-03-34)
    private static bool MapValues(CaseGuide guide, ValuesDto dto)
    {
        var used = new HashSet<string>(StringComparer.Ordinal);
        var changed = false;

        foreach (var item in dto.Values ?? new List<ValueDto>())
        {
            var mapped = item == null ? null : MapValue(guide, item.Kind, item.Label, item.Value, item.Description, item.FactIds);
            if (item == null || mapped == null)
                continue;

            var text = mapped.Value;
            var label = mapped.Label;
            var notes = mapped.MappingNotes;
            var factIds = mapped.FactIds;

            // existingId is honoured only for an existing, not hand-edited value not already used in this response
            var existingId = IdAllocator.Normalize(item.ExistingId, IdAllocator.Value);
            var target = existingId != null && !used.Contains(existingId) ? guide.FindValue(existingId) : null;
            if (target != null && !target.IsUserEdited)
            {
                target.Kind = item.Kind;
                target.Label = label;
                target.Value = text;
                target.Description = Clean(item.Description);
                target.FactIds = factIds;
                target.MappingNotes = notes;
                used.Add(target.Id);
                changed = true;
                continue;
            }

            var duplicate = guide.Values.FirstOrDefault(v => v != null
                && string.Equals(v.Label, label, StringComparison.OrdinalIgnoreCase)
                && string.Equals(v.Value, text, StringComparison.OrdinalIgnoreCase));
            if (duplicate != null)
            {
                // Hand-edited values are never changed by the model
                if (duplicate.IsUserEdited)
                    continue;

                duplicate.FactIds = duplicate.FactIds.Union(factIds).ToList();
                duplicate.MappingNotes = duplicate.MappingNotes.Union(notes).ToList();
                changed = true;
                continue;
            }

            var value = new CanonicalValue
            {
                Id = IdAllocator.Next(guide, IdAllocator.Value),
                Kind = item.Kind,
                Label = label,
                Value = text,
                Description = Clean(item.Description),
                FactIds = factIds,
                MappingNotes = notes
            };
            guide.Values.Add(value);
            used.Add(value.Id);
            changed = true;
        }

        return changed;
    }

    // Model output -> unsaved value (no id); fact links normalized and existence-checked; null when the value text is empty
    private static CanonicalValue? MapValue(CaseGuide guide, ValueKind kind, string? label, string? value, string? description, List<string>? factRefs)
    {
        var text = Clean(value);
        if (text.Length == 0)
            return null;

        var mapped = new CanonicalValue
        {
            Kind = kind,
            Label = Clean(label).Length > 0 ? Clean(label) : text,
            Value = text,
            Description = Clean(description)
        };

        foreach (var raw in factRefs ?? new List<string>())
        {
            var id = IdAllocator.Normalize(raw, IdAllocator.Fact);
            if (id != null && guide.FindFact(id) != null)
            {
                if (!mapped.FactIds.Contains(id))
                    mapped.FactIds.Add(id);
            }
            else if (Clean(raw).Length > 0)
            {
                mapped.MappingNotes.Add($"Dropped unknown fact {Clean(raw)}.");
            }
        }

        return mapped;
    }

    private sealed class FactUnit
    {
        public FactUnit(string key, FactSubsectionPlan plan, string from, string to, int target)
        {
            Key = key;
            Plan = plan;
            From = from;
            To = to;
            Target = target;
        }

        /// <summary>
        /// CompletedUnits key: the section id, or "{id}#{k}" for one date slice of a split section.
        /// </summary>
        public string Key { get; }

        public FactSubsectionPlan Plan { get; }
        public string From { get; }
        public string To { get; }
        public int Target { get; }
    }

    // ---------- Tab regeneration (D-19, GUIDE-08) ----------

    /// <summary>
    /// Rewrites the Cast, Facts or Values tab with optional guidance. Entries the model returns by existingId keep their ids,
    /// entries it leaves out are removed, and hand-edited entries are kept unless <paramref name="replaceEdited"/> is set.
    /// A failed call leaves the tab's entries untouched; a user cancel propagates. Issues are re-extracted, not regenerated.
    /// </summary>
    public async Task<GuidePartResult> RegenerateTabAsync(CaseDraft draft, GuidePart part, string guidance, bool replaceEdited,
        IProgress<GuideProgress>? progress = null, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(draft);
        if (part is not (GuidePart.Cast or GuidePart.Facts or GuidePart.Values))
            throw new ArgumentException("Only the Cast, Key Facts and Canonical Values tabs can be regenerated.", nameof(part));
        if (draft.CoreDocument == null)
            throw new InvalidOperationException("Write the core document before building the case guide.");

        draft.Guide ??= new CaseGuide();
        guidance ??= string.Empty;

        return part switch
        {
            GuidePart.Cast => await RegenerateCastAsync(draft, guidance, replaceEdited, progress, ct),
            GuidePart.Facts => await RegenerateFactsAsync(draft, guidance, replaceEdited, progress, ct),
            _ => await RegenerateValuesAsync(draft, guidance, replaceEdited, progress, ct)
        };
    }

    // Whole Cast tab: organizations re-synced from the parties, key roster merged, counsel topped up, details written,
    // and every non-individual organization's non-edited background staff rewritten (D-19)
    private async Task<GuidePartResult> RegenerateCastAsync(CaseDraft draft, string guidance, bool replaceEdited,
        IProgress<GuideProgress>? progress, CancellationToken ct)
    {
        var guide = draft.Guide!;
        var state = guide.CastState;
        var result = new GuidePartResult { Part = GuidePart.Cast };

        // Renamed or added parties follow the core document; ids and domains are kept
        var synced = SyncOrganizations(draft).Count > 0;

        var previous = state.Status;
        state.Status = GuidePartStatus.Generating;
        state.CompletedUnits.Clear();

        using var runCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        using var semaphore = new SemaphoreSlim(MaxParallelCalls, MaxParallelCalls);
        var run = new UnitRun(runCts, semaphore, ct, RegenerateHint("Cast"));
        var merged = false;
        var backgroundErrors = new List<string>();

        try
        {
            progress?.Report(new GuideProgress { Part = GuidePart.Cast, Message = "Regenerating the cast: key characters..." });

            var roster = await RunUnitAsync<CastRosterDto>(new LlmRequest
            {
                Tier = LlmTier.Story,
                OperationName = "Case Guide - Cast roster",
                SystemPrompt = CaseGuidePrompts.SystemPrompt,
                UserPrompt = CaseGuidePrompts.BuildRosterPrompt(draft, guidance, replaceEdited),
                MaxOutputTokens = RosterMaxOutputTokens
            }, run);

            if (roster.Value == null)
            {
                // Nothing merged: the cast stays exactly as it was
                state.Status = previous;
                if (synced)
                    draft.Revision++;
                result.FatalError = run.FatalError;
                result.WasRefused = roster.Refused;
                result.FailureMessage = roster.Error;
                return result;
            }

            MergeRoster(draft, roster.Value, replaceEdited, result);
            merged = true;
            SeedAuthor(draft);
            await TopUpCounselAsync(draft, result, run);
            SyncCounsel(draft);

            if (!run.IsFatal)
                await WriteDetailsAsync(draft, result, run, progress);
            if (!run.IsFatal)
                await RewriteBackgroundAsync(draft, guidance, replaceEdited, result, backgroundErrors, run, progress);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // User cancel: an untouched cast keeps its status; a merged roster finishes through Retry Cast
            state.Status = merged ? GuidePartStatus.Pending : previous;
            if (merged)
                OrderCast(guide);
            if (merged || synced)
                draft.Revision++;
            throw;
        }

        OrderCast(guide);
        draft.Revision++;

        if (run.IsFatal)
        {
            // Bad key or exhausted spend limit: the merged roster is kept and finishes through Retry Cast
            state.Status = GuidePartStatus.Pending;
            result.FatalError = run.FatalError;
            return result;
        }

        var details = guide.Cast.Count(c => c.Tier == CastTier.Key && c.DetailsPending);
        if (details > 0)
        {
            var message = $"Details for {details} characters were not written. {RetryHint("Cast")}";
            SetFailed(state, message);
            result.FailureMessage = message;
            return result;
        }

        SetReady(state);
        GuideStaleness.Stamp(draft, GuidePart.Cast);

        // A failed background call kept that organization's old staff; the cast is still complete
        result.Succeeded = backgroundErrors.Count == 0;
        if (!result.Succeeded)
            result.FailureMessage = $"Background staff for {backgroundErrors.Count} organizations were not rewritten: {backgroundErrors[0]}";
        return result;
    }

    // Key roster merge: matched people updated in place (DetailsPending), new people added, non-edited people left out removed
    private static void MergeRoster(CaseDraft draft, CastRosterDto dto, bool replaceEdited, GuidePartResult result)
    {
        var guide = draft.Guide!;
        MapOrganizations(guide, dto, result);

        var people = (dto.People ?? new List<RosterPersonDto>()).Where(p => p != null && RosterName(p).Length > 0).ToList();
        var removed = MergeEntries(
            guide.Cast.Where(c => c != null && c.Tier == CastTier.Key).ToList(),
            people,
            replaceEdited,
            IdAllocator.Character,
            c => c.Id,
            c => c.IsUserEdited,
            c => c.FullName,
            p => p.ExistingId,
            (p, c) => string.Equals(c.FullName, RosterName(p), StringComparison.OrdinalIgnoreCase),
            (member, person) => UpdateKeyMember(draft, member, person),
            person => AddRosterPerson(draft, person, result),
            result.Notes);

        foreach (var member in removed)
            RemoveCastMember(guide, member);
    }

    private static void UpdateKeyMember(CaseDraft draft, CastMember member, RosterPersonDto person)
    {
        var guide = draft.Guide!;
        var oldName = member.FullName;
        var oldOrg = member.OrganizationId;

        member.FirstName = Clean(person.FirstName);
        member.LastName = Clean(person.LastName);
        if (Clean(person.Title).Length > 0)
            member.Title = Clean(person.Title);
        // An unknown organization reference keeps the current organization
        member.OrganizationId = ResolveOrganization(guide, person.OrganizationRef)?.Id ?? member.OrganizationId;
        member.Role = person.Role;
        if (Clean(person.Involvement).Length > 0)
            member.Involvement = Clean(person.Involvement);
        member.IsUserEdited = false;
        member.DetailsPending = true;

        if (member.FullName == oldName && member.OrganizationId == oldOrg)
            return;

        CaseGuideEditing.RefreshIdentity(draft, member);
        if (member.FullName != oldName)
            CaseGuideEditing.FlagReferencingFacts(draft, member, oldName);
    }

    // Removes a character; the facts that cited them lose the link and need review
    private static void RemoveCastMember(CaseGuide guide, CastMember member)
    {
        guide.Cast.Remove(member);

        foreach (var fact in guide.Facts.Where(f => f != null && (f.ActorIds.Contains(member.Id) || f.KnownByIds.Contains(member.Id))))
        {
            fact.ActorIds.RemoveAll(id => id == member.Id);
            fact.KnownByIds.RemoveAll(id => id == member.Id);
            fact.NeedsReview = true;
            fact.ReviewReason = $"{member.FullName} was removed from the cast.";
        }
    }

    // Every non-individual organization: replace its revisable background staff, keeping hand-edited staff (D-13, D-19)
    private async Task RewriteBackgroundAsync(CaseDraft draft, string guidance, bool replaceEdited, GuidePartResult result,
        List<string> errors, UnitRun run, IProgress<GuideProgress>? progress)
    {
        var guide = draft.Guide!;
        var doc = draft.CoreDocument!;
        var plans = new List<(CaseOrganization Org, List<CastMember> Replace, int Count)>();

        foreach (var org in guide.Organizations.Where(o => o != null && o.Kind != OrganizationKind.Individual))
        {
            var staff = guide.Cast.Where(c => c.Tier == CastTier.Background && c.OrganizationId == org.Id).ToList();
            var kept = staff.Where(c => c.IsUserEdited && !replaceEdited).ToList();
            foreach (var member in kept)
                AddKeptNote(result.Notes, member.FullName);

            var replace = staff.Except(kept).ToList();
            var count = draft.Preset.BackgroundPerOrg - kept.Count;
            if (count > 0)
            {
                plans.Add((org, replace, count));
                continue;
            }

            // Enough hand-edited staff: the rest are removed without a call
            foreach (var member in replace)
                RemoveCastMember(guide, member);
        }

        var tasks = plans.Select((plan, i) => RunUnitAsync<BackgroundStaffDto>(new LlmRequest
        {
            Tier = LlmTier.Story,
            OperationName = "Case Guide - Background staff",
            SystemPrompt = CaseGuidePrompts.SystemPrompt,
            UserPrompt = CaseGuidePrompts.BuildBackgroundPrompt(draft, plan.Org, plan.Count, guidance),
            MaxOutputTokens = BackgroundMaxOutputTokens
        }, run, () => progress?.Report(new GuideProgress
        {
            Part = GuidePart.Cast,
            Message = $"Regenerating the cast: background staff for {plan.Org.Name} ({i + 1} of {plans.Count})...",
            Done = i,
            Total = plans.Count
        }))).ToList();

        // Throws only on a user cancel; then no organization's staff are replaced
        await Task.WhenAll(tasks);

        // Old staff are replaced only for organizations whose call succeeded; ids are allocated in organization order
        var start = doc.TimelineStart;
        var end = CaseGuidePrompts.StoryEnd(doc);
        for (var i = 0; i < tasks.Count; i++)
        {
            var unit = tasks[i].Result;
            if (unit.Refused)
                result.WasRefused = true;
            if (unit.Value == null)
            {
                if (unit.Error.Length > 0)
                    errors.Add(unit.Error);
                continue;
            }

            foreach (var member in plans[i].Replace)
                RemoveCastMember(guide, member);
            MapBackground(draft, plans[i].Org, plans[i].Count, unit.Value, start, end, result);
        }
    }

    // Key facts: every fact unit is rerun with its current facts; merges happen on this thread after all calls finish
    private async Task<GuidePartResult> RegenerateFactsAsync(CaseDraft draft, string guidance, bool replaceEdited,
        IProgress<GuideProgress>? progress, CancellationToken ct)
    {
        var guide = draft.Guide!;
        var state = guide.FactsState;
        var result = new GuidePartResult { Part = GuidePart.Facts };

        if (guide.CastState.Status != GuidePartStatus.Ready || guide.Cast.Count == 0)
        {
            result.FailureMessage = "Write the cast before the key facts.";
            return result;
        }

        var units = PlanFactUnits(draft);
        if (units.Count == 0)
        {
            result.FailureMessage = "The core document has no fact sections.";
            return result;
        }

        var existing = FactsByUnit(guide, units);
        var previous = state.Status;
        state.Status = GuidePartStatus.Generating;

        using var runCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        using var semaphore = new SemaphoreSlim(MaxParallelCalls, MaxParallelCalls);
        var run = new UnitRun(runCts, semaphore, ct, RegenerateHint("Key Facts"));

        var tasks = units.Select((unit, i) => RunUnitAsync<FactBatchDto>(new LlmRequest
        {
            Tier = LlmTier.Story,
            OperationName = $"Case Guide - Facts ({unit.Plan.Heading})",
            SystemPrompt = CaseGuidePrompts.SystemPrompt,
            UserPrompt = CaseGuidePrompts.BuildFactsPrompt(draft, unit.Plan, unit.From, unit.To, unit.Target, existing[i], guidance, replaceEdited),
            MaxOutputTokens = FactsMaxOutputTokens
        }, run, () => progress?.Report(new GuideProgress
        {
            Part = GuidePart.Facts,
            Message = $"Regenerating the key facts: {unit.Plan.Heading} ({i + 1} of {units.Count})...",
            Done = i,
            Total = units.Count
        }))).ToList();

        try
        {
            await Task.WhenAll(tasks);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // User cancel: nothing is merged
            state.Status = previous;
            throw;
        }

        // Units that failed keep their current facts
        if (MergeRegeneratedFacts(guide, units, existing, tasks, replaceEdited, result))
            draft.Revision++;

        var outcomes = tasks.Select(t => t.Result).ToList();
        result.WasRefused = outcomes.Any(o => o.Refused);

        if (run.IsFatal)
        {
            state.Status = previous;
            result.FatalError = run.FatalError;
            return result;
        }

        var errors = outcomes.Where(o => o.Value == null && o.Error.Length > 0).Select(o => o.Error).ToList();
        if (outcomes.All(o => o.Value != null))
        {
            SetReady(state);
            state.CompletedUnits.Clear();
            GuideStaleness.Stamp(draft, GuidePart.Facts);
            result.Succeeded = true;
            return result;
        }

        state.Status = previous;
        var first = errors.FirstOrDefault() ?? $"Some periods were not rewritten. {RegenerateHint("Key Facts")}";
        result.FailureMessage = errors.Distinct(StringComparer.Ordinal).Count() > 1 ? $"{errors.Count} periods were not rewritten: {first}" : first;
        return result;
    }

    // Each current fact belongs to the unit for its section (and date slice); facts from sections no longer planned are left alone
    private static List<List<KeyFact>> FactsByUnit(CaseGuide guide, List<FactUnit> units)
    {
        var lists = units.Select(_ => new List<KeyFact>()).ToList();
        foreach (var fact in guide.Facts.Where(f => f != null))
        {
            var candidates = Enumerable.Range(0, units.Count).Where(i => units[i].Plan.Id == fact.SourceSectionId).ToList();
            if (candidates.Count == 0)
                continue;

            var index = candidates.FirstOrDefault(i => InRange(fact.Date, units[i].From, units[i].To), candidates[0]);
            lists[index].Add(fact);
        }

        return lists;
    }

    private static bool InRange(string date, string from, string to) =>
        CaseDraft.TryParseDate(date, out var d) && CaseDraft.TryParseDate(from, out var f) && CaseDraft.TryParseDate(to, out var t)
        && d >= f && d <= t;

    // Merges every successful unit; new facts get F-ids in date order, removed facts are dropped from the values' links
    private static bool MergeRegeneratedFacts(CaseGuide guide, List<FactUnit> units, List<List<KeyFact>> existing, List<Task<UnitOutcome<FactBatchDto>>> tasks,
        bool replaceEdited, GuidePartResult result)
    {
        var added = new List<(KeyFact Fact, DateTime Date, int Unit, int Seq)>();
        var removed = new List<KeyFact>();
        var changed = false;

        for (var i = 0; i < tasks.Count; i++)
        {
            if (tasks[i].Result.Value is not { } batch)
                continue;

            var unit = i;
            var sectionId = units[i].Plan.Id;
            var items = (batch.Facts ?? new List<FactDto>())
                .Select(dto => (Dto: dto, Fact: MapFact(guide, dto, sectionId)))
                .Where(x => x.Fact != null)
                .Select(x => (x.Dto, Fact: x.Fact!))
                .ToList();

            removed.AddRange(MergeEntries(
                existing[i],
                items,
                replaceEdited,
                IdAllocator.Fact,
                f => f.Id,
                f => f.IsUserEdited,
                f => f.Id,
                x => x.Dto.ExistingId,
                null,
                (target, x) => CopyFact(target, x.Fact),
                x => added.Add((x.Fact, SortDate(x.Fact.Date), unit, added.Count)),
                result.Notes));
            changed = true;
        }

        foreach (var fact in removed)
        {
            guide.Facts.Remove(fact);
            foreach (var value in guide.Values.Where(v => v != null))
                value.FactIds.RemoveAll(id => id == fact.Id);
        }

        foreach (var item in added.OrderBy(a => a.Date).ThenBy(a => a.Unit).ThenBy(a => a.Seq))
        {
            item.Fact.Id = IdAllocator.Next(guide, IdAllocator.Fact);
            guide.Facts.Add(item.Fact);
        }

        if (changed)
            guide.Facts = guide.Facts.OrderBy(f => SortDate(f.Date)).ThenBy(f => IdNumber(f.Id)).ToList();

        return changed;
    }

    private static void CopyFact(KeyFact target, KeyFact source)
    {
        target.Date = source.Date;
        target.Title = source.Title;
        target.Description = source.Description;
        target.IssueIds = source.IssueIds;
        target.ActorIds = source.ActorIds;
        target.KnownByIds = source.KnownByIds;
        target.Significance = source.Significance;
        target.MappingNotes = source.MappingNotes;
        target.IsUserEdited = false;
        target.NeedsReview = false;
        target.ReviewReason = string.Empty;
    }

    // Canonical values: one call with the current values; merged only after a successful response
    private async Task<GuidePartResult> RegenerateValuesAsync(CaseDraft draft, string guidance, bool replaceEdited,
        IProgress<GuideProgress>? progress, CancellationToken ct)
    {
        var guide = draft.Guide!;
        var state = guide.ValuesState;
        var result = new GuidePartResult { Part = GuidePart.Values };

        if (guide.Facts.Count == 0)
        {
            result.FailureMessage = "Write the key facts before the canonical values.";
            return result;
        }

        var scanned = ScanValues(draft);
        var previous = state.Status;
        state.Status = GuidePartStatus.Generating;
        progress?.Report(new GuideProgress { Part = GuidePart.Values, Message = "Regenerating the canonical values..." });

        try
        {
            var dto = (await _llm.GenerateAsync<ValuesDto>(new LlmRequest
            {
                Tier = LlmTier.Story,
                OperationName = "Case Guide - Canonical values",
                SystemPrompt = CaseGuidePrompts.SystemPrompt,
                UserPrompt = CaseGuidePrompts.BuildValuesPrompt(draft, scanned, guide.Values, guidance, replaceEdited),
                MaxOutputTokens = ValuesMaxOutputTokens
            }, ct)).GetValueOrThrow();

            MergeRegeneratedValues(guide, dto, replaceEdited, result);
            SetReady(state);
            GuideStaleness.Stamp(draft, GuidePart.Values);
            draft.Revision++;
            result.Succeeded = true;
            return result;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // User cancel: nothing merged
            state.Status = previous;
            throw;
        }
        catch (OperationCanceledException)
        {
            // Not cancelled by the user: the request timed out
            result.FailureMessage = $"The request timed out. {RegenerateHint("Values")}";
        }
        catch (LlmCallException ex) when (ex.Result.IsFatal)
        {
            // Bad key or exhausted spend limit
            result.FatalError = ex.Result.Message ?? ex.Message;
        }
        catch (LlmCallException ex)
        {
            result.WasRefused = ex.Result.Outcome == LlmOutcome.Refused;
            result.FailureMessage = DescribeOutcome(ex.Result, RegenerateHint("Values"));
        }

        // Failure: the values stay as they were
        state.Status = previous;
        return result;
    }

    private static void MergeRegeneratedValues(CaseGuide guide, ValuesDto dto, bool replaceEdited, GuidePartResult result)
    {
        var items = (dto.Values ?? new List<ValueDto>())
            .Where(v => v != null)
            .Select(v => (Dto: v, Value: MapValue(guide, v.Kind, v.Label, v.Value, v.Description, v.FactIds)))
            .Where(x => x.Value != null)
            .Select(x => (x.Dto, Value: x.Value!))
            .ToList();

        var removed = MergeEntries(
            guide.Values.Where(v => v != null).ToList(),
            items,
            replaceEdited,
            IdAllocator.Value,
            v => v.Id,
            v => v.IsUserEdited,
            v => v.Label,
            x => x.Dto.ExistingId,
            (x, v) => SameValue(v, x.Value),
            (target, x) => CopyValue(target, x.Value),
            x => AddOrMergeValue(guide, x.Value),
            result.Notes);

        foreach (var value in removed)
            guide.Values.Remove(value);
    }

    private static bool SameValue(CanonicalValue a, CanonicalValue b) =>
        string.Equals(a.Label, b.Label, StringComparison.OrdinalIgnoreCase) && string.Equals(a.Value, b.Value, StringComparison.OrdinalIgnoreCase);

    private static void CopyValue(CanonicalValue target, CanonicalValue source)
    {
        target.Kind = source.Kind;
        target.Label = source.Label;
        target.Value = source.Value;
        target.Description = source.Description;
        target.FactIds = source.FactIds;
        target.MappingNotes = source.MappingNotes;
        target.IsUserEdited = false;
        target.NeedsReview = false;
        target.ReviewReason = string.Empty;
    }

    // A repeated label + value in the same response merges into the entry already written
    private static void AddOrMergeValue(CaseGuide guide, CanonicalValue value)
    {
        var duplicate = guide.Values.FirstOrDefault(v => v != null && SameValue(v, value));
        if (duplicate != null)
        {
            duplicate.FactIds = duplicate.FactIds.Union(value.FactIds).ToList();
            duplicate.MappingNotes = duplicate.MappingNotes.Union(value.MappingNotes).ToList();
            return;
        }

        value.Id = IdAllocator.Next(guide, IdAllocator.Value);
        guide.Values.Add(value);
    }

    /// <summary>
    /// Shared D-19 merge. An item whose existingId (or same-entry match) names a revisable entry not yet claimed in this response
    /// updates it in place; one naming a protected (hand-edited) entry is skipped with a note; anything else is created.
    /// Returns the revisable entries the response did not claim, for the caller to remove.
    /// </summary>
    private static List<TEntity> MergeEntries<TEntity, TItem>(IReadOnlyList<TEntity> current, IEnumerable<TItem> items, bool replaceEdited,
        string prefix, Func<TEntity, string> idOf, Func<TEntity, bool> isEdited, Func<TEntity, string> nameOf,
        Func<TItem, string?> existingIdOf, Func<TItem, TEntity, bool>? sameEntry,
        Action<TEntity, TItem> update, Action<TItem> create, List<string> notes)
        where TEntity : class
    {
        var revisable = current.Where(e => replaceEdited || !isEdited(e)).ToList();
        var kept = current.Where(e => !replaceEdited && isEdited(e)).ToList();
        var claimed = new HashSet<TEntity>(ReferenceEqualityComparer.Instance);

        foreach (var item in items)
        {
            // Ids are claimed once per response; an id used twice or an unknown id gets a new entry (T-03-38)
            var id = IdAllocator.Normalize(existingIdOf(item), prefix);
            var target = id == null ? null : revisable.FirstOrDefault(e => idOf(e) == id && !claimed.Contains(e));
            var keptEntry = id == null ? null : kept.FirstOrDefault(e => idOf(e) == id);

            if (target == null && keptEntry == null && sameEntry != null)
            {
                target = revisable.FirstOrDefault(e => !claimed.Contains(e) && sameEntry(item, e));
                keptEntry = target == null ? kept.FirstOrDefault(e => sameEntry(item, e)) : null;
            }

            if (target != null)
            {
                claimed.Add(target);
                update(target, item);
            }
            else if (keptEntry != null)
            {
                // Hand edits are never overwritten by the model (T-03-37)
                AddKeptNote(notes, nameOf(keptEntry));
            }
            else
            {
                create(item);
            }
        }

        return revisable.Where(e => !claimed.Contains(e)).ToList();
    }

    private static void AddKeptNote(List<string> notes, string name)
    {
        var note = $"Kept your edits to {name}.";
        if (!notes.Contains(note))
            notes.Add(note);
    }

    // ---------- Single-entry regeneration (GUIDE-08) ----------

    /// <summary>
    /// Rewrites one character, fact or value in place (same object, same id) with optional guidance; clears its edited flag.
    /// Failures (LlmCallException, cancellation) propagate and leave the entry untouched. Unknown ids throw ArgumentException.
    /// Applied like a hand edit: entries that depend on it are flagged for review and downstream parts stay fresh (D-20).
    /// </summary>
    public async Task RegenerateEntryAsync(CaseDraft draft, GuidePart part, string entityId, string guidance, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(draft);
        if (part is not (GuidePart.Cast or GuidePart.Facts or GuidePart.Values))
            throw new ArgumentException("Only characters, facts and values can be regenerated one at a time.", nameof(part));

        var guide = draft.Guide;
        guidance ??= string.Empty;

        switch (part)
        {
            case GuidePart.Cast:
            {
                var member = guide?.FindCast(entityId) ?? throw UnknownEntry(part, entityId);
                var dto = await GenerateEntryAsync<CastEntryDto>($"Case Guide - Character {member.Id}",
                    CaseGuidePrompts.BuildCastEntryPrompt(draft, member, guidance), ct);
                GuideStaleness.AbsorbHandEdit(draft, GuidePart.Cast, () => ApplyCastEntry(draft, member, dto));
                break;
            }

            case GuidePart.Facts:
            {
                var fact = guide?.FindFact(entityId) ?? throw UnknownEntry(part, entityId);
                var dto = await GenerateEntryAsync<FactEntryDto>($"Case Guide - Fact {fact.Id}",
                    CaseGuidePrompts.BuildFactEntryPrompt(draft, fact, guidance), ct);
                // Mapped before anything changes: an empty response leaves the fact as it was
                var mapped = MapFact(guide!, new FactDto
                {
                    Date = dto.Date,
                    Title = dto.Title,
                    Description = dto.Description,
                    IssueIds = dto.IssueIds,
                    ActorIds = dto.ActorIds,
                    KnownByIds = dto.KnownByIds,
                    Significance = dto.Significance
                }, fact.SourceSectionId) ?? throw new InvalidOperationException("The response had no fact text.");
                GuideStaleness.AbsorbHandEdit(draft, GuidePart.Facts, () =>
                {
                    CopyFact(fact, mapped);
                    CaseGuideEditing.FlagValues(guide!, fact.Id, $"{fact.Id} was changed after it was written.");
                });
                break;
            }

            default:
            {
                var value = guide?.FindValue(entityId) ?? throw UnknownEntry(part, entityId);
                var dto = await GenerateEntryAsync<ValueEntryDto>($"Case Guide - Value {value.Id}",
                    CaseGuidePrompts.BuildValueEntryPrompt(draft, value, guidance), ct);
                var mapped = MapValue(guide!, dto.Kind, dto.Label, dto.Value, dto.Description, dto.FactIds)
                    ?? throw new InvalidOperationException("The response had no value text.");
                GuideStaleness.AbsorbHandEdit(draft, GuidePart.Values, () => CopyValue(value, mapped));
                break;
            }
        }

        draft.Revision++;
    }

    private static ArgumentException UnknownEntry(GuidePart part, string? id) =>
        new($"Unknown {part} '{id}'.", nameof(id));

    // One Story call; failures throw LlmCallException (nothing is changed before this returns)
    private async Task<T> GenerateEntryAsync<T>(string operationName, string prompt, CancellationToken ct) where T : class =>
        (await _llm.GenerateAsync<T>(new LlmRequest
        {
            Tier = LlmTier.Story,
            OperationName = operationName,
            SystemPrompt = CaseGuidePrompts.SystemPrompt,
            UserPrompt = prompt,
            MaxOutputTokens = EntryMaxOutputTokens
        }, ct)).GetValueOrThrow();

    private static void ApplyCastEntry(CaseDraft draft, CastMember member, CastEntryDto dto)
    {
        // Renames go through the editing path: facts citing the character are updated and flagged for review
        var first = Clean(dto.FirstName);
        var last = Clean(dto.LastName);
        if (first.Length > 0 && first != member.FirstName)
            CaseGuideEditing.SetCastField(draft, member.Id, CastField.FirstName, first);
        if (last.Length > 0 && last != member.LastName)
            CaseGuideEditing.SetCastField(draft, member.Id, CastField.LastName, last);

        if (Clean(dto.Title).Length > 0)
            member.Title = Clean(dto.Title);
        if (Clean(dto.Summary).Length > 0)
            member.Involvement = Clean(dto.Summary);

        // Invalid or reversed dates keep the current range
        if (CaseDraft.TryParseDate(dto.ActiveFrom, out var from) && CaseDraft.TryParseDate(dto.ActiveTo, out var to) && from <= to
            && (Clean(dto.ActiveFrom) != member.ActiveFrom || Clean(dto.ActiveTo) != member.ActiveTo))
        {
            member.ActiveFrom = Clean(dto.ActiveFrom);
            member.ActiveTo = Clean(dto.ActiveTo);
            CaseGuideEditing.FlagReferencingFacts(draft, member, member.FullName);
        }

        // Background staff have no email voice or signature
        if (member.Tier == CastTier.Key)
        {
            member.VoiceNote = Clean(dto.VoiceNote);
            member.SignatureStyle = dto.SignatureStyle;
        }

        CaseGuideEditing.RefreshIdentity(draft, member);
        member.IsUserEdited = false;
        member.DetailsPending = false;
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

    private static string RetryHint(string part) => $"Click Retry {part}.";

    // Regeneration failures leave the tab as it was, so the hint names the regenerate action
    private static string RegenerateHint(string part) => $"Click Regenerate {part} to try again.";

    private static string DescribeOutcome(LlmResult result, string retryHint) => result.Outcome switch
    {
        LlmOutcome.Refused => $"The model declined: {result.Message}",
        LlmOutcome.Truncated => $"The response was cut off. {retryHint}",
        LlmOutcome.Invalid => $"The response could not be read. {retryHint}",
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

    internal class FactBatchDto
    {
        [JsonPropertyName("facts")]
        [Description("Dated facts for this period, in date order")]
        public List<FactDto> Facts { get; set; } = new();
    }

    internal class FactDto
    {
        [JsonPropertyName("existingId")]
        [Description("ID of the existing fact this revises, e.g. F-012; empty string for a new fact")]
        public string ExistingId { get; set; } = string.Empty;

        [JsonPropertyName("date")]
        [Description("YYYY-MM-DD")]
        public string Date { get; set; } = string.Empty;

        [JsonPropertyName("title")]
        [Description("Short headline, at most 12 words")]
        public string Title { get; set; } = string.Empty;

        [JsonPropertyName("description")]
        [Description("One to three sentences stating what happened as established fact: who did what, who knew, amounts")]
        public string Description { get; set; } = string.Empty;

        [JsonPropertyName("issueIds")]
        [Description("Issue IDs from the list, e.g. ISS-01")]
        public List<string> IssueIds { get; set; } = new();

        [JsonPropertyName("actorIds")]
        [Description("Cast IDs of the people who did it, e.g. CH-003")]
        public List<string> ActorIds { get; set; } = new();

        [JsonPropertyName("knownByIds")]
        [Description("Cast IDs of everyone else who knew about it at the time")]
        public List<string> KnownByIds { get; set; } = new();

        [JsonPropertyName("significance")]
        [Description("Hot, Key or Supporting")]
        public FactSignificance Significance { get; set; }
    }

    internal class ValuesDto
    {
        [JsonPropertyName("values")]
        [Description("Every amount, number and name the facts rely on")]
        public List<ValueDto> Values { get; set; } = new();
    }

    internal class ValueDto
    {
        [JsonPropertyName("existingId")]
        [Description("ID of the existing value this revises, e.g. V-004; empty string for a new value")]
        public string ExistingId { get; set; } = string.Empty;

        [JsonPropertyName("kind")]
        [Description("Amount, InvoiceNumber, ChangeOrderNumber, ContractNumber, PurchaseOrder, DocumentTitle, ProjectName, AccountNumber, Address or Other")]
        public ValueKind Kind { get; set; }

        [JsonPropertyName("label")]
        [Description("Short label, e.g. Change Order 7 amount")]
        public string Label { get; set; } = string.Empty;

        [JsonPropertyName("value")]
        [Description("Exact text a document or email would use, e.g. $184,250.00")]
        public string Value { get; set; } = string.Empty;

        [JsonPropertyName("description")]
        [Description("One sentence on what the value is")]
        public string Description { get; set; } = string.Empty;

        [JsonPropertyName("factIds")]
        [Description("Fact IDs that use this value, e.g. F-003")]
        public List<string> FactIds { get; set; } = new();
    }

    internal class CastEntryDto
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

        [JsonPropertyName("summary")]
        [Description("2-4 sentences on the person's key involvement, stated as fact; one line for background staff")]
        public string Summary { get; set; } = string.Empty;

        [JsonPropertyName("activeFrom")]
        [Description("First date active in the story, YYYY-MM-DD")]
        public string ActiveFrom { get; set; } = string.Empty;

        [JsonPropertyName("activeTo")]
        [Description("Last date active in the story, YYYY-MM-DD")]
        public string ActiveTo { get; set; } = string.Empty;

        [JsonPropertyName("voiceNote")]
        [Description("1-2 lines on how this person writes email; empty for background staff")]
        public string VoiceNote { get; set; } = string.Empty;

        [JsonPropertyName("signatureStyle")]
        [Description("Full, Short, NameOnly or MobileFooter")]
        public SignatureStyle SignatureStyle { get; set; }
    }

    internal class FactEntryDto
    {
        [JsonPropertyName("date")]
        [Description("YYYY-MM-DD")]
        public string Date { get; set; } = string.Empty;

        [JsonPropertyName("title")]
        [Description("Short headline, at most 12 words")]
        public string Title { get; set; } = string.Empty;

        [JsonPropertyName("description")]
        [Description("One to three sentences stating what happened as established fact: who did what, who knew, amounts")]
        public string Description { get; set; } = string.Empty;

        [JsonPropertyName("issueIds")]
        [Description("Issue IDs from the list, e.g. ISS-01")]
        public List<string> IssueIds { get; set; } = new();

        [JsonPropertyName("actorIds")]
        [Description("Cast IDs of the people who did it, e.g. CH-003")]
        public List<string> ActorIds { get; set; } = new();

        [JsonPropertyName("knownByIds")]
        [Description("Cast IDs of everyone else who knew about it at the time")]
        public List<string> KnownByIds { get; set; } = new();

        [JsonPropertyName("significance")]
        [Description("Hot, Key or Supporting")]
        public FactSignificance Significance { get; set; }
    }

    internal class ValueEntryDto
    {
        [JsonPropertyName("kind")]
        [Description("Amount, InvoiceNumber, ChangeOrderNumber, ContractNumber, PurchaseOrder, DocumentTitle, ProjectName, AccountNumber, Address or Other")]
        public ValueKind Kind { get; set; }

        [JsonPropertyName("label")]
        [Description("Short label, e.g. Change Order 7 amount")]
        public string Label { get; set; } = string.Empty;

        [JsonPropertyName("value")]
        [Description("Exact text a document or email would use, e.g. $184,250.00")]
        public string Value { get; set; } = string.Empty;

        [JsonPropertyName("description")]
        [Description("One sentence on what the value is")]
        public string Description { get; set; } = string.Empty;

        [JsonPropertyName("factIds")]
        [Description("Fact IDs that use this value, e.g. F-003")]
        public List<string> FactIds { get; set; } = new();
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

/// <summary>
/// Outcome of a whole-guide run (GenerateAsync): parts written and failed, a fatal stop or a refusal, and mapping notes.
/// </summary>
public class CaseGuideGenerationResult
{
    public List<GuidePart> PartsWritten { get; set; } = new();
    public List<GuidePart> PartsFailed { get; set; } = new();

    /// <summary>
    /// Non-empty when a bad key or exhausted spend limit stopped the run.
    /// </summary>
    public string FatalError { get; set; } = string.Empty;

    public bool IsFatal => FatalError.Length > 0;

    /// <summary>
    /// The part the model declined to write, when the run stopped on a refusal.
    /// </summary>
    public GuidePart? RefusedPart { get; set; }

    public string RefusalMessage { get; set; } = string.Empty;
    public string FailureMessage { get; set; } = string.Empty;
    public List<string> Notes { get; set; } = new();
}
