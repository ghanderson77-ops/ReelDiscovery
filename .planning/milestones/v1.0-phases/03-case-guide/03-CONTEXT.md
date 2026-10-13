# Phase 3: Case Guide - Context

**Gathered:** 2026-10-01
**Status:** Ready for planning

<domain>
## Phase Boundary

Turn the reviewed core document (Complaint / Petition / Investigation Memo from Phase 2) into a structured, validated, ID-stable **case guide**:
- an **issue list** (from the counts / findings),
- a **Cast of Key Characters** (incl. in-house and outside counsel, plus background staff per organization),
- a dated **Key Facts & Events timeline** (each fact linked to issues, actors and who knew),
- a **registry of canonical values** (amounts, invoice/change-order numbers, document titles, project names),

checked by a deterministic validator, editable and regenerable, saved as `case.json` in a per-case folder, reopenable, and exportable to Word (.docx). Requirements GUIDE-01..GUIDE-11.

Out of scope here: corpus planning, answer key, email generation (Phases 4-5).

</domain>

<decisions>
## Implementation Decisions

### Carried forward from Phase 2 UAT (locked, user feedback 2026-10-01)
- **D-01:** Names the scenario gives are kept exactly, **including names from fictional works** (TV, film, books, games — e.g. Dunder Mifflin, Michael Scott). Only real-world people/companies/brands are substituted (per the Phase 2 real-name dialog decisions stored on `CaseDraft.Substitutions`). Every Phase 3 generation prompt (cast, facts, values) must say this explicitly; never instruct the model that "every person/company must be fictional". Invent names only for people/orgs the core document does not name.
- **D-02:** Generated content takes a **firm position**: facts are stated as what happened (who did what, who knew what and when, where the money went), not hedged.
- **D-03:** All guide generation uses the **Story** tier via `ILlmGateway` (strict JSON-schema DTOs), works with Claude or OpenAI.
- **D-04:** UI uses the Phase 2 theme: `Helpers/Theme`, `ThemedButton` (via `ButtonHelper.CreateButton`), `CardPanel`, `SlimProgressBar`; sizes in logical px via `LogicalToDeviceUnits`; content-sized rows. The user runs Windows at ~150% scaling — every new screen must be rendered at 144 DPI and inspected before hand-off (see `ReelDiscovery.Tests` harness approach used in Phase 2: STA thread, `SetProcessDpiAwarenessContext(-4)`, `DrawToBitmap`). The user has rejected unaligned / stock-looking UI twice; visual quality is a hard requirement.

### Guide screen layout
- **D-05:** **One wizard step, "Case Guide"**, after Review Core Document, with **tabs: Issues / Cast / Key Facts / Canonical Values**. Use the same card visual language as the Review Core Document screen (tabs may be a themed segmented control like that screen's Section/Full document toggle rather than a stock TabControl).
- **D-06:** On entering the step with no guide yet, generation runs **automatically in order: issues → cast → facts → canonical values**; each tab fills as its part finishes, with inline progress and Cancel (same pattern as core document generation: partial results kept, refusal/fatal/timeout handling).
- **D-07:** Key Facts shown as a **sortable table** (date, fact, issues, actors, who knew) **+ detail panel** for the selected fact.
- **D-08:** **Issues are extracted from the core document's counts (pleadings) or findings (memo)** with stable IDs; the Issues tab is **read-only** (change issues by editing the core document).

### Saving & reopening
- **D-09:** **Per-case folder, auto-named**: default `Documents\ReelDiscovery\Cases\<case title>` (sanitized, unique), created when the case is first saved (when the core document exists); user can change the location. `case.json`, Word exports, and later the corpus all live there.
- **D-10:** **Autosave**: `case.json` is written after every generation step and every edit (core document edits included). No Save button. A crash/close never loses paid-for work. (This replaces Phase 2's "Close Case discards the case" confirmation — closing no longer discards; update/remove that C6 prompt accordingly.)
- **D-11:** **Reopen**: an **"Open Case..." button plus a short recent-cases list on the Describe the Case step**; opening loads `case.json` and jumps to the furthest step the case has reached.
- **D-12:** **End of flow (until Phases 4-5 exist)**: the Case Guide step's Next leads to a **Case Summary** step showing what was saved where, with buttons to **export the core document, cast and key-facts summary to Word (.docx)** and **open the case folder**; Finish closes. Phase 4 later inserts corpus steps before the summary.

### Cast shape & email identities
- **D-13:** **Cast size is preset-driven**: key characters follow the complexity preset (`ComplexityPreset.CharactersMin/Max`, e.g. Standard 12-20), plus **background (ancillary) staff per organization scaled by preset (~3 / 6 / 10 per org for Simple / Standard / Complex)** for non-relevant traffic later. Key vs background is a field on each character.
- **D-14:** **Realistic .com-style email identities**: `first.last@<orgdomain>.com` style, domain derived from each organization's name, consistent per org (one domain per organization; law firms get their own). No reserved `.example` domains. (No mail is ever sent.)
- **D-15:** **Counsel**: each represented organization gets **in-house counsel** and an **outside law firm (at least a partner + an associate)**, so privileged threads / privilege-log material can exist later (GUIDE-03).
- **D-16:** Each **key character carries a short voice/writing-style note (1-2 lines) and a signature block**, for consistent email voice in later phases.
- **D-17:** Required cast fields (GUIDE-02): stable ID, name, title, organization, email address, active date range, summary of key involvement; plus key/background flag, role (party/counsel/staff), voice note, signature (key only).

### Editing, regenerating & staleness
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

</decisions>

<canonical_refs>
## Canonical References

**Downstream agents MUST read these before planning or implementing.**

### Requirements & roadmap
- `.planning/REQUIREMENTS.md` — GUIDE-01..GUIDE-11 (Phase 3), and Phase 4/5 requirements the guide must feed (PLAN-*, CORP-*)
- `.planning/ROADMAP.md` — Phase 3 success criteria; Phase 4/5 goals (the guide is the single source of truth downstream)
- `.planning/PROJECT.md` — core value: every relevant email stays faithful to one consistent case story

### Phase 2 outputs this phase builds on
- `.planning/phases/02-case-scenario-core-document/02-RESEARCH.md` — gateway usage, strict-schema limits (≤16 nullable fields, no recursion, dates as yyyy-MM-dd strings), token caps, sectioned generation pattern
- `.planning/phases/02-case-scenario-core-document/02-UI-SPEC.md` — copy/tone and screen conventions (superseded visually by the Phase 2 theme redesign in code)
- `.planning/phases/02-case-scenario-core-document/02-REVIEW.md` and `02-REVIEW-FIX.md` — pitfalls already hit (edit-loss, init-order null refs, cancel on dispose, regex timeouts)
- `.planning/phases/02-case-scenario-core-document/02-HUMAN-UAT.md` — open UAT items and the UAT gaps (fictional names, DPI clipping)
- `.planning/phases/01-foundation-provider-gateway/01-CONTEXT.md` — provider/tier decisions (D-10..D-15)

### Code
- `Models/Case/CaseDraft.cs`, `Models/Case/CoreDocument.cs`, `Models/Case/ComplexityPreset.cs`, `Models/Case/EntitySubstitution.cs`
- `Services/Case/CoreDocumentGenerator.cs`, `Services/Case/CoreDocumentValidator.cs`, `Services/Case/NameSubstitution.cs`, `Services/Case/CitationScanner.cs`
- `UserControls/StepCoreDocument.cs`, `UserControls/StepCaseScenario.cs`, `Forms/WizardForm.cs`
- `Helpers/Theme.cs`, `Helpers/ThemedButton.cs`, `Helpers/CardPanel.cs`, `Helpers/SlimProgressBar.cs`, `Helpers/ButtonHelper.cs`
- `Services/OfficeDocumentService.cs` — existing .docx builder for exports

</canonical_refs>

<code_context>
## Existing Code Insights

### Reusable Assets
- `ILlmGateway.GenerateAsync<T>` with Story tier, strict schemas, outcome classification (`LlmCallException`, `IsFatal`, refused/truncated) — same calling pattern as `CoreDocumentGenerator`.
- `CaseDraft` + shared `JsonOptions` (string enums) — already JSON-round-trippable for case.json (Phase 2 tests prove it); `Revision` counter and `ComputeSourceHash()` for staleness.
- `ComplexityPreset` — character ranges already defined and fed into core-document prompts.
- `CoreDocumentValidator` — model for a deterministic, never-throwing issue list with severities (Error blocks progress, Warning asks).
- `StepCoreDocument` — model for inline generation with progress/cancel, card layout, list + editor, issues card, autosizing at high DPI.
- `OfficeDocumentService.CreateWordDocument` — .docx output.
- `CaseTestData` + `FakeLlmProvider` — test scaffolding for generator tests without a live provider.

### Established Patterns
- UI built in code, TableLayoutPanel roots, handlers wired after all controls exist (init-order null refs bit Phase 2 twice).
- Pure static helpers on step controls are unit-tested; UI verified by 144-DPI renders.
- Strict DTOs as nested internal classes with `[JsonPropertyName]` + `[Description]`; dates as `yyyy-MM-dd` strings parsed with `TryParseExact` + `InvariantCulture`.

### Integration Points
- `WizardForm.StepTypesFor(WizardFlow.CaseDriven)` — insert `StepCaseGuide` and `StepCaseSummary` after `StepCoreDocument`; `StepCoreDocument.NextButtonText` becomes "Next >".
- `WizardForm.OnFormClosing` "Close Case" discard confirmation — revise for autosave (D-10).
- `StepCaseScenario` — add "Open Case..." + recent cases (D-11).
- Autosave hook after core-document generation/edits in `StepCoreDocument` (D-10).

</code_context>

<specifics>
## Specific Ideas

- The user's real test scenario is "inspired by The Office" (Dunder Mifflin, Michael Scott, Ryan Howard, Oscar Martinez, David Wallace). The cast must keep those names; real companies mentioned (Staples, OfficeMax) follow the substitution decisions.
- Email identities should look believable in a review platform (e.g. `michael.scott@dundermifflin.com`).

</specifics>

<deferred>
## Deferred Ideas

None — discussion stayed within phase scope.

</deferred>

---

*Phase: 03-case-guide*
*Context gathered: 2026-10-01*
