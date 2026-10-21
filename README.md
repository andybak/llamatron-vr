# Llamatron Native

A native Windows OpenXR prototype of DOS Llamatron, with extruded sprites and
the original game's pixel effects. Native headset rendering and left-thumbstick
movement were confirmed on 19 October 2026.

## Use it

Download and fully extract a Windows ZIP from
[GitHub Releases](https://github.com/andybak/llamatron-vr/releases) or the
`Windows-<commit>` artifact in [Actions](https://github.com/andybak/llamatron-vr/actions/workflows/windows.yml).
Actions artifacts contain the distribution ZIP and its SHA-256 file; extract the
distribution ZIP as well before running the scripts.

1. **Get the original DOS game if needed.** Download and extract
   [Llamatron from DOS Games Archive](https://www.dosgamesarchive.com/download/llamatron),
   or use your existing copy. Keep the whole game folder, including `GAME86.EXE`,
   `LLAMA.EXE` and `LLAMA.INF`. The Atari ST version is not supported by this build.
2. **Run `Setup.cmd` once on a fresh installation.** Select your original game
   folder when asked. In the prebuilt ZIP, all runtime dependencies and the native
   app are included: setup only prepares a separate patched game copy and needs
   no downloads, compiler, administrator approval or system Python. Its diagnostic
   emulator is silent and closes automatically.
3. **Connect your PC VR headset and run `Play.cmd`.** Have your headset's PC VR
   software running with an active OpenXR runtime. Play starts and resumes the
   patched game and launches native VR. Close the native app, or press Ctrl+C
   in the Play console, to stop both the app and its emulator, including sound.

Use 64-bit Windows with a working PC VR headset/runtime. This is not a standalone
Quest app. The original game folder is left untouched. Setup remembers its path;
there is no source file to edit. Repeat setup after source updates that require a
rebuild. Subsequent play sessions do not rebuild, extract sprites or download tools.

From a source checkout, the same Setup script additionally downloads its build
dependencies, captures the palette, extracts sprites and compiles the app. A fresh
source build may require approval to install C++ Build Tools or a Windows restart.

## Help

1. Run `Setup.cmd` again to repair an incomplete setup. To choose a different
   game folder, use `Setup.cmd -GamePath "D:\Games\Llamatron"` from a terminal.
2. Setup details are in `artifacts/setup.log`. Game startup details are in
   `artifacts/native-runtime/bridge-launch.log`; native startup details are in
   `artifacts/native-user-test.log`. OpenXR logs are under `artifacts/native-xr-logs`.
3. In SteamVR, check Settings > OpenXR and select SteamVR as the OpenXR runtime
   if you intend to use it. Merely installing the OpenXR loader does not install
   or select a headset runtime. A previously stuck SteamVR connection required
   quitting and restarting SteamVR during development.
4. `Play.cmd --mute` disables original game audio. `Test Native.cmd` is a
   compatibility shortcut to Play and uses the same automatic startup/cleanup.

[Native implementation and developer checks](native/README.md) and
[native effect capture](docs/native-effects.md) describe the internals and limits.
Downloaded dependencies and generated game assets stay outside Git. The
`browser-prototype-archive` branch preserves the earlier browser and controller
experiments; they are not used by Setup or Play.

## Continuous builds

Each commit pushed to `main` produces a complete Windows ZIP and SHA-256 file as
an Actions artifact. Batched pushes build each commit separately. A commit whose
message contains the exact marker `[RELEASE]` also publishes its ZIP as a GitHub
Release tagged `build-<12-character-commit>`. Unmarked commits publish no release.
Manual Actions runs build the selected revision without creating a release.

CI downloads a checksum-pinned original DOS game only to extract build assets.
The distribution includes the extracted sprites inside the native executable,
but excludes the original/patched DOS executable and the CI game folder. Users
still supply the original game in step 1. Actions artifacts expire after 30 days;
release assets remain available until removed.
