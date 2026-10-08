using ReelDiscovery.Models.Case;
using ReelDiscovery.Models.Corpus;
using ReelDiscovery.Models.Discovery;
using ReelDiscovery.Services.Case;

namespace ReelDiscovery.Tests.Case;

public class CaseDraftSerializationTests
{
    [Fact]
    public void FormatVersion_IsFour()
    {
        Assert.Equal(4, CaseDraft.CurrentFormatVersion);
        Assert.Contains("\"FormatVersion\": 4", new CaseDraft().ToJson());
    }

    [Fact]
    public void Discovery_RoundTrips()
    {
        var draft = CaseTestData.SampleGuidedDraft();
        draft.Discovery = new DiscoverySettings
        {
            WitnessIds = { "CH-002", "CH-003" },
            PagesPerExcerpt = 30,
            RfpCount = 20,
            RogEnabled = false,
            PropoundingPartyId = "PTY-01",
            UserTouched = true
        };

        var copy = CaseDraft.FromJson(draft.ToJson());

        Assert.NotNull(copy.Discovery);
        Assert.Equal(new[] { "CH-002", "CH-003" }, copy.Discovery!.WitnessIds);
        Assert.Equal(30, copy.Discovery.PagesPerExcerpt);
        Assert.Equal(20, copy.Discovery.RfpCount);
        Assert.False(copy.Discovery.RogEnabled);
        Assert.True(copy.Discovery.RfpEnabled);
        Assert.Equal("PTY-01", copy.Discovery.PropoundingPartyId);
        Assert.True(copy.Discovery.UserTouched);
    }

    [Fact]
    public void V3File_LoadsWithoutDiscovery()
    {
        var json = "{ \"FormatVersion\": 3, \"Scenario\": \"A dispute\" }";

        var draft = CaseDraft.FromJson(json);
        Assert.Null(draft.Discovery);

        draft.Normalize();
        Assert.Null(draft.Discovery);
        Assert.Equal(4, draft.FormatVersion);
    }

    [Fact]
    public void DiscoverySettings_Normalize_Clamps()
    {
        var low = new DiscoverySettings { PagesPerExcerpt = 5, RfpCount = 2, RogCount = 0, RfaCount = 0 };
        low.Normalize();
        Assert.Equal(10, low.PagesPerExcerpt);
        Assert.Equal(5, low.RfpCount);
        Assert.Equal(1, low.RogCount);
        Assert.Equal(1, low.RfaCount);

        var high = new DiscoverySettings
        {
            PagesPerExcerpt = 99,
            RfpCount = 200,
            RogCount = 40,
            RfaCount = 90,
            WitnessIds = new() { "CH-001", "CH-002", "CH-003", "CH-004", "CH-005", "CH-006", "CH-007", "CH-008" }
        };
        high.Normalize();
        Assert.Equal(60, high.PagesPerExcerpt);
        Assert.Equal(80, high.RfpCount);
        Assert.Equal(25, high.RogCount);
        Assert.Equal(50, high.RfaCount);
        Assert.Equal(new[] { "CH-001", "CH-002", "CH-003", "CH-004", "CH-005", "CH-006" }, high.WitnessIds);

        var messy = new DiscoverySettings
        {
            WitnessIds = new() { "CH-001", null!, " ", "CH-001", "", "CH-002" },
            PropoundingPartyId = null!,
            RespondingPartyId = null!
        };
        messy.Normalize();
        Assert.Equal(new[] { "CH-001", "CH-002" }, messy.WitnessIds);
        Assert.Equal(string.Empty, messy.PropoundingPartyId);
        Assert.Equal(string.Empty, messy.RespondingPartyId);

        var nullList = new DiscoverySettings { WitnessIds = null! };
        nullList.Normalize();
        Assert.Empty(nullList.WitnessIds);
    }

    [Theory]
    [InlineData(ComplexityLevel.Simple, 15, 15, 12, 10)]
    [InlineData(ComplexityLevel.Standard, 25, 25, 18, 20)]
    [InlineData(ComplexityLevel.Complex, 40, 40, 25, 30)]
    public void DiscoverySettings_ForPreset_Defaults(ComplexityLevel level, int pages, int rfp, int rog, int rfa)
    {
        var settings = DiscoverySettings.ForPreset(level);

        Assert.Equal(pages, settings.PagesPerExcerpt);
        Assert.Equal(rfp, settings.RfpCount);
        Assert.Equal(rog, settings.RogCount);
        Assert.Equal(rfa, settings.RfaCount);
        Assert.True(settings.RfpEnabled);
        Assert.True(settings.RogEnabled);
        Assert.True(settings.RfaEnabled);
        Assert.False(settings.UserTouched);
    }

    [Fact]
    public void DiscoveryVariants_Of_MapsDocumentType()
    {
        Assert.Equal(DiscoveryVariant.Civil, DiscoveryVariants.Of(null));
        Assert.Equal(DiscoveryVariant.Civil, DiscoveryVariants.Of(new CoreDocument { DocumentType = CoreDocumentType.Complaint, MatterKind = MatterKind.Criminal }));
        Assert.Equal(DiscoveryVariant.Civil, DiscoveryVariants.Of(new CoreDocument { DocumentType = CoreDocumentType.Petition }));
        Assert.Equal(DiscoveryVariant.MemoCriminal, DiscoveryVariants.Of(new CoreDocument { DocumentType = CoreDocumentType.InvestigationMemo, MatterKind = MatterKind.Criminal }));
        Assert.Equal(DiscoveryVariant.MemoRegulatory, DiscoveryVariants.Of(new CoreDocument { DocumentType = CoreDocumentType.InvestigationMemo, MatterKind = MatterKind.Regulatory }));
        Assert.Equal(DiscoveryVariant.MemoCivil, DiscoveryVariants.Of(new CoreDocument { DocumentType = CoreDocumentType.InvestigationMemo, MatterKind = MatterKind.Civil }));
        Assert.False(DiscoveryVariants.IsMemo(DiscoveryVariant.Civil));
        Assert.True(DiscoveryVariants.IsMemo(DiscoveryVariant.MemoCivil));
    }

    [Fact]
    public void Corpus_RoundTrips_PlanNotSerialized()
    {
        var draft = CaseTestData.SampleGuidedDraft();
        var planId = Guid.NewGuid();
        draft.Corpus = new CorpusState
        {
            Settings = new CorpusSettings { TotalEmails = 2_500, RelevantPercent = 30, WindowStart = "2023-12-15", WindowEnd = "2024-08-20", PrivilegedPercent = 10, DocIdPrefix = "BL" },
            PlanId = planId,
            PlanGuideHash = "g",
            PlanSettingsHash = "s",
            Approved = true,
            WrittenFiles = { new WrittenCorpusFile { Kind = CorpusFileKind.AnswerKeyXlsx, FileName = "Answer Key.xlsx", PlanId = planId, WrittenUtc = new DateTime(2026, 10, 2, 0, 0, 0, DateTimeKind.Utc) } },
            Plan = new CorpusPlan { PlanId = planId, Notes = { "plan-only-note" } }
        };

        var json = draft.ToJson();
        var copy = CaseDraft.FromJson(json);

        Assert.DoesNotContain("plan-only-note", json);
        Assert.Contains("\"AnswerKeyXlsx\"", json);
        Assert.NotNull(copy.Corpus);
        Assert.Null(copy.Corpus!.Plan);
        Assert.Equal(planId, copy.Corpus.PlanId);
        Assert.True(copy.Corpus.Approved);
        Assert.Equal("g", copy.Corpus.PlanGuideHash);
        Assert.Equal(draft.Corpus.Settings.ComputeHash(), copy.Corpus.Settings.ComputeHash());
        var file = Assert.Single(copy.Corpus.WrittenFiles);
        Assert.Equal(CorpusFileKind.AnswerKeyXlsx, file.Kind);
        Assert.Equal("Answer Key.xlsx", file.FileName);
        Assert.Equal(planId, file.PlanId);
    }

    [Fact]
    public void Normalize_RepairsCorpus()
    {
        var draft = CaseDraft.FromJson("{\"FormatVersion\":2,\"Corpus\":{\"Settings\":null,\"WrittenFiles\":null}}");

        draft.Normalize();

        Assert.NotNull(draft.Corpus!.Settings);
        Assert.Equal(1_000, draft.Corpus.Settings.TotalEmails);
        Assert.Empty(draft.Corpus.WrittenFiles);
    }

    [Fact]
    public void Normalize_WithoutCorpus_StaysNull()
    {
        var draft = CaseTestData.SampleGuidedDraft();

        draft.Normalize();

        Assert.Null(draft.Corpus);
    }

    [Fact]
    public void Load_VersionOneFile_LoadsAndUpgradesVersion()
    {
        var temp = Path.Combine(Path.GetTempPath(), "rd-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(temp);
            var store = new CaseStore(Path.Combine(temp, "cases"), Path.Combine(temp, "app"));
            var path = Path.Combine(temp, "case.json");
            File.WriteAllText(path, "{\"FormatVersion\":1,\"Scenario\":\"x\",\"Guide\":{\"Cast\":[]}}");

            var result = store.Load(path);

            Assert.True(result.Succeeded, result.Message);
            Assert.Null(result.Draft!.Corpus);
            Assert.Equal("x", result.Draft.Scenario);
            // Saved files carry v2 so older builds refuse them instead of dropping Corpus (T-04-03)
            Assert.Equal(CaseDraft.CurrentFormatVersion, result.Draft.FormatVersion);
        }
        finally
        {
            if (Directory.Exists(temp))
                Directory.Delete(temp, true);
        }
    }

    [Fact]
    public void RoundTrip_PreservesEverything()
    {
        var original = CaseTestData.SampleComplaintDraft();

        var copy = CaseDraft.FromJson(original.ToJson());

        Assert.Equal(original.Scenario, copy.Scenario);
        Assert.Equal(original.EffectiveScenario, copy.EffectiveScenario);
        Assert.Equal(original.Complexity, copy.Complexity);
        Assert.Equal(original.DocumentPreference, copy.DocumentPreference);
        Assert.Equal(original.CourtPreference, copy.CourtPreference);
        Assert.Equal(original.StatePreference, copy.StatePreference);
        Assert.Equal(original.EntityCheckHash, copy.EntityCheckHash);
        Assert.Equal(original.Revision, copy.Revision);

        Assert.Equal(original.Substitutions.Count, copy.Substitutions.Count);
        for (var i = 0; i < original.Substitutions.Count; i++)
        {
            var a = original.Substitutions[i];
            var b = copy.Substitutions[i];
            Assert.Equal(a.Original, b.Original);
            Assert.Equal(a.Aliases, b.Aliases);
            Assert.Equal(a.Kind, b.Kind);
            Assert.Equal(a.Reason, b.Reason);
            Assert.Equal(a.Substitute, b.Substitute);
            Assert.Equal(a.Apply, b.Apply);
        }
        Assert.Contains(copy.Substitutions, s => !s.Apply);

        var doc = original.CoreDocument!;
        var docCopy = copy.CoreDocument!;
        Assert.NotNull(docCopy);
        AssertCoreDocumentEqual(doc, docCopy);
    }

    [Fact]
    public void Enums_SerializedAsStrings()
    {
        var json = CaseTestData.SampleComplaintDraft().ToJson();

        Assert.Contains("\"Complaint\"", json);
        Assert.Contains("\"FactualAllegations\"", json);
        Assert.Contains("\"Simple\"", json);
        Assert.DoesNotContain("\"DocumentType\": 0", json);

        var standard = new CaseDraft { Scenario = "x" }.ToJson();
        Assert.Contains("\"Standard\"", standard);
    }

    [Fact]
    public void Memo_RoundTrip()
    {
        var original = CaseTestData.SampleMemoDraft();

        var copy = CaseDraft.FromJson(original.ToJson());

        Assert.Equal(CoreDocumentType.InvestigationMemo, copy.CoreDocument!.DocumentType);
        AssertCoreDocumentEqual(original.CoreDocument!, copy.CoreDocument);
    }

    [Fact]
    public void Computed_NotSerialized()
    {
        var draft = CaseTestData.SampleComplaintDraft();
        var json = draft.ToJson();

        Assert.DoesNotContain("\"Preset\"", json);
        Assert.DoesNotContain("\"MemoParagraphsMin\"", json);

        // CaseCount.Summary is real data; the computed ComplexityPreset.Summary must not appear
        var summaryKeys = json.Split("\"Summary\"").Length - 1;
        Assert.Equal(draft.CoreDocument!.Counts.Count, summaryKeys);
        Assert.DoesNotContain(draft.Preset.Summary, json);
    }

    [Fact]
    public void FromJson_NullLiteral_Throws()
    {
        Assert.Throws<InvalidOperationException>(() => CaseDraft.FromJson("null"));
    }

    [Fact]
    public void SampleDrafts_HaveCurrentSourceHash()
    {
        var complaint = CaseTestData.SampleComplaintDraft();
        var memo = CaseTestData.SampleMemoDraft();

        Assert.Equal(complaint.ComputeSourceHash(), complaint.CoreDocument!.SourceHash);
        Assert.Equal(memo.ComputeSourceHash(), memo.CoreDocument!.SourceHash);
    }

    private static void AssertCoreDocumentEqual(CoreDocument doc, CoreDocument docCopy)
    {
        Assert.Equal(doc.DocumentType, docCopy.DocumentType);
        Assert.Equal(doc.MatterKind, docCopy.MatterKind);
        Assert.Equal(doc.MemoPerspective, docCopy.MemoPerspective);
        Assert.Equal(doc.UsesPetitionTerminology, docCopy.UsesPetitionTerminology);
        Assert.Equal(doc.DemandsJuryTrial, docCopy.DemandsJuryTrial);
        Assert.Equal(doc.Title, docCopy.Title);
        Assert.Equal(doc.FilingDate, docCopy.FilingDate);
        Assert.Equal(doc.TimelineStart, docCopy.TimelineStart);
        Assert.Equal(doc.TimelineEnd, docCopy.TimelineEnd);
        Assert.Equal(doc.SourceHash, docCopy.SourceHash);

        Assert.Equal(doc.Jurisdiction.CourtSystem, docCopy.Jurisdiction.CourtSystem);
        Assert.Equal(doc.Jurisdiction.State, docCopy.Jurisdiction.State);
        Assert.Equal(doc.Jurisdiction.CourtName, docCopy.Jurisdiction.CourtName);
        Assert.Equal(doc.Jurisdiction.District, docCopy.Jurisdiction.District);
        Assert.Equal(doc.Jurisdiction.VenueCity, docCopy.Jurisdiction.VenueCity);

        Assert.Equal(doc.Counsel.AttorneyName, docCopy.Counsel.AttorneyName);
        Assert.Equal(doc.Counsel.FirmName, docCopy.Counsel.FirmName);
        Assert.Equal(doc.Counsel.BarNumber, docCopy.Counsel.BarNumber);
        Assert.Equal(doc.Counsel.Address, docCopy.Counsel.Address);
        Assert.Equal(doc.Counsel.Phone, docCopy.Counsel.Phone);
        Assert.Equal(doc.Counsel.Email, docCopy.Counsel.Email);

        Assert.Equal(doc.Memo.To, docCopy.Memo.To);
        Assert.Equal(doc.Memo.From, docCopy.Memo.From);
        Assert.Equal(doc.Memo.Cc, docCopy.Memo.Cc);
        Assert.Equal(doc.Memo.Date, docCopy.Memo.Date);
        Assert.Equal(doc.Memo.Re, docCopy.Memo.Re);
        Assert.Equal(doc.Memo.Organization, docCopy.Memo.Organization);

        Assert.Equal(doc.Parties.Count, docCopy.Parties.Count);
        for (var i = 0; i < doc.Parties.Count; i++)
        {
            var a = doc.Parties[i];
            var b = docCopy.Parties[i];
            Assert.Equal(a.Id, b.Id);
            Assert.Equal(a.Name, b.Name);
            Assert.Equal(a.ShortName, b.ShortName);
            Assert.Equal(a.Role, b.Role);
            Assert.Equal(a.EntityType, b.EntityType);
            Assert.Equal(a.Domicile, b.Domicile);
            Assert.Equal(a.Description, b.Description);
        }

        Assert.Equal(doc.Counts.Count, docCopy.Counts.Count);
        for (var i = 0; i < doc.Counts.Count; i++)
        {
            var a = doc.Counts[i];
            var b = docCopy.Counts[i];
            Assert.Equal(a.Id, b.Id);
            Assert.Equal(a.Number, b.Number);
            Assert.Equal(a.Title, b.Title);
            Assert.Equal(a.AgainstPartyIds, b.AgainstPartyIds);
            Assert.Equal(a.LegalBasis, b.LegalBasis);
            Assert.Equal(a.Summary, b.Summary);
        }

        Assert.Equal(doc.FactPlan.Count, docCopy.FactPlan.Count);
        for (var i = 0; i < doc.FactPlan.Count; i++)
        {
            var a = doc.FactPlan[i];
            var b = docCopy.FactPlan[i];
            Assert.Equal(a.Id, b.Id);
            Assert.Equal(a.Heading, b.Heading);
            Assert.Equal(a.StartDate, b.StartDate);
            Assert.Equal(a.EndDate, b.EndDate);
            Assert.Equal(a.TargetParagraphs, b.TargetParagraphs);
            Assert.Equal(a.Focus, b.Focus);
        }

        Assert.Equal(doc.Sections.Count, docCopy.Sections.Count);
        for (var i = 0; i < doc.Sections.Count; i++)
            AssertSectionEqual(doc.Sections[i], docCopy.Sections[i]);
    }

    private static void AssertSectionEqual(CoreDocumentSection expected, CoreDocumentSection actual)
    {
        Assert.Equal(expected.Id, actual.Id);
        Assert.Equal(expected.Kind, actual.Kind);
        Assert.Equal(expected.Heading, actual.Heading);
        Assert.Equal(expected.CountId, actual.CountId);
        Assert.Equal(expected.Paragraphs, actual.Paragraphs);
        Assert.Equal(expected.Status, actual.Status);
        Assert.Equal(expected.FailureMessage, actual.FailureMessage);
        Assert.Equal(expected.IsUserEdited, actual.IsUserEdited);
    }
}
