---
phase: 05-relevant-evidence-generation-narrative-ledger
plan: 02
subsystem: shared-service-hooks
tags: [tokens, cost, answer-key, numbering, overlay]
requires: []
provides:
  - LlmResult.InputTokens/OutputTokens (summed over every billed attempt)
  - PlanCallOutcome.InputTokens/OutputTokens (success, refusal, fatal)
  - PlanNumbering.CustodianFolders(guide)
  - CanonicalValueScanner.TryParseAmount (internal)
  - AnswerKeyBuilder status constants + AnswerKeyOverlay/AnswerKeyOverlayEntry + Build(plan, guide, overlay)
  - XLSX legend text covering all five statuses
affects: [05-03..05-17]
tech-stack:
  added: []
  patterns: [optional overlay parameter keeps Phase 4 callers byte-identical]
key-files:
  created:
    - ReelDiscovery.Tests/Evidence/AnswerKeyOverlayTests.cs
  modified:
    - Services/Ai/LlmResult.cs
    - Services/Ai/LlmGateway.cs
    - Services/Corpus/PlanCallRunner.cs
    - Services/Corpus/PlanNumbering.cs
    - Services/Case/CanonicalValueScanner.cs
    - Services/Corpus/AnswerKeyBuilder.cs
    - Services/Corpus/AnswerKeyXlsxWriter.cs
    - ReelDiscovery.Tests/Ai/LlmGatewayTests.cs
    - ReelDiscovery.Tests/Corpus/PlanCallRunnerTests.cs
    - ReelDiscovery.Tests/Corpus/PlanNumberingTests.cs
decisions:
  - "Answer-key overlay applies only to email rows and is applied before attachment rows are built, so attachments copy the rendered subject but stay Planned (attachments are Phase 6)"
  - "A blank overlay Status falls back to Planned; a blank overlay Subject keeps the computed subject"
metrics:
  duration: 10min
  completed: 2026-10-03
  tasks: 2
  files: 11
---

# Phase 5 Plan 02: Shared Service Hooks (Tokens, Folders, Amounts, Key Overlay) Summary

Additive hooks so later Phase 5 plans never edit Phase 1-4 code: per-call token totals on gateway results and plan-call outcomes (for the per-thread cost journal, RUN-07), the custodian folder map used for planned paths, an internal amount parser, and an answer-key Status/Subject overlay with an updated XLSX legend.

## Tasks

| Task | Name | Commit |
|------|------|--------|
| 1 | Token totals on LlmResult and PlanCallOutcome | 90ebc4c |
| 2 | Custodian folder map, amount parser and answer-key overlay | 118c5cb |

## What Was Built

- **Tokens:** `LlmGateway.GenerateAsync` sums `Math.Max(0, ...)` input/output tokens over every attempt (truncation and invalid-JSON retries included) and passes them to `BuildResult`; `PlanCallRunner.RunAsync` copies them onto `PlanCallOutcome` on success, refusal/other failures and fatal (Auth/SpendLimit) stops. Timeouts report 0.
- **Folders:** `PlanNumbering.CustodianFolders(guide)` returns `BuildFolders(guide)` unchanged.
- **Amounts:** `CanonicalValueScanner.TryParseAmount` is now `internal` (body unchanged).
- **Answer key:** `StatusGenerated`, `StatusGeneratedWithWarnings`, `StatusFailed`, `StatusSkipped`; `AnswerKeyOverlay { ByDocId }` / `AnswerKeyOverlayEntry { Status, Subject }`; `Build(plan, guide, AnswerKeyOverlay? overlay = null)`. Legend: "Status: Planned, Generated, Generated - with warnings, Failed or Skipped. ...". `CorpusApproval.BuildFiles` unchanged.

## Verification

- `dotnet test --filter "FullyQualifiedName~Ai|FullyQualifiedName~Corpus"`: 1038 passed.
- `dotnet test --filter "Category!=Render"`: 2047 passed, 0 failed.
- `dotnet build ReelDiscovery.csproj`: 0 CS warnings.
- All acceptance greps match (InputTokens property = 1, `inputTotal += ` = 1, PlanCallRunner `InputTokens = ` = 3, overlay signature = 1, old legend text = 0).

## Deviations from Plan

None in behavior. Additions within the planned files:
- Extra tests: invalid-JSON retries counted in token totals; blank overlay Status falls back to Planned; `$250,000` amount parse; status constant values; no-overlay and empty-overlay output identical to the baseline.
- The overlay test selects the first email with an attachment from `EvidenceTestData.ApprovedCase()` instead of hard-coding `RD-000003`, so it stays valid if fixture numbering shifts.
- A null overlay entry is ignored (defensive `o != null`).

## TDD Gate Compliance

Both tasks were `tdd="true"` but each landed as one `feat` commit with its tests; no separate RED `test(...)` commits. The new members (token properties, overlay types, internal methods) had to exist for the tests to compile.

## Known Stubs

None.

## Self-Check: PASSED
- AnswerKeyOverlayTests.cs exists; commits 90ebc4c and 118c5cb are in git log.
