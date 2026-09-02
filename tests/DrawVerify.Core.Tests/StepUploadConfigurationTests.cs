using DrawVerify.Core.Configuration;
using DrawVerify.Core.Enums;
using Xunit;

namespace DrawVerify.Core.Tests;

public sealed class StepUploadConfigurationTests
{
    [Fact]
    public void Defaults_are_the_approved_mvp_values()
    {
        var configuration = new StepUploadConfiguration();

        Assert.Equal(50, configuration.MaxFileSizeMb);
        Assert.Equal("uploads/step", configuration.StorageFolder);
        Assert.Equal("BlockAndConfirm", configuration.DuplicatePolicy);
        configuration.Validate();
        Assert.Equal(DocumentType.StepModel, new DrawVerify.Core.Models.Document().DocumentType);
        Assert.Equal(UploadStatus.Uploaded, new DrawVerify.Core.Models.Document().UploadStatus);
        Assert.Equal(GeometryStatus.PendingValidation, new DrawVerify.Core.Models.Document().GeometryStatus);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Non_positive_size_is_an_explicit_configuration_failure(int size)
    {
        var configuration = new StepUploadConfiguration { MaxFileSizeMb = size };

        Assert.Throws<InvalidOperationException>(() => configuration.Validate());
    }

    [Theory]
    [InlineData("/absolute")]
    [InlineData("")]
    [InlineData("   ")]
    public void Invalid_storage_folder_is_rejected(string folder)
    {
        var configuration = new StepUploadConfiguration { StorageFolder = folder };

        Assert.Throws<InvalidOperationException>(() => configuration.Validate());
    }

    [Fact]
    public void Unsupported_duplicate_policy_is_rejected_without_fallback()
    {
        var configuration = new StepUploadConfiguration { DuplicatePolicy = "Replace" };

        Assert.Throws<InvalidOperationException>(() => configuration.Validate());
    }
}
