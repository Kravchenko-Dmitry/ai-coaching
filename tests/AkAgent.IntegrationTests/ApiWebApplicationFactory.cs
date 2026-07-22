using AkAgent.Domain.Interfaces;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace AkAgent.IntegrationTests;

public sealed class ApiWebApplicationFactory : WebApplicationFactory<Program>
{
    public string KnowledgeDocsPath { get; }
    public string DataPath { get; }

    private readonly string _root;

    public ApiWebApplicationFactory()
    {
        _root = Path.Combine(Path.GetTempPath(), "ak-agent-integration-tests", Guid.NewGuid().ToString());
        KnowledgeDocsPath = Path.Combine(_root, "knowledge-docs");
        DataPath = Path.Combine(_root, "data");
        Directory.CreateDirectory(KnowledgeDocsPath);
        Directory.CreateDirectory(DataPath);

        var fixturesPath = FindFixturesPath();
        foreach (var file in Directory.EnumerateFiles(fixturesPath, "*.md"))
            File.Copy(file, Path.Combine(KnowledgeDocsPath, Path.GetFileName(file)));
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("FileDrop:Path", KnowledgeDocsPath);
        builder.UseSetting("Store:DataPath", DataPath);
        builder.UseSetting("Sync:IntervalMinutes", "60");

        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IAnswerService>();
            services.AddSingleton<IAnswerService, FakeAnswerService>();
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            try
            {
                Directory.Delete(_root, recursive: true);
            }
            catch (IOException)
            {
                // best-effort cleanup
            }
        }
    }

    private static string FindFixturesPath()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "fixtures", "knowledge-docs");
            if (Directory.Exists(candidate))
                return candidate;
            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate tests/fixtures/knowledge-docs from the test output directory.");
    }
}
