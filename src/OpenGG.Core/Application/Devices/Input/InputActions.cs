using OneRGB.Application.Control;
using OneRGB.Application.Presentation;

namespace OneRGB.Application.Devices.Input;

/// <summary>O que uma tecla ou botão faz (A6.6, A5.4): a função de fábrica, nada, teclas, mouse, mídia, macro…</summary>
public enum InputActionKind
{
    /// <summary>A função original da tecla/botão.</summary>
    Default,

    Disabled,

    /// <summary>Uma tecla ou combinação ("ControlLeft+KeyC").</summary>
    Keys,

    Mouse,

    Media,

    Macro,

    /// <summary>Abre um programa (caminho absoluto, sem argumentos).</summary>
    LaunchApp,

    /// <summary>Comando do OneRGB ("devices.rescan").</summary>
    Command,

    /// <summary>Ativa um perfil.</summary>
    Profile,

    /// <summary>DPI: sniper enquanto segura, próximo estágio, subir, descer (mouse).</summary>
    Dpi,

    /// <summary>Camada Meta/alternativa enquanto segura ou alternando (teclado).</summary>
    Layer,
}

public enum MouseAction
{
    Left,
    Right,
    Middle,
    Back,
    Forward,
    DoubleClick,
    ScrollUp,
    ScrollDown,
    ScrollLeft,
    ScrollRight,
}

public enum MediaAction
{
    PlayPause,
    Next,
    Previous,
    Stop,
    VolumeUp,
    VolumeDown,
    Mute,
    MicMute,
}

public enum DpiAction
{
    /// <summary>Sniper: DPI reduzido só enquanto o botão está pressionado.</summary>
    Shift,
    Cycle,
    Up,
    Down,
}

public enum LayerAction
{
    Hold,
    Toggle,
}

/// <summary>
/// Uma atribuição, gravável como texto canônico ("key:ControlLeft+KeyC", "mouse:Back", "macro:recarga") — é assim
/// que ela entra em perfis, no jornal e no desfazer. Dado, nunca script (§539).
/// </summary>
public sealed record InputAction(InputActionKind Kind, string? Value = null)
{
    public static InputAction Default { get; } = new(InputActionKind.Default);

    public static InputAction Disabled { get; } = new(InputActionKind.Disabled);

    public static InputAction Keys(params string[] codes) => new(InputActionKind.Keys, string.Join('+', InputCodes.Canonical(codes)));

    public static InputAction MouseButton(MouseAction action) => new(InputActionKind.Mouse, action.ToString());

    public static InputAction MediaKey(MediaAction action) => new(InputActionKind.Media, action.ToString());

    public static InputAction RunMacro(string id) => new(InputActionKind.Macro, id);

    public static InputAction Launch(string path) => new(InputActionKind.LaunchApp, path);

    public static InputAction RunCommand(string id) => new(InputActionKind.Command, id);

    public static InputAction ActivateProfile(string id) => new(InputActionKind.Profile, id);

    public static InputAction DpiButton(DpiAction action) => new(InputActionKind.Dpi, action.ToString());

    public static InputAction LayerKey(LayerAction action) => new(InputActionKind.Layer, action.ToString());

    /// <summary>Códigos de tecla de uma ação <see cref="InputActionKind.Keys"/>.</summary>
    public IReadOnlyList<string> KeyCodes => Kind == InputActionKind.Keys && Value is { Length: > 0 } ? Value.Split('+') : [];

    /// <summary>Emite teclado (serve para Rapid Tap e 2-em-1, que decidem entre teclas).</summary>
    public bool EmitsKeys => Kind is InputActionKind.Default or InputActionKind.Keys;

    public string Encode() => Kind switch
    {
        InputActionKind.Default => "default",
        InputActionKind.Disabled => "none",
        _ => $"{Prefix(Kind)}:{Value}",
    };

    public static InputAction Parse(string text) =>
        TryParse(text, out var action, out var error) ? action : throw new FormatException(error);

    /// <summary>Lê a forma canônica. Não confere se macro/perfil/comando existem: isso é <see cref="InputActions.Validate"/>.</summary>
    public static bool TryParse(string? text, out InputAction action, out string? error)
    {
        action = Default;
        error = null;
        var value = text?.Trim() ?? string.Empty;
        switch (value)
        {
            case "" or "default":
                return true;
            case "none":
                action = Disabled;
                return true;
        }

        var split = value.IndexOf(':', StringComparison.Ordinal);
        if (split <= 0 || split == value.Length - 1)
        {
            error = $"Invalid assignment: '{value}'.";
            return false;
        }

        var prefix = value[..split];
        var rest = value[(split + 1)..];
        InputActionKind? kind = prefix switch
        {
            "key" => InputActionKind.Keys,
            "mouse" => InputActionKind.Mouse,
            "media" => InputActionKind.Media,
            "macro" => InputActionKind.Macro,
            "app" => InputActionKind.LaunchApp,
            "cmd" => InputActionKind.Command,
            "profile" => InputActionKind.Profile,
            "dpi" => InputActionKind.Dpi,
            "layer" => InputActionKind.Layer,
            _ => null,
        };
        if (kind is null)
        {
            error = $"Unknown assignment type: '{prefix}'.";
            return false;
        }

        action = kind == InputActionKind.Keys ? Keys(rest.Split('+')) : new InputAction(kind.Value, rest);
        return true;
    }

    /// <summary>Texto para a interface: "Ctrl + C", "Back button", "Macro recarga".</summary>
    public string Describe(Func<string, string?>? nameOf = null) => Kind switch
    {
        InputActionKind.Default => "Original action",
        InputActionKind.Disabled => "Disabled",
        InputActionKind.Keys => string.Join(" + ", KeyCodes.Select(InputCodes.Label)),
        InputActionKind.Mouse => InputActions.IsName<MouseAction>(Value) ? MouseLabel(Enum.Parse<MouseAction>(Value!)) : Value ?? string.Empty,
        InputActionKind.Media => InputActions.IsName<MediaAction>(Value) ? MediaLabel(Enum.Parse<MediaAction>(Value!)) : Value ?? string.Empty,
        InputActionKind.Macro => $"Macro {nameOf?.Invoke($"macro:{Value}") ?? Value}",
        InputActionKind.LaunchApp => $"Open {WindowsPaths.FileName(Value ?? string.Empty)}",
        InputActionKind.Command => nameOf?.Invoke($"cmd:{Value}") ?? $"Command {Value}",
        InputActionKind.Profile => $"Profile {nameOf?.Invoke($"profile:{Value}") ?? Value}",
        InputActionKind.Dpi => Value switch
        {
            nameof(DpiAction.Shift) => "DPI sniper (while held)",
            nameof(DpiAction.Cycle) => "Next DPI stage",
            nameof(DpiAction.Up) => "Increase DPI",
            nameof(DpiAction.Down) => "Decrease DPI",
            _ => $"DPI {Value}",
        },
        InputActionKind.Layer => Value == nameof(LayerAction.Toggle) ? "Toggle Meta layer" : "Meta layer (while held)",
        _ => Value ?? string.Empty,
    };

    public override string ToString() => Encode();

    private static string Prefix(InputActionKind kind) => kind switch
    {
        InputActionKind.Keys => "key",
        InputActionKind.Mouse => "mouse",
        InputActionKind.Media => "media",
        InputActionKind.Macro => "macro",
        InputActionKind.LaunchApp => "app",
        InputActionKind.Command => "cmd",
        InputActionKind.Profile => "profile",
        InputActionKind.Dpi => "dpi",
        InputActionKind.Layer => "layer",
        _ => kind.ToString().ToLowerInvariant(),
    };

    private static string MouseLabel(MouseAction action) => action switch
    {
        MouseAction.Left => "Left click",
        MouseAction.Right => "Right click",
        MouseAction.Middle => "Middle click",
        MouseAction.Back => "Back button",
        MouseAction.Forward => "Forward button",
        MouseAction.DoubleClick => "Double click",
        MouseAction.ScrollUp => "Scroll up",
        MouseAction.ScrollDown => "Scroll down",
        MouseAction.ScrollLeft => "Scroll left",
        _ => "Scroll right",
    };

    private static string MediaLabel(MediaAction action) => action switch
    {
        MediaAction.PlayPause => "Play/pause",
        MediaAction.Next => "Next track",
        MediaAction.Previous => "Previous track",
        MediaAction.Stop => "Stop",
        MediaAction.VolumeUp => "Volume up",
        MediaAction.VolumeDown => "Volume down",
        MediaAction.Mute => "Mute",
        _ => "Microphone mute",
    };
}

/// <summary>Códigos de tecla aceitos em atribuições e macros (padrão W3C <c>KeyboardEvent.code</c>).</summary>
public static class InputCodes
{
    private static readonly string[] ModifierOrder = ["ControlLeft", "ControlRight", "ShiftLeft", "ShiftRight", "AltLeft", "AltRight", "MetaLeft", "MetaRight"];

    private static readonly HashSet<string> Extra = new(StringComparer.Ordinal)
    {
        "F13", "F14", "F15", "F16", "F17", "F18", "F19", "F20", "F21", "F22", "F23", "F24",
        "Numpad0", "Numpad1", "Numpad2", "Numpad3", "Numpad4", "Numpad5", "Numpad6", "Numpad7", "Numpad8", "Numpad9",
        "NumpadDecimal", "NumpadAdd", "NumpadSubtract", "NumpadMultiply", "NumpadDivide", "NumpadEnter", "NumLock",
        "IntlBackslash", "MetaRight",
    };

    private static readonly Dictionary<string, string> Labels = new(StringComparer.Ordinal)
    {
        ["ControlLeft"] = "Ctrl",
        ["ControlRight"] = "Right Ctrl",
        ["ShiftLeft"] = "Shift",
        ["ShiftRight"] = "Right Shift",
        ["AltLeft"] = "Alt",
        ["AltRight"] = "AltGr",
        ["MetaLeft"] = "Win",
        ["MetaRight"] = "Right Win",
        ["Space"] = "Space",
        ["ArrowUp"] = "↑",
        ["ArrowDown"] = "↓",
        ["ArrowLeft"] = "←",
        ["ArrowRight"] = "→",
    };

    public static bool IsKnown(string code) => KeyboardLayout.Find(code) is not null || Extra.Contains(code);

    public static bool IsModifier(string code) => ModifierOrder.Contains(code, StringComparer.Ordinal);

    public static string Label(string code) =>
        Labels.GetValueOrDefault(code) ?? KeyboardLayout.Find(code)?.Label ?? code.Replace("Numpad", "Num ", StringComparison.Ordinal);

    /// <summary>Modificadores primeiro, na ordem Ctrl, Shift, Alt, Win; a tecla principal no fim; sem repetição.</summary>
    public static IReadOnlyList<string> Canonical(IEnumerable<string> codes)
    {
        ArgumentNullException.ThrowIfNull(codes);
        var list = codes.Select(c => c.Trim()).Where(c => c.Length > 0).Distinct(StringComparer.Ordinal).ToList();
        return
        [
            .. ModifierOrder.Where(m => list.Contains(m, StringComparer.Ordinal)),
            .. list.Where(c => !IsModifier(c)),
        ];
    }
}

/// <summary>O que existe para validar referências de uma atribuição (macros, comandos, perfis).</summary>
public sealed record InputActionContext
{
    public Func<string, bool>? MacroExists { get; init; }

    public Func<string, bool>? CommandExists { get; init; }

    public Func<string, bool>? ProfileExists { get; init; }

    /// <summary>O dispositivo tem estágios de DPI (mouse).</summary>
    public bool AllowDpi { get; init; }

    /// <summary>O dispositivo tem camada Meta (teclado).</summary>
    public bool AllowLayer { get; init; }
}

public static class InputActions
{
    public const int MaximumKeysInChord = 4;

    /// <summary>
    /// Programas que executam texto arbitrário: atribuições (que podem chegar por perfil importado, §193) não
    /// abrem interpretadores.
    /// </summary>
    private static readonly HashSet<string> ScriptHosts = new(StringComparer.OrdinalIgnoreCase)
    {
        "cmd.exe", "powershell.exe", "pwsh.exe", "wscript.exe", "cscript.exe", "mshta.exe", "rundll32.exe",
        "regsvr32.exe", "bash.exe", "wsl.exe", "python.exe", "pythonw.exe", "node.exe", "msiexec.exe",
    };

    /// <summary>
    /// Atribuições prontas para uma lista de escolha (A6.6): a original, desligada, botões do mouse, mídia e, conforme o
    /// dispositivo, DPI (mouse) ou camada Meta (teclado). Teclas, macros e programas vêm do editor próprio.
    /// </summary>
    public static IReadOnlyList<ChoiceOption> Common(InputActionContext? context = null)
    {
        context ??= new InputActionContext();
        var actions = new List<InputAction> { InputAction.Default, InputAction.Disabled };
        actions.AddRange(Enum.GetValues<MouseAction>().Select(InputAction.MouseButton));
        actions.AddRange(Enum.GetValues<MediaAction>().Select(InputAction.MediaKey));
        if (context.AllowDpi)
        {
            actions.AddRange(Enum.GetValues<DpiAction>().Select(InputAction.DpiButton));
        }

        if (context.AllowLayer)
        {
            actions.AddRange(Enum.GetValues<LayerAction>().Select(InputAction.LayerKey));
        }

        return [.. actions.Select(a => new ChoiceOption(a.Encode(), a.Describe()))];
    }

    /// <summary>Motivo da recusa, ou nulo.</summary>
    public static string? Validate(InputAction action, InputActionContext? context = null)
    {
        ArgumentNullException.ThrowIfNull(action);
        context ??= new InputActionContext();
        switch (action.Kind)
        {
            case InputActionKind.Default or InputActionKind.Disabled:
                return null;
            case InputActionKind.Keys:
                var codes = action.KeyCodes;
                if (codes.Count == 0)
                {
                    return "Choose at least one key.";
                }

                if (codes.Count > MaximumKeysInChord)
                {
                    return $"Too many keys in the chord (maximum {MaximumKeysInChord}).";
                }

                return codes.FirstOrDefault(c => !InputCodes.IsKnown(c)) is { } unknown ? $"Unknown key: '{unknown}'." : null;
            case InputActionKind.Mouse:
                return IsName<MouseAction>(action.Value) ? null : $"Unknown mouse button: '{action.Value}'.";
            case InputActionKind.Media:
                return IsName<MediaAction>(action.Value) ? null : $"Unknown media key: '{action.Value}'.";
            case InputActionKind.Macro:
                return string.IsNullOrWhiteSpace(action.Value) ? "Choose a macro."
                    : context.MacroExists is { } macro && !macro(action.Value) ? $"Macro '{action.Value}' does not exist." : null;
            case InputActionKind.Command:
                return string.IsNullOrWhiteSpace(action.Value) ? "Choose a command."
                    : context.CommandExists is { } command && !command(action.Value) ? $"Command '{action.Value}' does not exist." : null;
            case InputActionKind.Profile:
                return string.IsNullOrWhiteSpace(action.Value) ? "Choose a profile."
                    : context.ProfileExists is { } profile && !profile(action.Value) ? $"Profile '{action.Value}' does not exist." : null;
            case InputActionKind.LaunchApp:
                return ValidateLaunch(action.Value);
            case InputActionKind.Dpi:
                if (!context.AllowDpi)
                {
                    return "This device has no DPI setting.";
                }

                return IsName<DpiAction>(action.Value) ? null : $"Unknown DPI action: '{action.Value}'.";
            case InputActionKind.Layer:
                if (!context.AllowLayer)
                {
                    return "This device has no Meta layer.";
                }

                return IsName<LayerAction>(action.Value) ? null : $"Unknown layer action: '{action.Value}'.";
            default:
                return "Unknown assignment.";
        }
    }

    /// <summary>Nome exato de um valor do enum (sem números nem listas com vírgula, que o Enum.TryParse aceitaria).</summary>
    public static bool IsName<TEnum>(string? value)
        where TEnum : struct, Enum =>
        value is not null && Enum.GetNames<TEnum>().Contains(value, StringComparer.Ordinal);

    /// <summary>Programa a abrir: caminho absoluto local de um .exe ou .lnk, nunca um interpretador de comandos.</summary>
    public static string? ValidateLaunch(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return "Choose a program.";
        }

        if (path.StartsWith(@"\\", StringComparison.Ordinal) || path.Contains("://", StringComparison.Ordinal))
        {
            return "Choose a program on this computer (no network paths or URLs).";
        }

        if (!WindowsPaths.IsAbsoluteLocal(path))
        {
            return "Use the program's full path (e.g. C:\\Program Files\\...\\app.exe).";
        }

        var extension = WindowsPaths.Extension(path);
        if (!string.Equals(extension, ".exe", StringComparison.OrdinalIgnoreCase) && !string.Equals(extension, ".lnk", StringComparison.OrdinalIgnoreCase))
        {
            return "Choose a program (.exe) or shortcut (.lnk).";
        }

        return ScriptHosts.Contains(WindowsPaths.FileName(path))
            ? "Command interpreters cannot be assigned to keys (an imported profile could use them to execute commands)."
            : null;
    }
}

/// <summary>
/// Caminhos do Windows lidos do mesmo jeito em qualquer sistema (os testes rodam no Linux, onde "\\" não separa
/// pastas para <see cref="Path"/>).
/// </summary>
public static class WindowsPaths
{
    private static readonly char[] Separators = ['\\', '/'];

    public static string FileName(string path) => path[(path.LastIndexOfAny(Separators) + 1)..];

    public static string Extension(string path)
    {
        var name = FileName(path);
        var dot = name.LastIndexOf('.');
        return dot <= 0 ? string.Empty : name[dot..];
    }

    /// <summary>Caminho absoluto numa unidade local ("C:\\..."), sem rede nem endereço.</summary>
    public static bool IsAbsoluteLocal(string path) =>
        path.Length > 3 && char.IsAsciiLetter(path[0]) && path[1] == ':' && path[2] is '\\' or '/'
        && !path.Contains("..", StringComparison.Ordinal);
}

public enum ConflictSeverity
{
    /// <summary>Funciona, mas talvez não como o usuário espera.</summary>
    Warning,

    /// <summary>Não pode ser aplicado assim.</summary>
    Error,
}

/// <summary>Duas funções disputando o mesmo controle físico (A6.6), ou uma combinação que trava o dispositivo.</summary>
public sealed record BindingConflict(string Control, ConflictSeverity Severity, string Message);
