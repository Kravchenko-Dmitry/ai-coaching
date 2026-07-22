using System.Text.Json;
using Anthropic;
using Anthropic.Models.Messages;
using AkAgent.Domain.Exceptions;
using AkAgent.Infrastructure.Configuration;
using Microsoft.Extensions.Options;

namespace AkAgent.Infrastructure.Llm;

public sealed class AnthropicMessageClient : IAnthropicMessageClient
{
    private const long MaxTokens = 4096;

    private readonly AnthropicClient _client;
    private readonly LlmOptions _options;

    public AnthropicMessageClient(AnthropicClient client, IOptions<LlmOptions> options)
    {
        _client = client;
        _options = options.Value;
    }

    public async Task<string> CreateMessageAsync(AnthropicMessageRequest request, CancellationToken ct)
    {
        var parameters = new MessageCreateParams
        {
            Model = _options.Model,
            MaxTokens = MaxTokens,
            System = request.SystemPrompt,
            Messages = [new() { Role = Role.User, Content = request.UserMessage }],
            OutputConfig = request.JsonSchema is { } schema
                ? new OutputConfig { Format = new JsonOutputFormat { Schema = ToSchemaDictionary(schema) } }
                : null,
        };

        var response = await _client.Messages.Create(parameters, cancellationToken: ct);

        var text = response.Content
            .Select(block => block.Value)
            .OfType<TextBlock>()
            .Select(block => block.Text)
            .FirstOrDefault();

        if (string.IsNullOrEmpty(text))
            throw new AnswerServiceUnavailableException("Anthropic response contained no text content.");

        return text;
    }

    private static Dictionary<string, JsonElement> ToSchemaDictionary(JsonElement schema)
    {
        var dict = new Dictionary<string, JsonElement>();
        foreach (var property in schema.EnumerateObject())
            dict[property.Name] = property.Value;
        return dict;
    }
}
