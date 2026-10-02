---
status: partial
phase: 03-case-guide
source: [03-VERIFICATION.md, 03-20-PLAN.md]
started: 2026-10-01T00:00:00Z
updated: 2026-10-01T00:00:00Z
---

## Current Test

[awaiting human testing]

## Tests

### 1. Live "The Office" run (Claude and OpenAI): names kept, counsel + background staff, believable emails
expected: Cast keeps scenario names (Michael Scott, Oscar Martinez, David Wallace ...); each represented org has in-house + outside counsel (partner + associate); background staff per org; emails like michael.scott@dundermifflin.com (domain from the org's short name).
result: [pending]

### 2. Word exports open cleanly
expected: Export All to Word on Case Summary; all three .docx open in Word with no repair prompt and look professional.
result: [pending]

### 3. Reopen lands on the furthest step
expected: Close (no discard prompt), relaunch, open the case from Recent cases or Open Case...; lands on the furthest step; a partial guide shows Resume Guide.
result: [pending]

### 4. Close during a live guide run keeps finished work (WR-01)
expected: Closing mid-run cancels, waits briefly, saves; reopening shows the finished parts/units.
result: [pending]

### 5. Regenerate the core document with reordered parties / retitled count (WR-03)
expected: Organizations keep their IDs/domains/cast; facts linked to a retitled count are flagged for review; Update Guide works.
result: [pending]

### 6. Visual quality (D-04) at your scaling
expected: Case Guide (all tabs, banner, Checks, state cards), Case Summary, Describe the Case with Recent cases, Review Core Document: aligned edges, segmented tabs, no clipped text, no stock grey surfaces.
result: [pending]

## Summary

total: 6
passed: 0
issues: 0
pending: 6
skipped: 0
blocked: 0

## Gaps

## UAT gap fixes (2026-10-02)

### Individual parties became their own organizations — resolved (0ed4eff)
- reported: The Office run created organizations "Ryan Howard" (howardfamily.com), "Michael Scott" and "David Wallace" and filed each person there instead of at Dunder Mifflin.
- fix: individual parties are people, not organizations. `CastMember.PartyId` links a key character to the party they are; `SyncOrganizations` no longer creates organizations for individual parties; the roster prompt lists them separately ("placed at the organization they work for ... Never create an organization named after a person"); after the roster they are linked by name, and only an unmatched party gets a personal organization (a private individual with no employer). Regenerate Cast / Update Guide on an older guide moves the people to their employer (email and signature recomputed) and removes the empty person-named organizations. Counsel rules cover Party organizations and personal organizations only; validator rule 24 now reports "Party X is not in the cast" for a missing individual.
- retest: Regenerate Cast (or Update Guide) on the saved Office case; expect ryan.howard@dundermifflin.com etc. and no person-named organizations.

### Character active ranges did not cover their facts — resolved (ba4ecdd)
- reported: Errors like "F-001 is dated 2007-01-08, but David Wallace (CH-003) is only active 2007-03-01 to 2008-07-31."
- fix: `CaseGuideEditing.WidenActiveRanges` widens every generated (not hand-edited) character's range to cover each fact they did or knew about. Runs after fact generation, Regenerate Cast/Facts, single-entry regeneration, Update Guide, and once when the Case Guide step opens an existing guide (autosaved). Hand-edited characters are left for the validator to flag.
- retest: reopen the saved Office case; the active-range Errors for generated characters are gone.

### Errors blocked Next without saying how to fix them — resolved (36d7803, ea140cb)
- reported: "How can I clear it in the future for errors? It won't let me progress and doesn't tell me how to fix it." Next showed a bare "Fix the Guide First" message; double-click navigation in Checks existed but was undiscoverable.
- fix: every validator finding carries a plain-language `FixHint` naming the entity and value (every Error has one) and a `GuideQuickFix`. `CaseGuideQuickFix` applies the automatic ones with no model call: new IDs, remove broken links (facts, values, firms), widen/repair a character's active range (even hand-edited), normalize unambiguous dates, rebuild an email / give an organization its own domain, add actors under Who knew, replace real names, link a party to the same-named character or organization, mark reviewed. Model fixes (Regenerate Fact/Character/Value, Regenerate Cast..., Regenerate Values..., Retry Issue Details, Update Guide, Resume Guide) start the existing runs with their usual confirmations.
- UI: Checks rows show the fix under each check; "Double-click a check to go to it." beside the caption; Fix (selected check; Primary when automatic, Default while the stale banner's Update Guide is up) and Fix All ({n}) with a confirmation listing counts (default Cancel). Double-click/Enter opens the tab, selects the entry and focuses the field. The card shows two whole checks, one on a short window, and folds only when even one would crowd the body. Next with errors opens a dialog listing up to 5 errors with their fixes: Show Checks (default), Continue Anyway (asks once, default No), Fix Automatically ({k}) (applies, then re-checks the gate). M1 now says "Click Resume Guide to write ..., then click Next again."
- retest: on a guide with errors, click Next: the dialog lists each error and its fix; Fix Automatically clears active-range / broken-link errors and Next proceeds (or asks about warnings). In Checks, select a check and click Fix; try Fix All.
