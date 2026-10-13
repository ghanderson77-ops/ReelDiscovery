---
phase: 07-scale-cost-qa-cutover
verified: 2026-10-08T00:00:00Z
status: passed
score: 5/5 roadmap success criteria verified in code and tests (2 provider-live checks outstanding)
overrides_applied: 0
human_verification:
  - test: "With an Anthropic key stored, run REELDISCOVERY_LIVE_SPIKE=1 dotnet test ReelDiscovery.Tests/ReelDiscovery.Tests.csproj --filter \"Category=LiveSpike\" (Anthropic evidence-prefix caching, spike part c)"
    expected: "Repeated evidence calls on claude-sonnet-5-5 report cache_creation_input_tokens on the first call and cache_read_input_tokens > 0 on later calls. The measured stable prefix is at least the model's MinCacheablePrefixTokens (512)."
    why_human: "Needs a paid Anthropic key. 07-22 spike part (c) was not run, so research A3 and GenerationCostEstimator.StablePrefixTokens = 600 are still [ASSUMED]. If the system prompt plus schema prefix is below Anthropic's minimum, evidence calls on Claude are never cached and the live estimate overstates cache savings."
  - test: "With an Anthropic key stored, run a real Anthropic Message Batch round trip (the LiveSpike batch test, or a small case with 'Use batch pricing' on and Claude as text provider), including closing and reopening the app while the batch is pending"
    expected: "The batch is submitted, appears in batch-ledger.jsonl, is collected after restart without resubmission, and results/usage are billed at the batch discount"
    why_human: "Only the OpenAI batch transport was exercised live (27m38s round trip). AnthropicBatchTransport is covered by wire tests only; SC1 names both providers."
---

# Phase 7: Scale, Cost, QA & Cutover Verification Report

**Phase Goal:** The user can run a full 5,000-email case affordably and verify its quality, and the case-driven flow is the only flow in the app.
**Verified:** 2026-10-08
**Status:** passed (human verification items reported complete by the user, 2026-10-13)
**Re-verification:** No (initial verification; the UAT gap from test 4 was closed by 07-23/07-24 before this report)

## Goal Achievement

### Observable Truths

| # | Truth (ROADMAP SC) | Status | Evidence |
|---|-------|--------|----------|
| 1 | Batch API for OpenAI or Anthropic; batches tracked on disk; after a restart results are collected and failed/expired items re-run | VERIFIED (Anthropic is not live-tested) | `Services/Ai/BatchingLlmProvider.cs` (1555 lines): `Recover(BatchLedgerState)` rebuilds in-flight/available items from `BatchLedger`, `ResumePending()` restarts polling. `SendAsync` routes `_failedIds` (errored/expired) to live (D-10) and attaches to in-flight items instead of resubmitting. `Models/WizardState.CreateRunGateway` wraps the live provider when `useBatch` is set, using `LlmProviderFactory.CreateBatchTransport` (OpenAI and Anthropic transports). `GenerationRunner` calls `batch.ResumePending()` (l.264). Relevant waves run under `BatchStage.EnterLive()` (l.383-384, amended D-07), and only the `non-relevant mail` stage is batched (l.671). UI: `_chkUseBatch` in `StepGenerateCorpus` is saved to `Generation.UseBatch`. Tests: `Restart_PendingBatch_AttachesAndNeverResubmits`, `Restart_CollectedNotConsumed_ServedFromResultsFile`, `BatchParityTests.Restart_CancelWhileNonRelevantWaits_ContinueResumes_NoResubmit_SameJournal`. A live OpenAI batch round trip and the UAT restart test passed. |
| 2 | Prompt caching of shared case-guide context; cost display shows cache writes, cache reads and batch discounts; per-model pricing is editable | VERIFIED (Anthropic is not live-measured) | Anthropic: block-level `CacheControlEphemeral` on the byte-stable system block (`AnthropicTextProvider.BuildParams` l.243); usage maps cache_creation/cache_read. OpenAI: implicit caching kept per the live spike (1816 cached tokens per call); `ReadCacheWriteTokens` parses raw `cache_write_tokens`. Blind-review protocol is the cached system prefix. `AIModelConfig.CalculateCost` prices cache write/read and `BatchDiscountPercent`. `TokenUsageTracker` accumulates `TotalCacheWriteCost/TotalCacheReadCost/TotalBatchSavings`. `WizardForm` l.136 header tooltip uses `GenerationUiText.CostBreakdown`. `ModelConfigurationDialog` has Cache Write $/M, Cache Read $/M and Batch Discount % columns with validation. The Case Summary cost row shows the breakdown (`StepCaseSummary` l.1621). |
| 3 | A ~5,000-email run completes with the expected file and answer-key counts and no OOM | VERIFIED (simulated provider) | `ReelDiscovery.Tests/Scale/FiveThousandRunTests.cs`: `TotalEmails = 5_000`, live and batch variants. It asserts Finished, a process peak working set under 1.5 GB, a managed-memory budget, the exact EML count, answer-key rows equal to messages plus attachments, QA reports present, and batch submissions only for `non-relevant mail`. 2/2 passed in this verification (5m28s). |
| 4 | Blind review flags label disagreements for relabel/regenerate; metadata-only leakage audit with a reported result; timeline/value scans flag future facts and canonical-value contradictions | VERIFIED | `BlindReviewer` + `BlindReviewPrompts` (label-free: `KeyRelevant` is never rendered) run from `StepCaseSummary` l.1924-1958 via `BlindReviewDialog`. Disagreements are shown in `QaFindingsDialog`, whose Regenerate Thread action calls `ThreadInvalidation.InvalidateAsync` (l.2143). Relabel is a manual answer-key edit, per user decision CONTEXT l.67-68, and the copy tells the user how. `LeakageAudit` features are sender/recipient/CC counts/day/hour/month/subject length only, using cross-validated logistic regression with a Pass/Warning/Fail verdict. `CorpusScans` runs V4 timeline, V3 canonical-value and V6 ledger checks. `QaRunner.RunEndOfRunAsync` runs scans and leakage automatically at the end of a run (`StepGenerateCorpus.Run.cs` l.529) and saves them to `QaReportStore`. |
| 5 | Old topic/storyline wizard and its dead generation code are gone; the app opens directly into the case-driven flow | VERIFIED | `Program.Main`: disclaimer, then `WizardForm`; there is no flow choice, telemetry or legacy key migration. `WizardForm.StepTypes` = ApiConfiguration, CaseScenario, CoreDocument, CaseGuide, CorpusSettings, ReviewPlan, GenerateCorpus, CaseSummary (D-21). `EmailGenerator`, `StorylineGenerator`, `CharacterGenerator`, `OpenAIService`, `ThemeGenerator` and the `StepTopicInput/StepStorylines/StepCharacters/StepGenerationConfig/StepGeneration/StepComplete` files no longer exist. `PlatformTests` l.87-94 asserts these types are absent. |

**Score:** 5/5 truths verified. Each also has a passing automated test.

### Required Artifacts (key)

| Artifact | Status | Details |
|----------|--------|---------|
| `Services/Ai/BatchingLlmProvider.cs`, `BatchLedger.cs`, `BatchStage.cs` | VERIFIED | Substantive; wired through `WizardState.CreateRunGateway` and `GenerationRunner.Batch` |
| `Services/Ai/AnthropicBatchTransport.cs`, `OpenAIBatchTransport.cs` | VERIFIED | Created by `LlmProviderFactory.CreateBatchTransport` |
| `Services/Corpus/GenerationCostEstimator.cs` | VERIFIED | Batch total prices relevant work live and non-relevant work at batch prices (07-24) |
| `UserControls/ModelConfigurationDialog.cs` | VERIFIED | Cache and batch price columns |
| `Services/Qa/*` (BlindReviewer, LeakageAudit, CorpusScans, QaRunner, QaReportStore) | VERIFIED | Wired to StepCaseSummary and StepGenerateCorpus |
| `Forms/BlindReviewDialog.cs`, `Forms/QaFindingsDialog.cs` | VERIFIED | Opened from StepCaseSummary |
| `ReelDiscovery.Tests/Scale/FiveThousandRunTests.cs` | VERIFIED | 2/2 pass |

### Key Link Verification

| From | To | Via | Status |
|------|----|-----|--------|
| StepGenerateCorpus.Run | BatchingLlmProvider | `_state.CreateRunGateway(useBatch, folder, planId)` -> `GenerationRunner.Batch` | WIRED |
| GenerationRunner relevant waves | live provider | `BatchStage.EnterLive()` | WIRED |
| GenerationRunner Stage B | batch transport | `BatchStage.Enter(NonRelevantStageLabel)` | WIRED |
| Provider usage | header tooltip | TokenUsage -> TokenUsageTracker -> `GenerationUiText.CostBreakdown` | WIRED |
| End of run | QA reports | `QaRunner.RunEndOfRunAsync` -> `QaReportStore` | WIRED |
| QaFindingsDialog | thread regeneration | `ThreadInvalidation.InvalidateAsync` | WIRED |

### Behavioral Spot-Checks (run in this verification, `--artifacts-path obj/gsd-verify-*`)

| Check | Result | Status |
|-------|--------|--------|
| `dotnet build ReelDiscovery.csproj -warnaserror` | 0 warnings, 0 errors | PASS |
| `dotnet build ReelDiscovery.Tests/ReelDiscovery.Tests.csproj -warnaserror` | 0 warnings, 0 errors | PASS |
| `dotnet test --filter "Category!=Render&Category!=Scale"` | 3766/3766 passed (1m56s) | PASS |
| `dotnet test --filter "Category=Render"` | 135/135 passed (8m38s) | PASS |
| `dotnet test --filter "Category=Scale"` | 2/2 passed (5m28s) | PASS |

### Requirements Coverage

| Requirement | Source Plans | Status | Evidence |
|-------------|-------------|--------|----------|
| RUN-03 | 07-06..11, 21-24 | SATISFIED | Truth 1; live OpenAI batch round trip; Anthropic transport is wire-tested only (human item 2) |
| RUN-04 | 07-07, 08, 10, 11, 22 | SATISFIED | Ledger replay and resume; restart tests; UAT restart |
| RUN-05 | 07-02, 06, 16, 22 | SATISFIED (Anthropic is not measured) | Truth 2; human item 1 |
| PROV-07 | 07-01, 02, 05, 06, 11, 12, 18, 22 | SATISFIED | Truth 2 |
| QA-01 | 07-13, 16, 17, 18, 22 | SATISFIED | Truth 4 (relevance changes are manual edits by user decision) |
| QA-02 | 07-03, 14, 15, 17, 18, 22 | SATISFIED | Truth 4 |
| QA-03 | 07-13, 15, 17, 18, 22 | SATISFIED | Truth 4 |
| OUT-03 | 07-03, 04, 19, 20, 21, 22 | SATISFIED | Truth 5 |
| PLAT-05 | 07-04, 19, 20, 22 | SATISFIED | Truth 5 |

No orphaned requirements. All 9 phase IDs are claimed by plans and marked Complete in REQUIREMENTS.md.

### Anti-Patterns Found

| File | Line | Pattern | Severity | Impact |
|------|------|---------|----------|--------|
| `Helpers/LoadingOverlay.cs` | 17-45, 58-70 | Leftover legacy `Storylines`/`Characters` loading types and message arrays. Only `EntityCheck` is used (`StepCaseScenario` l.271). | Info | Small dead-code leftover from the legacy flow (PLAT-05). Does not block. |
| `Services/Evidence/GenerationRunner.cs` / `BatchingLlmProvider.cs` | - | Review WR-A06 (orphan batch spend billed only on Finished) and WR-A08 (live scopes ignore items in flight in resumed batches) were skipped on purpose | Warning | Small cost-accounting / duplicate-spend edge cases after cancel or restart. Documented in 07-REVIEW-FIX.md as needing a design decision. |
| `Services/Corpus/GenerationCostEstimator.cs` | 126-127 | `StablePrefixTokens = 600 [ASSUMED]` | Warning | The live estimate's Anthropic cache credit is unmeasured (human item 1) |

No TODO/FIXME/NotImplementedException in app code.

### Human Verification Required

1. **Anthropic evidence-prefix caching (spike part c).** Store an Anthropic key and run `REELDISCOVERY_LIVE_SPIKE=1 dotnet test ReelDiscovery.Tests/ReelDiscovery.Tests.csproj --filter "Category=LiveSpike"`. Expected: cache write on the first call, cache reads on later calls, and a prefix at or above the 512-token model minimum. Why human: needs a paid key. If the prefix is too small, the D-19-safe case-directory prefix (research Pattern 5) is the follow-up.
2. **Anthropic batch round trip with restart.** Expected: submit, ledger entry, collection after an app restart with no resubmission, and batch-discounted billing. Why human: only OpenAI was run live.

### Gaps Summary

No blocking gaps. All five roadmap success criteria are implemented, wired and covered by passing tests. The UAT blocker (per-wave batch latency) is closed in code: relevant waves run under `BatchStage.EnterLive()`, and the estimator and copy were updated. Two items remain open because no Anthropic key was available: live Anthropic prompt caching and a live Anthropic batch round trip. The Anthropic code paths exist and are wire-tested. Minor issues are the leftover legacy LoadingOverlay types and the two intentionally skipped review warnings.

---

_Verified: 2026-10-08_
_Verifier: Claude (gsd-verifier)_


---

**Human verification closed 2026-10-13:** the user reported completing live testing of the items above; see the phase HUMAN-UAT file.
