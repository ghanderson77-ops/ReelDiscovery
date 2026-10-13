---
phase: 04-corpus-plan-answer-key
plan: 13
subsystem: corpus
tags: [planner, llm, chunking, mapping, re-ask, templates, tdd]
requires:
  - phase: 04-01
    provides: PlannedThread/PlannedMessage/PlannedAttachment (UsesFallbackContent, Intent, MustConvey, Title)
  - phase: 04-02
    provides: PlanCallRun, PlanCallRunner.RunAsync, PlanProgress/PlanStage, FillResult, FakeLlmProvider.Respond, CorpusTestData.ThreadSeqsIn
  - phase: 04-07
    provides: CorpusPlanPrompts.SystemPrompt, AppendCaseSynopsis, AppendCast, AppendValues
  - phase: 04-09
    provides: ThreadSkeletonBuilder.BuildRelevant (skeleton threads with default attachment file names)
provides:
  - RelevantThreadFiller(ILlmGateway, maxParallel = 3)
  - RelevantThreadFiller.FillAsync(draft, relevant, guidance, progress, ct) -> FillResult
  - RelevantThreadFiller.BuildChunks / BuildChunkPrompt / SchemaTypes / TemplateSubject / FileNameFor
  - ThreadPlanBatchDto / ThreadPlanDto / MessagePlanDto (strict, text-only)
affects: [04-planner-orchestration, 04-validator, 05-relevant-renderer]
tech-stack:
  added: []
  patterns: [time-window chunking across issues, DTO-only parallel tasks applied after WhenAll, one re-ask per chunk then flagged templates, unknown-id token stripping]
key-files:
  created:
    - Services/Corpus/RelevantThreadFiller.cs
    - ReelDiscovery.Tests/Corpus/RelevantThreadFillerTests.cs
  modified: []
key-decisions:
  - "Chunks are cut greedily in first-message (UTC) order, ties by thread id: a new chunk starts when 20 threads or 60 messages would be exceeded"
  - "Window facts = dated facts whose date falls within the chunk's sender-local message days, plus any fact a chunk thread carries; 'Already happened' lists up to 3 dated facts before the first window fact"
  - "Earlier threads (all threads of previous chunks) are linkable; the prompt lists only the last 40 to bound context at 5K-email scale"
  - "Re-ask triggers on a blank subject, purpose or rationale, or any message without intent or must-convey points (broader than subject/intent so the answer-key rationale is never blank); refused chunks are not re-sent"
  - "Any template use sets UsesFallbackContent; the note is singular for one thread ('1 relevant thread uses a placeholder subject because the model did not plan it.') and the plan's plural text otherwise"
  - "Failed (non-refused) chunks are counted once by their final outcome and add a note 'Relevant threads part i of n was not planned: {error}'"
  - "Attachment file names: FileNameHelper.SanitizeForFileName, leading/trailing dots/underscores trimmed, stem capped at 80, code extension; blank titles keep the code default file name and take its stem as Title (not flagged)"
  - "Subjects also lose any RE:/FW:/FWD: prefix in code"
metrics:
  duration: 20min
  completed: 2026-10-02
  tasks: 2
  files: 2
---

# Phase 4 Plan 13: Relevant Thread Filler Summary

Chunked Story-tier pass that writes subject, purpose, rationale, per-message intent, must-convey points, attachment titles and links onto the code-built relevant-thread skeleton. Output is mapped strictly by canonical thread id and seq. Each chunk gets one re-ask, then flagged code templates.

## What Was Built

- **DTOs** (`ThreadPlanBatchDto`, `ThreadPlanDto`, `MessagePlanDto`): copied from RESEARCH. They hold text only, with no nullable, date, person, count or label members. The strict-schema validator reports 0 errors and the union count stays within the limit of 16.
- **BuildChunks**: orders threads by first SentAt. Each chunk has at most 20 threads and 60 messages, every thread is in exactly one chunk, and chunks span all issues (a March-May chunk mixes ISS-01-only and ISS-02 threads).
- **BuildChunkPrompt**: name rules, case synopsis, cast lines for the chunk's participants, canonical values, "Already happened" earlier facts, facts in the period (`F-003 | 2024-03-18 | title | description`), earlier thread ids with their fact titles, then one block per thread and one line per message in the exact planned formats. "Guidance from the user: ..." is added only when guidance is given. The task text ends the prompt. There are no JSON instructions.
- **FillAsync**:
  - Builds every prompt up front and runs one `PlanCallRun` (retry hint "Click Retry Planning.") with bounded `PlanCallRunner.RunAsync<ThreadPlanBatchDto>` calls. Progress reports PlanningRelevant, StageDone i of n.
  - Applies results on the calling thread after `Task.WhenAll`.
  - Re-asks once, per chunk, with only the threads still missing text and the same earlier list. The OperationName gets the suffix " retry".
  - Fills whatever is still missing from templates based on the first fact and issue, and sets `UsesFallbackContent = true`.
  - Fatal errors return `FatalError` without a re-ask. A user cancel throws OperationCanceledException. Refusals and failures are counted in `FillResult`.

## Verification

- `dotnet test --filter "FullyQualifiedName~RelevantThreadFillerTests|FullyQualifiedName~PlanCallRunnerTests"`: 36 passed
- `dotnet test --filter "FullyQualifiedName~ReelDiscovery.Tests.Corpus&Category!=Render"`: 536 passed
- Acceptance greps: DTO classes 1/1, chunk limit constants 1/1, `PlanCallRunner.RunAsync<ThreadPlanBatchDto>` 2, `LlmTier.Story` 1, `UsesFallbackContent = true` 1, `GeneratedRegex` 3, 23 `[Fact]` tests (need at least 14)

## TDD Gate Compliance

- RED: a3aaa93 (Task 1 tests), 209e372 (Task 2 tests). Both failed to compile against the missing members.
- GREEN: bc7d517 (Task 1), 66d7846 (Task 2)

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 2 - Correctness] Broader re-ask/template trigger**
- **Found during:** Task 2
- **Issue:** Re-asking only for a blank subject or intent would let a blank rationale through. The rationale feeds the answer key (D-31).
- **Fix:** A blank purpose, a blank rationale or empty must-convey points also count as missing. The plan's subject/intent case is still covered.
- **Commit:** 66d7846

**2. [Rule 2 - Security] File name hardening beyond SanitizeForFileName**
- **Found during:** Task 2
- **Issue:** SanitizeForFileName keeps dots, so `../CO 7: Approval?` became `..CO_7_Approval`.
- **Fix:** Leading and trailing dots, underscores and spaces are trimmed before the 80-character cap (T-04-38).
- **Commit:** 66d7846

**3. [Copy] Singular placeholder note**
- For one templated thread the note reads "1 relevant thread uses a placeholder subject because the model did not plan it." This follows the singular-copy convention from [03-06]. For two or more threads the note uses the plan's exact text.

Test adjustment: the plan's "no fictional" check applies to the system prompt. The chunk prompt includes `CaseGuidePrompts.AppendNameRules`, which restates the user's applied substitutions ("Use these fictional names exactly: ...") and the shared scenario rule. That is existing D-02 behaviour, so the test only asserts that no invented-names-fictional instruction appears.

## Known Stubs

None.

## Notes for the Planner Orchestration Plan

- Register `RelevantThreadFiller.SchemaTypes` in `CorpusPlanner.SchemaTypes` and in the CaseSchemaTests theory data when the planner exists.
- RESEARCH suggests failing the plan when more than half of `FillResult.TotalChunks` are refused. That decision belongs to the caller.
- PLAN-02, PLAN-03 and PLAT-03 stay Pending. This plan is one stage of the planner pipeline.

## Self-Check: PASSED

- FOUND: Services/Corpus/RelevantThreadFiller.cs
- FOUND: ReelDiscovery.Tests/Corpus/RelevantThreadFillerTests.cs
- FOUND commits: a3aaa93, bc7d517, 209e372, 66d7846
