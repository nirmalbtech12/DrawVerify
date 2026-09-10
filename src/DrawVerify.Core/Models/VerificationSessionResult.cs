namespace DrawVerify.Core.Models;
public sealed record VerificationSessionResult(VerificationSession Session, IReadOnlyList<DimensionMatch> Matches)
{
    public IReadOnlyList<GdtResult> GdtResults { get; init; } = Array.Empty<GdtResult>();
}
