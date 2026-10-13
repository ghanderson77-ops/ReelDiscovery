---
phase: 05-relevant-evidence-generation-narrative-ledger
plan: 07
subsystem: evidence-composition
tags: [email-composer, threading, message-id, signatures, privilege, html]
requires:
  - 05-01 (RenderedThread, ThreadAssignment, PrivilegeMarking, MessageTime, EvidenceTestData)
  - 05-04 (KnowledgeGate.Ancestors)
  - 05-05 (PeopleDirectory, OrganizationThemes, HtmlEmailFormatter.ComposeFragment/ComposeHtml)
provides:
  - Services/Evidence/EmailComposer (SubjectFor, MessageIdFor, SignatureFor, QuoteHeader, ForwardHeaderLines, Compose)
  - EmailAddressPart, ComposedEmail, ComposedThread
affects: [EML writer, answer-key overlay subjects, generation journal (BodyPlain/SignOff)]
tech-stack:
  added: []
  patterns: [pure composer, SHA-256 deterministic ids, en-US invariant date formatting, GeneratedRegex]
key-files:
  created:
    - Services/Evidence/EmailComposer.cs
    - ReelDiscovery.Tests/Evidence/EmailComposerTests.cs
  modified: []
decisions:
  - "[Phase 05] Message-IDs are stored without angle brackets (MimeKit adds them); domain keeps only [a-z0-9.-], trimmed of leading/trailing dots and hyphens, else reeldiscovery.local"
  - "[Phase 05] SignatureFor uses the first name only for Short-band non-New messages from NameOnly signers; every other cast case is the Signature verbatim (SignatureBuilder.Build(member, null) when blank); externals sign with their full name"
  - "[Phase 05] One table per thread is consumed only when the body has a pipe table AND ComposeFragment actually rendered it (rejected oversize tables do not use the allowance, matching 05-05)"
  - "[Phase 05] Model body and sign-off are CRLF-normalized and trimmed before plain text and HTML composition"
metrics:
  duration: 12min
  completed: 2026-10-03
  tasks: 2
  files: 2
---

# Phase 5 Plan 07: Email Composer Summary

Pure `EmailComposer` that turns a planned thread plus repaired model text into structured emails: SHA-256(PlanId:DocId) Message-IDs at the sender domain, ReplyToSeq-based In-Reply-To/References, one shared `SubjectFor` (matches the answer key), verbatim cast signatures, privileged banner/subject tag per marking, en-US quote/forward headers, and themed HTML through the 05-05 allowlist formatter.

## Tasks

| Task | Name | Commits |
|------|------|---------|
| 1 | Subjects, Message-IDs, signatures and headers | 700b4d8 (test), 37c7090 (feat) |
| 2 | Compose a whole thread | e5ae561 (test), 1a75614 (feat) |

## What Was Built

- **Types:** `EmailAddressPart`, `ComposedEmail` (Planned, ThreadId, MessageId, InReplyTo, References, Subject, From/To/Cc, Date, PlainText, Html, BodyPlain, SignOff, Signature, `[JsonIgnore] internal HtmlFragment`), `ComposedThread`.
- **SubjectFor:** strips `\p{Cc}` (CR/LF header-injection guard, T-05-17), prepends `Privileged & Confidential - ` for BannerAndSubject (once, case-insensitive), then `ThreadingHelper.AddReplyPrefix/AddForwardPrefix`. Verified equal to `AnswerKeyBuilder` subjects for every relevant fixture message.
- **MessageIdFor:** 24 lower-hex chars + `@` + sanitized domain (T-05-18).
- **QuoteHeader / ForwardHeaderLines:** `CultureInfo.GetCultureInfo("en-US")`, verified under de-DE.
- **Compose:** iterates planned messages by Seq, matches rendered text by Seq (ArgumentException naming the seq when missing), addresses/dates only from the plan (D-02), plain text `[banner\n\n]body\n\n[signOff\n]signature` + code-built quote or `---------- Forwarded message ----------` block, HTML fragment nested into children, theme from `OrganizationThemes.ForDomain(sender domain)`.

## Verification

- `dotnet test --filter "FullyQualifiedName~EmailComposerTests"`: 33 passed.
- `dotnet test --filter "FullyQualifiedName~ReelDiscovery.Tests.Evidence&Category!=Render"`: 296 passed.
- `dotnet test --filter "Category!=Render"`: 2318 passed, 0 failed.
- `dotnet build ReelDiscovery.csproj`: 0 warnings, 0 errors.
- All acceptance greps match.

## Deviations from Plan

None - plan executed as written. Small clarifications recorded as decisions above (table allowance confirmed against rendered output, body normalization, NameOnly rule limited to non-New messages).

## Known Stubs

None.

## TDD Gate Compliance

RED (`test(05-07)`) commits precede GREEN (`feat(05-07)`) commits for both tasks.

## Self-Check: PASSED
