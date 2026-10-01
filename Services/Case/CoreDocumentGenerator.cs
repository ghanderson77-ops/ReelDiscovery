using System.ComponentModel;
using System.Globalization;
using System.Text;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using ReelDiscovery.Models.Case;
using ReelDiscovery.Services.Ai;

namespace ReelDiscovery.Services.Case;

/// <summary>
/// Writes the core document outline-first: one Story-tier outline call fixes the type, court, parties,
/// counts and timeline, then each template section is written by its own call (CASE-04..07).
/// </summary>
public class CoreDocumentGenerator
{
    public const int OutlineMaxOutputTokens = 16_000;
    public const int SectionMaxOutputTokens = 12_000;
    private const int MaxParallelSections = 3;

    // Byte-stable for prompt caching: never interpolate run data into this text
    private const string SystemPrompt = "You draft realistic United States legal documents for a fictional e-discovery training dataset. Every person, company, law firm and judge must be fictional; never use the name of a real person, company, law firm or judge. Do not cite any court decision, case name or reporter citation (for example Smith v. Jones or 531 U.S. 98). You may cite statutes, regulations and court rules by section number. Describe legal standards in plain words. Write each paragraph as plain prose without any paragraph number, letter or bullet, and never refer to other paragraphs by number. Keep one consistent court, jurisdiction and set of names throughout. For criminal or regulatory matters keep operational methods general and focus on who knew what, when, money flows and cover stories.";

    private readonly ILlmGateway _llm;

    /// <summary>
    /// Top-level response types sent as strict schemas (verified by tests).
    /// </summary>
    internal static IReadOnlyList<Type> SchemaTypes { get; } = new[] { typeof(OutlineDto), typeof(SectionDto) };

    public CoreDocumentGenerator(ILlmGateway llm)
    {
        _llm = llm;
    }

    /// <summary>
    /// One outline call; every non-success outcome throws LlmCallException (the UI shows the Refused state).
    /// </summary>
    public async Task<CoreDocument> GenerateOutlineAsync(CaseDraft draft, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(draft);

        var result = await _llm.GenerateAsync<OutlineDto>(new LlmRequest
        {
            Tier = LlmTier.Story,
            OperationName = "Core Document - Outline",
            SystemPrompt = SystemPrompt,
            UserPrompt = BuildOutlinePrompt(draft),
            MaxOutputTokens = OutlineMaxOutputTokens
        }, ct);

        return MapOutline(result.GetValueOrThrow(), draft);
    }

    /// <summary>
    /// Outline, then every section. Section failures are recorded on the section; a fatal failure stops
    /// further calls and keeps completed sections. Cancellation propagates.
    /// </summary>
    public async Task<CoreDocumentGenerationResult> GenerateAsync(CaseDraft draft, IProgress<string>? progress = null, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(draft);

        progress?.Report("Writing the outline...");
        var doc = await GenerateOutlineAsync(draft, ct);
        draft.CoreDocument = doc;
        draft.Revision++;
        progress?.Report($"Outline ready: {doc.Sections.Count} sections");

        return await GenerateSectionsAsync(draft, progress, ct);
    }

    /// <summary>
    /// Writes every Pending or Failed section that holds no hand-written text (also serves Retry and resume): lead sections in order,
    /// counts/findings in parallel, then the tail, then the executive summary last.
    /// </summary>
    public async Task<CoreDocumentGenerationResult> GenerateSectionsAsync(CaseDraft draft, IProgress<string>? progress = null, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(draft);
        var doc = draft.CoreDocument ?? throw new InvalidOperationException("Generate the outline before writing sections.");

        var result = new CoreDocumentGenerationResult();
        var preset = draft.Preset;
        // Hand-written text is never overwritten by a resume or retry (CR-02); only Regenerate Section/All replace it
        var todo = doc.Sections
            .Where(s => s.Status is SectionStatus.Pending or SectionStatus.Failed)
            .Where(s => !(s.IsUserEdited && s.Paragraphs.Count > 0))
            .ToList();
        if (todo.Count == 0)
            return result;

        var lead = todo.Where(s => IsLead(s.Kind)).ToList();
        var parallel = todo.Where(s => s.Kind is SectionKind.Count or SectionKind.Finding).ToList();
        var tail = todo.Where(s => s.Kind is SectionKind.PrayerForRelief or SectionKind.ConditionsPrecedent or SectionKind.LegalAnalysis or SectionKind.Recommendations).ToList();
        var last = todo.Where(s => s.Kind == SectionKind.ExecutiveSummary).ToList();

        using var runCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        try
        {
            // a. Lead sections, sequential in render order
            await WriteSequentialAsync(doc, preset, lead, result, progress, runCts, ct);

            // b. Counts / findings in parallel; each sees the lead sections
            if (!result.IsFatal && parallel.Count > 0)
            {
                ct.ThrowIfCancellationRequested();
                var leadText = FormatSections(doc.Sections.Where(s => IsLead(s.Kind) && s.Status == SectionStatus.Ready));

                using var semaphore = new SemaphoreSlim(MaxParallelSections, MaxParallelSections);
                var tasks = parallel.Select(async section =>
                {
                    await semaphore.WaitAsync(ct);
                    try
                    {
                        // A fatal error in another call stopped the run: leave this section Pending
                        if (runCts.IsCancellationRequested)
                        {
                            ct.ThrowIfCancellationRequested();
                            return;
                        }

                        progress?.Report(DescribeProgress(doc, section));
                        await WriteOneAsync(doc, preset, section, leadText, result, runCts, ct);
                    }
                    finally
                    {
                        semaphore.Release();
                    }
                }).ToList();

                await Task.WhenAll(tasks);
            }

            // c. Tail, then d. executive summary (it summarizes the findings)
            await WriteSequentialAsync(doc, preset, tail, result, progress, runCts, ct);
            await WriteSequentialAsync(doc, preset, last, result, progress, runCts, ct);
        }
        finally
        {
            // I1: Phase 3 stale-marking sees retried/resumed sections as a change (also on fatal stop or cancel)
            if (result.SectionsWritten > 0)
                draft.Revision++;
        }

        return result;
    }

    /// <summary>
    /// Rewrites one section in place (same object, same Id), using every other section's current text
    /// (including user edits) and optional guidance. Failures propagate and leave the old text untouched.
    /// </summary>
    public async Task RegenerateSectionAsync(CaseDraft draft, string sectionId, string guidance, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(draft);

        var doc = draft.CoreDocument;
        var section = doc?.FindSection(sectionId);
        if (doc == null || section == null)
            throw new ArgumentException($"Unknown section '{sectionId}'.", nameof(sectionId));

        var otherText = FormatSections(doc.Sections.Where(s => !ReferenceEquals(s, section) && s.Paragraphs.Count > 0));
        var currentText = string.Join("\n\n", section.Paragraphs);

        var paragraphs = await WriteSectionAsync(doc, draft.Preset, section, otherText, currentText, guidance ?? string.Empty, ct);

        section.Paragraphs = paragraphs;
        section.Status = SectionStatus.Ready;
        section.FailureMessage = string.Empty;
        section.IsUserEdited = false;
        draft.Revision++;
    }

    private static bool IsLead(SectionKind kind) => kind is not (SectionKind.Count or SectionKind.Finding
        or SectionKind.PrayerForRelief or SectionKind.ConditionsPrecedent or SectionKind.ExecutiveSummary
        or SectionKind.LegalAnalysis or SectionKind.Recommendations);

    private async Task WriteSequentialAsync(CoreDocument doc, ComplexityPreset preset, List<CoreDocumentSection> sections,
        CoreDocumentGenerationResult result, IProgress<string>? progress, CancellationTokenSource runCts, CancellationToken ct)
    {
        foreach (var section in sections)
        {
            ct.ThrowIfCancellationRequested();

            // Fatal error recorded: stop, remaining sections stay Pending
            if (result.IsFatal)
                return;

            progress?.Report(DescribeProgress(doc, section));
            var otherText = FormatSections(doc.Sections.Where(s => !ReferenceEquals(s, section) && s.Status == SectionStatus.Ready));
            await WriteOneAsync(doc, preset, section, otherText, result, runCts, ct);
        }
    }

    private async Task WriteOneAsync(CoreDocument doc, ComplexityPreset preset, CoreDocumentSection section, string otherText,
        CoreDocumentGenerationResult result, CancellationTokenSource runCts, CancellationToken ct)
    {
        section.Status = SectionStatus.Writing;
        try
        {
            var paragraphs = await WriteSectionAsync(doc, preset, section, otherText, string.Empty, string.Empty, runCts.Token);

            section.Paragraphs = paragraphs;
            section.Status = SectionStatus.Ready;
            section.FailureMessage = string.Empty;
            section.IsUserEdited = false;
            lock (result)
                result.SectionsWritten++;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // User cancel: nothing written for this section
            section.Status = SectionStatus.Pending;
            throw;
        }
        catch (OperationCanceledException) when (runCts.IsCancellationRequested)
        {
            // Fatal stop from another call
            section.Status = SectionStatus.Pending;
        }
        catch (OperationCanceledException)
        {
            // Run not cancelled: the request timed out
            MarkFailed(section, result, "The request timed out. Click Retry Section.");
        }
        catch (LlmCallException ex) when (ex.Result.IsFatal)
        {
            // Bad key or exhausted spend limit: stop all further calls, keep completed sections
            section.Status = SectionStatus.Pending;
            lock (result)
            {
                if (!result.IsFatal)
                    result.FatalError = ex.Result.Message ?? ex.Message;
            }

            runCts.Cancel();
        }
        catch (LlmCallException ex)
        {
            // Refused / truncated / invalid / non-fatal failure: mark this section, keep going
            MarkFailed(section, result, DescribeOutcome(ex.Result));
        }
        catch (Exception ex)
        {
            MarkFailed(section, result, ex.Message);
        }
    }

    private static void MarkFailed(CoreDocumentSection section, CoreDocumentGenerationResult result, string message)
    {
        section.Status = SectionStatus.Failed;
        section.FailureMessage = message;
        lock (result)
            result.SectionsFailed++;
    }

    private static string DescribeOutcome(LlmResult result) => result.Outcome switch
    {
        LlmOutcome.Refused => $"The model declined: {result.Message}",
        LlmOutcome.Truncated => "The response was cut off. Click Retry Section.",
        LlmOutcome.Invalid => "The response could not be read. Click Retry Section.",
        _ => $"{result.FailureKind}: {result.Message}"
    };

    private static string DescribeProgress(CoreDocument doc, CoreDocumentSection section)
    {
        switch (section.Kind)
        {
            case SectionKind.FactualAllegations:
            {
                var facts = doc.Sections.Where(s => s.Kind == SectionKind.FactualAllegations).ToList();
                return $"Writing {section.Heading} ({facts.IndexOf(section) + 1} of {facts.Count} fact sections)...";
            }
            case SectionKind.Count:
                return $"Writing Count {CoreDocumentAssembler.ToRoman(CountNumber(doc, section))} of {CoreDocumentAssembler.ToRoman(doc.Counts.Count)}...";
            case SectionKind.Finding:
                return $"Writing Finding {CountNumber(doc, section)} of {doc.Counts.Count}...";
            default:
                return $"Writing {section.Heading}...";
        }
    }

    private static int CountNumber(CoreDocument doc, CoreDocumentSection section)
    {
        var count = doc.Counts.FirstOrDefault(c => c.Id == section.CountId);
        return count?.Number ?? Math.Max(1, doc.Sections.Where(s => s.Kind == section.Kind).ToList().IndexOf(section) + 1);
    }

    private async Task<List<string>> WriteSectionAsync(CoreDocument doc, ComplexityPreset preset, CoreDocumentSection section,
        string otherSectionsText, string currentText, string guidance, CancellationToken ct)
    {
        var prompt = BuildSectionPrompt(doc, preset, section, otherSectionsText, currentText, guidance);
        var request = new LlmRequest
        {
            Tier = LlmTier.Story,
            OperationName = $"Core Document - {section.Heading}",
            SystemPrompt = SystemPrompt,
            UserPrompt = prompt,
            MaxOutputTokens = SectionMaxOutputTokens
        };

        var paragraphs = CleanParagraphs((await _llm.GenerateAsync<SectionDto>(request, ct)).GetValueOrThrow());

        // Content-level retry (not a transport retry): exactly one rewrite when case law was cited (T-02-22)
        List<CitationFinding> findings;
        try
        {
            findings = CitationScanner.Scan(string.Join("\n", paragraphs), OwnPartyNames(doc));
        }
        catch (RegexMatchTimeoutException)
        {
            // Too large to scan: keep the text; the validator reports the section (WR-04)
            findings = new List<CitationFinding>();
        }

        if (CitationScanner.HasErrors(findings))
        {
            var errors = findings.Where(f => f.Severity == CitationSeverity.Error).Select(f => f.Text);
            request.UserPrompt = prompt + "\n\nYour draft cited court decisions, which is not allowed. Rewrite the section without these citations and without any other case names or reporter citations: " + string.Join("; ", errors);

            // Whatever comes back is kept; remaining findings are reported by the validator
            paragraphs = CleanParagraphs((await _llm.GenerateAsync<SectionDto>(request, ct)).GetValueOrThrow());
        }

        return paragraphs;
    }

    private static List<string> CleanParagraphs(SectionDto dto) =>
        (dto.Paragraphs ?? new List<string>())
            .Select(p => CoreDocumentAssembler.StripLeadingNumber(p ?? string.Empty).Trim())
            .Where(p => p.Length > 0)
            .ToList();

    private static List<string> OwnPartyNames(CoreDocument doc) =>
        doc.Parties
            .SelectMany(p => new[] { p.Name, p.ShortName })
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .ToList();

    private static string FormatSections(IEnumerable<CoreDocumentSection> sections)
    {
        var blocks = sections
            .Where(s => s.Paragraphs.Count > 0)
            .Select(s => $"== {s.Heading} ==\n" + string.Join("\n\n", s.Paragraphs))
            .ToList();

        return blocks.Count == 0 ? "(nothing written yet)" : string.Join("\n\n", blocks);
    }

    internal static string BuildSectionPrompt(CoreDocument doc, ComplexityPreset preset, CoreDocumentSection section,
        string otherSectionsText, string currentText, string guidance)
    {
        var sb = new StringBuilder();

        sb.AppendLine($"Document type: {doc.DocumentType}");
        sb.AppendLine($"Court: {doc.Jurisdiction.CourtName} ({doc.Jurisdiction.District})");
        sb.AppendLine($"Title: {doc.Title}");
        sb.AppendLine($"Timeline: {doc.TimelineStart} to {doc.TimelineEnd}");

        sb.AppendLine("Parties:");
        foreach (var party in doc.Parties)
            sb.AppendLine($"- {party.Name} ({party.ShortName}), {party.Role}, {party.EntityType}, {party.Domicile}: {party.Description}");

        sb.AppendLine("Counts or issues:");
        foreach (var count in doc.Counts)
            sb.AppendLine($"- {count.Number}. {count.Title} against {AgainstNames(doc, count)}: {count.LegalBasis}. {count.Summary}");

        sb.AppendLine("Factual subsections:");
        foreach (var fact in doc.FactPlan)
            sb.AppendLine($"- {fact.Heading} ({fact.StartDate} to {fact.EndDate}): {fact.Focus}");

        if (doc.DocumentType == CoreDocumentType.InvestigationMemo)
            sb.AppendLine($"Memo: TO {doc.Memo.To}; FROM {doc.Memo.From}; RE {doc.Memo.Re}; organization {doc.Memo.Organization}");

        sb.AppendLine();
        sb.AppendLine("Document so far:");
        sb.AppendLine(string.IsNullOrWhiteSpace(otherSectionsText) ? "(nothing written yet)" : otherSectionsText);
        sb.AppendLine();

        var target = CoreDocumentTemplates.GetSectionSpecs(doc).FirstOrDefault(s => s.Id == section.Id)?.TargetParagraphs ?? 3;
        sb.AppendLine($"Section: {section.Heading}");
        sb.AppendLine($"Write about {target} paragraphs.");
        sb.Append(SectionInstructions(section, doc, preset));

        if (!string.IsNullOrWhiteSpace(currentText))
        {
            sb.AppendLine();
            sb.AppendLine();
            sb.AppendLine("Current text of this section (rewrite it):");
            sb.Append(currentText);
        }

        if (!string.IsNullOrWhiteSpace(guidance))
        {
            sb.AppendLine();
            sb.AppendLine();
            sb.Append("Guidance from the user: " + guidance.Trim());
        }

        return sb.ToString();
    }

    private static string AgainstNames(CoreDocument doc, CaseCount count)
    {
        var names = doc.Parties
            .Where(p => count.AgainstPartyIds.Contains(p.Id))
            .Select(p => p.ShortName.Length > 0 ? p.ShortName : p.Name)
            .ToList();

        return names.Count == 0 ? "the opposing parties" : string.Join(", ", names);
    }

    private static string CountDetail(CoreDocument doc, CoreDocumentSection section)
    {
        var count = doc.Counts.FirstOrDefault(c => c.Id == section.CountId);
        return count == null
            ? string.Empty
            : $" This issue: {count.Title} against {AgainstNames(doc, count)}; basis: {count.LegalBasis}. {count.Summary}";
    }

    private static string SectionInstructions(CoreDocumentSection s, CoreDocument doc, ComplexityPreset preset)
    {
        // W3: the preset's key-character count must show in the document; Phase 3 extracts the cast from these sections
        var characters = $"{preset.CharactersMin}-{preset.CharactersMax}";

        switch (s.Kind)
        {
            case SectionKind.NatureOfAction:
                return "Summarize who the parties are, what went wrong and the relief sought, in plain words.";
            case SectionKind.Parties:
                return "Write one paragraph per party giving its full name, entity type, domicile and role. " +
                    $"After the party paragraphs, identify the key individuals at each party by name and role; across the whole document name about {characters} named individuals.";
            case SectionKind.JurisdictionVenue:
                return $"State subject-matter jurisdiction and venue for {doc.Jurisdiction.CourtName} using statutes only.";
            case SectionKind.DiscoveryControlPlan:
                return "State the discovery control plan level under which discovery is intended to be conducted, citing the court rule by number.";
            case SectionKind.ClaimForRelief:
                return "State the range of monetary relief sought, as the court rules require.";
            case SectionKind.FactualAllegations:
            {
                var plan = doc.FactPlan.FirstOrDefault(f => f.Id == s.Id);
                var period = plan == null ? string.Empty : $" Period: {plan.StartDate} to {plan.EndDate}. Focus: {plan.Focus}";
                return "Write dated factual allegations for this period only, naming who said or did what, when, and the amounts involved." + period +
                    $" Use the named individuals already introduced; the whole document should name about {characters} named individuals.";
            }
            case SectionKind.Count:
                return "Apply the elements of this cause of action to the facts, then state the damages or relief. Do not write a paragraph that repeats or incorporates earlier paragraphs; it is added automatically." +
                    CountDetail(doc, s);
            case SectionKind.ConditionsPrecedent:
                return "State that all conditions precedent to the claims have been performed, have occurred or have been waived.";
            case SectionKind.PrayerForRelief:
                return "Write each item of relief requested as its own paragraph, without letters.";
            case SectionKind.ExecutiveSummary:
                return "Summarize the findings and the overall conclusion.";
            case SectionKind.Background:
                return "Describe the organizations involved, the business context and how the matter came to light.";
            case SectionKind.ScopeMethodology:
                return "Describe the review period, the sources examined (documents, email, financial records, interviews) and any limitations.";
            case SectionKind.KeyIndividuals:
                return $"Describe about {characters} named individuals (officers, employees, counsel, agents, associates) and the entities, with each person's role and what they knew.";
            case SectionKind.Finding:
                return "State the finding for this issue, the supporting evidence by date and person, and the confidence level." + CountDetail(doc, s);
            case SectionKind.LegalAnalysis:
                return "Analyze the exposure under the statutes and regulations involved, in plain words.";
            case SectionKind.Recommendations:
                return "State the conclusions and concrete recommended next steps.";
            default:
                return "Write this section in the style of the rest of the document.";
        }
    }

    internal static string BuildOutlinePrompt(CaseDraft draft)
    {
        var p = draft.Preset;
        var sb = new StringBuilder();

        sb.AppendLine("Scenario:");
        sb.AppendLine(draft.EffectiveScenario);
        sb.AppendLine();
        sb.AppendLine($"Complexity preset: {draft.Complexity}");
        // Always print both bounds, even when equal (e.g. "2-2")
        sb.AppendLine($"- Parties: {p.PartiesMin}-{p.PartiesMax}");
        sb.AppendLine($"- Counts or issues: {p.CountsMin}-{p.CountsMax}");
        sb.AppendLine($"- Key characters: {p.CharactersMin}-{p.CharactersMax}");
        sb.AppendLine($"- Timeline span (months): {p.TimelineMonthsMin}-{p.TimelineMonthsMax}");
        sb.AppendLine($"- Factual subsections: {p.FactSubsectionsMin}-{p.FactSubsectionsMax}");
        sb.AppendLine($"- Numbered paragraphs (whole document): {p.ParagraphsMin}-{p.ParagraphsMax}");
        sb.AppendLine($"- Memo findings if this is an investigation memo: {p.FindingsMin}-{p.FindingsMax}");
        // W4: same range the validator checks
        sb.AppendLine($"- Memo length if this is an investigation memo: {p.MemoPagesMin}-{p.MemoPagesMax} pages, about {p.MemoParagraphsMin}-{p.MemoParagraphsMax} paragraphs in total");
        sb.AppendLine();

        sb.AppendLine(draft.DocumentPreference == DocumentTypePreference.Auto
            ? "Document type: choose (Complaint or Petition for a civil dispute, Investigation Memo for a criminal or regulatory matter)"
            : $"Document type: {DescribeDocumentPreference(draft.DocumentPreference)}");
        sb.AppendLine(draft.CourtPreference == CourtPreference.Auto
            ? "Court system: choose"
            : $"Court system: {draft.CourtPreference} (required: the court name must belong to this court system)");
        sb.AppendLine(string.IsNullOrWhiteSpace(draft.StatePreference)
            ? "State: choose"
            : $"State: {draft.StatePreference.Trim()} (required: use a court in this state)");

        var applied = draft.Substitutions
            .Where(s => s.Apply && !string.IsNullOrWhiteSpace(s.Original) && !string.IsNullOrWhiteSpace(s.Substitute))
            .ToList();
        if (applied.Count > 0)
        {
            sb.AppendLine($"Use these fictional names exactly: {string.Join(", ", applied.Select(s => s.Substitute.Trim()))}");
            sb.AppendLine($"Never use these real names: {string.Join(", ", applied.Select(s => s.Original.Trim()))}");
        }

        // CASE-03 override (WR-01): names the user confirmed keeping. Stated here, not in the byte-stable system prompt.
        var kept = draft.Substitutions
            .Where(s => !s.Apply && !string.IsNullOrWhiteSpace(s.Original))
            .Select(s => s.Original.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToList();
        if (kept.Count > 0)
            sb.AppendLine($"The user has approved keeping these names exactly as written; use them unchanged as an exception to the fictional-names rule: {string.Join(", ", kept)}");

        sb.AppendLine();
        sb.Append("Plan the document: classify the matter, choose one court and venue, list the parties with fictional names, the counts or issues, the timeline (filing or memo date after the timeline ends), the factual subsections in date order with a paragraph target each, and fictional counsel or memo author details.");

        return sb.ToString();
    }

    private static string DescribeDocumentPreference(DocumentTypePreference preference) => preference switch
    {
        DocumentTypePreference.InvestigationMemo => "Investigation Memo",
        _ => preference.ToString()
    };

    private static CoreDocument MapOutline(OutlineDto dto, CaseDraft draft)
    {
        var partyDtos = dto.Parties ?? new List<PartyDto>();
        if (partyDtos.Count == 0)
            throw new InvalidOperationException("The outline listed no parties.");

        var jurisdiction = new Jurisdiction
        {
            CourtSystem = dto.CourtSystem,
            State = Clean(dto.State),
            CourtName = Clean(dto.CourtName),
            District = Clean(dto.District),
            VenueCity = Clean(dto.VenueCity)
        };

        // The user's court/state preferences win. CourtName is deliberately NOT rewritten: if the model
        // ignored the required court system or state, the contradiction is left for CoreDocumentValidator
        // (Federal with a non-"United States" court, or State with a "United States" court, is an Error that
        // blocks Finish until the user regenerates; a state court naming a different state is a Warning).
        if (draft.CourtPreference == CourtPreference.Federal)
            jurisdiction.CourtSystem = CourtSystem.Federal;
        else if (draft.CourtPreference == CourtPreference.State)
            jurisdiction.CourtSystem = CourtSystem.State;

        if (!string.IsNullOrWhiteSpace(draft.StatePreference))
            jurisdiction.State = draft.StatePreference.Trim();

        var doc = new CoreDocument
        {
            MatterKind = dto.MatterKind,
            MemoPerspective = dto.MemoPerspective,
            Jurisdiction = jurisdiction,
            UsesPetitionTerminology = dto.UsesPetitionTerminology,
            DemandsJuryTrial = dto.DemandsJuryTrial,
            Title = Clean(dto.Title),
            FilingDate = Clean(dto.FilingDate),
            TimelineStart = Clean(dto.TimelineStart),
            TimelineEnd = Clean(dto.TimelineEnd)
        };

        doc.DocumentType = DocumentTypeResolver.Resolve(dto.MatterKind, draft.DocumentPreference, jurisdiction.CourtSystem, dto.UsesPetitionTerminology);

        for (var i = 0; i < partyDtos.Count; i++)
        {
            var party = partyDtos[i];
            doc.Parties.Add(new CaseParty
            {
                Id = $"party-{i + 1}",
                Name = Clean(party.Name),
                ShortName = Clean(party.ShortName),
                Role = party.Role,
                EntityType = Clean(party.EntityType),
                Domicile = Clean(party.Domicile),
                Description = Clean(party.Description)
            });
        }

        var countDtos = dto.Counts ?? new List<CountDto>();
        for (var i = 0; i < countDtos.Count; i++)
        {
            var count = countDtos[i];
            var against = (count.AgainstParties ?? new List<string>()).Select(Clean).Where(a => a.Length > 0).ToList();

            doc.Counts.Add(new CaseCount
            {
                Id = $"count-{i + 1}",
                Number = i + 1,
                Title = Clean(count.Title),
                // Unresolved names are dropped
                AgainstPartyIds = doc.Parties
                    .Where(p => against.Any(a =>
                        string.Equals(a, p.ShortName, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(a, p.Name, StringComparison.OrdinalIgnoreCase)))
                    .Select(p => p.Id)
                    .ToList(),
                LegalBasis = Clean(count.LegalBasis),
                Summary = Clean(count.Summary)
            });
        }

        var factDtos = dto.FactSubsections ?? new List<FactSubsectionDto>();
        for (var i = 0; i < factDtos.Count; i++)
        {
            var fact = factDtos[i];
            doc.FactPlan.Add(new FactSubsectionPlan
            {
                Id = $"facts-{i + 1}",
                Heading = Clean(fact.Heading),
                StartDate = Clean(fact.StartDate),
                EndDate = Clean(fact.EndDate),
                TargetParagraphs = Math.Clamp(fact.TargetParagraphs, 3, CoreDocumentTemplates.MaxParagraphsPerSection),
                Focus = Clean(fact.Focus)
            });
        }

        var counsel = dto.Counsel ?? new CounselDto();
        doc.Counsel = new CounselInfo
        {
            AttorneyName = Clean(counsel.AttorneyName),
            FirmName = Clean(counsel.FirmName),
            BarNumber = Clean(counsel.BarNumber),
            Address = Clean(counsel.Address),
            // Fictional 555-01xx range reserved for fiction; never a real number (Pitfall 12)
            Phone = $"(555) 555-01{Random.Shared.Next(0, 100).ToString("D2", CultureInfo.InvariantCulture)}",
            Email = Clean(counsel.Email)
        };

        var memo = dto.Memo ?? new MemoDto();
        doc.Memo = new MemoHeader
        {
            To = Clean(memo.To),
            From = Clean(memo.From),
            Cc = Clean(memo.Cc),
            Date = Clean(memo.Date),
            Re = Clean(memo.Re),
            Organization = Clean(memo.Organization)
        };

        doc.Sections = CoreDocumentTemplates.GetSectionSpecs(doc)
            .Select(s => new CoreDocumentSection
            {
                Id = s.Id,
                Kind = s.Kind,
                Heading = s.Heading,
                CountId = s.CountId,
                Status = SectionStatus.Pending
            })
            .ToList();

        doc.SourceHash = draft.ComputeSourceHash();
        return doc;
    }

    private static string Clean(string value) => (value ?? string.Empty).Trim();

    // API Response DTOs (strict schemas: no nullable properties, dates as YYYY-MM-DD strings)
    internal class OutlineDto
    {
        [JsonPropertyName("matterKind")]
        [Description("Civil, Criminal or Regulatory")]
        public MatterKind MatterKind { get; set; }

        [JsonPropertyName("courtSystem")]
        [Description("Federal or State")]
        public CourtSystem CourtSystem { get; set; }

        [JsonPropertyName("state")]
        [Description("Full US state name, e.g. Illinois")]
        public string State { get; set; } = string.Empty;

        [JsonPropertyName("courtName")]
        [Description("Full court name, e.g. United States District Court for the Northern District of Illinois")]
        public string CourtName { get; set; } = string.Empty;

        [JsonPropertyName("district")]
        [Description("Federal district or state county")]
        public string District { get; set; } = string.Empty;

        [JsonPropertyName("venueCity")]
        [Description("City where the court sits")]
        public string VenueCity { get; set; } = string.Empty;

        [JsonPropertyName("usesPetitionTerminology")]
        [Description("True only for a state court whose pleadings are called petitions, e.g. Texas")]
        public bool UsesPetitionTerminology { get; set; }

        [JsonPropertyName("memoPerspective")]
        [Description("InternalCorporate or Government; who wrote the memo if this is an investigation memo")]
        public MemoPerspective MemoPerspective { get; set; }

        [JsonPropertyName("title")]
        [Description("Document title, e.g. Complaint for Breach of Contract and Fraud, or the memo subject")]
        public string Title { get; set; } = string.Empty;

        [JsonPropertyName("filingDate")]
        [Description("Filing or memo date, YYYY-MM-DD, after the timeline ends")]
        public string FilingDate { get; set; } = string.Empty;

        [JsonPropertyName("timelineStart")]
        [Description("First relevant event, YYYY-MM-DD")]
        public string TimelineStart { get; set; } = string.Empty;

        [JsonPropertyName("timelineEnd")]
        [Description("Last relevant event, YYYY-MM-DD")]
        public string TimelineEnd { get; set; } = string.Empty;

        [JsonPropertyName("demandsJuryTrial")]
        [Description("True when the pleading demands a jury trial")]
        public bool DemandsJuryTrial { get; set; }

        [JsonPropertyName("parties")]
        [Description("Parties with fictional names")]
        public List<PartyDto> Parties { get; set; } = new();

        [JsonPropertyName("counts")]
        [Description("Causes of action for a pleading, or issues/findings for a memo")]
        public List<CountDto> Counts { get; set; } = new();

        [JsonPropertyName("factSubsections")]
        [Description("Factual subsections in date order")]
        public List<FactSubsectionDto> FactSubsections { get; set; } = new();

        [JsonPropertyName("counsel")]
        [Description("Fictional counsel signing a pleading; empty strings for a memo")]
        public CounselDto Counsel { get; set; } = new();

        [JsonPropertyName("memo")]
        [Description("Memo header; empty strings for a pleading")]
        public MemoDto Memo { get; set; } = new();
    }

    internal class PartyDto
    {
        [JsonPropertyName("name")]
        [Description("Full fictional legal name, e.g. Brightline Glazing, LLC")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("shortName")]
        [Description("Short name used in the text, e.g. Brightline")]
        public string ShortName { get; set; } = string.Empty;

        [JsonPropertyName("role")]
        [Description("Plaintiff, Defendant, Subject, Complainant or ThirdParty")]
        public PartyRole Role { get; set; }

        [JsonPropertyName("entityType")]
        [Description("e.g. Delaware corporation, Illinois limited liability company, individual")]
        public string EntityType { get; set; } = string.Empty;

        [JsonPropertyName("domicile")]
        [Description("City and state of residence or principal place of business")]
        public string Domicile { get; set; } = string.Empty;

        [JsonPropertyName("description")]
        [Description("One sentence on the party's role in the matter")]
        public string Description { get; set; } = string.Empty;
    }

    internal class CountDto
    {
        [JsonPropertyName("title")]
        [Description("Count or issue title, e.g. Breach of Contract")]
        public string Title { get; set; } = string.Empty;

        [JsonPropertyName("againstParties")]
        [Description("Short names of the parties this count is against")]
        public List<string> AgainstParties { get; set; } = new();

        [JsonPropertyName("legalBasis")]
        [Description("Statute, regulation or common-law theory; never a case citation")]
        public string LegalBasis { get; set; } = string.Empty;

        [JsonPropertyName("summary")]
        [Description("One or two sentences on the theory of this count")]
        public string Summary { get; set; } = string.Empty;
    }

    internal class FactSubsectionDto
    {
        [JsonPropertyName("heading")]
        [Description("Subsection heading")]
        public string Heading { get; set; } = string.Empty;

        [JsonPropertyName("startDate")]
        [Description("YYYY-MM-DD")]
        public string StartDate { get; set; } = string.Empty;

        [JsonPropertyName("endDate")]
        [Description("YYYY-MM-DD")]
        public string EndDate { get; set; } = string.Empty;

        [JsonPropertyName("targetParagraphs")]
        [Description("Number of paragraphs to write for this subsection")]
        public int TargetParagraphs { get; set; }

        [JsonPropertyName("focus")]
        [Description("What happened in this period")]
        public string Focus { get; set; } = string.Empty;
    }

    internal class CounselDto
    {
        [JsonPropertyName("attorneyName")]
        [Description("Fictional attorney name")]
        public string AttorneyName { get; set; } = string.Empty;

        [JsonPropertyName("firmName")]
        [Description("Fictional law firm name")]
        public string FirmName { get; set; } = string.Empty;

        [JsonPropertyName("barNumber")]
        [Description("Fictional bar number")]
        public string BarNumber { get; set; } = string.Empty;

        [JsonPropertyName("address")]
        [Description("Firm street address, city, state and ZIP")]
        public string Address { get; set; } = string.Empty;

        [JsonPropertyName("email")]
        [Description("Attorney email at an .example domain")]
        public string Email { get; set; } = string.Empty;
    }

    internal class MemoDto
    {
        [JsonPropertyName("to")]
        [Description("Recipient name and title")]
        public string To { get; set; } = string.Empty;

        [JsonPropertyName("from")]
        [Description("Author name and title")]
        public string From { get; set; } = string.Empty;

        [JsonPropertyName("cc")]
        [Description("Empty string when none")]
        public string Cc { get; set; } = string.Empty;

        [JsonPropertyName("date")]
        [Description("Memo date, YYYY-MM-DD")]
        public string Date { get; set; } = string.Empty;

        [JsonPropertyName("re")]
        [Description("Subject line")]
        public string Re { get; set; } = string.Empty;

        [JsonPropertyName("organization")]
        [Description("Organization issuing the memo")]
        public string Organization { get; set; } = string.Empty;
    }

    internal class SectionDto
    {
        [JsonPropertyName("paragraphs")]
        [Description("Paragraphs in order, plain prose, no numbers or letters")]
        public List<string> Paragraphs { get; set; } = new();
    }
}

public class CoreDocumentGenerationResult
{
    public int SectionsWritten { get; set; }
    public int SectionsFailed { get; set; }

    /// <summary>
    /// Non-empty when a fatal failure (bad key or spend limit) stopped further calls.
    /// </summary>
    public string FatalError { get; set; } = string.Empty;

    public bool IsFatal => FatalError.Length > 0;
}
