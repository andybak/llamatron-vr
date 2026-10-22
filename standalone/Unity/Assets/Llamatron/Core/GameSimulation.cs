using System;

namespace Llamatron.Core
{
    // Discrete normal-play frame advancement. Unported actor/spawn/damage states
    // still fail explicitly; this is not yet the complete game or timing driver.
    public sealed class GameSimulation
    {
        public ActorContext Actors { get; }
        public BulletSystem Bullets { get; } = new BulletSystem();
        public PaletteState Palette { get; } = new PaletteState();
        public WaveDefinition Wave { get; private set; }
        public short ObstacleRemaining = 7; // 07fe
        public short GroupRemaining = -1; // 08fa
        public int NextGroup { get; private set; }
        public uint SpawnRampAccumulator { get => Actors.Spawning.RampAcceleration; set => Actors.Spawning.RampAcceleration = value; }
        public WaveScriptStore Scripts => Actors.Spawning.Scripts;
        private byte currentKind;

        public GameSimulation(RandomSequence random, int waveIndex = 0, byte playerFlags = 0, short cycle = 0,
                              WaveScriptStore? scripts = null, SoundDriverState? sound = null)
        {
            if ((uint)waveIndex >= 100) throw new ArgumentOutOfRangeException(nameof(waveIndex));
            var parameters = new WaveParameters();
            var spawn = new SpawnContext(new EntityPool(), random, parameters, scripts)
            {
                WaveIndex = (short)waveIndex, PlayerFlags = playerFlags, DifficultyIncrement = cycle,
                PlayerX = (playerFlags & 1) != 0 ? (short)140 : (short)160
            };
            Actors = new ActorContext(spawn) { CycleShift = cycle };
            if (sound != null)
            {
                Actors.SoundRequested += sound.Queue;
                Actors.SoundStatus = () => sound.Priority;
                Actors.ActiveSoundSample = () => sound.ActiveSample;
            }
            // A tooling start on a later wave needs its carried prefix values.
            // Normal campaign advancement reuses this same parameter instance.
            for (int i = 0; i < waveIndex; i++) parameters.Apply(Scripts.Decode(i), cycle, _ => { });
            Wave = Scripts.Decode(waveIndex);
            StartWave(Wave, true);
        }

        private static short Word(int value) => unchecked((short)value);

        // CS:1e5-0243: index was already advanced by the clear/warp path. Only
        // the zero pointer at index 100 wraps the wave table and raises difficulty.
        public void AdvanceWave()
        {
            SpawnContext spawn = Actors.Spawning;
            if (spawn.WaveIndex == 100)
            {
                spawn.WaveIndex = 0;
                Actors.CycleShift = Word(Actors.CycleShift + 1);
                spawn.DifficultyIncrement = Word(spawn.DifficultyIncrement + 1);
            }
            if ((uint)spawn.WaveIndex >= 100) throw new InvalidOperationException("Wave index does not address a recovered wave pointer.");
            Actors.Sound(0x15, 1, 30);
            StartWave(Scripts.Decode(spawn.WaveIndex), true);
        }

        public void RetryWave()
        {
            WaveDefinition retry = WaveDefinition.FromSurvivors(Actors.Spawning.Pool, unchecked((byte)Actors.Spawning.Parameters.Mode));
            StartWave(retry, false);
        }

        // CS:0455-055b, 05ed-0615. Reset only the fields actually written by
        // wave setup. In particular score, lives, random index, hot bullets,
        // firing cooldown, animation/aim and unrelated entity words survive.
        private void StartWave(WaveDefinition wave, bool applyCommands)
        {
            Wave = wave;
            SpawnContext spawn = Actors.Spawning;
            if (applyCommands) spawn.Parameters.Apply(wave, Actors.CycleShift, cue => Actors.Sound(cue, 0, 30));
            spawn.PlayerX = (spawn.PlayerFlags & 1) != 0 ? (short)140 : (short)160;
            spawn.PlayerY = 104;
            if ((spawn.PlayerFlags & 1) != 0) { spawn.SecondPlayerX = 180; spawn.SecondPlayerY = 104; }
            spawn.HerdCount = 0; spawn.HostileCount = 0; spawn.FreeRecords = 69;
            Actors.Outcome = 0; Actors.SpawnComplete = false; Actors.FreezeTicks = 50;
            Bullets.Reset(); spawn.Pool.Reset();
            Actors.PrimaryTrailDelay = 0; Actors.SecondaryTrailDelay = 0;
            Actors.PrimaryTrail.Reset(spawn.PlayerX, spawn.PlayerY);
            Actors.SecondaryTrail.Reset(spawn.SecondPlayerX, spawn.SecondPlayerY);
            Actors.SpawnRampReload = Actors.SpawnRampInitial;
            Actors.SpawnRampCountdown = Actors.SpawnRampReload;
            SpawnRampAccumulator = 0x800; Bullets.Alternate = 0; Actors.Ricochet = 0;
            GroupRemaining = -1; ObstacleRemaining = 7; NextGroup = 0;
            Actors.TransitionLock = 0;
            Actors.PrimaryStatus = 100; Actors.SecondaryStatus = 100;
        }

        // Normal human-play path of CS:1562-1a38, followed by the spawn step.
        // The caller supplies one logic tick; elapsed-time pacing is separate.
        public void Step(byte primary, byte secondary = 0, bool twinStick = false)
        {
            if (Actors.Outcome != 0) return;
            PollInputTick();
            StepFrame(primary, secondary, twinStick);
        }

        // CS:3e08 executes on IRQ 0 even when the selected update callback is a
        // no-op. The campaign owns polling separately from callback selection.
        internal void PollInputTick()
        {
            Actors.FrameCounter = Word(Actors.FrameCounter + 1);
            Palette.TickInterrupt(Actors.FrameCounter);
        }

        internal void UpdateInterrupt(byte primary, byte secondary, bool twinStick, bool allowCleared)
        {
            if (Actors.Outcome == 0 || (allowCleared && Actors.Outcome == 2)) StepFrame(primary, secondary, twinStick);
        }

        private void StepFrame(byte primary, byte secondary, bool twinStick)
        {
            if (Actors.StartupTicks != 0) Actors.StartupTicks = Word(Actors.StartupTicks - 1);
            if (Actors.HitLockTicks != 0)
            {
                Actors.HitLockTicks = Word(Actors.HitLockTicks - 1);
                if (Actors.HitLockTicks == 50) Actors.Sound(0x1a, 1, 10);
            }
            if (Actors.FreezeTicks == 0)
            {
                Actors.LoveTimer = Word(Actors.LoveTimer - 1);
                Actors.SpawnRampCountdown = Word(Actors.SpawnRampCountdown - 1);
                if (Actors.SpawnRampCountdown < 0)
                {
                    Actors.SpawnRampCountdown = Actors.SpawnRampReload > 16
                        ? Word(Actors.SpawnRampReload >> 1) : Actors.SpawnRampReload;
                    SpawnRampAccumulator = unchecked(SpawnRampAccumulator + 0x200);
                }
            }
            if (Actors.PrimaryRescueLabelY >= 0) Actors.PrimaryRescueLabelY = Word(Actors.PrimaryRescueLabelY - 1);
            if (Actors.SecondaryRescueLabelY >= 0) Actors.SecondaryRescueLabelY = Word(Actors.SecondaryRescueLabelY - 1);
            if (Actors.DeathFlashTicks != 0)
            {
                Actors.DeathFlashTicks = Word(Actors.DeathFlashTicks - 1);
                if (Actors.DeathFlashTicks == 0 && Actors.AlternateAudioFlag == 0) Actors.Sound(0x11, 3, 30);
            }
            if (Actors.PrimaryStatus != 0) Actors.PrimaryStatus = Word(Actors.PrimaryStatus - 2);
            if (Actors.SecondaryStatus != 0) Actors.SecondaryStatus = Word(Actors.SecondaryStatus - 2);
            if (Actors.PrimaryStatus == -200 || Actors.SecondaryStatus == -200) { Actors.Outcome = 1; return; }
            if (Actors.PrimaryStatus < 0 || Actors.SecondaryStatus < 0) return;
            if (Actors.SpawnComplete)
            {
                if (Actors.FreezeTicks != 0) Actors.FreezeTicks = Word(Actors.FreezeTicks - 1);
                SpawnContext spawn = Actors.Spawning;
                if (Actors.Outcome != 2 && spawn.HostileCount == 0 && (spawn.Parameters.Mode == 0 || spawn.HerdCount == 0))
                {
                    Actors.Outcome = 2;
                    spawn.WaveIndex = Word(spawn.WaveIndex + 1);
                    spawn.SpriteVariant = Word(spawn.SpriteVariant + 1);
                    if (spawn.SpriteVariant == 27) spawn.SpriteVariant = 0;
                    Actors.Sound(0x14, 0, 30);
                    return;
                }
            }
            PlayerMotion.Step(Actors, primary, secondary, twinStick);
            Bullets.Fire(Actors);
            if (Actors.HotBulletTimer >= 0 && Actors.PermanentHotBullets == 0)
                Actors.HotBulletTimer = Word(Actors.HotBulletTimer - 1);
            Bullets.Step(Actors);
            Actors.ChaserVisits = 0;
            for (int i = 0; i < EntityPool.GameplayRecords; i++) ActorDispatcher.Step(Actors, i);
            Actors.Spawning.Parameters.ChaseReload = Word(Math.Max(3, Actors.ChaserVisits >> 1));
            SpawnStep();
        }

        // CS:1d40, 1d8d, 3d09: one obstacle, group read or instance per tick.
        private void SpawnStep()
        {
            if (ObstacleRemaining >= 0)
            {
                ObstacleRemaining = Word(ObstacleRemaining - 1);
                Actors.Spawning.CreateObstacle();
            }
            else if (GroupRemaining < 0)
            {
                if (NextGroup == Wave.Groups.Count)
                {
                    Actors.SpawnComplete = true;
                    Actors.RemainingRescues = Actors.Spawning.HerdCount;
                }
                else
                {
                    SpawnGroup group = Wave.Groups[NextGroup++];
                    currentKind = group.Kind; GroupRemaining = Word(group.Instances - 1);
                }
            }
            else
            {
                GroupRemaining = Word(GroupRemaining - 1);
                Actors.Spawning.Create(currentKind);
            }
        }
    }
}
