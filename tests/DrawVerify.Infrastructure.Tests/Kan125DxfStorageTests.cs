using DrawVerify.Core.Configuration;
using DrawVerify.Infrastructure.FileStorage;
using Xunit;

namespace DrawVerify.Infrastructure.Tests;

public sealed class Kan125DxfStorageTests
{
    [Fact(DisplayName = "TD-07 stores DXF files within an isolated root and cleans up")]
    public async Task Storage_round_trip_is_contained_and_disposable()
    {
        var root = Path.Combine(Path.GetTempPath(), "drawverify-kan125-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var store = new DxfDocumentFileStore(root, new DxfUploadConfiguration { StorageFolder = "dxf" });
            await using var staging = await store.CreateStagingFileAsync("..\\edited.dxf");
            await staging.Stream.WriteAsync(new byte[] { 1, 2, 3 });
            var relative = await store.MoveToFinalAsync(staging);
            Assert.DoesNotContain("..", relative);
            Assert.EndsWith("edited.dxf", relative, StringComparison.OrdinalIgnoreCase);
            await using var read = await store.OpenReadAsync(relative);
            Assert.Equal(3, read.Length);
            await read.DisposeAsync();
            await store.DeleteAsync(relative);
            Assert.Throws<FileNotFoundException>(() => store.OpenReadAsync(relative).GetAwaiter().GetResult());
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact(DisplayName = "TD-07 sanitizes external names and bounds their length")]
    public async Task Staging_name_is_safe_and_bounded()
    {
        var root = Path.Combine(Path.GetTempPath(), "drawverify-kan125-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var store = new DxfDocumentFileStore(root);
            await using var staging = await store.CreateStagingFileAsync("..\\" + new string('x', 200) + ".dxf");
            Assert.NotNull(staging.SanitizedFileName);
            Assert.True(staging.SanitizedFileName!.Length <= 128);
            Assert.DoesNotContain("\\", staging.SanitizedFileName);
            Assert.DoesNotContain("/", staging.SanitizedFileName);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact(DisplayName = "TD-07 rejects storage path traversal")]
    public void Storage_rejects_paths_outside_root()
    {
        var root = Path.Combine(Path.GetTempPath(), "drawverify-kan125-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new DxfDocumentFileStore(root);
            Assert.Throws<IOException>(() => store.OpenReadAsync("..\\outside.dxf").GetAwaiter().GetResult());
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
}
