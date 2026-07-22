using AkAgent.Domain.Models;

namespace AkAgent.Domain.Interfaces;

public interface IAnswerService
{
    Task<AgentAnswer> AskAsync(string question, CancellationToken ct);
    Task<ValidationResult> ValidateAsync(string proposal, CancellationToken ct);
}
