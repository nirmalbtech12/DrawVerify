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

    [Fact(DisplayName = "TSD-04 SQLite reload preserves application-composed stamp metadata")]
    public async Task Sqlite_reload_preserves_dynamic_stamp_metadata_and_integrity()
    {
        var path = Path.Combine(Path.GetTempPath(), $"drawverify-stamp-metadata-{Guid.NewGuid():N}.db");
        var packageId = Guid.NewGuid();
        var placeholderId = Guid.NewGuid();
        var timestamp = new DateTimeOffset(2026, 9, 22, 12, 0, 0, TimeSpan.Zero);
        var mapping = new TemplateMappingSnapshot
        {
            TemplateId = Guid.NewGuid(), TemplateVersion = 2,
            Assignments = new[] { new SignaturePlaceholderAssignment { PlaceholderId = placeholderId, Role = "Checker", AssignedSignerUserId = "checker-1" } },
            CustomFieldMappings = new Dictionary<string, string> { ["Department"] = "Quality" }
        };
        try
        {
            var repository = new SqliteWorkflowRepository(path);
            var maker = SignatureData.Create(SignatureMethod.Typed, "maker", "maker-1", "Maker One", packageId, 1, timestamp);
            var submission = new ReviewSubmission { PackageId = packageId, Revision = 1, MakerUserId = "maker-1", MakerDisplayName = "Maker One", MakerSignature = maker, TemplateMapping = mapping, PlaceholderAssignments = mapping.Assignments, Fingerprint = new DocumentFingerprint { Value = new string('a', 64), Algorithm = "SHA-256", CanonicalizationVersion = 1 } };
            await repository.SubmitAtomicallyAsync(new WorkflowState { PackageId = packageId, Status = WorkflowStatus.Draft, Revision = 1 }, submission, new AuditEntry { DocumentId = packageId, UserId = "maker-1", Action = DocumentAuditAction.SubmissionSucceeded, TimestampUtc = timestamp.UtcDateTime });
            var package = (await repository.GetReviewPackageAsync(packageId, 1))!;
            var signature = SignatureData.CreateChecker(SignatureMethod.Typed, "checker", "checker-1", "Checker One", packageId, 1, timestamp, placeholderId: placeholderId, customFields: new Dictionary<string, string> { ["Department"] = "Quality", ["DocumentRevision"] = "1" });
            await repository.SaveCheckerSignatureAsync(package, signature, submission.Fingerprint!, new AuditEntry { DocumentId = packageId, UserId = "checker-1", Action = DocumentAuditAction.CheckerSignatureApplied, TimestampUtc = timestamp.UtcDateTime });

            var reloaded = (await new SqliteWorkflowRepository(path).GetReviewPackageAsync(packageId, 1))!.Submission;
            Assert.Equal(signature, reloaded.CheckerSignature);
            Assert.Equal("Quality", reloaded.CheckerSignature!.CustomFields["Department"]);
            Assert.Equal(placeholderId, reloaded.CheckerSignature.PlaceholderId);
            Assert.True(reloaded.CheckerSignature.IsTamperEvident);
            Assert.True(reloaded.PlaceholderAssignments.Single(x => x.PlaceholderId == placeholderId).IsSigned);
        }
        finally { Delete(path); }
    }

    [Fact(DisplayName = "TSD-02 per-placeholder signer state round-trips in JSON")]
    public void Multiple_signer_assignment_state_round_trips()
    {
        var timestamp = new DateTimeOffset(2026, 9, 22, 12, 0, 0, TimeSpan.Zero);
        var assignments = new[]
        {
            new SignaturePlaceholderAssignment { PlaceholderId = Guid.NewGuid(), Role = "Approver", AssignedSignerUserId = "approver-1", IsSigned = true, SignedByUserId = "approver-1", SignedUtc = timestamp },
            new SignaturePlaceholderAssignment { PlaceholderId = Guid.NewGuid(), Role = "Approver", AssignedSignerUserId = "approver-2" }
        };

        var restored = JsonSerializer.Deserialize<SignaturePlaceholderAssignment[]>(JsonSerializer.Serialize(assignments));
        Assert.NotNull(restored);
        Assert.Equal(assignments, restored);
        Assert.False(restored![1].IsSigned);
        Assert.Equal("approver-1", restored[0].SignedByUserId);
        Assert.Equal(timestamp, restored[0].SignedUtc);
    }

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

    [Fact(DisplayName = "TD-003 Checker audit round-trip retains version, identity, method, and digest reference")]
    public async Task Checker_signature_audit_round_trips_without_raw_payload_logging()
    {
        var path = Path.Combine(Path.GetTempPath(), $"drawverify-checker-signature-{Guid.NewGuid():N}.db");
        var document = Guid.NewGuid();
        try
        {
            var repository = new SqliteAuditRepository(path);
            var signature = SignatureData.CreateChecker(SignatureMethod.Typed, "deterministic-signature", "checker-001", "Checker One", document, 2, new DateTimeOffset(2026, 9, 16, 10, 0, 0, TimeSpan.Zero));
            var entry = new AuditEntry { DocumentId = document, UserId = signature.CheckerUserId, ActorRole = signature.Role, DocumentRevision = "2", DocumentVersion = document.ToString("D"), Action = DocumentAuditAction.CheckerSignatureApplied, TimestampUtc = signature.TimestampUtc.UtcDateTime, DetailsJson = JsonSerializer.Serialize(new { documentId = document, revision = 2, checkerUserId = signature.CheckerUserId, checkerDisplayName = signature.CheckerDisplayName, checkerRole = signature.Role, checkerSignatureMethod = signature.Method.ToString(), checkerSignatureTimestampUtc = signature.TimestampUtc, checkerSignatureHash = signature.IntegrityHash }) };
            await repository.AppendAsync(entry);
            var record = Assert.Single(await repository.QueryAsync(new AuditLogQuery(DocumentId: document)));
            Assert.Equal(DocumentAuditAction.CheckerSignatureApplied, record.Action);
            Assert.Equal("checker-001", record.ActorId);
            Assert.Equal("Checker", record.ActorRole);
            Assert.Contains(signature.IntegrityHash, record.DetailsJson);
            Assert.DoesNotContain(signature.Payload, record.DetailsJson);
        }
        finally { Delete(path); }
    }

    [Fact(DisplayName = "TD-003 persistence source declares immutable Checker columns and validation")]
    public void Checker_persistence_contract_declares_round_trip_guards()
    {
        var source = File.ReadAllText(FindRepositoryFile(Path.Combine("src", "DrawVerify.Infrastructure", "Persistence", "SqliteWorkflowRepository.cs")));
        Assert.Contains("CheckerSignatureJson", source);
        Assert.Contains("CheckerSignatureFinalized", source);
        Assert.Contains("Checker signature does not match the current document version", source);
        Assert.Contains("CheckerSignatureApplied", source);
        Assert.Contains("checkerSignature.Validate", source);
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
