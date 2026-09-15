using System.Text.Json;
using System.Text.Json.Serialization;
using DrawVerify.Core.Authorization;
using DrawVerify.Core.Configuration;
using DrawVerify.Core.Enums;
using DrawVerify.Core.Models;
using Xunit;

namespace DrawVerify.Core.Tests;

public sealed class WorkflowConfigurationContractTests
{
    [Fact(DisplayName = "TD-CORE-001 complete versioned configuration round-trips without changing canonical roles")]
    public void Complete_configuration_round_trips()
    {
        var configuration = Baseline() with
        {
            Roles = [new() { DisplayName = "Reviewer", SystemRole = SystemRoles.Checker, Permissions = [RolePermission.ReviewVerification] }, new() { DisplayName = "Quality Manager", SystemRole = SystemRoles.Approver, Permissions = [RolePermission.FinalApproval] }],
            Stages = [new() { Name = "Review", Mode = WorkflowApprovalMode.Sequential, Roles = ["Reviewer"], RequiredApprovals = 1 }],
            Triggers = [new() { Name = "Drawing trigger", DocumentTypes = ["drawing"], Projects = ["P1"], RiskLevel = 2 }],
            Notifications = [new() { Event = WorkflowNotificationEvent.Submission, RecipientRoles = ["Reviewer"], Channels = [WorkflowNotificationChannel.InApp], ContentTemplate = "Review {document}" }],
            RevisionPolicy = new() { NumberFormat = "Major.Minor", RetainRejectedRevisions = false },
            RolloutPolicy = WorkflowRolloutPolicy.CaptureAtWorkflowStart,
            StatusLabels = new Dictionary<WorkflowStatus, string> { [WorkflowStatus.UnderReview] = "In Review", [WorkflowStatus.Released] = "Released" },
            StatusTransitions = [new() { From = WorkflowStatus.Draft, To = WorkflowStatus.SubmittedForReview, AuthorizedRoles = ["Reviewer"] }]
        };
        configuration.Validate();
        var json = JsonSerializer.Serialize(configuration, new JsonSerializerOptions { Converters = { new JsonStringEnumConverter() } });
        var copy = JsonSerializer.Deserialize<WorkflowConfiguration>(json, new JsonSerializerOptions { Converters = { new JsonStringEnumConverter() } });
        Assert.NotNull(copy);
        Assert.Equal(json, JsonSerializer.Serialize(copy, new JsonSerializerOptions { Converters = { new JsonStringEnumConverter() } }));
        Assert.Equal(configuration.Version, copy!.Version);
        Assert.Equal(configuration.Roles.Select(x => (x.DisplayName, x.SystemRole, x.Permissions.ToArray())), copy.Roles.Select(x => (x.DisplayName, x.SystemRole, x.Permissions.ToArray())));
        Assert.Equal(SystemRoles.Checker, copy.Roles[0].SystemRole);
        Assert.Contains(RolePermission.ReviewVerification, new RoleAuthorization().EffectivePermissions([SystemRoles.Checker]));
    }

    [Fact(DisplayName = "TD-CORE-002 invalid references and policies produce deterministic descriptive errors")]
    public void Invalid_configuration_is_rejected_without_defaults()
    {
        var invalid = Baseline() with
        {
            Roles = [new() { DisplayName = "Reviewer", SystemRole = "Missing" }, new() { DisplayName = "reviewer", SystemRole = SystemRoles.Checker }],
            Stages = [new() { Name = "Review", Roles = ["Unknown"], RequiredApprovals = 2 }],
            Notifications = [new() { Event = WorkflowNotificationEvent.Approval, RecipientRoles = ["Unknown"], Channels = [(WorkflowNotificationChannel)99] }],
            RevisionPolicy = new() { NumberFormat = "Unsupported" },
            StatusTransitions = [new() { From = WorkflowStatus.Draft, To = WorkflowStatus.SubmittedForReview, AuthorizedRoles = ["Unknown"] }]
        };
        var errors = invalid.ApplicationValidation();
        Assert.Contains(errors, x => x.Contains("undefined system role", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(errors, x => x.Contains("duplicated", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(errors, x => x.Contains("RequiredApprovals", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(errors, x => x.Contains("undefined role", StringComparison.OrdinalIgnoreCase));
        Assert.Empty(Baseline().ApplicationValidation());
    }

    [Fact(DisplayName = "TD-CORE-002 invalid enum values, predicates, cycles, and notification rules are reported")]
    public void Invalid_rules_are_descriptive_and_do_not_mutate_the_baseline()
    {
        var baseline = Baseline() with
        {
            Roles = [new() { DisplayName = "Checker", SystemRole = SystemRoles.Checker }],
            Stages = [new() { Name = "Review", Roles = ["Checker"], RequiredApprovals = 1 }]
        };
        var invalid = baseline with
        {
            Stages = [new() { Name = "Review", Roles = ["Checker"], RequiredApprovals = 2 }, new() { Name = "Review", Roles = ["Unknown"], RequiredApprovals = 1, Mode = (WorkflowApprovalMode)99 }],
            Triggers = [new() { Name = "Risk", MinimumRevision = -1, RiskLevel = -1, MinimumValue = -1 }],
            Notifications = [new() { Event = WorkflowNotificationEvent.Reminder, RecipientRoles = ["Unknown"], Channels = [(WorkflowNotificationChannel)99], ReminderEnabled = true, ReminderInterval = TimeSpan.Zero }],
            RevisionPolicy = new() { NumberFormat = "Unsupported" },
            RolloutPolicy = (WorkflowRolloutPolicy)99,
            StatusTransitions = [new() { From = WorkflowStatus.Draft, To = WorkflowStatus.SubmittedForReview, AuthorizedRoles = ["Checker"] }, new() { From = WorkflowStatus.SubmittedForReview, To = WorkflowStatus.Draft, AuthorizedRoles = ["Checker"] }]
        };

        var errors = invalid.ApplicationValidation();
        Assert.Contains(errors, error => error.Contains("RequiredApprovals", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(errors, error => error.Contains("undefined", StringComparison.OrdinalIgnoreCase));
        Assert.Empty(baseline.ApplicationValidation());
    }

    [Fact(DisplayName = "TD-CORE-003 legacy defaults preserve existing status values")]
    public void Legacy_configuration_contract_remains_compatible()
    {
        var json = File.ReadAllText(FindRepositoryFile("config/workflow.default.json"));
        var configuration = JsonSerializer.Deserialize<WorkflowConfiguration>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true, Converters = { new JsonStringEnumConverter() } });
        Assert.NotNull(configuration);
        configuration!.Validate();
        Assert.Equal(WorkflowStatus.SubmittedForReview, configuration.SubmittedForReviewState);
        Assert.Equal(0, (int)WorkflowStatus.Draft);
        Assert.Equal(12, (int)WorkflowStatus.Released);
        Assert.Empty(configuration.Roles);
    }

    private static WorkflowConfiguration Baseline() => new() { PermittedInitialStates = [WorkflowStatus.Draft, WorkflowStatus.InPreparation], DuplicateSubmissionStates = [WorkflowStatus.SubmittedForReview, WorkflowStatus.UnderReview, WorkflowStatus.Approved] };
    private static string FindRepositoryFile(string relative)
    {
        var directory = AppContext.BaseDirectory;
        while (directory is not null && !File.Exists(Path.Combine(directory, "DrawVerify.sln"))) directory = Directory.GetParent(directory)?.FullName;
        return Path.Combine(directory ?? throw new InvalidOperationException("Repository root was not found."), relative);
    }
}
