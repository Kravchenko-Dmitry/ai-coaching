using System.Net;
using System.Net.Http.Json;
using AkAgent.Domain.Enums;
using AkAgent.Domain.Interfaces;
using AkAgent.Domain.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace AkAgent.IntegrationTests;

public class ApiIntegrationTests
{
    private ApiWebApplicationFactory _factory = null!;
    private HttpClient _client = null!;

    [SetUp]
    public void SetUp()
    {
        _factory = new ApiWebApplicationFactory();
        _client = _factory.CreateClient();
    }

    [TearDown]
    public void TearDown()
    {
        _client.Dispose();
        _factory.Dispose();
    }

    private async Task WaitForReadyAsync()
    {
        for (var i = 0; i < 100; i++)
        {
            var response = await _client.GetAsync("/status");
            if (response.StatusCode == HttpStatusCode.OK)
                return;
            await Task.Delay(100);
        }

        Assert.Fail("Service did not become ready in time.");
    }

    [Test]
    public async Task FullSync_from_fixture_folder_then_Ask_returns_a_cited_answer()
    {
        await WaitForReadyAsync();

        var response = await _client.PostAsJsonAsync("/ask", new AskRequestBody("How do services communicate with each other?"));

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var answer = await response.Content.ReadFromJsonAsync<AgentAnswer>();
        Assert.That(answer, Is.Not.Null);
        Assert.That(answer!.IsDocumented, Is.True);
        Assert.That(answer.Citations, Is.Not.Empty);
        Assert.That(answer.Citations[0].Title, Is.EqualTo("ADR-001: Use REST for Service Communication"));
    }

    [Test]
    public async Task ModifyingAFile_then_Sync_makes_subsequent_Ask_reflect_the_change()
    {
        await WaitForReadyAsync();

        var before = await AskAsync("communication");
        Assert.That(before.Answer, Does.Not.Contain("gRPC"));

        var filePath = Path.Combine(_factory.KnowledgeDocsPath, "adr-001.md");
        var updatedContent = await File.ReadAllTextAsync(filePath) +
                              "\n\n## Update\n\nWe are migrating to gRPC for internal service-to-service calls.\n";
        await File.WriteAllTextAsync(filePath, updatedContent);
        File.SetLastWriteTimeUtc(filePath, DateTime.UtcNow.AddSeconds(5));

        var syncResponse = await _client.PostAsync("/sync", content: null);
        Assert.That(syncResponse.StatusCode, Is.EqualTo(HttpStatusCode.OK));

        var after = await AskAsync("gRPC migration");
        Assert.That(after.Answer, Does.Contain("gRPC"));
    }

    [Test]
    public async Task Ask_returns_400_problem_details_when_question_is_empty()
    {
        await WaitForReadyAsync();

        var response = await _client.PostAsJsonAsync("/ask", new AskRequestBody(""));

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        Assert.That(response.Content.Headers.ContentType?.MediaType, Is.EqualTo("application/problem+json"));
    }

    [Test]
    public async Task Documents_unknown_id_returns_404()
    {
        await WaitForReadyAsync();

        var response = await _client.GetAsync("/documents/FileDrop:does-not-exist.md");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task Documents_lists_the_synced_document()
    {
        await WaitForReadyAsync();

        var response = await _client.GetAsync("/documents");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var docs = await response.Content.ReadFromJsonAsync<List<DocumentSummary>>();
        Assert.That(docs, Is.Not.Null);
        Assert.That(docs!.Select(d => d.Id), Has.Some.Contains("adr-001.md"));
    }

    [Test]
    public async Task Status_reports_a_per_source_summary_once_ready()
    {
        await WaitForReadyAsync();

        var response = await _client.GetAsync("/status");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var statuses = await response.Content.ReadFromJsonAsync<List<SourceStatus>>();
        Assert.That(statuses, Is.Not.Null);
        var fileDrop = statuses!.Single(s => s.SourceName == "FileDrop");
        Assert.That(fileDrop.DocumentCount, Is.GreaterThan(0));
        Assert.That(fileDrop.Health.IsHealthy, Is.True);
        Assert.That(fileDrop.SyncState.Status, Is.EqualTo(SyncStatus.Ok));
    }

    [Test]
    public async Task Validate_returns_NotCovered_for_a_proposal_unrelated_to_any_document()
    {
        await WaitForReadyAsync();

        var response = await _client.PostAsJsonAsync("/validate", new ValidateRequestBody("zzz completely unrelated nonsense topic query"));

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        var result = await response.Content.ReadFromJsonAsync<ValidationResult>();
        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Decision, Is.EqualTo(ValidationDecision.NotCovered));
    }

    [Test]
    public async Task Unmatched_route_returns_problem_details()
    {
        await WaitForReadyAsync();

        var response = await _client.GetAsync("/this-route-does-not-exist");

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        Assert.That(response.Content.Headers.ContentType?.MediaType, Is.EqualTo("application/problem+json"));
    }

    [Test]
    public async Task Ask_returns_503_when_the_answer_service_is_unavailable()
    {
        using var factory = new ApiWebApplicationFactory(services =>
        {
            services.RemoveAll<IAnswerService>();
            services.AddSingleton<IAnswerService, ThrowingAnswerService>();
        });
        using var client = factory.CreateClient();

        for (var i = 0; i < 100; i++)
        {
            var probe = await client.GetAsync("/status");
            if (probe.StatusCode == HttpStatusCode.OK)
                break;
            await Task.Delay(100);
        }

        var response = await client.PostAsJsonAsync("/ask", new AskRequestBody("anything"));

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.ServiceUnavailable));
        Assert.That(response.Content.Headers.ContentType?.MediaType, Is.EqualTo("application/problem+json"));
    }

    [Test]
    public async Task Validate_returns_503_when_the_answer_service_is_unavailable()
    {
        using var factory = new ApiWebApplicationFactory(services =>
        {
            services.RemoveAll<IAnswerService>();
            services.AddSingleton<IAnswerService, ThrowingAnswerService>();
        });
        using var client = factory.CreateClient();

        for (var i = 0; i < 100; i++)
        {
            var probe = await client.GetAsync("/status");
            if (probe.StatusCode == HttpStatusCode.OK)
                break;
            await Task.Delay(100);
        }

        var response = await client.PostAsJsonAsync("/validate", new ValidateRequestBody("anything"));

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.ServiceUnavailable));
        Assert.That(response.Content.Headers.ContentType?.MediaType, Is.EqualTo("application/problem+json"));
    }

    private async Task<AgentAnswer> AskAsync(string question)
    {
        var response = await _client.PostAsJsonAsync("/ask", new AskRequestBody(question));
        response.EnsureSuccessStatusCode();
        var answer = await response.Content.ReadFromJsonAsync<AgentAnswer>();
        Assert.That(answer, Is.Not.Null);
        return answer!;
    }

    private sealed record AskRequestBody(string Question);
    private sealed record ValidateRequestBody(string Proposal);
}
