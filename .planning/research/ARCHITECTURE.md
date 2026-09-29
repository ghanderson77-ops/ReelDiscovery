# Architecture Research

**Domain:** Long-horizon, consistency-preserving LLM corpus generation (case-guide-driven eDiscovery email corpus) inside a .NET 8 WinForms app
**Researched:** 2026-09-29
**Confidence:** MEDIUM-HIGH (provider/SDK/batch facts verified against official docs; pipeline pattern synthesized from long-form-generation research plus the existing codebase. It is a recommended design, not an established off-the-shelf pattern.)

---

## TL;DR (the opinionated version)

1. **Structured case guide is the single source of truth.** Core document → Issues → Cast → dated Fact timeline → Canonical entity registry. It is stored as JSON and generated once. Every later LLM call gets a *projection* of it, never "the conversation so far".
2. **Plan everything, then render.** A `CorpusPlanner` produces a **manifest**: every thread and message, with its date, participants, fact refs, relevance label, custodian, and attachment specs. It also **pins every load-bearing specific** (amounts, doc names, meeting times, invoice numbers) *before* any prose is written. The answer key is a projection of the manifest, correct by construction.
3. **Rendering is embarrassingly parallel because of (2).** Each thread render is independent. It gets a bounded `ContextPack` (the case synopsis prefix, the cast subset, the facts known to those participants *as of* the thread date, and the thread plan). Independent items can go to sync calls or to provider Batch APIs without changing the renderer.
4. **One unit of work = one thread.** Render text → enrich attachments (each failure isolated) → write EML → append to journal → release memory. This fixes the data-loss, memory, and phase-barrier problems in one move.
5. **Our own provider-neutral `LlmRequest` sits above `IChatClient`.** `Microsoft.Extensions.AI.IChatClient` handles sync calls for both OpenAI and Claude, since both official SDKs ship adapters. `IChatClient` has no batch concept, so requests are built as serializable `LlmRequest` records. Sync mode translates them to `IChatClient`. Batch mode translates them to each provider's native batch format.
6. **Strangler, not a refactor, for `EmailGenerator`.** Extract the reusable leaves: signature correction, the DTO→`EmailMessage` mapping, the document/image/voicemail/calendar generators, and HTML image insertion. Replace the orchestration. Delete the old flow when the new one ships, because the old flow is out of scope.

---

## Standard Architecture

### System Overview

```
┌────────────────────────────────────────────────────────────────────────────────────┐
│ PRESENTATION  (WinForms wizard, IWizardStep - thin; no business logic)            │
│  StepProvider → StepScenario → StepCoreDocument → StepCaseGuide(cast/facts/issues) │
│  → StepAncillaryDocs(opt) → StepCorpusConfig → StepPlanReview → StepGeneration     │
│  → StepComplete                                                                     │
└───────────────┬────────────────────────────────────────────────────────────────────┘
                │ binds to CaseSession (replaces WizardState); calls application services
                ▼
┌────────────────────────────────────────────────────────────────────────────────────┐
│ APPLICATION / PIPELINE                                                              │
│ ┌───────────────────┐  ┌─────────────────────┐  ┌──────────────────────────────┐   │
│ │ CaseGuideBuilder  │  │ CorpusPlanner       │  │ RunEngine (scheduler)        │   │
│ │ - CoreDocGen      │→ │ - BudgetAllocator   │→ │ - WorkItem queue (Channel)   │   │
│ │ - IssueExtractor  │  │ - RelevantPlanner   │  │ - SyncExecutor (Semaphore)   │   │
│ │ - CastExtractor   │  │ - FillerPlanner     │  │ - BatchExecutor (poll)       │   │
│ │ - FactExtractor   │  │ - DecoyPlanner      │  │ - ThreadUnitOfWork           │   │
│ │ - EntityRegistry  │  │ - ManifestValidator │  │   render→enrich→EML→journal  │   │
│ │ - GuideValidator  │  └─────────────────────┘  └──────────────┬───────────────┘   │
│ └───────────────────┘                                          │                   │
│ ┌───────────────────┐  ┌─────────────────────┐  ┌──────────────▼───────────────┐   │
│ │ AncillaryDocGen   │  │ ContextAssembler    │← │ ThreadRenderer               │   │
│ │ (depo/RFP/RFA/Rog)│  │ (knowledge-gated    │  │ + RenderValidator (determ.)  │   │
│ └───────────────────┘  │  projection of guide│  │ Enrichers: Doc/Image/Voice/  │   │
│                        │  + thread plan)     │  │ Calendar (existing, extracted)│  │
│                        └─────────────────────┘  └──────────────────────────────┘   │
└───────────────┬───────────────────────────────────────────────┬────────────────────┘
                │ LlmRequest (provider-neutral, serializable)    │ bytes / messages
                ▼                                                ▼
┌──────────────────────────────────────┐  ┌──────────────────────────────────────────┐
│ AI GATEWAY                            │  │ ARTIFACT BUILDERS (existing, reused)     │
│ ILlmGateway (sync) → IChatClient      │  │ ThreadingHelper, HtmlEmailFormatter,     │
│   ├ OpenAI  (MEAI.OpenAI adapter)     │  │ OfficeDocumentService, CalendarService,  │
│   └ Claude  (Anthropic SDK adapter)   │  │ EmlFileService (MimeKit)                 │
│   middleware: usage/cost, retry, log  │  │ + AnswerKeyWriter (CSV/XLSX), GuideExport│
│ IBatchGateway (OpenAI Batch /         │  └──────────────────────────────────────────┘
│   Anthropic Message Batches)          │
│ IMediaGateway (OpenAI image + TTS)    │
└──────────────────────────────────────┘
                │
                ▼
┌────────────────────────────────────────────────────────────────────────────────────┐
│ PERSISTENCE  RunStore = one folder per case/run                                     │
│  case-guide.json | manifest.json (immutable plan) | journal.jsonl (append-only      │
│  status) | batches.json | rendered/{threadId}.json | output/**/*.eml |              │
│  answer-key.csv/.xlsx | case-guide/*.docx | run.log                                 │
└────────────────────────────────────────────────────────────────────────────────────┘
```

**Data flow direction is strictly left to right and top to bottom.** The case guide feeds the planner, the manifest feeds the engine, and the engine writes artifacts plus the journal. Nothing downstream mutates the case guide or the manifest during a run. The engine only appends to the journal.

### Component Responsibilities

| Component | Responsibility | Talks to | Implementation |
|-----------|----------------|----------|----------------|
| **CaseSession** | In-memory handle for the current case: `CaseGuide`, `CorpusConfig`, `Manifest?`, `RunStore`, provider selection, `UsageTracker`. Replaces `WizardState`. Has **no** service factory methods, which removes the Models→Services back-reference. | Steps (read/write), RunStore (save on every step leave) | POCO + `SaveAsync()` |
| **CoreDocumentGenerator** | Scenario + complexity preset → Complaint / Petition / Investigation Memo (long markdown text with numbered paragraphs and counts) | ILlmGateway | One or a few long sync calls. Sectioned generation for Complex (outline first, then each section). |
| **IssueExtractor / CastExtractor / FactExtractor** | Core doc → structured `Issue[]`, `CastMember[]`, `Fact[]` with stable IDs (`I1`, `C07`, `F023`). Each fact cites the core-doc paragraph(s) it came from. | ILlmGateway (structured output) | Separate calls, each fed the core doc plus the prior extraction's output (issues → cast → facts), so references resolve |
| **EntityRegistry builder** | Pins canonical values: org names, domains, project names, amounts, contract/invoice/PO numbers, document titles, and key dates. Facts reference entities by ID. | ILlmGateway + deterministic normalization | Structured output. The user can edit it in StepCaseGuide. |
| **CaseGuideValidator** | Deterministic checks. Every fact's participants exist, and each participant's active date range covers the fact date. Every issue has at least one fact. Dates fall inside the case span. Emails and domains are unique. No orphan entities. | none (pure) | Pure C#, unit-tested. Blocks Next with a readable report. |
| **AncillaryDocGenerator** | Depositions, RFPs, RFAs, and interrogatories grounded in the guide | ILlmGateway, OfficeDocumentService | Independent of the corpus pipeline and can be built in parallel |
| **CorpusPlanner** | Guide + config → `Manifest`. Allocates the budget (relevant vs. non-relevant, per issue/fact, per custodian). Plans relevant threads per fact or fact cluster. Plans filler (mostly deterministically) and decoys (LLM, with a forbidden-facts list). | ILlmGateway (relevant + decoy planning), pure code (allocation, filler) | Two tiers: deterministic skeleton, then LLM fills thread plans in bounded chunks (per issue-arc or per month) |
| **ManifestValidator** | Deterministic. No participant is outside their active range. Message dates are monotonic within a thread. Every fact marked "must be evidenced" has at least one thread. Entity refs resolve. Relevance labels are consistent with fact refs. Attachment specs are well-formed. Counts match the budget. | none | Pure C#, unit-tested |
| **ContextAssembler** | Builds a bounded `ContextPack` per render item. Contents: the stable prefix (system prompt, case synopsis, org/entity registry), the cast subset, **knowledge-gated facts** (facts dated ≤ thread date that the participants knew about), the thread plan, and prior-chunk messages for long threads. | CaseGuide, Manifest, rendered store | Pure C#, which makes context size predictable and testable |
| **ThreadRenderer** | `ContextPack` → `LlmRequest` → parsed `RenderedThread` (message bodies only; no quoting, headers, or signatures) | ILlmGateway or IBatchGateway via RunEngine | Structured output DTO tied to a JSON schema |
| **RenderValidator** | Deterministic post-checks on rendered text. Message count matches the plan. Canonical amounts and IDs appear verbatim when the plan requires them. No "future leak" (mentions of entities or facts introduced after the message date). No wrong-name sign-offs. On failure, retry up to N times synchronously with the violation list appended. | Entity registry, manifest | Pure C# (regex/entity matching) |
| **Enrichers** (DocumentEnricher, ImageEnricher, VoicemailEnricher, CalendarEnricher) | Realize the manifest's attachment specs. Document chains (versions) come from the manifest, not from runtime mutable state. | ILlmGateway, IMediaGateway, OfficeDocumentService, CalendarService | Extracted from `EmailGenerator.GeneratePlanned*Async`. Every enricher catches its own failures and returns `EnrichmentResult`. |
| **ThreadUnitOfWork** | For one thread: render (or accept a batch result) → validate → map to `EmailMessage` (threading, HTML, signatures) → enrich → write EML → append a journal record → drop byte arrays | Renderer, Enrichers, ThreadingHelper, HtmlEmailFormatter, EmlFileService, RunStore | The only place a thread goes from plan to disk |
| **RunEngine** | Owns the work queue, concurrency, sync/batch routing, cancellation, progress snapshots, and failure isolation | Executors, RunStore, IProgress | `System.Threading.Channels` + `SemaphoreSlim`; `ConfigureAwait(false)` everywhere below the UI |
| **SyncExecutor** | Runs `LlmRequest`s through `ILlmGateway` with bounded parallelism and retry | ILlmGateway | Relevant threads, retries, and long multi-chunk threads |
| **BatchExecutor** | Packs `LlmRequest`s into provider batches with `custom_id = workItemId`, persists batch IDs, polls, maps results back by `custom_id`, and requeues errored/expired items to sync | IBatchGateway, RunStore | Filler, decoys, and optionally single-chunk relevant threads |
| **ILlmGateway** | Provider-neutral sync LLM call: `LlmRequest` → `LlmResponse<T>` with usage. Wraps `IChatClient`. | IChatClient (OpenAI / Anthropic adapters) | Middleware: usage/cost tracking, retry with jitter + Retry-After, file logging |
| **IBatchGateway** | `Submit(IEnumerable<LlmRequest>) → batchId`, `Poll`, `GetResults → (customId, LlmResponse|error)` | OpenAI Files+Batches, Anthropic `Messages.Batches` | Two implementations with provider-specific translators |
| **IMediaGateway** | Image + TTS. OpenAI-only today. | OpenAI SDK | Claude has no image/TTS API, so media needs an OpenAI key or gets disabled (see Pitfalls) |
| **RunStore** | Atomic file I/O for the run folder: guide, manifest, journal, batches, rendered cache, outputs | File system | JSON via System.Text.Json; temp-file + `File.Move(overwrite)` for atomic writes; append-only JSONL journal |
| **AnswerKeyWriter** | Manifest + journal (final file paths, status) → CSV + XLSX | RunStore, OpenXml | Pure projection. Also runs on a partial run. |
| **CaseGuideExporter** | Core doc / cast / facts → .docx | OfficeDocumentService | Reuses the existing OpenXml builder |

---

## Core Data Model (the "source of truth")

These records are the contract between every component. Build them first; everything else depends on their shape.

```csharp
// Case guide: generated once, user-edited, then frozen for the run
public sealed record CaseGuide(
    CaseProfile Profile,                 // scenario, preset, doc type, case span (start/end), jurisdiction flavor
    CoreDocument CoreDocument,           // markdown text + numbered paragraphs + counts
    IReadOnlyList<Organization> Orgs,    // Id, Name, Domain, Role (plaintiff/defendant/third party), Theme
    IReadOnlyList<Issue> Issues,         // Id "I1", Title, AllegationText, CoreDocParagraphRefs
    IReadOnlyList<CastMember> Cast,      // Id "C07", Name, Title, OrgId, Email, ActiveFrom/To, Involvement,
                                         // IsCustodian, IsKeyWitness, VoiceId, WritingStyle
    IReadOnlyList<Fact> Facts,           // see below
    IReadOnlyList<Entity> Entities);     // canonical registry: Id "E12", Kind (Amount/DocTitle/Invoice/PO/
                                         // Project/Meeting...), CanonicalValue, IntroducedOn (date)

public sealed record Fact(
    string Id,                           // "F023"
    DateOnly Date, DateOnly? EndDate,    // point or interval (validity interval, cf. FactTrack)
    string Summary,                      // what happened
    IReadOnlyList<string> IssueIds,      // many-to-many: this is how storylines overlap
    IReadOnlyList<string> ActorIds,      // who did it
    IReadOnlyList<string> AwareIds,      // who knew at the time (knowledge gating)
    IReadOnlyList<string> EntityIds,     // pinned specifics this fact introduces/uses
    string EvidenceHint,                 // "email from PM to GC admitting schedule slip", "revised invoice v2"
    FactImportance Importance,           // Key / Supporting / Background
    IReadOnlyList<string> CoreDocRefs);  // traceability to paragraphs
```

```csharp
// Manifest: the plan; immutable once the run starts
public sealed record Manifest(string RunId, string CaseGuideHash, CorpusConfig Config,
                              IReadOnlyList<ThreadPlan> Threads, IReadOnlyList<DocChainPlan> DocChains);

public sealed record ThreadPlan(
    string Id,                           // "T0412"  (also the batch custom_id root; ^[A-Za-z0-9_-]{1,64}$)
    RelevanceLabel Label,                // Relevant / NotRelevant / Decoy (reported as NotRelevant, flagged)
    NonRelevantCategory? Category,       // DailyBusiness / Ancillary / Newsletter / Spam / Personal / Decoy
    IReadOnlyList<string> IssueIds, IReadOnlyList<string> FactIds,
    string Subject, string Purpose,      // one-paragraph intent ("PM downplays leak reports to owner")
    IReadOnlyList<string> MustConvey,    // bullet assertions the thread MUST contain
    IReadOnlyList<string> MustNotMention,// forbidden facts/entities (decoys, pre-fact threads)
    IReadOnlyList<string> EntityIds,     // pinned specifics allowed/required
    IReadOnlyList<MessagePlan> Messages);

public sealed record MessagePlan(
    string Id,                           // "T0412-M03"
    int Seq, int? ReplyToSeq,            // thread-relative: fixes the replyToIndex bug class by construction
    MessageKind Kind,                    // New / Reply / ReplyAll / Forward
    string FromId, IReadOnlyList<string> ToIds, IReadOnlyList<string> CcIds,
    DateTimeOffset SentAt,               // decided in planning, not by the renderer
    string Intent,                       // one line
    IReadOnlyList<AttachmentSpec> Attachments);  // DocChainId+Version / Image / Voicemail / Calendar
```

```csharp
// Journal: append-only execution status (one JSON object per line)
public sealed record JournalEntry(DateTimeOffset At, string ItemId, ItemKind Kind, ItemStatus Status,
                                  string? BatchId, string? OutputPath, string? Error, TokenUsage? Usage);
```

Key properties of this model:
- **Stable string IDs everywhere.** They are used as prompt references ("F023"), batch `custom_id`s, journal keys, and answer-key columns.
- **Facts are many-to-many with issues.** Overlapping storylines are just facts whose `IssueIds` differ but whose `ActorIds` overlap. No separate "storyline" object owns the people.
- **`AwareIds` + date = knowledge gating.** The renderer only sees facts a participant could know about at `SentAt`. This is the single biggest lever against contradiction and "future leak".
- **The planner decides dates and reply structure.** The renderer never picks `SentAt` or `ReplyTo`. That removes the culture-dependent date parsing, the out-of-order date bugs, and the replyToIndex bug.

---

## Recommended Project Structure

Stay single-project (single-file publish constraint) but add a test project beside it. Introduce folders by pipeline stage, not by technical type:

```
ReelDiscovery/
├── Program.cs
├── Forms/                         # WizardForm (shell), dialogs
├── UserControls/Steps/            # new Step* controls (thin)
├── Domain/                        # CaseGuide, Fact, CastMember, Issue, Entity, Manifest, ThreadPlan,
│                                  # MessagePlan, AttachmentSpec, JournalEntry, CorpusConfig, presets
├── Ai/
│   ├── LlmRequest.cs              # provider-neutral request/response records
│   ├── ILlmGateway.cs / ChatClientLlmGateway.cs
│   ├── IBatchGateway.cs / OpenAIBatchGateway.cs / AnthropicBatchGateway.cs
│   ├── IMediaGateway.cs / OpenAIMediaGateway.cs
│   ├── ProviderFactory.cs         # builds IChatClient per provider + middleware pipeline
│   ├── UsageTrackingChatClient.cs # DelegatingChatClient → TokenUsageTracker
│   └── Pricing/                   # model catalog + prices (JSON, user-editable)
├── Prompts/                       # embedded .md/.txt templates + PromptTemplate renderer
├── CaseGuide/                     # CoreDocumentGenerator, *Extractor, EntityRegistryBuilder,
│                                  # CaseGuideValidator, AncillaryDocGenerator, CaseGuideExporter
├── Planning/                      # BudgetAllocator, RelevantThreadPlanner, FillerPlanner,
│                                  # DecoyPlanner, ManifestValidator, CorpusPlanner (facade)
├── Rendering/                     # ContextAssembler, ThreadRenderer, RenderValidator,
│                                  # RenderedThreadMapper (DTO→EmailMessage), SignatureCorrector
├── Enrichment/                    # DocumentEnricher, ImageEnricher, VoicemailEnricher, CalendarEnricher
├── Execution/                     # RunEngine, WorkItem, SyncExecutor, BatchExecutor, ThreadUnitOfWork,
│                                  # ProgressSnapshot
├── Persistence/                   # RunStore, JournalWriter, AtomicFile
├── Output/                        # EmlFileService (moved), AnswerKeyWriter, OfficeDocumentService,
│                                  # CalendarService
├── Helpers/                       # ThreadingHelper, HtmlEmailFormatter, FileNameHelper, DateHelper
└── Models/ (legacy, shrinks to zero as old flow is deleted)
ReelDiscovery.Tests/               # sibling xUnit project: validators, allocator, assembler, mapper,
                                   # signature corrector, journal replay, answer key projection
```

### Structure Rationale
- **Domain/** has no dependencies. Every other folder depends on it, and it depends on nothing, which breaks the `WizardState` → `OpenAIService` cycle.
- **Ai/** is the only folder that references `OpenAI`, `Anthropic`, or `Microsoft.Extensions.AI`. Everything else sees `ILlmGateway` / `IBatchGateway` / `IMediaGateway`, and tests use canned fakes.
- **Planning/** and **Rendering/** are separate folders because they are separate phases with a persisted artifact (the manifest) between them. That boundary is what makes per-item retry, batching, and the answer key possible.
- **Prompts/** as embedded resources: prompt edits don't touch C# logic, and each template is paired with the DTO whose JSON schema is generated from the type (see Pattern 5).

---

## Architectural Patterns

### Pattern 1: Hierarchical source of truth (core doc → issues → cast → facts → entities)

**What:** Generate the narrative top-down once, then extract structured layers from it. Every layer references the layer above by ID.
**When:** Always. This is the backbone of the core value.
**Why:** Long-form generation research consistently finds that LLMs drift on characters, timelines, and world facts across long outputs. The two main mitigations are hierarchical planning (outline first) and explicit state/memory tracking. (ConStory-Bench/ConWriter and "Lost in Stories" survey the failure modes. FactTrack adds validity intervals per fact so that "changed over time" isn't mistaken for a contradiction. NstAgent tracks retrospective facts plus prospective obligations.) Our Fact records with `Date`/`EndDate` and the planner's `MustConvey` list are direct analogues.
**Trade-offs:** More up-front calls and a review UI, but they are cheap (a handful of calls) compared with 5K emails, and the user explicitly wants to review and edit here.

### Pattern 2: Plan-then-render with pinned specifics

**What:** The planner decides *who, when, which facts, which exact numbers/titles, and reply structure* for every message. The renderer only writes prose that satisfies the plan.
**When:** For all relevant and decoy threads. Filler gets a lighter plan (category, custodian, date, subject seed).
**Why:**
- Cross-thread consistency no longer depends on generation order. If two threads mention the change-order amount, both read `E12 = $184,350.00` from the registry. No thread invents it.
- Rendering items become independent, which is the prerequisite for parallelism and Batch APIs (50% cost reduction on both providers).
- The answer key is exact because labels are assigned at planning time, not inferred afterwards.
**Trade-offs:** The planner prompt is the hardest prompt in the system, and the manifest for 5K emails is large. Mitigate by planning in chunks (per issue-arc or per calendar month) with a deterministic skeleton first.

**Planner decomposition (recommended):**
1. **BudgetAllocator (pure).** Splits total emails into relevant and non-relevant by the user's ratio. Relevant: distributes across facts, weighted by `Importance` (Key facts get 2-4 threads, Background facts get 0-1), using the largest-remainder method (fixes the `DistributeEmails` bug class). Non-relevant: distributes across categories × custodians × months.
2. **RelevantThreadPlanner (LLM, chunked).** Input per chunk: the case synopsis, the facts in the chunk's window plus the facts immediately before and after it, the cast active in the window, the entity registry, and the thread count budget. Output: `ThreadPlan`s with message skeletons. Thread length is capped at about 12 messages; long sagas become multiple threads (more realistic, and each render fits in one call).
3. **DecoyPlanner (LLM).** The same project, orgs, and keywords, with `MustNotMention` populated with the relevant facts and entities. Plans labeled `Decoy` (non-relevant in the key, flagged as near-miss).
4. **FillerPlanner (mostly deterministic).** Category templates × custodian × date, plus an LLM "subject seed" pass in bulk. No case facts in context at all, so filler cannot leak relevance.
5. **ManifestValidator (pure)** runs over the whole manifest and rejects it or re-plans the offending chunk.

### Pattern 3: Knowledge-gated context projection (ContextAssembler)

**What:** Every render call gets a bounded, deterministic context pack instead of "the history so far".

```
[STABLE PREFIX — identical for every relevant-thread call in the run → prompt-cache friendly]
  System prompt (style rules, output schema rules)
  Case synopsis (≈300-600 words, generated once from the core doc)
  Organizations + domains + entity registry (canonical values)
[VARIABLE SUFFIX]
  Cast subset: participants of this thread (+ people they'd mention), with role/title *as of* date
  Knowledge-gated facts: facts with Date ≤ thread.start where participant ∈ AwareIds ∪ ActorIds
                         (+ the thread's own FactIds, even if they occur during the thread)
  Thread plan: messages (from/to/date/intent), MustConvey, MustNotMention, pinned EntityIds
  (Long threads only) prior chunk's rendered messages verbatim
```

**Why:** It keeps each call bounded (~3-8K input tokens), which matters at 5K emails. It prevents future leak by construction. And it lets overlapping storylines coexist: a thread about issue I2 still sees the I1 facts its participants know, so it can't contradict them.
**Prompt caching:** Put the stable content first. OpenAI caches long identical prefixes automatically. Anthropic needs explicit `cache_control` breakpoints, and for batches Anthropic recommends the 1-hour cache duration because batch items may run more than 5 minutes apart. Anthropic also documents batch cache hits as best-effort, since items process concurrently and in any order.

### Pattern 4: Thread as the unit of work, journal as the source of progress

**What:** `ThreadUnitOfWork` takes one thread from plan to `.eml` on disk, then appends a journal line. Enrichment failures degrade that single message (the attachment is omitted and the error is journaled). They never fail the thread, and a failed thread never fails the run.

```csharp
public async Task<ThreadOutcome> ExecuteAsync(ThreadPlan plan, RenderedThread? prerendered, CancellationToken ct)
{
    var rendered = prerendered ?? await _renderer.RenderAsync(_assembler.Build(plan), ct).ConfigureAwait(false);
    var violations = _validator.Check(plan, rendered);
    for (int i = 0; violations.Count > 0 && i < MaxRepairAttempts; i++)
    {
        rendered = await _renderer.RenderAsync(_assembler.Build(plan, violations), ct).ConfigureAwait(false);
        violations = _validator.Check(plan, rendered);
    }
    var thread = _mapper.ToEmailThread(plan, rendered);            // ThreadingHelper + HtmlEmailFormatter + SignatureCorrector
    foreach (var (msg, spec) in thread.AttachmentWork())
        _journal.Append(await _enrichers.TryEnrichAsync(msg, spec, ct).ConfigureAwait(false)); // never throws
    var paths = await _eml.SaveThreadAsync(thread, ct).ConfigureAwait(false);
    _journal.Append(JournalEntry.ThreadDone(plan.Id, paths, violations));
    thread.ReleasePayloads();                                        // free byte[]s immediately
    return ThreadOutcome.Success(plan.Id, paths);
}
```

**Why:** This one pattern fixes four known problems: the save-at-end data-loss bug, the "one attachment aborts all" bug, unbounded memory, and phase-barrier stalls.
**Journal over rewrite:** The manifest is written once and never rewritten. Status goes to an append-only `journal.jsonl`, and current state is the replay of the journal. That makes a crash mid-write lose at most one line and avoids rewriting a multi-MB manifest 5K times. (Pause/resume is out of scope for v1, but this layout makes it a small later feature: replay the journal and skip Done items.)

### Pattern 5: Provider-neutral `LlmRequest` above `IChatClient`

**What:**

```csharp
public sealed record LlmRequest(
    string ItemId,                       // becomes batch custom_id
    ModelTier Tier,                      // Planning / Rendering / Filler  → resolved to concrete model per provider
    string System, IReadOnlyList<LlmMessage> Messages,
    Type? ResponseType,                  // DTO; JSON schema generated from it (AIJsonUtilities)
    int MaxOutputTokens, float? Temperature,
    IReadOnlyList<int> CacheBreakpoints);// indices of messages ending the cacheable prefix

public interface ILlmGateway  { Task<LlmResponse<T>> SendAsync<T>(LlmRequest r, CancellationToken ct); }
public interface IBatchGateway
{
    Task<string> SubmitAsync(IReadOnlyList<LlmRequest> reqs, CancellationToken ct);   // returns batchId
    Task<BatchStatus> PollAsync(string batchId, CancellationToken ct);
    IAsyncEnumerable<BatchItemResult> ResultsAsync(string batchId, CancellationToken ct); // keyed by custom_id
}
```

- **Sync path:** `ChatClientLlmGateway` translates to `IChatClient.GetResponseAsync<T>` (MEAI structured-output extension, JSON-schema response format). OpenAI comes via `Microsoft.Extensions.AI.OpenAI` (`AsIChatClient()`), and Claude via the official `Anthropic` package (`client.AsIChatClient(model)`). A `DelegatingChatClient` middleware records usage into `TokenUsageTracker`, so cost tracking works for both providers.
- **Batch path:** `IChatClient` has no batch abstraction, so each `IBatchGateway` translates `LlmRequest` to the provider's native line format. OpenAI uses a `/v1/chat/completions` or `/v1/responses` JSONL file, then Files upload, then Batch create. Anthropic uses `Messages.Batches.Create` with `MessageCreateParams`. Results are **not ordered** on either provider, so always map by `custom_id`.
- **Why not bind the renderer directly to `IChatClient`:** A `ChatMessage` list plus `ChatOptions` isn't serializable or translatable into batch lines. The neutral record is. It also lets the manifest store "the exact request used" for diagnostics.
- **Structured outputs:** Use schema-constrained output on both providers. Claude's structured outputs are GA via `output_config.format`; OpenAI has JSON-schema response format. That removes the fragile "JSON in prompt + hope" contract. Keep a deterministic post-parse validation anyway.

### Pattern 6: Hybrid sync/batch routing in the RunEngine

**What:** Work items carry a routing hint.

| Item type | Default route | Why |
|-----------|---------------|-----|
| Case guide, planning, repair retries | Sync | The user is waiting; needs iteration |
| Relevant thread, fits one call | Batch if "economy mode", else Sync | Independent by design (Pattern 2) |
| Relevant thread > cap (multi-chunk) | Sync, sequential chunks | Chunk N needs chunk N-1 text |
| Decoy + filler threads | Batch | Bulk, cheap, order-independent: the 50% discount applies to most of the corpus |
| Document content (docx/xlsx/pptx text) | Batch or Sync | Independent given the manifest's DocChainPlan (version N content spec is planned) |
| Images, TTS | Sync (OpenAI) | Not in the chat batch path. OpenAI Batch supports image endpoints, but keep v1 simple. |

A batch result flows into the *same* `ThreadUnitOfWork` as `prerendered`, so validation, mapping, enrichment, and saving are identical for both routes. Errored or expired batch items (both providers have a 24-hour window; Anthropic doesn't bill expired items) are requeued to sync. Persist `batchId → [custom_id]` in `batches.json` as soon as the submit call returns, so paid work is never orphaned.

Verified limits: Anthropic allows 100,000 requests or 256 MB per batch, keeps results for 29 days, and uses `custom_id` matching `^[a-zA-Z0-9_-]{1,64}$`. OpenAI allows 50,000 requests or 200 MB per batch with a 24-hour window; output and error files are kept for 30 days. A 5K-email corpus is roughly 500-1,500 thread requests, well within one batch per provider.

---

## Data Flow

### End-to-end flow

```
[Scenario text + Preset] ──StepScenario──► CaseProfile
        │
        ▼ CoreDocumentGenerator (sync; outline→sections for Complex)
CoreDocument ──user edit/regenerate──► frozen CoreDocument
        │
        ▼ IssueExtractor → CastExtractor → FactExtractor → EntityRegistryBuilder (sync, structured output)
Issues, Cast, Facts, Entities ──CaseGuideValidator──► user edit/regenerate ──► CaseGuide (saved: case-guide.json)
        │                                   │
        │                                   └──► AncillaryDocGenerator (optional) ──► .docx
        │                                   └──► CaseGuideExporter ──► .docx
        ▼ + CorpusConfig (total N, relevant %, attachment %s, provider/economy mode)
CorpusPlanner: BudgetAllocator → RelevantThreadPlanner (chunked) → DecoyPlanner → FillerPlanner
        │
        ▼ ManifestValidator (re-plan offending chunks)
Manifest (saved: manifest.json, immutable) ──StepPlanReview──► (counts per issue/custodian/month; answer-key preview)
        │
        ▼ RunEngine: enqueue WorkItems
   ┌────┴──────────────────────────────┐
   ▼ Sync route                        ▼ Batch route
ContextAssembler → LlmRequest       ContextAssembler → LlmRequest[] → IBatchGateway.Submit
   → ILlmGateway                        → poll → results by custom_id
   └────────────┬──────────────────────┘
                ▼
ThreadUnitOfWork: validate/repair → map (ThreadingHelper, HtmlEmailFormatter, SignatureCorrector)
                → enrich (docs/images/voicemail/calendar; per-item failure isolated)
                → EmlFileService.SaveThread → journal.jsonl append → release payloads
                │
                ▼ (continuous) ProgressSnapshot (immutable record) → IProgress → UI
                ▼ (end, or on cancel/failure) AnswerKeyWriter(manifest + journal) → answer-key.csv/.xlsx
```

### Answer key derivation
One row per output document, keyed by `MessagePlan.Id`. Columns: file path (from the journal), custodian (`FromId` for sent items; for per-recipient copies, the folder owner), sent date, relevance label, issue IDs + titles, fact IDs + summaries, near-miss flag, attachments (names; family = parent email), and generation status (OK / degraded: attachment X missing). Because it is a projection of manifest + journal, it is regenerated after every run, including partial and cancelled runs. That satisfies "never lose finished work" for the key as well as the emails.

Optional QA (a later phase): an LLM "blind classifier" pass samples rendered emails and checks the label, which catches renders that drifted off-plan. Treat it as a quality metric, not a gate.

### State management
- **CaseSession** (UI-facing) is saved to the RunStore on every step leave. That is cheap, protects the expensive guide and plan from app crashes, and is not the same as run pause/resume.
- **Nothing in the run mutates shared objects.** Document chain versions are decided in `DocChainPlan`, not by `VersionNumber++` at runtime. Randomness uses `Random.Shared` or per-item seeded `Random(hash(itemId))`, which is thread-safe and reproducible.
- **Progress** is reported as immutable `record ProgressSnapshot` values, not a shared mutable object.

---

## Handling overlapping storylines among shared characters

This is the core consistency problem. The design handles it in five places:

1. **Facts are global, not per-storyline.** There is one timeline. An issue is a *tag* on facts, not a container. Two concurrent issues involving the same PM are just interleaved facts with overlapping `ActorIds`.
2. **The planner sees neighbors.** Each planning chunk is a time window across all issues, not a single issue in isolation. The planner therefore sees that in March the PM is both hiding leak reports (I1) and disputing change order CO-7 (I2), and can plan threads that touch both or reference each other.
3. **Knowledge gating at render time.** The ContextAssembler gives every thread all facts its participants know as of that date, across issues. A thread can't contradict a fact it was shown and can't mention one it wasn't.
4. **Pinned entities.** Every number, title, and identifier comes from the registry, so two threads can't disagree on the amount.
5. **Cast temporal validity.** `ActiveFrom/To` plus title-as-of-date stop people from emailing before they're hired or after they leave. The ManifestValidator enforces this in the plan, and the RenderValidator checks that the "From" names in signatures match.

What this design deliberately does **not** do is carry "the story so far" between batches as free-form summaries. That is the current approach, and it is the root cause of drift.

---

## Where the EmailGenerator refactor fits

Use the **strangler pattern**. Keep the old flow compiling until the new pipeline produces EMLs end-to-end, then delete the old flow (its steps and generators are explicitly out of scope).

| Existing piece | Fate | New home |
|----------------|------|----------|
| `GenerateEmailsAsync` orchestration, `DistributeEmails`, storyline batching, BEGINNING/MIDDLE/CONCLUSION phases, phase barriers | **Replace** | RunEngine + CorpusPlanner + ThreadUnitOfWork |
| `ConvertDtoToEmailMessage` | **Extract + rewrite** | `RenderedThreadMapper`. Reply target comes from `MessagePlan.ReplyToSeq`; dates come from the plan. |
| `CorrectSignatureBlock` | **Extract as-is first, add table-driven tests, then fix** | `Rendering/SignatureCorrector` (static). With plan-known senders, it can become much simpler. |
| `GeneratePlannedDocumentAsync` + `_documentChains` | **Extract**; chain state moves into `DocChainPlan` | `Enrichment/DocumentEnricher` |
| `GeneratePlannedImageAsync` + HTML surgery | **Extract**; HTML insertion moves into `HtmlEmailFormatter` via a placeholder token | `Enrichment/ImageEnricher` |
| `DetectAndAddCalendarInviteAsync` | **Replace detection with planning.** Meetings are planned `AttachmentSpec.Calendar` with validated times. | `Enrichment/CalendarEnricher` |
| Voicemail (TTS) generation | **Extract** | `Enrichment/VoicemailEnricher` via `IMediaGateway` |
| Dead code (`SelectEmailsForAttachments`, `GenerateAttachmentAsync`, `GenerateImageForEmailAsync`, `GenerateVoicemailForEmailAsync`, `ImageSuggestionResponse`) | **Delete first** (phase 1, zero risk) | n/a |
| `OpenAIService` | **Split**: chat → `IChatClient` via `ProviderFactory`; image/TTS → `OpenAIMediaGateway`; retry → gateway middleware | `Ai/` |
| `StorylineGenerator`, `CharacterGenerator`, `StepTopicInput/Storylines/Characters` | **Delete** after the new flow ships | n/a |
| `ThemeGenerator` | **Keep**; call it per `Organization.Domain` from the case guide | `CaseGuide/` or unchanged |
| `ThreadingHelper`, `HtmlEmailFormatter`, `EmlFileService`, `OfficeDocumentService`, `CalendarService`, `FileNameHelper` | **Reuse**. Add a `SaveThreadAsync`, ICS time validation, and the `cid:` banner fix. | `Helpers/`, `Output/` |

---

## Suggested Build Order

The dependency graph drives this order: domain model → gateway → guide → plan → render → scale. Each step produces something testable on its own.

| # | Phase | Delivers | Depends on | Why this position |
|---|-------|----------|------------|-------------------|
| 1 | **Foundation & seams** | Test project. Delete dead code. `Random.Shared` and lock fixes. File logging + global exception handlers. `Ai/` gateway: `LlmRequest`, `ILlmGateway` over `IChatClient` for OpenAI **and** Claude, usage middleware, structured output. `IMediaGateway`. DPAPI key storage per provider. | nothing | Every later component calls the gateway. Building it first gives the Claude requirement early and lets all generators be tested with fakes. |
| 2 | **Domain model + RunStore** | `Domain/` records, `RunStore` (atomic JSON, JSONL journal), `CaseSession` replacing `WizardState` for new steps | 1 (minimal) | The schema is the contract for guide, planner, renderer, and answer key. Persistence must exist before anything expensive is generated. |
| 3 | **Case guide pipeline + UI** | Scenario/preset step, CoreDocumentGenerator, Issue/Cast/Fact/Entity extractors, CaseGuideValidator, review/edit/regenerate steps, .docx export | 1, 2 | Guide quality bounds corpus quality. It is also user-visible value on its own (a guide with no corpus is already a demo artifact). |
| 4 | **Corpus planner + manifest + answer key projection** | BudgetAllocator, RelevantThreadPlanner, ManifestValidator, StepPlanReview, AnswerKeyWriter (from the manifest, before rendering) | 3 | The hardest prompt. Validating it *before* spending render tokens is cheap. The answer key can be demoed from the plan alone. |
| 5 | **Renderer + ThreadUnitOfWork (sync), text-only EMLs** | ContextAssembler, ThreadRenderer, RenderValidator + repair loop, RenderedThreadMapper, SignatureCorrector, incremental EML save + journal, RunEngine (sync), immutable progress | 4 | First end-to-end corpus. Incremental save and failure isolation are built in from day one, not retrofitted. |
| 6 | **Enrichment extraction** | Document/Image/Voicemail/Calendar enrichers from EmailGenerator, driven by `AttachmentSpec` + `DocChainPlan`, failure-isolated | 5 | Reuses working builders. It plugs into an existing unit of work, so the risk is contained. |
| 7 | **Non-relevant corpus** | FillerPlanner (deterministic + subject seeds), DecoyPlanner (`MustNotMention`), relevance ratio control, 5K scale test (memory, file counts) | 4, 5 | It is the majority of the volume, so scale problems surface here. It must exist before batching pays off. |
| 8 | **Batch execution** | `IBatchGateway` for OpenAI + Anthropic, BatchExecutor, `batches.json`, requeue-to-sync, economy-mode toggle | 5, 7 | Only valuable once there is bulk independent work. It depends on `LlmRequest` being serializable (phase 1). |
| 9 | **Ancillary discovery docs** | Depositions, RFP, RFA, interrogatories | 3 only | Off the critical path. Can run in parallel with 4-8 or go last. |
| 10 | **Cutover & cleanup** | Remove the old wizard steps and generators, remove `WizardState`, raise the UI caps to 5K, optional blind-classifier QA | all | Strangler completion |

**Critical path:** 1 → 2 → 3 → 4 → 5 → 7 → 8. Phases 6 and 9 hang off it.

**Research flags for the roadmap:**
- **Phase 4 (planner):** needs deeper prompt research and experimentation: chunk size, how many neighbor facts, and planning cost at 5K. Highest uncertainty in the system.
- **Phase 8 (batch):** verify the OpenAI .NET SDK's batch/file surface in the pinned version (the protocol-level `BatchClient` has been marked experimental in 2.x; LOW confidence on the current status). Also check how to pass Anthropic `cache_control` through MEAI for the sync path (may need provider-specific `RawRepresentationFactory` / additional properties; LOW confidence).
- **Phase 3:** standard patterns, but extraction fidelity (facts ↔ core-doc paragraphs) needs a spike on Complex presets.
- **Phases 1, 2, 5, 6:** standard engineering. The patterns here are sufficient.

---

## Anti-Patterns

### Anti-Pattern 1: Rolling "story so far" summaries between batches
**What people do:** Summarize prior output and feed it into the next batch, as the current `EmailGenerator` does.
**Why it's wrong:** Summaries lose specifics, compound errors, force sequential generation (no batching), and still leak the future when summaries are shared across threads.
**Do this instead:** A structured facts timeline plus pinned entities plus knowledge-gated projection (Patterns 2 and 3). Only intra-thread chunking carries prior text, and it carries it verbatim.

### Anti-Pattern 2: Letting the renderer choose dates, recipients, reply targets, or numbers
**Why it's wrong:** This is the source of the replyToIndex bug, the culture-dependent date parsing, the chronology inversions, and cross-thread number drift.
**Do this instead:** The planner owns all structure. The renderer owns prose only.

### Anti-Pattern 3: Labeling relevance after generation
**Why it's wrong:** The key becomes an opinion instead of ground truth, and demos of automated classification need a reliable key.
**Do this instead:** Labels are assigned in the manifest. Validators and optional QA check that renders honor them.

### Anti-Pattern 4: Phase-wide barriers (`Task.WhenAll` per artifact type)
**Why it's wrong:** One failure aborts everything, memory holds the whole corpus, and slow items stall all progress.
**Do this instead:** Per-thread unit of work, per-item try/catch, and an append-only journal.

### Anti-Pattern 5: Coding the pipeline directly against one SDK or against `IChatClient` only
**Why it's wrong:** You can't batch, and you can't swap providers per tier (for example, Claude for planning and a cheaper model for filler).
**Do this instead:** `LlmRequest` + `ModelTier`, with sync and batch translators per provider.

### Anti-Pattern 6: Rewriting the manifest file on every status change
**Why it's wrong:** O(n²) I/O at 5K items, and a crash mid-write corrupts the plan.
**Do this instead:** Immutable `manifest.json` + append-only `journal.jsonl`.

---

## Integration Points

### External services

| Service | Integration | Notes |
|---------|-------------|-------|
| OpenAI chat | `OpenAI` SDK → `Microsoft.Extensions.AI.OpenAI` `AsIChatClient()` | MEAI.OpenAI 10.10.x is stable, targets net8.0, and requires `OpenAI` ≥ 2.14 (the current pin of 2.1.0 must be upgraded) |
| Anthropic chat | Official `Anthropic` NuGet (v10+ is the official SDK; 12.51.0 current) → `client.AsIChatClient(model)` | Targets net8.0/netstandard2.0; depends on MEAI.Abstractions ≥ 10.5.1. Built-in retries (2 by default; 408/409/429/5xx) mean the gateway's own retry must not double up. |
| OpenAI Batch | Files upload JSONL + Batches create/poll + output/error files | 50% discount, 24h window, 50K requests / 200 MB, unordered results (match by `custom_id`) |
| Anthropic Message Batches | `client.Messages.Batches.*` | 50% discount, most batches under 1h, 24h expiry (expired items not billed), 100K requests / 256 MB, results kept 29 days, unordered |
| OpenAI images / TTS | `IMediaGateway` (OpenAI only) | With Claude as the text provider, media needs a secondary OpenAI key or gets disabled with a clear UI message |

### Internal boundaries

| Boundary | Communication | Notes |
|----------|---------------|-------|
| UI ↔ pipeline | Async service calls, `IProgress<ProgressSnapshot>`, `CancellationToken` | Steps never construct gateways. `ProviderFactory` is built once from settings. Block form close while running, or cancel and await. |
| CaseGuide → Planner | Frozen `CaseGuide` object + hash | The manifest records `CaseGuideHash`; editing the guide invalidates the manifest |
| Planner → Engine | `manifest.json` (immutable) | The run's only input |
| Engine → Output | `EmailThread` → `EmlFileService.SaveThreadAsync` | Per thread, immediately |
| Engine → Answer key | journal replay + manifest | Pure projection |

---

## Scalability Considerations

| Concern | ~100 emails | ~1K emails | ~5K emails |
|---------|-------------|------------|------------|
| Planning | 1-2 planner calls | Chunk by month/issue (~10 calls) | ~30-60 chunked planner calls; filler is mostly deterministic |
| Rendering | Sync, parallel 4-6 | Sync, or batch for filler | Batch for filler/decoys (~70-90% of volume); sync for multi-chunk relevant threads |
| Memory | Irrelevant | Per-thread release matters with images | Mandatory per-thread release; never hold the corpus. Fix the base64 banner (about 300 MB at 5K). |
| Rate limits | n/a | Gateway retry with jitter + Retry-After | Batch moves most load off synchronous rate limits |
| Persistence | Single folder | Journal replay < 1s | Journal of ~10-20K lines is fine; `manifest.json` several MB, written once |
| Cost visibility | Live ticker | Live ticker per tier | Ticker plus batch "pending" cost (batch usage arrives with results) |

---

## Sources

- Anthropic C# SDK docs (official package `Anthropic`, IChatClient integration, retries, batches pagination): https://platform.claude.com/docs/en/cli-sdks-libraries/sdks/csharp (HIGH)
- Anthropic NuGet `Anthropic` 12.51.0, net8.0, MEAI.Abstractions ≥ 10.5.1: https://www.nuget.org/packages/Anthropic (HIGH)
- Anthropic Message Batches (limits, 24h expiry, 29-day retention, `custom_id` regex, unordered results, best-effort cache hits, 1h cache recommendation): https://platform.claude.com/docs/en/build-with-claude/batch-processing (HIGH)
- Claude structured outputs GA (`output_config.format`): https://platform.claude.com/docs/en/build-with-claude/structured-outputs (MEDIUM, via search summary of official page)
- OpenAI Batch API (50%, 24h, 50K/200 MB, unordered, error file, `batch_expired`): https://developers.openai.com/api/docs/guides/batch (HIGH)
- Microsoft.Extensions.AI.OpenAI 10.10.1 (stable, net8.0, OpenAI ≥ 2.14): https://www.nuget.org/packages/Microsoft.Extensions.AI.OpenAI/ (HIGH)
- MEAI structured output (`GetResponseAsync<T>`, JSON schema response format): https://learn.microsoft.com/en-us/dotnet/ai/quickstarts/structured-output , https://learn.microsoft.com/en-us/dotnet/ai/ichatclient (HIGH)
- FactTrack, time-aware world-state tracking with validity intervals: https://arxiv.org/pdf/2407.16347 (MEDIUM, informs the Fact model)
- Narrative state tracking (retrospective facts + prospective obligations): https://arxiv.org/html/2609.35759 (MEDIUM)
- ConWriter / ConStory-Bench, hierarchical planning + memory as the main consistency mitigations: https://arxiv.org/html/2608.05169v1 (MEDIUM)
- Lost in Stories, consistency bug taxonomy in long LLM generation: https://arxiv.org/html/2603.05890v1 (MEDIUM)
- Existing codebase analysis: `.planning/codebase/ARCHITECTURE.md`, `CONCERNS.md`, `STRUCTURE.md` (HIGH)

---
*Architecture research for: case-guide-driven eDiscovery corpus generation (ReelDiscovery)*
*Researched: 2026-09-29*
