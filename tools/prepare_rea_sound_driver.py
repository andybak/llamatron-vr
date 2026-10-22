"""Wrap the supplied flat 16-bit sound driver for analysis only; never execute it."""
import argparse
import hashlib
import json
from pathlib import Path
import struct


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('driver', type=Path)
    parser.add_argument('entry', type=lambda value: int(value, 0))
    parser.add_argument('output', type=Path)
    args = parser.parse_args()
    if args.output.resolve() == args.driver.resolve():
        parser.error('The analysis wrapper must not overwrite the original driver')
    body = args.driver.read_bytes()
    if not 0 <= args.entry < len(body):
        parser.error('Entry must be within the original driver bytes')
    header = bytearray(64)
    size = len(header) + len(body)
    struct.pack_into('<14H', header, 0, 0x5a4d, size % 512, (size + 511) // 512,
                     0, 4, 0, 0xffff, 0, 0xfffe, 0, args.entry, 0, 0x40, 0)
    wrapped = bytes(header) + body
    assert wrapped[64:] == body
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_bytes(wrapped)
    proof = {'purpose': 'Analysis-only DOS header; never execute this wrapper',
             'source': str(args.driver), 'source_sha256': hashlib.sha256(body).hexdigest(),
             'derived_sha256': hashlib.sha256(wrapped).hexdigest(),
             'unchanged_load_image_sha256': hashlib.sha256(wrapped[64:]).hexdigest(),
             'header_bytes': 64, 'initial_ip': args.entry}
    args.output.with_suffix('.provenance.json').write_text(f'{json.dumps(proof, indent=2)}\n')
    print(f'Analysis-only sound-driver entry {args.entry:04x}; original {len(body)} code/data bytes unchanged.')


if __name__ == '__main__':
    main()
