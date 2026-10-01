using System.Text.RegularExpressions;
using ReelDiscovery.Models.Case;
using ReelDiscovery.Services.Case;

namespace ReelDiscovery.Tests.Case;

public class CastIdentityTests
{
    private static HashSet<string> Taken(params string[] items) => new(items, StringComparer.OrdinalIgnoreCase);

    private static string Domain(string name, OrganizationKind kind = OrganizationKind.Party, string shortName = "", HashSet<string>? taken = null) =>
        EmailIdentity.DomainFor(new CaseOrganization { Name = name, ShortName = shortName, Kind = kind }, taken ?? Taken());

    [Fact]
    public void Domain_ShortNameWins_DunderMifflin()
    {
        var domain = Domain("Dunder Mifflin Paper Company, Inc.", OrganizationKind.Party, "Dunder Mifflin");
        Assert.Equal("dundermifflin.com", domain);

        var address = EmailIdentity.AddressFor("Michael", "Scott", domain, Taken());
        Assert.Equal("michael.scott@dundermifflin.com", address);
    }

    [Theory]
    [InlineData("The Harmon Builders, Inc.", "harmonbuilders.com")]
    [InlineData("Brightline Glazing, L.L.C.", "brightlineglazing.com")]
    [InlineData("Brightline Glazing LLC", "brightlineglazing.com")]
    [InlineData("Acme Holdings Corp.", "acmeholdings.com")]
    [InlineData("Dunder Mifflin Paper Company, Inc.", "dundermifflinpaper.com")]
    [InlineData("First Lakeshore Bank, N.A.", "firstlakeshorebank.com")]
    public void Domain_StripsSuffixesAndThe(string name, string expected)
    {
        Assert.Equal(expected, Domain(name));
    }

    [Theory]
    [InlineData("Thistle & Bracken LLP", "thistlebracken.com")]
    [InlineData("Okafor, Lindqvist and Marsh LLP", "okaforlindqvist.com")]
    [InlineData("Ellis Park P.C.", "ellispark.com")]
    public void Domain_LawFirmSurnames(string name, string expected)
    {
        Assert.Equal(expected, Domain(name, OrganizationKind.LawFirm));
    }

    [Fact]
    public void Domain_Agency()
    {
        Assert.Equal("departmentofwaterresources.gov", Domain("Department of Water Resources", OrganizationKind.Agency));
        Assert.Equal("drugenforcementadministration.gov", Domain("Drug Enforcement Administration", OrganizationKind.Agency));
    }

    [Fact]
    public void Domain_Individual()
    {
        Assert.Equal("whitefamily.com", Domain("Walter White", OrganizationKind.Individual));
    }

    [Fact]
    public void Domain_AsciiFold()
    {
        Assert.Equal("caferougesa.com", Domain("Café Rouge S.A."));
    }

    [Fact]
    public void Domain_Collision()
    {
        var taken = Taken("harmon.com");
        Assert.Equal("harmongroup.com", Domain("Harmon Builders, Inc.", OrganizationKind.Party, "Harmon", taken));
        Assert.Contains("harmongroup.com", taken);

        // Third organization with the same base: both base and "group" taken
        Assert.Equal("harmon2.com", Domain("Harmon Holdings", OrganizationKind.Party, "Harmon", taken));
        Assert.Equal("harmon3.com", Domain("Harmon Realty", OrganizationKind.Party, "Harmon", taken));

        var lawTaken = Taken("thistlebracken.com");
        Assert.Equal("thistlebrackenlaw.com", Domain("Thistle & Bracken LLP", OrganizationKind.LawFirm, "", lawTaken));
        Assert.Equal("thistlebracken2.com", Domain("Thistle & Bracken LLP", OrganizationKind.LawFirm, "", lawTaken));
    }

    [Fact]
    public void Domain_EmptyNameFallsBack()
    {
        Assert.Equal("org.com", Domain("!!!"));
    }

    [Fact]
    public void Domain_NeverReserved()
    {
        var domain = Domain("Example");
        Assert.NotEqual("example.com", domain);
        Assert.Equal("examplegroup.com", domain);
        Assert.False(EmailIdentity.IsReservedDomain(domain));
    }

    [Theory]
    [InlineData("x@foo.example", true)]
    [InlineData("example.com", true)]
    [InlineData("EXAMPLE.NET", true)]
    [InlineData("someone@example.org", true)]
    [InlineData("a.test", true)]
    [InlineData("a.invalid", true)]
    [InlineData("a.localhost", true)]
    [InlineData("localhost", true)]
    [InlineData("dundermifflin.com", false)]
    [InlineData("michael.scott@dundermifflin.com", false)]
    [InlineData("myexample.com", false)]
    [InlineData(null, false)]
    public void IsReservedDomain_Cases(string? input, bool expected)
    {
        Assert.Equal(expected, EmailIdentity.IsReservedDomain(input));
    }

    [Fact]
    public void Address_Basic()
    {
        Assert.Equal("michael.scott@dundermifflin.com", EmailIdentity.AddressFor("Michael", "Scott", "dundermifflin.com", Taken()));
    }

    [Fact]
    public void Address_FoldsAndKeepsHyphen()
    {
        Assert.Equal("jose.oneil@d.com", EmailIdentity.AddressFor("José", "O'Neil", "d.com", Taken()));
        Assert.Equal("mary-kate.ng@d.com", EmailIdentity.AddressFor("Mary-Kate", "Ng", "d.com", Taken()));
        Assert.Equal("ana.delarosa@d.com", EmailIdentity.AddressFor("Ana", "De La Rosa", "d.com", Taken()));
    }

    [Fact]
    public void Address_Collision()
    {
        var taken = Taken("michael.scott@d.com");

        Assert.Equal("michael.scott2@d.com", EmailIdentity.AddressFor("Michael", "Scott", "d.com", taken));
        Assert.Equal("michael.scott3@d.com", EmailIdentity.AddressFor("Michael", "Scott", "d.com", taken));
        Assert.Contains("michael.scott3@d.com", taken);
    }

    [Fact]
    public void Address_EmptyLast()
    {
        Assert.Equal("cher@d.com", EmailIdentity.AddressFor("Cher", "", "d.com", Taken()));
    }

    [Fact]
    public void Address_CannotInjectSeparators()
    {
        var address = EmailIdentity.AddressFor("Eve\r\nBcc: x", "<Mallory>\"", "d.com", Taken());

        Assert.Equal("evebccx.mallory@d.com", address);
        Assert.True(EmailIdentity.IsWellFormed(address));
    }

    [Theory]
    [InlineData("a.b@c.com", true)]
    [InlineData("michael.scott@dundermifflin.com", true)]
    [InlineData("a b@c.com", false)]
    [InlineData("a@c", false)]
    [InlineData("@c.com", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsWellFormed_Cases(string? email, bool expected)
    {
        Assert.Equal(expected, EmailIdentity.IsWellFormed(email));
    }

    [Theory]
    [InlineData("Michael.Scott@DunderMifflin.com", "dundermifflin.com")]
    [InlineData("no-at-sign", "")]
    [InlineData(null, "")]
    public void DomainOf_Cases(string? email, string expected)
    {
        Assert.Equal(expected, EmailIdentity.DomainOf(email));
    }

    [Fact]
    public void Fold_StripsAccentsAndPunctuation()
    {
        Assert.Equal("cafe", EmailIdentity.Fold("Café"));
        Assert.Equal("marykate", EmailIdentity.Fold("Mary-Kate"));
        Assert.Equal("mary-kate", EmailIdentity.Fold("Mary-Kate", keepHyphen: true));
    }

    private static (CastMember Member, CaseOrganization Org) Maria(SignatureStyle style) => (
        new CastMember
        {
            FirstName = "Maria",
            LastName = "Delgado",
            Title = "President",
            Phone = "(555) 555-0123",
            Email = "maria.delgado@brightline.com",
            SignatureStyle = style
        },
        new CaseOrganization { Name = "Brightline Glazing, LLC", ShortName = "Brightline" });

    [Fact]
    public void Signature_Styles()
    {
        var (full, org) = Maria(SignatureStyle.Full);
        Assert.Equal("Maria Delgado\nPresident\nBrightline Glazing, LLC\n(555) 555-0123\nmaria.delgado@brightline.com", SignatureBuilder.Build(full, org));

        var (shortSig, _) = Maria(SignatureStyle.Short);
        Assert.Equal("Maria Delgado\nPresident, Brightline Glazing, LLC", SignatureBuilder.Build(shortSig, org));

        var (nameOnly, _) = Maria(SignatureStyle.NameOnly);
        Assert.Equal("Maria", SignatureBuilder.Build(nameOnly, org));

        var (mobile, _) = Maria(SignatureStyle.MobileFooter);
        Assert.Equal("Maria\n\nSent from my mobile", SignatureBuilder.Build(mobile, org));
    }

    [Fact]
    public void Signature_NullOrgAndBlankFields()
    {
        var (full, _) = Maria(SignatureStyle.Full);
        Assert.Equal("Maria Delgado\nPresident\n(555) 555-0123\nmaria.delgado@brightline.com", SignatureBuilder.Build(full, null));

        var (shortSig, _) = Maria(SignatureStyle.Short);
        Assert.Equal("Maria Delgado\nPresident", SignatureBuilder.Build(shortSig, null));

        full.Title = string.Empty;
        full.Phone = string.Empty;
        Assert.Equal("Maria Delgado\nmaria.delgado@brightline.com", SignatureBuilder.Build(full, null));

        var noFirst = new CastMember { LastName = "Delgado", SignatureStyle = SignatureStyle.NameOnly };
        Assert.Equal("Delgado", SignatureBuilder.Build(noFirst, null));
    }

    [Fact]
    public void FictionalPhone_InReservedRange()
    {
        for (var i = 0; i < 50; i++)
            Assert.Matches(new Regex(@"^\(555\) 555-01\d\d$"), SignatureBuilder.FictionalPhone());
    }

    [Theory]
    [InlineData("Chicago, Illinois", "America/Chicago")]
    [InlineData("Phoenix, AZ", "America/Phoenix")]
    [InlineData("Los Angeles, California", "America/Los_Angeles")]
    [InlineData("Honolulu, Hawaii", "Pacific/Honolulu")]
    [InlineData("Anchorage, Alaska", "America/Anchorage")]
    [InlineData("Denver, colorado", "America/Denver")]
    [InlineData("Albuquerque, New Mexico", "America/Denver")]
    [InlineData("Houston, TX", "America/Chicago")]
    [InlineData("Washington, D.C.", "America/New_York")]
    [InlineData("Seattle, WA 98101", "America/Los_Angeles")]
    [InlineData("Chicago, Illinois, USA", "America/Chicago")]
    [InlineData("Somewhere", "America/New_York")]
    [InlineData("", "America/New_York")]
    [InlineData(null, "America/New_York")]
    public void TimeZones_ForLocation(string? location, string expected)
    {
        Assert.Equal(expected, UsTimeZones.ForLocation(location));
    }

    [Fact]
    public void TimeZones_CoverAllStatesAndDc()
    {
        Assert.Equal(51, UsTimeZones.StateCount);
    }
}
