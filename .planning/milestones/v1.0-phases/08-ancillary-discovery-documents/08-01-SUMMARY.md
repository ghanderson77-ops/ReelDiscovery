---
phase: 08-ancillary-discovery-documents
plan: 01
subsystem: discovery-model
tags: [discovery, persistence, staleness, case-json, ANC-05]
requires: []
provides:
  - "Models/Discovery/*: DiscoverySettings (Normalize, ForPreset), DiscoveryDocument + enums (Kind, Status, RowState, Variant, StaleSource), DiscoveryVariants.Of/IsMemo, DiscoveryDocumentSet + ExhibitAssignment, DepositionContent, WrittenDiscoveryContent, DiscoveryViolation + DiscoveryRule, DiscoveryCorpus + ExhibitCandidate"
  - "CaseDraft.Discovery (DiscoverySettings?), CaseDraft.CurrentFormatVersion = 4"
  - "DiscoveryStore: SetPath, FolderPath, Load, LoadOrNew, SaveAsync, IsDiscoveryFileName, DocxPath, FileNameFor, HasDocuments; DiscoveryLoadResult"
  - "DiscoveryStaleness: CaptionHash, Stamp, Check"
affects: [08-planners, 08-layout, 08-builders, 08-validator, 08-generator, 08-ui]
tech-stack:
  added: []
  patterns:
    - "Sidecar store copied from CorpusPlanStore: size cap, PeekFormatVersion, CaseId check, never-throwing load, atomic write"
    - "Docx paths = case folder + constant Discovery + IsDiscoveryFileName name, then GetFullPath StartsWith(root)"
    - "Staleness by content hash (guide, core, caption) plus corpus plan id; never CaseDraft.Revision"
key-files:
  created:
    - Models/Discovery/DiscoverySettings.cs
    - Models/Discovery/DiscoveryDocument.cs
    - Models/Discovery/DiscoveryDocumentSet.cs
    - Models/Discovery/DepositionContent.cs
    - Models/Discovery/WrittenDiscoveryContent.cs
    - Models/Discovery/DiscoveryViolation.cs
    - Models/Discovery/DiscoveryCorpus.cs
    - Services/Discovery/DiscoveryStore.cs
    - Services/Discovery/DiscoveryStaleness.cs
    - ReelDiscovery.Tests/Discovery/DiscoveryStoreTests.cs
    - ReelDiscovery.Tests/Discovery/DiscoveryStalenessTests.cs
  modified:
    - Models/Case/CaseDraft.cs
    - ReelDiscovery.Tests/Case/CaseDraftSerializationTests.cs
    - ReelDiscovery.Tests/Evidence/GenerationStateTests.cs
decisions:
  - "08-01: DiscoverySettings.Normalize also trims witness ids and drops whitespace-only ids (CaseGuide.CleanStrings only removes nulls)"
  - "08-01: A missing discovery-documents.json (or empty case folder) is Missing = true with an empty Reason, not a failure message"
  - "08-01: DiscoveryStore.FileNameFor caps the file name near 120 chars by truncating the case part (min 10 chars); docPart is already capped at 80 by CaseFolderNaming.Sanitize"
  - "08-01: DiscoveryStaleness.CaptionHash is empty when there is no core document, matching CoreHash"
metrics:
  duration: 18min
  completed: 2026-10-08
  tasks: 2
  files: 14
---

# Phase 8 Plan 01: Discovery Data Contracts, Sidecar Store and Staleness Summary

This plan adds the Phase 8 data model: discovery settings, documents, deposition and written content, violations and a corpus snapshot. It also adds `CaseDraft.Discovery`, saved in case.json format version 4, plus a store for discovery-documents.json and per-document staleness stamps. The store's load never throws, and it only builds .docx paths that stay inside the case's `Discovery\` folder. A stamp records the guide, core-document and caption hashes and the corpus plan id.

## What Was Built

- **Models/Discovery** (namespace `ReelDiscovery.Models.Discovery`). These are mutable classes with no records:
  - `DiscoverySettings` holds the range constants (pages 10-60, RFP 5-80, ROG 1-25 per FRCP 33(a)(1), RFA 1-50, at most 6 witnesses) and the selections. `ForPreset(Simple/Standard/Complex)` returns 15/25/40 pages, 15/25/40 RFP, 12/18/25 ROG and 10/20/30 RFA. `Normalize()` covers T-08-01.
  - `DiscoveryDocument` sits in the same file as the enums `DiscoveryKind`, `DiscoveryStatus`, `DiscoveryRowState`, `DiscoveryVariant` and `StaleSource`. That file also holds the static `DiscoveryVariants.Of(CoreDocument?)` / `IsMemo`.
  - `DiscoveryDocumentSet` defines `FileName = "discovery-documents.json"` and `CurrentFormatVersion = 1`, plus `CaseNumber` (persisted once), `ExhibitAssignments`, `Documents`, `UnattributedCostUsd`, `TotalCostUsd`, `Find`, `Upsert` and `Normalize`.
  - `DepositionContent` (speakers, turns, exhibits) and `WrittenDiscoveryContent` (definitions, requests, caption, boilerplate and signature text) carry everything needed to rebuild a .docx without a model call.
  - `DiscoveryViolation` covers rules D1-D8, with `IsStructural` set for the D1 and D4 rules. `DiscoveryCorpus` and `ExhibitCandidate` are the corpus snapshot; `DiscoveryCorpus.None(reason)` stands for "no usable corpus".
- **CaseDraft v4:** `CurrentFormatVersion = 4`, a `Discovery` property and `Discovery?.Normalize()` inside `Normalize()`. Version 3 files still load, with `Discovery == null`, and are upgraded to version 4.
- **DiscoveryStore:**
  - `Load` mirrors `CorpusPlanStore`: a 20 MB size cap (with an `AsyncLocal` test override), `PeekFormatVersion` to refuse files from a newer version, a CaseId check, and the same list of caught exceptions.
  - `SaveAsync` goes through `CaseStore.WriteAtomicAsync`.
  - `IsDiscoveryFileName` and `DocxPath` add the `GetFullPath` + `StartsWith(root)` check (T-08-02).
  - `FileNameFor` builds `"{case} - {document}.docx"` and truncates the case part first.
  - `HasDocuments` reports whether the Discovery folder holds any .docx.
- **DiscoveryStaleness:**
  - `CaptionHash` covers jurisdiction, counsel, document type, matter kind, memo perspective, title, filing date and the party identity fields.
  - `Stamp` records the current sources; the plan id is kept only for a usable corpus.
  - `Check` returns the first source that changed, in the order Guide → Core → Caption → CorpusPlan. A document with no content, or with a null plan id, is never stale on that account.

## Verification

- `dotnet build ReelDiscovery.csproj -warnaserror --artifacts-path obj/gsd-artifacts`: 0 errors, 0 warnings.
- `dotnet build ReelDiscovery.Tests/ReelDiscovery.Tests.csproj -warnaserror --artifacts-path obj/gsd-artifacts`: 0 errors, 0 warnings.
- Targeted run: `Tests.Discovery.` passed 32/32. With CaseDraftSerializationTests and CaseStoreTests added, 78/78 passed. CaseDraftSerializationTests, CaseStoreTests and GenerationStateTests together passed 67/67.
- Full suite (run by accident, see note): 3942/3942 passed.
- All acceptance greps match. There are no `record` types, and `Revision` does not appear in the code of DiscoveryStaleness.cs.

Note: the plan's verification filter `FullyQualifiedName~Discovery` matches every test, because every test sits in the `ReelDiscovery.Tests` namespace. The run therefore covers the whole suite, which takes about 13 minutes. Later plans should filter on `FullyQualifiedName~Tests.Discovery.` instead.

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 2 - Correctness] Whitespace witness ids dropped**
- **Found during:** Task 1
- **Issue:** `CaseGuide.CleanStrings` removes only nulls, but the plan's behavior requires blank ids to be dropped.
- **Fix:** `Normalize` now trims and filters empty ids before the Distinct/Take step.
- **Files modified:** Models/Discovery/DiscoverySettings.cs
- **Commit:** 9191803

Otherwise the plan was executed as written. The extra tests, all beyond the plan's list, are: the `DiscoveryVariants_Of_MapsDocumentType` test, a `Load_NullLists_AreRepaired` test, a `LoadOrNew` test, a `HasDocuments` test, a test for the order in which staleness sources are reported, and a test that a document with no content is never stale.

## TDD Gate Compliance

- Task 1: RED eade0ed (test) → GREEN 9191803 (feat)
- Task 2: RED 4231479 (test) → GREEN e33af48 (feat)
- No refactor commits were needed.

## Commits

| Task | Commit | Message |
|------|--------|---------|
| 1 RED | eade0ed | test(08-01): add failing tests for discovery settings and case.json v4 |
| 1 GREEN | 9191803 | feat(08-01): add discovery model types and case.json v4 |
| 2 RED | 4231479 | test(08-01): add failing tests for discovery store and staleness |
| 2 GREEN | e33af48 | feat(08-01): add discovery sidecar store and staleness stamp |

## Known Stubs

None. These are data contracts and persistence code; no UI is wired yet.

## Self-Check: PASSED
