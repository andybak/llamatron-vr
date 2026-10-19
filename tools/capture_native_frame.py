"""Read a coherent original VGA frame from the experimental guest-side capture."""
import argparse
import json
import struct
from pathlib import Path

from PIL import Image
from ring_reader import RingReader


def read_frame(reader):
    capture = reader.manifest["framebuffer"]
    address = reader.ring_address + capture["metadata_offset"]
    for _ in range(5):
        before = reader.read(address, 8)
        bank, lock, sequence, page = struct.unpack("<4H", before)
        if not lock or lock & 1:
            continue
        if bank not in (0, 0x8000) or page not in (0, 0x2000, 0x4000, 0x6000):
            raise ValueError("Invalid native capture bank or video page")
        base = reader.guest_address + (reader.game_segment + capture["buffer_segment_relative"]) * 16
        data = reader.read(base + bank, capture["bank_bytes"] + capture.get("palette_bytes", 0))
        if reader.read(address, 8) == before:
            info = {"sequence": sequence, "page": page, "publication": lock}
            if capture.get("palette_bytes"):
                rgb = data[capture["bank_bytes"]:]
                info["palette"] = [[round(v * 255 / 63) for v in rgb[i:i + 3]] for i in range(0, 48, 3)]
            return data[:capture["bank_bytes"]], info
    raise LookupError("No complete native framebuffer available yet")


def decode_planes(planes, palette, width=320, height=200):
    plane_size = width * height // 8
    if len(planes) != plane_size * 4:
        raise ValueError("Expected four equally sized VGA bitplanes")
    pixels = bytearray(width * height)
    for offset in range(plane_size):
        values = [planes[offset + p * plane_size] for p in range(4)]
        for bit in range(8):
            pixels[offset * 8 + bit] = sum(((v >> (7 - bit)) & 1) << p for p, v in enumerate(values))
    image = Image.frombytes("P", (width, height), bytes(pixels))
    image.putpalette([channel for colour in palette for channel in colour] + [0] * (768 - len(palette) * 3))
    return image.convert("RGB")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--runtime", type=Path, default=Path("artifacts/effects-probe"))
    parser.add_argument("--output", type=Path)
    args = parser.parse_args()
    state = json.loads((args.runtime / "bridge-state.json").read_text())
    reader = RingReader(state["emulator_pid"], args.runtime / "hook-manifest.json")
    try:
        reader.locate()
        planes, info = read_frame(reader)
        palette = json.loads((args.runtime / "palette.json").read_text())
        output = args.output or args.runtime / "native-frame.png"
        decode_planes(planes, info.get("palette", palette)).save(output)
        output.with_suffix(".planes.bin").write_bytes(planes)
        output.with_suffix(".json").write_text(json.dumps(info, indent=2))
        print(f"Original VGA frame {info['sequence']}, page {info['page']:#x}: {output}")
    finally:
        reader.close()


if __name__ == "__main__":
    main()
