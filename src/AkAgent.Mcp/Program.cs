using AkAgent.Mcp;
using AkAgent.Mcp.Configuration;
using AkAgent.Mcp.Tools;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

var builder = Host.CreateApplicationBuilder(args);

// stdio carries the MCP protocol; all logging must go to stderr, never stdout.
builder.Logging.AddConsole(options => options.LogToStandardErrorThreshold = LogLevel.Trace);

builder.Services.Configure<ApiClientOptions>(builder.Configuration.GetSection("Api"));

builder.Services.AddHttpClient<ArchitectureApiClient>((serviceProvider, client) =>
{
    var options = serviceProvider.GetRequiredService<IOptions<ApiClientOptions>>().Value;
    client.BaseAddress = new Uri(options.BaseUrl);
});

builder.Services
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithTools<ArchitectureKnowledgeTools>();

await builder.Build().RunAsync();
