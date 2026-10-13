---
phase: 07-scale-cost-qa-cutover
plan: 04
subsystem: email-formatting
tags: [theme, sanitization, html, css-injection, legacy-removal]
requires: []
provides:
  - "Helpers/ThemeSanitizer.cs: AllowedFonts, SanitizeHexColor, SanitizeFont, SanitizedTheme, TemplateTheme"
  - "D-26: BuildEmailTemplate cleans every theme color and font"
affects:
  - "07-20 (ThemeGenerator deletion): case flow no longer references ThemeGenerator"
tech-stack:
  added: []
  patterns:
    - "Case-preserving sanitization: valid values pass through byte-for-byte, invalid ones fall back to OrganizationTheme.Default"
key-files:
  created:
    - Helpers/ThemeSanitizer.cs
    - ReelDiscovery.Tests/Helpers/ThemeSanitizerTests.cs
  modified:
    - Helpers/HtmlEmailFormatter.cs
    - Helpers/BulkMailFormatter.cs
    - Services/Evidence/OrganizationThemes.cs
    - Services/ThemeGenerator.cs
    - ReelDiscovery.Tests/Evidence/PeopleDirectoryTests.cs
decisions:
  - "[07-04]: ThemeSanitizer.TemplateTheme keeps a color only if it is exactly 6 ASCII hex chars (original case) and a font only if it is in AllowedFonts (original spelling); else SanitizeHexColor/SanitizeFont, else the Default value (D-26)"
  - "[07-04]: Test namespace is ReelDiscovery.Tests.HelperTests (file under ReelDiscovery.Tests/Helpers): ReelDiscovery.Tests.Helpers would shadow ReelDiscovery.Helpers in the render tests"
metrics:
  duration: 6min
  completed: 2026-10-06
  tasks: 2
  files: 7
---

# Phase 7 Plan 04: ThemeSanitizer and D-26 Business HTML Cleaning Summary

The theme cleaning code now lives in a new `Helpers/ThemeSanitizer.cs`, moved out of the legacy `ThemeGenerator`. `HtmlEmailFormatter.BuildEmailTemplate` now cleans every theme color and font through the case-preserving `TemplateTheme`. Valid themes render exactly as before, so the golden HTML did not change. Invalid or injected values fall back to `OrganizationTheme.Default`.

## What Was Built

- **ThemeSanitizer** (`Helpers/ThemeSanitizer.cs`): `AllowedFonts`, `SanitizeHexColor` and `SanitizeFont` were moved unchanged. `SanitizedTheme` was moved unchanged from `BulkMailFormatter`; it upper-cases colors, so bulk output is the same. New `TemplateTheme(OrganizationTheme?)`:
  - Keeps a color unchanged when it is exactly 6 hex characters.
  - Keeps a font unchanged when it is in the allowed list.
  - Otherwise cleans the value, and uses the default if cleaning fails.
  - Returns the default theme when the theme is null.
- **D-26**: `BuildEmailTemplate` calls `ThemeSanitizer.TemplateTheme(theme)`. `BulkMailFormatter.Compose` passes an already-sanitized theme, which goes through unchanged.
- **Callers switched**: `HtmlEmailFormatter.ComposeFragment`, `BulkMailFormatter` (helpers and `Compose`), `OrganizationThemes`, `PeopleDirectoryTests` and `ThemeGenerator` itself now call `ThemeSanitizer.*`. `ThemeGenerator` no longer defines its own sanitizers. The only remaining `ThemeGenerator.` references are the `SchemaTypes` uses in `ReelDiscovery.Tests/Generation/LegacyGeneratorPortTests.cs`, which 07-20 will delete.

## Commits

| Task | Commit | Message |
|------|--------|---------|
| 1 (RED) | bfe2bcd | test(07-04): add failing ThemeSanitizer tests (D-20, D-26) |
| 1 (GREEN) | d12dd67 | feat(07-04): add ThemeSanitizer with case-preserving template cleaning |
| 2 | 9ca4d8b | refactor(07-04): switch theme cleaning to ThemeSanitizer and clean business HTML (D-20, D-26) |

## Verification

- `dotnet build ReelDiscovery.csproj -warnaserror`: 0 warnings, 0 errors
- Golden, formatter, PeopleDirectory, OrganizationTheme and ThemeSanitizer tests: 180/180 passed. The goldens stayed byte-identical.
- All non-render tests (`Category!=Render`): 3387/3387 passed
- `grep ThemeGenerator.` in Helpers, Services/Evidence, Services/Corpus and ReelDiscovery.Tests (excluding Generation): 0 matches. `static string? Sanitize` in ThemeGenerator: 0 matches.

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 3 - Blocking] Test namespace renamed to avoid shadowing**
- **Found during:** Task 1 (RED build)
- **Issue:** The planned namespace `ReelDiscovery.Tests.Helpers` hides `ReelDiscovery.Helpers` for the render tests. Their 18 `Helpers.Theme...` references (CaseSummaryRenderTests, CaseScenarioRenderTests) stopped compiling.
- **Fix:** The file stays at the planned path `ReelDiscovery.Tests/Helpers/ThemeSanitizerTests.cs` but uses namespace `ReelDiscovery.Tests.HelperTests`. A comment in the file explains why.
- **Commit:** bfe2bcd

**2. [Cleanup] Removed the unused `using ReelDiscovery.Services;` from BulkMailFormatter.cs** because its only use was ThemeGenerator. The build has no warnings.

## TDD Gate Compliance

The RED commit (bfe2bcd, failing to compile because ThemeSanitizer did not exist yet) comes before the GREEN commit (d12dd67). No refactor commit was needed for Task 1.

## Threat Mitigations

- T-07-12: `TemplateTheme` lets through only 6-hex colors and allowed fonts; anything else gets the default. Tested with `red;}body{` and `x</style><script>`.
- T-07-13: Valid values are kept byte-for-byte, and the golden tests pass unchanged.

## Self-Check: PASSED

- FOUND: Helpers/ThemeSanitizer.cs
- FOUND: ReelDiscovery.Tests/Helpers/ThemeSanitizerTests.cs
- FOUND: bfe2bcd, d12dd67, 9ca4d8b
