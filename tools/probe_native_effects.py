"""Launch a separate silent diagnostic game; leave the user's VR prototype alone."""
import argparse
import json
import shutil
import subprocess
import sys
import time
from pathlib import Path

from build_hook import build
from capture_native_frame import decode_planes, read_frame
from debug_command import execute
from ring_reader import RingReader


def wait_for(function, seconds=15):
    deadline = time.monotonic() + seconds
    error = None
    while time.monotonic() < deadline:
        try:
            return function()
        except (OSError, ValueError, LookupError, KeyError) as caught:
            error = caught
            time.sleep(.15)
    raise RuntimeError(f"Startup timed out: {error}")


def spawn(script, arguments, runtime, log_name):
    with (runtime / log_name).open("ab") as log:
        return subprocess.Popen([sys.executable, str(script), *arguments],
                                stdout=log, stderr=subprocess.STDOUT,
                                creationflags=subprocess.CREATE_NO_WINDOW)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--effects", action="store_true", help="Capture isolated native transformed draws")
    parser.add_argument("--audio", action="store_true", help="Enable original Sound Blaster audio")
    args = parser.parse_args()
    root = Path(__file__).resolve().parent.parent
    runtime = root / "artifacts/effects-probe"
    game = runtime / "game"
    if (runtime / "bridge-state.json").exists():
        try:
            if "PONG" in execute("PING", runtime):
                raise RuntimeError("An effects probe is already running; inspect its state before replacing it")
        except OSError:
            pass
    game.mkdir(parents=True, exist_ok=True)
    original = Path(r"F:\Emulation\DOSBox\games\llama")
    for source in original.iterdir():
        if source.is_file() and source.suffix.lower() != ".lnk" and source.name not in ("stdout.txt", "stderr.txt"):
            shutil.copy2(source, game / source.name)
    configuration = bytearray((game / "LLAMA.INF").read_bytes())
    configuration[2], configuration[4] = ord("K"), ord("S" if args.audio else "N")
    (game / "LLAMA.INF").write_bytes(configuration)
    executable, manifest = build((original / "GAME86.EXE").read_bytes(), framebuffer=True, effects=args.effects)
    (game / "GAME86.EXE").write_bytes(executable)
    (runtime / "hook-manifest.json").write_text(json.dumps(manifest, indent=2))
    exe = root / ".tools/dosbox-x/mingw-build/mingw-sdl2/dosbox-x.exe"
    spawn(root / "tools/debug_bridge.py", ["--runtime", str(runtime), "--exe", str(exe)], runtime, "bridge-launch.log")
    command = lambda text: execute(text, runtime)
    wait_for(lambda: command("PING"))
    command("BREAK")
    def loading_screen():
        if " FS" not in command("EXEC DOS MCBS"):
            command("EXEC RUN")
            time.sleep(.2)
            command("BREAK")
            raise LookupError("Waiting for diagnostic loading screen")
        return True
    wait_for(loading_screen)
    command("EXEC SM 0040:001A 1E 00 20 00 20 39")
    command("EXEC RUN")
    state = json.loads((runtime / "bridge-state.json").read_text())
    reader = RingReader(state["emulator_pid"], runtime / "hook-manifest.json")
    try:
        wait_for(reader.locate)
        command("BREAK")
        for _ in range(2):
            reader.input(128, 0x39)
            command("EXEC RUN")
            time.sleep(.35)
            command("BREAK")
            reader.input(0, 0xB9)
            command("EXEC RUN")
            time.sleep(.15)
            command("BREAK")
        planes, info = read_frame(reader)
        palette = info["palette"]
        (runtime / "palette.json").write_text(json.dumps(palette, indent=2))
        decode_planes(planes, palette).save(runtime / "native-frame.png")
        (runtime / "native-frame.planes.bin").write_bytes(planes)
        print(f"Separate probe paused at original frame {info['sequence']}; emulator PID {state['emulator_pid']}")
    finally:
        reader.close()


if __name__ == "__main__":
    main()
