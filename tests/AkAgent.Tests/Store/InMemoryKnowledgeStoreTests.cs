using AkAgent.Domain.Enums;
using AkAgent.Domain.Models;
using AkAgent.Infrastructure.Configuration;
using AkAgent.Infrastructure.Store;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace AkAgent.Tests.Store;

public class InMemoryKnowledgeStoreTests
{
    private string _dataPath = null!;

    [SetUp]
    public void SetUp()
    {
        _dataPath = Path.Combine(Path.GetTempPath(), "ak-agent-tests", Guid.NewGuid().ToString());
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_dataPath))
            Directory.Delete(_dataPath, recursive: true);
    }

    private InMemoryKnowledgeStore CreateStore(double minScore = 0.15)
    {
        var options = Options.Create(new StoreOptions { DataPath = _dataPath, MinScore = minScore, MaxResults = 5 });
        return new InMemoryKnowledgeStore(options, NullLogger<InMemoryKnowledgeStore>.Instance);
    }

    private static KnowledgeDocument MakeDoc(
        string id,
        string title,
        IReadOnlyList<DocumentSection>? sections = null,
        string content = "")
        => new()
        {
            Id = id,
            SourceName = "FileDrop",
            Title = title,
            Content = content,
            ContentHash = "hash-" + id,
            LastModified = DateTimeOffset.UtcNow,
            Type = DocumentType.Adr,
            Sections = sections ?? []
        };

    [Test]
    public async Task UpsertAsync_then_GetAsync_returns_the_document()
    {
        var store = CreateStore();
        var doc = MakeDoc(
            "filedrop:adr-001.md",
            "Service Communication",
            sections: [new DocumentSection("Overview", "some content", 0)]);

        await store.UpsertAsync(doc, CancellationToken.None);
        var result = await store.GetAsync(doc.Id, CancellationToken.None);

        AssertDocumentsEqual(result, doc);
    }

    [Test]
    public async Task GetAsync_returns_null_for_unknown_id()
    {
        var store = CreateStore();
        var result = await store.GetAsync("nope", CancellationToken.None);
        Assert.That(result, Is.Null);
    }

    [Test]
    public async Task RemoveAsync_removes_the_document()
    {
        var store = CreateStore();
        var doc = MakeDoc("filedrop:adr-001.md", "Service Communication");
        await store.UpsertAsync(doc, CancellationToken.None);

        await store.RemoveAsync(doc.Id, CancellationToken.None);

        Assert.That(await store.GetAsync(doc.Id, CancellationToken.None), Is.Null);
    }

    [Test]
    public async Task ListAsync_returns_summaries_for_all_documents()
    {
        var store = CreateStore();
        await store.UpsertAsync(MakeDoc("a", "Doc A"), CancellationToken.None);
        await store.UpsertAsync(MakeDoc("b", "Doc B"), CancellationToken.None);

        var summaries = await store.ListAsync(CancellationToken.None);

        Assert.That(summaries.Select(s => s.Id), Is.EquivalentTo(["a", "b"]));
    }

    [Test]
    public async Task SearchAsync_ranks_title_matches_above_body_only_matches()
    {
        var store = CreateStore(minScore: 0.0);

        var titleMatch = MakeDoc(
            "title-doc",
            "Messaging Patterns",
            sections: [new DocumentSection("Overview", "unrelated content here", 0)]);

        var bodyMatch = MakeDoc(
            "body-doc",
            "Unrelated Title",
            sections: [new DocumentSection("Details", "we use messaging for async communication", 0)]);

        await store.UpsertAsync(titleMatch, CancellationToken.None);
        await store.UpsertAsync(bodyMatch, CancellationToken.None);

        var hits = await store.SearchAsync("messaging", 5, CancellationToken.None);

        Assert.That(hits, Has.Count.EqualTo(2));
        Assert.That(hits[0].Document.Id, Is.EqualTo("title-doc"));
        Assert.That(hits[0].Score, Is.EqualTo(1.0));
        Assert.That(hits[1].Document.Id, Is.EqualTo("body-doc"));
        Assert.That(hits[1].Score, Is.LessThan(1.0));
    }

    [Test]
    public async Task SearchAsync_sets_BestSection_to_the_highest_scoring_section()
    {
        var store = CreateStore(minScore: 0.0);

        var doc = MakeDoc(
            "doc",
            "Architecture Overview",
            sections:
            [
                new DocumentSection("Intro", "background information", 0),
                new DocumentSection("Caching Strategy", "caching caching caching decisions", 1)
            ]);

        await store.UpsertAsync(doc, CancellationToken.None);

        var hits = await store.SearchAsync("caching", 5, CancellationToken.None);

        Assert.That(hits, Has.Count.EqualTo(1));
        Assert.That(hits[0].BestSection?.Heading, Is.EqualTo("Caching Strategy"));
    }

    [Test]
    public async Task SearchAsync_discards_hits_below_the_MinScore_threshold()
    {
        var store = CreateStore(minScore: 0.5);

        var strong = MakeDoc(
            "strong",
            "Deployment Strategy",
            sections: [new DocumentSection("Details", "deployment deployment deployment rollout", 0)]);

        var weak = MakeDoc(
            "weak",
            "Unrelated",
            sections: [new DocumentSection("Details", "a single mention of deployment here", 0)]);

        await store.UpsertAsync(strong, CancellationToken.None);
        await store.UpsertAsync(weak, CancellationToken.None);

        var hits = await store.SearchAsync("deployment", 5, CancellationToken.None);

        Assert.That(hits.Select(h => h.Document.Id), Does.Not.Contain("weak"));
    }

    [Test]
    public async Task SearchAsync_returns_empty_when_no_document_matches()
    {
        var store = CreateStore();
        await store.UpsertAsync(MakeDoc("a", "Doc A", content: "nothing relevant"), CancellationToken.None);

        var hits = await store.SearchAsync("zzzznomatch", 5, CancellationToken.None);

        Assert.That(hits, Is.Empty);
    }

    [Test]
    public async Task SearchAsync_ignores_stop_words_in_the_query()
    {
        var store = CreateStore(minScore: 0.0);
        await store.UpsertAsync(MakeDoc("a", "Caching"), CancellationToken.None);

        var hits = await store.SearchAsync("the of caching", 5, CancellationToken.None);

        Assert.That(hits, Has.Count.EqualTo(1));
        Assert.That(hits[0].Document.Id, Is.EqualTo("a"));
    }

    [Test]
    public async Task SearchAsync_respects_maxResults()
    {
        var store = CreateStore(minScore: 0.0);
        for (var i = 0; i < 10; i++)
            await store.UpsertAsync(MakeDoc($"doc-{i}", "Caching Strategy"), CancellationToken.None);

        var hits = await store.SearchAsync("caching", 3, CancellationToken.None);

        Assert.That(hits, Has.Count.EqualTo(3));
    }

    [Test]
    public async Task SyncState_round_trips_through_SaveSyncStateAsync_and_GetSyncStateAsync()
    {
        var store = CreateStore();
        var state = new SyncState
        {
            SourceName = "FileDrop",
            LastSyncAt = DateTimeOffset.UtcNow,
            Status = SyncStatus.Ok,
            DocumentHashes = new Dictionary<string, string> { ["a"] = "hash-a" }
        };

        await store.SaveSyncStateAsync(state, CancellationToken.None);
        var result = await store.GetSyncStateAsync("FileDrop", CancellationToken.None);

        Assert.That(result, Is.EqualTo(state));
    }

    [Test]
    public async Task GetSyncStateAsync_returns_null_for_unknown_source()
    {
        var store = CreateStore();
        var result = await store.GetSyncStateAsync("Unknown", CancellationToken.None);
        Assert.That(result, Is.Null);
    }

    [Test]
    public async Task Documents_and_sync_state_persist_to_disk_and_reload_in_a_new_store_instance()
    {
        var store = CreateStore();
        var doc = MakeDoc("filedrop:adr-001.md", "Service Communication");
        await store.UpsertAsync(doc, CancellationToken.None);

        var state = new SyncState
        {
            SourceName = "FileDrop",
            LastSyncAt = DateTimeOffset.UtcNow,
            Status = SyncStatus.Ok,
            DocumentHashes = new Dictionary<string, string> { [doc.Id] = doc.ContentHash }
        };
        await store.SaveSyncStateAsync(state, CancellationToken.None);

        var reloaded = CreateStore();

        AssertDocumentsEqual(await reloaded.GetAsync(doc.Id, CancellationToken.None), doc);

        var reloadedState = await reloaded.GetSyncStateAsync("FileDrop", CancellationToken.None);
        Assert.That(reloadedState, Is.Not.Null);
        Assert.That(reloadedState!.SourceName, Is.EqualTo(state.SourceName));
        Assert.That(reloadedState.Status, Is.EqualTo(state.Status));
        Assert.That(reloadedState.LastSyncAt, Is.EqualTo(state.LastSyncAt));
        Assert.That(reloadedState.DocumentHashes, Is.EqualTo(state.DocumentHashes));
    }

    [Test]
    public async Task RemoveAsync_persists_the_deletion_across_store_instances()
    {
        var store = CreateStore();
        var doc = MakeDoc("filedrop:adr-001.md", "Service Communication");
        await store.UpsertAsync(doc, CancellationToken.None);
        await store.RemoveAsync(doc.Id, CancellationToken.None);

        var reloaded = CreateStore();

        Assert.That(await reloaded.GetAsync(doc.Id, CancellationToken.None), Is.Null);
    }

    private static void AssertDocumentsEqual(KnowledgeDocument? actual, KnowledgeDocument expected)
    {
        Assert.That(actual, Is.Not.Null);
        Assert.That(actual!.Id, Is.EqualTo(expected.Id));
        Assert.That(actual.SourceName, Is.EqualTo(expected.SourceName));
        Assert.That(actual.Title, Is.EqualTo(expected.Title));
        Assert.That(actual.Content, Is.EqualTo(expected.Content));
        Assert.That(actual.ContentHash, Is.EqualTo(expected.ContentHash));
        Assert.That(actual.LastModified, Is.EqualTo(expected.LastModified));
        Assert.That(actual.Type, Is.EqualTo(expected.Type));
        Assert.That(actual.Sections, Is.EqualTo(expected.Sections));
    }
}
