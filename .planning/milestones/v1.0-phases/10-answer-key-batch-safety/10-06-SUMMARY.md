---
phase: 10-answer-key-batch-safety
plan: 06
subsystem: ui/generate-corpus
tags: [batch, banner, provider, start-over, WR-A06, D-14, D-15, D-16]
requires:
  - "10-02 (per-provider ledger counts, PriceUnusedResults, OrphanOperation)"
provides:
  - "GenerationUiText.BannerBody other-provider parameters and OtherProviderKeyMissingText(provider)"
  - "StepGenerateCorpus.TransportFor() / HasBatchTransport(kind) / OtherProviderBatches / OtherProviderBlocked (reusable by Plan 07's run-start resolver)"
  - "WizardState.PriceBatchModel(kind, modelId) shared by CreateRunGateway and Start Over"
  - "Start Over records collected-but-unused batch spend under 'Batch results not used'"
affects:
  - "Pending-batch banner clause and CG1/CG3 batch clauses now refer to the current text provider's batches only"
tech-stack:
  added: []
  patterns:
    - "Key availability = a batch transport can be built for that provider (D-22), via the BatchTransportFactory seam or the session key"
key-files:
  created: []
  modified:
    - UserControls/GenerationUiText.cs
    - UserControls/StepGenerateCorpus.cs
    - UserControls/StepGenerateCorpus.Run.cs
    - Models/WizardState.cs
    - ReelDiscovery.Tests/Evidence/GenerationUiTextTests.cs
    - ReelDiscovery.Tests/Evidence/GenerateCorpusStepTests.cs
    - ReelDiscovery.Tests/Render/GenerateCorpusRenderTests.cs
decisions:
  - "The banner's current-provider clause comes first, then the other-provider clause (resolve copy, or the D-14 notice when that key is missing)"
  - "Start Over prices unused results only when !cancelFailed; an unreadable ledger prices as empty, so nothing is billed twice (Pitfall 13)"
  - "No explicit header refresh after recording: WizardForm's cost timer polls the UsageTracker and OnEnterStepAsync calls RefreshHeader"
metrics:
  duration: "~10 min"
  completed: 2026-10-12
  tasks: 3
  files: 7
---

# Phase 10 Plan 06: Per-Provider Pending-Batch Banner and Start Over Unused-Spend Summary

The pending-batch banner now counts only batches the current text provider can collect "without paying again" (D-15). Another provider's batches get their own clause: either they are collected or cancelled before generating, or, when that provider's key is missing, the banner shows the D-14 notice pointing to API Configuration. Start Over now records the spend of collected batch results that were never used on the session tracker, under "Batch results not used", before the ledger is deleted (D-16, WR-A06).

## What Was Built

### Task 1: Banner copy (commits d689758 RED, 4857f1f GREEN)
- `BannerBody(..., int otherProviderBatches = 0, string otherProvider = "", bool otherProviderBlocked = false)` appends the D-15 clause verbatim. Examples: "2 Anthropic batches from an earlier session are collected or cancelled before generating." and the singular form "1 Anthropic batch ... is ...".
- `OtherProviderKeyMissingText(provider)` holds the D-14 notice. It replaces the resolve clause when the run is blocked.
- Existing callers and tests are unchanged.

### Task 2: Per-provider counts at all three call sites (commits 0bc6146 RED, eb39e48 GREEN)
- After a run, `ReadEntryAsync` (new `LlmProviderKind textProvider` parameter) and `LoadForRender` now use `PendingBatchCount(folder, planId, provider)` and `PendingBatchCountOtherProviders`.
- New step state `_otherProviderBatches`, `_otherProvider` and `_otherProviderBlocked` is set through `SetOtherProviderBatches`. It is reset on entry.
- `TransportFor()` (extracted from `CancelPendingBatchesAsync`) and `HasBatchTransport(kind)` are ready for Plan 07's run-start resolver.
- Start Over still uses `PendingBatchCountAllPlans` (WR-A05). The runner's live count during a run (`view.PendingProviderBatches`) is unchanged.
- Render: `PartialPendingBatches` now seeds an OpenAI record, the fixture's current provider. New render tests `PartialOtherProviderBatches` and `PartialOtherProviderKeyMissing` were added.

### Task 3: Start Over unused-spend recording (commits 8fce125 RED, 72abef5 GREEN)
- `WizardState.PriceBatchModel` replaces the inline lambda in `CreateRunGateway`, with the same lookup.
- Start Over runs `PriceUnusedResults(new BatchLedger(folder), _state.PriceBatchModel)` off the UI thread. It runs after the cancel succeeded and before `GenerationReset.StartOverAsync`, and records each line with `RecordUsage(OrphanOperation, ...)`.

## Verification
- `-warnaserror` build is green at every commit. RED commits used compiling stubs.
- Filter `GenerationUiTextTests|GenerateCorpusStepTests|GenerateCorpusRenderTests|Batch`: 532 passed, 0 failed.
- Filter `GenerateCorpusStepTests|BatchParityTests|WizardStateProviderTests`: 72 passed.

## Render / DPI
- I inspected the PNGs for both new renders at 1000x540 and 1100x750. The banner body wraps to four lines in the key-missing case and nothing is clipped (the harness clipping checks pass).
- **This machine renders at 120 DPI, not 144.** A human still needs to check the new banner states at 150% scaling.

## Deviations from Plan

None - plan executed exactly as written. (The RED commits included minimal compiling stubs so that every commit builds with -warnaserror, as the orchestrator required.)

## Notes for Later Plans
- The D-14 banner notice says the run is blocked, but Continue Generating stays enabled and the run does not refuse yet. Refusing at run start is Plan 07's job (D-22). Plan 07 can reuse `HasBatchTransport` / `TransportFor` / `OtherProviderBlocked`.

## TDD Gate Compliance
RED and GREEN commits exist for every task: d689758 then 4857f1f, 0bc6146 then eb39e48, 8fce125 then 72abef5.

## Self-Check: PASSED
- All 7 modified files exist; commits d689758, 4857f1f, 0bc6146, eb39e48, 8fce125, 72abef5 are present in git log.
