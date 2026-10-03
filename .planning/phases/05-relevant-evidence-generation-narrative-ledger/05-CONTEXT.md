# Phase 5: Relevant Evidence Generation & Narrative Ledger - Context

**Gathered:** 2026-10-03
**Status:** Ready for planning

<domain>
## Phase Boundary

Starting from an **approved Phase 4 corpus plan**, this phase writes the **relevant threads** as a coherent evidentiary story and saves them as EML files:
- each email is written from a **point-in-time context**: only facts dated on or before it that its participants could know (CORP-01);
- relevant threads render in **chronological waves**. After each thread, the new details it introduced go into a **narrative ledger**, and later threads receive the relevant ledger entries (CORP-08..10);
- output is checked against the plan, canonical values and the ledger. Violations trigger retries (CORP-02, CORP-10);
- emails read as natural business correspondence with varied subtlety, deliberate hot documents, privileged counsel threads, distinct voices and signatures, and correct threading (CORP-03..07);
- EMLs are written into **every participating custodian's mailbox folder** with an `X-ReelDiscovery-DocID` header (OUT-01, OUT-02);
- every finished thread is saved immediately. One failure never aborts the run. Cancel or a crash keeps finished work, and progress and cost stay live (RUN-01, RUN-02, RUN-06, RUN-07);
- the ledger is saved with the case and exports as a readable "story so far" (CORP-11).

Requirements: CORP-01..11, OUT-01, OUT-02, RUN-01, RUN-02, RUN-06, RUN-07.

**Out of scope:** attachment content (Office docs, .ics, images, voicemail) and all non-relevant email content (Phase 6). Phase 5 renders relevant threads as text/HTML email only. Planned attachments on relevant messages are left for Phase 6, and their key rows stay `Planned`. Batch API, prompt caching, the 5K-scale QA scans and cutover are Phase 7.

</domain>

<decisions>
## Implementation Decisions

### Carried forward (locked in Phases 1-4)
- **D-01:** All LLM work goes through `ILlmGateway` with strict JSON-schema DTOs and works with Claude or OpenAI. The Phase 2 strict-schema limits apply (≤16 nullable fields, no recursion, `yyyy-MM-dd` date strings, provider output caps → chunk the work).
- **D-02:** **The plan owns structure.** Every date/time, sender, To/CC, reply/forward kind, fact link, label, hot/privileged flag, DocID and planned file path comes from `corpus-plan.json` (Phase 4 D-13). The renderer writes text only, and the LLM never decides people, dates or labels.
- **D-03:** Names are kept exactly as the guide has them, **including names from fictional works**. Prompts never say that names must be fictional. Content takes a firm position.
- **D-04:** Every new or changed screen is rendered at **144 DPI (150% scaling)** with the render-test harness and checked for clipping before hand-off. Use the Phase 2/3/4 theme and controls (`Theme`, `ThemedButton`, `CardPanel`, `SlimProgressBar`).
- **D-05:** The answer key is a projection of plan + run state. Its `Status` column (Phase 4 D-29) moves from `Planned` to Generated / Generated with warnings / Failed / Skipped as threads finish, and it is regenerable after a partial or cancelled run. The DocID goes into `X-ReelDiscovery-DocID` (D-30).
- **D-06:** Fatal provider stops (Auth / SpendLimit) end the run cleanly and keep completed threads. Refusals are not blindly retried (Phase 1 PROV-04 rules).

### Narrative ledger
- **D-07:** **The thread-writing call also returns the ledger delta.** The same strict-schema response holds the messages plus a structured list of new details the thread introduced. There is no separate extraction call.
- **D-08:** **A ledger entry is structured:** date (from the source message), kind (commitment / figure / meeting / decision / person / document / other), a one-line statement, the exact value if any, the participant IDs who know it (participants of the source message, extended by later forwards/CCs), related fact and issue IDs, and the source DocID. Entries get stable code-owned IDs.
- **D-09:** **Filtered and capped context:** a later thread's prompt includes only ledger entries dated **before** its first message and **known to at least one of its participants**, ranked by shared fact/issue IDs and then recency, and capped at **about 25-40 entries** (exact cap is Claude's discretion).
- **D-10:** **Export only, no in-app ledger viewer.** The ledger is saved with the case (`case.json` or a sibling file in the case folder, planner's choice for size). It exports as a readable **"story so far" .docx**, opened from Case Summary like the other case documents.

### Waves, checks & retries
- **D-11:** **Time-window waves, parallel inside each wave.** Relevant threads are grouped into waves by first-message date (short windows, about one to two weeks; exact sizing is Claude's discretion). Threads within a wave run in parallel under the existing parallelism limit, and each sees only the ledger from **earlier waves**. A wave's ledger deltas merge after the wave completes.
- **D-12:** **Code checks only, no LLM judge.** Before a thread is accepted, code checks the following:
  - message count and order match the plan;
  - no unknown people are named (cast names / known externals only);
  - canonical registry values appear exactly when used, and no conflicting variant of a canonical value appears;
  - no fact IDs, canonical values or ledger values that are **future** to the message (dated after it) or unknown to its participants;
  - numbers, dates and values do not contradict prior ledger entries.
- **D-13:** **2 retries (3 attempts total).** Each retry includes the specific violations as feedback.
- **D-14:** **Out of retries → keep and flag.** Save the best attempt (fewest violations), record the violations in the run journal, and set the answer-key Status to **"Generated - with warnings"**. Its ledger details still enter the ledger.
- **D-15:** API/transport failures (as opposed to check failures) are retried by the gateway. A thread that still cannot be written is logged and marked Failed in the key, and the run continues (RUN-02).

### Email look & voice
- **D-16:** **Themed HTML + plain-text alternative.** Reuse `HtmlEmailFormatter` (per-organization theme, quoted history, signature) and write multipart/alternative EMLs. **Light inline formatting is welcome where it fits naturally:** bold for emphasis, short bullet lists, occasionally a simple table of figures. Keep it modest ("not too crazy"); most emails are plain paragraphs.
- **D-17:** **Subtlety is mostly indirect, assigned by code.** About **60% indirect** (logistics, oblique references, "call me", euphemism), **30% clearly relevant**, and **10% explicit**. **Hot documents are always explicit.** Code assigns a level per message so the mix is exact. Relevant email never uses legal framing (no "this evidences the fraud").
- **D-18:** **Privileged threads carry realistic markers.** Most privileged emails have a "Privileged & Confidential / Attorney-Client Communication" banner or subject tag, counsel's signature block and legal-advice content. **A small share (about 10-20%) lack the label**, as in real privilege review. Privilege is still known from the plan.
- **D-19:** **Story tier** writes relevant threads. Volume tier is reserved for Phase 6 non-relevant filler.
- **D-20:** **Voice comes from the cast, and code appends the signature.** Each prompt includes the sender's `VoiceNote` and `SignatureStyle`. **Code appends the cast `Signature` verbatim** so it never drifts. Short replies may sign with a first name or initials when `SignatureStyle` allows. The model never writes the signature block.
- **D-21:** **Code sets a target length per message.** Most messages are short (1-4 sentences for replies), some are medium, and a few are long status/summary emails. Code assigns a length band per message so the distribution is controlled and can be matched by Phase 6 filler (NOISE-05).

### Claude's Discretion
- **Run screen and partial runs** (the user did not discuss these). Sensible defaults: a generation step after Review Plan (Approve → Generate), with an inline stage label, progress bar (waves / threads / emails), Cancel, live cost ticker and a short log, following the Case Guide/Review Plan run pattern. On reopening a case with a partial run, offer to **continue** (skip journaled Done threads; this is cheap with the journal) or **start over**, with a confirm before discarding. Confirm on close during a run (like C12).
- Run state storage: an append-only journal (JSONL) in the case folder per the research architecture. The answer key is re-projected from plan + journal at the end, on cancel, and on reopen.
- Mailbox folder layout and file naming follow the Phase 4 planned paths and `CustodianIds` (sender's copy in Sent, recipients' copies in Inbox). Writing the copies after the thread passes checks is the unit of work.
- Threading details: Message-ID domain from the sender's org, In-Reply-To/References chains, quoted history built by code from earlier rendered messages (the model does not re-quote), and RE:/FW: prefixes via `ThreadingHelper`.
- Organization themes for the case flow (derive from the existing `ThemeGenerator` or a deterministic palette per organization).
- Wave window size, parallelism within a wave, ledger cap, ledger ranking, prompt layout (stable case-guide prefix first so Phase 7 caching can reuse it), DTO decomposition for long threads.
- The exact set of code checks beyond D-12 and how value conflicts are detected (normalization of amounts/dates).
- The "story so far" .docx layout (chronological, grouped by month or issue).

</decisions>

<canonical_refs>
## Canonical References

**Downstream agents MUST read these before planning or implementing.**

### Requirements & roadmap
- `.planning/REQUIREMENTS.md` — CORP-01..11, OUT-01/02, RUN-01/02/06/07 (Phase 5); NOISE-05 and ATT-* (Phase 6) show what the renderer must later share with filler
- `.planning/ROADMAP.md` §Phase 5 — goal and 5 success criteria
- `.planning/PROJECT.md` — core value; Key Decisions "Running narrative ledger built during generation; relevant threads rendered in chronological waves" and "Code-owned plan (manifest) before prose"

### Research
- `.planning/research/ARCHITECTURE.md` — ThreadUnitOfWork (render → validate → map → write EML → journal → release), RunStore with append-only `journal.jsonl`, answer key as manifest + journal projection, ContextAssembler. Adapt to the codebase conventions (mutable classes, no records).
- `.planning/research/PITFALLS.md` — point-in-time context / knowledge gating, canonical value ledger scans, timeline drift, concealment behavior (pitfalls 1-3, 18). Note: the research warns against a free-text rolling "story so far" in prompts. The ledger here is **structured and filtered** (D-08/D-09), which satisfies that warning.
- `.planning/research/SUMMARY.md` — pipeline overview and phase rationale

### Prior phase outputs
- `.planning/phases/04-corpus-plan-answer-key/04-CONTEXT.md` — plan model, DocIDs, planned paths, custodians, Status column, answer-key columns, approval/staleness rules
- `.planning/phases/04-corpus-plan-answer-key/04-UI-SPEC.md` — run/progress UI conventions on Review Plan
- `.planning/phases/03-case-guide/03-CONTEXT.md` — cast voice/signature fields, KnownByIds, canonical values, autosave
- `.planning/phases/02-case-scenario-core-document/02-RESEARCH.md` — gateway usage, strict-schema limits, chunked generation

### Code
- `Models/Corpus/CorpusPlan.cs`, `PlannedThread.cs`, `PlannedMessage.cs` (Intent, MustConvey, FactIds, IsHot, FilePath, CustodianIds), `PlannedAttachment.cs`, `CorpusState.cs`, `AnswerKeyRow.cs`
- `Services/Corpus/CorpusPlanStore.cs`, `CorpusApproval.cs`, `AnswerKeyBuilder.cs` (`StatusPlanned`), `AnswerKeyCsvWriter.cs`, `AnswerKeyXlsxWriter.cs`, `PlanCallRunner.cs` (slot-limited cancellable calls), `CorpusPlanPrompts.cs`, `RelevantThreadFiller.cs`
- `Models/Case/CastMember.cs` (VoiceNote, SignatureStyle, Signature, ActiveFrom/To), `KeyFact.cs`, `CanonicalValue.cs`, `CaseOrganization.cs`
- `Helpers/HtmlEmailFormatter.cs` (`ConvertToHtml(plainText, OrganizationTheme?)`), `Helpers/ThreadingHelper.cs`, `Services/EmlFileService.cs`, `Services/ThemeGenerator.cs`, `Models/PresentationTheme.cs`
- `Services/Ai/ILlmGateway.cs` (Story tier), `Models/TokenUsageTracker.cs`
- `UserControls/StepReviewPlan.cs`, `StepCaseSummary.cs`, `Forms/WizardForm.cs` (`StepTypesFor`, stage mapping), `Services/Case/CaseStore.cs`, `Services/Case/CaseGuideDocxExporter.cs`

</canonical_refs>

<code_context>
## Existing Code Insights

### Reusable Assets
- `corpus-plan.json` via `CorpusPlanStore`: every relevant message already has DocID, SentAt + TimeZoneId, From/To/CC, Kind, ReplyToSeq, FactIds, IsHot, Intent, MustConvey, FilePath and CustodianIds.
- `AnswerKeyBuilder` + CSV/XLSX writers: re-run them with a status overlay from the journal.
- `PlanCallRunner`: slot-limited, cancellable parallel LLM calls with fatal-stop handling. This is the model for in-wave parallelism.
- `HtmlEmailFormatter.ConvertToHtml` (quotes, forwards, lists, signature), `ThreadingHelper` (Message-ID, RE:/FW:, quoting), `EmlFileService` (MimeKit). These are legacy-shaped around `EmailMessage`/`EmailThread`, so map plan → `EmailMessage` or add a plan-based overload.
- `CaseGuideDocxExporter` / `OfficeDocumentService.CreateWordDocument` for the "story so far" export.
- `CaseStore` (autosave, atomic writes), `FakeLlmProvider` + `CaseTestData` for tests without a live provider, 144-DPI render harness.

### Established Patterns
- Strict DTOs are nested internal classes with `[JsonPropertyName]` + `[Description]`. Code maps or drops unknown IDs returned by the model.
- Inline run UI with stage label, `SlimProgressBar`, Cancel, refusal/fatal/timeout handling (Case Guide, Review Plan).
- `Normalize()` repairs untrusted persisted JSON on load, so new ledger and journal models need the same.
- Use `Random.Shared`, `Interlocked`/concurrent collections, and merge results on the calling thread after `Task.WhenAll`.

### Integration Points
- `WizardForm.StepTypesFor(CaseDriven)`: add the generation step after `StepReviewPlan` and before `StepCaseSummary`. Add a `CaseStage` value.
- `StepCaseSummary`: list and open the generated mailboxes folder and the "story so far" .docx, and show run status counts.
- Case-flow organizations have no `OrganizationTheme` yet. Themes must be derived for `CaseOrganization` domains.

</code_context>

<specifics>
## Specific Ideas

- The user is open to **formatted text where appropriate (bolding, etc.), "not too crazy"**: occasional bold, short lists, a small figures table; most mail stays plain paragraphs.
- The test case is "inspired by The Office" (Dunder Mifflin). Expect `DM-000001` DocIDs and `michael.scott@dundermifflin.com` addresses, with names kept as-is.
- Indirect evidence should look like real concealment behavior: "call me", "let's discuss offline", euphemisms, moving to phone.

</specifics>

<deferred>
## Deferred Ideas

- In-app ledger viewer or editor: the user chose export only for v1.
- LLM consistency judge for semantic contradictions: code checks only for now. It could join the Phase 7 QA passes.
- Strictly sequential or dependency-aware scheduling: time-window waves chosen. Revisit if continuity across same-wave threads proves weak.

</deferred>

---

*Phase: 05-relevant-evidence-generation-narrative-ledger*
*Context gathered: 2026-10-03*
