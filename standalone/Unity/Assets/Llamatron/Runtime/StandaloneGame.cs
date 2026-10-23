using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using Llamatron.Core;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.XR;
using UnityEngine.XR;
using UnityEngine.XR.Management;
#if UNITY_WEBGL && !UNITY_EDITOR
using Llamatron.Web;
#endif
using CommonUsages = UnityEngine.XR.CommonUsages;

namespace Llamatron.Unity
{
    public enum PlayMode { Solo = 0, Droid = 1, TwoPlayers = 3, Alternating = 4, AlternatingDroid = 5, AlternatingTwoPlayers = 7 }

    public sealed class StandaloneGame : MonoBehaviour
    {
        public Camera view;
        public float displayHz = 70; // Provisional VGA retrace rate; independent of the recovered PIT clock.
        public PaletteEffects paletteEffects = PaletteEffects.Full;
        public PlayMode playMode;
        public bool twinStick;
        private GameSimulation game;
        private GameCampaign campaign;
        private GameClock clock;
        private GameSession session;
        private WaveScriptStore waveScripts = new WaveScriptStore();
        private const string SaveKey = "LlamatronSave.v1";
        private bool previousSave, previousLoad;
        private string saveNotice;
        private float saveNoticeUntil;
        private ArenaRenderer arena;
        private SampledAudio sampledAudio;
        private PixelText hud;
        private GamePresentation presentation;
        private bool ready, xr, paused, captured;
        private bool smokeFrameReady, systemPaused, skipNextDelta;
        private int ticks;
        private string fault;
        private bool smoke;
        private int smokeTicks = 120;
        private InputAction headPosition, headRotation, headTracking;
#if UNITY_WEBGL && !UNITY_EDITOR
        private BrowserPlatform browser;
        [DllImport("__Internal")] private static extern void LLWebPresentationState(int state);
        private bool? reportedPause;
        [DllImport("__Internal")] private static extern void LLWebPauseState(int paused);
        [DllImport("__Internal")] private static extern void LLWebDownloadSave(string data);
        [DllImport("__Internal")] private static extern void LLWebSaveNotice(string message);
        [DllImport("__Internal")] private static extern void LLWebGameStarted();
        [DllImport("__Internal")] private static extern void LLWebImportResult(int success);
        [DllImport("__Internal")] private static extern void LLWebUpdateState(string data);
#endif

        private IEnumerator Start()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            // Let HTML fields receive typing; gameplay receives keys when the canvas has focus.
            WebGLInput.captureAllKeyboardInput = false;
#endif
            smoke = HasArgument("--smoke");
            SpriteLibrary library = Resources.Load<SpriteLibrary>("Generated/SpriteLibrary");
            if (library == null) { Debug.LogError("[LLUNITY] Bake the standalone sprite library before running."); yield break; }
            arena = new GameObject("Arena presentation").AddComponent<ArenaRenderer>();
            arena.Initialize(library);
            sampledAudio = gameObject.AddComponent<SampledAudio>(); sampledAudio.Initialize();
            CreateHud(arena.ArenaBounds, library.material);
#if UNITY_WEBGL && !UNITY_EDITOR
            browser = gameObject.AddComponent<BrowserPlatform>();
            yield return browser.Initialize(view);
            while (!browser.Started) yield return null;
#else
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
#endif
            if (!Restart()) yield break;
            presentation = gameObject.AddComponent<GamePresentation>();
            presentation.Initialize(this, hud, library, !HasArgument("--presentation-check"));
#if !UNITY_WEBGL || UNITY_EDITOR
            string initialSave = ArgumentValue("--load-save");
            if (!string.IsNullOrEmpty(initialSave)) RestoreSave(File.ReadAllBytes(initialSave));
            else if (smoke || HasArgument("--wave")) presentation.Loaded(false);
#endif
            ready = true;
#if UNITY_WEBGL && !UNITY_EDITOR
            LLWebGameStarted();
#endif
            PresentationChanged();
            if (HasArgument("--presentation-capture")) StartCoroutine(CapturePresentation());
            if (HasArgument("--presentation-check")) StartCoroutine(CheckPresentationFlow());
            Debug.Log($"[LLUNITY] Standalone ready; XR={xr}; sprites={library.graphics.Length}; irqHz={clock.InterruptHz:F6}; displayHz={clock.DisplayHz}; DOS runtime dependencies=0");
        }

        private void CreateHud(Bounds arenaBounds, Material spriteMaterial)
        {
            var go = new GameObject("Score and status"); hud = go.AddComponent<PixelText>();
            hud.Initialize(spriteMaterial);
            hud.text = "LLAMATRON\n2112";
            go.transform.position = new Vector3(arenaBounds.center.x, arenaBounds.max.y + 0.55f, arenaBounds.min.z);
            go.transform.rotation = Quaternion.Euler(0, 180, 0);
        }

        private IEnumerator CapturePresentation()
        {
            presentation.Preview(ArgumentValue("--presentation-page") ?? "Title");
            yield return new WaitForSecondsRealtime(2);
            yield return null;
            yield return new WaitForEndOfFrame();
            Capture(ArgumentValue("--presentation-capture"));
            yield return null;
            yield return new WaitForSecondsRealtime(1);
            Debug.Log($"[LLPRESENT] Captured {presentation.Page}; font=original; high-scores=10.");
            Application.Quit(0);
        }

        private IEnumerator CheckPresentationFlow()
        {
            IEnumerator checks = PresentationFlowSteps();
            while (true)
            {
                bool more = false; Exception failure = null;
                try { more = checks.MoveNext(); }
                catch (Exception e) { failure = e; }
                if (failure != null) { Debug.LogError($"[LLPRESENT] Flow check failed: {failure}"); Application.Quit(1); yield break; }
                if (!more) break;
                yield return checks.Current;
            }
            Debug.Log("[LLPRESENT] Flow checks passed: title, modes, menu freeze/resume, paused saves, test-wave entry, story/demo/credits rotation.");
            Application.Quit(0);
        }

        private IEnumerator PresentationFlowSteps()
        {
            presentation.Menu();
            presentation.Activate(2); presentation.Activate(3); presentation.Activate(4); presentation.Activate(0);
            if (!presentation.ActiveGameplay || game.Actors.Spawning.PlayerFlags != 5 || !session.TwinStick)
                throw new InvalidOperationException("Mode selection did not start alternating droid with twin-stick aim.");
            yield return new WaitForSecondsRealtime(0.1f);
            presentation.Menu(); long before = clock.InterruptCount;
            yield return new WaitForSecondsRealtime(0.1f);
            if (clock.InterruptCount != before) throw new InvalidOperationException("Menu advanced the suspended game.");
            presentation.Activate(0); TogglePause();
            if (!GameSession.Load(CaptureSave()).Campaign.Paused) throw new InvalidOperationException("Pause was not captured immediately.");
            presentation.Menu(); presentation.Menu();
            if (!presentation.ActiveGameplay || !paused) throw new InvalidOperationException("Menu toggle did not resume the paused game.");
            TogglePause(); StartTestWave("69");
            if (!presentation.ActiveGameplay || campaign.WaveNumber != 69) throw new InvalidOperationException("Test wave did not leave the menu.");
            byte[] state = CaptureSave(); RestoreSave(state);
            if (campaign.WaveNumber != 69 || !presentation.ActiveGameplay) throw new InvalidOperationException("Loading did not restore gameplay.");
            presentation.Preview("Demo");
            if (!presentation.IsDemo || game.Actors.ScriptedMode != 3) throw new InvalidOperationException("Attract mode is not using the original demo AI.");
            presentation.Finished(game, playMode);
            if (presentation.Page != PresentationPage.Story) throw new InvalidOperationException("Story attract entry missing.");
            presentation.NextAttract(); presentation.Finished(game, playMode);
            if (presentation.Page != PresentationPage.Credits) throw new InvalidOperationException("Credits attract entry missing.");
            presentation.NextAttract();
            if (!presentation.IsDemo) throw new InvalidOperationException("Second demo attract entry missing.");
            presentation.Menu(); presentation.Activate(0);
            game.Actors.Scores.Award(0, 3, 2, 0); // Fixture: 400,000, ahead of the initial top score.
            presentation.Finished(game, playMode);
            if (presentation.Page != PresentationPage.GameOver) throw new InvalidOperationException("Game-over presentation missing.");
            presentation.EndGameOver();
            if (presentation.Page != PresentationPage.NameEntry) throw new InvalidOperationException("Qualifying score did not request a name.");
            presentation.SubmitScore();
            if (presentation.Page != PresentationPage.Scores) throw new InvalidOperationException("Name entry did not return to high scores.");
            yield return new WaitForSecondsRealtime(12.5f);
            if (presentation.Page != PresentationPage.Title) throw new InvalidOperationException("Post-game high scores did not return to title.");
            Debug.Log("[LLPRESENT] Timed post-game scores -> title passed.");
            yield return new WaitForSecondsRealtime(20.5f);
            if (presentation.Page != PresentationPage.Story) throw new InvalidOperationException("Idle title did not start the attract cycle.");
            Debug.Log("[LLPRESENT] Timed idle title -> story passed.");
            presentation.ToggleScrollPause(); float scrollBefore = presentation.ScrollOffset;
            yield return new WaitForSecondsRealtime(0.2f);
            if (presentation.ScrollOffset != scrollBefore) throw new InvalidOperationException("Paused scroll page kept moving.");
            presentation.ToggleScrollPause();
            yield return new WaitForSecondsRealtime(0.2f);
            if (presentation.ScrollOffset <= scrollBefore) throw new InvalidOperationException("Scroll page did not resume.");
            Debug.Log("[LLPRESENT] Scroll pause/resume passed.");
        }

        public bool BeginGame(PlayMode mode, bool splitAim, bool demonstration)
        {
            playMode = demonstration ? PlayMode.Solo : mode;
            twinStick = !demonstration && splitAim;
            return Restart(demonstration);
        }

        public byte PresentationInput
        {
            get
            {
                byte input = ReadInput(null, InputDevices.GetDeviceAtXRNode(XRNode.LeftHand), InputDevices.GetDeviceAtXRNode(XRNode.RightHand), false);
                if (Button(InputDevices.GetDeviceAtXRNode(XRNode.LeftHand), CommonUsages.secondaryButton) ||
                    Button(InputDevices.GetDeviceAtXRNode(XRNode.RightHand), CommonUsages.secondaryButton)) input |= 64;
#if UNITY_WEBGL && !UNITY_EDITOR
                if (browser != null) input |= browser.Primary(false);
                if (browser != null && (browser.SavePressed || browser.LoadPressed)) input |= 64;
#endif
                return input;
            }
        }
        public bool PresentationBack
        {
            get
            {
                bool pressed = Button(InputDevices.GetDeviceAtXRNode(XRNode.LeftHand), CommonUsages.primaryButton) || Button(InputDevices.GetDeviceAtXRNode(XRNode.RightHand), CommonUsages.primaryButton);
#if UNITY_WEBGL && !UNITY_EDITOR
                if (browser != null) pressed |= browser.RestartPressed;
#endif
                return pressed;
            }
        }

        [UnityEngine.Scripting.Preserve]
        public void ShowMenu() { if (ready) presentation.Menu(); }

        public void StopPresentationAudio()
        {
            sampledAudio.Driver.Queue(0, 0, 0);
            sampledAudio.Driver.StopAll(); sampledAudio.Driver.Service();
        }

        public void PlayGameOverSound()
        {
            sampledAudio.Driver.Queue(30, 1, 8); sampledAudio.Driver.Service();
        }

        public bool PresentationSoundPlaying
        {
            get { sampledAudio.PollCompletion(); return sampledAudio.Driver.ActiveSample != 0; }
        }

        public void PresentationChanged()
        {
            if (presentation == null) return;
            sampledAudio.SetSystemPaused(systemPaused || paused || (!presentation.ActiveGameplay && presentation.Page != PresentationPage.GameOver));
            hud.gameObject.SetActive(presentation.ActiveGameplay);
            if (presentation.ActiveGameplay) { skipNextDelta = true; UpdateHud(); }
            else arena.gameObject.SetActive(false);
#if UNITY_WEBGL && !UNITY_EDITOR
            if (ready) LLWebPresentationState(presentation.Page == PresentationPage.NameEntry || presentation.Page == PresentationPage.GameOver ? 4 :
                presentation.IsDemo ? 2 : presentation.ActiveGameplay ? 1 : presentation.HasPlayerGame ? 3 : 0);
#endif
        }

        private bool Restart(bool demonstration = false)
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
            if (demonstration) { waveNumber = random.Next(32) + 1; if (waveNumber > 27) waveNumber = 1; }
            sampledAudio.Driver.StopAll();
            waveScripts = new WaveScriptStore();
            game = new GameSimulation(RandomSequence.Shuffle(() => (ushort)random.Next(65536)), waveIndex: waveNumber - 1, playerFlags: (byte)playMode,
                                      scripts: waveScripts, sound: sampledAudio.Driver, demoWaves: demonstration ? (short)3 : (short)0);
            game.Palette.Effects = paletteEffects;
            arena.ApplyPalette(game.Palette);
            campaign = new GameCampaign(game);
            clock = new GameClock(displayHz);
            session = new GameSession(game, campaign, clock, sampledAudio.Driver) { TwinStick = twinStick };
            ConnectPresentation();
            fault = null; paused = false; ticks = 0; captured = false; smokeFrameReady = false;
            campaign.TickDisplay(); UpdateHud();
            return true;
        }

        private void ConnectPresentation()
        {
            campaign.FrameReady += simulation => arena.Present(session.Frame);
            campaign.WipeReady += arena.PresentWipe;
        }

        private byte[] CaptureSave()
        {
            if (session == null) throw new InvalidOperationException("[LLSAVE] Game is not ready.");
            session.CaptureAudioPosition(sampledAudio.SamplePosition);
            session.TwinStick = twinStick; session.Fault = fault ?? "";
            return session.Save();
        }

        private void RestoreSave(byte[] bytes)
        {
            GameSession restored = GameSession.Load(bytes);
            sampledAudio.Restore(restored.Sound, (int)restored.AudioFramePosition);
            session = restored; game = restored.Simulation; campaign = restored.Campaign; clock = restored.Clock;
            waveScripts = game.Scripts; twinStick = restored.TwinStick;
            playMode = (PlayMode)(game.Actors.Spawning.PlayerFlags & 7);
            displayHz = (float)clock.DisplayHz; paused = campaign.Paused;
            fault = string.IsNullOrEmpty(restored.Fault) ? null : restored.Fault;
            ticks = (int)Math.Min(int.MaxValue, clock.InterruptCount);
            captured = false; smokeFrameReady = false; skipNextDelta = true;
            ConnectPresentation(); arena.ApplyPalette(game.Palette); arena.Present(session.Frame);
            foreach (WipeStep step in session.Wipes) arena.PresentWipe(step);
            UpdateHud();
            ReportSave($"Loaded wave {campaign.WaveNumber}, tick {clock.InterruptCount}.");
            if (presentation != null) presentation.Loaded();
        }

        private void ReportSave(string message)
        {
            saveNotice = message; saveNoticeUntil = Time.unscaledTime + 4;
            Debug.Log($"[LLSAVE] {message}");
#if UNITY_WEBGL && !UNITY_EDITOR
            LLWebSaveNotice(message);
#endif
        }

        [UnityEngine.Scripting.Preserve]
        public void SaveGame()
        {
            try
            {
                byte[] bytes = CaptureSave();
#if UNITY_WEBGL && !UNITY_EDITOR
                PlayerPrefs.SetString(SaveKey, Convert.ToBase64String(bytes)); PlayerPrefs.Save();
#else
                string path = Path.Combine(Application.persistentDataPath, "quicksave.llsave");
                string temporary = $"{path}.tmp";
                File.WriteAllBytes(temporary, bytes);
                if (File.Exists(path)) File.Replace(temporary, path, null); else File.Move(temporary, path);
#endif
                ReportSave($"Saved wave {campaign.WaveNumber}, tick {clock.InterruptCount}.");
            }
            catch (Exception exception) { ReportSave($"Save failed: {exception.Message}"); }
        }

        [UnityEngine.Scripting.Preserve]
        public void LoadGame()
        {
            try
            {
#if UNITY_WEBGL && !UNITY_EDITOR
                if (!PlayerPrefs.HasKey(SaveKey)) throw new InvalidOperationException("No quick save yet.");
                RestoreSave(Convert.FromBase64String(PlayerPrefs.GetString(SaveKey)));
#else
                RestoreSave(File.ReadAllBytes(Path.Combine(Application.persistentDataPath, "quicksave.llsave")));
#endif
            }
            catch (Exception exception) { ReportSave($"Load failed: {exception.Message}"); }
        }

        [UnityEngine.Scripting.Preserve]
        public void ExportSaveGame()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            try { LLWebDownloadSave(Convert.ToBase64String(CaptureSave())); }
            catch (Exception exception) { ReportSave($"Export failed: {exception.Message}"); }
#else
            SaveGame();
#endif
        }

        [UnityEngine.Scripting.Preserve]
        public void ImportSaveGame(string data)
        {
            try
            {
                RestoreSave(Convert.FromBase64String(data));
#if UNITY_WEBGL && !UNITY_EDITOR
                LLWebImportResult(1);
#endif
            }
            catch (Exception exception)
            {
                ReportSave($"Import failed: {exception.Message}");
#if UNITY_WEBGL && !UNITY_EDITOR
                LLWebImportResult(0);
#endif
            }
        }

        [UnityEngine.Scripting.Preserve]
        public void PrepareUpdate()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            try { LLWebUpdateState(Convert.ToBase64String(CaptureSave())); }
            catch (Exception exception) { ReportSave($"Update preparation failed: {exception.Message}"); }
#endif
        }

        [UnityEngine.Scripting.Preserve]
        public void StartTestWave(string value)
        {
            try
            {
                if (!int.TryParse(value, out int wave) || wave < 1 || wave > 100)
                    throw new ArgumentException("Choose a wave from 1 to 100.");
                var test = GameSession.Create(wave, seed: 17, playerFlags: (byte)playMode, displayHz: displayHz);
                test.TwinStick = twinStick;
                RestoreSave(test.Save());
                if (presentation != null) presentation.Loaded(false);
                ReportSave($"Started test wave {wave} with seed 17.");
            }
            catch (Exception exception) { ReportSave($"Test wave failed: {exception.Message}"); }
        }

        [UnityEngine.Scripting.Preserve]
        public void TogglePause()
        {
            if (!ready || (presentation != null && !presentation.ActiveGameplay)) return;
            paused = !paused;
            campaign.Paused = paused;
            sampledAudio.SetSystemPaused(systemPaused || paused);
            UpdateHud();
        }

        private void Update()
        {
            if (!ready) return;
            Keyboard keyboard = Keyboard.current;
            if (presentation != null && !presentation.ActiveGameplay)
            {
                if (keyboard != null && keyboard.f5Key.wasPressedThisFrame && presentation.HasPlayerGame) SaveGame();
                if (keyboard != null && keyboard.f9Key.wasPressedThisFrame) LoadGame();
                return;
            }
            UnityEngine.XR.InputDevice left = InputDevices.GetDeviceAtXRNode(XRNode.LeftHand);
            UnityEngine.XR.InputDevice right = InputDevices.GetDeviceAtXRNode(XRNode.RightHand);
            bool save = Button(left, CommonUsages.secondaryButton) || (keyboard != null && keyboard.f5Key.isPressed);
            bool load = Button(right, CommonUsages.secondaryButton) || (keyboard != null && keyboard.f9Key.isPressed);
#if UNITY_WEBGL && !UNITY_EDITOR
            save |= browser.SavePressed;
            load |= browser.LoadPressed;
#endif
            if (save && !previousSave) SaveGame();
            if (load && !previousLoad) LoadGame();
            previousSave = save; previousLoad = load;
#if UNITY_WEBGL && !UNITY_EDITOR
            xr = browser.Immersive;
#endif
            if (keyboard != null && keyboard.rKey.wasPressedThisFrame)
            {
                Restart(); if (presentation != null) presentation.Loaded(false);
            }
            if (keyboard != null && keyboard.pKey.wasPressedThisFrame) TogglePause();
            campaign.Paused = paused;
            if (!systemPaused && !skipNextDelta && fault == null)
            {
                byte input = smoke ? (byte)0 : ReadInput(keyboard, left, right, twinStick || ((int)playMode & 3) == 3);
                byte secondary = smoke ? (byte)0 : ReadSecondaryInput(keyboard, right);
#if UNITY_WEBGL && !UNITY_EDITOR
                input |= browser.Primary(twinStick || ((int)playMode & 3) == 3);
                secondary |= browser.Secondary();
#endif
                bool acceptContinue = !smoke && (input & 128) != 0;
                try
                {
                    clock.Advance(Time.unscaledDeltaTime, () =>
                    {
                        sampledAudio.ServiceInterrupt();
                        campaign.TickInterrupt(input, secondary, twinStick, acceptContinue); ticks++;
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
            if (!smoke && presentation != null && (campaign.Phase == CampaignPhase.GameOver || campaign.Phase == CampaignPhase.Menu))
            {
                presentation.Finished(game, playMode); return;
            }
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
            if (sampledAudio != null) sampledAudio.SetSystemPaused(pause || paused || (presentation != null && !presentation.ActiveGameplay && presentation.Page != PresentationPage.GameOver));
        }

        private void UpdateHud()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            if (reportedPause != paused)
            {
                reportedPause = paused;
                LLWebPauseState(paused ? 1 : 0);
            }
#endif
            string status = fault != null ? "This part of the reconstruction is unfinished.\nR: restart - M: menu" :
                paused ? "PAUSED" : campaign.Phase == CampaignPhase.Continue ? $"CONTINUE? {campaign.ContinueCount} · SPACE / TRIGGER" :
                campaign.Phase == CampaignPhase.GameOver ? "GAME OVER · R / A: new game" :
                campaign.Phase == CampaignPhase.Menu ? "R / A: new game" :
                campaign.Phase == CampaignPhase.WarpAudio ? "WARP" :
                campaign.Phase != CampaignPhase.Playing ? "WAVE CLEARED" :
                presentation != null && presentation.IsDemo ? "DEMO - SPACE / TRIGGER: MENU" : "RESCUE THE BEASTIES";
            arena.gameObject.SetActive(campaign.Phase != CampaignPhase.Continue && campaign.Phase != CampaignPhase.GameOver && campaign.Phase != CampaignPhase.Menu);
            string player = ((int)playMode & 4) != 0 ? $"PLAYER {campaign.ActivePlayer + 1} · " : "";
            if (Time.unscaledTime < saveNoticeUntil) status = $"{status}\n{saveNotice}";
            hud.text = $"LLAMATRON VR · WAVE {campaign.WaveNumber}\n{player}SCORE {game.Actors.Scores.Value(campaign.ActivePlayer):D8}   LIVES {game.Actors.Lives}\n{status}";
        }

        private static byte ReadInput(Keyboard keyboard, UnityEngine.XR.InputDevice left, UnityEngine.XR.InputDevice right, bool splitControllers)
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
            if ((!left.TryGetFeatureValue(CommonUsages.primary2DAxis, out axis) || axis.sqrMagnitude < 0.04f) && !splitControllers)
                right.TryGetFeatureValue(CommonUsages.primary2DAxis, out axis);
            if (axis.y > 0.25f) input |= 1; if (axis.y < -0.25f) input |= 2;
            if (axis.x < -0.25f) input |= 4; if (axis.x > 0.25f) input |= 8;
            if (Button(left, CommonUsages.triggerButton) || (!splitControllers && Button(right, CommonUsages.triggerButton))) input |= 128;
            return input;
        }

        private static byte ReadSecondaryInput(Keyboard keyboard, UnityEngine.XR.InputDevice right)
        {
            byte input = 0;
            if (keyboard != null)
            {
                if (keyboard.iKey.isPressed) input |= 1; if (keyboard.kKey.isPressed) input |= 2;
                if (keyboard.jKey.isPressed) input |= 4; if (keyboard.lKey.isPressed) input |= 8;
                if (keyboard.rightCtrlKey.isPressed) input |= 128;
            }
            if (right.TryGetFeatureValue(CommonUsages.primary2DAxis, out Vector2 axis))
            {
                if (axis.y > 0.25f) input |= 1; if (axis.y < -0.25f) input |= 2;
                if (axis.x < -0.25f) input |= 4; if (axis.x > 0.25f) input |= 8;
            }
            if (Button(right, CommonUsages.triggerButton)) input |= 128;
            return input;
        }

        private static bool Button(UnityEngine.XR.InputDevice device, InputFeatureUsage<bool> usage) => device.TryGetFeatureValue(usage, out bool value) && value;
        private static bool HasArgument(string value)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            return false;
#else
            return Array.IndexOf(Environment.GetCommandLineArgs(), value) >= 0;
#endif
        }
        private static string ArgumentValue(string value)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            return null;
#else
            string[] args = Environment.GetCommandLineArgs(); int index = Array.IndexOf(args, value);
            return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
#endif
        }

        private void Capture(string path)
        {
#if !UNITY_WEBGL || UNITY_EDITOR
            var target = new RenderTexture(1280, 800, 24); var pixels = new Texture2D(1280, 800, TextureFormat.RGB24, false);
            RenderTexture previous = RenderTexture.active; RenderTexture oldTarget = view.targetTexture;
            view.targetTexture = target; view.Render(); RenderTexture.active = target;
            pixels.ReadPixels(new Rect(0, 0, 1280, 800), 0, 0); pixels.Apply();
            string full = Path.GetFullPath(path); Directory.CreateDirectory(Path.GetDirectoryName(full)); File.WriteAllBytes(full, pixels.EncodeToPNG());
            view.targetTexture = oldTarget; RenderTexture.active = previous;
            target.Release(); Destroy(target); Destroy(pixels);
            Debug.Log($"[LLUNITY] Captured {full}");
#endif
        }

        private void OnDestroy()
        {
            headPosition?.Dispose(); headRotation?.Dispose(); headTracking?.Dispose();
#if !UNITY_WEBGL || UNITY_EDITOR
            if (xr && XRGeneralSettings.Instance != null)
            {
                XRGeneralSettings.Instance.Manager.StopSubsystems(); XRGeneralSettings.Instance.Manager.DeinitializeLoader();
            }
#endif
        }
    }
}
