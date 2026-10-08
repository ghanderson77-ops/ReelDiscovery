# Phase 8: Ancillary Discovery Documents - Research

**Researched:** 2026-10-08
**Domain:** Grounded LLM generation of litigation discovery documents (deposition transcript excerpts, RFPs, RFAs, interrogatories) rendered to .docx with DocumentFormat.OpenXml, inside the existing WinForms case wizard
**Confidence:** HIGH for codebase integration, MEDIUM-HIGH for legal format conventions, MEDIUM for token/cost sizing

## Summary

Phase 8 is mostly an integration phase. The repo already has every building block it needs:
- a provider-neutral `ILlmGateway` with strict-schema DTOs;
- a bounded fan-out primitive (`PlanCallRun` / `PlanCallRunner`) that already handles fatal stops, cancellation and cost capture;
- a schema-ordered, validator-clean `CaseGuideDocxExporter.WordBuilder`;
- canonical-value and people checks (`CanonicalValueScanner`, `RenderValidator` V2/V3 logic);
- point-in-time knowledge gating (`KnowledgeGate`, `LedgerMerger` / `LedgerSelector`);
- content hashes for staleness (`CaseGuide.ComputeHash()`, `GuideStaleness.CoreDocumentHash`);
- atomic writes (`CaseStore.WriteAtomicAsync`) and a sidecar-JSON precedent (`CorpusPlanStore`);
- a 144-DPI render harness.

Three things are new and need real design work:
1. **A deterministic transcript layout engine:** turns, then wrapped fixed-width lines, then 25-line pages, then .docx.
2. **The code-owned "planners"** that decide witnesses, roles, dates, case number, exhibits, topic/page budgets, request slots per issue, and RFA fact selection.
3. **A `DiscoveryValidator`** that applies the D-26 checks to ancillary text. The existing V2/V3 rules are private and coupled to `RenderedThread`.

The legal format conventions are well documented:
- The federal Judicial Conference transcript format specifies 25 double-spaced lines per page, 10 characters per inch (about 63 characters per line), text from 1-3/4" to 3/8" from the page edges, and Q/A text starting at the 5th space. Colloquy speaker labels start at the 10th space and parentheticals at the 5th space.
- FRCP 33(a)(1) caps interrogatories at 25 "including all discrete subparts".
- Rules 33, 34 and 36 each give 30 days to respond.
- Rule 36(a)(1)(B) expressly allows requests to admit the genuineness of described documents.

**The recommended transcript approach is "everything is a line":**
- Every transcript page (caption, appearances, index, testimony, certificate) is produced by one layout engine as exactly 25 strings of at most 62 monospaced characters.
- Each line becomes one Word paragraph in Courier New 12 pt with **exact** 24 pt (480 twip) line spacing, a right-tabbed line number, and literal leading spaces for indentation.
- A page-heading paragraph with `pageBreakBefore` opens every page.
- This makes page:line deterministic, unit-testable from the layout model without opening Word, and validator-clean.

**Primary recommendation:**
- Build a `Services/Discovery/` module with pure planners (`DepositionPlanner`, `WrittenDiscoveryPlanner`), a pure `TranscriptLayout` engine, a `DiscoveryValidator`, a `DiscoveryGenerator` (gateway calls through `PlanCallRunner`, check → 2 retries with feedback → keep best and flag), `DiscoveryDocxBuilder` (written discovery on the existing `WordBuilder`) plus `TranscriptDocxBuilder` (a sibling builder with transcript geometry), and `DiscoveryStore` (the `discovery-documents.json` sidecar).
- Then add `StepDiscoveryDocuments` between Generate Corpus and Case Summary, plus one Case Summary row.

## Architectural Responsibility Map

This is a single-process desktop app. The "tiers" are its layers.

| Capability | Primary Tier | Secondary Tier | Rationale |
|------------|-------------|----------------|-----------|
| Witness eligibility, ranking, defaults, role assignment, dates, case number, reporter picks | Services (pure planner, `Services/Discovery`) | Models (`CaseDraft.Discovery` selections) | D-04: code owns structure; must be deterministic and unit-testable |
| Request slot apportioning per issue, RFA fact selection, exhibit numbering | Services (pure planner) | — | Code owns which issue or fact each request is keyed to (D-04, D-17..D-21) |
| Writing testimony turns and request text | LLM via `ILlmGateway` (Story tier, live) | Services (prompt builder) | The only part the LLM owns (D-04, D-28) |
| Consistency checks and retry feedback | Services (`DiscoveryValidator`) | — | Mirrors Phase 5 D-12..D-14; reuses CanonicalValueScanner |
| Transcript pagination (25 lines/page, page:line) | Services (pure `TranscriptLayout`) | — | Must be deterministic and testable without Word (D-23) |
| .docx rendering | Services (`TranscriptDocxBuilder`, `DiscoveryDocxBuilder`) | — | Pure `byte[]` builders like `CaseGuideDocxExporter` |
| Persistence of selections | Models (`CaseDraft.Discovery` in case.json) | `CaseStore` | D-29 |
| Persistence of generated content and source hashes | Storage (`discovery-documents.json` sidecar) | `CaseStore.WriteAtomicAsync` | D-29; follows the corpus-plan.json precedent |
| Cost estimate | Services (`GenerationCostEstimator` new method) | UI (Ready card) | D-28: single pricing path |
| Step UI, results grid, regenerate dialog, Summary row | UI (`UserControls/`, `Forms/`) | `DiscoveryUiText` (pure copy) | D-07..D-11 |

<user_constraints>
## User Constraints (from CONTEXT.md)

### Locked Decisions

#### Carried forward (locked in Phases 1-7)
- **D-01:** All LLM work goes through `ILlmGateway` with strict JSON-schema DTOs and must work with Claude or OpenAI. The Phase 2 strict-schema limits apply: at most 16 nullable fields, no recursion, dates as `yyyy-MM-dd` strings, and provider output caps, so large documents are chunked. New DTOs are registered in `SchemaTypes` / `CaseSchemaTests`.
- **D-02:** Names are kept exactly as the guide has them, **including names from fictional works**. Prompts never say that names must be fictional. Only real-world entities follow the Phase 2 substitution decisions already stored on `CaseDraft.Substitutions`. Content takes a firm position.
- **D-03:** Every new or changed screen is rendered at **144 DPI (150% scaling)** with `ReelDiscovery.Tests/Render/RenderHarness.cs`, checked for clipping, and the PNGs are inspected by eye. No fixed pixel sizes. Use the existing theme and controls: `Theme`, `ThemedButton` via `ButtonHelper.CreateButton`, `CardPanel`, `SlimProgressBar`, `ThemedDialog`, and `GridStyle`.
- **D-04:** **Code owns structure; the LLM writes text only.** Everything that has a canonical source comes from code: captions, court, parties, counsel and signature blocks, dates, numbering, exhibit numbers, page and line layout, and which issue or fact each request is keyed to.
- **D-05:** Never-throws units of work. Catch `OperationCanceledException` before `Exception`. One failed document never aborts the others (RUN-02 pattern). Fatal provider stops (Auth / SpendLimit) end the run cleanly and keep finished documents.
- **D-06:** Costs fold into the existing ticker and the Case Summary cost breakdown. The stable case-guide prefix is cached where the provider supports it (Phase 7 D-19).

#### Where it lives in the flow
- **D-07:** A **new optional wizard step, "Discovery Documents"**, sits **between Generate Corpus and Case Summary**.
  - Next is always enabled unless generation is running, so the step can be skipped.
  - Placing it after the corpus lets exhibits cite DocIDs when a corpus exists. Every corpus step can already be passed with Next, so the step is also reachable without a corpus.
  - Rationale: the step needs witness picking, four document types, an estimate, progress and per-document results. That is too much for a Case Summary dialog, and putting it before the corpus would add a step to the critical path.
- **D-08:** **Case Summary gets one "Discovery documents" row.**
  - Its status line shows "N documents · last generated …" or "Not generated".
  - Buttons: **"Open Discovery Folder"** and **"Discovery Documents"**. The second navigates to the step, the same way "Plan Corpus" does.
  - Export All on Case Summary does not regenerate these documents. They are written when they are generated (D-27).
- **D-09:** **Do not add a new `CaseStage` value.** Reopening a case still lands on the furthest corpus stage or Summary. The step is reached through navigation or the Case Summary button. Rationale: this avoids changing the ordinal semantics of `AdvanceStage`.

#### Screen layout
- **D-10:** One step screen with three cards, matching the Case Guide, Review Plan and Generate Corpus visual language:
  1. **Depositions:** a checklist of eligible witnesses (D-12) and a pages-per-excerpt control.
  2. **Written discovery:** checkboxes for RFP, Interrogatories and RFA, each with a count control, plus propounding and responding party pickers (D-16).
  3. **Ready / results:** a live cost estimate, one **Generate** button, a SlimProgressBar and Cancel. A results grid has one row per document showing status (Generated / Generated with warnings / Failed / Out of date), with **Open in Word** and **Regenerate…** buttons.
- **D-11:** **Regenerate…** opens a small ThemedDialog (following the `RegenerateTabDialog` pattern) with optional guidance text. It replaces only that document. Nothing regenerates silently.

#### Witness selection (depositions)
- **D-12:** **Eligible witnesses** are key-tier cast members whose role is not counsel. They are ranked by the number of key facts in which they appear as actor or knower.
  - Default checked count by preset: **Simple 1, Standard 2, Complex 3**. The defaults favor the top-ranked witnesses on the defending side, then the top-ranked on the other side.
  - The user can check any eligible witness, up to **6** per run.
- **D-13:** **Code assigns the deposition roles from the cast:**
  - **Examining attorney:** the partner at the outside law firm representing the opposing party.
  - **Defending attorney:** the partner or associate at the outside firm representing the witness's organization.
  - **Appearances:** the counsel for each party.
  - The court reporter's name, CSR number and the videographer are **invented per document by code from a small built-in list**. They are never cast members.
- **D-14:** **Deposition dates are assigned by code.**
  - Business days (`BusinessCalendar`) about **3-9 months after the core document's FilingDate**, and never before the latest key-fact date.
  - Each witness gets a different date, in a deterministic order.
  - Start time is 9:30 or 10:00 a.m. Location is either the examining firm's office in the venue city or "taken remotely via videoconference".
- **D-15:** **Excerpt length:** the pages-per-excerpt default is **Simple 15, Standard 25, Complex 40**. The user can choose 10-60 pages. The caption page says **"EXCERPT"** and the index reflects only the excerpted pages.

#### Written discovery
- **D-16:** **Default parties and sets:**
  - The first plaintiff or petitioner serves the primary defendant or respondent, as "FIRST SET OF …". The user can switch either party picker to any party in the core document.
  - Each run produces one set per checked document type. Multiple sets or multiple party pairs are deferred.
- **D-17:** **RFPs are keyed to issues.**
  - Default count by preset: **Simple 15, Standard 25, Complex 40**; adjustable from 5 to 80.
  - Code distributes the request slots so that **every issue gets at least one RFP**. Each request carries its issue ID(s) and names the custodians, project and date ranges from the guide.
  - Includes a preliminary statement, Definitions, Instructions (relevant time period = case timeline window, ESI form, privilege-log requirement, duty to supplement), a signature block and a certificate of service.
- **D-18:** **Interrogatories are keyed to issues**, with a **hard cap of 25 including subparts** (FRCP 33).
  - Default count by preset: **Simple 12, Standard 18, Complex 25**.
  - Every issue gets at least one. The set mixes "identify persons with knowledge" with contention interrogatories per issue.
- **D-19:** **RFAs are keyed to key facts**, one discrete fact per request.
  - Default count by preset: **Simple 10, Standard 20, Complex 30**; adjustable up to 50.
  - Code selects the facts, Key significance first and then chronological. Each RFA carries one fact ID and states that fact's **date, amount and actors verbatim** from the guide.
  - When a corpus exists, a few genuineness RFAs ("Admit that the document produced as DocID … is a true and correct copy …") reference hot documents linked to the selected facts.
- **D-20:** **Rule citations follow the jurisdiction in the core document.**
  - Federal court: FRCP 26/33/34/36.
  - State court: the state's rules of civil procedure cited generically, without invented local-rule numbers.
  - Response deadline: 30 days.

#### Deposition content and format
- **D-21:** **Exhibits are numbered by code, consecutively across all depositions in the case.**
  - With a corpus: exhibits are hot or key relevant documents in which the witness participated, cited by DocID, subject and date, and the exhibit index lists them.
  - Without a corpus: exhibits are canonical documents from the values registry (contract, change order and invoice titles and numbers).
  - "(Deposition Exhibit N was marked for identification.)" appears where code placed it.
- **D-22:** **Testimony is grounded in the witness's knowledge.**
  - The prompt includes only facts in which the witness is an actor or knower (a KnowledgeGate-style filter). When a corpus exists, it also includes the ledger entries the witness knows as of the deposition date.
  - Examination is organized by issue or topic. Adverse witnesses may be evasive ("I don't recall", hedging) but **never contradict canonical dates, amounts or who-did-what**. There are a few impeachment sequences built on exhibits.
  - Objections ("Objection, form.") and privilege instructions come from the defending attorney.
- **D-23:** **The LLM returns structured turns; code lays out the pages.**
  - Each turn has a kind (Q, A, colloquy, objection, parenthetical), a speaker ID and text. Turns are generated in chunks per examination topic to stay under output caps.
  - Code wraps the turns to a fixed character width and lays out **25 numbered lines per page** with left-margin line numbers 1-25 and page numbers, in a monospaced transcript font (Courier New 12 pt) with exact line spacing and explicit page breaks. Word's automatic line numbering is not used, so page:line citations are deterministic and testable.
  - Front and back matter: a caption page (court, caption, case number, "DEPOSITION OF [NAME]", "EXCERPT", date, time, location, reporter), an appearances page, an examination index and exhibit index, the oath, the body, "(Whereupon the deposition concluded at …)", a witness signature and errata page, and the reporter's certificate.
- **D-24:** **One case number is assigned by code** in a court-appropriate format, for example `2:25-cv-01873` (federal) or a state-style docket number. It is derived deterministically from `CaseId` and shared by every ancillary document. The core document keeps its pre-filing "Case No. ____" blank.

#### Investigation-memo cases
- **D-25:** **The civil litigation format is the primary target** (Complaint / Petition). For **Investigation Memo** cases, the step still works with the investigative equivalents:
  - Depositions render as **"Transcript of Sworn Witness Interview / Examination Under Oath"**, with the same page and line format.
  - RFPs render as a **document request / subpoena rider** (grand-jury style for Criminal, Civil Investigative Demand style for Regulatory, pre-suit request for InternalCorporate).
  - Interrogatories render as **CID-style written questions** for Regulatory matters and are hidden for Criminal matters.
  - **RFAs are hidden for Criminal and Regulatory memos**, with an explanatory note in the card.
  - Exact titles are Claude's discretion.

#### Consistency enforcement
- **D-26:** **Grounding plus code checks with retries**, mirroring Phase 5 D-12..D-14.
  - Prompts use structured guide data (issues, the relevant cast, filtered facts, canonical values), **not the raw complaint text** (PITFALLS P-Ancillary).
  - After each chunk or document, code checks:
    - amounts and identifiers match the canonical values (`CanonicalValueScanner` / RenderValidator V3 logic);
    - every person named resolves to the cast or to the code-provided reporter and videographer;
    - every request references an existing issue or fact ID;
    - RFA dates and amounts equal their fact's values;
    - nothing references an event dated after the deposition date.
  - Each failure gets **2 retries with violation feedback**. After that, the best attempt is kept and the document is flagged **"Generated with warnings"**, with the violations listed on the result row.
- **D-27:** **Staleness:** each generated document stores the guide hash (`CaseGuide.ComputeHash()`) and core-document revision it was built from. If either changes later, the row shows **"Out of date with the case guide"** with Regenerate. Nothing regenerates automatically (Phase 3 D-20 pattern).

#### Model, cost and persistence
- **D-28:** The **Story tier** writes every ancillary document, through **live calls only** (no Batch API, because the volume is small). The Ready card shows a cost estimate before Generate, computed through `GenerationCostEstimator`'s single pricing path. Bounded parallelism: one document per unit of work, under the existing parallelism setting.
- **D-29:** **Persistence and output:**
  - User selections (witnesses, counts, parties, pages) are saved in `case.json` on a new `CaseDraft.Discovery` settings object (bump the format version as needed).
  - Generated structured content (turns, requests, warnings, source hashes) goes in a sibling **`discovery-documents.json`** in the case folder, following the `corpus-plan.json` precedent, written atomically via `CaseStore.WriteAtomicAsync`.
  - .docx files are written to a **`Discovery\`** subfolder of the case folder, never inside the mailboxes or produced corpus. File names follow the pattern "<Case> - Deposition of <Full Name> (Excerpt).docx" and "<Case> - <Plaintiff short name>'s First Set of Requests for Production.docx".
  - The .docx is written **automatically when the document is generated**, and rewritten on regenerate.
- **D-30:** **Not added to the answer key or review protocol.** Ancillary documents are case-file material, not review-corpus documents. Mapping RFP numbers to issue codes in the key or protocol is deferred.

### Claude's Discretion
- DTO shapes for deposition turns, requests and chunking granularity (respect the strict-schema limits and provider output caps).
- Characters per line and wrap rules for the transcript layout, plus how colloquy and parentheticals indent. It must be exactly 25 lines per page.
- Exact reporter and videographer name list and CSR number format, the case-number format per court system, and the deposition date spacing within D-14's window.
- Witness ranking tie-breaks and the "defending side first" ordering rule.
- How issue slots are apportioned across requests (reuse `Helpers/Apportion.cs`), and the boilerplate wording of definitions and instructions.
- How the corpus-dependent enrichments (exhibit DocIDs, genuineness RFAs, ledger slice) detect that a usable corpus exists, using the plan plus journal state.
- Investigative-equivalent document titles for memo cases (D-25).
- Whether the .docx builder extends `CaseGuideDocxExporter.WordBuilder` (recommended: reuse its schema-ordered builder and caption code) or adds a sibling builder in the same style.

### Deferred Ideas (OUT OF SCOPE)
- Responses and objections to RFPs, ROGs and RFAs, plus interrogatory verifications: a separate document family.
- Privilege log generated from the privileged threads: a natural v2 item now that the privilege column exists.
- Mapping RFP numbers into the answer key and review protocol, so relevance is traceable to specific requests.
- Multiple sets or multiple propounding/responding party pairs per document type, and Rule 30(b)(6) corporate-representative depositions.
- Video deposition files or synced (.ptx) transcript formats.
- An in-app editor for generated discovery documents.
</user_constraints>

<phase_requirements>
## Phase Requirements

| ID | Description | Research Support |
|----|-------------|------------------|
| ANC-01 | User can optionally generate deposition transcript excerpts for chosen key witnesses, formatted with numbered lines and pages | Witness eligibility and roles: §Pattern 2. Transcript layout engine and OpenXml geometry: §Pattern 4, §Code Examples. Federal format facts: §State of the Art. Turn DTO and chunking: §Pattern 3. Optional step: §Pattern 7 |
| ANC-02 | User can optionally generate Requests for Production keyed to the issues | Slot apportioning with `Apportion.Largest(min: 1)`: §Pattern 5. Code-owned definitions and instructions: §Pattern 6. Request DTO keyed by issue id: §Pattern 3 |
| ANC-03 | User can optionally generate Requests for Admission keyed to the key facts | Fact selection order and verbatim date/amount/actor checks: §Pattern 5, §Pattern 8. Genuineness RFAs from hot DocIDs: §Pattern 9 |
| ANC-04 | User can optionally generate Interrogatories keyed to the issues | 25-including-subparts constant and no-subparts validator rule: §Pattern 5, §Pitfall 4 |
| ANC-05 | Ancillary documents are consistent with the core document, cast and facts, and are exported as .docx | `DiscoveryValidator` (D-26): §Pattern 8. Staleness hashes: §Pattern 10. Atomic `.docx` in `Discovery\`: §Pattern 11. OpenXmlValidator-clean tests: §Validation Architecture |
</phase_requirements>

## Project Constraints (from CLAUDE.md)

These directives carry the same weight as locked decisions.

**Code reality differs from CLAUDE.md's stack text:**
- The csproj targets **`net10.0-windows`** (both app and tests), with `DocumentFormat.OpenXml` **3.5.1**, `OpenAI` 2.14.0, `Anthropic` 12.51.0 and `Microsoft.Extensions.AI.Abstractions` 10.10.1. [VERIFIED: ReelDiscovery.csproj]
- CLAUDE.md's "net8 / OpenXml 3.0.2 / no test project" text is legacy. Plans must target what the csproj says.

**Conventions the planner must enforce:**
- One public type per file; namespace mirrors folder (`ReelDiscovery.Services.Discovery`, `ReelDiscovery.Models.Discovery`).
- Private fields `_camelCase`; WinForms control fields use a Hungarian prefix (`_chkRfp`, `_numPages`, `_gridResults`).
- Async methods end in `Async`. `IProgress<T>? progress = null, CancellationToken ct = default`, with `ct` always last.
- No `record` types, no primary constructors, no `#region`. Models are mutable classes with auto-properties and target-typed `new()`.
- `[GeneratedRegex]` source-generated regex only, never `new Regex(...)`.
- Rely on implicit usings; no blank lines between using groups.
- Catch `OperationCanceledException` before `Exception`. Bare `catch` is allowed only for non-critical side effects, with a comment.
- Strict DTOs are nested `internal class` types with `[JsonPropertyName]` + `[Description]`, exposed through `internal static IReadOnlyList<Type> SchemaTypes` and registered in `CaseSchemaTests`.
- UI is built in code: TableLayoutPanel roots, content-sized rows, row-index comments on row styles. Handlers are wired after all controls exist (init-order null refs bit Phases 2-3).
- Use `Theme`, `ButtonHelper.CreateButton`, `CardPanel`, `SlimProgressBar`, `ThemedDialog`, `GridStyle`. No fixed pixel sizes (LDU / DPI-scaled). Segoe UI fonts via `Theme`.
- Determinism: `RenderAssignments.StableRank` / SHA-256, never `Random`, `Guid.NewGuid()` or `string.GetHashCode()` for dates, case numbers or reporter picks.
- Pure `*UiText` static copy helpers get unit tests (add `DiscoveryUiText`).
- **GSD workflow:** edits happen only inside `/gsd-execute-phase`.
- **Build/test:** use `--artifacts-path obj/gsd-artifacts`, because the running app locks `bin\Debug`.

**User memory directives:**
- Never substitute names from fictional works (a deposition of "Michael Scott" stays Michael Scott).
- Verify every WinForms change at 150% DPI.
- LLM prose is the default path.

## Standard Stack

### Core (all already referenced; no new packages)
| Library | Version | Purpose | Why Standard |
|---------|---------|---------|--------------|
| DocumentFormat.OpenXml | 3.5.1 [VERIFIED: csproj] | .docx for transcripts and written discovery | Already used by `CaseGuideDocxExporter`, `StorySoFarDocxBuilder`, `ReviewProtocolBuilder`; tests validate with `OpenXmlValidator` |
| Microsoft.Extensions.AI.Abstractions | 10.10.1 [VERIFIED: csproj] | `AIJsonUtilities.CreateJsonSchema` behind `JsonSchemaRegistry` | Generates strict schemas for new DTOs automatically |
| System.Text.Json | in-box (.NET 10) | `discovery-documents.json`, DTO deserialization | `CaseDraft.JsonOptions` (string enums, case-insensitive) is the shared options object |
| xUnit | 2.9.3 + runner 3.1.4 + Test.Sdk 17.14.1 [VERIFIED: tests csproj] | Tests | Existing project `ReelDiscovery.Tests` |

### Reused in-repo components (the real "stack" for this phase)
| Component | File | Use in Phase 8 |
|-----------|------|----------------|
| `ILlmGateway` / `LlmRequest { Tier = LlmTier.Story, AllowBatch = false }` | `Services/Ai/` | Every LLM call |
| `PlanCallRun` / `PlanCallRunner.RunAsync<T>` | `Services/Corpus/PlanCallRunner.cs` | Bounded parallel calls, fatal stop (`SetFatal` cancels the rest), user-cancel rethrow, timeout and refusal classification, cost capture incl. failed attempts. `BlindReviewer` is the closest small precedent |
| `JsonSchemaRegistry` + `StrictSchemaValidator` | `Services/Ai/`, `Tests/Fakes/` | Schema generation and the strict test (≤16 union types) |
| `CaseGuideDocxExporter.WordBuilder`, `ParaFormat`, `StripInvalidXmlChars`, `LongDate` (all `internal`; `InternalsVisibleTo` tests) | `Services/Case/CaseGuideDocxExporter.cs` | Written discovery body, caption and signature |
| `CoreDocumentAssembler.BuildCaption / PleadingPartyLabel / IsPlaintiffSide` | `Services/Case/CoreDocumentAssembler.cs` | Caption text (needs a case-number/title overload, see Pattern 6) |
| `CanonicalValueScanner.Scan / TryParseAmount / AmountsEqual` | `Services/Case/CanonicalValueScanner.cs` | V3-style amount and identifier checks |
| `RenderValidator.ContainsWord` (internal) | `Services/Evidence/RenderValidator.cs` | Whole-word name and term checks |
| `PeopleDirectory.Create(guide, plan)` | `Services/Evidence/PeopleDirectory.cs` | Name resolution (works with `new CorpusPlan()` when no corpus) |
| `KnowledgeGate.KnowersOf / KnowsFact / Knowability / KnowsValue` | `Services/Evidence/KnowledgeGate.cs` | Witness fact and value slice as of the deposition date |
| `GenerationJournal.Replay` → `GenerationRunState.From` → `LedgerMerger.Build(plan, guide, WavePlanner.WaveIndexByThread(WavePlanner.Plan(plan)), state.DeltasByThread)` | `Services/Evidence/` | Corpus detection, ledger slice, written DocIDs. Copy the call chain from `StepCaseSummary.cs:2253` |
| `BlindReviewer.GeneratedEmailDocIds(replay)` | `Services/Qa/BlindReviewer.cs` | The set of actually written email DocIDs |
| `BusinessCalendar.IsBusinessDay / NextBusinessDay` | `Services/Corpus/BusinessCalendar.cs` | Deposition dates (observed federal holidays included) |
| `Apportion.Largest(total, weights, min)` | `Helpers/Apportion.cs` | Request slots per issue, pages per topic |
| `RenderAssignments.StableRank(string)` | `Services/Evidence/RenderAssignments.cs` | SHA-256 deterministic ranking (case number, dates, reporter) |
| `CaseGuide.ComputeHash()`, `GuideStaleness.CoreDocumentHash(doc)` | Models / `Services/Case/GuideStaleness.cs` | D-27 staleness |
| `CaseStore.WriteAtomicAsync(path, bytes, ct)` | `Services/Case/CaseStore.cs` | Sidecar JSON and .docx writes |
| `CaseFolderNaming.CaseTitle / Sanitize` | `Services/Case/CaseFolderNaming.cs` | File names |
| `GenerationCostEstimator` (private `Price`) | `Services/Corpus/GenerationCostEstimator.cs` | Add a public `PriceDiscovery(...)` that reuses `Price(model, in, out, batch:false)` and `QaOverhead` |
| `RegenerateTabDialog` / `ThemedDialog` | `Forms/` | Template for `RegenerateDiscoveryDialog` |
| `FakeLlmProvider` / `CaseTestData.CreateGateway()` / `SampleComplaintDraft / SampleGuidedDraft / SampleCounselDraft / SampleMemoDraft` | Tests | Generator and planner tests without a live provider |

### Alternatives Considered
| Instead of | Could Use | Tradeoff |
|------------|-----------|----------|
| One paragraph per transcript line + exact spacing | Word `lnNumType` (automatic line numbering) | Rejected by D-23. Word decides numbering and pagination, so page:line is not deterministic or testable |
| One paragraph per line | A 2-column table per page (number / text, 25 fixed-height rows) | Tables add cell-margin and row-height rounding risk and are harder to read in Word's navigation. Paragraph-per-line is simpler and validator-friendly |
| Literal page-number paragraph (page heading) | `PAGE` field in a header part | A field is rendered by Word and not inspectable in tests. The federal format already puts a page heading above line 1 (§520.36.50), so a body paragraph is authentic and deterministic |
| LLM-written Definitions/Instructions | Code templates | Code templates are free, deterministic and cannot drift (D-04). The LLM only writes request bodies |
| Extending `WordBuilder` for transcripts | Sibling `TranscriptDocxBuilder` | `WordBuilder` hard-codes Calibri, 0.75" margins and auto spacing in its styles. A sibling with its own styles and geometry (copying its `Save`/schema-order patterns) avoids regressions in three existing exports. Written discovery **does** reuse `WordBuilder` |

**Installation:** none. No new NuGet packages.

**Version verification:** versions were read from the two csproj files on 2026-10-08. No upgrade is needed for this phase.

## Architecture Patterns

### System Architecture Diagram

```
 StepDiscoveryDocuments (UI)                      StepCaseSummary (row: status + Open folder + navigate)
   │ selections ─► CaseDraft.Discovery ─► case.json (CaseStore.SaveAsync)
   │ Generate
   ▼
 DiscoveryContext.Build(draft, folder)  ── reads ──► CaseDraft (CoreDocument, Guide, Complexity, CaseId)
   │                                     └─ optional ► corpus-plan.json + generation-journal.jsonl
   │                                        (CorpusDetector: plan loads, journal readable, plan unchanged, ≥1 written relevant email)
   ▼
 Planners (pure, deterministic)
   DepositionPlanner ── witnesses ► roles (examining/defending/appearances) ► dates ► reporter/videographer
                     ── topics (issues of witness facts) ► page budget per topic (Apportion) ► chunks
                     ── exhibits (corpus DocIDs or canonical docs) ► global exhibit numbers
   WrittenDiscoveryPlanner ── RFP slots per issue (min 1) ► ROG slots (≤25) ► RFA facts (Hot/Key first, chrono) ► genuineness RFAs
   CaseNumber.For(CaseId, Jurisdiction, FilingDate)
   ▼
 DiscoveryGenerator (one unit of work per document; PlanCallRun semaphore = 3)
   for each chunk/document:  prompt(stable guide prefix + per-chunk slice) ─► ILlmGateway (Story, live)
                             ─► DTO ─► DiscoveryValidator.Check ─► violations?
                                   ├─ yes & attempt<3 ─► same prompt + feedback ─► retry
                                   └─ no / exhausted ─► keep best, record warnings
   fatal (Auth/SpendLimit) ─► run.SetFatal ─► remaining docs NotRun; finished docs kept
   ▼
 Layout + render
   Deposition: turns ─► TranscriptLayout (wrap 62 cols, 25 lines/page, page:line map, index pages) ─► TranscriptDocxBuilder
   Written: requests ─► DiscoveryDocxBuilder (caption w/ case no., preliminary stmt, definitions, instructions, requests, signature, COS)
   ▼
 Persist (per finished document, not at run end)
   Discovery\<file>.docx  (CaseStore.WriteAtomicAsync)  then  discovery-documents.json (atomic, whole-file rewrite)
   ▼
 Results grid rows (Generated / Generated with warnings / Failed / Out of date) ◄─ DiscoveryStaleness (guide hash + core hash + caption hash)
```

### Recommended Project Structure
```
Models/Discovery/
├── DiscoverySettings.cs        # CaseDraft.Discovery: selections (witness ids, pages, counts, party ids, enabled flags) + Normalize/clamps
├── DiscoveryDocument.cs        # One generated document: kind, title, file name, status, warnings, source hashes, cost, utc
├── DiscoveryDocumentSet.cs     # discovery-documents.json root: FormatVersion, CaseId, CaseNumber, Documents
├── DepositionContent.cs        # Speakers (+honorifics), turns, exhibits, dates, roles, reporter
└── WrittenDiscoveryContent.cs  # Requests (number, kind, issueIds/factId, text, docId for genuineness)
Services/Discovery/
├── CaseNumber.cs               # Deterministic case number per court system
├── DepositionPlanner.cs        # Eligibility, ranking, defaults, roles, dates, topics, exhibits
├── WrittenDiscoveryPlanner.cs  # Slots per issue, RFA facts, genuineness RFAs, party defaults, memo variants
├── CorpusDetector.cs           # "Usable corpus" + exhibit candidates + ledger slice
├── DiscoveryPrompts.cs         # Stable system prefix + per-chunk user prompts + DTOs (SchemaTypes)
├── DiscoveryValidator.cs       # D-26 checks -> List<DiscoveryViolation>
├── DiscoveryGenerator.cs       # Units of work, retries, fatal/cancel handling, progress, cost
├── TranscriptLayout.cs         # Turns -> lines -> pages; page:line map; front/back matter pages
├── TranscriptDocxBuilder.cs    # Courier New 12pt, exact 24pt, 25 lines/page .docx
├── DiscoveryDocxBuilder.cs     # RFP/ROG/RFA .docx via CaseGuideDocxExporter.WordBuilder
├── DiscoveryBoilerplate.cs     # Code-owned definitions, instructions, rule citations, titles (federal/state/memo)
├── DiscoveryStore.cs           # discovery-documents.json load/save (size cap, version check, never throws on load)
└── DiscoveryStaleness.cs       # Source hashes and IsOutOfDate
UserControls/
├── StepDiscoveryDocuments.cs   # 3 cards (layout)
├── StepDiscoveryDocuments.Run.cs # Generate/cancel/regenerate run wiring (partial, like StepGenerateCorpus.Run.cs)
└── DiscoveryUiText.cs          # Pure copy helpers (unit-tested)
Forms/
└── RegenerateDiscoveryDialog.cs
ReelDiscovery.Tests/Discovery/  # New test folder (see Validation Architecture)
```

### Pattern 1: Optional step inserted without a new CaseStage (D-07, D-09)
**What:** Insert `typeof(StepDiscoveryDocuments)` into `WizardForm.StepTypes` between `StepGenerateCorpus` and `StepCaseSummary`.
- `NavigateToStage` maps by `CaseStage`, and D-09 forbids a new stage, so add `public void NavigateToStep(Type stepType)`. The Case Summary button calls `(FindForm() as WizardForm)?.NavigateToStep(typeof(StepDiscoveryDocuments))`.
- `StepTypeForStage` is unchanged.

**Tests that MUST be updated:**
- `WizardFlowTests.StepTypes_AreTheCaseFlow` and `CorpusStepsSitBetweenGuideAndSummary` assert 8 steps and Summary at index 7.
- `WizardShellRenderTests` asserts "Step N of 8".
- The new count is 9: Discovery is index 7 and Summary index 8. [VERIFIED: ReelDiscovery.Tests/Case/WizardFlowTests.cs, Render/WizardShellRenderTests.cs]

**Step lifecycle:**
- `CanMoveNext` = `!_isGenerating` (D-07).
- `OnEnterStepAsync` rebuilds eligibility from the current draft, recomputes staleness and loads the sidecar.
- `ValidateStepAsync` saves the selections to `CaseDraft.Discovery` and returns true. Never block Next.
- Leaving the step during a run: mirror `StepCaseSummary.ConfirmLeave` (blind review), which `WizardForm.ShowStep` already special-cases. Generalize that check to an interface or add the new step to it. **Generalize** rather than adding another `is` check.

### Pattern 2: Deterministic deposition planning (D-12..D-15, D-21, D-24)
**Eligibility:** `Tier == Key && Role is Party or Staff`.

**Side resolution:**
- `CastMember.PartyId` (individual party), else `OrganizationId → CaseOrganization.PartyId → CoreDocument.Parties[*].Role`.
- Plaintiff side: `Plaintiff | Complainant`. Defending side: `Defendant | Subject | ThirdParty`. This mirrors `CoreDocumentAssembler.BuildCaption`'s grouping.
- A witness whose organization resolves to no party (ThirdParty org kind, agency) is a **non-party witness**. Recommendation: eligible, examined by the propounding (first plaintiff's) counsel, and no defending attorney; objections come from the other party's counsel. Flagged as discretion (A5).

**Ranking:**
- Score = number of facts where the id is in `ActorIds ∪ KnowersOf(fact)`.
- Tie-break: actor count desc, then `StableRank(caseId + memberId)`, then id ordinal.

**Defaults:**
- Preset count (1/2/3). Alternate defending side first: top defending, top plaintiff, next defending…, falling back to whatever side has candidates.
- Cap at 6.

**Roles (counsel guaranteed by the guide):**
- `CaseGuideGenerator.CounselGaps` / validator rule 17 already require every Party organization to have a LawFirm with `RepresentsOrgIds` containing it, plus a cast member `Role == OutsideCounsel` with "Partner" and one with "Associate" in `Title`. [VERIFIED: CaseGuideGenerator.cs:572-603]
- Examining = Partner of the opposing side's firm (first firm representing the first opposing party).
- Defending = Partner of the witness's firm, or the Associate when the Partner is the examining attorney of another deposition on the same day (n/a, dates differ). The simple rule: Partner by default; Associate when `StableRank` picks it, for variety.
- Planners must still **degrade gracefully** when the guide was hand-edited and a firm is missing. Fall back to `CoreDocument.Counsel` (the plaintiff's signing attorney) for the plaintiff side, and to "Counsel for {party}" with no name for the other side. Add a warning to the plan and never throw.

**Honorifics:** There is no gender field anywhere in the model. [VERIFIED: grep "gender|pronoun|honorific" in Models/Services]
- Colloquy labels ("MR. HALPERT:") need one. Recommended: the first chunk's DTO returns `speakers: [{speakerId, honorific}]`, where `honorific` is a string enum `MR|MS|DR`.
- Code validates the ids and persists the honorifics in the sidecar, so later chunks and regenerations stay consistent.
- Witness labels use "THE WITNESS:". Reporter and videographer labels use "THE REPORTER:" / "THE VIDEOGRAPHER:".

**Dates (D-14):**
- Anchor = `FilingDate` if parseable, else the latest fact date. If no dates exist at all, use the timeline end or `CreatedUtc` date.
- Window = [anchor + 3 months, anchor + 9 months], floor = latest dated key fact + 1 day.
- Witness order = ranking order. Spread witnesses across the window: slot i of n gets offset `round(i * span / n) + StableRank(caseId+witnessId) % max(1, span/(2n))` days, then `NextBusinessDay` until it is a business day and unused.
- Start time: `StableRank % 2 == 0 ? 9:30 : 10:00`. Location: `StableRank % 3 == 0 ? remote : "{examining firm name}, {VenueCity}"`.
- All of this is a pure function. Unit-test it for business days, distinctness, ≥ floor and determinism.

**Case number (D-24):**
- `h = StableRank(CaseId.ToString("N"))`, year from `FilingDate` (else timeline end or the current year).
- Federal: `$"{1 + h % 4}:{yy:00}-cv-{10000 + h % 90000:00000}"` (division prefix 1-4, then "cv", then 5 digits).
- State: `$"{yyyy}-CV-{h % 1_000_000:000000}"`, with the caption label "Case No.", or "Cause No." for Petition, as `BuildCaption` already does.
- For memos: Criminal "Grand Jury Matter No. {yyyy}-{nnnn}", Regulatory "CID No. {yyyy}-{nnnn}", InternalCorporate "Matter No. {yyyy}-{nnnn}" (titles are discretion, D-25).
- Persist the computed number in the sidecar, so the number on existing documents never changes if the derivation is later tweaked. [ASSUMED formats; A2]

**Reporter / videographer:** a built-in list of ~12 obviously fictional names and CSR numbers `CSR No. {4-5 digits}`. Pick with `StableRank(caseId + witnessId + "reporter")`. Add these names to the validator's known-names set for that document only.

**Exhibits (D-21):**
- Ordering is global across depositions in witness date order. Exhibit numbers continue across the case: deposition 1 gets 1-6, deposition 2 gets 7-11.
- With a corpus, candidates = planned messages in relevant threads that are (a) in `GeneratedEmailDocIds(replay)`, (b) have the witness in `FromId/ToIds/CcIds`, and (c) are `IsHot`, or link a Hot/Key fact through `FactIds`. Rank: hot first, then fact significance, then `SentAt`. Take 3-8 per deposition scaled by pages (about 1 per 5 pages).
- Without a corpus, use canonical values with `Kind in {DocumentTitle, ContractNumber, ChangeOrderNumber, InvoiceNumber, PurchaseOrder}` whose linked facts the witness knows.
- Exhibit-to-topic assignment = the topic (issue) of the exhibit's first linked fact.
- **Re-numbering on regenerate:** a regenerated deposition must keep its exhibit numbers. Store the assignments in the sidecar and only append new numbers.

### Pattern 3: Strict DTO shapes and chunking (D-01, D-23)
Keep every DTO flat (no recursion), with zero nullable fields. Lists of small objects are fine.

```csharp
// Source: pattern from Services/Qa/BlindReviewPrompts.cs (BlindReviewDto) — nested internal classes
internal class DepositionChunkDto
{
    [JsonPropertyName("speakers")]
    [Description("Only in the first part: one entry per speaker id given, with MR, MS or DR; empty otherwise")]
    public List<SpeakerDto> Speakers { get; set; } = new();

    [JsonPropertyName("turns")]
    [Description("The testimony for this topic in order")]
    public List<TurnDto> Turns { get; set; } = new();

    [JsonPropertyName("peopleNamed")]
    [Description("Full names of every person mentioned in the turns")]
    public List<string> PeopleNamed { get; set; } = new();
}

internal class TurnDto
{
    [JsonPropertyName("kind")]
    [Description("Q, A, Colloquy, Objection or Parenthetical")]
    public TurnKind Kind { get; set; }          // string enum via JsonStringEnumConverter

    [JsonPropertyName("speakerId")]
    [Description("Speaker id exactly as listed (empty for Q and A)")]
    public string SpeakerId { get; set; } = string.Empty;

    [JsonPropertyName("text")]
    [Description("What was said, without the Q/A label or speaker name")]
    public string Text { get; set; } = string.Empty;

    [JsonPropertyName("exhibitNumber")]
    [Description("Exhibit number this turn first shows the witness, or 0")]
    public int ExhibitNumber { get; set; }
}

internal class RequestSetDto   // RFP / ROG / RFA bodies; code numbers them
{
    [JsonPropertyName("requests")]
    public List<RequestDto> Requests { get; set; } = new();
}

internal class RequestDto
{
    [JsonPropertyName("slot")]
    [Description("Slot number exactly as given")]
    public int Slot { get; set; }                 // code maps slot -> issue/fact id; the LLM never chooses keys

    [JsonPropertyName("text")]
    public string Text { get; set; } = string.Empty;
}
```

**Code-placed exhibit markers:**
- The model reports `exhibitNumber` on the turn that introduces it.
- The layout engine inserts "(Deposition Exhibit N was marked for identification.)" as a parenthetical **before** that turn, using code text.
- If the model never introduces an assigned exhibit, the validator flags it (feedback: "introduce Exhibit N in this topic").

**Chunking:**
- One chunk ≈ one examination topic, capped at ~8 testimony pages. A topic budget above 8 pages is split into "part 1/2".
- Each chunk asks for a target number of turns ≈ `pages × 14` (≈1.8 lines per turn at 62 cols, 25 lines/page). [ASSUMED ratio; A3. Calibrate on the first live run.]
- Chunks of one deposition run **sequentially**: the next chunk's prompt carries the last ~6 turns as context plus "topics already covered".
- Documents run in parallel under the semaphore (3, like `GenerationRunner.MaxParallelThreads` / `CaseGuideGenerator.MaxParallelCalls`).

**Output budget:**
- 8 pages ≈ 200 lines × ~50 chars ≈ 10k chars ≈ 2.5-3k text tokens plus ~15 JSON tokens/turn × ~110 turns ≈ 1.7k, so ~4.5k tokens.
- `LlmRequest.MaxOutputTokens` includes reasoning tokens [VERIFIED: LlmRequest.cs comment]. Use **16,000** per chunk (the default), like `CaseGuideGenerator.FactsMaxOutputTokens`.
- Written sets: one call per set for ≤40 requests. Split RFPs into calls of ≤25 slots when count > 40 (80 max → 4 calls).

**Prompt caching (D-06):**
- `SystemPrompt` must be **byte-identical** across all chunks and documents of one case: case background from structured guide data (issues with titles and summaries, parties, the counsel/cast roster, canonical values), format rules, and rule-of-thumb style.
- Everything per-witness or per-chunk goes in `UserPrompt`.
- Story-tier models have `MinCacheablePrefixTokens` 512 (Claude) / 1024 (OpenAI) [VERIFIED: Models/AIModelConfig.cs]. A guide-derived prefix easily exceeds that.

### Pattern 4: "Everything is a line" transcript layout (D-23)
**Geometry**, based on the federal format §520.36 [CITED: Guide to Judiciary Policy Vol. 6 Ch. 5, via wyd.uscourts.gov Transcript_Format.pdf]:
- Letter page 12240×15840 twips.
- Left page margin 1440 (1.0"); line number right-aligned at a tab of 600; text starts at a left tab/hanging indent of 1080 → text begins at 1.75" from the edge (federal).
- Right margin 540 (3/8") → text width 12240 − 1440 − 1080 − 540 = 9180 twips = 6.375".
- Courier New 12 pt advance = 7.2 pt = 0.1" (10 cpi), so 63 characters fit. **MaxCharsPerLine = 62** leaves one character of slack. [ASSUMED Courier New advance = 0.6 em; A4. Verify by eye in Word during UAT.]
- Exact line spacing 480 twips (24 pt = "double-spaced" 12 pt), `SpaceBefore = SpaceAfter = 0`.
- Each page = 1 page-heading paragraph + 25 line paragraphs = 26 × 480 = 12480 twips. Top and bottom margins 1080 each leave 13680 available, i.e. 1200 twips of slack, so Word never inserts its own break before line 25.
- Every page-heading paragraph after the first has `<w:pageBreakBefore/>`.

**Indentation, using literal spaces (monospaced, `xml:space="preserve"` already set by the builder):**
- Q/A: `"Q.   text"` (label at col 1, text at col 5). Continuation lines at col 1 (federal 520.36.35(a)).
- Colloquy and objections: 9 spaces + `"MR. HALPERT:"` + 2 spaces + text. Continuation at col 1 (520.36.35(b)).
- Parenthetical: 4 spaces + `"("` + text + `")"`. Continuation at col 5 (520.36.55 requires the opening parenthesis at the 5th space; continuation is discretion).
- Headings: `"EXAMINATION"` centered, then `"BY MR. HALPERT:"` at col 1 (customary).
- Front matter centered by padding: `pad = (62 - len) / 2`.

**Wrapping:**
- Greedy word wrap on spaces to ≤62 columns, using the first-line prefix width for line 1 and 0 (or 4 for parentheticals) for continuation lines.
- Hard-break any single token longer than the remaining width (URLs, DocIDs are short).
- Normalize whitespace and strip control characters first (`StripInvalidXmlChars` and `\s+` → space).

**Pagination:**
- Fill pages with exactly 25 lines. Pad the final testimony page with empty numbered lines (real transcripts number blank lines too).
- Record `(page, line)` for every turn start, exhibit marking and examination start. That map drives the examination index ("Examination by Mr. X ..... 5") and the exhibit index (description and page marked).
- **Index chicken-and-egg:** compute the testimony pages first with a placeholder for the front-matter page count. The front matter has a fixed page count (caption 1, appearances 1, index 1, plus 1 more if the exhibit index exceeds the lines), so compute that count first and offset.

**Excerpt semantics (D-15):**
- "Pages-per-excerpt" counts **testimony pages**; front and back matter are extra.
- The caption shows "EXCERPT" and "Pages 1 - N".
- Number pages continuously from 1. Real excerpts often keep the original page numbers, but continuous numbering is simpler and still credible (A6).

**Back matter:**
- "(Whereupon the deposition concluded at h:mm p.m.)". Code computes the end time as start + ~45 s per line, rounded to 5 min, kept ≤ 7 h per FRCP 30(d)(1).
- Witness signature page ("I, NAME, have read … subject to the changes on the errata sheet", FRCP 30(e)(1) 30-day review).
- Errata sheet with blank Page/Line/Change/Reason rows rendered as underscores.
- Reporter's certificate ("duly sworn", "true record", CSR No.), per FRCP 30(f)(1).
- All of this is code text, laid out by the same engine.

### Pattern 5: Written discovery slot planning (D-16..D-19)
**RFP:**
- `count = clamp(user, 5, 80)`. `slots = Apportion.Largest(count, weights: per-issue fact count (min weight 1), min: 1)`.
- If `count < issues.Count`, the total cannot cover the minimum. Raise the count to `issues.Count` and show a UI note; never drop an issue.
- Each slot carries `IssueIds = [issue.Id]` plus a code-chosen "anchor": custodian ids (key cast of the responding party who know the issue's facts), canonical project/contract names, and a date range from the min/max dates of the issue's facts.
- The LLM writes "All Documents and Communications Concerning …" text constrained by the anchor.

**ROG:**
- Hard constant `MaxInterrogatories = 25` (FRCP 33(a)(1)). `count = clamp(user, issues.Count, 25)`.
- Slot mix: 1-2 "identify persons with knowledge" slots, the remainder contention slots apportioned per issue with min 1.
- **No subparts:** lettered subparts count against the 25-limit. The prompt forbids them and the validator flags `\(\s*[a-z]\s*\)|\b[a-z]\)` patterns at list positions.
- If more issues than 25 exist (only possible on hand-edited Complex guides), merge low-weight issues into shared slots with multiple `IssueIds`.

**RFA:**
- `count = clamp(user, 1, 50)`. Facts with a parseable date ordered by Significance (Hot, Key, Supporting; D-19 says "Key significance first", so read it as Hot+Key before Supporting), then date, then id. Take the first `count - genuinenessCount`.
- Genuineness RFAs (corpus only): `min(3, max(1, count/10))` hot DocIDs linked to the selected facts.
- Each fact RFA: the code composes the **prefix** "Admit that on {LongDate(fact.Date)}," and the LLM writes the remainder.
- The validator then requires (a) every Amount-kind canonical value linked to the fact to appear verbatim (or `AmountsEqual`), and (b) every actor's full name or their organization's short name (for organizational actors) to appear. This guarantees "date verbatim" by construction (Pattern 8).

**Parties (D-16):**
- Defaults: first party with Role Plaintiff/Complainant → first Defendant/Subject.
- Persist the party ids. If a persisted id no longer exists, fall back to the defaults with a note.

**Memo variants (D-25):** `WrittenDiscoveryPlanner.Availability(doc)` returns, per kind, Available, Hidden + note, or Renamed title:

| MatterKind / Perspective | RFP renders as | ROG | RFA |
|---|---|---|---|
| Civil pleading (Complaint/Petition) | "…FIRST SET OF REQUESTS FOR PRODUCTION…" | Interrogatories | RFAs |
| Memo, Criminal | "SUBPOENA RIDER — DOCUMENTS TO BE PRODUCED" (grand-jury style) | hidden | hidden |
| Memo, Regulatory | "CIVIL INVESTIGATIVE DEMAND — DOCUMENT REQUESTS" | "CIVIL INVESTIGATIVE DEMAND — WRITTEN INTERROGATORIES" | hidden |
| Memo, InternalCorporate / Civil memo | "PRE-SUIT REQUEST FOR PRESERVATION AND PRODUCTION OF DOCUMENTS" | hidden or "Written Questions" (discretion) | available? (D-25 hides only Criminal/Regulatory; recommend available but titled "Requests to Admit") |
| Deposition (any memo) | "TRANSCRIPT OF SWORN WITNESS INTERVIEW" (Criminal/InternalCorporate) or "EXAMINATION UNDER OATH" (Regulatory) | | |

### Pattern 6: Captions and boilerplate (D-04, D-17, D-20)
**Caption:**
- `CoreDocumentAssembler.BuildCaption(doc)` hard-codes `"{label} ____________"` and the core document title as the last line. [VERIFIED: CoreDocumentAssembler.cs:126-156]
- Add an overload `BuildCaption(CoreDocument doc, string caseNumber, string title)` that the existing method delegates to (`caseNumber = "____________"`, `title = core title + jury suffix`), so existing tests stay green.
- `CaseGuideDocxExporter.WriteCaption` and `WriteSignatureBlock` are `private static`. Make them `internal static` so `DiscoveryDocxBuilder` reuses them verbatim.
- Federal captions need no state line. State captions include the county line. Both are already handled.

**Signature block:**
- Built from the **propounding party's** outside-counsel Partner in the cast: name, firm org name, email, phone. Fall back to `CoreDocument.Counsel` when the propounding party is the original plaintiff.
- Do not invent bar numbers for the other side. Omit the bar line when absent; the existing `BuildSignatureBlock` already skips an empty BarNumber.
- Dated = the service date. Recommended: FilingDate + ~45-60 days, a business day, before the first deposition.

**Certificate of service:** code text ("I hereby certify that on {date}, I served the foregoing on counsel of record for {responding party} by electronic mail at {email}"). The email is the responding firm partner's cast email.

**Definitions** (code, parameterized): "You/Your" (responding party name + short name + affiliates), "Document" (with an FRCP 34(a)(1)(A) reference when federal), "Communication", "Concerning", "Person", "Identify", "Project", and "Relevant Period" = TimelineStart-TimelineEnd. Add one defined term per Amount/ProjectName/ContractNumber canonical value (for example "the Subcontract" = value label).

**Instructions** (code):
- Relevant period.
- ESI form (native with metadata, per FRCP 34(b)(1)(C)).
- Privilege log (FRCP 26(b)(5)(A)).
- Continuing duty to supplement (FRCP 26(e)).
- 30-day response (Rules 33(b)(2), 34(b)(2)(A), 36(a)(3)).
- RFAs: "matters not timely answered are deemed admitted (Rule 36(a)(3))".
- State court: "the [State] Rules of Civil Procedure", with no rule numbers (D-20).

### Pattern 7: Units of work and run control (D-05, D-28)
**Use `PlanCallRun` / `PlanCallRunner` exactly as `BlindReviewer.RunAsync` does:**
- `using var run = new PlanCallRun(ct, MaxParallel (3), retryHint: "Click Regenerate… to try again.")`.
- One task per document; `await Task.WhenAll(tasks)` so finished documents are persisted before cancel propagates.
- Each document task is never-throws except `OperationCanceledException` on user cancel.

**Within a document:**
- For each chunk: up to 3 attempts (1 + 2 retries, D-26).
- After each successful document: build bytes, `WriteAtomicAsync` the .docx into `Discovery\`, then upsert the record and `WriteAtomicAsync` the sidecar.
- Both writes go through a **single-writer lock** (`SemaphoreSlim(1)`), because parallel documents upsert the same JSON file.
- Write with `CancellationToken.None` once the content is paid for (the `BlindReviewer` "a verdict that was paid for is always kept" precedent).

**Fatal handling:** when `run.IsFatal`, the remaining documents get status Failed with the fatal message (or stay "Not generated"). Finished ones stay.

**Progress:** `IProgress<DiscoveryProgress>` with document and chunk counters. The UI marshals with `InvokeRequired`.

**Cost:**
- Sum `PlanCallOutcome.Cost` (includes failed attempts) per document and store `CostUsd` + `CostDetail` (`CostBreakdown`) on the record.
- The ticker updates automatically because the gateway comes from `WizardState.CreateGateway()` (the tracker is attached).

### Pattern 8: DiscoveryValidator (D-26)
The `RenderValidator` rules are private and bound to `RenderedThread` / `ThreadRenderContext`. [VERIFIED: RenderValidator.cs]
- **Do not** try to wrap ancillary text as a fake thread.
- Write `DiscoveryValidator` with its own rules, reusing the public and internal helpers.
- **Promote** the needed private regexes (`NameBigram`, `HonorificName`, `MonthDate`, `IsoDate`, `IdentifierPrefix`, `Whitespace`) and the `NotSurnames` / `Honorifics` sets to a shared `internal static partial class TextScan` (in `Services/Evidence` or `Helpers`). Point `RenderValidator` at it so both use one copy.

| Code | Rule | Applies to |
|------|------|------------|
| D1 | Structure: chunk returned ≥ 50% of the target turns; no empty text; Q/A alternate sensibly (no two A without Q, allowing colloquy between); every assigned exhibit introduced | Depositions |
| D2 | Unknown people: `peopleNamed` and bigram/honorific scans resolve to cast (`PeopleDirectory`) or this document's reporter/videographer/counsel | All |
| D3 | Canonical variants: amounts within ±15% (`RenderValidator.VariantWindow`) of a canonical amount but not equal; identifiers with a canonical prefix that are not registered | All |
| D4 | Keys: every request slot returned exactly once; no unknown slot numbers (code owns issue and fact ids, so this replaces "references an existing issue/fact id") | Written |
| D5 | RFA fidelity: fact's canonical amounts present (`AmountsEqual`); each actor named; no other date of a different fact contradicts (prefix is code-owned) | RFA |
| D6 | Time travel: any `MonthDate`/`IsoDate` mention later than the deposition date; any ledger/fact content the witness could not know as of that date (KnowledgeGate) | Depositions |
| D7 | Interrogatory subparts: lettered or numbered sub-lists inside one interrogatory | ROG |
| D8 | Real-world entity leak: original names in `CaseDraft.Substitutions` (with aliases) appearing in text (D-02) | All |

- **Keep-best rule:** fewer violations wins, ties go to the later attempt. A structurally broken result (D1) loses to any non-structural one, copying `ThreadUnitOfWork`'s comparator. [VERIFIED: ThreadUnitOfWork.cs:209-224]
- Feedback text uses the `EvidencePrompts.FeedbackHeader` style: "Problems in the previous attempt (fix all of them and rewrite the whole part):".

### Pattern 9: Corpus detection (Claude's discretion)
`CorpusDetector.Detect(draft, folder)` returns `Usable` only when ALL hold:
- `draft.Corpus?.HasPlan`
- `CorpusPlanStore.Load(folder, PlanId).Succeeded`
- `GenerationJournal.Replay(folder, plan)` is `Readable && !PlanChanged`
- `GeneratedEmailDocIds(replay)` contains ≥1 DocID of a relevant thread

It returns `(plan, replay, runState, ledger, writtenDocIds)`. Wrap it in try/catch and fall back to "no corpus" on any exception (never throws).
- Subjects for exhibits: the journal message `Subject` when present, else the planned thread subject.
- Exhibit and genuineness DocIDs are taken only from the written set, so they always exist in `Mailboxes\`.
- Store the corpus PlanId on each document record. If the plan changes later, the document is out of date (DocIDs may no longer exist).

### Pattern 10: Staleness (D-27)
- `DiscoveryStaleness.SourceHashes(draft)` = `(GuideHash: draft.Guide.ComputeHash(), CoreHash: GuideStaleness.CoreDocumentHash(doc), CaptionHash: hash(Jurisdiction, Counsel, DocumentType, MatterKind, MemoPerspective, Title))`.
- `CaseDraft.Revision` bumps on every guide edit (14+ sites in `CaseGuideEditing`) [VERIFIED]. It is a poor "core document revision" signal. Use the content hashes instead; this is what D-27 intends.
- `CoreDocumentHash` deliberately excludes the signature block and jurisdiction [VERIFIED: GuideStaleness.cs:23-36], so the extra caption hash is needed for discovery captions.
- Out of date = any hash differs, or the document cited the corpus and the current PlanId differs.

### Pattern 11: Persistence (D-29)
**`CaseDraft.Discovery`:**
- `DiscoverySettings` holds `WitnessIds`, `PagesPerExcerpt`, `RfpEnabled/Count`, `RogEnabled/Count`, `RfaEnabled/Count`, `PropoundingPartyId`, `RespondingPartyId`, and `UserTouched` (so defaults are only applied while untouched).
- Its `Normalize()` clamps all counts (pages 10-60, RFP 5-80, ROG ≤25, RFA ≤50, witnesses ≤6), drops null or duplicate ids, and nulls → empty.
- Call it from `CaseDraft.Normalize()`.

**Format version:**
- Bump `CaseDraft.CurrentFormatVersion` 3 → 4. Older builds then refuse case.json files that carry Discovery instead of silently dropping it (`CaseStore.Load` checks `PeekFormatVersion > Current`).
- Update `CaseDraftSerializationTests` (`Assert.Equal(3, …)`). [VERIFIED: CaseDraftSerializationTests.cs:12]

**`discovery-documents.json`:**
- Root `{FormatVersion, CaseId, CaseNumber, ExhibitAssignments, Documents[]}`.
- Load: size cap (e.g. 20 MB), newer-version refusal, CaseId mismatch → ignore with a reason. Never throws (the `CorpusPlanStore.Load` shape).
- Path: `Path.Combine(caseFolder, "discovery-documents.json")`, built from the constant only.

**`.docx`:**
- `Path.Combine(caseFolder, "Discovery", fileName)`.
- `fileName = CaseFolderNaming.Sanitize($"{folderName} - Deposition of {FullName} (Excerpt)") + ".docx"`.
- Guard the total path length (< 240 chars): truncate the case part first.
- When reading names back from the sidecar, accept them only after an `IsDiscoveryFileName`-style check (no separators, no "..", `.docx` suffix), as in `StorySoFarDocxBuilder.IsStoryFileName` (T-05-33).
- File locked by Word on rewrite: reuse `StepCaseSummary.IsFileOpenElsewhere` logic (sharing/lock violation). Mark the document "Generated - file open in Word, close it and click Regenerate…", or write `…(2).docx`. Recommend: report and keep the JSON content, with an "Open/Retry write" path.

**Start Over / `GenerationReset`:** must not touch `Discovery\` or the sidecar. It deletes only journal-listed paths under Mailboxes plus constant files [VERIFIED: GenerationReset.cs]. Add a regression test that Start Over leaves them.

**Case Summary cost row:**
- The row reads only `GenerationJournal.Replay(...).SpentCostUsd`, and `LoadQa` returns early when there is no plan. [VERIFIED: StepCaseSummary.cs:1609-1625]
- Discovery spend lives in the sidecar (Start Over deletes the journal, so the journal is the wrong home). The Summary must add `DiscoveryDocumentSet.TotalCostUsd` and its `CostBreakdown` into the cost row. Extend `GenerationUiText.CostRow` with a "discovery documents" item, or add it into input/output.
- This has to work when there is no corpus plan too: move the cost-row computation out of the plan-required branch.

### Anti-Patterns to Avoid
- **Feeding the raw complaint text to prompts** (P-Ancillary). Use structured issues, facts, cast and values only.
- **Letting the LLM number requests, pick issue/fact ids, assign exhibits or format pages.** Code owns all of these (D-04).
- **Word automatic line numbering or `PAGE` fields** for page:line. Neither is deterministic or testable (D-23).
- **Writing the sidecar from parallel tasks without a lock**, or writing it only at run end (cancel loses paid work).
- **Reusing `NavigateToStage`** by adding a `CaseStage` (D-09).
- **Generating a new case number on every run.** Persist it once.
- **`Random` / `Guid.NewGuid()`** for dates, reporters or numbers.
- **Silent regeneration when stale** (D-27).

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| Bounded parallel LLM calls with fatal-stop and cancel semantics | New semaphore/try-catch scaffolding | `PlanCallRun` + `PlanCallRunner.RunAsync<T>` | Already classifies timeout vs user cancel vs fatal, captures the cost of failed attempts, and has tests |
| JSON schema for DTOs | Hand-written schema strings | `JsonSchemaRegistry` (+ `CaseSchemaTests` registration) | Strict-mode rules for both providers are already encoded |
| Atomic, OneDrive-safe writes | `File.WriteAllBytes` | `CaseStore.WriteAtomicAsync` | Temp + move with retries already solved |
| Amount parsing/equality ("$7.5 million" == "$7,500,000") | New regex | `CanonicalValueScanner.TryParseAmount / AmountsEqual / Scan` | Edge cases (K/M/MM suffixes, "$12 Main Street") already handled |
| Business days and holidays | DayOfWeek checks | `BusinessCalendar.IsBusinessDay / NextBusinessDay` | Observed federal holidays cached |
| Integer apportioning with minimums | Loops | `Apportion.Largest(total, weights, min)` | Largest-remainder with deterministic ties |
| Deterministic pseudo-randomness | `Random(seed)` / `GetHashCode` | `RenderAssignments.StableRank(string)` | SHA-256 stable across processes |
| Who-knew-what as of a date | Ad-hoc filters | `KnowledgeGate.KnowersOf / KnowsFact / Knowability / KnowsValue` | Same semantics as corpus generation (consistency) |
| Ledger reconstruction | Reading the journal by hand | `GenerationJournal.Replay` → `GenerationRunState.From` → `LedgerMerger.Build` | The exact chain `StepCaseSummary` uses |
| XML-invalid characters in .docx | Custom sanitizer | `CaseGuideDocxExporter.StripInvalidXmlChars` (WordBuilder applies it) | Lone surrogates and control chars handled |
| File-name sanitizing | `Path.GetInvalidFileNameChars` loops | `CaseFolderNaming.Sanitize` | Reserved names, trailing dots, length |
| Regenerate dialog shell | New Form | `ThemedDialog` (template: `RegenerateTabDialog`) | DPI, theme, LDU helpers |

**Key insight:** this phase is low-risk when it composes existing, tested primitives. Every hand-rolled replacement re-opens bugs that Phases 2-7 already closed (cancel versus fatal, file locks, amount parsing, schema strictness).

## Runtime State Inventory

Not a rename/refactor phase, but it adds persisted state. Recorded for the planner:

| Category | Items Found | Action Required |
|----------|-------------|------------------|
| Stored data | case.json gains `Discovery` (FormatVersion 3→4); new `discovery-documents.json`; new `Discovery\*.docx` | Code edit + migration via `Normalize()` (v3 files load with `Discovery = null` → defaults) |
| Live service config | None. No external services beyond the existing LLM providers | none |
| OS-registered state | None | none |
| Secrets/env vars | None new. Uses existing provider keys via `WizardState` | none |
| Build artifacts | None. No new packages; test project picks up new .cs automatically | none |

## Common Pitfalls

### Pitfall 1: Word re-paginates and page:line drifts
**What goes wrong:** Word inserts its own page break before line 25, or a long line wraps to two visual lines, so the code's page:line map no longer matches what the user sees.
**Why it happens:** auto line spacing, or text wider than the measure; the paragraph height exceeds the remaining body height; widow/orphan control; a font fallback when Courier New is missing.
**How to avoid:**
- Exact spacing (`LineRule = Exact`, 480).
- `SpaceBefore`/`SpaceAfter` = 0 on every transcript paragraph.
- ≤ 62 characters per line, asserted by tests.
- ≥ 1 line-height of slack in the body.
- `pageBreakBefore` on each page heading.
- Set `RunFonts` for Ascii/HighAnsi/ComplexScript/EastAsia to "Courier New" in the transcript styles.
- Courier New ships with Windows.
**Warning signs:** in UAT, open the .docx in Word: page count ≠ code page count, or line 25 appears at the top of the next page.

### Pitfall 2: Schema-order violations in new OpenXml elements
**What goes wrong:** `OpenXmlValidator` reports errors. Word may refuse to open the file or "repair" it.
**Why it happens:** CT_PPrBase order is pStyle, keepNext, keepLines, **pageBreakBefore**, framePr, widowControl, numPr, suppressLineNumbers, pBdr, shd, **tabs**, suppressAutoHyphens, …, **spacing**, **ind**, …, **jc**. CT_SectPr order is pgSz, pgMar, paperSrc, **pgBorders**, lnNumType, **pgNumType**, cols…
**How to avoid:** append in that order (the existing builder comments document part of it), and validate every builder output in tests with `OpenXmlValidator` (the `CaseGuideDocxExporterTests.OpenAndValidate` pattern). [ASSUMED element order from ECMA-376 knowledge; enforced by the validator test, A7]

### Pitfall 3: Turn counts miss the page budget
**What goes wrong:** the model returns 40 turns when 110 were asked, so the excerpt is 9 pages instead of 25. Or it returns 300 and overflows.
**How to avoid:**
- Ask per chunk with explicit numbers ("about 110 turns; at least 90").
- D1 flags < 50% of the target and triggers a retry.
- After all chunks, accept ±25%. If short after retries, keep the result and show it as a warning ("23 of 25 pages").
- Trim overflow only at a turn boundary inside the **last** topic, never mid-exam closing. Then always append the code closing lines.

### Pitfall 4: Interrogatory count exceeds 25 through subparts
**What goes wrong:** "Identify (a) … (b) … (c) …" counts as discrete subparts under FRCP 33(a)(1). [CITED: law.cornell.edu/rules/frcp/rule_33]
**How to avoid:** use the constant `MaxInterrogatories = 25`, prompt "one question per interrogatory, no lettered or numbered parts", and validator rule D7. Clamp the UI control maximum to 25.

### Pitfall 5: Fictional-name or real-entity regressions
**What goes wrong:** the prompt says "use fictional names" and the model renames "Michael Scott". Or real company names from the original scenario leak back.
**How to avoid:**
- Copy `CaseGuidePrompts`' rules verbatim: firm position, names exactly as listed. Never instruct "fictional".
- Validator D8 checks `Substitutions` originals and aliases.
- Add a prompt-invariant test like `CaseGuidePromptInvariantTests` / `CorpusPromptInvariantTests` (no "fictional", no raw complaint paragraphs in prompts).

### Pitfall 6: Parallel sidecar writes and cancellation lose paid work
**What goes wrong:** two documents finish together and the last writer wins. Or a cancel during the write drops a finished document.
**How to avoid:** one `SemaphoreSlim(1)` writer; read-modify-write of the in-memory set; write with `CancellationToken.None` after content exists; `Task.WhenAll` before propagating cancel.

### Pitfall 7: Time travel in testimony
**What goes wrong:** a witness on a 2025-03 deposition discusses a 2025-06 event. Or the witness "knows" facts only others knew.
**How to avoid:** deposition date ≥ the latest fact date (planner). Prompt slice = `KnowsFact(f, witness, depoDate)` facts plus `KnowsValue` values plus, when a corpus exists, ledger entries with `KnownByIds ∋ witness` and date ≤ the deposition date. Validator D6 date scan.
**Note:** examining counsel may *show* the witness an exhibit the witness did not author. That is legitimate. The knowledge rule applies to what the witness **asserts knowing at the time**, so keep D6 to date checks plus "facts not in the witness slice cited as personal knowledge". Accept some false negatives rather than over-flagging (treat over-strict checks as warnings to tune, as RenderValidator A7 did).

### Pitfall 8: Init-order null refs and 150% DPI clipping in the new step
**What goes wrong:** this hit Phases 2-3: handlers fire during control construction (`ValueChanged` while setting `Value`) before other fields exist.
**How to avoid:**
- Wire handlers after all controls exist. Use content-sized rows and no fixed pixels.
- Render tests at 144 DPI for: empty (no guide), ready (default selections), running, results (warnings + out of date + failed), memo-criminal (RFA/ROG hidden note), at both `RenderHarness.Large` and `WizardMin`.
- Inspect the PNGs.

### Pitfall 9: Case Summary cost row misses discovery spend when there is no corpus
**What goes wrong:** `LoadQa` returns before computing cost when `plan == null` [VERIFIED].
**How to avoid:** compute the cost row from the journal (if any) plus the discovery sidecar, independent of the plan.

### Pitfall 10: File name / path length
**What goes wrong:** the case folder name (≤ `CaseFolderNaming.MaxLength`) + " - Deposition of Firstname Lastname (Excerpt).docx" + `\Discovery\` under a deep OneDrive path can exceed MAX_PATH.
**How to avoid:** cap the case-name part, test with a long folder name, and surface write errors on the row (never throw).

## Code Examples

### Transcript line paragraph (OpenXml 3.x)
```csharp
// Source: pattern of CaseGuideDocxExporter.WordBuilder.BuildParagraph (schema-ordered pPr), extended for transcript lines.
// Order inside pPr: pStyle, pageBreakBefore, tabs, spacing, ind  (CT_PPrBase)
private static Paragraph TranscriptLine(int lineNumber, string text)
{
    var pPr = new ParagraphProperties(
        new ParagraphStyleId { Val = "TranscriptLine" },
        new Tabs(
            new TabStop { Val = TabStopValues.Right, Position = 600 },   // line number, right-aligned
            new TabStop { Val = TabStopValues.Left, Position = 1080 }),  // text column (1.75" from page edge)
        new SpacingBetweenLines { Before = "0", After = "0", Line = "480", LineRule = LineSpacingRuleValues.Exact },
        new Indentation { Left = "1080", Hanging = "1080" });

    var run = new Run(
        new TabChar(),
        new Text(lineNumber.ToString(CultureInfo.InvariantCulture)),
        new TabChar(),
        new Text(StripInvalidXmlChars(text)) { Space = SpaceProcessingModeValues.Preserve });
    return new Paragraph(pPr, run);
}

// Page heading opens every page after the first; it is not one of the 25 lines (federal §520.36.50)
private static Paragraph PageHeading(int page, string heading, bool first) =>
    new(new ParagraphProperties(
            new ParagraphStyleId { Val = "TranscriptLine" },
            first ? null : new PageBreakBefore(),           // filter nulls before constructing in real code
            new Tabs(new TabStop { Val = TabStopValues.Right, Position = 9720 }),
            new SpacingBetweenLines { Before = "0", After = "0", Line = "480", LineRule = LineSpacingRuleValues.Exact }),
        new Run(new Text(heading) { Space = SpaceProcessingModeValues.Preserve }, new TabChar(),
                new Text(page.ToString(CultureInfo.InvariantCulture))));
```

### Transcript section geometry and style
```csharp
// Letter, left 1.0", right 3/8", top/bottom 0.75"; body height 13680 >= 26 x 480 = 12480 (1200 twips slack)
new SectionProperties(
    new PageSize { Width = 12240U, Height = 15840U },
    new PageMargin { Top = 1080, Right = 540U, Bottom = 1080, Left = 1440U, Header = 540U, Footer = 540U, Gutter = 0U });

// Style: Courier New 12 pt everywhere (sz is half-points)
new StyleRunProperties(
    new RunFonts { Ascii = "Courier New", HighAnsi = "Courier New", ComplexScript = "Courier New", EastAsia = "Courier New" },
    new FontSize { Val = "24" }, new FontSizeComplexScript { Val = "24" });
```

### Layout engine core (pure, testable)
```csharp
public const int LinesPerPage = 25;
public const int MaxCharsPerLine = 62;

// Q/A text at col 5; colloquy label at col 10, text 3 spaces after the colon; parenthetical "(" at col 5
internal static IEnumerable<string> FormatTurn(TranscriptTurn turn, Func<string, string> label) => turn.Kind switch
{
    TurnKind.Q => Wrap("Q.   " + turn.Text, MaxCharsPerLine, continuationIndent: 0),
    TurnKind.A => Wrap("A.   " + turn.Text, MaxCharsPerLine, continuationIndent: 0),
    TurnKind.Parenthetical => Wrap("    (" + turn.Text.Trim('(', ')') + ")", MaxCharsPerLine, continuationIndent: 5),
    _ => Wrap(new string(' ', 9) + label(turn.SpeakerId) + ":  " + turn.Text, MaxCharsPerLine, continuationIndent: 0)
};

// Pages: chunk the flat line list into 25s; the last page is padded with "" so every page has 25 numbered lines
internal static List<TranscriptPage> Paginate(IReadOnlyList<TranscriptLine> lines, int firstPageNumber) { /* ... */ }
```

### Unit of work skeleton (mirrors BlindReviewer.ReviewOneAsync + ThreadUnitOfWork attempt loop)
```csharp
var outcome = await PlanCallRunner.RunAsync<DiscoveryPrompts.DepositionChunkDto>(_llm, request, run);
cost.Add(outcome.Cost);
if (outcome.Value == null)
{
    if (run.IsFatal) return DocumentOutcome.NotRun(run.FatalError, cost);
    if (best != null) break;                       // keep earlier attempt
    return DocumentOutcome.Failed(outcome.Error, cost);
}
var violations = DiscoveryValidator.CheckChunk(outcome.Value, chunkContext);
// keep-best comparator: non-structural beats structural; fewer violations; tie -> later attempt
```

### Request slot apportioning
```csharp
// Every issue gets >= 1 RFP, the rest weighted by how many facts the issue has
var weights = issues.Select(i => (double)Math.Max(1, guide.Facts.Count(f => f.IssueIds.Contains(i.Id)))).ToList();
var total = Math.Max(count, issues.Count);
var perIssue = Apportion.Largest(total, weights, min: 1);
```

## State of the Art

| Old Approach | Current Approach | When Changed | Impact |
|--------------|------------------|--------------|--------|
| Typewriter transcript pages with preprinted margin lines | Electronic transcripts (PDF/ASCII/Word) must still follow the 25-line, 10-cpi format; hyphenation at line end no longer required | JCUS-MAR 95 (hyphenation dropped); JCUS-SEP 12 (electronic media) [CITED: Guide to Judiciary Policy Vol. 6 Ch. 5 §520.13] | Courier 10-cpi, 25-line layout remains the realism target. No hyphenation needed in the wrap engine |
| In-person depositions | "Taken remotely via videoconference" is common | Post-2020 practice [CITED: .planning/research/FEATURES.md §C] | D-14 location option |
| Paper RFP production | ESI form specified in the request (FRCP 34(b)(1)(C)) | 2006 ESI amendments [CITED: law.cornell.edu/rules/frcp/rule_34] | Instructions must specify native/ESI form |

**Verified rule facts used by this phase:**
- FRCP 33(a)(1): "no more than 25 written interrogatories, including all discrete subparts". 33(b)(2): 30 days. [CITED: law.cornell.edu/rules/frcp/rule_33]
- FRCP 34(b)(1)(A)-(C): reasonable particularity, time/place/manner, form of ESI. 34(b)(2)(A): 30 days. [CITED: law.cornell.edu/rules/frcp/rule_34]
- FRCP 36(a)(1)(B): genuineness of described documents. (a)(2): each matter separately stated, and a copy of the document attached unless already furnished. (a)(3): 30 days or deemed admitted. [CITED: law.cornell.edu/rules/frcp/rule_36]
  - Implication: genuineness RFAs should say "produced as DocID …" (already furnished), which satisfies (a)(2).
- FRCP 30(c)(2): objections concise, nonargumentative and nonsuggestive; instructions not to answer only to preserve privilege, enforce a court limit, or present a 30(d)(3) motion. 30(d)(1): 1 day of 7 hours. 30(e)(1): 30 days to review, with changes in a signed statement. 30(f)(1): officer certifies the witness was duly sworn and the record is accurate. [CITED: law.cornell.edu/rules/frcp/rule_30]
- Federal transcript format §520.30-520.36.55: text 1-3/4" from left to 3/8" from right; 10 letters/inch ≈ 63 chars; 25 lines/page excluding page numbers and notations; double spaced; Q/A at the left margin with the statement at the 5th space and continuation lines at the left margin; colloquy speaker at the 10th space, statement on the 3rd space after the colon; parenthetical "(" at the 5th space, remark at the 6th; page heading (witness last name + examination type) above line 1, on the same line as the page number. [CITED: Guide to Judiciary Policy Vol. 6 Ch. 5, wyd.uscourts.gov/sites/wyd/files/Transcript_Format.pdf]

**Deprecated/outdated:** nothing in-repo is deprecated for this phase. CLAUDE.md's ".NET 8 / OpenXml 3.0.2 / no tests" stack section is stale relative to the csproj.

## Assumptions Log

| # | Claim | Section | Risk if Wrong |
|---|-------|---------|---------------|
| A1 | The ~14 turns per testimony page heuristic and ~4.5k output tokens per 8-page chunk | Pattern 3 | Excerpts come out short or long; cost estimate off by ±50%. Mitigated by ±25% tolerance and retries; calibrate on the first live run |
| A2 | Case-number formats (federal `d:yy-cv-nnnnn`; state `yyyy-CV-nnnnnn`; memo matter numbers) | Pattern 2 | Cosmetic realism only; D-24 allows Claude's discretion |
| A3 | Estimator constants for discovery work (suggest DepositionChunkInput 7,000 / Output 4,500; WrittenSetInput 6,000; OutputPerRequest 150; QaOverhead 1.15) | Pattern 7 / estimate | Ready-card estimate inaccurate; never blocks function |
| A4 | Courier New 12 pt advance = 0.1" (10 cpi), so 62 chars fit in 6.375" | Pattern 4 | Lines wrap in Word, breaking page:line. Verify by opening a generated file at UAT; fall back to 60 chars if needed |
| A5 | Non-party witnesses (orgs that are not parties) are eligible, with no defending attorney | Pattern 2 | Mild realism issue. Alternative: exclude them from eligibility |
| A6 | Excerpt pages are numbered continuously from 1 and "pages-per-excerpt" counts testimony pages only | Pattern 4 | User may expect total pages; label the control "testimony pages" in UI copy |
| A7 | OOXML child-element orders listed in Pitfall 2 | Pitfall 2 | Caught by `OpenXmlValidator` tests in Wave 0/1 before UAT |
| A8 | RFA ordering "Key significance first" means Hot and Key before Supporting | Pattern 5 | Different subset of facts chosen; low impact |
| A9 | Memo-variant titles and InternalCorporate RFA availability | Pattern 5 | D-25 leaves titles to discretion; confirm in UI-SPEC |

## Open Questions

1. **Does "pages-per-excerpt" include front/back matter?**
   - What we know: D-15 sets defaults of 15/25/40 and the "EXCERPT" label.
   - What's unclear: whether 25 means testimony pages or total pages.
   - Recommendation: testimony pages. Label the control "Testimony pages per excerpt" and mention front matter in the tooltip.
2. **Where do discovery costs appear on Case Summary?**
   - What we know: D-06 says "fold into the Case Summary cost breakdown"; the cost row is journal-only today.
   - Recommendation: add a "discovery documents" cost item to `GenerationUiText.CostRow` (keeping the total = journal spend + discovery spend), and compute it even without a plan. The UI-SPEC should confirm the copy.
3. **What happens when a .docx is open in Word during regenerate?**
   - Recommendation: keep the new content in the sidecar, mark the row "Generated - close the file in Word, then click Write again" (or reuse Regenerate), and use the existing file-lock detection.
4. **Does the UI-SPEC come first?** `workflow.ui_phase` and `ui_safety_gate` are true. The planner should expect an `08-UI-SPEC.md` (gsd-ui-phase) for the new step, the Case Summary row and the dialog before UI tasks are planned.

## Environment Availability

| Dependency | Required By | Available | Version | Fallback |
|------------|------------|-----------|---------|----------|
| .NET SDK | Build/test (net10.0-windows) | ✓ | 10.0.401 (also 9.0.103) | — |
| xUnit test project | Validation | ✓ | builds; filtered run of CaseSchemaTests + CaseGuideDocxExporterTests passed 51/51 with `--artifacts-path obj/gsd-artifacts` [VERIFIED 2026-10-08] | — |
| Courier New font | Transcript rendering in Word | ✓ (ships with Windows) [ASSUMED] | — | Word substitutes a monospace font; layout still valid since pagination is explicit |
| Microsoft Word | Visual UAT only | not probed | — | Tests use `OpenXmlValidator`; the human UAT opens the files |
| Live Claude/OpenAI keys | Live generation UAT | user-provided | — | `FakeLlmProvider` for automated tests |

**Missing dependencies with no fallback:** none.

## Validation Architecture

### Test Framework
| Property | Value |
|----------|-------|
| Framework | xUnit 2.9.3 (`ReelDiscovery.Tests`, net10.0-windows, `InternalsVisibleTo` from app) |
| Config file | `ReelDiscovery.Tests/ReelDiscovery.Tests.csproj` (no runsettings) |
| Quick run command | `dotnet test ReelDiscovery.Tests/ReelDiscovery.Tests.csproj --artifacts-path obj/gsd-artifacts --filter "FullyQualifiedName~ReelDiscovery.Tests.Discovery"` |
| Full suite command | `dotnet test ReelDiscovery.Tests/ReelDiscovery.Tests.csproj --artifacts-path obj/gsd-artifacts` |
| Render-only | `... --filter "Category=Render&FullyQualifiedName~Discovery"` (PNGs under `RenderHarness.OutputFolder`) |

### Phase Requirements → Test Map
| Req ID | Behavior | Test Type | Automated Command | File Exists? |
|--------|----------|-----------|-------------------|-------------|
| ANC-01 | Eligible witnesses = key, non-counsel; ranking; preset defaults 1/2/3 alternating sides; cap 6 | unit | `--filter FullyQualifiedName~DepositionPlannerTests` | ❌ Wave 0 |
| ANC-01 | Roles from cast (examining = opposing partner, defending = own firm), fallbacks never throw | unit | same | ❌ Wave 0 |
| ANC-01 | Dates: business days, window 3-9 months after filing, ≥ latest fact, distinct, deterministic; case number deterministic per CaseId | unit | `~DepositionPlannerTests`, `~CaseNumberTests` | ❌ Wave 0 |
| ANC-01 | Layout: every page exactly 25 lines; no line > 62 chars; Q/A col 5, colloquy col 10, parenthetical col 5; page:line map for exhibits and index; padding of last page; front/back matter present (caption with EXCERPT, appearances, index, certificate, errata) | unit | `~TranscriptLayoutTests` | ❌ Wave 0 |
| ANC-01 | .docx: OpenXmlValidator clean; Courier New; exact 480 spacing on every line paragraph; `pageBreakBefore` count = pages - 1; line paragraphs per page = 25 | unit | `~TranscriptDocxBuilderTests` | ❌ Wave 0 |
| ANC-01 | Exhibits numbered consecutively across depositions; corpus vs no-corpus sources; marker inserted before the introducing turn; kept on regenerate | unit | `~DepositionPlannerTests`, `~CorpusDetectorTests` | ❌ Wave 0 |
| ANC-02 | RFP slots: every issue ≥ 1; count clamps 5-80; count < issues raised; anchors carry custodians/date range | unit | `~WrittenDiscoveryPlannerTests` | ❌ Wave 0 |
| ANC-03 | RFA fact order (Hot/Key, then date); code prefix carries the fact's long date; genuineness RFAs only with a usable corpus and only written DocIDs | unit | same + `~DiscoveryValidatorTests` | ❌ Wave 0 |
| ANC-04 | ROG ≤ 25 hard cap; every issue ≥ 1; subpart detector (D7) | unit | same | ❌ Wave 0 |
| ANC-02/03/04 | Memo variants: Criminal hides ROG and RFA; Regulatory hides RFA; titles per kind | unit | `~WrittenDiscoveryPlannerTests` | ❌ Wave 0 |
| ANC-02/03/04 | Written .docx: validator clean; caption with case number and court; title "…FIRST SET OF…"; definitions/instructions incl. 30-day and rule cites (federal) vs generic state; numbered requests; signature; certificate of service | unit | `~DiscoveryDocxBuilderTests` | ❌ Wave 0 |
| ANC-05 | Validator D2-D8: unknown person, amount variant, unregistered identifier, missing/unknown slot, RFA amount/actor mismatch, post-deposition date, subparts, substitution leak | unit | `~DiscoveryValidatorTests` | ❌ Wave 0 |
| ANC-05 | Generator with FakeLlmProvider: 2 retries with feedback text in the prompt; keep-best → "Generated with warnings"; one failed document doesn't stop others; fatal (Auth/SpendLimit) stops and keeps finished; user cancel keeps finished and persists them; cost includes failed attempts | unit | `~DiscoveryGeneratorTests` | ❌ Wave 0 |
| ANC-05 | Prompts: no raw complaint paragraphs, no "fictional", byte-identical system prompt across chunks and documents of one case | unit | `~DiscoveryPromptInvariantTests` | ❌ Wave 0 |
| ANC-05 | Staleness: guide edit / core edit / caption edit / plan change → out of date; unchanged → current | unit | `~DiscoveryStalenessTests` | ❌ Wave 0 |
| ANC-05 | Persistence: CaseDraft v4 round trip + Normalize clamps; sidecar round trip, size cap, newer version refused, CaseId mismatch ignored; file-name guard; Start Over leaves `Discovery\` and the sidecar | unit | `~DiscoveryStoreTests`, `CaseDraftSerializationTests`, `GenerationResetTests` | ❌ Wave 0 (+ update existing) |
| D-01 | New DTOs strict (≤16 union types) | unit | `~CaseSchemaTests` (add `DiscoveryGenerator.SchemaTypes`) | ✅ update |
| D-07 | Step order (9 steps), Next always enabled unless running, `NavigateToStep` | unit | `~WizardFlowTests` | ✅ update |
| D-03 | Step and dialog render at 144 DPI without clipping (empty / ready / running / results / memo-criminal), Case Summary with discovery row, "Step N of 9" shell titles | render | `--filter "Category=Render&(FullyQualifiedName~DiscoveryDocumentsRenderTests\|FullyQualifiedName~WizardShellRenderTests\|FullyQualifiedName~CaseSummaryRenderTests)"` | ❌ Wave 0 (+ update existing) |
| D-08/D-10 | UI copy: status line "N documents · last generated …", row statuses, estimate text | unit | `~DiscoveryUiTextTests` | ❌ Wave 0 |
| D-28 | Estimate uses the single pricing path; zero when no model; scales with pages and counts | unit | `~GenerationCostEstimatorTests` (add cases) | ✅ update |
| Manual | Open generated deposition .docx in Word: page count equals code count, line 25 sits on its page, Courier New looks real; live run with Claude and OpenAI | manual UAT | — | n/a |

### Sampling Rate
- **Per task commit:** the quick run filtered to the touched test classes (< 30 s).
- **Per wave merge:** `--filter "FullyQualifiedName~Discovery|FullyQualifiedName~WizardFlowTests|FullyQualifiedName~CaseSchemaTests|FullyQualifiedName~CaseDraftSerializationTests"`, plus the render filter when UI changed.
- **Phase gate:** full suite green before `/gsd-verify-work`, PNGs inspected at 144 DPI, plus manual Word and live-provider UAT.

### Wave 0 Gaps
- [ ] `ReelDiscovery.Tests/Discovery/DiscoveryTestData.cs`: drafts with a guide whose cast includes partner and associate per party firm (start from `CaseTestData.SampleCounselDraft` / `SampleGuidedDraft`), a memo-criminal and a memo-regulatory draft, plus a small corpus fixture (plan + journal) reusing `Evidence/EvidenceTestData` / `Corpus/PlanFixtures`.
- [ ] `DepositionPlannerTests.cs`, `CaseNumberTests.cs`, `TranscriptLayoutTests.cs`, `TranscriptDocxBuilderTests.cs`, `WrittenDiscoveryPlannerTests.cs`, `DiscoveryDocxBuilderTests.cs`, `DiscoveryValidatorTests.cs`, `DiscoveryGeneratorTests.cs`, `DiscoveryPromptInvariantTests.cs`, `DiscoveryStalenessTests.cs`, `DiscoveryStoreTests.cs`, `CorpusDetectorTests.cs`, `DiscoveryUiTextTests.cs`.
- [ ] `ReelDiscovery.Tests/Render/DiscoveryDocumentsRenderTests.cs` (+ the regenerate dialog in `DialogRenderTests`).
- [ ] Shared `OpenAndValidate` helper: copy from `CaseGuideDocxExporterTests` or promote to a test utility.
- [ ] Update existing: `WizardFlowTests` (9 steps), `WizardShellRenderTests` ("of 9"), `CaseDraftSerializationTests` (version 4), `CaseSchemaTests` (register), `CaseSummaryRenderTests`/`CaseSummaryLogicTests` (new row), `GenerationResetTests` (leaves Discovery), `GenerationCostEstimatorTests`.

## Security Domain

`security_enforcement` is enabled, ASVS level 1, `security_block_on: high`.

### Applicable ASVS Categories
| ASVS Category | Applies | Standard Control |
|---------------|---------|-----------------|
| V2 Authentication | no | Provider keys are handled by existing `ApiKeyStore`; nothing new |
| V3 Session Management | no | Desktop app, no sessions |
| V4 Access Control | no | Single local user |
| V5 Input Validation | yes | `DiscoverySettings.Normalize()` clamps counts and resolves ids against the cast and parties. The sidecar load has a size cap, version check, CaseId match, and never throws. LLM DTO text is length-capped and stripped of invalid XML chars. Slot and speaker ids from the LLM are validated against code-issued ids |
| V6 Cryptography | no (hashing only) | SHA-256 via `CaseDraft.ComputeHash` / `StableRank` for determinism and staleness, not security |
| V12 Files & Resources | yes | Paths are built only from the verified case folder + the constant `Discovery` + sanitized names (`CaseFolderNaming.Sanitize`). Names read back from the sidecar are re-validated (`IsStoryFileName` pattern, T-05-33) before `Process.Start(UseShellExecute)`. Open Folder uses `explorer.exe "<full path>"` like `OpenMailboxesFolder` (T-05-52) |
| V14 Configuration | n/a | No new config |

### Known Threat Patterns for this stack
| Pattern | STRIDE | Standard Mitigation |
|---------|--------|---------------------|
| Path traversal via a tampered `discovery-documents.json` file name ("..\..\x.docx") | Tampering / Elevation | Never trust stored names: recompute from the document kind and cast, or validate with a strict name check, and resolve under `caseFolder\Discovery` with a `StartsWith(fullRoot)` check |
| Shell-executing an arbitrary path from JSON | Elevation | Only open files whose resolved path is inside `Discovery\` and ends with `.docx` |
| Oversized or corrupt sidecar causing a crash or OOM | DoS | Size cap before read; catch `JsonException`/IO and show the reason (`CorpusPlanStore.Load` shape) |
| Malformed XML from model text (control chars, lone surrogates) | Tampering (document corruption) | `StripInvalidXmlChars` on every run (WordBuilder does this; the transcript builder must too) |
| Prompt injection via user-edited guide text (embedded instructions in cast or fact text) | Tampering | Structured data in labeled sections; the system prompt states that the data is data. The impact is limited to document text, and the validator still checks names and values |
| Real-entity defamation leak (Pitfall 12) | Information disclosure / legal | Validator D8 against `Substitutions` originals and aliases; prompts use only the effective (substituted) guide data |

## Sources

### Primary (HIGH confidence)
- Codebase (read 2026-10-08): `ReelDiscovery.csproj`, `ReelDiscovery.Tests.csproj`, `Models/Case/*.cs`, `Services/Case/CaseGuideDocxExporter.cs`, `CoreDocumentAssembler.cs`, `CaseStore.cs`, `CaseFolderNaming.cs`, `GuideStaleness.cs`, `CanonicalValueScanner.cs`, `CaseGuideGenerator.cs` (counsel rules), `Services/Evidence/RenderValidator.cs`, `KnowledgeGate.cs`, `LedgerSelector.cs`, `StorySoFarDocxBuilder.cs`, `ThreadUnitOfWork.cs`, `GenerationReset.cs`, `GenerationJournal.cs`, `Services/Corpus/PlanCallRunner.cs`, `GenerationCostEstimator.cs`, `CorpusPlanStore.cs`, `Helpers/Apportion.cs`, `Services/Qa/BlindReviewer.cs`, `BlindReviewPrompts.cs`, `Services/Ai/*.cs`, `Forms/WizardForm.cs`, `Forms/RegenerateTabDialog.cs`, `UserControls/StepCaseSummary.cs`, tests `WizardFlowTests`, `CaseSchemaTests`, `CaseGuideDocxExporterTests`, `RenderHarness`
- FRCP Rules 30, 33, 34, 36: https://www.law.cornell.edu/rules/frcp/rule_30, /rule_33, /rule_34, /rule_36
- Guide to Judiciary Policy Vol. 6 Ch. 5 §520 (Transcript Format): https://www.wyd.uscourts.gov/sites/wyd/files/Transcript_Format.pdf (text extracted and read)

### Secondary (MEDIUM confidence)
- `.planning/research/FEATURES.md` §C, §D (deposition and written-discovery structure, 25-line convention, CA 16 CCR 2473 / NCRA references)
- `.planning/research/PITFALLS.md` Pitfall 11 (P-Ancillary)
- Web search summary of federal transcript format (consistent with the extracted PDF): https://www.mtd.uscourts.gov/sites/mtd/files/Transcript%20Format.pdf

### Tertiary (LOW confidence)
- Token-per-page and turns-per-page heuristics (A1, A3): reasoning only; calibrate live.

## Metadata

**Confidence breakdown:**
- Standard stack and integration points: HIGH. Every component was read in code, and the test build was verified.
- Architecture (planners, layout engine, validator, persistence): HIGH for fit with existing patterns; MEDIUM for chunk sizing.
- Legal format conventions: HIGH for the FRCP and federal transcript rules (primary sources); MEDIUM for state and memo variants (generic by design per D-20/D-25).
- Pitfalls: HIGH (most derived from verified code behavior: cost row early return, Revision semantics, private validator rules, step-count tests).

**Research date:** 2026-10-08
**Valid until:** 2026-11-07 (stable domain; re-check only if Phase 7 follow-ups change `StepCaseSummary` cost loading or `PlanCallRunner`)
