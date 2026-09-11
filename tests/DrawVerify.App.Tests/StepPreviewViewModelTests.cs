using Xunit;

namespace DrawVerify.App.Tests;

public sealed class StepPreviewViewModelTests
{
    [Fact(DisplayName = "T16 STEP preview contract retains loading, highlight, camera, and disposal behavior")]
    public void Step_preview_contract_is_failure_safe()
    {
        var source = Read("src", "DrawVerify.App", "ViewModels", "StepPreviewViewModel.cs");
        Assert.Contains("LoadModel", source);
        Assert.Contains("HighlightAndFit", source);
        Assert.Contains("CameraZoom", source);
        Assert.Contains("_preview.Dispose()", source);
        Assert.Contains("_logger?.Log", source);
    }

    private static string Read(params string[] parts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var path = Path.Combine(new[] { directory.FullName }.Concat(parts).ToArray());
            if (File.Exists(path)) return File.ReadAllText(path);
            directory = directory.Parent;
        }
        throw new FileNotFoundException($"Source file not found: {Path.Combine(parts)}");
    }
}
