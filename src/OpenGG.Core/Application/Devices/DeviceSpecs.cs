using OneRGB.Application.Control;

namespace OneRGB.Application.Devices;

/// <summary>
/// Faixa exata com passo (A26): 0,1–4,0 mm em passos de 0,1 são 40 níveis, e é isso que a interface mostra — nunca
/// "rápido/médio/lento".
/// </summary>
public sealed record StepRange(double Minimum, double Maximum, double Step)
{
    public int Levels => (int)Math.Round((Maximum - Minimum) / Step) + 1;

    /// <summary>Valor mais próximo que o dispositivo aceita.</summary>
    public double Snap(double value) =>
        Math.Clamp(Math.Round(Minimum + (Math.Round((value - Minimum) / Step) * Step), 10), Minimum, Maximum);

    /// <summary>Dentro da faixa e exatamente num passo.</summary>
    public bool Contains(double value) =>
        double.IsFinite(value) && value >= Minimum - 1e-9 && value <= Maximum + 1e-9 && Math.Abs(Snap(value) - value) < 1e-6;

    /// <summary>Nível 1..<see cref="Levels"/> do valor.</summary>
    public int LevelOf(double value) => (int)Math.Round((Snap(value) - Minimum) / Step) + 1;

    public double ValueOf(int level) => Snap(Minimum + ((Math.Clamp(level, 1, Levels) - 1) * Step));
}

/// <summary>Atalhos para montar <see cref="ControlSpec"/> dos modelos de dispositivo com o mesmo padrão.</summary>
public static class DeviceSpecs
{
    public static ControlSpec Number(string setting, string label, string group, StepRange range, string unit, double @default, string source, string? evidence = null, int? decimals = null) => new()
    {
        Setting = setting,
        Label = label,
        Group = group,
        Kind = ControlKind.Number,
        Minimum = range.Minimum,
        Maximum = range.Maximum,
        Step = range.Step,
        Unit = unit,
        Decimals = decimals ?? DecimalsOf(range.Step),
        Default = new NumberValue(range.Snap(@default)),
        Source = source,
        Evidence = evidence,
    };

    public static ControlSpec Toggle(string setting, string label, string group, bool @default, string source, string? evidence = null) => new()
    {
        Setting = setting,
        Label = label,
        Group = group,
        Kind = ControlKind.Toggle,
        Default = new ToggleValue(@default),
        Source = source,
        Evidence = evidence,
    };

    public static ControlSpec Choice(string setting, string label, string group, IReadOnlyList<ChoiceOption> choices, string @default, string source, string? evidence = null) => new()
    {
        Setting = setting,
        Label = label,
        Group = group,
        Kind = ControlKind.Choice,
        Choices = choices,
        Default = new ChoiceValue(@default),
        Source = source,
        Evidence = evidence,
    };

    public static ControlSpec Text(string setting, string label, string group, string @default, string source, Func<ControlValue, string?> check, Func<TextValue, IFormatProvider, string> format, string? evidence = null) => new()
    {
        Setting = setting,
        Label = label,
        Group = group,
        Kind = ControlKind.Text,
        Default = new TextValue(@default),
        Source = source,
        Check = check,
        Format = format,
        Evidence = evidence,
    };

    /// <summary>Casas decimais que o passo pede (0,1 → 1; 0,05 → 2; 1 → 0).</summary>
    public static int DecimalsOf(double step)
    {
        for (var decimals = 0; decimals < 6; decimals++)
        {
            if (Math.Abs((step * Math.Pow(10, decimals)) - Math.Round(step * Math.Pow(10, decimals))) < 1e-9)
            {
                return decimals;
            }
        }

        return 6;
    }
}
