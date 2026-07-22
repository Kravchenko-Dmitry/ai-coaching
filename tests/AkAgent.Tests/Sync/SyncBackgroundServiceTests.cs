using AkAgent.Api.Sync;
using AkAgent.Domain.Interfaces;
using AkAgent.Domain.Models;
using AkAgent.Infrastructure.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace AkAgent.Tests.Sync;

public class SyncBackgroundServiceTests
{
    private static SyncBackgroundService CreateService(
        ISyncEngine syncEngine,
        IKnowledgeStore store,
        IEnumerable<IKnowledgeSource> sources,
        SyncReadinessGate readinessGate,
        double intervalMinutes = 60)
        => new(
            syncEngine,
            store,
            sources,
            readinessGate,
            Options.Create(new SyncOptions { IntervalMinutes = intervalMinutes }),
            NullLogger<SyncBackgroundService>.Instance);

    [Test]
    public async Task ExecuteAsync_runs_a_startup_sync_when_the_store_is_empty()
    {
        var store = Substitute.For<IKnowledgeStore>();
        store.ListAsync(Arg.Any<CancellationToken>()).Returns((IReadOnlyList<DocumentSummary>)[]);

        var syncEngine = Substitute.For<ISyncEngine>();
        var syncCalled = new TaskCompletionSource();
        syncEngine.RunSyncAsync(Arg.Any<CancellationToken>()).Returns(_ =>
        {
            syncCalled.TrySetResult();
            return Task.FromResult(new SyncEngineRunResult(true, null));
        });

        var readinessGate = new SyncReadinessGate();
        var service = CreateService(syncEngine, store, [], readinessGate);

        await service.StartAsync(CancellationToken.None);
        await syncCalled.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await service.StopAsync(CancellationToken.None);

        await syncEngine.Received(1).RunSyncAsync(Arg.Any<CancellationToken>());
        Assert.That(readinessGate.IsReady, Is.True);
    }

    [Test]
    public async Task ExecuteAsync_runs_a_startup_sync_when_a_registered_source_has_no_persisted_state()
    {
        var store = Substitute.For<IKnowledgeStore>();
        store.ListAsync(Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<DocumentSummary>)[new DocumentSummary("a", "A", AkAgent.Domain.Enums.DocumentType.Other, DateTimeOffset.UtcNow)]);
        store.GetSyncStateAsync("FileDrop", Arg.Any<CancellationToken>()).Returns((SyncState?)null);

        var source = Substitute.For<IKnowledgeSource>();
        source.Name.Returns("FileDrop");

        var syncEngine = Substitute.For<ISyncEngine>();
        var syncCalled = new TaskCompletionSource();
        syncEngine.RunSyncAsync(Arg.Any<CancellationToken>()).Returns(_ =>
        {
            syncCalled.TrySetResult();
            return Task.FromResult(new SyncEngineRunResult(true, null));
        });

        var readinessGate = new SyncReadinessGate();
        var service = CreateService(syncEngine, store, [source], readinessGate);

        await service.StartAsync(CancellationToken.None);
        await syncCalled.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await service.StopAsync(CancellationToken.None);

        await syncEngine.Received(1).RunSyncAsync(Arg.Any<CancellationToken>());
        Assert.That(readinessGate.IsReady, Is.True);
    }

    [Test]
    public async Task ExecuteAsync_skips_startup_sync_when_store_has_data_and_all_sources_have_state()
    {
        var store = Substitute.For<IKnowledgeStore>();
        store.ListAsync(Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<DocumentSummary>)[new DocumentSummary("a", "A", AkAgent.Domain.Enums.DocumentType.Other, DateTimeOffset.UtcNow)]);
        store.GetSyncStateAsync("FileDrop", Arg.Any<CancellationToken>())
            .Returns(new SyncState { SourceName = "FileDrop", Status = AkAgent.Domain.Enums.SyncStatus.Ok });

        var source = Substitute.For<IKnowledgeSource>();
        source.Name.Returns("FileDrop");

        var syncEngine = Substitute.For<ISyncEngine>();
        var readinessGate = new SyncReadinessGate();
        var service = CreateService(syncEngine, store, [source], readinessGate);

        await service.StartAsync(CancellationToken.None);
        // Give the readiness-gate flip a moment to run before we assert and stop.
        await Task.Delay(200);
        await service.StopAsync(CancellationToken.None);

        await syncEngine.DidNotReceive().RunSyncAsync(Arg.Any<CancellationToken>());
        Assert.That(readinessGate.IsReady, Is.True);
    }

    [Test]
    public async Task ExecuteAsync_triggers_periodic_syncs_at_the_configured_interval()
    {
        var store = Substitute.For<IKnowledgeStore>();
        store.ListAsync(Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<DocumentSummary>)[new DocumentSummary("a", "A", AkAgent.Domain.Enums.DocumentType.Other, DateTimeOffset.UtcNow)]);

        var syncEngine = Substitute.For<ISyncEngine>();
        var secondCall = new TaskCompletionSource();
        var callCount = 0;
        syncEngine.RunSyncAsync(Arg.Any<CancellationToken>()).Returns(_ =>
        {
            if (Interlocked.Increment(ref callCount) >= 2)
                secondCall.TrySetResult();
            return Task.FromResult(new SyncEngineRunResult(true, null));
        });

        var readinessGate = new SyncReadinessGate();
        // Sub-minute interval so the periodic loop ticks quickly in a test.
        var service = CreateService(syncEngine, store, [], readinessGate, intervalMinutes: 0.001);

        await service.StartAsync(CancellationToken.None);
        await secondCall.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await service.StopAsync(CancellationToken.None);

        Assert.That(Volatile.Read(ref callCount), Is.GreaterThanOrEqualTo(2));
    }
}
