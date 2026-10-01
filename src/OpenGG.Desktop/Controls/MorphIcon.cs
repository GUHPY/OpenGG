using System.Collections.Concurrent;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Animation;
using OneRGB.Application.Presentation;

namespace OpenGG.Desktop.Controls;

/// <summary>
/// Easing de mola para animações WPF de duração fixa (porte de morphicons/src/core/spring.ts, MIT). O padrão é
/// criticamente amortecido — sem ultrapassar o alvo, como pede a spec §75. Use <see cref="DurationOf"/> como duração
/// para a curva terminar exatamente quando a mola assenta.
/// </summary>
public sealed class SpringEase : EasingFunctionBase
{
    private static readonly ConcurrentDictionary<SpringConfig, double[]> Curves = new();
    private static readonly ConcurrentDictionary<SpringConfig, TimeSpan> Durations = new();
    private SpringConfig _config;
    private double[] _curve;

    public SpringEase()
        : this(SpringConfig.Smooth)
    {
    }

    public SpringEase(SpringConfig config)
    {
        EasingMode = EasingMode.EaseIn; // a curva já é a animação inteira
        _config = config;
        _curve = Curves.GetOrAdd(config, c => DampedSpring.Curve(c));
    }

    public static SpringEase Smooth { get; } = new(SpringConfig.Smooth);

    public static SpringEase Quick { get; } = new(SpringConfig.Quick);

    /// <summary>Preset no XAML: Smooth (padrão) ou Quick.</summary>
    public string Preset
    {
        get => _config == SpringConfig.Quick ? "Quick" : "Smooth";
        set
        {
            _config = value == "Quick" ? SpringConfig.Quick : SpringConfig.Smooth;
            _curve = Curves.GetOrAdd(_config, c => DampedSpring.Curve(c));
        }
    }

    public static TimeSpan DurationOf(SpringConfig config) => Durations.GetOrAdd(config, DampedSpring.SettleTime);

    protected override double EaseInCore(double normalizedTime) => DampedSpring.Sample(_curve, normalizedTime);

    protected override Freezable CreateInstanceCore() => new SpringEase(_config);
}

/// <summary>
/// Ícone que se transforma no próximo (porte de morphicons, MIT): ao trocar <see cref="Glyph"/>, os traços são
/// reamostrados, pareados e interpolados em forma polar com uma mola criticamente amortecida. Interromper no meio
/// continua do quadro atual. Com "Reduzir movimento", troca na hora (spec §76).
/// </summary>
