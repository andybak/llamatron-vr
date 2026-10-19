"""Capture the running game's 16 displayed colours through the DOSBox debugger."""
import json
import re
from pathlib import Path

from debug_command import execute


def main():
    dac = execute("EXEC VGA DACPAL")
    attributes = execute("EXEC VGA AC")
    colours = []
    for line in dac.splitlines():
        if re.match(r"^[0-9a-fA-F]{2}:", line):
            colours.extend(tuple(int(code[i:i + 2], 16) for i in (0, 2, 4))
                           for code in re.findall(r"\b[0-9a-fA-F]{6}\b", line))
    match = re.search(r"palette=\{([^}]+)\}", attributes)
    mapping = [int(value, 16) for value in re.findall(r"[0-9a-fA-F]{2}", match[1])]
    if len(colours) != 256 or len(mapping) != 16 or max(mapping) >= len(colours):
        raise ValueError("Unexpected debugger palette format")
    if "color-select=00h" not in attributes or "mode_control=09h" not in attributes:
        raise ValueError("Unsupported attribute-controller palette configuration")
    palette = [[round(channel * 255 / 63) for channel in colours[index]] for index in mapping]
    output = Path("artifacts/runtime/palette.json")
    output.write_text(json.dumps(palette, indent=2), encoding="utf-8")
    Path("artifacts/runtime/palette-evidence.txt").write_text(dac + "\n" + attributes, encoding="utf-8")
    print(f"Saved 16 live palette colours to {output}")


if __name__ == "__main__":
    main()
