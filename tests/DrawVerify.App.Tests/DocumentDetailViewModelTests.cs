using DrawVerify.App.ViewModels;
using DrawVerify.Core.Models;
using Xunit;

namespace DrawVerify.App.Tests;

public sealed class DocumentDetailViewModelTests
{
    [Fact(DisplayName = "TD-UI-001 exposes STEP-derived status and error")]
    public void Details_model_maps_geometry_status_and_stored_error()
    {
        var document = new Document { DocumentType = DocumentType.StepModel, UploadStatus = UploadStatus.Uploaded, GeometryStatus = GeometryStatus.Error, GeometryError = "Measurement extraction failed." };
        var viewModel = new DocumentDetailViewModel();

        viewModel.Load(document);

        Assert.Equal(GeometryStatus.Error, viewModel.GeometryStatus);
        Assert.Equal("Measurement extraction failed.", viewModel.GeometryError);
        Assert.Equal(document, viewModel.Document);
    }

    [Fact(DisplayName = "TD-UI-001 defaults missing geometry to not extracted")]
    public void Empty_details_are_not_reported_as_processed()
    {
        var viewModel = new DocumentDetailViewModel();
        Assert.Equal(GeometryStatus.NotExtracted, viewModel.GeometryStatus);
        Assert.Null(viewModel.GeometryError);
    }
}
