using DrawVerify.Core.Enums;
using DrawVerify.Core.Models;
namespace DrawVerify.Core.Interfaces;
public interface IVerificationSessionRepository
{
    Task<VerificationSession> GetOrCreateAsync(Guid stepDocumentId, Guid dxfDocumentId, CancellationToken cancellationToken = default);
    Task<VerificationSession?> GetAsync(Guid sessionId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<DimensionMatch>> GetMatchesAsync(Guid sessionId, DimensionMatchStatus? status = null, CancellationToken cancellationToken = default);
    Task ReplaceResultsAsync(VerificationSession session, IReadOnlyCollection<DimensionMatch> matches, DocumentAuditAction auditAction, string userId, string detailsJson, CancellationToken cancellationToken = default);
}
