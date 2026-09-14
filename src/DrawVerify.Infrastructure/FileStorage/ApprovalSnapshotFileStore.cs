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

    public async Task<ApprovalSnapshot> SaveAsync(ApprovalSnapshot snapshot, Stream stepArtifact, Stream dxfArtifact, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stepArtifact); ArgumentNullException.ThrowIfNull(dxfArtifact);
        Directory.CreateDirectory(_root);
        var stepPath = Path.Combine(_root, $"approval-{snapshot.SnapshotId:N}.step");
        var dxfPath = Path.Combine(_root, $"approval-{snapshot.SnapshotId:N}.dxf");
        await CopyAsync(stepArtifact, stepPath, cancellationToken).ConfigureAwait(false);
        try { await CopyAsync(dxfArtifact, dxfPath, cancellationToken).ConfigureAwait(false); }
        catch { TryDelete(stepPath); throw; }
        var captured = snapshot with { StepArtifactPath = Path.GetFileName(stepPath), DxfArtifactPath = Path.GetFileName(dxfPath), StepSha256 = await HashAsync(stepPath, cancellationToken).ConfigureAwait(false), DxfSha256 = await HashAsync(dxfPath, cancellationToken).ConfigureAwait(false) };
        try { return await SaveAsync(captured, cancellationToken).ConfigureAwait(false); }
        catch { TryDelete(stepPath); TryDelete(dxfPath); throw; }
    }

    public async Task<ApprovalSnapshot?> GetAsync(Guid snapshotId, CancellationToken cancellationToken = default)
    {
        if (_index.TryGetValue(snapshotId, out var snapshot)) return snapshot;
        var path = Path.Combine(_root, $"approval-{snapshotId:N}.json");
        if (!File.Exists(path)) return null;
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.Asynchronous);
        var loaded = await JsonSerializer.DeserializeAsync<ApprovalSnapshot>(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
        if (loaded is not null) _index.TryAdd(snapshotId, loaded);
        if (loaded is not null && !string.IsNullOrWhiteSpace(loaded.StepArtifactPath) && !string.Equals(await HashAsync(Path.Combine(_root, loaded.StepArtifactPath), cancellationToken).ConfigureAwait(false), loaded.StepSha256, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("The approval snapshot STEP artifact hash does not match.");
        if (loaded is not null && !string.IsNullOrWhiteSpace(loaded.DxfArtifactPath) && !string.Equals(await HashAsync(Path.Combine(_root, loaded.DxfArtifactPath), cancellationToken).ConfigureAwait(false), loaded.DxfSha256, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("The approval snapshot DXF artifact hash does not match.");
        return loaded;
    }
    public Task DeleteAsync(Guid snapshotId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested(); TryDelete(Path.Combine(_root, $"approval-{snapshotId:N}.json")); TryDelete(Path.Combine(_root, $"approval-{snapshotId:N}.step")); TryDelete(Path.Combine(_root, $"approval-{snapshotId:N}.dxf")); _index.TryRemove(snapshotId, out _); return Task.CompletedTask;
    }
    private static async Task CopyAsync(Stream source, string path, CancellationToken token) { if (source.CanSeek) source.Position = 0; await using var target = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous); await source.CopyToAsync(target, token).ConfigureAwait(false); await target.FlushAsync(token).ConfigureAwait(false); }
    private static async Task<string> HashAsync(string path, CancellationToken token) { await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.Asynchronous); return Convert.ToHexString(await System.Security.Cryptography.SHA256.HashDataAsync(stream, token).ConfigureAwait(false)); }
    private static void TryDelete(string path) { if (File.Exists(path)) File.Delete(path); }
}
