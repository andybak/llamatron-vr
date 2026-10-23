# Browser preview

1. Use **Llamatron > Build Web preview** in Unity 6000.3.18f1 with Web Build Support
   installed. It writes the complete static app to `artifacts/standalone/web`.
   The existing generated sprite and audio assets must be present; regenerate
   audio with `tools/recover_audio.py` to include the `sample-XX-pcm.bytes` files.
2. Start the local server from the repository root:

   ```powershell
   .tools/python/python.exe tools/serve_web.py
   ```

   Open `http://127.0.0.1:8080`. Select **Open game** to enable browser audio and
   open the title menu. Choose solo, droid partner or two players, with optional
   alternating turns and twin-stick aim, then **Start game**. WASD/arrows move,
   Space locks firing direction, P pauses and M opens the menu without advancing
   the suspended game. Mouse clicks or arrows/Enter select menu items.
   **Enter VR** appears when the browser reports immersive VR support. Headset
   input uses the same stick/trigger controls as the native preview.
   Menu navigation also accepts stick up/down and trigger; A/X returns to the menu.
   **How to play > Field guide** shows the original illustrated instructions.
   Idle title screens rotate through the original story, three-wave AI demos and
   credits. **Watch demo** starts a demo immediately; Space/trigger leaves it.
   Qualifying games offer eight-character name entry and retain the top ten scores
   locally across visits. Test-wave starts do not submit high scores.
3. Upload the entire web output directory to a static HTTPS host to distribute
   it. Relative paths support hosting in a subdirectory. Unity's Gzip
   decompression fallback supports hosts without custom compression headers.
   Opening `index.html` as a local file does not work.
4. The manifest and service worker support app installation in browsers that
   offer it. The worker precaches the complete build; offline use becomes
   available after that installation completes. Browser storage quotas, browser
   support and eviction still apply. Use **Install app** when offered, or the
   browser's installation menu.
5. WebXR requires HTTPS or a trusted localhost origin and a compatible
   browser/headset. This build pins WebXR Export 0.19.0 from OpenUPM because it
   supports the project's Built-in renderer; version 0.20 and later dropped that
   rendering path. The adapter owns its two eye cameras and receives headset
   poses, projection matrices and controller data through the package's events.
   Windows and Android retain their OpenXR setup.
6. Web audio is created from the recovered unsigned PCM bytes at runtime, with
   the original frame counts and pitch-based playback rates. It bypasses Web
   AAC clip conversion. Gameplay waits for a Start gesture, and the page resumes
   Unity's Web Audio context during that gesture. Analog/DSP timing parity is
   still subject to the browser audio implementation.

This remains the reconstruction preview. A successful browser build does not
prove immersive stereo, controller or audio behavior on physical headsets.

## GitHub Pages releases

1. `.github/workflows/web-pages.yml` builds every commit pushed to `main` whose
   message contains the exact marker `[RELEASE]`. Other commits skip the web
   build and deployment. Each marked commit has a downloadable `Web-<sha>`
   artifact; when a push includes several marked commits, the newest marked
   build becomes the live Pages site after all builds pass.
2. Set repository **Settings > Pages > Source** to **GitHub Actions**. Deployment
   uses the `github-pages` environment and the workflow token's Pages/OIDC
   permissions. The expected project URL is
   `https://andybak.github.io/llamatron-vr/`.
3. Licensing follows [Open Brush's build workflow](https://github.com/icosa-foundation/open-brush/blob/main/.github/workflows/build.yml):
   without a custom license it uses the same public Personal license and test
   account. The license is stored in `tools/ci/unity-personal.ulf`; provenance is
   documented beside it. This path does not require repository secrets.
   To override it, set `UNITY_LICENSE` or `UNITY_SERIAL`, plus `UNITY_PASSWORD`
   and `UNITY_EMAIL` (a secret or repository variable). Incomplete custom
   credentials fail early. See [GameCI activation](https://game.ci/docs/github/activation/)
   for custom licenses. Compatibility with this project's Editor version still
   needs a successful GitHub build.
4. CI uses Unity 6000.3.18f1 on a GitHub-hosted Linux runner and calls
   `Llamatron.Editor.StandaloneBuild.BuildWebCi`. It bakes the generated sprite
   library and makes a release build with Gzip decompression fallback. It does
   not recreate the scene or change the native OpenXR configuration.
5. `python tools/prepare_unity_web.py` downloads and verifies the same pinned
   original archive as Windows CI, then recreates sprite atlases and PCM assets.
   Install `Pillow==11.3.0` first. `--archive <path>` accepts a cached copy.
   The small fixture `tools/fixtures/unity-web-profile.json` preserves the
   captured palette and the existing 96-byte sample-30 overread profile; CI
   does not invent padding or require a live emulator capture.
6. GitHub caches the Unity `Library` (including package, shader and incremental
   build data) together with generated assets and their Unity metadata. Cache
   keys include runner OS/architecture, Editor version, package configuration,
   project sources/settings and asset-generation inputs. A source change can
   restore the newest cache with the same Editor and packages; an Editor or
   package change starts cold. Asset generation and the Unity build still run
   on cache hits. Only the newest marked commit saves a cache, after output
   verification succeeds. Increase `UnityWebGL-v1` in both workflow key prefixes
   to discard existing caches if necessary. Caches are optional accelerators;
   GitHub can evict them and the workflow can rebuild from source.

## Browser updates and offline play

1. Online visits and normal reloads check the network for HTML. Unity output uses
   content-hashed filenames; the loader, stylesheet, update helper and manifest
   use explicit build versions. An older cached file cannot be mistaken for a
   new build's file. The offline manifest records the exact URLs, including query
   strings, and is verified during the build and in CI.
2. While playing, **Update available** captures a temporary update save before
   reloading. Select **Resume saved game** on the new welcome screen to restore
   it and enable audio. This handoff does not replace your manual quick-save slot.
   If capture/storage fails, updating is cancelled. If restoration fails, a copy
   remains available through **Download previous game**.
3. The worker checks for updates on visits, returning to the tab and periodically
   while visible. New workers activate without requiring all tabs to close;
   running older pages keep their own cached assets. Cleanup removes only older
   caches belonging to this site's scope, once all its tabs report the current
   version and the current offline bundle is complete.
4. Offline navigation uses a completed page/asset bundle. A partial installation
   cannot replace that fallback. Storage limits or unavailable caches do not block
   online play. An already open older page can continue playing until you apply
   its update or normally reload; a forced refresh is not part of the update flow.

Package source and license:
[WebXR Export 0.19.0](https://github.com/De-Panther/unity-webxr-export/tree/1454bd1a6716cbcd51d4013d780d97754be97d5d/Packages/webxr),
Apache-2.0. The dependency is pinned in the Unity package manifest/lockfile; local
analysis copies under `.tools` are not part of the project or distribution.
