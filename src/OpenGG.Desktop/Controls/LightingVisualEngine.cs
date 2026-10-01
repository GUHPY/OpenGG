using System.Windows;
using System.Windows.Media;
using OneRGB.Application.Lighting;
using OneRGB.Domain;

namespace OpenGG.Desktop.Controls;

/// <summary>
/// Motor de renderização procedural de efeitos de iluminação RGB para os palcos de hardware (Apex, G Pro, Astro, Mobo, GPU, RAM, etc.).
/// </summary>
public static class LightingVisualEngine
{
    private static readonly Color[] RainbowColors =
    [
        Color.FromRgb(0xFF, 0x1E, 0x38), // Vermelho vibrante
        Color.FromRgb(0xFF, 0x78, 0x00), // Laranja
        Color.FromRgb(0xFF, 0xD6, 0x00), // Amarelo
        Color.FromRgb(0x00, 0xE6, 0x76), // Verde esmeralda
        Color.FromRgb(0x00, 0xE5, 0xFF), // Ciano
        Color.FromRgb(0x3B, 0x82, 0xF6), // Azul royal
        Color.FromRgb(0x9C, 0x27, 0xB0), // Roxo
        Color.FromRgb(0xFF, 0x1E, 0x38), // Loop vermelho
    ];

    public static SolidColorBrush CreateFrozenBrush(Color color, double opacity = 1.0)
    {
        var b = new SolidColorBrush(color) { Opacity = Math.Clamp(opacity, 0, 1) };
        b.Freeze();
        return b;
    }

    public static RadialGradientBrush CreateRadialGlowBrush(Color color, double centerOpacity = 0.6)
    {
        var centerCol = Color.FromArgb((byte)(255 * Math.Clamp(centerOpacity, 0, 1)), color.R, color.G, color.B);
        var edgeCol = Color.FromArgb(0, color.R, color.G, color.B);
        var brush = new RadialGradientBrush(centerCol, edgeCol)
        {
            Center = new Point(0.5, 0.5),
            GradientOrigin = new Point(0.5, 0.5),
            RadiusX = 0.5,
            RadiusY = 0.5,
        };
        brush.Freeze();
        return brush;
    }

    public static Brush CreateEffectBrush(LightingMode mode, Color color, double opacity, double speed, double phase, double time)
    {
        var clampedOpacity = Math.Clamp(opacity, 0.0, 1.0);
        if (clampedOpacity <= 0.001 || mode == LightingMode.Off)
        {
            return Brushes.Transparent;
        }

        switch (mode)
        {
            case LightingMode.Wave:
            default:
            {
                var p = (phase * 0.45) % 1.0;
                var brush = new LinearGradientBrush
                {
                    StartPoint = new Point(p - 1.0, 0),
                    EndPoint = new Point(p, 0),
                    SpreadMethod = GradientSpreadMethod.Repeat,
                    MappingMode = BrushMappingMode.RelativeToBoundingBox,
                    Opacity = clampedOpacity
                };

                for (var i = 0; i < RainbowColors.Length; i++)
                {
                    var offset = (double)i / (RainbowColors.Length - 1);
                    brush.GradientStops.Add(new GradientStop(RainbowColors[i], offset));
                }

                brush.Freeze();
                return brush;
            }

            case LightingMode.ColorCycle:
            {
                var hue = ((time * 45.0 * (0.3 + (Math.Clamp(speed, 0, 1) * 1.5))) + (phase * 20.0)) % 360.0;
                var rgb = Rgb.FromHsv(hue, 1.0, 1.0);
                var c = Color.FromRgb(rgb.R, rgb.G, rgb.B);
                return CreateFrozenBrush(c, clampedOpacity);
            }

            case LightingMode.Breathing:
            {
                var period = 4.0 / Math.Max(0.1, 0.2 + (Math.Clamp(speed, 0, 1) * 1.8));
                var factor = 0.12 + (0.88 * (0.5 - (0.5 * Math.Cos((2.0 * Math.PI * time) / period))));
                var r = (byte)Math.Clamp(color.R * factor, 0, 255);
                var g = (byte)Math.Clamp(color.G * factor, 0, 255);
                var b = (byte)Math.Clamp(color.B * factor, 0, 255);
                return CreateFrozenBrush(Color.FromRgb(r, g, b), clampedOpacity);
            }

            case LightingMode.AudioReactive:
            {
                // Sem o nível real aqui (só enquanto o primeiro quadro não chega): pulso de exemplo, que também apaga.
                var factor = 0.5 + (0.5 * Math.Sin(time * 8.0));
                var r = (byte)Math.Clamp(color.R * factor, 0, 255);
                var g = (byte)Math.Clamp(color.G * factor, 0, 255);
                var b = (byte)Math.Clamp(color.B * factor, 0, 255);
                return CreateFrozenBrush(Color.FromRgb(r, g, b), clampedOpacity);
            }

            case LightingMode.Static:
            {
                return CreateFrozenBrush(color, clampedOpacity);
            }
        }
    }

    public static Brush CreateFrameBrush(IReadOnlyList<Rgb> frame, double opacity)
    {
        var clampedOpacity = Math.Clamp(opacity, 0.0, 1.0);
        if (frame.Count == 0 || clampedOpacity <= 0.001)
        {
            return Brushes.Transparent;
        }

        var count = Math.Min(frame.Count, 24);
        var stops = new GradientStopCollection();
        if (count <= 1)
        {
            var c = Color.FromRgb(frame[0].R, frame[0].G, frame[0].B);
            stops.Add(new GradientStop(c, 0.0));
            stops.Add(new GradientStop(c, 1.0));
        }
        else
        {
            for (var i = 0; i < count; i++)
            {
                var idx = (int)((double)i * (frame.Count - 1) / (count - 1));
                var rgb = frame[idx];
                stops.Add(new GradientStop(Color.FromRgb(rgb.R, rgb.G, rgb.B), (double)i / (count - 1)));
            }
        }

        var brush = new LinearGradientBrush(stops, new Point(0, 0), new Point(1, 0))
        {
            Opacity = clampedOpacity
        };
        brush.Freeze();
        return brush;
    }
}
