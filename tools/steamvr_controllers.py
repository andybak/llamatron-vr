"""Read both controller roles from SteamVR without taking compositor focus.

Uses Valve's IVRSystem_026 C function table from headers/openvr_capi.h.
No bindings, runtime settings, haptics or tracked-device state are changed.
"""
import ctypes
import json
import os
import time
import urllib.request
from pathlib import Path


class Axis(ctypes.Structure):
    _fields_ = [("x", ctypes.c_float), ("y", ctypes.c_float)]


class ControllerState(ctypes.Structure):
    _fields_ = [("packet", ctypes.c_uint32), ("pressed", ctypes.c_uint64),
                ("touched", ctypes.c_uint64), ("axes", Axis * 5)]


class SteamVRControllers:
    def __init__(self):
        logs = Path(__file__).resolve().parent.parent / "artifacts/runtime/steamvr-logs"
        logs.mkdir(parents=True, exist_ok=True)
        os.environ["VR_LOG_PATH"] = str(logs)
        path = Path(r"C:\Program Files (x86)\Steam\steamapps\common\SteamVR\bin\win64\openvr_api.dll")
        self.dll = ctypes.CDLL(str(path))
        self.dll.VR_InitInternal.argtypes = [ctypes.POINTER(ctypes.c_int), ctypes.c_int]
        self.dll.VR_InitInternal.restype = ctypes.c_size_t
        self.dll.VR_GetGenericInterface.argtypes = [ctypes.c_char_p, ctypes.POINTER(ctypes.c_int)]
        self.dll.VR_GetGenericInterface.restype = ctypes.c_void_p
        error = ctypes.c_int()
        self.dll.VR_InitInternal(ctypes.byref(error), 3)  # VRApplication_Background
        if error.value:
            raise RuntimeError(f"SteamVR background initialization failed: {error.value}")
        table = self.dll.VR_GetGenericInterface(b"FnTable:IVRSystem_026", ctypes.byref(error))
        if error.value or not table:
            self.close()
            raise RuntimeError(f"SteamVR controller interface unavailable: {error.value}")
        pointers = ctypes.cast(table, ctypes.POINTER(ctypes.c_void_p))
        bind = lambda slot, result, *args: ctypes.WINFUNCTYPE(result, *args)(pointers[slot])
        self.role_index = bind(18, ctypes.c_uint32, ctypes.c_int)
        self.connected = bind(21, ctypes.c_bool, ctypes.c_uint32)
        self.int_property = bind(24, ctypes.c_int32, ctypes.c_uint32, ctypes.c_int, ctypes.POINTER(ctypes.c_int))
        self.controller_state = bind(37, ctypes.c_bool, ctypes.c_uint32, ctypes.POINTER(ControllerState), ctypes.c_uint32)

    def snapshot(self):
        sources = []
        for hand, role in [("left", 1), ("right", 2)]:
            index = self.role_index(role)
            if index >= 64 or not self.connected(index):
                continue
            state = ControllerState()
            if not self.controller_state(index, ctypes.byref(state), ctypes.sizeof(state)):
                continue
            axis = None
            for slot in range(5):
                error = ctypes.c_int()
                kind = self.int_property(index, 3002 + slot, ctypes.byref(error))
                if not error.value and kind == 2:  # k_eControllerAxis_Joystick
                    axis = state.axes[slot]
                    break
            if axis is None:
                continue
            sources.append({"handedness": hand, "device_index": index, "packet": state.packet,
                            "gamepad": {"mapping": "xr-standard", "axes": [0, 0, axis.x, -axis.y],
                                        "buttons": [{"pressed": bool(state.pressed & (1 << 33))}]}})
        return sources

    def close(self):
        self.dll.VR_ShutdownInternal()


def run_bridge():
    import msvcrt
    root = Path(__file__).resolve().parent.parent
    runtime = root / "artifacts/runtime"
    # Prevent duplicate helpers without changing or stopping any existing process.
    with (runtime / "controller-helper.lock").open("a+b") as lock:
        lock.seek(0)
        try:
            msvcrt.locking(lock.fileno(), msvcrt.LK_NBLCK, 1)
        except OSError:
            return
        controllers = None
        next_connect = 0
        while True:
            now = time.monotonic()
            result = {"sources": [], "error": None}
            try:
                if controllers is None and now >= next_connect:
                    next_connect = now + 5
                    controllers = SteamVRControllers()
                if controllers is not None:
                    result["sources"] = controllers.snapshot()
            except Exception as error:
                result["error"] = str(error)
            try:
                request = urllib.request.Request("http://127.0.0.1:53443/controllers",
                    data=json.dumps(result).encode(), headers={"Content-Type": "application/json"})
                with urllib.request.urlopen(request, timeout=1) as response:
                    response.read()
            except (OSError, ValueError):
                pass
            time.sleep(.02 if controllers is not None else 1)


if __name__ == "__main__":
    import sys
    if "--probe" in sys.argv:
        controllers = SteamVRControllers()
        try:
            print(json.dumps(controllers.snapshot()))
        finally:
            controllers.close()
    else:
        run_bridge()
