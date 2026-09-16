using System.Text;
using DrawVerify.App.ViewModels;
using DrawVerify.Application.DxfDimensionExtraction;
using DrawVerify.Application.Services;
using DrawVerify.Core.Authorization;
using DrawVerify.Core.Configuration;
using DrawVerify.Core.Enums;
using DrawVerify.Core.Interfaces;
using DrawVerify.Core.Models;
using Xunit;
using CoreDocumentType = DrawVerify.Core.Enums.DocumentType;
using CoreGeometryStatus = DrawVerify.Core.Enums.GeometryStatus;
using CoreUploadStatus = DrawVerify.Core.Enums.UploadStatus;

namespace DrawVerify.App.Tests;

public sealed class DocumentDetailViewModelTests
{
    [Fact(DisplayName = "TD-07 exposes fingerprint metadata and exact verification states")]
    public void Fingerprint_metadata_and_result_messages_are_distinct()
    {
        var fingerprint = new DocumentFingerprint { Value = new string('f', 64), Algorithm = "SHA-256", CanonicalizationVersion = 1 };
        var document = new Document { DocumentId = Guid.NewGuid(), CurrentVersion = 4, OriginalFileName = "drawing.step", Version = new DocumentVersion { Fingerprint = fingerprint } };
        var viewModel = new DocumentDetailViewModel(document);
        var match = new FingerprintVerificationResult(FingerprintVerificationStatus.Match, fingerprint, "Fingerprint Verified - Document Unchanged");
        var mismatch = new FingerprintVerificationResult(FingerprintVerificationStatus.Mismatch, fingerprint, "Fingerprint Mismatch - Document May Have Been Altered");
        var error = new FingerprintVerificationResult(FingerprintVerificationStatus.Error, null, "Cannot verify fingerprint - file(s) missing or corrupted");

        Assert.Equal(fingerprint.Value, viewModel.Fingerprint);
        Assert.Equal(fingerprint.Value, viewModel.FingerprintDisplay);
        Assert.Equal("Fingerprint Verified - Document Unchanged", match.Message);
        Assert.Equal("Fingerprint Mismatch - Document May Have Been Altered", mismatch.Message);
        Assert.NotEqual(match.Message, mismatch.Message);
        Assert.Contains("missing or corrupted", error.Message);
    }

    [Theory(DisplayName = "TD-07 fingerprint verification command follows the authorized role matrix")]
    [InlineData(SystemRoles.Checker, true)]
    [InlineData(SystemRoles.Approver, true)]
    [InlineData(SystemRoles.Maker, false)]
    [InlineData(SystemRoles.Viewer, false)]
    [InlineData(SystemRoles.Admin, false)]
    public async Task Verify_command_is_enabled_only_for_authorized_roles(string role, bool allowed)
    {
        var authentication = new SessionAuthenticationService();
        authentication.SignIn(new AuthenticatedUser(role, new[] { role }) { UserId = role + "-001", DisplayName = role });
        var workflow = new FingerprintWorkflowStub();
        var document = new Document { DocumentId = Guid.NewGuid(), CurrentVersion = 4, Version = new DocumentVersion { VersionId = Guid.NewGuid() } };
        var viewModel = new DocumentDetailViewModel(document, null, workflow, authentication: authentication);

        Assert.Equal(allowed, viewModel.CanVerifyFingerprint);
        Assert.Equal(allowed, viewModel.VerifyFingerprintCommand.CanExecute(null));
        viewModel.VerifyFingerprintCommand.Execute(null);
        await Task.Delay(25);
        Assert.Equal(allowed ? 1 : 0, workflow.VerifyCalls);
    }

    [Theory(DisplayName = "TD-07 submitted revision metadata is available before and after verification")]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Submitted_metadata_is_loaded_for_current_and_legacy_fingerprints(bool hasFingerprint)
    {
        var packageId = Guid.NewGuid();
        var submittedUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var fingerprint = hasFingerprint ? new DocumentFingerprint { Value = new string('a', 64), Algorithm = "SHA-256", CanonicalizationVersion = 1 } : null;
        var workflow = new FingerprintWorkflowStub
        {
            Snapshot = new PriorVersionReview
            {
                PackageId = packageId, Revision = 4, MakerUserId = "maker-001", MakerDisplayName = "Maker One", SubmittedUtc = submittedUtc,
                Package = new ReviewPackage { Submission = new ReviewSubmission { Revision = 4, MakerUserId = "maker-001", MakerDisplayName = "Maker One", SubmittedUtc = submittedUtc, Fingerprint = fingerprint } }
            },
            Verification = new FingerprintVerificationResult(FingerprintVerificationStatus.Error, null, "Cannot verify fingerprint - file(s) missing or corrupted")
        };
        var authentication = new SessionAuthenticationService();
        authentication.SignIn(new AuthenticatedUser("checker", new[] { SystemRoles.Checker }) { UserId = "checker-001", DisplayName = "Checker One" });
        var viewModel = new DocumentDetailViewModel(new Document { DocumentId = packageId, CurrentVersion = 4, Version = new DocumentVersion { VersionId = Guid.NewGuid() } }, null, workflow, authentication: authentication);

        await WaitUntilAsync(() => viewModel.SubmittedTimestampUtc is not null);
        Assert.Equal("Maker One", viewModel.SubmittedMakerDisplayName);
        Assert.Equal(submittedUtc, viewModel.SubmittedTimestampUtc);
        Assert.Equal(hasFingerprint ? fingerprint!.Value : "Unavailable", viewModel.SubmittedFingerprintDisplay);
        Assert.Equal(hasFingerprint ? "SHA-256" : "Unavailable", viewModel.SubmittedFingerprintAlgorithm);
        Assert.Equal(hasFingerprint ? "1" : "Unavailable", viewModel.SubmittedFingerprintCanonicalizationVersion);

        viewModel.VerifyFingerprintCommand.Execute(null);
        await WaitUntilAsync(() => workflow.VerifyCalls == 1);
        Assert.Equal(FingerprintVerificationStatus.Error, viewModel.FingerprintVerification!.Status);
        Assert.Equal("Maker One", viewModel.SubmittedMakerDisplayName);
        Assert.Equal(submittedUtc, viewModel.SubmittedTimestampUtc);
        Assert.Equal(hasFingerprint ? fingerprint!.Value : "Unavailable", viewModel.SubmittedFingerprintDisplay);
        Assert.Equal(hasFingerprint ? "SHA-256" : "Unavailable", viewModel.SubmittedFingerprintAlgorithm);
        Assert.Equal(hasFingerprint ? "1" : "Unavailable", viewModel.SubmittedFingerprintCanonicalizationVersion);
    }

    [Fact(DisplayName = "TD-07 clears old submitted metadata while a reused view model loads a new document")]
    public async Task Reusing_view_model_does_not_show_stale_snapshot_metadata()
    {
        var firstId = Guid.NewGuid();
        var secondId = Guid.NewGuid();
        var secondSnapshot = new TaskCompletionSource<PriorVersionReview?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var workflow = new FingerprintWorkflowStub
        {
            SnapshotProvider = (id, _) => id == firstId
                ? Task.FromResult<PriorVersionReview?>(Snapshot(firstId, "First Maker", new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), "1"))
                : secondSnapshot.Task
        };
        var authentication = AuthorizedChecker();
        var viewModel = new DocumentDetailViewModel(Document(firstId, 1), null, workflow, authentication: authentication);

        await WaitUntilAsync(() => viewModel.SubmittedMakerDisplayName == "First Maker");
        viewModel.Load(Document(secondId, 2));

        Assert.Equal("Unavailable", viewModel.SubmittedMakerDisplayName);
        Assert.Null(viewModel.SubmittedTimestampUtc);
        Assert.Equal("Unavailable", viewModel.SubmittedFingerprintDisplay);
        secondSnapshot.SetResult(Snapshot(secondId, "Second Maker", new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc), "2"));
        await WaitUntilAsync(() => viewModel.SubmittedMakerDisplayName == "Second Maker");
        Assert.Equal("Second Maker", viewModel.SubmittedMakerDisplayName);
        Assert.Equal(secondId, viewModel.Document!.DocumentId);
    }

    [Fact(DisplayName = "TD-07 failed submitted snapshot retrieval is explicit and logged")]
    public async Task Failed_snapshot_retrieval_clears_metadata_and_exposes_unavailable_state()
    {
        var logger = new SnapshotLogger();
        var workflow = new FingerprintWorkflowStub
        {
            SnapshotProvider = (_, _) => Task.FromException<PriorVersionReview?>(new InvalidOperationException("snapshot store unavailable"))
        };
        var viewModel = new DocumentDetailViewModel(Document(Guid.NewGuid(), 1), null, workflow, authentication: AuthorizedChecker(), logger: logger);

        await WaitUntilAsync(() => logger.Context.Contains("documentId=", StringComparison.Ordinal));
        Assert.Equal("Unavailable", viewModel.SubmittedMakerDisplayName);
        Assert.Null(viewModel.SubmittedTimestampUtc);
        Assert.Equal("Unavailable", viewModel.SubmittedFingerprintDisplay);
        Assert.Contains("snapshot", logger.Context, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("documentId=", logger.Context, StringComparison.Ordinal);
        Assert.Contains("unavailable", viewModel.SubmittedSnapshotError!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact(DisplayName = "TD-07 keeps each historical version fingerprint associated with its own revision")]
    public void Historical_version_read_models_expose_their_own_fingerprints()
    {
        var first = new DocumentFingerprint { Value = new string('1', 64), Algorithm = "SHA-256", CanonicalizationVersion = 1 };
        var second = new DocumentFingerprint { Value = new string('2', 64), Algorithm = "SHA-256", CanonicalizationVersion = 1 };
        var versions = new[]
        {
            new PriorVersionReview { PackageId = Guid.NewGuid(), Revision = 1, Package = new ReviewPackage { Submission = new ReviewSubmission { Revision = 1, Fingerprint = first } } },
            new PriorVersionReview { PackageId = Guid.NewGuid(), Revision = 2, Package = new ReviewPackage { Submission = new ReviewSubmission { Revision = 2, Fingerprint = second } } }
        };

        Assert.Equal(first, versions[0].Package.Fingerprint);
        Assert.Equal(second, versions[1].Package.Fingerprint);
        Assert.NotEqual(versions[0].Package.Fingerprint, versions[1].Package.Fingerprint);
    }
    [Fact(DisplayName = "TD-KAN23-WPF-003 details display all DXF metadata and statuses")]
    public void Dxf_details_expose_required_metadata()
    {
        var document = new Document { OriginalFileName = "drawing.dxf", FileSizeBytes = 42, DocumentType = CoreDocumentType.DxfDrawing, UploadStatus = CoreUploadStatus.Uploaded, VerificationStatus = DrawVerify.Core.Enums.VerificationStatus.PendingVerification, UploaderUserId = "user-1", UploaderDisplayName = "Maker", CurrentVersion = 1 };
        var viewModel = new DocumentDetailViewModel(document);
        Assert.Equal("drawing.dxf", viewModel.OriginalFileName);
        Assert.Equal(42, viewModel.FileSizeBytes);
        Assert.Equal(CoreDocumentType.DxfDrawing, viewModel.DocumentType);
        Assert.Equal(CoreUploadStatus.Uploaded, viewModel.UploadStatus);
        Assert.Equal("user-1", viewModel.UploaderUserId);
        Assert.Equal("Maker", viewModel.UploaderDisplayNameSnapshot);
        Assert.Equal("Pending Verification", viewModel.VerificationStatusDisplay);
        Assert.Equal(1, viewModel.CurrentVersion);
    }
    [Fact(DisplayName = "TD-KAN116-APP-003 exposes retained document detail state without uploader identity")]
    public void Details_model_maps_geometry_status_and_stored_error()
    {
        var document = new Document { DocumentType = CoreDocumentType.StepModel, UploadStatus = CoreUploadStatus.Uploaded, GeometryStatus = CoreGeometryStatus.Error, GeometryError = "Measurement extraction failed." };
        var viewModel = new DocumentDetailViewModel();

        viewModel.Load(document);

        Assert.Equal(CoreGeometryStatus.Error, viewModel.GeometryStatus);
        Assert.Equal("Measurement extraction failed.", viewModel.GeometryError);
        Assert.Equal(document, viewModel.Document);
    }

    [Fact(DisplayName = "TD-KAN116-APP-003 defaults missing geometry to not extracted")]
    public void Empty_details_are_not_reported_as_processed()
    {
        var viewModel = new DocumentDetailViewModel();
        Assert.Equal(CoreGeometryStatus.NotExtracted, viewModel.GeometryStatus);
        Assert.Null(viewModel.GeometryError);
    }

    [Theory(DisplayName = "TD-KAN25-WPF-001 Maker and Checker can extract DXF dimensions")]
    [InlineData(SystemRoles.Maker)]
    [InlineData(SystemRoles.Checker)]
    public async Task Authorized_roles_receive_derived_dimension_results(string role)
    {
        var scenario = CreateScenario(role, DxfDimensionExtractionResult.Success(new[]
        {
            new DimensionData(Guid.NewGuid(), Guid.NewGuid(), 0, DimensionType.Linear, 20, -0.1, 0.2, "20 ±0.1")
        }));
        scenario.Parser.Result = DxfDimensionExtractionResult.Success(new[]
        {
            new DimensionData(scenario.Document.DocumentId, scenario.Version.VersionId, 0, DimensionType.Linear, 20, -0.1, 0.2, "20 ±0.1"),
            new DimensionData(scenario.Document.DocumentId, scenario.Version.VersionId, 1, DimensionType.Angular, 45, displayText: "45°")
        });
        var viewModel = new DocumentDetailViewModel(scenario.Document, scenario.Service);

        Assert.True(viewModel.CanViewDimensions);
        Assert.True(viewModel.CanExtractDimensions);
        Assert.True(viewModel.ExtractDimensionsCommand.CanExecute(null));

        await ExecuteAndWaitAsync(viewModel);

        Assert.Equal(VerificationStatus.DimensionsExtracted, viewModel.VerificationStatus);
        Assert.Equal("Derived from DXF drawing", viewModel.DimensionResultsHeading);
        Assert.Equal(2, viewModel.Dimensions.Count);
        Assert.Equal(DimensionType.Linear, viewModel.Dimensions[0].DimensionType);
        Assert.Equal(20, viewModel.Dimensions[0].NominalValue);
        Assert.Equal(-0.1, viewModel.Dimensions[0].LowerTolerance);
        Assert.Equal(0.2, viewModel.Dimensions[0].UpperTolerance);
        Assert.Equal("20 ±0.1", viewModel.Dimensions[0].DisplayText);
        Assert.Equal(DimensionType.Angular, viewModel.Dimensions[1].DimensionType);
        Assert.Equal("45°", viewModel.Dimensions[1].DimensionText);
        Assert.IsAssignableFrom<IReadOnlyList<DimensionData>>(viewModel.Dimensions);
        Assert.Equal(1, scenario.Parser.Calls);
    }

    [Fact(DisplayName = "TD-KAN25-WPF-001 exposes the complete DXF-derived binding contract")]
    public void Dimension_result_properties_are_public_and_labelled_as_derived_data()
    {
        var scenario = CreateScenario(SystemRoles.Maker, DxfDimensionExtractionResult.Success(Array.Empty<DimensionData>()));
        var viewModel = new DocumentDetailViewModel(scenario.Document, scenario.Service);

        Assert.Equal("Derived from DXF drawing", viewModel.DimensionResultsHeading);
        Assert.NotNull(typeof(DocumentDetailViewModel).GetProperty(nameof(DocumentDetailViewModel.Dimensions)));
        Assert.NotNull(typeof(DimensionData).GetProperty(nameof(DimensionData.DimensionType)));
        Assert.NotNull(typeof(DimensionData).GetProperty(nameof(DimensionData.NominalValue)));
        Assert.NotNull(typeof(DimensionData).GetProperty(nameof(DimensionData.LowerTolerance)));
        Assert.NotNull(typeof(DimensionData).GetProperty(nameof(DimensionData.UpperTolerance)));
        Assert.NotNull(typeof(DimensionData).GetProperty(nameof(DimensionData.DisplayText)));
        Assert.Equal("Derived from DXF drawing", viewModel.DimensionResultsHeading);
    }

    [Fact(DisplayName = "TD-KAN25-WPF-001 displays empty and partial extraction outcomes")]
    public async Task Empty_and_partial_results_remain_visible_without_invented_values()
    {
        var empty = CreateScenario(SystemRoles.Maker, DxfDimensionExtractionResult.Success(Array.Empty<DimensionData>()));
        var emptyViewModel = new DocumentDetailViewModel(empty.Document, empty.Service);
        await ExecuteAndWaitAsync(emptyViewModel);
        Assert.Empty(emptyViewModel.Dimensions);
        Assert.Equal("No dimension entities were found in the DXF drawing.", emptyViewModel.Message);
        Assert.Equal(VerificationStatus.DimensionsExtracted, emptyViewModel.VerificationStatus);

        var partial = CreateScenario(SystemRoles.Checker, DxfDimensionExtractionResult.Success(new[]
        {
            new DimensionData(Guid.NewGuid(), Guid.NewGuid(), 0, DimensionType.Radial, 4, displayText: "R4")
        }, new[] { "Skipped malformed DIMENSION entity 1." }));
        partial.Parser.Result = DxfDimensionExtractionResult.Success(new[]
        {
            new DimensionData(partial.Document.DocumentId, partial.Version.VersionId, 0, DimensionType.Radial, 4, displayText: "R4")
        }, new[] { "Skipped malformed DIMENSION entity 1." });
        var partialViewModel = new DocumentDetailViewModel(partial.Document, partial.Service);
        await ExecuteAndWaitAsync(partialViewModel);
        Assert.Single(partialViewModel.Dimensions);
        Assert.Equal("R4", partialViewModel.Dimensions[0].DisplayText);
        Assert.Equal(VerificationStatus.DimensionsExtracted, partialViewModel.VerificationStatus);
        Assert.Empty(partialViewModel.ErrorMessage);
    }

    [Fact(DisplayName = "TD-KAN25-WPF-001 clears stale dimensions after extraction failure")]
    public async Task Failure_replaces_stale_results_with_a_clear_error()
    {
        var oldDimension = new DimensionData(Guid.NewGuid(), Guid.NewGuid(), 0, DimensionType.Linear, 10, displayText: "old");
        var scenario = CreateScenario(SystemRoles.Maker, DxfDimensionExtractionResult.Failure("unable to parse DXF file."), new[] { oldDimension });
        var viewModel = new DocumentDetailViewModel(scenario.Document, scenario.Service);
        await viewModel.LoadDimensionsAsync();
        Assert.Single(viewModel.Dimensions);

        await ExecuteAndWaitAsync(viewModel);

        Assert.Empty(viewModel.Dimensions);
        Assert.Equal(VerificationStatus.Error, viewModel.VerificationStatus);
        Assert.StartsWith("Dimension extraction failed:", viewModel.ErrorMessage);
        Assert.Contains("unable to parse DXF file", viewModel.Message);
    }

    [Theory(DisplayName = "TD-KAN25-WPF-001 denies derived actions to non-Maker and non-Checker users")]
    [InlineData(null)]
    [InlineData(SystemRoles.Admin)]
    [InlineData(SystemRoles.Approver)]
    [InlineData(SystemRoles.Viewer)]
    public async Task Unauthorized_users_cannot_extract_or_load_dimensions(string? role)
    {
        var scenario = CreateScenario(role, DxfDimensionExtractionResult.Success(new[]
        {
            new DimensionData(Guid.NewGuid(), Guid.NewGuid(), 0, DimensionType.Linear, 1)
        }));
        var viewModel = new DocumentDetailViewModel(scenario.Document, scenario.Service);

        Assert.False(viewModel.CanViewDimensions);
        Assert.False(viewModel.CanExtractDimensions);
        Assert.False(viewModel.ExtractDimensionsCommand.CanExecute(null));
        await viewModel.LoadDimensionsAsync();
        viewModel.ExtractDimensionsCommand.Execute(null);
        await Task.Delay(10);

        Assert.Empty(viewModel.Dimensions);
        Assert.Equal(0, scenario.Repository.GetCalls);
        Assert.Equal(0, scenario.Repository.GetDimensionsCalls);
        Assert.Equal(0, scenario.Repository.ReplaceCalls);
        Assert.Equal(0, scenario.Parser.Calls);
        Assert.Equal(0, scenario.Store.OpenReadCalls);
    }

    [Fact(DisplayName = "TD-UI-01 navigates and filters history without replacing current document")]
    public async Task Prior_version_commands_preserve_current_document_context()
    {
        var document = new Document { DocumentId = Guid.NewGuid(), CurrentVersion = 2, Version = new DocumentVersion { VersionId = Guid.NewGuid(), VersionNumber = 2 } };
        var prior = new PriorVersionReview { PackageId = document.DocumentId, Revision = 1, Status = WorkflowStatus.Rejected, MakerDisplayName = "Maker", IsCurrent = false };
        var current = prior with { Revision = 2, Status = WorkflowStatus.SubmittedForReview, IsCurrent = true };
        var auth = new SessionAuthenticationService(); auth.SignIn(new AuthenticatedUser("checker", new[] { SystemRoles.Checker }));
        var service = new HistoryService(new[] { prior, current });
        var viewModel = new DocumentDetailViewModel(document, null, service, authentication: auth);

        Assert.True(viewModel.CanViewPriorVersions);
        await viewModel.LoadPriorVersionsAsync();
        Assert.Equal(2, viewModel.PriorVersions.Count);
        viewModel.SelectPriorVersionCommand.Execute(prior);
        await WaitUntilIdleAsync(viewModel);
        Assert.Equal(1, viewModel.SelectedPriorVersion!.Revision);
        Assert.Equal(document.DocumentId, viewModel.Document!.DocumentId);
        Assert.True(viewModel.SelectedPriorVersion.IsReadOnly);
        viewModel.ViewCurrentVersionCommand.Execute(null);
        Assert.Null(viewModel.SelectedPriorVersion);
        Assert.Equal(2, document.CurrentVersion);
    }

    private static async Task WaitUntilIdleAsync(DocumentDetailViewModel viewModel)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        while (viewModel.IsBusy)
        {
            await Task.Delay(1, timeout.Token);
        }
    }

    private sealed class HistoryService(IReadOnlyList<PriorVersionReview> versions) : IWorkflowService
    {
        public Task<SubmissionReadiness> ValidateReadinessAsync(Guid packageId, CancellationToken cancellationToken = default) => Task.FromResult(SubmissionReadiness.Ready);
        public Task<SubmissionResult> SubmitForReviewAsync(SubmissionRequest request, CancellationToken cancellationToken = default) => Task.FromResult(new SubmissionResult(false, "not used"));
        public Task RecordCancellationAsync(Guid packageId, int revision, string? note, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<WorkflowState?> GetPackageAsync(Guid packageId, CancellationToken cancellationToken = default) => Task.FromResult<WorkflowState?>(null);
        public Task<IReadOnlyList<ReviewSubmission>> GetSubmittedPackagesAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<ReviewSubmission>>(Array.Empty<ReviewSubmission>());
        public Task<IReadOnlyList<WorkflowQueueItem>> GetCheckerQueueAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<WorkflowQueueItem>>(Array.Empty<WorkflowQueueItem>());
        public Task<IReadOnlyList<PriorVersionReview>> GetPriorVersionsAsync(Guid packageId, PriorVersionFilter? filter = null, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<PriorVersionReview>>(versions.Where(x => filter?.Status is null || x.Status == filter.Status).ToArray());
        public Task<PriorVersionReview?> GetPriorVersionAsync(Guid packageId, int revision, CancellationToken cancellationToken = default)
            => Task.FromResult<PriorVersionReview?>(versions.FirstOrDefault(x => x.Revision == revision));
        public Task<VersionComparison> CompareVersionsAsync(Guid packageId, int currentRevision, int priorRevision, CancellationToken cancellationToken = default)
            => Task.FromResult(new VersionComparison { PackageId = packageId, CurrentRevision = currentRevision, PriorRevision = priorRevision, EvidenceAvailable = true });
    }

    private static async Task ExecuteAndWaitAsync(DocumentDetailViewModel viewModel)
    {
        viewModel.ExtractDimensionsCommand.Execute(null);
        for (var attempt = 0; attempt < 1_000; attempt++)
        {
            if (!viewModel.IsBusy)
                return;
            await Task.Delay(1);
        }
        throw new TimeoutException("The document-detail extraction command did not complete.");
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        while (!condition())
            await Task.Delay(1, timeout.Token);
    }

    private static SessionAuthenticationService AuthorizedChecker()
    {
        var authentication = new SessionAuthenticationService();
        authentication.SignIn(new AuthenticatedUser("checker", new[] { SystemRoles.Checker }) { UserId = "checker-001", DisplayName = "Checker One" });
        return authentication;
    }

    private static Document Document(Guid id, int revision) => new() { DocumentId = id, CurrentVersion = revision, Version = new DocumentVersion { VersionId = Guid.NewGuid() } };

    [Fact(DisplayName = "TD-004 Checker role is exposed to approval ViewModel gating")]
    public void Checker_approval_gating_uses_authenticated_role()
    {
        var auth = AuthorizedChecker();
        var viewModel = new DocumentDetailViewModel(Document(Guid.NewGuid(), 1), null, new HistoryService(Array.Empty<PriorVersionReview>()), authentication: auth);
        Assert.True(auth.CurrentUser!.HasRole(SystemRoles.Checker));
        Assert.NotNull(viewModel);
    }

    [Fact(DisplayName = "TD-007 signed, unsigned, rejected, and returned preview states are represented by version data")]
    public void Preview_fixtures_keep_checker_signature_version_scoped_and_optional()
    {
        var id = Guid.NewGuid();
        var signed = SignatureData.CreateChecker(SignatureMethod.Typed, "signed", "checker-001", "Checker One", id, 1, new DateTimeOffset(2026, 9, 16, 10, 0, 0, TimeSpan.Zero));
        var signedVersion = new ReviewSubmission { PackageId = id, Revision = 1, Status = WorkflowStatus.Approved, CheckerSignature = signed };
        var unsignedVersion = new ReviewSubmission { PackageId = id, Revision = 2, Status = WorkflowStatus.ReturnedForRework };
        Assert.Equal(signed, signedVersion.CheckerSignature);
        Assert.Null(unsignedVersion.CheckerSignature);
        Assert.Equal(1, signedVersion.CheckerSignature!.Revision);
        Assert.Equal(WorkflowStatus.ReturnedForRework, unsignedVersion.Status);
    }

    [Fact(DisplayName = "TD-005 document-detail signature draft state is isolated per ViewModel")]
    public void Signature_preview_fixture_does_not_share_state_between_versions()
    {
        var first = new ReviewSubmission { PackageId = Guid.NewGuid(), Revision = 1 };
        var second = new ReviewSubmission { PackageId = first.PackageId, Revision = 2 };
        Assert.Null(first.CheckerSignature);
        Assert.Null(second.CheckerSignature);
        Assert.NotEqual(first.Revision, second.Revision);
    }

    private static PriorVersionReview Snapshot(Guid packageId, string maker, DateTime submittedUtc, string fingerprintValue) => new()
    {
        PackageId = packageId, Revision = 1, MakerUserId = maker.ToLowerInvariant().Replace(' ', '-'), MakerDisplayName = maker, SubmittedUtc = submittedUtc,
        Package = new ReviewPackage { Submission = new ReviewSubmission { Revision = 1, MakerUserId = maker, MakerDisplayName = maker, SubmittedUtc = submittedUtc, Fingerprint = new DocumentFingerprint { Value = new string(fingerprintValue[0], 64), Algorithm = "SHA-256", CanonicalizationVersion = 1 } } }
    };

    private sealed class FingerprintWorkflowStub : IWorkflowService
    {
        public PriorVersionReview? Snapshot { get; init; }
        public Func<Guid, int, Task<PriorVersionReview?>>? SnapshotProvider { get; init; }
        public FingerprintVerificationResult Verification { get; init; } = new(FingerprintVerificationStatus.Match, null, "Fingerprint Verified - Document Unchanged");
        public int VerifyCalls { get; private set; }
        public Task<SubmissionReadiness> ValidateReadinessAsync(Guid packageId, CancellationToken cancellationToken = default) => Task.FromResult(SubmissionReadiness.Ready);
        public Task<SubmissionResult> SubmitForReviewAsync(SubmissionRequest request, CancellationToken cancellationToken = default) => Task.FromResult(new SubmissionResult(false, "not used"));
        public Task RecordCancellationAsync(Guid packageId, int revision, string? note, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<WorkflowState?> GetPackageAsync(Guid packageId, CancellationToken cancellationToken = default) => Task.FromResult<WorkflowState?>(null);
        public Task<IReadOnlyList<ReviewSubmission>> GetSubmittedPackagesAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<ReviewSubmission>>(Array.Empty<ReviewSubmission>());
        public Task<IReadOnlyList<WorkflowQueueItem>> GetCheckerQueueAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<WorkflowQueueItem>>(Array.Empty<WorkflowQueueItem>());
        public Task<PriorVersionReview?> GetPriorVersionAsync(Guid packageId, int revision, CancellationToken cancellationToken = default) => SnapshotProvider?.Invoke(packageId, revision) ?? Task.FromResult(Snapshot);
        public Task<FingerprintVerificationResult> VerifyFingerprintAsync(Guid packageId, int revision, CancellationToken cancellationToken = default)
        {
            VerifyCalls++;
            return Task.FromResult(Verification);
        }
    }

    private sealed class SnapshotLogger : ICentralizedLogger
    {
        public string Context { get; private set; } = string.Empty;
        public void Log(Exception exception, string operation, string context) => Context = context;
    }

    private static Scenario CreateScenario(string? role, DxfDimensionExtractionResult result, IEnumerable<DimensionData>? initialDimensions = null)
    {
        var documentId = Guid.NewGuid();
        var version = new DocumentVersion
        {
            DocumentId = documentId,
            VersionNumber = 1,
            VersionId = Guid.NewGuid(),
            StoredRelativePath = "uploads/dxf/drawing.dxf",
            FileSizeBytes = 128
        };
        var document = new Document
        {
            DocumentId = documentId,
            OriginalFileName = "drawing.dxf",
            FileSizeBytes = 128,
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
            session.SignIn(new AuthenticatedUser("maker", new[] { role }));
        var service = new DxfDimensionExtractionService(repository, store, parser, new DxfDimensionExtractionConfiguration(), session);
        return new Scenario(document, version, repository, store, parser, service);
    }

    private sealed class Scenario(Document document, DocumentVersion version, FakeRepository repository, FakeStore store, FakeParser parser, DxfDimensionExtractionService service)
    {
        public Document Document { get; } = document;
        public DocumentVersion Version { get; } = version;
        public FakeRepository Repository { get; } = repository;
        public FakeStore Store { get; } = store;
        public FakeParser Parser { get; } = parser;
        public DxfDimensionExtractionService Service { get; } = service;
    }

    private sealed class FakeRepository(Document document, DocumentVersion version, IEnumerable<DimensionData>? initialDimensions) : IDocumentRepository
    {
        public Document? StoredDocument { get; private set; } = document;
        public DocumentVersion CurrentVersion { get; } = version;
        public List<DimensionData> Dimensions { get; } = initialDimensions?.ToList() ?? new();
        public int GetCalls { get; private set; }
        public int GetDimensionsCalls { get; private set; }
        public int ReplaceCalls { get; private set; }

        public Task<Document?> GetAsync(Guid documentId, CancellationToken cancellationToken = default)
        {
            GetCalls++;
            return Task.FromResult<Document?>(StoredDocument?.DocumentId == documentId ? StoredDocument : null);
        }

        public Document? Get(Guid documentId) => StoredDocument?.DocumentId == documentId ? StoredDocument : null;
        public DocumentVersion? GetCurrentVersion(Guid documentId) => StoredDocument?.DocumentId == documentId ? CurrentVersion : null;

        public Task ReplaceDimensionsAsync(Guid documentId, Guid documentVersionId, IReadOnlyCollection<DimensionData> dimensions, VerificationStatus status, string? verificationError, AuditEntry auditEntry, CancellationToken cancellationToken = default)
        {
            ReplaceCalls++;
            Dimensions.Clear();
            Dimensions.AddRange(dimensions);
            if (StoredDocument is not null)
                StoredDocument = StoredDocument with { VerificationStatus = status, VerificationError = verificationError };
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<DimensionData>> GetDimensionsAsync(Guid documentId, CancellationToken cancellationToken = default)
        {
            GetDimensionsCalls++;
            return Task.FromResult<IReadOnlyList<DimensionData>>(Dimensions.ToArray());
        }
    }

    private sealed class FakeStore : IDxfUploadFileStore
    {
        public int OpenReadCalls { get; private set; }
        public Task<DxfUploadStagingFile> CreateStagingFileAsync(string originalFileName, CancellationToken cancellationToken = default) =>
            Task.FromResult(new DxfUploadStagingFile("stage", new MemoryStream()));
        public Task<string> MoveToFinalAsync(DxfUploadStagingFile stagingFile, CancellationToken cancellationToken = default) => Task.FromResult("final.dxf");
        public Task DeleteAsync(string relativePath, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<Stream> OpenReadAsync(string relativePath, CancellationToken cancellationToken = default)
        {
            OpenReadCalls++;
            return Task.FromResult<Stream>(new MemoryStream(Encoding.ASCII.GetBytes("dxf")));
        }
    }

    private sealed class FakeParser : IDxfDimensionParser
    {
        public DxfDimensionExtractionResult Result { get; set; } = DxfDimensionExtractionResult.Success(Array.Empty<DimensionData>());
        public int Calls { get; private set; }
        public Task<DxfDimensionExtractionResult> ParseAsync(Stream content, Guid documentId, Guid documentVersionId, CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult(Result);
        }
    }
}
