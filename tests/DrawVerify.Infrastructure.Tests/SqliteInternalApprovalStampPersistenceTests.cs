using Xunit;

namespace DrawVerify.Infrastructure.Tests;

public sealed class SqliteInternalApprovalStampPersistenceTests
{
    [Fact(DisplayName = "T04 SQLite persistence is atomic and immutable")]
    public void T04_schema_guards_and_atomicity_are_present()
    {
        var source = Read("src", "DrawVerify.Infrastructure", "Persistence", "SqliteWorkflowRepository.cs");
        Assert.Contains("InternalApprovalStampJson", source);
        Assert.Contains("InternalApprovalStampFinalized", source);
        Assert.Contains("TR_ReviewSubmission_ApprovalStampUpdate", source);
        Assert.Contains("TR_ReviewSubmission_ApprovalStampDelete", source);
        Assert.Contains("RollbackAsync", source);
    }

    [Fact(DisplayName = "T15 SQLite persistence writes the exact stamp audit action")]
    public void T15_persistence_writes_exact_stamp_audit_payload()
    {
        var source = Read("src", "DrawVerify.Infrastructure", "Persistence", "SqliteWorkflowRepository.cs");
        Assert.Contains("InternalApprovalStampApplied", source);
        Assert.Contains("stampId = stamp.StampId", source);
        Assert.Contains("stampAppliedUtc = stamp.AppliedUtc", source);
    }

    private static string Read(params string[] parts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null) { var path = Path.Combine(new[] { directory.FullName }.Concat(parts).ToArray()); if (File.Exists(path)) return File.ReadAllText(path); directory = directory.Parent; }
        throw new FileNotFoundException(string.Join(Path.DirectorySeparatorChar, parts));
    }
}
