using DrawVerify.Application.Services;
using DrawVerify.Core.Models;
using Xunit;

namespace DrawVerify.Application.Tests;

public sealed class ReportTemplateExchangeServiceTests
{
    [Fact(DisplayName = "TSD-AC13 export/import round trips validated settings as inactive")]
    public void Exchange_round_trip_does_not_activate_imported_template()
    {
        var service = new ReportTemplateExchangeService();
        var source = new ReportTemplate { IsOrganizationDefault = true, Header = new() { OrganizationName = "Acme" } };
        var imported = service.Import(service.Export(source));
        Assert.Equal(source.Id, imported.Id); Assert.Equal("Acme", imported.Header.OrganizationName); Assert.False(imported.IsOrganizationDefault);
    }

    [Theory(DisplayName = "TSD-AC13 malformed or unsafe exchange payloads are rejected")]
    [InlineData("{}")]
    [InlineData("{\"schemaVersion\":99,\"template\":{}}")]
    [InlineData("{\"schemaVersion\":1,\"template\":{\"Name\":\"x\",\"LogoAssetPath\":\"..\\secret\"}}")]
    public void Invalid_exchange_payload_fails_before_activation(string json) => Assert.ThrowsAny<Exception>(() => new ReportTemplateExchangeService().Import(json));
}
