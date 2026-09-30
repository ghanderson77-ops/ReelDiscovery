# Phase 2: Case Scenario & Core Document - Research

**Researched:** 2026-09-30
**Domain:** LLM structured long-form legal document generation (Complaint / Petition / Investigation Memo) inside a .NET 10 WinForms wizard, via the Phase 1 provider gateway
**Confidence:** MEDIUM-HIGH (codebase integration HIGH; legal anatomy MEDIUM-HIGH; preset numbers and some UX choices are ASSUMED defaults)

## Summary

Phase 1 left a solid foundation: `ILlmGateway.GenerateAsync<T>(LlmRequest)` gives strict JSON-schema output on both OpenAI and Anthropic. It classifies outcomes (Succeeded / Refused / Truncated / Invalid / Failed), resolves the **Story** tier to the strong model, and records token usage into the live cost ticker. Phase 2 needs **no new NuGet packages**. It is pure application code: a case-draft domain model, three LLM-backed services (real-entity detector, outline generator, section generator), deterministic code for assembly and validation, and two new wizard steps.

The main design decision: **the LLM writes section content as structured paragraph arrays, and code owns the legal skeleton.** Code renders the caption, paragraph numbering, count incorporation paragraphs ("realleges paragraphs 1 through N"), prayer lettering, jury demand, signature block and memo header from structured fields. Paragraph numbering stays correct after any edit or section regeneration. Jurisdiction stays consistent because it is rendered from one `Jurisdiction` object, not re-invented in each section. Phase 3 can extract issues, cast and facts from structured sections instead of re-parsing prose. The PITFALLS research recommends this directly: "keep the LLM filling sections rather than inventing the format" and "consecutive paragraph numbering must be enforced in code".

Generation is **outline first, then section by section**. This is not optional. Anthropic non-streaming requests are capped at `MaxNonStreamingTokens = 21_333` in `AnthropicTextProvider`, and a Complex complaint (about 200+ numbered paragraphs) is around 20-30K output tokens. One call would truncate. Sectioning also delivers CASE-07's "regenerate by section". Citation safety (CASE-06) uses a prompt rule plus a deterministic `CitationScanner` (regex over reporter / WL / LEXIS / "v." / Id. / supra patterns). There is no maintained .NET citation library, so a curated regex is the right call. Real-name handling (CASE-03) uses an LLM pre-flight that returns the entities plus suggested fictional substitutes. Substitution runs **deterministically in code** as a single-pass, word-boundary replacement. A post-generation leak scan confirms that no original name survived.

**Primary recommendation:** Build `Models/Case/*` (mutable POCOs, JSON-round-trippable for Phase 3's case.json) plus `Services/Case/{RealEntityDetector, CoreDocumentGenerator, CoreDocumentAssembler, CitationScanner, NameSubstitution, CoreDocumentValidator}`. Add `StepCaseScenario` and `StepCoreDocument` to a new "case-driven" step list in `WizardForm`, alongside the legacy flow. All LLM calls use `LlmTier.Story` and small per-section DTOs.

## User Constraints

No `02-CONTEXT.md` exists (auto mode; discuss-phase skipped). The constraints below are inherited locked decisions from Phase 1 CONTEXT, PROJECT.md, CLAUDE.md and the user's recorded feedback. Treat them as locked.

### Locked (inherited)
- **D-10 (Phase 1):** The core document is "Story" tier work. Every generator call declares `LlmTier.Story`, and the gateway resolves it to the selected provider's Story model (Claude Opus 5.5 / `gpt-6.1-sol` effort low).
- **D-11 (Phase 1):** One text provider per run. All Phase 2 calls go through `WizardState.CreateGateway()`. There are no direct SDK calls.
- **PROV-03/04 (Phase 1):** Strict JSON-schema DTOs only (no `[Range]`/`[StringLength]`/`[RegularExpression]`). Refusals and spend-limit failures are not retried blindly.
- **PROJECT.md:** The case-driven flow will replace the old topic/storyline wizard, but removal is Phase 7 (OUT-03/PLAT-05). **The legacy flow must keep working through Phase 2.**
- **PROJECT.md Out of Scope:** "Jurisdiction-perfect local rules for every court". Use one consistent jurisdiction per case.
- **REQUIREMENTS v2:** Indictment / criminal-complaint affidavit is **LEGAL-01 (v2)**. Criminal scenarios use the Investigation Memo in v1.
- **User feedback (memory, 2026-07-16):** Everything is generated from a short prompt, with no hand-authored inputs. LLM prose is the only user-facing mode. Legal documents must be **scenario-driven, never hardcoded to one case**. (The stashed PoC `LegalDocumentGenerator` hard-coded a construction complaint. Do not repeat that.)
- **CLAUDE.md conventions:** mutable classes with auto-properties (**no `record` types**, no primary constructors, no `#region`); UI built in code with `TableLayoutPanel` and row-index comments; `_camelCase` fields; `ct` last; `[GeneratedRegex]` instead of `new Regex(...)`; `Random.Shared`; catch `OperationCanceledException` before `Exception`; `IProgress<string>` for progress; private nested `*Dto` classes inside the generator.

### Claude's Discretion (recommended defaults, flagged as ASSUMED where unverified)
- Complexity preset numbers (see Presets table)
- How the case-driven flow coexists with the legacy flow in `WizardForm`
- Editor UX: section list plus a plain-text editor per section, instead of a rich-text whole-document editor
- Whether a draft snapshot is saved to disk in Phase 2 (full case.json is GUIDE-10, Phase 3)

### Deferred (OUT OF SCOPE for Phase 2)
- Issues/cast/facts extraction, canonical registry, stale-marking of downstream artifacts (Phase 3: GUIDE-01..11)
- .docx export of the core document (GUIDE-11, Phase 3)
- Indictment / affidavit document types (LEGAL-01, v2)
- Prompt caching / batch (Phase 7)

<phase_requirements>
## Phase Requirements

| ID | Description | Research Support |
|----|-------------|------------------|
| CASE-01 | Free-text real-life scenario seeds a case | `StepCaseScenario` multiline input; `CaseDraft.Scenario`; length cap and non-empty validation (Architecture: Pattern 1, Security V5) |
| CASE-02 | Simple/Standard/Complex preset sets parties, issues/counts, characters, timeline span, doc length | `ComplexityPreset` table (Presets section); preset values injected into the outline prompt; `CoreDocumentValidator` checks the outline against the preset ranges; the UI shows a live preset summary |
| CASE-03 | Detect real people/companies, warn, substitute fictional names by default, allow override | `RealEntityDetector` (LLM, strict DTO) + `EntityReviewDialog` (checkbox default = substitute) + `NameSubstitution` (deterministic, single-pass) + post-generation leak scan (Pattern 3) |
| CASE-04 | Complaint/Petition for civil; Investigation Memo for criminal/regulatory | The outline call classifies `MatterKind` → `CoreDocumentType`; a user override combo; `DocumentTypeResolver` pure function (Pattern 2) |
| CASE-05 | Realistic structure for one chosen jurisdiction | Code-owned skeleton (`CoreDocumentAssembler`): caption, numbered paragraphs, incorporation, prayer, jury demand, signature / memo header, all rendered from one `Jurisdiction`; per-type required-section templates (Legal Anatomy section) |
| CASE-06 | No invented case law; automatic check | Prompt rule + `CitationScanner` (`[GeneratedRegex]`); auto-regenerate a flagged section once with the violation list; remaining flags block Next (Pattern 4, Code Examples) |
| CASE-07 | Review, edit, regenerate (whole or by section) | `StepCoreDocument`: section list, per-section editor, "Regenerate section" with optional guidance, "Regenerate all"; numbering recomputed by code after every change (Pattern 5) |
</phase_requirements>

## Architectural Responsibility Map

This is a single-process desktop app, so the "tiers" are the app's layers.

| Capability | Primary Tier | Secondary Tier | Rationale |
|------------|-------------|----------------|-----------|
| Scenario/preset/jurisdiction input | UI (`UserControls/StepCaseScenario`) | Models (`CaseDraft`) | Input collection only. State is written in `ValidateStepAsync` per convention |
| Real-entity detection | Services (`RealEntityDetector`, LLM) | UI (`EntityReviewDialog`) | Needs world knowledge (LLM). The user decides overrides |
| Name substitution and leak scan | Services / pure code (`NameSubstitution`) | — | Must be deterministic and unit-testable. Never trust the LLM to "remember" substitutions |
| Doc-type classification | Services (outline LLM call) | Pure code (`DocumentTypeResolver`) | The LLM classifies civil vs criminal/regulatory. Code applies user override and consistency rules |
| Section prose | Services (`CoreDocumentGenerator`, LLM, Story tier) | — | Creative content |
| Legal skeleton (caption, numbering, incorporation, prayer letters, signature, memo header) | Pure code (`CoreDocumentAssembler`) | — | Must stay correct after edits. The LLM is unreliable at numbering and consistency |
| Citation check, structure and preset check | Pure code (`CitationScanner`, `CoreDocumentValidator`) | UI (issues panel) | Deterministic, testable, runs on every edit |
| Review/edit/regenerate | UI (`StepCoreDocument`) | Services | Thin UI over the services |
| Persistence of the draft | Models (in-memory `WizardState.Case`) | (Phase 3: case.json) | Phase 1 built **no** run store (verified: nothing under `Services/` persists runs). Keep the models JSON-serializable now |

## Standard Stack

### Core (all already referenced; no additions)
| Library | Version | Purpose | Why Standard |
|---------|---------|---------|--------------|
| Phase 1 `ILlmGateway` / `LlmGateway` | in-repo | Strict structured output, tier resolution, outcome classification, usage tracking | Locked by PROV-03. Every text call goes through it [VERIFIED: Services/Ai/LlmGateway.cs] |
| `Microsoft.Extensions.AI.Abstractions` | 10.10.1 | Schema generation for DTOs (via `JsonSchemaRegistry`) | Already used. Emits strict schemas (`additionalProperties:false`, all required) [VERIFIED: ReelDiscovery.csproj, JsonSchemaRegistry.cs] |
| `System.Text.Json` | inbox (.NET 10) | DTO deserialization, `CaseDraft` round-trip | Already the project standard |
| `System.Text.RegularExpressions` `[GeneratedRegex]` | inbox | Citation scanner, name substitution helpers | CLAUDE.md mandates source-generated regex |
| WinForms (`TextBox`, `ListBox`, `DataGridView`, `SplitContainer`) | .NET 10 | Scenario and document-review UI | Existing UI stack |
| xUnit | 2.9.3 (+ runner 3.1.4, Test SDK 17.14.1) | Unit tests | Existing test project [VERIFIED: ReelDiscovery.Tests.csproj] |

### Supporting
| Item | Purpose | When to Use |
|------|---------|-------------|
| `ReelDiscovery.Tests/Fakes/FakeLlmProvider` | Scripted LLM responses, captured prompts | All generator tests (outline, sections, detector, retry-on-citation) |
| `ReelDiscovery.Tests/Fakes/StrictSchemaValidator` | Asserts every new DTO schema is strict and has **≤16 union types** (Anthropic limit) | One theory test over `CoreDocumentGenerator.SchemaTypes` + `RealEntityDetector.SchemaTypes` |
| `Helpers/LoadingOverlay` | Busy overlay | Add a `LoadingType.CoreDocument` (and messages). It currently has only `Storylines`, `Characters` [VERIFIED] |
| `Helpers/ButtonHelper` | Styled buttons | All new buttons |

### Alternatives Considered
| Instead of | Could Use | Tradeoff |
|------------|-----------|----------|
| Hand-rolled citation regex | eyecite (Python, BSD, reporters-db trained on 55M citations) | Most accurate, but Python-only. No .NET port found [CITED: github.com/freelawproject/eyecite; VERIFIED via web search: no NuGet equivalent]. Recommended: mirror the reporter list in a C# regex |
| LLM entity detection | Hard-coded famous-name lists / NER models (ML.NET, ONNX) | Lists cannot cover arbitrary real companies. ONNX NER adds 100+ MB to the single-file exe and cannot tell "real" from "fictional" anyway. LLM is the right tool |
| Structured section editor | `RichTextBox` whole-document editor | PITFALLS: "Reviewing a 30-page complaint in a TextBox: edits get lost". Whole-doc editing breaks the structured model and renumbering |
| MigraDoc PDF (stashed PoC) | — | Not needed. Phase 2 has no file export; Phase 3 exports .docx via the existing OpenXml builder. Do **not** add PDFsharp-MigraDoc |

**Installation:** none. No new packages.

## Reuse Audit: Stashed Case Bible PoC

`git stash list` shows `stash@{0}: On main: Case Bible work (removed 2026-09-29)` with `Models/CaseBible/CaseBibleModels.cs`, `Services/CaseBible/*` (CaseBibleGenerator, LegalDocumentGenerator, ...), `Services/PdfDocumentService.cs` and `Forms/CaseBibleForm.cs` [VERIFIED: git stash show].

| Stashed item | Reusable? | Why |
|--------------|-----------|-----|
| `LegalDocumentGenerator` | **No** | Hard-coded Meridian construction complaint and §558 notice. Violates the user's "scenario-driven, never hardcoded" feedback. Emits PDF via MigraDoc |
| `CaseBibleGenerator` | **Ideas only** | Staged "world then evidence" generation is the same shape as outline then sections. It calls the deleted `OpenAIService.GetJsonCompletionAsync`, and its DTOs are not strict-schema safe |
| `CaseBibleModels` (`CaseOrganization`, `PartyAlignment`, `Custodian`...) | **Phase 3 inspiration** | Cast/timeline modeling belongs to Phase 3. `PartyAlignment` is a useful enum name for party roles |
| `PdfDocumentService` / MigraDoc | **No** | Adds a dependency. .docx export is Phase 3 |

Recommendation: **do not `git stash pop`.** Reference it read-only with `git show stash@{0}:<path>` if useful.

## Architecture Patterns

### System Architecture Diagram

```
 User scenario text + preset + (optional) jurisdiction/doc-type preference
        │
        ▼
 [StepCaseScenario.ValidateStepAsync]
        │  scenario hash changed?
        ▼
 RealEntityDetector (LLM, Story) ──► entities[] {name, aliases, kind, substitute}
        │
        ▼
 EntityReviewDialog (user: keep substitute ✓ default / override ✗ / edit substitute)
        │
        ▼
 NameSubstitution.Apply(scenario, accepted map) ──► EffectiveScenario  (code, single pass)
        │
        ▼
 [StepCoreDocument.OnEnterStepAsync]  (no document yet, or scenario/preset changed)
        │
        ▼
 CoreDocumentGenerator.GenerateOutlineAsync (LLM, Story)
   input: EffectiveScenario + ComplexityPreset numbers + jurisdiction/doc-type preference
   output: OutlineDto {matterKind, docType, jurisdiction, parties[], counts[]/issues[],
                       timelineStart/End, factSubsections[], memoHeader, counsel}
        │
        ▼
 DocumentTypeResolver + CoreDocumentValidator.ValidateOutline (preset ranges, dates parse)
        │
        ▼
 For each section spec in the type template (sequential; counts may run ≤3 in parallel):
   CoreDocumentGenerator.GenerateSectionAsync (LLM, Story)
     context: outline JSON + prior generated sections text + section spec
     output: SectionDto {paragraphs[] | subsections[]}
        │
        ├──► CitationScanner.Scan(section) ── hits? ── regenerate once with violation list
        │
        ▼
 CoreDocument (structured sections)  ──►  CoreDocumentAssembler.Render()  (code: caption,
        │                                   numbering, incorporation, prayer letters,
        │                                   jury demand, signature / memo header)
        ▼
 CoreDocumentValidator.Validate (structure, preset, jurisdiction consistency,
                                 citation scan, real-name leak scan)
        │
        ▼
 StepCoreDocument UI: section list │ section editor │ issues panel │ full preview
   edit ──► re-split paragraphs ──► re-validate ──► Revision++
   regenerate section (optional guidance) ──► GenerateSectionAsync ──► replace in place
   regenerate all ──► confirm ──► outline + sections again
        │
        ▼
 WizardState.Case (CaseDraft)  ──► Phase 3 (issues/cast/facts extraction, case.json)
```

### Recommended Project Structure
```
Models/Case/
├── CaseDraft.cs              # Scenario, EffectiveScenario, Preset, Substitutions, preferences, CoreDocument, Revision
├── ComplexityPreset.cs       # ComplexityLevel enum + preset table (static Get(level))
├── Jurisdiction.cs           # CourtSystem enum, State, CourtName, District/County, VenueText
├── CoreDocument.cs           # CoreDocumentType, parties, counts outline, sections, memo header, signature
├── CoreDocumentSection.cs    # SectionKind enum, Id, Heading, Paragraphs/Subsections, flags
└── EntitySubstitution.cs     # Original, Aliases, Kind, Substitute, Apply (bool)
Services/Case/
├── RealEntityDetector.cs     # LLM pre-flight (+ private DTOs, SchemaTypes)
├── CoreDocumentGenerator.cs  # Outline + section calls, prompts, citation retry (+ private DTOs, SchemaTypes)
├── CoreDocumentTemplates.cs  # Required section list per CoreDocumentType (pure)
├── CoreDocumentAssembler.cs  # Pure rendering: numbering, caption, incorporation, prayer, signature
├── CoreDocumentValidator.cs  # Pure checks: structure, preset ranges, jurisdiction, dates
├── CitationScanner.cs        # Pure [GeneratedRegex] scanner → CitationFinding[]
├── NameSubstitution.cs       # Pure single-pass replace + leak scan
└── DocumentTypeResolver.cs   # Pure: matterKind + preference + court system → doc type
UserControls/
├── StepCaseScenario.cs
├── StepCoreDocument.cs
└── EntityReviewDialog.cs     # Modal DataGridView (a *Dialog per naming convention)
ReelDiscovery.Tests/Case/
├── CitationScannerTests.cs, NameSubstitutionTests.cs, CoreDocumentAssemblerTests.cs,
├── CoreDocumentValidatorTests.cs, ComplexityPresetTests.cs, DocumentTypeResolverTests.cs,
├── CoreDocumentGeneratorTests.cs, RealEntityDetectorTests.cs, CaseDraftSerializationTests.cs
```
Namespaces follow folders: `ReelDiscovery.Models.Case` and `ReelDiscovery.Services.Case`. The `Services/Ai` precedent shows subfolders are acceptable. Watch for a name clash: the type `Case` should not exist (`case` is a keyword, and a `Case` namespace segment is fine in C#). If it causes friction, name the folders `CaseFile`/`CaseDraft`. [ASSUMED]

### Pattern 1: Wizard flow coexistence (legacy + case-driven)
**What:** `WizardForm` keeps `StepApiConfiguration` as step 0, then switches the remaining step list by a `WizardState.Flow` (`CaseDriven` | `Legacy`). The keys screen (or a small radio group under it) picks the flow. `LoadSteps()` currently builds a fixed 7-step list up front [VERIFIED: Forms/WizardForm.cs]. Refactor it into `BuildSteps(flow)`, which hides and disposes the non-API steps and adds the flow's steps. `ShowStep` already uses `_steps.Count` for "Step X of N", so dynamic lists work.
**Case-driven list in Phase 2:** `[StepApiConfiguration, StepCaseScenario, StepCoreDocument]`. Phase 3 appends guide steps. The last step shows "Finish" (existing behavior closes the form). Add a confirm on Finish in the case flow because nothing is persisted yet (see Open Questions).
**Default flow:** `CaseDriven` [ASSUMED]. It is the product direction and needed for Phase 2 UAT. Legacy stays selectable until Phase 7.

### Pattern 2: Outline-first, code-owned skeleton
**What:** Call 1 (outline) fixes every structural decision: document type, jurisdiction, parties (fictional names, entity type, domicile, role), counts/issues (title, against which parties, legal basis described by statute or common-law theory, **no case citations**), timeline start/end (`yyyy-MM-dd` strings), factual subsection plan (heading, date range, paragraph target), and counsel/memo-author identities. Section calls then fill prose paragraphs only.
**Why:** Consistency across sections (same party names, one jurisdiction), predictable length per preset, and the ability to regenerate one section without drift.
**Rules:**
- Section DTOs return `paragraphs: string[]` with **no leading numbers** and **no cross-references by paragraph number**. Code numbers them. Strip any leading `^\s*\d+\.\s+` the model adds anyway.
- The count incorporation paragraph is generated by code: `"{PartyLabel} repeats and realleges paragraphs 1 through {lastFactParagraph} as if fully set forth herein."` It is recomputed on every render.
- The caption, "Case No." placeholder, jury demand and signature block are rendered by code from outline fields. Phone numbers use the `555-01xx` fictional range [CITED: PITFALLS.md Pitfall 12]. **No judge name** in the caption (avoids colliding with real judges) [CITED: PITFALLS.md Pitfall 11].
- `DocumentTypeResolver`: user preference wins. Otherwise `MatterKind.Criminal|Regulatory` → `InvestigationMemo`, and `Civil` → `Complaint`. Use `Petition` only when the outline chose a state court and returned `usesPetitionTerminology = true` (for example Texas "Plaintiff's Original Petition") [CITED: .planning/research/FEATURES.md §A, MEDIUM]. Federal court always → `Complaint`.

### Pattern 3: Real-entity pre-flight + deterministic substitution
**What:** One Story-tier call over the scenario text returns `entities[]` with `name`, `aliases[]` (surname only, nicknames, short company forms), `kind` (`Person` | `Company` | `Brand` | `FictionalWorkCharacter` | `FictionalWorkOrganization`), `reason`, `suggestedSubstitute`.
- **Exclude** geographic places, government agencies/courts (FBI, DEA, SEC, "U.S. District Court") and statutes. Investigation memos and captions need them to be real. [ASSUMED policy; flag for user]
- **Include fictional-work names** ("Walter White", "Los Pollos Hermanos") under `FictionalWork*` kinds, substituted by default. The PITFALLS research flags copyright/trademark risk for the "Breaking Bad" example [CITED: PITFALLS.md Pitfall 12, LOW confidence on degree of risk].
- `EntityReviewDialog`: a warning banner, then a grid with **[Use fictional name ✓ (default)] | Name found | Type | Substitute (editable)**. OK applies. Unchecking a row is the override (CASE-03) and records `Apply = false`. Do not re-call the detector if the scenario text hash is unchanged.
- `NameSubstitution.Apply`: build one alternation regex from all accepted originals **and** aliases, longest first, `Regex.Escape`d, with letter/digit lookarounds for word boundaries. Single-token aliases (surnames) match **case-sensitively** so "White" the name is replaced but "white paper" is not. Multi-token names match case-insensitively. Replace in one pass through a dictionary so substitutes are never re-substituted (A→B, B→C chains). Preserve possessives naturally (`Walter White's` → `Hank Brennan's`).
- The substitution map is also passed to the outline prompt ("Use these names: …; never use: …"), and **after generation** `NameSubstitution.FindLeaks(documentText, map)` flags any original name or alias in the output. It shows as an error in the issues panel.

### Pattern 4: Citation policy = prompt rule + scanner + one auto-retry
**What:** The system prompt states: "Do not cite any court decisions, case names or reporter citations. You may cite statutes, regulations and court rules by section number. Describe legal standards in plain words." `CitationScanner.Scan(text)` returns findings with a severity:
- **Error:** reporter citations (`123 F.3d 456`, `531 U.S. 98`, `45 Cal. Rptr. 3d 12`...), Westlaw/Lexis (`2019 WL 1234567`, `2020 U.S. Dist. LEXIS 12345`).
- **Warning:** bare case names `X v. Y` that are **not** this document's own caption (compare normalized party names), and signals `Id. at`, `supra`, `See, e.g.,`.
- **Not flagged:** statutes and rules (`28 U.S.C. § 1332`, `Fla. Stat. § 553.84`, `Fed. R. Civ. P. 38`). The reporter pattern requires `U.S.` followed by whitespace and a page number, so `U.S.C.` never matches.
**Flow:** after each section call, scan. On Error findings, call the section again **once** with the violation list appended ("Remove these citations: …"). If findings remain, show them in the issues panel. **Error findings block Next** (message box). Warnings ask Yes/No. The scanner also runs on every user edit, so CASE-06 holds for user text too.

### Pattern 5: Section editing and regeneration
- `CoreDocumentSection` has a stable `Id` (for example `intro`, `parties`, `jurisdiction`, `facts-1..n`, `count-1..n`, `prayer`; memo: `exec-summary`, `background`, `scope`, `key-individuals`, `chronology`, `finding-1..n`, `legal-analysis`, `recommendations`). **Regeneration keeps the Id** (Phase 3 traceability).
- The editor shows the section's paragraphs **without numbers**, separated by blank lines. On `Leave`/"Apply edits", split on `\r?\n\s*\r?\n`, trim, drop empties, set `IsUserEdited = true`, increment `CaseDraft.Revision` (Phase 3's GUIDE-09 stale-marking keys off it), then re-run validators.
- "Regenerate section" sends the outline, **all other sections' current text** (including user edits, so the regeneration stays consistent with them), the current text of this section and optional user guidance. It confirms first if `IsUserEdited`.
- "Regenerate all" confirms that edits will be discarded, then re-runs outline + sections.
- "Full preview" shows `CoreDocumentAssembler.RenderPlainText()` in a read-only `TextBox` (numbered, caption, and so on).
- Generation is cancellable (`CancellationTokenSource`, Cancel button). A fatal gateway result (`IsFatal`) stops generation. Other failures mark that section `Failed` with the message and a Retry option, so completed sections are kept.

### Pattern 6: Generation order and parallelism
Outline → intro → parties → jurisdiction/venue (Complaint/Petition) → fact subsections **sequentially**, each seeing prior sections → counts (**parallel, `SemaphoreSlim(3)`**, each sees all facts) → prayer (sees counts). Memo: exec summary is generated **last** (it summarizes findings), even though it renders first. Report progress with `IProgress<string>` ("Writing Count II of IV…").

### Anti-Patterns to Avoid
- **One giant call for the whole document.** It truncates on Anthropic (21,333 cap) and makes section regeneration impossible.
- **Letting the LLM number paragraphs or write "paragraphs 1 through 87".** The numbers break after the first edit.
- **Free-text whole-document editing** that later needs re-parsing into sections.
- **Nullable DTO properties.** Each `string?` becomes a JSON-schema union type, and Anthropic allows at most 16 [VERIFIED: StrictSchemaValidator.CountUnionTypes comment + LegacyGeneratorPortTests]. Use empty-string sentinels.
- **`DateTime`/`DateOnly` in DTOs.** Use `string` `yyyy-MM-dd` and parse with `DateTime.TryParseExact(..., CultureInfo.InvariantCulture)`. Culture-dependent date parsing was a known bug class in the legacy code [CITED: ARCHITECTURE.md "removes culture-dependent date parsing"].
- **Recursive DTOs** (subsections containing subsections). Strict structured outputs reject recursive schemas [CITED: PITFALLS.md Pitfall 14]. Keep exactly two levels: section → subsections[] → paragraphs[].
- **Adding "Respond with JSON" prompt text.** Existing tests assert that prompts contain none (`AssertNoJsonInstructions`). Schema enforcement does it.
- **`TextBox` with default `MaxLength`.** The default is 32,767 characters for typed/pasted text [CITED: learn.microsoft.com TextBoxBase.MaxLength]. Set `MaxLength = 0` (no limit) on scenario and section editors, or pastes silently truncate.

## Complexity Presets (CASE-02)

All numbers are recommended defaults [ASSUMED]. The research files give no verified numeric standard. Keep them in one static table (`ComplexityPreset.Get(level)`) so the UI summary, the outline prompt, the validator and Phase 3 (character counts) all read the same values.

| Parameter | Simple | Standard | Complex |
|-----------|--------|----------|---------|
| Parties (named litigants / subjects) | 2 | 3-4 | 5-7 |
| Counts / issues | 1-2 | 3-4 | 5-7 |
| Key characters (target for Phase 3 cast; named individuals in the doc) | 6-10 | 12-20 | 25-40 |
| Timeline span (months) | 3-6 | 9-18 | 24-48 |
| Factual subsections | 2 | 3-4 | 6-8 |
| Numbered paragraphs, whole doc (Complaint) | 40-70 | 90-140 | 180-260 |
| Approx. length | ~8-12 pp | ~20-30 pp | ~40-60 pp |
| Memo equivalent | ~6-10 pp, 1-2 findings | ~15-25 pp, 3-4 findings | ~30-50 pp, 5-7 findings |

The UI shows this row for the selected preset (live label), which makes the change "visible" per success criterion 1. The validator flags the outline if party or count numbers fall outside the range (warning, not a block). Budget about 60-90 output tokens per numbered paragraph [ASSUMED], and keep each section call at 40 paragraphs or fewer (for Complex, split fact subsections). Set `MaxOutputTokens = 12_000` per section call; this budget includes reasoning tokens (per `LlmRequest` doc comment) [VERIFIED].

## Legal Anatomy Templates (CASE-05)

From `.planning/research/FEATURES.md` §A/§B [CITED; HIGH for complaint anatomy per FRCP 8/10/11, MEDIUM-HIGH for memo, MEDIUM for Petition state specifics].

| Complaint (federal or state) | Petition (state; e.g. Texas style) | Investigation Memo |
|------------------------------|-------------------------------------|--------------------|
| Caption (code) + title incl. counts + "DEMAND FOR JURY TRIAL" | Caption (code), "Plaintiff's Original Petition" | Header (code): PRIVILEGED & CONFIDENTIAL / ATTORNEY-CLIENT / WORK PRODUCT (internal) or LAW ENFORCEMENT SENSITIVE (government); TO/FROM/CC/DATE/RE |
| Nature of the Action (LLM) | Discovery Control Plan level (LLM, 1 para) | Executive Summary (LLM, generated last) |
| Parties (LLM, 1 para per party) | Claim for Relief / monetary range statement (LLM, 1 para) | Background / how the matter arose (LLM) |
| Jurisdiction and Venue (LLM, constrained to the outline's court and statutes) | Parties, Jurisdiction and Venue (LLM) | Scope and Methodology: custodians, date range, sources, interviews (LLM). This later maps onto the corpus |
| Factual Allegations, n subsections (LLM) | Facts (LLM) | Key Individuals and Entities (LLM) |
| Count I..N: incorporation para (code) + elements + damages (LLM) | Causes of Action (as Complaint) | Chronology / Factual Findings, dated (LLM) |
| Prayer for Relief, lettered (LLM items, code letters) | Conditions Precedent (LLM, 1 para) | Findings by Issue / Allegation 1..N (LLM) |
| Jury Demand (code) | Request for Disclosure / jury demand (code) | Legal Analysis / Exposure: statutes and policies only (LLM) |
| Signature block (code from outline counsel) | Prayer + signature (code) | Conclusions and Recommendations (LLM) |

**Memo perspective:** the outline picks `InternalCorporate` (outside counsel for a company: regulatory or fraud) or `Government` (for example an agency task-force memo for criminal-conspiracy scenarios). The header banner and author block follow from that choice [ASSUMED].

**Jurisdiction consistency checks (deterministic):** (1) the caption court name is built from `Jurisdiction`. (2) For state courts, the state name appears in the court name and the venue section. (3) The jurisdiction section for a federal court mentions 28 U.S.C. § 1331 or § 1332 (warning only). (4) The outline timeline end is on or before the filing/memo date, and every fact subsection date range falls inside the timeline. Keep these checks modest. Full local-rule fidelity is explicitly out of scope.

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| JSON schemas for DTOs | Hand-written schema strings | `JsonSchemaRegistry.GetSchemaJson(typeof(T))` via the gateway | Strictness rules differ per provider. Phase 1 already handles them and tests them |
| Retry / refusal / truncation handling | New retry loops around LLM calls | `LlmGateway` policy (Invalid ×3, Truncated ×1 at 2×, no retry on Refused/Failed) | Double-retry burns money. PROV-04 forbids blind retries. The **only** new retry is the one-shot citation-violation regeneration, which is a content retry, not a transport retry |
| Real-vs-fictional entity knowledge | Name lists, capitalization heuristics | LLM pre-flight | Only world knowledge can say "Enron is real" |
| Cost tracking | Per-generator token math | Gateway records into `WizardState.UsageTracker` automatically | The live ticker already works |
| Provider selection | Direct `AnthropicClient`/`OpenAIClient` use | `WizardState.CreateGateway()` | D-11 |

**Key insight:** the only hand-rolled pieces should be the deterministic ones: numbering, assembly, citation regex, substitution and validation. These are cheap to unit-test, and the LLM is unreliable at exactly those jobs.

## Common Pitfalls

### Pitfall 1: Truncated long sections
**What goes wrong:** A Complex factual section or a single-call document hits `max_tokens`. The gateway retries once at 2×, capped at 21,333 on Anthropic, and then returns `Truncated`.
**How to avoid:** Keep sections at 40 paragraphs or fewer and split Complex facts into subsections. On a `Truncated` outcome, split the subsection in two (outline-level) instead of retrying.
**Warning signs:** `LlmOutcome.Truncated` in the log; the last paragraph ends mid-sentence.

### Pitfall 2: Refusal on criminal scenarios
**What goes wrong:** A "Breaking Bad conspiracy" scenario hits `Refused` (Anthropic `stop_reason: refusal`, OpenAI refusal field) [CITED: PITFALLS.md Pitfall 7].
**How to avoid:** Frame the system prompt ("fictional training dataset for legal e-discovery review; keep operational criminal detail abstract, focusing on who knew what, when, money flows and cover stories"). Show `Refused` clearly with the model's message and a suggestion to rephrase. No automatic retry (PROV-04). The outline call acts as the scenario pre-flight, so a refusal surfaces before any spend on 5K emails.

### Pitfall 3: Substitution that corrupts ordinary words or chains
**What goes wrong:** Replacing surname "White" breaks "white paper". Replacing A→B and then B→C rewrites already-substituted text. Substring hits: "Apple" inside "Pineapple".
**How to avoid:** Single-pass alternation, letter/digit lookarounds, and case-sensitive single-token aliases (Pattern 3). Unit-test each case.

### Pitfall 4: Scanner false positives on the case's own caption and statutes
**What goes wrong:** "Meridian Partners, LLC v. Crestwell Builders, Inc." (the document's own title) or "28 U.S.C. § 1332" gets flagged, which blocks Next forever.
**How to avoid:** Exclude matches whose normalized parties equal this document's parties. Reporter patterns require `volume + reporter + page`, and `U.S.C.` cannot match. Test a negative corpus.

### Pitfall 5: Paragraph cross-references break after edits
**What goes wrong:** The LLM writes "as alleged in paragraphs 23-31 above". After a regeneration, the numbers are wrong.
**How to avoid:** The prompt forbids paragraph-number references. Code generates incorporation paragraphs. Add a validator warning for `paragraphs? \d+` inside LLM text.

### Pitfall 6: Stale document when the scenario or preset changes
**What goes wrong:** The user goes Back, changes the preset or scenario, and returns to find the old document.
**How to avoid:** Store `CoreDocument.SourceHash` = hash(EffectiveScenario + preset + preferences). On entering `StepCoreDocument` with a different hash, prompt "Scenario changed: regenerate the document?" and never regenerate silently. `OnEnterStepAsync` must not regenerate when the hash matches (unlike `StepStorylines`, which generates on every enter when the list is empty).

### Pitfall 7: Real names introduced by the model itself
**What goes wrong:** The scenario had no real names, but the model uses "Home Depot" or a real judge or law firm.
**How to avoid:** Put "all persons, companies, law firms and judges must be fictional" in the prompt, and render no judge in the caption. Optionally re-run `RealEntityDetector` over the finished document text (one extra Story call, about 10-20K input tokens) and show hits as warnings. Recommended but not required by CASE-03 [ASSUMED].

### Pitfall 8: Running app locks the build output
**What goes wrong:** `dotnet test` fails with MSB3027 "file is locked by ReelDiscovery (pid)" while the app is open for UAT [VERIFIED: happened during this research].
**How to avoid:** Close the app before test runs. Executors should check for a running `ReelDiscovery` process before `dotnet test`.

### Pitfall 9: UI thread blocking / reentrancy
**What goes wrong:** Generation runs on the UI context (no `ConfigureAwait(false)` in the codebase). A double click on Regenerate starts two runs.
**How to avoid:** Set `_isGenerating` and disable all generate/edit buttons, and raise `StateChanged` so `CanMoveNext/Back` are false. Put the finally-block reset per the CLAUDE.md UI boundary rule.

## Code Examples

### Citation scanner (pattern shape; the planner copies it into `Services/Case/CitationScanner.cs`)
```csharp
// Source: pattern design informed by eyecite/reporters-db (github.com/freelawproject/eyecite); reporter list curated [ASSUMED complete enough for v1]
public static partial class CitationScanner
{
    // volume + reporter + first page, e.g. "531 U.S. 98", "123 F.3d 456", "45 Cal. Rptr. 3d 12"
    [GeneratedRegex(@"\b\d{1,4}\s+(?:U\.\s?S\.|S\.\s?Ct\.|L\.\s?Ed\.(?:\s?2d)?|F\.\s?Supp\.(?:\s?[23]d)?|F\.\s?App'x|Fed\.\s?Appx\.?|F\.(?:\s?(?:2d|3d|4th))?|B\.\s?R\.|Fed\.\s?Cl\.|A\.(?:\s?[23]d)?|P\.(?:\s?[23]d)?|N\.E\.(?:\s?[23]d)?|N\.W\.(?:\s?2d)?|S\.E\.(?:\s?2d)?|S\.W\.(?:\s?[23]d)?|So\.(?:\s?[23]d)?|Cal\.\s?Rptr\.(?:\s?[23]d)?|Cal\.\s?App\.\s?(?:2d|3d|4th|5th)|Cal\.\s?(?:2d|3d|4th|5th)|N\.Y\.S\.(?:\s?[23]d)?|N\.Y\.\s?[23]d|A\.D\.\s?[23]d|Ill\.\s?Dec\.|Ill\.\s?App\.(?:\s?[23]d)?)\s+\d{1,5}\b")]
    private static partial Regex ReporterCitation();

    // "2019 WL 1234567", "2020 U.S. Dist. LEXIS 12345"
    [GeneratedRegex(@"\b(?:19|20)\d{2}\s+(?:WL|U\.S\.\s?(?:Dist\.|App\.)\s?LEXIS|[A-Z][A-Za-z.]*\s?LEXIS)\s+\d+\b")]
    private static partial Regex DatabaseCitation();

    // "Smith v. Jones" (warning unless it is this document's own caption)
    [GeneratedRegex(@"\b([A-Z][\w.&'\-]*(?:\s+(?:[A-Z][\w.&'\-]*|of|the|and|&))*)\s+v\.\s+([A-Z][\w.&'\-]*(?:\s+(?:[A-Z][\w.&'\-]*|of|the|and|&))*)")]
    private static partial Regex CaseName();

    [GeneratedRegex(@"\bId\.\s+at\s+\d+|\bsupra\b|\bSee,?\s+e\.g\.,")]
    private static partial Regex CitationSignal();

    public static List<CitationFinding> Scan(string text, IReadOnlyCollection<string> ownPartyNames) { /* ... */ }
}
```
Required tests: `"28 U.S.C. § 1332"` → none. `"531 U.S. 98 (2000)"` → Error. `"Fed. R. Civ. P. 38"` → none. `"2019 WL 1234567"` → Error. Own caption → none. `"Smith v. Jones"` → Warning.

### Section DTO (strict-schema safe: no nullables, no recursion, strings for dates)
```csharp
// Private nested in CoreDocumentGenerator, per CLAUDE.md DTO convention
private class SectionDto
{
    [JsonPropertyName("subsections")] public List<SubsectionDto> Subsections { get; set; } = new();
}
private class SubsectionDto
{
    [JsonPropertyName("subheading")] public string Subheading { get; set; } = string.Empty; // "" = none
    [JsonPropertyName("paragraphs")] public List<string> Paragraphs { get; set; } = new();  // no numbers
}
internal static IReadOnlyList<Type> SchemaTypes { get; } = new[] { typeof(OutlineDto), typeof(SectionDto) };
```

### Gateway call (existing pattern from Phase 1 generators)
```csharp
var result = await _llm.GenerateAsync<SectionDto>(new LlmRequest
{
    Tier = LlmTier.Story,
    OperationName = $"Core Document - {spec.Heading}",
    SystemPrompt = CoreDocumentPrompts.System,          // stable prefix (Phase 7 caching)
    UserPrompt = BuildSectionPrompt(outline, priorText, spec, guidance),
    MaxOutputTokens = 12_000
}, ct);
if (result.IsFatal) throw new LlmCallException(result); // caller stops generation
var dto = result.GetValueOrThrow();
```

### Code-owned incorporation paragraph
```csharp
// In CoreDocumentAssembler: numbering is computed, never stored in LLM text
var lastFact = numbered.Last(p => p.SectionKind == SectionKind.FactualAllegations).Number;
yield return new NumberedParagraph(++n,
    $"{doc.PleadingPartyLabel} repeats and realleges paragraphs 1 through {lastFact} as if fully set forth herein.");
```
(`NumberedParagraph` should be a small class, not a record, per CLAUDE.md.)

## State of the Art

| Old Approach | Current Approach | When Changed | Impact |
|--------------|------------------|--------------|--------|
| JSON-mode + "respond with JSON" prompts | Strict JSON-schema structured outputs on both providers | Phase 1 (this repo) | DTOs are the contract. No prompt JSON instructions |
| Hardcoded PoC complaint (stash) | Scenario-driven outline + sections | This phase | Matches the user's feedback |
| Topic → storylines as the story source | Core document → (Phase 3) structured guide | Roadmap | Phase 2's structured sections feed Phase 3 extraction |

**Deprecated/outdated:** `OpenAIService` (deleted in 01-11) is referenced by all stashed PoC code, so none of it compiles as-is.

## Assumptions Log

| # | Claim | Section | Risk if Wrong |
|---|-------|---------|---------------|
| A1 | Preset numbers (parties, counts, characters, months, paragraphs) | Complexity Presets | Low. Values are editable constants. The user may want different sizes |
| A2 | Case-driven flow is the default flow; legacy selectable until Phase 7 | Pattern 1 | Medium. The user may prefer legacy as default until the corpus exists |
| A3 | Government agencies, courts, places and statutes are exempt from substitution | Pattern 3 | Medium. Some users may want agencies fictionalized too |
| A4 | Fictional-work character names (e.g. Breaking Bad) are substituted by default | Pattern 3 | Low. The user can override per row |
| A5 | Error-level citation findings block Next; warnings prompt | Pattern 4 | Low |
| A6 | 60-90 output tokens per paragraph; 12K max tokens per section call | Presets | Low. The gateway truncation retry covers a moderate miss |
| A7 | Optional real-entity re-scan of the finished document | Pitfall 7 | Low (cost about cents) |
| A8 | Memo perspective (InternalCorporate vs Government) chosen by the outline | Legal Anatomy | Low |
| A9 | Reporter regex list covers the realistic LLM output space | Code Examples | Medium. Rare regional reporters are missed; mitigated by the "v." warning and the prompt rule |
| A10 | Petition chosen only for state courts when the outline says the forum uses Petition terminology | Pattern 2 | Low. The user override exists |

## Open Questions

1. **Persistence in Phase 2.**
   - What we know: GUIDE-10 (case.json) is Phase 3, and Phase 1 built no run store. The case-driven flow in Phase 2 ends at the core document, and "Finish" closes the form.
   - What's unclear: whether losing the draft on close is acceptable for Phase 2 UAT.
   - Recommendation: make `CaseDraft` fully JSON-round-trippable (tested). Add a small "Save draft…/Open draft…" (SaveFileDialog, `.reelcase.json`) **only if cheap**, reusing the same serializer Phase 3 will use for case.json. Otherwise add a confirm on Finish. Planner's call. Mark it optional in the plan.
2. **Jurisdiction picker granularity.**
   - Recommendation: combos for "Court system: Auto / Federal / State" and "State: (Auto) + 50 states + DC". The outline picks the district/county. Anything finer is out of scope.
3. **Should edits to the core doc re-run real-name leak scanning?** Yes. It is cheap and deterministic, so run it with the citation scan on every edit.

## Environment Availability

| Dependency | Required By | Available | Version | Fallback |
|------------|------------|-----------|---------|----------|
| .NET SDK (pinned by global.json) | Build/test | ✓ | 10.0.401 | — |
| xUnit test project | Validation | ✓ | xunit 2.9.3 | — |
| OpenAI / Anthropic API keys | Live UAT only | user-supplied | — | Unit tests use `FakeLlmProvider`; no live calls needed for the phase gate (D-03 precedent) |
| Running ReelDiscovery.exe | — | Currently running (pid seen in build error) | — | Close before `dotnet test` |

No new external dependencies.

## Validation Architecture

### Test Framework
| Property | Value |
|----------|-------|
| Framework | xUnit 2.9.3, xunit.runner.visualstudio 3.1.4, Microsoft.NET.Test.Sdk 17.14.1 |
| Config file | `ReelDiscovery.Tests/ReelDiscovery.Tests.csproj` (references `..\ReelDiscovery.csproj`; `InternalsVisibleTo ReelDiscovery.Tests` already set) |
| Quick run command | `dotnet test ReelDiscovery.slnx --filter "FullyQualifiedName~ReelDiscovery.Tests.Case"` |
| Full suite command | `dotnet test ReelDiscovery.slnx` (baseline 211 passing per 01-VERIFICATION) |
| Precondition | ReelDiscovery.exe must not be running (MSB3027 file lock) |

### Phase Requirements → Test Map
| Req ID | Behavior | Test Type | Automated Command | File Exists? |
|--------|----------|-----------|-------------------|-------------|
| CASE-01 | Scenario stored on CaseDraft; empty or over-length scenario rejected by a pure validator (`CaseDraft.ValidateScenario`) | unit | `dotnet test ReelDiscovery.slnx --filter "FullyQualifiedName~CaseDraftTests"` | ❌ Wave 0 |
| CASE-02 | Presets strictly increase Simple<Standard<Complex on every parameter; outline prompt contains the preset numbers (FakeLlmProvider request capture); validator warns on out-of-range parties/counts | unit | `--filter "FullyQualifiedName~ComplexityPresetTests\|FullyQualifiedName~CoreDocumentValidatorTests"` | ❌ Wave 0 |
| CASE-03 | Detector parses entities; substitution: word boundaries, possessive, case-sensitive surname aliases, no chains, longest-first, unchecked rows untouched; leak scan finds originals in output | unit | `--filter "FullyQualifiedName~NameSubstitutionTests\|FullyQualifiedName~RealEntityDetectorTests"` | ❌ Wave 0 |
| CASE-04 | Resolver: criminal/regulatory → Memo; civil federal → Complaint; state + petition flag → Petition; user preference overrides | unit | `--filter "FullyQualifiedName~DocumentTypeResolverTests"` | ❌ Wave 0 |
| CASE-05 | Assembler: consecutive numbering across sections; incorporation paragraph cites 1..lastFact and updates after a facts edit; required sections present per type; caption built from Jurisdiction; memo header present; leading LLM numbers stripped; jurisdiction consistency checks | unit | `--filter "FullyQualifiedName~CoreDocumentAssemblerTests\|FullyQualifiedName~CoreDocumentValidatorTests"` | ❌ Wave 0 |
| CASE-06 | Scanner positive/negative corpus; generator regenerates a section once when the fake returns a reporter citation (2nd request contains the violation text), and surfaces remaining findings | unit | `--filter "FullyQualifiedName~CitationScannerTests\|FullyQualifiedName~CoreDocumentGeneratorTests"` | ❌ Wave 0 |
| CASE-07 | RegenerateSection replaces only the target section, keeps its Id, and passes other sections' current (edited) text in the prompt; paragraph split of edited text; Revision increments on edit/regenerate; full regenerate resets | unit | `--filter "FullyQualifiedName~CoreDocumentGeneratorTests\|FullyQualifiedName~CoreDocumentEditingTests"` | ❌ Wave 0 |
| (cross) | All new DTO schemas strict, ≤16 union types | unit | `--filter "FullyQualifiedName~CaseSchemaTests"` | ❌ Wave 0 |
| (cross) | CaseDraft JSON round-trip (Phase 3 case.json readiness) | unit | `--filter "FullyQualifiedName~CaseDraftSerializationTests"` | ❌ Wave 0 |
| (cross) | Outcome handling: Refused/Fatal stop generation with message; Truncated/Failed mark section Failed and keep others | unit | `--filter "FullyQualifiedName~CoreDocumentGeneratorTests"` | ❌ Wave 0 |
| UI (all) | Steps render, preset summary changes, dialog override, editor/regenerate, Next gating on citation errors, live Claude + OpenAI civil and criminal scenarios | manual (HUMAN-UAT) | — | n/a: needs a desktop session and API keys |

### Sampling Rate
- **Per task commit:** quick run (`FullyQualifiedName~ReelDiscovery.Tests.Case`)
- **Per wave merge:** `dotnet test ReelDiscovery.slnx`
- **Phase gate:** full suite green plus `dotnet build ReelDiscovery.slnx` with 0 warnings (project keeps builds warning-free) before `/gsd-verify-work`

### Wave 0 Gaps
- [ ] `ReelDiscovery.Tests/Case/` folder with the test files listed above
- [ ] Test helper: `CaseTestData` (sample outline JSON, section JSON with and without citations, sample scenario with real names)
- [ ] Reuse `FakeLlmProvider` + `LlmGateway` construction pattern from `LegacyGeneratorPortTests.CreateGateway()` (Anthropic kind proves provider-agnostic)
- No framework install needed

## Security Domain

`security_enforcement: true`, ASVS level 1.

### Applicable ASVS Categories
| ASVS Category | Applies | Standard Control |
|---------------|---------|-----------------|
| V2 Authentication | no | (keys handled in Phase 1 DPAPI store) |
| V3 Session Management | no | — |
| V4 Access Control | no | single-user desktop |
| V5 Input Validation | yes | Scenario non-empty with a length cap (e.g. 8,000 chars); preferences are enums; LLM output parsed only through strict DTOs; substitution regex built from `Regex.Escape`d literals with a match timeout |
| V6 Cryptography | no | nothing new (keys stay in the DPAPI store) |
| V7 Error Handling/Logging | yes | Show `LlmResult.Message` (never contains keys per Phase 1). Do **not** write scenario text or documents to any log file |
| V8 Data Protection | yes | Scenario may contain real names. It goes only to the user-selected provider (telemetry removed in Phase 1, D-16). Substitution is on by default |

### Known Threat Patterns
| Pattern | STRIDE | Standard Mitigation |
|---------|--------|---------------------|
| Generated defamatory "evidence" about real people/companies | Repudiation / legal risk | Real-entity pre-flight, default substitution, leak scan, fictional-only prompt rule, no judge names |
| Fabricated case citations in a legal product | Tampering (integrity) | Prompt rule + CitationScanner + blocking Next on errors |
| Denial of wallet (repeated regenerate clicks, huge scenario) | DoS | Busy-state button disabling, scenario length cap, section-level regeneration instead of whole-doc |
| Prompt injection via scenario text | Tampering | Low impact (the user attacks only their own output). Scenario placed in the user prompt, never the system prompt; strict schema limits output shape |
| ReDoS from user/LLM-derived patterns | DoS | Escaped literals only; `[GeneratedRegex]` fixed patterns; match timeout on the dynamic substitution regex |

## Sources

### Primary (HIGH confidence)
- Codebase (verified by reading): `Services/Ai/{ILlmGateway, LlmGateway, LlmRequest, LlmResult, JsonSchemaRegistry, AnthropicTextProvider}.cs`, `Models/{WizardState, AIModelConfig}.cs`, `Forms/WizardForm.cs`, `UserControls/{IWizardStep, StepStorylines, StepTopicInput}.cs`, `Helpers/LoadingOverlay.cs`, `ReelDiscovery.Tests/Fakes/*`, `ReelDiscovery.Tests/Generation/LegacyGeneratorPortTests.cs`, csproj files, global.json
- `.planning/phases/01-foundation-provider-gateway/01-CONTEXT.md`, `01-VERIFICATION.md`, `01-07-SUMMARY.md`
- `git stash show stash@{0}` and `git show stash@{0}:...` for the Case Bible PoC contents
- [Microsoft Learn: TextBoxBase.MaxLength](https://learn.microsoft.com/en-us/dotnet/api/system.windows.forms.textboxbase.maxlength): default 32767, applies to typed/pasted text only

### Secondary (MEDIUM confidence)
- `.planning/research/FEATURES.md` §A/§B (complaint/petition/memo anatomy), `PITFALLS.md` (Pitfalls 7, 11, 12, 13, 14), `ARCHITECTURE.md` (CoreDocumentGenerator sectioning), `SUMMARY.md`
- [eyecite (Free Law Project)](https://github.com/freelawproject/eyecite): citation categories, regex+reporters-db approach, BSD license, Python-only
- Web search: no .NET/NuGet legal citation extractor found ([eyecite JOSS paper](https://www.theoj.org/joss-papers/joss.03617/10.21105.joss.03617.pdf), [unitedstates/citation (JS)](https://github.com/unitedstates/citation))

### Tertiary (LOW confidence)
- Preset numeric ranges, token-per-paragraph estimate (training knowledge; flagged ASSUMED)

## Metadata

**Confidence breakdown:**
- Standard stack: HIGH. No new packages; every integration point was read in code.
- Architecture: HIGH for gateway use, sectioning and code-owned skeleton (forced by the verified 21,333-token cap and strict-schema limits). MEDIUM for flow-coexistence UX.
- Legal anatomy: MEDIUM-HIGH (standard practice, from project research).
- Pitfalls: HIGH (most verified in code or during this session).
- Preset numbers: LOW (assumed defaults).

**Research date:** 2026-09-30
**Valid until:** 2026-10-30 (stable. Re-check only if the gateway contract or model catalog changes)
