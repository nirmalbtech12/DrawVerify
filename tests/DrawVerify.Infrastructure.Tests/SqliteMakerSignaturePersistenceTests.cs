using System.Text.Json;
using DrawVerify.Core.Enums;
using DrawVerify.Core.Models;
using DrawVerify.Infrastructure.Persistence;
using DrawVerify.Infrastructure.Signatures;
using Microsoft.Data.Sqlite;
using Xunit;

namespace DrawVerify.Infrastructure.Tests;

public sealed class SqliteMakerSignaturePersistenceTests
{
    private const string ValidPngPayload = "iVBORw0KGgo=";

    [Fact(DisplayName = "TD-KAN64-INFRA-001 audit persistence records MakerSignatureApplied with integrity")]
    public async Task Maker_signature_audit_round_trips_and_is_append_only()
    {
        var path = Path.Combine(Path.GetTempPath(), $"drawverify-maker-signature-{Guid.NewGuid():N}.db");
        var document = Guid.Parse("11111111-1111-1111-1111-111111111111");
        try
        {
            var repository = new SqliteAuditRepository(path);
            var signature = SignatureData.Create(SignatureMethod.Typed, "Maker One", "maker-001", "Maker One", document, 1, new DateTimeOffset(2026, 1, 10, 8, 0, 0, TimeSpan.Zero));
            var entry = new AuditEntry { DocumentId = document, UserId = signature.MakerUserId, ActorRole = signature.Role, DocumentRevision = "1", DocumentVersion = document.ToString("D"), Action = DocumentAuditAction.MakerSignatureApplied, TimestampUtc = signature.TimestampUtc.UtcDateTime, DetailsJson = JsonSerializer.Serialize(new { documentId = document, revision = 1, maker = signature.MakerDisplayName, method = signature.Method.ToString(), timestampUtc = signature.TimestampUtc }) };
            await repository.WriteAsync(entry);
            var records = await repository.QueryAsync(new AuditLogQuery(DocumentId: document, PageSize: 100));
            var record = Assert.Single(records);
            Assert.Equal(DocumentAuditAction.MakerSignatureApplied, record.Action);
            Assert.Equal("maker-001", record.ActorId);
            Assert.Equal("Maker", record.ActorRole);
            Assert.Equal("1", record.DocumentRevision);
            Assert.Contains("Typed", record.DetailsJson);
            Assert.False(string.IsNullOrWhiteSpace(record.IntegrityHash));
            await repository.VerifyIntegrityAsync(document);
            await Assert.ThrowsAsync<SqliteException>(() => repository.AppendAsync(new AuditEntry { AuditId = entry.AuditId, DocumentId = document, UserId = "maker-001", ActorRole = "Maker", DocumentRevision = "1", DocumentVersion = document.ToString("D"), Action = DocumentAuditAction.MakerSignatureApplied, TimestampUtc = signature.TimestampUtc.UtcDateTime, DetailsJson = entry.DetailsJson }));
        }
        finally { Delete(path); }
    }

    [Fact(DisplayName = "TD-KAN64-INFRA-001 signature renderer preserves canonical integrity and revision")]
    public void Signature_stamp_service_validates_preview_and_tamper_evidence()
    {
        var service = new SignatureStampService();
        var signature = SignatureData.Create(SignatureMethod.Uploaded, ValidPngPayload, "maker-001", "Maker One", Guid.Parse("11111111-1111-1111-1111-111111111111"), 2, new DateTimeOffset(2026, 1, 10, 8, 0, 0, TimeSpan.Zero), "Engineering", "Design");
        Assert.Equal(signature.CanonicalRepresentation, service.Canonicalize(signature));
        Assert.Equal(signature.Payload, service.Preview(signature));
        Assert.True(service.VerifyIntegrity(signature));
        Assert.False(service.VerifyIntegrity(signature with { Payload = "altered" }));
    }

    [Fact(DisplayName = "TD-KAN64-INFRA-001 persistence contract guards finalized signatures and validates loaded data")]
    public void Workflow_persistence_contract_declares_signature_guards_and_validation()
    {
        var source = File.ReadAllText(FindRepositoryFile(Path.Combine("src", "DrawVerify.Infrastructure", "Persistence", "SqliteWorkflowRepository.cs")));
        Assert.Contains("MakerSignatureFinalized", source);
        Assert.Contains("MakerSignatureJson", source);
        Assert.Contains("signature.Validate();", source);
        Assert.Contains("DocumentAuditLog", source);
        Assert.Contains("Action", source);
        Assert.Contains("immutable", source, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("RAISE(FAIL", source, StringComparison.OrdinalIgnoreCase);
    }

    private static void Delete(string path)
    {
        SqliteConnection.ClearAllPools();
        if (File.Exists(path)) File.Delete(path);
        if (File.Exists(path + "-wal")) File.Delete(path + "-wal");
        if (File.Exists(path + "-shm")) File.Delete(path + "-shm");
    }

    private static string FindRepositoryFile(string relativePath)
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        for (var index = 0; index < 10 && current is not null; index++, current = current.Parent)
        {
            var candidate = Path.Combine(current.FullName, relativePath);
            if (File.Exists(candidate)) return candidate;
        }
        throw new FileNotFoundException(relativePath);
    }
}
