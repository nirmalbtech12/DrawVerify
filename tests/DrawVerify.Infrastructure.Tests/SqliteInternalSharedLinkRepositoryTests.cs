using DrawVerify.Core.Enums;
using DrawVerify.Core.Models;
using DrawVerify.Infrastructure.Persistence;
using Xunit;

namespace DrawVerify.Infrastructure.Tests;

public sealed class SqliteInternalSharedLinkRepositoryTests
{
    [Fact(DisplayName = "TSD-AC01 SQLite persists generated link fields and URL")]
    public async Task Repository_round_trips_link()
    {
        var path = Path.Combine(Path.GetTempPath(), $"drawverify-link-{Guid.NewGuid():N}.db");
        try
        {
            var repository = new SqliteInternalSharedLinkRepository(path);
            var link = new InternalSharedLink { DocumentId = Guid.NewGuid(), VersionId = Guid.NewGuid(), Token = "token-hash", Url = "https://drawverify.internal/share?token=value", AccessLevel = SharedLinkAccessLevel.ViewOnly, IsActive = true };
            await repository.CreateAsync(link);
            var saved = await repository.GetByIdAsync(link.LinkId);
            Assert.NotNull(saved);
            Assert.Equal(link.Url, saved!.Url);
            Assert.Equal(link.DocumentId, saved.DocumentId);
            Assert.Equal(link.VersionId, saved.VersionId);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
