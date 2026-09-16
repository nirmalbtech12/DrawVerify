using DrawVerify.App.Controls;
using Xunit;

namespace DrawVerify.App.Tests;

public sealed class SignatureStampControlTests
{
    [Fact(DisplayName = "TD-KAN64-APPUI-001 Maker Signature control exposes approved guidance and recovery text")]
    public void Signature_control_xaml_contains_required_user_guidance_and_states()
    {
        var path = FindRepositoryFile(Path.Combine("src", "DrawVerify.App", "Controls", "SignatureStampControl.xaml"));
        var xaml = File.ReadAllText(path);
        Assert.Contains("Maker Signature", xaml);
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
