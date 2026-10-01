using System.Globalization;
using OneRGB.Application.Control;
using OneRGB.Application.Devices.Input;
using OneRGB.Application.Presentation;

namespace OneRGB.Application.Devices.Keyboard;

/// <summary>O que uma tecla física sabe fazer (A5.4). A interface só mostra o que a tecla tem.</summary>
[Flags]
public enum KeyCapabilities
{
    None = 0,
    DigitalInput = 1 << 0,
    Remappable = 1 << 1,
    MacroAssignable = 1 << 2,
    DualAction = 1 << 3,
    ActuationAdjustable = 1 << 4,
    RapidTrigger = 1 << 5,
    RapidTapParticipant = 1 << 6,
    ProtectionModeParticipant = 1 << 7,
    RgbAssignable = 1 << 8,
    MetaLayerAssignable = 1 << 9,

    /// <summary>Chave magnética ajustável (OmniPoint/Hall): todos os recursos analógicos.</summary>
    Analog = DigitalInput | Remappable | MacroAssignable | DualAction | ActuationAdjustable | RapidTrigger
        | RapidTapParticipant | ProtectionModeParticipant | RgbAssignable | MetaLayerAssignable,

    /// <summary>Chave de ponto fixo: liga/desliga, atribuições e RGB.</summary>
    Fixed = DigitalInput | Remappable | MacroAssignable | RgbAssignable | MetaLayerAssignable,
}

public enum SwitchType
{
    /// <summary>Magnética com leitura analógica (ponto de atuação ajustável).</summary>
    Adjustable,

    /// <summary>Ponto de atuação fixo.</summary>
    Fixed,
}

public sealed record KeySwitch(string Code, string Label, SwitchType Switch, KeyCapabilities Capabilities)
{
    public bool Has(KeyCapabilities capability) => (Capabilities & capability) == capability;
}

/// <summary>Priority quando as duas teclas de um par do Rapid Tap estão pressionadas (A5.8).</summary>
public enum RapidTapMode
{
    /// <summary>A última pressionada vence; soltá-la devolve a outra (Rapid Tap / SOCD "last input").</summary>
    LastInput,

    /// <summary>A primeira tecla do par sempre vence.</summary>
    FirstKey,

    /// <summary>A segunda tecla do par sempre vence.</summary>
    SecondKey,

    /// <summary>As duas se anulam.</summary>
    Neutral,
}

/// <summary>Quando o Protection Mode endurece as teclas em volta das protegidas (A5.7).</summary>
public enum ProtectionActivation
{
    /// <summary>Enquanto o perfil está ativo.</summary>
    Always,

    /// <summary>Só enquanto alguma tecla protegida está pressionada.</summary>
    WhileProtectedKeyHeld,
}

/// <summary>Tela monocromática do teclado (A5.11).</summary>
public sealed record OledPanel(int Width, int Height);

/// <summary>Nomes dos ajustes no pipeline de controle (<c>recurso#ajuste</c>).</summary>
public static class KeyboardSettings
{
    public const string RapidTapPairs = "rapidtap.pairs";
    public const string ProtectionEnabled = "protection.enabled";
    public const string ProtectionKeys = "protection.keys";
    public const string ProtectionReduction = "protection.reduction";
    public const string ProtectionActivation = "protection.activation";
    public static string ProtectionSensitivity(string code) => $"protection.sensitivity.{code}";

    public static string Actuation(string code) => $"actuation.{code}";

    public const string RapidTriggerEnabled = "rt.enabled";

    public static string RapidTrigger(string code) => $"rt.{code}";

    /// <summary>Sensibilidade única (quando o teclado não separa pressionar/soltar).</summary>
    public static string Sensitivity(string code) => $"rt.sensitivity.{code}";

    public static string Press(string code) => $"rt.press.{code}";

    public static string Release(string code) => $"rt.release.{code}";

    public static string Dual(string code) => $"dual.{code}";

    public static string Remap(string code) => $"remap.{code}";

    public static string Meta(string code) => $"meta.{code}";
}

/// <summary>
/// Teclado analógico (A5): quais teclas têm chave ajustável, faixas exatas e os controles que ele expõe. A
/// configuração vive em controles por tecla (<c>actuation.KeyW</c>, <c>rt.KeyW</c>…), então perfis de jogo sobrepõem
/// só as teclas que mudam (A5.5 "per-key, group, profile, game") e cada ajuste passa pelo jornal e pelo desfazer.
/// </summary>
public sealed record AnalogKeyboardDescriptor
{
    public required string DeviceId { get; init; }

    public required string Name { get; init; }

    public required IReadOnlyList<KeyCap> Layout { get; init; }

    public required IReadOnlyList<KeySwitch> Keys { get; init; }

    public required StepRange Actuation { get; init; }

    public double DefaultActuation { get; init; } = 2.0;

    public required StepRange Sensitivity { get; init; }

    public double DefaultSensitivity { get; init; } = 0.3;

    /// <summary>Sensibilidade de pressionar e de soltar ajustáveis separadamente.</summary>
    public bool SeparateSensitivities { get; init; }

    /// <summary>Rapid Trigger continua ativo até a tecla subir toda (e não só até o ponto de atuação).</summary>
    public bool ContinuousRapidTrigger { get; init; }

    public int MaximumRapidTapPairs { get; init; } = 5;

    /// <summary>Quanto o Protection Mode aprofunda o ponto de atuação das teclas vizinhas.</summary>
    public StepRange ProtectionReduction { get; init; } = new(0.1, 2.0, 0.1);

    public double DefaultProtectionReduction { get; init; } = 0.5;

    /// <summary>Distância mínima entre os dois pontos de uma tecla 2-em-1.</summary>
    public double DualActionMinimumGap { get; init; } = 0.2;

    public bool HasMetaLayer { get; init; } = true;

    public OledPanel? Oled { get; init; }

    /// <summary>Protocolo/fonte para o modo avançado.</summary>
    public string Source { get; init; } = string.Empty;

    /// <summary>De onde vêm as faixas e o que ainda falta confirmar no hardware.</summary>
    public string Evidence { get; init; } = string.Empty;

    public ResourceId ActuationResource => new($"{DeviceId}/actuation");

    public ResourceId KeysResource => new($"{DeviceId}/keys");

    public ResourceId RgbResource => new($"{DeviceId}/rgb");

    public ResourceId OledResource => new($"{DeviceId}/oled");

    /// <summary>Estado informado pelo teclado (bateria, carregando), como o <c>/settings</c> do mouse e do headset.</summary>
    public ResourceId SettingsResource => new($"{DeviceId}/settings");

    public KeySwitch? Key(string code) => Keys.FirstOrDefault(k => string.Equals(k.Code, code, StringComparison.Ordinal));

    public IEnumerable<KeySwitch> With(KeyCapabilities capability) => Keys.Where(k => k.Has(capability));

    /// <summary>
    /// SteelSeries Apex Pro TKL Gen 3 (A5). Faixas do fabricante: atuação 0,1–4,0 mm em passos de 0,1 nas teclas
    /// OmniPoint, dez entradas de Rapid Tap no schema 19. Teclas digitais não recebem atuação/RT.
    /// Fn mantém o acesso às configurações e não é remapeada.
    /// </summary>
    public static AnalogKeyboardDescriptor ApexProTklGen3 { get; } = new()
    {
        DeviceId = "kbd-steelseries-apex-pro-tkl-gen3",
        Name = "SteelSeries Apex Pro TKL Gen 3",
        Layout = KeyboardLayout.Tkl,
        Keys = [.. KeyboardLayout.Tkl.Select(k => string.Equals(k.Id, "Fn", StringComparison.Ordinal)
            ? new KeySwitch(k.Id, k.Label, SwitchType.Fixed, KeyCapabilities.DigitalInput | KeyCapabilities.RgbAssignable)
            : ApexProtocol.LedId(k.Id) is { } usage && ApexProtocol.AnalogKeys.Contains(usage)
                ? new KeySwitch(k.Id, k.Label, SwitchType.Adjustable, KeyCapabilities.Analog)
                : new KeySwitch(k.Id, k.Label, SwitchType.Fixed, KeyCapabilities.Fixed))],
        Actuation = new StepRange(0.1, 4.0, 0.1),
        DefaultActuation = 2.0,
        Sensitivity = new StepRange(0.1, 4.0, 0.1),
        DefaultSensitivity = 0.3,
        MaximumRapidTapPairs = 10,
        Oled = new OledPanel(128, 40),
        Source = "HID vendor (interface 3, usage page 0xFFC0)",
        Evidence = "Schema 19 and HID replies verified on receiver 1038:1644. Actuation 0x6F, per-key RT 0x76, sensitivity 0x77; 0x57 is Rapid Tap. Profiles written and read back byte for byte.",
    };

    /// <summary>Todos os controles do teclado, gerados pelas capacidades de cada tecla (A25).</summary>
    public IReadOnlyList<DeviceControl> Controls()
    {
        var list = new List<DeviceControl>();
        var actuation = ActuationResource;
        var keys = KeysResource;
        const string Where = "Actuation";
        list.Add(new DeviceControl(new ControlKey(actuation, KeyboardSettings.RapidTriggerEnabled),
            DeviceSpecs.Toggle(KeyboardSettings.RapidTriggerEnabled, "Keyboard Rapid Trigger", "Rapid Trigger", false, Source,
                "Feature 0x76 with [HID usage, 0/1 mode] pairs.")
                with
                {
                    Description = "Enable per-key Rapid Trigger switches. One sensitivity value per key.",
                }));
        foreach (var key in Keys)
        {
            if (key.Has(KeyCapabilities.ActuationAdjustable))
            {
                list.Add(new DeviceControl(new ControlKey(actuation, KeyboardSettings.Actuation(key.Code)),
                    DeviceSpecs.Number(KeyboardSettings.Actuation(key.Code), $"Actuation {key.Label}", Where, Actuation, "mm", DefaultActuation, Source, Evidence)
                        with { PersistsOnDevice = true }));
                list.Add(new DeviceControl(new ControlKey(actuation, KeyboardSettings.ProtectionSensitivity(key.Code)),
                    DeviceSpecs.Number(KeyboardSettings.ProtectionSensitivity(key.Code), $"Protection {key.Label}", "Protection Mode",
                        new StepRange(0, 255, 1), "raw", 20, Source, "Schema 19, byte 12156 + physical index; no confirmed mm conversion.")
                        with { PersistsOnDevice = true }));
            }

            if (key.Has(KeyCapabilities.RapidTrigger))
            {
                list.Add(new DeviceControl(new ControlKey(actuation, KeyboardSettings.RapidTrigger(key.Code)),
                    DeviceSpecs.Toggle(KeyboardSettings.RapidTrigger(key.Code), $"Rapid Trigger {key.Label}", "Rapid Trigger", false, Source, Evidence)
                        with
                        {
                            PersistsOnDevice = true,
                            Description = "Per-key enable via 0x76; persists through Save to keyboard.",
                        }));
                if (SeparateSensitivities)
                {
                    list.Add(new DeviceControl(new ControlKey(actuation, KeyboardSettings.Press(key.Code)),
                        DeviceSpecs.Number(KeyboardSettings.Press(key.Code), $"Press sensitivity for {key.Label}", "Rapid Trigger", Sensitivity, "mm", DefaultSensitivity, Source, Evidence)
                            with { PersistsOnDevice = true, DependsOn = [KeyboardSettings.RapidTrigger(key.Code)] }));
                    list.Add(new DeviceControl(new ControlKey(actuation, KeyboardSettings.Release(key.Code)),
                        DeviceSpecs.Number(KeyboardSettings.Release(key.Code), $"Release sensitivity for {key.Label}", "Rapid Trigger", Sensitivity, "mm", DefaultSensitivity, Source, Evidence)
                            with { PersistsOnDevice = true, DependsOn = [KeyboardSettings.RapidTrigger(key.Code)] }));
                }
                else
                {
                    list.Add(new DeviceControl(new ControlKey(actuation, KeyboardSettings.Sensitivity(key.Code)),
                        DeviceSpecs.Number(KeyboardSettings.Sensitivity(key.Code), $"Sensitivity for {key.Label}", "Rapid Trigger", Sensitivity, "mm", DefaultSensitivity, Source, Evidence)
                            with
                            {
                                PersistsOnDevice = true,
                                DependsOn = [KeyboardSettings.RapidTriggerEnabled],
                                Description = "One distance: how far the key must release before triggering again.",
                            }));
                }
            }

            if (key.Has(KeyCapabilities.DualAction))
            {
                var code = key.Code;
                list.Add(new DeviceControl(new ControlKey(actuation, KeyboardSettings.Dual(code)),
                    DeviceSpecs.Text(KeyboardSettings.Dual(code), $"Dual action {key.Label}", "Dual-action keys", string.Empty, Source,
                        value => value is TextValue text ? KeyboardRules.CheckDual(this, code, text.Value) : "Invalid value.",
                        (text, provider) => DualAction.TryParse(text.Value, out var dual, out _) && dual is not null ? dual.Describe(provider) : "Disabled",
                        Evidence) with { PersistsOnDevice = true, DependsOn = [KeyboardSettings.Actuation(code)] }));
            }

            if (key.Has(KeyCapabilities.Remappable))
            {
                var code = key.Code;
                list.Add(new DeviceControl(new ControlKey(keys, KeyboardSettings.Remap(code)),
                    DeviceSpecs.Text(KeyboardSettings.Remap(code), $"Action for {key.Label}", "Assignments", InputAction.Default.Encode(), Source,
                        value => value is TextValue text ? KeyboardRules.CheckBinding(this, code, text.Value, meta: false) : "Invalid value.",
                        (text, provider) => InputAction.TryParse(text.Value, out var action, out _) ? action.Describe() : text.Value,
                        Evidence) with { PersistsOnDevice = true }));
            }

            if (HasMetaLayer && key.Has(KeyCapabilities.MetaLayerAssignable))
            {
                var code = key.Code;
                list.Add(new DeviceControl(new ControlKey(keys, KeyboardSettings.Meta(code)),
                    DeviceSpecs.Text(KeyboardSettings.Meta(code), $"{key.Label} on the Meta layer", "Meta layer", InputAction.Default.Encode(), Source,
                        value => value is TextValue text ? KeyboardRules.CheckBinding(this, code, text.Value, meta: true) : "Invalid value.",
                        (text, provider) => InputAction.TryParse(text.Value, out var action, out _) ? action.Describe() : text.Value,
                        Evidence) with { PersistsOnDevice = true }));
            }
        }

        list.Add(new DeviceControl(new ControlKey(actuation, KeyboardSettings.RapidTapPairs),
            DeviceSpecs.Text(KeyboardSettings.RapidTapPairs, "Rapid Tap pairs", "Rapid Tap", string.Empty, Source,
                value => value is TextValue text ? KeyboardRules.CheckPairs(this, text.Value) : "Invalid value.",
                (text, provider) => RapidTapPair.TryParseList(text.Value, out var pairs, out _) ? RapidTapPair.Describe(pairs) : text.Value,
                Evidence) with { PersistsOnDevice = true }));
        list.Add(new DeviceControl(new ControlKey(actuation, KeyboardSettings.ProtectionEnabled),
            DeviceSpecs.Toggle(KeyboardSettings.ProtectionEnabled, "Protection Mode", "Protection Mode", false, Source, Evidence) with { PersistsOnDevice = true }));
        list.Add(new DeviceControl(new ControlKey(actuation, KeyboardSettings.ProtectionKeys),
            DeviceSpecs.Text(KeyboardSettings.ProtectionKeys, "Protected keys", "Protection Mode", string.Join(',', KeyboardLayout.Wasd), Source,
                value => value is TextValue text ? KeyboardRules.CheckProtectedKeys(this, text.Value) : "Invalid value.",
                (text, provider) => string.Join(", ", KeyboardConfig.SplitCodes(text.Value).Select(c => Key(c)?.Label ?? c)),
                Evidence) with { PersistsOnDevice = true }));
        list.Add(new DeviceControl(new ControlKey(actuation, KeyboardSettings.ProtectionReduction),
            DeviceSpecs.Number(KeyboardSettings.ProtectionReduction, "Neighboring key sensitivity reduction", "Protection Mode", ProtectionReduction, "mm", DefaultProtectionReduction, Source, Evidence)
                with { PersistsOnDevice = true }));
        list.Add(new DeviceControl(new ControlKey(actuation, KeyboardSettings.ProtectionActivation),
            DeviceSpecs.Choice(KeyboardSettings.ProtectionActivation, "When to protect", "Protection Mode",
                [new(nameof(Keyboard.ProtectionActivation.Always), "Always"), new(nameof(Keyboard.ProtectionActivation.WhileProtectedKeyHeld), "While a protected key is held")],
                nameof(Keyboard.ProtectionActivation.WhileProtectedKeyHeld), Source, Evidence) with { PersistsOnDevice = true }));
        return list;
    }

    /// <summary>Texto de uma distância com as casas do passo ("0,2 mm").</summary>
    public string Millimeters(double value, IFormatProvider? provider = null) =>
        string.Create(provider ?? CultureInfo.CurrentCulture, $"{value.ToString("F" + DeviceSpecs.DecimalsOf(Actuation.Step).ToString(CultureInfo.InvariantCulture), provider ?? CultureInfo.CurrentCulture)} mm");
}
