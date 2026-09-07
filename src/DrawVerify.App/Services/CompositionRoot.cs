using System.IO;
using System.Text.Json;
using DrawVerify.Application.Services;
using DrawVerify.Core.Configuration;
using DrawVerify.Core.Interfaces;
using DrawVerify.Core.Authorization;
using DrawVerify.Infrastructure.FileStorage;
using DrawVerify.Infrastructure.Persistence;
using DrawVerify.Infrastructure.Logging;
using DrawVerify.App.ViewModels;
using DrawVerify.OcctService;

namespace DrawVerify.App.Services;

/// <summary>
/// UI-level composition root. Wires concrete implementations from
/// Application and Infrastructure into the Core interfaces consumed by ViewModels.
/// </summary>
public static class CompositionRoot
{
    private static readonly StepUploadConfiguration StepUploadConfiguration = LoadStepUploadConfiguration();
    private static readonly DxfUploadConfiguration DxfUploadConfiguration = LoadDxfUploadConfiguration();
    private static readonly AuthenticationConfiguration AuthenticationConfiguration = LoadAuthenticationConfiguration();
    private static readonly AuditLogConfiguration AuditLogConfiguration = LoadAuditLogConfiguration();
    private static readonly SessionAuthenticationService Session = new();
    private static readonly RoleAuthorization Authorization = new();
    public static void Initialize() { }
    public static ISessionAuthenticationService SessionAuthentication => Session;
    public static RoleAuthorization RoleAuthorization => Authorization;
    public static UsernamePasswordAuthenticationService CreateAuthenticationService()
    {
        var logger=new CentralizedLogger(); var database=DatabasePath();
        return new(new SqliteUserRepository(database,logger),new SqliteAuthenticationAuditRepository(database,logger),Session,AuthenticationConfiguration,logger);
    }
    public static UserAdministrationService CreateUserAdministrationService()
    {
        var logger=new CentralizedLogger(); var database=DatabasePath();
        return new(new SqliteUserRepository(database,logger),new SqliteAuthenticationAuditRepository(database,logger),Session,AuthenticationConfiguration,logger,Authorization);
    }
    public static AuditLogService CreateAuditLogService()
    {
        var logger = new CentralizedLogger(); var database = DatabasePath();
        return new(new SqliteAuthenticationAuditRepository(database, logger), Session, Authorization, logger);
    }
    internal static AuditRetentionService CreateAuditRetentionService() => new(new SqliteAuditRetentionRepository(DatabasePath(), new CentralizedLogger()), AuditLogConfiguration);
    public static async Task<int> ApplySystemRetentionAsync(CancellationToken cancellationToken = default)
    {
        var logger = new CentralizedLogger();
        try { return await CreateAuditRetentionService().ApplySystemAsync(cancellationToken); }
        catch (Exception ex) { logger.Log(ex, "System audit retention", "Unable to apply the configured audit retention policy."); throw; }
    }
    public static StepUploadViewModel CreateStepUploadViewModel()
    {
        var config=StepUploadConfiguration;
        var database=Path.Combine(AppContext.BaseDirectory,"drawverify.db");
        var logger = new CentralizedLogger();
        return new StepUploadViewModel(new DocumentService(new SqliteDocumentRepository(database, logger),new SqliteAuditRepository(database),new DocumentFileStore(AppContext.BaseDirectory,config),config,Session,logger,Authorization,users:new SqliteUserRepository(database,logger)));
    }
    public static DxfUploadViewModel CreateDxfUploadViewModel()
    {
        var config = DxfUploadConfiguration;
        var database = DatabasePath();
        var logger = new CentralizedLogger();
        return new DxfUploadViewModel(new DxfDocumentService(new SqliteDocumentRepository(database, logger), new SqliteAuditRepository(database), new DxfDocumentFileStore(AppContext.BaseDirectory, config), config, Session, logger, Authorization, new SqliteUserRepository(database, logger)));
    }
    private static StepUploadConfiguration LoadStepUploadConfiguration()
    {
        var logger=new CentralizedLogger();
        try
        {
            using var document=JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"appsettings.json")));
            var section=document.RootElement.GetProperty("StepUpload");
            var config=new StepUploadConfiguration { MaxFileSizeMb=section.GetProperty("MaxFileSizeMb").GetInt32(), StorageFolder=section.GetProperty("StorageFolder").GetString() ?? string.Empty, DuplicatePolicy=section.GetProperty("DuplicatePolicy").GetString() ?? string.Empty, ValidationTimeoutSeconds=section.GetProperty("ValidationTimeoutSeconds").GetInt32() };
            config.Validate();
            return config;
        }
        catch(Exception exception)
        {
            logger.Log(exception,"STEP configuration startup","Unable to load or validate StepUpload settings");
            throw;
        }
    }
    private static DxfUploadConfiguration LoadDxfUploadConfiguration()
    {
        var logger = new CentralizedLogger();
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "appsettings.json")));
            var section = document.RootElement.GetProperty("DxfUpload");
            var config = new DxfUploadConfiguration
            {
                MaxFileSizeMb = section.GetProperty("MaxFileSizeMb").GetInt32(),
                StorageFolder = section.GetProperty("StorageFolder").GetString() ?? string.Empty,
                DuplicatePolicy = section.GetProperty("DuplicatePolicy").GetString() ?? string.Empty,
                ValidationTimeoutSeconds = section.GetProperty("ValidationTimeoutSeconds").GetInt32()
            };
            config.Validate();
            return config;
        }
        catch (Exception exception)
        {
            logger.Log(exception, "DXF configuration startup", "Unable to load or validate DxfUpload settings");
            throw;
        }
    }
    private static string DatabasePath() => Path.Combine(AppContext.BaseDirectory,"drawverify.db");
    private static AuthenticationConfiguration LoadAuthenticationConfiguration()
    {
        var logger=new CentralizedLogger(); try { using var document=JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"appsettings.json"))); var section=document.RootElement.GetProperty("Authentication"); var defaults=section.TryGetProperty("DefaultRoles", out var values) ? values.EnumerateArray().Select(x=>x.GetString() ?? string.Empty).ToArray() : Array.Empty<string>(); var config=new AuthenticationConfiguration { PasswordMinimumLength=section.GetProperty("PasswordMinimumLength").GetInt32(), RequireUppercase=section.GetProperty("RequireUppercase").GetBoolean(), RequireLowercase=section.GetProperty("RequireLowercase").GetBoolean(), RequireDigit=section.GetProperty("RequireDigit").GetBoolean(), RequireNonAlphanumeric=section.GetProperty("RequireNonAlphanumeric").GetBoolean(), LockoutThreshold=section.GetProperty("LockoutThreshold").GetInt32(), LockoutDuration=TimeSpan.FromMinutes(section.GetProperty("LockoutDurationMinutes").GetDouble()), DefaultRoles=defaults }; config.Validate(); return config; } catch(Exception ex) { logger.Log(ex,"Authentication configuration startup","Unable to load or validate Authentication settings"); throw; }
    }
    private static AuditLogConfiguration LoadAuditLogConfiguration()
    {
        var logger = new CentralizedLogger();
        try { using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "appsettings.json"))); var section = document.RootElement.GetProperty("AuditLog"); var config = new AuditLogConfiguration { RetentionDays = section.GetProperty("RetentionDays").GetInt32() }; config.Validate(); return config; }
        catch (Exception ex) { logger.Log(ex, "Audit log configuration startup", "Unable to load or validate AuditLog settings"); throw; }
    }
}
