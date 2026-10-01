---
gsd_state_version: 1.0
milestone: v1.0
milestone_name: milestone
status: executing
stopped_at: Completed 03-12-PLAN.md
last_updated: "2026-10-01T21:37:29.892Z"
last_activity: 2026-10-01
progress:
  total_phases: 8
  completed_phases: 2
  total_plans: 42
  completed_plans: 33
  percent: 79
---

# Project State

## Project Reference

See: .planning/PROJECT.md (updated 2026-09-29)

**Core value:** Every relevant email in a multi-thousand-document corpus must stay faithful to one consistent case story (core pleading + key-facts timeline).
**Current focus:** Phase 3 — Case Guide

## Current Position

Phase: 3 (Case Guide) — EXECUTING
Plan: 12 of 20
Status: Ready to execute
Last activity: 2026-10-01

Progress: [████████░░] 76%

## Performance Metrics

**Velocity:**

- Total plans completed: 9
- Average duration: -
- Total execution time: 0.0 hours

**By Phase:**

| Phase | Plans | Total | Avg/Plan |
|-------|-------|-------|----------|
| 2 | 9 | - | - |

**Recent Trend:**

- Last 5 plans: -
- Trend: -

*Updated after each plan completion*
| Phase 01 P01 | 5min | 2 tasks | 9 files |
| Phase 01 P02 | 12min | 3 tasks | 6 files |
| Phase 01 P03 | 14min | 3 tasks | 15 files |
| Phase 01 P04 | 6min | 2 tasks | 7 files |
| Phase 01 P05 | 8min | 2 tasks | 8 files |
| Phase 01 P06 | 3min | 2 tasks | 4 files |
| Phase 01 P07 | 6min | 3 tasks | 8 files |
| Phase 01 P08 | 5min | 2 tasks | 6 files |
| Phase 01 P09 | 8min | 3 tasks | 9 files |
| Phase 01 P10 | 4min | 2 tasks | 3 files |
| Phase 01 P11 | 9min | 2 tasks | 11 files |
| Phase 01 P13 | 3min | 2 tasks | 6 files |
| Phase 02 P01 | 9min | 2 tasks | 12 files |
| Phase 02 P02 | 8min | 2 tasks | 4 files |
| Phase 02 P03 | 10min | 2 tasks | 6 files |
| Phase 02 P05 | 5min | 1 tasks | 2 files |
| Phase 02 P04 | 12min | 2 tasks | 4 files |
| Phase 02 P06 | 12min | 2 tasks | 3 files |
| Phase 02 P07 | 9min | 2 tasks | 4 files |
| Phase 02 P08 | 14min | 2 tasks | 2 files |
| Phase 02 P09 | 6min | 2 tasks | 3 files |
| Phase 03 P01 | 5min | 2 tasks | 14 files |
| Phase 03 P02 | 15min | 2 tasks | 8 files |
| Phase 03 P03 | 12min | 2 tasks | 8 files |
| Phase 03 P04 | 14min | 2 tasks | 5 files |
| Phase 03 P05 | 22min | 2 tasks | 2 files |
| Phase 03 P08 | 14min | 2 tasks | 4 files |
| Phase 03 P06 | 25min | 2 tasks | 7 files |
| Phase 03 P07 | 10min | 2 tasks | 4 files |
| Phase 03 P09 | 10min | 2 tasks | 5 files |
| Phase 03 P10 | 8min | 2 tasks | 3 files |
| Phase 03 P12 | 8min | 2 tasks | 8 files |

## Accumulated Context

### Decisions

Decisions are logged in the PROJECT.md Key Decisions table.
Recent decisions affecting current work:

- [Roadmap]: Research's 10 phases consolidated into 8. Domain model/persistence is folded into Phases 2-3 (case.json) and Phase 5 (run journal). Cutover is folded into Phase 7.
- [Roadmap]: The narrative ledger (CORP-08..11) ships with the relevant-thread renderer in Phase 5. Relevant threads render sequentially in chronological waves, so batch execution (Phase 7) mainly targets non-relevant/noise and QA passes.
- [Roadmap]: The answer key and review protocol are projections of the plan (Phase 4), not labels inferred after generation.
- [Roadmap]: Ancillary docs (Phase 8) depend only on the case guide (Phase 3) and can be pulled forward.
- [01-01]: Word builder's 3 pre-existing w:color ordering schema errors pinned as test baseline; fix deferred
- [01-02]: Doc chain versions reserved before generation (gaps possible on failure, never duplicates)
- [Phase 01-03]: OpenAI Story default gpt-6.1-sol effort low (D-15); gpt-6-sol kept as alternative
- [Phase 01-03]: Gateway treats provider Outcome=Invalid like unparseable output (3-attempt cap); MEAI inlines nested DTOs (no $ref)
- [Phase 01-04]: Anthropic spend-cap 429 guard uses x-should-retry: false (SDK 12.51.0 honours it); MaxNonStreamingTokens 21_333 kept (SDK has no client-side long-request guard)
- [Phase 01-04]: JsonSchemaRegistry serializer options frozen with explicit TypeInfoResolver (fixes concurrent-build race)
- [Phase 01]: D-15 verified: gpt-6.1-sol supports Chat Completions, strict Structured Outputs and reasoning_effort low (none/minimal unsupported); catalog values unchanged
- [Phase 01]: OpenAI transport retries use QuotaAwareRetryPolicy: 429 insufficient_quota is never retried
- [Phase 01]: OpenAI images use gpt-image-2.5-flare at quality low (GenerationConfig.ImageModel/ImageQuality); speech stays tts-1
- [Phase 01-06]: API keys stored DPAPI CurrentUser in keys.dat; legacy StepApiConfiguration routed through ApiKeyStore as interim bridge until 01-07
- [Phase 01]: Keys loaded from the DPAPI store are shown untested; the user must Test before Next (tested flags never persisted)
- [Phase 01]: Configure Models OK requires one Story and one Volume default per provider; first default per (Provider, Tier) wins
- [Phase 01]: Legacy CreateOpenAIService() uses the OpenAI Volume model until deleted in 01-11
- [Phase 01-08]: Legacy generators take ILlmGateway; SuggestedDateRange non-null (empty startDate = none); SuggestedEmailCount clamped in code; themes rethrow fatal failures, else default-theme fallback
- [Phase 01-09]: Media flags forced off in ValidateStepAsync via WizardState.ApplyMediaAvailability; Slack telemetry removed entirely (D-16) with source-scan guard
- [Phase 01-10]: EmailGenerator on ILlmGateway (Volume tier) + OpenAIMediaClient; null media skips image/voicemail passes before any API call; VoicemailScriptResponse reduced to voicemailScript
- [01-11]: Fatal provider stop (Auth/SpendLimit) sets FatalError and saves completed threads; WasCancelled means user cancel only
- [01-11]: PLAT-03 partial in Phase 1; planner/validator/answer-key tests carried to Phase 4
- [01-12]: Provider OCE rethrown only when caller ct is cancelled; other OCE (transport timeout) -> Failed(Network) / media null
- [01-12]: Key test result applied only if KeyTestGuard.IsResultCurrent; key box and Clear locked during test
- [01-13]: Generator OCE rethrown only when run token cancelled; other OCE = recorded timeout skip; unexplained stop records an error
- [01-13]: DistributeEmails largest-remainder, never negative; 0-email storylines skip the LLM call
- [02-01]: CaseCount.Summary is serialized data; Computed_NotSerialized asserts only count summaries and no preset summary text
- [02-01]: Case models use yyyy-MM-dd string dates parsed only via CaseDraft.TryParseDate; CaseDraft.JsonOptions (string enums) shared with Phase 3 case.json
- [02-02]: Person aliases that prefix the original first name (Walt/Walter) map to the substitute first name; FindLeaks reports canonical stored terms
- [02-03]: Number() strips LLM leading numbers and drops blank paragraphs before numbering
- [02-03]: Every count incorporates through the last factual paragraph, not the previous count's paragraphs
- [02-05]: RealEntityDetector drops blank/duplicate names (first wins, case-insensitive) and cleans aliases in code; LlmCallException propagates to the UI boundary
- [02-04]: Memo date earlier than timeline end warns 'The timeline ends after the memo date.'; blank timeline dates reported as yyyy-MM-dd errors
- [02-04]: ApplyEdit on a Failed section with non-empty text sets Status Ready and clears FailureMessage
- [02-06]: One linked runCts spans all section phases so a fatal Auth/SpendLimit stops in-flight and later section calls uniformly
- [02-06]: Retry of Failed/Pending sections sends no old text; only RegenerateSectionAsync sends currentText + guidance
- [02-07]: StatePreference is stored only when Court = State and a state is chosen; otherwise empty
- [02-07]: Scenario TextBox is ReadOnly (not Disabled) during the name check so the text stays readable
- [02-07]: Default complexity radio is checked only after all radios and the preset label exist (CheckedChanged reads them)
- [02-08]: StepCoreDocument hides the empty state while a run is in progress, so the first run shows sections filling in
- [02-08]: ApplyPendingEdit always normalizes editor text to the section's joined paragraphs (whitespace-only changes do not leave Apply enabled)
- [02-08]: Retry Section runs GenerateSectionsAsync for all Pending/Failed sections; Regenerate Section (Ready) calls RegenerateSectionAsync with guidance
- [02-09]: Wizard steps come from one StepTypesFor(flow) table; the API step is shared and later steps are disposed and rebuilt only when the flow changes on leaving step 0
- [02-09]: Finish always runs the last step's ValidateStepAsync before closing; case-driven Finish adds the C6 'Close Case' confirm (default No)
- [03-01]: SampleGuidedDraft has a single issue (ISS-01) because SampleComplaintDraft has one count; fixture law-firm domain derived from Counsel.FirmName (ellispark.com, CH-006 Morgan Ellis)
- [03-01]: CaseDraft.Normalize also repairs substitution strings/aliases and CoreDocument scalar strings; Stage only moves forward via AdvanceStage
- [03-02]: Render tests ([Trait Category=Render]) write PNG + .clip.txt to ReelDiscovery.Tests/bin/Debug/net10.0-windows/renders/; the harness throws (not skips) off 144 DPI; build test controls in the arrange callback
- [03-02]: Every Phase 3 grid/text field/status dot uses GridStyle.Apply/AddDotColumn, TextFieldHost.Create (EM_SETMARGINS zeroed for one left edge) and Theme.DrawStatusDot
- [03-03]: Individual parties get {last}family.com, agencies always .gov, domain stem from ShortName when set else Name (Dunder Mifflin -> michael.scott@dundermifflin.com)
- [03-03]: IssueExtractor applies match rules as passes over all counts (CountId+title, then title, then CountId retitle) so title matches always win
- [03-04]: Queued autosave snapshots carry a sequence number; a waiter whose snapshot was already written by a newer one (or superseded by SaveNow) returns the latest write outcome
- [03-04]: The SaveAsync drain (lock, write, release) runs inside Task.Run so a UI-thread SaveNow/MoveFolder blocking on the writer lock cannot deadlock
- [03-04]: MoveFolder holds the writer lock, drops any queued snapshot and replaces the recent entry; same-volume Directory.Move, cross-volume copy + delete
- [03-04]: Reserved device names are checked on the base name before the first dot; leading dots are stripped so traversal collapses to a plain name
- [03-05]: Cast and Key Facts exports put the title block on the first landscape page with the wide tables; profiles, background staff and the issue index stay portrait
- [03-05]: Core document export reuses CoreDocumentAssembler caption/numbering/memo header/signature text; caption padding becomes a right tab stop, memo labels are tab-aligned
- [03-05]: No page-number footer in the exports (PAGE fields would contradict the T-03-18 no-fields acceptance)
- [03-06]: All Phase 3 copy with counts/parts/states comes from CaseGuideUiText (pure, unit-tested); UI plans stay layout-only
- [03-06]: ThemedDialog scales the empty form once before children are built with LogicalToDeviceUnits; dialogs size ClientSize from the root TLP preferred height
- [03-06]: RenderHarness captures form.Size for bordered forms (DrawToBitmap includes the title bar)
- [03-06]: Empty message/reason strings drop their ': {message}' clause; singular counts get singular follow-ups ('Fix it', 'that link', 'it is listed')
- [03-07]: Guide staleness = stored per-part basis hash over component hashes (core; +issues/cast for Facts; +facts for Values); hand edits run in GuideStaleness.AbsorbHandEdit so fresh downstream parts are restamped and review flows through NeedsReview
- [03-07]: Enum-valued guide edits accept names only; other input returns 'Choose a value from the list.'
- [03-07]: Counsel sync runs on any identity change (name, organization or typed email) when the old full name matches Counsel.AttorneyName
- [03-07]: Empty cast ActiveFrom/ActiveTo is allowed (unknown); empty fact Date is rejected; MarkReviewed bumps Revision without IsUserEdited
- [03-08]: CaseGuideValidator leak findings use the entity id as {where}; organization findings use Area Guide with the ORG id as EntityId
- [03-08]: Conflict findings (duplicate label, value under two labels, shared org domain) are reported once on the later entry; label conflicts compare exact trimmed text
- [03-08]: Text-checked canonical value kinds (must appear in core doc or facts): Amount, InvoiceNumber, ChangeOrderNumber, ContractNumber, PurchaseOrder, AccountNumber
- [03-08]: Missing/invalid active range is a validator Error for every cast member; inactive-actor checks skip invalid ranges
- [03-09]: CaseGuidePrompts.SystemPrompt says "Keep each person, company and organization" (not "every"): the plan's exact text matched its own D-01 regex
- [03-09]: Counsel top-up runs only right after a roster call (never on resume), at most once; CounselGaps mirrors validator rule 17
- [03-09]: Signing attorney seeded as Partner at the counsel firm (created if missing, representing Plaintiff/Complainant orgs); memo author seeded from Memo.From
- [03-09]: Parallel guide units return DTOs; the guide is merged on the calling thread after Task.WhenAll (finally block keeps finished units on user cancel)
- [03-10]: Fact target = preset midpoint spread by TargetParagraphs (largest remainder, min 2); sections above 30 facts split into equal date slices; CompletedUnits keys are the section id or {id}#{k}
- [03-10]: Fact refs resolve by ISS/CH id or exact cast full name; unknown refs dropped with MappingNotes; KnownByIds = ActorIds union knownBy; F-ids allocated in date order after Task.WhenAll
- [03-10]: GenerateAsync skips Ready parts, stops at the first failed part or fatal, awaits partCompleted after each part (and after a cancelled part that changed Revision)
- [03-10]: Canonical values merge duplicates by label+value (case-insensitive) and never change hand-edited values; first-generation paths send no existing entries (regeneration is 03-11)
- [03-12]: GridStyle header left padding is LDU(15) - 2 (GridStyle.HeaderTextMargin): DataGridViewColumnHeaderCell adds an unscaled 2 px text margin; right-aligned headers subtract it on the right
- [03-12]: GuideTabPanel.BuildColumns runs from the base ctor (Grid + LDU only); the detail column switches to Absolute LDU(320)+LDU(8) when 40% is narrower; empty-detail text is padded to center on the grid's empty-text line
- [03-12]: LinkPicker puts the locked suffix after the name (before " — "), lists locked then checked then the rest, and keeps dangling checked ids in CheckedIds
- [03-12]: GUIDE-01/GUIDE-08 left Pending: 03-12 is display/editing infrastructure; the editable Cast/Facts tabs and step shell come in 03-13..03-16

### Pending Todos

None yet.

### Blockers/Concerns

- [Phase 4]: Corpus planner is the highest-uncertainty component (chunk size, neighbor facts, planning cost at 5K). Needs phase research.
- [Phase 4]: Resolve custodial-duplicate vs. per-custodian folder layout (OUT-01) before building the answer-key writer.
- [Phase 7]: Verify the OpenAI .NET batch/file surface (experimental OPENAI001) and Anthropic cache_control pass-through before planning.
- [Phase 8]: Jurisdiction-specific discovery limits (interrogatory caps, RFA numbering, transcript line counts) need verification.
- [Timing]: .NET 8 reaches EOL on 2026-11-10, so Phase 1's retarget is time-sensitive.

## Deferred Items

| Category | Item | Status | Deferred At |
|----------|------|--------|-------------|
| *(none)* | | | |

## Session Continuity

Last session: 2026-10-01T21:37:29.888Z
Stopped at: Completed 03-12-PLAN.md
Resume file: None
