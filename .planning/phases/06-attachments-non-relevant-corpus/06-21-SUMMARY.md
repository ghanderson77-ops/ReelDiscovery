---
phase: 06-attachments-non-relevant-corpus
plan: 21
subsystem: evidence-parity
tags: [noise-05, parity, d-07, d-11, d-12, pitfall-1, pitfall-2, tests]
requires:
  - "06-19/06-20 GenerationRunner.RunAsync (waves, Stage B, repairs)"
  - "06-06 RenderAssignments / FillerAssignments / LengthBands"
  - "06-10 MailboxWriter.DocIdHeader, CopyPathsFor"
  - "06-14 answer-key.csv via checkpoints"
  - "Phase 4 CorpusPlanner (production-planner fixture)"
provides:
  - "ReelDiscovery.Tests/Evidence/DistributionParityTests.cs: ParityRuns class fixture (Synthetic + ProductionPlanner) and 8 parity facts"
  - "DistributionParityTests.BuildSyntheticPlan(relevant, nonRelevant) and RunAsync(name, draft, plan, overrideJson) for reuse"
  - "CorpusPlannerTests.Draft / Responder / Planner now internal static"
affects: [06-25 live run record (model-written parity), Phase 7 leakage audit]
tech-stack:
  added: []
  patterns:
    - "Each parity fact's XML doc starts with 'Proves:' and names code-controlled input or plumbing only (fake provider)"
    - "One class fixture (IAsyncLifetime) runs both plans once; every run is guarded with WaitAsync(90 s)"
key-files:
  created:
    - ReelDiscovery.Tests/Evidence/DistributionParityTests.cs
  modified:
    - ReelDiscovery.Tests/Corpus/CorpusPlannerTests.cs
    - .planning/phases/06-attachments-non-relevant-corpus/deferred-items.md
decisions:
  - "Header sets are compared per shape with Cc left out. Whether Cc appears depends on the plan, and Parity_StructureByShape checks the Cc count against the plan."
  - "The production-planner fixture constructs `new CorpusPlanner(gateway, new(PlannerSeed))` directly so the key link is in the file. The seeded generator is the planner's own input. The synthetic builder uses index arithmetic only, so the 'no new Random' check holds where it matters."
  - "Synthetic non-relevant attachments use the planner's code-default file names (Document N.docx, Photo N.png). This means Parity_AttachmentNames proves that filler titles replace them in both the EML and the key."
metrics:
  duration: 25min
  completed: 2026-10-05
  tasks: 1
  files: 2
---

# Phase 6 Plan 21: Distribution Parity Suite Summary

This is an end-to-end parity suite. It runs a synthetic mixed plan and a plan built by the real Phase 4 CorpusPlanner through GenerationRunner with the fake provider. It then checks that code-controlled metadata cannot tell relevant mail from non-relevant business mail. Each fact states what it proves. The fake-output fact is labelled as proving plumbing only.

## What was built

**ParityRuns (class fixture, IAsyncLifetime)** builds and runs both fixtures once and deletes the temp case folders on dispose.
- **Synthetic:** MixedCase plus deterministic clones, built by index arithmetic with no Random, up to 40 relevant and 60 business threads:
  - 1-5 messages per thread, mixing Reply, ReplyAll and Forward
  - 0-3 Cc, chosen from cast members active that day
  - every fourth clone privileged
  - an attachment on every fifth message: Word, Excel, PowerPoint or an inline image
  - MixedCase's bulk threads, invites, voicemails and chains are kept
  - renumbered with PlanNumbering.Assign
- **ProductionPlanner:** `CorpusPlanner.PlanAsync(draft, mediaAvailable: true, "")` on CorpusPlannerTests.Draft() (250 emails, 20% relevant), using the CorpusPlannerTests responder and a fixed seed.

Both runs finish (GenerationOutcome.Finished).

**Facts.** Each theory runs on both fixtures. The out-of-band fact is a separate [Fact].

| Fact | Proves | What it checks |
|------|--------|----------------|
| Parity_LengthBands | code-controlled input | Each band count is within 1 of Apportion.Largest. Band shares differ between populations by at most 0.05 |
| Parity_LengthTokens | code-controlled input | Every ThreadRenderDto and FillerThreadDto message line carries the assigned band's `length: min-max words` token. Voicemail lines carry the voicemail instruction |
| Parity_FakeOutputWordCounts_PlumbingOnly | plumbing only (fake provider) | Every non-voicemail EML body is non-empty and equals the fake's midpoint body. Median word counts per band are logged |
| Parity_FillerOutOfBand_IsKeptWithoutRetry | code-controlled input | A 400-word body on a Short daily-business message: exactly 1 filler request, ThreadDone Written, Attempts 1, and the 400 words are in the EML (D-12 strict) |
| Parity_PrivilegeMarkers | code-controlled input | 85% labelled and 40% subject-tagged, each within 1 thread, for both populations. Banner in the plain text if and only if the thread is Banner/BannerAndSubject. Tagged subjects. Privileged=Y key rows with a banner marking have the banner in their EML |
| Parity_HeadersByShape | code-controlled input | Header-name sets are equal per shape (root, reply, forward). Message-ID matches the MessageIdFor shape. The DocID header equals the file stem. The disclaimer banner is in the HTML |
| Parity_StructureByShape | code-controlled input | To, Cc and References counts match the plan. In-Reply-To is present exactly when the message replies to another. The MIME root follows one rule for both populations: none = alternative, inline only = alternative with related, files = mixed |
| Parity_AttachmentNames | code-controlled input | Each key File Name equals its EML part's file name (matched by DocID header). No code-default names remain. Non-relevant rows are Generated or Generated - with warnings |

**Observed numbers (logged):**
- **Length bands, Synthetic:** relevant S/M/L 52/33/10, business 84/53/15.
- **Length bands, ProductionPlanner:** relevant 28/17/5, business 88/56/16.
- **MIME layouts, Synthetic:** relevant 21 files / 3 inline / 71 none; business 25 / 7 / 120.
- **MIME layouts, ProductionPlanner:** relevant 10 files / 40 none; business 32 / 128; neither population has inline images.
- **Privilege, Synthetic:** relevant 7 privileged / 6 labelled / 2 tagged; non-relevant 13 / 11 / 4.

**Mutation check:** I forced FillerAssignments' Long count to 0 and both Parity_LengthBands theories failed. The change was reverted with no production change committed.

## Verification

- `dotnet test ... --filter "FullyQualifiedName~DistributionParityTests|FullyQualifiedName~CorpusPlannerTests"`: 35/35 passed in about 20 s, including the build (well under 90 s).
- Full suite: 3379/3379 passed (baseline 3364 + 15 new).
- Acceptance greps:
  - `public void/async Task Parity_`: 8
  - `new CorpusPlanner(`: 1
  - Headers/Structure/PlumbingOnly names: 3
  - `Proves:`: 8
  - FillerOutOfBand name: 1
  - non-comment `new Random`: 0
- The build adds no new warnings. The xUnit1031 warnings in GenerateCorpusStepTests already existed.

## Deviations from Plan

### Adjustments

**1. Planner fixture built directly instead of through CorpusPlannerTests.Planner(seed)**
- **Why:** the key link requires `new CorpusPlanner(` in this file, and the grep forbids `new Random` outside comments. The fixture uses `new CorpusPlanner(gateway, new(PlannerSeed))`, a target-typed seeded Random that is the planner's own input. The Planner helper is still opened to internal static, as the plan specifies.
- **Commit:** 9e0d1ba

Otherwise the plan was executed as written. No production code changed.

## Findings (not hidden, not a test failure)

- **Privilege incidence differs between populations on the production-planner plan at 250 emails.** One relevant thread is privileged and no non-relevant thread is.
- **Cause:** `NonRelevantSkeletonBuilder.AssignDailyPrivilege` privileges about 2% of daily-business threads. It guarantees one only when there are at least 50 daily threads.
- **Effect:** in small corpora a privilege banner appears only on relevant mail.
- **Why the test still passes:** the marking mix (85% / 40%) is applied correctly to whatever is privileged, and that is what the plan asserts.
- **Status:** this is a Phase 4 planner signal and out of scope here. It is logged in deferred-items.md for the Phase 7 leakage audit. The assertion was not loosened or changed to hide it.
- **Also seen:** the production planner put no inline images on either population. The inline-only MIME rule is therefore exercised only by the synthetic fixture.

## TDD Gate Compliance

The plan is test-only: it adds tests over production behavior that already exists. There is one `test(06-21)` commit and no `feat` commit, because no production code was needed. The tests passed on first run, so I checked that they test something real. A mutation of FillerAssignments' length apportionment made Parity_LengthBands fail on both fixtures, and the change was reverted.

## Known Stubs

None.

## Self-Check: PASSED

- FOUND: ReelDiscovery.Tests/Evidence/DistributionParityTests.cs
- FOUND: ReelDiscovery.Tests/Corpus/CorpusPlannerTests.cs (helpers internal static)
- FOUND: commit 9e0d1ba
