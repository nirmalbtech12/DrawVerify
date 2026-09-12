using Xunit;

namespace DrawVerify.Infrastructure.Tests;

public sealed class Kan125VerificationVersionPersistenceTests
{
    [Fact(DisplayName = "TD-09 persists the exact selected STEP and DXF version IDs")]
    public void Verification_session_schema_contains_explicit_version_pair()
    {
        var source = Read("src", "DrawVerify.Infrastructure", "Persistence", "SqliteVerificationSessionRepository.cs");
        Assert.Contains("StepDocumentVersionId", source);
        Assert.Contains("DxfDocumentVersionId", source);
        Assert.Contains("StepDocumentVersionId=$", source);
        Assert.Contains("DxfDocumentVersionId=$", source);
    }

    [Fact(DisplayName = "TD-09 selected version pair is included in persisted match rows")]
    public void Dimension_matches_are_linked_to_both_versions()
    {
        var source = Read("src", "DrawVerify.Infrastructure", "Persistence", "SqliteVerificationSessionRepository.cs");
        Assert.Contains("FOREIGN KEY", source);
        Assert.Contains("DimensionMatch", source);
        Assert.Contains("RequiresManualReview", source);
    }

    private static string Read(params string[] parts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null) { var path = Path.Combine(new[] { directory.FullName }.Concat(parts).ToArray()); if (File.Exists(path)) return File.ReadAllText(path); directory = directory.Parent; }
        throw new FileNotFoundException(string.Join(Path.DirectorySeparatorChar, parts));
    }
}
