namespace DrawVerify.Core.Models;

public sealed class ViewerAccessConfiguration
{
    public bool AllowOnlyApprovedDocuments { get; init; } = true;
    public bool AllowExpiration { get; init; }
    public bool AllowBulkActions { get; init; }
}
