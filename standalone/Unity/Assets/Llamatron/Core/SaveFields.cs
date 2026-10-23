// Explicit version-1 state fields. No reflection or runtime-type deserialization.
using System.IO;
namespace Llamatron.Core
{
    public sealed partial class ActorContext
    {
        internal void WriteFields(BinaryWriter writer)
        {
            writer.Write(PrimaryStatus);
            writer.Write(SecondaryStatus);
            writer.Write(Outcome);
            writer.Write(HitLockTicks);
            writer.Write(ScriptedMode);
            writer.Write(TransitionLock);
            writer.Write(SpecialDropCountdown);
            writer.Write(DeathFlashTicks);
            writer.Write(FreezeTicks);
            writer.Write(StartupTicks);
            writer.Write(AlternateAudioFlag);
            writer.Write(BurstFlag);
            writer.Write(ChaserVisits);
            writer.Write(FrameCounter);
            writer.Write(CycleShift);
            writer.Write(LoveTimer);
            writer.Write(RemainingRescues);
            writer.Write(LastRescuePending);
            writer.Write(PrimaryRescueLabelY);
            writer.Write(SecondaryRescueLabelY);
            writer.Write(PrimaryTrailDelay);
            writer.Write(SecondaryTrailDelay);
            writer.Write(HotBulletTimer);
            writer.Write(Announcement);
            writer.Write(ScoreRepeat);
            writer.Write(Lives);
            writer.Write(Ricochet);
            writer.Write(PermanentHotBullets);
            writer.Write(KillCount);
            writer.Write(SpawnRampCountdown);
            writer.Write(SpawnRampReload);
            writer.Write(SpawnRampInitial);
            writer.Write(SpawnComplete);
            writer.Write(PaletteSequenceOffset);
            writer.Write(ScoreDisplayDirty);
        }
        internal void ReadFields(BinaryReader reader)
        {
            PrimaryStatus = reader.ReadInt16();
            SecondaryStatus = reader.ReadInt16();
            Outcome = reader.ReadInt16();
            HitLockTicks = reader.ReadInt16();
            ScriptedMode = reader.ReadInt16();
            TransitionLock = reader.ReadInt16();
            SpecialDropCountdown = reader.ReadInt16();
            DeathFlashTicks = reader.ReadInt16();
            FreezeTicks = reader.ReadInt16();
            StartupTicks = reader.ReadInt16();
            AlternateAudioFlag = reader.ReadInt16();
            BurstFlag = reader.ReadInt16();
            ChaserVisits = reader.ReadInt16();
            FrameCounter = reader.ReadInt16();
            CycleShift = reader.ReadInt16();
            LoveTimer = reader.ReadInt16();
            RemainingRescues = reader.ReadInt16();
            LastRescuePending = reader.ReadInt16();
            PrimaryRescueLabelY = reader.ReadInt16();
            SecondaryRescueLabelY = reader.ReadInt16();
            PrimaryTrailDelay = reader.ReadInt16();
            SecondaryTrailDelay = reader.ReadInt16();
            HotBulletTimer = reader.ReadInt16();
            Announcement = reader.ReadInt16();
            ScoreRepeat = reader.ReadInt16();
            Lives = reader.ReadInt16();
            Ricochet = reader.ReadInt16();
            PermanentHotBullets = reader.ReadInt16();
            KillCount = reader.ReadInt16();
            SpawnRampCountdown = reader.ReadInt16();
            SpawnRampReload = reader.ReadInt16();
            SpawnRampInitial = reader.ReadInt16();
            SpawnComplete = reader.ReadBoolean();
            PaletteSequenceOffset = reader.ReadUInt16();
            ScoreDisplayDirty = reader.ReadBoolean();
        }
    }
    public sealed partial class SpawnContext
    {
        internal void WriteFields(BinaryWriter writer)
        {
            writer.Write(RampAcceleration);
            writer.Write(RecoilBaseSprite);
            writer.Write(SelectedHerdSprite);
            writer.Write(OrbitPhase);
            writer.Write(OrbitAcceleration);
            writer.Write(PlayerX);
            writer.Write(PlayerY);
            writer.Write(SecondPlayerX);
            writer.Write(SecondPlayerY);
            writer.Write(PlayerFlags);
            writer.Write(WaveIndex);
            writer.Write(DifficultyIncrement);
            writer.Write(SpriteVariant);
            writer.Write(SpawnSerial);
            writer.Write(HerdCount);
            writer.Write(HostileCount);
            writer.Write(InitialHerdCount);
            writer.Write(FreeRecords);
        }
        internal void ReadFields(BinaryReader reader)
        {
            RampAcceleration = reader.ReadUInt32();
            RecoilBaseSprite = reader.ReadInt16();
            SelectedHerdSprite = reader.ReadInt16();
            OrbitPhase = reader.ReadInt16();
            OrbitAcceleration = reader.ReadUInt32();
            PlayerX = reader.ReadInt16();
            PlayerY = reader.ReadInt16();
            SecondPlayerX = reader.ReadInt16();
            SecondPlayerY = reader.ReadInt16();
            PlayerFlags = reader.ReadByte();
            WaveIndex = reader.ReadInt16();
            DifficultyIncrement = reader.ReadInt16();
            SpriteVariant = reader.ReadInt16();
            SpawnSerial = reader.ReadInt16();
            HerdCount = reader.ReadInt16();
            HostileCount = reader.ReadInt16();
            InitialHerdCount = reader.ReadInt16();
            FreeRecords = reader.ReadInt16();
        }
    }
    public sealed partial class PlayerMotionState
    {
        internal void WriteFields(BinaryWriter writer)
        {
            writer.Write(FireX);
            writer.Write(FireY);
            writer.Write(ProjectileSprite);
            writer.Write(AimMask);
            writer.Write(Facing);
            writer.Write(MoveX);
            writer.Write(MoveY);
            writer.Write(TrailDirection);
            writer.Write(CompanionX);
            writer.Write(CompanionY);
        }
        internal void ReadFields(BinaryReader reader)
        {
            FireX = reader.ReadInt16();
            FireY = reader.ReadInt16();
            ProjectileSprite = reader.ReadInt16();
            AimMask = reader.ReadInt16();
            Facing = reader.ReadInt16();
            MoveX = reader.ReadInt16();
            MoveY = reader.ReadInt16();
            TrailDirection = reader.ReadInt16();
            CompanionX = reader.ReadInt16();
            CompanionY = reader.ReadInt16();
        }
    }
    public sealed partial class DroidControl
    {
        internal void WriteFields(BinaryWriter writer)
        {
            writer.Write(AimCountdown);
            writer.Write(TargetRecord);
            writer.Write(RandomTargetX);
            writer.Write(RandomTargetY);
        }
        internal void ReadFields(BinaryReader reader)
        {
            AimCountdown = reader.ReadInt16();
            TargetRecord = reader.ReadInt16();
            RandomTargetX = reader.ReadInt16();
            RandomTargetY = reader.ReadInt16();
        }
    }
    internal sealed partial class CampaignSnapshot
    {
        internal void WriteFields(BinaryWriter writer)
        {
            writer.Write(Lives);
            writer.Write(SpriteVariant);
            writer.Write(WaveIndex);
            writer.Write(ScoreRepeat);
            writer.Write(LoveTimer);
            writer.Write(Announcement);
            writer.Write(SpecialDropCountdown);
            writer.Write(DifficultyIncrement);
        }
        internal void ReadFields(BinaryReader reader)
        {
            Lives = reader.ReadInt16();
            SpriteVariant = reader.ReadInt16();
            WaveIndex = reader.ReadInt16();
            ScoreRepeat = reader.ReadInt16();
            LoveTimer = reader.ReadInt16();
            Announcement = reader.ReadInt16();
            SpecialDropCountdown = reader.ReadInt16();
            DifficultyIncrement = reader.ReadInt16();
        }
    }
}
