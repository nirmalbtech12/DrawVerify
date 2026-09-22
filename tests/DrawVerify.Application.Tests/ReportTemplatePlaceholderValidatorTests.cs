using DrawVerify.Core.Models;
using Xunit;

namespace DrawVerify.Application.Tests;

public sealed class ReportTemplatePlaceholderValidatorTests
{
    [Fact(DisplayName = "TSD-05 validator accepts legacy and valid configurations")]
    public void Valid_and_legacy_placeholders_are_accepted()
    {
        DrawVerify.Application.Services.ReportTemplatePlaceholderValidator.Validate([]);
        DrawVerify.Application.Services.ReportTemplatePlaceholderValidator.Validate(new[] { new TemplatePlaceholder { Type = TemplatePlaceholderType.Signature, Role = "Maker", Label = "Maker" } });
    }

    [Theory(DisplayName = "TSD-05 validator rejects invalid configurations")]
    [InlineData("duplicate")]
    [InlineData("missing-label")]
    [InlineData("missing-role")]
    [InlineData("geometry")]
    public void Invalid_configurations_are_rejected(string kind)
    {
        var placeholder = new TemplatePlaceholder { Type = TemplatePlaceholderType.Signature, Role = "Maker", Label = "Maker" };
        var values = kind switch
        {
            "duplicate" => new[] { placeholder, placeholder with { Id = placeholder.Id, Label = "Other" } },
            "missing-label" => new[] { placeholder with { Label = "" } },
            "missing-role" => new[] { placeholder with { Role = null } },
            _ => new[] { placeholder with { Position = new TemplatePlaceholderPosition { X = .9, Y = .1, Width = .2, Height = .1 } } }
        };
        var errors = DrawVerify.Application.Services.ReportTemplatePlaceholderValidator.ValidateAndCollect(values);
        Assert.NotEmpty(errors);
    }

    [Fact(DisplayName = "TSD-05 allowed and separate-block duplicate roles remain valid")]
    public void Duplicate_role_exceptions_are_scoped()
    {
        var maker = new TemplatePlaceholder { Type = TemplatePlaceholderType.Signature, Role = "Maker", Label = "Maker", LogicalBlock = "one" };
        DrawVerify.Application.Services.ReportTemplatePlaceholderValidator.Validate(new[] { maker, maker with { Id = Guid.NewGuid(), Label = "Maker two", LogicalBlock = "two" }, maker with { Id = Guid.NewGuid(), Label = "Maker allowed", AllowDuplicateRole = true } });
    }
}
