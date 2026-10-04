# Phase 06 Deferred Items

## From 06-07

- `HtmlEmailFormatter.BuildEmailTemplate` writes `theme.BodyFont` / `HeadingFont` and the theme hex colors into the template `<style>` block without sanitizing them. The business path (`ComposeHtml`) relies on themes having been sanitized upstream (`ThemeGenerator`, `OrganizationThemes`). `BulkMailFormatter.Compose` now passes a sanitized theme copy; the business path was left unchanged so Phase 5 output stays byte-identical (golden fixtures). Consider sanitizing inside `BuildEmailTemplate` in a later plan (no output change for already-sanitized themes).
