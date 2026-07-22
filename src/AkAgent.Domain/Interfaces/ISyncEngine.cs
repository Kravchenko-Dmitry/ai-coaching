using AkAgent.Domain.Models;

namespace AkAgent.Domain.Interfaces;

public interface ISyncEngine
{
    /// Runs a sync across all registered sources, guarded by an internal semaphore.
    /// Returns Started = false (no Report) if a sync is already running.
    Task<SyncEngineRunResult> RunSyncAsync(CancellationToken ct);
}
