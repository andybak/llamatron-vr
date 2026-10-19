# Native PC implementation

The delivery target is one native PC executable using OpenXR. The browser
prototype and OpenVR controller experiment are preserved on the
`browser-prototype-archive` branch.
The native project does not use WebXR, a browser, HTTP capture, Python at runtime,
or the experimental OpenVR controller helper.

## Current checkpoint

1. `native/build/Release/LlamatronNative.exe` is a built Windows x64 executable.
   It embeds 406 sprite graphics and an official OpenXR loader. The loader is
   extracted to a private `runtime` directory alongside the executable when
   OpenXR is used; users do not need to install the OpenXR SDK.
2. The C++ capture reader validates the recorded emulator PID, hook signature
   and relocated game jump, then reads completed draw events directly from
   that process. It reads matching original VGA effects and palette buffers
   with the same publication checks as the browser prototype.
3. Direct3D 11 renders the arena, masked sprite extrusions and native pixel
   effects. Directional bullets lie parallel to the arena. Texture sampling is
   nearest-neighbour; sRGB textures and targets preserve the original palette.
4. The OpenXR path implements runtime-selected D3D11 adapter selection, stereo
   swapchains, stage/local-floor space, predicted head views, session events and
   separate left/right thumbstick and trigger subaction paths. It suggests
   Touch, Index, Windows MR and simple-controller bindings. The user confirmed
   headset rendering and left-thumbstick movement on 19 October 2026.
5. This development checkpoint attaches to the existing emulator. It does not
   yet contain or start emulation or own its audio. Packaging and controlling
   an isolated emulator session inside the native application is the next step.

## Build and run

From the workspace root:

Build prerequisites are Visual Studio 2022 with the C++ desktop workload,
the Windows SDK, CMake 3.21 or later, and Python with Pillow. Original game files
and downloaded development dependencies are not included in Git.

Prepare the generated sprite assets using the commands in the root README.
Place an official Windows x64 `openxr_loader.dll` at
`native/generated/openxr_loader.dll` before building; the directory is ignored.

```powershell
tools/build_native.ps1
native/build/Release/LlamatronNative.exe
```

The default is a read-only native desktop preview. It finds the development
capture state under `artifacts/effects-probe`; `--runtime` overrides that path.
Default read-only mode lets the existing game retain its current input owner.

```powershell
native/build/Release/LlamatronNative.exe --smoke --snapshot artifacts/native-preview.png
native/build/Release/LlamatronNative.exe --xr-probe
native/build/Release/LlamatronNative.exe --vr
```

For the current development test, start `python tools/probe_native_effects.py
--effects --audio`, resume that isolated emulator through the debug bridge, then
run `Test Native.cmd`. The launcher uses `--vr --control` and records startup
output in `artifacts/native-user-test.log`. It requires a running OpenXR runtime
and an already running emulator; it does not start the game itself.

`--control` enables native input writes for an isolated test session. Concurrent
browser input must be stopped before using that option. This is development
input ownership, not the final launch flow.

## Verified results

1. Release build passed with MSVC 14.44 and the Windows SDK. The initial
   standalone renderer executable is approximately 3 MiB.
2. The native offscreen smoke check attached to the live recorded emulator and
   rendered completed frame 61188 with 36 sprite draws and native effects through
   D3D11 WARP. Output: `artifacts/native-preview.png` (1280 by 800).
3. The PNG was visually inspected, and its background pixel matched the
   expected RGB (6, 9, 14). Build output: `artifacts/native-build.log`.
4. A normal Windows launch through `Test Native.cmd` entered the headset, and
   the user confirmed native VR rendering and left-thumbstick movement.
   Eye render targets explicitly use the negotiated swapchain format, and
   adapter selection uses DXGI 1.1 so SteamVR can share the eye textures.
   Runtime-side logs are under `artifacts/native-xr-logs`. Other controller
   profiles and sustained performance have not been validated.

## Remaining integration

1. Give the native application ownership of emulator startup, shutdown, audio
   and game state, replacing the Python/debug-server launch path.
2. Bundle game/emulator assets and required licenses into the final application.
   The current capture reader depends on a separately running development game.
3. Validate both controllers, stereo placement, bullet orientation and original
   effect appearance more thoroughly in the native headset session.
4. Extend native effect coverage and measure headset performance under load.

The headers in `vendor/openxr` are Khronos OpenXR 1.1.49 (license included).
The application requests OpenXR 1.0 plus `XR_KHR_D3D11_enable`. Generated asset
code, build output and the development loader copy stay outside versioned source.
