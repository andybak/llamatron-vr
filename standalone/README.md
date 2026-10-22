# Standalone reconstruction in progress

The existing `Play.cmd` still starts the DOSBox-based Windows mod. This directory
contains the replacement's portable gameplay core, with no DOS, emulator, process
memory, Windows, graphics or Unity API dependencies. The Unity presentation is an
unfinished campaign preview, not yet a complete playable replacement.

The C# source is under `Unity/Assets/Llamatron/Core` so it can be consumed directly
by the Unity 6000.3.18f1 project targeting Windows and Android OpenXR. `Core`
compiles the same files as a .NET Standard 2.1 library. `Unity` contains the scene,
OpenXR packages, keyboard/controller input and sprite presentation. Headset runtime
behavior has not been verified.

1. Recover the authoritative table map from the supplied binary:

   ```powershell
   .tools/python/python.exe tools/recover_gameplay.py .tools/ci-game-input/GAME86.EXE --csharp standalone/Unity/Assets/Llamatron/Core/OriginalWaves.cs
   ```

   Keep the original `S.CMP` beside `GAME86.EXE`, or supply its path with
   `--motion-resource`. It supplies the recovered motion lookup table for generation.

2. Compile and check the portable routines:

   ```powershell
   dotnet run --project standalone/Checks/Llamatron.Checks.csproj
   ```

3. Generate the sampled audio and sprite assets. The audio probe uses an isolated
   Sound Blaster fixture and preserves the observed sample-30 overread tail:

   ```powershell
   .tools/python/python.exe tools/probe_reference_audio.py --runtime artifacts/reconstruction/audio-reference
   .tools/python/python.exe tools/recover_audio.py --reference-memory artifacts/reconstruction/audio-reference/sample-memory.bin
   .tools/python/python.exe tools/export_standalone_assets.py
   ```

4. Open `standalone/Unity` in Unity 6000.3.18f1. Use **Llamatron > Prepare standalone
   project** to bake sprite meshes/audio imports, configure OpenXR and create the scene. This
   replaces the preview scene after offering to save any current scene edits.
   Extracted textures and baked meshes are generated locally and are not committed.

5. Read [the reconstruction evidence and remaining work](../docs/standalone-reconstruction.md).

The preview uses WASD or arrow keys to move, Space to hold firing direction, P to
pause and R to restart. In XR it reads a controller stick and trigger. Pass
`--desktop` to skip OpenXR startup, or `--smoke --capture <absolute-png-path>` to
capture a deterministic desktop frame after 120 simulation ticks and exit.
For developer checks, `--wave N` starts at wave 1 through 100. Unfinished modes
still stop the preview, and starting later does not replay earlier play.
`--smoke-ticks N` selects a later smoke capture after 1 through 10000 logic ticks
(default 120), allowing effects with longer startup delays to be inspected.
Gameplay uses the recovered IBM PIT rate, approximately 50.158988 Hz. Drawing has
its own provisional 70 Hz clock. Their events are scheduled in timestamp order,
so host refresh rate does not change the logic/display ratio. Exact retrace timing
and original hardware phase still require comparison with a DOS run.
Cleared waves advance automatically. Losing a life rebuilds the current wave from
surviving actors. On the continue screen, Space, trigger or A accepts; R starts a
new game. Later unported enemy behavior still stops the preview explicitly.

Use **Llamatron > Build Windows preview**, **Build Android XR preview**, or
**Build Quest preview** in the Editor. Outputs go to `artifacts/standalone/windows`,
`artifacts/standalone/android-xr`, and `artifacts/standalone/quest` respectively.
The Android builds select separate platform support features and use ARM64,
IL2CPP, Vulkan and Game Activity. Android packaging clears the build cache to keep
vendor manifest hooks from retaining another variant's entries. This configuration alone does not prove device
compatibility. Copy the entire Windows output folder when moving the preview;
the executable needs its adjacent Unity data and libraries.

For developer reference timing, run:

```powershell
.tools/python/python.exe tools/probe_reference_clock.py --seconds 5
```

This Windows diagnostic uses the local
instrumented DOSBox-X build and a complete original fixture. It copies inputs into
a fresh artifact directory, measures IRQ/draw counters, and closes its owned
process job. Full reports and snapshots remain under `artifacts/reconstruction`.

The check executable verifies selected boundary vectors, connected first-wave
combat/progression, and real scripts through selected later waves, including
waves 3, 5, 7, 9 and 12, without reading DOS files.
It does not run DOS
instructions or establish full gameplay, Unity, headset or Android parity. Original
asset extraction is a developer step. The intended player distribution will include
the recovered assets and need no DOS installation or executable at runtime.

`OriginalWaves.cs` contains recovered data for all 100 waves and spawn
graphics/input/collision tables. The connected core includes normal human movement,
automatic/spread firing, damage, scoring, herd rescue/following and
collectible effects. The normal and rapid moving-spawner family now has its two spawns, three
update handlers, two child generations and pickup-producing damage behavior.
The campaign controller includes normal wave advancement, survivor retries, lives,
continue countdown, game-over state and alternating-player campaign packets.
The Unity component's Play Mode selects solo, droid or simultaneous human play,
with optional alternating turns. Twin Stick separates primary movement and aim;
I/J/K/L plus Right Ctrl, or the right controller stick/trigger, supplies the second
input. Droid following and quadrant firing use the recovered original routines.
Palette Effects selects None, Cycle or Full. Indexed sprites follow the live
palette, and wave transitions use the recovered 48+47-step pixel wipe on the arena.
Attract/menu and high-score flow remain unfinished. These additions have core
checks and Unity compilation results, with live mode/visual parity still to verify.
Sampled audio
now uses the recovered single-voice queue, playback rates and priorities. Warp
completion polls its active voice after the original minimum two retraces.
All 32 clips are bundled; no original sample bank or sound driver is used at runtime.
Sample 30 includes a documented 96-byte tail from one captured DOS allocation layout.
Audible/timing parity and headset audio routing remain unverified.
The hunter family includes herd targeting/conversion and projectile bursts. The
axis-chase enemy alternates horizontal and vertical pursuit and records its trail.
The producer hunter creates axis-chase children; shooting and alarm variants
share the timed hunter movement. The center-column spawner and its two child
types include the parent's ten-offspring death transformation.
The fixed-point shooter and its later-wave directional child use recovered
16.16 movement and turning tables. The contact-deactivated emitter and falling
actor retain their separate freeze/contact behavior. Including the split and
beam families, alternating-axis dash, mutable-data spawner, recoil actors and
leader/follower chains, orbiting enemies, four edge shooters, phased pursuit
emitters and detonation effects, all 46 actor-dispatch entries, 41 spawn kinds,
18 damage handlers and 18 of 19 render-dispatch entries now have implementations.
Render entry 4 still requires an original-runtime trace of its continuation.
This does not establish original-runtime or full game parity. All twelve chase-death outcomes are
connected, including cardinal hazards, reflected shots and hunter conversion.
Beam renderers retain the original
pixel spans and captured palette index; their hard-coded head is included in
the 407 recovered sprites.
Wave scripts are mutable: the original state-19 initializer changes two later
wave-22 spawn groups. A controlled DOS runtime probe confirmed the writes, and
the standalone retains them through retries and later wave starts.
Generated data carries the original
executable's SHA-256 for reproducibility. Actual REA/Ghidra Evidence is indexed in
the reconstruction notes, including provenance for analysis-only entry hints.
