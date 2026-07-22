using System.Text.Json;

namespace AkAgent.Infrastructure.Llm;

/// Thin seam over the Anthropic Messages API so AnswerService's own logic
/// (prompt building, retrieval, empty-hits branch) can be unit tested without
/// a live API call. Returns the response text (plain answer, or JSON text
/// when a schema is supplied for structured output).
public interface IAnthropicMessageClient
{
    Task<string> CreateMessageAsync(AnthropicMessageRequest request, CancellationToken ct);
}

public sealed record AnthropicMessageRequest(string SystemPrompt, string UserMessage, JsonElement? JsonSchema = null);
