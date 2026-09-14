using System.Collections.Concurrent;
using System.Text.Json;
using DrawVerify.Core.Interfaces;
using DrawVerify.Core.Models;

namespace DrawVerify.Infrastructure.FileStorage;

public sealed class ApprovalSnapshotFileStore : IApprovalSnapshotStore
{
    private readonly string _root;
    private readonly ConcurrentDictionary<Guid, ApprovalSnapshot> _index = new();
    public ApprovalSnapshotFileStore(string root) => _root = Path.GetFullPath(root ?? throw new ArgumentNullException(nameof(root)));

    public async Task<ApprovalSnapshot> SaveAsync(ApprovalSnapshot snapshot, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (snapshot.PackageId == Guid.Empty || snapshot.Revision < 1) throw new ArgumentException("A package and revision are required.", nameof(snapshot));
        Directory.CreateDirectory(_root);
        var path = Path.Combine(_root, $"approval-{snapshot.SnapshotId:N}.json");
        if (File.Exists(path)) throw new IOException("Approval snapshots are immutable and cannot be overwritten.");
        await using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read, 81920, FileOptions.Asynchronous);
        await JsonSerializer.SerializeAsync(stream, snapshot, cancellationToken: cancellationToken).ConfigureAwait(false);
        _index.TryAdd(snapshot.SnapshotId, snapshot);
        return snapshot;
    }

    public async Task<ApprovalSnapshot?> GetAsync(Guid snapshotId, CancellationToken cancellationToken = default)
    {
        if (_index.TryGetValue(snapshotId, out var snapshot)) return snapshot;
        var path = Path.Combine(_root, $"approval-{snapshotId:N}.json");
        if (!File.Exists(path)) return null;
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.Asynchronous);
        var loaded = await JsonSerializer.DeserializeAsync<ApprovalSnapshot>(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
        if (loaded is not null) _index.TryAdd(snapshotId, loaded);
        return loaded;
    }
}
