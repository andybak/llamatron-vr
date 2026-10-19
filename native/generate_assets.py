"""Embed the captured sprite graphics and the development OpenXR loader."""
import json
from pathlib import Path
from PIL import Image

root = Path(__file__).resolve().parent
generated = root / "generated"
generated.mkdir(exist_ok=True)
manifest = json.loads((root.parent / "artifacts/sprites/sprites.json").read_text())
with (generated / "assets.h").open("w") as output:
    output.write('#pragma once\n#include <cstdint>\nstruct EmbeddedSprite {uint16_t bank,offset,width,height;const uint8_t* rgba;};\n')
    records = []
    for number, item in enumerate(manifest["sprites"]):
        data = Image.open(root.parent / "artifacts/sprites" / item["bank"] / f"{item['id']}.png").convert("RGBA").tobytes()
        output.write(f'const uint8_t pixels_{number}[]={{\n')
        for offset in range(0, len(data), 64):
            output.write(','.join(str(value) for value in data[offset:offset + 64]) + ',\n')
        output.write('};\n')
        bank = 0x118C if item["bank"] == "main" else 0x20EB
        records.append(f'{{{bank},{item["offset"]},{item["width"]},{item["height"]},pixels_{number}}}')
    output.write('const EmbeddedSprite embeddedSprites[]={\n' + ',\n'.join(records) + '\n};\n')
loader = generated / "openxr_loader.dll"
if not loader.is_file():
    raise SystemExit("Place an official Windows x64 OpenXR loader at native/generated/openxr_loader.dll")
(generated / "loader.rc").write_text(f'101 RCDATA "{loader.as_posix()}"\n')
print(f'Embedded {len(records)} sprites; OpenXR loader is bundled as an executable resource')
