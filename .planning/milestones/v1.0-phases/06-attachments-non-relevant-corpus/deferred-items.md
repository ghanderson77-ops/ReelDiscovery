# Phase 06 Deferred Items

## From 06-07

- `HtmlEmailFormatter.BuildEmailTemplate` writes `theme.BodyFont` / `HeadingFont` and the theme hex colors into the template `<style>` block without sanitizing them. The business path (`ComposeHtml`) relies on themes having been sanitized upstream (`ThemeGenerator`, `OrganizationThemes`). `BulkMailFormatter.Compose` now passes a sanitized theme copy; the business path was left unchanged so Phase 5 output stays byte-identical (golden fixtures). Consider sanitizing inside `BuildEmailTemplate` in a later plan (no output change for already-sanitized themes).

## From 06-12

- `GenerationResetTests.StartOver_LockedFile_IsReported_RestStillDeleted` failed once in the full suite (3170/3171) and passed on an isolated rerun (12/12 GenerationResetTests). Looks timing/file-lock flaky under full-suite load; unrelated to 06-12 files. **Addressed in 06-25 (57d663b):** the test opened the just-written EML with FileShare.None, which fails while antivirus or the indexer holds any handle on it. It now retries that open (bounded). Production `GenerationReset.TryDelete` has no transient-lock retry (unlike `CaseStore.WriteAtomic`). That is left as is; see the 06-25 summary.

## From 06-21

- Privilege incidence (not the marking mix) differs between populations on a production-planner plan at 250 emails: 1 relevant thread is privileged, 0 non-relevant threads are. `NonRelevantSkeletonBuilder.AssignDailyPrivilege` privileges about 2% of daily-business threads and guarantees one only when there are 50 or more of them, so small corpora can have privilege banners on relevant mail only. FillerAssignments applies the same 85% / 40% mix to whatever is privileged, so `DistributionParityTests.Parity_PrivilegeMarkers` passes. The remaining signal is planner-level (Phase 4). Hand to the Phase 7 leakage audit: compare privileged-thread share per population at 250 / 1000 / 5000 emails.
