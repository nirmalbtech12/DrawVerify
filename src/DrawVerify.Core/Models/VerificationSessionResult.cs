namespace DrawVerify.Core.Models;
public sealed record VerificationSessionResult(VerificationSession Session, IReadOnlyList<DimensionMatch> Matches);
