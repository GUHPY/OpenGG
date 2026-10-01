# Troubleshooting

## No device found

Connect a SteelSeries keyboard and press Search/F5. Discovery requires a standard keyboard collection with VID 1038, usage page 1 and usage 6. Known mouse product names are excluded because a mouse may also expose a keyboard collection for shortcuts. Check Diagnostics' read-only HID inventory for reported product, vendor and capabilities.

A disconnected remembered card is expected. A query/filter can make the visible list empty without losing remembered cards. Bluetooth IDs and vendor-only collections may need a new identification path; attach an inventory instead of forcing the Apex protocol onto them.

## The Apex card has generic controls

Advanced support requires receiver `1038:1644` or the wireless model connected by USB `1038:1646`, interface 3, FFC0/0001 and 642/65/65 report sizes. The separate wired-only `1642` model and other variants use generic diagnostics until validated. Card subtitles distinguish the transport.

A receiver and cable can coexist; opening a card selects its exact path and the default prefers USB. Repeated matching units of the same PID block advanced operations. Changing or losing the chosen path clears the loaded-state gate; read/activate again on the desired transport.

## Close other controllers

The app detects GG/Engine, Prism, OneRGB, OpenRGB and SignalRGB. Fully exit them before reading or editing. OneRGB's window close can leave it in the tray. OpenGG's CLI also refuses to run while OpenGG itself is open because it is a separate external controller.

Process detection is a convention, not a driver-exclusive lock. A script outside that list can still interfere. Avoid a second HID reader or writer while testing. Do not fix conflicts by disabling response matching or spraying repeated commands.

## Five-second command timeout

If temporary RGB is active, advanced commands need a serialized release before the feature/output report: `61`/`62` wirelessly and `21`/`22` over USB. This is already in the shared link. Check that your external implementation does the same and does not let RGB run between release and the following request.

An operation failure stops the remaining batch, drops the channel and invalidates the loaded-profile guard. Click Read and activate again before editing. The first error should include the command/failure detail; the app does not retry sixty keys and create sixty errors.

Distinguish a timeout from a nonzero response status, unsupported feature report, receiver present/keyboard radio disconnected or wrong report size. Export the actual diagnostic and journal. Do not increase retries around profile erase.

## 1.5 displays as 1.4

The owner observed native 0.2 mm menu steps and the same result through GG. OpenGG uses the confirmed 1.5 pair `28/31`. Leave it unchanged; a display label is not a calibrated depth measurement. See [profile format](profile-format.md).

## A change appears local but not stored after reload

Actuation/RT sends are live. Remap, Meta, dual, Tap and Protection edits are held in the local editing profile until an explicit Save to keyboard. Pressing load restores the stored slot. Saving compares/patches the file, backs it up and verifies receiver readback wirelessly or direct keyboard readback over USB.

The simulator can demonstrate behavior that has not been compiled for firmware. Protection reduction/activation conversions and the friendly macro compiler are specifically not claimed as native support. Flash rejects unsupported translations instead of silently claiming they worked.

## Profile changed outside OpenGG

Read again. The native save checks its baseline against the selected transport's current stored bytes before erase. The independent CLI can do the same with `--expected`. If a write partially fails, keep the error and backup path; decide restoration using the correct current baseline and complete transaction.

## Unknown keyboard feature read fails

Some models have no vendor feature collection or do not accept report ID zero. That is a useful diagnostic result. OpenGG does not try the Gen 3 configuration namespace blindly. The UI deadline is five seconds; an underlying synchronous driver call may remain pending, and further generic probes are blocked until it finishes.

## RGB/audio/OLED

RGB has no command ACK. Confirm the shared link, exclusive controller use, correct cable/receiver transport and actual key lighting. The UI surfaces send errors; a rendered preview alone is not physical validation. Audio Reactive uses the default Windows output loopback, so verify that audio is playing on that output. No microphone is required by this effect.

For the direct OLED preview, uncheck its control to send the verified `4B` wireless / `0B` USB background restore. Opening a connection does not send that restore as a mode command. Loading a stored profile restores its persistent configuration.

## Build/runtime

The source build needs .NET 10 SDK on Windows. A framework-dependent executable needs the .NET 10 Desktop Runtime; the default portable build includes it. Keep all portable files together. Local exceptions go to `%LOCALAPPDATA%\OpenGG\errors.log` and startup failures are shown explicitly.
