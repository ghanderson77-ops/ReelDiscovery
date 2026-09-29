# Phase 1: Foundation & Provider Gateway - Discussion Log

> **Audit trail only.** Do not use as input to planning, research, or execution agents.
> Decisions are captured in CONTEXT.md — this log preserves the alternatives considered.

**Date:** 2026-09-29
**Phase:** 01-foundation-provider-gateway
**Areas discussed:** Legacy flow on the gateway, Keys & provider screen, Model lineup & tiers

**User note at area selection:** "I think we should be able to run the story builder at a higher AI model tier." (This was folded into Model lineup & tiers.)

---

## Legacy flow on the gateway

| Option | Description | Selected |
|--------|-------------|----------|
| Port it fully | All 4 generators use the gateway with strict schemas and work with Claude or OpenAI | ✓ |
| Adapter only | Shim OpenAIService text calls through the gateway and keep the old prompts | |
| Leave it OpenAI-only | Untouched until Phase 7 (violates PROV-03) | |

| Option | Description | Selected |
|--------|-------------|----------|
| Only in-scope fixes | Port plus PLAT-04 concurrency fixes only | |
| Also fix cheap known bugs | Also fix replyToIndex and attachment failure isolation | ✓ |
| You decide | Fix what's trivial | |

| Option | Description | Selected |
|--------|-------------|----------|
| Small live run per provider | Manual ~10-email run on each provider | |
| Unit tests only | Translator and schema tests | ✓ |

---

## Keys & provider screen

| Option | Description | Selected |
|--------|-------------|----------|
| Two key sections + provider picker | Separate key boxes with Test buttons, provider picker, media status line | ✓ |
| Provider-first | Pick a provider, then only that provider's key field shows | |
| Single key, auto-detect | Detect the provider from the key prefix | |

| Option | Description | Selected |
|--------|-------------|----------|
| Claude | New default | |
| OpenAI | Matches today's behavior; media out of the box | ✓ |
| Whichever key is present | Remember the last used provider | |

| Option | Description | Selected |
|--------|-------------|----------|
| Only the selected text provider | Media key optional | ✓ |
| Every entered key | All entered keys must pass | |

| Option | Description | Selected |
|--------|-------------|----------|
| Provider + model choice | Remember between sessions | |
| Keys only | Provider and model reset each launch | ✓ |

| Option | Description | Selected |
|--------|-------------|----------|
| Yes, 'Clear' per key | Delete from the encrypted store | ✓ |
| No, overwrite only | | |

---

## Model lineup & tiers

| Option | Description | Selected |
|--------|-------------|----------|
| Two tiers: Story + Volume | Strong model for story work, cheap model for bulk | ✓ |
| Three tiers | Story / Evidence / Volume | |
| Per-task model table | An advanced setting per operation | |

| Option | Description | Selected |
|--------|-------------|----------|
| One text provider per run | Both tiers use the selected provider | ✓ |
| Pick provider per tier | Tiers can mix providers | |

| Option | Description | Selected |
|--------|-------------|----------|
| Story: Opus 5.5 / Volume: Haiku 4.5 | | ✓ |
| Story: Sonnet / Volume: Haiku 4.5 | | |
| Story: Opus 5.5 / Volume: Sonnet | | |

| Option | Description | Selected |
|--------|-------------|----------|
| Researcher verifies current IDs | Current flagship / cheapest tier | ✓ |
| Keep gpt-4o / gpt-4o-mini | | |

| Option | Description | Selected |
|--------|-------------|----------|
| Two dropdowns | Story model + Volume model; Provider column in the catalog dialog | ✓ |
| One dropdown + Advanced | | |

| Option | Description | Selected |
|--------|-------------|----------|
| Story for storylines/characters, Volume for emails/themes | | ✓ |
| Everything on Story | | |
| You decide | | |

---

## Claude's Discretion

- UX and retry policy for refusal, truncation and spend-limit outcomes
- Default gpt-image tier, size and quality
- Test framework and project layout; an optional env-gated live smoke test
- The exact shape of the gateway interface
- Package upgrade sequencing and Office builder regression checks

## Deferred Ideas

- Remembering provider and model selection between sessions
- Mixing providers across tiers
- A per-task model table or a third tier
- A mandatory live smoke run of the legacy wizard
