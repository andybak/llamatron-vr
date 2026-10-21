# Llamatron VR

Play the original DOS Llamatron in VR, with sprites given depth and the game's
original pixel effects and sound.

For 64-bit Windows and a PC VR headset. To use a Quest, connect it to your PC
using your usual PC VR connection.

Download and extract the [latest Windows release](https://github.com/andybak/llamatron-vr/releases/latest).

## Start playing

1. **Get the original DOS game if needed.** Download and extract
   [Llamatron](https://www.dosgamesarchive.com/download/llamatron), or use your
   existing copy. Keep the whole game folder.
2. **Run `Setup.cmd` once.** Select the game folder when asked. Setup prepares
   the game and keeps your original files unchanged. The release includes the
   required software.
3. **Run `Play.cmd`.** Have your headset connected and its PC VR software
   running. The game starts automatically.

## Controls

1. Move with the **left thumbstick**. Your llama fires automatically.
2. Hold a **trigger** to lock your firing direction while moving.
3. Close the game window to quit. This also stops the sound.

## Help

1. If setup fails, check that you selected the extracted DOS game folder
   containing `LLAMA.EXE`, `GAME86.EXE` and `LLAMA.INF`. Run `Setup.cmd` again.
2. If nothing appears in the headset, check that your PC VR software can see
   the headset. If using SteamVR, check Settings > OpenXR and make sure SteamVR
   is selected as the OpenXR runtime. Restart SteamVR if the connection is stuck.
3. If you need help, [report an issue](https://github.com/andybak/llamatron-vr/issues)
   and include `artifacts/setup.log` or `artifacts/native-user-test.log`.

[Developer guide](native/README.md)
