using OneRGB.Application.Devices.Mouse;

using OneRGB.Application.Lighting;

namespace OneRGB.Application.Devices.Keyboard;

/// <summary>
/// Protocolo HID do SteelSeries Apex Pro TKL Gen 3 pela interface de controle do fabricante (página 0xFFC0, MI_03 no sem fio, MI_01 no cabo).
/// Receptor 2.4 GHz, schema 19: captura de saídas e validação com respostas HID reais em 30/09/2026.
/// - 0x61/0x40/0x21: iluminação RGB por tecla (642 bytes);
/// - 0x6F: atuação analógica ao vivo por tecla nas 68 chaves OmniPoint (642 bytes, 0,1–4,0 mm);
/// - 0x76: Rapid Trigger por tecla; 0x57 é Rapid Tap;
/// - 0x77: uma sensibilidade ao vivo por tecla (642 bytes, mm × 10). Não liga nem desliga o Rapid Trigger;
/// - 0x4A: OLED em colunas; 0x62: libera iluminação temporária;
/// - 0xBC/0xD2 (0x92 no cabo): conexão e bateria, só leitura.
/// </summary>
public static class ApexProtocol
{
    public const ushort VendorId = 0x1038;
    public const ushort UsagePage = 0xFFC0;
    public const byte ClearLighting = 0x62;
    public const byte OpcodeActuation = 0x6F;
    /// <summary>Receiver command names routed directly to the same wireless keyboard over USB.</summary>
    public static byte CommandOpcode(ushort productId, byte opcode) => productId switch
    {
        0x1644 => opcode,
        0x1646 => opcode is 0x53 or 0x68 or 0x6F or 0x76 or 0x77 or 0x4A or 0x4B or 0xE6 ? (byte)(opcode & ~0x40) : opcode,
        _ => throw new NotSupportedException("Advanced commands require the verified wireless Gen 3 receiver or USB cable."),
    };
    public const byte OpcodeRapidTriggerEnable = 0x76;
    public const byte OpcodeRapidTapEnable = 0x57;
    public const byte OpcodeProtection = 0x54;
    public const byte OpcodeRapidTrigger = 0x77;
    public const byte OpcodeOled = 0x4A;
    public const int ReportLength = 642;
    public const int RapidTriggerSwitchLength = ReportLength;

    /// <summary>Faixa física do teclado. Os passos de 0,1 mm cabem todos.</summary>
    public const double MaxActuationMm = 4.0;
    public const int OledReportLength = ReportLength;
    public const int OledBitmapLength = 640;
    public const int AnalogKeyCount = 68;

    /// <summary>
    /// Os 68 usos HID analógicos da região anunciada pelo GG, em ordem de uso HID. O perfil usa outra ordem física.
    /// </summary>
    public static readonly byte[] AnalogKeys =
    [
        0x04, 0x05, 0x06, 0x07, 0x08, 0x09, 0x0A, 0x0B, 0x0C, 0x0D, 0x0E, 0x0F,
        0x10, 0x11, 0x12, 0x13, 0x14, 0x15, 0x16, 0x17, 0x18, 0x19, 0x1A, 0x1B,
        0x1C, 0x1D, 0x1E, 0x1F, 0x20, 0x21, 0x22, 0x23, 0x24, 0x25, 0x26, 0x27,
        0x28, 0x2A, 0x2B, 0x2C, 0x2D, 0x2E, 0x2F, 0x30, 0x31, 0x32, 0x33, 0x34,
        0x35, 0x36, 0x37, 0x38, 0x39, 0x64, 0x87, 0x88, 0x89, 0x8A, 0x8B,
        0xE0, 0xE1, 0xE2, 0xE3, 0xE4, 0xE5, 0xE6, 0xE7, 0xF0
    ];

    private static readonly (byte Actuation, byte Release)[] Thresholds =
    [
        (4,4),(3,4),(4,5),(5,7),(7,8),(8,10),(10,12),(12,14),(14,16),(16,18),
        (18,20),(20,22),(22,25),(25,28),(28,31),(31,34),(34,38),(38,41),(41,45),(45,49),
        (49,54),(54,59),(59,65),(65,71),(71,78),(78,85),(85,93),(93,102),(102,112),(112,122),
        (122,134),(134,147),(147,162),(162,177),(177,186),(186,196),(196,206),(206,216),(208,216),(210,216),
    ];

    /// <summary>
    /// Converte distância em milímetros (0,1–4,0 mm) nos valores de atuação (A) e soltura (B).
    /// Tabela de 40 posições do firmware >= 3.22.0; não interpolar os pontos.
    /// </summary>
    public static (byte Actuation, byte Release) ActuationToHardware(double millimeters)
    {
        if (!double.IsFinite(millimeters))
        {
            throw new ArgumentOutOfRangeException(nameof(millimeters), millimeters, "Invalid actuation. Nothing was converted.");
        }

        return Thresholds[(int)Math.Round(Math.Clamp(millimeters, 0.1, MaxActuationMm) * 10) - 1];
    }

    /// <summary>Converte valor de atuação do hardware de volta para milímetros (0,1 a 4,0 mm).</summary>
    public static double HardwareToActuationMm(byte actuation)
    {
        return (Enumerable.Range(0, Thresholds.Length).MinBy(i => Math.Abs(Thresholds[i].Actuation - actuation)) + 1) / 10.0;
    }

    public static double HardwareToActuationMm(byte actuation, byte release) =>
        (Enumerable.Range(0, Thresholds.Length).MinBy(i => Math.Abs(Thresholds[i].Actuation - actuation) + Math.Abs(Thresholds[i].Release - release)) + 1) / 10.0;

    /// <summary>
    /// Converte sensibilidade de Rapid Trigger em milímetros para o valor de hardware (escala linear em décimos de mm: 0,1 mm = 1, 0,5 mm = 5).
    /// </summary>
    public static byte SensitivityToHardware(double millimeters) => double.IsFinite(millimeters)
        ? (byte)Math.Clamp((int)Math.Round(millimeters * 10.0), 1, 40)
        : throw new ArgumentOutOfRangeException(nameof(millimeters));

    /// <summary>Converte valor de hardware de Rapid Trigger de volta para milímetros.</summary>
    public static double HardwareToSensitivityMm(byte sensitivity) =>
        Math.Clamp(sensitivity / 10.0, 0.1, 4.0);

    /// <summary>
    /// Interface e opcode do quadro RGB por PID:
    /// - 1642: no cabo (MI_01);
    /// - 1644: pelo receptor (MI_03);
    /// - 1646: o sem fio ligado no cabo (MI_03).
    /// </summary>
    public static (int Interface, byte Opcode)? Model(ushort productId) => productId switch
    {
        0x1642 => (1, 0x40),
        0x1644 => (3, 0x61),
        0x1646 => (3, 0x21),
        _ => null,
    };

    /// <summary>
    /// Consulta de conexão pelo receptor, que o GG repete a cada ~0,9 s: resposta <c>[BC, 1]</c> com o teclado ligado nele
    /// (lido neste PC em 29/09/2026).
    /// </summary>
    public const byte OpcodeConnection = 0xBC;

    /// <summary>
    /// Consulta de bateria: 0x92 direto no cabo (1646) e 0x92 | 0x40 = 0xD2 pelo receptor (1644), a mesma regra do RGB
    /// (0x21/0x61). O 1642 é o modelo só com fio, sem bateria. O GG repete o 0xD2 a cada ~0,9 s (apex_gen3.pcapng).
    /// </summary>
    public static byte? BatteryOpcode(ushort productId) => productId switch
    {
        0x1644 => 0xD2,
        0x1646 => 0x92,
        _ => null,
    };

    /// <summary>
    /// Byte de estado da resposta da bateria (<c>[opcode, estado]</c>): bit 7 = carregando; o resto é o nível em passos de
    /// 5 % a partir de 1 (1 = 0 %, 21 = 100 %), como nos mouses sem fio da SteelSeries (rivalcfg, só consulta). Nulo com 0
    /// (nada a informar). Lido neste PC em 29/09/2026: 0x04 e, horas depois, 0x03 (descarregando).
    /// </summary>
    // ponytail: escala da família Aerox, não conferida no GG para este teclado; calibrar aqui se o GG mostrar outro número.
    public static (int Percent, bool Charging)? Battery(byte state) =>
        (state & 0x7F) is var level and > 0 ? (Math.Clamp((level - 1) * 5, 0, 100), (state & 0x80) != 0) : null;

    /// <summary>
    /// LED e uso HID da tecla, pelo código W3C:
    /// - o uso HID dela (página 0x07);
    /// - 0xE0–0xE6 nos modificadores; o Fn fica no lugar do Win direito, 0xE7;
    /// - 0xF0 no Menu.
    /// Nulo sem LED / fora da matriz analógica.
    /// </summary>
    public static byte? LedId(string code) => code switch
    {
        "ControlLeft" => 0xE0,
        "ShiftLeft" => 0xE1,
        "AltLeft" => 0xE2,
        "MetaLeft" => 0xE3,
        "ControlRight" => 0xE4,
        "ShiftRight" => 0xE5,
        "AltRight" => 0xE6,
        "Fn" or "MetaRight" => 0xE7,
        "ContextMenu" => 0xF0,
        _ => HidKeys.Usage(code) is int usage and <= 0xFF ? (byte)usage : null,
    };

    /// <summary>Quadro de iluminação RGB por tecla.</summary>
    public static byte[] Frame(byte opcode, IReadOnlyList<byte?> leds, ReadOnlySpan<Rgb> colors)
    {
        var packet = new List<byte> { 0x00, opcode, 0 };
        for (var i = 0; i < leds.Count && i < colors.Length; i++)
        {
            if (leds[i] is { } id)
            {
                packet.AddRange([id, colors[i].R, colors[i].G, colors[i].B]);
            }
        }

        packet[2] = (byte)((packet.Count - 3) / 4);
        return [.. packet];
    }

    /// <summary>
    /// Monta o relatório de recurso de 642 bytes para atuação ao vivo (Opcode 0x6F).
    /// Cabeçalho: [0x00, 0x6F, 0x00, 0x44], seguido das 68 triplas [usage, atuação_A, soltura_B].
    /// </summary>
    public static byte[] ActuationFrame(IReadOnlyDictionary<byte, (byte Actuation, byte Release)> perKeyValues, (byte Actuation, byte Release) defaultValues)
    {
        var frame = new byte[ReportLength];
        frame[0] = 0x00;
        frame[1] = OpcodeActuation;
        frame[2] = 0x00;
        frame[3] = (byte)AnalogKeys.Length;

        for (var i = 0; i < AnalogKeys.Length; i++)
        {
            var usage = AnalogKeys[i];
            var (act, rel) = perKeyValues.TryGetValue(usage, out var custom) ? custom : defaultValues;
            var offset = 4 + (i * 3);
            frame[offset] = usage;
            frame[offset + 1] = act;
            frame[offset + 2] = rel;
        }

        return frame;
    }

    /// <summary>
    /// Liga ou desliga as 68 teclas via feature 0x76, pares [usage, modo 0/1].
    /// </summary>
    public static byte[] RapidTriggerSwitch(bool enabled)
    {
        return ToggleFrame(OpcodeRapidTriggerEnable, AnalogKeys.ToDictionary(u => u, _ => enabled));
    }

    /// <summary>0x76 = Rapid Trigger; 0x54 = Protection Mode. 0x57 é Rapid Tap.</summary>
    public static byte[] ToggleFrame(byte opcode, IReadOnlyDictionary<byte, bool> enabled)
    {
        var frame = new byte[ReportLength];
        frame[1] = opcode;
        frame[2] = AnalogKeyCount;
        for (var i = 0; i < AnalogKeys.Length; i++)
        {
            frame[3 + 2*i] = AnalogKeys[i];
            frame[4 + 2*i] = enabled.GetValueOrDefault(AnalogKeys[i]) ? (byte)1 : (byte)0;
        }

        return frame;
    }

    /// <summary>
    /// Monta o relatório de recurso de 642 bytes para a sensibilidade ao vivo (Opcode 0x77).
    /// Cabeçalho de três bytes: [0x00, 0x77, 0x44]. O byte seguinte é o usage da primeira tecla, não campo de cabeçalho.
    /// Uma sensibilidade por tecla.
    /// </summary>
    public static byte[] RapidTriggerFrame(IReadOnlyDictionary<byte, byte> perKeySensitivity, byte defaultSensitivity = 1)
    {
        var frame = new byte[ReportLength];
        frame[0] = 0x00;
        frame[1] = OpcodeRapidTrigger;
        frame[2] = (byte)AnalogKeys.Length;

        for (var i = 0; i < AnalogKeys.Length; i++)
        {
            var usage = AnalogKeys[i];
            var sens = perKeySensitivity.TryGetValue(usage, out var custom) ? custom : defaultSensitivity;
            var offset = 3 + (i * 2);
            frame[offset] = usage;
            frame[offset + 1] = sens;
        }

        return frame;
    }

    /// <summary>
    /// Recebe linhas MSB-first 128×40; monta feature 0x4A em páginas de oito linhas, LSB-first por coluna.
    /// </summary>
    public static byte[] OledFrame(ReadOnlySpan<byte> bitmap)
    {
        if (bitmap.Length != OledBitmapLength) { throw new ArgumentException("OLED requires 640 bytes.", nameof(bitmap)); }
        var frame = new byte[OledReportLength];
        frame[1] = OpcodeOled;
        for (var y = 0; y < 40; y++)
        {
            for (var x = 0; x < 128; x++)
            {
                if ((bitmap[y * 16 + x / 8] & (128 >> (x % 8))) != 0)
                {
                    frame[2 + (y / 8) * 128 + x] |= (byte)(1 << (y % 8));
                }
            }
        }
        return frame;
    }
}
