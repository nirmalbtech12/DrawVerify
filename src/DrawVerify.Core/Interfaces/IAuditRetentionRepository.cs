namespace DrawVerify.Core.Interfaces;
public interface IAuditRetentionRepository { Task<int> ApplyAsync(int retentionDays, CancellationToken cancellationToken = default); }
