using System.Globalization;
using ReelDiscovery.Models.Discovery;

namespace ReelDiscovery.UserControls;

/// <summary>
/// All Phase 8 copy (08-UI-SPEC S1-S5), pure and unit-tested like GenerationUiText and QaUiText. Strings may carry
/// guide names, model-written titles and violation values; labels showing names set UseMnemonic = false (T-08-26).
/// </summary>
public static class DiscoveryUiText
{
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;
    private static readonly CultureInfo EnUs = CultureInfo.GetCultureInfo("en-US");

    // Attempts per document before it is kept with warnings (D-26)
    private const int Attempts = 3;

    private const string NoUsableContent = "the model returned no usable content";

    /// <summary>
    /// Which estimate-line copy applies (S1 Card 3, D-28).
    /// </summary>
    public enum EstimateCondition { SomeToWrite, NoKey, NothingSelected, AllWritten }

    // --- Header, notice and no-guide state (S1) ---

    public const string StepTitle = "Discovery Documents";
    public const string HeaderTitle = "Discovery Documents";
    public const string HeaderSubtitle = "Deposition excerpts and written discovery written from the case guide and saved as Word files.";

    public const string NoGuideHeading = "The case guide is not ready yet";

    public const string NoGuideBody =
        "Discovery documents are written from the case guide. Finish the Case Guide step, then come back here. You can skip this step with Next.";

    public const string LoadingText = "Loading...";

    // Buttons (Title Case verb + noun; the ellipsis only on the one that opens a dialog)
    public const string GenerateButton = "Generate Documents";
    public const string CancelGenerationButton = "Cancel Generation";
    public const string OpenInWordButton = "Open in Word";
    public const string RegenerateButton = "Regenerate...";
    public const string SaveAgainButton = "Save Again";
    public const string OpenFolderButton = "Open Discovery Folder";
    public const string DiscoveryDocumentsButton = "Discovery Documents";

    public static string Notice(bool memo, bool corpusUsable)
    {
        const string skip = "Optional: click Next to skip. ";
        if (corpusUsable)
            return skip + (memo ? "Interview" : "Deposition") + " exhibits and document admissions cite DocIDs from the generated corpus.";
        return skip + "No corpus has been generated yet, so " + (memo ? "interview" : "deposition")
            + " exhibits are the documents named in the case guide's canonical values.";
    }

    // --- Card 1: Depositions ---

    public const string CapHint = "Up to 6 witnesses per run";

    public const string FactsHeaderTooltip = "Key facts this person acted in or knew about. Witnesses are ranked by this count.";

    public const string NoWitnessesText =
        "No key witnesses to depose. Witnesses are key people in the cast who are not counsel. Mark someone Key on the Case Guide step's Cast tab to add them.";

    public const string PagesLabel = "Testimony pages per excerpt";

    public const string PagesHintValid =
        "10 to 60. The caption, index, signature page, errata sheet and reporter's certificate add about 7 pages.";

    public const string PagesHintInvalid = "Enter a number from 10 to 60.";

    public const string PagesTooltip =
        "Counts only the pages of questions and answers. The transcript is numbered from page 1 and marked EXCERPT on the caption page.";

    public static string DepositionsCaption(DiscoveryVariant variant) => variant switch
    {
        DiscoveryVariant.MemoRegulatory => "Examinations under oath",
        DiscoveryVariant.MemoCriminal or DiscoveryVariant.MemoCivil => "Sworn witness interviews",
        _ => "Depositions"
    };

    public static string SelectionHint(int selected) =>
        selected.ToString("N0", Invariant) + " of " + DiscoverySettings.MaxWitnesses.ToString(Invariant) + " selected";

    public static string SideText(WitnessSide side, bool memo) => side switch
    {
        WitnessSide.Plaintiff => memo ? "Complainant" : "Plaintiff",
        WitnessSide.Defense => memo ? "Subject" : "Defense",
        _ => "Non-party"
    };

    /// <summary>
    /// "{Title}, {organization}. Acts in or knows about {n} key facts." Empty title or organization parts are dropped.
    /// </summary>
    public static string WitnessTooltip(string? title, string? organization, int facts)
    {
        var head = string.Join(", ", new[] { title?.Trim() ?? "", organization?.Trim() ?? "" }.Where(p => p.Length > 0));
        var tail = "Acts in or knows about " + Plural(facts, "key fact", "key facts") + ".";
        return head.Length > 0 ? head + ". " + tail : tail;
    }

    // --- Card 2: Written discovery ---

    /// <summary>
    /// Row label for a kind in a variant (S1 Card 2 table); empty when the kind is hidden in that variant.
    /// The deposition kind returns the Depositions card caption.
    /// </summary>
    public static string KindLabel(DiscoveryKind kind, DiscoveryVariant variant) => (kind, variant) switch
    {
        (DiscoveryKind.Deposition, _) => DepositionsCaption(variant),
        (DiscoveryKind.RequestsForProduction, DiscoveryVariant.MemoCriminal) => "Subpoena rider (documents)",
        (DiscoveryKind.RequestsForProduction, DiscoveryVariant.MemoRegulatory) => "CID document requests",
        (DiscoveryKind.RequestsForProduction, DiscoveryVariant.MemoCivil) => "Pre-suit document requests",
        (DiscoveryKind.RequestsForProduction, _) => "Requests for Production",
        (DiscoveryKind.Interrogatories, DiscoveryVariant.MemoCriminal) => "",
        (DiscoveryKind.Interrogatories, DiscoveryVariant.MemoRegulatory) => "CID interrogatories",
        (DiscoveryKind.Interrogatories, DiscoveryVariant.MemoCivil) => "Written questions",
        (DiscoveryKind.Interrogatories, _) => "Interrogatories",
        (DiscoveryKind.RequestsForAdmission, DiscoveryVariant.MemoCriminal or DiscoveryVariant.MemoRegulatory) => "",
        (DiscoveryKind.RequestsForAdmission, DiscoveryVariant.MemoCivil) => "Requests to admit",
        (DiscoveryKind.RequestsForAdmission, _) => "Requests for Admission",
        _ => ""
    };

    /// <summary>
    /// D-25: criminal memos have no interrogatories or admissions, regulatory memos no admissions.
    /// </summary>
    public static bool KindVisible(DiscoveryKind kind, DiscoveryVariant variant) => KindLabel(kind, variant).Length > 0;

    /// <summary>
    /// Lower-case label for the invalid-count hint ("requests for production", "CID interrogatories").
    /// </summary>
    public static string KindLabelLower(DiscoveryKind kind, DiscoveryVariant variant) =>
        LowerFirst(VisibleOrCivilLabel(kind, variant)).Replace("Production", "production").Replace("Admission", "admission");

    /// <summary>
    /// Sentence-case label for the raised/lowered sentences ("Requests for production").
    /// </summary>
    public static string KindSentenceLabel(DiscoveryKind kind, DiscoveryVariant variant) => UpperFirst(KindLabelLower(kind, variant));

    /// <summary>
    /// Ranges of the visible written-discovery rows, e.g. "Production 5 to 80, interrogatories up to 25 including subparts, admissions up to 50."
    /// </summary>
    public static string CountsHint(DiscoveryVariant variant, IEnumerable<DiscoveryKind> visibleKinds)
    {
        var kinds = (visibleKinds ?? Array.Empty<DiscoveryKind>()).ToHashSet();
        var memo = DiscoveryVariants.IsMemo(variant);
        var parts = new List<string>();

        string Label(DiscoveryKind kind, string civil) => memo ? KindLabelLower(kind, variant) : civil;

        if (kinds.Contains(DiscoveryKind.RequestsForProduction) && KindVisible(DiscoveryKind.RequestsForProduction, variant))
            parts.Add($"{Label(DiscoveryKind.RequestsForProduction, "production")} {DiscoverySettings.MinRfp} to {DiscoverySettings.MaxRfp}");
        if (kinds.Contains(DiscoveryKind.Interrogatories) && KindVisible(DiscoveryKind.Interrogatories, variant))
            parts.Add($"{Label(DiscoveryKind.Interrogatories, "interrogatories")} up to {DiscoverySettings.MaxInterrogatories}"
                + (memo ? "" : " including subparts"));
        if (kinds.Contains(DiscoveryKind.RequestsForAdmission) && KindVisible(DiscoveryKind.RequestsForAdmission, variant))
            parts.Add($"{Label(DiscoveryKind.RequestsForAdmission, "admissions")} up to {DiscoverySettings.MaxRfa}");

        return parts.Count == 0 ? "" : UpperFirst(string.Join(", ", parts)) + ".";
    }

    public static string InvalidCountHint(int min, int max, string labelLower) =>
        $"Enter {min.ToString(Invariant)} to {max.ToString(Invariant)} {labelLower}.";

    /// <summary>
    /// Second hint sentence (leading space) when the count is raised so every issue gets a request (research Pattern 5).
    /// </summary>
    public static string RaisedSentence(string label, int n) =>
        $" {label} raised to {n.ToString("N0", Invariant)} so every issue gets at least one.";

    /// <summary>
    /// Second hint sentence (leading space) when the admissions count is capped at the dated key facts. Never called with 0:
    /// without dated facts the admissions set is unavailable.
    /// </summary>
    public static string LoweredSentence(string label, int n) =>
        $" {label} lowered to {n.ToString("N0", Invariant)} because the guide has {Plural(n, "dated key fact", "dated key facts")}.";

    /// <summary>
    /// Lowered sentence when the cap also counts corpus documents to authenticate (effective = dated facts + documents).
    /// </summary>
    public static string LoweredSentence(string label, int effective, int datedFacts)
    {
        var documents = effective - datedFacts;
        if (documents <= 0)
            return LoweredSentence(label, effective);
        return $" {label} lowered to {effective.ToString("N0", Invariant)} because the guide has "
            + $"{Plural(datedFacts, "dated key fact", "dated key facts")} and "
            + $"{Plural(documents, "written document to authenticate", "written documents to authenticate")}.";
    }

    public static string MemoNote(DiscoveryVariant variant) => variant switch
    {
        DiscoveryVariant.MemoCriminal => "Interrogatories and requests for admission are not used in a criminal investigation.",
        DiscoveryVariant.MemoRegulatory => "Requests for admission are not used in a regulatory investigation.",
        _ => ""
    };

    public static (string By, string On) PartyLabels(bool memo) =>
        memo ? ("Requested by", "Directed to") : ("Served by", "Served on");

    public static string PartyItem(string name, string role) => $"{name} ({role})";

    public const string SamePartyHint = "Choose two different parties.";
    public const string MissingPartyHint = "The saved party is no longer in the core document, so the default is used.";

    // --- Card 3: Documents ---

    public const string DocumentsCaption = "Documents";

    public const string EmptyDocumentsText = "Nothing selected yet. Check a witness or a written discovery set to add it here.";

    /// <summary>
    /// "${c:0.00} spent"; empty at $0 (the hint is hidden).
    /// </summary>
    public static string SpentHint(decimal spent) => spent <= 0m ? "" : Dollars(spent) + " spent";

    /// <summary>
    /// Grid title: "Deposition of {name}" (memo "Interview of" / "EUO of") or "{set label} ({n})".
    /// A saved set whose kind is hidden in the current variant keeps its civil label.
    /// </summary>
    public static string ShortTitle(DiscoveryKind kind, DiscoveryVariant variant, string? witnessName, int count)
    {
        if (kind == DiscoveryKind.Deposition)
        {
            var prefix = variant switch
            {
                DiscoveryVariant.MemoRegulatory => "EUO of",
                DiscoveryVariant.MemoCriminal or DiscoveryVariant.MemoCivil => "Interview of",
                _ => "Deposition of"
            };
            return prefix + " " + (witnessName?.Trim() ?? "");
        }

        return $"{VisibleOrCivilLabel(kind, variant)} ({count.ToString("N0", Invariant)})";
    }

    /// <summary>
    /// Row state with UI-SPEC precedence: Writing, Failed, Not saved, File missing, Out of date, Warnings, Generated;
    /// Not generated when nothing is saved.
    /// </summary>
    public static DiscoveryRowState ResolveState(bool writing, DiscoveryDocument? doc, StaleSource stale, bool fileExists)
    {
        if (writing)
            return DiscoveryRowState.Writing;
        if (doc == null)
            return DiscoveryRowState.NotGenerated;
        if (!string.IsNullOrWhiteSpace(doc.FailureReason) || doc.Status == DiscoveryStatus.Failed)
            return DiscoveryRowState.Failed;
        if (!doc.HasContent)
            return DiscoveryRowState.NotGenerated;
        if (!doc.FileSaved)
            return DiscoveryRowState.NotSaved;
        if (!fileExists)
            return DiscoveryRowState.FileMissing;
        if (stale != StaleSource.None)
            return DiscoveryRowState.OutOfDate;
        if (doc.Status == DiscoveryStatus.GeneratedWithWarnings || doc.Warnings.Count > 0)
            return DiscoveryRowState.Warnings;
        return DiscoveryRowState.Generated;
    }

    public static string RowStatusCell(DiscoveryRowState state) => state switch
    {
        DiscoveryRowState.Writing => "Writing...",
        DiscoveryRowState.Generated => "Generated",
        DiscoveryRowState.Warnings => "Warnings",
        DiscoveryRowState.Failed => "Failed",
        DiscoveryRowState.OutOfDate => "Out of date",
        DiscoveryRowState.NotSaved => "Not saved",
        DiscoveryRowState.FileMissing => "File missing",
        _ => "Not generated"
    };

    /// <summary>
    /// Full status for the selected-row detail line and the row tooltip head (S1 status table).
    /// </summary>
    public static string RowStatusFull(DiscoveryRowInfo row)
    {
        switch (row.State)
        {
            case DiscoveryRowState.Writing:
                return row.PartCount > 1
                    ? $"Writing: part {Math.Max(1, row.Part).ToString(Invariant)} of {row.PartCount.ToString(Invariant)}..."
                    : "Writing...";

            case DiscoveryRowState.Generated:
            {
                var stamp = row.GeneratedUtc is { } utc ? " " + ToLocal(utc).ToString("MMM d, h:mm tt", EnUs) : "";
                var size = row.Kind == DiscoveryKind.Deposition
                    ? Plural(row.TestimonyPages, "page", "pages")
                    : Plural(row.RequestCount, "request", "requests");
                return "Generated" + stamp + " · " + size;
            }

            case DiscoveryRowState.Warnings:
            {
                var text = $"Generated with warnings: {Plural(row.Warnings.Count, "check", "checks")} still failed after {Attempts} attempts.";
                return row.Warnings.Count > 0 ? text + " " + ViolationText(row.Warnings[0]) : text;
            }

            case DiscoveryRowState.Failed:
                return "Failed: " + Reason(row.FailureReason) + (row.PreviousKept
                    ? ". The previous version is kept."
                    : ". Click Generate Documents or Regenerate... to try again.");

            case DiscoveryRowState.OutOfDate:
                return $"Out of date with the case guide: {StaleWhat(row.Stale)} changed after this document was written. Click Regenerate... to write it again.";

            case DiscoveryRowState.NotSaved:
                return $"Not saved: {row.FileName} is open in Word. Close it, then click Save Again.";

            case DiscoveryRowState.FileMissing:
                return $"File missing: {row.FileName} is not in the Discovery folder. Click Save Again to write it from the saved content.";

            default:
                return "Not generated. Generate Documents writes it.";
        }
    }

    /// <summary>
    /// Short title, full status, then (after a blank line) every violation and planner note as bullets.
    /// </summary>
    public static string RowTooltip(DiscoveryRowInfo row)
    {
        var lines = new List<string> { row.ShortTitle, RowStatusFull(row) };
        var bullets = new List<string>();
        if (row.State == DiscoveryRowState.Warnings)
            bullets.AddRange(row.Warnings.Select(v => "• " + ViolationText(v)));
        bullets.AddRange(row.PlannerNotes.Where(n => !string.IsNullOrWhiteSpace(n)).Select(n => "• " + n.Trim()));
        if (bullets.Count > 0)
        {
            lines.Add("");
            lines.AddRange(bullets);
        }
        return string.Join(Environment.NewLine, lines);
    }

    /// <summary>
    /// Validator rule text (UI-SPEC D1-D8 table).
    /// </summary>
    public static string ViolationText(DiscoveryViolation v) => v.Rule switch
    {
        DiscoveryRule.D1ShortPart =>
            $"part {N(v.Number)} came back with {N(v.Actual)} of about {N(v.Expected)} turns",
        DiscoveryRule.D1ExhibitNotShown => $"Exhibit {N(v.Number)} is never shown to the witness",
        DiscoveryRule.D2UnknownPerson => $"names \"{v.Value}\", who is not in the cast",
        DiscoveryRule.D3AmountVariant => $"mentions \"{v.Value}\", which is close to but not the case amount {v.Canonical}",
        DiscoveryRule.D3UnknownIdentifier => $"uses identifier \"{v.Value}\", which is not in the case guide",
        DiscoveryRule.D4MissingRequest => $"request {N(v.Number)} is missing",
        DiscoveryRule.D4ExtraRequest => "returned a request that was not asked for",
        DiscoveryRule.D5MissingFactDetail => $"admission {N(v.Number)} leaves out {v.Value} from its key fact",
        DiscoveryRule.D6AfterDepositionDate => $"mentions {v.Value}, after the deposition date",
        DiscoveryRule.D7Subparts => $"interrogatory {N(v.Number)} has lettered or numbered parts",
        DiscoveryRule.D8RealEntityName => $"uses the real-world name \"{v.Value}\" instead of \"{v.Canonical}\"",
        _ => "failed a consistency check"
    };

    /// <summary>
    /// The {what} of the out-of-date status: the first case source that changed (D-27).
    /// </summary>
    public static string StaleWhat(StaleSource source) => source switch
    {
        StaleSource.Core => "the core document",
        StaleSource.Caption => "the caption or counsel",
        StaleSource.CorpusPlan => "the corpus plan",
        _ => "it"
    };

    // --- Estimate (D-28) ---

    public static string EstimateLine(EstimateCondition condition, decimal cost, int documents, string? modelName) => condition switch
    {
        EstimateCondition.NoKey => "Test an API key on the API Configuration step first.",
        EstimateCondition.NothingSelected => "Select a witness or a written discovery set to see the cost.",
        EstimateCondition.AllWritten => "Every selected document is written. Select a row and click Regenerate... to write it again.",
        _ => $"Estimated cost {Approx(cost)}, ±50%, for {Plural(documents, "document", "documents")}"
            + (string.IsNullOrWhiteSpace(modelName) ? "" : " on " + modelName.Trim())
            + ", including prompt caching. Each document is saved to the Discovery folder as soon as it is finished."
    };

    public static string EstimateTooltipLine(string shortTitle, decimal cost) => $"{shortTitle}: {Approx(cost)}";

    // --- Status line ---

    public const string StatusStopping = "Stopping... finished documents are saved.";

    public static string StatusRunning(int index, int total, string shortTitle, int part, int parts) =>
        $"Writing {index.ToString("N0", Invariant)} of {Plural(total, "document", "documents")}: {shortTitle}"
        + (parts > 1 ? $", part {part.ToString(Invariant)} of {parts.ToString(Invariant)}" : "") + "...";

    public static string StatusDone(int documents) =>
        $"Done: {Plural(documents, "document", "documents")} saved to the Discovery folder.";

    public static string StatusDoneWithProblems(int warnings, int failed, int notSaved)
    {
        var clauses = new List<string>();
        if (warnings > 0)
            clauses.Add(Plural(warnings, "document", "documents") + " with warnings");
        if (failed > 0)
            clauses.Add(Plural(failed, "failed document", "failed documents"));
        if (notSaved > 0)
            clauses.Add(Plural(notSaved, "file", "files") + " not saved");
        return $"Done with {JoinClauses(clauses)}. Select a row to see what was flagged.";
    }

    public static string StatusCancelled(int done, int total) =>
        $"Cancelled. {done.ToString("N0", Invariant)} of {Plural(total, "document", "documents")} saved. Click Generate Documents to write the rest.";

    public static string StatusFatal(string message) =>
        $"Generation stopped: {TrimPeriod(message)}. Finished documents are saved. Fix the problem on the API Configuration step, then click Generate Documents.";

    public static string OpenFailed(string fileName, string message) =>
        $"Could not open {fileName}: {TrimPeriod(message)}. Click Open Discovery Folder to open it from Explorer.";

    public static string StillOpen(string fileName) => $"{fileName} is still open in Word. Close it, then click Save Again.";

    public static string Saved(string fileName) => $"Saved {fileName}.";

    // --- Confirmations (S3) ---

    public static (string Title, string Text) CD1CancelGeneration() =>
        ("Cancel Generation",
            "Stop writing discovery documents? Finished documents are saved. The rest stay Not generated, and Generate Documents writes them later.");

    public static (string Title, string Text) CD2CloseDuringRun() =>
        ("Stop Generating?", "Discovery documents are still being written. Close and stop? Finished documents are saved.");

    // --- S2: Regenerate dialog ---

    public const string RegenerateTitle = "Regenerate Document";
    public const string GuidanceCaption = "Guidance (optional)";

    public static string RegenerateHeadline(string shortTitle) => $"Regenerate {shortTitle}?";

    public static string RegenerateBody(string fileName, bool isDeposition) =>
        $"This writes the document again from the current case guide and replaces {fileName} in the Discovery folder. Nothing else changes."
        + (isDeposition ? " Exhibit numbers stay the same." : "");

    /// <summary>
    /// "Uses the current settings: {n} testimony pages, deposed {MMMM d, yyyy}." The date is yyyy-MM-dd; an empty date is dropped.
    /// </summary>
    public static string SettingsLineDeposition(int pages, string? yyyyMmDd)
    {
        var date = "";
        if (!string.IsNullOrWhiteSpace(yyyyMmDd))
        {
            date = DateTime.TryParseExact(yyyyMmDd.Trim(), "yyyy-MM-dd", Invariant, DateTimeStyles.None, out var parsed)
                ? parsed.ToString("MMMM d, yyyy", EnUs)
                : yyyyMmDd.Trim();
        }
        return "Uses the current settings: " + Plural(pages, "testimony page", "testimony pages")
            + (date.Length > 0 ? ", deposed " + date : "") + ".";
    }

    public static string SettingsLineWritten(int requests, string by, string on, bool memo) =>
        EndSentence("Uses the current settings: " + Plural(requests, "request", "requests")
            + (memo ? $", requested by {by} from {on}" : $", served by {by} on {on}"));

    // A party such as "Harmon Construction Group, Inc." already ends the sentence
    private static string EndSentence(string text) => text.EndsWith('.') ? text : text + ".";

    public static string GuidancePlaceholder(bool isDeposition) => isDeposition
        ? "For example: spend more time on the change order and less on scheduling."
        : "For example: focus more requests on the payment dispute.";

    public static string FileOpenWarning(string fileName) =>
        $"{fileName} is open in Word. Close it before you regenerate, or the new version cannot be saved.";

    public static string DialogEstimate(decimal cost) => $"Estimated cost {Approx(cost)}, ±50%.";

    // --- S4: Case Summary row ---

    public const string SummaryTitle = "Discovery documents";

    public const string SummaryLine2 =
        "Deposition excerpts and written discovery written from the case guide, saved as Word files in the Discovery folder. Not part of the answer key.";

    /// <summary>
    /// The Discovery documents row (S4): saved count, last generated date and problem clauses, or "Not generated".
    /// ShowPrimary = Open Discovery Folder (folder holds a document), ShowSecondary = Discovery Documents (always).
    /// </summary>
    public static SummaryRowContent SummaryRow(IReadOnlyList<DiscoveryRowInfo> rows, bool folderHasDocuments)
    {
        rows ??= Array.Empty<DiscoveryRowInfo>();
        var saved = rows.Where(r => IsSaved(r.State)).ToList();
        int Count(DiscoveryRowState state) => rows.Count(r => r.State == state);
        var w = Count(DiscoveryRowState.Warnings);
        var o = Count(DiscoveryRowState.OutOfDate);
        var s = Count(DiscoveryRowState.NotSaved);
        var m = Count(DiscoveryRowState.FileMissing);
        var f = Count(DiscoveryRowState.Failed);

        var clauses = new List<string>();
        if (w > 0) clauses.Add(N(w) + " with warnings");
        if (o > 0) clauses.Add(N(o) + " out of date");
        if (s > 0) clauses.Add(N(s) + " not saved");
        if (m > 0) clauses.Add(Plural(m, "file missing", "files missing"));
        if (f > 0) clauses.Add(N(f) + " failed");
        var suffix = string.Concat(clauses.Select(c => " · " + c));

        var content = new SummaryRowContent
        {
            Line2 = SummaryLine2,
            ShowPrimary = folderHasDocuments,
            ShowSecondary = true
        };

        string line3;
        if (saved.Count == 0)
        {
            // Failed-only rows count only in the failed clause; with no saved version there is no date to show
            line3 = "Not generated" + suffix;
        }
        else
        {
            var last = saved.Where(r => r.GeneratedUtc.HasValue).Select(r => r.GeneratedUtc!.Value).DefaultIfEmpty().Max();
            var date = last == default ? "" : " · last generated " + ToLocal(last).ToString("MMM d, yyyy", EnUs);
            line3 = Plural(saved.Count, "document", "documents") + date + suffix;
        }

        var tone = f > 0 ? SummaryTone.Danger
            : w + o + s + m > 0 ? SummaryTone.Warning
            : saved.Count > 0 ? SummaryTone.Success
            : SummaryTone.Muted;

        content.Line3 = line3;
        content.Line3Tone = tone;
        content.DotTone = tone;

        var listed = rows.Where(r => r.State != DiscoveryRowState.NotGenerated).ToList();
        content.Line3Tooltip = listed.Count == 0
            ? ""
            : string.Join(Environment.NewLine,
                new[] { line3, "" }.Concat(listed.Select(r => $"{r.ShortTitle}: {RowStatusCell(r.State)}")));
        return content;
    }

    // --- Shared formatting ---

    /// <summary>
    /// "{n:N0} {word}" with the singular only for exactly 1 (CaseGuideUiText.Plural).
    /// </summary>
    public static string Plural(int n, string singular, string plural) => CaseGuideUiText.Plural(n, singular, plural);

    private static bool IsSaved(DiscoveryRowState state) => state is DiscoveryRowState.Generated or DiscoveryRowState.Warnings
        or DiscoveryRowState.OutOfDate or DiscoveryRowState.NotSaved or DiscoveryRowState.FileMissing;

    private static string N(int n) => n.ToString("N0", Invariant);

    private static string Dollars(decimal amount) => "$" + amount.ToString("0.00", Invariant);

    private static string Approx(decimal amount) => "≈" + Dollars(amount);

    private static DateTime ToLocal(DateTime utc) =>
        (utc.Kind == DateTimeKind.Local ? utc : DateTime.SpecifyKind(utc, DateTimeKind.Utc).ToLocalTime());

    private static string Reason(string? reason)
    {
        var text = TrimPeriod(reason);
        return text.Length > 0 ? text : NoUsableContent;
    }

    private static string TrimPeriod(string? text) => (text ?? "").Trim().TrimEnd('.').TrimEnd();

    private static string VisibleOrCivilLabel(DiscoveryKind kind, DiscoveryVariant variant)
    {
        var label = KindLabel(kind, variant);
        return label.Length > 0 ? label : KindLabel(kind, DiscoveryVariant.Civil);
    }

    // Acronym-led labels ("CID interrogatories") keep their case
    private static string LowerFirst(string text) =>
        text.Length == 0 || (text.Length > 1 && char.IsUpper(text[1])) ? text : char.ToLowerInvariant(text[0]) + text[1..];

    private static string UpperFirst(string text) =>
        text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text[1..];

    // "a", "a and b", "a, b and c"
    private static string JoinClauses(IReadOnlyList<string> clauses) => clauses.Count switch
    {
        0 => "",
        1 => clauses[0],
        _ => string.Join(", ", clauses.Take(clauses.Count - 1)) + " and " + clauses[^1]
    };
}
