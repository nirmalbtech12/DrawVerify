using DrawVerify.Core.Models;
using DrawVerify.Infrastructure.Persistence;
using Xunit;

namespace DrawVerify.Infrastructure.Tests;

public sealed class SqliteInternalSharedLinkRevocationTests
{
    [Fact(DisplayName = "TSD-AC06 SQLite revocation invalidates only the selected link")]
    public async Task Revocation_is_immediate_and_scoped()
    {
        var path = Path.Combine(Path.GetTempPath(), $"drawverify-revoke-{Guid.NewGuid():N}.db");
        try
        {
            var repository = new SqliteInternalSharedLinkRepository(path);
            var selected = new InternalSharedLink { DocumentId = Guid.NewGuid(), VersionId = Guid.NewGuid(), Token = "selected", Url = "https://drawverify.internal/share?token=selected", IsActive = true };
            var other = selected with { LinkId = Guid.NewGuid(), Token = "other", Url = "https://drawverify.internal/share?token=other" };
            await repository.CreateAsync(selected);
            await repository.CreateAsync(other);
            await repository.RevokeAsync(selected.LinkId, DateTime.UtcNow);
            Assert.False((await repository.GetByIdAsync(selected.LinkId))!.IsActive);
            Assert.True((await repository.GetByIdAsync(other.LinkId))!.IsActive);
            await Assert.ThrowsAsync<InvalidOperationException>(() => repository.RevokeAsync(selected.LinkId, DateTime.UtcNow));
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
