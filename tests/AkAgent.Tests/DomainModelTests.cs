using AkAgent.Domain.Enums;
using AkAgent.Domain.Models;

namespace AkAgent.Tests;

public class DomainModelTests
{
    [Test]
    public void KnowledgeDocument_HoldsSectionsAndExposesRequiredProperties()
    {
        var section = new DocumentSection("Overview", "Some content", 0);
        var doc = new KnowledgeDocument
        {
            Id = "filedrop:adr-007.md",
            SourceName = "FileDrop",
            Title = "ADR 007",
            Content = "# ADR 007\nSome content",
            ContentHash = "deadbeef",
            LastModified = DateTimeOffset.UtcNow,
            Type = DocumentType.Adr,
            Sections = new[] { section }
        };

        Assert.Multiple(() =>
        {
            Assert.That(doc.Id, Is.EqualTo("filedrop:adr-007.md"));
            Assert.That(doc.Type, Is.EqualTo(DocumentType.Adr));
            Assert.That(doc.Sections, Has.Count.EqualTo(1));
            Assert.That(doc.Sections[0].Heading, Is.EqualTo("Overview"));
        });
    }

    [Test]
    public void SyncState_DefaultsToEmptyHashMap()
    {
        var state = new SyncState
        {
            SourceName = "FileDrop",
            Status = SyncStatus.Never
        };

        Assert.That(state.DocumentHashes, Is.Empty);
    }
}
