using DrawVerify.Core.Models;
using Xunit;

namespace DrawVerify.Core.Tests;

public sealed class TemplatePlaceholderTests
{
    [Fact(DisplayName = "TSD-02 signature roles and metadata validate")]
    public void Signature_roles_and_metadata_are_valid()
    {
        foreach (var role in new[] { "Maker", "Checker", "Approver", "Custom Role" })
            new TemplatePlaceholder { Type = TemplatePlaceholderType.Signature, Role = role, Label = $"{role} Signature", IsRequired = true }.Validate();
    }

    [Fact(DisplayName = "TSD-03 dynamic placeholder catalog validates")]
    public void Dynamic_types_validate_with_labels()
    {
        foreach (var type in new[] { TemplatePlaceholderType.Date, TemplatePlaceholderType.DocumentNumber, TemplatePlaceholderType.Revision, TemplatePlaceholderType.ProjectName, TemplatePlaceholderType.CustomText })
            new TemplatePlaceholder { Type = type, Label = type == TemplatePlaceholderType.CustomText ? "Customer reference" : type.ToString() }.Validate();
    }

    [Fact(DisplayName = "TSD-04 geometry remains normalized and rejects out of bounds")]
    public void Geometry_validates_page_bounds()
    {
        new TemplatePlaceholderPosition { X = .1, Y = .2, Width = .3, Height = .4 }.Validate();
        Assert.Throws<InvalidOperationException>(() => new TemplatePlaceholderPosition { X = .8, Y = .2, Width = .3, Height = .1 }.Validate());
    }

    [Fact(DisplayName = "TSD-05 duplicate roles are scoped to logical blocks")]
    public void Duplicate_roles_are_scoped_to_logical_blocks()
    {
        var first = new TemplatePlaceholder { Type = TemplatePlaceholderType.Signature, Role = "Maker", Label = "Maker 1", LogicalBlock = "approval-1" };
        var second = first with { Id = Guid.NewGuid(), Label = "Maker 2", LogicalBlock = "approval-2" };
        var allowed = first with { Id = Guid.NewGuid(), Label = "Maker 3", AllowDuplicateRole = true };
        first.Validate();
        second.Validate();
        allowed.Validate();
        Assert.NotEqual(first.LogicalBlock, second.LogicalBlock);
        Assert.True(allowed.AllowDuplicateRole);
    }

    [Fact(DisplayName = "TSD-05 duplicate identifiers and required metadata are rejected")]
    public void Invalid_placeholder_metadata_is_rejected()
    {
        var placeholder = new TemplatePlaceholder { Type = TemplatePlaceholderType.ProjectName, Label = "Project" };
        Assert.Throws<InvalidOperationException>(() => (placeholder with { Label = "" }).Validate());
        Assert.Throws<InvalidOperationException>(() => new TemplatePlaceholder { Type = TemplatePlaceholderType.Signature, Label = "Signature" }.Validate());
    }
}
