using DrawVerify.Core.Authorization;

namespace DrawVerify.App.Navigation;

public static class ScreenRegistry
{
    public const string Dashboard = "Dashboard";
    public const string StepUpload = "StepUpload";
    public const string DxfUpload = "DxfUpload";
    public const string UploadVerify = "UploadVerify";
    public const string Verification = "Verification";
    public const string Workflow = "Workflow";
    public const string UserAdministration = "UserAdministration";
    public const string WorkflowConfiguration = "WorkflowConfiguration";
    public const string ReportTemplateConfiguration = "ReportTemplateConfiguration";
    public const string AuditLog = "AuditLog";
    public const string Report = "Report";
    public const string Help = "Help";
    public const string SharedDocuments = "SharedDocuments";
    public const string SharedLinkManagement = "SharedLinkManagement";
    public const string NotificationCenter = "NotificationCenter";

    public static IReadOnlyList<MenuDefinition> Menus { get; } = new[]
    {
        new MenuDefinition("_File", Dashboard, Children: new[] { new MenuDefinition("_Dashboard / Home", Dashboard), new MenuDefinition("_Notifications", NotificationCenter), new MenuDefinition("_Logout / Exit", "Logout") }),
        new MenuDefinition("_Upload & Verify", UploadVerify, RolePermission.Upload, Children: new[] { new MenuDefinition("_Combined Upload & Verify", UploadVerify, RolePermission.Upload) }),
        new MenuDefinition("_Verification", "Verification", Children: new[] { new MenuDefinition("_My Verifications", Verification), new MenuDefinition("_All Verifications", Verification, RolePermission.ReviewVerification) }),
        new MenuDefinition("_Workflow", "Workflow", Children: new[] { new MenuDefinition("_My Submissions", Workflow, RequiredRole: SystemRoles.Maker), new MenuDefinition("_To Review", Workflow, RequiredRole: SystemRoles.Checker), new MenuDefinition("_To Approve", Workflow, RequiredRole: SystemRoles.Approver) }),
        new MenuDefinition("_Shared Documents", SharedDocuments, RolePermission.ReadApprovedDocuments),
        new MenuDefinition("_Shared Link Management", SharedLinkManagement, RolePermission.FinalApproval),
        new MenuDefinition("_Admin", "Admin", RolePermission.ManageGlobalConfiguration, Children: new[] { new MenuDefinition("_User Management", UserAdministration, RolePermission.ManageUsersAndRoles), new MenuDefinition("_Role Assignment", UserAdministration, RolePermission.ManageUsersAndRoles), new MenuDefinition("_Workflow Configuration", WorkflowConfiguration, RolePermission.ManageGlobalConfiguration), new MenuDefinition("_Report Template Configuration", ReportTemplateConfiguration, RolePermission.ManageGlobalConfiguration) }),
        new MenuDefinition("_Help", Help)
    };

    public static string Title(string target) => target switch { UploadVerify => "Upload & Verify", StepUpload => "Upload Model", DxfUpload => "Upload Drawing", Verification => "Verification", Workflow => "Workflow", NotificationCenter => "Notifications", SharedDocuments => "Shared Documents", SharedLinkManagement => "Shared Link Management", UserAdministration => "Administration", WorkflowConfiguration => "Workflow Configuration", ReportTemplateConfiguration => "Report Template Configuration", AuditLog => "Audit Log", Report => "Report", Help => "Help", _ => "Dashboard" };
}
