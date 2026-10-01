using OneRGB.Hardware.Native;

namespace OpenGG;

public static class KeyboardDiagnostics
{
    public static string ReadFeature(KeyboardDevice device)
    {
        var vendor = device.Interfaces.FirstOrDefault(i => i.Error is null && i.Identity.UsagePage >= 0xFF00 && i.FeatureReportLength >= 2);
        if (vendor is null) { throw new NotSupportedException("This keyboard was identified but exposes no vendor feature report. Its configuration protocol has not been verified."); }
        using var channel = HidWriter.Open(vendor.DevicePath, vendor.OutputReportLength, vendor.FeatureReportLength);
        var data = channel.GetFeature(0);
        return $"HID GetFeature returned {data.Length} bytes. This is a transport read, not a verified command ACK or configuration support.";
    }
}
