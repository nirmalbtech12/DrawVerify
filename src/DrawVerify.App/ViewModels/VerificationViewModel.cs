using DrawVerify.Core.Authorization;
using DrawVerify.Core.Interfaces;

namespace DrawVerify.App.ViewModels;

public sealed class VerificationViewModel
{
    private readonly ISessionAuthenticationService _session;
    private readonly RoleAuthorization _authorization;
    public VerificationViewModel(ISessionAuthenticationService session, RoleAuthorization authorization) { _session = session; _authorization = authorization; }
    public bool CanReview => _authorization.IsAllowed(_session.CurrentUser, RolePermission.ReviewVerification);
    public bool CanEnterReview => CanReview;
    public string DenialMessage => "The Checker or Approver role is required to review verification results.";
    public bool TryEnterReview(out string message) { if (CanReview) { message = string.Empty; return true; } message = DenialMessage; return false; }
}
