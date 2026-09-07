namespace DrawVerify.Core.Authorization;

public enum RolePermission
{
    ManageUsersAndRoles, ViewAuditLog, ManageGlobalConfiguration, Upload, TriggerExtraction, StartVerification,
    ReviewVerification, ResolveUnmatched, ApproveVerification, FinalApproval, ControlDocumentAccess,
    GenerateFinalReports, ReadApprovedDocuments, ReadApprovedReports
}
