using DrawVerify.Core.Configuration;
using Xunit;

namespace DrawVerify.Core.Tests;

public sealed class DxfUploadConfigurationTests
{
    [Fact(DisplayName = "TD-KAN23-CORE-001 default configuration is the approved MVP policy")]
    public void Default_configuration_is_valid_and_50_mib()
    {
        var configuration = new DxfUploadConfiguration();
        configuration.Validate();
        Assert.Equal(50, configuration.MaxFileSizeMb);
        Assert.Equal("uploads/dxf", configuration.StorageFolder);
        Assert.Equal("BlockAndConfirm", configuration.DuplicatePolicy);
        Assert.Equal(50L * 1024 * 1024, configuration.MaxFileSizeBytes);
    }

    [Theory(DisplayName = "TD-KAN23-CORE-001 invalid configuration fails explicitly")]
    [InlineData(0, "uploads/dxf", "BlockAndConfirm")]
    [InlineData(-1, "uploads/dxf", "BlockAndConfirm")]
    [InlineData(50, "", "BlockAndConfirm")]
    [InlineData(50, "../outside", "BlockAndConfirm")]
    [InlineData(50, "uploads/dxf", "Allow")]
    public void Invalid_configuration_is_rejected(int size, string folder, string policy)
    {
        var configuration = new DxfUploadConfiguration { MaxFileSizeMb = size, StorageFolder = folder, DuplicatePolicy = policy };
        Assert.Throws<InvalidOperationException>(() => configuration.Validate());
    }
}
