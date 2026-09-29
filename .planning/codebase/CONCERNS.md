# Codebase Concerns

**Analysis Date:** 2026-09-29

Scope: whole repository (`bin/`, `obj/`, `.vs/` excluded). .NET 8 WinForms app (`ReelDiscovery.csproj`, `net8.0-windows`), about 9,700 lines of C#, no test project.

## Tech Debt

**`EmailGenerator` is a 2,000-line god class:**
- Issue: A single class holds orchestration, prompt construction, DTO mapping, signature repair, every attachment generator (Word/Excel/PowerPoint/image/voicemail/calendar), HTML image injection, and all JSON response DTOs.
- Files: `Services/EmailGenerator.cs` (2,036 lines)
- Impact: It is hard to change one phase without touching others, and it cannot be unit-tested in isolation because `OpenAIService` is a concrete class with no interface and `OfficeDocumentService`/`CalendarService` are `new`'d in the constructor (lines 21-26).
- Fix approach: Split it into `EmailThreadGenerator` (batches + DTO conversion), `AttachmentGenerator` (documents/chains), `MediaGenerator` (image/voicemail), `MeetingDetector` (calendar), and a `SignatureCorrector` static helper. Put an `IOpenAIService` interface in front of `Services/OpenAIService.cs` and inject the dependencies.

**Dead code in `EmailGenerator`:**
- Issue: These private methods are never called: `SelectEmailsForAttachments` (line 1095), `GenerateAttachmentAsync` (line 1345), `GenerateImageForEmailAsync` (line 1636), and `GenerateVoicemailForEmailAsync` (line 1851). Their DTO `ImageSuggestionResponse` is also unused. They come from an older approach where attachments were chosen after generation, which the "Planned*" flow has replaced.
- Files: `Services/EmailGenerator.cs`
- Impact: About 400 lines of duplicated logic. For example, the HTML image-insertion code is copied between `GeneratePlannedImageAsync` (lines 1227-1280) and `GenerateImageForEmailAsync` (lines 1691-1733). Fixes applied to one copy drift from the other.
- Fix approach: Delete the unused methods. Move HTML image insertion into `Helpers/HtmlEmailFormatter.cs` (for example `InsertInlineImage(string html, string imgHtml)`).

**Other unused or legacy members:**
- `DateHelper.DistributeDatesForThread` and `AdjustToBusinessHours` in `Helpers/DateHelper.cs` (lines 7-78) are not called from anywhere else.
- `WizardState.AvailableModels` in `Models/WizardState.cs` (lines 58-66) is marked "Legacy" and is unused.
- The legacy overload `GetJsonCompletionAsync<T>(string, string, CancellationToken)` in `Services/OpenAIService.cs` (lines 175-180) is kept "for backward compatibility".
- `OpenAIService._apiKey` is stored but never read (`Services/OpenAIService.cs`, line 14).
- The `OpenAIService(string apiKey, string model)` constructor has no usage tracking, so costs go unrecorded when `SelectedModelConfig` is null (`Models/WizardState.cs`, lines 48-56).
- Fix approach: Remove them, or connect them if they were meant to be used.

**Misleading "incremental saving" comment:**
- Issue: `Services/EmailGenerator.cs` lines 76 and 347 say "EML service for incremental saving" and "Save any remaining EML files (threads that weren't saved incrementally)". In reality nothing is saved until every phase has finished. All `.eml` files are written in one pass at line 359.
- Impact: The comment misleads maintainers. The real behavior causes the data-loss bug described under Known Bugs.
- Fix approach: Either write each thread's EML right after its attachments are complete, or fix the comments.

**Prompt text hard-coded in C# string literals:**
- Issue: Very large prompts are built inline with `$@"..."` interpolation.
- Files: `Services/EmailGenerator.cs` (`BuildEmailSystemPrompt` lines 561-660, `BuildBatchUserPrompt` lines 765-873), `Services/StorylineGenerator.cs`, `Services/CharacterGenerator.cs`, `Services/ThemeGenerator.cs`
- Impact: Every prompt tweak needs a recompile, and the JSON schema in the prompt is not linked to the DTO classes, so changing a DTO can silently break parsing.
- Fix approach: Move prompts to embedded resource templates, or at least a `Prompts/` static class. Consider OpenAI structured outputs (JSON schema), which would guarantee the DTO shape.

**Duplicated retry logic:**
- Issue: The 429/503 retry blocks are copy-pasted four times in `GetCompletionAsync`, `GetJsonCompletionAsync`, `GenerateImageAsync`, and `GenerateSpeechAsync`.
- Files: `Services/OpenAIService.cs` (lines 71-173, 193-236, 247-301)
- Fix approach: Use a single `ExecuteWithRetryAsync<T>(Func<Task<T>>)` helper, or configure `OpenAIClientOptions.RetryPolicy` (System.ClientModel has built-in retry).

## Known Bugs

**`replyToIndex` is read as an absolute index, but the prompt asks for a batch-relative one:**
- Symptoms: For storylines with more than 15 emails (`MaxEmailsPerBatch = 15`), replies in batch 2 and later quote the wrong email, usually an early email from batch 1, instead of the message they actually reply to.
- Files: `Services/EmailGenerator.cs`. The prompt says "use replyToIndex relative to THIS batch, starting at 0" (lines 679, 684, 832), but `ConvertDtoToEmailMessage` indexes `thread.Messages[e.ReplyToIndex]` over the whole thread (lines 926-929).
- Trigger: Any storyline allocated more than 15 emails, which is common at higher email counts.
- Fix: Pass the batch's starting offset into `ConvertDtoToEmailMessage` and resolve `thread.Messages[batchOffset + e.ReplyToIndex]`.

**One failed attachment, image, calendar invite, or voicemail aborts the whole run, and nothing is saved:**
- Symptoms: Only the storyline phase catches exceptions per item (lines 124-136). The document, image, calendar, and voicemail task lambdas (lines 159-207, 224-257, 278-291, 307-338) have `try/finally` but no `catch`. One exception fails `Task.WhenAll`, the outer `catch (Exception ex)` at line 402 returns a result with one error, and `SaveAllEmailsAsync` (line 359) never runs. All generated emails and API spend are lost.
- Files: `Services/EmailGenerator.cs`
- Triggers:
  - `GetJsonCompletionAsync` throws after four JSON failures or on non-429/503 HTTP errors (500, 502, 401).
  - `new DateTime(..., hour, minute, 0)` in `DetectAndAddCalendarInviteAsync` (line 1796) throws `ArgumentOutOfRangeException` if the model returns an hour of 24 or more, or a minute of 60 or more.
- Cancellation has the same effect: `OperationCanceledException` returns `WasCancelled` without saving any partial results (lines 396-401).
- Fix: Catch per item inside each task lambda and add the error to `result.Errors` under a lock. Validate the hour and minute before building the `DateTime`. Save completed threads before or independently of the enrichment phases.

**"Verified: N .eml files" reports 0 when sender folders are enabled:**
- Symptoms: After a successful run, the log says `Verified: 0 .eml files in output folder`.
- Files: `UserControls/StepGeneration.cs` (around line 232) uses `Directory.GetFiles(result.OutputFolder, "*.eml")` with no `SearchOption.AllDirectories`. `OrganizeBySender` defaults to `true` (`Models/GenerationConfig.cs`, line 16), which puts the files in subfolders (`Services/EmlFileService.cs`, lines 139-145).
- Fix: Use `SearchOption.AllDirectories`.

**Shared `System.Random` used from concurrent tasks:**
- Symptoms: `System.Random` is not thread-safe. Concurrent `Next()` calls can corrupt its internal state and make it return 0 forever, which quietly removes randomness (always the first character, the first chain, and so on).
- Files:
  - `Services/EmailGenerator.cs`, line 13: the `_random` field is used inside parallel lambdas at lines 885, 1136, 1144, and 1176.
  - `Helpers/DateHelper.cs`, line 5: a static `_random` used by `RandomDateInRange`, which is called from parallel storyline tasks.
  - `Services/CharacterGenerator.cs`, line 9.
- Fix: Replace them with `Random.Shared`.

**Non-thread-safe mutation in parallel sections:**
- `result.Errors` is a plain `List<string>` (`Models/GenerationResult.cs`, line 15). It is appended from parallel storyline tasks without a lock (`Services/EmailGenerator.cs`, line 127).
- `DocumentChainState.VersionNumber++` (line 1170) runs concurrently on shared chain objects. Two documents can get the same version label and file name.
- `progressData` is one mutable `GenerationProgress` object. It is passed to `IProgress.Report` repeatedly and mutated at the same time (for example `progressData.CurrentOperation` at line 355 without a lock). The UI thread reads it while workers write to it.
- Fix: Lock around `Errors.Add`, or use a `ConcurrentQueue`. Lock chain updates. Report an immutable snapshot (`progress.Report(progressData with {...})`, or clone it).

**`DistributeEmails` can produce zero or negative counts, or divide by zero:**
- Files: `Services/EmailGenerator.cs`, lines 417-451
- Issue: Proportional rounding with `Math.Max(1, ...)` can push the sum above the total. All of the difference is then subtracted from `distribution[0]`, which can go to 0 or below. With 0 storylines, `totalEmails / storylines.Count` throws.
- Fix: Use the largest-remainder method and guard against an empty `storylines` list.

**Model output count mismatches are not handled:**
- Files: `Services/EmailGenerator.cs`, lines 494-550
- Issue: `emailsGenerated += response.Emails.Count` trusts whatever count the model returns. If the model returns more emails than requested, the next `batchSize` can be 0 or negative, and date interpolation fractions go past 1. If it returns fewer, the storyline ends up short with no retry.
- Fix: Clamp or trim `response.Emails` to `batchSize`, and top up any shortfall.

**Other crash risks in `ConvertDtoToEmailMessage`:**
- `characters.Where(c => c.Id != fromChar.Id).First()` (line 896) throws if there is only one character.
- `DateTime.TryParse(e.SentDateTime, ...)` (line 913) depends on the current culture and ignores time zones. Parsed dates are not clamped to the batch range or sorted chronologically, so thread order can contradict `SequenceInThread`.

**Elapsed-time display wraps after one hour:**
- Files: `UserControls/StepGeneration.cs` (`{result.ElapsedTime:mm\\:ss}`), `UserControls/StepComplete.cs` (`ElapsedTime.ToString(@"mm\:ss")`), `Services/TelemetryService.cs` (`{duration:mm\\:ss}`)
- Fix: Use `hh\:mm\:ss`, or `[h]:mm:ss` logic.

**`TotalAttachmentsGenerated` counts only Office documents:**
- Files: `Services/EmailGenerator.cs`, lines 210 and 385. Images, calendar invites, and voicemails are left out, so the "Attachments created" and "Document Attachments" numbers look lower than the real total.

## Security Considerations

**Slack webhook URL hard-coded in source and git history (HIGH):**
- Risk: A live Slack incoming-webhook URL is a constant in `Services/TelemetryService.cs` (line 14). The origin remote points at GitHub (`github.com/ghanderson77-ops/ReelDiscovery`), and the published single-file exe embeds it too. Commit `fbb03d8` ("Revert to hardcoded webhook URL for telemetry") put it back after an earlier change. Anyone who has the URL can post arbitrary messages into the workspace channel (spam or phishing).
- Current mitigation: None.
- Recommendations: Rotate or revoke the webhook in Slack now. Send telemetry through a server-side relay you control (for example an Azure Function with rate limiting) rather than a raw Slack webhook, and never ship the destination secret in a client binary. Removing it from the current code does not remove it from history, so rotation is required.

**API key stored with Base64 only:**
- Risk: The OpenAI API key is written to `%LOCALAPPDATA%\ReelDiscovery\reeldiscovery.settings` as plain Base64 (`UserControls/StepApiConfiguration.cs`, lines 348-378; the code comment admits "not secure"). Any process or user with access to the profile can recover it.
- Recommendations: Use DPAPI (`System.Security.Cryptography.ProtectedData.Protect` with `DataProtectionScope.CurrentUser`) or the Windows Credential Manager. Add a "forget key" option.

**Telemetry sends user-entered topic text:**
- Risk: `SendGenerationEventAsync` sends the first 50 characters of the user's topic to Slack (`Services/TelemetryService.cs`, lines 85-91). The class calls itself "non-identifying", but topics can contain client or matter names in e-discovery use.
- Also: `StepComplete.OnEnterStepAsync` (`UserControls/StepComplete.cs`, lines 181-195) sends the event every time the step is entered, not once per run.
- Recommendations: Leave the topic out, or hash it. Send once per `GenerationResult`, for example with a flag on the result. Make the opt-in dialog text (`Forms/TelemetryOptInDialog.cs`) disclose exactly which fields are sent.

**Silent exception swallowing:**
- Files: bare `catch { }` in `Services/OpenAIService.cs` line 65 (`TestConnectionAsync` hides the real error, such as a bad key, bad model, or network failure), `Services/TelemetryService.cs` lines 43/64/94/115, `UserControls/StepApiConfiguration.cs` lines 357/374, `Helpers/HtmlEmailFormatter.cs` line 633, and `Forms/DisclaimerDialog.cs` lines 30/48.
- Recommendations: At minimum, write the exception to a log file. Show the real error message when the connection test fails.

## Performance Bottlenecks

**All generated content is held in memory until the end:**
- Problem: Every thread, HTML body, and attachment byte array (DALL-E PNGs of about 1-3 MB each, MP3s, Office files) stays in memory until `SaveAllEmailsAsync` finishes (`Services/EmailGenerator.cs`, lines 344-371). Memory is only released after saving.
- Cause: The pipeline runs phase by phase over the entire dataset.
- Improvement path: Process each thread fully (emails, then attachments, then EML write), then release its byte arrays. This also fixes the data-loss bug above.

**Disclaimer banner inlined as Base64 into every email:**
- Problem: A 45 KB PNG (`Resources/quikdata_disclaimer_banner_yellow_svglogo_with_url.png`) becomes roughly 61 KB of Base64 in the `data:` URI of every HTML body (`Helpers/HtmlEmailFormatter.cs`, lines 136 and 604-641). That is about 60 MB of extra output and memory per 1,000 emails. Many clients (Outlook, most review platforms) also block `data:` images.
- Improvement path: Attach the banner once per message as a `cid:` linked resource in `Services/EmlFileService.cs`, the same way inline images are handled.

**Phase barriers limit throughput:**
- Problem: Storylines, documents, images, calendar invites, and voicemails each run as a separate `Task.WhenAll` barrier sharing one `SemaphoreSlim` (`Services/EmailGenerator.cs`, lines 73-342). One slow storyline stalls the start of all attachment work.
- Improvement path: Run each storyline's full pipeline as one unit of work under the semaphore.

**Unused large embedded resources increase exe size:**
- Problem: `ReelDiscovery.csproj` embeds every `Resources\**\*.png`. `Resources/ReelDiscovery.png` (2.2 MB) and `Resources/ReelDiscoveryHeader.png` (290 KB) are not referenced by any code, but they are still bundled into the single-file exe.
- Improvement path: Embed only the named resources that are actually used.

**Retry behavior:**
- Rate-limit backoff tops out at 16 seconds over 4 attempts with no jitter (`Services/OpenAIService.cs`). It ignores `Retry-After` headers.
- HTTP 500/502/504 and timeouts are not retried at all.
- With `ParallelThreads` up to 10 (`UserControls/StepGenerationConfig.cs`, line 89), requests tend to hit rate limits and retry in sync.

## Fragile Areas

**Signature correction heuristics:**
- Files: `Services/EmailGenerator.cs`, `CorrectSignatureBlock` (lines 981-1093)
- Why fragile:
  - It searches for another character's full name in the last 30% of the body and truncates everything after the sign-off. A legitimate mention of a colleague near the end ("Thanks, and cc'ing Jim Halpert") can delete real content.
  - The sign-off pattern list is duplicated inside the method.
- Safe modification: Extract the method as a static helper and cover it with table-driven unit tests before changing it.

**HTML string surgery for inline images:**
- Files: `Services/EmailGenerator.cs`, lines 1238-1279
- Why fragile: It inserts `<img>` tags by searching for literal class markers (`<div class="quoted-content">`, `<div class="forward-header">`, `<div class="signature">`) and the `data:image/png;base64,` banner prefix produced by `Helpers/HtmlEmailFormatter.cs`. Any change to the formatter's markup silently breaks image placement.
- Safe modification: Have `HtmlEmailFormatter.ConvertToHtml` accept the inline images, or emit a placeholder token such as `<!--INLINE_IMAGES-->`.

**LLM JSON contracts:**
- Files: DTO classes at the bottom of `Services/EmailGenerator.cs` (lines 1924-2036), `Services/StorylineGenerator.cs`, `Services/CharacterGenerator.cs`, `Services/ThemeGenerator.cs`
- Why fragile: They rely on `JsonObjectFormat` plus prompt wording. Excel rows need an `ExcelDocResponseRaw` workaround because the model returns numbers (lines 2007-2017). Missing fields fall back quietly to placeholders such as "Content unavailable." or `Data1/Data2`.
- Safe modification: Use structured outputs with a JSON schema, and log the fallbacks.

**`async void` wizard navigation:**
- Files: `Forms/WizardForm.cs` (`ShowStep` line 235, `BtnNext_Click` line 283), `UserControls/StepApiConfiguration.cs` line 213, `UserControls/StepCharacters.cs` line 190, `UserControls/StepStorylines.cs` line 162
- Why fragile: An exception thrown from `OnEnterStepAsync` or `OnLeaveStepAsync` (for example a telemetry or AI call) crashes the process, because `Program.cs` has no `Application.ThreadException` or `AppDomain.UnhandledException` handler. Rapid Next/Back clicks can also interleave overlapping `ShowStep` calls.
- Safe modification: Wrap the bodies in try/catch with a user-facing error, add global exception handlers in `Program.cs`, and disable navigation while a transition is in progress.

**Closing the window during generation:**
- Files: `Forms/WizardForm.cs` (`OnFormClosing` lines 345-350), `UserControls/StepGeneration.cs`
- Why fragile: Closing the form does not cancel `_cts`. Background tasks keep calling the API and then `Invoke` into disposed controls, which throws `ObjectDisposedException`.
- Safe modification: Have `OnFormClosing` ask the current step to cancel and await it, or block closing while `_isGenerating` is true.

## Scaling Limits

**Email count:**
- Current capacity: The UI caps `TotalEmailCount` at 500 (`UserControls/StepGenerationConfig.cs`, lines 72-75), yet commit `6c63066` is titled "Scale email generation to support 1000+ emails".
- Limit: Memory (see above), the replyToIndex bug in multi-batch threads, and the fact that any single enrichment failure loses the whole run.
- Scaling path: Stream threads to disk, isolate failures per item, then raise the cap.

**One thread per storyline:**
- `GenerateThreadsForStorylineAsync` always returns exactly one `EmailThread` (`Services/EmailGenerator.cs`, line 555). With 500 emails over 10 storylines, each thread has about 50 messages. Every reply re-quotes the previous body through `FormatQuotedReply`, so bodies grow as the thread gets longer.

## Dependencies at Risk

**Out-of-date OpenAI model catalog:**
- Risk: `Models/AIModelConfig.cs` hard-codes `gpt-4o`, `gpt-4o-mini`, `gpt-4-turbo`, `gpt-3.5-turbo`, `o1-preview`, and `o1-mini` with fixed prices. `o1-preview` and `o1-mini` are deprecated. o1-family models reject `MaxOutputTokenCount` and system messages as used in `TestConnectionAsync`, so choosing them fails in `Services/OpenAIService.cs`. The cost display is only as accurate as these hard-coded prices.
- Risk: Image and TTS model IDs are hard-coded: `"dall-e-3"` (line 196) and `"tts-1"` (line 257). DALL-E 3 is on OpenAI's deprecation path in favor of `gpt-image-1`.
- Migration plan: Load models and prices from a config file, move image generation to `gpt-image-1`, and make the image and TTS model IDs configurable.

**OpenAI .NET SDK 2.1.0:**
- Risk: This is an early 2.x release (`ReelDiscovery.csproj`). Newer SDK versions add structured outputs, built-in retry configuration, and newer image and audio APIs that the fixes above would use.
- Migration plan: Upgrade to a current 2.x release and check the `ChatCompletionOptions` and `ImageGenerationOptions` API changes.

**DocumentFormat.OpenXml 3.0.2:** Behind the current 3.x releases. Low risk, but it should be upgraded together with the other packages.

## Missing Critical Features

**No persistence or resume:**
- Problem: A cancelled or crashed run produces no output and cannot be resumed. `WizardState` (`Models/WizardState.cs`) is never saved, so storylines and characters must be regenerated, and paid for again, after restarting the app.
- Blocks: Reliable long or large runs.

**No logging:**
- Problem: There is no log file or logging framework. Errors appear only in the in-memory `RichTextBox` log (`UserControls/StepGeneration.cs`) and disappear when the app closes.
- Blocks: Diagnosing user-reported failures.

**No CI:**
- Problem: There is no `.github/workflows` or any other pipeline, so builds and releases are produced by hand.

## Test Coverage Gaps

**No automated tests exist:**
- What's not tested: Everything. There is no test project in the repository.
- Highest-value targets (pure logic that is easy to test):
  - `CorrectSignatureBlock`, `DistributeEmails`, `DistributeAttachmentsForBatch`, and `ConvertDtoToEmailMessage` reply resolution in `Services/EmailGenerator.cs`
  - `Helpers/ThreadingHelper.cs`: subject prefixes, `SetupThreading` References chains
  - `Helpers/FileNameHelper.cs`: sanitization, name collisions
  - `Helpers/DateHelper.cs`: `AllocateDateWindow`, `InterpolateDateInRange`
  - `Services/CalendarService.cs`: ICS escaping, line folding (lines longer than 75 octets are not folded as RFC 5545 requires)
  - `Services/EmlFileService.cs`: round-trip parse with MimeKit, checking Message-ID, In-Reply-To, References, and attachments
- Risk: Prompt and heuristic changes (such as the signature fixes in `df97dcd`) ship without any regression check, and the replyToIndex bug went unnoticed.
- Priority: High. Add an xUnit project `ReelDiscovery.Tests`, introduce `IOpenAIService` so `EmailGenerator` can run against canned JSON responses, and make `CorrectSignatureBlock` `internal` with `InternalsVisibleTo`.

---

*Concerns audit: 2026-09-29*
