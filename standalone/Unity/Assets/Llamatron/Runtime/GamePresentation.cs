using System;
using System.Collections.Generic;
using Llamatron.Core;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Llamatron.Unity
{
    public enum PresentationPage { Title, Playing, Demo, Instructions, Story, Credits, Scores, NameEntry, GameOver, Guide }

    public sealed class GamePresentation : MonoBehaviour
    {
        [Serializable] private sealed class ScrollPage { public ScrollDraw[] draws; public int height; }
        [Serializable] private sealed class ScrollDraw { public int x, y, color, sprite; public string text; }
        private const string ScoresKey = "LlamatronHighScores.v1";
        private StandaloneGame owner;
        private PixelPanel panel;
        private HighScoreTable scores;
        private Keyboard textKeyboard;
        private string entry = "";
        private ScrollPage scrollPage;
        private Color32[] textPalette;
        private readonly Queue<(int player, uint score)> pendingScores = new Queue<(int, uint)>();
        private uint pendingScore;
        private int pendingPlayer = 1;
        private int mode, selection, attractStep;
        private bool alternating, twinStick, automatic, hasGame, scoreEligible;
        private bool persistScores;
        private bool preview;
        private float elapsed, scroll;
        private byte previousInput;
        private bool previousBack;
        public PresentationPage Page { get; private set; } = PresentationPage.Title;
        public bool ActiveGameplay => Page == PresentationPage.Playing || Page == PresentationPage.Demo;
        public bool IsDemo => Page == PresentationPage.Demo;
        public bool HasPlayerGame => hasGame;

        public void Initialize(StandaloneGame game, PixelText font, SpriteLibrary library, bool persistentScores = true)
        {
            owner = game;
            persistScores = persistentScores;
            textPalette = library.palette;
            panel = new GameObject("Game presentation").AddComponent<PixelPanel>();
            panel.Initialize(owner.view, library, font.Font);
            byte[] initial = Resources.Load<TextAsset>("Generated/OriginalScores").bytes;
            try { scores = new HighScoreTable(persistScores && PlayerPrefs.HasKey(ScoresKey) ? Convert.FromBase64String(PlayerPrefs.GetString(ScoresKey)) : initial); }
            catch (Exception e) { Debug.LogWarning($"[LLPRESENT] Using initial high scores: {e.Message}"); scores = new HighScoreTable(initial); }
            textKeyboard = Keyboard.current;
            if (textKeyboard != null) textKeyboard.onTextInput += TextInput;
        }
        private void OnDestroy()
        {
            if (textKeyboard != null) textKeyboard.onTextInput -= TextInput;
            if (panel != null) Destroy(panel.gameObject);
        }
        private void TextInput(char c)
        {
            if (Page == PresentationPage.NameEntry && c >= 32 && c < 127 && entry.Length < 8) entry += c;
        }
        public void Loaded(bool eligible = true)
        {
            hasGame = true; scoreEligible = eligible; Show(PresentationPage.Playing);
        }
        public void Menu()
        {
            if (IsDemo) { owner.StopPresentationAudio(); hasGame = false; }
            Show(PresentationPage.Title);
        }
        public void Preview(string page)
        {
            if (!Enum.TryParse(page, true, out PresentationPage target)) throw new ArgumentException("Unknown presentation page.");
            if (target == PresentationPage.Demo) { NewGame(true); return; }
            if (target == PresentationPage.NameEntry) { pendingScore = 400000; entry = "TEST"; }
            Show(target);
            preview = true;
            if (target == PresentationPage.Story || target == PresentationPage.Guide) scroll = 400;
            else if (target == PresentationPage.Credits) scroll = 250;
        }
        private void Show(PresentationPage page, bool auto = false)
        {
            Page = page; elapsed = scroll = 0; automatic = auto; preview = false;
            panel.Visible(!ActiveGameplay);
            if (!ActiveGameplay) panel.Position();
            if (page == PresentationPage.Story || page == PresentationPage.Credits || page == PresentationPage.Guide)
                scrollPage = JsonUtility.FromJson<ScrollPage>(Resources.Load<TextAsset>($"Generated/Original{page}Layout").text);
            owner.PresentationChanged();
        }
        private void NewGame(bool demo = false)
        {
            pendingScores.Clear();
            int flags = (mode == 2 ? 3 : mode) | (alternating ? 4 : 0);
            if (!owner.BeginGame((PlayMode)flags, twinStick, demo)) return;
            hasGame = !demo; scoreEligible = !demo;
            Show(demo ? PresentationPage.Demo : PresentationPage.Playing, demo);
        }
        internal void NextAttract()
        {
            // CS:0269 dispatch: story, three-wave demo, credits, three-wave demo.
            int step = attractStep++ & 3;
            if (step == 0) Show(PresentationPage.Story, true);
            else if (step == 2) Show(PresentationPage.Credits, true);
            else NewGame(true);
        }
        public void Finished(GameSimulation simulation, PlayMode mode)
        {
            if (IsDemo) { owner.StopPresentationAudio(); hasGame = false; NextAttract(); return; }
            hasGame = false;
            if (scoreEligible)
            {
                uint first = simulation.Actors.Scores.Value(0);
                if (scores.Rank(first) >= 0) pendingScores.Enqueue((1, first));
                if (((int)mode & 4) != 0)
                {
                    uint second = simulation.Actors.Scores.Value(1);
                    if (scores.Rank(second) >= 0) pendingScores.Enqueue((2, second));
                }
            }
            owner.StopPresentationAudio(); Show(PresentationPage.GameOver);
            owner.PlayGameOverSound();
        }
        private void NextScore()
        {
            while (pendingScores.Count != 0)
            {
                var pending = pendingScores.Dequeue();
                pendingScore = pending.score; pendingPlayer = pending.player;
                if (scores.Rank(pendingScore) < 0) continue;
                entry = PlayerPrefs.GetString($"LlamatronPlayer{pendingPlayer}Name.v1", "");
                Show(PresentationPage.NameEntry); return;
            }
            Show(PresentationPage.Scores);
        }
        internal void SubmitScore()
        {
            scores.Insert(pendingScore, string.IsNullOrWhiteSpace(entry) ? "ANON" : entry);
            if (persistScores)
            {
                PlayerPrefs.SetString(ScoresKey, Convert.ToBase64String(scores.Save()));
                PlayerPrefs.SetString($"LlamatronPlayer{pendingPlayer}Name.v1", entry); PlayerPrefs.Save();
            }
            NextScore();
        }
        internal void EndGameOver() { owner.StopPresentationAudio(); NextScore(); }
        private void Update()
        {
            if (owner == null) return;
            if (preview) return;
            var keyboard = Keyboard.current;
            byte input = owner.PresentationInput, pressed = (byte)(input & ~previousInput);
            previousInput = input;
            bool back = owner.PresentationBack, backPressed = back && !previousBack; previousBack = back;
            bool fire = (pressed & 128) != 0 || (keyboard != null && (keyboard.spaceKey.wasPressedThisFrame || keyboard.enterKey.wasPressedThisFrame));
            bool escape = backPressed || (keyboard != null && (keyboard.escapeKey.wasPressedThisFrame || keyboard.mKey.wasPressedThisFrame));
            if (ActiveGameplay)
            {
                if (IsDemo && (fire || escape)) Menu();
                else if (backPressed) Menu();
                return;
            }
            elapsed += Time.unscaledDeltaTime;
            if (Page == PresentationPage.GameOver)
            {
                if ((elapsed > 2 && !owner.PresentationSoundPlaying) || fire) EndGameOver();
                return;
            }
            if (Page == PresentationPage.NameEntry)
            {
                if (keyboard != null && keyboard.backspaceKey.wasPressedThisFrame && entry.Length > 0) entry = entry.Substring(0, entry.Length - 1);
                if ((pressed & 128) != 0 || (keyboard != null && keyboard.enterKey.wasPressedThisFrame)) SubmitScore();
                return;
            }
            if (escape) { Menu(); return; }
            if (Page == PresentationPage.Title)
            {
                if ((pressed & 2) != 0 || (keyboard != null && keyboard.downArrowKey.wasPressedThisFrame)) { selection = (selection + 1) % 9; elapsed = 0; }
                if ((pressed & 1) != 0 || (keyboard != null && keyboard.upArrowKey.wasPressedThisFrame)) { selection = (selection + 8) % 9; elapsed = 0; }
                if (fire) { Activate(selection); return; }
                if (!hasGame && elapsed > 20) NextAttract();
            }
            else if (Page == PresentationPage.Story || Page == PresentationPage.Credits || Page == PresentationPage.Guide)
            {
                scroll += Time.unscaledDeltaTime * 18;
                if (Mouse.current != null) scroll = Mathf.Max(0, scroll - Mouse.current.scroll.ReadValue().y * 0.1f);
                if (fire) { Menu(); return; }
                if (automatic && scroll > scrollPage.height + 178) NextAttract();
            }
            else if (fire) Menu();
        }
        internal void Activate(int item)
        {
            elapsed = 0;
            switch (item)
            {
                case 0: if (hasGame) Show(PresentationPage.Playing); else NewGame(); break;
                case 1: NewGame(!hasGame); break;
                case 2: mode = (mode + 1) % 3; break;
                case 3: alternating = !alternating; break;
                case 4: twinStick = !twinStick; break;
                case 5: Show(PresentationPage.Scores); break;
                case 6: Show(PresentationPage.Instructions); break;
                case 7: Show(PresentationPage.Story); break;
                case 8: Show(PresentationPage.Credits); break;
            }
        }
        private void Text(string text, float x, float y, Color color, bool centered = false) => panel.Text(text, x, y, color, centered);
        private bool Button(string label, float x, float y, float width, bool selected = false) => panel.Button(label, x, y, width, selected);
        private void Sprite(int offset, float x, float y, int scale = 1) => panel.Sprite(offset, x, y, scale);
        private void LateUpdate()
        {
            if (owner == null || panel == null || ActiveGameplay) return;
            panel.Begin();
            Text("LLAMATRON: 2112", 200, 14, Color.yellow, true);
            if (Page == PresentationPage.Title)
            {
                Sprite(0x1b12, 0, 86); Sprite(0x2f0a, 303, 86);
                string[] labels = { hasGame ? "RESUME GAME" : "START GAME", hasGame ? "NEW GAME" : "WATCH DEMO", $"MODE: {new[] { "SOLO", "DROID PARTNER", "TWO PLAYERS" }[mode]}",
                    $"ALTERNATING: {(alternating ? "ON" : "OFF")}", $"TWIN STICK: {(twinStick ? "ON" : "OFF")}", "HIGH SCORES", "HOW TO PLAY", "THE STORY", "CREDITS" };
                for (int i = 0; i < labels.Length; i++) if (Button(labels[i], 92, 36 + i * 23, 216, selection == i)) Activate(i);
                Text("ARROWS / STICK + ENTER / TRIGGER", 200, 248, Color.white, true);
                Text("YAK 1992 - PC BY JONATHAN HOWELL", 200, 264, Color.cyan, true);
            }
            else if (Page == PresentationPage.GameOver)
            {
                Sprite(0x1bc, 122, 116, 2);
                Text("THE HERD WILL REMEMBER YOU", 200, 166, Color.cyan, true);
            }
            else if (Page == PresentationPage.Scores)
            {
                Text("HIGH SCORES", 200, 37, Color.magenta, true);
                for (int i = 0; i < HighScoreTable.Count; i++) Text($"{i + 1,2}  {scores.Score(i):D8}  {scores.Name(i)}", 66, 58 + i * 16, i == 0 ? Color.yellow : Color.cyan);
                if (Button("BACK", 130, 245, 140)) Menu();
            }
            else if (Page == PresentationPage.NameEntry)
            {
                Text($"PLAYER {pendingPlayer} - NEW HIGH SCORE\n\n{pendingScore:D8}\n\nENTER YOUR NAME (8 CHARACTERS)", 200, 65, Color.cyan, true);
                Text($"{entry.PadRight(8, '-')}", 200, 142, Color.yellow, true);
                Text("TYPE - BACKSPACE TO EDIT", 200, 174, Color.white, true);
                if (Button("SAVE SCORE / ENTER", 80, 210, 240)) SubmitScore();
            }
            else if (Page == PresentationPage.Instructions)
            {
                Text("RESCUE BEASTIES. SHOOT THE INVADERS.\n\nWASD / ARROWS: MOVE\nSPACE: LOCK FIRING DIRECTION\nTWIN STICK: IJKL TO AIM\nPLAYER 2: IJKL + RIGHT CTRL\nP: PAUSE / RESUME    M: MENU\nF5: QUICK SAVE    F9: QUICK LOAD\n\nBEASTIES GIVE BONUS + HOT BULLETS.\nHEARTS MAKE BEASTIES FOLLOW YOU.\nRESCUE THE LAST ONE FOR A BONUS!", 30, 46, Color.cyan);
                if (Button("FIELD GUIDE", 40, 245, 160)) Show(PresentationPage.Guide);
                if (Button("BACK", 220, 245, 140)) Menu();
            }
            else
            {
                Text(Page.ToString().ToUpperInvariant(), 200, 37, Color.magenta, true);
                foreach (ScrollDraw draw in scrollPage.draws)
                {
                    float y = 235 - scroll + draw.y;
                    if (draw.sprite < 0) panel.Text(draw.text, 40 + draw.x, y, textPalette[draw.color & 15], false, 57, 235);
                    else panel.Sprite(draw.sprite, 40 + draw.x, y, 1, 0x118c, 57, 235);
                }
                if (Button("BACK / SKIP", 110, 245, 180)) { if (automatic) NextAttract(); else Menu(); }
            }
            panel.End();
        }
    }
}
