namespace DrawVerify.Core.Authorization;

public enum RolePermission
{
    ManageUsersAndRoles, ViewAuditLog, ManageGlobalConfiguration, Upload, GenerateDxfDrawing, TriggerExtraction, TriggerDxfDimensionExtraction, ViewDxfDimensionResults, StartVerification,
    ReviewVerification, ResolveUnmatched, ApproveVerification, FinalApproval, ControlDocumentAccess,
    GenerateFinalReports, ReadApprovedDocuments, ReadApprovedReports,
    MatchDimensionsToGeometry, ViewDimensionMatchResults, ManageToleranceComparison,
    ViewMissingDimensions, CommentOnMissingDimensions, ViewExtraDimensions, CommentOnExtraDimensions, FlagExtraDimensions,
    ViewGdtCannotValidate, ModifyGdtCannotValidate, SubmitForReview
}
