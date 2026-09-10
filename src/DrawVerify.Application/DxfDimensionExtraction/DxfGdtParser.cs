using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using DrawVerify.Core.Enums;
using DrawVerify.Geometry.Models;

namespace DrawVerify.Application.DxfDimensionExtraction;

/// <summary>Extracts recognizable feature-control-frame text without changing DIMENSION extraction.</summary>
public sealed class DxfGdtParser : IDxfGdtParser
{
    private static readonly Regex Supported = new("(?<symbol>⌖|⏥|∥|⊥|POSITION|FLATNESS|PARALLELISM|PERPENDICULARITY)\\s*(?<tol>[^;|]*)(?:\\|(?<datum>[A-Z](?:\\s*[A-Z])*))?", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex UnsupportedModifier = new("\\b(COMPOSITE|UNSUPPORTED|MMC|RFS|LMC|PROJECTED|TANGENT|FREE[\\s_-]*STATE)\\b", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public async Task<IReadOnlyList<GdtAnnotationData>> ParseAsync(Stream content, Guid documentId, Guid documentVersionId, CancellationToken cancellationToken = default)
        => (await ParseDetailedAsync(content, documentId, documentVersionId, cancellationToken).ConfigureAwait(false)).Annotations;

    public async Task<DxfGdtExtractionResult> ParseDetailedAsync(Stream content, Guid documentId, Guid documentVersionId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);
        using var reader = new StreamReader(content, Encoding.UTF8, true, 4096, leaveOpen: true);
        var results = new List<GdtAnnotationData>();
        var entity = new Dictionary<int, string>();
        var entityName = string.Empty;
        var ordinal = 0;
        var diagnostics = new List<string>();
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var codeLine = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
            if (codeLine is null) break;
            var value = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
            if (value is null) break;
            if (!int.TryParse(codeLine.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var code)) continue;
            if (code == 0)
            {
                Flush(entityName, entity, documentId, documentVersionId, ++ordinal, results, diagnostics);
                entity.Clear(); entityName = value.Trim();
            }
            else if (code is 1 or 2 or 3 or 5 or 8 or 10 or 20 or 303 or 304 or 310 or 410 or 1000)
                entity[code] = value.Trim();
        }
        Flush(entityName, entity, documentId, documentVersionId, ++ordinal, results, diagnostics);
        return DxfGdtExtractionResult.Success(results, diagnostics);
    }

    private static void Flush(string entityName, Dictionary<int, string> values, Guid documentId, Guid versionId, int ordinal, ICollection<GdtAnnotationData> results, ICollection<string>? diagnostics = null)
    {
        if (entityName is not ("TEXT" or "MTEXT" or "LEADER" or "MULTILEADER" or "ATTDEF" or "INSERT")) return;
        var raw = new[] { 1, 3, 303, 304, 1000 }
            .Where(values.ContainsKey)
            .Select(code => values[code])
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
        if (string.IsNullOrWhiteSpace(raw)) return;
        var match = Supported.Match(raw);
        var symbol = match.Success ? match.Groups["symbol"].Value : null;
        var type = symbol?.ToUpperInvariant() switch
        {
            "⌖" or "POSITION" => GdtControlType.Position,
            "⏥" or "FLATNESS" => GdtControlType.Flatness,
            "∥" or "PARALLELISM" => GdtControlType.Parallelism,
            "⊥" or "PERPENDICULARITY" => GdtControlType.Perpendicularity,
            _ => GdtControlType.Unknown
        };
        var datums = match.Success && match.Groups["datum"].Success
            ? match.Groups["datum"].Value.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            : Array.Empty<string>();
        var location = values.TryGetValue(10, out var x) && values.TryGetValue(20, out var y) ? $"{x},{y}" : $"entity:{ordinal}";
        var entityHandle = values.TryGetValue(5, out var handle)
            ? handle
            : ordinal.ToString(CultureInfo.InvariantCulture);
        var layer = values.TryGetValue(8, out var layerValue) ? layerValue : null;
        var block = values.TryGetValue(2, out var blockValue) ? blockValue : null;
        var view = values.TryGetValue(410, out var viewValue) ? viewValue : null;
        var annotationDiagnostics = new List<string>();
        if (!match.Success) annotationDiagnostics.Add("GD&T symbol or tolerance was not recognized.");
        if (match.Success && string.IsNullOrWhiteSpace(match.Groups["tol"].Value)) annotationDiagnostics.Add("GD&T tolerance was not extracted.");
        var tolerance = match.Success ? match.Groups["tol"].Value.Trim() : null;
        var unsupportedModifier = match.Success && UnsupportedModifier.IsMatch(tolerance ?? string.Empty);
        var modifiers = unsupportedModifier ? tolerance : null;
        var hasCompositeFrame = unsupportedModifier && tolerance!.Contains("COMPOSITE", StringComparison.OrdinalIgnoreCase);
        if (unsupportedModifier) annotationDiagnostics.Add("GD&T modifier or composite frame is unsupported.");
        if (diagnostics is not null)
            foreach (var message in annotationDiagnostics)
                diagnostics.Add($"Entity {ordinal}: {message}");
        results.Add(new GdtAnnotationData($"DXF:{documentId:N}:{versionId:N}:{entityHandle}", type, symbol, tolerance, modifiers, datums, $"DXF:{documentId:N}", location, raw, ordinal, entityHandle, layer, block, view, annotationDiagnostics, HasCompositeFrame: hasCompositeFrame, SourceDocumentVersion: versionId.ToString(), UnsupportedSyntax: unsupportedModifier));
    }
}
