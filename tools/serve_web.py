"""Serve a Unity web build locally, including correct MIME/compression headers."""

import argparse
from functools import partial
from http.server import SimpleHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path


class UnityWebHandler(SimpleHTTPRequestHandler):
    extensions_map = {**SimpleHTTPRequestHandler.extensions_map,
                      '.wasm': 'application/wasm', '.js': 'application/javascript',
                      '.data': 'application/octet-stream', '.webmanifest': 'application/manifest+json'}

    def guess_type(self, path):
        if path.endswith(('.gz', '.br')):
            path = path.rsplit('.', 1)[0]
        return super().guess_type(path)

    def end_headers(self):
        path = self.path.split('?', 1)[0]
        if path.endswith('.gz'):
            self.send_header('Content-Encoding', 'gzip')
        elif path.endswith('.br'):
            self.send_header('Content-Encoding', 'br')
        self.send_header('Cache-Control', 'no-cache')
        self.send_header('Permissions-Policy', 'xr-spatial-tracking=(self), fullscreen=(self)')
        super().end_headers()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--root', type=Path, default=Path('artifacts/standalone/web'))
    parser.add_argument('--host', default='127.0.0.1')
    parser.add_argument('--port', type=int, default=8080)
    args = parser.parse_args()
    root = args.root.resolve()
    if not (root / 'index.html').is_file():
        parser.error(f'No built web app at {root}; use Llamatron > Build Web preview first.')
    server = ThreadingHTTPServer((args.host, args.port), partial(UnityWebHandler, directory=str(root)))
    print(f'[LLWEB] Serving {root} at http://{args.host}:{server.server_port}', flush=True)
    try:
        server.serve_forever()
    except KeyboardInterrupt:
        pass
    finally:
        server.server_close()


if __name__ == '__main__':
    main()
