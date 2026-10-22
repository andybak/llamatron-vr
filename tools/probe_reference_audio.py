"""Capture the original loaded Sound Blaster bank in an owned DOSBox session."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import shutil
import struct
import subprocess
import sys
import time

ROOT = Path(__file__).resolve().parent.parent
sys.path.insert(0, str(ROOT / "tools"))
from build_hook import build
from debug_command import execute
from game_session import OwnedProcesses, REQUIRED_FILES, wait_for
from ring_reader import RingReader


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--source", type=Path, default=ROOT / ".tools/ci-game-input")
    parser.add_argument("--emulator", type=Path, default=ROOT / ".tools/dosbox-x/mingw-build/mingw-sdl2/dosbox-x.exe")
    parser.add_argument("--runtime", type=Path, help="New output directory; existing directories are refused")
    parser.add_argument("--seconds", type=float, default=1)
    args = parser.parse_args()
    if not 1 <= args.seconds <= 60:
        parser.error("Probe duration must be between 1 and 60 seconds")
    source = args.source.resolve()
    missing = [name for name in REQUIRED_FILES if not (source / name).is_file()]
    if missing:
        parser.error(f"Incomplete source fixture: {missing}")
    runtime = args.runtime or ROOT / "artifacts/reconstruction" / f"reference-audio-{time.strftime('%Y%m%dT%H%M%S', time.gmtime())}"
    runtime = runtime.resolve()
    runtime.parent.mkdir(parents=True, exist_ok=True)
    runtime.mkdir(exist_ok=False)
    game = runtime / "game"; game.mkdir()
    for path in source.iterdir():
        if path.is_file() and path.suffix.lower() != ".lnk":
            shutil.copy2(path, game / path.name)
    original = (source / "GAME86.EXE").read_bytes()
    patched, manifest = build(original)
    header = struct.unpack_from("<H", patched, 8)[0] * 16
    baseline = (ROOT / "artifacts/analysis/GAME86.image.bin").read_bytes()
    assert patched[header + 0xE543:header + 0xE550] == baseline[0xE543:0xE550]
    assert patched[header + 0x3DE4:header + 0x3EE0] == baseline[0x3DE4:0x3EE0]
    (game / "GAME86.EXE").write_bytes(patched)
    (runtime / "hook-manifest.json").write_text(json.dumps(manifest, indent=2))
    configuration = bytearray((game / "LLAMA.INF").read_bytes())
    configuration[2], configuration[4] = ord("K"), ord("S")
    (game / "LLAMA.INF").write_bytes(configuration)
    owned = OwnedProcesses(); reader = None; command = None
    try:
        with (runtime / "bridge-launch.log").open("wb") as output:
            environment = dict(os.environ, SDL_VIDEODRIVER="dummy", SDL_AUDIODRIVER="dummy")
            bridge = owned.spawn([sys.executable, str(ROOT / "tools/debug_bridge.py"), "--runtime", str(runtime),
                                  "--exe", str(args.emulator.resolve())],
                                 stdout=output, stderr=subprocess.STDOUT,
                                 creationflags=subprocess.CREATE_NO_WINDOW, env=environment)

            def connected():
                if bridge.poll() is not None:
                    raise RuntimeError(f"Reference bridge exited; inspect {runtime / 'bridge-launch.log'}")
                state = json.loads((runtime / "bridge-state.json").read_text())
                if state["bridge_pid"] != bridge.pid:
                    raise LookupError("Waiting for owned bridge")
                if "PONG" not in execute("PING", runtime):
                    raise LookupError("Waiting for debugger")
                return state

            state = wait_for(connected, seconds=45)
            command = lambda text: execute(text, runtime)
            time.sleep(1); command("BREAK")

            def loading():
                if " FS" not in command("EXEC DOS MCBS"):
                    command("EXEC RUN"); time.sleep(.2); command("BREAK")
                    raise LookupError("Waiting for loading screen")
                return True

            wait_for(loading, seconds=45)
            command("EXEC SM 0040:001A 1E 00 20 00 20 39")
            command("EXEC RUN")
            reader = RingReader(state["emulator_pid"], runtime / "hook-manifest.json")
            wait_for(reader.locate, seconds=45)
            command("BREAK")
            for _ in range(2):
                reader.input(128, 0x39); command("EXEC RUN"); time.sleep(.35); command("BREAK")
                reader.input(0, 0xB9); command("EXEC RUN"); time.sleep(.15); command("BREAK")
            data = reader.guest_address + (reader.game_segment + 0x2559) * 16
            word = lambda offset: struct.unpack("<H", reader.read(data + offset, 2))[0]
            if word(0x6c4) != 0:
                raise RuntimeError("Reference did not enter normal play")
            cs = reader.game_segment
            ds = cs + 0x2559
            def write(offset, payload):
                command(f"EXEC SM {ds:04X}:{offset:04X} {' '.join(f'{value:02X}' for value in payload)}")
            offset, segment = struct.unpack('<HH', reader.read(data + 0x400, 4))
            if offset != 0 or segment == cs:
                raise RuntimeError(f'Sound Blaster driver is not active: callback {segment:04x}:{offset:04x}')
            driver_file = (source / 'SBLASTER.BIN').read_bytes()
            bank_file = (source / 'LLATRON.SPL').read_bytes()
            address = reader.guest_address + segment * 16
            driver = reader.read(address, len(driver_file))
            for first, last in [(0x76, 0xa1), (0x10f, 0x135), (0x609, 0x75d)]:
                assert driver[first:last] == driver_file[first:last]
            descriptors = [struct.unpack_from('<IHHH', driver, 0x609 + index * 10) for index in range(2, 34)]
            end = max(start + length for start, length, _, _ in descriptors)
            samples = reader.read(address + len(driver_file), end)
            assert samples[:len(bank_file)] == bank_file
            allocation = reader.read(address - 16, 16)
            (runtime / 'driver-resident.bin').write_bytes(driver)
            (runtime / 'sample-memory.bin').write_bytes(samples)
            (runtime / 'sample-tail.bin').write_bytes(samples[len(bank_file):])
            (runtime / 'driver-mcb.bin').write_bytes(allocation)
            (runtime / 'data-segment.bin').write_bytes(reader.read(data, 65536))
            report = {'source_sha256': hashlib.sha256(original).hexdigest(),
                      'setup': 'Owned fixture configured for Sound Blaster; unmodified loader and driver; preserving graphics hook',
                      'driver_segment': segment, 'driver_entry_offset': offset,
                      'driver_sha256': hashlib.sha256(driver_file).hexdigest(),
                      'bank_sha256': hashlib.sha256(bank_file).hexdigest(),
                      'bank_memory_matches_file': True, 'bank_bytes': len(bank_file),
                      'declared_end': end, 'overread_bytes': end - len(bank_file),
                      'tail_sha256': hashlib.sha256(samples[len(bank_file):]).hexdigest(),
                      'tail_nonzero_bytes': sum(value != 0 for value in samples[len(bank_file):]),
                      'allocation_paragraphs': struct.unpack_from('<H', allocation, 3)[0],
                      'driver_api_max': driver[0xa1], 'cl_rate_table': list(driver[0x3d2:0x3d7]),
                      'driver_sample_id': driver[0xb8], 'driver_priority': driver[0x2af]}
            (runtime / 'audio-report.json').write_text(json.dumps(report, indent=2))
            print(f"[LLAUDIO] Verified resident sample bank; captured {report['overread_bytes']} tail bytes, {report['tail_nonzero_bytes']} nonzero.")
            print(f"[LLAUDIO] Retained {runtime / 'audio-report.json'}")

    except Exception:
        if command is not None:
            for label, request in [("mcbs", "EXEC DOS MCBS"), ("screen", "EXEC D B800:0000")]:
                try:
                    (runtime / f"failure-{label}.txt").write_text(command(request))
                except Exception as diagnostic:
                    (runtime / f"failure-{label}.txt").write_text(str(diagnostic))
        raise
    finally:
        if reader is not None:
            reader.close()
        owned.close()
        print("[LLREFERENCE] Closed only this audio probe's owned process job.")


if __name__ == "__main__":
    main()
