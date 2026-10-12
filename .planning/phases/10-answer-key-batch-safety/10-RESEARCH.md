# Phase 10: Answer-Key & Batch Safety - Research

**Researched:** 2026-10-12
**Domain:** Answer-key relabel round-trip (CSV/XLSX read-back + sidecar), provider-batch ledger safety (cross-provider, Start Over spend, stale wave batches), dead-type cleanup
**Confidence:** HIGH for code seams, MEDIUM for Excel file behavior, HIGH/MEDIUM for provider cancel semantics (see each claim)

<user_constraints>
## User Constraints (from CONTEXT.md)

### Locked Decisions

#### Relabel capture and storage (W4)
- **D-01:** Relabels are made by hand in `answer-key.csv` or `answer-key.xlsx` (Phase 7 D-13 stands; no new UI).
  - Immediately before **every** answer-key write, the app harvests user relabels from the on-disk key files.
  - It saves them to a new sidecar, `answer-key-relabels.json`, in the case folder (constant name, tolerant reads, atomic writes).
  - It then re-applies them to the projected rows, so the XLSX Statistics sheet reflects them too.
  - The sidecar follows the `QaReportStore` and `discovery-documents.json` pattern. It is **not** stored in case.json or the journal: Start Over deletes the journal, and `GenerationOutputs` never touches the draft.
- **D-02:** All answer-key writers go through one harvest-and-reapply step:
  - `GenerationOutputs.WriteAnswerKeyAsync`, called from GenerationRunner, ThreadInvalidation, Start Over, retry-on-leave and reopen / crash recovery;
  - `CorpusApproval.BuildFiles`.
  - No writer may bypass it.
- **D-03:** **Detecting a relabel.** A user edit is any on-disk Relevance value, for the same PlanId, that differs from "plan projection + current sidecar relabels". Setting a row back to the plan label removes the relabel.
  - The app reads both the CSV and the XLSX "Answer Key" sheet. If they disagree for the same DocID, the file with the later LastWriteTimeUtc wins.
  - The XLSX reader must handle both inline-string cells (what our writer produces) and shared-string cells (what Excel produces on save).
  - Reads must allow sharing (FileShare.ReadWrite) so they work while Excel has the file open.

#### Relabel key and scope
- **D-04:** Relabels are keyed by **(PlanId, DocID)**. DocIDs change only on re-plan; Regenerate Thread and Start Over keep the PlanId.
- **D-05:** **What is preserved.**
  - The Relevance column: only "Relevant" or "Not Relevant", trimmed, case-insensitive, with a leading `'` CSV-guard stripped. Other values are ignored.
  - On relabeled rows only, the user's Rationale cell, kept verbatim when it differs from the projected rationale.
  - No family propagation: a relabel applies only to the DocID the user edited.
  - Edits to Hot, Privileged, Issue Codes and Fact IDs are **not** preserved. Tooltip and copy say "edit the Relevance column".

#### Relabel lifecycle and warnings
- **D-06:** **Regenerate Thread** keeps relabels. When the thread has any, `QaUiText.CQ1RegenerateThread` adds the clause "Relabels you made for its emails in the answer key are kept."
- **D-07:** **Start Over** keeps relabels. When any exist, `GenerationUiText.CG2StartOver` adds "Relabels you made in the answer key are kept."
- **D-08:** **Re-plan** (a new PlanId) is the only event that drops relabels, and the user is warned first.
  - When the current plan has relabels, `CorpusUiText.ReplanBody` and the settings-change replace notice add "{N} manual relabels in the answer key will be dropped."
  - Sidecar entries for other PlanIds are ignored and pruned on the next write.
- **D-09:** DocIDs that are not in the plan, and rows the user added or deleted, are ignored. Removing a row is not a relabel.
- **D-10:** **Damaged key file.** If a key file exists but cannot be parsed, the app copies it to a timestamped backup next to the original, writes the new file, and reports the backup through the existing KeyError/status path. It never refuses to write.
  - A key file locked by Excel needs nothing new: the existing write failure is already reported and nothing is discarded.
- **D-11:** Relabels apply to the **key files only**. BlindReviewer and LeakageAudit keep plan labels (KEY-03: labels come from the plan; QA measures content against plan intent).
  - Accepted trade-off: a relabeled email can show as a disagreement again on the next blind review.

#### Re-approval resets the key (deferred from 09-RESEARCH Pitfall 9)
- **D-12:** This is in scope. `CorpusApproval.BuildFiles` re-applies relabels when it re-approves the **same** PlanId. When the journal is readable, it also applies the run-state overlay (`GenerationRunState.From(plan, GenerationJournal.Replay(...)).BuildFullOverlay`, as in ThreadInvalidation), so re-approval after Keep This Plan no longer resets statuses to Planned.
  - Approving a new PlanId writes the plain plan key.

#### Cross-provider pending batches (W5)
- **D-13:** Ledger records already carry `Provider`, so no schema change is needed.
  - At run start (Generate or Continue), **before the first wave**, the app resolves this plan's pending batches that belong to a provider other than the current text provider, using that provider's transport when its key is available. It logs this in Activity:
    - a batch that has already ended is collected, and its results are priced and billed as orphans through the existing CR-A04 path;
    - any other batch is cancelled via `BatchingLlmProvider.CancelPendingAsync(..., include: r => r.Provider != current)`, and its items then run once under the current provider.
- **D-14:** If the other provider's key is **missing**, the run refuses to start. A clear notice names the provider and points to API Configuration: add that key, or switch back.
  - There is no "run anyway" override, because silent double pay is exactly what W5 forbids.
  - Gating the provider switch only on API Configuration is not enough, because `TextProvider` is not persisted and resets to OpenAI on launch.
- **D-15:** **The pending-batch banner** counts only batches of this plan for the current `TextProvider`, which is what "collected first, without paying again" describes.
  - Other-provider batches with a key get their own clause: "{N} {Provider} batches from an earlier session are collected or cancelled before generating."
  - Other-provider batches without a key show the blocking notice from D-14 instead.
  - A provider-filtered overload goes beside `BatchLedger.PendingBatchCount`, used at its three call sites.
  - Start Over keeps `PendingBatchCountAllPlans` (WR-A05).
  - `ResumePending_IgnoresBatchesOfAnotherProvider` is revised, not deleted.

#### Batch spend fixes (WR-A06 / WR-A08)
- **D-16:** **WR-A06.** In `StartOverAsync`, after `CancelPendingBatchesAsync` and before `GenerationReset` deletes `provider-batches.jsonl` and `batch-results/`, the app prices unconsumed collected results with the same lookup as `PriceOrphans`. It records them on the session `UsageTracker` under the existing "Batch results not used" operation.
  - Billing orphans on Cancelled/Fatal is rejected (07-REVIEW-FIX), as is a new persistent spend field.
- **D-17:** **WR-A08.** Before the first relevant wave, stale "wave N" batches (from builds before amended D-07) go through these steps:
  1. They are cancelled at the provider but still tracked (not Closed).
  2. They are polled until they reach a terminal state, bounded by the existing `BatchTimings` drain bound.
  3. Partial results are collected and used free by live scopes. Leftovers become itemized orphans billed at Finished.
  4. If the bound expires, the run goes live and leaves the batch polling, and late results become billed orphans.

  This replaces the unitemized end-of-run `CloseStaleWaveBatchesAsync`. Live waves still never wait a full provider cycle (amended D-07).

#### Legacy types (W8)
- **D-18:** Delete `class Attachment` and `enum AttachmentComplexity` from `Models/Attachment.cs`.
  - Keep `enum AttachmentType`, which is serialized in the plan and journal. Move it to `Models/AttachmentType.cs` in the same namespace.
  - Add `ReelDiscovery.Models.Attachment` and `ReelDiscovery.Models.AttachmentComplexity` as `InlineData` to `PlatformTests.LegacyType_IsDeleted`.

#### Testing
- **D-19:** Use xUnit with the existing fakes and add no new test infrastructure.
  - `FakeBatchTransport`: use two instances, with Kind set to OpenAI and Anthropic.
  - Step tests use the `BatchTransportFactory`, `RunGatewayFactory` and `GatewayFactory` seams.
  - Relabel round-trips cover both CSV and XLSX (inline and shared-string cells) via `SpreadsheetDocument.Open`.
  - Run `Category=Scale` once, because batching changes (07-REVIEW-FIX precedent).

### Claude's Discretion
- How the harvest is implemented: the parser structure, and whether CSV and XLSX readers share a row model.
- The exact Activity log wording for cross-provider resolution, consistent with existing Activity phrasing.
- The exact timestamp format of the backup file name, as long as it is filesystem-safe and inside the case folder.

### Deferred Ideas (OUT OF SCOPE)
- Persisting `TextProvider` per case.
- A same-provider model change also strands that provider's batches, because custom ids hash the model.
- An in-app relabel UI.
- A persistent case-level spend ledger that outlives Start Over.
- Applying relabels to blind review and leakage audit inputs (see the D-11 trade-off).
- W7: planning traceability, plus the 11 REQ-IDs missing from the REQUIREMENTS.md traceability table. This is documentation work, not this phase.
</user_constraints>

<phase_requirements>
## Phase Requirements

| ID | Description | Research Support |
|----|-------------|------------------|
| QA-01 | Blind review flags disagreements that can be relabeled or regenerated | Relabel harvest/sidecar/re-apply (D-01..D-12): "Relabel Harvest Algorithm", "Writer Inventory", Pitfalls 1-8 |
| KEY-03 | Labels come from the plan, family relevance propagated | Relabel is an overlay on the plan projection, never family-propagated, key files only (D-05, D-11); plan label stays the baseline |
| RUN-03 | Bulk generation uses provider Batch APIs | D-13/D-17 cancel+collect semantics verified against OpenAI and Anthropic docs ("Provider Cancel Semantics") |
| RUN-04 | Submitted batches tracked on disk so results survive restart | D-17 keeps cancelled stale batches *uncancelled in the ledger* until collected (Pitfall 10); D-13 resolution persists Collected/Cancelled lines |
| PROV-01 | Choose Anthropic or OpenAI | D-13/D-14 make a provider switch safe; key availability decided by `transportFor(kind) != null` |
| PROV-07 | Usage/cost tracking incl. batch discounts | D-16 Start Over pricing onto session `UsageTracker`; D-13 collected foreign results billed via CR-A04 orphan path; D-17 itemized orphans |
| PLAT-05 | Dead code removed | D-18: `Attachment`/`AttachmentComplexity` have zero references (verified by grep); guard in `PlatformTests.LegacyType_IsDeleted` |
</phase_requirements>

## Project Constraints (from CLAUDE.md)

- C# / WinForms; csproj is authoritative: **`net10.0-windows`**, `DocumentFormat.OpenXml` **3.5.1**, `OpenAI` **2.14.0**, `Anthropic` **12.51.0**, `MimeKit` 4.18.1 [VERIFIED: ReelDiscovery.csproj]. (CLAUDE.md's "net8 / OpenXml 3.0.2 / OpenAI 2.1.0" text is stale.)
- No new NuGet packages unless necessary; reuse existing builders. No record types, no primary constructors, no `#region`; mutable classes with auto-properties.
- `ct` is always the last parameter; optional trailing `IProgress<T>?`/`CancellationToken ct = default`.
- `[GeneratedRegex]` instead of `new Regex(...)`; `Random.Shared` in new code.
- Catch `OperationCanceledException` before `Exception`; bare `catch` only for non-critical side effects, always with an explanatory comment.
- UI text is pure static `*UiText` methods; new copy arrives as optional trailing parameters so existing callers/tests compile (Phase 9 precedent).
- Answer-key writes happen off the UI thread; failures are reported, never thrown to the UI.
- Paths = case folder + constant names only (T-04-68, T-05-33, T-07-40).
- GSD workflow: edits only through `/gsd-execute-phase`.
- Memory notes: verify WinForms UI at 150% DPI (only relevant for new banner/notice copy wrapping).

## Summary

Phase 10 is three independent workstreams on code that is already well-factored: (1) a relabel **read-back** layer in front of the two answer-key writers, (2) three batch-ledger fixes (cross-provider resolution at run start, Start Over pricing, stale-wave drain), and (3) a trivial type deletion. Every seam is located below with file:line.

The relabel work is the riskiest part. The locked D-03 rule ("differs from plan projection + current sidecar") is correct for the common case but **misreads a stale file as a user removal** when one of the two key files failed to write (Excel lock) on a previous pass. The fix that stays inside D-03's intent is to keep, per key file, a snapshot of what we last wrote (PlanId, SHA-256, the relabels applied in that write). A file only "votes" when its content changed since our write *and* a row differs from that file's own baseline. This one refinement handles "our own write is not a user edit", the PlanId check, the stale-file case and the legacy (pre-Phase-10) case.

On the provider side, both vendors document that a cancelled batch still yields results for completed requests: OpenAI "will have partial results (if any) available in the output file" after `cancelling` (up to 10 minutes) → `cancelled` [CITED]; Anthropic's canceled batches "end up with a status of `ended` and may contain partial results" [CITED]. That makes D-17's cancel-then-collect design sound, **provided the D-17 path does not append a `Cancelled` ledger line** (a Cancelled line makes the batch non-pending and a crash would lose its paid results). Both transports already map the intermediate states (`cancelling`/`canceling`) to `InProgress` and the poll loop already collects Cancelled/Expired/Ended batches.

**Primary recommendation:** Build one `AnswerKeyFiles` funnel in `Services/Corpus` (harvest → apply → build CSV/XLSX → write → record per-file snapshot, all under a per-case-folder gate) and route `GenerationOutputs.WriteAnswerKeyAsync` and `CorpusApproval.WriteFilesAsync` through it. Do cross-provider resolution in `StepGenerateCorpus.RunCoreAsync` **before** `CreateRunGateway` (the `BatchingLlmProvider` constructor's `Recover` reads the ledger once). Implement D-17 as a new `BatchingLlmProvider.DrainStaleStagesAsync` that cancels via `_transport.CancelAsync` directly (no ledger line, no `Closed`), then awaits per-batch completion bounded by `DrainTimeout`.

## Architectural Responsibility Map

| Capability | Primary Tier | Secondary Tier | Rationale |
|------------|-------------|----------------|-----------|
| Relabel harvest (read CSV/XLSX), sidecar persistence, re-apply | Services/Corpus (file I/O service, off UI thread) | — | Same layer as AnswerKey writers; GenerationOutputs (Evidence) and CorpusApproval (Corpus) both call it |
| Run-state overlay for re-approval (D-12) | Services/Evidence (`GenerationRunState`) | UI step computes and passes it | Corpus layer currently has no `using ReelDiscovery.Services.Evidence`; keep it that way by passing the overlay in |
| Relabel counts for warnings (D-06/07/08) | Services/Corpus (dry harvest) | UiText (pure copy) | Copy stays pure; counts computed off the UI thread by the step/dialog |
| Cross-provider resolution + D-14 gate | UI step (`RunCoreAsync`) calling a static Services/Ai helper | Services/Ai (`BatchingLlmProvider` static) | Needs keys (`WizardState.GetApiKey`) and must run before the gateway is constructed |
| Stale wave drain (D-17) | Services/Ai (`BatchingLlmProvider` instance) | Services/Evidence (`GenerationRunner` calls it before wave 0) | Needs the tracked batch/poll state that lives in the instance |
| Start Over pricing (D-16) | Services/Ai (static pricer over ledger) | UI step records on `_state.UsageTracker` | No `BatchingLlmProvider` instance exists at Start Over time |
| Banner/notice counts (D-15) | Services/Ai (`BatchLedger` static counts) | UiText | Pure count + pure copy |

## Standard Stack

### Core (all already referenced — no new packages)
| Library | Version | Purpose | Why Standard |
|---------|---------|---------|--------------|
| DocumentFormat.OpenXml | 3.5.1 [VERIFIED: csproj] | Read the "Answer Key" sheet (inline + shared strings) | Already the XLSX writer; `SpreadsheetDocument.Open(Stream, false)` |
| `Microsoft.VisualBasic.FileIO.TextFieldParser` | in-box (Microsoft.VisualBasic.Core.dll, .NET Core 3.0+ through .NET 11) [CITED: learn.microsoft.com TextFieldParser] | RFC 4180-ish CSV read-back (quoted fields, embedded newlines) | In the shared framework; no package |
| `System.Text.CodePagesEncodingProvider` | in-box [ASSUMED] | Windows-1252 fallback when Excel re-saved the CSV as "CSV (Comma delimited)" (ANSI) | `CodePagesEncodingProvider.Instance.GetEncoding(1252)`; `Encoding.Latin1` is the zero-risk fallback if this is unavailable |
| `System.Security.Cryptography.SHA256` | in-box | Fingerprint of each key file we wrote | `SHA256.HashData(bytes)` |
| `System.Text.Json` | in-box | Sidecar (`CaseDraft.JsonOptions`, as `QaReportStore`) | Existing pattern |

### Alternatives Considered
| Instead of | Could Use | Tradeoff |
|------------|-----------|----------|
| TextFieldParser | ~50-line RFC 4180 state machine | Fully controls whitespace/blank lines inside quoted Rationale; TextFieldParser needs `TrimWhiteSpace = false` and throws `MalformedLineException` per bad line. Either is acceptable (discretion); if TextFieldParser is used, catch `MalformedLineException` per line and skip the row |
| CsvHelper / ClosedXML | — | New dependency in a single-file publish for one read path; not justified |
| SAX `OpenXmlReader` | DOM `SheetData` | DOM is fine for ~10-15K rows x 23 cols; SAX only if the Scale run shows memory pressure |

**Installation:** none.

## Writer Inventory (D-02) — every answer-key write today

| # | Writer call | File:line | Overlay used | Thread |
|---|-------------|-----------|--------------|--------|
| 1 | Run checkpoint | `Services/Evidence/GenerationRunner.cs:1041-1042` (`CheckpointAsync`) | `state.BuildOverlay(plan, ctx.Assignments, ctx.Chains)` | pool |
| 2 | Regenerate Thread | `Services/Evidence/ThreadInvalidation.cs:98` | `BuildFullOverlay` | pool (ConfigureAwait false) |
| 3 | Start Over | `UserControls/StepGenerateCorpus.Run.cs:982` | **`new AnswerKeyOverlay()`** (plain Planned) | UI |
| 4 | Retry on leave | `UserControls/StepGenerateCorpus.Run.cs:1097` (`OnLeaveStepAsync`, inside `Task.Run`) | `BuildFullOverlay` | pool |
| 5 | Reopen / crash recovery | `UserControls/StepGenerateCorpus.Run.cs:1204` (`ReadEntryAsync`, inside `Task.Run` from :178) | `BuildFullOverlay` | pool |
| 6 | Approval | `Services/Corpus/CorpusApproval.cs:35-47` `BuildFiles` → `WriteFilesAsync` :52-92 → `ApproveAsync` :130; called from `UserControls/StepReviewPlan.Run.cs:529` | none (plan only) | UI |

Writers 1-5 all go through `GenerationOutputs.WriteAnswerKeyAsync` (`Services/Evidence/GenerationOutputs.cs:31-67`). Writer 6 builds bytes itself. Non-production direct writers exist only in tests (`ReelDiscovery.Tests/Render/CaseSummaryRenderTests.cs:138-139, 958-959`) [VERIFIED: grep].

**Recommended funnel:** a new `Services/Corpus/AnswerKeyFiles.cs` with
`Task<AnswerKeyWriteResult> WriteAsync(string caseFolder, CorpusPlan plan, CaseGuide guide, AnswerKeyOverlay? overlay, FileWriter writer, CancellationToken ct)` that harvests, applies, builds both byte arrays, writes each independently (keeps Pitfall-8 "locked CSV must not block XLSX"), and saves the sidecar. `GenerationOutputs.WriteAnswerKeyAsync` becomes a thin wrapper; `CorpusApproval.WriteFilesAsync` calls it for the first two files and then writes the protocol (keeping its stop-at-first-failure semantics for approval). `BuildFiles` keeps its public signature for `CorpusApprovalTests:69` (add optional trailing `AnswerKeyOverlay? overlay = null, IReadOnlyDictionary<string, AnswerKeyRelabel>? relabels = null`).

Add a source-scan guard test (PlatformTests style, it already reads `Program.cs` text): `AnswerKeyCsvWriter.Build(` and `AnswerKeyXlsxWriter.Build(` appear in production code only inside `AnswerKeyFiles.cs` (and `CorpusApproval.BuildFiles` if it keeps building bytes). This enforces "no writer may bypass it".

## Architecture Patterns

### System Architecture Diagram

```
                ┌────────────── any key write trigger ───────────────┐
 Runner checkpoint │ ThreadInvalidation │ Start Over │ Leave-retry │ Reopen │ Approve (same PlanId → overlay from journal)
                └──────────────────────┬──────────────────────────────┘
                                       ▼
                    AnswerKeyFiles.WriteAsync  [per-case-folder SemaphoreSlim]
                                       │
        ┌──────────────────────────────┼─────────────────────────────────┐
        ▼                              ▼                                 ▼
 Load sidecar (tolerant)     Read answer-key.csv bytes          Read answer-key.xlsx bytes
 relabels[PlanId]            (FileShare.ReadWrite|Delete)        (FileShare.ReadWrite|Delete)
 fileSnap[csv/xlsx]                    │                                 │
        │               hash == snap.Sha256 ? ──skip──┐   hash == snap.Sha256 ? ──skip
        │               snap.PlanId == plan ? else skip│   snap.PlanId == plan ? else skip
        │               parse (fail → backup bytes, D-10)  parse sheet (inline/shared/str)
        │                              ▼                                 ▼
        │                   votes: rows where Relevance ≠ baseline_F(DocID)
        └──────────────► resolve votes (single vote wins; conflicting votes → later LastWriteTimeUtc)
                                       ▼
                 relabels' = relabels ± votes  (value == plan label → remove)
                                       ▼
          rows = AnswerKeyBuilder.Build(plan, guide, overlay) → Apply(relabels')
                                       ▼
               CSV bytes / XLSX bytes (Statistics reflect relabels)
                                       ▼
            writer(csv) ─ok→ snap[csv] = {PlanId, sha(csvBytes), applied=relabels'}
            writer(xlsx)─ok→ snap[xlsx] = {...}     (failure keeps the old snapshot)
                                       ▼
                 Save sidecar atomically (prune other PlanIds) — failure tolerated
```

```
Run start (StepGenerateCorpus.RunCoreAsync)
  guide-stale / not-approved checks (existing :418-438)
  ──► ResolveOtherProviderPending(ledger, planId, current, transportFor)   [NEW, off UI thread]
        foreign pending of this plan? none → continue
        any foreign provider with transportFor(kind) == null → REFUSE (D-14 notice), return
        per record: status = transport.GetStatus
           Ended/Expired/Cancelled → GetResults → classify vs record.Items → SaveResults → append Collected (record.Provider) → delete files
           Failed → all-Errored cache + Collected (mirror FailAtProviderAsync)
           InProgress → CancelPendingAsync(include: id match, provider != current)
           status/cancel failure → REFUSE with "could not reach {Provider}" (recommended, see Open Q1)
  ──► CreateRunGateway (constructor Recover now sees Collected foreign results → _available → orphans)
  ──► GenerationRunner.RunAsync
        ResumePending (:264) → RunStarted (:276)
        ──► batch.DrainStaleStagesAsync("wave *", DrainTimeout, ct)   [NEW, replaces :536]
        voice cast → relevant waves (live; collected stale results consumed free) → non-relevant (batched)
        Finished → AddOrphanSpend (:537, bills foreign + stale leftovers) → RunEnded → MarkOrphansBilled
```

### Recommended Project Structure (new/changed files)
```
Services/Corpus/
├── AnswerKeyFiles.cs          # NEW funnel: harvest → apply → build → write → snapshot (D-01/D-02)
├── AnswerKeyReader.cs         # NEW: CSV + XLSX → List<(DocId, Relevance, Rationale)> (shared row model)
├── AnswerKeyRelabelStore.cs   # NEW sidecar (QaReportStore pattern): answer-key-relabels.json
├── AnswerKeyBuilder.cs        # unchanged (pure); relabels applied after Build
└── CorpusApproval.cs          # WriteFilesAsync uses the funnel; BuildFiles gains optional overlay/relabels
Services/Evidence/
├── GenerationOutputs.cs       # WriteAnswerKeyAsync → AnswerKeyFiles.WriteAsync
└── GenerationRunner.cs        # D-17 drain before wave 0; delete CloseStaleWaveBatchesAsync (:1119-1131)
Services/Ai/
├── BatchLedger.cs             # PendingBatchCount(folder, planId, provider) overload
└── BatchingLlmProvider.cs     # ResolveOtherProviderPendingAsync (static), PriceUnusedResults (static), DrainStaleStagesAsync
Models/
├── Attachment.cs              # DELETE file
└── AttachmentType.cs          # NEW: enum AttachmentType only
```

### Pattern 1: Relabel Harvest Algorithm (refines D-03 without changing its intent)

**What:** each key file has its own baseline = "what that file held when we last wrote it" = plan label overridden by `snap[F].Applied`. A file votes only for rows that differ from its baseline.

**Why this is D-03-compatible:** when both files were written successfully last time, `baseline_F` *is* "plan projection + current sidecar relabels". It only differs when a previous write of that file failed, which is exactly when the literal rule misfires (Pitfall 2).

```
sidecar (answer-key-relabels.json):
{
  "version": 1,
  "relabels": [ { "planId": "...", "docId": "BR-000123", "relevance": "Not Relevant",
                  "rationale": "user text or empty", "capturedUtc": "...", "source": "answer-key.xlsx" } ],
  "files": {
    "answer-key.csv":  { "planId": "...", "sha256": "hex", "writtenUtc": "...",
                         "applied": { "BR-000123": { "relevance": "Not Relevant", "rationale": "..." } } },
    "answer-key.xlsx": { ... }
  }
}

Harvest(folder, plan, guide):
  planLabel[docId] ← from plan threads (email + attachment DocIDs; Relevant/Not Relevant by thread.Relevance)
  current ← sidecar.relabels where planId == plan.PlanId
  for F in [csv, xlsx]:
     if !exists(F) continue
     bytes ← read with FileShare.ReadWrite|Delete (IOException → F.Unreadable = true; continue)
     snap ← sidecar.files[F]
     if snap != null && snap.sha256 == SHA256(bytes) continue           // our own write, untouched
     owner ← snap?.planId ?? LegacyOwner(parsed rows, plan)               // Pitfall 4
     if owner != plan.PlanId continue                                      // PlanId check (D-04/D-08)
     rows ← parse(bytes)  (parse failure → F.Damaged = bytes; continue)    // D-10
     for row in rows where planLabel has row.DocId and Normalize(row.Relevance) ∈ {Relevant, Not Relevant}:
        base ← snap?.applied[row.DocId]?.relevance ?? planLabel[row.DocId]
        if value ≠ base: vote(row.DocId, value, row.Rationale, LastWriteTimeUtc(F))
        else if current has row.DocId and rationale changed vs snap.applied: vote rationale-only update
  per DocID: one vote → take; two votes that disagree → later LastWriteTimeUtc wins (D-03)
  apply: value == planLabel → remove; else upsert { relevance, rationale = custom ? verbatim : "" }
```

Normalize: trim; strip one leading `'` (CSV guard); case-insensitive compare to "Relevant"/"Not Relevant"; return canonical `AnswerKeyBuilder.RelevantLabel/NotRelevantLabel`; anything else → ignored (D-05). Duplicate DocID rows with conflicting values in one file → ignore that DocID for that file (D-09 spirit).

Custom rationale (D-05): on relabeled rows only. Treat it as custom when, after stripping a CSV guard `'` that precedes a formula lead, it is non-empty and **does not start with** the row's base projected rationale (the rationale before `WithNote` appended a run note), and differs from the stored one. A StartsWith test survives overlay notes ("Skipped: ...") that change between writes (Pitfall 6).

### Pattern 2: Shared-read of an Excel-open file
```csharp
// Source: Telerik KB "open locked files read only" + OpenXml SDK stream overload (MEDIUM)
byte[] bytes;
using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
using (var ms = new MemoryStream())
{
    fs.CopyTo(ms);
    bytes = ms.ToArray();
}
using var doc = SpreadsheetDocument.Open(new MemoryStream(bytes), false);   // never Open(path, false): it uses FileShare.Read
```
Read once into memory: the same bytes give the SHA-256, the parse input and, if unparseable, the D-10 backup content (no second open of a locked file).

### Pattern 3: XLSX cell text (inline, shared, formula-string)
```csharp
// Source: ECMA-376 cell types; OpenXml SDK 3.x object model
static string CellText(Cell cell, SharedStringTable? sst)
{
    var type = cell.DataType?.Value;
    if (type == CellValues.SharedString)
    {
        if (sst == null || !int.TryParse(cell.CellValue?.Text, NumberStyles.None, CultureInfo.InvariantCulture, out var i)) return "";
        var item = sst.Elements<SharedStringItem>().ElementAtOrDefault(i);   // index once into a List for O(1)
        return item == null ? "" : TextOf(item);
    }
    if (type == CellValues.InlineString) return cell.InlineString == null ? "" : TextOf(cell.InlineString);
    return cell.CellValue?.Text ?? "";            // str (formula result), n, b, e
}
// TextOf: concatenate Text elements of the item/runs, EXCLUDING PhoneticRun (<rPh>) descendants — InnerText includes them
```
Column positions come from the header row by name (`AnswerKeyColumns.Headers`, case-insensitive trim), and each cell's column from `CellReference` letters (fallback: running position when `r` is absent). Sheet: the one named "Answer Key"; fallback to the first sheet whose header row has DocID and Relevance.

### Pattern 4: CSV read-back tolerant of Excel re-save
- Encoding: UTF-8 BOM → UTF-8; UTF-16 BOM → that encoding; no BOM → strict UTF-8 (`new UTF8Encoding(false, true)`), on `DecoderFallbackException` → Windows-1252 [ASSUMED Excel ANSI = system code page; 1252 on US/Western].
- Delimiter: honor a first line `sep=X`; otherwise pick the one of `,` `;` `\t` that makes the header row contain both "DocID" and "Relevance" [CITED: ablebits — Excel saves with the Windows list separator, `;` in most European locales].
- Fields: RFC 4180 quoting, embedded CRLF in quoted fields, trailing empty fields; never trim inside quotes.

### Pattern 5: Cross-provider resolution helper (D-13/D-14)
```csharp
// Sketch — Services/Ai/BatchingLlmProvider.cs (static); returns what happened for Activity and the gate
public static async Task<OtherProviderResolution> ResolveOtherProviderPendingAsync(BatchLedger ledger,
    Func<LlmProviderKind, IBatchTransport?> transportFor, Guid planId, LlmProviderKind current,
    CancellationToken ct = default)
// 1. state = ReadForCancel(ledger)  (unreadable → throws IOException: refuse run, CR-A05)
// 2. foreign = state.Pending(planId).Where(r => r.Provider != current)
// 3. missing = foreign.Select(r => r.Provider).Distinct().Where(k => transportFor(k) == null) → if any: return Blocked(missing)
// 4. per record: GetStatusAsync → terminal: GetResultsAsync → Classify(record.Items, results) [share with CollectAsync]
//    → ledger.SaveResults → Append Collected {Provider = record.Provider, PlanId, OutputFileId} → DeleteFilesAsync
// 5. still running: CancelPendingAsync(ledger, transportFor, planId, include: r => r.Provider != current && ids.Contains(r.BatchId))
```
Key availability = `transportFor(kind) != null`; the step's default `transportFor` already returns null for an empty key (`StepGenerateCorpus.Run.cs:1032-1036`), and tests drive it with `BatchTransportFactory`.

### Pattern 6: Stale wave drain (D-17)
```csharp
// Sketch — BatchingLlmProvider instance method; replaces CloseStaleWaveBatchesAsync
public async Task<int> DrainStaleStagesAsync(Func<string, bool> stageMatch, CancellationToken ct = default)
{
    List<TrackedBatch> stale;
    lock (_lock) stale = _batches.Values.Where(b => !b.Closed && stageMatch(b.StageLabel ?? "")).ToList();
    if (stale.Count == 0) return 0;
    foreach (var b in stale)
    {
        try { await _transport.CancelAsync(b.BatchId, ct); }            // NO Cancelled ledger line, NOT Closed
        catch (BatchCancelFailedException) { /* keep polling; bound still applies */ }
    }
    // Ended = TaskCompletionSource completed by UntrackLocked (collect, fail, release)
    try { await Task.WhenAll(stale.Select(b => b.Ended.Task)).WaitAsync(_timings.DrainTimeout, ct); }
    catch (TimeoutException) { /* D-17 step 4: go live; batch keeps polling; late results become orphans */ }
    return stale.Count;
}
```
Call it in `GenerationRunner.RunAsync` right after the `RunStarted` append (`:276-284`), inside the `try` so a user cancel during the drain ends the run as Cancelled, and only when `batch != null`. Then delete `CloseStaleWaveBatchesAsync(batch, snapshot)` at `:536` and its definition at `:1119-1131`; keep `AddOrphanSpend` at `:537`.

### Anti-Patterns to Avoid
- **Comparing the on-disk file to "projection + sidecar" without per-file snapshots** — misreads a file whose last write failed as a user removal (Pitfall 2).
- **Using `CancelPendingAsync` / `CancelPendingStagesAsync` for D-17** — both append `Cancelled`, which removes the batch from `Pending()` and loses its paid results on a crash (Pitfall 10).
- **Resolving foreign batches inside the runner** — `BatchingLlmProvider`'s constructor already ran `Recover`; results collected afterwards never reach `_available`, so they are never billed (Pitfall 11).
- **`SpreadsheetDocument.Open(path, false)`** — opens with `FileShare.Read`, fails while Excel has the file.
- **Applying relabels in `AnswerKeyBuilder.Build`** — keep the builder a pure plan projection (KEY-03); BlindReviewer/LeakageAudit/QaRunner call it and must keep plan labels (D-11).
- **Putting the backup notice into `GenerationState.KeyError`** — a non-empty `KeyError` triggers the retry on leave (`StepGenerateCorpus.Run.cs:1072`) forever (Pitfall 9).

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| XLSX parsing | zip + XML string scraping | `SpreadsheetDocument.Open(Stream,false)` (OpenXml 3.5.1) | Shared strings, rich text runs, phonetic runs, missing `r` attributes |
| Code page 1252 | manual byte table | `CodePagesEncodingProvider.Instance.GetEncoding(1252)` (or `Encoding.Latin1`) | Excel ANSI save |
| File fingerprint | timestamps/length only | `SHA256.HashData` | OneDrive/AV touch timestamps; Excel "save without change" rewrites |
| Atomic sidecar write | in-place overwrite | `CaseStore.WriteAtomicAsync` / `QaReportStore.WriteJson` pattern (temp + `File.Move(overwrite)` with retries) | Crash-safe, AV-lock tolerant |
| Orphan pricing | new pricing math | `_priceLookup(provider, modelId)` + `model.CalculateCost(usage)` with `usage.IsBatch = true; usage.Normalize()` exactly as `PriceOrphans` (`BatchingLlmProvider.cs:288-345`) | Batch discount, cache tokens, CR-A04 unpriced handling |
| Result classification for foreign collect | new matcher | extract `CollectAsync`'s "only submitted ids; missing → Expired" block (`:1207-1216`) into a static helper | T-07-23 |

**Key insight:** every hard part (atomic writes, ledger replay, orphan pricing, cancel semantics) already exists; Phase 10 is mostly re-routing and two read paths.

## Provider Cancel Semantics (external questions)

| Question | Answer | Source / Confidence |
|----------|--------|---------------------|
| OpenAI: does a batch cancelled while in progress still give an `output_file_id` with completed items? | Yes, when anything completed: "The batch will be in status `cancelling` for up to 10 minutes, before changing to `cancelled`, where it will have partial results (if any) available in the output file." Our `OpenAIBatchTransport.GetResultsAsync` already downloads both `output_file_id` and `error_file_id` for any terminal state (`:154-189`). `output_file_id` may be null if nothing completed → result list empty → every id becomes Expired → runs live. | [CITED: developers.openai.com/api/reference/.../batches/methods/cancel] HIGH |
| OpenAI: are completed requests in a cancelled batch billed? | Not stated for cancel. For expired batches the guide says "You will be charged for tokens consumed from any completed requests." Treat cancelled the same. | [CITED: developers.openai.com/api/docs/guides/batch] for expired; cancelled = [ASSUMED] MEDIUM |
| Anthropic: do canceled batches return results for requests that succeeded before cancellation? | Yes: "Canceled batches end up with a status of `ended` and may contain partial results for requests that were processed before cancellation." Unsent requests get result type `canceled`: "You will not be billed for these requests." | [CITED: platform.claude.com/docs/en/build-with-claude/batch-processing] HIGH |
| How long do `cancelling` / `canceling` last? | OpenAI: up to 10 minutes (documented). Anthropic: not documented; "cancellation may not take immediate effect". | OpenAI [CITED] HIGH; Anthropic duration unknown |
| Transport mapping | OpenAI `MapStatus`: `cancelling` → InProgress, `cancelled` → Cancelled (`OpenAIBatchTransport.cs:243-251`); Anthropic: `canceling` → InProgress, `ended` → Ended (`AnthropicBatchTransport.cs:163-168`). Poll loop collects Ended/Expired/Cancelled (`BatchingLlmProvider.cs:1166-1191`). | [VERIFIED: codebase] |

**Consequence for D-17:** `BatchTimings.DrainTimeout` defaults to 5 minutes (`BatchingLlmProvider.cs:25`), less than OpenAI's documented 10-minute `cancelling` window. On OpenAI the bound will sometimes expire; D-17 step 4 already covers that (late results → orphans).

## Excel File Behavior (external questions)

| Question | Answer | Confidence |
|----------|--------|------------|
| Encoding/BOM when Excel re-saves a CSV | Two save types exist: "CSV UTF-8 (Comma delimited)" (UTF-8, with BOM) and "CSV (Comma delimited)" (ANSI/system code page, no BOM; non-ASCII outside the code page is lost). Which one a plain Save of an opened BOM file uses is not documented in sources found → reader must accept both. | MEDIUM [CITED: ablebits.com convert-excel-csv] / [ASSUMED] for Save behavior |
| `;` separator in non-US locales | Excel uses the Windows **List separator** for CSV; in most European locales that is `;`. A `sep=;` first line is honored on import. | MEDIUM [CITED: ablebits.com change-excel-csv-delimiter] |
| Other Excel CSV edits | Excel may reformat dates/numbers and strip leading zeros on re-save. Only DocID, Relevance, Rationale matter here; DocIDs like `BR-000123` and `BR-000123.0001` are text-safe. Excel keeps a literal leading `'` in CSV data as part of the value. | MEDIUM [ASSUMED for the apostrophe] |
| Does Excel convert inline strings to shared strings on save? | Excel's own files store text as `t="s"` shared strings; user reports say edited files come back as shared strings. No official statement of the conversion → reader handles `s`, `inlineStr`, and `str`. | MEDIUM [CITED: ericwhite.com, learn.microsoft.com Q&A 1279879] |
| Can a file be read with FileShare.ReadWrite while Excel has it open? | Generally yes when opened `FileAccess.Read` + `FileShare.ReadWrite` and copied into memory (vendor KB guidance); fails only if Excel denies read sharing. Our own write still fails (lock) → existing reported failure (D-10). | MEDIUM [CITED: Telerik SpreadProcessing KB]; confirm in UAT |

## Runtime State Inventory

Not a rename phase, but D-18 deletes a type and D-13/D-16/D-17 touch persisted state:

| Category | Items Found | Action Required |
|----------|-------------|------------------|
| Stored data | `provider-batches.jsonl` (records carry `Provider`, `PlanId`, `StageLabel`); `batch-results/*.jsonl`; NEW `answer-key-relabels.json`. `AttachmentType` is serialized in plan/journal as enum — moving it to another file in the same namespace changes nothing on disk. | No migration. Sidecar is created lazily. |
| Live service config | Provider-side batches/files of the other provider (OpenAI input/output files; Anthropic batches) | D-13 collects or cancels them; files deleted best-effort as today |
| OS-registered state | None — verified (no tasks/services) | None |
| Secrets/env vars | Keys held in `WizardState` (session); other-provider key needed for D-13 | Keys only to transport constructors (T-07-54) |
| Build artifacts | `obj/gsd-artifacts` | None |

## Common Pitfalls

### Pitfall 1: Our own write read back as a user edit
**What goes wrong:** the next harvest parses the file we just wrote and "detects" differences (e.g., Rationale notes, statuses, or relabels already applied).
**How to avoid:** SHA-256 fast path (`snap.Sha256 == hash(bytes)` → skip), and compare only Relevance (+ Rationale on relabeled rows) against that file's own baseline. Record the snapshot from the **bytes passed to the writer**, only when the writer returned without throwing.
**Warning signs:** relabel count grows after a plain Continue with no Excel edits.

### Pitfall 2: Stale file misread as a removal (literal D-03 bug)
**What goes wrong:** relabel R captured from the CSV; XLSX write failed (Excel open). User later saves the XLSX in Excel (it never contained R). Literal D-03 sees XLSX row ≠ "projection + sidecar" and removes R.
**How to avoid:** per-file baseline (Pattern 1). Votes, not "disagreement", decide; only conflicting votes use LastWriteTimeUtc.
**Test:** CSV relabel → XLSX writer fails → modify XLSX elsewhere → next write keeps R.

### Pitfall 3: Concurrency between background key writes and harvest
**What goes wrong:** two writers (e.g., a runner checkpoint on the pool and `OnLeaveStepAsync`/`ReadEntryAsync`/`ThreadInvalidation`) interleave harvest and write, so one overwrites a relabel the other just harvested, or both rewrite the sidecar.
**How to avoid:** a process-wide `SemaphoreSlim` per normalized case folder (`ConcurrentDictionary<string, SemaphoreSlim>(StringComparer.OrdinalIgnoreCase)` keyed by `Path.GetFullPath(Path.TrimEndingDirectorySeparator(folder))`), held across harvest → write CSV → write XLSX → save sidecar. Never nest it. Today the UI gates mostly serialize these (`_isGenerating`, `_isResetting`), but `ThreadInvalidation` runs from Case Summary and `ReadEntryAsync` on entry, so do not rely on UI gating.
**Residual:** a non-locking editor (Notepad) saving between our read and our move is a sub-second race; accept (optionally re-hash just before the write and re-harvest once if it changed).

### Pitfall 4: PlanId check when no snapshot exists (legacy cases, deleted sidecar)
**What goes wrong:** the CSV carries no PlanId. After a re-plan, old-plan DocIDs map to unrelated new-plan rows, so every row would "vote".
**How to avoid:** with a snapshot, use `snap.PlanId`. Without one, accept the file as belonging to the current plan only if its (DocID → Thread ID, Doc Type) pairs match the plan for (nearly) every row and the row count equals the projection; otherwise skip it. Approval of a new PlanId writes plain rows and creates snapshots, so this fallback is a one-time upgrade path. Flag in Assumptions (A3).

### Pitfall 5: The D-14 block and existing tests
**Finding:** no existing step test seeds a ledger record whose `Provider` differs from the step's text provider (fixture default `TextProvider = OpenAI`; seeded records at `GenerateCorpusStepTests.cs:890-901, 969-978` are OpenAI). Runner-level tests (`BatchParityTests`, `GenerationRunnerTests`) call `GenerationRunner.RunAsync` directly, so a step-level resolver cannot affect them. With resolution in the step, the expected impact on Continue/Recover tests is **none** [VERIFIED: grep]. Tests that must change deliberately:
- `BatchingLlmProviderTests.ResumePending_IgnoresBatchesOfAnotherProvider` (:866): its last assertion ("a fresh submission") encodes the W5 double pay. Revise: the provider still ignores foreign records (Recover is unchanged), and a companion test shows that after `ResolveOtherProviderPendingAsync` the foreign batch is Cancelled/Collected before the new submission.
- `BatchParityTests.FinishedRun_ClosesStaleWaveBatch_PendingCountZero` (:646-706): rewrite for D-17. `cancelFails=false`: the fake marks it Cancelled, then returns **all** items succeeded, so the live render is not sent (served free), the ledger has **Collected, not Cancelled**, and the cost differs from the reference. `cancelFails=true`: NeverEnd plus cancel failure means the drain bound expires; set `DrainTimeout` small in that test (`Fast()` at :89-95 does not set it, so the default 5 min would apply); pending stays 1, with no Cancelled line.
- `BatchingLlmProviderTests.CancelPendingStagesAsync_*` (:1196, :1243): keep if `CancelPendingStagesAsync` stays public (still used? after D-17 it has no production caller → either delete with tests or keep as API; prefer delete with the tests, PLAT-05 spirit).

### Pitfall 6: Rationale round-trip false "custom rationale"
**What goes wrong:** the projected rationale changes between writes (overlay note "Skipped: ...", or the run state changes), so a relabeled row's untouched rationale looks user-edited; or the CSV guard `'` is compared literally.
**How to avoid:** custom only if it does not start with the base projected rationale (before `WithNote`); strip one `'` when followed by a formula lead (`AnswerKeyCsvWriter.FormulaLeads`). Re-writing a user rationale passes through `Field()` again, which is stable (a value starting with `'` is not re-guarded).

### Pitfall 7: Excel-open read with the wrong API
`File.ReadAllBytes` / `SpreadsheetDocument.Open(path,…)` use `FileShare.Read` and fail while Excel holds the file open for write. Use Pattern 2. A read `IOException` means **skip harvesting that file**. Recommended: also skip writing that file in this pass and report it as `FileOpenElsewhere`; otherwise an unreadable-but-writable file could have unsaved edits overwritten. (The write would fail anyway in practice.)

### Pitfall 8: Start Over writes the key with an empty overlay
`StepGenerateCorpus.Run.cs:982` passes `new AnswerKeyOverlay()`. Relabels must still apply (D-07). Route it through the funnel like the other writers; Planned statuses are correct after Start Over.

### Pitfall 9: D-10 backup notice in `KeyError`
`GenerationState.KeyError` non-empty drives `retryKey` in `OnLeaveStepAsync` (`:1072`), and the key write itself succeeded. Add a separate notice (e.g., `GenerationFileResult.BackupFileName` / `Notice`) surfaced through `SetStatus` and `ThreadInvalidationResult` (`KeyNotice`), not `KeyError`. Backup name: `answer-key (unreadable yyyyMMdd-HHmmss).csv` / `.xlsx` from `DateTime.Now`, invariant culture, written via `CaseStore.WriteAtomicAsync` from the bytes already read. Use `FileMode.CreateNew` semantics or a counter suffix on collision.

### Pitfall 10: D-17 must not append `Cancelled`
`BatchLedgerState.Pending()` excludes `CancelledIds` (`BatchLedger.cs:82-86`). Both `CancelPendingAsync` and `CancelPendingStagesAsync` append `Cancelled` (`BatchingLlmProvider.cs:478-494`) and `DeleteRecordFilesAsync` deletes the OpenAI input file (and the output file if known). For D-17, call `_transport.CancelAsync` directly, keep the batch in `_batches`/`_inflight`, do not set `Closed`, and let the existing poll loop `CollectAsync` it (which appends `Collected` and deletes files after the cache is durable). If the app dies mid-drain, the record is still pending and the next session resumes and collects it.

### Pitfall 11: Order of resolution vs `BatchingLlmProvider` construction
`Recover` runs in the constructor (`BatchingLlmProvider.cs:157`) and loads collected results of **any** provider for this plan into `_available` (`:183-213`). Foreign results collected after construction never become orphans. Resolve in `RunCoreAsync` **before** `CreateRunGateway` (`StepGenerateCorpus.Run.cs:446-449`). Foreign results never match current custom ids (`RequestIdentity.CustomIdFor` hashes provider+model), so they are always orphans and are billed at Finished via `PriceOrphans` with `_priceLookup(orphan.Provider, …)` (CR-A04; covered by the test at :1544).

### Pitfall 12: Live mode with foreign pending batches
If `CaseUseBatch` is false, `CreateRunGateway` returns `Batch = null` and nothing bills orphans in the run. The resolver must still run (cancel running batches). Collected foreign results then stay unbilled in the ledger until a batch-mode Finished run or Start Over (D-16). This is rare: the checkbox is only on the Ready card (`StepGenerateCorpus.cs:484-496`, `Run.cs:1279-1288`). See Open Question 2.

### Pitfall 13: D-16 must price only what will be deleted
Price only when `!cancelFailed` and the ledger replays `Readable` (the same condition `GenerationReset.DeleteAll` uses at `GenerationReset.cs:338-350`). Otherwise the ledger is kept and a later run would bill the same results again. Include every plan (Start Over deletes the whole ledger). Skip `OrphansBilledIds`, consumed `(batch, id)` pairs and non-usable results. Skip collected batches without a cache (CX-05: no usage to price).

### Pitfall 14: Banner/close/cancel prompts after D-15
`_pendingProviderBatches` feeds `BannerBody`, `CG1` (`Run.cs:886`) and `CG3` (`WizardForm.cs:609`). After D-15 they count current-provider batches only. Pass the other-provider count and key state separately, so the banner shows the D-15 clause or the D-14 notice. The CG1/CG3 "keep running" clauses then correctly refer to current-provider batches only.

## Code Seams per Decision

| D | Seam (file:line) | Change |
|---|------------------|--------|
| D-01/02/03/05/09/10 | `GenerationOutputs.cs:31-67`; `CorpusApproval.cs:35-92`; new `Services/Corpus/AnswerKeyFiles.cs`, `AnswerKeyReader.cs`, `AnswerKeyRelabelStore.cs` | Funnel; harvest; apply after `AnswerKeyBuilder.Build`; snapshot; backup |
| D-04/D-08 prune | sidecar save | Drop entries/snapshots whose PlanId ≠ plan.PlanId on every save |
| D-06 | `QaUiText.CQ1RegenerateThread` (`QaUiText.cs:335`), caller `Forms/QaFindingsDialog.cs:407` | Optional trailing `bool relabelsKept = false`; count = relabels whose DocID belongs to the thread (emails + attachments), computed off the UI thread |
| D-07 | `GenerationUiText.CG2StartOver` (`GenerationUiText.cs:987`), caller `StepGenerateCorpus.Run.cs:940-947` (count in the existing `Task.Run` tuple) | Optional trailing `bool relabelsKept = false` |
| D-08 | `CorpusUiText.ReplanBody` (`CorpusUiText.cs:315`) via `ReplanDialog(approved, generatedThreads)` (`Forms/ReplanDialog.cs:19,35`; caller `StepReviewPlan.Run.cs:474`); settings notice `CorpusUiText.SettingsNotice` unapproved branch (`CorpusUiText.cs:214-215`, caller `StepCorpusSettings.cs:1159`) | Optional trailing `int relabels = 0`; copy "{N} manual relabels in the answer key will be dropped." Use a cheap sidecar-only count on the UI thread for the settings notice, or compute a dry harvest on step entry off the UI thread |
| D-11 | `AnswerKeyBuilder.Build` stays pure | No change in QA paths (`QaRunner`, `BlindReviewer`, `LeakageAudit`) |
| D-12 | `StepReviewPlan.Run.cs:529` computes the overlay (journal replay `Readable && !PlanChanged` → `GenerationRunState.From(plan, replay).BuildFullOverlay(plan, guide)`, off the UI thread) and passes it to `CorpusApproval.ApproveAsync`/`WriteFilesAsync` | Keeps Services/Corpus free of a Services/Evidence dependency (it has none today [VERIFIED: grep]). Respect "ct last": add an overload or put `overlay` before `writer` with named arguments at existing test call sites |
| D-13/D-14 | `StepGenerateCorpus.Run.cs:412-450` (`RunCoreAsync`, between the approval check :431-438 and `CreateRunGateway` :446-449) | Resolver plus refusal notice (`SetStatus(..., Theme.Warning)` and banner); Activity lines via new `GenerationRunInput` data, emitted by the runner after `snapshot.Reset` (:239-252) as new appended `GenerationEventKind` members (append-only enum, `GenerationProgress.cs:36-37`) |
| D-15 | `BatchLedger.cs:274-280` add `PendingBatchCount(string caseFolder, Guid planId, LlmProviderKind provider)`; call sites `StepGenerateCorpus.cs:1309`, `StepGenerateCorpus.Run.cs:531`, `:1193`; `GenerationUiText.BannerBody` (`:935-943`) | New optional trailing `int otherProviderBatches = 0, string otherProvider = "", bool otherProviderBlocked = false`. `ReadEntryAsync` is static, so pass `_state.TextProvider` in |
| D-16 | `StepGenerateCorpus.Run.cs:963-971` (after `CancelPendingBatchesAsync` :965, before `GenerationReset.StartOverAsync` :971) | New static `BatchingLlmProvider.PriceUnusedResults(BatchLedger, Func<LlmProviderKind,string,AIModelConfig?>)` → `OrphanBill`; record each line on `_state.UsageTracker.RecordUsage("Batch results not used", model, usage, cost)`. Make `OrphanOperation` (`:384`) `public const`. Extract the price lambda from `WizardState.CreateRunGateway` (`WizardState.cs:107-108`) into a `WizardState` method so both use it |
| D-17 | `GenerationRunner.cs:255-266` (resume), `:276-284` (RunStarted), `:533-538` (finish), `:1119-1131` (`CloseStaleWaveBatchesAsync`) | Pattern 6; add `TaskCompletionSource Ended` to `TrackedBatch` (`BatchingLlmProvider.cs:1683+`) completed in `UntrackLocked` (`:971-979`) |
| D-18 | `Models/Attachment.cs` → delete; new `Models/AttachmentType.cs`; `ReelDiscovery.Tests/PlatformTests.cs:86-108` | Two `InlineData` rows. Zero references to `Attachment`/`AttachmentComplexity` [VERIFIED: grep] |

## Code Examples

### Applying relabels after projection
```csharp
// Source: pattern derived from AnswerKeyBuilder.Build (Services/Corpus/AnswerKeyBuilder.cs:33-107)
var rows = AnswerKeyBuilder.Build(plan, guide, overlay);          // pure plan + run-state projection
foreach (var row in rows)
{
    if (!relabels.TryGetValue(row.DocId, out var r)) continue;      // keyed by DocID within plan.PlanId
    row.Relevance = r.Relevance;                                     // canonical "Relevant" / "Not Relevant"
    if (r.Rationale.Length > 0) row.Rationale = r.Rationale;          // verbatim user text (D-05)
}
var csv = AnswerKeyCsvWriter.Build(rows);
var xlsx = AnswerKeyXlsxWriter.Build(rows, guide);                   // Statistics use row.Relevance → reflect relabels
```

### Start Over pricing (D-16)
```csharp
// Source: StepGenerateCorpus.Run.cs:963-971 seam; PriceOrphans math at BatchingLlmProvider.cs:288-345
var cancelFailed = pending > 0 && !await CancelPendingBatchesAsync(folder);
if (!cancelFailed)
{
    var bill = await Task.Run(() => BatchingLlmProvider.PriceUnusedResults(new BatchLedger(folder), _state.PriceBatchModel));
    foreach (var line in bill.Lines)
        _state.UsageTracker.RecordUsage(BatchingLlmProvider.OrphanOperation, line.Model, line.Usage, line.Cost);
}
var reset = await GenerationReset.StartOverAsync(folder, plan, Draft.Guide, keepBatchLedger: cancelFailed);
```

## State of the Art

| Old Approach | Current Approach | When Changed | Impact |
|--------------|------------------|--------------|--------|
| `CloseStaleWaveBatchesAsync` at Finished (Cancelled line, spend "not itemized") | Cancel-but-track + bounded drain before wave 0 (D-17) | Phase 10 | Stale results served free or billed as orphans |
| Recover ignores other-provider pending batches | Resolved (collect/cancel) at run start; refuse without key | Phase 10 | W5 closed |
| Answer key a pure projection, overwritten each write | Projection + sidecar relabels | Phase 10 | W4 closed |

**Deprecated/outdated:** `CancelPendingStagesAsync` loses its only production caller after D-17.

## Assumptions Log

| # | Claim | Section | Risk if Wrong |
|---|-------|---------|---------------|
| A1 | OpenAI bills completed requests of a *cancelled* batch (documented only for expired) | Provider Cancel Semantics | Orphan cost slightly over-reported; never under. Low |
| A2 | Excel "CSV (Comma delimited)" save uses the system ANSI code page (1252 on Western installs); plain Save of a BOM file may switch formats | Excel File Behavior | Non-ASCII in user Rationale mangled; Relevance unaffected. Low |
| A3 | Legacy (no-snapshot) files can be attributed to the current plan by matching (DocID → Thread ID, Doc Type) | Pitfall 4 | Old-plan file misread → bogus relabels on first upgrade write. Medium; keep the threshold strict |
| A4 | `CodePagesEncodingProvider` is in the .NET 10 shared framework without a package | Standard Stack | Compile error → use `Encoding.Latin1`. Low |
| A5 | Reading an Excel-open file with `FileShare.ReadWrite` succeeds | Excel File Behavior | Harvest skipped while Excel is open; write also fails → nothing lost (D-10). Low; verify in UAT |
| A6 | Excel stores a literal leading `'` from CSV data as text (round-trips it) | Pitfall 6 | Guard-strip handles either case. Low |
| A7 | OpenXml SDK 3.x rejects DTDs / external entities when reading packages | Security | XXE on a malicious key file. Low (local user file); verify `XmlReaderSettings` if concerned |

## Open Questions

1. **Foreign-batch status or cancel call fails (network/5xx) at run start.**
   - Known: `CancelPendingAsync` with default flags rethrows `BatchCancelFailedException` (`BatchingLlmProvider.cs:470-482`; test at :1269).
   - Unclear: D-13 does not cover this case.
   - Recommendation: refuse the run with "{Provider} could not be reached to stop its earlier batches; try again" (same no-double-pay principle as D-14). The planner should confirm this in UI copy.
2. **Live-mode case with foreign pending batches (Pitfall 12).**
   - Recommendation: run the resolver anyway. Leave collected foreign results unbilled until a batch Finished run or Start Over (D-16). Do not add live-mode orphan billing.
3. **Projected rationale on relabeled rows without a user rationale.**
   - The projected rationale explains the *plan* label, so it reads contradictory after a relabel.
   - D-05 only says to keep the user's text.
   - Recommendation: keep the projected rationale unchanged (no new copy), and note this for UI-SPEC.
4. **Rationale-only edits on an already relabeled row.**
   - D-05 implies they are kept.
   - Pattern 1 handles them with a rationale-only vote. Confirm this is wanted.

## Environment Availability

| Dependency | Required By | Available | Version | Fallback |
|------------|------------|-----------|---------|----------|
| .NET SDK (net10.0-windows target) | build/test | ✓ | 10.0.401 (also 9.0.103) | — |
| Excel | manual UAT of relabel round-trip | not probed | — | Tests simulate Excel output (shared strings, ANSI `;` CSV) |
| OpenAI / Anthropic keys | manual UAT of D-13/D-17 | not probed | — | Fakes (`FakeBatchTransport` x2) |

## Validation Architecture

### Test Framework
| Property | Value |
|----------|-------|
| Framework | xUnit 2.9.3, Microsoft.NET.Test.Sdk 17.14.1 (`ReelDiscovery.Tests/ReelDiscovery.Tests.csproj`, net10.0-windows) |
| Config file | none (traits: `Category=Render` x19, `Scale` x1, `LiveSpike` x1) |
| Quick run command | `dotnet test ReelDiscovery.Tests/ReelDiscovery.Tests.csproj --artifacts-path obj/gsd-artifacts --filter "FullyQualifiedName~<TouchedTestClass>"` |
| Full suite command | `dotnet test ReelDiscovery.slnx --artifacts-path obj/gsd-artifacts --filter "Category!=Scale&Category!=LiveSpike"` (~11 min) |
| Scale (once, D-19) | `dotnet test ReelDiscovery.Tests/ReelDiscovery.Tests.csproj --artifacts-path obj/gsd-artifacts --filter "Category=Scale"` |

### Phase Requirements → Test Map
| Req ID | Behavior | Test Type | Automated Command (filter) | File Exists? |
|--------|----------|-----------|----------------------------|-------------|
| QA-01/KEY-03 | CSV relabel survives checkpoint, Continue, Regenerate Thread, reopen, Start Over | unit/integration | `FullyQualifiedName~AnswerKeyRelabelTests` | ❌ Wave 0 (`Corpus/AnswerKeyRelabelTests.cs`) |
| QA-01 | XLSX relabel read from inline-string and shared-string cells (build a shared-string workbook with `SpreadsheetDocument.Create` in the test) | unit | `FullyQualifiedName~AnswerKeyReaderTests` | ❌ Wave 0 |
| QA-01 | CSV variants: BOM UTF-8, no-BOM 1252, `;` delimiter, `sep=;`, quoted multiline Rationale, `'` guard | unit | `FullyQualifiedName~AnswerKeyReaderTests` | ❌ Wave 0 |
| QA-01 | Own write not a user edit; stale-file not a removal (Pitfall 2); set-back removes; DocID not in plan ignored; conflicting votes → later mtime | unit | `FullyQualifiedName~AnswerKeyRelabelTests` | ❌ Wave 0 |
| QA-01 | Damaged file → backup + new file written + notice, `KeyError` empty | unit | `FullyQualifiedName~AnswerKeyRelabelTests` | ❌ Wave 0 |
| QA-01 | Excel-open read (FileStream held with FileShare.Read/ReadWrite by the test) still harvests | unit | `FullyQualifiedName~AnswerKeyReaderTests` | ❌ Wave 0 |
| KEY-03 | Re-approve same PlanId keeps relabels + overlay statuses; new PlanId → plain key, sidecar pruned | unit | `FullyQualifiedName~CorpusApprovalTests` | ✅ extend |
| QA-01 | Copy clauses D-06/07/08 present only with relabels; defaults unchanged | unit | `FullyQualifiedName~QaUiTextTests\|FullyQualifiedName~GenerationUiTextTests\|FullyQualifiedName~CorpusUiTextTests` | ✅ extend |
| QA-01 | No writer bypasses the funnel (source scan) | unit | `FullyQualifiedName~PlatformTests` | ✅ extend |
| RUN-03/04, PROV-01 | Foreign pending, key present: ended → Collected + billed orphan at Finished; running → Cancelled, items run once | integration (step, two `FakeBatchTransport`) | `FullyQualifiedName~GenerateCorpusStepTests` | ✅ extend |
| PROV-01 | Foreign pending, key missing (`BatchTransportFactory` returns null for that kind) → run refused, no gateway call, notice names provider | integration (step) | `FullyQualifiedName~GenerateCorpusStepTests` | ✅ extend |
| RUN-04 | Banner counts current provider only + D-15 clause / D-14 notice | unit + render | `FullyQualifiedName~GenerationUiTextTests\|FullyQualifiedName~BatchLedgerTests` | ✅ extend |
| RUN-03 | `ResumePending_IgnoresBatchesOfAnotherProvider` revised | unit | `FullyQualifiedName~BatchingLlmProviderTests` | ✅ revise |
| PROV-07 | Start Over records "Batch results not used" on the session tracker; not when cancel failed or ledger unreadable | integration (step) | `FullyQualifiedName~GenerateCorpusStepTests` | ✅ extend |
| RUN-03/PROV-07 | D-17: stale wave batch cancelled (no Cancelled line), collected, served free; bound expiry → live + still pending | integration (runner) | `FullyQualifiedName~BatchParityTests` | ✅ rewrite `FinishedRun_ClosesStaleWaveBatch_PendingCountZero` |
| PLAT-05 | `Attachment`, `AttachmentComplexity` absent | unit | `FullyQualifiedName~PlatformTests` | ✅ extend |
| all | 5K-run regression with batching | scale | `Category=Scale` | ✅ |

### Sampling Rate
- **Per task commit:** quick filter on touched classes.
- **Per wave merge:** `FullyQualifiedName~AnswerKey|FullyQualifiedName~Batch|FullyQualifiedName~GenerateCorpusStepTests|FullyQualifiedName~CorpusApprovalTests|FullyQualifiedName~PlatformTests` plus the Render command from 09-VALIDATION.md when banner copy changes.
- **Phase gate:** full suite green + one `Category=Scale` run before `/gsd-verify-work`.

### Wave 0 Gaps
- [ ] `ReelDiscovery.Tests/Corpus/AnswerKeyReaderTests.cs`: CSV/XLSX read-back variants, including a shared-string workbook built in-test
- [ ] `ReelDiscovery.Tests/Corpus/AnswerKeyRelabelTests.cs`: harvest/vote/apply/snapshot/backup lifecycle
- [ ] No new fakes. `FakeBatchTransport` already supports `Kind`, `CancelFailure`, `NeverEndIds` and `PollsUntilEnded`. Note that after a successful cancel it returns all items as Succeeded (so "partial" = all), which is acceptable.

## Security Domain

### Applicable ASVS Categories (Level 1)
| ASVS Category | Applies | Standard Control |
|---------------|---------|-----------------|
| V2 Authentication | no | — |
| V3 Session Management | no | — |
| V4 Access Control | no | — |
| V5 Input Validation | **yes**: user-edited CSV/XLSX and sidecar JSON are untrusted input | Accept only two canonical Relevance values; DocID must exist in the plan; cap file size read (e.g., 200 MB like the journal cap) and Rationale length (`AnswerKeyXlsxWriter.MaxCellLength` 32,767); tolerant JSON (`CaseDraft.JsonOptions`, catch `JsonException`) |
| V5 Output encoding | yes | User Rationale re-written via `AnswerKeyCsvWriter.Field` (OWASP CSV-injection guard, T-05-32) and as XLSX inline strings (never formulas); `Clean()` strips XML-invalid chars |
| V6 Cryptography | minimal | SHA-256 only as a change fingerprint (not security) |
| V12 Files | yes | Sidecar/backup paths = case folder + constant name (+ invariant timestamp); never a name read from data (T-04-68). Backup written atomically |
| V14 Config/secrets | yes | Other-provider key used only in transport constructors (T-07-54); never logged in Activity text (T-07-52 fixed text only) |

### Known Threat Patterns
| Pattern | STRIDE | Standard Mitigation |
|---------|--------|---------------------|
| CSV formula injection via user Rationale round-trip | Tampering | `Field()` guard on every CSV write |
| XML/zip bomb in a hand-edited xlsx | DoS | Size cap before parse; OpenXml SDK package limits; harvest failure → treat as damaged, never block the write (D-10) |
| Path traversal via sidecar content | Tampering | Sidecar never supplies paths; file names are constants |
| Silent double spend (provider switch) | Repudiation/financial | D-13/D-14 resolve-or-refuse; D-16/D-17 itemize spend |

## Sources

### Primary (HIGH confidence)
- Codebase (all file:line references above) — read directly this session.
- https://developers.openai.com/api/reference/cli/resources/batches/methods/cancel/index.md — cancel → `cancelling` up to 10 min → `cancelled` with partial results in output file.
- https://developers.openai.com/api/docs/guides/batch — expired batches: partial output, charged for completed requests.
- https://platform.claude.com/docs/en/build-with-claude/batch-processing — `canceling` → `ended` with partial results; `canceled` items not billed.
- https://learn.microsoft.com/en-us/dotnet/api/microsoft.visualbasic.fileio.textfieldparser — in-box through .NET 10/11.

### Secondary (MEDIUM confidence)
- https://ablebits.com/office-addins-blog/change-excel-csv-delimiter — Excel uses the Windows list separator; `;` in European locales; `sep=` line.
- https://www.ablebits.com/office-addins-blog/convert-excel-csv/ — CSV vs CSV UTF-8 save types.
- https://www.ericwhite.com/blog/?p=2822 and https://learn.microsoft.com/en-us/answers/questions/1279879/ — Excel writes shared strings.
- https://www.telerik.com/document-processing-libraries/documentation/knowledge-base/spreadprocessing-open-locked-files-read-only — FileShare.ReadWrite + copy-to-memory for Excel-open files.

### Tertiary (LOW confidence)
- https://docs.morphllm.com/api-reference/endpoint/batches-cancel.md — third-party batch API billing on cancel (not OpenAI; not relied on).

## Metadata

**Confidence breakdown:**
- Code seams / writer inventory: HIGH — read and grepped this session.
- Provider cancel semantics: HIGH (results availability), MEDIUM (OpenAI billing on cancel).
- Excel behavior: MEDIUM — reader designed to tolerate every variant.
- Harvest algorithm: MEDIUM-HIGH — refinement of D-03 justified by the stale-file case; needs planner acceptance.

**Research date:** 2026-10-12
**Valid until:** 2026-11-11 (provider docs move fast; re-check cancel wording if the transports change)
