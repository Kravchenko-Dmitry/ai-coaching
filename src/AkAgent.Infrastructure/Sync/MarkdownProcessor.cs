using System.Text.RegularExpressions;
using AkAgent.Domain.Models;

namespace AkAgent.Infrastructure.Sync;

internal static partial class MarkdownProcessor
{
    public static string Normalize(string raw)
    {
        var unifiedLineEndings = raw.Replace("\r\n", "\n").Replace("\r", "\n");
        var lines = unifiedLineEndings.Split('\n').Select(l => l.TrimEnd());
        return string.Join('\n', lines).Trim('\n');
    }

    /// Splits normalized content into sections at headings level 1-3 (# .. ###).
    /// Leading content before the first heading becomes a section with an empty Heading.
    public static IReadOnlyList<DocumentSection> SplitIntoSections(string normalizedContent)
    {
        var sections = new List<DocumentSection>();
        string? currentHeading = null;
        var currentBody = new List<string>();
        var order = 0;

        void FlushSection()
        {
            var body = string.Join('\n', currentBody).Trim();
            if (currentHeading is null && body.Length == 0)
            {
                currentBody.Clear();
                return;
            }

            sections.Add(new DocumentSection(currentHeading ?? string.Empty, body, order++));
            currentBody.Clear();
        }

        foreach (var line in normalizedContent.Split('\n'))
        {
            var match = HeadingPattern().Match(line);
            if (match.Success)
            {
                FlushSection();
                currentHeading = match.Groups["text"].Value.Trim();
            }
            else
            {
                currentBody.Add(line);
            }
        }

        FlushSection();
        return sections;
    }

    [GeneratedRegex(@"^(#{1,3})\s+(?<text>.+)$")]
    private static partial Regex HeadingPattern();
}
