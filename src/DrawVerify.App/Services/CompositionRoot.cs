using System.IO;
using System.Text.Json;
using DrawVerify.Application.Services;
using DrawVerify.Core.Configuration;
using DrawVerify.Core.Interfaces;
using DrawVerify.Core.Models;
using DrawVerify.Infrastructure.FileStorage;
using DrawVerify.Infrastructure.Persistence;
using DrawVerify.Infrastructure.Logging;
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
    private static readonly StepUploadConfiguration StepUploadConfiguration = LoadStepUploadConfiguration();
    public static SessionAuthenticationService Authentication { get; } = new();
    public static void Initialize() { }
    public static StepUploadViewModel CreateStepUploadViewModel()
    {
        var config=StepUploadConfiguration;
        var database=Path.Combine(AppContext.BaseDirectory,"drawverify.db");
        return new StepUploadViewModel(new DocumentService(new SqliteDocumentRepository(database),new SqliteAuditRepository(database),Authentication,new DocumentFileStore(AppContext.BaseDirectory,config),config,new CentralizedLogger()));
    }
    private static StepUploadConfiguration LoadStepUploadConfiguration()
    {
        var logger=new CentralizedLogger();
        try
        {
            using var document=JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"appsettings.json")));
            var section=document.RootElement.GetProperty("StepUpload");
            var config=new StepUploadConfiguration { MaxFileSizeMb=section.GetProperty("MaxFileSizeMb").GetInt32(), StorageFolder=section.GetProperty("StorageFolder").GetString() ?? string.Empty, DuplicatePolicy=section.GetProperty("DuplicatePolicy").GetString() ?? string.Empty };
            config.Validate();
            return config;
        }
        catch(Exception exception)
        {
            logger.Log(exception,"STEP configuration startup","Unable to load or validate StepUpload settings");
            throw;
        }
    }
}

public sealed class SessionAuthenticationService : IAuthenticationService
{
    public User? CurrentUser { get; private set; }
    public bool IsAuthenticated => CurrentUser is not null;
    public bool IsMaker => CurrentUser?.IsMaker == true;
    public void SignIn(User user) => CurrentUser = user;
    public void SignOut() => CurrentUser = null;
    private sealed class DesktopAuthenticationService:IAuthenticationService
    { public User? CurrentUser { get; }=new() { UserId="local-maker",DisplayName="Local Maker",Role=Role.Maker }; public bool IsAuthenticated=>CurrentUser is not null; public bool IsMaker=>CurrentUser?.IsMaker==true; }
>>>>>>> 642060c (KAN-19: implementation)
}
