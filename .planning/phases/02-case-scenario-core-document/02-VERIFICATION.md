---
phase: 02-case-scenario-core-document
verified: 2026-10-01T17:30:00Z
status: human_needed
score: 5/5 roadmap success criteria verified in code (44/44 plan must-have truths verified); live UI/LLM behavior awaits human testing
overrides_applied: 0
human_verification:
  - test: "Launch the app, choose Case-driven (new) on API Configuration, enter a scenario, switch Simple / Standard / Complex"
    expected: "Header reads 'Step 2 of 3: Describe the Case'; the preset summary line changes immediately (Simple: 2 parties, 1-2 counts, 6-10 characters, 3-6 month timeline, about 8-12 pages; Complex: 5-7 parties, 5-7 counts, 25-40 characters, 24-48 months, about 40-60 pages); the character counter tracks against 8,000"
    why_human: "WinForms rendering and layout need a desktop session"
  - test: "Enter a scenario naming a real company and a real person (e.g. 'Walter White and Apple Inc.'), click Next"
    expected: "Real Names Found dialog lists each with 'Use fictional name' ticked; untick one -> 'Keep Real Names?' confirm; after Apply, generated document uses substitutes for ticked names and the kept name unchanged; 'Review real names' link reopens decisions"
    why_human: "Requires live Story-tier LLM detection and a modal dialog"
  - test: "Generate a civil construction-defect scenario and a criminal/regulatory scenario (once with Claude, once with OpenAI)"
    expected: "Civil -> Complaint (or Petition for a petition-terminology state court) with caption, PARTIES, JURISDICTION AND VENUE, FACTUAL ALLEGATIONS, numbered consecutive paragraphs, COUNT I.. each opening with an incorporation paragraph, WHEREFORE prayer A., B., ..., signature block, one consistent court/state. Criminal/regulatory -> Investigation Memo with header (TO/FROM/DATE/RE), Executive Summary, Background, Scope, Key Individuals, facts, Findings, Legal Analysis, Recommendations. Party/count/character counts and length roughly track the preset; Issues list shows no case-law citations"
    why_human: "Legal realism and preset adherence of live LLM output cannot be checked headlessly"
  - test: "On Review Core Document: edit a section and Apply Edits; regenerate a different section with guidance; Regenerate All; inject '531 U.S. 98' into a section and click Finish"
    expected: "Edits re-number in Full Document tab and Issues refresh; regenerating a user-edited section asks first; Regenerate All asks first; Finish is blocked with 'Case Citations Found'; remaining warnings require an explicit Yes; closing asks 'Close Case'"
    why_human: "Interactive UI flow"
  - test: "Force a section failure / fatal stop (e.g. invalid key mid-run) and cancel a run; also close the window while the real-name check and while generation run"
    expected: "Completed sections kept; failed section shows Retry Section (writes only that section with guidance); Resume All appears when unwritten sections remain and resumes without a new outline call; empty state explains the cancel/stop; closing cancels in-flight calls without exceptions"
    why_human: "Review-fix items CR-02, WR-05, WR-07, WR-09 changed runtime UI/status logic and were flagged by the fixer as requiring human verification"
---

# Phase 2: Case Scenario & Core Document Verification Report

**Phase Goal:** The user describes a real-life dispute or investigation and gets a realistic, legally structured core document that they can review, edit and regenerate.
**Verified:** 2026-10-01T17:30:00Z
**Status:** human_needed
**Re-verification:** No — initial verification

## Goal Achievement

### Observable Truths (Roadmap Success Criteria)

| # | Truth | Status | Evidence |
|---|-------|--------|----------|
| 1 | Free-text scenario + Simple/Standard/Complex preset visibly changes parties, counts, characters, timeline span, document length | VERIFIED (code) | `UserControls/StepCaseScenario.cs`: multiline TextBox `MaxLength = 0` (no 32K truncation), live counter, radios' `CheckedChanged` set `_lblPresetSummary.Text = ComplexityPreset.Get(SelectedLevel()).Summary`. `Models/Case/ComplexityPreset.cs` table strictly increases on every field; `Summary` prints parties/counts/characters/months/pages. Preset numbers flow into `CoreDocumentGenerator.BuildOutlinePrompt` (all ranges) and `SectionInstructions` (CharactersMin-Max in Parties/Facts/KeyIndividuals), and `CoreDocumentValidator` warns when outline/paragraph/memo length leaves the range. Visual check -> human |
| 2 | Real names warned, fictional substitutes applied by default, user can override | VERIFIED (code) | `RealEntityDetector.DetectAsync` (Story tier, "Real Name Check") sets `Apply = true` for every entity; refusals throw via `GetValueOrThrow`. `StepCaseScenario.ValidateStepAsync` runs it when the scenario hash changes, shows `EntityReviewDialog` ("Real Names Found", "Use fictional name" column, "Keep Real Names?" confirm on untick, leaking-substitute rejection), stores decisions, sets `EffectiveScenario = NameSubstitution.Apply(...)`. Generator only sends `EffectiveScenario`; kept names listed as approved exception (WR-01); validator `FindLeaks` flags surviving accepted originals across sections and outline fields (WR-02). Live LLM -> human |
| 3 | Civil -> Complaint/Petition, criminal/regulatory -> Investigation Memo, full realistic structure, one jurisdiction | VERIFIED (code) | `DocumentTypeResolver.Resolve` (preference wins; Criminal/Regulatory -> InvestigationMemo; State+petition terminology -> Petition). `CoreDocumentTemplates.GetSectionSpecs` fixed lists (Complaint: nature/parties/jurisdiction/facts/counts/prayer; Petition adds discovery control plan, claim for relief, conditions precedent; Memo: exec summary/background/scope/key individuals/facts/findings/legal analysis/recommendations). `CoreDocumentAssembler` renders caption from one `Jurisdiction` (no judge line), code-computed consecutive numbering, per-count incorporation paragraph, WHEREFORE lettered prayer, jury demand, signature block, memo header. Validator: court system/name contradictions = Error, state mismatch warnings, missing 1331/1332 basis, missing sections = Error. Output realism -> human |
| 4 | No invented case-law citations; automatic check flags any | VERIFIED | System prompt forbids case citations; `CitationScanner` ([GeneratedRegex], 1s timeout) flags reporter/WL/LEXIS cites as Error, bare "X v. Y" (not own caption, whole-token match) and signals as Warning, never statutes. Generator re-writes a section exactly once on Error findings. Validator scans every section plus title/memo RE; `StepCoreDocument.ValidateStepAsync` blocks Finish on any citation Error ("Case Citations Found"), then any Error, then asks on warnings. `WizardForm.BtnNext_Click` runs `ValidateStepAsync` before `Close()` on the last step |
| 5 | User can edit the core document or regenerate it (whole or by section) before continuing | VERIFIED (code) | `StepCoreDocument`: section list + editor, Apply Edits -> `CoreDocumentEditing.ApplyEdit` (re-split, strip numbers, IsUserEdited, Revision++), `RefreshAll` re-validates and re-renders `CoreDocumentAssembler.RenderPlainText`. Regenerate Section -> `RegenerateSectionAsync(draft, id, guidance)` (same Id, other sections' edited text, confirm when edited). Regenerate All with confirm; Resume All; stale-scenario prompt on entry. Interaction -> human |

**Score:** 5/5 roadmap truths supported by code; all 44 PLAN frontmatter truths (02-01..02-09) checked — none contradicted, with post-review deviations noted below.

### Plan must-have deviations (intentional, from 02-REVIEW-FIX.md)

| Plan truth | Current behavior | Assessment |
|------------|------------------|------------|
| 02-09: "Finish asks 'Close Case' before discarding" | Prompt moved to `OnFormClosing` (all close paths: Finish, Cancel, X) via `HasUnsavedCase` (WR-08) | Superset of planned behavior — VERIFIED |
| 02-08: "failed sections offer Retry Section" | Retry Section now regenerates only the selected section with guidance; new Resume All button resumes everything (WR-05) | Intent preserved — VERIFIED |
| 02-04: missing/unwritten sections are errors | Additionally: no counts, no fact subsections, empty Ready section are errors (WR-03) | Stricter — VERIFIED |

### Required Artifacts

| Artifact | Expected | Status | Details |
|----------|----------|--------|---------|
| `Models/Case/ComplexityPreset.cs` | Enum + preset table + Summary | VERIFIED | 100 lines, `Get(ComplexityLevel)` present |
| `Models/Case/CaseDraft.cs` | Root, JsonOptions, ValidateScenario, hashes | VERIFIED | `MaxScenarioLength = 8_000`, `JsonStringEnumConverter` |
| `Models/Case/CoreDocument.cs`, `CoreDocumentSection.cs`, `Jurisdiction.cs`, `EntitySubstitution.cs` | Models | VERIFIED | Present, used throughout |
| `Models/WizardState.cs` | `Flow` (CaseDriven default), `Case` | VERIFIED | lines 104, 107 |
| `Services/Case/CitationScanner.cs` | Scanner | VERIFIED | 124 lines, wired into generator + validator |
| `Services/Case/NameSubstitution.cs` | Apply + FindLeaks | VERIFIED | Wired into scenario step, dialog, validator |
| `Services/Case/DocumentTypeResolver.cs` | Doc-type decision | VERIFIED | Called in `MapOutline` |
| `Services/Case/CoreDocumentTemplates.cs` | Section specs | VERIFIED | Used by generator, validator, assembler |
| `Services/Case/CoreDocumentAssembler.cs` | Numbering/caption/render | VERIFIED | 317 lines; `RenderPlainText` used by UI preview |
| `Services/Case/CoreDocumentValidator.cs` | Validation | VERIFIED | 450 lines; used by UI after every run/edit and in Finish |
| `Services/Case/CoreDocumentEditing.cs` | ApplyEdit | VERIFIED | Used by UI Apply/leave/validate |
| `Services/Case/RealEntityDetector.cs` | LLM pre-flight | VERIFIED | Used by scenario step |
| `Services/Case/CoreDocumentGenerator.cs` | Outline + sections + retry + regen | VERIFIED | 880 lines; used by core-doc step |
| `UserControls/StepCaseScenario.cs` | Describe the Case step | VERIFIED | 525 lines |
| `UserControls/EntityReviewDialog.cs` | Real Names Found dialog | VERIFIED | 297 lines |
| `UserControls/StepCoreDocument.cs` | Review Core Document step | VERIFIED | 1,061 lines |
| `Helpers/LoadingOverlay.cs` | EntityCheck type | VERIFIED | `LoadingType.EntityCheck` |
| `Forms/WizardForm.cs` | `StepTypesFor`, `BuildSteps`, validated Finish | VERIFIED | lines 216-262, 314-346 |
| `UserControls/StepApiConfiguration.cs` | Workflow radios | VERIFIED | "Case-driven (new)" / "Legacy topic wizard"; `_state.Flow =` |

### Key Link Verification

| From | To | Via | Status |
|------|----|-----|--------|
| WizardState | CaseDraft | `public CaseDraft Case` | WIRED |
| CaseDraft | JsonStringEnumConverter | `JsonOptions` | WIRED |
| NameSubstitution | EntitySubstitution | `entry.Apply` filter in `TermMap.Build` | WIRED |
| CitationScanner | Regex source generator | `[GeneratedRegex(..., MatchTimeoutMs)]` | WIRED |
| CoreDocumentAssembler | Jurisdiction | `jurisdiction.CourtName` in `BuildCaption` | WIRED |
| CoreDocumentTemplates | CoreDocument | `doc.FactPlan` / `doc.Counts` | WIRED |
| CoreDocumentValidator | CitationScanner / NameSubstitution / Templates | `Scan`, `FindLeaks`, `GetSectionSpecs` | WIRED |
| RealEntityDetector | ILlmGateway | `_llm.GenerateAsync<EntityResponseDto>`, Tier Story | WIRED |
| CoreDocumentGenerator | ILlmGateway / CitationScanner / DocumentTypeResolver | `LlmTier.Story`, `CitationScanner.Scan`+`HasErrors`, `DocumentTypeResolver.Resolve` | WIRED |
| StepCaseScenario | RealEntityDetector / NameSubstitution / ComplexityPreset | `new RealEntityDetector(_state.CreateGateway())`, `NameSubstitution.Apply`, `.Summary` | WIRED |
| StepCoreDocument | Generator / Validator / Assembler / Editing | all four called (lines 750, 476, 549, 637, 861) | WIRED |
| WizardForm | IWizardStep.ValidateStepAsync on last step | `BtnNext_Click` validates before `Close()` | WIRED |
| StepApiConfiguration | WizardState.Flow | radio change + ValidateStepAsync | WIRED |
| WizardForm | StepCaseScenario + StepCoreDocument | `StepTypesFor(WizardFlow.CaseDriven)` | WIRED |

### Data-Flow Trace (Level 4)

| Artifact | Data Variable | Source | Produces Real Data | Status |
|----------|---------------|--------|--------------------|--------|
| StepCaseScenario preset label | `_lblPresetSummary.Text` | `ComplexityPreset.Get(level).Summary` | Yes (static table by design) | FLOWING |
| StepCaseScenario -> draft | `EffectiveScenario`, `Substitutions` | detector LLM result + dialog decisions | Yes | FLOWING |
| StepCoreDocument sections/preview | `_state.Case.CoreDocument` | `CoreDocumentGenerator.GenerateAsync` (outline + section LLM calls on `EffectiveScenario`) | Yes | FLOWING |
| StepCoreDocument issues grid | `_issues` | `CoreDocumentValidator.Validate(_state.Case)` | Yes | FLOWING |

### Behavioral Spot-Checks

| Behavior | Command | Result | Status |
|----------|---------|--------|--------|
| Phase 2 unit tests (scanner, substitution, resolver, templates, assembler, validator, editing, generator with fake gateway, schemas, UI logic, wizard flow) | `dotnet test ReelDiscovery.slnx --filter "FullyQualifiedName~Case\|FullyQualifiedName~WizardFlow"` | Passed 276/276 | PASS |
| Full suite / build | orchestrator-confirmed | 489/489, 0 warnings | PASS |
| Live UI / LLM | — | needs desktop + API key | SKIP (human) |

### Requirements Coverage

| Requirement | Source Plan(s) | Description | Status | Evidence |
|-------------|----------------|-------------|--------|----------|
| CASE-01 | 02-01, 02-07, 02-09 | Free-text scenario seeds a case | SATISFIED | StepCaseScenario + CaseDraft.Scenario; flow wired in WizardForm |
| CASE-02 | 02-01, 02-04, 02-06, 02-07 | Complexity preset sets parties, counts, characters, timeline, length | SATISFIED | ComplexityPreset table -> summary, prompts, validator warnings |
| CASE-03 | 02-02, 02-05, 02-07 | Detect real names, warn, substitute by default, override | SATISFIED (needs human for live) | RealEntityDetector, EntityReviewDialog, NameSubstitution, leak scan |
| CASE-04 | 02-03, 02-06, 02-08 | Complaint/Petition vs Investigation Memo | SATISFIED | DocumentTypeResolver in MapOutline |
| CASE-05 | 02-03, 02-04, 02-06, 02-08 | Realistic structure, one jurisdiction | SATISFIED (output quality human) | Templates, Assembler, jurisdiction checks |
| CASE-06 | 02-02, 02-04, 02-06, 02-08 | No invented case law, checked automatically | SATISFIED | CitationScanner + generator retry + Finish block |
| CASE-07 | 02-04, 02-06, 02-08, 02-09 | Review, edit, regenerate before continuing | SATISFIED | StepCoreDocument edit/regen/regenerate-all |

No orphaned requirements: REQUIREMENTS.md maps exactly CASE-01..CASE-07 to Phase 2 and every ID appears in at least one plan's `requirements` field.

### Anti-Patterns Found

| File | Line | Pattern | Severity | Impact |
|------|------|---------|----------|--------|
| (Phase 2 files) | — | No TODO/FIXME/placeholder/NotImplemented found | — | — |
| `Forms/WizardForm.cs` | 391 | Finish discards the case ("This version does not save the case yet") | Info | Intentional; persistence (case.json) is Phase 3 SC4. User is warned on every close path |
| `Services/Case/CoreDocumentValidator.cs` | 380 | Regex timeout downgrades a section's citation scan to a Warning ("too large to scan") | Info | Edge case only (sections capped at 40 paragraphs); Finish still requires explicit Yes on warnings |

### Human Verification Required

1. **Scenario step and preset summary** — Launch, choose Case-driven, type a scenario, switch presets. Expected: summary numbers change; "Step 2 of 3" header; counter vs 8,000.
2. **Real-name warning and override** — Scenario naming a real company/person. Expected: dialog with defaults ticked; untick -> "Keep Real Names?"; substitutes used in the document; kept name preserved.
3. **Civil vs criminal document structure (Claude and OpenAI)** — Expected: Complaint/Petition or Investigation Memo with full structure for one court/state; counts roughly match preset; no citation errors.
4. **Edit / regenerate / Finish gating** — Edit, regenerate a section with guidance, Regenerate All, inject "531 U.S. 98", click Finish. Expected: confirmations, renumbered preview, Finish blocked, Close Case prompt.
5. **Failure, cancel and close paths (review-fix items CR-02, WR-05, WR-07, WR-09)** — Expected: completed sections kept, Retry Section single-section, Resume All, explanatory empty state, no exceptions when closing mid-run.

### Gaps Summary

No code gaps found. Every roadmap success criterion and every plan must-have truth has substantive, wired implementation, and 276 Phase 2 tests pass. All artifacts exist, have real content and are connected: models -> services -> wizard steps -> WizardForm flow. The remaining uncertainty is runtime-only: WinForms appearance and interaction, and the legal realism and preset adherence of live LLM output from both providers. The review-fix changes CR-02, WR-05, WR-07, WR-08 and WR-10 also need a hands-on check. These are listed above for human testing.

---

_Verified: 2026-10-01T17:30:00Z_
_Verifier: Claude (gsd-verifier)_
