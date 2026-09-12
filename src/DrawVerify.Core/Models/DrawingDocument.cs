using DrawVerify.Core.Enums;

namespace DrawVerify.Core.Models;

public sealed class DrawingDocument
{
    public Guid DocumentId { get; init; }
    public DrawingSource Source { get; init; }
    public Guid VersionId { get; init; }
}
