---
phase: 07-scale-cost-qa-cutover
plan: 09
subsystem: ai-batch
tags: [batch, anthropic, openai, wire-parity, RUN-03, D-05, D-19]
requires:
  - "07-02 AnthropicTextProvider.BuildParams/BuildOutputConfig/Classify/MapException; OpenAITextProvider.BuildMessages/BuildOptions/Classify/MapStatus/CreateDefaultOptions"
  - "07-07 IBatchTransport contracts and result types"
provides:
  - "AnthropicBatchTransport : IBatchTransport (Messages.Batches Create/Retrieve/ResultsStreaming/Cancel)"
  - "OpenAIBatchTransport : IBatchTransport (Files upload purpose batch + BatchClient, 24h window)"
  - "OpenAIBatchTransport.SerializeBody(request) = live Chat Completions body bytes; BuildJsonl(items)"
  - "LlmProviderFactory.CreateBatchTransport(kind, apiKey)"
affects: [07-10-runner]
tech-stack:
  added: []
  patterns:
    - "Parity by construction: OpenAI batch body captured from the SDK's own serializer through an in-process handler"
    - "Anthropic batch Params copied field-by-field from the live MessageCreateParams (system union converted via Match)"
key-files:
  created:
    - Services/Ai/AnthropicBatchTransport.cs
    - Services/Ai/OpenAIBatchTransport.cs
    - ReelDiscovery.Tests/Ai/AnthropicBatchWireTests.cs
    - ReelDiscovery.Tests/Ai/OpenAIBatchWireTests.cs
  modified:
    - Services/Ai/LlmProviderFactory.cs
    - Services/Ai/AnthropicTextProvider.cs
    - Services/Ai/OpenAITextProvider.cs
decisions:
  - "07-09: OpenAI cancel uses CreateBatchOperation.RehydrateAsync + CancelAsync (BatchClient 2.14.0 has no CancelBatchAsync)"
  - "07-09: OpenAI status Done = completed + failed; Anthropic Done = total - processing, Failed = errored + expired + canceled"
  - "07-09: OpenAI 200 output lines without a readable completion object are skipped (like garbage) so the decorator re-runs them"
  - "07-09: Anthropic errored items carry a fixed message only; OpenAI non-200 lines use SafeMessage (error.message only)"
metrics:
  duration: "~25 min"
  completed: 2026-10-06
  tasks: 2
  files: 7
---

# Phase 7 Plan 09: Provider Batch Transports Summary

Real Anthropic Message Batches and OpenAI Batch API transports whose request bodies are produced by the live request builders/serializer and whose results go through the live classifiers, proven by wire tests (params deep-equal / body byte-identical to live).

## What was built

- **AnthropicBatchTransport** — `Request { CustomID, Params = ToBatchParams(AnthropicTextProvider.BuildParams(r)) }`; Model, MaxTokens (live 21,333 cap), System (list form with `cache_control: ephemeral`), Messages and OutputConfig copied from the live params. `processing_status` `ended` → Ended, everything else → InProgress. Results streamed; succeeded → `AnthropicTextProvider.Classify(message)` with `IsBatch = true`; errored/expired/canceled mapped to `BatchItemOutcome`. Errors go through `MapException` + `SafeMessage` (now `internal`). Cancel is best-effort; `DeleteFilesAsync` is a no-op.
- **OpenAIBatchTransport** — `SerializeBody` runs `ChatClient.CompleteChat(BuildMessages, BuildOptions)` against a private capturing `HttpMessageHandler` (never touches the network) and returns the captured bytes. JSONL lines written with `Utf8JsonWriter` (`custom_id`, `method`, `url`, raw `body`), built in memory, uploaded with `FileUploadPurpose.Batch`, then `CreateBatchAsync` with `input_file_id`, `/v1/chat/completions`, `24h`. Status JSON parsed defensively (validating/in_progress/finalizing/cancelling → InProgress, completed → Ended, failed → Failed, expired → Expired, cancelled → Cancelled). Results download output then error file; 200 lines → `ModelReaderWriter.Read<ChatCompletion>` + `OpenAITextProvider.Classify(completion, raw)` (cache_write_tokens honored), non-200/error-file lines → Errored, garbage skipped, first line per id wins. Failures mapped via `MapStatus` + `SafeMessage` (now `internal`, as is `ReadBody`). File deletes are best-effort per file.
- **LlmProviderFactory.CreateBatchTransport** — switch like `Create`.

## Commits

| Task | Commit | Message |
|------|--------|---------|
| 1 | b0bb405 | feat(07-09): Anthropic batch transport with live-identical params |
| 2 | 266ffcc | feat(07-09): OpenAI batch transport with byte-identical JSONL bodies |

## Verification

- `dotnet build ReelDiscovery.csproj -warnaserror`: 0 warnings, 0 errors
- `dotnet test --filter "OpenAIBatchWireTests|AnthropicBatchWireTests|Ai.OpenAI|Ai.Anthropic|Batch"`: 176/176 passed (10 Anthropic batch, 20 OpenAI batch wire tests)
- Acceptance greps: class lines = 1 each; OPENAI001 disable 5 / restore 5; `OpenAITextProvider.Classify` 2; `FileUploadPurpose.Batch` 1; no Debug/Console/Trace writes in either transport; `AnthropicTextProvider.BuildParams|Classify` 4; `CreateBatchTransport` 1

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 3 - Blocking] Anthropic system union type mismatch**
- **Found during:** Task 1
- **Issue:** `MessageCreateParamsSystem` does not convert implicitly to `Batches.ParamsSystem` (research A6).
- **Fix:** `live.System!.Match<ParamsSystem>(text => text, blocks => blocks.ToList())`.
- **Commit:** b0bb405

**2. [Rule 3 - Blocking] No `BatchClient.CancelBatchAsync` in OpenAI 2.14.0**
- **Found during:** Task 2
- **Fix:** `CreateBatchOperation.RehydrateAsync(_batches, batchId, ct)` then `operation.CancelAsync(...)`; still best-effort.
- **Commit:** 266ffcc

**3. [Rule 3 - Blocking] Private error helpers**
- `AnthropicTextProvider.SafeMessage`, `OpenAITextProvider.SafeMessage` and `ReadBody` changed from private to internal so the transports reuse the live safe-message logic.

**4. Upload uses the Stream overload** of `UploadFileAsync` (instead of BinaryData) so the caller's `ct` is honored; the bytes come from an in-memory `MemoryStream`.

## TDD Gate Compliance

Both tasks are `tdd="true"`, but tests and implementation were committed together in one `feat(...)` commit per task (no separate RED `test(...)` commit). The wire tests were written against the final SDK shapes, which had to be discovered by compiling. Warning: RED gate commits are missing for 07-09.

## Known Stubs

None.

## Threat Flags

None. The new network surface (batch/file endpoints) is covered by T-07-29..T-07-32.

## Self-Check: PASSED
