using AkAgent.Infrastructure.Configuration;
using AkAgent.Infrastructure.Sources;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace AkAgent.Tests.Sources;

public class FileDropKnowledgeSourceTests
{
    private string _rootPath = null!;

    [SetUp]
    public void SetUp()
    {
        _rootPath = Path.Combine(Path.GetTempPath(), "ak-agent-tests", "filedrop-" + Guid.NewGuid());
        Directory.CreateDirectory(_rootPath);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_rootPath))
            Directory.Delete(_rootPath, recursive: true);
    }

    private FileDropKnowledgeSource CreateSource()
    {
        var options = Options.Create(new FileDropOptions { Path = _rootPath });
        return new FileDropKnowledgeSource(options, NullLogger<FileDropKnowledgeSource>.Instance);
    }

    private string WriteFile(string relativePath, string content, DateTime? lastWriteUtc = null)
    {
        var fullPath = Path.Combine(_rootPath, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllText(fullPath, content);
        if (lastWriteUtc is not null)
            File.SetLastWriteTimeUtc(fullPath, lastWriteUtc.Value);
        return fullPath;
    }

    [Test]
    public void Name_is_FileDrop()
    {
        Assert.That(CreateSource().Name, Is.EqualTo("FileDrop"));
    }

    [Test]
    public async Task GetChangesAsync_with_null_since_returns_all_markdown_files()
    {
        WriteFile("adr-001.md", "# Decision One\nBody one");
        WriteFile("adr-002.md", "# Decision Two\nBody two");

        var source = CreateSource();
        var docs = await source.GetChangesAsync(null, CancellationToken.None);

        Assert.That(docs.Select(d => d.Id), Is.EquivalentTo(["FileDrop:adr-001.md", "FileDrop:adr-002.md"]));
    }

    [Test]
    public async Task GetChangesAsync_ignores_non_markdown_files()
    {
        WriteFile("adr-001.md", "# Decision One\nBody");
        WriteFile("notes.txt", "not markdown");
        WriteFile("image.png", "binary-ish");

        var source = CreateSource();
        var docs = await source.GetChangesAsync(null, CancellationToken.None);

        Assert.That(docs, Has.Count.EqualTo(1));
        Assert.That(docs[0].Id, Is.EqualTo("FileDrop:adr-001.md"));
    }

    [Test]
    public async Task GetChangesAsync_includes_markdown_files_in_subdirectories()
    {
        WriteFile("guidelines/logging.md", "# Logging\nBody");

        var source = CreateSource();
        var docs = await source.GetChangesAsync(null, CancellationToken.None);

        Assert.That(docs, Has.Count.EqualTo(1));
        Assert.That(docs[0].Id, Is.EqualTo("FileDrop:guidelines/logging.md"));
    }

    [Test]
    public async Task GetChangesAsync_filters_by_since_using_LastWriteTimeUtc()
    {
        var cutoff = new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc);
        WriteFile("old.md", "# Old\nBody", cutoff.AddDays(-5));
        WriteFile("new.md", "# New\nBody", cutoff.AddDays(5));

        var source = CreateSource();
        var docs = await source.GetChangesAsync(new DateTimeOffset(cutoff), CancellationToken.None);

        Assert.That(docs, Has.Count.EqualTo(1));
        Assert.That(docs[0].Id, Is.EqualTo("FileDrop:new.md"));
    }

    [Test]
    public async Task GetChangesAsync_excludes_files_modified_exactly_at_since()
    {
        var cutoff = new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc);
        WriteFile("exact.md", "# Exact\nBody", cutoff);

        var source = CreateSource();
        var docs = await source.GetChangesAsync(new DateTimeOffset(cutoff), CancellationToken.None);

        Assert.That(docs, Is.Empty);
    }

    [Test]
    public async Task GetChangesAsync_returns_empty_when_directory_does_not_exist()
    {
        var options = Options.Create(new FileDropOptions
        {
            Path = Path.Combine(_rootPath, "does-not-exist")
        });
        var source = new FileDropKnowledgeSource(options, NullLogger<FileDropKnowledgeSource>.Instance);

        var docs = await source.GetChangesAsync(null, CancellationToken.None);

        Assert.That(docs, Is.Empty);
    }

    [Test]
    public async Task GetChangesAsync_extracts_title_from_first_level_one_heading()
    {
        WriteFile("adr-001.md", "intro line\n# Service Communication\nmore body");

        var source = CreateSource();
        var docs = await source.GetChangesAsync(null, CancellationToken.None);

        Assert.That(docs[0].Title, Is.EqualTo("Service Communication"));
    }

    [Test]
    public async Task GetChangesAsync_falls_back_to_filename_when_no_heading_present()
    {
        WriteFile("service-communication.md", "no heading here, just body text");

        var source = CreateSource();
        var docs = await source.GetChangesAsync(null, CancellationToken.None);

        Assert.That(docs[0].Title, Is.EqualTo("service communication"));
    }

    [Test]
    public async Task GetAllIdsAsync_returns_ids_for_every_markdown_file_regardless_of_age()
    {
        WriteFile("a.md", "# A", DateTime.UtcNow.AddYears(-2));
        WriteFile("b.md", "# B", DateTime.UtcNow);

        var source = CreateSource();
        var ids = await source.GetAllIdsAsync(CancellationToken.None);

        Assert.That(ids, Is.EquivalentTo(["FileDrop:a.md", "FileDrop:b.md"]));
    }

    [Test]
    public async Task GetAllIdsAsync_returns_empty_when_directory_does_not_exist()
    {
        var options = Options.Create(new FileDropOptions
        {
            Path = Path.Combine(_rootPath, "missing")
        });
        var source = new FileDropKnowledgeSource(options, NullLogger<FileDropKnowledgeSource>.Instance);

        var ids = await source.GetAllIdsAsync(CancellationToken.None);

        Assert.That(ids, Is.Empty);
    }

    [Test]
    public async Task GetDocumentAsync_returns_document_for_known_id()
    {
        WriteFile("adr-001.md", "# Decision One\nBody one");
        var source = CreateSource();

        var doc = await source.GetDocumentAsync("FileDrop:adr-001.md", CancellationToken.None);

        Assert.That(doc, Is.Not.Null);
        Assert.That(doc!.Title, Is.EqualTo("Decision One"));
        Assert.That(doc.RawContent, Does.Contain("Body one"));
    }

    [Test]
    public async Task GetDocumentAsync_returns_null_for_unknown_id()
    {
        var source = CreateSource();
        var doc = await source.GetDocumentAsync("FileDrop:missing.md", CancellationToken.None);
        Assert.That(doc, Is.Null);
    }

    [Test]
    public async Task GetDocumentAsync_returns_null_for_id_from_a_different_source()
    {
        WriteFile("adr-001.md", "# Decision One\nBody one");
        var source = CreateSource();

        var doc = await source.GetDocumentAsync("Confluence:adr-001.md", CancellationToken.None);

        Assert.That(doc, Is.Null);
    }

    [Test]
    public async Task HealthCheckAsync_is_healthy_when_directory_exists()
    {
        var source = CreateSource();
        var health = await source.HealthCheckAsync(CancellationToken.None);
        Assert.That(health.IsHealthy, Is.True);
    }

    [Test]
    public async Task HealthCheckAsync_is_unhealthy_when_directory_is_missing()
    {
        var options = Options.Create(new FileDropOptions
        {
            Path = Path.Combine(_rootPath, "missing")
        });
        var source = new FileDropKnowledgeSource(options, NullLogger<FileDropKnowledgeSource>.Instance);

        var health = await source.HealthCheckAsync(CancellationToken.None);

        Assert.That(health.IsHealthy, Is.False);
        Assert.That(health.Message, Is.Not.Null.And.Not.Empty);
    }
}
