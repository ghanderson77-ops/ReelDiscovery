using ReelDiscovery.Models.Case;
using ReelDiscovery.Services.Case;
using ReelDiscovery.Services.Corpus;

namespace ReelDiscovery.Tests.Corpus;

public class BusinessCalendarTests
{
    private static readonly TimeZoneInfo Chicago = TimeZoneInfo.FindSystemTimeZoneById("America/Chicago");
    private static readonly TimeZoneInfo Phoenix = TimeZoneInfo.FindSystemTimeZoneById("America/Phoenix");

    private static DateOnly D(int y, int m, int d) => new(y, m, d);

    [Theory]
    [InlineData(2021, 2021, 6, 18)]
    [InlineData(2021, 2021, 12, 31)]
    [InlineData(2022, 2022, 12, 26)]
    [InlineData(2023, 2023, 11, 10)]
    [InlineData(2026, 2026, 7, 3)]
    [InlineData(2027, 2027, 6, 18)]
    [InlineData(2024, 2024, 11, 28)]
    [InlineData(2024, 2024, 5, 27)]
    [InlineData(2024, 2024, 1, 15)]
    [InlineData(2024, 2024, 7, 4)]
    public void ObservedFederalHolidays_ContainsVector(int year, int y, int m, int d)
    {
        Assert.Contains(D(y, m, d), BusinessCalendar.ObservedFederalHolidays(year));
    }

    [Fact]
    public void ObservedFederalHolidays_JuneteenthOnlyFrom2021()
    {
        var h2020 = BusinessCalendar.ObservedFederalHolidays(2020);
        Assert.DoesNotContain(h2020, d => d.Month == 6);
        Assert.Equal(10, h2020.Count);
        Assert.Equal(11, BusinessCalendar.ObservedFederalHolidays(2024).Count);
    }

    [Fact]
    public void ObservedFederalHolidays_DoesNotLeakOtherYears()
    {
        // 2022-01-01 is a Saturday: observed 2021-12-31, so 2022 has no New Year's entry in 2022
        var h2022 = BusinessCalendar.ObservedFederalHolidays(2022);
        Assert.All(h2022, d => Assert.Equal(2022, d.Year));
        Assert.DoesNotContain(D(2022, 1, 1), h2022);
    }

    [Fact]
    public void IsBusinessDay_WeekendsAndHolidays()
    {
        Assert.False(BusinessCalendar.IsBusinessDay(D(2024, 6, 1)));  // Saturday
        Assert.False(BusinessCalendar.IsBusinessDay(D(2024, 6, 2)));  // Sunday
        Assert.False(BusinessCalendar.IsBusinessDay(D(2024, 7, 4)));
        Assert.True(BusinessCalendar.IsBusinessDay(D(2024, 7, 5)));
        Assert.True(BusinessCalendar.IsHoliday(D(2024, 7, 4)));
        Assert.Equal(D(2024, 7, 5), BusinessCalendar.NextBusinessDay(D(2024, 7, 4)));
        Assert.Equal(D(2024, 6, 3), BusinessCalendar.NextBusinessDay(D(2024, 6, 1)));
    }

    [Fact]
    public void At_UsesZoneOffsetIncludingDst()
    {
        var ten = new TimeOnly(10, 0);
        Assert.Equal(TimeSpan.FromHours(-6), BusinessCalendar.At(Chicago, D(2024, 3, 8), ten).Offset);
        Assert.Equal(TimeSpan.FromHours(-5), BusinessCalendar.At(Chicago, D(2024, 3, 11), ten).Offset);
        Assert.Equal(TimeSpan.FromHours(-7), BusinessCalendar.At(Phoenix, D(2024, 3, 8), ten).Offset);
        Assert.Equal(TimeSpan.FromHours(-7), BusinessCalendar.At(Phoenix, D(2024, 3, 11), ten).Offset);
    }

    [Fact]
    public void IsBusinessTime_Boundaries()
    {
        var tue = D(2024, 3, 12);
        Assert.True(BusinessCalendar.IsBusinessTime(BusinessCalendar.At(Chicago, tue, new TimeOnly(16, 59)), Chicago));
        Assert.False(BusinessCalendar.IsBusinessTime(BusinessCalendar.At(Chicago, tue, new TimeOnly(17, 0)), Chicago));
        Assert.False(BusinessCalendar.IsBusinessTime(BusinessCalendar.At(Chicago, tue, new TimeOnly(8, 59)), Chicago));
        Assert.True(BusinessCalendar.IsBusinessTime(BusinessCalendar.At(Chicago, tue, new TimeOnly(9, 0)), Chicago));
        Assert.False(BusinessCalendar.IsBusinessTime(BusinessCalendar.At(Chicago, D(2024, 3, 16), new TimeOnly(10, 0)), Chicago));
    }

    [Fact]
    public void IsBusinessTime_ConvertsInstantIntoZone()
    {
        // 15:30 Chicago (CDT, -05:00) = 13:30 Phoenix: business time in both; 16:30 Phoenix = 18:30 Chicago
        var phoenixLate = BusinessCalendar.At(Phoenix, D(2024, 3, 12), new TimeOnly(16, 30));
        Assert.True(BusinessCalendar.IsBusinessTime(phoenixLate, Phoenix));
        Assert.False(BusinessCalendar.IsBusinessTime(phoenixLate, Chicago));
    }

    [Fact]
    public void NextBusinessInstant_FridayEveningToMonday()
    {
        var fri = BusinessCalendar.At(Chicago, D(2024, 3, 8), new TimeOnly(17, 30));
        var next = BusinessCalendar.NextBusinessInstant(fri, Chicago);
        Assert.Equal(BusinessCalendar.At(Chicago, D(2024, 3, 11), new TimeOnly(9, 0)), next);
        Assert.Equal(TimeSpan.FromHours(-5), next.Offset);
    }

    [Fact]
    public void NextBusinessInstant_SkipsThanksgiving()
    {
        var wed = BusinessCalendar.At(Chicago, D(2024, 11, 27), new TimeOnly(18, 0));
        Assert.Equal(BusinessCalendar.At(Chicago, D(2024, 11, 29), new TimeOnly(9, 0)), BusinessCalendar.NextBusinessInstant(wed, Chicago));
    }

    [Fact]
    public void NextBusinessInstant_BusinessTimeReturnsItself_EarlyMorningSnapsToday()
    {
        var inside = BusinessCalendar.At(Chicago, D(2024, 3, 12), new TimeOnly(11, 17));
        Assert.Equal(inside, BusinessCalendar.NextBusinessInstant(inside, Chicago, 30));

        var early = BusinessCalendar.At(Chicago, D(2024, 3, 12), new TimeOnly(7, 0));
        Assert.Equal(BusinessCalendar.At(Chicago, D(2024, 3, 12), new TimeOnly(9, 20)), BusinessCalendar.NextBusinessInstant(early, Chicago, 20));
    }

    [Fact]
    public void NextBusinessInstant_JitterClamped()
    {
        var sat = BusinessCalendar.At(Chicago, D(2024, 3, 9), new TimeOnly(10, 0));
        Assert.Equal(BusinessCalendar.At(Chicago, D(2024, 3, 11), new TimeOnly(9, 45)), BusinessCalendar.NextBusinessInstant(sat, Chicago, 500));
        Assert.Equal(BusinessCalendar.At(Chicago, D(2024, 3, 11), new TimeOnly(9, 0)), BusinessCalendar.NextBusinessInstant(sat, Chicago, -5));
    }

    [Fact]
    public void AddBusinessMinutes_CrossesWeekend()
    {
        var start = BusinessCalendar.At(Chicago, D(2024, 3, 8), new TimeOnly(16, 50));
        Assert.Equal(BusinessCalendar.At(Chicago, D(2024, 3, 11), new TimeOnly(9, 20)), BusinessCalendar.AddBusinessMinutes(start, 30, Chicago));
    }

    [Fact]
    public void AddBusinessMinutes_FullDay()
    {
        var start = BusinessCalendar.At(Chicago, D(2024, 3, 12), new TimeOnly(10, 0));
        Assert.Equal(BusinessCalendar.At(Chicago, D(2024, 3, 13), new TimeOnly(10, 0)), BusinessCalendar.AddBusinessMinutes(start, 480, Chicago));
        Assert.Equal(BusinessCalendar.At(Chicago, D(2024, 3, 13), new TimeOnly(12, 0)), BusinessCalendar.AddBusinessMinutes(start, 600, Chicago));
        Assert.Equal(BusinessCalendar.BusinessMinutesPerDay, 480);
    }

    [Fact]
    public void AddBusinessMinutes_AlwaysBusinessTimeAndLater()
    {
        var starts = new[]
        {
            BusinessCalendar.At(Chicago, D(2024, 11, 27), new TimeOnly(16, 59)),
            BusinessCalendar.At(Chicago, D(2024, 12, 31), new TimeOnly(23, 0)),
            BusinessCalendar.At(Phoenix, D(2024, 7, 3), new TimeOnly(16, 0))
        };
        foreach (var start in starts)
        {
            foreach (var minutes in new[] { 1, 2, 59, 479, 480, 481, 2000 })
            {
                var result = BusinessCalendar.AddBusinessMinutes(start, minutes, Chicago);
                Assert.True(result > start);
                Assert.True(BusinessCalendar.IsBusinessTime(result, Chicago), $"{start} + {minutes} = {result}");
            }
        }
    }

    [Fact]
    public void AddBusinessMinutes_EndOfDayLandsNextMorningNot1700()
    {
        // 16:00 + 60 = exactly 17:00, which is not business time: the next instant is 09:00 next business day
        var start = BusinessCalendar.At(Chicago, D(2024, 3, 12), new TimeOnly(16, 0));
        Assert.Equal(BusinessCalendar.At(Chicago, D(2024, 3, 13), new TimeOnly(9, 0)), BusinessCalendar.AddBusinessMinutes(start, 60, Chicago));
    }

    [Fact]
    public void Resolve_FallbackChain()
    {
        Assert.Equal(Phoenix.Id, BusinessCalendar.Resolve(new CaseOrganization { TimeZoneId = "America/Phoenix" }).Id);

        var denver = BusinessCalendar.Resolve(new CaseOrganization { TimeZoneId = "Not/AZone", Location = "Denver, Colorado" });
        Assert.Equal(TimeZoneInfo.FindSystemTimeZoneById(UsTimeZones.ForLocation("Denver, Colorado")).Id, denver.Id);

        Assert.Equal(TimeZoneInfo.FindSystemTimeZoneById(UsTimeZones.Default).Id, BusinessCalendar.Resolve(null).Id);
        Assert.Equal(TimeZoneInfo.FindSystemTimeZoneById(UsTimeZones.Default).Id, BusinessCalendar.Resolve(new CaseOrganization { TimeZoneId = "\0bad\\..\\x" }).Id);
    }

    [Fact]
    public void ResolveId_NeverThrows()
    {
        Assert.Null(BusinessCalendar.ResolveId("Not/AZone"));
        Assert.Null(BusinessCalendar.ResolveId(""));
        Assert.Null(BusinessCalendar.ResolveId(null));
        Assert.Null(BusinessCalendar.ResolveId(new string('x', 5000)));
        Assert.Equal(Chicago.Id, BusinessCalendar.ResolveId("America/Chicago")!.Id);
    }

    [Fact]
    public void LocalDate_InZone()
    {
        // 2024-03-12 05:30 UTC = 2024-03-12 00:30 Chicago (CDT) but 2024-03-11 22:30 Phoenix
        var instant = new DateTimeOffset(2024, 3, 12, 5, 30, 0, TimeSpan.Zero);
        Assert.Equal(D(2024, 3, 12), BusinessCalendar.LocalDate(instant, Chicago));
        Assert.Equal(D(2024, 3, 11), BusinessCalendar.LocalDate(instant, Phoenix));
    }
}
