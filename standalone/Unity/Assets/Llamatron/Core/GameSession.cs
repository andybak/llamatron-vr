using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace Llamatron.Core
{
    // Portable save/replay boundary. Rendering on load reuses saved commands and
    // wipe steps; calling SpriteFrame.Build again would advance original state.
    public sealed class GameSession
    {
        private const int Version = 1, MaximumBytes = 2 * 1024 * 1024;
        private static readonly byte[] Magic = Encoding.ASCII.GetBytes("LLASAVE\0");
        public GameSimulation Simulation { get; }
        public GameCampaign Campaign { get; }
        public GameClock Clock { get; }
        public SoundDriverState Sound { get; }
        public SpriteFrame Frame { get; } = new SpriteFrame();
        public IReadOnlyList<WipeStep> Wipes => wipes;
        public bool TwinStick { get; set; }
        public string Fault { get; set; } = "";
        public double AudioFramePosition { get; private set; }
        private double audioTime;
        private readonly List<WipeStep> wipes = new List<WipeStep>();
        private readonly SpriteFrame scratch = new SpriteFrame();

        public GameSession(GameSimulation simulation, GameCampaign campaign, GameClock clock, SoundDriverState sound)
        {
            if (campaign.Simulation != simulation) throw new ArgumentException("Campaign belongs to another simulation.");
            Simulation = simulation; Campaign = campaign; Clock = clock; Sound = sound;
            campaign.FrameReady += state =>
            {
                scratch.Build(state);
                Frame.Commands.Clear(); Frame.Commands.AddRange(scratch.Commands); wipes.Clear();
            };
            campaign.WipeReady += step => wipes.Add(step);
            sound.Started += (sample, rate) => { AudioFramePosition = 0; audioTime = Clock.Time; };
        }

        public static GameSession Create(int wave = 1, int seed = 1, byte playerFlags = 0, short cycle = 0, double displayHz = 70)
        {
            var random = new Random(seed); var sound = new SoundDriverState();
            var simulation = new GameSimulation(RandomSequence.Shuffle(() => (ushort)random.Next(65536)), wave - 1, playerFlags, cycle, sound: sound);
            var session = new GameSession(simulation, new GameCampaign(simulation), new GameClock(displayHz), sound);
            session.Campaign.TickDisplay(); return session;
        }

        // Deterministic host for fixture generation and replay, including sample
        // completion. Unity supplies its actual AudioSource playback position.
        public void Advance(double seconds, byte primary = 0, byte secondary = 0, bool acceptContinue = false)
        {
            Clock.Advance(seconds, () =>
            {
                PollVirtualAudio(); Sound.Service(); Campaign.TickInterrupt(primary, secondary, TwinStick, acceptContinue);
            }, () => { PollVirtualAudio(); Campaign.TickDisplay(); });
        }
        private void PollVirtualAudio()
        {
            if (Sound.ActiveSample != 0)
            {
                AudioFramePosition += (Clock.Time - audioTime) * SoundDriverState.RateHz(Sound.ActiveRate);
                if (AudioFramePosition >= OriginalSoundData.Frames[Sound.ActiveSample - 1]) Sound.Complete();
            }
            audioTime = Clock.Time;
        }
        public void CaptureAudioPosition(int frame) { AudioFramePosition = frame; audioTime = Clock.Time; }

        public byte[] Save()
        {
            using var stream = new MemoryStream();
            using (var w = new BinaryWriter(stream, Encoding.UTF8, true))
            {
                w.Write(Magic); w.Write(Version); w.Write(Clock.DisplayHz);
                Simulation.WriteState(w); Campaign.WriteState(w); Clock.WriteState(w); Sound.WriteState(w);
                w.Write(TwinStick); w.Write(Fault); w.Write(AudioFramePosition); w.Write(audioTime);
                w.Write(Frame.Commands.Count);
                foreach (SpriteCommand c in Frame.Commands)
                {
                    w.Write(c.IsLine); w.Write(c.X); w.Write(c.Y);
                    if (c.IsLine) { w.Write(c.EndX); w.Write(c.EndY); w.Write(c.ColorIndex); }
                    else { w.Write(c.Offset); w.Write(c.Transformed); w.Write(c.RowSpacing); w.Write(c.Skew); w.Write(c.Flat); }
                }
                w.Write(wipes.Count); foreach (WipeStep step in wipes) step.WriteState(w);
            }
            byte[] payload = stream.ToArray();
            if (payload.Length > MaximumBytes - 32) throw new InvalidOperationException("Save exceeds supported size.");
            using var sha = SHA256.Create(); byte[] checksum = sha.ComputeHash(payload);
            stream.Write(checksum, 0, checksum.Length); return stream.ToArray();
        }

        // Parse into an independent session; a corrupt/incompatible file never
        // modifies the currently running game or dispatches its audio/events.
        public static GameSession Load(byte[] bytes)
        {
            if (bytes == null || bytes.Length < Magic.Length + 4 + 32 || bytes.Length > MaximumBytes)
                throw new InvalidDataException("Invalid save size.");
            using var sha = SHA256.Create(); byte[] checksum = sha.ComputeHash(bytes, 0, bytes.Length - 32);
            for (int i = 0; i < checksum.Length; i++) if (checksum[i] != bytes[bytes.Length - 32 + i]) throw new InvalidDataException("Save checksum mismatch.");
            using var stream = new MemoryStream(bytes, 0, bytes.Length - 32, false);
            using var r = new BinaryReader(stream, Encoding.UTF8);
            byte[] magic = r.Exact(Magic.Length);
            for (int i = 0; i < Magic.Length; i++) if (magic[i] != Magic[i]) throw new InvalidDataException("Not a Llamatron save.");
            if (r.ReadInt32() != Version) throw new InvalidDataException("Unsupported save version.");
            var clock = new GameClock(r.ReadDouble()); var sound = new SoundDriverState();
            var simulation = new GameSimulation(new RandomSequence(new byte[512]), sound: sound);
            simulation.ReadState(r);
            var campaign = new GameCampaign(simulation); campaign.ReadState(r); clock.ReadState(r); sound.ReadState(r);
            var result = new GameSession(simulation, campaign, clock, sound) { TwinStick = r.ReadBoolean(), Fault = r.ReadString() };
            result.AudioFramePosition = r.ReadDouble(); result.audioTime = r.ReadDouble();
            if (double.IsNaN(result.AudioFramePosition) || double.IsInfinity(result.AudioFramePosition) || result.AudioFramePosition < 0 ||
                double.IsNaN(result.audioTime) || double.IsInfinity(result.audioTime) || result.audioTime < 0 || result.audioTime > clock.Time)
                throw new InvalidDataException("Invalid audio cursor.");
            int count = r.Count(1024);
            for (int i = 0; i < count; i++)
            {
                bool line = r.ReadBoolean(); short x = r.ReadInt16(), y = r.ReadInt16();
                result.Frame.Commands.Add(line ? SpriteCommand.Line(x, y, r.ReadInt16(), r.ReadInt16(), r.ReadByte()) :
                    new SpriteCommand(x, y, r.ReadUInt16(), r.ReadBoolean(), r.ReadInt16(), r.ReadInt16(), r.ReadBoolean()));
            }
            count = r.Count(95); for (int i = 0; i < count; i++) result.wipes.Add(new WipeStep(r));
            if (stream.Position != stream.Length) throw new InvalidDataException("Unexpected data after save.");
            return result;
        }
    }

    public readonly partial struct WipeStep
    {
        internal void WriteState(BinaryWriter w) { w.Write(Left); w.Write(Top); w.Write(Right); w.Write(Bottom); w.Write(ColorIndex); }
        internal WipeStep(BinaryReader r) { Left = r.ReadInt32(); Top = r.ReadInt32(); Right = r.ReadInt32(); Bottom = r.ReadInt32(); ColorIndex = r.ReadByte(); }
    }
}
