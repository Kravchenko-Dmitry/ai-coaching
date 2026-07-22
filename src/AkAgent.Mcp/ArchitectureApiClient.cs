using System.Net.Http.Json;
using AkAgent.Domain.Models;

namespace AkAgent.Mcp;

/// Calls the running REST API (SPEC.md §4.6: "stdio process + HTTP call to the
/// service is the simplest and is the MVP choice").
public sealed class ArchitectureApiClient
{
    private readonly HttpClient _httpClient;

    public ArchitectureApiClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<AgentAnswer> AskAsync(string question, CancellationToken ct)
    {
        var response = await _httpClient.PostAsJsonAsync("/ask", new { question }, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<AgentAnswer>(cancellationToken: ct)
               ?? throw new InvalidOperationException("Empty response from /ask.");
    }

    public async Task<ValidationResult> ValidateAsync(string proposal, CancellationToken ct)
    {
        var response = await _httpClient.PostAsJsonAsync("/validate", new { proposal }, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<ValidationResult>(cancellationToken: ct)
               ?? throw new InvalidOperationException("Empty response from /validate.");
    }
}
