using AkAgent.Domain.Models;

namespace AkAgent.Domain.Interfaces;

public interface IKnowledgeSource
{
    string Name { get; }  // unique instance name, used in doc Ids and sync state

    /// Documents created/changed since 'since' (null = full listing).
    /// May over-report; the sync engine deduplicates by ContentHash.
    Task<IReadOnlyList<SourceDocument>> GetChangesAsync(DateTimeOffset? since, CancellationToken ct);

    Task<SourceDocument?> GetDocumentAsync(string id, CancellationToken ct);

    /// Ids currently existing in the source (used to detect deletions).
    Task<IReadOnlyList<string>> GetAllIdsAsync(CancellationToken ct);

    Task<HealthStatus> HealthCheckAsync(CancellationToken ct);
}
