# Phase 06 Deferred Items

## From 06-07

- `HtmlEmailFormatter.BuildEmailTemplate` writes `theme.BodyFont` / `HeadingFont` and the theme hex colors into the template `<style>` block without sanitizing them. The business path (`ComposeHtml`) relies on themes having been sanitized upstream (`ThemeGenerator`, `OrganizationThemes`). `BulkMailFormatter.Compose` now passes a sanitized theme copy; the business path was left unchanged so Phase 5 output stays byte-identical (golden fixtures). Consider sanitizing inside `BuildEmailTemplate` in a later plan (no output change for already-sanitized themes).

## From 06-12

- `GenerationResetTests.StartOver_LockedFile_IsReported_RestStillDeleted` failed once in the full suite (3170/3171) and passed on an isolated rerun (12/12 GenerationResetTests). Looks timing/file-lock flaky under full-suite load; unrelated to 06-12 files.
