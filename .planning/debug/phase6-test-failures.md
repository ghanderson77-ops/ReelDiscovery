---
status: awaiting_human_verify
trigger: "Three failing tests: DialogRenderTests.ModelDialog_MediaPricesRow_MinimumSize (clipping at 144 DPI); AttachmentRepairTests.Repair_CancelDuringSecondAttachment_FirstCommitted_ThenThrows and Repair_FatalAfterPaidCall_NotRunSpendReturned (fail in VS full run only)"
created: 2026-10-05
updated: 2026-10-05
---

## Current Focus

hypothesis: (1) real clipping in ModelConfigurationDialog media row at 144 DPI; (2,3) order/concurrency dependence in AttachmentRepair or leaked static test hooks
test: run render test, capture clipping strings, view PNG; read AttachmentRepair + tests for static state
expecting: concrete clipped controls; identify shared static
next_action: user re-runs the full suite in VS Test Explorer at 150% and confirms

## Symptoms

expected: all three pass in any run order at the user's 150% DPI
actual: render test has 2 clipping entries; repair tests get RD-900005.0001 where .0002 expected under VS full run
errors: Assert.Empty failure (2 entries); Expected "RD-900005.0002" Actual "RD-900005.0001"; DoesNotContain matched AttachmentDocId RD-900005.0001
reproduction: render: run test at 144 DPI; repair: VS Test Explorer full run
started: render after DPI change 120->144; repair intermittently in full runs

## Eliminated

## Evidence

- timestamp: 2026-10-05
  checked: full clipping strings for ModelDialog_MediaPricesRow_MinimumSize at 144 DPI + PNG
  found: Remove and Reset Defaults ThemedButtons extend past the grid button FlowLayoutPanel (186x73); grid row only ~73 px tall because Size/MinimumSize were fixed pixels (980x480 / 820x360) while content scales with DPI
  implication: real clipping; fixed by scaling logical sizes with LogicalToDeviceUnits (commit b5831ce); render suite 113/113 clean
- timestamp: 2026-10-05
  checked: AttachmentRepair loop and test data numbering
  found: repair is sequential in plan order; RelevantDoc root attachments are Word .0001, Excel .0002, PowerPoint .0003. Failing runs show .0001 (Word) repaired, i.e. the committed Word part was NOT reused
  implication: production order is deterministic; Word was demoted to 'wanted' because harvesting its part from the EML failed
- timestamp: 2026-10-05
  checked: mutable statics in Services
  found: AttachmentHarvester.EmlCapOverride/PartCapOverride/TotalCapOverride (static fields) set to 100/300/400 by AttachmentHarvesterTests; GenerationJournal.SizeCapOverride=100 by GenerationJournalTests; CorpusPlanStore.SizeCapOverride=1024 by CorpusPlanStoreTests. xUnit runs these classes in parallel with AttachmentRepairTests
  implication: a cap set by a concurrent harvester test makes AttachmentRepair.HarvestMessage skip the Word part -> Word regenerated first -> cancel/fatal hit Word instead of Excel

- timestamp: 2026-10-05
  checked: injected EmlCapOverride=100 after Write in the two failing tests
  found: exact original failures (Expected RD-900005.0002 / Actual .0001; DoesNotContain matched) - 2/2
  implication: leaked harvest cap is the confirmed cause
- timestamp: 2026-10-05
  checked: after AsyncLocal fix, cap injected from another flow (Task.Run) 5 runs; Evidence namespace 5 runs; full suite
  found: 5/5 pass; 1175/1175 x5; full suite 3398/3398
  implication: fix verified

## Resolution

root_cause: (1) ModelConfigurationDialog used fixed-pixel Size/MinimumSize, so at 144 DPI the grid row shrank to ~73 px and clipped the Remove/Reset Defaults buttons. (2,3) Static test-only cap overrides (AttachmentHarvester, GenerationJournal, CorpusPlanStore) leaked from parallel test classes; a harvest cap made AttachmentRepair regenerate the committed Word part first.
fix: logical sizes scaled via LogicalToDeviceUnits and clamped to the working area (b5831ce); AsyncLocal-backed overrides + regression test (7d78ac6)
verification: render suite 113/113 at 144 DPI; leak sim 5/5; Evidence 5x; full suite 3398 passed
files_changed: [UserControls/ModelConfigurationDialog.cs, Services/Evidence/AttachmentHarvester.cs, Services/Evidence/GenerationJournal.cs, Services/Corpus/CorpusPlanStore.cs, ReelDiscovery.Tests/Evidence/AttachmentHarvesterTests.cs]
