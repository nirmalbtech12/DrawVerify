namespace DrawVerify.Application.Services;

using DrawVerify.Application.Verification;
using DrawVerify.Core.Interfaces;
using DrawVerify.Core.Models;

/// <summary>
/// Orchestrates document verification against geometry and tolerance models.
/// Pure business logic; testable with a fake IGeometryService.
/// </summary>
public sealed class VerificationService
{
    private readonly VerificationEngine _engine;
    private readonly IVerificationSessionRepository _sessions;
    public VerificationService(VerificationEngine engine, IVerificationSessionRepository sessions) { _engine = engine ?? throw new ArgumentNullException(nameof(engine)); _sessions = sessions ?? throw new ArgumentNullException(nameof(sessions)); }
    public async Task<IReadOnlyList<GdtResult>> VerifyGdtAsync(Guid sessionId, IEnumerable<DrawVerify.Geometry.Models.GdtAnnotationData> annotations, string userId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(annotations);
        var results = _engine.ValidateGdt(annotations, sessionId);
        await _sessions.ReplaceGdtResultsAsync(sessionId, results, userId, cancellationToken).ConfigureAwait(false);
        return results;
    }
    public async Task<IReadOnlyList<GdtResult>> VerifyGdtAsync(Guid sessionId, IEnumerable<DrawVerify.Geometry.Models.GdtAnnotationData> stepAnnotations, IEnumerable<DrawVerify.Geometry.Models.GdtAnnotationData> dxfAnnotations, string userId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stepAnnotations);
        ArgumentNullException.ThrowIfNull(dxfAnnotations);
        var results = _engine.ValidateGdt(stepAnnotations, dxfAnnotations, sessionId);
        await _sessions.ReplaceGdtResultsAsync(sessionId, results, userId, cancellationToken).ConfigureAwait(false);
        return results;
    }
}
