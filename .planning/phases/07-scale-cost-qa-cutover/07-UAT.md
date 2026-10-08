---
status: partial
phase: 07-scale-cost-qa-cutover
source: [07-10-SUMMARY.md, 07-11-SUMMARY.md, 07-19-SUMMARY.md, 07-22-SPIKE-NOTES.md]
started: 2026-10-08T08:45:00
updated: 2026-10-08T11:00:00
---

## Current Test

number: 4
name: Batch run with restart (07-22 Task 4 step 4)
expected: |
  Batch mode finishes a small case in a practical time.
awaiting: user re-run of 07-22 Task 4 step 4 (gaps closed by 07-23 and 07-24)

## Tests

### 4. Batch run with restart
expected: Ticking "Use batch pricing" on a ~445-thread case (59 relevant in 24 waves) produces a corpus in a practical time.
result: issue
reported: "Is each wave going to take up to 24 hours?" (screenshot: wave 1 waiting on provider batch, 0 of 4 done; 23 relevant waves queued behind it)
severity: blocker

## Gaps

- truth: "In batch mode, relevant waves run live and only non-relevant mail and its attachments are batched (amended D-07), so a batch run is not serialized behind one provider batch cycle per relevant wave"
  status: resolved
  reason: "User reported: Is each wave going to take up to 24 hours? 24 relevant waves each submitted as a separate batch; observed OpenAI batch ~28 min, SLA 24 h"
  resolved_by: [07-23, 07-24]
  resolution: "07-23 runs relevant waves live in batch mode and batches only non-relevant mail; 07-24 prices the batch total the same way and updates the Ready-card and UI-SPEC copy. Needs the user re-run of test 4."
  severity: blocker
  test: 4
  root_cause: "GenerationRunner batch mode (07-10) labels every relevant wave 'wave {i}' and submits its first attempts through BatchingLlmProvider; each wave waits for its batch before the next starts (original D-07). Relevant-wave calls (thread renders AND their attachments) must run under BatchStage.EnterLive() in batch mode; only the non-relevant stage (and its attachments) stays batch-eligible."
  artifacts: ["Services/Evidence/GenerationRunner.cs", "Services/Ai/BatchingLlmProvider.cs (BatchStage.EnterLive)", "Services/Corpus/GenerationCostEstimator.cs (batch total must price relevant work live)", "Services/Evidence/GenerationRunState.cs (EstimateRemaining batch flag)", "UserControls/GenerationUiText.cs (Ready-card hint 'can take several hours' copy, batch clauses)", "ReelDiscovery.Tests/Evidence/BatchParityTests.cs", "ReelDiscovery.Tests/Scale/FiveThousandRunTests.cs (one submission per wave label assertion)", ".planning/phases/07-scale-cost-qa-cutover/07-UI-SPEC.md (S1 batch copy)"]
  missing: ["Relevant waves forced live in batch mode", "Batch total estimate = relevant live + non-relevant batch", "Tests: no relevant-wave thread or attachment call reaches the batch transport; parity still holds; scale batch variant asserts batch submissions only for non-relevant stage", "Ready-card/UI-SPEC copy updated to the amended behavior"]
  debug_session: ""

- truth: "The media-gating warning on the API Configuration step does not tell the user to go to the step they are already on"
  status: resolved
  reason: "Found during 07-22 handover: API Configuration step shows 'Add one on the API Configuration step to enable them.' while on that step"
  resolved_by: [07-24]
  resolution: "API step now reads 'Images and voicemail need a tested OpenAI key. Enter and test one above to enable them.'"
  severity: cosmetic
  test: 1
  root_cause: "WizardState media gating message (Models/WizardState.cs ~line 50) is shared by the API step and later steps"
  artifacts: ["Models/WizardState.cs", "UserControls/StepApiConfiguration.cs", "ReelDiscovery.Tests/MediaGatingTests.cs"]
  missing: ["Step-appropriate wording: on the API step e.g. 'Images and voicemail need a tested OpenAI key.'; elsewhere keep the pointer to the API Configuration step"]
  debug_session: ""
