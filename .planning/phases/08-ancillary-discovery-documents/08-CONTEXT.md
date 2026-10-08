# Phase 8: Ancillary Discovery Documents - Context

**Gathered:** 2026-10-08
**Status:** Ready for planning
**Mode:** --auto (every gray area resolved with the recommended option; see 08-DISCUSSION-LOG.md)

<domain>
## Phase Boundary

The user can **optionally** generate realistic discovery documents grounded in the case guide (Phase 3) and export each one as .docx:
- **Deposition transcript excerpts** for key witnesses the user picks, with a caption page, numbered lines and numbered pages (ANC-01).
- **Requests for Production** keyed to the issues (ANC-02).
- **Requests for Admission** keyed to the key facts (ANC-03).
- **Interrogatories** keyed to the issues (ANC-04).
- Every document uses the same names, dates, amounts and allegations as the core document, cast, facts and canonical values (ANC-05).

The feature depends only on the case guide. It works whether or not a corpus has been generated. When a corpus exists, deposition exhibits and document RFAs may cite real DocIDs (D-21).

**Out of scope:**
- Responses and objections to discovery, privilege logs, Bates-stamped productions, load files.
- An in-app text editor for these documents (edit the exported .docx in Word).
- Answer-key or review-protocol changes (D-30).
- Full-length (150+ page) transcripts, video, or synced transcript formats.

</domain>

<decisions>
## Implementation Decisions

### Carried forward (locked in Phases 1-7)
- **D-01:** All LLM work goes through `ILlmGateway` with strict JSON-schema DTOs and must work with Claude or OpenAI. The Phase 2 strict-schema limits apply: at most 16 nullable fields, no recursion, dates as `yyyy-MM-dd` strings, and provider output caps, so large documents are chunked. New DTOs are registered in `SchemaTypes` / `CaseSchemaTests`.
- **D-02:** Names are kept exactly as the guide has them, **including names from fictional works**. Prompts never say that names must be fictional. Only real-world entities follow the Phase 2 substitution decisions already stored on `CaseDraft.Substitutions`. Content takes a firm position.
- **D-03:** Every new or changed screen is rendered at **144 DPI (150% scaling)** with `ReelDiscovery.Tests/Render/RenderHarness.cs`, checked for clipping, and the PNGs are inspected by eye. No fixed pixel sizes. Use the existing theme and controls: `Theme`, `ThemedButton` via `ButtonHelper.CreateButton`, `CardPanel`, `SlimProgressBar`, `ThemedDialog`, and `GridStyle`.
- **D-04:** **Code owns structure; the LLM writes text only.** Everything that has a canonical source comes from code: captions, court, parties, counsel and signature blocks, dates, numbering, exhibit numbers, page and line layout, and which issue or fact each request is keyed to.
- **D-05:** Never-throws units of work. Catch `OperationCanceledException` before `Exception`. One failed document never aborts the others (RUN-02 pattern). Fatal provider stops (Auth / SpendLimit) end the run cleanly and keep finished documents.
- **D-06:** Costs fold into the existing ticker and the Case Summary cost breakdown. The stable case-guide prefix is cached where the provider supports it (Phase 7 D-19).

### Where it lives in the flow
- **D-07:** A **new optional wizard step, "Discovery Documents"**, sits **between Generate Corpus and Case Summary**.
  - Next is always enabled unless generation is running, so the step can be skipped.
  - Placing it after the corpus lets exhibits cite DocIDs when a corpus exists. Every corpus step can already be passed with Next, so the step is also reachable without a corpus.
  - Rationale: the step needs witness picking, four document types, an estimate, progress and per-document results. That is too much for a Case Summary dialog, and putting it before the corpus would add a step to the critical path.
- **D-08:** **Case Summary gets one "Discovery documents" row.**
  - Its status line shows "N documents · last generated …" or "Not generated".
  - Buttons: **"Open Discovery Folder"** and **"Discovery Documents"**. The second navigates to the step, the same way "Plan Corpus" does.
  - Export All on Case Summary does not regenerate these documents. They are written when they are generated (D-27).
- **D-09:** **Do not add a new `CaseStage` value.** Reopening a case still lands on the furthest corpus stage or Summary. The step is reached through navigation or the Case Summary button. Rationale: this avoids changing the ordinal semantics of `AdvanceStage`.

### Screen layout
- **D-10:** One step screen with three cards, matching the Case Guide, Review Plan and Generate Corpus visual language:
  1. **Depositions:** a checklist of eligible witnesses (D-12) and a pages-per-excerpt control.
  2. **Written discovery:** checkboxes for RFP, Interrogatories and RFA, each with a count control, plus propounding and responding party pickers (D-16).
  3. **Ready / results:** a live cost estimate, one **Generate** button, a SlimProgressBar and Cancel. A results grid has one row per document showing status (Generated / Generated with warnings / Failed / Out of date), with **Open in Word** and **Regenerate…** buttons.
- **D-11:** **Regenerate…** opens a small ThemedDialog (following the `RegenerateTabDialog` pattern) with optional guidance text. It replaces only that document. Nothing regenerates silently.

### Witness selection (depositions)
- **D-12:** **Eligible witnesses** are key-tier cast members whose role is not counsel. They are ranked by the number of key facts in which they appear as actor or knower.
  - Default checked count by preset: **Simple 1, Standard 2, Complex 3**. The defaults favor the top-ranked witnesses on the defending side, then the top-ranked on the other side.
  - The user can check any eligible witness, up to **6** per run.
- **D-13:** **Code assigns the deposition roles from the cast:**
  - **Examining attorney:** the partner at the outside law firm representing the opposing party.
  - **Defending attorney:** the partner or associate at the outside firm representing the witness's organization.
  - **Appearances:** the counsel for each party.
  - The court reporter's name, CSR number and the videographer are **invented per document by code from a small built-in list**. They are never cast members.
- **D-14:** **Deposition dates are assigned by code.**
  - Business days (`BusinessCalendar`) about **3-9 months after the core document's FilingDate**, and never before the latest key-fact date.
  - Each witness gets a different date, in a deterministic order.
  - Start time is 9:30 or 10:00 a.m. Location is either the examining firm's office in the venue city or "taken remotely via videoconference".
- **D-15:** **Excerpt length:** the pages-per-excerpt default is **Simple 15, Standard 25, Complex 40**. The user can choose 10-60 pages. The caption page says **"EXCERPT"** and the index reflects only the excerpted pages.

### Written discovery
- **D-16:** **Default parties and sets:**
  - The first plaintiff or petitioner serves the primary defendant or respondent, as "FIRST SET OF …". The user can switch either party picker to any party in the core document.
  - Each run produces one set per checked document type. Multiple sets or multiple party pairs are deferred.
- **D-17:** **RFPs are keyed to issues.**
  - Default count by preset: **Simple 15, Standard 25, Complex 40**; adjustable from 5 to 80.
  - Code distributes the request slots so that **every issue gets at least one RFP**. Each request carries its issue ID(s) and names the custodians, project and date ranges from the guide.
  - Includes a preliminary statement, Definitions, Instructions (relevant time period = case timeline window, ESI form, privilege-log requirement, duty to supplement), a signature block and a certificate of service.
- **D-18:** **Interrogatories are keyed to issues**, with a **hard cap of 25 including subparts** (FRCP 33).
  - Default count by preset: **Simple 12, Standard 18, Complex 25**.
  - Every issue gets at least one. The set mixes "identify persons with knowledge" with contention interrogatories per issue.
- **D-19:** **RFAs are keyed to key facts**, one discrete fact per request.
  - Default count by preset: **Simple 10, Standard 20, Complex 30**; adjustable up to 50.
  - Code selects the facts, Key significance first and then chronological. Each RFA carries one fact ID and states that fact's **date, amount and actors verbatim** from the guide.
  - When a corpus exists, a few genuineness RFAs ("Admit that the document produced as DocID … is a true and correct copy …") reference hot documents linked to the selected facts.
- **D-20:** **Rule citations follow the jurisdiction in the core document.**
  - Federal court: FRCP 26/33/34/36.
  - State court: the state's rules of civil procedure cited generically, without invented local-rule numbers.
  - Response deadline: 30 days.

### Deposition content and format
- **D-21:** **Exhibits are numbered by code, consecutively across all depositions in the case.**
  - With a corpus: exhibits are hot or key relevant documents in which the witness participated, cited by DocID, subject and date, and the exhibit index lists them.
  - Without a corpus: exhibits are canonical documents from the values registry (contract, change order and invoice titles and numbers).
  - "(Deposition Exhibit N was marked for identification.)" appears where code placed it.
- **D-22:** **Testimony is grounded in the witness's knowledge.**
  - The prompt includes only facts in which the witness is an actor or knower (a KnowledgeGate-style filter). When a corpus exists, it also includes the ledger entries the witness knows as of the deposition date.
  - Examination is organized by issue or topic. Adverse witnesses may be evasive ("I don't recall", hedging) but **never contradict canonical dates, amounts or who-did-what**. There are a few impeachment sequences built on exhibits.
  - Objections ("Objection, form.") and privilege instructions come from the defending attorney.
- **D-23:** **The LLM returns structured turns; code lays out the pages.**
  - Each turn has a kind (Q, A, colloquy, objection, parenthetical), a speaker ID and text. Turns are generated in chunks per examination topic to stay under output caps.
  - Code wraps the turns to a fixed character width and lays out **25 numbered lines per page** with left-margin line numbers 1-25 and page numbers, in a monospaced transcript font (Courier New 12 pt) with exact line spacing and explicit page breaks. Word's automatic line numbering is not used, so page:line citations are deterministic and testable.
  - Front and back matter: a caption page (court, caption, case number, "DEPOSITION OF [NAME]", "EXCERPT", date, time, location, reporter), an appearances page, an examination index and exhibit index, the oath, the body, "(Whereupon the deposition concluded at …)", a witness signature and errata page, and the reporter's certificate.
- **D-24:** **One case number is assigned by code** in a court-appropriate format, for example `2:25-cv-01873` (federal) or a state-style docket number. It is derived deterministically from `CaseId` and shared by every ancillary document. The core document keeps its pre-filing "Case No. ____" blank.

### Investigation-memo cases
- **D-25:** **The civil litigation format is the primary target** (Complaint / Petition). For **Investigation Memo** cases, the step still works with the investigative equivalents:
  - Depositions render as **"Transcript of Sworn Witness Interview / Examination Under Oath"**, with the same page and line format.
  - RFPs render as a **document request / subpoena rider** (grand-jury style for Criminal, Civil Investigative Demand style for Regulatory, pre-suit request for InternalCorporate).
  - Interrogatories render as **CID-style written questions** for Regulatory matters and are hidden for Criminal matters.
  - **RFAs are hidden for Criminal and Regulatory memos**, with an explanatory note in the card.
  - Exact titles are Claude's discretion.

### Consistency enforcement
- **D-26:** **Grounding plus code checks with retries**, mirroring Phase 5 D-12..D-14.
  - Prompts use structured guide data (issues, the relevant cast, filtered facts, canonical values), **not the raw complaint text** (PITFALLS P-Ancillary).
  - After each chunk or document, code checks:
    - amounts and identifiers match the canonical values (`CanonicalValueScanner` / RenderValidator V3 logic);
    - every person named resolves to the cast or to the code-provided reporter and videographer;
    - every request references an existing issue or fact ID;
    - RFA dates and amounts equal their fact's values;
    - nothing references an event dated after the deposition date.
  - Each failure gets **2 retries with violation feedback**. After that, the best attempt is kept and the document is flagged **"Generated with warnings"**, with the violations listed on the result row.
- **D-27:** **Staleness:** each generated document stores the guide hash (`CaseGuide.ComputeHash()`) and core-document revision it was built from. If either changes later, the row shows **"Out of date with the case guide"** with Regenerate. Nothing regenerates automatically (Phase 3 D-20 pattern).

### Model, cost and persistence
- **D-28:** The **Story tier** writes every ancillary document, through **live calls only** (no Batch API, because the volume is small). The Ready card shows a cost estimate before Generate, computed through `GenerationCostEstimator`'s single pricing path. Bounded parallelism: one document per unit of work, under the existing parallelism setting.
- **D-29:** **Persistence and output:**
  - User selections (witnesses, counts, parties, pages) are saved in `case.json` on a new `CaseDraft.Discovery` settings object (bump the format version as needed).
  - Generated structured content (turns, requests, warnings, source hashes) goes in a sibling **`discovery-documents.json`** in the case folder, following the `corpus-plan.json` precedent, written atomically via `CaseStore.WriteAtomicAsync`.
  - .docx files are written to a **`Discovery\`** subfolder of the case folder, never inside the mailboxes or produced corpus. File names follow the pattern "<Case> - Deposition of <Full Name> (Excerpt).docx" and "<Case> - <Plaintiff short name>'s First Set of Requests for Production.docx".
  - The .docx is written **automatically when the document is generated**, and rewritten on regenerate.
- **D-30:** **Not added to the answer key or review protocol.** Ancillary documents are case-file material, not review-corpus documents. Mapping RFP numbers to issue codes in the key or protocol is deferred.

### Claude's Discretion
- DTO shapes for deposition turns, requests and chunking granularity (respect the strict-schema limits and provider output caps).
- Characters per line and wrap rules for the transcript layout, plus how colloquy and parentheticals indent. It must be exactly 25 lines per page.
- Exact reporter and videographer name list and CSR number format, the case-number format per court system, and the deposition date spacing within D-14's window.
- Witness ranking tie-breaks and the "defending side first" ordering rule.
- How issue slots are apportioned across requests (reuse `Helpers/Apportion.cs`), and the boilerplate wording of definitions and instructions.
- How the corpus-dependent enrichments (exhibit DocIDs, genuineness RFAs, ledger slice) detect that a usable corpus exists, using the plan plus journal state.
- Investigative-equivalent document titles for memo cases (D-25).
- Whether the .docx builder extends `CaseGuideDocxExporter.WordBuilder` (recommended: reuse its schema-ordered builder and caption code) or adds a sibling builder in the same style.

</decisions>

<canonical_refs>
## Canonical References

**Downstream agents MUST read these before planning or implementing.**

### Requirements and roadmap
- `.planning/ROADMAP.md`: Phase 8 goal and success criteria.
- `.planning/REQUIREMENTS.md`: ANC-01..ANC-05.
- `.planning/PROJECT.md`: core value (one consistent case story) and constraints.

### Research (domain conventions)
- `.planning/research/FEATURES.md` §C "Deposition transcripts", §D "Written discovery", the differentiator rows (exhibits = corpus DocIDs, true transcript format) and the Dependency Notes: the transcript layout norms, RFP/RFA/ROG structure and FRCP limits.
- `.planning/research/PITFALLS.md` P-Ancillary section (around lines 255-273): drift from the raw complaint, missing page:line, rule limits as constants.
- `.planning/research/ARCHITECTURE.md`: the AncillaryDocGenerator component (ILlmGateway + Office builders, independent of the corpus pipeline).

### Prior phase decisions
- `.planning/phases/03-case-guide/03-CONTEXT.md`: guide structure, stable IDs, staleness (D-20), Word export (D-12), fictional names (D-01).
- `.planning/phases/05-relevant-evidence-generation-narrative-ledger/05-CONTEXT.md`: code checks + 2 retries + keep-and-flag (D-12..D-14), knowledge gating, ledger.
- `.planning/phases/06-attachments-non-relevant-corpus/06-CONTEXT.md`: Office builders and org branding (D-19), hot documents.
- `.planning/phases/07-scale-cost-qa-cutover/07-CONTEXT.md`: cost display, caching, Case Summary QA rows, 150% DPI rule (D-04).
- `.planning/phases/02-case-scenario-core-document/02-RESEARCH.md`: strict-schema limits and token caps.
- `.planning/phases/07-scale-cost-qa-cutover/07-UI-SPEC.md`: current Case Summary row conventions.

### Code
- `Models/Case/CaseDraft.cs`, `Models/Case/CaseGuide.cs`, `Models/Case/CastMember.cs`, `Models/Case/KeyFact.cs`, `Models/Case/CaseIssue.cs`, `Models/Case/CanonicalValue.cs`, `Models/Case/CaseOrganization.cs`, `Models/Case/CoreDocument.cs` (parties, counts, counsel, FilingDate, Jurisdiction), `Models/Case/Jurisdiction.cs`, `Models/Case/ComplexityPreset.cs`: the grounding data.
- `Services/Case/CaseGuideDocxExporter.cs` (WordBuilder, caption and signature code, `StripInvalidXmlChars`, `LongDate`) and `Services/Case/CoreDocumentAssembler.cs`: the .docx and caption patterns to reuse.
- `Services/Evidence/StorySoFarDocxBuilder.cs`: precedent for an extra case-folder .docx and file-name guarding.
- `Services/Case/CaseStore.cs` (`WriteAtomicAsync`) and `Services/Corpus/CorpusPlanStore.cs`: sibling-file persistence pattern.
- `Services/Case/CanonicalValueScanner.cs`, `Services/Evidence/RenderValidator.cs`, `Services/Evidence/KnowledgeGate.cs`, `Services/Evidence/LedgerSelector.cs`: the consistency checks and knowledge filtering to reuse.
- `Services/Case/CaseGuideGenerator.cs` and `Services/Case/CaseGuidePrompts.cs`: the gateway calling pattern, chunking, and prompt rules for fictional names and firm position.
- `Services/Ai/ILlmGateway.cs`, `Services/Ai/LlmRequest.cs` (LlmTier.Story): the gateway.
- `Services/Corpus/GenerationCostEstimator.cs`: the single pricing path for the estimate.
- `Services/Corpus/BusinessCalendar.cs`: business-day dates. `Helpers/Apportion.cs`: slot apportioning.
- `Services/Corpus/AnswerKeyBuilder.cs`: the hot-document flag used to pick exhibits when a corpus exists.
- `Forms/WizardForm.cs` (`StepTypes`, `StepTypeForStage`, `NavigateToStage`): where the new step is inserted.
- `UserControls/StepCaseSummary.cs` (`CreateRow`, export rows): where the new row goes.
- `UserControls/IWizardStep.cs`, `UserControls/StepGenerateCorpus.cs`, `UserControls/StepGenerateCorpus.Run.cs`: step, Ready card and run patterns.
- `Forms/ThemedDialog.cs`, `Forms/RegenerateTabDialog.cs`: the regenerate-with-guidance dialog.
- `Helpers/Theme.cs`, `Helpers/CardPanel.cs`, `Helpers/ThemedButton.cs`, `Helpers/ButtonHelper.cs`, `Helpers/SlimProgressBar.cs`, `Helpers/GridStyle.cs`: UI kit.
- `ReelDiscovery.Tests/Render/RenderHarness.cs`: 144-DPI render tests.

</canonical_refs>

<code_context>
## Existing Code Insights

### Reusable Assets
- `CaseGuideDocxExporter.WordBuilder`: schema-ordered WordprocessingML (styles, paragraphs, tables, sections, tab stops, borders) plus the pleading caption and signature logic in `BuildCoreDocument`. Extend it for the transcript layout and the discovery captions.
- `CanonicalValueScanner` and the RenderValidator V3 (canonical values) and V4 (knowledge) checks: drop-in consistency checks for ancillary text.
- `KnowledgeGate` / `LedgerSelector`: point-in-time "what this person knows" filtering for deposition grounding.
- `CaseGuide.ComputeHash()` and `CaseDraft.Revision`: staleness detection.
- `CaseStore.WriteAtomicAsync`: atomic, OneDrive-safe writes for the JSON sidecar and the .docx files.
- `GenerationCostEstimator.PriceWork`: the estimate path. `TokenUsageTracker`: the ticker.
- `FakeLlmProvider` / `CaseTestData`: generator tests without a live provider.

### Established Patterns
- UI is built in code with TableLayoutPanel roots and content-sized rows. Handlers are wired after all controls exist; init-order null refs bit Phases 2-3.
- Pure static `*UiText` copy helpers are unit-tested (`CaseGuideUiText`, `GenerationUiText`, `QaUiText`). Add a `DiscoveryUiText`.
- Strict DTOs are nested internal classes with `[JsonPropertyName]` + `[Description]`.
- Output is deterministic: use `StableRank` / SHA-256, never `Random` or `Guid`, for dates, case numbers and reporter picks.

### Integration Points
- `WizardForm.StepTypes`: insert `StepDiscoveryDocuments` between `StepGenerateCorpus` and `StepCaseSummary`.
- `StepCaseSummary`: add the "Discovery documents" row and its navigation button.
- `CaseDraft`: add the `Discovery` settings object, plus a JSON round-trip test and a format-version migration.
- Case folder: a `Discovery\` subfolder and the `discovery-documents.json` sidecar. `GenerationReset` / Start Over must **not** delete these (they are independent of the corpus run).

</code_context>

<specifics>
## Specific Ideas

- The user's test cases include ones "inspired by The Office" and "Breaking Bad". Witnesses keep those names (for example, a deposition of Michael Scott), and email addresses and firms match the cast.
- A trainer should be able to go from a deposition exhibit (DocID) to the native email in the mailbox folder when a corpus exists.
- Transcripts should look indistinguishable from a real excerpt at a glance: line numbers 1-25 down the left margin, page numbers, Q./A. lines, "EXAMINATION BY MR. …:" headings, and a reporter's certificate.
- The user has been frustrated by slow progress. Keep the step optional, quick to skip, and clearly priced.

</specifics>

<deferred>
## Deferred Ideas

- Responses and objections to RFPs, ROGs and RFAs, plus interrogatory verifications: a separate document family.
- Privilege log generated from the privileged threads: a natural v2 item now that the privilege column exists.
- Mapping RFP numbers into the answer key and review protocol, so relevance is traceable to specific requests.
- Multiple sets or multiple propounding/responding party pairs per document type, and Rule 30(b)(6) corporate-representative depositions.
- Video deposition files or synced (.ptx) transcript formats.
- An in-app editor for generated discovery documents.

</deferred>

---

*Phase: 08-ancillary-discovery-documents*
*Context gathered: 2026-10-08*
