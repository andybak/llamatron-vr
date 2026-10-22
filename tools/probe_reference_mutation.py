"""Verify a controlled state-19 wave-script mutation in an owned DOSBox session."""
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
    runtime = args.runtime or ROOT / "artifacts/reconstruction" / f"reference-mutation-{time.strftime('%Y%m%dT%H%M%S', time.gmtime())}"
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
    configuration[2], configuration[4] = ord("K"), ord("N")
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
            regions = [(0x1232, 0x12c6), (0x256e, 0x25ae), (0x432b, 0x4374)]
            for begin, end in regions:
                assert reader.read(reader.guest_address + cs * 16 + begin, end - begin) == baseline[begin:end]
            before = reader.read(data + 0x256e, 16)
            assert before == baseline[0x25590 + 0x256e:0x25590 + 0x257e]
            pool = bytearray(70 * 64)
            for index in range(70):
                struct.pack_into('<h', pool, index * 64, -1)
            actor = {0: 0x1313, 2: 100, 4: 80, 6: 13, 0x0c: 137, 0x10: 1,
                     0x24: 1, 0x2e: 1, 0x30: -1, 0x3a: 10}
            for offset, value in actor.items():
                struct.pack_into('<h', pool, offset, value)
            for offset in range(0, len(pool), 128):
                write(0x48c1 + offset, pool[offset:offset + 128])
            for offset, value in {0x8fc: 100, 0x7f3: 10000, 0x634: 10000,
                                  0x62f: 1, 0x810: 0}.items():
                write(offset, struct.pack('<h', value))
            (runtime / 'before-data-segment.bin').write_bytes(reader.read(data, 65536))
            command('EXEC RUN')
            time.sleep(args.seconds)
            command('BREAK')
            after = reader.read(data + 0x256e, 16)
            final_pool = reader.read(data + 0x48c1, 70 * 64)
            report = {'source_sha256': hashlib.sha256(original).hexdigest(),
                      'setup': 'One synthetic state-19 actor in the unmodified normal dispatcher; freeze 100; one child quota',
                      'regions_verified': [[hex(begin), hex(end)] for begin, end in regions],
                      'wave22_start_ds': '256e', 'affected_ds': ['2576', '2578'],
                      'wave22_before': list(before), 'wave22_after': list(after),
                      'child_state': struct.unpack_from('<h', final_pool, 64)[0],
                      'irq_after': word(0x39e6), 'confirmed_mutation': before[8:12] != bytes(4) and after[8:12] == bytes(4)}
            (runtime / 'mutation-report.json').write_text(json.dumps(report, indent=2))
            (runtime / 'after-data-segment.bin').write_bytes(reader.read(data, 65536))
            (runtime / 'after-pool.bin').write_bytes(final_pool)
            if not report['confirmed_mutation']:
                raise RuntimeError('The controlled actor did not reproduce the suspected script mutation')
            print(f"[LLMUTATION] Confirmed DS2576/2578 changed {before[8:12].hex()} -> {after[8:12].hex()}; child={report['child_state']:04x}")
            print(f"[LLMUTATION] Retained {runtime / 'mutation-report.json'}")

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
        print("[LLREFERENCE] Closed only this mutation probe's owned process job.")


if __name__ == "__main__":
    main()
