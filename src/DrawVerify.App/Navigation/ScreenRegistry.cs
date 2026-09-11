using DrawVerify.Core.Authorization;

namespace DrawVerify.App.Navigation;

public static class ScreenRegistry
{
    public const string Dashboard = "Dashboard";
    public const string StepUpload = "StepUpload";
    public const string DxfUpload = "DxfUpload";
    public const string Verification = "Verification";
    public const string Workflow = "Workflow";
    public const string UserAdministration = "UserAdministration";
    public const string AuditLog = "AuditLog";
    public const string Report = "Report";
    public const string Help = "Help";

    public static IReadOnlyList<MenuDefinition> Menus { get; } = new[]
    {
        new MenuDefinition("_File", Dashboard, Children: new[] { new MenuDefinition("_Dashboard / Home", Dashboard), new MenuDefinition("_Logout / Exit", "Logout") }),
        new MenuDefinition("_Upload", "Upload", RolePermission.Upload, Children: new[] { new MenuDefinition("Upload _Model (STEP)", StepUpload, RolePermission.Upload), new MenuDefinition("Upload _Drawing (DXF)", DxfUpload, RolePermission.Upload) }),
        new MenuDefinition("_Verification", "Verification", Children: new[] { new MenuDefinition("_My Verifications", Verification), new MenuDefinition("_All Verifications", Verification, RolePermission.ReviewVerification) }),
        new MenuDefinition("_Workflow", "Workflow", Children: new[] { new MenuDefinition("_My Submissions", Workflow, RequiredRole: SystemRoles.Maker), new MenuDefinition("_To Review", Workflow, RequiredRole: SystemRoles.Checker), new MenuDefinition("_To Approve", Workflow, RequiredRole: SystemRoles.Approver) }),
        new MenuDefinition("_Admin", "Admin", RolePermission.ManageUsersAndRoles, Children: new[] { new MenuDefinition("_User Management", UserAdministration, RolePermission.ManageUsersAndRoles), new MenuDefinition("_Role Assignment", UserAdministration, RolePermission.ManageUsersAndRoles), new MenuDefinition("_Workflow Configuration", UserAdministration, RolePermission.ManageGlobalConfiguration) }),
        new MenuDefinition("_Help", Help)
    };

    public static string Title(string target) => target switch { StepUpload => "Upload Model", DxfUpload => "Upload Drawing", Verification => "Verification", Workflow => "Workflow", UserAdministration => "Administration", AuditLog => "Audit Log", Report => "Report", Help => "Help", _ => "Dashboard" };
}
