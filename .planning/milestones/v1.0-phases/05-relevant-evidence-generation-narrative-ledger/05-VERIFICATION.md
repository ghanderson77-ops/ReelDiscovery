---
phase: 05-relevant-evidence-generation-narrative-ledger
verified: 2026-10-03T19:00:00Z
status: passed
human_verification_approved: 2026-10-03
score: 5/5 roadmap success criteria verified in code (live-run quality items pending human check)
overrides_applied: 0
deferred:
  - truth: "OUT-01 for the whole corpus: every email (relevant and non-relevant) appears in each participating custodian's mailbox folder"
    addressed_in: "Phase 6"
    evidence: "Phase 6 SC2/SC4: non-relevant traffic 'comes from the same pipeline ... same model and formatter path'. Relevant mail already lands in Sent Items/Inbox per custodian (MailboxWriter). NOTE: Phase 6's requirement list does not name OUT-01, so the traceability row must be moved or Phase 6 must claim it."
human_verification:
  - test: "Live provider run on a real test case (05-17 Task 2): approve a plan, click Generate Emails, let at least 2 waves finish"
    expected: "Later threads refer back to commitments, figures, meetings and people introduced earlier; no email mentions a fact dated after it or known only to people not on it"
    why_human: "Narrative coherence across waves depends on real model output; tests use a fake gateway"
  - test: "Read a sample of the generated relevant EMLs (Indirect / Clear / Explicit, hot, privileged counsel threads, several senders)"
    expected: "Natural business voice, varied subtlety, hot documents plainly state the key point, counsel threads read as plain legal advice with banner/label where assigned, distinct voices and signatures"
    why_human: "Reading quality and voice distinctness cannot be asserted by grep or unit tests"
  - test: "Open a multi-message thread from one custodian's Inbox and Sent Items in a mail client (Outlook / Thunderbird)"
    expected: "Messages group as one conversation, quoting shows the right parent, Date shows sender-local time, X-ReelDiscovery-DocID header matches the answer-key row"
    why_human: "Client-side threading display is external behavior"
  - test: "During the live run drag and resize the window, watch tiles and cost; press Cancel Generation mid-wave, then Continue Generating; separately kill the process mid-run and reopen the case"
    expected: "Window stays responsive; cost/tiles update live; after Cancel or kill every journaled thread's EMLs and answer-key rows remain, Continue finishes the rest without rewriting done threads"
    why_human: "Responsiveness and real process-kill behavior require the running app"
  - test: "Inspect the Phase 5 render PNGs and the live app at 150% DPI (Generate Corpus states, Case Summary Mailboxes / Story so far rows, re-plan dialog)"
    expected: "No clipping, no ellipsized buttons, no stock grey surfaces"
    why_human: "Visual check (project memory: verify WinForms UI at 150% DPI)"
  - test: "Open '<case> - Story So Far.docx' from Case Summary after the run"
    expected: "Ledger grouped by month, readable statements with date, kind, value, who knew it, facts, issues, source DocID"
    why_human: "Readability of the exported document"
---

# Phase 5: Relevant Evidence Generation & Narrative Ledger Verification Report

**Phase Goal:** Relevant threads are written as a coherent evidentiary story. Each email knows only what its participants could know at that date, later emails build on a running narrative ledger, and every finished thread is saved immediately.
**Verified:** 2026-10-03T19:00:00Z
**Status:** human_needed
**Re-verification:** No - initial verification

## Goal Achievement

### Observable Truths (ROADMAP success criteria)

| # | Truth | Status | Evidence |
|---|-------|--------|----------|
| 1 | Approved plan runs as chronological waves; later emails build on earlier ones; no email mentions later or unknown facts | VERIFIED (code) / quality needs human | `GenerationRunner.RunAsync` iterates `WavePlanner.Plan(input.Plan)` (whole plan, stable on Continue); per wave builds ledger from earlier deltas only (`LedgerMerger.Build` before tasks, merge after `Task.WhenAll`, line 198-228). `ThreadRenderContext.Create` calls `KnowledgeGate.Build` and `LedgerSelector.Select` (wave < k, dated before first message, known to a participant, cap 30). Prompt (`EvidencePrompts.BuildThreadPrompt`) carries only gated facts, knowable values, selected ledger, per-message may-mention; never RelevanceCriteria/complaint. V4 in `RenderValidator` flags future/unknown mentions. Live coherence is a human item. |
| 2 | Ledger extracted after each thread; contradictions with ledger, canonical values or plan flagged and retried; ledger in case.json; exports "story so far" | VERIFIED | ThreadDone record carries `LedgerDelta = best.NewDetails`; `RenderValidator` emits V1-V8 (V2 people, V3 canonical-value variants, V4 date/knowledge, V6 ledger value + date contradictions); `ThreadUnitOfWork` retries up to `MaxAttempts = 3` with violations appended as feedback; best attempt kept as "Generated - with warnings" (D-14). `GenerationState.Ledger` under `CorpusState.Generation`, `CaseDraft.CurrentFormatVersion = 3`; `GenerationStateApplier.Apply` on UI thread. `StorySoFarDocxBuilder` + `GenerationOutputs.WriteStorySoFarAsync`, wired in runner final checkpoint and Case Summary export (StepCaseSummary.cs:1347). |
| 3 | Natural correspondence, varied subtlety, hot docs, privileged threads, distinct voices/signatures, correct threading | VERIFIED (mechanics) / reading quality needs human | `RenderAssignments` (Apportion 60/30/10 subtlety, 55/35/10 length, hot = Explicit, privileged marking); `EmailComposer`: Message-ID = SHA-256(PlanId:DocId)[..24], `InReplyTo = parent.MessageId`, `References = KnowledgeGate.Ancestors`, RE:/FW: via ThreadingHelper, en-US "On ... wrote:" quote header, cast Signature verbatim; voice/sign-off style in prompt. |
| 4 | EMLs in each participating custodian's mailbox (sent and received) with matching X-ReelDiscovery-DocID | VERIFIED | `MailboxWriter.CopyPathsFor`: sender copy in `Sent Items`, each cast recipient in `Inbox`, folders from `PlanNumbering.CustodianFolders`; `CustodianIds` = From+To+Cc filtered to cast (PlanNumbering.cs:95). `BuildMime` adds `X-ReelDiscovery-DocID = email.Planned.DocId`; answer key overlay keyed by the same DocID and subject from `EmailComposer.SubjectFor`. Atomic writes via `CaseStore.WriteAtomicAsync`, guarded by `IsSafeEmlPath` + `ResolveUnderCase`. |
| 5 | Kill/Cancel keeps completed threads + key rows; single failure logged/skipped; progress & cost responsive | VERIFIED (code) / live behavior needs human | Journal `AppendAsync` writes one line and `Flush(flushToDisk: true)` after all copies are written (writes use `CancellationToken.None`); replay tolerates torn last line; key re-projected debounced 2 s, every wave and in `finally`; reopen re-materializes from journal. Any thread exception -> `FailAsync` ThreadFailed record, run continues. Run executes via `Task.Run(() => runner.RunAsync(...))`; 250 ms WinForms timer drains lock-protected `GenerationSnapshot`. |

**Score:** 5/5 truths verified at code level; live-output quality and responsiveness routed to human verification.

### Plan must-haves (05-01 .. 05-16)

All artifacts named in plan frontmatter exist with substantive implementations (8,108 lines across Services/Evidence, Models/Evidence, StepGenerateCorpus, GenerationUiText). Spot-checked key links all present: `Generation?.Normalize()`, `CorpusPlan.IsSafeEmlPath`, `Apportion.Largest`, `SHA256.HashData`, `KnowledgeGate.Ancestors`, `LedgerSelector.Select`, `LlmTier.Story`, `PlanCallRunner.RunAsync<ThreadRenderer.ThreadRenderDto>`, `RenderValidator.Check`, `AppendAsync`, `WavePlanner.Plan(input.Plan)`, `Task.WhenAll`, `GenerationOutputs.WriteAnswerKeyAsync/WriteStorySoFarAsync`, `Task.Run(() => runner.RunAsync`, `GenerationStateApplier.Apply`, `GenerationJournal.Replay`, `typeof(StepGenerateCorpus)`, `CaseStage.Generation => typeof(StepGenerateCorpus)`, `StepGenerateCorpus { IsGenerating: true }`.

05-17 must-haves: truth 1 (suites pass) VERIFIED by re-run; truths 2-4 (PNG inspection, live run reading quality, responsiveness) are the unperformed human checkpoint -> human_verification.

### Key Link Verification

| From | To | Via | Status |
|------|----|-----|--------|
| GenerationRunner | WavePlanner / ThreadUnitOfWork | whole-plan waves, per-wave Task.WhenAll | WIRED |
| ThreadUnitOfWork | PlanCallRunner -> RenderRepair -> RenderValidator -> EmailComposer -> MailboxWriter -> GenerationJournal | render/check/retry/compose/write/append | WIRED |
| ThreadRenderContext | KnowledgeGate / LedgerSelector | gated facts + ledger view | WIRED |
| GenerationRunner | GenerationOutputs | key + story re-projection | WIRED |
| StepGenerateCorpus.Run | GenerationRunner | Task.Run off UI thread | WIRED |
| StepGenerateCorpus.Run | GenerationStateApplier | UI-thread checkpoint (BeginInvoke) | WIRED |
| WizardForm | StepGenerateCorpus | step list, stage map, close-during-run guard | WIRED |
| StepCaseSummary | GenerationOutputs.WriteStorySoFarAsync | Export Story So Far | WIRED |

### Data-Flow Trace (Level 4)

| Artifact | Data | Source | Real data | Status |
|----------|------|--------|-----------|--------|
| Generate Corpus tiles/waves/activity | GenerationView | GenerationSnapshot.Drain fed by ThreadUnitOfWork/Runner | Yes | FLOWING |
| Answer key status/subject | overlay | GenerationRunState.From(plan, journal replay) | Yes | FLOWING |
| case.json ledger | Corpus.Generation.Ledger | LedgerMerger.Build from journaled deltas | Yes | FLOWING |
| Render prompt ledger section | LedgerView | LedgerSelector over merged earlier-wave ledger | Yes | FLOWING |

### Behavioral Spot-Checks

| Behavior | Command | Result | Status |
|----------|---------|--------|--------|
| App builds | `dotnet build ReelDiscovery.csproj` | 0 errors | PASS |
| Non-render suite | `dotnet test ... --filter "Category!=Render"` | 2572 passed, 0 failed | PASS |
| Render suite | not re-run (writes PNGs); executor reports 101 green | - | SKIP (human PNG review) |
| Live provider run | requires API key + running app | - | SKIP (human) |

### Requirements Coverage

| Req | Description (short) | Code status | REQUIREMENTS.md | Note |
|-----|--------------------|-------------|-----------------|------|
| CORP-01 | Point-in-time, participant-limited context | SATISFIED | Complete | KnowledgeGate + V4 |
| CORP-02 | Canonical values; output checked vs plan; retry | SATISFIED | **Pending** | Delivered (V2/V3/V4/V6 + 3-attempt retry; From/To/Date from plan by construction). Was left Pending by 05-12 and never updated. Mark Complete. |
| CORP-03 | Varied subtlety, natural voice, no legal framing | SATISFIED (mechanics) | Complete | Marked early: reading quality unconfirmed until 05-17 human check |
| CORP-04 | Hot documents | SATISFIED (mechanics) | Complete | Marked early: same |
| CORP-05 | Privileged counsel threads | SATISFIED | Complete | |
| CORP-06 | Correct threading headers/quoting | SATISFIED | Complete | Mail-client display is human item |
| CORP-07 | Per-character voice/length/signature | SATISFIED (mechanics) | Complete | Marked early: voice distinctness unconfirmed |
| CORP-08 | Narrative ledger extraction | SATISFIED | Complete | |
| CORP-09 | Ledger entries in later context | SATISFIED | Complete | |
| CORP-10 | Chronological waves; contradictions flagged/retried | SATISFIED | Complete | |
| CORP-11 | Ledger in case file; story-so-far export | SATISFIED | Complete | |
| OUT-01 | Per-custodian mailboxes (sent + received) | PARTIAL - relevant mail only | Pending | Correctly Pending; non-relevant mail is Phase 6, but Phase 6 does not list OUT-01 (traceability gap) |
| OUT-02 | X-ReelDiscovery-DocID traceable to key | SATISFIED | Complete | |
| RUN-01 | Completed thread saved immediately | SATISFIED | Complete | |
| RUN-02 | Failed item logged, retried/skipped | SATISFIED for emails | Complete | Attachments/images/invites/voicemails are Phase 6; requirement text covers them, so Complete is early |
| RUN-06 | Cancel keeps work | SATISFIED | Complete | |
| RUN-07 | Responsive progress/cost | SATISFIED (code) | Complete | Live responsiveness unconfirmed (human) |

No orphaned Phase 5 requirement IDs: every ID mapped to Phase 5 in REQUIREMENTS.md appears in at least one plan.

### Anti-Patterns Found

| File | Line | Pattern | Severity | Impact |
|------|------|---------|----------|--------|
| (Services/Evidence, Models/Evidence, StepGenerateCorpus*) | - | No TODO/FIXME/placeholder/NotImplemented | - | - |
| ReelDiscovery.Tests/Evidence/GenerateCorpusStepTests.cs | - | 2 xUnit1031 analyzer warnings (blocking task ops in tests), per 05-REVIEW-FIX | Info | Test project only |
| Services/Evidence/ThreadUnitOfWork.cs | 177-201 | Kept-with-warnings threads are written with known violations | Info | By design (D-14); flagged as "Generated - with warnings" in the key and Activity |

### Human Verification Required

See frontmatter `human_verification` (6 items): live run coherence across waves, reading quality/voice, mail-client threading, responsiveness + Cancel/kill/Continue, 150% DPI visual check, story-so-far document readability. These are the 05-17 Task 2 checkpoint, which has not been performed.

### Gaps Summary

No code-level blockers. All five roadmap success criteria have substantive, wired implementations, and the build plus 2572-test non-render suite pass on the current tree (including the 9 code-review fixes). The phase cannot be marked passed until the 05-17 human checkpoint confirms live output quality and responsiveness.

Bookkeeping corrections for REQUIREMENTS.md:
- CORP-02 is delivered but still marked Pending. Mark it Complete.
- CORP-03, CORP-04, CORP-07 and RUN-07 were marked Complete before the live human check. Confirm them in 05-17 Task 2.
- RUN-02 covers attachments, images, invites and voicemails, which arrive in Phase 6. It was marked Complete early.
- OUT-01 correctly stays Pending, but no later phase claims it. Phase 6 should add OUT-01, or the traceability table should move it to Phase 6.

---

_Verified: 2026-10-03T19:00:00Z_
_Verifier: Claude (gsd-verifier)_
