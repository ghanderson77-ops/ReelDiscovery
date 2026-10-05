namespace ReelDiscovery.Services.Evidence;

/// <summary>
/// Per-run media state: the stop flag and its reason, the count of media items not yet attempted, and the 2-slot
/// semaphore that bounds concurrent image and speech calls. Media-only stop: never sets the text run fatal (UI-SPEC
/// Decision 2, Pitfall 11).
/// </summary>
public class MediaRunGate
{
    private readonly object _lock = new();
    private bool _stopped;
    private string _reason = string.Empty;
    private int _remaining;

    // At most two media calls in flight (T-06-51)
    public SemaphoreSlim Slots { get; } = new(2, 2);

    public bool Stopped
    {
        get
        {
            lock (_lock)
                return _stopped;
        }
    }

    public string Reason
    {
        get
        {
            lock (_lock)
                return _reason;
        }
    }

    /// <summary>
    /// Media items not yet attempted; reported on the MediaStopped activity row.
    /// </summary>
    public int Remaining
    {
        get
        {
            lock (_lock)
                return _remaining;
        }
    }

    public void SetRemaining(int n)
    {
        lock (_lock)
            _remaining = Math.Max(0, n);
    }

    /// <summary>
    /// One media item was attempted (Ok or Failed); a Stopped call does not count, its item is still unrendered.
    /// </summary>
    public void Consume()
    {
        lock (_lock)
        {
            if (_remaining > 0)
                _remaining--;
        }
    }

    /// <summary>
    /// Stops all remaining media for the run. True only for the first caller, so exactly one MediaStopped event is added.
    /// </summary>
    public bool TryStop(string reason)
    {
        lock (_lock)
        {
            if (_stopped)
                return false;

            _stopped = true;
            _reason = string.IsNullOrWhiteSpace(reason) ? "the OpenAI media service stopped" : reason.Trim();
            return true;
        }
    }
}
