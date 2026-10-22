"""Verify retained sound-driver REA records against the original flat driver bytes."""
import hashlib
import json
from pathlib import Path
import struct


def sha(data):
    return hashlib.sha256(data).hexdigest()


def main():
    directory = Path('artifacts/reconstruction/audio')
    driver = Path('.tools/ci-game-input/SBLASTER.BIN').read_bytes()
    records = []
    paths = sorted(set(directory.glob('rea-driver-*-function.json')) | set(directory.glob('rea-driver-*-code.json')))
    for path in paths:
        entry = path.stem.split('-')[-2]
        wrapper = directory / f'driver-entry-{entry}.exe'
        body = wrapper.read_bytes()
        proof = json.loads(wrapper.with_suffix('.provenance.json').read_text())
        record = json.loads(path.read_text())
        evidence = record.get('structuredContent', {})
        if (record.get('isError') or not evidence.get('evidence_id') or
            struct.unpack_from('<H', body, 8)[0] * 16 != 64 or body[64:] != driver or
            struct.unpack_from('<H', body, 20)[0] != int(entry, 16) or
            proof['source_sha256'] != sha(driver) or proof['unchanged_load_image_sha256'] != sha(driver) or
            proof['derived_sha256'] != sha(body) or evidence['subject']['digest']['sha256'] != sha(body)):
            raise ValueError(f'Driver Evidence/provenance mismatch: {path}')
        records.append({'record': path.as_posix(), 'record_sha256': sha(path.read_bytes()),
                        'evidence_id': evidence['evidence_id'], 'operation': evidence['operation'],
                        'driver_entry': f'0x{entry}', 'wrapper_sha256': sha(body),
                        'provenance': wrapper.with_suffix('.provenance.json').as_posix()})
    report = {'driver_sha256': sha(driver), 'driver_bytes': len(driver), 'records': records,
              'limitations': ['Analysis-only 64-byte DOS headers are never executed.',
                              'Wrapper load images match every original flat driver byte.',
                              'Static dossiers do not establish hardware timing or audible parity.']}
    Path('docs/rea-audio-evidence-index.json').write_text(f'{json.dumps(report, indent=2)}\n')
    print(f'[LLAUDIO] Verified {len(records)} retained REA driver records and byte-exact wrapper provenance.')


if __name__ == '__main__':
    main()
