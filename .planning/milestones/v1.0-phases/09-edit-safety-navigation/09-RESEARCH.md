# Phase 9: Edit Safety & Navigation - Research

**Researched:** 2026-10-11
**Domain:** WinForms wizard state gating (approval / staleness), derived staleness of discovery documents, wizard navigation
**Confidence:** HIGH (all findings come from reading this repo's code and planning docs; no external libraries are involved)

<user_constraints>
## User Constraints

No CONTEXT.md exists for this phase (auto mode). The constraints come from the roadmap, the v1.0 audit and earlier locked decisions.

### Locked decisions inherited from earlier phases (do not contradict)
- **Phase 4 D-12:** "Next on Corpus Settings runs planning." (Implemented as an auto-plan when Review Plan is entered with no plan; 04-CONTEXT l.82.)
- **Phase 4 D-25:** A stale guide withdraws approval. "Keep This Plan" dismisses the stale banner for the current guide hash only. Approval withdrawn for staleness **stays withdrawn**; approving again rewrites the protocol from the current guide (WR-11).
- **Phase 4 T-04-34 / T-04-62:** Nothing silently re-plans an approved plan, and a case opened from disk never starts a planning run by itself.
- **Phase 5 U3 / rule 5:** Generate Corpus never auto-starts, because generation spends real money.
- **Phase 8 D-07:** Discovery Documents sits between Generate Corpus and Case Summary. Its Next is always enabled, and the step must be reachable **without a corpus**.
- **Phase 8 D-09:** There is no `CaseStage` value for Discovery.
- **Phase 8 D-27:** Discovery staleness is content-hash based (guide, core, caption, corpus PlanId) and derived when read. Nothing writes a "stale" flag.
- **Project memory:** verify WinForms UI at 150% DPI (144). Never use names from fictional works.

### Claude's discretion (the audit says W2/W3 need "small design calls")
These designs are recommended below. The planner should lock them:
- **W1:** where approval is withdrawn, the new Generate Corpus "guide changed" card, and the Case Guide dependency notice.
- **W2:** how Review Plan is passed through without planning, and the direct way to Discovery Documents.
- **W3:** the per-exhibit staleness mechanism.
- **W6:** the Case Summary button text and visibility.

### Deferred / out of scope (Phase 10 or backlog)
- W4 (relabel overwrite), W5 (provider switch strands batches), W8 (dead legacy types), WR-A06, WR-A08 belong to Phase 10.
- CR-03 (replanning saved exhibit numbers) is a backlog design decision.
- The answer key being reset to "Planned" by re-approval after generation is W4-adjacent (Phase 10). Note it, but do not fix it here.
</user_constraints>

<phase_requirements>
## Phase Requirements

| ID | Description | Research Support |
|----|-------------|------------------|
| GUIDE-09 | Editing an upstream artifact warns the user and marks downstream artifacts stale | W1: withdraw approval from the Case Guide save funnel; Case Guide dependency notice; Generate Corpus guide-changed gate |
| PLAN-06 | Validator checks the plan (counts, ratio, active ranges, fact coverage) | W1: a plan validated against guide A can no longer be run against guide B without re-approval |
| PLAN-07 | User sees plan summary and approves or re-plans before generation | W1: approval is enforced at Generate Corpus entry, in RunCoreAsync, on reopen and on Case Summary entry |
| KEY-03 | Labels come from the plan at generation time | W1: labels and custodians cannot drift from a deleted or renamed cast during a run |
| ANC-01 | Optional deposition excerpts | W2: reachable without a plan. W3: depositions whose exhibits changed read Out of date |
| ANC-03 | Optional RFAs keyed to key facts | W3: genuineness RFAs citing DocIDs read Out of date when those emails change |
| ANC-05 | Ancillary docs consistent with core, cast and facts | W3: new `StaleSource.Exhibits` in `DiscoveryStaleness.Check` |
| QA-01 | Blind review: relabel or regenerate | W6: after Regenerate Thread, Case Summary links straight to Generate Corpus |
| QA-03 | Timeline/value scans flag emails | W6: same path (scan findings lead to Regenerate Thread, then Continue) |
| RUN-06 | Cancel and keep what was generated | W6: a partial run is resumable from Case Summary in one click |
</phase_requirements>

## Project Constraints (from CLAUDE.md)

- C# / .NET WinForms. The app and tests target `net10.0-windows` (global.json pins SDK 10.0.401; CLAUDE.md's "net8" text is stale; csproj files are authoritative). UI is built in code, not the Designer.
- Naming: `*UiText` static pure-copy classes (unit tested), `Step*` controls, `_camelCase` fields, `ct` last parameter. No records, no primary constructors, no `#region`.
- Errors: catch `OperationCanceledException` before `Exception`. Never use a bare `catch` in core logic, and comment every swallowed exception.
- Every state transition goes through a service class (e.g. `CorpusApproval`). Steps are UI glue only (04-22 precedent).
- Copy lives in `CorpusUiText` / `GenerationUiText` / `DiscoveryUiText` / `CaseGuideUiText` / `QaUiText` as pure functions. Labels set `UseMnemonic = false`.
- Keep the build warning-free (nullable on).
- GSD workflow: edits only through `/gsd-execute-phase`.
- Project memory: verify UI at 150% DPI. Small UAT bugs are fixed inline with filtered tests.

## Summary

All four gaps are small, local changes to existing mechanisms. None needs a new library.

1. **W1 (stale guide survives approval).** The machinery already exists: `CorpusApproval.IsGuideStale` (content-only `CaseGuide.ComputeHash()` against `plan.GuideHash`, minus a kept/dismissed hash) and `WithdrawApprovalIfStale`. Only `StepReviewPlan.OnEnterStepAsync` (StaleBanner branch, `StepReviewPlan.Run.cs:113`) calls it. Generate Corpus gates only on `Corpus.Approved` (`StepGenerateCorpus.Run.cs:142, 390`). Generation reads **only** the live guide from the draft; settings come from `plan.Settings`, verified by grep with no `Corpus.Settings` reads under `Services/Evidence`. That makes the guide the single live dependency that can corrupt output. The fix is to call the existing withdrawal at every point where the guide or the run can meet:
   - the Case Guide step's save funnel
   - case reopen
   - Generate Corpus entry and `RunCoreAsync`
   - Case Summary entry

   Add a dedicated "guide changed" state card on Generate Corpus and a dependency notice on Case Guide.
2. **W2 (Discovery unreachable without a plan).** `CorpusUiText.PlanNextGate` returns `NoPlan` and blocks. Review Plan also auto-plans on **any** entry with no plan unless `SuppressAutoPlanOnce` is set, and only a reopen sets that flag. As a result, going Back from Generate Corpus to Review Plan with no plan starts a paid run today. The fix keeps D-12 and makes it explicit and one-shot: only Corpus Settings → Next may auto-plan. Every other entry shows the existing "No corpus plan yet" card, and Next with no plan becomes a confirm-and-skip.
3. **W3 (Start Over / Regenerate Thread keep the PlanId).** There is an exact in-repo precedent. CR-B02 already solved "this email was regenerated since" for blind-review verdicts with `BlindReviewer.WrittenStamps(replay)`, which maps DocId to the journal Utc. Store a DocId → ThreadDone-Utc map for the exhibits each discovery document actually cites, and compare it in `DiscoveryStaleness.Check` as a new `StaleSource.Exhibits`. Because it is derived (D-27), Start Over and Regenerate Thread need **no** code changes. Deleted journal lines and new ThreadDone lines change the stamps by themselves.
4. **W6.** `GenerationUiText.MailboxesRow` sets `ShowSecondary = !input.Generated`. Make it always true, and add a secondary caption: "Generate Emails" before anything is generated, "Generate Corpus" after.

**Primary recommendation:** Reuse `CorpusApproval.WithdrawApprovalIfStale` and the CR-B02 `WrittenStamps` pattern. Add one new card kind, one new stale source and one one-shot wizard flag. Put every decision in pure `*UiText` / service functions so they are unit-testable, and add render tests at 144 DPI for every changed surface.

## Architectural Responsibility Map

| Capability | Primary Tier | Secondary Tier | Rationale |
|------------|-------------|----------------|-----------|
| Guide-vs-plan staleness and approval withdrawal | Service (`Services/Corpus/CorpusApproval.cs`) | Steps call it | All approval transitions already live here (04-22) |
| Generate Corpus entry decision / new card | Pure UI-text (`GenerationUiText.EntryDecision`, `StateCard`) | `StepGenerateCorpus` glue | The existing entry-decision pattern is pure and unit-tested |
| Review Plan entry / Next gate | Pure UI-text (`CorpusUiText.ReviewPlanEntryDecision`, `PlanNextGate`) | `StepReviewPlan.Run.cs` | Same |
| Auto-plan intent flag | `WizardState` (session, not persisted) | `StepCorpusSettings` sets it, `StepReviewPlan` consumes it | Replaces `SuppressAutoPlanOnce` |
| Exhibit staleness | Service (`Services/Discovery/DiscoveryStaleness.cs`) + model (`DiscoveryDocument`, `DiscoveryCorpus`) | `CorpusDetector`, `DiscoveryStepModel`, Case Summary `LoadQa` | D-27: derived when read, never written as a flag |
| Case Summary Mailboxes button | Pure UI-text (`GenerationUiText.MailboxesRow`) | `StepCaseSummary` | Existing row-content pattern |
| Case Guide dependency notice | Pure UI-text (`CaseGuideUiText`) | `StepCaseGuide` banner row | Existing banner pattern |

## Standard Stack

No new packages. Existing stack (verified in csproj files):

| Component | Version | Use in this phase |
|-----------|---------|-------------------|
| .NET SDK | 10.0.401 (global.json, `rollForward: latestFeature`) | build/test [VERIFIED: global.json, `dotnet --list-sdks`] |
| xUnit | 2.9.3, Microsoft.NET.Test.Sdk 17.14.1, xunit.runner.visualstudio 3.1.4 | tests [VERIFIED: ReelDiscovery.Tests.csproj] |
| System.Text.Json | in-box | new `ExhibitStamps` dictionary round-trips in the discovery sidecar |

**Installation:** none.

## Architecture Patterns

### Data flow after the fix (W1)

```
Case Guide edit ──► StepCaseGuide.SaveAsync (single save funnel)
                      │  CorpusApproval.WithdrawApprovalIfStale(draft) → Approved=false, Revision++
                      ▼
                  case.json (Approved=false persisted)

Reopen (StepCaseScenario after CaseStore.Load) ──► WithdrawApprovalIfStale ──► NavigateToStage(stage)

Generate Corpus entry ─► GuideStale? ─yes─► WithdrawApprovalIfStale + save ─► "Guide changed" card [Review Plan]
                          │no
                          ▼
                       existing EntryDecision (NotApproved / PlanChanged / Unreadable / Ready / dashboards)

Generate/Continue/Retry click ─► RunCoreAsync ─► GuideStale? ─yes─► withdraw, show card + status, return (nothing billed)

Case Summary entry ─► WithdrawApprovalIfStale (+save) ─► plan row shows stale (existing PlanStatusLine)
Case Summary "Generate Emails/Generate Corpus" ─► NavigateToStage(Generation) ─► entry gate above
```

### Data flow (W3)

```
Discovery generate: CorpusDetector.Detect → DiscoveryCorpus{ Candidates, WrittenStamps (DocId→ThreadDone Utc) }
   → DiscoveryGenerator builds content (Deposition.Exhibits[].DocId, Written.Requests[].DocId)
   → DiscoveryStaleness.Stamp: doc.ExhibitStamps = {cited DocId → corpus.WrittenStamps[DocId]}

Read side (Discovery step after probe; Case Summary LoadQa):
   current = WrittenStamps(replay)  (null = unknown; {} = journal gone)
   Check(doc, draft, current): Guide → Core → Caption → CorpusPlan → Exhibits
     Exhibits stale ⇔ any cited DocId missing from current OR current[DocId] != doc.ExhibitStamps[DocId]

Start Over: journal deleted → current = {} → every doc citing exhibits = Out of date
Regenerate Thread: ThreadInvalidated removes LastByThread → DocId missing → Out of date;
                   Continue writes a new ThreadDone (new Utc) → still Out of date until regenerated
```

### Pattern 1: W1 recommended design (lock this)

1. **Service:** keep `CorpusApproval.WithdrawApprovalIfStale(draft)` as the one transition. Optionally add `CorpusApproval.IsApprovedForCurrentGuide(draft) => Approved && !IsGuideStale(draft)` for read-only checks. Do **not** change `KeepStalePlan` or `RecordApproval` semantics (D-25).
2. **Case Guide (the "wherever the edit happens" clause):** in `StepCaseGuide.Run.cs` `SaveAsync()` (the single funnel used by `QueueSave`, `NotifyEdited`, generation runs, `OnEnterStepAsync`, `OnLeaveStepAsync`), call `if (CorpusApproval.WithdrawApprovalIfStale(Draft)) Draft.Revision++;` **before** `await _state.CaseStore.SaveAsync(Draft)`.
   - The close path calls `_ = step.OnLeaveStepAsync()` without awaiting it and then `SaveNow`. Only the synchronous prefix runs before `SaveNow`, so the withdrawal must sit before the first `await`.
   - Reverting an edit does **not** restore approval. That matches D-25 ("stays withdrawn").
3. **Reopen:** in `StepCaseScenario` immediately after `_state.Case = result.Draft!;` (next to `MarkOpenedFromDisk`, l.866-867), call the withdrawal and bump Revision. `IsGuideStale` works without loading the plan, because it falls back to `corpus.PlanGuideHash`.
4. **Generate Corpus entry:** add `GuideStale` to `GenerationEntryInput`, computed **before** withdrawal from `CorpusApproval.IsGuideStale(Draft)`, and a new `GenerationEntry.GuideChangedCard` checked right after `Running` and before `!HasPlan || !Approved`. In `OnEnterStepAsync`, when the decision is GuideChangedCard: withdraw, `Revision++`, `await SaveAsync()`, then show the card. Skip the journal read.
5. **New card** `GenerationStateCard.GuideChanged`. Recommended copy:
   - Heading: "The case guide changed after the plan was approved"
   - Body: "Emails are written from the approved plan and the guide it was built from. Open Review Plan to keep this plan and approve it again, or re-plan from the current guide." When emails exist, add: "The {n} emails already written stay in place."
   - Button: "Review Plan" (Primary).
   - Add `GenerateCorpusRenderState.GuideChanged` for the render harness.
6. **`RunCoreAsync` guard (defense in depth):** replace the silent `return` at l.389-391 with:
   - stale: withdraw, save, show the GuideChanged card and set a Warning status ("Generation did not start: the case guide changed after the plan was approved.")
   - not approved: show the NotApproved card instead of returning silently.
   - Nothing is billed. `CreateRunGateway` comes after this point.
7. **Case Summary entry:** in `OnEnterStepAsync`, before the existing save, call the withdrawal and bump Revision. Its rows already render stale (`PlanRowStale`, `PlanStatusLine(approved, …, stale)`).
8. **Case Guide warning (SC1 last clause).** Add a dependency notice computed by a pure function, e.g. `CaseGuideUiText.DependencyNotice(hasPlan, approved, guideStale, emailsWritten)`:
   - **Before an edit** (approved plan or generated emails, guide not stale): Info or muted tone. "An approved corpus plan{ and N generated emails} use this guide. Editing it withdraws plan approval; you will review the plan again before generating more emails."
   - **After an edit** (`IsGuideStale` and a plan exists): Warning tone. "The corpus plan was built from an earlier version of this guide. Plan approval was withdrawn. Open Review Plan to keep the plan or re-plan." Button: **Review Plan** (`NavigateToStage(CaseStage.ReviewPlan)`).
   - Counts come from `Draft.Corpus.Generation` (cached in case.json), so the UI thread never reads the journal.
   - Placement: implement it as the lowest-priority `BannerVariant` (`PlanDependency`) in the existing banner row, so StaleCore, StaleDownstream and NeedsReview still win. Alternatively use a separate one-line notice under the header if UI-SPEC prefers. The UI-SPEC step should finalize this.

### Pattern 2: W2 recommended design (lock this)

1. **One-shot intent flag.** Replace `WizardState.SuppressAutoPlanOnce` (set on reopen, cleared by Corpus Settings Next and Plan Corpus) with the inverse `WizardState.PlanOnEntryOnce`.
   - `StepCorpusSettings.ValidateStepAsync` sets it to true on success. That site already calls `ClearsAutoPlanSuppression`, so rename it to `RequestsPlanning`.
   - `StepReviewPlan.OnEnterStepAsync` **reads and clears it on every entry**. Clear it before the entry decision, so a failed or cancelled run does not re-trigger it.
   - `MarkOpenedFromDisk` sets it to false.
   - **Result:** D-12 holds, because Next on Corpus Settings still plans. Back from Generate Corpus, Case Summary's Review Plan button and reopen never auto-plan. `ReviewPlanEntryDecision`'s `SuppressAutoPlan` becomes `!PlanRequested`. AutoReplan (unapproved plan with changed settings) also requires the flag, which can only be set by leaving Corpus Settings with Next, so it is user-asked.
2. **Next with no plan becomes a skip with confirm.** `PlanNextGate(running, hasPlan:false, …)` returns a new `PlanNextGateKind.AskSkip` with a YesNo confirm:
   - Title "Continue Without a Plan"
   - Text: "Continue without a corpus plan? Generate Corpus needs an approved plan before it writes emails, but you can write discovery documents now. You can plan the corpus later from Review Plan."
   - Default button: Yes. It is non-destructive and the user clicked Next explicitly.
   - **Unapproved plan:** keep AskApprove (M-P2) unchanged. If the user wants to skip with an unapproved plan, recommend a third option is **not** added; they can answer No and use the skip link below. Keep this minimal.
3. **Direct route to Discovery without passing planning.** Add a secondary link-style button "Skip to Discovery Documents" on Review Plan's **No corpus plan yet** card (`PlanStateCard.NoPlan` content gains `SecondaryText`). It calls `NavigateToStep(typeof(StepDiscoveryDocuments))`, which skips Generate Corpus.
   - A user arriving forward from Corpus Settings still triggers D-12 planning, which is the user asking. They can Cancel, which lands on the NoPlan / Failed card with the skip.
   - **Recommended addition for a truly zero-cost path:** the same "Skip to Discovery Documents" link on Corpus Settings when no plan exists. Flag this for the user/UI-SPEC, because it adds a control to Corpus Settings.
4. **Generate Corpus NotApproved card copy:** when there is no plan, append "Or click Next to write discovery documents without emails." Generate Corpus Next already always passes (`ValidateStepAsync => !_isGenerating`).
5. Discovery itself already works without a plan. `CorpusDetector.Detect` returns `None("No corpus plan.")`, and the step never checks `Approved`. No change is needed there.

### Pattern 3: W3 recommended design (lock this)

- **Model:**
  - `DiscoveryDocument.ExhibitStamps : Dictionary<string, DateTime>` (DocId → ThreadDone Utc). Normalize it: null becomes empty, and drop null or whitespace keys.
  - `DiscoveryCorpus.WrittenStamps : Dictionary<string, DateTime>?`. **null = unknown**, empty = no written emails.
- **Stamp source:** ThreadDone Utc only. Do **not** reuse `BlindReviewer.WrittenStamps` as is, because it advances the stamp when a later attachment completes. Finishing a run's attachments or "Create Skipped Media" would then mark every deposition Out of date even though the email text did not change.
  - Add `DiscoveryStaleness.EmailStamps(JournalReplay? replay)`, the same loop as `WrittenStamps` minus the attachment pass, or add a parameter `includeAttachments` to `WrittenStamps`.
- **Detector** (`CorpusDetector.Detect`):
  - Usable path: set `WrittenStamps` from the replay it already has.
  - `!replay.Exists` (journal deleted by Start Over or never run): set `WrittenStamps = {}` (known-empty).
  - Unreadable, PlanChanged, no plan, exception: leave it null.
  - The "No relevant emails written" path: stamps from the replay.
- **Stamp:** `DiscoveryStaleness.Stamp(doc, draft, corpus)` collects cited DocIds from `doc.Deposition?.Exhibits[].DocId` and `doc.Written?.Requests[].DocId` (non-empty) and stores `corpus.WrittenStamps[id]` for each. It runs at `DiscoveryGenerator.cs:443` after content exists. `SaveAgainAsync` must not re-stamp. It currently does not call `Stamp`; keep it that way.
- **Check:** `Check(DiscoveryDocument doc, CaseDraft draft, IReadOnlyDictionary<string, DateTime>? currentStamps = null)`. After CorpusPlan:
  - if `currentStamps != null`, take the cited DocIds (from content, not only `ExhibitStamps`, so legacy docs work);
  - stale if any id is missing from `currentStamps`, or if `doc.ExhibitStamps.TryGetValue(id, out s) && s != currentStamps[id]`;
  - legacy docs without stamps get the existence check only.
- **Callers:**
  - `DiscoveryStepModel.Build` uses `corpus.WrittenStamps`. The pre-probe placeholder `DiscoveryCorpus.None()` has null stamps, so nothing flickers to Out of date before the probe finishes.
  - `DiscoveryStepModel.SavedRows(..., currentStamps)` from `StepCaseSummary.LoadQaWithDiscovery`. Compute it from the replay `LoadQa` already performs (do not replay the journal twice). When `plan == null`, pass null.
  - Case Summary already calls `RefreshQaAsync()` after Regenerate Thread (`StepCaseSummary.cs` ~2273), so the discovery row updates automatically.
- **Copy:** add `StaleSource.Exhibits`. `RowStatusFull` currently says "Out of date with the case guide: {what} changed…", which reads wrong for exhibits. Add a dedicated branch: "Out of date: an email it cites as an exhibit was regenerated or deleted after this document was written. Click Regenerate... to write it again."
- **Optional (discretion):** append a conditional clause to `CG2StartOver` and to the Regenerate Thread confirmation: "Discovery documents that cite these emails will show Out of date." Show it only when the sidecar has documents with exhibits.

### Pattern 4: W6 recommended design (lock this)

- `SummaryRowContent` gains `SecondaryText`. In `MailboxesRow`:
  - `ShowSecondary = true`
  - `SecondaryText = input.Generated ? "Generate Corpus" : "Generate Emails"`
  - Naming a step matches the other navigation buttons ("Review Plan", "Discovery Documents").
- `StepCaseSummary.RefreshGenerationRows` sets `_btnGenerateEmails.Text` and **refits the button** (`ThemedButton.FitToText(0, buttonHeight)`, the same as `StepGenerateCorpus` l.~1070). Otherwise the longer caption clips at 150% DPI.
- Leave the button visible even when the plan is stale or unapproved. The Generate Corpus entry gate (W1) shows the right card. This satisfies "including via the Case Summary button".
- Leave the C-Q1 copy "Go to Generate Corpus and click Continue Generating" (`QaUiText.cs:313`) as is; it is now accurate.

### Anti-Patterns to Avoid
- **Do not** add a `GenerationStateCard` value without updating `ClickStateActionAsync` (`StepGenerateCorpus.Run.cs:306-318`). Its `default:` branch calls **`StartOverAsync()`**, so an unhandled GuideChanged card would delete the corpus.
- **Do not** put approval withdrawal inside `CaseStore.SaveAsync`. That hides a domain transition in persistence and would also fire from Review Plan between Keep This Plan and Approve.
- **Do not** compare raw hashes in steps. Use `CorpusApproval.IsGuideStale` (it honors the D-25 dismissed hash). The `PlanRowStale` doc comment says the same.
- **Do not** write a "stale" flag into `discovery-documents.json` from `GenerationReset` or `ThreadInvalidation` (D-27: derived only). It would also need sidecar locking (CX-03).
- **Do not** replay the journal on the UI thread (T-05-45 / T-08-42; 200 MB cap). Reuse the replay in `CorpusDetector` and `LoadQa`.

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| Guide-changed detection | new hash or diff of cast/facts | `CorpusApproval.IsGuideStale` / `CaseGuide.ComputeHash()` | Content-only projection (A16) already excludes UI state; honors kept plans |
| Approval withdrawal | ad hoc `Approved = false` in steps | `CorpusApproval.WithdrawApprovalIfStale` | One tested transition (CorpusApprovalTests l.311-357) |
| "Email regenerated since" | file mtimes or EML hashing | journal ThreadDone Utc (CR-B02 `WrittenStamps` precedent) | Survives copies in multiple custodian folders; deterministic; already tested |
| Exhibit list | parsing the .docx | `Deposition.Exhibits[].DocId`, `Written.Requests[].DocId` | Structured content is saved in the sidecar |
| Button sizing after a caption change | manual widths | `ThemedButton.FitToText` | DPI-correct |

## Runtime State Inventory

This is not a rename phase, but persisted-state changes matter:

| Category | Items Found | Action Required |
|----------|-------------|------------------|
| Stored data | `case.json` `Corpus.Approved` (existing cases may hold Approved=true with a stale guide) | Code only. Reopen and entry gates withdraw it lazily. No migration. |
| Stored data | `Discovery/discovery-documents.json`: existing documents have no `ExhibitStamps` | Code only. Missing property deserializes to empty; legacy docs get the existence-only check. No migration. Bump nothing (additive property). |
| Live service config | None (desktop app) | None |
| OS-registered state | None | None |
| Secrets/env vars | None | None |
| Build artifacts | None | None |

## Common Pitfalls

### Pitfall 1: The GuideChanged card falls into Start Over
**What goes wrong:** `ClickStateActionAsync`'s `default:` runs `StartOverAsync()`.
**How to avoid:** Add an explicit `case GenerationStateCard.GuideChanged:` that navigates to Review Plan. Add a test asserting that the card action never calls reset (e.g. via `GenerateCorpusStepTests`: the journal still exists after the click).

### Pitfall 2: Withdrawal after the first await on close
**What goes wrong:** `WizardForm.FlushCurrentCaseStep` fires `_ = step.OnLeaveStepAsync()` and then `SaveNow`. Code after the first `await` runs after the synchronous save, so approval is not persisted on close.
**How to avoid:** Withdraw synchronously before `await`. Reopen withdrawal is the backstop.

### Pitfall 3: Stamp flicker on Discovery entry
**What goes wrong:** The step paints with `DiscoveryCorpus.None()` before the background probe. If None carries `{}`, every exhibit-citing row shows Out of date for a moment, or permanently if the probe fails.
**How to avoid:** null means unknown and skips the check. Only `!replay.Exists` produces `{}`.

### Pitfall 4: Attachment completions mark depositions stale
**What goes wrong:** Reusing `BlindReviewer.WrittenStamps` advances the stamp when attachments finish later.
**How to avoid:** Use ThreadDone Utc only (Pattern 3).

### Pitfall 5: Auto-plan re-fires after cancel or failure
**What goes wrong:** If the one-shot flag is cleared only on success, Back then Forward re-plans.
**How to avoid:** Clear it on every Review Plan entry, before the decision.

### Pitfall 6: Visiting Case Guide changes the hash by itself
**What goes wrong:** `StepCaseGuide.OnEnterStepAsync` runs `CaseGuideEditing.WidenActiveRanges`, and `ActiveFrom`/`ActiveTo` are in `ComputeHash`. With the new save-funnel withdrawal, a hash change on entry would withdraw approval with no user edit.
**Analysis:** Planner-added knowers are chosen from people active on the fact date (`ThreadSkeletonBuilder` l.344, 838), and widening already ran when the guide was generated. So this should be a no-op in practice [ASSUMED from code reading].
**How to avoid:** Add a regression test: plan a fixture, call `ApplyNewPlanAsync`, then assert that `WidenActiveRanges(draft)` returns false and `IsGuideStale(draft)` is false.

### Pitfall 7: Button caption change clips at 150%
**How to avoid:** `FitToText` after setting Text. Add a CaseSummary render test of the generated-partial state at 144 DPI with `ClippingDetector`.

### Pitfall 8: Existing tests encode the old behavior
These must be updated deliberately, not deleted:
- `ReviewPlanLogicTests.Entry_NoPlanAutoPlans` (l.69)
- the suppress test (l.77)
- `PlanNextGate` NoPlan (l.434)
- `CaseScenarioUiLogicTests.MarkOpenedFromDisk_SuppressesAutoPlanOnce` (l.113)
- `CorpusSettingsStepLogicTests.ClearsAutoPlanSuppression_*` (l.96)
- `WizardShellRenderTests` l.92 (`SuppressAutoPlanOnce = true`)
- `GenerationUiTextTests` l.1627 (`Assert.False(row.ShowSecondary)` for the generated state)

### Pitfall 9: Re-approval after generation resets the answer key file
**What goes wrong:** `CorpusApproval.BuildFiles` writes the key from the plan only (Planned statuses). After W1 forces Keep plus re-approve mid-corpus, the key file reads Planned until the next re-projection (Generate Corpus entry `NeedsProjection`, or the next checkpoint).
**Scope:** Out of scope (Phase 10 / W4-adjacent). Mention it in the GuideChanged card body only if UI-SPEC wants it, and log it.

## Code Examples

Existing withdrawal call site (template) — `UserControls/StepReviewPlan.Run.cs:110-120`:
```csharp
case ReviewPlanEntry.StaleBanner:
    // D-25: approval is withdrawn while the guide is stale and not kept; the files on disk stay
    if (CorpusApproval.WithdrawApprovalIfStale(Draft))
    {
        Draft.Revision++;
        await SaveAsync();
        if (IsDisposed)
            return;
    }
```

Existing CR-B02 stamp precedent — `Services/Qa/BlindReviewer.cs:81-118` (`WrittenStamps`, `IsCurrent`). Copy the ThreadDone loop only:
```csharp
foreach (var record in (replay?.LastByThread ?? new()).Values)
{
    if (record == null || record.Type != JournalRecordType.ThreadDone) continue;
    foreach (var message in record.Messages ?? new())
        if (message != null && !string.IsNullOrEmpty(message.DocId))
            stamps.TryAdd(message.DocId, record.Utc);
}
```

The entry-decision ordering to implement (pure, in `GenerationUiText.EntryDecision`):
```csharp
if (input.Running) return GenerationEntry.KeepRun;
if (input.HasPlan && input.GuideStale) return GenerationEntry.GuideChangedCard;   // new, before the Approved check
if (!input.HasPlan || !input.Approved) return GenerationEntry.NotApprovedCard;
// ... unchanged
```

## State of the Art (in this repo)

| Old | New (this phase) | Impact |
|-----|------------------|--------|
| Withdraw only on Review Plan entry | Withdraw on guide save, reopen, Generate entry and run, Summary entry | W1 closed |
| `SuppressAutoPlanOnce` (auto-plan unless reopened) | `PlanOnEntryOnce` (auto-plan only right after Corpus Settings Next) | Back navigation is free; D-12 kept |
| PlanNextGate NoPlan blocks | AskSkip confirm + NoPlan-card skip link | W2 closed |
| Discovery stale on guide/core/caption/PlanId | + cited-exhibit stamps | W3 closed |
| Generate Emails button only before generation | Always shown; "Generate Corpus" after | W6 closed |

## Assumptions Log

| # | Claim | Section | Risk if Wrong |
|---|-------|---------|---------------|
| A1 | `WidenActiveRanges` on Case Guide entry never changes the hash after planning (added knowers are active on the date) | Pitfall 6 | Approval silently withdrawn by just visiting Case Guide; the regression test catches it |
| A2 | Keeping D-12 (Corpus Settings Next = "user asks") satisfies SC2 "unless the user asks" | W2 design | If the user meant "never auto-plan", the NoPlan card should always show on entry (one-line change in `ReviewPlanEntryDecision`). RESOLVED: see 09-UI-SPEC U3 |
| A3 | Reverting a guide edit should not restore approval (D-25 "stays withdrawn") | W1 design | Minor annoyance: an extra re-approve |
| A4 | ThreadDone-only stamps are the desired "changed" signal (attachment-only completions do not stale discovery docs) | W3 | Docs could miss an attachment-only change; attachments are not cited as exhibits |
| A5 | "Skip to Discovery Documents" on the Corpus Settings step is acceptable UI | W2 item 3 | Can be dropped; the Review Plan skip alone still meets SC2 after one cancel |
| A6 | Default button Yes for the skip confirm | W2 | Cosmetic |

## Open Questions (RESOLVED)

1. **Should Corpus Settings also get the skip link?** We know the Review Plan skip meets "reachable without an approved plan". What is unclear is whether a forward path with zero spend is required. Recommendation: include it. It is small and is the only zero-spend forward path.
   RESOLVED: see 09-UI-SPEC U1 (included; visible whenever the plan is not approved).
2. **Where does the Case Guide dependency notice go: a banner variant or a separate line?** Recommendation: a banner variant with the lowest priority. UI-SPEC should confirm the 150% layout.
   RESOLVED: see 09-UI-SPEC U2 (Changed = lowest-priority PlanDependency banner; Info and banner-occupied Changed = header dependency line).
3. **Should Generate Corpus offer "Keep plan and approve" directly on the GuideChanged card?** Recommendation: no. Send the user to Review Plan, where the stale banner, validator rule 19 and approval live (single source of truth).
   RESOLVED: see 09-UI-SPEC U4 (no keep-and-approve on Generate Corpus; the card links to Review Plan).

## Environment Availability

| Dependency | Required By | Available | Version | Fallback |
|------------|------------|-----------|---------|----------|
| .NET SDK | build/test | ✓ | 10.0.401 (also 9.0.103) | — |
| Display at 144 DPI | Render tests (`RenderHarness.RequiredDpi = 144`, min 120) | depends on session | — | 120 accepted (Phase 3 override); flag a human check at 150% |

No external services. All work is offline: no provider calls are needed, and tests use `FakeLlmProvider`.

## Validation Architecture

### Test Framework
| Property | Value |
|----------|-------|
| Framework | xUnit 2.9.3 on net10.0-windows |
| Config file | none — `ReelDiscovery.Tests/ReelDiscovery.Tests.csproj` |
| Quick run command | `dotnet test ReelDiscovery.Tests/ReelDiscovery.Tests.csproj --artifacts-path obj/gsd-artifacts --filter "FullyQualifiedName~CorpusApprovalTests\|FullyQualifiedName~ReviewPlanLogicTests\|FullyQualifiedName~GenerationUiTextTests\|FullyQualifiedName~DiscoveryStalenessTests\|FullyQualifiedName~CorpusDetectorTests"` |
| Full suite command | `dotnet test ReelDiscovery.slnx --artifacts-path obj/gsd-artifacts --filter "Category!=Scale&Category!=LiveSpike"` (~11 min, 4,468 tests at audit) |
| Render command | `dotnet test ReelDiscovery.Tests/ReelDiscovery.Tests.csproj --artifacts-path obj/gsd-artifacts --filter "Category=Render&(FullyQualifiedName~GenerateCorpusRenderTests\|FullyQualifiedName~CaseSummaryRenderTests\|FullyQualifiedName~CorpusReviewPlanRenderTests\|FullyQualifiedName~CaseGuideRenderTests\|FullyQualifiedName~DiscoveryDocumentsRenderTests)"` |

### Phase Requirements → Test Map
| Req / SC | Behavior | Type | Where (existing pattern) |
|----------|----------|------|--------------------------|
| GUIDE-09 / SC1 | Guide save withdraws approval when stale; kept hash does not | unit | `Corpus/CorpusApprovalTests.cs` (extend) + `Case/CaseGuideStepLogicTests.cs` |
| SC1 | `EntryDecision` returns GuideChangedCard before NotApproved; card copy | unit | `Evidence/GenerationUiTextTests.cs` |
| SC1 | Entering Generate Corpus with a stale guide persists Approved=false and starts no run; `RunCoreAsync` refuses (fake provider sees 0 calls) | step | `Evidence/GenerateCorpusStepTests.cs` (Harness with `Fake` call counter) |
| SC1 | GuideChanged card action navigates and never resets (journal intact) | step | same |
| SC1 | Reopen withdraws a stale approval | unit | `Case/CaseScenarioUiLogicTests.cs` |
| SC1 | Case Guide dependency notice text (pre/post edit) | unit | `Case/CaseGuideUiLogicTests.cs` |
| SC1 / A1 | After `ApplyNewPlanAsync`, `WidenActiveRanges` is a no-op and the guide is not stale | unit | `Corpus/CorpusApprovalTests.cs` or `Case/ActiveRangeWideningTests.cs` |
| SC2 | Entry with no plan: AutoPlan only when PlanRequested; NoPlanCard otherwise (Back path) | unit | `Corpus/ReviewPlanLogicTests.cs` (update l.69/77) |
| SC2 | `PlanNextGate` no plan returns AskSkip with copy | unit | `Corpus/ReviewPlanLogicTests.cs` (update l.434) |
| SC2 | Flag set by Corpus Settings Next, cleared on Review Plan entry, false after reopen | unit | `CorpusSettingsStepLogicTests`, `CaseScenarioUiLogicTests` |
| SC3 | Stamp records cited DocIds only; Check returns Exhibits on missing or changed stamp; null stamps skip; legacy docs use existence-only | unit | `Discovery/DiscoveryStalenessTests.cs` |
| SC3 | Detector stamps: `{}` when the journal was deleted (Start Over), ThreadDone-only (attachment completion does not move it) | integration | `Discovery/CorpusDetectorTests.cs` with `DiscoveryCorpusFixture` (real journal) |
| SC3 | Start Over (`GenerationReset`) then SavedRows shows OutOfDate; Regenerate Thread (`ThreadInvalidation.InvalidateAsync`) then OutOfDate, still OutOfDate after a new ThreadDone | integration | `Discovery/DiscoveryStepModelTests.cs` + fixtures from `Evidence/GenerationResetTests.cs`, `ThreadInvalidationTests.cs` |
| SC3 | Exhibits status copy | unit | `Discovery/DiscoveryUiTextTests.cs` |
| SC3 | Sidecar round-trip of `ExhibitStamps` + Normalize (null, blank keys) | unit | `Discovery/DiscoveryStoreTests.cs` |
| SC4 | `MailboxesRow` always shows the secondary button; caption switches | unit | `Evidence/GenerationUiTextTests.cs` (update l.1627) |
| SC4 / DPI | Case Summary partial state, Generate Corpus GuideChanged, Review Plan NoPlan with skip, Case Guide notice: no clipping at 144 | render | `Render/*RenderTests.cs` (`ClippingDetector`) |

### Sampling Rate
- **Per task commit:** the quick run filtered to touched classes.
- **Per wave merge:** quick run plus `FullyQualifiedName~WizardFlowTests|FullyQualifiedName~CaseSummary|FullyQualifiedName~Discovery`, plus Render when UI changed.
- **Phase gate:** full suite plus Render green, PNGs inspected at 144 DPI.

### Wave 0 Gaps
None. The test project, fakes (`FakeLlmProvider`), real-journal fixtures (`DiscoveryCorpusFixture`, `GenerateCorpusRenderTests.StepFixture`), the render harness and `ClippingDetector` all exist. Tests are added TDD-style in the task that needs them (Phase 8 precedent).

## Security Domain

| ASVS Category | Applies | Standard Control |
|---------------|---------|-----------------|
| V2 Authentication | no | — |
| V3 Session Management | no | — |
| V4 Access Control | no | — |
| V5 Input Validation | yes | `Normalize()` on untrusted `discovery-documents.json` / `case.json`: new `ExhibitStamps` null→empty, drop blank keys; DocIds are never used to build paths |
| V6 Cryptography | no | SHA hashes are content fingerprints, not security |

| Threat | STRIDE | Mitigation |
|--------|--------|-----------|
| Hand-edited case.json sets Approved=true with a stale guide | Tampering | Gates recompute `IsGuideStale` on entry and run; never trust the stored flag alone |
| Hand-edited sidecar with huge/malformed `ExhibitStamps` | DoS/Tampering | Existing `DiscoveryStore` size cap + Normalize; `Check` tolerant of missing keys; SavedRows already catches |
| Unintended paid call via navigation | Repudiation/cost | One-shot plan intent; RunCoreAsync guard before gateway creation |

## Sources

### Primary (HIGH confidence, read in this session)
- `.planning/v1.0-MILESTONE-AUDIT.md`, `.planning/v1.0-INTEGRATION-CHECK.md` (W1, W2, W3, W6 evidence)
- `.planning/phases/08-ancillary-discovery-documents/08-CONTEXT.md` (D-07, D-08, D-09, D-27), `04-CONTEXT.md` (D-12, l.82)
- `Services/Corpus/CorpusApproval.cs`, `Models/Corpus/CorpusState.cs`, `Models/Case/CaseGuide.cs` (`ComputeHash`)
- `UserControls/StepReviewPlan.Run.cs`, `CorpusUiText.cs` (entry decision, Next gate, NoPlan card)
- `UserControls/StepGenerateCorpus.Run.cs` (entry, `RunCoreAsync`, `ClickStateActionAsync`), `GenerationUiText.cs` (EntryDecision, StateCard, MailboxesRow)
- `UserControls/StepCaseGuide.cs` / `.Run.cs` (SaveAsync funnel, banner), `CaseGuideUiText.cs`
- `UserControls/StepCaseSummary.cs` (rows, Generate Emails, LoadQaWithDiscovery), `StepCaseScenario.cs` (reopen)
- `Services/Discovery/DiscoveryStaleness.cs`, `CorpusDetector.cs`, `DiscoveryGenerator.cs`, `Models/Discovery/*`, `UserControls/DiscoveryStepModel.cs`, `DiscoveryUiText.cs`
- `Services/Evidence/ThreadInvalidation.cs`, `GenerationJournal.cs` (replay semantics), `Services/Qa/BlindReviewer.cs` (CR-B02)
- `Forms/WizardForm.cs` (StepTypes, NavigateToStage/Step, close flush)
- `ReelDiscovery.Tests/*` (patterns, tests that must be updated)

## Metadata

**Confidence breakdown:**
- Standard stack: HIGH (no changes)
- Architecture: HIGH for mechanisms (code read); MEDIUM for the W2/W6/notice UX choices (recommendations pending lock)
- Pitfalls: HIGH (each tied to specific lines)

**Research date:** 2026-10-11
**Valid until:** until the next change to the corpus/discovery steps (internal code; no external drift)
