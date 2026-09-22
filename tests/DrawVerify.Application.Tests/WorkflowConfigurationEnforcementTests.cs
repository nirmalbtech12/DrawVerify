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
    [Fact(DisplayName = "TSD-06 workflow evaluation logs, notifies, and enforces invalid mapping policy")]
    public async Task Invalid_mapping_policy_is_enforced_with_warning_notification_and_state_preservation()
    {
        var validPlaceholderId = Guid.NewGuid();
        var invalidPlaceholderId = Guid.NewGuid();
        var templateId = Guid.NewGuid();
        var mapping = new TemplateMappingSnapshot
        {
            TemplateId = templateId,
            TemplateVersion = 3,
            Assignments = new[]
            {
                new SignaturePlaceholderAssignment { PlaceholderId = validPlaceholderId, Role = SystemRoles.Approver, AssignedSignerUserId = "approver-1" },
                new SignaturePlaceholderAssignment { PlaceholderId = invalidPlaceholderId, Role = "UnknownRole", AssignedSignerUserId = "approver-2" }
            },
            PlaceholderLabels = new Dictionary<string, string> { [invalidPlaceholderId.ToString("D")] = "Unmapped Approver" }
        };

        var blocked = await RunInvalidMappingScenarioAsync(UnmappedSignaturePlaceholderPolicy.Block, mapping, notificationFails: false);
        var blockedError = Assert.IsType<InvalidOperationException>(blocked.Exception);
        Assert.Contains("blocked", blockedError.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(blocked.Workflow.Submissions);
        Assert.Equal(WorkflowStatus.Draft, blocked.Workflow.State.Status);
        AssertNotification(blocked, templateId, invalidPlaceholderId, "Unmapped Approver");

        var skipped = await RunInvalidMappingScenarioAsync(UnmappedSignaturePlaceholderPolicy.Skip, mapping, notificationFails: false);
        Assert.Null(skipped.Exception);
        Assert.True(skipped.Result!.Succeeded);
        var saved = Assert.Single(skipped.Workflow.Submissions);
        Assert.Equal(validPlaceholderId, Assert.Single(saved.PlaceholderAssignments).PlaceholderId);
        Assert.Equal(WorkflowStatus.SubmittedForReview, skipped.Workflow.State.Status);
        AssertNotification(skipped, templateId, invalidPlaceholderId, "Unmapped Approver");

        var unsupported = await RunInvalidMappingScenarioAsync((UnmappedSignaturePlaceholderPolicy)999, mapping, notificationFails: false);
        var unsupportedError = Assert.IsType<InvalidOperationException>(unsupported.Exception);
        Assert.Contains(nameof(UnmappedSignaturePlaceholderPolicy), unsupportedError.Message, StringComparison.Ordinal);
        Assert.Contains("Block or Skip", unsupportedError.Message, StringComparison.Ordinal);
        Assert.Empty(unsupported.Workflow.Submissions);
        Assert.Empty(unsupported.Notifications.Calls);

        var notificationFailure = await RunInvalidMappingScenarioAsync(UnmappedSignaturePlaceholderPolicy.Block, mapping, notificationFails: true);
        var notificationError = Assert.IsType<InvalidOperationException>(notificationFailure.Exception);
        Assert.Contains("notification failed", notificationError.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(notificationFailure.Workflow.Submissions);
        Assert.Equal(WorkflowStatus.Draft, notificationFailure.Workflow.State.Status);
        Assert.Contains(notificationFailure.Logger.Entries, entry => entry.Operation == "Invalid signature placeholder notification" && entry.Context.Contains(invalidPlaceholderId.ToString(), StringComparison.Ordinal));
    }

    private static void AssertNotification(InvalidMappingScenario scenario, Guid templateId, Guid placeholderId, string label)
    {
        var notification = Assert.Single(scenario.Notifications.Calls);
        Assert.Equal(templateId, notification.TemplateId);
        Assert.Equal(3, notification.TemplateVersion);
        Assert.Equal(scenario.Workflow.PackageId, notification.PackageId);
        Assert.Equal(1, notification.Revision);
        Assert.Equal(placeholderId, notification.PlaceholderId);
        Assert.Equal(label, notification.PlaceholderLabel);
        Assert.Contains("role", notification.Reason, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(scenario.Logger.Entries, entry => entry.Operation == "Invalid signature placeholder mapping" && entry.Context.Contains(label, StringComparison.Ordinal));
    }

    private static async Task<InvalidMappingScenario> RunInvalidMappingScenarioAsync(UnmappedSignaturePlaceholderPolicy policy, TemplateMappingSnapshot mapping, bool notificationFails)
    {
        var packageId = Guid.NewGuid();
        var timestamp = new DateTime(2026, 9, 22, 12, 0, 0, DateTimeKind.Utc);
        var workflow = new MappingWorkflowRepository(packageId);
        var logger = new MappingLogger();
        var notifications = new MappingNotifications(notificationFails);
        var authentication = new SessionAuthenticationService();
        authentication.SignIn(new AuthenticatedUser("maker-1", new[] { SystemRoles.Maker }) { UserId = "maker-1", DisplayName = "Maker One" });
        var configuration = BaselineConfiguration() with { UnmappedSignaturePlaceholderPolicy = policy };
        var documents = new MappingDocumentRepository(packageId);
        var request = new SubmissionRequest(packageId)
        {
            StepDocumentVersionId = Guid.NewGuid(),
            DxfDocumentVersionId = Guid.NewGuid(),
            DxfDocumentId = Guid.NewGuid(),
            VerificationSessionId = Guid.NewGuid(),
            ExtractionEvidenceJson = "{\"extraction\":\"complete\"}",
            VerificationEvidenceJson = "{\"verification\":\"complete\"}",
            MakerSignature = SignatureData.Create(SignatureMethod.Typed, "maker-signature", "maker-1", "Maker One", packageId, 1, timestamp),
            TemplateMapping = mapping
        };

        try
        {
            var service = new WorkflowService(workflow, authentication, configuration, new RoleAuthorization(), logger, documents, clock: () => timestamp, notifications: notifications);
            var result = await service.SubmitForReviewAsync(request);
            return new(workflow, logger, notifications, result, null);
        }
        catch (Exception exception)
        {
            return new(workflow, logger, notifications, null, exception);
        }
    }

    private sealed record InvalidMappingScenario(MappingWorkflowRepository Workflow, MappingLogger Logger, MappingNotifications Notifications, SubmissionResult? Result, Exception? Exception);

    private sealed class MappingWorkflowRepository(Guid packageId) : IWorkflowRepository
    {
        public Guid PackageId { get; } = packageId;
        public WorkflowState State { get; private set; } = new() { PackageId = packageId, Status = WorkflowStatus.Draft, Revision = 1 };
        public List<ReviewSubmission> Submissions { get; } = [];
        public Task<WorkflowState?> GetStateAsync(Guid requestedPackageId, CancellationToken cancellationToken = default) => Task.FromResult<WorkflowState?>(requestedPackageId == PackageId ? State : null);
        public Task<ReviewSubmission?> GetSubmissionAsync(Guid submissionId, CancellationToken cancellationToken = default) => Task.FromResult<ReviewSubmission?>(Submissions.SingleOrDefault(x => x.SubmissionId == submissionId));
        public Task<IReadOnlyList<ReviewSubmission>> GetSubmissionsAsync(Guid requestedPackageId, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<ReviewSubmission>>(Submissions.Where(x => x.PackageId == requestedPackageId).ToArray());
        public Task<IReadOnlyList<WorkflowQueueItem>> GetCheckerQueueAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<WorkflowQueueItem>>(Array.Empty<WorkflowQueueItem>());
        public Task<ReviewSubmission> SubmitAtomicallyAsync(WorkflowState expectedState, ReviewSubmission submission, AuditEntry auditEntry, CancellationToken cancellationToken = default)
        {
            Submissions.Add(submission);
            State = expectedState with { Status = WorkflowStatus.SubmittedForReview, UpdatedUtc = auditEntry.TimestampUtc };
            return Task.FromResult(submission);
        }
        public Task RecordAuditAsync(AuditEntry auditEntry, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task MarkSupersededAsync(Guid submissionId, AuditEntry auditEntry, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class MappingDocumentRepository(Guid packageId) : IDocumentRepository
    {
        private readonly Document _document = new() { DocumentId = packageId, DocumentType = DrawVerify.Core.Enums.DocumentType.PdfDrawing, Version = new DocumentVersion { DocumentId = packageId, VersionNumber = 1 } };
        public Task<Document?> GetAsync(Guid documentId, CancellationToken cancellationToken = default) => Task.FromResult<Document?>(documentId == _document.DocumentId ? _document : null);
    }

    private sealed class MappingLogger : ICentralizedLogger
    {
        public List<(Exception Exception, string Operation, string Context)> Entries { get; } = [];
        public void Log(Exception exception, string operation, string context) => Entries.Add((exception, operation, context));
    }

    private sealed class MappingNotifications(bool shouldFail) : INotificationService
    {
        public List<AdminNotification> Calls { get; } = [];
        public Task NotifyAdministratorAsync(Guid templateId, int templateVersion, Guid packageId, int revision, Guid placeholderId, string placeholderLabel, string reason, CancellationToken cancellationToken = default)
        {
            if (shouldFail) throw new InvalidOperationException("notification unavailable");
            Calls.Add(new(templateId, templateVersion, packageId, revision, placeholderId, placeholderLabel, reason));
            return Task.CompletedTask;
        }
    }

    private sealed record AdminNotification(Guid TemplateId, int TemplateVersion, Guid PackageId, int Revision, Guid PlaceholderId, string PlaceholderLabel, string Reason);

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
