using System.Globalization;

namespace OneRGB.Domain;

/// <summary>Um código VCP anunciado nas capacidades, com os valores permitidos (vazio = contínuo ou não listado).</summary>
public sealed record VcpCapability(byte Code, IReadOnlyList<ushort> Values);

/// <summary>
/// String de capacidades MCCS interpretada (CapabilitiesRequestAndCapabilitiesReply): protocolo, tipo, modelo,
/// comandos, códigos VCP com valores permitidos, nomes de VCP do fabricante e as demais seções cruas.
/// </summary>
public sealed record MccsCapabilities(
    string? Protocol,
    string? Type,
    string? Model,
    string? MccsVersion,
    IReadOnlyList<byte> Commands,
    IReadOnlyList<VcpCapability> Vcp,
    IReadOnlyDictionary<byte, string> VcpNames,
    IReadOnlyDictionary<string, string> Sections,
    IReadOnlyList<string> Warnings)
{
    public VcpCapability? Find(byte code) => Vcp.FirstOrDefault(v => v.Code == code);

    public bool Supports(byte code) => Vcp.Any(v => v.Code == code);
}

/// <summary>
/// Leitura tolerante da string de capacidades MCCS (ex.: "(prot(monitor)vcp(02 10 14(05 08) 60(0F 11)))"). Monitores
/// reais mandam parênteses sem fechar, códigos grudados ("0210") e valores de 16 bits; nada disso derruba a leitura —
/// vira aviso.
/// </summary>
public static class Mccs
{
    public static MccsCapabilities Parse(string capabilities)
    {
        ArgumentNullException.ThrowIfNull(capabilities);
        var warnings = new List<string>();
        var sections = new List<(string Name, string Content)>();
        ReadSections(capabilities.Replace('\0', ' '), sections, warnings);

        var raw = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, content) in sections)
        {
            if (!raw.TryAdd(name, content))
            {
                warnings.Add($"Duplicate section '{name}'; using the first.");
            }
        }

        var vcp = raw.TryGetValue("vcp", out var vcpText) ? ReadVcp(vcpText, warnings) : [];
        var names = raw.TryGetValue("vcpname", out var namesText) ? ReadVcpNames(namesText, warnings) : new Dictionary<byte, string>();
        var commands = raw.TryGetValue("cmds", out var commandsText)
            ? [.. HexTokens(commandsText).SelectMany(t => Bytes(t, "cmds", warnings))]
            : new List<byte>();

        return new MccsCapabilities(
            Text(raw, "prot"),
            Text(raw, "type"),
            Text(raw, "model"),
            Text(raw, "mccs_ver"),
            commands,
            vcp,
            names,
            raw,
            warnings);
    }

    /// <summary>Lista os códigos VCP de primeiro nível declarados na seção vcp(...).</summary>
    public static IReadOnlyList<byte> ListedVcpCodes(string capabilities) => [.. Parse(capabilities).Vcp.Select(v => v.Code)];

    /// <summary>Valor de model(...) na string de capacidades, quando o monitor informa.</summary>
    public static string? ModelName(string capabilities) => Parse(capabilities).Model;

    private static string? Text(Dictionary<string, string> raw, string name) =>
        raw.TryGetValue(name, out var value) && value.Trim() is { Length: > 0 } text ? text : null;

    // name(conteúdo) name(conteúdo) … ; um "(" sem nome é o invólucro externo e é aberto no lugar.
    private static void ReadSections(string text, List<(string, string)> sections, List<string> warnings)
    {
        var i = 0;
        while (i < text.Length)
        {
            if (char.IsWhiteSpace(text[i]) || text[i] == ')')
            {
                i++;
                continue;
            }

            var nameStart = i;
            while (i < text.Length && (char.IsAsciiLetterOrDigit(text[i]) || text[i] is '_' or '-'))
            {
                i++;
            }

            var name = text[nameStart..i];
            if (i >= text.Length || text[i] != '(')
            {
                if (name.Length == 0)
                {
                    i++;
                }

                continue;
            }

            var content = Group(text, ref i, out var closed);
            if (!closed)
            {
                warnings.Add(name.Length == 0 ? "Capabilities have no closing ')'." : $"Section '{name}' has no closing ')'.");
            }

            if (name.Length == 0)
            {
                ReadSections(content, sections, warnings);
            }
            else
            {
                sections.Add((name.ToLowerInvariant(), content));
            }
        }
    }

    // Lê "( … )" com aninhamento a partir de text[i] == '('; devolve o conteúdo sem os parênteses externos.
    private static string Group(string text, ref int i, out bool closed)
    {
        var start = ++i;
        var depth = 1;
        while (i < text.Length)
        {
            switch (text[i])
            {
                case '(':
                    depth++;
                    break;
                case ')':
                    if (--depth == 0)
                    {
                        closed = true;
                        return text[start..i++];
                    }

                    break;
            }

            i++;
        }

        closed = false;
        return text[start..];
    }

    private static List<VcpCapability> ReadVcp(string text, List<string> warnings)
    {
        var result = new List<VcpCapability>();
        var i = 0;
        var pending = new List<byte>();
        while (i < text.Length)
        {
            var c = text[i];
            if (char.IsWhiteSpace(c))
            {
                i++;
                continue;
            }

            if (c == '(')
            {
                var values = Group(text, ref i, out var closed);
                if (!closed)
                {
                    warnings.Add("Value list has no closing ')' in the vcp section.");
                }

                if (pending.Count == 0)
                {
                    warnings.Add("Value list has no preceding VCP code; ignored.");
                    continue;
                }

                // Os valores pertencem ao último código lido; os anteriores do mesmo token ficam sem lista.
                var code = pending[^1];
                pending.RemoveAt(pending.Count - 1);
                Add(pending, []);
                Add([code], [.. HexTokens(values).SelectMany(t => Values(t, code, warnings))]);
                pending.Clear();
                continue;
            }

            if (c == ')')
            {
                i++;
                continue;
            }

            var start = i;
            while (i < text.Length && !char.IsWhiteSpace(text[i]) && text[i] is not '(' and not ')')
            {
                i++;
            }

            Add(pending, []);
            pending.Clear();
            pending.AddRange(Bytes(text[start..i], "vcp", warnings));
        }

        Add(pending, []);
        return result;

        void Add(IEnumerable<byte> codes, IReadOnlyList<ushort> values)
        {
            foreach (var code in codes)
            {
                if (result.Any(v => v.Code == code))
                {
                    warnings.Add(string.Create(CultureInfo.InvariantCulture, $"VCP 0x{code:X2} anunciado duas vezes; valendo o primeiro."));
                    continue;
                }

                result.Add(new VcpCapability(code, values));
            }
        }
    }

    private static Dictionary<byte, string> ReadVcpNames(string text, List<string> warnings)
    {
        var names = new Dictionary<byte, string>();
        var i = 0;
        while (i < text.Length)
        {
            if (char.IsWhiteSpace(text[i]) || text[i] == ')')
            {
                i++;
                continue;
            }

            var start = i;
            while (i < text.Length && text[i] != '(' && !char.IsWhiteSpace(text[i]))
            {
                i++;
            }

            var token = text[start..i];
            if (i < text.Length && text[i] == '(')
            {
                var name = Group(text, ref i, out _).Trim();
                if (byte.TryParse(token, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var code) && name.Length > 0)
                {
                    names.TryAdd(code, name);
                }
                else
                {
                    warnings.Add($"Unreadable VCP name: '{token}'.");
                }
            }
        }

        return names;
    }

    private static string[] HexTokens(string text) =>
        text.Split([' ', '\t', '\r', '\n', '(', ')'], StringSplitOptions.RemoveEmptyEntries);

    // Códigos são sempre 1 byte: "0210" grudado vira 02 e 10.
    private static IEnumerable<byte> Bytes(string token, string section, List<string> warnings)
    {
        if (token.Length % 2 != 0 || !token.All(char.IsAsciiHexDigit))
        {
            warnings.Add($"Unreadable token '{token}' in section {section}.");
            yield break;
        }

        for (var p = 0; p < token.Length; p += 2)
        {
            yield return byte.Parse(token.AsSpan(p, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        }
    }

    // Valores têm 1 byte ou 2 (alguns fabricantes listam valores de 16 bits, "0100"); mais que isso é dividido em bytes.
    private static IEnumerable<ushort> Values(string token, byte code, List<string> warnings)
    {
        if (token.Length is 2 or 4 && token.All(char.IsAsciiHexDigit))
        {
            yield return ushort.Parse(token, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            yield break;
        }

        if (token.Length % 2 == 0 && token.All(char.IsAsciiHexDigit))
        {
            warnings.Add(string.Create(CultureInfo.InvariantCulture, $"Concatenated values '{token}' in VCP 0x{code:X2}; read byte by byte."));
            for (var p = 0; p < token.Length; p += 2)
            {
                yield return byte.Parse(token.AsSpan(p, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            }

            yield break;
        }

        warnings.Add(string.Create(CultureInfo.InvariantCulture, $"Unreadable value '{token}' in VCP 0x{code:X2}."));
    }
}
