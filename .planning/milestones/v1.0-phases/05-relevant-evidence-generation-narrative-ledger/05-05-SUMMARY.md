---
phase: 05-relevant-evidence-generation-narrative-ledger
plan: 05
subsystem: evidence-composition
tags: [people-directory, themes, html, allowlist, security]
requires:
  - 05-01 (EvidenceTestData.ApprovedCase fixture)
provides:
  - Services/Evidence/PeopleDirectory (PersonInfo, Create/Find/NameOf/EmailOf/KnownNameTokens/All)
  - Services/Evidence/OrganizationThemes.ForDomain (8 fixed palettes, SHA-256 of domain)
  - HtmlEmailFormatter.ComposeFragment / ComposeHtml / QuotedPart / PrivilegedBannerText and caps
affects: [05-06 EmailComposer, render repair (KnownNameTokens)]
tech-stack:
  added: []
  patterns: [encode-then-allowlist HTML, deterministic hash palette, GeneratedRegex in partial static class]
key-files:
  created:
    - Services/Evidence/PeopleDirectory.cs
    - Services/Evidence/OrganizationThemes.cs
    - ReelDiscovery.Tests/Evidence/PeopleDirectoryTests.cs
    - ReelDiscovery.Tests/Evidence/ComposeHtmlTests.cs
  modified:
    - Services/ThemeGenerator.cs
    - Helpers/HtmlEmailFormatter.cs
decisions:
  - "[Phase 05] ComposeFragment bold budget is per message across body and table cells; the privileged banner's <strong> does not count against it"
  - "[Phase 05] A rejected pipe table (over 4 columns, over 6 rows or wider rows than the header) renders its lines as literal paragraph text and does not consume the one-table allowance"
  - "[Phase 05] OrganizationThemes still passes its fixed palettes through ThemeGenerator.SanitizeHexColor/SanitizeFont (defense in depth, T-05-13)"
metrics:
  duration: 14min
  completed: 2026-10-03
  tasks: 2
  files: 6
---

# Phase 5 Plan 05: People Directory, Organization Themes and ComposeHtml Summary

People directory over cast and plan externals (name, email, sender domain), deterministic SHA-256 domain-to-palette themes (8 fixed palettes, personal domains get Default), and an encode-first, allowlist-only `HtmlEmailFormatter.ComposeHtml` (2 bold runs, one 6-item list, one 4x6 table, reply blockquote / forward block) beside the unchanged legacy `ConvertToHtml`.

## Tasks

| Task | Name | Commits |
|------|------|---------|
| 1 | PeopleDirectory and OrganizationThemes | 1d1bd27 (test), 610e82a (feat) |
| 2 | Structured, allowlist-only ComposeHtml | 84cb94d (test), 7e6c9ec (feat) |

## What Was Built

- **PeopleDirectory:** `PersonInfo { Id, FullName, FirstName, LastName, Email, Domain, OrganizationId, Cast, IsExternal }`. Cast first, then externals, first wins on duplicate ids, null-safe lists and entries. Domain = org Domain (trimmed, lower-case) else email host. External names split on the first space. `NameOf` falls back to the id, `EmailOf` to empty. `KnownNameTokens` (ordinal-ignore-case) holds full/first/last names with no blanks.
- **OrganizationThemes.ForDomain:** blank or personal (gmail, yahoo, outlook, hotmail, icloud, aol, proton.me) returns `OrganizationTheme.Default`; otherwise `SHA256(lower(domain))` first 8 bytes big-endian `% 8` selects one of 8 muted palettes (fonts all from `ThemeGenerator.AllowedFonts`). New instance per call, `Domain` set.
- **ThemeGenerator:** `SanitizeHexColor`, `AllowedFonts`, `SanitizeFont` are now `internal` (no other change).
- **HtmlEmailFormatter:** class is `partial`; `WrapInTemplate` is `internal`; new `PrivilegedBannerText`, `MaxBoldRuns/MaxBulletItems/MaxTableColumns/MaxTableRows`, `ComposeFragment`, `ComposeHtml` (=> `WrapInTemplate(ComposeFragment(...), theme)`), and `QuotedPart` in the same file. Every model string is HTML-encoded before the allowlist runs; the bold regex is a `[GeneratedRegex]` with a `{1,120}` bound; leftover `**` removed. Colors and font are re-sanitized from the `theme` parameter; `_currentTheme` is never read.

## Verification

- `dotnet test --filter "FullyQualifiedName~PeopleDirectoryTests"`: 17 passed.
- `dotnet test --filter "FullyQualifiedName~ComposeHtmlTests"`: 21 passed (including a SHA-256-pinned legacy `ConvertToHtml("Hello _world_")` regression captured before editing).
- `dotnet test --filter "Category!=Render"`: 2145 passed, 0 failed.
- `dotnet build ReelDiscovery.csproj`: 0 warnings.
- Acceptance greps: all match (`new Regex(` = 0, `GeneratedRegex` = 2).

## Deviations from Plan

**1. [Rule 2 - Security] OrganizationThemes routes palettes through the sanitizers at runtime**
- **Found during:** Task 2 review of key_links (pattern `ThemeGenerator.(SanitizeHexColor|SanitizeFont)` from OrganizationThemes.cs)
- **Fix:** `ForDomain` passes each palette color/font through `ThemeGenerator.SanitizeHexColor` / `SanitizeFont` with Default fallbacks, in addition to the test check.
- **Files modified:** Services/Evidence/OrganizationThemes.cs
- **Commit:** 7e6c9ec

**2. [Rule 2 - Security] ComposeFragment re-sanitizes theme colors and body font**
- The parameter theme's PrimaryColor/SecondaryColor/BodyFont go through the same sanitizers before reaching inline styles, so a caller-supplied theme cannot inject CSS.

Normalized `Helpers/HtmlEmailFormatter.cs` to CRLF after editing so the legacy template's verbatim string keeps its original line endings.

## Known Stubs

None.

## TDD Gate Compliance

RED (`test(05-05)`) commits precede GREEN (`feat(05-05)`) commits for both tasks.

## Self-Check: PASSED
