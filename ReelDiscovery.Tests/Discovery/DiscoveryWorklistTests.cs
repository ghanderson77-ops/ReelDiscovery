using ReelDiscovery.Models.Discovery;
using ReelDiscovery.Services.Discovery;

namespace ReelDiscovery.Tests.Discovery;

public class DiscoveryWorklistTests
{
    private static DiscoverySettings TwoWitnesses(ReelDiscovery.Models.Case.CaseDraft draft)
    {
        var settings = DiscoverySettings.ForPreset(draft.Complexity);
        settings.WitnessIds = DepositionPlanner.DefaultWitnessIds(draft);
        return settings;
    }

    [Fact]
    public void Build_Litigation_DepositionsInDateOrderThenRfpRogRfa()
    {
        var draft = DiscoveryTestData.LitigationDraft();
        var settings = TwoWitnesses(draft);
        Assert.Equal(2, settings.WitnessIds.Count);

        var items = DiscoveryWorklist.Build(draft, settings, null, null);

        Assert.Equal(5, items.Count);
        Assert.All(items.Take(2), i => Assert.Equal(DiscoveryKind.Deposition, i.Kind));
        Assert.True(items[0].Date <= items[1].Date);
        Assert.Equal(settings.WitnessIds.Select(id => "DEP-" + id).OrderBy(k => k),
            items.Take(2).Select(i => i.Key).OrderBy(k => k));
        Assert.Equal(new[] { "RFP", "ROG", "RFA" }, items.Skip(2).Select(i => i.Key));
        Assert.Equal(new[] { DiscoveryKind.RequestsForProduction, DiscoveryKind.Interrogatories, DiscoveryKind.RequestsForAdmission },
            items.Skip(2).Select(i => i.Kind));
    }

    [Fact]
    public void Build_FileNames_AreSafeAndDescriptive()
    {
        var draft = DiscoveryTestData.LitigationDraft();
        var items = DiscoveryWorklist.Build(draft, TwoWitnesses(draft), null, null);

        Assert.All(items, i => Assert.True(DiscoveryStore.IsDiscoveryFileName(i.FileName), i.FileName));
        var deposition = items[0];
        Assert.Contains($"Deposition of {deposition.WitnessName} (Excerpt)", deposition.FileName);
        Assert.Contains("First Set of Requests for Production", items.Single(i => i.Key == "RFP").FileName);
    }

    [Fact]
    public void Build_WitnessesWithSameName_GetDistinctFileNames()
    {
        // WR-08: two witnesses whose names differ only in case would share (and overwrite) one .docx on Windows
        var draft = DiscoveryTestData.LitigationDraft();
        var settings = TwoWitnesses(draft);
        var first = draft.Guide!.FindCast(settings.WitnessIds[0])!;
        var second = draft.Guide.FindCast(settings.WitnessIds[1])!;
        second.FirstName = first.FirstName.ToUpperInvariant();
        second.LastName = first.LastName;

        var items = DiscoveryWorklist.Build(draft, settings, null, null).Where(i => i.Kind == DiscoveryKind.Deposition).ToList();

        Assert.Equal(2, items.Count);
        Assert.Equal(2, items.Select(i => i.FileName).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.All(items, i => Assert.True(DiscoveryStore.IsDiscoveryFileName(i.FileName), i.FileName));
        Assert.Contains($"({items[1].WitnessId}).docx", items[1].FileName);

        // A saved record keeps owning its name; the other witness is the one renamed, also when regenerated alone
        var set = new DiscoveryDocumentSet();
        set.Upsert(new DiscoveryDocument { Key = items[1].Key, Kind = DiscoveryKind.Deposition, WitnessId = items[1].WitnessId, FileName = items[0].FileName });
        var again = DiscoveryWorklist.Build(draft, settings, null, set).Where(i => i.Kind == DiscoveryKind.Deposition).ToList();
        Assert.Equal(items[0].FileName, again.Single(i => i.Key == items[1].Key).FileName);
        Assert.NotEqual(items[0].FileName, again.Single(i => i.Key == items[0].Key).FileName, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void Build_SavedValidFileName_IsKept_UnsafeOneReplaced()
    {
        var draft = DiscoveryTestData.LitigationDraft();
        var set = new DiscoveryDocumentSet();
        set.Upsert(new DiscoveryDocument { Key = "RFP", Kind = DiscoveryKind.RequestsForProduction, FileName = "Saved RFP.docx" });
        set.Upsert(new DiscoveryDocument { Key = "ROG", Kind = DiscoveryKind.Interrogatories, FileName = "..\\evil.docx" });

        var items = DiscoveryWorklist.Build(draft, TwoWitnesses(draft), null, set);

        Assert.Equal("Saved RFP.docx", items.Single(i => i.Key == "RFP").FileName);
        var rog = items.Single(i => i.Key == "ROG").FileName;
        Assert.NotEqual("..\\evil.docx", rog);
        Assert.True(DiscoveryStore.IsDiscoveryFileName(rog));
    }

    [Fact]
    public void Build_MemoCriminal_OnlyDepositionsAndDocumentRequests()
    {
        var draft = DiscoveryTestData.MemoCriminalDraft();
        var settings = TwoWitnesses(draft);

        var items = DiscoveryWorklist.Build(draft, settings, null, null);

        Assert.Contains(items, i => i.Kind == DiscoveryKind.Deposition);
        Assert.Contains(items, i => i.Key == "RFP");
        Assert.DoesNotContain(items, i => i.Key is "ROG" or "RFA");
        Assert.All(items.Where(i => i.Kind == DiscoveryKind.Deposition), i => Assert.StartsWith("DEP-", i.Key));
    }

    [Fact]
    public void Build_SkipsUncheckedKindsAndUnknownWitnesses()
    {
        var draft = DiscoveryTestData.LitigationDraft();
        var settings = TwoWitnesses(draft);
        settings.WitnessIds = new List<string> { settings.WitnessIds[0], "CH-999" };
        settings.RogEnabled = false;
        settings.RfaEnabled = false;

        var items = DiscoveryWorklist.Build(draft, settings, null, null);

        Assert.Equal(new[] { "DEP-" + settings.WitnessIds[0], "RFP" }, items.Select(i => i.Key));
    }

    [Fact]
    public void Build_PricingInputs_FollowThePlans()
    {
        var draft = DiscoveryTestData.LitigationDraft();
        var items = DiscoveryWorklist.Build(draft, TwoWitnesses(draft), null, null);

        foreach (var item in items)
        {
            if (item.Kind == DiscoveryKind.Deposition)
            {
                Assert.NotNull(item.Deposition);
                Assert.Equal(item.Deposition!.Chunks.Count, item.ChunkCount);
                Assert.True(item.ChunkCount > 0);
                Assert.Equal(item.Deposition.TestimonyPages, item.TestimonyPages);
            }
            else
            {
                Assert.NotNull(item.Written);
                var slots = item.Written!.Slots.Count;
                Assert.Equal(slots, item.RequestCount);
                Assert.Equal((int)Math.Ceiling(slots / 25.0), item.CallCount);
            }
        }
    }

    [Fact]
    public void Build_NoGuide_IsEmpty()
    {
        var draft = DiscoveryTestData.NoGuideDraft();

        Assert.Empty(DiscoveryWorklist.Build(draft, new DiscoverySettings { WitnessIds = new List<string> { "CH-001" } }, null, null));
    }

    [Fact]
    public void ForDocument_UncheckedWitnessAndSet_StillPlanned()
    {
        var draft = DiscoveryTestData.LitigationDraft();
        var settings = TwoWitnesses(draft);
        var witness = settings.WitnessIds[0];
        settings.WitnessIds = new List<string>();
        settings.RfaEnabled = false;
        var set = new DiscoveryDocumentSet();

        var deposition = DiscoveryWorklist.ForDocument(draft,
            new DiscoveryDocument { Key = "DEP-" + witness, Kind = DiscoveryKind.Deposition, WitnessId = witness }, settings, null, set);
        var rfa = DiscoveryWorklist.ForDocument(draft,
            new DiscoveryDocument { Key = "RFA", Kind = DiscoveryKind.RequestsForAdmission }, settings, null, set);

        Assert.NotNull(deposition);
        Assert.Equal("DEP-" + witness, deposition!.Key);
        Assert.NotNull(rfa);
        Assert.Equal("RFA", rfa!.Key);
        Assert.False(settings.RfaEnabled);
    }
}
