"""Save a self-contained local browser-prototype milestone before native work."""
import hashlib
import json
from datetime import datetime, timezone
from pathlib import Path
from zipfile import ZIP_DEFLATED, ZipFile


def main():
    root = Path(__file__).resolve().parent.parent
    stamp = datetime.now(timezone.utc).strftime("%Y%m%d-%H%M%SZ")
    destination = root / "milestones" / f"{stamp}-browser-native-effects"
    destination.mkdir(parents=True, exist_ok=False)
    archive = destination / "browser-prototype.zip"
    excluded = {"__pycache__", "steamvr-logs", "node_modules"}
    transient = {"bridge-state.json", "viewer-state.json", "controller-helper.lock"}
    files = [root / "README.md", root / ".gitignore", root / "Start Controllers.cmd"]
    for folder in ["tools", "viewer", "docs", "artifacts", ".tools"]:
        files.extend(p for p in (root / folder).rglob("*") if p.is_file()
                     and not excluded.intersection(p.relative_to(root).parts)
                     and p.name not in transient and p.suffix not in (".log", ".pyc"))
    manifest = []
    with ZipFile(archive, "w", ZIP_DEFLATED, compresslevel=6) as output:
        for path in sorted(set(files)):
            data = path.read_bytes()
            name = path.relative_to(root).as_posix()
            output.writestr(name, data)
            manifest.append({"path": name, "bytes": len(data), "sha256": hashlib.sha256(data).hexdigest()})
        for path in sorted(Path(r"F:\Emulation\DOSBox\games\llama").iterdir()):
            if not path.is_file() or path.suffix.lower()==".lnk" or path.name in ("stdout.txt","stderr.txt"):
                continue
            data=path.read_bytes();name=f"milestone-original-game/{path.name}"
            output.writestr(name,data)
            manifest.append({"path":name,"bytes":len(data),"sha256":hashlib.sha256(data).hexdigest()})
    with ZipFile(archive) as check:
        damaged = check.testzip()
        if damaged:
            raise RuntimeError(f"Milestone archive failed its integrity check: {damaged}")
    checksum = hashlib.sha256(archive.read_bytes()).hexdigest()
    (destination / "files.json").write_text(json.dumps(manifest, indent=2))
    (destination / "SHA256.txt").write_text(f"{checksum}  {archive.name}\n")
    (destination / "RESTORE.md").write_text(f"""# Browser prototype milestone

Saved {stamp}, before starting the native PC application.

1. This archive contains source, decoded graphics, original pixel-effect capture,
   copied game files and the portable DOSBox-X distribution, including its license.
   It is a private local backup; no redistribution rights to the game are implied.
2. The user confirmed sprite flicker fixed, audio working, and native pixel effects
   visually good in VR. Left-controller discovery remains unresolved. The OpenVR
   helper is an unfinished workaround, included for historical completeness.
3. Both ordinary and isolated native-effect captures were tested. The browser
   displays captured VGA pixels; it does not simulate the effects. The effect layer
   is flat, and not every rendering path has been captured.
4. Runtime process IDs and live connection state are deliberately omitted.
   Extract to a folder and launch from that folder using Python with Pillow:

```powershell
python tools/probe_native_effects.py --effects --audio
python tools/viewer_server.py --runtime artifacts/effects-probe --paused --port 53443
```

5. Open http://127.0.0.1:53443/. The archive includes local copies of game files,
   although the recorded probe launcher still names the original source folder
   F:\\Emulation\\DOSBox\\games\\llama. Point that launcher at milestone-original-game
   if the original folder is unavailable. Do not overwrite an active workspace.
6. SHA256.txt covers the archive; files.json covers each archived file. ZIP CRC
   integrity was checked after creation. Restoring into a second running workspace
   was not tested and could conflict with the fixed viewer port.

## Direction chosen after this milestone

Build one native PC application using OpenXR for stereo rendering and both
controllers. Replace the browser and localhost server. Retain the original game's
emulation, audio and native pixel effects. One executable is the delivery target;
the initial native renderer is an intermediate development step.
""", encoding="utf-8")
    print(f"Milestone: {destination.relative_to(root)}")
    print(f"Archived {len(manifest)} files; {archive.stat().st_size / 1048576:.1f} MiB; CRC integrity passed")
    print(f"SHA256: {checksum}")


if __name__ == "__main__":
    main()
