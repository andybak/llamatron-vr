"""Measure DOS IRQ and drawing clocks in an owned, isolated Windows DOSBox session."""
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
    parser.add_argument("--seconds", type=float, default=5)
    args = parser.parse_args()
    if not 1 <= args.seconds <= 60:
        parser.error("Measurement duration must be between 1 and 60 seconds")
    source = args.source.resolve()
    missing = [name for name in REQUIRED_FILES if not (source / name).is_file()]
    if missing:
        parser.error(f"Incomplete source fixture: {missing}")
    runtime = args.runtime or ROOT / "artifacts/reconstruction" / f"reference-clock-{time.strftime('%Y%m%dT%H%M%S', time.gmtime())}"
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
            (runtime / "initial-random.bin").write_bytes(reader.read(data + 0x6ac9, 512))
            (runtime / "initial-data-segment.bin").write_bytes(reader.read(data, 65536))
            initial = {"irq": word(0x39e6), "wave_index": word(0x21aa), "lives": word(0x21a6),
                       "update_callback": word(0x39e4), "random_index": word(0x39e9),
                       "irq_busy": reader.read(data + 0x428, 1)[0]}
            command("EXEC RUN")
            samples = []
            start = time.monotonic()
            while time.monotonic() - start < args.seconds:
                now = time.monotonic()
                irq = word(0x39e6)
                draw = struct.unpack("<H", reader.read(reader.ring_address + manifest["frame_sequence_offset"], 2))[0]
                samples.append({"seconds": now - start, "irq": irq, "draw": draw, "outcome": word(0x810)})
                time.sleep(.005)
            command("BREAK")
            first, last = samples[0], samples[-1]
            elapsed = last["seconds"] - first["seconds"]
            report = {"original_sha256": hashlib.sha256(original).hexdigest(), "emulator_pid": state["emulator_pid"],
                      "pit_and_irq_bytes_unchanged": True, "instrumentation": "sprite ring and draw-begin/end markers; no framebuffer copy",
                      "headless_video": "SDL dummy", "emulator_cycles": 12000, "seconds": elapsed,
                      "initial": initial,
                      "irq_hz": ((last["irq"] - first["irq"]) & 65535) / elapsed,
                      "draw_hz": ((last["draw"] - first["draw"]) & 65535) / elapsed,
                      "samples": samples}
            (runtime / "clock-report.json").write_text(json.dumps(report, indent=2))
            (runtime / "final-entities.bin").write_bytes(reader.read(data + 0x48c1, 70 * 64))
            (runtime / "final-data-segment.bin").write_bytes(reader.read(data, 65536))
            print(f"[LLREFERENCE] IRQ={report['irq_hz']:.3f} Hz; draw={report['draw_hz']:.3f} Hz; elapsed={elapsed:.3f}s; samples={len(samples)}")
            print(f"[LLREFERENCE] Retained {runtime / 'clock-report.json'}")
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
        print("[LLREFERENCE] Closed only this probe's owned process job.")


if __name__ == "__main__":
    main()
