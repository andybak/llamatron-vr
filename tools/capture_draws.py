"""Bounded diagnostic sprite-event capture; debugger round trips are not realtime."""
import argparse
import json
import re
import time
from pathlib import Path

from debug_command import execute


REGISTERS = "CS EIP AX BX ES SI DI DS SS SP"


def registers():
    response = execute(f"EXEC EV {REGISTERS}")
    values = response.split("is:\n", 1)[1].strip().split()
    return dict(zip(REGISTERS.lower().split(), (int(v, 16) for v in values)))


def signed(value):
    return value - 65536 if value >= 32768 else value


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--count", type=int, default=100)
    parser.add_argument("--segment", type=lambda v: int(v, 16), required=True)
    parser.add_argument("--output", type=Path, default=Path("artifacts/runtime/draw-events.json"))
    args = parser.parse_args()
    if not 1 <= args.count <= 1000:
        parser.error("Count must be between 1 and 1000")
    execute("BREAK")
    execute("EXEC BPDEL *")
    execute(f"EXEC BP {args.segment:04X}:8CBB")
    events = []
    started = time.monotonic()
    for _ in range(args.count):
        state = registers()
        if state["cs"] != args.segment or state["eip"] != 0x8CBB:
            execute("EXEC RUN")
            deadline = time.monotonic() + 3
            while time.monotonic() < deadline:
                time.sleep(.01)
                state = registers()
                if state["cs"] == args.segment and state["eip"] == 0x8CBB:
                    break
            else:
                execute("BREAK")
                print("No sprite breakpoint within three seconds", flush=True)
                break
        bank = {0x118C: "main", 0x20EB: "g3"}.get((state["es"] - args.segment) & 65535, "unknown")
        events.append({"order": len(events), "bank": bank,
                       "sprite_id": f"{bank}-{state['si']:04x}",
                       "x": signed(state["ax"]), "y": signed(state["bx"]),
                       "registers": state})
        if len(events) % 20 == 0:
            args.output.write_text(json.dumps(events, indent=2), encoding="utf-8")
            print(f"Captured {len(events)} draw events in {time.monotonic() - started:.1f}s", flush=True)
        execute("EXEC RUN")
        time.sleep(.01)
    execute("BREAK")
    args.output.write_text(json.dumps(events, indent=2), encoding="utf-8")
    print(f"Saved {len(events)} draw events to {args.output}", flush=True)


if __name__ == "__main__":
    main()
