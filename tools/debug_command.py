"""Issue a bounded debugger command to our running DOSBox-X bridge."""
import argparse
import json
import urllib.request
import urllib.error
from pathlib import Path


def execute(command, runtime=Path("artifacts/runtime")):
    state = json.loads((runtime / "bridge-state.json").read_text())
    request = urllib.request.Request(f"http://127.0.0.1:{state['http_port']}/",
                                     data=json.dumps({"command": command}).encode(),
                                     headers={"Content-Type": "application/json"})
    try:
        with urllib.request.urlopen(request, timeout=20) as response:
            return json.load(response)["response"]
    except urllib.error.HTTPError as error:
        detail = json.loads(error.read()).get("error", str(error))
        raise RuntimeError(f"DOSBox command {command!r} failed: {detail}") from error


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("command")
    parser.add_argument("--save", type=Path)
    args = parser.parse_args()
    result = execute(args.command)
    if args.save:
        args.save.write_text(result, encoding="utf-8")
        print(f"Saved {len(result)} characters to {args.save}")
    else:
        print(result[:6000])
