using System.Windows.Input;
using DrawVerify.App.ViewModels;
using DrawVerify.Core.Interfaces;
using DrawVerify.Core.Models;
using Xunit;

namespace DrawVerify.App.Tests;

public sealed class ViewerAccessViewModelTests
{
    [Fact(DisplayName = "TSD-AC15-FEEDBACK refreshes grants and exposes success feedback")]
    public async Task Successful_mutation_refreshes_grants_and_reports_count()
    {
        var service = new FakeService();
        var model = new ViewerAccessViewModel(service, Guid.NewGuid(), Guid.NewGuid());
        await WaitUntilAsync(() => service.LoadCount >= 1, TimeSpan.FromSeconds(1));
        var initialLoadCount = service.LoadCount;
        model.SelectedTarget = service.Target;
        model.GrantCommand.Execute(null);
        await WaitUntilAsync(() => service.LoadCount > initialLoadCount && model.Status.Contains("updated", StringComparison.Ordinal), TimeSpan.FromSeconds(1));
        Assert.Contains("1", model.Status, StringComparison.Ordinal);
        Assert.True(service.LoadCount > initialLoadCount);
    }

    private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (!condition() && DateTime.UtcNow < deadline) await Task.Yield();
        Assert.True(condition());
    }

    private sealed class FakeService : IViewerAccessService
    {
        public ViewerAccessTarget Target { get; } = new(ViewerAccessTargetKind.User, "viewer-1", "Viewer One");
        public int LoadCount { get; private set; }
        public bool CanManageAccess => true;
        public Task<IReadOnlyList<ViewerAccessTarget>> SearchTargetsAsync(string search, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<ViewerAccessTarget>>(new[] { Target });
        public Task<IReadOnlyList<ViewerAccessGrant>> ListGrantsAsync(Guid documentId, Guid versionId, CancellationToken cancellationToken = default) { LoadCount++; return Task.FromResult<IReadOnlyList<ViewerAccessGrant>>(Array.Empty<ViewerAccessGrant>()); }
        public Task<ViewerAccessMutationResult> GrantAsync(Guid documentId, Guid versionId, ViewerAccessChange change, CancellationToken cancellationToken = default) => Task.FromResult(new ViewerAccessMutationResult(1, Array.Empty<string>()));
        public Task<ViewerAccessMutationResult> RevokeAsync(Guid documentId, Guid versionId, IReadOnlyList<ViewerAccessTarget> targets, CancellationToken cancellationToken = default) => Task.FromResult(new ViewerAccessMutationResult(targets.Count, Array.Empty<string>()));
        public Task<bool> HasAccessAsync(string userId, Guid documentId, Guid versionId, CancellationToken cancellationToken = default) => Task.FromResult(true);
    }
}
