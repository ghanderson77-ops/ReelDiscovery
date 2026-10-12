using ReelDiscovery.Models;
using ReelDiscovery.Models.Case;
using ReelDiscovery.Models.Corpus;
using ReelDiscovery.Models.Discovery;
using ReelDiscovery.Services.Corpus;
using ReelDiscovery.Services.Discovery;
using ReelDiscovery.Services.Evidence;
using ReelDiscovery.Tests.Evidence;
using ReelDiscovery.UserControls;

namespace ReelDiscovery.Tests.Discovery;

public class DiscoveryStepModelTests
{
    private const string Folder = @"C:\Cases\Brightline v. Harmon";

    private static readonly AIModelConfig Story = new()
    {
        DisplayName = "Story Model", InputTokenPricePerMillion = 3m, OutputTokenPricePerMillion = 15m
    };

    private static CaseDraft Draft()
    {
        var draft = DiscoveryTestData.LitigationDraft();
        DiscoveryStepModel.ApplyDefaults(draft);
        return draft;
    }

    private static DiscoveryStepModel Build(CaseDraft draft, DiscoveryDocumentSet? set = null, AIModelConfig? model = null,
        Func<string, bool>? exists = null) =>
        DiscoveryStepModel.Build(draft, set ?? new DiscoveryDocumentSet(), DiscoveryCorpus.None(), model ?? Story, Folder,
            exists ?? (_ => true));

    private static DiscoveryDocument SavedDeposition(CaseDraft draft, string witnessId, string date = "2025-06-01")
    {
        var doc = new DiscoveryDocument
        {
            Key = "DEP-" + witnessId,
            Kind = DiscoveryKind.Deposition,
            WitnessId = witnessId,
            FileName = $"Deposition {witnessId}.docx",
            Status = DiscoveryStatus.Generated,
            FileSaved = true,
            TestimonyPages = 25,
            CostUsd = 1.25m,
            Deposition = new DepositionContent { WitnessId = witnessId, WitnessName = "Saved Witness", Date = date }
        };
        DiscoveryStaleness.Stamp(doc, draft, null);
        return doc;
    }

    private static DiscoveryDocument SavedSet(CaseDraft draft, DiscoveryKind kind, string key)
    {
        var doc = new DiscoveryDocument
        {
            Key = key,
            Kind = kind,
            FileName = key + ".docx",
            Status = DiscoveryStatus.Generated,
            FileSaved = true,
            RequestCount = 12,
            Written = new WrittenDiscoveryContent()
        };
        DiscoveryStaleness.Stamp(doc, draft, null);
        return doc;
    }

    // ---- Defaults ----

    [Fact]
    public void ApplyDefaults_NoSettings_CreatesPresetWithWitnessesAndParties()
    {
        var draft = DiscoveryTestData.LitigationDraft();
        draft.Discovery = null;

        DiscoveryStepModel.ApplyDefaults(draft);

        var expected = DiscoverySettings.ForPreset(draft.Complexity);
        Assert.NotNull(draft.Discovery);
        Assert.Equal(expected.RfpCount, draft.Discovery!.RfpCount);
        Assert.Equal(expected.PagesPerExcerpt, draft.Discovery.PagesPerExcerpt);
        Assert.Equal(DepositionPlanner.DefaultWitnessIds(draft), draft.Discovery.WitnessIds);
        var parties = WrittenDiscoveryPlanner.ResolveParties(draft.CoreDocument, null);
        Assert.Equal(parties.Propounding!.Id, draft.Discovery.PropoundingPartyId);
        Assert.Equal(parties.Responding!.Id, draft.Discovery.RespondingPartyId);
        Assert.False(draft.Discovery.UserTouched);
    }

    [Fact]
    public void ApplyDefaults_UserTouched_ChangesNothing()
    {
        var draft = DiscoveryTestData.LitigationDraft();
        var touched = new DiscoverySettings { RfpCount = 77, UserTouched = true, WitnessIds = new List<string> { "CH-001" } };
        draft.Discovery = touched;
        draft.Complexity = ComplexityLevel.Complex;

        DiscoveryStepModel.ApplyDefaults(draft);

        Assert.Same(touched, draft.Discovery);
        Assert.Equal(77, draft.Discovery!.RfpCount);
        Assert.Equal(new[] { "CH-001" }, draft.Discovery.WitnessIds);
    }

    [Fact]
    public void ApplyDefaults_Untouched_ComplexityChange_ReappliesPreset()
    {
        var draft = Draft();
        Assert.Equal(25, draft.Discovery!.RfpCount);

        draft.Complexity = ComplexityLevel.Complex;
        DiscoveryStepModel.ApplyDefaults(draft);

        Assert.Equal(40, draft.Discovery!.RfpCount);
        Assert.Equal(40, draft.Discovery.PagesPerExcerpt);
        Assert.Equal(DepositionPlanner.DefaultWitnessIds(draft), draft.Discovery.WitnessIds);
    }

    // ---- Rows ----

    [Fact]
    public void Build_NoGuide_NoRows()
    {
        var model = Build(DiscoveryTestData.NoGuideDraft());

        Assert.False(model.HasGuide);
        Assert.Empty(model.Rows);
        Assert.Empty(model.ToWrite);
    }

    [Fact]
    public void Build_StandardCivil_NoSidecar_FiveNotGeneratedRows()
    {
        var draft = Draft();

        var model = Build(draft);

        Assert.True(model.HasGuide);
        Assert.Equal(5, model.Rows.Count);
        Assert.All(model.Rows.Take(2), r => Assert.Equal(DiscoveryKind.Deposition, r.Kind));
        Assert.Equal(new[] { "RFP", "ROG", "RFA" }, model.Rows.Skip(2).Select(r => r.Key));
        Assert.All(model.Rows, r => Assert.Equal(DiscoveryRowState.NotGenerated, r.State));
        Assert.Equal(5, model.ToWrite.Count);
        Assert.True(model.EstimateUsd > 0m);
        Assert.Equal(model.EstimateByKey.Values.Sum(), model.EstimateUsd);
        Assert.StartsWith("Deposition of ", model.Rows[0].ShortTitle);
        Assert.Equal(DiscoveryUiText.EstimateCondition.SomeToWrite, model.Condition(keyReady: true));
        Assert.Equal(DiscoveryUiText.EstimateCondition.NoKey, model.Condition(keyReady: false));
    }

    [Fact]
    public void Build_NoModel_ZeroEstimate()
    {
        var draft = Draft();

        var model = DiscoveryStepModel.Build(draft, new DiscoveryDocumentSet(), DiscoveryCorpus.None(), null, Folder, _ => true);

        Assert.Equal(5, model.ToWrite.Count);
        Assert.Equal(0m, model.EstimateUsd);
    }

    [Fact]
    public void Build_SavedDeposition_GuideEdited_IsOutOfDateAndNotWritten()
    {
        var draft = Draft();
        var witness = draft.Discovery!.WitnessIds[0];
        var set = new DiscoveryDocumentSet();
        set.Upsert(SavedDeposition(draft, witness));

        var clean = Build(draft, set);
        Assert.Equal(DiscoveryRowState.Generated, clean.Rows.Single(r => r.Key == "DEP-" + witness).State);

        draft.Guide!.FindFact("F-001")!.Description += " Edited.";
        var model = Build(draft, set);

        var row = model.Rows.Single(r => r.Key == "DEP-" + witness);
        Assert.Equal(DiscoveryRowState.OutOfDate, row.State);
        Assert.Equal(StaleSource.Guide, row.Stale);
        Assert.DoesNotContain(model.ToWrite, i => i.Key == "DEP-" + witness);
        Assert.Equal(4, model.ToWrite.Count);
        Assert.Equal(1.25m, model.SpentUsd);
    }

    [Fact]
    public void Build_SavedSets_NotSavedFileMissingFailed()
    {
        var draft = Draft();
        var set = new DiscoveryDocumentSet();
        var rfp = SavedSet(draft, DiscoveryKind.RequestsForProduction, "RFP");
        rfp.FileSaved = false;
        set.Upsert(rfp);
        set.Upsert(SavedSet(draft, DiscoveryKind.Interrogatories, "ROG"));
        var rfa = SavedSet(draft, DiscoveryKind.RequestsForAdmission, "RFA");
        rfa.FailureReason = "The model returned no requests";
        rfa.Status = DiscoveryStatus.Failed;
        rfa.Written = null;
        set.Upsert(rfa);

        var model = Build(draft, set, exists: path => !path.EndsWith("ROG.docx", StringComparison.OrdinalIgnoreCase));

        Assert.Equal(DiscoveryRowState.NotSaved, model.Rows.Single(r => r.Key == "RFP").State);
        Assert.Equal(DiscoveryRowState.FileMissing, model.Rows.Single(r => r.Key == "ROG").State);
        Assert.Equal(DiscoveryRowState.Failed, model.Rows.Single(r => r.Key == "RFA").State);
        Assert.Contains(model.ToWrite, i => i.Key == "RFA");
        Assert.DoesNotContain(model.ToWrite, i => i.Key is "RFP" or "ROG");
    }

    [Fact]
    public void Build_AdmissionsLowered_HintAndEstimateUseEffectiveCount()
    {
        var draft = Draft();

        var model = Build(draft);

        var count = model.Counts[DiscoveryKind.RequestsForAdmission];
        var dated = draft.Guide!.Facts.Count(f => !string.IsNullOrWhiteSpace(f.Date));
        Assert.True(dated < draft.Discovery!.RfaCount);
        Assert.True(count.Lowered);
        Assert.Equal(dated, count.Effective);
        var label = DiscoveryUiText.KindSentenceLabel(DiscoveryKind.RequestsForAdmission, DiscoveryVariant.Civil);
        Assert.EndsWith(DiscoveryUiText.LoweredSentence(label, count.Effective), model.CountHint(DiscoveryKind.RequestsForAdmission));

        var rfa = model.ToWrite.Single(i => i.Key == "RFA");
        Assert.Equal(count.Effective, rfa.RequestCount);
        Assert.Equal(GenerationCostEstimator.PriceDiscoveryWrittenSet(1, count.Effective, Story), model.EstimateByKey["RFA"]);
        Assert.Equal("", model.CountHint(DiscoveryKind.Interrogatories));
    }

    [Fact]
    public void Build_SavedDepositionOfUncheckedWitness_StillARow()
    {
        var draft = Draft();
        var set = new DiscoveryDocumentSet();
        set.Upsert(SavedDeposition(draft, "CH-013", "2025-05-01"));
        draft.Discovery!.WitnessIds = draft.Discovery.WitnessIds.Where(id => id != "CH-013").ToList();
        draft.Discovery.UserTouched = true;

        var model = Build(draft, set);

        var row = model.Rows.Single(r => r.Key == "DEP-CH-013");
        Assert.Equal(DiscoveryRowState.Generated, row.State);
        Assert.DoesNotContain(model.Selected, i => i.Key == "DEP-CH-013");
    }

    [Fact]
    public void Build_NothingSelected_NothingSaved_NoRows()
    {
        var draft = Draft();
        draft.Discovery = new DiscoverySettings
        {
            UserTouched = true, RfpEnabled = false, RogEnabled = false, RfaEnabled = false, WitnessIds = new List<string>()
        };

        var model = Build(draft);

        Assert.Empty(model.Rows);
        Assert.Equal(DiscoveryUiText.EstimateCondition.NothingSelected, model.Condition(true));
        Assert.False(model.CanGenerate(true, true));
    }

    [Fact]
    public void Build_EverythingWritten_AllWritten()
    {
        var draft = Draft();
        draft.Discovery!.RogEnabled = false;
        draft.Discovery.RfaEnabled = false;
        draft.Discovery.WitnessIds = new List<string>();
        var set = new DiscoveryDocumentSet();
        set.Upsert(SavedSet(draft, DiscoveryKind.RequestsForProduction, "RFP"));

        var model = Build(draft, set);

        Assert.Single(model.Rows);
        Assert.Empty(model.ToWrite);
        Assert.Equal(DiscoveryUiText.EstimateCondition.AllWritten, model.Condition(true));
        Assert.Equal(0m, model.EstimateUsd);
    }

    [Fact]
    public void SavedRows_CleanWarningsFailed()
    {
        var draft = Draft();
        var set = new DiscoveryDocumentSet();
        set.Upsert(SavedSet(draft, DiscoveryKind.RequestsForProduction, "RFP"));
        var rog = SavedSet(draft, DiscoveryKind.Interrogatories, "ROG");
        rog.Status = DiscoveryStatus.GeneratedWithWarnings;
        set.Upsert(rog);
        var rfa = SavedSet(draft, DiscoveryKind.RequestsForAdmission, "RFA");
        rfa.Written = null;
        rfa.Status = DiscoveryStatus.Failed;
        rfa.FailureReason = "Timed out";
        set.Upsert(rfa);

        var rows = DiscoveryStepModel.SavedRows(draft, set, Folder, _ => true);

        Assert.Equal(new[] { "RFP", "ROG", "RFA" }, rows.Select(r => r.Key));
        Assert.Equal(new[] { DiscoveryRowState.Generated, DiscoveryRowState.Warnings, DiscoveryRowState.Failed },
            rows.Select(r => r.State));
        Assert.Equal("Requests for Production (12)", rows[0].ShortTitle);
    }

    // ---- Validation ----

    [Theory]
    [InlineData("5", false)]
    [InlineData("10", true)]
    [InlineData("60", true)]
    [InlineData("61", false)]
    [InlineData("x", false)]
    [InlineData("", false)]
    [InlineData("-20", false)]
    [InlineData("2 0", false)]
    public void TryParsePages_Range(string text, bool ok)
    {
        Assert.Equal(ok, DiscoveryStepModel.TryParsePages(text, out var pages));
        if (ok)
            Assert.Equal(int.Parse(text), pages);
    }

    [Theory]
    [InlineData(DiscoveryKind.RequestsForProduction, "99", false)]
    [InlineData(DiscoveryKind.RequestsForProduction, "80", true)]
    [InlineData(DiscoveryKind.RequestsForProduction, "4", false)]
    [InlineData(DiscoveryKind.Interrogatories, "26", false)]
    [InlineData(DiscoveryKind.Interrogatories, "25", true)]
    [InlineData(DiscoveryKind.RequestsForAdmission, "50", true)]
    [InlineData(DiscoveryKind.RequestsForAdmission, "0", false)]
    [InlineData(DiscoveryKind.Deposition, "10", false)]
    public void TryParseCount_Ranges(DiscoveryKind kind, string text, bool ok)
    {
        Assert.Equal(ok, DiscoveryStepModel.TryParseCount(kind, text, out _));
    }

    [Fact]
    public void CanGenerate_Gates()
    {
        var draft = Draft();
        var model = Build(draft);

        Assert.True(model.CanGenerate(true, true));
        Assert.False(model.CanGenerate(false, true));
        Assert.False(model.CanGenerate(true, false));

        draft.Discovery!.RespondingPartyId = draft.Discovery.PropoundingPartyId;
        var same = Build(draft);
        Assert.True(same.PartyChoice.Same);
        Assert.False(same.CanGenerate(true, true));
    }

    // ----- 09 W3: exhibit staleness -----

    // A deposition citing RD-000001 (thread T-0001) and an RFA set citing RD-000002 (thread T-0002), stamped against the
    // corpus as it is now, saved to the sidecar with their .docx files present
    private static async Task<DiscoveryDocumentSet> SaveCitingDocsAsync(string folder, CaseDraft draft, DiscoveryCorpus corpus)
    {
        var deposition = new DiscoveryDocument
        {
            Key = "DEP-CH-003",
            Kind = DiscoveryKind.Deposition,
            WitnessId = "CH-003",
            FileName = "Deposition CH-003.docx",
            Status = DiscoveryStatus.Generated,
            FileSaved = true,
            TestimonyPages = 25,
            Deposition = new DepositionContent
            {
                WitnessId = "CH-003",
                WitnessName = "Saved Witness",
                Date = "2025-06-01",
                Exhibits = { new DepositionExhibit { Number = 1, Description = "Email", DocId = "RD-000001", Date = "2024-03-04" } }
            }
        };
        var rfa = new DiscoveryDocument
        {
            Key = "RFA",
            Kind = DiscoveryKind.RequestsForAdmission,
            FileName = "RFA.docx",
            Status = DiscoveryStatus.Generated,
            FileSaved = true,
            RequestCount = 1,
            Written = new WrittenDiscoveryContent
            {
                Requests = { new DiscoveryRequest { Number = 1, Text = "Admit the email is genuine.", DocId = "RD-000002", IsGenuineness = true } }
            }
        };
        DiscoveryStaleness.Stamp(deposition, draft, corpus);
        DiscoveryStaleness.Stamp(rfa, draft, corpus);

        var set = new DiscoveryDocumentSet { CaseId = draft.CaseId };
        set.Upsert(deposition);
        set.Upsert(rfa);
        await DiscoveryStore.SaveAsync(folder, set);
        Directory.CreateDirectory(DiscoveryStore.FolderPath(folder));
        foreach (var doc in set.Documents)
            await File.WriteAllBytesAsync(DiscoveryStore.DocxPath(folder, doc.FileName)!, new byte[] { 1 });
        return set;
    }

    private static DiscoveryRowInfo RowFor(List<DiscoveryRowInfo> rows, string key) => rows.Single(r => r.Key == key);

    [Fact]
    public async Task ExhibitStaleness_StartOver_MarksCitingDocumentsOutOfDate()
    {
        var folder = EvidenceTestData.TempCaseFolder();
        try
        {
            var draft = await DiscoveryCorpusFixture.WithWrittenCorpusAsync(folder, relevant: 4, written: 3);
            var corpus = CorpusDetector.Detect(draft);
            Assert.NotNull(corpus.WrittenStamps);
            var set = await SaveCitingDocsAsync(folder, draft, corpus);

            var before = DiscoveryStepModel.SavedRows(draft, set, folder, _ => true, corpus.WrittenStamps);
            Assert.Equal(2, before.Count);
            Assert.All(before, r => Assert.Equal(DiscoveryRowState.Generated, r.State));

            var reset = await GenerationReset.StartOverAsync(folder, DiscoveryCorpusFixture.PlanOf(draft), draft.Guide);
            Assert.Empty(reset.Errors);

            var after = DiscoveryStepModel.SavedRows(draft, set, folder, _ => true, CorpusDetector.Detect(draft).WrittenStamps);
            Assert.Equal(2, after.Count);
            Assert.All(after, r =>
            {
                Assert.Equal(DiscoveryRowState.OutOfDate, r.State);
                Assert.Equal(StaleSource.Exhibits, r.Stale);
            });

            // Unknown stamps never stale: before the probe finishes nothing flickers to Out of date
            var unknown = DiscoveryStepModel.SavedRows(draft, set, folder, _ => true, currentStamps: null);
            Assert.All(unknown, r => Assert.Equal(DiscoveryRowState.Generated, r.State));
            var model = DiscoveryStepModel.Build(draft, set, DiscoveryCorpus.None(), Story, folder, _ => true);
            Assert.NotEmpty(model.Rows);
            Assert.DoesNotContain(model.Rows, r => r.State == DiscoveryRowState.OutOfDate);
        }
        finally
        {
            EvidenceTestData.DeleteFolder(folder);
        }
    }

    [Fact]
    public async Task ExhibitStaleness_RegenerateThread_MarksOnlyItsCitersAndStaysAfterRewrite()
    {
        var folder = EvidenceTestData.TempCaseFolder();
        try
        {
            var draft = await DiscoveryCorpusFixture.WithWrittenCorpusAsync(folder, relevant: 4, written: 3);
            var plan = DiscoveryCorpusFixture.PlanOf(draft);
            var set = await SaveCitingDocsAsync(folder, draft, CorpusDetector.Detect(draft));

            var result = await ThreadInvalidation.InvalidateAsync(folder, plan, draft.Guide!, "T-0001");
            Assert.True(result.Appended);

            var rows = DiscoveryStepModel.SavedRows(draft, set, folder, _ => true, CorpusDetector.Detect(draft).WrittenStamps);
            Assert.Equal(DiscoveryRowState.OutOfDate, RowFor(rows, "DEP-CH-003").State);
            Assert.Equal(StaleSource.Exhibits, RowFor(rows, "DEP-CH-003").Stale);
            Assert.Equal(DiscoveryRowState.Generated, RowFor(rows, "RFA").State);

            // Continue Generating writes the thread again with a later ThreadDone: the cited email changed, still Out of date
            var rewrite = DiscoveryCorpusFixture.Done(plan.PlanId, plan.Threads.Single(t => t.Id == "T-0001"));
            rewrite.Utc = rewrite.Utc.AddHours(2);
            await new GenerationJournal(folder).AppendAsync(rewrite);

            var corpus = CorpusDetector.Detect(draft);
            Assert.True(corpus.WrittenStamps!.ContainsKey("RD-000001"));
            rows = DiscoveryStepModel.SavedRows(draft, set, folder, _ => true, corpus.WrittenStamps);
            Assert.Equal(DiscoveryRowState.OutOfDate, RowFor(rows, "DEP-CH-003").State);
            Assert.Equal(StaleSource.Exhibits, RowFor(rows, "DEP-CH-003").Stale);
            Assert.Equal(DiscoveryRowState.Generated, RowFor(rows, "RFA").State);
        }
        finally
        {
            EvidenceTestData.DeleteFolder(folder);
        }
    }

    [Fact]
    public async Task ExhibitStaleness_CaseSummaryRow_AfterStartOver_ShowsOutOfDate()
    {
        var folder = EvidenceTestData.TempCaseFolder();
        try
        {
            var draft = await DiscoveryCorpusFixture.WithWrittenCorpusAsync(folder, relevant: 4, written: 3);
            var plan = DiscoveryCorpusFixture.PlanOf(draft);
            await SaveCitingDocsAsync(folder, draft, CorpusDetector.Detect(draft));
            StepCaseSummary.QaSnapshot Load() => StepCaseSummary.LoadQaWithDiscovery(
                (folder, plan, draft.Guide!, new ReviewProtocolProse(), _ => null, draft));

            var before = Load();
            Assert.StartsWith("2 documents", before.Discovery.Line3);
            Assert.DoesNotContain("out of date", before.Discovery.Line3);

            var reset = await GenerationReset.StartOverAsync(folder, plan, draft.Guide);
            Assert.Empty(reset.Errors);

            var after = Load();
            Assert.Contains(" · 2 out of date", after.Discovery.Line3);
            Assert.Equal(SummaryTone.Warning, after.Discovery.Line3Tone);
        }
        finally
        {
            EvidenceTestData.DeleteFolder(folder);
        }
    }
}
