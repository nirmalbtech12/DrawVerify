using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using DrawVerify.Core.Models;
using DrawVerify.Infrastructure.Word;
using Xunit;

namespace DrawVerify.Application.Tests;

public sealed class ReportTemplateEditorPreviewTests
{
    [Fact(DisplayName = "TSD-06 editor preview contains signature and dynamic sample values")]
    public async Task Preview_renders_samples_without_mutating_document()
    {
        var editor = new OpenXmlWordTemplateEditor();
        var document = await editor.OpenAsync(CreateDocx());
        document = await editor.InsertPlaceholderAsync(document, new TemplatePlaceholder { Type = TemplatePlaceholderType.Signature, Role = "Approver", Label = "Approver Signature" });
        document = await editor.InsertPlaceholderAsync(document, new TemplatePlaceholder { Type = TemplatePlaceholderType.ProjectName, Label = "Project" });
        var before = document.DocxBytes.ToArray();
        var preview = await new OpenXmlWordTemplatePreviewService().PreviewAsync(document);
        Assert.NotEmpty(preview.DocxBytes);
        Assert.Contains("Sample Approver Signature / Stamp", preview.Samples.Values);
        Assert.Contains("Sample Project", preview.Samples.Values);
        Assert.Equal(before, document.DocxBytes);
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
