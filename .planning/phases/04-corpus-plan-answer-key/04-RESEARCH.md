# Phase 4: Corpus Plan & Answer Key - Research

**Researched:** 2026-10-02
**Domain:** Deterministic corpus planning (allocation, scheduling, thread shapes), chunked strict-schema LLM planning, OpenXml/CSV answer-key output, WinForms review screen
**Confidence:** HIGH for codebase integration, .NET/OpenXml mechanics and CSV safety (verified in this session); MEDIUM for realism parameters (published corpus statistics, adapted); LOW-MEDIUM for cost and time estimates (heuristic)

<user_constraints>
## User Constraints (from CONTEXT.md)

### Locked Decisions

#### Carried forward (locked in Phases 1-3)
- **D-01:** All LLM work goes through `ILlmGateway` on the **Story** tier with strict JSON-schema DTOs, and works with Claude or OpenAI. The strict-schema limits from Phase 2 research apply (≤16 nullable fields, no recursion, dates as `yyyy-MM-dd` strings, provider max-output caps → chunk the work).
- **D-02:** Names are kept exactly as the guide has them, **including names from fictional works**. Prompts must never tell the model that names must be fictional. Content takes a firm position and does not hedge.
- **D-03:** UI uses the Phase 2/3 theme (`Theme`, `ThemedButton` via `ButtonHelper`, `CardPanel`, `SlimProgressBar`, segmented controls) with content-sized rows and logical-px sizing. **Every new screen and dialog must be rendered at 144 DPI (150% scaling) with the existing render-test harness and checked for clipping before hand-off.** Visual quality is a hard requirement.
- **D-04:** Autosave: corpus settings, the plan and its approval state persist with the case (D-10 of Phase 3). Reopening a case resumes at the furthest corpus step reached. Add `CaseStage` values for the new steps; they map to step types, never indexes.
- **D-05:** The guide is the single source of truth. The plan stores the **`CaseGuide.ComputeHash()`** it was built from (stale detection, see D-25).

#### Corpus settings (new step "Corpus Settings", after Case Guide)
- **D-06:** **Size:** quick-pick presets **250 / 1,000 / 2,500 / 5,000** plus a custom number box (**50-5,000**). The default follows the complexity preset (Simple 250, Standard 1,000, Complex 2,500).
- **D-07:** **Relevant ratio:** a **% relevant slider** (default ~20%) with **live counts** ("≈200 relevant / 800 non-relevant emails"). It warns when the relevant count is too low for every key fact to get at least one thread.
- **D-08:** **Non-relevant mix:** a sensible default split of about **50% custodians' daily business / 20% ancillary-staff traffic / 20% newsletters-spam-personal / 10% near-miss decoys**, editable under an **"Advanced" expander**. The plan reserves slots per category.
- **D-09:** **Hot docs, privileged threads and attachment rate are automatic:** hot documents follow the facts with `Significance = Hot`, privileged threads follow the counsel cast (in-house + outside counsel), and the attachment rate gets a realistic default. Their counts show on the plan summary. Overrides live in the same Advanced expander.
- **D-10:** **Date window:** defaults to the first through last key fact, plus **~1 month of lead-in and ~2 months of tail** for non-relevant traffic. Start and end are editable, but clamped so every dated key fact stays inside.
- **D-11:** **Media type toggles:** checkboxes for Office documents / calendar invites / images / voicemail. Images and voicemail are disabled with a notice when no OpenAI key is configured (Phase 1 rule). The plan assigns attachment specs only for enabled types.
- **D-12:** Wizard order becomes **Case Guide → Corpus Settings → Review Plan → Case Summary**. Next on Corpus Settings runs planning.

#### How planning works
- **D-13:** **Relevant threads use a code skeleton plus LLM thread plans.** Code allocates thread and message counts per fact/issue, picks participants (from `ActorIds` / `KnownByIds`, only cast active on that date), fixes dates/times and reply/reply-all/forward structure, and assigns labels and flags. The LLM, called in **chunks by time window across all issues** (so overlapping issues among shared characters are visible together), writes each thread's subject, per-message intent, what each message must convey, links between threads, and the one-sentence relevance rationale. **Code owns every date, person, count and label.** LLM output that names unknown people or ids is mapped or dropped by code.
- **D-14:** **Non-relevant slots:** code decides category, custodian, participants (key and background cast, plus external senders for newsletters/spam), dates, thread shape and attachment specs, with no case facts in context. A **bulk LLM pass in Phase 4 seeds subjects (and decoy angles)** so the plan summary and answer key show real subjects for every thread. Decoy seeds share project names and keywords with the case but must not be about the alleged conduct: give the decoy prompt the relevant facts as a must-not-mention list.
- **D-15:** **Volume is weighted by significance.** Every key fact gets at least one relevant thread. Hot and Key facts get more threads and messages, Supporting facts fewer. Periods with many facts close together get denser traffic. Some threads deliberately touch two concurrent issues.
- **D-16:** **Timestamps use simple business hours plus latency and holidays:** weekdays only, 9am-5pm in the **sender's organization time zone** (`CaseOrganization.TimeZoneId`), no evening or weekend mail, **US federal holidays skipped**, and realistic **reply latency** (minutes to a day or two, monotonic within a thread). This satisfies PLAN-04 under the user's preference for simplicity.
- **D-17:** Thread depth, reply vs. reply-all vs. forward, CC patterns and attachment rate follow realistic distributions (PLAN-05). Long sagas split into several threads rather than one huge thread. Exact distribution parameters are Claude's discretion.
- **D-18:** **Progress:** inline stage label (Allocating → Planning relevant threads (chunk n/N) → Planning other email → Validating), progress bar and **Cancel**, the same pattern as Case Guide generation, with refusal/fatal/timeout handling. **A cancelled or failed plan is discarded** (no partial-chunk resume). Settings are kept.
- **D-19:** **Repair:** the planner auto-repairs what code can fix (moves dates into active ranges or business hours, swaps an inactive participant), re-asks the LLM **once** for an offending chunk, and reports anything still wrong in the validator Checks.
- **D-20:** **No reproducibility seed** is needed. Use `Random.Shared` (or a local Random) and don't store a seed.
- **D-21:** **Cost:** planning calls show in the existing live cost ticker. The Review Plan screen also shows a **rough estimate of the cost of generating the corpus** on the selected Story model (from planned message counts × typical tokens × model pricing in `AIModelConfig`).

#### Review Plan screen (new step "Review Plan")
- **D-22:** **Summary only, with no per-thread drill-in.** Stat tiles (emails, threads, relevant %, hot, privileged, attachments, estimated generation cost), a **month-by-month date histogram** stacked relevant vs. non-relevant, **three compact sortable tables (by issue / by key fact / by custodian, with relevant and non-relevant counts)**, and the **Checks** list from the validator. Same card visual language as Case Guide.
- **D-23:** **The plan is read-only.** Changes come from **Re-plan**, which opens a small dialog with an optional guidance box (e.g. "more traffic around the March change-order dispute"), confirms, and replaces the whole plan. Going Back to Corpus Settings changes the numbers.
- **D-24:** **Validator results are warnings only. Nothing blocks Approve.** Checks still carry clear fix hints (Phase 3 style), but no error tier blocks approval.
- **D-25:** **Stale plan:** if the guide hash no longer matches the plan's basis, Review Plan shows an **"out of date with the case guide" banner with a Re-plan action** and approval is withdrawn until the user re-plans or dismisses it. Nothing re-plans silently (mirrors Phase 3 D-20).
- **D-26:** **Approve** locks the plan in the case and **immediately writes `answer-key.csv`, `answer-key.xlsx` and the review protocol `.docx` into the case folder**. Case Summary lists them and can open them. Re-planning after approval overwrites them after a confirm.

#### Answer key
- **D-27:** **Relevance is binary (Relevant / Not Relevant) plus flags:** separate columns for Hot (Y/N), Privileged (Y/N), Near-miss decoy (Y/N), and Non-relevant category (daily business / ancillary / newsletter-spam-personal / decoy).
- **D-28:** **One row per unique document** (each email message and each attachment). Custodian = the sender (owner of the sent copy), and an **"All custodians"** column lists every mailbox that will hold a copy. Row counts match the plan, not the mailbox duplication.
- **D-29:** **Planned paths plus a Status column.** DocIDs and deterministic planned relative file paths are fixed now. `Status = Planned` in Phase 4. Later phases regenerate the key from plan + run journal to mark Generated / Failed / Skipped.
- **D-30:** **DocID = case prefix + number**, e.g. `DM-000001`. Attachments are `DM-000001.0001` (parent id visible = family). The prefix comes from the case title. DocIDs are stable for a given plan, and re-plan issues new ones. Phase 5 stamps the DocID into the `X-ReelDiscovery-DocID` header.
- **D-31:** **Rationale comes from the plan.** For relevant threads, the LLM writes it in the thread plan, tied to the fact/issue. Non-relevant rows use a code template by category. Attachments and quoted replies **inherit family relevance** (KEY-03), and their rationale notes the inheritance.
- **D-32:** Required columns (KEY-02): DocID, file path, custodian, all custodians, date, from, to, cc, subject, thread ID, family/parent ID, relevance, issue codes, linked fact IDs, hot, privileged, near-miss, non-relevant category, rationale, status.
- **D-33:** **Excel workbook = three sheets:** "Answer Key" (frozen header, filters, sized columns), "Statistics" (counts by custodian, issue, relevance and month: KEY-04), and "Legend" (issue codes with titles, fact IDs with titles, column definitions).

#### Review protocol
- **D-34:** **A realistic reviewer manual (.docx)** in the style of a real document-review protocol: matter background, parties and key people, issue codes with relevance criteria (from `CaseIssue.RelevanceCriteria`) and illustrative examples, hot-document and privilege guidance (counsel names and law firms), coding fields and decision rules, and family rules. Code builds the structure from the guide, and **one Story-tier LLM pass writes the prose sections**. It must **not** reveal the answer key or per-document labels.

### Claude's Discretion
- Plan data model and storage (inside `case.json` vs a sibling plan file in the case folder for size at 5K), as long as it autosaves and reopens with the case.
- Chunk size, the number of neighbor facts per planning chunk, and DTO decomposition within the strict-schema limits.
- Exact distribution parameters (thread depth, CC rates, reply/forward mix, latency curve, default attachment rate), histogram rendering approach, and stat-tile set details.
- Exact validator rule set beyond PLAN-06's minimum (counts, ratio, active ranges, every key fact evidenced). Also: dates within window and business hours, monotonic thread dates, entity refs resolve, labels consistent with fact links, attachment specs well-formed.
- DocID prefix derivation (e.g. initials of the first party or case title), file-path naming scheme, and the generation-cost estimate heuristic.
- The US federal holiday table (computed rules, including observed dates).

### Deferred Ideas (OUT OF SCOPE)
- Per-thread drill-in, editing, or "re-plan this thread" on Review Plan: the user chose summary-only + whole-plan re-plan for v1.
- Evening/weekend mail, executive or crunch-period patterns: the user chose simple business hours. Could be revisited as a realism option later.
- Resuming a cancelled plan from completed chunks: the plan is discarded on cancel for now.
</user_constraints>

<phase_requirements>
## Phase Requirements

| ID | Description | Research Support |
|----|-------------|------------------|
| PLAN-01 | User sets corpus size (up to ~5,000) and relevant vs. non-relevant proportion | `CorpusSettings` model + `StepCorpusSettings` (presets 250/1,000/2,500/5,000 + custom 50-5,000, % slider with live counts, low-relevant warning threshold = dated fact count). See Pattern 1, Pitfall 9 |
| PLAN-02 | Every thread and message planned in code before prose: date/time, sender, To/CC, reply/forward structure, linked facts/issues, label, attachments | `CorpusPlan` → `PlannedThread` → `PlannedMessage` → `PlannedAttachment` data model (Pattern 2), `CorpusPlanner` pipeline (Pattern 3). Every structural field is code-owned; the LLM fills text only |
| PLAN-03 | Relevant threads scheduled against the key-facts timeline; overlapping issues run concurrently among shared characters | `BudgetAllocator` (significance-weighted largest remainder, min 1 thread per fact), anchor dates around fact dates (density follows fact clustering), multi-issue threads from concurrent facts sharing actors, LLM chunks by time window across all issues (Pattern 4) |
| PLAN-04 | Realistic timestamps (business hours, time zones, weekends/holidays, latency) | `BusinessCalendar` with IANA `TimeZoneInfo` (verified on this machine), computed US federal holidays with observed rules (OPM), latency mixture calibrated to Avocado/Yahoo studies (Pattern 5) |
| PLAN-05 | Realistic thread depth, reply/reply-all/forward, CC, attachment rate | `ThreadShapes` parameter table calibrated to Enron thread-size distribution (Klimt & Yang) and Avocado recipient statistics (Yang et al.) (Pattern 6) |
| PLAN-06 | Validator: counts, ratio, active ranges, every key fact evidenced | `CorpusPlanValidator` (static, never throws, warnings only, fix hints) with ~18 rules (Pattern 7) |
| PLAN-07 | Plan summary (counts by issue/fact/relevance/custodian, histogram), approve or re-plan | `PlanSummary` pure projection + `StepReviewPlan` (tiles, custom-painted stacked month histogram, three sortable grids, Checks), `ReplanDialog`, stale banner (Pattern 8) |
| KEY-01 | Answer key CSV + Excel, one row per document | `AnswerKeyBuilder` (pure rows from plan) + `AnswerKeyCsvWriter` (RFC 4180, UTF-8 BOM, formula-injection guard) + `AnswerKeyXlsxWriter` (OpenXml multi-sheet, prototype validated with 0 errors) |
| KEY-02 | Required columns | Column table in Pattern 9 (D-32 order plus Doc Type and File Name extras) |
| KEY-03 | Labels from the plan; family relevance to attachments and quoted replies | Relevance basis per message (Own / Quoted) and per attachment (Family); relevant threads never mix with non-relevant ones, so propagation is total by construction (Pattern 9) |
| KEY-04 | Statistics sheet | "Statistics" sheet: by custodian, issue, relevance/category, month; numeric cells (Pattern 9) |
| KEY-05 | Review protocol / coding manual | `ReviewProtocolBuilder` (code structure from guide) + one Story call for prose (`ReviewProtocolProseDto`), Word output via the existing `CaseGuideDocxExporter.WordBuilder` made `internal` (Pattern 10) |
| PLAT-03 | Unit tests for planner, validator, answer-key writer | New `ReelDiscovery.Tests/Corpus/` folder; test map in Validation Architecture |
</phase_requirements>

## Project Constraints (from CLAUDE.md)

CLAUDE.md's stack section is partly stale; verified state of the repo: **net10.0-windows** (`ReelDiscovery.csproj`), packages `Anthropic 12.51.0`, `DocumentFormat.OpenXml 3.5.1`, `Microsoft.Extensions.AI.Abstractions 10.10.1`, `MimeKit 4.18.1`, `OpenAI 2.14.0`; xUnit test project `ReelDiscovery.Tests` (xunit 2.9.3) with `InternalsVisibleTo`. [VERIFIED: ReelDiscovery.csproj, ReelDiscovery.Tests.csproj]

Directives that still apply and that plans must honor:
- C# conventions: mutable classes with auto-properties, **no `record` types, no primary constructors, no `#region`**; `_camelCase` private fields; `ct` is the last parameter; `Async` suffix; target-typed `new()`; `[GeneratedRegex]` instead of `new Regex`; `using var`.
- Namespace mirrors folder; implicit usings (don't add `System`, `System.IO`, `System.Linq`, etc.).
- Nullable on; builds warning-free by hand.
- Error handling: catch `OperationCanceledException` before `Exception`; no bare `catch` in core generation logic; every swallowed exception gets a comment; generators return result objects; accumulate (don't abort) in bulk work.
- UI built in code (`InitializeUI()`, `TableLayoutPanel` root); WinForms control fields use Hungarian prefixes (`_btnApprove`, `_gridByIssue`); wire handlers after all controls exist (STATE: init-order null refs bit twice).
- Use `Random.Shared` in new code (shared `System.Random` across tasks is a known bug class).
- Strict-schema DTOs are nested `internal class` with `[JsonPropertyName]` + `[Description]`; dates are `yyyy-MM-dd` strings parsed with `CaseDraft.TryParseDate` (InvariantCulture).
- GSD workflow enforcement: edits happen through `/gsd-execute-phase`.
- User memory: never substitute names from fictional works; verify WinForms UI at 150% DPI.

## Summary

Phase 4 is mostly **deterministic C#** wrapped around two kinds of small, bounded LLM calls. The codebase already has every building block: `ILlmGateway` with strict schemas and outcome classification, the `CaseGuideGenerator.RunUnitAsync`/`UnitRun` pattern for parallel, cancellable, fatal-aware calls, `CaseGuideValidator`'s never-throwing findings model, `CaseStore.WriteAtomicAsync` for OneDrive-safe writes, `UsTimeZones` + `CaseOrganization.TimeZoneId`, the `CaseGuideDocxExporter.WordBuilder`, and the 144-DPI render harness. No new NuGet packages are needed. I verified on this machine that `TimeZoneInfo.FindSystemTimeZoneById("America/Chicago")` resolves IANA IDs under .NET 10 on Windows, with correct DST offsets. I also verified that an OpenXml 3.5.1 three-sheet workbook with frozen header panes, column widths, autofilters and `_xlnm._FilterDatabase` defined names passes `OpenXmlValidator` with 0 errors.

The core design is: (1) **allocate** in pure code (significance-weighted largest remainder; every fact gets ≥1 thread); (2) build a **skeleton** for every thread (participants from `ActorIds ∪ KnownByIds` who are active on the date, Seq/ReplyToSeq/Kind, To/CC by kind, `DateTimeOffset` timestamps from a business calendar in the sender's IANA zone); (3) ask the LLM in **time-window chunks of ~20 threads** to write subject, purpose, per-message intent and must-convey, and the rationale, keyed by code-issued thread IDs; (4) seed non-relevant subjects in batches of ~100 slots, with decoys given a must-not-mention list; (5) **validate** (warnings only); (6) project the plan into the answer key and protocol. The plan is immutable once built. Store it in a sibling **`corpus-plan.json`** (about 3-6 MB at 5K messages), and keep only settings, approval state and a reference in `case.json`. That keeps autosave cheap.

Several traps are specific to this codebase. `CaseGuide.ComputeHash()` serializes UI state (`DismissedStaleKey`, part `GeneratedUtc`, `NeedsReview`), so the plan would go stale on trivial actions. Using `IdAllocator.Next(guide, ...)` for plan IDs would mutate `guide.IdCounters` and make the plan stale immediately. DocIDs assigned relevant-first would leak the label through ID order. Finally, the CSV is a formula-injection vector because LLM text lands in cells.

**Primary recommendation:** Build a pure, injectable-`Random` `CorpusPlanner` pipeline (Allocator → Skeleton → Scheduler → LLM fill → Repair → Validator) that returns a new immutable `CorpusPlan` assigned to the draft on the UI thread only on success. Persist it to `corpus-plan.json`, and write the answer key and protocol with pure builders tested against `OpenXmlValidator` and RFC 4180/OWASP rules.

## Architectural Responsibility Map

This is a single-process desktop app; "tiers" map to app layers.

| Capability | Primary Tier | Secondary Tier | Rationale |
|------------|-------------|----------------|-----------|
| Corpus settings entry, live counts, warnings | UI (`StepCorpusSettings`) | Pure text helper (`CorpusUiText`) | Copy/thresholds are unit-tested pure functions, UI is layout only (Phase 3 pattern 03-06) |
| Budget allocation, skeleton, scheduling, thread shapes | Services (pure static: `BudgetAllocator`, `BusinessCalendar`, `ThreadShapes`, `ThreadSkeletonBuilder`) | — | Code owns every date, person, count and label (D-13); pure = testable |
| Thread content (subject, intent, must-convey, rationale) | Services (`CorpusPlanner` → `ILlmGateway` Story) | Prompts (`CorpusPlanPrompts`) | LLM writes text only, keyed by code IDs |
| Non-relevant subject seeds / decoy angles | Services (`CorpusPlanner` → `ILlmGateway` Story) | — | Bulk batched calls with no case facts (decoys get must-not-mention) |
| Plan validation | Services (`CorpusPlanValidator`, static, never throws) | — | Mirrors `CaseGuideValidator` |
| Plan persistence | Storage (`CaseStore` + new `CorpusPlanStore` for `corpus-plan.json`) | Models `Normalize()` | Large immutable file separate from frequently autosaved `case.json` |
| Plan summary, histogram data, cost estimate | Services (pure `PlanSummary`, `GenerationCostEstimator`) | UI renders | Pure projections, unit-tested |
| Answer key rows, CSV, XLSX | Services (pure `AnswerKeyBuilder`, `AnswerKeyCsvWriter`, `AnswerKeyXlsxWriter`) | UI writes bytes via `CaseStore.WriteAtomicAsync` | Same split as `CaseGuideDocxExporter` (pure bytes, UI writes) |
| Review protocol | Services (`ReviewProtocolBuilder` + one Story call) | `WordBuilder` | Code structure + LLM prose (D-34) |
| Navigation, stage mapping | UI shell (`WizardForm.StepTypesFor`, `StepTypeForStage`) | `CaseStage` enum | Map by type, never by index (D-04) |

## Standard Stack

### Core (all already referenced; no installs)
| Library | Version | Purpose | Why Standard |
|---------|---------|---------|--------------|
| .NET / WinForms | net10.0-windows (SDK 10.0.401 installed) | Runtime/UI | Existing app [VERIFIED: csproj, `dotnet --list-sdks`] |
| `System.TimeZoneInfo` (ICU-backed IANA lookup) | in-box | Sender-local business hours, DST | `FindSystemTimeZoneById("America/Chicago")` works on this Windows 11 machine under .NET 10; Phoenix/Honolulu have no DST; unknown id throws `TimeZoneNotFoundException` [VERIFIED: scratch run] |
| `DocumentFormat.OpenXml` | 3.5.1 | XLSX answer key, DOCX protocol | Already used by `OfficeDocumentService` and `CaseGuideDocxExporter`; multi-sheet + frozen pane + autofilter prototype validated with 0 errors [VERIFIED: scratch run with OpenXmlValidator] |
| `System.Text.Json` | in-box | `corpus-plan.json`, DTOs | `CaseDraft.JsonOptions` (string enums) shared [VERIFIED: CaseDraft.cs] |
| `ILlmGateway` / `LlmGateway` | in-repo | Strict-schema Story-tier calls, usage tracking | Locked by D-01 [VERIFIED: Services/Ai] |
| `Microsoft.Extensions.AI.Abstractions` | 10.10.1 | Schema generation via `JsonSchemaRegistry` | Existing; emits strict schemas [VERIFIED] |

### Supporting (in-repo, reuse)
| Asset | Purpose | When to Use |
|-------|---------|-------------|
| `CaseGuideGenerator.RunUnitAsync` / `UnitRun` (private today) | Semaphore-bounded calls, fatal stop via linked CTS, timeout vs. user-cancel classification | Copy the shape into `CorpusPlanner` (or extract to an internal shared helper) |
| `CaseGuideGenerator.Distribute` (private static, largest remainder) | Integer apportionment | Extract to `Helpers/Apportion.cs` with a per-item minimum; reuse for facts, categories, custodians, attachment types |
| `CaseStore.WriteAtomicAsync` | Temp file + `File.Move(overwrite)` with retries | `corpus-plan.json`, `answer-key.*`, protocol .docx |
| `CaseGuideDocxExporter.WordBuilder`, `ParaFormat`, `Cell`, XML-char sanitizer | Validator-clean docx | Change from `private` to `internal` (or move to `Services/Case/WordBuilder.cs`) for the protocol |
| `UsTimeZones.ForLocation` / `Default` | Fallback zone when `TimeZoneId` is blank or invalid | Scheduler |
| `ThreadingHelper.AddReplyPrefix` / `AddForwardPrefix` | `RE:` / `FW:` subjects for answer-key rows | AnswerKeyBuilder |
| `Helpers/FileNameHelper` (source-generated regex) | Sanitize LLM-proposed attachment file names | Skeleton/attachment specs |
| `CaseFolderNaming` sanitization rules (reserved names, invalid chars) | Custodian folder names in planned paths | Path builder |
| `GridStyle`, `CardPanel`, `SlimProgressBar`, `ThemedButton`, `Theme`, `TextFieldHost` | Review Plan / Corpus Settings UI | All new UI |
| `FakeLlmProvider`, `CaseTestData.SampleGuidedDraft()` / `SampleCounselDraft()`, `StrictSchemaValidator`, `RenderHarness` | Tests | All new tests |

### Alternatives Considered
| Instead of | Could Use | Tradeoff |
|------------|-----------|----------|
| Custom-painted histogram control | `System.Windows.Forms.DataVisualization` charting | Not part of WinForms on .NET Core/.NET 5+; only unofficial ports exist [ASSUMED]. Adds a package and DPI risk for a 12-24 bar chart. Paint it yourself |
| Sibling `corpus-plan.json` | Plan inside `case.json` | Simpler load. But the whole draft is serialized on the UI thread on every autosave (`CaseStore.TakeSnapshot`). 3-6 MB per keystroke-triggered save during later guide edits is wasteful, and the plan is immutable anyway (ARCHITECTURE Anti-Pattern 6) |
| Inline strings in XLSX | Shared string table | Inline strings are simpler and validated. Excel converts them on save. Size is trivial at ~7K rows |
| ClosedXML / EPPlus | — | New dependency (EPPlus is commercially licensed). OpenXml is already in the project and enough |

**Installation:** none.

## Architecture Patterns

### System Architecture Diagram

```
 CaseDraft (guide + CorpusSettings) ──► [StepCorpusSettings] ──Next──► [StepReviewPlan: auto-plan when no current plan]
                                                                              │
                                                     CorpusPlanner.PlanAsync(draft snapshot, settings, guidance, ct)
                                                                              │
   ┌───────────────────────────── pure code ─────────────────────────────────┤
   │ 1 Allocate   R = round(N·ratio); facts → thread counts (Hot>Key>Supporting, ≥1 each)
   │              NR → categories (mix) → custodians (weighted) ; message budgets per thread
   │ 2 Skeleton   per thread: anchor date, participants (ActorIds∪KnownByIds ∩ active), Seq/ReplyToSeq/Kind,
   │              To/CC by kind, privileged/hot flags, attachment specs (enabled media only)
   │ 3 Schedule   BusinessCalendar: sender TZ, weekdays, 09-17, US holidays, latency → DateTimeOffset (monotonic)
   └──────────────────────────────────────────────────────────────────────────┘
                      │                                               │
   4a Relevant fill (LLM, Story)                     4b Non-relevant seeds (LLM, Story)
      chunks of ~20 threads by time window,             batches of ~100 slots, no case facts;
      all issues, ±neighbor facts → ThreadPlanBatchDto   decoys: project names + must-not-mention → SubjectSeedBatchDto
      map by threadId/seq; drop unknown ids             scan decoys for forbidden terms
      missing → re-ask chunk once → fallback template    missing → re-ask once → template
                      │                                               │
                      └──────────────► 5 Repair (code) ◄──────────────┘
                                         │
                       6 Assign DocIDs chronologically, planned paths, custodians
                                         │
                       7 CorpusPlanValidator → findings (warnings) ; ReviewProtocol prose (1 Story call, fallback text)
                                         │
             success ──► draft.Corpus = new plan (UI thread) ─► write corpus-plan.json, then case.json
             cancel/fail ─► discard (settings kept)
                                         │
                [Review Plan UI] tiles · month histogram · by-issue/fact/custodian grids · Checks · stale banner
                         │ Approve                                   │ Re-plan (guidance)
                         ▼                                           └──► back to PlanAsync (confirm overwrite if approved)
      AnswerKeyBuilder(plan, guide) → rows ─► CSV writer ─► answer-key.csv
                                         └─► XLSX writer ─► answer-key.xlsx (Answer Key | Statistics | Legend)
      ReviewProtocolBuilder(guide, plan.Protocol) ─► "{folder} - Review Protocol.docx"
                         ▼
                 [Case Summary] lists/opens the three files
```

### Recommended Project Structure
```
Models/Corpus/
├── CorpusSettings.cs        # size, ratio, mix, window, media toggles, overrides, ComputeHash(), Normalize()
├── CorpusState.cs           # lives in CaseDraft.Corpus: Settings, plan reference (PlanId, file, hashes), Approved, DismissedStaleHash, WrittenFiles
├── CorpusPlan.cs            # immutable-by-convention plan root (FormatVersion, PlanId, GuideHash, SettingsHash, DocIdPrefix, Threads, Externals, Protocol, Notes)
├── PlannedThread.cs         # + enums ThreadRelevance, NonRelevantCategory
├── PlannedMessage.cs        # + enums MessageKind, RelevanceBasis
├── PlannedAttachment.cs     # reuses Models.AttachmentType
└── ExternalParty.cs         # newsletter/vendor/personal senders (EXT-001)
Services/Corpus/
├── CorpusPlanner.cs         # orchestration, LLM chunks, re-ask, repair; nested strict DTOs; SchemaTypes
├── CorpusPlanPrompts.cs     # byte-stable system prompt + user-prompt builders
├── BudgetAllocator.cs       # pure
├── ThreadSkeletonBuilder.cs # pure (participants, kinds, recipients, flags, attachments)
├── ThreadShapes.cs          # pure parameter table + samplers (inject Random)
├── BusinessCalendar.cs      # pure (holidays, business-time arithmetic, TZ resolution)
├── CorpusPlanValidator.cs   # static, never throws; PlanFinding model
├── PlanSummary.cs           # pure projections for the Review Plan screen
├── GenerationCostEstimator.cs
├── DocIdPrefix.cs           # prefix derivation from the case
├── CorpusPlanStore.cs       # load/save corpus-plan.json (size cap, Normalize, PlanId match)
├── AnswerKeyBuilder.cs      # plan → rows (pure)
├── AnswerKeyCsvWriter.cs
├── AnswerKeyXlsxWriter.cs
└── ReviewProtocolBuilder.cs # structure + prose DTO; uses WordBuilder
UserControls/
├── StepCorpusSettings.cs
├── StepReviewPlan.cs (+ StepReviewPlan.Run.cs)
├── MonthHistogram.cs        # custom-painted control
└── CorpusUiText.cs          # all copy with counts (pure, tested)
Forms/ReplanDialog.cs        # ThemedDialog subclass
ReelDiscovery.Tests/Corpus/  # namespace ReelDiscovery.Tests.Corpus
```
(Putting these under `Services/Case`/`Models/Case` is also acceptable. A separate `Corpus` namespace keeps the 30K-line Case folder navigable and gives a clean test filter.)

### Pattern 1: Settings model with live counts and clamps (PLAN-01)
- `TotalEmails` int (default from `draft.Complexity`: Simple 250, Standard 1,000, Complex 2,500), clamped 50..5,000.
- `RelevantPercent` int, default 20. Recommended slider range 5..60 [ASSUMED range; demo richness 10-30% per FEATURES.md].
- Live counts: `relevant = (int)Math.Round(TotalEmails * RelevantPercent / 100.0, MidpointRounding.AwayFromZero)`, `nonRelevant = TotalEmails - relevant`. The same function must be used by the allocator so the screen and the plan agree.
- Low-relevant warning (D-07): `relevant < datedFactCount`. Every fact needs at least one message, so this is the hard floor. Add a softer hint when `relevant < 2 × datedFactCount` (threads of length 1 only).
- Mix: four ints summing to 100; Normalize renormalizes with largest remainder if the sum is off (untrusted JSON).
- Window: default `firstFact − 1 month` .. `lastFact + 2 months`. On edit, clamp so `start ≤ firstFact` and `end ≥ lastFact`. Store as `yyyy-MM-dd`.
- Media toggles: persist the user's choice, and compute the effective value as `choice && (type is Office/Calendar || state.IsMediaAvailable)`. Re-evaluate on every enter (keys can disappear between sessions). Mirrors `WizardState.ApplyMediaAvailability`.
- Overrides (Advanced): `AttachmentPercent` (default 20 [CITED: FEATURES.md F, ASSUMED]), `PrivilegedPercentOfRelevant` (default auto), `HotPerHotFact` (default auto). Use `int?` in the persisted model (not a DTO, so the nullable limit doesn't apply); null = automatic.
- `ComputeHash()` over all fields → `CorpusPlan.SettingsHash`, so returning to Review Plan with changed settings is detected.

### Pattern 2: Plan data model (PLAN-02) — mutable classes, code-owned
```csharp
// Models/Corpus/PlannedMessage.cs (sketch; conventions: mutable, no records)
public enum MessageKind { New, Reply, ReplyAll, Forward }
public enum RelevanceBasis { None, Own, Quoted }   // Own = carries fact evidence; Quoted = inherits via quoted/forwarded text

public class PlannedMessage
{
    public string Id { get; set; } = string.Empty;        // "T-0042-M03" (thread-relative, opaque)
    public string DocId { get; set; } = string.Empty;     // "DM-000317", assigned chronologically after scheduling
    public int Seq { get; set; }                           // 1-based; Seq order == time order
    public int ReplyToSeq { get; set; }                    // 0 = thread root
    public MessageKind Kind { get; set; }
    public string FromId { get; set; } = string.Empty;    // CH-xxx or EXT-xxx
    public List<string> ToIds { get; set; } = new();
    public List<string> CcIds { get; set; } = new();
    public DateTimeOffset SentAt { get; set; }             // sender-local offset; System.Text.Json writes ISO 8601 with offset
    public string TimeZoneId { get; set; } = string.Empty; // IANA id used for SentAt
    public List<string> FactIds { get; set; } = new();     // own evidence links (empty for Quoted-only)
    public RelevanceBasis Basis { get; set; }
    public bool IsHot { get; set; }
    public string Intent { get; set; } = string.Empty;     // LLM (relevant) / empty (non-relevant; Phase 6 writes)
    public List<string> MustConvey { get; set; } = new();  // LLM (relevant)
    public List<PlannedAttachment> Attachments { get; set; } = new();
    public string FilePath { get; set; } = string.Empty;   // planned sender-copy path, relative to case folder
    public List<string> CustodianIds { get; set; } = new(); // every mailbox holding a copy
}
```
Thread level: `Id` ("T-0001", numbered chronologically by first message), `Relevance` (Relevant/NotRelevant), `Category` (None/DailyBusiness/Ancillary/NewsletterSpamPersonal/Decoy), `IsPrivileged`, `IssueIds`, `FactIds`, `Subject`, `Purpose`, `Rationale`, `DecoyAngle`, `MustNotMention`, `LinkedThreadIds`, `OwnerCustodianId`, `Messages`.
Attachment: `DocId` ("DM-000317.0001"), `Kind` (`Models.AttachmentType`), `FileName`, `Title`, `Description`, `IsInline`, `DocChainId` (optional, for later version chains).
Plan root: `FormatVersion`, `PlanId` (Guid), `CreatedUtc`, `GuideHash`, `SettingsHash`, `DocIdPrefix`, `Settings` (snapshot), `Threads`, `Externals`, `Protocol` (prose sections), `Notes` (repair/mapping notes surfaced in Checks), `Guidance` (re-plan text).

**Plan IDs must not use `IdAllocator.Next(guide, ...)`.** It writes `guide.IdCounters`, which changes `guide.ComputeHash()` and makes the brand-new plan stale (Pitfall 2). Format plan IDs locally (`$"T-{n:0000}"`).

### Pattern 3: Planner pipeline and run semantics (PLAN-02, D-18, D-19)
- `CorpusPlanner(ILlmGateway llm, Random? random = null)` — `random ?? Random.Shared`; tests inject `new Random(42)`. No seed stored (D-20 satisfied).
- `Task<CorpusPlanResult> PlanAsync(CaseDraft draft, string guidance, IProgress<PlanProgress>? progress = null, CancellationToken ct = default)`.
- Read the guide, but **never mutate the draft** during planning. Build a new `CorpusPlan`. The caller assigns it on the UI thread only on success. Cancel/failure then discards by construction (D-18), and settings stay untouched.
- Progress stages: `Allocating` → `PlanningRelevant (chunk n/N)` → `PlanningOther (batch n/M)` → `WritingProtocol` (optional, see Pattern 10) → `Validating`.
- LLM calls: up to `MaxParallelCalls = 3` (same as the guide), using the `UnitRun` linked-CTS pattern. Fatal (`Auth`/`SpendLimit`) cancels siblings and fails the plan. A user cancel propagates `OperationCanceledException`. A non-user OCE is a timeout (record it as a chunk failure). A Refused chunk counts as a failed chunk: use the fallback templates and add a Checks note, or fail the plan if more than half the chunks refuse.
- **Re-ask once (D-19):** after the first pass, find threads with missing/blank subject or missing messages (map by `threadId`/`seq`). Re-send one request containing only the offending threads. Anything still missing gets a code template (subject from fact title; intent "Discuss {fact title}") plus a plan note → Checks warning.
- Guidance (D-23): passed verbatim to every relevant-chunk prompt. **Recommended:** when guidance is non-empty, also make one small Story call (`GuidanceWeightsDto { factWeights: [{ factId, multiplier: "boost"|"reduce"|"normal" }] }`) before allocation, so "more traffic around the March change-order dispute" changes volume, not just wording. Code maps boost → ×2, reduce → ×0.5 and ignores unknown IDs.

### Pattern 4: Allocation and timeline scheduling (PLAN-03, D-15)
1. `R`, `NR` from Pattern 1.
2. **Relevant threads:** estimate `T_R = max(datedFacts, round(R / meanRelevantDepth))` with `meanRelevantDepth ≈ 3.0`. Thread weights per fact: Hot 3, Key 2, Supporting 1 (× guidance multiplier). `Apportion(T_R, weights, min: 1)`.
3. **Message budget:** sample a depth per thread from the relevant depth distribution (Pattern 6). Hot/Key threads get +1 on the sampled depth (cap 12). Then adjust to hit `R` exactly: add messages to the longest-eligible threads or trim from the longest, never below 1 and never above the cap. Long sagas: when a fact's share would exceed ~12 messages in one thread, split it into more threads (D-17).
4. **Multi-issue threads:** find pairs of facts with different `IssueIds`, dates within 10 business days, and an overlapping `ActorIds ∪ KnownByIds`. Convert ~10-15% of threads into two-fact threads (`FactIds` = both, `IssueIds` = union) by merging one thread from each fact's allocation. Participants come from the intersection of knowers. [ASSUMED share]
5. **Anchor dates:** for each thread of fact F, offset = sample from {−3..−1 business days: 15% (lead-up, conveys no F content before F.Date), 0..+2: 50%, +3..+15: 35%} [ASSUMED]. Then clamp into the window and into the participants' active ranges. Fact-dense periods automatically get denser traffic because each fact contributes its threads there.
6. **Knowledge rule:** any message that carries F (`FactIds` contains F) must have `SentAt.Date ≥ F.Date`. Lead-up messages have no `FactIds` and are `Basis = Quoted` only if they come after an Own message. Simplest: make lead-up threads' roots Own-evidence for an *earlier* fact or keep the lead-up inside the same thread before the Own message, with `Basis = None`. **Recommended simple rule:** the root of every relevant thread is Own (linked to its fact, sent on/after F.Date); there are no lead-up messages in v1. The 15% lead-up bucket is optional.
7. **Participants:** eligible = `F.ActorIds ∪ F.KnownByIds`, filtered to cast active on the anchor date (empty ActiveFrom/To = unknown = active). Sender = an actor (prefer Key tier). To = 1-2 others. CC from the remaining eligible people per the CC distribution. **If only one eligible person exists**, plan a "note to self" message (To = sender). Real collections contain self-addressed emails, and it does not break knowledge gating. Add a Checks note suggesting the user add knowers.
8. **Privileged threads:** eligible when a counsel cast member (`Role` InHouseCounsel/OutsideCounsel) is a knower of the fact. Target ~8% of relevant threads [ASSUMED]. Participants restricted to counsel + people of the counsel's client side (in-house: same org; outside: orgs in `RepresentsOrgIds` of the counsel's law firm). Also plan ~2% of daily-business threads with in-house counsel (privileged, not relevant) so that "privileged ⇒ relevant" doesn't hold [ASSUMED].
9. **Hot:** for each Hot fact, flag 1 message as `IsHot` (2 when the fact has ≥3 threads): the Own-evidence message with the most participants. Hot ⇒ Relevant.

**Non-relevant allocation:**
- `Apportion(NR, mix)` → message counts per category. Then thread counts per category from its depth distribution (newsletters/spam/personal: all singletons).
- **Daily business:** sender custodians weighted by their relevant message volume (+1 smoothing), so key custodians carry lots of mundane mail (Pitfall 3 leakage defense). Recipients are colleagues in the same org (Key + Background); 20% external to other case orgs.
- **Ancillary:** senders from `Tier = Background`; recipients often a Key person of the same org.
- **Newsletter/spam/personal:** sender = `ExternalParty` (code-generated), recipient = one custodian; singletons; ~10% of personal items get a one-message reply.
- **Decoy:** same cast and time window as relevant traffic (dates concentrated within ±30 days of facts), depth distribution identical to relevant threads, attachments at the same rate.
- **Dates:** uniform over business days in the window (respecting active ranges). Decoys follow fact density.

### Pattern 5: Business calendar (PLAN-04, D-16)
- **Time zone:** `org.TimeZoneId` → `TimeZoneInfo.FindSystemTimeZoneById` in try/catch(`TimeZoneNotFoundException`, `InvalidTimeZoneException`) → fallback `UsTimeZones.ForLocation(org.Location)` → `UsTimeZones.Default`. Cache per id. Externals use the recipient's zone. [VERIFIED: IANA ids resolve on this machine]
- **Business window:** 09:00-17:00 local, Mon-Fri, not a federal holiday (observed date). DST gaps happen at 02:00 local, so 09:00-17:00 is never an invalid or ambiguous local time [VERIFIED: `IsInvalidTime(02:30)` true only at 02:30 on the transition day].
- Construct `new DateTimeOffset(localUnspecified, tz.GetUtcOffset(localUnspecified))`.
- **Root time:** sample an hour from a mild bimodal intra-day curve (peaks ~10:00 and ~14:00) [ASSUMED]; minute uniform. Using exactly uniform 9-5 is acceptable but looks robotic.
- **Reply latency** (business minutes, then converted to the replier's calendar). Mixture calibrated to the Avocado enterprise corpus, where reply times split into tertiles <25 min / 25-245 min / >245 min (32.8% / 32.8% / 34.4%), median ~44-95 min depending on recipient count, internal faster than external (66 vs. 135 min median) [CITED: Yang et al., SIGIR 2017]. Yahoo Mail: median 47 min, >90% within a day [CITED: Kooti et al. 2015]:
  - 33%: 2-25 min (uniform)
  - 33%: 25-240 min (log-uniform)
  - 26%: 4-8 business hours (same day or next morning)
  - 8%: 1-2 business days
  - External recipients: multiply by 1.5. Forwards: same mixture.
- **Business-time addition:** walk forward from the parent's instant converted into the replier's zone. If outside business time, snap to the next business-day 09:00 plus a small random offset (0-45 min, so not every snapped reply lands at exactly 9:00). Then add the latency, skipping non-business time. Enforce `SentAt > parent.SentAt + 1 min` and `SentAt > previous Seq's SentAt` (monotonic, D-16).
- **US federal holidays (5 U.S.C. 6103; OPM observance):** New Year's (Jan 1), MLK Day (3rd Mon Jan; from 1986), Washington's Birthday (3rd Mon Feb), Memorial Day (last Mon May), Juneteenth (Jun 19; **from 2021 only**), Independence Day (Jul 4), Labor Day (1st Mon Sep), Columbus Day (2nd Mon Oct), Veterans Day (Nov 11), Thanksgiving (4th Thu Nov), Christmas (Dec 25). Saturday → observed Friday; Sunday → observed Monday. **Jan 1 on Saturday is observed Dec 31 of the previous year**, so compute holidays for year+1 too. [CITED: opm.gov federal-holidays] Test vectors (weekdays verified): 2022-01-01 Sat → 2021-12-31; 2026-07-04 Sat → 2026-07-03; 2027-06-19 Sat → 2027-06-18; 2022-12-25 Sun → 2022-12-26; 2023-11-11 Sat → 2023-11-10; 2021-06-19 Sat → 2021-06-18 (first Juneteenth).
- Skip both the actual holiday and the observed day? Federal offices close on the observed day only. Recommendation: block the **observed** day; also block Dec 25 and Jan 1 themselves even on weekends (already weekends). Keep it simple: observed days + weekends.

### Pattern 6: Thread shapes (PLAN-05, D-17)
Reference data. In the Enron corpus, Klimt & Yang detected 101,786 threads in 200,399 messages. 71,695 threads (70%) were single-message; 61.6% of messages were in multi-message threads. Among the 30,091 multi-message threads, sizes were: 2 → 16,736 (55.6%), 3 → 4,782 (15.9%), 4 → 3,049 (10.1%), 5 → 1,282, 6 → 879, 7 → 903, 8 → 378, 9 → 214, 10 → 178, 11-20 → 1,260, 21-30 → 209, >30 → ~221; median 2 [CITED: Klimt & Yang, ECML 2004, extracted from the paper PDF in this session]. In Avocado, 53% of enterprise emails go to more than one recipient [CITED: Yang et al. 2017].

Recommended parameters (thread length in messages, per thread; cap 12):

| Class | 1 | 2 | 3 | 4 | 5-6 | 7-9 | 10-12 |
|-------|---|---|---|---|-----|-----|-------|
| Relevant, Decoy, Daily business | 30% | 28% | 15% | 10% | 9% | 5% | 3% |
| Ancillary | 45% | 30% | 12% | 7% | 4% | 2% | 0% |
| Newsletter/spam/personal | 100% (personal: 10% get a 1-message reply) | | | | | | |

These give a mean of ~2.9 messages per business thread [ASSUMED fit]. **Use the same table for relevant, decoy and daily-business** to avoid depth leakage (Pitfall 3).

Message kinds for non-root messages: Reply 55% / Reply-All 30% / Forward 15% [ASSUMED; FEATURES.md F suggests 60/25/15]. Reply-All is only possible when the parent had >1 recipient; otherwise it becomes a Reply.
- **Reply:** To = parent sender; CC = none (or the parent CC with 25% probability).
- **Reply-All:** To = parent sender + parent To − self; CC = parent CC − self.
- **Forward:** To = 1 new person (manager, counsel, or colleague) who is eligible (a knower for relevant threads, active); CC rarely. Forward to counsel → thread becomes privileged only if counsel is on the client side.
- **ReplyToSeq:** usually the previous message (80%), otherwise an earlier message (branching) [ASSUMED]. Seq order stays time order.

Root recipients: To count 1 (65%) / 2 (25%) / 3-4 (10%); CC count 0 (50%) / 1 (30%) / 2-3 (20%), so ~50-55% of messages have more than one recipient, matching Avocado's 53% [ASSUMED fit]. Prefer the sender's manager-like Key colleague for CC.

Attachments: default 20% of messages in business categories (relevant, decoy, daily, ancillary). Newsletters 0%; spam 5% (image). Type mix among enabled types: Word 40, Excel 25, PowerPoint 10, Calendar invite 12, Image 8, Voicemail 5 (renormalized over enabled types) [ASSUMED]. Calendar invites go on root messages only. Voicemail = a single-message "Voicemail from X" thread with one .mp3, To = custodian. Max attachments per message: 1 (80%), 2 (15%), 3 (5%).

### Pattern 7: Validator (PLAN-06, D-24)
`CorpusPlanValidator.Validate(CaseDraft draft, CorpusPlan? plan) → List<PlanFinding>`. Static, null-safe, wraps each rule in try/catch like `CaseGuideValidator.Run`, never throws. All findings are `Warning` (D-24; reuse `IssueSeverity`). Every finding has `Message` + `FixHint` + optional `EntityId` (thread/fact/cast id).

| # | Rule | Minimum (PLAN-06)? |
|---|------|--------|
| 1 | Total emails = settings total | yes |
| 2 | Relevant ratio within ±1 percentage point (or ±1 message) of settings | yes |
| 3 | Every message participant active on SentAt's local date | yes |
| 4 | Every dated key fact has ≥1 relevant message with `FactIds` containing it ("no planned evidence") | yes |
| 5 | SentAt inside window | |
| 6 | SentAt weekday, 09:00-17:00 in sender zone, not an observed holiday | |
| 7 | Monotonic: Seq order ascending in time; ReplyToSeq < Seq and parent earlier | |
| 8 | All ids resolve (cast/EXT/fact/issue/thread links) | |
| 9 | Label consistency: Relevant ⇔ thread has facts; NotRelevant ⇒ no facts; Decoy ⇒ category Decoy; Hot ⇒ Relevant and linked to a Hot fact | |
| 10 | Future leak: message's FactIds dated after SentAt | |
| 11 | Knowledge: participants of fact-carrying messages ⊆ ActorIds ∪ KnownByIds (except note-to-self) | |
| 12 | Privilege plausibility: privileged thread has counsel and no adverse-party participant | |
| 13 | Attachments: only enabled types; images/voicemail only when media available; file names valid; DocIds unique | |
| 14 | DocIDs unique and contiguous; message ids unique | |
| 15 | Non-relevant mix within ±3 points of settings | |
| 16 | Threads > 12 messages | |
| 17 | LLM gaps: threads using fallback content (from plan Notes) | |
| 18 | Leakage audit: relevant vs. same-pipeline non-relevant (daily+decoy) mean depth, attachment rate, recipients/message, sender-hour mean; warn if any ratio > 1.5 (Pitfall 3) | |
| 19 | Stale: plan GuideHash ≠ current (also shown as the banner) | |

### Pattern 8: Review Plan screen (PLAN-07, D-22..D-26)
- **Hosting the run:** the planning run is best hosted on `StepReviewPlan` (auto-start in `OnEnterStepAsync` when there is no plan or `SettingsHash` differs), with the inline progress/cancel pattern from `StepCaseGuide.Run.cs`. To the user, "Next on Corpus Settings runs planning" (D-12) still holds. Corpus Settings stays simple, and a cancelled plan leaves the user on Review Plan with an empty state and a "Plan Corpus" button. Alternative: run inside Corpus Settings and navigate on success. That needs a busy state inside `ValidateStepAsync`. The planner should pick one; recommended: Review Plan. Confirm before replacing an approved plan when settings changed (D-26).
- **Tiles:** Emails, Threads, Relevant %, Hot, Privileged, Attachments, Est. generation cost.
- **Histogram:** a custom `Control` with `OnPaint`. Month buckets from window start to end, stacked relevant (accent) over non-relevant (muted). Sizes via `LogicalToDeviceUnits`. Axis labels "MMM yy", tooltip with counts. ≤ 48 buckets (Complex timelines are up to 48 months + 3).
- **Grids:** three `DataGridView`s with `GridStyle.Apply`, bound to `List<T>`/`DataTable` (a `DataTable` gives built-in click-to-sort). Columns: Name, Relevant, Not relevant, Total. By fact: ID, Date, Title, Threads, Messages.
- **Checks:** reuse the Case Guide Checks card visuals with warning dots and fix hints. Re-plan is the only action.
- **Stale banner (D-25):** `plan.GuideHash != draft.Guide.ComputeHash()` and `DismissedStaleHash != currentGuideHash`. Approval is withdrawn (`Approved = false`) while stale. Dismiss stores the current guide hash.
- **Approve:** writes the three files (Pattern 9/10), sets `Approved`, `ApprovedUtc`, `WrittenFiles`, advances `CaseStage`, saves.

### Pattern 9: Answer key (KEY-01..04)
**Columns (D-32 order, plus two recommended extras marked +):**
`DocID, Doc Type(+), File Path, File Name(+), Custodian, All Custodians, Date Sent, From, To, CC, Subject, Thread ID, Family ID, Parent DocID, Relevance, Issue Codes, Fact IDs, Hot, Privileged, Near-Miss Decoy, Non-Relevant Category, Rationale, Status`

D-32 lists "family/parent ID" as one column. eDiscovery load files carry both a family/group range (BegAttach/EndAttach or GroupID) and a ParentID for attachments [CITED: Veritas Load File Import Guide; Lexbe standard load file fields]. **Recommended:** `Family ID` (the parent email's DocID on every family member, including the parent) and `Parent DocID` (blank on emails, the parent's DocID on attachments). If the planner prefers strict D-32, keep a single `Family ID` column.

- **Rows:** for each thread → each message (Doc Type `Email`) → each attachment (Doc Type `Attachment`). Row count = messages + attachments (D-28). Sort by DocID.
- **DocIDs (D-30):** sort all messages by `SentAt` (UTC), ties by thread id/seq, and number them `{prefix}-{n:000000}`. Attachments `{parentDocId}.{k:0000}`. **Never number relevant-first** (Pitfall 4). Thread IDs are also numbered chronologically by first message.
- **Prefix:** case title → producing-party name. Pleadings: first defendant/respondent party; memo: `Memo.Organization`; else the first organization. Use `ShortName` if set ("Dunder Mifflin" → `DM`). Otherwise initials of significant words, excluding `Inc, LLC, LLP, Corp, Corporation, Co, Company, Ltd, LP, PC, The, of, and, &`, 2-4 letters, uppercase ASCII only. Fallback `RD`. [ASSUMED derivation; matches the user's DM example]
- **Custodian (D-28):** sender if the sender is a cast member; otherwise the first cast recipient (newsletters/spam/personal from externals). **All Custodians:** distinct cast members among From/To/CC, joined by "; ". Display as "Full Name" (or `Name <email>`).
- **File path (D-29):** `Mailboxes/{custodian-folder}/{Sent Items|Inbox}/{DocID}.eml` for the custodian copy (Inbox when the custodian is a recipient of an external message). Custodian folder = sanitized `{first.last}` from the email local part, falling back to the CH id. Attachments: the parent's path (attachments live inside the EML), with the attachment's name in `File Name`. Paths are opaque (DocID only), so the label never leaks through names (Pitfall 3). Phase 5 must use exactly these paths for the sender copy.
- **Date Sent:** ISO 8601 with offset, `yyyy-MM-dd HH:mm zzz` (sender-local), InvariantCulture. Excel: keep as text for exactness; the Statistics sheet does the month grouping.
- **Subject:** root = thread subject; Reply/ReplyAll → `ThreadingHelper.AddReplyPrefix`; Forward → `AddForwardPrefix` (applied cumulatively through the reply chain is unnecessary; prefix once from the root subject).
- **Relevance & propagation (KEY-03, D-31):** relevant thread ⇒ every message Relevant (root Own; later messages Own if they carry FactIds, else Quoted, rationale "Quotes {parent DocID}, which is relevant: {thread rationale}"). Attachments inherit the parent's relevance, rationale "Attachment to relevant email {DocID}; family coded Relevant." Non-relevant threads never contain relevant messages (the planner never mixes), so no non-relevant message quotes relevant text. Non-relevant rationales are code templates by category, e.g. "Routine business correspondence unrelated to the issues in this matter." / "Near-miss: mentions {project} but concerns {decoy angle}, not the alleged conduct."
- **Issue Codes / Fact IDs:** `; `-joined ids. For Quoted messages and attachments, use the thread's issue/fact ids (family-level coding).
- **Status:** `Planned`.
- **Flags:** `Y`/`N`.

**CSV (`AnswerKeyCsvWriter`):** RFC 4180: CRLF row terminator; quote a field if it contains `,`, `"`, CR or LF; double embedded quotes. Write **UTF-8 with BOM** (`new UTF8Encoding(encoderShouldEmitUTF8Identifier: true)`) so Excel detects UTF-8 for names with accents [ASSUMED Excel behavior; widely documented]. **Formula injection:** if a field starts with `=`, `+`, `-`, `@`, tab, CR, LF, or full-width `＝＋－＠`, prefix it with a single quote `'` and quote the field [CITED: OWASP CSV Injection]. Apply to every text field: LLM subjects and rationales and guide names are all untrusted.

**XLSX (`AnswerKeyXlsxWriter`)**, verified shape (0 validator errors):
- Element order inside `Worksheet`: `SheetViews` → `SheetFormatProperties`(optional) → `Columns` → `SheetData` → `AutoFilter`.
- Frozen header: `Pane { VerticalSplit = 1, TopLeftCell = "A2", ActivePane = BottomLeft, State = Frozen }`.
- Autofilter: `AutoFilter { Reference = "A1:{lastCol}{lastRow}" }`, plus a workbook `DefinedName` `_xlnm._FilterDatabase` (hidden, `LocalSheetId`) so Excel shows the filter state consistently.
- Column widths: `Column { Min, Max, Width, CustomWidth = true }` (rationale ~60, subject ~45, ids ~14).
- Stylesheet with a bold, filled header style and a wrap/top-aligned body style (index 0 must remain the default).
- Text cells: `CellValues.InlineString` (or `String`). **Never write `CellFormula`.** Text starting with `=` is then inert in XLSX, so no quote prefix is needed there. Numbers on the Statistics sheet: `CellValues.Number` with `CellValue(int)`.
- Strip XML-invalid control characters (reuse the docx exporter's sanitizer) and cap cell text at 32,767 characters.
- Sheets: "Answer Key", "Statistics" (sections: by custodian [custodian, relevant, not relevant, total], by issue [issue id, title, relevant docs], by relevance/category [Relevant, Not Relevant × category, Hot, Privileged, Near-miss], by month [yyyy-MM, relevant, not relevant]), "Legend" (issues with titles and relevance criteria, facts with dates/titles, column definitions, category definitions). Multiple tables on one sheet are separated by a blank row and a bold section title.
- Refactor `OfficeDocumentService.CreateExcelDocument` or add a new writer? **Add a new pure writer** in `Services/Corpus`. The legacy `OfficeDocumentService` serves attachment generation and is pinned by `DocumentRegressionTests`; don't change its output.

### Pattern 10: Review protocol (KEY-05, D-34)
Typical protocol sections: introduction/objective, case background, key people, issue codes with when to apply them, coding instructions, privilege guidance (including partially privileged documents), hot-document handling, and family coding consistency (code the parent and attachments consistently) [CITED: CLA "Considerations When Drafting a Document Review Protocols Memorandum"; Everlaw document review guide].

Recommended outline (code builds headings and tables; LLM writes the marked prose):
1. Title block: "Document Review Protocol — {case title}", prepared-for line, date, "Privileged & Confidential — Attorney Work Product".
2. Purpose and scope (LLM prose).
3. Matter background (LLM prose from core doc summary + issues; no facts beyond the guide).
4. Parties and key people (code table: name, title, organization, role. Counsel listed separately with firm names).
5. Review fields and decision rules (code table: Relevance, Issue Codes, Hot, Privileged, Near-miss is **not** a reviewer field. **Do not expose "near-miss decoy" or "non-relevant category" as coding fields**: they are answer-key metadata).
6. Issue codes: per issue, code + title (code), `RelevanceCriteria` (code, verbatim), LLM-written "Examples of relevant documents" (2-3 hypothetical bullets) and "Not relevant" bullets.
7. Hot documents guidance (LLM prose, generic to the issues).
8. Privilege guidance (code: list counsel names and law firms; LLM prose on attorney-client/work product, waiver when third parties are copied, partial privilege → redact).
9. Family and thread rules (code text: family coding, quoted replies inherit relevance of quoted content, attachments coded with the family).
10. Questions/escalation (code boilerplate).

DTO: `ReviewProtocolProseDto { purpose, background, hotGuidance, privilegeGuidance, issues: [{ issueId, relevantExamples: [string], notRelevantExamples: [string] }] }`. No nullables. Map by issueId; unknown ids dropped. Prompt input: guide only (issues, criteria, cast/counsel, core-doc summary), **never plan data, DocIDs or labels**. Test: the output text contains no `{prefix}-\d{6}` pattern and no thread/fact counts.
**When to call:** recommended as the last planning stage ("Writing review protocol"), stored in `CorpusPlan.Protocol`, so Approve is a pure deterministic write. On failure, use code fallback prose (criteria-only sections) plus a Checks warning. Max output 8,000 tokens.

### LLM chunk DTOs (strict-schema safe)
```csharp
// Source: pattern from CaseGuideGenerator.FactBatchDto; no nullable members (0 unions), no recursion
internal class ThreadPlanBatchDto
{
    [JsonPropertyName("threads")]
    [Description("One entry per thread listed in the request, using its threadId")]
    public List<ThreadPlanDto> Threads { get; set; } = new();
}

internal class ThreadPlanDto
{
    [JsonPropertyName("threadId")] [Description("Thread ID from the request, e.g. T-0042")]
    public string ThreadId { get; set; } = string.Empty;
    [JsonPropertyName("subject")] [Description("Realistic email subject line, no RE:/FW: prefix, at most 10 words")]
    public string Subject { get; set; } = string.Empty;
    [JsonPropertyName("purpose")] [Description("One sentence: what this thread is about and why it happens now")]
    public string Purpose { get; set; } = string.Empty;
    [JsonPropertyName("rationale")] [Description("One sentence: why this thread is relevant to the listed issue and fact")]
    public string Rationale { get; set; } = string.Empty;
    [JsonPropertyName("linkedThreadIds")] [Description("Thread IDs from the request that this thread refers back to; empty if none")]
    public List<string> LinkedThreadIds { get; set; } = new();
    [JsonPropertyName("messages")] [Description("One entry per message in the request, by seq")]
    public List<MessagePlanDto> Messages { get; set; } = new();
}

internal class MessagePlanDto
{
    [JsonPropertyName("seq")] [Description("Message number from the request")]
    public int Seq { get; set; }
    [JsonPropertyName("intent")] [Description("One sentence: what the sender does in this message")]
    public string Intent { get; set; } = string.Empty;
    [JsonPropertyName("mustConvey")] [Description("One to three specific points the message must contain, using the exact names and values given")]
    public List<string> MustConvey { get; set; } = new();
    [JsonPropertyName("attachmentTitles")] [Description("Document title for each planned attachment, in order; empty if none")]
    public List<string> AttachmentTitles { get; set; } = new();
}

internal class SubjectSeedBatchDto
{
    [JsonPropertyName("seeds")] public List<SubjectSeedDto> Seeds { get; set; } = new();
}

internal class SubjectSeedDto
{
    [JsonPropertyName("slotId")] [Description("Slot ID from the request")] public string SlotId { get; set; } = string.Empty;
    [JsonPropertyName("subject")] [Description("Realistic subject line")] public string Subject { get; set; } = string.Empty;
    [JsonPropertyName("angle")] [Description("For decoy slots: one sentence on the unrelated topic; otherwise empty string")]
    public string Angle { get; set; } = string.Empty;
    [JsonPropertyName("senderName")] [Description("For external slots: the sending publication, company or person; otherwise empty string")]
    public string SenderName { get; set; } = string.Empty;
}
```
Register all top-level types in `CorpusPlanner.SchemaTypes` (plus `ReviewProtocolProseDto`, `GuidanceWeightsDto`) and add them to `CaseSchemaTests`' theory data (strict, ≤16 unions).

**Chunk sizing:** sort relevant skeleton threads by first-message time. Cut chunks of ≤20 threads and ≤60 messages. Each chunk prompt contains: the case synopsis (issues with titles; core-document title), the cast active in the chunk window (id, name, title, org, role), the canonical values (label = value), the facts **in** the window, plus the 3 facts immediately before and after it as context ("already happened" / "coming later: do not mention") — the "coming later" list can also be dropped, since knowledge gating is Phase 5's job. Then the skeleton per thread: id, fact ids + titles, issue ids, and per message seq/kind/from→to/cc/local date-time/attachment kinds, plus the IDs and subjects of already-planned threads in the previous chunk (if run sequentially) for `linkedThreadIds`. Output ≈ 20 threads × ~300 tokens ≈ 6K tokens, so use `MaxOutputTokens = 16_000` (includes reasoning; gateway doubles once to the 21,333 Anthropic ceiling on truncation) [VERIFIED: LlmGateway, AnthropicTextProvider]. **Parallel vs. sequential:** with 3 parallel calls, links across chunks are impossible. Allow links only within a chunk plus a code-supplied list of earlier skeleton thread IDs with their fact titles (subjects unknown yet). That's fine because the code knows the fact links.

**Subject seeding:** 100 slots per call (~3K output tokens). Slot line: `slotId | category | sender name/title/org/department or "external: newsletter/spam/personal" | recipient | month | messages | attachment kinds`. Daily/ancillary/newsletter prompts contain **no** facts, issues or values. Decoy prompt: project names + organization names (from `Values` of kind `ProjectName` and orgs) + the must-not-mention list (fact titles and one-line descriptions, plus `Amount`/`InvoiceNumber`/`ChangeOrderNumber`/`ContractNumber`/`PurchaseOrder` values linked to facts). Post-check decoy subject + angle: case-insensitive contains-check for any must-not-mention value string. A hit means re-ask once, then a template ("{Project} {routine topic}"). Store `MustNotMention` on decoy threads for Phase 6.

### Anti-Patterns to Avoid
- **Mutating the draft during planning:** breaks "cancel discards" and races autosave snapshots taken on the UI thread.
- **Letting the LLM return dates, recipients, counts or labels:** only text fields exist in the DTOs (D-13).
- **Allocating plan ids with `IdAllocator.Next(guide, …)`:** mutates the guide → plan stale at birth.
- **Writing the plan into `case.json`:** multi-MB UI-thread serialization on every later autosave.
- **Numbering DocIDs or threads relevant-first, or encoding labels in paths:** label leakage.
- **Different shape tables for relevant vs. same-custodian non-relevant mail:** label leakage (depth/attachments/CC).
- **Reusing `OfficeDocumentService.CreateExcelDocument` by changing it:** breaks legacy regression pins; write a new writer.
- **`CellFormula` or unescaped CSV text:** formula injection.
- **Using `TrackBar` unthemed for the ratio slider:** it renders with system chrome and inconsistent DPI scaling. Prefer a custom-drawn slider or a NumericUpDown + preset buttons in the Phase 3 visual language [ASSUMED; verify with a render].

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| IANA → offset/DST | Custom DST tables or an IANA→Windows map | `TimeZoneInfo.FindSystemTimeZoneById(ianaId)` | ICU-backed on .NET 6+ Windows; verified here |
| Strict JSON schema | Hand-written schema strings | `JsonSchemaRegistry` via the gateway | Provider strictness differences already handled and tested |
| Parallel bounded LLM calls with fatal stop | New concurrency code | Copy `RunUnitAsync`/`UnitRun` from `CaseGuideGenerator` | Timeout vs. cancel vs. fatal classification is already debugged (01-12, 01-13) |
| Atomic file writes | `File.WriteAllBytes` | `CaseStore.WriteAtomicAsync` | OneDrive/AV transient locks, temp cleanup |
| Docx structure | Raw OpenXml in a new class | `WordBuilder` (make internal) | Schema-ordered, validator-clean, already tested |
| Largest remainder | Ad-hoc rounding | Extracted `Apportion` helper | Exact totals; the `DistributeEmails` bug class was fixed this way (01-13) |
| RE:/FW: prefixes | New string logic | `ThreadingHelper.AddReplyPrefix/AddForwardPrefix` | Consistent with Phase 5 threading |
| File name sanitization | New regex | `FileNameHelper`, `CaseFolderNaming` rules | Reserved names, invalid chars already covered |

**Key insight:** this phase's risk is not technology but invariants (sums, monotonic time, knowledge, labels, leakage). Put every invariant in a pure function and a validator rule, and test with an injected `Random`.

## Common Pitfalls

### Pitfall 1: `CaseGuide.ComputeHash()` hashes UI state → spurious "out of date" banners
**What goes wrong:** `ComputeHash()` serializes the whole guide, including `DismissedStaleKey`, every `GuidePartState` (`GeneratedUtc`, `CompletedUnits`, `Status`), `NeedsReview`/`ReviewReason`, `IsUserEdited`, `DetailsPending`. Clicking "Not Now" on the guide's stale banner or "Mark reviewed" makes the plan stale even though no content changed. [VERIFIED: CaseGuide.cs line 67; only used by tests today]
**How to avoid:** D-05 says the plan stores `CaseGuide.ComputeHash()`. Keep that name, but change its body to hash a **content projection**: issues (id, title, criteria, elements), organizations (id, name, short name, kind, domain, time zone, represents), cast (id, names, title, org, tier, role, email, active range, signature style), facts (id, date, title, description, issue/actor/known ids, significance), values (id, kind, label, value, fact ids). Existing tests (`ComputeHash_ChangesOnEdit`, round-trip equality) still pass. Add tests that `DismissedStaleKey`/`NeedsReview` changes do **not** change the hash.
**Warning signs:** the banner appears right after approving, or after reviewing a fact.

### Pitfall 2: Plan IDs via `IdAllocator` mutate the guide
**What goes wrong:** `IdAllocator.Next(guide, "T")` writes `guide.IdCounters["T"]`. That changes `ComputeHash()` (even with the projection, if counters are included) and pollutes the guide with plan state.
**How to avoid:** plan-local formatting only; never touch `guide.IdCounters` from Phase 4 code. Exclude `IdCounters` from the content hash anyway.

### Pitfall 3: Label leakage through metadata (PITFALLS #3)
**What goes wrong:** relevant threads deeper, with more CC and attachments, sent only by key people, clustered at fact dates; DocIDs relevant-first.
**How to avoid:** a shared shape table for relevant/decoy/daily-business; daily-business senders weighted by relevant volume; decoys clustered near facts; chronological DocIDs; opaque paths; validator rule 18 (leakage ratios).
**Warning signs:** Statistics sheet shows a much higher attachment % or mean recipients for Relevant.

### Pitfall 4: Count drift between screen, plan and key
**What goes wrong:** the settings screen rounds differently than the allocator; trims/merges change totals; attachments are counted as emails.
**How to avoid:** one `CorpusMath.Split(total, percent)` function used by UI and allocator; post-allocation assert (validator rule 1/2); emails vs. documents are distinct counters in `PlanSummary`.

### Pitfall 5: Facts on weekends/holidays and timeline vs. business-hours conflicts
**What goes wrong:** a Saturday fact, or a fact on Thanksgiving, gets an email that day (violates D-16) or before the fact (future leak).
**How to avoid:** "carrying" messages start at the first business instant on/after `F.Date` in the sender's zone. The validator flags both.

### Pitfall 6: Participants outside active range or outside the knowers
**What goes wrong:** a forward to a manager who is not in `KnownByIds`; a CC to someone who left; a reply 3 days later after `ActiveTo`.
**How to avoid:** the skeleton draws only from active knowers. After scheduling, repair (D-19): if a later message falls after a participant's `ActiveTo`, swap the participant for another eligible one, or pull the message earlier within latency limits. If impossible, truncate the thread (and rebalance the count) and note it.

### Pitfall 7: LLM output that doesn't map
**What goes wrong:** missing threads/messages, invented thread IDs, IDs or unknown names in intents, extra messages, empty subjects.
**How to avoid:** map strictly by `threadId` (canonicalize with trim/upper) and `seq`; drop extras; strip `CH-`/`F-`/`ISS-`/`T-` tokens not in the known set from text; re-ask once for missing; template fallback; all mapping notes go to `plan.Notes` → Checks.

### Pitfall 8: Prompts that break D-02 or leak legal framing
**What goes wrong:** copying `CaseGuidePrompts.SystemPrompt` wording "make those invented names fictional" into planning prompts; legal words ("alleged", "plaintiff") in thread purposes leaking into Phase 5 emails.
**How to avoid:** a new byte-stable `CorpusPlanPrompts.SystemPrompt` that says to keep names exactly as given (including names from films/TV/books) and contains no "fictional names" instruction; instruct "plain business language, no legal terms" for subject/intent/mustConvey (the rationale may use issue titles). Add a prompt-invariant test like `CaseGuidePromptInvariantTests` (no "fictional", no JSON instructions via `CaseTestData.AssertNoJsonInstructions`).

### Pitfall 9: Too few relevant emails for the facts
**What goes wrong:** 250 emails × 5% = 13 relevant for 20 facts.
**How to avoid:** D-07 warning on the settings screen (hard floor = dated facts). The planner still plans ≥1 single-message thread per fact (exceeding the ratio slightly), and the validator reports the ratio difference with a fix hint ("Raise % relevant to at least N%").

### Pitfall 10: Old cases and stage mapping
**What goes wrong:** cases saved at `Stage = Summary` before Phase 4 open on Case Summary with no corpus; ordinal comparisons in `AdvanceStage` change when enum values are inserted.
**How to avoid:** insert `CorpusSettings`, `ReviewPlan` before `Summary` (stored as names via `JsonStringEnumConverter`, so case.json is safe [VERIFIED: CaseDraft.JsonOptions]). Case Summary shows a "Corpus not planned yet" row with a link to Corpus Settings. Extend `StepTypeForStage` and `WizardFlowTests`. Bump `CaseDraft.CurrentFormatVersion` to 2 so an older build refuses the file rather than silently dropping `Corpus` on save (`PeekFormatVersion` already guards).

### Pitfall 11: Plan file and case.json out of sync
**What goes wrong:** crash between writing `corpus-plan.json` and `case.json`; a user copies an old plan file; folder moved.
**How to avoid:** write the plan file first, then case.json with `PlanId`. On load, accept the plan only if `plan.PlanId == state.PlanId` and the file is under a size cap (e.g. 100 MB), deserializes, and passes `Normalize()`. Otherwise treat it as "no plan" with a notice. `MoveFolder` copies subfolders and files [VERIFIED: CaseStore.CopyDirectory].

### Pitfall 12: Non-thread-safe test fake under parallel chunks
**What goes wrong:** `FakeLlmProvider` uses a plain `Queue`/`List`; with 3 parallel calls responses are dequeued in nondeterministic order and `Requests.Add` races.
**How to avoid:** add a lock and an optional `Func<LlmProviderRequest, LlmProviderResponse> Respond` that builds responses from the request (e.g. parse thread IDs out of the prompt). Or run planner tests with `MaxParallelCalls` injectable = 1.

### Pitfall 13: Media toggles stale across sessions
**What goes wrong:** images/voicemail saved on, reopened without an OpenAI key; the plan includes image specs that Phase 6 can't render.
**How to avoid:** effective-media evaluation on enter and at plan time; validator rule 13.

### Pitfall 14: Invalid XML characters and long text in OpenXml
**What goes wrong:** an LLM emits `\u0001` or a lone surrogate → corrupt XLSX/DOCX; >32,767 chars in a cell → Excel repair prompt.
**How to avoid:** reuse the exporter's XML-char filter for all cell/paragraph text; cap cell length; validate in tests with `OpenXmlValidator`.

## Code Examples

### Holiday table with observed dates
```csharp
// Source: rules from 5 U.S.C. 6103 / OPM observance policy (opm.gov/policy-data-oversight/pay-leave/federal-holidays)
public static HashSet<DateOnly> ObservedFederalHolidays(int year)
{
    var set = new HashSet<DateOnly>();
    // year+1 New Year's can be observed on Dec 31 of this year
    foreach (var y in new[] { year, year + 1 })
    {
        void Fixed(int m, int d) { var h = Observed(new DateOnly(y, m, d)); if (h.Year == year) set.Add(h); }
        void Nth(int m, DayOfWeek dow, int n) { if (y == year) set.Add(NthWeekday(y, m, dow, n)); }

        Fixed(1, 1);
        if (y >= 1986) Nth(1, DayOfWeek.Monday, 3);   // Martin Luther King, Jr.
        Nth(2, DayOfWeek.Monday, 3);                   // Washington's Birthday
        if (y == year) set.Add(LastWeekday(y, 5, DayOfWeek.Monday)); // Memorial Day
        if (y >= 2021) Fixed(6, 19);                   // Juneteenth
        Fixed(7, 4);
        Nth(9, DayOfWeek.Monday, 1);                   // Labor Day
        Nth(10, DayOfWeek.Monday, 2);                  // Columbus Day
        Fixed(11, 11);                                 // Veterans Day
        Nth(11, DayOfWeek.Thursday, 4);                // Thanksgiving
        Fixed(12, 25);
    }
    return set;
}

private static DateOnly Observed(DateOnly d) => d.DayOfWeek switch
{
    DayOfWeek.Saturday => d.AddDays(-1),
    DayOfWeek.Sunday => d.AddDays(1),
    _ => d
};
```

### Sender-local business instant
```csharp
// Source: TimeZoneInfo IANA lookup verified on .NET 10 / Windows 11 in this session
public static DateTimeOffset At(TimeZoneInfo tz, DateOnly day, TimeOnly time)
{
    var local = day.ToDateTime(time, DateTimeKind.Unspecified);   // 09:00-17:00 never hits a DST gap (02:00)
    return new DateTimeOffset(local, tz.GetUtcOffset(local));
}

public static TimeZoneInfo Resolve(CaseOrganization? org)
{
    foreach (var id in new[] { org?.TimeZoneId, UsTimeZones.ForLocation(org?.Location), UsTimeZones.Default })
    {
        if (string.IsNullOrWhiteSpace(id)) continue;
        try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            // Untrusted case.json value; fall through to the next candidate
        }
    }
    return TimeZoneInfo.Utc;
}
```

### CSV field encoding (RFC 4180 + OWASP)
```csharp
// Source: OWASP CSV Injection (community.owasp.org/attacks/CSV_Injection); RFC 4180 quoting
private static readonly char[] FormulaLeads = { '=', '+', '-', '@', '\t', '\r', '\n', '＝', '＋', '－', '＠' };

public static string Field(string? value)
{
    var text = value ?? string.Empty;
    var guarded = text.Length > 0 && Array.IndexOf(FormulaLeads, text[0]) >= 0;
    if (guarded)
        text = "'" + text;
    var needsQuotes = guarded || text.IndexOfAny(new[] { ',', '"', '\r', '\n' }) >= 0;
    return needsQuotes ? "\"" + text.Replace("\"", "\"\"") + "\"" : text;
}
// Rows joined with "\r\n"; file bytes = new UTF8Encoding(true).GetPreamble() + UTF8 body
```

### XLSX sheet with frozen header and autofilter (validated: 0 OpenXmlValidator errors)
```csharp
// Source: scratch prototype run in this session against DocumentFormat.OpenXml 3.5.1
var ws = new Worksheet(
    new SheetViews(new SheetView(
        new Pane { VerticalSplit = 1, TopLeftCell = "A2", ActivePane = PaneValues.BottomLeft, State = PaneStateValues.Frozen },
        new Selection { Pane = PaneValues.BottomLeft, ActiveCell = "A2", SequenceOfReferences = new ListValue<StringValue> { InnerText = "A2" } })
        { WorkbookViewId = 0, TabSelected = isFirstSheet }),
    new Columns(new Column { Min = 1, Max = 1, Width = 16, CustomWidth = true } /* ... */),
    sheetData,
    new AutoFilter { Reference = $"A1:{lastColumn}{lastRow}" });
// Workbook: Sheets first, then DefinedNames:
// new DefinedName($"'{sheetName}'!$A$1:${lastColumn}${lastRow}") { Name = "_xlnm._FilterDatabase", LocalSheetId = sheetIndex, Hidden = true }
// Text cells: new Cell { CellReference = "B2", DataType = CellValues.InlineString, StyleIndex = 2, InlineString = new InlineString(new Text(text) { Space = SpaceProcessingModeValues.Preserve }) }
```

### Largest remainder with a per-item minimum
```csharp
// Source: generalizes CaseGuideGenerator.Distribute (in-repo, 03-10)
public static List<int> Apportion(int total, IReadOnlyList<double> weights, int min = 0)
{
    var n = weights.Count;
    var result = Enumerable.Repeat(min, n).ToList();
    var remaining = total - min * n;
    if (n == 0 || remaining <= 0) return result;   // caller reports the shortfall (e.g. too few relevant emails)
    var sum = weights.Sum();
    var shares = weights.Select(w => sum > 0 ? remaining * w / sum : (double)remaining / n).ToList();
    for (var i = 0; i < n; i++) result[i] += (int)Math.Floor(shares[i]);
    var left = total - result.Sum();
    foreach (var i in Enumerable.Range(0, n).OrderByDescending(i => shares[i] - Math.Floor(shares[i])).ThenBy(i => i).Take(left))
        result[i]++;
    return result;
}
```

### Generation cost estimate heuristic (D-21)
```csharp
// All constants [ASSUMED]; keep them in one class with a unit test so Phase 5-7 can recalibrate from real usage
public const int RelevantInputPerMessage = 4_000, RelevantOutputPerMessage = 450;   // guide slice + ledger + plan, amortized per message
public const int OtherInputPerMessage = 1_500, OtherOutputPerMessage = 300;
public const int DocumentInput = 3_000, DocumentOutput = 1_500;                      // Word/Excel/PowerPoint attachment
public const int SmallAttachmentInput = 800, SmallAttachmentOutput = 200;            // calendar invite, voicemail script, image prompt
public const decimal QaOverhead = 1.15m;                                             // ledger extraction, validation retries
// cost = Σ count × model.CalculateTotalCost(in, out) × QaOverhead; show as "about $X (±50%)"; images/TTS shown as a separate note
```
Example: 5,000 emails, 20% relevant, 20% attachments (mostly documents) on Claude Opus 5.5 ($4/$20 per M) ≈ relevant $25 + other $48 + attachments ~$42 → **≈ $130**. On GPT-6.1 Sol ($2/$10) ≈ $65 [ASSUMED heuristic; prices VERIFIED in AIModelConfig.cs]. A future batch discount (Phase 7) is not applied.

**Planning cost/time (this phase):** ~330 relevant threads → ~17 chunks × (~12K in, ~7K out). ~2,200 non-relevant slots → ~22 batches × (~4K in, ~3K out). Protocol: 1 call. On Opus 5.5 ≈ 0.3M in × $4 + 0.19M out × $20 ≈ **$5**. On GPT-6.1 Sol ≈ **$2.5**. Wall time with 3 parallel calls ≈ 4-10 minutes at 5K [ASSUMED throughput]. For 1,000 emails, ≈ 1-2 minutes and under $1.50.

## State of the Art

| Old Approach | Current Approach | When Changed | Impact |
|--------------|------------------|--------------|--------|
| Legacy `EmailGenerator`: LLM picks dates/recipients/reply index; storylines in isolation | Code-owned manifest; LLM writes text only | This project's redesign (Roadmap) | No chronology inversions or replyToIndex bugs; exact answer key |
| Rolling "story so far" summaries | Structured facts + knowledge gating (Phase 5) + plan links | Roadmap | Consistency independent of generation order |
| Windows-only time zone ids | IANA ids directly via `TimeZoneInfo` (ICU) | .NET 6 | `CaseOrganization.TimeZoneId` already stores IANA ids |
| Labels inferred after generation | Labels assigned in the plan; key is a projection | Roadmap decision | Key exists before generation; later phases only add Status |

**Deprecated/outdated:** `OfficeDocumentService.CreateExcelDocument` (single sheet, no styles) is fine for attachments but not for the key.

## Assumptions Log

| # | Claim | Section | Risk if Wrong |
|---|-------|---------|---------------|
| A1 | Thread-length table, reply/reply-all/forward 55/30/15, CC/To counts, branch 20% | Pattern 6 | Realism only; tune later. Calibrated loosely to Enron/Avocado |
| A2 | Latency mixture buckets | Pattern 5 | Realism only; tertiles cited, buckets fitted |
| A3 | Attachment type mix and 20% default rate | Pattern 6 | Realism; user can override |
| A4 | Privileged ~8% of relevant threads + ~2% of daily business; hot = 1-2 messages per Hot fact | Pattern 4 | Counts on the summary; user can override (D-09) |
| A5 | Multi-issue threads 10-15%; anchor offset distribution | Pattern 4 | Realism of timeline density |
| A6 | DocID prefix derivation (producing party, ShortName, initials) | Pattern 9 | Wrong letters (e.g. DMP vs DM); consider an editable prefix in Advanced |
| A7 | Every cast member is a custodian; externals never are | Pattern 9 | Phase 5 mailbox layout depends on it; confirm with user |
| A8 | Planned path scheme `Mailboxes/{first.last}/{Sent Items|Inbox}/{DocID}.eml`; attachments share the parent path | Pattern 9 | Phase 5 must follow; changeable before Phase 5 |
| A9 | Cost/time constants for planning and generation estimates | Code Examples | Estimate off by 2x; labeled "rough" (D-21) |
| A10 | Excel needs UTF-8 BOM to auto-detect UTF-8 CSV | Pattern 9 | Mojibake in names when opened by double-click |
| A11 | WinForms DataVisualization charting unavailable on modern .NET | Alternatives | None (we paint our own anyway) |
| A12 | TrackBar renders poorly against the Phase 3 theme | Anti-Patterns | Minor; verify by render |
| A13 | Hosting the planning run on Review Plan satisfies D-12 | Pattern 8 | If the user insists on Corpus Settings hosting, the run UI moves there |
| A14 | Protocol prose generated as the last planning stage (not at Approve) | Pattern 10 | If at Approve, Approve needs progress/failure handling |
| A15 | Extra key columns (Doc Type, File Name, separate Parent DocID) are acceptable additions to D-32 | Pattern 9 | Strict D-32 reading: drop extras |
| A16 | Changing `CaseGuide.ComputeHash()` to a content projection honors D-05 | Pitfall 1 | If D-05 means "byte-for-byte current behavior", spurious stale banners remain |

## Open Questions

1. **Who is a custodian?**
   - What we know: D-28 says custodian = sender, and "All custodians" = every mailbox holding a copy. The guide has no `IsCustodian` flag.
   - Unclear: are outside-counsel and adverse-party mailboxes "collected"? Real productions come from one side.
   - Recommendation: all cast members are custodians (a pooled demo collection); externals never. Confirm during plan review; it also fixes Phase 5's mailbox folders.
2. **External sender domains (newsletters, spam, personal).**
   - What we know: PITFALLS security recommends synthetic or reserved domains. Realism wants `gmail.com`-like personal mail.
   - Recommendation: code-generated fictional publication domains from the LLM-seeded `senderName` (`{slug}.com`, checked against case org domains). Personal mail uses a short fixed list of common consumer domains (no mail is ever sent). Flag for the user if they prefer `.example`.
3. **D-12 literal hosting** (A13) and **protocol timing** (A14): low risk, planner's call.
4. **DocID prefix editability** (A6): add an optional "DocID prefix" box under Advanced (2-6 letters) to absorb derivation surprises. Cheap.

## Environment Availability

| Dependency | Required By | Available | Version | Fallback |
|------------|------------|-----------|---------|----------|
| .NET SDK | build/test | ✓ | 10.0.401 (also 9.0.103) | — |
| ICU (Windows built-in) for IANA zones | Scheduler | ✓ | Windows 11 26200 | `UsTimeZones` fallback → UTC |
| DocumentFormat.OpenXml | key/protocol | ✓ | 3.5.1 (package ref) | — |
| Display at 120-144 DPI | render tests | (dev machine; harness throws below 120) | — | Run render tests on a 150% display |
| LLM API keys | live planning only | user-supplied | — | All tests use `FakeLlmProvider` |

No missing blocking dependencies. Note: do not enable `InvariantGlobalization` in the csproj (it would break IANA lookups); it is not set today [VERIFIED: csproj].

## Validation Architecture

### Test Framework
| Property | Value |
|----------|-------|
| Framework | xUnit 2.9.3 + Microsoft.NET.Test.Sdk 17.14.1 (net10.0-windows) |
| Config file | `ReelDiscovery.Tests/ReelDiscovery.Tests.csproj` (render tests in non-parallel `RenderCollection`) |
| Quick run command | `dotnet test ReelDiscovery.Tests/ReelDiscovery.Tests.csproj --filter "FullyQualifiedName~ReelDiscovery.Tests.Corpus&Category!=Render"` |
| Full suite command | `dotnet test ReelDiscovery.Tests/ReelDiscovery.Tests.csproj --filter "Category!=Render"` |
| Render check | `dotnet test ReelDiscovery.Tests/ReelDiscovery.Tests.csproj --filter "Category=Render"` → PNGs + `.clip.txt` under `ReelDiscovery.Tests/bin/Debug/net10.0-windows/renders/` (open and inspect at 144 DPI, D-03) |

### Phase Requirements → Test Map
| Req ID | Behavior | Test Type | Automated Command (filter) | File Exists? |
|--------|----------|-----------|-------------------|-------------|
| PLAN-01 | Preset defaults by complexity; clamp 50-5,000; split math shared with allocator; low-relevant warning at < dated facts; window default ±1/+2 months and clamp around facts; media gating without OpenAI key; settings round-trip + Normalize | unit | `FullyQualifiedName~CorpusSettingsTests` | ❌ Wave 0 |
| PLAN-02 | Every message has SentAt/From/To/Kind/ReplyToSeq; LLM fills only text; unknown ids dropped; missing threads re-asked once then templated; cancel discards (draft unchanged); fatal stops all chunks; refusal handling | unit (FakeLlmProvider) | `FullyQualifiedName~CorpusPlannerTests` | ❌ Wave 0 |
| PLAN-02 | Plan file round-trip, PlanId mismatch → no plan, size cap, Normalize garbage, format version 2 | unit | `FullyQualifiedName~CorpusPlanStoreTests` | ❌ Wave 0 |
| PLAN-03 | Sum of messages = R exactly; every fact ≥1 thread; Hot ≥ Key ≥ Supporting mean threads; multi-issue threads exist when concurrent facts share actors; chunk prompt for a window contains facts of ≥2 issues; carrying messages on/after fact date | unit | `FullyQualifiedName~BudgetAllocatorTests\|FullyQualifiedName~ThreadSkeletonTests` | ❌ Wave 0 |
| PLAN-04 | Holiday vectors (2021-06-18, 2021-12-31, 2022-12-26, 2023-11-10, 2026-07-03, 2027-06-18); no weekend/holiday/after-hours times; offsets correct across DST (Mar/Nov weeks, Phoenix no DST); latency monotonic; invalid TimeZoneId falls back | unit | `FullyQualifiedName~BusinessCalendarTests` | ❌ Wave 0 |
| PLAN-05 | Over 10K seeded draws: depth/kind/CC/attachment frequencies within ±3 points of the table; reply-all recipient rules; forward adds a new eligible person; only enabled attachment types | unit | `FullyQualifiedName~ThreadShapesTests` | ❌ Wave 0 |
| PLAN-06 | Each of the ~19 rules fires on a crafted bad plan and is silent on a good plan; never throws on null/garbage; all warnings (no errors) with fix hints | unit | `FullyQualifiedName~CorpusPlanValidatorTests` | ❌ Wave 0 |
| PLAN-07 | Summary counts by issue/fact/custodian/relevance; month buckets cover window; cost estimate math; stale detection vs. hash; dismiss; approval withdrawn when stale | unit | `FullyQualifiedName~PlanSummaryTests\|FullyQualifiedName~ReviewPlanLogicTests` | ❌ Wave 0 |
| PLAN-07 | Corpus Settings, Review Plan (planned / progress / empty / stale banner), Re-plan dialog at Large and Small sizes with no clipping | render | `Category=Render&FullyQualifiedName~CorpusRenderTests` | ❌ Wave 0 |
| KEY-01 | Row count = messages + attachments; CSV and XLSX contain the same rows | unit | `FullyQualifiedName~AnswerKeyBuilderTests` | ❌ Wave 0 |
| KEY-02 | Exact column order and headers; DocIDs chronological, attachments `.0001`; custodian rules; paths opaque and valid | unit | `FullyQualifiedName~AnswerKeyBuilderTests` | ❌ Wave 0 |
| KEY-03 | Attachments and Quoted replies Relevant with inheritance rationale; non-relevant threads contain no relevant rows; issue/fact codes propagated to family | unit | `FullyQualifiedName~AnswerKeyBuilderTests` | ❌ Wave 0 |
| KEY-01 | CSV: BOM, CRLF, quotes/commas/newlines escaped, formula-leading text prefixed with `'` | unit | `FullyQualifiedName~AnswerKeyCsvWriterTests` | ❌ Wave 0 |
| KEY-04 | XLSX: 3 sheets named exactly; `OpenXmlValidator` 0 errors; frozen pane A2; autofilter covers data; Statistics totals equal row counts; numeric cells; control chars stripped | unit | `FullyQualifiedName~AnswerKeyXlsxWriterTests` | ❌ Wave 0 |
| KEY-05 | Docx validates; contains every issue code + RelevanceCriteria verbatim + counsel names; contains no DocID pattern or counts; prompt contains no plan data or "fictional" wording; fallback prose on LLM failure | unit | `FullyQualifiedName~ReviewProtocolTests` | ❌ Wave 0 |
| (cross) | New DTOs strict and ≤16 unions | unit | `FullyQualifiedName~CaseSchemaTests` (extend theory data) | ✅ (extend) |
| (cross) | Wizard order and stage mapping (CorpusSettings, ReviewPlan before Summary) | unit | `FullyQualifiedName~WizardFlowTests` (extend) | ✅ (extend) |
| (cross) | Guide content hash ignores UI state | unit | `FullyQualifiedName~CaseGuideModelTests` (extend) | ✅ (extend) |
| PLAT-03 | All of the above exist and pass | suite | full suite command | — |

### Sampling Rate
- **Per task commit:** quick run command (Corpus namespace, non-render), < 30 s.
- **Per wave merge:** full suite (`Category!=Render`), then `Category=Render` for UI waves.
- **Phase gate:** full suite + render suite green, PNGs inspected at 144 DPI, before `/gsd-verify-work`.

### Wave 0 Gaps
- [ ] `ReelDiscovery.Tests/Corpus/CorpusTestData.cs` — a richer fixture built on `SampleGuidedDraft`: ≥2 issues, orgs in two time zones (e.g. America/Chicago + America/Phoenix), a fact on a Saturday and one on a holiday, a cast member with a narrow active range, a single-knower fact, counsel on both sides, Hot/Key/Supporting mix.
- [ ] `FakeLlmProvider`: lock + `Func<LlmProviderRequest, LlmProviderResponse>? Respond` for parallel chunk tests (or an injectable max-parallel = 1 in the planner).
- [ ] A helper that builds a valid `ThreadPlanBatchDto` JSON echoing the thread IDs/seqs found in a prompt.
- [ ] Test files listed in the map above.
- [ ] Render test class `ReelDiscovery.Tests/Render/CorpusRenderTests.cs` in `RenderCollection`.

## Security Domain

Security enforcement is enabled (ASVS L1, block on high).

### Applicable ASVS Categories
| ASVS Category | Applies | Standard Control |
|---------------|---------|-----------------|
| V2 Authentication | no | — (local app; API keys handled in Phase 1 via DPAPI) |
| V3 Session Management | no | — |
| V4 Access Control | no | — |
| V5 Input Validation | yes | `case.json` and `corpus-plan.json` are untrusted: size caps, `Normalize()` (null lists, clamps, mix renormalization, enum names via string converter), `TryParseExact` dates, time-zone ids resolved in try/catch, `PlanId` match. LLM output only through strict DTOs; ids canonicalized and checked against known sets |
| V5 Output Encoding | yes | CSV: RFC 4180 quoting + OWASP formula guard. XLSX: string cells only, never formulas. XML-invalid chars stripped for OpenXml |
| V6 Cryptography | no (hash only) | SHA-256 via `CaseDraft.ComputeHash` for staleness (not a security control) |
| V12 Files and Resources | yes | Planned relative paths built only from sanitized parts (no `..`, not rooted, reserved device names avoided, `Path.GetInvalidFileNameChars` removed); LLM-proposed attachment names via `FileNameHelper`; all writes inside the case folder via `CaseStore.WriteAtomicAsync`; opening files only for app-written paths in the case folder (existing `UseShellExecute` pattern in `StepCaseSummary`) |

### Known Threat Patterns for this stack
| Pattern | STRIDE | Standard Mitigation |
|---------|--------|---------------------|
| CSV/spreadsheet formula injection from LLM text or a tampered case.json (`=HYPERLINK(...)`, `=cmd|...`) | Tampering / Elevation | Leading `= + - @ \t \r \n` (and full-width) prefixed with `'` and quoted; XLSX inline strings only [CITED: OWASP] |
| Path traversal via a crafted custodian name/email local part or attachment file name in `corpus-plan.json` | Tampering | Sanitize every segment; recompute paths from ids rather than trusting stored `FilePath` when writing files in later phases; assert `Path.GetFullPath(combined).StartsWith(caseFolder)` |
| Oversized/garbage plan file (DoS on load) | DoS | Size cap before read; `Normalize`; reject on `JsonException` with a user-facing message (mirrors `CaseStore.Load`) |
| Prompt injection via guide text (user-authored) into planning prompts | Tampering | Low impact (the user only affects their own output); scenario/guide content stays in the user prompt; strict schema bounds output; code ignores structure in text |
| Label leakage into the protocol | Information disclosure (of the key) | Protocol prompt receives no plan data; test for DocID patterns and counts |
| Malformed XML chars → corrupt Office files | DoS | XML-char filter + `OpenXmlValidator` tests |

## Sources

### Primary (HIGH confidence)
- Codebase (read this session): `Models/Case/*.cs`, `Services/Case/{CaseGuideGenerator,CaseGuidePrompts,CaseGuideValidator,GuideStaleness,CaseStore,IdAllocator,UsTimeZones,CaseGuideDocxExporter,CaseFolderNaming}.cs`, `Services/Ai/{ILlmGateway,LlmGateway,LlmRequest,LlmResult,JsonSchemaRegistry,AnthropicTextProvider,OpenAITextProvider}.cs`, `Services/OfficeDocumentService.cs`, `Models/{AIModelConfig,TokenUsageTracker,Attachment,WizardState}.cs`, `Forms/WizardForm.cs`, `UserControls/{StepCaseGuide.Run,StepCaseSummary}.cs`, `ReelDiscovery.Tests/{Fakes/FakeLlmProvider,Case/CaseTestData,Render/RenderHarness,Case/CaseGuideModelTests}.cs`, csproj files
- Scratch verification runs (this session): IANA `TimeZoneInfo` lookups and DST offsets on .NET 10/Windows 11; OpenXml 3.5.1 three-sheet workbook with frozen panes/autofilter/defined names → `OpenXmlValidator` 0 errors
- OPM federal holidays and weekend observance: https://www.opm.gov/policy-data-oversight/pay-leave/federal-holidays/
- OWASP CSV Injection: https://community.owasp.org/attacks/CSV_Injection
- `.planning/research/ARCHITECTURE.md`, `PITFALLS.md`, `FEATURES.md`; `.planning/phases/02-case-scenario-core-document/02-RESEARCH.md` (strict-schema limits, 21,333 cap)

### Secondary (MEDIUM confidence)
- Klimt & Yang, "The Enron Corpus: A New Dataset for Email Classification Research", ECML 2004 — thread statistics extracted from the PDF: https://bklimt.com/papers/2004_klimt_ecml.pdf
- Yang et al., "Characterizing and Predicting Enterprise Email Reply Behavior", SIGIR 2017 (Avocado corpus) — reply-time tertiles, internal/external medians, 53% non-dyadic: https://www.microsoft.com/en-us/research/wp-content/uploads/2017/04/sigir17a.pdf
- Kooti et al., "Evolution of Conversations in the Age of Email Overload", 2015 — median reply 47 min, >90% within a day: https://arxiv.org/pdf/1504.00704
- Load-file family fields (BEGATTACH/ENDATTACH, PARENTID/ATTACHIDS): https://www.veritas.com/support/en_US/doc/Load_File_Import_Guide_10.1 ; https://www.lexbe.com/wp-content/uploads/2014/08/Lexbe-Standard-Metadata-Processing-Load-File-Fields.pdf
- Review protocol structure: https://calawyers.org/law-practice-management-technology/considerations-when-drafting-a-document-review-protocols-memorandum-for-remote-document-reviewers/ ; https://www.everlaw.com/guides/the-everlaw-guide-to-ediscovery/document-review/

### Tertiary (LOW confidence)
- Distribution fits, cost/time constants, attachment type mix, privileged/hot shares (marked [ASSUMED] above)

## Metadata

**Confidence breakdown:**
- Standard stack: HIGH — no new packages; key mechanics verified by running code.
- Architecture: HIGH — directly derived from locked decisions and existing in-repo patterns.
- Realism parameters: MEDIUM — anchored to Enron/Avocado/Yahoo statistics, adapted by judgment.
- Pitfalls: HIGH — several found by reading the actual code (hash scope, IdAllocator side effect, fake provider thread safety, stage ordinals).
- Cost/time estimates: LOW-MEDIUM — heuristic, labeled rough.

**Research date:** 2026-10-02
**Valid until:** 2026-11-01 (stable domain; re-check model prices in `AIModelConfig` if the catalog changes)
