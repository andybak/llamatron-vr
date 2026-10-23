"""Export the original bitmap font and initial high-score table for the player."""
import argparse
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
    for name, offset in [('OriginalCredits', 0x117a), ('OriginalStory', 0x14e1)]:
        data = image[0x25590 + offset:]
        text, cursor = [], 0
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
                elif command == 'c':
                    cursor += 1
                elif command in ('s', 'g'):
                    cursor += 2  # Inline sprite or spacing command; prose retained.
                elif command != 'q':
                    raise ValueError(f'[LLPRESENT] Unknown text command {command}')
            elif 32 <= c < 127:
                text.append(chr(c))
        (output / f'{name}.txt').write_text(''.join(text).strip(), encoding='utf-8')
    print('[LLPRESENT] Exported original bitmap font and ten-entry score table.')


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--analysis', type=Path, default=Path('artifacts/unity-web-input/analysis'))
    parser.add_argument('--source', type=Path, default=Path('artifacts/unity-web-input/game'))
    parser.add_argument('--output', type=Path, default=Path('standalone/Unity/Assets/Llamatron/Resources/Generated'))
    args = parser.parse_args()
    export(args.analysis, args.source, args.output)
