"""Pack recovered sprite PNGs into a player-ready atlas without resizing pixels."""

import argparse
import hashlib
import json
from pathlib import Path

from PIL import Image


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--sprites", type=Path, default=Path("artifacts/sprites"))
    parser.add_argument("--output", type=Path, default=Path("standalone/Unity/Assets/Llamatron/Resources/Generated"))
    args = parser.parse_args()
    manifest_path = args.sprites / "sprites.json"
    manifest = json.loads(manifest_path.read_text())
    width, padding = 1024, 2
    x = y = row_height = 0
    placements = []
    for item in sorted(manifest["sprites"], key=lambda s: (-s["height"], s["bank"], s["offset"])):
        w, h = item["width"], item["height"]
        if w + padding * 2 > width:
            raise ValueError(f"Sprite does not fit atlas: {item['id']}")
        if x + w + padding * 2 > width:
            y += row_height; x = row_height = 0
        placements.append((item, x + padding, y + padding))
        x += w + padding * 2; row_height = max(row_height, h + padding * 2)
    height = 1
    while height < y + row_height:
        height *= 2
    atlas = Image.new("RGBA", (width, height))
    records = []
    for item, left, top in placements:
        source = args.sprites / item["bank"] / f"{item['id']}.png"
        with Image.open(source) as sprite:
            rgba = sprite.convert("RGBA")
            if rgba.size != (item["width"], item["height"]):
                raise ValueError(f"Sprite dimensions disagree with manifest: {source}")
            atlas.paste(rgba, (left, top))
            pixel_hash = hashlib.sha256(rgba.tobytes()).hexdigest()
        records.append({"bank": 0x118C if item["bank"] == "main" else 0x20EB,
                        "offset": item["offset"], "width": item["width"], "height": item["height"],
                        "x": left, "y": height - top - item["height"], "pixelSha256": pixel_hash})
    args.output.mkdir(parents=True, exist_ok=True)
    atlas.save(args.output / "atlas.png")
    palette = manifest["palette_rgb"]
    if len(palette) != 16 or any(len(color) != 3 or any(not 0 <= component <= 255 for component in color) for color in palette):
        raise ValueError("Expected sixteen source palette RGB triples")
    data = {"width": width, "height": height, "sprites": records,
            "palette": [{"r": color[0], "g": color[1], "b": color[2], "a": 255} for color in palette],
            "sourceManifestSha256": hashlib.sha256(manifest_path.read_bytes()).hexdigest()}
    (args.output / "atlas.json").write_text(f"{json.dumps(data, indent=2)}\n", encoding="utf-8")
    print(f"Packed {len(records)} authentic sprites into {width}x{height}; no source pixels resized.")


if __name__ == "__main__":
    main()
