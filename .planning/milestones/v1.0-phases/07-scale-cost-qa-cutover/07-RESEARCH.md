# Phase 7: Scale, Cost, QA & Cutover - Research

**Researched:** 2026-10-06
**Domain:** Provider Batch APIs + prompt caching behind `ILlmGateway`, usage/cost accounting, corpus QA (blind review, leakage audit, timeline/value scans), 5K-scale run hardening, legacy wizard removal
**Confidence:** MEDIUM-HIGH (SDK surfaces verified by reflection against the exact package versions; provider limits and prices verified against current official docs; three provider behaviours still need a live spike, see Open Questions)

<user_constraints>
## User Constraints (from CONTEXT.md)

### Locked Decisions

#### Carried forward (locked in Phases 1-6)
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

#### Batch execution
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

#### QA passes
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

#### Cost display & pricing
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

#### Caching
- **D-19:** The shared case-guide context is sent as a stable prefix and cached where the provider supports it (Anthropic cache_control, OpenAI automatic prefix caching). Phase 5 already put the case-guide prefix first in prompts. Do not reorder prompts in a way that breaks the D-02 knowledge limits (each thread's prompt shows only what its senders know).

#### Legacy cutover
- **D-20:** **Remove the whole legacy flow:**
  - the steps StepTopicInput, StepStorylines, StepCharacters, StepGenerationConfig, StepGeneration and StepComplete;
  - `EmailGenerator`, `StorylineGenerator`, `CharacterGenerator` and `ThemeGenerator`;
  - the `WizardFlow` switch and the `Legacy` enum value;
  - any other legacy-only models or services.

  Before deleting anything, move what the case flow still uses out of the legacy code: `HtmlEmailFormatter`, `BulkMailFormatter`, the Office/Calendar builders, and anything `OrganizationThemes` depends on. The build must stay warning-free and every test green.
- **D-21:** The app opens on **API setup, then Case Scenario (recent cases)**. This is today's case flow minus the flow choice.
- **D-22:** **Old settings files are ignored.** The legacy Base64 `reeldiscovery.settings` key file and old telemetry files are not migrated or deleted. Users re-enter keys if the current `ApiKeyStore` has none.

#### Folded from Phase 6 (deferred-items.md)
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

### Deferred Ideas (OUT OF SCOPE)
- Automatic regeneration of blind-review disagreements: rejected for now. The report plus a manual Regenerate action is enough (D-13).
- A "close the app and come back later" batch workflow with background collection: not wanted. The run screen stays open and polls; collection after a crash is recovery only (D-09).
- Migrating the old Base64 key and settings files: declined (D-22).
- An LLM semantic-consistency judge that rewrites content: still deferred. Blind review reports only.
</user_constraints>

<phase_requirements>
## Phase Requirements

| ID | Description | Research Support |
|----|-------------|------------------|
| RUN-03 | Bulk generation can use the provider's Batch API (OpenAI and Anthropic) for lower cost | Pattern 1 (batching provider decorator), Pattern 2 (request identity), verified SDK surfaces for `client.Messages.Batches.*` (Anthropic 12.51.0) and `BatchClient`/`OpenAIFileClient` protocol methods (OpenAI 2.14.0) |
| RUN-04 | Submitted batch jobs are tracked on disk so results can be collected if the app is restarted | Pattern 3 (batch ledger file, append+flush like the journal, keyed by request hash), Pitfall 4 (resubmission double pay) |
| RUN-05 | Shared case-guide context uses prompt caching where the provider supports it | Pattern 5 + key finding: today's stable prefix is only ~300-330 tokens of system prompt plus schema; verified minimum cacheable lengths per model; block-level `cache_control` (never top-level automatic); OpenAI GPT-6 implicit cache-write premium |
| PROV-07 | Token usage and cost tracking accounts for cache writes, cache reads and batch discounts, with editable pricing per model | Pattern 4 (normalized `TokenUsage` + single `AIModelConfig.CalculateCost`), verified Anthropic/OpenAI usage field semantics (exclusive vs inclusive input tokens), verified default prices for every catalog model |
| QA-01 | Blind-review pass classifies a sample (or all) without labels; disagreements flagged and can be relabeled or regenerated | Pattern 7 (reviewer gateway on the other provider, review protocol as cached prefix, strict DTO), Pattern 9 ("Regenerate thread" is NEW work: no thread-regenerate path exists today) |
| QA-02 | Label-leakage audit checks metadata alone cannot predict relevance and reports the result | Pattern 8 (grouped-by-thread k-fold logistic regression + per-feature stumps in pure C#, answer-key rows as input), thresholds proposed |
| QA-03 | Timeline and value scans flag future-fact references or canonical-value contradictions | Pattern 10 (post-hoc rescan of journal text with existing `RenderValidator` V3/V4/V6 rules + `CanonicalValueScanner`) |
| OUT-03 | Case-driven flow replaces the old topic/storyline wizard flow (plus roadmap criterion 3: ~5K run completes with expected counts, no OOM) | Pattern 11 (cutover dependency map), Pattern 12 (5K scale: adaptive projection interval, in-process 5K fake run test) |
| PLAT-05 | Dead code in the old generation path is removed as it is replaced | Runtime State Inventory + cutover map: exact files, tests and shared helpers to split before deletion |
</phase_requirements>

## Summary

The codebase already has the right seam. Every LLM call goes `caller -> ILlmGateway.GenerateAsync<T> -> LlmGateway (retry/usage) -> ILlmProvider.SendAsync`. The least invasive way to satisfy D-01, D-05 and D-07 together is a **batching `ILlmProvider` decorator** that sits between `LlmGateway` and the real provider. It queues first-attempt requests, submits them as one provider batch when the callers go quiet, polls, and completes each awaiting `SendAsync` with the classified result. Units of work (`ThreadUnitOfWork`, `AttachmentUnitOfWork`, repairs) keep awaiting exactly as today. The journal records they write therefore stay identical (D-05), and the order "wave N batch, commit, merge ledger, wave N+1 batch" falls out of the existing runner (D-07). Retries after code checks and expired or errored items go straight to the live provider (D-10). The decorator needs only one new bit of request metadata: "may this attempt be batched".

Prompt caching has a much smaller effect than CONTEXT.md assumes, and the planner must know this. The "case-guide prefix" that Phase 5 put first is only the **byte-stable system prompt (about 1,360-1,470 characters, roughly 300-330 tokens) plus the JSON schema and a short name-rules block**. All the large per-thread context (people, gated facts, values, ledger) is knowledge-gated per thread, and D-19 forbids sharing it. Minimum cacheable prefixes are verified at 512 tokens for Opus 5.5 and Sonnet 5.5, 4,096 for Haiku 4.5 and 1,024 for GPT-6 models. So only the Anthropic Story tier can realistically cache, and the saving is a few percent of input. The prefix that does cache well is the **blind-review protocol prefix** (one protocol, thousands of calls). There is also a cost trap on OpenAI. GPT-5.6-and-later models default to *implicit* caching, which writes the whole prompt through the last user message at **1.25x input**. Unless Chat Completions honours explicit mode (to be checked in a live spike), OpenAI input costs are being under-reported today by up to 25%. The planner must normalize usage accounting first; caching tweaks come second.

QA-03 is mostly free: `RenderValidator` already implements V3 (canonical values), V4 (future and unknown knowledge) and V6 (ledger contradictions). Each ThreadDone line already journals its violations. QA-02 needs a small pure-C# grouped-CV logistic regression over answer-key rows. QA-01 needs a second gateway on the other provider. **"Regenerate thread" (D-13, D-15) does not exist today.** `AttachmentRepair` only regenerates attachments, and Retry Failed only reruns non-done threads. A new "thread invalidated" journal record plus late-render rules for relevant threads is real design work. The cutover is mostly mechanical, but `GenerationConfig` (image model and quality), `ThreadingHelper`, `FileNameHelper` and `ThemeGenerator.Sanitize*` are still used by the case flow and must be split out first. `Program.cs` still calls `MigrateLegacyKey()`, which contradicts D-22.

**Primary recommendation:** Build in this order:
1. Normalized usage plus cost (PROV-07).
2. The batching provider decorator with an on-disk batch ledger keyed by request hash (RUN-03, RUN-04).
3. Block-level caching with honest per-model eligibility (RUN-05).
4. The QA engines as pure, testable services.
5. The cutover as its own late wave, with "compiles warning-free and tests green" as the oracle.

## Architectural Responsibility Map

| Capability | Primary Tier | Secondary Tier | Rationale |
|------------|-------------|----------------|-----------|
| Batch submit/poll/collect | API / Backend (Services/Ai provider layer) | Storage (case folder batch ledger) | Provider-specific wire formats belong only in `Services/Ai` (ARCHITECTURE.md rule) |
| Batch routing decision (batch vs live) | API / Backend (gateway/provider decorator) | — | D-01: behind the seam; units must not know |
| Batch job tracking across restarts | Database / Storage (append-only jsonl in case folder) | API / Backend | Same durability model as `generation-journal.jsonl` |
| Prompt cache markers | API / Backend (provider request builders) | — | Only providers know `cache_control` / `prompt_cache_options` |
| Usage normalization and cost | API / Backend (`LlmGateway` + `AIModelConfig`) | Browser/Client (WinForms ticker tooltip) | One calculation, many displays |
| Live vs batch estimate | API / Backend (`GenerationCostEstimator.PriceWork`) | Client (Ready card) | D-17 single pricing path |
| Blind review, leakage audit, scans | API / Backend (new `Services/Qa/*`) | Storage (QA report json in case folder), Client (Case Summary QA section) | Pure services, UI only renders |
| Regenerate thread | Storage (journal record) + Backend (runner treats as pending) | Client (button navigates to Generate step) | D-05: the key stays a journal projection |
| Legacy removal | Client (wizard shell) + Backend (dead services) | — | Compile-driven deletion |

## Project Constraints (from CLAUDE.md)

- C# / WinForms on **net10.0-windows** (the csproj and `global.json` say net10.0 / SDK 10.0.401; CLAUDE.md's ".NET 8" text is stale). Single-file self-contained win-x64 publish.
- Reuse MimeKit and DocumentFormat.OpenXml builders. Do not add new heavy dependencies.
- No `record` types, no primary constructors, no `#region`. Mutable classes with auto-properties. `_camelCase` fields, `ct` last parameter, `Async` suffix.
- `[GeneratedRegex]` instead of `new Regex`. Use `Random.Shared` in new code. In this project, prefer `StableRank` / SHA-256 for anything that affects output (determinism rule from Phase 5/6).
- Catch `OperationCanceledException` before `Exception`. Units of work never throw. Bare `catch` only for non-critical side effects, with a comment.
- UI is built in code. Use `TableLayoutPanel` with row-index comments, `ButtonHelper.CreateButton`, Segoe UI. Verify at 150% DPI and never use fixed pixel sizes (memory note + D-04).
- Strict DTOs are registered in `SchemaTypes` lists and covered by `CaseSchemaTests`.
- Build warning-free. No analyzers, so this is enforced by hand.
- GSD workflow: edits only through GSD commands.
- Never substitute names from fictional works (memory note; D-03).

## Standard Stack

### Core (already referenced; no new packages)

| Library | Version | Purpose | Why Standard |
|---------|---------|---------|--------------|
| Anthropic (official C# SDK) | 12.51.0 | `client.Messages.Batches.Create/Retrieve/ResultsStreaming/Cancel`; `TextBlockParam.CacheControl`; `Usage.CacheCreationInputTokens/CacheReadInputTokens` | Already the Claude provider; batch types mirror `MessageCreateParams` [VERIFIED: reflection on nuget cache 12.51.0] |
| OpenAI (official .NET SDK) | 2.14.0 | `OpenAIFileClient.UploadFileAsync(..., FileUploadPurpose.Batch)`, `DownloadFileAsync`, `BatchClient.CreateBatchAsync(BinaryContent, waitUntilCompleted:false)`, `GetBatchAsync(id)` (protocol JSON), `ChatTokenUsage.InputTokenDetails.CachedTokenCount`, `ModelReaderWriter.Read<ChatCompletion>` | Already the OpenAI provider. `BatchClient`/`BatchJob`/`CreateBatchOperation` carry `[Experimental("OPENAI001")]` [VERIFIED: reflection] |
| System.Text.Json | in-box (.NET 10) | JSONL request lines (`Utf8JsonWriter`), result parsing, ledger records | Existing convention |
| xUnit | 2.9.3 | Tests | Existing test project `ReelDiscovery.Tests` |

### Supporting

| Library | Version | Purpose | When to Use |
|---------|---------|---------|-------------|
| `System.ClientModel` (transitive) | via OpenAI 2.14.0 | `BinaryContent`, `ClientResult.GetRawResponse()`, `ModelReaderWriter` | OpenAI batch protocol calls, raw usage JSON (`cache_write_tokens`) |
| DocumentFormat.OpenXml | 3.5.1 | QA report sheet if one is added (not required) | Only if the QA report gets an xlsx (out of scope per CONTEXT) |

### Alternatives Considered

| Instead of | Could Use | Tradeoff |
|------------|-----------|----------|
| Provider decorator (transparent batching) | Separate `IBatchGateway` + "prerendered" path into units (ARCHITECTURE.md Pattern 6) | The ARCHITECTURE design needs every unit (thread, attachment, chained attachment, repair) to accept prerendered results and duplicates the retry/validation flow. That risks D-05 parity. The decorator keeps one code path. |
| ML.NET for leakage audit | Pure C# logistic regression | ML.NET adds a large native dependency to a single-file publish. About 150 lines of C# are enough for <=10K rows x <100 features. |
| Hand JSON for Anthropic batch | SDK `Request{CustomID, Params}` | The SDK types exist and mirror live params. Use them. |
| OpenAI Responses API batch | `/v1/chat/completions` batch lines | The app is built on Chat Completions (classification, wire tests). Switching endpoint mid-milestone is out of scope. |

**Installation:** none. No new packages.

**Version verification:** csproj pins Anthropic 12.51.0 and OpenAI 2.14.0 (both present in `~/.nuget/packages`). The APIs listed above were checked by reflection against those exact DLLs on 2026-10-06. Do not upgrade packages in this phase.

## Architecture Patterns

### System Architecture Diagram

```
 Runner / units of work (unchanged control flow)
   ThreadUnitOfWork ─┐  AttachmentUnitOfWork ─┐  AttachmentRepair ─┐  BlindReviewer ─┐
                     ▼                        ▼                    ▼                 ▼
               ILlmGateway.GenerateAsync<T>(LlmRequest{ AllowBatch, CachePrefix... })
                     │  (retry policy, JSON parse, usage → TokenUsage → CalculateCost)
                     ▼
               ILlmProvider.SendAsync(LlmProviderRequest{ AllowBatch: attempt==1 && request.AllowBatch })
                     │
        ┌────────────┴─────────────────────────┐
        │ BatchingLlmProvider (batch mode only)│
        │  AllowBatch=false ─────────────────────────────────────────► inner live provider
        │  AllowBatch=true:                     │
        │   hash = SHA256(canonical request)    │
        │   ledger has hash? ── yes ─► await that batch's result      │
        │        │ no                           │
        │   enqueue → quiet-period / size flush │
        │        ▼                              │
        │   IBatchTransport.Submit ─► append "Submitted" to batch ledger (flush) ─► provider
        │   poll loop (30-60 s) ─► status to GenerationSnapshot (Stages grid)
        │   ended ─► download/stream results ─► save results file ─► complete awaiting tasks
        │      errored/expired/canceled item ─► inner live provider (D-10)
        └───────────────────────────────────────┘
                     │
                     ▼
  LlmProviderResponse{ Text, Outcome, Usage{Input, CacheWrite, CacheRead, Output}, Route=Batch|Live }
                     ▼
  back up the same stack ─► unit validates (V1..V8) ─► retry with feedback = LIVE ─► ThreadDone journal line (same shape)
```

### Recommended Project Structure (new/changed only)

```
Services/Ai/
├── TokenUsage.cs                 # normalized usage (input, cacheWrite, cacheRead, output, IsBatch)
├── BatchingLlmProvider.cs        # ILlmProvider decorator: queue, flush, await, live fallback
├── IBatchTransport.cs            # Submit / Poll / Results / Cancel (provider-neutral)
├── AnthropicBatchTransport.cs    # client.Messages.Batches.*
├── OpenAIBatchTransport.cs       # Files upload + BatchClient protocol + output file download
├── BatchLedger.cs                # case-folder provider-batches.jsonl (append+flush, replay)
└── RequestIdentity.cs            # canonical request bytes → SHA-256 → custom_id
Services/Qa/
├── BlindReviewer.cs, BlindReviewPrompts.cs (+ DTO registered in SchemaTypes)
├── LeakageAudit.cs, LogisticRegression.cs (pure C#)
├── CorpusScans.cs                # QA-03 post-hoc V3/V4/V6 over journal text
└── QaReportStore.cs              # qa-report.json in case folder
Services/Evidence/ThreadInvalidation.cs   # "Regenerate thread" journal record + EML removal
```

### Pattern 1: Transparent batching as an `ILlmProvider` decorator

**What:** `BatchingLlmProvider : ILlmProvider` wraps the real provider. `SendAsync` with `AllowBatch == false` passes straight through to live. With `AllowBatch == true` it enqueues a `TaskCompletionSource<LlmProviderResponse>` and returns its task.

**Flush policy (discretion, recommended):**
- Flush when no new request has arrived for a **quiet period of 3 s**, or the queue reaches the packaging cap.
- Packaging cap: Anthropic 10,000 requests or about 200 MB; OpenAI 10,000 requests or about 150 MB, and also under about 2M estimated input tokens so Tier-1 accounts (gpt-6-luna batch queue limit 5,000,000 tokens) never fail on `token_limit_exceeded`.
- Units of one wave issue their first call within milliseconds of each other, so one wave becomes one batch (D-07).
- Attachment calls issued after the thread results arrive form the next batch.
- Chained attachments that wait on `ChainScheduler` simply land in a later flush.

**Parallelism:** In batch mode the runner must let every unit of a wave or chunk start, because queued requests cost nothing. Today `GenerationRunner.MaxParallelThreads = 3` would put only 3 requests in each batch. Pass a large `maxParallel` (wave size, or `FillerChunkSize` = 50, raised to something like 500 for Stage B) when batch mode is on, and keep `MaxConcurrentRepairs` bounded for memory.

**Gateway change:** `LlmGateway` sets `providerRequest.AllowBatch = request.AllowBatch && attempts == 1`. Its invalid-JSON and truncation retries are then live automatically, which is D-10's "fails code checks → live". The unit-level feedback retry (`ThreadUnitOfWork` attempt 2-3) creates a new `LlmRequest` with `AllowBatch = false`.

**Example:**
```csharp
// Source: pattern derived from Services/Ai/LlmGateway.cs + ILlmProvider contract (this repo)
public async Task<LlmProviderResponse> SendAsync(LlmProviderRequest request, CancellationToken ct = default)
{
    if (!request.AllowBatch)
        return await _live.SendAsync(request, ct);

    var identity = RequestIdentity.For(_live.Kind, request);           // SHA-256 of canonical body
    var pending = _ledger.TryAttach(identity) ?? Enqueue(identity, request);  // restart: reuse submitted item
    var result = await pending.Task.WaitAsync(ct);                      // never re-submit (D-09)
    return result.Outcome == BatchItemOutcome.Succeeded
        ? result.Response!                                              // classified with the SAME Classify() as live
        : await _live.SendAsync(request, ct);                           // errored / expired / canceled -> live (D-10)
}
```

**Classification parity:**
- Anthropic: `MessageBatchSucceededResult.Message` is the same `Anthropic.Models.Messages.Message` type, so call the existing `AnthropicTextProvider.Classify(message)`.
- OpenAI: each output line's `response.body` is a chat completion JSON, so `ModelReaderWriter.Read<ChatCompletion>(BinaryData)` (ChatCompletion implements `IJsonModel`) followed by the existing `OpenAITextProvider.Classify(...)`.
- One classifier means identical outcomes and identical journal records (D-05).

### Pattern 2: Request identity and `custom_id`

- Anthropic `custom_id` must match `^[a-zA-Z0-9_-]{1,64}$` [CITED: platform.claude.com batch-processing]. DocIds contain dots, so never use them directly.
- Use `custom_id = "q" + hex(SHA256(canonical request bytes))[..40]`. The canonical bytes are model, effort, schema name and JSON, system, user and max tokens, joined with a separator.
- Identical requests share one item; deduplicate them inside a flush.
- Because the prompts are byte-deterministic given plan plus journal (Phase 5 rule), a Continue after a crash regenerates the **same hash**. That hash is how RUN-04 recovery finds the already-submitted item instead of paying twice.

### Pattern 3: Batch ledger on disk (RUN-04)

- Separate append-only file `provider-batches.jsonl` in the case folder, with a constant file name only (T-04-26 style path rule).
- Use the same "serialize, lock, append one line, `Flush(true)`" technique as `GenerationJournal.AppendAsync`.
- Do **not** put batch records in `generation-journal.jsonl`. Keeping them out means the journal and answer-key projection are untouched (D-05), and the 200 MB journal cap (IN-06) is not consumed.
- Record types:
  - `Submitted {Provider, BatchId, PlanId, SubmittedUtc, InputFileId?, Items:[{CustomId, Hash, Operation, ThreadId?, Tier}]}`
  - `Status {BatchId, Status, Counts}` (optional)
  - `Collected {BatchId, ResultsFile, OrphanInput/Output/Cost}`
  - `Cancelled {BatchId}`
- Append `Submitted` **before** returning from the create call path. If the create call succeeds and the append fails, cancel the provider batch immediately (best effort).
- Save the downloaded or streamed results to `batch-results/{sanitizedBatchId}.jsonl` with an atomic write. Results stay available 29 days (Anthropic) or 30 days (OpenAI output file), but keeping a local copy makes re-collection offline and deterministic for tests.
- Items whose results arrive but are never consumed are still billed. Typical causes are a thread already done, a plan changed, or a hash that no longer occurs. Sum them as "orphan spend" and add them to RunEnded through the existing unjournaled-spend path, so the ticker stays honest.
- `GenerationReset` (Start Over) must:
  - cancel in-progress provider batches (best effort);
  - delete `provider-batches.jsonl` and `batch-results/`;
  - delete OpenAI input/output files (best effort).

### Pattern 4: Normalized usage and one cost function (PROV-07)

Provider semantics differ, and this is verified:

- **Anthropic:** `usage.input_tokens` is only the tokens **after the last cache breakpoint**. Total input = `cache_read_input_tokens + cache_creation_input_tokens + input_tokens` [CITED: platform.claude.com prompt-caching]. The current code records only `InputTokens`, so with caching on, cached tokens would vanish from cost.
- **OpenAI:** `prompt_tokens` is **inclusive**. `ordinary = input - cached - cache_write` [CITED: developers.openai.com prompt-caching]. SDK 2.14.0 exposes `InputTokenDetails.CachedTokenCount` but **not** `cache_write_tokens`. Read it from the raw response JSON (`ClientResult.GetRawResponse().Content`, and the batch output line body).

```csharp
public class TokenUsage   // mutable class per CLAUDE.md (no records)
{
    public int InputTokens { get; set; }      // uncached input only (normalized, exclusive)
    public int CacheWriteTokens { get; set; }
    public int CacheReadTokens { get; set; }
    public int OutputTokens { get; set; }
    public bool IsBatch { get; set; }
}
// AIModelConfig.CalculateCost(TokenUsage u):
//   raw = u.Input*In + u.CacheWrite*CacheWrite + u.CacheRead*CacheRead + u.Output*Out  (per 1M)
//   return u.IsBatch ? raw * (1 - BatchDiscountPercent/100) : raw;
//   BatchSavings = raw - cost  (for the D-16 tooltip)
```

- Today four runtime sites recompute cost from tokens: `ThreadUnitOfWork.Cost`, `AttachmentUnitOfWork` line ~600, `GenerationRunner.ModelCost`, and `TokenUsageTracker.RecordUsage`.
- They must all take `CostUsd` from the gateway result instead. Add `Usage` and `CostUsd` to `LlmResult` and `PlanCallOutcome<T>`.
- Journal lines keep `InputTokens`/`OutputTokens`/`CostUsd`. Add **optional** `CacheWriteTokens`, `CacheReadTokens` and `BatchSavingsUsd` properties so Case Summary can show the D-16 breakdown across sessions. Adding properties is backward compatible, because System.Text.Json ignores unknown members by default and `CaseDraft.JsonOptions` does not set `UnmappedMemberHandling`.

**Verified default prices** (USD per 1M tokens; batch = 50% of every line, cache lines included):

| Model (catalog id) | Input | Cache write (5m) | Cache read | Output | Batch discount | Min cacheable prefix |
|---|---|---|---|---|---|---|
| claude-opus-5-5 | 4.00 | 5.00 | 0.20 (0.05x) | 20.00 | 50% | 512 |
| claude-sonnet-5-5 | 2.00 | 2.50 | 0.20 | 10.00 | 50% | 512 |
| claude-haiku-4-5-20251001 | 1.00 | 1.25 | 0.10 | 5.00 | 50% | 4,096 |
| gpt-6.1-sol | 2.00 | 2.50 | 0.10 (0.05x) | 10.00 | 50% | 1,024 |
| gpt-6-sol | 2.00 | 2.50 | 0.20 | 10.00 | 50% | 1,024 |
| gpt-6-luna | 0.10 | 0.125 | 0.01 | 0.50 | 50% | 1,024 |

[VERIFIED: platform.claude.com/docs/en/about-claude/pricing.md, developers.openai.com/api/docs/pricing.md and model pages, fetched 2026-10-06]

- Keep the minimum cacheable prefix as a **hidden** catalog field (not a grid column). That keeps D-18's grid at +3 columns.
- Pricing is **not persisted** across app sessions today: `AvailableModelConfigs` is in-memory defaults. Keep it session-only unless the user asks (see Open Questions).

### Pattern 5: Prompt caching that actually pays (RUN-05)

**Anthropic:**
- Put `CacheControl = new CacheControlEphemeral()` on the **system `TextBlockParam`**, which is block-level and 5 min TTL.
- **Never use top-level `MessageCreateParams.CacheControl` (automatic caching).** It moves the breakpoint to the last cacheable block, the unique per-thread user prompt. Every call would then write about 4,000 tokens at 1.25x that are never read [CITED: prompt-caching doc, "Top-level cache control automatically applies a cache_control marker to the last cacheable block"].
- Prefixes below the model minimum are "processed without caching, and no error is returned" [CITED]. So marking the system block is safe on Haiku 4.5. It costs nothing and gives nothing.
- The structured-output format is part of the cache hierarchy: a format change invalidates system and message caches [CITED]. One DTO type gives one cache line, which is fine.

**OpenAI (GPT-5.6 and later, so every catalog GPT-6 model):**
- Implicit mode is the **default**. It "places a breakpoint at the end of the latest eligible message", which is our unique user message, and cache writes cost **1.25x** [CITED: developers.openai.com prompt-caching, "GPT-5.6 and later"].
- Explicit-only mode with no breakpoints means "the request does not use prompt caching or create cache writes" [CITED].
- The documentation shows these controls (`prompt_cache_options`, `prompt_cache_breakpoint`) only for the Responses API. Chat Completions support is **unverified**. See Open Question 1: a live spike must read `usage.prompt_tokens_details.cache_write_tokens` on a Chat Completions call.
- If writes appear and Chat Completions accepts `prompt_cache_options: {"mode":"explicit"}`, send it through `ChatCompletionOptions.Patch` (the SDK has no typed property). Add a breakpoint only on prefixes of at least 1,024 tokens.

**Where caching truly helps in this phase:** the blind-review prompt.
- The review protocol (issues and relevance criteria) is identical for every reviewed document and is easily over 1,024 tokens, so put it in the system or first block with the breakpoint.
- Thousands of calls then read it at 0.05-0.1x.

**What NOT to do to "make caching work":** do not move the whole cast or fact list into a shared prefix. That breaks D-19 (knowledge limits). On Haiku (4,096 minimum) it also raises cost, because you pay the full prefix on every call without ever reaching the minimum. A "case directory" prefix (organizations plus full cast voice lines, no facts) is the only D-19-safe candidate. Treat it as an optional, measured experiment, not a default.

**Batch plus cache:** Anthropic stacks the discounts, with cache hits "best-effort" in batches [CITED]. OpenAI's batch price table lists cached and cache-write rates at 50% [VERIFIED: pricing.md batch table]. For estimates, assume **no** cache reads in batch mode (conservative).

### Pattern 6: Batch status in the UI (D-09)

- The decorator reports per batch `{SubmittedLocal, Done, Total, Status}` to `GenerationSnapshot`. Add a `BatchWait` field per stage row. `GenerationUiText` renders "Waiting on provider batch, submitted 2:14 PM, 3 of 5 done" as a pure function (testable, render-tested).
- Poll every **30 s**, backing off to 60 s after 10 minutes [discretion]. Anthropic `processing_status`: `in_progress | canceling | ended`. OpenAI: `validating | failed | in_progress | finalizing | completed | expired | cancelling | cancelled` [CITED].
- **Cancel** stops waiting but does **not** cancel the provider batch. The ledger keeps it, so Continue collects the results without paying twice. **Start Over** cancels.

### Pattern 7: Blind review (QA-01)

- **Reviewer gateway:** add `WizardState.CreateReviewerGateway()`. It picks the provider ≠ `TextProvider` when that key is tested (D-12) and otherwise falls back to the same provider with a `SameProviderFallback` flag in the report. Default to that provider's **Volume** model (cheap). The tier choice is discretion.
- **Prompt:**
  - System (cached prefix): fixed reviewer instructions plus the review protocol built in code from the guide (`ReviewProtocolBuilder` structure: issues, relevance criteria, privilege rule). No labels, no plan data, no counts.
  - User: one document as a reviewer sees it: From/To/Cc/Date, the rendered subject (overlay, including the privilege tag), the body, and attachment titles plus the first N characters of flattened text. All of this comes from journal text, not EML parsing.
- **DTO (strict):** `{ relevant: bool, issueCodes: string[], reason: string }`, with the reason capped at about 40 words. Register it in `SchemaTypes`.
- **Sample:** all relevant emails, plus an equal number of non-relevant ones taken decoys first, then the rest in `StableRank` order (deterministic). The user can resize up to the whole corpus.
- **Estimate:** `N × [P_protocol × cacheRead + avgDocTokens × input + 150 × output]` on the reviewer model, plus one cache write.
- Run with bounded parallelism through `PlanCallRunner`. Append each verdict to `qa-blind-review.jsonl` so cancel and resume work.
- **Disagreement** is key ≠ reviewer on relevance. Issue-code mismatch is informational only.

### Pattern 8: Leakage audit (QA-02), pure C#

- **Unit:** each email row (attachments excluded; they inherit family) from `AnswerKeyBuilder` rows plus the plan.
- **Features (D-14):**
  - sender (one-hot, top-k with an "other" bucket);
  - recipient count, CC count;
  - weekday, hour bucket, and month offset in the corpus range;
  - subject length;
  - attachment count and one flag per kind;
  - thread depth / position;
  - privilege banner flag and subject-tag flag (from plan/overlay).
- **Model:** L2-regularized logistic regression (batch gradient descent or Newton, standardized numerics). Use **5-fold cross-validation grouped by ThreadId**. Messages of one thread share label and features, so ungrouped CV leaks and inflates the score. Assign folds by `StableRank(threadId) % 5` (deterministic, no Random).
- **Metric:** balanced accuracy, which is directly comparable to the 50% baseline D-14 names. Also report AUC.
- **Proposed thresholds [ASSUMED, discretion]:** Pass ≤ 60%, Warning 60-70%, Fail > 70% balanced accuracy. The report should note that date and sender predictiveness partly reflects real corpora (custodians, dispute window).
- **Top signals:** for each binary or categorical feature value with support ≥ 5, compute P(relevant | value) against the base rate. Emit "only on relevant mail" when P = 100% and "never on relevant mail" when P = 0%. Rank by |lift| × support.
- Test it with `DistributionParityTests` fixtures. On a 250-email real-planner plan **before** D-23 it must report "privilege banner: only on relevant mail"; after the fix that signal must disappear.

### Pattern 9: "Regenerate thread" is new work (D-13/D-15)

- No thread-regenerate path exists. `AttachmentRepair` regenerates attachments only, and Retry Failed reruns only non-done threads [VERIFIED: grep]. Recommended design:
  1. A new `JournalRecordType.ThreadInvalidated`, **appended at the end of the enum** (the enum is append-only; older builds skip unknown lines). Replay removes the thread from `LastByThread` and its attachments, so `GenerationRunState` counts it pending. Its spend stays counted.
  2. Before appending, remove its EML copies with the existing `MailboxWriter.DeleteCopies`, and record any copies that could not be removed.
  3. The user lands on Generate Corpus and clicks Continue. The runner re-renders it like any pending thread.
  4. **Relevant threads after later waves exist:** render with the same as-of-wave ledger view, plus the thread's previous `LedgerDelta` passed as "already established by this thread" (reuse the `LaterEstablished`/`LateRepair` mechanism). New deltas stay out of the ledger, so later waves are never contradicted (D-02). See Open Question 3.

### Pattern 10: Timeline and value scans (QA-03)

- At run end, after the final checkpoint, replay the journal and rebuild each thread's `ThreadRenderContext` (the same code `GenerationRunner` uses). Then:
  - re-run `RenderValidator` V3 (canonical values), V4 (future-dated or unknown knowledge) and V6 (ledger contradictions) on the journaled bodies **with the final ledger as-of-wave views**;
  - run `CheckAttachment` on attachment `FlattenedText`;
  - run a filler-specific check: any `CanonicalValueScanner` hit or registered canonical value in **non-relevant** text is flagged, because filler prompts never print values (T-06-46).
- Merge in the journaled `Violations` of `Warnings` threads (already recorded).
- Write `qa-scans.json` and show it in a Case Summary QA section. It is report only and never blocks export.

### Pattern 11: Cutover dependency map (OUT-03, PLAT-05)

**Delete:**
- `UserControls/Step{TopicInput,Storylines,Characters,GenerationConfig,Generation,Complete}.cs`
- `Services/{EmailGenerator,StorylineGenerator,CharacterGenerator,ThemeGenerator,EmlFileService}.cs`
- `Models/{WizardFlow,Storyline,Character,EmailThread,EmailMessage,GenerationResult,PresentationTheme(legacy subclass)}.cs`
- `Helpers/DateHelper.cs` (Storyline-only)
- Legacy fields in `WizardState` (`StorylineCount`, `Storylines`, `Characters`, `DomainThemes`, `GeneratedThreads`, `Result`, `Flow`)

**Move or split first (used by the case flow):**
- `ThemeGenerator.SanitizeHexColor/SanitizeFont/AllowedFonts`: used by `BulkMailFormatter`, `HtmlEmailFormatter`, `OrganizationThemes` and `PeopleDirectoryTests`. Move them to a new static `Helpers/ThemeSanitizer.cs`. This is also the D-26 hook.
- `ThreadingHelper.AddReplyPrefix/AddForwardPrefix/QuoteText`: used by `EmailComposer` and `AnswerKeyBuilder`. Keep these and delete the `EmailMessage`/`EmailThread` members.
- `FileNameHelper.SanitizeForFileName`: used by `RelevantThreadFiller`. Keep it and drop the `EmailMessage` overloads.
- `GenerationConfig.ImageModel/ImageQuality`: used by `WizardState.CreateMediaClient/CreateMediaGenerator`. Move to `WizardState.MediaSettings` (or `MediaPrices`' sibling) and delete the rest of `GenerationConfig`.
- `OrganizationTheme` (in `Models/PresentationTheme.cs`): used everywhere in the case flow. Keep `OrganizationTheme` and delete only the empty `PresentationTheme` subclass.

**Shell and startup:**
- Remove the `_rbLegacy`/`_rbCaseDriven` workflow radios and their panel from `StepApiConfiguration` (re-render at 144 DPI).
- In `WizardForm`, `StepTypesFor` returns one list. Remove `_builtFlow` and the `DecideClose(flow, ...)` flow parameter.
- **`Program.cs` still calls `new ApiKeyStore().MigrateLegacyKey()`, which imports and then deletes the legacy file. That contradicts D-22.** Remove the call, `MigrateLegacyKey` itself, `LegacySettingsPath`, and their tests in `ApiKeyStoreTests`.

**Tests to delete or rewrite:**
- Delete `ReelDiscovery.Tests/Generation/*` (EmailGenerator*, LegacyGeneratorPortTests, EmailDistributionTests).
- `Concurrency/ConcurrencyTests.cs`: keep only the parts that do not use EmailGenerator.
- `RunSummaryTests.cs` (StepComplete) and `Case/WizardFlowTests.cs`: rewrite as a single-flow test.
- `Case/CaseDraftTests.cs` (WizardFlow refs).
- `Documents/DocumentRegressionTests.cs`: uses EmailThread/EmailMessage. Port it to OfficeDocumentService inputs.
- `Render/WizardShellRenderTests`: re-baseline.

The compile is the oracle: delete in small commits and build after each.

### Pattern 12: 5K scale (roadmap criterion 3)

**Biggest hidden cost:** `MaybeProjectAsync` re-projects every 2 s. Each projection does a full journal replay, then `GenerationRunState.From`, `LedgerMerger.Build`, writes the answer key CSV and **XLSX**, and persists `case.json` on the UI thread. At 5K emails (about 20-50 MB journal, about 10K key rows) each projection is seconds of CPU and GC, so the run spends much of its time re-projecting.

**Fix (discretion):** use an adaptive interval: `max(2 s, 10 × last projection duration)`, capped at 60 s. Wave and run end still always project.

- Journal size at 5K is about 5K × (~1.5 KB body + nested attachment text up to 20K characters for about 20%), roughly 20-40 MB. That is well under the 180 MB stop line (IN-06 already fixed). Batch mode adds nothing to the journal (separate ledger).
- Attachment bytes are per-thread and written immediately (`MailboxWriter`). Keep `MaxConcurrentRepairs` bounded even when batch mode raises unit parallelism. Many units holding composed attachment bytes while waiting on a batch is the memory risk: hold batch-wait **before** any bytes exist. Units only build bytes after the text result arrives, which is already the case.
- `AnswerKeyXlsxWriter` builds rows with the DOM `Row` API into a `MemoryStream`. That is acceptable at 10K rows. Switch to `OpenXmlWriter` only if the 5K test shows peaks.
- **Verification:**
  - an in-process 5K run test (Category `Scale`): `CorpusPlanner` at 5000 with the fake responder (already exists in `CorpusPlannerTests`), then `GenerationRunner` with `EvidenceTestData.RenderResponder` plus `AttachmentResponder` and `FakeMediaGenerator`;
  - assert EML count = sum of custodian copies, answer-key row count = plan docs plus attachments, every row status Generated/Skipped as expected, and peak `GC.GetTotalMemory`/`Process.PeakWorkingSet64` under a budget (proposed 1.5 GB, [ASSUMED]);
  - a batch-mode variant using a fake `IBatchTransport`.

### Anti-Patterns to Avoid

- **Top-level automatic `cache_control` on Anthropic:** writes the unique suffix every call (cost up, no reads).
- **Putting batch state in `generation-journal.jsonl`:** breaks D-05 projection purity and eats the journal cap.
- **Re-submitting on Continue without a hash lookup:** pays twice (D-09).
- **A second pricing path for the batch estimate:** extend `PriceWork` (D-17). Note: `GenerationRunState.EstimateRemaining` (line ~513) already calls `CalculateTotalCost` directly. Route it through the new cost function too.
- **Ungrouped CV in the leakage audit:** messages of one thread leak across folds.
- **Using DocIds or ThreadIds as `custom_id`:** dots violate Anthropic's regex.
- **Raising parallelism for live mode:** only batch mode should lift `MaxParallelThreads`.

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| Anthropic batch wire format | Raw HTTP/JSON | `client.Messages.Batches.Create(new BatchCreateParams{ Requests = [new Request{ CustomID, Params }] })`, `ResultsStreaming(id)` (IAsyncEnumerable) | `Batches.Params` mirrors `MessageCreateParams` incl. `OutputConfig`, `System`, `CacheControl` [VERIFIED reflection] |
| Parsing OpenAI batch output bodies | Custom DTO for chat completion | `ModelReaderWriter.Read<ChatCompletion>(BinaryData.FromString(bodyJson))` then existing `Classify` | Same classification as live |
| OpenAI file upload | Multipart by hand | `OpenAIFileClient.UploadFileAsync(BinaryData, "batch.jsonl", FileUploadPurpose.Batch)` | Verified method exists |
| Results matching across restart | Positional matching | SHA-256 request identity → `custom_id` | Results may be out of order [CITED] |
| Durable append | New file-writing scheme | Copy `GenerationJournal.AppendAsync` technique (single line + `Flush(true)`, torn-line repair) | Proven in Phases 5-6 |
| Atomic results file / delete retry | Ad hoc `File.WriteAllText` | `CaseStore.WriteAtomic` pattern (temp + move, 100 ms × attempt retries) | D-25 uses the same retry |
| Timeline/value detection | New regexes | `RenderValidator` V3/V4/V6 + `CanonicalValueScanner` | Already tuned (IN-05 fix) |
| Reviewer relevance criteria | New prompt from scratch | `ReviewProtocolBuilder` structure from the guide | Same criteria the key uses |
| Deterministic sampling/folds | `Random` | `RenderAssignments.StableRank` | Project determinism rule |

**Key insight:** this phase is mostly plumbing through seams that already exist. Each place where the plan invents a parallel path (second pricing function, second classifier, batch results flowing into units through a different door) is where D-05/D-17 parity will break.

## Runtime State Inventory

(Cutover/removal phase. All five categories checked.)

| Category | Items Found | Action Required |
|----------|-------------|------------------|
| Stored data | `case.json` files hold no flow or legacy fields: `WizardFlow` lives only in in-memory `WizardState` [VERIFIED: grep Models/Case]. Journals and plans are unaffected. New files this phase: `provider-batches.jsonl`, `batch-results/`, `qa-*.json(l)` in case folders | None for existing cases. New files must be added to `GenerationReset` deletion and `CaseFolderNaming`-safe path rules |
| Live service config | Provider-side state: submitted Anthropic Message Batches and OpenAI batch jobs plus uploaded `purpose=batch` files persist on the provider for 29-30 days | Start Over cancels in-progress batches and deletes OpenAI files (best effort). Nothing else exists today (no batches before this phase) |
| OS-registered state | None. No scheduled tasks, services or registry entries [VERIFIED: no such code in repo] | None |
| Secrets/env vars | `%LOCALAPPDATA%\ReelDiscovery\reeldiscovery.settings` (legacy Base64 key) and old telemetry `settings.json`. `Program.cs` currently migrates and **deletes** the legacy key file | D-22: remove the `MigrateLegacyKey()` call and code; leave files untouched; DPAPI `ApiKeyStore` unchanged |
| Build artifacts | `bin/`, `obj/` contain compiled legacy types; the single-file publish output is regenerated | `dotnet clean` / rebuild after deletion. No egg-info-style stale installs |

## Common Pitfalls

### Pitfall 1: Batch latency with tiny relevant waves
**What goes wrong:** `WavePlanner.MaxThreadsPerWave = 12`. A 5K corpus at the 20% default has about 1,000 relevant emails, which is roughly 300+ threads and about 28+ waves. Each wave needs about 2-3 serial batch rounds (thread text, attachments, chained attachments). That is 60-90 serial batch cycles before Stage B. Anthropic says "most batches finish in less than 1 hour" [CITED]. So a run can take many hours or more.
**Why:** D-07 locks wave-by-wave batching, and the waves are small by design (D-02).
**How to avoid:**
- Be honest about duration on the Ready card. Recommend batch only at large sizes.
- Measure real round latency in the Wave 0 spike.
- Make the hint copy say "slower: can take several hours".
- Raise this tradeoff with the user (Open Question 2).
**Warning signs:** Stages grid stuck on "Waiting on provider batch" for a 12-request batch.

### Pitfall 2: Cost under-reporting from cached tokens (Anthropic) and cache writes (OpenAI)
**What goes wrong:**
- Anthropic `input_tokens` excludes cached tokens. Adding `cache_control` without changing accounting makes cost look lower than it is.
- On GPT-6 models implicit caching may bill 1.25x on writes that the app never records.

**How to avoid:** Pattern 4 normalized usage. Read raw `cache_write_tokens`. Run wire tests that feed captured JSON bodies with cache fields.

### Pitfall 3: Batch classification drift
**What goes wrong:** If batch results are mapped with different code than live, outcomes and journal records differ (D-05).
**How to avoid:** Batch items go through the same `Classify(Message)` / `Classify(ChatCompletion)`. Add a parity test: the same canned response sent live and through a fake batch transport must produce byte-identical journal lines (ignoring `Utc`).

### Pitfall 4: Paying twice after a crash
**What goes wrong:** Continue re-enqueues the same work as a new batch.
**How to avoid:** Look up the request hash in the ledger before enqueuing. Never resubmit an item whose batch is `in_progress`/`ended` unless its result is errored or expired, and those go **live**, not into a new batch (D-10).

### Pitfall 5: Anthropic batch validation happens late
**What goes wrong:** "Validation of the `params` object for each message request is performed asynchronously, and validation errors are returned when processing of the entire batch has ended" [CITED]. A malformed param fails every item only after the wait.
**How to avoid:** Build batch `Params` with the same builder as live `MessageCreateParams` (share the mapping). Unit-test that the serialized params equal the live body captured by `FakeHttpHandler`.

### Pitfall 6: OpenAI batch queue limits on low tiers
**What goes wrong:** gpt-6-luna Tier 1 batch queue limit is 5,000,000 tokens [VERIFIED: model page]. A large Stage B batch fails with `token_limit_exceeded`, and the batch status becomes `failed`.
**How to avoid:** Split by estimated tokens. Treat batch-level `failed` as "all items go live" plus a clear Warning event (D-10).

### Pitfall 7: Model minimum cacheable length makes caching a no-op
**What goes wrong:** The Haiku 4.5 Volume tier (4,096 minimum) and GPT-6 (1,024) never cache a ~600-token prefix. The estimate then claims savings that never appear.
**How to avoid:** The estimator applies cache terms only when the estimated prefix is at least the model minimum. Show the cache read share in the D-16 tooltip from **actual** usage.

### Pitfall 8: Leakage audit with an unbalanced or degenerate sample
**What goes wrong:**
- A small corpus has few relevant threads, so CV folds without positives produce NaN or 100%.
- A one-hot sender with one email overfits.

**How to avoid:**
- Group folds and require at least 1 positive and 1 negative per fold, falling back to fewer folds.
- Use min-support 5 for categorical values and L2 regularization.
- Report "Not enough data" below about 40 emails per class.

### Pitfall 9: Regenerating a relevant thread rewrites history
**What goes wrong:** New ledger deltas contradict later waves that quoted the old thread.
**How to avoid:** Pattern 9 late-render rule. Old deltas stay canonical, and new deltas are not merged.

### Pitfall 10: Cutover breaks DPI-tested API step layout
**What goes wrong:** Removing the workflow radio panel shifts `StepApiConfiguration` rows, and the `TableLayoutPanel` row-index comments go stale.
**How to avoid:** Re-run `Render/*` tests and inspect the PNGs at 144 DPI (D-04).

### Pitfall 11: Projection thrash at 5K
See Pattern 12. Warning sign: the UI stutters and CPU stays high while the provider is idle.

## Code Examples

### Anthropic batch submit with cached system block
```csharp
// Source: Anthropic C# SDK 12.51.0 types (verified by reflection); batch doc C# sample
var request = new Anthropic.Models.Messages.Batches.Request
{
    CustomID = identity.CustomId,                       // ^[a-zA-Z0-9_-]{1,64}$
    Params = new Anthropic.Models.Messages.Batches.Params
    {
        Model = r.ModelId,
        MaxTokens = Math.Min(r.MaxTokens, AnthropicTextProvider.MaxNonStreamingTokens), // keep live parity
        System = new List<TextBlockParam> { new(r.SystemPrompt) { CacheControl = new CacheControlEphemeral() } },
        Messages = [new() { Role = Role.User, Content = r.UserPrompt }],
        OutputConfig = outputConfig                     // same builder as live BuildParams
    }
};
MessageBatch batch = await _client.Messages.Batches.Create(new BatchCreateParams { Requests = requests }, ct);
// poll: (await _client.Messages.Batches.Retrieve(batch.ID, new BatchRetrieveParams(), ct)).ProcessingStatus.Raw() == "ended"
await foreach (var item in _client.Messages.Batches.ResultsStreaming(batch.ID, new BatchResultsParams(), ct))
{
    if (item.Result.TryPickSucceeded(out var ok))
        Complete(item.CustomID, AnthropicTextProvider.Classify(ok.Message), Usage(ok.Message.Usage));
    else
        FallBackToLive(item.CustomID);                  // errored / canceled / expired: not billed [CITED]
}
```
Note: the exact implicit conversions for `System`/`Content` on `Batches.Params` mirror `MessageCreateParams` (verified property types `ParamsSystem`, `IReadOnlyList<MessageParam>`). Confirm by compiling against the live `BuildParams` code. [ASSUMED: identical implicit operators]

### OpenAI batch line and submit (protocol level)
```csharp
// Source: developers.openai.com batch guide (line shape) + OpenAI 2.14.0 BatchClient/OpenAIFileClient (verified)
// One JSONL line per request, written with Utf8JsonWriter (never string concat):
// {"custom_id":"q3f…","method":"POST","url":"/v1/chat/completions",
//  "body":{"model":"gpt-6-luna","messages":[{"role":"system","content":"…"},{"role":"user","content":"…"}],
//          "response_format":{"type":"json_schema","json_schema":{"name":"…","strict":true,"schema":{…}}},
//          "max_completion_tokens":16000,"reasoning_effort":"none"}}
#pragma warning disable OPENAI001 // BatchClient is [Experimental] in 2.14.0
var file = (await _files.UploadFileAsync(BinaryData.FromBytes(jsonl), "reeldiscovery-batch.jsonl", FileUploadPurpose.Batch)).Value;
var op = await _batches.CreateBatchAsync(BinaryContent.Create(BinaryData.FromObjectAsJson(new
{
    input_file_id = file.Id, endpoint = "/v1/chat/completions", completion_window = "24h"
})), waitUntilCompleted: false);
// poll: JsonDocument.Parse((await _batches.GetBatchAsync(op.BatchId, null)).GetRawResponse().Content)
//       → "status", "request_counts", "output_file_id", "error_file_id"
#pragma warning restore OPENAI001
// results: each output line {"custom_id", "response":{"status_code","body":{chat completion}}, "error"}
var completion = ModelReaderWriter.Read<ChatCompletion>(BinaryData.FromString(bodyJson));
```

## State of the Art

| Old Approach | Current Approach | When Changed | Impact |
|--------------|------------------|--------------|--------|
| OpenAI automatic caching, no write charge | GPT-5.6+: implicit or explicit breakpoints, **1.25x cache writes**, 0.1x reads (0.05x gpt-6.1-sol), 30m TTL via `prompt_cache_options.ttl` | GPT-5.6 generation [CITED] | Implicit default can raise input cost on unique prompts |
| Anthropic block-only `cache_control` | Top-level automatic `cache_control` also available | Recent SDK (`MessageCreateParams.CacheControl` present in 12.51.0) | Do not use it for per-thread prompts |
| Anthropic min cacheable 1,024 / 2,048 | 512 for Opus 5.x / Sonnet 5.5; 4,096 for Haiku 4.5 / Opus 4.5-4.6 | Per model table [CITED] | Story tier can cache a short prefix; Volume (Haiku) cannot |
| Opus cache read 0.1x | Opus 5.5 cache read **0.05x** | [CITED pricing] | Default price column must reflect it |

**Deprecated/outdated in this repo:**
- CLAUDE.md's ".NET 8" stack text: the project is net10.0. Do not "fix" the csproj back.
- `ApiKeyStore.MigrateLegacyKey`: obsolete under D-22.

## Assumptions Log

| # | Claim | Section | Risk if Wrong |
|---|-------|---------|---------------|
| A1 | Chat Completions on GPT-6 models applies implicit cache writes at 1.25x and reports `prompt_tokens_details.cache_write_tokens` | Pattern 4/5 | Either cost is under-reported today (if true) or the explicit-mode work is unnecessary (if false). Needs a live spike |
| A2 | Chat Completions accepts `prompt_cache_options` / `prompt_cache_breakpoint` (docs show only Responses API) | Pattern 5 | If rejected (400), OpenAI caching control is impossible on Chat Completions; leave implicit and price it honestly |
| A3 | The structured-output JSON schema counts toward the Anthropic cacheable prefix length | Pattern 5 | If not, the Opus/Sonnet prefix (~330 tokens system) is below 512 and nothing caches on the evidence path |
| A4 | Leakage thresholds Pass ≤60% / Warning 60-70% / Fail >70% balanced accuracy | Pattern 8 | Mis-calibrated verdicts; easy to retune (documented in report) |
| A5 | 5K peak memory budget 1.5 GB for the Scale test | Pattern 12 | Test too loose or too strict; adjust after first measurement |
| A6 | `Batches.Params` accepts the same implicit conversions as `MessageCreateParams` for `System`/`Messages` | Code Examples | Minor compile adjustments only |
| A7 | Quiet-period flush (3 s) reliably groups one wave into one batch | Pattern 1 | Waves split into several batches (slower, same cost); tune the timer |
| A8 | Batch recommendation threshold about 2,000 planned emails | D-08 discretion | Only affects hint text |
| A9 | Typical batch round latency is minutes (Anthropic) to tens of minutes (OpenAI) | Pitfall 1 | Batch mode could be impractically slow for relevant waves |

## Open Questions (RESOLVED)

1. **Does Chat Completions on GPT-6 bill implicit cache writes, and does it accept explicit cache options?** (A1, A2)
   - What we know: the GPT-5.6+ docs say implicit is the default and writes cost 1.25x. The controls are shown for the Responses API only.
   - What's unclear: Chat Completions behaviour and usage fields.
   - Recommendation: **Wave 0 live spike** (with user consent, a few cents). Make 2 identical and 2 different-suffix calls with gpt-6-luna on `/v1/chat/completions`. Log raw usage JSON. Try `prompt_cache_options: {"mode":"explicit"}` via `Patch`. Decide OpenAI caching mode from the results.
   - **RESOLVED:** 07-22 runs the consented live spike (Tasks 1-3) and applies one recorded branch; until then 07-02 reads `cache_write_tokens` when present and sends no cache options (degrades safely).
2. **Is "everything in batch, wave by wave" acceptable given about 60-90 serial batch rounds for relevant waves?**
   - What we know: waves are at most 12 threads (`WavePlanner`). Batches take "most less than 1 hour".
   - Recommendation: keep D-07 as locked. Measure one real round in the spike and surface the expected duration on the Ready card. If the user finds it too slow, the cheapest amendment is "relevant waves live, Stage B batched". About 2/3 of spend is Story-tier relevant work, so that amendment keeps roughly 1/6 of the savings. Flag it for discuss-phase rather than deciding here.
   - **RESOLVED (planner default, user-overridable):** D-07 kept; the Ready-card hint warns batch runs can take several hours (07-11); real round latency recorded by the 07-22 spike.
3. **Regenerate thread semantics for a relevant thread whose details later waves already used.**
   - Recommendation: Pattern 9 late-render rule (old deltas canonical, new deltas not merged). Confirm with the user that a regenerated relevant thread may not introduce new details.
   - **RESOLVED (planner default, user-overridable):** a regenerated relevant thread keeps its prior established details and adds none (07-13).
4. **Should edited prices persist across sessions?** Today the catalog and media prices are session-only. D-18 does not say. Recommend keeping them session-only (no new settings file, consistent with D-22's no-new-legacy-files spirit) unless the user asks.
   - **RESOLVED:** prices stay session-only (07-12).
5. **Reviewer tier:** the other provider's Volume model (cheap) versus Story (stronger judge). Recommend Volume by default with the model shown in the estimate line.
   - **RESOLVED:** the reviewer uses the other provider's Volume model for both tiers (07-16).

## Environment Availability

| Dependency | Required By | Available | Version | Fallback |
|------------|------------|-----------|---------|----------|
| .NET SDK | build/test | ✓ | 10.0.401 (and 9.0.103) | — |
| Anthropic SDK (nuget cache) | batch/caching | ✓ | 12.51.0 | — |
| OpenAI SDK (nuget cache) | batch/caching | ✓ | 2.14.0 | — |
| Anthropic / OpenAI API keys + network | live spike, UAT | Unknown for agents (user's DPAPI store) | — | All automated tests use fakes; the live spike needs the user |
| Two provider keys | D-12 cross-provider review | User-dependent | — | Same-provider fallback (reported) |

**Missing dependencies with no fallback:** none for automated work.
**Missing dependencies with fallback:** live provider verification (Open Question 1) needs the user's keys. Plan it as a human-in-the-loop checkpoint.

## Validation Architecture

### Test Framework
| Property | Value |
|----------|-------|
| Framework | xUnit 2.9.3 + Microsoft.NET.Test.Sdk 17.14.1, net10.0-windows (`ReelDiscovery.Tests/ReelDiscovery.Tests.csproj`, ~2,560 test methods; 3,420 cases passing after Phase 6) |
| Config file | none (no runsettings); `InternalsVisibleTo ReelDiscovery.Tests` |
| Quick run command | `dotnet test ReelDiscovery.Tests/ReelDiscovery.Tests.csproj --filter "FullyQualifiedName~ReelDiscovery.Tests.Ai\|FullyQualifiedName~ReelDiscovery.Tests.Qa&Category!=Render&Category!=Scale"` |
| Full suite command | `dotnet test ReelDiscovery.Tests/ReelDiscovery.Tests.csproj --filter "Category!=Render&Category!=Scale"` (~60-90 s) |
| Render command | `dotnet test ReelDiscovery.Tests/ReelDiscovery.Tests.csproj --filter "Category=Render"`; PNGs under `ReelDiscovery.Tests/bin/Debug/net10.0-windows/renders/`, inspected at 144 DPI |
| Scale command (new) | `dotnet test ReelDiscovery.Tests/ReelDiscovery.Tests.csproj --filter "Category=Scale"` (minutes; per wave merge and phase gate only) |

Reusable fixtures:
- `Fakes/FakeLlmProvider` (scripted / `Respond` func)
- `Fakes/FakeHttpHandler` (captures `RequestBodies`, so live wire bodies can be compared against batch params/lines)
- `Fakes/StrictSchemaValidator`
- `Fakes/FakeMediaGenerator`
- `EvidenceTestData.RenderResponder(plan, guide)`, `AttachmentResponder`
- `Corpus/PlanFixtures`, `CorpusPlannerTests` 5000-email planner responder
- `DistributionParityTests` fixtures
- `Render/RenderHarness` + `ClippingDetector`
- `GenerationJournal.SizeCapOverride` (AsyncLocal)

### Phase Requirements → Test Map
| Req ID | Behavior | Test Type | Automated Command (filter) | File Exists? |
|--------|----------|-----------|----------------------------|-------------|
| RUN-03 | First attempts queue and flush as one batch per wave; retries and invalid parses go live; errored/expired items go live; same journal lines as live (parity) | unit + integration (fake `IBatchTransport`) | `FullyQualifiedName~BatchingLlmProviderTests\|FullyQualifiedName~BatchParityTests` | ❌ Wave 0 |
| RUN-03 | Anthropic batch `Params` equal the live `MessageCreateParams` body; OpenAI JSONL `body` equals the captured live Chat Completions body | wire | `FullyQualifiedName~AnthropicBatchWireTests\|FullyQualifiedName~OpenAIBatchWireTests` | ❌ Wave 0 |
| RUN-04 | Ledger append+flush; restart with the same request hash attaches to the submitted batch (no new submit); torn last line skipped; Start Over cancels and deletes | unit | `FullyQualifiedName~BatchLedgerTests\|FullyQualifiedName~GenerationResetTests` | ❌ / ✅ extend |
| RUN-05 | System block carries `cache_control`; no top-level cache_control; OpenAI options per spike decision; byte-stable prefixes (existing `EvidencePromptsTests` invariants) | wire | `FullyQualifiedName~AnthropicWireTests\|FullyQualifiedName~OpenAIWireTests\|FullyQualifiedName~EvidencePromptsTests` | ✅ extend |
| PROV-07 | Usage normalization (Anthropic exclusive, OpenAI inclusive with raw `cache_write_tokens`); `CalculateCost` with batch discount; tracker breakdown; journal optional fields round-trip and old lines still replay | unit | `FullyQualifiedName~TokenUsageTests\|FullyQualifiedName~ModelCatalogTests\|FullyQualifiedName~GenerationJournalTests` | ❌ / ✅ extend |
| PROV-07 | Live and batch totals from one `PriceWork`; cache term only when prefix ≥ model minimum | unit | `FullyQualifiedName~GenerationCostEstimatorTests` | ✅ extend |
| PROV-07 / D-18 | Model dialog with 3 new columns fits at 150%; Reset Defaults restores verified prices | render + unit | `FullyQualifiedName~DialogRenderTests\|FullyQualifiedName~MediaPricesDialogTests` | ✅ extend |
| QA-01 | Sample stratification (all relevant + equal NR, decoys first, deterministic); reviewer provider selection and fallback flag; DTO strict; disagreement report; estimate math | unit | `FullyQualifiedName~BlindReviewTests` | ❌ Wave 0 |
| QA-02 | Grouped CV logistic regression deterministic; "privilege banner: only on relevant mail" reported on the pre-D-23 250-email plan and gone after the fix; degenerate data gives "Not enough data" | unit | `FullyQualifiedName~LeakageAuditTests\|FullyQualifiedName~NonRelevantSkeletonTests` | ❌ / ✅ extend |
| QA-03 | Post-hoc scan flags a seeded future-fact mention (V4), a wrong canonical amount (V3), a ledger contradiction (V6) and a canonical value in filler; report only | unit | `FullyQualifiedName~CorpusScansTests` | ❌ Wave 0 |
| D-13/D-15 | `ThreadInvalidated` replays as pending, EMLs removed, spend kept; relevant late-render keeps old deltas | unit + integration | `FullyQualifiedName~ThreadInvalidationTests\|FullyQualifiedName~GenerationRunnerTests` | ❌ Wave 0 |
| OUT-03 | ~5K in-process run: EML count, key rows, statuses, peak memory budget; batch-mode variant | integration (Scale) | `Category=Scale` | ❌ Wave 0 |
| OUT-03 / PLAT-05 | Single flow: API step then Case Scenario; legacy types gone (reflection test: no `ReelDiscovery.Services.EmailGenerator` etc.); `Program` does not call the migration | unit + render | `FullyQualifiedName~WizardFlowTests\|FullyQualifiedName~PlatformTests\|Category=Render` | ✅ rewrite |
| D-24 | Two invites for one meeting from different sender TZs get the same instant | unit | `FullyQualifiedName~InviteSchedulerTests` | ✅ extend |
| D-25 | `TryDelete` retries a transient lock then succeeds | unit | `FullyQualifiedName~GenerationResetTests` | ✅ extend |
| D-26 | Invalid theme fonts/colors cleaned in `BuildEmailTemplate`; Phase 5 golden HTML byte-identical | unit (golden) | `FullyQualifiedName~ComposeHtmlGolden\|FullyQualifiedName~ComposeHtmlTests` | ✅ |

### Sampling Rate
- **Per task commit:** the quick run command for the touched area.
- **Per wave merge:** full suite. UI waves also run Render. Batch/scale waves also run Scale.
- **Phase gate:** full + Render + Scale green, PNGs inspected at 144 DPI, the live spike results recorded, and one human UAT batch run (small corpus) on each provider before `/gsd-verify-work`.

### Wave 0 Gaps
- [ ] `ReelDiscovery.Tests/Fakes/FakeBatchTransport.cs`: scripted submit/poll/results with injectable latency, errored/expired items and a batch-level `failed`
- [ ] `ReelDiscovery.Tests/Ai/BatchingLlmProviderTests.cs`, `BatchParityTests.cs`, `AnthropicBatchWireTests.cs`, `OpenAIBatchWireTests.cs`, `BatchLedgerTests.cs`, `TokenUsageTests.cs`
- [ ] `ReelDiscovery.Tests/Qa/BlindReviewTests.cs`, `LeakageAuditTests.cs`, `CorpusScansTests.cs`
- [ ] `ReelDiscovery.Tests/Evidence/ThreadInvalidationTests.cs`
- [ ] `ReelDiscovery.Tests/Scale/FiveThousandRunTests.cs` with `[Trait("Category","Scale")]`
- [ ] Live spike script/checklist (human) for Open Questions 1-2

## Security Domain

(security_enforcement enabled, ASVS L1)

### Applicable ASVS Categories

| ASVS Category | Applies | Standard Control |
|---------------|---------|-----------------|
| V2 Authentication | yes (API keys) | Existing DPAPI `ApiKeyStore`. Keys never written into batch files, ledger or logs. Reviewer gateway reads the key the same way |
| V3 Session Management | no | — |
| V4 Access Control | no | Single-user desktop |
| V5 Input Validation | yes | Strict JSON-schema DTOs for reviewer output. Batch result lines parsed defensively (`JsonDocument`, unknown custom_id ignored). Provider batch IDs and file IDs sanitized before use in file names (constant prefix + `[A-Za-z0-9_-]` only). CSV formula neutralization stays for QA output if exported |
| V6 Cryptography | yes (hashing only) | `SHA256.HashData` for request identity. Never hand-roll |
| V7 Error handling/logging | yes | Provider error bodies reduced to `error.message` (existing `SafeMessage`). Never log request headers |
| V12 Files | yes | Case-folder-relative constant names. Atomic writes. Delete retry bounded (D-25) |

### Known Threat Patterns for this stack

| Pattern | STRIDE | Standard Mitigation |
|---------|--------|---------------------|
| Path traversal via provider-supplied batch/file id in a results file name | Tampering | Sanitize to `^[A-Za-z0-9_-]{1,64}$`; else hash it |
| Results injected for unknown custom_ids or a foreign plan | Tampering | Only hashes present in the ledger for the current PlanId complete tasks; others count as orphan spend only |
| Prompt injection inside reviewed documents steering the blind reviewer | Tampering | Document text placed in the user turn after the cached protocol; strict DTO; report only (no automatic action, D-13) |
| Key leakage through JSONL request files kept on disk | Information disclosure | JSONL is built in memory and uploaded; only ids are persisted. Never persist request bodies with headers |
| Runaway spend from batches the user forgot | Denial of wallet | Start Over cancels; Cancel keeps tracking so nothing is resubmitted; orphan spend surfaced in RunEnded |

## Sources

### Primary (HIGH confidence)
- Anthropic C# SDK 12.51.0 and OpenAI .NET SDK 2.14.0: reflection over the nuget-cache DLLs (Batches service, Params, Usage, CacheControlEphemeral, BatchClient/OpenAIFileClient, ChatInputTokenUsageDetails, Experimental attributes)
- https://platform.claude.com/docs/en/build-with-claude/prompt-caching: min lengths per model, usage semantics, automatic caching, invalidation table, below-minimum behaviour
- https://platform.claude.com/docs/en/build-with-claude/batch-processing: limits, 24h expiry, 29-day results, custom_id regex, result types and billing, caching in batches, async validation
- https://platform.claude.com/docs/en/about-claude/pricing.md: Opus 5.5 / Sonnet 5.5 / Haiku 4.5 base, write, read and batch prices
- https://developers.openai.com/api/docs/guides/batch: line format, endpoints, 50,000 requests / 200 MB, statuses, expiry billing, 30-day output retention
- https://developers.openai.com/api/docs/guides/prompt-caching.md: GPT-5.6+ implicit/explicit modes, 1.25x writes, minimum 1,024, usage fields
- https://developers.openai.com/api/docs/pricing.md, /models/gpt-6.1-sol.md, /models/gpt-6-luna.md, /models/gpt-6-sol.md: standard and batch prices including cache columns, batch queue limits
- Repository code (Services/Ai, Services/Evidence, Services/Corpus, Forms/WizardForm.cs, Program.cs, tests): read directly

### Secondary (MEDIUM confidence)
- `.planning/research/ARCHITECTURE.md` / `PITFALLS.md` (earlier project research; batch and leakage design ideas)

### Tertiary (LOW confidence)
- Chat Completions behaviour for GPT-6 cache options (not documented; Assumptions A1/A2)

## Metadata

**Confidence breakdown:**
- Standard stack: HIGH. Exact package versions are reflected and no new dependencies are needed.
- Architecture: MEDIUM-HIGH. The decorator fits the verified seams. Flush timing and the regenerate-thread semantics need validation.
- Pitfalls: HIGH for cost/caching semantics and limits (official docs). MEDIUM for latency and memory figures (estimates, to be measured).

**Research date:** 2026-10-06
**Valid until:** about 2026-11-05 for provider pricing and caching (fast-moving: re-check before UAT); stable for repo structure.
