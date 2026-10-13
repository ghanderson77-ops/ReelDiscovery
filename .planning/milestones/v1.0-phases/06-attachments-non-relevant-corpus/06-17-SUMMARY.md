---
phase: 06-attachments-non-relevant-corpus
plan: 17
subsystem: attachment-unit-of-work
tags: [attachments, media, retries, invites, att-01, att-02, att-03, att-04, run-02, d-06, d-14, d-17, d-19, d-20, d-27, d-28, d-29]
requires:
  - "06-01 JournalRecord attachment fields, AttachmentDone/Failed/Skipped, MaxFlattenedTextChars"
  - "06-02 GenerationSnapshot.AddEvent, AttachmentRetry / MediaStopped events, ComposedAttachment"
  - "06-03 OfficeDocumentService themed overloads, CalendarService.CreateInvite, OrganizationThemes.ForOrganization, DocumentMetadata"
  - "06-04 IMediaGenerator / MediaResult, FakeMediaGenerator"
  - "06-05 AttachmentNaming.ContentIdFor, InviteSlot, InviteScheduler.NotTiedWarning, VoiceAssignments.For"
  - "06-06 DecoyScanner.ScanText"
  - "06-08 RenderValidator.CheckAttachment, AttachmentCheckInput"
  - "06-09 PersonInfo.OrganizationName"
  - "06-11 AttachmentRenderContext, AttachmentRenderer (BuildRequest/DtoTypeFor/Map/Flatten/VariantText/IsUsable), AttachmentPrompts.ImagePrompt"
provides:
  - "AttachmentOutcomeKind { Written, Warnings, Failed, Skipped, NotRun }"
  - "AttachmentOutcome with full identity (For(ctx)) and ToRecord(planId) -> AttachmentDone/Failed/Skipped record or null for NotRun"
  - "AttachmentUnitOfWork(llm, snapshot, media, gate, storyModel, volumeModel).RunAsync(ctx, run) (internal), MaxAttempts = 3, CastVoices"
  - "Reason constants: RefusedReason, TimeoutReason, UnusableReason, BuildFailedPrefix, EmptyDocumentText, NoMediaKeyReason"
  - "MediaRunGate: Slots (2, 2), Stopped, Reason, Remaining, SetRemaining, Consume, TryStop"
affects: [06-18 thread unit with attachments, 06-19 runner wiring, 06-20 attachment repair]
tech-stack:
  added: []
  patterns:
    - "Typed PlanCallRunner.RunAsync<T> per DTO type through a private CallResult adapter (no reflection)"
    - "Unusable content counts as a V5 'the document was empty' violation, so it is retried with feedback like a failed check"
    - "Best attempt: usable beats empty, then fewest violations, tie goes to the later attempt"
    - "Media stop: TryStop once -> one MediaStopped event; stop flag rechecked after the slot is acquired"
key-files:
  created:
    - Services/Evidence/AttachmentUnitOfWork.cs
    - Services/Evidence/MediaRunGate.cs
    - ReelDiscovery.Tests/Evidence/AttachmentUnitOfWorkTests.cs
  modified: []
decisions:
  - "ToRecord leaves Utc and Files unset: the caller nests the record in its ThreadDone / AttachmentsRepaired line (the unit may not read the clock)"
  - "MediaRunGate.Consume is called only for Ok/Failed media results; a Stopped call keeps its item in Remaining, so MediaRemaining counts every unrendered item including the one that hit the stop"
  - "After a stop, every Skipped media outcome takes the gate's first reason, so the run log names one cause"
  - "Office document title = the AttachmentNaming title when set, else the model's title; invite SUMMARY = the model's title"
  - "A missing InviteSlot (caller resolved none) falls back to the next day at the email's time with the 'invite not tied to a known meeting' warning instead of failing"
  - "Invite UID = first 32 hex of SHA-256(DocId) @ sender domain; Office Created = 1h..3d and Modified = 5..59 min before the local send time, both StableRank-stable per DocId"
  - "Non-refusal, non-timeout text-call failures use the plain reason 'the model returned no usable content'; media exceptions use 'the media could not be created: ...'"
metrics:
  duration: 16min
  completed: 2026-10-04
---

# Phase 6 Plan 17: Attachment Unit of Work Summary

Each planned attachment now runs as its own unit. Office documents and invites go through a typed text call, then the checks and up to two retries with feedback. The best attempt is kept and the branded bytes are built. Images and voicemail audio go through the media seam, and a run-level gate stops all media once without stopping text. Every outcome carries its identity, and the unit never reports completion.

## What was built

- **AttachmentOutcome**:
  - `For(ctx)` fills the identity first: DocId, kind, thread id, parent DocId and seq, inline flag, relevance, late-repair flag, file name and title. So Failed, Skipped and NotRun outcomes are complete without bytes (review concern 2).
  - `ToRecord(planId)` maps Written/Warnings to `AttachmentDone` with a Status, Failed to `AttachmentFailed` and Skipped to `AttachmentSkipped`. It copies the identity, flattened text (cut to `MaxFlattenedTextChars`), ledger deltas, tokens, text and media cost, the failure kind and reason, and the invite fields. NotRun returns null but keeps its spend on the outcome (D-29).
- **AttachmentUnitOfWork, text path** (Word, Excel, PowerPoint, invite):
  - Up to 3 attempts. Retry feedback goes at the end of the prompt, and each retry adds an `AttachmentRetry` event with the file name.
  - Relevant attachments get `RenderValidator.CheckAttachment` with all its inputs: the parent seq, flattened and variant text, people named, the accepted thread (whose visible deltas and ancestor bodies the check gates itself), NewDetails, late-repair mode and later-established entries.
  - Variant labels are the canonical value labels plus the ledger and visible-delta subjects.
  - Decoys get `DecoyScanner.ScanText` on the attachment text, which covers the 06-06 note. Other filler only needs usable content.
  - Refusals and timeouts are never re-asked. A fatal stop returns NotRun with the spend.
- **Bytes**:
  - The theme comes from `OrganizationThemes.ForOrganization(sender.Domain, sender.OrganizationName)`, and the sender is the author (D-19).
  - The PowerPoint subtitle is `{Org} | {MMMM yyyy}` (invariant culture).
  - Invites are built by code: the organizer is the sender and the attendees are the distinct To + Cc people, at the time of the caller's slot. The slot's MeetingKey, Start and End are copied onto the outcome. A slot warning becomes a violation that is not retried (D-20).
  - Any builder or other exception becomes Failed `the document could not be built: ...` (RUN-02). Only `OperationCanceledException` propagates.
- **Media path**:
  - No generator → Skipped `no tested OpenAI key this session`.
  - Gate already stopped → Skipped with the gate's reason.
  - Otherwise the unit waits for one of 2 slots and checks the stop flag again before calling (review MEDIUM).
  - Image prompts come from `AttachmentPrompts.ImagePrompt` (no names, no-text rule, D-27). Voicemail speaks `ctx.ParentBody` in `VoiceAssignments.For(parent.FromId, CastVoices)` (D-28).
  - Results: Ok → Written (inline images get `AttachmentNaming.ContentIdFor`); Failed → Failed with its reason; Stopped → the first `TryStop` adds one `MediaStopped` event with `MediaRemaining`, and the item is Skipped. The unit never calls `SetFatal`.
- **MediaRunGate**: a 2-slot semaphore plus the stop flag, reason and remaining count, all under a lock. `TryStop` returns true only once.

## Tests

`AttachmentUnitOfWorkTests` has 27 tests, covering every behavior in the plan:

- clean branded Word (the organization name is in the document)
- identity across Written, Failed and NotRun
- V2 retry with the feedback block, and V6 feedback from a visible thread delta
- always violating → Warnings after 3 calls
- refusal, timeout, and unusable Word/Excel/PowerPoint
- filler Excel on the Volume tier with no checks
- decoy D1
- the PowerPoint subtitle
- the invite built from the slot, and an untied invite kept with its warning and no retry
- a builder exception, and user cancel
- inline image ContentId and media cost
- no key → Skipped; rate limit → Failed
- StopAfter=1 across three items, `Media_QueuedAfterStop_MakesNoCall`
- the cast voice and the fallback voice
- at most 2 media calls at once
- cancel during a media call and while waiting for a slot (slots balanced)

Every test also asserts that the snapshot attachment counts and the Failed / KeptWithWarnings / Skipped events stay untouched.

- Plan verification `FullyQualifiedName~ReelDiscovery.Tests.Evidence&Category!=Render`: 1033 passed.
- Full suite (wave-4 gate): **3253 passed, 0 failed** (baseline 3226 + 27 new), 7 m 22 s.
- The app build has 0 warnings. The test build's only warnings are the 2 known xUnit1031 warnings in GenerateCorpusStepTests.

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 3 - Blocking] MediaRunGate created in Task 1**
- **Found during:** Task 1
- **Issue:** The Task 1 constructor takes a `MediaRunGate`, but the plan creates the gate in Task 2.
- **Fix:** Created the full `MediaRunGate` in the Task 1 commit. Task 2 added the media path that uses it.
- **Files modified:** Services/Evidence/MediaRunGate.cs
- **Commit:** 157503f

**2. [Rule 3 - Blocking] Internal `BeforeBuild` test seam**
- **Found during:** Task 1 (builder-exception behavior)
- **Issue:** `OfficeDocumentService` and `CalendarService` are concrete classes with non-virtual methods, so a test had no way to make them throw.
- **Fix:** Added `internal Action<AttachmentContent>? BeforeBuild`, which runs just before the bytes are built. The exception goes through the same catch as a real builder error.
- **Commit:** 157503f

**3. [Rule 1 - Correctness] MediaRemaining counts the item that hit the stop**
- **Found during:** Task 2
- **Issue:** The plan calls `Consume()` before mapping the result. On a Stopped result that would drop the stopped item from `MediaRemaining`, even though that item was never rendered.
- **Fix:** `Consume()` now runs only for Ok and Failed results.
- **Commit:** 1ce1e7e

## Known Stubs

None.

## Threat Flags

None. No new endpoints, file access or trust-boundary schema changes: the unit writes no files, and media and LLM output stay behind the existing seams.

## TDD Gate Compliance

- RED: 80000b4 (Task 1) and fc96df0 (Task 2).
- GREEN: 157503f and 1ce1e7e.
- No refactor commits were needed.

## Self-Check: PASSED

- Files: AttachmentUnitOfWork.cs, MediaRunGate.cs, AttachmentUnitOfWorkTests.cs exist.
- Commits: 80000b4, 157503f, fc96df0, 1ce1e7e are in the log.
- Every acceptance grep in the plan passed for both tasks.
