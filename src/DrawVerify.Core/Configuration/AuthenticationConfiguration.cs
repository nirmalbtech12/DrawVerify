namespace DrawVerify.Core.Configuration;

public sealed class AuthenticationConfiguration
{
    public int PasswordMinimumLength { get; init; } = 12;
    public bool RequireUppercase { get; init; } = true;
    public bool RequireLowercase { get; init; } = true;
    public bool RequireDigit { get; init; } = true;
    public bool RequireNonAlphanumeric { get; init; } = true;
    public int LockoutThreshold { get; init; }
    public TimeSpan LockoutDuration { get; init; } = TimeSpan.FromMinutes(15);
    public IReadOnlyList<string> DefaultRoles { get; init; } = Array.Empty<string>();
    public bool LockoutEnabled => LockoutThreshold > 0;

    public void Validate()
    {
        if (PasswordMinimumLength < 8) throw new InvalidOperationException("Authentication password minimum length must be at least 8.");
        if (LockoutThreshold < 0) throw new InvalidOperationException("Authentication lockout threshold cannot be negative.");
        if (LockoutEnabled && LockoutDuration <= TimeSpan.Zero) throw new InvalidOperationException("Authentication lockout duration must be positive when lockout is enabled.");
        if (DefaultRoles.Any(string.IsNullOrWhiteSpace)) throw new InvalidOperationException("Authentication default roles cannot contain blank values.");
    }

    public string? ValidatePassword(string password)
    {
        if (password.Length < PasswordMinimumLength) return $"Password must be at least {PasswordMinimumLength} characters.";
        if (RequireUppercase && !password.Any(char.IsUpper)) return "Password must contain an uppercase letter.";
        if (RequireLowercase && !password.Any(char.IsLower)) return "Password must contain a lowercase letter.";
        if (RequireDigit && !password.Any(char.IsDigit)) return "Password must contain a digit.";
        if (RequireNonAlphanumeric && !password.Any(c => !char.IsLetterOrDigit(c))) return "Password must contain a non-alphanumeric character.";
        return null;
    }
}
