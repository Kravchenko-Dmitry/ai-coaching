using AkAgent.Domain.Models;

namespace AkAgent.Domain.Interfaces;

public interface IKnowledgeStore
{
    Task UpsertAsync(KnowledgeDocument doc, CancellationToken ct);
    Task RemoveAsync(string docId, CancellationToken ct);
    Task<IReadOnlyList<SearchHit>> SearchAsync(string query, int maxResults, CancellationToken ct);
    Task<KnowledgeDocument?> GetAsync(string docId, CancellationToken ct);
    Task<IReadOnlyList<DocumentSummary>> ListAsync(CancellationToken ct); // id, title, type, lastModified
    Task<SyncState?> GetSyncStateAsync(string sourceName, CancellationToken ct);
    Task SaveSyncStateAsync(SyncState state, CancellationToken ct);
}
