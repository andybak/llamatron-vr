using System;

namespace Llamatron.Core
{
    // Shared actor-update globals. Field comments retain their original DS
    // locations; the full game loop still owns timing and phase transitions.
    public sealed class ActorContext
    {
        public SpawnContext Spawning { get; }
        public short PrimaryStatus = 100; // 07f3
        public short SecondaryStatus = 100; // 0634
        public short Outcome; // 0810
        public short HitLockTicks; // 0907
        public short ScriptedMode; // 06c4
        public short TransitionLock; // 0615
        public short SpecialDropCountdown = 2; // 21c8
        public short DeathFlashTicks; // 0872
        public short FreezeTicks = 50; // 08fc
        public short StartupTicks = 50; // 068a
        public short AlternateAudioFlag; // 0874
        public short BurstFlag; // 0876, written by projectile-burst damage paths.
        public short ChaserVisits; // 091d
        public short FrameCounter; // 39e6
        public short CycleShift; // 233c
        public short LoveTimer = 1; // 21c4 initial load-image value.
        public short RemainingRescues; // 08f8; set from herd count at stream termination.
        public short LastRescuePending; // 091f
        public short PrimaryRescueLabelY; // 0921
        public short SecondaryRescueLabelY; // 0923
        public short PrimaryTrailDelay; // 06ba
        public short SecondaryTrailDelay; // 06bc
        public short HotBulletTimer = -1; // 233e
        public short Announcement; // 21c6
        public short ScoreRepeat; // 21b4
        public short Lives = 5; // 21a6
        public short Ricochet; // 0650
        public short PermanentHotBullets; // 0652
        public short KillCount; // 063a
        public short SpawnRampCountdown; // 2336
        public short SpawnRampReload; // 2338
        public short SpawnRampInitial = 220; // 233a
        public bool SpawnComplete; // 0802
        public ushort PaletteSequenceOffset; // 06c7
        public bool ScoreDisplayDirty; // 05a1
        public DecimalScoreboard Scores { get; } = new DecimalScoreboard();
        public TrailHistory PrimaryTrail { get; }
        public TrailHistory SecondaryTrail { get; }
        public PlayerMotionState PrimaryMotion { get; } = new PlayerMotionState(false);
        public PlayerMotionState SecondaryMotion { get; } = new PlayerMotionState(true);
        public event Action<ushort>? PaletteSequenceRequested;
        // Event arguments are original AL, CL, CH, without guessing mixer roles.
        public event Action<byte, byte, byte>? SoundRequested;
        public Func<byte>? SoundStatus; // Audio driver AH=6, AL; zero for the silent host.
        public Func<byte>? ActiveSoundSample; // AH=6, AH; independent of retained priority.

        public ActorContext(SpawnContext spawning)
        {
            Spawning = spawning;
            PrimaryTrail = new TrailHistory(spawning.PlayerX, spawning.PlayerY);
            SecondaryTrail = new TrailHistory(spawning.SecondPlayerX, spawning.SecondPlayerY);
            Scores.TenThousandCarry += () => { Announcement = 3; Sound(0x19, 1, 6); };
        }

        public (short X, short Y) Target(int poolIndex)
        {
            bool second = (Spawning.PlayerFlags & 2) != 0 &&
                          ((EntityPool.GameplayRecords - 1 - poolIndex) & 1) != 0;
            return second ? (Spawning.SecondPlayerX, Spawning.SecondPlayerY) : (Spawning.PlayerX, Spawning.PlayerY);
        }

        internal void Sound(byte al, byte cl, byte ch) => SoundRequested?.Invoke(al, cl, ch);

        internal void RequestPaletteSequence(ushort offset)
        {
            PaletteSequenceOffset = offset;
            PaletteSequenceRequested?.Invoke(offset);
        }

        internal void AwardScore(short ax, int column)
        {
            if (ScriptedMode != 0) return;
            Scores.Award((Spawning.PlayerFlags & 0x10) != 0 ? 1 : 0, ax, column, ScoreRepeat);
            ScoreDisplayDirty = true;
        }

        public int Contact(Entity entity) => RecoveredRules.PlayerContact(entity,
            Spawning.PlayerX, Spawning.PlayerY, PrimaryStatus,
            Spawning.SecondPlayerX, Spawning.SecondPlayerY, SecondaryStatus, Spawning.PlayerFlags);

        // CS:43f7-4464. For simultaneous contact, AX bit 1 selects the second
        // player; the original does not process both hits in this invocation.
        public void PlayerHit(int contact)
        {
            if (Outcome == 2 || HitLockTicks != 0 || ScriptedMode != 0) return;
            RequestPaletteSequence(0x06c9);
            if (TransitionLock != 0) return;
            FinishPlayerHit(contact);
        }

        // CS:37c2-37fd: the harmful collectible's contact path.
        internal void HarmfulCollectible(int contact)
        {
            if (Outcome == 2 || HitLockTicks != 0 || ScriptedMode != 0) return;
            RequestPaletteSequence(0x06cd);
            if (TransitionLock != 0) return;
            Sound(0x1d, 3, 0x1e);
            DeathFlashTicks = 20;
            FinishPlayerHit(contact);
        }

        private void FinishPlayerHit(int contact)
        {
            SpecialDropCountdown = 2;
            if ((contact & 2) == 0) PrimaryStatus = -2;
            else
            {
                if ((Spawning.PlayerFlags & 2) == 0) return;
                SecondaryStatus = -2;
            }
            if (Spawning.HostileCount < 10)
            {
                DeathFlashTicks = 20;
                SoundRequested?.Invoke(0x1d, 3, 0x1e);
            }
            else SoundRequested?.Invoke(0x11, 0, 0x1e);
        }
    }

    public static class ChaseActor
    {
        // Entire local state-3 routine CS:4ac9-4b48, with shared helpers ported
        // separately. No world tick, automatic firing or wave transition implied.
        public static void Step(ActorContext context, int poolIndex)
        {
            if ((uint)poolIndex >= EntityPool.GameplayRecords)
                throw new ArgumentOutOfRangeException(nameof(poolIndex));
            Entity entity = context.Spawning.Pool.Records[poolIndex];
            if (!entity.Active || entity.Behavior != 3)
                throw new ArgumentException("Expected an active chase-state entity.");
            context.ChaserVisits = unchecked((short)(context.ChaserVisits + 1));
            if (context.FreezeTicks != 0) return;
            int contact = context.Contact(entity);
            if (contact != 0) { context.PlayerHit(contact); return; }
            var target = context.Target(poolIndex);
            RecoveredRules.ChaseMotion(entity, context.Spawning.Parameters, target.X, target.Y);
        }
    }
}
