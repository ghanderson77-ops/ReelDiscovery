using ReelDiscovery.Models;
using ReelDiscovery.UserControls;

namespace ReelDiscovery.Tests;

/// <summary>
/// WR-05: the completion page reports the true run outcome, never "ready for import" after a stop or cancel.
/// </summary>
public class RunSummaryTests
{
    [Fact]
    public void Fatal_ShowsProviderError()
    {
        var result = new GenerationResult { FatalError = "SpendLimit: Spend limit reached" };

        Assert.Equal("Stopped by provider error", StepComplete.DescribeStatus(result));
        var summary = StepComplete.BuildSummaryText(result);
        Assert.Contains("SpendLimit: Spend limit reached", summary);
        Assert.DoesNotContain("ready for import", summary);
    }

    [Fact]
    public void Cancelled_ShowsCancelled()
    {
        var result = new GenerationResult { WasCancelled = true };

        Assert.Equal("Cancelled", StepComplete.DescribeStatus(result));
        var summary = StepComplete.BuildSummaryText(result);
        Assert.Contains("cancelled", summary);
        Assert.DoesNotContain("ready for import", summary);
    }

    [Fact]
    public void Errors_ShowsErrorCount()
    {
        var result = new GenerationResult();
        result.AddError("a");
        result.AddError("b");

        Assert.Equal("Completed with 2 error(s)", StepComplete.DescribeStatus(result));
        Assert.Contains("2 item(s)", StepComplete.BuildSummaryText(result));
    }

    [Fact]
    public void Clean_ShowsReady()
    {
        var result = new GenerationResult();

        Assert.Equal("Completed", StepComplete.DescribeStatus(result));
        Assert.Contains("ready for import", StepComplete.BuildSummaryText(result));
    }

    [Fact]
    public void Precedence_FatalBeatsCancelled()
    {
        var result = new GenerationResult { FatalError = "Auth: bad key", WasCancelled = true };

        Assert.Equal("Stopped by provider error", StepComplete.DescribeStatus(result));
    }
}
