using System.Reflection;
using DrawVerify.App.Navigation;
using DrawVerify.App.ViewModels;
using DrawVerify.Core.Authorization;
using DrawVerify.Core.Models;
using Xunit;

namespace DrawVerify.App.Tests;

public sealed class CombinedUploadVerifyViewModelTests
{
    [Fact(DisplayName = "T02 Maker access and denied access are explicit")]
    public void Maker_access_is_guarded_before_workflow_creation()
    {
        var guard = Read("src", "DrawVerify.App", "Navigation", "ScreenAccessGuard.cs");
        Assert.Contains("ScreenRegistry.UploadVerify", guard);
        Assert.Contains("RolePermission.Upload", guard);
        Assert.Equal("UploadVerify", ScreenRegistry.UploadVerify);
    }

    [Fact(DisplayName = "T03 combined workflow has four labelled steps and accessible states")]
    public void Four_step_contract_is_present()
    {
        var source = Read("src", "DrawVerify.App", "ViewModels", "CombinedUploadVerifyViewModel.cs");
        var view = Read("src", "DrawVerify.App", "Views", "CombinedUploadVerifyView.xaml");
        Assert.Contains("Step 1: Upload 3D Model", source);
        Assert.Contains("Step 2: Upload 2D Drawing", source);
        Assert.Contains("Step 3: Verify", source);
        Assert.Contains("Step 4: Comparison Results", source);
        Assert.Contains("Content=\"Verify\"", view);
        Assert.Contains("StepStatus", view);
        Assert.Contains("DxfStatus", view);
        Assert.Contains("ProcessingStatus", view);
    }

    [Fact(DisplayName = "T05 partial failure preserves the successful input")]
    public void Partial_failure_keeps_each_document_independent()
    {
        var source = Read("src", "DrawVerify.App", "ViewModels", "CombinedUploadVerifyViewModel.cs");
        Assert.Contains("_stepDocument = document", source);
        Assert.Contains("_dxfDocument = document", source);
        Assert.Contains("Verification remains disabled", source);
        Assert.Contains("CanVerify", source);
    }

    [Fact(DisplayName = "T06 replacement resets stale state")]
    public void Reset_clears_documents_results_and_status()
    {
        var source = Read("src", "DrawVerify.App", "ViewModels", "CombinedUploadVerifyViewModel.cs");
        Assert.Contains("public void ResetWorkflow()", source);
        Assert.Contains("_stepDocument = null", source);
        Assert.Contains("_dxfDocument = null", source);
        Assert.Contains("_dxfDimensionCount = 0", source);
    }

    [Fact(DisplayName = "T07 new workflow resets local state")]
    public void New_workflow_command_is_bound_to_reset()
    {
        var source = Read("src", "DrawVerify.App", "ViewModels", "CombinedUploadVerifyViewModel.cs");
        Assert.Contains("NewWorkflowCommand", source);
        Assert.Contains("ResetWorkflow()", source);
        Assert.Contains("Start New Upload &amp; Verify Workflow", Read("src", "DrawVerify.App", "Views", "CombinedUploadVerifyView.xaml"));
    }

    [Fact(DisplayName = "T16 STEP preview exposes load and highlight state")]
    public void Step_preview_has_failure_safe_loading_and_feature_navigation()
    {
        var source = Read("src", "DrawVerify.App", "ViewModels", "StepPreviewViewModel.cs");
        Assert.Contains("LoadModel", source);
        Assert.Contains("HighlightAndFit", source);
        Assert.Contains("STEP preview unavailable", source);
        Assert.Contains("_preview.Dispose()", source);
    }

    [Fact(DisplayName = "T17 DXF preview exposes rendering navigation")]
    public void Dxf_preview_has_zoom_pan_and_failure_safe_anchor_navigation()
    {
        var source = Read("src", "DrawVerify.App", "ViewModels", "DxfPreviewViewModel.cs");
        Assert.Contains("SelectDimension", source);
        Assert.Contains("highlighted and centered", source);
        Assert.Contains("Preview unavailable", source);
    }

    [Fact(DisplayName = "T18 extracted metadata and no-dimension state are retained")]
    public void Extraction_state_is_visible_without_fabricated_values()
    {
        var source = Read("src", "DrawVerify.App", "ViewModels", "CombinedUploadVerifyViewModel.cs");
        Assert.Contains("ExtractedDxfDimensionCount", source);
        Assert.Contains("No supported dimensions found", source);
        Assert.Contains("result.Dimensions.Count", source);
        Assert.Contains("VerificationStatus.DimensionsExtracted", source);
    }

    [Fact(DisplayName = "T19 valid and unavailable source anchors are distinct")]
    public void Dxf_anchor_contract_is_explicit()
    {
        var source = Read("src", "DrawVerify.App", "ViewModels", "DxfPreviewViewModel.cs");
        Assert.Contains("entityReference <= 0", source);
        Assert.Contains("IsHighlightVisible = false", source);
        Assert.Contains("IsHighlightVisible = true", source);
    }

    [Fact(DisplayName = "T22 results presentation exposes required status cues")]
    public void Result_view_models_expose_textual_status_and_result_fields()
    {
        var item = Read("src", "DrawVerify.App", "ViewModels", "DimensionMatchItemViewModel.cs");
        var view = Read("src", "DrawVerify.App", "Views", "VerificationView.xaml");
        foreach (var name in new[] { "Status", "DimensionType", "NominalValue", "Tolerance", "Delta", "Reason" }) Assert.Contains(name, item);
        Assert.Contains("MatchPercentageText", view);
    }

    [Fact(DisplayName = "T23 result navigation uses source references")]
    public void Result_navigation_is_wired_to_both_viewers()
    {
        var item = Read("src", "DrawVerify.App", "ViewModels", "DimensionMatchItemViewModel.cs");
        Assert.Contains("public DimensionMatch Match", item);
        Assert.Contains("GeometryReference", item);
        Assert.Contains("PreviewAnchor", item);
    }

    [Fact(DisplayName = "T24 Maker cannot use reserved role actions")]
    public void Reserved_permissions_remain_role_gated()
    {
        var authorization = new RoleAuthorization();
        var maker = new AuthenticatedUser("maker", new[] { SystemRoles.Maker });
        var checker = new AuthenticatedUser("checker", new[] { SystemRoles.Checker });
        var approver = new AuthenticatedUser("approver", new[] { SystemRoles.Approver });
        Assert.False(authorization.IsAllowed(maker, RolePermission.ResolveUnmatched));
        Assert.False(authorization.IsAllowed(maker, RolePermission.ApproveVerification));
        Assert.False(authorization.IsAllowed(maker, RolePermission.ReviewVerification));
        Assert.True(authorization.IsAllowed(checker, RolePermission.ResolveUnmatched));
        Assert.True(authorization.IsAllowed(checker, RolePermission.ApproveVerification));
        Assert.True(authorization.IsAllowed(approver, RolePermission.ReviewVerification));
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
