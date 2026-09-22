using DrawVerify.Core.Authorization;

namespace DrawVerify.Core.Models;

public sealed record SignaturePlaceholderAssignment
{
    public Guid PlaceholderId { get; init; }
    public string Role { get; init; } = string.Empty;
    public string? AssignedSignerUserId { get; init; }
    public bool IsSigned { get; init; }
    public string? SignedByUserId { get; init; }
    public DateTimeOffset? SignedUtc { get; init; }

    public void Validate()
    {
        if (PlaceholderId == Guid.Empty) throw new InvalidOperationException("Signature placeholder identifier is required.");
        if (!SystemRoles.TryCanonicalize(Role, out var canonical) || !SystemRoles.WorkflowRoles.Any(x => string.Equals(x.Id, canonical, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException($"Signature placeholder '{PlaceholderId}' has an invalid workflow role.");
        if (IsSigned && (string.IsNullOrWhiteSpace(SignedByUserId) || SignedUtc is null))
            throw new InvalidOperationException($"Signed placeholder '{PlaceholderId}' must include signer identity and UTC time.");
        if (SignedUtc is { } timestamp && timestamp.Offset != TimeSpan.Zero)
            throw new InvalidOperationException($"Signed placeholder '{PlaceholderId}' timestamp must be UTC.");
    }

    public bool IsEligible(string? userId, IEnumerable<string>? roles)
    {
        if (!SystemRoles.TryCanonicalize(Role, out var canonicalRole) || roles is null || !roles.Any(role => string.Equals(role, canonicalRole, StringComparison.OrdinalIgnoreCase))) return false;
        return string.IsNullOrWhiteSpace(AssignedSignerUserId) || string.Equals(AssignedSignerUserId, userId, StringComparison.Ordinal);
    }
}

public sealed record TemplateMappingSnapshot
{
    public Guid TemplateId { get; init; }
    public int TemplateVersion { get; init; }
    public IReadOnlyList<SignaturePlaceholderAssignment> Assignments { get; init; } = Array.Empty<SignaturePlaceholderAssignment>();
    public IReadOnlyDictionary<string, string> CustomFieldMappings { get; init; } = new Dictionary<string, string>();

    public void Validate()
    {
        if (TemplateId == Guid.Empty || TemplateVersion < 1) throw new InvalidOperationException("Template identity and version are required for mapping metadata.");
        var seen = new HashSet<Guid>();
        foreach (var assignment in Assignments)
        {
            assignment.Validate();
            if (!seen.Add(assignment.PlaceholderId)) throw new InvalidOperationException($"Placeholder '{assignment.PlaceholderId}' is assigned more than once.");
        }
    }
}
