using DrawVerify.Core.Interfaces;
using DrawVerify.Core.Models;

namespace DrawVerify.Application.Services;

public sealed class MarkdownReportGenerator : IReportGenerator
{
    public Task<string> GenerateMarkdownAsync(VerificationReport report, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(report);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(report.ToMarkdown());
    }
}
