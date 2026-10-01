using ReelDiscovery.Models.Case;
using ReelDiscovery.Services.Case;

namespace ReelDiscovery.Tests.Case;

public class NameSubstitutionTests
{
    private static EntitySubstitution Walter(bool apply = true) => new()
    {
        Original = "Walter White",
        Aliases = new List<string> { "White", "Walt" },
        Kind = EntityKind.Person,
        Substitute = "Hank Brennan",
        Apply = apply
    };

    private static EntitySubstitution[] Map(params EntitySubstitution[] items) => items;

    [Fact]
    public void Apply_FullName()
    {
        Assert.Equal("Hank Brennan met Jesse.", NameSubstitution.Apply("Walter White met Jesse.", Map(Walter())));
    }

    [Fact]
    public void Apply_Possessive()
    {
        Assert.Equal("Hank Brennan's lab", NameSubstitution.Apply("Walter White's lab", Map(Walter())));
    }

    [Fact]
    public void Apply_SurnameAlias_CaseSensitive()
    {
        Assert.Equal("Brennan called. Bring the white paper.",
            NameSubstitution.Apply("White called. Bring the white paper.", Map(Walter())));
    }

    [Fact]
    public void Apply_FirstNameAlias()
    {
        Assert.Equal("Hank agreed.", NameSubstitution.Apply("Walt agreed.", Map(Walter())));
    }

    [Fact]
    public void Apply_MultiToken_CaseInsensitive()
    {
        Assert.Equal("Hank Brennan signed", NameSubstitution.Apply("WALTER WHITE signed", Map(Walter())));
    }

    [Fact]
    public void Apply_WordBoundary_CompanyAliasDropsSuffix()
    {
        var apple = new EntitySubstitution
        {
            Original = "Apple Inc.",
            Aliases = new List<string> { "Apple" },
            Kind = EntityKind.Company,
            Substitute = "Orchard Systems Inc."
        };

        Assert.Equal("Orchard Systems shipped pineapple and Pineapple juice",
            NameSubstitution.Apply("Apple shipped pineapple and Pineapple juice", Map(apple)));
    }

    [Fact]
    public void Apply_NoChain()
    {
        var acme = new EntitySubstitution { Original = "Acme Corp", Kind = EntityKind.Company, Substitute = "Beta Corp" };
        var beta = new EntitySubstitution { Original = "Beta Corp", Kind = EntityKind.Company, Substitute = "Gamma Corp" };

        Assert.Equal("Beta Corp sued Gamma Corp", NameSubstitution.Apply("Acme Corp sued Beta Corp", Map(acme, beta)));
    }

    [Fact]
    public void Apply_LongestFirst()
    {
        var walter = new EntitySubstitution
        {
            Original = "Walter White",
            Aliases = new List<string> { "Walter" },
            Kind = EntityKind.Person,
            Substitute = "Hank Brennan"
        };
        var industries = new EntitySubstitution { Original = "Walter Industries", Kind = EntityKind.Company, Substitute = "Lumen Works" };

        Assert.Equal("Hank Brennan of Lumen Works",
            NameSubstitution.Apply("Walter White of Walter Industries", Map(walter, industries)));
    }

    [Fact]
    public void Apply_UncheckedUntouched()
    {
        const string text = "Walter White met Walt and White.";
        Assert.Equal(text, NameSubstitution.Apply(text, Map(Walter(apply: false))));
    }

    [Fact]
    public void Apply_BlankSubstituteIgnored()
    {
        var blank = Walter();
        blank.Substitute = "";
        const string text = "Walter White met Jesse.";
        Assert.Equal(text, NameSubstitution.Apply(text, Map(blank)));
    }

    [Fact]
    public void Apply_EmptyText_Unchanged()
    {
        Assert.Equal(string.Empty, NameSubstitution.Apply(string.Empty, Map(Walter())));
    }

    [Fact]
    public void FindLeaks_FindsOriginal_AndNoneAfterApply()
    {
        const string text = "Report on Walter White";
        Assert.Equal(new[] { "Walter White" }, NameSubstitution.FindLeaks(text, Map(Walter())));

        var substituted = NameSubstitution.Apply(text, Map(Walter()));
        Assert.Empty(NameSubstitution.FindLeaks(substituted, Map(Walter())));
    }

    [Fact]
    public void FindLeaks_IgnoresUnchecked()
    {
        Assert.Empty(NameSubstitution.FindLeaks("Report on Walter White", Map(Walter(apply: false))));
    }

    [Fact]
    public void FindLeaks_Distinct_CanonicalTerm()
    {
        Assert.Equal(new[] { "Walter White" },
            NameSubstitution.FindLeaks("Walter White and WALTER WHITE and walter white", Map(Walter())));
    }

    [Fact]
    public void FindLeaks_SingleTokenAlias_ReportedAsTerm()
    {
        Assert.Equal(new[] { "White", "Walter White" },
            NameSubstitution.FindLeaks("White called about Walter White.", Map(Walter())));
    }

    [Fact]
    public void FindLeaks_EmptyText_Empty()
    {
        Assert.Empty(NameSubstitution.FindLeaks(string.Empty, Map(Walter())));
    }
}
