using System.Globalization;
using System.Reflection;
using ReelDiscovery.Models.Discovery;
using ReelDiscovery.UserControls;

namespace ReelDiscovery.Tests.Discovery;

public class DiscoveryUiTextTests
{
    private static readonly CultureInfo EnUs = CultureInfo.GetCultureInfo("en-US");
    private static readonly DateTime Utc = new(2026, 10, 8, 12, 0, 0, DateTimeKind.Utc);
    private static readonly string[] BannedWords = { "fictional", "evidence" };

    private static string LocalDay(DateTime utc) => utc.ToLocalTime().ToString("MMM d, yyyy", EnUs);

    private static string LocalStamp(DateTime utc) => utc.ToLocalTime().ToString("MMM d, h:mm tt", EnUs);

    private static void AssertClean(string text)
    {
        foreach (var word in BannedWords)
            Assert.DoesNotContain(word, text, StringComparison.OrdinalIgnoreCase);
    }

    private static DiscoveryDocument Doc(
        DiscoveryStatus status = DiscoveryStatus.Generated, bool saved = true, string failure = "", int warnings = 0)
    {
        var doc = new DiscoveryDocument
        {
            Key = "RFP",
            Kind = DiscoveryKind.RequestsForProduction,
            Status = status,
            FileSaved = saved,
            FailureReason = failure,
            FileName = "Case - Requests for Production.docx",
            GeneratedUtc = Utc,
            RequestCount = 25,
            Written = new WrittenDiscoveryContent()
        };
        for (var i = 0; i < warnings; i++)
            doc.Warnings.Add(new DiscoveryViolation { Rule = DiscoveryRule.D7Subparts, Number = i + 1 });
        return doc;
    }

    private static DiscoveryRowInfo Row(DiscoveryRowState state, string title = "Requests for Production (25)") =>
        new()
        {
            State = state,
            ShortTitle = title,
            Kind = DiscoveryKind.RequestsForProduction,
            GeneratedUtc = Utc,
            RequestCount = 25,
            FileName = "Case - Requests for Production.docx"
        };

    // --- Plurals ---

    [Theory]
    [InlineData(0, "document", "documents", "0 documents")]
    [InlineData(1, "document", "documents", "1 document")]
    [InlineData(2, "document", "documents", "2 documents")]
    [InlineData(1000, "document", "documents", "1,000 documents")]
    [InlineData(0, "failed document", "failed documents", "0 failed documents")]
    [InlineData(1, "failed document", "failed documents", "1 failed document")]
    [InlineData(2, "failed document", "failed documents", "2 failed documents")]
    [InlineData(1000, "failed document", "failed documents", "1,000 failed documents")]
    [InlineData(0, "file", "files", "0 files")]
    [InlineData(1, "file", "files", "1 file")]
    [InlineData(2, "file", "files", "2 files")]
    [InlineData(1000, "file", "files", "1,000 files")]
    [InlineData(0, "key fact", "key facts", "0 key facts")]
    [InlineData(1, "key fact", "key facts", "1 key fact")]
    [InlineData(2, "key fact", "key facts", "2 key facts")]
    [InlineData(1000, "key fact", "key facts", "1,000 key facts")]
    [InlineData(0, "check", "checks", "0 checks")]
    [InlineData(1, "check", "checks", "1 check")]
    [InlineData(2, "check", "checks", "2 checks")]
    [InlineData(1000, "check", "checks", "1,000 checks")]
    [InlineData(0, "page", "pages", "0 pages")]
    [InlineData(1, "page", "pages", "1 page")]
    [InlineData(2, "page", "pages", "2 pages")]
    [InlineData(1000, "page", "pages", "1,000 pages")]
    [InlineData(0, "testimony page", "testimony pages", "0 testimony pages")]
    [InlineData(1, "testimony page", "testimony pages", "1 testimony page")]
    [InlineData(2, "testimony page", "testimony pages", "2 testimony pages")]
    [InlineData(1000, "testimony page", "testimony pages", "1,000 testimony pages")]
    [InlineData(0, "request", "requests", "0 requests")]
    [InlineData(1, "request", "requests", "1 request")]
    [InlineData(2, "request", "requests", "2 requests")]
    [InlineData(1000, "request", "requests", "1,000 requests")]
    public void Plural_CoversEveryTemplate(int n, string singular, string plural, string expected)
    {
        Assert.Equal(expected, DiscoveryUiText.Plural(n, singular, plural));
    }

    [Theory]
    [InlineData(1, "1 document")]
    [InlineData(1000, "1,000 documents")]
    public void StatusDone_UsesPlural(int n, string count)
    {
        Assert.Equal($"Done: {count} saved to the Discovery folder.", DiscoveryUiText.StatusDone(n));
    }

    // --- Header, notice and empty states (S1) ---

    [Fact]
    public void Header_AndNoGuide()
    {
        Assert.Equal("Discovery Documents", DiscoveryUiText.StepTitle);
        Assert.Equal("Discovery Documents", DiscoveryUiText.HeaderTitle);
        Assert.Equal("Deposition excerpts and written discovery written from the case guide and saved as Word files.",
            DiscoveryUiText.HeaderSubtitle);
        Assert.Equal("The case guide is not ready yet", DiscoveryUiText.NoGuideHeading);
        Assert.Equal(
            "Discovery documents are written from the case guide. Finish the Case Guide step, then come back here. You can skip this step with Next.",
            DiscoveryUiText.NoGuideBody);
    }

    [Theory]
    [InlineData(false, true, "Optional: click Next to skip. Deposition exhibits and document admissions cite DocIDs from the generated corpus.")]
    [InlineData(false, false, "Optional: click Next to skip. No corpus has been generated yet, so deposition exhibits are the documents named in the case guide's canonical values.")]
    [InlineData(true, true, "Optional: click Next to skip. Interview exhibits and document admissions cite DocIDs from the generated corpus.")]
    [InlineData(true, false, "Optional: click Next to skip. No corpus has been generated yet, so interview exhibits are the documents named in the case guide's canonical values.")]
    public void Notice_PerVariantAndCorpus(bool memo, bool corpusUsable, string expected)
    {
        Assert.Equal(expected, DiscoveryUiText.Notice(memo, corpusUsable));
    }

    [Fact]
    public void EmptyTexts()
    {
        Assert.Equal(
            "No key witnesses to depose. Witnesses are key people in the cast who are not counsel. Mark someone Key on the Case Guide step's Cast tab to add them.",
            DiscoveryUiText.NoWitnessesText);
        Assert.Equal("Nothing selected yet. Check a witness or a written discovery set to add it here.", DiscoveryUiText.EmptyDocumentsText);
    }

    // --- Card 1: Depositions ---

    [Theory]
    [InlineData(DiscoveryVariant.Civil, "Depositions")]
    [InlineData(DiscoveryVariant.MemoCriminal, "Sworn witness interviews")]
    [InlineData(DiscoveryVariant.MemoCivil, "Sworn witness interviews")]
    [InlineData(DiscoveryVariant.MemoRegulatory, "Examinations under oath")]
    public void DepositionsCaption_PerVariant(DiscoveryVariant variant, string expected)
    {
        Assert.Equal(expected, DiscoveryUiText.DepositionsCaption(variant));
    }

    [Fact]
    public void SelectionHint_AndCap()
    {
        Assert.Equal("0 of 6 selected", DiscoveryUiText.SelectionHint(0));
        Assert.Equal("3 of 6 selected", DiscoveryUiText.SelectionHint(3));
        Assert.Equal("Up to 6 witnesses per run", DiscoveryUiText.CapHint);
    }

    [Theory]
    [InlineData(WitnessSide.Plaintiff, false, "Plaintiff")]
    [InlineData(WitnessSide.Defense, false, "Defense")]
    [InlineData(WitnessSide.NonParty, false, "Non-party")]
    [InlineData(WitnessSide.Plaintiff, true, "Complainant")]
    [InlineData(WitnessSide.Defense, true, "Subject")]
    [InlineData(WitnessSide.NonParty, true, "Non-party")]
    public void SideText_PerVariant(WitnessSide side, bool memo, string expected)
    {
        Assert.Equal(expected, DiscoveryUiText.SideText(side, memo));
    }

    [Fact]
    public void WitnessTooltips()
    {
        Assert.Equal("Project Manager, Harmon Glass. Acts in or knows about 1 key fact.",
            DiscoveryUiText.WitnessTooltip("Project Manager", "Harmon Glass", 1));
        Assert.Equal("Project Manager, Harmon Glass. Acts in or knows about 12 key facts.",
            DiscoveryUiText.WitnessTooltip("Project Manager", "Harmon Glass", 12));
        Assert.Equal("Project Manager. Acts in or knows about 2 key facts.", DiscoveryUiText.WitnessTooltip("Project Manager", "", 2));
        Assert.Equal("Key facts this person acted in or knew about. Witnesses are ranked by this count.", DiscoveryUiText.FactsHeaderTooltip);
    }

    [Fact]
    public void PagesCopy()
    {
        Assert.Equal("Testimony pages per excerpt", DiscoveryUiText.PagesLabel);
        Assert.Equal("10 to 60. The caption, index, signature page, errata sheet and reporter's certificate add about 7 pages.",
            DiscoveryUiText.PagesHintValid);
        Assert.Equal("Enter a number from 10 to 60.", DiscoveryUiText.PagesHintInvalid);
        Assert.Equal("Counts only the pages of questions and answers. The transcript is numbered from page 1 and marked EXCERPT on the caption page.",
            DiscoveryUiText.PagesTooltip);
    }

    // --- Card 2: Written discovery ---

    [Theory]
    [InlineData(DiscoveryKind.RequestsForProduction, DiscoveryVariant.Civil, "Requests for Production")]
    [InlineData(DiscoveryKind.Interrogatories, DiscoveryVariant.Civil, "Interrogatories")]
    [InlineData(DiscoveryKind.RequestsForAdmission, DiscoveryVariant.Civil, "Requests for Admission")]
    [InlineData(DiscoveryKind.RequestsForProduction, DiscoveryVariant.MemoCriminal, "Subpoena rider (documents)")]
    [InlineData(DiscoveryKind.Interrogatories, DiscoveryVariant.MemoCriminal, "")]
    [InlineData(DiscoveryKind.RequestsForAdmission, DiscoveryVariant.MemoCriminal, "")]
    [InlineData(DiscoveryKind.RequestsForProduction, DiscoveryVariant.MemoRegulatory, "CID document requests")]
    [InlineData(DiscoveryKind.Interrogatories, DiscoveryVariant.MemoRegulatory, "CID interrogatories")]
    [InlineData(DiscoveryKind.RequestsForAdmission, DiscoveryVariant.MemoRegulatory, "")]
    [InlineData(DiscoveryKind.RequestsForProduction, DiscoveryVariant.MemoCivil, "Pre-suit document requests")]
    [InlineData(DiscoveryKind.Interrogatories, DiscoveryVariant.MemoCivil, "Written questions")]
    [InlineData(DiscoveryKind.RequestsForAdmission, DiscoveryVariant.MemoCivil, "Requests to admit")]
    public void KindLabel_PerVariant(DiscoveryKind kind, DiscoveryVariant variant, string expected)
    {
        Assert.Equal(expected, DiscoveryUiText.KindLabel(kind, variant));
        Assert.Equal(expected.Length > 0, DiscoveryUiText.KindVisible(kind, variant));
    }

    [Fact]
    public void CountsHint_PerVariant()
    {
        var all = new[] { DiscoveryKind.RequestsForProduction, DiscoveryKind.Interrogatories, DiscoveryKind.RequestsForAdmission };
        Assert.Equal("Production 5 to 80, interrogatories up to 25 including subparts, admissions up to 50.",
            DiscoveryUiText.CountsHint(DiscoveryVariant.Civil, all));
        Assert.Equal("CID document requests 5 to 80, CID interrogatories up to 25.",
            DiscoveryUiText.CountsHint(DiscoveryVariant.MemoRegulatory, all));
        Assert.Equal("Subpoena rider (documents) 5 to 80.", DiscoveryUiText.CountsHint(DiscoveryVariant.MemoCriminal, all));
        Assert.Equal("Pre-suit document requests 5 to 80, written questions up to 25, requests to admit up to 50.",
            DiscoveryUiText.CountsHint(DiscoveryVariant.MemoCivil, all));
        Assert.Equal("", DiscoveryUiText.CountsHint(DiscoveryVariant.Civil, Array.Empty<DiscoveryKind>()));
    }

    [Fact]
    public void CountHints_InvalidRaisedLowered()
    {
        Assert.Equal("Enter 5 to 80 requests for production.", DiscoveryUiText.InvalidCountHint(5, 80, "requests for production"));
        Assert.Equal(" Requests for production raised to 6 so every issue gets at least one.",
            DiscoveryUiText.RaisedSentence("Requests for production", 6));
        Assert.Equal(" Requests for admission lowered to 9 because the guide has 9 dated key facts.",
            DiscoveryUiText.LoweredSentence("Requests for admission", 9));
        Assert.Equal(" Requests for admission lowered to 1 because the guide has 1 dated key fact.",
            DiscoveryUiText.LoweredSentence("Requests for admission", 1));
        Assert.Equal(" Requests for admission lowered to 9 because the guide has 7 dated key facts and 2 written documents to authenticate.",
            DiscoveryUiText.LoweredSentence("Requests for admission", 9, 7));
        Assert.Equal(" Requests for admission lowered to 8 because the guide has 7 dated key facts and 1 written document to authenticate.",
            DiscoveryUiText.LoweredSentence("Requests for admission", 8, 7));
    }

    [Theory]
    [InlineData(DiscoveryKind.RequestsForProduction, DiscoveryVariant.Civil, "requests for production", "Requests for production")]
    [InlineData(DiscoveryKind.Interrogatories, DiscoveryVariant.MemoRegulatory, "CID interrogatories", "CID interrogatories")]
    [InlineData(DiscoveryKind.RequestsForAdmission, DiscoveryVariant.MemoCivil, "requests to admit", "Requests to admit")]
    public void KindLabelForms(DiscoveryKind kind, DiscoveryVariant variant, string lower, string sentence)
    {
        Assert.Equal(lower, DiscoveryUiText.KindLabelLower(kind, variant));
        Assert.Equal(sentence, DiscoveryUiText.KindSentenceLabel(kind, variant));
    }

    [Theory]
    [InlineData(DiscoveryVariant.MemoCriminal, "Interrogatories and requests for admission are not used in a criminal investigation.")]
    [InlineData(DiscoveryVariant.MemoRegulatory, "Requests for admission are not used in a regulatory investigation.")]
    [InlineData(DiscoveryVariant.MemoCivil, "")]
    [InlineData(DiscoveryVariant.Civil, "")]
    public void MemoNote_PerVariant(DiscoveryVariant variant, string expected)
    {
        Assert.Equal(expected, DiscoveryUiText.MemoNote(variant));
    }

    [Fact]
    public void PartyCopy()
    {
        Assert.Equal(("Served by", "Served on"), DiscoveryUiText.PartyLabels(false));
        Assert.Equal(("Requested by", "Directed to"), DiscoveryUiText.PartyLabels(true));
        Assert.Equal("Brightline Windows LLC (Plaintiff)", DiscoveryUiText.PartyItem("Brightline Windows LLC", "Plaintiff"));
        Assert.Equal("Choose two different parties.", DiscoveryUiText.SamePartyHint);
        Assert.Equal("The saved party is no longer in the core document, so the default is used.", DiscoveryUiText.MissingPartyHint);
    }

    // --- Card 3: Documents ---

    [Fact]
    public void DocumentsCaption_AndSpent()
    {
        Assert.Equal("Documents", DiscoveryUiText.DocumentsCaption);
        Assert.Equal("", DiscoveryUiText.SpentHint(0m));
        Assert.Equal("$1.25 spent", DiscoveryUiText.SpentHint(1.249m));
    }

    [Theory]
    [InlineData(DiscoveryKind.Deposition, DiscoveryVariant.Civil, "Deposition of Ann Lee")]
    [InlineData(DiscoveryKind.Deposition, DiscoveryVariant.MemoCriminal, "Interview of Ann Lee")]
    [InlineData(DiscoveryKind.Deposition, DiscoveryVariant.MemoCivil, "Interview of Ann Lee")]
    [InlineData(DiscoveryKind.Deposition, DiscoveryVariant.MemoRegulatory, "EUO of Ann Lee")]
    [InlineData(DiscoveryKind.RequestsForProduction, DiscoveryVariant.Civil, "Requests for Production (25)")]
    [InlineData(DiscoveryKind.Interrogatories, DiscoveryVariant.Civil, "Interrogatories (18)")]
    [InlineData(DiscoveryKind.RequestsForAdmission, DiscoveryVariant.Civil, "Requests for Admission (20)")]
    [InlineData(DiscoveryKind.RequestsForProduction, DiscoveryVariant.MemoCriminal, "Subpoena rider (documents) (25)")]
    [InlineData(DiscoveryKind.Interrogatories, DiscoveryVariant.MemoRegulatory, "CID interrogatories (18)")]
    [InlineData(DiscoveryKind.RequestsForAdmission, DiscoveryVariant.MemoCivil, "Requests to admit (20)")]
    // A saved set whose kind is hidden in the current variant keeps the civil label
    [InlineData(DiscoveryKind.RequestsForAdmission, DiscoveryVariant.MemoCriminal, "Requests for Admission (20)")]
    public void ShortTitle_PerKindAndVariant(DiscoveryKind kind, DiscoveryVariant variant, string expected)
    {
        var count = kind switch
        {
            DiscoveryKind.RequestsForProduction => 25,
            DiscoveryKind.Interrogatories => 18,
            _ => 20
        };
        Assert.Equal(expected, DiscoveryUiText.ShortTitle(kind, variant, "Ann Lee", count));
    }

    [Fact]
    public void ResolveState_Precedence()
    {
        // Writing beats everything
        Assert.Equal(DiscoveryRowState.Writing,
            DiscoveryUiText.ResolveState(true, Doc(failure: "the request timed out", saved: false), StaleSource.Guide, false));
        Assert.Equal(DiscoveryRowState.Writing, DiscoveryUiText.ResolveState(true, null, StaleSource.None, false));
        // Nothing saved
        Assert.Equal(DiscoveryRowState.NotGenerated, DiscoveryUiText.ResolveState(false, null, StaleSource.None, false));
        // Failed beats Not saved
        Assert.Equal(DiscoveryRowState.Failed,
            DiscoveryUiText.ResolveState(false, Doc(failure: "the request timed out", saved: false), StaleSource.Guide, false));
        Assert.Equal(DiscoveryRowState.Failed,
            DiscoveryUiText.ResolveState(false, Doc(status: DiscoveryStatus.Failed), StaleSource.None, true));
        // Not saved beats File missing
        Assert.Equal(DiscoveryRowState.NotSaved, DiscoveryUiText.ResolveState(false, Doc(saved: false), StaleSource.Guide, false));
        // File missing beats Out of date
        Assert.Equal(DiscoveryRowState.FileMissing, DiscoveryUiText.ResolveState(false, Doc(), StaleSource.Guide, false));
        // Out of date beats Warnings
        Assert.Equal(DiscoveryRowState.OutOfDate,
            DiscoveryUiText.ResolveState(false, Doc(DiscoveryStatus.GeneratedWithWarnings, warnings: 2), StaleSource.Core, true));
        // Warnings beats Generated
        Assert.Equal(DiscoveryRowState.Warnings,
            DiscoveryUiText.ResolveState(false, Doc(DiscoveryStatus.GeneratedWithWarnings, warnings: 2), StaleSource.None, true));
        Assert.Equal(DiscoveryRowState.Generated, DiscoveryUiText.ResolveState(false, Doc(), StaleSource.None, true));
    }

    [Fact]
    public void ResolveState_NoContentIsNotGenerated()
    {
        var doc = Doc();
        doc.Written = null;
        Assert.Equal(DiscoveryRowState.NotGenerated, DiscoveryUiText.ResolveState(false, doc, StaleSource.None, true));
    }

    [Theory]
    [InlineData(DiscoveryRowState.NotGenerated, "Not generated")]
    [InlineData(DiscoveryRowState.Writing, "Writing...")]
    [InlineData(DiscoveryRowState.Generated, "Generated")]
    [InlineData(DiscoveryRowState.Warnings, "Warnings")]
    [InlineData(DiscoveryRowState.Failed, "Failed")]
    [InlineData(DiscoveryRowState.OutOfDate, "Out of date")]
    [InlineData(DiscoveryRowState.NotSaved, "Not saved")]
    [InlineData(DiscoveryRowState.FileMissing, "File missing")]
    public void RowStatusCell_PerState(DiscoveryRowState state, string expected)
    {
        Assert.Equal(expected, DiscoveryUiText.RowStatusCell(state));
    }

    [Fact]
    public void RowStatusFull_NotGeneratedAndWriting()
    {
        Assert.Equal("Not generated. Generate Documents writes it.", DiscoveryUiText.RowStatusFull(Row(DiscoveryRowState.NotGenerated)));

        var writing = Row(DiscoveryRowState.Writing);
        writing.Part = 2;
        writing.PartCount = 3;
        Assert.Equal("Writing: part 2 of 3...", DiscoveryUiText.RowStatusFull(writing));
        writing.PartCount = 1;
        Assert.Equal("Writing...", DiscoveryUiText.RowStatusFull(writing));
    }

    [Fact]
    public void RowStatusFull_Generated()
    {
        Assert.Equal($"Generated {LocalStamp(Utc)} · 25 requests", DiscoveryUiText.RowStatusFull(Row(DiscoveryRowState.Generated)));

        var deposition = Row(DiscoveryRowState.Generated, "Deposition of Ann Lee");
        deposition.Kind = DiscoveryKind.Deposition;
        deposition.TestimonyPages = 1;
        Assert.Equal($"Generated {LocalStamp(Utc)} · 1 page", DiscoveryUiText.RowStatusFull(deposition));
    }

    [Fact]
    public void RowStatusFull_Warnings()
    {
        var row = Row(DiscoveryRowState.Warnings);
        row.Warnings.Add(new DiscoveryViolation { Rule = DiscoveryRule.D2UnknownPerson, Value = "Bob Vance" });
        row.Warnings.Add(new DiscoveryViolation { Rule = DiscoveryRule.D7Subparts, Number = 4 });
        Assert.Equal("Generated with warnings: 2 checks still failed after 3 attempts. names \"Bob Vance\", who is not in the cast",
            DiscoveryUiText.RowStatusFull(row));

        row.Warnings.RemoveAt(1);
        Assert.Equal("Generated with warnings: 1 check still failed after 3 attempts. names \"Bob Vance\", who is not in the cast",
            DiscoveryUiText.RowStatusFull(row));
    }

    [Fact]
    public void RowStatusFull_Failed()
    {
        var row = Row(DiscoveryRowState.Failed);
        row.FailureReason = "the request timed out";
        Assert.Equal("Failed: the request timed out. Click Generate Documents or Regenerate... to try again.", DiscoveryUiText.RowStatusFull(row));

        row.PreviousKept = true;
        Assert.Equal("Failed: the request timed out. The previous version is kept.", DiscoveryUiText.RowStatusFull(row));

        row.PreviousKept = false;
        row.FailureReason = "";
        Assert.Equal("Failed: the model returned no usable content. Click Generate Documents or Regenerate... to try again.",
            DiscoveryUiText.RowStatusFull(row));
    }

    [Theory]
    [InlineData(StaleSource.Guide, "it")]
    [InlineData(StaleSource.Core, "the core document")]
    [InlineData(StaleSource.Caption, "the caption or counsel")]
    [InlineData(StaleSource.CorpusPlan, "the corpus plan")]
    public void RowStatusFull_OutOfDate(StaleSource stale, string what)
    {
        Assert.Equal(what, DiscoveryUiText.StaleWhat(stale));
        var row = Row(DiscoveryRowState.OutOfDate);
        row.Stale = stale;
        Assert.Equal($"Out of date with the case guide: {what} changed after this document was written. Click Regenerate... to write it again.",
            DiscoveryUiText.RowStatusFull(row));
    }

    [Fact]
    public void RowStatusFull_OutOfDate_Exhibits()
    {
        Assert.Equal("a cited email", DiscoveryUiText.StaleWhat(StaleSource.Exhibits));
        var row = Row(DiscoveryRowState.OutOfDate);
        row.Stale = StaleSource.Exhibits;
        Assert.Equal("Out of date: an email it cites was regenerated or deleted after this document was written. Click Regenerate... to write it again.",
            DiscoveryUiText.RowStatusFull(row));
    }

    [Fact]
    public void RowStatusFull_NotSavedAndFileMissing()
    {
        Assert.Equal("Not saved: Case - Requests for Production.docx is open in Word. Close it, then click Save Again.",
            DiscoveryUiText.RowStatusFull(Row(DiscoveryRowState.NotSaved)));
        Assert.Equal(
            "File missing: Case - Requests for Production.docx is not in the Discovery folder. Click Save Again to write it from the saved content.",
            DiscoveryUiText.RowStatusFull(Row(DiscoveryRowState.FileMissing)));
    }

    [Fact]
    public void RowTooltip_ListsViolationsAndNotes()
    {
        var row = Row(DiscoveryRowState.Warnings, "Deposition of Ann Lee");
        row.Kind = DiscoveryKind.Deposition;
        row.Warnings.Add(new DiscoveryViolation { Rule = DiscoveryRule.D2UnknownPerson, Value = "Bob Vance" });
        row.Warnings.Add(new DiscoveryViolation { Rule = DiscoveryRule.D1ExhibitNotShown, Number = 3 });
        row.PlannerNotes.Add("Counsel for Harmon Glass is not in the cast, so the transcript says \"Counsel for Harmon Glass\".");

        var expected = string.Join(Environment.NewLine,
            "Deposition of Ann Lee",
            "Generated with warnings: 2 checks still failed after 3 attempts. names \"Bob Vance\", who is not in the cast",
            "",
            "• names \"Bob Vance\", who is not in the cast",
            "• Exhibit 3 is never shown to the witness",
            "• Counsel for Harmon Glass is not in the cast, so the transcript says \"Counsel for Harmon Glass\".");
        Assert.Equal(expected, DiscoveryUiText.RowTooltip(row));

        Assert.Equal("Requests for Production (25)" + Environment.NewLine + "Not generated. Generate Documents writes it.",
            DiscoveryUiText.RowTooltip(Row(DiscoveryRowState.NotGenerated)));
    }

    [Theory]
    [InlineData(DiscoveryRule.D1ShortPart, 2, 40, 12, "", "", "part 2 came back with 12 of about 40 turns")]
    [InlineData(DiscoveryRule.D1ExhibitNotShown, 4, 0, 0, "", "", "Exhibit 4 is never shown to the witness")]
    [InlineData(DiscoveryRule.D2UnknownPerson, 0, 0, 0, "Bob Vance", "", "names \"Bob Vance\", who is not in the cast")]
    [InlineData(DiscoveryRule.D3AmountVariant, 0, 0, 0, "$1,200,000", "$1,250,000", "mentions \"$1,200,000\", which is close to but not the case amount $1,250,000")]
    [InlineData(DiscoveryRule.D3UnknownIdentifier, 0, 0, 0, "PO-7781", "", "uses identifier \"PO-7781\", which is not in the case guide")]
    [InlineData(DiscoveryRule.D4MissingRequest, 7, 1, 0, "", "", "request 7 is missing")]
    [InlineData(DiscoveryRule.D4ExtraRequest, 30, 0, 1, "", "", "returned a request that was not asked for")]
    [InlineData(DiscoveryRule.D5MissingFactDetail, 5, 0, 0, "$48,000", "F-012", "admission 5 leaves out $48,000 from its key fact")]
    [InlineData(DiscoveryRule.D6AfterDepositionDate, 0, 0, 0, "July 2, 2025", "", "mentions July 2, 2025, after the deposition date")]
    [InlineData(DiscoveryRule.D7Subparts, 9, 0, 0, "", "", "interrogatory 9 has lettered or numbered parts")]
    [InlineData(DiscoveryRule.D8RealEntityName, 0, 0, 0, "Walmart", "Valumart", "uses the real-world name \"Walmart\" instead of \"Valumart\"")]
    public void ViolationText_PerRule(DiscoveryRule rule, int number, int expected, int actual, string value, string canonical, string text)
    {
        var v = new DiscoveryViolation { Rule = rule, Number = number, Expected = expected, Actual = actual, Value = value, Canonical = canonical };
        Assert.Equal(text, DiscoveryUiText.ViolationText(v));
    }

    [Fact]
    public void ViolationText_CoversEveryRule()
    {
        foreach (var rule in Enum.GetValues<DiscoveryRule>())
            Assert.False(string.IsNullOrWhiteSpace(DiscoveryUiText.ViolationText(new DiscoveryViolation { Rule = rule, Number = 1, Value = "x" })));
    }

    // --- Estimate ---

    [Fact]
    public void EstimateLine_FourConditions()
    {
        Assert.Equal(
            "Estimated cost ≈$1.20, ±50%, for 5 documents on Claude Sonnet 4.5, including prompt caching. Each document is saved to the Discovery folder as soon as it is finished.",
            DiscoveryUiText.EstimateLine(DiscoveryUiText.EstimateCondition.SomeToWrite, 1.2m, 5, "Claude Sonnet 4.5"));
        Assert.Equal(
            "Estimated cost ≈$0.30, ±50%, for 1 document on gpt-5, including prompt caching. Each document is saved to the Discovery folder as soon as it is finished.",
            DiscoveryUiText.EstimateLine(DiscoveryUiText.EstimateCondition.SomeToWrite, 0.3m, 1, "gpt-5"));
        Assert.Equal("Test an API key on the API Configuration step first.",
            DiscoveryUiText.EstimateLine(DiscoveryUiText.EstimateCondition.NoKey, 1.2m, 5, "gpt-5"));
        Assert.Equal("Select a witness or a written discovery set to see the cost.",
            DiscoveryUiText.EstimateLine(DiscoveryUiText.EstimateCondition.NothingSelected, 0m, 0, "gpt-5"));
        Assert.Equal("Every selected document is written. Select a row and click Regenerate... to write it again.",
            DiscoveryUiText.EstimateLine(DiscoveryUiText.EstimateCondition.AllWritten, 0m, 0, "gpt-5"));
        Assert.Equal("Deposition of Ann Lee: ≈$0.42", DiscoveryUiText.EstimateTooltipLine("Deposition of Ann Lee", 0.42m));
    }

    // --- Status line ---

    [Fact]
    public void StatusRunning_WithAndWithoutParts()
    {
        Assert.Equal("Writing 2 of 5 documents: Deposition of Ann Lee, part 1 of 3...",
            DiscoveryUiText.StatusRunning(2, 5, "Deposition of Ann Lee", 1, 3));
        Assert.Equal("Writing 1 of 1 document: Requests for Production (25)...",
            DiscoveryUiText.StatusRunning(1, 1, "Requests for Production (25)", 1, 1));
        Assert.Equal("Stopping... finished documents are saved.", DiscoveryUiText.StatusStopping);
    }

    [Theory]
    [InlineData(1, 0, 0, "Done with 1 document with warnings. Select a row to see what was flagged.")]
    [InlineData(0, 2, 0, "Done with 2 failed documents. Select a row to see what was flagged.")]
    [InlineData(0, 0, 1, "Done with 1 file not saved. Select a row to see what was flagged.")]
    [InlineData(2, 1, 0, "Done with 2 documents with warnings and 1 failed document. Select a row to see what was flagged.")]
    [InlineData(1, 1, 3, "Done with 1 document with warnings, 1 failed document and 3 files not saved. Select a row to see what was flagged.")]
    public void StatusDoneWithProblems_JoinsClauses(int w, int f, int s, string expected)
    {
        Assert.Equal(expected, DiscoveryUiText.StatusDoneWithProblems(w, f, s));
    }

    [Fact]
    public void Status_CancelledFatalAndFileMessages()
    {
        Assert.Equal("Cancelled. 2 of 5 documents saved. Click Generate Documents to write the rest.", DiscoveryUiText.StatusCancelled(2, 5));
        Assert.Equal(
            "Generation stopped: the API key was rejected. Finished documents are saved. Fix the problem on the API Configuration step, then click Generate Documents.",
            DiscoveryUiText.StatusFatal("the API key was rejected."));
        Assert.Equal("Could not open a.docx: Access is denied. Click Open Discovery Folder to open it from Explorer.",
            DiscoveryUiText.OpenFailed("a.docx", "Access is denied."));
        Assert.Equal("a.docx is still open in Word. Close it, then click Save Again.", DiscoveryUiText.StillOpen("a.docx"));
        Assert.Equal("Saved a.docx.", DiscoveryUiText.Saved("a.docx"));
    }

    [Fact]
    public void ButtonCaptions()
    {
        Assert.Equal("Generate Documents", DiscoveryUiText.GenerateButton);
        Assert.Equal("Cancel Generation", DiscoveryUiText.CancelGenerationButton);
        Assert.Equal("Open in Word", DiscoveryUiText.OpenInWordButton);
        Assert.Equal("Regenerate...", DiscoveryUiText.RegenerateButton);
        Assert.Equal("Save Again", DiscoveryUiText.SaveAgainButton);
        Assert.Equal("Open Discovery Folder", DiscoveryUiText.OpenFolderButton);
        Assert.Equal("Discovery Documents", DiscoveryUiText.DiscoveryDocumentsButton);
    }

    [Fact]
    public void Confirmations()
    {
        Assert.Equal(("Cancel Generation",
                "Stop writing discovery documents? Finished documents are saved. The rest stay Not generated, and Generate Documents writes them later."),
            DiscoveryUiText.CD1CancelGeneration());
        Assert.Equal(("Stop Generating?", "Discovery documents are still being written. Close and stop? Finished documents are saved."),
            DiscoveryUiText.CD2CloseDuringRun());
    }

    // --- S2: Regenerate dialog ---

    [Fact]
    public void RegenerateDialogCopy()
    {
        Assert.Equal("Regenerate Document", DiscoveryUiText.RegenerateTitle);
        Assert.Equal("Regenerate Deposition of Ann Lee?", DiscoveryUiText.RegenerateHeadline("Deposition of Ann Lee"));
        Assert.Equal(
            "This writes the document again from the current case guide and replaces a.docx in the Discovery folder. Nothing else changes. Exhibit numbers stay the same.",
            DiscoveryUiText.RegenerateBody("a.docx", true));
        Assert.Equal(
            "This writes the document again from the current case guide and replaces a.docx in the Discovery folder. Nothing else changes.",
            DiscoveryUiText.RegenerateBody("a.docx", false));
        Assert.Equal("Uses the current settings: 25 testimony pages, deposed June 10, 2025.",
            DiscoveryUiText.SettingsLineDeposition(25, "2025-06-10"));
        Assert.Equal("Uses the current settings: 1 testimony page.", DiscoveryUiText.SettingsLineDeposition(1, ""));
        Assert.Equal("Uses the current settings: 25 requests, served by Brightline on Harmon.",
            DiscoveryUiText.SettingsLineWritten(25, "Brightline", "Harmon", false));
        Assert.Equal("Uses the current settings: 1 request, requested by Brightline from Harmon.",
            DiscoveryUiText.SettingsLineWritten(1, "Brightline", "Harmon", true));
        Assert.Equal("Uses the current settings: 25 requests, served by Brightline on Harmon Construction Group, Inc.",
            DiscoveryUiText.SettingsLineWritten(25, "Brightline", "Harmon Construction Group, Inc.", false));
        Assert.Equal("Guidance (optional)", DiscoveryUiText.GuidanceCaption);
        Assert.Equal("For example: spend more time on the change order and less on scheduling.", DiscoveryUiText.GuidancePlaceholder(true));
        Assert.Equal("For example: focus more requests on the payment dispute.", DiscoveryUiText.GuidancePlaceholder(false));
        Assert.Equal("a.docx is open in Word. Close it before you regenerate, or the new version cannot be saved.",
            DiscoveryUiText.FileOpenWarning("a.docx"));
        Assert.Equal("Estimated cost ≈$0.42, ±50%.", DiscoveryUiText.DialogEstimate(0.42m));
    }

    // --- S4: Case Summary row ---

    [Fact]
    public void SummaryRow_NotGenerated()
    {
        var row = DiscoveryUiText.SummaryRow(Array.Empty<DiscoveryRowInfo>(), folderHasDocuments: false);
        Assert.Equal("Discovery documents", DiscoveryUiText.SummaryTitle);
        Assert.Equal(
            "Deposition excerpts and written discovery written from the case guide, saved as Word files in the Discovery folder. Not part of the answer key.",
            row.Line2);
        Assert.Equal("Not generated", row.Line3);
        Assert.Equal(SummaryTone.Muted, row.Line3Tone);
        Assert.Equal(SummaryTone.Muted, row.DotTone);
        Assert.False(row.ShowPrimary);
        Assert.True(row.ShowSecondary);
    }

    [Fact]
    public void SummaryRow_AllClean()
    {
        var rows = new[] { Row(DiscoveryRowState.Generated, "Deposition of Ann Lee"), Row(DiscoveryRowState.Generated) };
        var row = DiscoveryUiText.SummaryRow(rows, folderHasDocuments: true);
        Assert.Equal($"2 documents · last generated {LocalDay(Utc)}", row.Line3);
        Assert.Equal(SummaryTone.Success, row.Line3Tone);
        Assert.Equal(SummaryTone.Success, row.DotTone);
        Assert.True(row.ShowPrimary);
        Assert.Equal(string.Join(Environment.NewLine,
                row.Line3, "", "Deposition of Ann Lee: Generated", "Requests for Production (25): Generated"),
            row.Line3Tooltip);
    }

    [Fact]
    public void SummaryRow_LastGeneratedIsLatest()
    {
        var older = Row(DiscoveryRowState.Generated);
        older.GeneratedUtc = Utc.AddDays(-30);
        var row = DiscoveryUiText.SummaryRow(new[] { older, Row(DiscoveryRowState.Generated) }, true);
        Assert.EndsWith(LocalDay(Utc), row.Line3);
    }

    [Fact]
    public void SummaryRow_WarningsAndFailed_IsDanger()
    {
        var rows = new[] { Row(DiscoveryRowState.Generated), Row(DiscoveryRowState.Warnings), Row(DiscoveryRowState.Failed) };
        var row = DiscoveryUiText.SummaryRow(rows, true);
        Assert.Equal($"2 documents · last generated {LocalDay(Utc)} · 1 with warnings · 1 failed", row.Line3);
        Assert.Equal(SummaryTone.Danger, row.Line3Tone);
        Assert.Equal(SummaryTone.Danger, row.DotTone);
    }

    [Fact]
    public void SummaryRow_OnlyOutOfDate_IsWarning()
    {
        var rows = new[] { Row(DiscoveryRowState.OutOfDate) };
        var row = DiscoveryUiText.SummaryRow(rows, true);
        Assert.Equal($"1 document · last generated {LocalDay(Utc)} · 1 out of date", row.Line3);
        Assert.Equal(SummaryTone.Warning, row.Line3Tone);
        Assert.Equal(SummaryTone.Warning, row.DotTone);
    }

    [Fact]
    public void SummaryRow_EveryClause()
    {
        var rows = new[]
        {
            Row(DiscoveryRowState.Warnings), Row(DiscoveryRowState.OutOfDate), Row(DiscoveryRowState.NotSaved),
            Row(DiscoveryRowState.FileMissing), Row(DiscoveryRowState.FileMissing), Row(DiscoveryRowState.Failed)
        };
        var row = DiscoveryUiText.SummaryRow(rows, true);
        Assert.Equal(
            $"5 documents · last generated {LocalDay(Utc)} · 1 with warnings · 1 out of date · 1 not saved · 2 files missing · 1 failed",
            row.Line3);
        Assert.Equal(SummaryTone.Danger, row.Line3Tone);
    }

    [Theory]
    [InlineData(DiscoveryRowState.NotSaved, "1 not saved")]
    [InlineData(DiscoveryRowState.FileMissing, "1 file missing")]
    [InlineData(DiscoveryRowState.Warnings, "1 with warnings")]
    public void SummaryRow_SingleProblemClause_IsWarning(DiscoveryRowState state, string clause)
    {
        var row = DiscoveryUiText.SummaryRow(new[] { Row(DiscoveryRowState.Generated), Row(state) }, true);
        Assert.Equal($"2 documents · last generated {LocalDay(Utc)} · {clause}", row.Line3);
        Assert.Equal(SummaryTone.Warning, row.Line3Tone);
    }

    [Fact]
    public void SummaryRow_OnlyFailed()
    {
        var row = DiscoveryUiText.SummaryRow(new[] { Row(DiscoveryRowState.Failed) }, false);
        Assert.Equal("Not generated · 1 failed", row.Line3);
        Assert.Equal(SummaryTone.Danger, row.Line3Tone);
        Assert.Equal(SummaryTone.Danger, row.DotTone);
    }

    [Fact]
    public void SummaryRow_IgnoresUnsavedRows()
    {
        var row = DiscoveryUiText.SummaryRow(new[] { Row(DiscoveryRowState.NotGenerated) }, false);
        Assert.Equal("Not generated", row.Line3);
        Assert.Equal(SummaryTone.Muted, row.DotTone);
    }

    // --- D-02: never "fictional", never "evidence" ---

    [Fact]
    public void NoPublicString_SaysFictionalOrEvidence()
    {
        var type = typeof(DiscoveryUiText);
        foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.Static).Where(f => f.FieldType == typeof(string)))
            AssertClean((string)field.GetValue(null)!);
        foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.Static).Where(p => p.PropertyType == typeof(string)))
            AssertClean((string)prop.GetValue(null)!);

        var samples = new List<string>
        {
            DiscoveryUiText.Notice(true, true), DiscoveryUiText.Notice(false, false), DiscoveryUiText.SelectionHint(2),
            DiscoveryUiText.WitnessTooltip("t", "o", 2), DiscoveryUiText.CountsHint(DiscoveryVariant.Civil, Enum.GetValues<DiscoveryKind>()),
            DiscoveryUiText.InvalidCountHint(5, 80, "x"), DiscoveryUiText.RaisedSentence("x", 2), DiscoveryUiText.LoweredSentence("x", 2),
            DiscoveryUiText.LoweredSentence("x", 4, 2), DiscoveryUiText.SpentHint(2m),
            DiscoveryUiText.EstimateTooltipLine("t", 1m), DiscoveryUiText.StatusRunning(1, 2, "t", 1, 2), DiscoveryUiText.StatusDone(2),
            DiscoveryUiText.StatusDoneWithProblems(1, 1, 1), DiscoveryUiText.StatusCancelled(1, 2), DiscoveryUiText.StatusFatal("m"),
            DiscoveryUiText.OpenFailed("f", "m"), DiscoveryUiText.StillOpen("f"), DiscoveryUiText.Saved("f"),
            DiscoveryUiText.CD1CancelGeneration().Title, DiscoveryUiText.CD1CancelGeneration().Text,
            DiscoveryUiText.CD2CloseDuringRun().Title, DiscoveryUiText.CD2CloseDuringRun().Text,
            DiscoveryUiText.RegenerateHeadline("t"), DiscoveryUiText.RegenerateBody("f", true),
            DiscoveryUiText.SettingsLineDeposition(2, "2025-06-10"), DiscoveryUiText.SettingsLineWritten(2, "a", "b", true),
            DiscoveryUiText.GuidancePlaceholder(true), DiscoveryUiText.GuidancePlaceholder(false), DiscoveryUiText.FileOpenWarning("f"),
            DiscoveryUiText.DialogEstimate(1m), DiscoveryUiText.PartyItem("a", "b"),
            DiscoveryUiText.PartyLabels(true).By, DiscoveryUiText.PartyLabels(false).On
        };
        foreach (var variant in Enum.GetValues<DiscoveryVariant>())
        {
            samples.Add(DiscoveryUiText.DepositionsCaption(variant));
            samples.Add(DiscoveryUiText.MemoNote(variant));
            foreach (var kind in Enum.GetValues<DiscoveryKind>())
                samples.Add(DiscoveryUiText.ShortTitle(kind, variant, "n", 2));
        }
        foreach (var condition in Enum.GetValues<DiscoveryUiText.EstimateCondition>())
            samples.Add(DiscoveryUiText.EstimateLine(condition, 1m, 2, "m"));
        foreach (var state in Enum.GetValues<DiscoveryRowState>())
        {
            var row = Row(state);
            row.FailureReason = "r";
            row.Warnings.Add(new DiscoveryViolation { Rule = DiscoveryRule.D3UnknownIdentifier, Value = "v" });
            samples.Add(DiscoveryUiText.RowStatusCell(state));
            samples.Add(DiscoveryUiText.RowTooltip(row));
        }
        foreach (var rule in Enum.GetValues<DiscoveryRule>())
            samples.Add(DiscoveryUiText.ViolationText(new DiscoveryViolation { Rule = rule, Value = "v", Canonical = "c" }));
        var summary = DiscoveryUiText.SummaryRow(new[] { Row(DiscoveryRowState.Failed), Row(DiscoveryRowState.Warnings) }, true);
        samples.Add(summary.Line2);
        samples.Add(summary.Line3);
        samples.Add(summary.Line3Tooltip);

        foreach (var text in samples)
            AssertClean(text);
    }
}
