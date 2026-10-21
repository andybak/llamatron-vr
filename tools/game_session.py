"""Prepare and own one isolated DOS game session for setup or native play."""
import ctypes
from ctypes import wintypes
import json
import msvcrt
from pathlib import Path
import shutil
import subprocess
import sys
import time

from build_hook import build
from capture_native_frame import read_frame
from debug_command import execute
from ring_reader import RingReader

ROOT = Path(__file__).resolve().parent.parent
RUNTIME = ROOT / "artifacts/native-runtime"
CONFIG = ROOT / "artifacts/install.json"
REQUIRED_FILES = ("GAME86.EXE", "LLAMA.EXE", "LLAMA.INF", "FS.EXE", "G3.CMP",
                  "F.CMP", "S.CMP", "LLATRON.SPL", "SBLASTER.BIN")


def wait_for(function, seconds=30):
    deadline = time.monotonic() + seconds
    while True:
        try:
            return function()
        except (OSError, ValueError, LookupError, KeyError):
            if time.monotonic() >= deadline:
                raise
            time.sleep(.15)


class InstallLock:
    """OS lock shared by setup and play; automatically released after a crash."""
    def __enter__(self):
        RUNTIME.mkdir(parents=True, exist_ok=True)
        self.file = (RUNTIME / "session.lock").open("a+b")
        self.file.write(b"0")
        self.file.flush()
        self.file.seek(0)
        try:
            msvcrt.locking(self.file.fileno(), msvcrt.LK_NBLCK, 1)
        except OSError:
            self.file.close()
            raise RuntimeError("Setup or Play is already running for this installation") from None
        return self

    def __exit__(self, *_):
        self.file.seek(0)
        msvcrt.locking(self.file.fileno(), msvcrt.LK_UNLCK, 1)
        self.file.close()


class OwnedProcesses:
    """Closing this Windows job stops only the processes started for this session."""
    def __init__(self):
        class BasicLimits(ctypes.Structure):
            _fields_ = [("process_time", ctypes.c_int64), ("job_time", ctypes.c_int64),
                        ("flags", wintypes.DWORD), ("min_working_set", ctypes.c_size_t),
                        ("max_working_set", ctypes.c_size_t), ("process_limit", wintypes.DWORD),
                        ("affinity", ctypes.c_size_t), ("priority", wintypes.DWORD),
                        ("scheduling", wintypes.DWORD)]

        class IoCounters(ctypes.Structure):
            _fields_ = [(name, ctypes.c_uint64) for name in
                        ("read_ops", "write_ops", "other_ops", "read_bytes", "write_bytes", "other_bytes")]

        class Limits(ctypes.Structure):
            _fields_ = [("basic", BasicLimits), ("io", IoCounters),
                        ("process_memory", ctypes.c_size_t), ("job_memory", ctypes.c_size_t),
                        ("peak_process", ctypes.c_size_t), ("peak_job", ctypes.c_size_t)]

        self.api = ctypes.WinDLL("kernel32", use_last_error=True)
        self.api.CreateJobObjectW.argtypes = [ctypes.c_void_p, wintypes.LPCWSTR]
        self.api.CreateJobObjectW.restype = wintypes.HANDLE
        self.api.SetInformationJobObject.argtypes = [wintypes.HANDLE, ctypes.c_int, ctypes.c_void_p, wintypes.DWORD]
        self.api.SetInformationJobObject.restype = wintypes.BOOL
        self.api.AssignProcessToJobObject.argtypes = [wintypes.HANDLE, wintypes.HANDLE]
        self.api.AssignProcessToJobObject.restype = wintypes.BOOL
        self.api.CloseHandle.argtypes = [wintypes.HANDLE]
        self.handle = self.api.CreateJobObjectW(None, None)
        if not self.handle:
            raise ctypes.WinError(ctypes.get_last_error())
        limits = Limits()
        limits.basic.flags = 0x2000  # JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE
        if not self.api.SetInformationJobObject(self.handle, 9, ctypes.byref(limits), ctypes.sizeof(limits)):
            error = ctypes.WinError(ctypes.get_last_error())
            self.close()
            raise error

    def spawn(self, args, **kwargs):
        # Attach the new bridge immediately so the emulator it spawns inherits the job.
        process = subprocess.Popen(args, cwd=ROOT, **kwargs)
        if not self.api.AssignProcessToJobObject(self.handle, int(process._handle)):
            error = ctypes.WinError(ctypes.get_last_error())
            process.terminate()
            process.wait()
            raise error
        return process

    def close(self):
        if self.handle:
            self.api.CloseHandle(self.handle)
            self.handle = None


def prepare_game(source):
    source = source.resolve()
    missing = [name for name in REQUIRED_FILES if not (source / name).is_file()]
    if missing:
        raise RuntimeError(f"Incomplete DOS Llamatron folder {source}: missing {', '.join(missing)}")
    if source == RUNTIME / "game":
        raise RuntimeError("Choose the original game folder, not the patched runtime copy")
    game = RUNTIME / "game"
    game.mkdir(parents=True, exist_ok=True)
    for path in source.iterdir():
        if path.is_file() and path.suffix.lower() != ".lnk" and path.name.lower() not in ("stdout.txt", "stderr.txt"):
            # Keep scores and other user state across setup reruns.
            if not (game / path.name).exists():
                shutil.copy2(path, game / path.name)
    executable, manifest = build((source / "GAME86.EXE").read_bytes(), framebuffer=True, effects=True)
    (game / "GAME86.EXE").write_bytes(executable)
    (RUNTIME / "hook-manifest.json").write_text(json.dumps(manifest, indent=2))


class GameSession:
    def __init__(self, emulator, audio=False):
        self.emulator = Path(emulator)
        self.audio = audio
        self.processes = None
        self.bridge = None
        self.log = None
        self.emulator_pid = None

    def __enter__(self):
        self.processes = OwnedProcesses()
        try:
            configuration = bytearray((RUNTIME / "game/LLAMA.INF").read_bytes())
            configuration[2], configuration[4] = ord("K"), ord("S" if self.audio else "N")
            (RUNTIME / "game/LLAMA.INF").write_bytes(configuration)
            self.log = (RUNTIME / "bridge-launch.log").open("ab")
            self.bridge = self.processes.spawn(
                [sys.executable, str(ROOT / "tools/debug_bridge.py"), "--runtime", str(RUNTIME),
                 "--exe", str(self.emulator)], stdout=self.log, stderr=subprocess.STDOUT,
                creationflags=subprocess.CREATE_NO_WINDOW)

            def connected():
                if self.bridge.poll() is not None:
                    raise RuntimeError("Emulator startup failed; see artifacts/native-runtime/bridge-launch.log")
                state = json.loads((RUNTIME / "bridge-state.json").read_text())
                if state["bridge_pid"] != self.bridge.pid:
                    raise LookupError("Waiting for this session's bridge")
                self.emulator_pid = state["emulator_pid"]
                if "PONG" not in self.command("PING"):
                    raise LookupError("Waiting for emulator debugger")
                return state

            wait_for(connected)
            # The control socket is available during BIOS/video initialization,
            # before BREAK can be serviced by the emulation loop.
            time.sleep(1)
            self.command("BREAK")

            def loading_screen():
                if " FS" not in self.command("EXEC DOS MCBS"):
                    self.command("EXEC RUN")
                    time.sleep(.2)
                    self.command("BREAK")
                    raise LookupError("Waiting for game loading screen")

            wait_for(loading_screen)
            self.command("EXEC SM 0040:001A 1E 00 20 00 20 39")
            self.command("EXEC RUN")
            reader = RingReader(self.emulator_pid, RUNTIME / "hook-manifest.json")
            try:
                wait_for(reader.locate)
                self.command("BREAK")
                for _ in range(2):
                    reader.input(128, 0x39)
                    self.command("EXEC RUN")
                    time.sleep(.35)
                    self.command("BREAK")
                    reader.input(0, 0xB9)
                    self.command("EXEC RUN")
                    time.sleep(.15)
                    self.command("BREAK")
                # BREAK can land halfway through the framebuffer publication.
                # Keep emulation running until a coherent snapshot is available;
                # retrying the same paused, odd seqlock cannot make progress.
                self.command("EXEC RUN")
                _, info = wait_for(lambda: read_frame(reader))
                self.command("BREAK")
                (RUNTIME / "palette.json").write_text(json.dumps(info["palette"], indent=2))
            finally:
                reader.close()
            return self
        except BaseException:
            self.close()
            raise

    def command(self, command):
        return execute(command, RUNTIME)

    def close(self):
        if self.processes:
            self.processes.close()
            self.processes = None
        if self.bridge:
            self.bridge.wait(timeout=10)
        if self.log:
            self.log.close()

    def __exit__(self, *_):
        self.close()
