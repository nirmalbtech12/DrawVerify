using DrawVerify.App.ViewModels;
using DrawVerify.Core.Models;
using Xunit;

namespace DrawVerify.App.Tests;

public sealed class DocumentDetailViewModelTests
{
    [Fact(DisplayName = "TD-KAN23-WPF-003 details display all DXF metadata and statuses")]
    public void Dxf_details_expose_required_metadata()
    {
        var document = new Document { OriginalFileName = "drawing.dxf", FileSizeBytes = 42, DocumentType = DocumentType.DxfDrawing, UploadStatus = UploadStatus.Uploaded, VerificationStatus = DrawVerify.Core.Enums.VerificationStatus.PendingVerification, UploaderUserId = "user-1", UploaderDisplayName = "Maker", CurrentVersion = 1 };
        var viewModel = new DocumentDetailViewModel(document);
        Assert.Equal("drawing.dxf", viewModel.OriginalFileName);
        Assert.Equal(42, viewModel.FileSizeBytes);
        Assert.Equal(DocumentType.DxfDrawing, viewModel.DocumentType);
        Assert.Equal(UploadStatus.Uploaded, viewModel.UploadStatus);
        Assert.Equal("user-1", viewModel.UploaderUserId);
        Assert.Equal("Maker", viewModel.UploaderDisplayNameSnapshot);
        Assert.Equal("Pending Verification", viewModel.VerificationStatusDisplay);
        Assert.Equal(1, viewModel.CurrentVersion);
    }
    [Fact(DisplayName = "TD-KAN116-APP-003 exposes retained document detail state without uploader identity")]
    public void Details_model_maps_geometry_status_and_stored_error()
    {
        var document = new Document { DocumentType = DocumentType.StepModel, UploadStatus = UploadStatus.Uploaded, GeometryStatus = GeometryStatus.Error, GeometryError = "Measurement extraction failed." };
        var viewModel = new DocumentDetailViewModel();

        viewModel.Load(document);

        Assert.Equal(GeometryStatus.Error, viewModel.GeometryStatus);
        Assert.Equal("Measurement extraction failed.", viewModel.GeometryError);
        Assert.Equal(document, viewModel.Document);
    }

    [Fact(DisplayName = "TD-KAN116-APP-003 defaults missing geometry to not extracted")]
    public void Empty_details_are_not_reported_as_processed()
    {
        var viewModel = new DocumentDetailViewModel();
        Assert.Equal(GeometryStatus.NotExtracted, viewModel.GeometryStatus);
        Assert.Null(viewModel.GeometryError);
    }
}
