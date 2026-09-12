using Xunit;

namespace DrawVerify.Application.Tests;

public sealed class CombinedUploadVerifyServiceTests
{
    [Fact(DisplayName = "T08 readiness requires exact identities, processed STEP, and DXF dimensions")]
    public void Readiness_contract_requires_all_inputs()
    {
        var source = Read("src", "DrawVerify.App", "ViewModels", "CombinedUploadVerifyViewModel.cs");
        Assert.Contains("GeometryStatus.Processed", source);
        Assert.Contains("VerificationStatus.DimensionsExtracted", source);
        Assert.Contains("_dxfDimensionCount > 0", source);
        Assert.Contains("StepDocumentId is not null && DxfDocumentId is not null", source);
    }

    [Fact(DisplayName = "T09 existing upload services remain the validation boundary")]
    public void Upload_services_are_composed_without_substitution()
    {
        Assert.Contains("StepUploadViewModel", Read("src", "DrawVerify.App", "Services", "CompositionRoot.cs"));
        Assert.Contains("DxfUploadViewModel", Read("src", "DrawVerify.App", "Services", "CompositionRoot.cs"));
    }

    [Fact(DisplayName = "T10 exact document identities flow into processing")]
    public void Exact_ids_are_used_for_extraction()
    {
        var source = Read("src", "DrawVerify.App", "ViewModels", "CombinedUploadVerifyViewModel.cs");
        Assert.Contains("Extract(document.DocumentId", source);
        Assert.Contains("ExtractAsync(document.DocumentId", source);
    }

    [Fact(DisplayName = "T11 processing failures are logged and non-ready")]
    public void Processing_failures_preserve_safe_error_and_disabled_readiness()
    {
        var source = Read("src", "DrawVerify.App", "ViewModels", "CombinedUploadVerifyViewModel.cs");
        Assert.Contains("_logger?.Log(exception", source);
        Assert.Contains("processing failed", source);
        Assert.Contains("CanVerify", source);
    }

    [Fact(DisplayName = "T12 verification selects an exact pair")]
    public void Verification_receives_both_selected_ids()
    {
        var source = Read("src", "DrawVerify.App", "ViewModels", "CombinedUploadVerifyViewModel.cs");
        Assert.Contains("SelectPairAsync(StepDocumentId!.Value, DxfDocumentId!.Value, StepDocumentVersionId!.Value, DxfDocumentVersionId!.Value)", source);
        Assert.Contains("GetOrCreateSessionAsync(stepDocumentId, dxfDocumentId", Read("src", "DrawVerify.App", "ViewModels", "VerificationViewModel.cs"));
    }

    [Fact(DisplayName = "T13 verification busy state prevents duplicate submissions")]
    public void Verification_command_is_busy_gated()
    {
        var source = Read("src", "DrawVerify.App", "ViewModels", "CombinedUploadVerifyViewModel.cs");
        Assert.Contains("VerifyCommand", source);
        Assert.Contains("CanVerify && !IsBusy", source);
        Assert.Contains("_isProcessing = true", source);
    }

    [Fact(DisplayName = "T15 cancellation and restart are non-success states")]
    public void Reset_cancels_processing_and_clears_success_state()
    {
        var source = Read("src", "DrawVerify.App", "ViewModels", "CombinedUploadVerifyViewModel.cs");
        Assert.Contains("_processingCancellation", source);
        Assert.Contains("CancelAndDispose()", source);
        Assert.Contains("ProcessingStatus = \"Upload both files to begin processing.\"", source);
    }

    [Fact(DisplayName = "T20 diagnostics retain operation context")]
    public void Diagnostics_include_operation_context()
    {
        var source = Read("src", "DrawVerify.App", "ViewModels", "CombinedUploadVerifyViewModel.cs");
        Assert.Contains("Combined STEP processing", source);
        Assert.Contains("Combined DXF extraction", source);
        Assert.Contains("Combined verification", source);
        Assert.Contains("documentId=", source);
    }

    [Fact(DisplayName = "T21 existing comparison service remains authoritative")]
    public void Combined_flow_delegates_to_existing_matching_service()
    {
        var source = Read("src", "DrawVerify.App", "Services", "CompositionRoot.cs");
        Assert.Contains("CreateDimensionMatchingService", source);
        Assert.Contains("new VerificationViewModel", source);
    }

    [Fact(DisplayName = "T25 replacement resets current association without deleting history")]
    public void Replacement_reset_is_local_to_current_workflow()
    {
        var source = Read("src", "DrawVerify.App", "ViewModels", "CombinedUploadVerifyViewModel.cs");
        Assert.DoesNotContain("DELETE", source, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("_stepDocument = null", source);
        Assert.Contains("_dxfDocument = null", source);
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
