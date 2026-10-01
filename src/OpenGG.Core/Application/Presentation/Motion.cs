namespace OneRGB.Application.Presentation;

/// <summary>
/// Curva cubic-bezier do CSS (porte de design/tokens/motion/motion.css e motion-presets.ts).
/// O WPF não tem cubic-bezier nativo; a UI usa esta função dentro de um EasingFunctionBase.
/// </summary>
public sealed class CubicBezier
{
    public static readonly CubicBezier Standard = new(0.2, 0, 0, 1);
    public static readonly CubicBezier Emphasized = new(0.05, 0.7, 0.1, 1);
    public static readonly CubicBezier Spring = new(0.175, 0.885, 0.32, 1.275);
    public static readonly CubicBezier EaseOut = new(0, 0, 0.2, 1);
    public static readonly CubicBezier EaseInOut = new(0.4, 0, 0.2, 1);

    private readonly double _x1, _y1, _x2, _y2;

    public CubicBezier(double x1, double y1, double x2, double y2)
    {
        _x1 = Math.Clamp(x1, 0, 1);
        _y1 = y1;
        _x2 = Math.Clamp(x2, 0, 1);
        _y2 = y2;
    }

    /// <summary>Progresso da animação (y) para o tempo normalizado <paramref name="x"/> ∈ [0,1].</summary>
    public double Evaluate(double x)
    {
        if (x <= 0)
        {
            return 0;
        }

        if (x >= 1)
        {
            return 1;
        }

        // Newton-Raphson para achar t tal que Bx(t) = x, com bisseção de reserva.
        var t = x;
        for (var i = 0; i < 8; i++)
        {
            var err = Sample(_x1, _x2, t) - x;
            if (Math.Abs(err) < 1e-6)
            {
                return Sample(_y1, _y2, t);
            }

            var d = SampleDerivative(_x1, _x2, t);
            if (Math.Abs(d) < 1e-6)
            {
                break;
            }

            t -= err / d;
        }

        double lo = 0, hi = 1;
        t = x;
        for (var i = 0; i < 30; i++)
        {
            var v = Sample(_x1, _x2, t);
            if (Math.Abs(v - x) < 1e-6)
            {
                break;
            }

            if (v < x)
            {
                lo = t;
            }
            else
            {
                hi = t;
            }

            t = (lo + hi) / 2;
        }

        return Sample(_y1, _y2, t);
    }

    private static double Sample(double p1, double p2, double t)
    {
        var u = 1 - t;
        return (3 * u * u * t * p1) + (3 * u * t * t * p2) + (t * t * t);
    }

    private static double SampleDerivative(double p1, double p2, double t)
    {
        var u = 1 - t;
        return (3 * u * u * p1) + (6 * u * t * (p2 - p1)) + (3 * t * t * (1 - p2));
    }
}
