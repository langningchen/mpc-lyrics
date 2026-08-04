using System.Drawing;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MpcLyrics.Core;

[JsonConverter(typeof(RgbaColorJsonConverter))]
public readonly record struct RgbaColor(byte R, byte G, byte B, byte A)
{
    public static RgbaColor FromArgb(byte a, byte r, byte g, byte b) => new(r, g, b, a);

    public static RgbaColor Parse(string value)
    {
        if (!TryParse(value, out var color))
        {
            throw new FormatException($"Invalid RGBA color: {value}");
        }
        return color;
    }

    public static bool TryParse(string? value, out RgbaColor color)
    {
        color = default;
        if (string.IsNullOrWhiteSpace(value)) return false;
        var text = value.Trim();
        if (text.StartsWith('#')) text = text[1..];
        if (text.Length == 6) text += "FF";
        if (text.Length != 8) return false;

        return byte.TryParse(text.AsSpan(0, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var r)
            && byte.TryParse(text.AsSpan(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var g)
            && byte.TryParse(text.AsSpan(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var b)
            && byte.TryParse(text.AsSpan(6, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var a)
            && Assign(r, g, b, a, out color);
    }

    private static bool Assign(byte r, byte g, byte b, byte a, out RgbaColor color)
    {
        color = new RgbaColor(r, g, b, a);
        return true;
    }

    public Color ToDrawingColor() => Color.FromArgb(A, R, G, B);
    public Windows.UI.Color ToWindowsColor() => Windows.UI.Color.FromArgb(A, R, G, B);
    public RgbaColor WithAlpha(byte alpha) => new(R, G, B, alpha);
    public string ToHex() => $"#{R:X2}{G:X2}{B:X2}{A:X2}";
    public override string ToString() => ToHex();

    public static RgbaColor FromWindowsColor(Windows.UI.Color color) =>
        new(color.R, color.G, color.B, color.A);
}

internal sealed class RgbaColorJsonConverter : JsonConverter<RgbaColor>
{
    public override RgbaColor Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        RgbaColor.Parse(reader.GetString() ?? "#00000000");

    public override void Write(Utf8JsonWriter writer, RgbaColor value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.ToHex());
}
