namespace OneRGB.Application.Presentation;

/// <summary>
/// Uma tecla no mapa visual (posição e largura em unidades de tecla). <see cref="Id"/> é o código físico no padrão
/// W3C <c>KeyboardEvent.code</c> ("KeyW", "ShiftLeft", "Backquote"): estável entre idiomas de teclado e usado em
/// perfis, atribuições e mapas de LED. <see cref="Label"/> é só o texto da tecla.
/// </summary>
public sealed record KeyCap(string Id, string Label, double X, double Y, double Width = 1)
{
    public double Height { get; init; } = 1;

    public double CenterX => X + (Width / 2);

    public double CenterY => Y + (Height / 2);
}

/// <summary>Layout TKL (87 teclas, ANSI) do Apex Pro TKL para o editor de atuação, atribuições e RGB por tecla.</summary>
public static class KeyboardLayout
{
    public static IReadOnlyList<KeyCap> Tkl { get; } = Build();

    public static readonly string[] Wasd = ["KeyW", "KeyA", "KeyS", "KeyD"];

    private static readonly Dictionary<string, KeyCap> ByCode = Tkl.ToDictionary(k => k.Id, StringComparer.Ordinal);

    private static readonly Dictionary<string, KeyCap> ByLabel = Tkl.ToDictionary(k => k.Label, StringComparer.Ordinal);

    public static KeyCap? Find(string code) => ByCode.GetValueOrDefault(code);

    /// <summary>
    /// Código da tecla a partir do código ou do rótulo antigo ("W", "LShift"): configurações gravadas antes dos
    /// códigos W3C continuam valendo.
    /// </summary>
    public static string? Resolve(string codeOrLabel) =>
        ByCode.ContainsKey(codeOrLabel) ? codeOrLabel : ByLabel.GetValueOrDefault(codeOrLabel)?.Id;

    /// <summary>
    /// Código W3C da tecla física a partir do código virtual do Windows (VK), para o pulso por tecla do canvas. Teclas
    /// sem lugar no TKL devolvem nulo. <paramref name="extended"/> separa as direitas (Ctrl, Alt) e Enter do teclado
    /// numérico.
    /// </summary>
    public static string? CodeOfVirtualKey(int virtualKey, bool extended = false) => virtualKey switch
    {
        >= 0x41 and <= 0x5A => $"Key{(char)virtualKey}",
        >= 0x30 and <= 0x39 => $"Digit{(char)virtualKey}",
        >= 0x70 and <= 0x7B => string.Create(System.Globalization.CultureInfo.InvariantCulture, $"F{virtualKey - 0x6F}"),
        0x1B => "Escape",
        0x08 => "Backspace",
        0x09 => "Tab",
        0x0D => extended ? null : "Enter",
        0x14 => "CapsLock",
        0x20 => "Space",
        0x10 or 0xA0 => "ShiftLeft",
        0xA1 => "ShiftRight",
        0x11 => extended ? "ControlRight" : "ControlLeft",
        0xA2 => "ControlLeft",
        0xA3 => "ControlRight",
        0x12 => extended ? "AltRight" : "AltLeft",
        0xA4 => "AltLeft",
        0xA5 => "AltRight",
        0x5B => "MetaLeft",
        0x5D => "ContextMenu",
        0x2C => "PrintScreen",
        0x91 => "ScrollLock",
        0x13 => "Pause",
        0x2D => "Insert",
        0x24 => "Home",
        0x21 => "PageUp",
        0x2E => "Delete",
        0x23 => "End",
        0x22 => "PageDown",
        0x25 => "ArrowLeft",
        0x26 => "ArrowUp",
        0x27 => "ArrowRight",
        0x28 => "ArrowDown",
        0xC0 => "Backquote",
        0xBD => "Minus",
        0xBB => "Equal",
        0xDB => "BracketLeft",
        0xDD => "BracketRight",
        0xDC => "Backslash",
        0xBA => "Semicolon",
        0xDE => "Quote",
        0xBC => "Comma",
        0xBE => "Period",
        0xBF => "Slash",
        _ => null,
    };

    /// <summary>
    /// Teclas vizinhas (retângulos que se tocam ou quase, até <paramref name="reach"/> unidades): as que recebem o
    /// Protection Mode em volta das teclas protegidas (A5.7).
    /// </summary>
    public static IReadOnlyList<KeyCap> Neighbors(IReadOnlyList<KeyCap> layout, string code, double reach = 0.3)
    {
        ArgumentNullException.ThrowIfNull(layout);
        var key = layout.FirstOrDefault(k => string.Equals(k.Id, code, StringComparison.Ordinal));
        if (key is null)
        {
            return [];
        }

        return layout
            .Where(k => !ReferenceEquals(k, key)
                && k.X < key.X + key.Width + reach && key.X < k.X + k.Width + reach
                && k.Y < key.Y + key.Height + reach && key.Y < k.Y + k.Height + reach)
            .ToList();
    }

    private static List<KeyCap> Build()
    {
        var keys = new List<KeyCap>();
        void Row(double y, double x, params (string Code, string Label, double Width)[] items)
        {
            foreach (var (code, label, width) in items)
            {
                if (code.Length > 0)
                {
                    keys.Add(new KeyCap(code, label, x, y, width));
                }

                x += width;
            }
        }

        (string, string, double) Gap(double width) => (string.Empty, string.Empty, width);

        Row(0, 0, ("Escape", "Esc", 1), Gap(1), ("F1", "F1", 1), ("F2", "F2", 1), ("F3", "F3", 1), ("F4", "F4", 1), Gap(0.5),
            ("F5", "F5", 1), ("F6", "F6", 1), ("F7", "F7", 1), ("F8", "F8", 1), Gap(0.5), ("F9", "F9", 1), ("F10", "F10", 1),
            ("F11", "F11", 1), ("F12", "F12", 1), Gap(0.25), ("PrintScreen", "PrtSc", 1), ("ScrollLock", "ScrLk", 1), ("Pause", "Pause", 1));
        Row(1.25, 0, ("Backquote", "`", 1), ("Digit1", "1", 1), ("Digit2", "2", 1), ("Digit3", "3", 1), ("Digit4", "4", 1),
            ("Digit5", "5", 1), ("Digit6", "6", 1), ("Digit7", "7", 1), ("Digit8", "8", 1), ("Digit9", "9", 1), ("Digit0", "0", 1),
            ("Minus", "-", 1), ("Equal", "=", 1), ("Backspace", "Backspace", 2), Gap(0.25), ("Insert", "Ins", 1), ("Home", "Home", 1), ("PageUp", "PgUp", 1));
        Row(2.25, 0, ("Tab", "Tab", 1.5), ("KeyQ", "Q", 1), ("KeyW", "W", 1), ("KeyE", "E", 1), ("KeyR", "R", 1), ("KeyT", "T", 1),
            ("KeyY", "Y", 1), ("KeyU", "U", 1), ("KeyI", "I", 1), ("KeyO", "O", 1), ("KeyP", "P", 1), ("BracketLeft", "[", 1),
            ("BracketRight", "]", 1), ("Backslash", "\\", 1.5), Gap(0.25), ("Delete", "Del", 1), ("End", "End", 1), ("PageDown", "PgDn", 1));
        Row(3.25, 0, ("CapsLock", "Caps", 1.75), ("KeyA", "A", 1), ("KeyS", "S", 1), ("KeyD", "D", 1), ("KeyF", "F", 1), ("KeyG", "G", 1),
            ("KeyH", "H", 1), ("KeyJ", "J", 1), ("KeyK", "K", 1), ("KeyL", "L", 1), ("Semicolon", ";", 1), ("Quote", "'", 1), ("Enter", "Enter", 2.25));
        Row(4.25, 0, ("ShiftLeft", "LShift", 2.25), ("KeyZ", "Z", 1), ("KeyX", "X", 1), ("KeyC", "C", 1), ("KeyV", "V", 1), ("KeyB", "B", 1),
            ("KeyN", "N", 1), ("KeyM", "M", 1), ("Comma", ",", 1), ("Period", ".", 1), ("Slash", "/", 1), ("ShiftRight", "RShift", 2.75), Gap(1.25), ("ArrowUp", "Up", 1));
        Row(5.25, 0, ("ControlLeft", "LCtrl", 1.25), ("MetaLeft", "Win", 1.25), ("AltLeft", "LAlt", 1.25), ("Space", "Space", 6.25),
            ("AltRight", "RAlt", 1.25), ("Fn", "Fn", 1.25), ("ContextMenu", "Menu", 1.25), ("ControlRight", "RCtrl", 1.25), Gap(0.25),
            ("ArrowLeft", "Left", 1), ("ArrowDown", "Down", 1), ("ArrowRight", "Right", 1));
        return keys;
    }
}
