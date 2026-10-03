---
phase: 05-relevant-evidence-generation-narrative-ledger
plan: 08
subsystem: evidence-rendering
tags: [render-prompt, knowledge-gate, strict-schema, repair, ledger, story-tier]
requires:
  - 05-01 (RenderedThread, LedgerDelta, LedgerKind, RenderViolation, ThreadAssignment, MessageTime, EvidenceTestData)
  - 05-03 (RenderAssignments.Compute / Spec)
  - 05-04 (KnowledgeGate.Build, LedgerSelector.Select)
  - 05-05 (PeopleDirectory, PersonInfo, HtmlEmailFormatter.PrivilegedBannerText)
provides:
  - Services/Evidence/ThreadRenderContext (Create)
  - Services/Evidence/EvidencePrompts (SystemPrompt, HotSuffix, BuildThreadPrompt, SubtletyGuidance)
  - Services/Evidence/ThreadRenderer (MaxOutputTokens, SchemaTypes, BuildRequest, ThreadRenderDto/RenderedMessageDto/LedgerDeltaDto)
  - Services/Evidence/RenderRepair (Apply, CleanBody, MaxBodyChars)
  - EvidenceTestData.RenderResponder (fake provider responder keyed by "Thread id: T-xxxx")
affects: [generation orchestrator (render loop, retries), validators V1-V8, ledger merge, integration tests]
tech-stack:
  added: []
  patterns: [byte-stable system prompt, trailing retry feedback, nested strict-schema DTOs, GeneratedRegex repair pipeline]
key-files:
  created:
    - Services/Evidence/ThreadRenderContext.cs
    - Services/Evidence/EvidencePrompts.cs
    - Services/Evidence/ThreadRenderer.cs
    - Services/Evidence/RenderRepair.cs
    - ReelDiscovery.Tests/Evidence/EvidencePromptsTests.cs
    - ReelDiscovery.Tests/Evidence/RenderRepairTests.cs
  modified:
    - ReelDiscovery.Tests/Evidence/EvidenceTestData.cs
    - ReelDiscovery.Tests/Case/CaseSchemaTests.cs
decisions:
  - "[Phase 05] Render prompt 'known to' lists only knowers who are on the thread; public facts/values say 'everyone'; a plan fact none of the participants knows lists all its knowers"
  - "[Phase 05] Message lines omit empty intent / must convey / attachments segments; an empty may-mention list reads 'nothing beyond the quoted messages'; a missing assignment defaults to Clear (Own) or None (other) and Medium length"
  - "[Phase 05] RenderRepair also drops a trailing body line equal to the sign-off (the composer writes the sign-off after the body) and tidies a space left before , . ; : by id-token removal"
  - "[Phase 05] Repaired ledger deltas keep the model seq (LedgerMerger already drops unknown seqs); entries with empty About and Statement are skipped; text capped at 400 chars"
metrics:
  duration: 15min
  completed: 2026-10-03
  tasks: 3
  files: 8
---

# Phase 5 Plan 08: Thread Render Request and Repair Summary

The request side of evidence rendering: a per-thread `ThreadRenderContext` (knowledge gate + ledger slice), a knowledge-gated, voice-aware user prompt under a byte-stable system prompt, a Story-tier strict-schema request returning messages and the ledger delta in one call, and `RenderRepair`, which normalizes untrusted model output into a `RenderedThread`.

## Tasks

| Task | Name | Commits |
|------|------|---------|
| 1 | ThreadRenderContext and EvidencePrompts | 803d2d4 (test), 56e44a2 (feat) |
| 2 | ThreadRenderer DTOs, request builder, schema test, fake responder | bc67c20 (test), 201caa0 (feat) |
| 3 | RenderRepair | b169baf (test), a8dabeb (feat) |

## What Was Built

- **ThreadRenderContext.Create** builds `Knowledge = KnowledgeGate.Build(guide, thread)` and `LedgerView = LedgerSelector.Select(ledger, thread, waveIndex, Knowledge.Participants)`.
- **EvidencePrompts.SystemPrompt** is a const that keeps names exactly as written (including names from films, TV, books and games), never says names must be fictional, limits legal terms to counsel exchanges, forbids signatures, banners and re-quoting, and allows only modest markup.
- **BuildThreadPrompt** builds sections a-j in a fixed order: name rules (`includeScenario: false`), participant organizations, people with voice and sign-off style, gated facts as of the last date, knowable values, selected ledger entries, the thread header (with the counsel instruction when privileged), one `msg {seq} | ...` line per planned message (subtlety guidance, length band, may-mention, intent, must convey, attachment titles, and the hot suffix on every hot message whatever its subtlety), the task text, and the retry feedback block, which always comes last.
- **ThreadRenderer**: `MaxOutputTokens = 12_000`, `SchemaTypes`, `BuildRequest` (Story tier, `Generate Corpus - T-xxxx[ attempt N]`), and nested strict DTOs. The DTOs are registered in `CaseSchemaTests` and pass `StrictSchemaValidator`.
- **RenderRepair.Apply / CleanBody**:
  - Maps messages by planned seq; the first entry for a seq wins, and unknown or duplicate seqs are dropped with a note.
  - Cuts the body at quote and forward headers, drops `>` lines, the leading privilege banner and trailing name, signature or mobile-footer lines.
  - Strips id tokens, HTML tags, URLs, `_x_` and `*x*` emphasis using word-boundary guards. Identifiers and file names such as `Pay_App_7.xlsx` are left alone, and `**bold**` is kept.
  - Collapses whitespace. Caps the body at 6,000 characters on a word boundary, the sign-off at its first line and 40 characters (empty if it is the sender's name), peopleNamed at 30 entries (distinct, case-insensitive) and newDetails at 20.
- **EvidenceTestData.RenderResponder** answers fake provider requests by the `Thread id:` line. It returns in-band neutral bodies, `Thanks,` and one `site walk {n}` Commitment detail, takes an optional `(threadId, attempt)` JSON override, keeps a per-thread attempt counter and fails transiently for an unknown thread.

## Verification

- `dotnet test --filter "FullyQualifiedName~EvidencePromptsTests"`: 13 passed
- `dotnet test --filter "FullyQualifiedName~RenderRepairTests"`: 14 passed
- Full suite: 2432 passed, 0 failed. The app and test builds have no warnings.
- All acceptance greps match (SystemPrompt const, keep-names sentence, no "fictional name", no synopsis/values/criteria calls, `includeScenario: false`, `MaxOutputTokens = 12_000`, `LlmTier.Story`, `ThreadRenderer.SchemaTypes`, 12 `[Description(`, no nullable DTO members, 10 `GeneratedRegex`, no `new Regex(`, `MaxBodyChars = 6_000`).

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 2 - Missing functionality] Trailing sign-off line removed from the body**
- **Found during:** Task 3
- **Issue:** The composer appends the sign-off after the body. A model body that already ends with that sign-off (for example "Thanks,") would show it twice.
- **Fix:** `Apply` drops a final body line equal to the cleaned sign-off and records the note "removed repeated sign-off". This is covered by `Apply_RemovesTrailingSignOffLineFromBody`.
- **Files modified:** Services/Evidence/RenderRepair.cs
- **Commit:** a8dabeb

**2. [Rule 1 - Bug] Leftover space before punctuation after id-token removal**
- **Found during:** Task 3
- **Issue:** Removing "F-003" from "See F-003, and" left "See , and".
- **Fix:** `CleanBody` removes spaces before `, . ; :` when followed by whitespace or end of text.
- **Files modified:** Services/Evidence/RenderRepair.cs
- **Commit:** a8dabeb

The BuildRequest and RenderResponder tests live in `EvidencePromptsTests.cs`, because the plan lists no separate ThreadRenderer test file.

## TDD Gate Compliance

Every task has a `test(05-08)` RED commit followed by a `feat(05-08)` GREEN commit. No refactor commits were needed.

## Known Stubs

None. `RenderResponder` filler bodies are intentional test fixtures.

## Threat Flags

None. All new surface is covered by T-05-20 to T-05-23 (repair caps, gated prompt content, MaxOutputTokens 12,000).

## Self-Check: PASSED
