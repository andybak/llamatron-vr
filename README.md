# Llamatron Native

A native Windows OpenXR prototype that presents the original DOS game as a
3D arena with extruded sprites and captured original pixel effects. The user
confirmed headset rendering and left-thumbstick movement on 19 October 2026.

The current application attaches to a separately running, instrumented DOSBox-X
instance. Integrating emulator startup, shutdown and audio into the native
application is the next step. See [native/README.md](native/README.md) for build
requirements, implementation details and validation limits.

## Prepare and build

Python with Pillow, Visual Studio 2022 C++ desktop tools, the Windows SDK and
CMake 3.21 or later are required. Supply the original game files separately:

```powershell
python tools/inspect_game.py 'F:\Emulation\DOSBox\games\llama'
python tools/export_sprites.py
```

Place an official Windows x64 OpenXR loader at
`native/generated/openxr_loader.dll`, then run `tools/build_native.ps1`.
Generated graphics and the loader copy are not versioned.

## Development test

1. Supply DOSBox-X at `.tools/dosbox-x/mingw-build/mingw-sdl2/dosbox-x.exe`.
2. Start `python tools/probe_native_effects.py --effects --audio`. This prepares
   an isolated game copy and leaves it paused. Omit `--audio` for a silent test.
3. Resume through the development debug bridge:

   ```powershell
   python -c 'import sys; from pathlib import Path; sys.path.insert(0,"tools"); from debug_command import execute; print(execute("EXEC RUN",Path("artifacts/effects-probe")))'
   ```

4. Run `Test Native.cmd` from Windows with a working OpenXR runtime and headset.
   Startup output is written to `artifacts/native-user-test.log`.
5. Stop the test emulator before resuming development so sound does not keep playing.

The capture tools patch a copied executable and leave the original game files
untouched. [Native effect capture](docs/native-effects.md) describes the hook
and its coverage limits. `tools/test_frame_publication.py` checks complete-frame
publication in the capture reader.

## Archived prototype

The `browser-prototype-archive` branch preserves the browser/WebXR implementation,
its launcher, the OpenVR controller experiment and earlier diagnostic utilities.
They are not part of the native application on `main`. A separate local milestone
archive also exists under `milestones/`; archives, game assets, downloaded tools,
builds and runtime logs are excluded from Git.
