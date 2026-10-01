# Research and implementation log

This records the investigation that became OpenGG, from the supplied OneRGB handoff through the standalone desktop. It distinguishes historical hypotheses, actual hardware responses, code changes and remaining limits. The local archive also preserves the original six inputs, full captures, complete backups, native harness sources, result records and a point-in-time conversation transcript. Those originals are reference evidence, not extra user instructions.

## 1. Establish the target and the existing project

The objective was to control the owner's keyboard from open source software, including actuation, Rapid Trigger, Rapid Tap, Protection, dual actuation and onboard profiles. The working receiver was `1038:1644`; the project already had OneRGB's WPF device page, keyboard editor, arbitration pipeline, RGB transport and PSD-layer artwork.

The original inputs were `ONERGB_APEX_GEN3_REVERSE_ENGINEERING_HANDOFF.md`, `04_STEELSERIES_APEX_PRO_TKL_PROTOCOL.md`, `Capture-UsbTrace.ps1`, `teste_10s.json`, `capture.json` and `analise_protocolo.md`. [Sizes and hashes](input-manifest.json) identify exact copies. Historical handoff prose and earlier model protocols were treated as hypotheses until checked against complete transfers or this hardware.

The original OneRGB worktree contained other ongoing changes. The protocol work extended its existing interfaces and preserved that unrelated work. OpenGG was then extracted into a separate directory, with its own application, data directory and selected keyboard-only dependencies. Original source namespaces remain in the imported core to avoid a pointless rewrite.

## 2. Separate capture sources and their limitations

The short capture contained periodic RGB traffic, not enough magnetic-setting activity to infer an onboard protocol. The longer JSON had **6,297 records**, including complete 642-byte DeviceIoControl payloads. It also contained unrelated process writes; the large raw analysis was about 281,000 lines. Unfiltered records were not treated as a clean USB trace.

The available PCAPs were derived from these process-API outputs. They did not supply original device replies. The command ACKs subsequently used in the implementation came from the physical receiver, not from fabricated PCAP replies.

Windows ETW capture used UCX, USBXHCI and USBHUB3 providers, with rundown to associate endpoints and timestamps for each controlled change. On the investigated Windows 11 build 26200, UCX transfer-data events did not expose useful report payloads even with the requested full-data keywords. ETW was useful for topology/timing, not a substitute for missing bytes. The public helper retains this limitation and supports a dry run.

## 3. Correlate settings with reports

Compare changing all keys to 0.1, 1.0, 2.0 and 3.0 mm, then W alone at 2.0 mm. The `6F` feature command contained 68 HID usages with actuation/release pairs. Capture anchors included `4/4`, `16/18`, `45/49` and `112/122`. A per-key change affected W's entry while leaving the rest unchanged.

Earlier guesses of 0.025 or 0.04 raw units per millimeter did not explain the final nonlinear mapping. The exact modern-firmware table was recovered from installed vendor metadata and checked against captured anchors. Local vendor descriptor inspection was used as research reference; no decrypted vendor sources, executable images or keys are included in OpenGG.

Older Apex Pro TKL 2023 findings were useful comparison material but did not validate Gen 3 opcodes. The earlier global `2D` experiment was not used as the Gen 3 implementation. No arbitrary `00`–`FF` opcode scan was performed.

## 4. Reassemble the onboard files

Reports showed a length of **512**, a four-byte little-endian offset advancing by 512, and payload at byte 10. A bank therefore consisted of 24 blocks, 12,288 bytes, rather than the earlier 24×500/12,000 hypothesis.

The analyzer keyed transfers by opcode/namespace/file, started a new bank at offset zero, accepted only aligned complete blocks, ignored unrelated records and validated CRC after reconstruction. It recovered **18 complete banks: nine transactions, each with a keyboard copy and a receiver copy**. Every reconstructed CRC was valid.

The correct checksum was ordinary CRC-32 IEEE over bytes `[8:12280]`. STM32 CRC and word-swapping guesses were discarded. The final eight `FF` bytes lie outside the checksum. Complete-file differential comparisons identified name, mappings, thresholds, RT, Protection, Tap, dual-actuation, macros and OLED regions; the resulting [profile map](profile-format.md) documents all implemented offsets.

The key order also needed correction: W usage 26 maps to physical position 16. HID usages, the 100-position firmware profile, 70 analog positions, 68 live entries and 60 visible adjustable ANSI keys were kept as separate spaces.

## 5. Build a receiver reader before writing

Open the exact `MI_03 / FFC0:0001` vendor collection with 642-byte features and 65-byte interrupt reports. Send `83`, await a matching ACK, then use GetFeature. The reply starts `00 83 00`; its 512 data bytes are `[3:515]`, not a repeated outgoing ten-byte header.

Read all five slots, verify size/schema/CRC and save original files. A tunneled `C3` keyboard-copy read failed with error responses and was dropped. The receiver read remains the established readback path, with its narrower evidence boundary explicitly stated.

## 6. Validate the two-destination write

Before erase, back up the current profile. Write keyboard namespace 3 using `42` erase, 24 `43` blocks and `E6` validation; then receiver namespace 1 using `02` erase, 24 `03` blocks and `E6`. Wait for each matched reply. Read the receiver again and compare every byte.

A modified profile containing actuation, RT, Protection, Tap, remap, Meta and dual-actuation was accepted and exactly stored in receiver readback. Both Python/hidapi and the native C#/Win32 harness exercised the path. After these earlier destructive tests, all five slots were restored and compared with their **then-current initial backups**; slot 2 was reactivated.

This proves acceptance/storage, not every physical output action or power-cycle persistence. The two copies are not one atomic transaction. Failure injection verifies backup creation and stopping before additional writes; the implementation neither retries flash automatically nor silently restores an outdated backup.

## 7. Handle the effective global primary setting

An enabled global flag overrides the per-key pair array. Read and live seeding must use its 20-level table. When a primary actuation edit moves the profile to per-key mode, materialize the effective global pair across all 70 analog positions before changing selected keys and clearing the flag. Otherwise unselected keys would unexpectedly fall back to stale stored pairs.

Offline regression checks cover W's physical index, exact changed bytes, global materialization, unknown-field preservation and CRC rejection. Editing a different function leaves global mode intact.

## 8. Bring native support into OneRGB

The keyboard integration used the existing ControlArbiter, ControlService, ProfileManager and device editor rather than a competing HID transport. The editor gained Read and activate, onboard slot selection, backed-up explicit Save to keyboard and live actuation/RT. Live state was seeded from the slot actually loaded; UI defaults were not used to overwrite hidden keys.

OLED `4A` and background restore `4B` received success ACKs. The 128×40 vertical-page layout was cross-checked against OmniLED. Opening a channel stopped sending `4B` as a generic mode switch and stopped using reset `41` during cleanup. Temporary RGB is released through `62`.

Battery/connection requests `BC`/`D2` were retained as data queries, separate from commands with status bytes. Raw battery telemetry was not mistaken for a calibrated physical percentage.

## 9. Reproduce and fix the live-setting timeouts

The owner saw one five-second error per attempted key. Native experiments reproduced `6F`/`76`/`77` losing replies while `61` temporary RGB continued. Reading profile blocks could still return valid CRC, so a successful read alone did not exclude this interaction.

Releasing `62` before **feature as well as output** advanced commands restored the replies. Release, the minimum delay and the next command had to share the gate: otherwise another RGB frame could reactivate temporary mode in between.

On I/O, timeout or cancellation failure, the channel is dropped, the loaded-profile gate is invalidated and remaining commands for that resource stop. The UI receives a meaningful failure instead of retrying all selected keys and producing sixty toasts.

Frida-based observation helped isolate calls, but detach from OneRGB produced an access violation involving `frida-agent.dll`. Later checks used the isolated native harness and installed UI instead. The public diagnostics describe Frida as optional research tooling and the observed failure, not a default runtime dependency.

## 10. Resolve the 1.5 mm OLED question

The first success set 1.5 mm in software while the OLED displayed 1.4. The owner then checked the native wheel menu and GG: the menu advanced in 0.2 mm steps, and GG setting 1.5 also displayed 1.4. The interpretation changed from a possible off-by-one encoding bug to an observed software/display precision difference.

Keep the confirmed pair **28/31** for 1.5. Do not apply a speculative +0.1 mm correction. Document the owner observation and that physical switch-depth measurement was not performed.

The owner subsequently saved 1.5 mm in their current slot 2. Its current hash differs from the earlier initial backup. Later speed tests restore this **current** slot, not the historical all-1.0 configuration.

## 11. Remove the per-key throughput cost

The older native loop sent a full 68-entry report separately for each of 60 selected keys and took **8.5410694 s**. Replace it with validated grouping in the shared pipeline:

1. Validate all values, availability, group, writer and resource before I/O.
2. Acquire the involved control gates in a stable order, check versions and acquire one resource lease.
3. Journal every requested setting before sending.
4. Clone the loaded live map and apply every selected change to the clone.
5. Keep only the final report per opcode, ordered `6F`, `76`, `77`; omit `77` while RT is off.
6. Send the batch under one HID gate, with the RGB release rule.
7. Commit state and Applied values only after the whole batch succeeds. If it fails, report one failure and cancel its remaining entries.

After explicit onboard load, clear the cached Applied values so a request is not incorrectly skipped merely because an older local request had the same value. Tests cover one batch call, validation before writes, journal ordering, failure stopping and preserving nonselected keys.

The native batch test under continuous RGB took **0.1443179 s**, with one `6F` report and the stored profile unchanged. The installed OneRGB UI also applied/restored all selected keys. OneRGB's Release desktop/service packages were published and the installed binaries updated as required by that project's workflow. See [verification](verification.md).

## 12. Extract OpenGG

The chosen directory was `OpenGG`, with English documentation and a visual README based on the owner's design. Import the existing device card, keyboard page, Apex RGB card, design tokens, icons and the owner's seven PNG preview layers. Exclude the rest of OneRGB's hardware catalog, service, broad automation runtime and unrelated device artwork.

Use two small projects: `OpenGG.Core` for imported protocol/control code and `OpenGG.Desktop` for WPF. Preserve imported core namespaces for traceability; the desktop has its own OpenGG namespace. Add an independent `%LOCALAPPDATA%\OpenGG` store for profiles, settings, journal, errors and backups.

General discovery filters vendor 1038 and requires a standard keyboard collection. Windows Container ID groups composite interfaces and distinguishes identical connected units. A PID-only fallback is documented as a limited fallback when that property is unavailable. Strict report sizes and exact receiver identity select the detailed Apex UI; other models get generic diagnostics. Two matching receivers block advanced selection.

During the port, resource pruning initially omitted `PageScroll`, causing a startup exception. Restore the imported style and ensure a fatal startup error is surfaced rather than leaving a blank process. Combining native and audio code in one assembly also required retaining runtime marshalling for the imported WASAPI COM interfaces; native HID LibraryImport stubs continue to work.

Process-owner checks were initially repeated through each control's availability property, and RGB generation ran while the editor was closed. This made the port unnecessarily slow. Cache the process inventory at scan time, stop RGB generation outside the active editor and preserve the existing shared channel. Native WASAPI initialization was subsequently checked successfully with a nonzero audio level.

## 13. Finish the requested standalone interface

The user's final interface revision added the original sidebar with only **Devices** and **Diagnostics**. Keep the existing RGB control card in the Apex detail and the user-created image layers. Preserve keyboard settings and their existing simulator boundaries rather than presenting those simulation fields as verified firmware fields.

Diagnostics adds the known connection/battery queries, profile CRC/SHA, an opt-in current-live-map ACK test, bounded generic feature reads, export and a program/script inventory. Include native HID inventory, CLI, capture reconstruction, ETW helper and external Wireshark/USBPcap/Frida/USBView references. Inventory detection is PATH/registry evidence, not a claim that every driver or Python module works.

The OpenGG UI accepted a 60-key 1.6 mm batch in approximately **0.19 s** recorded duration and restored 1.5 in approximately **0.18 s**. One UI harness missed its success label after the visible page changed to Diagnostics; the actual 60 Applied journal entries proved the completed batch. Restore was checked from the journal, not inferred from a missing toast. The dedicated diagnostics also returned connection data, battery data, valid profile CRC and live `6F`/`76` ACKs.

Maintain both human-readable docs and executable byte tables. Archive the untouched inputs privately. Package the native desktop with its runtime and the useful scripts. Publish source-ready files locally; creating the folder and package does not by itself create or publish a GitHub repository.

The final recovery check found that a failure initiated by RGB or diagnostics could close the shared transport while leaving the keyboard integration's loaded-profile guard set. Connect that failure notification to loaded-state invalidation, distinguish it from normal RGB release, and close the channel when another controller is detected. Route imported battery reads through the shared channel as well. Three offline checks cover normal release, explicit drop and failed discovery; the final core run passes 80 checks. A fresh hardware batch still takes about 0.15 s under continuous RGB, preserves current flash and reloads slot 2 in `finally`.

## 14. Complete the usability port and offline tool actions

The owner's visual review identified parts of the OneRGB shell missing from the first port: the black selection contour, slide transitions, stable icon size, integrated window controls and the scan animation. They also requested an RGB-first editor, less repeated text and actual local research installers.

Reuse the original selected-row geometry and animation helpers. Keep the navigation icon at 17 pixels inside the same 34-pixel surface in both layouts. Animate sidebar width from 224 to 76 pixels over 280 ms, fade the fixed-width brand/version text, and use a transition counter so an interrupted animation finishes in the latest state. Respect Windows' animation setting. Replace the normal title bar with WindowChrome and existing caption styles. Adapt the original monitor/resize helper; add a native rounded region so the window itself excludes the black rectangle at each corner. Reset the region when maximized, account for taskbar work area and refresh it after a DPI change.

Remove the device name filter field. A manual scan exposes an IsScanning state for at least 400 ms: the refresh glyph spins and the button turns gray; polling remains quiet. Reuse the original 144-pixel wheel handler and actually install it at startup. The first port had retained two nested page ScrollViewers inside an outer viewer; detach their contents and keep the UserControls with one shared outer scroll. Wheel events over the RGB card then advance that page rather than being trapped in a card.

Move the existing RGB page ahead of keyboard settings, drop the repeated device hero, put status in the header and use the original back-arrow style. Rename the second RGB card to Lighting control. Make the effect card a direct responsive-grid child so it stretches to the combined left-column height. Remove the per-profile footer, empty single-key help card and requested RT descriptions. Move Protection Mode into an expander inside actuation/RT; keep its existing controls and preserve the distinction between simulated and firmware fields. Two RGB expert descriptions retained broken ancestor bindings after extraction; remove those obsolete hidden-mode descriptions instead of adding a new expert mode.

Prepare real offline payloads for Python, Frida and dependencies, Wireshark, USBPcap/source/symbols and the Windows SDK Debugging Tools layout that supplies USBView. The first Microsoft redirect returned a download page rather than an executable; Authenticode validation rejected it before execution. Resolve the documented current SDK installer and produce a download-only layout. The complete cache contains 66 files, approximately 435 MiB, with hashes and original URLs. Native HID inventory and ETW capture already use the shipped program and Windows scripts, so they need an Open folder action rather than a fabricated installer.

The Install/Prepare helper whitelists tool IDs, confines paths to the cache and checks every relevant hash before extraction/execution. Python and Frida install into the writable OpenGG folder using offline wheels; Windows program installers remain interactive. The UI runs the helper asynchronously and records its complete output/exit code locally. Nine categories expose action and documentation buttons. A source checkout fetches the cache; the default portable build includes it, while an explicit minimal build omits it. Generated Python runtimes and raw research remain excluded from the package/source.

Validate the actual WPF window with 23 checks, including intermediate animation widths, reversed animation completion, unchanged icon dimensions, native region corners, scan spin state, all tool-action bindings, the RGB height comparison, a routed wheel event over a card and exact maximized work-area bounds. Seven installer plans pass under Windows PowerShell 5.1; fake caches prove that corrupted hashes and traversal paths are rejected. Python, Frida, pip requirements and the CLI run from the contained runtime. No system driver/SDK installation or additional flash transaction is required for these UI changes.

The full GUI installer test caught two compatibility details missed by dry runs. A PowerShell 7 parent can pass module paths that hide Windows PowerShell's Get-FileHash; clear that child's PSModulePath and use the system PowerShell executable. A one-item array returned through an if expression can become a scalar, so type pip argument lists as string arrays before splatting. Also register the included tools directory in embedded Python's path configuration so the analyzer/test scripts can import their sibling protocol module. Re-run real local preparation for Frida, the single-package CLI path and the analyzer, then exercise Frida installation through the portable GUI. Its log confirms successful offline installation; the freshly installed Frida reports 17.19.0. The final ordinary keyboard diagnostic returns valid CRC with the same current slot-2 SHA256, without activation or save.

## 15. Simplify the editor and integrate OLED into the artwork

The next visual review requested removal of the boxed helper paragraphs and the detail diagnostic button, a wider rainbow, transparent RGB dimming, integrated OLED image editing, vertical key sliders and one card for Presets/Rapid Tap/Macros. Treat the supplied screenshots as layout feedback; they do not change the hardware authorization or protocol tables.

Compare both implementations before editing. OneRGB's ApexLightingViewModel returns no spatial frame for its artwork, selecting the original procedural gradient. The first OpenGG port supplied a brightness-scaled hardware frame instead. ApexKeyboard sampled every few **key-order** entries and distributed them across the screen's X axis, so multiple keyboard rows produced narrow/repeated color bands. Because the hardware frame was already dimmed, the first port applied it at opacity one; low RGB values covered the gray base with an opaque dark layer. Remove that obsolete frame/gradient path, reuse the original continuous curated rainbow and apply brightness through opacity. Breathing and audio intensity also fade opacity. Leave the hardware generator's brightness scaling intact.

Expose the current OLED bitmap as an ApexKeyboard drawing property and fit it inside the artwork's original display rectangle. Remove placeholder OpenGG lettering and the separate Preview card. Color/OLED radios switch the color controls and OLED editor; effect, brightness and speed stay outside that switch. Use the existing Motion helper for a 360 ms camera transition toward the display, with snapshot-and-replace interruption and reduced-motion handling.

Reuse the existing OledFrame monochrome conversion and packing instead of adding an image package. WPF decodes a bounded file and the first frame/page, fits it to 128×40 without distortion and composites it over black. Convert to luminance and Floyd–Steinberg dithering; preview and transport use the same resulting frame. Open File enables the existing direct `4A` send. Updates debounce for 200 ms and coalesce while sending. Disabling sends `4B` after any already-pending send. Preserve stored OLED bytes in ordinary profile edits; static session overlays and persistent CLI bitmap edits remain distinct.

Move profile controls into the map header. Reuse the existing vertical Fader style for actuation, RT and Protection. The lightning/shield buttons choose independent paint tools rather than global switches. Key clicks toggle only that feature; RT is orange, Protection blue and both use a split background. Entering Protection from disabled state starts with the clicked key instead of enabling its inherited WASD defaults. Preserve the remap/Meta/dual editor in an expandable section for one selected key. Place Presets, Rapid Tap and Macros behind three tabs in one card, preserving their editors and local macro limitations.

Selection changes seed slider values under a synchronization guard. This avoids turning a programmatic value load into a group edit after making slider changes auto-apply. Only explicit slider edits enter the existing live-commit debounce. Actuation remains available by default; the other sliders activate for their paint mode or a matching selected key. Protection uses its established per-key byte at `12156 + physicalIndex`, not the simulator's unverified millimeter reduction. Extend the existing configuration/control serialization, clone, validation, profile read and minimal patch to carry that integer byte; saving uses the unchanged backed-up transaction. No raw-sensitivity live opcode or mm conversion is guessed.

Real-window screenshots exposed a radio-button accessibility issue: SelectionItem automation could mark a command-only radio without switching its model, showing a Color label above OLED content. Bind panel/tab/mode selection two-way and teach the existing equality converter to return the typed enum for lighting modes. Validate through the WPF selection provider, rather than assuming mouse command execution covers assistive technology.

Run 84 offline core checks, including exact Protection byte/mask/CRC changes, preservation of unrelated sections, local profile round-trip and rejection of fractional bytes. Run 49 real WPF checks covering painting/removal, selection seeding, accessible panels/effects/tabs, OLED zoom reversal, PNG/GIF conversion, the 640-byte `4A` payload and actual 25%-opacity rendering. The All-selection screenshot also exposed Esc, a non-analog key, seeding actuation with the default while the analog keys had different values. Seed actuation from the first supported selected key, and each sensitivity from its matching selected feature; cover that case explicitly. Restore local settings/profile files after the UI check; do not load or flash a profile. Inspect the final portable screenshots and run the ordinary keyboard diagnostic: ACK zero, valid schema/CRC and unchanged current stored slot-2 SHA256. Update the English visual guide, source provenance, verification metadata and self-contained package.


## 16. English interface, native battery and owner branding

Finish the owner's next usability pass without changing the shared protocol or saved keyboard settings:

1. **Translate the real interface paths.** Convert labels, menus, tooltips, validation/errors, preset/source descriptions and generated OLED status text to English. Set English UI/numeric culture so sliders use decimal points consistently. Keep user-authored profile names, macro steps, setting IDs and native key/action tokens unchanged. Audit both XAML and C# strings; English documentation alone does not translate runtime feedback.
2. **Restore real battery state.** DeviceCardViewModel had hardcoded battery availability/percentage/charging values, despite the existing native query being functional. Replace those placeholders with nullable state and coherent notifications. Poll the exact verified receiver every second with a nonoverlap guard, reusing ControlService and the integration's one-second cached response for percent/charge. Query through the existing serialized channel and ownership guard; clear stale values on disconnect, failure or a blocked controller. Reuse BatteryBar's charge shimmer, full-charge stop, unload cleanup and reduced-motion behavior. The inherited quantized percentage mapping remains an estimate rather than a new calibration claim.
3. **Remove the OLED camera's inner clipping limit.** The old Viewbox and inner fixed-size preview border constrained the enlarged keyboard to a rectangle inside the card. Let ApexKeyboard fill the entire stage. Move resting margins into its camera transform, calculate the close view against the full stage and retain only the rounded outer clip. The OLED bitmap still uses the same artwork coordinates and monochrome transport.
4. **Correct the vertical fader at its shared template.** Bind Track.IsDirectionReversed to the Slider and align its filled segment with the bottom minimum. Keep icons below. Move the lightning/shield buttons above their respective right-side columns; put WASD/All/Clear on the left. A local accent value would override the disabled trigger, so feature accent belongs in the style. Inactive button, bar and bottom icon are gray.
5. **Share individual and bulk feature painting.** Use one PaintKeys path for clicks and WASD/All/Clear. Enable only supported keys, derive the RT master from actual flags and preserve the other feature. Clear removes the active feature from all keys. Entering disabled Protection starts with an empty selection, preserving the earlier removal of implicit WASD behavior. One bulk edit enters the existing debounce once; no per-key transport loop returns. Color the instruction with the active paint tool.
6. **Preserve space through panel transitions.** Reuse native motion timing with a small PanelActive helper. Hidden panels retain measured height rather than collapsing. Fade/translate in place and replace animations from their current values when interrupted. Switching from populated Presets to empty Rapid Tap/Macros and back must retain the page extent and bottom scroll offset. Apply the same behavior to Color/OLED; honor reduced motion.
7. **Refine the existing shell and status card.** Increase top clearance for Scan/Connected. This initial compact-sidebar pass removed the selection contour; stage 17 restores it after the owner clarified that only the unselected hover pill should disappear. Keep the lighting status card's size, remove its title and arrange battery, OpenGG RGB switch/Sync and a reserved concise feedback area. Observe existing keyboard/editor state for access, profile-read requirements, pending changes, progress and communication errors; keep full detail in tooltips.
8. **Use the owner's four SVG logos.** Preserve the original Typography, Isotype, Full Logo and Secondary Logo SVG files. Their source paths use black/default fills; render white UI derivatives without modifying the originals. Build-BrandAssets.ps1 uses native WPF geometry and bitmap rendering to export white transparent typography, light/dark README PNGs and a seven-size ICO (16–256). Set the executable/window icon and sidebar image resources. Mixing the new Image brand with the version TextBlock initially prevented C# array inference; use the existing FrameworkElement base type for the fade loop. No SVG/image library is added.
9. **Expand the public entry point.** Introduce the owner's complete closed-source OneRGB application and the deliberately public OpenGG keyboard section. Add real screenshots, feature/support comparisons, startup/build examples, protocol/doc links, battery/OLED notes, diagnostics/tool installation and contribution questions. Use descriptive search phrases such as SteelSeries GG alternative, Apex Pro TKL Gen 3 actuation and Rapid Trigger naturally. Remove the earlier README-reference attribution; design and artwork are the owner's. Earlier public OpenRGB Gen 3 RGB and OmniLED OLED work prevent a blanket first-ever protocol claim; distinguish this repository's original advanced configuration research with direct primary links.
10. **Verify the outcome and preserve current state.** Pass 84 offline core checks and 73 actual desktop checks, including a live receiver battery reply, detached battery/charging state tests, gray disabled controls, bottom-up fills, safe bulk painting, full-stage bounds, stable panel extent/scroll, actual brand resources and previous OLED/RGB checks. The UI harness restores local settings/profile files and does not load or flash a profile. Capture the actual portable window, run the ordinary read-only diagnostic, compare current slot-2 SHA256, refresh import destination hashes and validate local documentation links. Package the four original SVGs and all prepared offline tool payloads with the self-contained app.

The exact final check list and diagnostic checkpoint are retained in [machine-readable verification](hardware-verification.json). Historical stages above remain dated evidence; their earlier check counts do not replace the final validation.

## 17. Compact selection restored, USB cable decoded and OneRGB updated

1. **Correct the exact sidebar behavior.** Preserve the selected white circle and moving black contour in both sidebar widths. Hide only the unselected compact hover pill. Animate that icon to 112% on enter and 100% on leave, and translate the collapse toggle by −8 px so its center matches the navigation icons at x = 36 px. Add four native checks to the existing desktop harness; all 77 pass.
2. **Identify the actual cable channel.** Read current HID inventory with both receiver `1644` and cable `1646` present. The wireless keyboard's cable interface is MI_03, FFC0:0001, with 642/65/65 reports and a separate Container ID. Do not authorize the similarly named wired-only `1642` model. Reject repeated units of either tested PID rather than silently choosing one.
3. **Probe stored data before changing it.** Query direct battery `92`. The receiver-style `83` namespace 1/file 12 fails with status 5 on USB. Namespace 3/file 2 succeeds; reconstruct 24 blocks, validate schema 19/CRC and compare the current SHA256 `3F06EC3E9BE7B7CA005413FDB01B18D08F6FD3F533F700D8E35E7E162D45433B`.
4. **Validate direct commands.** Release RGB with `22`, then obtain ACK zero for validation `A6`, activation `13`, persistent settings `28`, actuation `2F` and RT `36`/`37`. Temporarily test W's RT and reload current slot 2 in `finally`. Route only these known command families; keep the file opcode `83` unchanged.
5. **Reuse the shared native and CLI transport.** Choose cable by default, or pin the interface explicitly from its device card / CLI `--pid`. Select direct namespace-3 storage for USB and keep the receiver's two-destination transaction intact. Match replies against the routed opcode. A changed/disconnected path invalidates the loaded map. Keep telemetry, RGB, OLED, live changes and flash on the same gate.
6. **Measure and verify on hardware.** The public native harness sends one 68-entry `2F` report for 60 selected keys during continuous RGB in 0.144309 s, with the stored profile unchanged and slot 2 reloaded. Back up current slot 5, re-write the same bytes using `02`, 24 `03` blocks and `A6`, then read the keyboard bank back exactly. Slot 5 SHA256 remains `81F209455CC3E0BA867B7F50B7EC7529B30419EB903BEC82C84F297D82027160`; slot 2 is unchanged. A temporary blank `0A` OLED frame and `0B` background restoration also receive ACK zero. No historical backup replaces the owner's current configuration.
7. **Port the updated Apex section back to OneRGB.** Retain snapshots of every replaced source file. Adapt the shared protocol/profile/native integration, key map, OLED importer and RGB page to OneRGB namespaces. Reuse its original canvas and single hardware sink through six Apex effect samplers; update only the keyboard placement's effect/brightness. Opening the page reads its saved Apex preferences without rewriting the lighting scene. Remove only the Apex duplicate hero and inner page scroll viewers; retain other device flows and the complete OneRGB shell. Add OpenGG to the existing process-owner detector so the two applications do not compete for Apex resources.
8. **Check the backport and package.** OneRGB passes 1,304 unit tests and 13 targeted native-window Apex checks; source compilation has zero warnings/errors. OpenGG passes 90 core checks, 77 desktop checks and four Python tests. Preserve the original extraction hashes and record the authorized backport separately. Publish/install OneRGB through its repository script, then refresh real OpenGG screenshots, wired diagnostics, documentation links and the portable package.

USB flash acceptance and byte readback establish the direct keyboard storage path. They do not measure physical actuation depth, test a power cycle or prove receiver-cache reconciliation after a connection change. The 1.5 mm setting / 1.4 mm OLED observation remains documented without a compensating offset.

Publication completed for both OneRGB Desktop and Service. The Windows UAC installation was canceled, so `C:\Program Files\OneRGB` and its client authorization metadata were not updated. The new application is available at `OneRGB/artifacts/app/OneRGB.exe`; running the repository installer completes the system update. The previously installed service remains running.
