using DrawVerify.Application.Services;
using DrawVerify.Core.Interfaces;
using DrawVerify.Core.Models;
using Xunit;

namespace DrawVerify.Application.Tests;

public sealed class AuditFailureObservabilityTests
{
    [Fact]
    public async Task TD_APP_005_repository_and_integrity_failures_are_logged_and_rethrown()
    {
        var logger = new LoggerFake(); var document = Guid.NewGuid(); var repository = new FailingRepository();
        var session = new SessionAuthenticationService(); session.SignIn(new AuthenticatedUser("approver", new[] { "Approver" }));
        var service = new AuditLogService(repository, session, logger: logger);
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.QueryDocumentAsync(document));
        Assert.Contains(document.ToString(), logger.Context!); Assert.Contains("Document audit query", logger.Operation!);
    }

    [Fact]
    public async Task TD_APP_005_invalid_range_is_rejected_without_repository_access()
    {
        var repository = new FailingRepository(); var session = new SessionAuthenticationService(); session.SignIn(new AuthenticatedUser("approver", new[] { "Approver" }));
        await Assert.ThrowsAsync<ArgumentException>(() => new AuditLogService(repository, session).QueryDocumentAsync(Guid.NewGuid(), new AuditLogQuery(DateTimeOffset.MaxValue, DateTimeOffset.MinValue)));
        Assert.Equal(0, repository.Calls);
    }

    private sealed class FailingRepository : IDocumentAuditRepository
    {
        public int Calls { get; private set; }
        public Task AppendAsync(AuditEntry entry, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<IReadOnlyList<DocumentAuditRecord>> QueryAsync(AuditLogQuery query, CancellationToken cancellationToken = default) { Calls++; return Task.FromException<IReadOnlyList<DocumentAuditRecord>>(new InvalidOperationException("database failure")); }
        public Task VerifyIntegrityAsync(Guid documentId, CancellationToken cancellationToken = default) => Task.FromException(new InvalidOperationException("integrity failure"));
    }
    private sealed class LoggerFake : ICentralizedLogger
    {
        public string? Operation { get; private set; } public string? Context { get; private set; }
        public void Log(Exception exception, string operation, string context) => (Operation, Context) = (operation, context);
    }
}
