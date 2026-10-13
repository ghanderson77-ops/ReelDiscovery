---
phase: 06-attachments-non-relevant-corpus
plan: 12
subsystem: evidence-side-world-and-voices
tags: [filler, side-world, voicemail, tts, strict-schema, fallback, journal]
requires:
  - "06-01 SideWorld/SideWorldOrganization/SideProject, CastVoiceSet/CastVoice, JournalRecord.SideWorld/CastVoices, JournalReplay.LastSideWorld/LastCastVoices"
  - "06-05 VoiceAssignments.Voices/Fallback/For, RenderAssignments.StableRank"
provides:
  - "SideWorldBuilder (internal static): SystemPrompt, SchemaTypes (SideWorldDto), CoveredOrganizations, CaseTerms, Collides, BuildRequest, BuildAsync -> SideWorldOutcome, Fallback, TemplatesFor, ProjectsFor, FromRecord"
  - "CastVoiceBuilder (internal static): SystemPrompt, SchemaTypes (CastVoiceDto), OrderedCast, BuildRequest, BuildAsync -> CastVoiceOutcome, Fallback, FromRecord"
affects: [06-14 filler prompts (ProjectsFor), 06-16 filler responder, run start in GenerationRunner (journal SideWorld + CastVoices records), voicemail TTS]
tech-stack:
  added: []
  patterns:
    - "One Volume-tier strict-schema call through PlanCallRunner; fatal stop -> outcome value null; refusal/failure -> deterministic code fallback with FromFallback = true"
    - "Untrusted side-project names and topics checked whole-word both ways (RenderValidator.ContainsWord) against every case term; the prompt term list alone is capped at 60"
    - "Deterministic templates chosen by StableRank(orgId + ':side' + i) and validated against the same terms"
    - "FromRecord reuse requires full coverage (first 24 orgs by ordinal id; every cast id) and runs Normalize on replay"
key-files:
  created:
    - Services/Evidence/SideWorldBuilder.cs
    - Services/Evidence/CastVoiceBuilder.cs
    - ReelDiscovery.Tests/Evidence/SideWorldTests.cs
    - ReelDiscovery.Tests/Evidence/CastVoiceTests.cs
  modified: []
decisions:
  - "Side world: case terms = fact titles, issue titles, every canonical Value (all kinds), cast full names; ProjectName values listed first in the prompt term list (most specific first), then other values, people, fact titles, issue titles"
  - "Side world: a model project survives only with a non-colliding name and at least 1 non-colliding topic; an organization with 0 surviving projects gets 3 templates, 1 surviving project is topped up to 2; zero usable model projects overall -> full Fallback (FromFallback = true)"
  - "Side world: a template is skipped if its name or any topic collides; template names use the organization ShortName (else the first word of Name)"
  - "Side world FromRecord requires every covered organization (first 24 by ordinal id) to be present with at least 1 project"
  - "Cast voices: cast ordered by ordinal id (prompt and result), capped at CastVoiceSet.MaxVoices; first valid entry per person wins; voices lower-cased; StableRank fallback per skipped/invalid person keeps FromFallback false"
metrics:
  duration: "~20 min"
  completed: 2026-10-04
  tasks: 2
  files: 4
---

# Phase 6 Plan 12: Side World and Cast Voices Summary

Per-organization recurring side projects (D-21) from one Volume-tier call, validated whole-word against every case term with deterministic StableRank templates as fallback/top-up, plus a per-cast tts-1 voice set (D-28) from one Volume-tier call with VoiceAssignments.Fallback for anyone the call skipped. Both are journal-reusable via FromRecord.

## What Was Built

**SideWorldBuilder** (`Services/Evidence/SideWorldBuilder.cs`) — D-21, NOISE-01, NOISE-02. Side projects must not collide with case facts or terms.
- `BuildRequest`: Volume tier, "Generate Corpus - side projects", name rules without scenario, one line per covered organization (`{Id} {Name} ({Domain}) - {Kind}, {Location}`), the 3-4 projects / 2-4 topics instruction and a "Do not use these names or terms:" list capped at 60.
- `CaseTerms` (uncapped, validation) and `Collides` (term in text or text in term, whole word, case-insensitive).
- `BuildAsync` via `PlanCallRunner.RunAsync<SideWorldDto>`: fatal -> `World` null; null value -> `Fallback`; else validates, drops unknown org ids, tops up, Normalize.
- 8 fixed generic templates ("{Short} Q3 Supplier Review", "Facilities Refresh", "Key Account Renewal", "Office Move Planning", "{Short} Annual Budget Cycle", "Staff Training Program", "IT Systems Upgrade", "{Short} Benefits Enrollment"); `TemplatesFor` takes the first 3 non-colliding in StableRank order.
- `ProjectsFor(world, org, guide)` returns persisted projects or validated templates (orgs past the 24 cap).

**CastVoiceBuilder** (`Services/Evidence/CastVoiceBuilder.cs`) — D-28; user decision 2026-10-04: per-cast voice call persisted in the journal; StableRank fallback only if the call fails.
- `BuildRequest`: Volume tier, "Generate Corpus - voicemail voices", name rules, the six voices with descriptions, one `{Id} | {FullName} | {Title} | {organization} | {Involvement first 25 words}` line per cast member.
- `BuildAsync` via `PlanCallRunner.RunAsync<CastVoiceDto>`: fatal -> `Voices` null; refusal/failure -> `Fallback` (FromFallback true); else keeps known ids with one of the six voices, fills the rest with `VoiceAssignments.Fallback(id)`.
- `FromRecord` returns the normalized journaled set only when it covers every cast id.
- No Random/Guid/clock use.

## Tasks

| Task | Name | Commit | Files |
| ---- | ---- | ------ | ----- |
| 1 | SideWorldBuilder with validation and fallback | f16b2cb | Services/Evidence/SideWorldBuilder.cs, ReelDiscovery.Tests/Evidence/SideWorldTests.cs |
| 2 | CastVoiceBuilder per-cast voice call with fallback | 8d7564e | Services/Evidence/CastVoiceBuilder.cs, ReelDiscovery.Tests/Evidence/CastVoiceTests.cs |

## Verification

- `--filter SideWorldTests`: 13/13 passed; `--filter CastVoiceTests`: 10/10 passed.
- Full suite: 3170/3171 (baseline 3148 + 23 new). The single failure, `GenerationResetTests.StartOver_LockedFile_IsReported_RestStillDeleted`, passed on an isolated rerun (12/12). It is unrelated and flaky under load, and is logged in deferred-items.md.
- `dotnet build ReelDiscovery.csproj`: 0 warnings, 0 errors.
- All acceptance greps match. "fictional" appears 0 times in either builder file.

## Deviations from Plan

- **SideWorldBuilder is `internal static class`, not `partial`.** No `[GeneratedRegex]` was needed, so there is nothing for `partial` to serve. No acceptance criterion depends on it.
- **"No fictional wording" test.** `CaseGuidePrompts.AppendNameRules` is the shared helper and is required here. It writes "Use these fictional names exactly" when the draft has applied substitutions. The no-"fictional" assertions therefore run on a draft with substitutions cleared, which proves the builder-owned prompt text never says it. The shared helper was left unchanged (out of scope).
- **TDD gate.** Each task was committed as one `feat` commit (tests + implementation) rather than separate RED/GREEN commits. The tests reference the new internal types, so a RED-only commit would not compile the test project.
- **Ordering.** The cast voice set uses ordinal cast id order, the same order as the prompt (the plan's action text). It is identical to guide order in the tests.

## TDD Gate Compliance

Warning: no separate `test(...)` RED commits; tests and implementation landed together in each task's `feat` commit (see Deviations).

## Known Stubs

None.

## Self-Check: PASSED

- FOUND: Services/Evidence/SideWorldBuilder.cs, Services/Evidence/CastVoiceBuilder.cs, ReelDiscovery.Tests/Evidence/SideWorldTests.cs, ReelDiscovery.Tests/Evidence/CastVoiceTests.cs
- FOUND commits: f16b2cb, 8d7564e
