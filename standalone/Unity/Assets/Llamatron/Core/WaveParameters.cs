using System;
using System.Collections.Generic;

namespace Llamatron.Core
{
    // Parameters retain their original DS identifiers until all consumers are
    // understood. This is a small level-configuration map, not emulated memory.
    // Unwritten parameters carry over into the next wave in the original game.
    public sealed partial class WaveParameters
    {
        private readonly Dictionary<ushort, short> words = new Dictionary<ushort, short>
        {
            [0x21ac] = 10, [0x21ae] = 10, [0x21b2] = 15, [0x0812] = 1,
            [0x0818] = OriginalRecoilData.InitialHealth, [0x0826] = OriginalRecoilData.InitialIncrement,
            [0x0820] = OriginalOrbitData.InitialSpacing,
            [0x0828] = OriginalEdgeData.InitialAccelerationLow, [0x082a] = OriginalEdgeData.InitialAccelerationHigh,
            [0x082c] = OriginalEdgeData.InitialCadence, [0x082e] = OriginalEdgeData.InitialHealth
        };

        public short this[ushort sourceOffset] => words.TryGetValue(sourceOffset, out short value)
            ? value : throw new KeyNotFoundException($"Uninitialized level parameter DS:{sourceOffset:x4}.");

        public short ChaseMaxX => this[0x21ac];
        public short ChaseMaxY => this[0x21ae];
        public short ChaseReload { get => this[0x21b2]; internal set => words[0x21b2] = value; }
        public short Mode => this[0x21b0];
        internal void SetChaseLimits(short value) { words[0x21ac] = value; words[0x21ae] = value; }

        // CS:5317 swaps only DS:21a6..226d. Parameters elsewhere remain shared.
        internal Dictionary<ushort, short> CaptureCampaign()
        {
            var snapshot = new Dictionary<ushort, short>();
            foreach (var item in words)
                if (item.Key >= 0x21a6 && item.Key < 0x226e) snapshot.Add(item.Key, item.Value);
            return snapshot;
        }

        internal void RestoreCampaign(Dictionary<ushort, short> snapshot)
        {
            var remove = new List<ushort>();
            foreach (ushort key in words.Keys) if (key >= 0x21a6 && key < 0x226e) remove.Add(key);
            foreach (ushort key in remove) words.Remove(key);
            foreach (var item in snapshot) words.Add(item.Key, item.Value);
        }

        // CS:0229-0243 and its eight handlers. Supply the completed wave-cycle
        // count (DS:233c), not the wave number. Sound cue delivery is explicit.
        public void Apply(WaveDefinition wave, int cycle, Action<byte> soundCue)
        {
            if (soundCue == null) throw new ArgumentNullException(nameof(soundCue));
            short Word(int value) => unchecked((short)value);
            int Signed(byte value) => unchecked((sbyte)value);
            short Doubled(byte value) => Word(value | value << 8);

            foreach (WaveCommand command in wave.Commands)
            {
                IReadOnlyList<byte> a = command.Arguments;
                switch (command.Code)
                {
                    case 1: // CS:0421: sign extend, then shift by CL.
                        words[0x21ac] = Word(Signed(a[0]) << (cycle & 31));
                        words[0x21ae] = Word(Signed(a[1]) << (cycle & 31));
                        break;
                    case 2: // CS:0438
                        words[0x21b6] = Word(Signed(a[0]));
                        words[0x21b8] = Word(Signed(a[1]));
                        words[0x21ba] = Word(Signed(a[2]));
                        words[0x21bc] = Word(Signed(a[3]));
                        break;
                    case 3: // CS:03f4; the fourth argument is consumed but unused.
                        words[0x21c0] = Doubled(a[0]);
                        words[0x21be] = Word(a[1] & 15);
                        words[0x21c2] = Doubled(a[2]);
                        soundCue(0x16);
                        break;
                    case 4: // CS:03e1
                        words[0x0826] = a[0]; words[0x0818] = a[1];
                        break;
                    case 5: // CS:03cf
                        words[0x0816] = a[0]; words[0x086e] = Doubled(a[1]);
                        break;
                    case 6: // CS:03bc
                        words[0x0830] = a[0]; words[0x0812] = a[1];
                        break;
                    case 7: // CS:039c; three arguments, not four.
                        words[0x0828] = Word(a[0] << 8); words[0x082a] = 0;
                        words[0x082c] = Doubled(a[1]); words[0x082e] = a[2];
                        break;
                    case 8: // CS:0392
                        words[0x0820] = a[0];
                        break;
                    default: throw new NotSupportedException($"Unported wave command {command.Code}.");
                }
            }
            words[0x21b0] = wave.Mode; // CS:0455
        }
    }
}
