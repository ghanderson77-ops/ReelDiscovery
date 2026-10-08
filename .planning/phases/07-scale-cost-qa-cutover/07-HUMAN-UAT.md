---
status: deferred
phase: 07-scale-cost-qa-cutover
source: [07-VERIFICATION.md]
started: 2026-10-08T18:00:00
updated: 2026-10-08T18:00:00
---

## Current Test

number: 1
name: Anthropic evidence-prefix caching (live spike part c)
expected: |
  With an Anthropic key saved and tested on the API Configuration step, running
  REELDISCOVERY_LIVE_SPIKE=1 dotnet test ReelDiscovery.Tests/ReelDiscovery.Tests.csproj --filter "FullyQualifiedName~LiveSpike.LiveProviderSpikeTests.Anthropic"
  reports cache_creation / cache_read tokens on the second Sonnet evidence call; StablePrefixTokens (600, [ASSUMED]) is recalibrated if the measured prefix differs by >20% or is below the cache minimum.
awaiting: deferred by user 2026-10-08 ("not worry about Anthropic right now")

## Tests

### 1. Anthropic evidence-prefix caching
expected: second evidence call shows cache reads; StablePrefixTokens verified or recalibrated
result: [pending]

### 2. Anthropic batch round trip with app restart
expected: a small case with batch pricing on the Anthropic provider submits only the non-relevant mail batch; closing and reopening during the wait and clicking Continue collects it without a second submission
result: [pending]

## Gaps
