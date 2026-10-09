---
phase: 07-08
source: codex review --base cbe36a1 (codex-cli 0.160.0)
reviewed: 2026-10-09
depth: standard
status: issues_found
findings:
  critical: 1
  warning: 6
  info: 0
  total: 7
---

# Codex Review — Phases 7-8

IDs: CX-01 = [P1] (treat as critical); CX-02..CX-07 = [P2] (treat as warning).

### CX-01 [P1] Partition OpenAI batches by model as well as stage — C:\Users\ghand\source\repos\ReelDiscovery\ReelDiscovery\Services\Ai\BatchingLlmProvider.cs:644-648

  When a non-relevant chunk contains both decoys and ordinary filler, `RenderProfile.For` selects the Story and Volume models respectively, but this queue combines them under the same stage label. OpenAI requires each batch input file to contain only one model ([documentation](https://developers.openai.com/api/docs/guides/batch)). With different tier models, the batch fails validation and its requests fall back to full-price live calls. Partition OpenAI submissions by model before building the input file.

### CX-02 [P2] Reject empty best attempts before replacing saved documents — C:\Users\ghand\source\repos\ReelDiscovery\ReelDiscovery\Services\Discovery\WrittenSetWriter.cs:160-162

  If every attempt returns an empty request list, blank text, or only unknown slots, `best` becomes an empty dictionary rather than remaining null. This guard therefore accepts a batch containing no usable requests. `DiscoveryGenerator` treats the resulting non-null content as generated and can overwrite a valid previous document with boilerplate. Reject an empty normalized best attempt after retries; the equivalent null-only guard in `DepositionWriter` also accepts empty testimony.

### CX-03 [P2] Serialize Save Again with other discovery writes — C:\Users\ghand\source\repos\ReelDiscovery\ReelDiscovery\UserControls\StepDiscoveryDocuments.Run.cs:150-150

  While this await is pending, Save Again and generation remain available because no busy state is set. `SaveAgainAsync` loads the entire discovery sidecar before awaiting the Word-file write, then saves that stale snapshot afterward. Overlapping saves can lose another document's saved status, and overlapping generation can lose newly generated records or costs. Serialize discovery read-modify-write operations, or prevent other discovery mutations until Save Again finishes.

### CX-04 [P2] Add the admission prefix only once in Word output — C:\Users\ghand\source\repos\ReelDiscovery\ReelDiscovery\Services\Discovery\DiscoveryDocxBuilder.cs:54-55

  `WrittenSetWriter.Assemble` already stores `Joined(slot.Prefix, text)` in `request.Text` while retaining `request.Prefix`. Prepending the prefix again here duplicates the opening clause in generated requests for admission, including their dates or document identifiers. Both initial generation and Save Again produce the duplication. Make either the writer or the Word builder responsible for joining the prefix, consistently.

### CX-05 [P2] Keep batches recoverable when result caching fails — C:\Users\ghand\source\repos\ReelDiscovery\ReelDiscovery\Services\Ai\BatchingLlmProvider.cs:1072-1074

  If `SaveResults` fails but this ledger append succeeds, the batch is marked collected without a durable result cache. After a restart, recovery reads collected batches only from that cache and excludes them from pending downloads, so already-paid results are lost and matching requests are submitted again. Only commit collection after durable caching, or redownload collected batches whose cache is missing. This also undermines the incremental-persistence and batch cost-control requirements in [CLAUDE.md:17](CLAUDE.md#L17).

### CX-06 [P2] Enforce the token cap across outstanding batches — C:\Users\ghand\source\repos\ReelDiscovery\ReelDiscovery\Services\Ai\BatchingLlmProvider.cs:798-799

  `Split` applies the token allowance separately to each chunk, while `SubmitAsync` returns after submission rather than batch completion. A large same-model workload therefore submits multiple chunks whose combined queued tokens exceed the allowance. OpenAI's limit applies across queued requests per model, not independently per file ([documentation](https://developers.openai.com/api/docs/guides/batch)); rejected chunks fall back to full-price live calls. Track outstanding tokens and wait for capacity before submitting another chunk, preserving the batch cost control required by [CLAUDE.md:17](CLAUDE.md#L17).

### CX-07 [P2] Drain in-flight submissions before declaring the run stopped — C:\Users\ghand\source\repos\ReelDiscovery\ReelDiscovery\Services\Evidence\GenerationRunner.cs:565-568

  If Stop occurs during upload or batch creation, the caller's wait is cancelled, but the fire-and-forget submission continues with `CancellationToken.None`. `Detach` does not await it, so the runner returns and the UI permits closing or Start Over before the Submitted ledger record exists. Closing can leave an accepted paid batch untracked; Start Over can miss it and have its ledger recreated afterward. Await submission and durable ledger recording before reporting the run stopped, without waiting for remote batch execution.
