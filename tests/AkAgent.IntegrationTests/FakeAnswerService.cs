using AkAgent.Domain.Enums;
using AkAgent.Domain.Interfaces;
using AkAgent.Domain.Models;

namespace AkAgent.IntegrationTests;

/// Test double for the LLM-backed AnswerService (mocked at the IAnswerService
/// boundary per CLAUDE.md). Performs real retrieval against the real
/// IKnowledgeStore so answers genuinely reflect sync state, without ever
/// calling the Anthropic API.
public sealed class FakeAnswerService : IAnswerService
{
    private readonly IKnowledgeStore _store;

    public FakeAnswerService(IKnowledgeStore store)
    {
        _store = store;
    }

    public async Task<AgentAnswer> AskAsync(string question, CancellationToken ct)
    {
        var hits = await _store.SearchAsync(question, 5, ct);
        if (hits.Count == 0)
            return new AgentAnswer("This is not documented in the architecture knowledge base.", [], null, DateTimeOffset.UtcNow, false);

        var answer = string.Join(" ", hits.Select(h => h.BestSection?.Content ?? h.Document.Content));
        var citations = hits.Select(ToCitation).ToList();
        return new AgentAnswer(answer, citations, hits.Min(h => h.Document.LastModified), DateTimeOffset.UtcNow, true);
    }

    public async Task<ValidationResult> ValidateAsync(string proposal, CancellationToken ct)
    {
        var hits = await _store.SearchAsync(proposal, 5, ct);
        if (hits.Count == 0)
            return new ValidationResult(ValidationDecision.NotCovered, "not covered by any document", [], DateTimeOffset.UtcNow);

        var citations = hits.Select(ToCitation).ToList();
        return new ValidationResult(ValidationDecision.Aligned, "fake aligned decision", citations, DateTimeOffset.UtcNow);
    }

    private static Citation ToCitation(SearchHit hit)
        => new(hit.Document.Id, hit.Document.Title, hit.BestSection?.Heading, hit.Document.LastModified);
}
