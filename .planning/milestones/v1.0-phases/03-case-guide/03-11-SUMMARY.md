---
phase: 03-case-guide
plan: 11
subsystem: case-guide-generation
tags: [llm, story-tier, regeneration, staleness, hand-edits, tdd]
requires:
  - "03-07: GuideStaleness (Order, IsStale, Stamp, AbsorbHandEdit), CaseGuideEditing (SetCastField, RefreshIdentity)"
  - "03-09: CaseGuideGenerator cast path (SyncOrganizations, CounselGaps, roster mapping, details, background, RunUnitAsync)"
  - "03-10: PlanFactUnits, MapFact, MapValues, GenerateAsync, BuildFactsPrompt/BuildValuesPrompt existing lists"
provides:
  - "CaseGuideGenerator.RegenerateTabAsync(draft, part, guidance, replaceEdited, progress, ct) for Cast/Facts/Values"
  - "CaseGuideGenerator.RegenerateEntryAsync(draft, part, entityId, guidance, ct)"
  - "CaseGuideGenerator.UpdateStaleAsync(draft, progress, partCompleted, ct)"
  - "CaseGuidePrompts.BuildCastEntryPrompt / BuildFactEntryPrompt / BuildValueEntryPrompt; BuildRosterPrompt(draft, guidance, replaceEdited)"
  - "CastEntryDto, FactEntryDto, ValueEntryDto (strict schemas)"
  - "CaseTestData.SampleCounselDraft (Harmon counsel, empty CounselGaps)"
affects: [03-13, 03-14, 03-15, 03-16]
tech-stack:
  added: []
  patterns:
    - "Shared D-19 merge (MergeEntries): existingId or same-entry match claims a revisable entry once per response; protected (hand-edited) ids are skipped with 'Kept your edits to {name}.'; unclaimed revisable entries are removed"
    - "Tab regeneration merges only after a successful response; Cast merges per step (roster, then per-organization background)"
    - "Entry regeneration is applied inside GuideStaleness.AbsorbHandEdit, like a hand edit"
key-files:
  created:
    - ReelDiscovery.Tests/Case/CaseGuideRegenerationTests.cs
    - ReelDiscovery.Tests/Case/CaseGuidePromptInvariantTests.cs
  modified:
    - Services/Case/CaseGuideGenerator.cs
    - Services/Case/CaseGuidePrompts.cs
    - Services/Case/CaseGuideEditing.cs
    - ReelDiscovery.Tests/Case/CaseTestData.cs
decisions:
  - "[03-11]: Regenerate Cast = SyncOrganizations -> roster merge (matched people get DetailsPending, unmatched non-edited key people removed, their facts flagged '{name} was removed from the cast.') -> SeedAuthor -> one counsel top-up -> details -> per non-Individual organization background rewrite (old revisable staff removed only when that organization's call succeeds; count = BackgroundPerOrg - kept)"
  - "[03-11]: Roster merge falls back to a same-full-name match when existingId is missing or unusable; a person naming a hand-edited character (by id or name) is ignored with a Kept note"
  - "[03-11]: Tab regeneration failures (refused/truncated/timeout/fatal) before any merge restore the part's previous status and leave entries untouched; the failure hint is 'Click Regenerate {part} to try again.' A Cast regen that merged the roster but left characters without details is Failed with 'Click Retry Cast.' (resumes through GenerateCastAsync)"
  - "[03-11]: Regenerate Facts assigns current facts to units by SourceSectionId (date slice for split sections); facts from sections no longer planned are left alone; failed units keep their facts; removed facts are dropped from values' FactIds"
  - "[03-11]: Regenerate Values claims by existingId, then same label+value; a repeated label+value in the response merges into the entry already written"
  - "[03-11]: RegenerateEntryAsync is applied like a hand edit (GuideStaleness.AbsorbHandEdit): downstream parts stay fresh and dependent entries are flagged for review (facts via SetCastField rename / active-range change; values via FlagValues)"
  - "[03-11]: UpdateStaleAsync re-checks IsStale before each part; Issues -> GenerateIssuesAsync (re-extract keeps ids and details), Cast/Facts/Values -> RegenerateTabAsync(guidance '', replaceEdited false); GenerateAsync and UpdateStaleAsync share RunTrackedAsync/RecordPart"
metrics:
  duration: 35min
  completed: 2026-10-01
---

# Phase 3 Plan 11: Tab/Entry Regeneration and Update Guide Summary

The case guide generator can now regenerate a whole tab (Cast, Key Facts or Canonical Values) with guidance, regenerate one character, fact or value, and run "Update Guide" over only the stale parts. In all three, matched entries keep their IDs and hand edits are kept unless the user confirmed replacing them.

## What was built

- **RegenerateTabAsync(draft, part, guidance, replaceEdited)**
  - Cast, Facts and Values are supported. Issues throws `ArgumentException` because issues are re-extracted, not regenerated.
  - All three tabs share a generic `MergeEntries` helper:
    - An item's `existingId` (or a same-entry match) claims a revisable entry once per response and updates it in place, clearing `IsUserEdited`.
    - An item that names a protected entry is skipped and records the note "Kept your edits to {name}."
    - Any other item creates a new entry with `IdAllocator.Next`.
    - Revisable entries that no item claimed are removed.
- **Cast regeneration** covers the whole Cast tab (D-19):
  1. `SyncOrganizations`: renamed parties keep their ID and domain, and added parties get a new organization.
  2. The roster prompt lists the current roster under "Current roster (revise by existingId; leave existingId empty for new people):" as `CH-001 | Maria Delgado | President | ORG-01 | Party | ...`, with hand-edited people under "Keep exactly; do not return these:".
  3. The roster merge: new law firms are created, matched people get `DetailsPending`, and renames flag the facts that cite them. Removed people are taken out of the facts' actor and known-by lists, and those facts are marked `NeedsReview` with "{name} was removed from the cast.".
  4. `SeedAuthor`, then at most one counsel top-up, then `SyncCounsel`, then the details batches.
  5. For every non-Individual organization, the background staff are rewritten. Hand-edited staff are kept with a note, and the request asks for `BackgroundPerOrg - kept` people. Old staff are removed only for organizations whose call succeeded.
  6. The part is set Ready and stamped, so Facts become stale.
- **Facts regeneration** reruns every fact unit with that unit's current facts, using the keep-exactly list unless `replaceEdited`. The merge happens on the calling thread after `WhenAll`. New F-IDs are allocated in date order, removed facts are dropped from the values' links, and the part is set Ready and stamped, so Values become stale.
- **Values regeneration** makes one call with the current values and the scanner hits, then merges, sets the part Ready and stamps it. FactsState is not touched.
- **Failures:** a refusal, truncation, timeout or fatal error before the merge leaves the entries byte-identical. This is verified by comparing the guide JSON. A user cancel rethrows and leaves the entries untouched.
- **RegenerateEntryAsync(draft, part, id, guidance)**
  - One Story call with `MaxOutputTokens` 4,000, named "Case Guide - Character/Fact/Value {id}".
  - The prompts carry:
    - the case summary and timeline
    - the issues
    - a compact cast list (fact and value prompts)
    - the entry's current fields
    - for a character, the facts they appear in
    - the name rules
    - the guidance
  - The response is applied in place only after success:
    - **Character renames** go through `CaseGuideEditing.SetCastField`, so the citing facts get the new name in their text and `NeedsReview` is set. Background staff never get a voice note or signature.
    - **Facts** are mapped with the batch mapper: unknown links are dropped with MappingNotes, and KnownBy always includes the actors. `NeedsReview` and `IsUserEdited` are cleared, and values that cite the fact are flagged for review.
    - **Values** have their fact links normalized and checked.
  - Unknown IDs throw `ArgumentException("Unknown {part} '{id}'.")`.
- **UpdateStaleAsync** walks `GuideStaleness.Order` and re-checks `IsStale` before each part. Issues go through `GenerateIssuesAsync`, which keeps IDs and details, so no issue request is made when nothing changed. Cast, Facts and Values go through `RegenerateTabAsync` with empty guidance and `replaceEdited: false`. `partCompleted` is awaited after each part, and the run stops at the first failure or fatal error. GenerateAsync now uses the same `RunTrackedAsync` and `RecordPart` helpers.
- **Prompt invariant theory (W1)** covers 13 request paths:
  - first-generation roster, cast details, background and counsel top-up
  - facts and values
  - issue details
  - Regenerate Cast, Facts and Values
  - the Character, Fact and Value entries

  Each path sends the byte-identical `SystemPrompt`, with no scenario text in it. The user prompt carries the name-rule line and the kept-names override, and there are no JSON instructions.

## Commits

| Task | Commit | Message |
|------|--------|---------|
| 1 RED | 84a34ef | test(03-11): add failing tab regeneration tests |
| 1 GREEN | 81e60b8 | feat(03-11): regenerate Cast, Facts and Values tabs keeping ids and hand edits |
| 2 RED | 1689052 | test(03-11): add failing entry regeneration, Update Guide and prompt invariant tests |
| 2 GREEN | 032104c | feat(03-11): add single-entry regeneration and Update Guide |

## Verification

- `dotnet test --filter "FullyQualifiedName~CaseGuideRegenerationTests|FullyQualifiedName~CaseGuidePromptInvariantTests|FullyQualifiedName~CaseSchemaTests"`: 63 passed. The three entry DTOs are strict.
- Full suite `--filter "Category!=Render"`: 1013 passed, 0 failed.
- `dotnet build` of the app and the tests: 0 warnings, 0 errors.
- Acceptance greps:
  - `RegenerateTabAsync(` signature: 1
  - "Kept your edits to": 1
  - "was removed from the cast.": 1
  - "Current roster (revise by existingId; ...)": 1
  - `SyncOrganizations(draft)` in the Cast regeneration path: yes
  - `RegenerateEntryAsync(`: 1
  - `UpdateStaleAsync(`: 1
  - `GuideStaleness.IsStale`: 1
  - `CaseGuideEditing.SetCastField`: 2
  - entry DTO `typeof` lines: 3
  - `MemberData`: 1, with 13 rows
  - `AppendNameRules(`: 11

## Deviations from Plan

### Auto-fixed / adjusted

**1. [Rule 3 - Blocking] Failure hints for regeneration**
- **Issue:** `UnitRun.Label` and `DescribeOutcome` always produced "Click Retry {part}.", which is wrong after a regeneration that left the tab as it was.
- **Fix:** `UnitRun` now carries a `RetryHint` sentence. The first-generation paths still say "Click Retry Cast.", "Click Retry Key Facts." and so on, unchanged. The regeneration paths say "Click Regenerate {part} to try again."
- **Commit:** 81e60b8

**2. [Rule 3 - Blocking] Shared helpers made internal / extracted**
- `CaseGuideEditing.FlagReferencingFacts` and `FlagValues` are now `internal` so regeneration can reuse their rename and review side effects.
- `MapRoster` was split into `MapOrganizations` and `AddRosterPerson`, the counsel top-up became `TopUpCounselAsync`, and `MapValues` uses a new `MapValue`.
- First-generation behaviour is unchanged, and every existing generator test passes.
- **Commit:** 81e60b8

**3. [Plan detail] Entry regeneration is applied as a hand edit**
- The plan did not say what happens to staleness after a single-entry regeneration. The change runs inside `GuideStaleness.AbsorbHandEdit`, so downstream parts stay fresh and the dependent entries are flagged with NeedsReview instead (D-20).
- An active-range change also flags the facts that cite the character.

**4. [Plan detail] Test fixture placed in CaseTestData**
- `SampleCounselDraft()` was added to `CaseTestData`, which is not in the plan's file list, because both new test files use it.
- The invariant tests reuse the canned-response helpers from `CaseGuideRegenerationTests` (internal static).

**5. [Plan detail] Partial background failure during Regenerate Cast**
- When one organization's background call fails, that organization keeps its old staff. The cast is still set Ready and stamped because the data is complete, but the result reports `Succeeded = false` with the message "Background staff for N organizations were not rewritten: ...".

## Requirements

GUIDE-08 and GUIDE-09 are implemented at the service level only. They are not marked complete, following the 03-10 precedent: the tabs that call `RegenerateTabAsync` and `RegenerateEntryAsync` (03-13..03-15) and the Update Guide banner (03-16) are not built yet.

## Known Stubs

None.

## Threat Mitigations

- **T-03-37:** Hand-edited entries are excluded from revision unless `replaceEdited` is set. If the model returns an edited entry anyway, by ID or by matching name, that item is ignored and a "Kept your edits to {name}." note is recorded. Tests: RegenCast_HandEditKept, RegenCast_BackgroundRewritten and RegenValues.
- **T-03-38:** Each ID is claimed once per response, and unknown or reused IDs get new IDs. Tests: RegenCast_DuplicateExistingIdInResponse and RegenCast_KeepsMatchedIds.
- **T-03-39:** `UpdateStaleAsync` runs only when the user calls it, and only for parts where `IsStale` is true. Tests: UpdateStale_SkipsFresh and UpdateStale_NothingStale.
- **T-03-40:** Nothing is mutated until `GetValueOrThrow` succeeds. Tests: RegenEntry_Failure_KeepsOld, which compares the draft JSON, and RegenTab_FailureLeavesEntriesUntouched.

## TDD Gate Compliance

Each task has a `test(03-11)` RED commit that does not compile because the members are missing, followed by a `feat(03-11)` GREEN commit. No refactor commits were needed.

## Self-Check: PASSED
