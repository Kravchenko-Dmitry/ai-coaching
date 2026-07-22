using AkAgent.Domain.Enums;

namespace AkAgent.Domain.Models;

/// A trimmed view of SyncState for public API responses — omits DocumentHashes,
/// which is internal store bookkeeping, not consumer-facing status.
public record SyncStateSummary(DateTimeOffset? LastSyncAt, SyncStatus Status, string? LastError);

public record SourceStatus(string SourceName, SyncStateSummary SyncState, int DocumentCount, HealthStatus Health);
