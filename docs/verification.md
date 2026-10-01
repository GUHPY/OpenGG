# Verification and evidence boundaries

Research began in the supplied September 2026 handoff. Hardware and installed OneRGB checks were performed on 30 September, followed by the standalone OpenGG checks on **1 October 2026**. Times recorded in individual artifacts may be UTC or Europe/London (+01:00).

## Target

| Property | Value |
|---|---|
| Keyboard | SteelSeries Apex Pro TKL Wireless Gen 3 |
| Connection | 2.4 GHz receiver |
| VID:PID | `1038:1644` |
| Interface / usage | `MI_03 / FFC0:0001` |
| Reports | Feature 642; input/output 65 |
| Tested keyboard firmware | 3.24.1 |
| Profile schema / size | 19 / 12,288 bytes |
| Desktop | Windows x64, WPF, .NET 10 |
| SDK for this package | 10.0.401 |

## Confirmed results

| Check | Evidence |
|---|---|
| Original capture reconstruction | 6,297 records; 18 complete banks; nine keyboard/receiver pairs; all CRCs valid |
| Receiver read | All five slots read and backed up; size/schema/CRC valid |
| Profile write | Keyboard and receiver block/validation ACKs; exact receiver readback |
| Earlier restoration | All five slots matched their then-current initial backups; slot 2 reactivated |
| Native/Python paths | Both exercised read/write/restore independently of GG |
| Live actuation/RT | `6F`, `76`, `77` ACK zero; stored bytes unchanged |
| RGB conflict fix | Continuous RGB reproduced missing advanced ACKs; serialized `62` release fixed feature and output paths |
| OLED | `4A` and `4B` ACK zero; pixel layout cross-checked against independent implementation |
| OneRGB regression suite | **1,303 passed, zero failed** after the batching change |
| OpenGG offline checks | **84 passed**; strict discovery, batching, global mode, preserved state, CRC, shared-channel recovery and per-key raw Protection preservation/validation |
| Python offline checks | Three unittest methods, including numerous field/preservation/failure assertions |
| OpenGG GUI | Slot 2 valid read, save enabled, no-change save avoided flash; 60 Applied batch entries for 1.6 and restoration to 1.5 |
| OpenGG diagnostic | `BC` connected, `D2` raw telemetry, `83` CRC/SHA and optional `6F`/`76` echo ACKs |
| Final portable executable | Native HID inventory: 40 collections, one recognized keyboard; PowerShell 5.1 inventory wrapper completed with a spaced output path; readable 260 px diagnostic report |
| Final OpenGG CLI | Slot 2 read, W re-sent at current 1.5 mm via `6F`, exact stored hash unchanged, slot 2 reloaded |
| Audio Reactive transport | WASAPI output-loopback initialized successfully; nonzero level observed; no recording stored |
| Desktop usability | 73 checks passed on the actual WPF window: shell/scroll behavior, brand resources, live receiver battery, charging animation, bottom-up sliders, inactive gray controls, independent bulk painting, safe slider seeding, stable tab extent/scroll, full-stage OLED camera, image conversion/transport and RGB transparency |
| Offline tool installers | Seven plans validated on Windows PowerShell 5.1; checksum and path-escape failure cases rejected; 66 local payload files prepared |
| Local tool runtime | Python 3.14.8, Frida 17.19.0, hidapi import, pip dependency check and independent CLI help exercised from the contained runtime |

The original OneRGB tests cover that original project's shared pipeline. OpenGG's own adapted assembly has its own offline checks and explicit native test; these are separate validation sets, not one inflated test total. [Machine-readable selected results](hardware-verification.json).

## Timing

| Measurement | Seconds | Boundary |
|---|---:|---|
| Old native individual loop | 8.5410694 | 60 separate full actuation reports |
| Original native batch under RGB | 0.1443179 | One `6F`, 68 entries, one gate, matching ACK |
| Standalone OpenGG native batch under RGB | 0.1491984 | Final adapted core assembly; current stored slot unchanged; restored in finally |
| Installed OneRGB UI 1.6 test | 3.950324 | Includes UI Automation traversal/polling |
| Installed OneRGB UI restore 1.5 | 4.4811853 | Includes UI Automation traversal/polling |
| OpenGG 1.6 GUI batch | ~0.190 | Maximum recorded completion duration among its 60 journal entries |
| OpenGG 1.5 restoration | 0.1784761 | Maximum recorded completion duration among its 60 journal entries |

The native comparison is roughly 59× faster on this receiver. GUI traversal is not USB transport time. Journal durations also include pipeline work; they are not physical-key latency measurements. The public native harness allows a fresh measurement on another setup without flashing.

## Preserve current settings

The September initial slot-2 backup had SHA256 `B6282EDA8C53D54BBB8C351987E2FD4D9C32BB0D72840B07EA0F23C13F288C95`. After the owner saved their current 1.5 mm settings, slot 2 had SHA256 **`3F06EC3E9BE7B7CA005413FDB01B18D08F6FD3F533F700D8E35E7E162D45433B`**. These are different checkpoints.

The later speed tests preserved the **current** stored profile and restored 1.5 mm, not an older all-1.0 profile. An OpenGG live operation leaves flash unchanged; reading and loading a stored profile is an explicit runtime reset to those stored values.

## What these checks do not prove

- ACK zero proves command acceptance, not a physical actuation-depth measurement.
- Receiver byte comparison proves receiver storage. The keyboard copy has block/validation ACK evidence, not an independent successful flash readback.
- A power-cycle persistence test was not performed.
- Every mapping combination, dual-action behavior, Rapid Tap mode, Protection effect and macro output was not physically exercised.
- The friendly macro editor does not compile to the native macro container yet.
- Cable `1646`, wired `1642`, Bluetooth and other SteelSeries models have no advanced-write validation from this work.
- Generic HID discovery and synthetic mouse/duplicate-device checks do not replace tests on every real keyboard and mouse.
- The tool inventory finds selected PATH/registry evidence; it does not validate every capture driver, portable install or optional Python package.
- Wireshark, USBPcap and SDK system installation were not performed during the usability checks. Their local payloads and installer plans were validated; the Install action opens their own interactive UI.
- The OLED's 1.4 label after a 1.5 setting was reproduced by the owner in GG. It does not establish the physical sensor threshold by itself.

## Artifact custody

`local-research/original-inputs/` retains the six supplied originals. `local-research/onergb-evidence/` retains historical native results, backups and the reconstruction manifest. `local-research/validation/` retains the standalone UI/diagnostic results. `local-research/conversation/session-snapshot.jsonl` retains a point-in-time full interaction record. This directory is gitignored and excluded from the portable package/public source.

Public docs contain exact byte tables, selected hashes/timings and reproducible tooling. Unfiltered traffic, actual user profile contents, vendor descriptors and secrets are not needed to redistribute an independent implementation.

## Re-run the usability and installer checks

```powershell
# Close OpenGG; this opens the real WPF window briefly, then closes it.
dotnet run --project tests/OpenGG.DesktopChecks/OpenGG.DesktopChecks.csproj -c Release
# Checks every prepared install plan and two isolated rejection cases; no system installer runs.
powershell -NoProfile -ExecutionPolicy Bypass -File tools/Test-ResearchTools.ps1
```

The desktop check uses real controls, templates, native window regions and routed wheel events. Its Apex layout assertions run when a verified receiver card is connected or remembered; other environments report those checks as skipped. Sidebar animation assertions respect the Windows reduced-motion setting. It never changes or saves an onboard profile. Installing Python/Frida is a separate explicit check; testing an installer plan does not claim a capture driver was installed.

The refined editor checks seed an isolated in-memory configuration, exercise sliders/painting and restore it before the debounce can commit. Local settings/profile files are restored after the check. Bitmap checks verify transparent pixels, aspect-preserving black margins, PNG decoding, first-frame GIF behavior, 640-byte packing, the existing vertical-page `4A` report and opacity blending over the gray PSD base. Source compilation finishes with zero warnings/errors. The ordinary portable diagnostic recorded after refinement returns ACK zero, valid schema/CRC and unchanged stored slot-2 SHA256 `3F06EC3E9BE7B7CA005413FDB01B18D08F6FD3F533F700D8E35E7E162D45433B`, without activation or save.

The English/brand pass additionally waits for a real battery reply through the normal desktop polling path, then tests changing percent/charge/disconnection on a detached card. Charge-animation checks use a detached BatteryBar and do not manufacture a live-device reading. Tab checks compare page extent and bottom offset before/after empty tabs. Full-stage checks use actual control bounds; all three fader fills are verified below their thumbs. Read-only telemetry does not establish calibrated battery capacity. The owner logo SVGs are retained unchanged and their generated PNG/ICO resources are loaded by the real window.

