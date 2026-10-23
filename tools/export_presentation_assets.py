"""Export the original bitmap font and initial high-score table for the player."""
import argparse
import json
import struct
from pathlib import Path


def export(analysis, source, output):
    font = (analysis / 'F.decoded.bin').read_bytes()
    scores = (source / 'SCORES.ORI').read_bytes()
    if len(font) != 1710 or len(scores) != 160:
        raise ValueError('[LLPRESENT] Unexpected original font or high-score resource')
    output.mkdir(parents=True, exist_ok=True)
    (output / 'OriginalFont.bytes').write_bytes(font)
    (output / 'OriginalScores.bytes').write_bytes(scores)
    image = (analysis / 'GAME86.image.bin').read_bytes()
    main = struct.unpack_from('<H', image, 0x25f)[0] * 16
    # CS:402c selects ten logo glyphs through DS:2118 and spaces centers by 24.
    title = []
    for i, code in enumerate(image[0x25590 + 0x2118:0x25590 + 0x2122]):
        sprite = struct.unpack_from('<H', image, main + 0x1c + code * 2)[0]
        last_y, last_x = struct.unpack_from('<HH', image, main + sprite + 4)
        title.append(dict(x=50 + i * 24 - (last_x >> 1), y=20 - (last_y >> 1), color=15, sprite=sprite, text=''))
    particles = []
    bases = struct.unpack_from('<4H', image, 0x25590 + 0x909)
    for bp in range(31, -1, -1):
        sine = int.from_bytes(image[0x25590 + 0x4141 + (bp * 8 & 255):][:1], 'little', signed=True)
        base = bases[(bp >> 3) & 3]
        frames = [struct.unpack_from('<H', image, main + 0x1c + (base + frame) * 2)[0] for frame in (0, 1, 6, 7)]
        # CS:4517-4545 has twelve SHL/RCL pairs: (bp + 1) / 16 pixels per IRQ.
        particles.append(dict(x=160 + (bp - 15) * 4, y=18 + (sine >> 3), velocity=(bp + 1) << 12, sprites=frames))
    (output / 'OriginalTitleLayout.json').write_text(json.dumps(dict(draws=title, particles=particles, height=32)), encoding='utf-8')
    def advance(c):
        p = struct.unpack_from('<H', font, 2 + ord(c) * 2)[0]
        return struct.unpack_from('<H', font, p + 2)[0] if p else 5
    def width(text):
        return sum(advance(c) for c in text)
    for name, offset in [('OriginalCredits', 0x117a), ('OriginalStory', 0x14e1), ('OriginalGuide', 0x1bc8)]:
        data = image[0x25590 + offset:]
        text, cursor, draws = [], 0, []
        x = y = margin = 0
        color = 15
        def letters(value, left, top):
            if draws and draws[-1]['sprite'] == -1 and draws[-1]['color'] == color and draws[-1]['y'] == top and draws[-1]['x'] + width(draws[-1]['text']) == left:
                draws[-1]['text'] += value
            else:
                draws.append(dict(x=left, y=top, color=color, sprite=-1, text=value))
        while cursor < len(data):
            c = data[cursor]
            cursor += 1
            if c == 92:
                command = chr(data[cursor])
                cursor += 1
                if command == 'e':
                    break
                if command == 'r':
                    text.append('\n')
                    x = margin
                    y += 21
                elif command == 'c':
                    color = data[cursor] & 15
                    cursor += 1
                elif command == 's':
                    index = int.from_bytes(data[cursor:cursor + 2], 'big') + 14
                    sprite = struct.unpack_from('<H', image, main + index * 2)[0]
                    last_y, last_x = struct.unpack_from('<HH', image, main + sprite + 4)
                    draws.append(dict(x=x, y=y + 4 - (last_y + 1) // 2, color=color, sprite=sprite, text=''))
                    x += last_x
                    cursor += 2
                elif command == 'g':
                    x = margin = data[cursor]
                    y += int.from_bytes(data[cursor + 1:cursor + 2], 'little', signed=True)
                    cursor += 2
                elif command == 'q':
                    end = data.index(0, cursor)
                    centered = data[cursor:end].decode('ascii')
                    letters(centered, 160 - width(centered) // 2, y)
                    text.append(centered)
                    cursor = end + 1
                else:
                    raise ValueError(f'[LLPRESENT] Unknown text command {command}')
            elif 32 <= c < 127:
                text.append(chr(c))
                end = cursor
                while data[end] not in (0, 32, 92):
                    end += 1
                word = data[cursor - 1:end].decode('ascii')
                if x + width(word) >= 304:
                    x = margin
                    y += 21
                    if c == 32:
                        continue
                letters(chr(c), x, y)
                x += advance(chr(c))
        (output / f'{name}.txt').write_text(''.join(text).strip(), encoding='utf-8')
        (output / f'{name}Layout.json').write_text(json.dumps(dict(draws=draws, height=max(y + 21, max(d['y'] + 32 for d in draws)))), encoding='utf-8')
    print('[LLPRESENT] Exported original bitmap font and ten-entry score table.')


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--analysis', type=Path, default=Path('artifacts/unity-web-input/analysis'))
    parser.add_argument('--source', type=Path, default=Path('artifacts/unity-web-input/game'))
    parser.add_argument('--output', type=Path, default=Path('standalone/Unity/Assets/Llamatron/Resources/Generated'))
    args = parser.parse_args()
    export(args.analysis, args.source, args.output)
