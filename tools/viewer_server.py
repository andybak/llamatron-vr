"""Serve the local WebXR prototype and a live sprite feed from our emulator."""
import functools
import argparse
import base64
import json
import threading
import time
from http.server import SimpleHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path
from urllib.parse import unquote, urlsplit

from ring_reader import RingReader
from debug_command import execute
from capture_native_frame import read_frame


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--port", type=int, default=53443)
    parser.add_argument("--paused", action="store_true")
    parser.add_argument("--runtime", type=Path, default=Path("artifacts/runtime"))
    args = parser.parse_args()
    root = Path(__file__).resolve().parent.parent
    runtime = args.runtime.resolve()
    state = json.loads((runtime / "bridge-state.json").read_text())
    reader = RingReader(state["emulator_pid"], runtime / "hook-manifest.json")
    reader.locate()
    lock = threading.Lock()
    control = {"paused": args.paused}
    audio = (runtime / "game/LLAMA.INF").read_bytes()[4:5] == b"S"
    native_effects = reader.manifest.get("framebuffer", {}).get("layer") == "transformed-sprites"
    palette = json.loads((runtime / "palette.json").read_text()) if native_effects else None
    last_native_frame = None
    controllers = {"sources": [], "error": None, "updated": 0}

    def scene_frame():
        nonlocal last_native_frame
        if not native_effects:
            return reader.frame()
        for _ in range(4):
            frame = reader.frame()
            try:
                planes, capture = read_frame(reader)
            except LookupError:
                continue
            if frame["frame_key"] and frame["frame_key"][0] == capture["sequence"]:
                frame["native_effects"] = {**capture, "planes": base64.b64encode(planes).decode("ascii"), "palette": capture.get("palette", palette)}
                last_native_frame = frame
                return dict(frame)
        if last_native_frame is not None:
            return dict(last_native_frame)
        raise LookupError("Waiting for matching sprites and native effect pixels")

    class Handler(SimpleHTTPRequestHandler):
        def log_message(self, *_):
            pass

        def json_response(self, data, status=200):
            content = json.dumps(data).encode("utf-8")
            self.send_response(status)
            self.send_header("Content-Type", "application/json")
            self.send_header("Content-Length", str(len(content)))
            self.send_header("Cache-Control", "no-store")
            self.end_headers()
            self.wfile.write(content)

        def do_GET(self):
            if self.path == "/frame":
                try:
                    with lock:
                        frame = scene_frame()
                        frame["paused"] = control["paused"]
                        frame["audio_enabled"] = audio
                        frame["native_controllers"] = {"sources": controllers["sources"], "error": controllers["error"],
                                                       "age_ms": (time.monotonic() - controllers["updated"]) * 1000}
                    self.json_response(frame)
                except Exception as error:
                    self.json_response({"error": str(error)}, 500)
                return
            if self.path == "/":
                self.path = "/viewer/index.html"
            # Only viewer code and extracted graphics are available over HTTP.
            decoded = unquote(urlsplit(self.path).path)
            if not decoded.startswith(("/viewer/", "/artifacts/sprites/")) or ".." in decoded or "\\" in decoded:
                self.send_error(404)
                return
            super().do_GET()

        def do_POST(self):
            if self.path not in ("/input", "/pause", "/controllers"):
                self.send_error(404)
                return
            origin = self.headers.get("Origin")
            if origin and origin != f"http://{self.headers.get('Host')}":
                self.send_error(403)
                return
            try:
                length = int(self.headers.get("Content-Length", 0))
                if not 0 < length < (4096 if self.path == "/controllers" else 1024):
                    raise ValueError("Invalid request length")
                request = json.loads(self.rfile.read(length))
                with lock:
                    if self.path == "/controllers":
                        sources = request.get("sources", [])
                        if not isinstance(sources, list) or len(sources) > 2:
                            raise ValueError("Expected up to two native controller roles")
                        controllers.update(sources=sources, error=request.get("error"), updated=time.monotonic())
                    elif self.path == "/input":
                        reader.input(int(request["bits"]), request.get("scan"))
                    else:
                        paused = bool(request["paused"])
                        response = execute("BREAK" if paused else "EXEC RUN", runtime)
                        if " ERR" in response:
                            raise RuntimeError("Emulator refused pause/resume command")
                        control["paused"] = paused
                self.json_response({"ok": True, **control})
            except Exception as error:
                self.json_response({"error": str(error)}, 500)

    server = ThreadingHTTPServer(("127.0.0.1", args.port), functools.partial(Handler, directory=str(root)))
    url = f"http://127.0.0.1:{server.server_address[1]}/"
    (runtime / "viewer-state.json").write_text(json.dumps({
        "url": url, "pid": __import__("os").getpid(), "emulator_pid": state["emulator_pid"]
    }, indent=2), encoding="utf-8")
    print(url, flush=True)
    try:
        server.serve_forever()
    finally:
        reader.close()
        server.server_close()


if __name__ == "__main__":
    main()
