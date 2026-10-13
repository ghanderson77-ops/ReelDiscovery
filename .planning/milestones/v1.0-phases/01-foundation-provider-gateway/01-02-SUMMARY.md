---
phase: 01-foundation-provider-gateway
plan: 02
subsystem: generation
tags: [concurrency, thread-safety, bug-fix, emailgenerator, xunit]
requires: [01-01]
provides:
  - GenerationResult.AddError (locked error sink) and GenerationProgress.Clone() snapshots
  - Random.Shared across Services/ and Helpers/ (no shared System.Random instances)
  - EmailGenerator.RunIsolatedAsync per-item isolation for enrichment passes
  - EmailGenerator.ResolveReferencedEmail (batch-relative replyToIndex) and BuildMeetingStart (clamped)
  - DocumentChainState.ReserveNextVersion (atomic chain version reservation)
  - SaveAndFinalizeAsync, so completed threads are saved on success and on cancel
  - Linked runCts/runToken in GenerateEmailsAsync, ready for the plan 01-11 fatal stop
affects: [01-08, 01-10, 01-11]
tech-stack:
  added: []
  patterns: [lock-then-Clone progress snapshots via a local Report(mutate) function, RunIsolatedAsync per-item failure isolation, source-scan architecture test]
key-files:
  created:
    - ReelDiscovery.Tests/Concurrency/ConcurrencyTests.cs
    - ReelDiscovery.Tests/Generation/EmailGeneratorFixTests.cs
  modified:
    - Models/GenerationResult.cs
    - Helpers/DateHelper.cs
    - Services/CharacterGenerator.cs
    - Services/EmailGenerator.cs
decisions:
  - "Chain versions are reserved before the document is generated, so a failed generation leaves a gap in version numbers rather than risking duplicates"
  - "The reserved version is also passed to GenerateWordAttachmentAsync (optional reservedVersion param) so the Word prompt names the right version"
  - "The cancel-path attachment total comes from the Word/Excel/PowerPoint result counters, because totalAttachments is only summed after the attachment pass finishes"
metrics:
  duration: 12min
  completed: 2026-09-30
  tasks: 3
  files: 6
---

# Phase 01 Plan 02: Concurrency and Legacy Bug Fixes Summary

The existing generation pipeline is now thread-safe. It uses a locked error sink, reports progress as Clone() snapshots, and uses Random.Shared everywhere. Each attachment, image, calendar invite and voicemail runs in isolation, so one failed item is logged and skipped. A cancelled run still saves the threads it finished. Six legacy bugs are fixed through small `internal static` helpers with unit tests: batch-relative replyToIndex, clamped meeting time, single-character cast, trimmed batches, atomic chain versions, and shared Random. The full suite passes 16 of 16.

## Tasks

| Task | Name | Commit | Key files |
|------|------|--------|-----------|
| 1 | Thread-safe result/progress types, Random.Shared in helpers | 6a41a0a | Models/GenerationResult.cs, Helpers/DateHelper.cs, Services/CharacterGenerator.cs, ReelDiscovery.Tests/Concurrency/ConcurrencyTests.cs |
| 2 | EmailGenerator Random.Shared, chain version reservation, B1/B3/B4/B5 | 993c1ce | Services/EmailGenerator.cs, ReelDiscovery.Tests/Generation/EmailGeneratorFixTests.cs, ConcurrencyTests.cs |
| 3 | RunIsolatedAsync, progress snapshots, save-on-cancel | 37066e6 | Services/EmailGenerator.cs, EmailGeneratorFixTests.cs |

## Bug inventory coverage

- **C1/C2/C3**: every `_random` is gone. The `NoSharedRandomInstances` source scan guards Services/ and Helpers/.
- **C4**: `RunIsolatedAsync` wraps all 4 enrichment lambdas. The semaphore Wait/Release stays in the outer try/finally.
- **C5/C7**: every report goes through the local `Report(mutate)` function or through a lock block that ends with `Clone()`. Result counters are still updated under `progressLock`.
- **C6**: `DocumentChainState.ReserveNextVersion()` is used in both `GeneratePlannedDocumentAsync` and `GenerateAttachmentAsync`. `VersionNumber++` now appears only inside the reservation method.
- **B1**: `ResolveReferencedEmail(thread.Messages, batchOffset, e.ReplyToIndex)`, where `batchOffset` is captured before each batch.
- **B2**: `SaveAndFinalizeAsync` runs on success and in `catch (OperationCanceledException)` when threads exist. It saves with `CancellationToken.None`.
- **B3**: `BuildMeetingStart` clamps the hour to 0-23 and the minute to 0-59.
- **B4**: `FirstOrDefault(c => c.Id != fromChar.Id) ?? fromChar`.
- **B5**: `response.Emails.Take(batchSize)`, and an empty batch breaks the loop.

## Verification

- `dotnet build ReelDiscovery.csproj`: 0 warnings, 0 errors.
- Bare `dotnet test`: 16 of 16 pass (6 prior + 4 ConcurrencyTests + 6 EmailGeneratorFixTests).
- Grep acceptance counts in EmailGenerator.cs: `_random` 0, `result.Errors.Add(` 0, `result.AddError(` 5, `RunIsolatedAsync(` 5, `Clone()` 3, `SaveAndFinalizeAsync(` 3, `VersionNumber++` 1.
- No `_openAI` call site was changed.

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 1 - Bug] Second chain-versioning site and the Word prompt version**
- **Found during:** Task 2
- **Issue:** The same non-atomic `VersionNumber++` pattern also exists in `GenerateAttachmentAsync`, not only in `GeneratePlannedDocumentAsync`. In addition, `GenerateWordAttachmentAsync` computed `chainState.VersionNumber + 1` for its prompt, which would be off by one after reservation.
- **Fix:** Both sites now reserve the version up front. `GenerateWordAttachmentAsync` takes an optional `int? reservedVersion` and uses it in the prompt. The acceptance criterion (`VersionNumber++` count 1) required this.
- **Commit:** 993c1ce

**2. [Rule 1 - Bug] Attachment count on the cancel path**
- **Found during:** Task 3
- **Issue:** `totalAttachments` is only set after the whole attachment pass completes, so a cancelled run would have reported 0 attachments.
- **Fix:** The cancel path sums the Word/Excel/PowerPoint result counters.
- **Commit:** 37066e6

### Other notes

- I removed the unused `totalImages` local and moved `EmlFileService` construction into `SaveAndFinalizeAsync`. Neither changes behavior.
- Git Bash `sed -i` rewrote CRLF files as LF, so I restored CRLF on each edited file before committing. The diffs contain only the intended changes.
- TDD: the helpers and their tests went into one commit per task. There were no separate RED commits because the tested functions did not exist yet. The one test planned to fail first, `NoSharedRandomInstances`, was seen failing after Task 1 and passing after Task 2.

## Known Stubs

- `RunIsolatedAsync` accepts `runCts` but does not use it yet. This is intentional: plan 01-11 adds the fatal-provider-error stop.

## Threat Flags

None. T-01-04 through T-01-08 are mitigated as the plan specifies.

## Self-Check: PASSED
