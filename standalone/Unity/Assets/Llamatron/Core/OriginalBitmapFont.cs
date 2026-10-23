using System;

namespace Llamatron.Core
{
    // F.CMP loaded at DS:39ee. CS:54f6 uses the table at +2, advances by
    // glyph word +2; CS:559f draws word +4 plus one rows starting at +6.
    public sealed class OriginalBitmapFont
    {
        private readonly byte[] data;
        public OriginalBitmapFont(byte[] bytes)
        {
            data = (byte[])bytes.Clone();
            if (data.Length < 258) throw new ArgumentException("Truncated original font.");
            for (int c = 32; c < 127; c++)
            {
                int p = Word(2 + c * 2);
                if (p == 0) continue;
                if (p + 6 > data.Length || Word(p) != 4 || Word(p + 2) > 16 ||
                    Word(p + 4) > 11)
                    throw new ArgumentException($"Invalid original glyph {c}.");
            }
        }
        private int Word(int p) => data[p] | (data[p + 1] << 8);
        private int Offset(char c)
        {
            int p = c < 128 ? Word(2 + c * 2) : 0;
            // The original file ends partway through the shared ]/^/_ bitmap.
            // Use a complete fallback instead of reading adjacent score memory.
            return p != 0 && p + 6 + (Word(p + 4) + 1) * 2 > data.Length ? Word(2 + '?' * 2) : p;
        }
        public int Advance(char c) => Offset(c) == 0 ? 5 : Word(Offset(c) + 2);
        public bool Pixel(char c, int x, int y)
        {
            int p = Offset(c);
            return p != 0 && x >= 0 && x < 16 && y >= 0 && y <= Word(p + 4) &&
                (data[p + 6 + y * 2 + x / 8] & (128 >> (x & 7))) != 0;
        }
        public int Width(string text)
        {
            int width = 0;
            foreach (char c in text) width += Advance(c);
            return width;
        }
    }
}
