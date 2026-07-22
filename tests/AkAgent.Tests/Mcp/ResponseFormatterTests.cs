using AkAgent.Domain.Enums;
using AkAgent.Domain.Models;
using AkAgent.Mcp.Formatting;

namespace AkAgent.Tests.Mcp;

public class ResponseFormatterTests
{
    private static readonly DateTimeOffset LastModified = new(2026, 1, 15, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset LastSyncAt = new(2026, 7, 1, 12, 0, 0, TimeSpan.Zero);

    [Test]
    public void FormatAskResponse_includes_answer_sources_and_freshness()
    {
        var answer = new AgentAnswer(
            "We use REST for service-to-service communication.",
            [new Citation("FileDrop:adr-001.md", "Service Communication", "Overview", LastModified)],
            LastModified,
            LastSyncAt,
            IsDocumented: true);

        var text = ResponseFormatter.FormatAskResponse(answer);

        Assert.That(text, Does.StartWith("We use REST for service-to-service communication."));
        Assert.That(text, Does.Contain("Sources:"));
        Assert.That(text, Does.Contain("- Service Communication (Overview) — last modified"));
        Assert.That(text, Does.Contain("Knowledge last synchronized:"));
    }

    [Test]
    public void FormatAskResponse_omits_Sources_section_when_not_documented()
    {
        var answer = new AgentAnswer(
            "This is not documented in the architecture knowledge base.",
            [],
            OldestSourceModified: null,
            LastSyncAt,
            IsDocumented: false);

        var text = ResponseFormatter.FormatAskResponse(answer);

        Assert.That(text, Does.Contain("This is not documented in the architecture knowledge base."));
        Assert.That(text, Does.Not.Contain("Sources:"));
        Assert.That(text, Does.Contain("Knowledge last synchronized:"));
    }

    [TestCase(ValidationDecision.Aligned, "Decision: aligned")]
    [TestCase(ValidationDecision.Warning, "Decision: warning")]
    [TestCase(ValidationDecision.NotCovered, "Decision: not-covered")]
    public void FormatValidateResponse_formats_the_decision_line(ValidationDecision decision, string expectedLine)
    {
        var result = new ValidationResult(decision, "Some explanation.", [], LastSyncAt);

        var text = ResponseFormatter.FormatValidateResponse(result);

        Assert.That(text, Does.StartWith(expectedLine));
        Assert.That(text, Does.Contain("Some explanation."));
    }

    [Test]
    public void FormatValidateResponse_includes_sources_when_citations_present()
    {
        var result = new ValidationResult(
            ValidationDecision.Warning,
            "Conflicts with the documented standard.",
            [new Citation("FileDrop:adr-002.md", "Data Storage", null, LastModified)],
            LastSyncAt);

        var text = ResponseFormatter.FormatValidateResponse(result);

        Assert.That(text, Does.Contain("Sources:"));
        Assert.That(text, Does.Contain("- Data Storage — last modified"));
    }

    [Test]
    public void FormatValidateResponse_omits_Sources_section_when_NotCovered()
    {
        var result = new ValidationResult(
            ValidationDecision.NotCovered, "the architecture documentation does not cover this", [], LastSyncAt);

        var text = ResponseFormatter.FormatValidateResponse(result);

        Assert.That(text, Does.Not.Contain("Sources:"));
    }
}
