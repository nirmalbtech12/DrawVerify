using DrawVerify.Core.Enums;
using DrawVerify.Core.Models;
using DrawVerify.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Xunit;

namespace DrawVerify.Infrastructure.Tests;

public sealed class SqliteSharedDocumentRepositoryTests
{
    [Fact(DisplayName = "TSD-ACCESS-FILTER TSD-APPROVED-STATE TSD-SCOPED-SEARCH SQLite predicates isolate recipient, approval, and metadata search")]
    public async Task Shared_document_query_returns_only_current_recipient_approved_rows()
    {
        var path = Path.Combine(Path.GetTempPath(), $"shared-documents-{Guid.NewGuid():N}.db");
        try
        {
            var approved = await SeedAsync(path, WorkflowStatus.FinallyApproved, "approved-plate.dxf", "viewer-1", revoked: false);
            await SeedAsync(path, WorkflowStatus.Draft, "draft-plate.dxf", "viewer-1", revoked: false);
            await SeedAsync(path, WorkflowStatus.FinallyApproved, "other-plate.dxf", "other-viewer", revoked: false);
            var repository = new SqliteSharedDocumentRepository(path);

            var rows = await repository.ListAsync("viewer-1", new SharedDocumentQuery("plate"));

            var row = Assert.Single(rows);
            Assert.Equal(approved.DocumentId, row.DocumentId);
            Assert.Equal(WorkflowStatus.FinallyApproved, row.ApprovalStatus);
        }
        finally { SqliteConnection.ClearAllPools(); if (File.Exists(path)) File.Delete(path); }
    }

    [Fact(DisplayName = "TSD-DIRECT-DENIAL TSD-NONAPPROVED-DENIAL unshared, revoked, and non-approved rows are inaccessible")]
    public async Task Get_requires_active_share_and_approved_workflow_state()
    {
        var path = Path.Combine(Path.GetTempPath(), $"shared-denial-{Guid.NewGuid():N}.db");
        try
        {
            var revoked = await SeedAsync(path, WorkflowStatus.FinallyApproved, "revoked.dxf", "viewer-1", revoked: true);
            var draft = await SeedAsync(path, WorkflowStatus.Draft, "draft.dxf", "viewer-1", revoked: false);
            var repository = new SqliteSharedDocumentRepository(path);

            Assert.Null(await repository.GetAsync("viewer-1", revoked.DocumentId, revoked.VersionId));
            Assert.Null(await repository.GetAsync("viewer-1", draft.DocumentId, draft.VersionId));
            Assert.Null(await repository.GetAsync("other-viewer", revoked.DocumentId, revoked.VersionId));
        }
        finally { SqliteConnection.ClearAllPools(); if (File.Exists(path)) File.Delete(path); }
    }

    [Fact(DisplayName = "TSD-REVOCATION revoked shares disappear after refresh")]
    public async Task Revocation_removes_row_from_list_and_get()
    {
        var path = Path.Combine(Path.GetTempPath(), $"shared-revoke-{Guid.NewGuid():N}.db");
        try
        {
            var document = await SeedAsync(path, WorkflowStatus.Released, "released.dxf", "viewer-1", revoked: false);
            var repository = new SqliteSharedDocumentRepository(path);
            Assert.Single(await repository.ListAsync("viewer-1", new SharedDocumentQuery()));
            await repository.RevokeShareAsync(document.DocumentId, document.VersionId, "viewer-1");
            Assert.Empty(await repository.ListAsync("viewer-1", new SharedDocumentQuery()));
            Assert.Null(await repository.GetAsync("viewer-1", document.DocumentId, document.VersionId));
        }
        finally { SqliteConnection.ClearAllPools(); if (File.Exists(path)) File.Delete(path); }
    }

    private static async Task<SharedDocument> SeedAsync(string path, WorkflowStatus status, string name, string recipient, bool revoked)
    {
        var document = new SharedDocument { DocumentId = Guid.NewGuid(), VersionId = Guid.NewGuid(), DocumentName = name, Revision = 2, ApprovalStatus = status, DocumentType = DrawVerify.Core.Enums.DocumentType.DxfDrawing, StoredRelativePath = name, SharedUtc = new DateTime(2026, 9, 18, 12, 0, 0, DateTimeKind.Utc) };
        await using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path }.ToString());
        await connection.OpenAsync();
        await using (var schema = connection.CreateCommand())
        {
            schema.CommandText = "CREATE TABLE IF NOT EXISTS Document (DocumentId TEXT PRIMARY KEY, OriginalFileName TEXT NOT NULL, DocumentType INTEGER NOT NULL, Status INTEGER NOT NULL); CREATE TABLE IF NOT EXISTS DocumentVersion (VersionId TEXT PRIMARY KEY, DocumentId TEXT NOT NULL, VersionNumber INTEGER NOT NULL, StoredRelativePath TEXT NOT NULL); CREATE TABLE IF NOT EXISTS WorkflowState (PackageId TEXT PRIMARY KEY, Status INTEGER NOT NULL); CREATE TABLE IF NOT EXISTS ReviewDecision (DecisionId TEXT PRIMARY KEY, PackageId TEXT NOT NULL, Revision INTEGER NOT NULL, DecidedUtc TEXT NOT NULL, ApproverUserId TEXT NULL, ApproverDisplayName TEXT NULL, IsFinalApproval INTEGER NOT NULL DEFAULT 0);";
            await schema.ExecuteNonQueryAsync();
        }
        await using (var insert = connection.CreateCommand())
        {
            insert.CommandText = "INSERT INTO Document VALUES($document,$name,$type,1); INSERT INTO DocumentVersion VALUES($version,$document,2,$path); INSERT INTO WorkflowState VALUES($document,$status); INSERT INTO ReviewDecision VALUES($decision,$document,2,$decided,$approver,$approverName,1);";
            insert.Parameters.AddWithValue("$document", document.DocumentId.ToString()); insert.Parameters.AddWithValue("$version", document.VersionId.ToString()); insert.Parameters.AddWithValue("$name", name); insert.Parameters.AddWithValue("$type", (int)DrawVerify.Core.Enums.DocumentType.DxfDrawing); insert.Parameters.AddWithValue("$path", name); insert.Parameters.AddWithValue("$status", (int)status); insert.Parameters.AddWithValue("$decision", Guid.NewGuid().ToString()); insert.Parameters.AddWithValue("$decided", document.SharedUtc.ToString("O")); insert.Parameters.AddWithValue("$approver", "approver-1"); insert.Parameters.AddWithValue("$approverName", "Approver One"); await insert.ExecuteNonQueryAsync();
        }
        var repository = new SqliteSharedDocumentRepository(path);
        await repository.CreateShareAsync(document.DocumentId, document.VersionId, recipient, "approver-1", "Approver One", document.SharedUtc, "reference", default);
        if (revoked) await repository.RevokeShareAsync(document.DocumentId, document.VersionId, recipient);
        return document;
    }
}
