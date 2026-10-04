---
phase: 06-attachments-non-relevant-corpus
plan: 09
subsystem: evidence-composition
tags: [email, composer, attachments, inline-images, bulk-mail, voicemail, determinism]
requires: ["06-02", "06-07"]
provides:
  - "EmailComposer.Compose(..., IReadOnlyDictionary<string, ComposedAttachment>? attachments = null, bool voicemailNotice = true)"
  - "ComposedEmail.Attachments filled in plan order; ComposedEmail.QuoteFragment always set (== HtmlFragment without inline images)"
  - "EmailComposer.BulkKindFor(thread, planned, sender) -> BulkMailKind?"
  - "PersonInfo.ExternalKind (externals) and PersonInfo.OrganizationName (cast), consumed by 06-17"
  - "VoicemailComposer: TranscriptHeader, IsVoicemail, DurationSeconds, Duration, Apply"
  - "internal EmailComposer.SanitizeDomain (reused for the voicemail sender domain)"
affects: [06-17 Office theme and voicemail sender, 06-20 attachment repair recompose, MailboxWriter multipart parts, answer-key voicemail rows]
tech-stack:
  added: []
  patterns: ["frozen Base64 golden output captured from the unchanged composer", "explicit copy instead of with-expression for BulkMailContent quote variant"]
key-files:
  created:
    - Services/Evidence/VoicemailComposer.cs
    - ReelDiscovery.Tests/Evidence/VoicemailComposerTests.cs
    - ReelDiscovery.Tests/Evidence/Golden/EmailComposerGolden.cs
  modified:
    - Services/Evidence/EmailComposer.cs
    - Services/Evidence/PeopleDirectory.cs
    - ReelDiscovery.Tests/Evidence/EmailComposerTests.cs
    - ReelDiscovery.Tests/Evidence/PeopleDirectoryTests.cs
decisions:
  - "Bulk formatter applies only to New messages from Newsletter/Spam/Personal senders in NewsletterSpamPersonal threads; a reply from a bulk sender, a vendor, or a bulk sender in a business thread uses the business path"
  - "Bulk PlainText ends with the sign-off (no signature block and no trailing newline); the business PlainText layout is unchanged byte-for-byte"
  - "Voicemail notice (D-28, UI-SPEC Decision 1): From \"{Recipient org} Voice Mail\" <voicemail@{first recipient domain}>, Subject 'Voicemail from {Caller} ({m:ss})'; all planned copies kept and the key From/Subject must match the EML"
  - "Voicemail duration = max(5, round(words / 2.5)) seconds (~150 wpm, 06-RESEARCH A4); dates en-US via InvariantCulture from MessageTime.Local"
  - "Voicemail notices carry no privilege banner and no signature; BodyPlain keeps the spoken transcript for TTS on retry"
  - "Bulk footer postal address: deterministic PO Box over 8 generic US city lines ranked by StableRank(domain + ':postal')"
metrics:
  duration: "~40 min"
  completed: 2026-10-04
  tasks: 2
  files: 7
---

# Phase 6 Plan 09: Composer Attachments, Bulk Mail and Voicemail Notice Summary

EmailComposer now attaches generated parts in plan order, renders inline images by `cid:` while every quote uses an image-free `QuoteFragment`, routes newsletter/spam/personal senders through BulkMailFormatter on the same header path, and turns single-message voicemail threads into phone-system notices with an auto-generated transcript.

## What was built

**Task 1: attachment parts, inline images, quote fragments, bulk branch**
- First step, before any production edit (commit bbd4d69): `EmailComposerGolden` freezes Base64 PlainText / Html / HtmlFragment from the unchanged composer for three fixture threads: F-003 (reply, reply-all, forward chain), F-001 (Banner) and F-004 (external vendor). `Compose_NoAttachments_MatchesGolden` passes on the new code and also asserts `QuoteFragment == HtmlFragment` and no attachments.
- `Compose` takes a trailing `IReadOnlyDictionary<string, ComposedAttachment>? attachments = null`. Each email gets the present parts of its own planned attachments, in plan order (`PartsFor`). Inline images become `InlineImageRef`s.
- Quoted parents use `parent.QuoteFragment` (Pitfall 10). In the business path `QuoteFragment` is `HtmlFragment` when there are no images, otherwise a placeholder render. In the bulk path the quote variant is an explicit `BulkMailContent` copy with `ImagePlaceholders = true` (06-07 contract).
- `BulkKindFor` maps Newsletter/Spam/Personal `ExternalKind` to `BulkMailKind` for new messages in NewsletterSpamPersonal threads. Bulk mail has an empty signature, `Publication = SenderName = sender.FullName`, `IssueDate = MessageTime.Local`, and a deterministic generic postal address.
- `PersonInfo` gains `ExternalKind` (external party kind; null for cast) and `OrganizationName` (from `guide.FindOrganization`; empty when unresolved).

**Task 2: voicemail phone-system notice**
- `VoicemailComposer.Apply` overrides From, Subject, PlainText, Html, HtmlFragment/QuoteFragment, SignOff and Signature. Message-ID, Date, To, Cc, threading, BodyPlain and attachments are left as planned. Names are cleaned of control characters before they go into headers (T-06-25), and HTML goes only through the allowlist formatter (T-06-26).
- `Compose` calls it when `voicemailNotice && VoicemailComposer.IsVoicemail(thread, planned)`. `voicemailNotice: false` keeps the Phase 5 From, Subject and body, plus the audio part, for 06-20 repair.
- Recipient without an organization: the From name is "Voice Mail" at the recipient's domain. A blank domain falls back to `EmailComposer.FallbackDomain`.

## Commits

| Task | Commit | Message |
|------|--------|---------|
| 1 (golden) | bbd4d69 | test(06-09): freeze Phase 5 EmailComposer golden output |
| 1 (RED) | 6fbf2fa | test(06-09): add failing tests for composer attachments, inline images and bulk branch |
| 1 (GREEN) | 1481d8b | feat(06-09): composer attachment parts, inline images, quote fragments and bulk branch |
| 2 (RED) | ceba2fb | test(06-09): add failing tests for voicemail phone-system notice |
| 2 (GREEN) | c6a6d8f | feat(06-09): voicemail phone-system notice composition |

## Verification

- `dotnet test --filter "EmailComposerTests|VoicemailComposerTests|PeopleDirectoryTests"`: 82 passed.
- Full suite: 2940 of 2941 passed (baseline 2909, plus 32 new tests). The one failure was `GenerationResetTests.StartOver_LockedFile_IsReported_RestStillDeleted`, which is timing-sensitive under full-suite load. It touches no code from this plan and passes on rerun (7/7 GenerationResetTests).
- The main project builds with 0 warnings. All grep acceptance criteria for both tasks pass. EmailComposer contains no Random, Guid.NewGuid, DateTime.Now or UtcNow.

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 1 - Bug] Bulk path restricted to New messages**
- **Found during:** Task 1
- **Issue:** The bulk variants have no quoted-history section. A bulk-sender reply would drop its quote from the HTML but keep it in the PlainText.
- **Fix:** `BulkKindFor` also requires `planned.Kind == MessageKind.New`, and it takes the planned message as a parameter. Bulk senders in NewsletterSpamPersonal threads are single-message roots in the plan, so planned output does not change.
- **Files modified:** Services/Evidence/EmailComposer.cs
- **Commit:** 1481d8b

**2. [Rule 1 - Bug] Bulk PlainText ends cleanly**
- **Found during:** Task 1
- **Issue:** The shared PlainText layout appends `SignOff + '\n' + Signature`. With the empty bulk signature this would leave a dangling newline.
- **Fix:** `PlainTextOf` takes a `bulk` flag and ends bulk text with the sign-off. The business layout is unchanged, and the golden test proves it.
- **Commit:** 1481d8b

**3. [Rule 3 - Blocking] SanitizeDomain made internal**
- VoicemailComposer needs the same domain sanitization for `voicemail@{domain}` (T-05-18). `EmailComposer.SanitizeDomain` changed from private to internal. Behavior is unchanged.
- **Commit:** c6a6d8f

**4. PeopleDirectoryTests extended**
- The ExternalKind/OrganizationName tests went into the existing `PeopleDirectoryTests.cs`, which the plan's verification filter covers. That file is not in `files_modified`.

## Known Stubs

None.

## Threat Flags

None. No new network, auth or file surface. Header and HTML mitigations T-06-25, T-06-26 and T-06-27 are implemented and tested: control-char stripping, allowlist formatters, and identical headers between the bulk and business paths.

## TDD Gate Compliance

Both tasks have a RED `test(...)` commit followed by a GREEN `feat(...)` commit. No refactor commits were needed.

## Self-Check: PASSED
