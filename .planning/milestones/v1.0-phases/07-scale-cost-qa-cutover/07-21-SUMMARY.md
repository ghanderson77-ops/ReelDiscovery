---
phase: 07-scale-cost-qa-cutover
plan: 21
subsystem: generation-runner
tags: [scale, batch, memory, OUT-03, RUN-03, D-07, Pattern-12]
requires:
  - phase: 07-10
    provides: "Batch-mode runner (BatchingLlmProvider, one batch per wave label, BatchMaxParallel)"
  - phase: 07-13
    provides: "Batch filler chunking and stage labels"
  - phase: 07-15
    provides: "QaRunner.RunEndOfRunAsync (scans + leakage)"
  - phase: 07-20
    provides: "Legacy generation path deleted; case flow is the only path"
provides:
  - "GenerationRunner.ProjectionIntervalFor: adaptive per-thread key projection interval (2 s floor, 10x last duration, 60 s cap)"
  - "RunContext.RecordProjectionDuration / ReadProjectionDuration"
  - "ReelDiscovery.Tests/Scale/FiveThousandRunTests.cs: 5K live + batch scale tests (Category=Scale)"
affects: [07-22]
tech-stack:
  added: []
  patterns:
    - "Adaptive debounce: next projection allowed after max(floor, 10 x last projection duration) capped"
    - "Scale tests use a lean no-log ILlmProvider so measured memory is the app's, not the fake's"
key-files:
  created:
    - ReelDiscovery.Tests/Scale/FiveThousandRunTests.cs
  modified:
    - Services/Evidence/GenerationRunner.cs
    - ReelDiscovery.Tests/Evidence/GenerationRunnerTests.cs
    - ReelDiscovery.Tests/Evidence/DistributionParityTests.cs
decisions:
  - "Key projection interval is adaptive: max(2 s, 10 x last per-thread projection duration), capped at 60 s; wave end and run end still always project"
  - "AnswerKeyXlsxWriter left on the DOM API: the 5K run peaks at ~360 MB (live) / ~465 MB (batch), far under the 1.5 GB budget"
  - "Batch QuietPeriod 500 ms kept: measured wave unit start-up spread is 36-38 ms (limit 250 ms)"
metrics:
  duration: ~35 min
  completed: 2026-10-07
---

# Phase 7 Plan 21: Adaptive Key Projection + 5K Scale Tests Summary

Per-thread answer-key projection now backs off to 10x its own last duration (2 s floor, 60 s cap), and two in-process ~5,000-email scale tests (live and batch) prove the run finishes with exact EML and answer-key counts, an identical key in both modes, one thread-render batch per wave, and peak memory under a third of the 1.5 GB budget.

## Tasks

| Task | Name | Commits | Files |
|------|------|---------|-------|
| 1 | Adaptive projection interval (TDD) | f88e7df (RED), 41b64c5 (GREEN) | Services/Evidence/GenerationRunner.cs, ReelDiscovery.Tests/Evidence/GenerationRunnerTests.cs |
| 2 | 5K scale tests, live and batch | 3d09e2f | ReelDiscovery.Tests/Scale/FiveThousandRunTests.cs, ReelDiscovery.Tests/Evidence/DistributionParityTests.cs |

## What was built

- `GenerationRunner.ProjectionIntervalFor(TimeSpan lastDuration)` returns `max(KeyProjectionInterval, 10 x lastDuration)` capped at the new `MaxKeyProjectionInterval` (60 s). `MaybeProjectAsync` uses it for both the pre-gate and in-gate checks and times each per-thread projection with a `Stopwatch` and stores it on `RunContext` under its lock. `CheckpointAsync` (wave end, run end) is unchanged and always projects.
- `FiveThousandRunTests` (`[Trait("Category", "Scale")]`): a class fixture plans 5,000 emails once with the real `CorpusPlanner` (seed 2121, `CorpusPlannerTests.Responder`), runs it live once (lazily), and each test runs `GenerationRunner` in a fresh temp folder. Temp folders are deleted on dispose.
  - Live: checks the run Finished, EML files on disk = Σ `MailboxWriter.CopyPathsFor` over planned messages, key rows = planned emails + planned attachments (also split by Doc Type), every status is Generated or Generated - with warnings, process `PeakWorkingSet64` and the 500 ms `PeriodicTimer` sampled working set are under 1.5 GB, managed memory after a forced GC is under 1 GB, and the journal is under 180 MB. Then `QaRunner.RunEndOfRunAsync` returns non-null scans and leakage reports.
  - Batch: `FakeBatchTransport` + `BatchingLlmProvider` with QuietPeriod 500 ms, PollInterval 20 ms. Same checks, plus the same EML count as live, an answer-key CSV byte-identical to live's, exactly one all-thread-render submission per wave label (labels from the batch ledger), and a wave start-up spread under 250 ms.
  - A `TimingProvider` decorator records when each thread render leaves the runner, with its ambient `BatchStage` label. That is how the start-up spread is measured.

## Measurements (two consecutive runs, Debug build, net10.0 test host)

| Metric | Run 1 live | Run 1 batch | Run 2 live | Run 2 batch |
|--------|-----------|-------------|-----------|-------------|
| Plan (threads / emails) | 2,299 / 5,000 | same | same | same |
| Planner time | 0.5 s | — | 0.5 s | — |
| Run duration | 177.4 s | 203.4 s | 174.0 s | 207.4 s |
| Peak working set (sampled / process) | 351 / 361 MB | 431 / 446 MB | 362 / 362 MB | 464 / 464 MB |
| Managed after GC | 49 MB | 77 MB | 49 MB | 77 MB |
| Journal size | 6.8 MB | 6.8 MB | 6.8 MB | 6.8 MB |
| Key rows (Generated / with warnings) | 6,065 / 11 | 6,065 / 11 | 6,065 / 11 | 6,065 / 11 |
| End-of-run QA | 4.1 s | — | 4.5 s | — |
| Waves / submissions | — | 44 / 162 | — | 44 / 165 |
| Widest wave start-up spread | — | 36 ms (wave 37, 10 units) | — | 38 ms (wave 32, 11 units) |

- The batch process peak includes the live run that came before it in the same process. It also includes `FakeBatchTransport`, which keeps every submitted request.
- The submission total changes by a few between runs because attachment submissions depend on timing. The asserted counts do not change: one thread-render submission per wave, all EML and key counts, and the key bytes.
- The Scale category takes about 6.5 minutes. The full non-render suite (`Category!=Render&Category!=Scale`) passes 3,714/3,714.

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 3 - Blocking] Made `DistributionParityTests.Responder(plan, guide, overrideJson)` internal**
- **Found during:** Task 2
- **Issue:** The scale run needs the same AttachmentResponder + side-world responder that the production-planner parity fixture uses, and it was private.
- **Fix:** Changed `private` to `internal`. There is no behavior change.
- **Commit:** 3d09e2f

**2. [Measurement accuracy] Lean provider in the scale harness**
- `FakeLlmProvider` keeps a snapshot of every request. At 5K that would add the fake's memory to the budget being measured. The harness uses a private `RespondingProvider` (same responder, no log). Batch mode still uses `FakeBatchTransport` because its `Submissions` are needed for the per-wave assertion.

### Not changed
- `Services/Corpus/AnswerKeyXlsxWriter.cs` is unchanged. The measured peak (≤ 464 MB) is well under the 1.5 GB budget, so the plan's condition for switching to `OpenXmlWriter` streaming was not met.

## TDD Gate Compliance

- Task 1: RED `test(07-21)` f88e7df (compile failure, `ProjectionIntervalFor` missing), then GREEN `feat(07-21)` 41b64c5.
- Task 2: the scale tests check behavior that already exists (the runner at scale). They passed on their first run, with no production change needed, so there is no separate GREEN commit. One `test(07-21)` commit (3d09e2f).

## Known Stubs

None.

## Self-Check: PASSED

- FOUND: ReelDiscovery.Tests/Scale/FiveThousandRunTests.cs
- FOUND: Services/Evidence/GenerationRunner.cs (`internal static TimeSpan ProjectionIntervalFor(` x1, `ProjectionIntervalFor(ctx` x2)
- FOUND commits: f88e7df, 41b64c5, 3d09e2f
