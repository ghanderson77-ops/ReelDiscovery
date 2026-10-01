---
phase: 03-case-guide
plan: 09
subsystem: case-guide-generation
tags: [llm, story-tier, strict-schema, cast, issues, counsel, parallel, tdd]
requires:
  - "03-01: CaseGuide model, GuidePartState, ComplexityPreset.BackgroundPerOrg"
  - "03-03: IdAllocator, IssueExtractor, EmailIdentity, SignatureBuilder, UsTimeZones"
  - "03-07: GuideStaleness.Stamp/BasisFor, CaseGuideEditing.RefreshIdentity"
provides:
  - "Services/Case/CaseGuidePrompts.cs (SystemPrompt, AppendNameRules, AppendGuidance, BuildIssueDetailsPrompt, BuildRosterPrompt, BuildCastDetailsPrompt, BuildBackgroundPrompt, BuildCounselTopUpPrompt, StoryEnd)"
  - "Services/Case/CaseGuideGenerator.cs (GenerateIssuesAsync, RetryIssueDetailsAsync, GenerateCastAsync, SyncOrganizations, CounselGaps, SchemaTypes; GuideProgress, GuidePartResult)"
affects: [03-10, 03-11, 03-13, 03-14]
tech-stack:
  added: []
  patterns:
    - "Parallel units return DTOs through RunUnitAsync (semaphore + linked CTS, full catch ladder); the guide is changed only on the calling thread after Task.WhenAll (in a finally block, so finished units survive a user cancel)"
    - "Resume = key cast exists and CastState is not Ready: no roster/top-up call; only DetailsPending characters and organizations missing from CompletedUnits are sent"
key-files:
  created:
    - Services/Case/CaseGuidePrompts.cs
    - Services/Case/CaseGuideGenerator.cs
    - ReelDiscovery.Tests/Case/CaseGuideGeneratorTests.cs
  modified:
    - Services/Case/CoreDocumentGenerator.cs
    - ReelDiscovery.Tests/Case/CaseSchemaTests.cs
decisions:
  - "[03-09]: CaseGuidePrompts.SystemPrompt says \"Keep each person, company and organization ...\" instead of \"Keep every ...\": the plan's exact text matched its own D-01 regex (every person ... fictional with no period between)"
  - "[03-09]: Issue details that leave some issues without a description mark IssueDetailsState Failed \"Details for {n} issues were not written. Click Retry Issue Details.\"; details for ids not in the request are ignored with a Note"
  - "[03-09]: Counsel top-up runs only right after a roster call (never on resume) and at most once; a failed top-up becomes a Note, gaps remain validator warnings"
  - "[03-09]: CounselGaps mirrors CaseGuideValidator rule 17 exactly (in-house counsel per Party org; firm with Partner + Associate titles per Party/Individual org)"
  - "[03-09]: Signing attorney seeded as Partner at the counsel firm (created if missing, representing Plaintiff/Complainant orgs); memo author seeded from Memo.From at the Memo.Organization (Agency for government memos)"
  - "[03-09]: Background staff are deduplicated only within their organization (identical canned responses map for every org); extra people beyond the needed count are dropped"
  - "[03-09]: Cast-wide failure message when only background is missing: \"Background staff for {m} organizations were not written. Click Retry Cast.\""
  - "[03-09]: GuidePartResult/GuideProgress use auto-properties (project convention) instead of the public fields in the plan contract"
metrics:
  duration: 10min
  completed: 2026-10-01
---

# Phase 3 Plan 09: Case Guide Generator (Issues and Cast) Summary

The new Story-tier `CaseGuideGenerator` extracts the issues in code and enriches them with one call. It then builds the cast in four steps: organizations from the parties, a roster call plus at most one counsel top-up, detail batches of up to 10 people with up to 3 in flight, and background staff for each organization. Code assigns every ID, email address, phone number and signature. Every prompt keeps the scenario's names (D-01) and takes a firm position (D-02).

## What was built

- **CaseGuidePrompts**: a const, byte-stable `SystemPrompt`, `AppendNameRules` (keeps names from fictional works and lists both the applied substitutions and the names the user kept), `AppendGuidance`, and builders for issue details, roster, cast details, background staff and the counsel top-up. Every cast prompt calls `AppendNameRules`. No prompt contains JSON-format instructions.
- **Issues**: `IssueExtractor.Extract` sets IssuesState to Ready and stamps it. A single "Case Guide - Issue details" request (8,000 tokens) then fills in the description, elements and relevance criteria for each issue that has none. `RetryIssueDetailsAsync` resends only the issues still missing details.
- **Cast**: `SyncOrganizations` matches parties to organizations by PartyId and is idempotent: a renamed party keeps its ID and domain. It returns the IDs it added. The roster mapper resolves organization references by ID (after normalization) or by exact name. People whose organization cannot be resolved are dropped with a Note. Duplicate names are skipped. Each new person gets a CH ID, a fictional phone number and an email from `RefreshIdentity`. `doc.Counsel.Email` is synced from the signing attorney's cast entry. Details map by castId within each batch. Invalid or reversed active dates fall back to the timeline start through the filing date, with a Note. Signatures are built by `SignatureBuilder.Build`. Background staff get Tier Background, Role Staff, a Department and their role line as Involvement. Each finished organization is added to `CastState.CompletedUnits`.
- **Outcomes**: Ready stamps the cast, clears CompletedUnits and orders the cast with key characters by ID, then background staff by organization. A partial run is marked Failed with "Details for {n} characters[ and staff for {m} organizations] were not written. Click Retry Cast." A fatal error sets the part back to Pending, records FatalError and cancels the remaining calls. A user cancel sets it to Pending, keeps finished units and rethrows. A refused roster marks the part Failed with "The model declined: ...".
- The `CoreDocumentGenerator.CounselDto.Email` description now reads "Attorney email at the law firm's own .com domain (never a reserved .example domain)".

## Commits

| Task | Commit | Message |
|------|--------|---------|
| 1 RED | 580f339 | test(03-09): add failing case guide prompt and issue generation tests |
| 1 GREEN | 605ecee | feat(03-09): add CaseGuidePrompts and issue extraction + enrichment |
| 2 RED | 6d0da04 | test(03-09): add failing cast pipeline tests |
| 2 GREEN | b64654c | feat(03-09): add cast pipeline with code-owned identities |

## Verification

- `dotnet test --filter "FullyQualifiedName~CaseGuideGeneratorTests|FullyQualifiedName~CaseSchemaTests|FullyQualifiedName~CoreDocumentGeneratorTests"`: green. CaseGuideGeneratorTests has 29 tests, all four new schema types are strict, and none has more than 16 union types.
- Full suite `--filter "Category!=Render"`: 916 passed, 0 failed.
- `dotnet build`: 0 warnings, 0 errors.
- Acceptance greps: all pass. `AppendNameRules(` appears 6 times. `GuideStaleness.Stamp` appears 2 times. `Task.WhenAll` appears 3 times. The D-01 regex finds 0 matches in CaseGuidePrompts.cs, and `.example` appears once in CoreDocumentGenerator.cs (inside the "never a reserved .example domain" description).
- Merge_NotInParallel was checked by code review. The two `guide.Cast.Add` calls are in `AddKeyMember`, which runs during roster mapping on the calling thread, and in `MapBackground`, which runs in the `finally` block after `Task.WhenAll`.
- Details_Batched runs through a test-only `SlowProvider` wrapped around FakeLlmProvider. It adds a 50 ms delay and records the peak number of calls in flight, which falls between 2 and `MaxParallelCalls`.

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 1 - Bug] The required system prompt failed its own D-01 regex**
- **Found during:** Task 1
- **Issue:** The plan's exact text begins "Keep every person, company and organization ... which are already fictional". That matches `every (person|character|company)[^.]*fictional`, which both the behavior test and the acceptance grep require to find nothing.
- **Fix:** Changed "every" to "each" in that sentence. Everything else, including "including characters and companies from films, TV series, books or games" and "never rename them", is unchanged.
- **Files modified:** Services/Case/CaseGuidePrompts.cs
- **Commit:** 605ecee

**2. [Plan conflict] Background_PerOrg vs Cast_Ready on CompletedUnits**
- **Issue:** Background_PerOrg expects CompletedUnits to contain every organization ID, but Cast_Ready requires CompletedUnits to be cleared once the part is Ready.
- **Fix:** Background_PerOrg queues a refused details response, so the part ends Failed and CompletedUnits is kept. Cast_Ready asserts that CompletedUnits is empty.
- **Commit:** 6d0da04

**3. [Test detail] CounselDto description test**
- The plan says the description "no longer contains .example" but also requires the text "(never a reserved .example domain)". The test asserts that "at an .example domain" is gone and that "own .com domain" is present.

## Requirements

GUIDE-01..04 are implemented at the service level only. This plan does not mark them complete because no wizard step calls the generator yet (the StepCaseGuide plans do that), and regeneration is planned for 03-11.

## Known Stubs

None.

## Threat Mitigations

- **T-03-29**: Scenario and document text go only into user prompts. The system prompt is a const. Strict schemas limit the shape of the output.
- **T-03-30**: Every model ID and organization reference goes through `IdAllocator.Normalize` and an existence check. Unknown references are dropped with Notes, and details are accepted only for IDs in the current request or batch.
- **T-03-31**: Detail batches hold at most 10 people with at most 3 calls in parallel, every call has a max-output-token limit, a fatal error cancels the linked CTS, and the top-up never loops.
- **T-03-32**: `AppendNameRules` appears in every guide prompt.
- **T-03-33**: Domains are derived in code (.com, .gov for agencies) and phone numbers use the 555-01xx range.

## TDD Gate Compliance

Each task has a `test(03-09)` RED commit that fails to compile because the types and members did not exist yet, followed by a `feat(03-09)` GREEN commit. No refactor commits were needed.

## Self-Check: PASSED
