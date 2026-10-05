---
status: approved
phase: 06-attachments-non-relevant-corpus
source: [06-VALIDATION.md, 06-25-PLAN.md]
started: 2026-10-04T00:00:00Z
updated: 2026-10-05T00:00:00Z
---

## Current Test

Complete: the user approved the phase on 2026-10-05 after a live run on the Office case. Results were given as an overall sign-off, not step by step.

## Automated Gates (06-25 Task 1)

- `dotnet build ReelDiscovery.csproj`: 0 warnings, 0 errors
- `dotnet test ... --filter "Category!=Render"`: 3284 passed, 0 failed
- `dotnet test ... --filter "Category=Render"`: 113 passed, 0 failed (3397 total)
- The renders on this machine ran at **120 DPI (125%)**. `generate-corpus-ready-media-1100x750.png` is 1375x938. For true 144-DPI PNGs, set Windows display scaling to 150%, sign out and back in, then rerun the render command.

## Plan coverage

Source: the approved `corpus-plan.json` of the Office case. It was read only, not regenerated.
Case folder: `C:\Users\ghand\OneDrive\Documents\ReelDiscovery\Cases\Ashvale & Brindle LLP - Established Findings and Recommended Action Concerning D`
Plan settings: 1000 emails, 20% relevant, all four media switches on (Office, Calendar, Images, Voicemail), attachment share default. The plan has 462 threads, 1000 messages and 170 messages with attachments. 34 threads have attachments on two or more messages.

| AttachmentType | Total | On relevant | On non-relevant |
|----------------|-------|-------------|-----------------|
| Word | 95 | 25 | 70 |
| Excel | 55 | 17 | 38 |
| PowerPoint | 29 | 4 | 25 |
| Image | 16 | 3 | 13 |
| CalendarInvite | 8 | 1 | 7 |
| Voicemail | 2 | 0 | 2 |

| NonRelevantCategory | Threads |
|---------------------|---------|
| None (relevant) | 60 |
| DailyBusiness | 146 |
| Ancillary | 77 |
| NewsletterSpamPersonal | 154 |
| Decoy | 25 (all 25 carry MustNotMention) |

No kind or category has a count of 0, so no re-approval is needed for coverage. Thin spots to know about before Step 2:
- **Inline images: 0.** All 16 images are planned as file attachments. The inline (multipart/related + cid) path will not appear in this run.
- **Voicemail: 2, both on non-relevant threads.** Look for them in daily-business or ancillary mail, not in relevant mail.
- **Invites: 8, only 1 on a relevant thread.** A "two invites for one meeting" pair may not occur. If it does not, mark that sub-check skipped in Step 3.
- **Privileged non-relevant threads: 3** (relevant: 5).
- **Cost.** This is a 1000-email plan, not the small corpus the threat model (T-06-73) asks for. The estimate from the app's own `GenerationCostEstimator.Estimate` at default prices, before any retries, is about **$4.73** with OpenAI defaults (GPT-6.1 Sol / GPT-6 Luna) or about **$11.56** with Anthropic defaults (Claude Opus 5.5 / Claude Haiku 4.5). Media (images and voicemail audio) is about $0.11 of that. Check the Ready card before clicking Generate Emails. For the recovery steps (7-8), use a smaller copy of the case (for example 150-250 emails, attachment share raised, media on). Re-approve the plan on the copy and check its coverage the same way.

## Tests

| Step | Result (pass / issue / skipped) | Note |
|------|---------------------------------|------|
| 1. 144-DPI render inspection (06-22 / 06-24 PNGs, Large and WizardMin) | approved | Covered by the overall sign-off |
| 2. Live run start at 150%: coverage checked, Stages grid moves from waves to "Non-relevant mail", cost ticker moves | approved | Covered by the overall sign-off |
| 3. Attachments open from EMLs (docx/xlsx/pptx/ics/png/mp3): figures match the guide, Word header shows the org, deck subtitle is "{Org} \| {Month yyyy}", images have no readable text, paired invites share a time, voicemail notice + transcript + audio | approved | Covered by the overall sign-off |
| 4. Decoys (3) read as near-misses; newsletter, spam and personal (1 each) are recognizable and mundane, with no sensitive data and no live links | approved | Covered by the overall sign-off |
| 5. Relevant vs daily-business parity (3 + 3 threads): same template, signatures and banner. Per-band median word counts: relevant = _, daily-business = _ (per band). Names exactly as in the guide | approved | Covered by the overall sign-off |
| 6. Non-relevant copy in every participating custodian folder; answer-key non-relevant and attachment rows show Generated / Failed / Skipped with matching file names | approved | Covered by the overall sign-off |
| 7. Controlled failure + Continue: banner "The last run did not finish."; nothing written before Cancel is regenerated (mtimes unchanged); no answer-key row missing | approved | Covered by the overall sign-off |
| 8. Multi-message repair: only the missing item is created; the other message's attachment is byte-identical in every copy; email text unchanged | approved | Covered by the overall sign-off |
| 9. Phase 5 upgrade: banner "Part of the corpus is not written yet." with the "Attachments for written threads" row; Continue adds attachments (text unchanged, a Phase 5 voicemail keeps From/Subject and gains audio) and writes the non-relevant mail | skipped | No Phase 5 run folder (a Mailboxes folder plus a journal) was found under Documents\ReelDiscovery\Cases. Mark skipped with that reason unless one is available. |
| 10. Optional: no OpenAI key, then Continue/Retry. Media rows stay Skipped and the media banner explains why | skipped | Optional; not reported |

## Summary

total: 10
passed: 8
issues: 3 (all fixed, see Gaps)
pending: 0
skipped: 2
blocked: 0

## Gaps

Issues found during UAT. All were fixed and committed before approval:

1. **The model dialog clipped at 150% (144 DPI).** Remove was cut in half, Reset Defaults was hidden, and no grid rows showed. Cause: the window size and minimum size were fixed pixel values. Fix b5831ce: both are now logical sizes scaled with LogicalToDeviceUnits and limited to the working area. Two AttachmentRepair tests also failed in Test Explorer runs, because static test size caps leaked between test classes running in parallel. Fix 7d78ac6: the caps are AsyncLocal.
2. **Personal emails came from "A friend" <a.friend@gmail.com>.** Cause: SubjectSeeder accepted any two-word senderName for a personal contact. Fix 31cd841: EmailIdentity.LooksLikePersonName rejects roles and relationships, and the code-generated contact is kept.
3. **Ryan Howard emailed himself in 20 threads (72 messages).** Cause: facts with only one person under Who knew became notes-to-self threads, and merged two-fact threads did the same. Fix 262f9c9: the planner adds the closest colleague under Who knew. The additions are carried on the plan as AddedKnowers and applied to the guide when the plan is saved. Existing plans keep their self-addressed threads until they are re-planned.

Still open, deferred to Phase 7 (see deferred-items.md):
- Privilege banners appear only on relevant mail in small corpora.
- Invites for the same meeting can get different times when sent from different time zones.
- GenerationReset.TryDelete has no retry when a file is briefly locked.
- Theme fonts in the business HTML path are not sanitized.

