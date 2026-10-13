---
phase: 01-foundation-provider-gateway
verified: 2026-09-30T18:30:00Z
status: passed
score: 5/5 roadmap success criteria verified (plan must-haves previously failed are now verified; live UI/API checks pending human)
overrides_applied: 0
re_verification:
  previous_status: gaps_found
  previous_score: 4/5
  gaps_closed:
    - "Refusals, truncations and spend-limit errors surface as classified outcomes (SC3 / PROV-04); provider failures never end a run silently (CR-01)"
    - "Tested keys are saved to the DPAPI store; Clear removes a key from the store (CR-02)"
  gaps_remaining: []
  regressions: []
deferred:
  - truth: "PLAT-03: tests for planner, validators and answer-key writer"
    addressed_in: "Phase 4"
    evidence: "ROADMAP Phase 4 (Corpus Plan & Answer Key) Requirements list includes PLAT-03; REQUIREMENTS.md traceability: 'Partial (Phase 1: harness + EML tests; planner/validator/answer-key tests in Phase 4)'"
human_verification:
  - test: "Launch the published single-file ReelDiscovery.exe on a clean Windows x64 machine and walk the full wizard with OpenAI selected"
    expected: "App starts (no .NET install needed), disclaimer shown, no telemetry dialog, a small run completes and writes .eml files"
    why_human: "WinForms UI launch and end-to-end run need a desktop session and a real API key"
  - test: "Enter a real Anthropic key and a real OpenAI key, press Test on each, restart the app"
    expected: "Both show green OK; keys reload from keys.dat after restart; keys.dat does not contain plaintext keys"
    why_human: "Requires real provider keys and network"
  - test: "While a key test is in flight, confirm the key box and Clear are disabled; after it finishes, press Clear and restart"
    expected: "Controls re-enable after the test; the cleared key does not reappear after restart (CR-02 fix in the live UI)"
    why_human: "UI control state and timing need a desktop session"
  - test: "Select Claude + Story/Volume models, generate storylines, characters and a small email run"
    expected: "Valid typed results from Claude; token/cost display updates"
    why_human: "Live Anthropic structured-output call"
  - test: "With a legacy %LOCALAPPDATA%\\ReelDiscovery\\reeldiscovery.settings file present, launch the app"
    expected: "Key appears as the OpenAI key; legacy file deleted"
    why_human: "Unit-tested, but real-profile startup migration should be confirmed once"
  - test: "Select Claude, clear/omit the OpenAI key, open Topic and Generation Config steps"
    expected: "Image and voicemail options unchecked, disabled, with orange notice; with a tested OpenAI key, a gpt-image image is generated in a run"
    why_human: "Visual gating and live gpt-image call"
---

# Phase 1: Foundation & Provider Gateway Verification Report

**Phase Goal:** The app runs on a supported runtime with tests and safe concurrency, and all text generation goes through one provider-agnostic gateway, so the user can choose OpenAI or Claude.
**Verified:** 2026-09-30T18:30:00Z
**Status:** passed (human verification items reported complete by the user, 2026-10-13)
**Re-verification:** Yes, after gap closure (plans 01-12, 01-13)

## Re-verification Summary

Both earlier gaps are closed. I confirmed this in the code, not only from the SUMMARY files.

| Gap | Previous | Now | Evidence |
|-----|----------|-----|----------|
| CR-01: timeout treated as a cancel, false "GENERATION COMPLETE" | FAILED | VERIFIED | `OpenAITextProvider.cs:54-62, 83-91` and `AnthropicTextProvider.cs:62-70, 94-102`: `catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }`, then an unfiltered OCE returns `Failed(LlmFailureKind.Network, "... timed out")`. `OpenAIMediaClient.cs:57, 107`: `when (!ct.IsCancellationRequested)` returns null, so the item is skipped. `LlmGateway` default branch returns Failed with no app retry. `EmailGenerator.cs:184-194` (storyline) and `494-503` (`RunIsolatedAsync`) rethrow only when `runCts.IsCancellationRequested`; otherwise they call `AddError("... skipped (request timed out)")`. The outer catch (`441-451`) calls `RecordUnexpectedStop`, which records an error whenever the stop is neither a user cancel nor fatal. The results then land in StepGeneration's "completed with errors" branch rather than the clean "GENERATION COMPLETE!" branch, because `Errors.Count > 0`. New tests: `TransportTimeoutTests` (9), `EmailGeneratorTimeoutTests` (8), `RunSummaryTests` (5). |
| CR-02: key test undoing Clear | PARTIAL | VERIFIED | `StepApiConfiguration.cs:326-381`: the key box and Clear are disabled before the await. After the await, `KeyTestGuard.IsResultCurrent(key, keyBox.Text)` discards a stale result before `SetKeyState`/`SetKey`. The finally block re-enables all three controls. `KeyTestGuardTests` (6). |

No regressions found. The build is still warning-free, the test count went from 167 to 211 with every test passing, and publish still produces one exe.

## Goal Achievement

### Observable Truths (Roadmap Success Criteria)

| # | Truth | Status | Evidence |
|---|-------|--------|----------|
| 1 | Builds on net10.0-windows with pinned SDK; single-file self-contained publish launches and runs the existing flow | VERIFIED (launch: human) | `dotnet --version` = 10.0.401 (global.json pin). `dotnet build ReelDiscovery.slnx`: 0 warnings, 0 errors. `dotnet publish ReelDiscovery.csproj -c Release -o <scratch>/pub` gave `ReelDiscovery.exe` (64,300,084 bytes) and a pdb only. |
| 2 | Enter/test OpenAI and Anthropic keys; DPAPI-encrypted; legacy Base64 file migrated | VERIFIED (live keys: human) | Unchanged from the initial verification (ApiKeyStore DPAPI, `Program.cs` migration, Test buttons for both providers). The CR-02 Clear race is now closed. |
| 3 | Pick Claude or OpenAI plus a model; structured JSON-schema request returns a typed result from either; refusals/truncations/spend-limit classified and not retried blindly | VERIFIED (live Claude call: human) | Gateway classification is unchanged and tested. A transport timeout is now classified as `Failed/Network` at the provider layer and recorded as a skip at the generator layer, with no silent success. Media uses the same `CreateDefaultOptions()` (`QuotaAwareRetryPolicy`), so insufficient_quota is not blindly retried there either. |
| 4 | gpt-image model; Claude with no OpenAI key disables images/voicemail with a notice | VERIFIED (visual/live: human) | Unchanged from the initial verification. |
| 5 | `dotnet test` passes; shared-Random and parallel-mutation bugs fixed | VERIFIED | `dotnet test ReelDiscovery.slnx`: Passed 211, Failed 0. Random.Shared and the locked result/progress mutations are unchanged. |

**Score:** 5/5 roadmap success criteria verified

### Plan must-haves previously failing

| Plan | Truth | Status |
|------|-------|--------|
| 01-07 | Clear removes a key from the store | VERIFIED (CR-02 closed) |
| 01-11 | Failures reported clearly; nothing ends silently | VERIFIED (CR-01 closed) |
| 01-12/13 | Timeout-safe providers, generator and media; stale key-test results discarded; true run outcome reported; safe distribution | VERIFIED (code and tests above) |

### Key Link Verification (changed item)

| From | To | Via | Status |
|------|----|-----|--------|
| Provider transport timeout | LlmResult classification | unfiltered OCE catch returns `Failed(Network)` | WIRED (was NOT WIRED) |
| Generator non-run OCE | GenerationResult.Errors | storyline/RunIsolated catch and `RecordUnexpectedStop` | WIRED |
| Key test completion | ApiKeyStore.SetKey | gated by `KeyTestGuard.IsResultCurrent` | WIRED |

### Behavioral Spot-Checks

| Behavior | Command | Result | Status |
|----------|---------|--------|--------|
| Build | `dotnet build ReelDiscovery.slnx` | 0 warnings, 0 errors | PASS |
| Tests | `dotnet test ReelDiscovery.slnx` | 211 passed, 0 failed | PASS |
| Single-file publish | `dotnet publish ReelDiscovery.csproj -c Release -o <scratch>` | exe (64.3 MB) and pdb | PASS |
| SDK pin | `dotnet --version` | 10.0.401 | PASS |

### Requirements Coverage

| Requirement | Status | Note |
|-------------|--------|------|
| PLAT-01 | SATISFIED | |
| PLAT-02 | SATISFIED | |
| PLAT-03 | PARTIAL, deferred to Phase 4 by design | Harness, EML, gateway/provider and generator tests are present |
| PLAT-04 | SATISFIED | |
| PROV-01, PROV-02, PROV-03 | SATISFIED | |
| PROV-04 | SATISFIED | Was BLOCKED. Timeout classification is now in place. |
| PROV-05, PROV-06 | SATISFIED | |

No orphaned requirements.

### Open Review Warnings: Gap or Debt?

I checked each open warning in 01-REVIEW.md against the success criteria. None of them defeats one, so all are recorded as quality debt and none as a gap.

| Item | Decision | Reasoning |
|------|----------|-----------|
| WR-10: completion page says "usable for import" after a save failure or an abort with no output | Debt | The failure is still surfaced. The save error is added to `Errors`, and StepGeneration shows "completed with errors" and lists each error. That meets SC3's intent that failures are not silent success. The problem is misleading summary wording, not a missing classification. Should be fixed before or with Phase 4/5 run reporting. |
| WR-11: `DistributeEmails` int overflow on absurd user-edited "Est. Emails" values | Debt | Needs values in the millions typed by hand. This is input validation, not a PLAT-04 concurrency bug. |
| WR-12: key-test lock can last 10+ minutes on a stalled connection | Debt (UX) | Users can still enter and test keys (SC2). A 30-second key-test deadline is recommended. |
| WR-01: failing batch discards earlier batches of the storyline | Debt (cost) | The failure is recorded. Paid-for work is lost, but no outcome is hidden. Revisit in the Phase 5 volume engine. |
| WR-02: key test checks only one model | Debt | A missing Volume model fails per item with a recorded error. |
| WR-03: media 401/403/404 not classified as fatal | Debt | SC3 covers structured text requests. Media failures are recorded per item, and quota errors are not blindly retried because of the shared retry policy. |
| WR-04, WR-07, WR-08, WR-09, IN-01..IN-10 | Debt | UX, logging and validation issues. None affects a success criterion. IN-10 (no integration test through a real provider) is covered in part by the provider-level `TransportTimeoutTests` plus the generator-level tests. |

### Anti-Patterns

The earlier blockers (unfiltered OCE rethrows) are gone. The open WR/IN items above are the remaining warnings.

### Human Verification Required

1. **Published exe launch and OpenAI end-to-end run.** Expected: the app launches without a .NET install and writes .eml files.
2. **Real key tests for both providers, then a restart.** Expected: green OK, and the keys reload from `keys.dat`, which holds no plaintext.
3. **Key-test lock and Clear in the live UI.** Expected: controls are disabled during a test, and a cleared key stays cleared after a restart.
4. **Claude-selected run.** Expected: valid typed storylines, characters and emails.
5. **Legacy `reeldiscovery.settings` migration on a real profile.** Expected: the key is imported and the file is deleted.
6. **Media gating visuals and a live gpt-image image.**

### Gaps Summary

No gaps remain. CR-01 and CR-02 are fixed in code and covered by new tests, and build, test and publish all pass. What is left is quality debt (WR-01..04, WR-07..12, IN items) that does not block any success criterion, plus live UI and API checks that need a human. PLAT-03 stays deferred to Phase 4 by design.

---

_Verified: 2026-09-30T18:30:00Z_
_Verifier: Claude (gsd-verifier)_


---

**Human verification closed 2026-10-13:** the user reported completing live testing of the items above; see the phase HUMAN-UAT file.
