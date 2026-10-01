using System.Globalization;
using OneRGB.Application.Presentation;

namespace OneRGB.Application.Lighting.Spatial;

/// <summary>
/// Um LED de um dispositivo em coordenadas locais (mm, origem no canto superior esquerdo do dispositivo, y para
/// baixo). <see cref="Zone"/> agrupa os LEDs (teclas, barra lateral, chipset) para os grupos da tela (SRGB-DEV-001).
/// </summary>
public readonly record struct LedPoint(string Id, double X, double Y, string Zone);

/// <summary>Mais um lugar onde o LED <see cref="Led"/> do quadro acende (fans em paralelo num hub repetem o mesmo quadro).</summary>
public readonly record struct LedCopy(int Led, double X, double Y);

/// <summary>Um elemento de um dispositivo composto (fan, bomba, pente, zona) que dá para soltar no canvas: caixa em mm, centro em (X, Y).</summary>
public sealed record LedPart(string Id, string Name, double X, double Y, double Width, double Height);

/// <summary>
/// Mapa de LEDs de um dispositivo (SRGB-LAYOUT-001): a ordem de <see cref="Leds"/> é a ordem do quadro que o
/// dispositivo recebe. <see cref="Evidence"/> diz por que a contagem ou a posição ainda não foi conferida no hardware.
/// </summary>
public sealed record LedMap(string DeviceId, string Name, double Width, double Height, IReadOnlyList<LedPoint> Leds)
{
    /// <summary>Quadros por segundo que o dispositivo aceita: o renderizador decima acima disto.</summary>
    public int MaxFps { get; init; } = 30;

    /// <summary>Contagem ou posição dos LEDs "a confirmar" (A20). Nulo quando veio do protocolo conferido.</summary>
    public string? Evidence { get; init; }

    /// <summary>Por onde o dispositivo recebe o quadro (modo avançado).</summary>
    public string Source { get; init; } = string.Empty;

    /// <summary>Periférico (teclado, microfone); os outros são componentes do PC. São os grupos da sincronização.</summary>
    public bool Peripheral { get; init; }

    /// <summary>
    /// Onde os LEDs acendem de verdade quando não é num lugar só (fans em paralelo): a prévia desenha estes pontos e o
    /// efeito é amostrado em <see cref="Leds"/>. Nulo: cada LED no próprio lugar.
    /// </summary>
    public IReadOnlyList<LedCopy>? Copies { get; init; }

    /// <summary>
    /// LEDs num anel em volta do centro (fans): no layout sincronizado o eixo horizontal do efeito dá a volta no anel, e
    /// um anel de 8 LEDs mostra o mesmo arco-íris inteiro que um de 24.
    /// </summary>
    public bool Ring { get; init; }

    /// <summary>
    /// Elementos que o usuário separa no canvas (Desagrupar). Cada ponto do mapa é da parte de centro mais perto; num
    /// mapa em paralelo o anel amostrado vai com a primeira parte. Nulo: o dispositivo é uma peça só.
    /// </summary>
    public IReadOnlyList<LedPart>? Parts { get; init; }

    public IReadOnlyList<string> Zones => [.. Leds.Select(l => l.Zone).Distinct(StringComparer.Ordinal)];

    public int CountIn(string zone) => Leds.Count(l => string.Equals(l.Zone, zone, StringComparison.Ordinal));

    /// <summary>Índice da parte de centro mais perto de (x, y) mm; 0 sem partes.</summary>
    public int PartAt(double x, double y) => Parts is { Count: > 1 } parts
        ? Enumerable.Range(0, parts.Count).MinBy(i => Math.Pow(parts[i].X - x, 2) + Math.Pow(parts[i].Y - y, 2))
        : 0;
}

/// <summary>
/// Descritores estáticos dos dispositivos com LED do PC (phase0/data/devices.json). O QuadCast tem a ordem dos LEDs
/// tirada do protocolo corroborado; o que cada cabeçalho ARGB da placa leva foi conferido acendendo um por um neste PC
/// (28/09), com as contagens do SignalRGB (o controlador não informa quantos LEDs há nas fitas); os outros são
/// estimativas. Tudo com <see cref="LedMap.Evidence"/>.
/// </summary>
public static class LedMaps
{
    public const string KeyboardId = "kbd-steelseries-apex-pro-tkl-gen3";
    public const double KeyPitch = 19.05;
    public static LedMap Keyboard { get; } = BuildKeyboard();    private static LedMap BuildKeyboard()
    {
        var keys = KeyboardLayout.Tkl;
        var width = keys.Max(k => k.X + k.Width) * KeyPitch;
        var height = keys.Max(k => k.Y + k.Height) * KeyPitch;
        var leds = keys.Select(k => new LedPoint(k.Id, k.CenterX * KeyPitch, k.CenterY * KeyPitch, k.Y < 1 ? "funcao" : "keys")).ToList();
        return new LedMap(KeyboardId, "SteelSeries Apex Pro TKL", width, height, leds)
        {
            MaxFps = 30,
            Peripheral = true,
            Source = "USB HID 0x61, exact verified 1038:1644 receiver",
            Evidence = "87 LED usages from the ANSI layout; advanced configuration uses a separate 68-usage map.",
        };
    }
}
