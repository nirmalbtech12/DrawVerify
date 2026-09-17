using Xunit;

namespace DrawVerify.App.Tests;

public sealed class InternalApprovalStampViewModelTests
{
    [Fact(DisplayName = "T09 workflow UI exposes approval errors and preview stamp state")]
    public void T09_workflow_and_preview_models_expose_stamp_state()
    {
        var workflow = Read("src", "DrawVerify.App", "ViewModels", "WorkflowViewModel.cs");
        var step = Read("src", "DrawVerify.App", "ViewModels", "StepPreviewViewModel.cs");
        var dxf = Read("src", "DrawVerify.App", "ViewModels", "DxfPreviewViewModel.cs");
        Assert.Contains("InternalApprovalStamp", step);
        Assert.Contains("ApprovalStampText", dxf);
        Assert.Contains("Approver", workflow);
    }

    [Fact(DisplayName = "T10 preview views render the persisted stamp metadata")]
    public void T10_preview_views_bind_stamp_text()
    {
        Assert.Contains("ApprovalStampText", Read("src", "DrawVerify.App", "Views", "StepPreviewView.xaml"));
        Assert.Contains("ApprovalStampText", Read("src", "DrawVerify.App", "Views", "DxfPreviewView.xaml"));
    }

    private static string Read(params string[] parts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null) { var path = Path.Combine(new[] { directory.FullName }.Concat(parts).ToArray()); if (File.Exists(path)) return File.ReadAllText(path); directory = directory.Parent; }
        throw new FileNotFoundException(string.Join(Path.DirectorySeparatorChar, parts));
    }
}
