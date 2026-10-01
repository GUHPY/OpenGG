using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace OneRGB.Hardware.Native;

/// <summary>
/// Canal de escrita HID para UMA interface cuja identidade já foi conferida por quem chama.
/// Não é criado diretamente pela UI: só o <see cref="Lighting.QuadCastLighting"/> e o <see cref="Lighting.FusionLighting"/>
/// abrem, depois de conferir VID/PID/interface/usage page exatos e obter posse no ControlArbiter.
/// </summary>
internal sealed partial class HidWriter : IDisposable
{
    private const uint GenericRead = 0x80000000;
    private const uint GenericWrite = 0x40000000;
    private const uint FileShareRead = 0x1;
    private const uint FileShareWrite = 0x2;
    private const uint OpenExisting = 3;

    private readonly SafeFileHandle _handle;

    private HidWriter(SafeFileHandle handle, int outputLength, int featureLength)
    {
        _handle = handle;
        OutputReportLength = outputLength;
        FeatureReportLength = featureLength;
    }

    public int OutputReportLength { get; }

    public int FeatureReportLength { get; }

    public static HidWriter Open(string devicePath, int outputLength, int featureLength)
    {
        var handle = CreateFile(devicePath, GenericRead | GenericWrite, FileShareRead | FileShareWrite, 0, OpenExisting, 0, 0);
        if (handle.IsInvalid)
        {
            var error = Marshal.GetLastPInvokeError();
            handle.Dispose();
            throw new Win32Exception(error, "Could not open the device for writing (another app may be using it).");
        }

        return new HidWriter(handle, outputLength, featureLength);
    }

    /// <summary>
    /// Envia um report. O primeiro byte de <paramref name="report"/> é o report ID. O buffer é completado com
    /// zeros até o tamanho que o próprio dispositivo declara; report maior que isso é recusado.
    /// </summary>
    public void Send(ReadOnlySpan<byte> report)
    {
        if (OutputReportLength >= report.Length)
        {
            var buffer = new byte[OutputReportLength];
            report.CopyTo(buffer);
            if (!WriteFile(_handle, buffer, (uint)buffer.Length, out _, 0))
            {
                throw new Win32Exception(Marshal.GetLastPInvokeError(), "WriteFile failed on the HID device.");
            }

            return;
        }

        SetFeature(report);
    }

    /// <summary>
    /// Relatório de saída por WriteFile. Nunca cai em SetFeature: um comando curto que o dispositivo declara
    /// menor do que o pacote iria para o lugar errado.
    /// </summary>
    public void WriteOutput(ReadOnlySpan<byte> report)
    {
        var length = OutputReportLength >= report.Length ? OutputReportLength : report.Length;
        if (length <= 0)
        {
            throw new InvalidOperationException("The device declares no output report. Nothing was sent.");
        }

        var buffer = new byte[length];
        report.CopyTo(buffer);
        if (!WriteFile(_handle, buffer, (uint)buffer.Length, out _, 0))
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError(), "WriteFile failed on the HID device.");
        }
    }

    /// <summary>Envia como report de recurso (feature) mesmo quando caberia no de saída, completado com zeros.</summary>
    public void SetFeature(ReadOnlySpan<byte> report)
    {
        if (FeatureReportLength < report.Length)
        {
            throw new InvalidOperationException(
                $"The device declares {OutputReportLength}-byte output and {FeatureReportLength}-byte feature reports; the packet has {report.Length} bytes. Nothing was sent.");
        }

        var buffer = new byte[FeatureReportLength];
        report.CopyTo(buffer);
        if (!HidD_SetFeature(_handle, buffer, (uint)buffer.Length))
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError(), "HidD_SetFeature failed.");
        }
    }

    /// <summary>Lê um report de recurso (feature) com o id <paramref name="reportId"/> no byte 0, no tamanho que o dispositivo declara.</summary>
    public byte[] GetFeature(byte reportId)
    {
        var buffer = new byte[FeatureReportLength];
        buffer[0] = reportId;
        return HidD_GetFeature(_handle, buffer, (uint)buffer.Length)
            ? buffer
            : throw new Win32Exception(Marshal.GetLastPInvokeError(), "HidD_GetFeature failed.");
    }

    public void Dispose() => _handle.Dispose();

    [LibraryImport("kernel32.dll", EntryPoint = "CreateFileW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial SafeFileHandle CreateFile(string fileName, uint desiredAccess, uint shareMode, nint securityAttributes, uint creationDisposition, uint flagsAndAttributes, nint templateFile);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool WriteFile(SafeFileHandle file, byte[] buffer, uint bytesToWrite, out uint bytesWritten, nint overlapped);

    [LibraryImport("hid.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool HidD_SetFeature(SafeFileHandle device, byte[] buffer, uint length);

    [LibraryImport("hid.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool HidD_GetFeature(SafeFileHandle device, byte[] buffer, uint length);
}
