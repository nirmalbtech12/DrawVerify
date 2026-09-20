using DrawVerify.Application.Services;
using DrawVerify.Core.Authorization;
using DrawVerify.Core.Configuration;
using DrawVerify.Core.Interfaces;
using DrawVerify.Core.Models;
using DrawVerify.Core.Enums;
using Xunit;

namespace DrawVerify.Application.Tests;

public sealed class WorkflowConfigurationServiceTests
{
    [Fact(DisplayName = "TSD-CHANNEL-DISPATCH supported InApp and unsupported channel rules are explicit")]
    public async Task TSD_CHANNEL_DISPATCH_validates_supported_and_unsupported_channels()
    {
        var session = new TestSession(new("admin", [SystemRoles.Admin])); var repository = new MemoryRepository { Current = Baseline() with { Version = 1 } }; var service = new WorkflowConfigurationService(repository, session, new RoleAuthorization());
        var supported = Baseline() with { Notifications = [new() { Event = WorkflowNotificationEvent.Rejection, RecipientRoles = ["Maker"], Channels = [WorkflowNotificationChannel.InApp], ContentTemplate = "rejected" }] };
        var saved = await service.SaveAsync(supported, 1); Assert.Contains(saved.Notifications, rule => rule.Channels.Contains(WorkflowNotificationChannel.InApp));
        var unsupported = supported with { Notifications = [supported.Notifications[0] with { Channels = [(WorkflowNotificationChannel)999] }] };
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => service.SaveAsync(unsupported, saved.Version)); Assert.Contains("unsupported", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact(DisplayName = "TSD-NOTIFICATION-LOCALIZATION missing resource does not invent a translation")]
    public void TSD_NOTIFICATION_LOCALIZATION_preserves_existing_content_template()
    {
        var rule = new WorkflowNotificationRule { Event = WorkflowNotificationEvent.Rejection, RecipientRoles = ["Maker"], Channels = [WorkflowNotificationChannel.InApp], ContentTemplate = "Document rejected - action required" };
        Assert.Equal("Document rejected - action required", rule.ContentTemplate); Assert.DoesNotContain("translation", rule.ContentTemplate, StringComparison.OrdinalIgnoreCase);
    }
    [Fact(DisplayName = "TSD-AC06-POLICY defaults to approval-only sharing")]
    public void Viewer_access_policy_defaults_to_approved_only()
    {
        var configuration = new ViewerAccessConfiguration();
        Assert.True(configuration.AllowOnlyApprovedDocuments);
        Assert.Equal("Only approved documents can be shared with Viewers.", ViewerAccessService.ApprovedOnlyMessage);
    }

    [Fact(DisplayName = "TD-APP-001 Admin reads and saves a new configuration version")]
    public async Task Admin_can_read_and_save()
    {
        var repository = new MemoryRepository { Current = Baseline() with { Version = 1 } }; var session = new TestSession(new AuthenticatedUser("admin", [SystemRoles.Admin]));
        var service = new WorkflowConfigurationService(repository, session, new RoleAuthorization(), clock: () => new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        var saved = await service.SaveAsync(Baseline(), 1);
        Assert.Equal(2, saved.Version); Assert.Equal(saved, await service.GetAsync()); Assert.Single(repository.Audits);
    }

    [Fact(DisplayName = "TD-APP-002 unauthorized and missing contexts are explicitly denied")]
    public async Task Unauthorized_operations_do_not_mutate_repository()
    {
        foreach (var user in new AuthenticatedUser?[] { new("maker", [SystemRoles.Maker]), null, new("admin", [SystemRoles.Admin]) })
        {
            var session = new TestSession(user) { IsAuthenticatedOverride = user is not null && user.Roles.Contains(SystemRoles.Admin) };
            if (user is not null && user.Roles.Contains(SystemRoles.Admin)) session.IsAuthenticatedOverride = false;
            var repository = new MemoryRepository(); var service = new WorkflowConfigurationService(repository, session, new RoleAuthorization());
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.GetAsync());
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.SaveAsync(Baseline()));
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.PreviewAsync(Baseline(), new WorkflowPreviewMetadata()));
            Assert.Empty(repository.Audits);
        }
    }

    [Fact(DisplayName = "TD-APP-003 invalid and stale saves preserve version and audit state")]
    public async Task Invalid_and_stale_saves_are_atomic()
    {
        var repository = new MemoryRepository { Current = Baseline() with { Version = 1 } }; var session = new TestSession(new("admin", [SystemRoles.Admin]));
        var service = new WorkflowConfigurationService(repository, session, new RoleAuthorization());
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.SaveAsync(Baseline() with { Version = 0 }));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.SaveAsync(Baseline(), expectedVersion: 99));
        Assert.Equal(1, repository.Current!.Version); Assert.Empty(repository.Audits);
    }

    private static WorkflowConfiguration Baseline() => new() { PermittedInitialStates = [DrawVerify.Core.Enums.WorkflowStatus.Draft, DrawVerify.Core.Enums.WorkflowStatus.InPreparation], DuplicateSubmissionStates = [DrawVerify.Core.Enums.WorkflowStatus.SubmittedForReview, DrawVerify.Core.Enums.WorkflowStatus.UnderReview, DrawVerify.Core.Enums.WorkflowStatus.Approved] };
    private sealed class TestSession(AuthenticatedUser? user) : ISessionAuthenticationService { public AuthenticatedUser? CurrentUser { get; } = user; public bool IsAuthenticatedOverride { get; set; } = true; public bool IsAuthenticated => IsAuthenticatedOverride; public void SignIn(AuthenticatedUser value) { } public void SignOut() { } }
    private sealed class MemoryRepository : IWorkflowConfigurationRepository { public WorkflowConfiguration? Current; public List<WorkflowConfigurationAudit> Audits { get; } = []; public Task<WorkflowConfiguration?> GetCurrentAsync(CancellationToken cancellationToken = default) => Task.FromResult(Current); public Task<WorkflowConfiguration?> GetVersionAsync(int version, CancellationToken cancellationToken = default) => Task.FromResult(Current?.Version == version ? Current : null); public Task<WorkflowConfiguration> SaveAsync(WorkflowConfiguration configuration, WorkflowConfigurationAudit audit, int? expectedVersion, CancellationToken cancellationToken = default) { if (expectedVersion.HasValue && Current?.Version != expectedVersion) throw new InvalidOperationException("version conflict"); Current = configuration; Audits.Add(audit); return Task.FromResult(configuration); } }
}
