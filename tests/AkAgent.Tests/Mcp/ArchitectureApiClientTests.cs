using System.Net;
using System.Text;
using System.Text.Json;
using AkAgent.Domain.Models;
using AkAgent.Mcp;

namespace AkAgent.Tests.Mcp;

public class ArchitectureApiClientTests
{
    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _statusCode;
        private readonly string _content;

        public HttpRequestMessage? LastRequest { get; private set; }
        public string? LastRequestBody { get; private set; }

        public StubHttpMessageHandler(HttpStatusCode statusCode, string content)
        {
            _statusCode = statusCode;
            _content = content;
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            LastRequest = request;
            LastRequestBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(ct);
            return new HttpResponseMessage(_statusCode)
            {
                Content = new StringContent(_content, Encoding.UTF8, "application/json")
            };
        }
    }

    private static ArchitectureApiClient CreateClient(StubHttpMessageHandler handler)
    {
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5024") };
        return new ArchitectureApiClient(httpClient);
    }

    [Test]
    public async Task AskAsync_posts_the_question_and_deserializes_the_answer()
    {
        var json = """
            {
              "answer": "We use REST.",
              "citations": [{"documentId":"FileDrop:a.md","title":"A","section":"Overview","lastModified":"2026-01-01T00:00:00+00:00"}],
              "oldestSourceModified": "2026-01-01T00:00:00+00:00",
              "lastSyncAt": "2026-07-01T00:00:00+00:00",
              "isDocumented": true
            }
            """;
        var handler = new StubHttpMessageHandler(HttpStatusCode.OK, json);
        var client = CreateClient(handler);

        var answer = await client.AskAsync("how do services talk?", CancellationToken.None);

        Assert.That(answer.Answer, Is.EqualTo("We use REST."));
        Assert.That(answer.Citations, Has.Count.EqualTo(1));
        Assert.That(handler.LastRequest!.Method, Is.EqualTo(HttpMethod.Post));
        Assert.That(handler.LastRequest.RequestUri!.AbsolutePath, Is.EqualTo("/ask"));
        Assert.That(handler.LastRequestBody, Does.Contain("how do services talk?"));
    }

    [Test]
    public void AskAsync_throws_on_non_success_status_code()
    {
        var handler = new StubHttpMessageHandler(HttpStatusCode.ServiceUnavailable, "{}");
        var client = CreateClient(handler);

        Assert.ThrowsAsync<HttpRequestException>(() => client.AskAsync("question", CancellationToken.None));
    }

    [Test]
    public async Task ValidateAsync_posts_the_proposal_and_deserializes_the_result()
    {
        var json = """
            {
              "decision": 0,
              "explanation": "Matches the messaging pattern.",
              "citations": [],
              "lastSyncAt": "2026-07-01T00:00:00+00:00"
            }
            """;
        var handler = new StubHttpMessageHandler(HttpStatusCode.OK, json);
        var client = CreateClient(handler);

        var result = await client.ValidateAsync("use async messaging", CancellationToken.None);

        Assert.That(result.Explanation, Is.EqualTo("Matches the messaging pattern."));
        Assert.That(handler.LastRequest!.RequestUri!.AbsolutePath, Is.EqualTo("/validate"));
        Assert.That(handler.LastRequestBody, Does.Contain("use async messaging"));
    }

    [Test]
    public void ValidateAsync_throws_on_non_success_status_code()
    {
        var handler = new StubHttpMessageHandler(HttpStatusCode.ServiceUnavailable, "{}");
        var client = CreateClient(handler);

        Assert.ThrowsAsync<HttpRequestException>(() => client.ValidateAsync("proposal", CancellationToken.None));
    }
}
