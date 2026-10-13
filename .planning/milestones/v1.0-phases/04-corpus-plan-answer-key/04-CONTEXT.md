# Phase 4: Corpus Plan & Answer Key - Context

**Gathered:** 2026-10-02
**Status:** Ready for planning

<domain>
## Phase Boundary

Before any prose is written, **code plans the whole corpus** (a manifest of every thread and message) against the Phase 3 case guide:
- the user sets corpus size, the relevant/non-relevant ratio, the date window, the non-relevant mix and the attachment media types;
- a planner fixes every message's date/time, sender, To/CC, reply/forward structure, linked facts/issues, relevance label, flags (hot / privileged / near-miss) and attachment specs;
- a deterministic validator checks the plan;
- a **Review Plan** screen shows the summary and lets the user approve or re-plan;
- on approval, the **answer key (CSV + Excel)** and a **review protocol / coding manual (.docx)** are written from the plan alone.

Requirements: PLAN-01..07, KEY-01..05, PLAT-03 (planner, validator and answer-key tests).

Out of scope: writing email prose or EMLs (Phase 5), attachment content and non-relevant email content (Phase 6), batch execution, cost QA and 5K render runs (Phase 7). Phase 4 only *plans* non-relevant slots and attachment specs.

</domain>

<decisions>
## Implementation Decisions

### Carried forward (locked in Phases 1-3)
- **D-01:** All LLM work goes through `ILlmGateway` on the **Story** tier with strict JSON-schema DTOs, and works with Claude or OpenAI. The strict-schema limits from Phase 2 research apply (≤16 nullable fields, no recursion, dates as `yyyy-MM-dd` strings, provider max-output caps → chunk the work).
- **D-02:** Names are kept exactly as the guide has them, **including names from fictional works**. Prompts must never tell the model that names must be fictional. Content takes a firm position and does not hedge.
- **D-03:** UI uses the Phase 2/3 theme (`Theme`, `ThemedButton` via `ButtonHelper`, `CardPanel`, `SlimProgressBar`, segmented controls) with content-sized rows and logical-px sizing. **Every new screen and dialog must be rendered at 144 DPI (150% scaling) with the existing render-test harness and checked for clipping before hand-off.** Visual quality is a hard requirement.
- **D-04:** Autosave: corpus settings, the plan and its approval state persist with the case (D-10 of Phase 3). Reopening a case resumes at the furthest corpus step reached. Add `CaseStage` values for the new steps; they map to step types, never indexes.
- **D-05:** The guide is the single source of truth. The plan stores the **`CaseGuide.ComputeHash()`** it was built from (stale detection, see D-25).

### Corpus settings (new step "Corpus Settings", after Case Guide)
- **D-06:** **Size:** quick-pick presets **250 / 1,000 / 2,500 / 5,000** plus a custom number box (**50-5,000**). The default follows the complexity preset (Simple 250, Standard 1,000, Complex 2,500).
- **D-07:** **Relevant ratio:** a **% relevant slider** (default ~20%) with **live counts** ("≈200 relevant / 800 non-relevant emails"). It warns when the relevant count is too low for every key fact to get at least one thread.
- **D-08:** **Non-relevant mix:** a sensible default split of about **50% custodians' daily business / 20% ancillary-staff traffic / 20% newsletters-spam-personal / 10% near-miss decoys**, editable under an **"Advanced" expander**. The plan reserves slots per category.
- **D-09:** **Hot docs, privileged threads and attachment rate are automatic:** hot documents follow the facts with `Significance = Hot`, privileged threads follow the counsel cast (in-house + outside counsel), and the attachment rate gets a realistic default. Their counts show on the plan summary. Overrides live in the same Advanced expander.
- **D-10:** **Date window:** defaults to the first through last key fact, plus **~1 month of lead-in and ~2 months of tail** for non-relevant traffic. Start and end are editable, but clamped so every dated key fact stays inside.
- **D-11:** **Media type toggles:** checkboxes for Office documents / calendar invites / images / voicemail. Images and voicemail are disabled with a notice when no OpenAI key is configured (Phase 1 rule). The plan assigns attachment specs only for enabled types.
- **D-12:** Wizard order becomes **Case Guide → Corpus Settings → Review Plan → Case Summary**. Next on Corpus Settings runs planning.

### How planning works
- **D-13:** **Relevant threads use a code skeleton plus LLM thread plans.** Code allocates thread and message counts per fact/issue, picks participants (from `ActorIds` / `KnownByIds`, only cast active on that date), fixes dates/times and reply/reply-all/forward structure, and assigns labels and flags. The LLM, called in **chunks by time window across all issues** (so overlapping issues among shared characters are visible together), writes each thread's subject, per-message intent, what each message must convey, links between threads, and the one-sentence relevance rationale. **Code owns every date, person, count and label.** LLM output that names unknown people or ids is mapped or dropped by code.
- **D-14:** **Non-relevant slots:** code decides category, custodian, participants (key and background cast, plus external senders for newsletters/spam), dates, thread shape and attachment specs, with no case facts in context. A **bulk LLM pass in Phase 4 seeds subjects (and decoy angles)** so the plan summary and answer key show real subjects for every thread. Decoy seeds share project names and keywords with the case but must not be about the alleged conduct: give the decoy prompt the relevant facts as a must-not-mention list.
- **D-15:** **Volume is weighted by significance.** Every key fact gets at least one relevant thread. Hot and Key facts get more threads and messages, Supporting facts fewer. Periods with many facts close together get denser traffic. Some threads deliberately touch two concurrent issues.
- **D-16:** **Timestamps use simple business hours plus latency and holidays:** weekdays only, 9am-5pm in the **sender's organization time zone** (`CaseOrganization.TimeZoneId`), no evening or weekend mail, **US federal holidays skipped**, and realistic **reply latency** (minutes to a day or two, monotonic within a thread). This satisfies PLAN-04 under the user's preference for simplicity.
- **D-17:** Thread depth, reply vs. reply-all vs. forward, CC patterns and attachment rate follow realistic distributions (PLAN-05). Long sagas split into several threads rather than one huge thread. Exact distribution parameters are Claude's discretion.
- **D-18:** **Progress:** inline stage label (Allocating → Planning relevant threads (chunk n/N) → Planning other email → Validating), progress bar and **Cancel**, the same pattern as Case Guide generation, with refusal/fatal/timeout handling. **A cancelled or failed plan is discarded** (no partial-chunk resume). Settings are kept.
- **D-19:** **Repair:** the planner auto-repairs what code can fix (moves dates into active ranges or business hours, swaps an inactive participant), re-asks the LLM **once** for an offending chunk, and reports anything still wrong in the validator Checks.
- **D-20:** **No reproducibility seed** is needed. Use `Random.Shared` (or a local Random) and don't store a seed.
- **D-21:** **Cost:** planning calls show in the existing live cost ticker. The Review Plan screen also shows a **rough estimate of the cost of generating the corpus** on the selected Story model (from planned message counts × typical tokens × model pricing in `AIModelConfig`).

### Review Plan screen (new step "Review Plan")
- **D-22:** **Summary only, with no per-thread drill-in.** Stat tiles (emails, threads, relevant %, hot, privileged, attachments, estimated generation cost), a **month-by-month date histogram** stacked relevant vs. non-relevant, **three compact sortable tables (by issue / by key fact / by custodian, with relevant and non-relevant counts)**, and the **Checks** list from the validator. Same card visual language as Case Guide.
- **D-23:** **The plan is read-only.** Changes come from **Re-plan**, which opens a small dialog with an optional guidance box (e.g. "more traffic around the March change-order dispute"), confirms, and replaces the whole plan. Going Back to Corpus Settings changes the numbers.
- **D-24:** **Validator results are warnings only. Nothing blocks Approve.** Checks still carry clear fix hints (Phase 3 style), but no error tier blocks approval.
- **D-25:** **Stale plan:** if the guide hash no longer matches the plan's basis, Review Plan shows an **"out of date with the case guide" banner with a Re-plan action** and approval is withdrawn until the user re-plans or dismisses it. Nothing re-plans silently (mirrors Phase 3 D-20).
- **D-26:** **Approve** locks the plan in the case and **immediately writes `answer-key.csv`, `answer-key.xlsx` and the review protocol `.docx` into the case folder**. Case Summary lists them and can open them. Re-planning after approval overwrites them after a confirm.

### Answer key
- **D-27:** **Relevance is binary (Relevant / Not Relevant) plus flags:** separate columns for Hot (Y/N), Privileged (Y/N), Near-miss decoy (Y/N), and Non-relevant category (daily business / ancillary / newsletter-spam-personal / decoy).
- **D-28:** **One row per unique document** (each email message and each attachment). Custodian = the sender (owner of the sent copy), and an **"All custodians"** column lists every mailbox that will hold a copy. Row counts match the plan, not the mailbox duplication.
- **D-29:** **Planned paths plus a Status column.** DocIDs and deterministic planned relative file paths are fixed now. `Status = Planned` in Phase 4. Later phases regenerate the key from plan + run journal to mark Generated / Failed / Skipped.
- **D-30:** **DocID = case prefix + number**, e.g. `DM-000001`. Attachments are `DM-000001.0001` (parent id visible = family). The prefix comes from the case title. DocIDs are stable for a given plan, and re-plan issues new ones. Phase 5 stamps the DocID into the `X-ReelDiscovery-DocID` header.
- **D-31:** **Rationale comes from the plan.** For relevant threads, the LLM writes it in the thread plan, tied to the fact/issue. Non-relevant rows use a code template by category. Attachments and quoted replies **inherit family relevance** (KEY-03), and their rationale notes the inheritance.
- **D-32:** Required columns (KEY-02): DocID, file path, custodian, all custodians, date, from, to, cc, subject, thread ID, family/parent ID, relevance, issue codes, linked fact IDs, hot, privileged, near-miss, non-relevant category, rationale, status.
- **D-33:** **Excel workbook = three sheets:** "Answer Key" (frozen header, filters, sized columns), "Statistics" (counts by custodian, issue, relevance and month: KEY-04), and "Legend" (issue codes with titles, fact IDs with titles, column definitions).

### Review protocol
- **D-34:** **A realistic reviewer manual (.docx)** in the style of a real document-review protocol: matter background, parties and key people, issue codes with relevance criteria (from `CaseIssue.RelevanceCriteria`) and illustrative examples, hot-document and privilege guidance (counsel names and law firms), coding fields and decision rules, and family rules. Code builds the structure from the guide, and **one Story-tier LLM pass writes the prose sections**. It must **not** reveal the answer key or per-document labels.

### Claude's Discretion
- Plan data model and storage (inside `case.json` vs a sibling plan file in the case folder for size at 5K), as long as it autosaves and reopens with the case.
- Chunk size, the number of neighbor facts per planning chunk, and DTO decomposition within the strict-schema limits.
- Exact distribution parameters (thread depth, CC rates, reply/forward mix, latency curve, default attachment rate), histogram rendering approach, and stat-tile set details.
- Exact validator rule set beyond PLAN-06's minimum (counts, ratio, active ranges, every key fact evidenced). Also: dates within window and business hours, monotonic thread dates, entity refs resolve, labels consistent with fact links, attachment specs well-formed.
- DocID prefix derivation (e.g. initials of the first party or case title), file-path naming scheme, and the generation-cost estimate heuristic.
- The US federal holiday table (computed rules, including observed dates).

### Resolved during planning (auto chain — research recommendations accepted; user may revisit)
- **Custodians:** every cast member (key and background) is a custodian; external senders (newsletters, spam, personal contacts) never are (RESEARCH A7).
- **External sender domains:** invented publication/vendor domains plus a few common consumer domains for personal mail.
- **Where planning runs:** Next on Corpus Settings advances to Review Plan, which starts the planning run automatically on entry when no current plan exists (still satisfies D-12; RESEARCH A13). Review protocol prose is generated as the last planning stage, so Approve is a pure file write (A14).
- **Answer-key extras:** Doc Type, File Name and a separate Parent DocID column are allowed in addition to D-32 (A15). DocID prefix is derived automatically and editable under Advanced (A6).
- **Guide hash:** `CaseGuide.ComputeHash()` becomes a content-only projection (excludes UI/state fields such as `DismissedStaleKey`, generation timestamps, review flags) so UI actions don't stale the plan (A16).
- **Plan storage:** plan lives in a sibling `corpus-plan.json` in the case folder; `case.json` keeps settings, plan id/hash and approval state; `CaseDraft.CurrentFormatVersion` → 2.

</decisions>

<canonical_refs>
## Canonical References

**Downstream agents MUST read these before planning or implementing.**

### Requirements & roadmap
- `.planning/REQUIREMENTS.md` — PLAN-01..07, KEY-01..05, PLAT-03 (Phase 4), and the Phase 5/6 requirements the plan must feed (CORP-*, NOISE-*, ATT-*, OUT-01/02)
- `.planning/ROADMAP.md` §Phase 4 — goal and the 5 success criteria. Phase 5/6 goals show what the plan must support downstream.
- `.planning/PROJECT.md` — core value. Key Decisions: "Code-owned plan (manifest) before prose; answer key derived from plan".

### Research (planner design)
- `.planning/research/ARCHITECTURE.md` — CorpusPlanner decomposition (BudgetAllocator → RelevantThreadPlanner chunked by time window → DecoyPlanner with MustNotMention → FillerPlanner), ManifestValidator rules, ThreadPlan/MessagePlan shape, answer-key derivation, the "planner owns structure, renderer owns prose" anti-pattern notes. Adapt to the codebase's conventions (mutable classes, no records).
- `.planning/research/PITFALLS.md` — label leakage, timeline drift, and scale pitfalls relevant to plan design
- `.planning/research/FEATURES.md` — answer key / review protocol expectations from the eDiscovery domain

### Prior phase outputs
- `.planning/phases/03-case-guide/03-CONTEXT.md` — guide decisions (cast tiers/roles, counsel, KnownByIds, staleness pattern D-20, autosave D-10, Case Summary D-12)
- `.planning/phases/03-case-guide/03-UI-SPEC.md` — screen conventions and the card/segmented-control language to reuse
- `.planning/phases/03-case-guide/deferred-items.md` — known render-harness artifacts and window-size notes (WizardForm opens at a 1150x900 logical client)
- `.planning/phases/02-case-scenario-core-document/02-RESEARCH.md` — gateway usage, strict-schema limits, token caps, chunked generation pattern

### Code
- `Models/Case/CaseGuide.cs`, `KeyFact.cs` (Significance, ActorIds, KnownByIds), `CastMember.cs` (Tier, Role, ActiveFrom/To), `CaseOrganization.cs` (TimeZoneId, Kind LawFirm), `CaseIssue.cs` (RelevanceCriteria), `CanonicalValue.cs`, `ComplexityPreset.cs`, `CaseStage.cs`, `CaseDraft.cs`
- `Services/Case/CaseGuideGenerator.cs`, `CaseGuidePrompts.cs`, `CaseGuideValidator.cs`, `CaseGuideQuickFix.cs`, `GuideStaleness.cs`, `CaseStore.cs`, `IdAllocator.cs`, `UsTimeZones.cs`, `CaseGuideDocxExporter.cs`
- `UserControls/StepCaseGuide.cs` + `StepCaseGuide.Run.cs` (inline generation with progress/cancel), `StepCaseSummary.cs`, `Forms/WizardForm.cs` (`StepTypesFor`, stage→step mapping)
- `Services/OfficeDocumentService.cs` — `CreateWordDocument`, `CreateExcelDocument` (extend for multi-sheet, frozen header and filters)
- `Services/Ai/ILlmGateway.cs`, `Models/AIModelConfig.cs` (pricing for the cost estimate), `Models/TokenUsageTracker.cs`

</canonical_refs>

<code_context>
## Existing Code Insights

### Reusable Assets
- `ILlmGateway.GenerateAsync<T>` with Story tier, strict DTOs and outcome classification, the same calling pattern as `CaseGuideGenerator` (chunked, cancellable).
- `CaseGuideValidator` + `CaseGuideQuickFix`: the model for a deterministic, never-throwing checks list with fix hints (here warnings only).
- `GuideStaleness` + `GuidePartState.BasisHash`: the model for plan-vs-guide staleness. `CaseGuide.ComputeHash()` was added for this phase.
- `UsTimeZones` + `CaseOrganization.TimeZoneId`: per-sender time zones.
- `IdAllocator`: the code-owned stable ID pattern (reuse it for thread/message/DocIDs).
- `CaseGuideDocxExporter` / `OfficeDocumentService`: .docx output for the protocol, and the Excel builder for the key.
- `CaseStore` (autosave, case folder), `CaseTestData` + `FakeLlmProvider` (test scaffolding without a live provider), 144-DPI render-test harness in `ReelDiscovery.Tests`.

### Established Patterns
- UI is built in code with TableLayoutPanel roots. Wire handlers after all controls exist (init-order null refs have bitten twice).
- Pure static helpers are unit-tested, and UI is verified with 144-DPI renders plus the clipping detector.
- Strict DTOs are nested internal classes with `[JsonPropertyName]` + `[Description]`. Dates are `yyyy-MM-dd` strings parsed with `TryParseExact` + InvariantCulture.
- `Normalize()` repairs untrusted `case.json` on load. Any new persisted plan model needs the same.

### Integration Points
- `WizardForm.StepTypesFor(WizardFlow.CaseDriven)`: insert `StepCorpusSettings` and `StepReviewPlan` between `StepCaseGuide` and `StepCaseSummary`. Extend the `CaseStage` → step mapping.
- `StepCaseSummary`: list and open the answer key CSV/XLSX and the protocol .docx.
- `CaseDraft`: holds corpus settings and the plan (or a reference to a plan file) and autosaves via `CaseStore`.

</code_context>

<specifics>
## Specific Ideas

- The user's real test case is "inspired by The Office" (Dunder Mifflin, Michael Scott, etc.), so a DocID like `DM-000001` and addresses like `michael.scott@dundermifflin.com` are the expected look.
- Live counts beside the ratio slider ("≈200 relevant / 800 non-relevant") are the expected interaction.
- Example re-plan guidance: "more traffic around the March change-order dispute".

</specifics>

<deferred>
## Deferred Ideas

- Per-thread drill-in, editing, or "re-plan this thread" on Review Plan: the user chose summary-only + whole-plan re-plan for v1.
- Evening/weekend mail, executive or crunch-period patterns: the user chose simple business hours. Could be revisited as a realism option later.
- Resuming a cancelled plan from completed chunks: the plan is discarded on cancel for now.

</deferred>

---

*Phase: 04-corpus-plan-answer-key*
*Context gathered: 2026-10-02*
