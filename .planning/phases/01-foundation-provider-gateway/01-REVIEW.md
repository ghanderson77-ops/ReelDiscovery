---
phase: 01-foundation-provider-gateway
reviewed: 2026-09-30T18:00:00Z
depth: standard
re_review_of: 2026-09-30T14:33:03Z
diff_base: 90ac93e
files_reviewed: 15
files_reviewed_list:
  - Helpers/KeyTestGuard.cs
  - Services/Ai/AnthropicTextProvider.cs
  - Services/Ai/ILlmProvider.cs
  - Services/Ai/OpenAIMediaClient.cs
  - Services/Ai/OpenAITextProvider.cs
  - Services/EmailGenerator.cs
  - UserControls/StepApiConfiguration.cs
  - UserControls/StepComplete.cs
  - ReelDiscovery.Tests/Ai/TransportTimeoutTests.cs
  - ReelDiscovery.Tests/Generation/EmailDistributionTests.cs
  - ReelDiscovery.Tests/Generation/EmailGeneratorFixTests.cs
  - ReelDiscovery.Tests/Generation/EmailGeneratorTimeoutTests.cs
  - ReelDiscovery.Tests/KeyTestGuardTests.cs
  - ReelDiscovery.Tests/RunSummaryTests.cs
  - UserControls/StepGeneration.cs
findings:
  critical: 0
  warning: 10
  info: 10
  total: 20
resolved:
  - CR-01
  - CR-02
  - WR-05
  - WR-06
status: issues_found
---

# Phase 01: Code Review Report (re-review after gap closure 01-12 / 01-13)

**Reviewed:** 2026-09-30T18:00:00Z (original review 2026-09-30T14:33:03Z)
**Depth:** standard
**Files Reviewed:** 15 (diff `90ac93e..HEAD`, plus `StepGeneration.cs` as the consumer of the new result fields)
**Status:** issues_found

## Summary

This re-review covers the gap-closure changes from plans 01-12 and 01-13: timeout classification in both providers and the media client, the timeout-safe `EmailGenerator` catches, `RecordUnexpectedStop`, `SavePartialAsync`, the new `DistributeEmails`, the key-test lock and `KeyTestGuard`, and the outcome-aware completion page. The full test suite passes (211/211).

**Resolved:**
- **CR-01:** A transport timeout is now classified instead of treated as a cancel. Every OCE catch is filtered on the caller's token: `ct` in the providers and media client, `runCts` in the generator. A timeout becomes `Failed/Network` or a `null` media item at the provider layer, and a recorded skip at the generator layer. The outer catch now records an error for any stop that is neither a user cancel nor a fatal error. User cancel and fatal stop still propagate correctly: `runCts` is linked to `ct`, so a user cancel satisfies both filters. I found no path where a genuine cancel is swallowed.
- **CR-02:** The key box and Clear are disabled while a test is in flight, and a stale result is discarded before `SetKeyState`/`SetKey`. The finally block re-enables all three controls on every path, including the stale-result early `return` and the exception path.
- **WR-05:** `state.Result` is assigned at the start of the run, so every exit path leaves the current result visible. The outer `catch (Exception)` now saves partial output. The completion page shows the run status and error count. Double saving is not possible: `SaveAndFinalizeAsync` catches its own exceptions, so the happy-path save can never fall into the outer catch and save again.
- **WR-06:** `DistributeEmails` no longer produces negative counts for the reported case, always sums to the total for realistic inputs, and throws a clear error when there are no storylines. Storylines allocated zero emails make no LLM call.

**New findings from the fixes:**
- **WR-10:** The completion page still calls output "usable for import" when the EML save failed, or when the run aborted before producing any email.
- **WR-11:** `DistributeEmails` can still produce negative counts, through `int` overflow on a user-edited "Est. Emails" value.
- **WR-12:** The new key-test lock can hold the key box and Clear disabled for 10+ minutes, with no way to cancel.

WR-01 to WR-04 and WR-07 to WR-09 are unchanged and still open. There are no open blockers, but warnings remain, so the status is `issues_found`.

## Resolved Findings

### CR-01: Transport timeouts are treated as cancellation (RESOLVED)

**Files:** `Services/Ai/OpenAITextProvider.cs:54-62, 83-91`, `Services/Ai/AnthropicTextProvider.cs:62-70, 94-102`, `Services/Ai/OpenAIMediaClient.cs:57-61, 107-111`, `Services/EmailGenerator.cs:184-194, 441-452, 494-503, 550-557`

**Verification:**
- Both providers split the OCE catch into two clauses. `when (ct.IsCancellationRequested)` rethrows; the unfiltered clause returns `Failed(Network)`. The media client catches `when (!ct.IsCancellationRequested)` and returns `null`, and a caller cancel still propagates.
- The storyline lambda and `RunIsolatedAsync` rethrow only when `runCts.IsCancellationRequested`, and record a skip otherwise.
- `RecordUnexpectedStop` adds an error when the stop is neither a user cancel nor a fatal error.
- Tests cover the provider timeout, the provider user cancel, a media timeout, a storyline timeout, a `RunIsolated` timeout, and a `RunIsolated` run cancel.

**Race analysis:**
- **Timeout while a fatal stop cancels `runCts`:** the OCE is rethrown as cancellation. That is correct, because the run is stopping anyway.
- **Timeout while the user cancels:** `ct` is cancelled, so the OCE is rethrown and `WasCancelled` is set.
- **Semaphore wait:** `semaphore.WaitAsync(runToken)` sits outside the lambda's try, but it can only throw when `runToken` is cancelled, so it does not bypass classification.

### CR-02: Key test finishing after Clear writes the old key back (RESOLVED)

**File:** `UserControls/StepApiConfiguration.cs:326-382`

**Verification:** The key box and Clear are disabled before the await. `KeyTestGuard.IsResultCurrent` drops a result whose key no longer matches the box, and it treats an empty tested key as stale. The finally block re-enables the Test button, key box and Clear on every path. With the controls locked, the guard is effectively defense in depth: nothing in the step changes `keyBox.Text` programmatically during a test, because `OnEnterStepAsync` loads keys only once. See WR-12 for a side effect of the lock.

### WR-05: No result recorded / "ready" shown after an early fatal stop (RESOLVED, see WR-10)

**Files:** `Services/EmailGenerator.cs:93, 453-478`, `UserControls/StepComplete.cs:135-209`

**Verification:** `state.Result = result` is set before any work starts. `SavePartialAsync` clears `state.GeneratedThreads` when nothing was produced. The status row and summary text now distinguish four outcomes: fatal, cancelled, completed with errors, and clean. Test `FatalBeforeAnyThread_SetsStateResult` covers the stale-result case. The "completed with errors" wording is still misleading in some cases (WR-10).

### WR-06: `DistributeEmails` gives negative counts (RESOLVED, see WR-11)

**File:** `Services/EmailGenerator.cs:627-684`

**Verification:** The new code clamps negative weights to 0 and falls back to an equal split when all weights are 0. When there are fewer emails than storylines, the first `total` storylines get one email each. Otherwise every storyline gets 1 and the rest is split by largest remainder. With 0 storylines it throws a clear `InvalidOperationException`, which is recorded as a run error. The storyline loop returns early for `emailCount == 0`. The overflow case is still open as WR-11.

## Warnings

### WR-01: A failing batch discards the storyline's earlier batches, including on a fatal stop (OPEN)

**File:** `Services/EmailGenerator.cs` (`GenerateThreadsForStorylineAsync`, storyline catch block at 184-217)

**Issue:** `GenerateThreadsForStorylineAsync` builds the thread across batches, but it only returns the thread after the last batch. If batch N fails (`LlmCallException` from `GetValueOrThrow`, whether fatal, refused, truncated or invalid), batches 1..N-1 are dropped even though they were billed. In a fatal stop this contradicts the "Completed threads were saved" text. A 60-email storyline that fails on its fourth batch loses 45 paid-for emails.

**Fix:** Catch `LlmCallException` inside the batch loop. Record the error, stop looping, and return the partial thread if it has messages. For example, return `(thread, LlmCallException? stopReason)` and let the caller add the partial thread before it calls `SetFatalError` / `runCts.Cancel()`.

### WR-02: The key test checks only one model and is not invalidated when models change (OPEN)

**File:** `UserControls/StepApiConfiguration.cs:338, 411-448, 517-539`, `Models/WizardState.cs`

**Issue:** `GetKeyTestModelId` checks only the Story model for the text provider, or the Volume default for the other provider. Changing the tier combos, the provider, or the model catalog keeps `*KeyTested = true`. An inaccessible Volume model returns 404, which maps to non-fatal `BadRequest`, so items fail one at a time instead of the run stopping.

**Fix:** Test every model the run will use (Story and Volume). Reset `*KeyTested` for the text provider in `CboTierModel_SelectedIndexChanged`, `CboProvider_SelectedIndexChanged` and after `BtnConfigureModels_Click`.

### WR-03: Media access is never verified, and media failures are neither classified nor stopped (OPEN)

**File:** `Services/Ai/OpenAIMediaClient.cs:62-66, 112-116`, `Services/EmailGenerator.cs:517-521`

**Issue:** The CR-01 fix added only timeout handling. Status 401, 403, 404 and 429 `insufficient_quota` still escape as a raw `ClientResultException`. They are never fatal, so every planned image or voicemail fails separately. `RunIsolatedAsync` then logs the verbose multi-line `ex.Message`, which includes the response content and headers, once per item.

**Fix:** Map the status with `OpenAITextProvider.MapStatus`. For Auth/SpendLimit, throw an `LlmCallException` or a dedicated fatal media exception and turn media off for the rest of the run. Log a short sanitized message.

### WR-04: After a fatal stop the UI says "run again", but it cannot re-run (OPEN)

**File:** `UserControls/StepGeneration.cs:204-212`, `UserControls/StepComplete.cs:186-190`

**Issue:** The fatal branch still sets `_generationComplete = true`, and `OnEnterStepAsync` never resets it. The new `StepComplete.BuildSummaryText` repeats "Check your API key, billing or spend limit, then run again.", so the instruction now appears on two pages but still cannot be followed without restarting the app. The cancel path still restarts automatically into the same output folder when the user returns to the step.

**Fix:** As before: keep a separate "stopped" state that allows a re-run, or reset `_generationComplete` in `OnEnterStepAsync` when `_state.Result?.FatalError != null`. Warn before writing into a folder that already contains `.eml` files.

### WR-07: The run log repeats "Processing storyline: X" on every progress report (OPEN)

**File:** `UserControls/StepGeneration.cs` (`UpdateProgress`), `Services/EmailGenerator.cs:162-166`

**Issue:** `CurrentStoryline` is never cleared, so every snapshot re-logs the last storyline. This buries the new "skipped (request timed out)" lines that CR-01 adds.

**Fix:** Log only when the value changes (keep a `_lastLoggedStoryline` field).

### WR-08: AI-suggested date ranges are not validated (OPEN)

**File:** `Services/StorylineGenerator.cs`, `UserControls/StepGenerationConfig.cs`, `Services/EmailGenerator.cs:129-134`

**Issue:** A date before 1753 crashes the `DateTimePicker` assignment, and a reversed range reaches `EmailGenerator` unchecked.

**Fix:** Accept the AI range only if `start < end` and both dates fall within the `DateTimePicker` bounds. Clamp before assigning to the pickers.

### WR-09: Theme failure leaves the previous run's themes in place, and the DTO fallbacks can never apply (OPEN)

**File:** `UserControls/StepCharacters.cs`, `Services/ThemeGenerator.cs`

**Issue:** If theme generation fails, the previous run's `DomainThemes` are kept with the new cast. `ThemeDto` string defaults of `string.Empty` defeat the `??` fallbacks.

**Fix:** Reset `DomainThemes` before the theme call. Use `string.IsNullOrWhiteSpace(...) ? fallback : value`.

### WR-10 (NEW): The completion page says output is "usable for import" when the save failed or the run aborted with no output

**File:** `UserControls/StepComplete.cs:174-178, 184-201`, `Services/EmailGenerator.cs:453-462, 550-557, 601-608`

**Issue:** `DescribeStatus` and `BuildSummaryText` fall into "Completed with N error(s)" / "The saved .EML files are usable for import" whenever `Errors.Count > 0`. Several paths that land there produced no usable output:
- **The EML save itself failed** (disk full, access denied, invalid output folder). `SaveAndFinalizeAsync` catches the error and records "Failed to save EML files: ...", and the page still says the saved files are usable.
- **The run aborted in the outer `catch (Exception)`** before any storyline ran. Examples: "No storylines to generate emails for", or `Directory.CreateDirectory` failing on a bad output folder. `TotalEmailsGenerated == 0`, yet the status reads "Completed with 1 error(s)". `StepGeneration` also sets `_generationComplete = true` on this path, so the user can reach this page.
- **A fatal stop before any thread completed.** The fatal text says "Threads completed before the stop were saved to the output folder" even when none were. `RecordUnexpectedStop` has the same problem: it always appends "Completed threads were saved", even when `threads` is empty.

This is the WR-05 symptom (a misleading success summary) in a narrower form.

**Fix:** Separate aborts and save failures from per-item skips, and base the wording on what was actually written:
```csharp
// GenerationResult
public bool SaveFailed { get; set; }   // set in SaveAndFinalizeAsync's catch
public bool Aborted { get; set; }      // set in GenerateEmailsAsync's outer catch (Exception)

internal static string DescribeStatus(GenerationResult r) =>
    r.FatalError != null ? "Stopped by provider error"
    : r.WasCancelled ? "Cancelled"
    : r.Aborted || r.SaveFailed || r.TotalEmailsGenerated == 0 ? "Failed"
    : r.Errors.Count > 0 ? $"Completed with {r.Errors.Count} error(s)"
    : "Completed";
```
In `BuildSummaryText`, say "No emails were saved" when `TotalEmailsGenerated == 0`. Leave `_generationComplete` false in `StepGeneration` when the run aborted with no output.

### WR-11 (NEW): `DistributeEmails` can still produce negative counts or throw through integer overflow

**File:** `Services/EmailGenerator.cs:656-668`

**Issue:** The weights come from `Storyline.SuggestedEmailCount`. The "Est. Emails" column in `StepStorylines` is an editable, unbounded `int` cell; only AI output is clamped to 5..20. Two overflow paths remain:
- `exact[i] = remaining * weights[i] / weightSum;` evaluates `remaining * weights[i]` as unchecked `int` multiplication **before** the conversion to double. With 500 emails (the UI maximum) and one storyline edited to 5,000,000, the product exceeds `int.MaxValue` and wraps to a negative number. `floor` goes negative, so the distribution contains a negative count, which is exactly the WR-06 symptom. `leftover` can then exceed `n`, and `Take` caps it, so the sum no longer matches `total`. The doc comment's "Never negative; always sums to max(0, totalEmails)" is false for these inputs.
- `weights.Sum()` uses checked `int` addition, so two cells near 1.2 billion throw `OverflowException`. The run then aborts with "Arithmetic operation resulted in an overflow."

**Fix:** Do the arithmetic in `long`/`double`, and clamp the weights:
```csharp
var weights = storylines.Select(s => (long)Math.Clamp(s.SuggestedEmailCount, 0, 1_000_000)).ToArray();
...
double weightSum = weights.Sum();
exact[i] = (double)remaining * weights[i] / weightSum;
```
Also validate the grid column (range 0..1000) in `StepStorylines`. Add a test such as `DistributeEmails(S(5_000_000, 1), 500)`.

### WR-12 (NEW): The key-test lock can disable the key box and Clear for 10+ minutes with no way to cancel

**File:** `UserControls/StepApiConfiguration.cs:329-339, 374-381`, `Services/Ai/OpenAITextProvider.cs:41`, `Services/Ai/AnthropicTextProvider.cs:34, 52`

**Issue:** The CR-02 fix disables the key box and Clear until `TestKeyAsync` returns. The test runs with `CancellationToken.None` on the shared transport settings: a 10-minute `NetworkTimeout` / `HttpClient.Timeout` per attempt, with SDK retries on top. A stalled connection (proxy, captive portal, firewall blackhole) therefore locks that provider's key box, Clear and Test buttons for 10 minutes or longer, and the UI offers no cancel. The user cannot even clear a key they pasted by mistake until the request gives up. A free model retrieve does not need a 10-minute budget.

**Fix:** Give the key test its own short deadline, and treat a deadline hit as a timeout:
```csharp
using var testCts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
LlmKeyTestResult result;
try
{
    result = await LlmProviderFactory.Create(provider, key).TestKeyAsync(modelId, testCts.Token);
}
catch (OperationCanceledException)
{
    SetStatus(provider, "Key test timed out; check your connection and try again.", Color.Red);
    SetTested(provider, false);
    return; // finally re-enables the controls
}
```
Optionally let Clear cancel `testCts` instead of disabling Clear.

## Info

### IN-01: The legacy key file is truncated, not overwritten (OPEN)

**File:** `Services/ApiKeyStore.cs:132-134`
**Issue:** `File.WriteAllText(path, string.Empty)` truncates the file and does not overwrite the old key data, so the "overwrite" comment overstates what happens.
**Fix:** Write zeros with `Flush(true)` before deleting, or reword the comment.

### IN-02: Providers leak exceptions they do not map (OPEN)

**File:** `Services/Ai/AnthropicTextProvider.cs:56-83`, `Services/Ai/OpenAITextProvider.cs:47-73`
**Issue:** The updated `ILlmProvider` doc now promises "never throws for API/HTTP failures (including transport timeouts)". SDK `JsonException`, base `AnthropicException` and `InvalidOperationException` still escape.
**Fix:** Add a final `catch (Exception ex)` that returns `Failed(LlmFailureKind.Unknown, ...)` with a sanitized message.

### IN-03: Some Anthropic stop reasons are not classified as truncation (OPEN)

**File:** `Services/Ai/AnthropicTextProvider.cs` (`Classify`)
**Issue:** `model_context_window_exceeded` falls to `Failed/Unknown`.
**Fix:** Map it to `LlmOutcome.Truncated`.

### IN-04: HttpClient and handlers are never disposed (OPEN)

**File:** `Services/Ai/AnthropicTextProvider.cs:29-37`, `Models/WizardState.cs`, `UserControls/StepApiConfiguration.cs:339`
**Issue:** Every gateway creation and every Test click creates an `HttpClient` that is never disposed.
**Fix:** Cache one provider per (kind, key), or make the providers `IDisposable`.

### IN-05: Token totals are read without the lock (OPEN)

**File:** `Models/TokenUsageTracker.cs`, `UserControls/StepGeneration.cs:150-157`, `UserControls/StepComplete.cs:162-165`
**Issue:** The `decimal` total and the token counts are read without `_lock`.
**Fix:** Add a locked `GetSnapshot()`.

### IN-06: Elapsed-time display wraps after an hour (OPEN)

**File:** `UserControls/StepComplete.cs:157`, `UserControls/StepGeneration.cs:237`
**Issue:** `mm\:ss` drops the hours.
**Fix:** Use `@"hh\:mm\:ss"`.

### IN-07: The Effort field accepts any text (OPEN)

**File:** `UserControls/ModelConfigurationDialog.cs`, both text providers
**Issue:** An invalid value causes a 400 on every OpenAI call, and is silently dropped for Anthropic.
**Fix:** Use a combo box of allowed values, or validate in `BtnOk_Click`.

### IN-08: CLAUDE.md no longer matches the code (OPEN)

**File:** `CLAUDE.md`
**Issue:** It still describes .NET 8, `OpenAIService`, a Base64 key file and DALL-E. The code now targets net10.0 and uses `LlmGateway`, the DPAPI key store and gpt-image.
**Fix:** Regenerate the stack and architecture sections.

### IN-09 (NEW): When there are fewer emails than storylines, the extra storylines are dropped without notice

**File:** `Services/EmailGenerator.cs:643-649, 149-151`
**Issue:** When `TotalEmailCount < Storylines.Count`, only the first `total` storylines get an email. The rest are skipped silently, regardless of their weights, and nothing appears in the log or in `Errors`. The storylines the user reviewed simply do not appear in the corpus.
**Fix:** Report "N storyline(s) received no emails (email count lower than storyline count)" as a progress/log note. Better, validate `TotalEmailCount >= Storylines.Count` in `StepGenerationConfig.ValidateStepAsync`. Consider giving the slots to the highest-weight storylines rather than the first ones.

### IN-10 (NEW): Generator timeout tests bypass the providers, and the unexpected-stop branch is effectively unreachable

**File:** `ReelDiscovery.Tests/Generation/EmailGeneratorTimeoutTests.cs:17-67`, `Services/EmailGenerator.cs:441-446`
**Issue:**
- The generator tests throw `TaskCanceledException` from `FakeLlmProvider`, which is exactly what the fixed providers no longer do. They cover only the defensive layer. No test drives a real provider timeout through the gateway to confirm it becomes a "skipped (failed: Network)" storyline.
- After the fix, the only OCEs that reach the outer catch come from `runCts` (`WaitAsync(runToken)` or `ThrowIfRunStopped`). The "Run stopped unexpectedly" branch of `RecordUnexpectedStop` is therefore reachable only through a contract violation, so it is covered only by a direct unit test.

**Fix:** Add one integration test that wires `OpenAITextProvider` with the timeout `FakeHttpHandler` into `EmailGenerator`, and assert that the storyline is skipped with a Network classification and the run continues.

---

_Reviewed: 2026-09-30T18:00:00Z (re-review of 2026-09-30T14:33:03Z)_
_Reviewer: Claude (gsd-code-reviewer)_
_Depth: standard_
