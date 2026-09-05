using DrawVerify.Core.Models;

namespace DrawVerify.Core.Interfaces;

/// <summary>
/// Records auditable actions (who, what, when) for documents and workflows.
/// </summary>
public interface IAuditLogger
{
    Task WriteAsync(AuditEntry entry, CancellationToken cancellationToken = default) { Append(entry); return Task.CompletedTask; }
    void Append(AuditEntry entry) => WriteAsync(entry).GetAwaiter().GetResult();
}
