# Phase 10: Answer-Key & Batch Safety - Context

**Gathered:** 2026-10-12
**Status:** Ready for planning
**Mode:** `--auto`. Every decision below is the recommended option from a code-grounded assumptions analysis. No user Q&A took place; see 10-DISCUSSION-LOG.md.

<domain>
## Phase Boundary

This phase makes sure manual answer-key relabels and batch spend are never silently lost or paid for twice. It is gap closure for W4, W5 and W8 in `.planning/v1.0-MILESTONE-AUDIT.md`, plus Phase 7 review findings WR-A06 and WR-A08. It hardens QA-01, KEY-03, RUN-03, RUN-04, PROV-01, PROV-07 and PLAT-05, which are already satisfied.

In scope:
1. Relabels made by hand in the answer-key spreadsheet survive Continue, Regenerate Thread, crash recovery, re-approval and Start Over. The user is warned before anything discards them.
2. Pending batches from a provider other than the current text provider are collected or cancelled before a run. They are never resubmitted and never paid twice. The banner counts only batches that can be resolved.
3. WR-A06: spend on collected but unused batch results is recorded at Start Over. WR-A08: live relevant waves do not pay again for items still pending in an earlier session's wave batch.
4. W8: the legacy `Attachment` / `AttachmentComplexity` types are deleted and guarded by the deleted-types test.

Out of scope: an in-app relabel UI (D-13 keeps relabeling manual), persisting `TextProvider`, same-provider model-change stranding, and a persistent case-level spend ledger.

</domain>

<decisions>
## Implementation Decisions

### Relabel capture and storage (W4)
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

### Relabel key and scope
- **D-04:** Relabels are keyed by **(PlanId, DocID)**. DocIDs change only on re-plan; Regenerate Thread and Start Over keep the PlanId.
- **D-05:** **What is preserved.**
  - The Relevance column: only "Relevant" or "Not Relevant", trimmed, case-insensitive, with a leading `'` CSV-guard stripped. Other values are ignored.
  - On relabeled rows only, the user's Rationale cell, kept verbatim when it differs from the projected rationale.
  - No family propagation: a relabel applies only to the DocID the user edited.
  - Edits to Hot, Privileged, Issue Codes and Fact IDs are **not** preserved. Tooltip and copy say "edit the Relevance column".

### Relabel lifecycle and warnings
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

### Re-approval resets the key (deferred from 09-RESEARCH Pitfall 9)
- **D-12:** This is in scope. `CorpusApproval.BuildFiles` re-applies relabels when it re-approves the **same** PlanId. When the journal is readable, it also applies the run-state overlay (`GenerationRunState.From(plan, GenerationJournal.Replay(...)).BuildFullOverlay`, as in ThreadInvalidation), so re-approval after Keep This Plan no longer resets statuses to Planned.
  - Approving a new PlanId writes the plain plan key.

### Cross-provider pending batches (W5)
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

### Batch spend fixes (WR-A06 / WR-A08)
- **D-16:** **WR-A06.** In `StartOverAsync`, after `CancelPendingBatchesAsync` and before `GenerationReset` deletes `provider-batches.jsonl` and `batch-results/`, the app prices unconsumed collected results with the same lookup as `PriceOrphans`. It records them on the session `UsageTracker` under the existing "Batch results not used" operation.
  - Billing orphans on Cancelled/Fatal is rejected (07-REVIEW-FIX), as is a new persistent spend field.
- **D-17:** **WR-A08.** Before the first relevant wave, stale "wave N" batches (from builds before amended D-07) go through these steps:
  1. They are cancelled at the provider but still tracked (not Closed).
  2. They are polled until they reach a terminal state, bounded by the existing `BatchTimings` drain bound.
  3. Partial results are collected and used free by live scopes. Leftovers become itemized orphans billed at Finished.
  4. If the bound expires, the run goes live and leaves the batch polling, and late results become billed orphans.

  This replaces the unitemized end-of-run `CloseStaleWaveBatchesAsync`. Live waves still never wait a full provider cycle (amended D-07).

### Legacy types (W8)
- **D-18:** Delete `class Attachment` and `enum AttachmentComplexity` from `Models/Attachment.cs`.
  - Keep `enum AttachmentType`, which is serialized in the plan and journal. Move it to `Models/AttachmentType.cs` in the same namespace.
  - Add `ReelDiscovery.Models.Attachment` and `ReelDiscovery.Models.AttachmentComplexity` as `InlineData` to `PlatformTests.LegacyType_IsDeleted`.

### Testing
- **D-19:** Use xUnit with the existing fakes and add no new test infrastructure.
  - `FakeBatchTransport`: use two instances, with Kind set to OpenAI and Anthropic.
  - Step tests use the `BatchTransportFactory`, `RunGatewayFactory` and `GatewayFactory` seams.
  - Relabel round-trips cover both CSV and XLSX (inline and shared-string cells) via `SpreadsheetDocument.Open`.
  - Run `Category=Scale` once, because batching changes (07-REVIEW-FIX precedent).

### Claude's Discretion
- How the harvest is implemented: the parser structure, and whether CSV and XLSX readers share a row model.
- The exact Activity log wording for cross-provider resolution, consistent with existing Activity phrasing.
- The exact timestamp format of the backup file name, as long as it is filesystem-safe and inside the case folder.

</decisions>

<canonical_refs>
## Canonical References

**Downstream agents MUST read these before planning or implementing.**

### Gap sources
- `.planning/v1.0-MILESTONE-AUDIT.md` (l.110-114): W4, W5 and W8 definitions
- `.planning/v1.0-INTEGRATION-CHECK.md` (l.93-122): W4/W5/W8 evidence with file and line references
- `.planning/phases/07-scale-cost-qa-cutover/07-REVIEW.md` (WR-A06 l.192, WR-A08 l.204)
- `.planning/phases/07-scale-cost-qa-cutover/07-REVIEW-FIX.md` (l.154-197): why WR-A06/A08 were skipped, and the rejected approaches
- `.planning/phases/09-edit-safety-navigation/09-RESEARCH.md` (Pitfall 9, l.290): re-approval resets the key

### Locked prior decisions
- `.planning/phases/07-scale-cost-qa-cutover/07-CONTEXT.md`: D-05, amended D-07 (live waves never wait), D-09, D-10, D-13 (relabeling is manual)
- `.planning/phases/04-*/04-CONTEXT.md`: answer key and approval decisions (KEY-03, D-26)
- `.planning/REQUIREMENTS.md`: QA-01, KEY-03, RUN-03, RUN-04, PROV-01, PROV-07, PLAT-05
- `.planning/ROADMAP.md`: Phase 10 success criteria

### Code
- `Services/Corpus/AnswerKeyBuilder.cs`, `AnswerKeyCsvWriter.cs`, `AnswerKeyXlsxWriter.cs`, `CorpusApproval.cs`
- `Services/Evidence/GenerationOutputs.cs`, `ThreadInvalidation.cs`, `GenerationReset.cs`, `GenerationRunner.cs` (l.255-266, 528-560, 1119-1160)
- `Services/Ai/BatchingLlmProvider.cs` (Recover l.176-240, PriceOrphans/MarkOrphansBilled l.270-385, CancelPendingAsync l.391-575, SendAsync l.620-700, poll l.1160-1200), `Services/Ai/BatchLedger.cs`
- `Services/Qa/QaReportStore.cs` (the sidecar pattern)
- `Models/WizardState.cs` (TextProvider not persisted l.21-44; CreateRunGateway l.93-118), `Models/Attachment.cs`
- `UserControls/StepGenerateCorpus.Run.cs` (run start l.440-450, Start Over l.921-1045, reopen l.1190-1210), `UserControls/StepGenerateCorpus.cs` (banner l.1100-1125, l.1309)
- `UserControls/GenerationUiText.cs` (BannerBody l.936, CG2/CG3 l.987-996), `UserControls/QaUiText.cs` (l.296, CQ1 l.335), `UserControls/CorpusUiText.cs` (l.215, ReplanBody l.315), `Forms/WizardForm.cs` (l.609)
- Tests: `ReelDiscovery.Tests/PlatformTests.cs` (l.86-111), `Fakes/FakeBatchTransport.cs`, `Ai/BatchingLlmProviderTests.cs` (l.866, 1484, 1544), `Evidence/AnswerKeyOverlayTests.cs`, `Evidence/GenerateCorpusStepTests.cs`

</canonical_refs>

<code_context>
## Existing Code Insights

### Reusable Assets
- `QaReportStore`: the sidecar pattern (case folder, constant name, tolerant read, atomic write).
- `BatchingLlmProvider.CancelPendingAsync(ledger, transportFor, planId, include)`: a static, filterable cancel.
- `Recover`: already loads collected results from other providers into `_available`, and the CR-A04 orphan billing prices them with that provider's model.
- `GenerationRunState.BuildFullOverlay`: the run-state overlay used by ThreadInvalidation.
- `FakeBatchTransport`: settable Kind, CancelFailure, NeverEndIds, PollsUntilEnded.

### Established Patterns
- Text and decision helpers are pure static `*UiText` methods. New copy is added as optional trailing parameters, so existing callers and tests keep compiling (Phase 9 precedent).
- Writes to the answer key happen off the UI thread from background checkpoints. A failure is reported, never thrown at the UI.
- Custom ids hash provider and model (`RequestIdentity.CustomIdFor`), so items cannot be mapped across providers.

### Integration Points
- Every answer-key writer (D-02), the run start in `StepGenerateCorpus.Run.cs`, `StartOverAsync`, `GenerationRunner` startup before the first relevant wave, and `CorpusApproval.BuildFiles`.

</code_context>

<specifics>
## Specific Ideas

- Copy strings are listed in D-06, D-07, D-08 and D-15. Keep them verbatim unless a UI-SPEC changes them.
- Needs external research: whether OpenAI and Anthropic still deliver results for items that finished before a batch was cancelled, and how long cancellation takes. Also, Excel's CSV re-save behavior: encoding, BOM, and `;` as the separator in non-US locales.

</specifics>

<deferred>
## Deferred Ideas

- Persisting `TextProvider` per case.
- A same-provider model change also strands that provider's batches, because custom ids hash the model.
- An in-app relabel UI.
- A persistent case-level spend ledger that outlives Start Over.
- Applying relabels to blind review and leakage audit inputs (see the D-11 trade-off).
- W7: planning traceability, plus the 11 REQ-IDs missing from the REQUIREMENTS.md traceability table. This is documentation work, not this phase.

</deferred>

---

*Phase: 10-answer-key-batch-safety*
*Context gathered: 2026-10-12 (--auto)*
