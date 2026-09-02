using System.IO;
using DrawVerify.Application.Services;
using DrawVerify.Core.Configuration;
using DrawVerify.Core.Interfaces;
using DrawVerify.Core.Models;
using DrawVerify.Infrastructure.FileStorage;
using DrawVerify.Infrastructure.Persistence;
using DrawVerify.App.ViewModels;

namespace DrawVerify.App.Services;
using DrawVerify.Application.Services;
using DrawVerify.Core.Interfaces;
using DrawVerify.Infrastructure.Logging;
using DrawVerify.Infrastructure.Persistence;
using System.Text.Json;
using System.IO;

/// <summary>
/// UI-level composition root. Wires concrete implementations from
/// Application and Infrastructure into the Core interfaces consumed by ViewModels.
/// </summary>
public static class CompositionRoot
{
<<<<<<< HEAD
    public sealed record Services(IDocumentRepository Documents, IAuditLogger Audit, IAuthenticationService Authentication, MeasurementExtractionService Measurements);

    public static Services Create(string databasePath, string workerPath, IAuthenticationService authentication, string configurationPath = "appsettings.json")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(workerPath);
        ArgumentNullException.ThrowIfNull(authentication);
        var settings = LoadMeasurementSettings(configurationPath);
        var documents = new SqliteDocumentRepository(databasePath);
        var audit = new SqliteAuditRepository(databasePath);
        var geometry = new OcctWorkerGeometryService(workerPath, settings.MaximumFileSizeBytes, settings.TimeoutSeconds);
        return new Services(documents, audit, authentication, new MeasurementExtractionService(documents, audit, authentication, geometry));
    }

    private static MeasurementSettings LoadMeasurementSettings(string path)
    {
        if (!File.Exists(path)) throw new InvalidOperationException($"Measurement configuration file was not found: {path}");
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            if (!document.RootElement.TryGetProperty("MeasurementExtraction", out var section)) throw new InvalidOperationException("MeasurementExtraction configuration section is missing.");
            if (!section.TryGetProperty("MaximumFileSizeBytes", out var size) || !size.TryGetInt64(out var maximumFileSizeBytes) || maximumFileSizeBytes <= 0) throw new InvalidOperationException("MeasurementExtraction.MaximumFileSizeBytes must be a positive integer.");
            if (!section.TryGetProperty("TimeoutSeconds", out var timeout) || !timeout.TryGetInt32(out var timeoutSeconds) || timeoutSeconds <= 0) throw new InvalidOperationException("MeasurementExtraction.TimeoutSeconds must be a positive integer.");
            return new MeasurementSettings(maximumFileSizeBytes, timeoutSeconds);
        }
        catch (JsonException ex) { throw new InvalidOperationException("MeasurementExtraction configuration is invalid JSON.", ex); }
    }

    private sealed record MeasurementSettings(long MaximumFileSizeBytes, int TimeoutSeconds);
=======
    public static StepUploadViewModel CreateStepUploadViewModel()
    {
        var config=new StepUploadConfiguration();
        var auth=new DesktopAuthenticationService();
        var database=Path.Combine(AppContext.BaseDirectory,"drawverify.db");
        return new StepUploadViewModel(new DocumentService(new SqliteDocumentRepository(database),new SqliteAuditRepository(database),auth,new DocumentFileStore(AppContext.BaseDirectory,config),config));
    }
    private sealed class DesktopAuthenticationService:IAuthenticationService
    { public User? CurrentUser { get; }=new() { UserId="local-maker",DisplayName="Local Maker",Role=Role.Maker }; public bool IsAuthenticated=>CurrentUser is not null; public bool IsMaker=>CurrentUser?.IsMaker==true; }
>>>>>>> 642060c (KAN-19: implementation)
}
