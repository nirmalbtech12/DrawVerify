using System.Globalization;
using System.Text;
using DrawVerify.Geometry.Models;

namespace DrawVerify.Application.Services;

public static class DxfFileWriter
{
    public static byte[] Write(IEnumerable<ProjectionDrawing> drawings)
    {
        var builder = new StringBuilder("0\nSECTION\n2\nHEADER\n0\nENDSEC\n0\nSECTION\n2\nENTITIES\n");
        foreach (var drawing in drawings)
        {
            foreach (var entity in drawing.Entities)
                WriteEntity(builder, entity);
            foreach (var dimension in drawing.Dimensions)
                WriteDimension(builder, dimension);
        }
        builder.Append("0\nENDSEC\n0\nEOF\n");
        return Encoding.ASCII.GetBytes(builder.ToString());
    }

    private static void WriteDimension(StringBuilder builder, ProjectionDrawingDimension dimension)
    {
        var radial = string.Equals(dimension.Type, "Radius", StringComparison.OrdinalIgnoreCase) || string.Equals(dimension.Type, "Diameter", StringComparison.OrdinalIgnoreCase);
        var flags = string.Equals(dimension.Type, "Radius", StringComparison.OrdinalIgnoreCase) ? 4 : radial ? 3 : 0;
        Add(builder, "DIMENSION", 8, "GENERATED", 70, flags, 42, dimension.Value, 10, dimension.X, 20, dimension.Y, 13, dimension.X, 23, dimension.Y, 14, dimension.X + dimension.Value, 24, dimension.Y, 1, dimension.Value.ToString("G17", CultureInfo.InvariantCulture));
    }

    private static void WriteEntity(StringBuilder builder, ProjectionDrawingEntity entity)
    {
        var values = entity.Coordinates;
        switch (entity.Type)
        {
            case ProjectionDrawingEntityType.Line when values.Count >= 4:
                Add(builder, "LINE", 8, entity.Hidden ? "HIDDEN" : "VISIBLE", 10, values[0], 20, values[1], 11, values[2], 21, values[3]); break;
            case ProjectionDrawingEntityType.Circle when values.Count >= 3:
                Add(builder, "CIRCLE", 8, entity.Hidden ? "HIDDEN" : "VISIBLE", 10, values[0], 20, values[1], 40, values[2]); break;
            case ProjectionDrawingEntityType.Arc when values.Count >= 5:
                Add(builder, "ARC", 8, entity.Hidden ? "HIDDEN" : "VISIBLE", 10, values[0], 20, values[1], 40, values[2], 50, Degrees(values[3]), 51, Degrees(values[4])); break;
            case ProjectionDrawingEntityType.Polyline when values.Count >= 4:
                builder.Append("0\nLWPOLYLINE\n8\n").Append(entity.Hidden ? "HIDDEN" : "VISIBLE").Append("\n90\n").Append(values.Count / 2).Append('\n');
                for (var index = 0; index + 1 < values.Count; index += 2) Add(builder, null, 10, values[index], 20, values[index + 1]);
                break;
        }
    }

    private static void Add(StringBuilder builder, string? type, params object[] pairs)
    { if (type is not null) builder.Append("0\n").Append(type).Append('\n'); for (var i = 0; i < pairs.Length; i += 2) builder.Append(pairs[i]).Append('\n').Append(Format(pairs[i + 1])).Append('\n'); }
    private static string Format(object value) => value switch { double number => number.ToString("G17", CultureInfo.InvariantCulture), _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty };
    private static double Degrees(double radians) => radians * 180d / Math.PI;
}
