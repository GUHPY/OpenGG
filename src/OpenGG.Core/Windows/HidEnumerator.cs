using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using OneRGB.Application;
using OneRGB.Domain;
using static OneRGB.Windows.Interop.NativeMethods;

namespace OneRGB.Windows;

/// <summary>Uma interface HID presente no sistema, com os dados necessários para identificar o dispositivo.</summary>
public sealed record HidInterfaceInfo(
    string DevicePath,
    UsbIdentity Identity,
    ushort VersionNumber,
    string? Product,
    string? Manufacturer,
    ushort InputReportLength,
    ushort OutputReportLength,
    ushort FeatureReportLength,
    string? Error)
{
    public Guid? ContainerId { get; init; }
    public ObservedDevice ToObserved() => new(Identity, Product, Manufacturer, DevicePath);
}

/// <summary>
/// Enumera interfaces HID SOMENTE LENDO. Cada dispositivo é aberto com acesso 0, que permite consultar
/// atributos e capacidades mas impede, pelo próprio Windows, qualquer envio de report (ADR-0005, threat model).
/// </summary>
public static partial class HidEnumerator
{
    /// <param name="pathContains">
    /// Só abre as interfaces cujo caminho contém isto (<c>054c</c>): quem procura um dispositivo a cada poucos segundos
    /// não consulta todos, que no Bluetooth vão pelo rádio.
    /// </param>
    public static IReadOnlyList<HidInterfaceInfo> Enumerate(string? pathContains = null)
    {
        HidD_GetHidGuid(out var hidGuid);
        var set = SetupDiGetClassDevs(in hidGuid, 0, 0, DigcfPresent | DigcfDeviceInterface);
        if (set == -1)
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError(), "SetupDiGetClassDevs(HID) failed.");
        }

        try
        {
            var results = new List<HidInterfaceInfo>();
            for (uint index = 0; ; index++)
            {
                var data = new SpDeviceInterfaceData { CbSize = (uint)Unsafe.SizeOf<SpDeviceInterfaceData>() };
                if (!SetupDiEnumDeviceInterfaces(set, 0, in hidGuid, index, ref data))
                {
                    if (Marshal.GetLastPInvokeError() == ErrorNoMoreItems)
                    {
                        break;
                    }

                    continue;
                }

                var path = GetInterfacePath(set, ref data, out var containerId);
                if (path is not null && (pathContains is null || path.Contains(pathContains, StringComparison.OrdinalIgnoreCase)))
                {
                    results.Add(Describe(path) with { ContainerId = containerId });
                }
            }

            return results;
        }
        finally
        {
            SetupDiDestroyDeviceInfoList(set);
        }
    }

    private static string? GetInterfacePath(nint set, ref SpDeviceInterfaceData data, out Guid? containerId)
    {
        containerId = null;
        SetupDiGetDeviceInterfaceDetail(set, ref data, 0, 0, out var required, 0);
        if (required == 0)
        {
            return null;
        }

        var buffer = Marshal.AllocHGlobal((int)required);
        var devinfo = Marshal.AllocHGlobal(Unsafe.SizeOf<SpDevinfoData>());
        try
        {
            // SP_DEVICE_INTERFACE_DETAIL_DATA_W.cbSize = tamanho da parte fixa: 8 em x64, 6 em x86.
            Marshal.WriteInt32(buffer, IntPtr.Size == 8 ? 8 : 6);
            Marshal.WriteInt32(devinfo, Unsafe.SizeOf<SpDevinfoData>());
            if (!SetupDiGetDeviceInterfaceDetail(set, ref data, buffer, required, out _, devinfo))
            {
                return null;
            }

            var property = new DevicePropertyKey { Format = new Guid("8C7ED206-3F8A-4827-B3AB-AE9E1FAEFC6C"), Id = 2 };
            if (ReadContainerId(set, devinfo, in property, out var type, out var value, 16, out var size, 0)
                && type == 0x0D && size == 16 && value != Guid.Empty) { containerId = value; }
            return Marshal.PtrToStringUni(buffer + 4);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
            Marshal.FreeHGlobal(devinfo);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DevicePropertyKey { public Guid Format; public uint Id; }

    [LibraryImport("setupapi.dll", EntryPoint = "SetupDiGetDevicePropertyW", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ReadContainerId(nint set, nint devinfo, in DevicePropertyKey property,
        out uint type, out Guid buffer, uint bufferSize, out uint requiredSize, uint flags);

    private static HidInterfaceInfo Describe(string path)
    {
        // O caminho só serve de reserva (e para o número da interface, MI_xx); VID/PID oficiais vêm de HidD_GetAttributes.
        if (!UsbIdentity.TryParse(path, out var fromPath))
        {
            fromPath = new UsbIdentity(0, 0);
        }

        using var handle = CreateFile(path, 0, FileShareRead | FileShareWrite, 0, OpenExisting, 0, 0);
        if (handle.IsInvalid)
        {
            var error = new Win32Exception(Marshal.GetLastPInvokeError()).Message;
            return new HidInterfaceInfo(path, fromPath, 0, null, null, 0, 0, 0, $"Could not open for inspection: {error}");
        }

        var attributes = new HiddAttributes { Size = (uint)Unsafe.SizeOf<HiddAttributes>() };
        var hasAttributes = HidD_GetAttributes(handle, ref attributes);

        ushort usagePage = 0, usage = 0, inLen = 0, outLen = 0, featLen = 0;
        string? capsError = null;
        if (HidD_GetPreparsedData(handle, out var preparsed))
        {
            try
            {
                if (HidP_GetCaps(preparsed, out var caps) == HidpStatusSuccess)
                {
                    usagePage = caps.UsagePage;
                    usage = caps.Usage;
                    inLen = caps.InputReportByteLength;
                    outLen = caps.OutputReportByteLength;
                    featLen = caps.FeatureReportByteLength;
                }
            }
            finally
            {
                HidD_FreePreparsedData(preparsed);
            }
        }
        else
        {
            capsError = "HidD_GetPreparsedData failed.";
        }

        var identity = new UsbIdentity(
            hasAttributes ? attributes.VendorId : fromPath.VendorId,
            hasAttributes ? attributes.ProductId : fromPath.ProductId,
            fromPath.InterfaceNumber,
            usagePage == 0 ? null : usagePage,
            usage == 0 ? null : usage);

        return new HidInterfaceInfo(
            path,
            identity,
            attributes.VersionNumber,
            ReadString(handle, product: true),
            ReadString(handle, product: false),
            inLen,
            outLen,
            featLen,
            capsError);
    }

    private static string? ReadString(Microsoft.Win32.SafeHandles.SafeFileHandle handle, bool product)
    {
        // Limite documentado das strings HID: 126 caracteres + terminador.
        const int bytes = 256;
        var buffer = Marshal.AllocHGlobal(bytes);
        try
        {
            var ok = product ? HidD_GetProductString(handle, buffer, bytes) : HidD_GetManufacturerString(handle, buffer, bytes);
            return ok ? Marshal.PtrToStringUni(buffer) : null;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }
}
