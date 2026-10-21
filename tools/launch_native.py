"""Setup preparation and owned native play; no browser or manual debugger steps."""
import argparse
import json
import os
from pathlib import Path
import subprocess
import sys

from game_session import CONFIG, ROOT, RUNTIME, GameSession, InstallLock, prepare_game


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--prepare", action="store_true")
    parser.add_argument("--game", type=Path)
    parser.add_argument("--emulator", type=Path)
    parser.add_argument("--smoke", action="store_true", help="Silent offscreen integration check")
    parser.add_argument("--mute", action="store_true")
    args = parser.parse_args()
    if not args.prepare:
        (ROOT / "artifacts").mkdir(exist_ok=True)
        try:
            with (ROOT / "artifacts/setup.lock").open("a+b"):
                pass
        except PermissionError:
            raise RuntimeError("Setup is still running; wait for it to finish before playing") from None
    with InstallLock():
        if args.prepare:
            if not args.game or not args.emulator:
                parser.error("--prepare needs --game and --emulator")
            print("[LLSETUP] Preparing an isolated patched game copy", flush=True)
            prepare_game(args.game)
            with GameSession(args.emulator):
                print("[LLSETUP] Original palette captured; silent setup emulator is closing", flush=True)
            return 0
        if not CONFIG.is_file():
            raise RuntimeError("Run Setup.cmd first")
        configuration = json.loads(CONFIG.read_text(encoding="utf-8-sig"))
        emulator = ROOT / configuration["emulator"]
        executable = ROOT / configuration["native"]
        if not emulator.is_file() or not executable.is_file():
            raise RuntimeError("Installation is incomplete; run Setup.cmd again")
        logs = ROOT / "artifacts/native-xr-logs"
        logs.mkdir(exist_ok=True)
        environment = dict(os.environ, VR_LOG_PATH=str(logs), XR_LOADER_DEBUG="error")
        print("[LLPLAY] Starting original game", flush=True)
        with GameSession(emulator, audio=not (args.mute or args.smoke)) as game:
            game.command("EXEC RUN")
            arguments = [str(executable), "--runtime", str(RUNTIME)]
            if args.smoke:
                arguments += ["--smoke", "--snapshot", str(ROOT / "artifacts/native-launch-smoke.png")]
            else:
                arguments += ["--vr", "--control"]
            print("[LLPLAY] Launching native renderer; closing it also stops game audio", flush=True)
            with (ROOT / "artifacts/native-user-test.log").open("w") as log:
                native = game.processes.spawn(arguments, stdout=log, stderr=subprocess.STDOUT, env=environment)
                result = native.wait()
        print("[LLPLAY] Native session closed; emulator stopped", flush=True)
        if result:
            raise RuntimeError("Native renderer failed; see artifacts/native-user-test.log")
        return result


if __name__ == "__main__":
    try:
        sys.exit(main())
    except KeyboardInterrupt:
        print("\n[LLPLAY] Interrupted; owned processes stopped", file=sys.stderr)
        sys.exit(1)
    except Exception as error:
        print(f"[LLPLAY] {error}", file=sys.stderr)
        sys.exit(1)
