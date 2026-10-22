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

Package source and license:
[WebXR Export 0.19.0](https://github.com/De-Panther/unity-webxr-export/tree/1454bd1a6716cbcd51d4013d780d97754be97d5d/Packages/webxr),
Apache-2.0. The dependency is pinned in the Unity package manifest/lockfile; local
analysis copies under `.tools` are not part of the project or distribution.
