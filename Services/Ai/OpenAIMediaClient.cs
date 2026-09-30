using System.ClientModel;
using OpenAI;
using OpenAI.Audio;
using OpenAI.Images;

namespace ReelDiscovery.Services.Ai;

/// <summary>
/// OpenAI media generation (gpt-image images and tts-1 speech). Transient 429/5xx errors are retried by the
/// SDK transport; a 400 (content policy or invalid prompt) returns null so the caller skips the attachment.
/// Media usage is not token-tracked in Phase 1; <c>operationName</c> is accepted for signature parity with callers.
/// </summary>
public class OpenAIMediaClient
{
    private readonly OpenAIClient _client;
    private readonly string _imageModel;
    private readonly string _imageQuality;

    public OpenAIMediaClient(string apiKey, string imageModel = "gpt-image-2.5-flare", string imageQuality = "low")
        : this(apiKey, imageModel, imageQuality, OpenAITextProvider.CreateDefaultOptions())
    {
    }

    internal OpenAIMediaClient(string apiKey, string imageModel, string imageQuality, OpenAIClientOptions options)
    {
        _client = new OpenAIClient(new ApiKeyCredential(apiKey), options);
        _imageModel = imageModel;
        _imageQuality = imageQuality;
    }

    /// <summary>
    /// Generate a 1024x1024 PNG image with the configured gpt-image model and quality.
    /// </summary>
    /// <returns>PNG bytes, or null when the request is rejected (e.g. moderation)</returns>
    public async Task<byte[]?> GenerateImageAsync(string prompt, string? operationName = null, CancellationToken ct = default)
    {
        try
        {
            var imageClient = _client.GetImageClient(_imageModel);

            // gpt-image always returns base64 data: DALL-E-only response_format/style are never set.
            // OutputFileFormat is marked experimental (OPENAI001) in OpenAI 2.14.0 but maps to the stable
            // gpt-image "output_format" field
#pragma warning disable OPENAI001
            var options = new ImageGenerationOptions
            {
                Quality = new GeneratedImageQuality(_imageQuality),
                Size = GeneratedImageSize.W1024xH1024,
                OutputFileFormat = GeneratedImageFileFormat.Png
            };
#pragma warning restore OPENAI001

            var result = await imageClient.GenerateImageAsync(prompt, options, ct);
            return result.Value?.ImageBytes?.ToArray();
        }
        catch (ClientResultException ex) when (ex.Status == 400)
        {
            // Content policy violation or invalid prompt - don't retry
            return null;
        }
    }

    /// <summary>
    /// Generate speech audio from text using OpenAI TTS
    /// </summary>
    /// <param name="text">The text to convert to speech</param>
    /// <param name="voice">Voice to use: alloy, echo, fable, onyx, nova, shimmer</param>
    /// <param name="operationName">Optional operation name for tracking</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>MP3 audio bytes, or null on failure</returns>
    public async Task<byte[]?> GenerateSpeechAsync(
        string text,
        string voice = "alloy",
        string? operationName = null,
        CancellationToken ct = default)
    {
        try
        {
            var audioClient = _client.GetAudioClient("tts-1");

            var options = new SpeechGenerationOptions
            {
                SpeedRatio = 1.0f,
                ResponseFormat = GeneratedSpeechFormat.Mp3
            };

            // Map voice string to enum
            var voiceEnum = voice.ToLowerInvariant() switch
            {
                "echo" => GeneratedSpeechVoice.Echo,
                "fable" => GeneratedSpeechVoice.Fable,
                "onyx" => GeneratedSpeechVoice.Onyx,
                "nova" => GeneratedSpeechVoice.Nova,
                "shimmer" => GeneratedSpeechVoice.Shimmer,
                _ => GeneratedSpeechVoice.Alloy
            };

            var response = await audioClient.GenerateSpeechAsync(text, voiceEnum, options, ct);
            return response.Value?.ToArray();
        }
        catch (ClientResultException ex) when (ex.Status == 400)
        {
            // Invalid request - don't retry
            return null;
        }
    }
}
