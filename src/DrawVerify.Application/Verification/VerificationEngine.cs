namespace DrawVerify.Application.Verification;

using DrawVerify.Core.Models;
using DrawVerify.Geometry.Models;

/// <summary>
/// Coordinates dimension and GD&T checks and aggregates VerificationResults.
/// </summary>
public sealed class VerificationEngine
{
    private readonly GdtValidator _gdtValidator;
    public VerificationEngine(GdtValidator gdtValidator) => _gdtValidator = gdtValidator ?? throw new ArgumentNullException(nameof(gdtValidator));
    public IReadOnlyList<GdtResult> ValidateGdt(IEnumerable<GdtAnnotationData> annotations, Guid sessionId = default) => _gdtValidator.Validate(annotations, sessionId);
    public IReadOnlyList<GdtResult> ValidateGdt(IEnumerable<GdtAnnotationData> stepAnnotations, IEnumerable<GdtAnnotationData> dxfAnnotations, Guid sessionId = default) => _gdtValidator.Validate(stepAnnotations, dxfAnnotations, sessionId);
}
