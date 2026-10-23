using System;

namespace Llamatron.Core
{
    // CS:0335 stores two scores as eight decimal digits, not binary integers.
    public sealed partial class DecimalScoreboard
    {
        private readonly byte[][] scores = { new byte[8], new byte[8] };
        public event Action? TenThousandCarry;

        public uint Value(int player)
        {
            uint result = 0;
            foreach (byte digit in scores[player]) result = result * 10 + digit;
            return result;
        }

        public ReadOnlySpan<byte> Digits(int player) => scores[player];

        // AX and the repeat word DS:21b4 are both inclusive counters. BX is
        // the starting digit column (0=ten millions, 7=units). The original
        // retries after an eight-digit carry, so 99,999,999 + 1 becomes 1.
        public void Award(int player, short ax, int column, short repeat)
        {
            if ((uint)column >= 8) throw new ArgumentOutOfRangeException(nameof(column));
            byte[] digits = scores[player];
            short outer = repeat;
            do
            {
                short inner = ax;
                do
                {
                    int cursor = column;
                    while (++digits[cursor] == 10)
                    {
                        digits[cursor] = 0;
                        cursor--;
                        if (cursor == 3) TenThousandCarry?.Invoke();
                        if (cursor < 0) cursor = column;
                    }
                    inner = unchecked((short)(inner - 1));
                } while (inner >= 0);
                outer = unchecked((short)(outer - 1));
            } while (outer >= 0);
        }
    }
}
