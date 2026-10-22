using System;

namespace Llamatron.Core
{
    // Layout recovered from GAME86 CS:4307, 432b, 4374 and 4f93.
    // Keep signed 16-bit storage: timers, inactive sentinels and arithmetic wrap.
    // Unknown words remain available without assigning speculative meanings.
    public sealed class Entity
    {
        public const int ByteSize = 64;
        private readonly short[] words = new short[ByteSize / 2];

        public short this[int byteOffset]
        {
            get => words[WordIndex(byteOffset)];
            set => words[WordIndex(byteOffset)] = value;
        }

        public short State { get => this[0]; set => this[0] = value; }
        public short X { get => this[2]; set => this[2] = value; }
        public short Y { get => this[4]; set => this[4] = value; }
        public short RenderState { get => this[6]; set => this[6] = value; }
        public short VelocityX { get => this[8]; set => this[8] = value; }
        public short VelocityY { get => this[10]; set => this[10] = value; }
        public bool Active => State >= 0;
        public byte Behavior => unchecked((byte)State);

        public void Read(ReadOnlySpan<byte> bytes)
        {
            if (bytes.Length != ByteSize) throw new ArgumentException("Expected one 64-byte entity.");
            for (int i = 0; i < words.Length; i++)
                words[i] = unchecked((short)(bytes[i * 2] | bytes[i * 2 + 1] << 8));
        }

        public void Write(Span<byte> bytes)
        {
            if (bytes.Length != ByteSize) throw new ArgumentException("Expected one 64-byte entity.");
            for (int i = 0; i < words.Length; i++)
            {
                bytes[i * 2] = unchecked((byte)words[i]);
                bytes[i * 2 + 1] = unchecked((byte)(words[i] >> 8));
            }
        }

        private static int WordIndex(int offset)
        {
            if ((uint)offset >= ByteSize || (offset & 1) != 0)
                throw new ArgumentOutOfRangeException(nameof(offset));
            return offset / 2;
        }
    }
}
