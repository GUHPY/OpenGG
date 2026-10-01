using System.Globalization;

namespace OneRGB.Application.Presentation;

/// <summary>
/// Regras dos campos numéricos (spec §73): passo com modificadores, arrasto no rótulo (porte do conceito de
/// microkit/scrub-number-field, MIT), digitação com vírgula ou ponto, limites e valor exato sempre visível.
/// </summary>
public sealed record NumericRange(double Minimum, double Maximum, double Step)
{
    /// <summary>Pixels de arrasto por passo (microkit usa 2).</summary>
    public const double PixelsPerStep = 2;

    /// <summary>Deslocamento mínimo para um clique virar arrasto.</summary>
    public const double DragThreshold = 3;

    public int Decimals => DecimalsOf(Step);

    /// <summary>Passo com modificadores: Shift = ×10, Alt = ÷10 (as duas velocidades de ferramentas de design).</summary>
    public double StepFor(bool shift, bool alt) => shift ? Step * 10 : alt ? Step / 10 : Step;

    /// <summary>Casas para o passo efetivo (Alt mostra uma casa a mais).</summary>
    public int DecimalsFor(bool alt) => alt ? DecimalsOf(Step / 10) : Decimals;

    /// <summary>Limita à faixa e arredonda ao passo a partir do mínimo (o hardware só aceita valores do passo).</summary>
    public double Snap(double value, double? step = null)
    {
        if (double.IsNaN(value))
        {
            return Minimum;
        }

        var s = step ?? Step;
        var clamped = Math.Clamp(value, Minimum, Maximum);
        if (s <= 0)
        {
            return clamped;
        }

        var snapped = Minimum + (Math.Round((clamped - Minimum) / s, MidpointRounding.AwayFromZero) * s);
        return Math.Round(Math.Clamp(snapped, Minimum, Maximum), Math.Max(DecimalsOf(s), 0) + 2);
    }

    /// <summary>Qual limite o valor pedido ultrapassou (para o "empurrão" visual no limite).</summary>
    public int Bound(double wanted) => wanted > Maximum + 1e-9 ? 1 : wanted < Minimum - 1e-9 ? -1 : 0;

    /// <summary>Valor durante o arrasto: base + passos inteiros percorridos.</summary>
    public double Scrub(double baseValue, double travelPixels, bool shift, bool alt) =>
        Snap(baseValue + (Math.Round(travelPixels / PixelsPerStep) * StepFor(shift, alt)), GridFor(alt));

    /// <summary>Uma tecla de seta: ±passo com os modificadores.</summary>
    public double Nudge(double value, int direction, bool shift, bool alt) =>
        Snap(value + (Math.Sign(direction) * StepFor(shift, alt)), GridFor(alt));

    /// <summary>
    /// Grade em que o valor cai. Alt usa o passo fino; Shift só anda mais rápido e continua no passo normal, para que
    /// apertar Shift no meio do arrasto não faça o valor pular sem o mouse sair do lugar.
    /// </summary>
    private double GridFor(bool alt) => alt ? Step / 10 : Step;

    public string Format(double value, IFormatProvider provider, bool alt = false) =>
        value.ToString("F" + DecimalsFor(alt).ToString(CultureInfo.InvariantCulture), provider);

    /// <summary>
    /// Lê o que o usuário digitou aceitando vírgula ou ponto como decimal, sinal e espaços; o separador de milhar não é
    /// aceito (ambíguo em pt-BR). Devolve falso para texto vazio ou inválido: o campo volta ao valor anterior.
    /// </summary>
    public static bool TryParse(string? text, out double value)
    {
        value = 0;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var cleaned = text.Trim().Replace(" ", string.Empty, StringComparison.Ordinal).Replace('−', '-');
        if (cleaned.Count(c => c is ',' or '.') > 1)
        {
            return false;
        }

        cleaned = cleaned.Replace(',', '.');
        return double.TryParse(cleaned, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out value)
            && double.IsFinite(value);
    }

    public static int DecimalsOf(double step)
    {
        if (step <= 0 || double.IsNaN(step))
        {
            return 0;
        }

        for (var decimals = 0; decimals <= 6; decimals++)
        {
            var scaled = step * Math.Pow(10, decimals);
            if (Math.Abs(scaled - Math.Round(scaled)) < 1e-9 * Math.Max(1, scaled))
            {
                return decimals;
            }
        }

        return 6;
    }
}

/// <summary>Régua da <c>RulerSlider</c> (porte de beui/range-slider-ruler, MIT): marcas por passo, maiores a cada N.</summary>
public static class Ruler
{
    public readonly record struct Tick(double Value, bool Major, double Offset);

    /// <summary>
    /// Marcas da régua com <paramref name="gap"/> pixels por passo. A faixa não precisa ser múltipla do passo: o máximo
    /// ganha marca própria, para a régua nunca passar do valor que o controle aceita. Faixas longas são rarefeitas
    /// (no máximo <paramref name="maxTicks"/> marcas) mantendo as maiores.
    /// </summary>
    public static IReadOnlyList<Tick> Ticks(double minimum, double maximum, double step, int majorEvery, double gap, int maxTicks = 400)
    {
        if (step <= 0 || maximum <= minimum)
        {
            return [new Tick(minimum, true, 0)];
        }

        var span = Math.Round((maximum - minimum) / step, 6);
        var whole = (int)Math.Floor(span);
        var every = Math.Max(1, majorEvery);
        var stride = MinorStride(whole, every, Math.Max(maxTicks, 2));
        var ticks = new List<Tick>(Math.Min(whole + 2, maxTicks + 2));
        for (var i = 0; i <= whole; i++)
        {
            var major = i % every == 0;
            if (!major && i % stride != 0)
            {
                continue;
            }

            ticks.Add(new Tick(Math.Round(minimum + (i * step), 6), major, i * gap));
        }

        if (span - whole > 1e-6)
        {
            ticks.Add(new Tick(maximum, true, span * gap));
        }

        return ticks;
    }

    /// <summary>
    /// De quantos em quantos passos desenhar as marcas menores: o menor divisor do intervalo das maiores que cabe em
    /// <paramref name="maxTicks"/>, para a régua rarefeita continuar regular. Se nem as maiores cabem, só elas ficam.
    /// </summary>
    private static int MinorStride(int whole, int every, int maxTicks)
    {
        for (var stride = 1; stride < every; stride++)
        {
            if (every % stride == 0 && (whole / stride) + 1 <= maxTicks)
            {
                return stride;
            }
        }

        return every;
    }

    /// <summary>Valor sob a agulha para um deslocamento da régua (em pixels a partir do mínimo), já no passo.</summary>
    public static double ValueAt(double offsetPixels, double minimum, double maximum, double step, double gap) =>
        new NumericRange(minimum, maximum, step).Snap(minimum + (offsetPixels / Math.Max(gap, 1e-6) * step));

    /// <summary>Deslocamento da régua para mostrar o valor sob a agulha.</summary>
    public static double OffsetOf(double value, double minimum, double step, double gap) =>
        step <= 0 ? 0 : (value - minimum) / step * gap;
}
