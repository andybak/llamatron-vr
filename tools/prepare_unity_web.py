"""Recreate Unity's generated assets from the checksum-pinned original archive."""

import argparse
import hashlib
import json
from pathlib import Path
import subprocess
import sys
from urllib.request import urlopen
import zipfile


ROOT = Path(__file__).resolve().parent.parent
ARCHIVE_URL = 'https://image.dosgamesarchive.com/games/llama.zip'
ARCHIVE_SHA256 = 'f6a96e8eb7775bee737fcd74701be8f89f55f95c2508a7c15c0958021bb976d2'


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--archive', type=Path, help='Use an already downloaded, checksum-matching archive')
    parser.add_argument('--output', type=Path, default=ROOT / 'standalone/Unity/Assets/Llamatron/Resources/Generated')
    args = parser.parse_args()
    work = ROOT / 'artifacts/unity-web-input'
    work.mkdir(parents=True, exist_ok=True)
    archive = args.archive or work / 'llama.zip'
    if args.archive is None:
        with urlopen(ARCHIVE_URL, timeout=120) as response:
            archive.write_bytes(response.read())
    if hashlib.sha256(archive.read_bytes()).hexdigest() != ARCHIVE_SHA256:
        raise ValueError('[LLWEB-CI] Original archive checksum mismatch')
    source = work / 'game'
    with zipfile.ZipFile(archive) as zipped:
        zipped.extractall(source)
    profile = json.loads((ROOT / 'tools/fixtures/unity-web-profile.json').read_text())
    tail = bytes.fromhex(profile['sample_tail_hex'])
    if len(tail) != 96 or hashlib.sha256(tail).hexdigest() != profile['sample_tail_sha256']:
        raise ValueError('[LLWEB-CI] Captured audio-tail profile is corrupt')
    memory = work / 'sample-memory.bin'
    memory.write_bytes((source / 'LLATRON.SPL').read_bytes() + tail)
    palette = work / 'palette.json'
    palette.write_text(json.dumps(profile['palette_rgb']))

    def run(tool, *arguments):
        subprocess.run([sys.executable, str(ROOT / 'tools' / tool), *map(str, arguments)], cwd=ROOT, check=True)

    analysis, sprites = work / 'analysis', work / 'sprites'
    run('inspect_game.py', source, '--output', analysis)
    run('export_sprites.py', '--analysis', analysis, '--output', sprites, '--palette', palette)
    run('export_standalone_assets.py', '--sprites', sprites, '--output', args.output)
    run('export_presentation_assets.py', '--analysis', analysis, '--source', source, '--output', args.output)
    # Keep the committed gameplay descriptors unchanged during asset preparation.
    run('recover_audio.py', '--source', source, '--reference-memory', memory,
        '--output', args.output / 'Audio', '--csharp', work / 'OriginalSoundData.cs',
        '--report', work / 'audio-assets.json')
    expected = ROOT / 'standalone/Unity/Assets/Llamatron/Core/OriginalSoundData.cs'
    if (work / 'OriginalSoundData.cs').read_text() != expected.read_text():
        raise ValueError('[LLWEB-CI] Recovered audio descriptors differ from committed data')
    print('[LLWEB-CI] Recreated sprite atlases and all 32 exact PCM samples.', flush=True)


if __name__ == '__main__':
    main()
