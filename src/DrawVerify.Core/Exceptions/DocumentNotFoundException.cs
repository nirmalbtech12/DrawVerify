namespace DrawVerify.Core.Exceptions;

/// <summary>
/// Thrown when a requested document or document version does not exist.
/// </summary>
public sealed class DocumentNotFoundException : Exception
{
    public DocumentNotFoundException(string message) : base(message)
    {
    }

    public DocumentNotFoundException(string message, Exception innerException) : base(message, innerException)
    {
    }
}