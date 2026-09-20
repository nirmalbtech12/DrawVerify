using DrawVerify.Core.Enums;
using DrawVerify.Core.Models;
using DrawVerify.Infrastructure.Persistence;
using Xunit;

namespace DrawVerify.Infrastructure.Tests;

public sealed class SqliteInternalSharedLinkRepositoryTests
{
    [Fact(DisplayName = "TSD-AC03-VALIDATE SQLite preserves active expired and inactive link states")]
    public async Task Repository_preserves_link_validation_state()
    {
        var path = Path.Combine(Path.GetTempPath(), $"drawverify-link-states-{Guid.NewGuid():N}.db");
        try
        {
            var repository = new SqliteInternalSharedLinkRepository(path);
            var active = new InternalSharedLink { DocumentId = Guid.NewGuid(), VersionId = Guid.NewGuid(), Token = "active", Url = "https://drawverify.internal/share?token=active", IsActive = true };
            var expired = active with { LinkId = Guid.NewGuid(), Token = "expired", ExpiresUtc = DateTime.UtcNow.AddMinutes(-1) };
            var inactive = active with { LinkId = Guid.NewGuid(), Token = "inactive", IsActive = false };
            await repository.CreateAsync(active); await repository.CreateAsync(expired); await repository.CreateAsync(inactive);
            Assert.Equal(SharedLinkStatus.Active, (await repository.GetByIdAsync(active.LinkId))!.Status(DateTime.UtcNow));
            Assert.Equal(SharedLinkStatus.Expired, (await repository.GetByIdAsync(expired.LinkId))!.Status(DateTime.UtcNow));
            Assert.Equal(SharedLinkStatus.Inactive, (await repository.GetByIdAsync(inactive.LinkId))!.Status(DateTime.UtcNow));
        }
        finally { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); if (File.Exists(path)) File.Delete(path); }
    }

    [Fact(DisplayName = "TSD-AC13-CONCURRENCY repository usage supports bounded access accounting")]
    public async Task Repository_usage_is_bounded_to_recorded_accesses()
    {
        var path = Path.Combine(Path.GetTempPath(), $"drawverify-link-usage-{Guid.NewGuid():N}.db");
        try
        {
            var repository = new SqliteInternalSharedLinkRepository(path);
            var link = new InternalSharedLink { DocumentId = Guid.NewGuid(), VersionId = Guid.NewGuid(), Token = "usage", Url = "https://drawverify.internal/share?token=usage", IsActive = true };
            await repository.CreateAsync(link);
            await repository.RecordAccessAsync(link.LinkId, "viewer-1", DateTime.UtcNow);
            var usage = await repository.GetUsageAsync(link.LinkId, true);
            Assert.Equal(1, usage.AccessCount);
            Assert.Single(usage.AccessingUsers);
        }
        finally { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); if (File.Exists(path)) File.Delete(path); }
    }

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
            Assert.Equal(link.TokenHash, saved.TokenHash);
            Assert.Equal(link.AccessLevel, saved.AccessLevel);
            Assert.Equal(link.IsActive, saved.IsActive);
            Assert.Null(await repository.FindByTokenAsync("different-token-hash"));
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
