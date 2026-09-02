using DrawVerify.Core.Models;
<<<<<<< HEAD
=======

>>>>>>> 642060c (KAN-19: implementation)
namespace DrawVerify.Core.Interfaces;

/// <summary>
/// Records auditable actions (who, what, when) for documents and workflows.
/// </summary>
public interface IAuditLogger
{
<<<<<<< HEAD
    void Append(AuditEntry entry);
=======
    Task WriteAsync(AuditEntry entry, CancellationToken cancellationToken = default);
>>>>>>> 642060c (KAN-19: implementation)
}
