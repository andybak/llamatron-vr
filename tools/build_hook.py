"""Build a local DOS executable with a bounded sprite-event ring buffer.

The hook saves/restores registers and flags, records eight words per draw,
then executes the displaced comparison and returns to the original routine.
Original game files are read only. Runtime integration remains experimental.
"""
import argparse
import hashlib
import json
import struct
from pathlib import Path

from inspect_game import rebuild_executable, unpack_exepack, unpack_lz91

MAGIC = b"LLVRRINGv2\0\0\0\0\0\0"
RING_START = 0x400


def hook_code(displaced, return_ip, marker=None, capture=False, effects=False):
    code = bytearray.fromhex("9c 50 53 51 52 56 57 55 1e 06 8b ec fc 0e 07")
    if marker == 1:
        code.extend(bytes.fromhex("26 ff 06 f6 00"))
        if effects:
            code.extend(b"\xe8" + struct.pack("<h", 0x500 - (0x100 + len(code) + 3)))
    code.extend(bytes.fromhex("26 8b 3e f0 00"))
    if marker is None:
        for displacement in (16, 14, 0, 8):
            code.extend(bytes.fromhex("8b 46") + bytes([displacement]) + b"\xab")
    else:
        code.extend(bytes.fromhex("31 c0 ab ab b8 ff ff ab"))
        code.extend(struct.pack("<BH", 0xB8, marker) + b"\xab")
    code.extend(bytes.fromhex("26 a1 f6 00 ab"))
    code.extend(bytes.fromhex("8b 46 14 ab") if marker is None else bytes.fromhex("31 c0 ab"))
    code.extend(bytes.fromhex("a1 d7 39 ab") if marker is None else bytes.fromhex("a1 d3 39 ab"))
    code.extend(bytes.fromhex("8b 46 02"))
    if effects and marker is None:
        # Tag transformed draws captured on the shadow page, so the viewer can
        # omit their ordinary sprite reconstruction.
        code.extend(bytes.fromhex("80 3e d9 39 00 78 0e a1 d7 39 3b 06 d3 39 75 05 b8 ff ff eb 03 8b 46 02"))
    code.extend(b"\xab")
    code.extend(bytes.fromhex("0b ff 75 03 bf") + struct.pack("<H", 0x800 if effects else RING_START))
    code.extend(bytes.fromhex("26 89 3e f0 00 26 83 06 f2 00 01 26 83 16 f4 00 00"))
    if capture:
        code.extend(b"\xe8" + struct.pack("<h", 0x300 - (0x200 + len(code) + 3)))
    if effects and marker is None:
        code.extend(b"\xe8" + struct.pack("<h", 0x400 - (len(code) + 3)))
    code.extend(bytes.fromhex("07 1f 5d 5f 5e 5a 59 5b 58 9d"))
    code.extend(displaced)
    code.extend(struct.pack("<BHH", 0xEA, return_ip, 0))
    return code


def framebuffer_code(buffer_segment, effects=False):
    """Copy four original VGA planes; preserve GC index, read map and read mode.

    Called inside the frame-end hook's register/flags save. No drawing, page
    changes, simulation, or palette changes. F8..FE publish bank/lock/frame/page.
    """
    code = bytearray.fromhex("2e ff 06 fa 00 2e 81 36 f8 00 00 80")
    code.extend(bytes.fromhex("bb 00 60" if effects else "8b 1e d3 39"))
    code.extend(bytes.fromhex("2e 89 1e fe 00"))
    # Save graphics-controller selected index, Read Map Select and Mode.
    code.extend(bytes.fromhex("ba ce 03 ec 50 b0 04 ee 42 ec 50 4a b0 05 ee 42 ec 50 24 f7 ee 4a"))
    code.extend(bytes.fromhex("b8 00 a0 8e d8 b8") + struct.pack("<H", buffer_segment))
    relocation = len(code) - 2
    code.extend(bytes.fromhex("8e c0 2e 8b 3e f8 00 b8 04 00"))
    loop = len(code)
    code.extend(bytes.fromhex("ef 8b f3 b9 a0 0f f3 a5 fe c4 80 fc 04"))
    code.extend(bytes([0x72, (loop - len(code) - 2) & 255]))
    # The game feeds these sixteen RGB triples to BIOS DAC entries 0..7/16..23.
    # Capture them beside the pixels so palette-driven flashes are preserved.
    code.extend(bytes.fromhex("8b 46 02 8e d8 be 51 44 b9 18 00 f3 a5"))
    # Restore GC mode, read plane and original selected register.
    code.extend(bytes.fromhex("b0 05 ee 42 58 ee 4a b0 04 ee 42 58 ee 4a 58 ee"))
    code.extend(bytes.fromhex("2e a1 f6 00 2e a3 fc 00 2e ff 06 fa 00 c3"))
    return code, relocation


def effects_duplicate_code():
    code = bytearray.fromhex("80 3e d9 39 00 78 00 a1 d7 39 3b 06 d3 39 75 00 50 b8 00 00 8e c0")
    relocations = [18]
    code.extend(bytes.fromhex("26 ff 36 2f 67 26 c7 06 2f 67 00 00 c7 06 d7 39 00 60 55"))
    code.extend(bytes.fromhex("8b 46 10 8b 5e 0e 8b 76 08 8e 46 00 9a 10 00 00 00"))
    relocations.append(len(code) - 2)
    code.extend(bytes.fromhex("5d b8 00 00 8e c0 26 8f 06 2f 67 8f 06 d7 39 0e 07"))
    relocations.append(len(code) - 15)
    done = len(code)
    code.extend(b"\xc3")
    code[6], code[15] = done - 7, done - 16
    return code, relocations


def effects_clear_code():
    # Startup 0000:0010 is no longer executed by the time the first frame begins.
    # Install a far-call adapter to the original near-return sprite rasterizer.
    code = bytearray.fromhex("b8 00 00 8e c0 bf 10 00 fc")
    code.extend(bytes.fromhex("b8 80 3e ab b8 d9 39 ab b8 00 e8 ab b8 a8 8c ab b0 cb aa"))
    code.extend(bytes.fromhex("ba ce 03 ec 50"))
    registers = [(1, 0), (3, 0), (5, 0), (8, 255)]
    for index, value in registers:
        code.extend(bytes([0xB0, index, 0xEE, 0x42, 0xEC, 0x50, 0xB0, value, 0xEE, 0x4A]))
    code.extend(bytes.fromhex("ba c4 03 ec 50 b0 02 ee 42 ec 50 b0 0f ee"))
    code.extend(bytes.fromhex("b8 00 a0 8e c0 bf 00 60 b9 a0 0f 31 c0 f3 ab"))
    code.extend(bytes.fromhex("58 ee 4a 58 ee ba ce 03"))
    for index, _ in reversed(registers):
        code.extend(bytes([0xB0, index, 0xEE, 0x42, 0x58, 0xEE, 0x4A]))
    code.extend(bytes.fromhex("58 ee 0e 07 c3"))
    return code, [1]


def build(original, framebuffer=False, effects=False):
    if effects and not framebuffer:
        raise ValueError("Native effects require framebuffer capture")
    image, metadata = unpack_lz91(original)
    image, metadata = unpack_exepack(image, metadata)
    if image[0x8CBB:0x8CC0] != bytes.fromhex("80 3e d9 39 00"):
        raise ValueError("This executable does not match the known sprite entry")
    end = max(len(image), metadata["stack_ss"] * 16 + metadata["stack_sp"])
    cave_offset = (end + 15) & ~15
    cave_segment = cave_offset // 16
    sites = [(0x8CBB, 0, bytes.fromhex("80 3e d9 39 00"), None),
             (0xE903, 0x100, bytes.fromhex("a1 d3 39 a3 d7 39"), 1),
             (0xE935, 0x200, bytes.fromhex("8b 0e d3 39 ba d4 03"), 2)]
    cave = bytearray(65536)
    extra_relocations, hook_sizes = [], []
    for site, entry, displaced, marker in sites:
        if image[site:site + len(displaced)] != displaced:
            raise ValueError(f"Unknown instruction sequence at {site:#x}")
        code = hook_code(displaced, site + len(displaced), marker, framebuffer and marker == 2, effects)
        if len(code) > (0xE0 if entry == 0 else 0x100):
            raise ValueError("Hook overlaps reserved metadata or another hook")
        cave[entry:entry + len(code)] = code
        extra_relocations.extend([site + 3, cave_offset + entry + len(code) - 2])
        hook_sizes.append(len(code))
    capture_metadata = {}
    if framebuffer:
        buffer_segment = cave_segment + 0x1000
        capture, relocation = framebuffer_code(buffer_segment, effects)
        if len(capture) > 0x100:
            raise ValueError("Framebuffer capture overlaps the draw ring")
        cave[0x300:0x300 + len(capture)] = capture
        extra_relocations.append(cave_offset + 0x300 + relocation)
        capture_metadata = {"buffer_segment_relative": buffer_segment,
                            "metadata_offset": 0xF8, "plane_bytes": 8000,
                            "bank_bytes": 32000, "width": 320, "height": 200,
                            "capture_site": 0xE935, "code_bytes": len(capture)}
        capture_metadata["palette_bytes"] = 48
        if effects:
            for entry, generator in [(0x400, effects_duplicate_code), (0x500, effects_clear_code)]:
                code, relocations = generator()
                if len(code) > 0x100:
                    raise ValueError("Native effect helper exceeds its reserved space")
                cave[entry:entry + len(code)] = code
                extra_relocations.extend(cave_offset + entry + offset for offset in relocations)
            capture_metadata["layer"] = "transformed-sprites"
    cave[0xE0:0xF0] = MAGIC
    ring_start = 0x800 if effects else RING_START
    struct.pack_into("<H", cave, 0xF0, ring_start)
    output = bytearray(image)
    output.extend(b"\0" * (cave_offset - len(output)))
    output.extend(cave)
    if framebuffer:
        output.extend(bytes(65536))
    for site, entry, displaced, marker in sites:
        output[site:site + len(displaced)] = struct.pack("<BHH", 0xEA, entry, cave_segment) + b"\x90" * (len(displaced) - 5)
    # Startup's INT 21h/AH=4Ah would otherwise release our appended memory.
    # Replace ADD BX,AX / INC BX with the complete image + PSP allocation.
    if image[0xE798:0xE79B] != bytes.fromhex("03 d8 43"):
        raise ValueError("Unknown DOS allocation-shrink sequence")
    allocation = (len(output) + 15) // 16 + 16
    output[0xE798:0xE79B] = struct.pack("<BH", 0xBB, allocation)
    metadata["relocation_addresses"] = metadata["relocation_addresses"] + extra_relocations
    manifest = {"source_sha256": hashlib.sha256(original).hexdigest(),
                "hook_segment_relative": cave_segment, "hook_offset": cave_offset,
                "hook_size": hook_sizes[0], "ring_start": ring_start,
                "frame_begin_site": 0xE903, "frame_end_site": 0xE935,
                "frame_hook_sizes": hook_sizes[1:], "frame_sequence_offset": 0xF6,
                "allocation_paragraphs_including_psp": allocation,
                "record_size": 16, "capacity": (65536 - ring_start) // 16,
                "cursor_offset": 0xF0, "count_offset": 0xF2,
                "magic_offset": 0xE0, "magic_hex": MAGIC.hex(),
                "record_words": ["x", "y", "sprite_segment", "sprite_offset",
                                 "frame_sequence", "caller_ip", "draw_page", "data_segment"]}
    if framebuffer:
        manifest["framebuffer"] = capture_metadata
    return rebuild_executable(original, output, metadata), manifest


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("game", type=Path)
    parser.add_argument("--output", type=Path, default=Path("artifacts/runtime/game/GAME86.EXE"))
    parser.add_argument("--framebuffer", action="store_true", help="Experimental original VGA frame capture")
    parser.add_argument("--effects", action="store_true", help="Capture native transformed sprites on a separate VGA page")
    parser.add_argument("--manifest", type=Path, help="Separate manifest for an experimental build")
    args = parser.parse_args()
    if args.output.resolve().is_relative_to(args.game.resolve()):
        parser.error("Hook output must be outside the original game directory")
    if args.framebuffer and args.manifest is None:
        parser.error("Use --manifest to keep experimental capture separate from the live manifest")
    executable, manifest = build((args.game / "GAME86.EXE").read_bytes(), args.framebuffer, args.effects)
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_bytes(executable)
    (args.manifest or args.output.parent.parent / "hook-manifest.json").write_text(
        json.dumps(manifest, indent=2), encoding="utf-8")
    print(f"Built {len(executable):,}-byte local executable; "
          f"{manifest['hook_size']}-byte draw hook, {manifest['capacity']} ring records")


if __name__ == "__main__":
    main()
