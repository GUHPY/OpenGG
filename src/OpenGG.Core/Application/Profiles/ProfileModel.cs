using OneRGB.Application.Automation;
using OneRGB.Application.Control;

namespace OneRGB.Application.Profiles;

/// <summary>Quando um app ligado ao perfil o ativa.</summary>
public enum AppActivation
{
    /// <summary>Com o app em primeiro plano (troca ao alternar janelas, como os apps de fabricante).</summary>
    Foreground,

    /// <summary>Enquanto o app estiver aberto, mesmo em segundo plano.</summary>
    Running,
}

/// <summary>
/// Camada de um perfil (spec §23): aplicativo/jogo vence dispositivo, que vence o global. Ajuste manual temporário
/// e segurança ficam acima de qualquer perfil e são impostos pelo árbitro, não aqui.
/// </summary>
public enum ProfileLayer
{
    Global,
    Device,
    Application,
}

/// <summary>Um ajuste salvo no perfil. <see cref="Value"/> nulo anula o herdado do perfil pai ("não mexer").</summary>
public sealed record ProfileSetting
{
    public required ControlKey Key { get; init; }

    public ControlValue? Value { get; init; }
}

/// <summary>
/// Perfil unificado (A31): um perfil junta o que cada app de fabricante chamava de perfil — teclado, mouse, GPU,
/// áudio, RGB, tela, câmera. É só dado (spec §538): nada executável, validado antes de ativar.
/// </summary>
public sealed record Profile
{
    public const int CurrentSchema = 1;

    public int SchemaVersion { get; init; } = CurrentSchema;

    public required string Id { get; init; }

    public required string Name { get; init; }

    public ProfileLayer Layer { get; init; }

    public string? Description { get; init; }

    /// <summary>Perfil pai (spec §252): herda os ajustes e sobrescreve só o que muda.</summary>
    public string? Parent { get; init; }

    /// <summary>Perfis de dispositivo: o dispositivo do catálogo a que se aplicam.</summary>
    public string? DeviceId { get; init; }

    public string? AccentHex { get; init; }

    public IReadOnlyList<ProfileSetting> Settings { get; init; } = [];

    /// <summary>
    /// Ajustes de alto risco em ativação automática exigem esta permissão explícita (spec §382); sem ela, só
    /// entram quando o usuário ativa o perfil à mão (e passam pela confirmação).
    /// </summary>
    public bool AllowHighRiskAutomatic { get; init; }

    /// <summary>
    /// Perfis de aplicativo: os apps que ativam o perfil sozinhos (spec §22, §383). Pasta, editor e hash deixam a
    /// identificação mais forte; a mais forte vence quando dois apps disputam.
    /// </summary>
    public IReadOnlyList<AppMatcher> Apps { get; init; } = [];

    public AppActivation ActivateWhen { get; init; } = AppActivation.Foreground;

    /// <summary>
    /// Travado (MSI-AB-PROF-001): continua ativando, mas não muda nem é apagado até destravar. É a trava dos perfis
    /// do Afterburner: evita gravar por cima sem querer.
    /// </summary>
    public bool Locked { get; init; }

    public DateTimeOffset Modified { get; init; }
}

/// <summary>Valor efetivo de um perfil depois da herança, com o perfil da cadeia que o forneceu.</summary>
public sealed record EffectiveSetting(ControlKey Key, ControlValue Value, string SourceProfileId, string SourceProfileName, bool Inherited);

/// <summary>Problema encontrado na validação da biblioteca de perfis.</summary>
public sealed record ProfileIssue(string ProfileId, string Message, bool Blocking);

/// <summary>
/// Biblioteca de perfis com validação (ids únicos, pai existente, herança sem ciclo, perfil de dispositivo só
/// com ajustes do próprio dispositivo) e cálculo dos ajustes efetivos com herança.
/// </summary>
public sealed class ProfileLibrary
{
    public const int MaximumSettings = 5000;
    public const int MaximumDepth = 8;
    private readonly Dictionary<string, Profile> _profiles = new(StringComparer.Ordinal);
    private readonly Lock _gate = new();

    public ProfileLibrary(IEnumerable<Profile>? profiles = null)
    {
        foreach (var profile in profiles ?? [])
        {
            _profiles[profile.Id] = profile;
        }
    }

    public event EventHandler? Changed;

    public IReadOnlyList<Profile> All
    {
        get
        {
            lock (_gate)
            {
                return [.. _profiles.Values.OrderBy(p => p.Layer).ThenBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase)];
            }
        }
    }

    public Profile? Find(string id)
    {
        lock (_gate)
        {
            return _profiles.GetValueOrDefault(id);
        }
    }

    /// <summary>Inclui ou troca um perfil. Recusa se a troca deixaria a biblioteca inválida.</summary>
    public IReadOnlyList<ProfileIssue> Save(Profile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        List<ProfileIssue> issues;
        lock (_gate)
        {
            // Travado só aceita a troca que destrava.
            if (_profiles.TryGetValue(profile.Id, out var current) && current.Locked && profile.Locked)
            {
                return [new ProfileIssue(profile.Id, $"Profile \"{current.Name}\" is locked; unlock it to edit.", Blocking: true)];
            }

            // Só importa o que a troca afeta: o próprio perfil e quem herda dele (problemas antigos de outros
            // perfis não impedem salvar este).
            var candidate = new Dictionary<string, Profile>(_profiles, StringComparer.Ordinal) { [profile.Id] = profile };
            issues =
            [
                .. Validate(candidate).Where(i => string.Equals(i.ProfileId, profile.Id, StringComparison.Ordinal)
                    || (candidate.TryGetValue(i.ProfileId, out var other) && string.Equals(other.Parent, profile.Id, StringComparison.Ordinal))),
            ];
            if (issues.Any(i => i.Blocking))
            {
                return issues;
            }

            _profiles[profile.Id] = profile;
        }

        Changed?.Invoke(this, EventArgs.Empty);
        return issues;
    }

    /// <summary>Remove um perfil destravado que nenhum outro herda.</summary>
    public string? Delete(string id)
    {
        lock (_gate)
        {
            if (!_profiles.TryGetValue(id, out var current))
            {
                return "Profile not found.";
            }

            if (current.Locked)
            {
                return $"Profile \"{current.Name}\" is locked; unlock it to delete.";
            }

            if (_profiles.Values.FirstOrDefault(p => string.Equals(p.Parent, id, StringComparison.Ordinal)) is { } child)
            {
                return $"Profile \"{child.Name}\" inherits from this profile; change its parent first.";
            }

            _profiles.Remove(id);
        }

        Changed?.Invoke(this, EventArgs.Empty);
        return null;
    }

    /// <summary>
    /// Tira os ajustes de um dispositivo de todos os perfis destravados ("esquecer o dispositivo", EMEET-REMOVE-001).
    /// Os perfis ficam, mesmo vazios: apagar perfil é outra decisão. Devolve quantos mudaram e os travados que ficaram.
    /// </summary>
    public (int Changed, IReadOnlyList<string> Locked) Forget(string deviceId, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(deviceId);
        var changed = 0;
        List<string> locked = [];
        foreach (var profile in All.Where(p => p.Settings.Any(s => BelongsTo(s.Key.Resource, deviceId))))
        {
            if (profile.Locked)
            {
                locked.Add(profile.Name);
            }
            else if (!Save(profile with { Settings = [.. profile.Settings.Where(s => !BelongsTo(s.Key.Resource, deviceId))], Modified = now }).Any(i => i.Blocking))
            {
                changed++;
            }
        }

        return (changed, locked);
    }

    public IReadOnlyList<ProfileIssue> Validate()
    {
        lock (_gate)
        {
            return Validate(_profiles);
        }
    }

    /// <summary>Ajustes efetivos de um perfil: do avô para o filho, o mais específico vence; nulo apaga o herdado.</summary>
    public IReadOnlyList<EffectiveSetting> Effective(string id)
    {
        lock (_gate)
        {
            var chain = Chain(_profiles, id);
            var result = new Dictionary<ControlKey, EffectiveSetting>();
            var order = new List<ControlKey>();
            for (var i = chain.Count - 1; i >= 0; i--)
            {
                var profile = chain[i];
                foreach (var setting in profile.Settings)
                {
                    if (!result.ContainsKey(setting.Key) && setting.Value is not null)
                    {
                        order.Add(setting.Key);
                    }

                    if (setting.Value is null)
                    {
                        result.Remove(setting.Key);
                        order.Remove(setting.Key);
                    }
                    else
                    {
                        result[setting.Key] = new EffectiveSetting(setting.Key, setting.Value, profile.Id, profile.Name, i > 0);
                    }
                }
            }

            return [.. order.Select(k => result[k])];
        }
    }

    /// <summary>Cadeia de herança do perfil até a raiz (o próprio perfil primeiro). Vazia se não existe.</summary>
    public IReadOnlyList<Profile> Ancestry(string id)
    {
        lock (_gate)
        {
            return Chain(_profiles, id);
        }
    }

    private static List<Profile> Chain(IReadOnlyDictionary<string, Profile> profiles, string id)
    {
        var chain = new List<Profile>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var current = profiles.GetValueOrDefault(id); current is not null && chain.Count < MaximumDepth; current = current.Parent is { } parent ? profiles.GetValueOrDefault(parent) : null)
        {
            if (!seen.Add(current.Id))
            {
                break;
            }

            chain.Add(current);
        }

        return chain;
    }

    private static List<ProfileIssue> Validate(IReadOnlyDictionary<string, Profile> profiles)
    {
        var issues = new List<ProfileIssue>();
        foreach (var profile in profiles.Values)
        {
            if (string.IsNullOrWhiteSpace(profile.Id) || profile.Id.Length > 64 || profile.Id.Any(c => !(char.IsAsciiLetterOrDigit(c) || c is '-' or '_')))
            {
                issues.Add(new ProfileIssue(profile.Id, "Invalid ID (use letters, numbers, '-' or '_', up to 64 characters).", true));
            }

            if (string.IsNullOrWhiteSpace(profile.Name) || profile.Name.Length > 80)
            {
                issues.Add(new ProfileIssue(profile.Id, "The name must contain 1–80 characters.", true));
            }

            if (profile.SchemaVersion > Profile.CurrentSchema)
            {
                issues.Add(new ProfileIssue(profile.Id, $"Profile from a newer OpenGG version (format {profile.SchemaVersion}).", true));
            }

            if (profile.Settings.Count > MaximumSettings)
            {
                issues.Add(new ProfileIssue(profile.Id, $"Profile contains too many settings ({profile.Settings.Count}).", true));
            }

            var duplicate = profile.Settings.GroupBy(s => s.Key).FirstOrDefault(g => g.Count() > 1);
            if (duplicate is not null)
            {
                issues.Add(new ProfileIssue(profile.Id, $"Ajuste repetido: {duplicate.Key}.", true));
            }

            if (profile.Apps.Count > 0 && profile.Layer != ProfileLayer.Application)
            {
                issues.Add(new ProfileIssue(profile.Id, "Only application profiles can be linked to apps.", true));
            }

            if (profile.Apps.Any(a => a is null || string.IsNullOrWhiteSpace(a.Executable)))
            {
                issues.Add(new ProfileIssue(profile.Id, "A linked app has no executable.", true));
            }

            if (profile.Layer == ProfileLayer.Device)
            {
                if (string.IsNullOrWhiteSpace(profile.DeviceId))
                {
                    issues.Add(new ProfileIssue(profile.Id, "A device profile must specify its device.", true));
                }
                else if (profile.Settings.FirstOrDefault(s => !BelongsTo(s.Key.Resource, profile.DeviceId)) is { } foreign)
                {
                    issues.Add(new ProfileIssue(profile.Id, $"Device profile contains a setting for another device: {foreign.Key}.", true));
                }
            }

            if (profile.Parent is { } parentId)
            {
                if (!profiles.TryGetValue(parentId, out var parent))
                {
                    issues.Add(new ProfileIssue(profile.Id, $"Parent profile \"{parentId}\" does not exist.", true));
                }
                else if (parent.Layer > profile.Layer)
                {
                    issues.Add(new ProfileIssue(profile.Id, "A profile cannot inherit from a more specific layer.", true));
                }
                else if (HasCycle(profiles, profile))
                {
                    issues.Add(new ProfileIssue(profile.Id, "Circular inheritance.", true));
                }
                else if (Chain(profiles, profile.Id).Count >= MaximumDepth)
                {
                    issues.Add(new ProfileIssue(profile.Id, $"Inheritance is too deep (maximum {MaximumDepth} levels).", true));
                }
            }
        }

        return issues;
    }

    private static bool HasCycle(IReadOnlyDictionary<string, Profile> profiles, Profile start)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal) { start.Id };
        for (var parent = start.Parent; parent is not null; parent = profiles.GetValueOrDefault(parent)?.Parent)
        {
            if (!seen.Add(parent))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>O recurso pertence ao dispositivo? Recursos são "&lt;dispositivo&gt;/&lt;função&gt;".</summary>
    public static bool BelongsTo(ResourceId resource, string deviceId)
    {
        ArgumentNullException.ThrowIfNull(deviceId);
        var value = resource.Value;
        return value.Length > deviceId.Length && value.StartsWith(deviceId, StringComparison.Ordinal) && value[deviceId.Length] == '/';
    }
}
