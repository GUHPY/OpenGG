using OneRGB.Application.Control;

namespace OneRGB.Application.Profiles;

/// <summary>
/// Onde as páginas de dispositivo guardam a configuração (A31): no perfil do dispositivo que está ativo ou, sem nenhum
/// ativo, no perfil do dispositivo criado pela página no primeiro salvamento. Com a escrita bloqueada (ADR-0005), a
/// configuração espera no perfil; quando o protocolo for conferido, ativar o perfil a leva ao dispositivo.
/// </summary>
public static class DeviceProfiles
{
    /// <summary>Id do perfil que a página cria para o dispositivo.</summary>
    public static string DefaultId(string deviceId) => $"device-{deviceId}";

    /// <summary>
    /// O perfil em edição: o do dispositivo que está ativo; senão o criado pela página; senão outro do dispositivo;
    /// senão um novo, ainda não salvo.
    /// </summary>
    public static Profile Editing(ProfileLibrary library, IEnumerable<string> activeIds, string deviceId, string deviceName)
    {
        ArgumentNullException.ThrowIfNull(library);
        ArgumentNullException.ThrowIfNull(activeIds);
        ArgumentException.ThrowIfNullOrWhiteSpace(deviceId);
        var active = activeIds.ToHashSet(StringComparer.Ordinal);
        var mine = library.All.Where(p => p.Layer == ProfileLayer.Device && string.Equals(p.DeviceId, deviceId, StringComparison.Ordinal)).ToList();
        return mine.FirstOrDefault(p => active.Contains(p.Id))
            ?? mine.FirstOrDefault(p => string.Equals(p.Id, DefaultId(deviceId), StringComparison.Ordinal))
            ?? mine.FirstOrDefault()
            ?? new Profile { Id = DefaultId(deviceId), Name = deviceName, Layer = ProfileLayer.Device, DeviceId = deviceId };
    }

    /// <summary>Valores guardados no perfil (com o que ele herda), só dos controles do dispositivo dele.</summary>
    public static IReadOnlyDictionary<ControlKey, ControlValue> Values(ProfileLibrary library, Profile profile)
    {
        ArgumentNullException.ThrowIfNull(library);
        ArgumentNullException.ThrowIfNull(profile);
        var settings = library.Find(profile.Id) is null
            ? profile.Settings.Where(s => s.Value is not null).Select(s => (s.Key, Value: s.Value!))
            : library.Effective(profile.Id).Select(s => (s.Key, s.Value));
        var values = new Dictionary<ControlKey, ControlValue>();
        foreach (var (key, value) in settings)
        {
            if (profile.DeviceId is null || ProfileLibrary.BelongsTo(key.Resource, profile.DeviceId))
            {
                values[key] = value;
            }
        }

        return values;
    }

    /// <summary>O perfil com estes valores no lugar dos anteriores, na mesma ordem; os outros ajustes ficam como estão.</summary>
    public static Profile With(Profile profile, IReadOnlyDictionary<ControlKey, ControlValue> values, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(values);
        var settings = profile.Settings.Select(s => values.TryGetValue(s.Key, out var value) ? s with { Value = value } : s).ToList();
        var present = profile.Settings.Select(s => s.Key).ToHashSet();
        settings.AddRange(values.Where(v => !present.Contains(v.Key)).Select(v => new ProfileSetting { Key = v.Key, Value = v.Value }));
        return profile with { Settings = settings, Modified = now };
    }
}
