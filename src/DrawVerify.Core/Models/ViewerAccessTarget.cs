namespace DrawVerify.Core.Models;

public enum ViewerAccessTargetKind { User, Group, Role }

public sealed record ViewerAccessTarget(ViewerAccessTargetKind Kind, string Id, string DisplayName, string Description = "")
{
    public string TargetId => Id;
    public string TargetType => Kind.ToString();
}
