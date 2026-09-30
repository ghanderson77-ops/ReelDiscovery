---
phase: 01
slug: foundation-provider-gateway
status: verified
threats_open: 0
asvs_level: 1
created: 2026-09-30
---

# Phase 01 — Security

> Per-phase security contract: threat register, accepted risks, and audit trail.

---

## Trust Boundaries

| Boundary | Description | Data Crossing |
|----------|-------------|---------------|
| App → OpenAI / Anthropic APIs | HTTPS calls via official SDKs (default base URLs only) from `Services/Ai/` | API keys (secret), prompts (case content), model IDs |
| Provider → App | Model responses and HTTP errors are untrusted input | JSON output (strict-schema), error bodies, status codes |
| App ↔ local disk | Keys at rest in `%LOCALAPPDATA%\ReelDiscovery\keys.dat`; legacy Base64 file migrated | DPAPI-protected API keys (secret) |
| User → App | Wizard inputs incl. editable storyline grid and key text boxes | Keys (secret), counts/dates (untrusted values) |
| App → local UI / run log | Status labels, run log, StepComplete summary | Classified outcome messages (must never contain keys) |

---

## Threat Register

| Threat ID | Category | Component | Disposition | Mitigation | Status |
|-----------|----------|-----------|-------------|------------|--------|
| T-01-01 | Tampering | csproj package refs | mitigate | Exact pins (ReelDiscovery.csproj:27-31) | closed |
| T-01-02 | Information disclosure | Test code in shipped exe | mitigate | `Remove="ReelDiscovery.Tests/**"` (csproj:57-59) | closed |
| T-01-03 | Denial of service | EOL runtime | mitigate | net10.0-windows + global.json 10.0.401; PlatformTests | closed |
| T-01-04 | Denial of service | Calendar hour/minute | mitigate | BuildMeetingStart clamps; test | closed |
| T-01-05 | Denial of service | Batch loop | mitigate | `Take(batchSize)` + break on empty batch | closed |
| T-01-06 | Tampering | replyToIndex | mitigate | ResolveReferencedEmail bounds-check; tests | closed |
| T-01-07 | Denial of service | Task.WhenAll faulting | mitigate | RunIsolatedAsync per item; locked AddError; ConcurrencyTests | closed |
| T-01-08 | Tampering | Torn progress state | mitigate | Locks, Clone() snapshots, Random.Shared; ConcurrencyTests | closed |
| T-01-09 | Denial of wallet | LlmGateway retry | mitigate | No app retry for Refused/Auth/SpendLimit/RateLimited/Transient/BadRequest; Invalid ≤3; Truncated ≤1; LlmGatewayTests | closed |
| T-01-10 | Tampering | Hostile model JSON | mitigate | Strict schema (DisallowAdditionalProperties), typed deserialize, parse fail → Invalid | closed |
| T-01-11 | Information disclosure | LlmResult / LlmCallException text | mitigate | Gateway holds no key; BuildMessage uses outcome/kind/message only | closed |
| T-01-12 | Tampering | Mixed providers across tiers | mitigate | LlmGateway ctor rejects mismatch; test | closed |
| T-01-13 | Information disclosure | Key in Anthropic errors | mitigate | SafeMessage from error.message; Message_NeverContainsKey | closed |
| T-01-14 | Denial of wallet | Anthropic spend cap | mitigate | spend-limit 429 / 402 / 400 usage → SpendLimit (fatal) | closed |
| T-01-15 | Tampering | Malformed Anthropic content | mitigate | TryPickText; unknown stop reason → Failed/Unknown | closed |
| T-01-16 | Spoofing | Custom endpoints | accept | No configurable base URL anywhere (verified) — see AR-01 | closed |
| T-01-17 | Denial of wallet | OpenAI insufficient_quota retry | mitigate | QuotaAwareRetryPolicy; test asserts 1 call | closed |
| T-01-18 | Information disclosure | Key in OpenAI messages | mitigate | SafeMessage from error.message; test asserts key absent | closed |
| T-01-19 | Tampering | Hostile OpenAI output | mitigate | Refusal/length/content_filter classified before parse | closed |
| T-01-20 | Denial of service | Image moderation 400 | mitigate | Returns null, caller skips; test | closed |
| T-01-21 | Information disclosure | keys.dat at rest | mitigate | DPAPI CurrentUser + entropy; KeysFile_ContainsNoPlaintext | closed |
| T-01-22 | Information disclosure | Legacy Base64 settings | mitigate | Migrated, emptied, deleted at startup (Program.cs:23); tests | closed |
| T-01-23 | Spoofing | DPAPI blob moved | mitigate | CryptographicException → absent; CorruptEntry_TreatedAsAbsent | closed |
| T-01-24 | Tampering | Partial keys.dat write | mitigate | temp file + File.Move(overwrite) | closed |
| T-01-25 | Information disclosure | Key in error dialog | mitigate | Only IO/crypto ex.Message shown | closed |
| T-01-26 | Information disclosure | Key text boxes | mitigate | UseSystemPasswordChar on both boxes | closed |
| T-01-27 | Information disclosure | Key in status / telemetry | mitigate | Status from LlmKeyTestResult.Message; telemetry removed | closed |
| T-01-28 | Information disclosure | Legacy Base64 save path | mitigate | SaveApiKey/LoadApiKey deleted; persist only via ApiKeyStore after pass | closed |
| T-01-29 | Denial of wallet | Key test spend | mitigate | Free model-retrieve endpoints only; test | closed |
| T-01-30 | Tampering | Mixed providers (state) | mitigate | Tier configs filtered by TextProvider; ctor rejects mismatch | closed |
| T-01-31 | Tampering | Out-of-range model values | mitigate | SuggestedEmailCount clamp 5..20; index bounds check; strict schema | closed |
| T-01-32 | Tampering | Hostile theme colors/fonts | mitigate | SanitizeHexColor + AllowedFonts allowlist | closed |
| T-01-33 | Denial of wallet | Generator retries | mitigate | No generator retries; GetValueOrThrow; fatal theme errors rethrown | closed |
| T-01-34 | Denial of service | Media without OpenAI key | mitigate | IsMediaAvailable gating + ApplyMediaAvailability in both steps | closed |
| T-01-35 | Repudiation | Silent feature loss | mitigate | Orange notice on Topic, Config and keys screens | closed |
| T-01-36 | Tampering | Hostile email JSON | mitigate | Strict schemas + post-validation helpers | closed |
| T-01-37 | Denial of service | One failed call aborting run | mitigate | Per-storyline catch + RunIsolatedAsync; test | closed |
| T-01-38 | Denial of wallet | Media calls without key | mitigate | `_media == null` passes return before API calls | closed |
| T-01-39 | Information disclosure | Dead retry code paths | mitigate | Dead methods and OpenAIService deleted | closed |
| T-01-40 | Denial of wallet | Continuing after fatal | mitigate | IsFatal → runCts.Cancel(); test asserts request count | closed |
| T-01-41 | Denial of service / data loss | Fatal stop discarding work | mitigate | SavePartialAsync on fatal/cancel/exception paths; test asserts EML files | closed |
| T-01-42 | Information disclosure | Key in run log | mitigate | Log shows only classified messages / fixed strings | closed |
| T-01-43 | Denial of wallet | Media planning without key | mitigate | `_media != null` guards planning; EmailGeneratorMediaGatingTests | closed |
| T-01-44 | Tampering / EoP | SDK outside gateway | mitigate | PlatformTests.OnlyServicesAiReferencesProviderSdks | closed |
| T-01-45 | Information disclosure | Hard-coded Slack webhook | mitigate + transfer | Telemetry removed; TelemetryRemovalTests source scan. Residual in git history transferred to user — see AR-03 | closed |
| T-01-46 | Denial of wallet | SDK auto-retry of spend-cap 429 | mitigate | AnthropicSpendLimitHandler x-should-retry:false; test Calls==1 | closed |
| T-01-47 | Denial of service | Long non-streaming requests | mitigate | max_tokens clamp 21_333; wire test | closed |
| T-01-12-01 | Information disclosure | Provider timeout messages | mitigate | Fixed strings, no ex.Message; TransportTimeoutTests | closed |
| T-01-12-02 | Tampering | TestKeyAsync → SetKey | mitigate | Key box + Clear disabled; KeyTestGuard after await; tests | closed |
| T-01-12-03 | Denial of service | Transport timeout aborting run | mitigate | Failed(Network) / null media; TransportTimeoutTests | closed |
| T-01-12-04 | Repudiation | Silent success (provider layer) | mitigate | All provider paths return classified results | closed |
| T-01-13-01 | Repudiation | Outer OCE catch | mitigate | RecordUnexpectedStop; StepComplete DescribeStatus; tests | closed |
| T-01-13-02 | Denial of service | Storyline lambda / RunIsolatedAsync | mitigate | Non-run OCE recorded as skip; tests | closed |
| T-01-13-03 | Tampering | DistributeEmails | mitigate | Est. Emails capped at 10,000 (grid CellValidating + Math.Clamp), math in double; overflow tests (commit e1300d3) | closed |
| T-01-13-04 | Information disclosure | result.Errors / StepComplete text | accept | Fixed or sanitized strings, local only — see AR-02 | closed |

*Status: open · closed*
*Disposition: mitigate (implementation required) · accept (documented risk) · transfer (third-party)*

---

## Accepted Risks Log

| Risk ID | Threat Ref | Rationale | Accepted By | Date |
|---------|------------|-----------|-------------|------|
| AR-01 | T-01-16 | Only the SDK default HTTPS endpoints are used; no user-configurable base URL exists (verified by audit: no Endpoint/BaseUrl/new Uri in app source). | greg45 (plan disposition, audit-verified) | 2026-09-30 |
| AR-02 | T-01-13-04 | Run errors and StepComplete text are fixed strings or sanitized provider messages; displayed locally only. | greg45 (plan disposition, audit-verified) | 2026-09-30 |
| AR-03 | T-01-45 (residual) | Slack webhook removed from source, but the URL remains in git history (commit ab7331e and earlier). Transferred to owner: user revokes/rotates the webhook in Slack. | greg45 (confirmed 2026-09-30) | 2026-09-30 |

*Accepted risks do not resurface in future audit runs.*

**Advisories (non-blocking, from audit):**
- `StepApiConfiguration.TestKeyAsync` generic catch shows raw `ex.Message` in the local status label (not shown to contain keys).
- Provider network-error messages append transport `ex.Message`; the OpenAI HttpRequestException path has no dedicated key-absence test.
- OpenAI's own 401 message includes a masked key suffix (`sk-…abcd`); the full key never appears.

---

## Security Audit Trail

| Audit Date | Threats Total | Closed | Open | Run By |
|------------|---------------|--------|------|--------|
| 2026-09-30 | 55 | 54 | 1 (T-01-13-03, low) | gsd-security-auditor |
| 2026-09-30 | 55 | 55 | 0 | orchestrator — T-01-13-03 fixed in e1300d3 (214/214 tests pass) |

---

## Sign-Off

- [x] All threats have a disposition (mitigate / accept / transfer)
- [x] Accepted risks documented in Accepted Risks Log
- [x] `threats_open: 0` confirmed
- [x] `status: verified` set in frontmatter

**Approval:** verified 2026-09-30
