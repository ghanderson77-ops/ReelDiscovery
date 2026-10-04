---
phase: 06-attachments-non-relevant-corpus
plan: 10
subsystem: evidence-output
tags: [mime, eml, attachments, inline-images, determinism, harvesting, bounded-io]
requires: ["06-02"]
provides:
  - "MailboxWriter.BuildMime composes ComposedEmail.Attachments: inline images in multipart/related, files in multipart/mixed (plan order), DocID header per part"
  - "private MailboxWriter.SetBoundaries: deterministic boundary for every nested multipart (root formula unchanged)"
  - "AttachmentHarvester.HarvestMessage(caseFolder, copyPaths, expectedDocIds) -> Dictionary<string, HarvestedPart>"
  - "HarvestedPart { Bytes, ContentId }; AttachmentHarvester.MaxEmlBytes / MaxPartBytes / MaxHarvestBytes"
  - "MailboxWriterGolden.NoAttachmentsEml frozen Phase 5 EML fixture"
affects: [06-20 attachment repair recompose, thread unit of work writes with attachments, answer-key attachment rows]
tech-stack:
  added: []
  patterns: ["frozen Base64 golden EML captured from unchanged code", "path-derived SHA-256 nested MIME boundaries", "write-only BoundedStream decode sink", "closing-boundary tail check before trusting a copy"]
key-files:
  created:
    - Services/Evidence/AttachmentHarvester.cs
    - ReelDiscovery.Tests/Evidence/AttachmentHarvesterTests.cs
    - ReelDiscovery.Tests/Evidence/Golden/MailboxWriterGolden.cs
  modified:
    - Services/Evidence/MailboxWriter.cs
    - ReelDiscovery.Tests/Evidence/MailboxWriterTests.cs
decisions:
  - "Root MIME boundary keeps the Phase 5 formula (DocId|MessageId); nested boundaries hash DocId|MessageId|tree-path (\"0.0\", \"0.0.1\", ...), so emails without attachments stay byte-identical"
  - "Attachment content types: OpenXML types for Word/Excel/PowerPoint, image/png, text/calendar; method=REQUEST; charset=utf-8, audio/mpeg; unknown kind -> application/octet-stream"
  - "Parts with empty bytes or an unsafe name (after Path.GetFileName) are skipped; inline requires Kind Image + IsInline + non-empty ContentId, otherwise the image is a regular attachment"
  - "Harvester trusts a copy only if its root multipart closing boundary is present in the last 512 bytes, so a truncated copy never supplies partial part bytes"
  - "Total-cap semantics: each part decodes against min(part cap, remaining total); overflowing the remaining total stops the call, overflowing only the part cap skips that part"
metrics:
  duration: "~35 min"
  completed: 2026-10-04
  tasks: 2
  files: 5
---

# Phase 6 Plan 10: Deterministic Multipart EMLs and Attachment Harvesting Summary

MailboxWriter now writes attachments as standard MIME. Inline images go in a multipart/related next to the HTML, and files follow the body in plan order inside a multipart/mixed. Every attachment part carries its X-ReelDiscovery-DocID header, and every multipart gets a boundary derived from SHA-256. A frozen golden EML shows that emails without attachments are written byte-for-byte as in Phase 5. The new AttachmentHarvester reads attachment parts back per parent message. It takes each part from the first complete copy that has it, and every read is size-bounded (D-26, V12).

## What Was Built

### Task 1: Frozen golden, then deterministic multipart composition
- `MailboxWriterGolden.NoAttachmentsEml` was captured from the unchanged `BuildMime(Composed())` with a scratch test (since deleted) and committed before any production edit (a6b239a). `MailboxWriter_NoAttachments_MatchesGolden` compares the raw bytes.
- `BuildMime` adds inline images with `LinkedResources.Add`, overwriting the Content-Id with the plan's ContentId. Other parts go through `Attachments.Add`, with a content type chosen by kind. Every part gets `Headers.Add(DocIdHeader, attachment.DocId)`.
- `SetBoundaries` walks the whole MIME tree. The root keeps `BoundaryFor(email)`, and nested multiparts use `"=-rd-" + hex(SHA256($"{DocId}|{MessageId}|{path}"))[..24]`.
- Name guard: `Path.GetFileName` + `CorpusPlan.IsSafeFileName`; an unsafe part is skipped.
- `CopyPathsFor` is unchanged. Tests confirm that a non-relevant email from an external sender is written only to the recipients' inbox copies.

### Task 2: AttachmentHarvester
- `HarvestMessage` tries copies in order. Each path must pass `CorpusPlan.IsSafeEmlPath` and `MailboxWriter.ResolveUnderCase`. A file over 64 MB is skipped before it is loaded.
- Only parts whose DocID is in the expected set and not yet found are decoded. Each one decodes through a private `BoundedStream` capped at 25 MB, and one call stops at 100 MB total.
- Per-file catch: `IOException`, `UnauthorizedAccessException`, `FormatException`, `ParseException`. Per-part catch: `InvalidDataException`.
- Test-only overrides `EmlCapOverride`, `PartCapOverride` and `TotalCapOverride` are reset in the test class's `Dispose`.

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 2 - Missing critical functionality] Truncated-copy guard in the harvester**
- **Found during:** Task 2
- **Issue:** MimeKit is lenient. A copy cut off in the middle of a base64 part still parses, and that part decodes to partial bytes. Those bytes would then be "harvested" and written back into every copy during a repair. A readable but damaged copy would beat an intact one, which is exactly what review concern 3 warns against.
- **Fix:** After loading, a copy is used only if the closing line of its root multipart boundary (`--boundary--`) is in the last 512 bytes of the file. The test `Harvest_CorruptEml_ContributesNothing_NoException` cuts a copy mid-part. I confirmed the test fails when this check is disabled.
- **Files modified:** Services/Evidence/AttachmentHarvester.cs
- **Commit:** 5bd975b

No other deviations.

## TDD Gate Compliance
- Task 1: golden fixture test(...) a6b239a, then RED test(...) 6a6964b (10 failing), then GREEN feat(...) 82454c0
- Task 2: RED test(...) 8e81b4b (compile failure, type missing), then GREEN feat(...) 5bd975b

## Verification
- `dotnet test --filter "FullyQualifiedName~MailboxWriterTests|FullyQualifiedName~AttachmentHarvesterTests"`: 43 passed
- Full suite: 2968 passed, 0 failed (baseline 2941 + 27 new)
- `dotnet build ReelDiscovery.csproj`: succeeded with no warnings
- All acceptance greps in the plan match

## Threat Mitigations
- T-06-28: IsSafeEmlPath + ResolveUnderCase. Tests cover `..\`, `../`, absolute paths, and `Mailboxes/../..` (Harvest_UnsafePaths_Ignored).
- T-06-29: the 64 MB / 25 MB / 100 MB caps, plus per-file catches (each has a test).
- T-06-30: Path.GetFileName + IsSafeFileName in BuildMime.
- T-06-31: a DocID header on every attachment part, and only expected DocIds are harvested.

## Known Stubs
None.

## Self-Check: PASSED
- FOUND: Services/Evidence/AttachmentHarvester.cs, ReelDiscovery.Tests/Evidence/AttachmentHarvesterTests.cs, ReelDiscovery.Tests/Evidence/Golden/MailboxWriterGolden.cs
- FOUND commits: a6b239a, 6a6964b, 82454c0, 8e81b4b, 5bd975b
