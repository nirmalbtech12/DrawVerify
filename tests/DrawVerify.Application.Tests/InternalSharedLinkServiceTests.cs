using DrawVerify.Application.Services;
using DrawVerify.Core.Authorization;
using DrawVerify.Core.Enums;
using DrawVerify.Core.Interfaces;
using DrawVerify.Core.Models;
using Xunit;

namespace DrawVerify.Application.Tests;

public sealed class InternalSharedLinkServiceTests
{
    [Fact(DisplayName = "TSD-AC01 approved Approver generation persists a unique full internal URL")]
    public async Task Approved_approver_generation_returns_persisted_url()
    {
        var document = new Document { DocumentId = Guid.NewGuid(), Status = DocumentStatus.Active };
        var version = new DocumentVersion { DocumentId = document.DocumentId, VersionId = Guid.NewGuid() };
        var documents = new DocumentsFake(document, version);
        var links = new LinksFake();
        var session = new SessionFake(new AuthenticatedUser("approver@drawverify.internal", new[] { SystemRoles.Approver }));
        var service = new InternalSharedLinkService(documents, links, session, baseUrl: "https://drawverify.internal/share");

        var first = await service.GenerateAsync(document.DocumentId, version.VersionId, new InternalSharedLinkConfiguration());
        var second = await service.RegenerateAsync(first.Link.LinkId);

        Assert.StartsWith("https://drawverify.internal/share?token=", first.FullUrl, StringComparison.Ordinal);
        Assert.NotEqual(first.Link.LinkId, second.Link.LinkId);
        Assert.Equal(2, links.Items.Count);
    }

    [Fact(DisplayName = "TSD-AC01 generation rejects missing non-approved unauthorized and persistence failures")]
    public async Task Generation_failures_are_clear_and_do_not_create_partial_links()
    {
        var documentId = Guid.NewGuid();
        var versionId = Guid.NewGuid();
        var version = new DocumentVersion { DocumentId = documentId, VersionId = versionId };
        var links = new LinksFake();
        var approver = new SessionFake(new AuthenticatedUser("approver@drawverify.internal", new[] { SystemRoles.Approver }));
        var missing = new InternalSharedLinkService(new DocumentsFake(null, version), links, approver);
        await Assert.ThrowsAsync<InvalidOperationException>(() => missing.GenerateAsync(documentId, versionId, new InternalSharedLinkConfiguration()));
        Assert.Empty(links.Items);

        var pending = new InternalSharedLinkService(new DocumentsFake(new Document { DocumentId = documentId, Status = DocumentStatus.Archived }, version), links, approver);
        var rejected = await Assert.ThrowsAsync<InvalidOperationException>(() => pending.GenerateAsync(documentId, versionId, new InternalSharedLinkConfiguration()));
        Assert.Equal(InternalSharedLinkService.ApprovedOnlyMessage, rejected.Message);
        Assert.Empty(links.Items);

        var unauthorized = new InternalSharedLinkService(new DocumentsFake(new Document { DocumentId = documentId, Status = DocumentStatus.Active }, version), links, new SessionFake(new AuthenticatedUser("viewer", new[] { SystemRoles.Viewer })));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => unauthorized.GenerateAsync(documentId, versionId, new InternalSharedLinkConfiguration()));
        Assert.Empty(links.Items);

        var failing = new LinksFake { ThrowOnCreate = true };
        var persistence = new InternalSharedLinkService(new DocumentsFake(new Document { DocumentId = documentId, Status = DocumentStatus.Active }, version), failing, approver);
        await Assert.ThrowsAsync<InvalidOperationException>(() => persistence.GenerateAsync(documentId, versionId, new InternalSharedLinkConfiguration()));
        Assert.Empty(failing.Items);
    }

    private sealed class DocumentsFake(Document? document, DocumentVersion version) : IDocumentRepository
    {
        public Task<Document?> GetAsync(Guid id, CancellationToken cancellationToken = default) => Task.FromResult<Document?>(document is not null && id == document.DocumentId ? document : null);
        public Task<DocumentVersion?> GetVersionAsync(Guid id, Guid versionId, CancellationToken cancellationToken = default) => Task.FromResult<DocumentVersion?>(document is not null && id == document.DocumentId && versionId == version.VersionId ? version : null);
    }
    private sealed class LinksFake : IInternalSharedLinkRepository
    {
        public bool ThrowOnCreate { get; init; }
        public List<InternalSharedLink> Items { get; } = new();
        public Task<InternalSharedLink> CreateAsync(InternalSharedLink link, CancellationToken cancellationToken = default) { if (ThrowOnCreate) throw new InvalidOperationException("persistence unavailable"); Items.Add(link); return Task.FromResult(link); }
        public Task<InternalSharedLink?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) => Task.FromResult(Items.SingleOrDefault(x => x.LinkId == id));
        public Task<InternalSharedLink?> FindByTokenAsync(string tokenHash, CancellationToken cancellationToken = default) => Task.FromResult(Items.SingleOrDefault(x => x.TokenHash == tokenHash));
        public Task<IReadOnlyList<InternalSharedLink>> ListAsync(Guid documentId, Guid? versionId = null, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<InternalSharedLink>>(Items.Where(x => x.DocumentId == documentId && (!versionId.HasValue || x.VersionId == versionId)).ToList());
        public Task RevokeAsync(Guid id, DateTime revokedUtc, CancellationToken cancellationToken = default) { var item = Items.Single(x => x.LinkId == id); Items[Items.IndexOf(item)] = item with { IsActive = false, RevokedUtc = revokedUtc }; return Task.CompletedTask; }
        public Task<InternalSharedLink> RegenerateAsync(Guid oldId, InternalSharedLink replacement, DateTime revokedUtc, CancellationToken cancellationToken = default) { RevokeAsync(oldId, revokedUtc, cancellationToken); Items.Add(replacement); return Task.FromResult(replacement); }
        public Task<InternalSharedLinkUsage> GetUsageAsync(Guid id, bool includeUsers, CancellationToken cancellationToken = default) => Task.FromResult(new InternalSharedLinkUsage(0, null, Array.Empty<string>()));
        public Task RecordAccessAsync(Guid id, string userId, DateTime accessedUtc, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
    private sealed class SessionFake(AuthenticatedUser user) : ISessionAuthenticationService
    {
        public AuthenticatedUser? CurrentUser { get; private set; } = user;
        public bool IsAuthenticated => CurrentUser is not null;
        public void SignIn(AuthenticatedUser value) => CurrentUser = value;
        public void SignOut() => CurrentUser = null;
    }
}
