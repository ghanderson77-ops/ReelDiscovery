using System.Text.Json;
using ReelDiscovery.Helpers;
using ReelDiscovery.Models;
using ReelDiscovery.Models.Case;
using ReelDiscovery.Models.Corpus;
using ReelDiscovery.Tests.Case;

namespace ReelDiscovery.Tests.Corpus;

public class CorpusSettingsTests
{
    [Fact]
    public void Apportion_EqualWeights_LargestRemainderTieBreakByIndex()
    {
        Assert.Equal(new List<int> { 4, 3, 3 }, Apportion.Largest(10, new double[] { 1, 1, 1 }));
    }

    [Fact]
    public void Apportion_WithMinimum_SumsToTotalAndRespectsMinimum()
    {
        var result = Apportion.Largest(7, new double[] { 3, 2, 1 }, 1);

        Assert.Equal(7, result.Sum());
        Assert.All(result, x => Assert.True(x >= 1));
    }

    [Fact]
    public void Apportion_AllZeroWeights_EqualSplit()
    {
        Assert.Equal(new List<int> { 2, 2, 2 }, Apportion.Largest(6, new double[] { 0, 0, 0 }));
    }

    [Fact]
    public void Apportion_TotalBelowMinimum_EveryItemGetsMinimum()
    {
        Assert.Equal(new List<int> { 1, 1, 1, 1 }, Apportion.Largest(2, new double[] { 1, 2, 3, 4 }, 1));
    }

    [Fact]
    public void Apportion_NoItems_EmptyList()
    {
        Assert.Empty(Apportion.Largest(10, Array.Empty<double>()));
    }

    [Theory]
    [InlineData(1000, 20, 200)]
    [InlineData(250, 5, 13)]
    [InlineData(50, 60, 30)]
    public void RelevantCount_RoundsAwayFromZero(int total, int percent, int expected)
    {
        Assert.Equal(expected, CorpusSettings.RelevantCount(total, percent));
    }

    [Fact]
    public void NonRelevantEmails_IsTotalMinusRelevant()
    {
        var settings = new CorpusSettings { TotalEmails = 250, RelevantPercent = 5 };

        Assert.Equal(13, settings.RelevantEmails);
        Assert.Equal(237, settings.NonRelevantEmails);
    }

    [Theory]
    [InlineData(ComplexityLevel.Simple, 250)]
    [InlineData(ComplexityLevel.Standard, 1000)]
    [InlineData(ComplexityLevel.Complex, 2500)]
    public void DefaultTotalFor_MapsComplexity(ComplexityLevel level, int expected)
    {
        Assert.Equal(expected, CorpusSettings.DefaultTotalFor(level));
    }

    [Fact]
    public void CreateDefault_FromSampleGuidedDraft()
    {
        var draft = CaseTestData.SampleGuidedDraft();
        draft.Complexity = ComplexityLevel.Standard;

        var settings = CorpusSettings.CreateDefault(draft);

        Assert.Equal(1000, settings.TotalEmails);
        Assert.Equal(20, settings.RelevantPercent);
        Assert.Equal(50, settings.DailyBusinessPercent);
        Assert.Equal(20, settings.AncillaryPercent);
        Assert.Equal(20, settings.NewsletterPercent);
        Assert.Equal(10, settings.DecoyPercent);
        Assert.Equal("2023-12-15", settings.WindowStart);
        Assert.Equal("2024-08-20", settings.WindowEnd);
        Assert.True(settings.IncludeOffice);
        Assert.True(settings.IncludeCalendar);
        Assert.True(settings.IncludeImages);
        Assert.True(settings.IncludeVoicemail);
        Assert.Null(settings.HotPerHotFact);
        Assert.Null(settings.PrivilegedPercent);
        Assert.Null(settings.AttachmentPercent);
        Assert.Equal("", settings.DocIdPrefix);
    }

    [Fact]
    public void DefaultWindow_NoDatedFacts_Empty()
    {
        var guide = new CaseGuide();
        guide.Facts.Add(new KeyFact { Id = "F-001", Date = "" });

        Assert.Equal(("", ""), CorpusSettings.DefaultWindow(guide));
        Assert.Equal(("", ""), CorpusSettings.DefaultWindow(null));
    }

    [Fact]
    public void DefaultWindow_FactsAtDateTimeEdges_DoesNotThrow()
    {
        // WR-04: AddMonths used to throw for facts near DateTime.MinValue / MaxValue
        var guide = new CaseGuide();
        guide.Facts.Add(new KeyFact { Id = "F-001", Date = "0001-01-15" });
        guide.Facts.Add(new KeyFact { Id = "F-002", Date = "9999-11-20" });

        var (start, end) = CorpusSettings.DefaultWindow(guide);

        Assert.Equal("0001-01-01", start);
        Assert.Equal("9999-12-31", end);
    }

    [Fact]
    public void SafeAddMonths_InRange_MatchesAddMonths()
    {
        var date = new DateTime(2024, 3, 31);
        Assert.Equal(date.AddMonths(-1), CorpusSettings.SafeAddMonths(date, -1));
        Assert.Equal(date.AddMonths(2), CorpusSettings.SafeAddMonths(date, 2));
    }

    [Fact]
    public void ClampWindow_KeepsEveryFactInside()
    {
        var guide = CaseTestData.SampleGuidedDraft().Guide;

        var clamp = CorpusSettings.ClampWindow("2024-02-01", "2024-06-01", guide);

        Assert.Equal("2024-01-15", clamp.Start);
        Assert.Equal("2024-06-20", clamp.End);
        Assert.True(clamp.StartClamped);
        Assert.True(clamp.EndClamped);
        Assert.False(clamp.StartInvalid);
        Assert.False(clamp.EndInvalid);
        Assert.False(clamp.EndBeforeStart);
    }

    [Fact]
    public void ClampWindow_WindowAlreadyCoversFacts_Unchanged()
    {
        var guide = CaseTestData.SampleGuidedDraft().Guide;

        var clamp = CorpusSettings.ClampWindow("2023-12-15", "2024-08-20", guide);

        Assert.Equal("2023-12-15", clamp.Start);
        Assert.Equal("2024-08-20", clamp.End);
        Assert.False(clamp.StartClamped);
        Assert.False(clamp.EndClamped);
    }

    [Fact]
    public void ClampWindow_InvalidStart_Flagged()
    {
        var guide = CaseTestData.SampleGuidedDraft().Guide;

        var clamp = CorpusSettings.ClampWindow("bad", "2024-08-20", guide);

        Assert.True(clamp.StartInvalid);
        Assert.False(clamp.EndInvalid);
    }

    [Fact]
    public void ClampWindow_EndBeforeStart_Flagged()
    {
        var clamp = CorpusSettings.ClampWindow("2024-09-01", "2024-08-01", null);

        Assert.True(clamp.EndBeforeStart);
    }

    [Fact]
    public void EnabledAttachmentTypes_NoMedia_ExcludesImageAndVoicemail()
    {
        var settings = new CorpusSettings();

        Assert.Equal(
            new[] { AttachmentType.Word, AttachmentType.Excel, AttachmentType.PowerPoint, AttachmentType.CalendarInvite },
            settings.EnabledAttachmentTypes(false));
    }

    [Fact]
    public void EnabledAttachmentTypes_OfficeOff_RemovesOfficeTypes()
    {
        var settings = new CorpusSettings { IncludeOffice = false };

        Assert.Equal(new[] { AttachmentType.CalendarInvite }, settings.EnabledAttachmentTypes(false));
    }

    [Fact]
    public void EnabledAttachmentTypes_MediaAvailable_IncludesImage()
    {
        var settings = new CorpusSettings { IncludeVoicemail = false };

        var types = settings.EnabledAttachmentTypes(true);

        Assert.Contains(AttachmentType.Image, types);
        Assert.DoesNotContain(AttachmentType.Voicemail, types);
    }

    [Theory]
    [InlineData(99999, 5000)]
    [InlineData(3, 50)]
    public void Normalize_ClampsTotal(int input, int expected)
    {
        var settings = new CorpusSettings { TotalEmails = input };

        settings.Normalize();

        Assert.Equal(expected, settings.TotalEmails);
    }

    [Theory]
    [InlineData(0, 5)]
    [InlineData(90, 60)]
    public void Normalize_ClampsRelevantPercent(int input, int expected)
    {
        var settings = new CorpusSettings { RelevantPercent = input };

        settings.Normalize();

        Assert.Equal(expected, settings.RelevantPercent);
    }

    [Fact]
    public void Normalize_RenormalizesMixTo100()
    {
        var settings = new CorpusSettings { DailyBusinessPercent = 40, AncillaryPercent = 40, NewsletterPercent = 40, DecoyPercent = 40 };

        settings.Normalize();

        Assert.Equal(100, settings.MixTotal);
        Assert.Equal(new[] { 25, 25, 25, 25 },
            new[] { settings.DailyBusinessPercent, settings.AncillaryPercent, settings.NewsletterPercent, settings.DecoyPercent });
    }

    [Fact]
    public void Normalize_HugeShares_DoNotOverflow()
    {
        // CR-01: crafted case.json values near int.MaxValue
        var settings = new CorpusSettings
        {
            DailyBusinessPercent = int.MaxValue, AncillaryPercent = 1, NewsletterPercent = int.MaxValue, DecoyPercent = 0
        };

        settings.Normalize();

        Assert.Equal(100, settings.MixTotal);
        Assert.Equal(0, settings.DecoyPercent);
    }

    [Fact]
    public void Normalize_NegativeShareBecomesZero()
    {
        var settings = new CorpusSettings { DailyBusinessPercent = -10, AncillaryPercent = 50, NewsletterPercent = 30, DecoyPercent = 20 };

        settings.Normalize();

        Assert.Equal(0, settings.DailyBusinessPercent);
        Assert.Equal(100, settings.MixTotal);
    }

    [Fact]
    public void Normalize_AllZeroMix_UsesDefault()
    {
        var settings = new CorpusSettings { DailyBusinessPercent = 0, AncillaryPercent = 0, NewsletterPercent = 0, DecoyPercent = 0 };

        settings.Normalize();

        Assert.Equal(new[] { 50, 20, 20, 10 },
            new[] { settings.DailyBusinessPercent, settings.AncillaryPercent, settings.NewsletterPercent, settings.DecoyPercent });
    }

    [Fact]
    public void Normalize_NullStringsBecomeEmpty()
    {
        var settings = JsonSerializer.Deserialize<CorpusSettings>(
            "{\"WindowStart\":null,\"WindowEnd\":null,\"DocIdPrefix\":null}", CaseDraft.JsonOptions)!;

        settings.Normalize();

        Assert.Equal("", settings.WindowStart);
        Assert.Equal("", settings.WindowEnd);
        Assert.Equal("", settings.DocIdPrefix);
    }

    [Fact]
    public void Normalize_ClampsOverrides()
    {
        var settings = new CorpusSettings { HotPerHotFact = 9, PrivilegedPercent = 50, AttachmentPercent = 99 };

        settings.Normalize();

        Assert.Equal(5, settings.HotPerHotFact);
        Assert.Equal(30, settings.PrivilegedPercent);
        Assert.Equal(60, settings.AttachmentPercent);
    }

    [Fact]
    public void Normalize_NegativeOverridesBecomeAutomatic()
    {
        var settings = new CorpusSettings { HotPerHotFact = -1, PrivilegedPercent = -5, AttachmentPercent = -2 };

        settings.Normalize();

        Assert.Null(settings.HotPerHotFact);
        Assert.Null(settings.PrivilegedPercent);
        Assert.Null(settings.AttachmentPercent);
    }

    [Theory]
    [InlineData("d m!", "DM")]
    [InlineData("abcdefghij", "ABCDEF")]
    [InlineData("..\\=X1", "X")]
    public void Normalize_SanitizesDocIdPrefix(string input, string expected)
    {
        var settings = new CorpusSettings { DocIdPrefix = input };

        settings.Normalize();

        Assert.Equal(expected, settings.DocIdPrefix);
    }

    [Fact]
    public void ComputeHash_ChangesWithFieldsAndStableAcrossRoundTrip()
    {
        var settings = new CorpusSettings { WindowStart = "2024-01-01", WindowEnd = "2024-12-31" };
        var hash = settings.ComputeHash();

        var copy = JsonSerializer.Deserialize<CorpusSettings>(JsonSerializer.Serialize(settings, CaseDraft.JsonOptions), CaseDraft.JsonOptions)!;
        Assert.Equal(hash, copy.ComputeHash());

        copy.TotalEmails = 2500;
        Assert.NotEqual(hash, copy.ComputeHash());

        var other = new CorpusSettings { WindowStart = "2024-01-01", WindowEnd = "2024-12-31", IncludeImages = false };
        Assert.NotEqual(hash, other.ComputeHash());

        var withOverride = new CorpusSettings { WindowStart = "2024-01-01", WindowEnd = "2024-12-31", PrivilegedPercent = 10 };
        Assert.NotEqual(hash, withOverride.ComputeHash());
    }

    [Fact]
    public void CorpusState_Normalize_RepairsNulls()
    {
        var state = JsonSerializer.Deserialize<CorpusState>(
            "{\"Settings\":null,\"WrittenFiles\":null,\"PlanGuideHash\":null,\"PlanSettingsHash\":null,\"DismissedStaleGuideHash\":null,\"DismissedSettingsHash\":null}",
            CaseDraft.JsonOptions)!;

        state.Normalize();

        Assert.NotNull(state.Settings);
        Assert.Equal(1000, state.Settings.TotalEmails);
        Assert.Empty(state.WrittenFiles);
        Assert.Equal("", state.PlanGuideHash);
        Assert.Equal("", state.PlanSettingsHash);
        Assert.Equal("", state.DismissedStaleGuideHash);
        Assert.Equal("", state.DismissedSettingsHash);
    }

    [Fact]
    public void CorpusState_Normalize_NormalizesSettings()
    {
        var state = new CorpusState { Settings = new CorpusSettings { TotalEmails = 1 } };

        state.Normalize();

        Assert.Equal(50, state.Settings.TotalEmails);
    }

    [Fact]
    public void CorpusState_HasPlan_FollowsPlanId()
    {
        var state = new CorpusState();
        Assert.False(state.HasPlan);

        state.PlanId = Guid.NewGuid();
        Assert.True(state.HasPlan);
    }

    [Fact]
    public void CorpusState_PlanNotSerialized()
    {
        var state = new CorpusState { PlanId = Guid.NewGuid(), Plan = new CorpusPlan { Notes = { "secret-note" } } };

        var json = JsonSerializer.Serialize(state, CaseDraft.JsonOptions);

        Assert.DoesNotContain("\"Plan\"", json);
        Assert.DoesNotContain("secret-note", json);
        Assert.DoesNotContain("HasPlan", json);
        Assert.Contains("\"PlanId\"", json);
    }
}
