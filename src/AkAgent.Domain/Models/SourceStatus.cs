namespace AkAgent.Domain.Models;

public record SourceStatus(string SourceName, SyncState SyncState, int DocumentCount, HealthStatus Health);
