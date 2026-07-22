using AkAgent.Domain.Enums;

namespace AkAgent.Domain.Models;

public record DocumentSummary(string Id, string Title, DocumentType Type, DateTimeOffset LastModified);
