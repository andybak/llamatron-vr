using System;
using System.Text;

namespace Llamatron.Core
{
    // Original CS:5da2: ten records, eight ASCII score digits then eight name
    // characters. Equal scores follow existing entries rather than displacing them.
    public sealed class HighScoreTable
    {
        public const int Count = 10;
        private readonly uint[] scores = new uint[Count];
        private readonly string[] names = new string[Count];
        public uint Score(int rank) => scores[rank];
        public string Name(int rank) => names[rank];
        public HighScoreTable(byte[] bytes)
        {
            if (bytes.Length != 160) throw new ArgumentException("High scores must contain ten 16-byte records.");
            for (int row = 0; row < Count; row++)
            {
                uint score = 0;
                for (int c = 0; c < 8; c++)
                {
                    byte digit = bytes[row * 16 + c];
                    if (digit < '0' || digit > '9') throw new ArgumentException("Invalid high-score digit.");
                    score = score * 10 + (uint)(digit - '0');
                }
                scores[row] = score;
                names[row] = CleanName(Encoding.ASCII.GetString(bytes, row * 16 + 8, 8));
                if (row != 0 && scores[row] > scores[row - 1]) throw new ArgumentException("High scores are not sorted.");
            }
        }
        public int Rank(uint score)
        {
            for (int i = 0; i < Count; i++) if (score > scores[i]) return i;
            return -1;
        }
        public int Insert(uint score, string name)
        {
            if (score > 99999999) throw new ArgumentOutOfRangeException(nameof(score));
            int rank = Rank(score);
            if (rank < 0) return -1;
            for (int i = Count - 1; i > rank; i--) { scores[i] = scores[i - 1]; names[i] = names[i - 1]; }
            scores[rank] = score; names[rank] = CleanName(name); return rank;
        }
        public byte[] Save()
        {
            var bytes = new byte[160];
            for (int i = 0; i < Count; i++)
                Encoding.ASCII.GetBytes($"{scores[i]:D8}{names[i].PadRight(8)}").CopyTo(bytes, i * 16);
            return bytes;
        }
        private static string CleanName(string name)
        {
            var clean = new StringBuilder(8);
            foreach (char c in name)
            {
                if (clean.Length == 8) break;
                clean.Append(c >= 32 && c <= 126 ? c : '?');
            }
            return clean.ToString().TrimEnd();
        }
    }
}
