using AkAgent.Domain.Enums;

namespace AkAgent.Domain.Models;

public record KnowledgeDocument
{
    public required string Id { get; init; }            // stable per source, e.g. "filedrop:adr-007.md"
    public required string SourceName { get; init; }    // connector instance name, e.g. "FileDrop"
    public required string Title { get; init; }
    public required string Content { get; init; }       // normalized markdown
    public required string ContentHash { get; init; }   // SHA-256 of Content
    public required DateTimeOffset LastModified { get; init; }  // from the source system
    public DocumentType Type { get; init; }             // Adr | Guideline | Standard | Diagram | Other
    public IReadOnlyList<DocumentSection> Sections { get; init; } = Array.Empty<DocumentSection>(); // split by markdown headings
}

public record DocumentSection(string Heading, string Content, int Order);
