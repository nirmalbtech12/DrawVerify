using DrawVerify.Core.Models;
using Xunit;

namespace DrawVerify.Application.Tests;

public sealed class ReportTemplateValidationTests
{
    [Fact(DisplayName = "TSD-AC03 AC05 AC06 AC07 AC08 valid structured settings validate")]
    public void Valid_header_sections_tables_summary_and_style_are_accepted()
    {
        var template = new ReportTemplate
        {
            Header = new() { OrganizationName = "Acme", OrganizationAddress = "1 Main", ReportTitleFormat = "Custom report", LogoPosition = "Right", TextAlignment = "Center" },
            Sections = ReportTemplateDefaults.Sections.Select(x => x with { Title = x.Title + " title", Description = "description" }).Reverse().Select((x, i) => x with { Ordinal = i }).ToArray(),
            Tables = [new ReportTemplateTable { Key = "Dimensions", Columns = [new() { Field = "DimensionId", Label = "ID", Precision = 2, Unit = "mm" }], ConditionalRules = [new() { Field = "Status", Operator = "Equals", Value = "Mismatched", Color = "#FF0000" }] }],
            Summary = new() { Layout = "KeyMetrics", Statistics = [new() { Key = "TotalDimensions", Label = "Total", Format = "0" }] },
            Style = new() { Orientation = "Landscape", Margin = 32, HeaderColor = "#112233" }
        };
        template.Validate();
    }

    [Theory(DisplayName = "TSD-AC03 AC05 AC06 AC07 AC08 invalid settings are rejected")]
    [InlineData("section")]
    [InlineData("table")]
    [InlineData("statistic")]
    [InlineData("style")]
    public void Invalid_configuration_fails_clearly(string kind)
    {
        var template = kind switch
        {
            "section" => new ReportTemplate { Sections = [new() { Key = "Unknown", Ordinal = 0, Title = "x" }] },
            "table" => new ReportTemplate { Tables = [new() { Key = "Dimensions", Columns = [new() { Field = "Unknown" }] }] },
            "statistic" => new ReportTemplate { Summary = new() { Statistics = [new() { Key = "Unknown", Label = "x" }] } },
            _ => new ReportTemplate { Style = new() { HeaderColor = "red" } }
        };
        Assert.Throws<InvalidOperationException>(() => template.Validate());
    }
}
