---
phase: 05-relevant-evidence-generation-narrative-ledger
plan: 09
subsystem: evidence-rendering
tags: [validator, consistency-checks, knowledge-gate, ledger, canonical-values, generated-regex]
requires:
  - 05-01 (RenderViolation, LengthBand, LedgerKind, LedgerEntry, LedgerDelta)
  - 05-02 (CanonicalValueScanner.TryParseAmount)
  - 05-03 (RenderAssignments.Spec)
  - 05-04 (KnowledgeGate.Knowability / KnowsValue / Ancestors, LedgerMerger.NormalizeAbout / SameValue)
  - 05-05 (PeopleDirectory)
  - 05-08 (ThreadRenderContext, RenderedThread from RenderRepair, repair note wording)
provides:
  - Services/Evidence/RenderValidator (Check, Run, VariantWindow, OverLengthFactor, UnderLengthFactor, LegalTerms, GroupGreetingWords)
affects: [generation orchestrator render loop (accept / retry with feedback / keep with warnings), Activity grid violation texts]
tech-stack:
  added: []
  patterns: [per-rule try/catch isolation, CheckContext snapshot, GeneratedRegex with match timeout, whole-token matching]
key-files:
  created:
    - Services/Evidence/RenderValidator.cs
    - ReelDiscovery.Tests/Evidence/RenderValidatorTests.cs
  modified: []
decisions:
  - "[Phase 05] V3 identifier registration is a whole-token match against non-amount canonical values and ledger values; CanonicalValueScanner.IsRegistered is not used because its containment test accepts CO-0071 as CO-007"
  - "[Phase 05] V4 checks only amounts and digit-bearing values (numbers, dates, identifiers); a value linked to the message's own plan facts, or seen in the subject or a quoted ancestor body, is never flagged; ledger entries with empty KnownByIds count as public"
  - "[Phase 05] V6 body dates match a sentence by the About's head phrase (words before the first function word, e.g. 'site walk') when it has two or more words, else by every significant word; a sentence that also states the ledger date (a reschedule) is not flagged"
  - "[Phase 05] V6 body amounts are skipped when they equal any canonical amount or any ledger value; V2 bigrams run per line and skip weekday, month and common capitalized words as surnames"
metrics:
  duration: 14min
  completed: 2026-10-03
  tasks: 2
  files: 2
---

# Phase 5 Plan 09: Render Validator Summary

`RenderValidator.Check(RenderedThread, ThreadRenderContext)` runs code-only checks V1-V8 with per-rule isolation. It returns UI-ready plain-English violations: a canonical amount off by a few dollars, a value the sender cannot know yet, a detail that contradicts an earlier email, an unknown person, legal framing outside counsel threads, or a grossly wrong length. A test file built mostly from realistic negative samples keeps these checks from burning retries.

## Tasks

| Task | Name | Commits |
|------|------|---------|
| 1 | Structure, people, legal wording and length checks (V1, V2, V5, V7, V8) | 8b711c6 (test), ba2c435 (feat) |
| 2 | Value, knowledge and ledger checks (V3, V4, V6) | 45baaec (test), b242936 (feat) |

## What Was Built

- **Check / Run**:
  - `Check` builds a null-safe `CheckContext` snapshot: planned and rendered messages by seq, the people directory (rebuilt from the guide when `ctx.People` is null), name sets, canonical values and the ledger view.
  - Each rule runs through `Run`, which materializes the rule's output inside a try/catch, so a rule that throws, even part-way through a lazy sequence, adds nothing and the other rules still report.
  - Violations are de-duplicated by code, seq and text. Null inputs return an empty list.
- **V1 / V5**: `has 3 messages; the plan has 4` (Seq 0). An empty body gives `message 2 is empty`. When the repair note `msg 2: removed quoted text` exists, it gives `message 2 is empty after removing quoted text` instead.
- **V2**, all reported as `names "X", who is not in the cast` unless noted:
  - (a) `peopleNamed` entries that are not a person id or a known first, last or full name. An honorific prefix and a possessive `'s` are ignored.
  - (b) Overlapping same-line `First Last` pairs where First is a known first name and Last is unknown.
  - (c) `Mr./Ms./Mrs./Dr. X` with an unknown name.
  - (d) A first-line greeting to a known person who is not in To/Cc, reported as `greets "Raymond", who is not on this email`. Group words and unknown non-person names are ignored.
- **V3**:
  - A scanned amount within ±15% of a canonical Amount, but not equal to any canonical amount, gives `uses $184,520 for Change Order 7; the case value is $184,250`. Only the closest canonical amount is reported.
  - A scanned identifier with a canonical prefix (`CO-`, `INV-`…) that is not registered gives `uses CO-0071, which is not a case document number`.
- **V4**, for amounts and digit-bearing values outside the sender's knowledge set at the message's local date:
  - Gives `mentions {Label}, dated after this email`, or `uses {Value}, which {FirstName} does not know yet`.
  - Not flagged when the value is in the subject or a quoted ancestor body, or is linked to the message's own plan facts.
  - Ledger values whose KnownByIds excludes the sender give the same "does not know yet" text.
- **V6**, each reported as `gives {about} as {new}; earlier emails say {old}`:
  - A delta with the same Kind and normalized About as a visible entry but a different value (`LedgerMerger.SameValue`).
  - A body amount within ±15% of a ledger Figure that is not equal to any canonical or ledger amount.
  - A body date (ISO or en-US `April 9` / `Apr 9, 2024`) in a sentence about the subject of an earlier Meeting or Commitment that differs from its ledger date.
- **V7** (non-privileged threads only): whole-word, case-insensitive legal terms, the case-sensitive `Count [IVX]+` pattern, and the core document title when it is 8 or more characters. Gives `uses legal wording ("plaintiff")`.
- **V8**: more than 2× the band maximum, or under ½ the minimum for the Long band, gives `message 1 is 130 words; target is 8-60`.

## Verification

- `dotnet test --filter "FullyQualifiedName~RenderValidatorTests"`: 77 passed. This includes 15 V2 negatives, 5 V7 negatives, and 6 V3, 5 V6-date and 2 V6-amount negatives.
- `dotnet test --filter "FullyQualifiedName~ReelDiscovery.Tests.Evidence&Category!=Render"`: 400 passed.
- Full suite: 2509 passed, 0 failed. The app build has no warnings.
- Acceptance greps:
  - Count is 1: the Check signature, `KnowledgeGate.KnowsValue`, `LedgerMerger.SameValue`, `dated after this email`, `earlier emails say` and `who is not in the cast`.
  - `GeneratedRegex` appears 9 times and `VariantWindow` 3 times.
  - `new Regex(` does not appear.

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 1 - Bug] Stricter identifier registration than CanonicalValueScanner.IsRegistered**
- **Found during:** Task 2
- **Issue:** `IsRegistered` treats an identifier as registered when either text contains the other, so `CO-0071` counts as registered because it contains `CO-007`. That contradicts the required V3 behavior.
- **Fix:** Added `CheckContext.IsRegisteredIdentifier`, an equal or whole-token match (letters, digits and hyphens end a token) against non-amount canonical values and ledger values. Ledger values also count, so identifiers established by earlier emails are not flagged.
- **Files modified:** Services/Evidence/RenderValidator.cs
- **Commit:** b242936

**2. [Rule 2 - Correctness / Pitfall 5] V4 narrowed to specific values**
- **Issue:** The plan's "other kinds via containment when Value length ≥ 4" rule would flag shared names. For example, Lakeview Tower is linked to F-001, which Tom does not know, so every mention of the project name by Tom would be flagged.
- **Fix:**
  - V4 considers only amounts and values that contain a digit (numbers, dates, identifiers).
  - The thread subject counts as seen text.
  - Values linked to the message's own plan facts are allowed, because the prompt provided them.
  - Ledger entries with no KnownByIds are treated as public.
- **Commit:** b242936

**3. [Rule 2 - Correctness / Pitfall 5] V6 body-date subject matching uses the About head phrase**
- **Issue:** The plan says "every significant word (length ≥ 4)" of the About. For "site walk with the glazing crew" that requires "with", "glazing" and "crew", so the required positive sample ("the site walk is now April 9") would not match.
- **Fix:** The match uses the head phrase (words before the first function word) when it has two or more words, and falls back to every significant word otherwise. A sentence that also states the ledger date is treated as a reschedule and not flagged.
- **Commit:** b242936

## TDD Gate Compliance

Each task has a `test(05-09)` RED commit (compile failure, then 11 failing tests for Task 2) followed by a `feat(05-09)` GREEN commit. No refactor commits were needed.

## Known Stubs

None.

## Threat Flags

None. Regex scans use GeneratedRegex with a 1-second match timeout over bodies already capped at 6,000 characters (T-05-24), and every rule is isolated by `Run` (T-05-25).

## Self-Check: PASSED
