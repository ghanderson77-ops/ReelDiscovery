using System.Text;
using ReelDiscovery.Services.Evidence;

namespace ReelDiscovery.Tests.Fakes;

/// <summary>
/// Scripted IMediaGenerator: records every request and returns deterministic Ok bytes by default,
/// a <see cref="Respond"/> result when set, or Stopped for every call after <see cref="StopAfter"/> calls.
/// </summary>
public sealed class FakeMediaGenerator : IMediaGenerator
{
    private static readonly byte[] PngSignature = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
    private static readonly byte[] Id3Signature = { 0x49, 0x44, 0x33 };

    private readonly object _lock = new();

    /// <summary>Kind is "image" or "speech"; Voice is empty for images.</summary>
    public List<(string Kind, string Input, string Voice, string Label)> Requests { get; } = new();

    /// <summary>(kind, input) to result; overrides the default Ok result.</summary>
    public Func<string, string, MediaResult>? Respond { get; set; }

    /// <summary>When non-negative, every call after the first N returns Stopped (scripts a mid-run media stop).</summary>
    public int StopAfter { get; set; } = -1;

    public Task<MediaResult> GenerateImageAsync(string prompt, string label, CancellationToken ct = default) =>
        Handle("image", prompt, string.Empty, label, ct);

    public Task<MediaResult> GenerateSpeechAsync(string text, string voice, string label, CancellationToken ct = default) =>
        Handle("speech", text, voice, label, ct);

    public static MediaResult Failed(string reason) => new() { Kind = MediaResultKind.Failed, Reason = reason };

    public static MediaResult Stopped(string reason = "the OpenAI key was rejected") =>
        new() { Kind = MediaResultKind.Stopped, Reason = reason };

    private Task<MediaResult> Handle(string kind, string input, string voice, string label, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        int index;
        lock (_lock)
        {
            index = Requests.Count;
            Requests.Add((kind, input, voice, label));
        }

        if (StopAfter >= 0 && index >= StopAfter)
            return Task.FromResult(Stopped());

        if (Respond != null)
            return Task.FromResult(Respond(kind, input));

        var prefix = kind == "image" ? PngSignature : Id3Signature;
        var bytes = prefix.Concat(Encoding.UTF8.GetBytes($"{kind}:{label}")).ToArray();
        return Task.FromResult(new MediaResult
        {
            Kind = MediaResultKind.Ok,
            Bytes = bytes,
            CostUsd = kind == "image" ? 0.006m : 0.001m
        });
    }
}
