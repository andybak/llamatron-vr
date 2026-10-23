"""Validate a stamped Unity web build and its exact offline asset URLs."""
import argparse
from html.parser import HTMLParser
import json
from pathlib import Path
import re
from urllib.parse import urlsplit


class PageAssets(HTMLParser):
    def __init__(self):
        super().__init__()
        self.urls = set()

    def handle_starttag(self, tag, attributes):
        attributes = dict(attributes)
        value = attributes.get('src') if tag == 'script' else attributes.get('href') if tag == 'link' else None
        if value:
            self.urls.add(value)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--root', type=Path, default=Path('artifacts/standalone/web'))
    args = parser.parse_args()
    root = args.root.resolve()
    html = (root / 'index.html').read_text(encoding='utf-8')
    manifest = json.loads((root / 'precache.json').read_text())
    build = manifest['buildId']
    assert re.fullmatch(r'\d{14}', build), 'Invalid build ID'
    for name in ['index.html', 'service-worker.js', 'manifest.webmanifest']:
        assert '__LLAMATRON_BUILD_ID__' not in (root / name).read_text(encoding='utf-8'), f'Unstamped {name}'
    assert f'name="llamatron-build" content="{build}"' in html, 'HTML/build mismatch'
    assert f"const buildId = '{build}'" in (root / 'service-worker.js').read_text(), 'Worker/build mismatch'
    files = set(manifest['files'])
    assert len(files) == len(manifest['files']), 'Duplicate precache URLs'
    for url in files:
        parsed = urlsplit(url)
        assert not parsed.scheme and not parsed.netloc, f'Nonlocal offline URL: {url}'
        path = (root / parsed.path).resolve()
        assert path.is_relative_to(root), f'Offline URL escapes output: {url}'
        assert path.is_dir() if url == './' else path.is_file(), f'Missing offline asset: {url}'
    page_assets = PageAssets()
    page_assets.feed(html)
    build_urls = set(re.findall(r"Build/[^'\"\s)]+", html))
    assert len(build_urls) == 4, 'Expected loader, framework, data and WebAssembly URLs'
    for url in page_assets.urls | build_urls:
        assert url in files, f'Page URL is not precached exactly: {url}'
    for url in build_urls:
        parsed = urlsplit(url)
        assert parsed.query == f'v={build}' or re.match(r'^[a-fA-F0-9]{32}\.', Path(parsed.path).name), f'Unversioned Unity URL: {url}'
    app = json.loads((root / 'manifest.webmanifest').read_text())
    for icon in app['icons']:
        assert icon['src'] in files, f'Install icon missing from offline cache: {icon["src"]}'
    print(f'[LLWEB] Verified build {build}: four versioned Unity URLs and {len(files)} complete offline entries.')


if __name__ == '__main__':
    main()
