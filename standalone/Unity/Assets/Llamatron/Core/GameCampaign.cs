using System;

namespace Llamatron.Core
{
    public enum CampaignPhase { Playing, WaveClear, WipeExpand, WipeContract, WarpAudio, Continue, GameOver, Menu }

    // Normal human campaign paths from CS:0625-0815. FrameReady is deliberately
    // raised BEFORE resolving outcomes: CS:0616 renders the final gameplay frame
    // before the main loop decrements lives or rebuilds the survivor stream.
    public sealed class GameCampaign
    {
        public GameSimulation Simulation { get; }
        public CampaignPhase Phase { get; private set; }
        public int TransitionTicks { get; private set; }
        public int WaveNumber { get; private set; }
        public short ContinueCredits { get; private set; } = 4; // 061f
        public short ContinueCount { get; private set; } // 0621
        public short ContinueTimer { get; private set; } // 0623
        public bool Paused { get; set; }
        public event Action<GameSimulation>? FrameReady;
        private bool sceneDrawn;

        public GameCampaign(GameSimulation simulation)
        {
            Simulation = simulation ?? throw new ArgumentNullException(nameof(simulation));
            WaveNumber = simulation.Actors.Spawning.WaveIndex + 1;
        }

        private static short Word(int value) => unchecked((short)value);

        public void TickInterrupt(byte primary, byte secondary = 0, bool twinStick = false, bool acceptContinue = false)
        {
            Simulation.PollInputTick();
            if (Paused) return;
            if (Phase == CampaignPhase.Playing || Phase == CampaignPhase.WaveClear)
            {
                // CS:0429 gates normal updates until CS:1a3b has drawn the new
                // scene once. It is reset by wave setup, not by every IRQ.
                if (sceneDrawn) Simulation.UpdateInterrupt(primary, secondary, twinStick, Phase == CampaignPhase.WaveClear);
            }
            else if (Phase == CampaignPhase.Continue && ContinueTimer >= 0) ContinueInterrupt(acceptContinue);
        }

        // Original main-loop outcome checks follow drawing, not every IRQ.
        // Wipe steps wait for retrace and never call the gameplay renderer.
        public void TickDisplay()
        {
            switch (Phase)
            {
                case CampaignPhase.Playing:
                    FrameReady?.Invoke(Simulation);
                    sceneDrawn = true;
                    ResolveOutcome();
                    return;
                case CampaignPhase.WaveClear:
                    FrameReady?.Invoke(Simulation);
                    sceneDrawn = true;
                    if (--TransitionTicks == 0) BeginWipe();
                    return;
                case CampaignPhase.WipeExpand:
                    if (--TransitionTicks == 0) { Phase = CampaignPhase.WipeContract; TransitionTicks = 47; }
                    return;
                case CampaignPhase.WipeContract:
                    if (--TransitionTicks == 0)
                    {
                        Simulation.AdvanceWave();
                        WaveNumber = Simulation.Actors.Spawning.WaveIndex + 1;
                        Phase = CampaignPhase.Playing;
                        Paused = false;
                        sceneDrawn = false;
                    }
                    return;
                case CampaignPhase.Continue:
                    ResolveContinue();
                    return;
                case CampaignPhase.WarpAudio:
                    if (TransitionTicks < 2) TransitionTicks++;
                    if (TransitionTicks == 2 && Simulation.Actors.ActiveSoundSample != null &&
                        Simulation.Actors.ActiveSoundSample() == 0) NotifyWarpAudioCompleted();
                    return;
                case CampaignPhase.GameOver:
                case CampaignPhase.Menu:
                    return;
                default: throw new InvalidOperationException("Unknown campaign phase.");
            }
        }

        private void ResolveOutcome()
        {
            ActorContext actors = Simulation.Actors;
            if (actors.Outcome == 0) return;
            if ((actors.Spawning.PlayerFlags & 4) != 0 || actors.ScriptedMode != 0)
                throw new NotSupportedException("Alternating-player snapshots and demo campaign flow are not ported yet.");
            actors.RequestPaletteSequence(0);
            switch (actors.Outcome)
            {
                case 1: // CS:075c. Zero, rather than negative, ends the lives test.
                    actors.Lives = Word(actors.Lives - 1);
                    if (actors.Lives != 0) { Retry(); return; }
                    ContinueCredits = Word(ContinueCredits - 1);
                    if (ContinueCredits == 0) Phase = CampaignPhase.GameOver;
                    else
                    {
                        Phase = CampaignPhase.Continue;
                        ContinueCount = 9; ContinueTimer = 50;
                    }
                    return;
                case 2:
                    Phase = CampaignPhase.WaveClear; TransitionTicks = 11;
                    return;
                case 3:
                    Phase = CampaignPhase.Menu;
                    return;
                case 4:
                    actors.Sound(6, 0, 10);
                    Phase = CampaignPhase.WarpAudio; TransitionTicks = 0;
                    return;
                default: throw new NotSupportedException($"Unported campaign outcome {actors.Outcome}.");
            }
        }

        // CS:063a-067a polls the audio driver until the warp sample finishes.
        // A presentation/audio host must signal that completion; no invented
        // fixed delay substitutes for the driver's playback status.
        public void NotifyWarpAudioCompleted()
        {
            if (Phase != CampaignPhase.WarpAudio) throw new InvalidOperationException("No warp sample is pending.");
            ActorContext actors = Simulation.Actors;
            actors.Spawning.WaveIndex = Word(actors.Spawning.WaveIndex + 5);
            actors.AwardScore(49, 4);
            BeginWipe();
        }

        private void BeginWipe()
        {
            // CS:06e8 draws X=94,92,...,0 (48 steps). CS:072d draws
            // X=94,92,...,2 (47 steps), with no gameplay update in either loop.
            Phase = CampaignPhase.WipeExpand; TransitionTicks = 48;
        }

        // CS:0816. Timer rollover has priority over the fire input. A count of
        // zero remains visible; the signed underflow on the next rollover exits.
        private void ContinueInterrupt(bool accept)
        {
            ContinueTimer = Word(ContinueTimer - 1);
            if (ContinueTimer == 0)
            {
                ContinueTimer = 50;
                ContinueCount = Word(ContinueCount - 1);
            }
            else if (accept) ContinueTimer = -1;
        }

        private void ResolveContinue()
        {
            if (ContinueCount < 0) { Phase = CampaignPhase.GameOver; return; }
            if (ContinueTimer >= 0) return;
            Simulation.Actors.Lives = 5;
            Retry();
            Phase = CampaignPhase.Playing;
        }

        private void Retry()
        {
            Simulation.RetryWave();
            sceneDrawn = false;
            Paused = false;
        }
    }
}
