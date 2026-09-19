using DrawVerify.Core.Models;

namespace DrawVerify.Core.Interfaces;

public sealed record ViewerAccessBulkItem(Guid DocumentId, Guid VersionId);
public sealed record ViewerAccessBulkRequest(IReadOnlyList<ViewerAccessBulkItem> Documents, IReadOnlyList<ViewerAccessTarget> Targets, bool Revoke, DateTime? ExpiresUtc = null);
public sealed record ViewerAccessBulkPreview(int DocumentCount, int TargetCount, string Summary, IReadOnlyList<string> Errors)
{
    public bool Succeeded => Errors.Count == 0;
}

public interface IViewerAccessService
{
    bool CanManageAccess { get; }
    Task<IReadOnlyList<ViewerAccessTarget>> SearchTargetsAsync(string search, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ViewerAccessGrant>> ListGrantsAsync(Guid documentId, Guid versionId, CancellationToken cancellationToken = default);
    Task<ViewerAccessMutationResult> GrantAsync(Guid documentId, Guid versionId, ViewerAccessChange change, CancellationToken cancellationToken = default);
    Task<ViewerAccessMutationResult> RevokeAsync(Guid documentId, Guid versionId, IReadOnlyList<ViewerAccessTarget> targets, CancellationToken cancellationToken = default);
    Task<bool> HasAccessAsync(string userId, Guid documentId, Guid versionId, CancellationToken cancellationToken = default);
    Task<int> GetActiveShareCountAsync(Guid documentId, Guid versionId, CancellationToken cancellationToken = default) => Task.FromResult(0);
    Task<ViewerAccessBulkPreview> PreviewBulkAsync(ViewerAccessBulkRequest request, CancellationToken cancellationToken = default) => Task.FromException<ViewerAccessBulkPreview>(new NotSupportedException("Bulk Viewer access actions are not supported by this service."));
    Task<ViewerAccessMutationResult> ConfirmBulkAsync(ViewerAccessBulkRequest request, CancellationToken cancellationToken = default) => Task.FromException<ViewerAccessMutationResult>(new NotSupportedException("Bulk Viewer access actions are not supported by this service."));
}
