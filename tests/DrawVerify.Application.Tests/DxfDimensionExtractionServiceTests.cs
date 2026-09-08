using System.Text;
using System.Text.Json;
using DrawVerify.Application.DxfDimensionExtraction;
using DrawVerify.Application.Services;
using DrawVerify.Core.Authorization;
using DrawVerify.Core.Configuration;
using DrawVerify.Core.Enums;
using DrawVerify.Core.Interfaces;
using DrawVerify.Core.Models;
using Xunit;
using CoreDocumentType = DrawVerify.Core.Enums.DocumentType;
using CoreUploadStatus = DrawVerify.Core.Enums.UploadStatus;

namespace DrawVerify.Application.Tests;

public sealed class DxfDimensionExtractionServiceTests
{
    [Theory(DisplayName = "TD-KAN25-APP-002 invokes extraction for Maker and Checker")]
    [InlineData(SystemRoles.Maker)]
    [InlineData(SystemRoles.Checker)]
    public async Task Authorized_roles_refresh_and_persist_success(string role)
    {
        var scenario = CreateScenario(role, new DxfDimensionExtractionResult(new[]
        {
            Dimension(Guid.Empty, Guid.Empty, 0, DimensionType.Linear, 25.0, displayText: "25")
        }));
        scenario.Parser.Result = new DxfDimensionExtractionResult(new[]
        {
            Dimension(scenario.Document.DocumentId, scenario.Version.VersionId, 0, DimensionType.Linear, 25.0, displayText: "25")
        });

        var result = await scenario.Service.ExtractDimensionsAsync(scenario.Document.DocumentId);

        Assert.True(result.Succeeded);
        Assert.Equal(VerificationStatus.DimensionsExtracted, result.VerificationStatus);
        Assert.Equal(1, scenario.Parser.Calls);
        Assert.Equal(1, scenario.Store.OpenReadCalls);
        Assert.Equal(1, scenario.Repository.ReplaceCalls);
        Assert.Single(scenario.Repository.Dimensions);
        Assert.Equal(DocumentAuditAction.DimensionsExtracted, Assert.Single(scenario.Repository.Audits).Action);
        Assert.Equal(scenario.Session.CurrentUser!.UserId, scenario.Repository.Audits[0].UserId);
        using var details = JsonDocument.Parse(scenario.Repository.Audits[0].DetailsJson);
        Assert.Equal(1, details.RootElement.GetProperty("dimensionCount").GetInt32());
        Assert.True(details.RootElement.GetProperty("durationMilliseconds").GetInt64() >= 0);
        Assert.Null(details.RootElement.GetProperty("error").GetString());
    }

    [Fact(DisplayName = "TD-KAN25-APP-002 refreshes persisted roles before opening the DXF")]
    public async Task Persisted_role_refresh_occurs_before_authorization_and_file_access()
    {
        var scenario = CreateScenario(SystemRoles.Viewer, new DxfDimensionExtractionResult(Array.Empty<DimensionData>()));
        scenario.Users.Account = Account(SystemRoles.Maker);
        scenario.Parser.Result = new DxfDimensionExtractionResult(new[]
        {
            Dimension(scenario.Document.DocumentId, scenario.Version.VersionId, 0, DimensionType.Angular, 90)
        });

        var result = await scenario.Service.ExtractDimensionsAsync(scenario.Document.DocumentId);

        Assert.True(result.Succeeded);
        Assert.Equal(1, scenario.Users.FindCalls);
        Assert.Equal(1, scenario.Store.OpenReadCalls);
        Assert.Equal(SystemRoles.Maker, scenario.Session.CurrentUser!.Roles.Single());
    }

    [Fact(DisplayName = "TD-KAN25-APP-002 records an informational audit for an empty drawing")]
    public async Task Empty_drawing_succeeds_with_zero_dimensions_and_information_log()
    {
        var scenario = CreateScenario(SystemRoles.Maker, DxfDimensionExtractionResult.Success(Array.Empty<DimensionData>()));

        var result = await scenario.Service.ExtractDimensionsAsync(scenario.Document.DocumentId);

        Assert.True(result.Succeeded);
        Assert.Empty(result.Dimensions);
        Assert.Equal(VerificationStatus.DimensionsExtracted, scenario.Repository.StoredDocument!.VerificationStatus);
        Assert.Equal(DocumentAuditAction.DimensionsExtracted, Assert.Single(scenario.Repository.Audits).Action);
        Assert.Contains(scenario.Logger.Entries, entry => entry.Operation.Contains("information", StringComparison.OrdinalIgnoreCase));
        using var details = JsonDocument.Parse(scenario.Repository.Audits[0].DetailsJson);
        Assert.Equal(0, details.RootElement.GetProperty("dimensionCount").GetInt32());
    }

    [Fact(DisplayName = "TD-KAN25-APP-002 persists partial data and warning audit metadata")]
    public async Task Recoverable_warnings_keep_dimensions_and_select_partial_audit()
    {
        var dimension = Dimension(Guid.NewGuid(), Guid.NewGuid(), 0, DimensionType.Radial, 8, displayText: "R8");
        var scenario = CreateScenario(SystemRoles.Checker, DxfDimensionExtractionResult.Success(new[] { dimension }, new[] { "Skipped malformed DIMENSION entity 1." }));
        scenario.Parser.Result = DxfDimensionExtractionResult.Success(new[]
        {
            Dimension(scenario.Document.DocumentId, scenario.Version.VersionId, 0, DimensionType.Radial, 8, displayText: "R8")
        }, new[] { "Skipped malformed DIMENSION entity 1." });

        var result = await scenario.Service.ExtractDimensionsAsync(scenario.Document.DocumentId);

        Assert.True(result.Succeeded);
        Assert.True(result.IsPartial);
        Assert.Single(scenario.Repository.Dimensions);
        Assert.Equal(VerificationStatus.DimensionsExtracted, scenario.Repository.StoredDocument!.VerificationStatus);
        Assert.Equal(DocumentAuditAction.DimensionsExtractedPartial, Assert.Single(scenario.Repository.Audits).Action);
        Assert.Contains(scenario.Logger.Entries, entry => entry.Operation.Contains("warning", StringComparison.OrdinalIgnoreCase));
        using var details = JsonDocument.Parse(scenario.Repository.Audits[0].DetailsJson);
        Assert.Equal(1, details.RootElement.GetProperty("warningCount").GetInt32());
    }

    [Fact(DisplayName = "TD-KAN25-APP-002 converts parser failure into Error with no dimensions")]
    public async Task Fatal_parser_result_sets_error_and_clears_current_dimensions()
    {
        var scenario = CreateScenario(SystemRoles.Maker, DxfDimensionExtractionResult.Failure("unable to parse DXF file."), initialDimensions: new[]
        {
            Dimension(Guid.NewGuid(), Guid.NewGuid(), 0, DimensionType.Linear, 1)
        });

        var result = await scenario.Service.ExtractDimensionsAsync(scenario.Document.DocumentId);

        Assert.False(result.Succeeded);
        Assert.Equal(VerificationStatus.Error, result.VerificationStatus);
        Assert.StartsWith("Dimension extraction failed:", result.ErrorMessage);
        Assert.Equal(VerificationStatus.Error, scenario.Repository.StoredDocument!.VerificationStatus);
        Assert.Empty(scenario.Repository.Dimensions);
        Assert.Equal(DocumentAuditAction.DimensionExtractionFailed, Assert.Single(scenario.Repository.Audits).Action);
        Assert.Contains(scenario.Logger.Entries, entry => entry.Operation == "DXF dimension extraction warning" || entry.Operation == "DXF dimension extraction");
        using var details = JsonDocument.Parse(scenario.Repository.Audits[0].DetailsJson);
        Assert.Equal(0, details.RootElement.GetProperty("dimensionCount").GetInt32());
        Assert.Equal("Dimension extraction failed: unable to parse DXF file.", details.RootElement.GetProperty("error").GetString());
    }

    [Fact(DisplayName = "TD-KAN25-APP-002 reports parser exceptions with diagnostics")]
    public async Task Parser_exception_is_logged_and_persisted_as_a_clear_failure()
    {
        var scenario = CreateScenario(SystemRoles.Maker, DxfDimensionExtractionResult.Success(Array.Empty<DimensionData>()));
        scenario.Parser.ExceptionToThrow = new InvalidDataException("severe parser corruption");

        var result = await scenario.Service.ExtractDimensionsAsync(scenario.Document.DocumentId);

        Assert.False(result.Succeeded);
        Assert.Equal("Dimension extraction failed: unable to parse DXF file.", result.Message);
        Assert.DoesNotContain("severe parser corruption", result.Message, StringComparison.Ordinal);
        Assert.Empty(scenario.Repository.Dimensions);
        Assert.Equal(DocumentAuditAction.DimensionExtractionFailed, Assert.Single(scenario.Repository.Audits).Action);
        Assert.Contains(scenario.Logger.Entries, entry => entry.Operation == "DXF dimension extraction" && entry.Exception is InvalidDataException);
        using var details = JsonDocument.Parse(scenario.Repository.Audits[0].DetailsJson);
        Assert.Equal("Dimension extraction failed: unable to parse DXF file.", details.RootElement.GetProperty("error").GetString());
        Assert.True(details.RootElement.GetProperty("durationMilliseconds").GetInt64() >= 0);
    }

    [Fact(DisplayName = "TD-KAN25-APP-002 reports missing DXF storage visibly")]
    public async Task Missing_file_is_a_failure_without_parser_invocation()
    {
        var scenario = CreateScenario(SystemRoles.Maker, DxfDimensionExtractionResult.Success(Array.Empty<DimensionData>()));
        scenario.Store.OpenException = new FileNotFoundException("stored DXF is missing");

        var result = await scenario.Service.ExtractDimensionsAsync(scenario.Document.DocumentId);

        Assert.False(result.Succeeded);
        Assert.Contains("stored DXF is missing", result.Message);
        Assert.Equal(0, scenario.Parser.Calls);
        Assert.Empty(scenario.Repository.Dimensions);
        Assert.Equal(VerificationStatus.Error, scenario.Repository.StoredDocument!.VerificationStatus);
        Assert.Equal(DocumentAuditAction.DimensionExtractionFailed, Assert.Single(scenario.Repository.Audits).Action);
    }

    [Fact(DisplayName = "TD-KAN25-APP-002 enforces the configured maximum file size")]
    public async Task Oversized_document_fails_before_file_open()
    {
        var scenario = CreateScenario(SystemRoles.Maker, DxfDimensionExtractionResult.Success(Array.Empty<DimensionData>()), maximumFileSizeBytes: 4);
        scenario.Repository.CurrentVersion = new DocumentVersion
        {
            VersionId = scenario.Version.VersionId,
            DocumentId = scenario.Document.DocumentId,
            VersionNumber = scenario.Version.VersionNumber,
            StoredRelativePath = scenario.Version.StoredRelativePath,
            FileSizeBytes = 5
        };

        var result = await scenario.Service.ExtractDimensionsAsync(scenario.Document.DocumentId);

        Assert.False(result.Succeeded);
        Assert.Contains("maximum size", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, scenario.Store.OpenReadCalls);
        Assert.Equal(0, scenario.Parser.Calls);
        Assert.Empty(scenario.Repository.Dimensions);
        Assert.Equal(DocumentAuditAction.DimensionExtractionFailed, Assert.Single(scenario.Repository.Audits).Action);
    }

    [Fact(DisplayName = "TD-KAN25-APP-002 enforces timeout and disposes the opened stream")]
    public async Task Timeout_fails_with_zero_dimensions_and_released_stream()
    {
        var scenario = CreateScenario(SystemRoles.Maker, DxfDimensionExtractionResult.Success(Array.Empty<DimensionData>()), timeoutSeconds: 1);
        scenario.Parser.BlockUntilCancelled = true;

        var result = await scenario.Service.ExtractDimensionsAsync(scenario.Document.DocumentId);

        Assert.False(result.Succeeded);
        Assert.Equal(DxfDimensionExtractionService.TimeoutMessage, result.Message);
        Assert.True(scenario.Parser.CancellationObserved);
        Assert.True(scenario.Store.LastStream!.IsDisposed);
        Assert.Equal(VerificationStatus.Error, scenario.Repository.StoredDocument!.VerificationStatus);
        Assert.Empty(scenario.Repository.Dimensions);
        Assert.Equal(DocumentAuditAction.DimensionExtractionFailed, Assert.Single(scenario.Repository.Audits).Action);
        using var details = JsonDocument.Parse(scenario.Repository.Audits[0].DetailsJson);
        Assert.Contains("configured timeout", details.RootElement.GetProperty("error").GetString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact(DisplayName = "TD-KAN25-APP-002 enforces the streaming size limit")]
    public async Task Stream_that_grows_past_the_limit_fails_and_releases_resources()
    {
        var scenario = CreateScenario(SystemRoles.Maker, DxfDimensionExtractionResult.Success(Array.Empty<DimensionData>()), maximumFileSizeBytes: 4);
        scenario.Repository.CurrentVersion = new DocumentVersion
        {
            VersionId = scenario.Version.VersionId,
            DocumentId = scenario.Document.DocumentId,
            VersionNumber = scenario.Version.VersionNumber,
            StoredRelativePath = scenario.Version.StoredRelativePath,
            FileSizeBytes = 4
        };
        scenario.Store.Content = new byte[] { 1, 2, 3, 4, 5 };
        scenario.Parser.ReadEntireStream = true;

        var result = await scenario.Service.ExtractDimensionsAsync(scenario.Document.DocumentId);

        Assert.False(result.Succeeded);
        Assert.Contains("maximum size", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.True(scenario.Store.LastStream!.IsDisposed);
        Assert.Empty(scenario.Repository.Dimensions);
        Assert.Equal(VerificationStatus.Error, scenario.Repository.StoredDocument!.VerificationStatus);
    }

    [Fact(DisplayName = "TD-KAN25-APP-002 denies unauthorized and ineligible extraction before access")]
    public async Task Unauthorized_and_ineligible_requests_do_not_call_dependencies()
    {
        foreach (var role in new string?[] { null, SystemRoles.Admin, SystemRoles.Approver, SystemRoles.Viewer })
        {
            var scenario = CreateScenario(role, DxfDimensionExtractionResult.Success(Array.Empty<DimensionData>()));
            var result = await scenario.Service.ExtractDimensionsAsync(scenario.Document.DocumentId);

            Assert.False(result.Succeeded);
            Assert.Equal(0, scenario.Repository.GetCalls);
            Assert.Equal(0, scenario.Store.OpenReadCalls);
            Assert.Equal(0, scenario.Parser.Calls);
            Assert.Equal(0, scenario.Repository.ReplaceCalls);
            Assert.Empty(scenario.Repository.Audits);
        }

        var ineligible = CreateScenario(SystemRoles.Maker, DxfDimensionExtractionResult.Success(Array.Empty<DimensionData>()));
        ineligible.Repository.StoredDocument = ineligible.Document with { DocumentType = CoreDocumentType.StepModel };
        var ineligibleResult = await ineligible.Service.ExtractDimensionsAsync(ineligible.Document.DocumentId);
        Assert.False(ineligibleResult.Succeeded);
        Assert.Equal(0, ineligible.Store.OpenReadCalls);
        Assert.Equal(0, ineligible.Parser.Calls);
        Assert.Empty(ineligible.Repository.Audits);
    }

    [Fact(DisplayName = "TD-KAN25-APP-002 authorizes and denies dimension result queries")]
    public async Task Result_queries_are_authorized_and_do_not_expose_data_to_other_roles()
    {
        var authorized = CreateScenario(SystemRoles.Checker, DxfDimensionExtractionResult.Success(Array.Empty<DimensionData>()));
        authorized.Repository.Dimensions.Add(Dimension(authorized.Document.DocumentId, authorized.Version.VersionId, 0, DimensionType.Angular, 30));
        var dimensions = await authorized.Service.GetDimensionsAsync(authorized.Document.DocumentId);
        Assert.Single(dimensions);
        Assert.Equal(1, authorized.Repository.GetDimensionsCalls);

        foreach (var role in new string?[] { null, SystemRoles.Admin, SystemRoles.Approver, SystemRoles.Viewer })
        {
            var scenario = CreateScenario(role, DxfDimensionExtractionResult.Success(Array.Empty<DimensionData>()));
            scenario.Repository.Dimensions.Add(Dimension(scenario.Document.DocumentId, scenario.Version.VersionId, 0, DimensionType.Linear, 1));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => scenario.Service.GetDimensionsAsync(scenario.Document.DocumentId));
            Assert.Equal(0, scenario.Repository.GetCalls);
            Assert.Equal(0, scenario.Repository.GetDimensionsCalls);
        }
    }

    [Fact(DisplayName = "TD-KAN25-APP-002 replaces dimensions on re-extraction without creating a version")]
    public async Task Re_extraction_replaces_current_results_and_adds_audit_only()
    {
        var scenario = CreateScenario(SystemRoles.Maker, DxfDimensionExtractionResult.Success(Array.Empty<DimensionData>()));
        scenario.Parser.Results.Enqueue(new DxfDimensionExtractionResult(new[]
        {
            Dimension(scenario.Document.DocumentId, scenario.Version.VersionId, 0, DimensionType.Linear, 10)
        }));
        scenario.Parser.Results.Enqueue(new DxfDimensionExtractionResult(new[]
        {
            Dimension(scenario.Document.DocumentId, scenario.Version.VersionId, 0, DimensionType.Linear, 12),
            Dimension(scenario.Document.DocumentId, scenario.Version.VersionId, 1, DimensionType.Radial, 4)
        }));

        await scenario.Service.ExtractDimensionsAsync(scenario.Document.DocumentId);
        await scenario.Service.ExtractDimensionsAsync(scenario.Document.DocumentId);

        Assert.Equal(2, scenario.Parser.Calls);
        Assert.Equal(2, scenario.Repository.ReplaceCalls);
        Assert.Equal(2, scenario.Repository.Dimensions.Count);
        Assert.Equal(new[] { 12.0, 4.0 }, scenario.Repository.Dimensions.Select(dimension => dimension.NominalValue));
        Assert.Equal(2, scenario.Repository.Audits.Count);
        Assert.All(scenario.Repository.Audits, audit => Assert.Equal(DocumentAuditAction.DimensionsExtracted, audit.Action));
        Assert.Equal(1, scenario.Repository.DocumentVersionCount);
        Assert.Equal(0, scenario.Repository.CreateVersionCalls);
        Assert.Equal(scenario.Version.VersionId, scenario.Repository.CurrentVersion.VersionId);
    }

    private static Scenario CreateScenario(
        string? role,
        DxfDimensionExtractionResult result,
        IEnumerable<DimensionData>? initialDimensions = null,
        long maximumFileSizeBytes = 52_428_800,
        int timeoutSeconds = 30)
    {
        var documentId = Guid.NewGuid();
        var version = new DocumentVersion
        {
            DocumentId = documentId,
            VersionNumber = 1,
            StoredRelativePath = "uploads/dxf/drawing.dxf",
            FileSizeBytes = 128
        };
        var document = new Document
        {
            DocumentId = documentId,
            OriginalFileName = "drawing.dxf",
            FileSizeBytes = version.FileSizeBytes,
            DocumentType = CoreDocumentType.DxfDrawing,
            UploadStatus = CoreUploadStatus.Uploaded,
            VerificationStatus = VerificationStatus.PendingVerification,
            Version = version,
            Versions = new[] { version }
        };
        var repository = new FakeRepository(document, version, initialDimensions);
        var store = new FakeStore();
        var parser = new FakeParser { Result = result };
        var session = new SessionAuthenticationService();
        if (role is not null)
            session.SignIn(new AuthenticatedUser("maker", new[] { role }) { UserId = "user-42", DisplayName = "Test User" });
        var users = new FakeUsers(Account(role ?? SystemRoles.Viewer));
        var logger = new FakeLogger();
        var service = new DxfDimensionExtractionService(
            repository,
            store,
            parser,
            new DxfDimensionExtractionConfiguration { MaximumFileSizeBytes = maximumFileSizeBytes, TimeoutSeconds = timeoutSeconds },
            session,
            users,
            logger,
            new RoleAuthorization());
        return new Scenario(document, version, repository, store, parser, session, users, logger, service);
    }

    private static UserAccount Account(params string[] roles) => new()
    {
        Id = Guid.Parse("00000000-0000-0000-0000-000000000042"),
        Username = "maker",
        DisplayName = "Test User",
        PasswordSalt = new byte[] { 1 },
        PasswordHash = new byte[] { 2 },
        Roles = roles
    };

    private static DimensionData Dimension(Guid documentId, Guid versionId, int ordinal, DimensionType type, double nominal, double? lower = null, double? upper = null, string? displayText = null) =>
        new(documentId == Guid.Empty ? Guid.NewGuid() : documentId, versionId == Guid.Empty ? Guid.NewGuid() : versionId, ordinal, type, nominal, lower, upper, displayText);

    private sealed class Scenario(
        Document document,
        DocumentVersion version,
        FakeRepository repository,
        FakeStore store,
        FakeParser parser,
        SessionAuthenticationService session,
        FakeUsers users,
        FakeLogger logger,
        DxfDimensionExtractionService service)
    {
        public Document Document { get; } = document;
        public DocumentVersion Version { get; set; } = version;
        public FakeRepository Repository { get; } = repository;
        public FakeStore Store { get; } = store;
        public FakeParser Parser { get; } = parser;
        public SessionAuthenticationService Session { get; } = session;
        public FakeUsers Users { get; } = users;
        public FakeLogger Logger { get; } = logger;
        public DxfDimensionExtractionService Service { get; } = service;
    }

    private sealed class FakeRepository(Document document, DocumentVersion version, IEnumerable<DimensionData>? initialDimensions) : IDocumentRepository
    {
        public Document? StoredDocument { get; set; } = document;
        public DocumentVersion CurrentVersion { get; set; } = version;
        public List<DimensionData> Dimensions { get; } = initialDimensions?.ToList() ?? new();
        public List<AuditEntry> Audits { get; } = new();
        public int GetCalls { get; private set; }
        public int GetDimensionsCalls { get; private set; }
        public int ReplaceCalls { get; private set; }
        public int DocumentVersionCount => StoredDocument?.Versions.Count ?? 0;
        public int CreateVersionCalls { get; private set; }

        public Document? Get(Guid documentId)
        {
            GetCalls++;
            return StoredDocument?.DocumentId == documentId ? StoredDocument : null;
        }

        public Task<Document?> GetAsync(Guid documentId, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(Get(documentId));
        }

        public DocumentVersion? GetCurrentVersion(Guid documentId) =>
            StoredDocument?.DocumentId == documentId ? CurrentVersion : null;

        public Task ReplaceDimensionsAsync(Guid documentId, Guid documentVersionId, IReadOnlyCollection<DimensionData> dimensions, VerificationStatus status, string? verificationError, AuditEntry auditEntry, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ReplaceCalls++;
            Dimensions.Clear();
            Dimensions.AddRange(dimensions);
            Audits.Add(auditEntry);
            if (StoredDocument is not null)
                StoredDocument = StoredDocument with { VerificationStatus = status, VerificationError = verificationError };
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<DimensionData>> GetDimensionsAsync(Guid documentId, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            GetDimensionsCalls++;
            return Task.FromResult<IReadOnlyList<DimensionData>>(Dimensions.ToArray());
        }

        public Task CreateWithVersionAsync(Document document, DocumentVersion version, CancellationToken cancellationToken = default)
        {
            CreateVersionCalls++;
            return Task.CompletedTask;
        }

        public Task AddVersionAsync(Guid documentId, DocumentVersion version, CancellationToken cancellationToken = default)
        {
            CreateVersionCalls++;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeStore : IDxfUploadFileStore
    {
        public byte[] Content { get; set; } = Encoding.ASCII.GetBytes("0\nSECTION\n2\nENTITIES\n0\nENDSEC\n0\nEOF\n");
        public Exception? OpenException { get; set; }
        public int OpenReadCalls { get; private set; }
        public TrackingStream? LastStream { get; private set; }

        public Task<DxfUploadStagingFile> CreateStagingFileAsync(string originalFileName, CancellationToken cancellationToken = default) =>
            Task.FromResult(new DxfUploadStagingFile("stage", new MemoryStream()));

        public Task<string> MoveToFinalAsync(DxfUploadStagingFile stagingFile, CancellationToken cancellationToken = default) =>
            Task.FromResult("uploads/dxf/final.dxf");

        public Task DeleteAsync(string relativePath, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<Stream> OpenReadAsync(string relativePath, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            OpenReadCalls++;
            if (OpenException is not null)
                return Task.FromException<Stream>(OpenException);
            LastStream = new TrackingStream(Content);
            return Task.FromResult<Stream>(LastStream);
        }
    }

    private sealed class TrackingStream(byte[] content) : MemoryStream(content)
    {
        public bool IsDisposed { get; private set; }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                IsDisposed = true;
            base.Dispose(disposing);
        }
    }

    private sealed class FakeParser : IDxfDimensionParser
    {
        public DxfDimensionExtractionResult Result { get; set; } = DxfDimensionExtractionResult.Success(Array.Empty<DimensionData>());
        public Queue<DxfDimensionExtractionResult> Results { get; } = new();
        public Exception? ExceptionToThrow { get; set; }
        public bool ReadEntireStream { get; set; }
        public bool BlockUntilCancelled { get; set; }
        public bool CancellationObserved { get; private set; }
        public int Calls { get; private set; }
        public CancellationToken LastToken { get; private set; }

        public Task<DxfDimensionExtractionResult> ParseAsync(Stream content, Guid documentId, Guid documentVersionId, CancellationToken cancellationToken = default)
        {
            Calls++;
            LastToken = cancellationToken;
            if (ExceptionToThrow is not null)
                return Task.FromException<DxfDimensionExtractionResult>(ExceptionToThrow);
            if (BlockUntilCancelled)
                return BlockAsync(cancellationToken);
            if (ReadEntireStream)
                return ReadAllAsync(content, cancellationToken);
            return Task.FromResult(Results.Count > 0 ? Results.Dequeue() : Result);
        }

        private async Task<DxfDimensionExtractionResult> BlockAsync(CancellationToken cancellationToken)
        {
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                CancellationObserved = true;
                throw;
            }
            return Result;
        }

        private async Task<DxfDimensionExtractionResult> ReadAllAsync(Stream content, CancellationToken cancellationToken)
        {
            var buffer = new byte[16];
            while (await content.ReadAsync(buffer, cancellationToken) > 0)
            {
            }
            return Result;
        }
    }

    private sealed class FakeUsers(UserAccount account) : IUserRepository
    {
        public UserAccount Account { get; set; } = account;
        public int FindCalls { get; private set; }

        public Task<UserAccount?> FindByUsernameAsync(string normalizedUsername, CancellationToken cancellationToken = default)
        {
            FindCalls++;
            return Task.FromResult<UserAccount?>(string.Equals(normalizedUsername, Account.Username, StringComparison.OrdinalIgnoreCase) ? Account : null);
        }

        public Task CreateAsync(UserAccount account, AuthenticationAuditEntry audit, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<LoginAttemptState> RecordLoginAttemptAsync(string normalizedUsername, bool successful, int lockoutThreshold, TimeSpan lockoutDuration, DateTimeOffset now, CancellationToken cancellationToken = default) =>
            Task.FromResult(new LoginAttemptState(Account, false, null));

        public Task<LoginAttemptState> VerifyAndRecordLoginAttemptAsync(string normalizedUsername, Func<UserAccount, bool> verifier, int lockoutThreshold, TimeSpan lockoutDuration, DateTimeOffset now, CancellationToken cancellationToken = default) =>
            Task.FromResult(new LoginAttemptState(Account, false, null));
    }

    private sealed class FakeLogger : ICentralizedLogger
    {
        public List<(Exception Exception, string Operation, string Context)> Entries { get; } = new();
        public void Log(Exception exception, string operation, string context) => Entries.Add((exception, operation, context));
    }
}
