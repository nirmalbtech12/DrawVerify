using DrawVerify.Infrastructure.Persistence;
using DrawVerify.Core.Models;
using Microsoft.Data.Sqlite;
using Xunit;

namespace DrawVerify.Infrastructure.Tests;

public sealed class SqliteReportTemplateDocumentRepositoryTests
{
    [Fact(DisplayName = "TSD-05 document repository stores bytes and source linkage")]
    public async Task Document_round_trip_preserves_metadata_and_bytes()
    {
        var path = Path.Combine(Path.GetTempPath(), $"drawverify-doc-{Guid.NewGuid():N}.db");
        try { var repository = new SqliteReportTemplateDocumentRepository(path); var source = new ReportTemplateSource { FileName = "source.pdf", PdfBytes = [1, 2] }; await repository.SaveSourceAsync(source); var document = new ReportTemplateDocument { TemplateId = Guid.NewGuid(), Version = 2, SourceId = source.SourceId, SourceFileName = source.FileName, Content = [0x50, 0x4B] }; await repository.SaveAsync(document); var actual = await repository.GetAsync(document.TemplateId, 2); var actualSource = await repository.GetSourceAsync(source.SourceId); Assert.NotNull(actual); Assert.Equal(document.TemplateId, actual!.TemplateId); Assert.Equal(document.Version, actual.Version); Assert.Equal(document.SourceId, actual.SourceId); Assert.Equal(document.SourceFileName, actual.SourceFileName); Assert.Equal(document.Content, actual.Content); Assert.NotNull(actualSource); Assert.Equal(source.SourceId, actualSource!.SourceId); Assert.Equal(source.FileName, actualSource.FileName); Assert.Equal(source.PdfBytes, actualSource.PdfBytes); }
        finally { SqliteConnection.ClearAllPools(); foreach (var suffix in new[] { "", "-wal", "-shm" }) { var file = path + suffix; if (File.Exists(file)) File.Delete(file); } }
    }

    [Fact(DisplayName = "TSD-07 repository versions replacement documents without changing unrelated state")]
    public async Task Versioned_documents_are_retrievable_independently()
    {
        var path = Path.Combine(Path.GetTempPath(), $"drawverify-doc-{Guid.NewGuid():N}.db");
        try { var repository = new SqliteReportTemplateDocumentRepository(path); var source = new ReportTemplateSource { FileName = "source.pdf", PdfBytes = [1] }; await repository.SaveSourceAsync(source); var target = new ReportTemplateDocument { TemplateId = Guid.NewGuid(), Version = 1, SourceId = source.SourceId, SourceFileName = source.FileName, Content = [1] }; var other = target with { TemplateId = Guid.NewGuid(), Content = [9] }; await repository.SaveAsync(target); await repository.SaveAsync(other); await repository.SaveAsync(target with { Version = 2, Content = [2] }, 1); Assert.Equal(new byte[] { 2 }, (await repository.GetAsync(target.TemplateId, 2))!.Content); Assert.Equal(new byte[] { 9 }, (await repository.GetAsync(other.TemplateId, 1))!.Content); }
        finally { SqliteConnection.ClearAllPools(); foreach (var suffix in new[] { "", "-wal", "-shm" }) { var file = path + suffix; if (File.Exists(file)) File.Delete(file); } }
    }
}
