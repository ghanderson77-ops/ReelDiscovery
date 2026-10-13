---
status: passed
phase: 02-case-scenario-core-document
source: [02-VERIFICATION.md]
started: 2026-10-01T00:00:00Z
updated: 2026-10-13T00:00:00Z
---

## Current Test

[awaiting human testing]

## Tests

### 1. Scenario step and complexity preset
expected: In the case-driven flow the step header reads "Step 2 of 3"; switching Simple/Standard/Complex visibly changes the preset summary (parties, counts, characters, timeline span, length); the character counter turns DarkOrange above 7,200 and Red above 8,000, and Next rejects an empty or over-length scenario.
result: pass (fixed in 113e0c3; retested by user 2026-10-13)

### 2. Real-name warning and substitution
expected: A scenario naming a real company/person shows the "Real Names Found" dialog with every row ticked by default; unticking a row asks "Keep Real Names?" (default No); substitutes appear in the generated document; a kept name stays unchanged; a substitute that still contains the real name is rejected; "Review real names" reopens the dialog.
result: pass (fixed in 113e0c3; retested by user 2026-10-13)

### 3. Live document structure (Claude and OpenAI)
expected: A civil scenario produces a Complaint/Petition and a criminal/regulatory scenario produces an Investigation Memo, each with full structure (caption with no judge, parties, jurisdiction/venue, consecutively numbered facts, counts with incorporation paragraphs, lettered prayer, jury demand, signature — or memo header/equivalent), one consistent court/state, and length within the chosen preset. Run on both providers.
result: pass (user-reported live testing, 2026-10-13)

### 4. Edit, regenerate and Finish gating
expected: Editing a section and clicking Apply Edits updates the numbered preview; Regenerate Section with guidance rewrites only that section; Regenerate All asks first; pasting "531 U.S. 98" into a section produces a citation Error and blocks Finish until removed; closing shows the "Close Case" confirmation.
result: pass (user-reported live testing, 2026-10-13)

### 5. Failure, cancel and close paths
expected: Retry Section rewrites only the selected failed section; Resume All completes remaining Pending/Failed sections without overwriting hand-edited text; the empty state explains a cancel or fatal stop; closing the window during the real-name check or during generation throws no exceptions (Cancel check link works).
result: pass (user-reported live testing, 2026-10-13)

## Summary

total: 5
passed: 5
issues: 0
pending: 0
skipped: 0
blocked: 0

## Gaps

- truth: "Scenarios inspired by fictional works keep their character and company names"
  status: resolved
  reason: "User reported: giving it a fictional scenario and then it's further fictionalizing it"
  fix: "113e0c3 — detector lists only real-world people/companies/brands; FictionalWork* kinds removed"
- truth: "New Phase 2 screens render without clipping at high display scaling"
  status: resolved
  reason: "User reported: UI is fucked up (clipped radios, combos, dialog banner at 150%)"
  fix: "113e0c3 — content-sized rows; dialog scaled by DPI; verified by 144-DPI renders"
