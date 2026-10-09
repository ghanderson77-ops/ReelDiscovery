using ReelDiscovery.Models;
using ReelDiscovery.Models.Case;
using ReelDiscovery.Models.Discovery;
using ReelDiscovery.Services.Corpus;
using ReelDiscovery.Services.Discovery;
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
}
