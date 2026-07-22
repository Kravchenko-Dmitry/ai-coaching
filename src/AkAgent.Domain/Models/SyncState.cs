using AkAgent.Domain.Enums;

namespace AkAgent.Domain.Models;

public record SyncState
{
    public required string SourceName { get; init; }
    public DateTimeOffset? LastSyncAt { get; init; }
    public SyncStatus Status { get; init; }             // Ok | Failed | InProgress | Never
    public string? LastError { get; init; }
    public Dictionary<string, string> DocumentHashes { get; init; } = new(); // docId -> hash
}
