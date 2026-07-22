using System.Text;
using System.Text.Json;
using AkAgent.Domain.Enums;
using AkAgent.Domain.Interfaces;
using AkAgent.Domain.Models;
using Microsoft.Extensions.Logging;

namespace AkAgent.Infrastructure.Llm;

public sealed class AnswerService : IAnswerService
{
    private const string NotDocumentedAnswer = "This is not documented in the architecture knowledge base.";

    private const string NotCoveredExplanation =
        "the architecture documentation does not cover this; consider creating an ADR";

    private const string QaSystemPrompt = """
        Answer only from the provided architecture documents.
        If the documents do not contain the answer, say so explicitly; never infer undocumented decisions.
        Cite every claim with document title and section.
        If documents conflict, present both positions and flag the conflict.
        """;

    private const string ValidationSystemPrompt = """
        You are validating a proposed implementation approach against documented architecture decisions.
        Compare the proposal to the provided architecture documents and determine whether it aligns with
        documented constraints or conflicts with them ("warning"). Only use "warning" for an actual
        conflict with a documented constraint, not merely a topic the documents don't fully cover.
        Respond only with the requested JSON structure. In referencedDocuments, list the exact Document ID
        (not the title) of every document you relied on.
        """;

    private static readonly JsonElement ValidationJsonSchema = JsonSerializer.SerializeToElement(new
    {
        type = "object",
        properties = new
        {
            decision = new { type = "string", @enum = new[] { "aligned", "warning" } },
            explanation = new { type = "string" },
            referencedDocuments = new { type = "array", items = new { type = "string" } }
        },
        required = new[] { "decision", "explanation", "referencedDocuments" },
        additionalProperties = false
    });

    private static readonly JsonSerializerOptions ValidationResponseJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly IKnowledgeStore _store;
    private readonly IEnumerable<IKnowledgeSource> _sources;
    private readonly IAnthropicMessageClient _llmClient;
    private readonly ILogger<AnswerService> _logger;

    public AnswerService(
        IKnowledgeStore store,
        IEnumerable<IKnowledgeSource> sources,
        IAnthropicMessageClient llmClient,
        ILogger<AnswerService> logger)
    {
        _store = store;
        _sources = sources;
        _llmClient = llmClient;
        _logger = logger;
    }

    public async Task<AgentAnswer> AskAsync(string question, CancellationToken ct)
    {
        var hits = await _store.SearchAsync(question, 5, ct);
        var lastSyncAt = await GetLastSyncAtAsync(ct);

        _logger.LogInformation(
            "Ask: questionHash={QuestionHash}, hitCount={HitCount}, topScore={TopScore}, documented={Documented}",
            Hash(question), hits.Count, hits.Count > 0 ? hits[0].Score : 0, hits.Count > 0);

        if (hits.Count == 0)
            return new AgentAnswer(NotDocumentedAnswer, [], OldestSourceModified: null, lastSyncAt, IsDocumented: false);

        var userMessage = BuildQaUserMessage(question, hits);
        var answerText = await _llmClient.CreateMessageAsync(new AnthropicMessageRequest(QaSystemPrompt, userMessage), ct);

        var citations = hits.Select(ToCitation).ToList();
        var oldestSourceModified = hits.Min(h => h.Document.LastModified);

        return new AgentAnswer(answerText, citations, oldestSourceModified, lastSyncAt, IsDocumented: true);
    }

    public async Task<ValidationResult> ValidateAsync(string proposal, CancellationToken ct)
    {
        var hits = await _store.SearchAsync(proposal, 5, ct);
        var lastSyncAt = await GetLastSyncAtAsync(ct);

        if (hits.Count == 0)
        {
            _logger.LogInformation(
                "Validate: proposalHash={ProposalHash}, hitCount=0, decision={Decision}",
                Hash(proposal), ValidationDecision.NotCovered);
            return new ValidationResult(ValidationDecision.NotCovered, NotCoveredExplanation, [], lastSyncAt);
        }

        var userMessage = BuildValidationUserMessage(proposal, hits);
        var responseJson = await _llmClient.CreateMessageAsync(
            new AnthropicMessageRequest(ValidationSystemPrompt, userMessage, ValidationJsonSchema), ct);

        var parsed = JsonSerializer.Deserialize<ValidationLlmResponse>(responseJson, ValidationResponseJsonOptions)
                     ?? throw new InvalidOperationException("LLM validation response could not be parsed.");

        var decision = parsed.Decision.Equals("warning", StringComparison.OrdinalIgnoreCase)
            ? ValidationDecision.Warning
            : ValidationDecision.Aligned;

        _logger.LogInformation(
            "Validate: proposalHash={ProposalHash}, hitCount={HitCount}, topScore={TopScore}, decision={Decision}",
            Hash(proposal), hits.Count, hits[0].Score, decision);

        var citations = hits
            .Where(h => parsed.ReferencedDocuments.Contains(h.Document.Id))
            .Select(ToCitation)
            .ToList();

        return new ValidationResult(decision, parsed.Explanation, citations, lastSyncAt);
    }

    private static string Hash(string value)
        => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private async Task<DateTimeOffset> GetLastSyncAtAsync(CancellationToken ct)
    {
        DateTimeOffset? earliest = null;
        foreach (var source in _sources)
        {
            var state = await _store.GetSyncStateAsync(source.Name, ct);
            if (state?.LastSyncAt is { } lastSyncAt && (earliest is null || lastSyncAt < earliest))
                earliest = lastSyncAt;
        }

        return earliest ?? DateTimeOffset.MinValue;
    }

    private static Citation ToCitation(SearchHit hit)
        => new(hit.Document.Id, hit.Document.Title, hit.BestSection?.Heading, hit.Document.LastModified);

    private static string BuildQaUserMessage(string question, IReadOnlyList<SearchHit> hits)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Architecture documents:");
        AppendDocuments(sb, hits);
        sb.AppendLine("Question:");
        sb.AppendLine(question);
        return sb.ToString();
    }

    private static string BuildValidationUserMessage(string proposal, IReadOnlyList<SearchHit> hits)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Architecture documents:");
        AppendDocuments(sb, hits);
        sb.AppendLine("Proposed approach:");
        sb.AppendLine(proposal);
        return sb.ToString();
    }

    private static void AppendDocuments(StringBuilder sb, IReadOnlyList<SearchHit> hits)
    {
        foreach (var hit in hits)
        {
            sb.AppendLine("---");
            sb.AppendLine($"Document ID: {hit.Document.Id}");
            sb.AppendLine($"Title: {hit.Document.Title}");
            sb.AppendLine($"Section: {hit.BestSection?.Heading ?? "(none)"}");
            sb.AppendLine($"Last modified: {hit.Document.LastModified:O}");
            sb.AppendLine(hit.BestSection?.Content ?? hit.Document.Content);
        }
        sb.AppendLine("---");
    }

    private sealed record ValidationLlmResponse(string Decision, string Explanation, IReadOnlyList<string> ReferencedDocuments);
}
