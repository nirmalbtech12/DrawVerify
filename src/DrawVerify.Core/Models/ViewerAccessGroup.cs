namespace DrawVerify.Core.Models;

public sealed record ViewerAccessGroup(string Id, string DisplayName, string Description, IReadOnlyList<ViewerAccessTarget> Members)
{
    public ViewerAccessGroup(string id, string displayName, string description) : this(id, displayName, description, Array.Empty<ViewerAccessTarget>()) { }
}
