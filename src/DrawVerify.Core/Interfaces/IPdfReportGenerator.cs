using DrawVerify.Core.Models;

namespace DrawVerify.Core.Interfaces;

public interface IPdfReportGenerator
{
    Task<byte[]> GeneratePdfAsync(VerificationReport report, CancellationToken cancellationToken = default);
}
