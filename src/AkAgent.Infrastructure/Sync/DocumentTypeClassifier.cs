using AkAgent.Domain.Enums;

namespace AkAgent.Infrastructure.Sync;

/// Heuristic classifier per SPEC.md §3: filename/first-heading conventions
/// (e.g. files starting with "adr-" map to DocumentType.Adr).
internal static class DocumentTypeClassifier
{
    public static DocumentType Infer(string sourceDocumentId, string title)
    {
        var fileName = ExtractFileNameWithoutExtension(sourceDocumentId);
        var normalizedTitle = title.ToLowerInvariant();

        if (StartsWithAny(fileName, "adr-", "adr_") || normalizedTitle.StartsWith("adr"))
            return DocumentType.Adr;

        if (StartsWithAny(fileName, "guideline-", "guideline_") || normalizedTitle.Contains("guideline"))
            return DocumentType.Guideline;

        if (StartsWithAny(fileName, "standard-", "standard_") || normalizedTitle.Contains("standard"))
            return DocumentType.Standard;

        if (StartsWithAny(fileName, "diagram-", "diagram_") || normalizedTitle.Contains("diagram"))
            return DocumentType.Diagram;

        return DocumentType.Other;
    }

    private static bool StartsWithAny(string value, params string[] prefixes)
        => prefixes.Any(p => value.StartsWith(p, StringComparison.Ordinal));

    private static string ExtractFileNameWithoutExtension(string sourceDocumentId)
    {
        var afterPrefix = sourceDocumentId.Contains(':')
            ? sourceDocumentId[(sourceDocumentId.IndexOf(':') + 1)..]
            : sourceDocumentId;

        var fileName = afterPrefix.Contains('/') ? afterPrefix[(afterPrefix.LastIndexOf('/') + 1)..] : afterPrefix;
        var dotIndex = fileName.LastIndexOf('.');
        return (dotIndex >= 0 ? fileName[..dotIndex] : fileName).ToLowerInvariant();
    }
}
