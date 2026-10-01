using System.Globalization;

namespace OneRGB.Application.Commands;

/// <summary>Modificadores (mesmos valores do WPF e de RegisterHotKey para facilitar a conversão).</summary>
[Flags]
public enum KeyModifiers
{
    None = 0,
    Alt = 1,
    Control = 2,
    Shift = 4,
    Windows = 8,
}

/// <summary>Onde o atalho vale: só com a janela do OneRGB em foco, ou no Windows inteiro (RegisterHotKey).</summary>
public enum ShortcutScope
{
    App,
    Global,
}

/// <summary>
/// Combinação de teclas com nome canônico da tecla ("K", "F5", "1", "Space", "Comma", "VolumeMute").
/// O texto é o que a interface mostra e o que as configurações gravam: <c>Ctrl+Shift+K</c>.
/// </summary>
public readonly record struct KeyChord(KeyModifiers Modifiers, string Key)
{
    private static readonly Dictionary<string, string> Aliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Esc"] = "Escape",
        ["Del"] = "Delete",
        ["Ins"] = "Insert",
        ["Return"] = "Enter",
        ["PgUp"] = "PageUp",
        ["Prior"] = "PageUp",
        ["PgDn"] = "PageDown",
        ["Next"] = "PageDown",
        ["Spacebar"] = "Space",
        ["Space"] = "Space",
        ["Back"] = "Backspace",
        ["OemComma"] = "Comma",
        [","] = "Comma",
        ["OemPeriod"] = "Period",
        ["."] = "Period",
        ["OemPlus"] = "Plus",
        ["+"] = "Plus",
        ["OemMinus"] = "Minus",
        ["-"] = "Minus",
        ["Subtract"] = "NumpadMinus",
        ["Add"] = "NumpadPlus",
        ["PrtSc"] = "PrintScreen",
        ["Snapshot"] = "PrintScreen",
        ["Print"] = "PrintScreen",
        ["Scroll"] = "ScrollLock",
        ["/"] = "Slash",
        ["OemQuestion"] = "Slash",
        ["\\"] = "Backslash",
        [";"] = "Semicolon",
        ["MediaPlayPause"] = "PlayPause",
        ["MediaNextTrack"] = "NextTrack",
        ["MediaPreviousTrack"] = "PreviousTrack",
        ["MediaStop"] = "Stop",
    };

    private static readonly HashSet<string> Named = new(StringComparer.OrdinalIgnoreCase)
    {
        "Escape", "Delete", "Insert", "Enter", "PageUp", "PageDown", "Home", "End", "Space", "Tab", "Backspace",
        "Up", "Down", "Left", "Right", "Comma", "Period", "Plus", "Minus", "Slash", "Backslash", "Semicolon", "Quote",
        "Tilde", "OpenBracket", "CloseBracket", "PrintScreen", "Pause", "ScrollLock", "CapsLock", "NumLock", "Apps",
        "NumpadPlus", "NumpadMinus", "Multiply", "Divide", "Decimal",
        "VolumeUp", "VolumeDown", "VolumeMute", "PlayPause", "NextTrack", "PreviousTrack", "Stop",
    };

    public bool IsEmpty => string.IsNullOrEmpty(Key);

    /// <summary>F13–F24, mídia e Pause podem ser globais sem modificador (não atrapalham a digitação).</summary>
    public bool IsStandaloneSafe => IsFunctionKey(Key, 13, 24)
        || Key is "VolumeUp" or "VolumeDown" or "VolumeMute" or "PlayPause" or "NextTrack" or "PreviousTrack" or "Stop" or "Pause" or "ScrollLock";

    public static KeyChord Parse(string text) =>
        TryParse(text, out var chord) ? chord : throw new FormatException($"Invalid shortcut: '{text}'.");

    /// <summary>Lê "Ctrl+Shift+K", "ctrl + alt + m", "Win+F13", "Ctrl+,". Modificador sozinho não é atalho.</summary>
    public static bool TryParse(string? text, out KeyChord chord)
    {
        chord = default;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var trimmed = text.Trim();

        // "Ctrl++" and "Ctrl+," : o último "+" pode ser a própria tecla.
        var parts = trimmed.EndsWith("++", StringComparison.Ordinal)
            ? [.. trimmed[..^2].Split('+', StringSplitOptions.TrimEntries), "+"]
            : trimmed.Split('+', StringSplitOptions.TrimEntries);
        var modifiers = KeyModifiers.None;
        string? key = null;
        foreach (var part in parts)
        {
            if (part.Length == 0)
            {
                return false;
            }

            var modifier = part.ToUpperInvariant() switch
            {
                "CTRL" or "CONTROL" or "CTL" => KeyModifiers.Control,
                "ALT" => KeyModifiers.Alt,
                "SHIFT" => KeyModifiers.Shift,
                "WIN" or "WINDOWS" or "META" or "SUPER" => KeyModifiers.Windows,
                _ => KeyModifiers.None,
            };
            if (modifier != KeyModifiers.None)
            {
                if (key is not null || modifiers.HasFlag(modifier))
                {
                    return false;
                }

                modifiers |= modifier;
                continue;
            }

            if (key is not null || NormalizeKey(part) is not { } normalized)
            {
                return false;
            }

            key = normalized;
        }

        if (key is null)
        {
            return false;
        }

        chord = new KeyChord(modifiers, key);
        return true;
    }

    /// <summary>Nome canônico de uma tecla, ou <c>null</c> se desconhecida. Aceita nomes do WPF ("D1", "OemComma", "Next").</summary>
    public static string? NormalizeKey(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        var key = Aliases.TryGetValue(name, out var alias) ? alias : name;
        if (key.Length == 1 && char.IsAsciiLetterOrDigit(key[0]))
        {
            return key.ToUpperInvariant();
        }

        if (key.Length == 2 && key[0] is 'D' or 'd' && char.IsAsciiDigit(key[1]))
        {
            return key[1].ToString();
        }

        if (key.StartsWith("NumPad", StringComparison.OrdinalIgnoreCase) && key.Length == 7 && char.IsAsciiDigit(key[6]))
        {
            return "Numpad" + key[6];
        }

        if (IsFunctionKey(key, 1, 24))
        {
            return key.ToUpperInvariant();
        }

        return Named.TryGetValue(key, out var canonical) ? canonical : null;
    }

    public override string ToString()
    {
        if (IsEmpty)
        {
            return string.Empty;
        }

        var parts = new List<string>(5);
        if (Modifiers.HasFlag(KeyModifiers.Control))
        {
            parts.Add("Ctrl");
        }

        if (Modifiers.HasFlag(KeyModifiers.Alt))
        {
            parts.Add("Alt");
        }

        if (Modifiers.HasFlag(KeyModifiers.Shift))
        {
            parts.Add("Shift");
        }

        if (Modifiers.HasFlag(KeyModifiers.Windows))
        {
            parts.Add("Win");
        }

        parts.Add(Key switch
        {
            "Comma" => ",",
            "Period" => ".",
            "Plus" => "+",
            "Minus" => "-",
            "Slash" => "/",
            _ => Key,
        });
        return string.Join('+', parts);
    }

    /// <summary>
    /// Teclas como aparecem no <c>Kbd</c> (porte de shadcn-ui/kbd, MIT): uma tecla por capa, setas como símbolos e
    /// nomes em português ("Space"). Texto que não é atalho vira uma capa só.
    /// </summary>
    public static IReadOnlyList<string> Caps(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        if (!TryParse(text, out var chord))
        {
            return [text.Trim()];
        }

        var caps = new List<string>(5);
        if (chord.Modifiers.HasFlag(KeyModifiers.Control))
        {
            caps.Add("Ctrl");
        }

        if (chord.Modifiers.HasFlag(KeyModifiers.Alt))
        {
            caps.Add("Alt");
        }

        if (chord.Modifiers.HasFlag(KeyModifiers.Shift))
        {
            caps.Add("Shift");
        }

        if (chord.Modifiers.HasFlag(KeyModifiers.Windows))
        {
            caps.Add("Win");
        }

        caps.Add(chord.Key switch
        {
            "Up" => "↑",
            "Down" => "↓",
            "Left" => "←",
            "Right" => "→",
            "Space" => "Space",
            "Escape" => "Esc",
            "Backspace" => "⌫",
            "PageUp" => "PgUp",
            "PageDown" => "PgDn",
            "Delete" => "Del",
            "Comma" => ",",
            "Period" => ".",
            "Plus" => "+",
            "Minus" => "-",
            "Slash" => "/",
            _ => chord.Key,
        });
        return caps;
    }

    private static bool IsFunctionKey(string key, int from, int to) =>
        key.Length is 2 or 3 && key[0] is 'F' or 'f'
        && int.TryParse(key.AsSpan(1), NumberStyles.None, CultureInfo.InvariantCulture, out var n) && n >= from && n <= to;
}

