---
phase: 06-attachments-non-relevant-corpus
plan: 07
subsystem: html-formatting
tags: [html, email, inline-images, bulk-mail, newsletter, spam, security]
requires: []
provides:
  - "InlineImageRef + inlineImages/imagePlaceholders on HtmlEmailFormatter.ComposeHtml/ComposeFragment"
  - "BulkMailFormatter.Compose / ComposeFragment (Newsletter, Spam, Personal) with BulkMailContent.ImagePlaceholders"
  - "internal HtmlEmailFormatter.Encode / ComposeInline / GetFontStack / ComposeState / AppendInlineImages for reuse"
affects: [06-09 EmailComposer bulk branch and quote fragments, 06 MailboxWriter multipart/related]
tech-stack:
  added: []
  patterns: ["frozen Base64 golden strings captured before a formatter change", "cid content-id allowlist regex"]
key-files:
  created:
    - Helpers/BulkMailFormatter.cs
    - ReelDiscovery.Tests/Evidence/BulkMailFormatterTests.cs
    - ReelDiscovery.Tests/Evidence/Golden/ComposeHtmlGolden.cs
    - ReelDiscovery.Tests/Evidence/Golden/ComposeHtmlGoldenFixtures.cs
  modified:
    - Helpers/HtmlEmailFormatter.cs
    - ReelDiscovery.Tests/Evidence/ComposeHtmlTests.cs
decisions:
  - "ComposeHtml golden constants mask the 45 KB disclaimer PNG (data:image/png;base64,{BANNER}) and are LF-normalized; template around it compared exactly"
  - "Inline images with a ContentId not matching ^[0-9a-f]{16}@[a-z0-9.-]+$ are silently omitted (T-06-20)"
  - "Spam button label is a fixed list chosen by a stable hash of the offer line (deterministic for Retry Failed rewrites; model never supplies button text)"
  - "Newsletter bold-run cap applies per section (fresh ComposeState per section); heading text does not consume the cap"
  - "BulkMailFormatter.Compose sanitizes the theme before WrapInTemplate because the template writes fonts/colors into <style> verbatim"
metrics:
  duration: "~45 min"
  completed: 2026-10-04
  tasks: 2
  files: 6
---

# Phase 6 Plan 07: Inline Images and Bulk Mail Formatter Summary

HtmlEmailFormatter now renders validated `cid:` inline images between body and signature (or `[image: title]` placeholders for quoted history), and a new BulkMailFormatter produces newsletter, spam and personal HTML through the same disclaimer template and encoder, with no links or model-supplied style.

## What was built

**Task 1 - inline images (HtmlEmailFormatter)**
- Golden capture first (commit d15c05b): 5 fixtures (plain, quoted reply, forward, privileged banner, table), each frozen as Base64 fragment + full HTML from the unchanged Phase 5 code. `ComposeHtml_NoInlineImages_MatchesGolden` and `InlineImages_EmptyList_MatchesGolden` prove byte-identical output.
- `InlineImageRef { ContentId, Title }`; trailing optional `IReadOnlyList<InlineImageRef>? inlineImages = null, bool imagePlaceholders = false` on both public methods; ComposeHtml passes through.
- `<p><img src="cid:..." alt="{encoded}" style="max-width:100%"></p>` or `<p>[image: {encoded}]</p>`; invalid ContentIds (quotes, brackets, uppercase, wrong length, empty) omitted.
- `Encode`, `ComposeInline`, `GetFontStack`, `ComposeState`, `AppendInlineImages` made internal for reuse.

**Task 2 - BulkMailFormatter**
- Newsletter: masthead (publication bold 18px theme primary, issue date `MMMM d, yyyy` 12px muted), 600px max-width table, up to 4 sections from `**Heading**` lines (or blank-line blocks, overflow merged into the last), `<h3>` headings, plain-text footer `You are receiving this because you subscribed to {publication}. Unsubscribe | Manage preferences | {postal address}` at 11px muted.
- Spam: bold offer line (first non-empty line), one padded `<span>` button in the theme primary, optional inline image, fixed 11px fine print.
- Personal: default theme font, plain `<p>` paragraphs, sign-off only when non-empty, no masthead/footer.
- All variants: `Compose` -> `HtmlEmailFormatter.WrapInTemplate`; hostile model text appears only encoded; no `<a `, `href="`, `<script`.

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 3 - Blocking] ComposeHtml golden masks the banner PNG**
- **Issue:** Full ComposeHtml output embeds the 45 KB disclaimer PNG; as Base64 the constant would be ~80 KB per fixture.
- **Fix:** `ComposeHtmlGoldenFixtures.MaskBanner` replaces the PNG payload with `{BANNER}` before capture and comparison; both sides LF-normalized (production template line endings depend on checkout). Fixture inputs live in `ComposeHtmlGoldenFixtures.cs` alongside `ComposeHtmlGolden.cs`.
- **Commit:** d15c05b

**2. [Rule 2 - Security] Sanitized theme for bulk WrapInTemplate**
- **Found during:** Task 2 (hostile-theme test)
- **Issue:** `BuildEmailTemplate` writes `theme.BodyFont`/colors into `<style>` unsanitized, so a hostile font name produced `<script` in the full document.
- **Fix:** `BulkMailFormatter.Compose` passes a sanitized copy (SanitizeHexColor / SanitizeFont with defaults). Business path left unchanged to keep golden output; logged in deferred-items.md.
- **Commit:** e79a70c

**3. [Test wording] `href=` assertion**
- Encoded model text legitimately contains the letters `href=&quot;`; tests assert no `href="` (a live attribute) and no `<a ` instead of no `href=` at all. Production source contains none of `<a `, `href=`, `<script` (acceptance grep = 0).

## TDD Gate Compliance

- Task 1: test d15c05b (golden), test d9f3096 (RED, compile failure) -> feat 3b5ab91 (GREEN)
- Task 2: test 6fa00d7 (RED, compile failure) -> feat e79a70c (GREEN)

## Verification

- `dotnet test --filter "ComposeHtmlTests|BulkMailFormatterTests|EmailComposerTests"`: 81 passed
- Full suite: 2808 passed, 0 failed (baseline 2781 + 27 new)
- `dotnet build ReelDiscovery.csproj`: 0 warnings

## Threat Flags

None beyond the plan's threat model (T-06-19/20/21 mitigated as specified).

## Self-Check: PASSED
