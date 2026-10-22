using System;

namespace Llamatron.Core
{
    public enum PaletteEffects { None, Cycle, Full }

    // VGA path: CS:1be6 (full load), 3e4e (IRQ entry 1), e946 (display entry 0).
    // Colors retain their original six-bit DAC components until presentation.
    public sealed class PaletteState
    {
        public readonly struct Color
        {
            public readonly byte Red, Green, Blue;
            internal Color(int red, int green, int blue) { Red = (byte)red; Green = (byte)green; Blue = (byte)blue; }
        }

        private readonly Color[] colors = new Color[16];
        public Color this[int index] => colors[index];
        public PaletteEffects Effects { get; set; } = PaletteEffects.Full;
        public bool CycleEnabled { get; set; } = true; // DS:0902, initialized to 1.
        public byte ReloadCountdown { get; private set; } // DS:044c, initially zero.
        public int Revision { get; private set; }

        public PaletteState() => Reload();

        // Single-register BIOS writes use DH=red, CH=green, CL=blue. The game
        // places its low nibble in CH and middle nibble in CL. Full palette
        // loads instead write consecutive RGB triples, swapping those two.
        public static Color Decode(ushort word) => new Color(OriginalPaletteData.Ramp[(word >> 8) & 15],
            OriginalPaletteData.Ramp[word & 15], OriginalPaletteData.Ramp[(word >> 4) & 15]);

        public void Reload()
        {
            for (int i = 0; i < colors.Length; i++)
            {
                ushort word = OriginalPaletteData.Default[i];
                colors[i] = new Color(OriginalPaletteData.Ramp[(word >> 8) & 15],
                    OriginalPaletteData.Ramp[(word >> 4) & 15], OriginalPaletteData.Ramp[word & 15]);
            }
            Revision++;
        }

        internal void TickInterrupt(short frameCounter)
        {
            if (Effects != PaletteEffects.None && CycleEnabled)
            {
                colors[1] = Decode(OriginalPaletteData.Cycle[frameCounter & 15]);
                Revision++;
            }
        }

        internal void TickDisplay(ActorContext actors)
        {
            if (Effects != PaletteEffects.Full) return;
            ReloadCountdown = unchecked((byte)(ReloadCountdown - 1));
            if (ReloadCountdown == 0) { Reload(); ReloadCountdown = 30; }
            ushort pointer = actors.PaletteSequenceOffset;
            if (pointer == 0) return; // Cancelling a request does not itself write black.
            int index = (pointer - 0x06c9) / 2;
            if (pointer < 0x06c9 || (pointer & 1) == 0 || index >= OriginalPaletteData.Sequences.Count)
                throw new NotSupportedException($"Unknown original palette sequence pointer {pointer:x4}.");
            ushort word = OriginalPaletteData.Sequences[index];
            actors.PaletteSequenceOffset = word == 0 ? (ushort)0 : (ushort)(pointer + 2);
            colors[0] = Decode(word);
            Revision++;
        }
    }
}
