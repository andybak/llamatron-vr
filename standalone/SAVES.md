# Saves and reproducible test states

1. **F5** quick-saves and **F9** quick-loads. In VR, the left controller's secondary
   face button saves and the right controller's secondary face button loads
   (Y and B on Quest Touch controllers). The HUD confirms the result.
2. Browser controls offer **Quick save**, **Quick load**, **Export save** and
   **Import save** after starting the game. The quick slot is stored locally in
   that browser. Export a `.llsave` file to keep/share it or transfer it between
   desktop and VR. Native quick saves live at
   `Application.persistentDataPath/quicksave.llsave`; `--load-save <absolute-file>`
   loads a portable state at startup.
3. Saves capture the current simulation and campaign, including partial spawning,
   all 100 actor records and retained words, bullets, both player histories and
   campaign packets, mutable wave scripts, parameters, random table/cursor,
   palette, interrupt/display clocks, queued/active sound and playback position.
   Last-drawn commands and wipe progress are saved too: loading does not perform
   another gameplay draw or advance animation counters. Headset pose and browser
   permissions remain live device state, outside the save.
4. The portable version-1 format includes a SHA-256 checksum and bounded parsing.
   Core loading builds a separate session before replacing the running one.
   Future incompatible layouts must increment the format version. Loading a save
   made by a different gameplay revision does not establish original-game parity.
5. Create a reproducible state for any wave without launching Unity:

   ```powershell
   dotnet run --project standalone/StateTool -- create --wave 69 --seed 17 --ticks 220 --save artifacts/save-states/wave-69-tick-220.llsave
   dotnet run --project standalone/StateTool -- inspect --load artifacts/save-states/wave-69-tick-220.llsave
   dotnet run --project standalone/StateTool -- replay --load artifacts/save-states/wave-69-tick-220.llsave --ticks 500 --input 5 --save artifacts/save-states/wave-69-later.llsave
   ```

   `--mode` uses 0 solo, 1 droid, 3 simultaneous players, 4 alternating solo,
   5 alternating droid or 7 alternating simultaneous players. `--cycle` sets the
   original difficulty cycle; `--twin-stick true` selects separate move/fire input.
   Input bytes use up=1, down=2, left=4, right=8, fire/lock=128; `--secondary`
   supplies the other input. This creates an independent later-wave start with
   its carried parameter prefix, not a claim to replay all prior waves.
6. The state tool services audio with a deterministic sample-duration model.
   Unity instead captures/resumes its AudioSource cursor. Byte-identical core
   replay is checked using matching input/event timing; browser/device audio
   latency and original-machine timing are separate validation work.
