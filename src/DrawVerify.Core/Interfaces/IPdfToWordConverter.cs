using DrawVerify.Core.Models;

namespace DrawVerify.Core.Interfaces;

public interface IPdfToWordConverter
{
    Task<PdfToWordConversionResult> ConvertAsync(ReportTemplateSource source, CancellationToken cancellationToken = default);
    Task<PdfToWordConversionResult> ConvertPdfAsync(ReportTemplateSource source, CancellationToken cancellationToken = default) => ConvertAsync(source, cancellationToken);
}
