using DrawVerify.Application.Services;
using DrawVerify.Core.Authorization;
using DrawVerify.Core.Enums;
using DrawVerify.Core.Interfaces;
using DrawVerify.Core.Models;
using Xunit;

namespace DrawVerify.Application.Tests;

public sealed class InternalSharedLinkAuthorizationTests
{
    [Fact(DisplayName = "TSD-AC02 TSD-AC09 internal authorization denies external and unauthenticated retrieval")]
    public async Task Identity_policy_allows_internal_users_and_denies_external_or_unauthenticated_without_retrieval()
    {
        var policy = new InternalSharedLinkPolicy { InternalDomain = "drawverify.internal", AllowedRoles = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Viewer" } };
        var document = new Document { DocumentId = Guid.NewGuid(), Status = DocumentStatus.Active };
        var version = new DocumentVersion { DocumentId = document.DocumentId, VersionId = Guid.NewGuid(), VersionNumber = 1 };
        var link = new InternalSharedLink { DocumentId = document.DocumentId, VersionId = version.VersionId, Token = "hash", IsActive = true, TrackingEnabled = false };
        var documents = new DocumentsFake(document, version);
        var session = new SessionFake(new AuthenticatedUser("viewer@drawverify.internal", new[] { "Viewer" }));
        var service = new InternalSharedLinkService(documents, new LinksFake(link), session, identity: new Provider(policy), policy: policy, baseUrl: "https://drawverify.internal/share", workflow: new WorkflowFake());
        var internalUser = new AuthenticatedUser("viewer@drawverify.internal", new[] { "Viewer" });
        var externalUser = new AuthenticatedUser("viewer@external.example", new[] { "Viewer" });
        var allowed = await service.AuthorizeAsync(new InternalSharedLinkAccessRequest("any"));
        Assert.True(allowed.Succeeded);
        Assert.Equal(1, documents.RetrievalCount);

        session.SignIn(externalUser);
        var external = await service.AuthorizeAsync(new InternalSharedLinkAccessRequest("any"));
        Assert.Equal(InternalSharedLinkAccessFailure.Unauthorized, external.Failure);
        Assert.Equal(1, documents.RetrievalCount);

        session.SignOut();
        var unauthenticated = await service.AuthorizeAsync(new InternalSharedLinkAccessRequest("any"));
        Assert.Equal(InternalSharedLinkAccessFailure.AuthenticationRequired, unauthenticated.Failure);
        Assert.Equal(1, documents.RetrievalCount);
    }

    private sealed class Provider(InternalSharedLinkPolicy policy) : ISharedLinkIdentityProvider
    {
        public bool IsAuthenticated => true;
        public bool IsInternal(AuthenticatedUser user) => user.Username.EndsWith("@" + policy.InternalDomain, StringComparison.OrdinalIgnoreCase);
        public bool IsAllowed(AuthenticatedUser user, InternalSharedLink link) => user.Roles.Any(policy.AllowedRoles.Contains);
    }

    private sealed class DocumentsFake(Document document, DocumentVersion version) : IDocumentRepository
    {
        public int RetrievalCount { get; private set; }
        public Task<Document?> GetAsync(Guid id, CancellationToken cancellationToken = default) { RetrievalCount++; return Task.FromResult<Document?>(id == document.DocumentId ? document : null); }
        public Task<DocumentVersion?> GetVersionAsync(Guid id, Guid versionId, CancellationToken cancellationToken = default) => Task.FromResult<DocumentVersion?>(id == document.DocumentId && versionId == version.VersionId ? version : null);
    }

    private sealed class LinksFake(InternalSharedLink link) : IInternalSharedLinkRepository
    {
        public Task<InternalSharedLink> CreateAsync(InternalSharedLink value, CancellationToken cancellationToken = default) => Task.FromResult(value);
        public Task<InternalSharedLink?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) => Task.FromResult<InternalSharedLink?>(link);
        public Task<InternalSharedLink?> FindByTokenAsync(string tokenHash, CancellationToken cancellationToken = default) => Task.FromResult<InternalSharedLink?>(link);
        public Task<IReadOnlyList<InternalSharedLink>> ListAsync(Guid documentId, Guid? versionId = null, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<InternalSharedLink>>(new[] { link });
        public Task RevokeAsync(Guid id, DateTime revokedUtc, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<InternalSharedLink> RegenerateAsync(Guid id, InternalSharedLink replacement, DateTime revokedUtc, CancellationToken cancellationToken = default) => Task.FromResult(replacement);
        public Task<InternalSharedLinkUsage> GetUsageAsync(Guid id, bool includeUsers, CancellationToken cancellationToken = default) => Task.FromResult(new InternalSharedLinkUsage(0, null, Array.Empty<string>()));
        public Task RecordAccessAsync(Guid id, string userId, DateTime accessedUtc, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class SessionFake(AuthenticatedUser? user) : ISessionAuthenticationService
    {
        public AuthenticatedUser? CurrentUser { get; private set; } = user;
        public bool IsAuthenticated => CurrentUser is not null;
        public void SignIn(AuthenticatedUser value) => CurrentUser = value;
        public void SignOut() => CurrentUser = null;
    }

    private sealed class WorkflowFake : IWorkflowRepository
    {
        public Task<WorkflowState?> GetStateAsync(Guid packageId, CancellationToken cancellationToken = default) => Task.FromResult<WorkflowState?>(new WorkflowState { PackageId = packageId, Status = WorkflowStatus.FinallyApproved, Revision = 1 });
        public Task<ReviewSubmission?> GetSubmissionAsync(Guid submissionId, CancellationToken cancellationToken = default) => Task.FromResult<ReviewSubmission?>(null);
        public Task<IReadOnlyList<ReviewSubmission>> GetSubmissionsAsync(Guid packageId, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<ReviewSubmission>>(Array.Empty<ReviewSubmission>());
        public Task<IReadOnlyList<WorkflowQueueItem>> GetCheckerQueueAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<WorkflowQueueItem>>(Array.Empty<WorkflowQueueItem>());
        public Task<ReviewSubmission> SubmitAtomicallyAsync(WorkflowState expectedState, ReviewSubmission submission, AuditEntry auditEntry, CancellationToken cancellationToken = default) => Task.FromResult(submission);
        public Task RecordAuditAsync(AuditEntry auditEntry, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task MarkSupersededAsync(Guid submissionId, AuditEntry auditEntry, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
