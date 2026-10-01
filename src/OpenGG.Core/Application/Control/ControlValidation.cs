using System.Globalization;

namespace OneRGB.Application.Control;

/// <summary>
/// Validação de um valor contra o <see cref="ControlSpec"/> (A26: valor exato, passo, faixa, envelope de segurança e
/// regra do domínio). Vale com ou sem dispositivo conectado: o editor de perfil e os modelos de dispositivo usam o
/// mesmo critério que o <see cref="ControlService"/> aplica antes de escrever.
/// </summary>
public static class ControlValidation
{
    /// <summary>Tamanho máximo de um valor de texto (textos estruturados incluídos).</summary>
    public const int MaximumTextLength = 512;

    /// <summary>
    /// Devolve o motivo da recusa, ou nulo. <paramref name="normalized"/> recebe o valor ajustado ao passo do
    /// dispositivo. <paramref name="safety"/> é o escritor de segurança, o único que passa fora do envelope.
    /// </summary>
    public static string? Validate(ControlSpec spec, ControlValue value, bool safety, out ControlValue normalized)
    {
        ArgumentNullException.ThrowIfNull(spec);
        ArgumentNullException.ThrowIfNull(value);
        normalized = value;
        if (!spec.Writable)
        {
            return "This control is read only.";
        }

        var error = Shape(spec, value, safety, ref normalized);
        return error ?? spec.Check?.Invoke(normalized);
    }

    private static string? Shape(ControlSpec spec, ControlValue value, bool safety, ref ControlValue normalized)
    {
        var provider = CultureInfo.CurrentCulture;
        string Range(double min, double max) => $"{new NumberValue(min).Display(spec, provider)} a {new NumberValue(max).Display(spec, provider)}";

        switch (spec.Kind, value)
        {
            case (ControlKind.Number, NumberValue number):
                if (!double.IsFinite(number.Value))
                {
                    return "Invalid value.";
                }

                var v = number.Value;
                if (spec.Step is > 0 and var step)
                {
                    var origin = spec.Minimum ?? 0;
                    v = Math.Round(origin + (Math.Round((v - origin) / step) * step), 10);
                }

                if ((spec.Minimum is { } min && v < min - 1e-9) || (spec.Maximum is { } max && v > max + 1e-9))
                {
                    return $"Outside the device range ({Range(spec.Minimum ?? double.MinValue, spec.Maximum ?? double.MaxValue)}).";
                }

                if (!safety && ((spec.SafeMinimum is { } safeMin && v < safeMin) || (spec.SafeMaximum is { } safeMax && v > safeMax)))
                {
                    return $"Outside the safety limits ({Range(spec.SafeMinimum ?? spec.Minimum ?? 0, spec.SafeMaximum ?? spec.Maximum ?? 0)}).";
                }

                normalized = new NumberValue(v);
                return null;
            case (ControlKind.Toggle, ToggleValue):
            case (ControlKind.Color, ColorValue):
                return null;
            case (ControlKind.Choice, ChoiceValue choice):
                return spec.Choices.Count == 0 || spec.Choices.Any(c => string.Equals(c.Value, choice.Value, StringComparison.Ordinal))
                    ? null
                    : $"Option '{choice.Value}' is unavailable on this device.";
            case (ControlKind.Text, TextValue text):
                return text.Value.Length <= MaximumTextLength ? null : $"Text is too long (maximum {MaximumTextLength} characters).";
            case (ControlKind.Curve, CurveValue curve):
                if (curve.Points.Count < 2)
                {
                    return "The curve needs at least two points.";
                }

                for (var i = 0; i < curve.Points.Count; i++)
                {
                    var p = curve.Points[i];
                    if (!double.IsFinite(p.X) || !double.IsFinite(p.Y) || (i > 0 && p.X <= curve.Points[i - 1].X))
                    {
                        return "Curve points must be in ascending X order.";
                    }

                    if ((spec.XMinimum is { } xMin && p.X < xMin) || (spec.XMaximum is { } xMax && p.X > xMax)
                        || (spec.Minimum is { } yMin && p.Y < yMin) || (spec.Maximum is { } yMax && p.Y > yMax))
                    {
                        return "Curve point is outside the device range.";
                    }

                    if (!safety && spec.SafeMinimum is { } floor && p.Y < floor)
                    {
                        return $"The curve falls below the safety floor ({new NumberValue(floor).Display(spec, provider)}).";
                    }
                }

                return null;
            default:
                return "Value type is incompatible with this control.";
        }
    }
}

/// <summary>Um controle conhecido de um dispositivo (chave + descrição), antes mesmo de o adapter existir.</summary>
public sealed record DeviceControl(ControlKey Key, ControlSpec Spec);
