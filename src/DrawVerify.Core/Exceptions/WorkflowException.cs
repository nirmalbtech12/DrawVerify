namespace DrawVerify.Core.Exceptions;

/// <summary>
/// Thrown when a workflow state transition is invalid or unauthorized.
/// </summary>
public sealed class WorkflowException : Exception
{
    public WorkflowException(string message) : base(message)
    {
    }

    public WorkflowException(string message, Exception innerException) : base(message, innerException)
    {
    }
}