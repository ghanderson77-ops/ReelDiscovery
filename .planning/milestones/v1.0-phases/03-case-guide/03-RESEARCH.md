# Phase 3: Case Guide - Research

**Researched:** 2026-10-01
**Domain:** Structured LLM extraction into an ID-stable domain model, deterministic validation, file persistence (case.json + autosave), WinForms data-entry UI at 150% scaling, OpenXml .docx export
**Confidence:** HIGH for codebase integration, gateway limits and persistence; MEDIUM for generation decomposition sizes and preset numbers (tunable)

## Summary

Phase 3 is **mostly application code on top of what Phase 2 built**. No new NuGet packages are needed. The gateway (`ILlmGateway.GenerateAsync<T>`, Story tier, strict JSON-schema DTOs) and the Phase 2 patterns carry over directly: a static validator that never throws, outline-first / batched generation with per-part status, `FakeLlmProvider` tests, and `CaseDraft.JsonOptions` with string enums. The core document already holds structured data the guide should reuse rather than re-ask the model for: `CoreDocument.Counts` (`count-N`, the issue source), `Parties` (`party-N`, the organization source), `FactPlan` (`facts-N`, with date ranges, which is the natural batching unit for facts), `TimelineStart/End/FilingDate` (validator range), and `Counsel` (the signing attorney who must appear in the cast).

The central design rule: **code owns every ID, every cross-reference, every email address and every date check. The model only fills content.** IDs (`ISS-01`, `ORG-01`, `CH-001`, `F-001`, `V-001`) come from a persisted allocator that never reuses a number. The model refers to existing entities by the IDs listed in its prompt, and code checks each reference before accepting it. On regeneration the model returns an `existingId` per entry, and code keeps that ID only if it is valid and unused. Hand-edited entries (`IsUserEdited`) are passed as fixed context and never overwritten without confirmation. Email identities are derived deterministically from organization names (D-14), so they can never drift.

Generation must be split into small calls. Anthropic non-streaming output is capped at 21,333 tokens including Opus thinking [VERIFIED: `AnthropicTextProvider.MaxNonStreamingTokens`]. Anthropic strict schemas allow at most 16 union-typed and 24 optional parameters per request [CITED: platform.claude.com/docs/en/build-with-claude/structured-outputs]. The split is: issues (deterministic from counts, plus one enrichment call), then cast (one roster call, batched detail calls, one background-staff call per organization), then facts (one call per `FactPlan` section, at most 3 in parallel), then canonical values (one call plus a deterministic scanner). Persistence is a per-case folder under `Documents\ReelDiscovery\Cases\`. On this machine that folder is **OneDrive-redirected** [VERIFIED: `C:\Users\ghand\OneDrive\Documents`], so atomic writes need temp-file + `File.Move(overwrite)` with retry on transient sharing violations. The existing `OfficeDocumentService.CreateWordDocument` supports only title + plain paragraphs and has a pinned schema-order violation, so the exports need a small new OpenXml builder with headings and tables, validated to **zero** `OpenXmlValidator` errors.

**Primary recommendation:** Add a `CaseGuide` model to `CaseDraft`, plus these pieces. Pure static services: `CaseGuideValidator`, `GuideStaleness`, `EmailIdentity`, `IdAllocator`, `CaseFolderNaming`. One gateway-backed `CaseGuideGenerator` with per-part entry points (issues / cast / facts / values / single entry). A `CaseStore` for atomic case.json and recent-cases I/O, and a `CaseGuideDocxExporter`. Then two new wizard steps: `StepCaseGuide` (segmented tabs, grid + detail panel) and `StepCaseSummary`. Build and test the model, validator and store before any UI.

## User Constraints

<user_constraints>
## User Constraints (from CONTEXT.md)

### Locked Decisions

#### Carried forward from Phase 2 UAT (locked, user feedback 2026-10-01)
- **D-01:** Names the scenario gives are kept exactly, **including names from fictional works** (TV, film, books, games — e.g. Dunder Mifflin, Michael Scott). Only real-world people/companies/brands are substituted (per the Phase 2 real-name dialog decisions stored on `CaseDraft.Substitutions`). Every Phase 3 generation prompt (cast, facts, values) must say this explicitly; never instruct the model that "every person/company must be fictional". Invent names only for people/orgs the core document does not name.
- **D-02:** Generated content takes a **firm position**: facts are stated as what happened (who did what, who knew what and when, where the money went), not hedged.
- **D-03:** All guide generation uses the **Story** tier via `ILlmGateway` (strict JSON-schema DTOs), works with Claude or OpenAI.
- **D-04:** UI uses the Phase 2 theme: `Helpers/Theme`, `ThemedButton` (via `ButtonHelper.CreateButton`), `CardPanel`, `SlimProgressBar`; sizes in logical px via `LogicalToDeviceUnits`; content-sized rows. The user runs Windows at ~150% scaling — every new screen must be rendered at 144 DPI and inspected before hand-off (see `ReelDiscovery.Tests` harness approach used in Phase 2: STA thread, `SetProcessDpiAwarenessContext(-4)`, `DrawToBitmap`). The user has rejected unaligned / stock-looking UI twice; visual quality is a hard requirement.

#### Guide screen layout
- **D-05:** **One wizard step, "Case Guide"**, after Review Core Document, with **tabs: Issues / Cast / Key Facts / Canonical Values**. Use the same card visual language as the Review Core Document screen (tabs may be a themed segmented control like that screen's Section/Full document toggle rather than a stock TabControl).
- **D-06:** On entering the step with no guide yet, generation runs **automatically in order: issues → cast → facts → canonical values**; each tab fills as its part finishes, with inline progress and Cancel (same pattern as core document generation: partial results kept, refusal/fatal/timeout handling).
- **D-07:** Key Facts shown as a **sortable table** (date, fact, issues, actors, who knew) **+ detail panel** for the selected fact.
- **D-08:** **Issues are extracted from the core document's counts (pleadings) or findings (memo)** with stable IDs; the Issues tab is **read-only** (change issues by editing the core document).

#### Saving & reopening
- **D-09:** **Per-case folder, auto-named**: default `Documents\ReelDiscovery\Cases\<case title>` (sanitized, unique), created when the case is first saved (when the core document exists); user can change the location. `case.json`, Word exports, and later the corpus all live there.
- **D-10:** **Autosave**: `case.json` is written after every generation step and every edit (core document edits included). No Save button. A crash/close never loses paid-for work. (This replaces Phase 2's "Close Case discards the case" confirmation — closing no longer discards; update/remove that C6 prompt accordingly.)
- **D-11:** **Reopen**: an **"Open Case..." button plus a short recent-cases list on the Describe the Case step**; opening loads `case.json` and jumps to the furthest step the case has reached.
- **D-12:** **End of flow (until Phases 4-5 exist)**: the Case Guide step's Next leads to a **Case Summary** step showing what was saved where, with buttons to **export the core document, cast and key-facts summary to Word (.docx)** and **open the case folder**; Finish closes. Phase 4 later inserts corpus steps before the summary.

#### Cast shape & email identities
- **D-13:** **Cast size is preset-driven**: key characters follow the complexity preset (`ComplexityPreset.CharactersMin/Max`, e.g. Standard 12-20), plus **background (ancillary) staff per organization scaled by preset (~3 / 6 / 10 per org for Simple / Standard / Complex)** for non-relevant traffic later. Key vs background is a field on each character.
- **D-14:** **Realistic .com-style email identities**: `first.last@<orgdomain>.com` style, domain derived from each organization's name, consistent per org (one domain per organization; law firms get their own). No reserved `.example` domains. (No mail is ever sent.)
- **D-15:** **Counsel**: each represented organization gets **in-house counsel** and an **outside law firm (at least a partner + an associate)**, so privileged threads / privilege-log material can exist later (GUIDE-03).
- **D-16:** Each **key character carries a short voice/writing-style note (1-2 lines) and a signature block**, for consistent email voice in later phases.
- **D-17:** Required cast fields (GUIDE-02): stable ID, name, title, organization, email address, active date range, summary of key involvement; plus key/background flag, role (party/counsel/staff), voice note, signature (key only).

#### Editing, regenerating & staleness
- **D-18:** Cast and Key Facts use **table + detail panel** editing: short fields edit in the table, long fields and link pickers (issues, actors, who knew) in the panel.
- **D-19:** Regenerate **a whole tab with optional guidance** ("Regenerate Cast" / "Regenerate Facts") **or a single entry** ("Regenerate this entry"). Hand-edited entries are kept unless the user confirms replacing them.
- **D-20:** **Staleness**: editing the core document after the guide exists shows a **banner on the Case Guide** ("out of date with the core document") with an **"Update guide"** action that regenerates while preserving hand edits where possible; nothing regenerates silently. Editing the cast flags affected facts as needing review. (GUIDE-09; use the existing `CaseDraft.Revision` / source-hash pattern from Phase 2.)
- **D-21:** **Add / Delete** characters and facts by hand. Deleting a referenced character shows the facts that reference it; the **validator flags orphan links** until fixed (GUIDE-07).

### Claude's Discretion
- Exact JSON shape of `case.json` (extend the Phase 2 `CaseDraft` + shared `JsonOptions` with string enums; version field for future migrations).
- Stable ID format (e.g. `ISS-01`, `CH-007`, `F-023`, `V-012`) and how IDs survive regeneration (keep IDs of matched entries).
- Canonical values registry contents and editing UI (table on its own tab; editable), and how the validator checks other artifacts against it.
- Validator rules beyond GUIDE-07's minimum (dates in case window and within each character's active range, facts reference existing cast/issues, no orphan IDs, unique emails, every issue has at least one fact, every key character appears in at least one fact).
- Generation decomposition and batching (respect the provider max-output-token caps found in Phase 2; batch large casts/timelines).
- Word export layout (use existing `Services/OfficeDocumentService.cs` builders; clean, readable formatting).
- Recent-cases list storage (e.g. `%LOCALAPPDATA%\ReelDiscovery\recent.json`).

### Deferred Ideas (OUT OF SCOPE)
None — discussion stayed within phase scope.
</user_constraints>

<phase_requirements>
## Phase Requirements

| ID | Description | Research Support |
|----|-------------|------------------|
| GUIDE-01 | Issue list (allegations/counts) with stable IDs from the core document | Deterministic `IssueExtractor` maps `CoreDocument.Counts` → `CaseIssue` (`ISS-nn`, `CountId` link); optional Story enrichment call adds description / elements / relevance criteria (Pattern 2) |
| GUIDE-02 | Cast with stable IDs: name, title, org, email, active range, key-involvement summary | `CastMember` model; roster call + batched detail calls; `EmailIdentity` derives email deterministically; `IdAllocator` `CH-nnn` (Patterns 1, 3, 4) |
| GUIDE-03 | Counsel characters (in-house and outside) | `CaseOrganization.Kind = LawFirm` + `RepresentsOrgIds`; `CastRole.InHouseCounsel / OutsideCounsel`; prompt requires per represented org 1 in-house + firm with partner + associate; the core document's signing attorney seeded into the cast; validator coverage warning (Pattern 3, Validator rules) |
| GUIDE-04 | Ancillary (non-key) characters per organization | `CastTier.Background`; one call per organization sized by new `ComplexityPreset.BackgroundPerOrg` (3/6/10) |
| GUIDE-05 | Dated key-facts timeline with stable IDs, linked to issues, actors, who knew | `KeyFact` (`F-nnn`, `Date` yyyy-MM-dd, `IssueIds`, `ActorIds`, `KnownByIds` ⊇ actors); one call per `FactPlan` section; code validates every ID (Pattern 5) |
| GUIDE-06 | Canonical values registry | `CanonicalValue` (`V-nnn`, `Kind`, `Label`, exact `Value`, `FactIds`); one call + deterministic `CanonicalValueScanner` for amounts / document numbers (Pattern 6) |
| GUIDE-07 | Deterministic validator | `CaseGuideValidator.Validate(CaseDraft)`: static, null-safe, never throws, Error/Warning severities, rules table below |
| GUIDE-08 | Review, edit, regenerate cast and facts | `CaseGuideEditing` (pure: apply edit, add, delete-with-references, mark `IsUserEdited`); tab and single-entry regeneration that keep IDs; `StepCaseGuide` grid + detail panel |
| GUIDE-09 | Upstream edit warns + marks downstream stale | `GuideStaleness`: per-part basis hashes (core doc content hash → issues/cast; + cast/issues hash → facts; + facts → values); cast edit → `KeyFact.NeedsReview`; banner + "Update guide"; notice on Review Core Document |
| GUIDE-10 | case.json saved alongside the output, reloadable | `CaseStore` (per-case folder, atomic temp+move with retry, `FormatVersion`, `Stage` for reopen, recent.json); `CaseDraft.Normalize()` after load |
| GUIDE-11 | Export core document, cast, key facts as .docx | New `CaseGuideDocxExporter` (OpenXml headings + tables + landscape sections, zero validator errors, XML-invalid characters stripped) |
</phase_requirements>

## Project Constraints (from CLAUDE.md)

CLAUDE.md is partly stale (it says .NET 8 and OpenAIService). **Trust the code:** `net10.0-windows`, SDK pinned by `global.json` to 10.0.401 [VERIFIED: csproj, global.json, `dotnet --version`]. All text LLM calls go through `ILlmGateway` [VERIFIED: Services/Ai]. Directives that still apply:

- C# / WinForms, Windows x64, single-file self-contained publish. **No new NuGet packages** without need (none needed here).
- MimeKit / DocumentFormat.OpenXml are reused (OpenXml 3.5.1 in csproj).
- One primary public type per file; namespace mirrors folder (`ReelDiscovery.Models.Case`, `ReelDiscovery.Services.Case`, `ReelDiscovery.UserControls`).
- Private fields `_camelCase`; WinForms control fields Hungarian after underscore (`_gridCast`, `_btnRegenerateCast`); constants PascalCase; `ct` last parameter, `IProgress<T>? progress = null, CancellationToken ct = default`.
- Async methods end in `Async`. Generators return result objects, not tuples.
- UI built in code: `InitializeUI()`, `TableLayoutPanel` root docked Fill, **row-index comments on RowStyles**, handlers wired **after** all controls exist.
- `[GeneratedRegex]` instead of `new Regex(...)`; **every regex has a match timeout** (Phase 2 WR-04).
- No `record` types, no primary constructors, no `#region`. Models are mutable classes with auto-properties. Target-typed `new()`.
- Strict-schema DTOs are nested `internal class *Dto` types with `[JsonPropertyName]` + `[Description]`, **no nullable properties**, dates as `yyyy-MM-dd` strings parsed with `CaseDraft.TryParseDate`.
- Catch `OperationCanceledException` before `Exception`. Bare `catch` only for non-critical side effects, always with a comment. Never use a silent catch for case saving (it is core).
- `Random.Shared` in new code; `Interlocked` / `lock` for shared state.
- Do not add explicit usings for implicit namespaces (`System`, `System.IO`, `System.Linq`, `System.Drawing`, `System.Windows.Forms`, `System.Threading.Tasks`).
- GSD workflow: edits happen inside `/gsd-execute-phase`.

## Architectural Responsibility Map

Single-process desktop app. The "tiers" here are the app's layers.

| Capability | Primary Tier | Secondary Tier | Rationale |
|------------|-------------|----------------|-----------|
| Guide data model, IDs, cross-references | Models (`Models/Case`) | — | Pure data plus `Normalize()` and hash helpers; JSON-round-trippable |
| ID allocation, email derivation, folder naming | Services (pure static) | — | Deterministic, unit-testable, no LLM |
| Issue extraction | Services (pure, from `CoreDocument.Counts`) | LLM enrichment (Story) | Issues exist without the model; enrichment adds prose only |
| Cast / facts / values content | LLM via `ILlmGateway` (Story) | Services (mapping + reference checks) | Model writes content; code assigns IDs, checks links, computes emails |
| Consistency validation | Services (`CaseGuideValidator`, static) | UI (shows findings) | Must be deterministic and never throw (GUIDE-07) |
| Staleness | Services (`GuideStaleness`, hashes) | UI banner | Hash comparison is exact and survives reload |
| Persistence (case.json, recent.json, exports) | Services (`CaseStore`, `CaseGuideDocxExporter`) | WizardForm (flush on navigation/close) | File I/O isolated from UI; atomic writes |
| Editing / regeneration orchestration | UI (`StepCaseGuide`) | Services (`CaseGuideEditing`, generator) | Pure edit operations are unit-testable; the step only wires them |
| Reopen / navigation to furthest step | UI (`WizardForm`, `StepCaseScenario`) | Models (`CaseDraft.Stage`) | Stage stored in data; mapping to step type in the form |

## Standard Stack

### Core (all already referenced; nothing to install)
| Library | Version | Purpose | Why Standard |
|---------|---------|---------|--------------|
| In-repo `ILlmGateway` / `LlmGateway` | — | Strict structured output, Story tier, outcome classification, usage tracking | Locked (D-03). Same calling pattern as `CoreDocumentGenerator` [VERIFIED: Services/Ai/LlmGateway.cs] |
| `Microsoft.Extensions.AI.Abstractions` | 10.10.1 | DTO → strict JSON schema through `JsonSchemaRegistry` | Already used; emits `additionalProperties:false`, all-required [VERIFIED: csproj, JsonSchemaRegistry.cs] |
| `System.Text.Json` | in-box (.NET 10) | case.json / recent.json; `CaseDraft.JsonOptions` (indented, case-insensitive, string enums) | Already the case.json serializer (Phase 2 round-trip tests) [VERIFIED: CaseDraft.cs] |
| `DocumentFormat.OpenXml` | 3.5.1 | .docx export (headings, tables, landscape sections) + `OpenXmlValidator` in tests | Already referenced; validator already used in `DocumentRegressionTests` [VERIFIED: csproj, tests] |
| `System.Security.Cryptography.SHA256` | in-box | Content hashes for staleness (`CaseDraft.ComputeHash`) | Existing helper [VERIFIED: CaseDraft.cs] |
| xUnit | 2.9.3 (+ runner 3.1.4, Test.Sdk 17.14.1) | Tests | Existing test project, 501 tests passing in about 1 s [VERIFIED: `dotnet test` run 2026-10-01] |

### Supporting (in repo)
| Asset | Purpose | When to Use |
|-------|---------|-------------|
| `NameSubstitution.FindLeaks` / `Apply` | Real-name leak scan over cast/org/fact/value text | Validator rule; also `Apply` to model output defensively |
| `CoreDocumentAssembler.Number` / `BuildCaption` / `BuildMemoHeader` / `BuildSignatureBlock` | Numbered paragraphs + caption for the core-document .docx | Exporter (do not re-implement numbering) |
| `CaseTestData` + `FakeLlmProvider` + `StrictSchemaValidator` | Generator tests without a provider; schema strictness | Every new generator / DTO |
| `Theme`, `ThemedButton` (Toggle variant + `Selected`), `CardPanel`, `SlimProgressBar`, `ButtonHelper.CreateButton` | Visual language (D-04) | All new UI |
| `FileNameHelper.SanitizeForFileName` | Base for folder names | Extend with reserved-name / trailing-dot / length rules |

### Alternatives Considered
| Instead of | Could Use | Tradeoff |
|------------|-----------|----------|
| New `CaseGuideDocxExporter` | Extend `OfficeDocumentService.CreateWordDocument` | Existing builder has no headings/tables and a pinned schema-order bug (w:color after w:sz). Changing it risks the legacy attachment path and its pinned test. Build a separate, clean exporter |
| Unbound `DataGridView` rows | `BindingSource` over `List<T>` | `List<T>` does not support header-click sorting (no `IBindingList` sort) [ASSUMED: standard WinForms behavior]. Unbound rows sort automatically and keep IDs in `Row.Tag` |
| Hash-based staleness | `CaseDraft.Revision` counter only | The counter cannot tell "edited then reverted" from a real change, and it bumps on resume writes. Keep `Revision` as a cheap change counter, but decide staleness by content hash |
| Deterministic issue extraction | LLM issue extraction | Counts already exist with IDs; an LLM pass could renumber or merge them. Use the LLM only to enrich descriptions |
| Code-derived emails | Model-written emails | Model emails drift (wrong domain, `.example`, duplicates). Code derivation is exact (D-14) |

**Installation:** none.

## Architecture Patterns

### System Architecture Diagram

```
Describe the Case step ──(Open Case... / recent list)──► CaseStore.Load ─► CaseDraft.Normalize ─► Wizard jumps to Stage
        │ Next (scenario)
        ▼
Review Core Document ──(first complete doc / every edit)──► CaseStore.Save (creates per-case folder on first save)
        │ Next  [notice if Guide exists: edits will mark guide out of date]
        ▼
Case Guide step ── OnEnter, no guide ──► CaseGuideGenerator (Story tier, strict DTOs)
   │                                     1. Issues: IssueExtractor(Counts) ─► [enrich call] ─► Issues tab fills ─► save
   │                                     2. Cast: roster call ─► detail calls (≤10/batch, ≤3 parallel)
   │                                        ─► background call per org ─► EmailIdentity (code) ─► Cast tab ─► save
   │                                     3. Facts: one call per FactPlan section (≤3 parallel), IDs checked in code
   │                                        ─► merge in date order ─► Key Facts tab ─► save
   │                                     4. Values: one call + CanonicalValueScanner ─► Values tab ─► save
   │        each part: Pending/Generating/Ready/Failed; Cancel keeps finished parts; fatal stops the run
   │
   ├─ user edits (grid cell / detail panel / add / delete) ─► CaseGuideEditing (IsUserEdited, NeedsReview) ─► save
   ├─ Regenerate tab / entry (guidance) ─► generator merge (existingId, keep hand edits) ─► save
   ├─ every refresh ─► CaseGuideValidator.Validate ─► "Consistency checks" card (Errors block Next)
   └─ GuideStaleness.Check(core doc hash vs part basis) ─► banner "out of date" ─► [Update guide]
        │ Next
        ▼
Case Summary step ─► CaseGuideDocxExporter (core doc / cast / key facts) ─► <case folder>\*.docx
                  ─► Open case folder (explorer) ─► Finish (flush save, close; no discard prompt)
```

### Recommended Project Structure
```
Models/Case/
├── CaseDraft.cs              # + FormatVersion, CaseId, Stage, Guide, [JsonIgnore] FolderPath, Normalize()
├── CaseStage.cs              # enum Described, CoreDocument, CaseGuide, Summary (Phase 4 adds values)
├── CaseGuide.cs              # Issues, Organizations, Cast, Facts, Values, IdCounters, part status + basis hashes
├── CaseIssue.cs
├── CaseOrganization.cs       # Id, Name, ShortName, Kind, PartyId, RepresentsOrgIds, Domain, Location, TimeZoneId
├── CastMember.cs             # + CastTier, CastRole enums
├── KeyFact.cs                # + FactSignificance enum
├── CanonicalValue.cs         # + ValueKind enum
└── GuidePartState.cs         # Status, FailureMessage, BasisHash, GeneratedUtc
Services/Case/
├── IdAllocator.cs            # static: Next(guide, prefix) using persisted counters
├── EmailIdentity.cs          # static: DomainFor(org, taken), AddressFor(first, last, domain, taken)
├── IssueExtractor.cs         # static: Counts → issues, matching existing IDs
├── CaseGuideGenerator.cs     # gateway-backed; per-part + single-entry methods; nested strict DTOs
├── CaseGuidePrompts.cs       # byte-stable system prompt (D-01, D-02) + shared prompt fragments
├── CanonicalValueScanner.cs  # [GeneratedRegex] amounts / "No." / "#" identifiers, with timeouts
├── CaseGuideValidator.cs     # static, never throws; GuideFinding list
├── GuideStaleness.cs         # content hashes, stale parts, cast-edit → facts NeedsReview
├── CaseGuideEditing.cs       # pure edit / add / delete / references-of
├── CaseStore.cs              # folder creation, atomic save, load + version check, recent.json
├── CaseFolderNaming.cs       # default name from parties / memo, sanitize, reserved names, unique
└── CaseGuideDocxExporter.cs  # core doc / cast / key facts .docx via a small OpenXml builder
UserControls/
├── StepCaseGuide.cs          # segmented tabs, grid + detail cards, checks card, status strip
├── StepCaseSummary.cs        # saved location, exports, open folder
└── LinkPicker.cs             # filter box + CheckedListBox for issues / cast references
ReelDiscovery.Tests/Case/
├── CaseGuideModelTests.cs, IdAllocatorTests.cs, EmailIdentityTests.cs, IssueExtractorTests.cs
├── CaseGuideGeneratorTests.cs, CaseGuideValidatorTests.cs, GuideStalenessTests.cs
├── CaseGuideEditingTests.cs, CaseStoreTests.cs, CaseFolderNamingTests.cs
├── CaseGuideDocxExporterTests.cs, CaseGuideUiLogicTests.cs
└── Render/ScreenRenderTests.cs   # [Trait("Category","Render")] 144-DPI PNGs + clipping detector
```

### Pattern 1: Code-owned IDs with a persisted allocator (never reuse)
**What:** `CaseGuide.IdCounters` (`Dictionary<string,int>` keyed by prefix) stores the last number issued. `IdAllocator.Next(guide, "CH")` returns `CH-001`, `CH-002`, and so on. Deleted IDs are never reissued, because Phase 4/5 artifacts and exported documents may still cite them.
**Formats:** `ISS-01`, `ORG-01`, `CH-001`, `F-001`, `V-001` (zero-padded so string sort = numeric sort in the grid).
**When:** Every entity creation: generation mapping, hand Add, and regeneration of a new entry.
```csharp
public static string Next(CaseGuide guide, string prefix, int width)
{
    // Counter only grows: also covers files edited by hand with higher IDs
    var max = guide.AllIds().Where(id => id.StartsWith(prefix + "-", StringComparison.Ordinal))
        .Select(id => int.TryParse(id.AsSpan(prefix.Length + 1), NumberStyles.None, CultureInfo.InvariantCulture, out var n) ? n : 0)
        .DefaultIfEmpty(0).Max();
    var next = Math.Max(max, guide.IdCounters.GetValueOrDefault(prefix)) + 1;
    guide.IdCounters[prefix] = next;
    return prefix + "-" + next.ToString(new string('0', width), CultureInfo.InvariantCulture);
}
```

### Pattern 2: Issues are extracted, not invented (D-08, GUIDE-01)
`IssueExtractor.Extract(CoreDocument doc, CaseGuide guide)` maps each `CaseCount` to a `CaseIssue { Id, CountId, Number, Title, LegalBasis, Summary, AgainstPartyIds, Description, Elements, RelevanceCriteria }`. An existing issue keeps its ID when `CountId` and the normalized title both match. If only the `CountId` matches (the outline was regenerated and reordered), match by title alone. Otherwise allocate a new ID. Orphaned old issues are dropped, and any facts that referenced them become validator orphans.

The optional enrichment call (`IssueDetailsDto`, keyed by the issue IDs listed in the prompt) fills `Description`, `Elements` (what must be proven) and `RelevanceCriteria` (what responsive evidence looks like). Phase 4's review protocol (KEY-*) needs these. If the call fails, the issues still exist and the part is marked Failed only for the enrichment.

### Pattern 3: Cast in three call types; code finishes every identity (GUIDE-02/03/04, D-13..D-17)
1. **Organizations (code first):** each `CaseParty` that is not an individual becomes a `CaseOrganization` (`Kind = Party`, `PartyId`). Domicile gives `Location`, and `TimeZoneId` is derived from the US state with a fixed table (Phase 4 PLAN-04 needs time zones; cheap to add now). Individual parties (EntityType contains "individual") become cast members, not organizations.
2. **Roster call** (`CastRosterDto`, ~3-5K output tokens): the model returns additional organizations (outside law firms, the memo author's firm, named third parties) and the key roster: `firstName`, `lastName`, `title`, `organizationRef` (existing `ORG-nn` or the exact name of a new org in the same response), `role` (enum), and a one-line `involvement`. The prompt lists (a) every individual the core document names, (b) the signing attorney `doc.Counsel.AttorneyName` / `FirmName` (must be in the cast as outside counsel), and (c) counsel requirements: per represented organization, one in-house counsel plus an outside firm with at least a partner and an associate. Code resolves org refs, allocates IDs, and derives emails.
3. **Detail calls** (`CastDetailsDto`, at most 10 people per call, at most 3 in parallel): `summary` of key involvement, `activeFrom`, `activeTo`, and a 1-2 line `voiceNote`. The **signature block is built by code** from name, title, organization, a fictional `(555) 555-01xx` phone and the derived email (the same rule `CoreDocumentGenerator` uses for counsel phones). The model may only pick a `signatureStyle` enum (Full / Short / NameOnly / MobileFooter). This keeps signatures byte-consistent with the cast fields, which the Phase-0 PoC confirmed matters ("signature always appended deterministically ... never drifts").
4. **Background staff** (`BackgroundStaffDto`, one call per organization, at most 3 in parallel): `ComplexityPreset.BackgroundPerOrg` people (new preset field: 3 / 6 / 10) with name, title, department, one-line role, active range. `Tier = Background`, no voice note or signature required.

**Who counts toward `CharactersMin/Max`:** recommend non-counsel key characters only, with counsel added on top [ASSUMED: see Open Questions].

### Pattern 4: Deterministic email identities (D-14)
```csharp
// Domain: org short name (else name) minus legal suffixes and "The", ASCII-folded, alphanumerics only, + ".com".
// Law firms with 2+ surname tokens keep the first two ("thistlebracken.com"). Collisions append "law"/"group"/digits.
// Local part: first.last, ASCII-folded, apostrophes dropped, hyphens kept; collisions: first.m.last, then first.last2.
public static string DomainFor(CaseOrganization org, ISet<string> takenDomains) { ... }
public static string AddressFor(string first, string last, string domain, ISet<string> takenAddresses) { ... }
```
- Legal suffixes to strip (case-insensitive, whole token, trailing commas/periods): LLC, L.L.C., Inc, Incorporated, Corp, Corporation, Co, Company, LLP, L.L.P., LP, PC, P.C., PLLC, Ltd, Limited, PLC, NA, N.A. [ASSUMED list; extend as found].
- ASCII folding: `string.Normalize(NormalizationForm.FormD)`, drop `UnicodeCategory.NonSpacingMark`, keep `[a-z0-9]`.
- Reserved / test TLDs (`.example`, `example.com`, `.test`, `.invalid`, `.localhost`) are never produced. The validator warns if a hand edit introduces one.
- Government agencies (investigation memos from the government perspective) use `.gov` [ASSUMED; see Open Questions].
- Recompute an address only when the name or organization changes and the email was not hand-edited (`EmailIsUserEdited`).
- The Phase 2 outline DTO still says counsel email is "at an .example domain" (`CoreDocumentGenerator.CounselDto.Email` description). After the cast exists, sync `doc.Counsel.Email` from the matching cast member, and change that description to stop asking for `.example`. Otherwise the pleading signature block and the cast disagree.

### Pattern 5: Facts batched by FactPlan section, links checked in code (GUIDE-05)
- One call per `FactPlan` entry (`facts-1..N`, 2-8 per preset), at most 3 in parallel. Input: the section's paragraphs + its date range, all issues (ID, title, one-line summary), and the compact cast roster (`CH-nnn | name | title | org`, about 15 tokens per person, so even 150 people is about 2.5K tokens). Output `FactBatchDto.Facts[]`: `date` (yyyy-MM-dd), `title` (≤ 12 words), `description` (1-3 sentences, firm position, D-02), `issueIds[]`, `actorIds[]`, `knownByIds[]`, `significance` (enum Hot / Key / Supporting), `existingId` (empty for new).
- Target count per section = preset facts total ÷ sections, weighted by `TargetParagraphs`. New preset fields `FactsMin/Max` (Simple 12-20, Standard 30-50, Complex 70-110) [ASSUMED; tunable].
- **Mapping rules (code):** unknown IDs are dropped and recorded on the fact as `MappingNotes` (shown as Warnings). `KnownByIds = KnownByIds ∪ ActorIds` (actors always know, which Phase 5 knowledge gating needs). A fact with no valid issue or actor left is still kept, and the validator raises an Error so the user fixes it. Dates outside the section range are kept and flagged.
- Parallel calls return DTOs only. **Merging into `guide.Facts` happens once, on the calling thread, after `Task.WhenAll`**, sorted by date, then IDs are allocated in date order for the first generation. Generator tests run without a `SynchronizationContext`, so continuations land on pool threads; mutating shared lists inside the parallel tasks would race.

### Pattern 6: Canonical values = model registry + deterministic scanner (GUIDE-06)
- One call (`ValuesDto`): input is all facts (ID, date, description) plus the fact sections' text. Output: values with `kind` (Amount, InvoiceNumber, ChangeOrderNumber, ContractNumber, PurchaseOrder, DocumentTitle, ProjectName, AccountNumber, Address, Other), `label` ("Change Order 7 amount"), exact `value` ("$184,250.00"), `description`, `factIds[]`.
- `CanonicalValueScanner` (`[GeneratedRegex]` with timeout) extracts `$` amounts and `No./#/CO-/INV-` identifiers from the core document + facts. The validator warns on: scanned amounts or identifiers not in the registry, registry values that appear nowhere, duplicate `Value` with different labels, and duplicate `Label` with different values (Error: the same thing has two values).
- Downstream (Phase 5, CORP-02) checks generated emails against this registry. Phase 3 only needs the registry plus the core-doc/facts cross-check.

### Pattern 7: Regeneration that keeps IDs and hand edits (D-19, GUIDE-08)
- **Tab regenerate (with guidance):** prompt lists current entries with IDs. Entries with `IsUserEdited` are listed under "Keep exactly; do not return these" unless the user confirmed replacing them. The DTO has `existingId` per entry. Code keeps an existing ID only if it exists, is not user-edited (or replacement was confirmed), and is not already used in this response. Otherwise it allocates a new ID. Non-edited entries the model did not return are removed. Facts that referenced removed cast become orphans (validator Error) and `NeedsReview`.
- **Order matters for "Update guide":** issues → cast → facts → values, each only when its basis hash changed, so facts are rebuilt against the new cast.
- **Single entry:** a one-entry DTO (same shape as the batch item). The object is updated in place (same ID), `IsUserEdited = false`, and old text is kept on failure (same contract as `RegenerateSectionAsync`).
- **Confirmation copy** follows Phase 2 (default button No).

### Pattern 8: Staleness by content hash (GUIDE-09, D-20)
```csharp
// CoreDocument content hash: parties, counts, fact plan, timeline, and every section's Id + paragraphs
public static string CoreDocumentHash(CoreDocument doc) => CaseDraft.ComputeHash(JsonSerializer.Serialize(new
{
    doc.Parties, doc.Counts, doc.FactPlan, doc.TimelineStart, doc.TimelineEnd, doc.FilingDate,
    Sections = doc.Sections.Select(s => new { s.Id, s.Paragraphs })
}, CaseDraft.JsonOptions));
// Part bases: Issues/Cast ← core hash; Facts ← core + issues + cast (ID, name, org, active range); Values ← core + facts
```
- Each `GuidePartState.BasisHash` is stamped when the part is generated or updated. `GuideStaleness.StaleParts(draft)` compares bases. The banner shows when Issues or Cast are stale against the core document. "Update guide" re-runs only the stale parts, in order.
- **Hand edits do not restamp their own part, but they do change downstream bases.** A cast edit makes Facts "needs review", not "regenerate". The UI should say "3 facts reference changed characters" and mark those facts `NeedsReview` deterministically (`CaseGuideEditing.ApplyCastEdit` returns the affected fact IDs). Renaming a character also replaces exact full-name occurrences in those facts' text (word-boundary, `Regex.Escape`, timeout) and still flags them for review.
- On Review Core Document: when `Guide != null`, show a one-line notice ("The case guide was built from this document. Edits will mark it out of date."). Regenerate All also mentions the guide in its confirmation.
- `CaseGuide.ComputeHash()` (whole guide) is defined now so Phase 4's plan can stamp its own basis.

### Pattern 9: Per-case folder, atomic autosave, reopen (GUIDE-10, D-09..D-11)
- **Folder:** `Path.Combine(Environment.GetFolderPath(SpecialFolder.MyDocuments), "ReelDiscovery", "Cases", name)`.
  - Default name: pleadings use `"{first plaintiff short} v. {first defendant short}"`; memos use `"{Memo.Organization or first party} - {Memo.Re}"`; fallback `doc.Title`.
  - Sanitize: invalid filename chars removed; trailing dots and spaces trimmed (Windows strips them, so `"Harmon v."` would break the path); reserved device names (`CON, PRN, AUX, NUL, COM1-9, LPT1-9`) get a suffix; at most 80 chars.
  - Uniqueness: `" (2)"`, `" (3)"`, and so on.
  - Created on the first save after the core document exists. `FolderPath` is `[JsonIgnore]` (set on load from the file location, so moved folders still work).
- **case.json fields added to `CaseDraft`:** `FormatVersion` (1), `CaseId` (Guid, recent-list identity), `CreatedUtc`, `UpdatedUtc`, `Stage` (furthest reached), `Guide`. **Never** API keys, provider or model IDs (test asserts this).
- **Atomic write:** serialize on the UI thread (consistent snapshot; generators mutate on the UI context), then write bytes on a pool thread. Write to `.case.json.<guid>.tmp` with `FileOptions.WriteThrough` + `Flush(true)`, then `File.Move(tmp, path, overwrite: true)`. Retry up to 5 times with backoff on `IOException` / `UnauthorizedAccessException` (OneDrive, antivirus and the search indexer can hold transient locks). Delete the temp file on final failure. One writer at a time (`SemaphoreSlim(1)`), and the latest snapshot wins (coalesce requests that arrive while a write runs).
- **Save failures are visible:** status line "Not saved: {reason}. Your work is still open; it will be saved again on the next change." Never a silent catch.
- **Load:** size cap (e.g. 50 MB), `JsonDocument` peek at `FormatVersion` (missing = 1; greater than current = refuse: "This case was saved by a newer version of ReelDiscovery."), deserialize, `Normalize()` (null lists → empty, Phase 2 IN-03), then `FolderPath` set.
- **Recent cases:** `%LOCALAPPDATA%\ReelDiscovery\recent.json` (same folder as `ApiKeyStore`), `[{ Path, Title, LastOpenedUtc }]`, at most 8, most recent first. Missing paths are pruned on read, and the file is written atomically too.
- **Reopen:** `StepCaseScenario` gets "Open Case..." (`OpenFileDialog`, filter `case.json|case.json|JSON files (*.json)|*.json`) plus a short recent list. After loading: replace `_state.Case`, **repopulate the step's inputs from the loaded draft before navigating**, then `WizardForm` maps `Stage` → step type (not index; Phase 4 inserts steps) and calls `NavigateToStep`.
- **Close (D-10):** WizardForm flushes the current step and saves before closing, with no discard prompt once a folder exists. If only a scenario exists (no core document, so no folder per D-09), keep a short "This scenario has not been saved yet" confirmation [ASSUMED; see Open Questions]. Update `DiscardCaseMessage`, `HasUnsavedCase` and their tests.

### Pattern 10: StepCaseGuide layout (D-04, D-05, D-07, D-18)
- Root TableLayoutPanel rows (row-index comments):
  - 0: header (title "Case Guide", subtitle = counts summary, right-aligned tab actions such as "Regenerate Cast...")
  - 1: stale banner (AutoSize, hidden when fresh; `CardPanel` with Warning border + "Update guide" button)
  - 2: segmented tabs: a FlowLayoutPanel of 4 `ThemedButton` Toggles, text with counts ("Cast (38)"). The existing Toggle variant paints independent rounded segments, so N segments work as-is [VERIFIED: ThemedButton.Colors()]
  - 3: body (Percent 100): per tab, a 2-column TLP (≈60/40) with a grid `CardPanel` and a detail `CardPanel`. Issues uses the same shape, read-only
  - 4: consistency-checks card (Absolute height like Phase 2's issues card; **label it "Checks", not "Issues"**, to avoid colliding with the legal Issues tab)
  - 5: status strip (status label, `SlimProgressBar`, "Cancel Generation")
- **Grid:** unbound rows, `Row.Tag = entity Id`, columns sized with `LogicalToDeviceUnits`, `RowTemplate.Height = LogicalToDeviceUnits(32)`, header style copied from the Phase 2 issues grid. Sort is automatic. **Rebuilds preserve sort column, direction and the selected ID**, and are deferred with `BeginInvoke` when triggered from a cell event.
- **Detail panel:** a `Panel { AutoScroll = true }` holding a `Dock = Top, AutoSize` TLP of label/field pairs. Long text uses a borderless multiline TextBox in a `CardPanel` (the Phase 2 guidance-box pattern). Link pickers use `LinkPicker`. Footer actions: "Regenerate this entry" (guidance box), "Delete".
- **Empty / generating state:** reuse `StepCoreDocument`'s empty-card + marquee pattern per tab ("Writing the cast..."). Tabs fill as parts finish (progress reports part + message).
- **Next gate:** generation running → disabled; Errors → blocked with a message; Warnings → Yes/No (default No). Same as Phase 2.

### Pattern 11: Word exports (GUIDE-11, D-12)
A small private builder inside `CaseGuideDocxExporter` (or `Services/Case/WordBuilder.cs`):
- `StyleDefinitionsPart` with Normal, Title, Heading1, Heading2 and a table style, so Word's navigation pane works.
- `Paragraph(text, style)`, `Table(headers, rows, columnWidthsTwips)`, and section breaks (portrait for prose, **landscape** for the cast and facts tables).
- Exports:
  1. **Core Document:** caption or memo header, section headings, numbered paragraphs from `CoreDocumentAssembler.Number`, prayer letters, signature block. Mirror `RenderPlainText` logic, but structured.
  2. **Cast of Key Characters:** an organizations table, a key-cast table (ID, Name, Title, Organization, Email, Active, Role), per-key-character profiles (summary, voice note, signature), and a background-staff table grouped by organization.
  3. **Key Facts & Events:** a chronological table (ID, Date as "March 3, 2024", Fact, Issues, Actors, Who knew), then an index by issue.
- File names: `"{folder name} - Core Document.docx"` and so on, written to the case folder atomically. If the target is open in Word (`IOException` sharing violation), say "Close {file} in Word and click Export again."

### Anti-Patterns to Avoid
- **Letting the model assign IDs or emails.** It will renumber, collide and use `.example`.
- **One call for the whole cast or timeline.** Complex casts (25-40 key + ~120 background) and timelines (70-110 facts) truncate at the 21,333 cap, and a fact batch also needs room for thinking tokens.
- **Nullable DTO properties / recursive DTOs / dynamic enums.** Schemas are cached per CLR type, so an "enum of current cast IDs" is impossible. Use strings and check them in code. Each `string?` adds a union type (Anthropic limit 16 per request).
- **Mutating `CaseGuide` inside parallel tasks** (see Pattern 5).
- **Rebuilding a DataGridView inside `CellEndEdit` / `CellValueChanged`.** This throws `InvalidOperationException` (reentrant `SetCurrentCellAddressCore`). Defer with `BeginInvoke`.
- **Binding the grid to `List<T>`** (no sorting) or showing raw `List<string>` IDs (unreadable). Show "ISS-01, ISS-03" text plus a tooltip with titles.
- **Saving inside a bare `catch {}`.** Saving is core work (CLAUDE.md error-handling rule).
- **Using `MyDocuments` without handling OneDrive** (transient locks, Files-On-Demand placeholders).
- **Navigating after Open Case without refreshing `StepCaseScenario` inputs.** Its `OnLeaveStepAsync` calls `StoreInputs` (the CR-01 fix) and would overwrite the loaded scenario with the old textbox text.
- **Naming the validator output "issues"** on a screen that has an Issues tab.

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| JSON schemas for new DTOs | Hand-written schema strings | `JsonSchemaRegistry` via the gateway | Strictness rules per provider already handled and tested |
| Retry / refusal / truncation / fatal handling | New retry loops | `LlmGateway` outcomes + `LlmCallException` + the Phase 2 catch ladder (`OperationCanceledException` → fatal → `LlmCallException` → `Exception`) | Already distinguishes user cancel, timeout, fatal, refused |
| Numbered pleading paragraphs in the export | New numbering | `CoreDocumentAssembler.Number`, `BuildCaption`, `BuildMemoHeader`, `BuildSignatureBlock` | Numbering rules (incorporation paragraphs, prayer letters) already tested |
| Real-name leak detection | New regex | `NameSubstitution.FindLeaks` | Single-pass, timeouts, aliases handled |
| .docx validity checking | Opening Word in tests | `OpenXmlValidator` (already used) + manual UAT open in Word | Word is installed for UAT [VERIFIED], but COM automation in tests is brittle |
| Hashing | Custom hash | `CaseDraft.ComputeHash` (SHA-256 hex) | Existing, deterministic |
| Date parsing | `DateTime.Parse` | `CaseDraft.TryParseDate` (yyyy-MM-dd, invariant) | Culture-independent (T-02-04) |
| Unicode → ASCII email folding | Char tables | `Normalize(FormD)` + drop `NonSpacingMark` | Handles accents generically |

**Key insight:** in this domain the expensive bugs are **consistency drift**: an email domain that differs between two characters of one organization, a fact pointing at a deleted character, a signature that disagrees with the cast row. Every place where code can be the single source (IDs, emails, signatures, links, dates) removes a class of drift that no prompt can fully prevent.

## Common Pitfalls

### Pitfall 1: Token caps and thinking tokens
**What goes wrong:** cast or fact batches come back `Truncated`. The gateway retries once at 2× and is capped at 21,333 on Anthropic, which includes Opus thinking.
**How to avoid:** set `MaxOutputTokens` around 12-16K per guide call, but size batches so the expected JSON is ≤ 6K tokens (≤ 10 detailed people, ≤ ~30 facts per section call). Split oversized fact sections (target > 30) into two calls by date sub-range.
**Warning signs:** `LlmOutcome.Truncated` in tests with large presets. Status "The response was cut off".

### Pitfall 2: The model invents or mangles IDs
**What goes wrong:** returns `CH-7` instead of `CH-007`, IDs from another case, or names in the ID field.
**How to avoid:** normalize (`CH-7` → `CH-007`) only if the result exists, otherwise drop it and record a mapping note. Also accept an exact full-name match as a fallback for cast references. Test with a fake response containing bad IDs.

### Pitfall 3: Fictional-work names replaced, or real names leaking (D-01, CASE-03)
**What goes wrong:** the cast prompt says "use fictional names" and the model renames Michael Scott. Or the model reintroduces a substituted real company (e.g. Staples) in fact text.
**How to avoid:** one byte-stable system prompt carrying the D-01 rule verbatim (adapted from `CoreDocumentGenerator.SystemPrompt`). Applied substitutions are listed in the user prompt ("use these fictional names exactly / never these real names"), and kept names are listed as approved. Run `NameSubstitution.FindLeaks` over every generated string in the validator. Add a prompt test asserting the absence of "every person" + "fictional" phrasing and the presence of the keep-names rule.

### Pitfall 4: Orphan references after delete or regenerate
**What goes wrong:** deleting `CH-012` leaves facts pointing at it, and Phase 4 crashes on lookup.
**How to avoid:** Delete shows the referencing facts (D-21) and offers "Remove from those facts" or "Delete anyway". The validator raises an Error per orphan, and Next is blocked until fixed. Phase 4 code must also tolerate unknown IDs (defensive).

### Pitfall 5: The Open Case flow clobbers the loaded draft
See Anti-Patterns. Also: `StepCoreDocument._declinedStaleHash` and selection state belong to the previous case. Reset per-case UI state on `OnEnterStepAsync` when `_state.Case` is a different instance (track `CaseId`).

### Pitfall 6: OneDrive / antivirus locks during autosave
**What goes wrong:** `File.Move(overwrite)` throws `IOException` / `UnauthorizedAccessException` intermittently in OneDrive-synced Documents (this machine's Documents is under OneDrive [VERIFIED]).
**How to avoid:** retry with backoff (Pattern 9). Keep the in-memory draft authoritative. Surface "Not saved" only after retries fail, and save again on the next change.

### Pitfall 7: DataGridView quirks at 150%
- `DataGridViewComboBoxColumn` raises a `DataError` dialog when a cell value is not in `Items`. Handle `DataError` and use display text, not raw IDs.
- `CheckedListBox.ItemCheck` fires **before** the state changes. Use `e.NewValue`, or apply in `BeginInvoke`.
- An AutoSize TLP inside an `AutoScroll` panel gets a horizontal scrollbar when its width is not tied to `ClientSize`. Set the TLP width on the panel's `ClientSizeChanged`.
- Fixed-pixel row heights clip at 144 DPI. Always use `LogicalToDeviceUnits` and content-sized rows (Phase 2 UAT fix 113e0c3).

### Pitfall 8: Grid edits and autosave storms
**What goes wrong:** every keystroke saves, or a cell edit triggers revalidate + rebuild, which resets scroll and selection.
**How to avoid:** commit on `CellEndEdit` / detail-field `Leave` / Apply (Phase 2 pattern). Coalesce saves. Rebuild by updating only the changed row's cells.

### Pitfall 9: XML-invalid characters in .docx
**What goes wrong:** LLM text can contain control characters (U+0000-U+001F except tab/CR/LF, U+FFFE/FFFF), which are illegal in XML. The save throws or Word reports a corrupt file [ASSUMED: XmlWriter rejects invalid chars by default].
**How to avoid:** strip them before `new Text(...)`. Newlines become `Break` elements. A test feeds `"\u0001"` and asserts the file opens and validates.

### Pitfall 10: OpenXml element order
**What goes wrong:** `OpenXmlValidator` flags children in the wrong schema order. The existing builder already ships 3 pinned `w:color` errors.
**How to avoid:** in `rPr` put `rFonts, b, ..., color, sz, szCs`. In `tblPr`: `tblStyle, tblW, ..., tblBorders, ..., tblLayout`. In `sectPr`: `pgSz` before `pgMar`. In `tcPr`: `tcW` before `shd`. Every `TableCell` contains at least one `Paragraph`, and a `TableGrid` with `GridColumn`s exists. The test asserts **zero** validator errors.

### Pitfall 11: "Issues" naming collision and stale copy
The Phase 2 issues card and validator use "Issues" for problems. On the Case Guide, call them **Checks**. `StepCoreDocument` copy says "click Finish again" and its `NextButtonText` is "Finish". Change to "Next >" and update the messages ("then click Next again").

### Pitfall 12: Step count / Finish assumptions
`WizardForm.UpdateNavigationButtons` labels the last step "Finish". `WizardFlowTests.CaseDriven_StepTypes` pins the 3-step list, and `FinishPath_ValidatesBeforeClose` reads source text. All change. Phase 4 will insert steps, so map `Stage` → step **type**, never an index.

## Code Examples

### Strict DTOs (no nullables, enums as strings, dates as strings)
```csharp
// Source: pattern from Services/Case/CoreDocumentGenerator.cs DTOs
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
```
Add every new top-level DTO to `CaseGuideGenerator.SchemaTypes` and to `CaseSchemaTests.CaseSchemaTypes()`.

### Gateway call (same shape as Phase 2)
```csharp
// Source: Services/Case/CoreDocumentGenerator.cs GenerateOutlineAsync
var result = await _llm.GenerateAsync<FactBatchDto>(new LlmRequest
{
    Tier = LlmTier.Story,
    OperationName = $"Case Guide - Facts ({plan.Heading})",
    SystemPrompt = CaseGuidePrompts.SystemPrompt,   // const, byte-stable
    UserPrompt = BuildFactsPrompt(draft, plan, existing, guidance),
    MaxOutputTokens = FactsMaxOutputTokens          // 16_000
}, ct);
var dto = result.GetValueOrThrow();                 // LlmCallException on Refused/Truncated/Invalid/Failed
```

### Validator skeleton (never throws, null-safe)
```csharp
// Source: pattern from Services/Case/CoreDocumentValidator.cs
public static List<GuideFinding> Validate(CaseDraft? draft)
{
    var findings = new List<GuideFinding>();
    var guide = draft?.Guide;
    if (draft?.CoreDocument == null || guide == null) return findings;

    var castIds = guide.Cast.Where(c => c != null).Select(c => c.Id).ToHashSet(StringComparer.Ordinal);
    var issueIds = guide.Issues.Where(i => i != null).Select(i => i.Id).ToHashSet(StringComparer.Ordinal);
    foreach (var fact in guide.Facts.Where(f => f != null))
    {
        foreach (var id in (fact.ActorIds ?? new()).Concat(fact.KnownByIds ?? new()).Where(id => !castIds.Contains(id)))
            findings.Add(GuideFinding.Error(GuideArea.Facts, fact.Id, $"{fact.Id} refers to a character that does not exist ({id})."));
        // ... issues, dates vs timeline, dates vs actor active ranges, etc.
    }
    return findings;
}
```

### Validator rules (recommended set)
| Rule | Severity |
|------|----------|
| Duplicate or empty ID in any list | Error |
| Fact → unknown issue / cast ID (orphan) | Error |
| Fact with no issue, or no actor | Error |
| Fact date not yyyy-MM-dd | Error |
| Fact date after the filing / memo date | Error |
| Fact date outside `TimelineStart..TimelineEnd` | Warning |
| Actor's active range does not contain the fact date | Error (Phase 4 cannot schedule the email) |
| Knower's active range does not contain the fact date | Warning |
| `KnownByIds` missing an actor | Warning (code normally enforces it) |
| Cast: active range invalid or ends before it starts | Error |
| Cast: email malformed, duplicate, domain ≠ its organization's domain (unless hand-edited), reserved/test TLD | Error / Error / Warning / Warning |
| Cast → unknown organization | Error |
| Two organizations with the same domain | Error |
| Issue → missing count (core doc changed) | Warning |
| Issue with no facts | Warning |
| Key (non-counsel) character in no fact | Warning |
| Counsel coverage per represented org (in-house + firm with partner + associate) | Warning |
| Preset ranges: key cast, background per org, facts total | Warning |
| Value: same label with different values | Error |
| Value: same value under different labels; scanned amount/identifier not registered; registered value used nowhere | Warning |
| Value → unknown fact | Error |
| Real-name leak in cast / org / fact / value text | Error (multi-word) / Warning (single token), same as Phase 2 |
| Part Failed / Pending (not generated) | Error |
| Part stale vs core document; fact `NeedsReview` | Warning |

### Atomic write
```csharp
// OneDrive-safe: temp file in the same folder, then same-volume rename with overwrite, retried on transient locks
public static async Task WriteAtomicAsync(string path, byte[] bytes, CancellationToken ct = default)
{
    var dir = Path.GetDirectoryName(path)!;
    var tmp = Path.Combine(dir, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
    await using (var fs = new FileStream(tmp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, FileOptions.WriteThrough | FileOptions.Asynchronous))
    {
        await fs.WriteAsync(bytes, ct);
        fs.Flush(flushToDisk: true);
    }
    for (var attempt = 1; ; attempt++)
    {
        try { File.Move(tmp, path, overwrite: true); return; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException && attempt < 5)
        {
            await Task.Delay(100 * attempt, ct); // transient lock (OneDrive, antivirus, indexer)
        }
        catch
        {
            TryDelete(tmp); // leave no temp files behind; the caller reports "Not saved"
            throw;
        }
    }
}
```

### Grid rebuild that keeps sort and selection
```csharp
var sortColumn = _gridFacts.SortedColumn;
var sortOrder = _gridFacts.SortOrder;
var selectedId = _gridFacts.CurrentRow?.Tag as string;
_gridFacts.Rows.Clear();
foreach (var f in guide.Facts)
{
    var i = _gridFacts.Rows.Add(f.Id, f.Date, f.Title, string.Join(", ", f.IssueIds), Names(f.ActorIds), Names(f.KnownByIds));
    _gridFacts.Rows[i].Tag = f.Id;
}
if (sortColumn != null && sortOrder != SortOrder.None)
    _gridFacts.Sort(sortColumn, sortOrder == SortOrder.Ascending ? ListSortDirection.Ascending : ListSortDirection.Descending);
SelectRowById(_gridFacts, selectedId);
```

### Word table (schema-ordered)
```csharp
var table = new Table(
    new TableProperties(
        new TableStyle { Val = "GuideTable" },
        new TableWidth { Width = "5000", Type = TableWidthUnitValues.Pct },
        new TableBorders(
            new TopBorder { Val = BorderValues.Single, Size = 4, Color = "BFBFBF" },
            new BottomBorder { Val = BorderValues.Single, Size = 4, Color = "BFBFBF" },
            new InsideHorizontalBorder { Val = BorderValues.Single, Size = 4, Color = "D9D9D9" }),
        new TableLayout { Type = TableLayoutValues.Fixed }),
    new TableGrid(widths.Select(w => new GridColumn { Width = w.ToString(CultureInfo.InvariantCulture) })));
var header = new TableRow(new TableRowProperties(new TableHeader()));   // repeats on each page
foreach (var (text, width) in headers.Zip(widths))
    header.Append(new TableCell(
        new TableCellProperties(new TableCellWidth { Width = width.ToString(CultureInfo.InvariantCulture), Type = TableWidthUnitValues.Dxa },
                                new Shading { Val = ShadingPatternValues.Clear, Fill = "F2F4F7" }),
        BoldParagraph(text)));   // every cell needs >= 1 paragraph
table.Append(header);
```

## Runtime State Inventory

Not a rename or migration phase. One related item: Phase 2 shipped **no** case files ("This version does not save the case yet"), so there is no stored case data to migrate. `FormatVersion` 1 is the baseline [VERIFIED: WizardForm.DiscardCaseMessage]. New runtime state this phase creates: `%LOCALAPPDATA%\ReelDiscovery\recent.json`, and `Documents\ReelDiscovery\Cases\*\case.json` + `.docx`.

## State of the Art

| Old Approach | Current Approach | When Changed | Impact |
|--------------|------------------|--------------|--------|
| JSON mode + "respond with JSON" prompts | Strict JSON-schema structured outputs on both providers | Phase 1 (this repo) | DTOs are the contract; no JSON instructions in prompts (`AssertNoJsonInstructions`) |
| Model-written world "bible" with model IDs (PoC) | Code-owned IDs + model-filled content | This phase | Stable references for Phases 4-5 |
| `.example` email domains (Phase 2 counsel field) | Derived `.com` org domains (D-14) | This phase | Update `CounselDto.Email` description; sync counsel email from the cast |
| "Close discards the case" prompt (Phase 2 C6) | Autosave, close without prompt (D-10) | This phase | Update `DiscardCaseMessage` / `HasUnsavedCase` and tests |

Anthropic structured outputs allow `minItems` only 0 or 1, and no numeric or string-length constraints [CITED: platform.claude.com structured-outputs]. So counts like "10 facts" go in prompt text and are checked in code, never in the schema.

## Assumptions Log

| # | Claim | Section | Risk if Wrong |
|---|-------|---------|---------------|
| A1 | Facts preset ranges Simple 12-20 / Standard 30-50 / Complex 70-110; background per org 3/6/10 (from D-13) | Patterns 3, 5 | Too few facts → thin Phase 4 schedule; too many → cost. Tunable constants |
| A2 | Counsel counts on top of `CharactersMin/Max` (the preset range applies to non-counsel key characters) | Pattern 3 | Validator warnings feel wrong; adjust rule |
| A3 | Individuals that are parties become cast members; personal email domain derived from surname (e.g. `schrutefarms.com` when a business is named, else `{last}family.com`) | Pattern 4 | Odd-looking addresses; user may prefer a webmail-style domain |
| A4 | Government agencies use a `.gov` domain derived from the agency name | Pattern 4 | Real agency domains appear (no mail is sent); alternative `.com` |
| A5 | Code-built signature block (model picks only a style) satisfies D-16 | Pattern 3 | User may want more varied, model-written signatures |
| A6 | Closing with a typed scenario but no core document still asks once (nothing saved yet per D-09) | Pattern 9 | Could annoy; alternative is a silent close |
| A7 | `List<T>` binding cannot header-sort; unbound grid recommended | Alternatives | Low; unbound works either way |
| A8 | XmlWriter rejects XML-invalid control characters | Pitfall 9 | Low; stripping is harmless either way |
| A9 | Opus 5.5 thinking tokens count against `max_tokens` (comment in `AnthropicTextProvider` says it emits a thinking block first) | Pitfall 1 | Batches may need to be smaller |
| A10 | OpenAI strict-mode limits (≈5,000 properties, nesting depth ≈10, ≈1,000 enum values) are far above these DTOs | Standard Stack | Very low; the DTOs are small |
| A11 | Time zone derived from the organization's US state (fixed table) is adequate for Phase 4 | Pattern 3 | Multi-state orgs get one zone; editable later |

## Open Questions (RESOLVED)

1. **Do counsel count toward the preset's key-character range?**
   - What we know: the core document prompt asked for "about N named individuals (officers, employees, counsel, agents...)". D-15 adds required counsel.
   - **RESOLVED:** counsel are added on top; the preset key range counts only Key characters with Role Party or Staff. Implemented in 03-08 (validator rule 18) and the roster prompt in 03-09 ("{min}-{max} key people who are not counsel").
2. **Email domains for individuals and government bodies** (A3, A4).
   - **RESOLVED:** individual parties get a personal-style `{last}family.com` domain (e.g. `walter.white@whitefamily.com`); government agencies get `.gov`; other organizations use their ShortName (else Name) + `.com`. Implemented in 03-03 (`EmailIdentity.DomainFor`) and used by 03-09 `SyncOrganizations`.
3. **Where does "change location" (D-09) live?**
   - **RESOLVED:** a "Move Case Folder..." button on Case Summary only (UI-SPEC U16); the folder path also shows in the Case Guide subtitle. `CaseStore.MoveFolder` (03-04) does `Directory.Move` on the same volume, copy + delete across volumes, and updates recent.json; the button and C7 confirmation are in 03-17.
4. **Should the Case Guide block Next on Errors?**
   - **RESOLVED:** yes, the same as Phase 2: Errors block (M2), unfinished parts block (M1), Warnings ask with default No (M3). Copy and decision logic in 03-06 (`CaseGuideUiText.NextGateDecision`), applied in 03-16 (`StepCaseGuide.ValidateStepAsync`).
5. **Issue enrichment call: required or optional?**
   - **RESOLVED:** included as one Story-tier call after deterministic extraction; on failure the issues still exist, `IssueDetailsState` is Failed and the Issues tab shows "Retry Issue Details". Implemented in 03-09 (`GenerateIssuesAsync`, `RetryIssueDetailsAsync`) and 03-12 (`IssuesTabPanel` tab action).

## Environment Availability

| Dependency | Required By | Available | Version | Fallback |
|------------|------------|-----------|---------|----------|
| .NET SDK | build/test | ✓ | 10.0.401 (global.json pin) | — |
| xUnit test project | validation | ✓ | 501 tests passing, ~1 s | — |
| Microsoft Word | UAT "opens in Word" (GUIDE-11) | ✓ | Office16 (`WINWORD.EXE` App Path) | `OpenXmlValidator` in tests |
| Documents folder | case folders (D-09) | ✓ | `C:\Users\ghand\OneDrive\Documents` (OneDrive-redirected) | Retry logic (Pitfall 6); user can move the folder |
| Display at 150% | 144-DPI render check (D-04) | ✓ | AppliedDPI = 144 | — |
| Live LLM key | manual UAT only | (user) | — | `FakeLlmProvider` for all automated tests |

**Missing dependencies with no fallback:** none.

## Validation Architecture

### Test Framework
| Property | Value |
|----------|-------|
| Framework | xUnit 2.9.3, Microsoft.NET.Test.Sdk 17.14.1, xunit.runner.visualstudio 3.1.4 |
| Config file | `ReelDiscovery.Tests/ReelDiscovery.Tests.csproj` (global `using Xunit`; `InternalsVisibleTo` from the app) |
| Quick run command | `dotnet test ReelDiscovery.Tests/ReelDiscovery.Tests.csproj --filter "FullyQualifiedName~ReelDiscovery.Tests.Case&Category!=Render"` |
| Full suite command | `dotnet test ReelDiscovery.Tests/ReelDiscovery.Tests.csproj --filter "Category!=Render"` |
| Render check (UI tasks) | `dotnet test ReelDiscovery.Tests/ReelDiscovery.Tests.csproj --filter "Category=Render"` → PNGs under `ReelDiscovery.Tests/bin/Debug/net10.0-windows/renders/` for inspection |

### Phase Requirements → Test Map
| Req ID | Behavior | Test Type | Automated Command | File Exists? |
|--------|----------|-----------|-------------------|-------------|
| GUIDE-01 | Counts/findings → `ISS-nn`; IDs kept on re-extract; reorder matched by title; enrichment maps by ID | unit | `--filter "FullyQualifiedName~IssueExtractorTests"` | ❌ Wave 0 |
| GUIDE-02 | Roster + details DTOs → cast with all required fields; IDs `CH-nnn`; emails derived; required-field validator | unit (FakeLlmProvider) | `--filter "FullyQualifiedName~CaseGuideGeneratorTests\|FullyQualifiedName~EmailIdentityTests\|FullyQualifiedName~IdAllocatorTests"` | ❌ Wave 0 |
| GUIDE-03 | Prompt demands in-house + firm partner/associate; signing attorney seeded; coverage warning when missing | unit | `--filter "FullyQualifiedName~CaseGuideGeneratorTests\|FullyQualifiedName~CaseGuideValidatorTests"` | ❌ Wave 0 |
| GUIDE-04 | One background call per org, count = preset; `Tier = Background` | unit | `--filter "FullyQualifiedName~CaseGuideGeneratorTests"` | ❌ Wave 0 |
| GUIDE-05 | One call per FactPlan section; unknown IDs dropped + noted; KnownBy ⊇ actors; merge in date order; no list mutation in parallel | unit | `--filter "FullyQualifiedName~CaseGuideGeneratorTests"` | ❌ Wave 0 |
| GUIDE-06 | Values mapped with `V-nnn`; scanner finds `$` amounts / CO numbers; duplicate-label Error | unit | `--filter "FullyQualifiedName~CanonicalValueScannerTests\|FullyQualifiedName~CaseGuideValidatorTests"` | ❌ Wave 0 |
| GUIDE-07 | Each rule in the table fires; `Validate` never throws on nulls / empty / malformed dates | unit | `--filter "FullyQualifiedName~CaseGuideValidatorTests"` | ❌ Wave 0 |
| GUIDE-08 | Edit sets `IsUserEdited`; tab regen keeps hand edits + matched IDs; single-entry regen keeps ID and old text on failure; delete lists referencing facts | unit | `--filter "FullyQualifiedName~CaseGuideEditingTests\|FullyQualifiedName~CaseGuideGeneratorTests"` | ❌ Wave 0 |
| GUIDE-09 | Core-doc edit → Issues/Cast stale; revert → fresh; cast rename → facts NeedsReview + name replaced; Update guide re-runs only stale parts in order | unit | `--filter "FullyQualifiedName~GuideStalenessTests"` | ❌ Wave 0 |
| GUIDE-10 | Save/load round trip in a temp dir; no temp files left; newer `FormatVersion` refused; null lists normalized; no API key / provider in JSON; folder naming (trailing dot, reserved names, uniqueness); recent list cap + prune; Stage → step type mapping | unit | `--filter "FullyQualifiedName~CaseStoreTests\|FullyQualifiedName~CaseFolderNamingTests\|FullyQualifiedName~WizardFlowTests"` | ❌ Wave 0 (WizardFlowTests exists, needs update) |
| GUIDE-11 | Three exports open with `WordprocessingDocument.Open`, **zero** `OpenXmlValidator` errors, contain heading text + table rows; control chars stripped | unit | `--filter "FullyQualifiedName~CaseGuideDocxExporterTests"` | ❌ Wave 0 |
| (cross) | All new DTO schemas strict, ≤16 unions | unit | `--filter "FullyQualifiedName~CaseSchemaTests"` | ✅ (extend theory data) |
| (cross) | Prompts keep scenario names (D-01), firm position (D-02), no JSON instructions | unit | `--filter "FullyQualifiedName~CaseGuideGeneratorTests"` | ❌ Wave 0 |
| (UI) | Pure UI helpers (tab captions, stale banner text, grid row text, Next gate decisions) | unit | `--filter "FullyQualifiedName~CaseGuideUiLogicTests"` | ❌ Wave 0 |
| (UI) | Case Guide / Case Summary / Describe-the-Case (with recent list) render at 144 DPI; clipping detector finds no Label/Button whose `PreferredSize` exceeds its bounds (AutoEllipsis excluded) | render + human inspection | `--filter "Category=Render"` | ❌ Wave 0 |
| (manual) | Live run with Claude and OpenAI on "The Office"-inspired scenario: names kept, `michael.scott@dundermifflin.com`; exports open in Word; reopen lands on the furthest step | manual UAT | — | — |

### Sampling Rate
- **Per task commit:** quick run (Case tests, about 1-2 s).
- **Per wave merge:** full suite minus Render.
- **UI tasks:** Render tests + inspection of the PNGs before hand-off (D-04).
- **Phase gate:** full suite green + render PNGs inspected + manual UAT items recorded, before `/gsd-verify-work`.

### Wave 0 Gaps
- [ ] `ReelDiscovery.Tests/Case/CaseTestData.cs`: add `SampleGuidedDraft()` (core doc + issues + organizations + cast + facts + values) and canned DTO JSON builders for roster / details / background / facts / values.
- [ ] `ReelDiscovery.Tests/Render/RenderHarness.cs`: run on an STA thread. Call `SetProcessDpiAwarenessContext(-4)` (P/Invoke user32) before creating any window; if `DeviceDpi != 144`, skip with a message. Host the step in a `Form` with the wizard's content padding at 1100×750 logical, bind a sample `WizardState`, `CreateControl()`, `PerformLayout()`, `DrawToBitmap` to PNG. Add a `ClippingDetector` that walks controls (note: `DrawToBitmap` draws container children in reverse order and does not render `RichTextBox` content [CITED: learn.microsoft.com Control.DrawToBitmap]). No such harness is committed today [VERIFIED: grep found none].
- [ ] New test files listed in the map above.
- [ ] Update `WizardFlowTests` (5-step case list, Finish on Case Summary, close without discard prompt), and `CaseSchemaTests` theory data.
- [ ] Update `ComplexityPresetTests.RangeNames` with "Facts" (strictly increasing) and add a test for `BackgroundPerOrg` 3/6/10.

## Security Domain

Config: `security_enforcement: true`, ASVS level 1, block on high.

### Applicable ASVS Categories
| ASVS Category | Applies | Standard Control |
|---------------|---------|-----------------|
| V2 Authentication | no | No auth surface added |
| V3 Session Management | no | — |
| V4 Access Control | no | Single-user desktop; files under the user profile |
| V5 Input Validation | yes | case.json from disk is untrusted input: size cap, `FormatVersion` check, System.Text.Json without polymorphic type handling, `Normalize()`, validator never throws. LLM output only through strict DTOs; IDs/dates checked in code; regexes with timeouts |
| V6 Cryptography | no (hash only) | SHA-256 via `CaseDraft.ComputeHash` for staleness only, not security |
| V8 Data Protection | yes | case.json / recent.json / exports never contain API keys, provider or model IDs (test). Keys stay in the DPAPI `ApiKeyStore` |
| V12 Files and Resources | yes | Folder names sanitized (invalid chars, trailing dots, reserved device names, length), always combined under a chosen root (no `..` traversal from LLM-derived titles). "Open case folder" launches `explorer.exe` with a verified existing **directory** only, never a file path from case.json. Temp files cleaned up |
| V14 Configuration | yes | recent.json written atomically; paths pruned if missing |

### Known Threat Patterns
| Pattern | STRIDE | Standard Mitigation |
|---------|--------|---------------------|
| Path traversal / reserved names via LLM-derived case title (`..\`, `CON`, trailing dot) | Tampering | `CaseFolderNaming.Sanitize` + `Path.GetFullPath` check that the result stays under the Cases root |
| Malicious or corrupt case.json (huge file, deep nesting, nulls) | DoS / Tampering | 50 MB cap, STJ default `MaxDepth` 64, `Normalize()`, validator null-safety; load errors shown, never crash |
| Shell execution of a crafted path from case.json / recent.json | Elevation | Only `Process.Start("explorer.exe", quotedDirectory)` after `Directory.Exists`. Never `UseShellExecute` on file paths from data |
| Prompt injection through scenario / core-document text into guide prompts | Tampering | Low impact (user attacks own output). Text only in user prompts, never in the byte-stable system prompt; strict schemas limit output shape; code validates every ID |
| Secrets written to the case folder (OneDrive-synced!) | Information disclosure | Test asserts serialized case JSON contains no key-like strings or `ApiKey` properties; `WizardState` is never serialized |
| XML-invalid characters producing corrupt .docx | DoS (of export) | Strip before writing (Pitfall 9) |

## Sources

### Primary (HIGH confidence)
- Codebase (read in this session): `Models/Case/*`, `Services/Case/CoreDocumentGenerator.cs`, `CoreDocumentValidator.cs`, `CoreDocumentEditing.cs`, `CoreDocumentAssembler.cs`, `CoreDocumentTemplates.cs`, `Services/Ai/*` (gateway, schema registry, Anthropic cap 21,333), `UserControls/StepCoreDocument.cs`, `StepCaseScenario.cs`, `Forms/WizardForm.cs`, `Helpers/Theme.cs`, `ThemedButton.cs`, `CardPanel.cs`, `ButtonHelper.cs`, `Services/OfficeDocumentService.cs`, `Services/ApiKeyStore.cs`, `ReelDiscovery.Tests/**` (FakeLlmProvider, StrictSchemaValidator, DocumentRegressionTests, WizardFlowTests, ComplexityPresetTests)
- `dotnet test` run: 501 passed, ~1 s (2026-10-01)
- Environment probes: SDK 10.0.401, Word installed, Documents = OneDrive path, AppliedDPI = 144
- https://platform.claude.com/docs/en/build-with-claude/structured-outputs: 20 strict tools, 24 optional params, 16 union-typed params per request; `minItems` 0/1 only; no numeric/string constraints; no recursion
- https://learn.microsoft.com/en-us/dotnet/api/system.windows.forms.control.drawtobitmap: DrawToBitmap limitations

### Secondary (MEDIUM confidence)
- Phase 2 artifacts: 02-RESEARCH.md (sectioned generation, strict-schema rules), 02-REVIEW.md (CR-01/02, WR-04/07/08/09, IN-03), commit 113e0c3 (144-DPI fixes)
- User memory notes on the earlier case-bible PoC (code-appended signatures avoid drift; counsel excluded from non-privileged threads; anachronism leaks when all facts are dumped into prompts). These inform the cast/fact fields Phase 5 needs

### Tertiary (LOW confidence)
- OpenAI strict-mode numeric limits (A10). The official page fetched did not state them.

## Metadata

**Confidence breakdown:**
- Standard stack: HIGH. Everything is already in the repo and verified.
- Architecture: HIGH for integration points, ID/staleness/persistence design. MEDIUM for batch sizes and preset numbers (tunable constants).
- Pitfalls: HIGH. Most come from Phase 2 review findings, verified environment facts (OneDrive, 144 DPI) or official docs.

**Research date:** 2026-10-01
**Valid until:** 2026-10-31 (stable; recheck provider structured-output limits if the gateway or models change)
