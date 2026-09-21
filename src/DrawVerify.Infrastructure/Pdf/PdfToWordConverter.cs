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
            var images = ExtractImages(source.PdfBytes);
            var content = CreateDocx(text, pdf.Pages.Count, images);
            return Task.FromResult(new PdfToWordConversionResult { Content = content, PageCount = pdf.Pages.Count, HasImages = images.Count > 0 });
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

    private static IReadOnlyList<PdfImage> ExtractImages(byte[] bytes)
    {
        var raw = Encoding.Latin1.GetString(bytes);
        var images = new List<PdfImage>();
        foreach (Match match in Regex.Matches(raw, @"(?s)<<(?<dictionary>.*?/Subtype\s*/Image\b.*?)>>\s*stream\s*(?<data>.*?)\s*endstream", RegexOptions.CultureInvariant))
        {
            var dictionary = match.Groups["dictionary"].Value;
            var dataStart = match.Groups["data"].Index;
            var dataLength = match.Groups["data"].Length;
            var imageBytes = bytes.AsSpan(dataStart, dataLength).ToArray();
            var filter = Regex.Match(dictionary, @"/Filter\s*/(?<filter>[A-Za-z0-9]+)", RegexOptions.CultureInvariant).Groups["filter"].Value;
            var width = ReadPositiveInt(dictionary, "Width");
            var height = ReadPositiveInt(dictionary, "Height");
            var bits = ReadPositiveInt(dictionary, "BitsPerComponent");
            var colorSpace = Regex.Match(dictionary, @"/ColorSpace\s*/(?<space>[A-Za-z0-9]+)", RegexOptions.CultureInvariant).Groups["space"].Value;

            if (string.Equals(filter, "DCTDecode", StringComparison.Ordinal))
            {
                images.Add(new PdfImage(imageBytes, IsJpeg: true));
                continue;
            }

            if (!string.IsNullOrEmpty(filter) && !string.Equals(filter, "FlateDecode", StringComparison.Ordinal))
                throw new InvalidDataException($"The PDF image encoding '{filter}' is not supported.");
            if (bits != 8 || (colorSpace is not ("DeviceRGB" or "DeviceGray")))
                throw new InvalidDataException("The PDF image color space or bit depth is not supported.");
            if (string.Equals(filter, "FlateDecode", StringComparison.Ordinal))
            {
                using var compressed = new MemoryStream(imageBytes, writable: false);
                using var decompressor = new DeflateStream(compressed, CompressionMode.Decompress);
                using var decoded = new MemoryStream();
                decompressor.CopyTo(decoded);
                imageBytes = decoded.ToArray();
            }
            var channels = colorSpace == "DeviceRGB" ? 3 : 1;
            if (imageBytes.Length != width * height * channels)
                throw new InvalidDataException("The PDF image data length does not match its dimensions.");
            images.Add(new PdfImage(CreatePng(imageBytes, width, height, channels), IsJpeg: false));
        }
        return images;
    }

    private static int ReadPositiveInt(string dictionary, string name)
    {
        var match = Regex.Match(dictionary, $@"/{name}\s+(?<value>\d+)", RegexOptions.CultureInvariant);
        return match.Success && int.TryParse(match.Groups["value"].Value, out var value) && value > 0
            ? value
            : throw new InvalidDataException($"The PDF image is missing a valid {name}.");
    }

    private static byte[] CreatePng(byte[] pixels, int width, int height, int channels)
    {
        using var output = new MemoryStream();
        output.Write([137, 80, 78, 71, 13, 10, 26, 10]);
        var header = new byte[13];
        WriteBigEndian(header, 0, width); WriteBigEndian(header, 4, height); header[8] = 8; header[9] = channels == 3 ? (byte)2 : (byte)0;
        WritePngChunk(output, "IHDR", header);
        var scanlines = new byte[height * (width * channels + 1)];
        for (var row = 0; row < height; row++) { Buffer.BlockCopy(pixels, row * width * channels, scanlines, row * (width * channels + 1) + 1, width * channels); }
        using (var compressed = new MemoryStream())
        {
            using (var compressor = new ZLibStream(compressed, CompressionMode.Compress, leaveOpen: true)) compressor.Write(scanlines);
            WritePngChunk(output, "IDAT", compressed.ToArray());
        }
        WritePngChunk(output, "IEND", []);
        return output.ToArray();
    }

    private static void WriteBigEndian(byte[] buffer, int offset, int value)
    {
        buffer[offset] = (byte)(value >> 24); buffer[offset + 1] = (byte)(value >> 16); buffer[offset + 2] = (byte)(value >> 8); buffer[offset + 3] = (byte)value;
    }

    private static void WritePngChunk(Stream output, string name, byte[] data)
    {
        var type = Encoding.ASCII.GetBytes(name); var length = new byte[4]; WriteBigEndian(length, 0, data.Length); output.Write(length); output.Write(type); output.Write(data);
        var checksum = new byte[4]; WriteBigEndian(checksum, 0, unchecked((int)Crc32(type, data))); output.Write(checksum);
    }

    private static uint Crc32(byte[] type, byte[] data)
    {
        uint crc = 0xFFFFFFFF;
        foreach (var value in type.Concat(data))
        {
            crc ^= value;
            for (var bit = 0; bit < 8; bit++) crc = (crc >> 1) ^ (0xEDB88320u & (uint)-(int)(crc & 1));
        }
        return ~crc;
    }

    private sealed record PdfImage(byte[] Content, bool IsJpeg);

    private static byte[] CreateDocx(string text, int pageCount, IReadOnlyList<PdfImage> images)
    {
        using var output = new MemoryStream();
        using (var package = WordprocessingDocument.Create(output, WordprocessingDocumentType.Document, true))
        {
            var main = package.AddMainDocumentPart(); main.Document = new DocumentFormat.OpenXml.Wordprocessing.Document(new Body());
            var body = main.Document.Body!;
            foreach (var page in text.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries)) body.Append(new Paragraph(new Run(new Text(page))));
            body.Append(new Paragraph(new Run(new Text($"Pages: {pageCount}"))));
            foreach (var pdfImage in images)
            {
                var imagePart = main.AddImagePart(pdfImage.IsJpeg ? ImagePartType.Jpeg : ImagePartType.Png);
                using var image = new MemoryStream(pdfImage.Content, writable: false);
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
            foreach (var rootImage in archive.Entries.Where(x => x.FullName.StartsWith("media/", StringComparison.Ordinal)).ToArray())
            {
                var documentImage = archive.CreateEntry($"word/{rootImage.FullName}");
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
                    writer.Write(content.Replace("Target=\"/media/", "Target=\"media/", StringComparison.Ordinal));
                }
            }
        }
        return output.ToArray();
    }
}
