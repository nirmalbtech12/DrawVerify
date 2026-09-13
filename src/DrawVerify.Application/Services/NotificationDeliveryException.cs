namespace DrawVerify.Application.Services;

public sealed class NotificationDeliveryException : Exception
{
    public NotificationDeliveryException(Guid packageId, int revision, Exception innerException)
        : base($"Maker notification delivery failed for package '{packageId}' revision {revision}.", innerException)
    {
        PackageId = packageId;
        Revision = revision;
    }

    public Guid PackageId { get; }
    public int Revision { get; }
}
