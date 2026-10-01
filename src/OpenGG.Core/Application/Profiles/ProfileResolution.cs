using System.Globalization;
using OneRGB.Application.Control;

namespace OneRGB.Application.Profiles;

public enum ActivationKind
{
    Manual,
    Automatic,
    Startup,
    Restore,
}

/// <summary>
/// Por que um perfil está ativo (spec §381): o diagnóstico mostra isto. <see cref="RunId"/> liga a ativação à
/// execução de automação que a pediu (cadeia de causas, spec §386).
/// </summary>
public sealed record ActivationCause(ActivationKind Kind, string Reason, string? RuleId = null, string? RuleName = null, string? MatchedExecutable = null, string? RunId = null)
{
    public static ActivationCause ByUser(string? detail = null) => new(ActivationKind.Manual, detail ?? "Selected by the user");

    public static ActivationCause AtStartup() => new(ActivationKind.Startup, "Restaurado ao iniciar o OneRGB");
}

public sealed record ProfileActivation(Profile Profile, ActivationCause Cause, long Order, DateTimeOffset Since);

/// <summary>Valor que perdeu para um perfil de camada mais alta (ou ativado depois).</summary>
public sealed record ShadowedValue(string ProfileId, string ProfileName, ProfileLayer Layer, ControlValue Value);

/// <summary>
/// Valor final de um controle com todos os perfis ativos (spec §380: determinístico e explicável): quem venceu,
/// de qual perfil da herança veio e o que ficou por baixo.
/// </summary>
public sealed record ResolvedSetting(
    ControlKey Key,
    ControlValue Value,
    string ProfileId,
    string ProfileName,
    ProfileLayer Layer,
    string SourceProfileName,
    IReadOnlyList<ShadowedValue> Shadowed)
{
    public WriterPriority Priority => ProfileResolver.PriorityOf(Layer);

    public string Explain(ControlSpec? spec, IFormatProvider? provider = null)
    {
        provider ??= CultureInfo.CurrentCulture;
        var label = spec?.Label ?? Key.Setting;
        string Show(ControlValue value) => spec is null ? value.ToString() : value.Display(spec, provider);
        var source = string.Equals(SourceProfileName, ProfileName, StringComparison.Ordinal) ? string.Empty : $", inherited from \"{SourceProfileName}\"";
        var text = $"{label} = {Show(Value)}: {ProfileResolver.LayerText(Layer)} profile \"{ProfileName}\"{source}";
        return Shadowed.Count == 0
            ? text + "."
            : text + "; overrides " + string.Join(", ", Shadowed.Select(s => $"\"{s.ProfileName}\" ({Show(s.Value)})")) + ".";
    }
}

/// <summary>Combina os perfis ativos (spec §23): aplicativo &gt; dispositivo &gt; global; na mesma camada, o mais recente.</summary>
public static class ProfileResolver
{
    public static WriterPriority PriorityOf(ProfileLayer layer) => layer switch
    {
        ProfileLayer.Application => WriterPriority.ApplicationProfile,
        ProfileLayer.Device => WriterPriority.DeviceProfile,
        _ => WriterPriority.GlobalProfile,
    };

    public static string LayerText(ProfileLayer layer) => layer switch
    {
        ProfileLayer.Application => "application",
        ProfileLayer.Device => "device",
        _ => "global",
    };

    public static IReadOnlyList<ResolvedSetting> Resolve(ProfileLibrary library, IEnumerable<ProfileActivation> active)
    {
        ArgumentNullException.ThrowIfNull(library);
        ArgumentNullException.ThrowIfNull(active);
        var candidates = new Dictionary<ControlKey, List<(ProfileActivation Activation, EffectiveSetting Setting)>>();
        var order = new List<ControlKey>();
        foreach (var activation in active.OrderByDescending(a => a.Profile.Layer).ThenByDescending(a => a.Order))
        {
            foreach (var setting in library.Effective(activation.Profile.Id))
            {
                // Perfil de dispositivo só vale para o próprio dispositivo, mesmo que herde de um perfil amplo.
                if (activation.Profile.Layer == ProfileLayer.Device
                    && (activation.Profile.DeviceId is not { } device || !ProfileLibrary.BelongsTo(setting.Key.Resource, device)))
                {
                    continue;
                }

                if (!candidates.TryGetValue(setting.Key, out var list))
                {
                    candidates[setting.Key] = list = [];
                    order.Add(setting.Key);
                }

                list.Add((activation, setting));
            }
        }

        return
        [
            .. order.Select(key =>
            {
                var list = candidates[key];
                var (winner, value) = list[0];
                return new ResolvedSetting(
                    key,
                    value.Value,
                    winner.Profile.Id,
                    winner.Profile.Name,
                    winner.Profile.Layer,
                    value.SourceProfileName,
                    [.. list.Skip(1).Select(c => new ShadowedValue(c.Activation.Profile.Id, c.Activation.Profile.Name, c.Activation.Profile.Layer, c.Setting.Value))]);
            }),
        ];
    }
}

/// <summary>
/// Ordem de aplicação (A31): por fase (proteção → estrutura → normal → desempenho) e, dentro do recurso, pelas
/// dependências declaradas. Quem depende de algo nunca vem antes dele, mesmo que a fase dele seja mais cedo.
/// </summary>
public static class ApplyOrder
{
    public static IReadOnlyList<T> Sort<T>(IEnumerable<T> items, Func<T, ControlKey> keyOf, Func<ControlKey, ControlSpec?> specOf)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(keyOf);
        ArgumentNullException.ThrowIfNull(specOf);
        var list = items.ToList();
        var keys = list.Select(keyOf).ToList();
        var index = new Dictionary<ControlKey, int>();
        for (var i = 0; i < keys.Count; i++)
        {
            index.TryAdd(keys[i], i);
        }

        // Arestas dependência → dependente, só entre itens presentes no plano.
        var dependents = keys.Select(_ => new List<int>()).ToList();
        var pending = new int[keys.Count];
        for (var i = 0; i < keys.Count; i++)
        {
            foreach (var setting in specOf(keys[i])?.DependsOn ?? [])
            {
                if (index.TryGetValue(keys[i] with { Setting = setting }, out var dependency) && dependency != i)
                {
                    dependents[dependency].Add(i);
                    pending[i]++;
                }
            }
        }

        // Fase efetiva: nunca antes da fase das próprias dependências.
        var phase = keys.Select(k => specOf(k)?.Phase ?? ApplyPhase.Normal).ToArray();
        for (var changed = true; changed;)
        {
            changed = false;
            for (var i = 0; i < keys.Count; i++)
            {
                foreach (var next in dependents[i].Where(next => phase[next] < phase[i]))
                {
                    phase[next] = phase[i];
                    changed = true;
                }
            }
        }

        var ready = new SortedSet<int>(Comparer<int>.Create((a, b) =>
        {
            var byPhase = phase[a].CompareTo(phase[b]);
            if (byPhase != 0)
            {
                return byPhase;
            }

            var byResource = string.CompareOrdinal(keys[a].Resource.Value, keys[b].Resource.Value);
            if (byResource != 0)
            {
                return byResource;
            }

            var bySetting = string.CompareOrdinal(keys[a].Setting, keys[b].Setting);
            return bySetting != 0 ? bySetting : a.CompareTo(b);
        }));
        for (var i = 0; i < keys.Count; i++)
        {
            if (pending[i] == 0)
            {
                ready.Add(i);
            }
        }

        var result = new List<T>(list.Count);
        var done = new bool[keys.Count];
        while (result.Count < list.Count)
        {
            if (ready.Count == 0)
            {
                // Dependência circular: segue pelo primeiro pendente na ordem determinística.
                var stuck = Enumerable.Range(0, keys.Count).Where(i => !done[i]).OrderBy(i => phase[i]).ThenBy(i => keys[i].Resource.Value, StringComparer.Ordinal).ThenBy(i => keys[i].Setting, StringComparer.Ordinal).First();
                pending[stuck] = 0;
                ready.Add(stuck);
            }

            var current = ready.Min;
            ready.Remove(current);
            done[current] = true;
            result.Add(list[current]);
            foreach (var next in dependents[current])
            {
                if (!done[next] && --pending[next] == 0)
                {
                    ready.Add(next);
                }
            }
        }

        return result;
    }
}
