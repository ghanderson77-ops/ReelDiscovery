---
phase: 6
reviewers: [codex]
reviewed_at: 2026-10-04T18:32:48Z
plans_reviewed: [06-01-PLAN.md, 06-02-PLAN.md, 06-03-PLAN.md, 06-04-PLAN.md, 06-05-PLAN.md, 06-06-PLAN.md, 06-07-PLAN.md, 06-08-PLAN.md, 06-09-PLAN.md, 06-10-PLAN.md, 06-11-PLAN.md, 06-12-PLAN.md, 06-13-PLAN.md, 06-14-PLAN.md, 06-15-PLAN.md, 06-16-PLAN.md, 06-17-PLAN.md, 06-18-PLAN.md, 06-19-PLAN.md, 06-20-PLAN.md, 06-21-PLAN.md, 06-22-PLAN.md, 06-23-PLAN.md, 06-24-PLAN.md, 06-25-PLAN.md]
---

# Cross-AI Plan Review — Phase 6

## Codex Review

**Summary.** The plans are thorough, traceable to requirements, and well aligned with the existing pipeline. The shared unit of work, provider separation, deterministic composition, and journal projections are good architectural choices. However, several cross-plan contracts need revision before execution: attachment repair can regenerate successful parts, attachment ledger validation omits required inputs, chronological repair can introduce contradictions, and progress can report completion before persistence. Coverage is broad, but the proposed tests do not yet establish all four phase success criteria.

**Strengths**

- **Clear requirement coverage:** all attachment kinds, non-relevant categories, mailbox copies, retries, and UI states have implementation owners.
- **Shared generation path:** 06-16/18/19 preserve common composition, formatting, threading, and mailbox behavior while honoring the Story/Volume decisions.
- **Compatibility awareness:** 06-03/07/09/10/20 explicitly protect legacy overloads, existing HTML, deterministic MIME, and Phase 5 voicemail behavior.
- **Useful failure isolation:** 06-04/17 separate media failures from text-provider fatal stops.
- **Strong verification infrastructure:** strict-schema checks, fake providers, OpenXml validation, MIME inspection, and 144-DPI renders provide substantial coverage.
- **Good scope discipline:** Batch APIs, semantic judges, and broader scale optimization remain deferred. The no-V8 filler clarification is consistently reflected in the revised plans.

**Concerns**

- **HIGH — Repair harvesting operates at the wrong granularity (06-10, 06-20).**  
  `HarvestParts` stops after the first readable EML, but repair passes files for an entire thread. If message 1 contains Word and message 2 contains Excel, reading message 1 makes the successful Excel appear missing. Repair then regenerates it, changing bytes and potentially content unnecessarily. A readable but incomplete custodian copy also prevents checking another complete copy. Harvest by **parent message**, inspect alternate copies for missing expected DocIDs, and add a multi-message repair test.

- **HIGH — Attachment validation omits the attachment’s ledger deltas (06-08, 06-11, 06-17).**  
  `AttachmentCheckInput` has no `NewDetails`, and the synthetic `RenderedThread` never receives them. The existing contradiction rule explicitly examines [`Rendered.NewDetails`](C:/Users/ghand/source/repos/ReelDiscovery/ReelDiscovery/Services/Evidence/RenderValidator.cs:350). Therefore, contradictory structured details can enter the ledger despite “V6 checked” claims. The validator also lacks the accepted parent thread’s own deltas and full ancestor text. Pass those inputs explicitly, restricted to the parent’s actual knowledge and ancestry; sequence ordering alone does not establish visibility across branches.

- **HIGH — Resume ordering can break narrative continuity (06-19, 06-20).**  
  Continue generates remaining relevant waves before repairing attachments on previously written earlier-wave threads. Later emails consequently cannot use details those repaired attachments introduce. Stage C also repairs different waves concurrently using cached, initial-state ledger views, so later repairs miss newly repaired earlier details. Process relevant repairs in chronological dependency order before dependent new work. For later emails already written, constrain repaired attachments to established details or flag conflicts without silently changing finished text.

- **HIGH — Completion is reported before durable completion (06-02, 06-17, 06-18, 06-20).**  
  `AttachmentUnitOfWork` increments Written counters and emits completion-related events before any EML or journal write succeeds. Cancellation during a subsequent attachment discards the accepted thread and earlier successful parts. A kill between `ThreadDone` and attachment records leaves successful embedded parts unrecorded; the repair design then regenerates them. Swallowed append failures can also let later waves use attachment deltas absent from durable replay. Define the commit boundary and reconciliation behavior explicitly, and account for all paid work when persistence fails.

- **HIGH — Failed/skipped outcomes lack durable identity (06-17, affecting 06-01/14/18/20).**  
  `AttachmentOutcome.ToRecord` receives neither attachment DocID nor attachment kind. Its only possible source is `Part`, which is null for Failed and Skipped outcomes. Add required attachment identity fields independent of bytes. Otherwise those records cannot reliably drive replay, answer-key status, or repair selection.

- **HIGH — Invite matching does not yet guarantee D-20 consistency (06-05, 06-11, 06-17).**  
  Make the meeting-date field explicit: [`LedgerEntry.Date`](C:/Users/ghand/source/repos/ReelDiscovery/ReelDiscovery/Models/Evidence/LedgerEntry.cs:18) is the source email’s date, and [`LedgerSelector`](C:/Users/ghand/source/repos/ReelDiscovery/ReelDiscovery/Services/Evidence/LedgerSelector.cs:37) selects entries before the thread starts. The actual meeting date is held in `Value`. Matching also needs fact/topic and participant compatibility. Independently hashing each invite’s DocID can assign different times to the same meeting, or a time before a same-day parent email. Resolve and persist one schedule per matched meeting.

- **HIGH — Version continuity and ledger identity are not stable under repair (06-05, 06-08, 06-14, 06-18, 06-20).**  
  Same-wave version context depends on which worker finishes first through mutable `DoneText`. Title-only grouping can also connect unrelated organizations’ documents. Separately, inserting a repaired attachment’s deltas before another attachment’s deltas can renumber existing attachment ledger entries or push them beyond the six-entry cap. Use explicitly scoped chains, deterministic predecessor scheduling, and attachment-derived stable ledger identities.

- **MEDIUM — Retry counters accumulate outcomes instead of replacing status (06-02, 06-17, 06-23).**  
  `RecordAttachment` lacks a DocID or previous-status parameter. A replayed Failed attachment becoming Written leaves the old failure counted; repeated failures also accumulate. This can make progress exceed its maximum. The existing thread counter already handles previous status. Apply equivalent transition semantics to attachments and keep spend accumulation separate.

- **MEDIUM — Media classification and gating need correction (06-04, 06-17).**  
  Not every HTTP 429 means exhausted quota; OpenAI distinguishes throttling from billing/spend failures. Classify the response error code, preserving retryable throttling behavior. [OpenAI error documentation](https://developers.openai.com/api/docs/guides/error-codes).  
  Also recheck the media stop flag **after acquiring the semaphore**. Otherwise queued callers that observed `Stopped == false` can continue making requests after another call stops media.

- **MEDIUM — Resource limits are enforced too late (06-10, 06-17, 06-20).**  
  Decoding an entire MIME part into `MemoryStream` before checking 25 MB does not bound allocation. Add bounded decoding and aggregate file/part limits. Fifty concurrent repair tasks can also retain substantial attachment buffers despite a two-call media semaphore; bound active repair work separately from provider calls.

- **MEDIUM — The parity suite overstates its evidence (06-21, 06-25).**  
  A synthetic plan deliberately built with matching distributions and a fake responder deliberately producing band-midpoint text validates plumbing, not production distribution parity. Add production-planner fixtures and measured live-run results. Preserve the accepted no-V8 policy. Also correct structural assertions: root/reply header sets legitimately differ, and inline-only messages need not have a `multipart/mixed` root. [MimeKit BodyBuilder implementation](https://raw.githubusercontent.com/jstedfast/MimeKit/master/MimeKit/BodyBuilder.cs).

The following table covers each plan’s individual readiness and additional revisions.

| Plan | Strength | Concern and specific improvement |
|---|---|---|
| **06-01** | Good journal and fixture foundation. | **MEDIUM:** `MaxOrganizations = 6` cannot cover all supported complex cases: the preset permits seven parties before additional organizations. Validate attachment-parent-thread ownership during replay and distinguish the latest run attempt from historical `RunEnded` records. |
| **06-02** | Useful separation of thread, attachment, and media counts. | **MEDIUM:** Make attachment status updates replace prior status by DocID; count committed outcomes separately from generated bytes. |
| **06-03** | Validator-clean overloads preserve legacy behavior. | **MEDIUM:** Automatically converting every numeric-looking string can remove leading zeros from identifiers. Define numeric-column semantics; test Unicode ICS folding and sheet names that become invalid after truncation. |
| **06-04** | A classified, priced media seam is appropriate. | **MEDIUM:** Correct 429 classification. The “never throws except cancellation” contract needs coverage for malformed responses and other SDK exceptions, not just HTTP exceptions. |
| **06-05** | Deterministic names and voice fallback are well scoped. | **HIGH:** Resolve meeting identity/time and chain dependencies as above. Document the narrow exception to D-02 that permits journaled final filenames to differ from approved plan filenames. |
| **06-06** | Separate filler assignment avoids changing existing relevant subjects. | **MEDIUM:** The `$1.25M` test cannot pass through the current scanner: its parser supports words such as “million,” but not `M` suffixes. Add an explicit scanner change and regression coverage. |
| **06-07** | Encoding and image-free quoting are sound. | **LOW:** The positive fixture uses `abc@x.com`, which fails the prescribed 16-hex-character CID regex. Freeze pre-change expected HTML; comparing two calls to the changed implementation does not prove compatibility. |
| **06-08** | Reusing content rules avoids validator duplication. | **HIGH:** Include structured attachment deltas and ancestor context. The Excel V3 exemption also leaves V6’s similar near-amount scan unchanged, so ledger figures can still produce subtotal false positives. |
| **06-09** | One composer preserves common metadata behavior. | **MEDIUM:** The bulk quote branch requires placeholder support that 06-07’s `BulkMailContent` API does not define. Specify that contract and test quoted bulk inline images. |
| **06-10** | Deterministic nested MIME and per-part DocIDs are strong. | **HIGH:** Fix per-parent harvesting and inspect alternate copies. Enforce byte limits during decoding. |
| **06-11** | Strict DTOs, clamping, and per-parent prompts are appropriate. | **HIGH:** Apply knowledge restrictions to own-thread deltas and prior-version text, not only guide facts. Define non-empty content by kind so a title-only or empty-table response cannot pass. |
| **06-12** | Persisted side projects and cast voices support consistent retries. | **MEDIUM:** The action specifies collision checks mainly for names, while the requirement includes topics and all canonical values. Validate fallback templates too, and remove the six-organization coverage conflict. |
| **06-13** | Pure copy functions are readily testable. | **MEDIUM:** “Last run ended” must refer to the latest `RunStarted` attempt. An older `RunEnded` must not conceal an interrupted Continue attempt. |
| **06-14** | Whole-corpus answer-key projection is the right design. | **HIGH:** Repair eligibility and ledger projection depend on the identity and commit fixes above. Separately, media spend must sum all attempts, not just latest attachment records. |
| **06-15** | Extending Start Over to planned non-relevant paths closes a real gap. | **MEDIUM:** Align initial and remaining estimates on richness, invite pricing, unavailable media, and overhead. Include side-world generation cost; avoid applying text QA overhead to media inconsistently. |
| **06-16** | Shared prompting honors category texture and ledger exclusion. | **LOW:** Promote shared prompt helpers in an explicitly owned file change. The plan permits editing `EvidencePrompts.cs` but omits it from `files_modified`. |
| **06-17** | Per-item retry and builder isolation fit RUN-02. | **HIGH:** Add outcome identity, correct commit-time reporting, and recheck media stop after slot acquisition. Declare the dependency on 06-09’s `PersonInfo.OrganizationName`. |
| **06-18** | A profile-driven unit of work is preferable to separate filler orchestration. | **HIGH:** Define cancellation after thread acceptance and failure between individual journal appends. Introduce `ThreadAttachmentContext` before the task whose signature requires it. |
| **06-19** | Relevant-first orchestration and bounded filler chunks follow D-25. | **HIGH:** Integrate earlier relevant repairs before dependent waves. Do not silently continue after failing to persist voices or side-world choices while promising identical choices on Continue. |
| **06-20** | Journal-based recomposition and legacy voicemail preservation are valuable. | **HIGH:** Fix harvesting and chronological repair. Add recovery for partial custodian rewrites and ensure a failed regeneration cannot silently remove an otherwise reusable successful part. |
| **06-21** | End-to-end inspection is stronger than isolated formatter tests. | **MEDIUM:** Test real planner output, condition comparisons on message shape, and distinguish measured content parity from fake-provider correctness. |
| **06-22** | Extensive minimum-size and 144-DPI coverage is appropriate. | **LOW:** Assert primary-action counts according to each state; avoid a universal “exactly one” rule for states with no available action. Inspect PNGs as well as clipping assertions. |
| **06-23** | Routing all generation buttons through one guarded entry point is sound. | **MEDIUM:** Correct latest-attempt detection and progress transitions. Specify how repair-row counts relate to attachment counts already displayed in wave rows. |
| **06-24** | Editable media prices and summary totals complete the user-facing work. | **LOW:** Add behavior tests for Cancel, Reset Defaults, and cloning prices into an active run; render tests cannot verify these semantics. |
| **06-25** | Automated and human gates cover complementary risks. | **MEDIUM:** Include a controlled failure/Continue exercise, a multi-message attachment repair, and a Phase 5 upgrade. Ensure the live fixture actually contains every required type; a small random corpus may not. Record skipped optional checks explicitly rather than forcing pass/issue. |

**Suggestions**

- **Revise the durable contracts first.** Give every attachment outcome immutable identity, separate generated content from committed status, and define recovery at each write/journal boundary.
- **Make relevant repair chronological.** Preserve the relevant-first decision while ensuring earlier attachment details are established before dependent new work.
- **Add focused failure-injection tests.** Cover cancellation after one successful attachment, failure on the second custodian copy, failure after `ThreadDone` but before attachment records, and interrupted repair.
- **Add adversarial consistency fixtures.** Include conflicting `newDetails`, private sibling branches, two invites for one meeting, same-wave document versions, and a repaired earlier attachment that would otherwise renumber existing ledger entries.
- **Reconcile interfaces and dependencies before execution.** In particular, outcome identity, bulk image placeholders, shared `DoneText`, scanner extensions, and `PersonInfo.OrganizationName` need explicit owners.
- **Replace fragile acceptance checks with behavioral evidence.** Exact `grep` counts constrain implementation unnecessarily; use portable checks and frozen compatibility fixtures. Keep the phase gate honest about what fake output establishes.

**Risk assessment: HIGH as written.** The main risks affect the core promises of the product: preserved finished work, stable replay, and a coherent case story. They are concentrated in attachment persistence, validation, and repair rather than spread across every feature. Resolving those contracts before implementation would substantially reduce integration risk without expanding into deferred Phase 7 work.

*This was a read-only review. No files were modified, and builds/tests were not run.*

---

## Consensus Summary

Only one external reviewer (Codex, codex-cli 0.160.0, read-only sandbox) was run, so there is no multi-reviewer consensus. The items below are Codex's HIGH concerns, ordered by priority for `/gsd-plan-phase 6 --reviews`. None of them repeats an issue the internal gsd-plan-checker already resolved (Phase 5 voicemail recompose, as-of-wave repair ledger, voice gender).

### Agreed Strengths
- Requirement traceability, one shared unit of work and formatter path for relevant and filler email, deterministic MIME and journal projections, and media failures kept separate from text failures. The internal plan-checker also noted these.

### Agreed Concerns (priority for replanning)
1. **Durable commit boundary.** `AttachmentUnitOfWork` reports Written before the EML or journal write succeeds. Cancellation or a kill between `ThreadDone` and the attachment records loses paid work, which repair then regenerates. (06-02, 06-17, 06-18, 06-20)
2. **Attachment outcome identity.** `AttachmentOutcome.ToRecord` has no DocID or kind when `Part` is null (the Failed and Skipped cases). (06-17, 06-01, 06-14, 06-18, 06-20)
3. **Harvest granularity.** `HarvestParts` stops at the first readable EML of a thread, so attachments on other messages look missing and get regenerated. Harvest per parent message and fall back to other custodian copies. (06-10, 06-20)
4. **Attachment V6 inputs.** `AttachmentCheckInput` lacks the attachment's `NewDetails` and the parent thread's deltas and ancestry, so contradictory details can enter the ledger. (06-08, 06-11, 06-17)
5. **Repair and resume chronology.** Continue writes later waves before repairing attachments on earlier waves, and Stage C repairs run concurrently on cached ledger views. (06-19, 06-20)
6. **Invite meeting identity (D-20).** The plans use `LedgerEntry.Date` (the source email's date), not the meeting date in `Value`. Two invites for the same meeting can get different hashed times. (06-05, 06-11, 06-17)
7. **Version chains and ledger IDs.** Same-wave predecessor text depends on which worker finishes first. Grouping by title only can link documents from different organizations. A repair can renumber attachment ledger entries. (06-05, 06-08, 06-14, 06-18, 06-20)

### MEDIUM items worth folding in
- Attachment counters should replace status by DocID instead of adding to it (06-02, 06-17, 06-23).
- Classify media 429 errors by error code, so throttling is not treated as exhausted quota, and recheck the media stop after the semaphore is acquired (06-04, 06-17).
- Enforce the MIME 25 MB limit while decoding, and bound concurrent repair work (06-10, 06-17, 06-20).
- `MaxOrganizations = 6` is less than the preset's seven parties (06-01, 06-12).
- The scanner does not parse `$1.25M` (06-06).
- The CID fixture breaks the 16-hex regex (06-07).
- The bulk quote placeholder contract is missing from 06-07 (06-09).
- "Last run ended" must refer to the latest `RunStarted` (06-13, 06-23).
- 06-16 is missing `EvidencePrompts.cs` from `files_modified`.
- The parity suite shows plumbing, not production parity (06-21, 06-25).

### Divergent Views
- **Internal plan-checker vs Codex:** the checker passed Dimension 9 (cross-plan data contracts) after revision 1. Codex rates the plans **HIGH risk** because of the persistence, identity and repair-ordering contracts above. These are deeper runtime-semantics issues that the checker's structural pass does not evaluate.
- Codex accepts the user's strict-D-12 decision (no V8 on filler) but asks the parity suite to report honestly what fake-provider output proves.
