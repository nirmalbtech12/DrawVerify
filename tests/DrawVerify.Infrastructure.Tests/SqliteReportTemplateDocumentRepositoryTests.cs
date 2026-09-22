using DrawVerify.Infrastructure.Persistence;
using DrawVerify.Core.Models;
using Microsoft.Data.Sqlite;
using Xunit;

namespace DrawVerify.Infrastructure.Tests;

public sealed class SqliteReportTemplateDocumentRepositoryTests
{
    [Fact(DisplayName = "TSD-05 versioned template document metadata remains readable after source changes")]
    public async Task Versioned_document_round_trips_without_using_a_later_version()
    {
        var path = Path.Combine(Path.GetTempPath(), $"drawverify-template-{Guid.NewGuid():N}.db");
        var templateId = Guid.NewGuid();
        try
        {
            var repository = new SqliteReportTemplateDocumentRepository(path);
            await repository.SaveAsync(new ReportTemplateDocument { TemplateId = templateId, Version = 1, SourceId = Guid.NewGuid(), Content = new byte[] { 1, 2, 3 } });
            await repository.SaveAsync(new ReportTemplateDocument { TemplateId = templateId, Version = 2, SourceId = Guid.NewGuid(), Content = new byte[] { 9 } });

            var captured = await repository.GetAsync(templateId, 1);
            Assert.NotNull(captured);
            Assert.Equal(1, captured!.Version);
            Assert.Equal(new byte[] { 1, 2, 3 }, captured.Content);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (File.Exists(path)) File.Delete(path);
        }
    }
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

    [Fact(DisplayName = "TSD-07 stale and failed document saves retain prior versions")]
    public async Task Stale_and_duplicate_document_saves_do_not_replace_history()
    {
        var path = Path.Combine(Path.GetTempPath(), $"drawverify-doc-rollback-{Guid.NewGuid():N}.db");
        try
        {
            var repository = new SqliteReportTemplateDocumentRepository(path); var templateId = Guid.NewGuid(); var sourceId = Guid.NewGuid();
            var first = new ReportTemplateDocument { TemplateId = templateId, Version = 1, SourceId = sourceId, Content = [1] };
            await repository.SaveAsync(first);
            var second = first with { Version = 2, Content = [2] };
            await repository.SaveAsync(second, 1);
            await Assert.ThrowsAsync<InvalidOperationException>(() => repository.SaveAsync(first with { Version = 3, Content = [3] }, 0));
            await Assert.ThrowsAsync<SqliteException>(() => repository.SaveAsync(second with { Content = [9] }));
            Assert.Equal(new byte[] { 1 }, (await repository.GetAsync(templateId, 1))!.Content);
            Assert.Equal(new byte[] { 2 }, (await repository.GetAsync(templateId, 2))!.Content);
            Assert.Null(await repository.GetAsync(templateId, 3));
        }
        finally { SqliteConnection.ClearAllPools(); foreach (var suffix in new[] { "", "-wal", "-shm" }) { var file = path + suffix; if (File.Exists(file)) File.Delete(file); } }
    }
}
