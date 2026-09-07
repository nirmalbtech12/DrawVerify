namespace DrawVerify.Core.Configuration;

public sealed class AuditLogConfiguration
{
    public int RetentionDays { get; init; }
    public void Validate()
    {
        if (RetentionDays < 0) throw new InvalidOperationException("Audit log retention days cannot be negative.");
    }
}
