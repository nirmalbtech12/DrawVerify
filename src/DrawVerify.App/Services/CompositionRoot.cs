using System.IO;
using DrawVerify.Application.Services;
using DrawVerify.Core.Configuration;
using DrawVerify.Core.Interfaces;
using DrawVerify.Core.Models;
using DrawVerify.Infrastructure.FileStorage;
using DrawVerify.Infrastructure.Persistence;
using DrawVerify.App.ViewModels;

namespace DrawVerify.App.Services;

/// <summary>
/// UI-level composition root. Wires concrete implementations from
/// Application and Infrastructure into the Core interfaces consumed by ViewModels.
/// </summary>
public static class CompositionRoot
{
    public static StepUploadViewModel CreateStepUploadViewModel()
    {
        var config=new StepUploadConfiguration();
        var auth=new DesktopAuthenticationService();
        var database=Path.Combine(AppContext.BaseDirectory,"drawverify.db");
        return new StepUploadViewModel(new DocumentService(new SqliteDocumentRepository(database),new SqliteAuditRepository(database),auth,new DocumentFileStore(AppContext.BaseDirectory,config),config));
    }
    private sealed class DesktopAuthenticationService:IAuthenticationService
    { public User? CurrentUser { get; }=new() { UserId="local-maker",DisplayName="Local Maker",Role=Role.Maker }; public bool IsAuthenticated=>CurrentUser is not null; public bool IsMaker=>CurrentUser?.IsMaker==true; }
}
