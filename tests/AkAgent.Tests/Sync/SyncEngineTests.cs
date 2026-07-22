using System.Security.Cryptography;
using System.Text;
using AkAgent.Domain.Enums;
using AkAgent.Domain.Interfaces;
using AkAgent.Domain.Models;
using AkAgent.Infrastructure.Sync;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace AkAgent.Tests.Sync;

public class SyncEngineTests
{
    private static IKnowledgeSource MakeSource(string name)
    {
        var source = Substitute.For<IKnowledgeSource>();
        source.Name.Returns(name);
        return source;
    }

    private static string Hash(string content)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content)));

    [Test]
    public async Task RunSyncAsync_upserts_new_documents_with_hash_sections_and_inferred_type()
    {
        var store = Substitute.For<IKnowledgeStore>();
        store.GetSyncStateAsync("FileDrop", Arg.Any<CancellationToken>()).Returns((SyncState?)null);

        var source = MakeSource("FileDrop");
        const string content = "# Decision One\nWe use X.\n## Context\nBecause Y.";
        var sourceDoc = new SourceDocument("FileDrop:adr-001.md", "Decision One", content, DateTimeOffset.UtcNow, "text/markdown");
        source.GetChangesAsync(null, Arg.Any<CancellationToken>()).Returns([sourceDoc]);
        source.GetAllIdsAsync(Arg.Any<CancellationToken>()).Returns([sourceDoc.Id]);

        KnowledgeDocument? captured = null;
        store.When(s => s.UpsertAsync(Arg.Any<KnowledgeDocument>(), Arg.Any<CancellationToken>()))
            .Do(ci => captured = ci.Arg<KnowledgeDocument>());

        var engine = new SyncEngine([source], store, NullLogger<SyncEngine>.Instance);
        var result = await engine.RunSyncAsync(CancellationToken.None);

        Assert.That(result.Started, Is.True);
        Assert.That(result.Report!.PerSource, Has.Count.EqualTo(1));
        Assert.That(result.Report.PerSource[0].Changed, Is.EqualTo(1));
        Assert.That(result.Report.PerSource[0].Status, Is.EqualTo(SyncStatus.Ok));

        Assert.That(captured, Is.Not.Null);
        Assert.That(captured!.Id, Is.EqualTo(sourceDoc.Id));
        Assert.That(captured.Type, Is.EqualTo(DocumentType.Adr));
        Assert.That(captured.ContentHash, Is.EqualTo(Hash(content)));
        Assert.That(captured.Sections, Has.Count.EqualTo(2));
        Assert.That(captured.Sections[0].Heading, Is.EqualTo("Decision One"));
        Assert.That(captured.Sections[1].Heading, Is.EqualTo("Context"));

        await store.Received(1).SaveSyncStateAsync(
            Arg.Is<SyncState>(s => s != null && s.SourceName == "FileDrop" && s.Status == SyncStatus.Ok && s.DocumentHashes.ContainsKey(sourceDoc.Id)),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task RunSyncAsync_computes_ContentHash_from_normalized_content_not_raw_content()
    {
        var store = Substitute.For<IKnowledgeStore>();
        store.GetSyncStateAsync("FileDrop", Arg.Any<CancellationToken>()).Returns((SyncState?)null);

        var source = MakeSource("FileDrop");
        const string rawContent = "# Title\r\nBody with trailing whitespace.   \r\n\r\n";
        var sourceDoc = new SourceDocument("FileDrop:doc.md", "Title", rawContent, DateTimeOffset.UtcNow, "text/markdown");
        source.GetChangesAsync(null, Arg.Any<CancellationToken>()).Returns([sourceDoc]);
        source.GetAllIdsAsync(Arg.Any<CancellationToken>()).Returns([sourceDoc.Id]);

        KnowledgeDocument? captured = null;
        store.When(s => s.UpsertAsync(Arg.Any<KnowledgeDocument>(), Arg.Any<CancellationToken>()))
            .Do(ci => captured = ci.Arg<KnowledgeDocument>());

        var engine = new SyncEngine([source], store, NullLogger<SyncEngine>.Instance);
        await engine.RunSyncAsync(CancellationToken.None);

        Assert.That(captured, Is.Not.Null);
        Assert.That(captured!.ContentHash, Is.EqualTo(Hash(captured.Content)));
        Assert.That(captured.ContentHash, Is.Not.EqualTo(Hash(rawContent)));
    }

    [Test]
    public async Task RunSyncAsync_normalizes_line_endings_and_keeps_deeper_headings_inside_the_current_section()
    {
        var store = Substitute.For<IKnowledgeStore>();
        store.GetSyncStateAsync("FileDrop", Arg.Any<CancellationToken>()).Returns((SyncState?)null);

        var source = MakeSource("FileDrop");
        const string content = "Intro line\r\n# Overview\r\nSome body\r\n#### Deep Heading\r\nstill body\r\n## Next\r\nmore";
        var sourceDoc = new SourceDocument("FileDrop:doc.md", "Doc", content, DateTimeOffset.UtcNow, "text/markdown");
        source.GetChangesAsync(null, Arg.Any<CancellationToken>()).Returns([sourceDoc]);
        source.GetAllIdsAsync(Arg.Any<CancellationToken>()).Returns([sourceDoc.Id]);

        KnowledgeDocument? captured = null;
        store.When(s => s.UpsertAsync(Arg.Any<KnowledgeDocument>(), Arg.Any<CancellationToken>()))
            .Do(ci => captured = ci.Arg<KnowledgeDocument>());

        var engine = new SyncEngine([source], store, NullLogger<SyncEngine>.Instance);
        await engine.RunSyncAsync(CancellationToken.None);

        Assert.That(captured!.Content, Does.Not.Contain("\r"));
        Assert.That(captured.Sections, Has.Count.EqualTo(3));
        Assert.That(captured.Sections[0].Heading, Is.EqualTo(""));
        Assert.That(captured.Sections[0].Content, Is.EqualTo("Intro line"));
        Assert.That(captured.Sections[1].Heading, Is.EqualTo("Overview"));
        Assert.That(captured.Sections[1].Content, Is.EqualTo("Some body\n#### Deep Heading\nstill body"));
        Assert.That(captured.Sections[2].Heading, Is.EqualTo("Next"));
        Assert.That(captured.Sections[2].Content, Is.EqualTo("more"));
    }

    [Test]
    public async Task RunSyncAsync_skips_documents_whose_content_hash_is_unchanged()
    {
        const string content = "# Title\nBody";
        var existingState = new SyncState
        {
            SourceName = "FileDrop",
            LastSyncAt = DateTimeOffset.UtcNow.AddHours(-1),
            Status = SyncStatus.Ok,
            DocumentHashes = new Dictionary<string, string> { ["FileDrop:a.md"] = Hash(content) }
        };

        var store = Substitute.For<IKnowledgeStore>();
        store.GetSyncStateAsync("FileDrop", Arg.Any<CancellationToken>()).Returns(existingState);

        var source = MakeSource("FileDrop");
        var sourceDoc = new SourceDocument("FileDrop:a.md", "Title", content, DateTimeOffset.UtcNow, "text/markdown");
        source.GetChangesAsync(existingState.LastSyncAt, Arg.Any<CancellationToken>()).Returns([sourceDoc]);
        source.GetAllIdsAsync(Arg.Any<CancellationToken>()).Returns(["FileDrop:a.md"]);

        var engine = new SyncEngine([source], store, NullLogger<SyncEngine>.Instance);
        var result = await engine.RunSyncAsync(CancellationToken.None);

        Assert.That(result.Report!.PerSource[0].Skipped, Is.EqualTo(1));
        Assert.That(result.Report.PerSource[0].Changed, Is.EqualTo(0));
        await store.DidNotReceive().UpsertAsync(Arg.Any<KnowledgeDocument>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task RunSyncAsync_removes_documents_no_longer_present_in_the_source()
    {
        var existingState = new SyncState
        {
            SourceName = "FileDrop",
            LastSyncAt = DateTimeOffset.UtcNow.AddHours(-1),
            Status = SyncStatus.Ok,
            DocumentHashes = new Dictionary<string, string> { ["FileDrop:gone.md"] = "somehash" }
        };

        var store = Substitute.For<IKnowledgeStore>();
        store.GetSyncStateAsync("FileDrop", Arg.Any<CancellationToken>()).Returns(existingState);

        var source = MakeSource("FileDrop");
        source.GetChangesAsync(existingState.LastSyncAt, Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<SourceDocument>)[]);
        source.GetAllIdsAsync(Arg.Any<CancellationToken>()).Returns((IReadOnlyList<string>)[]);

        var engine = new SyncEngine([source], store, NullLogger<SyncEngine>.Instance);
        var result = await engine.RunSyncAsync(CancellationToken.None);

        Assert.That(result.Report!.PerSource[0].Removed, Is.EqualTo(1));
        await store.Received(1).RemoveAsync("FileDrop:gone.md", Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task RunSyncAsync_marks_source_failed_and_preserves_LastSyncAt_on_exception()
    {
        var originalLastSyncAt = DateTimeOffset.UtcNow.AddHours(-2);
        var existingState = new SyncState
        {
            SourceName = "FileDrop",
            LastSyncAt = originalLastSyncAt,
            Status = SyncStatus.Ok,
            DocumentHashes = new Dictionary<string, string>()
        };

        var store = Substitute.For<IKnowledgeStore>();
        store.GetSyncStateAsync("FileDrop", Arg.Any<CancellationToken>()).Returns(existingState);

        var source = MakeSource("FileDrop");
        source.GetChangesAsync(originalLastSyncAt, Arg.Any<CancellationToken>())
            .Returns<IReadOnlyList<SourceDocument>>(_ => throw new InvalidOperationException("source unreachable"));

        var engine = new SyncEngine([source], store, NullLogger<SyncEngine>.Instance);
        var result = await engine.RunSyncAsync(CancellationToken.None);

        Assert.That(result.Report!.PerSource[0].Status, Is.EqualTo(SyncStatus.Failed));
        Assert.That(result.Report.PerSource[0].Error, Is.EqualTo("source unreachable"));

        await store.Received(1).SaveSyncStateAsync(
            Arg.Is<SyncState>(s =>
                s != null &&
                s.SourceName == "FileDrop" &&
                s.Status == SyncStatus.Failed &&
                s.LastSyncAt == originalLastSyncAt &&
                s.LastError == "source unreachable"),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task RunSyncAsync_continues_with_other_sources_after_one_fails()
    {
        var store = Substitute.For<IKnowledgeStore>();
        store.GetSyncStateAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((SyncState?)null);

        var failingSource = MakeSource("Failing");
        failingSource.GetChangesAsync(Arg.Any<DateTimeOffset?>(), Arg.Any<CancellationToken>())
            .Returns<IReadOnlyList<SourceDocument>>(_ => throw new InvalidOperationException("boom"));

        var okSource = MakeSource("Ok");
        okSource.GetChangesAsync(Arg.Any<DateTimeOffset?>(), Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<SourceDocument>)[]);
        okSource.GetAllIdsAsync(Arg.Any<CancellationToken>()).Returns((IReadOnlyList<string>)[]);

        var engine = new SyncEngine([failingSource, okSource], store, NullLogger<SyncEngine>.Instance);
        var result = await engine.RunSyncAsync(CancellationToken.None);

        Assert.That(result.Report!.PerSource.Select(p => (p.SourceName, p.Status)),
            Is.EquivalentTo(new[] { ("Failing", SyncStatus.Failed), ("Ok", SyncStatus.Ok) }));
    }

    [TestCase("FileDrop:adr-007.md", "Some Title", DocumentType.Adr)]
    [TestCase("FileDrop:guideline-logging.md", "Some Title", DocumentType.Guideline)]
    [TestCase("FileDrop:standard-naming.md", "Some Title", DocumentType.Standard)]
    [TestCase("FileDrop:diagram-topology.md", "Some Title", DocumentType.Diagram)]
    [TestCase("FileDrop:notes.md", "Random Notes", DocumentType.Other)]
    [TestCase("FileDrop:notes.md", "ADR: Use Kafka", DocumentType.Adr)]
    public async Task RunSyncAsync_infers_DocumentType_from_filename_or_title_heuristics(
        string sourceId, string title, DocumentType expectedType)
    {
        var store = Substitute.For<IKnowledgeStore>();
        store.GetSyncStateAsync("FileDrop", Arg.Any<CancellationToken>()).Returns((SyncState?)null);

        var source = MakeSource("FileDrop");
        var sourceDoc = new SourceDocument(sourceId, title, "# Heading\nBody", DateTimeOffset.UtcNow, "text/markdown");
        source.GetChangesAsync(null, Arg.Any<CancellationToken>()).Returns([sourceDoc]);
        source.GetAllIdsAsync(Arg.Any<CancellationToken>()).Returns([sourceId]);

        KnowledgeDocument? captured = null;
        store.When(s => s.UpsertAsync(Arg.Any<KnowledgeDocument>(), Arg.Any<CancellationToken>()))
            .Do(ci => captured = ci.Arg<KnowledgeDocument>());

        var engine = new SyncEngine([source], store, NullLogger<SyncEngine>.Instance);
        await engine.RunSyncAsync(CancellationToken.None);

        Assert.That(captured!.Type, Is.EqualTo(expectedType));
    }

    [Test]
    public async Task RunSyncAsync_returns_Started_false_when_a_sync_is_already_running()
    {
        var store = Substitute.For<IKnowledgeStore>();
        store.GetSyncStateAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((SyncState?)null);

        var enteredGetChanges = new TaskCompletionSource();
        var releaseGetChanges = new TaskCompletionSource();

        var source = MakeSource("FileDrop");
        source.GetChangesAsync(Arg.Any<DateTimeOffset?>(), Arg.Any<CancellationToken>())
            .Returns(async _ =>
            {
                enteredGetChanges.TrySetResult();
                await releaseGetChanges.Task;
                return (IReadOnlyList<SourceDocument>)[];
            });
        source.GetAllIdsAsync(Arg.Any<CancellationToken>()).Returns((IReadOnlyList<string>)[]);

        var engine = new SyncEngine([source], store, NullLogger<SyncEngine>.Instance);

        var firstRun = engine.RunSyncAsync(CancellationToken.None);
        await enteredGetChanges.Task;

        var secondResult = await engine.RunSyncAsync(CancellationToken.None);
        Assert.That(secondResult.Started, Is.False);
        Assert.That(secondResult.Report, Is.Null);

        releaseGetChanges.TrySetResult();
        var firstResult = await firstRun;
        Assert.That(firstResult.Started, Is.True);
    }

    [Test]
    public async Task RunSyncAsync_logs_an_information_message_on_a_successful_sync()
    {
        var store = Substitute.For<IKnowledgeStore>();
        store.GetSyncStateAsync("FileDrop", Arg.Any<CancellationToken>()).Returns((SyncState?)null);

        var source = MakeSource("FileDrop");
        source.GetChangesAsync(null, Arg.Any<CancellationToken>()).Returns((IReadOnlyList<SourceDocument>)[]);
        source.GetAllIdsAsync(Arg.Any<CancellationToken>()).Returns((IReadOnlyList<string>)[]);

        var logger = new RecordingLogger<SyncEngine>();
        var engine = new SyncEngine([source], store, logger);

        await engine.RunSyncAsync(CancellationToken.None);

        Assert.That(logger.Entries, Has.Some.Matches<(LogLevel Level, string Message)>(
            e => e.Level == LogLevel.Information && e.Message.Contains("Sync completed") && e.Message.Contains("FileDrop")));
    }

    private sealed class RecordingLogger<T> : ILogger<T>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => Entries.Add((logLevel, formatter(state, exception)));
    }
}
