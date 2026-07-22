using AkAgent.Api.Endpoints;
using AkAgent.Api.Security;
using AkAgent.Api.Sync;
using AkAgent.Domain.Interfaces;
using AkAgent.Infrastructure.Configuration;
using AkAgent.Infrastructure.Llm;
using AkAgent.Infrastructure.Sources;
using AkAgent.Infrastructure.Store;
using AkAgent.Infrastructure.Sync;
using Anthropic;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddProblemDetails();

builder.Services.Configure<StoreOptions>(builder.Configuration.GetSection("Store"));
builder.Services.Configure<FileDropOptions>(builder.Configuration.GetSection("FileDrop"));
builder.Services.Configure<SyncOptions>(builder.Configuration.GetSection("Sync"));
builder.Services.Configure<LlmOptions>(builder.Configuration.GetSection("Llm"));
builder.Services.Configure<SecurityOptions>(builder.Configuration.GetSection("Security"));

builder.Services.AddSingleton<IKnowledgeStore, InMemoryKnowledgeStore>();
builder.Services.AddSingleton<IKnowledgeSource, FileDropKnowledgeSource>();
builder.Services.AddSingleton<ISyncEngine, SyncEngine>();

builder.Services.AddSingleton(_ => new AnthropicClient());
builder.Services.AddSingleton<IAnthropicMessageClient, AnthropicMessageClient>();
builder.Services.AddSingleton<IAnswerService, AnswerService>();

builder.Services.AddSingleton<SyncReadinessGate>();
builder.Services.AddHostedService<SyncBackgroundService>();

var app = builder.Build();

app.UseExceptionHandler();
app.UseMiddleware<ApiKeyMiddleware>();

app.MapApiEndpoints();

app.Run();

public partial class Program;
