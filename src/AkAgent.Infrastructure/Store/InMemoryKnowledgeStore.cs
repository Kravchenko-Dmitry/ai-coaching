using System.Collections.Concurrent;
using System.Text.Json;
using AkAgent.Domain.Interfaces;
using AkAgent.Domain.Models;
using AkAgent.Infrastructure.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AkAgent.Infrastructure.Store;

public sealed class InMemoryKnowledgeStore : IKnowledgeStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly ConcurrentDictionary<string, KnowledgeDocument> _documents = new();
    private readonly ConcurrentDictionary<string, SyncState> _syncStates = new();
    private readonly StoreOptions _options;
    private readonly ILogger<InMemoryKnowledgeStore> _logger;
    private readonly SemaphoreSlim _persistLock = new(1, 1);

    private string DocumentsFilePath => Path.Combine(_options.DataPath, "documents.json");
    private string SyncStateFilePath => Path.Combine(_options.DataPath, "sync-state.json");

    public InMemoryKnowledgeStore(IOptions<StoreOptions> options, ILogger<InMemoryKnowledgeStore> logger)
    {
        _options = options.Value;
        _logger = logger;
        Directory.CreateDirectory(_options.DataPath);
        LoadFromDisk();
    }

    private void LoadFromDisk()
    {
        if (File.Exists(DocumentsFilePath))
        {
            var json = File.ReadAllText(DocumentsFilePath);
            var docs = JsonSerializer.Deserialize<List<KnowledgeDocument>>(json, JsonOptions) ?? [];
            foreach (var doc in docs)
                _documents[doc.Id] = doc;
        }

        if (File.Exists(SyncStateFilePath))
        {
            var json = File.ReadAllText(SyncStateFilePath);
            var states = JsonSerializer.Deserialize<List<SyncState>>(json, JsonOptions) ?? [];
            foreach (var state in states)
                _syncStates[state.SourceName] = state;
        }

        _logger.LogInformation(
            "Loaded {DocumentCount} documents and {SyncStateCount} sync states from {DataPath}",
            _documents.Count, _syncStates.Count, _options.DataPath);
    }

    public async Task UpsertAsync(KnowledgeDocument doc, CancellationToken ct)
    {
        _documents[doc.Id] = doc;
        await PersistDocumentsAsync(ct);
    }

    public async Task RemoveAsync(string docId, CancellationToken ct)
    {
        _documents.TryRemove(docId, out _);
        await PersistDocumentsAsync(ct);
    }

    public Task<IReadOnlyList<SearchHit>> SearchAsync(string query, int maxResults, CancellationToken ct)
    {
        var queryTokens = SearchTokenizer.Tokenize(query);
        if (queryTokens.Count == 0)
            return Task.FromResult<IReadOnlyList<SearchHit>>([]);

        var scored = new List<(KnowledgeDocument Doc, DocumentSection? Best, double Raw)>();

        foreach (var doc in _documents.Values)
        {
            var titleScore = 3.0 * CountMatches(SearchTokenizer.Tokenize(doc.Title), queryTokens);

            double bodyTotal = 0;
            DocumentSection? bestSection = null;
            var bestSectionScore = 0.0;

            foreach (var section in doc.Sections)
            {
                var sectionScore =
                    2.0 * CountMatches(SearchTokenizer.Tokenize(section.Heading), queryTokens) +
                    1.0 * CountMatches(SearchTokenizer.Tokenize(section.Content), queryTokens);

                bodyTotal += sectionScore;
                if (sectionScore > bestSectionScore)
                {
                    bestSectionScore = sectionScore;
                    bestSection = section;
                }
            }

            var rawScore = titleScore + bodyTotal;
            if (rawScore > 0)
                scored.Add((doc, bestSection, rawScore));
        }

        if (scored.Count == 0)
            return Task.FromResult<IReadOnlyList<SearchHit>>([]);

        var maxRaw = scored.Max(s => s.Raw);

        IReadOnlyList<SearchHit> hits = scored
            .Select(s => new SearchHit(s.Doc, s.Best, s.Raw / maxRaw))
            .Where(h => h.Score >= _options.MinScore)
            .OrderByDescending(h => h.Score)
            .ThenBy(h => h.Document.Id, StringComparer.Ordinal)
            .Take(maxResults)
            .ToList();

        return Task.FromResult(hits);
    }

    public Task<KnowledgeDocument?> GetAsync(string docId, CancellationToken ct)
        => Task.FromResult(_documents.GetValueOrDefault(docId));

    public Task<IReadOnlyList<DocumentSummary>> ListAsync(CancellationToken ct)
    {
        IReadOnlyList<DocumentSummary> summaries = _documents.Values
            .Select(d => new DocumentSummary(d.Id, d.Title, d.Type, d.LastModified))
            .ToList();
        return Task.FromResult(summaries);
    }

    public Task<SyncState?> GetSyncStateAsync(string sourceName, CancellationToken ct)
        => Task.FromResult(_syncStates.GetValueOrDefault(sourceName));

    public async Task SaveSyncStateAsync(SyncState state, CancellationToken ct)
    {
        _syncStates[state.SourceName] = state;
        await PersistSyncStateAsync(ct);
    }

    private static int CountMatches(IReadOnlyList<string> textTokens, IReadOnlyList<string> queryTokens)
    {
        if (textTokens.Count == 0)
            return 0;

        var counts = textTokens
            .GroupBy(t => t)
            .ToDictionary(g => g.Key, g => g.Count());

        return queryTokens.Sum(qt => counts.GetValueOrDefault(qt, 0));
    }

    private async Task PersistDocumentsAsync(CancellationToken ct)
    {
        await _persistLock.WaitAsync(ct);
        try
        {
            var json = JsonSerializer.Serialize(_documents.Values.ToList(), JsonOptions);
            await File.WriteAllTextAsync(DocumentsFilePath, json, ct);
        }
        finally
        {
            _persistLock.Release();
        }
    }

    private async Task PersistSyncStateAsync(CancellationToken ct)
    {
        await _persistLock.WaitAsync(ct);
        try
        {
            var json = JsonSerializer.Serialize(_syncStates.Values.ToList(), JsonOptions);
            await File.WriteAllTextAsync(SyncStateFilePath, json, ct);
        }
        finally
        {
            _persistLock.Release();
        }
    }
}
