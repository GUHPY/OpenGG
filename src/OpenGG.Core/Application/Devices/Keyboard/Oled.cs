using System.Globalization;
using System.Text;

namespace OneRGB.Application.Devices.Keyboard;

public enum Dithering
{
    /// <summary>Acende o que passa do limiar.</summary>
    Threshold,

    /// <summary>Difusão de erro (fotos, gradientes).</summary>
    FloydSteinberg,

    /// <summary>Matriz de Bayer 4×4 (padrão estável entre quadros de GIF, sem "chuvisco").</summary>
    Ordered,
}

/// <summary>
/// Quadro monocromático (1 bit por pixel) para a tela do teclado (A5.11). Exporta no formato de linhas com o bit mais
/// significativo à esquerda — 128×40 viram 640 bytes, o formato de imagem do GameSense.
/// </summary>
public sealed class OledFrame
{
    private static readonly int[,] Bayer =
    {
        { 0, 8, 2, 10 },
        { 12, 4, 14, 6 },
        { 3, 11, 1, 9 },
        { 15, 7, 13, 5 },
    };

    private readonly bool[] _pixels;

    public OledFrame(int width, int height)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(width, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(height, 1);
        Width = width;
        Height = height;
        _pixels = new bool[width * height];
    }

    public int Width { get; }

    public int Height { get; }

    /// <summary>Pixels acesos (para testes e para a prévia na interface).</summary>
    public int LitCount => _pixels.Count(p => p);

    public bool this[int x, int y]
    {
        get => x >= 0 && y >= 0 && x < Width && y < Height && _pixels[(y * Width) + x];
        set
        {
            if (x >= 0 && y >= 0 && x < Width && y < Height)
            {
                _pixels[(y * Width) + x] = value;
            }
        }
    }

    public void Clear() => Array.Clear(_pixels);

    public void Invert()
    {
        for (var i = 0; i < _pixels.Length; i++)
        {
            _pixels[i] = !_pixels[i];
        }
    }

    public void FillRect(int x, int y, int width, int height, bool on = true)
    {
        for (var row = y; row < y + height; row++)
        {
            for (var column = x; column < x + width; column++)
            {
                this[column, row] = on;
            }
        }
    }

    public void DrawRect(int x, int y, int width, int height)
    {
        FillRect(x, y, width, 1);
        FillRect(x, y + height - 1, width, 1);
        FillRect(x, y, 1, height);
        FillRect(x + width - 1, y, 1, height);
    }

    /// <summary>Barra com contorno, preenchida na fração (bateria, uso, progresso).</summary>
    public void Bar(int x, int y, int width, int height, double fraction)
    {
        DrawRect(x, y, width, height);
        var inner = (int)Math.Round((width - 4) * Math.Clamp(double.IsFinite(fraction) ? fraction : 0, 0, 1));
        FillRect(x + 2, y + 2, inner, height - 4);
    }

    /// <summary>Escreve texto com a fonte 5×7 (maiúsculas; acentos viram a letra base). Devolve a largura usada.</summary>
    public int DrawText(int x, int y, string text, int scale = 1)
    {
        ArgumentNullException.ThrowIfNull(text);
        scale = Math.Clamp(scale, 1, 4);
        var cursor = x;
        foreach (var c in OledFont.Normalize(text))
        {
            var glyph = OledFont.Glyph(c);
            for (var row = 0; row < OledFont.GlyphHeight; row++)
            {
                for (var column = 0; column < OledFont.GlyphWidth; column++)
                {
                    if ((glyph[row] & (1 << (OledFont.GlyphWidth - 1 - column))) != 0)
                    {
                        FillRect(cursor + (column * scale), y + (row * scale), scale, scale);
                    }
                }
            }

            cursor += OledFont.Advance * scale;
        }

        return cursor - x;
    }

    /// <summary>Linhas com o bit mais significativo à esquerda (largura arredondada para múltiplo de 8).</summary>
    public byte[] ToRowMajorMsbFirst()
    {
        var stride = (Width + 7) / 8;
        var data = new byte[stride * Height];
        for (var y = 0; y < Height; y++)
        {
            for (var x = 0; x < Width; x++)
            {
                if (_pixels[(y * Width) + x])
                {
                    data[(y * stride) + (x / 8)] |= (byte)(0x80 >> (x % 8));
                }
            }
        }

        return data;
    }

    public static OledFrame FromRowMajorMsbFirst(int width, int height, ReadOnlySpan<byte> data)
    {
        var frame = new OledFrame(width, height);
        var stride = (width + 7) / 8;
        if (data.Length < stride * height)
        {
            throw new ArgumentException($"Esperado {stride * height} bytes, veio {data.Length}.", nameof(data));
        }

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                frame._pixels[(y * width) + x] = (data[(y * stride) + (x / 8)] & (0x80 >> (x % 8))) != 0;
            }
        }

        return frame;
    }

    /// <summary>Converte uma imagem em tons de cinza (0 = preto, 255 = branco) já no tamanho da tela.</summary>
    public static OledFrame FromGrayscale(int width, int height, ReadOnlySpan<byte> luma, Dithering dithering = Dithering.FloydSteinberg, byte threshold = 128)
    {
        if (luma.Length < width * height)
        {
            throw new ArgumentException($"Esperado {width * height} pixels, veio {luma.Length}.", nameof(luma));
        }

        var frame = new OledFrame(width, height);
        switch (dithering)
        {
            case Dithering.Threshold:
                for (var i = 0; i < width * height; i++)
                {
                    frame._pixels[i] = luma[i] >= threshold;
                }

                break;
            case Dithering.Ordered:
                for (var y = 0; y < height; y++)
                {
                    for (var x = 0; x < width; x++)
                    {
                        var limit = ((Bayer[y % 4, x % 4] + 0.5) * 16) - 0.5;
                        frame._pixels[(y * width) + x] = luma[(y * width) + x] > limit;
                    }
                }

                break;
            default:
                var error = new double[width * height];
                for (var i = 0; i < error.Length; i++)
                {
                    error[i] = luma[i];
                }

                for (var y = 0; y < height; y++)
                {
                    for (var x = 0; x < width; x++)
                    {
                        var index = (y * width) + x;
                        var on = error[index] >= threshold;
                        frame._pixels[index] = on;
                        var residual = error[index] - (on ? 255 : 0);
                        Spread(x + 1, y, 7 / 16.0);
                        Spread(x - 1, y + 1, 3 / 16.0);
                        Spread(x, y + 1, 5 / 16.0);
                        Spread(x + 1, y + 1, 1 / 16.0);

                        void Spread(int sx, int sy, double weight)
                        {
                            if (sx >= 0 && sx < width && sy < height)
                            {
                                error[(sy * width) + sx] += residual * weight;
                            }
                        }
                    }
                }

                break;
        }

        return frame;
    }
}

/// <summary>Fonte 5×7 própria do OneRGB (maiúsculas, dígitos e símbolos usados em status).</summary>
public static class OledFont
{
    public const int GlyphWidth = 5;
    public const int GlyphHeight = 7;

    /// <summary>Largura de um caractere com o espaço entre letras.</summary>
    public const int Advance = GlyphWidth + 1;

    private static readonly Dictionary<char, byte[]> Glyphs = Build();

    public static int Measure(string text, int scale = 1) => (text?.Length ?? 0) * Advance * Math.Clamp(scale, 1, 4);

    public static bool Has(char c) => Glyphs.ContainsKey(c);

    public static byte[] Glyph(char c) => Glyphs.GetValueOrDefault(c) ?? Glyphs['?'];

    /// <summary>Maiúsculas sem acento ("Padrão" → "PADRAO"); o resto que a fonte não tem vira "?".</summary>
    public static string Normalize(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var builder = new StringBuilder(text.Length);
        foreach (var c in text.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            var upper = char.ToUpperInvariant(c);
            builder.Append(Glyphs.ContainsKey(upper) ? upper : '?');
        }

        return builder.ToString();
    }

    private static Dictionary<char, byte[]> Build()
    {
        var source = new Dictionary<char, string>
        {
            [' '] = "...../...../...../...../...../...../.....",
            ['0'] = ".###./#...#/#..##/#.#.#/##..#/#...#/.###.",
            ['1'] = "..#../.##../..#../..#../..#../..#../.###.",
            ['2'] = ".###./#...#/....#/...#./..#../.#.../#####",
            ['3'] = "#####/...#./..#../...#./....#/#...#/.###.",
            ['4'] = "...#./..##./.#.#./#..#./#####/...#./...#.",
            ['5'] = "#####/#..../####./....#/....#/#...#/.###.",
            ['6'] = "..##./.#.../#..../####./#...#/#...#/.###.",
            ['7'] = "#####/....#/...#./..#../.#.../.#.../.#...",
            ['8'] = ".###./#...#/#...#/.###./#...#/#...#/.###.",
            ['9'] = ".###./#...#/#...#/.####/....#/...#./.##..",
            ['A'] = ".###./#...#/#...#/#####/#...#/#...#/#...#",
            ['B'] = "####./#...#/#...#/####./#...#/#...#/####.",
            ['C'] = ".###./#...#/#..../#..../#..../#...#/.###.",
            ['D'] = "###../#..#./#...#/#...#/#...#/#..#./###..",
            ['E'] = "#####/#..../#..../####./#..../#..../#####",
            ['F'] = "#####/#..../#..../####./#..../#..../#....",
            ['G'] = ".###./#...#/#..../#.###/#...#/#...#/.####",
            ['H'] = "#...#/#...#/#...#/#####/#...#/#...#/#...#",
            ['I'] = ".###./..#../..#../..#../..#../..#../.###.",
            ['J'] = "..###/...#./...#./...#./...#./#..#./.##..",
            ['K'] = "#...#/#..#./#.#../##.../#.#../#..#./#...#",
            ['L'] = "#..../#..../#..../#..../#..../#..../#####",
            ['M'] = "#...#/##.##/#.#.#/#.#.#/#...#/#...#/#...#",
            ['N'] = "#...#/#...#/##..#/#.#.#/#..##/#...#/#...#",
            ['O'] = ".###./#...#/#...#/#...#/#...#/#...#/.###.",
            ['P'] = "####./#...#/#...#/####./#..../#..../#....",
            ['Q'] = ".###./#...#/#...#/#...#/#.#.#/#..#./.##.#",
            ['R'] = "####./#...#/#...#/####./#.#../#..#./#...#",
            ['S'] = ".####/#..../#..../.###./....#/....#/####.",
            ['T'] = "#####/..#../..#../..#../..#../..#../..#..",
            ['U'] = "#...#/#...#/#...#/#...#/#...#/#...#/.###.",
            ['V'] = "#...#/#...#/#...#/#...#/#...#/.#.#./..#..",
            ['W'] = "#...#/#...#/#...#/#.#.#/#.#.#/#.#.#/.#.#.",
            ['X'] = "#...#/#...#/.#.#./..#../.#.#./#...#/#...#",
            ['Y'] = "#...#/#...#/.#.#./..#../..#../..#../..#..",
            ['Z'] = "#####/....#/...#./..#../.#.../#..../#####",
            ['.'] = "...../...../...../...../...../.##../.##..",
            [','] = "...../...../...../...../.##../..#../.#...",
            [':'] = "...../.##../.##../...../.##../.##../.....",
            [';'] = "...../.##../.##../...../.##../..#../.#...",
            ['-'] = "...../...../...../#####/...../...../.....",
            ['+'] = "...../..#../..#../#####/..#../..#../.....",
            ['='] = "...../...../#####/...../#####/...../.....",
            ['/'] = "...../....#/...#./..#../.#.../#..../.....",
            ['\\'] = "...../#..../.#.../..#../...#./....#/.....",
            ['%'] = "##.../##..#/...#./..#../.#.../#..##/...##",
            ['('] = "...#./..#../.#.../.#.../.#.../..#../...#.",
            [')'] = ".#.../..#../...#./...#./...#./..#../.#...",
            ['!'] = "..#../..#../..#../..#../..#../...../..#..",
            ['?'] = ".###./#...#/....#/...#./..#../...../..#..",
            ['\''] = "..#../..#../.#.../...../...../...../.....",
            ['"'] = ".#.#./.#.#./.#.#./...../...../...../.....",
            ['_'] = "...../...../...../...../...../...../#####",
            ['<'] = "...#./..#../.#.../#..../.#.../..#../...#.",
            ['>'] = ".#.../..#../...#./....#/...#./..#../.#...",
            ['°'] = ".##../#..#./#..#./.##../...../...../.....",
            ['#'] = ".#.#./.#.#./#####/.#.#./#####/.#.#./.#.#.",
            ['*'] = "...../..#../#.#.#/.###./#.#.#/..#../.....",
            ['['] = ".###./.#.../.#.../.#.../.#.../.#.../.###.",
            [']'] = ".###./...#./...#./...#./...#./...#./.###.",
            ['|'] = "..#../..#../..#../..#../..#../..#../..#..",
        };

        var glyphs = new Dictionary<char, byte[]>();
        foreach (var (c, pattern) in source)
        {
            var rows = pattern.Split('/');
            if (rows.Length != GlyphHeight || rows.Any(r => r.Length != GlyphWidth))
            {
                throw new InvalidOperationException($"Glifo mal formado: '{c}'.");
            }

            glyphs[c] = [.. rows.Select(r => (byte)r.Select((ch, i) => ch == '#' ? 1 << (GlyphWidth - 1 - i) : 0).Sum())];
        }

        return glyphs;
    }
}

/// <summary>O que a tela de status do teclado mostra (A5.11).</summary>
public sealed record OledStatus
{
    public required string Profile { get; init; }

    /// <summary>Menor e maior ponto de atuação em uso.</summary>
    public (double Minimum, double Maximum)? Actuation { get; init; }

    public bool RapidTrigger { get; init; }

    public double? RapidTriggerMm { get; init; }

    public bool Protection { get; init; }

    public bool CapsLock { get; init; }

    public bool ScrollLock { get; init; }

    public int? BatteryPercent { get; init; }

    /// <summary>Resumo da configuração do teclado para a tela.</summary>
    public static OledStatus From(string profile, KeyboardConfig config, bool capsLock = false, bool scrollLock = false, int? battery = null)
    {
        ArgumentNullException.ThrowIfNull(config);
        var rapid = config.RapidTrigger.Where(r => r.Value.Enabled).Select(r => r.Value.PressMm).ToList();
        return new OledStatus
        {
            Profile = profile,
            Actuation = config.Actuation.Count == 0 ? null : (config.Actuation.Values.Min(), config.Actuation.Values.Max()),
            RapidTrigger = rapid.Count > 0,
            RapidTriggerMm = rapid.Count > 0 ? rapid.Min() : null,
            Protection = config.Protection.Enabled,
            CapsLock = capsLock,
            ScrollLock = scrollLock,
            BatteryPercent = battery,
        };
    }
}

public static class OledScreens
{
    /// <summary>Tela de status: perfil, atuação, Rapid Trigger, proteção, travas e bateria.</summary>
    public static OledFrame Status(OledPanel panel, OledStatus status, IFormatProvider? provider = null)
    {
        ArgumentNullException.ThrowIfNull(panel);
        ArgumentNullException.ThrowIfNull(status);
        provider ??= CultureInfo.CurrentCulture;
        var frame = new OledFrame(panel.Width, panel.Height);
        var columns = panel.Width / OledFont.Advance;
        frame.DrawText(0, 0, Fit(status.Profile, columns));
        frame.FillRect(0, 9, panel.Width, 1);

        var actuation = status.Actuation is { } a
            ? Math.Abs(a.Maximum - a.Minimum) < 1e-9
                ? string.Create(provider, $"ACTUATION {a.Minimum:0.0} MM")
                : string.Create(provider, $"ACTUATION {a.Minimum:0.0}-{a.Maximum:0.0}")
            : "FIXED ACTUATION";
        frame.DrawText(0, 12, Fit(actuation, columns));

        var rapid = status.RapidTrigger && status.RapidTriggerMm is { } rt ? string.Create(provider, $"RT {rt:0.0}") : "RT OFF";
        frame.DrawText(0, 21, Fit($"{rapid}  PROT {(status.Protection ? "ON" : "OFF")}", columns));

        var locks = string.Join(' ', new[] { status.CapsLock ? "CAPS" : null, status.ScrollLock ? "SCRL" : null }.Where(l => l is not null));
        frame.DrawText(0, 31, Fit(locks, columns - 6));
        if (status.BatteryPercent is { } battery)
        {
            var width = 26;
            frame.Bar(panel.Width - width, 31, width, 8, battery / 100.0);
        }

        return frame;
    }

    /// <summary>Até cinco linhas de texto (informações de sistema, notificações, conteúdo do usuário).</summary>
    public static OledFrame Lines(OledPanel panel, IReadOnlyList<string> lines)
    {
        ArgumentNullException.ThrowIfNull(panel);
        ArgumentNullException.ThrowIfNull(lines);
        var frame = new OledFrame(panel.Width, panel.Height);
        var columns = panel.Width / OledFont.Advance;
        var rows = panel.Height / (OledFont.GlyphHeight + 1);
        for (var i = 0; i < Math.Min(rows, lines.Count); i++)
        {
            frame.DrawText(0, i * (OledFont.GlyphHeight + 1), Fit(lines[i], columns));
        }

        return frame;
    }

    private static string Fit(string text, int columns) => text.Length <= columns ? text : text[..Math.Max(0, columns - 1)] + ".";
}
