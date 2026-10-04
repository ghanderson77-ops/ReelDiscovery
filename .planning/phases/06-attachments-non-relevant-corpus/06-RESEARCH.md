# Phase 6: Attachments & Non-Relevant Corpus - Research

**Researched:** 2026-10-04
**Domain:** Extending the Phase 5 evidence pipeline (C# / .NET 10 WinForms) with attachment content (OpenXml, iCalendar, OpenAI image/TTS), non-relevant thread rendering, MIME multipart composition and journal-driven resume
**Confidence:** HIGH for codebase integration (read directly from source; 538 Evidence tests green), MEDIUM for media and OpenXml details (docs plus training knowledge), LOW for the cost heuristics

<user_constraints>
## User Constraints (from CONTEXT.md)

### Locked Decisions

#### Carried forward (locked in Phases 1-5)
- **D-01:** All LLM work goes through `ILlmGateway` with strict JSON-schema DTOs and works with Claude or OpenAI (Phase 2 strict-schema limits apply).
- **D-02:** **The plan owns structure.** Category, participants, dates, thread shape, labels, DocIDs, file paths and attachment specs (kind, file name, title, description, inline) come from `corpus-plan.json`. The renderer writes text and content only.
- **D-03:** Names are kept exactly as the guide has them, **including names from fictional works**. Prompts never say names must be fictional.
- **D-04:** Every new or changed screen is rendered at **144 DPI (150% scaling)** with the render-test harness and checked for clipping. Use the existing theme and controls.
- **D-05:** The answer key is a projection of plan + journal. Attachment and non-relevant rows move from `Planned` to Generated / Generated with warnings / Failed / Skipped as work finishes.
- **D-06:** Images and voicemail require an OpenAI key (Phase 1 PROV-06). If the user disabled those types or no key exists, the plan has no such specs. Fatal provider stops end the run cleanly and keep finished work.
- **D-07:** Code assigns length bands, formatting allowances and signatures (Phase 5 D-16, D-20, D-21). Filler uses the same mechanisms so distributions match.

#### Model tier & leakage
- **D-08:** **Volume tier writes non-relevant filler** (daily business, ancillary, newsletters/spam/personal). The user accepts the style-leakage risk for cost. Phase 7's leakage audit measures it.
- **D-09:** **Near-miss decoys use the Story tier**, the same model as relevant mail, so they stay true near-misses.
- **D-10:** **One thread per call** for non-relevant threads, mirroring relevant rendering (same per-thread unit of work, journal records and EML writing). There is no multi-thread batching.
- **D-11:** **Shared prompt skeleton + code-side shaping** blunts tier differences: the same prompt structure, voice notes, length bands, formatting allowances and code-appended signatures for both tiers. The model only fills prose.
- **D-12:** **Non-relevant threads get structure-only checks** (message count/order match the plan). There are no ledger or knowledge-date checks for filler.
- **D-13:** **Non-relevant threads do not read or write the narrative ledger.**

#### Attachment content
- **D-14:** **A separate call per attachment runs after its thread is accepted.** It receives the parent email text, the plan spec, and the point-in-time case context (canonical values and the ledger slice known to the participants as of the email date). An attachment can fail without losing its email.
- **D-15:** **Richness varies by importance.** Attachments on hot / Key-fact relevant messages get full documents (Word about 1-3 pages with headings, Excel one sheet with roughly 10-40 rows of real figures, PowerPoint about 4-8 slides). Supporting-fact and non-relevant attachments get shorter ones (half-page memo, small table, 2-3 slides). Exact sizing is Claude's discretion.
- **D-16:** **Version chains for relevant documents.** When planned relevant attachments share a title/purpose across threads, later versions build on earlier content (changed figures consistent with the ledger and canonical values) and carry a version suffix. The legacy `EmailGenerator` "document chains" concept is the reference. How chains are detected or linked is the planner's choice.
- **D-17:** **Relevant attachments get the same code checks as relevant emails** (Phase 5 D-12): canonical values exact, no conflicting variants, nothing future-dated or unknown to participants, and no unknown people. They get 2 retries with violation feedback, then are kept and flagged "Generated - with warnings".
- **D-18:** **Attachments return ledger deltas.** The attachment call also returns the new details it introduced (figures, schedules, totals), merged into the ledger like thread deltas (at wave end) so later emails cite them consistently.
- **D-19:** **Office docs are org-branded through the existing builders.** Reuse `OfficeDocumentService` with the sender organization's theme (`OrganizationThemes`). Modest upgrades: Word headings and simple tables, and Excel with real numeric cells. This is not a rewrite.
- **D-20:** **Relevant calendar invites tie to ledger meetings / fact dates.** An invite on relevant mail must match a 'meeting' ledger entry or a key-fact date/time exactly (organizer, attendees and time consistent with the plan and ledger). Code builds the .ics (`CalendarService`). The LLM supplies only title, agenda and location. Non-relevant invites use plan data (organizer = sender, attendees = recipients, a business-hours slot after the email).

#### Non-relevant texture
- **D-21:** **Each organization gets a small invented side-world.** Before filler renders, 2-4 recurring non-case projects/accounts per organization are fixed (names and a few topics) by code or one cheap call, and persisted with the run. Daily-business threads draw from that list, so a custodian's inbox mentions the same side projects repeatedly. Side projects must not collide with case facts or terms.
- **D-22:** **Bulk mail is styled and recognizable.** Newsletters get a publication-style HTML layout with an unsubscribe footer. Spam looks like marketing or mild phishing. Personal mail is plain and casual from consumer domains. These need formatter variants beyond the business `HtmlEmailFormatter` template.
- **D-23:** **Decoys get a code scan of their must-not-mention list** (fact titles plus linked canonical values from `PlannedThread.MustNotMention`). On a hit they retry with feedback up to 2 times, then are kept and flagged. There is no LLM judge.
- **D-24:** **Personal mail stays mundane and workplace-safe**: dinner plans, kid pickups, fantasy football, appointment reminders. Nothing sensitive (no medical, financial or HR-type PII).

#### Run orchestration & media
- **D-25:** **Relevant waves run first, then filler, in one run.** Generate Corpus keeps one Generate button and one progress bar with stages: relevant threads + their attachments in waves (ledger-dependent), then non-relevant threads + their attachments in parallel (no dependencies). Continue / Retry Failed resume either part from the journal.
- **D-26:** **A failed attachment does not hold back its email.** The EML is written without it, the attachment's key row is marked Failed, and the email text is not changed. Continue / Retry Failed regenerates only the missing attachment and rewrites the affected EMLs (all custodian copies).
- **D-27:** **Images are contextual photos/graphics** driven by the plan spec and case setting: a site photo, product shot, whiteboard snapshot, or a marketing graphic in spam. **No legible text or figures in images**, because image models garble them and could contradict canonical values.
- **D-28:** **Voicemails are delivered as phone-system emails with a transcript.** A single-message thread from a voicemail-system address ("Voicemail from {caller}, 0:42") carries an audio attachment and an auto-transcript in the body. TTS uses the caller's cast voice. A relevant voicemail transcript goes through the same code checks as relevant email.
- **D-29:** **Costs fold into the existing ticker.** Attachment text calls count on their tier. Image and TTS calls add per-unit costs from the model catalog. The Generate Corpus Ready-card estimate is extended to cover attachments, filler and media.

### Claude's Discretion
- Exact richness thresholds per importance level, and attachment DTO shapes per kind (Word sections, Excel rows/columns, slides) within strict-schema limits.
- How version chains are identified (shared title stem vs. an explicit plan link) and how much of the prior version is passed as context.
- Side-world generation method (deterministic code vs. one cheap call), storage location (case folder sibling or journal), and naming rules that avoid case terms.
- Newsletter/spam/personal formatter designs, voicemail-system sender address and audio format.
- Parallelism of the filler stage, journal record types for attachments, and how Retry Failed rewrites EMLs (MailboxWriter is already byte-deterministic).
- How a relevant invite is matched to a ledger meeting when the plan has no explicit link (closest meeting entry or fact on or after the email date), plus the fallback when none fits (treat like non-relevant invite and flag with a warning).
- Run-screen copy and stage labels for the new stages, following the Phase 5 UI-SPEC patterns.

### Deferred Ideas (OUT OF SCOPE)
- LLM judge for decoy drift or semantic contradictions: deferred to Phase 7 QA (blind review).
- A per-custodian side ledger for long-running daily-business continuity: the user chose no ledger for filler, so the side-world list (D-21) provides recurring context instead.
- Sensitive personal mail (medical/financial/HR PII) for PII-review training: excluded for v1.
- A separate media cost line: folded into the existing ticker for now.
- Batching multiple filler threads per call: rejected for parity. Phase 7 Batch APIs address cost.
</user_constraints>

<phase_requirements>
## Phase Requirements

| ID | Description | Research Support |
|----|-------------|------------------|
| ATT-01 | Word, Excel and PowerPoint attachments are generated from the plan and case guide (reusing existing builders) | `AttachmentRenderer` per-kind strict DTOs (Pattern 4); `OfficeDocumentService` upgrades: Word headings and tables, numeric Excel cells, PowerPoint subtitle leak fix, org name, package metadata (Pattern 6, Pitfalls 6-8) |
| ATT-02 | Calendar invites (.ics) are generated for planned meetings | `InviteScheduler` matches the root's own Meeting delta, then visible ledger meetings, then fact dates (Pattern 7); `CalendarService` fixes for deterministic UID/DTSTAMP, DateTimeOffset and RFC 5545 escaping/folding (Pitfall 9) |
| ATT-03 | Inline images and voicemail audio are generated where planned (subject to PROV-06) | `IMediaGenerator` seam over `OpenAIMediaClient`; multipart/related with deterministic Content-IDs (Pattern 5); voicemail notice composition (Pattern 9); runtime media-unavailable handling (Pitfall 11) |
| ATT-04 | Attachment content is consistent with the case guide and canonical values | Attachment context gated per parent message seq by `KnowledgeGate` + `LedgerSelector`; `RenderValidator.CheckAttachment` content-rule subset V2/V3/V4/V6/V7 with retries (Pattern 3); attachment ledger deltas (Pattern 8); version chains (Pattern 10) |
| NOISE-01 | Custodians' daily business | Filler unit of work on Volume tier with side-world context (D-21, Pattern 2) |
| NOISE-02 | Ancillary-character traffic | Same filler path; plan already has Ancillary threads (`NonRelevantSkeletonBuilder.BuildAncillary`) |
| NOISE-03 | Newsletters, spam and personal email | `BulkMailFormatter` variants (newsletter/spam/personal) selected by `ExternalKind` (Pattern 11) |
| NOISE-04 | Near-miss decoys | Story-tier decoy render with `DecoyAngle` and a must-not-mention code scan plus retries (Pattern 2, D-23) |
| NOISE-05 | Same pipeline, matched distributions | One `ThreadUnitOfWork` with a `RenderProfile`; the same `EmailComposer`, `MailboxWriter`, length-band apportionment, privilege marking and signatures; attachment file names fixed for every row (Pitfalls 1-3); distribution-parity test |
| OUT-01 | Per-custodian mailbox copies | Unchanged `MailboxWriter.CopyPathsFor`; extend `GenerationReset` to non-relevant paths (Pitfall 13) |
| RUN-02 | A failed item is logged and retried or skipped without aborting the run | New journal record types `AttachmentDone`/`AttachmentFailed`/`AttachmentSkipped`, per-item try/catch, and Retry Failed that rebuilds the EML from the journal and harvests prior attachment parts (Pattern 12) |
</phase_requirements>

## Project Constraints (from CLAUDE.md)

- C# WinForms, single-file self-contained win-x64 publish. **Note:** CLAUDE.md still says .NET 8, but Phase 1 retargeted both projects to `net10.0-windows` with `global.json` SDK 10.0.401 [VERIFIED: ReelDiscovery.csproj, global.json]. Plan against net10.
- Reuse MimeKit (EML) and DocumentFormat.OpenXml (Office). Do not add new document libraries.
- Code conventions: one public type per file, file name = type name; `*Service` / `*Generator` / `*Helper` suffixes; `_camelCase` private fields; `ct` is always the last parameter; async methods end in `Async`; `[GeneratedRegex]` instead of `new Regex`; no `record` types, no primary constructors, no `#region`; implicit usings, no blank lines between using groups.
- Error handling: catch `OperationCanceledException` before `Exception`. Accumulate per-item failures, never abort bulk generation. Bare `catch` only for non-critical side effects, always with a comment. Never use a bare catch in core generation logic.
- Thread safety: `Random.Shared` in new code; `Interlocked` or locks for shared counters; merge results on the calling thread after `Task.WhenAll`.
- UI is built in code, using the Theme/CardPanel/GridStyle helpers. Every new or changed screen gets a 144 DPI render test (D-04 and the user memory "verify WinForms UI at 150% DPI").
- Names from fictional works are kept verbatim; never substitute them (user memory and D-03).
- GSD workflow: edits only through GSD commands.

## Summary

Phase 5 built a solid, deterministic, journal-driven pipeline for relevant threads: `GenerationRunner` (waves) → `ThreadUnitOfWork` (render → repair → validate → retry → compose → write every custodian copy → journal) → checkpoint (replay → answer key → story → persist). Almost everything Phase 6 needs plugs into that pipeline. The work is extension, not new architecture. Five structural gaps need deliberate design:

1. **`MailboxWriter.BuildMime` only emits `multipart/alternative`.** It has no attachments, no `multipart/related` for inline images and no per-part identity. Attachments need `multipart/mixed` and `multipart/related`, deterministic boundaries on *every* nested multipart, deterministic Content-IDs, and a per-part `X-ReelDiscovery-DocID` header.
2. **The journal and run state are thread-only.** `JournalRecordType` has RunStarted/ThreadDone/ThreadFailed/RunEnded. `GenerationRunState`, `WavePlanner.IsGeneratable`, `RenderAssignments.Compute`, `BuildOverlay` and `GenerationReset` all filter to relevant threads. Attachment records, a non-relevant state, attachment overlay rows and non-relevant Start Over are all new.
3. **Image and audio bytes cannot be regenerated deterministically, and OpenXml packages are probably not byte-stable.** Retry Failed rewrites EMLs (D-26), so it needs the bytes of attachments that already succeeded. The cleanest store is the EML on disk: harvest parts by their DocID header. Do not put base64 in the 200 MB-capped journal.
4. **The Phase 4 plan leaves non-relevant attachments with empty titles and default names** ("Document 1.docx", "Photo 1.png"), while relevant attachments have meaningful titles. Shipped as-is, that is a trivial relevance classifier (NOISE-05 leak). The renderer must produce titles, and code must derive one file name used by both the EML and the answer key.
5. **Several pieces the decisions assume do not exist yet:** a TTS voice per cast member (CastMember has only `VoiceNote`, a writing style), media unit prices in the catalog, an `OrganizationName` on themes (so the Word header never shows), privilege marking for *non-relevant* privileged daily-business threads, and a stable answer-key column for voicemail-system senders.

**Primary recommendation:** Generalize `ThreadUnitOfWork` with a `RenderProfile` (tier, prompt builder, check set, ledger on/off, assignment source). Add a separate `AttachmentUnitOfWork` that runs after a thread is accepted and before its EML copies are written. Extend `MailboxWriter` to compose attachments deterministically. Add three journal record types for attachments. Make every new projection a pure function of plan + journal, ranked by `RenderAssignments.StableRank`, exactly as Phase 5 does.

## Architectural Responsibility Map

| Capability | Primary Tier | Secondary Tier | Rationale |
|------------|-------------|----------------|-----------|
| Run orchestration (relevant waves, then filler stage, resume) | Service: `Services/Evidence/GenerationRunner` | UI step (start, cancel, persist callback) | Runner already owns waves, fatal stops and checkpoints; UI must stay a thin view (RUN-07) |
| Per-thread render/check/compose/write (relevant + filler) | Service: `ThreadUnitOfWork` (profile-driven) | `ILlmGateway` | One path guarantees NOISE-05 parity |
| Attachment content (Office text, invite text, image prompt, voicemail transcript) | Service: new `AttachmentUnitOfWork` + `AttachmentRenderer` | `ILlmGateway` (Story for relevant, Volume for filler) | D-14: separate call after acceptance; isolated failure |
| Attachment bytes (docx/xlsx/pptx/ics/png/mp3) | Service: `OfficeDocumentService`, `CalendarService`, `IMediaGenerator` | — | Code builds files; the LLM never emits markup or binary (D-02, D-20) |
| Consistency checks | Pure code: `RenderValidator` (+ attachment entry point), decoy scan | — | Deterministic, no LLM judge (D-12, D-17, D-23) |
| Ledger | Pure code: `LedgerMerger` over thread + attachment deltas | Journal | D-18; non-relevant never touches it (D-13) |
| MIME composition + custodian copies | Service: `MailboxWriter` | File system (case folder) | Atomic, path-validated writes (T-05-27) |
| Durable progress | File: `generation-journal.jsonl` | `case.json` cached counts | Journal is the source of truth (RUN-01/06) |
| Answer key | Pure projection: `AnswerKeyBuilder` + overlay | CSV/XLSX writers | D-05 |
| Progress/cost display | UI: `StepGenerateCorpus`, `StepCaseSummary` via `GenerationSnapshot` | `TokenUsageTracker` | UI-SPEC to follow (`/gsd-ui-phase`) |

## Standard Stack

### Core (all already referenced; no new packages)
| Library | Version | Purpose | Why Standard |
|---------|---------|---------|--------------|
| MimeKit | 4.18.1 | Build EML with `multipart/mixed` + `multipart/related` + `multipart/alternative`; deterministic `Multipart.Boundary`, settable `ContentId` | Already the EML writer; `BodyBuilder.LinkedResources` + `Attachments` produce the standard nesting [VERIFIED: ReelDiscovery.csproj; CITED: github.com/jstedfast/mimekit GettingStarted.md via Context7] |
| DocumentFormat.OpenXml | 3.5.1 | docx/xlsx/pptx bytes; `CellValues.Number` + `CellValue(decimal)` for real numbers; `OpenXmlValidator` in tests | Existing builders and regression tests [VERIFIED: csproj, DocumentRegressionTests; CITED: officedev/open-xml-docs working-with-sheets.md] |
| OpenAI | 2.14.0 | `OpenAIMediaClient`: gpt-image (configured `gpt-image-2.5-flare`, quality `low`, 1024x1024 PNG) and `tts-1` MP3 | PROV-06 media path already built and tested [VERIFIED: Services/Ai/OpenAIMediaClient.cs, Models/GenerationConfig.cs] |
| Anthropic | 12.51.0 | Text provider behind `ILlmGateway` | Existing [VERIFIED: csproj] |
| Microsoft.Extensions.AI.Abstractions | 10.10.1 | `JsonSchemaRegistry` strict schemas for new DTOs | Existing; every new DTO goes into `CaseSchemaTests` [VERIFIED] |
| xUnit | 2.9.3 (+ Test.Sdk 17.14.1) | Tests | Existing, 1,922 facts/theories; Evidence suite 538 green in 31 s on 2026-10-04 [VERIFIED: local run] |

### Supporting (in-repo, reuse rather than rebuild)
| Component | Purpose | When to Use |
|-----------|---------|-------------|
| `PlanCallRunner` / `PlanCallRun` | Slot-limited, cancellable, fatal-aware LLM calls | Every new text call (filler, attachments, side-world) |
| `RenderAssignments.StableRank` + `Apportion.Largest` | Deterministic exact mixes | Filler length bands, non-relevant privilege marking, chain selection |
| `KnowledgeGate` (`MayMentionBySeq`, `KnownValueIdsBySeq`) + `LedgerSelector` | Point-in-time context per message | Attachment context (gate to the *parent message* seq, not the thread's last date) |
| `CanonicalValueScanner` | Amount and identifier scanning | Excel numeric parsing; decoy scan of amounts |
| `RelevantThreadFiller.FileNameFor(title, kind)` | Safe file names (80-char stem, code-chosen extension) | All attachment names, relevant and non-relevant |
| `OrganizationThemes.ForDomain` | Deterministic palette | Office docs (D-19), bulk-mail variants |
| `EmailComposer` (pure) | Headers, threading, signatures | Filler and voicemail emails; rebuilding EMLs from the journal on retry |

### Alternatives Considered
| Instead of | Could Use | Tradeoff |
|------------|-----------|----------|
| Harvesting prior attachment parts from the on-disk EML | Sidecar binary store `{case}/_attachments/{DocId}.bin` | Sidecar is simpler to read but duplicates every image/MP3, needs Start Over/cleanup and may be ingested by users who load the whole case folder. Harvest reuses bytes that already exist |
| Base64 bytes in journal records | — | Rejected: the journal has a 200 MB cap (`MaxJournalBytes`), and one PNG is about 1-2 MB |
| An LLM call to pick chain members | Pure code on title stems | Code is deterministic and free; chains only need stable grouping |

**Installation:** none. **Version verification:** the package versions above were read from `ReelDiscovery.csproj`. No upgrade is required for this phase.

## Architecture Patterns

### System Architecture Diagram

```
 Generate / Continue / Retry Failed (StepGenerateCorpus)
            |
            v
 GenerationRunner.RunAsync(plan, draft, folder)
   | replay journal (threads + attachments + side-world) -> state
   |
   |== Stage A: relevant waves (unchanged order, ledger-dependent) ======================
   |   for each wave: ledger = LedgerMerger(thread deltas + attachment deltas, earlier waves)
   |     parallel per thread -> ThreadUnitOfWork[Relevant profile, Story tier]
   |        render -> repair -> V1..V8 -> retry(2) -> accept
   |        -> AttachmentUnitOfWork for each planned attachment on accepted messages
   |             (context: parent text + spec + KnowledgeGate(seq) + ledger slice + own thread deltas
   |                       + prior chain version)
   |             Office/Invite: LLM DTO -> CheckAttachment -> retry(2) -> build bytes
   |             Image: prompt (code) -> IMediaGenerator.Image -> bytes | null
   |             Voicemail: TTS(transcript = message body) -> bytes | null
   |        -> compose emails (+ attachments, inline cids) -> MailboxWriter (all copies)
   |        -> journal ThreadDone, then Attachment{Done|Failed|Skipped}
   |     wave end: merge thread + attachment deltas; checkpoint (key + story)
   |
   |== Stage B: filler (no ledger, no waves, parallel) ===================================
   |   side-world: replay SideWorld record or create (1 Volume call / code fallback) -> journal
   |   parallel per non-relevant thread -> ThreadUnitOfWork[profile by Category]
   |        DailyBusiness/Ancillary/Bulk: Volume tier, structure-only checks
   |        Decoy: Story tier, structure checks + must-not-mention scan, retry(2)
   |        -> AttachmentUnitOfWork (no ledger, no V3-V6) -> compose (bulk variants) -> write -> journal
   |
   |== Stage C: attachment repair (Retry Failed / Continue on threads already done) =======
   |   for done threads with missing/failed attachments:
   |        rebuild RenderedThread from JournalMessages -> EmailComposer.Compose (pure)
   |        harvest succeeded parts from the sender-copy EML by X-ReelDiscovery-DocID
   |        generate only the missing items -> rewrite every custodian copy -> journal
   |
   v finally: RunEnded -> replay -> answer key (emails + attachment rows + file-name overlay)
              -> story-so-far -> persist (UI thread) -> snapshot finished
```

### Recommended Project Structure (new files only)
```
Services/Evidence/
├── RenderProfile.cs            # tier, prompt builder, check set, ledger on/off per thread kind
├── FillerPrompts.cs            # shared skeleton for non-relevant + decoy prompts (byte-stable system prompt)
├── FillerAssignments.cs        # length bands + privilege marking for non-relevant threads (separate from relevant)
├── SideWorld.cs / SideWorldBuilder.cs   # D-21 per-organization side projects (+ code fallback)
├── DecoyScanner.cs             # must-not-mention scan (whole word; amounts by value)
├── AttachmentUnitOfWork.cs     # one attachment: render -> check -> retry -> bytes, never throws
├── AttachmentRenderer.cs       # per-kind strict DTOs + requests
├── AttachmentPrompts.cs        # system/user prompts per kind and richness
├── AttachmentNaming.cs         # one file-name function for EML + key (title, chain version suffix)
├── AttachmentChains.cs         # pure: chain id + version per relevant attachment DocId
├── InviteScheduler.cs          # pure: match invite to Meeting delta / ledger / fact date; slot times
├── AttachmentHarvester.cs      # read existing EML, return bytes by part DocID
├── IMediaGenerator.cs          # seam over OpenAIMediaClient for tests
├── VoiceAssignments.cs         # deterministic tts voice per person id
Helpers/
├── BulkMailFormatter.cs        # newsletter / spam / personal HTML variants (allowlist, encoded)
Models/Evidence/
├── AttachmentRecord fields on JournalRecord (or a nested JournalAttachment class)
├── AttachmentRunStatus.cs
```

### Pattern 1: Profile-driven unit of work (NOISE-05 parity)
**What:** Keep one `ThreadUnitOfWork.RunAsync`. Inject a `RenderProfile` that picks the tier, prompt builder, validator, ledger use and assignment. Do not fork a `FillerUnitOfWork`. Forks drift: a different composer call, signature rule or write order would create a metadata signal.
**When:** All four non-relevant categories and relevant threads.
```csharp
// Sketch only: fields mirror existing ThreadUnitOfWork inputs
public class RenderProfile
{
    public LlmTier Tier { get; set; }                       // Story (relevant, decoy) / Volume (filler)
    public Func<ThreadRenderContext, IReadOnlyList<RenderViolation>?, string> BuildPrompt { get; set; } = null!;
    public string SystemPrompt { get; set; } = string.Empty; // const, byte-stable per profile (Phase 7 caching)
    public Func<RenderedThread, ThreadRenderContext, List<RenderViolation>> Check { get; set; } = null!;
    public bool UsesLedger { get; set; }                     // false for all non-relevant (D-13)
}
```
`ThreadUnitOfWork.Cost` currently uses `_storyModel` for every call. Make cost tier-aware with `_llm.ResolveModel(profile.Tier)`. Otherwise filler spend is priced at Story rates and the ticker and journal disagree with the gateway's `TokenUsageTracker`.

### Pattern 2: Filler and decoy prompts share the relevant skeleton
Reuse `EvidencePrompts` section order: (a) name rules without scenario, (b) organizations, (c) people with voice and sign-off style, (d)/(e) **replaced** by "Ongoing work" (the side-world projects for DailyBusiness/Ancillary) or, for decoys, the "Angle" plus project/organization names (`ValueKind.ProjectName`, `Address`, org names are allowed shared keywords) and the must-not-mention list, (g) thread subject + purpose line, (h) the same `MessageLine` (kind, from/to/cc, local time, `length: min-max words`, attachments by title), (i) task, (j) feedback last. Use the **same** `ThreadRenderDto` schema, minus `newDetails`: give filler its own DTO without `newDetails` so it can never write the ledger (D-13). Keep the system prompt text a `const`. For filler, adapt the "Except where a thread is marked as an exchange with counsel..." clause: non-relevant privileged daily-business threads exist (`NonRelevantSkeletonBuilder` sets `IsPrivileged` with in-house counsel), so `CounselText` must apply to them too.

**Personal-mail safety (D-24):** add a fixed instruction line for `ExternalKind.Personal` threads ("everyday personal plans only: meals, kids, sports, errands, appointments; no health, money, account, HR or identity details"). Spam: "marketing or mild phishing tone; no working links, no real brands". `HtmlEmailFormatter` and the bulk variant render no links anyway.

### Pattern 3: Attachment checks reuse RenderValidator rules (D-17)
`RenderValidator.Check` cannot be called as-is with a synthetic one-message thread. `CheckStructure` (V1) counts every planned message and `CheckLength` (V8) uses message length bands. Add an entry point that runs only the content rules over a `CheckContext` whose `Planned` list holds just the parent message:
```csharp
// New internal entry in RenderValidator (same Run() isolation, same dedupe)
internal static List<RenderViolation> CheckAttachment(string flattenedText, IReadOnlyList<LedgerDelta> newDetails,
    PlannedMessage parent, ThreadRenderContext ctx)
{
    // RenderedThread with one RenderedMessage { Seq = parent.Seq, Body = flattenedText }
    // Run: CheckPeople (V2), CheckCanonicalVariants (V3), CheckKnowledge (V4), CheckLedger + CheckLedgerDates (V6), CheckLegalWording (V7)
    // Skip: CheckStructure (V1/V5), CheckLength (V8)
}
```
Flatten attachment text deterministically: Word = headings + paragraphs + table cells; Excel = header + every cell joined by " | " per row; PowerPoint = slide titles + bullets; invite = title + agenda + location; voicemail = transcript. The parent email's body counts as "seen text" for V4 (the attachment travels with it). Extend `SeenText` or pass the body as an ancestor.

### Pattern 4: Strict per-kind DTOs (D-01, strict-schema limits)
No nullable members (each nullable becomes a union type; Anthropic allows at most 16 [VERIFIED: StrictSchemaValidator.CountUnionTypes and 02-RESEARCH]), no recursion, no constraint keywords (`minItems`, `maxLength` and similar are forbidden by `StrictSchemaValidator`), strings for dates. Bound sizes in prose and clamp in code.
```csharp
internal class WordAttachmentDto
{
    [JsonPropertyName("title")] [Description("Document title as it appears at the top")] public string Title { get; set; } = string.Empty;
    [JsonPropertyName("sections")] public List<WordSectionDto> Sections { get; set; } = new();
    [JsonPropertyName("table")] [Description("One optional table; empty headers means no table")] public SimpleTableDto Table { get; set; } = new();
    [JsonPropertyName("peopleNamed")] public List<string> PeopleNamed { get; set; } = new();
    [JsonPropertyName("newDetails")] public List<ThreadRenderer.LedgerDeltaDto> NewDetails { get; set; } = new(); // seq ignored; code sets parent seq
}
internal class WordSectionDto { public string Heading { get; set; } = ""; public List<string> Paragraphs { get; set; } = new(); }
internal class SimpleTableDto { public List<string> Headers { get; set; } = new(); public List<List<string>> Rows { get; set; } = new(); }
// Excel: Title, SheetName, Headers, Rows (List<List<string>>), Notes; numbers are strings, code parses them
// PowerPoint: Title, Subtitle, Slides[{Title, Bullets[]}]
// Invite: Title, Agenda, Location (D-20: nothing else)
// Filler attachments: same DTOs without NewDetails (D-13)
```
Add every new DTO type to a `SchemaTypes` list and to `CaseSchemaTests.CaseSchemaTypes()`.

### Pattern 5: Deterministic multipart composition (extends MailboxWriter)
```csharp
// Source: MimeKit BodyBuilder (Context7 /jstedfast/mimekit GettingStarted.md) + existing BoundaryFor
var builder = new BodyBuilder { TextBody = email.PlainText, HtmlBody = email.Html };
foreach (var inline in email.Attachments.Where(a => a.IsInline && a.Kind == AttachmentType.Image))
{
    var part = builder.LinkedResources.Add(inline.FileName, inline.Bytes);
    part.ContentId = inline.ContentId;                         // deterministic, e.g. hash(DocId)@domain, never MimeUtils.GenerateMessageId()
    part.Headers.Add(DocIdHeader, inline.DocId);
}
foreach (var file in email.Attachments.Where(a => !a.IsInline))
{
    var part = builder.Attachments.Add(file.FileName, file.Bytes, ContentType.Parse(file.MimeType));
    part.Headers.Add(DocIdHeader, file.DocId);                 // harvest key + OUT-02 tie-back
}
var body = builder.ToMessageBody();
SetBoundaries(body, email);   // recurse every Multipart: boundary = "=-rd-" + hex(SHA-256(DocId|MessageId|depth|index))[..24]
```
- Invites: use `ContentType("text", "calendar")` with parameters `method=REQUEST` and `charset=utf-8`, attached as a file named per plan. Outlook also recognizes an inline `text/calendar` alternative, but an attachment is the safer, simpler choice [ASSUMED].
- Inline image HTML: `ComposeFragment` HTML-encodes every model string. Add an optional `inlineImages` parameter so the formatter emits `<img src="cid:..." alt="{encoded title}">` itself (trusted markup), after the body and before the signature.
- **Quoted history:** a reply's `QuotedPart.ParentFragment` would carry the parent's `cid:` reference, which the reply EML does not contain. Build a fragment variant without inline images for quoting (Pitfall 10).

### Pattern 6: OfficeDocumentService modest upgrades (D-19)
- **Word:** add `CreateWordDocument(string title, IReadOnlyList<(string Heading, IReadOnlyList<string> Paragraphs)> sections, SimpleTable? table, OrganizationTheme theme, DocumentMetadata meta)` as an overload. Keep the old signature for the legacy generator and `DocumentRegressionTests` (which pins 3 known `w:color` order errors). New runs should be validator-clean: emit `RunFonts, Bold, Color, FontSize` in schema order.
- **Excel:** numeric cells `new Cell { DataType = CellValues.Number, CellValue = new CellValue(decimal) }` when `CanonicalValueScanner.TryParseAmount` or an invariant number parse succeeds. Keep the formatted text in a sibling string column only if needed. Sanitize the sheet name: strip `: \ / ? * [ ]`, trim apostrophes at the ends, keep at most 31 characters, never empty. The current code only truncates, so an invalid name corrupts the workbook [ASSUMED from the Excel sheet-name rules].
- **PowerPoint:** `CreateTitleSlide(..., title, "Generated Email Dataset", t)` writes the subtitle **"Generated Email Dataset" on every deck**. That is a label/realism leak. Replace it with the DTO subtitle or "{Org} | {Month yyyy}".
- **Theme:** `OrganizationThemes.ForDomain` never sets `OrganizationName`, so the Word header bar never renders. Set it from `guide.FindOrganization(sender.OrganizationId).Name`.
- **Metadata (eDiscovery realism):** set `doc.PackageProperties.Creator`/`LastModifiedBy` = sender full name and `Created`/`Modified` = parent SentAt (UTC) minus a StableRank-derived offset of hours. Never write "ReelDiscovery" into app.xml or core.xml [ASSUMED: OpenXml `PackageProperties` API; verify with a unit test].

### Pattern 7: Invite scheduling (D-20)
Pure `InviteScheduler.Resolve(thread, rootMessage, ownDeltas, ledgerView, guide)`:
1. The root's **own** Meeting delta from this render (the email that schedules a meeting usually carries the invite). Value is `yyyy-MM-dd` and must be on or after the email date.
2. Else the closest visible ledger `Meeting` entry (`LedgerSelector` slice) dated on or after the email date, about the thread's facts.
3. Else a linked key fact (`thread.FactIds`) with a parseable date on or after the email date.
4. Else the non-relevant rule (next business-hours slot after the email) plus a warning violation: "invite not tied to a known meeting". The answer-key row becomes Generated with warnings.

The ledger stores dates only, so the time comes from code: a StableRank-chosen slot in 09:00-16:00 sender-local, 30 or 60 minutes. Organizer = sender, attendees = To+Cc from the plan. Use `BusinessCalendar` for weekdays and holidays when the date is code-chosen. A meeting date taken from the ledger keeps its date even on a weekend; do not move it.

### Pattern 8: Attachment ledger deltas (D-18)
Merge attachment deltas into the **parent thread's** delta list with `Seq = parent.Seq` (`LedgerMerger.EntriesFor` maps by seq, computes KnownBy from the message recipients and assigns `T-xxxx-Lnn` ids in kept order). Append them after the thread's own deltas so existing ids never change. **`MaxEntriesPerThread = 12` silently drops anything past 12.** Either raise the cap when attachments contribute, or give attachment-sourced entries their own cap (for example +6 per thread). Merge at wave end with the thread deltas (the runner's `deltas[threadId]`). `GenerationRunState.DeltasByThread` must also union deltas from `AttachmentDone` records on replay, or Continue would lose them.

### Pattern 9: Voicemail as a phone-system notice (D-28)
The plan models a voicemail as a `Voicemail` attachment on a **single-message** thread from the caller (`AllowedTypes`: voicemail only when `thread.Messages.Count == 1`) [VERIFIED: ThreadSkeletonBuilder.cs:761, NonRelevantSkeletonBuilder.cs:767]. Recommended realization:
- The thread render writes the message body **as the spoken transcript** (prompt line "voicemail: write what {caller} says on the phone, 20-90 words, spoken style"). Relevant transcripts get V2-V7 checks unchanged (D-28).
- TTS speaks the body (`tts-1`, MP3, input limit 4,096 characters [CITED: developers.openai.com/api/docs/models/tts-1]).
- Composer override: From = `"{Recipient Org} Voice Mail" <voicemail@{recipient org domain}>`, Subject = `Voicemail from {Caller} ({m:ss})`, body = code template ("You received a voicemail from {caller} ({phone}) on {date}... Transcript (auto-generated): ..."). No signature. Duration = round(words / 2.5 s) [ASSUMED ~150 wpm].
- **Answer key must match the EML** (the Phase 5 Pitfall 2 rule): the overlay needs `From` and `Subject` for voicemail rows. See Open Question 1 about the caller's Sent Items copy.

### Pattern 10: Version chains (D-16), pure and plan-derived
`AttachmentChains.Compute(plan)`: group relevant attachments of the same `Kind` by normalized title stem (lower-case, strip version tokens such as v2/final/draft/rev, dates and punctuation). Order by parent SentAt, then DocId. Groups of 2+ form a chain; version n = rank in the group. File name = `FileNameFor(stem + suffix)` with suffix `_v2`, `_v3`, and so on (keep the legacy labels "v4_final" only if you want the joke; plain is safer). Context for version n includes the prior version's **journaled flattened text** (cap about 1,500 words), if version n-1 is already done. If the prior version is in the same wave or still pending, generate without it and note "earlier draft not available". Waves are chronological, so earlier versions are usually done.

### Pattern 11: Bulk-mail formatter variants (D-22)
`BulkMailFormatter.Compose(kind, body, sender, theme)`, allowlist only, every model string encoded:
- Newsletter: masthead table (publication name, issue date), 2-4 headed sections parsed from `**Heading**` lines or `- ` items, footer "You are receiving this because you subscribed... Unsubscribe | Manage preferences | {postal address}" (plain text, `href="#"` or no anchor at all).
- Spam: bold offer line, a "button" styled `<span>`, fine-print footer. Optional image from `AssignSpamImages` (marketing graphic, inline).
- Personal: no template wrapper, default font, short; signature = first name or nothing (bulk senders sign with no cast signature).
- **All variants keep `WrapInTemplate`'s disclaimer banner** (every EML carries it today; dropping it on one category creates a signal).

### Pattern 12: Durable attachment records and Retry Failed
```csharp
public enum JournalRecordType { RunStarted, ThreadDone, ThreadFailed, RunEnded, AttachmentDone, AttachmentFailed, AttachmentSkipped, SideWorld }
// Attachment records: ThreadId, AttachmentDocId, ParentDocId, Kind, FileName (final), Status (Written/Warnings),
// Violations, FlattenedText (for chains + key), LedgerDelta, Files (rewritten EML copies), tokens, CostUsd, MediaCostUsd, Reason
```
- Enums serialize as strings (`JsonStringEnumConverter`), so an older build hits a JsonException on the new type names and skips the line (counted in `SkippedLines`). Forward compatibility is safe [VERIFIED: CaseDraft.JsonOptions, GenerationJournal.ReadRecords].
- Replay keeps `LastByAttachment[docId]`; spend sums every record (as for threads).
- **Order inside the unit (recommended):** render → accept → attachments (bounded by the same `PlanCallRun` slots; media through its own semaphore) → compose with the successful parts → write all copies → journal `ThreadDone` → journal one attachment record per planned attachment. A kill mid-attachments loses only that thread's unjournaled spend (already accounted via `AddUnjournaledSpend` when NotRun). The rule "journal line == files on disk" holds.
- **Retry Failed / Continue for attachments of done threads (Stage C):** rebuild `RenderedThread` from `JournalRecord.Messages` (BodyPlain + SignOff per seq; `Compose` is pure and deterministic), harvest every `AttachmentDone` part from the sender-copy EML by `X-ReelDiscovery-DocID`, generate only the missing parts, rewrite every copy. If harvest cannot find a part (user deleted the EML), regenerate it. Images and voicemail cost money again, so log that.
- **Existing Phase 5 runs:** relevant threads written in Phase 5 have no attachment records, so Stage C picks them up on the first Phase 6 Continue (see Existing Run State below).

### Anti-Patterns to Avoid
- **Changing `RenderAssignments.Compute`:** it decides privilege marking and therefore subjects of relevant mail already written (the key overlay uses `SubjectFor`). Compute non-relevant assignments in a *separate* function with its own salt. Never re-apportion relevant + non-relevant together.
- **Letting the model choose file names, dates or recipients for attachments or invites** (D-02, D-20).
- **Putting attachment bytes in the journal or case.json.**
- **Using `Guid.NewGuid()`, `DateTime.UtcNow`, `MimeUtils.GenerateMessageId()` or `Random.Shared` for anything written into an EML.** It breaks byte-stable rewrites and Continue determinism. Use `StableRank`/SHA-256 of DocId and SentAt.
- **Building media calls through `PlanCallRunner`:** media is a different provider (OpenAI) with null-on-failure semantics. Give it its own small semaphore (2) and classify exceptions yourself.
- **Running filler before relevant waves finish, or interleaved with them:** D-25 fixes the order. Filler needs no ledger, but decoys near fact dates must not influence relevant rendering.

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| MIME nesting, base64, RFC 2231 file names | String-built multipart | MimeKit `BodyBuilder` (LinkedResources, Attachments) + `Prepare(SevenBit)` | Header encoding, line limits and nesting order are easy to get wrong |
| OOXML packages | Raw zip/XML | DocumentFormat.OpenXml builders + `OpenXmlValidator` in tests | Schema order matters (the existing `w:color` bug proves it) |
| Amount parsing in Excel cells and decoy scan | Regex per call | `CanonicalValueScanner.TryParseAmount` / `AmountsEqual` / `Scan` | Already handles "$1.2M", commas and the 15% variant window |
| Whole-word term matching | `Contains` | `RenderValidator.ContainsWord` (internal) | Avoids "CO-007" inside "CO-0071" and similar false hits |
| Exact mixes | Random sampling | `Apportion.Largest` + `StableRank` | Deterministic on Continue and in other processes |
| Atomic, path-safe writes | `File.WriteAllBytes` | `CaseStore.WriteAtomicAsync` + `MailboxWriter.ResolveUnderCase` | T-05-27 path containment |
| Safe attachment names | Ad-hoc sanitize | `RelevantThreadFiller.FileNameFor` + `CorpusPlan.IsSafeFileName` | T-04-38 |
| Retry/backoff for text | Custom loops | `LlmGateway` + `PlanCallRunner` | Outcome classification and fatal stop already exist |

**Key insight:** every Phase 5 guarantee (deterministic reruns, journal = truth, key == EML) holds only if every new byte written to an EML is a pure function of plan + journal + harvested bytes.

## Existing Run State (resume after the Phase 5 → Phase 6 upgrade)

This is not a rename phase, but runs already on disk change meaning:

| Category | Items Found | Action Required |
|----------|-------------|-----------------|
| Stored data | `generation-journal.jsonl` with ThreadDone records for relevant threads and **no attachment records**; EMLs with no attachment parts | Code: Stage C treats "thread done, attachment has no record" as pending and rewrites those EMLs. No data migration |
| Live service config | None. Verified: no external service config; providers are called per request | none |
| OS-registered state | None. Verified: no scheduled tasks or services | none |
| Secrets/env vars | OpenAI key (`ApiKeyStore`), tested flag set per session in `StepApiConfiguration` | Media availability must be re-evaluated per run (Pitfall 11) |
| Build artifacts | None relevant | none |
| case.json | `Corpus.Generation` cached counts cover relevant only | Code: add non-relevant/attachment counts with `Normalize()` defaults (0) so old case.json loads |
| UI copy | `GenerationUiText.ScopeNotice` says non-relevant mail and attachments "are not generated yet" | Code: replace (UI-SPEC) |

## Common Pitfalls

### Pitfall 1: Default attachment names leak relevance (NOISE-05)
**What goes wrong:** Non-relevant attachments keep `Title = ""` and `FileName = "Document 1.docx"` / `"Photo 1.png"` from `NonRelevantSkeletonBuilder.AddAttachment` [VERIFIED: line 761]. Relevant attachments carry LLM titles ("CO-007 Pricing Backup.xlsx"). A classifier keyed on file names gets near-perfect accuracy.
**How to avoid:** The filler render (or the attachment call) returns a title. Code derives the file name with `FileNameFor`, plus the chain suffix for relevant chains. The final name goes in the attachment journal record, and the key overlay sets `FileName` from it. A single `AttachmentNaming.FinalName(...)` feeds both the EML and the key. The same applies to relevant fallback threads (`UsesFallbackContent`), where Title = the file stem.
**Warning signs:** The key's File Name column contains "Document 1" style names after a full run.

### Pitfall 2: Privilege banners only on relevant mail
**What goes wrong:** Non-relevant daily-business threads can be `IsPrivileged` (counsel included) [VERIFIED: NonRelevantSkeletonBuilder.cs:230]. `RenderAssignments.AssignPrivilege` only marks relevant threads. Without the same 85% banner / 40% subject-tag mix, "has privilege banner ⇒ relevant" becomes a signal, and the key's Privileged=Y rows have no banner.
**How to avoid:** `FillerAssignments` applies the same `LabeledShare`/`SubjectTagShare` over non-relevant privileged threads with a different salt. The key overlay uses `SubjectFor(..., marking)` for non-relevant rows too.

### Pitfall 3: Length/CC/depth distributions drift between tiers
**What goes wrong:** The Volume model ignores length bands or writes uniform medium emails. Bulk mail is naturally longer.
**How to avoid:** Apportion bands separately over non-relevant business messages (DailyBusiness + Ancillary + Decoy) with the identical 55/35/10 shares and the same Long-only-for-New/Forward rule. Run V8 (length) as a *structure* check for filler; D-12 allows count/order checks, and V8 is a gross-length check, not a ledger check (confirm, Open Question 3). Exclude newsletter/spam/personal from the parity population. CC counts and thread depth come from the plan, so they already match.
**Warning signs:** A parity test over a fake run shows different band shares between the populations.

### Pitfall 4: Gating attachment knowledge to the wrong date
**What goes wrong:** `ThreadRenderContext.Knowledge.LastDate` is the thread's *last* message date. An attachment on message 1 would see values that only become knowable at message 5.
**How to avoid:** Build the attachment's value/fact list from `Knowledge.KnownValueIdsBySeq[parent.Seq]` and `MayMentionBySeq[parent.Seq]`. Filter the ledger view to entries dated before the parent's local date.

### Pitfall 5: Excel subtotals trip V3 "variant" checks
**What goes wrong:** V3 flags any amount within 15% of a canonical amount that is not equal to it. Spreadsheets naturally contain partial sums, line items and running totals near the canonical total.
**How to avoid:** For Excel, run V3 on the *labelled* cells only (a row whose first cell or header matches the canonical label or About), or exempt values the DTO marks as line items. Otherwise expect frequent "Generated - with warnings". Calibrate with a test fixture: canonical $1,250,000; line items $412,500 / $837,500 (no hit); a subtotal of $1,180,000 labelled "Contract value" (hit).

### Pitfall 6: Non-deterministic OOXML bytes
**What goes wrong:** OpenXml writes through System.IO.Packaging/ZipArchive, whose entry timestamps default to "now" [ASSUMED]. Regenerating a doc on retry gives different bytes, and therefore different hashes in any exported load file.
**How to avoid:** Never regenerate a document that already succeeded. Harvest its part from the EML (Pattern 12). A unit test can assert byte-stability of the *EML rewrite*, not of the docx builder.

### Pitfall 7: PowerPoint subtitle "Generated Email Dataset"
Hard-coded in `CreatePowerPointDocument` [VERIFIED: OfficeDocumentService.cs]. It must not reach the corpus.

### Pitfall 8: Word header bar never renders
`OrganizationThemes.ForDomain` does not set `OrganizationName`, and the Word builder only draws the branded header when it is set [VERIFIED]. D-19 branding silently fails without it.

### Pitfall 9: CalendarService is not deterministic or strictly RFC 5545
`UID = Guid.NewGuid()`, `DTSTAMP = DateTime.UtcNow`, `DateTime.ToUniversalTime()` on an Unspecified kind (machine-local interpretation), no 75-octet line folding, `CN=` parameters escaped with `\,` instead of DQUOTE-quoting, and an unsanitized `mailto:` value [VERIFIED: CalendarService.cs]. Add an overload taking `DateTimeOffset` plus a deterministic UID (`{DocId-hash}@{domain}`) and DTSTAMP = parent SentAt (UTC). Fold lines, quote parameter values, strip CR/LF and control characters from every field. Keep the old signature for legacy callers.

### Pitfall 10: Broken inline images in quoted history
**What goes wrong:** A reply quotes the parent fragment containing `<img src="cid:X">`, but the reply EML has no part X.
**How to avoid:** Store a "quote fragment" without inline images on `ComposedEmail`, or replace the image with `[image: {title}]` in quotes (what many clients do).

### Pitfall 11: Media unavailable at generation time although planned
**What goes wrong:** The plan was made with a tested OpenAI key. This session has no tested key (`IsMediaAvailable` is key AND `OpenAIKeyTested`, set in `StepApiConfiguration`), or the key fails with 401/429-quota during the run. `OpenAIMediaClient` returns null only for 400 and timeouts. **401/403/429 throw `ClientResultException`** [VERIFIED: OpenAIMediaClient.cs].
**How to avoid:** Pass `IMediaGenerator? media` (null when unavailable). Null → journal `AttachmentSkipped` with reason "media unavailable" (key Status "Skipped"), retryable by Continue when media becomes available. A media auth or quota exception → mark media as stopped for the rest of the run (remaining media items Skipped with the reason) **without** stopping text generation. Never `SetFatal` the text run for a media-provider failure (Open Question 2).

### Pitfall 12: Image moderation and likeness
Prompts that name characters (including fictional-work names, kept per D-03 in text) or real-looking people invite 400s and likeness problems. Build image prompts in code from the spec + setting: "{scene} at {setting}; documentary photo; no people's faces in focus; **no text, letters, numbers, signage, logos or labels anywhere**". Never pass names. A null result → `AttachmentFailed` (retryable), never a fatal stop.

### Pitfall 13: Start Over leaves non-relevant EMLs behind
`GenerationReset.DeleteAll` only adds planned copy paths for **relevant** threads [VERIFIED]. Journal-listed files cover written threads, but a filler thread killed before its journal line would leave orphans. Extend it to every thread.

### Pitfall 14: Per-run cost uses the Story model for everything
`ThreadUnitOfWork.Cost`, `EstimateRemaining` and `GenerationCostEstimator.Estimate` all price with the Story model [VERIFIED]. Filler on Volume would show 10-40x inflated cost. Media has no catalog price and no tracker entry (`OpenAIMediaClient` is not token-tracked).
**How to avoid:** Use a tier-aware cost function; add media unit prices (Open Question 4) and a `TokenUsageTracker.RecordMediaUsage(operation, cost)`; extend the estimator with Volume pricing for filler and per-unit media.

### Pitfall 15: Same-wave attachment chains
Chain version n may be in the same wave as version n-1, or both may run in parallel. Do not block. Pass the prior text only when its record exists at the time the attachment starts, and otherwise mark the context "first available draft". Never let version numbering depend on completion order: numbering comes from the pure `AttachmentChains`.

## Code Examples

### Deterministic boundary for every nested multipart
```csharp
// Extends MailboxWriter.BoundaryFor (05-10); deterministic across rewrites
private static void SetBoundaries(MimeEntity entity, ComposedEmail email, string path = "0")
{
    if (entity is not Multipart multipart)
        return;
    var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"{email.Planned.DocId}|{email.MessageId}|{path}"));
    multipart.Boundary = "=-rd-" + Convert.ToHexString(hash, 0, 12).ToLowerInvariant();
    for (var i = 0; i < multipart.Count; i++)
        SetBoundaries(multipart[i], email, path + "." + i.ToString(CultureInfo.InvariantCulture));
}
```

### Harvesting succeeded attachment parts
```csharp
// Source: MimeKit MimeMessage.Load / BodyParts (Context7 /jstedfast/mimekit)
public static Dictionary<string, byte[]> Harvest(string emlFullPath)
{
    var found = new Dictionary<string, byte[]>(StringComparer.Ordinal);
    var message = MimeMessage.Load(emlFullPath);
    foreach (var part in message.BodyParts.OfType<MimePart>())
    {
        var docId = part.Headers[MailboxWriter.DocIdHeader];
        if (string.IsNullOrEmpty(docId) || part.Content == null)
            continue;
        using var ms = new MemoryStream();
        part.Content.DecodeTo(ms);
        found[docId] = ms.ToArray();
    }
    return found;
}
```

### Numeric Excel cell
```csharp
// Source: officedev/open-xml-docs working-with-sheets.md (Context7)
var cell = new Cell { CellReference = $"{col}{row}" };
if (TryNumber(text, out var number))          // CanonicalValueScanner.TryParseAmount or invariant decimal parse
{
    cell.DataType = CellValues.Number;
    cell.CellValue = new CellValue(number);
}
else
{
    cell.DataType = CellValues.String;
    cell.CellValue = new CellValue(text);
}
```

### Decoy must-not-mention scan (D-23)
```csharp
public static List<RenderViolation> Scan(RenderedThread rendered, PlannedThread thread)
{
    var result = new List<RenderViolation>();
    foreach (var message in rendered.Messages.Where(m => m != null))
        foreach (var term in thread.MustNotMention ?? new List<string>())
        {
            var hit = CanonicalValueScanner.TryParseAmount(term.Trim(), out _)
                ? CanonicalValueScanner.Scan(message.Body).Any(s => s.Kind == ScannedKind.Amount && CanonicalValueScanner.AmountsEqual(s.Text, term))
                : RenderValidator.ContainsWord(message.Body, term.Trim());
            if (hit)
                result.Add(new RenderViolation { Code = "D1", Seq = message.Seq, Text = $"mentions \"{term.Trim()}\", which this email must not mention" });
        }
    return result;
}
```

## State of the Art

| Old Approach | Current Approach | When Changed | Impact |
|--------------|------------------|--------------|--------|
| Legacy `EmailGenerator`: random 30% chain continuation, Volume model for everything, `Guid` names | Plan-owned specs, pure chains, tiered calls, deterministic names | Phases 4-6 | Legacy code is reference only; removed in Phase 7 |
| DALL-E 3 `response_format`/`style` | gpt-image models always return base64; quality low/medium/high; billed as output tokens per image (about 272 tokens at low 1024²) | 2025-2026 | Low quality is about $0.005-0.011/image depending on model [CITED: pricepertoken.com/gpt-image-pricing, eesel.ai; LOW-MEDIUM] |
| tts-1 | Still available: $15 / 1M characters, 6 classic voices (alloy, echo, fable, onyx, nova, shimmer), 4,096-character input | — | A 60-word voicemail costs about $0.005 [CITED: developers.openai.com/api/docs/models/tts-1, texttolab.com] |

**Deprecated/outdated:** `GenerationConfig.ImageModel` comment notes gpt-image-1/1-mini shut down 2026-12-01 [VERIFIED: code comment]. The configured `gpt-image-2.5-flare` id could not be independently verified this session (LOW). Read it from config; never hard-code it in Phase 6.

## Assumptions Log

| # | Claim | Section | Risk if Wrong |
|---|-------|---------|---------------|
| A1 | OpenXml/ZipArchive entry timestamps make docx bytes non-deterministic | Pitfall 6 | Low: the harvest design works either way |
| A2 | `PackageProperties.Creator/Created/Modified` settable via the OpenXml SDK and not populated with app names by default | Pattern 6 | Low: verify with a unit test |
| A3 | Excel rejects sheet names containing `: \ / ? * [ ]` (corrupt workbook) | Pattern 6 | Medium: a corrupt xlsx in the corpus; sanitize anyway |
| A4 | Voicemail duration ≈ words / 2.5 per second (~150 wpm) | Pattern 9 | Low: cosmetic subject text |
| A5 | text/calendar as a regular attachment is accepted by review tools and Outlook | Pattern 5 | Low |
| A6 | Media unit prices: image ≈ $0.006 (low, 1024²), TTS $15/1M chars | Pitfall 14 | Medium: estimate accuracy only; must be editable catalog values |
| A7 | Cost heuristics for filler/attachment tokens (`GenerationCostEstimator` constants) still hold | Pitfall 14 | Medium: estimate accuracy; recalibrate from the first live run |
| A8 | V8 (gross length) counts as a "structure-only" check acceptable for filler under D-12 | Pitfall 3 | Medium: if not, filler length parity is unenforced |

## Open Questions

1. **Voicemail From address vs plan sender and the caller's Sent Items copy.**
   - What we know: D-28 requires a voicemail-system From. The plan's `FromId` is the caller, `CustodianIds`/`FilePath` include the caller's Sent Items copy, and the key's From column is the caller.
   - What's unclear: whether to keep the caller's Sent copy (unrealistic) or drop it (changes plan structure and the key FilePath).
   - Recommendation: keep every planned copy (D-02), override only the EML From/Subject/body, and add `From` + `Subject` to `AnswerKeyOverlayEntry` so the key shows the voicemail-system sender. Note the realism trade-off for the user.
2. **Media auth/quota failure mid-run:** stop media only (recommended) or the whole run (D-06 wording "Fatal provider stops end the run")? D-06 most plausibly means the text provider. Recommend media-only stop with Skipped rows; confirm with the user.
3. **Is V8 allowed for filler under D-12?** It is needed for length parity (NOISE-05). Recommend yes: it is a gross length check, not a ledger or knowledge check.
4. **Where media unit prices live:** D-29 says "from the model catalog", but `AIModelConfig` has token prices only. Recommend `PricePerUnit` entries (image per image by quality; TTS per 1M characters) editable in `ModelConfigurationDialog`, or constants in `GenerationConfig` next to `ImageModel`. Planner's choice; it touches the model dialog UI.
5. **Cast TTS voice:** CastMember has no voice id or gender. Recommend deterministic `VoiceAssignments.For(personId)` = StableRank over the 6 voices (stable, no LLM), optionally overridable later. Gender mismatch is possible. If unacceptable, one Volume call per cast member at filler start, persisted in the `SideWorld` journal record.
6. **Side-world method:** recommend one Volume call for all organizations (names + 2-3 topics each) at Stage B start. Validate in code against fact titles, canonical values, issue titles and existing project names (case-insensitive whole word). Fall back to code templates ("{Org} {Quarter} facilities refresh"). Persist as a `SideWorld` journal record so Continue reuses it.

## Environment Availability

| Dependency | Required By | Available | Version | Fallback |
|------------|------------|-----------|---------|----------|
| .NET SDK | build/test | ✓ | 10.0.401 (+9.0.103) | — |
| xUnit test project | validation | ✓ | xunit 2.9.3; Evidence suite 538/538 green | — |
| OpenAI API key (tested) | images, voicemail (live) | user-provided | — | Media rows Skipped; unit tests use the `IMediaGenerator` fake / `FakeHttpHandler` |
| Anthropic or OpenAI text key | live smoke run | user-provided | — | `FakeLlmProvider` for all automated tests |

**Missing dependencies with no fallback:** none for automated work. The live smoke run needs the user's keys.

## Validation Architecture

### Test Framework
| Property | Value |
|----------|-------|
| Framework | xUnit 2.9.3, Microsoft.NET.Test.Sdk 17.14.1, net10.0-windows |
| Config file | `ReelDiscovery.Tests/ReelDiscovery.Tests.csproj` (no runsettings); app has `InternalsVisibleTo ReelDiscovery.Tests` |
| Quick run command | `dotnet test ReelDiscovery.Tests/ReelDiscovery.Tests.csproj --filter "FullyQualifiedName~ReelDiscovery.Tests.Evidence&Category!=Render"` (~31 s) |
| Full suite command | `dotnet test ReelDiscovery.Tests/ReelDiscovery.Tests.csproj --filter "Category!=Render"` |
| Render command | `dotnet test ReelDiscovery.Tests/ReelDiscovery.Tests.csproj --filter "Category=Render"` (PNGs under `ReelDiscovery.Tests/bin/Debug/net10.0-windows/renders/`, inspect at 144 DPI) |

Existing fixtures to reuse: `EvidenceTestData.ApprovedCase()`, `RenderResponder(plan, guide)`, `TempCaseFolder()`; `Fakes/FakeLlmProvider` (`Respond` callback answers by prompt content, which suits parallel filler); `Fakes/FakeHttpHandler` (OpenAI media wire tests); `Fakes/StrictSchemaValidator`; `OpenXmlValidator` (as in `DocumentRegressionTests`); `Render/RenderHarness` + `ClippingDetector`.

### Phase Requirements → Test Map
| Req ID | Behavior | Test Type | Automated Command (filter) | File Exists? |
|--------|----------|-----------|----------------------------|-------------|
| ATT-01 | Word sections/headings/table, Excel numeric cells + safe sheet name, PPT subtitle not "Generated Email Dataset", org name header, metadata; OpenXmlValidator clean on new overloads | unit | `FullyQualifiedName~OfficeDocumentServiceTests\|FullyQualifiedName~DocumentRegressionTests` | ❌ W0 (new) / ✅ extend |
| ATT-01 | Per-kind DTO schemas strict, ≤16 unions | unit | `FullyQualifiedName~CaseSchemaTests` | ✅ add types |
| ATT-01 | AttachmentUnitOfWork: render → bytes; refusal/invalid → AttachmentFailed; never throws; email still written | unit | `FullyQualifiedName~AttachmentUnitOfWorkTests` | ❌ W0 |
| ATT-02 | Invite matches own Meeting delta / ledger meeting / fact date; fallback + warning; organizer/attendees from plan | unit | `FullyQualifiedName~InviteSchedulerTests` | ❌ W0 |
| ATT-02 | .ics deterministic UID/DTSTAMP, folding, DQUOTE CN, CRLF stripped, DateTimeOffset correct | unit | `FullyQualifiedName~CalendarServiceTests` | ❌ W0 |
| ATT-03 | Inline image → multipart/related with cid in HTML; quote fragment has no cid; voicemail notice From/Subject/transcript; null media client → Skipped; 401 exception → media stopped, text continues | unit | `FullyQualifiedName~MailboxWriterTests\|FullyQualifiedName~VoicemailComposerTests\|FullyQualifiedName~MediaGatingTests` | ✅ extend / ❌ W0 |
| ATT-04 | CheckAttachment runs V2/V3/V4/V6/V7 against the parent-seq knowledge; Excel subtotal not flagged; future value flagged; 2 retries then Warnings | unit | `FullyQualifiedName~AttachmentValidatorTests` | ❌ W0 |
| ATT-04 | Attachment deltas merged into the ledger at wave end, ids stable, cap respected; replay restores them | unit | `FullyQualifiedName~LedgerMergerTests\|FullyQualifiedName~GenerationRunStateTests` | ✅ extend |
| ATT-04 | Chains: pure grouping, version order by SentAt, suffix in name, prior text passed when done | unit | `FullyQualifiedName~AttachmentChainsTests\|FullyQualifiedName~AttachmentNamingTests` | ❌ W0 |
| NOISE-01/02 | Filler uses Volume tier, side-world in prompt, no ledger/newDetails, structure checks only | unit | `FullyQualifiedName~FillerPromptsTests\|FullyQualifiedName~ThreadUnitOfWorkTests` | ❌ W0 / ✅ extend |
| NOISE-01 | Side-world: names avoid case terms; persisted and reused on Continue; code fallback | unit | `FullyQualifiedName~SideWorldTests` | ❌ W0 |
| NOISE-03 | Bulk variants: newsletter masthead + unsubscribe footer, spam styling, personal plain; all encoded, banner kept | unit | `FullyQualifiedName~BulkMailFormatterTests` | ❌ W0 |
| NOISE-03 | Personal prompt carries the safety line (D-24) | unit | `FullyQualifiedName~FillerPromptsTests` | ❌ W0 |
| NOISE-04 | Decoy Story tier; must-not-mention hit → retry with feedback ×2 → kept + flagged | unit | `FullyQualifiedName~DecoyScannerTests\|FullyQualifiedName~ThreadUnitOfWorkTests` | ❌ W0 |
| NOISE-05 | Parity: band shares equal (±1 message) between relevant and non-relevant business populations; privileged non-relevant get banner/subject mix; no "Document N" names in key after run; same composer path (Message-ID, DocID header, template) | unit | `FullyQualifiedName~DistributionParityTests\|FullyQualifiedName~FillerAssignmentsTests` | ❌ W0 |
| OUT-01 | Non-relevant copies in each custodian mailbox; external sender → inbox-only copies; Start Over removes non-relevant planned paths | unit | `FullyQualifiedName~MailboxWriterTests\|FullyQualifiedName~GenerationResetTests` | ✅ extend |
| RUN-02 | Runner: relevant waves then filler; attachment failure leaves email Written + key row Failed; Continue regenerates only missing attachments and rewrites all copies byte-identically except the new part; Phase-5 journals (no attachment records) resume; old builds skip new record types | integration (fake provider + temp folder) | `FullyQualifiedName~GenerationRunnerTests\|FullyQualifiedName~GenerationJournalTests\|FullyQualifiedName~AnswerKeyOverlayTests` | ✅ extend |
| D-29 | Estimate: Volume for filler, Story for decoys/relevant, attachments by tier, media per unit; ticker includes media | unit | `FullyQualifiedName~GenerationCostEstimatorTests\|FullyQualifiedName~GenerationRunStateTests` | ❌ W0 / ✅ extend |
| D-04 | Generate Corpus stages/counts, Ready card, Case Summary at 144 DPI, no clipping | render | `Category=Render&FullyQualifiedName~GenerateCorpusRenderTests\|...CaseSummaryRenderTests` | ✅ extend |
| Live | One end-to-end run on the Office test case: open docx/xlsx/pptx/ics/png/mp3 from EMLs; spot-read decoys | manual | — | HUMAN-UAT |

### Sampling Rate
- **Per task commit:** quick run command
- **Per wave merge:** full suite; UI waves also run the render command
- **Phase gate:** full + render green, PNGs inspected at 144 DPI, one live smoke run reviewed before `/gsd-verify-work`

### Wave 0 Gaps
- [ ] `ReelDiscovery.Tests/Evidence/AttachmentTestData.cs`: plan fixture with every attachment kind on relevant + non-relevant messages, a two-version chain, an invite root with a Meeting delta, a voicemail single-message thread, a privileged non-relevant thread and a decoy with MustNotMention
- [ ] `Fakes/FakeMediaGenerator.cs`: scripted bytes / null / throw-401 behind `IMediaGenerator`
- [ ] `RenderResponder` extension: answers attachment DTOs and filler DTOs by schema name (`LlmProviderRequest.SchemaName`)
- [ ] New test files listed in the map (❌ rows)

## Security Domain

### Applicable ASVS Categories
| ASVS Category | Applies | Standard Control |
|---------------|---------|-----------------|
| V2 Authentication | no (keys handled in Phase 1 `ApiKeyStore`) | — |
| V3 Session Management | no | — |
| V4 Access Control | no | — |
| V5 Input Validation | yes | Model output is untrusted. HTML-encode everything (ComposeFragment / BulkMailFormatter allowlist); strip control characters from subjects, file names, ICS fields and MIME headers; `FileNameFor` + `IsSafeFileName`; clamp DTO list sizes in code; journal `Normalize()` on replay for new fields |
| V6 Cryptography | no (SHA-256 used only for deterministic ids, not security) | — |
| V12 Files & Resources | yes | Writes only through `ResolveUnderCase` + `CaseStore.WriteAtomicAsync`; harvest reads only safe journal-listed EML paths; cap harvested part size |
| V14 Configuration | yes | No secrets in journal, EML headers or Office metadata |

### Known Threat Patterns
| Pattern | STRIDE | Standard Mitigation |
|---------|--------|---------------------|
| Header/ICS injection via model text (CRLF in title, location, attendee) | Tampering | Strip `\p{Cc}`; DQUOTE params; fold lines; MimeKit header encoding |
| HTML/script injection in newsletter/spam bodies | Tampering | Encode first, allowlist markup, no `<a href>` to real URLs, no `<script>`/`style` from the model |
| Path traversal via attachment file names | Tampering | `FileNameFor` sanitize + `IsSafeFileName`; MIME file name only, never a disk path |
| Oversized model output (40-column × 10K-row sheet, 500 slides) | DoS | Clamp in code (e.g. ≤ 8 columns, ≤ 60 rows, ≤ 12 slides, ≤ 4K words) before building bytes |
| Tampered journal (giant FlattenedText, unsafe Files) | Tampering/DoS | Existing 200 MB cap; `Normalize()` caps new string fields; `IsSafeEmlPath` on Files |
| Real-world brands or links in spam | Spoofing (realism/abuse) | Prompt rule + no link rendering; invented domains from `CreateExternals` |

## Sources

### Primary (HIGH confidence)
- Codebase (read 2026-10-04): `Services/Evidence/{GenerationRunner, ThreadUnitOfWork, ThreadRenderer, ThreadRenderContext, EvidencePrompts, RenderValidator, RenderAssignments, WavePlanner, EmailComposer, MailboxWriter, PeopleDirectory, OrganizationThemes, GenerationJournal, GenerationRunState, GenerationReset, LedgerMerger, LedgerSelector}.cs`, `Models/Corpus/*`, `Models/Evidence/*`, `Services/Corpus/{AnswerKeyBuilder, GenerationCostEstimator, NonRelevantSkeletonBuilder, ThreadSkeletonBuilder, SubjectSeeder, RelevantThreadFiller, PlanCallRunner}.cs`, `Services/{OfficeDocumentService, CalendarService, EmailGenerator}.cs`, `Services/Ai/{OpenAIMediaClient, LlmGateway, JsonSchemaRegistry}.cs`, `Models/{WizardState, AIModelConfig, GenerationConfig, Attachment}.cs`, `Helpers/HtmlEmailFormatter.cs`, test project layout and fakes
- Local test run: Evidence suite 538 passed (31 s)
- Context7 `/jstedfast/mimekit`: BodyBuilder LinkedResources/Attachments, ContentId assignment
- Context7 `/officedev/open-xml-docs`: numeric cells (`CellValues.Number`, `CellValue(100)`)

### Secondary (MEDIUM confidence)
- developers.openai.com/api/docs/models/tts-1: tts-1 model, voices, input limit
- texttolab.com/blog/openai-tts-pricing: $15/1M characters

### Tertiary (LOW confidence)
- pricepertoken.com/gpt-image-pricing, eesel.ai/blog/chatgpt-images-2-5-pricing, aifreeapi.com: per-image low-quality prices ($0.005-0.011)

## Metadata

**Confidence breakdown:**
- Standard stack: HIGH (no new packages; versions read from csproj)
- Architecture/integration points: HIGH (every claim tied to source lines read this session)
- Pitfalls: HIGH for code-derived pitfalls (1, 2, 4, 7, 8, 9, 11, 13, 14), MEDIUM for 5, 6, 10, 12
- Media pricing: LOW-MEDIUM

**Research date:** 2026-10-04
**Valid until:** 2026-11-03 (codebase-bound; re-check media model ids and prices before the live run)
