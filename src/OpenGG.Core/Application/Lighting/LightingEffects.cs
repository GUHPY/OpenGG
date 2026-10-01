namespace OneRGB.Application.Lighting;

/// <summary>Modos de efeito (porte de RGBEffectMode em design/clean/lib/types.ts).</summary>
public enum LightingMode
{
    Static,
    Breathing,
    ColorCycle,
    Wave,
    AudioReactive,
    Off,
}

/// <summary>Estado de uma cena de iluminação.</summary>
public sealed record LightingScene(LightingMode Mode, Rgb Color, double Brightness = 1.0, double Speed = 0.5)
{
    public static readonly LightingScene Default = new(LightingMode.Static, Rgb.FromHex("#06B6D4"));
}

/// <summary>
/// Gera os quadros de iluminação no PC (spec §33: animação gerada centralmente). Serve para qualquer
/// dispositivo com LEDs dispostos numa grade; o QuadCast 2 S é uma grade 12x9.
/// Função pura do tempo: facilita teste e mantém todos os dispositivos sincronizados.
/// </summary>
public static class LightingEffects
{
    /// <summary>Período do efeito em segundos para speed ∈ [0,1] (0 = lento, 1 = rápido).</summary>
    public static double PeriodSeconds(double speed) => 8.0 - (Math.Clamp(speed, 0, 1) * 7.0);

    /// <summary>Cor do LED na coluna <paramref name="x"/> de <paramref name="columns"/> no instante <paramref name="t"/> (s).</summary>
    public static Rgb ColorAt(LightingScene scene, int x, int columns, double t, double audioLevel = 0)
    {
        ArgumentNullException.ThrowIfNull(scene);
        var period = PeriodSeconds(scene.Speed);
        var c = scene.Mode switch
        {
            LightingMode.Off => Rgb.Black,
            LightingMode.Static => scene.Color,
            LightingMode.Breathing => scene.Color.Scale(0.15 + (0.85 * (0.5 - (0.5 * Math.Cos(2 * Math.PI * t / period))))),
            LightingMode.ColorCycle => Rgb.FromHsv(360 * (t / period), 1, 1),
            LightingMode.Wave => Rgb.FromHsv((360 * (t / period)) + (360.0 * x / Math.Max(1, columns)), 1, 1),
            LightingMode.AudioReactive => scene.Color.Scale(AudioLight.Level(audioLevel)),
            _ => scene.Color,
        };
        return c.Scale(scene.Brightness);
    }

    /// <summary>Quadro completo de uma grade colunas × linhas, em ordem de linha (y) e depois coluna (x).</summary>
    public static Rgb[] Frame(LightingScene scene, int columns, int rows, double t, double audioLevel = 0)
    {
        var frame = new Rgb[columns * rows];
        for (var y = 0; y < rows; y++)
        {
            for (var x = 0; x < columns; x++)
            {
                // VU meter: no modo reativo a áudio as linhas acendem de baixo para cima.
                var color = ColorAt(scene, x, columns, t, audioLevel);
                if (scene.Mode == LightingMode.AudioReactive)
                {
                    var lit = (rows - 1 - y) < AudioLight.LitRows(audioLevel, rows);
                    color = lit ? scene.Color.Scale(scene.Brightness) : Rgb.Black;
                }

                frame[(y * columns) + x] = color;
            }
        }

        return frame;
    }
}

/// <summary>
/// Nível de áudio para a luz: todo efeito que reage ao som passa por aqui (QuadCast, canvas, prévias), para o silêncio
/// apagar tudo. A entrada é a fração da escala de −60 a 0 dBFS, a mesma do VU e do <see cref="Spatial.AudioSpectrum"/>.
/// </summary>
public static class AudioLight
{
    /// <summary>Portão de ruído: abaixo disto é silêncio (o chiado do microfone não acende nada).</summary>
    public const double GateDbfs = -50;

    private const double Gate = (GateDbfs - Spatial.AudioSpectrum.FloorDb) / -Spatial.AudioSpectrum.FloorDb;

    /// <summary>Nível com o portão aplicado e o que sobra acima dele esticado até 1. Sem piso: zero é apagado.</summary>
    public static double Level(double scale) => double.IsFinite(scale) && scale > Gate ? Math.Min((scale - Gate) / (1 - Gate), 1) : 0;

    /// <summary>Quantas das <paramref name="rows"/> linhas acendem, de baixo para cima (arredondado, não para cima).</summary>
    public static int LitRows(double scale, int rows) => (int)Math.Round(Level(scale) * Math.Max(rows, 0), MidpointRounding.AwayFromZero);
}
