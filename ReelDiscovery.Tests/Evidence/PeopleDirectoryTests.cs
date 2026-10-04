using ReelDiscovery.Models;
using ReelDiscovery.Services;
using ReelDiscovery.Services.Evidence;

namespace ReelDiscovery.Tests.Evidence;

public class PeopleDirectoryTests
{
    [Fact]
    public void Find_CastMember_ResolvesNameEmailAndOrganizationDomain()
    {
        var (draft, plan) = EvidenceTestData.ApprovedCase();
        var directory = PeopleDirectory.Create(draft.Guide!, plan);

        var person = directory.Find("CH-001");

        Assert.NotNull(person);
        Assert.Equal("Maria Delgado", person!.FullName);
        Assert.Equal("Maria", person.FirstName);
        Assert.Equal("Delgado", person.LastName);
        Assert.Equal(draft.Guide!.FindCast("CH-001")!.Email, person.Email);
        Assert.Equal("brightline.com", person.Domain);
        Assert.False(person.IsExternal);
        Assert.NotNull(person.Cast);
    }

    [Fact]
    public void Find_External_ResolvesVendor()
    {
        var (draft, plan) = EvidenceTestData.ApprovedCase();
        var directory = PeopleDirectory.Create(draft.Guide!, plan);

        var person = directory.Find(EvidenceTestData.VendorId);

        Assert.NotNull(person);
        Assert.Equal("Dana Whitfield", person!.FullName);
        Assert.Equal("Dana", person.FirstName);
        Assert.Equal("Whitfield", person.LastName);
        Assert.Equal("summitglazingsupply.com", person.Domain);
        Assert.Equal("dana@summitglazingsupply.com", person.Email);
        Assert.True(person.IsExternal);
        Assert.Null(person.Cast);
    }

    [Fact]
    public void Find_EmptyOrganizationDomain_FallsBackToEmailHost()
    {
        var (draft, plan) = EvidenceTestData.ApprovedCase();
        var guide = draft.Guide!;
        var member = guide.FindCast("CH-001")!;
        guide.FindOrganization(member.OrganizationId)!.Domain = "  ";
        member.Email = "maria@Example-Host.COM";

        var person = PeopleDirectory.Create(guide, plan).Find("CH-001");

        Assert.Equal("example-host.com", person!.Domain);
    }

    [Fact]
    public void Find_UnknownId_ReturnsNullAndNameOfReturnsId()
    {
        var (draft, plan) = EvidenceTestData.ApprovedCase();
        var directory = PeopleDirectory.Create(draft.Guide!, plan);

        Assert.Null(directory.Find("CH-999"));
        Assert.Null(directory.Find(null));
        Assert.Equal("CH-999", directory.NameOf("CH-999"));
        Assert.Equal(string.Empty, directory.EmailOf("CH-999"));
        Assert.Equal("Maria Delgado", directory.NameOf("CH-001"));
    }

    [Fact]
    public void External_SingleWordName_IsFirstNameOnly()
    {
        var (draft, plan) = EvidenceTestData.ApprovedCase();
        plan.Externals.Add(new Models.Corpus.ExternalParty { Id = "EXT-090", Name = "Newsdesk", Email = "news@dailybrief.com" });

        var person = PeopleDirectory.Create(draft.Guide!, plan).Find("EXT-090");

        Assert.Equal("Newsdesk", person!.FirstName);
        Assert.Equal(string.Empty, person.LastName);
        Assert.Equal("dailybrief.com", person.Domain);
    }

    [Fact]
    public void KnownNameTokens_ContainsAllNamesAndNoEmpties()
    {
        var (draft, plan) = EvidenceTestData.ApprovedCase();
        var directory = PeopleDirectory.Create(draft.Guide!, plan);
        var tokens = directory.KnownNameTokens;

        foreach (var member in draft.Guide!.Cast)
        {
            Assert.Contains(member.FullName, tokens);
            if (!string.IsNullOrWhiteSpace(member.FirstName))
                Assert.Contains(member.FirstName.Trim(), tokens);
            if (!string.IsNullOrWhiteSpace(member.LastName))
                Assert.Contains(member.LastName.Trim(), tokens);
        }
        Assert.Contains("Dana Whitfield", tokens);
        Assert.Contains("dana", tokens.Select(t => t.ToLowerInvariant()));
        Assert.Contains("Whitfield", tokens);
        Assert.DoesNotContain(string.Empty, tokens);
        Assert.All(tokens, t => Assert.False(string.IsNullOrWhiteSpace(t)));
    }

    [Fact]
    public void Create_NullListsAndDuplicateIds_AreSafe()
    {
        var (draft, plan) = EvidenceTestData.ApprovedCase();
        var guide = draft.Guide!;
        guide.Cast.Add(null!);
        guide.Cast.Add(new Models.Case.CastMember { Id = "CH-001", FirstName = "Other", LastName = "Person" });
        plan.Externals.Add(null!);

        var directory = PeopleDirectory.Create(guide, plan);

        Assert.Equal("Maria Delgado", directory.NameOf("CH-001"));
        Assert.Equal(directory.All.Count, directory.All.Select(p => p.Id).Distinct().Count());
    }

    [Fact]
    public void ForDomain_IsStableAndCaseInsensitive()
    {
        var a = OrganizationThemes.ForDomain("brightline.com");
        var b = OrganizationThemes.ForDomain("brightline.com");
        var c = OrganizationThemes.ForDomain("BrightLine.COM");

        foreach (var other in new[] { b, c })
        {
            Assert.Equal(a.ThemeName, other.ThemeName);
            Assert.Equal(a.PrimaryColor, other.PrimaryColor);
            Assert.Equal(a.SecondaryColor, other.SecondaryColor);
            Assert.Equal(a.AccentColor, other.AccentColor);
            Assert.Equal(a.HeadingFont, other.HeadingFont);
            Assert.Equal(a.BodyFont, other.BodyFont);
        }
        Assert.Equal("brightline.com", a.Domain);
    }

    [Fact]
    public void ForDomain_SpreadsAcrossPalettes()
    {
        var names = Enumerable.Range(1, 50)
            .Select(i => OrganizationThemes.ForDomain($"company{i}.com").ThemeName)
            .Distinct()
            .Count();

        Assert.True(names >= 5, $"Only {names} palettes used");
    }

    [Theory]
    [InlineData("gmail.com")]
    [InlineData("GMAIL.com")]
    [InlineData("proton.me")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void ForDomain_PersonalOrBlank_ReturnsDefault(string? domain)
    {
        var theme = OrganizationThemes.ForDomain(domain);
        var expected = OrganizationTheme.Default;

        Assert.Equal(expected.ThemeName, theme.ThemeName);
        Assert.Equal(expected.PrimaryColor, theme.PrimaryColor);
        Assert.Equal(expected.SecondaryColor, theme.SecondaryColor);
        Assert.Equal(expected.AccentColor, theme.AccentColor);
        Assert.Equal(expected.BodyFont, theme.BodyFont);
    }

    [Fact]
    public void Palettes_UseSanitizedColorsAndAllowedFonts()
    {
        var themes = Enumerable.Range(1, 200).Select(i => OrganizationThemes.ForDomain($"org{i}.example.com")).ToList();
        Assert.Equal(8, themes.Select(t => t.ThemeName).Distinct().Count());

        foreach (var theme in themes)
        {
            foreach (var color in new[] { theme.PrimaryColor, theme.SecondaryColor, theme.AccentColor })
                Assert.Equal(color, ThemeGenerator.SanitizeHexColor(color));
            Assert.Contains(theme.HeadingFont, ThemeGenerator.AllowedFonts);
            Assert.Contains(theme.BodyFont, ThemeGenerator.AllowedFonts);
            Assert.Equal(theme.BodyFont, ThemeGenerator.SanitizeFont(theme.BodyFont));
        }
    }

    [Fact]
    public void ForDomain_ReturnsNewInstanceEachCall()
    {
        var first = OrganizationThemes.ForDomain("brightline.com");
        var original = first.PrimaryColor;
        first.PrimaryColor = "000000";

        Assert.Equal(original, OrganizationThemes.ForDomain("brightline.com").PrimaryColor);
    }
    [Fact]
    public void Create_SetsExternalKindForExternalsAndOrganizationNameForCast()
    {
        var (draft, plan) = AttachmentTestData.MixedCase();
        var guide = draft.Guide!;
        var directory = PeopleDirectory.Create(guide, plan);

        Assert.Equal(Models.Corpus.ExternalKind.Newsletter, directory.Find(AttachmentTestData.NewsletterId)!.ExternalKind);
        Assert.Equal(Models.Corpus.ExternalKind.Spam, directory.Find(AttachmentTestData.SpamId)!.ExternalKind);
        Assert.Equal(Models.Corpus.ExternalKind.Personal, directory.Find(AttachmentTestData.PersonalId)!.ExternalKind);
        Assert.Equal(Models.Corpus.ExternalKind.Vendor, directory.Find(EvidenceTestData.VendorId)!.ExternalKind);
        Assert.Equal(string.Empty, directory.Find(EvidenceTestData.VendorId)!.OrganizationName);

        var cast = directory.Find("CH-001")!;
        Assert.Null(cast.ExternalKind);
        var organization = guide.FindOrganization(guide.FindCast("CH-001")!.OrganizationId)!;
        Assert.False(string.IsNullOrWhiteSpace(organization.Name));
        Assert.Equal(organization.Name, cast.OrganizationName);
    }

    [Fact]
    public void Create_CastWithoutOrganization_HasEmptyOrganizationName()
    {
        var (draft, plan) = EvidenceTestData.ApprovedCase();
        draft.Guide!.FindCast("CH-001")!.OrganizationId = "ORG-MISSING";

        Assert.Equal(string.Empty, PeopleDirectory.Create(draft.Guide!, plan).Find("CH-001")!.OrganizationName);
    }
}
