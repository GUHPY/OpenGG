using OneRGB.Windows;

namespace OpenGG;

public sealed record KeyboardDevice(string Id, string Name, ushort ProductId, IReadOnlyList<HidInterfaceInfo> Interfaces)
{
    public bool HasVerifiedReceiver => ProductId == 0x1644 && Interfaces.Any(i => i.Error is null
        && i.Identity.VendorId == 0x1038 && i.Identity.ProductId == 0x1644
        && i.Identity.InterfaceNumber == 3 && i.Identity.UsagePage == 0xFFC0 && i.Identity.Usage == 1
        && i.FeatureReportLength == 642 && i.OutputReportLength == 65 && i.InputReportLength == 65);
}

public static class SteelSeriesKeyboards
{
    public static IReadOnlyList<KeyboardDevice> Discover(IEnumerable<HidInterfaceInfo> interfaces)
    {
        ArgumentNullException.ThrowIfNull(interfaces);
        return [.. interfaces.Where(i => i.Identity.VendorId == 0x1038)
            // ponytail: PID fallback can merge identical units if Windows supplies no Container ID.
            .GroupBy(i => i.ContainerId?.ToString("N") ?? $"pid-{i.Identity.ProductId:X4}")
            .Where(group => group.Any(i => i.Identity.UsagePage == 1 && i.Identity.Usage == 6))
            .Where(group => !group.Any(i => IsMouseName(i.Product)))
            .Select(group =>
            {
                var keyboard = group.First(i => i.Identity.UsagePage == 1 && i.Identity.Usage == 6);
                var product = group.Select(i => i.Product).FirstOrDefault(p => !string.IsNullOrWhiteSpace(p));
                return new KeyboardDevice($"steelseries-{group.Key}", product ?? $"SteelSeries keyboard ({keyboard.Identity.ProductId:X4})",
                    keyboard.Identity.ProductId, [.. group]);
            }).OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase)];
    }

    private static bool IsMouseName(string? name) => name is not null && new[] { "mouse","Aerox","Rival","Sensei" }
        .Any(word => name.Contains(word,StringComparison.OrdinalIgnoreCase));
}
