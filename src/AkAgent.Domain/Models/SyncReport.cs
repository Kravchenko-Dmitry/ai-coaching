using AkAgent.Domain.Enums;

namespace AkAgent.Domain.Models;

public record SyncReport(DateTimeOffset StartedAt, DateTimeOffset? FinishedAt, IReadOnlyList<SourceSyncReport> PerSource);

public record SourceSyncReport(string SourceName, int Changed, int Removed, int Skipped, SyncStatus Status, string? Error);

public record SyncEngineRunResult(bool Started, SyncReport? Report);
