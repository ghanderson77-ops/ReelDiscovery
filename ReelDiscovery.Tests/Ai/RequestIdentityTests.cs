using System.Text.RegularExpressions;
using ReelDiscovery.Services.Ai;
using Xunit;

namespace ReelDiscovery.Tests.Ai;

public class RequestIdentityTests
{
    private static LlmProviderRequest Request(Action<LlmProviderRequest>? change = null)
    {
        var request = new LlmProviderRequest
        {
            ModelId = "gpt-5-mini",
            Effort = "low",
            SchemaName = "thread",
            SchemaJson = "{\"type\":\"object\"}",
            SystemPrompt = "You write emails.",
            UserPrompt = "Write thread T-0001.",
            MaxTokens = 4000
        };
        change?.Invoke(request);
        return request;
    }

    [Fact]
    public void SameRequest_SameId()
    {
        Assert.Equal(RequestIdentity.CustomIdFor(LlmProviderKind.OpenAI, Request()),
            RequestIdentity.CustomIdFor(LlmProviderKind.OpenAI, Request()));
    }

    [Fact]
    public void ContentChanges_ChangeId()
    {
        var baseline = RequestIdentity.CustomIdFor(LlmProviderKind.OpenAI, Request());

        Assert.NotEqual(baseline, RequestIdentity.CustomIdFor(LlmProviderKind.OpenAI, Request(r => r.UserPrompt = "Write thread T-0002.")));
        Assert.NotEqual(baseline, RequestIdentity.CustomIdFor(LlmProviderKind.OpenAI, Request(r => r.MaxTokens = 4001)));
        Assert.NotEqual(baseline, RequestIdentity.CustomIdFor(LlmProviderKind.OpenAI, Request(r => r.Effort = "high")));
        Assert.NotEqual(baseline, RequestIdentity.CustomIdFor(LlmProviderKind.Anthropic, Request()));
    }

    [Fact]
    public void AllowBatch_DoesNotChangeId()
    {
        Assert.Equal(RequestIdentity.CustomIdFor(LlmProviderKind.OpenAI, Request(r => r.AllowBatch = false)),
            RequestIdentity.CustomIdFor(LlmProviderKind.OpenAI, Request(r => r.AllowBatch = true)));
    }

    [Fact]
    public void NullAndEmptyEffort_SameId()
    {
        Assert.Equal(RequestIdentity.CustomIdFor(LlmProviderKind.OpenAI, Request(r => r.Effort = null)),
            RequestIdentity.CustomIdFor(LlmProviderKind.OpenAI, Request(r => r.Effort = "")));
    }

    [Fact]
    public void Id_HasProviderSafeShape()
    {
        var id = RequestIdentity.CustomIdFor(LlmProviderKind.Anthropic, Request());

        Assert.Equal(41, id.Length);
        Assert.StartsWith("q", id);
        Assert.Matches(new Regex("^[a-zA-Z0-9_-]{1,64}$"), id);
        Assert.Equal(id.ToLowerInvariant(), id);
    }
}
