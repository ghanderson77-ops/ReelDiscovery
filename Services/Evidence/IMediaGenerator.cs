namespace ReelDiscovery.Services.Evidence;

public enum MediaResultKind { Ok, Failed, Stopped }

/// <summary>
/// Outcome of one media call. Failed is retryable on Continue; Stopped (auth or quota) means stop all remaining
/// media for the run. Reason is plain words for the run log (UI-SPEC Decision 2).
/// </summary>
public class MediaResult
{
    public MediaResultKind Kind { get; set; }
    public byte[]? Bytes { get; set; }
    public string Reason { get; set; } = string.Empty;
    public decimal CostUsd { get; set; }
}

/// <summary>
/// Seam over the OpenAI media client; never throws except on user cancel (D-06, Pitfall 11).
/// </summary>
public interface IMediaGenerator
{
    Task<MediaResult> GenerateImageAsync(string prompt, string label, CancellationToken ct = default);

    Task<MediaResult> GenerateSpeechAsync(string text, string voice, string label, CancellationToken ct = default);
}
