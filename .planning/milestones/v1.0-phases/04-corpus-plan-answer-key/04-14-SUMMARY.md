---
phase: 04-corpus-plan-answer-key
plan: 14
subsystem: corpus
tags: [planner, llm, subject-seeding, decoys, must-not-mention, re-ask, templates, tdd]
requires:
  - phase: 04-01
    provides: PlannedThread (Subject, DecoyAngle, MustNotMention, UsesFallbackContent), ExternalParty
  - phase: 04-02
    provides: PlanCallRun, PlanCallRunner.RunAsync, PlanProgress/PlanStage.PlanningOther, FillResult, CorpusTestData.SlotIdsIn
  - phase: 04-07
    provides: CorpusPlanPrompts.SystemPrompt, CaseGuidePrompts.AppendNameRules
  - phase: 04-10
    provides: NonRelevantSkeletonBuilder.Build (categories, decoy IssueIds, EXT-xxx roots), PersonalDomains
provides:
  - SubjectSeeder(ILlmGateway, maxParallel = 3)
  - SubjectSeeder.SeedAsync(draft, nonRelevant, externals, progress, ct) -> FillResult
  - SubjectSeeder.MustNotMention(guide), TemplateSubject(thread, guide, externals, random), RoutineTopics
  - SubjectSeeder.BuildBatches / BuildSeedPrompt / BuildDecoyPrompt / SchemaTypes (internal)
  - SubjectSeedBatchDto / SubjectSeedDto (strict, text-only)
  - CaseGuidePrompts.AppendNameRules(sb, draft, includeScenario = true)
affects: [04-planner-orchestration, 04-answer-key, 06-non-relevant-renderer]
tech-stack:
  added: []
  patterns: [fact-free everyday prompts, forbidden-term post-check with one re-ask then template, first-seed-wins external renames with collision-safe slugs]
key-files:
  created:
    - Services/Corpus/SubjectSeeder.cs
    - ReelDiscovery.Tests/Corpus/SubjectSeederTests.cs
  modified:
    - Services/Case/CaseGuidePrompts.cs
key-decisions:
  - "The everyday seed prompt omits the user's case scenario (AppendNameRules includeScenario: false): the scenario names the project and the dispute, which D-14 keeps out of daily/ancillary/bulk planning. The decoy prompt keeps it"
  - "MustNotMention = fact titles + Amount/InvoiceNumber/ChangeOrderNumber/ContractNumber/PurchaseOrder values with at least one fact id (plan wording; one-line fact descriptions from RESEARCH are not listed because a whole-sentence contains-check never matches)"
  - "Slot ids S-0001.. follow thread order; everyday batches come first, then decoy batches; a decoy seed with a blank angle or any forbidden term (case-insensitive, subject or angle) counts as missing"
  - "Slot lines label decoys 'Near-miss' and external roots by kind (Newsletter / Spam / Vendor / Personal, sender 'external: {kind}'); sender text is id + name, title, department, organization; recipients are id + name"
  - "Externals: first accepted seed per sender wins; names equal to a case organization name/short name or cast full name keep the default identity; business slug = Fold(name) capped at 30, '.com', 'mail' then 'mail{n}' on collision with case/cast, consumer, reserved or other external domains; mailbox prefix kept (news@/offers@); personal keeps its consumer domain with {first}.{last}"
  - "Templates: daily/ancillary '{routine topic}', decoy '{Project} {routine topic}' (first ProjectName value) plus angle 'A routine update unrelated to any dispute.', newsletter '{sender} weekly update', spam 'Limited-time offer from {sender}', vendor '{sender} account update', personal 'Weekend plans'"
  - "Id tokens stripped from seeds: CH/F/ISS/T/N/V/ORG/EXT-nn..nnnn (slot ids S- are not stripped); RE:/FW: prefixes removed; subject cap 120, sender name cap 60"
metrics:
  duration: 12min
  completed: 2026-10-02
  tasks: 2
  files: 3
---

# Phase 4 Plan 14: Subject Seeder Summary

Batched Story-tier subject seeding for every non-relevant thread: fact-free prompts for everyday traffic, a project/organization prompt with an explicit must-not-mention list for near-miss decoys, a post-check that re-asks once and then templates, and safe renames for outside senders.

## What Was Built

- **`Services/Corpus/SubjectSeeder.cs`**
  - `BuildBatches`: S-0001.. in thread order, at most 100 slots per batch, decoys never share a batch with everyday slots.
  - `BuildSeedPrompt`: name rules without the scenario, one line per slot (`S-0001 | Daily business | from CH-002 Tom Becker, Project Manager, Brightline Glazing, LLC | to CH-009 Owen Reilly | Mar 2024 | 3 messages | attachments: Word`), and the everyday task. It contains no fact ids, titles or descriptions, no issues, and no canonical values.
  - `BuildDecoyPrompt`: name rules with the scenario, `Projects:` (ProjectName values), `Companies and organizations:`, the slot lines, the near-miss task and `Do not mention any of these:` followed by the forbidden list.
  - `MustNotMention(guide)`: fact titles plus fact-linked Amount/Invoice/Change-order/Contract/PO values. Project and organization names are never on the list.
  - `SeedAsync`: builds every prompt first, runs bounded parallel `PlanCallRunner.RunAsync<SubjectSeedBatchDto>` calls (Story tier, `Corpus Plan - Other email (batch i of n)`, 8,000 output tokens) and applies results on the calling thread after `WhenAll`. It re-asks once per batch for missing or forbidden slots (refused batches are not re-sent), then fills templates and sets `UsesFallbackContent`. It stores `MustNotMention` on every decoy and renames externals. Auth/SpendLimit errors stop every batch and set `FatalError`; a user cancel throws. Progress is reported as `PlanningOther` per batch.
- **`Services/Case/CaseGuidePrompts.cs`**: `AppendNameRules` gained an optional `includeScenario` parameter (default true, so existing callers are unchanged).
- **`ReelDiscovery.Tests/Corpus/SubjectSeederTests.cs`**: 23 tests covering the strict schema, batching, the slot line format, no case facts in everyday prompts, the must-not-mention list, the decoy prompt, JSON/fictional wording, the happy path, external renames and caps, the forbidden-subject and forbidden-angle re-ask, unknown and missing slots, templates by category, id stripping and the subject cap, refused/failed/fatal/cancel handling, progress, and empty input.

## Verification

- `dotnet test ... --filter "FullyQualifiedName~SubjectSeederTests"`: 23/23 passed
- `dotnet test ... --filter "FullyQualifiedName~ReelDiscovery.Tests.Corpus&Category!=Render"`: 559/559 passed
- `dotnet test ... --filter "FullyQualifiedName~ReelDiscovery.Tests.Case&Category!=Render"`: 1092/1092 passed (AppendNameRules change)
- `dotnet build ReelDiscovery.csproj`: succeeded, no warnings
- Acceptance greps: SubjectSeedBatchDto 1, SlotsPerBatch = 100 1, "Do not mention any of these" 1, RunAsync<SubjectSeedBatchDto> 2, "MustNotMention = " 2, "Corpus Plan - Other email 1, [Fact] count 23

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 2 - Missing critical functionality] Everyday prompt drops the case scenario**
- **Found during:** Task 1
- **Issue:** The plan says both prompts start with `CaseGuidePrompts.AppendNameRules`, which always writes the user's scenario. The RichDraft scenario names "Lakeview Tower" (a canonical value) and describes the unpaid change-order dispute. That breaks D-14 (no case facts or values in everyday prompts) and the plan's own no-values assertion.
- **Fix:** Added `bool includeScenario = true` to `AppendNameRules`. The everyday prompt passes `false` and keeps the name-keeping and substitution lines; the decoy prompt and all existing callers still get the scenario.
- **Files modified:** Services/Case/CaseGuidePrompts.cs (outside the plan's files_modified)
- **Commit:** c733cab

No other deviations. The must-not-mention list follows the plan (fact titles + values) rather than RESEARCH's additional one-line fact descriptions; this is recorded as a key decision.

## TDD Gate Compliance

- Task 1: RED `9cb4401` test(04-14) -> GREEN `c733cab` feat(04-14)
- Task 2: RED `94605b0` test(04-14) -> GREEN `c3ac0c6` feat(04-14)
- No refactor commits were needed.

## Known Stubs

None. `SubjectSeeder` is not wired into a planner orchestrator yet; that is later Phase 4 work, as for `RelevantThreadFiller` (04-13).

## Requirements

PLAN-02 and PLAT-03 move forward but are not complete: planner orchestration and the remaining tests come in later plans. Neither is marked complete.

## Self-Check: PASSED

- FOUND: Services/Corpus/SubjectSeeder.cs
- FOUND: ReelDiscovery.Tests/Corpus/SubjectSeederTests.cs
- FOUND: 9cb4401, c733cab, 94605b0, c3ac0c6
