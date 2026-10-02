---
gsd_state_version: 1.0
milestone: v1.0
milestone_name: milestone
status: executing
stopped_at: Completed 04-13-PLAN.md
last_updated: "2026-10-02T21:26:05.232Z"
last_activity: 2026-10-02
progress:
  total_phases: 8
  completed_phases: 3
  total_plans: 64
  completed_plans: 57
  percent: 89
---

# Project State

## Project Reference

See: .planning/PROJECT.md (updated 2026-09-29)

**Core value:** Every relevant email in a multi-thousand-document corpus must stay faithful to one consistent case story (core pleading + key-facts timeline).
**Current focus:** Phase 4 — Corpus Plan & Answer Key

## Current Position

Phase: 4 (Corpus Plan & Answer Key) — EXECUTING
Plan: 16 of 22
Status: Ready to execute
Last activity: 2026-10-02

Progress: [██████████] 98%

## Performance Metrics

**Velocity:**

- Total plans completed: 29
- Average duration: -
- Total execution time: 0.0 hours

**By Phase:**

| Phase | Plans | Total | Avg/Plan |
|-------|-------|-------|----------|
| 2 | 9 | - | - |
| 3 | 20 | - | - |

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
| Phase 03 P11 | 35min | 2 tasks | 6 files |
| Phase 03 P13 | 14min | 2 tasks | 5 files |
| Phase 03 P14 | 25min | 2 tasks | 4 files |
| Phase 03 P15 | 12min | 2 tasks | 3 files |
| Phase 03 P16 | 26min | 3 tasks | 8 files |
| Phase 03 P18 | 35min | 3 tasks | 6 files |
| Phase 03 P19 | 35min | 2 tasks | 16 files |
| Phase 04 P01 | 12min | 3 tasks | 16 files |
| Phase 04 P02 | 14min | 3 tasks | 8 files |
| Phase 04 P21 | 6min | 1 tasks | 3 files |
| Phase 04 P03 | 22min | 3 tasks | 7 files |
| Phase 04 P04 | 15min | 2 tasks | 3 files |
| Phase 04 P05 | 8min | 2 tasks | 5 files |
| Phase 04 P06 | 10min | 2 tasks | 4 files |
| Phase 04 P07 | 14min | 2 tasks | 5 files |
| Phase 04 P08 | 12min | 2 tasks | 5 files |
| Phase 04 P09 | 35min | 2 tasks | 2 files |
| Phase 04 P10 | 25min | 2 tasks | 2 files |
| Phase 04 P11 | 20min | 2 tasks | 3 files |
| Phase 04 P22 | 10min | 2 tasks | 3 files |
| Phase 04 P12 | 35min | 2 tasks | 6 files |
| Phase 04 P13 | 20min | 2 tasks | 2 files |

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
- [03-11]: Regenerate Cast = SyncOrganizations -> roster merge by existingId, then same full name (unmatched non-edited key people removed, citing facts flagged "{name} was removed from the cast.") -> one counsel top-up -> details -> per non-Individual organization background rewrite (old staff removed only when that call succeeds)
- [03-11]: Shared MergeEntries: ids claimed once per response; hand-edited entries skipped with "Kept your edits to {name}." unless replaceEdited; unclaimed revisable entries removed
- [03-11]: Tab regeneration failures before the merge leave entries untouched and restore the part status; hint "Click Regenerate {part} to try again."
- [03-11]: RegenerateEntryAsync applies inside GuideStaleness.AbsorbHandEdit (downstream stays fresh; renames via SetCastField flag facts; fact changes flag values)
- [03-11]: UpdateStaleAsync re-checks IsStale before each part; Issues via GenerateIssuesAsync (keeps ids/details), Cast/Facts/Values via RegenerateTabAsync(guidance "", replaceEdited false)
- [03-12]: GridStyle header left padding is LDU(15) - 2 (GridStyle.HeaderTextMargin): DataGridViewColumnHeaderCell adds an unscaled 2 px text margin; right-aligned headers subtract it on the right
- [03-12]: GuideTabPanel.BuildColumns runs from the base ctor (Grid + LDU only); the detail column switches to Absolute LDU(320)+LDU(8) when 40% is narrower; empty-detail text is padded to center on the grid's empty-text line
- [03-12]: LinkPicker puts the locked suffix after the name (before " — "), lists locked then checked then the rest, and keeps dangling checked ids in CheckedIds
- [03-12]: GUIDE-01/GUIDE-08 left Pending: 03-12 is display/editing infrastructure; the editable Cast/Facts tabs and step shell come in 03-13..03-16
- [03-13]: Cast grid ID 72 / Tier 100 (UI-SPEC 64/88 cut off CH-006 and Background); Fill minimums Name 112, Title 92, Organization 92 keep 900x600 free of a horizontal scrollbar
- [03-13]: GuideTabPanel combos (AddCombo, AddPairedCombos) sit in the text fields' rounded 34 px border with text on the 10 px inner edge; a disabled combo is shown as a plain label (no grey system frame)
- [03-13]: Grid Name edits commit as CastField.FullName (atomic split at the last space); detail fields rebuild only when the character or tier changes, combo commits are deferred with BeginInvoke
- [03-13]: GUIDE-08/GUIDE-09 left Pending: the Facts/Values tabs and the step shell (stale banner) come in 03-14..03-16; GUIDE-02/03/04 marked Complete (generation done earlier, now visible in the Cast tab)
- [03-18]: Wizard opens at a 1150x900 logical client (min 1000x700, clamped to the working area); 1100x750 folded the Case Guide Checks list once the shell chrome was subtracted
- [03-18]: DecideClose: legacy closes; active run asks C12 first; no core document and no folder asks C11 when a scenario was typed; otherwise close on a successful save, else C10. Prompts only for CloseReason.UserClosing
- [03-18]: StepCoreDocument.SaveAsync advances the stage to CoreDocument before every save; run autosaves fire when the section or ready count changes
- [03-19]: The Recent cases card sits beside the scenario card only (top-aligned, ending with the hint row); the scenario card and hint share one nested TLP cell so the card spans no AutoSize row
- [03-19]: Opening another case or New Case first flushes a saved current case (StoreInputs + SaveNow; a failed save cancels the switch); C8 only for an unsaved typed scenario; clicking the open case does not reload it
- [03-19]: OpenCase sets PendingNotice and always calls NavigateToStage when hosted in WizardForm (a Described stage maps back to the scenario step)
- [03-19]: Render test classes run in one non-parallel xUnit collection (RenderCollection); parallel STA renders caused intermittent one-line clipping findings
- [03-17]: Case Summary Finish/Back are disabled while an export or folder move runs (CanMoveNext = !_busy)
- [03-17]: Export All stops at the first failure and keeps written documents; an open-in-Word target is detected via sharing/lock HResults or a FileShare.None probe
- [03-17]: Data labels use UseMnemonic = false so "&" renders (Key Facts & Events, case titles, paths); Last saved falls back to case.json write time
- [03-14]: Key Facts grid Who knew is 100 and Who did it min 112 (UI-SPEC 64/96) with single-line headers; at the spec widths the headers wrapped and the grid no longer top-aligned with the other tabs
- [03-14]: Facts rows list chronologically (yyyy-MM-dd ordinal, undated last); MetaLine adds ' · Edited by you' like Cast
- [03-14]: Mark Reviewed under the Needs review filter moves to the next fact to review; Add Fact resets the filter to All and focuses the Fact field
- [03-14]: Link pickers are re-set only when items/checks/locks change, so the list being clicked keeps its order and scroll; an Actors change re-sets Who knew with the actors locked
- [03-14]: GUIDE-05 and GUIDE-08 marked Complete; GUIDE-09 stays Pending until the step shell's stale/needs-review banner (03-16)
- [03-15]: Canonical values list in ID order; the Kind column stays at UI-SPEC 112 and ellipsizes 'Change order number' cleanly (full kind in the detail card)
- [03-15]: Used in facts lists facts in timeline order as 'F-003  2024-03-18  {title}'; the Kind combo commits the enum name; Mark Reviewed keeps the selection
- [03-15]: GUIDE-06 and GUIDE-08 marked Complete (registry shown and editable; ValuesTabPanel is hosted by the step shell in 03-16)
- [03-16]: A tab shows its panel as soon as its part has entries (Cast: key characters), or is Ready even when empty; otherwise a state card (Writing / Waiting / Failed / Not written / No guide). Generating left over from a closed run counts as Not written
- [03-16]: Unfinished parts = any part not Ready (every part when there is no guide). Issues with only failed details count as finished; Retry Issue Details covers them
- [03-16]: Mid-run Checks show only findings about Ready parts, and issue / cast findings wait until the key facts are Ready (they mostly check links from facts)
- [03-16]: The Checks list (152) folds to its caption line plus 'Make the window taller to see the list.' when it would leave the body under 300 logical px
- [03-16]: The Checks grid draws its selection only while focused, so the first row never looks picked on entry
- [03-16]: The banner row is Absolute, sized from the page width in SizeBanner (the AutoSize row measured the wrapping labels at zero width)
- [03-16]: The generator's trailing 'Click Retry {Part}.' is stripped from failure reasons before the state card and status lines add their own instruction
- [03-16]: Tab actions are hidden while a state card shows (the card carries the action); unparented tab actions are disposed with the step
- [03-16]: Regenerate Cast/Facts/Values failure reads 'The {part} was/were not regenerated: ... The previous {entries} were kept.'; Retry Issue Details failure reads 'The issue details were not written: ... Click Retry Issue Details.'
- [03-16]: Detail card minimum is 344 (UI-SPEC 320) so Delete Character and Regenerate Character sit 8 apart; Cast grid minimums are Name 96, Title 80, Organization 96, Tier 96 so the grid fits beside it with a vertical scrollbar
- [03-16]: The wizard's 900x600 default window is too small for the Case Guide; 03-18 should open the case flow at about 1100x750 (deferred-items.md)
- [Phase 04]: [04-01]: CaseDraft.Normalize upgrades FormatVersion below current to current so v1 cases re-saved with Corpus are refused by older builds (T-04-03)
- [Phase 04]: [04-01]: ClampWindow only widens toward the fact range; invalid dates are flagged and left as typed; EndBeforeStart compares the typed dates
- [Phase 04]: [04-01]: Override ranges HotPerHotFact 1-5, PrivilegedPercent 0-30, AttachmentPercent 0-60; negative = automatic (null)
- [Phase 04]: [04-02]: AddBusinessMinutes landing exactly on 17:00 rolls to 09:00 of the next business day (results always business time)
- [Phase 04]: [04-02]: BusinessCalendar.ResolveId returns null for blank/unknown ids (failures not cached); Resolve falls back TimeZoneId -> Location zone -> America/New_York -> UTC
- [Phase 04]: [04-02]: PlanCallRunner waits for a slot on run.Cts.Token and releases only when acquired, so a fatal stop frees queued callers
- [Phase 04]: [04-02]: FakeLlmProvider calls OnSend outside its lock; Respond (request-based) overrides the response queue
- [Phase 04]: [04-21]: CaseGuide.ComputeHash hashes a content projection only; part states, review flags, IsUserEdited/EmailIsUserEdited, DetailsPending, MappingNotes, SourceSectionId, IdCounters and DismissedStaleKey are excluded
- [Phase 04]: [04-03]: SampleKind's parentRecipients = parent To + CC count; ThreadShapes.ResolveKind exposes the degraded kind (ReplyAll without others -> Reply, Forward without target -> Reply, New on non-root -> Reply) so builders can set Kind consistently with RecipientsFor
- [Phase 04]: [04-03]: A Reply from the parent's own sender (follow-up) addresses the parent's To; if nobody is left it is a note to self
- [Phase 04]: [04-03]: SampleLatencyMinutes draws identically for internal/external; external = round(internal x 1.5)
- [Phase 04]: [04-03]: SkeletonContext window falls back settings -> DefaultWindow -> last three months (with a Note); ids outside the cast are always active and use UsTimeZones.Default
- [Phase 04]: [04-03]: ThreadScheduler bump rule: if the sampled reply time is < parent + 1 min or <= previous + 1 min, AddBusinessMinutes(later of parent/previous, 2..20) in the sender zone
- [Phase 04]: [04-03]: BudgetAllocator adjusts to the exact target by adding to the shortest thread below 12 and trimming the longest above 1 (fact-then-thread order); full threads add a thread to the fact with the most weight per thread; all-singleton overshoot drops Supporting threads first
- [Phase 04]: [04-04]: Plan mix check compares each non-relevant category with Apportion.Largest(actual non-relevant count, settings mix); warns above 3 points drift
- [Phase 04]: [04-04]: Privileged side = first counsel on the thread (in-house -> own org; outside -> firm + RepresentsOrgIds, plus any firm representing a client); anyone else, incl. externals, is from the other side
- [Phase 04]: [04-04]: DocID rule checks set equality with {prefix}-000001..n plus attachment {parent}.{k:0000}, not strict chronological order
- [Phase 04]: [04-04]: Leakage metrics (depth per thread, attachment share, To+CC, sender hour) are skipped when either group value is 0; relevant vs DailyBusiness+Decoy, both >= 5 messages
- [Phase 04]: [04-04]: CorpusPlanValidator reads plan.Settings/ToIds/CcIds as stored (no Normalize); null Settings skips settings rules
- [Phase 04]: [04-05]: DocID prefix: producing-party initials (non-ASCII letters dropped per word, single word -> first two letters), settings override wins, RD fallback
- [Phase 04]: [04-05]: Custodian folders assigned in cast order (first wins; later duplicates and reserved names get -{CH id}); no cast participant -> Mailboxes/Unassigned/Inbox
- [Phase 04]: [04-05]: Relevant thread roots are always Own; Own issue codes = issue ids of the message facts (fallback thread issues); quoted replies and attachments use thread codes
- [Phase 04]: [04-05]: Hot is Y only on IsHot emails of relevant threads and their attachments; non-relevant attachments read 'family coded Not Relevant'
- [Phase 04]: [04-06]: Statistics sections are title + header + data rows separated by one skipped row; By Custodian ends with Total; blank custodian = (No custodian), bad Date Sent = (Undated), both sorted last
- [Phase 04]: [04-06]: Not Relevant = every row whose Relevance is not Relevant, so Relevant + Not Relevant equals the row count per custodian and month; by-issue counts rows whose Issue Codes contain the id
- [Phase 04]: [04-06]: XLSX cells are inline strings only (no CellFormula), StripInvalidXmlChars then capped at 32,767 without splitting a surrogate pair
- [Phase 04]: [04-07]: Protocol prose with any blank top-level section is replaced by FallbackProse; an issue missing from the response gets the criteria-only example while the rest of the prose is kept (IsFallback false)
- [Phase 04]: [04-07]: Protocol issue ids match guide ids by trim + case-insensitive compare; unknown ids dropped; DocID tokens ([A-Z]{2,6}-######[.####]) stripped from all model prose
- [Phase 04]: [04-07]: CorpusPlanPrompts.AppendCast role text is Party / In-house counsel / Outside counsel / Staff, plus ', background' for Background-tier members
- [Phase 04]: [04-08]: CorpusPlanStore.EnsureLoaded with no corpus or no PlanId returns an empty-Reason result; callers detect failure by a non-empty Reason
- [Phase 04]: [04-08]: PlanSummary month buckets span the settings window widened to every planned message (sums equal Emails); windows over 600 months are ignored
- [Phase 04]: [04-08]: Fact rows count relevant threads listing the fact, relevant messages whose own FactIds carry it, and IsHot among those; HotEmails counts IsHot only in relevant threads
- [Phase 04]: [04-08]: Custodians tile counts distinct cast CustodianIds; custodian rows = sender if cast else first cast recipient, Total desc then name
- [Phase 04]: [04-09]: Multi-issue pairs need different issue sets, fact dates within 10 business days and >= 2 shared active knowers; merges keep one single-fact thread per fact; overflow above depth 12 spills to the dropped fact, then the kept fact, then a new thread
- [Phase 04]: [04-09]: Merges, privilege and participant pools are decided on thread specs before messages are built
- [Phase 04]: [04-09]: Privileged side mirrors validator rule 12 (in-house own org; outside firm + RepresentsOrgIds; plus firms representing those clients); candidates need counsel + 1 other active client-side knower; target round(threads x pct), min 1
- [Phase 04]: [04-09]: Attachments are an exact share (round(messages x AttachmentPercent)) picked among messages that can carry an enabled type; invites only Seq 1, voicemail only single-message threads
- [Phase 04]: [04-09]: Repair swaps an inactive person from that message onward (counsel for counsel on privileged threads), reschedules on sender swaps (3 passes), else truncates with a note and moves removed messages to a replacement thread of people active anchor..+30 days
- [Phase 04]: [04-10]: Daily-business root senders weighted 1 + relevant messages sent; ~20% of daily threads add one Party/ThirdParty person; round(threads x 2%) (min 1 at 50+) privileged daily threads stay inside the in-house counsel's organization
- [Phase 04]: [04-10]: Decoys use knowers of a fact (2+ knowers preferred), start within +/-30 days of it clamped to the window, carry its IssueIds and never FactIds
- [Phase 04]: [04-10]: Externals = clamp(bulk messages / 15, 3, 40) split 40/30/30 (spam first in the spam/vendor group), never reuse a case org or cast email domain; external-root threads are owned by the cast recipient; spam images are exactly 5% of spam messages when Image is enabled
- [Phase 04]: [04-10]: Non-relevant threads use stable pools (active anchor..+30 days) and reschedule-to-fit (active ranges and window end) instead of swap/truncate repair; totals never change
- [Phase 04]: [04-11]: Review Plan entry order: running, unreadable plan, no plan (AutoPlan or NoPlanCard on reopen), unapproved + settings changed -> AutoReplan, stale guide not dismissed -> StaleBanner (wins over settings changed), approved + settings changed -> SettingsBanner, else ShowPlan; WithdrawsApproval = approved and StaleBanner
- [Phase 04]: [04-11]: MinPercentFor searches 5..60 so the D-07 warning never suggests a value the field clamps; PercentText shows whole values without decimals, else one decimal; Est. cost is N0 from $10 up, else 0.00
- [Phase 04]: [04-22]: CorpusApproval staleness compares the loaded plan's GuideHash/SettingsHash, falling back to the case.json PlanGuideHash/PlanSettingsHash copy; no plan = never stale
- [Phase 04]: [04-22]: Approval files are named from the case folder's own name and written to folder + constant names only; ApplyNewPlanAsync keeps WrittenFiles so old files read as from an earlier plan
- [Phase 04]: [04-12]: PercentSlider exposes internal ValueFromX/HandleKey/Commit; ValueChanged only on change, ValueCommitted on mouse-up and key-up (owner autosaves on commit)
- [Phase 04]: [04-12]: MonthHistogram skips any x label that would come within 4 px of the previous one after edge clamping; odd axis maxima (1, 5) draw the middle gridline unlabeled
- [Phase 04]: [04-12]: Ellipsizing label rows (StatTile) use fixed one-line Absolute rows measured before docking, with TopLeft text; MiddleLeft lifts ellipsized text half a line
- [Phase 04]: [04-13]: Relevant chunks cut greedily in first-message order (<= 20 threads, <= 60 messages); window facts = facts dated within the chunk's sender-local days plus facts the threads carry, with up to 3 earlier facts as Already happened; only the last 40 earlier threads are listed
- [Phase 04]: [04-13]: Re-ask triggers on blank subject/purpose/rationale or any message without intent/must-convey; refused chunks are not re-sent; any template use sets UsesFallbackContent
- [Phase 04]: [04-13]: Attachment file names = SanitizeForFileName with dots/underscores trimmed, stem capped at 80, code extension by kind; blank titles keep the code default

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

Last session: 2026-10-02T21:26:05.227Z
Stopped at: Completed 04-13-PLAN.md
Resume file: None
