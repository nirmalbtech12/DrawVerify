namespace DrawVerify.Core.Enums;

public enum WorkflowStatus
{
    Draft = 0,
    InPreparation = 1,
    SubmittedForReview = 2,
    UnderReview = 3,
    Approved = 4,
    Superseded = 5,
    Rejected = 6,
    ReturnedToMaker = 7,
    ChangesRequested = 8,
    ApprovedByChecker = 9,
    PendingFinalApproval = 10,
    FinallyApproved = 11,
    Released = 12,
    ReturnedForRework = 13
}
