---
phase: 08-ancillary-discovery-documents
plan: 18
subsystem: case-summary
tags: [discovery, case-summary, cost, winforms, render, asvs-v12, ANC-01, ANC-02, ANC-03, ANC-04, ANC-05]
requires:
  - phase: 08-09
    provides: "DiscoveryUiText.SummaryTitle / SummaryLine2 / SummaryRow"
  - phase: 08-10
    provides: "DiscoveryStepModel.SavedRows"
  - phase: 08-16
    provides: "WizardForm.NavigateToStep(Type); StepCaseSummary as IConfirmLeave"
provides:
  - "Case Summary Discovery group with the Discovery documents row (Open Discovery Folder, Discovery Documents)"
  - "GenerationUiText.CostRow(..., decimal discovery = 0m): discovery documents clause and tooltip line"
  - "StepCaseSummary.LoadQa(..., decimal discoverySpend): cost row computed before the no-plan early return"
  - "StepCaseSummary seams: NavigateToDiscoveryOverride; internal NavigateToDiscovery, OpenDiscoveryFolder, DiscoveryPath"
affects: [08-19]
tech-stack:
  added: []
  patterns:
    - "discovery-documents.json read once per Case Summary refresh, off the UI thread, feeding both the cost row and the Discovery row"
key-files:
  created: []
  modified:
    - UserControls/GenerationUiText.cs
    - UserControls/StepCaseSummary.cs
    - ReelDiscovery.Tests/Evidence/GenerationUiTextTests.cs
    - ReelDiscovery.Tests/Qa/CaseSummaryQaTests.cs
    - ReelDiscovery.Tests/Case/CaseSummaryLogicTests.cs
    - ReelDiscovery.Tests/Render/CaseSummaryRenderTests.cs
    - .planning/phases/08-ancillary-discovery-documents/08-UI-SPEC.md
key-decisions:
  - "08-18: QaInputs carries the draft, and LoadQaWithDiscovery reads the sidecar once for both the spend and the row, inside the same Task.Run as LoadQa (T-08-55)"
  - "08-18: The Discovery row's line 2 gets a tooltip with the full text, because at 144 DPI it ellipsizes before 'Not part of the answer key.' (D-30). UI-SPEC S4 amended"
  - "08-18: Open Discovery Folder with a missing folder re-reads the rows (RefreshQaAsync) instead of opening anything, the same as Open Mailboxes Folder"
requirements-completed: []
duration: 20min
completed: 2026-10-09
---

# Phase 8 Plan 18: Case Summary Discovery Row and Discovery Cost Summary

Case Summary now has a `Discovery` group between Quality checks and Case guide, with one `Discovery documents` row. Line 3 reads `Not generated`, or gives the document count, the last generated date and every problem clause. Its buttons open the validated `Discovery\` folder and go back to the Discovery Documents step. The Generation cost row adds a `discovery documents $d` clause, and its total includes discovery spend. The row is computed even when the case has no corpus plan.

## What was built

- **Cost clause (S5).** `GenerationUiText.CostRow` and `CostItems` take an optional trailing `discovery` amount. The clause `discovery documents $d` and the tooltip line `Discovery documents: $d` sit after images and voicemail and before not itemized, and are dropped at zero.
- **Plan-independent cost.** `StepCaseSummary.LoadQa(..., decimal discoverySpend)` sets `qa.Cost` before the `folder empty || plan == null` return. A case with no corpus shows `$d spent · discovery documents $d` with a Success dot. With a plan, the journal spend plus discovery is the total.
- **Discovery row (S4).** `CreateRow(DiscoveryUiText.SummaryTitle, ..., { "Open Discovery Folder", "Discovery Documents" })`, both Default. Content comes from `DiscoveryUiText.SummaryRow(DiscoveryStepModel.SavedRows(...), DiscoveryStore.HasDocuments(folder))`. That gives line 3, the tone and dot, a tooltip with the full line plus one line per document, and Open Discovery Folder only when the folder holds a .docx. The row's buttons follow the step's busy gating.
- **Navigation.** `NavigateToDiscovery()` calls `(FindForm() as WizardForm)?.NavigateToStep(typeof(StepDiscoveryDocuments))`. Tests replace it through `NavigateToDiscoveryOverride`.
- **Open folder (T-08-54).** `DiscoveryPath` builds the path from the verified case folder plus the constant `Discovery` and requires it to resolve directly under that folder. It then goes through the existing `OpenFolder` seam (a quoted explorer.exe argument) and never uses a path from the sidecar.
- **Export All / D-30.** Export All and the answer key / review protocol builders never reference discovery. Source assertions check this.

## Tasks

| Task | Name | Commits |
|------|------|---------|
| 1 | Cost row discovery clause and plan-independent cost computation | 6f3600e (RED), 900efa3 (GREEN) |
| 2 | Discovery documents row, buttons and 144-DPI renders | e92dca0 (RED), 66f9144 (GREEN) |

## Verification

- `dotnet build ReelDiscovery.csproj -warnaserror` and `dotnet build ReelDiscovery.Tests/ReelDiscovery.Tests.csproj -warnaserror` (artifacts path `obj/gsd-artifacts`): both green, 0 warnings.
- `GenerationUiTextTests | CaseSummaryQaTests | CaseSummaryLogicTests | Category=Render&CaseSummaryRenderTests`: 333/333 passed.
- Regression: `Tests.Discovery. | WizardFlowTests`: 519/519 passed.
- Acceptance greps: `NavigateToStep(typeof(StepDiscoveryDocuments))` = 1, `(null, "Discovery")` = 1, `DiscoveryUiText.SummaryRow` = 1, and the AnswerKey/Discovery grep over AnswerKeyBuilder.cs and ReviewProtocolBuilder.cs = 0.

### 144-DPI render inspection (rule 40, render check 9)

I inspected these by eye at Large (1100x750) and WizardMin (1000x540):

- `case-summary-discovery-not-generated`: the Discovery heading sits between Blind review and Case guide. The dot is BorderStrong, line 3 reads `Not generated`, and only `Discovery Documents` is visible, at full width.
- `case-summary-discovery-problems`: the dot and line 3 are Danger. At Large the full line `5 documents · last generated Jun 2, 2025 · 1 with warnings · 1 out of date · 1 not saved · 1 file missing · 1 failed` fits. At WizardMin it ends in an ellipsis, and the tooltip has the full line plus six document lines. `Open Discovery Folder` and `Discovery Documents` never ellipsize, and the button column starts to the right of line 3's end.
- `case-summary-cost-no-corpus-discovery`: on a case with no corpus plan, Corpus plan reads Not planned and Generation cost reads `$1.50 spent · discovery documents $1.50` with a Success dot.
- The clipping detector found nothing in any of the six PNGs.
- Finding: line 2 (`... Not part of the answer key.`) ellipsizes at both sizes. I fixed this with a full-text tooltip on line 2 (see Deviations).

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 2 - Missing critical functionality] Line 2 tooltip so the D-30 sentence stays readable**
- **Found during:** Task 2 (render inspection)
- **Issue:** At 144 DPI line 2 ellipsizes before "Not part of the answer key.", which hides the D-30 statement.
- **Fix:** Line 2 gets a tooltip with `DiscoveryUiText.SummaryLine2`. UI-SPEC S4 now records this, and a logic test asserts it.
- **Files modified:** UserControls/StepCaseSummary.cs, ReelDiscovery.Tests/Case/CaseSummaryLogicTests.cs, 08-UI-SPEC.md
- **Commit:** 66f9144

**2. [Rule 3 - Structure] The sidecar is read once for the spend and the row**
- **Issue:** The plan read `TotalCostUsd` at the call sites and loaded the row separately. That would read discovery-documents.json twice per refresh.
- **Fix:** `QaInputs` now carries the draft instead of a case id. `LoadQaWithDiscovery` loads the set once and passes the spend to `LoadQa`, then sets `QaSnapshot.Discovery`. `RefreshQaAsync` runs it in the existing `Task.Run`, and `LoadForRender` calls it synchronously.
- **Commit:** 66f9144

Step-level tests (the step is constructed but never shown) live in `CaseSummaryLogicTests`, as the plan's file list says. The class comment now covers them.

## Known Stubs

None.

## TDD Gate Compliance

Both tasks have a `test(08-18)` RED commit (a compile failure against the missing API) followed by a `feat(08-18)` GREEN commit.

## Self-Check: PASSED
