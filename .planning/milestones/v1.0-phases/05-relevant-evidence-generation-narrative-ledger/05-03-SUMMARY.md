---
phase: 05-relevant-evidence-generation-narrative-ledger
plan: 03
subsystem: evidence-planning
tags: [waves, render-assignments, determinism, sha256, apportion]
requires: ["05-01"]
provides:
  - Services/Evidence/WavePlanner (Plan, WaveIndexByThread, window constants)
  - Services/Evidence/RenderAssignments (Compute, StableRank, LengthBands, Spec, SubtletyShares, LabeledShare, SubjectTagShare)
affects: [05-04..05-17, phase-06 filler length bands]
tech-stack:
  added: []
  patterns: [SHA-256 stable rank instead of seeds, largest-remainder exact mixes, whole-plan pure functions for Continue]
key-files:
  created:
    - Services/Evidence/WavePlanner.cs
    - Services/Evidence/RenderAssignments.cs
    - ReelDiscovery.Tests/Evidence/WavePlannerTests.cs
    - ReelDiscovery.Tests/Evidence/RenderAssignmentsTests.cs
  modified: []
decisions:
  - "Length bands are apportioned over all messages of relevant threads (privileged and quoted included); subtlety pool is own-basis non-privileged only"
  - "Of labeled privileged threads, the first Largest(n,[0.60,0.40])[1] by StableRank(id+':subject') get BannerAndSubject; unlabeled are the last by StableRank(id+':privilege')"
  - "Over-12 wave split recurses at the largest FirstUtc gap (earliest on ties)"
metrics:
  duration: 8min
  completed: 2026-10-03
  tasks: 2
  files: 4
---

# Phase 5 Plan 03: Wave Planner and Render Assignments Summary

Two pure, deterministic services: chronological 7/14-day waves capped at 12 threads (split at the largest gap), and per-message render knobs (exact 60/30/10 subtlety with hot always Explicit, exact 55/35/10 length bands with Long never on replies, 85%/40% privilege labeling), all ranked by SHA-256 so retries and Continue reproduce them exactly.

## Tasks

| Task | Name | Commit |
|------|------|--------|
| 1 | WavePlanner | 6504fca |
| 2 | RenderAssignments (subtlety, length, privilege marking) | 24784cd |

## What Was Built

- **WavePlanner.Plan(CorpusPlan):** relevant threads with messages, ordered by `MessageTime.FirstUtc` then Id (ordinal); greedy window (add if span < 7 days, or < 14 days while the wave has < 3 threads); recursive split above 12 at the largest gap; 1-based Index, Start/End from `FirstLocalDate`, MessageCount. Null-safe on `plan.Threads`. `WaveIndexByThread` maps thread id -> wave index.
- **RenderAssignments.Compute(CorpusPlan):** ordinal dictionary of `ThreadAssignment` for relevant threads only, messages in Seq order.
  - Subtlety: pool = own-basis messages of non-privileged threads; `Apportion.Largest(n, 60/30/10)`; hot fill Explicit first, overflow shrinks Indirect then Clear; ranked by `StableRank(id + ":subtlety")`. Privileged own -> Counsel, quoted/none -> None; every hot message -> Explicit (D-17).
  - Length: `Apportion.Largest(n, 55/35/10)`; Long only for New/Forward (shortfall -> Medium); Short to replies first, then others; rest Medium.
  - Privilege: `Largest(n, [0.85, 0.15])` labeled, `Largest(labeled, [0.60, 0.40])` subject-tagged.
  - `StableRank` = big-endian first 8 bytes of SHA-256(UTF-8 id); `LengthBands` table + `Spec(band)` shared with Phase 6.

## Verification

- `dotnet test --filter "FullyQualifiedName~WavePlannerTests"`: 13 passed.
- `dotnet test --filter "FullyQualifiedName~RenderAssignmentsTests"`: 17 passed.
- `dotnet test --filter "FullyQualifiedName~ReelDiscovery.Tests.Evidence&Category!=Render"`: 55 passed.
- `dotnet build ReelDiscovery.csproj`: 0 CS warnings.
- Acceptance greps: WindowDays/MaxThreadsPerWave/Plan signature = 1 each; no `Random` in WavePlanner code; `SHA256.HashData` = 1; no GetHashCode/Random in RenderAssignments code; `Apportion.Largest` = 4; LengthBands declaration = 1.

## Deviations from Plan

None - plan executed exactly as written. Extra tests beyond the spec: repeated splitting (30 threads), 14-day hard close, sender-local Start date, id tie-breaking, all-hot pool, Forward eligible for Long, null/empty plans, and messages reversed within threads for the stability check.

## TDD Gate Compliance

Both tasks were `tdd="true"`, but tests and implementation were committed together in one `feat` commit per task; there are no separate RED `test(...)` commits.

## Known Stubs

None.

## Self-Check: PASSED
- All 4 created files exist; commits 6504fca and 24784cd are in git log.
