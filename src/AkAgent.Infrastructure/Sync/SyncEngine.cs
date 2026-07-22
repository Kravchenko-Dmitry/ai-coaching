using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using AkAgent.Domain.Enums;
using AkAgent.Domain.Interfaces;
using AkAgent.Domain.Models;
using Microsoft.Extensions.Logging;

namespace AkAgent.Infrastructure.Sync;

public sealed class SyncEngine : ISyncEngine
{
    private readonly IEnumerable<IKnowledgeSource> _sources;
    private readonly IKnowledgeStore _store;
    private readonly ILogger<SyncEngine> _logger;
    private readonly SemaphoreSlim _semaphore = new(1, 1);

    public SyncEngine(IEnumerable<IKnowledgeSource> sources, IKnowledgeStore store, ILogger<SyncEngine> logger)
    {
        _sources = sources;
        _store = store;
        _logger = logger;
    }

    public async Task<SyncEngineRunResult> RunSyncAsync(CancellationToken ct)
    {
        if (!await _semaphore.WaitAsync(0, ct))
        {
            _logger.LogInformation("Sync already in progress; rejecting concurrent trigger");
            return new SyncEngineRunResult(Started: false, Report: null);
        }

        try
        {
            var startedAt = DateTimeOffset.UtcNow;
            var perSource = new List<SourceSyncReport>();

            foreach (var source in _sources)
            {
                perSource.Add(await SyncSourceAsync(source, ct));
            }

            var report = new SyncReport(startedAt, DateTimeOffset.UtcNow, perSource);
            return new SyncEngineRunResult(Started: true, Report: report);
        }
        finally
        {
            _semaphore.Release();
        }
    }

    private async Task<SourceSyncReport> SyncSourceAsync(IKnowledgeSource source, CancellationToken ct)
    {
        var existingState = await _store.GetSyncStateAsync(source.Name, ct)
                             ?? new SyncState { SourceName = source.Name, Status = SyncStatus.Never };

        var originalLastSyncAt = existingState.LastSyncAt;
        await _store.SaveSyncStateAsync(existingState with { Status = SyncStatus.InProgress }, ct);

        var hashes = new Dictionary<string, string>(existingState.DocumentHashes);
        var changed = 0;
        var removed = 0;
        var skipped = 0;
        var stopwatch = Stopwatch.StartNew();

        try
        {
            var changes = await source.GetChangesAsync(originalLastSyncAt, ct);

            foreach (var sourceDoc in changes)
            {
                ct.ThrowIfCancellationRequested();

                var normalizedContent = MarkdownProcessor.Normalize(sourceDoc.RawContent);
                var contentHash = ComputeHash(normalizedContent);
                if (hashes.TryGetValue(sourceDoc.Id, out var existingHash) && existingHash == contentHash)
                {
                    skipped++;
                    continue;
                }

                var sections = MarkdownProcessor.SplitIntoSections(normalizedContent);
                var documentType = DocumentTypeClassifier.Infer(sourceDoc.Id, sourceDoc.Title);

                var doc = new KnowledgeDocument
                {
                    Id = sourceDoc.Id,
                    SourceName = source.Name,
                    Title = sourceDoc.Title,
                    Content = normalizedContent,
                    ContentHash = contentHash,
                    LastModified = sourceDoc.LastModified,
                    Type = documentType,
                    Sections = sections
                };

                await _store.UpsertAsync(doc, ct);
                hashes[sourceDoc.Id] = contentHash;
                changed++;
            }

            var currentIds = (await source.GetAllIdsAsync(ct)).ToHashSet();
            var deletedIds = hashes.Keys.Where(id => !currentIds.Contains(id)).ToList();
            foreach (var id in deletedIds)
            {
                await _store.RemoveAsync(id, ct);
                hashes.Remove(id);
                removed++;
            }

            await _store.SaveSyncStateAsync(new SyncState
            {
                SourceName = source.Name,
                LastSyncAt = DateTimeOffset.UtcNow,
                Status = SyncStatus.Ok,
                LastError = null,
                DocumentHashes = hashes
            }, ct);

            _logger.LogInformation(
                "Sync completed for source {SourceName}: changed={Changed}, removed={Removed}, skipped={Skipped}, durationMs={DurationMs}",
                source.Name, changed, removed, skipped, stopwatch.ElapsedMilliseconds);

            return new SourceSyncReport(source.Name, changed, removed, skipped, SyncStatus.Ok, null);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(
                ex,
                "Sync failed for source {SourceName} after {DurationMs}ms: changed={Changed}, removed={Removed}, skipped={Skipped}",
                source.Name, stopwatch.ElapsedMilliseconds, changed, removed, skipped);

            await _store.SaveSyncStateAsync(new SyncState
            {
                SourceName = source.Name,
                LastSyncAt = originalLastSyncAt,
                Status = SyncStatus.Failed,
                LastError = ex.Message,
                DocumentHashes = hashes
            }, ct);

            return new SourceSyncReport(source.Name, changed, removed, skipped, SyncStatus.Failed, ex.Message);
        }
    }

    private static string ComputeHash(string content)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(content));
        return Convert.ToHexString(bytes);
    }
}
