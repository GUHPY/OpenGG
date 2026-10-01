using OneRGB.Application.Control;

namespace OneRGB.Application.Devices.Keyboard;

/// <summary>
/// Ajustes voláteis do receptor: atuação 0x6F, RT por tecla 0x76 e sensibilidade 0x77.
/// Os outros campos são editados no perfil persistente. Não abre o HID.
/// </summary>
public static class ApexLive
{
    public const double MaxMm = ApexProtocol.MaxActuationMm;

    public static bool Writes(string setting) =>
        setting == KeyboardSettings.RapidTriggerEnabled
        || (setting.StartsWith("actuation.", StringComparison.Ordinal) && Analog(setting["actuation.".Length..]))
        || (setting.StartsWith("rt.sensitivity.", StringComparison.Ordinal) && Analog(setting["rt.sensitivity.".Length..]))
        || (setting.StartsWith("rt.", StringComparison.Ordinal) && ApexProtocol.LedId(setting[3..]) is { } usage && ApexProtocol.AnalogKeys.Contains(usage));

    /// <summary>Mapa carregado do perfil ativado; mantém as teclas não editadas.</summary>
    public sealed class State
    {
        public State(double defaultActuationMm, double defaultSensitivityMm)
        {
            var actuation = ApexProtocol.ActuationToHardware(defaultActuationMm);
            var sensitivity = ApexProtocol.SensitivityToHardware(defaultSensitivityMm);
            foreach (var usage in ApexProtocol.AnalogKeys)
            {
                Actuations[usage] = actuation;
                Sensitivities[usage] = sensitivity;
                RapidTriggers[usage] = true;
            }
        }

        public Dictionary<byte, (byte Actuation, byte Release)> Actuations { get; } = [];

        public Dictionary<byte, byte> Sensitivities { get; } = [];

        public Dictionary<byte, bool> RapidTriggers { get; } = [];

        public bool Enabled { get; set; }

        private State() { }

        public State Clone()
        {
            var copy = new State { Enabled = Enabled };
            foreach (var (key, value) in Actuations) { copy.Actuations[key] = value; }
            foreach (var (key, value) in Sensitivities) { copy.Sensitivities[key] = value; }
            foreach (var (key, value) in RapidTriggers) { copy.RapidTriggers[key] = value; }
            return copy;
        }
    }

    /// <summary>Todos os ajustes entram no mapa; só o último relatório de cada opcode é enviado.</summary>
    public static byte[][] ApplyBatch(State state, IEnumerable<KeyValuePair<string, ControlValue>> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        var reports = new Dictionary<byte, byte[]>();
        foreach (var (setting, value) in values)
        {
            foreach (var report in Apply(state, setting, value)) { reports[report[1]] = report; }
        }
        if (!state.Enabled) { reports.Remove(ApexProtocol.OpcodeRapidTrigger); }
        return [.. reports.OrderBy(r => r.Key).Select(r => r.Value)];
    }

    /// <summary>
    /// Atualiza o mapa e devolve os relatórios a enviar. Vazio quando a sensibilidade muda com o Rapid Trigger
    /// desligado: o valor fica no mapa e sai no 0x77 quando o 0x76 ligar. Atuação acima de 4,0 mm é cortada na faixa.
    /// </summary>
    public static byte[][] Apply(State state, string setting, ControlValue value)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(setting);
        ArgumentNullException.ThrowIfNull(value);
        if (setting.StartsWith("actuation.", StringComparison.Ordinal))
        {
            var usage = Usage(setting["actuation.".Length..]);
            if (value is not NumberValue number)
            {
                throw new InvalidOperationException("Live actuation requires a number. Nothing was converted.");
            }

            state.Actuations[usage] = ApexProtocol.ActuationToHardware(number.Value);
            return [ApexProtocol.ActuationFrame(state.Actuations, state.Actuations[usage])];
        }

        if (setting == KeyboardSettings.RapidTriggerEnabled)
        {
            if (value is not ToggleValue toggle)
            {
                throw new InvalidOperationException("Live Rapid Trigger requires on or off. Nothing was sent.");
            }

            state.Enabled = toggle.Value;
            return toggle.Value
                ? [ToggleFrame(state), SensitivityFrame(state)]
                : [ApexProtocol.RapidTriggerSwitch(false)];
        }

        if (setting.StartsWith("rt.sensitivity.", StringComparison.Ordinal))
        {
            var usage = Usage(setting["rt.sensitivity.".Length..]);
            if (value is not NumberValue number)
            {
                throw new InvalidOperationException("Live sensitivity requires a number. Nothing was sent.");
            }

            state.Sensitivities[usage] = ApexProtocol.SensitivityToHardware(number.Value);
            return state.Enabled ? [SensitivityFrame(state)] : [];
        }

        if (setting.StartsWith("rt.", StringComparison.Ordinal) && value is ToggleValue perKey)
        {
            state.RapidTriggers[Usage(setting[3..])] = perKey.Value;
            return [ToggleFrame(state)];
        }

        throw new InvalidOperationException($"No live command for {setting}. Nothing was sent.");
    }

    private static byte[] SensitivityFrame(State state) =>
        ApexProtocol.RapidTriggerFrame(state.Sensitivities, state.Sensitivities[ApexProtocol.AnalogKeys[0]]);

    private static byte[] ToggleFrame(State state) => ApexProtocol.ToggleFrame(ApexProtocol.OpcodeRapidTriggerEnable,
        state.RapidTriggers.ToDictionary(p => p.Key, p => state.Enabled && p.Value));

    private static byte Usage(string code)
    {
        if (ApexProtocol.LedId(code) is { } usage && Array.IndexOf(ApexProtocol.AnalogKeys, usage) >= 0)
        {
            return usage;
        }

        throw new InvalidOperationException($"Key {code} is outside the analog matrix. Nothing was sent.");
    }

    private static bool Analog(string code) => ApexProtocol.LedId(code) is { } usage && ApexProtocol.AnalogKeys.Contains(usage);
}
