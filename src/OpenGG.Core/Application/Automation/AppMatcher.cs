namespace OneRGB.Application.Automation;
public sealed record ProcessInfo(int ProcessId, string Name, string? Path = null, string? Publisher = null, string? Sha256 = null, string? WindowTitle = null);

public enum MatchStrength
{
    /// <summary>Só o nome do executável: qualquer programa com o mesmo nome casa.</summary>
    Name,

    /// <summary>Nome mais pasta ou editor assinado.</summary>
    Strong,

    /// <summary>Hash do arquivo: só aquele binário.</summary>
    Exact,
}

/// <summary>
/// Como reconhecer um aplicativo (spec §22, §383): nome do executável (aceita <c>*</c>) e, opcionalmente, pasta,
/// editor da assinatura, hash e trecho do título da janela. Todos os sinais informados precisam casar.
/// </summary>
public sealed record AppMatcher
{
    public required string Executable { get; init; }

    public string? PathPrefix { get; init; }

    public string? Publisher { get; init; }

    public string? Sha256 { get; init; }

    public string? WindowTitle { get; init; }

    public MatchStrength Strength => Sha256 is not null ? MatchStrength.Exact
        : PathPrefix is not null || Publisher is not null ? MatchStrength.Strong
        : MatchStrength.Name;

    public bool Matches(ProcessInfo process)
    {
        ArgumentNullException.ThrowIfNull(process);
        if (!Wildcard.Matches(Executable, process.Name, ignoreCase: true))
        {
            return false;
        }

        if (PathPrefix is { } prefix && (process.Path is null || !process.Path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        if (Publisher is { } publisher && !string.Equals(process.Publisher, publisher, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (Sha256 is { } hash && !string.Equals(process.Sha256, hash, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return WindowTitle is not { } title || (process.WindowTitle?.Contains(title, StringComparison.OrdinalIgnoreCase) ?? false);
    }

    public override string ToString() => Executable;
}

public static class Wildcard
{
    public static bool Matches(string pattern, string value, bool ignoreCase = false)
    {
        ArgumentNullException.ThrowIfNull(pattern);
        ArgumentNullException.ThrowIfNull(value);
        var comparison = ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var parts = pattern.Split('*');
        if (parts.Length == 1)
        {
            return string.Equals(pattern, value, comparison);
        }

        if (!value.StartsWith(parts[0], comparison) || !value.EndsWith(parts[^1], comparison))
        {
            return false;
        }

        var position = parts[0].Length;
        for (var i = 1; i < parts.Length - 1; i++)
        {
            var found = value.IndexOf(parts[i], position, comparison);
            if (found < 0)
            {
                return false;
            }

            position = found + parts[i].Length;
        }

        return position <= value.Length - parts[^1].Length;
    }
}

/// <summary>Textos das regras para a interface e para as explicações.</summary>
