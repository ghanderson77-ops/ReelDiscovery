---
phase: 08-ancillary-discovery-documents
plan: 13
subsystem: discovery-written
tags: [discovery, rfp, rog, rfa, keep-best, retry, ANC-02, ANC-03, ANC-04, ANC-05]
requires:
  - phase: 08-04
    provides: "WrittenSetPlan / RequestSlot / WrittenDiscoveryPlanner.MaxSlotsPerCall"
  - phase: 08-07
    provides: "DiscoveryBoilerplate caption, preliminary statement, definitions, instructions, label, signature, certificate, service date"
  - phase: 08-08
    provides: "DiscoveryValidator.CheckRequests / IsBetter / FeedbackHeader"
  - phase: 08-11
    provides: "DiscoveryPrompts.BuildWrittenPrompt / RequestSetDto / OperationWritten / MaxOutputTokens"
provides:
  - "WrittenSetWriter.WriteAsync(draft, plan, caseNumber, systemPrompt, guidance, run, onPart) -> WrittenSetWriteOutcome"
  - "WrittenSetWriteOutcome { Content, Warnings, Error, Refused, TimedOut, NotRun, Cost }"
  - "WrittenSetWriter.MaxAttempts = 3"
affects: [08-14, 08-15]
tech-stack:
  added: []
  patterns:
    - "Batched slot loop (25 per call): PlanCallRunner per attempt, CheckRequests, feedback retry, keep-best via DiscoveryValidator.IsBetter"
    - "Code-owned request structure: label/prefix stripping, unknown slots dropped, consecutive renumbering, keys copied from slots"
key-files:
  created:
    - Services/Discovery/WrittenSetWriter.cs
    - ReelDiscovery.Tests/Discovery/WrittenSetWriterTests.cs
  modified: []
key-decisions:
  - "08-13: A refusal or timeout on a batch is retried (up to 3 attempts) while no usable attempt exists; any batch with no usable attempt fails the whole set (mirrors DepositionWriter)"
  - "08-13: Model texts that are empty after label/prefix cleanup are dropped before validation, so the slot reads as missing (D4) and is omitted from the set"
  - "08-13: The duplicated RFA prefix is stripped with or without its trailing comma, repeatedly; stored Text is prefix + space + model text"
  - "08-13: Label stripping accepts the set's own RequestLabel plus any short '<words> No. n:' lead-in"
duration: 10min
completed: 2026-10-08
---

# Phase 8 Plan 13: WrittenSetWriter Summary

`WrittenSetWriter` writes one RFP, ROG or RFA set. It sends the plan's slots in calls of at most 25 on the Story tier, and every call uses the same system prompt. Each call is checked with `DiscoveryValidator.CheckRequests` and retried with feedback up to 3 times, and the best attempt is kept. Code, not the model, owns request numbers, keys, RFA prefixes and all boilerplate.

## Tasks

| Task | Name | Commits | Files |
|------|------|---------|-------|
| 1 | WrittenSetWriter (TDD) | a83dd56 (RED), a90c51b (GREEN) | Services/Discovery/WrittenSetWriter.cs, ReelDiscovery.Tests/Discovery/WrittenSetWriterTests.cs |

## Verification

- `WrittenSetWriterTests`: 11/11 pass. The tests cover:
  - A clean 25-slot RFP: one Story-tier call with AllowBatch false. The content has requests 1..25 with the slots' IssueIds, plus caption with the case number, preliminary statement, definitions, instructions, label, signature, certificate, service date and party ids.
  - A 40-slot RFP: two calls of 25 and 15 slots with the same system prompt, and onPart reports (1,2) then (2,2).
  - An RFA with a duplicated prefix: the prefix is removed once, and FactId, DocId and IsGenuineness are copied from the slot.
  - Leading "REQUEST FOR PRODUCTION NO. 4:" and "Request No. 5:" labels are stripped.
  - Missing slot 3 plus unrequested slot 9: the retry prompt carries the feedback header, and the clean second attempt leaves no warnings.
  - Slot 3 missing on all 3 attempts: the best attempt is kept, requests are renumbered 1..7, and a D4MissingRequest 3 warning is kept.
  - An unknown slot 99 is dropped and recorded as D4ExtraRequest.
  - ROG subparts on every attempt: the set is kept with D7 warnings.
  - A fatal Auth error returns NotRun with its cost after a single call.
  - Refusals on all 3 attempts fail with Refused and all cost captured.
  - A user cancel propagates.
- `Tests.Discovery.` namespace: 449/449 pass.
- The app and test project `-warnaserror` builds are green.
- Acceptance greps:
  - RunAsync<RequestSetDto>: 1
  - internal WriteAsync: 1
  - MaxSlotsPerCall: 2
  - `DiscoveryBoilerplate.`: 8
  - `new Regex(`: 0

## Deviations from Plan

### Auto-added

**1. [Rule 2 - Correctness] Empty texts treated as missing**
- **Found during:** Task 1
- **Issue:** The validator skips content checks on empty text but still counts the slot as present. A blank request would then pass D4 and end up as an empty numbered request.
- **Fix:** Texts that are empty after cleanup are dropped before validation, so the slot is reported missing and omitted.
- **Commit:** a90c51b

Also added: an `UnknownSlotNumbers_AreDropped` test for T-08-37 (11 tests, above the minimum of 8).

## Known Stubs

None.

## TDD Gate Compliance

The RED commit `test(08-13)` a83dd56 does not compile because `WrittenSetWriter` and `WrittenSetWriteOutcome` do not exist yet. The GREEN commit `feat(08-13)` a90c51b followed it and also fixed a nullable warning in the test helper. No refactor commit was needed.

## Self-Check: PASSED
