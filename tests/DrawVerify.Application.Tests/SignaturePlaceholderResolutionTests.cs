using DrawVerify.Core.Models;
using Xunit;

namespace DrawVerify.Application.Tests;

public sealed class SignaturePlaceholderResolutionTests
{
    [Fact(DisplayName = "TSD-01 captured role assignments resolve independently of template changes")]
    public void Captured_mapping_is_the_source_of_role_targets()
    {
        var makerId = Guid.NewGuid();
        var checkerId = Guid.NewGuid();
        var mapping = new TemplateMappingSnapshot
        {
            TemplateId = Guid.NewGuid(),
            TemplateVersion = 3,
            Assignments = new[]
            {
                new SignaturePlaceholderAssignment { PlaceholderId = makerId, Role = "Maker", AssignedSignerUserId = "maker-1" },
                new SignaturePlaceholderAssignment { PlaceholderId = checkerId, Role = "Checker", AssignedSignerUserId = "checker-1" }
            }
        };
        var submission = new ReviewSubmission { TemplateMapping = mapping, PlaceholderAssignments = mapping.Assignments };

        Assert.Equal(new[] { makerId }, submission.EligiblePlaceholders("maker-1", new[] { "Maker" }).Select(x => x.PlaceholderId));
        Assert.Equal(new[] { checkerId }, submission.EligiblePlaceholders("checker-1", new[] { "Checker" }).Select(x => x.PlaceholderId));
        Assert.Empty(submission.EligiblePlaceholders("maker-1", new[] { "Checker" }));
    }

    [Fact(DisplayName = "TSD-01 malformed captured assignments fail visibly")]
    public void Invalid_assignment_is_rejected()
    {
        var invalid = new SignaturePlaceholderAssignment { PlaceholderId = Guid.NewGuid(), Role = "Unknown" };
        Assert.Throws<InvalidOperationException>(invalid.Validate);
    }
}
