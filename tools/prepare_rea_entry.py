"""Make an analysis-only MZ copy with one recovered indirect target as its entry.

Only the header's initial IP changes. All load-image and relocation bytes remain
identical. Do not execute the output: this is a Ghidra function-discovery hint.
"""

import argparse
import hashlib
import json
import struct
from pathlib import Path


def prepare(source, destination, entry):
    if source.resolve() == destination.resolve():
        raise ValueError("The analysis copy must not overwrite its source")
    data = source.read_bytes()
    if data[:2] != b"MZ" or len(data) < 28:
        raise ValueError("Expected an unpacked MZ executable")
    header_bytes = struct.unpack_from("<H", data, 8)[0] * 16
    initial_ip, initial_cs = struct.unpack_from("<HH", data, 20)
    if initial_cs != 0 or not 0 <= entry < min(65536, len(data) - header_bytes):
        raise ValueError("Expected a CS=0 image and an entry inside its first 64K")
    patched = bytearray(data)
    struct.pack_into("<H", patched, 20, entry)
    assert patched[header_bytes:] == data[header_bytes:]
    assert patched[:20] == data[:20] and patched[22:] == data[22:]
    destination.parent.mkdir(parents=True, exist_ok=True)
    destination.write_bytes(patched)
    report = {
        "purpose": "Analysis only; do not execute. Hints one indirect function to Ghidra.",
        "source_path": str(source.resolve()),
        "source_sha256": hashlib.sha256(data).hexdigest(),
        "derived_sha256": hashlib.sha256(patched).hexdigest(),
        "unchanged_load_image_sha256": hashlib.sha256(data[header_bytes:]).hexdigest(),
        "modified_file_range": {"offset": 20, "length": 2},
        "source_initial_ip": initial_ip, "derived_initial_ip": entry,
        "initial_cs": initial_cs, "load_image_unchanged": True,
        "relocations_unchanged": True}
    destination.with_suffix(".provenance.json").write_text(f"{json.dumps(report, indent=2)}\n", encoding="utf-8")
    return report


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("source", type=Path)
    parser.add_argument("entry", type=lambda value: int(value, 0))
    parser.add_argument("destination", type=Path)
    args = parser.parse_args()
    prepare(args.source, args.destination, args.entry)
    print(f"Analysis-only entry CS:{args.entry:04x}; load image unchanged; output: {args.destination}")


if __name__ == "__main__":
    main()
