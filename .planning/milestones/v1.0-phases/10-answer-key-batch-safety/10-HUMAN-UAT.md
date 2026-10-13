---
status: partial
phase: 10-answer-key-batch-safety
source: [10-VERIFICATION.md]
started: 2026-10-13T00:00:00Z
updated: 2026-10-13T00:00:00Z
---

## Current Test

[awaiting human testing — user deferred with "defer" on 2026-10-13]

## Tests

### 1. Real-Excel relabel round trip
expected: A Relevance relabel with a Rationale, saved by Excel as .xlsx, as UTF-8 CSV, as ANSI CSV and in a `;` list-separator locale, survives Continue, Regenerate Thread, reopen and Start Over. It shows in both key files and in the Statistics sheet. Watch for WR-03: the rationale must not come back garbled after an ANSI CSV save. The steps are in 10-11-SUMMARY.md.
result: [pending]

### 2. 150% DPI check of the relabel copy
expected: At 150% Windows scaling, the Re-plan Corpus dialog ending with "N manual relabels in the answer key will be dropped." and the Corpus Settings notice with the same clause wrap fully, with nothing clipped or ellipsized. Render tests ran only at 120 DPI. Optional 150% items: the Start Over and Regenerate Thread confirmations, the damaged-key backup notice, the blind-review tooltip, the missing-key and refusal lines, and the other-provider banner clause.
result: [pending]

### 3. Live cross-provider batch resolution (optional)
expected: With both keys present, a pending batch from the other provider is collected or cancelled at run start and its items are not resubmitted. With that provider's key removed, the run refuses to start.
result: [pending]

## Summary

total: 3
passed: 0
issues: 0
pending: 3
skipped: 0
blocked: 0

## Gaps
