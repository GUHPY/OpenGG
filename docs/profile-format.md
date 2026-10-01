# Schema 19 profile format

A profile is 12,288 bytes including an eight-byte header and eight final padding bytes. All offsets below are absolute from the first byte. Numeric fields use little-endian unless specified otherwise.

The same schema-19 format was read from receiver `1644` (namespace 1, file `10 + slot`) and the wireless keyboard connected by USB `1646` (namespace 3, file `slot`). USB readback verifies the physical keyboard bank directly. Their cache synchronization after a transport change has not been established; [transport and write operations](protocol.md) keep the destinations explicit.

## Fields

| Offset | Size | Field |
|---:|---:|---|
| 0 | 4 | CRC-32 IEEE, uint32 LE |
| 4 | 4 | Schema, uint32 LE = 19 |
| 8 | 1 | Physical Meta key index |
| 9 | 1 | Keyboard profile index |
| 10 | 22 | Up to 21 printable ASCII name bytes and zero terminator |
| 32 | 16 | GUID |
| 48 | 100 × 5 | Primary action mappings |
| 548 | 100 × 5 | Meta action mappings |
| 1048 | 70 × 5 | Secondary-actuation action mappings |
| 1398 | 9,600 | Native macro container |
| 10998 | 70 × 2 | Primary actuation/release pairs |
| 11138 | 70 × 2 | Secondary-actuation pairs |
| 11278 | 1 | Global-primary-sensitivity flag |
| 11279 | 1 | Global-primary level, 1–20 |
| 11280 | 13 | Secondary-action bitmask |
| 11293 | 8 | Physical dual-actuation indexes; unused entries `FF` |
| 11301 | 13 | Consume-first bitmask: 1 replaces primary, 0 retains it |
| 11314 | 96 | Meta lighting data |
| 11410 | 4 | Win/Caps/Num/Scroll indexes |
| 11414 | 1 | Polling-rate setting byte |
| 11415 | 640 | OLED vertical page bitmap |
| 12055 | 13 | Rapid Trigger bitmask |
| 12068 | 70 | Per-key RT sensitivity, tenths of mm |
| 12138 | 13 | Protection bitmask |
| 12151 | 1 | Padding |
| 12152 | 4 | Protection duration, uint32 LE milliseconds; GG default observed 500 |
| 12156 | 70 | Raw per-key Protection sensitivity; GG default observed 20 |
| 12226 | 10 × 5 | Rapid Tap pair entries |
| 12276 | 1 | Rapid Tap enabled |
| 12277 | 3 | Padding/alignment |
| 12280 | 8 | `FF` padding excluded from CRC |

Preserve the name/GUID/macros/OLED, unshown keys and unrecognized fields unless an edit explicitly targets them. Profile validity is checked before any hardware write. Schema 19 is required; a different schema is refused.

## CRC

```python
import struct, zlib
stored = struct.unpack_from('<I', blob, 0)[0]
computed = zlib.crc32(blob[8:12280])
assert len(blob) == 12288
assert struct.unpack_from('<I', blob, 4)[0] == 19
assert stored == computed
```

This is ordinary CRC-32 IEEE: reflected polynomial `EDB88320`, initial state `FFFFFFFF`, final XOR `FFFFFFFF`. Do not include schema/CRC header bytes or the last eight padding bytes. STM32 word swapping was an earlier hypothesis and did not match the recovered complete banks.

## Actuation lookup

This exact 40-entry table describes 0.1–4.0 mm in 0.1 mm increments for the tested modern firmware. Values are decimal **actuation/release** pairs. They are not a linear distance conversion.

| mm | A/B | mm | A/B | mm | A/B | mm | A/B |
|---:|---|---:|---|---:|---|---:|---|
| 0.1 | 4/4 | 1.1 | 18/20 | 2.1 | 49/54 | 3.1 | 122/134 |
| 0.2 | 3/4 | 1.2 | 20/22 | 2.2 | 54/59 | 3.2 | 134/147 |
| 0.3 | 4/5 | 1.3 | 22/25 | 2.3 | 59/65 | 3.3 | 147/162 |
| 0.4 | 5/7 | 1.4 | 25/28 | 2.4 | 65/71 | 3.4 | 162/177 |
| 0.5 | 7/8 | **1.5** | **28/31** | 2.5 | 71/78 | 3.5 | 177/186 |
| 0.6 | 8/10 | 1.6 | 31/34 | 2.6 | 78/85 | 3.6 | 186/196 |
| 0.7 | 10/12 | 1.7 | 34/38 | 2.7 | 85/93 | 3.7 | 196/206 |
| 0.8 | 12/14 | 1.8 | 38/41 | 2.8 | 93/102 | 3.8 | 206/216 |
| 0.9 | 14/16 | 1.9 | 41/45 | 2.9 | 102/112 | 3.9 | 208/216 |
| 1.0 | 16/18 | 2.0 | 45/49 | 3.0 | 112/122 | 4.0 | 210/216 |

Both bytes matter when decoding: raw actuation value 4 occurs at more than one distance. The pair distinguishes them. The CLI rejects NaN, infinity, booleans, out-of-range values and fractional values outside 0.1 mm steps.

## Global mode

When byte 11278 is nonzero, byte 11279 chooses a **1–20 index** into this separate table, overriding stored primary per-key pairs:

```text
4/4 3/4 5/7 8/10 12/14 16/18 20/22 25/28 31/34 38/41
45/49 54/59 65/71 78/85 93/102 112/122 134/147 162/162 196/206 210/216
```

Level 11 means 2.0 mm. The byte is not `mm × 10`. To edit one primary key in global mode, first materialize the effective global pair into **all 70 physical positions**, then patch the selected position and clear the global flag. This preserves keys outside the visible ANSI layout. Edits to other fields retain global mode unchanged.

## HID usage versus physical index

The profile has 100 firmware positions, including 70 analog, 23 digital and seven special positions. Its physical order differs from the live report's 68-usage order. The public [protocol data](protocol-data.json) contains the exact implemented HID-to-physical mapping; `KEY_INDEX` in the CLI is executable reference code.

For **W**, usage `26` (`1A`) maps to **physical index 16**:

```text
primary pair:     10998 + 2*16 = 11030,11031
RT sensitivity:  12068 + 16   = 12084
RT mask:         12055 + 16//8 = 12057, bit 16%8 = 0
primary mapping: 48 + 5*16    = 128..132
```

Escape, function keys and navigation keys are not assumed analog merely because their HID usage fits a numeric range. Validate each usage against the mapping and analog capability.

## Action mapping

A mapping uses five bytes: one function byte and four key-code bytes, padded with zeros. Identified function bytes are hexadecimal:

| Function | Meaning |
|---|---|
| `00` | Disabled |
| `01`–`08` | Mouse functions |
| `31`–`34` | Scroll/pan |
| `51` | Keyboard |
| `61` | Consumer/media |
| `62` | Meta |
| `71` | Macro |

These are keyboard-generated output actions, not support for controlling a SteelSeries mouse. OpenGG's device discovery remains keyboard-only. Combinations stored with ACK have not all been physically checked. Unsupported host actions such as launching a program are refused for firmware mapping rather than silently encoded as a macro.

## Dual actuation / 2-in-1

At most **eight** physical analog indexes fit the receiver's list at 11293. Count all enabled positions, including keys the UI does not show. A secondary action combines its 70-position action mapping, its threshold pair, enable mask and consume-first mask. Disabled secondary thresholds use `FF/FF`; unused list entries use `FF`.

CLI `dual` accepts an object with `mm`, `function`, `keys` and `keep_first`, or `null` to disable. Example: W at 3.6 mm sends Shift+W while retaining the primary action:

```json
{"dual":{"26":{"mm":3.6,"function":81,"keys":[225,26],"keep_first":true}}}
```

## Rapid Tap and Protection

Rapid Tap has ten five-byte entries `[hid1,hid2,modeAndFlag,0,0]`. Mode 0 selects the last input, 1 the first key, 2 the second key, 3 neutral. Bit 7 means report both. Pairs use distinct, non-overlapping usages. `57` enables Tap; it is not the RT switch.

Protection has a selection mask, duration in milliseconds and raw sensitivity bytes. The UI now paints the mask on blue keys and exposes per-key **0–255 raw sensitivity** through its vertical shield slider. `ApexProfile.ReadConfig` reads each visible analog key's byte; the local profile retains it and `Patch` validates integer/range before changing only that key's byte and recalculating CRC. Selection and sensitivity need **Save to keyboard**; no live sensitivity opcode is guessed. Unchanged physical positions and the duration remain intact. The CLI also exposes duration/sensitivity using raw units.

The inherited reduction-in-mm and activation fields still belong to the simulator; their conversion to physical fields is **unverified and refused for flash**. Their old controls and explanatory text have been removed from the normal editor. Do not invent a physical-mm conversion for sensitivity 20. Byte acceptance/preservation is verified; the physical response of every possible sensitivity value is not calibrated.

The 9,600-byte macro container can be preserved or replaced explicitly with `macro_hex`. Compiling the friendly local macro editor into this firmware container is not implemented. `oled_hex` requires exactly 640 bytes in vertical page order. Byte-level access is available without claiming every byte sequence produces a valid macro or physically correct action.
