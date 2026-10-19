# Native pixel effect capture

The native renderer reads original effects from the isolated emulator under
`artifacts/effects-probe`. Instrumentation changes only a copied game executable;
the supplied game files remain unchanged.

## Findings

1. The original sprite entry is unpacked-image `8CBBh`. A negative byte at
   `DS:39D9h` selects the ordinary sprite path. Other values select the original
   transformed rasterizer at `E135h`; `DS:39DAh` and `DS:39DCh` supply its
   additional parameters. Our original event stream omitted these parameters
   and reconstructed every draw as the intact exported bitmap.
2. Object render functions `4465h` and `4499h` select that transformed path.
   The startup/spawn sequence visibly produces coloured pixel streaks through
   it. This proves a missing native rendering path, but does not prove that all
   hit, death, boss and level effects use the same path.
3. Direct video writes also exist: `4738h` draws a vertical beam and `47F8h`
   fills a horizontal stripe. These separate paths are not yet captured.
4. The game uses planar 320 by 200 video pages at offsets 0000h and 2000h,
   with 4000h used for background/restoration. The test capture uses the spare
   6000h page, occupying 6000h through 7F3Fh in each colour plane. Later game
   modes that might reuse this page remain unverified.

## Implementation

1. At frame begin, a register-preserving helper clears the spare page and
   installs a nine-byte adapter in the completed startup entry at `0000:0010`.
   That adapter calls the original near-return rasterizer and returns far to
   the appended hook segment. This changes only the experimental game copy.
2. For transformed sprites targeting the current draw page, the hook invokes
   the original rasterizer once on the spare page, disables dirty-area list
   recording for that duplicate, restores the page/list state, and continues
   the normal draw. It does not replay game updates or the object render loop.
3. The frame-end hook copies four spare-page bitplanes into alternate guest
   RAM buffers. It also copies the game's sixteen RGB triples at `DS:4451h`,
   used by its BIOS DAC uploads for entries 0..7 and 16..23. Graphics-controller
   index, read plane and read mode are preserved. The clearing helper also
   preserves the write configuration it changes.
4. A publication counter guards pixels and palette against partial reads.
   The server publishes sprites and pixels with matching frame sequences,
   keeping the previous coherent pair if the next capture is incomplete.
5. The native renderer decodes bitplanes and displays a transparent arena texture.
   Corresponding intact sprite reconstructions are omitted. Effect motion,
   lifetimes, randomization and animation remain entirely in the DOS game.

## Validation and limits

1. Original full-frame capture and isolated capture were launched and visually
   inspected in separate diagnostic runs. Ordinary characters and bullets were
   absent from the isolated image.
2. An 80-sample trace contained 80 distinct frames, zero mismatched sprite/effect
   sequences and 34 samples with nonzero effect pixels, up to 499 pixels.
   Evidence: `artifacts/effects-probe/frame-checks.json` and `effects-sampled.png`.
3. Checks cover four-plane decoding, palette indexing, pixel ordering,
   transparency and the existing complete-frame publication cases.
4. The layer is flat at firing height. Index zero is transparent, so black
   pixels intended to cover geometry are not represented. Ordinary sprite
   textures still use the exported palette; only this layer follows the live
   palette buffer. This is not complete original-image parity.
5. VR appearance, hit/death coverage and heavy-effect performance require
   gameplay testing. Duplicate native draws and a 32 KB capture per frame add
   emulated CPU work; headset performance has not been benchmarked.

After closing the current diagnostic emulator, launch another probe with:

```powershell
python tools/probe_native_effects.py --effects --audio
```

The probe leaves the game paused. Resume through the debug bridge and launch
`Test Native.cmd` to enter native VR. It refuses to replace another running probe. Omit `--effects` for a
full-frame diagnostic. `capture_native_frame.py` saves the latest original
planar pixels and a decoded PNG from either probe type.
