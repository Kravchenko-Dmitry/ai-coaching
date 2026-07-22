namespace AkAgent.Api.Sync;

/// Tracks whether the initial startup sync (SPEC.md §4.2 trigger 3) has completed,
/// so the readiness probe can report "warming up" until knowledge is available.
public sealed class SyncReadinessGate
{
    private volatile bool _isReady;

    public bool IsReady => _isReady;

    public void MarkReady() => _isReady = true;
}
