---
phase: 04-corpus-plan-answer-key
plan: 10
subsystem: corpus
tags: [planner, skeleton, non-relevant, decoys, externals, privilege, attachments, tdd]
requires:
  - phase: 04-01
    provides: PlannedThread/PlannedMessage/PlannedAttachment, ExternalParty/ExternalKind, NonRelevantCategory
  - phase: 04-02
    provides: BusinessCalendar, CorpusTestData.RichDraft
  - phase: 04-03
    provides: SkeletonContext, ThreadShapes, ThreadScheduler, BudgetAllocator.SplitIntoThreads
  - phase: 04-09
    provides: ThreadSkeletonBuilder.BuildRelevant (relevant send volume that weights daily-business senders)
provides:
  - NonRelevantSkeletonBuilder.Build(SkeletonContext, IReadOnlyDictionary<NonRelevantCategory,int>, IReadOnlyList<PlannedThread>) -> NonRelevantSkeleton { Threads, Externals }
  - NonRelevantSkeletonBuilder.CreateExternals(int, SkeletonContext)
  - Constants PrivilegedDailyShare, ExternalDailyShare, DecoyDaysAroundFact, SpamImageShare; default external name lists and PersonalDomains
affects: [04-14 non-relevant seeding, 04-16 planner orchestration, 04-plan-numbering, 04-validator]
tech-stack:
  added: []
  patterns: [spec-then-build thread construction, eligible-day sampling per person with a 30-day stability preference, reschedule-to-fit]
key-files:
  created:
    - Services/Corpus/NonRelevantSkeletonBuilder.cs
    - ReelDiscovery.Tests/Corpus/NonRelevantSkeletonTests.cs
  modified: []
key-decisions:
  - "Daily-business root senders are weighted 1 + relevant messages sent; anchors are uniform over the sender's eligible business days (active on the day and 30 days later when possible)"
  - "Cross-organization daily threads (20% chance, or forced when the sender has no active colleague) add one person from a Party/ThirdParty organization as a forced root To recipient"
  - "Privileged daily threads: round(daily threads x 2%), at least 1 with 50+ daily threads; only non-cross threads whose sender organization has an in-house counsel; the counsel is forced into the root To and every participant stays in that organization"
  - "Decoys prefer facts with 2+ cast knowers; the anchor is fact date +/-30 days clamped to the window and kept on a business day inside that range; IssueIds = the fact's issues, FactIds empty"
  - "Attachments use the relevant builder's exact-share rule per category (daily, ancillary, decoy); spam gets round(spam messages x 5%) images only when Image is enabled; newsletters, vendor and personal mail never carry attachments"
  - "Externals: clamp(category messages / 15, 3, 40), groups 40/30/30 (min 1 each); the spam/vendor group alternates starting with Spam; repeated default names get a regional prefix; collisions with any case organization or cast email domain append news, then a digit"
  - "ScheduleToFit reschedules a thread (up to 6 times, moved earlier when it runs past the window end) until every participant is active on each sender-local date and the thread ends inside the window; otherwise a plan note"
metrics:
  duration: 25min
  completed: 2026-10-02
  tasks: 2
  files: 2
---

# Phase 4 Plan 10: Non-Relevant Skeleton Builder Summary

`NonRelevantSkeletonBuilder.Build` reserves every non-relevant slot with exact per-category totals. It structures daily business, ancillary, decoy and newsletter/spam/personal threads using the same shape tables, attachment rule and scheduler as relevant threads, and creates the outside senders in code.

## Tasks

| # | Task | Commits |
|---|------|---------|
| 1 | Daily business, ancillary and decoy threads | 7a41e98 (RED), 7743576 (GREEN) |
| 2 | Newsletters, spam and personal mail with external senders | 5fb4c2d (RED), 5af52a0 (GREEN) |

## What was built

- **Sizes**: `BudgetAllocator.SplitIntoThreads` per category (Business for daily business and decoys, Ancillary for background staff, Bulk singletons for newsletters and spam). Personal mail turns about 10% of its threads into two-message threads and keeps the category total exact.
- **Daily business**: cast senders weighted by relevant volume. Recipients are same-organization colleagues (key and background). About 20% of threads add one person from another Party/ThirdParty organization. About 2% are privileged: in-house counsel is copied and every participant (including forward targets) is from the counsel's own organization, so validator rule 12 never fires and privileged does not imply relevant.
- **Ancillary**: background-tier senders. The root goes to a key person of the same organization 60% of the time, otherwise a background colleague. If the cast has no background staff, key characters are used and a plan note is added.
- **Decoys**: participants are cast knowers of a randomly picked fact, the first message falls within 30 days of the fact (clamped to the window), and the thread carries the fact's IssueIds but never FactIds. MustNotMention and DecoyAngle are left for 04-14.
- **Bulk mail**: the root comes from an `EXT-` sender and goes to exactly one cast member (key weighted 3:1 over background). That recipient is the OwnerCustodianId, so externals are never custodians. Externals are scheduled in `ctx.ZoneFor(EXT)` = `UsTimeZones.Default`.
- **Structure**: replies, kinds, To/CC and forwards follow the same `ThreadShapes` rules as `ThreadSkeletonBuilder`. Forward targets come only from the thread's pool. Thread ids are provisional `N-{n:0000}` (PlanNumbering renumbers them). Message ids are `{T}-M{seq:00}`. Basis is `None`.
- **Tests**: 22 test methods (40 cases): totals, fields, sender weighting, cross-org share, depth tables over 3 seeds, privileged containment, decoy proximity, active ranges and business time, per-category attachment share, enabled types only, custodians, determinism, external ids/addresses/collisions, kind mix, 3:1 weighting, spam images, default zone.

## Verification

- `dotnet test --filter "FullyQualifiedName~NonRelevantSkeletonTests|FullyQualifiedName~ThreadShapesTests"`: 68 passed
- `dotnet test --filter "FullyQualifiedName~ReelDiscovery.Tests.Corpus&Category!=Render"`: 357 passed
- All acceptance greps pass (Build signature 1, SplitIntoThreads 5, ThreadShapeClass.Business 2, consumer domains 1, EXT- 2, test attributes 22)

## Deviations from Plan

### Design adjustments (same behavior as the plan)

**1. Spam names list added.** The plan lists newsletter and vendor names but not spam names. I added `DefaultSpamNames`, and spam senders use `offers@{slug}.com`.

**2. Exact spam image share.** Spam images are exactly round(spam x 5%), not a per-message 5% chance, so the test is deterministic. Same exact-share approach as 04-09.

**3. Stable pools and reschedule-to-fit instead of repair.** Non-relevant threads have no knowledge constraints. So instead of 04-09's swap/truncate repair, I pick people who are active on the anchor and 30 days later (when possible), and reschedule a thread that would break an active range or run past the window end. The totals never change.

**4. Kind mix test measured by messages.** The 40/30/30 split is applied to message counts. Personal replies count toward the personal share.

### Auto-fixed Issues

None.

## Known Stubs

None. Subjects, DecoyAngle and MustNotMention are intentionally empty here; 04-14 fills them.

## Threat Flags

None. External domains are checked against every case organization and cast email domain (T-04-30). The external count is clamped to 40 and every loop is bounded by the message totals (T-04-32). Senders are weighted and shapes/attachments are shared with relevant threads (T-04-31).

## Requirements

PLAN-02, PLAN-05 and PLAT-03 are advanced but stay Pending. Plans 04-13, 04-14 and 04-16 still contribute to PLAN-02, and 04-20 covers the end-to-end PLAN-05/PLAT-03 checks.

## Self-Check: PASSED

- FOUND: Services/Corpus/NonRelevantSkeletonBuilder.cs
- FOUND: ReelDiscovery.Tests/Corpus/NonRelevantSkeletonTests.cs
- FOUND: 7a41e98, 7743576, 5fb4c2d, 5af52a0
