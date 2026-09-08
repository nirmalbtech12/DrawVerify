using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using DrawVerify.Core.Enums;
using DrawVerify.Core.Models;

namespace DrawVerify.Application.DxfDimensionExtraction;

/// <summary>
/// Reads only the ASCII group-code records needed from standard DXF DIMENSION entities.
/// The input stream is consumed sequentially and is owned by the caller.
/// </summary>
public sealed class DxfDimensionParser : IDxfDimensionParser
{
    private static readonly Regex SymmetricTolerance = new(@"(?:±|%%p)\s*([+-]?(?:\d+(?:\.\d*)?|\.\d+)(?:[eE][+-]?\d+)?)", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex DeviationTolerance = new(@"([+-]?(?:\d+(?:\.\d*)?|\.\d+)(?:[eE][+-]?\d+)?)\s*/\s*([+-]?(?:\d+(?:\.\d*)?|\.\d+)(?:[eE][+-]?\d+)?)", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex LimitTolerance = new(@"([+-]?(?:\d+(?:\.\d*)?|\.\d+)(?:[eE][+-]?\d+)?)\s*[-–]\s*([+-]?(?:\d+(?:\.\d*)?|\.\d+)(?:[eE][+-]?\d+)?)", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex StackedTolerance = new(@"\\S\s*([+-]?(?:\d+(?:\.\d*)?|\.\d+)(?:[eE][+-]?\d+)?)\s*\^\s*([+-]?(?:\d+(?:\.\d*)?|\.\d+)(?:[eE][+-]?\d+)?)\s*;", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public async Task<DxfDimensionExtractionResult> ParseAsync(
        Stream content,
        Guid documentId,
        Guid documentVersionId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);
        var warnings = new List<string>();
        var dimensions = new List<DimensionData>();
        var entities = new Dictionary<int, List<string>>();
        var currentEntityName = string.Empty;
        var currentSection = string.Empty;
        var pendingSectionName = false;
        var sourceOrdinal = 0;
        var sawEntitiesSection = false;
        var structurallyTruncated = false;

        using var reader = new StreamReader(content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true), detectEncodingFromByteOrderMarks: true, bufferSize: 4096, leaveOpen: true);
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var codeLine = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
            if (codeLine is null)
                break;

            var valueLine = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
            if (valueLine is null)
            {
                warnings.Add("DXF ended in the middle of a group-code record.");
                structurallyTruncated = true;
                break;
            }

            if (!int.TryParse(codeLine.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var code))
            {
                warnings.Add($"Unreadable DXF group code '{codeLine.Trim()}'.");
                continue;
            }

            var value = valueLine.Trim();
            if (pendingSectionName)
            {
                if (code == 2)
                {
                    currentSection = value;
                    sawEntitiesSection |= string.Equals(currentSection, "ENTITIES", StringComparison.OrdinalIgnoreCase);
                }
                else
                {
                    warnings.Add("DXF SECTION record has no section name.");
                }
                pendingSectionName = false;
                continue;
            }

            if (code == 0)
            {
                FlushEntity(currentEntityName, entities, currentSection, sourceOrdinal, documentId, documentVersionId, dimensions, warnings);
                entities.Clear();
                currentEntityName = value;
                if (string.Equals(value, "SECTION", StringComparison.OrdinalIgnoreCase))
                {
                    currentSection = string.Empty;
                    pendingSectionName = true;
                }
                else if (string.Equals(value, "ENDSEC", StringComparison.OrdinalIgnoreCase))
                {
                    currentSection = string.Empty;
                    currentEntityName = string.Empty;
                }
                else if (string.Equals(value, "EOF", StringComparison.OrdinalIgnoreCase))
                {
                    currentSection = string.Empty;
                    currentEntityName = string.Empty;
                }
                else if (string.Equals(currentSection, "ENTITIES", StringComparison.OrdinalIgnoreCase))
                {
                    sourceOrdinal++;
                }
                continue;
            }

            if (string.Equals(currentSection, "ENTITIES", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(currentEntityName))
            {
                if (!entities.TryGetValue(code, out var values))
                {
                    values = new List<string>();
                    entities[code] = values;
                }
                values.Add(value);
            }
        }

        FlushEntity(currentEntityName, entities, currentSection, sourceOrdinal, documentId, documentVersionId, dimensions, warnings);
        if (pendingSectionName)
            warnings.Add("DXF SECTION record was not completed.");

        if (!sawEntitiesSection)
            return DxfDimensionExtractionResult.Failure("DXF file does not contain a valid ENTITIES section.", warnings);
        if (structurallyTruncated && dimensions.Count == 0)
            return DxfDimensionExtractionResult.Failure("DXF file ended before its entities could be read.", warnings);
        if (dimensions.Count == 0 && warnings.Any(warning => warning.StartsWith("Skipped malformed DIMENSION entity", StringComparison.Ordinal)))
            return DxfDimensionExtractionResult.Failure("DXF file contains no readable dimension entities.", warnings);

        return DxfDimensionExtractionResult.Success(dimensions, warnings);
    }

    private static void FlushEntity(
        string entityName,
        Dictionary<int, List<string>> values,
        string section,
        int sourceOrdinal,
        Guid documentId,
        Guid documentVersionId,
        ICollection<DimensionData> dimensions,
        ICollection<string> warnings)
    {
        if (!string.Equals(section, "ENTITIES", StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(entityName, "DIMENSION", StringComparison.OrdinalIgnoreCase))
            return;

        try
        {
            var flags = FirstInt(values, 70) ?? throw new InvalidDataException("DIMENSION entity has no type flags.");
            var baseType = flags & 0x0F;
            var type = baseType switch
            {
                0 or 1 => DimensionType.Linear,
                2 or 5 => DimensionType.Angular,
                3 or 4 => DimensionType.Radial,
                _ => throw new NotSupportedException($"DIMENSION type {baseType} is not supported.")
            };

            var nominal = FirstDouble(values, 42);
            if (nominal is null)
                throw new InvalidDataException("DIMENSION entity has no readable nominal measurement.");

            var displayText = FirstString(values, 1);
            var (lowerTolerance, upperTolerance) = ReadTolerances(values, displayText, nominal.Value);
            dimensions.Add(new DimensionData(documentId, documentVersionId, sourceOrdinal, type, nominal.Value,
                lowerTolerance, upperTolerance, displayText));
        }
        catch (NotSupportedException exception)
        {
            warnings.Add(exception.Message);
        }
        catch (Exception exception) when (exception is InvalidDataException or FormatException or ArgumentException)
        {
            warnings.Add($"Skipped malformed DIMENSION entity {sourceOrdinal}: {exception.Message}");
        }
    }

    private static (double? Lower, double? Upper) ReadTolerances(Dictionary<int, List<string>> values, string? displayText, double nominal)
    {
        var direct = ReadTolerancePair(values, 47, 48) ?? ReadTolerancePair(values, 43, 44) ?? ReadTolerancePair(values, 1040, 1041);
        if (direct is not null)
            return direct.Value;

        if (!string.IsNullOrWhiteSpace(displayText))
        {
            var symmetric = SymmetricTolerance.Match(displayText);
            if (symmetric.Success && TryDouble(symmetric.Groups[1].Value, out var symmetricValue))
                return (-Math.Abs(symmetricValue), Math.Abs(symmetricValue));

            var deviation = DeviationTolerance.Match(displayText);
            if (deviation.Success && TryDouble(deviation.Groups[1].Value, out var upper) && TryDouble(deviation.Groups[2].Value, out var lower))
                return (Math.Min(lower, upper), Math.Max(lower, upper));

            var stacked = StackedTolerance.Match(displayText);
            if (stacked.Success && TryDouble(stacked.Groups[1].Value, out var stackedUpper) && TryDouble(stacked.Groups[2].Value, out var stackedLower))
                return (Math.Min(stackedLower, stackedUpper), Math.Max(stackedLower, stackedUpper));

            var limits = LimitTolerance.Match(displayText);
            if (limits.Success &&
                TryDecimal(limits.Groups[1].Value, out var lowerLimit) &&
                TryDecimal(limits.Groups[2].Value, out var upperLimit) &&
                TryDecimal(nominal.ToString("R", CultureInfo.InvariantCulture), out var nominalValue) &&
                lowerLimit <= nominalValue && upperLimit >= nominalValue)
            {
                return ((double)(lowerLimit - nominalValue), (double)(upperLimit - nominalValue));
            }
        }

        return (null, null);
    }

    private static (double Lower, double Upper)? ReadTolerancePair(Dictionary<int, List<string>> values, int lowerCode, int upperCode)
    {
        var lower = FirstDouble(values, lowerCode);
        var upper = FirstDouble(values, upperCode);
        if (lower is null || upper is null)
            return null;
        if (lower <= upper)
            return (lower.Value, upper.Value);
        return (upper.Value, lower.Value);
    }

    private static string? FirstString(Dictionary<int, List<string>> values, int code) =>
        values.TryGetValue(code, out var list) && list.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) is { } value ? value : null;

    private static int? FirstInt(Dictionary<int, List<string>> values, int code) =>
        values.TryGetValue(code, out var list) && list.Select(value => int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : (int?)null).FirstOrDefault() is { } value ? value : null;

    private static double? FirstDouble(Dictionary<int, List<string>> values, int code) =>
        values.TryGetValue(code, out var list) && list.Select(value => TryDouble(value, out var parsed) ? parsed : (double?)null).FirstOrDefault() is { } value ? value : null;

    private static bool TryDouble(string value, out double result) =>
        double.TryParse(value, NumberStyles.Float | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out result) && double.IsFinite(result);

    private static bool TryDecimal(string value, out decimal result) =>
        decimal.TryParse(value, NumberStyles.Float | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out result);
}
