using System.ComponentModel;
using AkAgent.Mcp.Formatting;
using ModelContextProtocol.Server;

namespace AkAgent.Mcp.Tools;

[McpServerToolType]
public sealed class ArchitectureKnowledgeTools
{
    private readonly ArchitectureApiClient _apiClient;

    public ArchitectureKnowledgeTools(ArchitectureApiClient apiClient)
    {
        _apiClient = apiClient;
    }

    [McpServerTool(Name = "query_architecture_knowledge")]
    [Description(
        "Ask a question about the documented software architecture (decisions, standards, " +
        "patterns, constraints). Returns an answer with citations and freshness information.")]
    public async Task<string> QueryArchitectureKnowledge(
        [Description("The question to ask about the documented architecture")] string question,
        CancellationToken ct)
    {
        try
        {
            var answer = await _apiClient.AskAsync(question, ct);
            return ResponseFormatter.FormatAskResponse(answer);
        }
        catch (HttpRequestException ex)
        {
            return $"Unable to reach the architecture knowledge service: {ex.Message}";
        }
    }

    [McpServerTool(Name = "validate_against_architecture")]
    [Description(
        "Validate a planned implementation approach against the documented architecture BEFORE " +
        "writing code. Returns aligned / warning / not-covered with explanation and sources.")]
    public async Task<string> ValidateAgainstArchitecture(
        [Description("Short description of the planned approach")] string proposal,
        CancellationToken ct)
    {
        try
        {
            var result = await _apiClient.ValidateAsync(proposal, ct);
            return ResponseFormatter.FormatValidateResponse(result);
        }
        catch (HttpRequestException ex)
        {
            return $"Unable to reach the architecture knowledge service: {ex.Message}";
        }
    }
}
