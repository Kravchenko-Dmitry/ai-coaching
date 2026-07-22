using AkAgent.Domain.Exceptions;
using AkAgent.Domain.Interfaces;
using AkAgent.Domain.Models;

namespace AkAgent.IntegrationTests;

/// Simulates an unusable LLM response, to verify the /ask and /validate
/// endpoints map this to 503 rather than a generic 500.
public sealed class ThrowingAnswerService : IAnswerService
{
    public Task<AgentAnswer> AskAsync(string question, CancellationToken ct)
        => throw new AnswerServiceUnavailableException("simulated LLM failure");

    public Task<ValidationResult> ValidateAsync(string proposal, CancellationToken ct)
        => throw new AnswerServiceUnavailableException("simulated LLM failure");
}
