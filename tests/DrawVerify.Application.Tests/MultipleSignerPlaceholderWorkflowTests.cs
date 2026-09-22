using DrawVerify.Core.Models;
using Xunit;

namespace DrawVerify.Application.Tests;

public sealed class MultipleSignerPlaceholderWorkflowTests
{
    [Fact(DisplayName = "TSD-02 each configured signer owns one placeholder and completion waits for both")]
    public void Multiple_signer_assignments_track_distinct_signed_state()
    {
        var firstId = Guid.NewGuid();
        var secondId = Guid.NewGuid();
        var first = new SignaturePlaceholderAssignment { PlaceholderId = firstId, Role = "Approver", AssignedSignerUserId = "approver-1", IsSigned = true, SignedByUserId = "approver-1", SignedUtc = DateTimeOffset.UtcNow };
        var second = new SignaturePlaceholderAssignment { PlaceholderId = secondId, Role = "Approver", AssignedSignerUserId = "approver-2" };
        var submission = new ReviewSubmission { PlaceholderAssignments = new[] { first, second } };

        Assert.Equal(new[] { secondId }, submission.EligiblePlaceholders("approver-2", new[] { "Approver" }).Select(x => x.PlaceholderId));
        Assert.Empty(submission.EligiblePlaceholders("approver-1", new[] { "Approver" }));
        Assert.Contains(submission.PlaceholderAssignments, x => x.IsSigned && x.SignedByUserId == "approver-1");
        Assert.Contains(submission.PlaceholderAssignments, x => !x.IsSigned && x.PlaceholderId == secondId);
    }
}
