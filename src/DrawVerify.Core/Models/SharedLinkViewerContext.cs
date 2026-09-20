namespace DrawVerify.Core.Models;

public sealed record SharedLinkViewerContext(Guid LinkId, Guid DocumentId, Guid VersionId, bool CanDownload)
{
    public bool IsReadOnly => true;
    public bool CanEdit => false;
    public bool CanWorkflow => false;
    public bool CanReshare => false;
}
