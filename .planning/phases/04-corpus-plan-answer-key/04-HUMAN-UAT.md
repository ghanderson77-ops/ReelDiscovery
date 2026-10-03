---
status: passed
phase: 04-corpus-plan-answer-key
source: [04-VERIFICATION.md]
started: 2026-10-03T00:00:00Z
updated: 2026-10-03T00:00:00Z
---

## Current Test

[complete — approved 2026-10-03]

## Tests

### 1. CR-02 — reopening never re-plans by itself
expected: Reopen a case at Review Plan with an unapproved plan whose corpus settings changed (also: with an edited guide, and with a dismissed banner). No planning run starts; the correct banner shows, and its wording does not claim the plan was approved.
result: passed (user approved 2026-10-03)

### 2. WR-09 — late Cancel during planning
expected: Cancel is disabled once planning returns; a late confirmed cancel discards the new plan (previous plan kept, D-18); a click after the plan is applied shows the "too late" status.
result: passed (user approved 2026-10-03)

### 3. WR-06 — decoys stay off the alleged conduct
expected: With a live provider, decoy rows in the answer key share project names and keywords with the case but never paraphrase a key fact.
result: passed (user approved 2026-10-03)

## Summary

total: 3
passed: 3
issues: 0
pending: 0
skipped: 0
blocked: 0

## Gaps
