---
phase: 03-case-guide
verified: 2026-10-01T00:00:00Z
status: passed
score: 92/95 must-haves verified (4/4 roadmap success criteria verified in code; 3 plan truths need a human / 150% display)
overrides_applied: 0
human_verification:
  - test: "Live guide on an 'inspired by The Office' scenario, once with Claude and once with OpenAI: run Describe the Case -> Core Document -> Case Guide end to end, then inspect the Cast tab"
    expected: "Dunder Mifflin, Michael Scott, Ryan Howard, Oscar Martinez, David Wallace are kept exactly (D-01); real companies follow the substitution choices; in-house counsel + outside firm partner/associate per represented org; background staff per org; emails like michael.scott@dundermifflin.com (D-14); facts read as firm statements (D-02)"
    why_human: "Live model output. Note: EmailIdentity maps 'Dunder Mifflin Paper Company, Inc.' to dundermifflinpaper.com (CastIdentityTests), so the exact domain depends on how the core document names the company - confirm the result looks believable"
  - test: "Case Summary -> Export All to Word, then open each of the three .docx files (core document, cast, key facts) in Microsoft Word"
    expected: "All three open without a repair prompt; core document has caption/memo header, numbered paragraphs, signature; cast has org/key/background tables + profiles; key facts has chronological table + index by issue"
    why_human: "Needs Word; automated tests only prove WordprocessingDocument.Open + zero OpenXmlValidator errors"
  - test: "Close the app mid-guide (and once at Case Summary), relaunch, open the case from Recent cases and via Open Case..."
    expected: "case.json reloads, the wizard lands on the furthest stage reached (Case Guide / Case Summary), partial guide shows Resume Guide (no auto-run), 'Opened {title}.' is shown"
    why_human: "UI + filesystem + process restart"
  - test: "Close the window (X) while a guide part is being written (WR-01); answer Yes to C12"
    expected: "Run is cancelled, units that already returned are merged and saved; on reopen they are present; no hang beyond ~10 s"
    why_human: "Requires a live LLM run; REVIEW-FIX marks WR-01 'fixed: requires human verification'"
  - test: "Regenerate Document after a guide exists with parties returned in a different order / a retitled count (WR-03)"
    expected: "Organizations keep their IDs, domains and cast; facts on a retitled issue are flagged needs-review"
    why_human: "Matching logic exercised only with fakes; REVIEW-FIX marks WR-03 'fixed: requires human verification'"
  - test: "Switch the display to 150% (144 DPI), run `dotnet test --filter Category=Render`, and review Case Guide (all tabs, banner, Checks), Case Summary and Describe the Case visually"
    expected: "Render suite green at 144 DPI; screens aligned on Content x = 16, no clipping, not stock-looking (D-04)"
    why_human: "Current display is 120 DPI; the harness was relaxed to accept 120 or 144, so the 144-DPI reference renders were not produced in this state; visual quality is a human judgment"
---

# Phase 3: Case Guide Verification Report

**Phase Goal:** The core document becomes a structured, validated, ID-stable case guide (issues, cast, dated facts, canonical values). This guide is the single source of truth for everything generated downstream.
**Verified:** 2026-10-01
**Status:** human_needed
**Re-verification:** No - initial verification

## Goal Achievement

### Roadmap Success Criteria

| # | Truth | Status | Evidence |
|---|-------|--------|----------|
| 1 | Issue list, Cast (in-house + outside counsel + per-org background staff), dated Key Facts; stable IDs; each fact linked to issues, actors, who knew | VERIFIED | `Models/Case/CaseGuide.cs` (IdCounters, Issues/Organizations/Cast/Facts/Values); `Services/Case/IdAllocator.cs` (counters never reused); `IssueExtractor.Extract` -> `IdAllocator.Next`; `CaseGuideGenerator` roster/details/background/counsel top-up (`CounselGaps` lines 545-570, `Preset.BackgroundPerOrg` lines 962/1963); facts with IssueIds/ActorIds/KnownByIds; UI: `IssuesTabPanel` (read-only, `GridEditable => false`), `CastTabPanel`, `FactsTabPanel` (sortable grid + detail + 3 `LinkPicker`s) |
| 2 | Canonical values registry; validator reports inconsistencies (dates out of range, missing cast/issues) | VERIFIED | `ValuesTabPanel`; `CaseGuideValidator.Validate` (740 lines) with rules: orphan issue/cast links, "outside the timeline", "after the {dateKind} date", inactive actor/knower ranges, duplicate IDs/emails, reserved domains, counsel coverage, preset ranges, scanned amounts/identifiers not in registry (`CanonicalValueScanner.Scan`), real-name leaks (`NameSubstitution.FindLeaks`), stale/unfinished parts; shown in Checks card, status dots (`WorstFor`) and Next gate |
| 3 | Edit + regenerate cast and timeline; editing core doc/cast/facts warns and marks downstream stale | VERIFIED | `CaseGuideEditing` (Add/Set/Delete, every edit through `GuideStaleness.AbsorbHandEdit` x12); `CaseGuideGenerator.RegenerateTabAsync/RegenerateEntryAsync/UpdateStaleAsync`; `GuideStaleness.IsStale` by basis hash; StepCaseGuide banner with Update Guide / Not Now wired (lines 483-484); `StepCoreDocument` guide notice (line 153) + C9 confirm on regenerate; cast name/org/range edits mark facts NeedsReview |
| 4 | case.json next to output, reloadable; export core doc, cast, key-facts as .docx | VERIFIED (code) / human for Word + reopen | `CaseStore` (atomic temp+rename, 50 MB cap, newer-version refusal, corrupt -> message, recent.json max 8, Documents\ReelDiscovery\Cases); `StepCaseScenario` Open Case.../Recent -> `CaseStore.Load` -> `NavigateToStage(Stage)`; `WizardForm.StepTypeForStage`; `CaseGuideDocxExporter.Build` for CoreDocument/Cast/KeyFacts written via `CaseStore.WriteAtomicAsync` from `StepCaseSummary` |

### Locked Decisions (03-CONTEXT D-01..D-21)

| Decision | Status | Evidence |
|----------|--------|----------|
| D-01 names kept incl. fictional works; scenario text in prompts | VERIFIED | `CaseGuidePrompts.SystemPrompt` ("including characters and companies from films, TV series, books or games ... never rename them"); `AppendNameRules` -> `AppendScenario` (EffectiveScenario, 8,000-char cap, keep-names rule) called by all 10 builders; all 12 `SystemPrompt =` sites in the generator use the shared constant; no "must be fictional" wording anywhere in `Services/` |
| D-02 firm position | VERIFIED | System prompt: "Take a firm position ... Do not hedge" |
| D-03 Story tier via gateway | VERIFIED | 12x `LlmTier.Story`; `CreateGenerator() => new CaseGuideGenerator(_state.CreateGateway())` (StepCaseGuide.Run.cs:64) |
| D-04 theme + 144 DPI renders | UNCERTAIN | Theme helpers used throughout; harness now accepts 120 DPI and the current display is 120 -> 150% confirmation is a human item |
| D-05 one step with 4 tabs | VERIFIED | `StepCaseGuide` + segmented tab toggles, 4 `GuideTabPanel` subclasses |
| D-06 auto-run on entry, ordered, cancel, partial kept | VERIFIED | `OnEnterStepAsync` runs `GenerateOrResumeAsync` only when `Guide == null`; `GenerateAsync` issues->cast->facts->values; Cancel (C2) + finally save |
| D-07 sortable facts table + detail | VERIFIED | `FactsTabPanel`, sort preserved on rebuild (`GuideTabPanel` line 484) |
| D-08 issues from counts/findings, read-only | VERIFIED | `IssueExtractor`; `IssuesTabPanel.GridEditable => false`, footer note |
| D-09 per-case folder | VERIFIED | `CaseStore` CasesRoot = MyDocuments\ReelDiscovery\Cases; `CaseFolderNaming.UniqueFolderPath`; Move Case Folder in summary |
| D-10 autosave, no discard | VERIFIED (code) | Guide: `partCompleted` -> `SaveAsync` after each part, `NotifyEdited` -> `QueueSave`, run finally, leave; Core doc: SaveAsync after outline/section/apply edit/regenerate; `OnFormClosing` flushes + `SaveNow`, prompts only C10/C11/C12 |
| D-11 reopen | VERIFIED (code) | Recent cases card + Open Case... + `NavigateToStage`; `AdvanceStage` never regresses |
| D-12 Case Summary | VERIFIED | `StepCaseSummary` (folder, last saved, contents, Export per doc + Export All, Open file, Open folder after `Directory.Exists`, Move folder, Finish) |
| D-13 preset-driven cast | VERIFIED | Roster prompt uses CharactersMin/Max; BackgroundPerOrg 3/6/10; FactsMin/Max targets |
| D-14 .com identities | VERIFIED | `EmailIdentity` (.com / .gov for agencies, reserved suffix rejection, first.last@) |
| D-15 counsel | VERIFIED | Counsel gaps + top-up call + validator rule |
| D-16 voice note + signature | VERIFIED | `SignatureBuilder` (code-built), voice field edited in Cast detail |
| D-17 cast fields | VERIFIED | CastMember fields + validator checks |
| D-18 table + detail editing | VERIFIED | `GuideTabPanel` grid card + detail card |
| D-19 regenerate tab/entry, keep hand edits | VERIFIED | `RegenerateTabDialog` (guidance + replace-edited), `RegenerateEntryAsync` |
| D-20 staleness banner + Update Guide | VERIFIED | `GuideStaleness`, banner, `UpdateStaleAsync`, C1 confirm |
| D-21 add/delete, orphan flags | VERIFIED | `DeleteReferencesDialog` (Keep / Delete Only / Remove from Facts and Delete); validator orphan rules |

### Plan must-haves (03-01..03-20)

All artifacts in plan frontmatter exist and are substantive (37 files, 37-2904 lines). All key-link patterns match, except two in 03-16 that are satisfied in the partial class `UserControls/StepCaseGuide.Run.cs` (`new CaseGuideGenerator(_state.CreateGateway())` at line 64; `_state.CaseStore.SaveAsync(Draft)` at line 38) - WIRED.

| Plan truth | Status | Note |
|-----------|--------|------|
| 03-02 "harness fails loudly when the display is not 144 DPI" | UNCERTAIN | `RenderHarness.CheckDpi` now fails only below 120 DPI (`MinimumDpi = 120`). Intent (fail on unscaled display) kept; 144 is no longer enforced |
| 03-20 "whole suite green on the 150% display" | UNCERTAIN | Green at 120 DPI (non-render 1125/1125 re-run here; render 54/54 per caller); not at 144 |
| 03-20 "user confirms at 150% ..." | UNCERTAIN | Human check plan, not yet summarized |
| All other 88 plan truths | VERIFIED | Spot-checked through code + passing tests |

**Score:** 92/95 (4 roadmap SCs + 91 plan truths, overlapping); 0 failed.

### Key Link Verification (highlights)

| From | To | Via | Status |
|------|----|-----|--------|
| CaseDraft | CaseGuide | `public CaseGuide? Guide` | WIRED |
| StepCaseGuide(.Run) | CaseGuideGenerator | GenerateAsync / UpdateStaleAsync / RegenerateTab/Entry / RetryIssueDetails | WIRED |
| StepCaseGuide(.Run) | CaseStore | SaveAsync after each part, edit, leave | WIRED |
| StepCaseGuide | CaseGuideValidator | `RefreshShell` -> Validate -> Checks/dots/Next gate | WIRED |
| CaseGuideEditing | GuideStaleness | AbsorbHandEdit (12 sites) | WIRED |
| CaseGuideValidator | Scanner / NameSubstitution / GuideStaleness | Scan, FindLeaks, StaleParts | WIRED |
| Cast/Facts/Values tab panels | CaseGuideEditing | Set*/Add*/Delete*/MarkReviewed | WIRED |
| StepCaseSummary | CaseGuideDocxExporter + WriteAtomicAsync | Export | WIRED |
| WizardForm | StepCaseGuide, StepCaseSummary | StepTypesFor(CaseDriven) | WIRED |
| WizardForm.OnFormClosing | CaseStore.SaveNow | flush + save; CancelAndWaitForRun on C12 | WIRED |
| StepCoreDocument | CaseStore.SaveAsync | autosave | WIRED |
| StepCaseScenario | CaseStore.GetRecentCases/Load -> NavigateToStage | reopen | WIRED |

### Data-Flow Trace (Level 4)

| Artifact | Data | Source | Real data | Status |
|----------|------|--------|-----------|--------|
| Tab panels | `Draft.Guide` lists | CaseGuideGenerator (gateway) / CaseStore.Load | Yes | FLOWING |
| Checks card / dots | `_findings` | `CaseGuideValidator.Validate(Draft)` each refresh | Yes | FLOWING |
| Case Summary | draft + store LastSavedUtc | `_state.Case`, `CaseStore` | Yes | FLOWING |
| Exports | bytes | `CaseGuideDocxExporter.Build(Draft, kind)` | Yes | FLOWING |

### Behavioral Spot-Checks

| Behavior | Command | Result | Status |
|----------|---------|--------|--------|
| App builds clean | `dotnet build ReelDiscovery.csproj` | 0 warnings, 0 errors | PASS |
| Non-render suite (generator w/ fakes, validator, store, exporter + OpenXmlValidator, prompt invariants incl. scenario in all 13 request paths) | `dotnet test --filter "Category!=Render"` | 1125/1125 passed | PASS |
| Render suite | not re-run (opens windows); caller reports 54/54 at 120 DPI | - | SKIP -> human (144) |

### Requirements Coverage

| Req | Status | Evidence |
|-----|--------|----------|
| GUIDE-01 | SATISFIED | IssueExtractor + issue details call + Issues tab |
| GUIDE-02 | SATISFIED | Cast fields, IDs, EmailIdentity, active ranges, involvement |
| GUIDE-03 | SATISFIED | Counsel gaps/top-up + validator counsel rule |
| GUIDE-04 | SATISFIED | Background staff per org by preset |
| GUIDE-05 | SATISFIED | Facts per section, dated, IssueIds/ActorIds/KnownByIds, F-nnn in date order |
| GUIDE-06 | SATISFIED | Values call seeded by scanner; Values tab; validator registry checks |
| GUIDE-07 | SATISFIED | CaseGuideValidator |
| GUIDE-08 | SATISFIED | Editing + regenerate tab/entry |
| GUIDE-09 | SATISFIED | GuideStaleness + banner + C9 + NeedsReview |
| GUIDE-10 | SATISFIED (human: reopen) | CaseStore + recent + NavigateToStage |
| GUIDE-11 | SATISFIED (human: Word) | CaseGuideDocxExporter + Summary export |

No orphaned requirements: REQUIREMENTS.md maps exactly GUIDE-01..11 to Phase 3, all claimed by plans.

### Anti-Patterns Found

| File | Pattern | Severity | Impact |
|------|---------|----------|--------|
| ReelDiscovery.Tests/Render/RenderHarness.cs | DPI guard relaxed to 120 | Warning | 144-DPI reference renders not guaranteed by CI-style runs |
| deferred-items.md | LinkPicker long items cut (no ellipsis); Values grid scrolls sideways at minimum size; Cast/Facts filters not reset on case switch | Info | Cosmetic; candidates for the 150% visual check |
| deferred-items.md | Flaky RegenerateTabDialog checkbox render (once) | Info | Layout-timing race in a render test |

No TODO/placeholder/stub implementations found in phase files.

### Human Verification Required

See frontmatter `human_verification` (6 items): live "The Office" run on both providers, Word opening the exports, reopen-at-furthest-stage, close-during-run (WR-01), party reorder / retitled count after Regenerate Document (WR-03), and the 150% render + visual check (D-04).

### Gaps Summary

No blocking gaps. Every roadmap success criterion and locked decision is implemented and wired in code, and the automated suite passes. What remains is the planned 03-20 human check plus the two review fixes flagged "requires human verification" (WR-01, WR-03). D-04 visual quality also needs confirmation at 144 DPI, because the harness now accepts 120 DPI.

If the 120-DPI acceptance is intended, record it with an override on the 03-02 truth:

```yaml
overrides:
  - must_have: "The harness fails loudly (not silently passes) when the display is not 144 DPI"
    reason: "Layouts are DPI-scaled; harness fails below 120 DPI (unscaled) and 144 remains the reference for the human check"
    accepted_by: "{name}"
    accepted_at: "{ISO timestamp}"
```

---

_Verified: 2026-10-01_
_Verifier: Claude (gsd-verifier)_

## Override: render harness DPI (accepted)

The 03-02 "fail unless 144 DPI" rule was intentionally relaxed to "fail below 120 DPI" (commit 6f6ae81) on the user's explicit choice on 2026-10-01 ("Check at whatever scaling is active") after the user's display switched from 150% to 125%. 144 DPI remains the reference; layouts are DPI-scaled and the clipping detector is unchanged.

## Human verification (2026-10-02)

User approved Phase 3 ("approve phase 3"); see 03-HUMAN-UAT.md and 03-20-SUMMARY.md.
