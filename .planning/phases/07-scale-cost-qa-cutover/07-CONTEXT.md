# Phase 7: Scale, Cost, QA & Cutover - Context

**Gathered:** 2026-10-05
**Status:** Ready for planning

<domain>
## Phase Boundary

Phase 6 delivers a complete mixed corpus (relevant mail, attachments, non-relevant mail, decoys). This phase makes that corpus **affordable at about 5,000 emails**, makes it **checkable**, and makes the case-driven flow the **only** flow:
- **Batch API execution** for OpenAI and Anthropic. Submitted batches are tracked on disk; results are collected and failed or expired items are re-run (RUN-03, RUN-04).
- **Prompt caching** of the shared case-guide context (RUN-05). Cost tracking covers cache writes, cache reads and batch discounts, with editable per-model pricing (PROV-07).
- A **~5,000-email run** that finishes with the expected file and answer-key counts and does not run out of memory (OUT-03 scale criterion, roadmap criterion 3).
- **QA passes:**
  - blind review (QA-01);
  - metadata-only leakage audit (QA-02);
  - timeline and value scans (QA-03).
- **Cutover:** the old topic/storyline wizard and its dead generation code are removed, and the app opens straight into the case-driven flow (OUT-03, PLAT-05).

Requirements: RUN-03, RUN-04, RUN-05, PROV-07, QA-01, QA-02, QA-03, OUT-03, PLAT-05. The four Phase 6 deferred items are folded in (D-23..D-26).

**Out of scope:**
- No change to how the plan allocates slots, apart from the privilege-incidence fix (D-23).
- No LLM judge that rewrites content automatically.
- No separate QA export format beyond the report described here.
- Ancillary discovery documents remain Phase 8.

</domain>

<decisions>
## Implementation Decisions

### Carried forward (locked in Phases 1-6)
- **D-01:** All LLM work goes through `ILlmGateway` with strict JSON-schema DTOs and must work with Claude or OpenAI. The batch and cache paths are added behind that seam, not beside it.
- **D-02:** **The plan owns structure.** The plan, journal and ledger semantics from Phases 4-6 are unchanged:
  - the ThreadDone commit boundary;
  - relevant waves running in order;
  - the ledger's as-of-wave views;
  - AddedKnowers being applied when the plan is saved.
- **D-03:** Names are kept exactly as the guide has them, including names from fictional works.
- **D-04:** Every new or changed screen is rendered at **144 DPI (150% scaling)** and checked for clipping, and the PNGs are looked at by eye. Fixed pixel sizes are not allowed (see the Phase 6 model-dialog bug).
- **D-05:** The answer key stays a projection of plan plus journal. Batch mode must produce the same journal records as live mode.
- **D-06:** Phase 6 D-08 holds: the Volume tier writes ordinary filler. The leakage audit (D-14) is how that risk gets measured.

### Batch execution
- **D-07:** When batch mode is on, **everything goes through the Batch API, wave by wave**:
  - each relevant wave is one batch, submitted only after the previous wave's results are committed, so ledger and knowledge order stay correct;
  - non-relevant mail and attachments follow in large batches.
- **D-08:** Batch mode is a **per-run checkbox on the Ready card** next to the estimate, for example "Use batch pricing (slower, about half the cost)":
  - the card shows both the live and the batch total;
  - the choice is saved with the case, so Continue reuses it;
  - the app **recommends batch only for very large corpora**: the checkbox is off by default and a hint suggests it above a size threshold (threshold is Claude's discretion; see below).
- **D-09:** **The run screen stays open and polls** while batches are pending. The Stages grid shows batch status, for example "Waiting on provider batch, submitted 2:14 PM, 3 of 5 done".
  - Batch jobs are still written to disk as they are submitted (RUN-04).
  - If the app closes or crashes, reopening the case collects the finished results on Continue. Re-submitting would pay twice.
  - There is no "close the app and come back" workflow beyond that recovery.
- **D-10:** Items that **fail, expire, or fail the code checks** after a batch are **re-run automatically as live calls at full price**, so a run never waits for a second batch cycle. Those retries count toward the cost ticker and RunEnded spend.

### QA passes
- **D-11:** **Blind review** classifies emails without seeing their labels. By default it reviews a **stratified sample**:
  - all relevant mail plus an equal number of non-relevant, decoys first;
  - the user can resize the sample, up to the whole corpus;
  - the cost estimate is shown before it runs.
- **D-12:** The reviewer is **a different provider than the one that wrote the corpus**: Claude reviews OpenAI output and the reverse. It falls back to the same provider when only one key is configured, and the report says so.
- **D-13:** Disagreements are a **report** with the reviewer's reason for each item.
  - Each flagged row offers **one manual action, "Regenerate thread"**, which reuses the existing regenerate/repair path. Nothing changes unless it is clicked.
  - Relabeling stays manual (the user edits the answer-key spreadsheet).
  - This is how QA-01's "can be relabeled or regenerated" is met.
- **D-14:** The **leakage audit** uses metadata only: sender, recipients, CC count, dates and times, subject length, attachment count and kinds, thread depth, privilege banner and subject tag.
  - It reports how well metadata predicts relevance against a 50% baseline, as **Pass / Warning / Fail** with the thresholds explained.
  - It lists the **top leaking signals**, for example "privilege banner: only on relevant mail".
  - It never changes the corpus; the user decides what to do.
- **D-15:** **Timeline and value scans** (references to facts dated later than the email, and contradictions of canonical values) are free code checks.
  - They **run automatically at the end of every run** and are **report only**: they never block export.
  - Results appear in a QA section on Case Summary with the flagged emails listed.
  - Flagged threads get the same "Regenerate thread" action as D-13.

### Cost display & pricing
- **D-16:** The cost ticker shows **one real total spent**. A **tooltip** (and the Case Summary) breaks it down into:
  - input;
  - cached input, as cache write and cache read;
  - output;
  - batch discount;
  - media.
- **D-17:** The Ready card shows the **live total and the batch total side by side**. Both include the expected cache effect.
- **D-18:** Cache and batch prices are edited per model as **extra columns in the existing model dialog** (`ModelConfigurationDialog`):
  - Cache write $ per 1M tokens;
  - Cache read $ per 1M tokens;
  - Batch discount %.
  
  Provider defaults are filled in and restored by Reset Defaults. The grid must still fit at 150% (D-04).

### Caching
- **D-19:** The shared case-guide context is sent as a stable prefix and cached where the provider supports it (Anthropic cache_control, OpenAI automatic prefix caching). Phase 5 already put the case-guide prefix first in prompts. Do not reorder prompts in a way that breaks the D-02 knowledge limits (each thread's prompt shows only what its senders know).

### Legacy cutover
- **D-20:** **Remove the whole legacy flow:**
  - the steps StepTopicInput, StepStorylines, StepCharacters, StepGenerationConfig, StepGeneration and StepComplete;
  - `EmailGenerator`, `StorylineGenerator`, `CharacterGenerator` and `ThemeGenerator`;
  - the `WizardFlow` switch and the `Legacy` enum value;
  - any other legacy-only models or services.
  
  Before deleting anything, move what the case flow still uses out of the legacy code: `HtmlEmailFormatter`, `BulkMailFormatter`, the Office/Calendar builders, and anything `OrganizationThemes` depends on. The build must stay warning-free and every test green.
- **D-21:** The app opens on **API setup, then Case Scenario (recent cases)**. This is today's case flow minus the flow choice.
- **D-22:** **Old settings files are ignored.** The legacy Base64 `reeldiscovery.settings` key file and old telemetry files are not migrated or deleted. Users re-enter keys if the current `ApiKeyStore` has none.

### Folded from Phase 6 (deferred-items.md)
- **D-23:** **Privilege incidence parity.** In small corpora (fewer than about 250 emails) only relevant mail ends up privileged, because `NonRelevantSkeletonBuilder.AssignDailyPrivilege` only guarantees one privileged daily-business thread from 50 upward. Fix it so non-relevant mail carries privilege at a comparable rate at every size. The leakage audit (D-14) should show the improvement.
- **D-24:** **Invite times across time zones.** Two invites for the same meeting sent from different time zones must get the same time. Today the times are derived per sender time zone in `InviteScheduler`.
- **D-25:** **Start Over delete retry.** `GenerationReset.TryDelete` retries briefly on a transient lock (Defender or the indexer), the way `CaseStore.WriteAtomic` does.
- **D-26:** **Theme font and color cleaning** on the business HTML path. `HtmlEmailFormatter.BuildEmailTemplate` gets the same cleaning `BulkMailFormatter` uses, without changing the output for valid themes. The Phase 5 golden HTML stays byte-identical.

### Claude's Discretion
- The corpus size above which batch mode is recommended (D-08), plus the hint wording and placement on the Ready card.
- Batch packaging: how many requests per batch file, and how a wave larger than the provider limit is split.
- Poll interval, and how batch status shows in the Stages grid.
- Leakage-audit method: which classifier (for example logistic regression or decision stumps over the D-14 features, in pure C#), cross-validation, and the exact Pass / Warning / Fail thresholds. Document them in the report.
- Blind-review prompt and DTO design, sample stratification details, and the cost-estimate math.
- How the 5K run stays within memory: streaming EML writes, releasing attachment bytes, journal and answer-key growth. Includes IN-06 (journal 200 MB cap) from the 06-REVIEW.
- How Phase 6 code-review findings that touch this phase's code are handled, if `/gsd-code-review-fix 6` has not already fixed them.

</decisions>

<canonical_refs>
## Canonical References

**Downstream agents MUST read these before planning or implementing.**

### Requirements & roadmap
- `.planning/ROADMAP.md` — Phase 7 goal and success criteria
- `.planning/REQUIREMENTS.md` — RUN-03, RUN-04, RUN-05, PROV-07, QA-01, QA-02, QA-03, OUT-03, PLAT-05
- `.planning/PROJECT.md` — core value, constraints (5K scale, Batch APIs, cost control)

### Research
- `.planning/research/PITFALLS.md` — label leakage, timeline drift, scale pitfalls
- `.planning/research/ARCHITECTURE.md` — provider abstraction and batch/caching notes
- `.planning/research/STACK.md` — provider SDK capabilities

### Prior phase outputs
- `.planning/phases/06-attachments-non-relevant-corpus/06-CONTEXT.md` — D-08 tiering, leakage acceptance
- `.planning/phases/06-attachments-non-relevant-corpus/06-REVIEW.md` — open code-review findings (CR-01, WR-01..05, IN-01..07)
- `.planning/phases/06-attachments-non-relevant-corpus/deferred-items.md` — the four folded items (D-23..D-26)
- `.planning/phases/06-attachments-non-relevant-corpus/06-VERIFICATION.md` — Phase 6 verification and the D-08 override
- `.planning/phases/05-relevant-evidence-generation-narrative-ledger/05-CONTEXT.md` — wave ordering, stable case-guide prompt prefix for caching

### Code
- `Services/Ai/ILlmGateway.cs`, `Services/Ai/LlmGateway.cs`, `Services/Ai/AnthropicTextProvider.cs`, `Services/Ai/OpenAITextProvider.cs` — where batch and caching plug in
- `Services/Evidence/GenerationRunner.cs`, `Services/Evidence/ThreadUnitOfWork.cs`, `Services/Evidence/GenerationJournal.cs` — run orchestration and journal (batch results must produce the same records)
- `Services/Corpus/PlanCallRunner.cs` — bounded parallel call pattern
- `Models/TokenUsageTracker.cs`, `Models/AIModelConfig.cs`, `UserControls/ModelConfigurationDialog.cs`, `Models/MediaPrices.cs` — cost tracking and pricing UI
- `Services/Corpus/GenerationCostEstimator.cs` — the single pricing path (PriceWork) the live and batch estimates must share
- `UserControls/StepGenerateCorpus.cs`, `UserControls/StepGenerateCorpus.Run.cs`, `UserControls/GenerationUiText.cs` — Ready card, Stages grid, run lifecycle
- `UserControls/StepCaseSummary.cs` — where the QA section goes
- `Forms/WizardForm.cs`, `Models/WizardFlow.cs` — flow switch to remove
- `Services/EmailGenerator.cs`, `Services/StorylineGenerator.cs`, `Services/CharacterGenerator.cs`, `Services/ThemeGenerator.cs`, `UserControls/Step{TopicInput,Storylines,Characters,GenerationConfig,Generation,Complete}.cs` — legacy code to remove
- `Services/ApiKeyStore.cs` — the current key store (the old Base64 file is ignored, D-22)

</canonical_refs>

<code_context>
## Existing Code Insights

### Reusable Assets
- `GenerationCostEstimator.PriceWork`: the single pricing function for Ready-card and remaining estimates. Extend it with cache and batch terms instead of adding a second path.
- `RenderValidator` (V3 canonical values, V4 knowledge, V6 ledger contradictions) and `CanonicalValueScanner`: the base for the QA-03 timeline and value scans, run post hoc over journal text.
- `AttachmentRepair` and the Retry Failed path: the base for "Regenerate thread" (D-13, D-15).
- `GenerationJournal` append and replay, with an `AsyncLocal` size cap for tests: batch submissions can be journaled the same way, so collection after a restart works.
- The `RenderHarness` 144-DPI render tests and `GenerationUiText`'s pure copy functions: use both for all new UI.
- `DistributionParityTests` fixtures (synthetic plan plus a real-planner plan): a ready-made corpus for testing the leakage audit.

### Established Patterns
- Never-throws units of work that journal their outcome. Catch `OperationCanceledException` before `Exception`.
- Strict DTOs registered in the `SchemaTypes` and `CaseSchemaTests` lists.
- Deterministic output: `StableRank` / SHA-256, never `Random` or `Guid` in anything written to an EML.
- Wave-ordered relevant generation with committed-only ledger merges.

### Integration Points
- `ILlmGateway`: batch submit, poll and collect, plus cache-control hints on requests.
- The Ready card: the batch checkbox and the live/batch totals.
- The Stages grid: batch waiting rows.
- Case Summary: the QA section (blind review, leakage, scans).
- `WizardForm.LoadSteps`/`BuildSteps`: remove the flow switch.

</code_context>

<specifics>
## Specific Ideas

- Leakage example to catch: "privilege banner appears only on relevant mail". This is the known small-corpus case (D-23), and the audit should surface it before the fix and pass after it.
- The blind reviewer using the other provider is meant to catch model-specific "tells" in how relevant mail is written.
- The user has been frustrated by slow progress. Keep QA passes optional and clearly priced, and keep the default path fast.

</specifics>

<deferred>
## Deferred Ideas

- Automatic regeneration of blind-review disagreements: rejected for now. The report plus a manual Regenerate action is enough (D-13).
- A "close the app and come back later" batch workflow with background collection: not wanted. The run screen stays open and polls; collection after a crash is recovery only (D-09).
- Migrating the old Base64 key and settings files: declined (D-22).
- An LLM semantic-consistency judge that rewrites content: still deferred. Blind review reports only.

</deferred>

---

*Phase: 07-scale-cost-qa-cutover*
*Context gathered: 2026-10-05*
