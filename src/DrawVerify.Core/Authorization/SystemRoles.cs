using System.Collections.ObjectModel;
using DrawVerify.Core.Models;

namespace DrawVerify.Core.Authorization;

public static class SystemRoles
{
    public const string Admin = "Admin";
    public const string Maker = "Maker";
    public const string Checker = "Checker";
    public const string Approver = "Approver";
    public const string Viewer = "Viewer";
    private static readonly ReadOnlyCollection<RoleDefinition> Definitions = new(new[]
    {
        new RoleDefinition(Admin, Admin, "Manage users, roles, and global configuration."),
        new RoleDefinition(Maker, Maker, "Upload STEP/DXF files, trigger extraction, and start verification."),
        new RoleDefinition(Checker, Checker, "Review matches, resolve unmatched items, and approve or reject verification results."),
        new RoleDefinition(Approver, Approver, "Perform final approval, control document access, and generate final reports."),
        new RoleDefinition(Viewer, Viewer, "Read-only access to approved documents and reports.")
    });
    public static IReadOnlyList<RoleDefinition> Catalog => Definitions;
    public static IReadOnlyList<RoleDefinition> WorkflowRoles { get; } =
        Definitions.Where(x => x.Id is Maker or Checker or Approver).ToArray();
    public static bool TryCanonicalize(string? value, out string canonical)
    {
        canonical = string.Empty;
        if (string.IsNullOrWhiteSpace(value)) return false;
        var definition = Definitions.FirstOrDefault(x => string.Equals(x.Id, value.Trim(), StringComparison.OrdinalIgnoreCase));
        if (definition is null) return false;
        canonical = definition.Id;
        return true;
    }
    public static IReadOnlyList<string> ValidateAndCanonicalize(IEnumerable<string>? roles)
    {
        var result = new List<string>();
        foreach (var role in roles ?? Array.Empty<string>())
        {
            if (!TryCanonicalize(role, out var canonical)) throw new InvalidOperationException($"Unknown or blank system role '{role}'.");
            if (result.Contains(canonical, StringComparer.OrdinalIgnoreCase)) throw new InvalidOperationException($"Duplicate system role '{canonical}'.");
            result.Add(canonical);
        }
        return result.AsReadOnly();
    }

    public static IReadOnlyList<string> ValidateAndCanonicalizeWorkflowRoles(IEnumerable<string>? roles)
    {
        var canonical = ValidateAndCanonicalize(roles);
        if (canonical.Any(role => role is Admin or Viewer))
            throw new InvalidOperationException("Only Maker, Checker, and Approver can be assigned as workflow roles.");
        return canonical;
    }
}
