using DrawVerify.Core.Enums;
using DrawVerify.Core.Models;
namespace DrawVerify.Core.Interfaces;
public interface IVerificationSessionRepository
{
    Task<VerificationSession> GetOrCreateAsync(Guid stepDocumentId, Guid dxfDocumentId, CancellationToken cancellationToken = default);
    async Task<VerificationSession> GetOrCreateAsync(Guid stepDocumentId, Guid dxfDocumentId, Guid stepDocumentVersionId, Guid dxfDocumentVersionId, CancellationToken cancellationToken = default)
    {
        if (stepDocumentVersionId == Guid.Empty || dxfDocumentVersionId == Guid.Empty)
            throw new ArgumentException("Explicit STEP and DXF version IDs are required.");
        var session = await GetOrCreateAsync(stepDocumentId, dxfDocumentId, cancellationToken).ConfigureAwait(false);
        return session with { StepDocumentVersionId = stepDocumentVersionId, DxfDocumentVersionId = dxfDocumentVersionId };
    }
    Task<VerificationSession?> GetAsync(Guid sessionId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<DimensionMatch>> GetMatchesAsync(Guid sessionId, DimensionMatchStatus? status = null, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<GdtResult>> GetGdtResultsAsync(Guid sessionId, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<GdtResult>>(Array.Empty<GdtResult>());
    Task ReplaceGdtResultsAsync(Guid sessionId, IReadOnlyCollection<GdtResult> results, string userId, CancellationToken cancellationToken = default) => throw new NotSupportedException("GD&T result persistence is not supported by this repository.");
    Task UpdateGdtReviewAsync(Guid sessionId, Guid gdtResultId, string? comment, bool requiresEngineeringReview, string userId, CancellationToken cancellationToken = default) => throw new NotSupportedException("GD&T review persistence is not supported by this repository.");
    Task UpdateGdtDispositionAsync(Guid sessionId, Guid gdtResultId, string disposition, string userId, CancellationToken cancellationToken = default) => throw new NotSupportedException("GD&T disposition persistence is not supported by this repository.");
    Task ReplaceResultsAsync(VerificationSession session, IReadOnlyCollection<DimensionMatch> matches, DocumentAuditAction auditAction, string userId, string detailsJson, CancellationToken cancellationToken = default);
    Task UpdateReviewAsync(Guid sessionId, Guid dimensionMatchId, DimensionReview review, CancellationToken cancellationToken = default)
        => throw new NotSupportedException("Dimension review persistence is not supported by this repository.");
    async Task UpdateReviewAsync(Guid sessionId, Guid dimensionMatchId, DimensionReview review, string userId, DocumentAuditAction action, CancellationToken cancellationToken = default)
    {
        await UpdateReviewAsync(sessionId, dimensionMatchId, review, cancellationToken).ConfigureAwait(false);
    }
}
