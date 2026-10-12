---
status: partial
phase: 09-edit-safety-navigation
source: [09-VERIFICATION.md]
started: 2026-10-12T00:00:00Z
updated: 2026-10-12T00:00:00Z
---

## Current Test

[awaiting human testing — user deferred the 150% walkthrough with "finish it" on 2026-10-12]

## Tests

### 1. 150% DPI walkthrough of the new Phase 9 surfaces
expected: At 150% Windows scaling, nothing is clipped or ellipsized, and banners wrap fully on these surfaces: the Case Guide dependency line and the PlanDependency banner; the Generate Corpus GuideChanged and NoPlan cards; the Case Summary Mailboxes row ("Open Mailboxes Folder" + "Generate Corpus"); the Review Plan No-plan/Failed cards with Skip; the Corpus Settings skip header; and the Discovery Documents Exhibits-stale row. The steps are listed in 09-10-SUMMARY.md (steps 1-10). Render PNGs exist only at 120 DPI.
result: [pending]

### 2. Guide-change gate end to end in the running app
expected: After editing the guide, Generate Corpus shows "The case guide changed after this plan was built". It shows when reached directly, through Case Summary's button, and after closing and reopening the case. On Review Plan, Next offers "Continue Without Approving" (no approval). Keep This Plan followed by Approve Plan restores the dashboard.
result: [pending]

### 3. No paid planning on pass-through; Discovery reachable without a plan
expected: Going Back to Review Plan with no plan starts no planning run. Skip to Discovery Documents (Review Plan and Corpus Settings) reaches Discovery Documents with no model call.
result: [pending]

### 4. WR-02 design decision (missing journal = all cited emails deleted)
expected: The user accepts the current behavior, or asks for a Start Over marker so that an unexplained missing journal reads as "unknown" instead of Out of date.
result: [pending]

## Summary

total: 4
passed: 0
issues: 0
pending: 4
skipped: 0
blocked: 0

## Gaps
