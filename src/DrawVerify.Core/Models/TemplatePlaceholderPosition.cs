namespace DrawVerify.Core.Models;

public sealed record TemplatePlaceholderPosition
{
    public double X { get; init; }
    public double Y { get; init; }
    public double Width { get; init; } = 0.2;
    public double Height { get; init; } = 0.08;

    public void Validate()
    {
        if (double.IsNaN(X) || double.IsNaN(Y) || double.IsNaN(Width) || double.IsNaN(Height) ||
            X < 0 || Y < 0 || Width <= 0 || Height <= 0 || X + Width > 1 || Y + Height > 1)
            throw new InvalidOperationException("Placeholder position must be within page bounds and have a positive size.");
    }
}
