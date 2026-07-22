using AkAgent.Api.Sync;
using AkAgent.Domain.Enums;
using AkAgent.Domain.Exceptions;
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
        catch (Exception ex) when (ex is AnthropicException or AnswerServiceUnavailableException)
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
        catch (Exception ex) when (ex is AnthropicException or AnswerServiceUnavailableException)
        {
            return Results.Problem(statusCode: StatusCodes.Status503ServiceUnavailable, title: "LLM unavailable");
        }
    }

    private static async Task<IResult> SyncAsync(
        ISyncEngine syncEngine, IKnowledgeStore store, IEnumerable<IKnowledgeSource> sources, CancellationToken ct)
    {
        var result = await syncEngine.RunSyncAsync(ct);
        if (result.Started)
            return Results.Ok(result.Report);

        var runningState = await BuildSourceStatusesAsync(store, sources, ct);
        return Results.Problem(
            statusCode: StatusCodes.Status409Conflict,
            title: "sync already running",
            extensions: new Dictionary<string, object?> { ["runningState"] = runningState });
    }

    private static async Task<IResult> StatusAsync(
        IKnowledgeStore store, IEnumerable<IKnowledgeSource> sources, SyncReadinessGate readiness, CancellationToken ct)
    {
        if (!readiness.IsReady)
            return Results.Problem(statusCode: StatusCodes.Status503ServiceUnavailable, title: "knowledge base warming up");

        return Results.Ok(await BuildSourceStatusesAsync(store, sources, ct));
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

    private static async Task<List<SourceStatus>> BuildSourceStatusesAsync(
        IKnowledgeStore store, IEnumerable<IKnowledgeSource> sources, CancellationToken ct)
    {
        var statuses = new List<SourceStatus>();
        foreach (var source in sources)
        {
            var state = await store.GetSyncStateAsync(source.Name, ct);
            var summary = new SyncStateSummary(state?.LastSyncAt, state?.Status ?? SyncStatus.Never, state?.LastError);
            var health = await source.HealthCheckAsync(ct);
            statuses.Add(new SourceStatus(source.Name, summary, state?.DocumentHashes.Count ?? 0, health));
        }

        return statuses;
    }
}

public sealed record AskRequest(string Question);

public sealed record ValidateRequest(string Proposal);
