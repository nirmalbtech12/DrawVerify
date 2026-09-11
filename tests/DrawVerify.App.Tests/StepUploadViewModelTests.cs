using DrawVerify.Application.StepUpload;
using Xunit;

namespace DrawVerify.App.Tests;

public sealed class StepUploadViewModelTests
{
    [Fact(DisplayName = "T04 STEP selection is single-file and extension constrained")]
    public void Step_picker_contract_accepts_only_step_variants()
    {
        var view = FindSourceFile("src", "DrawVerify.App", "Views", "StepUploadView.xaml");
        var codeBehind = FindSourceFile("src", "DrawVerify.App", "Views", "StepUploadView.xaml.cs");
        var xaml = File.ReadAllText(view);
        var source = File.ReadAllText(codeBehind);
        Assert.Contains("Filter=\"STEP files (*.step;*.stp)|*.step;*.stp\"", source, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("new OpenFileDialog", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Multiselect = true", source, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Select STEP file", xaml, StringComparison.OrdinalIgnoreCase);
    }

    [Fact(DisplayName = "TD-KAN21-WPF-001 exposes supported-format guidance and rejection message binding")]
    public void Step_upload_view_contains_guidance_and_message_binding()
    {
        var path = FindSourceFile("src", "DrawVerify.App", "Views", "StepUploadView.xaml");
        var xaml = File.ReadAllText(path);
        Assert.Contains("Only STEP files (.step, .stp) are allowed. Please ensure the file is a valid STEP format.", xaml, StringComparison.Ordinal);
        Assert.Contains("{Binding Message}", xaml, StringComparison.Ordinal);
        Assert.Equal("The uploaded file is not a valid STEP file. Please check the file and try again.", StepFileValidator.NotStepMessage);
        Assert.Equal("The uploaded STEP file appears to be invalid or corrupt. Please check the file and try again.", StepFileValidator.InvalidMessage);
        Assert.Equal("The uploaded STEP file appears to be incomplete or corrupt. Please check the file and try again.", StepFileValidator.TruncatedMessage);
    }

    private static string FindSourceFile(params string[] parts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(new[] { directory.FullName }.Concat(parts).ToArray());
            if (File.Exists(candidate)) return candidate;
            directory = directory.Parent;
        }
        throw new FileNotFoundException("Step upload view was not found.");
    }
}
