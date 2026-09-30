using ReelDiscovery.Models;
using ReelDiscovery.Services;
using ReelDiscovery.Tests.Fakes;
using static ReelDiscovery.Tests.Generation.EmailGeneratorTestSetup;

namespace ReelDiscovery.Tests.Generation;

/// <summary>
/// WR-06: the email budget split is never negative, always sums to the total, and handles 0 storylines clearly.
/// </summary>
public class EmailDistributionTests
{
    private static List<Storyline> S(params int[] suggested) =>
        suggested.Select((n, i) => new Storyline { Title = $"S{i + 1}", SuggestedEmailCount = n }).ToList();

    [Fact]
    public void Equal_WithRemainder()
    {
        Assert.Equal(new[] { 4, 3, 3 }, EmailGenerator.DistributeEmails(S(0, 0, 0), 10));
    }

    [Fact]
    public void Proportional()
    {
        Assert.Equal(new[] { 20, 10, 10 }, EmailGenerator.DistributeEmails(S(10, 5, 5), 40));
    }

    [Fact]
    public void MinOneEach_WhenEnoughEmails()
    {
        Assert.Equal(new[] { 2, 1, 1 }, EmailGenerator.DistributeEmails(S(20, 5, 5), 4));
    }

    [Fact]
    public void FewerEmailsThanStorylines_NoNegatives()
    {
        Assert.Equal(
            new[] { 1, 1, 1, 1, 1, 0, 0, 0, 0, 0 },
            EmailGenerator.DistributeEmails(S(0, 0, 0, 0, 0, 0, 0, 0, 0, 0), 5));
    }

    [Fact]
    public void FewerEmailsThanStorylines_Proportional()
    {
        var distribution = EmailGenerator.DistributeEmails(S(5, 20, 5, 20, 5, 20, 5, 20, 5, 20), 5);

        Assert.All(distribution, n => Assert.True(n >= 0));
        Assert.Equal(5, distribution.Sum());
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(3, 5)]
    [InlineData(10, 5)]
    [InlineData(10, 10)]
    [InlineData(7, 100)]
    [InlineData(15, 5000)]
    public void SumsToTotal_NeverNegative(int storylineCount, int total)
    {
        var storylines = S(Enumerable.Range(0, storylineCount).Select(i => 5 + (i * 7) % 16).ToArray());

        var distribution = EmailGenerator.DistributeEmails(storylines, total);

        Assert.Equal(storylineCount, distribution.Count);
        Assert.Equal(total, distribution.Sum());
        Assert.All(distribution, n => Assert.True(n >= 0));
    }

    [Fact]
    public void ZeroTotal_AllZero()
    {
        Assert.Equal(new[] { 0, 0 }, EmailGenerator.DistributeEmails(S(5, 5), 0));
    }

    [Fact]
    public void NegativeSuggested_ClampedToZero()
    {
        var distribution = EmailGenerator.DistributeEmails(S(-10, 10), 12);

        Assert.Equal(new[] { 1, 11 }, distribution);
    }

    [Fact]
    public void NoStorylines_Throws()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            EmailGenerator.DistributeEmails(new List<Storyline>(), 5));

        Assert.Contains("No storylines", ex.Message);
    }

    [Fact]
    public async Task Generator_NoStorylines_RecordsErrorAndSetsStateResult()
    {
        var fake = new FakeLlmProvider();

        var folder = NewOutputFolder();
        try
        {
            var state = State(folder, 0);
            state.Config.TotalEmailCount = 5;

            var result = await new EmailGenerator(Gateway(fake), null).GenerateEmailsAsync(state, new SyncProgress<GenerationProgress>());

            Assert.Same(result, state.Result);
            Assert.Contains(result.Errors, e => e.Contains("No storylines"));
            Assert.Empty(fake.Requests);
        }
        finally
        {
            DeleteFolder(folder);
        }
    }

    [Fact]
    public async Task Generator_MoreStorylinesThanEmails_MakesExactlyTotal()
    {
        var fake = new FakeLlmProvider();
        for (var i = 0; i < 4; i++)
            fake.Responses.Enqueue(FakeLlmProvider.Ok(ThreadJson("x")));

        var folder = NewOutputFolder();
        try
        {
            var state = State(folder, 4);
            state.Config.TotalEmailCount = 2;

            var result = await new EmailGenerator(Gateway(fake), null).GenerateEmailsAsync(state, new SyncProgress<GenerationProgress>());

            // Storylines with 0 emails make no LLM call
            Assert.Equal(2, fake.Requests.Count);
            Assert.Empty(result.Errors);
            Assert.Null(result.FatalError);
        }
        finally
        {
            DeleteFolder(folder);
        }
    }
}
