using System.Text;
using DrawVerify.Application.DxfDimensionExtraction;
using DrawVerify.Application.DxfUpload;
using Xunit;

namespace DrawVerify.Application.Tests;

public sealed class Kan125DxfValidationAndExtractionTests
{
    [Fact(DisplayName = "TD-06 accepts externally editable supported DXF records")]
    public async Task Supported_external_records_validate_and_extract()
    {
        var documentId = Guid.NewGuid(); var versionId = Guid.NewGuid();
        const string dxf = "0\nSECTION\n2\nENTITIES\n0\nLINE\n10\n0\n20\n0\n11\n5\n21\n5\n0\nCIRCLE\n10\n2\n20\n2\n40\n1\n0\nENDSEC\n0\nEOF\n";
        await using var stream = new MemoryStream(Encoding.ASCII.GetBytes(dxf));
        Assert.True((await DxfFileValidator.ValidateAsync(stream, stream.Length, 1)).IsValid);
        stream.Position = 0;
        var result = await new DxfDimensionParser().ParseAsync(stream, documentId, versionId);
        Assert.True(result.Succeeded);
        Assert.All(result.Dimensions, dimension => Assert.Equal(documentId, dimension.DocumentId));
        Assert.All(result.Dimensions, dimension => Assert.Equal(versionId, dimension.DocumentVersionId));
    }

    [Theory(DisplayName = "TD-08 rejects malformed DXF with a clear diagnostic")]
    [InlineData("0\nSECTION\n2\nHEADER\n0\nENDSEC\n0\nEOF\n", "ENTITIES")]
    [InlineData("0\nSECTION\n2\nENTITIES\n0\nLINE\n10\nbad\n", "readable")]
    public async Task Invalid_payload_is_not_verifiable(string payload, string diagnostic)
    {
        await using var stream = new MemoryStream(Encoding.ASCII.GetBytes(payload));
        var result = await new DxfDimensionParser().ParseAsync(stream, Guid.NewGuid(), Guid.NewGuid());
        Assert.False(result.Succeeded);
        Assert.True((result.ErrorMessage ?? string.Empty).Contains(diagnostic, StringComparison.OrdinalIgnoreCase) || result.Warnings.Any(w => w.Contains(diagnostic, StringComparison.OrdinalIgnoreCase)));
    }

    [Fact(DisplayName = "TD-07 replacement path preserves immutable versioning contract")]
    public void Upload_service_contract_requires_new_version_and_uploaded_source()
    {
        var source = Read("src", "DrawVerify.Application", "Services", "DxfDocumentService.cs");
        Assert.Contains("DrawingSource.Uploaded", source);
        Assert.Contains("NewVersion", source);
        Assert.Contains("Replace", source, StringComparison.OrdinalIgnoreCase);
    }

    private static string Read(params string[] parts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null) { var path = Path.Combine(new[] { directory.FullName }.Concat(parts).ToArray()); if (File.Exists(path)) return File.ReadAllText(path); directory = directory.Parent; }
        throw new FileNotFoundException(string.Join(Path.DirectorySeparatorChar, parts));
    }
}
