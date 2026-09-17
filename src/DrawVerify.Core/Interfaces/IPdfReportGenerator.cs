using DrawVerify.Core.Models;

namespace DrawVerify.Core.Interfaces;

public interface IPdfReportGenerator
{
    Task<byte[]> GeneratePdfAsync(VerificationReport report, CancellationToken cancellationToken = default);
    Task<byte[]> GeneratePdfAsync(VerificationReport report, VerificationReportRequest request, CancellationToken cancellationToken = default)
        => GeneratePdfAsync(report with { Request = request.Normalize() }, cancellationToken);
}
