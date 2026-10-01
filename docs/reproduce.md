# Reproduce the work

Commands below assume a Windows PowerShell prompt at the OpenGG repository root. Keep actual profile files and captures under `local-research/`; it is gitignored. Use the current stored profile as the baseline, not an old backup from a previous experiment.

## 1. Build and run offline checks

Install/use the .NET 10 SDK. This project was built with SDK **10.0.401** on Windows x64. The desktop/core use no third party NuGet packages.

```powershell
dotnet build OpenGG.slnx -c Release
dotnet run --project tests/OpenGG.Checks/OpenGG.Checks.csproj -c Release
python tools/test_opengg.py
```

The C# checks cover identification, duplicate containers, mouse exclusion, strict receiver/cable sizes, consolidated actuation/RT, staging preservation and CRC/global mode. The Python checks cover reconstruction, exact changed bytes, untouched-byte preservation, RGB release ordering, strict live validation and stopping after an injected erase failure. No hardware is opened by either default check.

## 2. Export a native inventory

```powershell
Start-Process -FilePath '.\src\OpenGG.Desktop\bin\Release\net10.0-windows\win-x64\OpenGG.exe' `
  -ArgumentList '--inspect --out inventory.json' -Wait
```

Or use the Diagnostics inventory button. Enumeration obtains HID attributes/caps, usage/interface, report lengths, Container ID and device-revision field. These read-only properties establish topology; they do not establish support for arbitrary settings. Native inventory can include all HID collections, while the device-card selector accepts SteelSeries keyboards only.

## 3. Optional Python hardware setup

Use a local Python environment. The desktop remains independent of it.

```powershell
python -m venv .venv
.\.venv\Scripts\python.exe -m pip install hidapi
.\.venv\Scripts\python.exe tools/opengg.py --help
New-Item -ItemType Directory -Force -Path local-research | Out-Null
```

The CLI checks for conflicting controllers and exactly one selected `1038:1644` (receiver) or `1038:1646` (USB) `MI_03 / FFC0:0001` collection. It prefers USB when both are present. Use `--pid 0x1644` or `--pid 0x1646` on read/load/live/write to choose explicitly. Fully exit GG/Engine, Prism, OneRGB, OpenGG, OpenRGB and SignalRGB before opening that external CLI. OneRGB can remain in the tray after its window closes.

## 4. Read, patch and compare offline

```powershell
.\.venv\Scripts\python.exe tools/opengg.py read --slot 2 `
  --profile local-research/current-slot-2.bin --log local-research/read.jsonl

.\.venv\Scripts\python.exe tools/opengg.py edit `
  --profile local-research/current-slot-2.bin --edits tools/profile-edits.example.json `
  --out local-research/edited-slot-2.bin
```

The example demonstrates more than actuation; review it before use. `edit` is entirely offline and validates the original CRC/schema, keys, ranges, mappings, pair counts and byte lengths. It returns a patched copy with a new CRC and preserved unrelated fields.

Supported JSON fields are `actuation`, `rt`, `protection`, `protection_duration_ms`, `protection_sensitivity`, `rapid_tap`, `remap`, `meta`, `dual`, `name`, `oled_hex` and `macro_hex`. Usage keys can be decimal strings or `0x...`. The [profile format](profile-format.md) specifies the raw fields and unverified physical meanings.

## 5. Explicit backed-up write

```powershell
.\.venv\Scripts\python.exe tools/opengg.py write --slot 2 `
  --profile local-research/edited-slot-2.bin --expected local-research/current-slot-2.bin `
  --backup local-research/backups --log local-research/write.jsonl

.\.venv\Scripts\python.exe tools/opengg.py load --slot 2
```

`--expected` rejects a stored profile that changed after your read, before any erase. The CLI always creates a current-profile backup before erase. It stops on the first error and reports a possible partial transaction; it never automatically retries flash. Readback compares the receiver copy wirelessly and the physical keyboard copy over USB. USB does not rewrite the receiver's cached copy. Activation is explicit.

To restore an intentionally chosen backup, first read the current destination into a new baseline, then use the same write transaction with that current baseline as `--expected` and the backup as `--profile`. This makes the intended replacement explicit and catches outside changes. Do not automatically replace today's settings with the original research backup.

## 6. Live batch without flash

```powershell
.\.venv\Scripts\python.exe tools/opengg.py live --slot 2 `
  --edits tools/live-edits.example.json --log local-research/live.jsonl
```

`live` accepts only `actuation` and `rt`. It reads the chosen slot, validates/builds every final report, explicitly activates that slot, then sends at most one report per family. This starts from stored slot values, not arbitrary unsaved settings left by another controller. W in the example is usage 26. To undo temporary changes, load the chosen stored slot again.

RGB release `62` wirelessly or `22` over USB precedes advanced feature commands. `--delay-ms` defaults to 31 and is available for controlled timing experiments; raising it can help a slow receiver. Reducing it below the tested default is not a validated improvement.

## 7. Reconstruct the supplied capture

```powershell
python tools/analyze_capture.py local-research/original-inputs/capture.json `
  --out local-research/reconstructed-banks
```

The supplied complete capture should report 6,297 records and 18 valid banks. The manifest records source step, destination, size, CRC, SHA256 and changed offsets. Partial banks and unrelated traffic are ignored. The analyzer is useful even without hidapi or connected hardware.

Capture a single setting change with marked steps. The translated ETW helper retains the original provider/keyword choices:

```powershell
powershell -File tools/Capture-UsbTrace.ps1 -DryRun "All keys 1.0 mm" "W 2.0 mm" "Restore"
# For a real capture, start PowerShell as administrator and omit -DryRun.
```

Inspect the actual events before assuming payload exists. On this machine's Windows 26200 capture, useful UCX payload was missing. A complete process-API feature payload can still be analyzed, but label its origin and do not invent device replies.

## 8. Explicit native hardware/audio checks

Close all controllers, including OpenGG itself, before the hardware check:

```powershell
dotnet run --project tests/OpenGG.Checks/OpenGG.Checks.csproj -c Release -- --hardware
```

This opt-in check reads/activates **slot 2**, sends temporary RGB continuously, applies a 60-key 2.0 mm batch, checks the stored slot is byte-for-byte unchanged and reloads slot 2 in `finally`. It performs no flash erase/write. Its activation changes the active slot, so choose this check only when slot 2 is the intended test slot. Keep the result alongside a dated firmware/connection record.

```powershell
dotnet run --project tests/OpenGG.Checks/OpenGG.Checks.csproj -c Release -- --audio
```

The audio check initializes native WASAPI loopback and reports its level; it saves no recording. It is separate from default offline checks and depends on the current Windows output device.

## 9. Package the desktop

```powershell
powershell -ExecutionPolicy Bypass -File tools/build.ps1
# Existing per-user dotnet installations can be passed explicitly:
powershell -ExecutionPolicy Bypass -File tools/build.ps1 -Dotnet "$env:USERPROFILE\.dotnet\dotnet.exe"
```

Output is `artifacts/portable/OpenGG.exe` and `artifacts/OpenGG-0.1.0-win-x64.zip`, with docs, licenses and scripts. The default is self-contained. `-FrameworkDependent` creates a smaller developer package requiring the .NET 10 Desktop Runtime. Build output and local evidence stay outside Git.
