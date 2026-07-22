namespace AkAgent.Domain.Models;

public record SourceDocument(string Id, string Title, string RawContent,
                              DateTimeOffset LastModified, string? ContentType);
