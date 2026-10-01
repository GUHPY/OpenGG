using OneRGB.Domain;

namespace OneRGB.Application;

/// <summary>Um dispositivo observado na enumeração (HID/USB), independente de como foi descoberto.</summary>
public sealed record ObservedDevice(UsbIdentity Identity, string? Product, string? Manufacturer, string InstancePath);

/// <summary>Resultado de casar um dispositivo observado com o catálogo conhecido.</summary>
public sealed record DeviceMatch(ObservedDevice Observed, KnownDevice Known, ExpectedHardwareId ExpectedId);

/// <summary>
/// Casa a enumeração real com phase0/data/devices.json. É assim que afirmações "unverified" viram
/// evidência lida no hardware (phase0/02_PROTOCOL_VERIFICATION.md, "Como cada afirmação vira verified_hw").
/// </summary>
public static class DeviceMatcher
{
    public static IReadOnlyList<DeviceMatch> Match(IEnumerable<ObservedDevice> observed, IEnumerable<KnownDevice> catalog)
    {
        ArgumentNullException.ThrowIfNull(observed);
        ArgumentNullException.ThrowIfNull(catalog);

        var expected = catalog.SelectMany(k => k.UsbIdentities().Select(u => (Known: k, u.Identity, u.Source))).ToList();
        var matches = new List<DeviceMatch>();
        foreach (var device in observed)
        {
            // O candidato mais específico (mais campos preenchidos) vence.
            var best = expected
                .Where(e => e.Identity.Matches(device.Identity))
                .OrderByDescending(e => Specificity(e.Identity))
                .FirstOrDefault();
            if (best.Known is not null)
            {
                matches.Add(new DeviceMatch(device, best.Known, best.Source));
            }
        }

        return matches;
    }

    /// <summary>IDs esperados no catálogo que não apareceram em nenhum dispositivo observado.</summary>
    public static IReadOnlyList<(KnownDevice Known, ExpectedHardwareId ExpectedId)> Missing(
        IEnumerable<ObservedDevice> observed, IEnumerable<KnownDevice> catalog)
    {
        ArgumentNullException.ThrowIfNull(observed);
        ArgumentNullException.ThrowIfNull(catalog);

        var seen = observed.Select(o => o.Identity).ToList();
        return catalog
            .SelectMany(k => k.UsbIdentities().Select(u => (k, u.Identity, u.Source)))
            .Where(e => !seen.Any(e.Identity.Matches))
            .Select(e => (e.k, e.Source))
            .ToList();
    }

    private static int Specificity(UsbIdentity id) =>
        (id.InterfaceNumber is null ? 0 : 1) + (id.UsagePage is null ? 0 : 1) + (id.Usage is null ? 0 : 1);
}
