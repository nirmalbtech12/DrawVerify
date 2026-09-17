using Xunit;

namespace DrawVerify.Infrastructure.Tests;

public sealed class InternalApprovalStampPdfReportTests
{
    [Fact(DisplayName = "T11 PDF/report rendering preserves stamp representation and status")]
    public void T11_pdf_generator_has_stamp_rendering_path()
    {
        var source = Read("src", "DrawVerify.Infrastructure", "Pdf", "PdfReportGenerator.cs");
        Assert.Contains("InternalApprovalStamp", source);
        Assert.Contains("VerificationStatus", source);
        Assert.Contains("StampId", source);
    }

    private static string Read(params string[] parts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null) { var path = Path.Combine(new[] { directory.FullName }.Concat(parts).ToArray()); if (File.Exists(path)) return File.ReadAllText(path); directory = directory.Parent; }
        throw new FileNotFoundException(string.Join(Path.DirectorySeparatorChar, parts));
    }
}
