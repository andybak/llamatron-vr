"""Local DOSBox-X debugger transport. Binds only to loopback.

The HTTP endpoint serializes commands onto DOSBox-X's documented line protocol.
All command output and the emulator's files stay inside the selected runtime.
"""

import argparse
import json
import socket
import subprocess
import threading
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path


class Debugger:
    def __init__(self, listener):
        self.listener = listener
        self.connection = None
        self.reader = None
        self.lock = threading.Lock()
        self.sequence = 0

    def command(self, command):
        with self.lock:
            if self.connection is None:
                self.connection, _ = self.listener.accept()
                self.connection.settimeout(15)
                self.reader = self.connection.makefile("rb")
            self.sequence += 1
            request_id = str(self.sequence)
            self.connection.sendall(f"REQ {request_id} {command}\n".encode("utf-8"))
            lines = []
            for _ in range(20000):
                raw = self.reader.readline()
                if not raw:
                    raise ConnectionError("DOSBox-X disconnected")
                line = raw.decode("utf-8", errors="replace").rstrip("\r\n")
                if line == f"END {request_id}":
                    return "\n".join(lines)
                lines.append(line)
            raise ValueError("Debugger response exceeded the line limit")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--runtime", type=Path, default=Path("artifacts/runtime"))
    parser.add_argument("--exe", type=Path, required=True)
    args = parser.parse_args()
    runtime = args.runtime.resolve()
    runtime.mkdir(parents=True, exist_ok=True)
    listener = socket.socket()
    listener.bind(("127.0.0.1", 0))
    listener.listen(1)
    listener.settimeout(15)
    debugger = Debugger(listener)

    class Handler(BaseHTTPRequestHandler):
        def log_message(self, *_):
            pass

        def do_POST(self):
            try:
                length = int(self.headers.get("Content-Length", 0))
                if not 0 < length <= 8192:
                    raise ValueError("Invalid request length")
                request = json.loads(self.rfile.read(length))
                command = request["command"]
                if "\n" in command or "\r" in command:
                    raise ValueError("Only one command is permitted per request")
                response = {"response": debugger.command(command)}
                status = 200
            except Exception as error:
                response, status = {"error": str(error)}, 500
            content = json.dumps(response).encode("utf-8")
            self.send_response(status)
            self.send_header("Content-Type", "application/json")
            self.send_header("Content-Length", str(len(content)))
            self.end_headers()
            self.wfile.write(content)

    server = ThreadingHTTPServer(("127.0.0.1", 0), Handler)
    config = runtime / "dosbox-x.conf"
    config.write_text(f'''[sdl]
fullscreen=false
output=surface
autolock=false
[dosbox]
machine=vgaonly
memsize=16
mcp_server={listener.getsockname()[1]}
[cpu]
core=normal
cycles=fixed 12000
[midi]
mididevice=none
[sblaster]
sbtype=sb16
sbbase=220
irq=7
dma=1
hdma=5
[autoexec]
mount c "{runtime / 'game'}"
c:
llama.exe
''', encoding="utf-8")
    output = (runtime / "emulator-output.log").open("wb")
    process = subprocess.Popen([str(args.exe.resolve()), "-conf", str(config), "-noconsole"],
                               cwd=runtime, stdout=output, stderr=subprocess.STDOUT,
                               creationflags=subprocess.CREATE_NO_WINDOW)
    state = {"bridge_pid": __import__("os").getpid(), "emulator_pid": process.pid,
             "http_port": server.server_address[1], "debugger_port": listener.getsockname()[1],
             "emulator_exe": str(args.exe.resolve()), "runtime": str(runtime)}
    (runtime / "bridge-state.json").write_text(json.dumps(state, indent=2), encoding="utf-8")
    print(json.dumps(state), flush=True)
    def watch_emulator():
        process.wait()
        server.shutdown()
    threading.Thread(target=watch_emulator, daemon=True).start()
    try:
        server.serve_forever()
    finally:
        server.server_close()
        listener.close()
        output.close()


if __name__ == "__main__":
    main()
