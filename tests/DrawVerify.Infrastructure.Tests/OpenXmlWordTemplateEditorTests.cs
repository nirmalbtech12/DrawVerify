using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using DrawVerify.Core.Models;
using DrawVerify.Infrastructure.Word;
using Xunit;

namespace DrawVerify.Infrastructure.Tests;

public sealed class OpenXmlWordTemplateEditorTests
{
    [Fact(DisplayName = "TSD-01 valid and legacy DOCX documents open editable")]
    public async Task Valid_and_legacy_documents_open()
    {
        var editor = new OpenXmlWordTemplateEditor();
        var valid = await editor.OpenAsync(CreateDocx("Hello"));
        var legacy = await editor.OpenAsync(CreateDocx("Legacy"));
        Assert.True(valid.IsEditable);
        Assert.True(valid.PlaceholderToolsEnabled);
        Assert.Empty(legacy.Placeholders);
    }

    [Fact(DisplayName = "TSD-01 empty and malformed DOCX fail visibly")]
    public async Task Invalid_documents_fail()
    {
        var editor = new OpenXmlWordTemplateEditor();
        await Assert.ThrowsAsync<InvalidOperationException>(() => editor.OpenAsync([]));
        await Assert.ThrowsAnyAsync<Exception>(() => editor.OpenAsync([1, 2, 3]));
    }

    [Fact(DisplayName = "TSD-02 and TSD-03 placeholders survive DOCX round trip")]
    public async Task Placeholders_round_trip()
    {
        var editor = new OpenXmlWordTemplateEditor();
        var document = await editor.OpenAsync(CreateDocx("Body"));
        foreach (var role in new[] { "Maker", "Checker", "Approver", "Custom Role" })
            document = await editor.InsertPlaceholderAsync(document, new TemplatePlaceholder { Type = TemplatePlaceholderType.Signature, Role = role, Label = $"{role} Signature", IsRequired = true });
        foreach (var type in new[] { TemplatePlaceholderType.Date, TemplatePlaceholderType.DocumentNumber, TemplatePlaceholderType.Revision, TemplatePlaceholderType.ProjectName, TemplatePlaceholderType.CustomText })
            document = await editor.InsertPlaceholderAsync(document, new TemplatePlaceholder { Type = type, Label = type == TemplatePlaceholderType.CustomText ? "Custom text" : type.ToString() });
        var bytes = await editor.SaveAsync(document);
        var reopened = await editor.OpenAsync(bytes);
        Assert.Equal(document.Placeholders, reopened.Placeholders);
    }

    [Fact(DisplayName = "TSD-02 and TSD-03 invalid placeholder metadata fails without conversion")]
    public async Task Invalid_placeholder_metadata_is_rejected()
    {
        var editor = new OpenXmlWordTemplateEditor();
        var document = await editor.OpenAsync(CreateDocx("Body"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => editor.InsertPlaceholderAsync(document, new TemplatePlaceholder { Type = TemplatePlaceholderType.Signature, Label = "Missing role" }));
        await Assert.ThrowsAsync<InvalidOperationException>(() => editor.InsertPlaceholderAsync(document, new TemplatePlaceholder { Type = TemplatePlaceholderType.CustomText, Label = "" }));
    }

    private static byte[] CreateDocx(string text)
    {
        using var stream = new MemoryStream();
        using (var package = WordprocessingDocument.Create(stream, WordprocessingDocumentType.Document, true))
        {
            var main = package.AddMainDocumentPart();
            main.Document = new DocumentFormat.OpenXml.Wordprocessing.Document(new Body(new Paragraph(new Run(new Text(text)))));
            main.Document.Save();
        }
        return stream.ToArray();
    }
}
