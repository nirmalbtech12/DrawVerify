using System.IO;
using System.Text.Json;
using DrawVerify.Application.Services;
using DrawVerify.Application.Fingerprinting;
using DrawVerify.Application.DxfDimensionExtraction;
using DrawVerify.Core.Configuration;
using DrawVerify.Core.Interfaces;
using DrawVerify.Core.Authorization;
using DrawVerify.Infrastructure.FileStorage;
using DrawVerify.Infrastructure.Persistence;
using DrawVerify.Infrastructure.Logging;
using DrawVerify.Infrastructure.Notifications;
using DrawVerify.App.ViewModels;
using DrawVerify.OcctService;
using DrawVerify.Application.Verification;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

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
    private static readonly DxfDimensionExtractionConfiguration DxfDimensionExtractionConfiguration = LoadDxfDimensionExtractionConfiguration();
    private static readonly DimensionMatchingConfiguration DimensionMatchingConfiguration = LoadDimensionMatchingConfiguration();
    private static readonly WorkflowConfiguration WorkflowConfiguration = LoadWorkflowConfiguration();
    private static readonly SessionAuthenticationService Session = new();
    private static readonly RoleAuthorization Authorization = new();
    public static void Initialize()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "appsettings.json")));
        if (!document.RootElement.TryGetProperty("Branding", out var branding)) return;
        ApplyBrush("ShellBackground", branding, "Background");
        ApplyBrush("BrandGreen", branding, "PrimaryColor");
        ApplyBrush("BrandWhite", branding, "TextColor");
        ApplyImage("BrandLogoSource", branding, "LogoPath");
        ApplyImage("BrandIconSource", branding, "IconPath");
    }
    private static void ApplyImage(string resourceKey, JsonElement branding, string settingName)
    {
        if (System.Windows.Application.Current is null) return;
        if (!branding.TryGetProperty(settingName, out var value) || string.IsNullOrWhiteSpace(value.GetString())) return;
        var configuredPath = value.GetString()!;
        var path = Path.IsPathFullyQualified(configuredPath) ? configuredPath : Path.Combine(AppContext.BaseDirectory, configuredPath);
        if (!File.Exists(path)) return;
        try { var image = new BitmapImage(new Uri(path, UriKind.Absolute)); image.Freeze(); System.Windows.Application.Current.Resources[resourceKey] = image; }
        catch (Exception exception) { new CentralizedLogger().Log(exception, "Branding asset load", $"Unable to load configured {settingName}='{path}'. The built-in branding fallback will be used."); }
    }
    private static void ApplyBrush(string resourceKey, JsonElement branding, string settingName)
    {
        if (System.Windows.Application.Current is null) return;
        if (!branding.TryGetProperty(settingName, out var value) || string.IsNullOrWhiteSpace(value.GetString())) return;
        var color = (Color)ColorConverter.ConvertFromString(value.GetString()!)!;
        System.Windows.Application.Current.Resources[resourceKey] = new SolidColorBrush(color);
    }
    public static ISessionAuthenticationService SessionAuthentication => Session;
    public static RoleAuthorization RoleAuthorization => Authorization;
    public static IWorkflowService CreateWorkflowService()
    {
        var database = DatabasePath(); var logger = new CentralizedLogger();
        var policyPath = Path.Combine(AppContext.BaseDirectory, "config", "fingerprint-policy.default.json");
        if (!File.Exists(policyPath)) policyPath = Path.Combine(Directory.GetCurrentDirectory(), "config", "fingerprint-policy.default.json");
        var fingerprintService = new DocumentFingerprintService(FingerprintPolicy.FromFile(policyPath));
        return new WorkflowService(new SqliteWorkflowRepository(database, logger, WorkflowConfiguration), Session, WorkflowConfiguration, Authorization, logger, new SqliteDocumentRepository(database, logger), new SqliteVerificationSessionRepository(database), notifications: new InAppNotificationService(database), snapshots: new ApprovalSnapshotFileStore(Path.Combine(AppContext.BaseDirectory, "approval-snapshots")), stepFiles: new DocumentFileStore(AppContext.BaseDirectory, StepUploadConfiguration), dxfFiles: new DxfDocumentFileStore(AppContext.BaseDirectory, DxfUploadConfiguration), fingerprintService: fingerprintService);
    }
    public static IWorkflowConfigurationService CreateWorkflowConfigurationService()
    {
        var logger = new CentralizedLogger();
        try
        {
            var repository = new SqliteWorkflowConfigurationRepository(DatabasePath(), logger);
            if (repository.GetCurrentAsync().GetAwaiter().GetResult() is null)
            {
                var bootstrap = WorkflowConfiguration;
                repository.SaveAsync(bootstrap, new DrawVerify.Core.Models.WorkflowConfigurationAudit("system", DateTime.UtcNow, null, bootstrap.Version, string.Empty, JsonSerializer.Serialize(bootstrap)), null).GetAwaiter().GetResult();
            }
            return new WorkflowConfigurationService(repository, Session, Authorization, logger);
        }
        catch (Exception exception)
        {
            logger.Log(exception, "Workflow configuration bootstrap", "Unable to initialize the effective configuration from config/workflow.default.json.");
            throw;
        }
    }
    public static DimensionMatchingConfiguration ConfiguredDimensionMatching => DimensionMatchingConfiguration;
    public static GdtValidator CreateGdtValidator()
    {
        var configuredPath = Path.Combine(AppContext.BaseDirectory, "config", "gdt-subset.default.json");
        if (!File.Exists(configuredPath)) configuredPath = Path.Combine(Directory.GetCurrentDirectory(), "config", "gdt-subset.default.json");
        return GdtValidator.FromSubsetFile(configuredPath);
    }
    public static VerificationEngine CreateVerificationEngine() => new(CreateGdtValidator());
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
        return new((IDocumentAuditRepository)new SqliteAuditRepository(database), Session, Authorization, logger);
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
        return new StepUploadViewModel(new DocumentService(new SqliteDocumentRepository(database, logger),new SqliteAuditRepository(database),new DocumentFileStore(AppContext.BaseDirectory,config),config,Session,logger,Authorization,users:new SqliteUserRepository(database,logger),workflow:new SqliteWorkflowRepository(database, logger, WorkflowConfiguration)));
    }
    public static DxfUploadViewModel CreateDxfUploadViewModel()
    {
        var config = DxfUploadConfiguration;
        var database = DatabasePath();
        var logger = new CentralizedLogger();
        return new DxfUploadViewModel(new DxfDocumentService(new SqliteDocumentRepository(database, logger), new SqliteAuditRepository(database), new DxfDocumentFileStore(AppContext.BaseDirectory, config), config, Session, logger, Authorization, new SqliteUserRepository(database, logger)));
    }
    public static DxfDimensionExtractionService CreateDxfDimensionExtractionService()
    {
        var database = DatabasePath();
        var logger = new CentralizedLogger();
        return new DxfDimensionExtractionService(
            new SqliteDocumentRepository(database, logger),
            new DxfDocumentFileStore(AppContext.BaseDirectory, DxfUploadConfiguration),
            new DxfDimensionParser(),
            DxfDimensionExtractionConfiguration,
            Session,
            new SqliteUserRepository(database, logger),
            logger,
            Authorization);
    }
    public static MeasurementExtractionService CreateMeasurementExtractionService()
    {
        var database = DatabasePath();
        var logger = new CentralizedLogger();
        var worker = new OcctWorkerGeometryService(Path.Combine(AppContext.BaseDirectory, DimensionMatchingConfiguration.WorkerPath), DimensionMatchingConfiguration.MaximumStepFileSizeBytes, DimensionMatchingConfiguration.TimeoutSeconds, logger, Path.Combine(AppContext.BaseDirectory, StepUploadConfiguration.StorageFolder));
        return new MeasurementExtractionService(new SqliteDocumentRepository(database, logger), new SqliteAuditRepository(database), worker);
    }
    public static CombinedUploadVerifyViewModel CreateCombinedUploadVerifyViewModel() =>
        new(CreateStepUploadViewModel(), CreateDxfUploadViewModel(), CreateMeasurementExtractionService(), CreateDxfDimensionExtractionService(), new VerificationViewModel(Session, Authorization, CreateDimensionMatchingService(), new CentralizedLogger(), DimensionMatchingConfiguration), new CentralizedLogger(), CreateDxfDrawingGenerationService());
    public static DxfDrawingGenerationService CreateDxfDrawingGenerationService()
    {
        var database = DatabasePath(); var logger = new CentralizedLogger(); var documents = new SqliteDocumentRepository(database, logger); var users = new SqliteUserRepository(database, logger); var dxfFiles = new DxfDocumentFileStore(AppContext.BaseDirectory, DxfUploadConfiguration);
        var extraction = new DxfDimensionExtractionService(documents, dxfFiles, new DxfDimensionParser(), DxfDimensionExtractionConfiguration, Session, users, logger, Authorization);
        return new DxfDrawingGenerationService(documents, new DocumentFileStore(AppContext.BaseDirectory, StepUploadConfiguration), dxfFiles, new OcctGeometryService(), new SqliteAuditRepository(database), Session, Authorization, logger, users, extraction, new SqliteWorkflowRepository(database, logger, WorkflowConfiguration));
    }
    public static DocumentDetailViewModel CreateDocumentDetailViewModel(DrawVerify.Core.Models.Document document) =>
        new(document, CreateDxfDimensionExtractionService(), CreateWorkflowService(), null, Session, Authorization, CreateHistoricalDocumentService());
    public static DocumentDetailViewModel CreateDocumentDetailViewModel(DrawVerify.Core.Models.Document document, DrawVerify.Core.Models.SubmissionRequest submissionRequest) =>
        new(document, CreateDxfDimensionExtractionService(), CreateWorkflowService(), submissionRequest, Session, Authorization, CreateHistoricalDocumentService());
    private static DocumentService CreateHistoricalDocumentService()
    {
        var database = DatabasePath();
        var logger = new CentralizedLogger();
        return new DocumentService(new SqliteDocumentRepository(database, logger), new SqliteAuditRepository(database), new DocumentFileStore(AppContext.BaseDirectory, StepUploadConfiguration), StepUploadConfiguration, Session, logger, Authorization, users: new SqliteUserRepository(database, logger), workflow: new SqliteWorkflowRepository(database, logger, WorkflowConfiguration), dxfFiles: new DxfDocumentFileStore(AppContext.BaseDirectory, DxfUploadConfiguration));
    }
    public static DimensionMatchingService CreateDimensionMatchingService()
    {
        var database = DatabasePath();
        var logger = new CentralizedLogger();
        var workerPath = Path.Combine(AppContext.BaseDirectory, DimensionMatchingConfiguration.WorkerPath);
        var documents = new SqliteDocumentRepository(database, logger);
        var sessions = new SqliteVerificationSessionRepository(database);
        var dxfFiles = new DxfDocumentFileStore(AppContext.BaseDirectory, DxfUploadConfiguration);
        var worker = new OcctWorkerGeometryService(workerPath, DimensionMatchingConfiguration.MaximumStepFileSizeBytes, DimensionMatchingConfiguration.TimeoutSeconds, logger, Path.Combine(AppContext.BaseDirectory, StepUploadConfiguration.StorageFolder));
        var gdtVerification = new VerificationService(CreateVerificationEngine(), sessions);
        return new DimensionMatchingService(documents, sessions, worker, Session, DimensionMatchingConfiguration, Authorization, logger, gdtVerification, dxfFiles, new DxfGdtParser(), worker, Path.Combine(AppContext.BaseDirectory, StepUploadConfiguration.StorageFolder));
    }
    public static ReportService CreateReportService()
    {
        var database = DatabasePath();
        return new ReportService(new SqliteVerificationSessionRepository(database), Session, Authorization, new MarkdownReportGenerator(), new CentralizedLogger(), new SqliteAuditRepository(database), new DrawVerify.Infrastructure.Pdf.PdfReportGenerator());
    }
    public static MissingDimensionReviewService CreateMissingDimensionReviewService()
    {
        var database = DatabasePath();
        return new MissingDimensionReviewService(new SqliteVerificationSessionRepository(database), Session, Authorization, new CentralizedLogger());
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
    private static WorkflowConfiguration LoadWorkflowConfiguration()
    {
        var logger = new CentralizedLogger();
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "config", "workflow.default.json");
            if (!File.Exists(path)) path = Path.Combine(Directory.GetCurrentDirectory(), "config", "workflow.default.json");
            var configuration = JsonSerializer.Deserialize<WorkflowConfiguration>(File.ReadAllText(path), new JsonSerializerOptions { Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } }) ?? throw new InvalidOperationException("Workflow configuration is empty.");
            configuration.Validate(); return configuration;
        }
        catch (Exception exception) { logger.Log(exception, "Workflow configuration startup", "Unable to load or validate workflow.default.json"); throw; }
    }
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
    private static DxfDimensionExtractionConfiguration LoadDxfDimensionExtractionConfiguration()
    {
        var logger = new CentralizedLogger();
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "appsettings.json")));
            var section = document.RootElement.GetProperty("DxfDimensionExtraction");
            var config = new DxfDimensionExtractionConfiguration
            {
                MaximumFileSizeBytes = section.GetProperty("MaximumFileSizeBytes").GetInt64(),
                TimeoutSeconds = section.GetProperty("TimeoutSeconds").GetInt32()
            };
            config.Validate();
            return config;
        }
        catch (Exception exception)
        {
            logger.Log(exception, "DXF dimension extraction configuration startup", "Unable to load or validate DxfDimensionExtraction settings");
            throw;
        }
    }
    private static DimensionMatchingConfiguration LoadDimensionMatchingConfiguration()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "appsettings.json")));
        var section = document.RootElement.GetProperty("DimensionMatching");
        var config = new DimensionMatchingConfiguration { MaximumStepFileSizeBytes = section.GetProperty("MaximumStepFileSizeBytes").GetInt64(), TimeoutSeconds = section.GetProperty("TimeoutSeconds").GetInt32(), AbsoluteTolerance = section.GetProperty("AbsoluteTolerance").GetDouble(), RelativeTolerance = section.GetProperty("RelativeTolerance").GetDouble(), ToleranceThresholdMm = section.GetProperty("ToleranceThresholdMm").GetDouble(), AngularToleranceDegrees = section.GetProperty("AngularToleranceDegrees").GetDouble(), ComparisonTimeoutSeconds = section.GetProperty("ComparisonTimeoutSeconds").GetInt32(), HighConfidenceThreshold = section.GetProperty("HighConfidenceThreshold").GetDouble(), MediumConfidenceThreshold = section.GetProperty("MediumConfidenceThreshold").GetDouble(), WorkerPath = section.GetProperty("WorkerPath").GetString() ?? string.Empty };
        config.Validate(); return config;
    }
}
