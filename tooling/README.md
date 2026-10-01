# Offline research tools

The prepared Windows x64 package includes actual installers and offline Python wheels in `tooling/installers/`. Use **Diagnostics → Install** in OpenGG, or the script below. Installation uses local files; it does not download packages on click.

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools/Install-ResearchTool.ps1 -Tool frida
# Validate local payloads without installing anything:
powershell -NoProfile -ExecutionPolicy Bypass -File tools/Install-ResearchTool.ps1 -Tool usbview -DryRun
```

| Tool | Local payload | Button behavior |
|---|---|---|
| Python 3.14.8 | Full Windows installer and embedded x64 archive | Prepares `tooling/runtime/python` without changing the system Python |
| Frida tools 14.10.4 / Frida 17.19.0 | Wheels with all dependencies | Installs into the local Python, offline; does not attach to a process |
| Protocol CLI | `tools/opengg.py`, key map and hidapi wheel | Prepares local Python and hidapi |
| Capture analyzer | `tools/analyze_capture.py` | Prepares local Python; only the standard library is needed |
| Wireshark 4.6.9 | Official x64 installer | Opens its installer UI |
| USBPcap 1.5.4.0 | Official installer, source and symbols archive | Opens its installer UI |
| USBView | Windows SDK 10.0.26100.9457 Debugging Tools offline layout | Opens the SDK installer with Debugging Tools selected |
| HID inventory / ETW USB capture | Native OpenGG inventory and PowerShell scripts | Opens the included scripts folder; Windows supplies ETW |

USBView is supplied by Microsoft's Debugging Tools for Windows. The prepared layout contains its installer dependencies; the SDK installer performs the final installation. Its default executable is `C:\Program Files (x86)\Windows Kits\10\Debuggers\x64\usbview.exe`. See the [official USBView instructions](https://learn.microsoft.com/en-us/windows-hardware/drivers/debugger/usbview).

External installers remain interactive: their own UI handles licenses, elevation, destination and driver options. OpenGG does not restart Windows or install a capture driver automatically. The complete Python installer remains available for users who prefer a system installation; the app's Python button deliberately uses the contained runtime.

`manifest.json` records the upstream URLs, byte sizes and SHA256 hashes of every prepared payload. The install script checks hashes and rejects paths outside the cache before executing or extracting files. Hashes identify this prepared snapshot; they are not a substitute for upstream trust. The preparation script also validates Python, Wireshark and Microsoft SDK installer signatures and checks Wireshark's published checksum.

The installer cache and generated Python runtime are ignored by Git. They exist locally and the default portable ZIP carries the installer cache. A source checkout obtains the pinned packages with:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools/Fetch-ResearchTools.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File tools/Test-ResearchTools.ps1
# Also exercise offline Python/Frida/hidapi preparation (no system installers):
powershell -NoProfile -ExecutionPolicy Bypass -File tools/Test-ResearchTools.ps1 -PreparePythonTools
```

Preparation needs internet access and an existing Python with pip to build the offline wheel set. It downloads a Debugging Tools layout rather than installing the SDK on the preparation machine. The desktop itself remains self-contained and needs no Python.

After preparing Frida:

```powershell
tooling/runtime/python/python.exe --version
tooling/runtime/python/python.exe -m pip check
tooling/runtime/python/Scripts/frida.exe --version
tooling/runtime/python/python.exe tools/opengg.py --help
```

Keep this folder writable when preparing Python. If you move OpenGG after preparing Frida, click Install again to regenerate Python's executable launchers at the new location. These are separate third-party programs, covered by their own licenses; see [the attribution notice](../THIRD_PARTY_NOTICES.md).
