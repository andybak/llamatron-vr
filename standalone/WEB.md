# Browser preview

1. Use **Llamatron > Build Web preview** in Unity 6000.3.18f1 with Web Build Support
   installed. It writes the complete static app to `artifacts/standalone/web`.
   The existing generated sprite and audio assets must be present; regenerate
   audio with `tools/recover_audio.py` to include the `sample-XX-pcm.bytes` files.
2. Start the local server from the repository root:

   ```powershell
   .tools/python/python.exe tools/serve_web.py
   ```

   Open `http://127.0.0.1:8080`. Select **Start game** to enable browser audio and
   play. WASD/arrows move, Space locks firing direction, P pauses and R restarts.
   **Enter VR** appears when the browser reports immersive VR support. Headset
   input uses the same stick/trigger controls as the native preview.
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
3. Configure repository secrets `UNITY_EMAIL`, `UNITY_PASSWORD`, and either
   `UNITY_LICENSE` (an activated CI license file) or `UNITY_SERIAL` (a suitable
   paid license). Follow [GameCI activation](https://game.ci/docs/github/activation/).
   A local Editor activation is not automatically available to GitHub runners.
   The workflow fails early with a setup message when credentials are missing.
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

Package source and license:
[WebXR Export 0.19.0](https://github.com/De-Panther/unity-webxr-export/tree/1454bd1a6716cbcd51d4013d780d97754be97d5d/Packages/webxr),
Apache-2.0. The dependency is pinned in the Unity package manifest/lockfile; local
analysis copies under `.tools` are not part of the project or distribution.
