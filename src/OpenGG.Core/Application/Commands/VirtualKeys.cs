using System.Globalization;

namespace OneRGB.Application.Commands;

/// <summary>
/// Códigos de tecla virtual do Windows (VK_*) e os nomes canônicos do <see cref="KeyChord"/>. A janela converte a
/// tecla pressionada pelo código (os nomes do WPF para as teclas OEM repetem valores e variam de layout para layout),
/// e o registro de atalhos globais (RegisterHotKey) precisa do código de volta.
/// </summary>
public static class VirtualKeys
{
    private static readonly Dictionary<int, string> Names = Build();

    private static readonly Dictionary<string, int> Codes = Names.ToDictionary(p => p.Value, p => p.Key, StringComparer.OrdinalIgnoreCase);

    /// <summary>Todas as teclas conhecidas (para testes e para a tela de atalhos).</summary>
    public static IReadOnlyCollection<string> Known => Codes.Keys;

    /// <summary>Nome canônico da tecla ("K", "F5", "Comma"), ou nulo para teclas sem atalho (modificadores, IME).</summary>
    public static string? NameOf(int virtualKey) => Names.GetValueOrDefault(virtualKey);

    /// <summary>Código VK de um nome canônico, ou nulo se desconhecido.</summary>
    public static int? CodeOf(string key)
    {
        ArgumentNullException.ThrowIfNull(key);
        return Codes.TryGetValue(key, out var code) ? code : null;
    }

    /// <summary>Shift, Ctrl, Alt e Win (esquerdo, direito ou genérico) não formam atalho sozinhos.</summary>
    public static bool IsModifier(int virtualKey) => virtualKey is 0x10 or 0x11 or 0x12 or 0x5B or 0x5C or (>= 0xA0 and <= 0xA5);

    /// <summary>Combinação a partir da tecla pressionada; nulo quando ela sozinha não pode ser atalho.</summary>
    public static KeyChord? Chord(int virtualKey, KeyModifiers modifiers) =>
        IsModifier(virtualKey) || NameOf(virtualKey) is not { } name ? null : new KeyChord(modifiers, name);

    private static Dictionary<int, string> Build()
    {
        var map = new Dictionary<int, string>
        {
            [0x08] = "Backspace",
            [0x09] = "Tab",
            [0x0D] = "Enter",
            [0x13] = "Pause",
            [0x14] = "CapsLock",
            [0x1B] = "Escape",
            [0x20] = "Space",
            [0x21] = "PageUp",
            [0x22] = "PageDown",
            [0x23] = "End",
            [0x24] = "Home",
            [0x25] = "Left",
            [0x26] = "Up",
            [0x27] = "Right",
            [0x28] = "Down",
            [0x2C] = "PrintScreen",
            [0x2D] = "Insert",
            [0x2E] = "Delete",
            [0x5D] = "Apps",
            [0x6A] = "Multiply",
            [0x6B] = "NumpadPlus",
            [0x6D] = "NumpadMinus",
            [0x6E] = "Decimal",
            [0x6F] = "Divide",
            [0x90] = "NumLock",
            [0x91] = "ScrollLock",
            [0xAD] = "VolumeMute",
            [0xAE] = "VolumeDown",
            [0xAF] = "VolumeUp",
            [0xB0] = "NextTrack",
            [0xB1] = "PreviousTrack",
            [0xB2] = "Stop",
            [0xB3] = "PlayPause",
            [0xBA] = "Semicolon",
            [0xBB] = "Plus",
            [0xBC] = "Comma",
            [0xBD] = "Minus",
            [0xBE] = "Period",
            [0xBF] = "Slash",
            [0xC0] = "Tilde",
            [0xDB] = "OpenBracket",
            [0xDC] = "Backslash",
            [0xDD] = "CloseBracket",
            [0xDE] = "Quote",
        };

        for (var letter = 'A'; letter <= 'Z'; letter++)
        {
            map[letter] = letter.ToString();
        }

        for (var digit = 0; digit <= 9; digit++)
        {
            map[0x30 + digit] = digit.ToString(CultureInfo.InvariantCulture);
            map[0x60 + digit] = "Numpad" + digit.ToString(CultureInfo.InvariantCulture);
        }

        for (var function = 1; function <= 24; function++)
        {
            map[0x6F + function] = "F" + function.ToString(CultureInfo.InvariantCulture);
        }

        return map;
    }
}
