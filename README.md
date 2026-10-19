# Llamatron Native

The active delivery path is a single native PC executable using OpenXR.
The [native implementation](native/README.md) has a built Direct3D 11 renderer
and direct capture reader. Owning and packaging emulation is the next step;
the current native development preview attaches to the existing game process.
The user confirmed native headset rendering and left-thumbstick movement on
19 October 2026. `Test Native.cmd` launches the current native VR development build.

The working browser prototype is preserved in the
[milestone restore guide](milestones/20261018-213524Z-browser-native-effects/RESTORE.md).
Its archive includes source, graphics, game files and the portable emulator,
with per-file hashes and a checked ZIP integrity result.
That milestone archive is local and excluded from Git; browser prototype source
and its capture tools remain versioned here for reference.

## Browser prototype reference

The supplied DOS Llamatron binary now drives an experimental live 3D diorama.
Original gameplay runs in DOSBox-X. A small executable hook records sprites,
and a local WebGL/WebXR viewer presents them above a tabletop arena.

An experimental [native pixel-effects capture](docs/native-effects.md) is
available at http://127.0.0.1:53443/. It captures the original DOS
transformed-sprite rasterizer rather than recreating its effects in JavaScript.
This is the fixed prototype URL for future updates. Hit/death coverage and headset
appearance of the new layer still need gameplay testing.

## Try the prototype

From this workspace:

```powershell
python tools/start_prototype.py
```

To enable the supplied Sound Blaster driver on a fresh startup, add `--audio`.
Audio plays through DOSBox-X's Windows output; it is not spatialized by the viewer.
The emulated card uses port 220h, IRQ 7 and DMA 1. The current test instance has
audio enabled, and the user has confirmed audible output. A running instance
is reused, so the flag does not change sound configuration until a fresh start.

Open the local URL printed by the command. An existing running prototype is
reused. Fresh startup prepares a separate game copy with keyboard controls and
sound disabled unless `--audio` is supplied, skips the loading screens, and
leaves gameplay paused for review.

1. Click **Resume game** to play, or **Pause game** to inspect a frame.
2. Arrow keys move the llama. Space starts a game and locks the firing direction.
3. Drag the canvas to orbit and scroll to zoom. **Extruded sprites** switches
   between upright sprites with 12 mm thickness and flat sprites on the arena.
   Side faces follow the opaque pixel outline and use darker versions of the
   adjacent pixel colours. Geometry is generated once when graphics load.
   Normal and hot bullets retain their original direction in the arena plane,
   with 12 mm thickness at firing height, rather than standing upright.
4. With a compatible PC VR browser/runtime, click **Enter VR**. Either
   controller stick maps to movement and a trigger maps to Space. A Quest can
   be used as a connected PC VR headset; this is not a standalone Quest build.
   Both controllers are rescanned every XR frame, every 500 ms, and on input
   source changes. An active left stick takes priority; the right remains a
   fallback. A returning left controller is used immediately. Hidden XR sessions
   release movement until visibility returns.
   If WebXR omits a controller, `Start Controllers.cmd` starts a local SteamVR
   background reader for both hands. Fresh native input fills the missing hand
   and retains left-stick priority. Samples expire after 250 ms if the helper
   stops. It uses the same fixed viewer address and does not change VR bindings.

Desktop rendering, live draw capture, keyboard movement, browser pause/resume,
and a complete fresh startup have been checked. Chrome reports an immersive VR
session is available. The user has tested VR and confirmed the sprite flicker
is fixed and audio works. The latest controller mapping and input delivery
changes pass local mapping checks but still need a headset retry. The active
local URL and process IDs are recorded under
`artifacts/runtime/`.

The revised frame publication was sampled during play: 50 samples contained
50 distinct complete frames, no mixed frame sequences and no empty frames.
Three regression checks cover incomplete captures and page filtering. Browser
inspection showed the extruded scene without console warnings or errors. These
checks do not replace a perceptual flicker check or an audible/headset test.

This is an initial conversion, not a finished port. The capture covers the
identified sprite routine; other graphics paths, complete HUD/text, palette
animation, sprite scaling/shearing and original compositing effects still need
work. Complete frames now use explicit begin/end markers around the render pass
at `CS:E903` and `CS:E935`, before the hardware page flip. Only draws for the
published page within a matching marker pair are used. An unfinished capture
keeps the last complete frame. Graphics preload before presentation. Exact
duplicate sprite/position calls are removed; this still does not prove complete
rendering parity or establish that all possible flicker sources are resolved.

## Reproduce the offline analysis

Run from this workspace with Python 3 and Pillow:

```powershell
python tools/inspect_game.py 'F:\Emulation\DOSBox\games\llama'
python tools/export_sprites.py
```

The tools read the original directory and write only to `artifacts/` by default.
Game files and derived game assets are excluded from version control.

1. `artifacts/analysis/inventory.json` records source SHA-256 hashes, executable
   entry points, relocations, decompressed sizes and IFF image metadata.
2. `artifacts/analysis/*.image.bin` are unpacked, unrelocated DOS load images.
3. `artifacts/analysis/*.unpacked.exe` are reconstructed analysis copies with
   relocation tables. The instrumented main executable built from this image
   has run successfully; these other individual EXEs are not separately tested.
4. `artifacts/analysis/*.analysis.obj` are analysis-only COFF wrappers for LLVM.
   These objects are not compilable game sources or native executable ports.
5. `artifacts/sprites/contact-sheet.png` previews 406 distinct masked sprites.
6. `artifacts/sprites/sprites.json` maps sprite offsets to table indices.
   Individual RGBA PNGs and raw palette-index PNGs accompany it.

## Findings from these exact binaries

1. `GAME86.EXE` has two nested packing layers: LZEXE 0.91, then EXEPACK.
   The final load image is 182,880 bytes, initial entry `0000:0010`, with
   49 relocation words. Addresses below refer to this unpacked image and are
   relative to the program load segment, not fixed physical RAM addresses.
2. Startup loads DS with load-segment + `2559h`. Main graphics use
   load-segment + `118Ch`, initially stored at `CS:025F`. The G3 resource bank
   uses load-segment + `20EBh`, initially stored at `CS:0261`.
3. Resource loading starts at `CS:60AA`. Its CMP loader is `CS:E65E` and
   decompressor `CS:E6BB`. This is a 4,096-byte circular-dictionary LZSS stream
   with low-bit-first literal flags and a three-zero-byte termination escape.
   `G3.CMP` expands to 18,144 bytes, `F.CMP` to 1,710, `S.CMP` to 768.
   F and S purposes remain unconfirmed.
4. Main graphics have an initial run of 428 valid sprite pointers, including
   duplicates, giving 337 distinct directly referenced sprites. Walking enemy
   animation also advances to adjacent records without a pointer-table entry.
   Following valid records from those entries recovers another 53 sprites,
   giving 390 main-bank sprites plus 16 G3 sprites (406 total).
   The initial exporter omitted those animation frames: the captured enemy
   transition from `main-5392` to `main-5416` made the viewer silently drop the
   enemy. All sprite IDs in a 90-frame gameplay trace are now exported. Missing
   graphics produce a visible diagnostic and retain the previous valid scene.
   This does not establish coverage of every level or dynamically generated graphic.
5. Sprite records have a 12-byte header. The words at offsets 2, 4 and 6 are
   respectively the last 16-pixel block index, last row index and last column
   index. Pixel data contains a two-byte opacity mask followed by four two-byte
   colour planes per block. Mask bit 1 draws the pixel. Exported shapes have
   been visually checked. Default exports use a provisional EGA palette;
   `--palette artifacts/runtime/palette.json` uses a captured live palette.

## Candidate live extraction hook

At `CS:8CBB`, the drawing routine receives:

| Input | Meaning supported by disassembly |
| --- | --- |
| `ES:SI` | Sprite header address |
| `AX` | Horizontal centre coordinate |
| `BX` | Vertical centre coordinate |
| `DS:39D9` | Signed rendering-path selector; exact semantics unconfirmed |

The routine subtracts half the width-minus-one and height-minus-one from AX
and BX. One path draws through `CS:8CC5`; another branches to `CS:E135`.
Sprites wider than 16 pixels branch to `CS:63D2` from the first path.
Clipping checks use screen limits 319 and 199.

Capture at entry to `8CBB`, before registers change. Preserve execution and
record sprite bytes/identity, x/y and draw order. Menus also use this routine;
capture a gameplay frame before treating every draw as a gameplay entity.
Other rendering entry points may require additional hooks. Runtime capture has
confirmed recognizable llama and arena sprite addresses and coordinates.
Keyboard movement changed the llama's horizontal coordinate from 160 to 185.
Full object identities and definitive frame boundaries remain unverified.

The live scene is reconstructed from actual draw events, not image segmentation.
The new 3D placement changes the apparent overlap of cutouts; original gameplay
and collision still run inside the emulator.

## Live capture implementation

1. `tools/build_hook.py` removes both packing layers and patches a **local copy**
   of `GAME86.EXE`. The 97-byte sprite hook at the appended segment saves/restores
   registers and flags, records eight words, executes the displaced comparison,
   and jumps back to `CS:8CC0`.
2. A 64 KiB allocation contains sprite/frame hook code and 4,032 sixteen-byte ring records.
   The game's startup allocation-shrink request is adjusted to keep it reserved.
3. `tools/ring_reader.py` opens only the PID recorded for our DOSBox-X instance.
   It verifies the hook code, signature and relocated jump, then reads this
   process's ring without debugger pauses. Inputs update the emulated game's
   keyboard fields; they do not inject operating-system keyboard events.
4. `tools/debug_bridge.py` provides the loopback-only DOSBox-X debugger transport
   for startup, palette capture and pause/resume. `tools/viewer_server.py` serves
   the viewer, extracted graphics and a live sprite feed on loopback.
5. `tools/test_frame_publication.py` checks incomplete frames, draw-page filtering
   and retention of the last complete frame when a capture is truncated.
6. `tools/capture_draws.py` retains the earlier bounded breakpoint diagnostic.
   Its per-sprite pauses are not used by the live viewer.

The original directory's file hashes were checked after integration and remain
unchanged. Game binaries, generated assets, downloaded tools and runtime logs
are excluded from version control. No headset, system XR settings or permissions
were changed.

## Tooling

Local LLVM is available at
`C:\Program Files\Microsoft Visual Studio\2022\Community\VC\Tools\Llvm\bin\llvm-objdump.exe`.
Use `-D --triple=i386 --mattr=+16bit-mode` on the generated analysis object.
Start at a known instruction boundary when inspecting a routine: a full linear
listing also interprets inline data as instructions. Near-branch destinations
in LLVM output need 16-bit IP wrapping within the current segment.

The user-supplied portable DOSBox-X 2026.10.01 MinGW64 archive was verified
against its official SHA-256:
`def2d84d4415fc26c77252839b4dafb6b476e3a4204b896292e6b05505ca8c59`.
It is extracted under `.tools/dosbox-x/`. The SDL2 executable is used.
Python 3 and Pillow are required. No Unity project or engine installation is
required for this prototype.

DOSBox-X documents its control protocol, breakpoints and memory dumps in its
[debugger reference](https://github.com/joncampbell123/dosbox-x/blob/master/README.debugger).

LZEXE format reference: [UNLZEXE](https://github.com/mywave82/unlzexe).
