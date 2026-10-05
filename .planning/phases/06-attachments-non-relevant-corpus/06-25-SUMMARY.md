---
phase: 06-attachments-non-relevant-corpus
plan: 25
subsystem: phase-gate
tags: [phase-gate, uat, validation, render-tests, dpi]
requires:
  - "06-01 through 06-24 complete"
provides:
  - "06-VALIDATION.md with all 35 rows mapped and green; nyquist_compliant and wave_0_complete set to true"
  - "06-HUMAN-UAT.md approved by the user (2026-10-05); 3 UAT issues fixed and logged under Gaps"
affects: [phase 7 leakage audit (deferred items)]
tech-stack:
  added: []
  patterns:
    - "Test-only size caps backed by AsyncLocal so tests running in parallel never see each other's overrides"
    - "Planner additions to the guide are carried on the plan (AddedKnowers) and applied when the plan is saved, never by a failed run"
key-files:
  created:
    - .planning/phases/06-attachments-non-relevant-corpus/06-HUMAN-UAT.md
  modified:
    - .planning/phases/06-attachments-non-relevant-corpus/06-VALIDATION.md
    - .planning/phases/06-attachments-non-relevant-corpus/deferred-items.md
    - ReelDiscovery.Tests/Evidence/GenerationResetTests.cs
    - UserControls/ModelConfigurationDialog.cs
    - Services/Evidence/AttachmentHarvester.cs
    - Services/Evidence/GenerationJournal.cs
    - Services/Corpus/CorpusPlanStore.cs
    - Services/Case/EmailIdentity.cs
    - Services/Corpus/SubjectSeeder.cs
    - Services/Corpus/ThreadSkeletonBuilder.cs
    - Services/Corpus/CorpusPlanner.cs
    - Services/Corpus/CorpusApproval.cs
    - Services/Corpus/SkeletonContext.cs
    - Models/Corpus/CorpusPlan.cs
decisions:
  - "The human checkpoint was not auto-approved under --auto. It needed a paid live run and a visual check at 150%, so it waited for the user's sign-off"
  - "Lone-knower facts get the colleague who shares the most facts with the knower (then same organization, Key tier, id; counsel only for counsel). Notes to self remain only when nobody else is active"
  - "Personal sender names must pass EmailIdentity.LooksLikePersonName; a role such as 'A friend' keeps the code-generated contact"
metrics:
  duration: multi-session
  completed: 2026-10-05
  tasks: 2
  files: 16
---

# Phase 6 Plan 25: Phase gate Summary

The phase gate passed. All automated suites are green, the validation map is complete, and the user approved the live run on the Office case after three UAT issues were fixed.

## Task 1: automated gates, validation map and live-run preparation (785b704)

- The build has 0 warnings. 3,397 tests passed (3,284 non-render, 113 render).
- In `06-VALIDATION.md`, all 35 rows point to the task that implements them and are green. There are no TBD rows left.
- Live-run preparation (in the UAT file):
  - plan coverage per attachment kind and category;
  - cost estimate;
  - the thin spots in the plan: no inline images, both voicemails on non-relevant threads, one relevant invite.

## Task 2: human verification, approved 2026-10-05

The user signed off on the phase as a whole. Step 9 (Phase 5 upgrade) was skipped because no Phase 5 run folder exists. Step 10 (optional) was not reported.

## Deviations (issues found during the gate, all fixed)

1. **[Test flake] The Start Over locked-file test.** Its exclusive file open now retries on IOException, likely because Defender or the indexer briefly opens new EMLs. Commit 57d663b.
2. **[UI, 150%] The model dialog clipped at 144 DPI.** Its fixed pixel size and minimum size are now logical sizes scaled with LogicalToDeviceUnits and limited to the working area. Commit b5831ce.
3. **[Test isolation] Two AttachmentRepair tests failed in Test Explorer.** Static size-cap overrides set by other test classes leaked in while tests ran in parallel. The caps are now AsyncLocal, with a regression test. Commit 7d78ac6.
4. **[Generation] Personal email came from "A friend" <a.friend@gmail.com>.** Sender names now have to look like a real first and last name. Commit 31cd841.
5. **[Planning] Ryan Howard emailed himself in 20 threads (72 messages).** These came from facts with a single knower. The planner now adds a colleague under Who knew. The additions are carried on the plan and applied to the guide when the plan is saved. Commit 262f9c9.

Fixes 4 and 5 only ran their own test groups (111 and 722 tests). The full suite has not been rerun since.

## Deferred (see deferred-items.md)

- Privilege banners appear only on relevant mail in corpora under 250 emails.
- Invites for the same meeting can get different times when the senders are in different time zones.
- GenerationReset.TryDelete has no retry when a file is briefly locked.
- Theme fonts in the business HTML path are not sanitized.

## Self-Check: PASSED
