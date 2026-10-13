# Changelog

All notable changes to ReelDiscovery are listed here. Versions follow [Semantic Versioning](https://semver.org/).

## [2.0.0] - 2026-10-13

A full rebuild around a **case guide**. You describe a real-life dispute or investigation; ReelDiscovery writes the legal document, builds a structured case guide, plans every email in code, and then generates a consistent eDiscovery corpus of up to ~5,000 emails with an exact answer key, on **OpenAI or Anthropic Claude**.

### Breaking changes
- The topic → storylines → characters wizard is removed. Every run now uses the case-driven flow. Scenarios set in a TV show, movie or book still work: describe the case, and its characters and companies are kept as written.
- Requires a key for OpenAI or Anthropic. Images and voicemail audio still need an OpenAI key.
- API keys saved by 1.x (Base64 file) are not migrated. Enter your key again on the API Configuration step; it is now stored encrypted with Windows DPAPI.
- Opt-in Slack telemetry is removed.

### Added
- **AI providers:** OpenAI and Anthropic Claude behind one gateway with strict structured outputs, Story/Volume model tiers, retry and back-off, spend-limit detection and a free key test (30-second limit).
- **Describe the Case:** free-text scenario plus a Simple / Standard / Complex preset; real-entity check with fictional-name substitution; recent cases, open and new case.
- **Core document:** a Complaint, Petition or Investigation Memo with code-owned numbering and captions and no case-law citations; edit, regenerate sections, and validate before continuing.
- **Case Guide:** ID-stable issues, cast, dated key-facts timeline and canonical values (amounts, dates, names); checks with quick fixes; per-tab and per-entry regeneration; autosaved `case.json` in a per-case folder; export to Word.
- **Corpus Settings and Review Plan:** choose corpus size (up to 5,000), relevant share, non-relevant mix, date window and attachment types. Code plans every thread and message (business hours, time zones, holidays, participants, reply structure, facts, labels, attachments) before any text is written. Review, re-plan or approve, with a rough cost estimate.
- **Answer key and review protocol:** CSV and Excel answer key (one row per email and attachment, family relevance, Hot / Privileged / Near-miss flags, statistics and legend sheets) and a review protocol in Word, derived exactly from the approved plan.
- **Evidence generation:** relevant threads written in date order with a point-in-time knowledge gate and a running narrative ledger, so later emails build on earlier ones; continuity checks with retry.
- **Full mixed corpus:** case-driven Word, Excel, PowerPoint, calendar, image and voicemail attachments; non-relevant daily business, ancillary, newsletter, spam and personal mail; near-miss decoys on the same project and keywords.
- **Per-custodian mailboxes:** each email lands in its participants' Inbox and Sent Items folders with an `X-ReelDiscovery-DocID` header matching the answer key.
- **Scale and cost:** OpenAI and Anthropic Batch APIs (tracked on disk and collected after a restart), prompt caching, and per-operation cost accounting.
- **QA:** blind-review sampling with relabel or regenerate, a label-leakage audit, and end-of-run timeline and canonical-value scans on Case Summary.
- **Discovery documents (optional):** deposition excerpts, Requests for Production, Requests for Admission and Interrogatories grounded in the case guide, citing corpus DocIDs as exhibits, exported to Word.
- **Case Summary:** one place for exports, mailboxes, answer key, QA findings and discovery documents, with a way back to Generate Corpus.

### Changed
- Runs on .NET 10 (still a single self-contained `ReelDiscovery.exe`, Windows 10/11 x64).
- Images use gpt-image (DALL-E 3 is retired).
- Finished threads are saved as they complete. Cancel, a crash or one failed item never loses finished work; Continue Generating, Start Over and Regenerate Thread are available.
- Editing the case guide after a plan is approved withdraws the approval, and generation will not start from an outdated plan.
- Manual relabels in the answer key are kept across Continue, Regenerate Thread, reopening and Start Over; you are warned before Re-plan drops them.
- Switching provider no longer strands or double-pays the other provider's pending batches.

## [1.3.0] - 2026-01-27

### Fixed
- Email signatures are checked after generation and corrected when they don't match the sender.

### Changed
- EML files use an HTML-only body so every email client shows the styled version.

## [1.2.0] - 2026-01-27

### Added
- Batched email generation (15 per call with carried context) for 1,000+ email runs.

### Changed
- Attachment data is released after saving to limit memory use.
- Per-storyline error handling, a 10-minute API timeout and more retries.

## [1.1.0] - 2026-01-26

### Fixed
- Sender/signature mismatch, subject line in the body, image attachments not generating.

### Added
- Organization-specific email theming, gender-based voicemail voices, optional telemetry.

## [1.0.0] - 2026-01-25

### Added
- First release: email threads from movie, TV or business scenarios; Word, Excel and PowerPoint attachments; DALL-E images; TTS voicemail; calendar invites; proper email threading.

[2.0.0]: https://github.com/ghanderson77-ops/ReelDiscovery/compare/v1.3.0...v2.0.0
[1.3.0]: https://github.com/ghanderson77-ops/ReelDiscovery/compare/v1.2.0...v1.3.0
[1.2.0]: https://github.com/ghanderson77-ops/ReelDiscovery/compare/v1.1.0...v1.2.0
[1.1.0]: https://github.com/ghanderson77-ops/ReelDiscovery/compare/v1.0.0...v1.1.0
[1.0.0]: https://github.com/ghanderson77-ops/ReelDiscovery/releases/tag/v1.0.0
