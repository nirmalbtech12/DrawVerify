using Xunit;

namespace DrawVerify.App.Tests;

public sealed class InternalApprovalStampControlTests
{
    [Fact(DisplayName = "T13 optional branding retains required approval metadata")]
    public void T13_control_contract_keeps_branding_optional()
    {
        var model = Read("src", "DrawVerify.Core", "Models", "InternalApprovalStamp.cs");
        var xaml = Read("src", "DrawVerify.App", "Controls", "SignatureStampControl.xaml");
        Assert.Contains("OrganizationName", model);
        Assert.Contains("Internally Approved", model);
        Assert.Contains("IsEnabled=\"{Binding CanEdit", xaml);
    }

    [Fact(DisplayName = "T14 stamp placement uses a designated approval area")]
    public void T14_stamp_has_designated_placement_metadata()
    {
        var model = Read("src", "DrawVerify.Core", "Models", "InternalApprovalStamp.cs");
        Assert.Contains("Placement", model);
        Assert.Contains("Approval", model);
    }

    private static string Read(params string[] parts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null) { var path = Path.Combine(new[] { directory.FullName }.Concat(parts).ToArray()); if (File.Exists(path)) return File.ReadAllText(path); directory = directory.Parent; }
        throw new FileNotFoundException(string.Join(Path.DirectorySeparatorChar, parts));
    }
}
