---
phase: 08-ancillary-discovery-documents
plan: 12
subsystem: discovery-deposition
tags: [discovery, deposition, keep-best, retry, ANC-01, ANC-05]
requires:
  - phase: 08-03
    provides: "DepositionPlan / DepositionChunk / Speakers / Exhibits"
  - phase: 08-06
    provides: "TranscriptLayout.CountTestimonyLines / ComputeEndTime / FormatTurn / LinesPerPage"
  - phase: 08-07
    provides: "DiscoveryBoilerplate.TranscriptCaptionLines"
  - phase: 08-08
    provides: "DiscoveryValidator.CheckDepositionChunk / IsBetter / FeedbackHeader"
  - phase: 08-11
    provides: "DiscoveryPrompts.BuildDepositionPrompt / DepositionChunkDto / OperationDeposition / MaxOutputTokens"
provides:
  - "DepositionWriter.WriteAsync(draft, plan, caseNumber, systemPrompt, guidance, run, onPart) -> DepositionWriteOutcome"
  - "DepositionWriteOutcome { Content, Warnings, Error, Refused, TimedOut, NotRun, Cost }"
  - "DepositionWriter.MaxAttempts = 3, MaxPageOverflow = 1.25"
affects: [08-14, 08-15]
tech-stack:
  added: []
  patterns:
    - "Sequential chunk loop: PlanCallRunner per attempt, validator check, feedback retry, keep-best via DiscoveryValidator.IsBetter"
    - "Code-owned transcript structure: speaker id normalization, exhibit marker placement, first-chunk honorifics, overflow trim at turn boundaries"
key-files:
  created:
    - Services/Discovery/DepositionWriter.cs
    - ReelDiscovery.Tests/Discovery/DepositionWriterTests.cs
  modified: []
key-decisions:
  - "08-12: A refusal or timeout on a chunk is retried (up to 3 attempts) when no usable attempt exists yet; once a usable attempt exists, a later empty attempt ends the loop and the best is kept"
  - "08-12: An assigned exhibit mentioned in text (\"Exhibit N\") but never marked gets its marker on the first mentioning turn; repeat markers are dropped"
  - "08-12: Unrecognized Colloquy ids go to the examining attorney and unrecognized Parenthetical ids to the reporter; Q/A are always examining/witness"
  - "08-12: Overflow is trimmed from the end of the last topic; if the last topic reaches 2 turns, trimming moves to the topic before it; a dangling question at a topic's end is dropped"
  - "08-12: All plan speakers (including fallback counsel) plus reporter and videographer are added to ExtraNames; exhibit DocIds are AllowedIdentifiers"
duration: 15min
completed: 2026-10-08
---

# Phase 8 Plan 12: DepositionWriter Summary

`DepositionWriter` writes one deposition chunk by chunk on the Story tier. Every call uses the same system prompt. Each chunk is checked with `DiscoveryValidator`, retried with feedback up to 3 times, and the best attempt is kept. The writer then assembles a complete `DepositionContent`; code, not the model, owns speaker ids, exhibit markers, honorifics, overflow trimming and the end time.

## Tasks

| Task | Name | Commits | Files |
|------|------|---------|-------|
| 1 | DepositionWriter attempt loop and content assembly (TDD) | 8176f94 (RED), cf1ea65 (GREEN) | Services/Discovery/DepositionWriter.cs, ReelDiscovery.Tests/Discovery/DepositionWriterTests.cs |

## Verification

- `DepositionWriterTests`: 11/11 pass. They cover clean two-chunk assembly (Story model, AllowBatch false, identical system prompt, turn order and TopicIndex, honorifics with reporter and videographer untouched, deep-copied speakers, EndTime, caption), a retry carrying the feedback header, keep-best where attempt 2's D2 becomes the only warning, speaker and exhibit normalization with empty turns dropped, placing a marker on a text mention, overflow trimmed to 125% or less in the last topic, a fatal Auth error returning NotRun with its cost, three refusals failing with all cost captured, a chunk-2 timeout failing the document, user cancel propagating, and onPart reporting.
- `Tests.Discovery.` namespace: 437/437 pass.
- App and test project `-warnaserror` builds are green.
- Acceptance greps all return 1: RunAsync<DepositionChunkDto>, MaxAttempts = 3, internal WriteAsync, LlmTier.Story, AllowBatch = false, DiscoveryValidator.IsBetter.

## Deviations from Plan

### Auto-added

**1. [Rule 2 - Correctness] Exhibit markers placed from text mentions**
- **Found during:** Task 1
- **Issue:** The validator accepts an exhibit as shown when the text says "Exhibit N" without a marker. Without a marker, the transcript index has no page:line for that exhibit.
- **Fix:** For any assigned exhibit with no marker, the first turn that mentions it gets the marker. Repeat markers for the same exhibit in a chunk are zeroed.
- **Commit:** cf1ea65

**2. [Rule 2 - Correctness] Overflow trimming falls back to earlier topics**
- **Issue:** Trimming only the last topic cannot always reach 125% of the target pages without emptying that topic.
- **Fix:** The last topic keeps at least 2 turns. Once it reaches that floor, trimming moves to the topic before it.
- **Commit:** cf1ea65

Also added: an `OnPart_ReportsEachChunk` test (11 tests, above the minimum of 9).

**3. [Rule 1 - Bug] Overflow trimming no longer removes exhibit markers (coordinator follow-up)**
- **Issue:** Trimming could remove a turn carrying an exhibit marker. The exhibit then stayed in the exhibit list with no page:line in the layout.
- **Fix:** Trimming skips marker turns and the answer that follows a marked question. A question left unanswered by a trim is removed with its answer. If only protected turns remain, the overflow is accepted.
- **Test:** `Overflow_NeverTrimsExhibitMarkerTurnOrItsAnswer` checks that the exhibit is kept with a page:line in the layout and that no question is left unanswered. DepositionWriterTests 12/12 pass; Tests.Discovery. 438/438 pass.

## Known Stubs

None.

## TDD Gate Compliance

The RED commit `test(08-12)` 8176f94 does not compile because `DepositionWriter` and `DepositionWriteOutcome` do not exist yet. The GREEN commit `feat(08-12)` cf1ea65 followed it. No refactor commit was needed.

## Self-Check: PASSED
