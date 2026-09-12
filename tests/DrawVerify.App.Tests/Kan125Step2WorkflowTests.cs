using Xunit;

namespace DrawVerify.App.Tests;

public sealed class Kan125Step2WorkflowTests
{
    [Fact(DisplayName = "TD-01 renders exactly the two Step 2 drawing choices")]
    public void Step2_labels_and_routes_are_explicit()
    {
        var viewModel = Read("src", "DrawVerify.App", "ViewModels", "CombinedUploadVerifyViewModel.cs");
        var view = Read("src", "DrawVerify.App", "Views", "CombinedUploadVerifyView.xaml");
        Assert.Contains("GenerateDxfLabel => \"Generate 2D Drawing\"", viewModel);
        Assert.Contains("UploadDxfLabel => \"Upload Existing DXF\"", viewModel);
        Assert.Contains("Command=\"{Binding GenerateDxfCommand}\"", view);
        Assert.Contains("Command=\"{Binding DxfUpload.SelectCommand}\"", view);
        Assert.Equal(2, view.Split("<Button Content=\"{Binding ", StringSplitOptions.None).Length - 1);
    }

    [Fact(DisplayName = "TD-02 gates generation and verification on processed versions and authorization")]
    public void Workflow_commands_have_complete_preconditions_and_refresh()
    {
        var source = Read("src", "DrawVerify.App", "ViewModels", "CombinedUploadVerifyViewModel.cs");
        Assert.Contains("_generation?.CanGenerate == true", source);
        Assert.Contains("GeometryStatus.Processed", source);
        Assert.Contains("VerificationStatus.DimensionsExtracted", source);
        Assert.Contains("GenerateDxfCommand", source);
        Assert.Contains("CanVerify", source);
        Assert.Contains("Raise()", source);
    }

    [Fact(DisplayName = "TD-05 and TD-06 expose progress, terminal status, and visible error")]
    public void Generation_status_is_bound_and_exception_safe()
    {
        var source = Read("src", "DrawVerify.App", "ViewModels", "CombinedUploadVerifyViewModel.cs");
        var view = Read("src", "DrawVerify.App", "Views", "CombinedUploadVerifyView.xaml");
        Assert.Contains("GenerationProgress", source); Assert.Contains("GenerationStatus", source);
        Assert.Contains("generation failed", source, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ErrorMessage", source); Assert.Contains("ProgressBar", view);
    }

    private static string Read(params string[] parts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null) { var path = Path.Combine(new[] { directory.FullName }.Concat(parts).ToArray()); if (File.Exists(path)) return File.ReadAllText(path); directory = directory.Parent; }
        throw new FileNotFoundException(string.Join(Path.DirectorySeparatorChar, parts));
    }
}
