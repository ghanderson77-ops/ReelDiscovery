using ReelDiscovery.Models;
using ReelDiscovery.Services.Ai;

namespace ReelDiscovery.Tests;

public class MediaAvailabilityPolicyTests
{
    [Fact]
    public void Apply_WhenUnavailable_ForcesAllMediaFlagsOff()
    {
        var state = new WizardState { AnthropicApiKey = "k", AnthropicKeyTested = true };
        state.SetTextProvider(LlmProviderKind.Anthropic);
        state.WantsImages = true;
        state.WantsVoicemails = true;
        state.Config.IncludeImages = true;
        state.Config.IncludeVoicemails = true;

        state.ApplyMediaAvailability();

        Assert.False(state.WantsImages);
        Assert.False(state.WantsVoicemails);
        Assert.False(state.Config.IncludeImages);
        Assert.False(state.Config.IncludeVoicemails);
    }

    [Fact]
    public void Apply_WhenAvailable_LeavesFlags()
    {
        var state = new WizardState { OpenAIApiKey = "sk-test", OpenAIKeyTested = true };
        state.SetTextProvider(LlmProviderKind.Anthropic);
        state.WantsImages = true;
        state.WantsVoicemails = true;
        state.Config.IncludeImages = true;
        state.Config.IncludeVoicemails = true;

        state.ApplyMediaAvailability();

        Assert.True(state.WantsImages);
        Assert.True(state.WantsVoicemails);
        Assert.True(state.Config.IncludeImages);
        Assert.True(state.Config.IncludeVoicemails);
    }
}
