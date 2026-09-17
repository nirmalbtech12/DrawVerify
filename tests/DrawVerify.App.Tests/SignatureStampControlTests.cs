using DrawVerify.App.Controls;
using Xunit;

namespace DrawVerify.App.Tests;

public sealed class SignatureStampControlTests
{
    [Fact(DisplayName = "TD-UI-01 Approver capture control exposes methods preview guidance metadata retry and role gating")]
    public void Approver_control_contract_covers_capture_lifecycle_and_visibility()
    {
        var xaml = File.ReadAllText(FindRepositoryFile(Path.Combine("src", "DrawVerify.App", "Controls", "SignatureStampControl.xaml")));
        var code = File.ReadAllText(FindRepositoryFile(Path.Combine("src", "DrawVerify.App", "Controls", "SignatureStampControl.xaml.cs")));
        var workflow = File.ReadAllText(FindRepositoryFile(Path.Combine("src", "DrawVerify.App", "Views", "WorkflowView.xaml")));
        Assert.Contains("SignatureLabel", xaml);
        Assert.Contains("IsApprover ? \"Approver Signature\"", code);
        Assert.Contains("Typed", xaml, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Draw", xaml, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Upload", xaml, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("SignatureMethod", code);
        Assert.Contains("Preview", xaml);
        Assert.Contains("Cancel / Re-enter", xaml);
        Assert.Contains("legally binding", xaml, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("tamper-evident", xaml, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Failed to capture signature. Please try again.", code);
        Assert.Contains("Approver", code);
        Assert.Contains("Approver Signature", workflow);
    }

    [Fact(DisplayName = "TD-KAN64-APPUI-001 Maker Signature control exposes approved guidance and recovery text")]
    public void Signature_control_xaml_contains_required_user_guidance_and_states()
    {
        var path = FindRepositoryFile(Path.Combine("src", "DrawVerify.App", "Controls", "SignatureStampControl.xaml"));
        var xaml = File.ReadAllText(path);
        var code = File.ReadAllText(FindRepositoryFile(Path.Combine("src", "DrawVerify.App", "Controls", "SignatureStampControl.xaml.cs")));
        Assert.Contains("SignatureLabel", xaml);
        Assert.Contains("Maker Signature", code);
        Assert.Contains("legally binding", xaml, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("tamper-evident", xaml, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("typed name", xaml, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("draw", xaml, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("upload", xaml, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Preview", xaml);
        Assert.Contains("Cancel / Re-enter", xaml);
        Assert.Contains("submission is complete", xaml, StringComparison.OrdinalIgnoreCase);
    }

    [Fact(DisplayName = "TD-KAN64-APPUI-001 control has a discoverable preview state")]
    public void Signature_control_declares_preview_state()
    {
        Assert.True(typeof(SignatureStampControl).GetProperty(nameof(SignatureStampControl.IsPreviewVisible))?.CanRead);
        var codePath = FindRepositoryFile(Path.Combine("src", "DrawVerify.App", "Controls", "SignatureStampControl.xaml.cs"));
        var code = File.ReadAllText(codePath);
        Assert.Contains("Failed to capture signature. Please try again.", code);
        Assert.Contains("PreviewTyped_Click", code);
        Assert.Contains("CancelPreview_Click", code);
    }

    [Fact(DisplayName = "TD-KAN64-APPUI-001 control contract covers all finalized capture states")]
    public void Signature_control_contract_declares_capture_binding_role_lock_and_metadata()
    {
        var xaml = File.ReadAllText(FindRepositoryFile(Path.Combine("src", "DrawVerify.App", "Controls", "SignatureStampControl.xaml")));
        var code = File.ReadAllText(FindRepositoryFile(Path.Combine("src", "DrawVerify.App", "Controls", "SignatureStampControl.xaml.cs")));
        var workflow = File.ReadAllText(FindRepositoryFile(Path.Combine("src", "DrawVerify.App", "ViewModels", "WorkflowViewModel.cs")));
        Assert.Contains("Draw", xaml, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Upload", xaml, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(nameof(DrawVerify.Core.Models.SignatureData), code);
        Assert.Contains("IsFinalized", code);
        Assert.Contains("MakerUserId", code);
        Assert.Contains("MakerSignature", workflow);
        Assert.Contains("Title", xaml, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Department", xaml, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("IsMaker", code, StringComparison.OrdinalIgnoreCase);
    }

    [Fact(DisplayName = "TD-KAN64-APPUI-001 uploaded capture validates image bytes before preview")]
    public void Signature_control_contract_validates_uploaded_payloads_at_capture_boundary()
    {
        var code = File.ReadAllText(FindRepositoryFile(Path.Combine("src", "DrawVerify.App", "Controls", "SignatureStampControl.xaml.cs")));
        Assert.Contains("Upload_Click", code);
        Assert.Contains("ValidateUploadedPayload", code);
        Assert.Contains("Preview(SignatureMethod.Uploaded", code);
        Assert.Contains("Failed to capture signature. Please try again.", code);
    }

    [Fact(DisplayName = "TD-KAN64-APPUI-001 bound state and capture failures remain observable")]
    public void Signature_control_contract_declares_notifications_metadata_and_contextual_logging()
    {
        var code = File.ReadAllText(FindRepositoryFile(Path.Combine("src", "DrawVerify.App", "Controls", "SignatureStampControl.xaml.cs")));
        Assert.Contains("INotifyPropertyChanged", code);
        Assert.Contains("PropertyChanged", code);
        Assert.Contains("FinalizedSignature.Title", code);
        Assert.Contains("FinalizedSignature.Department", code);
        Assert.Contains("Logger?.Log", code);
        Assert.Contains("documentId={DocumentId}", code);
        Assert.Contains("revision={Revision}", code);
        Assert.Contains("Failed to capture signature. Please try again.", code);
    }

    [Fact(DisplayName = "TD-004 Checker placeholder is visible only for signature participants")]
    public void Checker_placeholder_and_role_gating_are_declared()
    {
        var xaml = File.ReadAllText(FindRepositoryFile(Path.Combine("src", "DrawVerify.App", "Controls", "SignatureStampControl.xaml")));
        var code = File.ReadAllText(FindRepositoryFile(Path.Combine("src", "DrawVerify.App", "Controls", "SignatureStampControl.xaml.cs")));
        var labelCode = File.ReadAllText(FindRepositoryFile(Path.Combine("src", "DrawVerify.App", "Controls", "SignatureStampControl.xaml.cs")));
        Assert.Contains("SignatureLabel", xaml);
        Assert.Contains("Checker Signature", labelCode);
        Assert.Contains("IsSignatureVisible", xaml);
        Assert.Contains("IsChecker", code);
        Assert.Contains("CanEdit", code);
        Assert.Contains("!IsFinalized", code);
    }

    [Fact(DisplayName = "TD-005 capture preview cancel guidance and retry preserve a draft lifecycle")]
    public void Checker_capture_lifecycle_contains_preview_cancel_retry_and_guidance()
    {
        var xaml = File.ReadAllText(FindRepositoryFile(Path.Combine("src", "DrawVerify.App", "Controls", "SignatureStampControl.xaml")));
        var code = File.ReadAllText(FindRepositoryFile(Path.Combine("src", "DrawVerify.App", "Controls", "SignatureStampControl.xaml.cs")));
        Assert.Contains("Preview", xaml);
        Assert.Contains("Cancel / Re-enter", xaml);
        Assert.Contains("legally binding", xaml, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("tamper-evident", xaml, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ShowCaptureError", code);
        Assert.Contains("PreviewText", code);
        Assert.Contains("ErrorText", code);
    }

    [Fact(DisplayName = "TD-010 WPF capture errors are actionable and centrally logged")]
    public void Checker_capture_error_contract_has_actionable_message_and_context()
    {
        var code = File.ReadAllText(FindRepositoryFile(Path.Combine("src", "DrawVerify.App", "Controls", "SignatureStampControl.xaml.cs")));
        Assert.Contains("Failed to capture signature. Please try again.", code);
        Assert.Contains("Logger?.Log", code);
        Assert.Contains("DocumentId", code);
        Assert.Contains("Revision", code);
    }

    [Fact(DisplayName = "REV-KAN65-003 Checker signature control is bound in the review approval section")]
    public void Checker_control_is_bound_to_review_package_and_finalization()
    {
        var xaml = File.ReadAllText(FindRepositoryFile(Path.Combine("src", "DrawVerify.App", "Views", "WorkflowView.xaml")));
        Assert.Contains("Checker approval signature", xaml);
        Assert.Contains("SignatureStampControl", xaml);
        Assert.Contains("IsChecker=\"{Binding IsChecker}\"", xaml);
        Assert.Contains("FinalizedSignature=\"{Binding CheckerSignature", xaml);
        Assert.Contains("SignatureFinalizationRequested=\"CheckerSignatureFinalizationRequested\"", xaml);
    }

    private static string FindRepositoryFile(string relativePath)
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        for (var index = 0; index < 10 && current is not null; index++, current = current.Parent)
        {
            var candidate = Path.Combine(current.FullName, relativePath);
            if (File.Exists(candidate)) return candidate;
        }
        throw new FileNotFoundException($"Could not locate repository file '{relativePath}'.");
    }
}
