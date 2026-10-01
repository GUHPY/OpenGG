using System.Globalization;
using OneRGB.Application.Presentation;

namespace OneRGB.Application.Devices.Input;

public enum MacroStepKind
{
    KeyDown,
    KeyUp,

    /// <summary>Pressiona e solta a tecla.</summary>
    KeyTap,

    MouseDown,
    MouseUp,

    Delay,

    /// <summary>Digita um texto (sem interpretar nada: é só texto).</summary>
    Text,
}

/// <summary>Um passo de macro. <see cref="Code"/> é tecla (W3C) ou botão (<see cref="MouseAction"/>).</summary>
public sealed record MacroStep(MacroStepKind Kind, string? Code = null, int Milliseconds = 0, string? Text = null)
{
    public static MacroStep Down(string code) => new(MacroStepKind.KeyDown, code);

    public static MacroStep Up(string code) => new(MacroStepKind.KeyUp, code);

    public static MacroStep Tap(string code) => new(MacroStepKind.KeyTap, code);

    public static MacroStep Wait(int milliseconds) => new(MacroStepKind.Delay, Milliseconds: milliseconds);

    public static MacroStep Type(string text) => new(MacroStepKind.Text, Text: text);
}

public enum MacroRepeat
{
    Once,

    /// <summary>Repete enquanto a tecla está pressionada.</summary>
    WhileHeld,

    /// <summary>Um toque liga, outro desliga.</summary>
    Toggle,

    /// <summary>Repete <see cref="Macro.Times"/> vezes.</summary>
    Times,
}

/// <summary>Macro como dado (§539): passos e tempos, sem código. Fica no perfil e pode ser exportada.</summary>
public sealed record Macro
{
    public required string Id { get; init; }

    public required string Name { get; init; }

    public IReadOnlyList<MacroStep> Steps { get; init; } = [];

    public MacroRepeat Repeat { get; init; }

    public int Times { get; init; } = 1;

    /// <summary>Usa as pausas gravadas; desligado, cada passo espera <see cref="FixedDelayMs"/>.</summary>
    public bool KeepRecordedDelays { get; init; } = true;

    public int FixedDelayMs { get; init; } = 20;
}

/// <summary>Um evento da macro no tempo (prévia e testes).</summary>
public sealed record MacroEvent(TimeSpan At, MacroStepKind Kind, string? Code, string? Text);

public static class Macros
{
    public const int MaximumSteps = 500;
    public const int MaximumDelayMs = 60_000;
    public const int MaximumTextLength = 200;
    public const int MaximumTimes = 100;

    /// <summary>Todos os problemas da macro (lista vazia = pode salvar).</summary>
    public static IReadOnlyList<string> Validate(Macro macro)
    {
        ArgumentNullException.ThrowIfNull(macro);
        var problems = new List<string>();
        if (string.IsNullOrWhiteSpace(macro.Id))
        {
            problems.Add("Macro has no ID.");
        }

        if (string.IsNullOrWhiteSpace(macro.Name))
        {
            problems.Add("Enter a macro name.");
        }

        if (macro.Steps.Count == 0)
        {
            problems.Add("The macro has no steps.");
        }

        if (macro.Steps.Count > MaximumSteps)
        {
            problems.Add($"Too many steps (maximum {MaximumSteps}).");
        }

        if (macro.Repeat == MacroRepeat.Times && macro.Times is < 1 or > MaximumTimes)
        {
            problems.Add($"Repeat between 1 and {MaximumTimes} times.");
        }

        if (!macro.KeepRecordedDelays && macro.FixedDelayMs is < 0 or > MaximumDelayMs)
        {
            problems.Add($"Pausa fixa entre 0 e {MaximumDelayMs} ms.");
        }

        var held = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < macro.Steps.Count; i++)
        {
            var step = macro.Steps[i];
            var where = $"Step {i + 1}";
            switch (step.Kind)
            {
                case MacroStepKind.KeyDown or MacroStepKind.KeyUp or MacroStepKind.KeyTap:
                    if (step.Code is null || !InputCodes.IsKnown(step.Code))
                    {
                        problems.Add($"{where}: unknown key '{step.Code}'.");
                        break;
                    }

                    Track(step.Kind == MacroStepKind.KeyDown, step.Kind == MacroStepKind.KeyUp, $"key:{step.Code}", InputCodes.Label(step.Code), where);
                    break;
                case MacroStepKind.MouseDown or MacroStepKind.MouseUp:
                    if (!InputActions.IsName<MouseAction>(step.Code) || step.Code is not (nameof(MouseAction.Left) or nameof(MouseAction.Right)
                        or nameof(MouseAction.Middle) or nameof(MouseAction.Back) or nameof(MouseAction.Forward)))
                    {
                        problems.Add($"{where}: invalid mouse button '{step.Code}'.");
                        break;
                    }

                    Track(step.Kind == MacroStepKind.MouseDown, step.Kind == MacroStepKind.MouseUp, $"mouse:{step.Code}", step.Code, where);
                    break;
                case MacroStepKind.Delay:
                    if (step.Milliseconds is < 0 or > MaximumDelayMs)
                    {
                        problems.Add($"{where}: pausa entre 0 e {MaximumDelayMs} ms.");
                    }

                    break;
                case MacroStepKind.Text:
                    if (string.IsNullOrEmpty(step.Text) || step.Text.Length > MaximumTextLength)
                    {
                        problems.Add($"{where}: text must contain 1–{MaximumTextLength} characters.");
                    }
                    else if (step.Text.Any(c => char.IsControl(c) && c is not '\n' and not '\t'))
                    {
                        problems.Add($"{where}: text contains control characters.");
                    }

                    break;
            }
        }

        if (held.Count > 0)
        {
            problems.Add($"The macro ends with a held key: {string.Join(", ", held.Select(h => h[(h.IndexOf(':', StringComparison.Ordinal) + 1)..]))}.");
        }

        return problems;

        void Track(bool down, bool up, string id, string label, string where)
        {
            if (down && !held.Add(id))
            {
                problems.Add($"{where}: {label} was already held.");
            }
            else if (up && !held.Remove(id))
            {
                problems.Add($"{where}: release {label} without pressing it first.");
            }
        }
    }

    /// <summary>Linha do tempo de uma execução (sem repetição).</summary>
    public static IReadOnlyList<MacroEvent> Timeline(Macro macro)
    {
        ArgumentNullException.ThrowIfNull(macro);
        var events = new List<MacroEvent>();
        var at = TimeSpan.Zero;
        foreach (var step in macro.Steps)
        {
            if (step.Kind == MacroStepKind.Delay)
            {
                if (macro.KeepRecordedDelays)
                {
                    at += TimeSpan.FromMilliseconds(step.Milliseconds);
                }

                continue;
            }

            events.Add(new MacroEvent(at, step.Kind, step.Code, step.Text));
            if (!macro.KeepRecordedDelays)
            {
                at += TimeSpan.FromMilliseconds(macro.FixedDelayMs);
            }
        }

        return events;
    }

    /// <summary>Duração de uma execução.</summary>
    public static TimeSpan Duration(Macro macro)
    {
        ArgumentNullException.ThrowIfNull(macro);
        if (macro.KeepRecordedDelays)
        {
            return TimeSpan.FromMilliseconds(macro.Steps.Where(s => s.Kind == MacroStepKind.Delay).Sum(s => (long)s.Milliseconds));
        }

        var actions = macro.Steps.Count(s => s.Kind != MacroStepKind.Delay);
        return TimeSpan.FromMilliseconds((long)actions * macro.FixedDelayMs);
    }

    /// <summary>
    /// Monta a macro a partir de uma gravação (tecla desce/sobe com o horário). Pausas menores que
    /// <paramref name="minimumDelay"/> somem (ruído do teclado) e as maiores que o máximo são cortadas.
    /// </summary>
    public static Macro FromRecording(string id, string name, IEnumerable<(TimeSpan At, bool Down, string Code)> recording, TimeSpan? minimumDelay = null)
    {
        ArgumentNullException.ThrowIfNull(recording);
        var threshold = minimumDelay ?? TimeSpan.FromMilliseconds(5);
        var steps = new List<MacroStep>();
        TimeSpan? last = null;
        var held = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (at, down, code) in recording.OrderBy(r => r.At))
        {
            if (!down && !held.Contains(code))
            {
                // Soltar sem ter apertado dentro da gravação (a tecla já estava pressionada quando começou).
                continue;
            }

            if (down && !held.Add(code))
            {
                // Repetição automática do Windows enquanto segura: não é um novo toque.
                continue;
            }

            if (!down)
            {
                held.Remove(code);
            }

            if (last is { } previous && at - previous >= threshold)
            {
                steps.Add(MacroStep.Wait((int)Math.Min(MaximumDelayMs, Math.Round((at - previous).TotalMilliseconds))));
            }

            steps.Add(down ? MacroStep.Down(code) : MacroStep.Up(code));
            last = at;
        }

        // Tecla que ficou pressionada no fim da gravação é solta, para a macro nunca travar uma tecla.
        steps.AddRange(held.Order(StringComparer.Ordinal).Select(MacroStep.Up));
        return new Macro { Id = id, Name = name, Steps = steps };
    }
}

/// <summary>
/// Passos de macro como texto editável, um por linha: <c>tap W</c>, <c>down Shift</c>, <c>up Shift</c>,
/// <c>mdown Left</c>, <c>mup Left</c>, <c>wait 50</c> e <c>type texto</c>. Teclas por código (W3C) ou rótulo do layout.
/// </summary>
public static class MacroText
{
    public static string Format(IEnumerable<MacroStep> steps)
    {
        ArgumentNullException.ThrowIfNull(steps);
        return string.Join(Environment.NewLine, steps.Select(step => step.Kind switch
        {
            MacroStepKind.KeyTap => $"tap {step.Code}",
            MacroStepKind.KeyDown => $"down {step.Code}",
            MacroStepKind.KeyUp => $"up {step.Code}",
            MacroStepKind.MouseDown => $"mdown {step.Code}",
            MacroStepKind.MouseUp => $"mup {step.Code}",
            MacroStepKind.Delay => string.Create(CultureInfo.InvariantCulture, $"wait {step.Milliseconds}"),
            _ => $"type {step.Text}",
        }));
    }

    /// <summary>Lê os passos; o erro diz a linha. Os limites (passos, pausas, texto) são de <see cref="Macros.Validate"/>.</summary>
    public static bool TryParse(string? text, out IReadOnlyList<MacroStep> steps, out string? error)
    {
        var list = new List<MacroStep>();
        steps = list;
        error = null;
        var lines = (text ?? string.Empty).Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i].TrimEnd('\r');
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var trimmed = line.TrimStart();
            var space = trimmed.IndexOf(' ', StringComparison.Ordinal);
            var verb = (space < 0 ? trimmed : trimmed[..space]).ToLowerInvariant();
            var rest = space < 0 ? string.Empty : trimmed[(space + 1)..];
            var argument = rest.Trim();
            MacroStep? step = verb switch
            {
                "type" when rest.Length > 0 => MacroStep.Type(rest),
                "wait" when int.TryParse(argument, NumberStyles.None, CultureInfo.InvariantCulture, out var ms) => MacroStep.Wait(ms),
                "tap" or "down" or "up" when Key(argument) is { } code => new MacroStep(
                    verb == "tap" ? MacroStepKind.KeyTap : verb == "down" ? MacroStepKind.KeyDown : MacroStepKind.KeyUp, code),
                "mdown" or "mup" when Enum.TryParse<MouseAction>(argument, ignoreCase: true, out var button) && Enum.IsDefined(button) =>
                    new MacroStep(verb == "mdown" ? MacroStepKind.MouseDown : MacroStepKind.MouseUp, button.ToString()),
                _ => null,
            };
            if (step is null)
            {
                error = $"Line {i + 1}: \"{trimmed}\" is not a step (use tap, down, up, mdown, mup, wait or type).";
                return false;
            }

            list.Add(step);
        }

        return true;
    }

    private static string? Key(string text)
    {
        if (text.Length == 0)
        {
            return null;
        }

        if (InputCodes.IsKnown(text))
        {
            return text;
        }

        return KeyboardLayout.Resolve(text) is { } code && InputCodes.IsKnown(code) ? code : null;
    }
}
