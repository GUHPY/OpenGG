using System.Globalization;

namespace OneRGB.Application.Lighting;

/// <summary>Cor RGB de 8 bits por canal, independente de UI.</summary>
public readonly record struct Rgb(byte R, byte G, byte B)
{
    public static readonly Rgb Black = new(0, 0, 0);

    public static bool TryParseHex(string? text, out Rgb color)
    {
        color = Black;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var s = text.Trim().TrimStart('#');
        if (s.Length != 6 || !uint.TryParse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var v))
        {
            return false;
        }

        color = new Rgb((byte)(v >> 16), (byte)(v >> 8), (byte)v);
        return true;
    }

    public static Rgb FromHex(string text) =>
        TryParseHex(text, out var c) ? c : throw new FormatException($"Invalid color: '{text}'. Use #RRGGBB.");

    public string ToHex() => string.Create(CultureInfo.InvariantCulture, $"#{R:X2}{G:X2}{B:X2}");

    public Rgb Scale(double factor)
    {
        var f = Math.Clamp(factor, 0, 1);
        return new Rgb((byte)Math.Round(R * f), (byte)Math.Round(G * f), (byte)Math.Round(B * f));
    }

    /// <summary>HSV → RGB. h em graus [0,360), s e v em [0,1].</summary>
    public static Rgb FromHsv(double h, double s, double v)
    {
        h = ((h % 360) + 360) % 360;
        s = Math.Clamp(s, 0, 1);
        v = Math.Clamp(v, 0, 1);
        var c = v * s;
        var x = c * (1 - Math.Abs((h / 60 % 2) - 1));
        var m = v - c;
        var (r, g, b) = (int)(h / 60) switch
        {
            0 => (c, x, 0d),
            1 => (x, c, 0d),
            2 => (0d, c, x),
            3 => (0d, x, c),
            4 => (x, 0d, c),
            _ => (c, 0d, x),
        };
        return new Rgb(ToByte(r + m), ToByte(g + m), ToByte(b + m));
    }

    /// <summary>RGB → HSV. Retorna h em graus, s e v em [0,1].</summary>
    public (double H, double S, double V) ToHsv()
    {
        double r = R / 255d, g = G / 255d, b = B / 255d;
        var max = Math.Max(r, Math.Max(g, b));
        var min = Math.Min(r, Math.Min(g, b));
        var d = max - min;
        double h = 0;
        if (d > 0)
        {
            if (max == r)
            {
                h = 60 * (((g - b) / d) % 6);
            }
            else if (max == g)
            {
                h = 60 * (((b - r) / d) + 2);
            }
            else
            {
                h = 60 * (((r - g) / d) + 4);
            }
        }

        if (h < 0)
        {
            h += 360;
        }

        return (h, max == 0 ? 0 : d / max, max);
    }

    public static Rgb Lerp(Rgb a, Rgb b, double t)
    {
        t = Math.Clamp(t, 0, 1);
        return new Rgb(
            (byte)Math.Round(a.R + ((b.R - a.R) * t)),
            (byte)Math.Round(a.G + ((b.G - a.G) * t)),
            (byte)Math.Round(a.B + ((b.B - a.B) * t)));
    }

    private static byte ToByte(double v) => (byte)Math.Round(Math.Clamp(v, 0, 1) * 255);
}
