"""Apex Pro TKL Wireless Gen 3 (1038:1644), schema 19, independent of GG.

Read a slot before editing it. Unknown bytes, macros and other keys are preserved.
The Windows HID reports include report ID 0; hidapi input replies omit that ID.
See docs/protocol.md for the measured protocol and JSON edits.
"""
import argparse
import hashlib
import json
import math
from pathlib import Path
import struct
import subprocess
import time
import zlib

SIZE, BODY_END, CHUNK = 12288, 12280, 512
KEY_INDEX = {
    **dict(zip(range(4, 58), [29,48,46,31,17,32,33,34,22,35,36,37,50,49,23,24,15,18,30,19,21,47,16,45,20,44,1,2,3,4,5,6,7,8,9,10,41,70,67,14,60,11,12,25,26,27,40,38,39,0,51,52,53,28])),
    **dict(zip(range(58, 70), range(71, 83))),
    **dict(zip(range(73, 83), [83,84,85,86,87,88,92,90,91,89])),
    100:43, 135:54, 136:62, 137:13, 138:61, 139:59,
    **dict(zip(range(224, 232), [56,42,58,57,66,55,63,64])),
    240:65,
}
# Firmware >= 3.22.0: measured capture anchors and the GG distance lookup table.
THRESHOLDS = [(4,4),(3,4),(4,5),(5,7),(7,8),(8,10),(10,12),(12,14),(14,16),(16,18),
              (18,20),(20,22),(22,25),(25,28),(28,31),(31,34),(34,38),(38,41),(41,45),(45,49),
              (49,54),(54,59),(59,65),(65,71),(71,78),(78,85),(85,93),(93,102),(102,112),(112,122),
              (122,134),(134,147),(147,162),(162,177),(177,186),(186,196),(196,206),(206,216),(208,216),(210,216)]
GLOBAL_THRESHOLDS = [(4,4),(3,4),(5,7),(8,10),(12,14),(16,18),(20,22),(25,28),(31,34),(38,41),
                     (45,49),(54,59),(65,71),(78,85),(93,102),(112,122),(134,147),(162,162),(196,206),(210,216)]


def validate(blob):
    if len(blob) != SIZE or struct.unpack_from('<I', blob, 4)[0] != 19:
        raise ValueError('Expected a 12288-byte schema-19 profile; no write attempted.')
    if struct.unpack_from('<I', blob)[0] != zlib.crc32(blob[8:BODY_END]):
        raise ValueError('Profile CRC mismatch; no write attempted.')


def thresholds(mm):
    if type(mm) not in (int, float) or not math.isfinite(mm) or not .1 <= mm <= 4:
        raise ValueError('Actuation must be between 0.1 and 4.0 mm.')
    if abs(mm * 10 - round(mm * 10)) > 1e-8:
        raise ValueError('Actuation must use 0.1 mm steps.')
    return THRESHOLDS[round(mm * 10) - 1]


def key_index(usage, analog=False):
    usage = int(usage, 0) if isinstance(usage, str) and usage.startswith('0x') else int(usage)
    index = KEY_INDEX.get(usage)
    if index is None or (analog and index >= 70):
        raise ValueError(f'Unsupported HID usage {usage} for this setting.')
    return index


def bit(blob, offset, index, enabled):
    if type(enabled) is not bool:
        raise ValueError('Enabled flags must be JSON true or false.')
    mask = 1 << (index % 8)
    blob[offset + index // 8] = (blob[offset + index // 8] & ~mask) | (mask if enabled else 0)


def mapping(value):
    function = value['function']
    keys = value['keys']
    if type(function) is not int or function not in [0, *range(1, 9), 0x31, 0x32, 0x33, 0x34, 0x51, 0x61, 0x62, 0x71]:
        raise ValueError('Unsupported onboard mapping function.')
    if not isinstance(keys, list) or len(keys) > 4 or any(type(x) is not int or not 0 <= x <= 255 for x in keys):
        raise ValueError('A mapping has at most four byte-sized key codes.')
    return bytes([function, *keys, *([0] * (4 - len(keys)))])


def patch_profile(blob, edits):
    validate(blob)
    if not isinstance(edits, dict):
        raise ValueError('Edits must be a JSON object.')
    result = bytearray(blob)
    known = {'name','actuation','rt','protection','protection_duration_ms','protection_sensitivity',
             'rapid_tap','remap','meta','dual','oled_hex','macro_hex'}
    if set(edits) - known:
        raise ValueError(f'Unknown settings: {set(edits) - known}')
    for field in ['actuation','rt','protection','protection_sensitivity','rapid_tap','remap','meta','dual']:
        if field in edits and not isinstance(edits[field], dict):
            raise ValueError(f'{field} must be a JSON object.')
    if 'name' in edits:
        name = edits['name'].encode('ascii')
        if len(name) > 21 or any(x < 32 or x > 126 for x in name):
            raise ValueError('Profile name must have at most 21 printable ASCII characters.')
        result[10:32] = name + bytes(22 - len(name))
    if edits.get('actuation') and result[11278]:
        global_pair = bytes(GLOBAL_THRESHOLDS[max(1,min(20,result[11279])) - 1])
        result[10998:11138] = global_pair * 70
    for usage, mm in edits.get('actuation', {}).items():
        index = key_index(usage, True)
        result[10998 + 2*index:11000 + 2*index] = bytes(thresholds(mm))
        result[11278] = 0  # Per-key thresholds take precedence over the global preset.
    for usage, value in edits.get('rt', {}).items():
        if not isinstance(value, dict) or set(value) - {'enabled','sensitivity'}:
            raise ValueError('RT accepts enabled and sensitivity.')
        index = key_index(usage, True)
        if 'enabled' in value:
            bit(result, 12055, index, value['enabled'])
        if 'sensitivity' in value:
            s = value['sensitivity']
            if type(s) not in (int, float) or not math.isfinite(s) or not .1 <= s <= 4:
                raise ValueError('Rapid Trigger sensitivity must be between 0.1 and 4.0 mm.')
            if abs(s * 10 - round(s * 10)) > 1e-8:
                raise ValueError('RT sensitivity must use 0.1 mm steps.')
            result[12068 + index] = round(s * 10)
    for usage, enabled in edits.get('protection', {}).items():
        bit(result, 12138, key_index(usage, True), enabled)
    if 'protection_duration_ms' in edits:
        duration = edits['protection_duration_ms']
        if type(duration) is not int or not 0 <= duration <= 60000:
            raise ValueError('Protection duration must be between 0 and 60000 ms.')
        struct.pack_into('<I', result, 12152, duration)
    for usage, sensitivity in edits.get('protection_sensitivity', {}).items():
        if type(sensitivity) is not int or not 0 <= sensitivity <= 255:
            raise ValueError('Protection sensitivity must fit one byte (GG default: 20).')
        result[12156 + key_index(usage, True)] = sensitivity
    for field, offset in [('remap',48), ('meta',548)]:
        for usage, value in edits.get(field, {}).items():
            index = key_index(usage)
            result[offset + 5*index:offset + 5*index + 5] = mapping(value)
    for usage, value in edits.get('dual', {}).items():
        index = key_index(usage, True)
        enabled = value is not None
        if enabled and (not isinstance(value, dict) or type(value.get('keep_first', True)) is not bool):
            raise ValueError('2-in-1 needs an object and a boolean keep_first flag, or null to disable.')
        result[11138 + 2*index:11140 + 2*index] = bytes(thresholds(value['mm'])) if enabled else b'\xff\xff'
        result[1048 + 5*index:1053 + 5*index] = mapping(value) if enabled else bytes(5)
        bit(result, 11280, index, enabled)
        bit(result, 11301, index, enabled and not value.get('keep_first', True))
    if 'dual' in edits:
        indexes = [i for i in range(70) if result[11280+i//8] & (1 << (i%8))]
        if len(indexes) > 8:
            raise ValueError('Wireless firmware supports at most eight 2-in-1 keys.')
        result[11293:11301] = bytes(indexes + [255] * (8-len(indexes)))
    if 'rapid_tap' in edits:
        tap = edits['rapid_tap']
        if set(tap) - {'enabled','pairs'}:
            raise ValueError('Rapid Tap accepts enabled and pairs.')
        if 'enabled' in tap:
            if type(tap['enabled']) is not bool:
                raise ValueError('Rapid Tap enabled must be a boolean.')
            result[12276] = int(tap['enabled'])
        if 'pairs' in tap:
            pairs = tap['pairs']
            if not isinstance(pairs, list) or len(pairs) > 10:
                raise ValueError('At most ten Rapid Tap pairs fit the profile.')
            result[12226:12276] = bytes(50)
            seen = set()
            for i, p in enumerate(pairs):
                if not isinstance(p, dict) or set(p) - {'first','second','mode','report_both'}:
                    raise ValueError('Invalid Rapid Tap pair.')
                a, b, mode = p['first'], p['second'], p.get('mode', 0)
                if any(type(x) is not int for x in [a,b,mode]) or type(p.get('report_both',False)) is not bool:
                    raise ValueError('Rapid Tap keys/mode must be integers; report_both must be a boolean.')
                key_index(a); key_index(b)
                if a == b or a in seen or b in seen or mode not in range(4):
                    raise ValueError('Rapid Tap pairs must use distinct, non-overlapping keys and mode 0..3.')
                seen.update([a,b])
                result[12226+5*i:12231+5*i] = bytes([a,b,mode | (128 if p.get('report_both',False) else 0),0,0])
    for field, offset, length in [('oled_hex',11415,640),('macro_hex',1398,9600)]:
        if field in edits:
            raw = bytes.fromhex(edits[field])
            if len(raw) != length:
                raise ValueError(f'{field} must contain exactly {length} bytes.')
            result[offset:offset+length] = raw
    struct.pack_into('<I', result, 0, zlib.crc32(result[8:BODY_END]))
    validate(result)
    return bytes(result)


ANALOG_USAGES = sorted(usage for usage,index in KEY_INDEX.items() if index < 70)


def live_reports(original, edits):
    if not isinstance(edits, dict) or not edits or set(edits) - {'actuation','rt'}:
        raise ValueError('Live edits accept only actuation and rt; no flash fields.')
    changed = patch_profile(original, edits)  # Validate every field before sending anything.
    reports = []
    if edits.get('actuation'):
        global_pair = bytes(GLOBAL_THRESHOLDS[max(1,min(20,changed[11279]))-1]) if changed[11278] else None
        entries = b''.join(bytes([usage]) + (global_pair or changed[10998+2*KEY_INDEX[usage]:11000+2*KEY_INDEX[usage]]) for usage in ANALOG_USAGES)
        reports.append(bytes([0,0x6f,0,len(ANALOG_USAGES)]) + entries)
    if edits.get('rt'):
        entries = b''.join(bytes([usage,int(bool(changed[12055+KEY_INDEX[usage]//8] & (1 << (KEY_INDEX[usage]%8))))]) for usage in ANALOG_USAGES)
        reports.append(bytes([0,0x76,len(ANALOG_USAGES)]) + entries)
        if any(entries[1::2]):
            sensitivities = b''.join(bytes([usage,changed[12068+KEY_INDEX[usage]]]) for usage in ANALOG_USAGES)
            reports.append(bytes([0,0x77,len(ANALOG_USAGES)]) + sensitivities)
    return reports


class Receiver:
    def __init__(self, log=None, delay_ms=31):
        import hid
        if not math.isfinite(delay_ms) or not 1 <= delay_ms <= 1000:
            raise ValueError('Report interval must be between 1 and 1000 ms.')
        running = subprocess.run(['tasklist','/FO','CSV','/NH'],capture_output=True,text=True,check=True)
        conflicts = ['SteelSeriesEngine.exe','SteelSeriesPrism.exe','OneRGB.exe','OpenGG.exe','OpenRGB.exe','SignalRgb.exe']
        if any(f'"{name.lower()}"' in running.stdout.lower() for name in conflicts):
            raise RuntimeError('Close GG, OneRGB and other keyboard/RGB controllers before using this external CLI.')
        devices = [d for d in hid.enumerate(0x1038,0x1644) if d['usage_page']==0xffc0 and d['usage']==1 and d['interface_number']==3]
        if len(devices) != 1:
            raise RuntimeError('Expected exactly one 1038:1644 / MI_03 / FFC0:0001 receiver.')
        self.device = hid.device()
        self.device.open_path(devices[0]['path'])
        self.device.set_nonblocking(1)
        self.log, self.delay = log, delay_ms / 1000
        self.rgb_released = False
        if self.log:
            self.log.parent.mkdir(parents=True,exist_ok=True)

    def close(self):
        self.device.close()

    def record(self, direction, report):
        if self.log:
            with self.log.open('a',encoding='utf-8') as f:
                f.write(json.dumps({'time_ns':time.time_ns(),'direction':direction,'hex':report.hex()})+'\n')

    def request(self, report, feature=False, timeout=5):
        size = 642 if feature else 65
        if not 2 <= len(report) <= size or report[0] != 0:
            raise ValueError('Invalid report size or report ID; no write attempted.')
        if feature and report[1] == 0x61:
            self.rgb_released = False
        if report[1] != 0x61 and not self.rgb_released:
            # Release any temporary RGB left by a controller that has already closed. No ACK for 0x62.
            release = bytes([0,0x62]) + bytes(63)
            self.record('out',release)
            if self.device.write(release) != len(release):
                raise RuntimeError('Incomplete RGB release; no profile command attempted.')
            time.sleep(max(.031,self.delay))
            self.rgb_released = True
        while self.device.read(65):
            pass
        report += bytes(size - len(report))
        start = time.monotonic()
        self.record('out',report)
        sent = (self.device.send_feature_report if feature else self.device.write)(report)
        if sent != len(report):
            raise RuntimeError(f'Incomplete HID send ({sent}/{len(report)}); transaction stopped.')
        while time.monotonic() - start < timeout:
            reply = bytes(self.device.read(65))
            if reply:
                self.record('in',reply)
                if len(reply) >= 2 and reply[0] == report[1]:
                    if reply[1] != 0:
                        raise RuntimeError(f'Device rejected {report[1]:02x}: {reply[:12].hex()}')
                    time.sleep(max(0,self.delay - (time.monotonic()-start)))
                    return reply
            time.sleep(.002)
        raise TimeoutError(f'No ACK for {report[:10].hex()}; transaction stopped.')

    def read(self, slot):
        if slot not in range(1,6):
            raise ValueError('Slot must be 1..5.')
        parts = []
        for offset in range(0,SIZE,CHUNK):
            self.request(bytes([0,0x83,1,10+slot])+struct.pack('<HI',CHUNK,offset),True)
            reply = bytes(self.device.get_feature_report(0,642))
            self.record('feature-in',reply)
            if len(reply) != 642 or reply[:3] != b'\x00\x83\x00':
                raise RuntimeError('Invalid profile read response.')
            parts.append(reply[3:515])
        blob = b''.join(parts)
        validate(blob)
        return blob

    def write(self, slot, blob, backup_dir, expected_original=None):
        validate(blob)
        original = self.read(slot)
        if expected_original is not None and original != expected_original:
            raise ValueError('The stored profile changed after the baseline was read. Read again; no erase attempted.')
        backup_dir.mkdir(parents=True,exist_ok=True)
        backup = backup_dir / f'slot-{slot}-{time.time_ns()}.bin'
        backup.write_bytes(original)
        try:
            # Erase ACKs take longer than normal reports. Each block needs its own ACK.
            for namespace, file_id, begin, write in [(3,slot,0x42,0x43),(1,10+slot,2,3)]:
                self.request(bytes([0,begin,namespace,file_id]))
                for offset in range(0,SIZE,CHUNK):
                    self.request(bytes([0,write,namespace,file_id])+struct.pack('<HI',CHUNK,offset)+blob[offset:offset+CHUNK],True)
                self.request(bytes([0,0xe6,slot-1]))
            if self.read(slot) != blob:
                raise RuntimeError('Readback mismatch.')
        except Exception as error:
            raise RuntimeError(f'Write stopped and may be incomplete. Original backup: {backup}. No automatic retry. {error}') from error
        return backup

    def load(self, slot):
        if slot not in range(1,6):
            raise ValueError('Slot must be 1..5.')
        self.request(bytes([0,0x53,slot-1]))
        self.request(bytes([0,0x68,0]))  # Use persistent settings instead of a volatile profile.


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('operation',choices=['read','edit','write','load','live'])
    parser.add_argument('--slot',type=int,choices=range(1,6),default=2)
    parser.add_argument('--profile',type=Path,help='Binary profile file (read output / edit input / write input).')
    parser.add_argument('--edits',type=Path,help='JSON settings; edit operates offline.')
    parser.add_argument('--expected',type=Path,help='Baseline profile to compare before erasing (recommended for write).')
    parser.add_argument('--out',type=Path,help='Edited binary output.')
    parser.add_argument('--backup',type=Path,default=Path.home()/'OpenGG-Apex-Backups')
    parser.add_argument('--log',type=Path)
    parser.add_argument('--delay-ms',type=float,default=31,help='Minimum command interval, adjustable for slower receivers.')
    args = parser.parse_args()
    if args.operation == 'edit':
        if not all([args.profile,args.edits,args.out]):
            parser.error('edit needs --profile, --edits and --out')
        args.out.write_bytes(patch_profile(args.profile.read_bytes(),json.loads(args.edits.read_text(encoding='utf-8-sig'))))
        print(args.out)
        return
    if args.operation in ['read','write'] and not args.profile:
        parser.error('read / write needs --profile')
    if args.operation == 'live' and not args.edits:
        parser.error('live needs --edits (actuation/rt only)')
    blob = args.profile.read_bytes() if args.operation == 'write' else None
    if blob is not None: validate(blob)
    device = Receiver(args.log,args.delay_ms)
    try:
        if args.operation == 'read':
            blob = device.read(args.slot)
            args.profile.write_bytes(blob)
            print(f'Slot {args.slot}: {blob[10:31].split(bytes(1))[0].decode()} / CRC OK / SHA256 {hashlib.sha256(blob).hexdigest()}')
        elif args.operation == 'write':
            expected = args.expected.read_bytes() if args.expected else None
            if expected is not None: validate(expected)
            backup = device.write(args.slot,blob,args.backup,expected)
            print(f'Written and read back byte for byte. Backup: {backup}')
        elif args.operation == 'live':
            original = device.read(args.slot)
            reports = live_reports(original,json.loads(args.edits.read_text(encoding='utf-8-sig')))
            device.load(args.slot)
            start = time.monotonic()
            for report in reports: device.request(report,True)
            print(f'Live ACKs: {[f"0x{r[1]:02X}" for r in reports]}; {time.monotonic()-start:.3f}s; no flash write. OLED may display 1.4 for encoded 1.5 mm.')
        else:
            device.load(args.slot)
            print(f'Loaded slot {args.slot}.')
    finally:
        device.close()


if __name__ == '__main__':
    main()
