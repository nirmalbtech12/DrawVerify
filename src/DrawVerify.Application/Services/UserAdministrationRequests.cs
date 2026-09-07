namespace DrawVerify.Application.Services;

/// <summary>Inputs for administrator-controlled user lifecycle operations.</summary>
public sealed record UserCreationRequest(string Username, string DisplayName, string InitialPassword);

public sealed record UserProfileUpdateRequest(
    string Username,
    string? DisplayName = null,
    string? Email = null,
    bool PasswordReset = false);

public sealed record UserDeactivationRequest(string Username, string? Reason = null);
