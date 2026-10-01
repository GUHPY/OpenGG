using System.Globalization;
using OneRGB.Application.Devices.Input;
using OneRGB.Application.Presentation;

namespace OneRGB.Application.Devices.Keyboard;

/// <summary>
/// Regras do teclado analógico: as de um controle só (usadas pelo <c>ControlSpec.Check</c>) e as cruzadas entre
/// controles (conflitos de atribuição, A6.6), avaliadas pelo editor antes de aplicar e ao salvar um perfil.
/// </summary>
public static class KeyboardRules
{
    public static string? CheckDual(AnalogKeyboardDescriptor device, string code, string text)
    {
        ArgumentNullException.ThrowIfNull(device);
        if (!DualAction.TryParse(text, out var dual, out var error))
        {
            return error;
        }

        if (dual is null)
        {
            return null;
        }

        if (device.Key(code) is not { } key || !key.Has(KeyCapabilities.DualAction))
        {
            return "This key does not support dual action.";
        }

        if (!device.Actuation.Contains(dual.DeepMm))
        {
            return $"Deep threshold is outside the range ({Range(device)}).";
        }

        if (dual.DeepMm < device.Actuation.Minimum + device.DualActionMinimumGap - 1e-9)
        {
            return "The second actuation point must be deeper than the first.";
        }

        if (dual.Second.Kind == InputActionKind.Default)
        {
            return "Choose the second action.";
        }

        return CheckActionFor(device, key, dual.Second, meta: false);
    }

    public static string? CheckBinding(AnalogKeyboardDescriptor device, string code, string text, bool meta)
    {
        ArgumentNullException.ThrowIfNull(device);
        if (!InputAction.TryParse(text, out var action, out var error))
        {
            return error;
        }

        if (device.Key(code) is not { } key)
        {
            return "Unknown key.";
        }

        if (!key.Has(meta ? KeyCapabilities.MetaLayerAssignable : KeyCapabilities.Remappable) && action.Kind != InputActionKind.Default)
        {
            return meta ? "This key does not support a Meta layer action." : "This key cannot be remapped.";
        }

        if (meta && action.Kind == InputActionKind.Layer && action.Value == nameof(LayerAction.Hold))
        {
            return "Use \"toggle\" in the Meta layer to return to the normal layer.";
        }

        return CheckActionFor(device, key, action, meta);
    }

    public static string? CheckPairs(AnalogKeyboardDescriptor device, string text)
    {
        ArgumentNullException.ThrowIfNull(device);
        if (!RapidTapPair.TryParseList(text, out var pairs, out var error))
        {
            return error;
        }

        if (pairs.Count > device.MaximumRapidTapPairs)
        {
            return $"At most {device.MaximumRapidTapPairs} Rapid Tap pairs.";
        }

        var used = new HashSet<string>(StringComparer.Ordinal);
        foreach (var pair in pairs)
        {
            if (string.Equals(pair.First, pair.Second, StringComparison.Ordinal))
            {
                return "A pair needs two different keys.";
            }

            foreach (var code in new[] { pair.First, pair.Second })
            {
                if (device.Key(code) is not { } key)
                {
                    return $"Unknown Rapid Tap key: '{code}'.";
                }

                if (!key.Has(KeyCapabilities.RapidTapParticipant))
                {
                    return $"{key.Label} cannot participate in Rapid Tap.";
                }

                if (!used.Add(code))
                {
                    return $"{key.Label} belongs to more than one pair.";
                }
            }
        }

        return null;
    }

    public static string? CheckProtectedKeys(AnalogKeyboardDescriptor device, string text)
    {
        ArgumentNullException.ThrowIfNull(device);
        var codes = KeyboardConfig.SplitCodes(text);
        if (codes.Distinct(StringComparer.Ordinal).Count() != codes.Count)
        {
            return "Duplicate key in the protected key list.";
        }

        foreach (var code in codes)
        {
            if (device.Key(code) is not { } key)
            {
                return $"Unknown key: '{code}'.";
            }

            if (!key.Has(KeyCapabilities.ProtectionModeParticipant))
            {
                return $"{key.Label} cannot participate in Protection Mode.";
            }
        }

        return null;
    }

    /// <summary>
    /// Teclas que o Protection Mode endurece: as vizinhas das protegidas no layout, menos as próprias protegidas
    /// (WASD protegidas não atrapalham umas às outras numa diagonal).
    /// </summary>
    public static IReadOnlyList<string> AffectedKeys(KeyboardConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        var device = config.Device;
        var protectedKeys = new HashSet<string>(config.Protection.Keys, StringComparer.Ordinal);
        return config.Protection.Keys
            .SelectMany(code => KeyboardLayout.Neighbors(device.Layout, code))
            .Select(k => k.Id)
            .Where(code => !protectedKeys.Contains(code) && device.Key(code)?.Has(KeyCapabilities.ProtectionModeParticipant) == true)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>Todos os problemas da configuração, erros primeiro (lista vazia = pode aplicar).</summary>
    public static IReadOnlyList<BindingConflict> Validate(KeyboardConfig config, InputActionContext? context = null)
    {
        ArgumentNullException.ThrowIfNull(config);
        var device = config.Device;
        var list = new List<BindingConflict>();
        var baseContext = (context ?? new InputActionContext()) with { AllowLayer = device.HasMetaLayer, AllowDpi = false };
        void Error(string control, string message) => list.Add(new BindingConflict(control, ConflictSeverity.Error, message));
        void Warn(string control, string message) => list.Add(new BindingConflict(control, ConflictSeverity.Warning, message));
        string Label(string code) => device.Key(code)?.Label ?? code;

        foreach (var (code, mm) in config.Actuation)
        {
            if (device.Key(code)?.Has(KeyCapabilities.ActuationAdjustable) != true)
            {
                Error(code, $"{Label(code)} has no adjustable actuation.");
            }
            else if (!device.Actuation.Contains(mm))
            {
                Error(code, $"{Label(code)} actuation is outside the range ({Range(device)}).");
            }
        }

        foreach (var (code, rt) in config.RapidTrigger)
        {
            if (device.Key(code)?.Has(KeyCapabilities.RapidTrigger) != true)
            {
                if (rt.Enabled)
                {
                    Error(code, $"{Label(code)} does not support Rapid Trigger.");
                }

                continue;
            }

            if (!device.Sensitivity.Contains(rt.PressMm) || !device.Sensitivity.Contains(rt.ReleaseMm))
            {
                Error(code, $"{Label(code)} Rapid Trigger sensitivity is outside the range.");
            }
        }

        foreach (var (code, action) in config.Remap)
        {
            if (CheckBinding(device, code, action.Encode(), meta: false) is { } problem)
            {
                Error(code, $"{Label(code)}: {problem}");
            }
            else if (InputActions.Validate(action, baseContext) is { } missing)
            {
                Error(code, $"{Label(code)}: {missing}");
            }
        }

        foreach (var (code, action) in config.Meta)
        {
            if (CheckBinding(device, code, action.Encode(), meta: true) is { } problem)
            {
                Error(code, $"{Label(code)} (Meta): {problem}");
            }
            else if (InputActions.Validate(action, baseContext) is { } missing)
            {
                Error(code, $"{Label(code)} (Meta): {missing}");
            }
        }

        // Quem entra na camada Meta alternando precisa de um jeito de sair.
        var toggles = config.Remap.Where(r => r.Value.Kind == InputActionKind.Layer && r.Value.Value == nameof(LayerAction.Toggle)).Select(r => r.Key).ToList();
        var exits = config.Meta.Any(m => m.Value.Kind == InputActionKind.Layer && m.Value.Value == nameof(LayerAction.Toggle));
        foreach (var code in toggles)
        {
            var own = config.Meta.GetValueOrDefault(code);
            if (!exits && own is not null && own.Kind != InputActionKind.Default)
            {
                Error(code, $"{Label(code)} activates the Meta layer, but no action returns to the normal layer.");
            }
        }

        foreach (var (code, dual) in config.Dual)
        {
            if (CheckDual(device, code, dual.Encode()) is { } problem)
            {
                Error(code, $"{Label(code)}: {problem}");
                continue;
            }

            var first = config.Actuation.GetValueOrDefault(code, device.DefaultActuation);
            if (dual.DeepMm < first + device.DualActionMinimumGap - 1e-9)
            {
                Error(code, string.Create(CultureInfo.CurrentCulture,
                    $"{Label(code)}: the deep threshold ({dual.DeepMm:0.0} mm) must be at least {device.DualActionMinimumGap:0.0} mm deeper than the first ({first:0.0} mm)."));
            }

            if (InputActions.Validate(dual.Second, baseContext) is { } missing)
            {
                Error(code, $"{Label(code)}: {missing}");
            }

            if (config.ActionOf(code).Kind == InputActionKind.Disabled)
            {
                Warn(code, $"{Label(code)}: the first action is disabled; only the deep action will work.");
            }
        }

        if (CheckPairs(device, RapidTapPair.EncodeList(config.RapidTapPairs)) is { } pairProblem)
        {
            Error(KeyboardSettings.RapidTapPairs, pairProblem);
        }
        else
        {
            foreach (var pair in config.RapidTapPairs.Where(p => p.Enabled))
            {
                foreach (var code in new[] { pair.First, pair.Second })
                {
                    if (!config.ActionOf(code).EmitsKeys)
                    {
                        Error(code, $"{Label(code)} is in Rapid Tap but is mapped to a non-key action.");
                    }

                    if (config.Dual.ContainsKey(code))
                    {
                        Warn(code, $"{Label(code)} uses dual action and Rapid Tap: Rapid Tap resolves the first action.");
                    }
                }
            }
        }

        foreach (var (code, raw) in config.ProtectionSensitivities)
            if (!double.IsFinite(raw) || raw < 0 || raw > 255 || raw != Math.Truncate(raw))
                Error(KeyboardSettings.ProtectionSensitivity(code), "Protection sensitivity is outside 0–255.");

        if (CheckProtectedKeys(device, string.Join(',', config.Protection.Keys)) is { } protectionProblem)
        {
            Error(KeyboardSettings.ProtectionKeys, protectionProblem);
        }
        else if (config.Protection.Enabled)
        {
            if (config.Protection.Keys.Count == 0)
            {
                Error(KeyboardSettings.ProtectionKeys, "Protection Mode is enabled with no protected keys.");
            }
            else if (AffectedKeys(config).Count == 0)
            {
                Warn(KeyboardSettings.ProtectionKeys, "No neighboring keys can be hardened.");
            }

            if (!device.ProtectionReduction.Contains(config.Protection.ReductionMm))
            {
                Error(KeyboardSettings.ProtectionReduction, "Protection Mode reduction is outside the range.");
            }
        }

        return [.. list.OrderByDescending(c => c.Severity)];
    }

    private static string? CheckActionFor(AnalogKeyboardDescriptor device, KeySwitch key, InputAction action, bool meta)
    {
        if (action.Kind == InputActionKind.Macro && !key.Has(KeyCapabilities.MacroAssignable))
        {
            return "This key does not support macros.";
        }

        if (action.Kind == InputActionKind.Layer && !meta && !device.HasMetaLayer)
        {
            return "This keyboard has no Meta layer.";
        }

        return InputActions.Validate(action, new InputActionContext { AllowLayer = device.HasMetaLayer });
    }

    private static string Range(AnalogKeyboardDescriptor device) => string.Create(CultureInfo.CurrentCulture,
        $"{device.Actuation.Minimum:0.0}–{device.Actuation.Maximum:0.0} mm, steps of {device.Actuation.Step:0.0}");
}
