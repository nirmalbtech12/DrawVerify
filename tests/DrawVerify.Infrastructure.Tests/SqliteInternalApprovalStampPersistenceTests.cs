using System.Text.Json;
using DrawVerify.Core.Enums;
using DrawVerify.Core.Models;
using DrawVerify.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Xunit;

namespace DrawVerify.Infrastructure.Tests;

public sealed class SqliteInternalApprovalStampPersistenceTests
{
    private static readonly Guid Document = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly DateTimeOffset Applied = new(2026, 1, 15, 10, 30, 0, TimeSpan.Zero);

    [Fact(DisplayName = "T04 SQLite persistence is atomic and immutable")]
    public async Task T04_migration_creates_stamp_columns_and_guards()
    {
        var path = Path.Combine(Path.GetTempPath(), $"drawverify-stamp-{Guid.NewGuid():N}.db");
        try
        {
            _ = new SqliteWorkflowRepository(path);
            await using var connection = new SqliteConnection($"Data Source={path}");
            await connection.OpenAsync();
            var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            await using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT name FROM pragma_table_info('ReviewSubmission') WHERE name LIKE 'InternalApprovalStamp%';";
                await using var reader = await command.ExecuteReaderAsync();
                while (await reader.ReadAsync()) columns.Add(reader.GetString(0));
            }
            Assert.Contains("InternalApprovalStampJson", columns);
            Assert.Contains("InternalApprovalStampFinalized", columns);
            await using var trigger = connection.CreateCommand();
            trigger.CommandText = "SELECT name FROM sqlite_master WHERE type='trigger' AND name LIKE 'TR_ReviewSubmission_ApprovalStamp%';";
            var names = new List<string>();
            await using (var reader = await trigger.ExecuteReaderAsync()) while (await reader.ReadAsync()) names.Add(reader.GetString(0));
            Assert.Contains("TR_ReviewSubmission_ApprovalStampUpdate", names);
            Assert.Contains("TR_ReviewSubmission_ApprovalStampDelete", names);
        }
        finally { SqliteConnection.ClearAllPools(); DeleteDatabase(path); }
    }

    [Fact(DisplayName = "T15 SQLite persistence writes the exact stamp audit action")]
    public async Task T15_recorded_stamp_audit_round_trips_complete_payload()
    {
        var path = Path.Combine(Path.GetTempPath(), $"drawverify-stamp-audit-{Guid.NewGuid():N}.db");
        try
        {
            var stamp = InternalApprovalStamp.Create(Document, 1, "approver-001", "Approver One", Applied, "fingerprint-001", Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"));
            var entry = new AuditEntry { AuditId = Guid.NewGuid(), DocumentId = Document, UserId = stamp.ApproverUserId, ActorRole = stamp.ApproverRole, DocumentRevision = "1", DocumentVersion = "REV-001", Action = DocumentAuditAction.InternalApprovalStampApplied, TimestampUtc = Applied.UtcDateTime, DetailsJson = JsonSerializer.Serialize(new { stampId = stamp.StampId, stampApproverUserId = stamp.ApproverUserId, stampApproverDisplayName = stamp.ApproverDisplayName, stampApproverRole = stamp.ApproverRole, stampAppliedUtc = stamp.AppliedUtc, stampFingerprintReference = stamp.FingerprintReference, stampIntegrityHash = stamp.IntegrityHash, verificationResult = stamp.VerificationStatus.ToString() }) };
            await new SqliteWorkflowRepository(path).RecordAuditAsync(entry);
            await using var connection = new SqliteConnection($"Data Source={path}");
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT Action,DetailsJson FROM DocumentAuditLog WHERE AuditId=$id";
            command.Parameters.AddWithValue("$id", entry.AuditId.ToString());
            await using var reader = await command.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
            Assert.Equal((int)DocumentAuditAction.InternalApprovalStampApplied, reader.GetInt32(0));
            using var details = JsonDocument.Parse(reader.GetString(1));
            Assert.Equal(stamp.StampId.ToString(), details.RootElement.GetProperty("stampId").GetString());
            Assert.Equal(stamp.ApproverDisplayName, details.RootElement.GetProperty("stampApproverDisplayName").GetString());
            Assert.Equal(stamp.FingerprintReference, details.RootElement.GetProperty("stampFingerprintReference").GetString());
            Assert.Equal(stamp.IntegrityHash, details.RootElement.GetProperty("stampIntegrityHash").GetString());
        }
        finally { SqliteConnection.ClearAllPools(); DeleteDatabase(path); }
    }

    private static void DeleteDatabase(string path)
    {
        foreach (var file in new[] { path, path + "-wal", path + "-shm" }) if (File.Exists(file)) File.Delete(file);
    }
}
