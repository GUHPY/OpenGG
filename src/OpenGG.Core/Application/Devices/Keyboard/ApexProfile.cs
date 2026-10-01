using System.Buffers.Binary;

using OneRGB.Application.Devices.Input;

namespace OneRGB.Application.Devices.Keyboard;

/// <summary>Perfil schema 19 do receptor 1038:1644. Edita uma cópia válida, preservando campos não alterados.</summary>
public static class ApexProfile
{
    public const int Length = 12288;
    public const int BodyEnd = 12280;
    private static readonly AnalogKeyboardDescriptor Device = AnalogKeyboardDescriptor.ApexProTklGen3;
    private static readonly Dictionary<byte, int> Indexes = MakeIndexes();
    private static readonly (byte Actuation, byte Release)[] GlobalThresholds =
    [
        (4,4),(3,4),(5,7),(8,10),(12,14),(16,18),(20,22),(25,28),(31,34),(38,41),
        (45,49),(54,59),(65,71),(78,85),(93,102),(112,122),(134,147),(162,162),(196,206),(210,216),
    ];

    private static (byte Actuation, byte Release)? GlobalThreshold(ReadOnlySpan<byte> profile) =>
        profile[11278] == 0 ? null : GlobalThresholds[Math.Clamp((int)profile[11279], 1, 20) - 1];

    public static void Validate(ReadOnlySpan<byte> profile)
    {
        if (profile.Length != Length || BinaryPrimitives.ReadUInt32LittleEndian(profile[4..]) != 19)
        {
            throw new InvalidDataException("Expected a 12,288-byte schema 19 profile. Nothing was sent.");
        }

        if (BinaryPrimitives.ReadUInt32LittleEndian(profile) != Crc(profile))
        {
            throw new InvalidDataException("Invalid profile CRC. Nothing was sent.");
        }
    }

    public static string Name(ReadOnlySpan<byte> profile)
    {
        Validate(profile);
        return System.Text.Encoding.ASCII.GetString(profile.Slice(10, 21)).TrimEnd('\0');
    }

    /// <summary>Valores representáveis no editor. Mapeamentos desconhecidos permanecem intactos na gravação por diferença.</summary>
    public static KeyboardConfig ReadConfig(ReadOnlySpan<byte> profile)
    {
        Validate(profile);
        var config = KeyboardConfig.Defaults(Device);
        var global = GlobalThreshold(profile);
        var protectedKeys = new List<string>();
        foreach (var key in Device.Keys)
        {
            if (ApexProtocol.LedId(key.Code) is not { } usage || !Indexes.TryGetValue(usage, out var index) || key.Code == "Fn")
            {
                continue;
            }

            if (index < 70 && key.Has(KeyCapabilities.ActuationAdjustable))
            {
                var at = 10998 + 2 * index;
                config.Actuation[key.Code] = global is { } threshold
                    ? ApexProtocol.HardwareToActuationMm(threshold.Actuation)
                    : ApexProtocol.HardwareToActuationMm(profile[at], profile[at + 1]);
                var enabled = Bit(profile, 12055, index);
                var sensitivity = ApexProtocol.HardwareToSensitivityMm(profile[12068 + index]);
                config.RapidTrigger[key.Code] = new KeyRapidTrigger(enabled, sensitivity, sensitivity);
                config.ProtectionSensitivities[key.Code] = profile[12156 + index];
                config.RapidTriggerEnabled |= enabled;
                if (Bit(profile, 12138, index)) { protectedKeys.Add(key.Code); }
                if (Bit(profile, 11280, index))
                {
                    var deep = 11138 + 2 * index;
                    if (DecodeMapping(profile.Slice(1048 + 5 * index, 5), usage) is { } second)
                    {
                        config.Dual[key.Code] = new DualAction(ApexProtocol.HardwareToActuationMm(profile[deep], profile[deep + 1]),
                            second, !Bit(profile, 11301, index));
                    }
                }
            }

            if (DecodeMapping(profile.Slice(48 + 5 * index, 5), usage) is { Kind: not InputActionKind.Default } remap)
            {
                config.Remap[key.Code] = remap;
            }

            if (DecodeMapping(profile.Slice(548 + 5 * index, 5), usage) is { Kind: not InputActionKind.Default } meta)
            {
                config.Meta[key.Code] = meta;
            }
        }

        config.Protection = config.Protection with { Enabled = protectedKeys.Count > 0, Keys = protectedKeys };
        for (var i = 0; i < 10; i++)
        {
            var at = 12226 + 5 * i;
            if (Code(profile[at]) is { } first && Code(profile[at + 1]) is { } second && (profile[at + 2] & 0x7F) < 4)
            {
                config.RapidTapPairs.Add(new RapidTapPair(first, second, (RapidTapMode)(profile[at + 2] & 3), profile[12276] != 0));
            }
        }

        return config;
    }

    public static void SeedLive(ReadOnlySpan<byte> profile, ApexLive.State state)
    {
        Validate(profile);
        ArgumentNullException.ThrowIfNull(state);
        state.Enabled = false;
        var global = GlobalThreshold(profile);
        foreach (var usage in ApexProtocol.AnalogKeys)
        {
            var index = Indexes[usage];
            state.Actuations[usage] = global ?? (profile[10998 + 2 * index], profile[10999 + 2 * index]);
            state.Sensitivities[usage] = profile[12068 + index];
            state.RapidTriggers[usage] = Bit(profile, 12055, index);
            state.Enabled |= state.RapidTriggers[usage];
        }
    }

    /// <summary>Somente valores que diferem da configuração lida. Não reserializa macros nem campos desconhecidos.</summary>
    public static byte[] Patch(ReadOnlySpan<byte> original, KeyboardConfig baseline, KeyboardConfig edited)
    {
        Validate(original);
        ArgumentNullException.ThrowIfNull(baseline);
        ArgumentNullException.ThrowIfNull(edited);
        var old = baseline.ToValues();
        var changed = edited.ToValues().Where(v => !old.TryGetValue(v.Key, out var previous) || !previous.Equals(v.Value))
            .Select(v => v.Key.Setting).ToHashSet(StringComparer.Ordinal);
        if (changed.Contains(KeyboardSettings.ProtectionReduction) || changed.Contains(KeyboardSettings.ProtectionActivation))
        {
            throw new InvalidOperationException("Simulator reduction in mm and activation have no verified firmware equivalent. Use the JSON editor for Protection Mode duration and raw sensitivity.");
        }

        var result = original.ToArray();
        if (changed.Contains(KeyboardSettings.RapidTriggerEnabled) && !edited.RapidTriggerEnabled)
        {
            for (var i = 0; i < 70; i++) { PutBit(result, 12055, i, false); }
        }
        var dualChanged = false;
        foreach (var key in Device.Keys)
        {
            if (key.Code == "Fn" || ApexProtocol.LedId(key.Code) is not { } usage || !Indexes.TryGetValue(usage, out var index))
            {
                if (changed.Contains(KeyboardSettings.Remap(key.Code)) || changed.Contains(KeyboardSettings.Meta(key.Code)))
                {
                    throw new InvalidOperationException($"{key.Label}: profile position is unverified. Nothing was sent.");
                }
                continue;
            }
            if (index < 70)
            {
                if (changed.Contains(KeyboardSettings.Actuation(key.Code)))
                {
                    if (GlobalThreshold(result) is { } global)
                    {
                        // Desligar a atuação global exige preservar seu valor efetivo nas outras 69 posições.
                        for (var i = 0; i < 70; i++)
                        {
                            result[10998 + 2 * i] = global.Actuation;
                            result[10999 + 2 * i] = global.Release;
                        }
                    }
                    PutThreshold(result, 10998 + 2 * index, edited.Actuation[key.Code]);
                    result[11278] = 0;
                }

                if (changed.Contains(KeyboardSettings.RapidTriggerEnabled) || changed.Contains(KeyboardSettings.RapidTrigger(key.Code)))
                {
                    PutBit(result, 12055, index, edited.RapidTriggerEnabled && edited.RapidTrigger[key.Code].Enabled);
                }

                if (changed.Contains(KeyboardSettings.Sensitivity(key.Code)))
                {
                    var mm = edited.RapidTrigger[key.Code].PressMm;
                    CheckMm(mm);
                    result[12068 + index] = ApexProtocol.SensitivityToHardware(mm);
                }

                if (changed.Contains(KeyboardSettings.ProtectionEnabled) || changed.Contains(KeyboardSettings.ProtectionKeys))
                {
                    PutBit(result, 12138, index, edited.Protection.Enabled && edited.Protection.Keys.Contains(key.Code, StringComparer.Ordinal));
                }
                if (changed.Contains(KeyboardSettings.ProtectionSensitivity(key.Code)))
                {
                    var raw = edited.ProtectionSensitivities[key.Code];
                    if (!double.IsFinite(raw) || raw < 0 || raw > 255 || raw != Math.Truncate(raw))
                        throw new InvalidOperationException("Protection sensitivity must be a whole byte (0–255).");
                    result[12156 + index] = (byte)raw;
                }

                if (changed.Contains(KeyboardSettings.Dual(key.Code)))
                {
                    dualChanged = true;
                    var dual = edited.Dual.GetValueOrDefault(key.Code);
                    PutBit(result, 11280, index, dual is not null);
                    PutBit(result, 11301, index, dual is { KeepFirst: false });
                    if (dual is null)
                    {
                        result.AsSpan(1048 + 5 * index, 5).Clear();
                        result.AsSpan(11138 + 2 * index, 2).Fill(255);
                    }
                    else
                    {
                        PutThreshold(result, 11138 + 2 * index, dual.DeepMm);
                        EncodeMapping(dual.Second, usage, false).CopyTo(result, 1048 + 5 * index);
                    }
                }
            }

            if (changed.Contains(KeyboardSettings.Remap(key.Code)))
            {
                EncodeMapping(edited.Remap.GetValueOrDefault(key.Code) ?? InputAction.Default, usage, false).CopyTo(result, 48 + 5 * index);
            }

            if (changed.Contains(KeyboardSettings.Meta(key.Code)))
            {
                EncodeMapping(edited.Meta.GetValueOrDefault(key.Code) ?? InputAction.Default, usage, true).CopyTo(result, 548 + 5 * index);
            }
        }

        if (dualChanged)
        {
            var indexes = Enumerable.Range(0, 70).Where(i => Bit(result, 11280, i)).ToArray();
            if (indexes.Length > 8) { throw new InvalidOperationException("Wireless firmware supports at most eight dual-action keys."); }
            result.AsSpan(11293, 8).Fill(255);
            for (var i = 0; i < indexes.Length; i++) { result[11293 + i] = (byte)indexes[i]; }
        }

        if (changed.Contains(KeyboardSettings.RapidTapPairs))
        {
            var pairs = edited.RapidTapPairs.Where(p => p.Enabled).ToArray();
            if (pairs.Length > 10) { throw new InvalidOperationException("At most ten Rapid Tap pairs."); }
            var used = new HashSet<byte>();
            result.AsSpan(12226, 50).Clear();
            result[12276] = pairs.Length > 0 ? (byte)1 : (byte)0;
            for (var i = 0; i < pairs.Length; i++)
            {
                var pair = pairs[i];
                var a = ApexProtocol.LedId(pair.First) ?? throw new InvalidOperationException("Invalid Rapid Tap key.");
                var b = ApexProtocol.LedId(pair.Second) ?? throw new InvalidOperationException("Invalid Rapid Tap key.");
                if (!used.Add(a) || !used.Add(b) || !Enum.IsDefined(pair.Mode)) { throw new InvalidOperationException("Duplicate or invalid Rapid Tap pair."); }
                var at = 12226 + 5 * i;
                result[at] = a;
                result[at + 1] = b;
                result[at + 2] = (byte)pair.Mode;
                for (var j = 0; j < 10; j++)
                {
                    var prior = 12226 + 5 * j;
                    if (original[prior] == a && original[prior + 1] == b) { result[at + 2] |= (byte)(original[prior + 2] & 128); }
                }
            }
        }

        BinaryPrimitives.WriteUInt32LittleEndian(result, Crc(result));
        Validate(result);
        return result;
    }

    private static uint Crc(ReadOnlySpan<byte> profile)
    {
        var crc = 0xFFFFFFFFu;
        foreach (var value in profile[8..BodyEnd])
        {
            crc ^= value;
            for (var bit = 0; bit < 8; bit++) { crc = (crc >> 1) ^ (0xEDB88320u & (uint)-(int)(crc & 1)); }
        }
        return ~crc;
    }

    private static bool Bit(ReadOnlySpan<byte> data, int offset, int index) => (data[offset + index / 8] & (1 << (index % 8))) != 0;

    private static void PutBit(byte[] data, int offset, int index, bool value)
    {
        var mask = 1 << (index % 8);
        data[offset + index / 8] = (byte)((data[offset + index / 8] & ~mask) | (value ? mask : 0));
    }

    private static void CheckMm(double mm)
    {
        if (!double.IsFinite(mm) || mm < 0.1 || mm > 4) { throw new InvalidOperationException("Distance outside 0.1–4.0 mm."); }
    }

    private static void PutThreshold(byte[] data, int offset, double mm)
    {
        CheckMm(mm);
        var (a, b) = ApexProtocol.ActuationToHardware(mm);
        data[offset] = a;
        data[offset + 1] = b;
    }

    private static string? Code(byte usage) => Device.Keys.FirstOrDefault(k => k.Code != "Fn" && ApexProtocol.LedId(k.Code) == usage)?.Code;

    private static InputAction? DecodeMapping(ReadOnlySpan<byte> mapping, byte originalUsage)
    {
        if (mapping[0] == 0) { return InputAction.Disabled; }
        if (mapping[0] != 0x51) { return null; }
        var codes = new List<string>();
        for (var i = 1; i < 5; i++)
        {
            if (mapping[i] == 0) { continue; }
            if (Code(mapping[i]) is not { } code) { return null; }
            codes.Add(code);
        }
        return codes.Count == 0 ? InputAction.Disabled
            : codes.Count == 1 && mapping[1] == originalUsage ? InputAction.Default : InputAction.Keys([.. codes]);
    }

    private static byte[] EncodeMapping(InputAction action, byte originalUsage, bool meta)
    {
        if (action.Kind == InputActionKind.Disabled || (meta && action.Kind == InputActionKind.Default)) { return new byte[5]; }
        if (action.Kind == InputActionKind.Default) { return [0x51, originalUsage, 0, 0, 0]; }
        if (action.Kind == InputActionKind.Keys && action.KeyCodes.Count is > 0 and <= 4)
        {
            var result = new byte[5];
            result[0] = 0x51;
            for (var i = 0; i < action.KeyCodes.Count; i++)
            {
                var usage = ApexProtocol.LedId(action.KeyCodes[i]);
                if (usage is null or 0) { throw new InvalidOperationException("Destination key has no verified HID usage."); }
                result[i + 1] = usage.Value;
            }
            return result;
        }

        // ponytail: preserve existing raw macros/media; add friendly encoding only after a differential capture.
        throw new InvalidOperationException($"Assignment '{action.Describe()}' has no verified encoding in this editor. The CLI accepts five raw bytes; the original profile is preserved.");
    }

    private static Dictionary<byte, int> MakeIndexes()
    {
        int[] first = [29,48,46,31,17,32,33,34,22,35,36,37,50,49,23,24,15,18,30,19,21,47,16,45,20,44,1,2,3,4,5,6,7,8,9,10,41,70,67,14,60,11,12,25,26,27,40,38,39,0,51,52,53,28];
        var result = first.Select((index, i) => ((byte)(i + 4), index)).ToDictionary(v => v.Item1, v => v.index);
        for (var i = 0; i < 12; i++) { result[(byte)(58 + i)] = 71 + i; }
        int[] navigation = [83,84,85,86,87,88,92,90,91,89];
        for (var i = 0; i < navigation.Length; i++) { result[(byte)(73 + i)] = navigation[i]; }
        foreach (var (usage, index) in new (byte, int)[] { (100,43),(135,54),(136,62),(137,13),(138,61),(139,59),
            (224,56),(225,42),(226,58),(227,57),(228,66),(229,55),(230,63),(231,64),(240,65) }) { result[usage] = index; }
        return result;
    }
}
