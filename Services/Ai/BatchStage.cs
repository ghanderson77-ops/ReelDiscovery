namespace ReelDiscovery.Services.Ai;

/// <summary>
/// Ambient stage label for provider batching (D-07). The runner wraps each wave or chunk in <see cref="Enter"/>; the
/// AsyncLocal flows into every task started inside the using block, so requests of one stage share a batch and different
/// stages never do.
/// </summary>
public static class BatchStage
{
    public const string DefaultLabel = "batch";

    private static readonly AsyncLocal<string?> _current = new();
    private static readonly AsyncLocal<bool> _live = new();
    private static readonly AsyncLocal<SemaphoreSlim?> _liveGate = new();

    /// <summary>The label of the enclosing stage; "batch" when unset.</summary>
    public static string Current => _current.Value ?? DefaultLabel;

    /// <summary>True inside <see cref="EnterLive"/>: requests always go live even when they allow batching.</summary>
    public static bool IsLive => _live.Value;

    /// <summary>Gate every live send inside <see cref="UseLiveGate"/> waits on; null when unset.</summary>
    public static SemaphoreSlim? LiveGate => _liveGate.Value;

    /// <summary>Sets the stage label (e.g. "wave 3", "non-relevant mail") until the returned scope is disposed.</summary>
    public static IDisposable Enter(string label)
    {
        var previous = _current.Value;
        _current.Value = string.IsNullOrWhiteSpace(label) ? DefaultLabel : label;
        return new Scope(() => _current.Value = previous);
    }

    /// <summary>Requests inside always go live (attachment repairs, revision 1 W9) until the returned scope is disposed.</summary>
    public static IDisposable EnterLive()
    {
        var previous = _live.Value;
        _live.Value = true;
        return new Scope(() => _live.Value = previous);
    }

    /// <summary>Live sends inside wait on this gate (amended D-07: Stage B live fallbacks and retries keep live parallelism) until the returned scope is disposed.</summary>
    public static IDisposable UseLiveGate(SemaphoreSlim gate)
    {
        ArgumentNullException.ThrowIfNull(gate);
        var previous = _liveGate.Value;
        _liveGate.Value = gate;
        return new Scope(() => _liveGate.Value = previous);
    }

    private sealed class Scope : IDisposable
    {
        private Action? _restore;

        public Scope(Action restore)
        {
            _restore = restore;
        }

        public void Dispose()
        {
            Interlocked.Exchange(ref _restore, null)?.Invoke();
        }
    }
}
