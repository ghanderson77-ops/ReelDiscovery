---
phase: 06-attachments-non-relevant-corpus
plan: 03
subsystem: document-builders
tags: [openxml, word, excel, powerpoint, icalendar, rfc5545, themes]
requires: []
provides:
  - "DocumentMetadata { Author, Created, Modified } + WordSection, SimpleTable, SlideContent"
  - "OfficeDocumentService.CreateWordDocument(title, sections, table, theme, meta)"
  - "OfficeDocumentService.CreateExcelDocument(title, sheetName, headers, rows, theme, meta)"
  - "OfficeDocumentService.CreatePowerPointDocument(title, subtitle, slides, theme, meta)"
  - "internal static OfficeDocumentService.SafeSheetName(string?)"
  - "CalendarInviteSpec + CalendarService.CreateInvite(spec) (deterministic, RFC 5545-safe)"
  - "OrganizationThemes.ForOrganization(domain, organizationName)"
affects: [06 attachment builders/realizers, mailbox writer]
tech-stack:
  added: []
  patterns:
    - "New overloads alongside pinned legacy overloads; legacy PowerPoint routes through a shared BuildPresentation with the legacy subtitle"
    - "Numeric Excel cell only for a $ amount (CanonicalValueScanner.TryParseAmount) or a plain decimal with no leading zero"
    - "ICS lines folded on whole code points (Rune) at 75 octets"
key-files:
  created:
    - Models/DocumentMetadata.cs
    - ReelDiscovery.Tests/Documents/OfficeDocumentServiceTests.cs
    - ReelDiscovery.Tests/Documents/CalendarServiceTests.cs
  modified:
    - Services/OfficeDocumentService.cs
    - Services/CalendarService.cs
    - Services/Evidence/OrganizationThemes.cs
decisions:
  - "Model text going into Office parts is cleaned of control characters (except newline/tab) so XML serialization never throws on LLM output"
  - "ICS SUMMARY/LOCATION/CN collapse newlines to spaces; DESCRIPTION keeps them as TEXT-escaped \\n"
  - "Organizer with an invalid mailto is omitted like a bad attendee; a blank UID falls back to a SHA-256 of summary|start|organizer (never random)"
  - "Excel stylesheet: style 1 = bold TextLight on PrimaryColor header, style 2 = currency numFmt 164 \"$\"#,##0.00"
metrics:
  duration: 16min
  completed: 2026-10-04
  tasks: 2
  files: 6
---

# Phase 6 Plan 03: Office and Calendar Builder Upgrades Summary

Branded, OpenXmlValidator-clean Word/Excel/PowerPoint overloads with sender metadata and type-safe numeric cells, plus a byte-deterministic RFC 5545 invite builder and organization-named Office themes.

## What Was Built

**Task 1 - Office overloads** (`Services/OfficeDocumentService.cs`, `Models/DocumentMetadata.cs`)
- Word: organization header bar + accent line, title, divider, headed sections (heading runs in schema order RunFonts, Bold, Color, FontSize), body paragraphs with line breaks, and an optional bordered table with a theme-filled header row and a `tblGrid`. 0 validator errors.
- Excel: one sheet named via `SafeSheetName`, a stylesheet with a bold themed header style and currency format 164, column widths, and typed cells: `$` amounts become currency-styled numbers, plain decimals with no leading zero become numbers, and anything else (`007`, `02134`, `CO-007`, phone numbers) stays as text exactly as written.
- PowerPoint: the caller's subtitle goes on the title slide, followed by bullet slides (hanging-indent `buChar`). The legacy overload passes "Generated Email Dataset" to the same `BuildPresentation`, so its output is unchanged.
- `ApplyMetadata` sets Creator/LastModifiedBy/Created/Modified (UTC). No extended-properties part is written, and a test scans every zip entry to confirm "ReelDiscovery" never appears.

**Task 2 - Calendar and themes** (`Services/CalendarService.cs`, `Services/Evidence/OrganizationThemes.cs`)
- `CreateInvite(CalendarInviteSpec)`: Outlook PRODID, METHOD:REQUEST, a single VEVENT, UID/DTSTAMP taken from the spec, UTC times from `DateTimeOffset`, control characters stripped, TEXT escaping, DQUOTE-quoted CN with inner `"` turned into `'`, mailto filtered to `[A-Za-z0-9._%+\-@]` (dropped when no `@` is left), CRLF line endings, 75-octet folding that never splits a code point, and UTF-8 without a BOM.
- `ForOrganization(domain, name)` = `ForDomain` + the trimmed organization name. `ForDomain` is unchanged, so email HTML stays byte-identical.

## Commits

| Task | Gate | Commit | Message |
|------|------|--------|---------|
| 1 | RED | dba4d19 | test(06-03): add failing tests for branded Word/Excel/PowerPoint overloads |
| 1 | GREEN | cff4a5c | feat(06-03): branded, validator-clean Word/Excel/PowerPoint overloads |
| 2 | RED | 4f45b0d | test(06-03): add failing tests for deterministic invites and organization themes |
| 2 | GREEN | 9940ba4 | feat(06-03): deterministic RFC 5545 invites and organization-named themes |

## Verification

- `--filter OfficeDocumentServiceTests|DocumentRegressionTests`: 28/28 pass (the legacy Word output still has exactly the 3 pinned errors)
- `--filter CalendarServiceTests`: 12/12 pass
- Full suite: 2758/2758 pass (baseline was 2722, plus 36 new tests)
- App build: 0 warnings
- Acceptance greps: SafeSheetName = 1, the WordSection and SlideContent signatures = 1 each, "Generated Email Dataset" = 1 (legacy), PackageProperties.Creator >= 1, CreateInvite = 1, Guid.NewGuid()/DateTime.UtcNow = 1 each (legacy only), ForOrganization = 1

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 2 - Correctness] Control characters in model text stripped in Office builders**
- **Found during:** Task 1
- **Issue:** Untrusted LLM text with C0 control characters would make the OpenXml XML writer throw.
- **Fix:** A private `Clean()` drops control characters (newline and tab are kept) from every text going into the new overloads.
- **Commit:** cff4a5c

**2. [Rule 1 - Bug] Empty slide body produced invalid txBody**
- **Found during:** Task 1
- **Issue:** A slide with no bullets (or legacy empty content) produced `p:txBody` with no `a:p`, which is a schema violation.
- **Fix:** `CreateContentTextShape` adds one empty paragraph when none was added. Output for non-empty content is unchanged.
- **Commit:** cff4a5c

**3. [Rule 1 - Correctness] ICS newlines in single-line fields**
- **Found during:** Task 2
- **Issue:** The plan said to strip `\p{Cc}` before TEXT escaping, which would also delete DESCRIPTION paragraph breaks.
- **Fix:** DESCRIPTION keeps line breaks as escaped `\n`. SUMMARY, LOCATION and CN collapse them to spaces. Every other control character is still stripped, so line injection is still impossible.
- **Commit:** 9940ba4

## Known Stubs

None.

## Self-Check: PASSED
