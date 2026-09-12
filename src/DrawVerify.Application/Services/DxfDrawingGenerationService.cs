using System.Text.Json;
using DrawVerify.Application.DxfUpload;
using DrawVerify.Core.Authorization;
using DrawVerify.Core.Enums;
using DrawVerify.Core.Interfaces;
using DrawVerify.Core.Models;
using DrawVerify.Geometry;
using DrawVerify.Geometry.Models;

namespace DrawVerify.Application.Services;

public sealed class DxfDrawingGenerationService : IDxfDrawingGenerationService
{
    private readonly IDocumentRepository _documents;
    private readonly ITransactionalDocumentRepository _transactionalDocuments;
    private readonly IStepUploadFileStore _stepFiles;
    private readonly IDxfUploadFileStore _dxfFiles;
    private readonly IProjectionDrawingService _projection;
    private readonly IAuditLogger _audit;
    private readonly ISessionAuthenticationService _session;
    private readonly RoleAuthorization _authorization;
    private readonly ICentralizedLogger? _logger;
    private readonly IUserRepository? _users;
    private readonly DxfDimensionExtractionService? _extraction;

    public DxfDrawingGenerationService(IDocumentRepository documents, IStepUploadFileStore stepFiles, IDxfUploadFileStore dxfFiles, IProjectionDrawingService projection, IAuditLogger audit, ISessionAuthenticationService session, RoleAuthorization authorization, ICentralizedLogger? logger = null, IUserRepository? users = null, DxfDimensionExtractionService? extraction = null)
    { _documents = documents ?? throw new ArgumentNullException(nameof(documents)); _transactionalDocuments = documents as ITransactionalDocumentRepository ?? throw new ArgumentException("The document repository must support transactions.", nameof(documents)); _stepFiles = stepFiles; _dxfFiles = dxfFiles; _projection = projection; _audit = audit; _session = session; _authorization = authorization; _logger = logger; _users = users; _extraction = extraction; }
    public bool CanGenerate => _session.IsAuthenticated && _authorization.IsAllowed(_session.CurrentUser, RolePermission.GenerateDxfDrawing);

    public async Task<DxfDrawingGenerationResult> GenerateAsync(Guid stepDocumentId, CancellationToken cancellationToken = default, IProgress<DxfDrawingGenerationProgress>? progress = null)
    {
        try { await RefreshAuthorizationAsync(cancellationToken).ConfigureAwait(false); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { ReportCancelled(progress, "2D drawing generation was cancelled."); throw; }
        catch (Exception exception) { ReportFailed(progress, $"2D drawing generation failed: {exception.Message}"); _logger?.Log(exception, "DXF drawing generation authorization", $"stepDocumentId={stepDocumentId}"); throw; }
        if (!CanGenerate) return Failure("Only an authenticated Maker can generate a 2D drawing.", progress: progress);
        Document? step;
        DocumentVersion? version;
        try
        {
            step = await _documents.GetAsync(stepDocumentId, cancellationToken).ConfigureAwait(false);
            version = step is null ? null : _documents.GetCurrentVersion(stepDocumentId) ?? step.Version;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { ReportCancelled(progress, "2D drawing generation was cancelled."); throw; }
        catch (Exception exception) { ReportFailed(progress, $"2D drawing generation failed: {exception.Message}"); _logger?.Log(exception, "DXF drawing generation document lookup", $"stepDocumentId={stepDocumentId}"); return Failure($"2D drawing generation failed: {exception.Message}", progress: progress); }
        if (step is null || version is null || step.DocumentType != DrawVerify.Core.Enums.DocumentType.StepModel || step.GeometryStatus != DrawVerify.Core.Enums.GeometryStatus.Processed) return Failure("A successfully processed STEP model is required before generating a 2D drawing.", progress: progress);
        var tempPath = Path.Combine(Path.GetTempPath(), $"drawverify-{Guid.NewGuid():N}.step");
        DxfUploadStagingFile? staging = null;
        string? finalPath = null;
        Guid? generatedDocumentId = null;
        var diagnostics = new List<string>();
        try
        {
            await WriteAuditAsync(stepDocumentId, DocumentAuditAction.DxfGenerationStarted, new { stepVersionId = version.VersionId }, cancellationToken).ConfigureAwait(false);
            progress?.Report(new("reading", 5, "Preparing the processed STEP model…"));
            await using (var source = await _stepFiles.OpenReadAsync(version.StoredRelativePath, cancellationToken).ConfigureAwait(false))
            await using (var target = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous))
                await source.CopyToAsync(target, cancellationToken).ConfigureAwait(false);
            var drawings = new List<ProjectionDrawing>();
            var views = Enum.GetValues<ProjectionDrawingView>();
            for (var index = 0; index < views.Length; index++) { cancellationToken.ThrowIfCancellationRequested(); progress?.Report(new("projection", 10 + index * 12, $"Generating {views[index]} view…")); var drawing = _projection.Generate(tempPath, views[index], cancellationToken); drawings.Add(drawing); diagnostics.AddRange(drawing.Diagnostics.Select(message => $"{drawing.View}: {message}")); }
            if (diagnostics.Count > 0)
                progress?.Report(new("projection-warning", 82, $"Projection completed with {diagnostics.Count} warning(s); unsupported entities were omitted."));
            progress?.Report(new("encoding", 82, "Encoding the supported drawing entities…"));
            var bytes = DxfFileWriter.Write(drawings);
            if (_extraction is null) throw new InvalidOperationException("The canonical DXF extraction service is not configured; generated output cannot become verification-ready.");
            await using var content = new MemoryStream(bytes, writable: false);
            var validation = await DxfFileValidator.ValidateAsync(content, bytes.Length, 30, cancellationToken).ConfigureAwait(false);
            if (!validation.IsValid) return Failure($"Generated DXF validation failed: {validation.Reason}", diagnostics, progress);
            content.Position = 0;
            var preflight = await _extraction.ParseAsync(content, Guid.Empty, Guid.Empty, cancellationToken).ConfigureAwait(false);
            if (!preflight.Succeeded || preflight.Dimensions.Count == 0)
                throw new InvalidDataException(preflight.ErrorMessage ?? "Generated DXF contains no supported dimensions.");
            diagnostics.AddRange(preflight.Warnings.Select(warning => $"DXF preflight: {warning}"));
            await WriteAuditAsync(stepDocumentId, DocumentAuditAction.DimensionsExtracted, new { stage = "preflight", dimensionCount = preflight.Dimensions.Count, warningCount = diagnostics.Count, diagnostics }, cancellationToken).ConfigureAwait(false);
            staging = await _dxfFiles.CreateStagingFileAsync($"{Path.GetFileNameWithoutExtension(step.OriginalFileName)}-generated.dxf", cancellationToken).ConfigureAwait(false);
            await staging.Stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
            await staging.DisposeAsync().ConfigureAwait(false);
            finalPath = await _dxfFiles.MoveToFinalAsync(staging, cancellationToken).ConfigureAwait(false); staging = null;
            await WriteAuditAsync(stepDocumentId, DocumentAuditAction.DxfValidationCompleted, new { stage = "structural", byteCount = bytes.Length, warningCount = diagnostics.Count, diagnostics }, cancellationToken).ConfigureAwait(false);
            var documentId = Guid.NewGuid(); generatedDocumentId = documentId; var now = DateTime.UtcNow; var documentVersion = new DocumentVersion { DocumentId = documentId, VersionNumber = 1, StoredRelativePath = finalPath, FileSizeBytes = bytes.Length, CreatedUtc = now, DrawingSource = DrawingSource.Generated };
            var document = new Document { DocumentId = documentId, OriginalFileName = $"{Path.GetFileNameWithoutExtension(step.OriginalFileName)}-generated.dxf", FileSizeBytes = bytes.Length, UploadTimestampUtc = now, DocumentType = DrawVerify.Core.Enums.DocumentType.DxfDrawing, UploadStatus = DrawVerify.Core.Enums.UploadStatus.Uploaded, VerificationStatus = VerificationStatus.PendingVerification, DrawingSource = DrawingSource.Generated, Version = documentVersion, Versions = new[] { documentVersion }, UploaderUserId = _session.CurrentUser?.UserId, UploaderDisplayName = _session.CurrentUser?.DisplayName };
            await _transactionalDocuments.CreateWithVersionAndAuditAsync(document, documentVersion, new AuditEntry { DocumentId = documentId, UserId = _session.CurrentUser?.UserId ?? "Anonymous", Action = DocumentAuditAction.Uploaded, DetailsJson = JsonSerializer.Serialize(new { source = DrawingSource.Generated.ToString(), stepDocumentId, stepVersionId = version.VersionId }) }, cancellationToken).ConfigureAwait(false);
            var extracted = await _extraction.ExtractDimensionsAsync(documentId, cancellationToken).ConfigureAwait(false);
            diagnostics.AddRange(extracted.Warnings.Select(warning => $"DXF extraction: {warning}"));
            if (!extracted.Succeeded || extracted.Dimensions.Count == 0)
                throw new InvalidDataException(extracted.Message.Length == 0 ? "Generated DXF contains no supported dimensions." : extracted.Message);
            document = (extracted.Document ?? document) with { DrawingSource = DrawingSource.Generated, Version = documentVersion };
            await WriteAuditAsync(documentId, DocumentAuditAction.DimensionsExtracted, new { versionId = documentVersion.VersionId, dimensionCount = extracted.Dimensions.Count, warningCount = diagnostics.Count, diagnostics }, cancellationToken).ConfigureAwait(false);
            await WriteAuditAsync(documentId, DocumentAuditAction.DxfGenerationCompleted, new { versionId = documentVersion.VersionId, entityCount = drawings.Sum(drawing => drawing.EntityCount), dimensionCount = drawings.Sum(drawing => drawing.DimensionCount), warningCount = diagnostics.Count, diagnostics }, cancellationToken).ConfigureAwait(false);
            var message = diagnostics.Count == 0 ? "2D drawing generated successfully." : $"2D drawing generated with warnings ({diagnostics.Count} projected item(s) omitted or flagged).";
            progress?.Report(new("complete", 100, message));
            return new(true, message, document, diagnostics, documentVersion.VersionId) { ExtractedDimensionCount = (await _documents.GetDimensionsAsync(documentId, cancellationToken).ConfigureAwait(false)).Count };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { await TryRollbackAsync(generatedDocumentId); if (finalPath is not null) await TryDeleteAsync(finalPath); ReportCancelled(progress, "2D drawing generation was cancelled."); return Failure("2D drawing generation was cancelled.", diagnostics, progress, reportTerminal: false); }
        catch (Exception exception) { await TryRollbackAsync(generatedDocumentId); if (finalPath is not null) await TryDeleteAsync(finalPath); _logger?.Log(exception, "DXF drawing generation", $"stepDocumentId={stepDocumentId}; stepVersionId={version?.VersionId}; diagnostics={JsonSerializer.Serialize(diagnostics)}"); try { await WriteAuditAsync(stepDocumentId, DocumentAuditAction.DxfGenerationFailed, new { error = exception.Message, diagnostics }, CancellationToken.None); } catch { } ReportFailed(progress, $"2D drawing generation failed: {exception.Message}"); return Failure($"2D drawing generation failed: {exception.Message}", diagnostics, progress, reportTerminal: false); }
        finally { if (staging is not null) await staging.DisposeAsync().ConfigureAwait(false); try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { } }
    }
    public async Task<Stream> DownloadAsync(Guid documentId, Guid versionId, CancellationToken cancellationToken = default)
    {
        await RefreshAuthorizationAsync(cancellationToken).ConfigureAwait(false);
        if (!CanGenerate) throw new UnauthorizedAccessException("Only an authenticated Maker can download drawing files.");
        var document = await _documents.GetAsync(documentId, cancellationToken).ConfigureAwait(false) ?? throw new KeyNotFoundException("The drawing document was not found.");
        var version = document.Versions.FirstOrDefault(candidate => candidate.VersionId == versionId) ?? throw new InvalidOperationException("The selected drawing version was not found.");
        var stream = await _dxfFiles.OpenReadAsync(version.StoredRelativePath, cancellationToken).ConfigureAwait(false);
        try { await WriteAuditAsync(documentId, DocumentAuditAction.DxfDownloaded, new { versionId }, cancellationToken).ConfigureAwait(false); return stream; }
        catch { await stream.DisposeAsync().ConfigureAwait(false); throw; }
    }
    private async Task RefreshAuthorizationAsync(CancellationToken cancellationToken)
    {
        var current = _session.CurrentUser;
        if (current is null || _users is null) return;
        var account = await _users.FindByUsernameAsync(current.Username, cancellationToken).ConfigureAwait(false);
        if (account is null || !account.IsActive) throw new UnauthorizedAccessException("The authenticated user is no longer active.");
        _session.ReplaceCurrentUserRoles(current.Username, account.Roles, account.RoleVersion);
        if (_session is SessionAuthenticationService concrete) concrete.SignIn(new AuthenticatedUser(account.Username, account.Roles, account.RoleVersion) { UserId = account.Id.ToString(), DisplayName = account.DisplayName });
    }
    private async Task WriteAuditAsync(Guid documentId, DocumentAuditAction action, object details, CancellationToken cancellationToken)
    { await _audit.WriteAsync(new AuditEntry { DocumentId = documentId, UserId = _session.CurrentUser?.UserId ?? "Anonymous", Action = action, DetailsJson = JsonSerializer.Serialize(details) }, cancellationToken).ConfigureAwait(false); }
    private async Task TryDeleteAsync(string relativePath) { try { await _dxfFiles.DeleteAsync(relativePath, CancellationToken.None).ConfigureAwait(false); } catch (Exception cleanup) { _logger?.Log(cleanup, "DXF generation cleanup", $"orphanPath={relativePath}"); } }
    private async Task TryRollbackAsync(Guid? documentId)
    {
        if (documentId is not Guid id) return;
        try { await _transactionalDocuments.DeleteDocumentAsync(id, CancellationToken.None).ConfigureAwait(false); }
        catch (Exception rollback) { _logger?.Log(rollback, "DXF generation rollback", $"documentId={id}"); }
    }
    private static DxfDrawingGenerationResult Failure(string message, IReadOnlyList<string>? diagnostics = null, IProgress<DxfDrawingGenerationProgress>? progress = null, bool reportTerminal = true)
    {
        if (reportTerminal) ReportFailed(progress, message);
        return new(false, message, Diagnostics: (diagnostics ?? Array.Empty<string>()).Append(message).ToArray());
    }
    private static void ReportFailed(IProgress<DxfDrawingGenerationProgress>? progress, string message) => progress?.Report(new("failed", 100, message));
    private static void ReportCancelled(IProgress<DxfDrawingGenerationProgress>? progress, string message) => progress?.Report(new("cancelled", 100, message));
}
