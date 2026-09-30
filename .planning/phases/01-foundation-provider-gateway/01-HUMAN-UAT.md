---
status: partial
phase: 01-foundation-provider-gateway
source: [01-VERIFICATION.md]
started: 2026-09-30T15:31:53Z
updated: 2026-09-30T16:30:00Z
---

## Current Test

[testing paused - 4 tests pending]

## Tests

### 1. Published exe runs end-to-end with OpenAI
expected: Launch the published single-file ReelDiscovery.exe on a clean machine, complete a small OpenAI run; .eml files are written to the output folder.
result: pass

### 2. Real keys persist via DPAPI
expected: Test a real Anthropic key and a real OpenAI key, restart the app; both reload from %LOCALAPPDATA%\ReelDiscovery\keys.dat and the file contains no plaintext key.
result: pass

### 3. Key-test lock and Clear
expected: During a key test the key textbox and Clear are disabled; a cleared key stays cleared after restart.
result: [pending]

### 4. Claude run produces valid output
expected: With Claude selected, storylines, characters and emails generate and parse as valid typed results.
result: [pending]

### 5. Legacy key migration
expected: On a profile with an old reeldiscovery.settings Base64 key, first launch migrates the key into keys.dat and deletes the legacy file.
result: [pending]

### 6. Media gating and live gpt-image
expected: With Claude selected and no tested OpenAI key, image/voicemail options are disabled with the orange notice; with a tested OpenAI key, one live gpt-image image generates.
result: [pending]

## Summary

total: 6
passed: 2
issues: 0
pending: 4
skipped: 0
blocked: 0

## Gaps
