---
phase: 01-foundation-provider-gateway
reviewed: 2026-09-30T14:33:03Z
depth: standard
files_reviewed: 36
files_reviewed_list:
  - Helpers/DateHelper.cs
  - Models/AIModelConfig.cs
  - Models/GenerationConfig.cs
  - Models/GenerationResult.cs
  - Models/TokenUsageTracker.cs
  - Models/WizardState.cs
  - Program.cs
  - ReelDiscovery.csproj
  - global.json
  - Services/Ai/AnthropicSpendLimitHandler.cs
  - Services/Ai/AnthropicTextProvider.cs
  - Services/Ai/ILlmGateway.cs
  - Services/Ai/ILlmProvider.cs
  - Services/Ai/JsonSchemaRegistry.cs
  - Services/Ai/LlmGateway.cs
  - Services/Ai/LlmProviderFactory.cs
  - Services/Ai/LlmProviderKind.cs
  - Services/Ai/LlmProviderRequest.cs
  - Services/Ai/LlmRequest.cs
  - Services/Ai/LlmResult.cs
  - Services/Ai/OpenAIMediaClient.cs
  - Services/Ai/OpenAITextProvider.cs
  - Services/Ai/QuotaAwareRetryPolicy.cs
  - Services/ApiKeyStore.cs
  - Services/CharacterGenerator.cs
  - Services/EmailGenerator.cs
  - Services/StorylineGenerator.cs
  - Services/ThemeGenerator.cs
  - UserControls/ModelConfigurationDialog.cs
  - UserControls/StepApiConfiguration.cs
  - UserControls/StepCharacters.cs
  - UserControls/StepComplete.cs
  - UserControls/StepGeneration.cs
  - UserControls/StepGenerationConfig.cs
  - UserControls/StepStorylines.cs
  - UserControls/StepTopicInput.cs
findings:
  critical: 2
  warning: 9
  info: 8
  total: 19
status: issues_found
---

# Phase 01: Code Review Report

**Reviewed:** 2026-09-30T14:33:03Z
**Depth:** standard
**Files Reviewed:** 36
**Status:** issues_found

## Summary

I reviewed the provider gateway (`Services/Ai/*`), the DPAPI key store, the ported generators, `EmailGenerator`'s parallel pipeline and fatal-stop/save path, and the wizard steps that use them.

Several parts hold up. Outcome classification, the spend-limit handling in both SDKs, locking of shared counters and errors, and first-fatal-wins recording are sound. No path I found leaks an API key into messages.

Two defects must be fixed before shipping:

1. **Timeouts are treated as user cancellation.** Both providers and `EmailGenerator` rethrow every `OperationCanceledException` without checking which token fired. A transport timeout (the System.ClientModel `NetworkTimeout` throws `TaskCanceledException`, "The operation was cancelled because it exceeded the configured timeout", confirmed in the 1.15.0 binary) or an `HttpClient.Timeout` ends the run early without saying so. It skips every later pass and can still report "GENERATION COMPLETE!".
2. **Clear can be undone by an in-flight key test.** A test that finishes after Clear, or after the user edits the key, writes the old key back to state and to `keys.dat`. This undoes the user's "Clear removes a saved key" action.

The warnings are mostly about work that is billed but thrown away, key-test coverage (the Volume model and media access are never checked), and wizard flows after a fatal stop.

## Critical Issues

### CR-01: Transport timeouts are treated as cancellation, so the run aborts silently and reports success

**File:** `Services/Ai/OpenAITextProvider.cs:54-57`, `Services/Ai/AnthropicTextProvider.cs:62-65`, `Services/Ai/OpenAIMediaClient.cs:35-61`, `Services/EmailGenerator.cs:178-181, 428-445, 466-469`, `UserControls/StepGeneration.cs:204-254`

**Issue:** Every layer uses `catch (OperationCanceledException) { throw; }` without checking which token was cancelled. The OpenAI transport enforces `NetworkTimeout = 10 min` (`OpenAITextProvider.CreateDefaultOptions`, which the media client also uses). When it expires, System.ClientModel throws `TaskCanceledException`. Anthropic's `HttpClient.Timeout` does the same. The exception then travels up:
- The provider rethrows it. This breaks the `ILlmProvider` contract ("never throws for API/HTTP failures").
- The storyline lambda (line 178) or `RunIsolatedAsync` (line 466) rethrows it.
- `Task.WhenAll` throws, and control jumps to the outer `catch (OperationCanceledException)` at line 428.

At that point `ct.IsCancellationRequested` is false and `FatalError` is null, so `WasCancelled = false`. No error is added. Every remaining pass (attachments, images, calendar, voicemail) is skipped. `StepGeneration` finds `FatalError == null`, `WasCancelled == false` and `Errors.Count == 0`, and logs "GENERATION COMPLETE!". One slow image request is enough to drop every later enrichment item across the whole corpus without any notice.

**Fix:** Treat an OCE as cancellation only when the caller's token fired. Map everything else to a classified failure:
```csharp
// Providers (both)
catch (OperationCanceledException) when (ct.IsCancellationRequested)
{
    throw;
}
catch (OperationCanceledException)
{
    return Failed(LlmFailureKind.Network, "Request to OpenAI timed out");
}

// EmailGenerator storyline lambda and RunIsolatedAsync
catch (OperationCanceledException) when (runCts.IsCancellationRequested)
{
    throw;
}
// ...a non-run OCE then falls through to catch (Exception) and is recorded as a skip

// Outer catch: never exit silently
catch (OperationCanceledException)
{
    result.WasCancelled = ct.IsCancellationRequested;
    if (!result.WasCancelled && result.FatalError == null)
        result.AddError("Run stopped unexpectedly (request timed out)");
    ...
}
```
Apply the same guard in `OpenAIMediaClient` (catch a non-`ct` OCE and return `null`).

### CR-02: A key test that finishes after Clear (or after the key is edited) writes the old key back to disk

**File:** `UserControls/StepApiConfiguration.cs:314-364, 366-391`

**Issue:** `TestKeyAsync` captures `key` before the await and disables only the Test button. Clear and the key TextBox stay usable while the request is in flight. With 3 SDK retries and a 10-minute network timeout, a test can take a long time.
- If the user clicks **Clear** during the test, `ClearKey` wipes `keys.dat` and state. The test then succeeds and runs `SetKeyState(provider, key, tested: true)` and `_keyStore.SetKey(provider, key)`. The key the user just removed is saved again and marked as tested.
- If the user **edits** the key during the test, `OnKeyTextChanged` sets tested = false. The old test then finishes and marks the old key as tested in state, while the textbox shows the new key. The old key becomes the active credential without the user knowing.

**Fix:** Disable the key box and the Clear button while testing. After the await, drop the result if the key changed:
```csharp
_txtKey.Enabled = false; clearButton.Enabled = false;
...
var result = await LlmProviderFactory.Create(provider, key).TestKeyAsync(modelId);
if (GetKeyBox(provider).Text.Trim() != key)
{
    SetStatus(provider, "Key changed during test; test again.", Color.Gray);
    return; // do not SetKeyState / SetKey
}
```

## Warnings

### WR-01: A failing batch discards the storyline's earlier batches, including on a fatal stop

**File:** `Services/EmailGenerator.cs:659-729, 182-204`

**Issue:** `GenerateThreadsForStorylineAsync` builds the thread across batches, but it only returns the thread after the last batch. If batch N fails (`LlmCallException` from `GetValueOrThrow`, whether fatal, refused, truncated or invalid), batches 1..N-1 are dropped even though they were billed. In a fatal stop this contradicts the comment at line 184 and the UI text "Completed threads were saved". With 15 emails per batch, a 60-email storyline that fails on its fourth batch loses 45 paid-for emails.

**Fix:** Catch `LlmCallException` inside the batch loop. Record the error, stop looping, and return the partial thread if it has messages; rethrow fatal errors only after returning or saving it. For example, return `(thread, LlmCallException? stopReason)` and let the caller add the partial thread before it calls `SetFatalError` / `runCts.Cancel()`.

### WR-02: The key test checks only one model and is not invalidated when models change

**File:** `UserControls/StepApiConfiguration.cs:329-330, 411-430, 499-521`, `Models/WizardState.cs:75-78`

**Issue:** `GetKeyTestModelId` returns the Story model for the text provider and the Volume default for the other provider. So:
1. The text provider's **Volume** model is never checked.
2. If the user tests a key while its provider is not selected (Volume default checked) and then switches `TextProvider` to it, the Story model is never checked either.
3. Changing either tier combo, or editing the catalog in Configure Models, keeps `*KeyTested = true`.

Every Volume call to an inaccessible model returns 404, which maps to `BadRequest`. That is not fatal, so each storyline and attachment fails one at a time instead of the run stopping.

**Fix:** Test every model the run will use (Story and Volume for the text provider). Model retrieve is free. Reset `*KeyTested` for the text provider in `CboTierModel_SelectedIndexChanged`, `CboProvider_SelectedIndexChanged` and after `BtnConfigureModels_Click`. Alternatively, treat `BadRequest` from a 404 model-not-found response as fatal in the gateway.

### WR-03: Media access is never verified, and media failures are neither classified nor stopped

**File:** `Models/WizardState.cs:44, 75-78`, `Services/Ai/OpenAIMediaClient.cs:52-60, 97-105`, `Services/EmailGenerator.cs:483-487`

**Issue:** `IsMediaAvailable` only requires that the OpenAI key passed a *chat* model retrieve. It does not check access to the image model (`gpt-image-*`, which requires OpenAI organization verification) or to `tts-1`. `OpenAIMediaClient` handles only status 400. A 401, 403, 404 or 429 `insufficient_quota` becomes a raw `ClientResultException`, which lands in `RunIsolatedAsync`'s generic `catch (Exception)`:
- These errors are never fatal, so every planned image or voicemail fails separately.
- `ex.Message` is logged verbatim. For System.ClientModel that message is multi-line and includes the response content and response headers, and it is repeated once per item.

**Fix:** In `OpenAIMediaClient`, map `ClientResultException` with `OpenAITextProvider.MapStatus`. Throw an `LlmCallException` or a dedicated `MediaFatalException` for Auth/SpendLimit, so `RunIsolatedAsync` can turn media off for the rest of the run (not necessarily the text run). Return a short `SafeMessage` rather than `ex.Message`. Consider probing the image model with `GetModelAsync(_imageModel)` when testing the OpenAI key.

### WR-04: After a fatal stop the UI says "run again", but it cannot re-run

**File:** `UserControls/StepGeneration.cs:204-212, 330-337`

**Issue:** The fatal branch sets `_generationComplete = true` and tells the user to "Check your API key, billing or spend limit, then run again." `OnEnterStepAsync` starts only when `!_generationComplete`, and nothing ever resets the flag. Going back, fixing the key and returning to the step does not start a run, so the user has to restart the app. The cancel path does the opposite: coming back to the step starts a new run automatically, into the same output folder as the partial one, without confirmation.

**Fix:** Keep `_generationComplete = false` on a fatal stop and show a separate "stopped" state. Alternatively, reset `_generationComplete`/`_txtLog` in `OnEnterStepAsync` when the previous result had a `FatalError`. Before an automatic restart, warn if the output folder already contains `.eml` files.

### WR-05: No result is recorded, and the summary says the data is "ready", after an early fatal stop or an outer exception

**File:** `Services/EmailGenerator.cs:434-451`, `UserControls/StepComplete.cs:127-167`

**Issue:** `state.Result` is set only inside `SaveAndFinalizeAsync`. If the fatal error happens before any thread completes (for example, the first storyline gets a 401), `threads.IsEmpty` skips the save, so `state.Result` stays null or holds the previous run's result. The same happens on the outer `catch (Exception)` path. StepGeneration still allows Next (`_generationComplete = true`), and StepComplete shows an empty or stale grid. When stats are shown, `_lblSummary` always reads "Your email dataset is ready for import", even after a fatal stop. `FatalError` and `Errors` never appear on the summary page.

**Fix:** In both catch paths, always set `state.Result = result` and `result.ElapsedTime`. In `StepComplete.LoadStatistics`, show `FatalError`, `WasCancelled` and the error count, and change the summary text accordingly.

### WR-06: `DistributeEmails` gives negative counts when there are more storylines than emails

**File:** `Services/EmailGenerator.cs:580-614`

**Issue:** With `TotalEmailCount = 5` (the UI minimum) and 10 storylines (the default), each storyline gets `Math.Max(1, ...)` = 1. The sum is 10, so `diff = -5` and `distribution[0] = -4`. Storyline 0 then yields no emails, `Interlocked.Add(ref completedEmails[0], -4)` drives progress negative, and the run produces 9 emails instead of 5. If there are no storylines, `totalEmails / storylines.Count` throws `DivideByZeroException`, which is reported only as "Attempted to divide by zero."

**Fix:** Clamp and redistribute the difference across storylines without letting any count go below 0 (or skip storylines when there are more storylines than emails). Guard `storylines.Count == 0` with a clear error. Validate `TotalEmailCount >= Storylines.Count` in `StepGenerationConfig.ValidateStepAsync`, or state that each storyline gets at least one email.

### WR-07: The run log repeats "Processing storyline: X" on every progress report

**File:** `UserControls/StepGeneration.cs:319-322`, `Services/EmailGenerator.cs:156-160`

**Issue:** `CurrentStoryline` is set once and never cleared, and every `Report()` snapshot copies it. `UpdateProgress` appends a log line whenever `CurrentStoryline` is non-empty. So every later progress report adds another "Processing storyline: <last storyline>" line: each storyline completion, each attachment start and finish, and each image. On a 5K-email run that is thousands of misleading lines, which bury the orange and red skip lines the phase added.

**Fix:** Log only when the value changes (keep a `_lastLoggedStoryline` field). Alternatively, clear `CurrentStoryline` after reporting it, or report a separate event for storyline starts.

### WR-08: AI-suggested date ranges are not validated, which can crash the config step or reverse date windows

**File:** `Services/StorylineGenerator.cs:241-248`, `UserControls/StepGenerationConfig.cs:506-514`, `Services/EmailGenerator.cs:128-133`

**Issue:** The prompt asks for "dates that fit the setting" of movies and books. Fictional or historical settings can produce dates like `0298-01-01` that `DateTime.TryParse` accepts. `StepGenerationConfig.OnEnterStepAsync` then sets `DateTimePicker.Value`, whose `MinDate` is 1753-01-01, and gets `ArgumentOutOfRangeException`. A reversed range (start after end) passes through too, because the `>=` check at line 568 runs only when AI dates are off. `EmailGenerator` then uses the reversed window as-is.

**Fix:** In `StorylineGenerator`, accept the range only if `start < end` and both fall within `DateTimePicker.MinimumDateTime..MaximumDateTime`. Otherwise leave both null. Clamp before assigning to the pickers as a second guard.

### WR-09: Theme failure leaves the previous run's themes in place, and the DTO fallbacks can never apply

**File:** `UserControls/StepCharacters.cs:314-327`, `Services/ThemeGenerator.cs:93-94, 108-109`

**Issue:**
- `_state.Characters` is replaced before theme generation. If `GenerateThemesForDomainsAsync` throws (fatal `LlmCallException` or a timeout), `_state.DomainThemes` still holds the previous generation's domains. `CanMoveNext` is still true because there are at least 2 characters, so emails and documents use themes that do not match the new cast.
- `ThemeDto.OrganizationName` and `ThemeName` are non-null `string.Empty` by default, so `t.OrganizationName ?? ...` and `t.ThemeName ?? "Corporate"` never fall back. An empty string from the model becomes an empty organization or theme name.

**Fix:** Reset `_state.DomainThemes = new(StringComparer.OrdinalIgnoreCase)` before the theme call, or assign characters and themes together only after both succeed. Use `string.IsNullOrWhiteSpace(t.OrganizationName) ? fallback : t.OrganizationName`.

## Info

### IN-01: The legacy key file is truncated, not overwritten

**File:** `Services/ApiKeyStore.cs:132-134`
**Issue:** `File.WriteAllText(path, string.Empty)` truncates the file. The NTFS clusters that held the Base64 key are freed but not overwritten, so the "overwrite" comment (D-09) overstates what happens.
**Fix:** Write a same-length buffer of zeros with `FileStream` and `Flush(true)` before deleting, or reword the comment.

### IN-02: Providers leak exceptions they do not map

**File:** `Services/Ai/AnthropicTextProvider.cs:54-79`, `Services/Ai/OpenAITextProvider.cs:45-68`
**Issue:** Only API, IO and `HttpRequestException` exceptions are caught. SDK deserialization errors (`JsonException`, base `AnthropicException`) and `InvalidOperationException` escape, which breaks the documented "never throws" contract. `EmailGenerator` then logs them as generic failures rather than classified outcomes.
**Fix:** Add a final `catch (Exception ex)` that returns `Failed(LlmFailureKind.Unknown, ...)` with a sanitized message.

### IN-03: Some Anthropic stop reasons are not classified as truncation

**File:** `Services/Ai/AnthropicTextProvider.cs:251-270`
**Issue:** `model_context_window_exceeded` is effectively a truncation, but it falls to `Failed/Unknown`. `pause_turn` also maps to `Unknown`.
**Fix:** Map `model_context_window_exceeded` to `LlmOutcome.Truncated`.

### IN-04: HttpClient and handlers are never disposed

**File:** `Services/Ai/AnthropicTextProvider.cs:29-37`, `Models/WizardState.cs:83-91`, `UserControls/StepApiConfiguration.cs:330`
**Issue:** Every `CreateGateway()` call (each Regenerate click and each run) and every Test click creates a new `HttpClient` and `HttpClientHandler`, and none are disposed. Neither provider implements `IDisposable`.
**Fix:** Cache one provider per (kind, key) in `WizardState`, or make the providers `IDisposable` and dispose them after test and generation.

### IN-05: Token totals are read without the lock

**File:** `Models/TokenUsageTracker.cs:8-10`, `UserControls/StepGeneration.cs:151-158`
**Issue:** `TotalCost` (a 16-byte `decimal`) and the token totals are read without `_lock`. Continuations currently resume on the UI thread, so the risk is low, but a torn or inconsistent snapshot is possible if continuations ever run on the thread pool.
**Fix:** Add a locked `GetSnapshot()` that returns all three values together.

### IN-06: Elapsed-time display wraps after an hour

**File:** `UserControls/StepComplete.cs:154`, `UserControls/StepGeneration.cs:237`
**Issue:** The `mm\:ss` format drops hours, so a 5K-email run that takes 1h05m shows "05:00".
**Fix:** Use `@"hh\:mm\:ss"` or `result.ElapsedTime.ToString(@"d\.hh\:mm\:ss")`.

### IN-07: The Effort field accepts any text

**File:** `UserControls/ModelConfigurationDialog.cs:143-149`, `Services/Ai/OpenAITextProvider.cs:114-121`, `Services/Ai/AnthropicTextProvider.cs:290-298`
**Issue:** An invalid Effort value is sent to OpenAI as `reasoning_effort`, and every call returns 400 (non-fatal, per-item skip). For Anthropic, an unknown value is silently dropped.
**Fix:** Make the column a combo box of allowed values (blank, none, low, medium, high, xhigh, max), or validate in `BtnOk_Click`.

### IN-08: CLAUDE.md no longer matches the code

**File:** `CLAUDE.md` (Technology Stack, Architecture and Error Handling sections)
**Issue:** CLAUDE.md still describes .NET 8, `OpenAIService` (deleted), `TelemetryService`, a Base64 key file and DALL-E. The code now targets net10.0-windows with global.json pinned to 10.0.401, uses `LlmGateway`, the DPAPI `ApiKeyStore` and gpt-image. The retarget was intentional (01-01-PLAN), but the conventions file will mislead future agents and reviewers.
**Fix:** Regenerate the stack and architecture sections of CLAUDE.md.

---

_Reviewed: 2026-09-30T14:33:03Z_
_Reviewer: Claude (gsd-code-reviewer)_
_Depth: standard_
