---
phase: 03-case-guide
plan: 04
subsystem: case-persistence
tags: [persistence, case-json, autosave, atomic-write, onedrive, recent-cases, tests]
requires:
  - "03-01 (CaseDraft FormatVersion/CaseId/UpdatedUtc/Stage/Guide/FolderPath, ToJson/FromJson/Normalize)"
provides:
  - "Services/Case/CaseFolderNaming.cs (CaseTitle, DefaultFolderName, Sanitize, UniqueFolderPath, MaxLength, Untitled)"
  - "Services/Case/CaseStore.cs (EnsureFolder, SaveAsync, SaveNow, Load, GetRecentCases, TouchRecent, RemoveRecent, MoveFolder, WriteAtomicAsync, WriteAtomic, LastSavedUtc, LastSaveError, SaveCompleted, CanSave)"
  - "CaseLoadError / CaseLoadResult / RecentCase types"
  - "WizardState.CaseStore and WizardState.PendingNotice"
affects: [03-05, 03-06, 03-12, 03-13, 03-14]
tech-stack:
  added: []
  patterns:
    - "Snapshot on the caller (UI) thread, write on the pool; SemaphoreSlim single writer; queued snapshots coalesce by sequence number (latest wins)"
    - "Whole drain (lock acquire, write, release) runs inside Task.Run so a UI-thread SaveNow/MoveFolder that blocks on the lock cannot deadlock"
    - "Atomic write: hidden .{file}.{guid}.tmp in the same folder, WriteThrough + Flush(true), File.Move overwrite retried 5x (100*n ms), temp deleted on failure"
    - "Load never throws: Missing / TooLarge / NewerVersion / Corrupt results with UI-SPEC messages"
key-files:
  created:
    - Services/Case/CaseFolderNaming.cs
    - Services/Case/CaseStore.cs
    - ReelDiscovery.Tests/Case/CaseFolderNamingTests.cs
    - ReelDiscovery.Tests/Case/CaseStoreTests.cs
  modified:
    - Models/WizardState.cs
decisions:
  - "[03-04]: Queued autosave snapshots carry a sequence number; a waiter whose snapshot was already written by a newer one (or superseded by SaveNow) returns the latest write outcome instead of writing again"
  - "[03-04]: SaveAsync/SaveNow are no-ops (return false, no SaveCompleted) until the core document exists; once a save is attempted SaveCompleted always fires"
  - "[03-04]: MoveFolder holds the writer lock, drops any queued snapshot (it points at the old folder) and replaces the recent entry; same-volume Directory.Move, cross-volume copy + delete"
  - "[03-04]: Reserved device names are checked on the base name before the first dot (COM3.txt -> 'COM3.txt Case'); leading dots are stripped so '..' traversal collapses to a plain name"
  - "[03-04]: recent.json stores Stage as a string and is read tolerantly (corrupt -> empty, unknown stage -> CoreDocument); write failures there are non-critical, case.json failures are always reported"
metrics:
  duration: 14min
  completed: 2026-10-01
---

# Phase 3 Plan 04: Case Persistence (CaseStore + CaseFolderNaming) Summary

Per-case folders under Documents\ReelDiscovery\Cases with readable, Windows-safe, root-confined names, plus a CaseStore that autosaves case.json atomically (temp + WriteThrough + retried rename, single coalescing writer), loads with size/version/corrupt checks and normalization, keeps an 8-entry recent list in %LOCALAPPDATA%, and moves case folders. The store is exposed as `WizardState.CaseStore`.

## What Was Built

- **CaseFolderNaming**: "Brightline Glazing, LLC v. Harmon Builders, Inc." titles (Plaintiff/Complainant v. Defendant/Subject), memo titles "{Organization or first party} - {Re}", fallbacks to doc.Title / "Untitled Case"; folder names from ShortNames ("Brightline v. Harmon"); `Sanitize` removes invalid chars, collapses whitespace, strips leading dots and trailing dots/spaces, suffixes reserved device names with " Case", caps at 80; `UniqueFolderPath` adds " (2)", " (3)"... and throws `InvalidOperationException` if the result's parent is not the Cases root.
- **CaseStore**: `EnsureFolder`, `SaveAsync` (coalescing), `SaveNow` (synchronous close flush under the same lock), `Load` (Missing removes the recent entry; >50 MB; FormatVersion peek; Corrupt message "Could not open case.json: {reason}. Choose a different case.json with Open Case..."), recent list (`GetRecentCases`, `TouchRecent`, `RemoveRecent`), `MoveFolder`, public `WriteAtomic`/`WriteAtomicAsync` (reusable by the Word exporters).
- **WizardState**: `CaseStore` and `PendingNotice` in the case-flow group. Constructing the store does no I/O.

## Verification

- `dotnet test --filter "FullyQualifiedName~CaseStoreTests|FullyQualifiedName~CaseFolderNamingTests|FullyQualifiedName~WizardStateProviderTests"`: 45 passed.
- `dotnet test --filter "Category!=Render"`: 650 passed, 0 failed.
- `dotnet build ReelDiscovery.csproj --no-incremental`: 0 warnings, 0 errors.
- All tests use unique `%TEMP%\rd-{guid}` roots for both casesRoot and appDataFolder, deleted in Dispose; no `rd-*` folders left behind.
- Acceptance greps: `File.Move(tmp, path, overwrite: true)` x2, `FileOptions.WriteThrough` x2, MaxCaseFileBytes and newer-version message x1, WizardState members x1 each; no bare `catch` in CaseStore.cs (all catches are typed and commented).

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 1 - Bug] Deadlock between SaveNow and an in-flight SaveAsync on the UI thread**
- **Found during:** Task 2 design
- **Issue:** As written in the plan, SaveAsync would release the writer lock in a continuation that resumes on the UI SynchronizationContext; a close-time `SaveNow` blocking the UI thread on `_writeLock.Wait()` would then never see the release.
- **Fix:** The whole drain (WaitAsync, write, Release) runs inside `Task.Run` with `ConfigureAwait(false)`; only result bookkeeping (LastSaveError, TouchRecent, SaveCompleted) runs back on the caller context.
- **Files modified:** Services/Case/CaseStore.cs
- **Commit:** a4a6ef7

**2. [Rule 2 - Correctness] No bare catch blocks in CaseStore**
- The plan allowed bare catches on the temp-delete/recent paths; they are typed instead (`IOException`/`UnauthorizedAccessException`, `JsonException`), each with a comment, which also satisfies the "comment on the next line" check unambiguously.

**3. [Rule 2 - Correctness] Extra tests**
- Added `Constructor_DoesNoIo`, `WizardState_ExposesStore`, `SaveNow_Failure_Reported`, `Recent_TouchExisting_MovesToFront`, `WriteAtomic_ReplacesContent`, `Unique_EscapingRoot_Throws`, extra corrupt inputs (`null`, `[1,2]`, empty) and a save-after-move check.

## Requirements

GUIDE-10 is not marked complete: the persistence layer is done, but nothing in the UI calls it yet (autosave triggers, Open Case, close flush and stage navigation land in later Phase 3 plans).

## Known Stubs

None.

## TDD Gate Compliance

- Task 1: RED df68cf4 (test) -> GREEN a9052e1 (feat)
- Task 2: RED 71bf25f (test) -> GREEN a4a6ef7 (feat)
- No refactor commits needed.

## Self-Check: PASSED
