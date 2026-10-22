"""Recover source-addressed gameplay tables from the original packed GAME86.EXE.

This is byte-level recovery, not REA decompilation. No game process is started.
"""

import argparse
import hashlib
import json
import struct
from pathlib import Path

from inspect_game import unpack_cmp, unpack_exepack, unpack_lz91


DATA_SEGMENT = 0x2559
UPDATE_TABLE = 0x1FD2
RENDER_TABLE = 0x202E
RENDER_COUNT = 19
WAVE_TABLE = 0x2BDA
SPAWN_TABLE = 0x2CA4
COMMAND_LENGTHS = {1: 2, 2: 4, 3: 4, 4: 2, 5: 2, 6: 2, 7: 3, 8: 1}
INPUT_TABLES = {"FireX": 0x743, "FireY": 0x753, "ProjectileSprite": 0x763,
                "SpreadLeft": 0x773, "SpreadRight": 0x783, "RicochetX": 0x793,
                "RicochetY": 0x7A3, "MoveX": 0x7B3, "MoveY": 0x7C3,
                "Facing": 0x7D3, "TrailDirection": 0x7E3}
PLAYER_DEFAULTS = {"PrimaryFireX": 0x69C, "PrimaryFireY": 0x69E,
                   "PrimaryProjectileSprite": 0x6AC, "PrimaryAimMask": 0x6B6,
                   "PrimaryFacing": 0x6C2, "SecondaryFireX": 0x6A0,
                   "SecondaryFireY": 0x6A2, "SecondaryProjectileSprite": 0x6AE,
                   "SecondaryAimMask": 0x6B8, "SecondaryFacing": 0x7F9}

# Instruction anchors deliberately include the table/pool addressing operations.
# Refuse another revision rather than exporting plausible but incorrect constants.
ANCHORS = {
    0x10: "ba59258eda",
    0x4307: "bec148b96400",
    0x432B: "bfc148bd4500",
    0x435B: "8a1d81e3ff00d1e38bb7d21f",
    0x4374: "bfc148b94600",
    0x437F: "8b750681e6ff00d1e68bb42e20",
    0x43C9: "ff450e837d0e0f",
    0x43DC: "836d0e02",
    0x4EC8: "ff4d147801c3",
    0xE4DE: "8b1ee9398a9fc96a81e3ff00",
    0xE543: "b036e643b8ec5ce6408ac4e640",
    0x3E08: "ff06e639c706de390000",
    0x20B: "8b1eaa21d1e38b9fda2b",
    0x23C: "2e8bb44502ffd6",
    0x3D09: "8b36eb398a5c0180fbff",
    0x3D29: "8b87a42ca3fe08",
    0x1F3F: "8b442883e001",
    0x60C5: "ba2a04bf4141",
    0x29AF: "8b1e13048a874141",
}


def recover(original, motion_resource):
    image, metadata = unpack_lz91(original)
    image, metadata = unpack_exepack(image, metadata)
    for address, expected_hex in ANCHORS.items():
        expected = bytes.fromhex(expected_hex)
        if image[address:address + len(expected)] != expected:
            raise ValueError(f"Unsupported GAME86 revision: instruction mismatch at CS:{address:04x}")

    def words(offset, count, signed=False):
        return list(struct.unpack_from(f"<{count}{'h' if signed else 'H'}", image,
                                       DATA_SEGMENT * 16 + offset))

    def entries(offset, count):
        return [{"id": i, "handler_cs_offset": address,
                 "table_ds_offset": offset + i * 2}
                for i, address in enumerate(words(offset, count))]

    wave_pointers = []
    for address in words(WAVE_TABLE, (SPAWN_TABLE - WAVE_TABLE) // 2):
        if address == 0:
            break
        wave_pointers.append(address)
    else:
        raise ValueError("Missing wave-table terminator")
    spawn_count = (0x2CF6 - SPAWN_TABLE) // 2
    main_bank = struct.unpack_from("<H", image, 0x25F)[0] * 16
    later_ricochet_sprites = set(image[DATA_SEGMENT * 16 + 0x773:DATA_SEGMENT * 16 + 0x7D3])
    if later_ricochet_sprites.intersection(range(29, 33)):
        raise ValueError("Unsupported ricochet scan: adjacent tables can match player projectile sprites")
    waves = []
    for index, address in enumerate(wave_pointers):
        cursor = DATA_SEGMENT * 16 + address
        start = cursor
        commands = []
        while image[cursor] >= 128:
            code = 256 - image[cursor]
            if code not in COMMAND_LENGTHS:
                raise ValueError(f"Unknown level command {code} in wave {index + 1}")
            size = COMMAND_LENGTHS[code]
            commands.append({"command": code, "arguments": list(image[cursor + 1:cursor + 1 + size])})
            cursor += size + 1
        mode = image[cursor]
        cursor += 1
        groups = []
        while image[cursor + 1] != 255:
            kind, count = image[cursor:cursor + 2]
            if kind >= spawn_count:
                raise ValueError(f"Invalid spawn kind {kind} in wave {index + 1}")
            groups.append({"spawn_kind": kind, "encoded_count": count, "instances": count + 1})
            cursor += 2
        cursor += 2
        waves.append({"number": index + 1, "script_ds_offset": address, "commands": commands,
                      "mode": mode, "groups": groups, "bytes": list(image[start:cursor])})

    motion = unpack_cmp(motion_resource)
    if len(motion) != 768:
        raise ValueError("Expected the original 768-byte S.CMP decoded resource")
    return {
        "schema": 1,
        "evidence_kind": "unpacked DOS bytes and manually traced instructions",
        "original_sha256": hashlib.sha256(original).hexdigest(),
        "image_sha256": hashlib.sha256(image).hexdigest(),
        "data_segment": DATA_SEGMENT,
        "address_convention": "CS offsets refer to the unrelocated load image; DS offsets add 0x25590",
        "pool": {"ds_offset": 0x48C1, "record_bytes": 64,
                 "initialized_records": 100, "updated_records": 70,
                 "rendered_records": 70, "allocator_scan_records": 70},
        "update_dispatch": entries(UPDATE_TABLE, (RENDER_TABLE - UPDATE_TABLE) // 2),
        "render_dispatch": entries(RENDER_TABLE, RENDER_COUNT),
        "spawn_dispatch": entries(SPAWN_TABLE, spawn_count),
        "wave_command_dispatch": list(struct.unpack_from("<8H", image, 0x245)),
        "waves": waves,
        "initial_parameters": {
            "chase_max_x": words(0x21AC, 1, True)[0],
            "chase_max_y": words(0x21AE, 1, True)[0],
            "chase_reload": words(0x21B2, 1, True)[0],
            "word_0812": words(0x0812, 1, True)[0]},
        "spawn_graphics": {
            "trail_sprites": words(0x0911, 4),
            "herd_base_sprites": words(0x0808, 4),
            "obstacle_sprites": words(0x0939, 4),
            "enemy_sprite_pairs": list(struct.unpack_from("<54H", image,
                struct.unpack_from("<H", image, 0x25F)[0] * 16 + 0x362))},
        "input_tables": {name: list(struct.unpack_from("<16b", image, DATA_SEGMENT * 16 + offset))
                         for name, offset in INPUT_TABLES.items()},
        "ricochet_scan_invariant": "Only the first 16 scan entries can match player projectile sprites 29..32",
        "player_defaults": {name: words(offset, 1, True)[0] for name, offset in PLAYER_DEFAULTS.items()},
        "damage_dispatch": list(struct.unpack_from("<18H", image, 0xC04)),
        "chase_damage_outcomes": list(struct.unpack_from("<12H", image, 0x1047)),
        "recoil_defaults": {"InitialHealth": words(0x0818, 1, True)[0],
                            "InitialBaseSprite": words(0x0824, 1, True)[0],
                            "InitialIncrement": words(0x0826, 1, True)[0]},
        "orbit_data": {"resource_sha256": hashlib.sha256(motion_resource).hexdigest(),
                       "decoded_sha256": hashlib.sha256(motion).hexdigest(),
                       "decoded_size": len(motion), "table_ds_offset": 0x4141,
                       "Motion": list(struct.unpack_from("<256b", motion)),
                       "InitialPhase": words(0x081e, 1, True)[0],
                       "InitialSpacing": words(0x0820, 1, True)[0],
                       "InitialAcceleration": words(0x081a, 2)},
        "edge_defaults": {"InitialAccelerationLow": words(0x0828, 1, True)[0],
                          "InitialAccelerationHigh": words(0x082a, 1, True)[0],
                          "InitialCadence": words(0x082c, 1, True)[0],
                          "InitialHealth": words(0x082e, 1, True)[0]},
        "directional_tables": {"Quadrants": list(image[DATA_SEGMENT * 16 + 0x084a:DATA_SEGMENT * 16 + 0x084e]),
                               "X": words(0x084e, 8, True), "Y": words(0x085e, 8, True)},
        "announcement_sprites": words(0x063C, 10),
        "death_rotation": list(struct.unpack_from("<4b", image, DATA_SEGMENT * 16 + 0x654)),
        "kill_score_calls": [{"ax": words(0x2054 + i * 4, 1)[0],
                              "column": words(0x2056 + i * 4, 1)[0]}
                             for i in range(46)],
        "main_sprite_last_y": [struct.unpack_from("<H", image,
            main_bank + struct.unpack_from("<H", image, main_bank + 0x1C + i * 2)[0] + 4)[0]
            for i in range(414)],
        "main_sprite_offsets": list(struct.unpack_from("<414H", image, main_bank + 0x1C)),
        "hunter_patterns": {name: [list(struct.unpack_from("<4h", image, DATA_SEGMENT * 16 + offset + i * 8))
                                    for i in range(count)]
                            for name, offset, count in [("Diagonal", 0x0878, 4), ("Horizontal", 0x0898, 4), ("EightWay", 0x08B8, 8)]},
        "herd_direction_tables": {"x": words(0x1FA2, 8, True),
                                  "y": words(0x1FB2, 8, True),
                                  "animation": words(0x1FBA, 8, True)},
        "random": {"table_ds_offset": 0x6AC9, "length": 512,
                   "index_ds_offset": 0x39E9, "shuffle_swaps": 4000,
                   "initialization": "two copies of bytes 0..255, swapped using timer samples at CS:e616",
                   "next_cs_offset": 0xE4DE},
        "timing": {"pit_divisor": struct.unpack_from("<H", image, 0xE548)[0],
                   "pit_program_cs_offset": 0xE543,
                   "interrupt_entry_cs_offset": 0x3DE4,
                   "logic_callback_ds_offset": 0x39E4,
                   "input_frame_counter_ds_offset": 0x39E6,
                   "vertical_retrace_wait_cs_offset": 0xE5F3,
                   "boundary": "PIT interrupt gameplay updates and vertical-retrace display calls are separate; runtime cadence is not yet reproduced."},
        "limitations": [
            "Dispatch IDs are behavior states, not established enemy species names.",
            "Initial parameters can be overwritten by level scripts.",
            "Pool memory is also reused by non-gameplay screens.",
            "Original timer sampling and frame cadence are not yet reproduced.",
            "The report decodes commands, but does not establish the behavior of their downstream consumers.",
            "This report does not establish a working standalone game or REA analysis."]}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("binary", type=Path)
    parser.add_argument("--output", type=Path, default=Path("artifacts/reconstruction/gameplay.json"))
    parser.add_argument("--csharp", type=Path, help="Export the recovered level data as a standalone C# asset")
    parser.add_argument("--motion-resource", type=Path, help="Original S.CMP; defaults to beside GAME86.EXE")
    args = parser.parse_args()
    resource_path = args.motion_resource or args.binary.parent / "S.CMP"
    report = recover(args.binary.read_bytes(), resource_path.read_bytes())
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(f"{json.dumps(report, indent=2)}\n", encoding="utf-8")
    if args.csharp:
        lines = ["// Generated by tools/recover_gameplay.py; level data, not DOS instructions.",
                 f"// Original SHA-256: {report['original_sha256']}",
                 "namespace Llamatron.Core", "{", "    public static class OriginalWaves", "    {",
                 "        public static WaveDefinition[] Decode()", "        {",
                 "            byte[][] scripts = CopyScripts();",
                 "            var waves = new WaveDefinition[scripts.Length];",
                 "            for (int i = 0; i < scripts.Length; i++) waves[i] = WaveDefinition.Decode(scripts[i]);",
                 "            return waves;", "        }",
                 "        public static byte[][] CopyScripts() => new byte[][]", "        {"]
        for wave in report["waves"]:
            payload = ", ".join(f"0x{x:02x}" for x in wave["bytes"])
            lines.append(f"            new byte[] {{ {payload} }}, // Wave {wave['number']}")
        offsets = ", ".join(f"0x{wave['script_ds_offset']:04x}" for wave in report["waves"])
        lines.extend(["        };",
                      "        public static System.Collections.Generic.IReadOnlyList<ushort> ScriptOffsets { get; } =",
                      f"            System.Array.AsReadOnly(new ushort[] {{ {offsets} }});",
                      "    }", "    public static class OriginalSpawnGraphics", "    {"])
        for name, key in [("HerdBaseSprites", "herd_base_sprites"), ("EnemySpritePairs", "enemy_sprite_pairs"),
                          ("ObstacleSprites", "obstacle_sprites"), ("TrailSprites", "trail_sprites")]:
            payload = ", ".join(f"0x{x:04x}" for x in report["spawn_graphics"][key])
            lines.append(f"        public static System.Collections.Generic.IReadOnlyList<ushort> {name} {{ get; }} =")
            lines.append(f"            System.Array.AsReadOnly(new ushort[] {{ {payload} }});")
        for name, key in [("HerdDirectionX", "x"), ("HerdDirectionY", "y"), ("HerdAnimation", "animation")]:
            payload = ", ".join(str(x) for x in report["herd_direction_tables"][key])
            lines.append(f"        public static System.Collections.Generic.IReadOnlyList<short> {name} {{ get; }} =")
            lines.append(f"            System.Array.AsReadOnly(new short[] {{ {payload} }});")
        lines.extend(["    }", "    public static class OriginalInputTables", "    {"])
        for name, values in report["input_tables"].items():
            payload = ", ".join(str(x) for x in values)
            lines.append(f"        public static System.Collections.Generic.IReadOnlyList<sbyte> {name} {{ get; }} =")
            lines.append(f"            System.Array.AsReadOnly(new sbyte[] {{ {payload} }});")
        for name, value in report["player_defaults"].items():
            lines.append(f"        public const short {name} = {value};")
        lines.extend(["    }", "    public static class OriginalDamageData", "    {"])
        for name, values, kind in [("AnnouncementSprites", report["announcement_sprites"], "ushort"),
                                   ("DeathRotation", report["death_rotation"], "sbyte"),
                                   ("ScoreAx", [x["ax"] for x in report["kill_score_calls"]], "ushort"),
                                   ("ScoreColumn", [x["column"] for x in report["kill_score_calls"]], "ushort"),
                                   ("SpriteLastY", report["main_sprite_last_y"], "ushort"),
                                   ("SpriteOffsets", report["main_sprite_offsets"], "ushort")]:
            payload = ", ".join(str(x) for x in values)
            lines.append(f"        public static System.Collections.Generic.IReadOnlyList<{kind}> {name} {{ get; }} =")
            lines.append(f"            System.Array.AsReadOnly(new {kind}[] {{ {payload} }});")
        lines.extend(["    }", "    public static class OriginalHunterData", "    {"])
        for name, values in report["hunter_patterns"].items():
            payload = ", ".join(f"new ProjectilePattern({sprite}, {x}, {y})" for sprite, x, y, _ in values)
            lines.append(f"        public static System.Collections.Generic.IReadOnlyList<ProjectilePattern> {name} {{ get; }} =")
            lines.append(f"            System.Array.AsReadOnly(new ProjectilePattern[] {{ {payload} }});")
        lines.extend(["    }", "    public static class OriginalRecoilData", "    {"])
        for name, value in report["recoil_defaults"].items():
            lines.append(f"        public const short {name} = {value};")
        lines.extend(["    }", "    public static class OriginalOrbitData", "    {"])
        orbit = report["orbit_data"]
        for name in ["InitialPhase", "InitialSpacing"]:
            lines.append(f"        public const short {name} = {orbit[name]};")
        low, high = orbit["InitialAcceleration"]
        lines.append(f"        public const uint InitialAcceleration = {low | high << 16};")
        payload = ", ".join(str(value) for value in orbit["Motion"])
        lines.extend(["        public static System.Collections.Generic.IReadOnlyList<sbyte> Motion { get; } =",
                      f"            System.Array.AsReadOnly(new sbyte[] {{ {payload} }});"])
        lines.extend(["    }", "    public static class OriginalEdgeData", "    {"])
        for name, value in report["edge_defaults"].items():
            lines.append(f"        public const short {name} = {value};")
        lines.extend(["    }", "    public static class OriginalDirectionalData", "    {"])
        for name, values in report["directional_tables"].items():
            payload = ", ".join(str(value) for value in values)
            lines.append(f"        public static System.Collections.Generic.IReadOnlyList<short> {name} {{ get; }} =")
            lines.append(f"            System.Array.AsReadOnly(new short[] {{ {payload} }});")
        lines.extend(["    }", "    public static class OriginalTiming", "    {",
                      "        // IBM PIT input clock reference: DOSBox-X include/timer.h, PIT_TICK_RATE_IBM.",
                      "        public const int PitInputHz = 1193182;",
                      f"        public const int PitDivisor = {report['timing']['pit_divisor']};",
                      "        public const double InterruptHz = (double)PitInputHz / PitDivisor;",
                      "    }", "}", ""])
        args.csharp.parent.mkdir(parents=True, exist_ok=True)
        args.csharp.write_text("\n".join(lines), encoding="utf-8")
    print(f"Recovered {len(report['update_dispatch'])} update states and "
          f"{len(report['render_dispatch'])} render states, {len(report['waves'])} waves, "
          f"{len(report['spawn_dispatch'])} spawn kinds; output: {args.output}")


if __name__ == "__main__":
    main()
