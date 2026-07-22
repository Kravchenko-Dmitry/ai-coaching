using System.Text;
using AkAgent.Domain.Enums;
using AkAgent.Domain.Models;

namespace AkAgent.Mcp.Formatting;

/// Text formatting for MCP tool output, per SPEC.md §4.6's exact output shapes.
public static class ResponseFormatter
{
    public static string FormatAskResponse(AgentAnswer answer)
    {
        var sb = new StringBuilder();
        sb.AppendLine(answer.Answer);

        AppendSources(sb, answer.Citations);

        sb.AppendLine();
        sb.Append($"Knowledge last synchronized: {answer.LastSyncAt:u}");
        return sb.ToString();
    }

    public static string FormatValidateResponse(ValidationResult result)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Decision: {FormatDecision(result.Decision)}");
        sb.AppendLine();
        sb.AppendLine(result.Explanation);

        AppendSources(sb, result.Citations);

        sb.AppendLine();
        sb.Append($"Knowledge last synchronized: {result.LastSyncAt:u}");
        return sb.ToString();
    }

    private static void AppendSources(StringBuilder sb, IReadOnlyList<Citation> citations)
    {
        if (citations.Count == 0)
            return;

        sb.AppendLine();
        sb.AppendLine("Sources:");
        foreach (var citation in citations)
            sb.AppendLine(FormatCitation(citation));
    }

    private static string FormatCitation(Citation citation)
    {
        var section = string.IsNullOrEmpty(citation.Section) ? "" : $" ({citation.Section})";
        return $"- {citation.Title}{section} — last modified {citation.LastModified:u}";
    }

    private static string FormatDecision(ValidationDecision decision) => decision switch
    {
        ValidationDecision.Aligned => "aligned",
        ValidationDecision.Warning => "warning",
        ValidationDecision.NotCovered => "not-covered",
        _ => decision.ToString().ToLowerInvariant()
    };
}
