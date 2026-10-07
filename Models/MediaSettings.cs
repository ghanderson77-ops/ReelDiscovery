namespace ReelDiscovery.Models;

/// <summary>
/// Image generation settings shared by every OpenAI media client (CreateMediaClient / CreateMediaGenerator).
/// </summary>
public class MediaSettings
{
    public string ImageModel { get; set; } = "gpt-image-2.5-flare"; // gpt-image-1/1-mini shut down 2026-12-01
    public string ImageQuality { get; set; } = "low"; // low | medium | high (~$0.006/image at low 1024x1024)
}
