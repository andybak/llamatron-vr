"""Start the isolated DOS Llamatron + live diorama prototype from this workspace."""
import argparse
import json
import shutil
import subprocess
import sys
import time
import urllib.request
from pathlib import Path

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
    parser.add_argument("--game", type=Path, default=Path(r"F:\Emulation\DOSBox\games\llama"))
    parser.add_argument("--exe", type=Path, default=Path(".tools/dosbox-x/mingw-build/mingw-sdl2/dosbox-x.exe"))
    parser.add_argument("--audio", action="store_true", help="Enable the supplied Sound Blaster driver in the test copy")
    parser.add_argument("--viewer-port", type=int, default=53443)
    args = parser.parse_args()
    root = Path(__file__).resolve().parent.parent
    if Path.cwd().resolve() != root:
        raise RuntimeError(f"Run this command from {root}")
    runtime = root / "artifacts/runtime"
    runtime.mkdir(parents=True, exist_ok=True)
    viewer_state = runtime / "viewer-state.json"
    if viewer_state.exists():
        try:
            url = json.loads(viewer_state.read_text())["url"]
            with urllib.request.urlopen(f"{url}frame", timeout=2) as response:
                frame = json.load(response)
            if "total_events" in frame:
                print(f"Prototype already available: {url}")
                return
        except (OSError, ValueError, KeyError):
            pass
    try:
        bridge_running = "PONG" in execute("PING")
    except (OSError, ValueError):
        bridge_running = False
    if not bridge_running:
        if not args.exe.is_file() or not (args.game / "GAME86.EXE").is_file():
            raise RuntimeError("DOSBox-X or the supplied game is missing; pass --exe and --game with their locations")
        game = runtime / "game"
        game.mkdir(exist_ok=True)
        for source in args.game.iterdir():
            if source.is_file() and source.suffix.lower() != ".lnk" and source.name not in ("stdout.txt", "stderr.txt"):
                shutil.copy2(source, game / source.name)
        configuration = bytearray((game / "LLAMA.INF").read_bytes())
        if len(configuration) < 5:
            raise RuntimeError("Unknown LLAMA.INF format")
        configuration[2], configuration[4] = ord("K"), ord("S" if args.audio else "N")
        (game / "LLAMA.INF").write_bytes(configuration)
        subprocess.run([sys.executable, "tools/inspect_game.py", str(args.game)], check=True)
        subprocess.run([sys.executable, "tools/build_hook.py", str(args.game)], check=True)
        spawn(root / "tools/debug_bridge.py", ["--exe", str(args.exe)], runtime, "bridge-launch.log")
        wait_for(lambda: execute("PING"))
        execute("BREAK")
        # The first helper displays a loading image and waits on the BIOS key queue.
        def ready():
            mcbs = execute("EXEC DOS MCBS")
            if " FS" not in mcbs:
                execute("EXEC RUN")
                time.sleep(.2)
                execute("BREAK")
                raise LookupError("Waiting for the loading-screen helper")
            return True
        wait_for(ready)
        execute("EXEC SM 0040:001A 1E 00 20 00 20 39")
        execute("EXEC RUN")
        state = json.loads((runtime / "bridge-state.json").read_text())
        reader = RingReader(state["emulator_pid"])
        try:
            wait_for(reader.locate)
            execute("BREAK")
            for _ in range(2):
                reader.input(128, 0x39)
                execute("EXEC RUN")
                time.sleep(.35)
                execute("BREAK")
                reader.input(0, 0xB9)
                execute("EXEC RUN")
                time.sleep(.15)
                execute("BREAK")
            if reader.snapshot()["total_events"] == 0:
                raise RuntimeError("Game started but no draw events were captured")
        finally:
            reader.close()
        subprocess.run([sys.executable, "tools/capture_palette.py"], check=True)
        subprocess.run([sys.executable, "tools/export_sprites.py", "--palette", str(runtime / "palette.json")], check=True)
    execute("BREAK")
    spawn(root / "tools/viewer_server.py", ["--paused", "--port", str(args.viewer_port)], runtime, "viewer-launch.log")
    def viewer_ready():
        state = json.loads(viewer_state.read_text())
        with urllib.request.urlopen(f"{state['url']}frame", timeout=2) as response:
            json.load(response)
        return state["url"]
    print(f"Prototype ready, initially paused: {wait_for(viewer_ready)}")


if __name__ == "__main__":
    try:
        main()
    except Exception as error:
        print(f"Llamatron startup: {error}", file=sys.stderr)
        sys.exit(1)
