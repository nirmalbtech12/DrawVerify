using DrawVerify.App.ViewModels;
using DrawVerify.Core.Interfaces;
using DrawVerify.Core.Models;
using Xunit;

namespace DrawVerify.App.Tests;

public sealed class DocumentDetailViewerAccessTests
{
    [Fact(DisplayName = "TSD-AC01-PANEL exposes Viewer Access state only when management is available")]
    public void Viewer_access_panel_state_follows_service_permission()
    {
        var documentId = Guid.NewGuid();
        var versionId = Guid.NewGuid();
        var available = new ViewerAccessViewModel(new ServiceDouble(true), documentId, versionId);
        var denied = new ViewerAccessViewModel(new ServiceDouble(false), documentId, versionId);
        Assert.True(available.CanManageAccess);
        Assert.False(denied.CanManageAccess);
    }

    private sealed class ServiceDouble(bool canManage) : IViewerAccessService
    {
        public bool CanManageAccess => canManage;
        public Task<IReadOnlyList<ViewerAccessTarget>> SearchTargetsAsync(string search, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<ViewerAccessTarget>>(Array.Empty<ViewerAccessTarget>());
        public Task<IReadOnlyList<ViewerAccessGrant>> ListGrantsAsync(Guid documentId, Guid versionId, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<ViewerAccessGrant>>(Array.Empty<ViewerAccessGrant>());
        public Task<ViewerAccessMutationResult> GrantAsync(Guid documentId, Guid versionId, ViewerAccessChange change, CancellationToken cancellationToken = default) => Task.FromResult(new ViewerAccessMutationResult(0, Array.Empty<string>()));
        public Task<ViewerAccessMutationResult> RevokeAsync(Guid documentId, Guid versionId, IReadOnlyList<ViewerAccessTarget> targets, CancellationToken cancellationToken = default) => Task.FromResult(new ViewerAccessMutationResult(0, Array.Empty<string>()));
        public Task<bool> HasAccessAsync(string userId, Guid documentId, Guid versionId, CancellationToken cancellationToken = default) => Task.FromResult(false);
    }
}
