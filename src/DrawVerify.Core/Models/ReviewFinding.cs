namespace DrawVerify.Core.Models;

public sealed record ReviewFinding(string Identifier, string Category, string? Description = null);
