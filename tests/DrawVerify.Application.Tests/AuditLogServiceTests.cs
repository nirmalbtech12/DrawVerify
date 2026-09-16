using DrawVerify.Application.Services;
using DrawVerify.Core.Authorization;
using DrawVerify.Core.Enums;
using DrawVerify.Core.Interfaces;
using DrawVerify.Core.Models;
using DrawVerify.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using System.Text.Json;
using Xunit;

namespace DrawVerify.Application.Tests;

public sealed class AuditLogServiceTests
{
    [Fact(DisplayName = "TD-06 export includes verification values and explicit Unavailable fallback")]
    public async Task Verification_export_includes_values_and_legacy_fallback()
    {
        var path = Path.Combine(Path.GetTempPath(), $"drawverify-export-{Guid.NewGuid():N}.db");
        try
        {
            var documentId = Guid.NewGuid();
            var repository = new SqliteAuditRepository(path);
            await repository.WriteAsync(new AuditEntry
            {
                DocumentId = documentId, UserId = "checker,001", ActorRole = "Checker", DocumentRevision = "2", DocumentVersion = "step-2",
                Action = DocumentAuditAction.IntegrityCheckFailed, TimestampUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                DetailsJson = JsonSerializer.Serialize(new { storedFingerprint = "abc", recomputedFingerprint = "def", result = "Mismatch" })
            });
            var session = new SessionAuthenticationService();
            session.SignIn(new AuthenticatedUser("admin", new[] { SystemRoles.Admin }));
            var csv = await new AuditLogService(repository, session).ExportDocumentCsvAsync(documentId);
            Assert.Contains("RecomputedFingerprint", csv);
            Assert.Contains("VerificationResult", csv);
            Assert.Contains("Mismatch", csv);
            Assert.Contains("abc", csv);
            Assert.Contains("def", csv);
            Assert.Contains("\"checker,001\"", csv);

            var legacyId = Guid.NewGuid();
            await repository.WriteAsync(new AuditEntry { DocumentId = legacyId, UserId = "legacy", Action = DocumentAuditAction.DocumentCreated, TimestampUtc = DateTime.UtcNow, DetailsJson = "{}" });
            var legacy = await new AuditLogService(repository, session).ExportDocumentCsvAsync(legacyId);
            Assert.Contains(",Unavailable,", legacy);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            foreach (var file in new[] { path, path + "-wal", path + "-shm" })
                if (File.Exists(file)) File.Delete(file);
        }
    }
    [Fact(DisplayName = "TD-APP-002 review audit evidence preserves Cannot Validate status")]
    public void Review_audit_evidence_does_not_change_system_status()
    {
        var entry = new AuditEntry
        {
            DocumentId = Guid.NewGuid(), UserId = "checker", Action = DocumentAuditAction.GdtReviewChanged,
            TimestampUtc = DateTime.UtcNow,
            DetailsJson = "{\"annotationIdentifier\":\"gdt-gap\",\"actionType\":\"CommentAdded\",\"before\":{\"status\":\"CannotValidate\"},\"after\":{\"status\":\"CannotValidate\",\"comment\":\"Review\"}}"
        };

        using var details = System.Text.Json.JsonDocument.Parse(entry.DetailsJson);
        Assert.Equal("CannotValidate", details.RootElement.GetProperty("before").GetProperty("status").GetString());
        Assert.Equal("CannotValidate", details.RootElement.GetProperty("after").GetProperty("status").GetString());
        Assert.Equal("CommentAdded", details.RootElement.GetProperty("actionType").GetString());
    }

    [Fact(DisplayName = "TD-APP-003 carries non-validation action, actor, timestamp, and annotation context")]
    public void Cannot_validate_audit_payload_is_complete()
    {
        var documentId = Guid.NewGuid();
        var entry = new AuditEntry
        {
            DocumentId = documentId, UserId = "checker", Action = DocumentAuditAction.GdtReviewChanged,
            TimestampUtc = DateTime.UtcNow, DetailsJson = "{\"verificationSessionId\":\"" + documentId + "\",\"annotationId\":\"gdt-gap\",\"actionType\":\"CommentAdded\",\"reason\":\"Unsupported GD&T control type\",\"before\":{},\"after\":{\"comment\":\"Review\"}}"
        };

        Assert.Equal(DocumentAuditAction.GdtReviewChanged, entry.Action);
        Assert.Equal(DateTimeKind.Utc, entry.TimestampUtc.Kind);
        Assert.Contains("annotationId", entry.DetailsJson);
        Assert.Contains("CommentAdded", entry.DetailsJson);
        Assert.Contains("checker", entry.UserId);
    }
    [Fact(DisplayName = "TD-APP-003 audit payload carries GD&T identifier, actor, action, and UTC timestamp")]
    public void Gdt_review_audit_contract_contains_required_fields()
    {
        var documentId = Guid.NewGuid();
        var entry = new AuditEntry { DocumentId = documentId, UserId = "checker", Action = DocumentAuditAction.UnsupportedGdtReviewChanged, TimestampUtc = DateTime.UtcNow, DetailsJson = "{\"identifier\":\"gdt-7\",\"before\":{\"status\":\"NotVerified\"},\"after\":{\"comment\":\"review\"}}" };

        Assert.Equal(documentId, entry.DocumentId);
        Assert.Equal("checker", entry.UserId);
        Assert.Equal(DocumentAuditAction.UnsupportedGdtReviewChanged, entry.Action);
        Assert.Equal(DateTimeKind.Utc, entry.TimestampUtc.Kind);
        Assert.Contains("gdt-7", entry.DetailsJson);
    }

    [Fact(DisplayName = "TD-05 queries and exports filtered audit data for Admin only")]
    public async Task Admin_query_is_ordered_and_csv_escapes_values()
    {
        var repository = new AuditRepositoryFake();
        repository.Page = new AuditLogPage(new[] { new AuthenticationAuditEntry("ADMIN", "USER,1", DateTimeOffset.UnixEpoch, "UserCreated", "Success", RolesAfter: "[\"Viewer\"]") }, 1, 100, 1);
        var session = new SessionAuthenticationService(); session.SignIn(new AuthenticatedUser("ADMIN", new[] { "Admin" }, 0));
        var service = new AuditLogService(repository, session);
        var page = await service.QueryAsync(new AuditLogQuery(Action: "UserCreated", Username: "USER,1"));
        Assert.Single(page.Entries);
        Assert.Equal(new[] { "UserCreated", "USER,1" }, repository.LastQuery is { } query ? new[] { query.Action!, query.Username! } : Array.Empty<string>());
        var csv = await service.ExportCsvAsync(new AuditLogQuery(Action: "UserCreated"));
        Assert.Contains("\"USER,1\"", csv);
        Assert.Contains("RolesBefore", csv);
    }

    [Fact(DisplayName = "TD-05 rejects non-Admin query and export before repository access")]
    public async Task Non_admin_cannot_query_or_export()
    {
        var repository = new AuditRepositoryFake();
        var session = new SessionAuthenticationService(); session.SignIn(new AuthenticatedUser("MAKER", new[] { "Maker" }, 0));
        var service = new AuditLogService(repository, session);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.QueryAsync(new AuditLogQuery()));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.ExportCsvAsync(new AuditLogQuery()));
        Assert.Null(repository.LastQuery);
    }

    private sealed class AuditRepositoryFake : IAuthenticationAuditRepository
    {
        public AuditLogPage? Page { get; set; }
        public AuditLogQuery? LastQuery { get; private set; }
        public Task WriteAsync(AuthenticationAuditEntry entry, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<AuditLogPage> QueryAsync(AuditLogQuery query, CancellationToken cancellationToken = default) { LastQuery = query; return Task.FromResult(Page ?? new AuditLogPage(Array.Empty<AuthenticationAuditEntry>(), query.Page, query.PageSize, 0)); }
    }
}
