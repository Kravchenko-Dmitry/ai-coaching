using AkAgent.Domain.Enums;
using AkAgent.Domain.Interfaces;
using AkAgent.Domain.Models;
using AkAgent.Infrastructure.Llm;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace AkAgent.Tests.Llm;

public class AnswerServiceTests
{
    private static KnowledgeDocument MakeDoc(
        string id,
        string title,
        DateTimeOffset lastModified,
        string content = "content",
        IReadOnlyList<DocumentSection>? sections = null)
        => new()
        {
            Id = id,
            SourceName = "FileDrop",
            Title = title,
            Content = content,
            ContentHash = "hash-" + id,
            LastModified = lastModified,
            Type = DocumentType.Adr,
            Sections = sections ?? []
        };

    private static IKnowledgeSource MakeSource(string name)
    {
        var source = Substitute.For<IKnowledgeSource>();
        source.Name.Returns(name);
        return source;
    }

    [Test]
    public async Task AskAsync_returns_not_documented_when_hits_empty()
    {
        var store = Substitute.For<IKnowledgeStore>();
        store.SearchAsync("what is X", 5, Arg.Any<CancellationToken>()).Returns((IReadOnlyList<SearchHit>)[]);
        store.GetSyncStateAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((SyncState?)null);

        var llmClient = Substitute.For<IAnthropicMessageClient>();
        var service = new AnswerService(store, [], llmClient, NullLogger<AnswerService>.Instance);

        var result = await service.AskAsync("what is X", CancellationToken.None);

        Assert.That(result.IsDocumented, Is.False);
        Assert.That(result.Answer, Is.EqualTo("This is not documented in the architecture knowledge base."));
        Assert.That(result.Citations, Is.Empty);
        Assert.That(result.OldestSourceModified, Is.Null);
        await llmClient.DidNotReceiveWithAnyArgs().CreateMessageAsync(default!, default);
    }

    [Test]
    public async Task AskAsync_returns_answer_with_citations_and_freshness_when_hits_present()
    {
        var lastModified = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var doc = MakeDoc(
            "FileDrop:adr-001.md",
            "Service Communication",
            lastModified,
            sections: [new DocumentSection("Overview", "We use REST.", 0)]);
        var hit = new SearchHit(doc, doc.Sections[0], 0.9);

        var store = Substitute.For<IKnowledgeStore>();
        store.SearchAsync("how do services talk", 5, Arg.Any<CancellationToken>()).Returns((IReadOnlyList<SearchHit>)[hit]);

        var syncedAt = new DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.Zero);
        var source = MakeSource("FileDrop");
        store.GetSyncStateAsync("FileDrop", Arg.Any<CancellationToken>())
            .Returns(new SyncState { SourceName = "FileDrop", LastSyncAt = syncedAt, Status = SyncStatus.Ok });

        var llmClient = Substitute.For<IAnthropicMessageClient>();
        llmClient.CreateMessageAsync(Arg.Any<AnthropicMessageRequest>(), Arg.Any<CancellationToken>())
            .Returns("We use REST for service-to-service communication.");

        var service = new AnswerService(store, [source], llmClient, NullLogger<AnswerService>.Instance);

        var result = await service.AskAsync("how do services talk", CancellationToken.None);

        Assert.That(result.IsDocumented, Is.True);
        Assert.That(result.Answer, Is.EqualTo("We use REST for service-to-service communication."));
        Assert.That(result.Citations, Has.Count.EqualTo(1));
        Assert.That(result.Citations[0].DocumentId, Is.EqualTo(doc.Id));
        Assert.That(result.Citations[0].Section, Is.EqualTo("Overview"));
        Assert.That(result.OldestSourceModified, Is.EqualTo(lastModified));
        Assert.That(result.LastSyncAt, Is.EqualTo(syncedAt));

        await llmClient.Received(1).CreateMessageAsync(
            Arg.Is<AnthropicMessageRequest>(r =>
                r != null && r.UserMessage.Contains("Service Communication") && r.UserMessage.Contains("how do services talk")),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task AskAsync_LastSyncAt_is_the_earliest_across_all_sources()
    {
        var doc = MakeDoc("FileDrop:a.md", "A", DateTimeOffset.UtcNow);
        var hit = new SearchHit(doc, null, 1.0);

        var store = Substitute.For<IKnowledgeStore>();
        store.SearchAsync(Arg.Any<string>(), 5, Arg.Any<CancellationToken>()).Returns((IReadOnlyList<SearchHit>)[hit]);

        var earlier = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var later = new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);
        store.GetSyncStateAsync("SourceA", Arg.Any<CancellationToken>())
            .Returns(new SyncState { SourceName = "SourceA", LastSyncAt = later, Status = SyncStatus.Ok });
        store.GetSyncStateAsync("SourceB", Arg.Any<CancellationToken>())
            .Returns(new SyncState { SourceName = "SourceB", LastSyncAt = earlier, Status = SyncStatus.Ok });

        var llmClient = Substitute.For<IAnthropicMessageClient>();
        llmClient.CreateMessageAsync(Arg.Any<AnthropicMessageRequest>(), Arg.Any<CancellationToken>()).Returns("answer");

        var service = new AnswerService(
            store, [MakeSource("SourceA"), MakeSource("SourceB")], llmClient, NullLogger<AnswerService>.Instance);

        var result = await service.AskAsync("question", CancellationToken.None);

        Assert.That(result.LastSyncAt, Is.EqualTo(earlier));
    }

    [Test]
    public async Task AskAsync_LastSyncAt_is_MinValue_when_no_source_has_synced()
    {
        var doc = MakeDoc("FileDrop:a.md", "A", DateTimeOffset.UtcNow);
        var hit = new SearchHit(doc, null, 1.0);

        var store = Substitute.For<IKnowledgeStore>();
        store.SearchAsync(Arg.Any<string>(), 5, Arg.Any<CancellationToken>()).Returns((IReadOnlyList<SearchHit>)[hit]);
        store.GetSyncStateAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((SyncState?)null);

        var llmClient = Substitute.For<IAnthropicMessageClient>();
        llmClient.CreateMessageAsync(Arg.Any<AnthropicMessageRequest>(), Arg.Any<CancellationToken>()).Returns("answer");

        var service = new AnswerService(store, [MakeSource("FileDrop")], llmClient, NullLogger<AnswerService>.Instance);

        var result = await service.AskAsync("question", CancellationToken.None);

        Assert.That(result.LastSyncAt, Is.EqualTo(DateTimeOffset.MinValue));
    }

    [Test]
    public async Task ValidateAsync_returns_NotCovered_when_hits_empty()
    {
        var store = Substitute.For<IKnowledgeStore>();
        store.SearchAsync(Arg.Any<string>(), 5, Arg.Any<CancellationToken>()).Returns((IReadOnlyList<SearchHit>)[]);
        store.GetSyncStateAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((SyncState?)null);

        var llmClient = Substitute.For<IAnthropicMessageClient>();
        var service = new AnswerService(store, [], llmClient, NullLogger<AnswerService>.Instance);

        var result = await service.ValidateAsync("use a new queue", CancellationToken.None);

        Assert.That(result.Decision, Is.EqualTo(ValidationDecision.NotCovered));
        Assert.That(result.Explanation, Does.Contain("consider creating an ADR"));
        Assert.That(result.Citations, Is.Empty);
        await llmClient.DidNotReceiveWithAnyArgs().CreateMessageAsync(default!, default);
    }

    [Test]
    public async Task ValidateAsync_parses_aligned_decision_and_filters_citations_to_referenced_documents()
    {
        var docA = MakeDoc("FileDrop:adr-001.md", "Messaging", DateTimeOffset.UtcNow);
        var docB = MakeDoc("FileDrop:adr-002.md", "Storage", DateTimeOffset.UtcNow);
        var hits = new List<SearchHit> { new(docA, null, 0.9), new(docB, null, 0.5) };

        var store = Substitute.For<IKnowledgeStore>();
        store.SearchAsync(Arg.Any<string>(), 5, Arg.Any<CancellationToken>()).Returns((IReadOnlyList<SearchHit>)hits);
        store.GetSyncStateAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((SyncState?)null);

        var llmClient = Substitute.For<IAnthropicMessageClient>();
        llmClient.CreateMessageAsync(Arg.Any<AnthropicMessageRequest>(), Arg.Any<CancellationToken>())
            .Returns("""{"decision":"aligned","explanation":"Matches the messaging pattern.","referencedDocuments":["FileDrop:adr-001.md"]}""");

        var service = new AnswerService(store, [], llmClient, NullLogger<AnswerService>.Instance);

        var result = await service.ValidateAsync("use async messaging for order events", CancellationToken.None);

        Assert.That(result.Decision, Is.EqualTo(ValidationDecision.Aligned));
        Assert.That(result.Explanation, Is.EqualTo("Matches the messaging pattern."));
        Assert.That(result.Citations, Has.Count.EqualTo(1));
        Assert.That(result.Citations[0].DocumentId, Is.EqualTo(docA.Id));

        await llmClient.Received(1).CreateMessageAsync(
            Arg.Is<AnthropicMessageRequest>(r => r != null && r.JsonSchema != null),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task ValidateAsync_parses_warning_decision()
    {
        var doc = MakeDoc("FileDrop:adr-003.md", "Data Storage", DateTimeOffset.UtcNow);
        var hits = new List<SearchHit> { new(doc, null, 0.9) };

        var store = Substitute.For<IKnowledgeStore>();
        store.SearchAsync(Arg.Any<string>(), 5, Arg.Any<CancellationToken>()).Returns((IReadOnlyList<SearchHit>)hits);
        store.GetSyncStateAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((SyncState?)null);

        var llmClient = Substitute.For<IAnthropicMessageClient>();
        llmClient.CreateMessageAsync(Arg.Any<AnthropicMessageRequest>(), Arg.Any<CancellationToken>())
            .Returns("""{"decision":"warning","explanation":"Conflicts with the documented storage standard.","referencedDocuments":["FileDrop:adr-003.md"]}""");

        var service = new AnswerService(store, [], llmClient, NullLogger<AnswerService>.Instance);

        var result = await service.ValidateAsync("store user data in a flat file", CancellationToken.None);

        Assert.That(result.Decision, Is.EqualTo(ValidationDecision.Warning));
        Assert.That(result.Citations, Has.Count.EqualTo(1));
    }
}
