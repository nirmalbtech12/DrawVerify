using DrawVerify.Core.Models;
using DrawVerify.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Xunit;

namespace DrawVerify.Infrastructure.Tests;

public sealed class SqliteMeasurementRepositoryTests
{
    [Fact(DisplayName = "TD-INF-001 persists a version-linked status and replaces measurements")]
    public void Replacement_updates_the_same_document_version()
    {
        var path = Path.Combine(Path.GetTempPath(), $"drawverify-{Guid.NewGuid():N}.db");
        try
        {
            var repository = new SqliteDocumentRepository(path);
            var document = new Document { Name = "part.step", DocumentType = DocumentType.StepModel, UploadStatus = UploadStatus.Uploaded };
            var version = new DocumentVersion { DocumentId = document.Id, StoredFilePath = "part.step" };
            repository.Save(document, version);
            var geometry = new GeometryMeasurement(version.Id, null, 0, 0, 0, 4, 5, 6, 1, 2, 3, "mm");
            repository.ReplaceMeasurements(version.Id, geometry,
                new[] { new FeatureMeasurement(version.Id, "Hole", new Dictionary<string, double> { ["Diameter"] = 2 }, "mm") }, GeometryStatus.Error, "non-solid");

            var stored = repository.GetCurrentVersion(document.Id);
            Assert.NotNull(stored);
            Assert.Equal(version.Id, stored!.Id);
            Assert.Equal(GeometryStatus.Error, stored.GeometryStatus);
            Assert.Equal("non-solid", stored.GeometryError);
            Assert.Equal(version.Id, geometry.DocumentVersionId);
        }
        finally { SqliteConnection.ClearAllPools(); if (File.Exists(path)) File.Delete(path); }
    }

    [Fact(DisplayName = "TD-INF-001 accepts null volume for partial errors")]
    public void Partial_geometry_can_omit_volume()
    {
        var geometry = new GeometryMeasurement(Guid.NewGuid(), null, 1, 2, 3, 4, 5, 6, 2, 3, 4, "mm");
        Assert.Null(geometry.Volume);
        Assert.Equal(3, geometry.Length);
    }
}
