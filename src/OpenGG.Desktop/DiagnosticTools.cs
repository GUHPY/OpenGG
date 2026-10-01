using System.IO;
using Microsoft.Win32;

namespace OpenGG.Desktop;

public sealed record DiagnosticTool(string Id, string Name, string Availability, string Purpose, string Command, string Url, string ActionLabel, bool CanInstall);

public static class DiagnosticTools
{
    public static string Root
    {
        get
        {
            // A source build can use the repository's cache; a portable release uses its own folder.
            for (var folder = new DirectoryInfo(AppContext.BaseDirectory); folder is not null; folder = folder.Parent)
                if (Directory.Exists(Path.Combine(folder.FullName,"tooling","installers")) && File.Exists(Path.Combine(folder.FullName,"tools","Install-ResearchTool.ps1"))) { return folder.FullName; }
            return AppContext.BaseDirectory;
        }
    }

    public static IReadOnlyList<DiagnosticTool> Inventory()
    {
        var installed = new List<string>();
        foreach (var hive in new[] { Registry.LocalMachine, Registry.CurrentUser })
        foreach (var path in new[] { @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall", @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall" })
        {
            using var parent = hive.OpenSubKey(path);
            foreach (var key in parent?.GetSubKeyNames() ?? [])
            {
                using var item = parent!.OpenSubKey(key);
                if (item?.GetValue("DisplayName") is string name) { installed.Add(name); }
            }
        }
        var root = Root;
        var localPython = File.Exists(Path.Combine(root,"tooling","runtime","python","python.exe"));
        var cache = File.Exists(Path.Combine(root,"tooling","manifest.json")) && Directory.Exists(Path.Combine(root,"tooling","installers"));
        string Found(string name, string exe, string path) => installed.Any(p => p.Contains(name,StringComparison.OrdinalIgnoreCase)) || OnPath(exe) || File.Exists(path) ? "Found" : "Not found";
        string Local(string state) => state + (cache ? " · local installer available" : " · prepare the installers");
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        return [
            new("hid-inventory", "OpenGG HID inventory", "Included", "VID/PID, usage, interfaces, Container ID and report sizes. Native read-only inventory.", "OpenGG.exe --inspect --out inventory.json", "docs/diagnostics.md", "Open folder", true),
            new("protocol-cli", "OpenGG protocol CLI", Local("Included · Python/hidapi"), "Read and edit profiles, write with backups, and apply batched actuation on the verified receiver.", "tooling/runtime/python/python.exe tools/opengg.py --help", "docs/protocol.md", "Prepare", cache),
            new("capture-analyzer", "Capture analyzer", Local("Included · Python"), "Reconstruct complete profiles, compare offsets and validate capture CRCs.", "tooling/runtime/python/python.exe tools/analyze_capture.py capture.json --out local-research/banks", "docs/reproduce.md", "Prepare", cache),
            new("etw-capture", "ETW USB capture", "Included · administrator", "Mark steps and capture ETW. On Windows build 26200, UCX exposed headers without useful payloads during this research.", "powershell -File tools/Capture-UsbTrace.ps1 -DryRun", "https://learn.microsoft.com/en-us/windows-hardware/drivers/usbcon/how-to-capture-a-usb-event-trace", "Open folder", true),
            new("python", "Python", Local(localPython ? "Installed in the OpenGG folder" : OnPath("python.exe") ? "Found on PATH" : "Not found"), "Prepare local Python for the scripts while preserving your system Python. The full installer is also included.", "tooling/runtime/python/python.exe --version", "https://www.python.org/downloads/windows/", "Install", cache),
            new("wireshark", "Wireshark", Local(Found("Wireshark","wireshark.exe",Path.Combine(programFiles,"Wireshark","Wireshark.exe"))), "Analyze USBPcap/PCAPNG transfers using captures that contain USB payloads.", "Wireshark + USBPcap", "https://www.wireshark.org/docs/wsug_html_chunked/", "Install", cache),
            new("usbpcap", "USBPcap", Local(Found("USBPcap","USBPcapCMD.exe",Path.Combine(programFiles,"USBPcap","USBPcapCMD.exe"))), "Capture USB traffic with a dedicated driver. Open the local installer to choose its options.", "USBPcapCMD.exe --help", "https://github.com/desowin/usbpcap", "Install", cache),
            new("frida", "Frida", Local(File.Exists(Path.Combine(root,"tooling","runtime","python","Scripts","frida.exe")) ? "Installed in the OpenGG folder" : OnPath("frida.exe") ? "Found on PATH" : "Not found"), "Local instrumentation tools. Installation does not start injection; detaching from GG caused a crash during this research.", "tooling/runtime/python/Scripts/frida.exe --version", "https://frida.re/docs/home/", "Install", cache),
            new("usbview", "USBView", Local(OnPath("usbview.exe") || File.Exists(Path.Combine(programFilesX86,"Windows Kits","10","Debuggers","x64","usbview.exe")) ? "Found" : "Not found"), "USB descriptors and topology. Install Windows SDK Debugging Tools using the bundled files.", "usbview.exe", "https://learn.microsoft.com/en-us/windows-hardware/drivers/debugger/usbview", "Install", cache),
        ];
    }

    private static bool OnPath(string name) => (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator)
        .Any(folder => !string.IsNullOrWhiteSpace(folder) && File.Exists(Path.Combine(folder.Trim('"'),name)));
}
