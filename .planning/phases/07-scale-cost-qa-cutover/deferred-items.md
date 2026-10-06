# Deferred Items - Phase 07

- [07-08] `Forms/WizardForm.cs(153,20)` CS8603 (possible null return from `ToolTip.GetToolTip` in `CostTooltipForTest`, added by 07-12 commit a8f811d) makes `dotnet build -warnaserror` fail. Pre-existing, out of 07-08 scope. **Resolved** by orchestrator (null-coalesce to "").
