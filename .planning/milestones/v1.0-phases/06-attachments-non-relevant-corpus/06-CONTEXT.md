# Phase 6: Attachments & Non-Relevant Corpus - Context

**Gathered:** 2026-10-03
**Status:** Ready for planning

<domain>
## Phase Boundary

Starting from an **approved Phase 4 plan** and the **Phase 5 generation run** (journal, ledger, mailboxes), this phase fills every remaining planned slot so the corpus becomes a full mixed collection:
- **attachment content** for planned attachments: Word / Excel / PowerPoint (reusing `OfficeDocumentService`), calendar invites (.ics), inline/attached images and voicemail audio. Content matches the case guide, canonical values and the ledger, and media follows the provider availability rules (ATT-01..04, PROV-06);
- **all non-relevant threads** from the plan: custodians' daily business, ancillary-character traffic, newsletters/spam/personal mail, and near-miss decoys (NOISE-01..04);
- non-relevant mail flows through the **same renderer/formatter/mailbox path** as relevant mail, so only content separates the two (NOISE-05);
- non-relevant EMLs land in every participating custodian's mailbox (OUT-01 completion);
- a failed attachment, image, invite or voicemail is logged and retried or skipped without aborting the run (RUN-02 completion).

Requirements: ATT-01..04, NOISE-01..05, OUT-01, RUN-02.

**Out of scope:** Batch API execution, prompt caching, the blind-review/leakage/timeline QA passes, 5K-scale memory work and old-wizard cutover (Phase 7). Ancillary discovery documents (Phase 8). No changes to how the plan allocates slots, since Phase 4 already reserved categories, attachment specs, subjects and decoy angles.

</domain>

<decisions>
## Implementation Decisions

### Carried forward (locked in Phases 1-5)
- **D-01:** All LLM work goes through `ILlmGateway` with strict JSON-schema DTOs and works with Claude or OpenAI (Phase 2 strict-schema limits apply).
- **D-02:** **The plan owns structure.** Category, participants, dates, thread shape, labels, DocIDs, file paths and attachment specs (kind, file name, title, description, inline) come from `corpus-plan.json`. The renderer writes text and content only.
- **D-03:** Names are kept exactly as the guide has them, **including names from fictional works**. Prompts never say names must be fictional.
- **D-04:** Every new or changed screen is rendered at **144 DPI (150% scaling)** with the render-test harness and checked for clipping. Use the existing theme and controls.
- **D-05:** The answer key is a projection of plan + journal. Attachment and non-relevant rows move from `Planned` to Generated / Generated with warnings / Failed / Skipped as work finishes.
- **D-06:** Images and voicemail require an OpenAI key (Phase 1 PROV-06). If the user disabled those types or no key exists, the plan has no such specs. Fatal provider stops end the run cleanly and keep finished work.
- **D-07:** Code assigns length bands, formatting allowances and signatures (Phase 5 D-16, D-20, D-21). Filler uses the same mechanisms so distributions match.

### Model tier & leakage
- **D-08:** **Volume tier writes non-relevant filler** (daily business, ancillary, newsletters/spam/personal). The user accepts the style-leakage risk for cost. Phase 7's leakage audit measures it.
- **D-09:** **Near-miss decoys use the Story tier**, the same model as relevant mail, so they stay true near-misses.
- **D-10:** **One thread per call** for non-relevant threads, mirroring relevant rendering (same per-thread unit of work, journal records and EML writing). There is no multi-thread batching.
- **D-11:** **Shared prompt skeleton + code-side shaping** blunts tier differences: the same prompt structure, voice notes, length bands, formatting allowances and code-appended signatures for both tiers. The model only fills prose.
- **D-12:** **Non-relevant threads get structure-only checks** (message count/order match the plan). There are no ledger or knowledge-date checks for filler.
- **D-13:** **Non-relevant threads do not read or write the narrative ledger.**

### Attachment content
- **D-14:** **A separate call per attachment runs after its thread is accepted.** It receives the parent email text, the plan spec, and the point-in-time case context (canonical values and the ledger slice known to the participants as of the email date). An attachment can fail without losing its email.
- **D-15:** **Richness varies by importance.** Attachments on hot / Key-fact relevant messages get full documents (Word about 1-3 pages with headings, Excel one sheet with roughly 10-40 rows of real figures, PowerPoint about 4-8 slides). Supporting-fact and non-relevant attachments get shorter ones (half-page memo, small table, 2-3 slides). Exact sizing is Claude's discretion.
- **D-16:** **Version chains for relevant documents.** When planned relevant attachments share a title/purpose across threads, later versions build on earlier content (changed figures consistent with the ledger and canonical values) and carry a version suffix. The legacy `EmailGenerator` "document chains" concept is the reference. How chains are detected or linked is the planner's choice.
- **D-17:** **Relevant attachments get the same code checks as relevant emails** (Phase 5 D-12): canonical values exact, no conflicting variants, nothing future-dated or unknown to participants, and no unknown people. They get 2 retries with violation feedback, then are kept and flagged "Generated - with warnings".
- **D-18:** **Attachments return ledger deltas.** The attachment call also returns the new details it introduced (figures, schedules, totals), merged into the ledger like thread deltas (at wave end) so later emails cite them consistently.
- **D-19:** **Office docs are org-branded through the existing builders.** Reuse `OfficeDocumentService` with the sender organization's theme (`OrganizationThemes`). Modest upgrades: Word headings and simple tables, and Excel with real numeric cells. This is not a rewrite.
- **D-20:** **Relevant calendar invites tie to ledger meetings / fact dates.** An invite on relevant mail must match a 'meeting' ledger entry or a key-fact date/time exactly (organizer, attendees and time consistent with the plan and ledger). Code builds the .ics (`CalendarService`). The LLM supplies only title, agenda and location. Non-relevant invites use plan data (organizer = sender, attendees = recipients, a business-hours slot after the email).

### Non-relevant texture
- **D-21:** **Each organization gets a small invented side-world.** Before filler renders, 2-4 recurring non-case projects/accounts per organization are fixed (names and a few topics) by code or one cheap call, and persisted with the run. Daily-business threads draw from that list, so a custodian's inbox mentions the same side projects repeatedly. Side projects must not collide with case facts or terms.
- **D-22:** **Bulk mail is styled and recognizable.** Newsletters get a publication-style HTML layout with an unsubscribe footer. Spam looks like marketing or mild phishing. Personal mail is plain and casual from consumer domains. These need formatter variants beyond the business `HtmlEmailFormatter` template.
- **D-23:** **Decoys get a code scan of their must-not-mention list** (fact titles plus linked canonical values from `PlannedThread.MustNotMention`). On a hit they retry with feedback up to 2 times, then are kept and flagged. There is no LLM judge.
- **D-24:** **Personal mail stays mundane and workplace-safe**: dinner plans, kid pickups, fantasy football, appointment reminders. Nothing sensitive (no medical, financial or HR-type PII).

### Run orchestration & media
- **D-25:** **Relevant waves run first, then filler, in one run.** Generate Corpus keeps one Generate button and one progress bar with stages: relevant threads + their attachments in waves (ledger-dependent), then non-relevant threads + their attachments in parallel (no dependencies). Continue / Retry Failed resume either part from the journal.
- **D-26:** **A failed attachment does not hold back its email.** The EML is written without it, the attachment's key row is marked Failed, and the email text is not changed. Continue / Retry Failed regenerates only the missing attachment and rewrites the affected EMLs (all custodian copies).
- **D-27:** **Images are contextual photos/graphics** driven by the plan spec and case setting: a site photo, product shot, whiteboard snapshot, or a marketing graphic in spam. **No legible text or figures in images**, because image models garble them and could contradict canonical values.
- **D-28:** **Voicemails are delivered as phone-system emails with a transcript.** A single-message thread from a voicemail-system address ("Voicemail from {caller}, 0:42") carries an audio attachment and an auto-transcript in the body. TTS uses the caller's cast voice. A relevant voicemail transcript goes through the same code checks as relevant email.
- **D-29:** **Costs fold into the existing ticker.** Attachment text calls count on their tier. Image and TTS calls add per-unit costs from the model catalog. The Generate Corpus Ready-card estimate is extended to cover attachments, filler and media.

### Clarifications (2026-10-04, user, during plan revision)
- **D-12 clarification (2026-10-04, user):** filler structure checks are **V1/V5 only** (message count/order, non-empty). **No V8** length check or length retry on non-relevant threads. Filler length parity relies on the code-assigned length bands in the prompt (D-07/D-11). Resolves 06-RESEARCH Open Question 3.
- **D-28 clarification (2026-10-04, user):** the caller's "cast voice" is **chosen by one per-cast voice call** (cheap Volume tier) that picks a fitting tts-1 voice for each cast member from name/role/description. The choice is persisted in the journal (CastVoices record, alongside the SideWorld record), reused on Continue / Retry Failed and removed by Start Over. A deterministic StableRank voice is used only if the call fails. Resolves 06-RESEARCH Open Question 5.

### Claude's Discretion
- Exact richness thresholds per importance level, and attachment DTO shapes per kind (Word sections, Excel rows/columns, slides) within strict-schema limits.
- How version chains are identified (shared title stem vs. an explicit plan link) and how much of the prior version is passed as context.
- Side-world generation method (deterministic code vs. one cheap call), storage location (case folder sibling or journal), and naming rules that avoid case terms.
- Newsletter/spam/personal formatter designs, voicemail-system sender address and audio format.
- Parallelism of the filler stage, journal record types for attachments, and how Retry Failed rewrites EMLs (MailboxWriter is already byte-deterministic).
- How a relevant invite is matched to a ledger meeting when the plan has no explicit link (closest meeting entry or fact on or after the email date), plus the fallback when none fits (treat like non-relevant invite and flag with a warning).
- Run-screen copy and stage labels for the new stages, following the Phase 5 UI-SPEC patterns.

</decisions>

<canonical_refs>
## Canonical References

**Downstream agents MUST read these before planning or implementing.**

### Requirements & roadmap
- `.planning/REQUIREMENTS.md` — ATT-01..04, NOISE-01..05, OUT-01, RUN-02 (Phase 6); PROV-06 media availability
- `.planning/ROADMAP.md` §Phase 6 — goal and 4 success criteria
- `.planning/PROJECT.md` — core value and Key Decisions

### Research
- `.planning/research/ARCHITECTURE.md` — ThreadUnitOfWork, RunStore/journal, answer key as projection
- `.planning/research/PITFALLS.md` — label leakage, canonical value contradictions, timeline drift
- `.planning/research/FEATURES.md` — non-relevant mix and attachment expectations

### Prior phase outputs
- `.planning/phases/05-relevant-evidence-generation-narrative-ledger/05-CONTEXT.md` — renderer, ledger, checks/retries, mailbox writing, journal (this phase extends it)
- `.planning/phases/05-relevant-evidence-generation-narrative-ledger/05-UI-SPEC.md` — Generate Corpus screen conventions
- `.planning/phases/04-corpus-plan-answer-key/04-CONTEXT.md` — non-relevant categories (D-08, D-14), attachment planning, DocIDs, answer-key columns/Status
- `.planning/phases/01-foundation-provider-gateway/` — media client (gpt-image, tts-1) and PROV-06 gating

### Code
- `Models/Corpus/PlannedThread.cs` (Category, DecoyAngle, MustNotMention, OwnerCustodianId), `PlannedMessage.cs`, `PlannedAttachment.cs`, `ExternalParty.cs`, `CorpusSettings.cs`
- `Services/Evidence/*` — `GenerationRunner`, `ThreadUnitOfWork`, `ThreadRenderer`, `EvidencePrompts`, `RenderValidator`, `RenderRepair`, `RenderAssignments`, `LedgerMerger`, `LedgerSelector`, `KnowledgeGate`, `EmailComposer`, `MailboxWriter`, `GenerationJournal`, `GenerationOutputs`, `GenerationReset`, `OrganizationThemes`, `WavePlanner`
- `Services/OfficeDocumentService.cs`, `Services/CalendarService.cs`, `Models/Attachment.cs`, `Helpers/HtmlEmailFormatter.cs`
- `Services/EmailGenerator.cs` — legacy attachment/image/voicemail/document-chain passes (reference only; slated for removal in Phase 7)
- `Services/Corpus/AnswerKeyBuilder.cs`, `GenerationCostEstimator.cs`, `Models/AIModelConfig.cs` (tiers, pricing)
- `UserControls/` Generate Corpus step and `StepCaseSummary.cs`

</canonical_refs>

<code_context>
## Existing Code Insights

### Reusable Assets
- Phase 5 `GenerationRunner` + `ThreadUnitOfWork` + journal: the per-thread unit of work (render → validate → compose → write EML copies → journal) that filler reuses with a different tier and check set. The journal already replays records for any thread in the plan (05-10).
- `RenderValidator` / `KnowledgeGate` / `LedgerSelector`: point-in-time checks and context reused for relevant attachments.
- `OfficeDocumentService` (Word/Excel/PowerPoint builders with theme), `CalendarService` (.ics), legacy media client for gpt-image and tts-1.
- `MailboxWriter`: deterministic boundaries make rewriting an EML after a late attachment byte-stable.
- `NonRelevantSkeletonBuilder` / `SubjectSeeder` (Phase 4): subjects, decoy angles, external parties and must-not-mention lists are already in the plan.

### Established Patterns
- Strict nested DTOs with `[JsonPropertyName]` + `[Description]`. Code maps or drops unknown ids.
- `PlanCallRunner`-style slot-limited, cancellable parallel calls with fatal-stop handling.
- `Normalize()` on all persisted JSON. `Random.Shared`; merge results on the calling thread after `Task.WhenAll`.
- Answer-key overlay from journal status; story/key re-projection throttled per finished thread.

### Integration Points
- `GenerationRunner`: add the filler stage after relevant waves and attachment work inside/after each thread unit.
- `GenerationRunState` / Generate Corpus step: new stages, counts covering non-relevant + attachments, and Continue / Retry Failed covering both.
- `AnswerKeyBuilder`: attachment rows get real status (currently they copy the email and stay Planned, 05-02).
- `GenerationCostEstimator`: include filler (Volume pricing), decoys (Story), attachments and media.
- `StepCaseSummary`: run status counts include non-relevant and attachments.

</code_context>

<specifics>
## Specific Ideas

- The test case is "inspired by The Office" (Dunder Mifflin). Side projects could be other paper clients or branch initiatives, kept clear of case facts.
- Voicemail email shape: "Voicemail from Michael Scott (0:42)" from a phone-system address, with transcript text and an audio file.
- Images must never contain readable figures or text.

</specifics>

<deferred>
## Deferred Ideas

- LLM judge for decoy drift or semantic contradictions: deferred to Phase 7 QA (blind review).
- A per-custodian side ledger for long-running daily-business continuity: the user chose no ledger for filler, so the side-world list (D-21) provides recurring context instead.
- Sensitive personal mail (medical/financial/HR PII) for PII-review training: excluded for v1.
- A separate media cost line: folded into the existing ticker for now.
- Batching multiple filler threads per call: rejected for parity. Phase 7 Batch APIs address cost.

</deferred>

---

*Phase: 06-attachments-non-relevant-corpus*
*Context gathered: 2026-10-03*
