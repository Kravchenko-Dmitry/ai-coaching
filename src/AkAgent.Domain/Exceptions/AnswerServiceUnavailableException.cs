namespace AkAgent.Domain.Exceptions;

/// Thrown when IAnswerService cannot produce an answer because the underlying
/// LLM response was unusable (empty, malformed, or failed to parse). Distinct
/// from a "not documented" / "not covered" result, which is a normal outcome.
public sealed class AnswerServiceUnavailableException : Exception
{
    public AnswerServiceUnavailableException(string message) : base(message)
    {
    }

    public AnswerServiceUnavailableException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
