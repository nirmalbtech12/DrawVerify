using DrawVerify.Application.Services;
using DrawVerify.Core.Authorization;
using DrawVerify.Core.Interfaces;
using DrawVerify.Core.Models;
using Xunit;

namespace DrawVerify.Application.Tests;

public sealed class ReportTemplateExchangeServiceTests
{
    [Fact(DisplayName = "TSD-AC13 export/import round trips validated settings as inactive")]
    public void Exchange_round_trip_does_not_activate_imported_template()
    {
        var session = new SessionAuthenticationService();
        session.SignIn(new AuthenticatedUser("admin", new[] { SystemRoles.Admin }));
        var audit = new AuditFake();
        var service = new ReportTemplateExchangeService(session, new RoleAuthorization(), audit);
        var source = new ReportTemplate { IsOrganizationDefault = true, Header = new() { OrganizationName = "Acme" } };

        var imported = service.Import(service.Export(source));

        Assert.Equal(source.Id, imported.Id);
        Assert.Equal("Acme", imported.Header.OrganizationName);
        Assert.False(imported.IsOrganizationDefault);
        Assert.Equal(new[] { "Exported", "Imported" }, audit.Entries.Select(x => x.Action));
    }

    [Theory(DisplayName = "TSD-AC13 malformed or unsafe exchange payloads are rejected")]
    [InlineData("{}")]
    [InlineData("{\"schemaVersion\":99,\"template\":{}}")]
    [InlineData("{\"schemaVersion\":1,\"template\":{\"Name\":\"x\",\"LogoAssetPath\":\"..\\secret\"}}")]
    public void Invalid_exchange_payload_fails_before_activation(string json)
    {
        var session = new SessionAuthenticationService();
        session.SignIn(new AuthenticatedUser("admin", new[] { SystemRoles.Admin }));
        var service = new ReportTemplateExchangeService(session, new RoleAuthorization(), new AuditFake());

        Assert.ThrowsAny<Exception>(() => service.Import(json));
    }

    [Fact(DisplayName = "TSD-AC12 TSD-AC13 exchange requires Admin authorization")]
    public void Exchange_is_denied_without_admin_session()
    {
        var session = new SessionAuthenticationService();
        var service = new ReportTemplateExchangeService(session, new RoleAuthorization(), new AuditFake());

        Assert.Throws<UnauthorizedAccessException>(() => service.Export(new ReportTemplate()));
        Assert.Throws<UnauthorizedAccessException>(() => service.Import("{}"));
    }

    private sealed class AuditFake : IReportTemplateAuditRepository
    {
        public List<ReportTemplateAuditEntry> Entries { get; } = new();
        public Task<IReadOnlyList<ReportTemplateAuditEntry>> GetAuditAsync(Guid? templateId = null, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<ReportTemplateAuditEntry>>(Entries);
        public Task RecordAuditAsync(ReportTemplateAuditEntry entry, CancellationToken cancellationToken = default)
        {
            Entries.Add(entry);
            return Task.CompletedTask;
        }
    }
}
