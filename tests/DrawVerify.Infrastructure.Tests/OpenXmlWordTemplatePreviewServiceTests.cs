using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using DrawVerify.Core.Models;
using DrawVerify.Infrastructure.Word;
using Xunit;

namespace DrawVerify.Infrastructure.Tests;

public sealed class OpenXmlWordTemplatePreviewServiceTests
{
    [Fact(DisplayName = "TSD-06 preview renders deterministic samples without changing input")]
    public async Task Preview_contains_samples_and_preserves_saved_bytes()
    {
        var editor = new OpenXmlWordTemplateEditor();
        var document = await editor.OpenAsync(CreateDocx());
        document = await editor.InsertPlaceholderAsync(document, new TemplatePlaceholder { Type = TemplatePlaceholderType.Signature, Role = "Maker", Label = "Maker Signature" });
        document = await editor.InsertPlaceholderAsync(document, new TemplatePlaceholder { Type = TemplatePlaceholderType.Date, Label = "Date" });
        var saved = await editor.SaveAsync(document);
        var baseline = document.DocxBytes.ToArray();
        var preview = await new OpenXmlWordTemplatePreviewService().PreviewAsync(document with { DocxBytes = saved });
        Assert.NotEmpty(preview.DocxBytes);
        Assert.Contains("Sample Maker Signature / Stamp", preview.Samples.Values);
        Assert.Contains("2026-01-15", preview.Samples.Values);
        Assert.Equal(baseline, document.DocxBytes);
    }

    [Fact(DisplayName = "TSD-06 invalid preview input fails visibly")]
    public async Task Invalid_preview_fails()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() => new OpenXmlWordTemplatePreviewService().PreviewAsync(new TemplateEditorDocument()));
    }

    private static byte[] CreateDocx()
    {
        using var stream = new MemoryStream();
        using (var package = WordprocessingDocument.Create(stream, WordprocessingDocumentType.Document, true))
        {
            var main = package.AddMainDocumentPart();
            main.Document = new DocumentFormat.OpenXml.Wordprocessing.Document(new Body(new Paragraph(new Run(new Text("Template")))));
            main.Document.Save();
        }
        return stream.ToArray();
    }
}
