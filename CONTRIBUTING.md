# Contributing

Build on Windows x64 with the .NET 10 SDK. The desktop uses WPF and native Windows HID; Python is optional.

```powershell
dotnet build OpenGG.slnx -c Release
dotnet run --project tests/OpenGG.Checks/OpenGG.Checks.csproj -c Release
python tools/test_opengg.py
dotnet run --project tests/OpenGG.DesktopChecks/OpenGG.DesktopChecks.csproj -c Release
powershell -ExecutionPolicy Bypass -File tools/Test-ResearchTools.ps1
```

The core offline checks never open a hardware handle; the Python checks use only the standard library. DesktopChecks is a separate interactive check: close OpenGG and other controllers first. It opens the real app, performs discovery, exercises layout/navigation and may send the existing temporary RGB preference while the Apex editor is active; it never changes or saves an onboard profile. Test-ResearchTools requires the prepared cache and validates plans without launching system installers. Run explicit protocol hardware checks separately.

For a new keyboard, attach an OpenGG diagnostic export, VID/PID, usage page/usage, interface number, report lengths, firmware version as actually reported by the vendor software, connection type and a small capture of one controlled change. Say whether the capture contains device replies or only host API calls. Remove unrelated traffic, macro contents, serial identifiers and anything you typed.

Identification support does not authorize advanced writes. Keep unknown models on the diagnostic page until their protocol has been independently captured and tested. Add an explicit model/schema identity, validate input before I/O, preserve untouched bytes, match replies to requests and stop after failure. Never scan arbitrary opcodes. Profile flashing must have a backup, acknowledgement for every block and a truthful account of the readback boundary.

Use the existing channel and control pipeline. A second reader or RGB writer on the same interface can consume replies or reactivate temporary RGB between release and an advanced command. Keep keyboard flash and receiver flash as two explicit destinations; do not call the transaction atomic.

Every protocol change needs one meaningful offline regression check and a dated hardware record when hardware is available. Report untested variants plainly. See [the research log](docs/research-log.md), [protocol](docs/protocol.md), [architecture](docs/architecture.md) and [verification](docs/verification.md).
