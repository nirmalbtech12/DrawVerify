using DrawVerify.Application.Services;
using DrawVerify.Application.Workflow;
using DrawVerify.Core.Authorization;
using DrawVerify.Core.Configuration;
using DrawVerify.Core.Enums;
using DrawVerify.Core.Interfaces;
using DrawVerify.Core.Models;
using Xunit;

namespace DrawVerify.Application.Tests;

public sealed class WorkflowConfigurationEnforcementTests
{
    [Fact(DisplayName = "TSD-06 missing or unsupported unmapped-placeholder policy is rejected")]
    public void Invalid_unmapped_placeholder_policy_is_visible_in_application_validation()
    {
        var configuration = BaselineConfiguration() with { UnmappedSignaturePlaceholderPolicy = (UnmappedSignaturePlaceholderPolicy)999 };
        Assert.Contains(configuration.ApplicationValidation(), error => error.Contains("UnmappedSignaturePlaceholderPolicy", StringComparison.Ordinal));
    }

    private static WorkflowConfiguration BaselineConfiguration() => new()
    {
        PermittedInitialStates = new[] { WorkflowStatus.Draft, WorkflowStatus.InPreparation },
        DuplicateSubmissionStates = new[] { WorkflowStatus.SubmittedForReview, WorkflowStatus.UnderReview, WorkflowStatus.Approved }
    };
    [Fact(DisplayName = "TD-APP-004 preview matches triggers and has no persistence or delivery side effects")]
    public async Task Preview_is_pure()
    {
        var repository = new MemoryRepository();
        var service = new WorkflowConfigurationService(repository, new Session(new("admin", [SystemRoles.Admin])), new RoleAuthorization());
        var configuration = Baseline() with { Stages = [new() { Name = "Review", Roles = ["Checker"], RequiredApprovals = 1 }], Triggers = [new() { Name = "Drawing", DocumentTypes = ["drawing"] }], Notifications = [new() { Event = WorkflowNotificationEvent.Submission, RecipientRoles = ["Checker"], ContentTemplate = "Preview {document}" }] };
        var result = await service.PreviewAsync(configuration, new WorkflowPreviewMetadata("drawing"));
        var miss = await service.PreviewAsync(configuration, new WorkflowPreviewMetadata("report"));
        Assert.True(result.MatchesTrigger); Assert.Equal(["Review"], result.Stages); Assert.Equal(["Checker"], result.Recipients); Assert.False(miss.MatchesTrigger); Assert.Empty(repository.Saves);
    }

    [Fact(DisplayName = "TD-APP-005 configured display labels remain separate from canonical permissions")]
    public void Custom_labels_propagate_as_data_without_replacing_role_types()
    {
        var configuration = Baseline() with { Roles = [new() { DisplayName = "Reviewer", SystemRole = SystemRoles.Checker, Permissions = [RolePermission.ReviewVerification] }, new() { DisplayName = "Quality Manager", SystemRole = SystemRoles.Approver }] , StatusLabels = new Dictionary<WorkflowStatus, string> { [WorkflowStatus.UnderReview] = "In Review", [WorkflowStatus.Released] = "Released" } };
        Assert.Equal(SystemRoles.Checker, configuration.Roles[0].SystemRole); Assert.Equal("Reviewer", configuration.Roles[0].DisplayName); Assert.Equal("In Review", configuration.StatusLabels[WorkflowStatus.UnderReview]); Assert.Contains(RolePermission.ReviewVerification, new RoleAuthorization().EffectivePermissions([SystemRoles.Checker]));
    }

    [Fact(DisplayName = "TD-APP-006 sequential, parallel, conditional stages and quorum are represented")]
    public void Routing_modes_and_quorum_are_explicit()
    {
        var configuration = Baseline() with { Stages = [new() { Name = "Sequential", Mode = WorkflowApprovalMode.Sequential, Roles = ["Checker"], RequiredApprovals = 1 }, new() { Name = "Parallel", Mode = WorkflowApprovalMode.Parallel, Roles = ["Checker", "Approver"], RequiredApprovals = 2 }, new() { Name = "Conditional", Mode = WorkflowApprovalMode.Conditional, Roles = ["Approver"], RequiredApprovals = 1 }] };
        new WorkflowConfigurationValidator().EnsureValid(configuration); Assert.Equal(3, configuration.Stages.Count); Assert.Equal(2, configuration.Stages[1].RequiredApprovals); Assert.Equal(WorkflowApprovalMode.Conditional, configuration.Stages[2].Mode);
    }

    [Fact(DisplayName = "TD-APP-006 premature advancement and invalid conditional values are rejected")]
    public void Routing_validation_rejects_premature_or_invalid_rules()
    {
        var invalid = Baseline() with
        {
            Roles = [new() { DisplayName = "Checker", SystemRole = SystemRoles.Checker }],
            Stages = [new() { Name = "Parallel review", Mode = WorkflowApprovalMode.Parallel, Roles = ["Checker", "Checker"], RequiredApprovals = 3 }],
            Triggers = [new() { Name = "Risk", RiskLevel = -1 }]
        };
        var errors = new WorkflowConfigurationValidator().Validate(invalid);
        Assert.Contains(errors, x => x.Contains("RequiredApprovals", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(errors, x => x.Contains("negative", StringComparison.OrdinalIgnoreCase));
    }

    [Fact(DisplayName = "TD-APP-007 delegation and substitution flags are independently retained")]
    public void Delegation_and_substitution_policy_is_explicit()
    { var stage = new WorkflowStageDefinition { Name = "Review", Roles = ["Checker"], RequiredApprovals = 1, AllowDelegation = true, AllowSubstitution = false }; Assert.True(stage.AllowDelegation); Assert.False(stage.AllowSubstitution); }

    [Fact(DisplayName = "TD-APP-008 rejection destination and comment requirements are retained")]
    public void Rejection_policy_is_explicit()
    { var stage = new WorkflowStageDefinition { Name = "Approval", Roles = ["Approver"], RequiredApprovals = 1, RejectTo = "Maker", RequireRejectionComment = true }; Assert.Equal("Maker", stage.RejectTo); Assert.True(stage.RequireRejectionComment); }

    [Fact(DisplayName = "TD-APP-008 rejection routes must reference configured roles or stages")]
    public void Rejection_route_references_are_validated()
    {
        var configuration = Baseline() with { Stages = [new() { Name = "Approval", Roles = ["Approver"], RequiredApprovals = 1, RejectTo = "Missing" }] };
        var errors = new WorkflowConfigurationValidator().Validate(configuration);
        Assert.Contains(errors, x => x.Contains("RejectTo", StringComparison.OrdinalIgnoreCase));
    }

    [Fact(DisplayName = "TD-APP-009 trigger predicates and transition authorization are validated")]
    public void Trigger_and_transition_rules_are_checked()
    { var c = Baseline() with { Triggers = [new() { Name = "Risk", DocumentTypes = ["drawing"], Projects = ["P1"], MinimumRevision = 2, RiskLevel = 3, MinimumValue = 10 }], StatusTransitions = [new() { From = WorkflowStatus.Draft, To = WorkflowStatus.SubmittedForReview, AuthorizedRoles = ["Maker"] }] }; new WorkflowConfigurationValidator().EnsureValid(c); Assert.Equal(2, c.Triggers[0].MinimumRevision); Assert.Contains("Maker", c.StatusTransitions[0].AuthorizedRoles); }

    [Fact(DisplayName = "TD-APP-010 notification recipients channels reminders and unsupported values are visible")]
    public void Notification_rules_are_explicit_and_invalid_channels_fail_validation()
    { var rule = new WorkflowNotificationRule { Event = WorkflowNotificationEvent.Reminder, RecipientRoles = ["Checker"], Channels = [WorkflowNotificationChannel.InApp], ContentTemplate = "Pending", ReminderEnabled = true, ReminderInterval = TimeSpan.FromHours(4) }; Assert.True(rule.ReminderEnabled); Assert.Equal(TimeSpan.FromHours(4), rule.ReminderInterval); var invalid = Baseline() with { Notifications = [rule with { Channels = [(WorkflowNotificationChannel)99] }] }; var errors = new WorkflowConfigurationValidator().Validate(invalid); Assert.Contains(errors, error => error.Contains("unsupported notification channel", StringComparison.OrdinalIgnoreCase)); Assert.Equal((WorkflowNotificationChannel)99, invalid.Notifications[0].Channels[0]); }

    [Fact(DisplayName = "TD-APP-011 revision and rollout versions are immutable configuration data")]
    public void Revision_and_rollout_policy_are_retained()
    { var c = Baseline() with { Version = 2, RevisionPolicy = new() { CreateRevisionOnSubmission = false, NumberFormat = "Major.Minor", RetainRejectedRevisions = false }, RolloutPolicy = WorkflowRolloutPolicy.ApplyToInProgress }; new WorkflowConfigurationValidator().EnsureValid(c); Assert.Equal(2, c.Version); Assert.False(c.RevisionPolicy.RetainRejectedRevisions); Assert.Equal(WorkflowRolloutPolicy.ApplyToInProgress, c.RolloutPolicy); }

    [Fact(DisplayName = "TD-APP-012 legacy configuration retains submission defaults and null-version compatibility")]
    public void Legacy_defaults_remain_available()
    { var c = Baseline(); c.Validate(); Assert.Contains(WorkflowStatus.Draft, c.PermittedInitialStates); Assert.Equal(WorkflowStatus.SubmittedForReview, c.SubmittedForReviewState); Assert.True(c.MarkChangedPackagesSuperseded); }

    private static WorkflowConfiguration Baseline() => new() { PermittedInitialStates = [WorkflowStatus.Draft, WorkflowStatus.InPreparation], DuplicateSubmissionStates = [WorkflowStatus.SubmittedForReview, WorkflowStatus.UnderReview, WorkflowStatus.Approved] };
    private sealed class Session(AuthenticatedUser user) : ISessionAuthenticationService { public AuthenticatedUser? CurrentUser { get; } = user; public bool IsAuthenticated => true; public void SignIn(AuthenticatedUser value) { } public void SignOut() { } }
    private sealed class MemoryRepository : IWorkflowConfigurationRepository
    {
        public List<WorkflowConfiguration> Saves { get; } = [];
        public Task<WorkflowConfiguration?> GetCurrentAsync(CancellationToken cancellationToken = default) => Task.FromResult<WorkflowConfiguration?>(null);
        public Task<WorkflowConfiguration?> GetVersionAsync(int version, CancellationToken cancellationToken = default) => Task.FromResult<WorkflowConfiguration?>(null);
        public Task<WorkflowConfiguration> SaveAsync(WorkflowConfiguration configuration, WorkflowConfigurationAudit audit, int? expectedVersion, CancellationToken cancellationToken = default) { Saves.Add(configuration); return Task.FromResult(configuration); }
    }
}
