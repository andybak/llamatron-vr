using System;

namespace Llamatron.Core
{
    // CS:e4de reads a cyclic shuffled 512-byte table, not a new PRNG sample.
    public sealed class RandomSequence
    {
        private readonly byte[] table;
        public int Index { get; private set; }

        public RandomSequence(ReadOnlySpan<byte> capturedTable, int index = 0)
        {
            if (capturedTable.Length != 512) throw new ArgumentException("Expected 512 random bytes.");
            if ((uint)index >= 512) throw new ArgumentOutOfRangeException(nameof(index));
            table = capturedTable.ToArray(); Index = index;
        }

        public byte Next()
        {
            byte result = table[Index];
            Index = (Index + 1) & 511;
            return result;
        }

        // CS:007b-00a9. Supply 8000 original timer samples for identical startup,
        // or an explicitly chosen source for a new run; do not claim seed parity.
        public static RandomSequence Shuffle(Func<ushort> timerSample)
        {
            if (timerSample == null) throw new ArgumentNullException(nameof(timerSample));
            var bytes = new byte[512];
            for (int i = 0; i < bytes.Length; i++) bytes[i] = unchecked((byte)i);
            for (int i = 0; i < 4000; i++)
            {
                int a = timerSample() & 511, b = timerSample() & 511;
                byte value = bytes[a]; bytes[a] = bytes[b]; bytes[b] = value;
            }
            return new RandomSequence(bytes);
        }
    }
}
