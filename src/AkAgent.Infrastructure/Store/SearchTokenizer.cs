using System.Text.RegularExpressions;

namespace AkAgent.Infrastructure.Store;

internal static partial class SearchTokenizer
{
    private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "a", "an", "the", "is", "are", "was", "were", "be", "been", "being",
        "of", "in", "on", "at", "to", "for", "and", "or", "but", "with", "by",
        "as", "that", "this", "it", "from", "how", "what", "when", "where",
        "why", "who", "which", "does", "do", "did", "i", "you", "we", "they"
    };

    public static IReadOnlyList<string> Tokenize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return [];

        return TokenPattern().Matches(text.ToLowerInvariant())
            .Select(m => m.Value)
            .Where(t => !StopWords.Contains(t))
            .ToList();
    }

    [GeneratedRegex("[a-z0-9]+")]
    private static partial Regex TokenPattern();
}
