using DrawVerify.Application.Services;
using DrawVerify.Core.Authorization;
using DrawVerify.Core.Interfaces;
using DrawVerify.Core.Models;
using Xunit;

namespace DrawVerify.Application.Tests;

public sealed class ViewerAccessServiceTests
{
    [Fact(DisplayName = "TSD-AC02-TARGETS searches active Viewer-capable targets")]
    public async Task Targets_are_searchable_and_preserve_target_kinds()
    {
        var user = Target(ViewerAccessTargetKind.User, "viewer-1", "Viewer One");
        var group = Target(ViewerAccessTargetKind.Group, "production", "Production Team");
        var role = Target(ViewerAccessTargetKind.Role, "External Viewer", "External Viewer");
        var repository = new AccessRepositoryFake { SearchResults = new[] { user, group, role } };
        var service = Create(repository, SystemRoles.Approver);

        var results = await service.SearchTargetsAsync("viewer");

        Assert.Equal(new[] { ViewerAccessTargetKind.User, ViewerAccessTargetKind.Group, ViewerAccessTargetKind.Role }, results.Select(x => x.Kind));
        Assert.Equal("viewer", repository.LastSearch);
    }

    [Fact(DisplayName = "TSD-AC03-GRANT grants selected targets and returns mutation feedback")]
    public async Task Grant_forwards_selected_targets_and_actor()
    {
        var target = Target(ViewerAccessTargetKind.User, "viewer-1", "Viewer One");
        var repository = new AccessRepositoryFake();
        var service = Create(repository, SystemRoles.Approver);

        var result = await service.GrantAsync(Guid.NewGuid(), Guid.NewGuid(), new ViewerAccessChange(new[] { target }));

        Assert.True(result.Succeeded);
        Assert.Equal(1, result.AffectedTargetCount);
        Assert.Equal(SystemRoles.Approver, repository.LastActor!.Roles.Single());
        Assert.Equal(target, repository.LastChange!.Targets.Single());
    }

    [Fact(DisplayName = "TSD-AC07-EXPIRATION validates optional future UTC expiration")]
    public async Task Expiration_requires_enabled_configuration_and_future_utc_time()
    {
        var target = Target(ViewerAccessTargetKind.User, "viewer-1", "Viewer One");
        var disabled = Create(new AccessRepositoryFake(), SystemRoles.Approver);
        await Assert.ThrowsAsync<InvalidOperationException>(() => disabled.GrantAsync(Guid.NewGuid(), Guid.NewGuid(), new ViewerAccessChange(new[] { target }, DateTime.UtcNow.AddHours(1))));

        var repository = new AccessRepositoryFake();
        var enabled = Create(repository, SystemRoles.Approver, new ViewerAccessConfiguration { AllowExpiration = true });
        var expiration = DateTime.UtcNow.AddHours(1);
        await enabled.GrantAsync(Guid.NewGuid(), Guid.NewGuid(), new ViewerAccessChange(new[] { target }, expiration));
        Assert.Equal(expiration, repository.LastChange!.ExpiresUtc);
    }

    [Fact(DisplayName = "TSD-AC08-BULK validates configured mutation summary inputs")]
    public async Task Bulk_configuration_is_observable_without_changing_single_mutation_contract()
    {
        var target = Target(ViewerAccessTargetKind.Group, "production", "Production Team");
        var repository = new AccessRepositoryFake();
        var service = Create(repository, SystemRoles.Approver, new ViewerAccessConfiguration { AllowBulkActions = true });

        var result = await service.GrantAsync(Guid.NewGuid(), Guid.NewGuid(), new ViewerAccessChange(new[] { target }));

        Assert.Equal("Viewer access updated for 1 target.", result.Summary);
        Assert.Equal(target, repository.LastChange!.Targets.Single());
    }

    [Fact(DisplayName = "TSD-AC15-FEEDBACK preserves clear mutation results")]
    public async Task Failed_mutation_result_is_not_reported_as_success()
    {
        var repository = new AccessRepositoryFake { GrantResult = new ViewerAccessMutationResult(0, new[] { "Unknown target." }) };
        var service = Create(repository, SystemRoles.Approver);
        var result = await service.GrantAsync(Guid.NewGuid(), Guid.NewGuid(), new ViewerAccessChange(new[] { Target(ViewerAccessTargetKind.User, "missing", "Missing") }));
        Assert.False(result.Succeeded);
        Assert.Equal("Unknown target.", result.Errors.Single());
    }

    private static ViewerAccessService Create(AccessRepositoryFake repository, string role, ViewerAccessConfiguration? configuration = null)
    {
        var session = new SessionAuthenticationService();
        session.SignIn(new AuthenticatedUser("actor", new[] { role }) { UserId = "actor-1", DisplayName = "Actor" });
        return new ViewerAccessService(repository, session, new RoleAuthorization(), new NoOpLogger(), configuration);
    }

    private static ViewerAccessTarget Target(ViewerAccessTargetKind kind, string id, string name) => new(kind, id, name, "Viewer read access");

    private sealed class AccessRepositoryFake : IViewerAccessRepository
    {
        public IReadOnlyList<ViewerAccessTarget> SearchResults { get; init; } = Array.Empty<ViewerAccessTarget>();
        public ViewerAccessMutationResult GrantResult { get; init; } = new(1, Array.Empty<string>());
        public string? LastSearch { get; private set; }
        public AuthenticatedUser? LastActor { get; private set; }
        public ViewerAccessChange? LastChange { get; private set; }
        public Task<IReadOnlyList<ViewerAccessTarget>> SearchTargetsAsync(string search, CancellationToken cancellationToken = default) { LastSearch = search; return Task.FromResult(SearchResults); }
        public Task<IReadOnlyList<ViewerAccessGrant>> ListGrantsAsync(Guid documentId, Guid versionId, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<ViewerAccessGrant>>(Array.Empty<ViewerAccessGrant>());
        public Task<bool> HasAccessAsync(string userId, Guid documentId, Guid versionId, CancellationToken cancellationToken = default) => Task.FromResult(false);
        public Task<ViewerAccessMutationResult> GrantAsync(Guid documentId, Guid versionId, ViewerAccessChange change, AuthenticatedUser actor, CancellationToken cancellationToken = default) { LastActor = actor; LastChange = change; return Task.FromResult(GrantResult); }
        public Task<ViewerAccessMutationResult> RevokeAsync(Guid documentId, Guid versionId, IReadOnlyList<ViewerAccessTarget> targets, AuthenticatedUser actor, CancellationToken cancellationToken = default) { LastActor = actor; return Task.FromResult(new ViewerAccessMutationResult(targets.Count, Array.Empty<string>())); }
    }

    private sealed class NoOpLogger : ICentralizedLogger { public void Log(Exception exception, string operation, string context) { } }
}
