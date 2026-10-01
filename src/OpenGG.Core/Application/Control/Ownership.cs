namespace OneRGB.Application.Control;

/// <summary>Como um app de fabricante convive com uma função (A21).</summary>
public enum Coexistence
{
    SafeCoexistence,
    ReadOnlyCoexistence,
    RequiresVendorApp,
    TakeoverRecommended,
    ControlBlocked,
}

/// <summary>
/// Regra de conflito: quando <see cref="VendorApp"/> está aberto, o que acontece com as funções cujo recurso casa
/// com <see cref="ResourcePattern"/> (<c>*</c> casa qualquer trecho: <c>*/rgb</c>, <c>cam-*</c>).
/// </summary>
public sealed record VendorConflictRule(string VendorApp, string ResourcePattern, Coexistence Mode, string Reason)
{
    public bool Matches(ResourceId resource)
    {
        var parts = ResourcePattern.Split('*');
        var value = resource.Value;
        if (parts.Length == 1)
        {
            return string.Equals(value, ResourcePattern, StringComparison.Ordinal);
        }

        if (!value.StartsWith(parts[0], StringComparison.Ordinal) || !value.EndsWith(parts[^1], StringComparison.Ordinal))
        {
            return false;
        }

        var position = parts[0].Length;
        for (var i = 1; i < parts.Length - 1; i++)
        {
            var found = value.IndexOf(parts[i], position, StringComparison.Ordinal);
            if (found < 0)
            {
                return false;
            }

            position = found + parts[i].Length;
        }

        return position <= value.Length - parts[^1].Length;
    }
}

/// <summary>Função de hardware do ponto de vista de posse (A22): "Tela", "Widgets", "RGB", "Firmware"…</summary>
public sealed record OwnedFunction(ResourceId Resource, string Device, string Function, bool CanRead, bool CanWrite);

/// <summary>Posse de um recurso (A15.5): dono, quem disputa, leitura/escrita, último escritor e explicação.</summary>
public sealed record ResourceOwnership(
    ResourceId Resource,
    string Device,
    string Function,
    string Owner,
    bool OwnedByOneRgb,
    IReadOnlyList<string> Contenders,
    bool CanRead,
    bool CanWrite,
    string? LastWriter,
    DateTimeOffset? LastWrite,
    Coexistence Mode,
    string Explanation);

/// <summary>
/// Ponte de compatibilidade (A23): quando a paridade nativa não é possível, o botão abre o app do fabricante e
/// diz por quê — nunca "não faz nada".
/// </summary>
public sealed record CompatibilityBridge(string FeatureId, string Feature, string Reason, string VendorApp, string? LaunchTarget, string ReturnHint)
{
    public string Explanation => $"{Feature}: {Reason} O OneRGB abre o {VendorApp}{(LaunchTarget is null ? string.Empty : " na tela certa")}; {ReturnHint}";
}

/// <summary>Monta a visão de posse por dispositivo e função (A15.5, A21, A22).</summary>
public static class OwnershipModel
{
    /// <summary>Regras de coexistência para os apps conhecidos (A21). Nada é encerrado automaticamente.</summary>
    public static IReadOnlyList<VendorConflictRule> DefaultRules { get; } =
    [
        new("HyperX NGENUITY", "mic-hyperx-quadcast-2s/rgb", Coexistence.ControlBlocked, "both send lighting through the same HID channel"),
        new("SteelSeries GG", "kbd-steelseries-apex-pro-tkl-gen3/rgb", Coexistence.SafeCoexistence, "OneRGB sends keyboard lighting through GG's GameSense"),
        new("SteelSeries GG", "kbd-steelseries-apex-pro-tkl-gen3/actuation", Coexistence.SafeCoexistence, "OneRGB writes keyboard actuation"),
        new("SteelSeries GG", "kbd-steelseries-apex-pro-tkl-gen3/oled", Coexistence.SafeCoexistence, "OneRGB uses GG's GameSense for the OLED"),
        new("Logitech G HUB", "mouse-logitech-pro-x2/settings", Coexistence.ControlBlocked, "G HUB writes DPI and HITS over HID++ when switching profiles"),
        new("Logitech G HUB", "headset-astro-a50x/settings", Coexistence.ControlBlocked, "G HUB controls the base's EQ and mixing"),
        new("Corsair iCUE", "monitor-corsair-xeneon-edge/settings", Coexistence.ControlBlocked, "iCUE controls display brightness and mode"),
        new("Corsair iCUE", "monitor-corsair-xeneon-edge/widgets", Coexistence.SafeCoexistence, "OneRGB widgets use their own screen windows"),
        new("Corsair iCUE", "monitor-corsair-xeneon-edge/firmware", Coexistence.RequiresVendorApp, "display firmware is updated only by iCUE"),
        new("Corsair iCUE", "*/sensors", Coexistence.ReadOnlyCoexistence, "iCUE also reads SMBus and EC through its driver; simultaneous reads can conflict"),
        new("GIGABYTE Control Center", "mobo-gigabyte-x870e/rgb", Coexistence.ControlBlocked, "RGB Fusion writes to the same controller"),
        new("GIGABYTE Control Center", "case-montech-king/rgb", Coexistence.ControlBlocked, "case fans use an ARGB header on the same board controller"),
        new("GIGABYTE Control Center", "cooler-corsair-nautilus-360/rgb", Coexistence.ControlBlocked, "the liquid cooler uses an ARGB header on the same board controller"),
        new("GIGABYTE Control Center", "acc-coolermaster-firepro-2450/rgb", Coexistence.ControlBlocked, "the GPU support uses an ARGB header on the same board controller"),
        new("GIGABYTE Control Center", "mobo-gigabyte-x870e/fans", Coexistence.TakeoverRecommended, "two fan curves would conflict on the same Super I/O"),
        new("MSI Center", "gpu-msi-rtx5070-gaming-trio/rgb", Coexistence.ControlBlocked, "Mystic Light writes to the same RGB controller"),
        new("MSI Afterburner", "gpu-msi-rtx5070-gaming-trio/tuning", Coexistence.TakeoverRecommended, "both would control GPU clocks, power and fans"),
        new("ASUS DisplayWidget Center", "monitor-asus*", Coexistence.TakeoverRecommended, "both write the same DDC/CI codes"),
        new("EMEET Studio", "cam-emeet*/image", Coexistence.TakeoverRecommended, "both control the camera's image parameters"),
        new("EMEET Studio", "cam-emeet*/ptz", Coexistence.TakeoverRecommended, "both move the gimbal and switch tracking modes"),
        new("EMEET Studio", "cam-emeet*/light", Coexistence.TakeoverRecommended, "both write the status light through the same HID"),
        new("EMEET Studio", "cam-emeet*/audio", Coexistence.TakeoverRecommended, "both switch the camera's audio mode"),
        new("NVIDIA Broadcast", "cam-*", Coexistence.SafeCoexistence, "Broadcast creates virtual devices; OneRGB only selects them"),
        new("SignalRGB", "*/rgb", Coexistence.ControlBlocked, "SignalRGB controls lighting on all devices"),
        new("HWiNFO", "*/sensors", Coexistence.ReadOnlyCoexistence, "simultaneous SMBus/EC reads can conflict; OneRGB prefers HWiNFO's shared-memory bridge"),
    ];

    /// <summary>
    /// As funções que a página de posse mostra: os recursos com controles registrados (lê/escreve pelo estado de cada
    /// controle) e os que algum app de fabricante disputa, mesmo sem controle no OneRGB ainda.
    /// </summary>
    public static IReadOnlyList<OwnedFunction> Functions(IEnumerable<ControlStatus> statuses, IEnumerable<ResourceId> vendorResources, Func<string, string?> deviceName)
    {
        ArgumentNullException.ThrowIfNull(statuses);
        ArgumentNullException.ThrowIfNull(vendorResources);
        ArgumentNullException.ThrowIfNull(deviceName);
        var functions = statuses
            .GroupBy(s => s.Key.Resource)
            .Select(g =>
            {
                var live = g.Where(s => s.State is not (FeatureState.Unavailable or FeatureState.Disconnected)).ToList();
                return Function(g.Key, live.Any(s => s.Spec.Readable), live.Any(s => s.Spec.Writable && s.ReadOnlyReason is null));
            })
            .ToList();
        var known = functions.Select(f => f.Resource).ToHashSet();
        functions.AddRange(vendorResources.Where(known.Add).Select(r => Function(r, false, false)));
        return [.. functions.OrderBy(f => f.Device, StringComparer.CurrentCulture).ThenBy(f => f.Function, StringComparer.CurrentCulture)];

        OwnedFunction Function(ResourceId resource, bool read, bool write)
        {
            var (device, part) = Split(resource);
            return new OwnedFunction(resource, deviceName(device) ?? device, FunctionName(part), read, write);
        }
    }

    /// <summary>
    /// Recursos que o OneRGB deixa de escrever enquanto o app está aberto: os que o app declara e os das regras em que
    /// ele bloqueia, só permite leitura ou exige o app. "Recomendado assumir" não bloqueia: quem decide é o usuário.
    /// </summary>
    public static IReadOnlyList<ResourceId> HeldBy(string vendorApp, IEnumerable<ResourceId> declared, IEnumerable<ResourceId> candidates, IEnumerable<VendorConflictRule>? rules = null)
    {
        ArgumentNullException.ThrowIfNull(declared);
        ArgumentNullException.ThrowIfNull(candidates);
        var holding = (rules ?? DefaultRules)
            .Where(r => string.Equals(r.VendorApp, vendorApp, StringComparison.OrdinalIgnoreCase)
                && r.Mode is Coexistence.ControlBlocked or Coexistence.ReadOnlyCoexistence or Coexistence.RequiresVendorApp)
            .ToList();
        return [.. declared.Concat(candidates.Where(c => holding.Any(r => r.Matches(c)))).Distinct()];
    }

    /// <summary>Nome da função pela parte final do recurso ("…/rgb" → "Lighting").</summary>
    public static string FunctionName(string part) => part switch
    {
        "rgb" or "lighting" => "Lighting",
        "actuation" => "Actuation",
        "settings" => "Settings",
        "oled" => "OLED display",
        "widgets" => "Widgets",
        "firmware" => "Firmware",
        "sensors" => "Sensors",
        "tuning" => "Clocks, power and fans",
        "fans" => "Fans",
        "image" or "picture" or "uvc" => "Image",
        "ptz" => "Movement (PTZ)",
        "light" => "Light",
        "audio" => "Audio",
        "volume" => "Volume",
        "display" or "windows" => "Windows display",
        _ => part.Length == 0 ? "Device" : char.ToUpperInvariant(part[0]) + part[1..],
    };

    private static (string Device, string Part) Split(ResourceId resource)
    {
        var value = resource.Value;
        var slash = value.IndexOf('/', StringComparison.Ordinal);
        return slash < 0 ? (value, string.Empty) : (value[..slash], value[(slash + 1)..]);
    }

    public static IReadOnlyList<ResourceOwnership> Describe(
        IEnumerable<OwnedFunction> functions,
        ControlArbiter arbiter,
        OperationJournal journal,
        IReadOnlyCollection<string> runningVendorApps,
        IEnumerable<VendorConflictRule>? rules = null)
    {
        ArgumentNullException.ThrowIfNull(functions);
        ArgumentNullException.ThrowIfNull(arbiter);
        ArgumentNullException.ThrowIfNull(journal);
        ArgumentNullException.ThrowIfNull(runningVendorApps);
        var ruleList = (rules ?? DefaultRules).ToList();
        var running = new HashSet<string>(runningVendorApps, StringComparer.OrdinalIgnoreCase);
        var result = new List<ResourceOwnership>();
        foreach (var function in functions)
        {
            var matches = ruleList
                .Where(r => running.Contains(r.VendorApp) && r.Matches(function.Resource))
                .OrderByDescending(r => r.Mode)
                .ToList();
            var contenders = matches.Select(r => r.VendorApp).Distinct(StringComparer.Ordinal).ToList();
            var owner = arbiter.OwnerOf(function.Resource);
            var ownedByUs = owner is not null && !running.Contains(owner);
            var mode = matches.Count == 0 ? Coexistence.SafeCoexistence : matches[0].Mode;
            var last = journal.LastWrite(function.Resource);
            var canWrite = function.CanWrite && mode is not (Coexistence.ControlBlocked or Coexistence.RequiresVendorApp or Coexistence.ReadOnlyCoexistence);
            result.Add(new ResourceOwnership(
                function.Resource,
                function.Device,
                function.Function,
                owner is null ? (contenders.Count > 0 && mode == Coexistence.ControlBlocked ? contenders[0] : "OneRGB (livre)") : ownedByUs ? $"OneRGB · {owner}" : owner,
                owner is null ? mode != Coexistence.ControlBlocked : ownedByUs,
                contenders,
                function.CanRead,
                canWrite,
                last?.Writer,
                last?.Time,
                mode,
                Explain(function, mode, matches.FirstOrDefault())));
        }

        return result;
    }

    public static string Explain(OwnedFunction function, Coexistence mode, VendorConflictRule? rule)
    {
        ArgumentNullException.ThrowIfNull(function);
        if (rule is null)
        {
            return function.CanWrite
                ? $"{function.Function}: controlado pelo OneRGB."
                : $"{function.Function}: OneRGB only reads this feature on this device.";
        }

        return mode switch
        {
            Coexistence.SafeCoexistence => $"{rule.VendorApp} pode ficar aberto: {rule.Reason}.",
            Coexistence.ReadOnlyCoexistence => $"While {rule.VendorApp} is open, OneRGB only reads {function.Function.ToLowerInvariantSafe()}: {rule.Reason}.",
            Coexistence.RequiresVendorApp => $"{function.Function} works through {rule.VendorApp} as a compatibility bridge: {rule.Reason}.",
            Coexistence.TakeoverRecommended => $"{rule.VendorApp} and OneRGB both write here ({rule.Reason}). Disable that control in {rule.VendorApp} or close it first.",
            _ => $"{rule.VendorApp} controls this feature ({rule.Reason}). OneRGB does not write while it is open; no app is closed automatically.",
        };
    }

    public static string Text(Coexistence mode) => mode switch
    {
        Coexistence.SafeCoexistence => "Safe coexistence",
        Coexistence.ReadOnlyCoexistence => "Read only while open",
        Coexistence.RequiresVendorApp => "Requires vendor software",
        Coexistence.TakeoverRecommended => "Recomendado o OneRGB assumir",
        _ => "Control blocked",
    };

    // Minúsculas só na primeira letra para encaixar no meio da frase, sem mexer em siglas (RGB, OLED).
    private static string ToLowerInvariantSafe(this string text) =>
        text.Length > 1 && char.IsUpper(text[0]) && !char.IsUpper(text[1]) ? char.ToLower(text[0], System.Globalization.CultureInfo.InvariantCulture) + text[1..] : text;
}
