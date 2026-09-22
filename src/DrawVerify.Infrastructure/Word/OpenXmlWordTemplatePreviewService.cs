using System.Text;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using DrawVerify.Core.Interfaces;
using DrawVerify.Core.Models;

namespace DrawVerify.Infrastructure.Word;

public sealed class OpenXmlWordTemplatePreviewService : IWordTemplatePreviewService
{
    public Task<TemplatePreviewData> PreviewAsync(TemplateEditorDocument document, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (document is null || document.DocxBytes.Length == 0) throw new InvalidOperationException("A loaded Word template is required for preview.");
        DrawVerify.Application.Services.ReportTemplatePlaceholderValidator.Validate(document.Placeholders);
        using var input = new MemoryStream(document.DocxBytes.ToArray());
        using var package = WordprocessingDocument.Open(input, false);
        var body = package.MainDocumentPart?.Document?.Body ?? throw new InvalidOperationException("The Word template has no document body.");
        var samples = document.Placeholders.ToDictionary(x => x.Id, Sample);
        foreach (var control in body.Descendants<SdtBlock>().ToArray())
        {
            var label = control.SdtProperties?.GetFirstChild<SdtAlias>()?.Val?.Value;
            if (label is null) continue;
            var placeholder = document.Placeholders.FirstOrDefault(x => x.Label == label);
            if (placeholder is null) continue;
            var text = control.Descendants<Text>().FirstOrDefault();
            if (text is not null) text.Text = samples[placeholder.Id];
        }
        using var output = new MemoryStream();
        package.Clone(output);
        return Task.FromResult(new TemplatePreviewData { DocxBytes = output.ToArray(), Samples = samples });
    }

    private static string Sample(TemplatePlaceholder placeholder) => placeholder.Type switch
    {
        TemplatePlaceholderType.Signature => $"Sample {placeholder.Role} Signature / Stamp",
        TemplatePlaceholderType.Date => "2026-01-15",
        TemplatePlaceholderType.DocumentNumber => "DOC-0001",
        TemplatePlaceholderType.Revision => "Rev A",
        TemplatePlaceholderType.ProjectName => "Sample Project",
        TemplatePlaceholderType.CustomText => $"Sample {placeholder.Label}",
        _ => throw new InvalidOperationException($"Unsupported placeholder type '{placeholder.Type}'.")
    };
}
