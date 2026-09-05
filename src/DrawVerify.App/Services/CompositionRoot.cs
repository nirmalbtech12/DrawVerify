namespace DrawVerify.App.Services;
using DrawVerify.Application.Services;
using DrawVerify.Core.Interfaces;
using DrawVerify.Infrastructure.Logging;
using DrawVerify.Infrastructure.Persistence;

/// <summary>
/// UI-level composition root. Wires concrete implementations from
/// Application and Infrastructure into the Core interfaces consumed by ViewModels.
/// </summary>
public static class CompositionRoot
{
    public sealed record Services(IDocumentRepository Documents, IAuditLogger Audit, IAuthenticationService Authentication, MeasurementExtractionService Measurements);

    public static Services Create(string databasePath, string workerPath, IAuthenticationService authentication)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(workerPath);
        ArgumentNullException.ThrowIfNull(authentication);
        var documents = new SqliteDocumentRepository(databasePath);
        var audit = new SqliteAuditRepository(databasePath);
        var geometry = new OcctWorkerGeometryService(workerPath);
        return new Services(documents, audit, authentication, new MeasurementExtractionService(documents, audit, authentication, geometry));
    }
}
