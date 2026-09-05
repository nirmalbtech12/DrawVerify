using System.IO;
using System.Text.Json;
using DrawVerify.Application.Services;
using DrawVerify.Core.Configuration;
using DrawVerify.Core.Interfaces;
using DrawVerify.Infrastructure.FileStorage;
using DrawVerify.Infrastructure.Persistence;
using DrawVerify.Infrastructure.Logging;
using DrawVerify.App.ViewModels;

namespace DrawVerify.App.Services;

/// <summary>
/// UI-level composition root. Wires concrete implementations from
/// Application and Infrastructure into the Core interfaces consumed by ViewModels.
/// </summary>
public static class CompositionRoot
{
    private static readonly StepUploadConfiguration StepUploadConfiguration = LoadStepUploadConfiguration();
    public static void Initialize() { }
    public static StepUploadViewModel CreateStepUploadViewModel()
    {
        var config=StepUploadConfiguration;
        var database=Path.Combine(AppContext.BaseDirectory,"drawverify.db");
        var logger = new CentralizedLogger();
        return new StepUploadViewModel(new DocumentService(new SqliteDocumentRepository(database, logger),new SqliteAuditRepository(database),new DocumentFileStore(AppContext.BaseDirectory,config),config,logger));
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
