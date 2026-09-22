namespace DrawVerify.Core.Models;

public sealed record TemplatePlaceholder
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public TemplatePlaceholderType Type { get; init; }
    public string Label { get; init; } = string.Empty;
    public bool IsRequired { get; init; }
    public string? Role { get; init; }
    public string LogicalBlock { get; init; } = "default";
    public bool AllowDuplicateRole { get; init; }
    public TemplatePlaceholderPosition Position { get; init; } = new();

    public bool IsSignature => Type == TemplatePlaceholderType.Signature;

    public void Validate()
    {
        if (Id == Guid.Empty) throw new InvalidOperationException("Placeholder identifier is required.");
        if (string.IsNullOrWhiteSpace(Label)) throw new InvalidOperationException($"Placeholder '{Id}' label is required.");
        if (IsSignature && string.IsNullOrWhiteSpace(Role)) throw new InvalidOperationException($"Signature placeholder '{Label}' role is required.");
        if (Type == TemplatePlaceholderType.CustomText && string.IsNullOrWhiteSpace(Label)) throw new InvalidOperationException("Custom text placeholder name is required.");
        Position.Validate();
    }
}
