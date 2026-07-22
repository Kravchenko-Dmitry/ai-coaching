using AkAgent.Domain.Interfaces;
using AkAgent.Domain.Models;
using AkAgent.Infrastructure.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AkAgent.Infrastructure.Sources;

public sealed class FileDropKnowledgeSource : IKnowledgeSource
{
    private const string SourceName = "FileDrop";

    private readonly string _rootPath;
    private readonly ILogger<FileDropKnowledgeSource> _logger;

    public string Name => SourceName;

    public FileDropKnowledgeSource(IOptions<FileDropOptions> options, ILogger<FileDropKnowledgeSource> logger)
    {
        _rootPath = Path.GetFullPath(options.Value.Path);
        _logger = logger;
    }

    public async Task<IReadOnlyList<SourceDocument>> GetChangesAsync(DateTimeOffset? since, CancellationToken ct)
    {
        if (!Directory.Exists(_rootPath))
            return [];

        var files = EnumerateMarkdownFiles()
            .Where(f => since is null || File.GetLastWriteTimeUtc(f) > since.Value.UtcDateTime)
            .ToList();

        var docs = new List<SourceDocument>(files.Count);
        foreach (var file in files)
        {
            ct.ThrowIfCancellationRequested();
            docs.Add(await ToSourceDocumentAsync(file, ct));
        }

        return docs;
    }

    public async Task<SourceDocument?> GetDocumentAsync(string id, CancellationToken ct)
    {
        var relativePath = TryGetRelativePath(id);
        if (relativePath is null)
            return null;

        var fullPath = Path.Combine(_rootPath, relativePath.Replace('/', Path.DirectorySeparatorChar));
        return File.Exists(fullPath) ? await ToSourceDocumentAsync(fullPath, ct) : null;
    }

    public Task<IReadOnlyList<string>> GetAllIdsAsync(CancellationToken ct)
    {
        if (!Directory.Exists(_rootPath))
            return Task.FromResult<IReadOnlyList<string>>([]);

        IReadOnlyList<string> ids = EnumerateMarkdownFiles()
            .Select(BuildId)
            .ToList();

        return Task.FromResult(ids);
    }

    public Task<HealthStatus> HealthCheckAsync(CancellationToken ct)
    {
        if (!Directory.Exists(_rootPath))
            return Task.FromResult(new HealthStatus(false, $"Directory not found: {_rootPath}"));

        try
        {
            _ = Directory.EnumerateFiles(_rootPath).Any();
            return Task.FromResult(new HealthStatus(true, null));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "FileDrop health check failed for {Path}", _rootPath);
            return Task.FromResult(new HealthStatus(false, ex.Message));
        }
    }

    private IEnumerable<string> EnumerateMarkdownFiles()
        => Directory.EnumerateFiles(_rootPath, "*.md", SearchOption.AllDirectories);

    private string BuildId(string fullPath)
    {
        var relative = Path.GetRelativePath(_rootPath, fullPath).Replace(Path.DirectorySeparatorChar, '/');
        return $"{SourceName}:{relative}";
    }

    private static string? TryGetRelativePath(string id)
    {
        const string prefix = $"{SourceName}:";
        return id.StartsWith(prefix, StringComparison.Ordinal) ? id[prefix.Length..] : null;
    }

    private async Task<SourceDocument> ToSourceDocumentAsync(string fullPath, CancellationToken ct)
    {
        var content = await File.ReadAllTextAsync(fullPath, ct);
        return new SourceDocument(
            Id: BuildId(fullPath),
            Title: ExtractTitle(content, fullPath),
            RawContent: content,
            LastModified: new DateTimeOffset(File.GetLastWriteTimeUtc(fullPath), TimeSpan.Zero),
            ContentType: "text/markdown");
    }

    private static string ExtractTitle(string content, string fullPath)
    {
        foreach (var rawLine in content.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r').TrimStart();
            if (line.StartsWith("# ", StringComparison.Ordinal))
                return line[2..].Trim();
        }

        return Path.GetFileNameWithoutExtension(fullPath).Replace('-', ' ').Replace('_', ' ');
    }
}
