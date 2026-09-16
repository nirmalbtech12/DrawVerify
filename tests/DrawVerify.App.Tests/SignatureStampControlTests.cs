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
        Assert.Contains("Type your name", xaml, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("draw", xaml, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("upload", xaml, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Preview", xaml);
        Assert.Contains("Cancel / Re-enter", xaml);
        Assert.Contains("Maker signature is required before submission", xaml, StringComparison.OrdinalIgnoreCase);
    }

    [Fact(DisplayName = "TD-KAN64-APPUI-001 control has a discoverable preview state")]
    public void Signature_control_declares_preview_state()
    {
        Assert.True(typeof(SignatureStampControl).GetProperty(nameof(SignatureStampControl.IsPreviewVisible))?.CanRead);
        var codePath = FindRepositoryFile(Path.Combine("src", "DrawVerify.App", "Controls", "SignatureStampControl.xaml.cs"));
        var code = File.ReadAllText(codePath);
        Assert.Contains("Failed to capture signature. Please try again.", code);
        Assert.Contains("Preview_Click", code);
        Assert.Contains("Cancel_Click", code);
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
