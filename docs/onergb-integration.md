# Returning the Apex editor to OneRGB

OpenGG is the owner's public SteelSeries keyboard section of OneRGB. On 1 October 2026, its updated Apex interface and native protocol were ported back to the original OneRGB project. The closed-source application keeps its complete sidebar, services, device support and existing canvas.

## What moved

| Original OneRGB location | Change |
|---|---|
| `Application/Devices/Keyboard` | Profile format, nonlinear actuation tables, raw Protection sensitivity, OLED packing and explicit receiver/USB command routing |
| `Hardware/Native/ApexHidLink.cs` | One shared channel, temporary-RGB release, routed ACK matching, correct direct USB bank and backed-up readback |
| `Hardware/Integrations/ApexKeyboardIntegration.cs` | Verified transport discovery, battery query, loaded-state gate and native controls |
| `Hardware/Lighting/ApexLighting.cs` | Exact transport selection and the existing single native RGB sink |
| `Desktop/Controls/ApexKeyboard.cs` and `OledImage.cs` | Owner artwork, full-stage camera, transparent RGB opacity and built-in image conversion |
| `Desktop/ViewModels/KeyboardViewModel.cs` and `Views/Pages/KeyboardPage.xaml` | Three bottom-up faders, feature painting, profile controls and combined keyboard-tool tabs |
| `Desktop/Views/Pages/ApexLightingPage.xaml` | Color/OLED editing and the battery/ownership/feedback card |
| `Application/Lighting/Spatial/ApexEffects.cs` | Six small samplers wrapping the existing lighting-color functions; no second RGB loop |
| `Desktop/ViewModels/ApexLightingViewModel.cs` | Existing canvas edits restricted to the keyboard placement's effect and brightness |
| `Desktop/Views/Pages/DevicesPage` | RGB first, duplicate Apex hero removed and one outer scroll viewer; other device pages retain their previous behavior |
| `Desktop/Theme/Controls.xaml`, `Controls/Motion.cs`, `Mvvm/Mvvm.cs` | Only the fader template, retained-space panel transitions and typed enum conversion required by the Apex views |
| `Hardware/System/VendorAppDetector.cs` and `Desktop/AppHost.cs` | OpenGG recognized by the existing owner guard for every Apex resource |

All paths in this table belong to `OneRGB/src/OneRGB.*`. Original source snapshots are retained privately under `local-research/onergb-port-before/`. The [import manifest](import-provenance.json) keeps the original extraction SHA256 and records the changed OneRGB source separately. It does not rewrite history to make the later backport look like the initial import.

## Behavior and persistence

Opening the Apex page reads an existing saved Apex canvas effect without resetting the lighting scene. A lighting edit updates only the keyboard placement; other devices' effects and locations remain intact. RGB continues through OneRGB's established canvas, ownership system and native sink. The native configuration and OLED operations share the same Apex HID channel.

Profile edits retain the same load-before-live gate, baseline comparison, fresh binary backup, preserved unknown fields and exact transport-specific readback described in [the protocol reference](protocol.md). USB belongs to the wireless model (`1646`), with its own bank and command bytes. The wired-only `1642` model and Bluetooth remain unvalidated.

## Reproduce the original project's checks

From the OneRGB repository:

```powershell
dotnet test --project tests/OneRGB.Tests/OneRGB.Tests.csproj -c Release
powershell -NoProfile -ExecutionPolicy Bypass -File tools/Publish-OneRGB.ps1
```

OneRGB's `AGENTS.md` requires Release publication and installation together. The publisher builds Desktop and Service as framework-dependent apps, updates `artifacts/release` and `artifacts/app`, then invokes the Windows installer for the application and `OneRGBPrivileged` service. That installer records both client executable and DLL SHA256 for named-pipe authorization. Windows can request UAC confirmation.

The backport passed **1,304 unit tests** and **13 targeted real-window Apex checks**. The targeted harness loaded real templates and views, checked page ordering/scroll/zoom/fader geometry, changed only an in-memory canvas effect and confirmed unrelated placements stayed equal. It never started the host lighting loops or wrote an onboard profile. Actual USB protocol tests are recorded separately in [verification](verification.md), rather than attributed to a UI test.

Publication completed for both OneRGB Desktop and Service. The Windows UAC installation was canceled, so `C:\Program Files\OneRGB` and its client authorization metadata were not updated. The new application is available at `OneRGB/artifacts/app/OneRGB.exe`; running the repository installer completes the system update. The previously installed service remains running.
