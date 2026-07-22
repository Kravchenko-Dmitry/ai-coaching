using AkAgent.Api.Sync;
using AkAgent.Domain.Enums;
using AkAgent.Domain.Interfaces;
using AkAgent.Domain.Models;
using Anthropic.Exceptions;

namespace AkAgent.Api.Endpoints;

public static class ApiEndpoints
{
    public static WebApplication MapApiEndpoints(this WebApplication app)
    {
        app.MapPost("/ask", AskAsync);
        app.MapPost("/validate", ValidateAsync);
        app.MapPost("/sync", SyncAsync);
        app.MapGet("/status", StatusAsync);
        app.MapGet("/documents", DocumentsAsync);
        app.MapGet("/documents/{*id}", DocumentByIdAsync);

        return app;
    }

    private static async Task<IResult> AskAsync(
        AskRequest request, IAnswerService answerService, SyncReadinessGate readiness, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Question))
            return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "question must not be empty");

        if (!readiness.IsReady)
            return Results.Problem(statusCode: StatusCodes.Status503ServiceUnavailable, title: "knowledge base initializing");

        try
        {
            var answer = await answerService.AskAsync(request.Question, ct);
            return Results.Ok(answer);
        }
        catch (AnthropicApiException)
        {
            return Results.Problem(statusCode: StatusCodes.Status503ServiceUnavailable, title: "LLM unavailable");
        }
    }

    private static async Task<IResult> ValidateAsync(
        ValidateRequest request, IAnswerService answerService, SyncReadinessGate readiness, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Proposal))
            return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "proposal must not be empty");

        if (!readiness.IsReady)
            return Results.Problem(statusCode: StatusCodes.Status503ServiceUnavailable, title: "knowledge base initializing");

        try
        {
            var result = await answerService.ValidateAsync(request.Proposal, ct);
            return Results.Ok(result);
        }
        catch (AnthropicApiException)
        {
            return Results.Problem(statusCode: StatusCodes.Status503ServiceUnavailable, title: "LLM unavailable");
        }
    }

    private static async Task<IResult> SyncAsync(ISyncEngine syncEngine, CancellationToken ct)
    {
        var result = await syncEngine.RunSyncAsync(ct);
        return result.Started
            ? Results.Ok(result.Report)
            : Results.Problem(statusCode: StatusCodes.Status409Conflict, title: "sync already running");
    }

    private static async Task<IResult> StatusAsync(
        IKnowledgeStore store, IEnumerable<IKnowledgeSource> sources, SyncReadinessGate readiness, CancellationToken ct)
    {
        if (!readiness.IsReady)
            return Results.Problem(statusCode: StatusCodes.Status503ServiceUnavailable, title: "knowledge base warming up");

        var statuses = new List<SourceStatus>();
        foreach (var source in sources)
        {
            var state = await store.GetSyncStateAsync(source.Name, ct)
                        ?? new SyncState { SourceName = source.Name, Status = SyncStatus.Never };
            var health = await source.HealthCheckAsync(ct);
            statuses.Add(new SourceStatus(source.Name, state, state.DocumentHashes.Count, health));
        }

        return Results.Ok(statuses);
    }

    private static async Task<IResult> DocumentsAsync(IKnowledgeStore store, CancellationToken ct)
        => Results.Ok(await store.ListAsync(ct));

    private static async Task<IResult> DocumentByIdAsync(string id, IKnowledgeStore store, CancellationToken ct)
    {
        var document = await store.GetAsync(id, ct);
        return document is not null
            ? Results.Ok(document)
            : Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "document not found");
    }
}

public sealed record AskRequest(string Question);

public sealed record ValidateRequest(string Proposal);
