using AkAgent.Infrastructure.Configuration;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace AkAgent.Api.Security;

/// Hook per SPEC.md §4.5: disabled (pass-through) when Security:ApiKey is empty,
/// otherwise requires a matching X-Api-Key header on every request.
public sealed class ApiKeyMiddleware
{
    private const string HeaderName = "X-Api-Key";

    private readonly RequestDelegate _next;

    public ApiKeyMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context, IOptions<SecurityOptions> options)
    {
        var configuredKey = options.Value.ApiKey;
        if (string.IsNullOrEmpty(configuredKey))
        {
            await _next(context);
            return;
        }

        if (!context.Request.Headers.TryGetValue(HeaderName, out var providedKey) || providedKey != configuredKey)
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            context.Response.ContentType = "application/problem+json";
            await context.Response.WriteAsJsonAsync(new ProblemDetails
            {
                Status = StatusCodes.Status401Unauthorized,
                Title = "Invalid or missing API key"
            });
            return;
        }

        await _next(context);
    }
}
