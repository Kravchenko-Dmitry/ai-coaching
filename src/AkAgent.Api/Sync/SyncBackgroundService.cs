using AkAgent.Domain.Interfaces;
using AkAgent.Infrastructure.Configuration;
using Microsoft.Extensions.Options;

namespace AkAgent.Api.Sync;

/// Drives sync triggers 1 and 3 from SPEC.md §4.2: a timer loop (Sync:IntervalMinutes)
/// and a startup sync run before the store is considered ready, when the store is empty
/// or a registered source has no persisted sync state.
public sealed class SyncBackgroundService : BackgroundService
{
    private readonly ISyncEngine _syncEngine;
    private readonly IKnowledgeStore _store;
    private readonly IEnumerable<IKnowledgeSource> _sources;
    private readonly SyncReadinessGate _readinessGate;
    private readonly IOptions<SyncOptions> _options;
    private readonly ILogger<SyncBackgroundService> _logger;

    public SyncBackgroundService(
        ISyncEngine syncEngine,
        IKnowledgeStore store,
        IEnumerable<IKnowledgeSource> sources,
        SyncReadinessGate readinessGate,
        IOptions<SyncOptions> options,
        ILogger<SyncBackgroundService> logger)
    {
        _syncEngine = syncEngine;
        _store = store;
        _sources = sources;
        _readinessGate = readinessGate;
        _options = options;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (await NeedsStartupSyncAsync(stoppingToken))
        {
            _logger.LogInformation("Store empty or sync state missing; running startup sync before serving queries");
            await _syncEngine.RunSyncAsync(stoppingToken);
        }

        _readinessGate.MarkReady();

        var interval = TimeSpan.FromMinutes(Math.Max(0.001, _options.Value.IntervalMinutes));
        using var timer = new PeriodicTimer(interval);

        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                await _syncEngine.RunSyncAsync(stoppingToken);
            }
        }
        catch (OperationCanceledException)
        {
            // expected on shutdown
        }
    }

    private async Task<bool> NeedsStartupSyncAsync(CancellationToken ct)
    {
        var documents = await _store.ListAsync(ct);
        if (documents.Count == 0)
            return true;

        foreach (var source in _sources)
        {
            if (await _store.GetSyncStateAsync(source.Name, ct) is null)
                return true;
        }

        return false;
    }
}
