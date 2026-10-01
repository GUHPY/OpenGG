using System.ComponentModel;
using System.Globalization;
using System.Runtime.InteropServices;

namespace OneRGB.Application.Integrations;

public enum DeviceErrorKind
{
    AccessDenied,
    InUse,
    NotConnected,
    Timeout,
    IoFailure,
    NotSupported,
    MissingDependency,
    Unknown,
}

/// <summary>Erro de dispositivo classificado: o tipo, a mensagem nativa, o que o usuário pode fazer e o código Win32.</summary>
public sealed record DeviceError(DeviceErrorKind Kind, string Message, string Advice, int? Code = null);

/// <summary>
/// Taxonomia única de erros de hardware (plano 1.3): exceções de Win32, COM, E/S e HTTP local viram um tipo com
/// orientação, em vez de cada integração inventar o próprio texto.
/// </summary>
public static class DeviceErrors
{
    public static DeviceError Classify(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        var (kind, code) = exception switch
        {
            Win32Exception win32 => (FromWin32(win32.NativeErrorCode), (int?)win32.NativeErrorCode),
            COMException com => FromHResult(com.HResult),
            UnauthorizedAccessException => (DeviceErrorKind.AccessDenied, (int?)5),
            TimeoutException or OperationCanceledException => (DeviceErrorKind.Timeout, null),
            DllNotFoundException or EntryPointNotFoundException or BadImageFormatException => (DeviceErrorKind.MissingDependency, null),
            NotSupportedException => (DeviceErrorKind.NotSupported, null),
            HttpRequestException => (DeviceErrorKind.NotConnected, null),
            IOException io => FromHResult(io.HResult),
            _ => (DeviceErrorKind.Unknown, null),
        };
        return new DeviceError(kind, exception.Message, Advice(kind), code);
    }

    /// <summary>Código Win32 (GetLastError) → tipo.</summary>
    public static DeviceErrorKind FromWin32(int code) => code switch
    {
        5 => DeviceErrorKind.AccessDenied, // ERROR_ACCESS_DENIED
        32 or 33 or 170 => DeviceErrorKind.InUse, // SHARING_VIOLATION, LOCK_VIOLATION, BUSY
        2 or 3 or 21 or 433 or 1167 => DeviceErrorKind.NotConnected, // FILE/PATH_NOT_FOUND, NOT_READY, NO_SUCH_DEVICE, DEVICE_NOT_CONNECTED
        121 or 258 or 1460 => DeviceErrorKind.Timeout, // SEM_TIMEOUT, WAIT_TIMEOUT, TIMEOUT
        23 or 31 or 1117 => DeviceErrorKind.IoFailure, // CRC, GEN_FAILURE, IO_DEVICE
        1 or 50 or 1168 => DeviceErrorKind.NotSupported, // INVALID_FUNCTION, NOT_SUPPORTED, NOT_FOUND (propriedade sem suporte)
        126 or 127 or 193 => DeviceErrorKind.MissingDependency, // MOD_NOT_FOUND, PROC_NOT_FOUND, BAD_EXE_FORMAT
        _ => DeviceErrorKind.Unknown,
    };

    public static string Text(DeviceErrorKind kind) => kind switch
    {
        DeviceErrorKind.AccessDenied => "acesso negado",
        DeviceErrorKind.InUse => "in use by another program",
        DeviceErrorKind.NotConnected => "desconectado",
        DeviceErrorKind.Timeout => "no response",
        DeviceErrorKind.IoFailure => "communication failure",
        DeviceErrorKind.NotSupported => "unsupported",
        DeviceErrorKind.MissingDependency => "componente ausente",
        _ => "erro",
    };

    public static string Advice(DeviceErrorKind kind) => kind switch
    {
        DeviceErrorKind.AccessDenied => "Windows denied access. See Diagnostics for interface details and required permissions.",
        DeviceErrorKind.InUse => "Another program has opened the device. Close other keyboard controllers and retry.",
        DeviceErrorKind.NotConnected => "The device disconnected or its software is unavailable. Reconnect it; OpenGG scans automatically.",
        DeviceErrorKind.Timeout => "The device did not respond in time. Retry; if it repeats, reconnect the device.",
        DeviceErrorKind.IoFailure => "The device rejected or corrupted the request. Reconnect it; repeated failures may indicate unsupported firmware.",
        DeviceErrorKind.NotSupported => "This device or firmware does not support the feature.",
        DeviceErrorKind.MissingDependency => "A required vendor driver or library is missing.",
        _ => "Unexpected error; details are in the integration log.",
    };

    private static (DeviceErrorKind Kind, int? Code) FromHResult(int hresult)
    {
        // HRESULT_FROM_WIN32: 0x8007xxxx carrega o código Win32 nos 16 bits de baixo.
        if ((hresult & unchecked((int)0xFFFF0000)) == unchecked((int)0x80070000))
        {
            var code = hresult & 0xFFFF;
            return (FromWin32(code), code);
        }

        return hresult switch
        {
            unchecked((int)0x80040154) => (DeviceErrorKind.MissingDependency, null), // REGDB_E_CLASSNOTREG
            unchecked((int)0x80004001) => (DeviceErrorKind.NotSupported, null), // E_NOTIMPL
            unchecked((int)0x88890004) => (DeviceErrorKind.NotConnected, null), // AUDCLNT_E_DEVICE_INVALIDATED
            unchecked((int)0x8889000A) => (DeviceErrorKind.InUse, null), // AUDCLNT_E_DEVICE_IN_USE
            _ => (DeviceErrorKind.Unknown, (int?)null),
        };
    }

    /// <summary>Uma linha para o registro: tipo, mensagem e código.</summary>
    public static string Describe(DeviceError error)
    {
        ArgumentNullException.ThrowIfNull(error);
        return error.Code is { } code
            ? string.Create(CultureInfo.InvariantCulture, $"{Text(error.Kind)} (Win32 {code}): {error.Message}")
            : $"{Text(error.Kind)}: {error.Message}";
    }
}
