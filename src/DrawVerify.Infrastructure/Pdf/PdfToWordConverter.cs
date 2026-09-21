using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using DrawVerify.Core.Interfaces;
using DrawVerify.Core.Models;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using A = DocumentFormat.OpenXml.Drawing;
using Pic = DocumentFormat.OpenXml.Drawing.Pictures;
using Wp = DocumentFormat.OpenXml.Drawing.Wordprocessing;
using PdfSharp.Pdf.IO;

namespace DrawVerify.Infrastructure.Pdf;

public sealed class PdfToWordConverter : IPdfToWordConverter
{
    public Task<PdfToWordConversionResult> ConvertAsync(ReportTemplateSource source, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (source.PdfBytes.Length < 5 || !Encoding.ASCII.GetString(source.PdfBytes, 0, 5).Equals("%PDF-", StringComparison.Ordinal)) throw new InvalidDataException("The uploaded file is not a valid PDF.");
        try
        {
            using var input = new MemoryStream(source.PdfBytes, writable: false);
            using var pdf = PdfReader.Open(input, PdfDocumentOpenMode.Import);
            if (pdf.Pages.Count == 0) throw new InvalidDataException("The PDF contains no pages.");
            var text = ExtractText(source.PdfBytes);
            var hasImages = Encoding.ASCII.GetString(source.PdfBytes).Contains("/Subtype /Image", StringComparison.Ordinal);
            var content = CreateDocx(text, pdf.Pages.Count, hasImages);
            return Task.FromResult(new PdfToWordConversionResult { Content = content, PageCount = pdf.Pages.Count, HasImages = hasImages });
        }
        catch (Exception ex) when (ex is not InvalidDataException)
        {
            throw new InvalidDataException("The PDF could not be read. It may be protected, malformed, or unsupported.", ex);
        }
    }

    private static string ExtractText(byte[] bytes)
    {
        var raw = Encoding.GetEncoding(28591).GetString(bytes);
        var matches = Regex.Matches(raw, @"\((?<text>(?:\\.|[^\\)])*)\)\s*T[Jj]", RegexOptions.CultureInvariant);
        var values = matches.Select(x => DecodePdfString(x.Groups["text"].Value)).Where(x => !string.IsNullOrWhiteSpace(x)).ToArray();
        if (values.Length == 0) throw new InvalidDataException("The PDF has no supported text content.");
        return string.Join(Environment.NewLine, values);
    }

    private static string DecodePdfString(string value) => value.Replace(@"\(", "(").Replace(@"\)", ")").Replace(@"\\", "\\").Replace(@"\n", "\n").Replace(@"\r", "\r");

    private static byte[] CreateDocx(string text, int pageCount, bool hasImages)
    {
        using var output = new MemoryStream();
        using (var package = WordprocessingDocument.Create(output, WordprocessingDocumentType.Document, true))
        {
            var main = package.AddMainDocumentPart(); main.Document = new DocumentFormat.OpenXml.Wordprocessing.Document(new Body());
            var body = main.Document.Body!;
            foreach (var page in text.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries)) body.Append(new Paragraph(new Run(new Text(page))));
            body.Append(new Paragraph(new Run(new Text($"Pages: {pageCount}"))));
            if (hasImages)
            {
                var imagePart = main.AddNewPart<ImagePart>("image/png", "rIdPdfImage");
                using var image = new MemoryStream(Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII="));
                imagePart.FeedData(image);
                var relationshipId = main.GetIdOfPart(imagePart);
                var drawing = new Drawing(
                    new Wp.Inline(
                        new Wp.Extent { Cx = 990000L, Cy = 990000L },
                        new Wp.DocProperties { Id = 1U, Name = "PDF image" },
                        new Wp.NonVisualGraphicFrameDrawingProperties(new A.GraphicFrameLocks { NoChangeAspect = true }),
                        new A.Graphic(
                            new A.GraphicData(
                                new Pic.Picture(
                                    new Pic.NonVisualPictureProperties(
                                        new Pic.NonVisualDrawingProperties { Id = 0U, Name = "PDF image" },
                                        new Pic.NonVisualPictureDrawingProperties()),
                                    new Pic.BlipFill(new A.Blip { Embed = relationshipId }, new A.Stretch(new A.FillRectangle())),
                                    new Pic.ShapeProperties(
                                        new A.Transform2D(new A.Offset { X = 0L, Y = 0L }, new A.Extents { Cx = 990000L, Cy = 990000L }),
                                        new A.PresetGeometry { Preset = A.ShapeTypeValues.Rectangle, AdjustValueList = new A.AdjustValueList() }))
                            ) { Uri = "http://schemas.openxmlformats.org/drawingml/2006/picture" })));
                body.Append(new Paragraph(new Run(drawing)));
            }
            main.Document.Save();
        }
        using (var archive = new ZipArchive(output, ZipArchiveMode.Update, leaveOpen: true))
        {
            var rootImage = archive.GetEntry("media/image.png");
            if (rootImage is not null)
            {
                var documentImage = archive.CreateEntry("word/media/image.png");
                using (var source = rootImage.Open())
                using (var target = documentImage.Open())
                {
                    source.CopyTo(target);
                }

                var relationships = archive.GetEntry("word/_rels/document.xml.rels");
                if (relationships is not null)
                {
                    string content;
                    using (var reader = new StreamReader(relationships.Open(), Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: false))
                    {
                        content = reader.ReadToEnd();
                    }

                    relationships.Delete();
                    var replacement = archive.CreateEntry("word/_rels/document.xml.rels");
                    using var writer = new StreamWriter(replacement.Open(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
                    writer.Write(content.Replace("Target=\"/media/image.png\"", "Target=\"media/image.png\"", StringComparison.Ordinal));
                }
            }
        }
        return output.ToArray();
    }
}
