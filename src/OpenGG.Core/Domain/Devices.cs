using System.Globalization;
using System.Text.RegularExpressions;

namespace OneRGB.Domain;

/// <summary>Ordem de preferência de acesso ao hardware (spec §10).</summary>
public enum AccessTier
{
    WindowsApi = 1,
    VendorSdk = 2,
    VendorProtocolDocs = 3,
    CompatibleOpenSource = 4,
    PublicProtocol = 5,
    ReverseEngineered = 6,
    VendorAppBridge = 7,
}

/// <summary>Quanto uma afirmação técnica foi conferida (phase0/02_PROTOCOL_VERIFICATION.md).</summary>
public enum VerificationStatus
{
    Unverified,
    Contradicted,
    Corroborated,
    VerifiedOnHardware,
}

/// <summary>Barramento/tipo de conexão normalizado (spec §8).</summary>
public enum BusType
{
    Unknown,
    Usb,
    Hid,
    Pci,
    I2cOverGpu,
    SmBus,
    DisplayDdcCi,
    Audio,
    Storage,
    Software,
}

/// <summary>
/// Identidade de um dispositivo USB/HID. Todos os campos opcionais porque a enumeração nem sempre
/// expõe interface ou usage page.
/// </summary>
public sealed partial record UsbIdentity(ushort VendorId, ushort ProductId, int? InterfaceNumber = null, ushort? UsagePage = null, ushort? Usage = null)
{
    [GeneratedRegex(@"VID_(?<vid>[0-9A-Fa-f]{4}).*?PID_(?<pid>[0-9A-Fa-f]{4})(?:.*?MI_(?<mi>[0-9A-Fa-f]{2}))?", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex HardwareIdPattern();

    /// <summary>
    /// Lê identificadores como <c>USB\VID_048D&amp;PID_5711</c>, <c>HID\VID_1038&amp;PID_1644&amp;MI_03</c>
    /// ou caminhos de interface <c>\\?\hid#vid_03f0&amp;pid_02b5&amp;mi_01#...</c>.
    /// </summary>
    public static bool TryParse(string? hardwareId, out UsbIdentity identity)
    {
        identity = new UsbIdentity(0, 0);
        if (string.IsNullOrWhiteSpace(hardwareId))
        {
            return false;
        }

        var match = HardwareIdPattern().Match(hardwareId);
        if (!match.Success)
        {
            return false;
        }

        var vid = ushort.Parse(match.Groups["vid"].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        var pid = ushort.Parse(match.Groups["pid"].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        int? mi = match.Groups["mi"].Success
            ? int.Parse(match.Groups["mi"].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture)
            : null;
        identity = new UsbIdentity(vid, pid, mi);
        return true;
    }

    /// <summary>
    /// Verdadeiro quando <paramref name="observed"/> (vindo da enumeração) satisfaz esta identidade esperada.
    /// Campos nulos na identidade esperada funcionam como curinga.
    /// </summary>
    public bool Matches(UsbIdentity observed)
    {
        ArgumentNullException.ThrowIfNull(observed);
        return VendorId == observed.VendorId
            && ProductId == observed.ProductId
            && (InterfaceNumber is null || InterfaceNumber == observed.InterfaceNumber)
            && (UsagePage is null || UsagePage == observed.UsagePage)
            && (Usage is null || Usage == observed.Usage);
    }

    public override string ToString()
    {
        var text = string.Create(CultureInfo.InvariantCulture, $"{VendorId:X4}:{ProductId:X4}");
        if (InterfaceNumber is { } mi)
        {
            text += string.Create(CultureInfo.InvariantCulture, $" MI_{mi:X2}");
        }

        if (UsagePage is { } page)
        {
            text += string.Create(CultureInfo.InvariantCulture, $" UP_{page:X4}");
        }

        if (Usage is { } usage)
        {
            text += string.Create(CultureInfo.InvariantCulture, $" U_{usage:X4}");
        }

        return text;
    }
}

/// <summary>
/// Um ID de hardware esperado, com o estado de verificação dele. <paramref name="Usb"/> é a forma estruturada
/// (com interface/usage page) quando o catálogo a informa; senão, tenta-se ler VID/PID do texto.
/// </summary>
public sealed record ExpectedHardwareId(string Id, VerificationStatus Status, string Evidence, UsbIdentity? Usb = null)
{
    public UsbIdentity? ResolveUsb() => Usb ?? (UsbIdentity.TryParse(Id, out var parsed) ? parsed : null);
}

/// <summary>Entrada do catálogo de dispositivos conhecidos (phase0/data/devices.json).</summary>
public sealed record KnownDevice(
    string Id,
    string DisplayName,
    string Category,
    string Bus,
    IReadOnlyList<ExpectedHardwareId> HardwareIds,
    IReadOnlyList<string> Replaces,
    IReadOnlyList<string> Capabilities,
    string WritePolicy,
    int Priority)
{
    /// <summary>Identidades USB/HID esperadas, extraídas dos IDs de hardware textuais.</summary>
    public IEnumerable<(UsbIdentity Identity, ExpectedHardwareId Source)> UsbIdentities()
    {
        foreach (var hw in HardwareIds)
        {
            if (hw.ResolveUsb() is { } identity)
            {
                yield return (identity, hw);
            }
        }
    }

    /// <summary>
    /// ADR-0005 regra 1: escrita só é permitida quando algum ID de hardware do dispositivo foi lido no hardware real.
    /// </summary>
    public bool WritesAllowed => HardwareIds.Any(h => h.Status == VerificationStatus.VerifiedOnHardware);

    /// <summary>
    /// A mesma regra para um caminho só: o ID de hardware que começa com <paramref name="hardwareIdPrefix"/> foi lido no
    /// hardware real. A GPU tem dois: o PCI (ajuste de clock e energia) e o I2C do MCU (RGB), liberados em separado.
    /// </summary>
    public bool Verified(string hardwareIdPrefix) => HardwareIds.Any(h =>
        h.Status == VerificationStatus.VerifiedOnHardware && h.Id.StartsWith(hardwareIdPrefix, StringComparison.OrdinalIgnoreCase));
}
