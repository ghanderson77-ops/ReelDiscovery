---
phase: 01-foundation-provider-gateway
plan: 01
subsystem: platform
tags: [dotnet10, nuget, xunit, openxml, mimekit, test-harness]
requires: []
provides:
  - net10.0-windows app target with global.json SDK pin (10.0.401, latestFeature)
  - Phase packages: Anthropic 12.51.0, OpenAI 2.14.0, Microsoft.Extensions.AI.Abstractions 10.10.1, DocumentFormat.OpenXml 3.5.1, MimeKit 4.18.1
  - ReelDiscovery.slnx solution (app + tests), so bare `dotnet test` works at the repo root
  - ReelDiscovery.Tests xUnit project with InternalsVisibleTo access
  - FakeHttpHandler for SDK wire tests
affects: [01-02, 01-03, 01-04, 01-05, 01-06, 01-07, 01-08, 01-09, 01-10, 01-11]
tech-stack:
  added: [Anthropic 12.51.0, Microsoft.Extensions.AI.Abstractions 10.10.1, xunit 2.9.3, xunit.runner.visualstudio 3.1.4, Microsoft.NET.Test.Sdk 17.14.1]
  patterns: [canned-response HttpMessageHandler for SDK tests, OpenXmlValidator regression pinning]
key-files:
  created:
    - global.json
    - ReelDiscovery.slnx
    - ReelDiscovery.Tests/ReelDiscovery.Tests.csproj
    - ReelDiscovery.Tests/Fakes/FakeHttpHandler.cs
    - ReelDiscovery.Tests/PlatformTests.cs
    - ReelDiscovery.Tests/Documents/DocumentRegressionTests.cs
    - .planning/phases/01-foundation-provider-gateway/deferred-items.md
  modified:
    - ReelDiscovery.csproj
    - UserControls/StepComplete.cs
decisions:
  - "Word builder's 3 pre-existing OOXML schema errors (w:color after w:sz) are pinned as a baseline in the test, not fixed in this plan"
metrics:
  duration: 5min
  completed: 2026-09-30
  tasks: 2
  files: 9
---

# Phase 01 Plan 01: .NET 10 Retarget and Test Harness Summary

The app now targets net10.0-windows, with SDK 10.0.401 pinned by global.json. It carries every package the phase needs (Anthropic 12.51.0, OpenAI 2.14.0, MEAI.Abstractions 10.10.1, OpenXml 3.5.1). A new xUnit project in ReelDiscovery.slnx provides a shared FakeHttpHandler plus platform, Office and EML regression tests, and bare `dotnet test` passes 6 of 6.

## Tasks

| Task | Name | Commit | Key files |
|------|------|--------|-----------|
| 1 | Retarget to net10.0-windows, pin SDK, upgrade packages, create solution | 7e9cf0a | global.json, ReelDiscovery.csproj, ReelDiscovery.slnx, UserControls/StepComplete.cs |
| 2 | Add the xUnit project, FakeHttpHandler, and platform/document regression tests | 3285fda | ReelDiscovery.Tests/** , ReelDiscovery.slnx |

## Verification

- `dotnet build ReelDiscovery.csproj`: 0 warnings, 0 errors. CS0067 is gone, and the retarget raised no WFO1000 or other new warnings. There is no CS0579 because the test folder is excluded from the app build.
- `dotnet publish ReelDiscovery.csproj -c Release` produces `bin/Release/net10.0-windows/win-x64/publish/ReelDiscovery.exe` (single file plus pdb).
- Bare `dotnet test` at the repo root passes 6 of 6. `--filter DocumentRegressionTests` passes 4 of 4.
- Excel and PowerPoint builders validate with 0 OpenXmlValidator errors. The EML round trip keeps In-Reply-To and the Word attachment part.

## Deviations from Plan

### Pinned baseline (as the plan allows)

**Word_OpensAndValidates baseline = 3 errors.** `OfficeDocumentService.CreateWordDocument` builds `w:rPr` with `w:sz` before `w:color`. The schema requires the reverse order, so each themed run produces one `Sch_UnexpectedElementContentExpectingComplex` error: 3 for the test input. This bug existed before the plan and was not caused by the OpenXml 3.5.1 upgrade. The test asserts exactly 3 errors, all of them this `:color` ordering error, so any new kind of regression still fails. The item is logged in `deferred-items.md`: swap the order to Color then FontSize and set the baseline to 0.

### Other notes

- The `tdd="true"` flag on Task 2 covers regression tests for existing behavior, so there was no separate RED commit. The only initial failure was the Word baseline described above. Tests and harness were committed together as `test(01-01)`.
- The first shell edit hung on a stray `python` invocation (the Windows Store stub). I killed it and the remaining `sed` edits completed. File contents were checked afterward, with no effect on the output.

## Known Stubs

None.

## Threat Flags

None. T-01-01 is handled by exact package pins, T-01-02 by the Compile/EmbeddedResource/None Remove of `ReelDiscovery.Tests/**`, and T-01-03 by the net10 retarget.

## Self-Check: PASSED
