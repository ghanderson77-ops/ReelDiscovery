# Deferred Items - Phase 01

| Found in | Item | File | Notes |
|----------|------|------|-------|
| 01-01 Task 2 | Word builder emits `w:color` after `w:sz` inside `w:rPr` (OOXML schema-order violation, `Sch_UnexpectedElementContentExpectingComplex`) | `Services/OfficeDocumentService.cs` lines ~41-45, 67-70, 95-97 | Existed before this plan and is unrelated to the OpenXml 3.5.1 upgrade. Pinned as baseline (3 errors) in `DocumentRegressionTests.Word_OpensAndValidates`. Fix: construct `Color` before `FontSize` in each `RunProperties`, then set the baseline to 0. |
