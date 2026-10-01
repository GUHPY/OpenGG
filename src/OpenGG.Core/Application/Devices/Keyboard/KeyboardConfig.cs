using System.Globalization;
using OneRGB.Application.Control;
using OneRGB.Application.Devices.Input;
using OneRGB.Application.Presentation;

namespace OneRGB.Application.Devices.Keyboard;

/// <summary>Par do Rapid Tap (A5.8). Texto canônico: <c>KeyA-KeyD:last</c>, com <c>:off</c> quando desligado.</summary>
public sealed record RapidTapPair(string First, string Second, RapidTapMode Mode = RapidTapMode.LastInput, bool Enabled = true)
{
    public bool Contains(string code) =>
        string.Equals(First, code, StringComparison.Ordinal) || string.Equals(Second, code, StringComparison.Ordinal);

    public string Encode() => $"{First}-{Second}:{ModeText(Mode)}{(Enabled ? string.Empty : ":off")}";

    public static string EncodeList(IEnumerable<RapidTapPair> pairs) => string.Join(';', pairs.Select(p => p.Encode()));

    public static bool TryParseList(string? text, out IReadOnlyList<RapidTapPair> pairs, out string? error)
    {
        var list = new List<RapidTapPair>();
        pairs = list;
        error = null;
        foreach (var part in (text ?? string.Empty).Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var fields = part.Split(':');
            var keys = fields[0].Split('-');
            if (keys.Length != 2 || keys.Any(string.IsNullOrWhiteSpace) || fields.Length > 3)
            {
                error = $"Invalid pair: '{part}'.";
                return false;
            }

            var mode = RapidTapMode.LastInput;
            if (fields.Length >= 2)
            {
                if (ParseMode(fields[1]) is not { } parsed)
                {
                    error = $"Unknown mode in pair '{part}'.";
                    return false;
                }

                mode = parsed;
            }

            if (fields.Length == 3 && !string.Equals(fields[2], "off", StringComparison.Ordinal))
            {
                error = $"Invalid pair: '{part}'.";
                return false;
            }

            list.Add(new RapidTapPair(keys[0].Trim(), keys[1].Trim(), mode, fields.Length != 3));
        }

        return true;
    }

    public static string Describe(IReadOnlyList<RapidTapPair> pairs) => pairs.Count == 0
        ? "No pairs"
        : string.Join("; ", pairs.Select(p =>
            $"{Label(p.First)}↔{Label(p.Second)} ({ModeLabel(p.Mode)}){(p.Enabled ? string.Empty : ", disabled")}"));

    public static string ModeLabel(RapidTapMode mode) => mode switch
    {
        RapidTapMode.LastInput => "last input wins",
        RapidTapMode.FirstKey => "first key wins",
        RapidTapMode.SecondKey => "second key wins",
        _ => "neutral",
    };

    private static string Label(string code) => KeyboardLayout.Find(code)?.Label ?? code;

    private static string ModeText(RapidTapMode mode) => mode switch
    {
        RapidTapMode.LastInput => "last",
        RapidTapMode.FirstKey => "first",
        RapidTapMode.SecondKey => "second",
        _ => "neutral",
    };

    private static RapidTapMode? ParseMode(string text) => text switch
    {
        "last" => RapidTapMode.LastInput,
        "first" => RapidTapMode.FirstKey,
        "second" => RapidTapMode.SecondKey,
        "neutral" => RapidTapMode.Neutral,
        _ => null,
    };
}

/// <summary>
/// Tecla 2-em-1 (A5.9): a ação A (a função da tecla) acontece no ponto de atuação dela; a ação B no ponto fundo.
/// Texto canônico: <c>3.6|key:ShiftLeft+KeyW|hold</c>. <see cref="KeepFirst"/> mantém A pressionada junto com B
/// (andar + correr); desligado, B substitui A.
/// </summary>
public sealed record DualAction(double DeepMm, InputAction Second, bool KeepFirst = true)
{
    public string Encode() => string.Create(CultureInfo.InvariantCulture, $"{DeepMm:0.###}|{Second.Encode()}|{(KeepFirst ? "hold" : "switch")}");

    /// <summary>Texto vazio = tecla sem 2-em-1 (<paramref name="action"/> nulo).</summary>
    public static bool TryParse(string? text, out DualAction? action, out string? error)
    {
        action = null;
        error = null;
        if (string.IsNullOrWhiteSpace(text))
        {
            return true;
        }

        var parts = text.Split('|');
        if (parts.Length != 3 || !double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var deep) || !double.IsFinite(deep))
        {
            error = "Invalid dual-action key.";
            return false;
        }

        if (parts[2] is not ("hold" or "switch"))
        {
            error = "Invalid dual-action mode.";
            return false;
        }

        if (!InputAction.TryParse(parts[1], out var second, out error))
        {
            return false;
        }

        action = new DualAction(deep, second, parts[2] == "hold");
        return true;
    }

    public string Describe(IFormatProvider? provider = null) =>
        string.Create(provider ?? CultureInfo.CurrentCulture, $"{DeepMm:0.0} mm → {Second.Describe()}{(KeepFirst ? " (keeps the first)" : " (replaces)")}");
}

/// <summary>Rapid Trigger de uma tecla (A5.6).</summary>
public sealed record KeyRapidTrigger(bool Enabled, double PressMm, double ReleaseMm);

/// <summary>Protection Mode (A5.7): teclas protegidas, quanto endurece as vizinhas e quando.</summary>
public sealed record ProtectionSettings(bool Enabled, IReadOnlyList<string> Keys, double ReductionMm, ProtectionActivation Activation);

/// <summary>Um jeito pronto de configurar o teclado (dados do OneRGB, não do fabricante).</summary>
public sealed record KeyboardPreset(string Id, string Name, string Description, Action<KeyboardConfig> Apply);

/// <summary>
/// A configuração inteira do teclado montada a partir dos controles por tecla: é o que o editor manipula
/// (atuação em grupo, reset, presets) e o que as regras cruzadas, o simulador e a tela OLED leem. Volta para
/// controles com <see cref="ToValues"/>.
/// </summary>
public sealed class KeyboardConfig
{
    private KeyboardConfig(AnalogKeyboardDescriptor device)
    {
        Device = device;
        Protection = new ProtectionSettings(false, KeyboardLayout.Wasd, device.DefaultProtectionReduction, ProtectionActivation.WhileProtectedKeyHeld);
    }

    public AnalogKeyboardDescriptor Device { get; }

    /// <summary>Ponto de atuação por tecla ajustável.</summary>
    public Dictionary<string, double> Actuation { get; } = new(StringComparer.Ordinal);

    public Dictionary<string, KeyRapidTrigger> RapidTrigger { get; } = new(StringComparer.Ordinal);

    /// <summary>Habilita os interruptores de Rapid Trigger selecionados por tecla.</summary>
    public bool RapidTriggerEnabled { get; set; }

    /// <summary>Só as teclas com 2-em-1 ligada.</summary>
    public Dictionary<string, DualAction> Dual { get; } = new(StringComparer.Ordinal);

    /// <summary>Só as teclas com função diferente da original.</summary>
    public Dictionary<string, InputAction> Remap { get; } = new(StringComparer.Ordinal);

    public Dictionary<string, InputAction> Meta { get; } = new(StringComparer.Ordinal);

    public List<RapidTapPair> RapidTapPairs { get; } = [];

    public ProtectionSettings Protection { get; set; }
    public Dictionary<string, double> ProtectionSensitivities { get; } = new(StringComparer.Ordinal);

    public static KeyboardConfig Defaults(AnalogKeyboardDescriptor device)
    {
        ArgumentNullException.ThrowIfNull(device);
        var config = new KeyboardConfig(device);
        foreach (var key in device.With(KeyCapabilities.ActuationAdjustable))
        {
            config.Actuation[key.Code] = device.DefaultActuation;
            config.ProtectionSensitivities[key.Code] = 20;
        }

        foreach (var key in device.With(KeyCapabilities.RapidTrigger))
        {
            config.RapidTrigger[key.Code] = new KeyRapidTrigger(false, device.DefaultSensitivity, device.DefaultSensitivity);
        }

        return config;
    }

    /// <summary>
    /// Monta a partir de valores (perfil resolvido, estado dos controles). Valor ausente ou ilegível fica no padrão;
    /// a validação dos valores em si é das regras (<see cref="KeyboardRules.Validate"/>).
    /// </summary>
    public static KeyboardConfig From(AnalogKeyboardDescriptor device, Func<ControlKey, ControlValue?> valueOf)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(valueOf);
        var config = Defaults(device);
        var actuation = device.ActuationResource;
        var keys = device.KeysResource;
        config.RapidTriggerEnabled = valueOf(new ControlKey(actuation, KeyboardSettings.RapidTriggerEnabled)) is ToggleValue keyboardRt && keyboardRt.Value;
        foreach (var key in device.Keys)
        {
            var code = key.Code;
            if (key.Has(KeyCapabilities.ActuationAdjustable) && valueOf(new ControlKey(actuation, KeyboardSettings.Actuation(code))) is NumberValue mm)
            {
                config.Actuation[code] = mm.Value;
            }
            if (key.Has(KeyCapabilities.ActuationAdjustable) && valueOf(new ControlKey(actuation, KeyboardSettings.ProtectionSensitivity(code))) is NumberValue raw)
                config.ProtectionSensitivities[code] = raw.Value;

            if (key.Has(KeyCapabilities.RapidTrigger))
            {
                var current = config.RapidTrigger[code];
                var enabled = valueOf(new ControlKey(actuation, KeyboardSettings.RapidTrigger(code))) is ToggleValue on ? on.Value : current.Enabled;
                double press, release;
                if (device.SeparateSensitivities)
                {
                    press = valueOf(new ControlKey(actuation, KeyboardSettings.Press(code))) is NumberValue p ? p.Value : current.PressMm;
                    release = valueOf(new ControlKey(actuation, KeyboardSettings.Release(code))) is NumberValue r ? r.Value : current.ReleaseMm;
                }
                else
                {
                    press = release = valueOf(new ControlKey(actuation, KeyboardSettings.Sensitivity(code))) is NumberValue s ? s.Value : current.PressMm;
                }

                config.RapidTrigger[code] = new KeyRapidTrigger(enabled, press, release);
            }

            if (key.Has(KeyCapabilities.DualAction) && valueOf(new ControlKey(actuation, KeyboardSettings.Dual(code))) is TextValue dualText
                && DualAction.TryParse(dualText.Value, out var dual, out _) && dual is not null)
            {
                config.Dual[code] = dual;
            }

            if (key.Has(KeyCapabilities.Remappable) && valueOf(new ControlKey(keys, KeyboardSettings.Remap(code))) is TextValue remapText
                && InputAction.TryParse(remapText.Value, out var remap, out _) && remap.Kind != InputActionKind.Default)
            {
                config.Remap[code] = remap;
            }

            if (device.HasMetaLayer && key.Has(KeyCapabilities.MetaLayerAssignable) && valueOf(new ControlKey(keys, KeyboardSettings.Meta(code))) is TextValue metaText
                && InputAction.TryParse(metaText.Value, out var meta, out _) && meta.Kind != InputActionKind.Default)
            {
                config.Meta[code] = meta;
            }
        }

        if (valueOf(new ControlKey(actuation, KeyboardSettings.RapidTapPairs)) is TextValue pairsText && RapidTapPair.TryParseList(pairsText.Value, out var pairs, out _))
        {
            config.RapidTapPairs.AddRange(pairs);
        }

        var protection = config.Protection;
        config.Protection = new ProtectionSettings(
            valueOf(new ControlKey(actuation, KeyboardSettings.ProtectionEnabled)) is ToggleValue enabledValue ? enabledValue.Value : protection.Enabled,
            valueOf(new ControlKey(actuation, KeyboardSettings.ProtectionKeys)) is TextValue keysText ? SplitCodes(keysText.Value) : protection.Keys,
            valueOf(new ControlKey(actuation, KeyboardSettings.ProtectionReduction)) is NumberValue reduction ? reduction.Value : protection.ReductionMm,
            valueOf(new ControlKey(actuation, KeyboardSettings.ProtectionActivation)) is ChoiceValue activation
                && Enum.TryParse<ProtectionActivation>(activation.Value, out var parsedActivation) ? parsedActivation : protection.Activation);
        return config;
    }

    /// <summary>Todos os controles com o valor desta configuração (para salvar no perfil ou aplicar em lote).</summary>
    public IReadOnlyDictionary<ControlKey, ControlValue> ToValues()
    {
        var values = new Dictionary<ControlKey, ControlValue>();
        var actuation = Device.ActuationResource;
        var keys = Device.KeysResource;
        values[new ControlKey(actuation, KeyboardSettings.RapidTriggerEnabled)] = new ToggleValue(RapidTriggerEnabled);
        foreach (var key in Device.Keys)
        {
            var code = key.Code;
            if (key.Has(KeyCapabilities.ActuationAdjustable))
            {
                values[new ControlKey(actuation, KeyboardSettings.Actuation(code))] = new NumberValue(Actuation.GetValueOrDefault(code, Device.DefaultActuation));
                values[new ControlKey(actuation, KeyboardSettings.ProtectionSensitivity(code))] = new NumberValue(ProtectionSensitivities.GetValueOrDefault(code, 20));
            }

            if (key.Has(KeyCapabilities.RapidTrigger))
            {
                var rt = RapidTrigger.GetValueOrDefault(code) ?? new KeyRapidTrigger(false, Device.DefaultSensitivity, Device.DefaultSensitivity);
                values[new ControlKey(actuation, KeyboardSettings.RapidTrigger(code))] = new ToggleValue(rt.Enabled);
                if (Device.SeparateSensitivities)
                {
                    values[new ControlKey(actuation, KeyboardSettings.Press(code))] = new NumberValue(rt.PressMm);
                    values[new ControlKey(actuation, KeyboardSettings.Release(code))] = new NumberValue(rt.ReleaseMm);
                }
                else
                {
                    values[new ControlKey(actuation, KeyboardSettings.Sensitivity(code))] = new NumberValue(rt.PressMm);
                }
            }

            if (key.Has(KeyCapabilities.DualAction))
            {
                values[new ControlKey(actuation, KeyboardSettings.Dual(code))] = new TextValue(Dual.TryGetValue(code, out var dual) ? dual.Encode() : string.Empty);
            }

            if (key.Has(KeyCapabilities.Remappable))
            {
                values[new ControlKey(keys, KeyboardSettings.Remap(code))] = new TextValue((Remap.GetValueOrDefault(code) ?? InputAction.Default).Encode());
            }

            if (Device.HasMetaLayer && key.Has(KeyCapabilities.MetaLayerAssignable))
            {
                values[new ControlKey(keys, KeyboardSettings.Meta(code))] = new TextValue((Meta.GetValueOrDefault(code) ?? InputAction.Default).Encode());
            }
        }

        values[new ControlKey(actuation, KeyboardSettings.RapidTapPairs)] = new TextValue(RapidTapPair.EncodeList(RapidTapPairs));
        values[new ControlKey(actuation, KeyboardSettings.ProtectionEnabled)] = new ToggleValue(Protection.Enabled);
        values[new ControlKey(actuation, KeyboardSettings.ProtectionKeys)] = new TextValue(string.Join(',', Protection.Keys));
        values[new ControlKey(actuation, KeyboardSettings.ProtectionReduction)] = new NumberValue(Protection.ReductionMm);
        values[new ControlKey(actuation, KeyboardSettings.ProtectionActivation)] = new ChoiceValue(Protection.Activation.ToString());
        return values;
    }

    /// <summary>Atuação em grupo (A5.5): só teclas ajustáveis; o valor vai para o passo do teclado. Devolve quantas mudaram.</summary>
    public int SetActuation(IEnumerable<string> codes, double millimeters)
    {
        ArgumentNullException.ThrowIfNull(codes);
        var value = Device.Actuation.Snap(millimeters);
        var changed = 0;
        foreach (var code in codes)
        {
            if (Device.Key(code)?.Has(KeyCapabilities.ActuationAdjustable) == true && Actuation.GetValueOrDefault(code) != value)
            {
                Actuation[code] = value;
                changed++;
            }
        }

        return changed;
    }

    /// <summary>Reset global (A5.5): todas as teclas ajustáveis no ponto padrão.</summary>
    public int ResetActuation() => SetActuation(Actuation.Keys.ToList(), Device.DefaultActuation);

    public int SetRapidTrigger(IEnumerable<string> codes, bool enabled, double? pressMm = null, double? releaseMm = null)
    {
        ArgumentNullException.ThrowIfNull(codes);
        var changed = 0;
        foreach (var code in codes)
        {
            if (Device.Key(code)?.Has(KeyCapabilities.RapidTrigger) != true)
            {
                continue;
            }

            if (enabled) { RapidTriggerEnabled = true; }

            var current = RapidTrigger.GetValueOrDefault(code) ?? new KeyRapidTrigger(false, Device.DefaultSensitivity, Device.DefaultSensitivity);
            var press = pressMm is { } p ? Device.Sensitivity.Snap(p) : current.PressMm;
            var release = Device.SeparateSensitivities ? (releaseMm is { } r ? Device.Sensitivity.Snap(r) : current.ReleaseMm) : press;
            var next = new KeyRapidTrigger(enabled, press, release);
            if (next != current)
            {
                RapidTrigger[code] = next;
                changed++;
            }
        }

        return changed;
    }

    public KeyboardConfig Clone()
    {
        var copy = new KeyboardConfig(Device) { Protection = Protection, RapidTriggerEnabled = RapidTriggerEnabled };
        foreach (var (code, raw) in ProtectionSensitivities) { copy.ProtectionSensitivities[code] = raw; }
        foreach (var (code, value) in Actuation)
        {
            copy.Actuation[code] = value;
        }

        foreach (var (code, value) in RapidTrigger)
        {
            copy.RapidTrigger[code] = value;
        }

        foreach (var (code, value) in Dual)
        {
            copy.Dual[code] = value;
        }

        foreach (var (code, value) in Remap)
        {
            copy.Remap[code] = value;
        }

        foreach (var (code, value) in Meta)
        {
            copy.Meta[code] = value;
        }

        copy.RapidTapPairs.AddRange(RapidTapPairs);
        return copy;
    }

    /// <summary>Ação A de uma tecla (a função dela na camada normal).</summary>
    public InputAction ActionOf(string code) => Remap.GetValueOrDefault(code) ?? InputAction.Default;

    public static IReadOnlyList<string> SplitCodes(string? text) =>
        (text ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    /// <summary>
    /// Presets do OneRGB. Nenhum liga o Rapid Tap: alguns jogos competitivos proíbem SOCD (ex.: CS2 em servidores
    /// oficiais), então ele só entra por escolha explícita, de preferência no perfil do jogo.
    /// </summary>
    public static IReadOnlyList<KeyboardPreset> Presets { get; } =
    [
        new("typing", "Typing", "Default actuation on all keys, Rapid Trigger off.", config =>
        {
            config.ResetActuation();
            config.RapidTriggerEnabled = false;
            config.SetRapidTrigger(config.RapidTrigger.Keys.ToList(), enabled: false);
        }),
        new("balanced", "Balanced", "1.2 mm on all keys; 0.3 mm Rapid Trigger on WASD.", config =>
        {
            config.SetActuation(config.Actuation.Keys.ToList(), 1.2);
            config.RapidTriggerEnabled = true;
            config.SetRapidTrigger(config.RapidTrigger.Keys.ToList(), enabled: false);
            config.SetRapidTrigger(KeyboardLayout.Wasd, enabled: true, pressMm: 0.3, releaseMm: 0.3);
        }),
        new("competitive", "Competitive", "0.6 mm actuation and 0.2 mm Rapid Trigger on WASD; 1.5 mm elsewhere to reduce accidental presses.", config =>
        {
            config.SetActuation(config.Actuation.Keys.ToList(), 1.5);
            config.SetActuation(KeyboardLayout.Wasd, 0.6);
            config.RapidTriggerEnabled = true;
            config.SetRapidTrigger(config.RapidTrigger.Keys.ToList(), enabled: false);
            config.SetRapidTrigger(KeyboardLayout.Wasd, enabled: true, pressMm: 0.2, releaseMm: 0.2);
        }),
    ];
}
