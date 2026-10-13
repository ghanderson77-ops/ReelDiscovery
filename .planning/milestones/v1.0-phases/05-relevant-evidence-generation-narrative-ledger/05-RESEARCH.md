# Phase 5: Relevant Evidence Generation & Narrative Ledger - Research

**Researched:** 2026-10-03
**Domain:** Plan-driven LLM email rendering (strict JSON schema), point-in-time context assembly, deterministic consistency checks, incremental EML output (MimeKit), crash-safe run journal, WinForms run dashboard
**Confidence:** HIGH for codebase integration, MimeKit behavior and persistence design. MEDIUM for the heuristic consistency checks (names, value conflicts), which need tuning against real model output.

## Summary

Phase 5 turns the approved `corpus-plan.json` into relevant-thread EML files. Almost every building block already exists and is proven: `ILlmGateway` with strict DTOs and outcome classification, `PlanCallRunner`/`PlanCallRun` for slot-limited cancellable fan-out with fatal-stop handling, `PlanNumbering` for DocIDs, paths and custodians, `AnswerKeyBuilder` and the CSV/XLSX writers as a pure projection, `CaseStore.WriteAtomicAsync`, `CaseGuideDocxExporter.WordBuilder`, `CanonicalValueScanner`, `SignatureBuilder`, and the `FakeLlmProvider`/`CaseTestData`/`PlanFixtures` test scaffolding. The new work is a renderer pipeline (context assembly → one strict call per thread that returns messages plus a ledger delta → repair → code checks → retry with feedback → compose → write every custodian copy → append a journal line), plus a wave scheduler, the narrative ledger, the run journal, the answer-key status overlay, the story-so-far export and one new wizard step.

The legacy email helpers are **not** fit for the plan-driven path as they stand. This was verified in code: `EmlFileService` writes HTML only (no `multipart/alternative`), sets `Date` from a `DateTime` (machine-local offset, not the sender's) and writes with a non-atomic `File.Create`. `ThreadingHelper.GenerateMessageId` is random. `FormatQuotedReply` formats with the current culture. `HtmlEmailFormatter` has no table support, turns `_x_` into italics (this breaks file names and addresses), auto-links URLs, renders `**bold**` with stray asterisks (its bold syntax is `*x*`), and finds signatures and quotes by heuristics. Phase 5 should add a **plan-based composer** that hands the formatter structured parts (body, sign-off, signature, quoted history) and a new MimeKit writer, and leave the legacy methods alone for the old flow.

Every random-looking decision (subtlety level, length band, privileged labeling) must stay **stable across retries, Continue and re-projection**. Do not store a seed. Rank messages by a SHA-256 hash of their ID and apportion with `Apportion.Largest`, which gives exact mixes and full determinism. Never use `string.GetHashCode()`, which is randomized per process. Waves, ledger entry IDs (`T-0042-L03`) and Message-IDs (hash of PlanId + DocId at the sender's domain) are pure functions of the plan in the same way. That is why Continue after a crash behaves exactly like an uninterrupted run.

**Primary recommendation:** Build a pure, unit-tested core (`WavePlanner`, `RenderAssignments`, `KnowledgeGate`, `LedgerSelector`/`LedgerMerger`, `RenderRepair`, `RenderValidator`, `EmailComposer`) under `Services/Evidence/`. Add a thin I/O shell (`ThreadRenderer`, `MailboxWriter`, `GenerationJournal`, `GenerationRunner`). The append-only `generation-journal.jsonl` is the source of truth, with each thread's ledger delta and rendered bodies inside its record. The ledger in `case.json`, the answer-key status, the tiles and the story-so-far .docx are all projections of plan + journal. Run the engine off the UI thread (`Task.Run`, `ConfigureAwait(false)` below the UI) and feed the step through a locked snapshot polled by a 250 ms timer.

## Project Constraints (from CLAUDE.md)

Directives the planner must honor (CLAUDE.md content, with corrections the codebase has since made):

- **Platform:** The csproj targets `net10.0-windows` (SDK 10.0.401 pinned in `global.json`) [VERIFIED: ReelDiscovery.csproj, global.json]. CLAUDE.md's ".NET 8" text is stale. Single-file self-contained win-x64 publish.
- **Libraries:** Reuse MimeKit 4.18.1 (EML) and DocumentFormat.OpenXml 3.5.1 [VERIFIED: csproj]. **No new NuGet packages** (UI-SPEC Registry Safety).
- **AI:** All LLM work goes through `ILlmGateway`, works with Claude and OpenAI, uses strict-schema DTOs, and returns result objects (no tuples).
- **Code style:** Mutable classes with auto-properties. No `record` types, no primary constructors, no `#region`. `_camelCase` private fields. `ct` is always the last parameter. `IProgress<T>? progress = null, CancellationToken ct = default`. `[GeneratedRegex]` instead of `new Regex`. `Random.Shared` in new code. Implicit usings are not re-added. File name = type name. Nested private/internal `*Dto` classes with `[JsonPropertyName]` + `[Description]`.
- **Error handling:** Catch `OperationCanceledException` before `Exception`. Bulk generation accumulates errors and never aborts. Bare `catch` is only allowed for non-critical side effects, with a comment. Saving is core work: never swallow save failures.
- **UI:** Built in code (`InitializeUI()`, `TableLayoutPanel` root, row-index comments on every `RowStyle`). Wire handlers after all controls exist. Theme tokens only (no `Color.Red`, no colored RichTextBox log). Verify at 144 DPI (150%) with the render harness (user memory + D-04).
- **Names:** Never substitute or "fictionalize" names from fictional works. Prompts must never say names must be fictional (D-03, user memory).
- **LLM prose is the default path** (user memory). Deterministic text is only a fallback or dev aid.
- **GSD workflow:** Edits only through GSD commands. Docs are committed by the orchestrator.

<user_constraints>
## User Constraints (from CONTEXT.md)

### Locked Decisions

#### Carried forward (locked in Phases 1-4)
- **D-01:** All LLM work goes through `ILlmGateway` with strict JSON-schema DTOs and works with Claude or OpenAI. The Phase 2 strict-schema limits apply (≤16 nullable fields, no recursion, `yyyy-MM-dd` date strings, provider output caps → chunk the work).
- **D-02:** **The plan owns structure.** Every date/time, sender, To/CC, reply/forward kind, fact link, label, hot/privileged flag, DocID and planned file path comes from `corpus-plan.json` (Phase 4 D-13). The renderer writes text only, and the LLM never decides people, dates or labels.
- **D-03:** Names are kept exactly as the guide has them, **including names from fictional works**. Prompts never say that names must be fictional. Content takes a firm position.
- **D-04:** Every new or changed screen is rendered at **144 DPI (150% scaling)** with the render-test harness and checked for clipping before hand-off. Use the Phase 2/3/4 theme and controls (`Theme`, `ThemedButton`, `CardPanel`, `SlimProgressBar`).
- **D-05:** The answer key is a projection of plan + run state. Its `Status` column (Phase 4 D-29) moves from `Planned` to Generated / Generated with warnings / Failed / Skipped as threads finish, and it is regenerable after a partial or cancelled run. The DocID goes into `X-ReelDiscovery-DocID` (D-30).
- **D-06:** Fatal provider stops (Auth / SpendLimit) end the run cleanly and keep completed threads. Refusals are not blindly retried (Phase 1 PROV-04 rules).

#### Narrative ledger
- **D-07:** **The thread-writing call also returns the ledger delta.** The same strict-schema response holds the messages plus a structured list of new details the thread introduced. There is no separate extraction call.
- **D-08:** **A ledger entry is structured:** date (from the source message), kind (commitment / figure / meeting / decision / person / document / other), a one-line statement, the exact value if any, the participant IDs who know it (participants of the source message, extended by later forwards/CCs), related fact and issue IDs, and the source DocID. Entries get stable code-owned IDs.
- **D-09:** **Filtered and capped context:** a later thread's prompt includes only ledger entries dated **before** its first message and **known to at least one of its participants**, ranked by shared fact/issue IDs and then recency, and capped at **about 25-40 entries** (exact cap is Claude's discretion).
- **D-10:** **Export only, no in-app ledger viewer.** The ledger is saved with the case (`case.json` or a sibling file in the case folder, planner's choice for size). It exports as a readable **"story so far" .docx**, opened from Case Summary like the other case documents.

#### Waves, checks & retries
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

#### Email look & voice
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

### Deferred Ideas (OUT OF SCOPE)
- In-app ledger viewer or editor: the user chose export only for v1.
- LLM consistency judge for semantic contradictions: code checks only for now. It could join the Phase 7 QA passes.
- Strictly sequential or dependency-aware scheduling: time-window waves chosen. Revisit if continuity across same-wave threads proves weak.

**Also out of scope (CONTEXT domain):** attachment content and all non-relevant email content (Phase 6). Planned attachments on relevant messages stay `Planned` in the key. Batch API, prompt caching, 5K QA scans and cutover are Phase 7.
</user_constraints>

<phase_requirements>
## Phase Requirements

| ID | Description | Research Support |
|----|-------------|------------------|
| CORP-01 | Point-in-time context: only facts dated on/before the email that its participants could know | `KnowledgeGate` (Pattern 3), fact/value gating rules, per-message "may mention" annotation, future/unknown checks in `RenderValidator` |
| CORP-02 | Canonical values used; output checked against plan (participants, dates, values); violations retry | `RenderValidator` checks V1-V8, `RenderRepair`, retry loop with violation feedback (Pattern 5), `CanonicalValueScanner.AmountsEqual` reuse |
| CORP-03 | Varied subtlety, natural business voice, no legal framing | `RenderAssignments` subtlety ranks (exact 60/30/10, hot = explicit), banned-legal-term check V7, system prompt rules |
| CORP-04 | Hot documents deliberately generated | Plan `IsHot` → subtlety Explicit + "hot" instruction. Key `Hot` column already projected |
| CORP-05 | Privileged attorney-client communications with counsel | Plan `IsPrivileged` → ~85% labeled (code banner/subject tag), counsel signature verbatim, legal-advice instruction |
| CORP-06 | Correct threading (quoting, Message-ID, In-Reply-To, References) | `EmailComposer` deterministic Message-IDs, ancestry chain from `ReplyToSeq`, code-built quoting (invariant en-US formatting); verified MimeKit header output |
| CORP-07 | Per-character voice, length, signature | `VoiceNote` + `SignatureStyle` in prompt, length bands by code, verbatim `Signature` appended by code, sign-off field |
| CORP-08 | Ledger delta extracted after each thread | `LedgerDeltaDto` in the same render response (D-07), `LedgerMerger` (IDs, knownBy extension via quote ancestry) |
| CORP-09 | Later emails get relevant ledger entries | `LedgerSelector` (dated before first message, known to a participant, earlier waves only, ranked, cap 30) |
| CORP-10 | Chronological waves; contradictions flagged and retried | `WavePlanner` (pure), ledger-conflict check V6 |
| CORP-11 | Ledger saved with the case; "story so far" export | `CorpusState.Generation.Ledger` in case.json (materialized from journal), `StorySoFarDocxBuilder` via `WordBuilder` |
| OUT-01 | EML in every participating custodian's mailbox (sent + received) | `MailboxWriter` copies per `CustodianIds`, Sent for `FromId`, Inbox otherwise, folder names via `PlanNumbering` (expose folder map) |
| OUT-02 | `X-ReelDiscovery-DocID` header matches key | `message.Headers.Add("X-ReelDiscovery-DocID", docId)` verified with MimeKit 4.18.1 |
| RUN-01 | Each completed thread saved immediately | Atomic per-copy EML writes, then journal append (flush to disk), key re-projected per wave and on reopen |
| RUN-02 | A failed item is logged and retried/skipped without aborting | `PlanCallRunner` classification, per-thread try/catch, Failed status, Retry Failed Threads |
| RUN-06 | Cancel keeps everything so far | User-token cancel discards in-flight threads only; journal replay; Continue skips written threads |
| RUN-07 | Live progress and cost stay responsive | Engine off the UI thread, locked snapshot + 250 ms timer, per-thread token usage in journal for the case cost tile |
</phase_requirements>

## Architectural Responsibility Map

The tiers below are adapted to a single-process desktop app.

| Capability | Primary Tier | Secondary Tier | Rationale |
|------------|-------------|----------------|-----------|
| Wave scheduling, subtlety/length/privilege assignment | Domain services (pure C#) | — | Deterministic functions of the plan, so resume and re-projection stay identical |
| Point-in-time context, ledger selection | Domain services (pure C#) | — | Knowledge gating must be testable without a provider |
| Prose + ledger delta | LLM via `ILlmGateway` (Story tier) | Domain (repair/validate) | The model writes text only (D-02). Code owns everything structural |
| Consistency checks, retries | Domain services | LLM (re-ask) | D-12: code checks only |
| EML composition (MIME, threading, HTML) | Domain services + MimeKit | `HtmlEmailFormatter` | Headers and quoting are code-built. MimeKit serializes |
| Persistence (EML copies, journal, case.json ledger, key, docx) | File system (atomic writes) | `CaseStore` | Crash safety and path safety live here |
| Run orchestration, cancel, fatal stop | Background engine (thread pool) | `PlanCallRun` | Keeps the UI thread free (RUN-07) |
| Progress display, confirmations, navigation | WinForms UI thread | — | Reads a snapshot only and never runs work |

## Standard Stack

### Core (all already referenced; no additions)
| Library | Version | Purpose | Why Standard |
|---------|---------|---------|--------------|
| MimeKit | 4.18.1 (latest on NuGet) | Build/write EML: `multipart/alternative`, threading headers, custom header | [VERIFIED: nuget.org flatcontainer index lists 4.18.1 as latest; scratch run in this session] |
| DocumentFormat.OpenXml | 3.5.1 | Story-so-far .docx via existing `CaseGuideDocxExporter.WordBuilder`; answer-key xlsx | [VERIFIED: csproj] |
| Anthropic / OpenAI SDKs behind `ILlmGateway` | 12.51.0 / 2.14.0 | Story-tier strict structured output | [VERIFIED: csproj, LlmGateway.cs] |
| Microsoft.Extensions.AI.Abstractions | 10.10.1 | `JsonSchemaRegistry` strict schema generation from DTOs | [VERIFIED: JsonSchemaRegistry.cs] |
| System.Text.Json | in-box | Journal JSONL, case.json, DTOs | [VERIFIED: codebase] |
| xUnit | 2.9.3 (+ runner 3.1.4, Test.Sdk 17.14.1) | Tests | [VERIFIED: ReelDiscovery.Tests.csproj] |

### Reused in-repo components (do not rewrite)
| Component | Use in Phase 5 |
|-----------|----------------|
| `PlanCallRun` / `PlanCallRunner.RunAsync<T>` | Slot-limited parallel calls, fatal stop (`SetFatal` cancels the linked token), timeout vs user-cancel discrimination, refusal capture |
| `CorpusPlanStore.EnsureLoaded`, `CorpusApproval.IsGuideStale` | Entry gating (approved, current plan) |
| `PlanNumbering` | DocIDs/paths already assigned. **Expose** the private `BuildFolders(guide)` as `internal static IReadOnlyDictionary<string,string> CustodianFolders(CaseGuide)` so Inbox copy paths use the same folder names as the planned sender paths |
| `AnswerKeyBuilder`, `AnswerKeyCsvWriter`, `AnswerKeyXlsxWriter`, `CorpusApproval.WriteFilesAsync` pattern, `FileLockDetector` | Re-projection with a status/subject overlay; locked-file reporting |
| `CanonicalValueScanner` (`Scan`, `AmountsEqual`, `IsRegistered`) | Amount/identifier extraction and equality for value checks |
| `SignatureBuilder` / `CastMember.Signature` | Verbatim signature (already built by code in Phase 3) |
| `CaseGuidePrompts.AppendNameRules`, `CorpusPlanPrompts.AppendCast` | Name rules (keep-as-written) and roster lines |
| `CaseGuideDocxExporter.WordBuilder` (internal), `StripInvalidXmlChars` | Story-so-far document |
| `Apportion.Largest` | Exact subtlety / length / labeling mixes |
| `BusinessCalendar.ResolveId` | Sender-local time for quoted headers |
| `CaseStore.WriteAtomicAsync` | Every EML copy, ledger-bearing case.json, key files, docx |
| `RenderHarness`, `ClippingDetector`, `FakeLlmProvider.Respond`, `PlanFixtures.ValidPlan`, `CorpusTestData.RichDraft` | Tests |

### Alternatives Considered
| Instead of | Could Use | Tradeoff |
|------------|-----------|----------|
| Ledger in case.json | Sibling `narrative-ledger.json` | D-10 allows either. Success criterion 2 says "saved in case.json", and the size is modest (≈1,500 entries × ~400 B ≈ 0.6 MB at 5K emails, against a 50 MB case.json cap). **Use case.json.** The journal stays the crash-recovery source |
| Journal-embedded rendered bodies | Re-parse EMLs in Phase 6 with `MimeMessage.Load` | Bodies in the journal (~1.5 MB at 5K) make Phase 6 attachment rewrites and Phase 7 QA trivial and avoid HTML round-tripping. **Store them** |
| `System.Threading.Channels` work queue (research ARCHITECTURE) | Per-wave `Task.WhenAll` over `PlanCallRunner` | Waves are barriers by design (D-11), so a channel adds nothing. Reuse the proven Phase 4 pattern |
| LLM-generated org themes (`ThemeGenerator`) | Deterministic 8-palette by domain hash | UI-SPEC S5 locks the deterministic palette: no cost, stable across runs |

**Installation:** none. No new packages.

## Architecture Patterns

### System Architecture Diagram

```
 [StepGenerateCorpus: Generate / Continue / Retry Failed]
            │ Task.Run (off UI thread)
            ▼
 ┌─────────────────────── GenerationRunner ────────────────────────┐
 │ load plan + guide ─► replay generation-journal.jsonl ─► RunState │
 │   (written / warnings / failed per thread, ledger, bodies)       │
 │ WavePlanner(plan) ─► waves[] (pure; same on every Continue)      │
 │ for each wave with unwritten threads:                            │
 │   ledgerView = entries from EARLIER waves only                   │
 │   parallel (PlanCallRun slots=3) per thread:                     │
 │   ┌──────────────── ThreadUnitOfWork ─────────────────┐          │
 │   │ KnowledgeGate + LedgerSelector + RenderAssignments│          │
 │   │   └─► EvidencePrompts.BuildThreadPrompt           │          │
 │   │ attempt 1..3:                                     │          │
 │   │   ThreadRenderer ─► ILlmGateway (Story, strict)   │──► provider
 │   │      ├ fatal (Auth/SpendLimit) ─► run.SetFatal ───┼─► stop all
 │   │      ├ refused / timeout / failed ─► FAILED rec   │          │
 │   │      └ DTO ─► RenderRepair ─► RenderValidator     │          │
 │   │            violations? ─► retry w/ feedback       │          │
 │   │ best attempt ─► EmailComposer (plain+HTML, IDs,   │          │
 │   │    quoting, signature, privileged marker)         │          │
 │   │ ─► MailboxWriter: atomic .eml per custodian copy  │          │
 │   │ ─► GenerationJournal.Append(ThreadDone{status,    │          │
 │   │      files, violations, ledgerDelta, bodies, usage})│        │
 │   └───────────────────────────────────────────────────┘          │
 │   wave end: LedgerMerger(deltas) ─► snapshot ─► persist callback │
 │             (case.json ledger + answer-key re-projection)        │
 │ run end/cancel/fatal: final merge, key CSV/XLSX, Story So Far    │
 └──────────────┬───────────────────────────────────────────────────┘
                │ lock-protected GenerationSnapshot (counters, events)
                ▼
 [250 ms WinForms Timer] ─► tiles, waves grid, activity grid, bar, status
```

### Recommended Project Structure
```
Models/Evidence/
├── GenerationState.cs        # CorpusState.Generation: RunPlanId, Ledger, story file stamp, last outcome
├── LedgerEntry.cs            # D-08 entry + LedgerKind enum
├── NarrativeLedger.cs        # entries + Normalize()
├── JournalRecord.cs          # RunStarted / ThreadDone / ThreadFailed / RunEnded (one class + RecordType enum)
├── RenderAssignment.cs       # per-message Subtlety, LengthBand; per-thread PrivilegeMarking
└── GenerationWave.cs
Services/Evidence/
├── WavePlanner.cs            # pure
├── RenderAssignments.cs      # pure, stable-hash ranking
├── KnowledgeGate.cs          # pure
├── LedgerSelector.cs         # pure (filter/rank/cap)
├── LedgerMerger.cs           # pure (IDs, knownBy extension, dedupe)
├── EvidencePrompts.cs        # const SystemPrompt + BuildThreadPrompt
├── ThreadRenderer.cs         # gateway call + nested DTOs + SchemaTypes
├── RenderRepair.cs           # pure post-processing
├── RenderValidator.cs        # pure D-12 checks -> List<RenderViolation>
├── EmailComposer.cs          # pure: ComposedEmail (headers, plain, html)
├── OrganizationThemes.cs     # deterministic palette per domain
├── MailboxWriter.cs          # MimeKit + atomic writes + path guard
├── GenerationJournal.cs      # append (flushed) + replay (torn-line tolerant)
├── GenerationRunState.cs     # replay result + answer-key status overlay
├── ThreadUnitOfWork.cs
├── GenerationRunner.cs       # waves, parallelism, cancel, fatal, snapshot
├── GenerationSnapshot.cs     # locked counters + bounded event queue
└── StorySoFarDocxBuilder.cs
UserControls/StepGenerateCorpus.cs + StepGenerateCorpus.Run.cs + GenerationUiText.cs
ReelDiscovery.Tests/Evidence/*Tests.cs  (+ Render/GenerateCorpusRenderTests.cs)
```
Namespace `ReelDiscovery.Services.Evidence` keeps the quick test filter clean. The existing `ReelDiscovery.Tests.Generation` holds legacy `EmailGenerator` tests, so do not reuse it.

### Pattern 1: Deterministic waves (pure)
**What:** Sort relevant threads by first-message UTC (tie: thread ID). Open a wave at the first thread and close it when the next thread starts ≥ 7 days after the wave start. If the wave has fewer than 3 threads, extend it to at most 14 days. Split any wave above 12 threads at the largest date gap. Waves are computed from the **whole plan**, never from the remaining threads, so Continue sees identical waves and identical ledger visibility. [ASSUMED sizing within D-11's "one to two weeks"]
**Ledger visibility:** thread in wave *k* sees entries whose source thread is in a wave < *k* **and** whose date < the thread's first message (D-09). On Continue, entries from already-written threads of the *same* wave exist in the journal but stay hidden by the wave-index filter. Behavior therefore matches an uninterrupted run.

### Pattern 2: Stable assignments without a seed
```csharp
// Source: pattern derived from Apportion.Largest (Helpers/Apportion.cs) + SHA-256 (BCL)
internal static ulong StableRank(string id)
{
    // Never string.GetHashCode(): randomized per process, would change on every Continue
    var hash = SHA256.HashData(Encoding.UTF8.GetBytes(id));
    return BinaryPrimitives.ReadUInt64BigEndian(hash);
}

// Subtlety (D-17): hot messages are Explicit; the rest of the Own-basis messages are ranked by hash, then cut exactly
var pool = ownMessages.Where(m => !m.IsHot).OrderBy(m => StableRank(m.Id)).ToList();
var target = ownMessages.Count;                       // mix measured over all Own messages
var hot = ownMessages.Count(m => m.IsHot);
var shares = Apportion.Largest(target, new[] { 0.60, 0.30, 0.10 });   // Indirect, Clear, Explicit
var explicitLeft = Math.Max(0, shares[2] - hot);       // hot already fills Explicit
// assign pool[0..] to Indirect, then Clear, then explicitLeft Explicit; overflow (hot > 10%) shrinks Indirect first
```
The same pattern applies to **length bands** (D-21) and **privilege marking** (thread-level: ~85% labeled; ~40% of the labeled threads also tag the subject: UI-SPEC S5). Quoted-basis messages ("carries the facts: no") get no subtlety level. They are reactions. Privileged threads get the level `Counsel` (legal-advice content) and are excluded from the 60/30/10 pool. [ASSUMED: privileged handling; D-17 is silent]

Suggested length bands (shared constant table so Phase 6 can match, NOISE-05) [ASSUMED]:

| Band | Words (body only) | Relevant share | Rule |
|------|-------------------|----------------|------|
| Short | 8-60 | ~55% | default for Reply/ReplyAll |
| Medium | 60-160 | ~35% | |
| Long | 160-320 | ~10% | only `MessageKind.New` or Forward-with-comment, never quoted-only replies |

### Pattern 3: Knowledge gate (CORP-01)
For a thread with participants P (From ∪ To ∪ Cc of all messages) and messages m₁..mₙ:
- **Facts in prompt:** facts with `Date ≤ date(mₙ)` and `KnownByIds ∩ P ≠ ∅`, plus the thread's own `FactIds`. Each fact line shows its date and "known to: names". Each message line then lists **"may mention: F-xxx…"** = facts dated ≤ date(mᵢ) whose `KnownByIds` contain mᵢ's sender, plus mᵢ's own `FactIds`.
- **Fallbacks** [ASSUMED]: empty `KnownByIds` → use `ActorIds`. Both empty → public. Undated fact → not date-gated (KnownBy still applies).
- **Value knowledge date:** a canonical value with `FactIds` becomes knowable at min(date of its facts) to the union of those facts' `KnownByIds`. A value with no `FactIds` (project name, address) is public.
- **Sender knowledge set for message mᵢ** = gated facts' values ∪ ledger entries (dated before the thread, in context, `KnownByIds ∋ sender`) ∪ details introduced by earlier messages in this thread that the sender received or sent (via `ReplyToSeq` ancestry).
- **Never** put the complaint text, issue `RelevanceCriteria` or the review protocol into render prompts (research Pitfall 2). Fact descriptions are allowed, with an instruction to express them as in-world behavior.

### Pattern 4: One strict call per thread returns messages + ledger delta (D-07)
```csharp
// Source: existing DTO conventions (RelevantThreadFiller nested DTOs); strict-safe: no nullables, no recursion
internal class ThreadRenderDto
{
    [JsonPropertyName("messages")] [Description("One entry per message in the request, by seq, in order")]
    public List<RenderedMessageDto> Messages { get; set; } = new();
    [JsonPropertyName("newDetails")] [Description("New specific details this thread introduces that later emails may refer back to; do not repeat details from the earlier-details list")]
    public List<LedgerDeltaDto> NewDetails { get; set; } = new();
}
internal class RenderedMessageDto
{
    [JsonPropertyName("seq")] public int Seq { get; set; }
    [JsonPropertyName("body")] [Description("Only the new text this sender writes: no greeting rules beyond the voice, no sign-off, no signature, no quoted earlier messages")]
    public string Body { get; set; } = string.Empty;
    [JsonPropertyName("signOff")] [Description("Closing word or phrase before the signature, e.g. 'Thanks,' or empty")]
    public string SignOff { get; set; } = string.Empty;
    [JsonPropertyName("peopleNamed")] [Description("Full or first names of every person this message mentions")]
    public List<string> PeopleNamed { get; set; } = new();
}
internal class LedgerDeltaDto
{
    [JsonPropertyName("seq")] public int Seq { get; set; }                  // source message
    [JsonPropertyName("kind")] public LedgerKind Kind { get; set; }        // enum OK: FactSignificance/ValueKind already used in strict DTOs
    [JsonPropertyName("about")] [Description("Short name of what the detail is about, e.g. 'retainage on pay application 7'")]
    public string About { get; set; } = string.Empty;
    [JsonPropertyName("statement")] public string Statement { get; set; } = string.Empty;
    [JsonPropertyName("value")] [Description("Exact amount, date (yyyy-MM-dd), number or name, or empty")]
    public string Value { get; set; } = string.Empty;
    [JsonPropertyName("factIds")] public List<string> FactIds { get; set; } = new();
}
```
Enums in strict DTOs already work (`FactSignificance`, `ValueKind` in `CaseGuideGenerator` DTOs) [VERIFIED: codebase]. Add `ThreadRenderer.SchemaTypes` to `CaseSchemaTests` (StrictSchemaValidator) [VERIFIED: CaseSchemaTests pattern]. **Sizing:** `MaxThreadDepth = 12` [VERIFIED: ThreadShapes.cs], so the worst case is ≈12 × ~450 tokens + delta ≈ 6-7K tokens. Use `MaxOutputTokens = 12_000`. The gateway doubles once to the Anthropic non-streaming ceiling of 21,333 on truncation [VERIFIED: LlmGateway, AnthropicTextProvider]. No per-thread chunking is needed. Issue IDs on entries are derived by code from `FactIds`, so the model does not have to return them.

### Pattern 5: Unit of work with check-retry (D-12..D-15)
```csharp
// Shape only; reuse PlanCallRunner for slot/fatal/timeout/refusal classification
for (var attempt = 1; attempt <= 3; attempt++)
{
    var outcome = await PlanCallRunner.RunAsync<ThreadRenderDto>(_llm, BuildRequest(ctx, feedback), run).ConfigureAwait(false);
    if (run.IsFatal) return ThreadResult.NotRun();                 // left pending; Continue writes it later
    if (outcome.Value == null)                                     // refused / timeout / transport / invalid after gateway retries
        return ThreadResult.Failed(outcome.Refused ? $"The model declined: {outcome.RefusalMessage}" : outcome.Error); // no blind re-ask (D-06)
    var rendered = RenderRepair.Apply(outcome.Value, ctx);         // strip ids, quotes, signatures, extra markup
    var violations = RenderValidator.Check(rendered, ctx);
    Keep(best, rendered, violations);                              // fewest violations wins; tie -> latest
    if (violations.Count == 0) break;
    feedback = violations;                                         // plain-language list into the next prompt (D-13)
}
var email = EmailComposer.Compose(ctx, best);                      // pure
var files = await _mailbox.WriteAsync(email, ct).ConfigureAwait(false); // every custodian copy, atomic
await _journal.AppendAsync(JournalRecord.ThreadDone(ctx, best, files), ct).ConfigureAwait(false);
```
Retries do **not** re-send the previous body. They resend the same prompt plus a "Problems in the previous attempt: …; rewrite the whole thread" block, which keeps the request bounded. A user cancel throws out of `PlanCallRunner` and discards in-flight threads. They were never journaled, so they stay pending (UI-SPEC C-G1 copy).

### Pattern 6: Composition (CORP-06, D-16, D-18, D-20)
- **Message-ID (deterministic):** `{hex(SHA256(PlanId:DocId))[..24]}@{senderDomain}`. The sender domain is the cast member's org `Domain` or the external's `Domain`, from the email address host when blank. All copies of one message share it (real duplicates share Message-ID, which is what de-duplication relies on). A rewrite gets the same ID, so overwriting a crashed partial thread is idempotent.
- **In-Reply-To / References:** follow `ReplyToSeq` ancestry, not "previous message". References = root…parent chain. Root messages have neither.
- **Subject:** root = thread subject (privileged-tagged when assigned); reply/forward = `ThreadingHelper.AddReplyPrefix/AddForwardPrefix` on the root subject (never cumulative). Use the **same pure function** in the answer-key overlay so the key's Subject matches the EML.
- **Plain text:** `[privileged banner line]\n\nbody\n\nsignOff\n{signature}` then the quoted history built by code: `On {ddd, MMM d, yyyy} at {h:mm tt}, {Name} <{addr}> wrote:` + `> ` lines of the parent's full plain text, which nests naturally. Forwards use the forward block. **Format dates with `CultureInfo.GetCultureInfo("en-US")`**. The existing helper uses the current culture.
- **HTML:** add a structured overload, e.g. `HtmlEmailFormatter.ComposeHtml(string body, string signOff, string signature, QuotedPart? quoted, OrganizationTheme theme, bool privilegedBanner)`. It renders `**bold**` (≤2 runs), `- ` bullets (≤6, one list), one pipe table (≤4×6, HTML-encoded cells), and **no** italics, autolinks or raw HTML. It reuses the existing template/`WrapInTemplate` (make it `internal`). Keep the legacy `ConvertToHtml` unchanged for the old flow.
- **Short-reply signatures:** when the band is Short and `SignatureStyle` is `NameOnly`/`MobileFooter`, use the first name (or the mobile footer). Otherwise use the full `Signature` verbatim [VERIFIED: SignatureBuilder styles].

### Pattern 7: Journal as source of truth
- File: `{case}/generation-journal.jsonl`. Path = case folder + constant name only (Phase 4 T-04-26 rule).
- Records (one compact JSON object per line; enum `Type`): `RunStarted {PlanId, Utc}`, `ThreadDone {ThreadId, Status: Written|Warnings, Attempts, Violations[], Files[] (relative), Messages[] {DocId, MessageId, Subject, BodyPlain, SignOff}, LedgerDelta[], InputTokens, OutputTokens, CostUsd}`, `ThreadFailed {ThreadId, Reason, Kind: Refused|Timeout|Provider|Write}`, `RunEnded {Outcome: Finished|Cancelled|Fatal, Message}`.
- Append: serialize the whole line first, then one write of line + `\n` under a `SemaphoreSlim(1)`, through a `FileStream(FileMode.Append, FileAccess.Write, FileShare.Read)` with `Flush(flushToDisk: true)`. Open per append (≈ hundreds of appends per run, cheap and robust).
- Replay: size cap (e.g. 200 MB). Skip blank/unparseable lines (a torn final line after a kill). Ignore unknown `Type`. Drop records whose ThreadId is not in the plan. Reject file paths that fail `CorpusPlan.IsSafeEmlPath`. Last record per thread wins. A different `PlanId` in `RunStarted` → "Plan changed" state.
- **Ledger** = `LedgerMerger` over the replayed `ThreadDone` deltas in wave order. It is materialized into `case.json` (`CorpusState.Generation.Ledger`) at each wave end, at run end, and on entry when the journal is newer.
- **Answer key status overlay** (D-05) by DocID: Written → `Generated`, Warnings → `Generated - with warnings`, Failed → `Failed`, else `Planned`. Attachments stay `Planned` (CONTEXT). Skipped is reserved; nothing in Phase 5 skips. Update the XLSX Legend's Status definition text, which currently says "Planned until the document is generated" [VERIFIED: AnswerKeyXlsxWriter.ColumnDefinitions].

### Pattern 8: Responsive UI (RUN-07)
Start the run with `_activeRun = Task.Run(() => runner.RunAsync(input, snapshot, persist, cts.Token))`. Below the UI everything uses `ConfigureAwait(false)` (`CorpusPlanStore` already does) [VERIFIED: codebase]. Workers write only into `GenerationSnapshot` (lock + counters + a bounded queue of activity events). The step's 250 ms `System.Windows.Forms.Timer` drains it. Wave-end persistence that touches `CaseDraft` is marshalled to the UI thread through a `BeginInvoke` + `TaskCompletionSource` callback, so `CaseDraft` is only mutated on the UI thread and `CaseStore.SaveAsync` keeps its existing coalescing. The shell's cost ticker already polls `TokenUsageTracker` (thread-safe via lock) [VERIFIED: TokenUsageTracker]. The **case** cost tile sums `CostUsd` from journal records, so it survives restarts. That needs per-call usage: add `InputTokens`/`OutputTokens` (summed across attempts) to `LlmResult` in `LlmGateway.BuildResult`, a small additive change.

Mirror `StepCaseGuide.CancelAndWaitForRun` for C-G3 and add `StepGenerateCorpus { IsGenerating: true }` to `WizardForm.IsRunActive/StopActiveRun` [VERIFIED: WizardForm.cs].

### Anti-Patterns to Avoid
- **Re-quoting by the model:** the model writes only new text. Code strips `>` lines and `On … wrote:` blocks in `RenderRepair`.
- **Model-written signatures or banners:** strip trailing lines equal to the sender's name or signature. Banners and subject tags come from code.
- **Mutating `CaseDraft` from worker threads,** or calling `CaseStore.SaveAsync` concurrently from workers.
- **Wave membership from the remaining threads:** this breaks ledger-visibility equivalence on Continue.
- **Random Message-IDs or `Random` assignments:** retries and Continue would produce different corpora and answer-key subjects.
- **Writing EMLs with `File.Create` or `EmlFileService.SaveEmailAsEmlAsync`:** non-atomic, HTML-only, wrong `Date` offset (`new DateTimeOffset(DateTime)` uses the machine's zone) [VERIFIED: EmlFileService.cs].
- **Sending the complaint, issue relevance criteria or protocol into render prompts:** leaks legal framing and future knowledge.
- **Using `HtmlEmailFormatter.ConvertToHtml` on model text:** `_word_` → italics, URLs → links, `**x**` → `*<strong>x</strong>*` [VERIFIED: HtmlEmailFormatter regexes].

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| MIME/RFC 5322 serialization, header folding/encoding | String-built EML | MimeKit `MimeMessage` + `BodyBuilder{TextBody,HtmlBody}` | Verified: produces `multipart/alternative`, `Date` with the sender offset, `<…>` IDs, encoded names |
| Parallel bounded calls + fatal stop + timeout vs cancel | New semaphore/catch ladder | `PlanCallRun` / `PlanCallRunner` | Already handles every edge case found in Phases 1-4 (WR-xx fixes) |
| Atomic file writes | `File.WriteAllBytes` | `CaseStore.WriteAtomicAsync` | temp + WriteThrough + move with retry for OneDrive/AV locks |
| Amount parsing/equality | New regex | `CanonicalValueScanner.Scan` / `AmountsEqual` | Handles `$7.5 million` == `$7,500,000`, timeouts |
| Exact proportional mixes | Random sampling | `Apportion.Largest` over hash-ranked items | Exact and deterministic |
| Answer-key serialization, CSV injection guard | New writers | `AnswerKeyCsvWriter` / `AnswerKeyXlsxWriter` | OWASP guard, OpenXml validity already tested |
| .docx building | Raw OpenXml | `CaseGuideDocxExporter.WordBuilder` | Styles, headings, invalid-XML stripping |
| Custodian folder names | Recompute from email | `PlanNumbering` folder map (expose it) | Must equal the planned sender paths exactly |
| Strict schema | Hand-written JSON schema | `JsonSchemaRegistry` from DTOs | Provider-portable, byte-stable |

**Key insight:** this phase's risk is in consistency and crash semantics, not in libraries. Every new "smart" step should be a pure function with a unit test. The only I/O is gateway calls, atomic file writes and journal appends.

## Runtime State Inventory

Not a rename/refactor phase, but the phase adds persistent state that later actions (re-plan, Start Over, reopen) must handle:

| Category | Items | Action Required |
|----------|-------|-----------------|
| Stored data | New: `generation-journal.jsonl`, `Mailboxes/**.eml`, ledger in `case.json`, `{folder} - Story So Far.docx`; answer-key files are rewritten | Start Over deletes journal-listed EMLs (safe-path checked), the journal, the ledger and the docx, then re-projects the key (all Planned) |
| Live service config | None (no external service state) | None |
| OS-registered state | None | None |
| Secrets/env vars | None new; API keys via existing DPAPI store | None |
| Build artifacts | None | None |
| Case format | `CaseStage.Generation` is inserted (stored by name, safe [VERIFIED: CaseStage doc comment]); `CorpusState.Generation` is added (needs `Normalize()`); `CorpusFileKind.StorySoFar` is added | Bump `CaseDraft.CurrentFormatVersion` to 3 only if older builds must refuse the file. Recommended, matching the T-04-03 precedent |

## Common Pitfalls

### Pitfall 1: Future knowledge leaks through values, not facts
**What goes wrong:** An email dated before the change order mentions its amount because the full value registry was in the prompt.
**Why:** `CorpusPlanPrompts.AppendValues` lists **every** value [VERIFIED].
**How to avoid:** List only knowable values (Pattern 3) and check V4 (future/unknown value) in code.
**Warning signs:** Any value appearing in an email earlier than the min date of its facts.

### Pitfall 2: Answer-key Subject ≠ EML Subject on privileged threads
**What goes wrong:** The subject tag is added at render time, but the key still projects `thread.Subject`.
**How to avoid:** One pure `EmailComposer.SubjectFor(thread, message, marking)` used by both the composer and the overlay.

### Pitfall 3: Continue produces a different corpus
**What goes wrong:** Random assignment or wave recomputation over the remaining threads changes subtlety mixes, ledger visibility and Message-IDs after a crash.
**How to avoid:** Patterns 1, 2 and 6 (hash-ranked, whole-plan waves, deterministic IDs). Test: a run killed after wave 2 and continued yields identical assignments and wave boundaries.

### Pitfall 4: Orphan EMLs after a kill between file writes and the journal append
**What goes wrong:** Copies exist on disk but the journal has no record, so the key says Planned while files exist.
**How to avoid:** Continue re-renders that thread and overwrites the same deterministic paths. Start Over deletes journal-listed files plus, defensively, planned paths of relevant messages that exist under `Mailboxes`. Document this rather than adding a two-phase commit.

### Pitfall 5: False-positive checks burn retries
**What goes wrong:** The name check flags "Hi team", month names, product names or nicknames. The value check flags unrelated small amounts. Retries cost money and threads end with "with warnings" for nothing.
**How to avoid:** Keep the checks narrow (see the Check table below). Unit-test each check against **realistic negative samples**. Repairs (strip IDs, quotes, signatures, extra markup) are not violations.
**Warning signs:** The Warnings tile above ~10% of threads on a real run means the heuristics need tuning before Phase 7 scale.

### Pitfall 6: Culture-dependent text in EMLs
**What goes wrong:** `FormatQuotedReply` uses `{SentDate:ddd, MMM d}` with the current culture [VERIFIED], so a non-US Windows produces localized day/month names in English emails.
**How to avoid:** Format with en-US explicitly. Keep the `Date` header from the plan's `DateTimeOffset`.

### Pitfall 7: UI freezes during long runs
**What goes wrong:** Awaiting the engine from a UI handler without `Task.Run` runs regex checks, HTML building and EML serialization on the UI thread between awaits.
**How to avoid:** Pattern 8. Render test with fake-provider delays, plus a manual responsiveness check.

### Pitfall 8: Locked answer-key file aborts end-of-run work
**What goes wrong:** The user has `answer-key.xlsx` open in Excel, the write throws, and the story-so-far or the save is skipped.
**How to avoid:** Write each projection independently. Report locks with `FileLockDetector` and the UI-SPEC "Answer key locked" copy. Retry on leaving the step.

### Pitfall 9: Disk volume from the embedded disclaimer banner
**What goes wrong:** `WrapInTemplate` inlines a 45 KB PNG as base64 (≈61 KB) in every HTML part [VERIFIED: Resources file size]. With ~2-3 custodian copies per email, a 5K corpus is several hundred MB.
**How to avoid:** Phase 5 volumes are small (relevant only). Flag it for Phase 7. See Open Question 2.

### Pitfall 10: Refusals on criminal-conspiracy scenarios
**What goes wrong:** Hot/explicit threads in "Breaking Bad"-style cases are refused.
**How to avoid:** The system prompt frames the work as a fictional training dataset and keeps operational detail abstract (research Pitfall 7). A refused thread is recorded `Failed` (no blind re-ask, D-06) and retried only through Retry Failed Threads.

## Code Checks (D-12 + discretion)

| # | Check | Detection (deterministic) | Violation text (UI-SPEC style) |
|---|-------|---------------------------|--------------------------------|
| V1 | Message count/order | DTO seqs == plan seqs, each exactly once, non-empty body | `has 3 messages; the plan has 4` |
| V2 | Unknown people | (a) each `peopleNamed` must resolve to a cast/external first, last or full name; (b) body bigram `First Last` where `First` is a cast first name and the pair is not a cast/external full name; (c) `Mr./Ms./Mrs./Dr. X` where X is not a cast/external last name; (d) greeting name (`Hi X,`) must be a recipient's first name or a group word | `names "{name}", who is not in the cast` |
| V3 | Canonical value variants | `CanonicalValueScanner.Scan` amounts within ±15% of a canonical Amount but not `AmountsEqual`; identifiers with a canonical prefix (`CO-`, `INV-`…) not registered | `uses ${value} for {label}; the case value is ${canonical}` |
| V4 | Future / unknown knowledge | Canonical value or ledger value text in message *i* outside the sender's knowledge set (Pattern 3); any `F-/CH-/ISS-/V-/T-` ID token is a repair, not a violation | `mentions {fact title}, dated after this email` / `uses {value}, which {sender} does not know yet` |
| V5 | Plan structure | Body must not contain quoted history or a signature (repaired first; reported only if repair would empty the body) | `message {n} is empty after removing quoted text` |
| V6 | Ledger contradictions | New delta with the same `Kind` and a normalized `About` matching a prior visible entry but a different normalized value (amounts via `AmountsEqual`, dates via `yyyy-MM-dd`); body amounts within ±15% of a known ledger figure but unequal | `gives {about} as {new}; earlier emails say {old}` |
| V7 | Legal framing (non-privileged) | Word-boundary scan for `plaintiff`, `defendant`, `allegedly`, `lawsuit`, `cause of action`, `Count [IVX]+`, the core document title; moralizing/disclaimer sentences (`this is fictional`, `illegal`) | `uses legal wording ("{term}")` |
| V8 | Gross length miss | Body word count > 2× band max or (Long band) < ½ band min | `message {n} is {w} words; target is {band}` |

[ASSUMED: thresholds (±15%, 2×), term list and V8; tune against real output in the first live run]

## Code Examples

### EML with all required headers (verified in this session with MimeKit 4.18.1)
```csharp
// Output verified: "Date: Mon, 04 Mar 2024 09:42:00 -0500", "Message-Id: <abc.123@dundermifflin.com>",
// "References: <root.0@…> <parent.1@…>", "X-ReelDiscovery-DocID: DM-000001", "Content-Type: multipart/alternative"
var m = new MimeMessage();
m.MessageId = messageId;                       // bare or <bracketed>; MimeKit stores it bare either way
if (inReplyTo != null) m.InReplyTo = inReplyTo;
foreach (var r in references) m.References.Add(r);
m.Date = planned.SentAt;                       // DateTimeOffset from the plan: sender offset preserved
m.From.Add(new MailboxAddress(fromName, fromEmail));
m.Subject = subject;
m.Headers.Add("X-ReelDiscovery-DocID", planned.DocId);
m.Body = new BodyBuilder { TextBody = plain, HtmlBody = html }.ToMessageBody(); // multipart/alternative, text first
using var ms = new MemoryStream();
m.WriteTo(ms);
await CaseStore.WriteAtomicAsync(Path.Combine(caseFolder, relPath.Replace('/', '\\')), ms.ToArray(), ct);
```

### Copy paths for every custodian (OUT-01)
```csharp
// Sender copy = plan FilePath; other copies: same custodian folder map PlanNumbering uses
foreach (var id in planned.CustodianIds)
{
    var box = id == planned.FromId ? PlanNumbering.SentFolder : PlanNumbering.InboxFolder;
    var rel = $"{PlanNumbering.MailboxesFolder}/{folders[id]}/{box}/{planned.DocId}.eml";
    if (!CorpusPlan.IsSafeEmlPath(rel)) continue;   // never write outside Mailboxes (T-04 path rules)
    // resolve under the case folder and verify Path.GetFullPath(...).StartsWith(fullCaseFolder + '\\')
}
```

## State of the Art

| Old Approach | Current Approach | When Changed | Impact |
|--------------|------------------|--------------|--------|
| `EmailGenerator` writes everything at the end; random Message-IDs; HTML-only EML | Per-thread unit of work, journal, deterministic IDs, multipart/alternative | This phase | Crash safety, reproducible Continue |
| Rolling free-text "story so far" in prompts | Structured, filtered, capped ledger (D-08/D-09) | Project decision | Bounded context, no hallucinated recap |
| Single-pass JSON-retry on parse failure | Outcome taxonomy + check-retry with violation feedback | Phases 1 + 5 | Refusals are not retried, contradictions are |

**Deprecated/outdated for this flow:** `EmlFileService.SaveEmailAsEmlAsync`, `ThreadingHelper.SetupThreading/GenerateMessageId` (random, linear chain), `HtmlEmailFormatter.ConvertToHtml` on model text. Leave them for the legacy flow until Phase 7 cutover (PLAT-05).

## Assumptions Log

| # | Claim | Section | Risk if Wrong |
|---|-------|---------|---------------|
| A1 | Wave sizing 7 days, extended to ≤14 when fewer than 3 threads, split above 12 threads | Pattern 1 | Continuity across same-wave threads weaker or runs slower; constants only |
| A2 | Parallelism 3 per wave (matches `RelevantThreadFiller.MaxParallelCalls`) | Pattern 5 | Throughput; constant |
| A3 | Ledger cap 30, ranked by shared FactIds, then shared IssueIds, then recency | CORP-09 | Context size/continuity; constant |
| A4 | Length bands and shares (55/35/10) | Pattern 2 | Style distribution; Phase 6 must reuse the same table |
| A5 | Privileged threads excluded from the 60/30/10 pool (level "Counsel") | Pattern 2 | Mix on privileged threads differs from the user's intent |
| A6 | KnownBy fallbacks (ActorIds; both empty = public; undated = not date-gated) | Pattern 3 | Over- or under-gating |
| A7 | Check thresholds (±15% variant window, 2× length, term list) | Code Checks | False positives/negatives; tune on the first live run |
| A8 | Ledger stored in case.json (not a sibling) | Alternatives | Literal success criterion 2; D-10 allows either |
| A9 | Rendered bodies stored in journal records for Phase 6/7 reuse | Pattern 7 | Journal size (~1.5 MB at 5K), acceptable |
| A10 | `LlmResult` gains token totals for per-thread cost | Pattern 8 | Small gateway change; alternative is per-operation-name tracker queries |
| A11 | Format version bump to 3 | Runtime State | Older builds silently dropping generation state if not bumped |
| A12 | Disclaimer banner kept in every HTML part | Pitfall 9 | Disk size at Phase 7 scale |

## Open Questions (RESOLVED)

1. **Should Phase 5 render planned attachments' references in prose before Phase 6 exists?**
   - What we know: attachments stay `Planned` and are not in the EML. Bodies should still say "see attached {title}" so Phase 6 can add the file without re-rendering.
   - Recommendation: include attachment titles in the prompt and journal the bodies (A9). Phase 6 recomposes the EML with attachments from journal data and the same Message-IDs.
   - **RESOLVED:** adopted in 05-08 (message lines list `attachments: "{Title}"`) and 05-12 (bodies journaled per message).
2. **Keep the embedded QuikData disclaimer banner in every generated email?**
   - What we know: it is existing product behavior (≈61 KB per HTML part).
   - Recommendation: keep it for Phase 5 (consistency with the legacy output). Raise it with the user before Phase 7 scale runs.
   - **RESOLVED (for Phase 5):** the banner is kept via the reused `WrapInTemplate` (05-05). Deferred to Phase 7 for user review: banner size at 5K scale.
3. **Budget guard for generation runs?**
   - What we know: research Pitfall 15 suggests a hard cap; CONTEXT does not ask for one. The Ready card shows an estimate.
   - Recommendation: no cap in Phase 5 (Phase 7 cost scope). The Cancel button and the live cost tile cover it.
   - **RESOLVED (for Phase 5):** no budget cap; the Ready card estimate, the live Cost tile, Cancel and the fatal spend-limit stop cover it (05-06, 05-13, 05-15). Deferred to Phase 7 for user review: a hard budget guard.

## Environment Availability

| Dependency | Required By | Available | Version | Fallback |
|------------|------------|-----------|---------|----------|
| .NET SDK | build/test | ✓ | 10.0.401 (pinned) + 9.0.103 | — |
| MimeKit package (NuGet cache) | EML writer | ✓ | 4.18.1 (restored and run in this session) | — |
| LLM API key (Claude or OpenAI) | live smoke run only | user-provided | — | All automated tests use `FakeLlmProvider` |
| Outlook / mail client | human check of EML rendering | user machine | — | Open EML in Windows Mail/Thunderbird |

**Missing dependencies with no fallback:** none.

## Validation Architecture

### Test Framework
| Property | Value |
|----------|-------|
| Framework | xUnit 2.9.3, Microsoft.NET.Test.Sdk 17.14.1 (net10.0-windows) |
| Config file | `ReelDiscovery.Tests/ReelDiscovery.Tests.csproj` (no runsettings) |
| Quick run command | `dotnet test ReelDiscovery.Tests/ReelDiscovery.Tests.csproj --filter "FullyQualifiedName~ReelDiscovery.Tests.Evidence&Category!=Render"` |
| Full suite command | `dotnet test ReelDiscovery.Tests/ReelDiscovery.Tests.csproj --filter "Category!=Render"` |
| Render command | `dotnet test ReelDiscovery.Tests/ReelDiscovery.Tests.csproj --filter "Category=Render"` (PNGs + `.clip.txt` under `ReelDiscovery.Tests/bin/Debug/net10.0-windows/renders/`, inspect at 144 DPI) |

### Phase Requirements → Test Map
| Req ID | Behavior | Test Type | Automated Command (filter) | File Exists? |
|--------|----------|-----------|----------------------------|-------------|
| CORP-01 | Prompt for a thread contains no fact/value dated after its last message or unknown to all participants; per-message "may mention" excludes facts the sender does not know | unit | `FullyQualifiedName~KnowledgeGateTests` | ❌ Wave 0 |
| CORP-01/02 | Validator flags a future/unknown value; passes known ones | unit | `FullyQualifiedName~RenderValidatorTests` | ❌ Wave 0 |
| CORP-02 | Canonical variant ($184,520 vs $184,250) flagged; `$184,250.00` accepted; retry prompt contains violation text; 3rd failure keeps best with Warnings | unit + integration (fake provider) | `FullyQualifiedName~RenderValidatorTests\|FullyQualifiedName~ThreadUnitOfWorkTests` | ❌ Wave 0 |
| CORP-03/04 | Subtlety mix exact 60/30/10 over Own non-privileged messages; every hot message Explicit; stable across calls/processes | unit | `FullyQualifiedName~RenderAssignmentsTests` | ❌ Wave 0 |
| CORP-03 | Legal-term scan flags non-privileged, ignores privileged | unit | `FullyQualifiedName~RenderValidatorTests` | ❌ Wave 0 |
| CORP-05 | ~85% of privileged threads labeled (banner first line), ~40% of those subject-tagged; counsel signature verbatim | unit | `FullyQualifiedName~EmailComposerTests` | ❌ Wave 0 |
| CORP-06 | Parsed EML (`MimeMessage.Load`): Message-ID domain = sender org, In-Reply-To = parent per `ReplyToSeq`, References chain, RE:/FW: not doubled, quoted header en-US, Date offset = plan | unit | `FullyQualifiedName~EmailComposerTests\|FullyQualifiedName~MailboxWriterTests` | ❌ Wave 0 |
| CORP-07 | Signature appended verbatim; Short + NameOnly uses first name; length bands distribution | unit | `FullyQualifiedName~EmailComposerTests\|FullyQualifiedName~RenderAssignmentsTests` | ❌ Wave 0 |
| CORP-08 | Delta → entries with IDs `T-xxxx-Lnn`, date from source message, knownBy = participants ∪ later quoting-chain participants, issue IDs from facts, duplicates dropped | unit | `FullyQualifiedName~LedgerMergerTests` | ❌ Wave 0 |
| CORP-09 | Selector: only earlier-wave entries dated before first message, known to a participant, ranked, capped | unit | `FullyQualifiedName~LedgerSelectorTests` | ❌ Wave 0 |
| CORP-10 | Waves deterministic, chronological, whole-plan based; ledger contradiction flagged | unit | `FullyQualifiedName~WavePlannerTests\|FullyQualifiedName~RenderValidatorTests` | ❌ Wave 0 |
| CORP-11 | Ledger round-trips in case.json (Normalize repairs nulls); story-so-far docx grouped by month, OpenXmlValidator 0 errors | unit | `FullyQualifiedName~StorySoFarDocxTests\|FullyQualifiedName~GenerationStateTests` | ❌ Wave 0 |
| OUT-01 | One copy per `CustodianIds` member, Sent for sender, Inbox for others, folders = PlanNumbering map, no path outside Mailboxes | unit (temp folder) | `FullyQualifiedName~MailboxWriterTests` | ❌ Wave 0 |
| OUT-02 | Every EML copy's `X-ReelDiscovery-DocID` equals plan DocId and key row | integration | `FullyQualifiedName~GenerationRunnerTests` | ❌ Wave 0 |
| RUN-01/06 | Cancel after N threads: N threads' files + journal records exist, key rows Generated, in-flight thread absent; Continue writes only the rest | integration (FakeLlmProvider.OnSend cancel) | `FullyQualifiedName~GenerationRunnerTests` | ❌ Wave 0 |
| RUN-01 | Journal replay tolerates torn last line, unknown types, foreign PlanId, unsafe paths | unit | `FullyQualifiedName~GenerationJournalTests` | ❌ Wave 0 |
| RUN-02 | Provider failure / refusal on one thread → Failed record, others written; fatal → remaining pending, completed kept | integration | `FullyQualifiedName~GenerationRunnerTests` | ❌ Wave 0 |
| RUN-07 | Snapshot updated from worker threads without UI access; runner completes under a blocked UI sync context (`Task.Run` path) | unit + manual | `FullyQualifiedName~GenerationSnapshotTests` + manual responsiveness check | ❌ Wave 0 |
| D-05 | Answer-key overlay statuses + rendered subjects; attachments stay Planned | unit | `FullyQualifiedName~AnswerKeyOverlayTests` | ❌ Wave 0 |
| D-01 | `ThreadRenderer.SchemaTypes` strict | unit | `FullyQualifiedName~CaseSchemaTests` | ✅ (add types) |
| UI | GenerationUiText plurals (0/1/2/1,000), entry decision table | unit | `FullyQualifiedName~GenerationUiTextTests` | ❌ Wave 0 |
| UI | 8 render states of UI-SPEC at Large + WizardMin, no clipping, no grid h-scroll | render | `Category=Render&FullyQualifiedName~GenerateCorpusRenderTests` | ❌ Wave 0 |

Manual-only: reading quality of real relevant emails (voice variety, subtlety, natural tone) on a live provider run of the Dunder Mifflin test case; opening EMLs in a mail client (HTML theme, quoting, banner); UI responsiveness during a live run; 144-DPI human inspection.

### Sampling Rate
- **Per task commit:** quick run command
- **Per wave merge:** full suite. UI waves also run the render command
- **Phase gate:** full + render suites green, PNGs inspected at 144 DPI, one live smoke run reviewed before `/gsd-verify-work`

### Wave 0 Gaps
- [ ] `ReelDiscovery.Tests/Evidence/EvidenceTestData.cs`: approved-plan fixture (from `PlanFixtures.ValidPlan` + `CorpusTestData.RichDraft`) with hot, privileged, forward, external-sender and multi-custodian threads; a `FakeLlmProvider.Respond` builder that answers `ThreadRenderDto` JSON by thread ID parsed from the prompt (plus injectable violations)
- [ ] Test files listed in the map above (all new)
- [ ] `ReelDiscovery.Tests/Render/GenerateCorpusRenderTests.cs` with a snapshot-driven state seeding hook on the step (no live run needed)

## Security Domain

### Applicable ASVS Categories (Level 1)
| ASVS Category | Applies | Standard Control |
|---------------|---------|-----------------|
| V2 Authentication | no | (provider keys unchanged, DPAPI store) |
| V3 Session Management | no | — |
| V4 Access Control | no | single-user desktop |
| V5 Input Validation | yes | Treat journal/case.json as untrusted (size caps, `Normalize`, safe-path checks); treat LLM output as untrusted (map by seq, drop unknown, strip markup, HTML-encode) |
| V6 Cryptography | minimal | SHA-256 only for stable IDs/ranking (BCL), not security |
| V12 Files/Resources | yes | All write/delete paths = case folder + `Mailboxes/...` validated by `IsSafeEmlPath` + full-path prefix check; Start Over deletes only validated journal-listed paths |
| V14 Configuration | n/a | — |

### Known Threat Patterns for this stack
| Pattern | STRIDE | Standard Mitigation |
|---------|--------|---------------------|
| Path traversal via tampered journal/plan path | Tampering | `CorpusPlan.IsSafeEmlPath`, `GetFullPath` prefix check, constant file names |
| HTML/script injection from model text into EML HTML | Tampering | Encode all model text (`WebUtility.HtmlEncode`), allowlisted markup only, no links/raw HTML |
| Header injection via subject/names | Tampering | MimeKit encodes headers; strip CR/LF/control chars from model subject text before use |
| Oversized/corrupt journal DoS on open | DoS | Size cap, line-by-line parse, skip bad lines, never throw to UI |
| Shell-open of arbitrary file from activity row | Elevation | Path built from verified case folder + plan `FilePath` only, rejected unless under the case folder (Phase 3 rule) |
| CSV formula injection in key | Tampering | Existing `AnswerKeyCsvWriter.Field` guard (subjects are now model text) |
| Prompt injection via guide text | Tampering | Low impact (local, user-authored); structure is code-owned so injected text cannot change labels/paths |

## Sources

### Primary (HIGH confidence)
- Codebase (read in this session): `Models/Corpus/*`, `Models/Case/*`, `Services/Ai/LlmGateway.cs`, `JsonSchemaRegistry.cs`, `Services/Corpus/PlanCallRunner.cs`, `RelevantThreadFiller.cs`, `CorpusPlanPrompts.cs`, `AnswerKeyBuilder.cs`, `CorpusPlanStore.cs`, `PlanNumbering.cs`, `CorpusApproval.cs`, `AnswerKeyXlsxWriter.cs`, `GenerationCostEstimator.cs`, `ThreadShapes.cs`, `Helpers/HtmlEmailFormatter.cs`, `ThreadingHelper.cs`, `Services/EmlFileService.cs`, `Services/Case/CanonicalValueScanner.cs`, `SignatureBuilder.cs`, `CaseStore.cs`, `Forms/WizardForm.cs`, `UserControls/StepReviewPlan.Run.cs`, `StepCaseGuide.Run.cs`, tests (`FakeLlmProvider`, fixtures, `CaseSchemaTests`, 04-VALIDATION commands)
- MimeKit 4.18.1, scratch console run in this session: `multipart/alternative`, `Date` offset, Message-ID bracket handling, `References`, custom header output
- nuget.org flat container index for MimeKit (4.18.1 latest)
- Context7 `/jstedfast/mimekit`: BodyBuilder TextBody/HtmlBody usage, `Headers.Add`
- `.planning/phases/05-*/05-CONTEXT.md`, `05-UI-SPEC.md`, `.planning/REQUIREMENTS.md`, `ROADMAP.md`, `STATE.md`, Phase 4 CONTEXT/RESEARCH/VALIDATION

### Secondary (MEDIUM confidence)
- `.planning/research/ARCHITECTURE.md` (ThreadUnitOfWork, journal, ContextAssembler) and `PITFALLS.md` (1, 2, 5, 6, 7, 9, 10, 14, 15, 19); project-internal research, adapted

### Tertiary (LOW confidence)
- Heuristic check thresholds and length/wave constants (Assumptions A1-A7): reasoning only, to be calibrated on the first live run

## Metadata

**Confidence breakdown:**
- Standard stack: HIGH. Everything is in the repo; MimeKit behavior was verified by execution.
- Architecture: HIGH. Direct reuse of Phase 4 patterns; determinism design is straightforward.
- Pitfalls: HIGH for the code defects found (verified in source); MEDIUM for check false-positive rates.

**Research date:** 2026-10-03
**Valid until:** 2026-11-02 (stable; in-repo dependencies)
