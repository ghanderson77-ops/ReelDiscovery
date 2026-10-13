---
phase: 08-ancillary-discovery-documents
verified: 2026-10-08T21:20:00Z
status: passed
score: 3/3 roadmap success criteria verified in code and automated tests (live-provider consistency, Word layout and 144-DPI inspection still need a person)
overrides_applied: 0
human_verification:
  - test: "Open obj/gsd-artifacts/bin/ReelDiscovery.Tests/debug/renders/discovery-sample-deposition.docx in Word"
    expected: "40 pages (matches discovery-sample-deposition.pages.txt), line 25 on the same page as lines 1-24 on every page, Courier New, EXCERPT caption, index page numbers for the examination and each exhibit. discovery-sample-rfp.docx shows caption with case number, definitions, instructions, numbered requests, signature block, certificate of service."
    why_human: "Word pagination cannot be checked from code; the code-computed page map is tested but Word's rendering is not"
  - test: "Inspect discovery-*, dialog-regenerate-*, wizard-shell-* and Case Summary PNGs (144 DPI) in the renders folder"
    expected: "No clipped or ellipsized text, aligned controls, one Primary button per screen"
    why_human: "Visual layout quality at 150% DPI"
  - test: "Live run with Claude on a small litigation case with a guide (e.g. with 'Michael Scott'): Standard, 2 witnesses, all written sets"
    expected: "Names verbatim, RFA dates/amounts match Key Facts, each RFP tied to a case issue, no (a)/(b) interrogatory subparts, warning rate acceptable (WR-03 made the D5 date check stricter)"
    why_human: "Real model output consistency (ANC-05) can only be judged on a live provider; all automated tests use FakeLlmProvider"
  - test: "Repeat the live run with OpenAI (one deposition, one RFP set)"
    expected: "Same as the Claude run"
    why_human: "Second provider's strict-schema behaviour is not exercised by tests"
  - test: "Regenerate with guidance; edit a fact title; regenerate while the .docx is open in Word"
    expected: "Regenerate replaces the document; fact edit marks rows Out of date with no auto-regeneration; open file gives the dialog warning, row shows Not saved, Save Again succeeds after closing Word"
    why_human: "Real Word file lock and end-to-end UI flow"
  - test: "Case Summary Discovery row, Discovery Documents navigation button, 'discovery documents $...' in the Generation cost row"
    expected: "Row text, navigation and cost clause as specified in UI-SPEC"
    why_human: "End-to-end UI flow on a real case"
---

# Phase 8: Ancillary Discovery Documents Verification Report

**Phase Goal:** The user can optionally generate realistic discovery documents that are consistent with the case guide.
**Verified:** 2026-10-08T21:20:00Z
**Status:** passed (human verification items reported complete by the user, 2026-10-13)
**Re-verification:** No. This is the initial verification. No earlier VERIFICATION.md exists.

## Goal Achievement

### Observable Truths (ROADMAP success criteria)

| # | Truth | Status | Evidence |
|---|-------|--------|----------|
| 1 | User can pick key witnesses and generate deposition transcript excerpts with numbered lines and pages | VERIFIED (code + tests) | `DepositionPlanner.Eligible/DefaultWitnessIds` (key-tier, non-counsel, cap 6). The witness checklist is in `StepDiscoveryDocuments.cs`. `DepositionWriter` writes chunk by chunk through `PlanCallRunner.RunAsync<DepositionChunkDto>`. `TranscriptLayout` sets `LinesPerPage = 25` and `MaxCharsPerLine = 62`. `TranscriptDocxBuilder` uses Courier New, exact 24 pt spacing and PageBreakBefore. Sample export: 40 pages, 25 lines per page. Wiring: Run.cs `GeneratorFactory` -> `DiscoveryGenerator.RunAsync` -> `DepositionWriter` -> `TranscriptDocxBuilder.Build` -> `CaseStore.WriteAtomicAsync` into `Discovery\`. |
| 2 | User can generate RFPs and Interrogatories keyed to the issues, and RFAs keyed to the key facts | VERIFIED (code + tests) | `WrittenDiscoveryPlanner` gives every issue at least one RFP via `Apportion.Largest(..., min: 1)`, keeps ROGs at 25 or fewer and covering the issues, and gives each RFA one fact with a code-owned prefix `Admit that on {LongDate},` (line 420). `WrittenSetWriter` calls `PlanCallRunner.RunAsync<RequestSetDto>` and uses `DiscoveryBoilerplate` for caption, definitions, instructions, signature and certificate of service. `DiscoveryDocxBuilder` reuses `WriteCaption`/`WriteSignatureBlock`. |
| 3 | All ancillary documents use the same names, dates, amounts and allegations as the core document, cast and facts, and export as .docx | VERIFIED in code, UNCERTAIN on live output (human) | Prompts are built only from structured guide data: Issues, Cast, Canonical values, the filtered facts and the ledger slice (`DiscoveryPrompts.cs` lines 74-185). Raw complaint text is not used. `DiscoveryValidator` D1-D8 checks unknown people, amount variants, identifiers, RFA fact date/amount/actor, dates after the deposition, subparts and substituted real names. It works with keep-best and retry feedback. Captions reuse the core document's court and parties. Staleness stamps record guide, core and caption hashes. Output is .docx validated by OpenXmlValidator in tests. Whether real model output stays faithful has to be judged on a live run (UAT items 3-4). |

**Score:** 3/3 roadmap truths are supported by implemented, wired and tested code. Truth 3's live-output half is routed to human verification.

### Plan must-haves (08-01 .. 08-19)

All plan artifacts exist and are substantive. The smallest is `IConfirmLeave.cs` at 10 lines, which is correct for an interface. Every `key_links` pattern from the 19 plans was grepped and matched (38/38). The parsed-frontmatter tool (`gsd-sdk verify.*`) could not read the PLAN YAML blocks, so the checks were run by hand.

| Plan truth (08-19) | Status | Note |
|---|---|---|
| Both -warnaserror builds, full non-render suite and full render suite green | VERIFIED | Re-run during this verification (see below) |
| Deposition .docx in Word matches the code page count, line 25 placement, Courier New, EXCERPT | HUMAN | UAT item 1, deferred |
| Live Claude + OpenAI run matches guide names, dates, amounts and allegations | HUMAN | UAT items 3-4, deferred |
| Every Phase 8 PNG inspected at 144 DPI | HUMAN | UAT item 2, deferred. 32 PNGs were produced. |
| ANC-01..ANC-05 marked complete only after human verification passes | NOT HONORED (WARNING) | REQUIREMENTS.md marks ANC-01..05 `[x]` / Complete, but 08-HUMAN-UAT.md is `status: deferred`. The user chose to defer. Either revert the checkboxes until UAT passes or record an override. |

### Required Artifacts

| Artifact | Status | Details |
|---|---|---|
| Models/Discovery/DiscoverySettings.cs, DiscoveryDocumentSet.cs | VERIFIED | 75 / 85 lines. `CaseDraft.Normalize` calls `Discovery?.Normalize()`. |
| Services/Discovery/DiscoveryStore.cs, DiscoveryStaleness.cs | VERIFIED | Atomic sidecar write via `CaseStore.WriteAtomicAsync`. `DocxPath` is used by the generator and by Open in Word. |
| Services/Discovery/CounselDirectory.cs, CaseNumber.cs | VERIFIED | Uses `RepresentsOrgIds`. `CaseNumber.For` is assigned once in `DiscoveryGenerator` line 156. |
| Services/Discovery/DepositionPlanner.cs (949), WrittenDiscoveryPlanner.cs (555) | VERIFIED | Calls StableRank, BusinessCalendar, KnowledgeGate and Apportion. |
| Services/Discovery/CorpusDetector.cs | VERIFIED | Calls `BlindReviewer.GeneratedEmailDocIds` and `LedgerMerger.Build`. |
| Services/Discovery/TranscriptLayout.cs, TranscriptDocxBuilder.cs | VERIFIED | 25 lines per page, 62 chars per line, Courier New, Exact spacing. |
| Services/Discovery/DiscoveryBoilerplate.cs, DiscoveryDocxBuilder.cs | VERIFIED | Reuses the core caption and signature writers. The core caption still shows `____________`. |
| Services/Evidence/TextScan.cs, Services/Discovery/DiscoveryValidator.cs | VERIFIED | `RenderValidator` uses the shared TextScan regexes. The validator calls `CanonicalValueScanner.Scan`. |
| Services/Discovery/DiscoveryPrompts.cs | VERIFIED | `SchemaTypes` are registered in `CaseSchemaTests`. Retry feedback goes through `DiscoveryValidator.FeedbackText`. |
| Services/Discovery/DepositionWriter.cs, WrittenSetWriter.cs | VERIFIED | Go through PlanCallRunner, with keep-best and cancellation spend capture (CR-02). |
| Services/Discovery/DiscoveryWorklist.cs, UserControls/DiscoveryStepModel.cs, GenerationCostEstimator | VERIFIED | `PriceDiscovery*` is wired. Same-name witnesses are disambiguated (WR-08). |
| Services/Discovery/DiscoveryGenerator.cs | VERIFIED | `MaxParallelCalls = 3`. The .docx is written atomically and then the sidecar is upserted under `_writeLock`. Uses FileLockDetector and Staleness.Stamp. |
| UserControls/StepDiscoveryDocuments.cs (+ .Run.cs), DiscoveryUiText, DiscoveryRowInfo, Forms/RegenerateDiscoveryDialog.cs | VERIFIED | Gateway comes from `_state.CreateGateway()`. Regenerate opens the dialog. Paths are validated. |
| Forms/WizardForm.cs, UserControls/IConfirmLeave.cs | VERIFIED | `StepDiscoveryDocuments` is in the step list between GenerateCorpus and CaseSummary. Has the `is IConfirmLeave` leave check, `CancelAndWaitForRun`, and CD2 confirmation on close. |
| UserControls/StepCaseSummary.cs, GenerationUiText.cs | VERIFIED | `NavigateToStep(typeof(StepDiscoveryDocuments))`, `DiscoveryUiText.SummaryRow`, and the "discovery documents" cost clause. |
| ReelDiscovery.Tests/Render/DiscoveryDocumentsRenderTests.cs, Discovery/DiscoverySampleExportTests.cs | VERIFIED | Produce PNGs plus sample .docx and page-map files. |

### Key Link Verification

38/38 plan `key_links` patterns found in their source files. The links that carry the critical path:

| From | To | Status |
|---|---|---|
| StepDiscoveryDocuments.Run.cs | DiscoveryGenerator.RunAsync (via `GeneratorFactory`, `_state.CreateGateway()`) | WIRED |
| DiscoveryGenerator | DepositionWriter / WrittenSetWriter | WIRED (lines 295, 304) |
| DiscoveryGenerator | TranscriptDocxBuilder.Build / DiscoveryDocxBuilder.Build -> CaseStore.WriteAtomicAsync | WIRED (lines 421, 493-494) |
| WizardForm | StepDiscoveryDocuments (step 8 of 9) | WIRED |
| StepCaseSummary | NavigateToStep(typeof(StepDiscoveryDocuments)) | WIRED |

### Data-Flow Trace (Level 4)

| Artifact | Data | Source | Real data | Status |
|---|---|---|---|---|
| Deposition prompt | issues, cast, canonical values, facts known by the witness, ledger | `CaseDraft` guide + `DepositionPlanner` plan + `CorpusDetector` ledger slice | Yes | FLOWING |
| Written-set prompt | code slots with issue/fact anchors and RFA prefixes | `WrittenDiscoveryPlanner.Plan` from the guide | Yes | FLOWING |
| Documents grid / Summary row | saved records + selected items | `DiscoveryStore.Load` sidecar + `DiscoveryWorklist.Build` | Yes | FLOWING |

### Behavioral Spot-Checks

| Behavior | Command | Result | Status |
|---|---|---|---|
| App builds warning-free | `dotnet build ReelDiscovery.csproj -warnaserror --artifacts-path obj/gsd-artifacts` | 0 warnings, 0 errors | PASS |
| Test project builds warning-free | `dotnet build ReelDiscovery.Tests/ReelDiscovery.Tests.csproj -warnaserror --artifacts-path obj/gsd-artifacts` | 0 warnings, 0 errors | PASS |
| Full non-render suite | `dotnet test ... --filter "Category!=Render&Category!=Scale"` | 4305 passed, 0 failed (1 m 35 s) | PASS |
| Full render suite | `dotnet test ... --filter "Category=Render"` | 150 passed, 0 failed (9 m 9 s) | PASS |
| Sample deposition page map | `discovery-sample-deposition.pages.txt` | 40 pages, testimony 4-37, 25 lines per page | PASS |

### Requirements Coverage

| Requirement | Source Plans | Description | Status | Evidence |
|---|---|---|---|---|
| ANC-01 | 02,03,05,06,09,10,11,12,14,15,16,17,18,19 | Deposition excerpts for chosen key witnesses with numbered lines and pages | SATISFIED (code); Word check deferred | Truth 1 |
| ANC-02 | 02,04,07,09,10,11,13-19 | RFPs keyed to issues | SATISFIED (code); live check deferred | Truth 2 |
| ANC-03 | 04,05,07,08,09,10,11,13-19 | RFAs keyed to key facts | SATISFIED (code); live check deferred | Truth 2 |
| ANC-04 | 04,07,08,09,10,11,13-19 | Interrogatories keyed to issues | SATISFIED (code); live check deferred | Truth 2 |
| ANC-05 | 01,03,06,07,08,09,10,11,12,13,15,17,18,19 | Consistent with core document, cast and facts; exported as .docx | SATISFIED (code); NEEDS HUMAN for live output | Truth 3 |

No orphaned requirements. REQUIREMENTS.md maps only ANC-01..05 to Phase 8, and all of them are claimed by plans.

### Anti-Patterns Found

| File | Line | Pattern | Severity | Impact |
|---|---|---|---|---|
| Services/Discovery, Models/Discovery, Discovery UI files | - | TODO/FIXME/NotImplementedException/placeholder | None found | - |
| .planning/REQUIREMENTS.md | 54-58, 205-209 | ANC-01..05 marked Complete while Phase 8 UAT is deferred, against the 08-19 must-have | WARNING | Bookkeeping only. The roadmap still shows Phase 8 unchecked. |
| UserControls/StepDiscoveryDocuments*.cs | - | CR-03 extra not done: exhibit numbers saved before a corpus existed are never replanned | INFO | New corpus-less assignments are blocked by the `_probing` gate. Old ones need a design decision. |
| Services/Discovery/DiscoveryValidator.cs | - | WR-03 made the D5 date check stricter | INFO | May raise the warning rate on live runs. Check during UAT item 3. |

### Human Verification Required

These are the deferred items from 08-HUMAN-UAT.md, listed in the frontmatter:
1. Word check of the sample deposition and RFP .docx (ANC-01, D-23)
2. 144-DPI PNG inspection (D-03)
3. Live Claude run for name, date and amount fidelity (ANC-02..05)
4. Live OpenAI run (ANC-02..05)
5. Regenerate with guidance, Out-of-date marking, and a Word-locked file followed by Save Again
6. Case Summary Discovery row, navigation and cost clause

### Gaps Summary

No code gaps. Every roadmap success criterion has a substantive, wired and tested implementation: UI -> DiscoveryGenerator -> writers -> .docx builders -> atomic write into `Discovery\`. Both builds are warning-free, and 4305 non-render plus 150 render tests pass. The status is `human_needed` because the parts that need a person have not been checked: Word pagination, visual layout at 144 DPI, and whether real model output stays faithful to the guide. The user deferred these. One bookkeeping warning: ANC-01..05 are already marked Complete in REQUIREMENTS.md, but the 08-19 must-have said to mark them only after human verification.

---

_Verified: 2026-10-08T21:20:00Z_
_Verifier: Claude (gsd-verifier)_

## Orchestrator note (2026-10-08)

User override: the user directed "yolo through the rest of the steps" and deferred manual/live checks. ANC-01..ANC-05 stay marked Complete on the strength of the automated verification above; the manual items remain tracked in 08-HUMAN-UAT.md (status: deferred) and should be run with `/gsd-verify-work 8` before release.


---

**Human verification closed 2026-10-13:** the user reported completing live testing of the items above; see the phase HUMAN-UAT file.
