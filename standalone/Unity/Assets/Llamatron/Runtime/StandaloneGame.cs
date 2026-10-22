using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using Llamatron.Core;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.XR;
using UnityEngine.XR;
using UnityEngine.XR.Management;
using CommonUsages = UnityEngine.XR.CommonUsages;

namespace Llamatron.Unity
{
    public sealed class StandaloneGame : MonoBehaviour
    {
        public Camera view;
        public float displayHz = 70; // Provisional VGA retrace rate; independent of the recovered PIT clock.
        public PaletteEffects paletteEffects = PaletteEffects.Full;
        private GameSimulation game;
        private GameCampaign campaign;
        private GameClock clock;
        private readonly SpriteFrame frame = new SpriteFrame();
        private readonly WaveScriptStore waveScripts = new WaveScriptStore();
        private ArenaRenderer arena;
        private SampledAudio sampledAudio;
        private TextMesh hud;
        private bool ready, xr, paused, previousRestart, captured;
        private bool smokeFrameReady, systemPaused, skipNextDelta;
        private int ticks;
        private string fault;
        private bool smoke;
        private int smokeTicks = 120;
        private InputAction headPosition, headRotation, headTracking;

        private IEnumerator Start()
        {
            smoke = HasArgument("--smoke");
            SpriteLibrary library = Resources.Load<SpriteLibrary>("Generated/SpriteLibrary");
            if (library == null) { Debug.LogError("[LLUNITY] Bake the standalone sprite library before running."); yield break; }
            arena = new GameObject("Arena presentation").AddComponent<ArenaRenderer>();
            arena.Initialize(library);
            sampledAudio = gameObject.AddComponent<SampledAudio>(); sampledAudio.Initialize();
            CreateHud(arena.ArenaBounds);
            if (!smoke && !HasArgument("--desktop"))
            {
                XRManagerSettings manager = XRGeneralSettings.Instance != null ? XRGeneralSettings.Instance.Manager : null;
                if (manager != null)
                {
                    yield return manager.InitializeLoader();
                    if (manager.activeLoader != null)
                    {
                        manager.StartSubsystems(); xr = true;
                        var origin = new GameObject("XR origin");
                        // Tracked forward is +Z; the arena is positioned on the -Z side.
                        origin.transform.rotation = Quaternion.Euler(0, 180, 0);
                        view.transform.SetParent(origin.transform, false);
                        view.transform.localPosition = Vector3.zero; view.transform.localRotation = Quaternion.identity;
                        var inputs = new List<XRInputSubsystem>(); SubsystemManager.GetSubsystems(inputs);
                        bool floor = false;
                        foreach (XRInputSubsystem input in inputs) floor |= input.TrySetTrackingOriginMode(TrackingOriginModeFlags.Floor);
                        if (!floor) origin.transform.position = new Vector3(0, 1.6f, 0);
                        headPosition = new InputAction("Head position", InputActionType.Value, "<XRHMD>/centerEyePosition", expectedControlType: "Vector3");
                        headRotation = new InputAction("Head rotation", InputActionType.Value, "<XRHMD>/centerEyeRotation", expectedControlType: "Quaternion");
                        headTracking = new InputAction("Head tracking", InputActionType.Value, "<XRHMD>/trackingState", expectedControlType: "Integer");
                        var pose = view.gameObject.AddComponent<TrackedPoseDriver>();
                        pose.positionInput = new InputActionProperty(headPosition);
                        pose.rotationInput = new InputActionProperty(headRotation);
                        pose.trackingStateInput = new InputActionProperty(headTracking);
                    }
                    else Debug.LogWarning("[LLUNITY] OpenXR initialization unavailable; using desktop camera.");
                }
            }
            if (!Restart()) yield break;
            ready = true;
            Debug.Log($"[LLUNITY] Standalone ready; XR={xr}; sprites={library.graphics.Length}; irqHz={clock.InterruptHz:F6}; displayHz={clock.DisplayHz}; DOS runtime dependencies=0");
        }

        private void CreateHud(Bounds arenaBounds)
        {
            var go = new GameObject("Score and status"); hud = go.AddComponent<TextMesh>();
            hud.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            go.GetComponent<MeshRenderer>().sharedMaterial = hud.font.material;
            hud.characterSize = 0.014f; hud.fontSize = 56; hud.anchor = TextAnchor.UpperCenter; hud.alignment = TextAlignment.Center;
            hud.color = new Color(0.7f, 1, 0.85f); hud.text = "LLAMATRON VR\nRECONSTRUCTION PREVIEW";
            go.transform.position = new Vector3(arenaBounds.center.x, arenaBounds.max.y + 0.55f, arenaBounds.min.z);
            go.transform.rotation = Quaternion.Euler(0, 180, 0);
        }

        private bool Restart()
        {
            if (HasArgument("--smoke-ticks") && (!int.TryParse(ArgumentValue("--smoke-ticks"), out smokeTicks) || smokeTicks < 1 || smokeTicks > 10000))
            {
                Debug.LogError("[LLUNITY] --smoke-ticks requires a number from 1 to 10000.");
                Application.Quit(2); return false;
            }
            int waveNumber = 1;
            if (HasArgument("--wave") && (!int.TryParse(ArgumentValue("--wave"), out waveNumber) || waveNumber < 1 || waveNumber > 100))
            {
                Debug.LogError("[LLUNITY] --wave requires a number from 1 to 100.");
                Application.Quit(2); return false;
            }
            var random = new System.Random(smoke ? 12345 : Environment.TickCount);
            sampledAudio.Driver.StopAll();
            game = new GameSimulation(RandomSequence.Shuffle(() => (ushort)random.Next(65536)), waveIndex: waveNumber - 1,
                                      scripts: waveScripts, sound: sampledAudio.Driver);
            game.Palette.Effects = paletteEffects;
            arena.ApplyPalette(game.Palette);
            campaign = new GameCampaign(game);
            clock = new GameClock(displayHz);
            campaign.FrameReady += simulation => { frame.Build(simulation); arena.Present(frame); };
            campaign.WipeReady += arena.PresentWipe;
            fault = null; paused = false; ticks = 0; captured = false; smokeFrameReady = false;
            campaign.TickDisplay(); UpdateHud();
            return true;
        }

        private void Update()
        {
            if (!ready) return;
            Keyboard keyboard = Keyboard.current;
            UnityEngine.XR.InputDevice left = InputDevices.GetDeviceAtXRNode(XRNode.LeftHand);
            UnityEngine.XR.InputDevice right = InputDevices.GetDeviceAtXRNode(XRNode.RightHand);
            bool restart = Button(left, CommonUsages.primaryButton) || Button(right, CommonUsages.primaryButton);
            bool ended = campaign.Phase == CampaignPhase.GameOver || campaign.Phase == CampaignPhase.Menu;
            if ((keyboard != null && keyboard.rKey.wasPressedThisFrame) || (restart && !previousRestart && (ended || fault != null))) Restart();
            previousRestart = restart;
            if (keyboard != null && keyboard.pKey.wasPressedThisFrame) paused = !paused;
            if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame && !xr) Application.Quit();
            campaign.Paused = paused;
            if (!systemPaused && !skipNextDelta && fault == null)
            {
                byte input = smoke ? (byte)0 : ReadInput(keyboard, left, right);
                bool acceptContinue = !smoke && (restart || (input & 128) != 0);
                try
                {
                    clock.Advance(Time.unscaledDeltaTime, () =>
                    {
                        sampledAudio.ServiceInterrupt();
                        campaign.TickInterrupt(input, acceptContinue: acceptContinue); ticks++;
                    }, () =>
                    {
                        sampledAudio.PollCompletion(); campaign.TickDisplay();
                        smokeFrameReady = smoke && (ticks >= smokeTicks || campaign.Phase == CampaignPhase.GameOver || campaign.Phase == CampaignPhase.Menu);
                    }, () => smokeFrameReady);
                }
                catch (NotSupportedException exception)
                {
                    fault = exception.Message; Debug.LogWarning($"[LLUNITY] Reconstruction stopped: {fault}");
                }
                catch (Exception exception)
                {
                    fault = exception.Message; Debug.LogException(exception);
                }
            }
            skipNextDelta = false;
            paused = campaign.Paused;
            arena.ApplyPalette(game.Palette);
            if (smoke) sampledAudio.ObserveOutput();
            UpdateHud();
            if (smoke && !captured && (smokeFrameReady || fault != null))
            {
                captured = true; Capture(ArgumentValue("--capture") ?? "standalone-preview.png");
                Debug.Log($"[LLUNITY] Smoke completed; ticks={ticks}; displays={clock.DisplayCount}; outcome={game.Actors.Outcome}; fault={fault ?? "none"}; score={game.Actors.Scores.Value(0)}");
                Debug.Log($"[LLAUDIO] Smoke voice starts={sampledAudio.PlayedCount}; stops={sampledAudio.StoppedCount}; active={sampledAudio.Driver.ActiveSample}; priority={sampledAudio.Driver.Priority}; peak={sampledAudio.PeakOutput:F6}.");
                Application.Quit(fault == null ? 0 : 1);
            }
        }

        private void OnApplicationPause(bool pause)
        {
            if (systemPaused && !pause) skipNextDelta = true;
            systemPaused = pause;
            if (sampledAudio != null) sampledAudio.SetSystemPaused(pause);
        }

        private void UpdateHud()
        {
            string status = fault != null ? "This part of the reconstruction is unfinished.\nR / A: restart" :
                paused ? "PAUSED" : campaign.Phase == CampaignPhase.Continue ? $"CONTINUE? {campaign.ContinueCount} · SPACE / TRIGGER" :
                campaign.Phase == CampaignPhase.GameOver ? "GAME OVER · R / A: new game" :
                campaign.Phase == CampaignPhase.Menu ? "R / A: new game" :
                campaign.Phase == CampaignPhase.WarpAudio ? "WARP" :
                campaign.Phase != CampaignPhase.Playing ? "WAVE CLEARED" : "RECONSTRUCTION PREVIEW";
            arena.gameObject.SetActive(campaign.Phase != CampaignPhase.Continue && campaign.Phase != CampaignPhase.GameOver && campaign.Phase != CampaignPhase.Menu);
            hud.text = $"LLAMATRON VR · WAVE {campaign.WaveNumber}\nSCORE {game.Actors.Scores.Value(0):D8}   LIVES {game.Actors.Lives}\n{status}";
        }

        private static byte ReadInput(Keyboard keyboard, UnityEngine.XR.InputDevice left, UnityEngine.XR.InputDevice right)
        {
            byte input = 0;
            if (keyboard != null)
            {
                if (keyboard.wKey.isPressed || keyboard.upArrowKey.isPressed) input |= 1;
                if (keyboard.sKey.isPressed || keyboard.downArrowKey.isPressed) input |= 2;
                if (keyboard.aKey.isPressed || keyboard.leftArrowKey.isPressed) input |= 4;
                if (keyboard.dKey.isPressed || keyboard.rightArrowKey.isPressed) input |= 8;
                if (keyboard.spaceKey.isPressed) input |= 128;
            }
            Vector2 axis = Vector2.zero;
            if (!left.TryGetFeatureValue(CommonUsages.primary2DAxis, out axis) || axis.sqrMagnitude < 0.04f)
                right.TryGetFeatureValue(CommonUsages.primary2DAxis, out axis);
            if (axis.y > 0.25f) input |= 1; if (axis.y < -0.25f) input |= 2;
            if (axis.x < -0.25f) input |= 4; if (axis.x > 0.25f) input |= 8;
            if (Button(left, CommonUsages.triggerButton) || Button(right, CommonUsages.triggerButton)) input |= 128;
            return input;
        }

        private static bool Button(UnityEngine.XR.InputDevice device, InputFeatureUsage<bool> usage) => device.TryGetFeatureValue(usage, out bool value) && value;
        private static bool HasArgument(string value) => Array.IndexOf(Environment.GetCommandLineArgs(), value) >= 0;
        private static string ArgumentValue(string value)
        {
            string[] args = Environment.GetCommandLineArgs(); int index = Array.IndexOf(args, value);
            return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
        }

        private void Capture(string path)
        {
            var target = new RenderTexture(1280, 800, 24); var pixels = new Texture2D(1280, 800, TextureFormat.RGB24, false);
            RenderTexture previous = RenderTexture.active; RenderTexture oldTarget = view.targetTexture;
            view.targetTexture = target; view.Render(); RenderTexture.active = target;
            pixels.ReadPixels(new Rect(0, 0, 1280, 800), 0, 0); pixels.Apply();
            string full = Path.GetFullPath(path); Directory.CreateDirectory(Path.GetDirectoryName(full)); File.WriteAllBytes(full, pixels.EncodeToPNG());
            view.targetTexture = oldTarget; RenderTexture.active = previous;
            target.Release(); Destroy(target); Destroy(pixels);
            Debug.Log($"[LLUNITY] Captured {full}");
        }

        private void OnDestroy()
        {
            headPosition?.Dispose(); headRotation?.Dispose(); headTracking?.Dispose();
            if (xr && XRGeneralSettings.Instance != null)
            {
                XRGeneralSettings.Instance.Manager.StopSubsystems(); XRGeneralSettings.Instance.Manager.DeinitializeLoader();
            }
        }
    }
}
