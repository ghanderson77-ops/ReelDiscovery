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
