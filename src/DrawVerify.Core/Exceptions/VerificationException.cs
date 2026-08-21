namespace DrawVerify.Core.Exceptions;

/// <summary>
/// Thrown when a document cannot be verified against its geometry or tolerance model.
/// </summary>
public sealed class VerificationException : Exception
{
    public VerificationException(string message) : base(message)
    {
    }

    public VerificationException(string message, Exception innerException) : base(message, innerException)
    {
    }
}