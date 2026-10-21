# Native PC implementation

The user-facing flow is in the [root README](../README.md): supply the original
DOS game, run Setup once, then run Play. No browser or manual debugger commands
are involved. The renderer is C++/Direct3D 11/OpenXR. The current installation
still runs a separate DOSBox-X process managed by a private Python launcher;
it is not yet a single executable containing emulation.

## Setup and process ownership

1. `Setup.cmd` runs `tools/setup.ps1` using Windows PowerShell. A folder chooser
   selects the original game; `-GamePath` supplies it without a dialog. Setup
   remembers it in ignored `artifacts/install.json`. Placing the complete game
   in ignored `game/` beside Setup also avoids the chooser.
2. Source-checkout setup obtains private Python 3.12.10 x64 and Pillow 11.3.0, plus DOSBox-X
   2026.10.01 MinGW64 SDL2. It reuses suitable CMake and Visual Studio 2022 C++
   tools, downloading CMake 3.31.8 and installing C++ Build Tools if needed.
   Dependencies are cached under `.tools`; system Python and PATH are unchanged.
3. A missing OpenXR loader is built from the official Khronos OpenXR-SDK
   `release-1.1.49` source with `DYNAMIC_LOADER=ON`. Generated sprite data and the
   loader are embedded by `native/generate_assets.py`; MSVC builds
   `native/build/Release/LlamatronNative.exe`. The embedded loader is extracted
   alongside that executable at runtime. A headset runtime must already work.
4. `tools/game_session.py` patches a separate game copy under
   `artifacts/native-runtime`, runs a silent session to capture the original
   palette, then stops that session. Setup exports all 406 sprites using this
   palette before building. Rerunning setup keeps existing runtime scores.
5. `Play.cmd` uses only `.tools/python/python.exe`. `tools/launch_native.py`
   starts the prepared game, skips the startup screens, resumes gameplay and
   runs the native app with `--vr --control --runtime artifacts/native-runtime`.
   It does not need the original folder after setup has prepared the copy.
6. The launcher owns its bridge, emulator and native app through a Windows job.
   Closing the native app, startup/render failure, Ctrl+C or closing the launcher
   stops its owned processes. It does not terminate other DOSBox or VR sessions.
   OS locks prevent concurrent Setup/Play sessions from sharing this game state.

The source-build C++ workload can require several GB and administrator approval
on a fresh Windows machine. A requested Windows restart requires rerunning Setup.
CI-built ZIPs contain a `bundle.json` marker, private dependencies and the compiled
app. Their Setup path is offline and only patches the supplied game copy; it never
installs a compiler. `tools/package.ps1` creates these ZIPs using an explicit file
list and includes the redistributable MSVC DLLs and third-party license notices.

## Dependency sources

1. [Python embeddable distribution](https://www.python.org/ftp/python/3.12.10/).
2. [Pillow pinned release](https://pypi.org/project/pillow/11.3.0/); setup selects
   the CPython 3.12 Windows x64 wheel and checks its published SHA-256.
3. [DOSBox-X release](https://github.com/joncampbell123/dosbox-x/releases/tag/dosbox-x-v2026.10.01).
   The portable archive is checked against the tested SHA-256
   `def2d84d4415fc26c77252839b4dafb6b476e3a4204b896292e6b05505ca8c59`.
4. [CMake release](https://github.com/Kitware/CMake/releases/tag/v3.31.8), checked
   against its published SHA-256 listing when a download is required.
5. [Microsoft C++ Build Tools installer](https://aka.ms/vs/17/release/vs_BuildTools.exe).
6. [Khronos OpenXR-SDK](https://github.com/KhronosGroup/OpenXR-SDK/tree/release-1.1.49)
   and its [dynamic-loader build instructions](https://github.com/KhronosGroup/OpenXR-SDK/blob/release-1.1.49/README.md).

## Native rendering

1. Capture validates the recorded emulator PID, hook signature and relocated
   game jump. Sprite events and VGA effect/palette buffers are read directly
   from that process with complete-frame publication checks.
2. Direct3D 11 renders the arena, masked sprite extrusions and captured original
   pixel effects. Bullets lie parallel to the arena. Textures use nearest-neighbour
   sampling; setup exports the game palette instead of provisional EGA colours.
3. OpenXR selects the D3D11 adapter and supplies stereo swapchains, floor/local
   space and predicted head views. Render targets use the negotiated format;
   DXGI 1.1 enables shared textures in SteamVR. Separate hand subactions suggest
   Touch, Index, Windows MR and simple-controller bindings; the left stick has
   priority, with the right available as fallback.

## Developer checks

From the repository root after Setup:

```powershell
.\Play.cmd --smoke
.tools\python\python.exe -m unittest discover -s tools -p test_frame_publication.py
```

The smoke path starts a silent game, renders a completed frame using D3D11 WARP,
then shuts down the emulator. The image is `artifacts/native-launch-smoke.png`.
It does not test headset rendering or physical controllers. Setup itself leaves
no emulator running. `--mute` is available for a normal silent VR launch.

Low-level `--xr-probe`, `--runtime`, `--snapshot`, `--smoke` and `--control`
options remain in the native executable for diagnosis. Low-level capture tools
remain available; they are not required steps in the user launch flow.

## Validation and remaining work

1. Native VR rendering and left-thumbstick movement were confirmed by the user
   on 19 October 2026. This predates the consolidated setup/launcher.
2. The consolidated setup and offscreen launch/cleanup are checked on the
   development PC. The new launcher still needs a headset retry. Automatic C++
   Build Tools installation on a machine without a compiler is not yet tested.
3. Other controller profiles, both-hand behaviour, placement, native bullet and
   effect appearance and sustained performance need more headset testing.
4. A final single native executable would need to integrate emulator ownership
   and packaging internally. The CI-built portable ZIP already removes the compiler
   and asset-build work from end-user setup.

OpenXR 1.1.49 headers and their license are in `vendor/openxr`. The app requests
OpenXR 1.0 plus `XR_KHR_D3D11_enable`. The original game, extracted graphics,
patched copy, downloaded tools and build output are not committed. The
`browser-prototype-archive` branch preserves earlier experiments.
