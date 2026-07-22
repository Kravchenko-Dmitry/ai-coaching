namespace AkAgent.Domain.Models;

public record SearchHit(KnowledgeDocument Document, DocumentSection? BestSection, double Score);
