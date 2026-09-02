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

/// <summary>
/// UI-level composition root. Wires concrete implementations from
/// Application and Infrastructure into the Core interfaces consumed by ViewModels.
/// </summary>
public static class CompositionRoot
{
    public static SessionAuthenticationService Authentication { get; } = new();
    public static StepUploadViewModel CreateStepUploadViewModel()
    {
        var config=LoadStepUploadConfiguration();
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
}
