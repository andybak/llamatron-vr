using System;
using System.Collections.Generic;
using System.IO;

namespace Llamatron.Core
{
    internal static class SaveIO
    {
        internal static int Count(this BinaryReader reader, int maximum)
        {
            int count = reader.ReadInt32();
            if (count < 0 || count > maximum) throw new InvalidDataException("Invalid save collection length.");
            return count;
        }
        internal static byte[] Exact(this BinaryReader reader, int count)
        {
            byte[] bytes = reader.ReadBytes(count);
            if (bytes.Length != count) throw new EndOfStreamException("Truncated save.");
            return bytes;
        }
        internal static void WriteMap(this BinaryWriter writer, Dictionary<ushort, short> values)
        {
            var keys = new List<ushort>(values.Keys); keys.Sort(); writer.Write(keys.Count);
            foreach (ushort key in keys) { writer.Write(key); writer.Write(values[key]); }
        }
        internal static Dictionary<ushort, short> ReadMap(this BinaryReader reader)
        {
            var result = new Dictionary<ushort, short>(); int count = reader.Count(1024);
            for (int i = 0; i < count; i++) result.Add(reader.ReadUInt16(), reader.ReadInt16());
            return result;
        }
        internal static void WriteWave(this BinaryWriter writer, WaveDefinition wave)
        {
            writer.Write(wave.Commands.Count);
            foreach (WaveCommand command in wave.Commands)
            {
                writer.Write(unchecked((byte)-command.Code));
                foreach (byte value in command.Arguments) writer.Write(value);
            }
            writer.Write(wave.Mode); writer.Write(wave.Groups.Count);
            foreach (SpawnGroup group in wave.Groups) { writer.Write(group.Kind); writer.Write((byte)(group.Instances - 1)); }
        }
        internal static WaveDefinition ReadWave(this BinaryReader reader)
        {
            using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream);
            int count = reader.Count(1024); int[] lengths = { 0, 2, 4, 4, 2, 2, 2, 3, 1 };
            for (int i = 0; i < count; i++)
            {
                byte prefix = reader.ReadByte(); int code = 256 - prefix;
                if (code < 1 || code >= lengths.Length) throw new InvalidDataException("Invalid saved wave command.");
                writer.Write(prefix); writer.Write(reader.Exact(lengths[code]));
            }
            writer.Write(reader.ReadByte()); count = reader.Count(1024);
            writer.Write(reader.Exact(count * 2)); writer.Write((byte)0); writer.Write((byte)255);
            return WaveDefinition.Decode(stream.ToArray());
        }
        internal static void WriteEntity(this BinaryWriter writer, Entity entity)
        { for (int i = 0; i < Entity.ByteSize; i += 2) writer.Write(entity[i]); }
        internal static void ReadEntity(this BinaryReader reader, Entity entity)
        { for (int i = 0; i < Entity.ByteSize; i += 2) entity[i] = reader.ReadInt16(); }
    }

    public sealed partial class RandomSequence
    {
        internal void WriteState(BinaryWriter w) { w.Write(table); w.Write(Index); }
        internal void ReadState(BinaryReader r)
        {
            r.Exact(512).CopyTo(table, 0); Index = r.ReadInt32();
            if ((uint)Index >= 512) throw new InvalidDataException("Invalid random cursor.");
        }
    }
    public sealed partial class WaveScriptStore
    {
        internal void WriteState(BinaryWriter w) { foreach (byte[] script in scripts) w.Write(script); }
        internal void ReadState(BinaryReader r)
        { for (int i = 0; i < scripts.Length; i++) scripts[i] = r.Exact(scripts[i].Length); }
    }
    public sealed partial class WaveParameters
    {
        internal void WriteState(BinaryWriter w) => w.WriteMap(words);
        internal void ReadState(BinaryReader r)
        { words.Clear(); foreach (var pair in r.ReadMap()) words.Add(pair.Key, pair.Value); }
    }
    public sealed partial class TrailHistory
    {
        internal void WriteState(BinaryWriter w)
        { w.Write(ByteCursor); foreach (var p in points) { w.Write(p.X); w.Write(p.Y); w.Write(p.Direction); } }
        internal void ReadState(BinaryReader r)
        {
            ByteCursor = r.ReadInt32();
            if ((ByteCursor & ~0x3f8) != 0) throw new InvalidDataException("Invalid trail cursor.");
            for (int i = 0; i < points.Length; i++) points[i] = new TrailPoint(r.ReadInt16(), r.ReadInt16(), r.ReadInt16());
        }
    }
    public sealed partial class DecimalScoreboard
    {
        internal void WriteState(BinaryWriter w) { foreach (byte[] score in scores) w.Write(score); }
        internal void ReadState(BinaryReader r)
        {
            foreach (byte[] score in scores) for (int i = 0; i < score.Length; i++)
            { score[i] = r.ReadByte(); if (score[i] > 9) throw new InvalidDataException("Invalid score digit."); }
        }
    }
    public sealed partial class PaletteState
    {
        internal void WriteState(BinaryWriter w)
        {
            w.Write((int)Effects); w.Write(CycleEnabled); w.Write(ReloadCountdown); w.Write(Revision);
            foreach (Color color in colors) { w.Write(color.Red); w.Write(color.Green); w.Write(color.Blue); }
        }
        internal void ReadState(BinaryReader r)
        {
            Effects = (PaletteEffects)r.ReadInt32(); CycleEnabled = r.ReadBoolean(); ReloadCountdown = r.ReadByte(); Revision = r.ReadInt32();
            if ((uint)Effects > 2) throw new InvalidDataException("Invalid palette mode.");
            for (int i = 0; i < colors.Length; i++) colors[i] = new Color(r.ReadByte(), r.ReadByte(), r.ReadByte());
        }
    }
    public sealed partial class SoundDriverState
    {
        internal void WriteState(BinaryWriter w)
        { w.Write(ActiveSample); w.Write(Priority); w.Write(ActiveRate); w.Write(PendingSample); w.Write(PendingStop); w.Write(pendingRate); w.Write(pendingPriority); }
        internal void ReadState(BinaryReader r)
        {
            ActiveSample = r.ReadByte(); Priority = r.ReadByte(); ActiveRate = r.ReadByte(); PendingSample = r.ReadByte(); PendingStop = r.ReadByte(); pendingRate = r.ReadByte(); pendingPriority = r.ReadByte();
            if (ActiveSample > 32) throw new InvalidDataException("Invalid active sample.");
        }
    }
    public sealed partial class BulletSystem
    {
        internal void WriteState(BinaryWriter w)
        {
            w.Write(Cooldown); w.Write(Alternate);
            foreach (Bullet b in shots) { w.Write(b.X); w.Write(b.Y); w.Write(b.VelocityX); w.Write(b.VelocityY); w.Write(b.Sprite); w.Write(b.Lifetime); w.Write(b.Word0A); w.Write(b.Word0E); }
        }
        internal void ReadState(BinaryReader r)
        {
            Cooldown = r.ReadSByte(); Alternate = r.ReadInt16();
            foreach (Bullet b in shots) { b.X = r.ReadInt16(); b.Y = r.ReadInt16(); b.VelocityX = r.ReadInt16(); b.VelocityY = r.ReadInt16(); b.Sprite = r.ReadInt16(); b.Lifetime = r.ReadInt16(); b.Word0A = r.ReadInt16(); b.Word0E = r.ReadInt16(); }
        }
    }
    public sealed partial class SpawnContext
    {
        internal void WriteState(BinaryWriter w)
        {
            WriteFields(w); Random.WriteState(w); Parameters.WriteState(w); Scripts.WriteState(w);
            foreach (Entity e in Pool.Records) w.WriteEntity(e); w.WriteEntity(Pool.TargetDummy);
            w.Write(Chain.Cursor); for (int i = 0; i < 64; i++) { var p = Chain[i]; w.Write(p.X); w.Write(p.Y); w.Write(p.Sprite); }
        }
        internal void ReadState(BinaryReader r)
        {
            ReadFields(r); Random.ReadState(r); Parameters.ReadState(r); Scripts.ReadState(r);
            foreach (Entity e in Pool.Records) r.ReadEntity(e); r.ReadEntity(Pool.TargetDummy);
            Chain.Cursor = r.ReadInt16(); for (int i = 0; i < 64; i++) Chain.Record(i, r.ReadInt16(), r.ReadInt16(), r.ReadInt16());
        }
    }
    public sealed partial class DroidControl
    {
        internal void WriteState(BinaryWriter w) { WriteFields(w); w.Write(quadrants); }
        internal void ReadState(BinaryReader r) { ReadFields(r); r.Exact(quadrants.Length).CopyTo(quadrants, 0); }
    }
    public sealed partial class ActorContext
    {
        internal void WriteState(BinaryWriter w)
        {
            WriteFields(w); Spawning.WriteState(w); Scores.WriteState(w); PrimaryTrail.WriteState(w); SecondaryTrail.WriteState(w);
            PrimaryMotion.WriteFields(w); SecondaryMotion.WriteFields(w); Droid.WriteState(w);
        }
        internal void ReadState(BinaryReader r)
        {
            ReadFields(r); Spawning.ReadState(r); Scores.ReadState(r); PrimaryTrail.ReadState(r); SecondaryTrail.ReadState(r);
            PrimaryMotion.ReadFields(r); SecondaryMotion.ReadFields(r); Droid.ReadState(r);
        }
    }
    public sealed partial class GameSimulation
    {
        internal void WriteState(BinaryWriter w)
        { Actors.WriteState(w); Bullets.WriteState(w); Palette.WriteState(w); w.WriteWave(Wave); w.Write(ObstacleRemaining); w.Write(GroupRemaining); w.Write(NextGroup); w.Write(currentKind); }
        internal void ReadState(BinaryReader r)
        {
            Actors.ReadState(r); Bullets.ReadState(r); Palette.ReadState(r); Wave = r.ReadWave(); ObstacleRemaining = r.ReadInt16(); GroupRemaining = r.ReadInt16(); NextGroup = r.ReadInt32(); currentKind = r.ReadByte();
            if ((uint)NextGroup > Wave.Groups.Count) throw new InvalidDataException("Invalid spawn-stream cursor.");
        }
    }
    public sealed partial class GameCampaign
    {
        internal void WriteState(BinaryWriter w)
        {
            w.Write((int)Phase); w.Write(TransitionTicks); w.Write(WaveNumber); w.Write(ContinueCredits); w.Write(ContinueCount); w.Write(ContinueTimer); w.Write(Paused); w.Write(sceneDrawn);
            w.Write(otherPlayer != null);
            if (otherPlayer != null) { otherPlayer.WriteFields(w); w.WriteMap(otherPlayer.Parameters); w.Write(otherPlayer.Survivors != null); if (otherPlayer.Survivors != null) w.WriteWave(otherPlayer.Survivors); }
        }
        internal void ReadState(BinaryReader r)
        {
            Phase = (CampaignPhase)r.ReadInt32(); TransitionTicks = r.ReadInt32(); WaveNumber = r.ReadInt32(); ContinueCredits = r.ReadInt16(); ContinueCount = r.ReadInt16(); ContinueTimer = r.ReadInt16(); Paused = r.ReadBoolean(); sceneDrawn = r.ReadBoolean();
            if ((uint)Phase > (uint)CampaignPhase.Menu) throw new InvalidDataException("Invalid campaign phase.");
            otherPlayer = null;
            if (r.ReadBoolean()) { otherPlayer = new CampaignSnapshot(Simulation.Actors, null); otherPlayer.ReadFields(r); otherPlayer.Parameters = r.ReadMap(); otherPlayer.Survivors = r.ReadBoolean() ? r.ReadWave() : null; }
        }
    }
    public sealed partial class GameClock
    {
        internal void WriteState(BinaryWriter w) { w.Write(Time); w.Write(InterruptCount); w.Write(DisplayCount); }
        internal void ReadState(BinaryReader r)
        {
            Time = r.ReadDouble(); InterruptCount = r.ReadInt64(); DisplayCount = r.ReadInt64();
            if (double.IsNaN(Time) || double.IsInfinity(Time) || Time < 0 || InterruptCount < 0 || DisplayCount < 0) throw new InvalidDataException("Invalid game clock.");
        }
    }
}
