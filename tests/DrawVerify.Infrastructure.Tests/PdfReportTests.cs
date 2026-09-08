namespace DrawVerify.Infrastructure.Tests;

using DrawVerify.Core.Enums;
using DrawVerify.Core.Models;
using Xunit;

/// <summary>
/// Tests for PDF report rendering and stamp placement.
/// </summary>
public class PdfReportTests
{
    [Fact]
    public void Markdown_report_contains_labeled_unmatched_section_and_required_fields()
    {
        var report = new VerificationReport { DimensionMatches = new[]
        {
            new DimensionMatch { Status = DimensionMatchStatus.Unmatched, DxfDimensionReference = 4, DimensionType = DimensionType.Linear, NominalValue = 25, ReasonCode = "NoCorrespondingGeometryFound", ReasonExplanation = "No corresponding geometry found in STEP model." },
            new DimensionMatch { Status = DimensionMatchStatus.Matched, DxfDimensionReference = 5 }
        }};
        var markdown = report.ToMarkdown();
        Assert.Contains("## Unmatched Dimensions – Requires Manual Review", markdown);
        Assert.Contains("Reference: `4`", markdown);
        Assert.Contains("Nominal value: 25", markdown);
        Assert.Contains("NoCorrespondingGeometryFound", markdown);
        Assert.DoesNotContain("Reference: `5`", markdown);
    }

    [Fact]
    public void Markdown_report_has_an_explicit_unmatched_section_when_there_are_no_matches()
        => Assert.Contains("Unmatched Dimensions – Requires Manual Review", new VerificationReport().ToMarkdown());
}
