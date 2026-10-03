using System.Globalization;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using DocumentFormat.OpenXml.Wordprocessing;
using ReelDiscovery.Models.Corpus;
using ReelDiscovery.Models.Evidence;
using ReelDiscovery.Services.Case;
using ReelDiscovery.Services.Evidence;

namespace ReelDiscovery.Tests.Evidence;

public class StorySoFarDocxTests
{
    private static (List<ValidationErrorInfo> Errors, List<string> Paragraphs) OpenAndValidate(byte[] bytes)
    {
        using var doc = WordprocessingDocument.Open(new MemoryStream(bytes), false);
        var errors = new OpenXmlValidator().Validate(doc).ToList();
        var paragraphs = doc.MainDocumentPart!.Document!.Body!.Descendants<Paragraph>().Select(p => p.InnerText).ToList();
        return (errors, paragraphs);
    }

    private static LedgerEntry Entry(string id, string date, LedgerKind kind, string statement, string value = "",
        string[]? knownBy = null, string[]? facts = null, string[]? issues = null, string source = "") => new()
    {
        Id = id,
        Date = date,
        Kind = kind,
        Statement = statement,
        Value = value,
        KnownByIds = (knownBy ?? Array.Empty<string>()).ToList(),
        FactIds = (facts ?? Array.Empty<string>()).ToList(),
        IssueIds = (issues ?? Array.Empty<string>()).ToList(),
        SourceDocId = source
    };

    // April entry first so the builder has to sort
    private static NarrativeLedger TwoMonthLedger() => new()
    {
        Entries = new List<LedgerEntry>
        {
            Entry("T-0002-L01", "2024-04-09", LedgerKind.Meeting, "Site meeting moved to the east entrance."),
            Entry("T-0001-L01", "2024-03-04", LedgerKind.Commitment, "Brightline committed to the revised glazing quote.", "$18,425",
                new[] { "CH-001", "CH-002" }, new[] { "F-003" }, new[] { "ISS-01" }, "RD-000005")
        }
    };

    private static GenerationRunState TwoDone(CorpusPlan plan)
    {
        var threads = plan.Threads.Where(t => t.Relevance == ThreadRelevance.Relevant).OrderBy(t => t.Id, StringComparer.Ordinal).ToList();
        var replay = GenerationRunStateTests.Replay(
            GenerationRunStateTests.Done(plan, threads[0], ThreadWriteStatus.Written, 0.1m),
            GenerationRunStateTests.Done(plan, threads[1], ThreadWriteStatus.Written, 0.1m));
        return GenerationRunState.From(plan, replay);
    }

    [Fact]
    public void FileNameFor_AppendsSuffix()
    {
        Assert.Equal("Brightline v Harmon - Story So Far.docx", StorySoFarDocxBuilder.FileNameFor("Brightline v Harmon"));
        Assert.Equal(" - Story So Far.docx", StorySoFarDocxBuilder.FileNameSuffix);
    }

    [Theory]
    [InlineData("..\\x - Story So Far.docx")]
    [InlineData("a/b - Story So Far.docx")]
    [InlineData("x.docx")]
    [InlineData(" - Story So Far.docx")]
    [InlineData("")]
    [InlineData(null)]
    public void IsStoryFileName_RejectsUnsafeNames(string? name)
    {
        Assert.False(StorySoFarDocxBuilder.IsStoryFileName(name));
    }

    [Fact]
    public void IsStoryFileName_AcceptsBareName()
    {
        Assert.True(StorySoFarDocxBuilder.IsStoryFileName("Brightline v Harmon - Story So Far.docx"));
    }

    [Fact]
    public void Build_GroupsByMonthWithEntryLines()
    {
        var (draft, plan) = EvidenceTestData.ApprovedCase();
        var people = PeopleDirectory.Create(draft.Guide!, plan);

        var (errors, paragraphs) = OpenAndValidate(StorySoFarDocxBuilder.Build(draft, plan, TwoMonthLedger(), TwoDone(plan), people));

        Assert.Empty(errors);
        var title = CaseFolderNaming.CaseTitle(draft);
        Assert.Equal($"{title} - Story So Far", paragraphs[0]);
        Assert.Contains("Internal narrative ledger written by ReelDiscovery from 2 generated threads, Mar 4, 2024 to Apr 9, 2024. Not part of the produced corpus.", paragraphs);

        var march = paragraphs.IndexOf("March 2024");
        var april = paragraphs.IndexOf("April 2024");
        Assert.True(march > 0);
        Assert.True(april > march);

        var commitment = paragraphs.IndexOf("Mar 4, 2024 · Commitment Brightline committed to the revised glazing quote. ($18,425)");
        Assert.True(commitment > march && commitment < april);
        Assert.Equal(
            $"Known to {people.NameOf("CH-001")}, {people.NameOf("CH-002")} · Facts F-003 · Issues ISS-01 · Source RD-000005",
            paragraphs[commitment + 1]);

        // No value, no detail clauses: no suffix and no detail line
        var meeting = paragraphs.IndexOf("Apr 9, 2024 · Meeting Site meeting moved to the east entrance.");
        Assert.True(meeting > april);
        Assert.DoesNotContain("Threads kept with warnings", paragraphs);
    }

    [Fact]
    public void Build_DetailLine_DropsEmptyClauses()
    {
        var (draft, plan) = EvidenceTestData.ApprovedCase();
        var people = PeopleDirectory.Create(draft.Guide!, plan);
        var ledger = new NarrativeLedger
        {
            Entries = new List<LedgerEntry> { Entry("T-0001-L01", "2024-03-04", LedgerKind.Figure, "Quote issued.", source: "RD-000009", facts: new[] { "F-001", "F-002" }) }
        };

        var (_, paragraphs) = OpenAndValidate(StorySoFarDocxBuilder.Build(draft, plan, ledger, TwoDone(plan), people));

        Assert.Contains("Facts F-001, F-002 · Source RD-000009", paragraphs);
    }

    [Fact]
    public void Build_WarningsThreads_ListedInFinalSection()
    {
        var (draft, plan, state, threads) = GenerationRunStateTests.Mixed();
        var people = PeopleDirectory.Create(draft.Guide!, plan);

        var (errors, paragraphs) = OpenAndValidate(StorySoFarDocxBuilder.Build(draft, plan, TwoMonthLedger(), state, people));

        Assert.Empty(errors);
        var heading = paragraphs.IndexOf("Threads kept with warnings");
        Assert.True(heading > paragraphs.IndexOf("April 2024"));
        Assert.Equal(
            $"{threads[1].Id} · {threads[1].Subject.Trim()} Message 2 is shorter than its length band; Message 1 names an unknown person",
            paragraphs[heading + 1]);
        Assert.DoesNotContain(paragraphs, p => p.StartsWith(threads[0].Id + " · ", StringComparison.Ordinal));
    }

    [Fact]
    public void Build_EmptyLedger_IsValidWithPlaceholder()
    {
        var (draft, plan) = EvidenceTestData.ApprovedCase();
        var people = PeopleDirectory.Create(draft.Guide!, plan);

        var (errors, paragraphs) = OpenAndValidate(StorySoFarDocxBuilder.Build(draft, plan, new NarrativeLedger(),
            GenerationRunState.From(plan, new JournalReplay()), people));

        Assert.Empty(errors);
        Assert.Equal($"{CaseFolderNaming.CaseTitle(draft)} - Story So Far", paragraphs[0]);
        Assert.Contains("No story details yet.", paragraphs);
    }

    [Fact]
    public void Build_InvalidXmlCharacters_AreStripped()
    {
        var (draft, plan) = EvidenceTestData.ApprovedCase();
        var people = PeopleDirectory.Create(draft.Guide!, plan);
        var ledger = new NarrativeLedger
        {
            Entries = new List<LedgerEntry> { Entry("T-0001-L01", "2024-03-04", LedgerKind.Other, "Bad\u0001 char\u0008 text.", "\u000b42") }
        };

        var (errors, paragraphs) = OpenAndValidate(StorySoFarDocxBuilder.Build(draft, plan, ledger, TwoDone(plan), people));

        Assert.Empty(errors);
        Assert.Contains("Mar 4, 2024 · Other Bad char text. (42)", paragraphs);
    }

    [Fact]
    public void Build_UsesEnglishMonthNamesUnderGermanCulture()
    {
        var (draft, plan) = EvidenceTestData.ApprovedCase();
        var people = PeopleDirectory.Create(draft.Guide!, plan);
        var previous = CultureInfo.CurrentCulture;
        var previousUi = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("de-DE");

            var (_, paragraphs) = OpenAndValidate(StorySoFarDocxBuilder.Build(draft, plan, TwoMonthLedger(), TwoDone(plan), people));

            Assert.Contains("March 2024", paragraphs);
            Assert.Contains("April 2024", paragraphs);
            Assert.Contains(paragraphs, p => p.StartsWith("Mar 4, 2024 · Commitment", StringComparison.Ordinal));
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
            CultureInfo.CurrentUICulture = previousUi;
        }
    }
}
