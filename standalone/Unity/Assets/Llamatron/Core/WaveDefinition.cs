using System;
using System.Collections.Generic;

namespace Llamatron.Core
{
    public sealed class WaveCommand
    {
        public byte Code { get; }
        public IReadOnlyList<byte> Arguments { get; }

        public WaveCommand(byte code, byte[] arguments)
        {
            Code = code; Arguments = Array.AsReadOnly(arguments);
        }
    }

    public readonly struct SpawnGroup
    {
        public byte Kind { get; }
        public int Instances { get; }

        public SpawnGroup(byte kind, byte encodedCount)
        {
            Kind = kind;
            // CS:1d8d checks negativity before CS:1db1 decrements and spawns.
            // Thus a count byte of zero produces one entity.
            Instances = encodedCount + 1;
        }
    }

    public sealed class WaveDefinition
    {
        public IReadOnlyList<WaveCommand> Commands { get; }
        public byte Mode { get; }
        public IReadOnlyList<SpawnGroup> Groups { get; }
        private static readonly int[] CommandLengths = { 0, 2, 4, 4, 2, 2, 2, 3, 1 };

        private WaveDefinition(List<WaveCommand> commands, byte mode, List<SpawnGroup> groups)
        {
            Commands = commands.AsReadOnly(); Mode = mode; Groups = groups.AsReadOnly();
        }

        // CS:1ced builds the retry stream from the first 70 records, grouping
        // surviving original spawn kinds in first-encounter order. Dynamically
        // generated children, rescued herd and dying actors carry kind -1.
        public static WaveDefinition FromSurvivors(EntityPool pool, byte mode)
        {
            var kinds = new List<byte>(31);
            var counts = new List<byte>(31);
            for (int i = 0; i < EntityPool.GameplayRecords; i++)
            {
                Entity entity = pool.Records[i];
                // The original tests the signed LOW byte here, unlike the
                // word-sized active test in the normal actor loop.
                if (unchecked((sbyte)entity.Behavior) < 0 || entity[0x3a] < 0) continue;
                byte kind = unchecked((byte)entity[0x3a]);
                int group = kinds.IndexOf(kind);
                if (group >= 0) counts[group] = unchecked((byte)(counts[group] + 1));
                else if (kinds.Count < 31) { kinds.Add(kind); counts.Add(0); }
            }
            var groups = new List<SpawnGroup>(kinds.Count);
            for (int i = 0; i < kinds.Count; i++) groups.Add(new SpawnGroup(kinds[i], counts[i]));
            return new WaveDefinition(new List<WaveCommand>(), mode, groups);
        }

        // CS:0229 interprets eight negative prefix commands, then CS:0455 reads
        // mode. CS:3d09 consumes (spawn kind, count) pairs until count=255.
        public static WaveDefinition Decode(ReadOnlySpan<byte> script)
        {
            var commands = new List<WaveCommand>();
            var groups = new List<SpawnGroup>();
            int cursor = 0;
            while (cursor < script.Length && script[cursor] >= 128)
            {
                int code = 256 - script[cursor++];
                if (code >= CommandLengths.Length) throw new ArgumentException($"Unknown wave command {code}.");
                int size = CommandLengths[code];
                if (script.Length - cursor < size) throw new ArgumentException("Truncated wave command.");
                commands.Add(new WaveCommand((byte)code, script.Slice(cursor, size).ToArray()));
                cursor += size;
            }
            if (cursor >= script.Length) throw new ArgumentException("Missing wave mode.");
            byte mode = script[cursor++];
            while (script.Length - cursor >= 2)
            {
                byte kind = script[cursor++], count = script[cursor++];
                if (count == 255)
                {
                    if (cursor != script.Length) throw new ArgumentException("Data follows wave terminator.");
                    return new WaveDefinition(commands, mode, groups);
                }
                if (kind >= 41) throw new ArgumentException($"Unknown spawn kind {kind}.");
                groups.Add(new SpawnGroup(kind, count));
            }
            throw new ArgumentException("Missing wave terminator.");
        }
    }
}
