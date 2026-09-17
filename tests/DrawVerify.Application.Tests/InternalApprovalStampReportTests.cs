using System.Text.Json;
using DrawVerify.Application.Services;
using DrawVerify.Core.Authorization;
using DrawVerify.Core.Enums;
using DrawVerify.Core.Models;
using DrawVerify.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Xunit;

namespace DrawVerify.Application.Tests;

public sealed class InternalApprovalStampReportTests
{
    private static readonly Guid Document = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly DateTimeOffset Applied = new(2026, 1, 15, 10, 30, 0, TimeSpan.Zero);

    [Fact(DisplayName = "T12 exports carry stamp metadata and verification status")]
    public async Task T12_audit_csv_contains_persisted_stamp_metadata()
    {
        var path = Path.Combine(Path.GetTempPath(), $"drawverify-stamp-export-{Guid.NewGuid():N}.db");
        try
        {
            var stamp = InternalApprovalStamp.Create(Document, 1, "approver-001", "Approver One", Applied, "fingerprint-001", Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"));
            var repository = new SqliteAuditRepository(path);
            await repository.WriteAsync(new AuditEntry { DocumentId = Document, UserId = "approver-001", ActorRole = SystemRoles.Approver, DocumentRevision = "1", DocumentVersion = "REV-001", Action = DocumentAuditAction.InternalApprovalStampApplied, TimestampUtc = Applied.UtcDateTime, DetailsJson = JsonSerializer.Serialize(new { stampId = stamp.StampId, stampApproverUserId = stamp.ApproverUserId, stampApproverDisplayName = stamp.ApproverDisplayName, stampApproverRole = stamp.ApproverRole, stampAppliedUtc = stamp.AppliedUtc, stampFingerprintReference = stamp.FingerprintReference, stampIntegrityHash = stamp.IntegrityHash, verificationResult = "Valid" }) });
            var session = new SessionAuthenticationService();
            session.SignIn(new AuthenticatedUser("admin", new[] { SystemRoles.Admin }));
            var csv = await new AuditLogService(repository, session).ExportDocumentCsvAsync(Document);
            Assert.Contains(stamp.StampId.ToString(), csv);
            Assert.Contains(stamp.ApproverUserId, csv);
            Assert.Contains(stamp.ApproverDisplayName, csv);
            Assert.Contains(stamp.ApproverRole, csv);
            Assert.Contains(stamp.FingerprintReference, csv);
            Assert.Contains(stamp.IntegrityHash, csv);
            Assert.Contains("Valid", csv);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            foreach (var file in new[] { path, path + "-wal", path + "-shm" }) if (File.Exists(file)) File.Delete(file);
        }
    }

    [Fact(DisplayName = "T17 compliance exports include stamp metadata and verification status")]
    public void T17_markdown_report_contains_stamp_projection()
    {
        var stamp = InternalApprovalStamp.Create(Document, 1, "approver-001", "Approver One", Applied, "fingerprint-001", Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"));
        var markdown = new VerificationReport { InternalApprovalStamp = stamp }.ToMarkdown();
        Assert.Contains(stamp.StampId.ToString(), markdown);
        Assert.Contains(stamp.ApproverDisplayName, markdown);
        Assert.Contains(stamp.FingerprintReference, markdown);
        Assert.Contains(stamp.VerificationStatus.ToString(), markdown);
    }
}
