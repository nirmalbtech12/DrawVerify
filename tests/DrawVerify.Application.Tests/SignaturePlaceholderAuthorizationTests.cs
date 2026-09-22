using DrawVerify.Core.Models;
using Xunit;

namespace DrawVerify.Application.Tests;

public sealed class SignaturePlaceholderAuthorizationTests
{
    [Theory(DisplayName = "TSD-03 only the assigned role and user are eligible")]
    [InlineData("checker-1", "Checker", true)]
    [InlineData("maker-1", "Checker", false)]
    [InlineData("other-user", "Checker", false)]
    [InlineData(null, "Checker", false)]
    public void Assignment_eligibility_rejects_wrong_role_user_and_absent_identity(string? userId, string role, bool expected)
    {
        var assignment = new SignaturePlaceholderAssignment { PlaceholderId = Guid.NewGuid(), Role = "Checker", AssignedSignerUserId = "checker-1" };
        Assert.Equal(expected, assignment.IsEligible(userId, new[] { role }));
    }

    [Fact(DisplayName = "TSD-03 signed targets are no longer eligible")]
    public void Signed_assignment_is_excluded_from_targets()
    {
        var assignment = new SignaturePlaceholderAssignment { PlaceholderId = Guid.NewGuid(), Role = "Approver", AssignedSignerUserId = "approver-1", IsSigned = true, SignedByUserId = "approver-1", SignedUtc = DateTimeOffset.UtcNow };
        var submission = new ReviewSubmission { PlaceholderAssignments = new[] { assignment } };
        Assert.Empty(submission.EligiblePlaceholders("approver-1", new[] { "Approver" }));
    }
}
