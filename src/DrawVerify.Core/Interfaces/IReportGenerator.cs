namespace DrawVerify.Core.Interfaces;
using DrawVerify.Core.Models;

/// <summary>
/// Produces signed verification reports (PDF). Implementation lives in Infrastructure.
/// </summary>
public interface IReportGenerator
{
    Task<string> GenerateMarkdownAsync(VerificationReport report, CancellationToken cancellationToken = default);
}
