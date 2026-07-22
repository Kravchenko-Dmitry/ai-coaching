namespace AkAgent.Domain.Models;

public record AgentAnswer(string Answer, IReadOnlyList<Citation> Citations,
                           DateTimeOffset? OldestSourceModified, DateTimeOffset LastSyncAt,
                           bool IsDocumented);

public record Citation(string DocumentId, string Title, string? Section, DateTimeOffset LastModified);
