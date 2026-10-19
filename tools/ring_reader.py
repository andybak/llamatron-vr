"""Read our instrumented DOSBox-X process, without pausing guest execution.

Only the explicitly recorded emulator PID is opened. A hook signature and
relocated jump are checked before any address is accepted as guest memory.
"""
import ctypes
import json
import struct
from ctypes import wintypes
from pathlib import Path

from build_hook import MAGIC


class MemoryRegion(ctypes.Structure):
    _fields_ = [("base", ctypes.c_void_p), ("allocation_base", ctypes.c_void_p),
                ("allocation_protect", wintypes.DWORD), ("partition", wintypes.WORD),
                ("region_size", ctypes.c_size_t), ("state", wintypes.DWORD),
                ("protect", wintypes.DWORD), ("type", wintypes.DWORD)]


class RingReader:
    def __init__(self, pid, manifest_path=Path("artifacts/runtime/hook-manifest.json")):
        self.pid = pid
        self.manifest = json.loads(manifest_path.read_text())
        self.kernel = ctypes.WinDLL("kernel32", use_last_error=True)
        self.kernel.OpenProcess.argtypes = [wintypes.DWORD, wintypes.BOOL, wintypes.DWORD]
        self.kernel.OpenProcess.restype = ctypes.c_void_p
        self.kernel.ReadProcessMemory.argtypes = [ctypes.c_void_p, ctypes.c_void_p,
                                                  ctypes.c_void_p, ctypes.c_size_t,
                                                  ctypes.POINTER(ctypes.c_size_t)]
        self.kernel.VirtualQueryEx.argtypes = [ctypes.c_void_p, ctypes.c_void_p,
                                               ctypes.POINTER(MemoryRegion), ctypes.c_size_t]
        self.kernel.VirtualQueryEx.restype = ctypes.c_size_t
        self.kernel.CloseHandle.argtypes = [ctypes.c_void_p]
        self.handle = self.kernel.OpenProcess(0x410, False, pid)
        if not self.handle:
            raise OSError(ctypes.get_last_error(), "Cannot read our DOSBox-X process")
        self.ring_address = None
        self.guest_address = None
        self.game_segment = None
        self.last_frame = None

    def read(self, address, size):
        buffer = ctypes.create_string_buffer(size)
        count = ctypes.c_size_t()
        if not self.kernel.ReadProcessMemory(self.handle, address, buffer, size, ctypes.byref(count)):
            raise OSError(ctypes.get_last_error(), f"Cannot read emulator address {address:#x}")
        if count.value != size:
            raise OSError("Short emulator memory read")
        return buffer.raw

    def locate(self):
        address, inspected = 0, 0
        region = MemoryRegion()
        while self.kernel.VirtualQueryEx(self.handle, address, ctypes.byref(region), ctypes.sizeof(region)):
            base, size = region.base or 0, region.region_size
            if not size or base + size <= address:
                break
            address = base + size
            if region.state != 0x1000 or region.type != 0x20000 or region.protect & 0x100:
                continue
            if region.protect & 0xFF not in (4, 8, 0x40, 0x80) or size < 65536 or size > 128 * 1024 * 1024:
                continue
            for start in range(base, base + size, 4 * 1024 * 1024):
                length = min(4 * 1024 * 1024 + 16, base + size - start)
                try:
                    data = self.read(start, length)
                except OSError:
                    continue
                inspected += length
                position = data.find(MAGIC)
                while position >= 0:
                    ring = start + position - self.manifest["magic_offset"]
                    try:
                        hook = self.read(ring, self.manifest["hook_size"])
                        if hook[:10] != bytes.fromhex("9c 50 53 51 52 56 57 55 1e 06"):
                            raise ValueError("Hook code was overwritten")
                        game = struct.unpack_from("<H", hook, len(hook) - 2)[0]
                        guest = ring - (game + self.manifest["hook_segment_relative"]) * 16
                        expected = struct.pack("<BHH", 0xEA, 0, game + self.manifest["hook_segment_relative"])
                        if self.read(guest + game * 16 + 0x8CBB, 5) == expected:
                            self.ring_address, self.guest_address, self.game_segment = ring, guest, game
                            return {"inspected_bytes": inspected, "game_segment": game,
                                    "ring_host_address": ring, "guest_host_address": guest}
                    except (OSError, ValueError, struct.error):
                        pass
                    position = data.find(MAGIC, position + 1)
                if inspected > 256 * 1024 * 1024:
                    raise LookupError("Hook not found in bounded emulator memory scan")
        raise LookupError("Game hook has not loaded yet")

    def snapshot(self, limit=None):
        if self.ring_address is None:
            self.locate()
        data = self.read(self.ring_address, 65536)
        if data[0xE0:0xF0] != MAGIC:
            raise ValueError("Hook signature changed; refusing stale addresses")
        cursor, total = struct.unpack_from("<HI", data, 0xF0)
        start = self.manifest["ring_start"]
        if cursor < start or cursor % 16:
            raise ValueError("Invalid ring cursor")
        count = min(total, self.manifest["capacity"])
        if limit is not None:
            count = min(count, limit)
        events = []
        for i in range(count):
            offset = start + ((cursor - start - count * 16 + i * 16) % (65536 - start))
            x, y, segment, sprite, frame, caller, page, ds = struct.unpack_from("<8H", data, offset)
            bank = {0x118C: "main", 0x20EB: "g3"}.get((segment - self.game_segment) & 65535, "unknown")
            events.append({"order": total - count + i, "x": x if x < 32768 else x - 65536,
                           "kind": ("begin" if sprite == 1 else "end") if segment == 65535 and sprite in (1, 2) else "draw",
                           "y": y if y < 32768 else y - 65536, "bank": bank,
                           "sprite_id": f"{bank}-{sprite:04x}", "frame_counter": frame,
                           "caller_ip": caller, "draw_page": page, "data_segment": ds,
                           "native_effect": ds == 65535 and self.manifest.get("framebuffer", {}).get("layer") == "transformed-sprites"})
        return {"total_events": total, "game_segment": self.game_segment, "events": events}

    def frame(self):
        snapshot = self.snapshot()
        events = snapshot.pop("events")
        end, selected = None, []
        for event in reversed(events):
            marker = event["kind"] != "draw"
            if end is None:
                if marker and event["kind"] == "end":
                    end = event
                continue
            if marker:
                if event["kind"] == "begin" and event["frame_counter"] == end["frame_counter"]:
                    selected.reverse()
                    self.last_frame = {"frame_key": [end["frame_counter"], end["order"]],
                                       "sprites": selected, "status": "live"}
                break
            if event["frame_counter"] != end["frame_counter"]:
                break
            if event["draw_page"] == end["draw_page"]:
                selected.append(event)
        snapshot.update(self.last_frame or {"frame_key": None, "sprites": [], "status": "waiting"})
        return snapshot

    def input(self, bits, scan=None):
        if self.ring_address is None:
            self.locate()
        if not 0 <= bits <= 255 or scan is not None and not 0 <= scan <= 255:
            raise ValueError("Invalid emulated keyboard state")
        if self.read(self.ring_address + 0xE0, 16) != MAGIC:
            raise ValueError("Hook no longer present")
        handle = self.kernel.OpenProcess(0x438, False, self.pid)
        if not handle:
            raise OSError(ctypes.get_last_error(), "Cannot update our emulated input state")
        self.kernel.WriteProcessMemory.argtypes = [ctypes.c_void_p, ctypes.c_void_p,
                                                   ctypes.c_void_p, ctypes.c_size_t,
                                                   ctypes.POINTER(ctypes.c_size_t)]
        address = self.guest_address + (self.game_segment + 0x2559) * 16 + 0x588
        data = bytes([bits]) if scan is None else bytes([scan, bits])
        if scan is not None:
            address -= 1
        count = ctypes.c_size_t()
        try:
            if not self.kernel.WriteProcessMemory(handle, address, data, len(data), ctypes.byref(count)):
                raise OSError(ctypes.get_last_error(), "Cannot set emulated keyboard flags")
            if count.value != len(data):
                raise OSError("Short emulated keyboard write")
        finally:
            self.kernel.CloseHandle(handle)

    def close(self):
        if self.handle:
            self.kernel.CloseHandle(self.handle)
            self.handle = None


if __name__ == "__main__":
    state = json.loads(Path("artifacts/runtime/bridge-state.json").read_text())
    reader = RingReader(state["emulator_pid"])
    try:
        print(json.dumps(reader.locate()))
        snapshot = reader.snapshot()
        Path("artifacts/runtime/ring-snapshot.json").write_text(json.dumps(snapshot, indent=2), encoding="utf-8")
        print(f"Read {len(snapshot['events'])} records; total draws {snapshot['total_events']}")
    finally:
        reader.close()
