# Apex Pro TKL Wireless Gen 3 protocol

This specification records two tested transports for the **Apex Pro TKL Wireless Gen 3**: receiver **VID `1038`, PID `1644`** and the same wireless keyboard connected by **USB cable, PID `1646`**. Both use interface 3, usage page `FFC0`, usage `0001`, feature length 642 and input/output length 65. Keyboard firmware is 3.24.1 and profile schema 19. The separate wired-only `1642` model, Bluetooth and other identities remain unvalidated.

## Windows reports and replies

Windows buffers include report ID `00`. Pad feature buffers to **642 bytes** and output buffers to **65 bytes**. C# interrupt reads include ID zero: `[00, opcode, status/data, ...]`. The hidapi Python interrupt reply omits that zero: `[opcode, status/data, ...]`. Feature reads retain report ID zero.

For configuration/file commands, status zero means accepted. Arm the pending response before sending; match the opcode, ignore unrelated reports and stop on a nonzero status or timeout. Five seconds allows slower erase operations. The minimum configuration start interval is **31 ms**, subtracting time already spent waiting for the reply. This is exposed as `ApexHidLink.ProfileReportInterval` and CLI `--delay-ms`; it is not an assumed universal radio timing constant.

Telemetry differs: in replies to `BC`, `D2` and USB `92`, byte 2 is **data**, not a success status. `00 BC 01` says the keyboard is connected to the receiver. Battery bytes are retained raw; the inherited percentage scale has not been independently calibrated.

## USB cable commands and direct storage

USB is a separate transport with a distinct Windows Container ID and PID. Sending the receiver's command bytes to that channel caused the cable failure. The tested direct commands are:

| Operation | Receiver `1644` | USB `1646` |
|---|---|---|
| Temporary RGB / release | `61` / `62` | `21` / `22` |
| Actuation | `6F` | `2F` |
| Rapid Trigger enable / sensitivity | `76` / `77` | `36` / `37` |
| OLED overlay / reset | `4A` / `4B` | `0A` / `0B` |
| Activate / use persistent settings | `53` / `68` | `13` / `28` |
| Validate profile | `E6` | `A6` |
| Battery | `D2` after `BC` connection | Direct `92`; no receiver `BC` query |
| Read profile | `83`, namespace 1, file `10 + slot` | `83`, namespace 3, file `slot` |
| Erase / write direct keyboard copy | Tunnel `42` / `43`, namespace 3 | Direct `02` / `03`, namespace 3 |

Only the explicit validated command families are translated. Clearing bit `40` on every opcode would corrupt commands such as `83`, which retains its opcode. Slot indices remain 1–5 for file operations and 0–4 for activation/validation. Protection, Rapid Tap and action mappings use the verified profile fields; their standalone USB live opcodes were not guessed.

On the real cable, `83` namespace 1/file 12 returned status 5. Namespace 3/file 2 returned all 24 valid blocks and the same current profile SHA256 as the receiver. A backed-up USB save therefore reads namespace 3, checks the baseline, erases with `02`, writes 24 `03` blocks, validates with `A6` and compares all 12,288 bytes from the keyboard. It does not rewrite the receiver cache.

The USB transaction was tested by re-writing slot 5 with its existing bytes, then checking exact keyboard readback and unchanged slot 2. The separate live test sent `2F` under continuous `21` RGB in 0.144309 s and reloaded the stored slot in `finally`. Temporary `0A` OLED sending and `0B` background restoration both returned ACK zero. See [verification](verification.md) for boundaries and hashes.

## Shared channel and temporary RGB

`61` submits temporary per-key RGB without ACK. `62` releases that mode, also without ACK. While temporary RGB is active, advanced commands including feature reports `6F`, `76` and `77` can receive no reply even though profile reading succeeds.

Before an advanced command: send `00 62`, wait the minimum interval and send the command under the **same channel gate**. Hold the gate over every report in a live batch or an entire profile transaction. A background RGB frame must not run between release and the command. RGB can resume afterward.

This behavior was reproduced with continuous RGB and fixed by releasing `62` before feature commands. `41` is a reset and is not used for cleanup. `4B` restores the OLED background and is not a generic software-mode command; it is not sent when opening the connection.

## Live settings

All numbers below are hexadecimal opcodes. Formats include report ID zero.

| Opcode | Layout | Transport and meaning |
|---|---|---|
| `6F` | `00 6F layer count [usage,A,B]...` | Feature + ACK; layer 0 primary, layer 1 secondary |
| `76` | `00 76 count [usage,enabled]...` | Feature + ACK; per-key Rapid Trigger enable |
| `77` | `00 77 count [usage,sensitivity]...` | Feature + ACK; sensitivity in tenths of mm |
| `54` | `00 54 count [usage,enabled]...` | Feature + ACK; Protection selection |
| `57` | `00 57 enabled` | Output + ACK; Rapid **Tap**, not Trigger |
| `58` | `00 58 count [index,hid1,hid2,modeAndFlag,0,0]...` | Output + ACK; Rapid Tap pairs |
| `61` | `00 61 count [usage,R,G,B]...` | Feature; temporary RGB, no ACK |
| `62` | `00 62` | Output; release temporary RGB, no ACK |
| `4A` | `00 4A bitmap640` | Feature + ACK; direct OLED image |
| `4B` | `00 4B` | Feature + ACK; restore OLED background |
| `4C` | `00 4C 01 offsetLE16 lengthLE16 data...` | Identified persistent/default OLED path, eight 80-byte blocks; not a desktop default |
| `BC` | `00 BC` | Output query; matching connection-data reply |
| `D2` | `00 D2` | Output query; matching battery-data reply |

The live analog list contains **68 HID usages**. The visible ANSI layout has **87 keys, 60 actuation-adjustable**; the profile has **70 physical analog positions**. These counts describe different coordinate spaces. Do not substitute one order for another. [Exact data](protocol-data.json).

`6F` encodes all 68 entries. At 1.5 mm the threshold pair is **28/31 decimal**, `1C/1F` hex. The software clones its loaded state, applies all selected changes, then emits only the final report per family: at most one `6F`, one `76`, one `77`. `77` is omitted while master RT is off; its pending sensitivity remains in the state for the next enable. The original live state commits only after every required ACK succeeds.

The 0.1–4.0 mm lookup is nonlinear. Use the exact 40 pairs in [profile format](profile-format.md), not an interpolation or a fixed raw-units/mm ratio.

## Profile file reports

| Bytes | Type | Meaning |
|---|---|---|
| 0 | byte | Report ID `00` |
| 1 | byte | Opcode |
| 2 | byte | Namespace |
| 3 | byte | File ID |
| 4–5 | uint16 LE | Block length, **512** |
| 6–9 | uint32 LE | Byte offset: `0,512,...,11776` |
| 10–521 | 512 bytes | Write payload |
| 522–641 | padding | Zero |

An entire transfer is **24 × 512 = 12,288 bytes**. User slots are 1–5; activation/validation use `slot - 1`.

| Operation | Report | Channel |
|---|---|---|
| Read receiver block | `00 83 01 (10+slot) 00 02 offsetLE32` | Feature, ACK, GetFeature |
| Erase keyboard slot | `00 42 03 slot` | Output + ACK |
| Write keyboard block | `00 43 03 slot 00 02 offsetLE32 data512` | Feature + ACK per block |
| Validate | `00 E6 (slot-1)` | Output + ACK |
| Erase receiver copy | `00 02 01 (10+slot)` | Output + ACK |
| Write receiver block | `00 03 01 (10+slot) 00 02 offsetLE32 data512` | Feature + ACK per block |
| Activate slot | `00 53 (slot-1)` then `00 68 00` | Output + ACK for each |

The receiver feature-read reply is **`00 83 00 data512 ...`**. Extract bytes **`[3:515]`**. The response does not reuse the outgoing file-header layout; skipping another 12 bytes corrupts the reconstruction.

## Backed-up flash transaction

```mermaid
sequenceDiagram
    participant A as OpenGG
    participant K as Keyboard
    participant R as Receiver
    A->>R: Read 24 blocks; validate CRC; save backup
    A->>K: 42 erase, namespace 3 / slot
    K-->>A: Matching ACK
    loop 24 blocks
        A->>K: 43 + 512 bytes
        K-->>A: Matching ACK
    end
    A->>K: E6 slot-1
    K-->>A: Matching ACK
    A->>R: 02 erase, namespace 1 / 10+slot
    R-->>A: Matching ACK
    loop 24 blocks
        A->>R: 03 + 512 bytes
        R-->>A: Matching ACK
    end
    A->>R: E6 slot-1
    R-->>A: Matching ACK
    A->>R: 83 read; compare all 12288 bytes
```

The native editor first checks the current stored bytes against its read baseline. Edits patch a copy and preserve all untouched fields. Before any erase, save the current original binary. If any operation fails, stop and report the backup path and the possibility of an incomplete write. No automatic flash retry or unrequested rollback runs.

The two wireless destinations are **not atomic**. Receiver readback verifies the receiver's stored copy; keyboard acceptance is supported by its block/validation ACKs. The attempted `C3` tunneled keyboard read was rejected and is not used. Power-cycle retention was not established. Independent keyboard-flash readback is now available over the tested USB namespace-3 path above; this does not establish receiver-cache synchronization.

## OLED pixels

The 128×40 image uses five vertical pages, 128 bytes each:

```python
index = (y // 8) * 128 + x
mask = 1 << (y % 8)
```

OpenGG's preview buffer is row-major, MSB-first; `ApexProtocol.OledFrame` converts it to this vertical LSB-first layout. The profile OLED field uses the same 640-byte payload.

The native OLED actuation menu is coarser than the software's 0.1 mm setting. The owner saw 1.5 display as 1.4 in both GG and OpenGG. This is a display observation, not a measurement proving which physical depth triggers the switch. No compensating +0.1 mm offset is applied.
