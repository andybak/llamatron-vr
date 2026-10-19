"""Export Llamatron's masked four-plane sprites for inspection.

Requires Pillow. Preview colours use the standard EGA palette provisionally;
palette-index exports preserve the data for a later runtime palette capture.
"""

import argparse
import json
import math
import struct
from pathlib import Path

from PIL import Image, ImageDraw

EGA = [(0, 0, 0), (0, 0, 170), (0, 170, 0), (0, 170, 170),
       (170, 0, 0), (170, 0, 170), (170, 85, 0), (170, 170, 170),
       (85, 85, 85), (85, 85, 255), (85, 255, 85), (85, 255, 255),
       (255, 85, 85), (255, 85, 255), (255, 255, 85), (255, 255, 255)]


def decode_sprite(data, offset, palette=EGA):
    kind, last_block, last_y, last_x, reserved1, reserved2 = struct.unpack_from("<6H", data, offset)
    width, height, blocks = last_x + 1, last_y + 1, last_block + 1
    if kind != 3 or not (1 <= width <= 320 and 1 <= height <= 200):
        raise ValueError(f"Invalid sprite header at {offset:#x}")
    if blocks != math.ceil(width / 16):
        raise ValueError(f"Unexpected block count at {offset:#x}")
    end = offset + 12 + height * blocks * 10
    if end > len(data):
        raise ValueError(f"Sprite exceeds bank at {offset:#x}")
    rgba = Image.new("RGBA", (width, height))
    indices = Image.new("L", (width, height))
    for y in range(height):
        for x in range(width):
            block, within = divmod(x, 16)
            row = offset + 12 + (y * blocks + block) * 10
            byte, bit = within // 8, 7 - within % 8
            index = sum(((data[row + 2 + plane * 2 + byte] >> bit) & 1) << plane
                        for plane in range(4))
            alpha = 255 if data[row + byte] & (1 << bit) else 0
            rgba.putpixel((x, y), (*palette[index], alpha))
            indices.putpixel((x, y), index)
    return rgba, indices, {"offset": offset, "width": width, "height": height,
                           "blocks": blocks, "encoded_size": end - offset}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--analysis", type=Path, default=Path("artifacts/analysis"))
    parser.add_argument("--output", type=Path, default=Path("artifacts/sprites"))
    parser.add_argument("--palette", type=Path)
    args = parser.parse_args()
    palette = json.loads(args.palette.read_text()) if args.palette else EGA
    if len(palette) != 16 or any(len(c) != 3 or any(not 0 <= v <= 255 for v in c) for c in palette):
        parser.error("Palette must contain sixteen RGB triples")
    image = (args.analysis / "GAME86.image.bin").read_bytes()
    main_segment = struct.unpack_from("<H", image, 0x25F)[0]
    g3_segment = struct.unpack_from("<H", image, 0x261)[0]
    banks = {"main": image[main_segment * 16:g3_segment * 16],
             "g3": (args.analysis / "G3.decoded.bin").read_bytes()}
    metadata, previews = [], []
    for bank, data in banks.items():
        # Confirmed initial pointer runs. Main bank has extra tables afterwards.
        count = 428 if bank == "main" else 16
        pointers = struct.unpack_from(f"<{count}H", data)
        folder = args.output / bank
        folder.mkdir(parents=True, exist_ok=True)
        seen = {}
        table_indices = {}
        for index, offset in enumerate(pointers):
            table_indices.setdefault(offset, []).append(index)
        offsets = set(pointers)
        # Animation routines also advance from a table entry to adjacent records.
        # These frames have no pointer of their own in the initial pointer run.
        for start in pointers:
            offset = start
            while offset + 12 <= len(data):
                kind, last_block, last_y, last_x, reserved1, reserved2 = struct.unpack_from("<6H", data, offset)
                size = 12 + (last_block + 1) * (last_y + 1) * 10
                if (kind != 3 or last_block != last_x // 16 or last_x > 319 or last_y > 199
                        or reserved1 or reserved2 or offset + size > len(data)):
                    break
                offsets.add(offset)
                offset += size
        for offset in sorted(offsets):
            name = f"{bank}-{offset:04x}"
            if offset not in seen:
                rgba, indices, item = decode_sprite(data, offset, palette)
                rgba.save(folder / f"{name}.png")
                indices.save(folder / f"{name}.indices.png")
                item.update(bank=bank, id=name, table_indices=table_indices.get(offset, []),
                            discovery="pointer_table" if offset in table_indices else "adjacent_animation_record")
                metadata.append(item)
                seen[offset] = item
                previews.append((name, rgba))
        print(f"{bank}: {count} table entries, {len(seen)} distinct sprites")
    columns, cell_w, cell_h = 10, 112, 126
    sheet = Image.new("RGB", (columns * cell_w, math.ceil(len(previews) / columns) * cell_h), (36, 36, 42))
    draw = ImageDraw.Draw(sheet)
    for i, (name, sprite) in enumerate(previews):
        x, y = (i % columns) * cell_w, (i // columns) * cell_h
        scale = max(1, min(3, (cell_w - 8) // sprite.width, (cell_h - 22) // sprite.height))
        sprite = sprite.resize((sprite.width * scale, sprite.height * scale), Image.Resampling.NEAREST)
        sheet.paste(sprite, (x + (cell_w - sprite.width) // 2, y + 18), sprite)
        draw.text((x + 4, y + 3), name, fill="white")
    sheet.save(args.output / "contact-sheet.png")
    (args.output / "sprites.json").write_text(json.dumps({
        "palette": str(args.palette) if args.palette else "Provisional standard EGA; runtime palette not verified",
        "sprites": metadata}, indent=2), encoding="utf-8")
    print(f"Exported {len(metadata)} sprites to {args.output}")


if __name__ == "__main__":
    main()
