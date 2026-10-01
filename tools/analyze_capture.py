"""Recover complete 512-byte profile transfers; ignore unrelated process traffic."""
import argparse
from collections import Counter
import hashlib
import json
from pathlib import Path
import struct
from opengg import validate


def banks(records):
    active = {}
    result = []
    for record in records:
        try:
            report = bytes.fromhex(record.get('hex', ''))
        except ValueError:
            continue
        if len(report) != 642 or report[0] != 0 or (report[1], report[2]) not in [(0x43, 3), (3, 1)]:
            continue
        length, offset = struct.unpack_from('<HI', report, 4)
        if length != 512 or offset % 512 or offset >= 12288:
            continue
        key = tuple(report[1:4])
        if offset == 0:
            active[key] = {'step': record.get('step'), 'destination': key, 'chunks': {}}
        if key not in active:
            continue
        active[key]['chunks'][offset] = report[10:522]
        if len(active[key]['chunks']) == 24:
            bank = active.pop(key)
            chunks = bank.pop('chunks')
            bank['blob'] = b''.join(chunks[i] for i in range(0, 12288, 512))
            validate(bank['blob'])
            result.append(bank)
    return result


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('capture', type=Path)
    parser.add_argument('--out', type=Path, required=True)
    args = parser.parse_args()
    records = json.loads(args.capture.read_text(encoding='utf-8-sig'))
    recovered = banks(records)
    args.out.mkdir(parents=True, exist_ok=True)
    manifest = []
    previous = None
    for i, bank in enumerate(recovered):
        blob = bank['blob']
        name = f'bank-{i:02}-step-{bank["step"]}.bin'
        (args.out / name).write_bytes(blob)
        manifest.append({**{k: v for k, v in bank.items() if k != 'blob'}, 'file': name,
            'size': len(blob), 'crc32': f'{struct.unpack_from("<I", blob)[0]:08x}',
            'sha256': hashlib.sha256(blob).hexdigest(),
            'changed_offsets': [j for j in range(len(blob)) if previous is not None and previous[j] != blob[j]]})
        previous = blob
    report = {'records': len(records), 'events': dict(Counter(r.get('event') for r in records)), 'banks': manifest}
    (args.out / 'manifest.json').write_text(json.dumps(report, indent=2), encoding='utf-8')
    print(f'{len(records)} records / {len(recovered)} complete profiles / all CRCs valid. {args.out / "manifest.json"}')


if __name__ == '__main__':
    main()
