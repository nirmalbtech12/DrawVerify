namespace DrawVerify.Core.Models;

public enum AuthenticationResultKind { Success, ValidationFailure, DuplicateUsername, InvalidCredentials, AccountLocked, OperationalFailure }
public sealed record AuthenticationResult(AuthenticationResultKind Kind, string Message, AuthenticatedUser? User = null)
{
    public bool Succeeded => Kind == AuthenticationResultKind.Success;
}
