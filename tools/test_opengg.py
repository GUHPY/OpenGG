"""Offline checks; no HID device is opened."""
import struct
import unittest
import zlib
import tempfile
from unittest.mock import Mock
from pathlib import Path
from opengg import Receiver, patch_profile, validate, live_reports, ANALOG_USAGES
from analyze_capture import banks


class ProfileCheck(unittest.TestCase):
    def test_bulk_live_preserves_other_keys_and_rejects_invalid_edits(self):
        raw = bytearray(12288)
        struct.pack_into('<I',raw,4,19)
        raw[12280:] = b'\xff' * 8
        raw[10998:11138] = bytes([45,49]) * 70
        struct.pack_into('<I',raw,0,zlib.crc32(raw[8:12280]))
        original = bytes(raw)
        report, = live_reports(original,{'actuation':{'26':1.5}})
        self.assertEqual(len(ANALOG_USAGES),68)
        self.assertEqual(report[:4],bytes([0,0x6f,0,68]))
        for i,usage in enumerate(ANALOG_USAGES):
            self.assertEqual(report[4+3*i:7+3*i],bytes([usage,28,31] if usage==26 else [usage,45,49]))
        with self.assertRaises(ValueError): live_reports(original,{'actuation':{'26':1.51}})
        with self.assertRaises(ValueError): live_reports(original,{'protection':{'26':True}})
        with self.assertRaises(ValueError): live_reports(original,{'actuation':{'26':float('nan')}})

    def test_live_feature_releases_rgb_before_send(self):
        receiver = Receiver.__new__(Receiver)
        receiver.log, receiver.delay, receiver.rgb_released = None, 0, False
        device = receiver.device = Mock()
        device.write.return_value = 65
        device.send_feature_report.return_value = 642
        device.read.side_effect = [[], [0x6f, 0]]

        receiver.request(bytes([0, 0x6f]), feature=True)

        self.assertEqual(device.mock_calls[0].args[0], bytes([0, 0x62]) + bytes(63))
        self.assertEqual(device.mock_calls[2].args[0], bytes([0, 0x6f]) + bytes(640))
        self.assertTrue(receiver.rgb_released)

    def test_crc_edit_and_capture_reassembly(self):
        raw = bytearray((i * 37 + 11) % 256 for i in range(12288))
        struct.pack_into('<I', raw, 4, 19)
        raw[12280:] = b'\xff' * 8
        raw[11278] = 0
        struct.pack_into('<I', raw, 0, zlib.crc32(raw[8:12280]))
        original = bytes(raw)
        changed = patch_profile(original, {'actuation': {'26': 3.0}, 'rt': {'26': {'enabled': True, 'sensitivity': .5}}})
        self.assertEqual(changed[11030:11032], b'\x70\x7a')  # W has physical index 16, not HID index 26.
        self.assertEqual(changed[12084], 5)
        self.assertTrue(changed[12057] & 1)
        allowed = {0,1,2,3,11030,11031,11278,12057,12084}
        self.assertTrue(all(a == b or i in allowed for i, (a,b) in enumerate(zip(original, changed))))
        self.assertEqual(original[12280:], changed[12280:])
        validate(changed)
        bad = bytearray(changed); bad[5000] ^= 1
        with self.assertRaises(ValueError): validate(bad)
        with self.assertRaises(ValueError): patch_profile(original, {'rt': {'26': {'enabled': 'false'}}})
        with self.assertRaises(ValueError): patch_profile(original, {'actuation': {'41': .5}})  # Escape is digital.
        records = [{'hex': '746c73', 'event': 'WriteFile'}]  # Noise is ignored.
        for offset in range(0,12288,512):
            report = bytes([0,0x43,3,2]) + struct.pack('<HI',512,offset) + changed[offset:offset+512] + bytes(120)
            records.append({'step':4, 'hex':report.hex()})
        self.assertEqual(banks(records)[0]['blob'], changed)
        self.assertEqual(banks(records[:-1]), [])
        global_profile = bytearray(original)
        global_profile[11278:11280] = bytes([1,11])
        struct.pack_into('<I', global_profile, 0, zlib.crc32(global_profile[8:12280]))
        changed_global = patch_profile(bytes(global_profile), {'actuation': {'26': 3.0}})
        validate(changed_global)
        for index in range(70):
            self.assertEqual(changed_global[10998+index*2:11000+index*2], b'\x70\x7a' if index==16 else b'\x2d\x31')
        self.assertEqual(changed_global[11278], 0)
        self.assertEqual(changed_global[1398:10998], original[1398:10998])
        receiver = Receiver.__new__(Receiver)  # Offline failure injection; no HID open.
        receiver.read = lambda slot: original
        sent = []
        def fail_erase(report, feature=False):
            sent.append(report)
            raise TimeoutError('missing erase ACK')
        receiver.request = fail_erase
        with tempfile.TemporaryDirectory() as directory:
            with self.assertRaisesRegex(RuntimeError, 'Original backup:'):
                receiver.write(2, original, Path(directory))
            self.assertEqual(next(Path(directory).glob('*.bin')).read_bytes(), original)
            self.assertEqual(sent, [bytes([0,0x42,3,2])])


if __name__ == '__main__':
    unittest.main()
