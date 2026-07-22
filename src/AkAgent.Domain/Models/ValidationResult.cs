using AkAgent.Domain.Enums;

namespace AkAgent.Domain.Models;

public record ValidationResult(ValidationDecision Decision,   // Aligned | Warning | NotCovered
                                string Explanation,
                                IReadOnlyList<Citation> Citations,
                                DateTimeOffset LastSyncAt);
