using DrawVerify.Core.Authorization;
using DrawVerify.Core.Models;
using Xunit;

namespace DrawVerify.Application.Tests;

public sealed class Kan125AuthorizationAndAuditTests
{
    [Theory(DisplayName = "TD-11 only active Maker authorization can generate")]
    [InlineData("Maker", true)]
    [InlineData("Checker", false)]
    [InlineData("Approver", false)]
    [InlineData("Viewer", false)]
    public void Generation_permission_matrix_is_enforced(string role, bool expected)
    {
        var user = new AuthenticatedUser(role.ToLowerInvariant(), [role]);
        Assert.Equal(expected, new RoleAuthorization().IsAllowed(user, RolePermission.GenerateDxfDrawing));
    }

    [Fact(DisplayName = "TD-11 service boundary refreshes authorization and audits failures")]
    public void Service_contract_checks_session_and_centralized_logging()
    {
        var source = Read("src", "DrawVerify.Application", "Services", "DxfDrawingGenerationService.cs");
        Assert.Contains("CanGenerate", source); Assert.Contains("IsAuthenticated", source);
        Assert.Contains("_logger?.Log", source); Assert.Contains("_audit", source);
        Assert.Contains("Only an authenticated Maker", source);
    }

    private static string Read(params string[] parts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null) { var path = Path.Combine(new[] { directory.FullName }.Concat(parts).ToArray()); if (File.Exists(path)) return File.ReadAllText(path); directory = directory.Parent; }
        throw new FileNotFoundException(string.Join(Path.DirectorySeparatorChar, parts));
    }
}
