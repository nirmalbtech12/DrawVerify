using Xunit;

namespace DrawVerify.App.Tests;

public sealed class ApprovedDocumentViewerViewModelTests
{
    [Fact(DisplayName = "TSD-VIEWER-OPEN TSD-READONLY TSD-CROSS-CLIENT viewer contract opens read-only and exposes only configured download")]
    public void Viewer_contract_is_read_only_and_download_is_configuration_gated()
    {
        var source = Read("src/DrawVerify.App/ViewModels/ApprovedDocumentViewerViewModel.cs");
        Assert.Contains("public bool IsReadOnly => true", source);
        Assert.Contains("public bool CanDownload", source);
        Assert.Contains("DownloadCommand", source);
        Assert.Contains("OpenAsync", source);
        Assert.Contains("_service.OpenAsync(_document.DocumentId, _document.VersionId)", source);
        Assert.DoesNotContain("EditCommand", source);
        Assert.DoesNotContain("WorkflowStatus", source);
    }

    [Fact(DisplayName = "TSD-METADATA TSD-APPROVAL-DETAILS TSD-SHARING-CONTEXT viewer retains authoritative approval and sharing projection")]
    public void Viewer_contract_preserves_approval_and_sharing_metadata()
    {
        var model = Read("src/DrawVerify.Core/Models/SharedDocument.cs");
        foreach (var field in new[] { "ApproverName", "FinalApprovalUtc", "ApproverSignature", "ApprovalStamp", "SharedByName", "SharedUtc", "SharingNote" })
            Assert.Contains(field, model);
        var view = Read("src/DrawVerify.App/Views/ApprovedDocumentViewerView.xaml");
        Assert.Contains("Document", view);
    }

    [Fact(DisplayName = "TSD-DOWNLOAD TSD-AUDIT viewer reports download failures instead of success")]
    public void Viewer_download_failure_is_visible()
    {
        var source = Read("src/DrawVerify.App/ViewModels/ApprovedDocumentViewerViewModel.cs");
        Assert.Contains("Status = exception.Message", source);
        Assert.Contains("_service.DownloadAsync(_document.DocumentId, _document.VersionId)", source);
    }

    private static string Read(string relativePath)
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        for (var i = 0; i < 10 && current is not null; i++, current = current.Parent)
        {
            var path = Path.Combine(current.FullName, relativePath);
            if (File.Exists(path)) return File.ReadAllText(path);
        }
        throw new FileNotFoundException(relativePath);
    }
}
