using System.Text.Json;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using DrawVerify.Core.Interfaces;
using DrawVerify.Core.Models;

namespace DrawVerify.Infrastructure.Word;

public sealed class OpenXmlWordTemplateEditor : IWordTemplateEditor
{
    private const string TagPrefix = "DrawVerifyPlaceholder:";

    public Task<TemplateEditorDocument> OpenAsync(byte[] docxBytes, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (docxBytes is null || docxBytes.Length == 0) throw new InvalidOperationException("A non-empty Word template is required.");
        using var stream = new MemoryStream(docxBytes, writable: false);
        using var package = WordprocessingDocument.Open(stream, false);
        var body = package.MainDocumentPart?.Document?.Body ?? throw new InvalidOperationException("The Word template has no document body.");
        var placeholders = body.Descendants<SdtBlock>().Select(Read).Where(x => x is not null).Cast<TemplatePlaceholder>().ToArray();
        return Task.FromResult(new TemplateEditorDocument { DocxBytes = docxBytes.ToArray(), Placeholders = placeholders });
    }

    public Task<TemplateEditorDocument> InsertPlaceholderAsync(TemplateEditorDocument document, TemplatePlaceholder placeholder, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (document is null) throw new ArgumentNullException(nameof(document));
        placeholder.Validate();
        var placeholders = document.Placeholders.Concat([placeholder]).ToArray();
        DrawVerify.Application.Services.ReportTemplatePlaceholderValidator.Validate(placeholders);
        return Task.FromResult(document with { Placeholders = placeholders });
    }

    public Task<byte[]> SaveAsync(TemplateEditorDocument document, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        DrawVerify.Application.Services.ReportTemplatePlaceholderValidator.Validate(document.Placeholders);
        // A stream created over an existing byte array is fixed-capacity. OpenXML may
        // need to grow the ZIP package when placeholder controls are added, so copy
        // the source bytes into an expandable writable stream first.
        using var stream = new MemoryStream();
        stream.Write(document.DocxBytes, 0, document.DocxBytes.Length);
        stream.Position = 0;
        using (var package = WordprocessingDocument.Open(stream, true))
        {
            var body = package.MainDocumentPart?.Document?.Body ?? throw new InvalidOperationException("The Word template has no document body.");
            foreach (var existing in body.Descendants<SdtBlock>().Where(x => x.SdtProperties?.GetFirstChild<Tag>()?.Val?.Value?.StartsWith(TagPrefix, StringComparison.Ordinal) == true).ToArray()) existing.Remove();
            foreach (var placeholder in document.Placeholders)
            {
                var tag = TagPrefix + Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(placeholder));
                var block = new SdtBlock(new SdtProperties(new Tag { Val = tag }, new SdtAlias { Val = placeholder.Label }), new SdtContentBlock(new Paragraph(new Run(new Text(placeholder.Label)))));
                body.Append(block);
            }
            package.MainDocumentPart!.Document.Save();
        }
        return Task.FromResult(stream.ToArray());
    }

    private static TemplatePlaceholder? Read(SdtBlock block)
    {
        var value = block.SdtProperties?.GetFirstChild<Tag>()?.Val?.Value;
        if (value is null || !value.StartsWith(TagPrefix, StringComparison.Ordinal)) return null;
        try { return JsonSerializer.Deserialize<TemplatePlaceholder>(Convert.FromBase64String(value[TagPrefix.Length..])); }
        catch (FormatException ex) { throw new InvalidOperationException("The Word template contains invalid placeholder metadata.", ex); }
        catch (JsonException ex) { throw new InvalidOperationException("The Word template contains invalid placeholder metadata.", ex); }
    }
}
