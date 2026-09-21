using System.IO.Compression;
using System.Text;
using DrawVerify.Core.Models;
using DrawVerify.Infrastructure.Pdf;
using Xunit;

namespace DrawVerify.Infrastructure.Tests;

public sealed class PdfToWordConverterTests
{
    [Fact(DisplayName = "TSD-03 supported PDF produces editable DOCX with ordered text, layout, and image part")]
    public async Task Supported_pdf_produces_valid_docx()
    {
        var result = await new PdfToWordConverter().ConvertAsync(new ReportTemplateSource { FileName = "fixture.pdf", PdfBytes = Pdf("First", "Second", true) });
        using var stream = new MemoryStream(result.Content); using var archive = new ZipArchive(stream, ZipArchiveMode.Read); var document = new StreamReader(archive.GetEntry("word/document.xml")!.Open()).ReadToEnd();
        Assert.NotNull(archive.GetEntry("[Content_Types].xml")); Assert.True(document.IndexOf("First", StringComparison.Ordinal) < document.IndexOf("Second", StringComparison.Ordinal)); Assert.Contains("Pages: 1", document); Assert.NotEmpty(archive.Entries.Where(x => x.FullName.StartsWith("word/media/", StringComparison.Ordinal)));
    }

    [Theory(DisplayName = "TSD-04 malformed or unreadable PDF fails without DOCX output"), InlineData(new byte[] { 1, 2, 3 }), InlineData(new byte[] { 37, 80, 68, 70, 45, 1 })]
    public async Task Invalid_pdf_fails_clearly(byte[] bytes)
    {
        var exception = await Assert.ThrowsAsync<InvalidDataException>(() => new PdfToWordConverter().ConvertAsync(new ReportTemplateSource { FileName = "bad.pdf", PdfBytes = bytes }));
        Assert.False(string.IsNullOrWhiteSpace(exception.Message));
    }

    private static byte[] Pdf(string first, string second, bool image)
    {
        var objects = new[]
        {
            "<</Type /Catalog /Pages 2 0 R>>",
            "<</Type /Pages /Count 1 /Kids [3 0 R]>>",
            "<</Type /Page /Parent 2 0 R /MediaBox [0 0 600 800] /Resources << /XObject << /Im1 5 0 R >> >> /Contents 4 0 R>>",
            "<</Length 23>>stream\n(First) Tj\n(Second) Tj\nendstream",
            "<</Type /XObject /Subtype /Image /Width 1 /Height 1 /ColorSpace /DeviceRGB /BitsPerComponent 8 /Length 3>>stream\nabc\nendstream"
        };
        var output = new StringBuilder("%PDF-1.4\n"); var offsets = new List<int> { 0 };
        for (var i = 0; i < objects.Length; i++) { offsets.Add(Encoding.ASCII.GetByteCount(output.ToString())); output.Append(i + 1).Append(" 0 obj\n").Append(objects[i]).Append("\nendobj\n"); }
        var startXref = Encoding.ASCII.GetByteCount(output.ToString()); output.Append("xref\n0 6\n0000000000 65535 f \n"); foreach (var offset in offsets.Skip(1)) output.Append(offset.ToString("D10")).Append(" 00000 n \n"); output.Append("trailer\n<</Size 6 /Root 1 0 R>>\nstartxref\n").Append(startXref).Append("\n%%EOF\n");
        return Encoding.ASCII.GetBytes(output.ToString());
    }
}
