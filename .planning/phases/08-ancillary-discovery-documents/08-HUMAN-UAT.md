---
phase: 08-ancillary-discovery-documents
status: deferred
awaiting: deferred by user 2026-10-08
source: 08-19-PLAN.md Task 2 (checkpoint:human-verify, auto-approved: user said "yolo", no live provider calls)
---

# Phase 8 Human UAT (deferred)

The 08-19 human-verify checkpoint was auto-approved at the user's request. Every automated gate passed (both
-warnaserror builds, 4289 non-render tests, 150 render tests). The manual items below were not performed and remain
open.

Sample files (render output folder):
`C:\Users\ghand\source\repos\ReelDiscovery\ReelDiscovery\obj\gsd-artifacts\bin\ReelDiscovery.Tests\debug\renders\`
- `discovery-sample-deposition.docx`
- `discovery-sample-deposition.pages.txt` (code: 40 pages total, testimony pages 4-37, 25 lines per page)
- `discovery-sample-rfp.docx`

## Items

| # | Item | Requirement | Status |
|---|------|-------------|--------|
| 1 | Open `discovery-sample-deposition.docx` in Word: page count equals 40, line 25 on the same page as lines 1-24 on every page, Courier New, EXCERPT caption, index page numbers for the examination and each exhibit. Open `discovery-sample-rfp.docx`: caption with case number, definitions, instructions, numbered requests, signature block, certificate of service. | ANC-01, D-23 | deferred |
| 2 | Inspect the `discovery-*`, `dialog-regenerate-*`, `wizard-shell-*` and Case Summary PNGs at 150% (144 DPI): no clipped/ellipsized text, aligned controls, one Primary button per screen. | D-03 | deferred |
| 3 | Live run with Claude on a small litigation case with a guide (e.g. one with "Michael Scott"): Standard, 2 witnesses; names verbatim, RFA dates/amounts match Key Facts, each RFP tied to a case issue, no (a)/(b) interrogatory parts. | ANC-02..ANC-05, D-02 | deferred |
| 4 | Repeat item 3 with OpenAI (one deposition and one RFP set). | ANC-02..ANC-05 | deferred |
| 5 | Regenerate with guidance replaces the document; editing a fact title marks rows Out of date without auto-regeneration. | ANC-04 | deferred |
| 6 | Case Summary Discovery row text, Discovery Documents button navigation, "discovery documents $..." in the Generation cost row. | ANC-05 | deferred |
| 7 | Regenerate while the .docx is open in Word: dialog warns, row shows Not saved, Save Again succeeds after closing Word. | ANC-04 | deferred |
