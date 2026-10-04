namespace ReelDiscovery.Models;

/// <summary>
/// Per-unit OpenAI media prices folded into the session cost ticker (D-29).
/// </summary>
public class MediaPrices
{
    // Research A6 (LOW confidence), editable in ModelConfigurationDialog (UI-SPEC S4)
    public decimal ImagePerUnit { get; set; } = 0.006m;
    public decimal TtsPerMillionChars { get; set; } = 15.00m;

    public decimal ImageCost() => ImagePerUnit;

    public decimal SpeechCost(int characters) => Math.Max(0, characters) * TtsPerMillionChars / 1_000_000m;

    /// <summary>Clamps hand-edited prices: image 0..10 USD per image, speech 0..1000 USD per 1M characters.</summary>
    public void Normalize()
    {
        ImagePerUnit = Math.Clamp(ImagePerUnit, 0m, 10m);
        TtsPerMillionChars = Math.Clamp(TtsPerMillionChars, 0m, 1000m);
    }

    public MediaPrices Clone() => new()
    {
        ImagePerUnit = ImagePerUnit,
        TtsPerMillionChars = TtsPerMillionChars
    };

    public static MediaPrices Defaults() => new();
}
