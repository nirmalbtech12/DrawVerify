using Xunit;

namespace DrawVerify.Infrastructure.Tests;

public sealed class Kan125ProvenanceAuditPersistenceTests
{
    [Fact(DisplayName = "TD-12 migrates legacy provenance to uploaded compatibility value")]
    public void Document_repository_migrates_and_round_trips_source()
    {
        var source = Read("src", "DrawVerify.Infrastructure", "Persistence", "SqliteDocumentRepository.cs");
        Assert.Contains("DrawingSource", source);
        Assert.Contains("DEFAULT 1", source);
        Assert.Contains("DrawingSource.Uploaded", source);
        Assert.Contains("CreateWithVersionAndAuditAsync", source);
    }

    [Fact(DisplayName = "TD-12 records audit context without file contents")]
    public void Audit_repository_contract_keeps_operation_context()
    {
        var source = Read("src", "DrawVerify.Infrastructure", "Persistence", "SqliteAuditRepository.cs");
        Assert.Contains("DetailsJson", source);
        Assert.Contains("DocumentId", source);
        Assert.Contains("UserId", source);
        Assert.DoesNotContain("FileBytes", source);
    }

    private static string Read(params string[] parts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null) { var path = Path.Combine(new[] { directory.FullName }.Concat(parts).ToArray()); if (File.Exists(path)) return File.ReadAllText(path); directory = directory.Parent; }
        throw new FileNotFoundException(string.Join(Path.DirectorySeparatorChar, parts));
    }
}
